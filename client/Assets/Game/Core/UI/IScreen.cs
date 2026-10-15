using System;

namespace Game.Core.UI
{
    /// <summary>
    /// 畫面生命週期介面。純 C#，不依賴 UnityEngine。
    /// </summary>
    public interface IScreen
    {
        string ScreenId { get; }

        /// <summary>畫面被推入堆疊頂端時呼叫。</summary>
        void OnPush(object args);

        /// <summary>畫面被從堆疊中彈出並銷毀/隱藏時呼叫。</summary>
        void OnPop();

        /// <summary>當有新畫面 Push 疊在自己上方時呼叫。</summary>
        void OnPause();

        /// <summary>當上層畫面 Pop 關閉、自己重新回到頂端時呼叫。</summary>
        void OnResume();

        /// <summary>
        /// 當使用者按下返回鍵（例如 Esc 或 Android Back）時呼叫。
        /// 回傳 true 表示此畫面已自行消費返回操作（例如關閉了子浮動面板）；
        /// 回傳 false 表示交由 Navigator 執行標準的 Pop。
        /// </summary>
        bool OnBackRequested();
    }
}
