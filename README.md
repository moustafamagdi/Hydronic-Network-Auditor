# Hydronic Network Auditor

Revit 2024 / .NET Framework 4.8 add-in for auditing complex hydronic networks where **physical connector continuity does not necessarily equal logical system identity**.

The initial use case is the HUMAIN DC101 chilled-water model, especially the HT/LT loops and the intentional controlled interconnections between them.

## Current MVP

The add-in is read-only. It does not rename, disconnect, reconnect, or modify any Revit element.

It currently:

- Reads Revit MEP physical connectors from Pipes, Pipe Fittings, Pipe Accessories and Mechanical Equipment.
- Builds an undirected element connectivity graph.
- Captures Revit system names and piping system types from connectors.
- Infers **HT / LT / Mixed / Unknown** from names, families, types, systems and selected parameters.
- Infers **Supply / Return / Mixed / Unknown**.
- Finds connected components containing both HT and LT evidence.
- Reports direct HT-to-LT classified boundaries when they exist.
- Counts open end connectors.
- Exports a detailed machine-readable graph so the model can be compared with the design schematics later.

## Output

Every run creates a timestamped folder on the Desktop:

`Desktop\HydronicNetworkAuditor\yyyyMMdd_HHmmss_fff\`

with:

- `summary.txt` — quick engineering summary.
- `nodes.csv` — one row per audited Revit element.
- `edges.csv` — physical element-to-element connections.
- `graph.json` — complete graph payload for deeper analysis.

The Revit command also opens a resizable modeless WPF summary window. The window only displays the already-captured audit snapshot, so it does not access the Revit API outside the valid command context.

## Build / install

1. Open `HydronicNetworkAuditor.sln` in Visual Studio 2022.
2. Confirm Revit 2024 is installed at:
   `C:\Program Files\Autodesk\Revit 2024\`
3. Build Debug or Release.
4. The post-build event copies both the DLL and manifest to:
   `%APPDATA%\Autodesk\Revit\Addins\2024\`
5. Start/restart Revit 2024.
6. Run **MM Tools > Hydronic Audit > Audit Hydronic Network**.

## Classification evidence

The MVP looks at element/family/type names, connector system names/types, and these parameters when present:

- System Name
- System Type
- System Classification
- System Abbreviation
- Service / Service Name
- Mark
- Comments
- Type Comments
- Description
- HNA_Network
- HNA_Flow_Side
- HNA_Connection_Type

The optional `HNA_*` parameters are reserved for a later project-specific rule layer. No parameters are created or written by the MVP.

## Next development step

After running this MVP on the actual HUMAIN model, use `summary.txt`, `nodes.csv`, `edges.csv` and `graph.json` to identify how the current model encodes the HT/LT cross-connections. The next rule layer will then detect the intended DH1-DH4 interconnections versus unintended network contamination and will add design-schematic validation.
