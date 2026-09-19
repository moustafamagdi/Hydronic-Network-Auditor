using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HydronicNetworkAuditor.Core;

namespace HydronicNetworkAuditor.Reporting
{
    public sealed class DiagnosticReportExporter
    {
        public void Export(AuditResult result, ExportBundle bundle)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));

            File.WriteAllText(
                Path.Combine(bundle.Folder, "diagnostics_summary.txt"),
                BuildSummary(result),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(bundle.Folder, "root_causes.csv"),
                BuildRootCausesCsv(result),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(bundle.Folder, "propagation_clusters.csv"),
                BuildClustersCsv(result),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(bundle.Folder, "diagnostic_issues.csv"),
                BuildIssuesCsv(result),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(bundle.Folder, "delta.txt"),
                BuildDelta(result),
                Encoding.UTF8);
        }

        private static string BuildSummary(AuditResult result)
        {
            var sb = new StringBuilder();
            DiagnosticResult d = result.Diagnostics;

            sb.AppendLine("HYDRONIC NETWORK DIAGNOSTICS");
            sb.AppendLine("============================");
            sb.AppendLine("Model: " + result.DocumentTitle);
            sb.AppendLine("Generated: " + result.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            sb.AppendLine("DIAGNOSTIC SUMMARY");
            sb.AppendLine("------------------");
            int openEndIssueNodes = d.Issues.Count(i =>
                string.Equals(i.IssueType, "Hydronic Open End", StringComparison.OrdinalIgnoreCase));
            int coreIssueCount = d.Issues.Count - openEndIssueNodes;

            sb.AppendLine("Core diagnostic issues: " + coreIssueCount);
            sb.AppendLine("Hydronic open-end nodes (separate QA): " + openEndIssueNodes);
            sb.AppendLine("Hydronic open-end connectors: " + result.OpenEndConnectorCount);
            sb.AppendLine("Flow classification conflicts: " + result.FlowClassificationConflictCount);
            sb.AppendLine("Family-wide root causes: " +
                          d.FamilyRankings.Count(f =>
                              string.Equals(
                                  f.ConfidenceClassification,
                                  "Family-wide Root Cause",
                                  StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine("Localized family suspects: " +
                          d.FamilyRankings.Count(f =>
                              string.Equals(
                                  f.ConfidenceClassification,
                                  "Localized Suspect",
                                  StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine("Propagation clusters: " + d.PropagationClusters.Count);
            sb.AppendLine("Direct HT/LT boundaries: " + result.DirectHtLtBoundaries.Count);
            sb.AppendLine("HT/LT gap candidates: " + result.InterfaceGapCandidates.Count);
            sb.AppendLine();

            sb.AppendLine("FAMILY DIAGNOSTIC CONFIDENCE");
            sb.AppendLine("----------------------------");
            foreach (FamilyDiagnostic family in d.FamilyRankings
                .Where(f => !string.Equals(
                    f.ConfidenceClassification,
                    "Healthy",
                    StringComparison.OrdinalIgnoreCase))
                .Take(15))
            {
                sb.AppendLine(
                    family.ConfidenceClassification +
                    " | " + family.Family +
                    " | CHWR=" + family.ChwrInstanceCount +
                    " | WrongSupply=" + family.WrongSupplyCount +
                    " | Mixed=" + family.MixedCount +
                    " | AffectedPipes=" + family.AffectedConflictPipeCount +
                    " | Error=" + family.ErrorRatePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%" +
                    " | EvidenceScore=" + family.RootCauseScore.ToString("0.0", CultureInfo.InvariantCulture) +
                    " | InstanceIDs=" + string.Join(";", family.ProblemInstanceIds));
            }

            sb.AppendLine();
            sb.AppendLine("TOP PROPAGATION CLUSTERS");
            sb.AppendLine("------------------------");
            foreach (PropagationCluster cluster in d.PropagationClusters.Take(20))
            {
                sb.AppendLine(
                    cluster.Scope +
                    " | " + cluster.Network +
                    " | Component " + cluster.ComponentIndex +
                    " | Conflict pipes=" + cluster.ConflictPipeCount +
                    " | Suspects=" +
                    (cluster.SuspectFamilies.Count == 0
                        ? "(none within search depth)"
                        : string.Join("; ", cluster.SuspectFamilies)) +
                    " | SuspectIDs=" +
                    (cluster.SuspectInstanceIds.Count == 0
                        ? "(none)"
                        : string.Join(";", cluster.SuspectInstanceIds)));
            }

            sb.AppendLine();
            sb.Append(BuildDelta(result));
            return sb.ToString();
        }

        private static string BuildRootCausesCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Family,Category,CHWR Instances,Correct Return,Wrong Supply,Mixed,Affected Conflict Pipes,Error Rate %,Evidence Score,Confidence Classification,Problem Instance IDs");

            foreach (FamilyDiagnostic f in result.Diagnostics.FamilyRankings)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(f.Family),
                    Csv(f.Category),
                    Csv(f.ChwrInstanceCount),
                    Csv(f.CorrectReturnCount),
                    Csv(f.WrongSupplyCount),
                    Csv(f.MixedCount),
                    Csv(f.AffectedConflictPipeCount),
                    Csv(f.ErrorRatePercent.ToString("0.0", CultureInfo.InvariantCulture)),
                    Csv(f.RootCauseScore.ToString("0.0", CultureInfo.InvariantCulture)),
                    Csv(f.ConfidenceClassification),
                    Csv(string.Join(";", f.ProblemInstanceIds))
                }));
            }

            return sb.ToString();
        }

        private static string BuildClustersCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Scope,Network,Component,Conflict Pipe Count,Suspect Families,Suspect Instance IDs,Conflict Pipe IDs");

            foreach (PropagationCluster c in result.Diagnostics.PropagationClusters)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(c.Scope),
                    Csv(c.Network),
                    Csv(c.ComponentIndex),
                    Csv(c.ConflictPipeCount),
                    Csv(string.Join(";", c.SuspectFamilies)),
                    Csv(string.Join(";", c.SuspectInstanceIds)),
                    Csv(string.Join(";", c.ConflictPipeIds))
                }));
            }

            return sb.ToString();
        }

        private static string BuildIssuesCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Issue Type,Scope,Element ID,Related Element ID,Component,Network,Flow Side,Family,Mark,System Type,System Classification,Connector Flow Side,Notes,Issue Key");

            foreach (DiagnosticIssue i in result.Diagnostics.Issues
                .OrderBy(i => i.IssueType)
                .ThenBy(i => i.Scope)
                .ThenBy(i => i.ElementId))
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(i.IssueType),
                    Csv(i.Scope),
                    Csv(i.ElementId),
                    Csv(i.RelatedElementId == 0 ? string.Empty : i.RelatedElementId.ToString()),
                    Csv(i.ComponentIndex),
                    Csv(i.Network),
                    Csv(i.FlowSide),
                    Csv(i.Family),
                    Csv(i.Mark),
                    Csv(i.DeclaredSystemType),
                    Csv(i.DeclaredSystemClassification),
                    Csv(i.ConnectorFlowSide),
                    Csv(i.Notes),
                    Csv(i.Key)
                }));
            }

            return sb.ToString();
        }

        private static string BuildDelta(AuditResult result)
        {
            AuditDelta delta = result.Diagnostics.Delta;
            var sb = new StringBuilder();

            sb.AppendLine("RUN-TO-RUN DELTA");
            sb.AppendLine("----------------");

            if (!delta.HasPreviousRun)
            {
                sb.AppendLine("No previous diagnostic snapshot found for this model.");
                sb.AppendLine("Current trackable issues: " + delta.CurrentIssueCount);
                return sb.ToString();
            }

            sb.AppendLine("Previous run: " + delta.PreviousGeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Previous trackable issues: " + delta.PreviousIssueCount);
            sb.AppendLine("Current trackable issues: " + delta.CurrentIssueCount);
            sb.AppendLine("Resolved: " + delta.ResolvedIssueCount);
            sb.AppendLine("New: " + delta.NewIssueCount);
            sb.AppendLine("Persisting: " + delta.PersistingIssueCount);
            sb.AppendLine("Net change: " + (delta.CurrentIssueCount - delta.PreviousIssueCount));
            sb.AppendLine();

            if (delta.ResolvedIssueKeys.Count > 0)
            {
                sb.AppendLine("Resolved issue keys:");
                foreach (string key in delta.ResolvedIssueKeys.Take(100))
                    sb.AppendLine("  " + key);

                if (delta.ResolvedIssueKeys.Count > 100)
                    sb.AppendLine("  ... +" + (delta.ResolvedIssueKeys.Count - 100) + " more");
                sb.AppendLine();
            }

            if (delta.NewIssueKeys.Count > 0)
            {
                sb.AppendLine("New issue keys:");
                foreach (string key in delta.NewIssueKeys.Take(100))
                    sb.AppendLine("  " + key);

                if (delta.NewIssueKeys.Count > 100)
                    sb.AppendLine("  ... +" + (delta.NewIssueKeys.Count - 100) + " more");
            }

            return sb.ToString();
        }

        private static string Csv(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
