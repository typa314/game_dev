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
    /// <summary>規則：角色只能從轉蛋取得，巧遇不招募角色（使用者指示 2026-10-14）。</summary>
    public class EncounterNoRecruitTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get { return new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc); } }
            public DateTime LocalNow { get { return UtcNow; } }
        }

        [Test]
        public void Validator_RejectsEncounterAsObtainSource()
        {
            MasterDatabase m = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            m.Characters["char01_claire"].ObtainableBy.Add("encounter");
            List<string> errors = MasterDataValidator.Validate(m);
            Assert.That(errors, Has.Some.Contains("char01_claire").And.Contains("obtainableBy 'encounter'"));
        }

        [Test]
        public void Candidates_ExcludeEventsOfUnownedCharacters()
        {
            MasterDatabase m = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new FixedClock();
            PlayerState p = GameCore.NewPlayer(m, clock);
            var ctx = new ConditionContext { Player = p, Master = m, Clock = clock, Rating = new RatingPolicy(Rating.G, Rating.G) };

            // 移除事件本身的 char_owned 條件，確認排除是引擎層的規則，而不是靠資料條件
            m.Encounters["enc_char01_claire_cafe"].Conditions = null;
            Assert.IsFalse(EncounterEngine.Candidates("spot_cafe", ctx).Any(e => e.Id == "enc_char01_claire_cafe"));

            p.Characters["char01_claire"] = new CharacterState();
            Assert.IsTrue(EncounterEngine.Candidates("spot_cafe", ctx).Any(e => e.Id == "enc_char01_claire_cafe"));
        }

        [Test]
        public void ResolvingEncounter_NeverAddsCharacters()
        {
            MasterDatabase m = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new FixedClock();
            PlayerState p = GameCore.NewPlayer(m, clock);
            var core = new GameCore(m, p, new SplitMix64Random(1), clock, new RatingPolicy(Rating.G, Rating.G));
            int before = p.Characters.Count;
            for (int i = 0; i < 5; i++)
            {
                var start = core.StartEncounter("spot_park");
                Assert.IsTrue(start.Ok, start.Error);
                if (start.Value.Event != null) Assert.IsTrue(core.ResolveEncounter("choice0").Ok);
            }
            Assert.AreEqual(before, p.Characters.Count);
        }
    }
}
