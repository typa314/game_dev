using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core;
using Game.Domain;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A2：主資料驗證器（MasterDataValidator.cs）單元測試
    /// 涵蓋查核點 A2-5, A2-6
    /// </summary>
    [TestFixture]
    public class MasterDataValidatorTests
    {
        private static MasterDatabase LoadCleanMaster()
        {
            MasterDatabase m = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            List<string> initialErrors = MasterDataValidator.Validate(m);
            Assert.AreEqual(0, initialErrors.Count, "範例資料在修改前必須通過驗證（0 個錯誤）");
            return m;
        }

        private static void AssertHasError(List<string> errors, string prefix, string keyword)
        {
            bool found = errors.Any(e => e.StartsWith(prefix, StringComparison.Ordinal) && e.Contains(keyword));
            Assert.IsTrue(found, "期望在錯誤清單中找到以 '" + prefix + "' 開頭且包含 '" + keyword + "' 的錯誤，但未找到。\n實際錯誤清單:\n" + string.Join("\n", errors));
        }

        // =============================================================
        // A2-5 改動定義（供個別測試與 A2-6 格式測試共用）
        // =============================================================

        internal static void Mutate01(MasterDatabase m)
        {
            m.Characters["char05_rin"].PortraitStages.Add("ps_missing");
        }

        internal static void Mutate02(MasterDatabase m)
        {
            m.Characters["char05_rin"].PortraitStages.Add("ps_char04_s1");
        }

        internal static void Mutate03(MasterDatabase m)
        {
            m.PortraitStages["ps_char01_s3"].Rating = Rating.R18;
        }

        internal static void Mutate04(MasterDatabase m)
        {
            m.PortraitStages["ps_char04_s1"].Unlock = new Condition { Type = "char_owned", Char = "char04_elysia" };
        }

        internal static void Mutate05(MasterDatabase m)
        {
            m.PortraitStages["ps_char05_s1"].Rating = Rating.R15;
        }

        internal static void Mutate06(MasterDatabase m)
        {
            m.Banners["banner_std"].Rates["R"] = 0.80;
        }

        internal static void Mutate07(MasterDatabase m)
        {
            m.Banners["banner_std"].Pool["R"] = new List<string>();
        }

        internal static void Mutate08(MasterDatabase m)
        {
            m.Banners["banner_std"].Pool["SSR"].Add("char04_elysia");
        }

        internal static void Mutate09(MasterDatabase m)
        {
            m.Banners["banner_std"].Pool["R"].Remove("char05_rin");
            m.Banners["banner_std"].Pickup = new BannerPickup { Chars = new List<string> { "char05_rin" } };
        }

        internal static void Mutate10(MasterDatabase m)
        {
            m.Stages["st_1_01"].Waves[0].Enemy = "en_missing";
        }

        internal static void Mutate11(MasterDatabase m)
        {
            m.Stages["st_1_01"].UnlocksOnClear.Add("nowhere");
        }

        internal static void Mutate12(MasterDatabase m)
        {
            m.Stages["st_1_01"].StarConditions.Add("perfect");
        }

        internal static void Mutate13(MasterDatabase m)
        {
            m.Encounters["enc_park_generic"].Spot = "spot_missing";
        }

        internal static void Mutate14(MasterDatabase m)
        {
            m.Encounters["enc_char01_claire_cafe"].Character = "char99_none";
        }

        internal static void Mutate15(MasterDatabase m)
        {
            m.Encounters["enc_park_generic"].Outcomes["choice0"].Add(new Outcome { Costume = "cos_missing", N = 1 });
        }

        internal static void Mutate16(MasterDatabase m)
        {
            m.Encounters["enc_char04_elysia_02"].Order = 3;
        }

        internal static void Mutate17(MasterDatabase m)
        {
            m.Encounters["enc_char04_elysia_02"].Order = 1;
        }

        internal static void Mutate18(MasterDatabase m)
        {
            m.Achievements["ach_first_win"].Trigger.Counter = "avg";
        }

        internal static void Mutate19(MasterDatabase m)
        {
            m.Achievements["ach_first_win"].Trigger.Event = null;
        }

        internal static void Mutate20(MasterDatabase m)
        {
            m.Achievements["ach_first_pull"].Trigger.Field = null;
        }

        internal static void Mutate21(MasterDatabase m)
        {
            m.Achievements["ach_first_pull"].Rating = Rating.R18;
        }

        internal static void Mutate22(MasterDatabase m)
        {
            m.Spots["spot_cafe"].Unlock = new Condition { Type = "moon_phase" };
        }

        internal static void Mutate23(MasterDatabase m)
        {
            m.Spots["spot_cafe"].Unlock = new Condition { Type = "all" };
        }

        internal static void Mutate24(MasterDatabase m)
        {
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "char_owned", Char = "char99_none" };
        }

        internal static void Mutate25(MasterDatabase m)
        {
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "stage_cleared", Stage = "st_9_99" };
        }

        internal static void Mutate26(MasterDatabase m)
        {
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "achievement", Id = "ach_missing" };
        }

        internal static void Mutate27(MasterDatabase m)
        {
            m.Scenes["scene_cafe"].Unlock = new Condition
            {
                Type = "all",
                Of = new List<Condition>
                {
                    new Condition
                    {
                        Type = "not",
                        Cond = new Condition
                        {
                            Type = "char_owned",
                            Char = "char99_none"
                        }
                    }
                }
            };
        }

        internal static void Mutate28(MasterDatabase m)
        {
            m.Config.Initial.Characters.Add("char99_none");
        }

        internal static void Mutate29(MasterDatabase m)
        {
            m.Config.Initial.UnlockedStages.Add("st_9_99");
        }

        internal static void Mutate30(MasterDatabase m)
        {
            m.Config.Initial.UnlockedSpots.Add("spot_missing");
        }

        internal static void Mutate31(MasterDatabase m)
        {
            m.Config.Initial.Scenes.Add("scene_missing");
        }

        // =============================================================
        // A2-5 單項測試（01～31）
        // =============================================================

        [Test]
        public void A2_5_01_Character_PortraitStageNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate01(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "character char05_rin: ", "ps_missing not found");
        }

        [Test]
        public void A2_5_02_Character_PortraitStageBelongsToOtherCharacter_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate02(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "character char05_rin: ", "belongs to char04_elysia");
        }

        [Test]
        public void A2_5_03_PortraitStage_R18_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate03(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "portraitStage ps_char01_s3: ", "R18");
        }

        [Test]
        public void A2_5_04_PortraitStage_FirstStageHasUnlockCondition_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate04(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "character char04_elysia: ", "first portrait stage");
        }

        [Test]
        public void A2_5_05_PortraitStage_FirstStageNotRatingG_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate05(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "character char05_rin: ", "first portrait stage");
        }

        [Test]
        public void A2_5_06_Banner_RatesSumNotEqualToOne_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate06(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "banner banner_std: ", "rates sum to");
        }

        [Test]
        public void A2_5_07_Banner_RarityHasRateButEmptyPool_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate07(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "banner banner_std: ", "rarity R has rate but empty pool");
        }

        [Test]
        public void A2_5_08_Banner_PoolCharacterRarityMismatch_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate08(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "banner banner_std: ", "char04_elysia is SR but listed under SSR");
        }

        [Test]
        public void A2_5_09_Banner_PickupCharacterNotInPool_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate09(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "banner banner_std: ", "pickup char05_rin not in pool");
        }

        [Test]
        public void A2_5_10_Stage_EnemyNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate10(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "stage st_1_01: ", "enemy en_missing not found");
        }

        [Test]
        public void A2_5_11_Stage_UnlocksOnClearNeitherStageNorSpot_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate11(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "stage st_1_01: ", "nowhere is neither stage nor spot");
        }

        [Test]
        public void A2_5_12_Stage_UnknownStarCondition_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate12(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "stage st_1_01: ", "unknown star condition perfect");
        }

        [Test]
        public void A2_5_13_Encounter_SpotNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate13(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "encounter enc_park_generic: ", "spot spot_missing not found");
        }

        [Test]
        public void A2_5_14_Encounter_CharacterNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate14(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "encounter enc_char01_claire_cafe: ", "character char99_none not found");
        }

        [Test]
        public void A2_5_15_Encounter_OutcomeCostumeNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate15(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "encounter enc_park_generic: ", "costume cos_missing not found");
        }

        [Test]
        public void A2_5_16_EncounterSeries_OrdersHaveGaps_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate16(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "encounter series encs_char04: ", "1..N");
        }

        [Test]
        public void A2_5_17_EncounterSeries_OrdersDuplicate_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate17(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "encounter series encs_char04: ", "1..N");
        }

        [Test]
        public void A2_5_18_Achievement_UnknownCounter_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate18(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "achievement ach_first_win: ", "unknown counter avg");
        }

        [Test]
        public void A2_5_19_Achievement_MissingEvent_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate19(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "achievement ach_first_win: ", "event required");
        }

        [Test]
        public void A2_5_20_Achievement_MissingField_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate20(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "achievement ach_first_pull: ", "field required");
        }

        [Test]
        public void A2_5_21_Achievement_R18WithSteamApiName_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate21(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "achievement ach_first_pull: ", "steamApiName");
        }

        [Test]
        public void A2_5_22_Spot_UnknownConditionType_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate22(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "spot spot_cafe: ", "unknown condition type moon_phase");
        }

        [Test]
        public void A2_5_23_Spot_AllConditionNeedsOf_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate23(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "spot spot_cafe: ", "'all' needs 'of'");
        }

        [Test]
        public void A2_5_24_Scene_ConditionCharNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate24(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "condition char char99_none not found");
        }

        [Test]
        public void A2_5_25_Scene_ConditionStageNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate25(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "condition stage st_9_99 not found");
        }

        [Test]
        public void A2_5_26_Scene_ConditionAchievementNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate26(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "condition achievement ach_missing not found");
        }

        [Test]
        public void A2_5_27_Scene_NestedConditionCharNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate27(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "condition char char99_none not found");
        }

        [Test]
        public void A2_5_28_ConfigInitial_CharacterNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate28(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "config.initial: ", "character char99_none not found");
        }

        [Test]
        public void A2_5_29_ConfigInitial_StageNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate29(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "config.initial: ", "stage st_9_99 not found");
        }

        [Test]
        public void A2_5_30_ConfigInitial_SpotNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate30(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "config.initial: ", "spot spot_missing not found");
        }

        [Test]
        public void A2_5_31_ConfigInitial_SceneNotFound_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            Mutate31(m);
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "config.initial: ", "scene scene_missing not found");
        }

        // =============================================================
        // A2-5 對照組（陰性案例 N1～N4）
        // =============================================================

        [Test]
        public void A2_5_N1_Stage_UnlocksOnClearSpot_IsValid()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Stages["st_1_01"].UnlocksOnClear.Add("spot_cafe");
            List<string> errors = MasterDataValidator.Validate(m);
            Assert.AreEqual(0, errors.Count, "N1 失敗: 地點也是合法的解鎖目標，不應有錯誤。\n" + string.Join("\n", errors));
        }

        [Test]
        public void A2_5_N2_Stage_PrefixStarConditions_AreValid()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Stages["st_1_01"].StarConditions.Add("within_sec_90");
            m.Stages["st_1_01"].StarConditions.Add("no_base_damage_pct_50");
            List<string> errors = MasterDataValidator.Validate(m);
            Assert.AreEqual(0, errors.Count, "N2 失敗: 合法的星級條件不應有錯誤。\n" + string.Join("\n", errors));
        }

        [Test]
        public void A2_5_N3_Achievement_R18WithoutSteamApiName_IsValid()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Achievements["ach_first_pull"].Rating = Rating.R18;
            m.Achievements["ach_first_pull"].SteamApiName = null;
            List<string> errors = MasterDataValidator.Validate(m);
            Assert.AreEqual(0, errors.Count, "N3 失敗: R18 成就沒有 steamApiName 時不應有錯誤。\n" + string.Join("\n", errors));
        }

        [Test]
        public void A2_5_N4_Scene_ConditionCharSelf_IsValid()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "char_owned", Char = "self" };
            List<string> errors = MasterDataValidator.Validate(m);
            Assert.AreEqual(0, errors.Count, "N4 失敗: char 為 'self' 不需是存在的角色 ID，不應有錯誤。\n" + string.Join("\n", errors));
        }

        [Test]
        public void A2_5_32_Scene_ConditionStoryFlagNeedsKey_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "story_flag", Value = "1" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'story_flag' needs 'key'");
        }

        [Test]
        public void A2_5_33_Scene_ConditionStoryFlagInvalidOp_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "story_flag", Key = "flag1", Op = "<>", Value = "1" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'story_flag' invalid op <>");
        }

        [Test]
        public void A2_5_34_Scene_ConditionCharLevelGte_NeedsNumericValue_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "char_level_gte", Char = "char01_claire", Value = "invalid" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'char_level_gte' needs valid numeric 'value'");
        }

        [Test]
        public void A2_5_35_Stage_WaveConditionEnemyBaseHpBelowPct_NeedsNumericValue_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Stages["st_1_01"].Waves[0].Trigger = new Condition { Type = "enemy_base_hp_below_pct", Value = "abc" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "stage st_1_01: ", "'enemy_base_hp_below_pct' needs valid numeric 'value'");
        }

        [Test]
        public void A2_5_36_Scene_ConditionTimeOfDay_InvalidValue_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "time_of_day", Value = "midnight" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'time_of_day' invalid value midnight");
        }

        [Test]
        public void A2_5_37_Scene_ConditionWeekday_InvalidValue_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "weekday", Value = "Funday" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'weekday' invalid value Funday");
        }

        [Test]
        public void A2_5_38_Scene_ConditionRatingAllowed_InvalidRating_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "rating_allowed", Rating = "PG13" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'rating_allowed' invalid rating PG13");
        }

        [Test]
        public void A2_5_39_Scene_ConditionEndingReached_NeedsEnding_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "ending_reached", Ending = "" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'ending_reached' needs 'ending'");
        }

        [Test]
        public void A2_5_40_Scene_ConditionStoryNode_NeedsNode_IsRejected()
        {
            MasterDatabase m = LoadCleanMaster();
            m.Scenes["scene_cafe"].Unlock = new Condition { Type = "story_node", Node = "" };
            List<string> errors = MasterDataValidator.Validate(m);
            AssertHasError(errors, "scene scene_cafe: ", "'story_node' needs 'node'");
        }

        // =============================================================
        // A2-6 錯誤訊息格式（正規表示式檢查）
        // =============================================================

        [Test]
        public void A2_6_ErrorMessages_MatchExpectedFormat()
        {
            MasterDatabase m = LoadCleanMaster();

            // 全部套用 A2-5 的 01～31 號改動
            Mutate01(m);
            Mutate02(m);
            Mutate03(m);
            Mutate04(m);
            Mutate05(m);
            Mutate06(m);
            Mutate07(m);
            Mutate08(m);
            Mutate09(m);
            Mutate10(m);
            Mutate11(m);
            Mutate12(m);
            Mutate13(m);
            Mutate14(m);
            Mutate15(m);
            Mutate16(m);
            Mutate17(m);
            Mutate18(m);
            Mutate19(m);
            Mutate20(m);
            Mutate21(m);
            Mutate22(m);
            Mutate23(m);
            Mutate24(m);
            Mutate25(m);
            Mutate26(m);
            Mutate27(m);
            Mutate28(m);
            Mutate29(m);
            Mutate30(m);
            Mutate31(m);

            List<string> errors = MasterDataValidator.Validate(m);
            Assert.That(errors, Is.Not.Empty, "套用改動後必須產生錯誤訊息");

            Regex entityRegex = new Regex(@"^(character|portraitStage|costume|scene|spot|stage|banner|encounter|encounter series|achievement) [^\s:]+: \S.*$");
            Regex configRegex = new Regex(@"^config\.initial: \S.*$");

            List<string> invalidMessages = new List<string>();
            foreach (string err in errors)
            {
                if (!entityRegex.IsMatch(err) && !configRegex.IsMatch(err))
                {
                    invalidMessages.Add(err);
                }
            }

            Assert.That(invalidMessages, Is.Empty,
                "以下錯誤訊息不符合規範格式:\n" + string.Join("\n", invalidMessages));
        }
    }
}
