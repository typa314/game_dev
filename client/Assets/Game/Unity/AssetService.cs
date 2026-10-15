using System.Collections.Generic;
using Game.Domain;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 以邏輯 key 取得資產（design/03 §4、design/15 §1）。key 例：char/char01_claire/portrait/ps_char01_s1/stand
    /// 目前實作：Resources.Load&lt;Sprite&gt;("Content/" + key)；找不到時回傳灰色 placeholder，不讓遊戲當掉。
    /// TODO: 換成 Addressables（content_g / content_r15 / content_r18 group），並改為非同步 API。
    /// TODO: 分級第二層過濾：讀取 asset manifest 的 rating，Rating.Allows(rating) 為 false 時改回 placeholder。
    /// </summary>
    public sealed class AssetService
    {
        private const string ResourcesRoot = "Content/";

        private readonly RatingPolicy _rating;
        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private readonly HashSet<string> _warned = new HashSet<string>();
        private Sprite _placeholder;

        public AssetService(RatingPolicy rating)
        {
            _rating = rating;
        }

        public RatingPolicy Rating { get { return _rating; } }

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

            // TODO(manifest): 在這裡查 manifest 的 rating，_rating.Allows(...) 為 false 就回傳 placeholder。
            s = Resources.Load<Sprite>(ResourcesRoot + key);
            if (s == null)
            {
                WarnOnce(key, "[Asset] 找不到資產 key：" + key + "，使用 placeholder");
                s = Placeholder;
            }
            _cache[key] = s;
            return s;
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
            // TODO: 依 design/03 §4 加上角色名稱文字（之後 UI 層疊一個 Label 即可）。
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 64f);
        }
    }
}
