using System;
using System.Collections.Generic;
using System.Linq;

namespace HydronicNetworkAuditor.Core
{
    public sealed class DiagnosticAnalyzer
    {
        private const int PropagationSearchDepth = 4;

        public void Analyze(AuditResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            result.Diagnostics.Issues.Clear();
            result.Diagnostics.FamilyRankings.Clear();
            result.Diagnostics.PropagationClusters.Clear();

            var nodeById = result.Nodes.ToDictionary(n => n.Id);
            var adjacency = result.Nodes.ToDictionary(
                n => n.Id,
                n => new HashSet<long>(n.ConnectedElementIds));

            BuildIssues(result);
            BuildFamilyRanking(result, nodeById, adjacency);
            BuildPropagationClusters(result, nodeById, adjacency);
        }

        private static void BuildIssues(AuditResult result)
        {
            foreach (AuditNode node in result.Nodes)
            {
                if (node.FlowClassificationConflict)
                {
                    result.Diagnostics.Issues.Add(CreateNodeIssue(
                        node,
                        "Flow Classification Conflict",
                        "Declared flow side does not match connector PipeSystemType."));
                }

                if (HasSystemTypeFlowMismatch(node))
                {
                    result.Diagnostics.Issues.Add(CreateNodeIssue(
                        node,
                        "System Type vs Connector",
                        "System Type and connector/classification flow intent are inconsistent."));
                }

                if (IsProbableFamilyConnectorProblem(node))
                {
                    result.Diagnostics.Issues.Add(CreateNodeIssue(
                        node,
                        "Probable Family Connector Misclassification",
                        "Non-pipe CHWR family instance is exposing Supply or Mixed hydronic connector behavior."));
                }

                if (node.OpenEndConnectorCount > 0)
                {
                    result.Diagnostics.Issues.Add(CreateNodeIssue(
                        node,
                        "Hydronic Open End",
                        node.OpenEndConnectorCount + " hydronic connector(s) are open."));
                }
            }

            foreach (AuditEdge edge in result.DirectHtLtBoundaries)
            {
                long a = Math.Min(edge.A, edge.B);
                long b = Math.Max(edge.A, edge.B);

                result.Diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Key = "BOUNDARY|" + a + "|" + b,
                    IssueType = "Direct HT/LT Boundary",
                    ElementId = edge.A,
                    RelatedElementId = edge.B,
                    Network = edge.ANetwork,
                    FlowSide = edge.AFlowSide,
                    Notes = "Physically connected edge crosses HT/LT classification."
                });
            }

