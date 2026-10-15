using System;
using System.Collections.Generic;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class StoryRunnerTests
    {
        // -----------------------------------------------------------------------------------------
        // C1-1: 同一個劇本選不同選項，會走到不同的結局節點
        // -----------------------------------------------------------------------------------------

        private const string TwoChoicesScript = @"
[node Main]
line main_001 Claire story.main.001
option story.choice.a -> EndingA
option story.choice.b -> EndingB

[node EndingA]
line end_a Claire story.end.a
end

[node EndingB]
line end_b Claire story.end.b
end
";

        [Test]
        public void C1_1_ScriptWithTwoChoices_ChoiceA_ReachesEndingA()
        {
            var runner = new MiniScriptRunner(TwoChoicesScript);

            runner.Start("Main");
            Assert.IsNull(runner.CurrentLine);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsFalse(runner.IsEnded);

            // 第一行台詞
            runner.Continue();
            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("main_001", runner.CurrentLine.LineId);
            Assert.AreEqual("Claire", runner.CurrentLine.Speaker);
            Assert.AreEqual("story.main.001", runner.CurrentLine.TextKey);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsFalse(runner.IsEnded);

            // 推進至選項群組
            runner.Continue();
            Assert.IsNull(runner.CurrentLine);
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.AreEqual("story.choice.a", runner.CurrentOptions[0].TextKey);
            Assert.AreEqual("EndingA", runner.CurrentOptions[0].TargetNode);
            Assert.IsTrue(runner.CurrentOptions[0].IsSelectable);
            Assert.AreEqual("story.choice.b", runner.CurrentOptions[1].TextKey);
            Assert.AreEqual("EndingB", runner.CurrentOptions[1].TargetNode);
            Assert.IsTrue(runner.CurrentOptions[1].IsSelectable);
            Assert.IsFalse(runner.IsEnded);

            // 選擇選項 0 (EndingA)
            runner.Choose(0);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("end_a", runner.CurrentLine.LineId);
            Assert.AreEqual("Claire", runner.CurrentLine.Speaker);
            Assert.AreEqual("story.end.a", runner.CurrentLine.TextKey);
            Assert.IsFalse(runner.IsEnded);

            // 推進至結束
            runner.Continue();
            Assert.IsNull(runner.CurrentLine);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsTrue(runner.IsEnded);
        }

        [Test]
        public void C1_1_ScriptWithTwoChoices_ChoiceB_ReachesEndingB()
        {
            var runner = new MiniScriptRunner(TwoChoicesScript);

            runner.Start("Main");
            runner.Continue(); // main_001
            runner.Continue(); // options

            Assert.AreEqual(2, runner.CurrentOptions.Count);

            // 選擇選項 1 (EndingB)
            runner.Choose(1);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("end_b", runner.CurrentLine.LineId);
            Assert.AreEqual("Claire", runner.CurrentLine.Speaker);
            Assert.AreEqual("story.end.b", runner.CurrentLine.TextKey);
            Assert.IsFalse(runner.IsEnded);

            runner.Continue();
            Assert.IsTrue(runner.IsEnded);
        }

        // -----------------------------------------------------------------------------------------
        // C1-2: set 和 if 能讀寫劇情變數；條件可以呼叫遊戲狀態函式 (affection, owned)
        // -----------------------------------------------------------------------------------------

        private const string VariablesAndGameStateScript = @"
[node Main]
set trust = 2
set trust = trust + 3
set hero_name = ""Claire""
if affection(""char01_claire"") >= 3 -> ClaireHigh
jump ClaireLow

[node ClaireHigh]
set branch = ""high""
if owned(""char07_kagura"") -> HasKagura
jump NoKagura

[node HasKagura]
set kagura_status = ""recruited""
line line_kagura Kagura story.kagura.intro
end

[node NoKagura]
set kagura_status = ""missing""
line line_no_kagura Claire story.no_kagura
end

[node ClaireLow]
set branch = ""low""
line line_low Claire story.claire.low
end
";

        [Test]
        public void C1_2_SetAndIf_HighAffectionAndOwnedKagura_TakesKaguraBranch()
        {
            var runner = new MiniScriptRunner(VariablesAndGameStateScript);

            // 注入遊戲狀態函式
            runner.RegisterFunction("affection", args =>
            {
                string charId = args != null && args.Length > 0 ? args[0]?.ToString() : null;
                return charId == "char01_claire" ? 4 : 0;
            });
            runner.RegisterFunction("owned", args =>
            {
                string charId = args != null && args.Length > 0 ? args[0]?.ToString() : null;
                return charId == "char07_kagura";
            });

            runner.Start("Main");
            runner.Continue();

            // 驗證變數讀寫
            Assert.AreEqual(5, runner.Variables["trust"]);
            Assert.AreEqual("Claire", runner.Variables["hero_name"]);
            Assert.AreEqual("high", runner.Variables["branch"]);
            Assert.AreEqual("recruited", runner.Variables["kagura_status"]);

            // 驗證停留的台詞行
            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("line_kagura", runner.CurrentLine.LineId);
            Assert.AreEqual("Kagura", runner.CurrentLine.Speaker);
        }

        [Test]
        public void C1_2_SetAndIf_HighAffectionAndNotOwnedKagura_TakesNoKaguraBranch()
        {
            var runner = new MiniScriptRunner(VariablesAndGameStateScript);

            runner.RegisterFunction("affection", _ => 4);
            runner.RegisterFunction("owned", _ => false);

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(5, runner.Variables["trust"]);
            Assert.AreEqual("high", runner.Variables["branch"]);
            Assert.AreEqual("missing", runner.Variables["kagura_status"]);
            Assert.AreEqual("line_no_kagura", runner.CurrentLine.LineId);
        }

        [Test]
        public void C1_2_SetAndIf_LowAffection_TakesLowBranch()
        {
            var runner = new MiniScriptRunner(VariablesAndGameStateScript);

            runner.RegisterFunction("affection", _ => 1);
            runner.RegisterFunction("owned", _ => true);

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(5, runner.Variables["trust"]);
            Assert.AreEqual("low", runner.Variables["branch"]);
            Assert.IsFalse(runner.Variables.ContainsKey("kagura_status"));
            Assert.AreEqual("line_low", runner.CurrentLine.LineId);
        }

        [Test]
        public void C1_2_IntegerCondition_PreservesLongPrecision()
        {
            const string script = @"
[node Main]
if large_value == 9007199254740992 -> Wrong
line correct Claire story.correct
end

[node Wrong]
line wrong Claire story.wrong
end
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");
            runner.Variables["large_value"] = 9007199254740993L;
            runner.Continue();

            Assert.AreEqual("correct", runner.CurrentLine.LineId);
        }

        [Test]
        public void C1_2_SetExpression_SupportsSubtractionsAndVariableReferences()
        {
            const string script = @"
[node Main]
set base = 100
set penalty = 30
set bonus = 5
set total = base - penalty + bonus
line result Claire story.result
end
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(75, runner.Variables["total"]);
            Assert.AreEqual(100, runner.Variables["base"]);
        }

        // -----------------------------------------------------------------------------------------
        // C1-3: 選項可以依條件隱藏或停用；停用/不可選的選項無法被選擇
        // -----------------------------------------------------------------------------------------

        private const string ConditionalOptionsScript = @"
[node Main]
line prompt Claire story.prompt
option story.choice.normal -> TargetNormal
option story.choice.secret if affection(""char01_claire"") >= 3 -> TargetSecret
option story.choice.locked [disabled] -> TargetLocked

[node TargetNormal]
line end_normal Claire story.end.normal
end

[node TargetSecret]
line end_secret Claire story.end.secret
end

[node TargetLocked]
line end_locked Claire story.end.locked
end
";

        [Test]
        public void C1_3_ConditionalOption_HiddenWhenFalse_DisabledOptionCannotBeChosen()
        {
            var runner = new MiniScriptRunner(ConditionalOptionsScript);
            // 好感度為 1 (< 3)，secret 選項應隱藏
            runner.RegisterFunction("affection", _ => 1);

            runner.Start("Main");
            runner.Continue(); // prompt
            runner.Continue(); // options

            // 只有 2 個選項：normal 與 locked (secret 被隱藏)
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.AreEqual("story.choice.normal", runner.CurrentOptions[0].TextKey);
            Assert.IsTrue(runner.CurrentOptions[0].IsSelectable);
            Assert.IsTrue(runner.CurrentOptions[0].IsEnabled);

            Assert.AreEqual("story.choice.locked", runner.CurrentOptions[1].TextKey);
            Assert.IsFalse(runner.CurrentOptions[1].IsSelectable);
            Assert.IsFalse(runner.CurrentOptions[1].IsEnabled);

            // 嘗試選擇被停用的選項 (index 1) -> 丟出 InvalidOperationException 且狀態不變
            Assert.Throws<InvalidOperationException>(() => runner.Choose(1));
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.IsNull(runner.CurrentLine);
            Assert.IsFalse(runner.IsEnded);

            // 選擇正常選項 (index 0) -> 成功進入 TargetNormal
            runner.Choose(0);
            Assert.AreEqual("end_normal", runner.CurrentLine.LineId);
        }

        [Test]
        public void C1_3_ConditionalOption_AvailableWhenTrue()
        {
            var runner = new MiniScriptRunner(ConditionalOptionsScript);
            // 好感度為 5 (>= 3)，secret 選項應顯示
            runner.RegisterFunction("affection", _ => 5);

            runner.Start("Main");
            runner.Continue(); // prompt
            runner.Continue(); // options

            // 共有 3 個選項，且保留原始順序
            Assert.AreEqual(3, runner.CurrentOptions.Count);
            Assert.AreEqual("story.choice.normal", runner.CurrentOptions[0].TextKey);
            Assert.AreEqual("story.choice.secret", runner.CurrentOptions[1].TextKey);
            Assert.IsTrue(runner.CurrentOptions[1].IsSelectable);
            Assert.AreEqual("story.choice.locked", runner.CurrentOptions[2].TextKey);
            Assert.IsFalse(runner.CurrentOptions[2].IsSelectable);

            // 選擇條件選項 (index 1) -> 成功進入 TargetSecret
            runner.Choose(1);
            Assert.AreEqual("end_secret", runner.CurrentLine.LineId);
        }

        // -----------------------------------------------------------------------------------------
        // C1-4: Snapshot() 後繼續推進再 Restore()，回到完全相同的行與變數；快照不別名
        // -----------------------------------------------------------------------------------------

        private const string SnapshotScript = @"
[node Main]
set trust = 10
line line_001 Claire story.line.001
set trust = 20
line line_002 Claire story.line.002
end
";

        [Test]
        public void C1_3_TargetNodeEndingWithDisabled_RemainsSelectable()
        {
            const string script = @"
[node Main]
option story.choice.enabled -> TargetDisabled
[node TargetDisabled]
line reached Claire story.reached
end
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(1, runner.CurrentOptions.Count);
            Assert.IsTrue(runner.CurrentOptions[0].IsSelectable);
            Assert.AreEqual("TargetDisabled", runner.CurrentOptions[0].TargetNode);
            runner.Choose(0);
            Assert.AreEqual("reached", runner.CurrentLine.LineId);
        }

        [Test]
        public void C1_3_AllConditionalOptionsHidden_ContinuesToFollowingLine()
        {
            const string script = @"
[node Main]
option story.choice.secret if affection(""char01_claire"") >= 99 -> Secret
line fallback Claire story.fallback
end

[node Secret]
line secret Claire story.secret
end
";
            var runner = new MiniScriptRunner(script);
            runner.RegisterFunction("affection", _ => 1);

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual("fallback", runner.CurrentLine.LineId);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsFalse(runner.IsEnded);
        }

        [Test]
        public void C1_4_SnapshotAndRestore_PreservesLineEndStateAndVariables_WithoutAliasing()
        {
            var runner = new MiniScriptRunner(SnapshotScript);
            var liveVariables = runner.Variables;

            runner.Start("Main");
            runner.Continue(); // 停在 line_001

            Assert.AreEqual("line_001", runner.CurrentLine.LineId);
            Assert.AreEqual("Claire", runner.CurrentLine.Speaker);
            Assert.AreEqual("story.line.001", runner.CurrentLine.TextKey);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsFalse(runner.IsEnded);
            Assert.AreEqual(10, runner.Variables["trust"]);

            // 建立快照
            StorySnapshot snapshot = runner.Snapshot();
            Assert.IsNotNull(snapshot);
            Assert.AreEqual("Main", snapshot.NodeName);
            Assert.AreEqual("line_001", snapshot.CurrentLine.LineId);
            Assert.AreEqual(10, snapshot.Variables["trust"]);

            // 繼續推進到下一行並修改變數
            runner.Continue(); // 停在 line_002
            Assert.AreEqual("line_002", runner.CurrentLine.LineId);
            Assert.AreEqual(20, runner.Variables["trust"]);

            runner.Variables["trust"] = 999;
            runner.Variables["live_extra"] = "mutated";

            // 還原快照
            runner.Restore(snapshot);
            Assert.AreSame(liveVariables, runner.Variables);

            // 驗證還原回 snapshot 當時的狀態
            Assert.IsNotNull(runner.CurrentLine);
            Assert.AreEqual("line_001", runner.CurrentLine.LineId);
            Assert.AreEqual("Claire", runner.CurrentLine.Speaker);
            Assert.AreEqual("story.line.001", runner.CurrentLine.TextKey);
            Assert.AreEqual(0, runner.CurrentOptions.Count);
            Assert.IsFalse(runner.IsEnded);
            Assert.AreEqual(10, runner.Variables["trust"]);
            Assert.IsFalse(runner.Variables.ContainsKey("live_extra"));

            // 驗證修改還原後的 live 變數不會污染快照 (No Aliasing)
            runner.Variables["trust"] = 555;
            runner.Variables["post_restore_var"] = true;
            Assert.AreEqual(10, snapshot.Variables["trust"]);
            Assert.IsFalse(snapshot.Variables.ContainsKey("post_restore_var"));

            // 從還原點繼續推進，應再次正確到達 line_002
            runner.Continue();
            Assert.AreEqual("line_002", runner.CurrentLine.LineId);
            Assert.AreEqual(20, runner.Variables["trust"]);
        }

        [Test]
        public void C1_4_SnapshotAndRestore_AtOptionGroup_RestoresOptionsAndEnablesAlternativeChoice()
        {
            var runner = new MiniScriptRunner(TwoChoicesScript);

            runner.Start("Main");
            runner.Continue(); // main_001
            runner.Continue(); // options

            Assert.AreEqual(2, runner.CurrentOptions.Count);
            StorySnapshot snapshotAtChoice = runner.Snapshot();

            // 走分支 A
            runner.Choose(0);
            Assert.AreEqual("end_a", runner.CurrentLine.LineId);

            // 還原快照回選擇點
            runner.Restore(snapshotAtChoice);
            Assert.IsNull(runner.CurrentLine);
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.IsFalse(runner.IsEnded);

            // 改走分支 B
            runner.Choose(1);
            Assert.AreEqual("end_b", runner.CurrentLine.LineId);
        }

        // -----------------------------------------------------------------------------------------
        // 邊界條件與例外驗證：無效索引、未註冊指令、未註冊函式、無窮迴圈、腳本格式錯誤
        // -----------------------------------------------------------------------------------------

        [Test]
        public void Validation_InvalidOptionIndex_ThrowsArgumentOutOfRangeException_WithoutChangingState()
        {
            var runner = new MiniScriptRunner(TwoChoicesScript);
            runner.Start("Main");
            runner.Continue(); // main_001
            runner.Continue(); // options

            Assert.AreEqual(2, runner.CurrentOptions.Count);

            // 負數索引
            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Choose(-1));
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.IsNull(runner.CurrentLine);

            // 超出範圍索引
            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Choose(2));
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.IsNull(runner.CurrentLine);
        }

        [Test]
        public void Command_QuotedArguments_PreserveEmptyArgument()
        {
            const string script = "[node Main]\ncommand record \"\" next\nend\n";
            var runner = new MiniScriptRunner(script);
            string[] received = null;
            runner.RegisterCommand("record", args => received = args);

            runner.Start("Main");
            runner.Continue();

            CollectionAssert.AreEqual(new[] { string.Empty, "next" }, received);
        }

        [Test]
        public void Validation_UnmatchedQuote_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() => MiniScript.SplitCommandLine("line id speaker \"unfinished"));
            StringAssert.Contains("quote", ex.Message);
        }

        [Test]
        public void Validation_UnknownCommand_ThrowsInvalidOperationException()
        {
            const string script = @"
[node Main]
command non_registered_command arg1 arg2
end
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Continue());
            StringAssert.Contains("non_registered_command", ex.Message);
        }

        [Test]
        public void Command_DispatchesToRegisteredCallback_WithCorrectArguments()
        {
            const string script = @"
[node Main]
command mark_node_read Main 42
line done Claire story.done
end
";
            var runner = new MiniScriptRunner(script);
            var received = new List<string>();

            runner.RegisterCommand("mark_node_read", args =>
            {
                received.AddRange(args);
            });

            runner.Start("Main");
            runner.Continue();

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual("Main", received[0]);
            Assert.AreEqual("42", received[1]);
            Assert.AreEqual("done", runner.CurrentLine.LineId);
        }

        [Test]
        public void Continue_WhileOptionGroupVisible_ThrowsInvalidOperationException()
        {
            var runner = new MiniScriptRunner(TwoChoicesScript);
            runner.Start("Main");
            runner.Continue(); // main_001
            runner.Continue(); // options

            Assert.AreEqual(2, runner.CurrentOptions.Count);

            // 有可見選項時禁止 Continue() 繞過選擇
            Assert.Throws<InvalidOperationException>(() => runner.Continue());
            Assert.AreEqual(2, runner.CurrentOptions.Count);
        }

        [Test]
        public void CallsAfterIsEnded_RemainEnded_WithoutExecutingFurtherCommands()
        {
            const string script = @"
[node Main]
end
command should_never_run
";
            var runner = new MiniScriptRunner(script);
            bool executed = false;
            runner.RegisterCommand("should_never_run", _ => executed = true);

            runner.Start("Main");
            runner.Continue(); // reaches end

            Assert.IsTrue(runner.IsEnded);
            Assert.IsFalse(executed);

            // 再次 Continue 依然處於結束狀態，不繼續執行後續指令
            runner.Continue();
            Assert.IsTrue(runner.IsEnded);
            Assert.IsFalse(executed);
        }

        [Test]
        public void Start_ResetsState_AndAllowsPopulatingVariablesBeforeContinuing()
        {
            const string script = @"
[node Main]
if initial_trust >= 10 -> HighTrust
line low Claire story.low
end

[node HighTrust]
line high Claire story.high
end
";
            var runner = new MiniScriptRunner(script);
            var liveVariables = runner.Variables;

            runner.Start("Main");
            // 在 Start 後、Continue 前寫入變數
            runner.Variables["initial_trust"] = 15;

            runner.Continue();
            Assert.AreEqual("high", runner.CurrentLine.LineId);

            runner.Start("Main");
            Assert.AreSame(liveVariables, runner.Variables);
            Assert.IsFalse(runner.Variables.ContainsKey("initial_trust"));
        }

        [Test]
        public void InfiniteLoop_NonYieldingControlFlow_ThrowsInvalidOperationException()
        {
            const string script = @"
[node LoopA]
jump LoopB

[node LoopB]
jump LoopA
";
            var runner = new MiniScriptRunner(script);
            runner.Start("LoopA");

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Continue());
            StringAssert.Contains("Infinite control-flow loop detected", ex.Message);
        }

        [Test]
        public void MissingNode_OnStart_ThrowsKeyNotFoundException()
        {
            var runner = new MiniScriptRunner(TwoChoicesScript);
            Assert.Throws<KeyNotFoundException>(() => runner.Start("NonExistentNode"));
        }

        [Test]
        public void MissingNode_OnJump_ThrowsInvalidOperationException()
        {
            const string script = @"
[node Main]
jump NonExistentTarget
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");
            Assert.Throws<InvalidOperationException>(() => runner.Continue());
        }

        [Test]
        public void MalformedScript_VariousErrors_ThrowFormatException()
        {
            // 缺少節點開頭
            Assert.Throws<FormatException>(() => MiniScript.Parse("line test Claire story.test"));

            // 節點名稱重複
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\nend\n[node A]\nend"));

            // line 參數不足
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\nline id_only"));

            // option 缺少 ->
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\noption text_key_no_arrow"));

            // set 缺少 =
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\nset no_equals_sign"));

            // if 缺少 ->
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\nif cond_no_arrow"));

            // jump 缺少目標
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\njump"));

            // command 缺少指令名稱
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\ncommand"));

            // 未知指令
            Assert.Throws<FormatException>(() => MiniScript.Parse("[node A]\nfoobar unknown"));
        }

        [Test]
        public void ConditionEvaluator_UnknownFunction_ThrowsInvalidOperationException()
        {
            const string script = @"
[node Main]
if unknown_func(""test"") -> Target
end

[node Target]
end
";
            var runner = new MiniScriptRunner(script);
            runner.Start("Main");
            var ex = Assert.Throws<InvalidOperationException>(() => runner.Continue());
            StringAssert.Contains("unknown_func", ex.Message);
        }
    }
}
