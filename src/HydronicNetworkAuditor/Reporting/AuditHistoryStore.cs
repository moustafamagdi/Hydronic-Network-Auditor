using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HydronicNetworkAuditor.Core;

namespace HydronicNetworkAuditor.Reporting
{
    public sealed class AuditHistoryStore
    {
        public void ApplyDelta(AuditResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            string statePath = GetStatePath(result);
            var currentKeys = new HashSet<string>(
                GetTrackableIssueKeys(result),
                StringComparer.OrdinalIgnoreCase);

            var delta = new AuditDelta
            {
                CurrentIssueCount = currentKeys.Count
            };

            if (!File.Exists(statePath))
            {
                result.Diagnostics.Delta = delta;
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(statePath);
                if (lines.Length == 0)
                {
                    result.Diagnostics.Delta = delta;
                    return;
                }

                DateTime previousGeneratedAt;
                if (DateTime.TryParse(lines[0], out previousGeneratedAt))
                    delta.PreviousGeneratedAt = previousGeneratedAt;

                var previousKeys = new HashSet<string>(
                    lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)),
                    StringComparer.OrdinalIgnoreCase);

                delta.HasPreviousRun = true;
                delta.PreviousIssueCount = previousKeys.Count;

                foreach (string key in currentKeys.Except(previousKeys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
                    delta.NewIssueKeys.Add(key);

                foreach (string key in previousKeys.Except(currentKeys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k))
                    delta.ResolvedIssueKeys.Add(key);

                delta.NewIssueCount = delta.NewIssueKeys.Count;
                delta.ResolvedIssueCount = delta.ResolvedIssueKeys.Count;
                delta.PersistingIssueCount = currentKeys.Intersect(previousKeys, StringComparer.OrdinalIgnoreCase).Count();
            }
            catch
            {
                // History comparison is advisory. A bad state file must never fail the audit.
            }

            result.Diagnostics.Delta = delta;
        }

        public void Save(AuditResult result)
        {
            if (result == null) return;

            try
            {
                string path = GetStatePath(result);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var lines = new List<string>
                {
                    result.GeneratedAt.ToString("o")
                };

                lines.AddRange(GetTrackableIssueKeys(result)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k));

                File.WriteAllLines(path, lines);
            }
            catch
            {
                // Audit export remains valid even if history persistence fails.
            }
        }

        private static IEnumerable<string> GetTrackableIssueKeys(AuditResult result)
        {
            return result.Diagnostics.Issues
                .Where(i =>
                    string.Equals(i.IssueType, "Flow Classification Conflict", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.IssueType, "Probable Family Connector Misclassification", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.IssueType, "Direct HT/LT Boundary", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.IssueType, "HT/LT Interface Gap", StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Key);
        }

        private static string GetStatePath(AuditResult result)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string root = Path.Combine(desktop, "HydronicNetworkAuditor");
            string safeName = SanitizeFileName(result.DocumentTitle);
            return Path.Combine(root, "last_diagnostics_" + safeName + ".txt");
        }

        private static string SanitizeFileName(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "model" : value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                text = text.Replace(invalid, '_');

            if (text.Length > 80)
                text = text.Substring(0, 80);

            return text;
        }
    }
}
