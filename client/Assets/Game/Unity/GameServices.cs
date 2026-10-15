using Game.Core;
using Game.Core.Audio;
using Game.Core.Persistence;
using Game.Core.UI;
using Game.Domain;

namespace Game.Unity
{
    /// <summary>
    /// 靜態服務定位器（暫時方案）。由 GameBootstrap 在啟動完成後填入。
    /// TODO: 之後換成 VContainer 的 LifetimeScope 注入，呼叫端改成建構子/[Inject] 取得。
    /// </summary>
    public static class GameServices
    {
        public static GameCore Core { get; private set; }
        public static FileSaveStore SaveStore { get; private set; }
        public static AssetService Assets { get; private set; }
        public static RatingPolicy Rating { get; private set; }

        // UI 與音訊服務
        public static ScreenNavigator Navigator { get; private set; }
        public static DialogManager Dialogs { get; private set; }
        public static ToastService Toasts { get; private set; }
        public static AudioService Audio { get; private set; }

        /// <summary>啟動流程（主資料載入、存檔讀取）是否已成功完成。</summary>
        public static bool IsReady { get { return Core != null; } }

        public static void Install(
            GameCore core,
            FileSaveStore saveStore,
            AssetService assets,
            RatingPolicy rating,
            ScreenNavigator navigator = null,
            DialogManager dialogs = null,
            ToastService toasts = null,
            AudioService audio = null)
        {
            Core = core;
            SaveStore = saveStore;
            Assets = assets;
            Rating = rating;
            Navigator = navigator ?? new ScreenNavigator();
            Dialogs = dialogs ?? new DialogManager();
            Toasts = toasts ?? new ToastService();
            Audio = audio;
        }

        public static void Clear()
        {
            Core = null;
            SaveStore = null;
            Assets = null;
            Rating = null;
            Navigator = null;
            Dialogs = null;
            Toasts = null;
            Audio = null;
        }
    }
}
