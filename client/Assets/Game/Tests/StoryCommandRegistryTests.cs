using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.Events;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class StoryCommandRegistryTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public DateTime LocalNow => UtcNow;

            public FixedClock(DateTime? utc = null)
            {
                UtcNow = DateTime.SpecifyKind(utc ?? new DateTime(2026, 10, 14, 12, 0, 0), DateTimeKind.Utc);
            }
        }

        private sealed class RecordingStoryPresenter : IStoryPresenter
        {
            public sealed class Invocation
            {
                public string Command { get; }
                public IReadOnlyList<string> Arguments { get; }

                public Invocation(string command, params string[] arguments)
                {
                    Command = command;
                    Arguments = arguments ?? Array.Empty<string>();
                }

                public override string ToString()
                {
                    return $"{Command}({string.Join(", ", Arguments)})";
                }
            }

            public readonly List<Invocation> Invocations = new List<Invocation>();

            public void Bg(string sceneId, string variant = null)
            {
                Invocations.Add(new Invocation("bg", sceneId, variant));
            }

            public void Show(string characterId, IReadOnlyList<string> options = null)
            {
                var list = new List<string> { characterId };
                if (options != null) list.AddRange(options);
                Invocations.Add(new Invocation("show", list.ToArray()));
            }

            public void Hide(string characterId)
            {
                Invocations.Add(new Invocation("hide", characterId));
            }

            public void Expr(string characterId, string expression)
            {
                Invocations.Add(new Invocation("expr", characterId, expression));
            }

            public void Motion(string characterId, string motion)
            {
                Invocations.Add(new Invocation("motion", characterId, motion));
            }

            public void Cg(string cgId)
            {
                Invocations.Add(new Invocation("cg", cgId));
            }

            public void Bgm(string bgmId)
            {
                Invocations.Add(new Invocation("bgm", bgmId));
            }

            public void Se(string seId)
            {
                Invocations.Add(new Invocation("se", seId));
            }

            public void Voice(string voiceId)
            {
                Invocations.Add(new Invocation("voice", voiceId));
            }

            public void Wait(float seconds)
            {
                Invocations.Add(new Invocation("wait", seconds.ToString(CultureInfo.InvariantCulture)));
            }

            public void Fade(IReadOnlyList<string> args = null)
            {
                Invocations.Add(new Invocation("fade", args != null ? new List<string>(args).ToArray() : Array.Empty<string>()));
            }

            public void Shake(IReadOnlyList<string> args = null)
            {
                Invocations.Add(new Invocation("shake", args != null ? new List<string>(args).ToArray() : Array.Empty<string>()));
            }
        }

        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static GameCore CreateCore(MasterDatabase master, PlayerState player = null)
        {
            var clock = new FixedClock();
            player = player ?? GameCore.NewPlayer(master, clock);
            var rating = new RatingPolicy(Rating.R18, Rating.R18);
            return new GameCore(master, player, new SplitMix64Random(1), clock, rating);
        }

        [Test]
        public void C2_SupportedCommands_ContainsAll15Commands()
        {
            var expected = new[]
            {
                "bg", "show", "hide", "expr", "motion", "cg", "bgm", "se", "voice",
                "affection", "give", "ending", "wait", "fade", "shake"
            };

            CollectionAssert.AreEquivalent(expected, StoryCommandRegistry.SupportedCommands);
        }

        [Test]
        public void C2_All15Commands_RegisteredAndCallableByRunner()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            const string script = @"
[node Main]
command bg scene_cafe
command show char01_claire
command hide char01_claire
command expr char01_claire happy
command motion char01_claire wave
command cg cg_test_01
command bgm bgm_main
command se se_click
command voice voice_001
command affection char04_elysia 1
command give gold 10
command ending ending_01
command wait 1
command fade
command shake
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            runner.Start("Main");
            runner.Continue();

            Assert.IsTrue(runner.IsEnded);
            Assert.AreEqual(0, errors.Count, "No errors should occur during valid script run.");
        }

        [Test]
        public void C2_PresentationCommands_CallOrderAndArgumentsPreserved()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            const string script = @"
