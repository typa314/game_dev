using System;
using System.Collections.Generic;
using Game.Domain;

namespace Game.Core.Assets
{
    /// <summary>
    /// Addressables 條目模型（支援純 C# 離線校驗與 Unity Addressables 整合）
    /// </summary>
    public sealed class AddressablesEntry
    {
        public string AddressKey { get; set; }
        public string AssetPath { get; set; }
        public string GroupName { get; set; }
        public Rating Rating { get; set; } = Rating.G;
        public List<string> Labels { get; } = new List<string>();

        public AddressablesEntry() { }

        public AddressablesEntry(string key, string path, string group, Rating rating, params string[] labels)
        {
            AddressKey = key;
            AssetPath = path;
            GroupName = group;
            Rating = rating;
            if (labels != null) Labels.AddRange(labels);
        }
    }

    /// <summary>
    /// Addressables Catalog 模型（對應 catalog.json）
    /// </summary>
    public sealed class AddressablesCatalog
    {
        public string CatalogId { get; set; } = "catalog_base";
        public List<AddressablesEntry> Entries { get; } = new List<AddressablesEntry>();

        public void AddEntry(AddressablesEntry entry)
        {
            if (entry != null) Entries.Add(entry);
        }
    }

    /// <summary>
    /// Addressables 審計報告
    /// </summary>
    public sealed class AddressablesAuditReport
    {
        public string CatalogId { get; set; }
        public int TotalScanned { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Addressables 分級 Group 隔離審計器 (F7)
    /// 依據 ADR-003：
    /// 1. content_g 與 content_r15 納入主遊戲本體 catalog。
    /// 2. content_r18 獨立打包為 DLC catalog，嚴禁混入主遊戲。
    /// 3. CI 閘門：建置本體後掃描 catalog，發現任何 R18 key、R18 路徑或 R18 label 立即讓建置失敗。
    /// </summary>
    public static class AddressablesCatalogAuditor
    {
        public const string GroupContentG = "content_g";
        public const string GroupContentR15 = "content_r15";
        public const string GroupContentR18 = "content_r18";

        private static readonly string[] R18FolderPatterns = new[]
        {
            "tier2_tease/",
            "tier3_climax_poses/",
            "tier4_aftermath/",
            "dlc_r18/",
            "content_r18/",
            "adult/"
        };

        /// <summary>
        /// 審計本體 Catalog，確保 100% 不含任何 R18 素材
        /// </summary>
        public static AddressablesAuditReport AuditBaseCatalog(AddressablesCatalog catalog)
        {
            var report = new AddressablesAuditReport
            {
                CatalogId = catalog?.CatalogId ?? "unknown"
            };

            if (catalog == null || catalog.Entries.Count == 0)
            {
                report.Warnings.Add("Catalog 為空，未掃描到任何 Addressable 條目。");
                return report;
            }

            report.TotalScanned = catalog.Entries.Count;

            foreach (var entry in catalog.Entries)
            {
                string group = entry.GroupName ?? string.Empty;
                string path = (entry.AssetPath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
                string key = entry.AddressKey ?? string.Empty;

                // 1. 檢查 Group 名稱：R18 Group 絕對禁止進入本體 Catalog
                if (string.Equals(group, GroupContentR18, StringComparison.OrdinalIgnoreCase))
                {
                    report.Errors.Add($"ERR_R18_GROUP_IN_BASE_CATALOG: 本體 Catalog 禁止包含 R18 群組 '{group}'（條目: {key}）");
                }

                // 2. 檢查 Rating 欄位
                if (entry.Rating >= Rating.R18)
                {
                    report.Errors.Add($"ERR_R18_KEY_IN_BASE_CATALOG: 本體 Catalog 偵測到 R18 標註資產 '{key}'（群組: {group}, Rating: {entry.Rating}）");
                }

                // 3. 檢查實體檔案路徑是否來自 R18 資料夾
                foreach (var folder in R18FolderPatterns)
                {
                    if (path.Contains(folder))
                    {
                        report.Errors.Add($"ERR_R18_FOLDER_IN_BASE_CATALOG: 本體資產 '{key}' 位於 R18 資料夾下: {entry.AssetPath}");
                        break;
                    }
                }

                // 4. 檢查標籤 (Labels)
                if (entry.Labels != null)
                {
                    foreach (var lbl in entry.Labels)
                    {
                        string lowerLbl = lbl?.ToLowerInvariant() ?? string.Empty;
                        if (lowerLbl == "r18" || lowerLbl == "nsfw" || lowerLbl == "adult")
                        {
                            report.Errors.Add($"ERR_R18_LABEL_IN_BASE_CATALOG: 本體資產 '{key}' 帶有成人標籤 '{lbl}'");
                        }
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// CI 建置閘門：若審計失敗，直接拋出例外以中止建置管線
        /// </summary>
        public static void AssertBaseCatalogClean(AddressablesCatalog catalog)
        {
            var report = AuditBaseCatalog(catalog);
            if (!report.IsValid)
            {
                string summary = string.Join("; ", report.Errors);
                throw new InvalidOperationException($"BUILD REJECTED (陽性對照攔截): 本體 Catalog 違反 R18 隔離規範！{summary}");
            }
        }
    }
}
