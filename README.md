# Hydronic Network Auditor

Revit 2024 add-in for auditing complex hydronic networks where physical connector continuity does not necessarily equal logical system identity.

Initial focus: HUMAIN DC101 chilled-water networks (HT/LT, Supply/Return, controlled cross-connections).

## MVP goals

- Read physical connectivity from Revit MEP connectors.
- Build a graph of pipes, fittings, accessories and mechanical equipment.
- Infer HT/LT and Supply/Return from system/type/family/instance metadata without changing the model.
- Detect physical transitions between logical networks.
- Report connected components, suspicious transitions, open connectors and classification conflicts.
- Export TXT, CSV and JSON reports for deeper review.

Target: Revit 2024 / .NET Framework 4.8.
