using System;
using Game.Domain;
using Game.Domain.Events;
using Game.Domain.MasterData;

namespace Game.Core.Platform
{
    /// <summary>
    /// 將遊戲內成就事件橋接至平台的轉發器（遵守 D2 平台同步規範）。
    /// </summary>
    public sealed class PlatformAchievementBridge : IDisposable
    {
        private readonly IPlatformService _platform;
        private readonly MasterDatabase _master;
        private readonly EventBus _bus;
        private readonly Action<DomainEvent> _handler;

        public PlatformAchievementBridge(IPlatformService platform, MasterDatabase master, EventBus bus)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _master = master;
            _bus = bus;

            if (_bus != null)
            {
                _handler = OnDomainEvent;
                _bus.Subscribe(_handler);
            }
        }

        public bool TrySyncAchievement(string achievementId)
        {
            // D2-3: 同步設定為關閉時，完全不呼叫平台 API
            if (_platform == null || !_platform.SyncEnabled)
            {
                return false;
            }

            if (_master == null || !_master.Achievements.TryGetValue(achievementId, out var def))
            {
                return false;
            }

            // D2-2: R18 成就永遠不送至平台
            if (def.Rating > Rating.R15)
            {
                return false;
            }

            // D2-2: 僅把設定了 steamApiName 的成就送到平台
            if (string.IsNullOrWhiteSpace(def.SteamApiName))
            {
                return false;
            }

            return _platform.UnlockAchievement(def.SteamApiName);
        }

        private void OnDomainEvent(DomainEvent ev)
        {
            if (ev.Name == Ev.AchievementUnlocked)
            {
                string achId = ev.Get("achievement");
                if (!string.IsNullOrEmpty(achId))
                {
                    TrySyncAchievement(achId);
                }
            }
        }

        public void Dispose()
        {
            if (_bus != null && _handler != null)
            {
                _bus.Unsubscribe(_handler);
            }
        }
    }
}
