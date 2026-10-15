using System.Collections.Generic;
using Game.Core.Platform;
using Game.Domain;
using Game.Domain.Events;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class PlatformServiceTests
    {
        private sealed class MockPlatformService : IPlatformService
        {
            public bool SyncEnabled { get; set; } = true;
            public List<string> UnlockedAchievements { get; } = new List<string>();

            public bool IsDlcInstalled(string dlcId) => dlcId == "r18_dlc";

            public bool UnlockAchievement(string steamApiName)
            {
                UnlockedAchievements.Add(steamApiName);
                return true;
            }

            public string GetLanguage() => "english";
        }

        private MasterDatabase CreateTestMaster()
        {
            var master = new MasterDatabase();
            // Normal achievement with steam API name
            master.Achievements["ach_normal"] = new AchievementDef
            {
                Id = "ach_normal",
                Rating = Rating.G,
                SteamApiName = "ACH_NORMAL_WIN"
            };
            // R18 achievement with steam API name (should NEVER be synced to platform)
            master.Achievements["ach_r18"] = new AchievementDef
            {
                Id = "ach_r18",
                Rating = Rating.R18,
                SteamApiName = "ACH_R18_SCENE"
            };
            // Achievement without steam API name
            master.Achievements["ach_no_steam"] = new AchievementDef
            {
                Id = "ach_no_steam",
                Rating = Rating.G,
                SteamApiName = null
            };
            return master;
        }

        [Test]
        public void D2_1_NullPlatformServiceWorksSafely()
        {
            var nullPlatform = new NullPlatformService();
            Assert.IsFalse(nullPlatform.SyncEnabled);
            Assert.IsFalse(nullPlatform.IsDlcInstalled("any_dlc"));
            Assert.IsTrue(nullPlatform.UnlockAchievement("ACH_ANY"));
            Assert.AreEqual("tchinese", nullPlatform.GetLanguage());
        }

        [Test]
        public void D2_2_OnlyAchievementsWithSteamApiNameAndNotR18AreSyncedToPlatform()
        {
            var platform = new MockPlatformService { SyncEnabled = true };
            var master = CreateTestMaster();
            var bus = new EventBus();

            using var bridge = new PlatformAchievementBridge(platform, master, bus);

            // 1. Normal achievement -> should sync
            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_normal"));
            Assert.AreEqual(1, platform.UnlockedAchievements.Count);
            Assert.AreEqual("ACH_NORMAL_WIN", platform.UnlockedAchievements[0]);

            // 2. R18 achievement -> D2-2 specifies R18 achievements are NEVER sent
            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_r18"));
            Assert.AreEqual(1, platform.UnlockedAchievements.Count, "R18 achievement must not be synced to platform!");

            // 3. Achievement without SteamApiName -> not synced
            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_no_steam"));
            Assert.AreEqual(1, platform.UnlockedAchievements.Count, "Achievement without SteamApiName must not be synced!");
        }

        [Test]
        public void D2_3_WhenSyncDisabled_PlatformApiIsNeverCalled()
        {
            var platform = new MockPlatformService { SyncEnabled = false };
            var master = CreateTestMaster();
            var bus = new EventBus();

            using var bridge = new PlatformAchievementBridge(platform, master, bus);

            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_normal"));
            Assert.AreEqual(0, platform.UnlockedAchievements.Count, "When SyncEnabled is false, platform API must never be called.");
        }
    }
}
