using System;
using NUnit.Framework;
using Game.Domain;
using Game.Core.Assets;

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
    }
}
