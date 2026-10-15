using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.Domain;
using Game.Core.Localization;
using Game.Core.Story;
using Game.Core.Story.Yarn;
using Game.Core.Story.MiniScript;

namespace Game.Tests
{
    [TestFixture]
    public class YarnStoryRunnerTests
    {
        private const string SampleYarnScript = @"
title: Start
tags: rating:G
---
<<set $affection = 0>>
<<bg scene_rooftop sunset>>
<<show char01_claire stand tsundere>>
Claire: 你來啦。 #line:rc3_001
-> 抱歉讓你久等了。 #line:rc3_opt1 <<jump Route_Apology>>
-> 我只是碰巧經過。 #line:rc3_opt2 <<jump Route_Dismissive>>
===
title: Route_Apology
tags: rating:G
---
<<set $affection = $affection + 1>>
Claire: 哼，知道就好。 #line:rc3_002
===
title: Route_Dismissive
tags: rating:G
---
<<set $affection = $affection - 1>>
Claire: 真是個笨蛋！ #line:rc3_003
===
title: Intimate_Scene
tags: rating:R18 fallback:Fade_To_Morning
---
Claire: 這裡只有我們兩個人…… #line:rc3_r18_001
===
title: Fade_To_Morning
tags: rating:G
---
Narrator: 隔天早晨，陽光灑入房內。 #line:rc3_fade_001
===
";

        private const string EquivalentMiniScript = @"
[node Start]
set affection = 0
command bg scene_rooftop sunset
command show char01_claire stand tsundere
line rc3_001 Claire 你來啦。
option 抱歉讓你久等了。 -> Route_Apology
option 我只是碰巧經過。 -> Route_Dismissive

[node Route_Apology]
set affection = affection + 1
line rc3_002 Claire 哼，知道就好。

[node Route_Dismissive]
set affection = affection - 1
line rc3_003 Claire 真是個笨蛋！
";

        [Test]
        public void F6_1_YarnRunner_ExecutesDialogueAndBranches_IdenticalToMiniScript()
        {
            var executedCommands = new List<string>();

            var yarnRunner = new YarnStoryRunner(SampleYarnScript);
            yarnRunner.RegisterCommand("bg", args => executedCommands.Add($"bg:{string.Join(" ", args)}"));
            yarnRunner.RegisterCommand("show", args => executedCommands.Add($"show:{string.Join(" ", args)}"));

            yarnRunner.Start("Start");
            yarnRunner.Continue();

            // 1. 驗證背景與角色登場指令
            Assert.AreEqual(2, executedCommands.Count);
            Assert.AreEqual("bg:scene_rooftop sunset", executedCommands[0]);
            Assert.AreEqual("show:char01_claire stand tsundere", executedCommands[1]);

            // 2. 驗證首句台詞與 line ID
            Assert.IsNotNull(yarnRunner.CurrentLine);
            Assert.AreEqual("Claire", yarnRunner.CurrentLine.Speaker);
            Assert.AreEqual("你來啦。", yarnRunner.CurrentLine.TextKey);
            Assert.AreEqual("rc3_001", yarnRunner.CurrentLine.LineId);

            // 推進至選項
            yarnRunner.Continue();
            Assert.AreEqual(2, yarnRunner.CurrentOptions.Count);
            Assert.AreEqual("抱歉讓你久等了。", yarnRunner.CurrentOptions[0].TextKey);
            Assert.AreEqual("Route_Apology", yarnRunner.CurrentOptions[0].TargetNode);

            // 3. 選擇選項 0 (Route_Apology)
            yarnRunner.Choose(0);
            Assert.AreEqual("Route_Apology", yarnRunner.CurrentNodeName);
            Assert.AreEqual("rc3_002", yarnRunner.CurrentLine.LineId);
            Assert.AreEqual(1.0, Convert.ToDouble(yarnRunner.Variables["affection"]));

            // 4. 與 MiniScript 執行結果對照
            var miniRunner = new MiniScriptRunner(EquivalentMiniScript);
            miniRunner.RegisterCommand("bg", args => {});
            miniRunner.RegisterCommand("show", args => {});
            miniRunner.Start("Start");
            miniRunner.Continue(); // dialogue
            miniRunner.Continue(); // options
            miniRunner.Choose(0);  // Route_Apology

            Assert.AreEqual(yarnRunner.CurrentNodeName, miniRunner.CurrentNodeName, "雙引擎跳轉節點一致");
            Assert.AreEqual(yarnRunner.CurrentLine.LineId, miniRunner.CurrentLine.LineId, "雙引擎 Line ID 一致");
            Assert.AreEqual(
                Convert.ToDouble(yarnRunner.Variables["affection"]),
                Convert.ToDouble(miniRunner.Variables["affection"]),
                "雙引擎劇情變數結果完全一致");
        }

