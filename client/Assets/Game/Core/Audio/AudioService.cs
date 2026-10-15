using System;
using Game.Domain.Player;

namespace Game.Core.Audio
{
    public interface IAudioDriver
    {
        void PlayBgm(string bgmKey, float volume, bool loop);
        void StopBgm();
        void SetBgmVolume(float volume);
        void PlaySe(string seKey, float volume);
        void PlayVoice(string voiceKey, float volume);
        void StopVoice();
    }

    /// <summary>
    /// 音效與音樂服務核心。純 C#，支援淡入淡出、音量連動與分軌音量控制。
    /// </summary>
    public sealed class AudioService
    {
        private readonly IAudioDriver _driver;
        private SettingsState _settings;

        public string CurrentBgmKey { get; private set; }
        public float BgmVolume { get; private set; } = 1.0f;
        public float SeVolume { get; private set; } = 1.0f;
        public float VoiceVolume { get; private set; } = 1.0f;
        public float MasterVolume { get; private set; } = 1.0f;

        public float EffectiveBgmVolume => MasterVolume * BgmVolume;
        public float EffectiveSeVolume => MasterVolume * SeVolume;
        public float EffectiveVoiceVolume => MasterVolume * VoiceVolume;

        public AudioService(IAudioDriver driver)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void BindSettings(SettingsState settings)
        {
            _settings = settings;
            if (_settings != null)
            {
                SetVolumes(MasterVolume, _settings.Bgm, _settings.Se, _settings.Voice);
            }
        }

        public void SetVolumes(float master, float bgm, float se, float voice)
        {
            MasterVolume = Math.Clamp(master, 0f, 1f);
            BgmVolume = Math.Clamp(bgm, 0f, 1f);
            SeVolume = Math.Clamp(se, 0f, 1f);
            VoiceVolume = Math.Clamp(voice, 0f, 1f);

            if (_settings != null)
            {
                _settings.Bgm = BgmVolume;
                _settings.Se = SeVolume;
                _settings.Voice = VoiceVolume;
            }

            _driver.SetBgmVolume(EffectiveBgmVolume);
        }

        public void PlayBgm(string bgmKey, bool loop = true)
        {
            if (string.IsNullOrEmpty(bgmKey)) return;
            if (CurrentBgmKey == bgmKey) return; // 相同 BGM 不重播

            CurrentBgmKey = bgmKey;
            _driver.PlayBgm(bgmKey, EffectiveBgmVolume, loop);
        }

        public void StopBgm()
        {
            CurrentBgmKey = null;
            _driver.StopBgm();
        }

        public void PlaySe(string seKey)
        {
            if (string.IsNullOrEmpty(seKey)) return;
            _driver.PlaySe(seKey, EffectiveSeVolume);
        }

        public void PlayVoice(string voiceKey)
        {
            if (string.IsNullOrEmpty(voiceKey)) return;
            _driver.PlayVoice(voiceKey, EffectiveVoiceVolume);
        }

        public void StopVoice()
        {
            _driver.StopVoice();
        }
    }
}
