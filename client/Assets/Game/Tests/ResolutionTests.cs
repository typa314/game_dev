using System;
using NUnit.Framework;
using Game.Core.UI;

namespace Game.Tests
{
    [TestFixture]
    public class ResolutionTests
    {
        // 核心 UI 元素配置（以 1920x1080 安全框為基準）
        private static readonly UIRect TopLeftProfile = new UIRect(20, 980, 360, 80);
        private static readonly UIRect TopRightCurrencies = new UIRect(1400, 1000, 500, 60);
        private static readonly UIRect BottomAdvDialog = new UIRect(210, 30, 1500, 240);
        private static readonly UIRect BottomRightDeployButton = new UIRect(1650, 40, 240, 80);
        private static readonly UIRect CenterBattleField = new UIRect(160, 200, 1600, 680);

        [Test]
        public void F10_1_Standard16x9_FullHdAnd4k_MaintainsExactResolutionAndZeroClipping()
        {
            // 1920x1080 (1080p)
            var r1080 = ResolutionLayoutCalculator.Calculate(1920, 1080);
            Assert.AreEqual(AspectRatioCategory.Standard16x9, r1080.Category);
            Assert.AreEqual(1920.0, r1080.VirtualCanvasWidth, 0.01);
            Assert.AreEqual(1080.0, r1080.VirtualCanvasHeight, 0.01);
            Assert.AreEqual(0.0, r1080.SafeContentRect.X, 0.01);
            Assert.AreEqual(0.0, r1080.SafeContentRect.Y, 0.01);
            Assert.AreEqual(1920.0, r1080.SafeContentRect.Width, 0.01);
            Assert.AreEqual(1080.0, r1080.SafeContentRect.Height, 0.01);

            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(r1080, TopLeftProfile));
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(r1080, TopRightCurrencies));
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(r1080, BottomAdvDialog));
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(r1080, BottomRightDeployButton));
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(r1080, CenterBattleField));

            // 3840x2160 (4K UHD) - 比例相同
            var r4k = ResolutionLayoutCalculator.Calculate(3840, 2160);
            Assert.AreEqual(AspectRatioCategory.Standard16x9, r4k.Category);
            Assert.AreEqual(1920.0, r4k.VirtualCanvasWidth, 0.01);
            Assert.AreEqual(1080.0, r4k.VirtualCanvasHeight, 0.01);
        }

        [Test]
        public void F10_2_Narrow16x10_SteamDeck_MatchesWidthToPreventHorizontalClipping()
        {
            // Steam Deck 原生解析度: 1280x800 (16:10, aspect = 1.6)
            var deck = ResolutionLayoutCalculator.Calculate(1280, 800);
            Assert.AreEqual(AspectRatioCategory.Narrow16x10, deck.Category);
            Assert.AreEqual(0.0f, deck.RecommendedMatchWidthOrHeight, "窄螢幕必須 Match Width (0.0)，防止左右兩側被裁切");
            Assert.AreEqual(1920.0, deck.VirtualCanvasWidth, 0.01);
            Assert.AreEqual(1200.0, deck.VirtualCanvasHeight, 0.01, "虛擬畫布高度延伸至 1200");

            // 16:9 內容安全框應垂直居中
            Assert.AreEqual(0.0, deck.SafeContentRect.X, 0.01);
            Assert.AreEqual(60.0, deck.SafeContentRect.Y, 0.01, "Y 位移應為 (1200-1080)/2 = 60");
            Assert.AreEqual(1920.0, deck.SafeContentRect.Width, 0.01);
            Assert.AreEqual(1080.0, deck.SafeContentRect.Height, 0.01);

            // 在安全框內平移後的各元件應 100% 可視
            var offsetTopRight = new UIRect(TopRightCurrencies.X, TopRightCurrencies.Y + deck.SafeContentRect.Y, TopRightCurrencies.Width, TopRightCurrencies.Height);
            var offsetDeploy = new UIRect(BottomRightDeployButton.X, BottomRightDeployButton.Y + deck.SafeContentRect.Y, BottomRightDeployButton.Width, BottomRightDeployButton.Height);

            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(deck, offsetTopRight));
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(deck, offsetDeploy));

            // PC 16:10 解析度: 1920x1200
            var pc16x10 = ResolutionLayoutCalculator.Calculate(1920, 1200);
            Assert.AreEqual(AspectRatioCategory.Narrow16x10, pc16x10.Category);
            Assert.AreEqual(1920.0, pc16x10.VirtualCanvasWidth, 0.01);
            Assert.AreEqual(1200.0, pc16x10.VirtualCanvasHeight, 0.01);
        }

        [Test]
        public void F10_3_Narrow16x10_PositiveControl_NaiveMatchHeightClipsRightEdgeButtons()
        {
            // 陽性對照：若錯誤使用 Match Height (1.0)
            // 在 1280x800 下，畫布寬度將被縮減至 1080 * (1280/800) = 1728
            // 位於 X=1800 的右側功能按鈕與出擊按鈕必會被直接裁切！
            var rightMostButton = new UIRect(1750, 40, 150, 80); // 右邊界達 1900 > 1728
            bool clips = ResolutionLayoutCalculator.DoesNaiveMatchHeightClipInNarrowScreen(1280, 800, rightMostButton);
            Assert.IsTrue(clips, "陽性對照：Naive Match Height 應檢測出右側邊界超出裁切");

            // 但透過本計算器的自適應佈局，在 Deck 下該按鈕位於 1920 畫布內，完全不被裁切
            var deckLayout = ResolutionLayoutCalculator.Calculate(1280, 800);
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(deckLayout, rightMostButton),
                "自適應佈局下，右側按鈕完好保留於 1920 寬度內");
        }

        [Test]
        public void F10_4_UltraWide21x9_MatchesHeightAndExpandsHorizontalBackground()
        {
            // 2560x1080 (WFHD 21:9, aspect ~ 2.37037)
            var ultra = ResolutionLayoutCalculator.Calculate(2560, 1080);
            Assert.AreEqual(AspectRatioCategory.UltraWide21x9, ultra.Category);
            Assert.AreEqual(1.0f, ultra.RecommendedMatchWidthOrHeight, "超寬螢幕必須 Match Height (1.0)，水平延伸背景");
            Assert.AreEqual(1080.0, ultra.VirtualCanvasHeight, 0.01);
            Assert.AreEqual(2560.0, ultra.VirtualCanvasWidth, 0.01, "虛擬畫布寬度水平延伸至 2560");

            // 16:9 核心內容區塊水平置中：OffsetX = (2560 - 1920) / 2 = 320
            Assert.AreEqual(320.0, ultra.SafeContentRect.X, 0.01);
            Assert.AreEqual(0.0, ultra.SafeContentRect.Y, 0.01);
            Assert.AreEqual(1920.0, ultra.SafeContentRect.Width, 0.01);
            Assert.AreEqual(1080.0, ultra.SafeContentRect.Height, 0.01);

            // 全螢幕背景延伸範圍無黑邊
            var bg = ultra.FullBackgroundRect;
            Assert.AreEqual(0.0, bg.X);
            Assert.AreEqual(2560.0, bg.Width);
            Assert.AreEqual(1080.0, bg.Height);

            // 置中後的 16:9 內容元件 100% 落在視窗內
            var centeredDialog = new UIRect(BottomAdvDialog.X + ultra.SafeContentRect.X, BottomAdvDialog.Y, BottomAdvDialog.Width, BottomAdvDialog.Height);
            Assert.IsTrue(ResolutionLayoutCalculator.IsElementFullyVisible(ultra, centeredDialog));

            // 3440x1440 (WQHD 21:9, aspect ~ 2.38888)
            var wqhd = ResolutionLayoutCalculator.Calculate(3440, 1440);
            Assert.AreEqual(AspectRatioCategory.UltraWide21x9, wqhd.Category);
            Assert.AreEqual(1.0f, wqhd.RecommendedMatchWidthOrHeight);
            Assert.AreEqual(1080.0, wqhd.VirtualCanvasHeight, 0.01);
            Assert.AreEqual(1080.0 * (3440.0 / 1440.0), wqhd.VirtualCanvasWidth, 0.01);
        }

        [Test]
        public void F10_5_ExtremeAspectRatios_4x3And32x9_HandledGracefully()
        {
            // 4:3 傳統螢幕 (1024x768, aspect = 1.333)
            var r4x3 = ResolutionLayoutCalculator.Calculate(1024, 768);
            Assert.AreEqual(AspectRatioCategory.Narrow16x10, r4x3.Category);
            Assert.AreEqual(0.0f, r4x3.RecommendedMatchWidthOrHeight);
            Assert.AreEqual(1920.0, r4x3.VirtualCanvasWidth, 0.01);
            Assert.AreEqual(1440.0, r4x3.VirtualCanvasHeight, 0.01, "4:3 高度延伸至 1440");
            Assert.AreEqual(180.0, r4x3.SafeContentRect.Y, 0.01, "Y 位移 (1440-1080)/2 = 180");

            // 32:9 超寬曲面螢幕 (5120x1440, aspect = 3.555)
            var r32x9 = ResolutionLayoutCalculator.Calculate(5120, 1440);
            Assert.AreEqual(AspectRatioCategory.UltraWide21x9, r32x9.Category);
            Assert.AreEqual(1.0f, r32x9.RecommendedMatchWidthOrHeight);
            Assert.AreEqual(1080.0, r32x9.VirtualCanvasHeight, 0.01);
            Assert.AreEqual(3840.0, r32x9.VirtualCanvasWidth, 0.01);
            Assert.AreEqual(960.0, r32x9.SafeContentRect.X, 0.01, "X 位移 (3840-1920)/2 = 960");
        }

        [Test]
        public void F10_6_InvalidDimensions_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => ResolutionLayoutCalculator.Calculate(0, 1080));
            Assert.Throws<ArgumentException>(() => ResolutionLayoutCalculator.Calculate(1920, -5));
        }
    }
}
