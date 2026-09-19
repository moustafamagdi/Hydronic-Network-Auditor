using System;
using System.Collections.Generic;
using System.Linq;

namespace HydronicNetworkAuditor.Core
{
    public sealed class NetworkAnalyzer
    {
        public void Analyze(AuditResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            result.Components.Clear();
            result.DirectHtLtBoundaries.Clear();

            foreach (AuditEdge edge in result.Edges.Where(e => e.IsDirectHtLtBoundary))
                result.DirectHtLtBoundaries.Add(edge);

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
                    Index = index++,
                    NodeCount = ids.Count,
                    EdgeCount = physicalEdgeCount,
                    HtNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.HT),
                    LtNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.LT),
                    MixedNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.Mixed),
                    UnknownNodeCount = ids.Count(id => nodeById[id].TemperatureNetwork == TemperatureNetwork.Unknown)
                };

                component.NodeIds.AddRange(ids.OrderBy(id => id));
                result.Components.Add(component);
            }

            result.Components.Sort((x, y) => y.NodeCount.CompareTo(x.NodeCount));
        }
    }
}
