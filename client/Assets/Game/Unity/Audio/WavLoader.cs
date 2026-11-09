using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Unity.Audio
{
    /// <summary>
    /// 高效能標準 PCM 16-bit WAV 同步解析器，可直接將 StreamingAssets 下的音訊檔案載入為 UnityEngine.AudioClip。
    /// 無需經由 UnityWebRequest 或非同步協程，零卡頓立即播放。
    /// </summary>
    public static class WavLoader
    {
        public static AudioClip LoadWavFromFile(string filePath, string clipName = null)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                string name = clipName ?? Path.GetFileNameWithoutExtension(filePath);
                return LoadWavFromBytes(bytes, name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WavLoader] 載入 WAV 失敗 ({filePath}): {ex.Message}");
                return null;
            }
        }

        public static AudioClip LoadWavFromBytes(byte[] wavBytes, string clipName = "audio_clip")
        {
            if (wavBytes == null || wavBytes.Length < 44)
            {
                return null;
            }

            // 檢查 RIFF 標頭
            string riff = Encoding.ASCII.GetString(wavBytes, 0, 4);
            string wave = Encoding.ASCII.GetString(wavBytes, 8, 4);
            if (riff != "RIFF" || wave != "WAVE")
            {
                Debug.LogWarning("[WavLoader] 非標準 RIFF/WAVE 檔案格式");
                return null;
            }

            int channels = BitConverter.ToInt16(wavBytes, 22);
            int sampleRate = BitConverter.ToInt32(wavBytes, 24);
            int bitsPerSample = BitConverter.ToInt16(wavBytes, 34);

            if (channels <= 0 || sampleRate <= 0)
            {
                return null;
            }

            // 搜尋 'data' 區塊
            int pos = 12;
            while (pos < wavBytes.Length - 8)
            {
                string chunkId = Encoding.ASCII.GetString(wavBytes, pos, 4);
                int chunkSize = BitConverter.ToInt32(wavBytes, pos + 4);

                if (chunkId == "data")
                {
                    pos += 8;
                    int bytesPerSample = bitsPerSample / 8;
                    if (bytesPerSample <= 0) return null;

                    int totalSamples = chunkSize / bytesPerSample;
                    float[] audioData = new float[totalSamples];

                    if (bitsPerSample == 16)
                    {
                        for (int i = 0; i < totalSamples; i++)
                        {
                            if (pos + i * 2 + 1 >= wavBytes.Length) break;
                            short val = BitConverter.ToInt16(wavBytes, pos + i * 2);
                            audioData[i] = val / 32768.0f;
                        }
                    }
                    else if (bitsPerSample == 8)
                    {
                        for (int i = 0; i < totalSamples; i++)
                        {
                            if (pos + i >= wavBytes.Length) break;
                            byte val = wavBytes[pos + i];
                            audioData[i] = (val - 128) / 128.0f;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[WavLoader] 不支援的位元深度: {bitsPerSample} bits");
                        return null;
                    }

                    int lengthSamples = totalSamples / channels;
                    AudioClip clip = AudioClip.Create(clipName, lengthSamples, channels, sampleRate, false);
                    clip.SetData(audioData, 0);
                    return clip;
                }

                pos += 8 + chunkSize;
            }

            return null;
        }
    }
}
