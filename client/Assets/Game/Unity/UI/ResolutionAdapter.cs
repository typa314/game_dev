using UnityEngine;
using UnityEngine.UI;
using Game.Core.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 自動根據目前視窗解析度與螢幕比例（16:9、16:10 Steam Deck、21:9 超寬螢幕）
    /// 動態調整 CanvasScaler 的 matchWidthOrHeight，並提供安全框範圍。
    /// 依據 ADR-002 與 14_ui_screens.md 設計。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    [ExecuteAlways]
    public class ResolutionAdapter : MonoBehaviour
    {
        [Header("組件參照")]
        [SerializeField] private CanvasScaler _scaler;
        [SerializeField] private RectTransform _safeContentRoot;

        [Header("偵錯與狀態")]
        [SerializeField] private int _lastWidth;
        [SerializeField] private int _lastHeight;
        [SerializeField] private AspectRatioCategory _currentCategory;
        [SerializeField] private float _currentMatch;

        public AspectRatioCategory CurrentCategory => _currentCategory;
        public float CurrentMatch => _currentMatch;

        private void Awake()
        {
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            ApplyLayout();
        }

        private void OnEnable()
        {
            ApplyLayout();
        }

        private void Update()
        {
            if (Screen.width != _lastWidth || Screen.height != _lastHeight)
            {
                ApplyLayout();
            }
        }

        public void ApplyLayout()
        {
            int w = Screen.width > 0 ? Screen.width : 1920;
            int h = Screen.height > 0 ? Screen.height : 1080;

            _lastWidth = w;
            _lastHeight = h;

            var result = ResolutionLayoutCalculator.Calculate(w, h);
            _currentCategory = result.Category;
            _currentMatch = result.RecommendedMatchWidthOrHeight;

            if (_scaler != null)
            {
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = new Vector2((float)ResolutionLayoutCalculator.ReferenceWidth, (float)ResolutionLayoutCalculator.ReferenceHeight);
                _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                _scaler.matchWidthOrHeight = _currentMatch;
            }

            if (_safeContentRoot != null)
            {
                // 設定 16:9 核心安全內容容器的錨點與位移
                _safeContentRoot.anchorMin = new Vector2(0.5f, 0.5f);
                _safeContentRoot.anchorMax = new Vector2(0.5f, 0.5f);
                _safeContentRoot.pivot = new Vector2(0.5f, 0.5f);
                _safeContentRoot.sizeDelta = new Vector2((float)ResolutionLayoutCalculator.ReferenceWidth, (float)ResolutionLayoutCalculator.ReferenceHeight);
                _safeContentRoot.anchoredPosition = Vector2.zero;
            }
        }
    }
}
