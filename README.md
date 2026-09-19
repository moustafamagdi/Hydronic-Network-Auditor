# Hydronic Network Auditor

Revit 2024 / .NET Framework 4.8 add-in for auditing complex hydronic networks where **physical connector continuity does not necessarily equal logical system identity**.

The initial use case is the HUMAIN DC101 chilled-water model, especially HT/LT loops and controlled interconnections between them.

## Current behavior

The add-in is read-only. It does not rename, disconnect, reconnect, or modify any Revit element.

It:

- reads Pipes, Pipe Fittings, Pipe Accessories and Mechanical Equipment,
- builds a connector-level physical graph,
- captures connector ID, system type, MEP system name, direction, connection state and XYZ,
- classifies HT/LT primarily from the Revit **System Type** parameter,
- classifies Supply/Return primarily from **System Classification**,
- uses Marks/family/type text only as fallback evidence,
- reports when connector flow classification conflicts with the declared Revit classification,
- counts hydronic open ends only (not electrical/HVAC/refrigerant open connectors),
- detects physically separated HT/LT open-connector pairs within 250 mm as **interface gap candidates**,
- exports the full graph for engineering review.

## Why classification priority matters

Project schematics intentionally use some tags such as `BFV-CH-HT-...` on the LT side of an HT/LT interface. Therefore a Mark must not override an element whose Revit System Type is explicitly `LT CHWS` or `LT CHWR`.

## Output

Each run creates:

`Desktop\HydronicNetworkAuditor\yyyyMMdd_HHmmss_fff\`

Files:

- `summary.txt`
- `nodes.csv`
- `connectors.csv`
- `edges.csv`
- `interface_gaps.csv`
- `graph.json`

The modeless WPF window shows the main audit metrics and the nearest HT/LT interface candidates.

## Build / install

1. Open `HydronicNetworkAuditor.sln` in Visual Studio 2022.
2. Build Debug or Release.
3. MSBuild deploys the DLL and manifest to:
   `%APPDATA%\Autodesk\Revit\Addins\2024\`
4. Restart Revit 2024.
5. Run **MM Tools > Hydronic Audit > Audit Hydronic Network**.

## Findings that drove v0.2

The first HUMAIN model export showed that many apparent HT/LT boundaries were classifier false positives caused by Marks. It also showed separated HT/LT interface geometry in DH2/DH3/DH4 at roughly 90-180 mm, while the DH1 pattern was materially farther apart. v0.2 therefore treats these as separated interface candidates rather than automatically calling them connected HT/LT boundaries.

The next project-specific layer can validate the expected DH1-DH4 interface tags against MC601-MC608 and classify each interface as expected, missing, mislabeled or unexpected.
