using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Core.Localization;
using Game.Domain.MasterData;

namespace LocalizationTool
{
    public static class Program
    {
        private static string FindLocDir()
        {
            string[] candidates = new[]
            {
                "data/loc",
                "../data/loc",
                "../../data/loc",
                "F:/game_dev/data/loc"
            };

            foreach (string p in candidates)
            {
                if (Directory.Exists(p)) return Path.GetFullPath(p);
            }
            return Path.GetFullPath("data/loc");
        }

        private static string FindMasterDataDir()
        {
            string[] candidates = new[]
            {
                "data/master",
                "../data/master",
                "../../data/master",
                "F:/game_dev/data/master"
            };

            foreach (string p in candidates)
            {
                if (Directory.Exists(p) && File.Exists(Path.Combine(p, "config.json")))
                {
                    return Path.GetFullPath(p);
                }
            }
            return Path.GetFullPath("data/master");
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==================================================================");
            Console.WriteLine("🌐  Project Tower Game — 多語言離線匯出入與格式校驗工具 (LocTool) 🌐");
            Console.WriteLine("==================================================================");

            string locDir = FindLocDir();
            if (!Directory.Exists(locDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 找不到多語言目錄: {locDir}");
                Console.ResetColor();
                return 1;
            }

            string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "validate";

            var locService = new LocalizationService();
            locService.LoadFromDirectory(locDir);

            switch (command)
            {
                case "export":
                {
                    string outCsv = args.Length > 1 ? args[1] : Path.Combine(locDir, "translations_export.csv");
                    Console.WriteLine($"📤 匯出語系字典 ({string.Join(", ", locService.AvailableLocales)}) 至 CSV...");
                    string csv = LocalizationCsvTool.ExportToCsv(locService, "zh-TW");
                    File.WriteAllText(outCsv, csv, System.Text.Encoding.UTF8);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ 匯出成功！檔案已儲存至: {outCsv}");
                    Console.ResetColor();
                    return 0;
                }

                case "import":
                {
                    if (args.Length < 2)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("❌ 請指定要匯入的 CSV 檔案路徑：dotnet run --project tools/LocalizationTool -- import <file.csv>");
                        Console.ResetColor();
                        return 1;
                    }

                    string inCsv = args[1];
                    if (!File.Exists(inCsv))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ 找不到匯入檔案: {inCsv}");
                        Console.ResetColor();
                        return 1;
                    }

                    Console.WriteLine($"📥 從 CSV 匯入多語言字串: {inCsv}");
                    string csvContent = File.ReadAllText(inCsv, System.Text.Encoding.UTF8);
                    var imported = LocalizationCsvTool.ImportFromCsv(csvContent);

                    LocalizationCsvTool.SaveImportedToDirectory(imported, locDir);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ 匯入成功！已更新 {imported.Count} 個語言目錄至: {locDir}");
                    Console.ResetColor();
                    return 0;
                }

                case "validate":
                default:
                {
                    Console.WriteLine($"🔍 執行全語系缺字、佔位符與主資料關聯校驗 (目錄: {locDir})...");

                    MasterDatabase master = null;
                    string masterDir = FindMasterDataDir();
                    if (Directory.Exists(masterDir))
                    {
                        try { master = MasterDataLoader.LoadFromDirectory(masterDir); } catch { }
                    }

                    var val = LocalizationValidator.Validate(locService, master, "zh-TW");

                    if (!val.IsValid)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n❌ 校驗失敗！共發現 {val.Errors.Count} 項嚴重錯誤：");
                        foreach (string err in val.Errors) Console.WriteLine($"  • [ERROR] {err}");
                        Console.ResetColor();
                        return 1;
                    }

                    if (val.Warnings.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n⚠️ 發現 {val.Warnings.Count} 項提示/未翻譯警告：");
                        foreach (string w in val.Warnings) Console.WriteLine($"  • [WARN] {w}");
                        Console.ResetColor();
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n✅ 多語言字串校驗通過！基底語言 zh-TW 完整，佔位符格式正確。");
                    Console.WriteLine($"📊 統計資訊：支援語系 [{string.Join(", ", locService.AvailableLocales)}]，zh-TW 共 {locService.GetTable("zh-TW").Count} 條字串。");
                    Console.ResetColor();
                    return 0;
                }
            }
        }
    }
}
