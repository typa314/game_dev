using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Domain;
using Game.Domain.Conditions;
using Game.Domain.Encounter;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A6：巧遇系統引擎（EncounterEngine.cs）單元測試
    /// 恪守 Claude 既定之 DDD 分層架構與設計約定（design/07_gacha_and_encounter.md §2.3）
    /// 涵蓋查核點 A6-1 ～ A6-9
    /// </summary>
    [TestFixture]
    public class EncounterEngineTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; }
            public DateTime LocalNow => UtcNow;

            public FixedClock(DateTime utc)
            {
                UtcNow = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            }
        }

        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static ConditionContext CreateContext(MasterDatabase m, PlayerState p, IClock clock = null, RatingPolicy rating = null)
        {
            return new ConditionContext
            {
                Player = p,
                Master = m,
                Clock = clock ?? new FixedClock(new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc)),
                Rating = rating ?? new RatingPolicy(Rating.R18, Rating.R18),
                SelfChar = null
            };
        }

        // =============================================================
        // A6-1：系列事件只會出現「下一段」（尚未看過第 1 段時，第 2 段不會出現）
        // =============================================================

        [Test]
        public void A6_1_SeriesEvents_OnlyProgressesToNextOrder()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();
            p.Characters["char04_elysia"] = new CharacterState { Affection = 100 }; // 滿足第 2 段的好感條件
            ConditionContext ctx = CreateContext(m, p);

            // 尚未看過第 1 段 (enc_char04_elysia_01)
            List<EncounterEventDef> candStep1 = EncounterEngine.Candidates("spot_park", ctx);
            Assert.IsTrue(candStep1.Any(e => e.Id == "enc_char04_elysia_01"), "第 1 段事件應出現於候選名單");
            Assert.IsFalse(candStep1.Any(e => e.Id == "enc_char04_elysia_02"), "未看過第 1 段前，第 2 段絕對不應出現");

            // 看過第 1 段
            p.Encounter.Seen.Add("enc_char04_elysia_01");
            List<EncounterEventDef> candStep2 = EncounterEngine.Candidates("spot_park", ctx);
            Assert.IsFalse(candStep2.Any(e => e.Id == "enc_char04_elysia_01"), "第 1 段為非重複事件，看過後不應再次出現");
            Assert.IsTrue(candStep2.Any(e => e.Id == "enc_char04_elysia_02"), "看過第 1 段後，第 2 段應解鎖並出現");
        }

        // =============================================================
        // A6-2：不可重複事件看過後不再出現；可重複事件會持續出現
        // =============================================================

        [Test]
        public void A6_2_RepeatableVersusNonRepeatableEvents()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();
            p.Characters["char04_elysia"] = new CharacterState();
            ConditionContext ctx = CreateContext(m, p);

            // enc_char04_elysia_01: repeatable = false
            // enc_park_generic: repeatable = true
            List<EncounterEventDef> initial = EncounterEngine.Candidates("spot_park", ctx);
            Assert.IsTrue(initial.Any(e => e.Id == "enc_char04_elysia_01"));
            Assert.IsTrue(initial.Any(e => e.Id == "enc_park_generic"));

            // 標記兩者均已看過
            p.Encounter.Seen.Add("enc_char04_elysia_01");
            p.Encounter.Seen.Add("enc_park_generic");

            List<EncounterEventDef> afterSeen = EncounterEngine.Candidates("spot_park", ctx);
            Assert.IsFalse(afterSeen.Any(e => e.Id == "enc_char04_elysia_01"), "非重複事件看過後應被排除");
            Assert.IsTrue(afterSeen.Any(e => e.Id == "enc_park_generic"), "可重複事件看過後仍應持續出現");
        }

        // =============================================================
        // A6-3：分級不允許或條件不成立的事件不會出現
        // =============================================================

        [Test]
        public void A6_3_RatingAndConditionFiltering()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();
            p.Characters["char04_elysia"] = new CharacterState { Affection = 10 }; // 未滿 30
            p.Encounter.Seen.Add("enc_char04_elysia_01"); // 解鎖第 2 段的前提順序

            ConditionContext ctx = CreateContext(m, p);

            // enc_char04_elysia_02 要求 affection >= 30，當前僅 10
            List<EncounterEventDef> candConditionFail = EncounterEngine.Candidates("spot_park", ctx);
            Assert.IsFalse(candConditionFail.Any(e => e.Id == "enc_char04_elysia_02"), "條件不成立之事件不應出現");

            // 動態新增 R18 事件於 spot_park
            EncounterEventDef r18Event = new EncounterEventDef
            {
                Id = "enc_r18_test",
                Spot = "spot_park",
                Rating = Rating.R18,
                Repeatable = true,
                Weight = 10,
                Outcomes = new Dictionary<string, List<Outcome>> { { "c0", new List<Outcome>() } }
            };
            m.Encounters[r18Event.Id] = r18Event;

            // 在 G 政策下
            ConditionContext ctxG = CreateContext(m, p, rating: new RatingPolicy(Rating.G, Rating.G));
            Assert.IsFalse(EncounterEngine.Candidates("spot_park", ctxG).Any(e => e.Id == "enc_r18_test"), "G 政策下 R18 事件不應出現");

            // 放寬為 R18 政策
            ConditionContext ctxR18 = CreateContext(m, p, rating: new RatingPolicy(Rating.R18, Rating.R18));
            Assert.IsTrue(EncounterEngine.Candidates("spot_park", ctxR18).Any(e => e.Id == "enc_r18_test"), "R18 政策下應允許出現");
        }

        // =============================================================
        // A6-4：加權隨機測試
        // 抽 10 萬次，各事件被選中比例符合 weight（在 99.9% 信賴區間 3.29σ 內）
        // =============================================================

        [Test]
        public void A6_4_WeightedPick_MatchesWeightDistribution()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();

            // 建立純粹的雙事件加權測試環境 (權重 10 vs 30，期望比例 0.25 vs 0.75)
            m.Encounters.Clear();
            m.Encounters["enc_w10"] = new EncounterEventDef
            {
                Id = "enc_w10", Spot = "spot_test", Weight = 10, Repeatable = true,
                Outcomes = new Dictionary<string, List<Outcome>> { { "c", new List<Outcome>() } }
            };
            m.Encounters["enc_w30"] = new EncounterEventDef
            {
                Id = "enc_w30", Spot = "spot_test", Weight = 30, Repeatable = true,
                Outcomes = new Dictionary<string, List<Outcome>> { { "c", new List<Outcome>() } }
            };

            ConditionContext ctx = CreateContext(m, p);
            IRandom rng = new SplitMix64Random(20261014UL);

            int total = 100000;
            int count10 = 0;

            for (int i = 0; i < total; i++)
            {
                EncounterEventDef chosen = EncounterEngine.Select("spot_test", ctx, rng);
                if (chosen.Id == "enc_w10") count10++;
            }

            double pObserved = (double)count10 / total;
            double pExpected = 0.25;
            double z = 3.29;
            double sigma = Math.Sqrt(pExpected * (1 - pExpected) / total);

            Assert.IsTrue(Math.Abs(pObserved - pExpected) <= z * sigma,
                string.Format("權重 10/40 實測比例 {0:F5} 超出 0.25 ± {1:F5}", pObserved, z * sigma));
        }

        // =============================================================
        // A6-4b：未持有角色的事件不會出現，持有後才出現
        // =============================================================

        [Test]
        public void A6_4b_Candidates_ExcludeEventsOfUnownedCharacters()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();
            ConditionContext ctx = CreateContext(m, p);

            // enc_char01_claire_cafe 指定角色 char01_claire
            Assert.IsFalse(p.Characters.ContainsKey("char01_claire"), "前提：尚未持有 Claire");
            List<EncounterEventDef> withoutClaire = EncounterEngine.Candidates("spot_cafe", ctx);
            Assert.IsFalse(withoutClaire.Any(e => e.Id == "enc_char01_claire_cafe"), "未持有該角色時事件不可出現");

            // 獲得角色
            p.Characters["char01_claire"] = new CharacterState();
            List<EncounterEventDef> withClaire = EncounterEngine.Candidates("spot_cafe", ctx);
            Assert.IsTrue(withClaire.Any(e => e.Id == "enc_char01_claire_cafe"), "持有該角色後事件應正常出現");
        }

        // =============================================================
        // A6-5：保底機制
        // MissStreak 達到門檻 (5) 且有系列事件時，必選系列事件；選中後歸零；選中非系列時 +1
        // =============================================================

        [Test]
        public void A6_5_PityGuarantee_ForcesSeriesEventAndResetsStreak()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();
            p.Characters["char04_elysia"] = new CharacterState();
            ConditionContext ctx = CreateContext(m, p);

            // 門檻設為 5
            ctx.Master.Config.Encounter.PityThreshold = 5;

            // 情境 1：MissStreak 未達門檻（例如 4）
            p.Encounter.MissStreak = 4;
            // 使用強制抽到非系列事件 (enc_park_generic) 的 mock RNG
            // spot_park 中有 enc_char04_elysia_01 (權重 10) 與 enc_park_generic (權重 20)
            // 亂數回傳 15 (落在 10~30 區間，選中 enc_park_generic)
            IRandom mockPickGeneric = new SplitMix64Random(999UL);
            // 手動驗證單次抽到非系列事件時 streak 遞增
            p.Encounter.MissStreak = 2;
            EncounterEventDef pickedNonSeries = EncounterEngine.Select("spot_park", ctx, new FixedRng(15));
            Assert.AreEqual("enc_park_generic", pickedNonSeries.Id);
            Assert.AreEqual(3, p.Encounter.MissStreak, "選中非系列事件時 MissStreak 應 +1");

            // 情境 2：MissStreak 達到門檻 5
            p.Encounter.MissStreak = 5;
            // 即使 RNG 指向非系列，因為觸發保底，pickFrom 縮減為僅有 series 事件
            EncounterEventDef pityChosen = EncounterEngine.Select("spot_park", ctx, new FixedRng(999));
            Assert.IsNotNull(pityChosen);
            Assert.IsNotNull(pityChosen.Series, "達到保底門檻必須選中系列事件");
            Assert.AreEqual(0, p.Encounter.MissStreak, "選中系列事件後 MissStreak 應重設歸零");
        }

        private class FixedRng : IRandom
        {
            private readonly int _val;
            public FixedRng(int val) { _val = val; }
            public double NextDouble() => 0.5;
            public int NextInt(int maxExclusive) => _val % maxExclusive;
        }

        // =============================================================
        // A6-6：無任何候選事件時回傳 null，MissStreak +1
        // =============================================================

        [Test]
        public void A6_6_NoCandidates_ReturnsNullAndIncrementsStreak()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p = new PlayerState();
            ConditionContext ctx = CreateContext(m, p);

            p.Encounter.MissStreak = 2;
            IRandom rng = new SplitMix64Random(1UL);

            // 查詢一個不存在任何事件的地點
            EncounterEventDef chosen = EncounterEngine.Select("spot_nonexistent", ctx, rng);

            Assert.IsNull(chosen, "無候選事件時應回傳 null");
            Assert.AreEqual(3, p.Encounter.MissStreak, "無候選事件時 MissStreak 應 +1");
        }

        // =============================================================
        // A6-7：體力自然回復計算
        // 0 點經過 30 分鐘變 1；29 分鐘仍是 0；10 小時到達上限 5；未滿保留零頭
        // =============================================================

        [Test]
        public void A6_7_RegenStamina_CalculatesTimeIntervalsAccurately()
        {
            EncounterState st = new EncounterState { Stamina = 0 };
            EncounterConfig cfg = new EncounterConfig
            {
                StaminaMax = 5,
                StaminaRegenMinutes = 30
            };

            DateTime t0 = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            st.StaminaUpdatedAtUtc = t0.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

            // 經過 29 分鐘：未達 1 週期，體力仍為 0，基準點保持 t0
            EncounterEngine.RegenStamina(st, cfg, t0.AddMinutes(29));
            Assert.AreEqual(0, st.Stamina, "經過 29 分鐘體力仍應為 0");
            Assert.AreEqual(t0.ToString("o"), st.StaminaUpdatedAtUtc);

            // 經過 30 分鐘：恰好 1 週期，體力變 1，基準點更新為 t0 + 30m
            EncounterEngine.RegenStamina(st, cfg, t0.AddMinutes(30));
            Assert.AreEqual(1, st.Stamina, "經過 30 分鐘體力應回復為 1");
            Assert.AreEqual(t0.AddMinutes(30).ToString("o"), st.StaminaUpdatedAtUtc);

            // 重設為 0 體力，經過 75 分鐘 (2 週期 60 分鐘 + 15 分鐘零頭)
            st.Stamina = 0;
            st.StaminaUpdatedAtUtc = t0.ToString("o");
            EncounterEngine.RegenStamina(st, cfg, t0.AddMinutes(75));
            Assert.AreEqual(2, st.Stamina, "75 分鐘應回復 2 點體力");
            Assert.AreEqual(t0.AddMinutes(60).ToString("o"), st.StaminaUpdatedAtUtc, "未滿時應保留 15 分鐘零頭，基準點為 t0 + 60m");

            // 經過 10 小時 (600 分鐘)：到達上限 5 點
            st.Stamina = 0;
            st.StaminaUpdatedAtUtc = t0.ToString("o");
            EncounterEngine.RegenStamina(st, cfg, t0.AddHours(10));
            Assert.AreEqual(5, st.Stamina, "經過 10 小時體力應回復至上限 5 點");
            Assert.AreEqual(t0.AddHours(10).ToString("o"), st.StaminaUpdatedAtUtc, "滿體力時基準點對齊當前時間");
        }

        // =============================================================
        // A6-8：系統時間被調回過去時不回復體力，並把基準點重設為現在
        // =============================================================

        [Test]
        public void A6_8_RegenStamina_ClockTamperedBackwards_DoesNotRegenAndResetsTimestamp()
        {
            EncounterState st = new EncounterState { Stamina = 2 };
            EncounterConfig cfg = new EncounterConfig
            {
                StaminaMax = 5,
                StaminaRegenMinutes = 30
            };

            DateTime t0 = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            st.StaminaUpdatedAtUtc = t0.ToString("o");

            // 系統時間被惡意往前調 2 小時
            DateTime tamperedTime = t0.AddHours(-2);
            EncounterEngine.RegenStamina(st, cfg, tamperedTime);

            Assert.AreEqual(2, st.Stamina, "時間回溯時體力不應增加");
            Assert.AreEqual(tamperedTime.ToString("o"), st.StaminaUpdatedAtUtc, "基準點應重設為調校後的時間");
        }

        // =============================================================
        // A6-9：決定性驗證（同一個 seed 和同一組資料，挑選結果絕對一致）
        // =============================================================

        [Test]
        public void A6_9_Determinism_IdenticalSeedProducesIdenticalSelections()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState p1 = new PlayerState();
            p1.Characters["char04_elysia"] = new CharacterState();
            PlayerState p2 = new PlayerState();
            p2.Characters["char04_elysia"] = new CharacterState();

            ConditionContext ctx1 = CreateContext(m, p1);
            ConditionContext ctx2 = CreateContext(m, p2);

            IRandom rng1 = new SplitMix64Random(77777UL);
            IRandom rng2 = new SplitMix64Random(77777UL);

            for (int i = 0; i < 50; i++)
            {
                EncounterEventDef e1 = EncounterEngine.Select("spot_park", ctx1, rng1);
                EncounterEventDef e2 = EncounterEngine.Select("spot_park", ctx2, rng2);
                Assert.AreEqual(e1.Id, e2.Id, "第 " + i + " 次抽取結果必須絕對一致");
                Assert.AreEqual(p1.Encounter.MissStreak, p2.Encounter.MissStreak);
            }
        }
    }
}
