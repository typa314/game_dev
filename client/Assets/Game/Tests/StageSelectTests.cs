using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Persistence;
using Game.Core.Platform;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

#if UNITY_2021_1_OR_NEWER
using Game.Unity;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;
#endif

namespace Game.Tests
{
    [TestFixture]
    public sealed class StageSelectTests
    {
        private MasterDatabase _master;
        private GameCore _core;

        private class MockStageSelectView : IStageSelectView
        {
            public IReadOnlyList<StageSummaryItem> Stages { get; private set; }
            public string SelectedStageId { get; private set; }
            public StageSummaryItem DetailSummary { get; private set; }
            public StageDef DetailDef { get; private set; }
            public IReadOnlyList<string> CurrentTeam { get; private set; }
            public IReadOnlyList<string> OwnedCharacters { get; private set; }
            public bool IsVisible { get; set; } = true;
            public string LastError { get; private set; }

            public void DisplayStages(IReadOnlyList<StageSummaryItem> stages, string selectedStageId)
            {
                Stages = stages;
                SelectedStageId = selectedStageId;
            }

            public void DisplayStageDetail(StageSummaryItem summary, StageDef stageDef)
            {
                DetailSummary = summary;
                DetailDef = stageDef;
            }

            public int BaseLevel { get; private set; }
            public int BaseMaxHp { get; private set; }
            public int BaseNextHp { get; private set; }
            public int BaseUpgradeCost { get; private set; }
            public bool BaseCanAfford { get; private set; }

            public void DisplayTeam(IReadOnlyList<string> currentTeam, IReadOnlyList<string> ownedCharacters)
            {
                CurrentTeam = currentTeam;
                OwnedCharacters = ownedCharacters;
            }

            public void DisplayBaseStatus(int baseLevel, int maxLevel, int currentHp, int nextHp, int upgradeGoldCost, bool canAfford)
            {
                BaseLevel = baseLevel;
                BaseMaxHp = currentHp;
                BaseNextHp = nextHp;
                BaseUpgradeCost = upgradeGoldCost;
                BaseCanAfford = canAfford;
            }

            public void ShowError(string message)
            {
                LastError = message;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            PlayerState player = GameCore.NewPlayer(_master, clock);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            _core = new GameCore(_master, player, rng, clock, rating);
        }

        [Test]
        public void OPT3_1_StageSelectPresenter_InitialUnlocks_ReflectMasterAndBlockLockedSortie()
        {
            var view = new MockStageSelectView();
            var presenter = new StageSelectPresenter(_core);
            presenter.AttachView(view);
            presenter.Initialize();

            var stages = presenter.GetStages();
            Assert.IsNotNull(stages);
            Assert.GreaterOrEqual(stages.Count, 6, "應包含第 1 章與第 2 章共至少 6 個主線關卡");

            var st1 = stages.FirstOrDefault(s => s.StageId == "st_1_01");
            var st2 = stages.FirstOrDefault(s => s.StageId == "st_1_02");
            var st3 = stages.FirstOrDefault(s => s.StageId == "st_1_03");

            Assert.IsNotNull(st1);
            Assert.IsNotNull(st2);
            Assert.IsNotNull(st3);

            // 1. 驗證新玩家初始關卡解鎖狀態
            Assert.IsTrue(st1.IsUnlocked, "st_1_01 初始應為已解鎖");
            Assert.IsFalse(st2.IsUnlocked, "st_1_02 初始應為未解鎖");
            Assert.IsFalse(st3.IsUnlocked, "st_1_03 初始應為未解鎖");

            // 2. 已解鎖關卡允許出擊
            presenter.SelectStage("st_1_01");
            Assert.AreEqual("st_1_01", presenter.SelectedStageId);
            Assert.IsTrue(presenter.CanStartSelectedStage(), "已解鎖且隊伍有效之關卡應允許出擊");

            // 3. 未解鎖關卡出擊阻擋 (陽性對照)
            presenter.SelectStage("st_1_02");
            Assert.AreEqual("st_1_02", presenter.SelectedStageId);
            Assert.IsFalse(presenter.CanStartSelectedStage(), "未解鎖關卡 CanStartSelectedStage 必須回傳 false");
        }