[node Main]
command bg scene_academy_rooftop sunset
command show char01_claire pose=stand expr=tsundere at=center costume=summer
command expr char01_claire shy
command motion char01_claire bow
command voice v_char01_001
command bgm bgm_theme
command se se_bell
command wait 1.5
command fade out 0.5
command shake intensity=high
command hide char01_claire
command cg cg_char01_t2_01
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            runner.Start("Main");
            runner.Continue();

            Assert.IsTrue(runner.IsEnded);
            Assert.AreEqual(0, errors.Count);

            // 驗證呼叫次序與參數完全相符
            Assert.AreEqual(12, presenter.Invocations.Count);

            // 1. bg
            Assert.AreEqual("bg", presenter.Invocations[0].Command);
            CollectionAssert.AreEqual(new[] { "scene_academy_rooftop", "sunset" }, presenter.Invocations[0].Arguments);

            // 2. show (保留 characterId 與所有後面 tokens 不重新解釋)
            Assert.AreEqual("show", presenter.Invocations[1].Command);
            CollectionAssert.AreEqual(
                new[] { "char01_claire", "pose=stand", "expr=tsundere", "at=center", "costume=summer" },
                presenter.Invocations[1].Arguments);

            // 3. expr
            Assert.AreEqual("expr", presenter.Invocations[2].Command);
            CollectionAssert.AreEqual(new[] { "char01_claire", "shy" }, presenter.Invocations[2].Arguments);

            // 4. motion
            Assert.AreEqual("motion", presenter.Invocations[3].Command);
            CollectionAssert.AreEqual(new[] { "char01_claire", "bow" }, presenter.Invocations[3].Arguments);

            // 5. voice
            Assert.AreEqual("voice", presenter.Invocations[4].Command);
            CollectionAssert.AreEqual(new[] { "v_char01_001" }, presenter.Invocations[4].Arguments);

            // 6. bgm
            Assert.AreEqual("bgm", presenter.Invocations[5].Command);
            CollectionAssert.AreEqual(new[] { "bgm_theme" }, presenter.Invocations[5].Arguments);

            // 7. se
            Assert.AreEqual("se", presenter.Invocations[6].Command);
            CollectionAssert.AreEqual(new[] { "se_bell" }, presenter.Invocations[6].Arguments);

            // 8. wait
            Assert.AreEqual("wait", presenter.Invocations[7].Command);
            CollectionAssert.AreEqual(new[] { "1.5" }, presenter.Invocations[7].Arguments);

            // 9. fade
            Assert.AreEqual("fade", presenter.Invocations[8].Command);
            CollectionAssert.AreEqual(new[] { "out", "0.5" }, presenter.Invocations[8].Arguments);

            // 10. shake
            Assert.AreEqual("shake", presenter.Invocations[9].Command);
            CollectionAssert.AreEqual(new[] { "intensity=high" }, presenter.Invocations[9].Arguments);

            // 11. hide
            Assert.AreEqual("hide", presenter.Invocations[10].Command);
            CollectionAssert.AreEqual(new[] { "char01_claire" }, presenter.Invocations[10].Arguments);

            // 12. cg (同時在核心解鎖並呼叫呈現層)
            Assert.AreEqual("cg", presenter.Invocations[11].Command);
            CollectionAssert.AreEqual(new[] { "cg_char01_t2_01" }, presenter.Invocations[11].Arguments);
            Assert.IsTrue(core.Player.Gallery.Contains("cg_char01_t2_01"));
        }

        [Test]
        public void C2_StateCommand_Affection_UpdatesState_AndPublishesDomainEvent()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();

            var events = new List<DomainEvent>();
            core.Bus.Subscribe(events.Add);

            int initialAffection = core.Player.Char("char04_elysia").Affection;

            const string script = @"
