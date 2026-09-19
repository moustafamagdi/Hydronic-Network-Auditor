using System;
using System.IO;
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
            catch (Exception ex)
            {
                string errorLog = TryWriteErrorLog(ex);
                message = string.IsNullOrWhiteSpace(errorLog)
                    ? ex.ToString()
                    : ex + Environment.NewLine + Environment.NewLine + "Error log: " + errorLog;

                return Result.Failed;
            }
        }

        private static string TryWriteErrorLog(Exception exception)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string root = Path.Combine(desktop, "HydronicNetworkAuditor");
                Directory.CreateDirectory(root);

                string path = Path.Combine(
                    root,
                    "error_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");

                File.WriteAllText(path, exception.ToString());
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
