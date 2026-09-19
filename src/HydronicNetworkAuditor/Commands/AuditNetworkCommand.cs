using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HydronicNetworkAuditor.Core;
using HydronicNetworkAuditor.Reporting;
using HydronicNetworkAuditor.UI;

namespace HydronicNetworkAuditor.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class AuditNetworkCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIDocument uiDocument = commandData.Application.ActiveUIDocument;
                if (uiDocument == null)
                {
                    message = "No active Revit document.";
                    return Result.Failed;
                }

                Document document = uiDocument.Document;

                var builder = new ConnectorGraphBuilder();
                AuditResult audit = builder.Build(document);

                var analyzer = new NetworkAnalyzer();
                analyzer.Analyze(audit);

                var exporter = new ReportExporter();
                ExportBundle export = exporter.Export(audit);

                var window = new AuditSummaryWindow(audit, export);
                window.Show();

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }
}
