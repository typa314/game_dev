#if UNITY_2021_1_OR_NEWER
using System;
using System.IO;
using Game.Core;
using Game.Core.Platform;
using Game.Domain;
using Game.Domain.Events;
using Game.Domain.MasterData;
using Game.Unity;
using Game.Unity.Platform;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    [TestFixture]
    public sealed class SteamPlatformIntegrationTests
    {
        private GameObject _holder;
        private GameBootstrap _bootstrap;

        [TearDown]
        public void TearDown()
        {
            GameServices.Clear();
            if (_holder != null)
            {
                UnityEngine.Object.DestroyImmediate(_holder);
            }
        }

        [Test]
        public void F8_1_Steamworks_Package_And_AppId_Configuration_Valid()
        {
            // 驗證 steam_appid.txt 配置
            string appidPath = Path.Combine(Application.dataPath, "..", "steam_appid.txt");
            Assert.IsTrue(File.Exists(appidPath), $"steam_appid.txt 必須存在於專案根目錄: {appidPath}");

            string content = File.ReadAllText(appidPath).Trim();
            Assert.IsTrue(uint.TryParse(content, out uint appId), $"steam_appid.txt 內容必須為合法的無號整數 AppID，當前為: '{content}'");
            Assert.AreEqual(480u, appId, "M0 階段測試用 AppID 必須設定為 Valve Spacewar 480");

            // 驗證 SteamPlatformService 預設 AppId
            using var service = new SteamPlatformService();
            Assert.AreEqual(480u, service.AppId);
            Assert.IsTrue(service.SyncEnabled);
        }

        [Test]
        public void F8_2_SteamPlatformService_WhenSteamNotRunning_DegradesGracefullyWithoutExceptions()
        {
            using var service = new SteamPlatformService(480);

            // 在未啟動 Steam 客戶端或無圖形 CI 環境下調用 Initialize
            bool initResult = service.Initialize();
            // 不管是否連上 Steam，皆不得拋出例外
            if (!initResult)
            {
                Assert.IsFalse(service.IsInitialized);
                // 降級模式下呼叫所有介面方法，必須安全返回預設值
                Assert.IsFalse(service.IsDlcInstalled("480"));
                Assert.IsFalse(service.IsDlcInstalled("r18_dlc"));
                Assert.IsFalse(service.UnlockAchievement("ACH_WIN"));
                Assert.AreEqual("tchinese", service.GetLanguage());

                // 回呼與銷毀亦不得拋出例外
                Assert.DoesNotThrow(() => service.RunCallbacks());
                Assert.DoesNotThrow(() => service.Dispose());
            }
            else
            {
                Assert.IsTrue(service.IsInitialized);
                Assert.IsNotNull(service.GetLanguage());
            }
        }

        [Test]
        public void F8_3_DlcId_Mapping_And_Query_Safety()
        {
            using var service = new SteamPlatformService(480);
            service.RegisterDlcMapping("adult_content_dlc", 1234567);

            // 測試無效值、空字串、非數字鍵值之查詢安全性
            Assert.DoesNotThrow(() =>
            {
                Assert.IsFalse(service.IsDlcInstalled(null));
                Assert.IsFalse(service.IsDlcInstalled(""));
                Assert.IsFalse(service.IsDlcInstalled("   "));
                Assert.IsFalse(service.IsDlcInstalled("non_numeric_unmapped_key"));
                Assert.IsFalse(service.IsDlcInstalled("adult_content_dlc"));
                Assert.IsFalse(service.IsDlcInstalled("999999"));
            });
        }

        [Test]
        public void F8_4_GameBootstrap_InjectsPlatformService_AndWiresAchievementBridge()
        {
            _holder = new GameObject("TestSteamBootstrapHolder");
            _bootstrap = _holder.AddComponent<GameBootstrap>();
            _bootstrap.Boot();

            // 驗證 GameServices 平台服務已注入
            Assert.IsNotNull(GameServices.Platform, "GameServices.Platform 必須已初始化注入");
            Assert.IsNotNull(GameServices.PlatformBridge, "GameServices.PlatformBridge 必須已注入");

            // 觸發成就解鎖事件，確認平台橋接運作順暢無例外
            Assert.DoesNotThrow(() =>
            {
                GameServices.Core.Bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_first_win"));
            });
        }

        [Test]
        public void F8_5_PositiveControl_AchievementSyncR18Filter_And_SyncDisabled()
        {
            var mockPlatform = new TestMockPlatform();
            var master = new MasterDatabase();
            master.Achievements["ach_g"] = new AchievementDef { Id = "ach_g", Rating = Rating.G, SteamApiName = "ACH_G" };
            master.Achievements["ach_r18"] = new AchievementDef { Id = "ach_r18", Rating = Rating.R18, SteamApiName = "ACH_R18" };

            var bus = new EventBus();
            using var bridge = new PlatformAchievementBridge(mockPlatform, master, bus);

            // 1. G 級成就有 steamApiName -> 應同步
            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_g"));
            Assert.AreEqual(1, mockPlatform.Calls.Count);
            Assert.AreEqual("ACH_G", mockPlatform.Calls[0]);

            // 2. 陽性對照：R18 成就絕對不可同步至 Steam 平台 (ADR-003, ADR-005)
            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_r18"));
            Assert.AreEqual(1, mockPlatform.Calls.Count, "R18 成就必須被攔截，不可送至 Steam！");

            // 3. 陽性對照：當 SyncEnabled 為 false 時，完全不呼叫平台 API
            mockPlatform.SyncEnabled = false;
            bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", "ach_g"));
            Assert.AreEqual(1, mockPlatform.Calls.Count, "SyncEnabled 為 false 時不得呼叫平台 API");
        }

        private sealed class TestMockPlatform : IPlatformService
        {
            public bool SyncEnabled { get; set; } = true;
            public System.Collections.Generic.List<string> Calls { get; } = new System.Collections.Generic.List<string>();

            public bool IsDlcInstalled(string dlcId) => false;
            public bool UnlockAchievement(string steamApiName)
            {
                Calls.Add(steamApiName);
                return true;
            }
            public string GetLanguage() => "tchinese";
        }
    }
}
#endif
