using System;
using System.Collections.Generic;

namespace Game.Core.Localization
{
    /// <summary>
    /// 多語言服務介面（design/12_localization.md、design/17 §D1）。
    /// </summary>
    public interface ILocalizationService
    {
        string CurrentLocale { get; }
        string FallbackLocale { get; }
        IReadOnlyList<string> AvailableLocales { get; }
        IReadOnlyList<string> Warnings { get; }

        event Action<string> LocaleChanged;

        void SetLocale(string locale);
        string Get(string key, params object[] args);
        bool Has(string key, string locale = null);
        void LoadLocale(string locale, IDictionary<string, string> entries);
        void LoadFromJson(string locale, string jsonText);
        void LoadFromDirectory(string rootDir);
        IReadOnlyDictionary<string, string> GetTable(string locale);
    }
}
