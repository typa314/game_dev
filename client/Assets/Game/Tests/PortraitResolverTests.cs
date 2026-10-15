using System.Collections.Generic;
using Game.Core;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Domain.Portrait;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A9：造型決定系統（PortraitResolver.cs）單元測試
    /// 恪守 Claude 既定之造型與服裝決定規則（design/10_costume_and_scene.md §1）
    /// 涵蓋查核點 A9-1 ～ A9-4
    /// </summary>
    [TestFixture]
    public class PortraitResolverTests
    {
        private static MasterDatabase LoadCleanMaster()
        {
            return MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        private static RatingPolicy DefaultRatingPolicy(Rating max = Rating.R18, Rating user = Rating.R18)
        {
            return new RatingPolicy(max, user);
        }

        // =============================================================
        // A9-1：沒有服裝時，使用目前選用的立繪階段的 art
        // =============================================================

        [Test]
        public void A9_1_NoCostume_UsesSelectedPortraitStageArt()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            var claire = new CharacterState
            {
                UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1", "ps_char01_s2" },
                PortraitStage = "ps_char01_s1",
                Costume = null
            };
            player.Characters["char01_claire"] = claire;

            ResolvedPortrait resolvedS1 = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");

            Assert.AreEqual("char01_claire", resolvedS1.Character);
            Assert.AreEqual("ps_char01_s1", resolvedS1.PortraitStage);
            Assert.IsNull(resolvedS1.Costume, "未套用服裝時 Costume 應為 null");
            Assert.IsNotNull(resolvedS1.Art);
            Assert.AreEqual("char/char01_claire/portrait/ps_char01_s1/stand", resolvedS1.Art.Stand);
            Assert.AreEqual(Rating.G, resolvedS1.Rating);

            // 切換至 S2 階段
            claire.PortraitStage = "ps_char01_s2";
            ResolvedPortrait resolvedS2 = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");

            Assert.AreEqual("ps_char01_s2", resolvedS2.PortraitStage);
            Assert.AreEqual("char/char01_claire/portrait/ps_char01_s2/stand", resolvedS2.Art.Stand);
        }

        // =============================================================
        // A9-2：有已持有、分級允許的服裝時，使用服裝的 art
        // =============================================================

        [Test]
        public void A9_2_OwnedAndRatingAllowedCostume_UsesCostumeArt()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            var claire = new CharacterState
            {
                UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" },
                PortraitStage = "ps_char01_s1",
                Costume = "cos_char01_academy"
            };
            player.Characters["char01_claire"] = claire;
            player.Costumes.Add("cos_char01_academy"); // 已持有

            ResolvedPortrait resolved = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");

            Assert.AreEqual("char01_claire", resolved.Character);
            Assert.AreEqual("ps_char01_s1", resolved.PortraitStage);
            Assert.AreEqual("cos_char01_academy", resolved.Costume);
            Assert.IsNotNull(resolved.Art);
            Assert.AreEqual("char/char01_claire/portrait/cos_char01_academy/stand", resolved.Art.Stand);
            Assert.AreEqual(Rating.G, resolved.Rating);
        }

        // =============================================================
        // A9-3：劇情強制服裝優先於玩家服裝，且不需要持有
        // =============================================================

        [Test]
        public void A9_3_ForcedCostume_OverridesPlayerSelection_AndDoesNotRequireOwnership()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            var claire = new CharacterState
            {
                UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" },
                PortraitStage = "ps_char01_s1",
                Costume = "cos_char01_default"
            };
            player.Characters["char01_claire"] = claire;
            player.Costumes.Add("cos_char01_default");
            // 注意：玩家並未持有 cos_char01_academy

            // 劇情強制指定 cos_char01_academy
            ResolvedPortrait resolved = PortraitResolver.Resolve(
                m, player, DefaultRatingPolicy(), "char01_claire", forcedCostume: "cos_char01_academy");

            Assert.AreEqual("char01_claire", resolved.Character);
            Assert.AreEqual("cos_char01_academy", resolved.Costume, "劇情強制服裝應覆蓋玩家選用服裝");
            Assert.AreEqual("char/char01_claire/portrait/cos_char01_academy/stand", resolved.Art.Stand);
        }

        // =============================================================
        // A9-4：服裝無效條件退回立繪階段
        // 包含：未持有、分級不允許、屬於其他角色、缺少 requiresPortraitStage
        // =============================================================

        [Test]
        public void A9_4_InvalidCostumeConditions_FallBackToPortraitStage()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            var claire = new CharacterState
            {
                UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" },
                PortraitStage = "ps_char01_s1"
            };
            player.Characters["char01_claire"] = claire;

            // 1. 未持有該服裝：退回立繪階段
            claire.Costume = "cos_char01_academy";
            ResolvedPortrait fallbackUnowned = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");
            Assert.IsNull(fallbackUnowned.Costume, "未持有服裝時應退回無服裝");
            Assert.AreEqual("ps_char01_s1", fallbackUnowned.PortraitStage);
            Assert.AreEqual("char/char01_claire/portrait/ps_char01_s1/stand", fallbackUnowned.Art.Stand);

            // 2. 屬於其他角色的服裝：退回立繪階段
            claire.Costume = "cos_char02_default"; // Luna 的服裝
            player.Costumes.Add("cos_char02_default");
            ResolvedPortrait fallbackOtherChar = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");
            Assert.IsNull(fallbackOtherChar.Costume, "屬於其他角色的服裝應退回");
            Assert.AreEqual("ps_char01_s1", fallbackOtherChar.PortraitStage);

            // 3. 缺少 requiresPortraitStage：退回立繪階段
            var cosReqS2 = new CostumeDef
            {
                Id = "cos_char01_s2_exclusive",
                Character = "char01_claire",
                Rating = Rating.G,
                RequiresPortraitStage = "ps_char01_s2",
                Art = new PortraitArt { Stand = "stand_req_s2" }
            };
            m.Costumes[cosReqS2.Id] = cosReqS2;
            player.Costumes.Add(cosReqS2.Id);
            claire.Costume = cosReqS2.Id;

            // 目前 Claire 只有解鎖 s1，未解鎖 s2
            ResolvedPortrait fallbackMissingStage = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");
            Assert.IsNull(fallbackMissingStage.Costume, "未解鎖 requiresPortraitStage 時應退回立繪階段");

            // 當解鎖 s2 後，該服裝即可正常啟用
            claire.UnlockedPortraitStages.Add("ps_char01_s2");
            ResolvedPortrait successWithStage = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");
            Assert.AreEqual(cosReqS2.Id, successWithStage.Costume, "解鎖指定階段後應正常穿戴");

            // 4. 分級不允許 (R18 服裝在 G 政策下)：退回立繪階段
            var cosR18 = new CostumeDef
            {
                Id = "cos_char01_r18",
                Character = "char01_claire",
                Rating = Rating.R18,
                Art = new PortraitArt { Stand = "stand_r18" }
            };
            m.Costumes[cosR18.Id] = cosR18;
            player.Costumes.Add(cosR18.Id);
            claire.Costume = cosR18.Id;

            var gRatingPolicy = new RatingPolicy(Rating.G, Rating.G);
            ResolvedPortrait fallbackRating = PortraitResolver.Resolve(m, player, gRatingPolicy, "char01_claire");
            Assert.IsNull(fallbackRating.Costume, "分級不允許的服裝應退回立繪階段");
            Assert.AreEqual("ps_char01_s1", fallbackRating.PortraitStage);
        }

        // =============================================================
        // A9-5：選用的立繪階段分級不允許或尚未解鎖時，退回已解鎖、分級允許的最低階段
        // =============================================================

        [Test]
        public void A9_5_SelectedStageLockedOrRatingRestricted_FallsBackToLowestAllowed()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();
            var claire = new CharacterState
            {
                UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" },
                PortraitStage = "ps_char01_s3" // 嘗試選用未解鎖的 S3 階段
            };
            player.Characters["char01_claire"] = claire;

            ResolvedPortrait fallbackLocked = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");
            Assert.AreEqual("ps_char01_s1", fallbackLocked.PortraitStage, "選用未解鎖階段應退回已解鎖最低階段 S1");

            // 測試分級限制立繪階段退回
            var psR18 = new PortraitStageDef
            {
                Id = "ps_char01_r18_stage",
                Character = "char01_claire",
                Order = 4,
                Rating = Rating.R18,
                Art = new PortraitArt { Stand = "stand_r18_stage" }
            };
            m.PortraitStages[psR18.Id] = psR18;
            claire.UnlockedPortraitStages.Add(psR18.Id);
            claire.PortraitStage = psR18.Id;

            var gRatingPolicy = new RatingPolicy(Rating.G, Rating.G);
            ResolvedPortrait fallbackRating = PortraitResolver.Resolve(m, player, gRatingPolicy, "char01_claire");
            Assert.AreEqual("ps_char01_s1", fallbackRating.PortraitStage, "分級不允許之階段應退回分級允許之最低階段 S1");
        }

        // =============================================================
        // A9-6：角色未持有（例如轉蛋預覽）時，回傳第一個立繪階段
        // =============================================================

        [Test]
        public void A9_6_UnownedCharacterPreview_ReturnsFirstPortraitStage()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState(); // 未持有任何角色

            ResolvedPortrait unownedPreview = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char03_scarlet");
            Assert.AreEqual("char03_scarlet", unownedPreview.Character);
            Assert.AreEqual("ps_char03_s1", unownedPreview.PortraitStage, "未持有角色應回傳第 1 立繪階段");
            Assert.IsNull(unownedPreview.Costume);
            Assert.IsNotNull(unownedPreview.Art);
        }

        // =============================================================
        // A9-7：主資料沒有 art 時，Art == null，不丟出例外（呈現層顯示 placeholder）
        // =============================================================

        [Test]
        public void A9_7_MissingArtInMasterData_ReturnsNullArtWithoutThrowing()
        {
            MasterDatabase m = LoadCleanMaster();
            PlayerState player = new PlayerState();

            // 模擬主資料立繪階段未配置 Art (Art == null)
            m.PortraitStages["ps_char01_s1"].Art = null;

            ResolvedPortrait resolved = null;
            Assert.DoesNotThrow(() =>
            {
                resolved = PortraitResolver.Resolve(m, player, DefaultRatingPolicy(), "char01_claire");
            });

            Assert.IsNotNull(resolved);
            Assert.AreEqual("ps_char01_s1", resolved.PortraitStage);
            Assert.IsNull(resolved.Art, "主資料缺少 Art 時應為 null 由呈現層顯示 placeholder，不得拋出例外");
        }
    }
}
