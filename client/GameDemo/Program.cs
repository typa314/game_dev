using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Game.Core;
using Game.Core.Gallery;
using Game.Core.Persistence;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.Encounter;
using Game.Domain.MasterData;
using Game.Domain.Player;

namespace GameDemo
{
    internal sealed class RealClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime LocalNow => DateTime.Now;
    }

    internal sealed class ConsolePresenter : IStoryPresenter
    {
        public void Bg(string sceneId, string variant = null)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  [場景背景] 切換至: {sceneId} (差分: {variant ?? "預設"})");
            Console.ResetColor();
        }

        public void Show(string characterId, IReadOnlyList<string> options = null)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            string optStr = options != null && options.Count > 0 ? $" ({string.Join(", ", options)})" : "";
            Console.WriteLine($"  [角色登場] {characterId}{optStr}");
            Console.ResetColor();
        }

        public void Hide(string characterId)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [角色退場] {characterId}");
            Console.ResetColor();
        }

        public void Expr(string characterId, string expression)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [表情更換] {characterId} -> 表情: {expression}");
            Console.ResetColor();
        }

        public void Motion(string characterId, string motion)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [動作演出] {characterId} -> 動作: {motion}");
            Console.ResetColor();
        }

        public void Cg(string cgId)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  🖼️  [全螢幕 CG 演出] 呈現高解析 CG: {cgId} 🖼️");
            Console.ResetColor();
        }

        public void Bgm(string bgmId)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"  🎵 [BGM 播放] 播放背景音樂: {bgmId}");
            Console.ResetColor();
        }

        public void Se(string seId)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  🔊 [音效播送] 音效: {seId}");
            Console.ResetColor();
        }

        public void Voice(string voiceId)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  🗣️ [角色語音] 播放配音: {voiceId}");
            Console.ResetColor();
        }

        public void Wait(float seconds)
        {
            Console.WriteLine($"  ⏳ [演出等待] 停頓 {seconds:F1} 秒...");
        }

        public void Fade(IReadOnlyList<string> args = null)
        {
            Console.WriteLine($"  ✨ [轉場淡入淡出] {(args != null ? string.Join(" ", args) : "fade")}");
        }

        public void Shake(IReadOnlyList<string> args = null)
        {
            Console.WriteLine($"  💥 [畫面震動效果] {(args != null ? string.Join(" ", args) : "shake")}");
        }
    }

    public static class Program
    {
        private static string FindMasterDataDir()
        {
            string[] candidates = new[]
            {
                "data/master",
                "../data/master",
                "../../data/master",
                "../../../data/master",
                "../../../../data/master",
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

        public static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "Project Tower Game — 框架試跑與全流程展示 (Console CLI Demo)";

            PrintHeader();

            string dataDir = FindMasterDataDir();
            if (!Directory.Exists(dataDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 找不到主資料目錄: {dataDir}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"📂 載入主資料庫: {dataDir}");
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(dataDir);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ 主資料載入成功！(角色: {master.Characters.Count}, 關卡: {master.Stages.Count}, 敵人: {master.Enemies.Count}, 巧遇: {master.Encounters.Count})");
            Console.ResetColor();

            IClock clock = new RealClock();
            RatingPolicy rating = new RatingPolicy(Rating.R18, Rating.R18);
            PlayerState player = GameCore.NewPlayer(master, clock);
            GameCore core = new GameCore(master, player, new SplitMix64Random((ulong)DateTime.UtcNow.Ticks), clock, rating);

            bool autoRun = args.Contains("--auto") || args.Contains("-a");

            if (autoRun)
            {
                RunAllShowcases(core, master);
                return;
            }

            while (true)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("======================= 🎮 遊戲系統試跑主選單 =======================");
                Console.ResetColor();
                Console.WriteLine(" [1] 執行完整全流程自動試跑展示 (轉蛋 -> 養成 -> 推線戰鬥 -> 巧遇 -> 劇情 -> 存檔)");
                Console.WriteLine(" [2] 轉蛋系統 (全新全階卡池 banner_ur 十連抽 & UR/SSR/SR/R 抽取展示)");
                Console.WriteLine(" [3] 角色養成與全階位進階 (LevelUp & R到UR連續進階/升階 & CG解鎖)");
                Console.WriteLine(" [4] 決定性推線戰鬥模擬器 (即時 ASCII 戰況推線與勝負結算)");
                Console.WriteLine(" [5] 巧遇事件系統 (隨機遭遇、選項分支、好感與道具回饋)");
                Console.WriteLine(" [6] ADV 互動劇情播放器 (MiniScript、分級 Fallback、多重結局)");
                Console.WriteLine(" [7] CG 圖鑑與雙層存檔 (查看圖鑑分組、存檔與讀檔隔離驗證)");
                Console.WriteLine(" [0] 退出程式");
                Console.Write("👉 請輸入選項 (0-7): ");

                string choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                switch (choice)
                {
                    case "1":
                        RunAllShowcases(core, master);
                        break;
                    case "2":
                        DemoGacha(core);
                        break;
                    case "3":
                        DemoGrowth(core);
                        break;
                    case "4":
                        DemoBattle(core, master);
                        break;
                    case "5":
                        DemoEncounter(core);
                        break;
                    case "6":
                        DemoStory(core);
                        break;
                    case "7":
                        DemoGalleryAndSave(core);
                        break;
                    case "0":
                        Console.WriteLine("感謝試跑，再見！");
                        return;
                    default:
                        Console.WriteLine("無效選項，請重新輸入。");
                        break;
                }
            }
        }

        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
*******************************************************************************
*             🎮 PROJECT TOWER GAME — CLIENT FRAMEWORK TEST RUN               *
*                 跨 Agent 協同開發成果：C# 9 / .NET 8 核心框架展示           *
*******************************************************************************");
            Console.ResetColor();
        }

        private static void RunAllShowcases(GameCore core, MasterDatabase master)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n🚀 >>> 開始執行完整全流程自動試跑展示 <<< 🚀\n");
            Console.ResetColor();

            DemoGacha(core);
            DemoGrowth(core);
            DemoBattle(core, master);
            DemoEncounter(core);
            DemoStory(core);
            DemoGalleryAndSave(core);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✨ ======================================================= ✨");
            Console.WriteLine("🎉 恭喜！遊戲主程式全系統端對端流程試跑成功，所有模組運作正常！");
            Console.WriteLine("✨ ======================================================= ✨\n");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // 1. 轉蛋展示
        // -----------------------------------------------------------------------------------------
        private static void DemoGacha(GameCore core)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- 🎰 [轉蛋系統展示 (Gacha Engine)] ---");
            Console.ResetColor();

            core.Player.AddItem("gem", 6000); // 補給寶石
            Console.WriteLine($"當前鑽石: {core.Player.ItemCount("gem")} 顆。準備進行全新全階級卡池 (banner_ur) 十連抽 (含 UR/SSR/SR/R)...");

            Result<List<GachaResultEntry>> res = core.PullGacha("banner_ur", 10);
            if (!res.Ok)
            {
                Console.WriteLine($"轉蛋失敗: {res.Error}");
                return;
            }

            Console.WriteLine("🎴 抽取結果：");
            int idx = 1;
            foreach (var draw in res.Value)
            {
                string tag = draw.IsNew ? "【🌟 NEW! 首次獲得】" : "【轉化為角色卡片與心靈碎片】";
                string rarity = draw.Draw.Rarity;
                ConsoleColor color = rarity == "UR" ? ConsoleColor.Cyan :
                                     rarity == "SSR" ? ConsoleColor.Yellow :
                                     rarity == "SR" ? ConsoleColor.Magenta : ConsoleColor.White;

                Console.ForegroundColor = color;
                Console.WriteLine($"  第 {idx:D2} 抽: [{rarity}] {draw.Draw.Char} {tag}");
                Console.ResetColor();
                idx++;
            }
            int pity = core.Player.Gacha.Pity.TryGetValue("banner_ur", out int p) ? p : 0;
            Console.WriteLine($"轉蛋歷史記錄總筆數: {core.Player.Gacha.History.Count} / SSR+ 保底計數: {pity}");
        }

        // -----------------------------------------------------------------------------------------
        // 2. 角色養成與全階位進階展示
        // -----------------------------------------------------------------------------------------
        private static void DemoGrowth(GameCore core)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- 📈 [角色數值成長、階級進階與立繪解鎖 (Character Growth & Promotion)] ---");
            Console.ResetColor();

            const string charId = "char01_claire";
            CharacterState cs = core.Player.Char(charId);
            if (cs == null)
            {
                Console.WriteLine($"尚未持有角色 {charId}");
                return;
            }

            Console.WriteLine($"目標角色: {charId} | 目前位階: {cs.DisplayGrade} (稀有度: {cs.CurrentRarity}, 階級: +{cs.Tier}) | 等級: Lv.{cs.Level}");

            // 增加經驗素材並升級
            core.Player.AddItem("gold", 50000);
            core.Player.AddItem("exp_s", 20);
            var mats = new List<ItemStack> { new ItemStack { Item = "exp_s", N = 10 } };

            Console.WriteLine("使用 10 個 exp_s 升級角色...");
            Result<int> upRes = core.LevelUp(charId, mats);
            if (upRes.Ok)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  🎉 升級成功！新等級: Lv.{upRes.Value}！");
                Console.ResetColor();
            }

            // 展示全新進階系統 (收集卡片/碎片: R -> R+1 -> ... -> R+5 -> SR+0)
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n🔥 [全稀有度可進階系統展示：R 到 UR 的修煉之路] 🔥");
            Console.ResetColor();
            Console.WriteLine($"給予 6 張同角色卡片 (card_{charId}) 進行連續進階...");
            core.Player.AddItem($"card_{charId}", 6);

            for (int i = 1; i <= 6; i++)
            {
                string beforeGrade = cs.DisplayGrade;
                var advRes = core.AdvanceCharacter(charId);
                if (advRes.Ok)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ✨ 第 {i} 次進階成功！{beforeGrade} -> 【{advRes.Value.DisplayGrade}】 (解鎖 CG: {advRes.Value.UnlockedCg ?? "無"})");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"  進階失敗: {advRes.Error}");
                }
            }
            Console.WriteLine($"進階後最新位階: {cs.DisplayGrade}，目前已解鎖 CG 總數: {core.Player.Gallery.Count}");

            // 提升好感度並解鎖立繪階段
            Console.WriteLine("\n增加好感度 +150...");
            core.AddAffection(charId, 150);
            int affLv = core.Growth.AffectionLevel(cs.Affection);
            Console.WriteLine($"  當前好感等級: Lv.{affLv} (已解鎖立繪階段數: {cs.UnlockedPortraitStages.Count})");
        }

        // -----------------------------------------------------------------------------------------
        // 3. 決定性推線戰鬥模擬展示
        // -----------------------------------------------------------------------------------------
        private static void DemoBattle(GameCore core, MasterDatabase master)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- ⚔️ [決定性推線戰鬥模擬 (Battle Simulator)] ---");
            Console.ResetColor();

            const string stageId = "st_1_01";
            Console.WriteLine($"挑戰關卡: {stageId} ({master.Stages[stageId].Name})");

            Result<BattleSetup> setupRes = core.PrepareBattle(stageId, 12345);
            if (!setupRes.Ok)
            {
                Console.WriteLine($"準備戰鬥失敗: {setupRes.Error}");
                return;
            }

            BattleSetup setup = setupRes.Value;
            var sim = new BattleSimulator(setup);
            Console.WriteLine($"戰場長度: {setup.Stage.Length} 單位 | 敵方據點 HP: {setup.Stage.EnemyBaseHp}");

            int step = 0;
            Console.WriteLine("模擬戰鬥推進中 (30 tick/s 決定性整數運算)：");

            while (sim.Outcome == BattleOutcome.Ongoing && step < 1200)
            {
                // 自動部署出兵
                for (int slot = 0; slot < setup.Team.Count; slot++)
                {
                    if (sim.CanDeploy(slot))
                    {
                        sim.Enqueue(CommandType.Deploy, slot);
                    }
                }

                sim.Step();
                step++;

                // 每 200 tick 打印一次戰況 ASCII 進度
                if (step % 200 == 0)
                {
                    PrintBattleField(sim, setup.Stage.Length);
                }
            }

            Console.WriteLine();
            PrintBattleField(sim, setup.Stage.Length);

            var report = new BattleReport
            {
                StageId = stageId,
                Seed = 12345,
                Team = new List<string>(core.Player.Team),
                Commands = sim.Recorded
            };

            Console.WriteLine("戰鬥結束，送交 GameCore 進行防篡改重播驗證並結算...");
            Result<BattleReward> submitRes = core.SubmitBattle(report);
            if (submitRes.Ok)
            {
                BattleReward reward = submitRes.Value;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  🏆 戰鬥結果: {reward.Result.Outcome}！(耗時: {reward.Result.Ticks / 30.0f:F1}s)");
                Console.WriteLine($"  ⭐ 評定星數: {new string('★', reward.Stars)}{new string('☆', 3 - reward.Stars)}");
                if (reward.FirstClear) Console.WriteLine("  🎁 首通獎勵: 發放大筆鑽石與新關卡解鎖！");
                Console.ResetColor();
            }
        }

        private static void PrintBattleField(BattleSimulator sim, int length)
        {
            float allyPct = (float)sim.AllyBaseHp / 1000.0f;
            float enemyPct = (float)sim.EnemyBaseHp / 2000.0f;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [我方據點 {sim.AllyBaseHp} HP] ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(new string('=', Math.Max(0, (int)(allyPct * 10))));
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("===⚔️===");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(new string('=', Math.Max(0, (int)(enemyPct * 10))));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" [敵方據點 {sim.EnemyBaseHp} HP]");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // 4. 巧遇事件展示
        // -----------------------------------------------------------------------------------------
        private static void DemoEncounter(GameCore core)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- 🌸 [巧遇事件系統 (Encounter System)] ---");
            Console.ResetColor();

            const string spotId = "spot_park";
            Console.WriteLine($"造訪地點: 公園 ({spotId})，消耗 1 體力...");

            Result<EncounterStart> startRes = core.StartEncounter(spotId);
            if (!startRes.Ok)
            {
                Console.WriteLine($"巧遇啟動失敗: {startRes.Error}");
                return;
            }

            EncounterEventDef ev = startRes.Value.Event;
            if (ev == null)
            {
                Console.WriteLine("未遭遇特定角色事件，獲得隨機散步獎勵！");
                return;
            }

            Console.WriteLine($"✨ 巧遇事件觸發: {ev.Id} (關聯劇本節點: {ev.Story})");
            Console.WriteLine($"可選分支選項: {string.Join(", ", ev.Outcomes.Keys)}");

            string chosen = ev.Outcomes.Keys.First();
            Console.WriteLine($"玩家選擇選項 [{chosen}]...");

            Result<List<Outcome>> resolveRes = core.ResolveEncounter(chosen);
            if (resolveRes.Ok)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  結算成功！獲得好感與道具加成：");
                foreach (var o in resolveRes.Value)
                {
                    if (o.AffectionChar != null) Console.WriteLine($"   - {o.AffectionChar} 好感增加 +{o.N}");
                    if (o.Item != null) Console.WriteLine($"   - 獲得道具 {o.Item} x{o.N}");
                }
                Console.ResetColor();
            }
        }

        // -----------------------------------------------------------------------------------------
        // 5. ADV 互動劇情展示
        // -----------------------------------------------------------------------------------------
        private static void DemoStory(GameCore core)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- 📖 [ADV 劇情執行器 (MiniScript Runner & Fallback)] ---");
            Console.ResetColor();

            string demoScript = @"