[node Main]
command affection char04_elysia 25
command affection char04_elysia -10
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(initialAffection + 15, core.Player.Char("char04_elysia").Affection);

            var affectionEvents = events.FindAll(e => e.Name == Ev.AffectionChanged);
            Assert.AreEqual(2, affectionEvents.Count);
            Assert.AreEqual("char04_elysia", affectionEvents[0].Get("char"));
            Assert.AreEqual((initialAffection + 25).ToString(), affectionEvents[0].Get("affection"));
            Assert.AreEqual("25", affectionEvents[0].Get("delta"));

            Assert.AreEqual("char04_elysia", affectionEvents[1].Get("char"));
            Assert.AreEqual((initialAffection + 15).ToString(), affectionEvents[1].Get("affection"));
            Assert.AreEqual("-10", affectionEvents[1].Get("delta"));

            // 呈現層不應被呼叫
            Assert.AreEqual(0, presenter.Invocations.Count);
        }

        [Test]
        public void C2_StateCommand_Give_AddsItem_SetsDirty_AndPublishesDomainEvent()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();

            var events = new List<DomainEvent>();
            core.Bus.Subscribe(events.Add);

            core.Dirty = false;
            long initialGold = core.Player.ItemCount("gold");

            const string script = @"
[node Main]
command give gold 5000
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(initialGold + 5000, core.Player.ItemCount("gold"));
            Assert.IsTrue(core.Dirty);

            var giveEvents = events.FindAll(e => e.Name == Ev.ItemGiven);
            Assert.AreEqual(1, giveEvents.Count);
            Assert.AreEqual("gold", giveEvents[0].Get("item"));
            Assert.AreEqual("5000", giveEvents[0].Get("n"));

            Assert.AreEqual(0, presenter.Invocations.Count);
        }

        [Test]
        public void C2_StateCommand_Give_RejectsInventoryOverflowWithoutMutation()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();
            core.Player.Items["overflow_item"] = long.MaxValue;
            core.Dirty = false;
            var events = new List<DomainEvent>();
            core.Bus.Subscribe(events.Add);
            var runner = new MiniScriptRunner("[node Main]\ncommand give overflow_item 1\nend");
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(long.MaxValue, core.Player.ItemCount("overflow_item"));
            Assert.IsFalse(core.Dirty);
            Assert.AreEqual(0, events.FindAll(e => e.Name == Ev.ItemGiven).Count);
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("give", errors[0]);
            StringAssert.Contains("item_amount_overflow", errors[0]);
        }

        [Test]
        public void C2_StateCommand_Ending_RecordsEnding_SetsDirty_AndPublishesDomainEvent()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();

            var events = new List<DomainEvent>();
            core.Bus.Subscribe(events.Add);

            core.Dirty = false;

            const string script = @"
[node Main]
command ending route_claire.ending_good
command ending route_claire.ending_good
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("Main");
            runner.Continue();

            Assert.IsTrue(core.Player.Endings.ContainsKey("route_claire.ending_good"));
            Assert.IsTrue(core.Dirty);

            // 重複觸發相同結局不重複發佈事件
            var endingEvents = events.FindAll(e => e.Name == Ev.EndingReached);
            Assert.AreEqual(1, endingEvents.Count);
            Assert.AreEqual("route_claire.ending_good", endingEvents[0].Get("ending"));

            Assert.AreEqual(0, presenter.Invocations.Count);
        }

        [Test]
        public void C2_StateCommand_Cg_UnlocksGallery_SetsDirty_PublishesEvent_AndCallsPresenter()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();

            var events = new List<DomainEvent>();
            core.Bus.Subscribe(events.Add);

            core.Dirty = false;

            const string script = @"
[node Main]
command cg cg_char01_01
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("Main");
            runner.Continue();

            Assert.IsTrue(core.Player.Gallery.Contains("cg_char01_01"));
            Assert.IsTrue(core.Dirty);

            var cgEvents = events.FindAll(e => e.Name == Ev.CgUnlocked);
            Assert.AreEqual(1, cgEvents.Count);
            Assert.AreEqual("cg_char01_01", cgEvents[0].Get("cg"));

            // 呈現層有收到 CG 演出通知
            Assert.AreEqual(1, presenter.Invocations.Count);
            Assert.AreEqual("cg", presenter.Invocations[0].Command);
            CollectionAssert.AreEqual(new[] { "cg_char01_01" }, presenter.Invocations[0].Arguments);
        }

        [Test]
        public void C2_MiniScriptRunner_UnknownCommand_ThrowsWithLineNumberAndCommandName()
        {
            const string script = @"
[node Main]
line l1 Claire story.l1
command unknown_magic_spell foo bar
end
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");
            runner.Continue(); // line l1

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Continue());
            Assert.AreEqual("Line 4: unknown story command 'unknown_magic_spell'.", ex.Message);
        }

        [Test]
        public void C2_ErrorHandling_Affection_UnknownOrUnownedCharacter_ReportsErrorWithoutThrowingOrMutation()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            const string script = @"
