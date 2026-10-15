using System.Collections.Generic;

namespace Game.Core.Story
{
    /// <summary>
    /// ADV 演出介面，由呈現層實作（例如 Unity ADV 播放器）。
    /// 負責接收純演出性質的指令（背景、立繪、表情、動作、音訊、特效等）。
    /// </summary>
    public interface IStoryPresenter
    {
        void Bg(string sceneId, string variant = null);
        void Show(string characterId, IReadOnlyList<string> options = null);
        void Hide(string characterId);
        void Expr(string characterId, string expression);
        void Motion(string characterId, string motion);
        void Cg(string cgId);
        void Bgm(string bgmId);
        void Se(string seId);
        void Voice(string voiceId);
        void Wait(float seconds);
        void Fade(IReadOnlyList<string> args = null);
        void Shake(IReadOnlyList<string> args = null);
    }
}
