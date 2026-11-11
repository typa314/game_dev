#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.MasterData;
using Game.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    [TestFixture]
    public sealed class BattlePageViewTests
    {
        private List<GameObject> _createdObjects;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_createdObjects != null)
            {
                for (int i = 0; i < _createdObjects.Count; i++)
                {
                    if (_createdObjects[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                    }
                }
                _createdObjects.Clear();
            }
        }

        private GameObject Track(GameObject go)
        {
            _createdObjects.Add(go);
            return go;
        }

        private static BattleSetup CreateTestSetup(int length = 1000)
        {
            var stage = new StageDef
            {
                Id = "st_test",
                Length = length,
                AllyBaseHp = 1000,
                EnemyBaseHp = 1000,
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
                Seed = 12345UL
            };
        }

        [Test]
        public void OPT4_1_ContinuousSimSteps_ConsumeViewEvents_CollectsAllEventsWithoutLoss()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            var skill = new CharacterSkillDef { Id = "sk_strike", Name = "打擊", ChargeSec = 1.0, EffectType = "damage", Value = 150 };
            UnitTemplate unit = new UnitTemplate { Id = "char_valkyrie", Cost = 100, CooldownTicks = 10, Hp = 500, Atk = 50, Skill = skill, SkillChargeTicks = 10 };
            setup.Team.Add(unit);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            runner.InitializeForTesting(sim, setup);

            // 1. 出兵指令
            runner.Deploy(0);

            // 2. 單一幀內連續推進 5 個 Step (模擬幀間時間積累)
            for (int i = 0; i < 5; i++)
            {
                runner.StepSim();
            }

            // 驗證累積佇列中有事件
            Assert.IsTrue(runner.PendingViewEventCount > 0, "多個 Step 產生的事件應成功累積於 Runner 緩衝中");

            // 3. 消費所有事件
            List<BattleEvent> buffer = new List<BattleEvent>();
            runner.ConsumeViewEvents(buffer);

            // 陽性查核：buffer 內應包含 Deploy 事件，且佇列清空
            Assert.IsTrue(buffer.Any(e => e.Type == BattleEventType.Deploy), "緩衝清單應包含 Deploy 戰鬥事件");
            Assert.AreEqual(0, runner.PendingViewEventCount, "ConsumeViewEvents 後佇列應完全清空");

            // 4. 再次呼叫 ConsumeViewEvents，buffer 應為空，不重送舊事件
            runner.ConsumeViewEvents(buffer);
            Assert.AreEqual(0, buffer.Count, "無新事件時 ConsumeViewEvents 應傳回空清單");
        }

        [Test]
        public void OPT4_2_SkillCast_TriggersCutIn_And_BaseImmune_TriggersAlert()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            setup.Stage.EnemyBaseTraits.Add("immune_ranged");
            setup.Stage.EnemyBaseHp = 5000;

            // Slot 0: 近戰充能技能角色
            var skill = new CharacterSkillDef { Id = "sk_valkyrie", Name = "女武神戰歌", ChargeSec = 1.0, EffectType = "damage", Value = 200 };
            UnitTemplate valkyrie = new UnitTemplate { Id = "char_valkyrie", Cost = 100, CooldownTicks = 10, Hp = 1000, Atk = 100, Skill = skill, SkillChargeTicks = 10 };
            setup.Team.Add(valkyrie);

            // Slot 1: 遠程射手 (Range = 300 > 150)
            UnitTemplate sniper = new UnitTemplate { Id = "char_sniper", Cost = 100, Hp = 500, Atk = 200, RangeMilli = 300 * 1000 };
            setup.Team.Add(sniper);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            // 1. 推進 10 ticks 使技能條充滿
            for (int i = 0; i < 10; i++) runner.StepSim();
            Assert.IsTrue(sim.IsSkillReady(0), "第 0 格技能應已滿充能");

            // 點擊施放技能
            runner.TapSlot(0);
            runner.StepSim(); // 觸發 SkillCast 事件

            // 驅動 View 更新並消費事件
            pageView.Refresh();

            // 查核 OPT4-2: 角色大招 Cut-in 面板啟動且包含技能名稱
            Assert.IsNotNull(pageView.SkillCutInPanel, "SkillCutInPanel 應被建置");
            Assert.IsTrue(pageView.SkillCutInPanel.activeSelf, "大招施放時 Cut-in 面板應為 active");
            Assert.IsNotNull(pageView.LastSkillCutInText, "LastSkillCutInText 不應為 null");
            StringAssert.Contains("女武神戰歌", pageView.LastSkillCutInText, "Cut-in 文字應包含角色大招名稱 '女武神戰歌'");

            // 2. 測試 BaseImmune 主堡免疫視覺提示
            runner.Deploy(1); // 部署遠程狙擊手
            runner.StepSim();
            BattleUnit uSniper = sim.Units.First(u => u.T.Id == "char_sniper");
            uSniper.X = 800 * 1000; // 移動至敵方免疫主堡前開火 (距離 200 單位)

            runner.StepSim(); // 產生 BaseImmune 事件
            pageView.Refresh();

            // 查核 OPT4-2: 主堡免疫警報視覺呈現
            Assert.IsNotNull(pageView.BaseAlertLabel, "BaseAlertLabel 應被建置");
            Assert.IsTrue(pageView.BaseAlertLabel.gameObject.activeSelf, "遭遇免疫時主堡警報橫幅應為 active");
            Assert.IsNotNull(pageView.LastBaseAlertText, "LastBaseAlertText 不應為 null");
            StringAssert.Contains("BASE IMMUNE", pageView.LastBaseAlertText, "警示文字應包含 'BASE IMMUNE'");
        }

        [Test]
        public void OPT4_3_BaseShockwave_TriggersShockwaveAlert()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            setup.Stage.EnemyBaseTraits.Add("shockwave_on_hp_loss");
            setup.Stage.EnemyBaseHp = 1000;

            UnitTemplate siege = new UnitTemplate { Id = "char_siege", Cost = 100, Hp = 2000, Atk = 250, RangeMilli = 100 * 1000 };
            setup.Team.Add(siege);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.Deploy(0);
            runner.StepSim();
            BattleUnit u = sim.Units.First(u => u.T.Id == "char_siege");
            u.X = 950 * 1000;

            // 攻擊主堡扣除 250 HP (> 20% 門檻)，引發 BaseShockwave
            runner.StepSim();
            pageView.Refresh();

            Assert.IsNotNull(pageView.LastBaseAlertText, "LastBaseAlertText 不應為 null");
            StringAssert.Contains("BASE SHOCKWAVE", pageView.LastBaseAlertText, "主堡觸發震波時應顯示 BASE SHOCKWAVE 警示");
            Assert.IsTrue(pageView.BaseAlertLabel.gameObject.activeSelf, "警報橫幅應為 active");
        }

        [Test]
        public void OPT4_4_UnitStatusLabels_DisplaysActiveBuffsAndDebuffs()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            UnitTemplate unit = new UnitTemplate { Id = "char_tank", Cost = 100, Hp = 1000, Atk = 50, RangeMilli = 100 * 1000 };
            setup.Team.Add(unit);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.Deploy(0);
            runner.StepSim();
            pageView.Refresh();

            BattleUnit u = sim.Units.First(u => u.T.Id == "char_tank");
            // 施加狀態
            u.AtkBuffTicks = 30;
            u.SlowTicks = 30;
            u.StunTicks = 15;

            pageView.Refresh();

            // 驗證單位視覺物件存在
            RectTransform unitRt = pageView.GetUnitTransform(u.Uid);
            Assert.IsNotNull(unitRt, "畫面中應存在 BattleUnit 物件");

            // 驗證狀態文字
            string status = pageView.GetUnitStatusText(u.Uid);
            StringAssert.Contains("[ATK↑]", status, "狀態文字應包含 [ATK↑]");
            StringAssert.Contains("[SLOW]", status, "狀態文字應包含 [SLOW]");
            StringAssert.Contains("[STUN]", status, "狀態文字應包含 [STUN]");
        }

        [Test]
        public void OPT4_5_EventQueue_CapAt256_DropsOldestOnOverflow()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            runner.InitializeForTesting(sim, setup);

            // 人工大量灌入事件以測試上限保護
            for (int i = 0; i < 300; i++)
            {
                sim.TickEvents.Add(new BattleEvent { Type = BattleEventType.EnergyUp, Value = i });
                runner.CaptureTickEvents();
                sim.TickEvents.Clear();
            }

            Assert.AreEqual(256, runner.PendingViewEventCount, "事件緩衝佇列應被嚴格限制在最多 256 筆");

            List<BattleEvent> buffer = new List<BattleEvent>();
            runner.ConsumeViewEvents(buffer);
            Assert.AreEqual(256, buffer.Count);
            // 驗證保留的是最新的事件（最舊的已被剔除）
            Assert.AreEqual(299, buffer[buffer.Count - 1].Value, "最後一筆事件應為第 299 號最新事件");
            Assert.AreEqual(44, buffer[0].Value, "第一筆事件應為第 44 號事件 (300 - 256 = 44)");
        }

        [Test]
        public void M2_1_3_VictoryWithUnlockedNextStage_ShowsNextStageButton_AndDefeatHidesIt()
        {
            MasterDatabase master = Game.Core.MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            var player = Game.Core.GameCore.NewPlayer(master, clock);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var core = new Game.Core.GameCore(master, player, new SplitMix64Random(99UL), clock, rating);
            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_m2_1_3_" + Guid.NewGuid().ToString("N"));
            var store = new Game.Core.Persistence.FileSaveStore(tempDir);
            GameServices.Install(core, store, new AssetService(rating), rating, new Game.Core.Platform.NullPlatformService(), null);

            try
            {
                GameObject runnerGo = Track(new GameObject("TestBattleRunner_M2_1_3"));
                BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
                BattlePageView pageView = runnerGo.GetComponent<BattlePageView>() ?? runnerGo.AddComponent<BattlePageView>();
                pageView.InitializeForTesting(runner);

                // 1. 啟動 st_1_01 並模擬戰敗 (陽性對照：戰敗時 NextStageButton 必須隱藏)
                Assert.IsTrue(runner.StartBattle("st_1_01"));
                // 直接將我方主堡血量扣至 0 使其戰敗
                var loseSetup = CreateTestSetup(length: 500);
                loseSetup.Stage.Id = "st_1_01";
                loseSetup.Stage.AllyBaseHp = 10;
                loseSetup.EnemyTemplates["en_killer"] = new UnitTemplate { Id = "en_killer", Hp = 500, Atk = 50, RangeMilli = 600 * 1000, AttackIntervalTicks = 1 };
                loseSetup.Stage.Waves.Add(new WaveDef { Enemy = "en_killer", AtSec = 0, Count = 1 });
                var loseSim = new BattleSimulator(loseSetup);
                runner.InitializeForTesting(loseSim, loseSetup);
                while (loseSim.Outcome == BattleOutcome.Ongoing)
                {
                    runner.StepSim();
                }
                Assert.AreEqual(BattleOutcome.Lose, loseSim.Outcome);
                runner.Submit();
                pageView.SetNextStageIdForTesting("st_1_02");
                pageView.Refresh();

                Assert.IsNotNull(pageView.NextStageButton);
                Assert.IsFalse(pageView.NextStageButton.gameObject.activeSelf, "戰敗時 NextStageButton 必須隱藏 (陽性對照)");

                // 2. 模擬勝利且下一關 st_1_02 已解鎖：NextStageButton 應顯示並可點擊進入 st_1_02
                core.Player.Stages["st_1_02"] = new Game.Domain.Player.StageProgress { Unlocked = true, Stars = 0 };
                pageView.SetNextStageIdForTesting(null); // 改由 GameServices.Core 動態解析

                var winSetup = CreateTestSetup(length: 500);
                winSetup.Stage.Id = "st_1_01";
                winSetup.Stage.EnemyBaseHp = 50;
                winSetup.Team.Add(new UnitTemplate { Id = "char01_claire", Cost = 0, Hp = 500, Atk = 100, RangeMilli = 600 * 1000, AttackIntervalTicks = 1 });
                var winSim = new BattleSimulator(winSetup);
                runner.InitializeForTesting(winSim, winSetup);
                runner.Deploy(0);
                while (winSim.Outcome == BattleOutcome.Ongoing)
                {
                    runner.StepSim();
                }
                Assert.AreEqual(BattleOutcome.Win, winSim.Outcome);
                runner.Submit();
                pageView.Refresh();

                Assert.IsTrue(pageView.NextStageButton.gameObject.activeSelf, "勝利且下一關已解鎖時 NextStageButton 應顯示");
                Assert.IsTrue(pageView.NextStageButton.interactable);

                // 點擊「下一關」按鈕，應直接啟動 st_1_02 戰鬥
                pageView.OnClickNextStage();
                Assert.AreEqual("st_1_02", runner.StageId, "點擊 NextStageButton 後應自動啟動下一關 st_1_02");
                Assert.IsTrue(runner.IsRunning, "新關卡應處於進行中狀態");
            }
            finally
            {
                GameServices.Clear();
                if (System.IO.Directory.Exists(tempDir))
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Test]
        public void BattleUnit_LoadsSpriteFromStreamingAssets_AndConfiguresVisualNode()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            UnitTemplate unit = new UnitTemplate { Id = "char01_claire", Cost = 100, Hp = 1000, Atk = 50, RangeMilli = 100 * 1000 };
            setup.Team.Add(unit);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.Deploy(0);
            runner.StepSim();
            pageView.Refresh();

            BattleUnit u = sim.Units.First(u => u.T.Id == "char01_claire");
            RectTransform unitRt = pageView.GetUnitTransform(u.Uid);
            Assert.IsNotNull(unitRt, "BattleUnit_Uid transform should exist");

            Transform visual = unitRt.Find("Visual");
            Assert.IsNotNull(visual, "BattleUnit should have Visual child when sprite exists");
            Image vImg = visual.GetComponent<Image>();
            Assert.IsNotNull(vImg, "Visual should have Image component");
            Assert.IsNotNull(vImg.sprite, "Visual Image should have loaded Sprite");
            Assert.AreEqual(Vector3.one, visual.localScale, "Ally should face right (localScale.x = 1)");

            // Non-healer should not have HealAura
            Transform aura = unitRt.Find("HealAura");
            Assert.IsNull(aura, "Non-healer unit (Claire) should NOT have HealAura at feet");
            Image rootImg = unitRt.GetComponent<Image>();
            Assert.AreEqual(Color.clear, rootImg.color, "Root image should be clear (no faction aura)");
        }

        [Test]
        public void BattleUnit_Healer_ConfiguresHealAuraAtFeet()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            var healSkill = new CharacterSkillDef { Id = "skill_holy_blessing", EffectType = "heal", Value = 450 };
            UnitTemplate healerUnit = new UnitTemplate { Id = "char04_elysia", Cost = 100, Hp = 600, Atk = 60, Skill = healSkill };
            setup.Team.Add(healerUnit);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner_Healer"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.Deploy(0);
            runner.StepSim();
            pageView.Refresh();

            BattleUnit u = sim.Units.First(u => u.T.Id == "char04_elysia");
            RectTransform unitRt = pageView.GetUnitTransform(u.Uid);
            Assert.IsNotNull(unitRt, "BattleUnit_Uid transform should exist");

            Transform aura = unitRt.Find("HealAura");
            Assert.IsNotNull(aura, "Healer unit (Elysia) MUST have HealAura at feet");
            RectTransform auraRt = aura.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(0.5f, 0f), auraRt.anchorMin, "HealAura anchorMin should be at feet (0.5, 0)");
            Assert.AreEqual(new Vector2(0.5f, 0f), auraRt.anchorMax, "HealAura anchorMax should be at feet (0.5, 0)");
            Image auraImg = aura.GetComponent<Image>();
            Assert.IsNotNull(auraImg, "HealAura should have Image component");
            Assert.AreEqual(0, aura.GetSiblingIndex(), "HealAura should be first sibling behind sprite");

            // Check dynamic aura scaling with range
            Assert.IsTrue(auraRt.sizeDelta.x >= 84f, "HealAura width should be configured smoothly");
        }

        [Test]
        public void BattleUnit_HealEvent_HighlightsUnitHpGreen()
        {
            BattleSetup setup = CreateTestSetup(length: 1500);
            UnitTemplate tank = new UnitTemplate { Id = "char05_rin", Cost = 50, Hp = 1000, Atk = 10, SpeedMilliPerTick = 0 };
            var healSkill = new CharacterSkillDef { Id = "sk_heal", EffectType = "heal", Value = 450 };
            UnitTemplate healer = new UnitTemplate { Id = "char04_elysia", Cost = 100, Hp = 600, Atk = 60, RangeMilli = 150 * 1000, AttackIntervalTicks = 2, SpeedMilliPerTick = 0, Skill = healSkill };
            setup.Team.Add(tank);
            setup.Team.Add(healer);

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner_HealFeedback"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.Deploy(0);
            runner.StepSim();
            runner.Deploy(1);
            runner.StepSim();

            BattleUnit tankUnit = sim.Units.First(u => u.T.Id == "char05_rin");
            tankUnit.Hp = 500;
            pageView.Refresh();

            // Advance 1 step so healer aura triggers (AttackIntervalTicks = 2)
            runner.StepSim();
            pageView.Refresh();

            Assert.AreEqual(560, tankUnit.Hp, "Tank should be healed by 60 HP from Elysia aura");
            RectTransform tankRt = pageView.GetUnitTransform(tankUnit.Uid);
            Text hpText = tankRt.Find("Hp").GetComponent<Text>();
            Assert.AreEqual(new Color(0.35f, 1f, 0.55f), hpText.color, "Healed unit HP label should flash bright healing green");
        }

        [Test]
        public void BattlePage_LoadsAllyAndEnemyBaseSprites_AndDisplaysUpgradedLevel()
        {
            BattleSetup setup = CreateTestSetup(length: 1000);
            setup.AllyBaseLevel = 5;
            setup.AllyBaseHpOverride = 30000;

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner_Bases"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);
            pageView.Refresh();

            Transform board = runnerGo.transform.Find("BattlePageCanvas/Board");
            Assert.IsNotNull(board, "Board should exist");

            Transform allyBase = board.Find("AllyBaseVisual");
            Assert.IsNotNull(allyBase, "AllyBaseVisual should exist on Board");
            Image allyImg = allyBase.GetComponent<Image>();
            Assert.IsNotNull(allyImg.sprite, "AllyBaseVisual should load ally_base.png sprite");

            Transform enemyBase = board.Find("EnemyBaseVisual");
            Assert.IsNotNull(enemyBase, "EnemyBaseVisual should exist on Board");
            Image enemyImg = enemyBase.GetComponent<Image>();
            Assert.IsNotNull(enemyImg.sprite, "EnemyBaseVisual should load enemy_base.png sprite");

            Text baseLvlText = allyBase.GetComponentInChildren<Text>();
            Assert.IsNotNull(baseLvlText);
            Assert.AreEqual("ALLY BASTION LV.5", baseLvlText.text, "Should display upgraded Ally Base Level on battlefield");

            // 模擬敵方攻擊主堡，驗證主堡受擊閃爍視覺回饋
            sim.TickEvents.Add(new BattleEvent { Type = BattleEventType.BaseHit, Side = Side.Enemy, Value = 100 });
            runner.CaptureTickEvents();
            sim.TickEvents.Clear();
            pageView.Refresh();
            Assert.AreEqual(new Color(1f, 0.45f, 0.45f, 1f), allyImg.color, "Ally Base should flash red on BaseHit event");
        }

        [Test]
        public void BattleUnit_LoadsAllEnemySprites_FacesLeft_AndScalesBossVsSmallMonsters()
        {
            string[] allEnemies = new[]
            {
                "en_slime", "en_bat", "en_harpy", "en_mage_skeleton",
                "en_wraith_assassin", "en_golem", "en_golem_colossus",
                "en_goblin_raider", "en_goblin_shaman",
                "en_dark_elf_blade", "en_dark_elf_archer"
            };

            foreach (string eid in allEnemies)
            {
                Sprite sp = BattlePageView.TryLoadBattleUnitSprite(eid);
                Assert.IsNotNull(sp, $"Enemy sprite {eid}.png should load from StreamingAssets/Battle/Units");
            }

            BattleSetup setup = CreateTestSetup(length: 1500);
            setup.Stage.Waves.Add(new WaveDef { Enemy = "en_dark_elf_blade", AtSec = 0, Count = 1 });
            setup.Stage.Waves.Add(new WaveDef { Enemy = "en_golem_colossus", AtSec = 0, Count = 1 });
            setup.Stage.Waves.Add(new WaveDef { Enemy = "en_slime", AtSec = 0, Count = 1 });
            setup.EnemyTemplates["en_dark_elf_blade"] = new UnitTemplate { Id = "en_dark_elf_blade", Type = "normal", Hp = 520, Atk = 55 };
            setup.EnemyTemplates["en_golem_colossus"] = new UnitTemplate { Id = "en_golem_colossus", Type = "armored", Hp = 7500, Atk = 280 };
            setup.EnemyTemplates["en_slime"] = new UnitTemplate { Id = "en_slime", Type = "normal", Hp = 300, Atk = 20 };

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner_Enemies"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.StepSim();
            pageView.Refresh();

            var deUnit = sim.Units.First(u => u.T.Id == "en_dark_elf_blade");
            var bossUnit = sim.Units.First(u => u.T.Id == "en_golem_colossus");
            var slimeUnit = sim.Units.First(u => u.T.Id == "en_slime");

            RectTransform deRt = pageView.GetUnitTransform(deUnit.Uid);
            RectTransform bossRt = pageView.GetUnitTransform(bossUnit.Uid);
            RectTransform slimeRt = pageView.GetUnitTransform(slimeUnit.Uid);

            Assert.AreEqual(new Vector2(96, 110), deRt.sizeDelta, "Dark Elf should use standard chibi dimensions");
            Assert.AreEqual(new Vector2(142, 162), bossRt.sizeDelta, "Armored Boss should use larger overlord scale");
            Assert.AreEqual(new Vector2(74, 84), slimeRt.sizeDelta, "Slime should use compact monster scale");

            Transform deVisual = deRt.Find("Visual");
            Assert.IsNotNull(deVisual);
            Assert.AreEqual(new Vector3(-1f, 1f, 1f), deVisual.localScale, "Enemy sprite must flip left (scale.x = -1)");
        }

        [Test]
        public void BattlePageView_UnitViewHolder_TracksStatusTransitions_AndScaleCategoryFromData()
        {
            // 使用有實際 Sprite 且特意設定不同 Type / 非預設 Id，驗證 ScaleCategory 直接驅動尺寸而非硬編碼 Id/Type
            BattleSetup setup = CreateTestSetup(length: 1500);
            setup.Stage.Waves.Add(new WaveDef { Enemy = "en_golem", AtSec = 0, Count = 1 });
            setup.Stage.Waves.Add(new WaveDef { Enemy = "en_goblin_raider", AtSec = 0, Count = 1 });
            setup.Stage.Waves.Add(new WaveDef { Enemy = "en_dark_elf_blade", AtSec = 0, Count = 1 });
            setup.EnemyTemplates["en_golem"] = new UnitTemplate { Id = "en_golem", Type = "normal", ScaleCategory = "boss", Hp = 1200, Atk = 50 };
            setup.EnemyTemplates["en_goblin_raider"] = new UnitTemplate { Id = "en_goblin_raider", Type = "normal", ScaleCategory = "small", Hp = 120, Atk = 10 };
            setup.EnemyTemplates["en_dark_elf_blade"] = new UnitTemplate { Id = "en_dark_elf_blade", Type = "armored", ScaleCategory = "normal", Hp = 500, Atk = 30 };

            var sim = new BattleSimulator(setup);
            GameObject runnerGo = Track(new GameObject("TestBattleRunner_ScaleAndHolder"));
            BattleRunner runner = runnerGo.AddComponent<BattleRunner>();
            BattlePageView pageView = runnerGo.AddComponent<BattlePageView>();
            runner.InitializeForTesting(sim, setup);
            pageView.InitializeForTesting(runner);

            runner.StepSim();
            pageView.Refresh();

            var bossUnit = sim.Units.First(u => u.T.Id == "en_golem");
            var tinyUnit = sim.Units.First(u => u.T.Id == "en_goblin_raider");
            var armoredNormalUnit = sim.Units.First(u => u.T.Id == "en_dark_elf_blade");
            RectTransform bossRt = pageView.GetUnitTransform(bossUnit.Uid);
            RectTransform tinyRt = pageView.GetUnitTransform(tinyUnit.Uid);
            RectTransform armoredNormalRt = pageView.GetUnitTransform(armoredNormalUnit.Uid);

            Assert.AreEqual(new Vector2(142, 162), bossRt.sizeDelta, "ScaleCategory=boss 應資料驅動放大尺寸（不依賴 Type==armored）");
            Assert.AreEqual(new Vector2(74, 84), tinyRt.sizeDelta, "ScaleCategory=small 應資料驅動縮小尺寸（不依賴 Id==en_slime）");
            Assert.AreEqual(new Vector2(96, 110), armoredNormalRt.sizeDelta, "顯式 ScaleCategory=normal 即使 Type==armored 也應使用標準尺寸");

            // 狀態遮罩變更：新增後再清空，確認 UnitViewHolder 正確同步並不重複建置物件
            bossUnit.AtkBuffTicks = 10;
            bossUnit.StunTicks = 10;
            bossUnit.Hp = 999;
            pageView.Refresh();
            Assert.AreSame(bossRt, pageView.GetUnitTransform(bossUnit.Uid), "重繪應重用同一個 RectTransform");
            Assert.AreEqual("[ATK↑][STUN]", pageView.GetUnitStatusText(bossUnit.Uid));
            Assert.AreEqual("999", bossRt.Find("Hp").GetComponent<Text>().text);

            bossUnit.AtkBuffTicks = 0;
            bossUnit.StunTicks = 0;
            pageView.Refresh();
            Assert.AreEqual(string.Empty, pageView.GetUnitStatusText(bossUnit.Uid), "狀態歸零後標籤應清空");
        }
    }
}
#endif
