using System;
using Game.Domain;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A1：共用基礎（Common.cs）單元測試
    /// 涵蓋查核點 A1-1, A1-2, A1-3, A1-4, A1-8
    /// </summary>
    [TestFixture]
    public class CommonTests
    {
        // -------------------------------------------------------------
        // A1-1: SplitMix64Random 同一個 seed 產生的 1,000 個 NextDouble() 完全相同；換成不同 seed 時，至少有 1 個值不同。
        // -------------------------------------------------------------
        [Test]
        public void A1_1_SplitMix64Random_SameSeed_ProducesIdenticalSequence()
        {
            ulong seed = 987654321UL;
            SplitMix64Random rng1 = new SplitMix64Random(seed);
            SplitMix64Random rng2 = new SplitMix64Random(seed);

            for (int i = 0; i < 1000; i++)
            {
                double v1 = rng1.NextDouble();
                double v2 = rng2.NextDouble();
                Assert.AreEqual(v1, v2, "第 " + i + " 次抽樣的值在同一個 seed 下必須完全相同。");
            }
        }

        [Test]
        public void A1_1_SplitMix64Random_DifferentSeed_ProducesDifferentSequence()
        {
            ulong seed1 = 12345UL;
            ulong seed2 = 54321UL;
            SplitMix64Random rng1 = new SplitMix64Random(seed1);
            SplitMix64Random rng2 = new SplitMix64Random(seed2);

            int differentCount = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (rng1.NextDouble() != rng2.NextDouble())
                {
                    differentCount++;
                }
            }

            Assert.IsTrue(differentCount > 0, "不同 seed 產生的 1,000 個值中至少要有 1 個值不同。");
        }

        // -------------------------------------------------------------
        // A1-2: NextDouble() 的值都落在 [0,1)（抽 100,000 次）；NextInt(n) 的值都落在 [0,n)（n = 1、7、1000）；NextInt(0) 和 NextInt(-1) 丟出 ArgumentOutOfRangeException。
        // -------------------------------------------------------------
        [Test]
        public void A1_2_NextDouble_ValuesInRangeZeroToOneExclusive()
        {
            SplitMix64Random rng = new SplitMix64Random(123456UL);
            for (int i = 0; i < 100000; i++)
            {
                double val = rng.NextDouble();
                if (val < 0.0 || val >= 1.0)
                {
                    Assert.Fail("NextDouble() 產生的數值超出 [0, 1) 範圍: " + val + " (第 " + i + " 次抽樣)");
                }
            }
        }

        [Test]
        public void A1_2_NextInt_ValuesInRangeZeroToNExclusive()
        {
            int[] testCases = new int[] { 1, 7, 1000 };
            SplitMix64Random rng = new SplitMix64Random(78910UL);

            foreach (int n in testCases)
            {
                for (int i = 0; i < 10000; i++)
                {
                    int val = rng.NextInt(n);
                    if (val < 0 || val >= n)
                    {
                        Assert.Fail("NextInt(" + n + ") 產生的數值超出 [0, " + n + ") 範圍: " + val);
                    }
                }
            }
        }

        [Test]
        public void A1_2_NextInt_NonPositiveMaxExclusive_ThrowsArgumentOutOfRangeException()
        {
            SplitMix64Random rng = new SplitMix64Random(42UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-1));
        }

        // -------------------------------------------------------------
        // A1-3: Rarity.Rank 的回傳值：N=0、R=1、SR=2、SSR=3、UR=4；未定義稀有度丟出 ArgumentException。
        // -------------------------------------------------------------
        [Test]
        public void A1_3_Rarity_Rank_ReturnsExpectedValues()
        {
            Assert.AreEqual(0, Rarity.Rank("N"));
            Assert.AreEqual(1, Rarity.Rank("R"));
            Assert.AreEqual(2, Rarity.Rank("SR"));
            Assert.AreEqual(3, Rarity.Rank("SSR"));
            Assert.AreEqual(4, Rarity.Rank("UR"));
        }

        [Test]
        public void A1_3_Rarity_Rank_UnknownRarity_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => Rarity.Rank("UNKNOWN_RARITY"));
        }

        // -------------------------------------------------------------
        // A1-4: RatingPolicy：new RatingPolicy(Rating.R18, Rating.G).Effective == Rating.G；(G, R18) 是 G；(R18, R18) 是 R18；(R15, R18).Allows(Rating.R18) == false；Allows(Rating.R15) == true。
        // -------------------------------------------------------------
        [Test]
        public void A1_4_RatingPolicy_EffectiveAndAllows_BehavesCorrectly()
        {
            RatingPolicy p1 = new RatingPolicy(Rating.R18, Rating.G);
            Assert.AreEqual(Rating.G, p1.Effective);

            RatingPolicy p2 = new RatingPolicy(Rating.G, Rating.R18);
            Assert.AreEqual(Rating.G, p2.Effective);

            RatingPolicy p3 = new RatingPolicy(Rating.R18, Rating.R18);
            Assert.AreEqual(Rating.R18, p3.Effective);

            RatingPolicy p4 = new RatingPolicy(Rating.R15, Rating.R18);
            Assert.IsFalse(p4.Allows(Rating.R18));
            Assert.IsTrue(p4.Allows(Rating.R15));
        }

        // -------------------------------------------------------------
        // A1-8: Result.Success().Ok == true、Result.Fail("x").Error == "x"；Result<int>.Success(5).Value == 5。
        // -------------------------------------------------------------
        [Test]
        public void A1_8_Result_SuccessAndFail_HoldExpectedValues()
        {
            Result success = Result.Success();
            Assert.IsTrue(success.Ok);

            Result fail = Result.Fail("x");
            Assert.AreEqual("x", fail.Error);

            Result<int> successVal = Result<int>.Success(5);
            Assert.AreEqual(5, successVal.Value);
        }
    }
}
