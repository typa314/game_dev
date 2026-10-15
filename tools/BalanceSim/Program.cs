using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Core.Battle;
using Game.Domain.MasterData;

namespace BalanceSim
{
    public static class Program
    {
        private static string FindMasterDataDir()
        {
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
            Console.WriteLine("⚔️  Project Tower Game — 戰鬥平衡與通關模擬器 (BalanceSim)  ⚔️");
            Console.WriteLine("==================================================================");

            string dataDir = FindMasterDataDir();
            if (!Directory.Exists(dataDir))
            {
                Console.WriteLine($"❌ 找不到主資料目錄: {dataDir}");
                return 1;
            }

            Console.WriteLine($"📂 載入主資料庫: {dataDir}");
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(dataDir);
            Console.WriteLine($"✅ 載入完成 (關卡數: {master.Stages.Count}, 角色數: {master.Characters.Count})");

            var teams = new List<SimTeamConfig>
            {
                new SimTeamConfig
                {
                    Name = "Initial_Lv1",
                    CharIds = new List<string> { "char04_elysia", "char05_rin" },
                    Level = 1,
                    Awaken = 0
                },
                new SimTeamConfig
                {
                    Name = "Initial_Trained_Lv15",
                    CharIds = new List<string> { "char04_elysia", "char05_rin" },
                    Level = 15,
                    Awaken = 0
                },
                new SimTeamConfig
                {
                    Name = "Full_Obtainable_Lv20",
                    CharIds = new List<string> { "char01_claire", "char02_luna", "char03_scarlet", "char04_elysia", "char05_rin" },
                    Level = 20,
                    Awaken = 1
                },
                new SimTeamConfig
                {
                    Name = "Full_Trained_Lv35",
                    CharIds = new List<string> { "char01_claire", "char02_luna", "char03_scarlet", "char04_elysia", "char05_rin" },
                    Level = 35,
                    Awaken = 2
                }
            };

            var stages = master.Stages.Keys.OrderBy(x => x).ToList();
            var strategies = new List<SimStrategy>
            {
                SimStrategy.AggressiveDeploy,
                SimStrategy.BalancedUpgradeDeploy,
                SimStrategy.CannonDeploy
            };

            Console.WriteLine($"\n🚀 啟動模擬矩陣 (編隊: {teams.Count} 組 × 關卡: {stages.Count} 關 × 策略: {strategies.Count} 種)...");
            var rows = BalanceSimRunner.RunSimulation(master, teams, stages, strategies, seedsPerConfig: 5);

            string csvContent = BalanceSimRunner.GenerateCsv(rows);

            Console.WriteLine("\n📊 模擬結果報表 (CSV Output)：");
            Console.WriteLine("------------------------------------------------------------------");
            Console.Write(csvContent);
            Console.WriteLine("------------------------------------------------------------------");

            string outputPath = args.FirstOrDefault(a => a.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, csvContent, System.Text.Encoding.UTF8);
                Console.WriteLine($"💾 報表已儲存至: {outputPath}");
            }

            return 0;
        }
    }
}
