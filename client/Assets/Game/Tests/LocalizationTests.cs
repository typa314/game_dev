using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Localization;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class LocalizationTests
    {
        private MasterDatabase _master;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        [Test]
        public void D1_1_LocKeyReturnsCorrectTextInZhTwAndEn()
        {
            var loc = new LocalizationService("zh-TW", "zh-TW");
            loc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "char/char01_claire.name", "克萊兒" }
            });
            loc.LoadLocale("en", new Dictionary<string, string>
            {
                { "char/char01_claire.name", "Claire" }
            });

            // With loc: prefix
            Assert.AreEqual("克萊兒", loc.Get("loc:char/char01_claire.name"));

            // Without loc: prefix
            Assert.AreEqual("克萊兒", loc.Get("char/char01_claire.name"));

            // Switch to en
            loc.SetLocale("en");
            Assert.AreEqual("Claire", loc.Get("loc:char/char01_claire.name"));
        }

        [Test]
        public void D1_2_LocaleChangedEventTriggersAndSubsequentGetsReturnNewLanguage()
        {
            var loc = new LocalizationService("zh-TW");
            loc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "msg", "你好" }
            });
            loc.LoadLocale("en", new Dictionary<string, string>
            {
                { "msg", "Hello" }
            });

            string eventFiredWith = null;
            loc.LocaleChanged += newLocale => eventFiredWith = newLocale;

            Assert.AreEqual("你好", loc.Get("msg"));

            loc.SetLocale("en");
            Assert.AreEqual("en", eventFiredWith);
            Assert.AreEqual("Hello", loc.Get("msg"));
        }

        [Test]
        public void D1_3_MissingKeyFallsBackToZhTwWithWarning_AndReturnsBracketKeyIfMissingInBoth()
        {
            var loc = new LocalizationService("en", "zh-TW");
            loc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "only_in_tw", "繁中獨有文字" }
            });
            loc.LoadLocale("en", new Dictionary<string, string>
            {
                { "english_only", "English" }
            });

            // Key missing in 'en', but exists in 'zh-TW'
            string res = loc.Get("only_in_tw");
            Assert.AreEqual("繁中獨有文字", res);
            Assert.IsTrue(loc.Warnings.Count > 0, "Warning should be recorded on fallback.");
            StringAssert.Contains("Missing key 'only_in_tw'", loc.Warnings[0]);

            // Key missing in both
            string missing = loc.Get("unknown_key_xyz");
            Assert.AreEqual("[unknown_key_xyz]", missing);
        }

        [Test]
        public void D1_4_PlaceholderFormattingAndValidatorDetectsPlaceholderMismatch()
        {
            var loc = new LocalizationService("zh-TW");
            loc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "welcome", "歡迎指揮官 {0} 回到第 {1} 基地！" }
            });

            string formatted = loc.Get("welcome", "Alice", 7);
            Assert.AreEqual("歡迎指揮官 Alice 回到第 7 基地！", formatted);

            // Mismatched placeholders in en: only has {0}, lacks {1}
            loc.LoadLocale("en", new Dictionary<string, string>
            {
                { "welcome", "Welcome Commander {0}!" }
            });

            var result = LocalizationValidator.Validate(loc, baseLocale: "zh-TW");
            Assert.IsFalse(result.IsValid, "Should fail due to placeholder mismatch.");
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("ERR_PLACEHOLDER_MISMATCH")));
        }

        [Test]
        public void D1_5_ValidatorDetectsMissingKeysInLocalesAndMissingKeysInMasterData()
        {
            var loc = new LocalizationService("zh-TW");
            loc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "k1", "文字1" },
                { "k2", "文字2" }
            });
            loc.LoadLocale("en", new Dictionary<string, string>
            {
                { "k1", "Text1" }
                // k2 missing
            });

            var res = LocalizationValidator.Validate(loc, baseLocale: "zh-TW");
            Assert.IsTrue(res.Warnings.Exists(w => w.Contains("ERR_MISSING_TRANSLATION") && w.Contains("k2")));

            // Test with real master database
            var resWithMaster = LocalizationValidator.Validate(loc, master: _master, baseLocale: "zh-TW");
            // master has many keys not in this small test dictionary
            Assert.IsFalse(resWithMaster.IsValid);
            Assert.IsTrue(resWithMaster.Errors.Exists(e => e.Contains("ERR_MASTER_KEY_NOT_FOUND")));
        }

        [Test]
        public void D1_DiskLocaleFilesLoadSuccessfully()
        {
            string locDir = Path.Combine(TestPaths.DataDir(), "loc");
            if (Directory.Exists(locDir))
            {
                var loc = new LocalizationService("zh-TW");
                loc.LoadFromDirectory(locDir);

                Assert.IsTrue(loc.Has("char/char01_claire.name", "zh-TW"));
                Assert.AreEqual("克萊兒", loc.Get("loc:char/char01_claire.name"));

                loc.SetLocale("en");
                Assert.AreEqual("Claire", loc.Get("loc:char/char01_claire.name"));

                var valResult = LocalizationValidator.Validate(loc, master: _master, baseLocale: "zh-TW");
                Assert.IsTrue(valResult.IsValid, $"Disk loc files should validate cleanly. Errors: {string.Join("; ", valResult.Errors)}");
            }
        }

        [Test]
        public void M3_4_1_And_M3_4_2_FourLocalesAndUiLengthAndTagValidation()
        {
            string locDir = Path.Combine(TestPaths.DataDir(), "loc");
            var loc = new LocalizationService("zh-TW");
            loc.LoadFromDirectory(locDir);

            // 1. M3-4-1: 驗證四大語系 (zh-TW, en, ja, zh-CN) 完整存在且無缺漏
            CollectionAssert.AreEquivalent(new[] { "zh-TW", "en", "ja", "zh-CN" }, loc.AvailableLocales);

            var baseVal = LocalizationValidator.Validate(loc, master: _master, baseLocale: "zh-TW");
            Assert.IsTrue(baseVal.IsValid, $"四大語系靜態驗證應 100% 通過: {string.Join("; ", baseVal.Errors)}");
            Assert.AreEqual(0, baseVal.Warnings.Count, $"四大語系翻譯集合應完全對齊，無缺失 key: {string.Join("; ", baseVal.Warnings)}");

            // 2. M3-4-2: 驗證正式字典符合 UI 文字長度上限與富文字標籤閉合
            var lengthVal = LocalizationValidator.ValidateUiLengthAndTags(loc, maxUiButtonChars: 40, maxGeneralChars: 200);
            Assert.IsTrue(lengthVal.IsValid, $"真實字典 UI 長度與標籤應 100% 通過: {string.Join("; ", lengthVal.Errors)}");

            // 3. 陽性對照：超長 UI 按鈕字串檢測
            var mockLoc = new LocalizationService("zh-TW");
            mockLoc.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "ui.oversized_button", "這是一個超過四十個字元的超長使用者介面按鈕標題文字用來觸發爆框測試驗證，文字長度必須切實超越四十個字元限制以利驗證" }
            });
            var posOverLength = LocalizationValidator.ValidateUiLengthAndTags(mockLoc, maxUiButtonChars: 40, maxGeneralChars: 200);
            Assert.IsFalse(posOverLength.IsValid, "超長 UI 按鈕應觸發校驗失敗 (陽性對照)");
            Assert.IsTrue(posOverLength.Errors.Exists(e => e.Contains("ERR_UI_LENGTH_EXCEEDED")));

            // 4. 陽性對照：未閉合富文字標籤 <b> 檢測
            var mockUnclosed = new LocalizationService("zh-TW");
            mockUnclosed.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "ui.unclosed_tag", "<b>粗體未閉合文字" }
            });
            var posUnclosed = LocalizationValidator.ValidateUiLengthAndTags(mockUnclosed, maxUiButtonChars: 40, maxGeneralChars: 200);
            Assert.IsFalse(posUnclosed.IsValid, "未閉合富文字標籤應觸發校驗失敗 (陽性對照)");
            Assert.IsTrue(posUnclosed.Errors.Exists(e => e.Contains("ERR_UNCLOSED_TAG")));

            // 5. 陽性對照：交錯閉合富文字標籤 <b><i>...</b></i> 檢測
            var mockMismatched = new LocalizationService("zh-TW");
            mockMismatched.LoadLocale("zh-TW", new Dictionary<string, string>
            {
                { "general.mismatched", "<b><i>交錯閉合標籤</b></i>" }
            });
            var posMismatched = LocalizationValidator.ValidateUiLengthAndTags(mockMismatched, maxUiButtonChars: 40, maxGeneralChars: 200);
            Assert.IsFalse(posMismatched.IsValid, "交錯標籤應觸發校驗失敗 (陽性對照)");
            Assert.IsTrue(posMismatched.Errors.Exists(e => e.Contains("ERR_UNCLOSED_TAG")));
        }
    }
}
