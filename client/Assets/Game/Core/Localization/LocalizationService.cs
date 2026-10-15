using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core.Localization
{
    public sealed class LocalizationService : ILocalizationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _warnings = new List<string>();
        private readonly HashSet<string> _warnedMissingKeys = new HashSet<string>(StringComparer.Ordinal);

        public string CurrentLocale { get; private set; } = "zh-TW";
        public string FallbackLocale { get; set; } = "zh-TW";

        public IReadOnlyList<string> AvailableLocales => _tables.Keys.ToList();
        public IReadOnlyList<string> Warnings => _warnings;

        public event Action<string> LocaleChanged;

        public LocalizationService(string defaultLocale = "zh-TW", string fallbackLocale = "zh-TW")
        {
            CurrentLocale = defaultLocale ?? "zh-TW";
            FallbackLocale = fallbackLocale ?? "zh-TW";
        }

        public void SetLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return;
            if (string.Equals(CurrentLocale, locale, StringComparison.OrdinalIgnoreCase)) return;

            CurrentLocale = locale;
            LocaleChanged?.Invoke(CurrentLocale);
        }

        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return key.StartsWith("loc:", StringComparison.OrdinalIgnoreCase)
                ? key.Substring(4)
                : key;
        }

        public bool Has(string key, string locale = null)
        {
            string loc = locale ?? CurrentLocale;
            string normKey = NormalizeKey(key);

            if (_tables.TryGetValue(loc, out var table) && table.ContainsKey(normKey))
            {
                return true;
            }

            return false;
        }

        public string Get(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string normKey = NormalizeKey(key);

            // 1. 嘗試從目前語言取得
            if (_tables.TryGetValue(CurrentLocale, out var curTable) && curTable.TryGetValue(normKey, out string text))
            {
                return FormatArgs(text, args);
            }

            // 2. 退回至 Fallback 語言
            if (!string.Equals(CurrentLocale, FallbackLocale, StringComparison.OrdinalIgnoreCase))
            {
                string warnKey = $"{CurrentLocale}:{normKey}";
                if (_warnedMissingKeys.Add(warnKey))
                {
                    _warnings.Add($"[Localization] Missing key '{normKey}' in locale '{CurrentLocale}', falling back to '{FallbackLocale}'.");
                }

                if (_tables.TryGetValue(FallbackLocale, out var fbTable) && fbTable.TryGetValue(normKey, out string fbText))
                {
                    return FormatArgs(fbText, args);
                }
            }

            // 3. Fallback 語言亦不存在，退回 [key]
            return $"[{normKey}]";
        }

        private static string FormatArgs(string template, object[] args)
        {
            if (args == null || args.Length == 0) return template;
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        public void LoadLocale(string locale, IDictionary<string, string> entries)
        {
            if (string.IsNullOrWhiteSpace(locale) || entries == null) return;

            if (!_tables.TryGetValue(locale, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                _tables[locale] = table;
            }

            foreach (var kvp in entries)
            {
                table[NormalizeKey(kvp.Key)] = kvp.Value;
            }
        }

        public void LoadFromJson(string locale, string jsonText)
        {
            if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(jsonText)) return;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            JToken token = JToken.Parse(jsonText);

            if (token is JObject obj)
            {
                FlattenJObject(obj, string.Empty, dict);
            }

            LoadLocale(locale, dict);
        }

        private static void FlattenJObject(JObject obj, string prefix, Dictionary<string, string> result)
        {
            foreach (var property in obj.Properties())
            {
                string fullKey = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                if (property.Value is JValue val)
                {
                    result[fullKey] = val.Value?.ToString() ?? string.Empty;
                }
                else if (property.Value is JObject childObj)
                {
                    FlattenJObject(childObj, fullKey, result);
                }
            }
        }

        public void LoadFromDirectory(string rootDir)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir)) return;

            string[] subDirs = Directory.GetDirectories(rootDir);
            foreach (string subDir in subDirs)
            {
                string localeName = Path.GetFileName(subDir);
                string[] files = Directory.GetFiles(subDir, "*.json", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    string content = File.ReadAllText(file);
                    LoadFromJson(localeName, content);
                }
            }
        }

        public IReadOnlyDictionary<string, string> GetTable(string locale)
        {
            if (_tables.TryGetValue(locale, out var table))
            {
                return table;
            }
            return new Dictionary<string, string>();
        }
    }
}
