using System;
using System.Collections.Generic;

namespace Game.Core.UI
{
    public enum NavigationActionType
    {
        Push,
        Pop,
        Replace,
        PopToRoot
    }

    public sealed class NavigationEventArgs : EventArgs
    {
        public NavigationActionType Action { get; }
        public IScreen PreviousScreen { get; }
        public IScreen CurrentScreen { get; }

        public NavigationEventArgs(NavigationActionType action, IScreen prev, IScreen curr)
        {
            Action = action;
            PreviousScreen = prev;
            CurrentScreen = curr;
        }
    }

    /// <summary>
    /// 畫面堆疊導航管理器（ScreenNavigator）。
    /// 純 C# 實作，負責堆疊結構、生命週期分發與轉場鎖定。
    /// </summary>
    public sealed class ScreenNavigator
    {
        private readonly Stack<IScreen> _stack = new Stack<IScreen>();
        private bool _isTransitioning;

        public event EventHandler<NavigationEventArgs> Navigated;

        public IScreen CurrentScreen => _stack.Count > 0 ? _stack.Peek() : null;
        public int Count => _stack.Count;
        public bool IsTransitioning => _isTransitioning;

        public void BeginTransition()
        {
            _isTransitioning = true;
        }

        public void EndTransition()
        {
            _isTransitioning = false;
        }

        /// <summary>
        /// 推入新畫面。若轉場中則拒絕操作並回傳 false。
        /// </summary>
        public bool Push(IScreen screen, object args = null)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (_isTransitioning) return false;

            IScreen prev = CurrentScreen;
            if (prev != null)
            {
                prev.OnPause();
            }

            _stack.Push(screen);
            screen.OnPush(args);

            Navigated?.Invoke(this, new NavigationEventArgs(NavigationActionType.Push, prev, screen));
            return true;
        }

        /// <summary>
        /// 彈出頂端畫面。若堆疊為空或轉場中則回傳 false。
        /// </summary>
        public bool Pop()
        {
            if (_isTransitioning || _stack.Count == 0) return false;

            IScreen popped = _stack.Pop();
            popped.OnPop();

            IScreen next = CurrentScreen;
            if (next != null)
            {
                next.OnResume();
            }

            Navigated?.Invoke(this, new NavigationEventArgs(NavigationActionType.Pop, popped, next));
            return true;
        }

        /// <summary>
        /// 替換頂端畫面（等同於 Pop 後立刻 Push）。
        /// </summary>
        public bool Replace(IScreen screen, object args = null)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (_isTransitioning) return false;

            IScreen prev = null;
            if (_stack.Count > 0)
            {
                prev = _stack.Pop();
                prev.OnPop();
            }

            _stack.Push(screen);
            screen.OnPush(args);

            Navigated?.Invoke(this, new NavigationEventArgs(NavigationActionType.Replace, prev, screen));
            return true;
        }

        /// <summary>
        /// 彈出直到堆疊只剩底層畫面（Root）。
        /// </summary>
        public bool PopToRoot()
        {
            if (_isTransitioning || _stack.Count <= 1) return false;

            IScreen initialTop = _stack.Peek();

            while (_stack.Count > 1)
            {
                IScreen popped = _stack.Pop();
                popped.OnPop();
            }

            IScreen root = _stack.Peek();
            root.OnResume();

            Navigated?.Invoke(this, new NavigationEventArgs(NavigationActionType.PopToRoot, initialTop, root));
            return true;
        }

        /// <summary>
        /// 處理 Esc 或 Android 返回鍵。
        /// 轉場期間一律阻擋輸入（回傳 false）。
        /// 若當前畫面有內部未完成操作（OnBackRequested 回傳 true），則不 Pop。
        /// 若為一般畫面且非 Root，則 Pop 頂端畫面；若已是 Root 則回傳 false。
        /// </summary>
        public bool HandleBackRequest()
        {
            if (_isTransitioning || _stack.Count == 0) return false;

            IScreen curr = CurrentScreen;
            if (curr != null && curr.OnBackRequested())
            {
                // 畫面自身已消費返回行為
                return true;
            }

            if (_stack.Count > 1)
            {
                return Pop();
            }

            return false;
        }
    }
}