        [Test]
        public void OPT3_2_StageSelectPresenter_ToggleTeamMember_AddsRemovesAndProtectsLastMember()
        {
            var view = new MockStageSelectView();
            var presenter = new StageSelectPresenter(_core);
            presenter.AttachView(view);
            presenter.Initialize();

            var initialTeam = presenter.GetTeam();
            Assert.IsNotNull(initialTeam);
            Assert.GreaterOrEqual(initialTeam.Count, 1, "初始隊伍應有成員");

            string memberA = initialTeam[0];

            // 若隊伍有多人，測試移除 1 人
            if (initialTeam.Count > 1)
            {
                int beforeCount = presenter.GetTeam().Count;
                var resRemove = presenter.ToggleTeamMember(memberA);
                Assert.IsTrue(resRemove.Ok, "有多人時移除 1 人應成功");
                Assert.AreEqual(beforeCount - 1, presenter.GetTeam().Count);
                Assert.IsFalse(presenter.GetTeam().Contains(memberA));

                // 再次點擊應重新加入
                var resAdd = presenter.ToggleTeamMember(memberA);
                Assert.IsTrue(resAdd.Ok, "點擊未上陣成員應重新加入");
                Assert.IsTrue(presenter.GetTeam().Contains(memberA));
            }

            // 將隊伍修剪為剛好剩下 1 人，驗證不可移除最後 1 人之保護
            while (presenter.GetTeam().Count > 1)
            {
                string toRemove = presenter.GetTeam()[presenter.GetTeam().Count - 1];
                presenter.ToggleTeamMember(toRemove);
            }

            Assert.AreEqual(1, presenter.GetTeam().Count, "隊伍現在剛好剩 1 人");
            string onlyMember = presenter.GetTeam()[0];

            // 嘗試移除最後一人：應失敗且原隊伍不變
            var failRes = presenter.ToggleTeamMember(onlyMember);
            Assert.IsFalse(failRes.Ok, "嘗試移除最後一位出戰隊員應被阻擋");
            Assert.AreEqual("invalid_team", failRes.Error);
            Assert.AreEqual(1, presenter.GetTeam().Count, "隊伍長度不應改變");
            Assert.AreEqual(onlyMember, presenter.GetTeam()[0], "原隊員應被保留");
        }

        [Test]
        public void OPT3_2_StageSelectPresenter_EnemyBaseTraits_MappedCorrectly()
        {
            var presenter = new StageSelectPresenter(_core);
            presenter.Initialize();
            var stages = presenter.GetStages();

            var st2 = stages.FirstOrDefault(s => s.StageId == "st_1_02");
            Assert.IsNotNull(st2);
            CollectionAssert.Contains(st2.EnemyBaseTraits, "shockwave_on_hp_loss", "st_1_02 應具備震波擊退特性");

            var st3 = stages.FirstOrDefault(s => s.StageId == "st_1_03");
            Assert.IsNotNull(st3);
            CollectionAssert.Contains(st3.EnemyBaseTraits, "immune_ranged", "st_1_03 應具備遠程免疫特性");

            var st203 = stages.FirstOrDefault(s => s.StageId == "st_2_03");
            Assert.IsNotNull(st203);
            CollectionAssert.Contains(st203.EnemyBaseTraits, "immune_area", "st_2_03 應具備範圍免疫特性");
        }

        [Test]
        public void M2_1_1_And_M2_1_2_ChapterExpansion_UnlocksChapter2_AndFiltersStagesByChapter()
        {
            var view = new MockStageSelectView();
            var presenter = new StageSelectPresenter(_core);
            presenter.AttachView(view);
            presenter.Initialize();

            // 1. 驗證章節清單包含 Chapter 1 與 Chapter 2
            var chapters = presenter.GetChapters();
            CollectionAssert.IsSubsetOf(new[] { 1, 2 }, chapters, "應包含第 1 章與第 2 章");

            // 2. 驗證 GetStagesByChapter(1) 與 GetStagesByChapter(2)
            var ch1Stages = presenter.GetStagesByChapter(1);
            var ch2Stages = presenter.GetStagesByChapter(2);
            Assert.AreEqual(3, ch1Stages.Count);
            Assert.AreEqual(3, ch2Stages.Count);
            Assert.IsTrue(ch2Stages.All(s => s.Chapter == 2));

            // 3. 初始未通關 st_1_01 時，st_1_01 的下一關尚未解鎖
            Assert.IsNull(presenter.GetNextUnlockedStageId("st_1_01"), "st_1_02 未解鎖時 GetNextUnlockedStageId 應回傳 null");

            // 4. 模擬依序通關 st_1_01 -> st_1_02 -> st_1_03，驗證自動解鎖 st_2_01
            _core.Player.Stages["st_1_02"] = new StageProgress { Unlocked = true, Stars = 3 };
            Assert.AreEqual("st_1_02", presenter.GetNextUnlockedStageId("st_1_01"), "st_1_02 解鎖後應回傳 st_1_02");

            _core.Player.Stages["st_1_03"] = new StageProgress { Unlocked = true, Stars = 0 };
            StageDef st103 = _master.Stages["st_1_03"];
            CollectionAssert.Contains(st103.UnlocksOnClear, "st_2_01", "st_1_03 通關解鎖清單應包含 st_2_01");

            // 驗證切換至第 2 章時 View 僅收到第 2 章關卡
            bool switched = presenter.SelectChapter(2);
            Assert.IsTrue(switched);
            Assert.AreEqual(2, presenter.CurrentChapter);
            Assert.AreEqual(3, view.Stages.Count);
            Assert.AreEqual("st_2_01", view.SelectedStageId);
        }

