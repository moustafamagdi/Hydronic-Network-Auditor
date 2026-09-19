using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;

namespace HydronicNetworkAuditor
{
    public sealed class App : IExternalApplication
    {
        private const string TabName = "MM Tools";
        private const string PanelName = "Hydronic Audit";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                try { application.CreateRibbonTab(TabName); }
                catch { /* Tab already exists. */ }

                RibbonPanel panel = application
                    .GetRibbonPanels(TabName)
                    .FirstOrDefault(p => string.Equals(p.Name, PanelName, StringComparison.OrdinalIgnoreCase))
                    ?? application.CreateRibbonPanel(TabName, PanelName);

                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    "HydronicNetworkAuditor.Audit",
                    "Audit Hydronic\nNetwork",
                    assemblyPath,
                    "HydronicNetworkAuditor.Commands.AuditNetworkCommand")
                {
                    ToolTip = "Build a connector graph, identify HT/LT and Supply/Return evidence, and export an audit report."
                };

                if (!panel.GetItems().Any(i => i.Name == buttonData.Name))
                    panel.AddItem(buttonData);

                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
