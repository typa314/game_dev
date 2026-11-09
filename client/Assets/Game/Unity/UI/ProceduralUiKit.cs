using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// 程序化 UI 紋理快取庫與 UGUI 節點輔助工具。
    /// 將圓角膠囊、圓形、漸層、對話氣泡、全息環與剪影等程序化紋理生成統一集中，
    /// 透過靜態字典快取生成的 Sprite 與 Texture2D，消除重複逐像素生成與記憶體洩漏 (ARCH-01)。
    /// </summary>
    public static class ProceduralUiKit
    {
        private struct CacheKey : IEquatable<CacheKey>
        {
            public readonly int ShapeType; // 0: Pill, 1: Circle, 2: Gradient, 3: SpeechBubble, 4: HoloRing, 5: Silhouette
            public readonly int W;
            public readonly int H;
            public readonly Color32 C1;
            public readonly Color32 C2;
            public readonly Color32 C3;
            public readonly int Param1;
            public readonly int Param2;
            public readonly bool HasC3;

            public CacheKey(int shapeType, int w, int h, Color32 c1, Color32 c2, Color32 c3, int param1, int param2, bool hasC3)
            {
                ShapeType = shapeType;
                W = w;
                H = h;
                C1 = c1;
                C2 = c2;
                C3 = c3;
                Param1 = param1;
                Param2 = param2;
                HasC3 = hasC3;
            }

            public bool Equals(CacheKey other)
            {
                return ShapeType == other.ShapeType &&
                       W == other.W &&
                       H == other.H &&
                       C1.r == other.C1.r && C1.g == other.C1.g && C1.b == other.C1.b && C1.a == other.C1.a &&
                       C2.r == other.C2.r && C2.g == other.C2.g && C2.b == other.C2.b && C2.a == other.C2.a &&
                       HasC3 == other.HasC3 &&
                       (!HasC3 || (C3.r == other.C3.r && C3.g == other.C3.g && C3.b == other.C3.b && C3.a == other.C3.a)) &&
                       Param1 == other.Param1 &&
                       Param2 == other.Param2;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ShapeType;
                    hash = (hash * 397) ^ W;
                    hash = (hash * 397) ^ H;
                    hash = (hash * 397) ^ (C1.r | (C1.g << 8) | (C1.b << 16) | (C1.a << 24));
                    hash = (hash * 397) ^ (C2.r | (C2.g << 8) | (C2.b << 16) | (C2.a << 24));
                    if (HasC3)
                    {
                        hash = (hash * 397) ^ (C3.r | (C3.g << 8) | (C3.b << 16) | (C3.a << 24));
                    }
                    hash = (hash * 397) ^ Param1;
                    hash = (hash * 397) ^ Param2;
                    return hash;
                }
            }
        }

        private static readonly Dictionary<CacheKey, Sprite> _spriteCache = new Dictionary<CacheKey, Sprite>();

        /// <summary>目前已快取的 Sprite 總數。</summary>
        public static int CachedSpriteCount => _spriteCache.Count;

        /// <summary>
        /// 清空快取字典，並徹底銷毀所有快取的 Sprite 與其底層 Texture2D，防止記憶體洩漏。
        /// </summary>
        public static void ClearCache()
        {
            foreach (var kvp in _spriteCache)
            {
                if (kvp.Value != null)
                {
                    if (kvp.Value.texture != null)
                    {
                        DestroyObject(kvp.Value.texture);
                    }
                    DestroyObject(kvp.Value);
                }
            }
            _spriteCache.Clear();
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        /// <summary>
        /// 取得或生成圓角膠囊 Sprite（兩端為半圓形）。
        /// </summary>
        public static Sprite GetPillSprite(int w, int h, Color fill, Color border)
        {
            if (w <= 0 || h <= 0) return null;
            var key = new CacheKey(0, w, h, (Color32)fill, (Color32)border, default, 0, 0, false);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            float r = h * 0.5f;
            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    float dx = x < r ? (r - x) : (x > w - r ? (x - (w - r)) : 0f);
                    float dy = y - r;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > r)
                    {
                        pixels[rowOffset + x] = Color.clear;
                    }
                    else if (dist > r - 2f)
                    {
                        pixels[rowOffset + x] = border;
                    }
                    else
                    {
                        pixels[rowOffset + x] = fill;
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sp;
            return sp;
        }

        /// <summary>
        /// 取得或生成正圓形 Sprite（含可自訂厚度的邊框）。
        /// </summary>
        public static Sprite GetCircleSprite(int size, Color fill, Color border, int borderWidth)
        {
            if (size <= 0) return null;
            var key = new CacheKey(1, size, size, (Color32)fill, (Color32)border, default, borderWidth, 0, false);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                int rowOffset = y * size;
                float dy = y - r;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > r)
                    {
                        pixels[rowOffset + x] = Color.clear;
                    }
                    else if (dist > r - borderWidth)
                    {
                        pixels[rowOffset + x] = border;
                    }
                    else
                    {
                        pixels[rowOffset + x] = fill;
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sp;
            return sp;
        }

        /// <summary>
        /// 取得或生成垂直漸層 Sprite（可選擇邊框色、邊框厚度與圓角半徑）。
        /// </summary>
        public static Sprite GetGradientSprite(int w, int h, Color top, Color bottom, Color? border = null, int borderWidth = 0, int radius = 0)
        {
            if (w <= 0 || h <= 0) return null;
            Color bCol = border ?? Color.clear;
            bool hasBorder = border.HasValue;
            var key = new CacheKey(2, w, h, (Color32)top, (Color32)bottom, (Color32)bCol, borderWidth, radius, hasBorder);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            float denom = h > 1 ? (h - 1) : 1f;
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / denom;
                Color rowCol = Color.Lerp(bottom, top, t);
                int rowOffset = y * w;

                for (int x = 0; x < w; x++)
                {
                    bool isBorder = false;
                    if (hasBorder && borderWidth > 0)
                    {
                        if (x < borderWidth || x >= w - borderWidth || y < borderWidth || y >= h - borderWidth)
                        {
                            isBorder = true;
                        }
                    }

                    if (radius > 0)
                    {
                        float cx = x < radius ? (radius - x) : (x >= w - radius ? (x - (w - radius - 1)) : 0f);
                        float cy = y < radius ? (radius - y) : (y >= h - radius ? (y - (h - radius - 1)) : 0f);
                        if (cx * cx + cy * cy > radius * radius)
                        {
                            pixels[rowOffset + x] = Color.clear;
                            continue;
                        }
                    }

                    pixels[rowOffset + x] = isBorder ? border.Value : rowCol;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sp;
            return sp;
        }

        /// <summary>
        /// 取得或生成對話氣泡 Sprite（圓角矩形，固定半徑 16px）。
        /// </summary>
        public static Sprite GetSpeechBubbleSprite(int w, int h, Color fill, Color border)
        {
            if (w <= 0 || h <= 0) return null;
            var key = new CacheKey(3, w, h, (Color32)fill, (Color32)border, default, 0, 0, false);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            float r = 16f;
            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    float cx = x < r ? (r - x) : (x >= w - r ? (x - (w - r - 1)) : 0f);
                    float cy = y < r ? (r - y) : (y >= h - r ? (y - (h - r - 1)) : 0f);
                    float dist = Mathf.Sqrt(cx * cx + cy * cy);

                    if (dist > r)
                    {
                        pixels[rowOffset + x] = Color.clear;
                    }
                    else if (dist > r - 2f)
                    {
                        pixels[rowOffset + x] = border;
                    }
                    else
                    {
                        pixels[rowOffset + x] = fill;
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sp;
            return sp;
        }

        /// <summary>
        /// 取得或生成全息地台環光 Sprite。
        /// </summary>
        public static Sprite GetHoloRingSprite(int w, int h, Color color)
        {
            if (w <= 0 || h <= 0) return null;
            var key = new CacheKey(4, w, h, (Color32)color, default, default, 0, 0, false);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            float rx = w * 0.5f;
            float ry = h * 0.5f;

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                float dy = (y - ry) / ry;
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - rx) / rx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    if (d > 1f || d < 0.65f)
                    {
                        pixels[rowOffset + x] = Color.clear;
                    }
                    else
                    {
                        float alpha = (1f - Mathf.Abs(d - 0.85f) / 0.15f) * color.a;
                        pixels[rowOffset + x] = new Color(color.r, color.g, color.b, alpha);
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sp;
            return sp;
        }

        /// <summary>
        /// 取得或生成看板娘預設占位剪影 Sprite。
        /// </summary>
        public static Sprite GetSilhouetteSprite(int w, int h)
        {
            if (w <= 0 || h <= 0) return null;
            var key = new CacheKey(5, w, h, default, default, default, 0, 0, false);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                float ty = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float tx = Mathf.Abs((float)x / w - 0.5f) * 2f;
                    float shape = 1f - ty * 0.5f;
                    if (tx < shape * 0.6f && ty < 0.95f)
                    {
                        float a = Mathf.Lerp(0.85f, 0.2f, ty);
                        pixels[rowOffset + x] = new Color(0.18f, 0.25f, 0.38f, a);
                    }
                    else
                    {
                        pixels[rowOffset + x] = Color.clear;
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sp;
            return sp;
        }

        /// <summary>
        /// 建立包含 RectTransform 的 UI GameObject 並附加至 parent。
        /// </summary>
        public static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>
        /// 將 RectTransform 設定為撐滿父容器（包含可選外邊距）。
        /// </summary>
        public static void StretchFull(RectTransform rt, float margin = 0f)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }

        /// <summary>
        /// 取得內建相容字型（優先 LegacyRuntime.ttf，備援 Arial.ttf）。
        /// </summary>
        public static Font GetDefaultFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