[node Main]
command affection char99_nonexistent 10
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("affection", errors[0]);
            StringAssert.Contains("character_not_owned", errors[0]);
            Assert.AreEqual(0, presenter.Invocations.Count);
        }

        [Test]
        public void C2_ErrorHandling_Affection_InvalidNumeric_ReportsErrorWithoutThrowing()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            int initial = core.Player.Char("char04_elysia").Affection;

            const string script = @"
[node Main]
command affection char04_elysia not_a_number
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("affection", errors[0]);
            Assert.AreEqual(initial, core.Player.Char("char04_elysia").Affection);
        }

        [Test]
        public void C2_ErrorHandling_Give_InvalidAmountOrBlankId_ReportsErrorWithoutThrowingOrMutation()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            long initialGold = core.Player.ItemCount("gold");

            const string script = @"
[node Main]
command give """" 100
command give gold 0
command give gold -50
command give gold invalid_number
command give gold
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.AreEqual(5, errors.Count);
            foreach (var err in errors)
            {
                StringAssert.Contains("give", err);
            }
            Assert.AreEqual(initialGold, core.Player.ItemCount("gold"));
        }

        [Test]
        public void C2_ErrorHandling_Ending_BlankIdOrInvalidCount_ReportsError()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            int initialEndings = core.Player.Endings.Count;

            const string script = @"
[node Main]
command ending """"
command ending
command ending too many arguments
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.AreEqual(3, errors.Count);
            foreach (var err in errors)
            {
                StringAssert.Contains("ending", err);
            }
            Assert.AreEqual(initialEndings, core.Player.Endings.Count);
        }

        [Test]
        public void C2_ErrorHandling_Cg_BlankIdOrInvalidCount_ReportsErrorAndDoesNotCallPresenter()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            int initialGallery = core.Player.Gallery.Count;

            const string script = @"
[node Main]
command cg """"
command cg
command cg cg_one cg_two
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.AreEqual(3, errors.Count);
            foreach (var err in errors)
            {
                StringAssert.Contains("cg", err);
            }
            Assert.AreEqual(initialGallery, core.Player.Gallery.Count);
            Assert.AreEqual(0, presenter.Invocations.Count);
        }

        [Test]
        public void C2_ErrorHandling_PresentationCommands_InvalidArgs_ReportsErrorAndDoesNotCallPresenter()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();
            var errors = new List<string>();

            const string script = @"
[node Main]
command bg
command bg scene_1 extra1 extra2
command show
command hide
command hide char1 char2
command expr char1
command expr char1 expr1 extra
command motion char1
command motion char1 motion1 extra
command wait
command wait not_a_number
command wait -1.0
command wait NaN
command wait Infinity
command bgm
command bgm bgm1 bgm2
command se
command se se1 se2
command voice
command voice v1 v2
end
";
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter, errors.Add);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.AreEqual(20, errors.Count);
            Assert.AreEqual(0, presenter.Invocations.Count);
        }

        [Test]
        public void C2_NullErrorCallback_InvalidCommandArgs_DoesNotThrow()
        {
            var master = LoadCleanMaster();
            var core = CreateCore(master);
            var presenter = new RecordingStoryPresenter();

            const string script = @"
[node Main]
command affection char01_claire not_a_num
command give gold -100
command bg
end
";
            var runner = new MiniScriptRunner(script);
            // onError 傳 null
            StoryCommandRegistry.Register(runner, core, presenter, null);

            Assert.DoesNotThrow(() =>
            {
                runner.Start("Main");
                runner.Continue();
            });

            Assert.IsTrue(runner.IsEnded);
        }
    }
}
