using System.Collections.Generic;
using Game.Core;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class StoryValidatorTests
    {
        private MasterDatabase _master;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        // -----------------------------------------------------------------------------------------
        // C5-1: jump / if / option 的目標節點不存在
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C5_1_JumpToNonExistentNode_FailsWithTargetNodeNotFound()
        {
            const string scriptText = @"
[node Start]
line l1 Claire hello
jump NonExistentNode
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.TargetNodeNotFound));
        }

        [Test]
        public void C5_1_IfAndOptionToNonExistentNode_FailsWithTargetNodeNotFound()
        {
            const string scriptText = @"
[node Start]
if $flag -> GhostIfNode
option choice.a -> GhostOptNode
line l1 Claire hello
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.TargetNodeNotFound));
        }

        [Test]
        public void C5_1_ValidTargetNodes_Passes()
        {
            const string scriptText = @"
[node Start]
line l1 Claire hello
jump NextNode

[node NextNode]
line l2 Claire next
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(result.HasError(StoryValidationError.TargetNodeNotFound));
        }

        // -----------------------------------------------------------------------------------------
        // C5-2: 有台詞缺少 line ID，或 line ID 重複
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C5_2_MissingLineId_FailsWithMissingLineId()
        {
            const string scriptText = @"
[node Start]
line - Claire hello
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.MissingLineId));
        }

        [Test]
        public void C5_2_DuplicateLineId_FailsWithDuplicateLineId()
        {
            const string scriptText = @"
[node Node1]
line dup_id Claire hello
jump Node2

[node Node2]
line dup_id Claire hello_again
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.DuplicateLineId));
        }

        [Test]
        public void C5_2_UniqueLineIds_Passes()
        {
            const string scriptText = @"
[node Start]
line id_001 Claire hello
line id_002 Claire world
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsTrue(result.IsValid);
        }

        // -----------------------------------------------------------------------------------------
        // C5-3: 引用的角色、CG、場景、結局 ID 不存在於主資料
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C5_3_InvalidCharacterId_FailsWithInvalidCharacterId()
        {
            const string scriptText = @"
[node Start]
command show ghost_character
line l1 Claire hello
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script, master: _master);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.InvalidCharacterId));
        }

        [Test]
        public void C5_3_InvalidSceneId_FailsWithInvalidSceneId()
        {
            const string scriptText = @"
[node Start]
command bg non_existent_scene
line l1 Claire hello
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script, master: _master);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.InvalidSceneId));
        }

        [Test]
        public void C5_3_InvalidCgId_FailsWithInvalidCgId()
        {
            const string scriptText = @"
[node Start]
command cg cg_unknown_999
line l1 Claire hello
end
";
            var script = MiniScript.Parse(scriptText);
            var validCgs = new HashSet<string> { "cg_char01_01", "cg_char01_02" };
            var result = StoryValidator.Validate(script, validCgIds: validCgs);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.InvalidCgId));
        }

        [Test]
        public void C5_3_InvalidEndingId_FailsWithInvalidEndingId()
        {
            const string scriptText = @"
[node Start]
command ending ending_fake_xyz
line l1 Claire hello
end
";
            var script = MiniScript.Parse(scriptText);
            var validEndings = new HashSet<string> { "ending_claire_good", "ending_claire_normal" };
            var result = StoryValidator.Validate(script, validEndingIds: validEndings);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.InvalidEndingId));
        }

        [Test]
        public void C5_3_ValidReferences_Passes()
        {
            const string scriptText = @"
[node Start]
command bg scene_lobby_default
command show char04_elysia
command cg cg_elysia_01
command ending ending_elysia_good
line l1 Claire all_valid
end
";
            var script = MiniScript.Parse(scriptText);
            var validCgs = new HashSet<string> { "cg_elysia_01" };
            var validEndings = new HashSet<string> { "ending_elysia_good" };
            var result = StoryValidator.Validate(script, master: _master, validCgIds: validCgs, validEndingIds: validEndings);

            Assert.IsTrue(result.IsValid);
        }

        // -----------------------------------------------------------------------------------------
        // C5-4: R18 節點缺少 fallback
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C5_4_R18NodeMissingFallback_FailsWithR18MissingFallback()
        {
            const string scriptText = @"
[node IntimateNode rating:R18]
line l1 Claire secret
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.R18MissingFallback));
        }

        [Test]
        public void C5_4_R18NodeWithNonExistentFallback_FailsWithFallbackNodeNotFound()
        {
            const string scriptText = @"
[node IntimateNode rating:R18 fallback:MissingFallbackNode]
line l1 Claire secret
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.FallbackNodeNotFound));
        }

        [Test]
        public void C5_4_R18NodeWithValidFallback_Passes()
        {
            const string scriptText = @"
[node IntimateNode rating:R18 fallback:SafeNode]
line l1 Claire secret
end

[node SafeNode]
line l2 Claire safe
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsTrue(result.IsValid);
        }

        // -----------------------------------------------------------------------------------------
        // C5-5: 可達性：每個結局都至少有一條路徑可以到達；列出所有無法到達的節點
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C5_5_UnreachableEndingInIsolatedNode_FailsWithUnreachableEnding()
        {
            const string scriptText = @"
[node Start]
line l1 Claire start
end

[node OrphanNode]
command ending ending_unreachable
line l2 Claire orphan
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.UnreachableEnding));
            Assert.Contains("OrphanNode", result.UnreachableNodes);
        }

        [Test]
        public void C5_5_ExpectedEndingNotDefined_FailsWithUnreachableEnding()
        {
            const string scriptText = @"
[node Start]
command ending ending_true
line l1 Claire start
end
";
            var script = MiniScript.Parse(scriptText);
            var expected = new HashSet<string> { "ending_true", "ending_bad" };
            var result = StoryValidator.Validate(script, expectedEndingIds: expected);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.UnreachableEnding));
        }

        [Test]
        public void C5_5_AllEndingsAndNodesReachable_PassesWithNoUnreachableNodes()
        {
            const string scriptText = @"
[node Start]
line l1 Claire start
option choice.a -> GoodBranch
option choice.b -> BadBranch

[node GoodBranch]
command ending ending_good
end

[node BadBranch]
command ending ending_bad
end
";
            var script = MiniScript.Parse(scriptText);
            var expected = new HashSet<string> { "ending_good", "ending_bad" };
            var result = StoryValidator.Validate(script, expectedEndingIds: expected);

            Assert.IsTrue(result.IsValid);
            Assert.IsEmpty(result.UnreachableNodes);
        }

        // -----------------------------------------------------------------------------------------
        // C5-6: 使用了未註冊的指令
        // -----------------------------------------------------------------------------------------
        [Test]
        public void C5_6_UnregisteredCommand_FailsWithUnregisteredCommand()
        {
            const string scriptText = @"
[node Start]
command destroy_database player
line l1 Claire boom
end
";
            var script = MiniScript.Parse(scriptText);
            var result = StoryValidator.Validate(script);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasError(StoryValidationError.UnregisteredCommand));
        }

        [Test]
        public void C5_6_AllRegisteredCommands_Passes()
        {
            const string scriptText = @"
[node Start]
command bg scene_lobby_default
command show char04_elysia
command expr char04_elysia smile
command motion char04_elysia idle
command cg cg_sample
command bgm bgm_main
command se se_click
command voice voice_01
command affection char04_elysia 10
command give gold 100
command ending ending_sample
command wait 1.5
command fade black 0.5
command shake 0.3 5
command hide char04_elysia
line l1 Claire done
end
";
            var script = MiniScript.Parse(scriptText);
            // 不傳 master / cg / ending 以純驗證指令註冊合法性
            var result = StoryValidator.Validate(script);

            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(result.HasError(StoryValidationError.UnregisteredCommand));
        }
    }
}
