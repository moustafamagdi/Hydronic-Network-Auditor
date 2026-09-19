using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace HydronicNetworkAuditor.Core
{
    public sealed class ConnectorGraphBuilder
    {
        private static readonly BuiltInCategory[] IncludedCategories =
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_MechanicalEquipment
        };

        private static readonly string[] EvidenceParameters =
        {
            "System Name",
            "System Type",
            "System Classification",
            "System Abbreviation",
            "Service",
            "Service Name",
            "Mark",
            "Comments",
            "Type Comments",
            "Description",
            "HNA_Network",
            "HNA_Flow_Side",
            "HNA_Connection_Type"
        };

        public AuditResult Build(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var result = new AuditResult
            {
                DocumentTitle = document.Title,
                DocumentPath = document.PathName,
                GeneratedAt = DateTime.Now
            };

            var filter = new ElementMulticategoryFilter(IncludedCategories);
            IList<Element> elements = new FilteredElementCollector(document)
                .WherePasses(filter)
                .WhereElementIsNotElementType()
                .ToElements();

            var nodes = new Dictionary<long, AuditNode>();

            foreach (Element element in elements)
            {
                ConnectorManager connectorManager = TryGetConnectorManager(element);
                if (connectorManager == null) continue;

                AuditNode node = BuildNode(document, element, connectorManager);
                nodes[node.Id] = node;
                result.Nodes.Add(node);
            }

            var edgeKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (Element element in elements)
            {
                long ownerId = element.Id.Value;
                if (!nodes.ContainsKey(ownerId)) continue;

                ConnectorManager connectorManager = TryGetConnectorManager(element);
                if (connectorManager == null) continue;

                foreach (Connector connector in connectorManager.Connectors)
                {
                    ConnectorSet references;
                    try { references = connector.AllRefs; }
                    catch { continue; }

                    foreach (Connector reference in references)
                    {
                        Element otherOwner;
                        try { otherOwner = reference.Owner; }
                        catch { continue; }

                        if (otherOwner == null) continue;

                        long otherId = otherOwner.Id.Value;
                        if (otherId == ownerId || !nodes.ContainsKey(otherId)) continue;

                        long min = Math.Min(ownerId, otherId);
                        long max = Math.Max(ownerId, otherId);
                        string key = min + "|" + max;

                        if (!edgeKeys.Add(key)) continue;

                        AuditNode a = nodes[min];
                        AuditNode b = nodes[max];

                        result.Edges.Add(new AuditEdge
                        {
                            A = min,
                            B = max,
                            ACategory = a.Category,
                            BCategory = b.Category,
                            ANetwork = a.TemperatureNetwork,
                            BNetwork = b.TemperatureNetwork,
                            AFlowSide = a.FlowSide,
                            BFlowSide = b.FlowSide
                        });

                        a.ConnectedElementIds.Add(max);
                        b.ConnectedElementIds.Add(min);
                    }
                }
            }

            foreach (AuditNode node in result.Nodes)
                node.ConnectedElementIds.Sort();

            result.OpenEndConnectorCount = result.Nodes.Sum(n => n.OpenEndConnectorCount);
            result.MixedClassificationNodeCount =
                result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.Mixed);

            return result;
        }

        private static AuditNode BuildNode(
            Document document,
            Element element,
            ConnectorManager connectorManager)
        {
            var systemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var systemTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int connectorCount = 0;
            int openEnds = 0;

            foreach (Connector connector in connectorManager.Connectors)
            {
                connectorCount++;

                try
                {
                    if (connector.ConnectorType == ConnectorType.End && !connector.IsConnected)
                        openEnds++;
                }
                catch
                {
                    // Some connector types do not expose all state consistently.
                }

                try
                {
                    if (connector.MEPSystem != null)
                    {
                        if (!string.IsNullOrWhiteSpace(connector.MEPSystem.Name))
                            systemNames.Add(connector.MEPSystem.Name);

                        var pipingSystem = connector.MEPSystem as PipingSystem;
                        if (pipingSystem != null)
                            systemTypes.Add(pipingSystem.SystemType.ToString());
                    }
                }
                catch
                {
                    // Keep auditing even if one connector/system is corrupt.
                }
            }

            string family = string.Empty;
            string typeName = string.Empty;

            var familyInstance = element as FamilyInstance;
            if (familyInstance?.Symbol != null)
            {
                family = familyInstance.Symbol.FamilyName ?? string.Empty;
                typeName = familyInstance.Symbol.Name ?? string.Empty;
            }
            else
            {
                Element type = document.GetElement(element.GetTypeId());
                typeName = type?.Name ?? string.Empty;
            }

            var evidence = new List<string>
            {
                element.Name ?? string.Empty,
                element.Category?.Name ?? string.Empty,
                family,
                typeName
            };

            evidence.AddRange(systemNames);
            evidence.AddRange(systemTypes);

            foreach (string parameterName in EvidenceParameters)
            {
                string value = TryReadParameter(element, parameterName);
                if (!string.IsNullOrWhiteSpace(value))
                    evidence.Add(parameterName + "=" + value);
            }

            string evidenceText = string.Join(" | ", evidence.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new AuditNode
            {
                Id = element.Id.Value,
                UniqueId = element.UniqueId,
                Category = element.Category?.Name ?? string.Empty,
                Name = element.Name ?? string.Empty,
                Family = family,
                Type = typeName,
                SystemNames = string.Join("; ", systemNames.OrderBy(s => s)),
                SystemTypes = string.Join("; ", systemTypes.OrderBy(s => s)),
                EvidenceText = evidenceText,
                TemperatureNetwork = NetworkClassifier.InferTemperatureNetwork(evidenceText),
                FlowSide = NetworkClassifier.InferFlowSide(evidenceText),
                ConnectorCount = connectorCount,
                OpenEndConnectorCount = openEnds
            };
        }

        private static string TryReadParameter(Element element, string name)
        {
            try
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null) return null;

                if (parameter.StorageType == StorageType.String)
                    return parameter.AsString();

                return parameter.AsValueString();
            }
            catch
            {
                return null;
            }
        }

        private static ConnectorManager TryGetConnectorManager(Element element)
        {
            try
            {
                var mepCurve = element as MEPCurve;
                if (mepCurve != null)
                    return mepCurve.ConnectorManager;

                var familyInstance = element as FamilyInstance;
                if (familyInstance?.MEPModel != null)
                    return familyInstance.MEPModel.ConnectorManager;
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}