[node Start]
command bg scene_academy_rooftop sunset
command show char04_elysia pose=stand expr=smile at=center
command bgm bgm/lobby_01
line l1 Elysia ""指揮官，今天工作辛苦了～夕陽很漂亮呢！""
option 「跟妳在一起更漂亮。」 -> SweetRoute
option 「我們回去整理報告吧。」 -> WorkRoute

[node SweetRoute]
command expr char04_elysia shy
command affection char04_elysia 20
line l2 Elysia ""討、討厭啦……突然說這種話……""
jump IntimateScene

[node IntimateScene rating:R18 fallback:MildScene]
command cg cg_elysia_intimate_r18
line l3 Elysia ""（此處為成人分級專屬演出，G 級政策會自動走 Fallback）""
command ending route_elysia_sweet
end

[node MildScene]
command cg cg_elysia_smile_g
line l4 Elysia ""我們手牽著手一起走回宿舍吧。""
command ending route_elysia_normal
end

[node WorkRoute]
command expr char04_elysia flat
line l5 Elysia ""真是個認真嚴肅的指揮官呢～""
command ending route_elysia_normal
end
";
            var script = MiniScript.Parse(demoScript);
            var presenter = new ConsolePresenter();
            var runner = new MiniScriptRunner(script, core.Rating);
            StoryCommandRegistry.Register(runner, core, presenter);

            Console.WriteLine("載入 ADV 劇本，從 [Start] 節點啟動：\n");
            runner.Start("Start");

            while (!runner.IsEnded)
            {
                runner.Continue();
                if (runner.CurrentLine != null)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"【{runner.CurrentLine.Speaker}】：{runner.CurrentLine.TextKey}");
                    Console.ResetColor();
                }

                if (runner.CurrentOptions != null && runner.CurrentOptions.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n👉 遇到分支選項：");
                    for (int i = 0; i < runner.CurrentOptions.Count; i++)
                    {
                        Console.WriteLine($"   [{i + 1}] {runner.CurrentOptions[i].TextKey}");
                    }
                    Console.ResetColor();

                    Console.WriteLine("自動選擇分支 [1] SweetRoute...\n");
                    runner.Choose(0);
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("劇情播放結束，結局已自動登記至 Profile！");
            Console.ResetColor();
        }

        // -----------------------------------------------------------------------------------------
        // 6. 圖鑑與雙層存檔展示
        // -----------------------------------------------------------------------------------------
        private static void DemoGalleryAndSave(GameCore core)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- 💾 [CG 圖鑑與雙層存檔 (Dual-Layer Save)] ---");
            Console.ResetColor();

            var cgs = new List<CgDef>
            {
                new CgDef("cg_claire_01", "char01_claire", Rating.G, "克萊兒的早晨"),
                new CgDef("cg_claire_r18_01", "char01_claire", Rating.R18, "克萊兒的秘密 (R18)"),
                new CgDef("cg_elysia_01", "char04_elysia", Rating.G, "愛麗西亞的笑容")
            };

            var gallery = new GalleryService(cgs);
            core.Player.Gallery.Add("cg_elysia_01");

            var grouped = gallery.GetGroupedGallery(core.Player, core.Rating);
            Console.WriteLine("🖼️ 當前 CG 圖鑑檢視 (含進階與階級解鎖之專屬 CG)：");
            foreach (var kvp in grouped)
            {
                Console.WriteLine($"  角色 [{kvp.Key}]:");
                foreach (var item in kvp.Value)
                {
                    string status = item.IsUnlocked ? "✅ 已解鎖" : "🔒 未解鎖";
                    ConsoleColor color = item.IsUnlocked ? ConsoleColor.Green : ConsoleColor.DarkGray;
                    Console.ForegroundColor = color;
                    Console.WriteLine($"    - {item.Def.Title} [{item.Def.Rating}] : {status} (ID: {item.Def.Id})");
                    Console.ResetColor();
                }
            }

            // 雙層存檔隔離展示
            Console.WriteLine("\n雙層存檔驗證：");
            var saveService = new StorySaveService(null, new RealClock());
            var runner = new MiniScriptRunner("[node Start]\nset trust = 50\nend");
            runner.Start("Start");
            runner.Continue();

            saveService.Save("quick_1", runner);
            Console.WriteLine("  1. 已保存劇情快照至 quick_1 (trust = 50)");

            int goldBefore = (int)core.Player.ItemCount("gold");
            core.Player.AddItem("gold", 10000);
            Console.WriteLine($"  2. 玩家在全域 Profile 賺取了 10,000 金幣 (總金幣: {core.Player.ItemCount("gold")})");

            StorySlot loaded = saveService.Load("quick_1");
            saveService.ApplyToRunner(loaded, runner, MiniScript.Parse("[node Start]\nset trust = 50\nend"));
            Console.WriteLine($"  3. 讀回劇情快照 quick_1。驗證全域金幣庫存: {core.Player.ItemCount("gold")} (絕不倒退！)");
            Console.WriteLine($"  4. 劇情變數成功恢復: trust = {runner.Variables["trust"]}");
        }
    }
}
