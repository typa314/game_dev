using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Persistence;
using Game.Core.Platform;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Domain.Portrait;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 工單 E1：不需畫面的完整遊玩測試（FullLoopTests）
    /// 依序驗證 E1-1 到 E1-10，在純 .NET 記憶體與領域層完成完整遊戲循環。
    /// </summary>
    [TestFixture]
    public class FullLoopTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public DateTime LocalNow => UtcNow;

            public FixedClock(DateTime utc)
            {
                UtcNow = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            }
        }

        private sealed class RecordingPresenter : IStoryPresenter
        {
            public readonly List<string> Events = new List<string>();

            public void Bg(string sceneId, string variant = null) => Events.Add($"bg:{sceneId}:{variant}");
            public void Show(string characterId, IReadOnlyList<string> options = null) => Events.Add($"show:{characterId}");
            public void Hide(string characterId) => Events.Add($"hide:{characterId}");
            public void Expr(string characterId, string expression) => Events.Add($"expr:{characterId}:{expression}");
            public void Motion(string characterId, string motion) => Events.Add($"motion:{characterId}:{motion}");
            public void Cg(string cgId) => Events.Add($"cg:{cgId}");
            public void Bgm(string bgmId) => Events.Add($"bgm:{bgmId}");
            public void Se(string seId) => Events.Add($"se:{seId}");
            public void Voice(string voiceId) => Events.Add($"voice:{voiceId}");
            public void Wait(float seconds) => Events.Add($"wait:{seconds}");
            public void Fade(IReadOnlyList<string> args = null) => Events.Add("fade");
            public void Shake(IReadOnlyList<string> args = null) => Events.Add("shake");
        }

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FullLoopTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        [Test]
        public void E1_FullGameLoop_EndToEnd_Headless()
        {
            // =====================================================================================
            // E1-1: 建立新玩家（E1-10: 全程使用 G 政策與 Null 平台服務）
            // =====================================================================================
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new FixedClock(new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc));
            var ratingPolicy = new RatingPolicy(Rating.G, Rating.G); // E1-10: G 政策
            PlayerState player = GameCore.NewPlayer(master, clock);
            var core = new GameCore(master, player, new SplitMix64Random(12345), clock, ratingPolicy);

            var platform = new NullPlatformService(); // E1-10: Null 平台
            using var platformBridge = new PlatformAchievementBridge(platform, master, core.Bus);

            // 斷言新玩家初始化狀態
            Assert.AreEqual(Rating.G, core.Rating.Effective, "E1-10: 生效分級政策為 G");
            Assert.IsTrue(player.Stages.ContainsKey("st_1_01") && player.Stages["st_1_01"].Unlocked, "初始關卡包含 st_1_01");
            Assert.IsFalse(player.Stages.ContainsKey("st_1_02") && player.Stages["st_1_02"].Unlocked, "st_1_02 尚未解鎖");
            Assert.IsTrue(player.Encounter.UnlockedSpots.Contains("spot_park"), "初始巧遇地點包含 spot_park");
            Assert.IsFalse(player.Encounter.UnlockedSpots.Contains("spot_cafe"), "spot_cafe 尚未解鎖");
            Assert.IsTrue(player.Scenes.Contains("scene_lobby_default"), "初始場景包含 scene_lobby_default");
            Assert.IsFalse(player.Scenes.Contains("scene_cafe"), "scene_cafe 尚未解鎖");
            Assert.IsTrue(player.Characters.ContainsKey("char04_elysia"), "初始持有角色 Elysia");
            Assert.IsTrue(player.Characters.ContainsKey("char05_rin"), "初始持有角色 Rin");

            // =====================================================================================
            // E1-2: 通關 st_1_01 → 解鎖 st_1_02；首次通關獎勵到帳
            // =====================================================================================
            long gemsBeforeSt1 = core.Player.ItemCount("gem");
            Result<BattleSetup> setupRes1 = core.PrepareBattle("st_1_01", 7);
            Assert.IsTrue(setupRes1.Ok, setupRes1.Error);

            var sim1 = new BattleSimulator(setupRes1.Value);
            while (sim1.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setupRes1.Value.Team.Count; slot++)
                {
                    if (sim1.CanDeploy(slot)) sim1.Enqueue(CommandType.Deploy, slot);
                }
                sim1.Step();
            }
            Assert.AreEqual(BattleOutcome.Win, sim1.Outcome, "st_1_01 戰鬥應順利獲勝");

            var report1 = new BattleReport
            {
                StageId = "st_1_01",
                Seed = 7,
                Team = new List<string>(core.Player.Team),
                Commands = sim1.Recorded
            };
            Result<BattleReward> rewardRes1 = core.SubmitBattle(report1);
            Assert.IsTrue(rewardRes1.Ok, rewardRes1.Error);
            Assert.IsTrue(rewardRes1.Value.FirstClear, "st_1_01 應標記為首次通關");
            Assert.IsTrue(core.Player.Stages.ContainsKey("st_1_02") && core.Player.Stages["st_1_02"].Unlocked, "通關 st_1_01 應解鎖 st_1_02");
            Assert.AreEqual(gemsBeforeSt1 + 100, core.Player.ItemCount("gem"), "首通 st_1_01 獎勵 100 寶石到帳");

            // =====================================================================================
            // E1-3: 通關 st_1_02 → 解鎖 spot_cafe 和 scene_cafe
            // =====================================================================================
            long gemsBeforeSt2 = core.Player.ItemCount("gem");
            Result<BattleSetup> setupRes2 = core.PrepareBattle("st_1_02", 8);
            Assert.IsTrue(setupRes2.Ok, setupRes2.Error);

            var sim2 = new BattleSimulator(setupRes2.Value);
            while (sim2.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setupRes2.Value.Team.Count; slot++)
                {
                    if (sim2.CanDeploy(slot)) sim2.Enqueue(CommandType.Deploy, slot);
                }
                sim2.Step();
            }
            Assert.AreEqual(BattleOutcome.Win, sim2.Outcome, "st_1_02 戰鬥應順利獲勝");

            var report2 = new BattleReport
            {
                StageId = "st_1_02",
                Seed = 8,
                Team = new List<string>(core.Player.Team),
                Commands = sim2.Recorded
            };
            Result<BattleReward> rewardRes2 = core.SubmitBattle(report2);
            Assert.IsTrue(rewardRes2.Ok, rewardRes2.Error);
            Assert.IsTrue(rewardRes2.Value.FirstClear, "st_1_02 應標記為首次通關");
            Assert.AreEqual(gemsBeforeSt2 + 150, core.Player.ItemCount("gem"), "首通 st_1_02 獎勵 150 寶石到帳");

            // 斷言解鎖 spot_cafe 和 scene_cafe
            Assert.IsTrue(core.Player.Encounter.UnlockedSpots.Contains("spot_cafe"), "通關 st_1_02 應解鎖地點 spot_cafe");
            Assert.IsTrue(core.Player.Scenes.Contains("scene_cafe"), "通關 st_1_02 應解鎖場景 scene_cafe");

            // =====================================================================================
            // E1-4: 十連抽 → 取得角色，成就進度更新
            // =====================================================================================
            // 補充足夠抽卡寶石
            core.Player.AddItem("gem", 3000);
            int charsCountBeforePull = core.Player.Characters.Count;

            Result<List<GachaResultEntry>> pullRes = core.PullGacha("banner_ur", 10);
            Assert.IsTrue(pullRes.Ok, pullRes.Error);
            Assert.AreEqual(10, pullRes.Value.Count, "十連抽回傳 10 筆抽取紀錄");

            // 斷言成就解鎖與進度更新
            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_first_pull"), "十連抽後解鎖 ach_first_pull 成就");
            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_gacha_100"), "ach_gacha_100 計數已建立");
            Assert.IsTrue(core.Player.Achievements["ach_gacha_100"].Progress >= 10, "ach_gacha_100 進度至少累計 10 抽");

            // =====================================================================================
            // E1-5: 給予足夠的素材和碎片 → 把一位角色升到 Lv30、★2 → 立繪自動解鎖 S2，SetPortraitStage 後 PortraitResolver 回傳 S2 的 art
            // =====================================================================================
            const string claireId = "char01_claire";
            // 確保擁有 Claire
            if (core.Player.Char(claireId) == null)
            {
                core.Player.Characters[claireId] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Exp = 0,
                    Awaken = 0,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" }
                };
            }

            CharacterState claire = core.Player.Char(claireId);

            // 1. 覺醒至 ★2（Awaken = 2，等級上限由 20 提升至 40）
            core.Player.AddItem("awaken_shard_char01_claire", 100);
            if (claire.Awaken < 1)
            {
                Result awk1 = core.Awaken(claireId);
                Assert.IsTrue(awk1.Ok, awk1.Error);
            }
            if (claire.Awaken < 2)
            {
                Result awk2 = core.Awaken(claireId);
                Assert.IsTrue(awk2.Ok, awk2.Error);
            }
            Assert.AreEqual(2, claire.Awaken, "Claire 覺醒等級應達到 ★2");

            // 2. 升級至 Lv30（★2 上限 40）
            core.Player.AddItem("gold", 500000);
            core.Player.AddItem("exp_l", 60);
            var mats = new List<ItemStack> { new ItemStack { Item = "exp_l", N = 50 } };
            Result<int> lvlRes = core.LevelUp(claireId, mats);
            Assert.IsTrue(lvlRes.Ok, lvlRes.Error);
            Assert.IsTrue(claire.Level >= 30, $"Claire 應升級至 Lv30 以上，目前 Lv.{claire.Level}");

            // 斷言自動解鎖 ps_char01_s2
            Assert.IsTrue(claire.UnlockedPortraitStages.Contains("ps_char01_s2"), "Lv30 且 ★2 應自動解鎖 S2 立繪階段");

            // 設定立繪並驗證 PortraitResolver
            Result setStageRes = core.SetPortraitStage(claireId, "ps_char01_s2");
            Assert.IsTrue(setStageRes.Ok, setStageRes.Error);

            ResolvedPortrait resolvedArt = PortraitResolver.Resolve(master, core.Player, ratingPolicy, claireId);
            Assert.IsNotNull(resolvedArt);
            Assert.IsNotNull(resolvedArt.Art);
            Assert.AreEqual("char/char01_claire/portrait/ps_char01_s2/stand", resolvedArt.Art.Stand, "PortraitResolver 應回傳 ps_char01_s2 的 stand art");

            // =====================================================================================
            // E1-6: 在 spot_park 完成 Elysia 的系列巧遇 → Elysia 的好感度和覺醒碎片增加，角色數量不變
            // =====================================================================================
            int charCountBeforeEncounter = core.Player.Characters.Count;
            int elysiaAffBefore = core.Player.Char("char04_elysia").Affection;
            long shardsBefore = core.Player.ItemCount("awaken_shard_char04_elysia");

            // 確保體力充沛
            core.Player.Encounter.Stamina = 5;

            // 觸發第 1 段系列事件
            Result<EncounterStart> enc1 = core.StartEncounter("spot_park");
            Assert.IsTrue(enc1.Ok, enc1.Error);
            Assert.AreEqual("enc_char04_elysia_01", enc1.Value.Event.Id, "應觸發 Elysia 第 1 段系列巧遇");

            // 選擇 choice0: 好感度 +30
            Result<List<Outcome>> resEnc1 = core.ResolveEncounter("choice0");
            Assert.IsTrue(resEnc1.Ok, resEnc1.Error);
            Assert.AreEqual(elysiaAffBefore + 30, core.Player.Char("char04_elysia").Affection, "Elysia 好感度應增加 30");

            // 觸發第 2 段系列事件（條件已滿足：affection >= 30）
            Result<EncounterStart> enc2 = core.StartEncounter("spot_park");
            Assert.IsTrue(enc2.Ok, enc2.Error);
            Assert.AreEqual("enc_char04_elysia_02", enc2.Value.Event.Id, "應推進至 Elysia 第 2 段系列巧遇");

            // 選擇 choice0: 好感度 +50，覺醒碎片 +5
            Result<List<Outcome>> resEnc2 = core.ResolveEncounter("choice0");
            Assert.IsTrue(resEnc2.Ok, resEnc2.Error);
            Assert.AreEqual(elysiaAffBefore + 30 + 50, core.Player.Char("char04_elysia").Affection, "Elysia 好感度應累計增加 80");
            Assert.AreEqual(shardsBefore + 5, core.Player.ItemCount("awaken_shard_char04_elysia"), "Elysia 覺醒碎片增加 5 片");

            // 關鍵斷言：角色數量絕對不變（巧遇不直接招募角色）
            Assert.AreEqual(charCountBeforeEncounter, core.Player.Characters.Count, "巧遇事件結束後角色總數絕對不變");

            // =====================================================================================
            // E1-7: 播放一段測試劇本，在選項前存到劇情存檔欄位 → 選 A 走到結局 A
            // E1-10: R18 內容一律走 fallback（在 G 政策下）
            // =====================================================================================
            var saveService = new StorySaveService(_tempDir, clock);
            var presenter = new RecordingPresenter();

            // 劇本包含：R18 節點帶 Fallback、分歧點存檔、走向結局 A 與 B
            const string testScript = @"
