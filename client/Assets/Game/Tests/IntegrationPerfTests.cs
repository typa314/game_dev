using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.Core;
using Game.Core.Persistence;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 工單 E3（戰鬥效能）與 E4（存檔大小與速度）驗證測試
    /// </summary>
    [TestFixture]
    public class IntegrationPerfTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PerfTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        // =========================================================================================
        // E3: 戰鬥效能驗證
        // E3-1: 雙方各 50 個單位、900 秒（27,000 tick）的戰鬥，記錄在本機 .NET 上的實際耗時
        // E3-2: 平均每個 tick 的耗時 < 1 ms
        // =========================================================================================
        [Test]
        public void E3_BattlePerformance_50v50_27000Ticks_Under1msPerTick()
        {
            // 建立高耐久戰場與大量單位波次以維持雙方 50 單位滿額戰鬥 900 秒 (27,000 ticks)
            var stage = new StageDef
            {
                Id = "st_perf_50v50",
                Chapter = 99,
                Name = "Performance Stage",
                Bg = "battle/bg/city_day",
                Bgm = "bgm/battle_01",
                Length = 3000,
                AllyBaseHp = 100_000_000,
                EnemyBaseHp = 100_000_000,
                TimeLimitSec = 900,
                EnemyStatPct = 100,
                Waves = new List<WaveDef>
                {
                    new WaveDef
                    {
                        Enemy = "en_slime",
                        AtSec = 0,
                        Count = 10,
                        IntervalSec = 1,
                        RepeatEverySec = 5
                    },
                    new WaveDef
                    {
                        Enemy = "en_bat",
                        AtSec = 2,
                        Count = 10,
                        IntervalSec = 1,
                        RepeatEverySec = 5
                    }
                },
                StarConditions = new List<string> { "clear" },
                UnlocksOnClear = new List<string>()
            };

            var rules = new BattleRules
            {
                TicksPerSecond = 30,
                DefaultAllyBaseHp = 100_000_000,
                MaxUnitsPerSide = 50,
                MaxBattleSec = 900,
                CannonChargeSec = 40,
                CannonDamage = 300,
                CannonRange = 1500,
                KnockbackDistance = 60,
                EnergyMaxByLevel = new List<int> { 50000, 50000 },
                EnergyRegenPerSecByLevel = new List<int> { 10000, 10000 },
                EnergyUpgradeCost = new List<int> { 100 },
                StartEnergy = 50000
            };

            // 隊伍包含低冷卻、多角色陣容以快速補滿 50 單位
            var team = new List<UnitTemplate>();
            for (int i = 0; i < 5; i++)
            {
                team.Add(new UnitTemplate
                {
                    Id = "unit_" + i,
                    Type = "normal",
                    Cost = 10,
                    CooldownTicks = 5,
                    Hp = 50000,
                    Atk = 10,
                    RangeMilli = 120 * 1000,
                    AttackIntervalTicks = 30,
                    SpeedMilliPerTick = 300,
                    Knockbacks = 1,
                    Area = false,
                    Traits = new List<string>()
                });
            }

            var setup = new BattleSetup
            {
                Stage = stage,
                Rules = rules,
                Seed = 9999,
                Team = team
            };

            MasterDatabase master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            foreach (WaveDef w in stage.Waves)
            {
                if (setup.EnemyTemplates.ContainsKey(w.Enemy)) continue;
                EnemyDef e = master.Enemies[w.Enemy];
                int hp = (int)((long)e.Battle.Hp * stage.EnemyStatPct / 100);
                int atk = (int)((long)e.Battle.Atk * stage.EnemyStatPct / 100);
                setup.EnemyTemplates[w.Enemy] = UnitTemplate.From(e.Id, e.Type, e.Battle, hp, atk, rules.TicksPerSecond);
            }

            var sim = new BattleSimulator(setup);

            const int totalTicks = 27000; // 900 秒 * 30 ticks/s
            var sw = Stopwatch.StartNew();

            int maxAlliesSeen = 0;
            int maxEnemiesSeen = 0;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                // 盡可能出兵維持 50 單位上限
                for (int slot = 0; slot < team.Count; slot++)
                {
                    if (sim.CanDeploy(slot))
                    {
                        sim.Enqueue(CommandType.Deploy, slot);
                    }
                }

                sim.Step();

                int allies = 0;
                int enemies = 0;
                for (int u = 0; u < sim.Units.Count; u++)
                {
                    if (sim.Units[u].Side == Side.Ally) allies++;
                    else enemies++;
                }
                if (allies > maxAlliesSeen) maxAlliesSeen = allies;
                if (enemies > maxEnemiesSeen) maxEnemiesSeen = enemies;
            }

            sw.Stop();

            double totalMs = sw.Elapsed.TotalMilliseconds;
            double avgMsPerTick = totalMs / totalTicks;

            TestContext.WriteLine($"=== E3 戰鬥效能測試結果 ===");
            TestContext.WriteLine($"模擬總 Ticks: {totalTicks} (相當於 900 秒實時戰鬥)");
            TestContext.WriteLine($"場上單位峰值: 我方 {maxAlliesSeen} 單位 / 敵方 {maxEnemiesSeen} 單位 (上限 50)");
            TestContext.WriteLine($"總耗時: {totalMs:F2} ms");
            TestContext.WriteLine($"平均每 Tick 耗時: {avgMsPerTick:F4} ms (標準要求 < 1.0 ms)");

            Assert.IsTrue(maxAlliesSeen >= 45, $"我方單位應達到滿額或接近上限 50，實際峰值: {maxAlliesSeen}");
            Assert.IsTrue(maxEnemiesSeen >= 45, $"敵方單位應達到滿額或接近上限 50，實際峰值: {maxEnemiesSeen}");
            Assert.Less(avgMsPerTick, 1.0, $"E3-2 查核點：平均每 Tick 耗時必須小於 1.0 ms，實際為 {avgMsPerTick:F4} ms");
        }

        // =========================================================================================
        // E4: 存檔大小與速度測試
        // E4-1: 50 位角色、200 筆轉蛋紀錄、80 個成就的 Profile，記錄存檔大小和寫入耗時
        // =========================================================================================
        [Test]
        public void E4_SaveSystemPerformance_50Chars_200Gacha_80Achievements()
        {
            var player = new PlayerState();
            player.Profile.Locale = "zh-TW";
            player.Profile.CreatedAtUtc = "2026-10-15T00:00:00Z";
            player.Profile.RatingOptIn = Rating.R18;
            player.Profile.AgeConfirmedAtUtc = "2026-10-15T00:00:00Z";

            // 1. 填入 50 位角色完整狀態
            for (int i = 1; i <= 50; i++)
            {
                string charId = $"char_{i:D2}_hero";
                player.Characters[charId] = new CharacterState
                {
                    CurrentRarity = i > 40 ? "UR" : i > 25 ? "SSR" : i > 10 ? "SR" : "R",
                    Tier = (i % 6),
                    Level = 1 + (i % 60),
                    Exp = i * 150,
                    Awaken = (i % 5),
                    Affection = i * 20,
                    PortraitStage = $"ps_char_{i:D2}_s1",
                    Costume = (i % 2 == 0) ? $"cos_char_{i:D2}_default" : null,
                    UnlockedPortraitStages = new HashSet<string> { $"ps_char_{i:D2}_s1", $"ps_char_{i:D2}_s2" }
                };
            }

            // 2. 填入 200 筆轉蛋歷史紀錄
            for (int i = 1; i <= 200; i++)
            {
                player.Gacha.History.Add(new GachaHistoryEntry
                {
                    Banner = "banner_ur",
                    Char = $"char_{(i % 50) + 1:D2}_hero",
                    Rarity = (i % 100 == 0) ? "UR" : (i % 30 == 0) ? "SSR" : (i % 10 == 0) ? "SR" : "R",
                    AtUtc = $"2026-10-15T12:{i % 60:D2}:00Z"
                });
            }

            // 3. 填入 80 個成就追蹤資料
            for (int i = 1; i <= 80; i++)
            {
                string achId = $"ach_perf_{i:D3}";
                player.Achievements[achId] = new AchievementProgress
                {
                    Progress = i * 5,
                    UnlockedAtUtc = (i % 2 == 0) ? $"2026-10-15T10:{i % 60:D2}:00Z" : null,
                    Claimed = (i % 4 == 0)
                };
            }

            // 4. 填入道具與其他狀態
            for (int i = 1; i <= 100; i++)
            {
                player.Items[$"item_{i:D3}"] = i * 1000;
            }

            DateTime utcNow = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

            // 量測編碼（序列化 + SHA-256 Hash）耗時與大小
            var swEncode = Stopwatch.StartNew();
            string saveJson = SaveCodec.Encode(player, "0.1.0", utcNow);
            swEncode.Stop();

            byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(saveJson);
            long fileSizeBytes = utf8Bytes.Length;
            double fileSizeKb = fileSizeBytes / 1024.0;

            // 量測解碼（反序列化 + Hash 驗證）耗時
            var swDecode = Stopwatch.StartNew();
            PlayerState decodedPlayer = SaveCodec.Decode(saveJson);
            swDecode.Stop();

            // 量測磁碟寫入（含 tmp 寫入、Atomic Swap 與 3 份滾動備份）耗時
            var store = new FileSaveStore(_tempDir);
            var swDisk = Stopwatch.StartNew();
            store.Write("profile_perf.gsave", saveJson);
            swDisk.Stop();

            // 量測從磁碟讀回並驗證耗時
            var errors = new List<string>();
            var swRead = Stopwatch.StartNew();
            PlayerState diskLoaded = store.ReadWithFallback("profile_perf.gsave", SaveCodec.Decode, errors);
            swRead.Stop();

            TestContext.WriteLine($"=== E4 存檔大小與效能測試結果 ===");
            TestContext.WriteLine($"測試資料規模: 50 角色、200 筆轉蛋歷史、80 個成就追蹤、100 種道具");
            TestContext.WriteLine($"存檔大小: {fileSizeBytes} bytes ({fileSizeKb:F2} KB)");
            TestContext.WriteLine($"序列化與 Hash 耗時: {swEncode.Elapsed.TotalMilliseconds:F3} ms");
            TestContext.WriteLine($"反序列化與驗證耗時: {swDecode.Elapsed.TotalMilliseconds:F3} ms");
            TestContext.WriteLine($"磁碟寫入 (含原子置換與備份輪替) 耗時: {swDisk.Elapsed.TotalMilliseconds:F3} ms");
            TestContext.WriteLine($"磁碟回讀與驗證耗時: {swRead.Elapsed.TotalMilliseconds:F3} ms");

            Assert.AreEqual(50, decodedPlayer.Characters.Count);
            Assert.AreEqual(200, decodedPlayer.Gacha.History.Count);
            Assert.AreEqual(80, decodedPlayer.Achievements.Count);
            Assert.That(errors, Is.Empty);
            Assert.IsNotNull(diskLoaded);

            // 效能斷言：50 角色規模存檔大小應小於 500 KB，寫入耗時應在 50 ms 內
            Assert.Less(fileSizeKb, 500.0, "存檔大小應低於 500 KB");
            Assert.Less(swEncode.Elapsed.TotalMilliseconds, 50.0, "序列化耗時應低於 50 ms");
            Assert.Less(swDisk.Elapsed.TotalMilliseconds, 100.0, "磁碟安全寫入應低於 100 ms");
        }
    }
}
