using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.Gacha;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class GachaPresenterTests
    {
        private MasterDatabase _master;
        private PlayerState _player;
        private GameCore _core;
        private GachaPresenter _presenter;
        private TestGachaView _view;

        private sealed class TestGachaView : IGachaView
        {
            public bool IsVisible { get; set; } = true;
            public IReadOnlyList<GachaResultEntry> LastResults { get; private set; }
            public string LastError { get; private set; }

            public void ShowGachaResult(IReadOnlyList<GachaResultEntry> results)
            {
                LastResults = results;
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
            _player = GameCore.NewPlayer(_master, clock);
            _player.AddItem("gem", 5000); // 注入額外測試寶石
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            _core = new GameCore(_master, _player, rng, clock, rating);

            _presenter = new GachaPresenter(_core);
            _view = new TestGachaView();
            _presenter.AttachView(_view);
        }

        [Test]
        public void GachaPresenter_GetAvailableBanners_ReturnsConfiguredBanners()
        {
            var banners = _presenter.GetAvailableBanners();
            Assert.IsNotNull(banners);
            Assert.IsTrue(banners.Count >= 2, "應包含至少 2 個有效卡池 (banner_std, banner_ur)");

            var bannerStd = _presenter.GetBanner("banner_std");
            Assert.IsNotNull(bannerStd);
            Assert.AreEqual("standard", bannerStd.Type);
            Assert.AreEqual(100, bannerStd.Pity.HardAt);
        }

        [Test]
        public void GachaPresenter_CurrencyAndPityQueries_ReflectState()
        {
            long initialGems = _presenter.GetGemCount();
            Assert.IsTrue(initialGems >= 5000);
            Assert.AreEqual(0, _presenter.GetPityCount("banner_std"));
            Assert.AreEqual(100, _presenter.GetPityLimit("banner_std"));

            // 執行 1 次單抽 (消耗 150 寶石)
            var res = _presenter.Pull("banner_std", 1);
            Assert.IsTrue(res.Ok);
            Assert.AreEqual(1, res.Value.Count);
            Assert.AreEqual(initialGems - 150, _presenter.GetGemCount());
            Assert.IsNotNull(_view.LastResults);
            Assert.AreEqual(1, _view.LastResults.Count);

            // 若未抽中 SSR，pity 應為 1；若抽中 SSR，pity 重置為 0
            int pity = _presenter.GetPityCount("banner_std");
            if (res.Value[0].Draw.Rarity == "SSR")
            {
                Assert.AreEqual(0, pity);
            }
            else
            {
                Assert.AreEqual(1, pity);
            }
        }

        [Test]
        public void GachaPresenter_TenPull_Deducts1500Gems_AndRecordsHistory()
        {
            long initialGems = _presenter.GetGemCount();
            var res = _presenter.Pull("banner_std", 10);
            Assert.IsTrue(res.Ok);
            Assert.AreEqual(10, res.Value.Count);
            Assert.AreEqual(initialGems - 1500, _presenter.GetGemCount());

            var history = _presenter.GetHistory(20);
            Assert.IsNotNull(history);
            Assert.AreEqual(10, history.Count);
            Assert.AreEqual("banner_std", history[0].Banner);
            Assert.IsFalse(string.IsNullOrEmpty(history[0].Char));
        }

        [Test]
        public void GachaPresenter_InsufficientGems_ReturnsError_PositiveControl()
        {
            // 將玩家寶石清零
            _player.Items["gem"] = 0;
            Assert.AreEqual(0, _presenter.GetGemCount());

            var res = _presenter.Pull("banner_std", 1);
            Assert.IsFalse(res.Ok);
            Assert.AreEqual("insufficient_item", res.Error);
            Assert.AreEqual("insufficient_item", _view.LastError);
        }

        [Test]
        public void GachaPresenter_InvalidBanner_ReturnsError()
        {
            var res = _presenter.Pull("banner_non_existent_999", 1);
            Assert.IsFalse(res.Ok);
            Assert.AreEqual("banner_not_found", res.Error);
            Assert.AreEqual("banner_not_found", _view.LastError);
        }

        [Test]
        public void M2_6_GachaPresenter_GetFeaturedCharacter_ReturnsPickupCharacterForSakuraBanner()
        {
            var banners = _presenter.GetAvailableBanners();
            Assert.IsTrue(banners.Count >= 3, "應包含至少 3 個有效卡池 (banner_std, banner_ur, banner_sakura_up)");

            var sakuraBanner = _presenter.GetBanner("banner_sakura_up");
            Assert.IsNotNull(sakuraBanner, "應載入 banner_sakura_up 限定 Pickup 卡池");
            Assert.AreEqual("pickup", sakuraBanner.Type);
            Assert.IsNotNull(sakuraBanner.Pickup);
            Assert.AreEqual(0.7, sakuraBanner.Pickup.ShareOfRarity, 1e-6);

            var featuredSakura = _presenter.GetFeaturedCharacter("banner_sakura_up");
            Assert.IsNotNull(featuredSakura);
            Assert.AreEqual("char18_sakura_miko", featuredSakura.Id, "banner_sakura_up 主打角色應為 char18_sakura_miko");

            var featuredStd = _presenter.GetFeaturedCharacter("banner_std");
            Assert.IsNotNull(featuredStd);
            Assert.AreEqual("char01_claire", featuredStd.Id, "banner_std 預設展示角色應為 char01_claire");
        }
    }
}
