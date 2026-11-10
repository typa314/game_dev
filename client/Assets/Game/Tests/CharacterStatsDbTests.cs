using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Game.Core;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.Growth;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 驗證 data/stats/character_stats_db.{json,csv,sqlite,schema.json} 與即時 MasterData、
    /// GrowthRules 及 BattleSimulator 運算結果完全同步且零漂移。
    /// </summary>
    [TestFixture]
    public class CharacterStatsDbTests
    {
        private string _repoRoot;
        private string _statsDir;
        private MasterDatabase _master;
        private GrowthRules _growth;
        private JObject _db;

        [SetUp]
        public void SetUp()
        {
            _repoRoot = TestPaths.RepoRootDir();
            _statsDir = Path.Combine(TestPaths.DataDir(), "stats");
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            _growth = new GrowthRules(_master.Config.Growth);
            _db = JObject.Parse(File.ReadAllText(Path.Combine(_statsDir, "character_stats_db.json")));
        }

        [Test]
        public void StatsDb_FilesExist_And_SourceHashesMatchLiveMasterData()
        {
            Assert.IsTrue(File.Exists(Path.Combine(_statsDir, "character_stats_db.schema.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_statsDir, "character_stats_db.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_statsDir, "character_stats_db.csv")));
            Assert.IsTrue(new FileInfo(Path.Combine(_statsDir, "character_stats_db.sqlite")).Length > 0);

            var hashes = (JObject)_db["source_master_hashes"];
            Assert.IsNotNull(hashes);
            foreach (JProperty prop in hashes.Properties())
            {
                string absPath = Path.Combine(_repoRoot, prop.Name.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(absPath), $"來源檔案不存在: {prop.Name}");
                string actualSha = ComputeSha256Hex(absPath);
                Assert.AreEqual(prop.Value.ToString(), actualSha, $"來源檔案 {prop.Name} 已變更，請重新執行 tools/build_character_stats_db.py");
            }
        }

        [Test]
        public void StatsDb_AlliesMatchMasterData_GrowthRules_And_HealerAura()
        {
            var allies = (JArray)_db["allies"];
            Assert.AreEqual(_master.Characters.Count, allies.Count, "我方角色數量應與 characters.json 完全一致");

            int tps = _master.Config.Battle.TicksPerSecond;
            var seenIds = new HashSet<string>();

            foreach (JObject entry in allies)
            {
                string id = entry["id"].ToString();
                Assert.IsTrue(seenIds.Add(id), $"重複的我方角色 ID: {id}");
                CharacterDef def = _master.Characters.GetOrNull(id);
                Assert.IsNotNull(def, $"characters.json 找不到角色 {id}");

                Assert.AreEqual("ally", entry["side"].ToString());
                Assert.AreEqual(def.Rarity, entry["rarity"].ToString());
                Assert.AreEqual(def.Role, entry["role"].ToString());

                UnitTemplate baseUt = UnitTemplate.From(def.Id, "normal", def.Battle, def.Battle.Hp, def.Battle.Atk, tps);
                JObject baseStats = (JObject)entry["base_stats"];
                Assert.AreEqual(baseUt.Cost, baseStats["cost"].Value<int>());
                Assert.AreEqual(baseUt.Hp, baseStats["hp"].Value<int>());
                Assert.AreEqual(baseUt.Atk, baseStats["atk"].Value<int>());
                Assert.AreEqual(baseUt.RangeMilli, baseStats["range_milli"].Value<long>());
                Assert.AreEqual(baseUt.AttackIntervalTicks, baseStats["attack_interval_ticks"].Value<int>());
                Assert.AreEqual(baseUt.SpeedMilliPerTick, baseStats["speed_milli_per_tick"].Value<long>());
                Assert.AreEqual(baseUt.CooldownTicks, baseStats["cooldown_ticks"].Value<int>());
                Assert.AreEqual(baseUt.Knockbacks, baseStats["knockbacks"].Value<int>());

                bool expectedHealer = BattleSimulator.IsHealer(new BattleUnit { T = baseUt });
                Assert.AreEqual(expectedHealer, entry["healer_aura"]["is_healer"].Value<bool>(), $"{id} 補師光環判定不一致");

                var snapshots = (JObject)entry["scaling_snapshots"];
                foreach (JProperty tierProp in snapshots.Properties())
                {
                    var snap = (JObject)tierProp.Value;
                    int lv = snap["level"].Value<int>();
                    int aw = snap["awaken"].Value<int>();
                    string stageId = snap["portrait_stage_id"].ToString();
                    PortraitStageDef ps = _master.PortraitStages.GetOrNull(stageId);
                    ComputedStats computed = _growth.ComputeStats(def, new CharacterState { Level = lv, Awaken = aw }, ps);

                    Assert.AreEqual(computed.Hp, snap["hp"].Value<int>(), $"{id} @ {tierProp.Name} HP 不一致");
                    Assert.AreEqual(computed.Atk, snap["atk"].Value<int>(), $"{id} @ {tierProp.Name} ATK 不一致");
                    Assert.AreEqual(expectedHealer ? computed.Atk : 0, snap["aura_heal_per_pulse"].Value<int>(), $"{id} @ {tierProp.Name} 治療量不一致");
                }

                string spriteRel = entry["assets"]["battle_sprite_path"].ToString();
                Assert.IsTrue(entry["assets"]["battle_sprite_exists"].Value<bool>(), $"{id} 戰鬥立繪旗標應為 true");
                Assert.IsTrue(File.Exists(Path.Combine(_repoRoot, spriteRel.Replace('/', Path.DirectorySeparatorChar))), $"缺少立繪: {spriteRel}");
            }
        }

        [Test]
        public void StatsDb_EnemiesMatchMasterData_Factions_And_StageScalings()
        {
            var enemies = (JArray)_db["enemies"];
            Assert.AreEqual(_master.Enemies.Count, enemies.Count, "敵方角色數量應與 enemies.json 完全一致");

            int tps = _master.Config.Battle.TicksPerSecond;
            var factionCounts = new Dictionary<string, int> { { "dark_elf", 0 }, { "goblin", 0 }, { "monster", 0 } };

            foreach (JObject entry in enemies)
            {
                string id = entry["id"].ToString();
                EnemyDef def = _master.Enemies.GetOrNull(id);
                Assert.IsNotNull(def, $"enemies.json 找不到敵人 {id}");

                string faction = entry["faction"].ToString();
                Assert.IsTrue(factionCounts.ContainsKey(faction), $"未知敵方陣營: {faction}");
                factionCounts[faction]++;

                Assert.AreEqual("enemy", entry["side"].ToString());
                Assert.AreEqual(def.Type, entry["unit_type"].ToString());
                Assert.AreEqual(def.DefeatGold, entry["defeat_gold"].Value<int>());

                UnitTemplate baseUt = UnitTemplate.From(def.Id, def.Type, def.Battle, def.Battle.Hp, def.Battle.Atk, tps);
                JObject baseStats = (JObject)entry["base_stats"];
                Assert.AreEqual(baseUt.Hp, baseStats["hp"].Value<int>());
                Assert.AreEqual(baseUt.Atk, baseStats["atk"].Value<int>());
                Assert.AreEqual(baseUt.RangeMilli, baseStats["range_milli"].Value<long>());
                Assert.AreEqual(baseUt.AttackIntervalTicks, baseStats["attack_interval_ticks"].Value<int>());
                Assert.AreEqual(baseUt.SpeedMilliPerTick, baseStats["speed_milli_per_tick"].Value<long>());

                foreach (JObject app in (JArray)entry["stage_appearances"])
                {
                    int pct = app["enemy_stat_pct"].Value<int>();
                    int expectedHp = (int)((long)def.Battle.Hp * pct / 100);
                    int expectedAtk = (int)((long)def.Battle.Atk * pct / 100);
                    Assert.AreEqual(expectedHp, app["scaled_hp"].Value<int>(), $"{id} @ {app["stage_id"]} 縮放 HP 不一致");
                    Assert.AreEqual(expectedAtk, app["scaled_atk"].Value<int>(), $"{id} @ {app["stage_id"]} 縮放 ATK 不一致");
                }

                string spriteRel = entry["assets"]["battle_sprite_path"].ToString();
                Assert.IsTrue(entry["assets"]["battle_sprite_exists"].Value<bool>(), $"{id} 敵方戰鬥立繪旗標應為 true");
                Assert.IsTrue(File.Exists(Path.Combine(_repoRoot, spriteRel.Replace('/', Path.DirectorySeparatorChar))), $"缺少敵方立繪: {spriteRel}");
            }

            Assert.GreaterOrEqual(factionCounts["dark_elf"], 2, "暗精靈陣營至少 2 隻");
            Assert.GreaterOrEqual(factionCounts["goblin"], 2, "哥布林陣營至少 2 隻");
            Assert.GreaterOrEqual(factionCounts["monster"], 5, "魔物陣營至少 5 隻");
        }

        [Test]
        public void StatsDb_BastionLevelsMatchGrowthRules_And_FullDnaRosterAccountedFor()
        {
            var allyBastion = (JObject)_db["bases"]["ally_bastion"];
            var levels = (JArray)allyBastion["levels"];
            Assert.AreEqual(_growth.BaseMaxLevel, levels.Count);

            int defaultBaseHp = _master.Config.Battle.DefaultAllyBaseHp;
            foreach (JObject lvEntry in levels)
            {
                int lv = lvEntry["level"].Value<int>();
                int expectedHp = _growth.ComputeBaseMaxHp(lv, defaultBaseHp);
                Assert.AreEqual(expectedHp, lvEntry["max_hp"].Value<int>(), $"主堡 Lv.{lv} 血量不一致");

                if (lv >= _growth.BaseMaxLevel)
                {
                    Assert.AreEqual(JTokenType.Null, lvEntry["upgrade_gold_cost_to_next"].Type);
                }
                else
                {
                    Assert.AreEqual(_growth.BaseUpgradeGoldCost(lv), lvEntry["upgrade_gold_cost_to_next"].Value<int>());
                }
            }

            int totalDna = ((JArray)_db["allies"]).Count + ((JArray)_db["unimplemented_dna_roster"]).Count;
            Assert.AreEqual(50, totalDna, "完整角色庫（現役+待開發）應涵蓋 presets/characters_dna.json 全部 50 位角色");
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(filePath))
            {
                byte[] digest = sha.ComputeHash(fs);
                return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
