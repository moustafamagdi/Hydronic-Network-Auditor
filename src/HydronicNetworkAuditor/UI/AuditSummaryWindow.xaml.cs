using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using HydronicNetworkAuditor.Core;
using HydronicNetworkAuditor.Reporting;

namespace HydronicNetworkAuditor.UI
{
    public partial class AuditSummaryWindow : Window
    {
        private readonly string _folder;

        public AuditSummaryWindow(AuditResult result, ExportBundle bundle)
        {
            InitializeComponent();

            _folder = bundle.Folder;
            ModelText.Text = result.DocumentTitle;
            FolderText.Text = bundle.Folder;
            FolderText.ToolTip = bundle.Folder;

            var sb = new StringBuilder();
            sb.AppendLine("Nodes                         : " + result.Nodes.Count);
            sb.AppendLine("Hydronic relevant nodes       : " + result.Nodes.Count(n => n.IsHydronicRelevant));
            sb.AppendLine("Connectors                    : " + result.Connectors.Count);
            sb.AppendLine("Physical connector edges      : " + result.Edges.Count);
            sb.AppendLine("Connected components          : " + result.Components.Count);
            sb.AppendLine("Hydronic open ends            : " + result.OpenEndConnectorCount);
            sb.AppendLine("Direct HT/LT boundaries       : " + result.DirectHtLtBoundaries.Count);
            sb.AppendLine("HT/LT gap candidates <=250 mm : " + result.InterfaceGapCandidates.Count);
            sb.AppendLine("Flow classification conflicts : " + result.FlowClassificationConflictCount);
            sb.AppendLine();
            sb.AppendLine("HT nodes                      : " + result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.HT));
            sb.AppendLine("LT nodes                      : " + result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.LT));
            sb.AppendLine("Unknown network nodes         : " + result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.Unknown));
            sb.AppendLine();

            sb.AppendLine("Nearest separated HT/LT interfaces:");
            foreach (var gap in result.InterfaceGapCandidates.Take(12))
            {
                sb.AppendLine(
                    "  " + gap.DistanceMm.ToString("0.0") + " mm | " +
                    gap.FlowSide + " | " +
                    gap.AElementId + " (HT Comp " + gap.AComponentIndex + ") <-> " +
                    gap.BElementId + " (LT Comp " + gap.BComponentIndex + ")");
            }

            sb.AppendLine();
            sb.AppendLine("Top probable root-cause families:");
            foreach (var family in result.Diagnostics.FamilyRankings
                .Where(f => f.IsProbableRootCause)
                .Take(10))
            {
                sb.AppendLine(
                    "  " + family.Family +
                    " | CHWR " + family.ChwrInstanceCount +
                    " | Wrong " + family.WrongSupplyCount +
                    " | Mixed " + family.MixedCount +
                    " | Affected pipes " + family.AffectedConflictPipeCount +
                    " | Score " + family.RootCauseScore.ToString("0.0"));
            }

            sb.AppendLine();
            sb.AppendLine("Top propagation clusters:");
            foreach (var cluster in result.Diagnostics.PropagationClusters.Take(10))
            {
                sb.AppendLine(
                    "  " + cluster.Scope +
                    " | " + cluster.Network +
                    " | Comp " + cluster.ComponentIndex +
                    " | Pipes " + cluster.ConflictPipeCount +
                    " | Suspects: " +
                    (cluster.SuspectFamilies.Count == 0
                        ? "(none)"
                        : string.Join("; ", cluster.SuspectFamilies)));
            }

            sb.AppendLine();
            sb.AppendLine("Run-to-run delta:");
            if (result.Diagnostics.Delta.HasPreviousRun)
            {
                sb.AppendLine(
                    "  Previous " + result.Diagnostics.Delta.PreviousIssueCount +
                    " -> Current " + result.Diagnostics.Delta.CurrentIssueCount +
                    " | Resolved " + result.Diagnostics.Delta.ResolvedIssueCount +
                    " | New " + result.Diagnostics.Delta.NewIssueCount +
                    " | Persisting " + result.Diagnostics.Delta.PersistingIssueCount);
            }
            else
            {
                sb.AppendLine(
                    "  First diagnostic snapshot for this model. Current tracked issues: " +
                    result.Diagnostics.Delta.CurrentIssueCount);
            }

            sb.AppendLine();
            sb.AppendLine("Additional files:");
            sb.AppendLine("  diagnostics_summary.txt");
            sb.AppendLine("  root_causes.csv");
            sb.AppendLine("  propagation_clusters.csv");
            sb.AppendLine("  diagnostic_issues.csv");
            sb.AppendLine("  delta.txt");

            SummaryText.Text = sb.ToString();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_folder)) return;
            Process.Start("explorer.exe", "\"" + _folder + "\"");
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
