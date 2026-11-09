using System;
using UnityEngine;

namespace Game.Unity.Platform
{
    /// <summary>
    /// SteamManager 生命週期組件（MonoBehaviour）。
    /// 自動管理 SteamPlatformService 的回呼迴圈（SteamAPI.RunCallbacks）與平穩銷毀。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1500)]
    public sealed class SteamManager : MonoBehaviour
    {
        [SerializeField] private uint appId = SteamPlatformService.DefaultTestAppId;
        [SerializeField] private bool autoInitializeOnAwake = true;

        private SteamPlatformService _service;

        public SteamPlatformService Service => _service;
        public bool IsInitialized => _service != null && _service.IsInitialized;

        private void Awake()
        {
            if (_service == null)
            {
                _service = new SteamPlatformService(appId);
            }

            if (autoInitializeOnAwake && !_service.IsInitialized)
            {
                _service.Initialize();
            }
        }

        private void Update()
        {
            if (_service != null && _service.IsInitialized)
            {
                _service.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }
        }
    }
}
