using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Persistence;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class DualLayerSaveTests
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

        private sealed class DummyPresenter : IStoryPresenter
        {
            public void Bg(string sceneId, string variant = null) { }
            public void Show(string characterId, IReadOnlyList<string> options = null) { }
            public void Hide(string characterId) { }
            public void Expr(string characterId, string expression) { }
            public void Motion(string characterId, string motion) { }
            public void Cg(string cgId) { }
            public void Bgm(string bgmId) { }
            public void Se(string seId) { }
            public void Voice(string voiceId) { }
            public void Wait(float seconds) { }
            public void Fade(IReadOnlyList<string> args = null) { }
            public void Shake(IReadOnlyList<string> args = null) { }
        }

        private MasterDatabase _master;
        private FixedClock _clock;
        private string _tempSaveDir;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            _clock = new FixedClock();
            _tempSaveDir = Path.Combine(Path.GetTempPath(), "DualLayerSaveTest_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempSaveDir))
            {
                try { Directory.Delete(_tempSaveDir, true); } catch { }
            }
        }

        private GameCore CreateCore(RatingPolicy rating = null, PlayerState player = null)
        {
            var p = player ?? GameCore.NewPlayer(_master, _clock);
            var r = rating ?? new RatingPolicy(Rating.R18, Rating.R18);
            return new GameCore(_master, p, new SplitMix64Random(1234), _clock, r);
        }

        // -----------------------------------------------------------------------------------------
        // B5-1: 在劇情存檔欄位 X 之後轉蛋、升級，再讀回 X：Profile 都沒有倒退，只有劇情變數回到 X
        // -----------------------------------------------------------------------------------------
        [Test]
        public void B5_1_RestoreStorySlot_DoesNotRevertProfileProgress()
        {
            var core = CreateCore();
            var saveService = new StorySaveService(_tempSaveDir, _clock);

            const string scriptText = @"
[node Start]
set route_trust = 10
line l1 Claire start_node
jump BranchNode

[node BranchNode]
set route_trust = 20
line l2 Claire branch_node
end
";
            var script = MiniScript.Parse(scriptText);
            var runner = new MiniScriptRunner(script);
            runner.Start("Start");
            runner.Continue(); // 執行 set route_trust = 10 與 l1

            Assert.AreEqual(10, Convert.ToInt32(runner.Variables["route_trust"]));

            // 1. 在此處儲存劇情存檔至 slot_001
            saveService.Save("slot_001", runner, chapterStartNode: "Start");

            // 2. 玩家在劇情存檔之後進行核心命令：轉蛋與角色升級
            // 確保金幣與道具足夠升級 char04_elysia
            core.Player.AddItem("gold", 50000);
            core.Player.AddItem("exp_s", 100);
            int levelBefore = core.Player.Char("char04_elysia").Level;
            var mats = new List<ItemStack> { new ItemStack { Item = "exp_s", N = 10 } };
            Result<int> lvlRes = core.LevelUp("char04_elysia", mats);
            Assert.IsTrue(lvlRes.Ok);
            int levelAfter = core.Player.Char("char04_elysia").Level;
            Assert.IsTrue(levelAfter > levelBefore, "角色成功升級");

            // 抽卡
            core.Player.AddItem("gem", 3000);
            int gachaHistoryCountBefore = core.Player.Gacha.History.Count;
            Result<List<GachaResultEntry>> gachaRes = core.PullGacha("banner_std", 1);
            Assert.IsTrue(gachaRes.Ok);
            Assert.AreEqual(gachaHistoryCountBefore + 1, core.Player.Gacha.History.Count, "轉蛋歷史增加 1 筆");

            // 劇情繼續推進，變數變成 20
            runner.Continue();
            Assert.AreEqual(20, Convert.ToInt32(runner.Variables["route_trust"]));

            // 3. 讀回劇情存檔 slot_001
            StorySlot slot = saveService.Load("slot_001");
            Assert.IsNotNull(slot);
            bool applied = saveService.ApplyToRunner(slot, runner, script);
            Assert.IsTrue(applied);

            // 關鍵斷言：Profile（角色、等級、轉蛋紀錄）絕無倒退！
            Assert.AreEqual(levelAfter, core.Player.Char("char04_elysia").Level, "讀回劇情存檔後，角色等級不可倒退");
            Assert.AreEqual(gachaHistoryCountBefore + 1, core.Player.Gacha.History.Count, "讀回劇情存檔後，轉蛋紀錄不可遺失");

            // 關鍵斷言：劇情變數精確恢復至存檔當下的 10
            Assert.AreEqual(10, Convert.ToInt32(runner.Variables["route_trust"]), "劇情變數成功恢復至存檔狀態");
        }

        // -----------------------------------------------------------------------------------------
        // B5-2: 讀回 X 後走另一個分支：已達成的結局仍然保留，新結局也會加入
        // -----------------------------------------------------------------------------------------
        [Test]
        public void B5_2_RestoringSlotAndTakingAlternateBranch_PreservesBothEndings()
        {
            var core = CreateCore();
            var presenter = new DummyPresenter();
            var saveService = new StorySaveService(_tempSaveDir, _clock);

            const string scriptText = @"
[node ChoicePoint]
line l0 Claire choose_now
option opt.a -> EndingNodeA
option opt.b -> EndingNodeB

[node EndingNodeA]
command ending ending_route_a
line la Claire ending_a_reached
end

[node EndingNodeB]
command ending ending_route_b
line lb Claire ending_b_reached
end
";
            var script = MiniScript.Parse(scriptText);
            var runner = new MiniScriptRunner(script);
            StoryCommandRegistry.Register(runner, core, presenter);

            runner.Start("ChoicePoint");
            runner.Continue(); // l0

            // 1. 在分歧點儲存劇情存檔 slot_002
            saveService.Save("slot_002", runner, chapterStartNode: "ChoicePoint");

            // 2. 走分支 A，達成 Ending A
            runner.Continue(); // 進入選項
            runner.Choose(0); // 選擇 EndingNodeA
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_a"), "分支 A 結局已記錄至 Profile");

            // 3. 讀回存檔 slot_002，改走分支 B
            StorySlot slot = saveService.Load("slot_002");
            saveService.ApplyToRunner(slot, runner, script);

            runner.Continue(); // l0
            runner.Continue(); // 進入選項
            runner.Choose(1); // 選擇 EndingNodeB

            // 關鍵斷言：Ending A 與 Ending B 雙雙保留在 Profile 中！
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_a"), "讀檔走新分支後，舊結局 ending_route_a 必須依然保留");
            Assert.IsTrue(core.Player.Endings.ContainsKey("ending_route_b"), "新達成的結局 ending_route_b 必須成功加入");
        }

        // -----------------------------------------------------------------------------------------
        // B5-3: 劇情存檔記錄的節點已經不存在時，退回該章節開頭，並回傳警告
        // -----------------------------------------------------------------------------------------
        [Test]
        public void B5_3_NonExistentNodeInSlot_FallsBackToChapterStart_AndEmitsWarning()
        {
            var saveService = new StorySaveService(_tempSaveDir, _clock);

            // 建立一個記錄在 DeletedNode 的舊存檔
            var oldSlot = new StorySlot
            {
                SlotId = "slot_old",
                Node = "DeletedNode",
                ChapterStartNode = "Chapter1_Start",
                Variables = new Dictionary<string, object> { { "trust", 15 } }
            };

            // 新劇本中已經沒有 DeletedNode，只有 Chapter1_Start
            const string newScriptText = @"
[node Chapter1_Start]
line l1 Claire chapter1_welcome
end
";
            var newScript = MiniScript.Parse(newScriptText);
            var runner = new MiniScriptRunner(newScript);

            string capturedWarning = null;
            bool success = saveService.ApplyToRunner(oldSlot, runner, newScript, warning => capturedWarning = warning);

            Assert.IsTrue(success);
            Assert.IsNotNull(capturedWarning, "節點遺失時應發出警告訊息");
            Assert.IsTrue(capturedWarning.Contains("DeletedNode"), "警告應包含遺失節點名稱");
            Assert.IsTrue(capturedWarning.Contains("Chapter1_Start"), "警告應包含退回之章節開頭名稱");

            // 驗證 runner 成功在 Chapter1_Start 啟動且變數成功恢復
            Assert.AreEqual("Chapter1_Start", runner.CurrentNodeName);
            Assert.AreEqual(15, Convert.ToInt32(runner.Variables["trust"]));
        }
    }
}
