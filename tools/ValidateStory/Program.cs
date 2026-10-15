using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain.MasterData;

namespace ValidateStory
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

            foreach (string p in candidates)
            {
                if (Directory.Exists(p) && File.Exists(Path.Combine(p, "config.json")))
                {
                    return Path.GetFullPath(p);
                }
            }
            return Path.GetFullPath("data/master");
        }

        private static string FindStoryDir(string customArg = null)
        {
            if (!string.IsNullOrEmpty(customArg) && Directory.Exists(customArg))
            {
                return Path.GetFullPath(customArg);
            }

            string[] candidates = new[]
            {
                "data/story",
                "../data/story",
                "../../data/story",
                "F:/game_dev/data/story"
            };

            foreach (string p in candidates)
            {
                if (Directory.Exists(p)) return Path.GetFullPath(p);
            }
            return Path.GetFullPath("data/story");
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("==================================================================");
            Console.WriteLine("📖  Project Tower Game — ADV 劇本靜態語法與邏輯校驗工具 (ValidateStory) 📖");
            Console.WriteLine("==================================================================");

            string storyDir = FindStoryDir(args.FirstOrDefault(a => !a.StartsWith("-")));
            if (!Directory.Exists(storyDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 錯誤：找不到劇本目錄: {storyDir}");
                Console.ResetColor();
                return 1;
            }

            string masterDir = FindMasterDataDir();
            MasterDatabase master = null;
            if (Directory.Exists(masterDir))
            {
                try
                {
                    master = MasterDataLoader.LoadFromDirectory(masterDir);
                    Console.WriteLine($"📂 已關聯主資料庫: {masterDir} (供角色/場景/CG/結局參照檢核)");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"⚠️ 主資料庫載入警告: {ex.Message}，部分實體 ID 檢查將跳過。");
                    Console.ResetColor();
                }
            }

            Console.WriteLine($"📁 掃描劇本檔案目錄: {storyDir}");
            string[] files = Directory.GetFiles(storyDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".yarn", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x)
                .ToArray();

            if (files.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠️ 目錄中未發現劇本檔案 (.txt 或 .yarn)。");
                Console.ResetColor();
                return 0;
            }

            int totalErrors = 0;
            int totalNodes = 0;
            var scriptReports = new List<(string File, StoryValidationResult Result)>();

            foreach (string filePath in files)
            {
                string relPath = Path.GetRelativePath(storyDir, filePath);
                string text = File.ReadAllText(filePath);

                MiniScript script;
                try
                {
                    script = MiniScript.Parse(text);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ [{relPath}] 劇本語法解析中斷: {ex.Message}");
                    Console.ResetColor();
                    totalErrors++;
                    continue;
                }

                totalNodes += script.Nodes.Count;
                StoryValidationResult res = StoryValidator.Validate(script, master);
                scriptReports.Add((relPath, res));

                if (!res.IsValid)
                {
                    totalErrors += res.Errors.Count;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n❌ [{relPath}] 發現 {res.Errors.Count} 項邏輯違規：");
                    foreach (var err in res.Errors)
                    {
                        Console.WriteLine($"   • {err}");
                    }
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"  ✓ [{relPath}] 節點數: {script.Nodes.Count}，校驗通過");
                }
            }

            if (totalErrors > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ 劇本校驗未通過！總檔案數: {files.Length}，總錯誤數: {totalErrors} 項。請修復後再提交！");
                Console.ResetColor();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✅ 所有劇本校驗 100% 通過！");
            Console.WriteLine($"📊 統計報告：檢核檔案 {files.Length} 個，總節點數 {totalNodes} 個，零錯誤！");
            Console.ResetColor();
            return 0;
        }
    }
}
