using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Domain.MasterData;

namespace Game.Core.Localization
{
    public sealed class LocalizationValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    public static class LocalizationValidator
    {
        private static readonly Regex PlaceholderRegex = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        public static LocalizationValidationResult Validate(
            ILocalizationService loc,
            MasterDatabase master = null,
            string baseLocale = "zh-TW")
        {
            var result = new LocalizationValidationResult();
            if (loc == null)
            {
                result.Errors.Add("LocalizationService is null.");
                return result;
            }

            var baseTable = loc.GetTable(baseLocale);
            if (baseTable == null || baseTable.Count == 0)
            {
                result.Errors.Add($"Base locale '{baseLocale}' has no entries or table is empty.");
                return result;
            }

            // 1. 檢查各語言與 baseLocale 之間的缺漏 key
            foreach (string locale in loc.AvailableLocales)
            {
                if (string.Equals(locale, baseLocale, StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetTable = loc.GetTable(locale);

                foreach (var baseKvp in baseTable)
                {
                    string key = baseKvp.Key;
                    if (!targetTable.TryGetValue(key, out string targetText))
                    {
                        result.Warnings.Add($"ERR_MISSING_TRANSLATION: Locale '{locale}' is missing key '{key}' present in base locale '{baseLocale}'.");
                        continue;
                    }

                    // 2. 佔位符 {0}, {1} 比對
                    var basePlaceholders = ExtractPlaceholders(baseKvp.Value);
                    var targetPlaceholders = ExtractPlaceholders(targetText);

                    if (!basePlaceholders.SetEquals(targetPlaceholders))
                    {
                        result.Errors.Add($"ERR_PLACEHOLDER_MISMATCH: Key '{key}' in locale '{locale}' placeholder mismatch with base locale '{baseLocale}'. Base: '{baseKvp.Value}', Translation: '{targetText}'.");
                    }
                }
            }

            // 3. 檢查主資料庫中所有 loc: 參照之 key 是否存在於 baseLocale
            if (master != null)
            {
                var referencedKeys = CollectMasterLocKeys(master);
                foreach (string rawKey in referencedKeys)
                {
                    string normKey = LocalizationService.NormalizeKey(rawKey);
                    if (!baseTable.ContainsKey(normKey))
                    {
                        result.Errors.Add($"ERR_MASTER_KEY_NOT_FOUND: MasterData references key '{rawKey}', but it does not exist in base locale '{baseLocale}'.");
                    }
                }
            }

            return result;
        }

        private static HashSet<int> ExtractPlaceholders(string text)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrEmpty(text)) return set;

            var matches = PlaceholderRegex.Matches(text);
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int idx))
                {
                    set.Add(idx);
                }
            }
            return set;
        }

        public static HashSet<string> CollectMasterLocKeys(MasterDatabase master)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (master == null) return set;

            void AddIfLoc(string s)
            {
                if (!string.IsNullOrEmpty(s) && s.StartsWith("loc:", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(s);
                }
            }

            foreach (var c in master.Characters.Values) AddIfLoc(c.Name);
            foreach (var e in master.Enemies.Values) AddIfLoc(e.Name);
            foreach (var s in master.Stages.Values) AddIfLoc(s.Name);
            foreach (var sp in master.Spots.Values) AddIfLoc(sp.Name);
            foreach (var sc in master.Scenes.Values) AddIfLoc(sc.Name);
            foreach (var b in master.Banners.Values) AddIfLoc(b.Name);
            foreach (var a in master.Achievements.Values)
            {
                AddIfLoc(a.Name);
                AddIfLoc(a.Desc);
            }

            return set;
        }

        private static readonly Regex RichTextTagRegex = new Regex(@"<(/?[a-zA-Z]+)(?:=[^>]*)?>", RegexOptions.Compiled);

        public static LocalizationValidationResult ValidateUiLengthAndTags(
            ILocalizationService loc,
            int maxUiButtonChars = 40,
            int maxGeneralChars = 200)
        {
            var result = new LocalizationValidationResult();
            if (loc == null)
            {
                result.Errors.Add("LocalizationService is null.");
                return result;
            }

            foreach (string locale in loc.AvailableLocales)
            {
                var table = loc.GetTable(locale);
                if (table == null) continue;

                foreach (var kvp in table)
                {
                    string key = kvp.Key;
                    string text = kvp.Value ?? string.Empty;

                    // 1. UI 與一般文字長度上限檢查
                    if (key.StartsWith("ui.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ui/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (text.Length > maxUiButtonChars)
                        {
                            result.Errors.Add($"ERR_UI_LENGTH_EXCEEDED: Key '{key}' in locale '{locale}' length {text.Length} exceeds limit of {maxUiButtonChars}. Text: '{text}'");
                        }
                    }
                    else
                    {
                        if (text.Length > maxGeneralChars)
                        {
                            result.Errors.Add($"ERR_GENERAL_LENGTH_EXCEEDED: Key '{key}' in locale '{locale}' length {text.Length} exceeds limit of {maxGeneralChars}. Text: '{text}'");
                        }
                    }

                    // 2. 富文字標籤成對閉合檢查 (<b>...</b>, <i>...</i>, <color=...>...</color>, <size=...>...</size>)
                    string tagError = ValidateRichTextTags(text);
                    if (!string.IsNullOrEmpty(tagError))
                    {
                        result.Errors.Add($"ERR_UNCLOSED_TAG: Key '{key}' in locale '{locale}' has malformed rich text tags: {tagError}. Text: '{text}'");
                    }
                }
            }

            return result;
        }

        public static string ValidateRichTextTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            var matches = RichTextTagRegex.Matches(text);
            if (matches.Count == 0) return null;

            var stack = new Stack<string>();
            var knownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b", "i", "color", "size" };

            foreach (Match m in matches)
            {
                string rawTag = m.Groups[1].Value;
                if (rawTag.StartsWith("/"))
                {
                    string closeTag = rawTag.Substring(1).ToLowerInvariant();
                    if (!knownTags.Contains(closeTag)) continue;

                    if (stack.Count == 0)
                    {
                        return $"Unexpected closing tag '</{closeTag}>' with no matching open tag";
                    }

                    string expected = stack.Pop();
                    if (!string.Equals(expected, closeTag, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Mismatched closing tag '</{closeTag}>', expected '</{expected}>'";
                    }
                }
                else
                {
                    string openTag = rawTag.ToLowerInvariant();
                    if (knownTags.Contains(openTag))
                    {
                        stack.Push(openTag);
                    }
                }
            }

            if (stack.Count > 0)
            {
                return $"Unclosed tag '<{stack.Peek()}>'";
            }

            return null;
        }
    }
}
