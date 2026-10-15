using Game.Domain.Portrait;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 立繪呈現介面（design/09 §2）。實作：StaticPortraitView（PNG＋程式動態效果）、Live2DPortraitView（Cubism，GAME_LIVE2D）。
    /// 表情/動作一律用語意詞彙（neutral / smile / shy ...、idle / greet / tap_head ...）。
    /// TODO: PortraitFactory：art.live2d 有值、資產存在、設定允許時選 Live2D，否則選 Static。
    /// TODO: 補上 SetLipSync(float)、OnTouched(TouchArea)；Show 之後改成非同步載入。
    /// </summary>
    public interface IPortraitView
    {
        void Show(ResolvedPortrait portrait);
        void SetExpression(string expr);
        void PlayMotion(string motion, bool loop = false);
        void LookAt(Vector2 screenPos);
    }
}