        [Test]
        public void F6_2_YarnRunner_LineId_LocalizationSwitching()
        {
            var loc = new LocalizationService("zh-TW");
            loc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "rc3_001", "你來啦。" },
                { "rc3_002", "哼，知道就好。" }
            });
            loc.LoadLocale("en", new Dictionary<string, string>
            {
                { "rc3_001", "You're finally here." },
                { "rc3_002", "Hmph, glad you know." }
            });

            // 繁體中文環境執行
            var runnerZh = new YarnStoryRunner(SampleYarnScript, null, loc);
            runnerZh.Start("Start");
            runnerZh.Continue();

            Assert.AreEqual("rc3_001", runnerZh.CurrentLine.LineId);
            Assert.AreEqual("你來啦。", runnerZh.CurrentLine.TextKey);

            // 切換為英文環境
            loc.SetLocale("en");
            var runnerEn = new YarnStoryRunner(SampleYarnScript, null, loc);
            runnerEn.Start("Start");
            runnerEn.Continue();

            Assert.AreEqual("rc3_001", runnerEn.CurrentLine.LineId, "切換語言後 Line ID 依然維持不變");
            Assert.AreEqual("You're finally here.", runnerEn.CurrentLine.TextKey, "文字內容自動轉譯為英文");
        }

        [Test]
        public void F6_3_YarnRunner_SnapshotAndRestore_PreservesState()
        {
            var runner = new YarnStoryRunner(SampleYarnScript);
            runner.Start("Start");
            runner.Continue(); // rc3_001
            runner.Continue(); // options

            // 在選項處存取快照
            var snapshot = runner.Snapshot();
            Assert.AreEqual("Start", snapshot.NodeName);
            Assert.AreEqual(2, snapshot.CurrentOptions.Count);

            // 選擇分支 1 推進至結束
            runner.Choose(1); // Route_Dismissive
            Assert.AreEqual("Route_Dismissive", runner.CurrentNodeName);
            Assert.AreEqual(-1.0, Convert.ToDouble(runner.Variables["affection"]));

            // 讀回快照
            runner.Restore(snapshot);
            Assert.AreEqual("Start", runner.CurrentNodeName);
            Assert.AreEqual(2, runner.CurrentOptions.Count);
            Assert.AreEqual(0.0, Convert.ToDouble(runner.Variables["affection"]));

            // 重新選分支 0 驗證分歧
            runner.Choose(0); // Route_Apology
            Assert.AreEqual("Route_Apology", runner.CurrentNodeName);
            Assert.AreEqual(1.0, Convert.ToDouble(runner.Variables["affection"]));
        }

        [Test]
        public void F6_4_YarnRunner_RatingFallback_ProtectsUnderGPolicy()
        {
            // 在 G 政策下
            var policyG = new RatingPolicy(Rating.G, Rating.G);
            var runnerG = new YarnStoryRunner(SampleYarnScript, policyG);
            runnerG.Start("Intimate_Scene"); // R18 節點
            runnerG.Continue();

            // 應自動退回至 Fade_To_Morning
            Assert.AreEqual("Fade_To_Morning", runnerG.CurrentNodeName, "G 政策下必須自動退回至 fallback 節點");
            Assert.AreEqual("rc3_fade_001", runnerG.CurrentLine.LineId);

            // 在 R18 政策下
            var policyR18 = new RatingPolicy(Rating.R18, Rating.R18);
            var runnerR18 = new YarnStoryRunner(SampleYarnScript, policyR18);
            runnerR18.Start("Intimate_Scene");
            runnerR18.Continue();

            Assert.AreEqual("Intimate_Scene", runnerR18.CurrentNodeName, "R18 政策下可順利進入成人節點");
            Assert.AreEqual("rc3_r18_001", runnerR18.CurrentLine.LineId);
        }
    }
}
