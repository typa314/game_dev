using System;
using System.IO;
using System.Threading;
using Game.Core;
using Game.Core.Diagnostics;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public class DiagnosticsAndReleaseAuditTests
    {
        [Test]
        public void M3_7_1_LocalCrashLogger_Continuous15Logs_RetainsExactly10Newest_AndContainsStackTrace()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_crash_logs_" + Guid.NewGuid().ToString("N"));
            try
            {
                var logger = new LocalCrashLogger(tempDir, maxLogFiles: 10);
                Assert.AreEqual(0, logger.GetLogFileCount());

                // 連續寫入 15 筆當機日誌
                for (int i = 0; i < 15; i++)
                {
                    try
                    {
                        throw new InvalidOperationException($"Simulated crash exception #{i:D2} for testing LocalCrashLogger");
                    }
                    catch (Exception ex)
                    {
                        logger.LogCrash(ex, $"Context_Step_{i:D2}");
                    }
                    // 微小延遲確保檔案建立時間與時間戳不重疊
                    Thread.Sleep(15);
                }

                // 驗證數量：嚴格保留 10 份日誌
                int fileCount = logger.GetLogFileCount();
                Assert.AreEqual(10, fileCount, "寫入 15 筆日誌後，目錄中應精確保留最新 10 份日誌");

                string[] logFiles = Directory.GetFiles(tempDir, "crash_*.log");
                Assert.AreEqual(10, logFiles.Length);

                // 檢查最舊的 5 筆日誌 (00 ~ 04) 已被輪替清理，絕不可出現
                for (int oldIdx = 0; oldIdx < 5; oldIdx++)
                {
                    string oldPattern = $"Simulated crash exception #{oldIdx:D2}";
                    foreach (var file in logFiles)
                    {
                        string content = File.ReadAllText(file);
                        StringAssert.DoesNotContain(oldPattern, content, $"最舊的記錄 #{oldIdx} 應已被輪替清理");
                    }
                }

                // 檢查最新的一筆日誌 (14) 必須存在且結構完整
                bool foundNewest = false;
                foreach (var file in logFiles)
                {
                    string content = File.ReadAllText(file);
                    StringAssert.Contains("=== LOCAL CRASH REPORT (PROJECT TOWER) ===", content);
                    StringAssert.Contains("--- STACK TRACE ---", content);

                    if (content.Contains("Simulated crash exception #14"))
                    {
                        foundNewest = true;
                        StringAssert.Contains("InvalidOperationException", content);
                        StringAssert.Contains("Context_Step_14", content);
                    }
                }
                Assert.IsTrue(foundNewest, "最新的一筆當機記錄 #14 必須完整保留");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Test]
        public void M3_7_2_ReleaseReadinessAuditor_DevModePasses_StrictModeInterceptsPlaceholderAndMissingLoc_PositiveControl()
        {
            var master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            string locDir = Path.Combine(TestPaths.DataDir(), "loc");
            string manifestPath = Path.Combine(TestPaths.DataDir(), "asset_manifest.csv");

            // 1. 開發模式 (strictReleaseMode: false) 下檢查現有專案
            var devReport = ReleaseReadinessAuditor.RunAudit(master, locDir, manifestPath, strictReleaseMode: false);
            Assert.IsTrue(devReport.IsPassed, "開發模式下全專案主資料、四語系與本體資產必須通過審計");
            Assert.AreEqual(0, devReport.Errors.Count);
            Assert.AreEqual(9, devReport.MasterTablesScanned);
            Assert.AreEqual(4, devReport.LocalesScanned);
            Assert.Greater(devReport.BaseAssetsScanned, 0);

            // 2. 陽性對照 A：當資產清單含有 placeholder 項目時，發行模式 (strictReleaseMode: true) 嚴格阻擋發行
            string tempManifestPath = Path.Combine(Path.GetTempPath(), "test_placeholder_manifest_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                string originalCsv = File.ReadAllText(manifestPath);
                string modifiedCsv = originalCsv + "\ncg/cg_test_placeholder,sample/placeholder.png,G,placeholder,test_placeholder\n";
                File.WriteAllText(tempManifestPath, modifiedCsv);

                var strictReport = ReleaseReadinessAuditor.RunAudit(master, locDir, tempManifestPath, strictReleaseMode: true);
                Assert.IsFalse(strictReport.IsPassed, "陽性對照：發行模式嚴格禁止包含 placeholder 佔位素材");
                Assert.IsTrue(strictReport.Errors.Exists(e => e.Contains("ERR_PLACEHOLDER_IN_RELEASE")), "必須攔截並報出 ERR_PLACEHOLDER_IN_RELEASE");
                Assert.Greater(strictReport.PlaceholderAssetCount, 0);
            }
            finally
            {
                if (File.Exists(tempManifestPath)) File.Delete(tempManifestPath);
            }

            // 3. 陽性對照 B：缺少語系字典時攔截
            string tempLocDir = Path.Combine(Path.GetTempPath(), "test_missing_loc_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempLocDir);
                // 僅拷貝 zh-TW，刻意缺漏 en, ja, zh-CN
                Directory.CreateDirectory(Path.Combine(tempLocDir, "zh-TW"));
                File.Copy(Path.Combine(locDir, "zh-TW", "strings.json"), Path.Combine(tempLocDir, "zh-TW", "strings.json"));

                var missingLocReport = ReleaseReadinessAuditor.RunAudit(master, tempLocDir, manifestPath, strictReleaseMode: false);
                Assert.IsFalse(missingLocReport.IsPassed, "陽性對照：缺少必備語系時必須審計失敗");
                Assert.IsTrue(missingLocReport.Errors.Exists(e => e.Contains("ERR_LOCALE_MISSING")), "必須報出 ERR_LOCALE_MISSING 錯誤");
            }
            finally
            {
                if (Directory.Exists(tempLocDir))
                {
                    try { Directory.Delete(tempLocDir, true); } catch { }
                }
            }

            // 4. 陽性對照 C：主資料為 null 時攔截
            var nullMasterReport = ReleaseReadinessAuditor.RunAudit(null, locDir, manifestPath, strictReleaseMode: false);
            Assert.IsFalse(nullMasterReport.IsPassed);
            Assert.IsTrue(nullMasterReport.Errors.Exists(e => e.Contains("ERR_MASTER_NULL")), "Master 為 null 必須報錯阻斷");
        }
    }
}
