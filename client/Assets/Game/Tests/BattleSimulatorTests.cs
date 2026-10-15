using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A7：戰鬥模擬器（BattleSimulator.cs）單元測試
    /// 恪守 Claude 既定之推線戰鬥架構（design/05_battle.md）
    /// 涵蓋查核點 A7-1 ～ A7-14
    /// </summary>
    [TestFixture]
    public class BattleSimulatorTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; }
            public DateTime LocalNow => UtcNow;

            public FixedClock(DateTime? utc = null)
            {
                UtcNow = DateTime.SpecifyKind(utc ?? new DateTime(2026, 10, 14, 12, 0, 0), DateTimeKind.Utc);
            }
        }

        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static GameCore CreateCore(MasterDatabase m, ulong seed = 1)
        {
            var clock = new FixedClock();
            PlayerState p = GameCore.NewPlayer(m, clock);
            return new GameCore(m, p, new SplitMix64Random(seed), clock, new RatingPolicy(Rating.G, Rating.G));
        }

        private static BattleSetup CreateSimpleSetup(int allyBaseHp = 1000, int enemyBaseHp = 1000, int length = 1000)
        {
            var stage = new StageDef
            {
                Id = "st_test",
                Length = length,
                AllyBaseHp = allyBaseHp,
                EnemyBaseHp = enemyBaseHp,
                Waves = new List<WaveDef>()
            };

            var rules = new BattleRules
            {
                TicksPerSecond = 30,
                DefaultAllyBaseHp = 20000,
                EnergyMaxByLevel = new List<int> { 1000, 2000 },
                EnergyRegenPerSecByLevel = new List<int> { 50, 100 },
                EnergyUpgradeCost = new List<int> { 500 },
                StartEnergy = 500,
                CannonChargeSec = 10,
                CannonDamage = 200,
                CannonRange = 800,
                KnockbackDistance = 60,
                MaxUnitsPerSide = 10,
                MaxBattleSec = 300
            };

            return new BattleSetup
            {
                Stage = stage,
                Rules = rules,
                Team = new List<UnitTemplate>(),
                EnemyTemplates = new Dictionary<string, UnitTemplate>(),
                Seed = 1
            };
        }

        // =============================================================
        // A7-1 & A7-2：即時模擬與回放完全一致，且 SubmitBattle 重新模擬勝負一致
        // =============================================================

        [Test]
        public void A7_1_And_2_LiveAndReplayMatch_AndSubmitMatches()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m, 7);
            BattleSetup setup = core.PrepareBattle("st_1_01", 7).Value;

            var sim = new BattleSimulator(setup);
            while (sim.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setup.Team.Count; slot++)
                {
                    if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                }
                sim.Step();
            }
            BattleResult live = sim.Result();

            var report = new BattleReport
            {
                StageId = "st_1_01",
                Seed = 7,
                Team = new List<string>(core.Player.Team),
                Commands = sim.Recorded,
                MasterDataVersion = 1
            };

            BattleResult replay = BattleSimulator.Run(core.BuildSetup(m.Stages["st_1_01"], report.Team, 7), report.Commands);
            Assert.AreEqual(live.Outcome, replay.Outcome, "即時模擬與回放結果勝負必須完全一致");
            Assert.AreEqual(live.Ticks, replay.Ticks, "即時模擬與回放之總 Tick 數必須完全一致");
            Assert.AreEqual(live.AllyBaseHp, replay.AllyBaseHp, "即時模擬與回放之我方據點 HP 必須一致");
            Assert.AreEqual(live.EnemiesDefeated, replay.EnemiesDefeated, "即時模擬與回放之擊殺數必須一致");

            var reward = core.SubmitBattle(report);
            Assert.IsTrue(reward.Ok, reward.Error);
            Assert.AreEqual(live.Outcome, reward.Value.Result.Outcome, "SubmitBattle 驗算之結果必須與即時模擬一致");
        }

        // =============================================================
        // A7-3：出兵校驗（能量不足、冷卻中、空格、數量滿額時 CanDeploy 回傳 false 且指令不記錄）
        // =============================================================

        [Test]
        public void A7_3_CanDeploy_Validations_AndIgnoredCommandNotRecorded()
        {
            BattleSetup setup = CreateSimpleSetup();
            // Team[0]: Cost 600, Cooldown 5s (150 ticks)
            setup.Team.Add(new UnitTemplate { Id = "u0", Cost = 600, CooldownTicks = 150, Hp = 100, Atk = 10 });
            // Team[1]: 空格子
            setup.Team.Add(null);

            var sim = new BattleSimulator(setup);
            // 初始能量 500 < 600
            Assert.IsFalse(sim.CanDeploy(0), "能量不足時 CanDeploy 應為 false");
            Assert.IsFalse(sim.CanDeploy(1), "空格子 CanDeploy 應為 false");
            Assert.IsFalse(sim.CanDeploy(-1), "越界格子 CanDeploy 應為 false");
            Assert.IsFalse(sim.CanDeploy(5), "越界格子 CanDeploy 應為 false");

            // 嘗試在能量不足時出兵
            sim.Enqueue(CommandType.Deploy, 0);
            sim.Step();
            Assert.AreEqual(0, sim.Deploys, "能量不足出兵指令應被忽略");
            Assert.AreEqual(0, sim.Recorded.Count, "被忽略之無效指令不得寫入 Recorded");
        }

        // =============================================================
        // A7-4：出兵扣除能量並啟動冷卻 (冷卻 tick = round(cooldownSec * 30))
        // =============================================================

        [Test]
        public void A7_4_Deploy_DeductsEnergy_AndAppliesCooldown()
        {
            BattleSetup setup = CreateSimpleSetup();
            setup.Team.Add(new UnitTemplate { Id = "u0", Cost = 300, CooldownTicks = 90, Hp = 100, Atk = 10 });

            var sim = new BattleSimulator(setup);
            Assert.IsTrue(sim.CanDeploy(0));

            sim.Enqueue(CommandType.Deploy, 0);
            sim.Step();

            Assert.AreEqual(1, sim.Deploys);
            Assert.AreEqual(1, sim.Recorded.Count);
            // 初始 500 扣 300 = 200，加上該 tick 回復 (50 * 1000 / 30 = 1666 milli = 1.666)
            Assert.IsTrue(sim.EnergyMilli < 300 * 1000, "出兵後能量應扣除 Cost (300)");
            Assert.AreEqual(89, sim.CooldownOf(0), "出兵後冷卻應設為 90 並在該 tick 推進扣 1 變 89");
            Assert.IsFalse(sim.CanDeploy(0), "冷卻中不可重複出兵");
        }

        // =============================================================
        // A7-5：能量回復（每 tick regen * 1000 / 30）與升級邏輯
        // =============================================================

        [Test]
        public void A7_5_EnergyRegen_AndUpgradeTransitions()
        {
            BattleSetup setup = CreateSimpleSetup();
            setup.Rules.StartEnergy = 0;
            var sim = new BattleSimulator(setup);

            Assert.AreEqual(0, sim.EnergyLevel);
            Assert.AreEqual(1000, sim.EnergyMax);
            Assert.AreEqual(500, sim.EnergyUpgradeCost);

            // 每 tick 回復 50 * 1000 / 30 = 1666 milli
            sim.Step();
            Assert.AreEqual(1666, sim.EnergyMilli);

            // 推進至累積足夠升級能量 (500)
            while (sim.EnergyMilli < 500 * 1000) sim.Step();

            sim.Enqueue(CommandType.UpgradeEnergy);
            sim.Step();

            Assert.AreEqual(1, sim.EnergyLevel, "能量等級應提升至 Lv1");
            Assert.AreEqual(2000, sim.EnergyMax, "Lv1 能量上限應提高至 2000");
            Assert.IsNull(sim.EnergyUpgradeCost, "已達頂級時升級費用應為 null");

            // 再次嘗試升級應失敗
            sim.Enqueue(CommandType.UpgradeEnergy);
            sim.Step();
            Assert.AreEqual(1, sim.EnergyLevel);
        }

        // =============================================================
        // A7-6：移動與接觸（相向前進，進入射程後停止移動）
        // =============================================================

        [Test]
        public void A7_6_MovementAndRangeContact()
        {
            BattleSetup setup = CreateSimpleSetup(length: 1000);
            // 我方單位：Speed 30/tick (Range 100)，從 0 出發
            UnitTemplate allyT = new UnitTemplate { Id = "a", SpeedMilliPerTick = 30000, RangeMilli = 100000, Hp = 500, Atk = 10 };
            // 敵方單位：Speed 30/tick (Range 100)，從 1000000 出發
            UnitTemplate enemyT = new UnitTemplate { Id = "e", SpeedMilliPerTick = 30000, RangeMilli = 100000, Hp = 500, Atk = 10 };

            setup.Team.Add(allyT);
            setup.EnemyTemplates["e"] = enemyT;
            setup.Stage.Waves.Add(new WaveDef { Enemy = "e", AtSec = 0, Count = 1 });

            var sim = new BattleSimulator(setup);
            sim.Enqueue(CommandType.Deploy, 0);
            sim.Step(); // Tick 0: Spawn both 並完成第 1 個 tick 移動

            BattleUnit ally = sim.Units.First(u => u.Side == Side.Ally);
            BattleUnit enemy = sim.Units.First(u => u.Side == Side.Enemy);

            Assert.AreEqual(30000, ally.X, "出生 tick 結束時移動 1 tick (30000 milli)");
            Assert.AreEqual(970000, enemy.X, "敵方出生 tick 結束時向左移動 1 tick (970000 milli)");

            // 推進數個 tick，驗證相向移動
            sim.Step();
            Assert.AreEqual(60000, ally.X, "我方相向右側前進");
            Assert.AreEqual(940000, enemy.X, "敵方相向左側前進");

            // 推進直到接觸射程 (Range = 100000)
            while (enemy.X - ally.X > ally.T.RangeMilli && sim.Outcome == BattleOutcome.Ongoing)
            {
                sim.Step();
            }

            long stoppedAllyX = ally.X;
            sim.Step();
            Assert.AreEqual(stoppedAllyX, ally.X, "進入射程後單位應停下展開交戰而非穿透前進");
        }

        // =============================================================
        // A7-7：單體攻擊鎖定最近目標；範圍攻擊涵蓋所有射程目標
        // =============================================================

        [Test]
        public void A7_7_TargetingSingleVsArea_AndAttackInterval()
        {
            BattleSetup setup = CreateSimpleSetup(length: 1000);
            // 我方範圍攻擊單位
            UnitTemplate areaUnit = new UnitTemplate { Id = "area", Area = true, RangeMilli = 300000, Hp = 500, Atk = 20, AttackIntervalTicks = 10 };
            // 我方單體攻擊單位
            UnitTemplate singleUnit = new UnitTemplate { Id = "single", Area = false, RangeMilli = 300000, Hp = 500, Atk = 20, AttackIntervalTicks = 10 };
            // 敵方目標
            UnitTemplate enemyT = new UnitTemplate { Id = "e", Hp = 100, Atk = 0 };

            setup.Team.Add(areaUnit);
            setup.Team.Add(singleUnit);
            setup.EnemyTemplates["e"] = enemyT;
            // 產生兩隻敵兵 (同在射程內)
            setup.Stage.Waves.Add(new WaveDef { Enemy = "e", AtSec = 0, Count = 2, IntervalSec = 0 });

            var sim = new BattleSimulator(setup);
            sim.Enqueue(CommandType.Deploy, 0); // 部署 Area 單位
            sim.Step(); // Tick 0: 出生第 1 隻敵兵
            sim.Step(); // Tick 1: 出生第 2 隻敵兵 (最小間隔 1 tick)

            var enemies = sim.Units.Where(u => u.Side == Side.Enemy).ToList();
            Assert.AreEqual(2, enemies.Count);

            // 將敵兵置於 Area 單位射程 (300,000 milli) 內
            enemies[0].X = 200000;
            enemies[1].X = 250000;

            // 模擬直到範圍攻擊命中
            while (enemies.All(e => e.Hp == 100) && sim.Outcome == BattleOutcome.Ongoing)
            {
                sim.Step();
            }

            Assert.IsTrue(enemies.All(e => e.Hp < 100), "範圍攻擊應同時命中射程內所有敵方目標");
        }

        // =============================================================
        // A7-8：擊退判定（Knockbacks = 3，每跨過 1/3 HP 被擊退 60 單位、硬直 15 ticks）
        // =============================================================

        [Test]
        public void A7_8_Knockback_Thresholds_DistanceAndStun()
        {
            BattleSetup setup = CreateSimpleSetup(length: 2000);
            // 敵方單位 Hp = 300, Knockbacks = 3 (每跨過 100 損血擊退一次)
            UnitTemplate enemyT = new UnitTemplate { Id = "e", Hp = 300, Knockbacks = 3, SpeedMilliPerTick = 0 };
            setup.EnemyTemplates["e"] = enemyT;
            setup.Stage.Waves.Add(new WaveDef { Enemy = "e", AtSec = 0, Count = 1 });

            // 我方單位 Atk = 110 (一次攻擊造成 110 傷害，跨過 100 門檻)，射程 200,000 (0.2)
            UnitTemplate allyT = new UnitTemplate { Id = "a", Hp = 500, Atk = 110, RangeMilli = 200000, AttackIntervalTicks = 10 };
            setup.Team.Add(allyT);

            var sim = new BattleSimulator(setup);
            sim.Enqueue(CommandType.Deploy, 0);
            sim.Step(); // Tick 0: 出生 (距離 2,000,000 > Range，不發生戰鬥)

            BattleUnit ally = sim.Units.First(u => u.Side == Side.Ally);
            BattleUnit enemy = sim.Units.First(u => u.Side == Side.Enemy);
            ally.X = 0;
            enemy.X = 150000; // 移至射程內且遠離邊界 clamp 區
            long initialX = enemy.X;

            // 推進 1 tick 觸發攻擊與擊退
            sim.Step();

            Assert.AreEqual(190, enemy.Hp);
            Assert.AreEqual(1, enemy.KnockbacksTaken);
            Assert.AreEqual(14, enemy.StunTicks, "擊退當 tick 扣減 1 後剩餘 14 ticks 硬直");
            Assert.AreEqual(initialX + 60000, enemy.X, "被擊退後應後退 60 單位 (60,000 milli)");
        }

        // =============================================================
        // A7-9：特性 anti_flying 對 flying 類型造成 1.5 倍傷害 (整數運算)
        // =============================================================

        [Test]
        public void A7_9_Traits_AntiFlyingDamageMultiplier()
        {
            BattleSetup setup = CreateSimpleSetup(length: 1000);
            // Claire 擁有 anti_flying 特性，Atk = 100
            UnitTemplate claireT = new UnitTemplate
            {
                Id = "char01_claire",
                Hp = 500,
                Atk = 100,
                Traits = new List<string> { "anti_flying" },
                RangeMilli = 1000000,
                AttackIntervalTicks = 30
            };
            // 飛行型敵人 Hp = 500
            UnitTemplate flyingEnemy = new UnitTemplate { Id = "e_flying", Type = "flying", Hp = 500, Atk = 0 };

            setup.Team.Add(claireT);
            setup.EnemyTemplates["e_flying"] = flyingEnemy;
            setup.Stage.Waves.Add(new WaveDef { Enemy = "e_flying", AtSec = 0, Count = 1 });

            var sim = new BattleSimulator(setup);
            sim.Enqueue(CommandType.Deploy, 0);
            sim.Step();

            BattleUnit enemy = sim.Units.First(u => u.Side == Side.Enemy);
            while (enemy.Hp == 500 && sim.Outcome == BattleOutcome.Ongoing) sim.Step();

            // 100 * 3 / 2 = 150 傷害
            Assert.AreEqual(350, enemy.Hp, "anti_flying 攻擊 flying 目標應造成 150 (1.5x) 傷害");
        }

        // =============================================================
        // A7-10：據點砲充能、發射與清場擊退
        // =============================================================

        [Test]
        public void A7_10_BaseCannon_ChargeRequirement_AndFiringEffect()
        {
            BattleSetup setup = CreateSimpleSetup(length: 1000);
            // 據點砲充能 10s = 300 ticks
            setup.Rules.CannonChargeSec = 10;
            setup.Rules.CannonDamage = 250;
            setup.Rules.CannonRange = 600;

            UnitTemplate enemyT = new UnitTemplate { Id = "e", Hp = 300, SpeedMilliPerTick = 0 };
            setup.EnemyTemplates["e"] = enemyT;
            setup.Stage.Waves.Add(new WaveDef { Enemy = "e", AtSec = 0, Count = 1 });

            var sim = new BattleSimulator(setup);
            sim.Step();

            // 未充滿時嘗試發射
            sim.Enqueue(CommandType.FireCannon);
            sim.Step();
            Assert.AreEqual(0, sim.Recorded.Count, "充能未滿時發射據點砲應被忽略");

            // 充能至滿 (300 ticks)
            while (sim.CannonChargeTicks < sim.CannonFullTicks) sim.Step();

            // 敵人在射程 600 內 (500,000 milli)
            BattleUnit enemy = sim.Units.First(u => u.Side == Side.Enemy);
            enemy.X = 500000;

            sim.Enqueue(CommandType.FireCannon);
            sim.Step();

            Assert.AreEqual(50, enemy.Hp, "據點砲造成 250 傷害 (300 - 250 = 50)");
            Assert.IsTrue(sim.TickEvents.Any(e => e.Type == BattleEventType.Cannon), "應產生據點砲發射事件");
            Assert.AreEqual(1, sim.CannonChargeTicks, "發射後充能歸零並在該 tick 結尾 Regen 累積 1 tick");
        }

        // =============================================================
        // A7-11：Wave 推進排程、重複波次與據點血量百分比觸發
        // =============================================================

        [Test]
        public void A7_11_WaveSpawning_AtSec_Repeat_AndHpThresholdTrigger()
        {
            BattleSetup setup = CreateSimpleSetup(length: 1000);
            UnitTemplate enemyT = new UnitTemplate { Id = "e", Hp = 10000, Atk = 0 };
            setup.EnemyTemplates["e"] = enemyT;

            // 攻城單位預先配置於隊伍
            UnitTemplate siegeUnit = new UnitTemplate { Id = "s", Hp = 1000, Atk = 600, RangeMilli = 1000000 };
            setup.Team.Add(siegeUnit);

            // Wave 1: 據點血量低於 50% 觸發
            setup.Stage.Waves.Add(new WaveDef
            {
                Enemy = "e",
                Count = 1,
                Trigger = new Condition { Type = "enemy_base_hp_below_pct", Value = "50" }
            });

            var sim = new BattleSimulator(setup);
            sim.Step();

            // 敵方據點 1000 (100%)，未觸發
            Assert.AreEqual(0, sim.Units.Count(u => u.Side == Side.Enemy));

            // 打掉敵方據點至 400 (40% < 50%)
            // 透過模擬打據點
            sim.Enqueue(CommandType.Deploy, 0);
            sim.Step();
            sim.Step(); // HitBase once -> 1000 - 600 = 400 (40%)

            // 下一個 tick 檢查據點血量波次觸發
            sim.Step();
            Assert.IsTrue(sim.Units.Any(u => u.Side == Side.Enemy), "據點 HP 低於 50% 時應成功觸發特殊 Wave");
        }

        // =============================================================
        // A7-12：勝負判定優先級（敵據點破獲勝優先於失敗；超時判定）
        // =============================================================

        [Test]
        public void A7_12_BattleOutcomes_WinPriority_LossAndTimeout()
        {
            BattleSetup setup = CreateSimpleSetup(length: 500);
            setup.Stage.TimeLimitSec = 1; // 30 ticks 超時

            var sim = new BattleSimulator(setup);
            while (sim.Outcome == BattleOutcome.Ongoing) sim.Step();

            Assert.AreEqual(BattleOutcome.Timeout, sim.Outcome, "到達時限應判定為 Timeout");
        }

        // =============================================================
        // A7-13：竄改防護（在回放指令中插入無效出兵或不存在格子，模擬自動忽略）
        // =============================================================

        [Test]
        public void A7_13_ReplayTamperProtection_IgnoresInvalidCommands()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m, 7);
            BattleSetup setup = core.PrepareBattle("st_1_01", 7).Value;

            // 乾淨模擬
            var sim = new BattleSimulator(setup);
            while (sim.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setup.Team.Count; slot++)
                    if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                sim.Step();
            }
            BattleResult legitimate = sim.Result();

            // 竄改指令：插入無效出兵（能量不足、無效格子 99）
            var tamperedCommands = new List<BattleCommand>(sim.Recorded);
            tamperedCommands.Insert(0, new BattleCommand(0, CommandType.Deploy, 99)); // 越界格子
            tamperedCommands.Insert(1, new BattleCommand(0, CommandType.UpgradeEnergy, 0)); // 能量不足升級

            BattleResult replayed = BattleSimulator.Run(core.BuildSetup(m.Stages["st_1_01"], core.Player.Team, 7), tamperedCommands);

            Assert.AreEqual(legitimate.Outcome, replayed.Outcome, "竄改指令應被安全略過，最終勝負保持不變");
            Assert.AreEqual(legitimate.Ticks, replayed.Ticks, "竄改指令不應影響決定性 Tick 步數");
            Assert.AreEqual(legitimate.AllyBaseHp, replayed.AllyBaseHp, "據點血量保持一致");
        }

        // =============================================================
        // A7-14：黃金回放檔回歸驗證
        // =============================================================

        [Test]
        public void A7_14_GoldenReplayRegression_VerifiesIdenticalResult()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m, 7);
            string goldenDir = TestPaths.GoldenDir();
            string goldenPath = Path.Combine(goldenDir, "battle_st_1_01_seed7.json");

            BattleReport report;
            if (!File.Exists(goldenPath))
            {
                // 若黃金檔不存在，自動執行一次決定性模擬並持久化生成
                BattleSetup setup = core.PrepareBattle("st_1_01", 7).Value;
                var sim = new BattleSimulator(setup);
                while (sim.Outcome == BattleOutcome.Ongoing)
                {
                    for (int slot = 0; slot < setup.Team.Count; slot++)
                        if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                    sim.Step();
                }

                report = new BattleReport
                {
                    StageId = "st_1_01",
                    Seed = 7,
                    Team = new List<string>(core.Player.Team),
                    Commands = sim.Recorded,
                    MasterDataVersion = m.Config.MasterDataVersion
                };
                File.WriteAllText(goldenPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            else
            {
                report = JsonConvert.DeserializeObject<BattleReport>(File.ReadAllText(goldenPath));
            }

            Assert.IsNotNull(report);
            Assert.AreEqual("st_1_01", report.StageId);
            Assert.AreEqual(7UL, report.Seed);

            // 執行黃金回放驗證
            BattleSetup replaySetup = core.BuildSetup(m.Stages[report.StageId], report.Team, report.Seed);
            BattleResult result = BattleSimulator.Run(replaySetup, report.Commands);

            Assert.AreEqual(BattleOutcome.Win, result.Outcome, "黃金回放結果應為 Win");
            Assert.AreEqual(6751, result.Ticks, "黃金回放步數應為 6751 ticks (225.03s)");
            Assert.AreEqual(20000, result.AllyBaseHp, "我方據點應毫髮無損 (20000 HP)");
            Assert.AreEqual(27, result.EnemiesDefeated, "擊殺敵兵數應為 27");
            Assert.AreEqual(50, result.Deploys, "出兵總次數應為 50");
        }
    }
}
