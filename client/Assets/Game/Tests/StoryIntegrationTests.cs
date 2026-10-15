using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.Encounter;
using Game.Domain.Events;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class StoryIntegrationTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public DateTime LocalNow => UtcNow;

            public FixedClock(DateTime? utc = null)
            {
                UtcNow = DateTime.SpecifyKind(utc ?? new DateTime(2026, 10, 15, 12, 0, 0), DateTimeKind.Utc);
            }
        }

        private MasterDatabase _master;
        private FixedClock _clock;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            _clock = new FixedClock();
        }

        private GameCore CreateCore(RatingPolicy rating = null, PlayerState player = null)
        {
            var p = player ?? GameCore.NewPlayer(_master, _clock);
            var r = rating ?? new RatingPolicy(Rating.R18, Rating.R18);
            return new GameCore(_master, p, new SplitMix64Random(1234), _clock, r);
        }

        // =========================================================================================
        // C6 巧遇和劇情的串接
        // =========================================================================================

        [Test]
        public void C6_1_StartEncounter_StoryNodeCanBePlayedByStoryRunner()
        {
            var core = CreateCore();

            // 啟動公園巧遇
            Result<EncounterStart> startRes = core.StartEncounter("spot_park");
            Assert.IsTrue(startRes.Ok);
            EncounterEventDef encEvent = startRes.Value.Event;
            Assert.IsNotNull(encEvent);
            Assert.IsFalse(string.IsNullOrEmpty(encEvent.Story));

            // 動態模擬一個以此 Story 名稱為節點的劇本
            string scriptText = $@"
[node {encEvent.Story}]
line enc_001 Elysia loc:story.encounter.elysia.001
option loc:story.choice.0 -> ChoiceNode0
option loc:story.choice.1 -> ChoiceNode1

[node ChoiceNode0]
line res_001 Elysia loc:story.res.0
end

[node ChoiceNode1]
line res_002 Elysia loc:story.res.1
end
";
            var runner = new MiniScriptRunner(scriptText);
            runner.Start(encEvent.Story);

            Assert.AreEqual(encEvent.Story, runner.CurrentNodeName);
            runner.Continue(); // 第一行台詞
            Assert.AreEqual("enc_001", runner.CurrentLine.LineId);

            runner.Continue(); // 進入選項群組
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.AreEqual("ChoiceNode0", runner.CurrentOptions[0].TargetNode);
            Assert.AreEqual("ChoiceNode1", runner.CurrentOptions[1].TargetNode);
        }

        [Test]
        public void C6_2_PlayerChoice_MapsToOutcomeKey_AndResolvesEncounter()
        {
            var core = CreateCore();
            Result<EncounterStart> startRes = core.StartEncounter("spot_park");
            Assert.IsTrue(startRes.Ok);
            EncounterEventDef encEvent = startRes.Value.Event;
            Assert.IsNotNull(encEvent);

            string chosenKey = "choice0";
            List<Outcome> expectedOutcomes = encEvent.Outcomes[chosenKey];

            // 紀錄結算前的道具與好感
            long expSBefore = core.Player.ItemCount("exp_s");
            int affBefore = core.Player.Char("char04_elysia")?.Affection ?? 0;

            Result<List<Outcome>> resolveRes = core.ResolveEncounter(chosenKey);

            Assert.IsTrue(resolveRes.Ok);
            Assert.IsNull(core.Player.Encounter.Pending, "結算後 Pending 應清除");
            Assert.IsTrue(core.Player.Encounter.Seen.Contains(encEvent.Id), "結算後應記錄至 Seen");

            // 驗證 outcomes 是否正確套用
            foreach (Outcome o in expectedOutcomes)
            {
                if (o.Item != null)
                {
                    Assert.IsTrue(core.Player.ItemCount(o.Item) > 0);
                }
                if (o.AffectionChar != null)
                {
                    Assert.AreEqual(affBefore + (int)o.N, core.Player.Char(o.AffectionChar).Affection);
                }
            }
        }

        [Test]
        public void C6_3_LeavingMidway_RetainsPending_ReenteringSameSpotResumesWithoutDeductingStamina()
        {
            var core = CreateCore();
            core.Player.Encounter.UnlockedSpots.Add("spot_cafe"); // 解鎖另一個地點供測試跨地點 pending 檢查
            int staminaBefore = core.Player.Encounter.Stamina;

            // 1. 首次進入 spot_park，消耗 1 體力，產生 pending
            Result<EncounterStart> start1 = core.StartEncounter("spot_park");
            Assert.IsTrue(start1.Ok);
            Assert.AreEqual(staminaBefore - 1, core.Player.Encounter.Stamina);
            string pendingId = core.Player.Encounter.Pending;
            Assert.IsNotNull(pendingId);

            // 2. 劇情中途離開（未呼叫 ResolveEncounter），Pending 仍保留
            Assert.AreEqual(pendingId, core.Player.Encounter.Pending);

            // 3. 嘗試進入其他已解鎖地點（spot_cafe）時，因仍有 pending 而被拒絕
            Result<EncounterStart> diffSpot = core.StartEncounter("spot_cafe");
            Assert.IsFalse(diffSpot.Ok);
            Assert.AreEqual("encounter_pending", diffSpot.Error);

            // 4. 再次進入同一個地點 spot_park，成功繼續該事件，且體力不重複扣除！
            int staminaMid = core.Player.Encounter.Stamina;
            Result<EncounterStart> resumeRes = core.StartEncounter("spot_park");

            Assert.IsTrue(resumeRes.Ok);
            Assert.AreEqual(pendingId, resumeRes.Value.Event.Id);
            Assert.AreEqual(staminaMid, core.Player.Encounter.Stamina, "繼續進行中的巧遇不應重複扣除體力");

            // 5. 結算巧遇
            core.ResolveEncounter("choice0");
            Assert.IsNull(core.Player.Encounter.Pending);
        }

        // =========================================================================================
        // C7 角色路線開放規則
        // =========================================================================================

        [Test]
        public void C7_1_RouteChapters_UnlockAccordingToAffectionLevel()
        {
            var core = CreateCore();
            const string charId = "char04_elysia";

            // char04_elysia 開局好感為 0，好感等級為 1
            Assert.IsTrue(core.IsRouteChapterUnlocked(charId, 1), "好感等級 1 應開放第 1 章");
            Assert.IsFalse(core.IsRouteChapterUnlocked(charId, 2), "好感等級 1 時第 2 章應鎖定");
            Assert.IsFalse(core.IsRouteChapterUnlocked(charId, 3), "第 3 章應鎖定");

            // 未持有的角色 (char01_claire) 第 1 章應鎖定
            Assert.IsFalse(core.IsRouteChapterUnlocked("char01_claire", 1), "未持有角色路線全數鎖定");

            // 提升好感到等級 2 所需門檻（例如 100），但尚未完成第 1 章
            core.AddAffection(charId, 100);
            Assert.IsTrue(core.Growth.AffectionLevel(core.Player.Char(charId).Affection) >= 2);
            Assert.IsFalse(core.IsRouteChapterUnlocked(charId, 2), "雖然好感等級達到 2，但前置第 1 章未完成，第 2 章仍應鎖定");
        }

        [Test]
        public void C7_2_CompletingChapter_RecordsProgress_AndUnlocksNextChapterWhenAffectionMet()
        {
            var core = CreateCore();
            const string charId = "char04_elysia";

            // 1. 完成第 1 章前，嘗試直接完成第 2 章應回傳 chapter_locked
            Result failRes = core.CompleteRouteChapter(charId, 2);
            Assert.IsFalse(failRes.Ok);
            Assert.AreEqual("chapter_locked", failRes.Error);

            // 2. 完成第 1 章
            Result c1Res = core.CompleteRouteChapter(charId, 1);
            Assert.IsTrue(c1Res.Ok);
            Assert.IsTrue(core.Player.Story.CompletedChapters.Contains(CharacterRouteService.GetChapterKey(charId, 1)));

            // 3. 好感未滿等級 2 前，第 2 章依然鎖定
            Assert.IsFalse(core.IsRouteChapterUnlocked(charId, 2));

            // 4. 提升好感至等級 2，第 2 章立刻開放！
            core.AddAffection(charId, 100);
            Assert.IsTrue(core.IsRouteChapterUnlocked(charId, 2));

            // 5. 完成第 2 章
            Result c2Res = core.CompleteRouteChapter(charId, 2);
            Assert.IsTrue(c2Res.Ok);
            Assert.IsTrue(core.Player.Story.CompletedChapters.Contains(CharacterRouteService.GetChapterKey(charId, 2)));
        }
    }
}
