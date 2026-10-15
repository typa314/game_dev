using System;
using System.Collections.Generic;
using Game.Core;
using Game.Domain;
using Game.Domain.Achievements;
using Game.Domain.Conditions;
using Game.Domain.Events;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A8：成就追蹤系統（AchievementTracker.cs）單元測試
    /// 恪守 Claude 既定之成就系統設計（design/13_achievements.md）
    /// 涵蓋查核點 A8-1 ～ A8-8
    /// </summary>
    [TestFixture]
    public class AchievementTrackerTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; }
            public DateTime LocalNow => UtcNow;

            public FixedClock(DateTime? utc = null)
            {
                UtcNow = DateTime.SpecifyKind(utc ?? new DateTime(2026, 10, 14, 12, 0, 0), DateTimeKind.Utc);
            }
        }

        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static AchievementTracker CreateTracker(MasterDatabase master, PlayerState player, EventBus bus, RatingPolicy rating = null, IClock clock = null)
        {
            rating = rating ?? new RatingPolicy(Rating.R18, Rating.R18);
            clock = clock ?? new FixedClock();
            var ctx = new ConditionContext
            {
                Player = player,
                Master = master,
                Rating = rating,
                Clock = clock
            };
            var tracker = new AchievementTracker(master, ctx, bus, clock);
            tracker.Attach();
            return tracker;
        }

        // =============================================================
        // A8-1：sum 計數器
        // 抽 1 次加 1、十連加 10；累計到 100 時解鎖 ach_gacha_100
        // =============================================================

        [Test]
        public void A8_1_SumCounter_AccumulatesFieldValue_AndUnlocksAtTarget()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();
            var unlockedEvents = new List<string>();
            bus.Subscribe(e =>
            {
                if (e.Name == Ev.AchievementUnlocked) unlockedEvents.Add(e.Get("achievement"));
            });

            AchievementTracker tracker = CreateTracker(m, player, bus);

            // 單抽 1 次
            bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 1));

            AchievementProgress firstPull = player.Achievements["ach_first_pull"];
            AchievementProgress gacha100 = player.Achievements["ach_gacha_100"];

            Assert.AreEqual(1, firstPull.Progress);
            Assert.IsNotNull(firstPull.UnlockedAtUtc, "單抽 1 次應達成 ach_first_pull (target 1)");
            Assert.IsTrue(unlockedEvents.Contains("ach_first_pull"));

            Assert.AreEqual(1, gacha100.Progress);
            Assert.IsNull(gacha100.UnlockedAtUtc, "累積 1 抽未達 ach_gacha_100 (target 100)");

            // 十連抽加 10
            bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 10));
            Assert.AreEqual(11, gacha100.Progress, "十連後累計應為 11 抽");
            Assert.IsNull(gacha100.UnlockedAtUtc);

            // 再抽 89 次達標 100
            bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 89));
            Assert.AreEqual(100, gacha100.Progress);
            Assert.IsNotNull(gacha100.UnlockedAtUtc, "累積達到 100 抽應成功解鎖 ach_gacha_100");
            Assert.IsTrue(unlockedEvents.Contains("ach_gacha_100"));
        }

        // =============================================================
        // A8-2：count 計數器
        // 只計算符合 filter 的事件 (outcome = Win)
        // =============================================================

        [Test]
        public void A8_2_CountCounter_OnlyCountsMatchingEvents()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();
            var unlockedEvents = new List<string>();
            bus.Subscribe(e =>
            {
                if (e.Name == Ev.AchievementUnlocked) unlockedEvents.Add(e.Get("achievement"));
            });

            AchievementTracker tracker = CreateTracker(m, player, bus);

            // 戰鬥失敗事件，filter 為 outcome = Win，不應計數
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_01").With("outcome", "Lose"));

            AchievementProgress firstWin = player.Achievements["ach_first_win"];
            Assert.AreEqual(0, firstWin.Progress, "不符合 filter 的事件不應增加進度");
            Assert.IsNull(firstWin.UnlockedAtUtc, "不符合 filter 的事件不應解鎖");

            // 戰鬥獲勝事件，符合 filter，計數 +1 並解鎖
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_01").With("outcome", "Win"));

            Assert.AreEqual(1, firstWin.Progress);
            Assert.IsNotNull(firstWin.UnlockedAtUtc, "首勝應達成 ach_first_win");
            Assert.IsTrue(unlockedEvents.Contains("ach_first_win"));
        }

        // =============================================================
        // A8-3：distinct 計數器
        // 同一關卡 3 星通關兩次只算 1；三個不同關卡才達到 target 3
        // =============================================================

        [Test]
        public void A8_3_DistinctCounter_DeduplicatesValues_AndRequiresUniqueTargets()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();
            AchievementTracker tracker = CreateTracker(m, player, bus);

            // 關卡 1-01 取得 3 星
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_01").With("outcome", "Win").With("stars", "3"));
            AchievementProgress p = player.Achievements["ach_ch1_3star"];
            Assert.AreEqual(1, p.Progress);
            Assert.IsNull(p.UnlockedAtUtc);

            // 再次於 1-01 取得 3 星（重複）
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_01").With("outcome", "Win").With("stars", "3"));
            Assert.AreEqual(1, p.Progress, "重複關卡不應增加 distinct 計數");
            Assert.IsNull(p.UnlockedAtUtc);

            // 關卡 1-02 取得 2 星（星數不符合 filter stars = 3）
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_02").With("outcome", "Win").With("stars", "2"));
            Assert.AreEqual(1, p.Progress, "不符 filter 條件不應計入 distinct");

            // 關卡 2-01 取得 3 星（stage 不符合 st_1_* filter）
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_2_01").With("outcome", "Win").With("stars", "3"));
            Assert.AreEqual(1, p.Progress, "非第一章關卡不符 st_1_* 通配比對");

            // 關卡 1-02 取得 3 星（第 2 個相異關卡）
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_02").With("outcome", "Win").With("stars", "3"));
            Assert.AreEqual(2, p.Progress);
            Assert.IsNull(p.UnlockedAtUtc);

            // 關卡 1-03 取得 3 星（第 3 個相異關卡，達成目標）
            bus.Publish(new DomainEvent(Ev.BattleFinished).With("stage", "st_1_03").With("outcome", "Win").With("stars", "3"));
            Assert.AreEqual(3, p.Progress);
            Assert.IsNotNull(p.UnlockedAtUtc, "三個不同第一章關卡滿星應解鎖 ach_ch1_3star");
        }

        // =============================================================
        // A8-4：state 計數器
        // 事件後條件成立就解鎖；指定 event 時只有該事件觸發判斷
        // =============================================================

        [Test]
        public void A8_4_StateCounter_EvaluatesConditionOnEvents_AndRespectsEventSpecification()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();

            // 新增兩個 state 成就：一個指定 event 為 CharacterLeveledUp，一個不指定 event (null)
            var achSpecific = new AchievementDef
            {
                Id = "ach_state_specific",
                Rating = Rating.G,
                Trigger = new AchievementTrigger
                {
                    Counter = "state",
                    Event = Ev.CharacterLeveledUp,
                    Condition = new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "10" }
                }
            };
            var achAny = new AchievementDef
            {
                Id = "ach_state_any",
                Rating = Rating.G,
                Trigger = new AchievementTrigger
                {
                    Counter = "state",
                    Event = null, // 任何事件皆可觸發評估
                    Condition = new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "10" }
                }
            };
            m.Achievements[achSpecific.Id] = achSpecific;
            m.Achievements[achAny.Id] = achAny;

            AchievementTracker tracker = CreateTracker(m, player, bus);

            // Claire 初始等級為 5，條件尚未達成
            player.Characters["char01_claire"] = new CharacterState { Level = 5 };
            bus.Publish(new DomainEvent(Ev.CharacterLeveledUp).With("char", "char01_claire").With("level", 5));

            Assert.IsNull(player.Achievements["ach_state_specific"].UnlockedAtUtc);
            Assert.IsNull(player.Achievements["ach_state_any"].UnlockedAtUtc);

            // Claire 等級提升至 10 (條件已滿足)
            player.Characters["char01_claire"].Level = 10;

            // 發佈不相干事件 GachaPulled：
            // ach_state_specific 指定了 CharacterLeveledUp，因此被略過；
            // ach_state_any 未指定事件，因此觸發評估並解鎖。
            bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 1));

            Assert.IsNull(player.Achievements["ach_state_specific"].UnlockedAtUtc, "指定 event 的 state 成就不應被其他事件解鎖");
            Assert.IsNotNull(player.Achievements["ach_state_any"].UnlockedAtUtc, "未指定 event 的 state 成就可在任何事件觸發時解鎖");

            // 發佈指定事件 CharacterLeveledUp：ach_state_specific 評估並解鎖
            bus.Publish(new DomainEvent(Ev.CharacterLeveledUp).With("char", "char01_claire").With("level", 10));
            Assert.IsNotNull(player.Achievements["ach_state_specific"].UnlockedAtUtc, "指定事件觸發時條件成立應成功解鎖");
        }

        // =============================================================
        // A8-5：MatchesFilter 通配前綴與欄位缺失
        // =============================================================

        [Test]
        public void A8_5_Filter_WildcardPrefixMatch_AndMissingFieldHandling()
        {
            var filter = new Dictionary<string, string>
            {
                { "stage", "st_1_*" },
                { "stars", "3" }
            };

            // 完全符合
            var eMatch = new DomainEvent("Test").With("stage", "st_1_02").With("stars", "3");
            Assert.IsTrue(AchievementTracker.MatchesFilter(filter, eMatch));

            // 通配不符 (非第一章)
            var eWrongPrefix = new DomainEvent("Test").With("stage", "st_2_01").With("stars", "3");
            Assert.IsFalse(AchievementTracker.MatchesFilter(filter, eWrongPrefix));

            // 數值不符
            var eWrongStars = new DomainEvent("Test").With("stage", "st_1_02").With("stars", "2");
            Assert.IsFalse(AchievementTracker.MatchesFilter(filter, eWrongStars));

            // 缺少指定欄位
            var eMissingField = new DomainEvent("Test").With("stage", "st_1_02");
            Assert.IsFalse(AchievementTracker.MatchesFilter(filter, eMissingField), "缺少 filter 指定欄位時不應匹配");

            // null filter 永遠匹配
            Assert.IsTrue(AchievementTracker.MatchesFilter(null, eMatch));
        }

        // =============================================================
        // A8-6：已解鎖成就不可重複解鎖或重複發送事件
        // =============================================================

        [Test]
        public void A8_6_AlreadyUnlocked_DoesNotUnlockAgain_OrPublishDuplicateEvent()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();
            int unlockEventCount = 0;
            bus.Subscribe(e =>
            {
                if (e.Name == Ev.AchievementUnlocked && e.Get("achievement") == "ach_first_pull")
                    unlockEventCount++;
            });

            AchievementTracker tracker = CreateTracker(m, player, bus);

            // 第一次抽卡解鎖
            bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 1));
            Assert.AreEqual(1, unlockEventCount);
            string firstUnlockTime = player.Achievements["ach_first_pull"].UnlockedAtUtc;
            Assert.IsNotNull(firstUnlockTime);

            // 再次抽卡符合條件
            bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 1));
            Assert.AreEqual(1, unlockEventCount, "已解鎖成就不應再次發佈 AchievementUnlocked 事件");
            Assert.AreEqual(firstUnlockTime, player.Achievements["ach_first_pull"].UnlockedAtUtc, "已解鎖時間戳記不得變更");
        }

        // =============================================================
        // A8-7：分級不允許時進度累積但不解鎖，放寬後補發解鎖
        // =============================================================

        [Test]
        public void A8_7_RatingRestricted_AccumulatesProgress_UnlocksWhenPolicyRelaxes()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();
            var unlockedEvents = new List<string>();
            bus.Subscribe(e =>
            {
                if (e.Name == Ev.AchievementUnlocked) unlockedEvents.Add(e.Get("achievement"));
            });

            // 建立 R18 成就
            var achR18 = new AchievementDef
            {
                Id = "ach_r18_secret",
                Rating = Rating.R18,
                Trigger = new AchievementTrigger
                {
                    Event = "SpecialEvent",
                    Counter = "count",
                    Target = 1
                }
            };
            m.Achievements[achR18.Id] = achR18;

            // 當前為 G 政策 (禁止 R18)
            var ratingPolicy = new RatingPolicy(Rating.G, Rating.G);
            var clock = new FixedClock();
            var ctx = new ConditionContext
            {
                Player = player,
                Master = m,
                Rating = ratingPolicy,
                Clock = clock
            };
            var tracker = new AchievementTracker(m, ctx, bus, clock);
            tracker.Attach();

            // 觸發達成目標事件
            bus.Publish(new DomainEvent("SpecialEvent"));

            AchievementProgress p = player.Achievements["ach_r18_secret"];
            Assert.AreEqual(1, p.Progress, "分級限制時進度照常累積");
            Assert.IsNull(p.UnlockedAtUtc, "分級不允許時不得標記為解鎖");
            Assert.IsFalse(unlockedEvents.Contains("ach_r18_secret"), "分級不允許時不得發佈解鎖事件");

            // 放寬分級政策為 R18
            ctx.Rating = new RatingPolicy(Rating.R18, Rating.R18);

            // 再次發生事件觸發補上解鎖
            bus.Publish(new DomainEvent("SpecialEvent"));
            Assert.IsNotNull(p.UnlockedAtUtc, "政策放寬後下一次事件應補發解鎖");
            Assert.IsTrue(unlockedEvents.Contains("ach_r18_secret"), "政策放寬後應發佈解鎖事件");
        }

        // =============================================================
        // A8-8：AchievementUnlocked 佇列派送支援鏈式解鎖且無重入異常
        // =============================================================

        [Test]
        public void A8_8_EventBusQueuedDispatch_HandlesNestedAchievementUnlocksWithoutReentrancy()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            EventBus bus = new EventBus();
            var eventHistory = new List<string>();
            bus.Subscribe(e =>
            {
                if (e.Name == Ev.AchievementUnlocked) eventHistory.Add(e.Get("achievement"));
            });

            // 成就 A：單抽 1 次解鎖 (ach_first_pull)
            // 成就 B (Meta)：監聽 AchievementUnlocked，累計解鎖 1 個成就時解鎖
            var achMeta = new AchievementDef
            {
                Id = "ach_meta_first_achievement",
                Rating = Rating.G,
                Trigger = new AchievementTrigger
                {
                    Event = Ev.AchievementUnlocked,
                    Counter = "count",
                    Target = 1
                }
            };
            m.Achievements[achMeta.Id] = achMeta;

            AchievementTracker tracker = CreateTracker(m, player, bus);

            // 觸發成就 A 解鎖，進而透過佇列派送自動連鎖解鎖成就 B
            Assert.DoesNotThrow(() =>
            {
                bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", "banner_std").With("count", 1));
            });

            Assert.IsNotNull(player.Achievements["ach_first_pull"].UnlockedAtUtc);
            Assert.IsNotNull(player.Achievements["ach_meta_first_achievement"].UnlockedAtUtc);

            // 驗證解鎖順序為隊列派送：先 A 後 B
            Assert.AreEqual(2, eventHistory.Count);
            Assert.AreEqual("ach_first_pull", eventHistory[0]);
            Assert.AreEqual("ach_meta_first_achievement", eventHistory[1]);
        }
    }
}
