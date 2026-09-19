using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HydronicNetworkAuditor.Core;

namespace HydronicNetworkAuditor.Reporting
{
    public sealed class ExportBundle
    {
        public string Folder { get; set; }
        public string SummaryTxt { get; set; }
        public string NodesCsv { get; set; }
        public string EdgesCsv { get; set; }
        public string GraphJson { get; set; }
    }

    public sealed class ReportExporter
    {
        public ExportBundle Export(AuditResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string root = Path.Combine(desktop, "HydronicNetworkAuditor");
            Directory.CreateDirectory(root);

            string folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(folder);

            var bundle = new ExportBundle
            {
                Folder = folder,
                SummaryTxt = Path.Combine(folder, "summary.txt"),
                NodesCsv = Path.Combine(folder, "nodes.csv"),
                EdgesCsv = Path.Combine(folder, "edges.csv"),
                GraphJson = Path.Combine(folder, "graph.json")
            };

            File.WriteAllText(bundle.SummaryTxt, BuildSummary(result), Encoding.UTF8);
            File.WriteAllText(bundle.NodesCsv, BuildNodesCsv(result), Encoding.UTF8);
            File.WriteAllText(bundle.EdgesCsv, BuildEdgesCsv(result), Encoding.UTF8);
            File.WriteAllText(bundle.GraphJson, BuildJson(result), Encoding.UTF8);

            return bundle;
        }

