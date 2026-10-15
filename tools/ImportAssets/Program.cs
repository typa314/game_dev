using System;
using System.IO;
using System.Linq;
using Game.Core.Assets;

namespace ImportAssets
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==================================================================");
            Console.WriteLine("🎨  Project Tower Game — 資產 Manifest 匯入與分級隔離工具 (ImportAssets) 🎨");
            Console.WriteLine("==================================================================");

            string manifestPath = GetArg(args, "--manifest") ?? FindManifestFile();
            string sourceRoot = GetArg(args, "--source") ?? "F:/game";
            string outBase = GetArg(args, "--out-base") ?? Path.GetFullPath("client/Assets/Game/Content/BaseGame");
            string outAdult = GetArg(args, "--out-adult") ?? Path.GetFullPath("client/Assets/Game/Content/AdultDLC");
            bool isRelease = args.Contains("--release");

            if (!File.Exists(manifestPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 找不到 Manifest 檔案: {manifestPath}");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"📂 讀取 Manifest: {manifestPath}");
            Console.WriteLine($"📁 來源美術資產根目錄: {sourceRoot}");
            Console.WriteLine($"📦 主程式本體產出目錄: {outBase}");
            Console.WriteLine($"🔞 成人 DLC 產出目錄: {outAdult}");
            Console.WriteLine($"⚙️  建置模式: {(isRelease ? "發行模式 (Release Mode - 嚴禁 placeholder)" : "開發模式 (Development Mode)")}");

            var options = new AssetImportOptions
            {
                ManifestPath = manifestPath,
                SourceRootDir = sourceRoot,
                BaseContentDir = outBase,
                AdultContentDir = outAdult,
                IsReleaseMode = isRelease,
                CopyFiles = false // CLI 預設執行檢查與分析報表，傳入 --copy 則執行實體複製
            };

            if (args.Contains("--copy"))
            {
                options.CopyFiles = true;
                Console.WriteLine("⚡ 啟用實體檔案複製 (--copy)");
            }

            AssetImportReport report = AssetImportService.Execute(options);

            if (!report.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ 資產匯入驗證失敗！共發現 {report.Errors.Count} 項違規錯誤：");
                foreach (string err in report.Errors)
                {
                    Console.WriteLine($"  • [ERROR] {err}");
                }
                Console.ResetColor();
                return 1;
            }

            if (report.Warnings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n⚠️ 警告項目 ({report.Warnings.Count} 項)：");
                foreach (string w in report.Warnings)
                {
                    Console.WriteLine($"  • [WARN] {w}");
                }
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ 資產 Manifest 驗證與分流報表完成！");
            Console.WriteLine("📊 匯入統計：");
            Console.WriteLine($"  - 總登錄資產: {report.TotalEntries} 筆");
            Console.WriteLine($"  - 本體全年齡 (G): {report.ImportedG} 筆");
            Console.WriteLine($"  - 本體輔導級 (R15): {report.ImportedR15} 筆");
            Console.WriteLine($"  - 隔離至成人 DLC (R18): {report.ExcludedR18} 筆 (絕不混入主程式)");
            Console.WriteLine($"  - 草稿狀態 (Draft): {report.DraftCount} 筆");
            Console.WriteLine($"  - 佔位狀態 (Placeholder): {report.PlaceholderCount} 筆");
            Console.WriteLine($"  - 來源檔案缺漏: {report.MissingSourceCount} 筆");
            Console.ResetColor();

            return 0;
        }

        private static string GetArg(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static string FindManifestFile()
        {
            string[] candidates = new[]
            {
                "data/asset_manifest.csv",
                "../data/asset_manifest.csv",
                "../../data/asset_manifest.csv",
                "F:/game_dev/data/asset_manifest.csv"
            };

            foreach (string p in candidates)
            {
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            return Path.GetFullPath("data/asset_manifest.csv");
        }
    }
}
