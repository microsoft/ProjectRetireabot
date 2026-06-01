using System.Text.RegularExpressions;
using Microsoft.RetireaBot.Models.Lifecycle;

namespace Microsoft.RetireaBot.Helpers.Lifecycle
{
    public class LifecycleParser
    {
        public static IReadOnlyList<LifecycleEntry> ParseAksVersionTable(string md, string url)
        {
            List<LifecycleEntry> entries = new List<LifecycleEntry>();
            string[] lines = md.Split("\n");

            bool tableBounds = false;

            int eolColumnIndex = -1;
            int versionColumnIndex = -1;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                if (!tableBounds
                && line.StartsWith('|')
                && line.Contains("End of life", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("LTS End of life", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Kubernetes version", StringComparison.OrdinalIgnoreCase))
                {
                    string[] headers = line.Split('|', StringSplitOptions.TrimEntries);
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (headers[i].Equals("Kubernetes version", StringComparison.OrdinalIgnoreCase))
                        {
                            versionColumnIndex = i;
                        }
                        else if (headers[i].Equals("End of life", StringComparison.OrdinalIgnoreCase))
                        {
                            eolColumnIndex = i;
                        }
                        tableBounds = true;
                        continue;
                    }
                }

                if (tableBounds && line.StartsWith("| -")) continue;
                if (tableBounds && !line.StartsWith('|')) break;
                if (!tableBounds) continue;

                string[] cols = line.Split('|', StringSplitOptions.TrimEntries);
                if (cols.Length <= Math.Max(eolColumnIndex, versionColumnIndex)) continue;

                string version = cols[versionColumnIndex];
                string eolText = cols[eolColumnIndex];

                if (TryParseEolDate(eolText, out var eolDate))
                {
                    entries.Add(new LifecycleEntry
                    {
                        Product = LifecycleProduct.AksKubernetes,
                        Version = version,
                        EndOfLife = eolDate,
                        SourceUrl = url
                    });
                }
            }

            return entries;
        }

        public static IReadOnlyList<LifecycleEntry> ParsePostgreSqlVersionTable(string md, string url)
        {
            List<LifecycleEntry> entries = new List<LifecycleEntry>();
            string[] lines = md.Split("\n");

            bool tableBounds = false;

            int eolColumnIndex = -1;
            int versionColumnIndex = -1;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                if (!tableBounds
                && line.StartsWith('|')
                && line.Contains("Azure Standard Support Start Date", StringComparison.OrdinalIgnoreCase)
                && line.Contains("Azure Standard Support End Date", StringComparison.OrdinalIgnoreCase)
                && line.Contains("PostgreSQL Version", StringComparison.OrdinalIgnoreCase))
                {
                    string[] headers = line.Split('|', StringSplitOptions.TrimEntries);
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (headers[i].Equals("PostgreSQL Version", StringComparison.OrdinalIgnoreCase))
                        {
                            versionColumnIndex = i;
                        }
                        else if (headers[i].Equals("Azure Standard Support End Date", StringComparison.OrdinalIgnoreCase))
                        {
                            eolColumnIndex = i;
                        }
                        tableBounds = true;
                        continue;
                    }
                }

                if (tableBounds && line.StartsWith("| -")) continue;
                if (tableBounds && !line.StartsWith('|')) break;
                if (!tableBounds) continue;

                string[] cols = line.Split('|', StringSplitOptions.TrimEntries);
                if (cols.Length <= Math.Max(eolColumnIndex, versionColumnIndex)) continue;

                string version = ExtractPostgreSqlVersion(cols[versionColumnIndex]) ?? cols[versionColumnIndex];
                string eolText = cols[eolColumnIndex];

                if (TryParseEolDate(eolText, out var eolDate))
                {
                    entries.Add(new LifecycleEntry
                    {
                        Product = LifecycleProduct.AzurePostgreSQLFlexible,
                        Version = version,
                        EndOfLife = eolDate,
                        SourceUrl = url
                    });
                }
            }

            return entries;
        }

        private static bool TryParseEolDate(string text, out DateTime result)
        {
            text = text.Trim();

            // PostGreSQL tend to use: "12-Sep-2025"
            if (DateTime.TryParseExact(text, "d-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
                return true;

            // Try the first AKS format: "Aug 22, 2025"
            if (DateTime.TryParseExact(text, "MMM d, yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
                return true;

            // Try month+year: "Mar 2026" → last day of that month
            if (DateTime.TryParseExact(text, "MMM yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var monthYear))
            {
                result = new DateTime(monthYear.Year, monthYear.Month,
                    DateTime.DaysInMonth(monthYear.Year, monthYear.Month));
                return true;
            }

            result = default;
            return false;
        }

        private static string? ExtractPostgreSqlVersion(string text)
        {
            var match = Regex.Match(text, @"\[?\s*PostgreSQL\s+([\d.]+)\s*\]?", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}