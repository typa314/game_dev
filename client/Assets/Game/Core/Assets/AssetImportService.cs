using System;
using System.Collections.Generic;
using System.IO;
using Game.Domain;
using Game.Domain.MasterData;

namespace Game.Core.Assets
{
    public sealed class AssetImportOptions
    {
        public string ManifestPath { get; set; }
        public string SourceRootDir { get; set; }       // e.g. F:/game
        public string BaseContentDir { get; set; }     // destination for G and R15 (Base Game Content)
        public string AdultContentDir { get; set; }    // destination for R18 (Adult DLC Content)
        public bool IsReleaseMode { get; set; }
        public bool CopyFiles { get; set; } = true;
    }

    public sealed class AssetImportReport
    {
        public int TotalEntries { get; set; }
        public int ImportedG { get; set; }
        public int ImportedR15 { get; set; }
        public int ExcludedR18 { get; set; }
        public int DraftCount { get; set; }
        public int PlaceholderCount { get; set; }
        public int MissingSourceCount { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool Success => Errors.Count == 0;
    }

    public static class AssetImportService
    {
        public static AssetImportReport Execute(AssetImportOptions options, MasterDatabase master = null)
        {
            var report = new AssetImportReport();

            if (options == null || string.IsNullOrWhiteSpace(options.ManifestPath) || !File.Exists(options.ManifestPath))
            {
                report.Errors.Add($"Manifest 檔案不存在: {options?.ManifestPath}");
                return report;
            }

            var catalog = new AssetCatalog();
            catalog.LoadFromFile(options.ManifestPath);
            report.TotalEntries = catalog.Entries.Count;

            // 1. 執行分級下限檢查 (D3-2) 與發行模式檢查 (D3-4)
            var validation = AssetManifestValidator.Validate(catalog, master, options.IsReleaseMode);
            report.Errors.AddRange(validation.Errors);
            report.Warnings.AddRange(validation.Warnings);

            // 若有違反資料夾分級下限或發行規格，立即停止匯入
            if (!validation.IsValid)
            {
                return report;
            }

            // 2. 依分級處理素材分流複製
            foreach (var kvp in catalog.Entries)
            {
                var entry = kvp.Value;

                if (string.Equals(entry.Status, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    report.DraftCount++;
                }
                else if (string.Equals(entry.Status, "placeholder", StringComparison.OrdinalIgnoreCase))
                {
                    report.PlaceholderCount++;
                }

                // 尋找原始檔案
                string srcFilePath = null;
                if (!string.IsNullOrEmpty(options.SourceRootDir) && !string.IsNullOrEmpty(entry.Source))
                {
                    string candidate = Path.Combine(options.SourceRootDir, entry.Source);
                    if (File.Exists(candidate))
                    {
                        srcFilePath = candidate;
                    }
                }

                if (srcFilePath == null && !string.Equals(entry.Status, "placeholder", StringComparison.OrdinalIgnoreCase))
                {
                    report.MissingSourceCount++;
                }

                // 分級下限保護與分流：
                // R18 素材一律不匯入主程式 BaseContent 目錄，嚴格隔離至 AdultContent (DLC) 目錄
                if (entry.Rating >= Rating.R18)
                {
                    report.ExcludedR18++;
                    if (options.CopyFiles && srcFilePath != null && !string.IsNullOrEmpty(options.AdultContentDir))
                    {
                        string targetPath = Path.Combine(options.AdultContentDir, entry.Key + Path.GetExtension(srcFilePath));
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        File.Copy(srcFilePath, targetPath, true);
                    }
                }
                else
                {
                    // G 與 R15 正常匯入 BaseContent
                    if (entry.Rating == Rating.G) report.ImportedG++;
                    else if (entry.Rating == Rating.R15) report.ImportedR15++;

                    if (options.CopyFiles && srcFilePath != null && !string.IsNullOrEmpty(options.BaseContentDir))
                    {
                        string targetPath = Path.Combine(options.BaseContentDir, entry.Key + Path.GetExtension(srcFilePath));
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                        File.Copy(srcFilePath, targetPath, true);
                    }
                }
            }

            return report;
        }
    }
}
