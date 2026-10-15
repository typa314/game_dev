// Live2D（Cubism SDK for Unity）實作的空殼。Cubism SDK 尚未匯入，所以整個檔案用 GAME_LIVE2D 包住；
// 匯入 SDK 並在 Player Settings → Scripting Define Symbols 加上 GAME_LIVE2D 後才會編譯。
#if GAME_LIVE2D
using Game.Domain.Portrait;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// L1/L2 Live2D 立繪（design/09）。
    /// TODO: Show：依 art.live2d 載入 .model3.json（Addressables key char/{charId}/live2d/{stageId|costumeId}）。
    /// TODO: SetExpression / PlayMotion：透過每個模型的語意詞對照表，對應到 .exp3.json / .motion3.json。
    /// TODO: LookAt：設定 ParamAngleX/Y、ParamEyeBallX/Y；SetLipSync：ParamMouthOpenY。
    /// </summary>
    public sealed class Live2DPortraitView : MonoBehaviour, IPortraitView
    {
        public void Show(ResolvedPortrait portrait)
        {
            // TODO
        }

        public void SetExpression(string expr)
        {
            // TODO
        }

        public void PlayMotion(string motion, bool loop = false)
        {
            // TODO
        }

        public void LookAt(Vector2 screenPos)
        {
            // TODO
        }
    }
}
#endif
