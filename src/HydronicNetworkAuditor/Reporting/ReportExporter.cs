using System;
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
        public string ConnectorsCsv { get; set; }
        public string EdgesCsv { get; set; }
        public string InterfaceGapsCsv { get; set; }
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
                ConnectorsCsv = Path.Combine(folder, "connectors.csv"),
                EdgesCsv = Path.Combine(folder, "edges.csv"),
                InterfaceGapsCsv = Path.Combine(folder, "interface_gaps.csv"),
                GraphJson = Path.Combine(folder, "graph.json")
            };

            File.WriteAllText(bundle.SummaryTxt, BuildSummary(result), Encoding.UTF8);
            File.WriteAllText(bundle.NodesCsv, BuildNodesCsv(result), Encoding.UTF8);
            File.WriteAllText(bundle.ConnectorsCsv, BuildConnectorsCsv(result), Encoding.UTF8);
            File.WriteAllText(bundle.EdgesCsv, BuildEdgesCsv(result), Encoding.UTF8);
            File.WriteAllText(bundle.InterfaceGapsCsv, BuildInterfaceGapsCsv(result), Encoding.UTF8);
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
            sb.AppendLine("Hydronic-relevant nodes: " + result.Nodes.Count(n => n.IsHydronicRelevant));
            sb.AppendLine("Connectors: " + result.Connectors.Count);
            sb.AppendLine("Physical connector edges: " + result.Edges.Count);
            sb.AppendLine("Connected components: " + result.Components.Count);
            sb.AppendLine("Hydronic open end connectors: " + result.OpenEndConnectorCount);
            sb.AppendLine("Direct HT/LT boundary edges: " + result.DirectHtLtBoundaries.Count);
            sb.AppendLine("HT/LT interface gap candidates <=250 mm: " + result.InterfaceGapCandidates.Count);
            sb.AppendLine("Flow classification conflicts: " + result.FlowClassificationConflictCount);
            sb.AppendLine("Mixed temperature-classification nodes: " + result.MixedClassificationNodeCount);
            sb.AppendLine();

            sb.AppendLine("CLASSIFICATION");
            sb.AppendLine("--------------");
            foreach (TemperatureNetwork network in Enum.GetValues(typeof(TemperatureNetwork)))
                sb.AppendLine(network + ": " + result.Nodes.Count(n => n.TemperatureNetwork == network));

            sb.AppendLine();
            foreach (FlowSide side in Enum.GetValues(typeof(FlowSide)))
                sb.AppendLine(side + ": " + result.Nodes.Count(n => n.FlowSide == side));

            sb.AppendLine();
            sb.AppendLine("INTERFACE GAP CANDIDATES");
            sb.AppendLine("------------------------");
            if (result.InterfaceGapCandidates.Count == 0)
            {
                sb.AppendLine("No separated HT/LT hydronic connector pairs were found within 250 mm.");
            }
            else
            {
                foreach (InterfaceGapCandidate gap in result.InterfaceGapCandidates)
                {
                    sb.AppendLine(
                        gap.DistanceMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm | " +
                        gap.FlowSide + " | " +
                        "HT " + gap.AElementId + ":" + gap.AConnectorId +
                        " [Comp " + gap.AComponentIndex + ", " + gap.ASystemNames + "] <-> " +
                        "LT " + gap.BElementId + ":" + gap.BConnectorId +
                        " [Comp " + gap.BComponentIndex + ", " + gap.BSystemNames + "]");
                }
            }

            sb.AppendLine();
            sb.AppendLine("FLOW CLASSIFICATION CONFLICTS");
            sb.AppendLine("-----------------------------");
            foreach (AuditNode node in result.Nodes.Where(n => n.FlowClassificationConflict))
            {
                sb.AppendLine(
                    node.Id + " | " +
                    (node.Mark ?? string.Empty) + " | Declared=" +
                    node.DeclaredSystemClassification + " | Connector=" +
                    node.ConnectorFlowSide + " | Systems=" + node.SystemNames);
            }

            sb.AppendLine();
            sb.AppendLine("DIRECT HT/LT BOUNDARIES");
            sb.AppendLine("-----------------------");
            if (result.DirectHtLtBoundaries.Count == 0)
            {
                sb.AppendLine("No physically connected HT-to-LT boundary was detected.");
            }
            else
            {
                foreach (AuditEdge edge in result.DirectHtLtBoundaries)
                {
                    sb.AppendLine(
                        edge.A + ":" + edge.AConnectorId +
                        " [" + edge.ACategory + "/" + edge.ANetwork + "] <-> " +
                        edge.B + ":" + edge.BConnectorId +
                        " [" + edge.BCategory + "/" + edge.BNetwork + "]");
                }
            }

            return sb.ToString();
        }

        private static string BuildNodesCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ElementId,UniqueId,Category,Name,Family,Type,Mark,SystemNames,ConnectorSystemTypes,DeclaredSystemType,DeclaredSystemClassification,TemperatureNetwork,FlowSide,ConnectorFlowSide,FlowClassificationConflict,IsHydronicRelevant,ComponentIndex,ConnectorCount,HydronicOpenEndConnectorCount,ConnectedElementIds,Evidence");

            foreach (AuditNode n in result.Nodes.OrderBy(n => n.Id))
            {
                string connected = string.Join(";", n.ConnectedElementIds);
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(n.Id), Csv(n.UniqueId), Csv(n.Category), Csv(n.Name), Csv(n.Family), Csv(n.Type),
                    Csv(n.Mark), Csv(n.SystemNames), Csv(n.SystemTypes), Csv(n.DeclaredSystemType),
                    Csv(n.DeclaredSystemClassification), Csv(n.TemperatureNetwork.ToString()),
                    Csv(n.FlowSide.ToString()), Csv(n.ConnectorFlowSide.ToString()),
                    Csv(n.FlowClassificationConflict), Csv(n.IsHydronicRelevant), Csv(n.ComponentIndex),
                    Csv(n.ConnectorCount), Csv(n.OpenEndConnectorCount), Csv(connected), Csv(n.EvidenceText)
                }));
            }

            return sb.ToString();
        }

        private static string BuildConnectorsCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OwnerElementId,ConnectorId,Domain,ConnectorType,PipeSystemType,MEPSystemName,Direction,IsConnected,OriginXmm,OriginYmm,OriginZmm");

            foreach (AuditConnector c in result.Connectors
                         .OrderBy(c => c.OwnerElementId)
                         .ThenBy(c => c.ConnectorId))
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(c.OwnerElementId), Csv(c.ConnectorId), Csv(c.Domain), Csv(c.ConnectorType),
                    Csv(c.PipeSystemType), Csv(c.MEPSystemName), Csv(c.Direction), Csv(c.IsConnected),
                    Csv(c.OriginXmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(c.OriginYmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(c.OriginZmm.ToString("0.###", CultureInfo.InvariantCulture))
                }));
            }

            return sb.ToString();
        }

        private static string BuildEdgesCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("A,AConnectorId,B,BConnectorId,ACategory,BCategory,ANetwork,BNetwork,AFlowSide,BFlowSide,DirectHtLtBoundary");

            foreach (AuditEdge e in result.Edges
                         .OrderBy(e => e.A)
                         .ThenBy(e => e.AConnectorId)
                         .ThenBy(e => e.B)
                         .ThenBy(e => e.BConnectorId))
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(e.A), Csv(e.AConnectorId), Csv(e.B), Csv(e.BConnectorId),
                    Csv(e.ACategory), Csv(e.BCategory), Csv(e.ANetwork.ToString()), Csv(e.BNetwork.ToString()),
                    Csv(e.AFlowSide.ToString()), Csv(e.BFlowSide.ToString()), Csv(e.IsDirectHtLtBoundary)
                }));
            }

            return sb.ToString();
        }

        private static string BuildInterfaceGapsCsv(AuditResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DistanceMm,FlowSide,AElementId,AConnectorId,AComponentIndex,ANetwork,AMark,ASystemNames,BElementId,BConnectorId,BComponentIndex,BNetwork,BMark,BSystemNames,AXmm,AYmm,AZmm,BXmm,BYmm,BZmm");

            foreach (InterfaceGapCandidate g in result.InterfaceGapCandidates)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(g.DistanceMm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(g.FlowSide), Csv(g.AElementId), Csv(g.AConnectorId), Csv(g.AComponentIndex),
                    Csv(g.ANetwork), Csv(g.AMark), Csv(g.ASystemNames), Csv(g.BElementId), Csv(g.BConnectorId),
                    Csv(g.BComponentIndex), Csv(g.BNetwork), Csv(g.BMark), Csv(g.BSystemNames),
                    Csv(g.AXmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(g.AYmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(g.AZmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(g.BXmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(g.BYmm.ToString("0.###", CultureInfo.InvariantCulture)),
                    Csv(g.BZmm.ToString("0.###", CultureInfo.InvariantCulture))
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
                AppendJsonProperty(sb, "mark", n.Mark, true, 3);
                AppendJsonProperty(sb, "systemNames", n.SystemNames, true, 3);
                AppendJsonProperty(sb, "connectorSystemTypes", n.SystemTypes, true, 3);
                AppendJsonProperty(sb, "declaredSystemType", n.DeclaredSystemType, true, 3);
                AppendJsonProperty(sb, "declaredSystemClassification", n.DeclaredSystemClassification, true, 3);
                AppendJsonProperty(sb, "temperatureNetwork", n.TemperatureNetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "flowSide", n.FlowSide.ToString(), true, 3);
                AppendJsonProperty(sb, "connectorFlowSide", n.ConnectorFlowSide.ToString(), true, 3);
                AppendJsonBoolean(sb, "flowClassificationConflict", n.FlowClassificationConflict, true, 3);
                AppendJsonBoolean(sb, "isHydronicRelevant", n.IsHydronicRelevant, true, 3);
                AppendJsonNumber(sb, "componentIndex", n.ComponentIndex, true, 3);
                AppendJsonProperty(sb, "evidence", n.EvidenceText, true, 3);
                AppendJsonNumber(sb, "connectorCount", n.ConnectorCount, true, 3);
                AppendJsonNumber(sb, "hydronicOpenEndConnectorCount", n.OpenEndConnectorCount, true, 3);
                sb.Append("      \"connectedElementIds\": [");
                sb.Append(string.Join(",", n.ConnectedElementIds));
                sb.AppendLine("]");
                sb.Append("    }");
                sb.AppendLine(i < result.Nodes.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"connectors\": [");
            for (int i = 0; i < result.Connectors.Count; i++)
            {
                AuditConnector c = result.Connectors[i];
                sb.AppendLine("    {");
                AppendJsonNumber(sb, "ownerElementId", c.OwnerElementId, true, 3);
                AppendJsonNumber(sb, "connectorId", c.ConnectorId, true, 3);
                AppendJsonProperty(sb, "domain", c.Domain, true, 3);
                AppendJsonProperty(sb, "connectorType", c.ConnectorType, true, 3);
                AppendJsonProperty(sb, "pipeSystemType", c.PipeSystemType, true, 3);
                AppendJsonProperty(sb, "mepSystemName", c.MEPSystemName, true, 3);
                AppendJsonProperty(sb, "direction", c.Direction, true, 3);
                AppendJsonBoolean(sb, "isConnected", c.IsConnected, true, 3);
                AppendJsonDouble(sb, "originXmm", c.OriginXmm, true, 3);
                AppendJsonDouble(sb, "originYmm", c.OriginYmm, true, 3);
                AppendJsonDouble(sb, "originZmm", c.OriginZmm, false, 3);
                sb.Append("    }");
                sb.AppendLine(i < result.Connectors.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"edges\": [");
            for (int i = 0; i < result.Edges.Count; i++)
            {
                AuditEdge e = result.Edges[i];
                sb.AppendLine("    {");
                AppendJsonNumber(sb, "a", e.A, true, 3);
                AppendJsonNumber(sb, "aConnectorId", e.AConnectorId, true, 3);
                AppendJsonNumber(sb, "b", e.B, true, 3);
                AppendJsonNumber(sb, "bConnectorId", e.BConnectorId, true, 3);
                AppendJsonProperty(sb, "aNetwork", e.ANetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "bNetwork", e.BNetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "aFlowSide", e.AFlowSide.ToString(), true, 3);
                AppendJsonProperty(sb, "bFlowSide", e.BFlowSide.ToString(), true, 3);
                AppendJsonBoolean(sb, "directHtLtBoundary", e.IsDirectHtLtBoundary, false, 3);
                sb.Append("    }");
                sb.AppendLine(i < result.Edges.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"interfaceGapCandidates\": [");
            for (int i = 0; i < result.InterfaceGapCandidates.Count; i++)
            {
                InterfaceGapCandidate g = result.InterfaceGapCandidates[i];
                sb.AppendLine("    {");
                AppendJsonDouble(sb, "distanceMm", g.DistanceMm, true, 3);
                AppendJsonProperty(sb, "flowSide", g.FlowSide.ToString(), true, 3);
                AppendJsonNumber(sb, "aElementId", g.AElementId, true, 3);
                AppendJsonNumber(sb, "aConnectorId", g.AConnectorId, true, 3);
                AppendJsonNumber(sb, "aComponentIndex", g.AComponentIndex, true, 3);
                AppendJsonProperty(sb, "aNetwork", g.ANetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "aMark", g.AMark, true, 3);
                AppendJsonProperty(sb, "aSystemNames", g.ASystemNames, true, 3);
                AppendJsonNumber(sb, "bElementId", g.BElementId, true, 3);
                AppendJsonNumber(sb, "bConnectorId", g.BConnectorId, true, 3);
                AppendJsonNumber(sb, "bComponentIndex", g.BComponentIndex, true, 3);
                AppendJsonProperty(sb, "bNetwork", g.BNetwork.ToString(), true, 3);
                AppendJsonProperty(sb, "bMark", g.BMark, true, 3);
                AppendJsonProperty(sb, "bSystemNames", g.BSystemNames, false, 3);
                sb.Append("    }");
                sb.AppendLine(i < result.InterfaceGapCandidates.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"components\": [");
            for (int i = 0; i < result.Components.Count; i++)
            {
                ConnectedComponentSummary c = result.Components[i];
                sb.AppendLine("    {");
                AppendJsonNumber(sb, "index", c.Index, true, 3);
                AppendJsonNumber(sb, "nodeCount", c.NodeCount, true, 3);
                AppendJsonNumber(sb, "edgeCount", c.EdgeCount, true, 3);
                AppendJsonNumber(sb, "htNodeCount", c.HtNodeCount, true, 3);
                AppendJsonNumber(sb, "ltNodeCount", c.LtNodeCount, true, 3);
                AppendJsonNumber(sb, "mixedNodeCount", c.MixedNodeCount, true, 3);
                AppendJsonNumber(sb, "unknownNodeCount", c.UnknownNodeCount, true, 3);
                AppendJsonBoolean(sb, "containsHtAndLt", c.ContainsHtAndLt, true, 3);
                sb.Append("      \"nodeIds\": [");
                sb.Append(string.Join(",", c.NodeIds));
                sb.AppendLine("]");
                sb.Append("    }");
                sb.AppendLine(i < result.Components.Count - 1 ? "," : "");
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

        private static void AppendJsonDouble(StringBuilder sb, string name, double value, bool comma, int indent)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append("\"");
            sb.Append(JsonEscape(name));
            sb.Append("\": ");
            sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            if (comma) sb.Append(",");
            sb.AppendLine();
        }

        private static void AppendJsonBoolean(StringBuilder sb, string name, bool value, bool comma, int indent)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append("\"");
            sb.Append(JsonEscape(name));
            sb.Append("\": ");
            sb.Append(value ? "true" : "false");
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