            foreach (InterfaceGapCandidate gap in result.InterfaceGapCandidates)
            {
                result.Diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Key = "GAP|" + gap.AElementId + ":" + gap.AConnectorId + "|" +
                          gap.BElementId + ":" + gap.BConnectorId,
                    IssueType = "HT/LT Interface Gap",
                    Scope = InferScope(gap.AMark + " " + gap.BMark),
                    ElementId = gap.AElementId,
                    RelatedElementId = gap.BElementId,
                    ComponentIndex = gap.AComponentIndex,
                    Network = gap.ANetwork,
                    FlowSide = gap.FlowSide,
                    Mark = gap.AMark + " <-> " + gap.BMark,
                    Notes = gap.DistanceMm.ToString("0.###") + " mm separated HT/LT connector candidate."
                });
            }
        }

        private static DiagnosticIssue CreateNodeIssue(
            AuditNode node,
            string issueType,
            string notes)
        {
            return new DiagnosticIssue
            {
                Key = NormalizeIssueKey(issueType) + "|" + node.Id,
                IssueType = issueType,
                Scope = InferScope(node.Mark),
                ElementId = node.Id,
                ComponentIndex = node.ComponentIndex,
                Network = node.TemperatureNetwork,
                FlowSide = node.FlowSide,
                Family = node.Family,
                Mark = node.Mark,
                DeclaredSystemType = node.DeclaredSystemType,
                DeclaredSystemClassification = node.DeclaredSystemClassification,
                ConnectorFlowSide = node.ConnectorFlowSide,
                Notes = notes
            };
        }

        private static void BuildFamilyRanking(
            AuditResult result,
            IDictionary<long, AuditNode> nodeById,
            IDictionary<long, HashSet<long>> adjacency)
        {
            var familyNodes = result.Nodes
                .Where(n => !IsPipe(n))
                .Where(n => !string.IsNullOrWhiteSpace(n.Family))
                .Where(IsChwr)
                .GroupBy(n => n.Family, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var affectedPipesByFamily =
                new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);

            foreach (AuditNode pipe in result.Nodes.Where(n => IsPipe(n) && n.FlowClassificationConflict))
            {
                foreach (AuditNode source in FindNearbyProblemFamilyNodes(
                    pipe.Id,
                    nodeById,
                    adjacency,
                    PropagationSearchDepth))
                {
                    HashSet<long> affected;
                    if (!affectedPipesByFamily.TryGetValue(source.Family, out affected))
                    {
                        affected = new HashSet<long>();
                        affectedPipesByFamily[source.Family] = affected;
                    }

                    affected.Add(pipe.Id);
                }
            }

            foreach (var group in familyNodes)
            {
                List<AuditNode> nodes = group.ToList();
                int mixed = nodes.Count(IsMixedFlow);
                int wrongSupply = nodes.Count(n => !IsMixedFlow(n) && IsWrongSupplyOnChwr(n));
                int correctReturn = nodes.Count(IsCorrectReturnOnChwr);
                int problemCount = wrongSupply + mixed;

                HashSet<long> affected;
                int affectedCount = affectedPipesByFamily.TryGetValue(group.Key, out affected)
                    ? affected.Count
                    : 0;

                double errorRate = nodes.Count == 0
                    ? 0.0
                    : 100.0 * problemCount / nodes.Count;

                double score = errorRate + Math.Min(100.0, affectedCount * 2.0);

                var row = new FamilyDiagnostic
                {
                    Family = group.Key,
                    Category = MostCommon(nodes.Select(n => n.Category)),
                    ChwrInstanceCount = nodes.Count,
                    CorrectReturnCount = correctReturn,
                    WrongSupplyCount = wrongSupply,
                    MixedCount = mixed,
                    AffectedConflictPipeCount = affectedCount,
                    ErrorRatePercent = errorRate,
                    RootCauseScore = score,
                    IsProbableRootCause =
                        nodes.Count >= 2 &&
                        problemCount > 0 &&
                        (errorRate >= 50.0 || affectedCount >= 3)
                };

                foreach (long id in nodes
                    .Where(n => IsWrongSupplyOnChwr(n) || IsMixedFlow(n))
                    .Select(n => n.Id)
                    .OrderBy(id => id))
                {
                    row.ProblemInstanceIds.Add(id);
                }

                result.Diagnostics.FamilyRankings.Add(row);
            }

            result.Diagnostics.FamilyRankings.Sort((a, b) =>
            {
                int rootCompare = b.IsProbableRootCause.CompareTo(a.IsProbableRootCause);
                if (rootCompare != 0) return rootCompare;

                int scoreCompare = b.RootCauseScore.CompareTo(a.RootCauseScore);
                if (scoreCompare != 0) return scoreCompare;

                return string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void BuildPropagationClusters(
            AuditResult result,
            IDictionary<long, AuditNode> nodeById,
            IDictionary<long, HashSet<long>> adjacency)
        {
            var groups = new Dictionary<string, PropagationCluster>(
                StringComparer.OrdinalIgnoreCase);

            foreach (AuditNode pipe in result.Nodes.Where(n => IsPipe(n) && n.FlowClassificationConflict))
            {
                string scope = FindNearestScope(pipe.Id, nodeById, adjacency, PropagationSearchDepth);
                string key =
                    pipe.TemperatureNetwork + "|" +
                    pipe.ComponentIndex + "|" +
                    scope;

                PropagationCluster cluster;
                if (!groups.TryGetValue(key, out cluster))
                {
                    cluster = new PropagationCluster
                    {
                        Key = key,
                        Scope = scope,
                        ComponentIndex = pipe.ComponentIndex,
                        Network = pipe.TemperatureNetwork
                    };
                    groups[key] = cluster;
                }

                cluster.ConflictPipeIds.Add(pipe.Id);

                foreach (AuditNode source in FindNearbyProblemFamilyNodes(
                    pipe.Id,
                    nodeById,
                    adjacency,
                    PropagationSearchDepth))
                {
                    if (!cluster.SuspectFamilies.Any(
                        f => string.Equals(f, source.Family, StringComparison.OrdinalIgnoreCase)))
                    {
                        cluster.SuspectFamilies.Add(source.Family);
                    }
                }
            }

            foreach (PropagationCluster cluster in groups.Values)
            {
                cluster.ConflictPipeIds.Sort();
                cluster.SuspectFamilies.Sort(StringComparer.OrdinalIgnoreCase);
                cluster.ConflictPipeCount = cluster.ConflictPipeIds.Count;
                result.Diagnostics.PropagationClusters.Add(cluster);
            }

            result.Diagnostics.PropagationClusters.Sort((a, b) =>
            {
                int countCompare = b.ConflictPipeCount.CompareTo(a.ConflictPipeCount);
                if (countCompare != 0) return countCompare;
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static IEnumerable<AuditNode> FindNearbyProblemFamilyNodes(
            long startId,
            IDictionary<long, AuditNode> nodeById,
            IDictionary<long, HashSet<long>> adjacency,
            int maxDepth)
        {
            var found = new Dictionary<long, AuditNode>();
            var visited = new HashSet<long> { startId };
            var queue = new Queue<Tuple<long, int>>();
            queue.Enqueue(Tuple.Create(startId, 0));

            while (queue.Count > 0)
            {
                Tuple<long, int> current = queue.Dequeue();
                if (current.Item2 >= maxDepth) continue;

                HashSet<long> neighbours;
                if (!adjacency.TryGetValue(current.Item1, out neighbours))
                    continue;

                foreach (long nextId in neighbours)
                {
                    if (!visited.Add(nextId))
                        continue;

                    AuditNode next;
                    if (!nodeById.TryGetValue(nextId, out next))
                        continue;

                    if (IsProbableFamilyConnectorProblem(next))
                        found[next.Id] = next;

                    queue.Enqueue(Tuple.Create(nextId, current.Item2 + 1));
                }
            }

            return found.Values;
        }

        private static string FindNearestScope(
            long startId,
            IDictionary<long, AuditNode> nodeById,
            IDictionary<long, HashSet<long>> adjacency,
            int maxDepth)
        {
            var visited = new HashSet<long> { startId };
            var queue = new Queue<Tuple<long, int>>();
            queue.Enqueue(Tuple.Create(startId, 0));

            while (queue.Count > 0)
            {
                Tuple<long, int> current = queue.Dequeue();

                AuditNode node;
                if (nodeById.TryGetValue(current.Item1, out node))
                {
                    string scope = InferScope(node.Mark);
                    if (!string.IsNullOrWhiteSpace(scope))
                        return scope;
                }

                if (current.Item2 >= maxDepth) continue;

                HashSet<long> neighbours;
                if (!adjacency.TryGetValue(current.Item1, out neighbours))
                    continue;

                foreach (long nextId in neighbours)
                {
                    if (visited.Add(nextId))
                        queue.Enqueue(Tuple.Create(nextId, current.Item2 + 1));
                }
            }

            AuditNode start;
            return nodeById.TryGetValue(startId, out start)
                ? "Component " + start.ComponentIndex
                : "Unknown";
        }

        private static bool HasSystemTypeFlowMismatch(AuditNode node)
        {
            if (node == null) return false;

            if (IsChwr(node))
            {
                return Contains(node.DeclaredSystemClassification, "Hydronic Supply") ||
                       node.ConnectorFlowSide == FlowSide.Supply ||
                       node.ConnectorFlowSide == FlowSide.Mixed;
            }

            if (Contains(node.DeclaredSystemType, "CHWS"))
            {
                return Contains(node.DeclaredSystemClassification, "Hydronic Return") ||
                       node.ConnectorFlowSide == FlowSide.Return ||
                       node.ConnectorFlowSide == FlowSide.Mixed;
            }

            return false;
        }

        private static bool IsProbableFamilyConnectorProblem(AuditNode node)
        {
            if (node == null || IsPipe(node) || string.IsNullOrWhiteSpace(node.Family))
                return false;

            return IsChwr(node) && (IsWrongSupplyOnChwr(node) || IsMixedFlow(node));
        }

        private static bool IsWrongSupplyOnChwr(AuditNode node)
        {
            return IsChwr(node) &&
                   (Contains(node.DeclaredSystemClassification, "Hydronic Supply") ||
                    node.ConnectorFlowSide == FlowSide.Supply ||
                    node.FlowSide == FlowSide.Supply);
        }

        private static bool IsMixedFlow(AuditNode node)
        {
            return node != null &&
                   (node.ConnectorFlowSide == FlowSide.Mixed ||
                    node.FlowSide == FlowSide.Mixed ||
                    (Contains(node.DeclaredSystemClassification, "Hydronic Supply") &&
                     Contains(node.DeclaredSystemClassification, "Hydronic Return")));
        }

        private static bool IsCorrectReturnOnChwr(AuditNode node)
        {
            return IsChwr(node) &&
                   Contains(node.DeclaredSystemClassification, "Hydronic Return") &&
                   node.ConnectorFlowSide == FlowSide.Return &&
                   node.FlowSide == FlowSide.Return;
        }

        private static bool IsChwr(AuditNode node)
        {
            return node != null && Contains(node.DeclaredSystemType, "CHWR");
        }

        private static bool IsPipe(AuditNode node)
        {
            return node != null &&
                   string.Equals(node.Category, "Pipes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string InferScope(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            for (int dh = 1; dh <= 9; dh++)
            {
                string token = "DH" + dh;
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return token;
            }

            return string.Empty;
        }

        private static string MostCommon(IEnumerable<string> values)
        {
            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string NormalizeIssueKey(string value)
        {
            return (value ?? string.Empty)
                .Replace(" ", "_")
                .Replace("/", "_")
                .ToUpperInvariant();
        }
    }
}
