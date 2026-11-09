using System;
using System.Collections.Generic;
using Game.Core.Platform;
using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace Game.Unity.Platform
{
    /// <summary>
    /// Steamworks.NET 平台實作（符合 design/02 ADR-005 與 F8 查核規範）。
    /// 當 Steam 運行時提供成就同步、DLC 檢核與語言取得；
    /// 若 Steam 未啟動或 SteamAPI.Init() 失敗時，安全降級，絕不拋出未捕捉例外。
    /// </summary>
    public sealed class SteamPlatformService : IPlatformService, IDisposable
    {
        public const uint DefaultTestAppId = 480; // Valve Spacewar Public Test AppID

        private readonly uint _appId;
        private readonly Dictionary<string, uint> _dlcMapping = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private bool _isInitialized;

        public bool SyncEnabled { get; set; } = true;
        public bool IsInitialized => _isInitialized;
        public uint AppId => _appId;

        public SteamPlatformService(uint appId = DefaultTestAppId)
        {
            _appId = appId;
        }

        /// <summary>
        /// 註冊自訂 DLC 鍵值映射至 Steam AppId（例如 "r18_dlc" => 1234560）。
        /// 若未特別映射，將嘗試解析 dlcId 為純數字 AppId。
        /// </summary>
        public void RegisterDlcMapping(string dlcKey, uint steamDlcAppId)
        {
            if (!string.IsNullOrWhiteSpace(dlcKey))
            {
                _dlcMapping[dlcKey] = steamDlcAppId;
            }
        }

        /// <summary>
        /// 嘗試初始化 Steam API。
        /// </summary>
        /// <returns>初始化成功回傳 true；若 Steam 未開啟或環境不支援回傳 false，安全降級。</returns>
        public bool Initialize()
        {
#if !DISABLESTEAMWORKS
            if (_isInitialized) return true;

            try
            {
                if (!Packsize.Test())
                {
                    Debug.LogWarning("[SteamPlatformService] Steamworks.NET Packsize 檢測失敗。");
                    _isInitialized = false;
                    return false;
                }

                if (!DllCheck.Test())
                {
                    Debug.LogWarning("[SteamPlatformService] Steamworks.NET 原生 DLL 檢測失敗。");
                    _isInitialized = false;
                    return false;
                }

                _isInitialized = SteamAPI.Init();
                if (!_isInitialized)
                {
                    Debug.LogWarning("[SteamPlatformService] SteamAPI.Init() 回傳 false（Steam 客戶端可能未開啟），進入降級模式。");
                    return false;
                }

                Debug.Log($"[SteamPlatformService] Steam API 初始化成功！AppID={SteamUtils.GetAppID()} User={SteamFriends.GetPersonaName()}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SteamPlatformService] 初始化過程捕捉例外，安全降級：{ex.Message}");
                _isInitialized = false;
                return false;
            }
#else
            Debug.Log("[SteamPlatformService] DISABLESTEAMWORKS 已定義，平台處於無 Steam 模式。");
            _isInitialized = false;
            return false;
#endif
        }

        public bool IsDlcInstalled(string dlcId)
        {
            if (!_isInitialized || string.IsNullOrWhiteSpace(dlcId))
            {
                return false;
            }

#if !DISABLESTEAMWORKS
            try
            {
                uint targetAppId;
                if (_dlcMapping.TryGetValue(dlcId, out uint mappedId))
                {
                    targetAppId = mappedId;
                }
                else if (uint.TryParse(dlcId, out uint parsedId))
                {
                    targetAppId = parsedId;
                }
                else
                {
                    return false;
                }

                return SteamApps.BIsDlcInstalled(new AppId_t(targetAppId));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SteamPlatformService] IsDlcInstalled({dlcId}) 查詢異常：{ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        public bool UnlockAchievement(string steamApiName)
        {
            if (!_isInitialized || !SyncEnabled || string.IsNullOrWhiteSpace(steamApiName))
            {
                return false;
            }

#if !DISABLESTEAMWORKS
            try
            {
                bool success = SteamUserStats.SetAchievement(steamApiName);
                if (success)
                {
                    SteamUserStats.StoreStats();
                }
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SteamPlatformService] UnlockAchievement({steamApiName}) 異常：{ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        public string GetLanguage()
        {
            if (!_isInitialized)
            {
                return "tchinese";
            }

#if !DISABLESTEAMWORKS
            try
            {
                string lang = SteamApps.GetCurrentGameLanguage();
                return string.IsNullOrWhiteSpace(lang) ? "tchinese" : lang;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SteamPlatformService] GetLanguage() 異常：{ex.Message}");
                return "tchinese";
            }
#else
            return "tchinese";
#endif
        }

        public void RunCallbacks()
        {
#if !DISABLESTEAMWORKS
            if (_isInitialized)
            {
                SteamAPI.RunCallbacks();
            }
#endif
        }

        public void Dispose()
        {
#if !DISABLESTEAMWORKS
            if (_isInitialized)
            {
                try
                {
                    SteamAPI.Shutdown();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SteamPlatformService] Shutdown 異常：{ex.Message}");
                }
                finally
                {
                    _isInitialized = false;
                }
            }
#endif
        }
    }
}
