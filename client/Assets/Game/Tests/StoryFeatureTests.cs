using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Gallery;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.Achievements;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class StoryFeatureTests
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

        private sealed class DummyStoryPresenter : IStoryPresenter
        {
            public int CgCalls { get; private set; }
            public void Bg(string sceneId, string variant = null) { }
            public void Show(string characterId, IReadOnlyList<string> options = null) { }
            public void Hide(string characterId) { }
            public void Expr(string characterId, string expression) { }
            public void Motion(string characterId, string motion) { }
            public void Cg(string cgId) { CgCalls++; }
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

        private GameCore CreateCore(RatingPolicy rating = null, PlayerState player = null)
        {
            var p = player ?? GameCore.NewPlayer(_master, _clock);
            var r = rating ?? new RatingPolicy(Rating.R18, Rating.R18);
            return new GameCore(_master, p, new SplitMix64Random(1234), _clock, r);
        }

        // -----------------------------------------------------------------------------------------
        // C4-1: 讀過的行/節點加入 ReadNodes/ReadLines；快進可以選擇「只快進已讀」
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C4_1_ReadNodesAndLines_Tracked_FastForward_StopsAtUnreadWhenConfigured()
        {
            const string scriptText = @"
[node Start]
line l1 Claire first_line
line l2 Claire second_line
jump NextNode

[node NextNode]
line l3 Claire third_line
end
";
            var core = CreateCore();
            var runner = new MiniScriptRunner(scriptText);
            var controller = new StoryPlaybackController(runner, core);

            // 預設 SkipUnread 為 false（只快進已讀）
            core.Player.Settings.SkipUnread = false;

            runner.Start("Start");
            controller.Advance(); // 造訪 Start, l1

            Assert.IsTrue(core.Player.Story.ReadNodes.Contains("Start"));
            Assert.IsTrue(core.Player.Story.ReadLines.Contains("l1"));
            Assert.IsFalse(core.Player.Story.ReadLines.Contains("l2"));

            // 推進至 l2，l2 尚未讀過，因此 CanSkipCurrent 應為 false
            runner.Continue();
            Assert.AreEqual("l2", runner.CurrentLine.LineId);
            Assert.IsFalse(controller.CanSkipCurrent(), "l2 未曾標記為已讀，在 SkipUnread=false 時不可快進跳過");

            // 允許快進全部時 (SkipUnread = true)
            core.Player.Settings.SkipUnread = true;
            Assert.IsTrue(controller.CanSkipCurrent(), "SkipUnread=true 時允許快進未讀");

            // 執行快進推進到底
            int steps = controller.FastForward();
            Assert.IsTrue(steps > 0);
            Assert.IsTrue(runner.IsEnded);
        }

        // -----------------------------------------------------------------------------------------
        // C4-2: <<ending>> 寫入 Profile 一次，觸發 EndingReached 和成就
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C4_2_EndingCommand_RecordsToProfile_AndTriggersAchievement()
        {
            var core = CreateCore();

            const string scriptText = @"
[node Start]
command ending ending_elysia_good
line l1 Claire ended
end
";
            var runner = new MiniScriptRunner(scriptText);
            var presenter = new DummyStoryPresenter();
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("Start");
            runner.Continue(); // 執行 command ending 與 line l1

            // 驗證結局寫入 Profile
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_elysia_good"));
            Assert.IsFalse(string.IsNullOrEmpty(core.Player.Endings["ending_elysia_good"]));

            // 驗證 ach_ending_any 成就被自動解鎖
            Assert.IsTrue(core.Player.Achievements.ContainsKey("ach_ending_any"));
            Assert.IsNotNull(core.Player.Achievements["ach_ending_any"].UnlockedAtUtc);

            // 重複觸發不會報錯且不會覆蓋達成時間
            string recordedTime = core.Player.Endings["ending_elysia_good"];
            _clock.UtcNow = _clock.UtcNow.AddHours(1);
            core.ReachEnding("ending_elysia_good");
            Assert.AreEqual(recordedTime, core.Player.Endings["ending_elysia_good"]);
        }

        [Test]
        public void C4_2_NullOrWhitespaceInputs_SafelyIgnored_WithoutException()
        {
            var core = CreateCore();
            int endingCount = core.Player.Endings.Count;
            int galleryCount = core.Player.Gallery.Count;
            int flagCount = core.Player.Story.Flags.Count;

            Assert.DoesNotThrow(() => core.ReachEnding(null));
            Assert.DoesNotThrow(() => core.ReachEnding("   "));
            Assert.DoesNotThrow(() => core.UnlockCg(null));
            Assert.DoesNotThrow(() => core.UnlockCg("   "));
            Assert.DoesNotThrow(() => core.SetGlobalFlag(null, 10));
            Assert.DoesNotThrow(() => core.SetGlobalFlag("   ", 10));

            Assert.AreEqual(endingCount, core.Player.Endings.Count);
            Assert.AreEqual(galleryCount, core.Player.Gallery.Count);
            Assert.AreEqual(flagCount, core.Player.Story.Flags.Count);
        }

        [Test]
        public void C4_5_NewPlayer_UnlocksStarterCharacterCg()
        {
            var core = CreateCore();
            foreach (var ch in core.Master.Config.Initial.Characters)
            {
                var cs = core.Player.Char(ch);
                Assert.IsNotNull(cs, $"初始角色 {ch} 應存在");
                string expectedCg = $"cg_{ch}_{cs.CurrentRarity.ToLower()}";
                Assert.IsTrue(core.Player.Gallery.Contains(expectedCg), $"新玩家應已解鎖初始角色 {ch} 之基礎 CG {expectedCg}");
            }
        }

        // -----------------------------------------------------------------------------------------
        // C4-3: 回憶模式：重播章節時使用暫時的變數副本，結束後 Profile 和所有存檔都沒有改變
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C4_3_RecallMode_LeavesProfileAndSaveCompletelyUnchanged()
        {
            var core = CreateCore();
            string profileBeforeJson = JsonConvert.SerializeObject(core.Player);
            bool dirtyBefore = core.Dirty;

            const string scriptText = @"
[node Start]
set local_trust = 99
command affection char04_elysia 50
command give gold 1000
command ending ending_recall_test
command cg cg_recall_test
line l1 Claire recall_text
end
";
            var runner = new MiniScriptRunner(scriptText);
            var presenter = new DummyStoryPresenter();

            // 啟用回憶模式 (isRecallMode: true)
            StoryCommandRegistry.Register(runner, core, presenter, isRecallMode: true);

            runner.Start("Start");
            runner.Continue(); // 執行腳本全部指令與台詞

            // 演出指令依舊呼叫 Presenter
            Assert.AreEqual(1, presenter.CgCalls);

            // 狀態完全不被修改
            string profileAfterJson = JsonConvert.SerializeObject(core.Player);
            Assert.AreEqual(profileBeforeJson, profileAfterJson, "回憶模式結束後，Profile 內容必須與執行前 100% 相同");
            Assert.AreEqual(dirtyBefore, core.Dirty, "回憶模式不得將 core 標記為 Dirty");

            // 臨時變數存在於 runner 內部副本，不影響 Profile
            Assert.AreEqual(99, runner.Variables["local_trust"]);
        }

        // -----------------------------------------------------------------------------------------
        // C4-4: CG 圖鑑依角色分組，分級不允許的 CG 不會列出
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C4_4_GalleryService_GroupsByCharacter_FiltersOutDisallowedRating()
        {
            var cgs = new List<CgDef>
            {
                new CgDef("cg_claire_01", "char01_claire", Rating.G, "Claire Morning"),
                new CgDef("cg_claire_r18_01", "char01_claire", Rating.R18, "Claire Secret"),
                new CgDef("cg_elysia_01", "char04_elysia", Rating.G, "Elysia Battle")
            };

            var gallery = new GalleryService(cgs);
            var player = GameCore.NewPlayer(_master, _clock);
            player.Gallery.Clear();
            player.Gallery.Add("cg_claire_01"); // Claire G 已解鎖

            // 1. 在 G 政策下檢視圖鑑
            var policyG = new RatingPolicy(Rating.R18, Rating.G); // Effective == G
            var groupedG = gallery.GetGroupedGallery(player, policyG);

            Assert.IsTrue(groupedG.ContainsKey("char01_claire"));
            Assert.AreEqual(1, groupedG["char01_claire"].Count, "G 政策下 Claire 只有 1 張 G 級 CG，R18 CG 絕對不列出");
            Assert.AreEqual("cg_claire_01", groupedG["char01_claire"][0].Def.Id);
            Assert.IsTrue(groupedG["char01_claire"][0].IsUnlocked);

            Assert.IsTrue(groupedG.ContainsKey("char04_elysia"));
            Assert.AreEqual(1, groupedG["char04_elysia"].Count);
            Assert.IsFalse(groupedG["char04_elysia"][0].IsUnlocked, "未解鎖的 G 級 CG 仍正常列出並標註未解鎖");

            // 2. 在 R18 政策下檢視圖鑑
            var policyR18 = new RatingPolicy(Rating.R18, Rating.R18); // Effective == R18
            var groupedR18 = gallery.GetGroupedGallery(player, policyR18);

            Assert.AreEqual(2, groupedR18["char01_claire"].Count, "R18 政策下 Claire 列出 2 張 CG");
            Assert.IsTrue(groupedR18["char01_claire"].Exists(c => c.Def.Id == "cg_claire_r18_01" && !c.IsUnlocked));
        }
    }
}
