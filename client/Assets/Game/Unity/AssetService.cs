using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Assets;
using Game.Domain;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 以邏輯 key 取得資產（design/03 §4、design/15 §1）。key 例：char/char01_claire/portrait/ps_char01_s1/stand
    /// 目前實作：Resources.Load<Sprite>("Content/" + key)；找不到時回傳灰色 placeholder，不讓遊戲當掉。
    /// 整合 AssetCatalog 執行第二層分級過濾防線。
    /// </summary>
    public sealed class AssetService
    {
        private const string ResourcesRoot = "Content/";

        private readonly RatingPolicy _rating;
        private readonly AssetCatalog _catalog;
        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private readonly HashSet<string> _warned = new HashSet<string>();
        private Sprite _placeholder;

        public AssetService(RatingPolicy rating, AssetCatalog catalog = null)
        {
            _rating = rating ?? throw new ArgumentNullException(nameof(rating));
            _catalog = catalog;
        }

        public RatingPolicy Rating => _rating;
        public AssetCatalog Catalog => _catalog;

        /// <summary>載入 Sprite；key 為 null/空字串或找不到時回傳 placeholder（每個 key 只警告一次）。</summary>
        public Sprite LoadSprite(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                WarnOnce("(empty)", "[Asset] 空的資產 key（主資料尚未填美術），使用 placeholder");
                return Placeholder;
            }

            Sprite s;
            if (_cache.TryGetValue(key, out s)) return s;

            // 第二層執行期分級防線：若 Catalog 存在且分級不允許，立即退回 Placeholder
            if (_catalog != null)
            {
                var resolve = _catalog.Resolve(key, _rating);
                if (!resolve.Allowed)
                {
                    WarnOnce(key, $"[Asset] 資產分級不允許：{key}（政策：{_rating.Effective}，原因：{resolve.Reason}），使用 placeholder");
                    _cache[key] = Placeholder;
                    return Placeholder;
                }
            }

            s = Resources.Load<Sprite>(ResourcesRoot + key);
            if (s == null)
            {
                WarnOnce(key, "[Asset] 找不到資產 key：" + key + "，使用 placeholder");
                s = Placeholder;
            }
            _cache[key] = s;
            return s;
        }

        /// <summary>
        /// 從 StreamingAssets 載入圖片並建立 Sprite 快取（供 UI 統一存取，零重複 IO 負擔）。
        /// relativePath 例："Portraits/char01_claire.png" 或 "UI/Icons/icon_gem.png"
        /// </summary>
        public Sprite LoadStreamingSprite(string relativePath, Vector2 pivot)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            if (_cache.TryGetValue(relativePath, out var cached) && cached != null)
                return cached;

            try
            {
                string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
                if (File.Exists(fullPath))
                {
                    byte[] data = File.ReadAllBytes(fullPath);
                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (tex.LoadImage(data))
                    {
                        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot);
                        _cache[relativePath] = sp;
                        return sp;
                    }
                }
            }
            catch (Exception ex)
            {
                WarnOnce(relativePath, $"[Asset] 載入 StreamingAssets 失敗：{relativePath}：{ex.Message}");
            }
            return null;
        }

        public void ClearCache()
        {
            foreach (var kvp in _cache)
            {
                if (kvp.Value != null && kvp.Value != _placeholder)
                {
                    if (kvp.Value.texture != null)
                    {
                        UnityEngine.Object.Destroy(kvp.Value.texture);
                    }
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            _cache.Clear();
            _warned.Clear();
        }

        /// <summary>灰色剪影 placeholder（64x128，pivot 在腳底中央）。</summary>
        public Sprite Placeholder
        {
            get
            {
                if (_placeholder == null) _placeholder = CreatePlaceholder();
                return _placeholder;
            }
        }

        private void WarnOnce(string key, string message)
        {
            if (_warned.Add(key)) Debug.LogWarning(message);
        }

        private static Sprite CreatePlaceholder()
        {
            const int w = 64, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            var gray = new Color32(128, 128, 128, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = gray;
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.name = "placeholder";
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 64f);
        }
    }
}
