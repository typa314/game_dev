using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class StoryRatingFallbackTests
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

        private sealed class MockStoryPresenter : IStoryPresenter
        {
            public int CgCalls { get; private set; }
            public string LastCgId { get; private set; }

            public void Bg(string sceneId, string variant = null) { }
            public void Show(string characterId, IReadOnlyList<string> options = null) { }
            public void Hide(string characterId) { }
            public void Expr(string characterId, string expression) { }
            public void Motion(string characterId, string motion) { }
            public void Cg(string cgId)
            {
                CgCalls++;
                LastCgId = cgId;
            }
            public void Bgm(string bgmId) { }
            public void Se(string seId) { }
            public void Voice(string voiceId) { }
            public void Wait(float seconds) { }
            public void Fade(IReadOnlyList<string> args = null) { }
            public void Shake(IReadOnlyList<string> args = null) { }
        }

        private MasterDatabase _master;
        private FixedClock _clock;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            _clock = new FixedClock();
        }

        private GameCore CreateCore(RatingPolicy ratingPolicy, PlayerState player = null)
        {
            var p = player ?? GameCore.NewPlayer(_master, _clock);
            return new GameCore(_master, p, new SplitMix64Random(1234), _clock, ratingPolicy);
        }

        // -----------------------------------------------------------------------------------------
        // C3-1: G 政策下進入 rating:R18 節點時，自動改走 fallback: 指定的節點
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C3_1_UnderGPolicy_EncounteringR18Node_FollowsFallbackNode()
        {
            const string scriptText = @"
[node Start]
line l1 Claire start_text
jump IntimateNode

[node IntimateNode rating:R18 fallback:MildNode]
line l2 Claire r18_intimate_text
end

[node MildNode]
line l3 Claire g_mild_text
end
";
            var policy = new RatingPolicy(Rating.R18, Rating.G); // Effective == G
            var runner = new MiniScriptRunner(scriptText, policy);

            runner.Start("Start");
            runner.Continue(); // l1
            Assert.AreEqual("l1", runner.CurrentLine.LineId);

            runner.Continue(); // jump IntimateNode -> fallback to MildNode -> l3
            Assert.AreEqual("l3", runner.CurrentLine.LineId);
            Assert.AreEqual("g_mild_text", runner.CurrentLine.TextKey);
            Assert.IsFalse(runner.IsEnded);

            runner.Continue();
            Assert.IsTrue(runner.IsEnded);
        }

        // -----------------------------------------------------------------------------------------
        // C3-2: R18 政策下正常進入該節點
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C3_2_UnderR18Policy_EncounteringR18Node_EntersDirectly()
        {
            const string scriptText = @"
[node Start]
line l1 Claire start_text
jump IntimateNode

[node IntimateNode rating:R18 fallback:MildNode]
line l2 Claire r18_intimate_text
end

[node MildNode]
line l3 Claire g_mild_text
end
";
            var policy = new RatingPolicy(Rating.R18, Rating.R18); // Effective == R18
            var runner = new MiniScriptRunner(scriptText, policy);

            runner.Start("Start");
            runner.Continue(); // l1
            Assert.AreEqual("l1", runner.CurrentLine.LineId);

            runner.Continue(); // jump IntimateNode -> allowed -> l2
            Assert.AreEqual("l2", runner.CurrentLine.LineId);
            Assert.AreEqual("r18_intimate_text", runner.CurrentLine.TextKey);
        }

        // -----------------------------------------------------------------------------------------
        // C3-3: <<cg>> 引用的 CG 分級不允許時，跳過 CG，劇情繼續
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C3_3_UnderGPolicy_R18Cg_IsSkipped_StoryContinues()
        {
            const string scriptText = @"
[node Start]
command cg cg_char01_r18
line l1 Claire text_after_cg
end
";
            var policyG = new RatingPolicy(Rating.R18, Rating.G); // Effective == G
            var coreG = CreateCore(policyG);
            var presenter = new MockStoryPresenter();
            var runner = new MiniScriptRunner(scriptText, policyG);
            StoryCommandRegistry.Register(runner, coreG, presenter);

            runner.Start("Start");
            runner.Continue(); // executes <<cg cg_char01_r18>> and advances to line l1

            // CG 指令應跳過：未解鎖至 Gallery、未呼叫 presenter
            Assert.IsFalse(coreG.Player.Gallery.Contains("cg_char01_r18"));
            Assert.AreEqual(0, presenter.CgCalls);

            // 劇情順暢推進
            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("l1", runner.CurrentLine.LineId);
            Assert.AreEqual("text_after_cg", runner.CurrentLine.TextKey);
        }

        [Test]
        public void C3_3_UnderR18Policy_R18Cg_IsUnlockedAndPresented()
        {
            const string scriptText = @"
[node Start]
command cg cg_char01_r18
line l1 Claire text_after_cg
end
";
            var policyR18 = new RatingPolicy(Rating.R18, Rating.R18); // Effective == R18
            var coreR18 = CreateCore(policyR18);
            var presenter = new MockStoryPresenter();
            var runner = new MiniScriptRunner(scriptText, policyR18);
            StoryCommandRegistry.Register(runner, coreR18, presenter);

            runner.Start("Start");
            runner.Continue(); // executes <<cg>> and line l1

            // 正常解鎖至 Gallery 並演出
            Assert.IsTrue(coreR18.Player.Gallery.Contains("cg_char01_r18"));
            Assert.AreEqual(1, presenter.CgCalls);
            Assert.AreEqual("cg_char01_r18", presenter.LastCgId);
            Assert.AreEqual("l1", runner.CurrentLine.LineId);
        }

        // -----------------------------------------------------------------------------------------
        // C3-4: R18 節點沒有 fallback 時，被劇本驗證器（C5）擋下
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C3_4_R18NodeWithoutFallback_IsCaughtByStoryValidator()
        {
            const string scriptText = @"
[node Start]
line l1 Claire start
jump AdultNode

[node AdultNode rating:R18]
line l2 Claire no_fallback
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.R18MissingFallback));
        }

        // -----------------------------------------------------------------------------------------
        // 回歸測試：條件剝除外層括號求值、賦值運算支援函式呼叫、零受傷通關星數
        // -----------------------------------------------------------------------------------------
        [Test]
        public void Regression_ConditionEvaluator_StripsOuterParentheses()
        {
            const string scriptText = @"
[node Start]
if ((affection(""char04_elysia"") >= 0)) -> TargetNode
line l1 Claire failed
end

[node TargetNode]
line l2 Claire passed
end
";
            var core = CreateCore(new RatingPolicy(Rating.R18, Rating.R18));
            var runner = new MiniScriptRunner(scriptText);
            runner.RegisterFunction("affection", args => (long)(core.Player.Char((string)args[0])?.Affection ?? 0));

            runner.Start("Start");
            runner.Continue();

            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("l2", runner.CurrentLine.LineId);
            Assert.AreEqual("passed", runner.CurrentLine.TextKey);
        }

        [Test]
        public void Regression_MiniScript_SetExpression_SupportsGameStateFunctionCalls()
        {
            const string scriptText = @"
[node Start]
set trust = affection(""char04_elysia"") + 5
if trust == 5 -> SuccessNode
line l1 Claire failed
end

[node SuccessNode]
line l2 Claire passed
end
";
            var core = CreateCore(new RatingPolicy(Rating.R18, Rating.R18));
            var runner = new MiniScriptRunner(scriptText);
            runner.RegisterFunction("affection", args => (long)(core.Player.Char((string)args[0])?.Affection ?? 0));

            runner.Start("Start");
            runner.Continue();

            Assert.AreEqual(5, runner.Variables["trust"]);
            Assert.AreEqual("l2", runner.CurrentLine.LineId);
        }

        [Test]
        public void Regression_ZeroDamage_BattleStars_Awards3Stars()
        {
            var core = CreateCore(new RatingPolicy(Rating.R18, Rating.R18));
            var stage = _master.Stages["st_1_01"];

            // 陽性測試：沒有受傷（AllyBaseHp == AllyBaseMaxHp, 即 lost == 0）
            var battleResult = new BattleResult
            {
                Outcome = BattleOutcome.Win,
                Ticks = 10 * _master.Config.Battle.TicksPerSecond,
                AllyBaseHp = 1000,
                AllyBaseMaxHp = 1000,
                EnemiesDefeated = 5,
                Deploys = 2
            };

            int stars = core.CountStars(stage, battleResult);
            Assert.AreEqual(3, stars, "0 lost base HP should meet lost <= 20% max HP threshold and award 3 stars");
        }
    }
}
