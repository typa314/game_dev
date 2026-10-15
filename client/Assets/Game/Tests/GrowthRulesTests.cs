using System;
using System.Collections.Generic;
using Game.Core;
using Game.Domain;
using Game.Domain.Conditions;
using Game.Domain.Growth;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A4：養成規則（GrowthRules.cs）單元測試
    /// 涵蓋查核點 A4-1 ～ A4-8
    /// </summary>
    [TestFixture]
    public class GrowthRulesTests
    {
        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static GrowthRules CreateRules(MasterDatabase m)
        {
            return new GrowthRules(m.Config.Growth);
        }

        // =============================================================
        // A4-1：經驗曲線計算驗證（SSR Lv1, Lv10, Lv50）
        // 公式：round(Base * level^Exponent)，SSR: Base 35, Exponent 1.55
        // =============================================================

        [Test]
        public void A4_1_ExpToNext_SSR_MatchesCalculatedValues()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            // Lv1: round(35 * 1^1.55) = 35
            Assert.AreEqual(35, rules.ExpToNext("SSR", 1), "SSR Lv1 升級經驗應為 35");

            // Lv10: round(35 * 10^1.55) = round(35 * 35.4813389) = round(1241.84686) = 1242
            Assert.AreEqual(1242, rules.ExpToNext("SSR", 10), "SSR Lv10 升級經驗應為 1242");

            // Lv50: round(35 * 50^1.55) = round(35 * 429.93666) = round(15047.78) = 15048
            Assert.AreEqual(15048, rules.ExpToNext("SSR", 50), "SSR Lv50 升級經驗應為 15048");
        }

        // =============================================================
        // A4-2：一次注入大量經驗可連續升級，回傳升級等級數正確
        // =============================================================

        [Test]
        public void A4_2_AddExp_LargeAmount_LevelsUpMultipleTimes()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            CharacterState s = new CharacterState { Level = 1, Exp = 0, Awaken = 0 };

            // Lv1 需要 35，Lv2 需要 round(35 * 2^1.55) = round(35 * 2.92817) = round(102.486) = 102
            // Lv3 需要 round(35 * 3^1.55) = round(35 * 5.4800) = 192
            // 升到 Lv4 總共需要 35 + 102 + 192 = 329
            // 給予 350 經驗：應升 3 級至 Lv4，剩下 350 - 329 = 21 經驗
            int levelsGained = rules.AddExp(s, "SSR", 350);

            Assert.AreEqual(3, levelsGained, "應提升 3 級");
            Assert.AreEqual(4, s.Level, "當前等級應為 Lv4");
            Assert.AreEqual(21, s.Exp, "剩餘經驗應為 21");
        }

        // =============================================================
        // A4-3：到達等級上限（★0 為 Lv20）後 Exp 歸零，多餘經驗捨棄；已在上限時 AddExp 回傳 0
        // =============================================================

        [Test]
        public void A4_3_AddExp_HitLevelCap_ExpZeroedAndExcessDiscarded()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            CharacterState s = new CharacterState { Level = 1, Exp = 0, Awaken = 0 };
            Assert.AreEqual(20, rules.LevelCap(s.Awaken), "★0 等級上限應為 20");

            // 注入巨額經驗，強制到達上限
            int levelsGained = rules.AddExp(s, "SSR", 10000000);

            Assert.AreEqual(19, levelsGained, "從 Lv1 到 Lv20 應提升 19 級");
            Assert.AreEqual(20, s.Level, "當前等級應為上限 20");
            Assert.AreEqual(0, s.Exp, "到達上限時經驗應歸零");
            Assert.AreEqual(0, rules.ExpToCap(s, "SSR"), "到達上限時 ExpToCap 應為 0");

            // 已在上限時再次增加經驗
            int extraGained = rules.AddExp(s, "SSR", 5000);
            Assert.AreEqual(0, extraGained, "已達上限時獲得等級應為 0");
            Assert.AreEqual(20, s.Level, "等級應保持在 20");
            Assert.AreEqual(0, s.Exp, "經驗應保持為 0");
        }

        // =============================================================
        // A4-4：覺醒後等級上限提高（★1 為 Lv30），可繼續升級；覺醒碎片消耗驗證
        // =============================================================

        [Test]
        public void A4_4_Awaken_RaisesLevelCapAndAllowsFurtherLevelUp()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            CharacterState s = new CharacterState { Level = 20, Exp = 0, Awaken = 0 };

            // 驗證覺醒消耗
            Assert.AreEqual(10, rules.AwakenCost(0), "★0 升 ★1 需 10 碎片");
            Assert.AreEqual(20, rules.AwakenCost(1), "★1 升 ★2 需 20 碎片");
            Assert.AreEqual(int.MaxValue, rules.AwakenCost(5), "滿覺醒後消耗為 int.MaxValue");
            Assert.AreEqual("awaken_shard_char01_claire", rules.ShardItem("char01_claire"));

            // 覺醒至 ★1
            s.Awaken = 1;
            Assert.AreEqual(30, rules.LevelCap(s.Awaken), "★1 等級上限提高至 30");
            Assert.IsTrue(rules.ExpToCap(s, "SSR") > 0, "上限提高後應有剩餘升級所需經驗");

            // 繼續增加經驗升級
            int gained = rules.AddExp(s, "SSR", 10000);
            Assert.IsTrue(gained > 0, "突破上限後可繼續升級");
            Assert.IsTrue(s.Level > 20, "等級應超過原上限 20");
            Assert.IsTrue(s.Level <= 30, "等級不高於新上限 30");
        }

        // =============================================================
        // A4-5：ComputeStats 數值計算與防溢位
        // Lv1, ★0, 無立繪加成時等於基礎值
        // Lv30, ★2, S2 (atk +5%) 比對手算值
        // =============================================================

        [Test]
        public void A4_5_ComputeStats_MatchesFormulaAndDoesNotOverflow()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            CharacterDef claire = m.Characters["char01_claire"];
            // Base: Hp = 1200, Atk = 180, growthCurve = "curve_standard" (8% per level)

            // Case 1: Lv1, ★0, 無額外加成
            CharacterState s1 = new CharacterState { Level = 1, Awaken = 0 };
            ComputedStats stats1 = rules.ComputeStats(claire, s1, null);
            Assert.AreEqual(1200, stats1.Hp, "Lv1 ★0 無加成時 Hp 應等於基礎值 1200");
            Assert.AreEqual(180, stats1.Atk, "Lv1 ★0 無加成時 Atk 應等於基礎值 180");

            // Case 2: Lv30, ★2 (awaken bonus 10%), S2 (statBonus: atkPct = 5, hpPct = 0)
            // levelPct = 100 + 8 * 29 = 332
            // awakenPct = 10
            // stageAtk = 5, stageHp = 0
            // Hp: 1200 * 332 / 100 * (100 + 10 + 0) / 100 = 3984 * 110 / 100 = 4382
            // Atk: 180 * 332 / 100 * (100 + 10 + 5) / 100 = 597 * 115 / 100 = 686
            CharacterState s2 = new CharacterState { Level = 30, Awaken = 2 };
            PortraitStageDef stage2 = m.PortraitStages["ps_char01_s2"];
            ComputedStats stats2 = rules.ComputeStats(claire, s2, stage2);
            Assert.AreEqual(4382, stats2.Hp, "Lv30 ★2 S2 實算 Hp 應為 4382");
            Assert.AreEqual(686, stats2.Atk, "Lv30 ★2 S2 實算 Atk 應為 686");

            // Case 3: 邊界大數計算，驗證 long 中繼計算不會 overflow
            CharacterDef bigDef = new CharacterDef
            {
                Id = "char_boss",
                Battle = new BattleStats
                {
                    Hp = 20000000,
                    Atk = 2000000,
                    GrowthCurve = "curve_standard"
                }
            };
            CharacterState sBig = new CharacterState { Level = 70, Awaken = 5 }; // levelPct = 100 + 8*69 = 652
            ComputedStats bigStats = rules.ComputeStats(bigDef, sBig, null);
            // levelPct = 652、★5 加成 = 30%；精確結果同時驗證中間乘法未以 int 溢位。
            Assert.AreEqual(169520000, bigStats.Hp, "大數 Hp 應為 20,000,000 × 6.52 × 1.30");
            Assert.AreEqual(16952000, bigStats.Atk, "大數 Atk 應為 2,000,000 × 6.52 × 1.30");
        }

        // =============================================================
        // A4-6：好感等級門檻比對（0->1, 99->1, 100->2, 999->4, 1000->5, 2000->5）
        // 門檻：[0, 100, 300, 600, 1000]
        // =============================================================

        [TestCase(0, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 2)]
        [TestCase(299, 2)]
        [TestCase(300, 3)]
        [TestCase(599, 3)]
        [TestCase(600, 4)]
        [TestCase(999, 4)]
        [TestCase(1000, 5)]
        [TestCase(2500, 5)]
        public void A4_6_AffectionLevel_ThresholdsMatch(int affection, int expectedLevel)
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            Assert.AreEqual(expectedLevel, rules.AffectionLevel(affection), "好感度 " + affection + " 應對應等級 " + expectedLevel);
        }

        // =============================================================
        // A4-7：立繪解鎖判定（ps_char01_s2: Lv30 且 ★2 解鎖，未達條件不解鎖，已解鎖不重複回傳）
        // =============================================================

        [Test]
        public void A4_7_RefreshPortraitUnlocks_EvaluatesConditionsAndDoesNotDuplicate()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            PlayerState player = new PlayerState();
            CharacterState claireState = new CharacterState { Level = 1, Awaken = 0 };
            player.Characters["char01_claire"] = claireState;

            ConditionContext ctx = new ConditionContext
            {
                Player = player,
                Master = m,
                Rating = new RatingPolicy(Rating.R18, Rating.R18)
            };

            // 初始時 s1 (order 1, unlock: null) 會被解鎖
            List<string> initialUnlocks = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctx);
            Assert.IsTrue(initialUnlocks.Contains("ps_char01_s1"), "無條件之 s1 應立即解鎖");
            Assert.IsFalse(initialUnlocks.Contains("ps_char01_s2"), "未達條件之 s2 不應解鎖");

            // 再次呼叫，已解鎖之 s1 不會再次被回傳
            List<string> dupeCheck = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctx);
            Assert.AreEqual(0, dupeCheck.Count, "已解鎖項目不應重複回傳");

            // 條件差一點：Lv29 且 ★2
            claireState.Level = 29;
            claireState.Awaken = 2;
            List<string> failLv = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctx);
            Assert.IsFalse(failLv.Contains("ps_char01_s2"), "Lv29 未滿 30 不應解鎖 s2");

            // 條件差一點：Lv30 且 ★1
            claireState.Level = 30;
            claireState.Awaken = 1;
            List<string> failAwaken = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctx);
            Assert.IsFalse(failAwaken.Contains("ps_char01_s2"), "★1 未滿 ★2 不應解鎖 s2");

            // 滿足條件：Lv30 且 ★2
            claireState.Level = 30;
            claireState.Awaken = 2;
            List<string> successUnlocks = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctx);
            Assert.IsTrue(successUnlocks.Contains("ps_char01_s2"), "Lv30 且 ★2 時應成功解鎖 s2");
            Assert.IsTrue(claireState.UnlockedPortraitStages.Contains("ps_char01_s2"));

            // 再次檢查無重複
            List<string> emptyAgain = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctx);
            Assert.AreEqual(0, emptyAgain.Count);
        }

        // =============================================================
        // A4-8：立繪階段分級高於政策時不解鎖，放寬政策後補上
        // =============================================================

        [Test]
        public void A4_8_RefreshPortraitUnlocks_RatingRestrictedUntilPolicyBroadened()
        {
            MasterDatabase m = LoadCleanMaster();
            GrowthRules rules = CreateRules(m);

            // 動態新增一個 R18 立繪階段於範例資料
            PortraitStageDef r18Stage = new PortraitStageDef
            {
                Id = "ps_char01_r18",
                Character = "char01_claire",
                Order = 4,
                Rating = Rating.R18,
                Unlock = null // 無額外條件
            };
            m.PortraitStages[r18Stage.Id] = r18Stage;
            m.Characters["char01_claire"].PortraitStages.Add(r18Stage.Id);

            PlayerState player = new PlayerState();
            CharacterState claireState = new CharacterState { Level = 1, Awaken = 0 };
            player.Characters["char01_claire"] = claireState;

            // 在 G 政策下
            ConditionContext ctxG = new ConditionContext
            {
                Player = player,
                Master = m,
                Rating = new RatingPolicy(Rating.G, Rating.G)
            };

            List<string> unlocksUnderG = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctxG);
            Assert.IsTrue(unlocksUnderG.Contains("ps_char01_s1"), "G 分級的 s1 應解鎖");
            Assert.IsFalse(unlocksUnderG.Contains("ps_char01_r18"), "R18 階段在 G 政策下不應解鎖");
            Assert.IsFalse(claireState.UnlockedPortraitStages.Contains("ps_char01_r18"));

            // 放寬政策至 R18（例如安裝成人 DLC）
            ConditionContext ctxR18 = new ConditionContext
            {
                Player = player,
                Master = m,
                Rating = new RatingPolicy(Rating.R18, Rating.R18)
            };

            List<string> unlocksUnderR18 = rules.RefreshPortraitUnlocks("char01_claire", claireState, ctxR18);
            Assert.IsTrue(unlocksUnderR18.Contains("ps_char01_r18"), "放寬為 R18 政策後應補上解鎖 R18 階段");
            Assert.IsTrue(claireState.UnlockedPortraitStages.Contains("ps_char01_r18"));
        }
    }
}
