using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Audio;
using UnityEngine;

namespace Game.Unity.Audio
{
    /// <summary>
    /// Unity 實體音訊驅動層（實作 IAudioDriver，對接 design/11 與 design/15 規範）。
    /// 提供 BGM、SE、語音（Voice）多軌道獨立播放與即時音量調控。
    /// 優先從 StreamingAssets/Audio/ 下載入高品質音訊（.wav / .mp3）。
    /// </summary>
    public class UnityAudioDriver : MonoBehaviour, IAudioDriver
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource _bgmSource;
        [SerializeField] private AudioSource _seSource;
        [SerializeField] private AudioSource _voiceSource;

        private readonly Dictionary<string, AudioClip> _clipCache = new();
        private float _bgmVolume = 1.0f;
        private float _seVolume = 1.0f;
        private float _voiceVolume = 1.0f;

        public AudioSource BgmSource => _bgmSource;
        public AudioSource SeSource => _seSource;
        public AudioSource VoiceSource => _voiceSource;

        private void Awake()
        {
            EnsureAudioSources();
        }

        private void EnsureAudioSources()
        {
            if (_bgmSource == null)
            {
                var bgmGo = new GameObject("BGM_Source");
                bgmGo.transform.SetParent(transform);
                _bgmSource = bgmGo.AddComponent<AudioSource>();
                _bgmSource.loop = true;
                _bgmSource.playOnAwake = false;
            }

            if (_seSource == null)
            {
                var seGo = new GameObject("SE_Source");
                seGo.transform.SetParent(transform);
                _seSource = seGo.AddComponent<AudioSource>();
                _seSource.loop = false;
                _seSource.playOnAwake = false;
            }

            if (_voiceSource == null)
            {
                var voiceGo = new GameObject("Voice_Source");
                voiceGo.transform.SetParent(transform);
                _voiceSource = voiceGo.AddComponent<AudioSource>();
                _voiceSource.loop = false;
                _voiceSource.playOnAwake = false;
            }
        }

        public void PlayBgm(string bgmKey, float volume, bool loop)
        {
            EnsureAudioSources();
            _bgmVolume = volume;
            _bgmSource.volume = volume;
            _bgmSource.loop = loop;

            AudioClip clip = ResolveAudioClip("Bgm", bgmKey);
            if (clip != null)
            {
                _bgmSource.clip = clip;
                _bgmSource.Play();
            }
        }

        public void StopBgm()
        {
            if (_bgmSource != null)
            {
                _bgmSource.Stop();
                _bgmSource.clip = null;
            }
        }

        public void SetBgmVolume(float volume)
        {
            _bgmVolume = volume;
            if (_bgmSource != null)
            {
                _bgmSource.volume = volume;
            }
        }

        public void PlaySe(string seKey, float volume)
        {
            EnsureAudioSources();
            _seVolume = volume;
            AudioClip clip = ResolveAudioClip("SE", seKey);
            if (clip != null && _seSource != null)
            {
                _seSource.PlayOneShot(clip, volume);
            }
        }

        public void PlayVoice(string voiceKey, float volume)
        {
            EnsureAudioSources();
            _voiceVolume = volume;
            AudioClip clip = ResolveAudioClip("Voice", voiceKey);
            if (clip != null && _voiceSource != null)
            {
                _voiceSource.Stop();
                _voiceSource.clip = clip;
                _voiceSource.volume = volume;
                _voiceSource.Play();
            }
        }

        public void StopVoice()
        {
            if (_voiceSource != null)
            {
                _voiceSource.Stop();
                _voiceSource.clip = null;
            }
        }

        /// <summary>
        /// 解析並快取音訊 Clip。
        /// 支援邏輯 key 形式（如 "char18_sakura_miko/greeting" 或 "Voice/char18_sakura_miko/greeting"）。
        /// </summary>
        public AudioClip ResolveAudioClip(string category, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            string cacheKey = $"{category}:{key}";
            if (_clipCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            // 搜尋 StreamingAssets/Audio/{category}/{key}.wav
            string cleanKey = key.Replace('\\', '/').TrimStart('/');
            if (cleanKey.StartsWith("voice/", StringComparison.OrdinalIgnoreCase))
            {
                cleanKey = cleanKey.Substring("voice/".Length);
            }

            string baseDir = Path.Combine(Application.streamingAssetsPath, "Audio", category);
            string wavPath = Path.Combine(baseDir, cleanKey + ".wav");

            if (!File.Exists(wavPath))
            {
                // 嘗試不同路徑組合
                wavPath = Path.Combine(Application.streamingAssetsPath, "Audio", cleanKey + ".wav");
            }

            if (File.Exists(wavPath))
            {
                AudioClip clip = WavLoader.LoadWavFromFile(wavPath, cleanKey);
                if (clip != null)
                {
                    _clipCache[cacheKey] = clip;
                    return clip;
                }
            }

            return null;
        }
    }
}
