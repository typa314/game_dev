using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core.Localization
{
    public static class LocalizationCsvTool
    {
        public static string InferContext(string key)
        {
            if (string.IsNullOrEmpty(key)) return "General";
            if (key.StartsWith("char/", StringComparison.OrdinalIgnoreCase)) return "Character";
            if (key.StartsWith("stage/", StringComparison.OrdinalIgnoreCase)) return "Stage";
            if (key.StartsWith("ach/", StringComparison.OrdinalIgnoreCase)) return "Achievement";
            if (key.StartsWith("gacha/", StringComparison.OrdinalIgnoreCase)) return "Gacha";
            if (key.StartsWith("scene/", StringComparison.OrdinalIgnoreCase)) return "Scene";
            if (key.StartsWith("story/", StringComparison.OrdinalIgnoreCase)) return "Story Line";
            if (key.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)) return "UI Text";
            return "General";
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        public static string ExportToCsv(ILocalizationService loc, string baseLocale = "zh-TW")
        {
            if (loc == null) throw new ArgumentNullException(nameof(loc));

            var locales = loc.AvailableLocales.ToList();
            if (!locales.Contains(baseLocale)) locales.Insert(0, baseLocale);

            var allKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string l in locales)
            {
                var table = loc.GetTable(l);
                if (table != null)
                {
                    foreach (string k in table.Keys) allKeys.Add(k);
                }
            }

            var orderedKeys = allKeys.OrderBy(x => x, StringComparer.Ordinal).ToList();

            var sb = new StringBuilder();
            sb.Append("Key,Context");
            foreach (string l in locales)
            {
                sb.Append(",").Append(EscapeCsvField(l));
            }
            sb.AppendLine();

            foreach (string k in orderedKeys)
            {
                sb.Append(EscapeCsvField(k));
                sb.Append(",").Append(EscapeCsvField(InferContext(k)));

                foreach (string l in locales)
                {
                    var table = loc.GetTable(l);
                    string text = table != null && table.TryGetValue(k, out string v) ? v : "";
                    sb.Append(",").Append(EscapeCsvField(text));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static Dictionary<string, Dictionary<string, string>> ImportFromCsv(string csvContent)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csvContent)) return result;

            using (var reader = new StringReader(csvContent))
            {
                string headerLine = reader.ReadLine();
                if (headerLine == null) return result;

                var headers = ParseCsvLine(headerLine);
                if (headers.Count < 2) return result;

                // 識別各語言欄位索引
                var localeCols = new List<(string Locale, int Index)>();
                for (int i = 2; i < headers.Count; i++)
                {
                    string loc = headers[i].Trim();
                    if (!string.IsNullOrEmpty(loc))
                    {
                        localeCols.Add((loc, i));
                        result[loc] = new Dictionary<string, string>(StringComparer.Ordinal);
                    }
                }

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var fields = ParseCsvLine(line);
                    if (fields.Count == 0) continue;

                    string key = fields[0].Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    foreach (var (locale, colIdx) in localeCols)
                    {
                        if (colIdx < fields.Count)
                        {
                            string text = fields[colIdx];
                            if (!string.IsNullOrEmpty(text))
                            {
                                result[locale][key] = text;
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static void SaveImportedToDirectory(
            Dictionary<string, Dictionary<string, string>> tables,
            string locRootDir)
        {
            if (tables == null || string.IsNullOrWhiteSpace(locRootDir)) return;

            foreach (var kvp in tables)
            {
                string locale = kvp.Key;
                var dict = kvp.Value;
                string dir = Path.Combine(locRootDir, locale);
                Directory.CreateDirectory(dir);

                string filePath = Path.Combine(dir, "strings.json");
                string json = JsonConvert.SerializeObject(dict, Formatting.Indented);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        cur.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(cur.ToString());
                    cur.Clear();
                }
                else
                {
                    cur.Append(c);
                }
            }
            fields.Add(cur.ToString());
            return fields;
        }
    }
}
