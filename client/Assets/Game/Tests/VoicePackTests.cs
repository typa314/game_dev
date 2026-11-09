#if UNITY_2021_1_OR_NEWER
using System.IO;
using Game.Core.Audio;
using Game.Unity.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    [TestFixture]
    public sealed class VoicePackTests
    {
        private string _voiceDir;
        private GameObject _audioHolder;
        private UnityAudioDriver _driver;
        private AudioService _service;

        [SetUp]
        public void SetUp()
        {
            _voiceDir = Path.Combine(Application.streamingAssetsPath, "Audio", "Voice", "char18_sakura_miko");

            _audioHolder = new GameObject("Test_AudioDriver_Holder");
            _driver = _audioHolder.AddComponent<UnityAudioDriver>();
            _service = new AudioService(_driver);
        }

        [TearDown]
        public void TearDown()
        {
            if (_audioHolder != null)
            {
                Object.DestroyImmediate(_audioHolder);
            }
        }

        [Test]
        public void M1_VoicePack_1_AllAudioFilesExistInStreamingAssets()
        {
            Assert.IsTrue(Directory.Exists(_voiceDir), $"語音目錄不存在: {_voiceDir}");

            string[] requiredLines = new[]
            {
                "greeting", "tap_1", "tap_2", "idle",
                "battle_start", "battle_skill", "battle_win", "gacha_summon"
            };

            foreach (string lineId in requiredLines)
            {
                string wavPath = Path.Combine(_voiceDir, $"{lineId}.wav");
                string mp3Path = Path.Combine(_voiceDir, $"{lineId}.mp3");

                Assert.IsTrue(File.Exists(wavPath), $"缺少 WAV 語音檔案: {wavPath}");
                Assert.IsTrue(File.Exists(mp3Path), $"缺少 MP3 語音檔案: {mp3Path}");

                long wavBytes = new FileInfo(wavPath).Length;
                long mp3Bytes = new FileInfo(mp3Path).Length;

                Assert.Greater(wavBytes, 10000, $"{lineId}.wav 檔案過小");
                Assert.Greater(mp3Bytes, 5000, $"{lineId}.mp3 檔案過小");
            }
        }

        [Test]
        public void M1_VoicePack_2_ManifestJson_IsCompleteAndValid()
        {
            string manifestPath = Path.Combine(_voiceDir, "voice_manifest.json");
            Assert.IsTrue(File.Exists(manifestPath), $"缺少 voice_manifest.json: {manifestPath}");

            string json = File.ReadAllText(manifestPath);
            Assert.IsTrue(json.Contains("char18_sakura_miko"));
            Assert.IsTrue(json.Contains("VOICEVOX:"), "manifest 必須包含 VOICEVOX 聲線作者標示 (credit)");
            Assert.IsTrue(json.Contains("greeting"));
            Assert.IsTrue(json.Contains("battle_skill"));
        }

        [Test]
        public void M1_VoicePack_3_WavLoader_ParsesPcmHeadersCorrectly()
        {
            string wavPath = Path.Combine(_voiceDir, "greeting.wav");
            AudioClip clip = WavLoader.LoadWavFromFile(wavPath, "sakura_greeting");

            Assert.IsNotNull(clip, "WavLoader 應成功載入 greeting.wav");
            Assert.AreEqual("sakura_greeting", clip.name);
            Assert.AreEqual(2, clip.channels, "應為雙聲道立體聲");
            Assert.AreEqual(44100, clip.frequency, "取樣率應為 44.1 kHz");
            Assert.Greater(clip.length, 5.0f, "問候語時長應大於 5 秒");
        }

        [Test]
        public void M1_VoicePack_4_UnityAudioDriver_ResolvesAllVoiceClips_WithoutPlaying()
        {
            // 只解析 Clip、不呼叫 Play，測試過程不會發出聲音
            foreach (string lineId in new[] { "greeting", "tap_1", "tap_2", "idle", "battle_start", "battle_skill", "battle_win", "gacha_summon" })
            {
                AudioClip clip = _driver.ResolveAudioClip("Voice", $"char18_sakura_miko/{lineId}");
                Assert.IsNotNull(clip, $"無法解析語音 {lineId}");
                Assert.Greater(clip.length, 3.0f, $"{lineId} 時長異常");
            }
            Assert.IsFalse(_driver.VoiceSource != null && _driver.VoiceSource.isPlaying, "解析 Clip 不應觸發播放");
        }

        private sealed class SilentDriver : IAudioDriver
        {
            public string LastVoiceKey; public float LastVoiceVolume;
            public void PlayBgm(string k, float v, bool l) { }
            public void StopBgm() { }
            public void SetBgmVolume(float v) { }
            public void PlaySe(string k, float v) { }
            public void PlayVoice(string k, float v) { LastVoiceKey = k; LastVoiceVolume = v; }
            public void StopVoice() { }
        }

        [Test]
        public void M1_VoicePack_5_AudioService_VolumeControlsPropagate()
        {
            var silent = new SilentDriver();
            var service = new AudioService(silent);
            service.SetVolumes(1.0f, 0.8f, 0.7f, 0.65f);
            Assert.AreEqual(0.65f, service.VoiceVolume, 0.001f);
            Assert.AreEqual(0.65f, service.EffectiveVoiceVolume, 0.001f);

            service.PlayVoice("char18_sakura_miko/battle_start");
            Assert.AreEqual("char18_sakura_miko/battle_start", silent.LastVoiceKey);
            Assert.AreEqual(0.65f, silent.LastVoiceVolume, 0.01f);
        }

        // 真的會出聲，僅手動執行（[Explicit]），一般批次測試不會跑
        [Test, Explicit("會經由裝置實際播放語音")]
        public void M1_VoicePack_6_Audible_UnityAudioDriver_PlaysTapVoice()
        {
            _driver.PlayVoice("char18_sakura_miko/battle_start", 0.5f);
            Assert.IsTrue(_driver.VoiceSource.isPlaying, "VoiceSource 應處於播放狀態");
            System.Threading.Thread.Sleep(5000);
            _driver.StopVoice();
            Assert.IsFalse(_driver.VoiceSource.isPlaying);
        }
    }
}
#endif
