using System;
using System.IO;
using NUnit.Framework;
using Game.Domain;
using Game.Core.Assets;
using Game.Core.Story.MiniScript;

namespace Game.Tests
{
    [TestFixture]
    public class AddressablesCatalogAuditorTests
    {
        [Test]
        public void F7_1_BaseCatalog_CleanGAndR15_PassesAudit()
        {
            var catalog = new AddressablesCatalog { CatalogId = "catalog_base" };
            catalog.AddEntry(new AddressablesEntry("char01_stand_s1", "Assets/Art/characters/char01_claire/s1.png", AddressablesCatalogAuditor.GroupContentG, Rating.G, "character", "stand"));
            catalog.AddEntry(new AddressablesEntry("ui_button_deploy", "Assets/UI/Sprites/btn_deploy.png", AddressablesCatalogAuditor.GroupContentG, Rating.G, "ui"));
            catalog.AddEntry(new AddressablesEntry("char02_swimsuit_s2", "Assets/Art/characters/char02_elysia/s2_swim.png", AddressablesCatalogAuditor.GroupContentR15, Rating.R15, "costume"));

            var report = AddressablesCatalogAuditor.AuditBaseCatalog(catalog);
            Assert.IsTrue(report.IsValid, "純 G 與 R15 的本體 Catalog 應順利通過審計");
            Assert.AreEqual(0, report.Errors.Count);
            Assert.AreEqual(3, report.TotalScanned);

            // 確保 CI 閘門不拋出異常
            Assert.DoesNotThrow(() => AddressablesCatalogAuditor.AssertBaseCatalogClean(catalog));
        }

