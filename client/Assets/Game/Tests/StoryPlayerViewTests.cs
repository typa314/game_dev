#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Gallery;
using Game.Core.Persistence;
using Game.Core.Platform;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Unity;
using Game.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    [TestFixture]
    public sealed class StoryPlayerViewTests
    {
        private List<GameObject> _createdObjects;
        private MasterDatabase _master;
        private GameCore _core;
        private ScreenNavigator _nav;

        private sealed class SystemClock : IClock
        {
            public DateTime UtcNow => DateTime.UtcNow;
            public DateTime LocalNow => DateTime.Now;
        }

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            PlayerState player = GameCore.NewPlayer(_master, clock);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            _core = new GameCore(_master, player, rng, clock, rating);

            if (!_core.Player.Characters.ContainsKey("char01_claire"))
            {
                _core.Player.Characters["char01_claire"] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Awaken = 1,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" }
                };
            }

            _nav = new ScreenNavigator();
            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_opt6_saves_" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(tempDir);
            GameServices.Install(_core, store, new AssetService(rating), rating, new NullPlatformService(), null, navigator: _nav);
        }

        [TearDown]
        public void TearDown()
        {
            if (_createdObjects != null)
            {
                for (int i = 0; i < _createdObjects.Count; i++)
                {
                    if (_createdObjects[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                    }
                }
                _createdObjects.Clear();
            }
        }

        private GameObject Track(GameObject go)
        {
            _createdObjects.Add(go);
            return go;
        }

        private const string TestBranchScript = @"
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

        [Test]
        public void OPT6_1_StoryPlayerView_BranchingOptions_And_EndingUnlock_PositiveControl()
        {
            var script = MiniScript.Parse(TestBranchScript);
            var runner = new MiniScriptRunner(script);
            var controller = new StoryPlaybackController(runner, _core);
            var presenter = new StoryPresenter(controller);
            StoryCommandRegistry.Register(runner, _core, presenter);

            GameObject go = Track(new GameObject("TestStoryPlayer"));
            StoryPlayerView view = go.AddComponent<StoryPlayerView>();
            view.Initialize(presenter);
            presenter.Start("Start");

            // 1. 推進第一行台詞
            Assert.AreEqual("Claire", view.CurrentSpeaker);
            Assert.AreEqual("歡迎來到星塔前哨站。", view.CurrentDialogue);
            Assert.IsFalse(view.IsOptionsVisible, "第一行不應出現選項");

            // 2. 推進第二行台詞
            view.Advance();
            Assert.AreEqual("Claire", view.CurrentSpeaker);
            Assert.AreEqual("請選擇未來的命運之路。", view.CurrentDialogue);

            // 3. 推進至分支選項
            view.Advance();
            Assert.IsTrue(view.IsOptionsVisible, "應顯示分支選項");
            Assert.AreEqual(2, view.OptionCount, "應有 2 個選項按鈕");

            // 4. 陽性對照：在選項未選前呼叫 Advance，不應越過選項
            view.Advance();
            Assert.IsTrue(view.IsOptionsVisible, "陽性對照：未做選擇前選項面板不可關閉");
            Assert.AreEqual("Start", runner.CurrentNodeName, "陽性對照：未做選擇前不可越過節點");

            // 5. 點選選項 0 (邁向光明之塔 -> RouteLight)
            view.ChooseOption(0);
            Assert.IsFalse(view.IsOptionsVisible, "選擇後選項面板應關閉");
            Assert.AreEqual("RouteLight", runner.CurrentNodeName);
            Assert.AreEqual("光明將洗滌一切黑暗！", view.CurrentDialogue);

            // 6. 推進至結局指令
            view.Advance();
            Assert.IsTrue(runner.IsEnded, "劇本應抵達終點");
            Assert.IsTrue(_core.Player.Endings.ContainsKey("ending_light"), "應成功解鎖 ending_light 結局");
        }

        [Test]
        public void OPT6_2_TwoTierSaveLoad_PreservesProfileAcrossBranchingAndReload()
        {
            string saveDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_opt6_slots_" + Guid.NewGuid().ToString("N"));
            var saveService = new StorySaveService(saveDir);

            var script = MiniScript.Parse(TestBranchScript);
            var runner = new MiniScriptRunner(script);
            var controller = new StoryPlaybackController(runner, _core);
            var presenter = new StoryPresenter(controller);
            StoryCommandRegistry.Register(runner, _core, presenter);

            GameObject go = Track(new GameObject("TestStoryPlayer2"));
            StoryPlayerView view = go.AddComponent<StoryPlayerView>();
            view.Initialize(presenter, saveService);
            presenter.Start("Start");

            // 推進至第二行台詞（選項前）
            view.Advance();
            Assert.AreEqual("請選擇未來的命運之路。", view.CurrentDialogue);

            // 1. 存入 slot_001
            view.SaveSlot("slot_001");
            Assert.IsNotNull(saveService.Load("slot_001"), "slot_001 應存在");

            // 2. 推進至選項並走 RouteLight 達成 ending_light
            view.Advance(); // 進入選項
            view.ChooseOption(0); // RouteLight
            view.Advance(); // 抵達 ending_light
            Assert.IsTrue(_core.Player.Endings.ContainsKey("ending_light"));

            // 3. 在外層 Profile 升級角色（代表全域養成狀態改變）
            CharacterState claire = _core.Player.Char("char01_claire");
            Assert.IsNotNull(claire);
            Assert.AreEqual(1, claire.Level);
            claire.Level = 25; // 升至 Lv.25

            // 4. 從 StorySaveService 讀取 slot_001
            bool loadSuccess = view.LoadSlot("slot_001", script);
            Assert.IsTrue(loadSuccess, "讀取劇情存檔應成功");

            // 驗證雙層存檔架構隔離性：Profile 養成角色等級不可倒退！
            Assert.AreEqual(25, claire.Level, "讀取劇情存檔時 Profile 角色等級絕不倒退");
            Assert.IsTrue(_core.Player.Endings.ContainsKey("ending_light"), "已達成的結局紀錄不應被覆寫或抹除");

            // 5. 從還原的節點改選 RouteDark 達成 ending_dark
            view.Advance(); // 抵達第 2 行台詞
            view.Advance(); // 進入選項
            view.ChooseOption(1); // RouteDark
            view.Advance(); // 抵達 ending_dark

            // 驗證兩組結局同時被記錄在全域 Profile 中
            Assert.IsTrue(_core.Player.Endings.ContainsKey("ending_light"), "結局 ending_light 依然保留");
            Assert.IsTrue(_core.Player.Endings.ContainsKey("ending_dark"), "結局 ending_dark 成功追加");
            Assert.AreEqual(25, claire.Level, "全域角色等級始終維持 Lv.25");
        }

        [Test]
        public void OPT6_3_StoryHubView_Gallery_FiltersByRating_And_PreviewsCg()
        {
            var galleryService = new GalleryService();
            galleryService.AddCg(new CgDef("cg_char01_01", "char01_claire", Rating.G, "Claire 日常", "art_cg_01"));
            galleryService.AddCg(new CgDef("cg_char01_r18", "char01_claire", Rating.R18, "Claire 秘密", "art_cg_r18"));

            _core.Player.Gallery.Add("cg_char01_01");
            _core.Player.Gallery.Add("cg_char01_r18");

            GameObject hubGo = Track(new GameObject("TestStoryHub"));
            StoryHubView hubView = hubGo.AddComponent<StoryHubView>();
            hubView.InitializeForTesting(_core, galleryService);

            // 1. 切換至 CG 圖鑑分頁
            hubView.SwitchTab(StoryHubView.TAB_GALLERY);
            Assert.AreEqual(StoryHubView.TAB_GALLERY, hubView.CurrentTab);

            // 2. 驗證 G 分級政策下：R18 CG 絕對不列出 (ARCH-07 分級隔離)
            var grouped = galleryService.GetGroupedGallery(_core.Player, _core.Rating);
            Assert.IsTrue(grouped.ContainsKey("char01_claire"));
            var claireCgs = grouped["char01_claire"];
            Assert.AreEqual(1, claireCgs.Count, "G 分級下只應包含 1 張全年齡 CG");
            Assert.AreEqual("cg_char01_01", claireCgs[0].Def.Id);

            // 3. 測試 CG 全螢幕預覽彈窗開關
            Assert.IsFalse(hubView.IsCgPreviewOpen);
            hubView.PreviewCg("cg_char01_01");
            Assert.IsTrue(hubView.IsCgPreviewOpen);
            Assert.AreEqual("cg_char01_01", hubView.LastPreviewedCg);

            hubView.CloseCgPreview();
            Assert.IsFalse(hubView.IsCgPreviewOpen);
        }

        [Test]
        public void OPT6_4_StoryPlayerView_Backlog_And_UiVisibility_And_LobbyNavigation()
        {
            var script = MiniScript.Parse(TestBranchScript);
            var runner = new MiniScriptRunner(script);
            var controller = new StoryPlaybackController(runner, _core);
            var presenter = new StoryPresenter(controller);

            GameObject playerGo = Track(new GameObject("TestStoryPlayer3"));
            StoryPlayerView playerView = playerGo.AddComponent<StoryPlayerView>();
            playerView.Initialize(presenter);
            presenter.Start("Start");

            // 1. Backlog 彈窗開關
            Assert.IsFalse(playerView.IsBacklogOpen);
            playerView.OpenBacklog();
            Assert.IsTrue(playerView.IsBacklogOpen);
            playerView.CloseBacklog();
            Assert.IsFalse(playerView.IsBacklogOpen);

            // 2. UI 隱藏切換
            Assert.IsTrue(playerView.IsUiVisible);
            playerView.ToggleHideUi();
            Assert.IsFalse(playerView.IsUiVisible);
            playerView.ToggleHideUi();
            Assert.IsTrue(playerView.IsUiVisible);

            // 3. 大廳導航至 StoryHubView
            GameObject lobbyGo = Track(new GameObject("TestLobby"));
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.Initialize(_core);

            _nav.Push(lobbyView);
            Assert.AreEqual(1, _nav.Count);
            Assert.AreSame(lobbyView, _nav.CurrentScreen);

            // 點擊開啟劇情中心
            lobbyView.OpenStoryHub();
            Assert.AreEqual(2, _nav.Count);
            Assert.IsInstanceOf<StoryHubView>(_nav.CurrentScreen);

            // 按下返回鍵回到大廳
            bool back = _nav.HandleBackRequest();
            Assert.IsTrue(back);
            Assert.AreEqual(1, _nav.Count);
            Assert.AreSame(lobbyView, _nav.CurrentScreen);
        }

        [Test]
        public void M2_3_StoryHubView_CharacterRoutes_FirstPlayCompletes_RecollectionModeLeavesSaveUnchanged()
        {
            GameObject hubGo = Track(new GameObject("TestStoryHubM2"));
            StoryHubView hubView = hubGo.AddComponent<StoryHubView>();
            hubView.InitializeForTesting(_core);
            hubView.SwitchTab(StoryHubView.TAB_ROUTES);

            GameObject playerGo = Track(new GameObject("TestStoryPlayerM2"));
            StoryPlayerView playerView = playerGo.AddComponent<StoryPlayerView>();

            var claire = _core.Player.Char("char01_claire");
            Assert.IsNotNull(claire);

            // 1. 陽性對照：好感度 0 (Lv.1) 時嘗試挑戰第 2 章 (需 Lv.2 與前置章節)，應遭鎖定攔截
            claire.Affection = 0;
            bool lockedPlay = hubView.PlayCharacterRouteChapter("char01_claire", 2, playerView);
            Assert.IsFalse(lockedPlay, "未達好感等級 Lv.2 時不可播放第 2 章");
            Assert.AreEqual("chapter_locked", hubView.LastError);

            // 2. 提升好感度至 100 (Lv.1+)，首次挑戰第 1 章 (非回憶模式)
            claire.Affection = 100;
            int affBeforeFirstPlay = claire.Affection;
            bool firstPlay = hubView.PlayCharacterRouteChapter("char01_claire", 1, playerView);
            Assert.IsTrue(firstPlay, "達好感門檻後應可啟動第 1 章");
            Assert.IsFalse(playerView.IsRecollectionMode, "首次挑戰不應為回憶模式");

            // 推進劇本完成第 1 章 (Start -> ChoiceNode -> 選 0 -> PraiseBranch -> End)
            playerView.Advance(); // ChoiceNode line
            playerView.Advance(); // Show options
            Assert.IsTrue(playerView.IsOptionsVisible);
            playerView.ChooseOption(0); // PraiseBranch (set route_char01_ch1_choice = 1, affection +25)
            playerView.Advance(); // End

            Assert.IsTrue(_core.Player.Story.CompletedChapters.Contains("char01_claire_ch1"), "首次播畢後應自動標記 char01_claire_ch1 已通關");
            Assert.AreEqual(affBeforeFirstPlay + 25, claire.Affection, "首次播放應實際增加好感度");
            Assert.AreEqual(1.0, _core.Player.Story.Flags["route_char01_ch1_choice"], 1e-6, "首次播放應記錄路線選擇旗標");

            // 3. 回憶重播模式：再次點擊已通關的第 1 章，改選選項 1，驗證正式存檔 Flags 與好感度 100% 不受污染
            int affBeforeRecall = claire.Affection;
            double flagBeforeRecall = _core.Player.Story.Flags["route_char01_ch1_choice"];
            _core.Dirty = false;

            bool recallPlay = hubView.PlayCharacterRouteChapter("char01_claire", 1, playerView);
            Assert.IsTrue(recallPlay, "已通關章節應支援重播");
            Assert.IsTrue(playerView.IsRecollectionMode, "已通關章節重播應自動進入回憶模式 (IsRecollectionMode)");

            playerView.Advance();
            playerView.Advance();
            playerView.ChooseOption(1); // TeaseBranch (set route_char01_ch1_choice = 2, affection +15)
            playerView.Advance(); // End

            Assert.AreEqual(affBeforeRecall, claire.Affection, "回憶重播模式絕不可重複增加角色好感度");
            Assert.AreEqual(flagBeforeRecall, _core.Player.Story.Flags["route_char01_ch1_choice"], 1e-6, "回憶重播模式結束後，Story.Flags 必須維持原值不被覆寫");
            Assert.IsFalse(_core.Dirty, "回憶重播模式不應污染存檔 Dirty 狀態");
        }

        [Test]
        public void M3_2_1_TitleScreenView_Navigation_ToLobbyAndSettings_Succeeds()
        {
            var titleGo = Track(new GameObject("Test_TitleScreenUI"));
            var titleView = titleGo.AddComponent<TitleScreenView>();

            var lobbyGo = Track(new GameObject("Screen_Lobby"));
            var lobbyView = lobbyGo.AddComponent<LobbyView>();

            var settingsGo = Track(new GameObject("SettingsModalUI"));
            var settingsView = settingsGo.AddComponent<SettingsModalView>();

            titleView.Initialize(null, settingsView);
            _nav.Push(titleView);
            Assert.AreSame(titleView, _nav.CurrentScreen);

            // 1. 點擊「系統設定」開啟 SettingsModalView
            titleView.OnClickSettings();
            Assert.AreSame(settingsView, _nav.CurrentScreen, "點擊設定按鈕應開啟 SettingsModalView");
            _nav.Pop();
            Assert.AreSame(titleView, _nav.CurrentScreen);

            // 2. 點擊「開始作戰」導航進入 LobbyView
            titleView.OnClickStart();
            Assert.AreSame(lobbyView, _nav.CurrentScreen, "點擊開始作戰按鈕應導航進入 LobbyView");
        }

        [Test]
        public void M3_2_2_SaveLoadModalView_SavesAndLoadsSlot_RestoresStoryStateWithoutRegressingProfile()
        {
            string scriptContent = @"
[node Start]
line 1 Claire line_prologue
command give gold 500
jump ChoiceNode

[node ChoiceNode]
line 2 Claire line_choice
option 走向 A 結局 -> BranchA
option 走向 B 結局 -> BranchB

[node BranchA]
line 3 Claire line_branch_a
command give gold 2000
set choice_taken = 1
end

[node BranchB]
line 4 Claire line_branch_b
command give gold 5000
set choice_taken = 2
end
";
            var script = MiniScript.Parse(scriptContent);
            string saveDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_m3_2_slots_" + Guid.NewGuid().ToString("N"));
            var saveService = new StorySaveService(saveDir);

            var playerGo = Track(new GameObject("Test_StoryPlayerUI"));
            var playerView = playerGo.AddComponent<StoryPlayerView>();

            var modalGo = Track(new GameObject("Test_SaveLoadModalUI"));
            var modalView = modalGo.AddComponent<SaveLoadModalView>();
            playerView.SetSaveLoadModal(modalView);

            playerView.LoadAndPlay("test_m3_2", script, _core, false);

            // 推進至 ChoiceNode (選項前)
            playerView.Advance(); // S1 -> give gold 500
            playerView.Advance(); // ChoiceNode S2
            Assert.AreEqual("ChoiceNode", playerView.Presenter.Runner.CurrentNodeName);
            long goldBeforeChoice = _core.Player.ItemCount("gold");

            // 透過 SaveLoadModalView 存檔至 slot_002
            modalView.Open(SaveLoadModalView.ModalMode.Save, playerView, saveService, script);
            bool saveOk = modalView.SaveToSlot("slot_002", "選項前關鍵存檔");
            Assert.IsTrue(saveOk, "存檔至 slot_002 應成功");
            var slotData = modalView.GetSlotData("slot_002");
            Assert.IsNotNull(slotData);
            Assert.AreEqual("ChoiceNode", slotData.Node);
            Assert.AreEqual("選項前關鍵存檔", slotData.Memo);

            // 推進劇本走分支 A，增加金幣並設置變數
            playerView.Advance();
            Assert.IsTrue(playerView.IsOptionsVisible);
            playerView.ChooseOption(0); // BranchA
            playerView.Advance(); // S3 -> give gold 2000, set choice_taken = 1
            Assert.AreEqual(goldBeforeChoice + 2000, _core.Player.ItemCount("gold"), "分支 A 應獲得 2000 金幣");
            Assert.AreEqual(1, Convert.ToInt32(playerView.Presenter.Runner.Variables["choice_taken"]));

            // 從 SaveLoadModalView 讀取 slot_002 還原至 ChoiceNode
            modalView.Open(SaveLoadModalView.ModalMode.Load, playerView, saveService, script);
            bool loadOk = modalView.LoadFromSlot("slot_002");
            Assert.IsTrue(loadOk, "從 slot_002 讀檔應成功");

            // 驗證劇本還原回 ChoiceNode，但全域 Profile 金幣絕不倒退
            Assert.AreEqual("ChoiceNode", playerView.Presenter.Runner.CurrentNodeName, "劇本節點應精確還原回 ChoiceNode");
            Assert.AreEqual(goldBeforeChoice + 2000, _core.Player.ItemCount("gold"), "雙層存檔架構下，讀檔絕不可使玩家全域金幣倒退 (B5-1)");
        }

        [Test]
        public void M3_2_3_SaveLoadModalView_LoadingEmptySlot_FailsGracefully_PositiveControl()
        {
            string scriptContent = @"
[node Start]
line 1 Claire line_keep_progress
end
";
            var script = MiniScript.Parse(scriptContent);
            string saveDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_m3_2_empty_" + Guid.NewGuid().ToString("N"));
            var saveService = new StorySaveService(saveDir);

            var playerGo = Track(new GameObject("Test_StoryPlayerUI_Empty"));
            var playerView = playerGo.AddComponent<StoryPlayerView>();

            var modalGo = Track(new GameObject("Test_SaveLoadModalUI_Empty"));
            var modalView = modalGo.AddComponent<SaveLoadModalView>();

            playerView.LoadAndPlay("test_empty", script, _core, false);
            modalView.Open(SaveLoadModalView.ModalMode.Load, playerView, saveService, script);

            // 嘗試讀取尚未寫入的 slot_006
            bool loadEmpty = modalView.LoadFromSlot("slot_006");
            Assert.IsFalse(loadEmpty, "讀取空白槽位必須回傳 false (陽性對照)");
            Assert.AreEqual("slot_not_found", modalView.LastError, "讀取空白槽位錯誤碼應為 slot_not_found");
            Assert.AreEqual("Start", playerView.Presenter.Runner.CurrentNodeName, "讀取空槽位失敗時絕不可破壞當前劇本狀態");
        }

        [Test]
        public void M3_3_2_StoryRoute_Chapter3_R18AndFallback_RatingPolicyValidation()
        {
            string scriptPath = System.IO.Path.Combine(TestPaths.DataDir(), "story", "route_char01_ch3.txt");
            if (!System.IO.File.Exists(scriptPath))
            {
                scriptPath = System.IO.Path.Combine(Application.dataPath, "..", "..", "data", "story", "route_char01_ch3.txt");
            }
            Assert.IsTrue(System.IO.File.Exists(scriptPath), "route_char01_ch3.txt 檔案必須存在");
            string scriptText = System.IO.File.ReadAllText(scriptPath);
            var script = MiniScript.Parse(scriptText);

            // 1. 全年齡 G 政策測試：進入 AdultIntimacyNode 時應自動走 fallback 節點 FallbackGentleNode
            var coreG = new GameCore(_master, GameCore.NewPlayer(_master, new SystemClock()), new SplitMix64Random(1), new SystemClock(), new RatingPolicy(Rating.G, Rating.G));
            var playerGoG = Track(new GameObject("Test_PlayerG"));
            var playerViewG = playerGoG.AddComponent<StoryPlayerView>();
            playerViewG.LoadAndPlay("route_char01_ch3", script, coreG, false);

            Assert.AreEqual("Start", playerViewG.Presenter.Runner.CurrentNodeName);
            playerViewG.Advance(); // jump to EveningEncounter
            Assert.AreEqual("EveningEncounter", playerViewG.Presenter.Runner.CurrentNodeName);
            playerViewG.Advance(); // reaches options
            Assert.IsTrue(playerViewG.IsOptionsVisible);

            // 選擇第 0 個選項 (opt_embrace -> AdultIntimacyNode)
            playerViewG.ChooseOption(0);
            // G 政策下，ResolveNodeWithFallback 應自動轉移至 FallbackGentleNode
            Assert.AreEqual("FallbackGentleNode", playerViewG.Presenter.Runner.CurrentNodeName, "M3-3-2 查核點 (G政策)：應自動重導至 FallbackGentleNode");
            playerViewG.Advance(); // execute set route_char01_ch3_gentle = 1
            Assert.AreEqual(1, Convert.ToInt32(playerViewG.Presenter.Runner.Variables["route_char01_ch3_gentle"]));
            Assert.IsFalse(playerViewG.Presenter.Runner.Variables.ContainsKey("route_char01_ch3_intimate"), "G政策下絕不可執行 R18 節點指令");

            // 2. 陽性對照 (R18 政策測試)：允許進入 AdultIntimacyNode
            var coreR18 = new GameCore(_master, GameCore.NewPlayer(_master, new SystemClock()), new SplitMix64Random(1), new SystemClock(), new RatingPolicy(Rating.R18, Rating.R18));
            var playerGoR18 = Track(new GameObject("Test_PlayerR18"));
            var playerViewR18 = playerGoR18.AddComponent<StoryPlayerView>();
            playerViewR18.LoadAndPlay("route_char01_ch3", script, coreR18, false);

            Assert.AreEqual("Start", playerViewR18.Presenter.Runner.CurrentNodeName);
            playerViewR18.Advance(); // EveningEncounter
            playerViewR18.Advance(); // reaches options
            Assert.IsTrue(playerViewR18.IsOptionsVisible);

            playerViewR18.ChooseOption(0); // opt_embrace -> AdultIntimacyNode
            Assert.AreEqual("AdultIntimacyNode", playerViewR18.Presenter.Runner.CurrentNodeName, "M3-3-2 查核點 (陽性對照/R18政策)：應正常進入 AdultIntimacyNode");
            playerViewR18.Advance(); // execute set route_char01_ch3_intimate = 1
            Assert.AreEqual(1, Convert.ToInt32(playerViewR18.Presenter.Runner.Variables["route_char01_ch3_intimate"]));
        }

        [Test]
        public void M3_3_2_StoryHubView_PlayCharacterRouteChapter_AutoRoutesByRatingPolicy()
        {
            var hubGo = Track(new GameObject("Test_StoryHubView"));
            var hubView = hubGo.AddComponent<StoryHubView>();
            var playerGo = Track(new GameObject("Test_StoryPlayerView"));
            var playerView = playerGo.AddComponent<StoryPlayerView>();

            // 確保 char01 解鎖好感至第 3 章 (Lv3 門檻為 300 好感度)
            var cs = _core.Player.Char("char01_claire");
            cs.Affection = 500; // >= Lv3 (300)
            _core.Player.Story.CompletedChapters.Add(CharacterRouteService.GetChapterKey("char01_claire", 1));
            _core.Player.Story.CompletedChapters.Add(CharacterRouteService.GetChapterKey("char01_claire", 2));

            hubView.Initialize(_core);

            // G 政策下執行第 3 章路線
            bool started = hubView.PlayCharacterRouteChapter("char01_claire", 3, playerView);
            Assert.IsTrue(started, "PlayCharacterRouteChapter 應成功啟動");
            Assert.AreEqual("Start", playerView.Presenter.Runner.CurrentNodeName);
            playerView.Advance(); // EveningEncounter
            playerView.Advance(); // options
            Assert.IsTrue(playerView.IsOptionsVisible);
            playerView.ChooseOption(0);
            Assert.AreEqual("FallbackGentleNode", playerView.Presenter.Runner.CurrentNodeName, "在 G 政策下透過 StoryHubView 啟動應自動導向 Fallback 節點");
        }

        [Test]
        public void M3_6_1_StoryBranchGraphBuilder_BuildGraph_InitialStateAllUnvisited_IdentifiesEndingsAndRatings()
        {
            const string scriptText = @"
[node Start]
line l1 Claire hello
option ChoiceA -> NodeA
option ChoiceB -> NodeB

[node NodeA]
line l2 Claire path_a
command ending ending_a

[node NodeB rating:R18 fallback:NodeB_Gentle]
line l3 Claire path_b_spicy
command ending ending_b

[node NodeB_Gentle]
line l4 Claire path_b_gentle
command ending ending_b_gentle
";
            var graph = StoryBranchGraphBuilder.Build(scriptText);
            Assert.IsNotNull(graph);
            Assert.AreEqual(4, graph.TotalNodes, "應解析出 4 個節點 (Start, NodeA, NodeB, NodeB_Gentle)");
            Assert.AreEqual(0, graph.VisitedNodes, "未遊玩前已訪問節點數應為 0");
            Assert.AreEqual(0f, graph.CompletionRate, 0.001f);

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                Assert.IsFalse(graph.Nodes[i].IsVisited, $"未遊玩前節點 {graph.Nodes[i].NodeId} 的 IsVisited 必須為 false");
            }

            var nodeA = graph.FindNode("NodeA");
            Assert.IsNotNull(nodeA);
            Assert.IsTrue(nodeA.IsEnding, "NodeA 應標記為結局節點");
            Assert.AreEqual("ending_a", nodeA.EndingId);
            Assert.AreEqual(Rating.G, nodeA.Rating);

            var nodeB = graph.FindNode("NodeB");
            Assert.IsNotNull(nodeB);
            Assert.IsTrue(nodeB.IsEnding, "NodeB 應標記為結局節點");
            Assert.AreEqual("ending_b", nodeB.EndingId);
            Assert.AreEqual(Rating.R18, nodeB.Rating, "NodeB 應解析為 R18 評級");

            var edges = graph.GetOutgoingEdges("Start");
            Assert.AreEqual(2, edges.Count, "Start 節點應具有 2 條出邊");
            Assert.AreEqual("NodeA", edges[0].ToNode);
            Assert.AreEqual("ChoiceA", edges[0].OptionTextKey);
            Assert.IsFalse(edges[0].IsTaken);
            Assert.AreEqual("NodeB", edges[1].ToNode);
            Assert.AreEqual("ChoiceB", edges[1].OptionTextKey);
            Assert.IsFalse(edges[1].IsTaken);
        }

        [Test]
        public void M3_6_2_StoryPlayerView_OptionCheckmarksAndBranchGraphModal_PositiveControl()
        {
            const string scriptText = @"
[node Start]
line l1 Claire hello
option ChoiceA -> NodeA
option ChoiceB -> NodeB

[node NodeA]
line l2 Claire path_a
command ending ending_a

[node NodeB]
line l3 Claire path_b
command ending ending_b
";
            var script = MiniScript.Parse(scriptText);
            var playerGo = Track(new GameObject("Test_Player_M3_6"));
            var playerView = playerGo.AddComponent<StoryPlayerView>();

            _core.Player.Story.ReadNodes.Clear();

            // 1. 初次播放劇本，尚未探索任何節點
            playerView.LoadAndPlay("test_m3_6", script, _core, false);
            playerView.Advance(); // reaches options
            Assert.IsTrue(playerView.IsOptionsVisible);
            Assert.AreEqual(2, playerView.OptionCount);

            Text label0 = playerView.OptionButtons[0].GetComponentInChildren<Text>();
            Text label1 = playerView.OptionButtons[1].GetComponentInChildren<Text>();
            Assert.AreEqual("ChoiceA", label0.text, "未探索時選項 0 不得包含 ✓");
            Assert.AreEqual("ChoiceB", label1.text, "未探索時選項 1 不得包含 ✓");
            Assert.IsFalse(playerView.IsOptionRead(0));
            Assert.IsFalse(playerView.IsOptionRead(1));

            // 2. 選擇選項 A 進入 NodeA，並將 NodeA 登記至 ReadNodes
            playerView.ChooseOption(0);
            _core.Player.Story.ReadNodes.Add("NodeA");

            // 3. 陽性對照：重新載入或回溯至選項前，驗證只有選項 A 獲得 ✓ 打勾，選項 B 仍為未選
            var playerGo2 = Track(new GameObject("Test_Player_M3_6_Replay"));
            var playerView2 = playerGo2.AddComponent<StoryPlayerView>();
            playerView2.LoadAndPlay("test_m3_6", script, _core, false);
            playerView2.Advance(); // reaches options
            Assert.IsTrue(playerView2.IsOptionsVisible);

            Text reLabel0 = playerView2.OptionButtons[0].GetComponentInChildren<Text>();
            Text reLabel1 = playerView2.OptionButtons[1].GetComponentInChildren<Text>();
            Assert.AreEqual("✓ ChoiceA", reLabel0.text, "陽性對照：走過選項 A 後，選項 A 標籤必須加上 ✓");
            Assert.AreEqual("ChoiceB", reLabel1.text, "陽性對照：未走過之選項 B 標籤絕對不可帶 ✓");
            Assert.IsTrue(playerView2.IsOptionRead(0));
            Assert.IsFalse(playerView2.IsOptionRead(1));

            // 4. 驗證開啟分支拓樸圖彈窗
            Assert.IsFalse(playerView2.IsBranchGraphModalOpen);
            playerView2.OpenBranchGraphModal();
            Assert.IsTrue(playerView2.IsBranchGraphModalOpen, "點擊分支圖按鈕應開啟彈窗");
            Assert.IsNotNull(playerView2.CurrentBranchGraph);
            Assert.AreEqual(3, playerView2.CurrentBranchGraph.TotalNodes);
            Assert.Greater(playerView2.CurrentBranchGraph.VisitedNodes, 0);

            // 關閉分支圖彈窗
            playerView2.CloseBranchGraphModal();
            Assert.IsFalse(playerView2.IsBranchGraphModalOpen);
        }

        [Test]
        public void M3_6_3_StoryHubView_BranchGraphModal_OpensAndDisplaysGraph()
        {
            var hubGo = Track(new GameObject("Test_StoryHubView_M3_6"));
            var hubView = hubGo.AddComponent<StoryHubView>();
            hubView.Initialize(_core);

            Assert.IsFalse(hubView.IsBranchGraphModalOpen);

            const string testScript = @"
[node Start]
line l1 Claire prologue_intro
option opt1 -> Node1
[node Node1]
line l2 Claire prologue_end
command ending ed_prologue
";
            hubView.OpenBranchGraphModal(testScript);
            Assert.IsTrue(hubView.IsBranchGraphModalOpen, "StoryHubView 應成功開啟分支拓樸圖彈窗");
            Assert.IsNotNull(hubView.CurrentBranchGraph);
            Assert.AreEqual(2, hubView.CurrentBranchGraph.TotalNodes);

            hubView.CloseBranchGraphModal();
            Assert.IsFalse(hubView.IsBranchGraphModalOpen, "關閉分支圖彈窗後狀態應為 false");
        }
    }
}
#endif
