using System;
using System.Collections.Generic;
using Game.Domain.Player;

namespace Game.Core.UI
{
    // =========================================================================================
    // 1. 對話視窗服務 (DialogManager) - 絕不調用 OS 原生對話框
    // =========================================================================================
    public enum DialogType
    {
        Alert,
        Confirm
    }

    public sealed class DialogRequest
    {
        public string Title { get; }
        public string Message { get; }
        public DialogType Type { get; }
        public Action OnConfirm { get; }
        public Action OnCancel { get; }

        public DialogRequest(string title, string message, DialogType type, Action onConfirm = null, Action onCancel = null)
        {
            Title = title;
            Message = message;
            Type = type;
            OnConfirm = onConfirm;
            OnCancel = onCancel;
        }
    }

    public sealed class DialogManager
    {
        private readonly Queue<DialogRequest> _queue = new Queue<DialogRequest>();
        public DialogRequest CurrentDialog { get; private set; }

        public event Action<DialogRequest> DialogOpened;
        public event Action DialogClosed;

        public bool HasActiveDialog => CurrentDialog != null;

        public void ShowAlert(string title, string message, Action onConfirm = null)
        {
            Enqueue(new DialogRequest(title, message, DialogType.Alert, onConfirm));
        }

        public void ShowConfirm(string title, string message, Action onConfirm, Action onCancel = null)
        {
            Enqueue(new DialogRequest(title, message, DialogType.Confirm, onConfirm, onCancel));
        }

        private void Enqueue(DialogRequest request)
        {
            if (CurrentDialog == null)
            {
                CurrentDialog = request;
                DialogOpened?.Invoke(CurrentDialog);
            }
            else
            {
                _queue.Enqueue(request);
            }
        }

        public void Confirm()
        {
            if (CurrentDialog == null) return;
            var req = CurrentDialog;
            CurrentDialog = null;
            req.OnConfirm?.Invoke();
            ProcessNext();
        }

        public void Cancel()
        {
            if (CurrentDialog == null) return;
            var req = CurrentDialog;
            CurrentDialog = null;
            req.OnCancel?.Invoke();
            ProcessNext();
        }

        private void ProcessNext()
        {
            if (_queue.Count > 0)
            {
                CurrentDialog = _queue.Dequeue();
                DialogOpened?.Invoke(CurrentDialog);
            }
            else
            {
                DialogClosed?.Invoke();
            }
        }
    }

    // =========================================================================================
    // 2. Toast 提示通知服務 (ToastService) - 劇情中自動暫存
    // =========================================================================================
    public sealed class ToastNotification
    {
        public string Message { get; }
        public string IconKey { get; }

        public ToastNotification(string message, string iconKey = null)
        {
            Message = message;
            IconKey = iconKey;
        }
    }

    public sealed class ToastService
    {
        private readonly Queue<ToastNotification> _pendingQueue = new Queue<ToastNotification>();
        private bool _isInStoryPlayback;

        public event Action<ToastNotification> ToastDisplayed;

        public bool IsInStoryPlayback
        {
            get => _isInStoryPlayback;
            set
            {
                _isInStoryPlayback = value;
                if (!_isInStoryPlayback)
                {
                    FlushPendingToasts();
                }
            }
        }

        public int PendingCount => _pendingQueue.Count;

        public void ShowToast(string message, string iconKey = null)
        {
            var notification = new ToastNotification(message, iconKey);
            if (_isInStoryPlayback)
            {
                // 劇情播放中，先暫存於佇列，不干擾玩家觀賞劇情
                _pendingQueue.Enqueue(notification);
            }
            else
            {
                ToastDisplayed?.Invoke(notification);
            }
        }

        public void FlushPendingToasts()
        {
            while (_pendingQueue.Count > 0)
            {
                var toast = _pendingQueue.Dequeue();
                ToastDisplayed?.Invoke(toast);
            }
        }
    }

    // =========================================================================================
    // 3. 紅點通知計算服務 (RedDotService)
    // =========================================================================================
    public sealed class RedDotService
    {
        private readonly GameCore _core;

        public RedDotService(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        /// <summary>是否有可領取之成就獎勵。</summary>
        public bool HasClaimableAchievements()
        {
            if (_core.Player == null) return false;
            foreach (var kvp in _core.Player.Achievements)
            {
                var prog = kvp.Value;
                if (!string.IsNullOrEmpty(prog.UnlockedAtUtc) && !prog.Claimed)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // =========================================================================================
    // 4. 頂部貨幣欄 Presenter (CurrencyBarPresenter)
    // =========================================================================================
    public interface ICurrencyBarView : IView
    {
        void UpdateCurrencies(long gold, long freeDia, long paidDia, int stamina);
    }

    public sealed class CurrencyBarPresenter : PresenterBase<ICurrencyBarView>
    {
        private readonly GameCore _core;

        public CurrencyBarPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public override void Initialize()
        {
            base.Initialize();
            Refresh();
        }

        public void Refresh()
        {
            if (View == null || _core.Player == null) return;
            long gold = _core.Player.ItemCount("item_gold");
            long freeDia = _core.Player.ItemCount("item_dia_free");
            long paidDia = _core.Player.ItemCount("item_dia_paid");
            int stamina = _core.Player.Encounter?.Stamina ?? 0;

            View.UpdateCurrencies(gold, freeDia, paidDia, stamina);
        }
    }
}
