using System;
using System.Collections.Generic;

namespace HydronicNetworkAuditor.Core
{
    public enum TemperatureNetwork
    {
        Unknown,
        HT,
        LT,
        Mixed
    }

    public enum FlowSide
    {
        Unknown,
        Supply,
        Return,
        Mixed
    }

    public sealed class AuditConnector
    {
        public long OwnerElementId { get; set; }
        public int ConnectorId { get; set; }
        public string Domain { get; set; }
        public string ConnectorType { get; set; }
        public string PipeSystemType { get; set; }
        public string MEPSystemName { get; set; }
        public string Direction { get; set; }
        public bool IsConnected { get; set; }
        public double OriginXmm { get; set; }
        public double OriginYmm { get; set; }
        public double OriginZmm { get; set; }
    }

    public sealed class AuditNode
    {
        public long Id { get; set; }
        public string UniqueId { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string SystemNames { get; set; }
        public string SystemTypes { get; set; }
        public string EvidenceText { get; set; }
        public TemperatureNetwork TemperatureNetwork { get; set; }
        public FlowSide FlowSide { get; set; }
        public int ConnectorCount { get; set; }
        public int OpenEndConnectorCount { get; set; }
        public List<long> ConnectedElementIds { get; } = new List<long>();
    }

    public sealed class AuditEdge
    {
        public long A { get; set; }
        public int AConnectorId { get; set; }
        public long B { get; set; }
        public int BConnectorId { get; set; }
        public string ACategory { get; set; }
        public string BCategory { get; set; }
        public TemperatureNetwork ANetwork { get; set; }
        public TemperatureNetwork BNetwork { get; set; }
        public FlowSide AFlowSide { get; set; }
        public FlowSide BFlowSide { get; set; }

        public bool IsDirectHtLtBoundary =>
            (ANetwork == TemperatureNetwork.HT && BNetwork == TemperatureNetwork.LT) ||
            (ANetwork == TemperatureNetwork.LT && BNetwork == TemperatureNetwork.HT);
    }

    public sealed class ConnectedComponentSummary
    {
        public int Index { get; set; }
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
        public int HtNodeCount { get; set; }
        public int LtNodeCount { get; set; }
        public int MixedNodeCount { get; set; }
        public int UnknownNodeCount { get; set; }
        public bool ContainsHtAndLt => HtNodeCount > 0 && LtNodeCount > 0;
        public List<long> NodeIds { get; } = new List<long>();
    }

    public sealed class AuditResult
    {
        public string DocumentTitle { get; set; }
        public string DocumentPath { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<AuditNode> Nodes { get; } = new List<AuditNode>();
        public List<AuditConnector> Connectors { get; } = new List<AuditConnector>();
        public List<AuditEdge> Edges { get; } = new List<AuditEdge>();
        public List<ConnectedComponentSummary> Components { get; } = new List<ConnectedComponentSummary>();
        public List<AuditEdge> DirectHtLtBoundaries { get; } = new List<AuditEdge>();

        public int OpenEndConnectorCount { get; set; }
        public int MixedClassificationNodeCount { get; set; }
    }
}
