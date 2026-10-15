using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Battle;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 工單 E2: 平衡模擬器驗證測試（BalanceSimTests）
    /// 涵蓋查核點：
    /// E2-1：產出勝率、平均通關秒數、星數的報表。
    /// E2-2：每個關卡至少有一種「初始角色加上可取得角色」的編隊能夠通關。
    /// E2-3：同樣的輸入產生完全相同的報表（決定性）。
    /// </summary>
    [TestFixture]
    public class BalanceSimTests
    {
        private MasterDatabase _master;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        [Test]
        public void E2_1_BalanceSim_GeneratesReport_WithWinRate_AvgTime_Stars()
        {
            var teams = new List<SimTeamConfig>
            {
                new SimTeamConfig
                {
                    Name = "Initial_Lv1",
                    CharIds = new List<string> { "char04_elysia", "char05_rin" },
                    Level = 1,
                    Awaken = 0
                }
            };
            var stages = new List<string> { "st_1_01" };
            var strategies = new List<SimStrategy> { SimStrategy.AggressiveDeploy };

            var rows = BalanceSimRunner.RunSimulation(_master, teams, stages, strategies, seedsPerConfig: 3);

            Assert.AreEqual(1, rows.Count);
            SimResultRow r = rows[0];
            Assert.AreEqual("st_1_01", r.StageId);
            Assert.AreEqual("Initial_Lv1", r.TeamName);
            Assert.AreEqual(SimStrategy.AggressiveDeploy, r.Strategy);
            Assert.AreEqual(3, r.TotalRuns);
            Assert.IsTrue(r.Wins >= 1, "初始隊伍應能通關 st_1_01");
            Assert.IsTrue(r.AvgDurationSec > 0, "應記錄通關耗時");
            Assert.IsTrue(r.AvgStars >= 1, "應記錄星數評定");

            string csv = BalanceSimRunner.GenerateCsv(rows);
            Assert.IsTrue(csv.Contains("st_1_01"), "CSV 應包含關卡資訊");
            Assert.IsTrue(csv.Contains("Initial_Lv1"), "CSV 應包含隊伍名稱");
            Assert.IsTrue(csv.Contains("AggressiveDeploy"), "CSV 應包含出兵策略");
        }

        [Test]
        public void E2_2_EveryStage_CanBeClearedByAtLeastOne_InitialOrObtainableTeam()
        {
            // 測試隊伍：全可取得角色 Lv20 與更高練度 Lv35 隊伍
            var teams = new List<SimTeamConfig>
            {
                new SimTeamConfig
                {
                    Name = "Full_Obtainable_Lv20",
                    CharIds = new List<string> { "char01_claire", "char02_luna", "char03_scarlet", "char04_elysia", "char05_rin" },
                    Level = 20,
                    Awaken = 1
                },
                new SimTeamConfig
                {
                    Name = "Full_Trained_Lv35",
                    CharIds = new List<string> { "char01_claire", "char02_luna", "char03_scarlet", "char04_elysia", "char05_rin" },
                    Level = 35,
                    Awaken = 2
                }
            };

            var stages = _master.Stages.Keys.OrderBy(x => x).ToList();
            var strategies = new List<SimStrategy> { SimStrategy.AggressiveDeploy, SimStrategy.CannonDeploy };

            var rows = BalanceSimRunner.RunSimulation(_master, teams, stages, strategies, seedsPerConfig: 2);

            foreach (var r in rows)
            {
                TestContext.WriteLine($"Stage={r.StageId}, Team={r.TeamName}, Strat={r.Strategy}, Wins={r.Wins}/{r.TotalRuns}, AvgSec={r.AvgDurationSec:F1}, HpPct={r.AvgBaseHpPct:F1}%");
            }

            foreach (string stageId in stages)
            {
                bool canClear = rows.Any(r => r.StageId == stageId && r.Wins > 0);
                Assert.IsTrue(canClear, $"E2-2 查核點：關卡 {stageId} 必須至少有一種編隊可通關");
            }
        }

        [Test]
        public void E2_3_BalanceSim_IsDeterministic_IdenticalInputProducesIdenticalCsv()
        {
            var teams = new List<SimTeamConfig>
            {
                new SimTeamConfig
                {
                    Name = "Initial_Lv1",
                    CharIds = new List<string> { "char04_elysia", "char05_rin" },
                    Level = 1,
                    Awaken = 0
                }
            };
            var stages = new List<string> { "st_1_01", "st_1_02" };
            var strategies = new List<SimStrategy> { SimStrategy.AggressiveDeploy };

            var rows1 = BalanceSimRunner.RunSimulation(_master, teams, stages, strategies, seedsPerConfig: 3);
            string csv1 = BalanceSimRunner.GenerateCsv(rows1);

            var rows2 = BalanceSimRunner.RunSimulation(_master, teams, stages, strategies, seedsPerConfig: 3);
            string csv2 = BalanceSimRunner.GenerateCsv(rows2);

            Assert.AreEqual(csv1, csv2, "E2-3 查核點：同樣的輸入必須產生完全一致的 CSV 報表");
        }
    }
}