        [Test]
        public void M3_3_1_Chapter3Expansion_UnlocksChapter3_AndFiltersStagesByChapter()
        {
            var view = new MockStageSelectView();
            var presenter = new StageSelectPresenter(_core);
            presenter.AttachView(view);
            presenter.Initialize();

            // 1. 驗證章節清單回傳 [1, 2, 3] (M3-3-1 查核點)
            var chapters = presenter.GetChapters();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, chapters, "GetChapters 應回傳 [1, 2, 3]");

            // 2. 驗證 st_2_03 通關解鎖清單包含 st_3_01
            StageDef st203 = _master.Stages["st_2_03"];
            CollectionAssert.Contains(st203.UnlocksOnClear, "st_3_01", "st_2_03 通關解鎖清單應包含 st_3_01");

            // 3. 驗證第 3 章關卡列表
            var ch3Stages = presenter.GetStagesByChapter(3);
            Assert.AreEqual(3, ch3Stages.Count, "第 3 章應有 3 個關卡");
            CollectionAssert.AreEqual(new[] { "st_3_01", "st_3_02", "st_3_03" }, ch3Stages.Select(s => s.StageId).ToArray());

            // 4. 驗證切換至第 3 章 View 呈現與預設選取
            bool switched = presenter.SelectChapter(3);
            Assert.IsTrue(switched, "切換至第 3 章應成功");
            Assert.AreEqual(3, presenter.CurrentChapter);
            Assert.AreEqual(3, view.Stages.Count);
            Assert.AreEqual("st_3_01", view.SelectedStageId);

            // 5. 驗證第 3 章最終關 st_3_03 特性包含 shockwave_on_hp_loss 與 immune_ranged
            StageDef st303 = _master.Stages["st_3_03"];
            CollectionAssert.Contains(st303.EnemyBaseTraits, "shockwave_on_hp_loss");
            CollectionAssert.Contains(st303.EnemyBaseTraits, "immune_ranged");
        }

