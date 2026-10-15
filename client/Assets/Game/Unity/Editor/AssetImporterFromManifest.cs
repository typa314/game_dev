using System.IO;
using Game.Core.Assets;
using UnityEditor;
using UnityEngine;

namespace Game.Unity.EditorTools
{
    /// <summary>
    /// 依 asset manifest 匯入美術素材（design/15 §2）。
    /// 嚴格遵循分級下限保護，R18 素材絕不匯入主程式 Content，僅 G/R15 納入本體。
    /// </summary>
    public static class AssetImporterFromManifest
    {
        [MenuItem("Game/Import Assets From Manifest")]
        public static void Import()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string manifestPath = Path.GetFullPath(Path.Combine(projectRoot, "..", "data", "asset_manifest.csv"));
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.GetFullPath(Path.Combine(projectRoot, "data", "asset_manifest.csv"));
            }

            string sourceRoot = "F:/game";
            if (!Directory.Exists(sourceRoot))
            {
                sourceRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
            }

            string baseContentDir = Path.Combine(Application.dataPath, "Game", "Content", "BaseGame");
            string adultContentDir = Path.Combine(Application.dataPath, "Game", "Content", "AdultDLC");

            var options = new AssetImportOptions
            {
                ManifestPath = manifestPath,
                SourceRootDir = sourceRoot,
                BaseContentDir = baseContentDir,
                AdultContentDir = adultContentDir,
                IsReleaseMode = false,
                CopyFiles = true
            };

            Debug.Log($"[AssetImporter] 開始自 Manifest 匯入資產: {manifestPath}");
            AssetImportReport report = AssetImportService.Execute(options);

            if (!report.Success)
            {
                Debug.LogError($"[AssetImporter] ❌ 資產匯入失敗！發現 {report.Errors.Count} 項嚴重規格違規（如資料夾分級下限違規）：");
                foreach (string err in report.Errors)
                {
                    Debug.LogError($"  • [ERROR] {err}");
                }
                return;
            }

            foreach (string warn in report.Warnings)
            {
                Debug.LogWarning($"  • [WARN] {warn}");
            }

            AssetDatabase.Refresh();
            Debug.Log($"[AssetImporter] ✅ 資產匯入完成！總清單: {report.TotalEntries} 筆，已匯入本體 (G: {report.ImportedG}, R15: {report.ImportedR15})，R18 隔離至 DLC: {report.ExcludedR18} 筆。");
        }
    }
}
