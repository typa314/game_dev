#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.Player;
using Game.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    [TestFixture]
    public sealed class UnityIntegrationPlayModeTests
    {
        private GameObject _holder;
        private GameBootstrap _bootstrap;

        [SetUp]
        public void SetUp()
        {
            GameServices.Clear();
            _holder = new GameObject("TestBootstrapHolder");
            _bootstrap = _holder.AddComponent<GameBootstrap>();
            _bootstrap.Boot();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.Clear();
            if (_holder != null)
            {
                UnityEngine.Object.DestroyImmediate(_holder);
            }
        }

        // =========================================================================================
        // F3: Play 模式 DebugMenu 操作與存檔重啟驗證
        // =========================================================================================
        [Test]
        public void F3_PlayMode_DebugMenuAndSaveReload_Succeeds()
        {
            // 1. 驗證 GameBootstrap 啟動成功，GameServices 註冊完成
            Assert.IsTrue(GameServices.IsReady, "GameServices 應在啟動後就緒");
            GameCore core = GameServices.Core;
            Assert.IsNotNull(core, "GameCore 實例應已建立");

            // 給予足夠資源進行操作測試（配置驅動）
            var stdBanner = core.Master.Banners.GetOrNull("banner_std");
            if (stdBanner?.Cost?.Ten != null)
            {
                foreach (var costItem in stdBanner.Cost.Ten)
                {
                    core.Player.AddItem(costItem.Item, costItem.N * 2);
                }
            }
            core.Player.AddItem("item_dia_free", 3000);
            core.Player.AddItem("exp_s", 10);
            core.Player.Encounter.Stamina = 100;

            // 2. 轉蛋操作：十連抽
            var gachaRes = core.PullGacha("banner_std", 10);
            Assert.IsTrue(gachaRes.Ok, $"轉蛋應成功執行: {gachaRes.Error}");
            Assert.AreEqual(10, gachaRes.Value.Count);
            int charCountAfterGacha = core.Player.Characters.Count;

            // 3. 角色養成：升級操作
            string firstChar = core.Player.Team[0];
            var lvRes = core.LevelUp(firstChar, new List<ItemStack> { new ItemStack("exp_s", 1) });
            Assert.IsTrue(lvRes.Ok, "升級應成功執行");

            // 4. 巧遇操作
            var encRes = core.StartEncounter("spot_park");
            Assert.IsTrue(encRes.Ok, "巧遇應成功執行");

            // 5. 存檔落盤：觸發 SaveIfDirty
            core.Dirty = true;
            _bootstrap.SaveIfDirty();

            string savePath = Path.Combine(Application.persistentDataPath, "saves", GameBootstrap.ProfileFile);
            Assert.IsTrue(File.Exists(savePath), "存檔 profile.json 應確實出現在 persistentDataPath");
            FileInfo fi = new FileInfo(savePath);
            Assert.IsTrue(fi.Length > 0, "存檔大小應大於 0");

            // 6. 模擬重開（Reboot）：讀取存檔並驗證無損一致性
            var store = GameServices.SaveStore;
            var loadErrors = new List<string>();
            PlayerState loadedPlayer = store.ReadWithFallback<PlayerState>(GameBootstrap.ProfileFile, Game.Core.Persistence.SaveCodec.Decode, loadErrors);
            Assert.IsEmpty(loadErrors, "重讀存檔應無任何錯誤");
            Assert.IsNotNull(loadedPlayer, "重讀之 PlayerState 不應為 null");
            Assert.AreEqual(charCountAfterGacha, loadedPlayer.Characters.Count, "重啟後角色數量應完全一致");
            Assert.AreEqual(core.Player.Char(firstChar).Level, loadedPlayer.Char(firstChar).Level, "重啟後角色等級應保持一致");
            Assert.AreEqual(core.Player.Gacha.TotalPulls, loadedPlayer.Gacha.TotalPulls, "重啟後抽卡次數應保持一致");
        }

        // =========================================================================================
        // F4: Play 模式 BattleDebugView 與戰鬥完整模擬驗證
        // =========================================================================================
        [Test]
        public void F4_PlayMode_BattleRunner_RunsFullBattle_AndMatchesSubmission()
        {
            Assert.IsTrue(GameServices.IsReady);
            GameCore core = GameServices.Core;

            var battleRunner = _holder.AddComponent<BattleRunner>();
            var debugView = _holder.AddComponent<BattleDebugView>();

            // 1. 開啟關卡 st_1_01
            bool started = battleRunner.StartBattle("st_1_01");
            Assert.IsTrue(started, "st_1_01 應成功開始戰鬥");
            Assert.IsTrue(battleRunner.IsRunning, "BattleRunner 應處於運行中狀態");
            Assert.IsNotNull(battleRunner.Sim, "BattleSimulator 不應為 null");

            // 2. 推進模擬直到戰鬥分出勝負（最多 300 秒）
            int maxTicks = 300 * battleRunner.Sim.TicksPerSecond;
            int tickCount = 0;
            while (battleRunner.IsRunning && tickCount < maxTicks)
            {
                // 每隔一定間隔自動點擊升級或出兵
                if (tickCount % 60 == 0)
                {
                    battleRunner.Deploy(0);
                    battleRunner.UpgradeEnergy();
                }

                // 呼叫 Step 推進 1 個 tick
                battleRunner.Sim.Step();
                tickCount++;

                // 檢查是否結束
                if (battleRunner.Sim.Outcome != BattleOutcome.Ongoing)
                {
                    battleRunner.Submit();
                    break;
                }
            }

            // 3. 驗證戰鬥已正常終止且表現層不為運行狀態
            Assert.IsFalse(battleRunner.IsRunning, "戰鬥結束後 Runner 不應處於運行狀態");
            Assert.IsNotNull(battleRunner.LastReward, "結束後應具有結算回報");
            Assert.IsTrue(battleRunner.LastReward.Ok, "SubmitBattle 結算應成功");

            // 4. 驗證畫面勝負與提交結果 100% 一致
            Assert.AreEqual(battleRunner.Sim.Outcome, battleRunner.LastReward.Value.Result.Outcome, "畫面與提交報告的勝負狀態應 100% 一致");
            Assert.IsTrue(battleRunner.LastReward.Value.Stars >= 0 && battleRunner.LastReward.Value.Stars <= 3, "星數應在 0 到 3 星範圍內");
        }
    }
}
#endif
