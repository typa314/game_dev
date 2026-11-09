using System;
using Game.Core.UI;
using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// Unity UGUI 畫面抽象基底類別 (ARCH-02)。
    /// 繼承 MonoBehaviour 並實作 Game.Core.UI.IScreen 生命週期介面，
    /// 讓 UGUI 畫面能無縫接入 ScreenNavigator 堆疊管理與 Esc/返回鍵事件鏈。
    /// </summary>
    public abstract class UnityScreenBase : MonoBehaviour, IScreen
    {
        [Header("畫面基底參照")]
        [SerializeField] protected Canvas _canvas;

        /// <summary>畫面唯一識別碼（由各畫面子類別實作）。</summary>
        public abstract string ScreenId { get; }

        /// <summary>
        /// 畫面顯示/隱藏狀態屬性（相容既有 UGUI 畫面存取邏輯）。
        /// 優先控制 Canvas.enabled，若無 Canvas 則控制 GameObject active。
        /// </summary>
        public virtual bool IsVisible
        {
            get => _canvas != null ? _canvas.enabled : gameObject.activeSelf;
            set
            {
                if (_canvas != null)
                {
                    _canvas.enabled = value;
                }
                else
                {
                    gameObject.SetActive(value);
                }
            }
        }

        /// <summary>
        /// 當畫面被 Push 推入導航堆疊頂端時呼叫。
        /// </summary>
        public virtual void OnPush(object args)
        {
            gameObject.SetActive(true);
            if (_canvas != null)
            {
                _canvas.enabled = true;
            }
        }

        /// <summary>
        /// 當畫面從導航堆疊彈出時呼叫。
        /// </summary>
        public virtual void OnPop()
        {
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 當有新畫面覆蓋在自己上方時呼叫。
        /// </summary>
        public virtual void OnPause()
        {
        }

        /// <summary>
        /// 當上層覆蓋畫面 Pop 關閉、自己重新回到堆疊頂端時呼叫。
        /// </summary>
        public virtual void OnResume()
        {
            gameObject.SetActive(true);
            if (_canvas != null)
            {
                _canvas.enabled = true;
            }
        }

        /// <summary>
        /// 當使用者按下返回鍵（例如 Esc 或 Android Back）時呼叫。
        /// 若畫面內部有未關閉的子彈窗，應在此關閉並回傳 true；
        /// 若已無子彈窗，回傳 false 交由 Navigator 執行 Pop。
        /// </summary>
        public virtual bool OnBackRequested()
        {
            return false;
        }
    }
}
