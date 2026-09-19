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