[node Start]
line l1 Claire welcome_commander
jump AdultNode

[node AdultNode rating:R18 fallback:SafeNode]
line l_r18 Claire naughty_content
end

[node SafeNode]
line l_safe Claire safe_greeting
jump DecisionBranch

[node DecisionBranch]
line l_decide Claire make_your_choice
option choose.a -> EndingPathA
option choose.b -> EndingPathB

[node EndingPathA]
command ending ending_route_a
line l_end_a Claire ending_a_celebration
end

[node EndingPathB]
command ending ending_route_b
line l_end_b Claire ending_b_celebration
end
";
            var parsedScript = MiniScript.Parse(testScript);
            var runner = new MiniScriptRunner(parsedScript, core.Rating);
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("Start");
            runner.Continue(); // l1
            Assert.AreEqual("l1", runner.CurrentLine.LineId);

            runner.Continue(); // 進入 AdultNode，但在 G 政策下自動 fallback 走 SafeNode
            Assert.AreEqual("l_safe", runner.CurrentLine.LineId, "E1-10: G 政策下 R18 節點應自動降級至 SafeNode");

            runner.Continue(); // 進入 DecisionBranch
            Assert.AreEqual("l_decide", runner.CurrentLine.LineId);

            // 在選項前儲存劇情進度至 slot_001
            saveService.Save("slot_001", runner, chapterStartNode: "Start");

            // 進入選項，選擇 A 走向結局 A
            runner.Continue(); // 觸發選項呈現
            Assert.IsNotNull(runner.CurrentOptions);
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            runner.Choose(0); // 選擇分支 A

            Assert.AreEqual("l_end_a", runner.CurrentLine.LineId);
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_a"), "結局 A 應已記錄至 Profile.Endings");

            // 記錄當前玩家進度狀態（角色等級、抽卡記錄、道具）
            int claireLevelBeforeReload = core.Player.Char(claireId).Level;
            int gachaHistoryCountBeforeReload = core.Player.Gacha.History.Count;
            long gemCountBeforeReload = core.Player.ItemCount("gem");

            // =====================================================================================
            // E1-8: 讀取劇情存檔欄位 → 選 B 走到結局 B → 兩個結局都記錄在 Profile；角色、等級、轉蛋紀錄都沒有倒退
            // =====================================================================================
            StorySlot loadedSlot = saveService.Load("slot_001");
            Assert.IsNotNull(loadedSlot, "應能順利讀回劇情存檔 slot_001");

            var runner2 = new MiniScriptRunner(parsedScript, core.Rating);
            StoryCommandRegistry.Register(runner2, core, presenter);
            bool applied = saveService.ApplyToRunner(loadedSlot, runner2, parsedScript);
            Assert.IsTrue(applied, "存檔應成功套用至 runner2");

            // 在 DecisionBranch 選項處選擇 B 走向結局 B
            runner2.Continue(); // 顯示 l_decide
            runner2.Continue(); // 進入選項分支
            Assert.IsNotNull(runner2.CurrentOptions);
            Assert.AreEqual(2, runner2.CurrentOptions.Count);
            runner2.Choose(1); // 選擇分支 B

            Assert.AreEqual("l_end_b", runner2.CurrentLine.LineId);
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_b"), "結局 B 應已記錄至 Profile.Endings");

            // 關鍵斷言：兩個結局都完整保留於 Profile！
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_a"), "舊結局 A 必須依然保留於 Profile");
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_b"), "新結局 B 同時成功累加至 Profile");

            // 關鍵斷言：角色、等級、轉蛋紀錄、道具絕無倒退！
            Assert.AreEqual(claireLevelBeforeReload, core.Player.Char(claireId).Level, "角色等級不可因讀檔而倒退");
            Assert.AreEqual(gachaHistoryCountBeforeReload, core.Player.Gacha.History.Count, "轉蛋歷史筆數不可因讀檔而減少");
            Assert.AreEqual(gemCountBeforeReload, core.Player.ItemCount("gem"), "道具庫存不可因讀檔而回滾");

            // =====================================================================================
            // E1-9: 把 Profile 存檔後重新讀取，序列化結果和存檔前完全相同
            // =====================================================================================
            string profileJson1 = SaveCodec.Encode(core.Player, "0.1.0", clock.UtcNow);
            PlayerState reloadedPlayer = SaveCodec.Decode(profileJson1);
            string profileJson2 = SaveCodec.Encode(reloadedPlayer, "0.1.0", clock.UtcNow);

            Assert.AreEqual(profileJson1, profileJson2, "Profile 存檔與重讀序列化 JSON 必須 100% 逐位元組一致");

            // 透過 FileSaveStore 寫入磁碟並使用備份防護回讀
            var saveStore = new FileSaveStore(_tempDir);
            saveStore.Write("profile.gsave", profileJson1);
            Assert.IsTrue(saveStore.Exists("profile.gsave"));

            var errors = new List<string>();
            PlayerState diskLoaded = saveStore.ReadWithFallback("profile.gsave", SaveCodec.Decode, errors);
            Assert.That(errors, Is.Empty, "磁碟讀取存檔不應產生任何錯誤");
            Assert.IsNotNull(diskLoaded);

            string profileJsonDisk = SaveCodec.Encode(diskLoaded, "0.1.0", clock.UtcNow);
            Assert.AreEqual(profileJson1, profileJsonDisk, "從磁碟讀回並編碼之 Profile 與原始資料完全一致");

            // =====================================================================================
            // E1-10: 全程驗證總結
            // =====================================================================================
            // 驗證 Null 平台接收成就無任何異常
            Assert.IsFalse(platform.SyncEnabled, "Null 平台服務預設不進行雲端同步");
            Assert.AreEqual("tchinese", platform.GetLanguage(), "Null 平台預設回傳繁體中文");
            Assert.IsTrue(platform.UnlockAchievement("DUMMY_ACH"), "Null 平台安全處理成就請求且不拋出異常");
        }
    }
}
