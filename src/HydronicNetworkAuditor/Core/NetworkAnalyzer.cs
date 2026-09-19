using System;
using System.Collections.Generic;
using System.Linq;

namespace HydronicNetworkAuditor.Core
{
    public sealed class NetworkAnalyzer
    {
        private const double InterfaceGapSearchMm = 250.0;

        public void Analyze(AuditResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            result.Components.Clear();
            result.DirectHtLtBoundaries.Clear();
            result.InterfaceGapCandidates.Clear();

            var adjacency = result.Nodes.ToDictionary(
                n => n.Id,
                n => new HashSet<long>());

            foreach (AuditEdge edge in result.Edges)
            {
                if (adjacency.ContainsKey(edge.A)) adjacency[edge.A].Add(edge.B);
                if (adjacency.ContainsKey(edge.B)) adjacency[edge.B].Add(edge.A);
            }

            var nodeById = result.Nodes.ToDictionary(n => n.Id);
            var unvisited = new HashSet<long>(nodeById.Keys);
            int index = 1;

            while (unvisited.Count > 0)
            {
                long seed = unvisited.First();
                var stack = new Stack<long>();
                var ids = new List<long>();
                stack.Push(seed);
                unvisited.Remove(seed);

                while (stack.Count > 0)
                {
                    long current = stack.Pop();
                    ids.Add(current);

                    foreach (long next in adjacency[current])
                    {
                        if (unvisited.Remove(next))
                            stack.Push(next);
                    }
                }

                var idSet = new HashSet<long>(ids);
                int physicalEdgeCount = result.Edges.Count(
                    e => idSet.Contains(e.A) && idSet.Contains(e.B));

                var component = new ConnectedComponentSummary
                {
                    Index = index,
                    NodeCount = ids.Count,
                    EdgeCount = physicalEdgeCount,
                    HtNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.HT),
                    LtNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.LT),
                    MixedNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.Mixed),
                    UnknownNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.Unknown)
                };

                foreach (long id in ids)
                {
                    component.NodeIds.Add(id);
                    nodeById[id].ComponentIndex = index;
                }

                component.NodeIds.Sort();
                result.Components.Add(component);
                index++;
            }

            result.Components.Sort((x, y) => y.NodeCount.CompareTo(x.NodeCount));

            foreach (AuditEdge edge in result.Edges.Where(e => e.IsDirectHtLtBoundary))
                result.DirectHtLtBoundaries.Add(edge);

            FindInterfaceGapCandidates(result, nodeById, InterfaceGapSearchMm);
        }

        private static void FindInterfaceGapCandidates(
            AuditResult result,
            IDictionary<long, AuditNode> nodeById,
            double maxDistanceMm)
        {
            var ltBuckets = new Dictionary<Tuple<int, int, int>, List<AuditConnector>>();
            var htOpen = new List<AuditConnector>();

            foreach (AuditConnector connector in result.Connectors)
            {
                AuditNode owner;
                if (!nodeById.TryGetValue(connector.OwnerElementId, out owner))
                    continue;

                if (!IsOpenHydronicConnector(connector))
                    continue;

                if (owner.TemperatureNetwork != TemperatureNetwork.HT &&
                    owner.TemperatureNetwork != TemperatureNetwork.LT)
                    continue;

                if (owner.FlowSide != FlowSide.Supply &&
                    owner.FlowSide != FlowSide.Return)
                    continue;

                if (!ConnectorMatchesFlowSide(connector, owner.FlowSide))
                    continue;

                if (owner.TemperatureNetwork == TemperatureNetwork.HT)
                {
                    htOpen.Add(connector);
                }
                else
                {
                    Tuple<int, int, int> key = GetBucket(connector, maxDistanceMm);
                    List<AuditConnector> bucket;
                    if (!ltBuckets.TryGetValue(key, out bucket))
                    {
                        bucket = new List<AuditConnector>();
                        ltBuckets[key] = bucket;
                    }

                    bucket.Add(connector);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (AuditConnector htConnector in htOpen)
            {
                AuditNode htNode = nodeById[htConnector.OwnerElementId];
                Tuple<int, int, int> center = GetBucket(htConnector, maxDistanceMm);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var key = Tuple.Create(
                                center.Item1 + dx,
                                center.Item2 + dy,
                                center.Item3 + dz);

                            List<AuditConnector> candidates;
                            if (!ltBuckets.TryGetValue(key, out candidates))
                                continue;

                            foreach (AuditConnector ltConnector in candidates)
                            {
                                AuditNode ltNode = nodeById[ltConnector.OwnerElementId];

                                if (htNode.ComponentIndex == ltNode.ComponentIndex)
                                    continue;

                                if (htNode.FlowSide != ltNode.FlowSide)
                                    continue;

                                if (!ConnectorMatchesFlowSide(ltConnector, ltNode.FlowSide))
                                    continue;

                                double distance = DistanceMm(htConnector, ltConnector);
                                if (distance > maxDistanceMm)
                                    continue;

                                string pairKey =
                                    htConnector.OwnerElementId + ":" + htConnector.ConnectorId + "|" +
                                    ltConnector.OwnerElementId + ":" + ltConnector.ConnectorId;

                                if (!seen.Add(pairKey))
                                    continue;

                                result.InterfaceGapCandidates.Add(new InterfaceGapCandidate
                                {
                                    AElementId = htConnector.OwnerElementId,
                                    AConnectorId = htConnector.ConnectorId,
                                    AComponentIndex = htNode.ComponentIndex,
                                    ANetwork = htNode.TemperatureNetwork,
                                    FlowSide = htNode.FlowSide,
                                    AMark = htNode.Mark,
                                    ASystemNames = htNode.SystemNames,

                                    BElementId = ltConnector.OwnerElementId,
                                    BConnectorId = ltConnector.ConnectorId,
                                    BComponentIndex = ltNode.ComponentIndex,
                                    BNetwork = ltNode.TemperatureNetwork,
                                    BMark = ltNode.Mark,
                                    BSystemNames = ltNode.SystemNames,

                                    DistanceMm = distance,
                                    AXmm = htConnector.OriginXmm,
                                    AYmm = htConnector.OriginYmm,
                                    AZmm = htConnector.OriginZmm,
                                    BXmm = ltConnector.OriginXmm,
                                    BYmm = ltConnector.OriginYmm,
                                    BZmm = ltConnector.OriginZmm
                                });
                            }
                        }
                    }
                }
            }

            result.InterfaceGapCandidates.Sort(
                (a, b) => a.DistanceMm.CompareTo(b.DistanceMm));
        }

        private static bool IsOpenHydronicConnector(AuditConnector connector)
        {
            if (connector == null || connector.IsConnected)
                return false;

            if (!string.Equals(connector.Domain, "DomainPiping", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(connector.PipeSystemType, "SupplyHydronic", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(connector.PipeSystemType, "ReturnHydronic", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ConnectorMatchesFlowSide(AuditConnector connector, FlowSide side)
        {
            if (side == FlowSide.Supply)
                return string.Equals(connector.PipeSystemType, "SupplyHydronic", StringComparison.OrdinalIgnoreCase);

            if (side == FlowSide.Return)
                return string.Equals(connector.PipeSystemType, "ReturnHydronic", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static Tuple<int, int, int> GetBucket(
            AuditConnector connector,
            double cellSizeMm)
        {
            return Tuple.Create(
                (int)Math.Floor(connector.OriginXmm / cellSizeMm),
                (int)Math.Floor(connector.OriginYmm / cellSizeMm),
                (int)Math.Floor(connector.OriginZmm / cellSizeMm));
        }

        private static double DistanceMm(AuditConnector a, AuditConnector b)
        {
            double dx = a.OriginXmm - b.OriginXmm;
            double dy = a.OriginYmm - b.OriginYmm;
            double dz = a.OriginZmm - b.OriginZmm;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
