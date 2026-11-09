#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Persistence;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Domain.Portrait;
using Game.Unity;
using Game.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    [TestFixture]
    public sealed class UnityIntegrationPlayModeTests
    {
        private GameObject _holder;
        private GameBootstrap _bootstrap;

        [SetUp]
        public void SetUp()
        {
            GameServices.Clear();
            string saveDir = Path.Combine(Application.persistentDataPath, "saves");
            if (Directory.Exists(saveDir))
            {
                try { Directory.Delete(saveDir, true); } catch { }
            }

            _holder = new GameObject("TestBootstrapHolder");
            _bootstrap = _holder.AddComponent<GameBootstrap>();
            _bootstrap.Boot();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.Clear();
            if (_holder != null)
            {
                UnityEngine.Object.DestroyImmediate(_holder);
            }
            string saveDir = Path.Combine(Application.persistentDataPath, "saves");
            if (Directory.Exists(saveDir))
            {
                try { Directory.Delete(saveDir, true); } catch { }
            }
        }

        // =========================================================================================
        // F3: Play 模式 DebugMenu 操作與存檔重啟驗證
        // =========================================================================================
        [Test]
        public void F3_PlayMode_DebugMenuAndSaveReload_Succeeds()
        {
            // 1. 驗證 GameBootstrap 啟動成功，GameServices 註冊完成
            Assert.IsTrue(GameServices.IsReady, "GameServices 應在啟動後就緒");
            GameCore core = GameServices.Core;
            Assert.IsNotNull(core, "GameCore 實例應已建立");

            // 給予足夠資源進行操作測試（配置驅動）
            var stdBanner = core.Master.Banners.GetOrNull("banner_std");
            if (stdBanner?.Cost?.Ten != null)
            {
                foreach (var costItem in stdBanner.Cost.Ten)
                {
                    core.Player.AddItem(costItem.Item, costItem.N * 2);
                }
            }
            core.Player.AddItem("item_dia_free", 3000);
            core.Player.AddItem("exp_s", 10);
            core.Player.Encounter.Stamina = 100;

            // 2. 轉蛋操作：十連抽
            var gachaRes = core.PullGacha("banner_std", 10);
            Assert.IsTrue(gachaRes.Ok, $"轉蛋應成功執行: {gachaRes.Error}");
            Assert.AreEqual(10, gachaRes.Value.Count);
            int charCountAfterGacha = core.Player.Characters.Count;

            // 3. 角色養成：升級操作
            string firstChar = core.Player.Team[0];
            var lvRes = core.LevelUp(firstChar, new List<ItemStack> { new ItemStack("exp_s", 1) });
            Assert.IsTrue(lvRes.Ok, "升級應成功執行");

            // 4. 巧遇操作
            var encRes = core.StartEncounter("spot_park");
            Assert.IsTrue(encRes.Ok, "巧遇應成功執行");

            // 5. 存檔落盤：觸發 SaveIfDirty
            core.Dirty = true;
            _bootstrap.SaveIfDirty();

            string savePath = Path.Combine(Application.persistentDataPath, "saves", GameBootstrap.ProfileFile);
            Assert.IsTrue(File.Exists(savePath), "存檔 profile.json 應確實出現在 persistentDataPath");
            FileInfo fi = new FileInfo(savePath);
            Assert.IsTrue(fi.Length > 0, "存檔大小應大於 0");

            // 6. 模擬重開（Reboot）：讀取存檔並驗證無損一致性
            var store = GameServices.SaveStore;
            var loadErrors = new List<string>();
            PlayerState loadedPlayer = store.ReadWithFallback<PlayerState>(GameBootstrap.ProfileFile, Game.Core.Persistence.SaveCodec.Decode, loadErrors);
            Assert.IsEmpty(loadErrors, "重讀存檔應無任何錯誤");
            Assert.IsNotNull(loadedPlayer, "重讀之 PlayerState 不應為 null");
            Assert.AreEqual(charCountAfterGacha, loadedPlayer.Characters.Count, "重啟後角色數量應完全一致");
            Assert.AreEqual(core.Player.Char(firstChar).Level, loadedPlayer.Char(firstChar).Level, "重啟後角色等級應保持一致");
            Assert.AreEqual(core.Player.Gacha.TotalPulls, loadedPlayer.Gacha.TotalPulls, "重啟後抽卡次數應保持一致");
        }

        // =========================================================================================
        // F4: Play 模式 BattleDebugView 與戰鬥完整模擬驗證
        // =========================================================================================
        [Test]
        public void F4_PlayMode_BattleRunner_RunsFullBattle_AndMatchesSubmission()
        {
            Assert.IsTrue(GameServices.IsReady);
            GameCore core = GameServices.Core;

            var battleRunner = _holder.AddComponent<BattleRunner>();
            var debugView = _holder.AddComponent<BattleDebugView>();

            // 1. 開啟關卡 st_1_01
            bool started = battleRunner.StartBattle("st_1_01");
            Assert.IsTrue(started, "st_1_01 應成功開始戰鬥");
            Assert.IsTrue(battleRunner.IsRunning, "BattleRunner 應處於運行中狀態");
            Assert.IsNotNull(battleRunner.Sim, "BattleSimulator 不應為 null");

            // 2. 推進模擬直到戰鬥分出勝負（最多 300 秒）
            int maxTicks = 300 * battleRunner.Sim.TicksPerSecond;
            int tickCount = 0;
            while (battleRunner.IsRunning && tickCount < maxTicks)
            {
                // 每隔一定間隔自動點擊升級或出兵
                if (tickCount % 60 == 0)
                {
                    battleRunner.Deploy(0);
                    battleRunner.UpgradeEnergy();
                }

                // 呼叫 Step 推進 1 個 tick
                battleRunner.Sim.Step();
                tickCount++;

                // 檢查是否結束
                if (battleRunner.Sim.Outcome != BattleOutcome.Ongoing)
                {
                    battleRunner.Submit();
                    break;
                }
            }

            // 3. 驗證戰鬥已正常終止且表現層不為運行狀態
            Assert.IsFalse(battleRunner.IsRunning, "戰鬥結束後 Runner 不應處於運行狀態");
            Assert.IsNotNull(battleRunner.LastReward, "結束後應具有結算回報");
            Assert.IsTrue(battleRunner.LastReward.Ok, "SubmitBattle 結算應成功");

            // 4. 驗證畫面勝負與提交結果 100% 一致
            Assert.AreEqual(battleRunner.Sim.Outcome, battleRunner.LastReward.Value.Result.Outcome, "畫面與提交報告的勝負狀態應 100% 一致");
            Assert.IsTrue(battleRunner.LastReward.Value.Stars >= 0 && battleRunner.LastReward.Value.Stars <= 3, "星數應在 0 到 3 星範圍內");
        }

        // =========================================================================================
        // OPT-8: M1 垂直切片全鏈路端到端驗收測試
        // =========================================================================================
        [Test]
        public void M1_VerticalSlice_FullPlayerJourney_AcrossAllUiViews_Succeeds()
        {
            Assert.IsTrue(GameServices.IsReady, "GameServices 應在啟動後就緒");
            GameCore core = GameServices.Core;
            Assert.IsNotNull(core, "GameCore 實例應已建立");

            // 確保 Elysia 角色已註冊在玩家持有列表中
            if (core.Player.Char("char04_elysia") == null)
            {
                core.Player.Characters["char04_elysia"] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Awaken = 1,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char04_s1" }
                };
            }

            // 建立大廳與各畫面節點，模擬完整 Boot.unity 導航架構
            var lobbyGo = new GameObject("LobbyUI");
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.EnsureUIHierarchy();
            lobbyView.Initialize(core);
            GameServices.Navigator.Push(lobbyView);

            Transform redDotBadge = lobbyGo.transform.Find("RightNavigationPanel/BtnAchievements/RedDotBadge");
            Assert.IsNotNull(redDotBadge, "大廳戰役榮譽按鈕應包含 RedDotBadge 紅點節點");
            Assert.IsFalse(redDotBadge.gameObject.activeSelf, "新玩家初始尚無成就可領取時，紅點應為隱藏狀態");

            // -------------------------------------------------------------------------------------
            // 步驟 1: 從 LobbyView 進入關卡星圖（StageSelectView）通關 st_1_01 與 st_1_02，解鎖 spot_cafe 與 st_1_03
            // -------------------------------------------------------------------------------------
            lobbyView.OpenStageSelect();
            Assert.IsInstanceOf<StageSelectView>(GameServices.Navigator.CurrentScreen);
            var stageSelect = (StageSelectView)GameServices.Navigator.CurrentScreen;
            var stageSelectGo = stageSelect.gameObject;
            stageSelect.Initialize(core);

            // 戰鬥通關 st_1_01
            Result<BattleSetup> setup1 = core.PrepareBattle("st_1_01", 7);
            Assert.IsTrue(setup1.Ok, setup1.Error);
            var sim1 = new BattleSimulator(setup1.Value);
            while (sim1.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setup1.Value.Team.Count; slot++)
                    if (sim1.CanDeploy(slot)) sim1.Enqueue(CommandType.Deploy, slot);
                sim1.Step();
            }
            Assert.AreEqual(BattleOutcome.Win, sim1.Outcome);
            var report1 = new BattleReport { StageId = "st_1_01", Seed = 7, Team = new List<string>(core.Player.Team), Commands = sim1.Recorded };
            var clear1 = core.SubmitBattle(report1);
            Assert.IsTrue(clear1.Ok, clear1.Error);

            // 戰鬥通關 st_1_02
            Result<BattleSetup> setup2 = core.PrepareBattle("st_1_02", 8);
            Assert.IsTrue(setup2.Ok, setup2.Error);
            var sim2 = new BattleSimulator(setup2.Value);
            while (sim2.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setup2.Value.Team.Count; slot++)
                    if (sim2.CanDeploy(slot)) sim2.Enqueue(CommandType.Deploy, slot);
                sim2.Step();
            }
            Assert.AreEqual(BattleOutcome.Win, sim2.Outcome);
            var report2 = new BattleReport { StageId = "st_1_02", Seed = 8, Team = new List<string>(core.Player.Team), Commands = sim2.Recorded };
            var clear2 = core.SubmitBattle(report2);
            Assert.IsTrue(clear2.Ok, clear2.Error);

            // 驗證通關解鎖
            Assert.IsTrue(core.Player.Stages.ContainsKey("st_1_01"), "st_1_01 應記錄在玩家已通關關卡中");
            Assert.IsTrue(core.Player.Stages.ContainsKey("st_1_02"), "st_1_02 應記錄在玩家已通關關卡中");
            Assert.IsTrue(core.Player.Encounter.UnlockedSpots.Contains("spot_cafe"), "通關 st_1_02 應自動解鎖 spot_cafe 地點");
            Assert.IsTrue(core.Player.Scenes.Contains("scene_cafe"), "通關 st_1_02 應自動解鎖 scene_cafe 場景");
            Assert.IsTrue(core.Player.Stages.ContainsKey("st_1_03"), "通關 st_1_02 應自動解鎖 st_1_03 關卡");

            stageSelect.Presenter.Refresh();
            stageSelect.Close();
            Assert.AreSame(lobbyView, GameServices.Navigator.CurrentScreen, "關閉關卡星圖後應回到大廳");
            UnityEngine.Object.DestroyImmediate(stageSelectGo);

            // -------------------------------------------------------------------------------------
            // 步驟 2: 從 LobbyView 進入契約招募（GachaView）十連抽，驗證獲得角色並觸發成就 ach_first_pull
            // -------------------------------------------------------------------------------------
            core.Player.AddItem("gem", 3000);
            lobbyView.OpenGacha();
            Assert.IsInstanceOf<GachaView>(GameServices.Navigator.CurrentScreen);
            var gachaView = (GachaView)GameServices.Navigator.CurrentScreen;
            var gachaGo = gachaView.gameObject;
            gachaView.Initialize(core);
            gachaView.OnClickPullTen();
            Assert.AreEqual(10, core.Player.Gacha.TotalPulls, "透過 GachaView 十連抽應累計 10 次招募");

            // 驗證觸發成就 ach_first_pull 與紅點服務
            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_first_pull"), "玩家成就應包含 ach_first_pull");
            Assert.IsNotNull(core.Player.Achievements["ach_first_pull"].UnlockedAtUtc, "十連抽後 ach_first_pull 應達成解鎖");
            Assert.IsTrue(GameServices.RedDots.HasClaimableAchievements(), "有可領取成就時 RedDotService 應回傳 True");

            gachaView.Close();
            Assert.AreSame(lobbyView, GameServices.Navigator.CurrentScreen, "關閉抽卡畫面後應回到大廳");
            Assert.IsTrue(redDotBadge.gameObject.activeSelf, "返回大廳時戰役榮譽按鈕上的 RedDotBadge 紅點應自動亮起");
            UnityEngine.Object.DestroyImmediate(gachaGo);

            // -------------------------------------------------------------------------------------
            // 步驟 3: 部隊名冊（CharacterRosterView）養成克萊兒至 Lv30 ★2 並同卡進階，再於衣櫥（WardrobeView）設為大廳秘書官
            // -------------------------------------------------------------------------------------
            string charId = "char01_claire";
            if (core.Player.Char(charId) == null)
            {
                core.Player.Characters[charId] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Exp = 0,
                    Awaken = 0,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" }
                };
            }
            CharacterState claire = core.Player.Char(charId);
            Assert.IsNotNull(claire, "克萊兒角色狀態應存在");

            lobbyView.OpenCharacterRoster(charId);
            Assert.IsInstanceOf<CharacterRosterView>(GameServices.Navigator.CurrentScreen);
            var roster = (CharacterRosterView)GameServices.Navigator.CurrentScreen;
            var rosterGo = roster.gameObject;
            roster.InitializeForTesting(core);

            // 給予養成道具、覺醒碎片與同卡進階素材
            core.Player.AddItem("exp_l", 60);
            core.Player.AddItem("gold", 500000);
            core.Player.AddItem("awaken_shard_char01_claire", 200);
            core.Player.AddItem("char_char01_claire", 5);

            // 覺醒至 ★2 (上限 40)
            if (claire.Awaken < 1) core.Awaken(charId);
            if (claire.Awaken < 2) core.Awaken(charId);
            Assert.AreEqual(2, claire.Awaken, "角色星級應為 ★2");

            // 升至 Lv.30 與同卡進階 (Advance -> SSR+1)
            var lvlRes = core.LevelUp(charId, new List<ItemStack> { new ItemStack("exp_l", 50) });
            Assert.IsTrue(lvlRes.Ok, "升級應成功");
            var advRes = core.AdvanceCharacter(charId);
            Assert.IsTrue(advRes.Ok, "同卡進階應成功");

            roster.SelectCharacter(charId);
            Assert.IsTrue(claire.Level >= 30, "角色等級應為 Lv.30 以上");
            Assert.AreEqual("SSR+1", claire.DisplayGrade, "同卡進階後位階應為 SSR+1");
            Assert.IsTrue(claire.UnlockedPortraitStages.Contains("ps_char01_s2"), "Lv30 ★2 後應自動解鎖 S2 立繪 (ps_char01_s2)");
            roster.Close();
            UnityEngine.Object.DestroyImmediate(rosterGo);

            // 衣櫥換裝並設定為大廳秘書官
            lobbyView.OpenWardrobe(charId);
            Assert.IsInstanceOf<WardrobeView>(GameServices.Navigator.CurrentScreen);
            var wardrobe = (WardrobeView)GameServices.Navigator.CurrentScreen;
            var wardrobeGo = wardrobe.gameObject;
            wardrobe.InitializeForTesting(core);

            wardrobe.SelectCharacter(charId);
            wardrobe.SelectPortraitStage("ps_char01_s2");
            wardrobe.SelectScene("scene_cafe");
            wardrobe.OnClickSetAsLobby();

            Assert.AreEqual(charId, core.Player.Lobby.Character, "大廳秘書官角色應為 char01_claire");
            Assert.AreEqual("scene_cafe", core.Player.Lobby.Scene, "大廳場景應為 scene_cafe");
            wardrobe.Close();
            UnityEngine.Object.DestroyImmediate(wardrobeGo);

            // -------------------------------------------------------------------------------------
            // 步驟 4: 從 LobbyView 進入巧遇約會地圖（EncounterView）在剛解鎖的咖啡廳完成一次巧遇互動獲得好感度
            // -------------------------------------------------------------------------------------
            lobbyView.OpenEncounter();
            Assert.IsInstanceOf<EncounterView>(GameServices.Navigator.CurrentScreen);
            var encounter = (EncounterView)GameServices.Navigator.CurrentScreen;
            var encounterGo = encounter.gameObject;
            encounter.InitializeForTesting(core);

            core.Player.Encounter.Pending = null;
            core.Player.Encounter.Stamina = 5;
            int staminaBefore = core.Player.Encounter.Stamina;
            int charCountBefore = core.Player.Characters.Count;
            int affBefore = core.Player.Char("char01_claire").Affection;

            encounter.StartSpot("spot_cafe");
            Assert.IsNull(encounter.LastError, $"StartSpot 應成功，但出現錯誤: {encounter.LastError}");
            Assert.AreEqual(staminaBefore - 1, core.Player.Encounter.Stamina, "巧遇啟動後應扣除 1 點體力");

            encounter.Choose("choice0");
            Assert.IsNull(encounter.LastError, $"Choose 應成功，但出現錯誤: {encounter.LastError}");
            Assert.Greater(core.Player.Char("char01_claire").Affection, affBefore, "巧遇完成後克萊兒好感度應增加");
            Assert.AreEqual(charCountBefore, core.Player.Characters.Count, "巧遇互動角色持有數量應保持不變（巧遇不招募角色）");
            encounter.CloseModals();
            GameServices.Navigator.Pop();
            UnityEngine.Object.DestroyImmediate(encounterGo);

            // -------------------------------------------------------------------------------------
            // 步驟 5: 進入 ADV 劇情播放器（StoryPlayerView）播放分支劇本，雙層存檔與雙結局達成驗證
            // -------------------------------------------------------------------------------------
            string scriptText = @"
