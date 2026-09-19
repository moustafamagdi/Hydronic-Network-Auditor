using System;
using System.Collections.Generic;

namespace HydronicNetworkAuditor.Core
{
    public sealed class DiagnosticIssue
    {
        public string Key { get; set; }
        public string IssueType { get; set; }
        public string Scope { get; set; }
        public long ElementId { get; set; }
        public long RelatedElementId { get; set; }
        public int ComponentIndex { get; set; }
        public TemperatureNetwork Network { get; set; }
        public FlowSide FlowSide { get; set; }
        public string Family { get; set; }
        public string Mark { get; set; }
        public string DeclaredSystemType { get; set; }
        public string DeclaredSystemClassification { get; set; }
        public FlowSide ConnectorFlowSide { get; set; }
        public string Notes { get; set; }
    }

    public sealed class FamilyDiagnostic
    {
        public string Family { get; set; }
        public string Category { get; set; }
        public int ChwrInstanceCount { get; set; }
        public int CorrectReturnCount { get; set; }
        public int WrongSupplyCount { get; set; }
        public int MixedCount { get; set; }
        public int AffectedConflictPipeCount { get; set; }
        public double ErrorRatePercent { get; set; }
        public double RootCauseScore { get; set; }
        public string ConfidenceClassification { get; set; }
        public bool IsProbableRootCause { get; set; }
        public List<long> ProblemInstanceIds { get; } = new List<long>();
    }

    public sealed class PropagationCluster
    {
        public string Key { get; set; }
        public string Scope { get; set; }
        public int ComponentIndex { get; set; }
        public TemperatureNetwork Network { get; set; }
        public int ConflictPipeCount { get; set; }
        public List<long> ConflictPipeIds { get; } = new List<long>();
        public List<string> SuspectFamilies { get; } = new List<string>();
        public List<long> SuspectInstanceIds { get; } = new List<long>();
    }

    public sealed class AuditDelta
    {
        public bool HasPreviousRun { get; set; }
        public DateTime PreviousGeneratedAt { get; set; }
        public int PreviousIssueCount { get; set; }
        public int CurrentIssueCount { get; set; }
        public int NewIssueCount { get; set; }
        public int ResolvedIssueCount { get; set; }
        public int PersistingIssueCount { get; set; }
        public List<string> NewIssueKeys { get; } = new List<string>();
        public List<string> ResolvedIssueKeys { get; } = new List<string>();
    }

    public sealed class DiagnosticResult
    {
        public List<DiagnosticIssue> Issues { get; } = new List<DiagnosticIssue>();
        public List<FamilyDiagnostic> FamilyRankings { get; } = new List<FamilyDiagnostic>();
        public List<PropagationCluster> PropagationClusters { get; } = new List<PropagationCluster>();
        public AuditDelta Delta { get; set; } = new AuditDelta();
    }
}
