#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Persistence;
using Game.Core.Platform;
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
    public sealed class GachaViewTests
    {
        private GameObject _uiHolder;
        private GachaView _gachaView;
        private GameCore _core;
        private string _tempSaveDir;

        [SetUp]
        public void SetUp()
        {
            _uiHolder = new GameObject("Test_GachaUI");
            _gachaView = _uiHolder.AddComponent<GachaView>();

            // 建立測試用 GameCore
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            PlayerState player = GameCore.NewPlayer(master, clock);
            player.AddItem("gem", 10000); // 注入足額寶石以供測試
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            _core = new GameCore(master, player, rng, clock, rating);

            _tempSaveDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_gacha_saves_" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(_tempSaveDir);
            GameServices.Install(_core, store, new AssetService(rating), rating, new NullPlatformService(), null);

            _gachaView.Initialize(_core);
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.Clear();
            if (_uiHolder != null)
            {
                UnityEngine.Object.DestroyImmediate(_uiHolder);
            }
            if (!string.IsNullOrEmpty(_tempSaveDir) && System.IO.Directory.Exists(_tempSaveDir))
            {
                try { System.IO.Directory.Delete(_tempSaveDir, true); } catch { }
            }
        }

        [Test]
        public void GachaView_EnsureUIHierarchy_BuildsAllComponents()
        {
            _gachaView.EnsureUIHierarchy();

            Canvas canvas = _uiHolder.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, "應自動掛載 Canvas");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.AreEqual(20, canvas.sortingOrder, "抽卡介面層級應置於大廳之上");

            CanvasScaler scaler = _uiHolder.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "應自動掛載 CanvasScaler");
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);

            ResolutionAdapter adapter = _uiHolder.GetComponent<ResolutionAdapter>();
            Assert.IsNotNull(adapter, "應自動掛載 F10 解析度自適應組件");

            // 核心子節點驗證
            Assert.IsNotNull(_uiHolder.transform.Find("BackgroundPanel"), "應建立 BackgroundPanel");
            Assert.IsNotNull(_uiHolder.transform.Find("TopHeaderBar"), "應建立 TopHeaderBar");
            Assert.IsNotNull(_uiHolder.transform.Find("BannerTabsBar"), "應建立 BannerTabsBar");
            Assert.IsNotNull(_uiHolder.transform.Find("CenterBannerShowcase"), "應建立 CenterBannerShowcase");
            Assert.IsNotNull(_uiHolder.transform.Find("BottomRecruitPanel"), "應建立 BottomRecruitPanel");
            Assert.IsNotNull(_uiHolder.transform.Find("SummonAnimLayer"), "應建立 SummonAnimLayer");
            Assert.IsNotNull(_uiHolder.transform.Find("SingleRevealModal"), "應建立 SingleRevealModal");
            Assert.IsNotNull(_uiHolder.transform.Find("TenResultModal"), "應建立 TenResultModal");
            Assert.IsNotNull(_uiHolder.transform.Find("RateDetailsModal"), "應建立 RateDetailsModal");
            Assert.IsNotNull(_uiHolder.transform.Find("HistoryModal"), "應建立 HistoryModal");
            Assert.IsNotNull(_uiHolder.transform.Find("ToastBanner"), "應建立 ToastBanner");
        }

        [Test]
        public void GachaView_SwitchBanner_UpdatesBannerTitlePityAndRates()
        {
            _gachaView.EnsureUIHierarchy();

            // 切換至進階特選卡池 (banner_ur)
            _gachaView.SwitchBanner("banner_ur");

            Transform showcase = _uiHolder.transform.Find("CenterBannerShowcase");
            Assert.IsNotNull(showcase);
            Transform infoPlate = showcase.Find("FeaturedInfoPlate");
            Assert.IsNotNull(infoPlate);

            Text rarityText = infoPlate.Find("Rarity")?.GetComponent<Text>();
            Assert.IsNotNull(rarityText);
            StringAssert.Contains("UR", rarityText.text, "UR 卡池應標示 UR 特選");

            // 切換回常駐特異契約 (banner_std)
            _gachaView.SwitchBanner("banner_std");
            StringAssert.Contains("SSR", rarityText.text, "標準卡池應標示 SSR 特選");
        }

        [Test]
        public void GachaView_RateModal_TogglesCorrectly_AndDisplaysRates()
        {
            _gachaView.EnsureUIHierarchy();
            Transform rateModal = _uiHolder.transform.Find("RateDetailsModal");
            Assert.IsNotNull(rateModal);
            Assert.IsFalse(rateModal.gameObject.activeSelf);

            _gachaView.OpenRateModal();
            Assert.IsTrue(rateModal.gameObject.activeSelf, "開啟後應處於啟用狀態");

            Text content = rateModal.Find("Window/Content")?.GetComponent<Text>();
            Assert.IsNotNull(content);
            StringAssert.Contains("卡池機率公開規格", content.text);
            StringAssert.Contains("SSR", content.text);
            StringAssert.Contains("天井保底", content.text);

            Button closeBtn = rateModal.Find("Window/BtnClose")?.GetComponent<Button>();
            Assert.IsNotNull(closeBtn);
            closeBtn.onClick.Invoke();
            Assert.IsFalse(rateModal.gameObject.activeSelf, "點擊關閉按鈕後應隱藏");
        }

        [Test]
        public void GachaView_PullSingle_DisplaysSingleRevealModalWithCharacterData()
        {
            _gachaView.EnsureUIHierarchy();
            Transform singleModal = _uiHolder.transform.Find("SingleRevealModal");
            Assert.IsNotNull(singleModal);
            Assert.IsFalse(singleModal.gameObject.activeSelf);

            // 執行單抽
            _gachaView.OnClickPullSingle();

            // 單抽應立即開啟 SingleRevealModal
            Assert.IsTrue(singleModal.gameObject.activeSelf, "單抽完成後應開啟單卡展示彈窗");

            Text nameText = singleModal.Find("CharacterName")?.GetComponent<Text>();
            Assert.IsNotNull(nameText);
            Assert.IsFalse(string.IsNullOrEmpty(nameText.text));

            Text rarityBadge = singleModal.Find("RarityBadge")?.GetComponent<Text>();
            Assert.IsNotNull(rarityBadge);
            Assert.IsFalse(string.IsNullOrEmpty(rarityBadge.text));

            Text quoteText = singleModal.Find("QuoteBox/QuoteText")?.GetComponent<Text>();
            Assert.IsNotNull(quoteText);
            Assert.IsFalse(string.IsNullOrEmpty(quoteText.text), "應呈現角色專屬登場台詞");
        }

        [Test]
        public void GachaView_PullTen_PopulatesResultGridWithTenCards()
        {
            _gachaView.EnsureUIHierarchy();
            Transform tenModal = _uiHolder.transform.Find("TenResultModal");
            Assert.IsNotNull(tenModal);
            Assert.IsFalse(tenModal.gameObject.activeSelf);

            // 略過動畫以利 EditMode 測試即時結算
            Toggle skipToggle = _uiHolder.transform.Find("BottomRecruitPanel/ToggleSkipAnim")?.GetComponent<Toggle>();
            if (skipToggle != null) skipToggle.isOn = true;

            // 執行十連抽
            _gachaView.OnClickPullTen();

            Assert.IsTrue(tenModal.gameObject.activeSelf, "十連抽完成後應開啟十連結算彈窗");

            Transform gridRoot = tenModal.Find("GridRoot");
            Assert.IsNotNull(gridRoot);
            Assert.AreEqual(10, gridRoot.childCount, "結算網格中應動態生成剛好 10 張戰術卡牌");

            // 驗證第一張卡牌節點結構
            Transform firstCard = gridRoot.GetChild(0);
            Assert.IsNotNull(firstCard.Find("RarityBadge"));
            Assert.IsNotNull(firstCard.Find("Avatar"));
            Assert.IsNotNull(firstCard.Find("Name"));
            Assert.IsNotNull(firstCard.Find("Status"));
        }

        [Test]
        public void GachaView_HistoryModal_ShowsRecordedPulls()
        {
            _gachaView.EnsureUIHierarchy();

            // 略過動畫進行 1 次單抽以產生歷史紀錄
            Toggle skipToggle = _uiHolder.transform.Find("BottomRecruitPanel/ToggleSkipAnim")?.GetComponent<Toggle>();
            if (skipToggle != null) skipToggle.isOn = true;
            _gachaView.OnClickPullSingle();

            Transform historyModal = _uiHolder.transform.Find("HistoryModal");
            Assert.IsNotNull(historyModal);
            Assert.IsFalse(historyModal.gameObject.activeSelf);

            _gachaView.OpenHistoryModal();
            Assert.IsTrue(historyModal.gameObject.activeSelf);

            Text historyText = historyModal.Find("Window/Content")?.GetComponent<Text>();
            Assert.IsNotNull(historyText);
            StringAssert.Contains("最近", historyText.text);
            StringAssert.Contains("卡池: banner_std", historyText.text);
        }

        [Test]
        public void GachaView_InsufficientGems_ShowsToastWarning()
        {
            _gachaView.EnsureUIHierarchy();

            // 將寶石清零
            _core.Player.Items["gem"] = 0;
            _gachaView.RefreshHeaderCurrencies();

            _gachaView.OnClickPullSingle();

            Transform toast = _uiHolder.transform.Find("ToastBanner");
            Assert.IsNotNull(toast);
            Assert.IsTrue(toast.gameObject.activeSelf, "寶石不足時應觸發 Toast 浮動警示");

            Text toastText = toast.Find("ToastText")?.GetComponent<Text>();
            Assert.IsNotNull(toastText);
            StringAssert.Contains("寶石不足", toastText.text);
        }

        [Test]
        public void M2_6_GachaView_SwitchToSakuraPickupBanner_UpdatesFeaturedShowcaseDynamically()
        {
            _gachaView.EnsureUIHierarchy();

            // 1. 預設 banner_std 主打為 char01_claire
            _gachaView.SwitchBanner("banner_std");
            Assert.AreEqual("char01_claire", _gachaView.FeaturedCharacterId);
            StringAssert.Contains("CLAIRE", _gachaView.FeaturedCharacterName);

            // 2. 切換至春櫻祈願限定卡池 banner_sakura_up，驗證中央看板動態切換為 char18_sakura_miko (櫻)
            _gachaView.SwitchBanner("banner_sakura_up");
            Assert.AreEqual("banner_sakura_up", _gachaView.CurrentBannerId);
            Assert.AreEqual("char18_sakura_miko", _gachaView.FeaturedCharacterId);
            StringAssert.Contains("SAKURA", _gachaView.FeaturedCharacterName);
            StringAssert.Contains("千本櫻神樂", _gachaView.FeaturedSkillDescription);
        }
    }
}
#endif
