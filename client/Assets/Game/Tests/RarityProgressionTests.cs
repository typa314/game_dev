using System.Collections.Generic;
using Game.Core;
using Game.Core.Gallery;
using Game.Domain;
using Game.Domain.Gacha;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class RarityProgressionTests
    {
        private MasterDatabase _master;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private GameCore CreateCore(PlayerState player = null)
        {
            var p = player ?? GameCore.NewPlayer(_master, new FakeClock(System.DateTime.UtcNow));
            return new GameCore(_master, p, new SplitMix64Random(42), new FakeClock(System.DateTime.UtcNow), new RatingPolicy(Rating.R18, Rating.R18));
        }

        // =========================================================================
        // 需求 1: 每張卡片都可以由 R 等級升到 UR
        // =========================================================================
        [Test]
        public void Req1_EveryCharacterCanBePromotedFromRToUR()
        {
            var core = CreateCore();
            const string charId = "char04_elysia"; // 初始 R 角色
            var cs = core.Player.Char(charId);
            Assert.IsNotNull(cs);
            cs.CurrentRarity = "R";
            cs.Tier = 0;

            // 給予足夠的同角色卡片 (R->SR 需 6 張, SR->SSR 需 6 張, SSR->UR 需 6 張，共 18 張)
            core.Player.AddItem("card_" + charId, 30);

            // 1. R -> R+5 -> SR
            for (int i = 0; i < 6; i++) core.AdvanceCharacter(charId);
            Assert.AreEqual("SR", cs.CurrentRarity, "R+5 進階後應晉升為 SR");
            Assert.AreEqual(0, cs.Tier);

            // 2. SR -> SR+5 -> SSR
            for (int i = 0; i < 6; i++) core.AdvanceCharacter(charId);
            Assert.AreEqual("SSR", cs.CurrentRarity, "SR+5 進階後應晉升為 SSR");
            Assert.AreEqual(0, cs.Tier);

            // 3. SSR -> SSR+5 -> UR
            for (int i = 0; i < 6; i++) core.AdvanceCharacter(charId);
            Assert.AreEqual("UR", cs.CurrentRarity, "SSR+5 進階後應晉升為最高稀有度 UR");
            Assert.AreEqual(0, cs.Tier);
        }

        // =========================================================================
        // 需求 2: 收集同角色 R 卡可進階為 R+1，+5 後升為下個等級
        // =========================================================================
        [Test]
        public void Req2_Advance_Plus1ThroughPlus5_AndPromoteToNextGrade()
        {
            var core = CreateCore();
            const string charId = "char04_elysia";
            var cs = core.Player.Char(charId);
            cs.CurrentRarity = "R";
            cs.Tier = 0;

            // 未持有卡片時失敗
            var failRes = core.AdvanceCharacter(charId);
            Assert.IsFalse(failRes.Ok);
            Assert.AreEqual("insufficient_item", failRes.Error);

            // 給予 1 張卡片 -> 升為 R+1
            core.Player.AddItem("card_" + charId, 1);
            var res1 = core.AdvanceCharacter(charId);
            Assert.IsTrue(res1.Ok);
            Assert.AreEqual("R", cs.CurrentRarity);
            Assert.AreEqual(1, cs.Tier);
            Assert.AreEqual("R+1", cs.DisplayGrade);
            Assert.IsFalse(res1.Value.IsRarityPromoted);

            // 陸續進階至 +2, +3, +4, +5
            core.Player.AddItem("card_" + charId, 4);
            for (int t = 2; t <= 5; t++)
            {
                var res = core.AdvanceCharacter(charId);
                Assert.IsTrue(res.Ok);
                Assert.AreEqual(t, cs.Tier);
                Assert.AreEqual($"R+{t}", cs.DisplayGrade);
            }

            // 當處於 R+5 時，再進階 1 次 -> 晉升為 SR (+0)
            core.Player.AddItem("card_" + charId, 1);
            var promoteRes = core.AdvanceCharacter(charId);
            Assert.IsTrue(promoteRes.Ok);
            Assert.IsTrue(promoteRes.Value.IsRarityPromoted);
            Assert.AreEqual("SR", cs.CurrentRarity);
            Assert.AreEqual(0, cs.Tier);
            Assert.AreEqual("SR", cs.DisplayGrade);
        }

        // =========================================================================
        // 需求 3: 抽卡時可以抽到 R, SR, SSR, UR 四種等級，依機率出現
        // =========================================================================
        [Test]
        public void Req3_GachaDraw_CanRoll_R_SR_SSR_UR()
        {
            var core = CreateCore();
            BannerDef bannerUr = _master.Banners["banner_ur"];
            Assert.IsNotNull(bannerUr, "banner_ur 必須存在於主資料庫中");

            // 檢查 rates 是否包含四種等級
            Assert.IsTrue(bannerUr.Rates.ContainsKey("UR"), "卡池應包含 UR 機率");
            Assert.IsTrue(bannerUr.Rates.ContainsKey("SSR"), "卡池應包含 SSR 機率");
            Assert.IsTrue(bannerUr.Rates.ContainsKey("SR"), "卡池應包含 SR 機率");
            Assert.IsTrue(bannerUr.Rates.ContainsKey("R"), "卡池應包含 R 機率");

            // 模擬抽到 UR: 直接將角色以 UR 稀有度抽入
            core.Player.AddItem("gem", 15000);
            int pity = 0;
            // 透過 MockRng 驗證可抽出 UR 卡
            var mockRng = new MockRandom(() => 0.005, max => 0); // 落在 UR 區間 (0.01)
            var draws = GachaEngine.Pull(bannerUr, 1, ref pity, mockRng);
            Assert.AreEqual("UR", draws[0].Rarity, "應抽到 UR 卡");

            // 透過 GameCore.PullGacha 驗證抽到 UR 後，角色直接晉升/獲得為 UR
            string targetChar = draws[0].Char;
            var playerChar = core.Player.Char(targetChar);
            if (playerChar != null) playerChar.CurrentRarity = "R";

            // 注入固定抽出 UR 的 Banner 進行抽卡
            var bannerTest = new BannerDef
            {
                Id = "banner_test_ur",
                Cost = bannerUr.Cost,
                Rates = new Dictionary<string, double> { { "UR", 1.0 } },
                Pool = new Dictionary<string, List<string>> { { "UR", new List<string> { targetChar } } },
                AllowCrossRarity = true
            };
            _master.Banners["banner_test_ur"] = bannerTest;

            var pullRes = core.PullGacha("banner_test_ur", 1);
            Assert.IsTrue(pullRes.Ok);
            Assert.AreEqual("UR", core.Player.Char(targetChar).CurrentRarity, "抽中 UR 後角色稀有度應為 UR");
        }

        // =========================================================================
        // 需求 4: 解鎖每個等級 & 進階可以解鎖對應的 CG 美術圖並收藏
        // =========================================================================
        [Test]
        public void Req4_Progression_Unlocks_CG_Art_In_Gallery()
        {
            var core = CreateCore();
            const string charId = "char01_claire";
            var cs = core.Player.Char(charId);
            if (cs == null)
            {
                // 若新玩家未初始持有 claire，抽一次或注入獲得
                core.Player.Characters[charId] = new CharacterState
                {
                    CurrentRarity = "R",
                    Tier = 0
                };
                cs = core.Player.Char(charId);
            }
            cs.CurrentRarity = "R";
            cs.Tier = 0;

            // 獲得 R 稀有度基礎 CG
            core.UnlockGradeCg(charId, "R", 0);
            Assert.IsTrue(core.Player.Gallery.Contains("cg_char01_claire_r"), "獲得 R 卡應解鎖 cg_char01_claire_r");

            // 給予卡片進行進階
            core.Player.AddItem("card_" + charId, 10);

            // 進階到 R+1
            var res1 = core.AdvanceCharacter(charId);
            Assert.IsTrue(res1.Ok);
            Assert.AreEqual("cg_char01_claire_r_plus_1", res1.Value.UnlockedCg);
            Assert.IsTrue(core.Player.Gallery.Contains("cg_char01_claire_r_plus_1"), "進階 R+1 應解鎖對應 CG");

            // 依序進階到 R+5
            for (int i = 2; i <= 5; i++)
            {
                var res = core.AdvanceCharacter(charId);
                Assert.IsTrue(core.Player.Gallery.Contains($"cg_char01_claire_r_plus_{i}"), $"進階 R+{i} 應解鎖對應 CG");
            }

            // R+5 升階到 SR
            var resSr = core.AdvanceCharacter(charId);
            Assert.IsTrue(resSr.Ok);
            Assert.IsTrue(resSr.Value.IsRarityPromoted);
            Assert.AreEqual("cg_char01_claire_sr", resSr.Value.UnlockedCg);
            Assert.IsTrue(core.Player.Gallery.Contains("cg_char01_claire_sr"), "升階至 SR 應解鎖 cg_char01_claire_sr");

            // 驗證在 GalleryService 圖鑑中依角色分組呈現解鎖狀態
            var galleryService = new GalleryService();
            var grouped = galleryService.GetGroupedGallery(core.Player, new RatingPolicy(Rating.G, Rating.G));
            Assert.IsTrue(grouped.ContainsKey(charId), "圖鑑中應包含該角色之分組");
            var claireItems = grouped[charId];
            Assert.IsTrue(claireItems.Exists(item => item.Def.Id == "cg_char01_claire_r_plus_1" && item.IsUnlocked));
            Assert.IsTrue(claireItems.Exists(item => item.Def.Id == "cg_char01_claire_sr" && item.IsUnlocked));
        }

        private sealed class MockRandom : IRandom
        {
            private readonly System.Func<double> _nextDouble;
            private readonly System.Func<int, int> _nextInt;

            public MockRandom(System.Func<double> nextDouble, System.Func<int, int> nextInt)
            {
                _nextDouble = nextDouble;
                _nextInt = nextInt;
            }

            public double NextDouble() => _nextDouble();
            public int NextInt(int maxExclusive) => _nextInt(maxExclusive);
        }
    }
}
