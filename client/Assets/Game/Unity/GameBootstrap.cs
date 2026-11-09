using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Diagnostics;
using Game.Core.Persistence;
using Game.Core.Platform;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Unity.Platform;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// Boot 場景的進入點（design/03 §3）：載入主資料 → 驗證 → 讀取存檔 → 建立 GameCore → 初始化平台服務 → 註冊到 GameServices。
    /// 之後定期把 GameCore.Dirty 寫回存檔，並調度 Steam 回呼。
    /// TODO: 啟動完成後切換到 Main 場景；錯誤改成顯示錯誤畫面（目前只寫 log 並停止）。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const string ProfileFile = "profile.json";
        private const float AutoSaveIntervalSec = 2f;

        [SerializeField] private BuildProfile buildProfile;

        private float _saveTimer;
        private LocalCrashLogger _crashLogger;

        public LocalCrashLogger CrashLogger => _crashLogger;
        public static LocalCrashLogger GlobalCrashLogger { get; private set; }

        private string GameVersion
        {
            get { return buildProfile != null ? buildProfile.GameVersion : "0.0.0-dev"; }
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            GameServices.Clear();

            // M3-7: 初始化本機結構化當機記錄器
            string logDir = Path.Combine(Application.persistentDataPath, "logs");
            _crashLogger = new LocalCrashLogger(logDir, maxLogFiles: 10);
            GlobalCrashLogger = _crashLogger;
            Application.logMessageReceivedThreaded += HandleUnityLog;

            if (buildProfile == null)
                Debug.LogWarning("[Boot] BuildProfile 未指定，使用預設值（MaxRating = G）");

            try
            {
                Boot();
            }
            catch (Exception e)
            {
                Debug.LogError("[Boot] 啟動失敗：" + e);
                _crashLogger.LogCrash(e, "GameBootstrap.Awake");
                GameServices.Clear();
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= HandleUnityLog;
            if (ReferenceEquals(GlobalCrashLogger, _crashLogger))
            {
                GlobalCrashLogger = null;
            }
        }

        private void HandleUnityLog(string logString, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert)
            {
                _crashLogger?.LogCrash(type.ToString(), logString, stackTrace);
            }
        }

        public void Boot()
        {
            // 1. 主資料
            // TODO: Android 的 streamingAssetsPath 在 apk 內，無法用 File IO 讀；目前目標平台是 PC，之後再處理。
            string masterDir = Path.Combine(Application.streamingAssetsPath, "MasterData");
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(masterDir);

            // 2. 主資料驗證：有錯誤就停止啟動
            List<string> errors = MasterDataValidator.Validate(master);
            if (errors.Count > 0)
            {
                Debug.LogError("[Boot] 主資料驗證失敗（" + errors.Count + " 筆）：\n" + string.Join("\n", errors));
                enabled = false;
                return;
            }

            // 3. 存檔
            var clock = new SystemClock();
            var store = new FileSaveStore(Path.Combine(Application.persistentDataPath, "saves"));
            var loadErrors = new List<string>();
            PlayerState player = store.ReadWithFallback<PlayerState>(ProfileFile, SaveCodec.Decode, loadErrors);
            if (player == null && loadErrors.Count > 0)
            {
                // 存檔存在但主檔與備份全部損壞：不建立新玩家，避免之後的自動存檔把備份輪替掉。
                // TODO: 顯示「存檔損壞」畫面，讓玩家選擇匯出 / 重新開始。
                Debug.LogError("[Boot] 存檔全部無法讀取，停止啟動：\n" + string.Join("\n", loadErrors));
                enabled = false;
                return;
            }
            if (loadErrors.Count > 0)
                Debug.LogWarning("[Boot] 主存檔讀取失敗，已改用備份：\n" + string.Join("\n", loadErrors));
            bool isNew = player == null;
            if (isNew) player = GameCore.NewPlayer(master, clock);

            // 4. GameCore
            Rating buildMax = buildProfile != null ? buildProfile.MaxRating : Rating.G;
            var rating = new RatingPolicy(buildMax, player.Profile.RatingOptIn);
            var rng = new SplitMix64Random((ulong)DateTime.UtcNow.Ticks);
            var core = new GameCore(master, player, rng, clock, rating);
            core.OnLoaded();
            if (isNew) core.Dirty = true;

            // 5. 平台服務與成就橋接 (D2, F8)
            IPlatformService platform = null;
            var steamPlatform = new SteamPlatformService();
            if (steamPlatform.Initialize())
            {
                platform = steamPlatform;
            }
            else
            {
                steamPlatform.Dispose();
                platform = new NullPlatformService();
            }

            var bridge = new PlatformAchievementBridge(platform, master, core.Bus);

            // 6. 橫切服務與資產防線 (OPT-1)
            var redDots = new Game.Core.UI.RedDotService(core);
            string storySaveDir = Path.Combine(Application.persistentDataPath, "saves", "story");
            var storySaves = new Game.Core.Persistence.StorySaveService(storySaveDir, clock);

            Game.Core.Assets.AssetCatalog catalog = null;
            string manifestPath = Path.Combine(Application.streamingAssetsPath, "asset_manifest.csv");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(Application.dataPath, "..", "..", "data", "asset_manifest.csv");
            }
            if (File.Exists(manifestPath))
            {
                try
                {
                    catalog = new Game.Core.Assets.AssetCatalog();
                    catalog.LoadFromFile(manifestPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Boot] AssetCatalog 載入警告：" + ex.Message);
                }
            }

            var assetService = new AssetService(rating, catalog);

            string settingsPath = Path.Combine(Application.persistentDataPath, "settings.json");
            var settingsService = new Game.Core.Settings.SettingsService(settingsPath);
            settingsService.Load();

            var localization = new Game.Core.Localization.LocalizationService();
            string locDir = Path.Combine(Application.streamingAssetsPath, "Loc");
            if (Directory.Exists(locDir))
            {
                localization.LoadFromDirectory(locDir);
            }
            if (!string.IsNullOrEmpty(settingsService.Current.Language))
            {
                localization.SetLocale(settingsService.Current.Language);
            }

            var ageGateState = new Game.Core.AgeGate.AgeGateState
            {
                IsVerified = player.Profile.RatingOptIn != Rating.G,
                RatingOptIn = player.Profile.RatingOptIn
            };

            // M2-4: 掛載實體 UnityAudioDriver 並註冊至 AudioService
            var audioDriver = gameObject.GetComponent<Game.Unity.Audio.UnityAudioDriver>();
            if (audioDriver == null)
            {
                audioDriver = gameObject.AddComponent<Game.Unity.Audio.UnityAudioDriver>();
            }
            var audioService = new Game.Core.Audio.AudioService(audioDriver);
            if (player.Settings != null)
            {
                audioService.SetVolumes(1.0f, player.Settings.Bgm, player.Settings.Se, player.Settings.Voice);
            }

            GameServices.Install(
                core,
                store,
                assetService,
                rating,
                platform,
                bridge,
                navigator: null,
                dialogs: null,
                toasts: null,
                audio: audioService,
                localization: localization,
                settings: settingsService,
                ageGate: ageGateState,
                redDots: redDots,
                storySaves: storySaves);

            Debug.Log("[Boot] 完成：masterDataVersion=" + master.Config.MasterDataVersion
                      + " characters=" + player.Characters.Count + (isNew ? "（新玩家）" : "")
                      + " platform=" + platform.GetType().Name);
        }

        private void Update()
        {
            if (!GameServices.IsReady) return;

            if (GameServices.Platform is SteamPlatformService steamSvc)
            {
                steamSvc.RunCallbacks();
            }

            _saveTimer += Time.unscaledDeltaTime;
            if (_saveTimer < AutoSaveIntervalSec) return;
            _saveTimer = 0f;
            SaveIfDirty();
        }

        private void OnApplicationQuit()
        {
            SaveIfDirty();
        }

        // TODO: 行動平台在 OnApplicationPause(true) 時也要存檔。

        /// <summary>GameCore.Dirty 為 true 時寫入 profile.json 並清除 Dirty。</summary>
        public void SaveIfDirty()
        {
            GameCore core = GameServices.Core;
            if (core == null || !core.Dirty) return;
            try
            {
                string json = SaveCodec.Encode(core.Player, GameVersion, DateTime.UtcNow);
                GameServices.SaveStore.Write(ProfileFile, json);
                core.Dirty = false;
            }
            catch (Exception e)
            {
                // Dirty 保持 true，下一輪再試
                Debug.LogError("[Save] 存檔失敗：" + e);
            }
        }
    }
}