        private static string BuildSummary(AuditResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine("HYDRONIC NETWORK AUDITOR");
            sb.AppendLine("========================");
            sb.AppendLine("Model: " + result.DocumentTitle);
            sb.AppendLine("Path: " + result.DocumentPath);
            sb.AppendLine("Generated: " + result.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            sb.AppendLine("GRAPH SUMMARY");
            sb.AppendLine("-------------");
            sb.AppendLine("Nodes: " + result.Nodes.Count);
            sb.AppendLine("Edges: " + result.Edges.Count);
            sb.AppendLine("Connected components: " + result.Components.Count);
            sb.AppendLine("Open end connectors: " + result.OpenEndConnectorCount);
            sb.AppendLine("Direct HT/LT boundary edges: " + result.DirectHtLtBoundaries.Count);
            sb.AppendLine("Mixed-classification nodes: " + result.MixedClassificationNodeCount);
            sb.AppendLine();

            sb.AppendLine("CLASSIFICATION");
            sb.AppendLine("--------------");
            foreach (TemperatureNetwork network in Enum.GetValues(typeof(TemperatureNetwork)))
                sb.AppendLine(network + ": " + result.Nodes.Count(n => n.TemperatureNetwork == network));

            sb.AppendLine();
            foreach (FlowSide side in Enum.GetValues(typeof(FlowSide)))
                sb.AppendLine(side + ": " + result.Nodes.Count(n => n.FlowSide == side));

            sb.AppendLine();
            sb.AppendLine("CONNECTED COMPONENTS");
            sb.AppendLine("--------------------");
            foreach (ConnectedComponentSummary component in result.Components)
            {
                sb.AppendLine(
                    "#" + component.Index +
                    " Nodes=" + component.NodeCount +
                    " Edges=" + component.EdgeCount +
                    " HT=" + component.HtNodeCount +
                    " LT=" + component.LtNodeCount +
                    " Mixed=" + component.MixedNodeCount +
                    " Unknown=" + component.UnknownNodeCount +
                    " ContainsHT+LT=" + component.ContainsHtAndLt);
            }

            sb.AppendLine();
            sb.AppendLine("DIRECT HT/LT BOUNDARIES");
            sb.AppendLine("-----------------------");
            if (result.DirectHtLtBoundaries.Count == 0)
            {
                sb.AppendLine("No direct HT-to-LT element boundary was detected.");
                sb.AppendLine("Note: a real transition can still pass through Unknown/Mixed fittings or accessories.");
            }
            else
            {
                foreach (AuditEdge edge in result.DirectHtLtBoundaries)
                {
                    sb.AppendLine(
                        edge.A + " [" + edge.ACategory + "/" + edge.ANetwork + "] <-> " +
                        edge.B + " [" + edge.BCategory + "/" + edge.BNetwork + "]");
                }
            }

            sb.AppendLine();
            sb.AppendLine("MIXED CLASSIFICATION NODES");
            sb.AppendLine("--------------------------");
            foreach (AuditNode node in result.Nodes.Where(n => n.TemperatureNetwork == TemperatureNetwork.Mixed))
                sb.AppendLine(node.Id + " | " + node.Category + " | " + node.Name + " | " + node.EvidenceText);

            return sb.ToString();
        }

        private static string BuildNodesCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ElementId,UniqueId,Category,Name,Family,Type,SystemNames,SystemTypes,TemperatureNetwork,FlowSide,ConnectorCount,OpenEndConnectorCount,ConnectedElementIds,Evidence");

            foreach (AuditNode n in result.Nodes.OrderBy(n => n.Id))
            {
                string connected = string.Join(";", n.ConnectedElementIds);
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(n.Id),
                    Csv(n.UniqueId),
                    Csv(n.Category),
                    Csv(n.Name),
                    Csv(n.Family),
                    Csv(n.Type),
                    Csv(n.SystemNames),
                    Csv(n.SystemTypes),
                    Csv(n.TemperatureNetwork.ToString()),
                    Csv(n.FlowSide.ToString()),
                    Csv(n.ConnectorCount),
                    Csv(n.OpenEndConnectorCount),
                    Csv(connected),
                    Csv(n.EvidenceText)
                }));
            }

            return sb.ToString();
        }

        private static string BuildEdgesCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,B,ACategory,BCategory,ANetwork,BNetwork,AFlowSide,BFlowSide,DirectHtLtBoundary");

            foreach (AuditEdge e in result.Edges.OrderBy(e => e.A).ThenBy(e => e.B))
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(e.A),
                    Csv(e.B),
                    Csv(e.ACategory),
                    Csv(e.BCategory),
                    Csv(e.ANetwork.ToString()),
                    Csv(e.BNetwork.ToString()),
                    Csv(e.AFlowSide.ToString()),
                    Csv(e.BFlowSide.ToString()),
                    Csv(e.IsDirectHtLtBoundary)
                }));
            }

            return sb.ToString();
        }

        private static string BuildJson(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            AppendJsonProperty(sb, "documentTitle", result.DocumentTitle, true, 1);
            AppendJsonProperty(sb, "documentPath", result.DocumentPath, true, 1);
            AppendJsonProperty(sb, "generatedAt", result.GeneratedAt.ToString("o"), true, 1);

            sb.AppendLine("  \"nodes\": [");
            for (int i = 0; i < result.Nodes.Count; i++)
            {
                AuditNode n = result.Nodes[i];
                sb.AppendLine("    {");
                AppendJsonNumber(sb, "id", n.Id, true, 3);
                AppendJsonProperty(sb, "uniqueId", n.UniqueId, true, 3);
                AppendJsonProperty(sb, "category", n.Category, true, 3);
                AppendJsonProperty(sb, "name", n.Name, true, 3);
                AppendJsonProperty(sb, "family", n.Family, true, 3);
                AppendJsonProperty(sb, "type", n.Type, true, 3);
                AppendJsonProperty(sb, "systemNames", n.SystemNames, true, 3);
                AppendJsonProperty(sb, "systemTypes", n.SystemTypes, true, 3);
                AppendJsonProperty(sb, "temperatureNetwork", n.TemperatureNetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "flowSide", n.FlowSide.ToString(), true, 3);
                AppendJsonNumber(sb, "connectorCount", n.ConnectorCount, true, 3);
                AppendJsonNumber(sb, "openEndConnectorCount", n.OpenEndConnectorCount, true, 3);
                sb.Append("      \"connectedElementIds\": [");
                sb.Append(string.Join(",", n.ConnectedElementIds));
                sb.AppendLine("]");
                sb.Append("    }");
                sb.AppendLine(i < result.Nodes.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"edges\": [");
            for (int i = 0; i < result.Edges.Count; i++)
            {
                AuditEdge e = result.Edges[i];
                sb.AppendLine("    {");
                AppendJsonNumber(sb, "a", e.A, true, 3);
                AppendJsonNumber(sb, "b", e.B, true, 3);
                AppendJsonProperty(sb, "aNetwork", e.ANetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "bNetwork", e.BNetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "aFlowSide", e.AFlowSide.ToString(), true, 3);
                AppendJsonProperty(sb, "bFlowSide", e.BFlowSide.ToString(), true, 3);
                sb.Append("      \"directHtLtBoundary\": ");
                sb.AppendLine(e.IsDirectHtLtBoundary ? "true" : "false");
                sb.Append("    }");
                sb.AppendLine(i < result.Edges.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string Csv(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static void AppendJsonProperty(StringBuilder sb, string name, string value, bool comma, int indent)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append("\"");
            sb.Append(JsonEscape(name));
            sb.Append("\": \"");
            sb.Append(JsonEscape(value ?? string.Empty));
            sb.Append("\"");
            if (comma) sb.Append(",");
            sb.AppendLine();
        }

        private static void AppendJsonNumber(StringBuilder sb, string name, long value, bool comma, int indent)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append("\"");
            sb.Append(JsonEscape(name));
            sb.Append("\": ");
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
            if (comma) sb.Append(",");
            sb.AppendLine();
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