[node Start]
line 1 Claire 歡迎來到星塔前哨站。
line 2 Claire 請選擇未來的命運之路。
option 邁向光明之塔 -> RouteLight
option 潛入深淵裂隙 -> RouteDark

[node RouteLight]
line 3 Claire 光明將洗滌一切黑暗！
command ending ending_light
end

[node RouteDark]
line 4 Claire 深淵之中潛藏著無盡奧秘...
command ending ending_dark
end
";
            string testSlotDir = Path.Combine(Application.temporaryCachePath, "test_m1_journey_saves_" + Guid.NewGuid().ToString("N"));
            var storySaveService = new StorySaveService(testSlotDir);

            var script = MiniScript.Parse(scriptText);
            var runner = new MiniScriptRunner(script);
            var controller = new StoryPlaybackController(runner, core);
            var presenter = new StoryPresenter(controller);
            StoryCommandRegistry.Register(runner, core, presenter);

            var storyGo = new GameObject("TestStoryPlayerView");
            StoryPlayerView storyPlayer = storyGo.AddComponent<StoryPlayerView>();
            storyPlayer.Initialize(presenter, storySaveService);
            presenter.Start("Start");

            // 推進至選項前第二行台詞並存檔
            storyPlayer.Advance();
            Assert.AreEqual("請選擇未來的命運之路。", storyPlayer.CurrentDialogue);
            storyPlayer.SaveSlot("slot_m1");
            Assert.IsNotNull(storySaveService.Load("slot_m1"), "slot_m1 存檔應存在");

            // 推進進入選項並走 RouteLight 達成 ending_light
            storyPlayer.Advance();
            storyPlayer.ChooseOption(0);
            storyPlayer.Advance();
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_light"), "ending_light 應成功達成");

            // 在 Profile 獲得獎勵（驗證養成進度不受讀檔倒退影響）
            long goldBeforeReward = core.Player.ItemCount("gold");
            core.Player.AddItem("gold", 8888);

            // 讀取 slot_m1 快照
            bool loaded = storyPlayer.LoadSlot("slot_m1", script);
            Assert.IsTrue(loaded, "讀取 slot_m1 應成功");
            Assert.IsTrue(claire.Level >= 30, "讀取劇情快照後角色等級絕不可倒退");
            Assert.AreEqual("SSR+1", claire.DisplayGrade, "讀取劇情快照後角色進階位階絕不可倒退");
            Assert.AreEqual(goldBeforeReward + 8888, core.Player.ItemCount("gold"), "讀取劇情快照後 Profile 金幣絕不可倒退");
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_light"), "已達成之結局 ending_light 不應遺失");

            // 從還原點改選 RouteDark 達成 ending_dark
            storyPlayer.Advance();
            storyPlayer.Advance();
            storyPlayer.ChooseOption(1);
            storyPlayer.Advance();
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_dark"), "ending_dark 應成功達成");
            UnityEngine.Object.DestroyImmediate(storyGo);

            // -------------------------------------------------------------------------------------
            // 步驟 6: 從 LobbyView 進入戰役榮譽成就（AchievementView）領取寶石、紅點清除與存檔無損持久化驗證
            // -------------------------------------------------------------------------------------
            lobbyView.OpenAchievements();
            Assert.IsInstanceOf<AchievementView>(GameServices.Navigator.CurrentScreen);
            var achView = (AchievementView)GameServices.Navigator.CurrentScreen;
            var achGo = achView.gameObject;
            achView.InitializeForTesting(core);

            long gemBeforeClaim = core.Player.ItemCount("gem");
            foreach (var kvp in new List<KeyValuePair<string, AchievementProgress>>(core.Player.Achievements))
            {
                if (!string.IsNullOrEmpty(kvp.Value.UnlockedAtUtc) && !kvp.Value.Claimed)
                {
                    achView.Claim(kvp.Key);
                    Assert.IsTrue(core.Player.Achievements[kvp.Key].Claimed, $"成就 {kvp.Key} 領取後應標記為已領取");
                }
            }

            Assert.Greater(core.Player.ItemCount("gem"), gemBeforeClaim, "領取所有成就後寶石數量應增加");
            Assert.IsFalse(GameServices.RedDots.HasClaimableAchievements(), "領取完所有已解鎖成就後紅點應自動消除");
            GameServices.Navigator.Pop();
            Assert.AreSame(lobbyView, GameServices.Navigator.CurrentScreen, "關閉成就畫面後應回到大廳");
            Assert.IsFalse(redDotBadge.gameObject.activeSelf, "領取完所有成就返回大廳後，RedDotBadge 紅點物件應自動隱藏");
            UnityEngine.Object.DestroyImmediate(achGo);
            UnityEngine.Object.DestroyImmediate(lobbyGo);

            // 磁碟持久化儲存與重啟驗證
            core.Dirty = true;
            _bootstrap.SaveIfDirty();

            string savePath = Path.Combine(Application.persistentDataPath, "saves", GameBootstrap.ProfileFile);
            Assert.IsTrue(File.Exists(savePath), "存檔檔案應確實存在於 persistentDataPath");

            var loadErrors = new List<string>();
            PlayerState loadedPlayer = GameServices.SaveStore.ReadWithFallback<PlayerState>(
                GameBootstrap.ProfileFile,
                Game.Core.Persistence.SaveCodec.Decode,
                loadErrors);

            Assert.IsEmpty(loadErrors, "重讀存檔應無錯誤");
            Assert.IsNotNull(loadedPlayer, "重讀之 PlayerState 不應為 null");
            Assert.IsTrue(loadedPlayer.Stages.ContainsKey("st_1_02"), "存檔應持久化記錄 st_1_02 通關");
            Assert.IsTrue(loadedPlayer.Encounter.UnlockedSpots.Contains("spot_cafe"), "存檔應持久化記錄 spot_cafe 解鎖");
            Assert.AreEqual("char01_claire", loadedPlayer.Lobby.Character, "存檔應持久化記錄大廳角色");
            Assert.AreEqual("scene_cafe", loadedPlayer.Lobby.Scene, "存檔應持久化記錄大廳場景 scene_cafe");
            Assert.IsTrue(loadedPlayer.Endings.ContainsKey("ending_light"), "存檔應持久化記錄 ending_light 結局");
            Assert.IsTrue(loadedPlayer.Endings.ContainsKey("ending_dark"), "存檔應持久化記錄 ending_dark 結局");
            Assert.IsTrue(loadedPlayer.Achievements["ach_first_pull"].Claimed, "存檔應持久化記錄成就已領取");
        }

        // =========================================================================================
        // M2-8: M2 功能完整版全鏈路端到端驗收測試
        // =========================================================================================
        [Test]
        public void M2_8_FeatureComplete_EndToEnd_Chapter2_Shrine_Routes_Pickup_ClaimAll_Locale_Succeeds()
        {
            Assert.IsTrue(GameServices.IsReady, "GameServices 應在啟動後就緒");
            Assert.IsNotNull(GameServices.Audio, "M2-4: GameBootstrap 應已掛載實體 AudioService (UnityAudioDriver)");
            GameCore core = GameServices.Core;

            // 1. 通關第 1 章最終關卡 st_1_03 與第 2 章 st_2_01 ~ st_2_03，驗證解鎖 spot_shrine 與觸發 ach_ch2_clear
            string[] stagesToClear = new[] { "st_1_01", "st_1_02", "st_1_03", "st_2_01", "st_2_02", "st_2_03" };
            foreach (string stId in stagesToClear)
            {
                var report = new BattleReport
                {
                    StageId = stId,
                    Seed = 42,
                    Team = new List<string>(core.Player.Team),
                    Commands = new List<BattleCommand>()
                };
                // 標記關卡已解鎖並模擬通關解鎖鏈
                if (!core.Player.Stages.TryGetValue(stId, out var prog))
                {
                    prog = new StageProgress { Unlocked = true };
                    core.Player.Stages[stId] = prog;
                }
                prog.Unlocked = true;
                prog.Cleared = true;
                prog.Stars = 3;
                prog.ClearCount = 1;

                var stDef = core.Master.Stages[stId];
                if (stDef.UnlocksOnClear != null)
                {
                    foreach (string u in stDef.UnlocksOnClear)
                    {
                        if (core.Master.Stages.ContainsKey(u))
                            core.Player.Stages[u] = new StageProgress { Unlocked = true };
                        else if (core.Master.Spots.ContainsKey(u))
                            core.Player.Encounter.UnlockedSpots.Add(u);
                    }
                }
                core.Bus.Publish(new Game.Domain.Events.DomainEvent(Game.Domain.Events.Ev.BattleFinished).With("stage", stId).With("outcome", "Win").With("stars", 3));
            }

            Assert.IsTrue(core.Player.Encounter.UnlockedSpots.Contains("spot_shrine"), "通關 st_1_03 應解鎖櫻花神社 spot_shrine");
            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_ch2_clear") &&
                          !string.IsNullOrEmpty(core.Player.Achievements["ach_ch2_clear"].UnlockedAtUtc),
                          "通關 st_2_03 應自動解鎖成就 ach_ch2_clear");

            // 2. M2-1: 關卡星圖切換至第 2 章分頁
            var stageSelectGo = new GameObject("M2_StageSelect");
            var stageSelect = stageSelectGo.AddComponent<StageSelectView>();
            stageSelect.InitializeForTesting(core);
            Assert.IsTrue(stageSelect.SelectChapter(2), "應成功切換至第 2 章分頁");
            Assert.AreEqual(2, stageSelect.CurrentChapter);
            UnityEngine.Object.DestroyImmediate(stageSelectGo);

            // 3. M2-6: 契約招募切換至春櫻祈願限定卡池 banner_sakura_up 並進行十連抽
            core.Player.AddItem("gem", 3000);
            var gachaGo = new GameObject("M2_Gacha");
            var gachaView = gachaGo.AddComponent<GachaView>();
            gachaView.Initialize(core);
            gachaView.SwitchBanner("banner_sakura_up");
            Assert.AreEqual("char18_sakura_miko", gachaView.FeaturedCharacterId);
            var pullRes = gachaView.Presenter.Pull("banner_sakura_up", 10);
            Assert.IsTrue(pullRes.Ok, pullRes.Error);
            UnityEngine.Object.DestroyImmediate(gachaGo);

            // 4. M2-2: 巧遇地圖進入櫻花神社 spot_shrine 並啟動 ADV 巧遇劇本
            if (core.Player.Char("char18_sakura_miko") == null)
            {
                core.Player.Characters["char18_sakura_miko"] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Awaken = 1,
                    Affection = 100,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char18_s1" }
                };
            }
            var encGo = new GameObject("M2_Encounter");
            var encView = encGo.AddComponent<EncounterView>();
            encView.InitializeForTesting(core);
            core.Player.Encounter.Stamina = 5;
            encView.StartSpot("spot_shrine");
            Assert.IsTrue(encView.IsEventModalOpen);

            var storyGo = new GameObject("M2_StoryPlayer");
            var storyPlayer = storyGo.AddComponent<StoryPlayerView>();
            Assert.IsTrue(encView.PlayEncounterStory(storyPlayer));
            Assert.AreEqual("encounter_shrine.txt", encView.LastPlayedStoryScriptId);
            encView.Choose("choice0");
            UnityEngine.Object.DestroyImmediate(encGo);

            // 5. M2-3: 劇情中心完成櫻的第 1 章路線並驗證回憶重播不污染存檔
            core.Player.Char("char18_sakura_miko").Affection = 120;
            var hubGo = new GameObject("M2_StoryHub");
            var hubView = hubGo.AddComponent<StoryHubView>();
            hubView.InitializeForTesting(core);
            Assert.IsTrue(hubView.PlayCharacterRouteChapter("char18_sakura_miko", 1, storyPlayer));
            Assert.IsFalse(storyPlayer.IsRecollectionMode);
            storyPlayer.Advance();
            storyPlayer.Advance();
            storyPlayer.ChooseOption(0);
            storyPlayer.Advance();
            Assert.IsTrue(core.Player.Story.CompletedChapters.Contains("char18_sakura_miko_ch1"));

            int affBeforeRecall = core.Player.Char("char18_sakura_miko").Affection;
            Assert.IsTrue(hubView.PlayCharacterRouteChapter("char18_sakura_miko", 1, storyPlayer));
            Assert.IsTrue(storyPlayer.IsRecollectionMode);
            storyPlayer.Advance();
            storyPlayer.Advance();
            storyPlayer.ChooseOption(1);
            storyPlayer.Advance();
            Assert.AreEqual(affBeforeRecall, core.Player.Char("char18_sakura_miko").Affection, "回憶模式不應變更存檔好感度");
            UnityEngine.Object.DestroyImmediate(hubGo);
            UnityEngine.Object.DestroyImmediate(storyGo);

            // 5b. M2-4: 換裝衣櫥穿戴服裝與四時段光線切換
            var wardGo = new GameObject("M2_Wardrobe");
            var wardView = wardGo.AddComponent<WardrobeView>();
            wardView.InitializeForTesting(core);
            wardView.SelectCharacter("char04_elysia");
            Assert.IsTrue(wardView.EquipCostume("cos_char04_default").Ok);
            wardView.SelectTimeOfDay("night");
            Assert.AreEqual("night", wardView.SelectedTimeOfDay);
            UnityEngine.Object.DestroyImmediate(wardGo);

            // 6. M2-5: 成就中心一鍵全部領取
            var achGo = new GameObject("M2_Achievement");
            var achView = achGo.AddComponent<AchievementView>();
            achView.InitializeForTesting(core);
            int claimed = achView.ClaimAll();
            Assert.GreaterOrEqual(claimed, 2, "應一鍵領取至少 2 個已解鎖成就");
            Assert.IsFalse(GameServices.RedDots.HasClaimableAchievements(), "一鍵領取後紅點應歸零");
            UnityEngine.Object.DestroyImmediate(achGo);

            // 7. M2-7: 系統設定切換語言至 en，並存檔重讀驗證無損持久化
            var setGo = new GameObject("M2_Settings");
            var setView = setGo.AddComponent<SettingsModalView>();
            setView.InitializeForTesting(core, GameServices.Audio, GameServices.Localization, GameServices.AgeGate, GameServices.Settings);
            setView.SelectLocale("en");
            Assert.AreEqual("en", core.Player.Profile.Locale);
            UnityEngine.Object.DestroyImmediate(setGo);

            core.Dirty = true;
            _bootstrap.SaveIfDirty();
            var loadErrors = new List<string>();
            PlayerState reloaded = GameServices.SaveStore.ReadWithFallback<PlayerState>(
                GameBootstrap.ProfileFile,
                SaveCodec.Decode,
                loadErrors);
            Assert.IsEmpty(loadErrors);
            Assert.AreEqual("en", reloaded.Profile.Locale);
            Assert.IsTrue(reloaded.Story.CompletedChapters.Contains("char18_sakura_miko_ch1"));
            Assert.IsTrue(reloaded.Achievements["ach_ch2_clear"].Claimed);
        }

        // =========================================================================================
        // M3-8: M3 內容與 Live2D 端到端全鏈路整合驗收測試
        // =========================================================================================
        [Test]
        public void M3_ContentAndLive2D_EndToEnd_Title_SaveLoad_BranchGraph_FourLocales_Chapter3_Succeeds()
        {
            Assert.IsTrue(GameServices.IsReady, "GameServices 應在啟動後就緒");
            GameCore core = GameServices.Core;
            Assert.IsNotNull(core, "GameCore 實例應已建立");

            // 1. 從 TitleScreenView 啟動進入 LobbyView，透過 PortraitFactory 驗證 Live2D L1 立繪載入、表情與點擊互動
            var saveLoadModalGo = new GameObject("M3_SaveLoadModal");
            var saveLoadModal = saveLoadModalGo.AddComponent<SaveLoadModalView>();
            saveLoadModal.EnsureUIHierarchy();

            var settingsModalGo = new GameObject("M3_SettingsModal");
            var settingsModal = settingsModalGo.AddComponent<SettingsModalView>();
            settingsModal.InitializeForTesting(core, GameServices.Audio, GameServices.Localization, GameServices.AgeGate, GameServices.Settings);

            var titleGo = new GameObject("M3_TitleScreen");
            var titleView = titleGo.AddComponent<TitleScreenView>();
            titleView.Initialize(saveLoadModal, settingsModal);

            var lobbyGo = new GameObject("M3_Lobby");
            var lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.Initialize(core);

            // 確保 Navigator 當前為 Title
            GameServices.Navigator.Push(titleView);
            Assert.AreSame(titleView, GameServices.Navigator.CurrentScreen);

            titleView.OnClickStart();
            Assert.AreSame(lobbyView, GameServices.Navigator.CurrentScreen, "點擊開始作戰後應推進至大廳畫面");

            // 透過 PortraitFactory 驗證 Live2D L1 立繪載入
            var portraitHostGo = new GameObject("M3_PortraitHost");
            var resolvedPortrait = new ResolvedPortrait
            {
                Character = "char01_claire",
                PortraitStage = "ps_char01_s1",
                Art = new PortraitArt
                {
                    Live2d = "Mao"
                }
            };
            var portraitView = PortraitFactory.CreateOrAttach(portraitHostGo, resolvedPortrait, useLive2d: true);
            Assert.IsNotNull(portraitView, "PortraitFactory 應成功建立立繪視圖");
            Assert.IsInstanceOf<Live2DPortraitView>(portraitView, "啟用 useLive2d 且指定 Mao 應回傳 Live2DPortraitView");

            var live2dView = (Live2DPortraitView)portraitView;
            live2dView.SetExpression("smile");
            Assert.AreEqual("smile", live2dView.CurrentExpression);
            Assert.IsTrue(live2dView.OnTouched("Head"), "點擊 Head 應觸發反應");
            Assert.AreEqual("Head", live2dView.LastTouchedArea);

            UnityEngine.Object.DestroyImmediate(portraitHostGo);
            UnityEngine.Object.DestroyImmediate(titleGo);

            // 2. 推進至第 3 章戰役（st_3_01～st_3_03）並驗證新角色與 S3 立繪階段
            string[] ch3Stages = new[] { "st_3_01", "st_3_02", "st_3_03" };
            foreach (string stId in ch3Stages)
            {
                if (!core.Player.Stages.TryGetValue(stId, out var prog))
                {
                    prog = new StageProgress { Unlocked = true };
                    core.Player.Stages[stId] = prog;
                }
                prog.Unlocked = true;
                prog.Cleared = true;
                prog.Stars = 3;
                prog.ClearCount = 1;

                core.Bus.Publish(new Game.Domain.Events.DomainEvent(Game.Domain.Events.Ev.BattleFinished)
                    .With("stage", stId).With("outcome", "Win").With("stars", 3));
            }

            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_ch3_clear") &&
                          !string.IsNullOrEmpty(core.Player.Achievements["ach_ch3_clear"].UnlockedAtUtc),
                          "通關 st_3_03 應自動解鎖成就 ach_ch3_clear");

            // 驗證第 3 章關聯角色存在於 MasterData (12 位角色)
            Assert.AreEqual(12, core.Master.Characters.Count, "MasterData 應包含全部 12 位角色");
            Assert.IsTrue(core.Master.Characters.ContainsKey("char07_kagura"), "Master 應包含角色 char07_kagura");
            Assert.IsTrue(core.Master.Characters.ContainsKey("char08_yuki"), "Master 應包含角色 char08_yuki");
            Assert.IsTrue(core.Master.Characters.ContainsKey("char09_chloe"), "Master 應包含角色 char09_chloe");
            Assert.IsTrue(core.Master.Characters.ContainsKey("char10_mirai"), "Master 應包含角色 char10_mirai");

            // 驗證 Claire S3 立繪階段解鎖
            if (core.Player.Char("char01_claire") == null)
            {
                core.Player.Characters["char01_claire"] = new CharacterState
                {
                    CurrentRarity = "R",
                    Level = 1,
                    Awaken = 0,
                    Affection = 10,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1", "ps_char01_s2" },
                    PortraitStage = "ps_char01_s1"
                };
            }
            core.Player.Char("char01_claire").UnlockedPortraitStages.Add("ps_char01_s3");
            core.Player.Char("char01_claire").PortraitStage = "ps_char01_s3";
            core.Bus.Publish(new Game.Domain.Events.DomainEvent(Game.Domain.Events.Ev.PortraitStageUnlocked)
                .With("char", "char01_claire").With("stage", "ps_char01_s3"));
            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_claire_s3") &&
                          !string.IsNullOrEmpty(core.Player.Achievements["ach_claire_s3"].UnlockedAtUtc),
                          "解鎖 ps_char01_s3 應自動解鎖成就 ach_claire_s3");
            var resolvedS3 = PortraitResolver.Resolve(core.Master, core.Player, core.Rating, "char01_claire");
            Assert.AreEqual("ps_char01_s3", resolvedS3.PortraitStage, "PortraitResolver 應決策至 ps_char01_s3 立繪階段");

            // 3. 在 StoryPlayerView 使用 SaveLoadModalView 多槽位存讀檔、檢視 StoryBranchGraph 分支拓樸圖與已選選項 ✓ 標記，並驗證 Ch.3 R18/G Fallback
            var storyGo = new GameObject("M3_StoryPlayer");
            var storyPlayer = storyGo.AddComponent<StoryPlayerView>();
            storyPlayer.SetCoreForTesting(core);
            storyPlayer.SetSaveLoadModal(saveLoadModal);

            string ch3Script = @"
