#if UNITY_2021_1_OR_NEWER
using Game.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    [TestFixture]
    public sealed class LobbyDynamicPortraitTests
    {
        private GameObject _holder;
        private RectTransform _charRt;
        private RectTransform _pedestalRt;
        private Image _pedestalImg;
        private RectTransform _bubbleRt;
        private LobbyDynamicPortrait _portrait;

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("Test_DynamicPortrait_Holder");

            var charGo = new GameObject("CharStand");
            charGo.transform.SetParent(_holder.transform);
            _charRt = charGo.AddComponent<RectTransform>();
            _charRt.anchoredPosition = new Vector2(0f, -50f);
            _charRt.localScale = Vector3.one;
            _charRt.localEulerAngles = Vector3.zero;

            var pedGo = new GameObject("Pedestal");
            pedGo.transform.SetParent(_holder.transform);
            _pedestalRt = pedGo.AddComponent<RectTransform>();
            _pedestalImg = pedGo.AddComponent<Image>();
            _pedestalImg.color = new Color(0f, 0.85f, 1f, 0.55f);

            var bubbleGo = new GameObject("Bubble");
            bubbleGo.transform.SetParent(_holder.transform);
            _bubbleRt = bubbleGo.AddComponent<RectTransform>();
            _bubbleRt.anchoredPosition = new Vector2(170f, 0f);

            _portrait = charGo.AddComponent<LobbyDynamicPortrait>();
            _portrait.Initialize(_charRt, _pedestalRt, _pedestalImg, _bubbleRt);
        }

        [TearDown]
        public void TearDown()
        {
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
            }
        }

        [Test]
        public void M1_DynamicPortrait_1_InitializesAndCapturesBaseTransforms()
        {
            Assert.IsNotNull(_portrait);
            Assert.IsFalse(_portrait.IsBouncing);
            Assert.AreEqual(0f, _portrait.CurrentLookAtAngle);
            Assert.AreEqual(Vector2.zero, _portrait.CurrentLookAtOffset);
        }

        [Test]
        public void M1_DynamicPortrait_2_Breathing_OscillatesScaleYAndVolumeX()
        {
            // 呼吸週期 3.6s，1/4 週期 = 0.9s 為正弦波最高峰 (sin(π/2) = 1)
            _portrait.EnableSway = false;
            _portrait.EnableLookAt = false;
            _portrait.SimulateTick(0.9f);

            // 吸氣時 Y 軸伸展、X 軸微束 (體積守恆)
            Assert.Greater(_charRt.localScale.y, 1.0f, "呼吸最高峰 Y 軸應大於基準尺度 1.0");
            Assert.Less(_charRt.localScale.x, 1.0f, "呼吸最高峰 X 軸應微縮小於 1.0 (體積守恆)");
            Assert.Greater(_charRt.anchoredPosition.y, -50f, "呼吸吸氣時垂直高度應微幅抬升");
        }

        [Test]
        public void M1_DynamicPortrait_3_IdleSway_RotatesZ()
        {
            _portrait.EnableBreathing = false;
            _portrait.EnableLookAt = false;

            _portrait.SimulateTick(1.2f);
            Assert.AreNotEqual(0f, _charRt.localEulerAngles.z, "重心微晃應產生非零的 Z 軸旋轉角");
        }

        [Test]
        public void M1_DynamicPortrait_4_LookAt_TracksSimulatedPointer()
        {
            _portrait.EnableBreathing = false;
            _portrait.EnableSway = false;

            // 游標在右側偏上方 (1.0, 0.5)
            _portrait.SetSimulatedPointer(new Vector2(1.0f, 0.5f));
            _portrait.SimulateTick(0.5f);

            Assert.Greater(_portrait.CurrentLookAtAngle, 0f, "游標在右側時身體應向右傾斜 (Z > 0)");
            Assert.Greater(_portrait.CurrentLookAtOffset.x, 0f, "游標在右側時身軀應向右平移 (OffsetX > 0)");
            Assert.Greater(_portrait.CurrentLookAtOffset.y, 0f, "游標在上方時身軀應微幅向上跟隨 (OffsetY > 0)");
        }

        [Test]
        public void M1_DynamicPortrait_5_TapReaction_TriggersSquashAndBounce()
        {
            _portrait.EnableBreathing = false;
            _portrait.EnableSway = false;
            _portrait.EnableLookAt = false;

            _portrait.TriggerTapReaction();
            Assert.IsTrue(_portrait.IsBouncing, "觸發點擊後應進入彈跳狀態");

            // 推進小段時間驗證彈性阻尼運作
            _portrait.SimulateTick(0.12f);
            Assert.IsTrue(_portrait.IsBouncing);
            Assert.AreNotEqual(-50f, _charRt.anchoredPosition.y, "點擊彈跳應使立繪產生位移");
        }

        [Test]
        public void M1_DynamicPortrait_6_HoloPedestal_RotatesAndPulsesAlpha()
        {
            float initZ = _pedestalRt.localEulerAngles.z;
            _portrait.SimulateTick(1.0f);

            Assert.AreNotEqual(initZ, _pedestalRt.localEulerAngles.z, "全息底座應隨時間自轉");
            Assert.Greater(_pedestalImg.color.a, 0.25f, "底座微光 Alpha 應在正常範圍");
            Assert.LessOrEqual(_pedestalImg.color.a, 0.75f, "底座微光 Alpha 應在正常上限範圍");
        }

        [Test]
        public void M1_DynamicPortrait_7_ResetToBase_RestoresTransform()
        {
            _portrait.SimulateTick(1.5f);
            _portrait.TriggerTapReaction();
            _portrait.ResetToBase();

            Assert.AreEqual(new Vector2(0f, -50f), _charRt.anchoredPosition);
            Assert.AreEqual(Vector3.one, _charRt.localScale);
            Assert.AreEqual(Vector3.zero, _charRt.localEulerAngles);
            Assert.IsFalse(_portrait.IsBouncing);
        }
    }
}
#endif
