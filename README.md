# Hydronic Network Auditor

Revit 2024 / .NET Framework 4.8 add-in for auditing complex hydronic networks where **physical connector continuity does not necessarily equal logical system identity**.

The initial use case is the HUMAIN DC101 chilled-water model, especially the HT/LT loops and the intentional controlled interconnections between them.

## Current MVP

The add-in is read-only. It does not rename, disconnect, reconnect, or modify any Revit element.

It currently:

- Reads Revit MEP physical connectors from Pipes, Pipe Fittings, Pipe Accessories and Mechanical Equipment.
- Builds a connector-level physical graph, not only an element-level adjacency list.
- Captures each connector's ID, domain, connector type, Revit piping system type, MEP system name, flow direction, connected state and XYZ location.
- Captures Revit system names and piping system types as evidence.
- Infers **HT / LT / Mixed / Unknown** from names, families, types, systems and selected parameters.
- Infers **Supply / Return / Mixed / Unknown**.
- Finds connected components containing both HT and LT evidence.
- Reports direct HT-to-LT classified connector boundaries when they exist.
- Counts open end connectors.
- Exports a machine-readable graph intended for comparison with the project design schematics.

## Output

Every run creates a timestamped folder on the Desktop:

`Desktop\HydronicNetworkAuditor\yyyyMMdd_HHmmss_fff\`

with:

- `summary.txt` — quick engineering summary.
- `nodes.csv` — one row per audited Revit element.
- `connectors.csv` — connector-level system/direction/location data.
- `edges.csv` — actual connector-to-connector physical connections.
- `graph.json` — combined graph payload including nodes, connectors, edges and connected components.

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

The manifest uses `.\HydronicNetworkAuditor.dll`, so the DLL and `.addin` file remain portable as long as they are deployed in the same Revit Addins folder.

## Classification evidence

The MVP looks at element/family/type names, connector MEP system names, connector pipe system types, and these parameters when present:

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

## Why connector-level export matters

A designed HT/LT interconnection can pass through a valve or fitting whose own family/type/system metadata does not say HT or LT. An element-only graph can therefore miss the actual logical transition. The connector export preserves enough evidence to trace the physical path through those intermediate objects.

## Next development step

Run the MVP on the actual HUMAIN chilled-water model and provide the generated report folder (preferably all five files). The next rule layer will use the real model data to:

- distinguish intended DH1-DH4 HT/LT cross-connections from accidental contamination,
- trace Supply and Return separately,
- identify the exact boundary valve/fitting chain,
- compare the as-modelled topology against MC601-MC608,
- flag missing/extra connections and reversed or inconsistent system classification.
