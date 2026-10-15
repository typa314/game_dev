using System;
using Game.Domain;

namespace Game.Core.Settings
{
    /// <summary>
    /// 遊戲全域使用者設定（design/17 §D4）。
    /// </summary>
    public sealed class GameSettings
    {
        public float BgmVolume { get; set; } = 1.0f;
        public float SeVolume { get; set; } = 1.0f;
        public float VoiceVolume { get; set; } = 1.0f;
        public float TextSpeed { get; set; } = 1.0f;
        public float AutoSpeed { get; set; } = 1.0f;
        public bool SkipUnread { get; set; } = false;
        public bool UseLive2d { get; set; } = true;
        public string Language { get; set; } = "zh-TW";
        public Rating RatingChoice { get; set; } = Rating.G;

        public GameSettings Clone()
        {
            return new GameSettings
            {
                BgmVolume = BgmVolume,
                SeVolume = SeVolume,
                VoiceVolume = VoiceVolume,
                TextSpeed = TextSpeed,
                AutoSpeed = AutoSpeed,
                SkipUnread = SkipUnread,
                UseLive2d = UseLive2d,
                Language = Language,
                RatingChoice = RatingChoice
            };
        }
    }
}