        [Test]
        public void BaseUpgrade_IncreasesBaseHp_DeductsGold_AndPersistsThroughSaveAndBattleReplay()
        {
            var view = new MockStageSelectView();
            var presenter = new StageSelectPresenter(_core);
            presenter.AttachView(view);
            presenter.Initialize();

            // 1. 初始狀態 (Lv.1, 20,000 HP, 升級花費 500 G)
            Assert.AreEqual(1, _core.GetAllyBaseLevel());
            Assert.AreEqual(20000, _core.GetAllyBaseMaxHp());
            Assert.AreEqual(500, _core.GetNextBaseUpgradeCost());
            Assert.AreEqual(1, view.BaseLevel);
            Assert.AreEqual(20000, view.BaseMaxHp);
            Assert.AreEqual(22500, view.BaseNextHp);

            long goldBefore = _core.Player.ItemCount("gold");
            var upRes = presenter.UpgradeBase();
            Assert.IsTrue(upRes.Ok, "首次強化應成功");
            Assert.AreEqual(2, upRes.Value);
            Assert.AreEqual(2, _core.GetAllyBaseLevel());
            Assert.AreEqual(22500, _core.GetAllyBaseMaxHp());
            Assert.AreEqual(goldBefore - 500, _core.Player.ItemCount("gold"), "應扣除 500 金幣");
            Assert.AreEqual(2, view.BaseLevel);
            Assert.AreEqual(22500, view.BaseMaxHp);
            Assert.AreEqual(1000, view.BaseUpgradeCost);

            // 2. 驗證戰鬥模擬器建置直接套用升級後主堡血量與等級
            var battleRes = _core.PrepareBattle("st_1_01", 42UL);
            Assert.IsTrue(battleRes.Ok);
            var sim = new Game.Domain.Battle.BattleSimulator(battleRes.Value);
            Assert.AreEqual(2, sim.AllyBaseLevel);
            Assert.AreEqual(22500, sim.AllyBaseMaxHp);
            Assert.AreEqual(22500, sim.AllyBaseHp);

            // 3. 驗證存檔編碼/解碼保存 BaseLevel
            string savedJson = Game.Core.Persistence.SaveCodec.Encode(_core.Player, "1.0.0", DateTime.UtcNow);
            var decodedPlayer = Game.Core.Persistence.SaveCodec.Decode(savedJson);
            Assert.AreEqual(2, decodedPlayer.BaseLevel, "存檔回讀後應保留主堡等級 Lv.2");

            // 4. 驗證金幣不足時拒絕強化
            _core.Player.AddItem("gold", -_core.Player.ItemCount("gold"));
            var failRes = presenter.UpgradeBase();
            Assert.IsFalse(failRes.Ok);
            Assert.AreEqual("insufficient_item", failRes.Error);

            // 5. 驗證滿級保護
            _core.Player.AddItem("gold", 9999999);
            while (_core.GetAllyBaseLevel() < _core.Growth.BaseMaxLevel)
            {
                Assert.IsTrue(_core.UpgradeBase().Ok);
            }
            var maxRes = _core.UpgradeBase();
            Assert.IsFalse(maxRes.Ok);
            Assert.AreEqual("base_max_level", maxRes.Error);
        }

#if UNITY_2021_1_OR_NEWER
        [Test]
        public void OPT3_3_StageSelectView_EnsureUIHierarchy_BuildsAllComponentsAndDisplaysThreatBadge()
        {
            var holder = new GameObject("Test_StageSelectUI");
            try
            {
                var view = holder.AddComponent<StageSelectView>();
                view.EnsureUIHierarchy();

                Canvas canvas = holder.GetComponent<Canvas>();
                Assert.IsNotNull(canvas, "應自動掛載 Canvas");
                Assert.AreEqual(15, canvas.sortingOrder);

                Assert.IsNotNull(holder.transform.Find("BackgroundPanel"), "應建立 BackgroundPanel");
                Assert.IsNotNull(holder.transform.Find("TopHeaderBar"), "應建立 TopHeaderBar");
                Assert.IsNotNull(holder.transform.Find("ChapterTabBar"), "應建立 ChapterTabBar");
                Assert.IsNotNull(holder.transform.Find("StageListPanel"), "應建立 StageListPanel");
                Assert.IsNotNull(holder.transform.Find("StageDetailPanel"), "應建立 StageDetailPanel");
                Assert.IsNotNull(holder.transform.Find("TeamRosterPanel"), "應建立 TeamRosterPanel");
                Assert.IsNotNull(holder.transform.Find("ToastBanner"), "應建立 ToastBanner");

                view.Initialize(_core);
                Assert.AreEqual(1, view.CurrentChapter, "預設應顯示第 1 章");
                Assert.AreEqual(3, view.DisplayedStageCount, "第 1 章應渲染 3 個關卡節點");

                // 驗證特異情報警示膠囊
                Transform alertBadge = holder.transform.Find("StageDetailPanel/ThreatAlertBadge");
                Assert.IsNotNull(alertBadge);

                // 切換至 st_1_02 驗證震波警示文字
                view.Presenter.SelectStage("st_1_02");
                Text badgeTxt = alertBadge.Find("Text")?.GetComponent<Text>();
                Assert.IsNotNull(badgeTxt);
                StringAssert.Contains("震波", badgeTxt.text);

                // 切換至 st_1_03 驗證遠程免疫警示文字
                view.Presenter.SelectStage("st_1_03");
                StringAssert.Contains("遠程", badgeTxt.text);

                // M2-1-2: 切換至第 2 章並選取 st_2_03 驗證範圍免疫警示文字
                Assert.IsTrue(view.SelectChapter(2), "切換至第 2 章應成功");
                Assert.AreEqual(2, view.CurrentChapter);
                Assert.AreEqual(3, view.DisplayedStageCount, "第 2 章應渲染 3 個關卡節點");
                view.Presenter.SelectStage("st_2_03");
                StringAssert.Contains("範圍", badgeTxt.text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void OPT3_3_LobbyView_OnClickBattle_NavigatesToStageSelectView()
        {
            var nav = new ScreenNavigator();
            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_opt3_saves_" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(tempDir);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            GameServices.Install(_core, store, new AssetService(rating), rating, new NullPlatformService(), null, navigator: nav);

            var lobbyGo = new GameObject("LobbyUI");
            var lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.EnsureUIHierarchy();
            lobbyView.Initialize(_core);

            var stageGo = new GameObject("StageSelectUI");
            var stageView = stageGo.AddComponent<StageSelectView>();
            stageView.EnsureUIHierarchy();
            stageView.Initialize(_core);

            try
            {
                nav.Push(lobbyView);
                Assert.AreEqual(1, nav.Count);
                Assert.AreSame(lobbyView, nav.CurrentScreen);

                // 從大廳開啟關卡星圖
                lobbyView.OpenStageSelect();
                Assert.AreEqual(2, nav.Count, "開啟關卡星圖後導航堆疊應為 2 層");
                Assert.AreSame(stageView, nav.CurrentScreen, "頂端畫面應為 StageSelectView");
                Assert.IsFalse(lobbyView.gameObject.activeSelf, "大廳應被隱藏");
                Assert.IsTrue(stageView.gameObject.activeSelf, "關卡星圖介面應顯示");

                // 按下返回鍵 (Esc)
                bool handled = nav.HandleBackRequest();
                Assert.IsTrue(handled);
                Assert.AreEqual(1, nav.Count, "返回後堆疊應回到 1 層");
                Assert.AreSame(lobbyView, nav.CurrentScreen, "頂端畫面回到 LobbyView");
                Assert.IsTrue(lobbyView.gameObject.activeSelf, "大廳應恢復顯示");
            }
            finally
            {
                GameServices.Clear();
                UnityEngine.Object.DestroyImmediate(lobbyGo);
                UnityEngine.Object.DestroyImmediate(stageGo);
                if (System.IO.Directory.Exists(tempDir))
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Test]
        public void StageSelectView_BaseUpgradeButton_UpgradesBaseHpAndUpdatesLabels()
        {
            var stageGo = new GameObject("Test_StageSelectBaseUI");
            var stageView = stageGo.AddComponent<StageSelectView>();
            try
            {
                stageView.EnsureUIHierarchy();
                stageView.Initialize(_core);

                Assert.IsNotNull(stageView.UpgradeBaseButton, "應建立強化主堡按鈕");
                Assert.IsNotNull(stageView.BaseLevelText, "應建立主堡等級標籤");
                Assert.IsNotNull(stageView.BaseHpText, "應建立主堡血量標籤");
                StringAssert.Contains("LV.1", stageView.BaseLevelText.text);
                StringAssert.Contains("20,000", stageView.BaseHpText.text);

                stageView.OnClickUpgradeBase();

                Assert.AreEqual(2, _core.GetAllyBaseLevel());
                StringAssert.Contains("LV.2", stageView.BaseLevelText.text);
                StringAssert.Contains("22,500", stageView.BaseHpText.text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stageGo);
            }
        }

        [Test]
        public void StageSelectView_DisplaysLocalizedNamesForAllCharacters_IncludingKaguraChloeMirai()
        {
            _core.Player.Characters["char07_kagura"] = new CharacterState { CurrentRarity = "R", Level = 1 };
            _core.Player.Characters["char09_chloe"] = new CharacterState { CurrentRarity = "R", Level = 1 };
            _core.Player.Characters["char10_mirai"] = new CharacterState { CurrentRarity = "R", Level = 1 };

            var stageGo = new GameObject("Test_StageSelectLocalizedNames");
            var stageView = stageGo.AddComponent<StageSelectView>();
            try
            {
                stageView.EnsureUIHierarchy();
                stageView.Initialize(_core);

                var cardTexts = stageGo.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
                CollectionAssert.Contains(cardTexts, "神樂", "char07_kagura 應正確顯示為神樂而非 char07");
                CollectionAssert.Contains(cardTexts, "克蘿伊", "char09_chloe 應正確顯示為克蘿伊而非 char09");
                CollectionAssert.Contains(cardTexts, "未來", "char10_mirai 應正確顯示為未來而非 char10");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stageGo);
            }
        }
#endif
    }
}
