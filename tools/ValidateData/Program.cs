using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Domain.MasterData;

namespace ValidateData
{
    public static class Program
    {
        private static string FindMasterDataDir(string customArg = null)
        {
            if (!string.IsNullOrEmpty(customArg))
            {
                if (Directory.Exists(customArg)) return Path.GetFullPath(customArg);
            }

            string[] candidates = new[]
            {
                "data/master",
                "../data/master",
                "../../data/master",
                "F:/game_dev/data/master"
            };

            foreach (string path in candidates)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "config.json")))
                {
                    return Path.GetFullPath(path);
                }
            }
            return Path.GetFullPath("data/master");
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==================================================================");
            Console.WriteLine("🛡️  Project Tower Game — 主資料靜態校驗與 CI 防護工具 (ValidateData) 🛡️");
            Console.WriteLine("==================================================================");

            string targetDir = FindMasterDataDir(args.FirstOrDefault(a => !a.StartsWith("-")));
            if (!Directory.Exists(targetDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 錯誤：找不到主資料目錄: {targetDir}");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"📂 讀取主資料來源: {targetDir}");

            MasterDatabase master;
            try
            {
                master = MasterDataLoader.LoadFromDirectory(targetDir);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 載入與 JSON 解析失敗: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"   內部異常: {ex.InnerException.Message}");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine("🔍 執行交叉參照、機率、分級、條件語法與完整性校驗...");
            List<string> errors = MasterDataValidator.Validate(master);

            if (errors != null && errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ 校驗未通過！共發現 {errors.Count} 項資料規格錯誤：");
                foreach (string err in errors)
                {
                    Console.WriteLine($"  • [ERROR] {err}");
                }
                Console.ResetColor();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ 主資料庫校驗 100% 通過！零錯誤！");
            Console.WriteLine("📊 資料庫統計：");
            Console.WriteLine($"  - 角色總數: {master.Characters.Count} 位");
            Console.WriteLine($"  - 關卡總數: {master.Stages.Count} 關");
            Console.WriteLine($"  - 敵人種類: {master.Enemies.Count} 種");
            Console.WriteLine($"  - 巧遇事件: {master.Encounters.Count} 筆 (地點: {master.Spots.Count} 處)");
            Console.WriteLine($"  - 場景數量: {master.Scenes.Count} 個");
            Console.WriteLine($"  - 服裝配置: {master.Costumes.Count} 套");
            Console.WriteLine($"  - 卡池配置: {master.Banners.Count} 個 (含全域跨階池)");
            Console.WriteLine($"  - 成就定義: {master.Achievements.Count} 個");
            Console.ResetColor();

            return 0;
        }
    }
}
