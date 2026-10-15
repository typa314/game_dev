using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Assets;
using Game.Core.Localization;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 工單 I 系列：資料與工具管線測試（DataPipelineTests）
    /// 涵蓋查核點：
    /// I1: 主資料驗證（合法資料通過、違規資料報錯攔截）
    /// I2: 主資料同步驗證機制（預檢驗證與無效資料阻絕）
    /// I3: 資產 Manifest 匯入與分級隔離（資料夾分級下限保護、R18 隔離至 DLC、G/R15 納入本體）
    /// I4: 劇本驗證（語法與 6 大邏輯檢查、陽性對照攔截）
    /// I5: 多語言字串匯出入與格式檢查（CSV 導出入可逆、佔位符一致性校驗）
    /// </summary>
    [TestFixture]
    public class DataPipelineTests
    {
        private string _tempDir;
        private MasterDatabase _master;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PipelineTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
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
        // I1: 主資料驗證 CLI
        // =========================================================================================
        [Test]
        public void I1_MasterDataValidation_PassesValidData_AndRejectsInvalidData()
        {
            // 陰性對照：乾淨主資料庫 100% 通過
            var cleanErrors = MasterDataValidator.Validate(_master);
            Assert.That(cleanErrors, Is.Empty, "合規主資料庫校驗應無錯誤");

            // 陽性對照：刻意製造非法卡池配置（例如 pickup 角色不在池中）
            var mutatedMaster = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            mutatedMaster.Banners["banner_std"].Pickup = new BannerPickup
            {
                Chars = new List<string> { "ghost_character_999" },
                ShareOfRarity = 0.5
            };

            var mutatedErrors = MasterDataValidator.Validate(mutatedMaster);
            Assert.IsTrue(mutatedErrors.Count > 0, "違規主資料必須被校驗器抓出");
            Assert.IsTrue(mutatedErrors.Exists(e => e.Contains("ghost_character_999")), "錯誤訊息應指出違規之 ghost_character_999");
        }

        // =========================================================================================
        // I2: 主資料同步
        // =========================================================================================
        [Test]
        public void I2_MasterDataSync_RejectsSyncWhenDataHasErrors()
        {
            string srcDir = Path.Combine(_tempDir, "master_src");
            string dstDir = Path.Combine(_tempDir, "StreamingAssets_MasterData");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(dstDir);

            // 寫入損壞的 JSON（缺少必要欄位或結構錯誤）
            File.WriteAllText(Path.Combine(srcDir, "config.json"), "{ invalid_json: true }");

            bool syncAttemptSucceeded = false;
            try
            {
                MasterDatabase loaded = MasterDataLoader.LoadFromDirectory(srcDir);
                var errors = MasterDataValidator.Validate(loaded);
                if (errors.Count == 0)
                {
                    syncAttemptSucceeded = true;
                }
            }
            catch
            {
                syncAttemptSucceeded = false;
            }

            Assert.IsFalse(syncAttemptSucceeded, "I2: 主資料存在 JSON 語法或規格錯誤時，同步機制必須拒絕執行");
        }

        // =========================================================================================
        // I3: 資產 Manifest 匯入與分級隔離
        // =========================================================================================
        [Test]
        public void I3_AssetImporter_EnforcesFolderRatingMinimum_AndIsolatesR18ToDLC()
        {
            string manifestPath = Path.Combine(_tempDir, "asset_manifest.csv");
            string sourceRoot = Path.Combine(_tempDir, "game_source");
            string outBase = Path.Combine(_tempDir, "BaseGame");
            string outAdult = Path.Combine(_tempDir, "AdultDLC");

            Directory.CreateDirectory(Path.Combine(sourceRoot, "tier1_dating"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "tier3_climax_poses"));

            // 建立測試假圖檔
            string datingFile = Path.Combine(sourceRoot, "tier1_dating", "claire_date.png");
            string adultFile = Path.Combine(sourceRoot, "tier3_climax_poses", "claire_climax.png");
            File.WriteAllBytes(datingFile, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(adultFile, new byte[] { 4, 5, 6 });

            // 1. 測試違規分級（tier3 資料夾素材卻標成 G）-> 陽性對照強制攔截
            string illegalManifest = @"key,source,rating,status,note
cg/cg_dating,tier1_dating/claire_date.png,G,draft,date
cg/cg_climax,tier3_climax_poses/claire_climax.png,G,draft,illegal_under_folder_min
";
            File.WriteAllText(manifestPath, illegalManifest);

            var options = new AssetImportOptions
            {
                ManifestPath = manifestPath,
                SourceRootDir = sourceRoot,
                BaseContentDir = outBase,
                AdultContentDir = outAdult,
                CopyFiles = true
            };

            var illegalReport = AssetImportService.Execute(options);
            Assert.IsFalse(illegalReport.Success, "I3: 標註低於資料夾分級下限之素材必須攔截");
            Assert.IsTrue(illegalReport.Errors.Exists(e => e.Contains("ERR_RATING_BELOW_FOLDER_MINIMUM")), "應回報 ERR_RATING_BELOW_FOLDER_MINIMUM 錯誤");

            // 2. 測試正確合規分級 -> 成功分流（G/R15 進 BaseGame，R18 嚴格隔離進 AdultDLC）
            string legalManifest = @"key,source,rating,status,note
cg/cg_dating,tier1_dating/claire_date.png,G,draft,date
cg/cg_climax,tier3_climax_poses/claire_climax.png,R18,draft,legal_r18
";
            File.WriteAllText(manifestPath, legalManifest);

            var legalReport = AssetImportService.Execute(options);
            Assert.IsTrue(legalReport.Success, "合規資產 Manifest 匯入應成功");
            Assert.AreEqual(1, legalReport.ImportedG);
            Assert.AreEqual(1, legalReport.ExcludedR18);

            // 驗證實體檔案落地隔離：
            // G 級素材出現在 BaseGame，R18 素材出現在 AdultDLC，BaseGame 絕對沒有 R18 素材！
            Assert.IsTrue(File.Exists(Path.Combine(outBase, "cg", "cg_dating.png")), "G 級素材應存入 BaseGame");
            Assert.IsFalse(File.Exists(Path.Combine(outBase, "cg", "cg_climax.png")), "R18 素材絕對不得混入 BaseGame！");
            Assert.IsTrue(File.Exists(Path.Combine(outAdult, "cg", "cg_climax.png")), "R18 素材應安全隔離存入 AdultDLC");
        }

        // =========================================================================================
        // I4: 劇本驗證
        // =========================================================================================
        [Test]
        public void I4_ValidateStory_ValidatesLegality_AndCatchesErrors()
        {
            // 陰性對照：合法劇本
            const string validScriptText = @"
[node Start]
command show char01_claire
line l1 Claire hello_commander
jump EndNode

[node EndNode]
line l2 Claire farewell
end
";
            var validScript = MiniScript.Parse(validScriptText);
            var validRes = StoryValidator.Validate(validScript, _master);
            Assert.IsTrue(validRes.IsValid, "合規劇本應通過校驗");

            // 陽性對照：非法未註冊指令
            const string badCmdScriptText = @"
[node Start]
command drop_database all
line l1 Claire hello
end
";
            var badScript = MiniScript.Parse(badCmdScriptText);
            var badRes = StoryValidator.Validate(badScript, _master);
            Assert.IsFalse(badRes.IsValid);
            Assert.IsTrue(badRes.HasError(StoryValidationError.UnregisteredCommand), "應攔截未註冊之無效指令");
        }

        // =========================================================================================
        // I5: 多語言字串匯出與匯入
        // =========================================================================================
        [Test]
        public void I5_LocalizationCsv_ExportAndImport_IsLosslessAndReversible()
        {
            var loc = new LocalizationService();
            loc.LoadFromJson("zh-TW", "{\"ui.play\": \"開始遊玩\", \"stage.name\": \"關卡 {0}\"}");
            loc.LoadFromJson("en", "{\"ui.play\": \"Play Game\", \"stage.name\": \"Stage {0}\"}");

            // 1. 匯出至 CSV
            string csv = LocalizationCsvTool.ExportToCsv(loc, "zh-TW");
            Assert.IsTrue(csv.Contains("Key,Context,en,zh-TW") || csv.Contains("Key,Context,zh-TW,en"));
            Assert.IsTrue(csv.Contains("ui.play"));
            Assert.IsTrue(csv.Contains("開始遊玩"));
            Assert.IsTrue(csv.Contains("Play Game"));

            // 2. 從 CSV 讀回
            var imported = LocalizationCsvTool.ImportFromCsv(csv);
            Assert.IsTrue(imported.ContainsKey("zh-TW"));
            Assert.IsTrue(imported.ContainsKey("en"));
            Assert.AreEqual("開始遊玩", imported["zh-TW"]["ui.play"]);
            Assert.AreEqual("Play Game", imported["en"]["ui.play"]);
            Assert.AreEqual("關卡 {0}", imported["zh-TW"]["stage.name"]);
            Assert.AreEqual("Stage {0}", imported["en"]["stage.name"]);

            // 3. 佔位符比對檢核
            var valRes = LocalizationValidator.Validate(loc, master: null, "zh-TW");
            Assert.IsTrue(valRes.IsValid, "佔位符一致時校驗應通過");

            // 陽性對照：故意破壞英文版佔位符 {0} 為 {1}
            loc.LoadFromJson("en", "{\"ui.play\": \"Play Game\", \"stage.name\": \"Stage {1}\"}");
            var badValRes = LocalizationValidator.Validate(loc, master: null, "zh-TW");
            Assert.IsFalse(badValRes.IsValid, "佔位符不一致時應報錯");
            Assert.IsTrue(badValRes.Errors.Exists(e => e.Contains("ERR_PLACEHOLDER_MISMATCH")));
        }
    }
}
