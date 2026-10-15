using System;
using System.Collections.Generic;
using Game.Domain;
using Game.Domain.Conditions;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A3：通用條件 DSL（ConditionEvaluator.cs）單元測試
    /// 涵蓋查核點 A3-1 ～ A3-7
    /// </summary>
    [TestFixture]
    public class ConditionEvaluatorTests
    {
        private class TestClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public DateTime LocalNow { get; set; }

            public TestClock(DateTime local)
            {
                LocalNow = local;
                UtcNow = local.ToUniversalTime();
            }
        }

        private static ConditionContext CreateBaseContext()
        {
            PlayerState player = new PlayerState();
            return new ConditionContext
            {
                Player = player,
                Master = null,
                Rating = new RatingPolicy(Rating.R18, Rating.R18),
                Clock = new TestClock(new DateTime(2026, 10, 14, 12, 0, 0)),
                SelfChar = "char01_claire",
                StoryVariable = null,
                EnemyBaseHpPct = null
            };
        }

        // =============================================================
        // A3-1：16 種 type 每種至少 1 個成立、1 個不成立
        // =============================================================

        [Test]
        public void A3_1_01_All_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState { Level = 10, Awaken = 2 };

            Condition cTrue = new Condition
            {
                Type = "all",
                Of = new List<Condition>
                {
                    new Condition { Type = "char_owned", Char = "char01_claire" },
                    new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "10" }
                }
            };
            Condition cFalse = new Condition
            {
                Type = "all",
                Of = new List<Condition>
                {
                    new Condition { Type = "char_owned", Char = "char01_claire" },
                    new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "20" }
                }
            };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "all: 所有子條件成立時應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "all: 有任一子條件不成立時應回傳 false");
        }

        [Test]
        public void A3_1_02_Any_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState { Level = 10 };

            Condition cTrue = new Condition
            {
                Type = "any",
                Of = new List<Condition>
                {
                    new Condition { Type = "char_owned", Char = "char_unowned" },
                    new Condition { Type = "char_owned", Char = "char01_claire" }
                }
            };
            Condition cFalse = new Condition
            {
                Type = "any",
                Of = new List<Condition>
                {
                    new Condition { Type = "char_owned", Char = "char_unowned1" },
                    new Condition { Type = "char_owned", Char = "char_unowned2" }
                }
            };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "any: 有任一子條件成立時應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "any: 所有子條件皆不成立時應回傳 false");
        }

        [Test]
        public void A3_1_03_Not_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState();

            Condition cTrue = new Condition
            {
                Type = "not",
                Cond = new Condition { Type = "char_owned", Char = "char_unowned" }
            };
            Condition cFalse = new Condition
            {
                Type = "not",
                Cond = new Condition { Type = "char_owned", Char = "char01_claire" }
            };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "not: 內層為 false 時應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "not: 內層為 true 時應回傳 false");
        }

        [Test]
        public void A3_1_04_CharOwned_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState();

            Condition cTrue = new Condition { Type = "char_owned", Char = "char01_claire" };
            Condition cFalse = new Condition { Type = "char_owned", Char = "char_unowned" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "char_owned: 已持有角色應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "char_owned: 未持有角色應回傳 false");
        }

        [Test]
        public void A3_1_05_CharLevelGte_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState { Level = 15 };

            Condition cTrue = new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "15" };
            Condition cFalse = new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "16" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "char_level_gte: 等級 >= 門檻應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "char_level_gte: 等級 < 門檻應回傳 false");
        }

        [Test]
        public void A3_1_06_CharAwakenGte_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState { Awaken = 3 };

            Condition cTrue = new Condition { Type = "char_awaken_gte", Char = "char01_claire", Value = "3" };
            Condition cFalse = new Condition { Type = "char_awaken_gte", Char = "char01_claire", Value = "4" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "char_awaken_gte: 覺醒 >= 門檻應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "char_awaken_gte: 覺醒 < 門檻應回傳 false");
        }

        [Test]
        public void A3_1_07_AffectionGte_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Characters["char01_claire"] = new CharacterState { Affection = 100 };

            Condition cTrue = new Condition { Type = "affection_gte", Char = "char01_claire", Value = "100" };
            Condition cFalse = new Condition { Type = "affection_gte", Char = "char01_claire", Value = "101" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "affection_gte: 好感度 >= 門檻應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "affection_gte: 好感度 < 門檻應回傳 false");
        }

        [Test]
        public void A3_1_08_StageCleared_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Stages["st_1_01"] = new StageProgress { Cleared = true };
            ctx.Player.Stages["st_1_02"] = new StageProgress { Cleared = false };

            Condition cTrue = new Condition { Type = "stage_cleared", Stage = "st_1_01" };
            Condition cFalseUncleared = new Condition { Type = "stage_cleared", Stage = "st_1_02" };
            Condition cFalseMissing = new Condition { Type = "stage_cleared", Stage = "st_9_99" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "stage_cleared: 關卡已通關應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalseUncleared, ctx), "stage_cleared: 關卡未通關應回傳 false");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalseMissing, ctx), "stage_cleared: 關卡無紀錄應回傳 false");
        }

        [Test]
        public void A3_1_09_StoryFlag_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Story.Flags["quest_progress"] = 5;

            Condition cTrue = new Condition { Type = "story_flag", Key = "quest_progress", Op = ">=", Value = "5" };
            Condition cFalse = new Condition { Type = "story_flag", Key = "quest_progress", Op = ">=", Value = "6" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "story_flag: 符合條件應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "story_flag: 不符條件應回傳 false");
        }

        [Test]
        public void A3_1_10_StoryNode_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Story.ReadNodes.Add("node_intro");

            Condition cTrue = new Condition { Type = "story_node", Node = "node_intro" };
            Condition cFalse = new Condition { Type = "story_node", Node = "node_ending" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "story_node: 節點已閱讀應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "story_node: 節點未閱讀應回傳 false");
        }

        [Test]
        public void A3_1_11_EndingReached_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Endings["ed_true"] = "2026-10-14T12:00:00Z";

            Condition cTrue = new Condition { Type = "ending_reached", Ending = "ed_true" };
            Condition cFalse = new Condition { Type = "ending_reached", Ending = "ed_bad" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "ending_reached: 結局已達成應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "ending_reached: 結局未達成應回傳 false");
        }

        [Test]
        public void A3_1_12_Achievement_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Achievements["ach_first_win"] = new AchievementProgress { UnlockedAtUtc = "2026-10-14T12:00:00Z" };
            ctx.Player.Achievements["ach_locked"] = new AchievementProgress { UnlockedAtUtc = null };

            Condition cTrue = new Condition { Type = "achievement", Id = "ach_first_win" };
            Condition cFalseLocked = new Condition { Type = "achievement", Id = "ach_locked" };
            Condition cFalseMissing = new Condition { Type = "achievement", Id = "ach_missing" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "achievement: 成就已解鎖應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalseLocked, ctx), "achievement: 成就未解鎖應回傳 false");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalseMissing, ctx), "achievement: 成就無紀錄應回傳 false");
        }

        [Test]
        public void A3_1_13_TimeOfDay_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Clock = new TestClock(new DateTime(2026, 10, 14, 8, 30, 0)); // morning

            Condition cTrue = new Condition { Type = "time_of_day", Value = "morning" };
            Condition cFalse = new Condition { Type = "time_of_day", Value = "night" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "time_of_day: 時段相符應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "time_of_day: 時段不符應回傳 false");
        }

        [Test]
        public void A3_1_14_Weekday_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            // 2026-10-14 為星期三 (Wednesday)
            ctx.Clock = new TestClock(new DateTime(2026, 10, 14, 12, 0, 0));

            Condition cTrue = new Condition { Type = "weekday", Value = "Wednesday" };
            Condition cTrueIgnoreCase = new Condition { Type = "weekday", Value = "wednesday" };
            Condition cFalse = new Condition { Type = "weekday", Value = "Sunday" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "weekday: 星期相符應回傳 true");
            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrueIgnoreCase, ctx), "weekday: 星期大小寫不拘應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "weekday: 星期不符應回傳 false");
        }

        [Test]
        public void A3_1_15_RatingAllowed_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Rating = new RatingPolicy(Rating.R18, Rating.R18);

            Condition cTrue = new Condition { Type = "rating_allowed", Rating = "R18" };
            Condition cFalse = new Condition { Type = "rating_allowed", Rating = "R18" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "rating_allowed: 在 R18 政策下評估 R18 應回傳 true");

            // 切換為 G 政策
            ctx.Rating = new RatingPolicy(Rating.G, Rating.G);
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalse, ctx), "rating_allowed: 在 G 政策下評估 R18 應回傳 false");
        }

        [Test]
        public void A3_1_16_EnemyBaseHpBelowPct_EvaluatesCorrectly()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.EnemyBaseHpPct = 40;

            Condition cTrue = new Condition { Type = "enemy_base_hp_below_pct", Value = "50" };
            Condition cFalseGte = new Condition { Type = "enemy_base_hp_below_pct", Value = "40" };
            Condition cFalseLower = new Condition { Type = "enemy_base_hp_below_pct", Value = "30" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cTrue, ctx), "enemy_base_hp_below_pct: 40 < 50 應回傳 true");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalseGte, ctx), "enemy_base_hp_below_pct: 40 < 40 應回傳 false");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cFalseLower, ctx), "enemy_base_hp_below_pct: 40 < 30 應回傳 false");

            // 當 EnemyBaseHpPct 為 null 時應回傳 false
            ctx.EnemyBaseHpPct = null;
            Assert.IsFalse(ConditionEvaluator.Evaluate(cTrue, ctx), "enemy_base_hp_below_pct: 未提供 EnemyBaseHpPct 時應回傳 false");
        }

        // =============================================================
        // A3-2：null 條件、空 all、空 any
        // =============================================================

        [Test]
        public void A3_2_NullCondition_ReturnsTrue()
        {
            ConditionContext ctx = CreateBaseContext();
            Assert.IsTrue(ConditionEvaluator.Evaluate(null, ctx), "null 條件視為無條件限制，應回傳 true");
        }

        [Test]
        public void A3_2_All_EmptyOrNullOf_ReturnsTrue()
        {
            ConditionContext ctx = CreateBaseContext();

            Condition cEmpty = new Condition { Type = "all", Of = new List<Condition>() };
            Condition cNull = new Condition { Type = "all", Of = null };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cEmpty, ctx), "all 的 of 為空清單時應回傳 true");
            Assert.IsTrue(ConditionEvaluator.Evaluate(cNull, ctx), "all 的 of 為 null 時應回傳 true");
        }

        [Test]
        public void A3_2_Any_EmptyOrNullOf_ReturnsFalse()
        {
            ConditionContext ctx = CreateBaseContext();

            Condition cEmpty = new Condition { Type = "any", Of = new List<Condition>() };
            Condition cNull = new Condition { Type = "any", Of = null };

            Assert.IsFalse(ConditionEvaluator.Evaluate(cEmpty, ctx), "any 的 of 為空清單時應回傳 false");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cNull, ctx), "any 的 of 為 null 時應回傳 false");
        }

        // =============================================================
        // A3-3：char: "self" 判定與角色未持有規則
        // =============================================================

        [Test]
        public void A3_3_SelfChar_ResolvesToSelfChar()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.SelfChar = "char01_claire";
            ctx.Player.Characters["char01_claire"] = new CharacterState { Level = 25, Awaken = 1, Affection = 50 };

            Condition cOwned = new Condition { Type = "char_owned", Char = "self" };
            Condition cLevel = new Condition { Type = "char_level_gte", Char = "self", Value = "20" };
            Condition cAwaken = new Condition { Type = "char_awaken_gte", Char = "self", Value = "1" };
            Condition cAffection = new Condition { Type = "affection_gte", Char = "self", Value = "50" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cOwned, ctx), "char: 'self' 應解析為 ctx.SelfChar 並成立");
            Assert.IsTrue(ConditionEvaluator.Evaluate(cLevel, ctx), "char: 'self' 等級應依 ctx.SelfChar 判定並成立");
            Assert.IsTrue(ConditionEvaluator.Evaluate(cAwaken, ctx), "char: 'self' 覺醒應依 ctx.SelfChar 判定並成立");
            Assert.IsTrue(ConditionEvaluator.Evaluate(cAffection, ctx), "char: 'self' 好感度應依 ctx.SelfChar 判定並成立");
        }

        [Test]
        public void A3_3_UnownedChar_AllCharConditionsReturnFalse()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.SelfChar = "char_not_owned";

            // 直接指定未持有角色
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "char_owned", Char = "char_not_owned" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "char_level_gte", Char = "char_not_owned", Value = "1" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "char_awaken_gte", Char = "char_not_owned", Value = "0" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "affection_gte", Char = "char_not_owned", Value = "0" }, ctx));

            // 透過 self 指定未持有角色
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "char_owned", Char = "self" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "char_level_gte", Char = "self", Value = "1" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "char_awaken_gte", Char = "self", Value = "0" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "affection_gte", Char = "self", Value = "0" }, ctx));
        }

        // =============================================================
        // A3-4：story_flag 優先級與六種比較運算子
        // =============================================================

        [Test]
        public void A3_4_StoryFlag_PrefersStoryVariableOverGlobalFlag()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Story.Flags["chapter_var"] = 100;
            ctx.StoryVariable = key => key == "chapter_var" ? 200.0 : (double?)null;

            Condition c = new Condition { Type = "story_flag", Key = "chapter_var", Op = "==", Value = "200" };
            Assert.IsTrue(ConditionEvaluator.Evaluate(c, ctx), "story_flag 應優先讀取 StoryVariable 的 200 而非全域旗標的 100");
        }

        [Test]
        public void A3_4_StoryFlag_FallsBackToGlobalFlagWhenVariableNull()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Story.Flags["chapter_var"] = 100;
            ctx.StoryVariable = key => null; // 找不到區域變數

            Condition c = new Condition { Type = "story_flag", Key = "chapter_var", Op = "==", Value = "100" };
            Assert.IsTrue(ConditionEvaluator.Evaluate(c, ctx), "StoryVariable 回傳 null 時應回退讀取全域旗標");
        }

        [Test]
        public void A3_4_StoryFlag_DefaultsToZeroWhenNeitherExists()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.StoryVariable = null;

            Condition cZero = new Condition { Type = "story_flag", Key = "missing_var", Op = "==", Value = "0" };
            Condition cPositive = new Condition { Type = "story_flag", Key = "missing_var", Op = ">", Value = "0" };

            Assert.IsTrue(ConditionEvaluator.Evaluate(cZero, ctx), "變數皆不存在時應視為 0");
            Assert.IsFalse(ConditionEvaluator.Evaluate(cPositive, ctx), "0 > 0 應為 false");
        }

        [Test]
        public void A3_4_StoryFlag_AllSixComparisonOperatorsAndDefault()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Story.Flags["val"] = 50;

            // >=
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = ">=", Value = "50" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = ">=", Value = "51" }, ctx));

            // >
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = ">", Value = "49" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = ">", Value = "50" }, ctx));

            // <=
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "<=", Value = "50" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "<=", Value = "49" }, ctx));

            // <
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "<", Value = "51" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "<", Value = "50" }, ctx));

            // ==
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "==", Value = "50" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "==", Value = "51" }, ctx));

            // !=
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "!=", Value = "51" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = "!=", Value = "50" }, ctx));

            // null (預設 >=)
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = null, Value = "50" }, ctx));
            Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Type = "story_flag", Key = "val", Op = null, Value = "51" }, ctx));
        }

        [Test]
        public void A3_4_StoryFlag_UnknownOperator_ThrowsInvalidOperationException()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Player.Story.Flags["val"] = 50;

            Condition c = new Condition { Type = "story_flag", Key = "val", Op = "<>", Value = "50" };
            Assert.Throws<InvalidOperationException>(() =>
            {
                ConditionEvaluator.Evaluate(c, ctx);
            });
        }

        // =============================================================
        // A3-5：時段分界測試
        // morning 5–10、day 11–16、evening 17–19、night 20–4
        // =============================================================

        [TestCase(4, "night")]
        [TestCase(5, "morning")]
        [TestCase(10, "morning")]
        [TestCase(11, "day")]
        [TestCase(16, "day")]
        [TestCase(17, "evening")]
        [TestCase(19, "evening")]
        [TestCase(20, "night")]
        [TestCase(0, "night")]
        [TestCase(23, "night")]
        public void A3_5_TimeOfDay_BoundariesEvaluatedCorrectly(int hour, string expectedPeriod)
        {
            Assert.AreEqual(expectedPeriod, ConditionEvaluator.TimeOfDay(hour), "小時 " + hour + " 應歸為 " + expectedPeriod);

            // 整合透過 Condition 評估
            ConditionContext ctx = CreateBaseContext();
            ctx.Clock = new TestClock(new DateTime(2026, 10, 14, hour, 59, 59));
            Condition c = new Condition { Type = "time_of_day", Value = expectedPeriod };
            Assert.IsTrue(ConditionEvaluator.Evaluate(c, ctx));
        }

        // =============================================================
        // A3-6：未知的 type 丟出 InvalidOperationException
        // =============================================================

        [Test]
        public void A3_6_UnknownType_ThrowsInvalidOperationException()
        {
            ConditionContext ctx = CreateBaseContext();
            Condition c = new Condition { Type = "unknown_dsl_type" };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                ConditionEvaluator.Evaluate(c, ctx);
            });
            Assert.IsTrue(ex.Message.Contains("unknown condition type"), "例外訊息應包含 'unknown condition type'");
        }

        // =============================================================
        // A3-7：rating_allowed 在 G 政策下對 R18 回傳 false
        // =============================================================

        [Test]
        public void A3_7_RatingAllowed_GPolicy_RejectsR18()
        {
            ConditionContext ctx = CreateBaseContext();
            ctx.Rating = new RatingPolicy(Rating.G, Rating.G);

            Condition cR18 = new Condition { Type = "rating_allowed", Rating = "R18" };
            Condition cG = new Condition { Type = "rating_allowed", Rating = "G" };

            Assert.IsFalse(ConditionEvaluator.Evaluate(cR18, ctx), "G 政策下評估 R18 應回傳 false");
            Assert.IsTrue(ConditionEvaluator.Evaluate(cG, ctx), "G 政策下評估 G 應回傳 true");
        }
    }
}