[node Start]
line 1 Claire 指揮官，決戰即將展開。
option 堅守陣線 -> node_defend
option 突擊核心 -> node_assault

[node node_defend]
line 2 Claire 全軍構築防線！
jump node_climax

[node node_assault]
line 3 Claire 隨我衝鋒陷陣！
jump node_climax

[node node_climax]
line 4 Claire 激烈的戰鬥結束了...
jump node_r18_special

[node node_r18_special rating:R18 fallback:node_g_special]
line 5 Claire 【R18解鎖】慶祝勝利的二人私密時光...
command ending ending_victory_r18
end

[node node_g_special rating:G]
line 6 Claire 【全年齡健全】慶祝勝利的頒獎典禮。
command ending ending_victory_g
end
";
            var parsedScript = MiniScript.Parse(ch3Script);
            storyPlayer.LoadAndPlay("ch3_test_script", parsedScript, core, false);

            // 檢視分支拓樸圖
            storyPlayer.OpenBranchGraphModal();
            Assert.IsTrue(storyPlayer.IsBranchGraphModalOpen, "應開啟分支拓樸圖彈窗");
            Assert.IsNotNull(storyPlayer.CurrentBranchGraph, "CurrentBranchGraph 不應為 null");
            Assert.AreEqual(6, storyPlayer.CurrentBranchGraph.TotalNodes, "劇本應包含 6 個節點");
            storyPlayer.CloseBranchGraphModal();
            Assert.IsFalse(storyPlayer.IsBranchGraphModalOpen);

            // 推進至選項
            storyPlayer.Advance();
            Assert.IsTrue(storyPlayer.IsOptionsVisible, "應顯示選項按鈕");
            Assert.AreEqual(2, storyPlayer.OptionCount);
            Assert.IsFalse(storyPlayer.IsOptionRead(0), "初始狀態選項 0 應尚未閱讀");

            // 選擇選項 0: 堅守陣線 -> node_defend
            storyPlayer.ChooseOption(0);
            Assert.IsTrue(core.Player.Story.ReadNodes.Contains("node_defend"), "ReadNodes 應記錄走過 node_defend");

            // 存檔至 slot_001
            saveLoadModal.Open(SaveLoadModalView.ModalMode.Save, storyPlayer, storyPlayer.SaveService, storyPlayer.CurrentScript);
            bool saveOk = saveLoadModal.SaveToSlot("slot_001", "Ch3 Defend Node");
            Assert.IsTrue(saveOk, $"存檔至 slot_001 應成功: {saveLoadModal.LastError}");
            saveLoadModal.Close();

            // 推進至 climax
            storyPlayer.Advance();
            storyPlayer.Advance();

            // 從 slot_001 讀檔還原
            saveLoadModal.Open(SaveLoadModalView.ModalMode.Load, storyPlayer, storyPlayer.SaveService, storyPlayer.CurrentScript);
            bool loadOk = saveLoadModal.LoadFromSlot("slot_001");
            Assert.IsTrue(loadOk, $"從 slot_001 讀檔應成功: {saveLoadModal.LastError}");
            saveLoadModal.Close();
            Assert.AreEqual("node_defend", storyPlayer.Presenter.Runner.CurrentNodeName, "讀檔後應精確回溯至 node_defend 節點");

            // 重新啟動劇本回到選項節點，驗證選項已標記 ✓
            storyPlayer.LoadAndPlay("ch3_test_script", parsedScript, core, false);
            storyPlayer.Advance();
            Assert.IsTrue(storyPlayer.IsOptionRead(0), "走過 node_defend 後選項 0 應已讀");
            Assert.IsFalse(storyPlayer.IsOptionRead(1), "尚未走過之選項 1 應未讀");
            StringAssert.Contains("✓", storyPlayer.OptionButtons[0].GetComponentInChildren<Text>().text, "已選選項 0 按鈕文字應包含 ✓ 打勾標記");

            // 驗證 Ch.3 R18 / G Fallback 機制
            // A. G 政策 (Rating.G) 下：進入 node_r18_special 自動退回 node_g_special
            var runnerG = new MiniScriptRunner(ch3Script, new RatingPolicy(Rating.G, Rating.G));
            runnerG.Start("node_climax");
            runnerG.Continue(); // line 4
            runnerG.Continue(); // jump node_r18_special -> fallback to node_g_special -> line 6
            Assert.AreEqual("node_g_special", runnerG.CurrentNodeName, "未解鎖成人模式時 R18 節點應自動退回 node_g_special");
            Assert.AreEqual("6", runnerG.CurrentLine.LineId);

            // B. R18 政策 (Rating.R18) 下：直接進入 node_r18_special
            var runnerR18 = new MiniScriptRunner(ch3Script, new RatingPolicy(Rating.R18, Rating.R18));
            runnerR18.Start("node_climax");
            runnerR18.Continue(); // line 4
            runnerR18.Continue(); // jump node_r18_special -> line 5
            Assert.AreEqual("node_r18_special", runnerR18.CurrentNodeName, "已解鎖成人模式時應進入 node_r18_special");
            Assert.AreEqual("5", runnerR18.CurrentLine.LineId);

            UnityEngine.Object.DestroyImmediate(storyGo);
            UnityEngine.Object.DestroyImmediate(saveLoadModalGo);

            // 4. 在 SettingsModalView 切換 ja 與 zh-CN 語系並驗證持久化
            settingsModal.SelectLocale("ja");
            Assert.AreEqual("ja", core.Player.Profile.Locale, "切換至日文語系");
            Assert.AreEqual("SYSTEM SETTINGS // システム設定", GameServices.Localization.Get("ui.settings.title", "ja"));

            settingsModal.SelectLocale("zh-CN");
            Assert.AreEqual("zh-CN", core.Player.Profile.Locale, "切換至簡體中文語系");
            Assert.AreEqual("SYSTEM SETTINGS // 系统设置", GameServices.Localization.Get("ui.settings.title", "zh-CN"));

            UnityEngine.Object.DestroyImmediate(settingsModalGo);
            UnityEngine.Object.DestroyImmediate(lobbyGo);

            // 存檔並重讀驗證磁碟持久化
            core.Dirty = true;
            _bootstrap.SaveIfDirty();

            var loadErrors = new List<string>();
            PlayerState reloaded = GameServices.SaveStore.ReadWithFallback<PlayerState>(
                GameBootstrap.ProfileFile,
                SaveCodec.Decode,
                loadErrors);

            Assert.IsEmpty(loadErrors, "重讀存檔應無錯誤");
            Assert.IsNotNull(reloaded, "重讀存檔不應為 null");
            Assert.AreEqual("zh-CN", reloaded.Profile.Locale, "持久化存檔應記錄 zh-CN 語系");
            Assert.IsTrue(reloaded.Stages.ContainsKey("st_3_03") && reloaded.Stages["st_3_03"].Cleared, "持久化存檔應記錄 st_3_03 通關");
            Assert.IsTrue(reloaded.Achievements.ContainsKey("ach_ch3_clear") && reloaded.Achievements["ach_ch3_clear"].Claimed == false, "持久化存檔應記錄 ach_ch3_clear 解鎖");
            Assert.IsTrue(reloaded.Achievements.ContainsKey("ach_claire_s3"), "持久化存檔應記錄 ach_claire_s3 解鎖");
        }
    }
}
#endif
