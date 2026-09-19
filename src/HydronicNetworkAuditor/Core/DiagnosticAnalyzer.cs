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
                    DiagnosticIssue familyIssue = CreateNodeIssue(
                        node,
                        "Family Connector Misclassification",
                        "Non-pipe CHWR family instance is exposing Supply or Mixed hydronic connector behavior.");

                    // Keep the historical key stable so the run-to-run delta is not reset by
                    // the wording improvement introduced in diagnostic QA v0.3.
                    familyIssue.Key = "PROBABLE_FAMILY_CONNECTOR_MISCLASSIFICATION|" + node.Id;
                    result.Diagnostics.Issues.Add(familyIssue);
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
                .Where(IsRootCauseCandidateCategory)
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

                // Family-wide confidence is driven primarily by prevalence inside the family.
                // Nearby affected pipes are supporting evidence only and must not promote an isolated
                // bad instance into a family-wide root cause.
                double score =
                    (errorRate * 0.8) +
                    Math.Min(20.0, affectedCount * 2.0);

                string confidence;
                bool isFamilyWideRootCause = false;

                if (problemCount == 0)
                {
                    confidence = "Healthy";
                }
                else if (nodes.Count >= 2 && errorRate >= 50.0)
                {
                    confidence = "Family-wide Root Cause";
                    isFamilyWideRootCause = true;
                }
                else if (affectedCount > 0)
                {
                    confidence = "Localized Suspect";
                }
                else
                {
                    confidence = "Likely Propagation Victim";
                }

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
                    ConfidenceClassification = confidence,
                    IsProbableRootCause = isFamilyWideRootCause
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
                int confidenceCompare =
                    ConfidenceRank(b.ConfidenceClassification)
                    .CompareTo(ConfidenceRank(a.ConfidenceClassification));
                if (confidenceCompare != 0) return confidenceCompare;

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
            // A connected component is the physical cluster. Do not split one component merely
            // because some pipes have a nearby DH mark and others do not.
            var groups = new Dictionary<string, PropagationCluster>(
                StringComparer.OrdinalIgnoreCase);
            var scopesByGroup = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (AuditNode pipe in result.Nodes.Where(n => IsPipe(n) && n.FlowClassificationConflict))
            {
                string key =
                    pipe.TemperatureNetwork + "|" +
                    pipe.ComponentIndex;

                PropagationCluster cluster;
                if (!groups.TryGetValue(key, out cluster))
                {
                    cluster = new PropagationCluster
                    {
                        Key = key,
                        Scope = string.Empty,
                        ComponentIndex = pipe.ComponentIndex,
                        Network = pipe.TemperatureNetwork
                    };
                    groups[key] = cluster;
                    scopesByGroup[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                cluster.ConflictPipeIds.Add(pipe.Id);

                string pipeScope = FindNearestScope(
                    pipe.Id,
                    nodeById,
                    adjacency,
                    PropagationSearchDepth);

                if (!string.IsNullOrWhiteSpace(pipeScope) &&
                    !pipeScope.StartsWith("Component ", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pipeScope, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    scopesByGroup[key].Add(pipeScope);
                }

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

                    if (!cluster.SuspectInstanceIds.Contains(source.Id))
                        cluster.SuspectInstanceIds.Add(source.Id);

                    string sourceScope = InferScope(source.Mark);
                    if (!string.IsNullOrWhiteSpace(sourceScope))
                        scopesByGroup[key].Add(sourceScope);
                }
            }

            foreach (KeyValuePair<string, PropagationCluster> pair in groups)
            {
                PropagationCluster cluster = pair.Value;
                HashSet<string> scopes = scopesByGroup[pair.Key];

                cluster.Scope = scopes.Count == 0
                    ? "Component " + cluster.ComponentIndex
                    : string.Join("/", scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

                cluster.ConflictPipeIds.Sort();
                cluster.SuspectFamilies.Sort(StringComparer.OrdinalIgnoreCase);
                cluster.SuspectInstanceIds.Sort();
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
            if (node == null ||
                !IsRootCauseCandidateCategory(node) ||
                string.IsNullOrWhiteSpace(node.Family))
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

        private static bool IsRootCauseCandidateCategory(AuditNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Category))
                return false;

            return string.Equals(node.Category, "Pipe Accessories", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(node.Category, "Mechanical Equipment", StringComparison.OrdinalIgnoreCase);
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

        private static int ConfidenceRank(string confidence)
        {
            if (string.Equals(confidence, "Family-wide Root Cause", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(confidence, "Localized Suspect", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(confidence, "Likely Propagation Victim", StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
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
