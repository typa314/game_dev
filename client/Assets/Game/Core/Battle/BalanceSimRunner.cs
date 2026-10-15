using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;

namespace Game.Core.Battle
{
    public enum SimStrategy
    {
        AggressiveDeploy,        // 只要冷卻與能量允許立即出兵
        BalancedUpgradeDeploy,   // 前期優先升級能量等級至 Lv2，再持續出兵
        CannonDeploy             // 積極出兵，且據點砲蓄力完成立即發射
    }

    public sealed class SimTeamConfig
    {
        public string Name { get; set; }
        public List<string> CharIds { get; set; } = new List<string>();
        public int Level { get; set; } = 1;
        public int Awaken { get; set; } = 0;
        public string PortraitStage { get; set; }
    }

    public sealed class SimResultRow
    {
        public string StageId { get; set; }
        public string TeamName { get; set; }
        public SimStrategy Strategy { get; set; }
        public int TotalRuns { get; set; }
        public int Wins { get; set; }
        public double WinRatePct => TotalRuns > 0 ? (double)Wins * 100.0 / TotalRuns : 0.0;
        public double AvgDurationSec { get; set; }
        public double AvgStars { get; set; }
        public double AvgBaseHpPct { get; set; }
    }

    public static class BalanceSimRunner
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow => new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            public DateTime LocalNow => UtcNow;
        }

        public static List<SimResultRow> RunSimulation(
            MasterDatabase master,
            IReadOnlyList<SimTeamConfig> teams,
            IReadOnlyList<string> stageIds,
            IReadOnlyList<SimStrategy> strategies,
            int seedsPerConfig = 5)
        {
            var results = new List<SimResultRow>();
            var clock = new FixedClock();
            var rating = new RatingPolicy(Rating.G, Rating.G);

            foreach (string stageId in stageIds)
            {
                if (!master.Stages.TryGetValue(stageId, out StageDef stage)) continue;

                foreach (SimTeamConfig team in teams)
                {
                    foreach (SimStrategy strategy in strategies)
                    {
                        int wins = 0;
                        double totalTicks = 0;
                        double totalStars = 0;
                        double totalBaseHpPct = 0;

                        for (int s = 0; s < seedsPerConfig; s++)
                        {
                            ulong seed = (ulong)(1000 + s * 37);

                            // 建立標準 PlayerState 與 GameCore
                            var player = GameCore.NewPlayer(master, clock);
                            foreach (string charId in team.CharIds)
                            {
                                if (string.IsNullOrEmpty(charId)) continue;
                                if (!player.Characters.TryGetValue(charId, out CharacterState cs))
                                {
                                    player.Characters[charId] = cs = new CharacterState();
                                }
                                cs.Level = team.Level;
                                cs.Awaken = team.Awaken;
                                if (!string.IsNullOrEmpty(team.PortraitStage))
                                {
                                    cs.PortraitStage = team.PortraitStage;
                                    cs.UnlockedPortraitStages.Add(team.PortraitStage);
                                }
                            }

                            var core = new GameCore(master, player, new SplitMix64Random(seed), clock, rating);
                            BattleSetup setup = core.BuildSetup(stage, team.CharIds, seed);

                            var sim = new BattleSimulator(setup);

                            while (sim.Outcome == BattleOutcome.Ongoing)
                            {
                                bool canFireCannon = sim.CannonFullTicks > 0 && sim.CannonChargeTicks >= sim.CannonFullTicks;
                                bool canUpgradeEnergy = sim.EnergyUpgradeCost != null && sim.EnergyMilli >= (long)sim.EnergyUpgradeCost.Value * 1000;

                                switch (strategy)
                                {
                                    case SimStrategy.CannonDeploy:
                                        if (canFireCannon) sim.Enqueue(CommandType.FireCannon, 0);
                                        for (int slot = 0; slot < setup.Team.Count; slot++)
                                        {
                                            if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                                        }
                                        break;

                                    case SimStrategy.BalancedUpgradeDeploy:
                                        if (sim.EnergyLevel < 2 && canUpgradeEnergy)
                                        {
                                            sim.Enqueue(CommandType.UpgradeEnergy, 0);
                                        }
                                        else
                                        {
                                            for (int slot = 0; slot < setup.Team.Count; slot++)
                                            {
                                                if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                                            }
                                        }
                                        break;

                                    case SimStrategy.AggressiveDeploy:
                                    default:
                                        for (int slot = 0; slot < setup.Team.Count; slot++)
                                        {
                                            if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                                        }
                                        break;
                                }

                                sim.Step();
                            }

                            BattleResult res = sim.Result();
                            bool isWin = res.Outcome == BattleOutcome.Win;
                            if (isWin) wins++;

                            totalTicks += res.Ticks;
                            totalStars += core.CountStars(stage, res);
                            double hpPct = (double)res.AllyBaseHp * 100.0 / res.AllyBaseMaxHp;
                            totalBaseHpPct += Math.Max(0.0, hpPct);
                        }

                        double avgDuration = totalTicks / (seedsPerConfig * master.Config.Battle.TicksPerSecond);
                        double avgStars = totalStars / seedsPerConfig;
                        double avgBaseHpPct = totalBaseHpPct / seedsPerConfig;

                        results.Add(new SimResultRow
                        {
                            StageId = stageId,
                            TeamName = team.Name,
                            Strategy = strategy,
                            TotalRuns = seedsPerConfig,
                            Wins = wins,
                            AvgDurationSec = avgDuration,
                            AvgStars = avgStars,
                            AvgBaseHpPct = avgBaseHpPct
                        });
                    }
                }
            }

            return results;
        }

        public static string GenerateCsv(IEnumerable<SimResultRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Stage,Team,Strategy,Runs,Wins,WinRatePct,AvgDurationSec,AvgStars,AvgBaseHpPct");

            foreach (SimResultRow r in rows)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5:F1},{6:F2},{7:F2},{8:F1}",
                    r.StageId,
                    r.TeamName,
                    r.Strategy,
                    r.TotalRuns,
                    r.Wins,
                    r.WinRatePct,
                    r.AvgDurationSec,
                    r.AvgStars,
                    r.AvgBaseHpPct));
            }

            return sb.ToString();
        }
    }
}
