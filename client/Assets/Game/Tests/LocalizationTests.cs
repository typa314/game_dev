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
    }
}
