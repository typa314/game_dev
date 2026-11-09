using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Assets;
using Game.Core.Localization;
using Game.Domain.MasterData;

namespace Game.Core.Diagnostics
{
    /// <summary>
    /// 發行就緒綜合檢核報告 (M3-7)
    /// </summary>
    public sealed class ReleaseAuditReport
    {
        public bool IsPassed => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool StrictMode { get; set; }
        public int MasterTablesScanned { get; set; }
        public int LocalesScanned { get; set; }
        public int BaseAssetsScanned { get; set; }
        public int PlaceholderAssetCount { get; set; }
    }

    /// <summary>
    /// 發行就緒自動化綜合稽核器 (M3-7)。
    /// 發行前執行綜合防線檢查：
    /// 1. 主資料完整性 (MasterDataValidator 0 錯誤)。
    /// 2. 四大首發語系與 UI 文字長度檢查 (LocalizationValidator 0 錯誤)。
    /// 3. 本體 Catalog R18 零洩漏 (AddressablesCatalogAuditor 0 錯誤)。
    /// 4. 發行模式 (strictReleaseMode) 下嚴格禁止任何 placeholder 佔位素材。
    /// </summary>
    public static class ReleaseReadinessAuditor
    {
        public static ReleaseAuditReport RunAudit(
            MasterDatabase master,
            string locDir,
            string assetManifestPath,
            bool strictReleaseMode = false)
        {
            var report = new ReleaseAuditReport { StrictMode = strictReleaseMode };

            // 1. 主資料校驗 (MasterDataValidator)
            if (master == null)
            {
                report.Errors.Add("ERR_MASTER_NULL: MasterDatabase 未載入或為空");
            }
            else
            {
                var masterErrors = MasterDataValidator.Validate(master);
                report.MasterTablesScanned = 9;
                if (masterErrors != null && masterErrors.Count > 0)
                {
                    foreach (var err in masterErrors)
                    {
                        report.Errors.Add($"ERR_MASTER_VALIDATION: {err}");
                    }
                }
            }

            // 2. 多語系完整性與 UI 長度驗證 (LocalizationValidator)
            if (string.IsNullOrEmpty(locDir) || !Directory.Exists(locDir))
            {
                report.Errors.Add($"ERR_LOC_DIR_NOT_FOUND: 多語言資料夾不存在: {locDir}");
            }
            else
            {
                var locService = new LocalizationService("zh-TW");
                locService.LoadFromDirectory(locDir);
                string[] requiredLocales = new[] { "zh-TW", "en", "ja", "zh-CN" };
                report.LocalesScanned = requiredLocales.Length;

                foreach (var locale in requiredLocales)
                {
                    if (!locService.AvailableLocales.Contains(locale))
                    {
                        report.Errors.Add($"ERR_LOCALE_MISSING: 缺少必備語系字典 '{locale}'");
                    }
                }

                var locVal = LocalizationValidator.ValidateUiLengthAndTags(locService);
                if (!locVal.IsValid)
                {
                    foreach (var err in locVal.Errors)
                    {
                        report.Errors.Add($"ERR_LOCALIZATION: {err}");
                    }
                }
            }

            // 3. 本體 Catalog R18 零洩漏 (AddressablesCatalogAuditor)
            if (string.IsNullOrEmpty(assetManifestPath) || !File.Exists(assetManifestPath))
            {
                report.Errors.Add($"ERR_MANIFEST_NOT_FOUND: 資產清單檔案不存在: {assetManifestPath}");
            }
            else
            {
                var catalog = new AssetCatalog();
                catalog.LoadFromFile(assetManifestPath);

                var splitResult = AddressablesCatalogAuditor.BuildSplitManifests(catalog);
                report.BaseAssetsScanned = splitResult.BaseCatalog.Entries.Count;

                if (!splitResult.BaseAuditReport.IsValid)
                {
                    foreach (var err in splitResult.BaseAuditReport.Errors)
                    {
                        report.Errors.Add($"ERR_R18_LEAK_IN_BASE: {err}");
                    }
                }

                // 4. 發行模式下檢查是否有 placeholder 佔位資產
                int placeholderCount = 0;
                foreach (var kvp in catalog.Entries)
                {
                    if (string.Equals(kvp.Value.Status, "placeholder", StringComparison.OrdinalIgnoreCase))
                    {
                        placeholderCount++;
                    }
                }
                report.PlaceholderAssetCount = placeholderCount;

                if (strictReleaseMode && placeholderCount > 0)
                {
                    report.Errors.Add($"ERR_PLACEHOLDER_IN_RELEASE: 發行模式嚴格禁止包含 placeholder 佔位素材（共偵測到 {placeholderCount} 筆未完成資產）");
                }
                else if (placeholderCount > 0)
                {
                    report.Warnings.Add($"開發模式提示：偵測到 {placeholderCount} 筆 placeholder 佔位素材，正式發行前需全數替換。");
                }
            }

            return report;
        }
    }
}
