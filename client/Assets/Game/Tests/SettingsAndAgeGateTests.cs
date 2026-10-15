using System;
using System.IO;
using Game.Core.AgeGate;
using Game.Core.Settings;
using Game.Domain;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class SettingsAndAgeGateTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "tower_game_settings_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void D4_1_SettingsSaveAndLoadRoundTrip()
        {
            string filePath = Path.Combine(_tempDir, "settings.json");
            var service = new SettingsService(filePath);

            var settings = new GameSettings
            {
                BgmVolume = 0.75f,
                SeVolume = 0.85f,
                VoiceVolume = 0.90f,
                TextSpeed = 1.5f,
                AutoSpeed = 2.0f,
                SkipUnread = true,
                UseLive2d = false,
                Language = "en",
                RatingChoice = Rating.R18
            };

            service.Save(settings);

            var loadedService = new SettingsService(filePath);
            var loaded = loadedService.Load();

            Assert.AreEqual(0.75f, loaded.BgmVolume);
            Assert.AreEqual(0.85f, loaded.SeVolume);
            Assert.AreEqual(0.90f, loaded.VoiceVolume);
            Assert.AreEqual(1.5f, loaded.TextSpeed);
            Assert.AreEqual(2.0f, loaded.AutoSpeed);
            Assert.IsTrue(loaded.SkipUnread);
            Assert.IsFalse(loaded.UseLive2d);
            Assert.AreEqual("en", loaded.Language);
            Assert.AreEqual(Rating.R18, loaded.RatingChoice);
        }

        [Test]
        public void D4_2_GetClampedRatingEnforcesBuildMaxRating()
        {
            var settings = new GameSettings { RatingChoice = Rating.R18 };

            // In G build: clamped to G
            Assert.AreEqual(Rating.G, SettingsService.GetClampedRating(settings, Rating.G));

            // In R15 build: clamped to R15
            Assert.AreEqual(Rating.R15, SettingsService.GetClampedRating(settings, Rating.R15));

            // In R18 build: retains R18
            Assert.AreEqual(Rating.R18, SettingsService.GetClampedRating(settings, Rating.R18));
        }

        [Test]
        public void D5_1_UnverifiedAgeGateAlwaysReturnsRatingG_EvenIfDlcInstalledAndBuildMaxIsR18()
        {
            var state = new AgeGateState
            {
                IsVerified = false,
                RatingOptIn = Rating.R18
            };

            Rating effective = AgeGateService.GetEffectiveRating(state, buildMax: Rating.R18, isDlcInstalled: true);
            Assert.AreEqual(Rating.G, effective, "Unverified age gate must strictly return Rating.G!");
        }

        [Test]
        public void D5_2_VerifiedAgeGateRecordsTimestampAndSetsRatingOptInWithinCeiling()
        {
            var state = new AgeGateState { IsVerified = false };
            var fakeClock = new FakeClock(new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc));

            AgeGateService.Verify(state, Rating.R18, fakeClock);

            Assert.IsTrue(state.IsVerified);
            Assert.AreEqual(fakeClock.UtcNow, state.VerifiedAtUtc);
            Assert.AreEqual(Rating.R18, state.RatingOptIn);

            // With DLC installed and buildMax=R18 -> effective is R18
            Rating effWithDlc = AgeGateService.GetEffectiveRating(state, buildMax: Rating.R18, isDlcInstalled: true);
            Assert.AreEqual(Rating.R18, effWithDlc);

            // Without DLC installed -> effective capped at R15
            Rating effWithoutDlc = AgeGateService.GetEffectiveRating(state, buildMax: Rating.R18, isDlcInstalled: false);
            Assert.AreEqual(Rating.R15, effWithoutDlc);

            // In G build -> effective capped at G
            Rating effGBuild = AgeGateService.GetEffectiveRating(state, buildMax: Rating.G, isDlcInstalled: true);
            Assert.AreEqual(Rating.G, effGBuild);
        }
    }
}
