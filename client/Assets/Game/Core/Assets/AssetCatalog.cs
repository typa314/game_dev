using System;
using System.Collections.Generic;
using System.IO;
using Game.Domain;

namespace Game.Core.Assets
{
    public sealed class AssetManifestEntry
    {
        public string Key { get; set; }
        public string Source { get; set; }
        public Rating Rating { get; set; } = Rating.G;
        public string Status { get; set; } = "placeholder"; // placeholder, draft, final
        public string Note { get; set; }
    }

    public sealed class AssetResolution
    {
        public string Key { get; set; }
        public bool Allowed { get; set; }
        public bool IsPlaceholder { get; set; }
        public AssetManifestEntry Entry { get; set; }
        public string Reason { get; set; }
    }

    public sealed class AssetCatalog
    {
        private readonly Dictionary<string, AssetManifestEntry> _entries =
            new Dictionary<string, AssetManifestEntry>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, AssetManifestEntry> Entries => _entries;

        public void LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            string csv = File.ReadAllText(filePath);
            LoadFromCsv(csv);
        }

        public void LoadFromCsv(string csvContent)
        {
            if (string.IsNullOrWhiteSpace(csvContent)) return;

            using (var reader = new StringReader(csvContent))
            {
                string header = reader.ReadLine();
                if (header == null) return;

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    string[] parts = SplitCsvLine(trimmed);
                    if (parts.Length < 3) continue;

                    string key = parts[0].Trim();
                    string source = parts[1].Trim();
                    string ratingStr = parts[2].Trim();
                    string status = parts.Length > 3 ? parts[3].Trim() : "draft";
                    string note = parts.Length > 4 ? parts[4].Trim() : string.Empty;

                    Rating rating = Rating.G;
                    if (Enum.TryParse<Rating>(ratingStr, true, out var parsedRating))
                    {
                        rating = parsedRating;
                    }

                    _entries[key] = new AssetManifestEntry
                    {
                        Key = key,
                        Source = source,
                        Rating = rating,
                        Status = status,
                        Note = note
                    };
                }
            }
        }

        public AssetResolution Resolve(string key, RatingPolicy policy)
        {
            if (string.IsNullOrEmpty(key))
            {
                return new AssetResolution
                {
                    Key = key,
                    Allowed = false,
                    IsPlaceholder = true,
                    Reason = "EmptyKey"
                };
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                return new AssetResolution
                {
                    Key = key,
                    Allowed = false,
                    IsPlaceholder = true,
                    Reason = "NotFoundInManifest"
                };
            }

            if (policy != null && !policy.Allows(entry.Rating))
            {
                return new AssetResolution
                {
                    Key = key,
                    Allowed = false,
                    IsPlaceholder = true,
                    Entry = entry,
                    Reason = "RatingDisallowed"
                };
            }

            bool isPlaceholder = string.Equals(entry.Status, "placeholder", StringComparison.OrdinalIgnoreCase);
            return new AssetResolution
            {
                Key = key,
                Allowed = true,
                IsPlaceholder = isPlaceholder,
                Entry = entry,
                Reason = "Ok"
            };
        }

        private static string[] SplitCsvLine(string line)
        {
            var parts = new List<string>();
            bool inQuotes = false;
            int start = 0;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    string part = line.Substring(start, i - start).Trim().Trim('"');
                    parts.Add(part);
                    start = i + 1;
                }
            }

            if (start <= line.Length)
            {
                string part = line.Substring(start).Trim().Trim('"');
                parts.Add(part);
            }

            return parts.ToArray();
        }
    }
}
