using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Domain;
using Game.Domain.Gacha;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A5：轉蛋抽卡引擎（GachaEngine.cs）單元測試
    /// 涵蓋查核點 A5-1 ～ A5-9
    /// </summary>
    [TestFixture]
    public class GachaEngineTests
    {
        private class MockRandom : IRandom
        {
            private readonly Func<double> _nextDouble;
            private readonly Func<int, int> _nextInt;

            public MockRandom(Func<double> nextDouble, Func<int, int> nextInt = null)
            {
                _nextDouble = nextDouble;
                _nextInt = nextInt ?? (max => 0);
            }

            public double NextDouble() { return _nextDouble(); }
            public int NextInt(int maxExclusive) { return _nextInt(maxExclusive); }
        }

        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static BannerDef CloneBanner(BannerDef src)
        {
            return new BannerDef
            {
                Id = src.Id,
                Type = src.Type,
                Name = src.Name,
                Cost = src.Cost,
                Rates = new Dictionary<string, double>(src.Rates),
                Pool = src.Pool.ToDictionary(k => k.Key, v => new List<string>(v.Value)),
                Pickup = src.Pickup != null ? new BannerPickup
                {
                    Chars = new List<string>(src.Pickup.Chars),
                    ShareOfRarity = src.Pickup.ShareOfRarity
                } : null,
                Pity = src.Pity != null ? new BannerPity
                {
                    HardAt = src.Pity.HardAt,
                    Rarity = src.Pity.Rarity
                } : null,
                TenPullGuarantee = src.TenPullGuarantee
            };
        }

        // =============================================================
        // A5-1：統計測試
        // 用固定 seed 抽 1,000,000 次，SSR、SR、R 的比率都落在理論值的 99.9% 信賴區間內 (z = 3.29)
        // 理論值：SSR = 0.03, SR = 0.12, R = 0.85
        // =============================================================

        [Test]
        public void A5_1_StatisticalDistribution_FallsWithinConfidenceInterval()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);
            // 排除保底與天井對自然機率的干擾
            banner.Pity = null;
            banner.TenPullGuarantee = null;

            IRandom rng = new SplitMix64Random(20261014UL);
            int total = 1000000;
            int countSSR = 0, countSR = 0, countR = 0;
            int dummyPity = 0;

            for (int i = 0; i < total; i++)
            {
                List<GachaDraw> draws = GachaEngine.Pull(banner, 1, ref dummyPity, rng);
                string r = draws[0].Rarity;
                if (r == "SSR") countSSR++;
                else if (r == "SR") countSR++;
                else if (r == "R") countR++;
            }

            double pSSR = (double)countSSR / total;
            double pSR = (double)countSR / total;
            double pR = (double)countR / total;

            double z = 3.29; // 99.9% 信賴區間
            double sigmaSSR = Math.Sqrt(0.03 * 0.97 / total);
            double sigmaSR = Math.Sqrt(0.12 * 0.88 / total);
            double sigmaR = Math.Sqrt(0.85 * 0.15 / total);

            Assert.IsTrue(Math.Abs(pSSR - 0.03) <= z * sigmaSSR,
                string.Format("SSR 機率 {0:F5} 超出 99.9% 信賴區間 [0.03 ± {1:F5}]", pSSR, z * sigmaSSR));
            Assert.IsTrue(Math.Abs(pSR - 0.12) <= z * sigmaSR,
                string.Format("SR 機率 {0:F5} 超出 99.9% 信賴區間 [0.12 ± {1:F5}]", pSR, z * sigmaSR));
            Assert.IsTrue(Math.Abs(pR - 0.85) <= z * sigmaR,
                string.Format("R 機率 {0:F5} 超出 99.9% 信賴區間 [0.85 ± {1:F5}]", pR, z * sigmaR));
        }

        // =============================================================
        // A5-2：天井機制
        // pity 從 0 開始，第 100 抽一定是 SSR，標記 ByPity；出現 SSR 後 pity 歸零
        // =============================================================

        [Test]
        public void A5_2_HardPity_GuaranteesSSRAt100AndResetsPity()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);
            banner.TenPullGuarantee = null; // 排除十連干擾

            // Mock RNG：每次 NextDouble 都回傳 0.99（常態下必定抽到 R）
            IRandom mockRng = new MockRandom(() => 0.99, max => 0);

            int pity = 0;
            // 前 99 抽均為 R，ByPity = false
            for (int i = 1; i <= 99; i++)
            {
                List<GachaDraw> draws = GachaEngine.Pull(banner, 1, ref pity, mockRng);
                Assert.AreEqual("R", draws[0].Rarity);
                Assert.IsFalse(draws[0].ByPity);
                Assert.AreEqual(i, pity, "第 " + i + " 抽 pity 累計值應為 " + i);
            }

            // 第 100 抽觸發天井
            List<GachaDraw> pityDraw = GachaEngine.Pull(banner, 1, ref pity, mockRng);
            Assert.AreEqual("SSR", pityDraw[0].Rarity, "第 100 抽必須觸發 SSR 天井");
            Assert.IsTrue(pityDraw[0].ByPity, "第 100 抽應標記 ByPity = true");
            Assert.AreEqual(0, pity, "天井觸發後 pity 應歸零");
        }

        // =============================================================
        // A5-3：自然抽到 SSR 時，pity 也會歸零
        // =============================================================

        [Test]
        public void A5_3_NaturalSSR_ResetsPityToZero()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);

            // Mock RNG：NextDouble 回傳 0.01（小於 0.03，自然抽到 SSR）
            IRandom mockRng = new MockRandom(() => 0.01, max => 0);

            int pity = 50; // 已累計 50 抽
            List<GachaDraw> draws = GachaEngine.Pull(banner, 1, ref pity, mockRng);

            Assert.AreEqual("SSR", draws[0].Rarity);
            Assert.IsFalse(draws[0].ByPity, "自然抽到不應標記 ByPity");
            Assert.AreEqual(0, pity, "自然抽中 SSR 後 pity 應立即歸零");
        }

        // =============================================================
        // A5-4：十連保底
        // 前 9 抽都是 R 時，第 10 抽提升為 SR，標記 ByTenGuarantee；前 9 抽已出 SR 時不觸發保底
        // =============================================================

        [Test]
        public void A5_4_TenPullGuarantee_UpgradesTenthDrawWhenNoPriorGuaranteeRarity()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);

            // 情境 A：前 9 抽與第 10 抽原本都是 R (0.99)
            IRandom allRRng = new MockRandom(() => 0.99, max => 0);
            int pityA = 0;
            List<GachaDraw> drawsA = GachaEngine.Pull(banner, 10, ref pityA, allRRng);

            for (int i = 0; i < 9; i++)
            {
                Assert.AreEqual("R", drawsA[i].Rarity);
                Assert.IsFalse(drawsA[i].ByTenGuarantee);
            }
            Assert.AreEqual("SR", drawsA[9].Rarity, "第 10 抽應被提升為 SR 保底");
            Assert.IsTrue(drawsA[9].ByTenGuarantee, "第 10 抽應標記 ByTenGuarantee = true");

            // 情境 B：第 1 抽自然出 SR (0.05)，後續為 R (0.99)
            int callCount = 0;
            IRandom firstSRRng = new MockRandom(() =>
            {
                callCount++;
                return callCount == 1 ? 0.05 : 0.99;
            }, max => 0);

            int pityB = 0;
            List<GachaDraw> drawsB = GachaEngine.Pull(banner, 10, ref pityB, firstSRRng);

            Assert.AreEqual("SR", drawsB[0].Rarity);
            Assert.IsFalse(drawsB[0].ByTenGuarantee);
            Assert.AreEqual("R", drawsB[9].Rarity, "前 9 抽已有 SR 時，第 10 抽不應被提升保底");
            Assert.IsFalse(drawsB[9].ByTenGuarantee);
        }

        // =============================================================
        // A5-5：單抽不觸發十連保底
        // 連續 10 次單抽，即使全部都是 R，第 10 次單抽亦不觸發十連保底
        // =============================================================

        [Test]
        public void A5_5_SinglePull_DoesNotTriggerTenPullGuarantee()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);

            IRandom allRRng = new MockRandom(() => 0.99, max => 0);
            int pity = 0;

            for (int i = 1; i <= 10; i++)
            {
                List<GachaDraw> single = GachaEngine.Pull(banner, 1, ref pity, allRRng);
                Assert.AreEqual("R", single[0].Rarity, "單抽不應提升為 SR");
                Assert.IsFalse(single[0].ByTenGuarantee, "單抽不應標記 ByTenGuarantee");
            }
        }

        // =============================================================
        // A5-6：Pickup 機制
        // ShareOfRarity = 0.5 時，抽 10 萬次，pickup 角色比例在 0.5 ± 3.29σ 內
        // 池中只有 pickup 角色時必定抽到 pickup
        // =============================================================

        [Test]
        public void A5_6_Pickup_MatchesShareOfRarityAndGuaranteesWhenSole()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);
            banner.Rates["SSR"] = 1.0;
            banner.Rates["SR"] = 0.0;
            banner.Rates["R"] = 0.0;
            banner.Pool["SSR"] = new List<string> { "char01_claire", "char_other_ssr" };
            banner.Pickup = new BannerPickup
            {
                Chars = new List<string> { "char01_claire" },
                ShareOfRarity = 0.5
            };
            banner.Pity = null;
            banner.TenPullGuarantee = null;

            IRandom rng = new SplitMix64Random(20261014UL);
            int total = 100000;
            int pickupCount = 0;
            int dummyPity = 0;

            for (int i = 0; i < total; i++)
            {
                List<GachaDraw> draws = GachaEngine.Pull(banner, 1, ref dummyPity, rng);
                if (draws[0].IsPickup) pickupCount++;
            }

            double p = (double)pickupCount / total;
            double z = 3.29;
            double sigma = Math.Sqrt(0.5 * 0.5 / total);
            Assert.IsTrue(Math.Abs(p - 0.5) <= z * sigma,
                string.Format("Pickup 比例 {0:F5} 超出 0.5 ± {1:F5}", p, z * sigma));

            // 當池中只有 pickup 角色時
            banner.Pool["SSR"] = new List<string> { "char01_claire" };
            for (int i = 0; i < 100; i++)
            {
                List<GachaDraw> draws = GachaEngine.Pull(banner, 1, ref dummyPity, rng);
                Assert.AreEqual("char01_claire", draws[0].Char);
                Assert.IsTrue(draws[0].IsPickup);
            }
        }

        // =============================================================
        // A5-7：DisplayRates 機率表計算
        // 角色機率加總為 1 (誤差 < 1e-9)，且各角色機率與手算一致
        // =============================================================

        [Test]
        public void A5_7_DisplayRates_SumsToOneAndMatchesManualCalculation()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);
            // banner_std: SSR 0.03 (1 角), SR 0.12 (2 角), R 0.85 (2 角)

            Dictionary<string, double> rates = GachaEngine.DisplayRates(banner);
            double sum = rates.Values.Sum();
            Assert.IsTrue(Math.Abs(sum - 1.0) < 1e-9, "DisplayRates 加總應為 1.0");

            Assert.AreEqual(0.03, rates["char01_claire"], 1e-9, "Claire SSR 機率應為 0.03");
            Assert.AreEqual(0.06, rates["char02_luna"], 1e-9, "Luna SR 機率應為 0.06 (0.12 / 2)");
            Assert.AreEqual(0.06, rates["char03_scarlet"], 1e-9, "Scarlet SR 機率應為 0.06 (0.12 / 2)");
            Assert.AreEqual(0.425, rates["char04_elysia"], 1e-9, "Elysia R 機率應為 0.425 (0.85 / 2)");
            Assert.AreEqual(0.425, rates["char05_rin"], 1e-9, "Rin R 機率應為 0.425 (0.85 / 2)");
        }

        // =============================================================
        // A5-8：浮點誤差缺口處理
        // 機率總和略小於 1 時，落在缺口的亂數歸給最低稀有度，不會丟出例外
        // =============================================================

        [Test]
        public void A5_8_FloatingPointGap_FallsBackToLowestRarityWithoutThrowing()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);
            // 故意設為加總略小於 1
            banner.Rates["SSR"] = 0.03;
            banner.Rates["SR"] = 0.12;
            banner.Rates["R"] = 0.849999; // 累加僅達 0.999999
            banner.Pity = null;
            banner.TenPullGuarantee = null;

            // 讓 RNG 回傳 0.9999999 (落在 [0.999999, 1.0) 缺口)
            IRandom mockRng = new MockRandom(() => 0.9999999, max => 0);
            int pity = 0;

            Assert.DoesNotThrow(() =>
            {
                List<GachaDraw> draws = GachaEngine.Pull(banner, 1, ref pity, mockRng);
                Assert.AreEqual("R", draws[0].Rarity, "缺口應回退指派給最低稀有度 R");
            });
        }

        // =============================================================
        // A5-9：池子為空時拋出 InvalidOperationException
        // =============================================================

        [Test]
        public void A5_9_EmptyPool_ThrowsInvalidOperationException()
        {
            MasterDatabase m = LoadCleanMaster();
            BannerDef banner = CloneBanner(m.Banners["banner_std"]);
            banner.Pool["R"] = new List<string>(); // R 池清空

            int pity = 7;
            int rngCalls = 0;
            IRandom rng = new MockRandom(() => { rngCalls++; return 0.99; }, max => { rngCalls++; return 0; });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                GachaEngine.Pull(banner, 1, ref pity, rng);
            });
            Assert.IsTrue(ex.Message.Contains("empty pool"), "例外訊息應包含 'empty pool'");
            Assert.AreEqual(7, pity, "失敗的抽卡不得變更 pity 狀態");
            Assert.AreEqual(0, rngCalls, "空池應在消耗亂數前被拒絕");

            // 沒有任何正機率稀有度時，也要在修改 pity 或消耗亂數前拒絕。
            banner.Rates.Clear();
            pity = 7;
            rngCalls = 0;
            Exception noRates = Assert.Catch(() => GachaEngine.Pull(banner, 1, ref pity, rng));
            Assert.Multiple(() =>
            {
                Assert.IsInstanceOf<InvalidOperationException>(noRates);
                Assert.AreEqual(7, pity, "沒有有效機率時不得變更 pity 狀態");
                Assert.AreEqual(0, rngCalls, "沒有有效機率時不得消耗亂數");
            });
        }
    }
}
