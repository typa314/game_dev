using System;
using System.Collections.Generic;
using Game.Core;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 框架煙霧測試：確認範例主資料能載入、通過驗證，核心流程能串起來。
    /// 各模組的詳細單元測試之後再補（預定交由 Gemini 撰寫，由 Claude 執行並核實）。
    /// </summary>
    public class SmokeTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public DateTime LocalNow { get { return UtcNow; } }
        }

        private static GameCore NewCore(out MasterDatabase m)
        {
            m = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new FixedClock { UtcNow = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc) };
            PlayerState p = GameCore.NewPlayer(m, clock);
            return new GameCore(m, p, new SplitMix64Random(42), clock, new RatingPolicy(Rating.G, Rating.G));
        }

        [Test]
        public void SampleMasterData_LoadsAndValidates()
        {
            MasterDatabase m = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            List<string> errors = MasterDataValidator.Validate(m);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.AreEqual(5, m.Characters.Count);
            // 數字型的 value 要能讀進字串欄位（條件 DSL 依賴這一點）
            Assert.AreEqual("30", m.PortraitStages["ps_char01_s2"].Unlock.Of[0].Value);
        }

        [Test]
        public void TenPull_ChargesGemAndGivesTenDraws()
        {
            MasterDatabase m;
            GameCore core = NewCore(out m);
            long gemBefore = core.Player.ItemCount("gem");
            var r = core.PullGacha("banner_std", 10);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual(10, r.Value.Count);
            // 十連會解鎖「首抽」成就，但成就獎勵要手動領取，所以寶石只會減少 1500
            Assert.AreEqual(gemBefore - 1500, core.Player.ItemCount("gem"));
        }

        [Test]
        public void Battle_IsDeterministic_AndSubmitReplaysSameResult()
        {
            MasterDatabase m;
            GameCore core = NewCore(out m);
            BattleSetup setup = core.PrepareBattle("st_1_01", 7).Value;

            // 簡單的自動出兵策略：能出就出
            var sim = new BattleSimulator(setup);
            while (sim.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setup.Team.Count; slot++)
                    if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                sim.Step();
            }
            BattleResult live = sim.Result();

            var report = new BattleReport { StageId = "st_1_01", Seed = 7, Team = new List<string>(core.Player.Team), Commands = sim.Recorded };
            BattleResult replay = BattleSimulator.Run(core.BuildSetup(m.Stages["st_1_01"], report.Team, 7), report.Commands);
            Assert.AreEqual(live.Outcome, replay.Outcome);
            Assert.AreEqual(live.Ticks, replay.Ticks);
            Assert.AreEqual(live.AllyBaseHp, replay.AllyBaseHp);

            var reward = core.SubmitBattle(report);
            Assert.IsTrue(reward.Ok, reward.Error);
            Assert.AreEqual(live.Outcome, reward.Value.Result.Outcome);
            TestContext.WriteLine("st_1_01 auto-deploy: " + live.Outcome + " in " + live.Ticks / 30.0 + "s, stars=" + reward.Value.Stars);
        }
    }
}
