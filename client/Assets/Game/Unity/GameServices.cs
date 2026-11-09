using System;
using Game.Core;
using Game.Core.AgeGate;
using Game.Core.Audio;
using Game.Core.Localization;
using Game.Core.Persistence;
using Game.Core.Platform;
using Game.Core.Settings;
using Game.Core.UI;
using Game.Domain;

namespace Game.Unity
{
    /// <summary>
    /// 靜態服務定位器。由 GameBootstrap 在啟動完成後填入。
    /// </summary>
    public static class GameServices
    {
        public static GameCore Core { get; private set; }
        public static FileSaveStore SaveStore { get; private set; }
        public static AssetService Assets { get; private set; }
        public static RatingPolicy Rating { get; private set; }
        public static IPlatformService Platform { get; private set; }
        public static PlatformAchievementBridge PlatformBridge { get; private set; }

        // UI 與音訊服務
        public static ScreenNavigator Navigator { get; private set; }
        public static DialogManager Dialogs { get; private set; }
        public static ToastService Toasts { get; private set; }
        public static AudioService Audio { get; private set; }

        // 橫切服務與狀態 (OPT-1 擴充)
        public static ILocalizationService Localization { get; private set; }
        public static SettingsService Settings { get; private set; }
        public static AgeGateState AgeGate { get; private set; }
        public static RedDotService RedDots { get; private set; }
        public static StorySaveService StorySaves { get; private set; }

        /// <summary>啟動流程（主資料載入、存檔讀取）是否已成功完成。</summary>
        public static bool IsReady { get { return Core != null; } }

        public static void Install(
            GameCore core,
            FileSaveStore saveStore,
            AssetService assets,
            RatingPolicy rating,
            IPlatformService platform = null,
            PlatformAchievementBridge platformBridge = null,
            ScreenNavigator navigator = null,
            DialogManager dialogs = null,
            ToastService toasts = null,
            AudioService audio = null,
            ILocalizationService localization = null,
            SettingsService settings = null,
            AgeGateState ageGate = null,
            RedDotService redDots = null,
            StorySaveService storySaves = null)
        {
            Core = core;
            SaveStore = saveStore;
            Assets = assets;
            Rating = rating;
            Platform = platform ?? new NullPlatformService();
            PlatformBridge = platformBridge;
            Navigator = navigator ?? new ScreenNavigator();
            Dialogs = dialogs ?? new DialogManager();
            Toasts = toasts ?? new ToastService();
            Audio = audio;
            Localization = localization;
            Settings = settings;
            AgeGate = ageGate;
            RedDots = redDots;
            StorySaves = storySaves;
        }

        public static void Clear()
        {
            if (PlatformBridge != null)
            {
                PlatformBridge.Dispose();
                PlatformBridge = null;
            }
            if (Platform is IDisposable disposablePlatform)
            {
                disposablePlatform.Dispose();
            }
            Platform = null;
            Core = null;
            SaveStore = null;
            Assets = null;
            Rating = null;
            Navigator = null;
            Dialogs = null;
            Toasts = null;
            Audio = null;
            Localization = null;
            Settings = null;
            AgeGate = null;
            RedDots = null;
            StorySaves = null;
        }
    }
}
