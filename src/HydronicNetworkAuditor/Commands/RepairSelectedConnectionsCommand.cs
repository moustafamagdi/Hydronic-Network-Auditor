using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HydronicNetworkAuditor.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class RepairSelectedConnectionsCommand : IExternalCommand
    {
        private const double ReconnectToleranceFeet = 1.0 / 304.8; // 1 mm
        private const double LookupFallbackToleranceFeet = 5.0 / 304.8; // 5 mm

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            Document document = uiDocument.Document;
            ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();

            if (selectedIds == null || selectedIds.Count == 0)
            {
                TaskDialog.Show(
                    "Hydronic Connection Repair",
                    "Select one or more connected MEP family instances, then run Repair Selected Connections.");
                return Result.Cancelled;
            }

            var log = new List<string>
            {
                "HYDRONIC CONNECTION REPAIR",
                "==========================",
                "Model: " + document.Title,
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "Selected IDs: " + string.Join(", ", selectedIds.Select(id => id.Value))
            };

            try
            {
                List<Element> selectedElements = selectedIds
                    .Select(document.GetElement)
                    .Where(e => e != null)
                    .Where(e => e is FamilyInstance)
                    .Where(e => TryGetConnectorManager(e) != null)
                    .ToList();

                if (selectedElements.Count == 0)
                {
                    TaskDialog.Show(
                        "Hydronic Connection Repair",
                        "The current selection does not contain a supported connected MEP family instance.");
                    return Result.Cancelled;
                }

                List<ConnectionDescriptor> connections =
                    CaptureExternalPipingConnections(selectedElements);

                if (connections.Count == 0)
                {
                    TaskDialog.Show(
                        "Hydronic Connection Repair",
                        "No physical piping connections were found on the selected instance(s).");
                    return Result.Cancelled;
                }

                log.Add("Supported selected instances: " + selectedElements.Count);
                log.Add("Captured physical piping pairs: " + connections.Count);

                foreach (ConnectionDescriptor pair in connections)
                    log.Add("PAIR " + pair.Describe());

                using (var group = new TransactionGroup(
                    document,
                    "Repair Selected Hydronic Connections"))
                {
                    if (group.Start() != TransactionStatus.Started)
                        throw new InvalidOperationException("Could not start repair transaction group.");

                    try
                    {
                        using (var disconnectTransaction = new Transaction(
                            document,
                            "Disconnect Selected Hydronic Connections"))
                        {
                            if (disconnectTransaction.Start() != TransactionStatus.Started)
                                throw new InvalidOperationException("Could not start disconnect transaction.");

                            foreach (ConnectionDescriptor pair in connections)
                            {
                                Connector a = ResolveConnector(document, pair.A);
                                Connector b = ResolveConnector(document, pair.B);

                                if (a == null || b == null)
                                    throw new InvalidOperationException(
                                        "Could not resolve original connector pair before disconnect: " +
                                        pair.Describe());

                                if (!AreConnected(a, b))
                                    throw new InvalidOperationException(
                                        "Original connector pair is no longer connected: " +
                                        pair.Describe());

                                a.DisconnectFrom(b);
                            }

                            document.Regenerate();

                            if (disconnectTransaction.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Disconnect transaction did not commit.");
                        }

                        using (var reconnectTransaction = new Transaction(
                            document,
                            "Reconnect Selected Hydronic Connections"))
                        {
                            if (reconnectTransaction.Start() != TransactionStatus.Started)
                                throw new InvalidOperationException("Could not start reconnect transaction.");

                            foreach (ConnectionDescriptor pair in connections)
                            {
                                Connector a = ResolveConnector(document, pair.A);
                                Connector b = ResolveConnector(document, pair.B);

                                if (a == null || b == null)
                                    throw new InvalidOperationException(
                                        "A connector disappeared after disconnect. Repair was cancelled safely: " +
                                        pair.Describe());

                                double distance = a.Origin.DistanceTo(b.Origin);
                                if (distance > ReconnectToleranceFeet)
                                {
                                    throw new InvalidOperationException(
                                        "Connector endpoints moved more than 1 mm after disconnect. " +
                                        "Repair was cancelled safely: " + pair.Describe());
                                }

                                if (!AreConnected(a, b))
                                    a.ConnectTo(b);

                                document.Regenerate();

                                if (!AreConnected(a, b))
                                {
                                    throw new InvalidOperationException(
                                        "Revit did not restore the original connection: " +
                                        pair.Describe());
                                }
                            }

                            if (reconnectTransaction.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Reconnect transaction did not commit.");
                        }

                        foreach (ConnectionDescriptor pair in connections)
                        {
                            Connector a = ResolveConnector(document, pair.A);
                            Connector b = ResolveConnector(document, pair.B);

                            if (a == null || b == null || !AreConnected(a, b))
                            {
                                throw new InvalidOperationException(
                                    "Post-repair verification failed: " + pair.Describe());
                            }
                        }

                        if (group.Assimilate() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Repair transaction group did not assimilate.");

                        log.Add("RESULT: SUCCESS");
                        log.Add("Repaired selected instances: " + selectedElements.Count);
                        log.Add("Restored physical piping pairs: " + connections.Count);
                    }
                    catch
                    {
                        try
                        {
                            if (group.GetStatus() == TransactionStatus.Started)
                                group.RollBack();
                        }
                        catch { }

                        throw;
                    }
                }

                TryWriteLog(log, "repair_success");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                log.Add("RESULT: FAILED - ALL CHANGES ROLLED BACK");
                log.Add(ex.ToString());
                string path = TryWriteLog(log, "repair_failed");

                message = ex.Message +
                          (string.IsNullOrWhiteSpace(path)
                              ? string.Empty
                              : Environment.NewLine + "Log: " + path);

                return Result.Failed;
            }
        }

        private static List<ConnectionDescriptor> CaptureExternalPipingConnections(
            IEnumerable<Element> selectedElements)
        {
            var selectedIds = new HashSet<long>(selectedElements.Select(e => e.Id.Value));
            var pairs = new List<ConnectionDescriptor>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Element element in selectedElements)
            {
                ConnectorManager manager = TryGetConnectorManager(element);
                if (manager == null) continue;

                foreach (Connector connector in manager.Connectors)
                {
                    if (!IsPhysicalPipingEnd(connector))
                        continue;

                    ConnectorSet references;
                    try { references = connector.AllRefs; }
                    catch { continue; }

                    foreach (Connector reference in references)
                    {
                        Element otherOwner;
                        try { otherOwner = reference.Owner; }
                        catch { continue; }

                        if (otherOwner == null || otherOwner.Id == element.Id)
                            continue;

                        if (reference.Domain != Domain.DomainPiping)
                            continue;

                        if (!AreConnected(connector, reference))
                            continue;

                        ConnectorManager otherManager = TryGetConnectorManager(otherOwner);
                        if (otherManager == null)
                            continue;

                        ConnectorDescriptor a = ConnectorDescriptor.From(connector);
                        ConnectorDescriptor b = ConnectorDescriptor.From(reference);

                        string key = MakePairKey(a, b);
                        if (!seen.Add(key))
                            continue;

                        pairs.Add(new ConnectionDescriptor
                        {
                            A = a,
                            B = b
                        });
                    }
                }
            }

            return pairs;
        }

        private static bool IsPhysicalPipingEnd(Connector connector)
        {
            if (connector == null)
                return false;

            try
            {
                return connector.Domain == Domain.DomainPiping &&
                       connector.ConnectorType == ConnectorType.End;
            }
            catch
            {
                return false;
            }
        }

        private static Connector ResolveConnector(
            Document document,
            ConnectorDescriptor descriptor)
        {
            Element owner = document.GetElement(new ElementId(descriptor.OwnerElementId));
            ConnectorManager manager = TryGetConnectorManager(owner);
            if (manager == null)
                return null;

            try
            {
                Connector exact = manager.Lookup(descriptor.ConnectorId);
                if (MatchesDescriptor(exact, descriptor, LookupFallbackToleranceFeet))
                    return exact;
            }
            catch { }

            Connector nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (Connector connector in manager.Connectors)
            {
                if (connector == null)
                    continue;

                try
                {
                    if (connector.Domain != descriptor.Domain)
                        continue;

                    XYZ origin = connector.Origin;
                    double distance = origin.DistanceTo(descriptor.Origin);

                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = connector;
                    }
                }
                catch { }
            }

            return nearest != null && nearestDistance <= LookupFallbackToleranceFeet
                ? nearest
                : null;
        }

        private static bool MatchesDescriptor(
            Connector connector,
            ConnectorDescriptor descriptor,
            double toleranceFeet)
        {
            if (connector == null)
                return false;

            try
            {
                return connector.Domain == descriptor.Domain &&
                       connector.Origin.DistanceTo(descriptor.Origin) <= toleranceFeet;
            }
            catch
            {
                return false;
            }
        }

        private static bool AreConnected(Connector a, Connector b)
        {
            if (a == null || b == null)
                return false;

            try { return a.IsConnectedTo(b); }
            catch { return false; }
        }

        private static string MakePairKey(
            ConnectorDescriptor a,
            ConnectorDescriptor b)
        {
            string left = a.OwnerElementId + ":" + a.ConnectorId;
            string right = b.OwnerElementId + ":" + b.ConnectorId;

            return string.CompareOrdinal(left, right) <= 0
                ? left + "|" + right
                : right + "|" + left;
        }

        private static ConnectorManager TryGetConnectorManager(Element element)
        {
            if (element == null)
                return null;

            try
            {
                var curve = element as MEPCurve;
                if (curve != null)
                    return curve.ConnectorManager;

                var instance = element as FamilyInstance;
                if (instance != null && instance.MEPModel != null)
                    return instance.MEPModel.ConnectorManager;
            }
            catch { }

            return null;
        }

        private static string TryWriteLog(
            IEnumerable<string> lines,
            string prefix)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string root = Path.Combine(desktop, "HydronicNetworkAuditor");
                Directory.CreateDirectory(root);

                string path = Path.Combine(
                    root,
                    prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");

                File.WriteAllLines(path, lines);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private sealed class ConnectionDescriptor
        {
            public ConnectorDescriptor A { get; set; }
            public ConnectorDescriptor B { get; set; }

            public string Describe()
            {
                return A.OwnerElementId + ":" + A.ConnectorId +
                       " <-> " +
                       B.OwnerElementId + ":" + B.ConnectorId;
            }
        }

        private sealed class ConnectorDescriptor
        {
            public long OwnerElementId { get; set; }
            public int ConnectorId { get; set; }
            public Domain Domain { get; set; }
            public XYZ Origin { get; set; }

            public static ConnectorDescriptor From(Connector connector)
            {
                return new ConnectorDescriptor
                {
                    OwnerElementId = connector.Owner.Id.Value,
                    ConnectorId = SafeConnectorId(connector),
                    Domain = connector.Domain,
                    Origin = new XYZ(
                        connector.Origin.X,
                        connector.Origin.Y,
                        connector.Origin.Z)
                };
            }

            private static int SafeConnectorId(Connector connector)
            {
                try { return connector.Id; }
                catch { return -1; }
            }
        }
    }
}
