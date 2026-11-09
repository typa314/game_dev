using System;
using System.Collections.Generic;
using System.IO;
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
            Assert.AreEqual(12, m.Characters.Count);
            Assert.AreEqual("route_char04_ch1.txt", Game.Core.Story.RouteScriptNames.FileName("char04_elysia", 1, "route_char04"));
            Assert.AreEqual("route_char18_ch1.txt", Game.Core.Story.RouteScriptNames.FileName("char18_sakura_miko", 1, null));
            Assert.AreNotEqual("route_char01_ch1.txt", Game.Core.Story.RouteScriptNames.FileName("char07_kagura", 1, "route_char07"));
            // 數字型的 value 要能讀進字串欄位（條件 DSL 依賴這一點）
            Assert.AreEqual("30", m.PortraitStages["ps_char01_s2"].Unlock.Of[0].Value);
            Assert.IsTrue(m.Characters.ContainsKey("char07_kagura"));
            Assert.IsTrue(m.Characters.ContainsKey("char09_chloe"));
            Assert.IsTrue(m.Characters.ContainsKey("char10_mirai"));
            Assert.AreEqual("cos_char05_default", m.Characters["char05_rin"].DefaultCostume);
            Assert.AreEqual("route_char10", m.Characters["char10_mirai"].StoryRoute);
            CollectionAssert.IsSubsetOf(
                new[] { "char07_kagura", "char09_chloe", "char10_mirai" },
                m.Banners["banner_std"].Pool["R"]);
            Assert.IsFalse(m.Banners.ContainsKey("banner_newcomer"));
        }

        [Test]
        public void M3_1_FirstTierChapter1Scripts_AffectionTargetsThatCharacter()
        {
            string storyDir = Path.Combine(TestPaths.DataDir(), "story");
            string[] ids =
            {
                "char02_luna", "char03_scarlet", "char04_elysia", "char05_rin", "char06_seraphina",
                "char07_kagura", "char08_yuki", "char09_chloe", "char10_mirai"
            };
            foreach (string id in ids)
            {
                string prefix = id.Substring(0, id.IndexOf('_'));
                string path = Path.Combine(storyDir, "route_" + prefix + "_ch1.txt");
                Assert.IsTrue(File.Exists(path), path);
                string text = File.ReadAllText(path);
                StringAssert.Contains("command affection " + id + " ", text);
                StringAssert.DoesNotContain("char01_claire", text);
                StringAssert.DoesNotContain("sailor", text);
            }
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
