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
            sb.AppendLine("Nodes                    : " + result.Nodes.Count);
            sb.AppendLine("Connectors               : " + result.Connectors.Count);
            sb.AppendLine("Physical connector edges : " + result.Edges.Count);
            sb.AppendLine("Connected components     : " + result.Components.Count);
            sb.AppendLine("Open end connectors      : " + result.OpenEndConnectorCount);
            sb.AppendLine("Direct HT/LT boundaries  : " + result.DirectHtLtBoundaries.Count);
            sb.AppendLine("Mixed classification     : " + result.MixedClassificationNodeCount);
            sb.AppendLine();
            sb.AppendLine("HT nodes                 : " + result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.HT));
            sb.AppendLine("LT nodes                 : " + result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.LT));
            sb.AppendLine("Unknown network nodes    : " + result.Nodes.Count(n => n.TemperatureNetwork == TemperatureNetwork.Unknown));
            sb.AppendLine();
            sb.AppendLine("Mixed HT/LT components:");
            foreach (var component in result.Components.Where(c => c.ContainsHtAndLt))
            {
                sb.AppendLine(
                    "  Component #" + component.Index +
                    " | Nodes=" + component.NodeCount +
                    " | HT=" + component.HtNodeCount +
                    " | LT=" + component.LtNodeCount +
                    " | Mixed=" + component.MixedNodeCount +
                    " | Unknown=" + component.UnknownNodeCount);
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
