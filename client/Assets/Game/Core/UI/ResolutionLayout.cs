using System;

namespace Game.Core.UI
{
    /// <summary>
    /// 定義螢幕比例分類
    /// </summary>
    public enum AspectRatioCategory
    {
        Standard16x9,  // 16:9（如 1920x1080、2560x1440、4K）
        Narrow16x10,   // 16:10（如 1280x800 Steam Deck、1920x1200）或更窄（如 4:3）
        UltraWide21x9  // 21:9（如 2560x1080、3440x1440）或更寬（如 32:9）
    }

    /// <summary>
    /// 矩形座標資料（純 C#，無 UnityEngine 依賴）
    /// </summary>
    public struct UIRect
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public double Right => X + Width;
        public double Top => Y + Height;

        public UIRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(UIRect other)
        {
            const double eps = 1e-4;
            return other.X >= X - eps &&
                   other.Right <= Right + eps &&
                   other.Y >= Y - eps &&
                   other.Top <= Top + eps;
        }

        public bool Intersects(UIRect other)
        {
            return X < other.Right && Right > other.X &&
                   Y < other.Top && Top > other.Y;
        }

        public override string ToString() => $"[({X:F1}, {Y:F1}) {Width:F1}x{Height:F1}]";
    }

    /// <summary>
    /// 解析度自適應版面計算結果
    /// </summary>
    public sealed class ResolutionLayoutResult
    {
        public int ViewportWidth { get; }
        public int ViewportHeight { get; }
        public double AspectRatio { get; }
        public AspectRatioCategory Category { get; }
        
        /// <summary>
        /// Unity CanvasScaler 的 matchWidthOrHeight 建議值：
        /// 寬螢幕 (>=16:9) 為 1.0 (Match Height)，避免垂直空間壓縮；
        /// 窄螢幕 (<16:9，如 Steam Deck 16:10) 為 0.0 (Match Width)，避免兩側按鈕被切掉。
        /// </summary>
        public float RecommendedMatchWidthOrHeight { get; }

        /// <summary>
        /// 虛擬畫布大小（以 1920x1080 為基準縮放）
        /// </summary>
        public double VirtualCanvasWidth { get; }
        public double VirtualCanvasHeight { get; }

        /// <summary>
        /// 16:9 核心內容安全展示區塊（保證永不被裁切）
        /// </summary>
        public UIRect SafeContentRect { get; }

        /// <summary>
        /// 全螢幕背景繪製區塊（全視窗填滿延伸，無黑邊）
        /// </summary>
        public UIRect FullBackgroundRect => new UIRect(0, 0, VirtualCanvasWidth, VirtualCanvasHeight);

        public ResolutionLayoutResult(
            int viewportWidth,
            int viewportHeight,
            double aspectRatio,
            AspectRatioCategory category,
            float recommendedMatch,
            double virtualCanvasWidth,
            double virtualCanvasHeight,
            UIRect safeContentRect)
        {
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
            AspectRatio = aspectRatio;
            Category = category;
            RecommendedMatchWidthOrHeight = recommendedMatch;
            VirtualCanvasWidth = virtualCanvasWidth;
            VirtualCanvasHeight = virtualCanvasHeight;
            SafeContentRect = safeContentRect;
        }
    }

    /// <summary>
    /// 多解析度與螢幕比例（16:9、16:10 Steam Deck、21:9 超寬螢幕）佈局計算器
    /// 依據 ADR-002 與 14_ui_screens.md 規範設計
    /// </summary>
    public static class ResolutionLayoutCalculator
    {
        public const double ReferenceWidth = 1920.0;
        public const double ReferenceHeight = 1080.0;
        public const double ReferenceAspect = ReferenceWidth / ReferenceHeight; // ~1.7777777777777777

        /// <summary>
        /// 計算給定視窗解析度下的畫布尺寸與安全佈局
        /// </summary>
        public static ResolutionLayoutResult Calculate(int viewportWidth, int viewportHeight)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0)
                throw new ArgumentException($"Invalid viewport dimensions: {viewportWidth}x{viewportHeight}");

            double aspect = (double)viewportWidth / viewportHeight;
            const double eps = 0.01;

            AspectRatioCategory category;
            float match;
            double virtualW;
            double virtualH;
            UIRect safeRect;

            if (Math.Abs(aspect - ReferenceAspect) < eps)
            {
                // 標準 16:9
                category = AspectRatioCategory.Standard16x9;
                match = 1.0f; // 或 0.5f，兩者皆為 1920x1080
                virtualW = ReferenceWidth;
                virtualH = ReferenceHeight;
                safeRect = new UIRect(0, 0, ReferenceWidth, ReferenceHeight);
            }
            else if (aspect > ReferenceAspect)
            {
                // 超寬螢幕 21:9 或更寬 (如 2560x1080, 3440x1440)
                category = AspectRatioCategory.UltraWide21x9;
                match = 1.0f; // Match Height (固定高度 1080，寬度水平延伸)
                virtualH = ReferenceHeight;
                virtualW = ReferenceHeight * aspect;
                double offsetX = (virtualW - ReferenceWidth) / 2.0;
                safeRect = new UIRect(offsetX, 0, ReferenceWidth, ReferenceHeight);
            }
            else
            {
                // 窄螢幕 16:10 (如 Steam Deck 1280x800) 或 4:3
                category = AspectRatioCategory.Narrow16x10;
                match = 0.0f; // Match Width (固定寬度 1920，高度垂直延伸，避免左右 UI 被切)
                virtualW = ReferenceWidth;
                virtualH = ReferenceWidth / aspect;
                double offsetY = (virtualH - ReferenceHeight) / 2.0;
                safeRect = new UIRect(0, offsetY, ReferenceWidth, ReferenceHeight);
            }

            return new ResolutionLayoutResult(
                viewportWidth,
                viewportHeight,
                aspect,
                category,
                match,
                virtualW,
                virtualH,
                safeRect);
        }

        /// <summary>
        /// 驗證特定 UI 元素在當前解析度下是否完全落在可視範圍內（無裁切）
        /// </summary>
        public static bool IsElementFullyVisible(ResolutionLayoutResult layout, UIRect elementRect)
        {
            var screenRect = new UIRect(0, 0, layout.VirtualCanvasWidth, layout.VirtualCanvasHeight);
            return screenRect.Contains(elementRect);
        }

        /// <summary>
        /// 陽性對照檢查：若在 16:10 下錯誤使用 Match Height (1.0)，水平邊界是否會發生溢出裁切
        /// </summary>
        public static bool DoesNaiveMatchHeightClipInNarrowScreen(int viewportWidth, int viewportHeight, UIRect elementAnchoredToRight)
        {
            double aspect = (double)viewportWidth / viewportHeight;
            if (aspect >= ReferenceAspect) return false;

            // Naive Match Height (1.0):
            // 虛擬高度固定 1080，但虛擬寬度縮減為 1080 * aspect (例如 16:10 時為 1728)
            double naiveVirtualWidth = ReferenceHeight * aspect;
            var naiveScreenRect = new UIRect(0, 0, naiveVirtualWidth, ReferenceHeight);

            // 若 UI 原本排在 1920 座標系的右側 (例如 1800~1920)，就會超出 1728
            return !naiveScreenRect.Contains(elementAnchoredToRight);
        }
    }
}