        [Test]
        public void F7_2_PositiveControl_R18KeyInContentG_BuildFails()
        {
            // 陽性對照：故意放一個 R18 素材進本體 group (content_g)
            var catalog = new AddressablesCatalog { CatalogId = "catalog_base" };
            catalog.AddEntry(new AddressablesEntry("char01_stand_s1", "Assets/Art/characters/char01_claire/s1.png", AddressablesCatalogAuditor.GroupContentG, Rating.G));
            
            // 注入 R18 素材
            catalog.AddEntry(new AddressablesEntry(
                "cg_elysia_climax_01",
                "Assets/Art/cg/tier3_climax_poses/elysia_01.png",
                AddressablesCatalogAuditor.GroupContentG,
                Rating.R18,
                "cg"));

            var report = AddressablesCatalogAuditor.AuditBaseCatalog(catalog);
            Assert.IsFalse(report.IsValid, "陽性對照：本體 Catalog 混入 R18 素材必須審計失敗！");
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("ERR_R18_KEY_IN_BASE_CATALOG")), "必須報出 ERR_R18_KEY 錯誤");
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("ERR_R18_FOLDER_IN_BASE_CATALOG")), "必須報出 ERR_R18_FOLDER 錯誤");

            // 驗證 CI 閘門拋出阻斷建置之例外
            var ex = Assert.Throws<InvalidOperationException>(() => AddressablesCatalogAuditor.AssertBaseCatalogClean(catalog));
            StringAssert.Contains("BUILD REJECTED", ex.Message);
        }

        [Test]
        public void F7_3_PositiveControl_ContentR18GroupInBaseCatalog_BuildFails()
        {
            // 陽性對照：故意將 content_r18 群組打包進本體 catalog
            var catalog = new AddressablesCatalog { CatalogId = "catalog_base" };
            catalog.AddEntry(new AddressablesEntry(
                "dlc_scene_climax",
                "Assets/DLC/r18_scenes/scene01.unity",
                AddressablesCatalogAuditor.GroupContentR18,
                Rating.R18));

            var report = AddressablesCatalogAuditor.AuditBaseCatalog(catalog);
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("ERR_R18_GROUP_IN_BASE_CATALOG")), "必須攔截 R18 Group 混入本體 Catalog");
        }

        [Test]
        public void F7_4_PositiveControl_AdultLabelInBaseCatalog_BuildFails()
        {
            // 陽性對照：素材 rating 雖標記為 G，但帶有 nsfw 標籤
            var catalog = new AddressablesCatalog { CatalogId = "catalog_base" };
            catalog.AddEntry(new AddressablesEntry(
                "suspicious_art",
                "Assets/Art/temp/art_01.png",
                AddressablesCatalogAuditor.GroupContentG,
                Rating.G,
                "ui", "nsfw"));

            var report = AddressablesCatalogAuditor.AuditBaseCatalog(catalog);
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("ERR_R18_LABEL_IN_BASE_CATALOG")), "必須攔截帶有 adult/nsfw label 的資產");
        }

        [Test]
        public void F7_5_AdultDlcCatalog_ProperlySeparatedFromBase()
        {
            // 模擬正常發行情況：本體 catalog 與 DLC catalog 乾淨分離
            var baseCatalog = new AddressablesCatalog { CatalogId = "catalog_base" };
            baseCatalog.AddEntry(new AddressablesEntry("stand_g", "Assets/Art/g/stand.png", AddressablesCatalogAuditor.GroupContentG, Rating.G));
            baseCatalog.AddEntry(new AddressablesEntry("swim_r15", "Assets/Art/r15/swim.png", AddressablesCatalogAuditor.GroupContentR15, Rating.R15));

            var dlcCatalog = new AddressablesCatalog { CatalogId = "catalog_dlc_r18" };
            dlcCatalog.AddEntry(new AddressablesEntry("cg_h_scene", "Assets/DLC/tier4_aftermath/cg01.png", AddressablesCatalogAuditor.GroupContentR18, Rating.R18, "r18"));

            // 1. 本體審計：100% 乾淨
            var baseReport = AddressablesCatalogAuditor.AuditBaseCatalog(baseCatalog);
            Assert.IsTrue(baseReport.IsValid);
            Assert.AreEqual(0, baseReport.Errors.Count);

            // 2. 本體絕無 DLC 的 key
            Assert.IsFalse(baseCatalog.Entries.Exists(e => e.AddressKey == "cg_h_scene"));
        }

        [Test]
        public void M3_5_1_BuildSplitManifests_SplitsAssetManifest_BasePassesAuditWithoutR18()
        {
            string manifestPath = Path.Combine(TestPaths.DataDir(), "asset_manifest.csv");
            Assert.IsTrue(File.Exists(manifestPath), "asset_manifest.csv 必須存在");

            var catalog = new AssetCatalog();
            catalog.LoadFromFile(manifestPath);
            Assert.Greater(catalog.Entries.Count, 0, "AssetCatalog 應成功載入條目");

            // 執行拆分
            SplitManifestResult split = AddressablesCatalogAuditor.BuildSplitManifests(catalog);
            Assert.IsNotNull(split);
            Assert.IsNotNull(split.BaseCatalog);
            Assert.IsNotNull(split.AdultDlcCatalog);

            // 驗證本體 Catalog
            Assert.Greater(split.BaseCatalog.Entries.Count, 0, "BaseCatalog 應包含全年齡與 R15 素材");
            Assert.IsTrue(split.BaseAuditReport.IsValid, "BaseCatalog 雙重安全審計必須 100% 通過（0 筆 R18）");
            Assert.AreEqual(0, split.BaseAuditReport.Errors.Count);

            foreach (var entry in split.BaseCatalog.Entries)
            {
                Assert.Less(entry.Rating, Rating.R18, $"BaseCatalog 條目 {entry.AddressKey} 分級不可為 R18");
                Assert.AreNotEqual(AddressablesCatalogAuditor.GroupContentR18, entry.GroupName);
            }

            // 驗證成人 DLC Catalog
            Assert.Greater(split.AdultDlcCatalog.Entries.Count, 0, "AdultDlcCatalog 應包含 R18 素材");
            foreach (var entry in split.AdultDlcCatalog.Entries)
            {
                Assert.GreaterOrEqual(entry.Rating, Rating.R18, $"AdultDlcCatalog 條目 {entry.AddressKey} 分級必須 >= R18");
                Assert.AreEqual(AddressablesCatalogAuditor.GroupContentR18, entry.GroupName);
            }

            // CI 閘門不拋出異常
            Assert.DoesNotThrow(() => AddressablesCatalogAuditor.AssertBaseCatalogClean(split.BaseCatalog));
        }

        [Test]
        public void M3_5_2_PositiveControl_Tier3ClimaxAssetInBaseCatalog_InterceptedWithErrR18InBaseCatalog()
        {
            // 陽性對照：故意將一筆 tier3_climax_poses/ 資產混入 BaseCatalog
            var baseCatalog = new AddressablesCatalog { CatalogId = "catalog_base" };
            baseCatalog.AddEntry(new AddressablesEntry("ui_btn_play", "Assets/UI/btn_play.png", AddressablesCatalogAuditor.GroupContentG, Rating.G));
            
            // 故意混入未經審查的 R18 路徑資產（即使 rating 偽裝為 G）
            baseCatalog.AddEntry(new AddressablesEntry(
                "cg_leaked_climax",
                "sample/anime/tier3_climax_poses/anime_tier3_char01_claire_pony.png",
                AddressablesCatalogAuditor.GroupContentG,
                Rating.G));

            var report = AddressablesCatalogAuditor.AuditBaseCatalog(baseCatalog);
            Assert.IsFalse(report.IsValid, "陽性對照：混入 tier3_climax_poses 素材之 BaseCatalog 必須審計失敗！");
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("ERR_R18_IN_BASE_CATALOG")), "必須攔截並回報 ERR_R18_IN_BASE_CATALOG");
            Assert.IsTrue(report.Errors.Exists(e => e.Contains("ERR_R18_FOLDER_IN_BASE_CATALOG")), "必須標註 ERR_R18_FOLDER_IN_BASE_CATALOG");

            // 驗證拋出例外阻斷建置
            var ex = Assert.Throws<InvalidOperationException>(() => AddressablesCatalogAuditor.AssertBaseCatalogClean(baseCatalog));
            StringAssert.Contains("BUILD REJECTED", ex.Message);
        }

        [Test]
        public void M3_5_3_DefenseInDepth_DlcNotInstalledOrAgeGateUnverified_LocksToG_AndFallsBackSafely()
        {
            string manifestPath = Path.Combine(TestPaths.DataDir(), "asset_manifest.csv");
            var catalog = new AssetCatalog();
            catalog.LoadFromFile(manifestPath);

            // 1. 情境 A：成人 DLC 未安裝 (isDlcInstalled == false)，玩家設定勾選 R18
            var policyA = AddressablesCatalogAuditor.DetermineEffectivePolicy(Rating.R18, isDlcInstalled: false, isAgeVerified: false);
            Assert.AreEqual(Rating.G, policyA.Effective, "成人 DLC 未安裝時有效分級必須降鎖為 G");
            Assert.IsFalse(policyA.Allows(Rating.R18));

            // 資產請求阻斷
            var resR18A = catalog.Resolve("cg/cg_char01_t3_01", policyA);
            Assert.IsFalse(resR18A.Allowed, "未安裝成人 DLC 時 R18 CG 必須被阻擋");
            Assert.AreEqual("RatingDisallowed", resR18A.Reason);

            var resGA = catalog.Resolve("cg/cg_elysia_smile_g", policyA);
            Assert.IsTrue(resGA.Allowed, "全年齡素材依然可正常取用");

            // 2. 情境 B：成人 DLC 已安裝但年齡確認未通過 (isAgeVerified == false)
            var policyB = AddressablesCatalogAuditor.DetermineEffectivePolicy(Rating.R18, isDlcInstalled: true, isAgeVerified: false);
            Assert.AreEqual(Rating.G, policyB.Effective, "年齡未確認時有效分級必須維持 G");
            Assert.IsFalse(policyB.Allows(Rating.R18));

            // 3. 情境 C：MiniScript ADV 劇本節點 R18 防禦降級
            const string script = @"
[node SceneRoot]
jump AdultIntimateNode

[node AdultIntimateNode rating:R18 fallback:GentleSafeNode]
line l1 Claire r18_dialogue
end

[node GentleSafeNode]
line l2 Claire gentle_hug_dialogue
end
";
            var runner = new MiniScriptRunner(script, policyB);
            runner.Start("SceneRoot");
            runner.Continue(); // jump -> 自動 fallback 到 GentleSafeNode -> l2
            Assert.AreEqual("l2", runner.CurrentLine.LineId);
            Assert.AreEqual("gentle_hug_dialogue", runner.CurrentLine.TextKey, "有效分級為 G 時必須自動轉進 fallback 安全節點");

            // 4. 情境 D：DLC 已安裝且年齡已確認，且玩家 opt-in 為 R18
            var policyD = AddressablesCatalogAuditor.DetermineEffectivePolicy(Rating.R18, isDlcInstalled: true, isAgeVerified: true);
            Assert.AreEqual(Rating.R18, policyD.Effective, "DLC 已安裝且年齡已確認後升級為 R18");
            Assert.IsTrue(policyD.Allows(Rating.R18));

            var resR18D = catalog.Resolve("cg/cg_char01_t3_01", policyD);
            Assert.IsTrue(resR18D.Allowed, "雙重授權具備後允許取得 R18 CG");

            var runnerD = new MiniScriptRunner(script, policyD);
            runnerD.Start("SceneRoot");
            runnerD.Continue(); // jump -> 直接進入 AdultIntimateNode -> l1
            Assert.AreEqual("l1", runnerD.CurrentLine.LineId);
            Assert.AreEqual("r18_dialogue", runnerD.CurrentLine.TextKey, "R18 授權生效時正常進入成人節點");
        }
    }
}
