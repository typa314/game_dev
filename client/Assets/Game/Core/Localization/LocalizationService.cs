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

    /// <summary>
    /// 統一顯示名稱與多語系鍵值解析器，避免各 UI View 硬編碼角色名單或將 loc: 鍵值原樣輸出。
    /// 當外部尚未安裝 ILocalizationService 時，自動惰性載入磁碟語系表作為備援。
    /// </summary>
    public static class LocalizationResolver
    {
        private static readonly object FallbackLock = new object();
        private static LocalizationService _fallbackService;
        private static bool _fallbackSearched;

        public static LocalizationService GetFallbackService()
        {
            lock (FallbackLock)
            {
                if (_fallbackSearched) return _fallbackService;
                _fallbackSearched = true;
                string dir = FindLocaleDirectory();
                if (!string.IsNullOrEmpty(dir))
                {
                    var svc = new LocalizationService("zh-TW", "zh-TW");
                    svc.LoadFromDirectory(dir);
                    _fallbackService = svc;
                }
                return _fallbackService;
            }
        }

        public static bool TryResolveKey(ILocalizationService loc, string rawOrKey, out string resolved)
        {
            resolved = null;
            if (string.IsNullOrEmpty(rawOrKey)) return false;
            ILocalizationService active = loc ?? GetFallbackService();
            if (active == null) return false;

            string norm = LocalizationService.NormalizeKey(rawOrKey);
            if (active.Has(norm) || active.Has(norm, active.FallbackLocale))
            {
                resolved = active.Get(norm);
                return true;
            }
            return false;
        }

        public static string Resolve(ILocalizationService loc, string rawOrKey, string fallbackText = null)
        {
            if (TryResolveKey(loc, rawOrKey, out string resolved)) return resolved;
            if (!string.IsNullOrEmpty(rawOrKey) && !rawOrKey.StartsWith("loc:", StringComparison.OrdinalIgnoreCase))
            {
                return rawOrKey;
            }
            return !string.IsNullOrEmpty(fallbackText) ? fallbackText : FormatIdFallback(rawOrKey);
        }

        public static string ResolveCharacterName(
            ILocalizationService loc,
            string charId,
            Game.Domain.MasterData.CharacterDef def = null)
        {
            if (def != null && !string.IsNullOrEmpty(def.Name))
            {
                if (TryResolveKey(loc, def.Name, out string defResolved)) return defResolved;
                if (!def.Name.StartsWith("loc:", StringComparison.OrdinalIgnoreCase)) return def.Name;
            }

            if (!string.IsNullOrEmpty(charId))
            {
                if (TryResolveKey(loc, $"char/{charId}.name", out string idResolved)) return idResolved;
                string aliasKey = MapLegacyCharacterLocKey(charId);
                if (aliasKey != null && TryResolveKey(loc, aliasKey, out string aliasResolved)) return aliasResolved;
                return FormatIdFallback(charId);
            }

            return "HERO";
        }

        public static string ResolveEnemyName(
            ILocalizationService loc,
            string enemyId,
            Game.Domain.MasterData.EnemyDef def = null)
        {
            if (def != null && !string.IsNullOrEmpty(def.Name))
            {
                if (TryResolveKey(loc, def.Name, out string defResolved)) return defResolved;
                if (!def.Name.StartsWith("loc:", StringComparison.OrdinalIgnoreCase)) return def.Name;
            }

            if (!string.IsNullOrEmpty(enemyId))
            {
                if (TryResolveKey(loc, $"enemy/{enemyId}.name", out string idResolved)) return idResolved;
                return FormatIdFallback(enemyId);
            }

            return "ENEMY";
        }

        public static string ResolveUnitShortName(
            ILocalizationService loc,
            string unitId,
            Game.Domain.MasterData.MasterDatabase master = null)
        {
            if (string.IsNullOrEmpty(unitId)) return "UNIT";
            if (master != null)
            {
                if (master.Characters != null && master.Characters.TryGetValue(unitId, out var cDef))
                    return ResolveCharacterName(loc, unitId, cDef);
                if (master.Enemies != null && master.Enemies.TryGetValue(unitId, out var eDef))
                    return ResolveEnemyName(loc, unitId, eDef);
            }
            if (unitId.StartsWith("en_", StringComparison.OrdinalIgnoreCase))
                return ResolveEnemyName(loc, unitId, null);
            return ResolveCharacterName(loc, unitId, null);
        }

        public static string FormatIdFallback(string idOrKey)
        {
            if (string.IsNullOrEmpty(idOrKey)) return "UNIT";
            string s = LocalizationService.NormalizeKey(idOrKey);
            if (s.StartsWith("char/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            if (s.StartsWith("enemy/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
            if (s.EndsWith(".name", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5);
            if (s.StartsWith("char", StringComparison.OrdinalIgnoreCase) &&
                s.Length > 7 &&
                char.IsDigit(s[4]) &&
                char.IsDigit(s[5]) &&
                s[6] == '_')
            {
                s = s.Substring(7);
            }
            else if (s.StartsWith("en_", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(3);
            }
            return s.Replace("_", " ").Trim();
        }

        private static string MapLegacyCharacterLocKey(string charId)
        {
            switch (charId)
            {
                case "char07_ayame": return "char/char26_ayame_ninja.name";
                case "char08_sakura":
                case "char18_sakura": return "char/char18_sakura_miko.name";
                case "char09_yuki": return "char/char08_yuki.name";
                default: return null;
            }
        }

        private static string FindLocaleDirectory()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                if (string.IsNullOrEmpty(start)) continue;
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string saLoc = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Loc");
                    if (Directory.Exists(saLoc)) return saLoc;
                    string clientSaLoc = Path.Combine(dir.FullName, "client", "Assets", "StreamingAssets", "Loc");
                    if (Directory.Exists(clientSaLoc)) return clientSaLoc;
                    string dataLoc = Path.Combine(dir.FullName, "data", "loc");
                    if (Directory.Exists(dataLoc)) return dataLoc;
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }
}
