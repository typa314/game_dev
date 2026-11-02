using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Domain;
using Game.Domain.Battle;
using Game.Domain.Conditions;
using Game.Domain.Events;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A10：GameCore 核心命令整合測試
    /// 恪守 Claude 既定之 GameCore 命令介面與錯誤碼（design/03 §2, design/17_framework_worklist.md A10）
    /// 涵蓋查核點 A10-1 ～ A10-6
    /// </summary>
    [TestFixture]
    public class GameCoreTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; set; }
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

        private static GameCore CreateCore(MasterDatabase master, PlayerState player = null, ulong seed = 1, IClock clock = null, RatingPolicy rating = null)
        {
            clock = clock ?? new FixedClock();
            player = player ?? GameCore.NewPlayer(master, clock);
            rating = rating ?? new RatingPolicy(Rating.R18, Rating.R18);
            return new GameCore(master, player, new SplitMix64Random(seed), clock, rating);
        }

        private static void AssertCommandFailsWithRollback<T>(GameCore core, Func<Result<T>> act, string expectedErrorCode)
        {
            string before = JsonConvert.SerializeObject(core.Player);
            Result<T> r = act();
            Assert.IsFalse(r.Ok, $"命令應失敗，但回傳成功");
            Assert.AreEqual(expectedErrorCode, r.Error, $"預期錯誤碼為 {expectedErrorCode}，實際為 {r.Error}");
            string after = JsonConvert.SerializeObject(core.Player);
            Assert.AreEqual(before, after, $"失敗碼 {expectedErrorCode} 執行後狀態必須完全無副作用 (原子性)");
        }

        private static void AssertCommandFailsWithRollback(GameCore core, Func<Result> act, string expectedErrorCode)
        {
            string before = JsonConvert.SerializeObject(core.Player);
            Result r = act();
            Assert.IsFalse(r.Ok, $"命令應失敗，但回傳成功");
            Assert.AreEqual(expectedErrorCode, r.Error, $"預期錯誤碼為 {expectedErrorCode}，實際為 {r.Error}");
            string after = JsonConvert.SerializeObject(core.Player);
            Assert.AreEqual(before, after, $"失敗碼 {expectedErrorCode} 執行後狀態必須完全無副作用 (原子性)");
        }

        // =============================================================
        // A10-1：NewPlayer 初始資料配置
        // =============================================================

        [Test]
        public void A10_1_NewPlayer_InitialStateSetup()
        {
            MasterDatabase m = LoadCleanMaster();
            var clock = new FixedClock();
            PlayerState p = GameCore.NewPlayer(m, clock);

            // 道具
            foreach (ItemStack it in m.Config.Initial.Items)
                Assert.AreEqual(it.N, p.ItemCount(it.Item));

            // 關卡與地點
            foreach (string st in m.Config.Initial.UnlockedStages)
                Assert.IsTrue(p.Stages.ContainsKey(st) && p.Stages[st].Unlocked);
            foreach (string sp in m.Config.Initial.UnlockedSpots)
                Assert.IsTrue(p.Encounter.UnlockedSpots.Contains(sp));

            // 場景與體力
            foreach (string sc in m.Config.Initial.Scenes)
                Assert.IsTrue(p.Scenes.Contains(sc));
            Assert.AreEqual(m.Config.Encounter.StaminaMax, p.Encounter.Stamina);

            // 角色與隊伍
            foreach (string ch in m.Config.Initial.Characters)
            {
                Assert.IsTrue(p.Characters.ContainsKey(ch));
                CharacterState cs = p.Characters[ch];
                Assert.IsTrue(cs.PortraitStage.EndsWith("_s1"));
                Assert.IsTrue(p.Team.Contains(ch));
            }

            Assert.AreEqual("scene_lobby_default", p.Lobby.Scene);
            Assert.AreEqual("char04_elysia", p.Lobby.Character);
        }

        // =============================================================
        // A10-2：所有命令的錯誤碼測試與失敗原子性驗證
        // =============================================================

        [Test]
        public void A10_2_PullGacha_ErrorCodes_AndAtomicity()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);

            // 1. banner_not_found
            AssertCommandFailsWithRollback(core, () => core.PullGacha("non_existent_banner", 1), "banner_not_found");

            // 2. invalid_count
            AssertCommandFailsWithRollback(core, () => core.PullGacha("banner_std", 5), "invalid_count");

            // 3. banner_closed
            BannerDef closedBanner = m.Banners["banner_std"];
            closedBanner.PeriodEndUtc = "2026-01-01T00:00:00Z"; // 過去時間
            AssertCommandFailsWithRollback(core, () => core.PullGacha("banner_std", 1), "banner_closed");
            closedBanner.PeriodEndUtc = null;

            // 4. cost_not_defined
            BannerDef noCostBanner = m.Banners["banner_std"];
            var originalCost = noCostBanner.Cost.Single;
            noCostBanner.Cost.Single = new List<ItemStack>();
            AssertCommandFailsWithRollback(core, () => core.PullGacha("banner_std", 1), "cost_not_defined");
            noCostBanner.Cost.Single = originalCost;

            // 5. insufficient_item
            core.Player.Items["gem"] = 0;
            AssertCommandFailsWithRollback(core, () => core.PullGacha("banner_std", 1), "insufficient_item");
        }

        [Test]
        public void A10_2_LevelUp_And_Awaken_ErrorCodes_AndAtomicity()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);

            // LevelUp: character_not_owned
            AssertCommandFailsWithRollback(core, () => core.LevelUp("char_unowned", new List<ItemStack> { new ItemStack("exp_s", 1) }), "character_not_owned");

            // LevelUp: no_material
            AssertCommandFailsWithRollback(core, () => core.LevelUp("char04_elysia", new List<ItemStack>()), "no_material");

            // LevelUp: invalid_material
            AssertCommandFailsWithRollback(core, () => core.LevelUp("char04_elysia", new List<ItemStack> { new ItemStack("invalid_item", 1) }), "invalid_material");
            AssertCommandFailsWithRollback(core, () => core.LevelUp("char04_elysia", new List<ItemStack> { new ItemStack("exp_s", 0) }), "invalid_material");

            // LevelUp: insufficient_item (exp 或 gold)
            core.Player.Items["gold"] = 0;
            AssertCommandFailsWithRollback(core, () => core.LevelUp("char04_elysia", new List<ItemStack> { new ItemStack("exp_s", 1) }), "insufficient_item");

            // LevelUp: level_cap
            CharacterState elysia = core.Player.Char("char04_elysia");
            elysia.Level = core.Growth.LevelCap(elysia.Awaken);
            core.Player.AddItem("exp_s", 100);
            core.Player.AddItem("gold", 100000);
            AssertCommandFailsWithRollback(core, () => core.LevelUp("char04_elysia", new List<ItemStack> { new ItemStack("exp_s", 1) }), "level_cap");

            // Awaken: character_not_owned
            AssertCommandFailsWithRollback(core, () => core.Awaken("char_unowned"), "character_not_owned");

            // Awaken: insufficient_item
            core.Player.Items.Remove(core.Growth.ShardItem("char04_elysia"));
            AssertCommandFailsWithRollback(core, () => core.Awaken("char04_elysia"), "insufficient_item");

            // Awaken: awaken_max
            elysia.Awaken = core.Growth.MaxAwaken;
            core.Player.AddItem(core.Growth.ShardItem("char04_elysia"), 1000);
            AssertCommandFailsWithRollback(core, () => core.Awaken("char04_elysia"), "awaken_max");
        }

        [Test]
        public void A10_2_SetPortraitStage_SetTeam_EquipCostume_SetLobby_ErrorCodes_AndAtomicity()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);

            // SetPortraitStage: character_not_owned, stage_not_found, stage_locked, rating_not_allowed
            AssertCommandFailsWithRollback(core, () => core.SetPortraitStage("char_unowned", "ps_char01_s1"), "character_not_owned");
            AssertCommandFailsWithRollback(core, () => core.SetPortraitStage("char04_elysia", "ps_not_found"), "stage_not_found");
            AssertCommandFailsWithRollback(core, () => core.SetPortraitStage("char04_elysia", "ps_char01_s1"), "stage_not_found"); // 屬於 Claire 的階段
            
            // 加入 Claire 測試 stage_locked 與 rating_not_allowed
            core.Player.Characters["char01_claire"] = new CharacterState { UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" } };
            AssertCommandFailsWithRollback(core, () => core.SetPortraitStage("char01_claire", "ps_char01_s2"), "stage_locked");

            // 建立 R18 立繪階段於 G 政策下測試
            m.PortraitStages["ps_r18"] = new PortraitStageDef { Id = "ps_r18", Character = "char01_claire", Rating = Rating.R18 };
            core.Player.Char("char01_claire").UnlockedPortraitStages.Add("ps_r18");
            GameCore gCore = CreateCore(m, core.Player, rating: new RatingPolicy(Rating.G, Rating.G));
            AssertCommandFailsWithRollback(gCore, () => gCore.SetPortraitStage("char01_claire", "ps_r18"), "rating_not_allowed");

            // SetTeam: invalid_team, character_not_owned, duplicate_member
            AssertCommandFailsWithRollback(core, () => core.SetTeam(null), "invalid_team");
            AssertCommandFailsWithRollback(core, () => core.SetTeam(new List<string>()), "invalid_team");
            AssertCommandFailsWithRollback(core, () => core.SetTeam(new List<string> { null, null }), "invalid_team");
            AssertCommandFailsWithRollback(core, () => core.SetTeam(Enumerable.Range(0, 11).Select(i => "char04_elysia").ToList()), "invalid_team");
            AssertCommandFailsWithRollback(core, () => core.SetTeam(new List<string> { "char_unowned" }), "character_not_owned");
            AssertCommandFailsWithRollback(core, () => core.SetTeam(new List<string> { "char04_elysia", "char04_elysia" }), "duplicate_member");

            // EquipCostume: character_not_owned, costume_not_found, costume_locked, rating_not_allowed, portrait_stage_required
            AssertCommandFailsWithRollback(core, () => core.EquipCostume("char_unowned", "cos_char01_default"), "character_not_owned");
            AssertCommandFailsWithRollback(core, () => core.EquipCostume("char01_claire", "cos_not_found"), "costume_not_found");
            AssertCommandFailsWithRollback(core, () => core.EquipCostume("char01_claire", "cos_char02_default"), "costume_not_found"); // 屬於 Luna
            AssertCommandFailsWithRollback(core, () => core.EquipCostume("char01_claire", "cos_char01_academy"), "costume_locked"); // 未持有

            // R18 服裝在 G 政策
            m.Costumes["cos_r18"] = new CostumeDef { Id = "cos_r18", Character = "char01_claire", Rating = Rating.R18 };
            core.Player.Costumes.Add("cos_r18");
            AssertCommandFailsWithRollback(gCore, () => gCore.EquipCostume("char01_claire", "cos_r18"), "rating_not_allowed");

            // portrait_stage_required
            m.Costumes["cos_req_s2"] = new CostumeDef { Id = "cos_req_s2", Character = "char01_claire", Rating = Rating.G, RequiresPortraitStage = "ps_char01_s2" };
            core.Player.Costumes.Add("cos_req_s2");
            AssertCommandFailsWithRollback(core, () => core.EquipCostume("char01_claire", "cos_req_s2"), "portrait_stage_required");

            // SetLobby: scene_locked, rating_not_allowed, character_not_owned
            AssertCommandFailsWithRollback(core, () => core.SetLobby("sc_locked", "char01_claire", null), "scene_locked");
            m.Scenes["sc_r18"] = new SceneDef { Id = "sc_r18", Rating = Rating.R18 };
            core.Player.Scenes.Add("sc_r18");
            AssertCommandFailsWithRollback(gCore, () => gCore.SetLobby("sc_r18", "char01_claire", null), "rating_not_allowed");
            AssertCommandFailsWithRollback(core, () => core.SetLobby("scene_lobby_default", "char_unowned", null), "character_not_owned");
        }

        [Test]
        public void A10_2_Battle_Encounter_Achievement_ErrorCodes_AndAtomicity()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);

            // PrepareBattle & SubmitBattle: stage_not_found, stage_locked, character_not_owned
            AssertCommandFailsWithRollback(core, () => core.PrepareBattle("stage_none", 1), "stage_not_found");
            AssertCommandFailsWithRollback(core, () => core.PrepareBattle("st_1_02", 1), "stage_locked"); // st_1_02 未解鎖

            var invalidReport = new BattleReport { StageId = "stage_none", Team = core.Player.Team };
            AssertCommandFailsWithRollback(core, () => core.SubmitBattle(invalidReport), "stage_not_found");
            invalidReport.StageId = "st_1_02";
            AssertCommandFailsWithRollback(core, () => core.SubmitBattle(invalidReport), "stage_locked");
            invalidReport.StageId = "st_1_01";
            invalidReport.Team = new List<string> { "char_unowned" };
            AssertCommandFailsWithRollback(core, () => core.SubmitBattle(invalidReport), "character_not_owned");

            // StartEncounter: spot_not_found, spot_locked, encounter_pending, no_stamina
            AssertCommandFailsWithRollback(core, () => core.StartEncounter("spot_none"), "spot_not_found");
            m.Spots["spot_locked"] = new SpotDef { Id = "spot_locked" };
            AssertCommandFailsWithRollback(core, () => core.StartEncounter("spot_locked"), "spot_locked");

            core.Player.Encounter.Pending = "enc_pending";
            AssertCommandFailsWithRollback(core, () => core.StartEncounter("spot_park"), "encounter_pending");
            core.Player.Encounter.Pending = null;

            core.Player.Encounter.Stamina = 0;
            // 基準點設為當前固定時鐘時間 (經過 0 分鐘，體力保持 0 且不觸發時間回溯防弊)
            core.Player.Encounter.StaminaUpdatedAtUtc = GameCore.Iso(new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc));
            AssertCommandFailsWithRollback(core, () => core.StartEncounter("spot_park"), "no_stamina");

            // ResolveEncounter: no_pending_encounter, invalid_choice
            AssertCommandFailsWithRollback(core, () => core.ResolveEncounter("choice0"), "no_pending_encounter");
            core.Player.Encounter.Pending = "enc_char04_elysia_01";
            AssertCommandFailsWithRollback(core, () => core.ResolveEncounter("invalid_choice_key"), "invalid_choice");
            core.Player.Encounter.Pending = null;

            // ClaimAchievement: achievement_locked, already_claimed
            AssertCommandFailsWithRollback(core, () => core.ClaimAchievement("ach_gacha_100"), "achievement_locked");
            core.Player.Achievements["ach_first_pull"] = new AchievementProgress
            {
                Progress = 1,
                UnlockedAtUtc = "2026-10-14T12:00:00Z",
                Claimed = true
            };
            AssertCommandFailsWithRollback(core, () => core.ClaimAchievement("ach_first_pull"), "already_claimed");
        }

        // =============================================================
        // A10-3：轉蛋角色取得、重複轉換與紀錄上限
        // =============================================================

        [Test]
        public void A10_3_GachaPull_NewCharacter_DuplicatesConversion_AndHistoryLimit()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);
            core.Player.AddItem("gem", 100000);

            // 確保池子裡抽出的第一隻新角色
            string newChar = "char01_claire";
            core.Player.Characters.Remove(newChar);

            // 抽卡獲得新角色
            Result<List<GachaResultEntry>> res = core.PullGacha("banner_std", 1);
            Assert.IsTrue(res.Ok);
            GachaResultEntry entry = res.Value[0];

            if (entry.Draw.Char == newChar)
            {
                Assert.IsTrue(entry.IsNew);
                Assert.IsTrue(core.Player.Characters.ContainsKey(newChar));
                Assert.AreEqual("ps_char01_s1", core.Player.Char(newChar).PortraitStage);
                Assert.IsTrue(core.Player.Costumes.Contains("cos_char01_default"));
            }

            // 測試重複角色轉為碎片 (未滿星)
            CharacterState claire = new CharacterState { Awaken = 0 };
            core.Player.Characters["char01_claire"] = claire;
            m.Banners["banner_std"].Pool["SSR"] = new List<string> { "char01_claire" };
            m.Banners["banner_std"].Rates["SSR"] = 1.0;
            m.Banners["banner_std"].Rates["SR"] = 0.0;
            m.Banners["banner_std"].Rates["R"] = 0.0;
            m.Banners["banner_std"].Pity = null;
            m.Banners["banner_std"].TenPullGuarantee = null;

            Result<List<GachaResultEntry>> dupRes = core.PullGacha("banner_std", 1);
            Assert.IsTrue(dupRes.Ok);
            Assert.IsFalse(dupRes.Value[0].IsNew);
            Assert.AreEqual("awaken_shard_char01_claire", dupRes.Value[0].Converted[0].Item);
            Assert.AreEqual(10, dupRes.Value[0].Converted[0].N);

            // 測試滿星重複角色轉為心之結晶
            claire.Awaken = 5;
            Result<List<GachaResultEntry>> maxDupRes = core.PullGacha("banner_std", 1);
            Assert.IsTrue(maxDupRes.Ok);
            Assert.AreEqual("heart_crystal", maxDupRes.Value[0].Converted[0].Item);
            Assert.AreEqual(5, maxDupRes.Value[0].Converted[0].N);

            // 測試抽卡歷史紀錄上限 200 筆
            for (int i = 0; i < 25; i++)
            {
                core.PullGacha("banner_std", 10);
            }
            Assert.AreEqual(200, core.Player.Gacha.History.Count, "抽卡歷史紀錄應被截斷保留最新的 200 筆");
        }

        // =============================================================
        // A10-4：LevelUp 扣除金幣素材、多筆合併與自動解鎖立繪
        // =============================================================

        [Test]
        public void A10_4_LevelUp_CostConsumption_MultiStacksMerge_AndAutoPortraitUnlock()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);

            core.Player.Characters["char01_claire"] = new CharacterState
            {
                Level = 29,
                Exp = core.Growth.ExpToNext("SSR", 29) - 100,
                Awaken = 2,
                UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" }
            };
            core.Player.AddItem("exp_s", 10);
            core.Player.AddItem("gold", 100000);

            long initialExpS = core.Player.ItemCount("exp_s");
            long initialGold = core.Player.ItemCount("gold");

            // 傳入兩筆相同素材 exp_s (各 1 個，每個 100 exp，共 200 exp，每 exp 需 1 gold)
            var materials = new List<ItemStack>
            {
                new ItemStack("exp_s", 1),
                new ItemStack("exp_s", 1)
            };

            var unlockedStages = new List<string>();
            core.Bus.Subscribe(e =>
            {
                if (e.Name == Ev.PortraitStageUnlocked) unlockedStages.Add(e.Get("stage"));
            });

            Result<int> res = core.LevelUp("char01_claire", materials);
            Assert.IsTrue(res.Ok);
            Assert.AreEqual(2, initialExpS - core.Player.ItemCount("exp_s"), "應合併扣除 2 個 exp_s");
            Assert.AreEqual(initialGold - 200, core.Player.ItemCount("gold"), "應扣除 200 金幣");

            // 驗證等級達到 30 且 Awaken=2 時自動解鎖 ps_char01_s2
            CharacterState claire = core.Player.Char("char01_claire");
            Assert.IsTrue(claire.Level >= 30);
            Assert.IsTrue(claire.UnlockedPortraitStages.Contains("ps_char01_s2"), "升級達標應自動解鎖 S2 立繪階段");
            Assert.IsTrue(unlockedStages.Contains("ps_char01_s2"), "應發佈 PortraitStageUnlocked 事件");
        }

        // =============================================================
        // A10-5：SubmitBattle 獲勝獎勵、首通判定、掉落、星數與隊伍成長
        // =============================================================

        [Test]
        public void A10_5_SubmitBattle_WinRewards_Drops_Unlocks_ExpAffection_StarsRecord()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m, seed: 7);
            string stageId = "st_1_01";

            // 執行第一次戰鬥勝利
            BattleSetup setup = core.PrepareBattle(stageId, 7).Value;
            var sim = new BattleSimulator(setup);
            while (sim.Outcome == BattleOutcome.Ongoing)
            {
                for (int slot = 0; slot < setup.Team.Count; slot++)
                    if (sim.CanDeploy(slot)) sim.Enqueue(CommandType.Deploy, slot);
                sim.Step();
            }

            var report = new BattleReport
            {
                StageId = stageId,
                Seed = 7,
                Team = new List<string>(core.Player.Team),
                Commands = sim.Recorded
            };

            CharacterState elysia = core.Player.Char("char04_elysia");
            long expBefore = elysia.Exp;
            int affBefore = elysia.Affection;
            long gemBefore = core.Player.ItemCount("gem");

            Result<BattleReward> res1 = core.SubmitBattle(report);
            Assert.IsTrue(res1.Ok);
            BattleReward r1 = res1.Value;
            Assert.AreEqual(BattleOutcome.Win, r1.Result.Outcome);
            Assert.IsTrue(r1.FirstClear, "首次通關應標記 FirstClear = true");
            Assert.IsTrue(core.Player.ItemCount("gem") > gemBefore, "首次通關應獲得 FirstClear 鑽石獎勵");
            Assert.IsTrue(r1.Unlocked.Contains("st_1_02"), "通關應解鎖下一關 st_1_02");
            Assert.IsTrue(core.Player.Stages["st_1_02"].Unlocked);
            Assert.IsTrue(elysia.Exp > expBefore, "出戰角色應獲得隊伍經驗");
            Assert.IsTrue(elysia.Affection > affBefore, "出戰角色應獲得好感度");
            Assert.AreEqual(2, core.Player.Stages[stageId].Stars);

            // 第二次通關：FirstClear 不應再次給予，星數保留最高值
            long gemAfterFirst = core.Player.ItemCount("gem");
            Result<BattleReward> res2 = core.SubmitBattle(report);
            Assert.IsTrue(res2.Ok);
            BattleReward r2 = res2.Value;
            Assert.IsFalse(r2.FirstClear, "第二次通關 FirstClear 應為 false");
            // 首通獎勵不重複發放
            Assert.IsFalse(r2.Items.Any(it => it.Item == "gem"), "非首次通關不得重複給予 FirstClear 寶石獎勵");
            Assert.AreEqual(2, core.Player.Stages[stageId].Stars, "星數應維持最高評價 2 星");
        }

        // =============================================================
        // A10-6：SubmitBattle 失敗或超時不發放任何獎勵
        // =============================================================

        [Test]
        public void A10_6_SubmitBattle_LossOrTimeout_NoRewardsGranted()
        {
            MasterDatabase m = LoadCleanMaster();
            GameCore core = CreateCore(m);
            string stageId = "st_1_01";

            // 建立一個不發指令直接超時的 BattleReport (TimeLimitSec 縮小或模擬 timeout)
            m.Stages[stageId].TimeLimitSec = 1; // 30 ticks 超時
            var report = new BattleReport
            {
                StageId = stageId,
                Seed = 1,
                Team = new List<string>(core.Player.Team),
                Commands = new List<BattleCommand>() // 不出兵，直接超時
            };

            CharacterState elysia = core.Player.Char("char04_elysia");
            long expBefore = elysia.Exp;
            int affBefore = elysia.Affection;
            long goldBefore = core.Player.ItemCount("gold");

            Result<BattleReward> res = core.SubmitBattle(report);
            Assert.IsTrue(res.Ok);
            BattleReward reward = res.Value;
            Assert.AreEqual(BattleOutcome.Timeout, reward.Result.Outcome);
            Assert.AreEqual(0, reward.Items.Count, "失敗或超時不應獲得任何道具");
            Assert.AreEqual(0, reward.Unlocked.Count, "失敗或超時不應解鎖任何新關卡");
            Assert.AreEqual(expBefore, elysia.Exp, "失敗或超時出戰角色不得增加經驗");
            Assert.AreEqual(affBefore, elysia.Affection, "失敗或超時出戰角色不得增加好感度");
            Assert.AreEqual(goldBefore, core.Player.ItemCount("gold"));
        }
    }
}
