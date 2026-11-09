using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 2.5D 看板娘擬真動態控制器（依據 design/09_live2d.md §1 L0 規範與 design/14_ui_screens.md §2 規劃設計）。
    /// 為 Sakura 等靜態去背立繪賦予如同 Live2D 般的靈活生命力：
    /// 1. 胸腔自然呼吸起伏（Organic Breathing with Volume Preservation：Y 軸舒張時 X 軸微縮保持體積）
    /// 2. 身體重心微擺（Idle Balance Swaying：Z 軸微幅平滑搖曳）
    /// 3. 滑鼠／視線即時追蹤（Pointer Parallax LookAt：身軀與視線隨游標平滑轉向）
    /// 4. 點擊彈性物理反應（Interactive Physics Bounce & Squash/Stretch）
    /// 5. 全息底座動態自轉與呼吸微光（Hologram Pedestal Rotation & Pulsing Glow）
    /// 6. 對話氣泡微風懸浮（Speech Bubble Floating）
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyDynamicPortrait : MonoBehaviour
    {
        [Header("呼吸起伏 (Breathing)")]
        [SerializeField] private bool _enableBreathing = true;
        [SerializeField] private float _breathPeriod = 3.6f;
        [SerializeField] private float _breathScaleY = 0.016f;
        [SerializeField] private float _breathScaleX = 0.006f;
        [SerializeField] private float _breathOffsetY = 3.5f;

        [Header("重心微擺 (Idle Sway)")]
        [SerializeField] private bool _enableSway = true;
        [SerializeField] private float _swayPeriod = 4.8f;
        [SerializeField] private float _swayMaxAngle = 0.45f;

        [Header("游標視角跟隨 (Parallax LookAt)")]
        [SerializeField] private bool _enableLookAt = true;
        [SerializeField] private float _lookAtMaxAngleZ = 0.8f;
        [SerializeField] private float _lookAtMaxOffsetX = 12.0f;
        [SerializeField] private float _lookAtMaxOffsetY = 6.0f;
        [SerializeField] private float _lookAtSmoothSpeed = 4.5f;

        [Header("點擊彈性物理反應 (Tap Reaction)")]
        [SerializeField] private bool _enableTapBounce = true;
        [SerializeField] private float _tapSquashY = 0.05f;
        [SerializeField] private float _tapBounceHeight = 16.0f;
        [SerializeField] private float _tapBounceDuration = 0.70f;

        [Header("全息科技底座 (Holo Pedestal)")]
        [SerializeField] private RectTransform _pedestalRt;
        [SerializeField] private Image _pedestalImage;
        [SerializeField] private float _pedestalRotateSpeed = 10f;

        [Header("對話氣泡懸浮 (Speech Bubble)")]
        [SerializeField] private RectTransform _speechBubbleRt;

        // 內部基礎變數
        private RectTransform _characterRt;
        private Vector2 _basePosition;
        private Vector3 _baseScale = Vector3.one;
        private Vector2 _baseBubblePosition;
        private bool _isInitialized;

        // 運行時狀態
        private float _time;
        private float _tapTimer;
        private float _curLookAtAngle;
        private Vector2 _curLookAtOffset;
        private Vector2? _simulatedPointerNorm; // 供單元測試或特定輸入覆寫

        // 屬性暴露
        public bool EnableBreathing { get => _enableBreathing; set => _enableBreathing = value; }
        public bool EnableSway { get => _enableSway; set => _enableSway = value; }
        public bool EnableLookAt { get => _enableLookAt; set => _enableLookAt = value; }
        public bool EnableTapBounce { get => _enableTapBounce; set => _enableTapBounce = value; }
        public bool IsBouncing => _tapTimer > 0f;
        public float CurrentLookAtAngle => _curLookAtAngle;
        public Vector2 CurrentLookAtOffset => _curLookAtOffset;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void Initialize(RectTransform charRt, RectTransform pedestalRt = null, Image pedestalImg = null, RectTransform bubbleRt = null)
        {
            _characterRt = charRt ?? GetComponent<RectTransform>();
            _pedestalRt = pedestalRt;
            _pedestalImage = pedestalImg;
            _speechBubbleRt = bubbleRt;

            if (_characterRt != null)
            {
                _basePosition = _characterRt.anchoredPosition;
                _baseScale = _characterRt.localScale;
            }

            if (_speechBubbleRt != null)
            {
                _baseBubblePosition = _speechBubbleRt.anchoredPosition;
            }

            _isInitialized = true;
        }

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            if (_characterRt == null)
            {
                _characterRt = GetComponent<RectTransform>();
            }
            if (_characterRt != null)
            {
                _basePosition = _characterRt.anchoredPosition;
                _baseScale = _characterRt.localScale;
                _isInitialized = true;
            }
        }

        /// <summary>
        /// 觸發點擊歡快彈跳物理反應。
        /// </summary>
        public void TriggerTapReaction()
        {
            if (!_enableTapBounce) return;
            _tapTimer = _tapBounceDuration;
        }

        /// <summary>
        /// 設定模擬游標位置（歸一化坐標 -1..1），主要供測試或自訂視角使用。
        /// </summary>
        public void SetSimulatedPointer(Vector2? normalizedPos)
        {
            _simulatedPointerNorm = normalizedPos;
        }

        /// <summary>
        /// 手動推進時間（供單元測試與離線渲染驗證）。
        /// </summary>
        public void SimulateTick(float deltaTime)
        {
            UpdateMotion(deltaTime);
        }

        private void Update()
        {
            UpdateMotion(Time.deltaTime);
        }

        private void UpdateMotion(float dt)
        {
            if (!_isInitialized) EnsureInitialized();
            if (_characterRt == null) return;

            _time += dt;

            // 1. 呼吸計算 (Breathing Oscillation)
            float breathY = 0f;
            float scaleY = 1f;
            float scaleX = 1f;

            if (_enableBreathing && _breathPeriod > 0f)
            {
                float breathAngle = _time * (2f * Mathf.PI / _breathPeriod);
                float breathSin = Mathf.Sin(breathAngle);

                breathY = breathSin * _breathOffsetY;
                scaleY = 1f + breathSin * _breathScaleY;
                scaleX = 1f - breathSin * _breathScaleX; // 體積守恆微縮
            }

            // 2. 重心微擺 (Idle Swaying)
            float swayAngle = 0f;
            if (_enableSway && _swayPeriod > 0f)
            {
                float swayTimer = (_time + 0.8f) * (2f * Mathf.PI / _swayPeriod);
                swayAngle = Mathf.Sin(swayTimer) * _swayMaxAngle;
            }

            // 3. 游標視角跟隨 (Parallax LookAt)
            if (_enableLookAt)
            {
                Vector2 normPointer = GetNormalizedPointerPos();
                float targetAngle = normPointer.x * _lookAtMaxAngleZ;
                Vector2 targetOffset = new Vector2(
                    normPointer.x * _lookAtMaxOffsetX,
                    normPointer.y * _lookAtMaxOffsetY
                );

                float t = Mathf.Clamp01(dt * _lookAtSmoothSpeed);
                _curLookAtAngle = Mathf.Lerp(_curLookAtAngle, targetAngle, t);
                _curLookAtOffset = Vector2.Lerp(_curLookAtOffset, targetOffset, t);
            }
            else
            {
                _curLookAtAngle = Mathf.Lerp(_curLookAtAngle, 0f, dt * _lookAtSmoothSpeed);
                _curLookAtOffset = Vector2.Lerp(_curLookAtOffset, Vector2.zero, dt * _lookAtSmoothSpeed);
            }

            // 4. 點擊彈跳計算 (Tap Spring Reaction)
            float bounceOffsetY = 0f;
            float bounceScaleY = 0f;
            float bounceScaleX = 0f;

            if (_tapTimer > 0f)
            {
                _tapTimer -= dt;
                float progress = Mathf.Clamp01(1f - (_tapTimer / _tapBounceDuration));

                // 阻尼彈簧震盪 (Damped Spring Curve)
                float damp = Mathf.Exp(-progress * 3.8f);
                float springSin = Mathf.Sin(progress * Mathf.PI * 3f);

                bounceOffsetY = springSin * damp * _tapBounceHeight;
                bounceScaleY = -springSin * damp * _tapSquashY;
                bounceScaleX = -bounceScaleY * 0.5f;
            }

            // 5. 應用至看板娘立繪 RectTransform
            Vector2 finalPos = _basePosition + new Vector2(
                _curLookAtOffset.x,
                breathY + _curLookAtOffset.y + bounceOffsetY
            );
            _characterRt.anchoredPosition = finalPos;

            Vector3 finalScale = new Vector3(
                _baseScale.x * (scaleX + bounceScaleX),
                _baseScale.y * (scaleY + bounceScaleY),
                _baseScale.z
            );
            _characterRt.localScale = finalScale;

            _characterRt.localEulerAngles = new Vector3(0f, 0f, swayAngle + _curLookAtAngle);

            // 6. 全息底座動態旋轉與微光脈衝 (Hologram Pedestal Animation)
            if (_pedestalRt != null)
            {
                _pedestalRt.Rotate(0f, 0f, _pedestalRotateSpeed * dt);
            }

            if (_pedestalImage != null)
            {
                float pulseAlpha = 0.50f + 0.22f * Mathf.Sin(_time * 2.2f);
                Color c = _pedestalImage.color;
                _pedestalImage.color = new Color(c.r, c.g, c.b, pulseAlpha);
            }

            // 7. 對話氣泡溫柔懸浮 (Floating Speech Bubble)
            if (_speechBubbleRt != null && _speechBubbleRt.gameObject.activeInHierarchy)
            {
                float bubbleBob = Mathf.Sin(_time * 2.5f) * 3.0f;
                _speechBubbleRt.anchoredPosition = _baseBubblePosition + new Vector2(0f, bubbleBob);
            }
        }

        private Vector2 GetNormalizedPointerPos()
        {
            if (_simulatedPointerNorm.HasValue)
            {
                return _simulatedPointerNorm.Value;
            }

            // 實機以螢幕中心為 (0, 0)，左右 -1..1，上下 -1..1
            if (Screen.width > 0 && Screen.height > 0)
            {
                Vector3 mouse = Input.mousePosition;
                float nx = (mouse.x - Screen.width * 0.5f) / (Screen.width * 0.5f);
                float ny = (mouse.y - Screen.height * 0.5f) / (Screen.height * 0.5f);
                return new Vector2(Mathf.Clamp(nx, -1f, 1f), Mathf.Clamp(ny, -1f, 1f));
            }

            return Vector2.zero;
        }

        private void OnDisable()
        {
            ResetToBase();
        }

        public void ResetToBase()
        {
            if (_characterRt != null)
            {
                _characterRt.anchoredPosition = _basePosition;
                _characterRt.localScale = _baseScale;
                _characterRt.localEulerAngles = Vector3.zero;
            }
            if (_speechBubbleRt != null)
            {
                _speechBubbleRt.anchoredPosition = _baseBubblePosition;
            }
            _curLookAtAngle = 0f;
            _curLookAtOffset = Vector2.zero;
            _tapTimer = 0f;
        }
    }
}
