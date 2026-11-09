#if UNITY_2021_1_OR_NEWER
using System;
using Game.Core;
using Game.Core.AgeGate;
using Game.Core.Audio;
using Game.Core.Localization;
using Game.Core.Settings;
using Game.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 系統設定與年齡認證彈窗畫面 (OPT-7 / ARCH-04)。
    /// 支援音量調整 (AudioService)、多語言切換 (ILocalizationService)、
    /// 以及切換至 R18 內容時之 18+ 年齡門檻防禦性確認 (AgeGateService)。
    /// </summary>
    public sealed class SettingsModalView : UnityScreenBase
    {
        private GameCore _core;
        private AudioService _audio;
        private ILocalizationService _loc;
        private AgeGateState _ageGate;
        private SettingsService _settingsService;

        // 音量
        private float _bgmVolume = 1.0f;
        private float _seVolume = 1.0f;
        private float _voiceVolume = 1.0f;

        // 語言與分級
        private string _selectedLocale = "zh-TW";
        private Rating _selectedRating = Rating.G;

        // 18+ 年齡確認彈窗
        private GameObject _ageGateModal;
        private Button _ageGateConfirmBtn;
        private Button _ageGateCancelBtn;
        private Text _titleLabel;

        // 檢核與測試屬性 (G3 品質閘門)
        public float BgmVolume => _bgmVolume;
        public float SeVolume => _seVolume;
        public float VoiceVolume => _voiceVolume;
        public string SelectedLocale => _selectedLocale;
        public Rating SelectedRating => _selectedRating;
        public bool IsAgeGateModalOpen => _ageGateModal != null && _ageGateModal.activeSelf;
        public string TitleText => _titleLabel != null ? _titleLabel.text : string.Empty;

        public override string ScreenId => "SettingsModalUI";

        public void Initialize(
            GameCore core,
            AudioService audio = null,
            ILocalizationService loc = null,
            AgeGateState ageGate = null,
            SettingsService settingsService = null)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _audio = audio;
            _loc = loc;
            _ageGate = ageGate ?? new AgeGateState();
            _settingsService = settingsService;

            if (_core.Player?.Settings != null)
            {
                _bgmVolume = _core.Player.Settings.Bgm;
                _seVolume = _core.Player.Settings.Se;
                _voiceVolume = _core.Player.Settings.Voice;
            }

            if (_core.Player?.Profile != null)
            {
                _selectedLocale = _core.Player.Profile.Locale ?? "zh-TW";
                _selectedRating = _core.Player.Profile.RatingOptIn;
            }

            EnsureUiHierarchy();
        }

        public void InitializeForTesting(
            GameCore core,
            AudioService audio = null,
            ILocalizationService loc = null,
            AgeGateState ageGate = null,
            SettingsService settingsService = null)
        {
            Initialize(core, audio, loc, ageGate, settingsService);
        }

        private void Awake()
        {
            EnsureUiHierarchy();
        }

        private void EnsureUiHierarchy()
        {
            if (_ageGateModal != null) return;

            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                gameObject.AddComponent<CanvasScaler>();
                gameObject.AddComponent<GraphicRaycaster>();
            }
            _canvas = canvas;

            RectTransform root = GetComponent<RectTransform>();
            if (root == null) root = gameObject.AddComponent<RectTransform>();
            ProceduralUiKit.StretchFull(root);

            // 背景遮罩
            GameObject bgGo = ProceduralUiKit.CreateUiObject("ModalMask", transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(bgRt);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.75f);

            // 設定面板主體
            GameObject boxGo = ProceduralUiKit.CreateUiObject("SettingsBox", transform);
            RectTransform bRt = boxGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.20f, 0.15f);
            bRt.anchorMax = new Vector2(0.80f, 0.85f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;

            Image bImg = boxGo.AddComponent<Image>();
            bImg.color = new Color(0.08f, 0.12f, 0.18f, 0.98f);

            // 標題
            _titleLabel = CreateLabel(bRt, "Title", "SYSTEM SETTINGS // 系統設定", new Vector2(0.05f, 0.88f), new Vector2(0.70f, 0.96f), 18, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleLeft);

            // 關閉按鈕
            GameObject closeGo = ProceduralUiKit.CreateUiObject("CloseBtn", bRt);
            RectTransform cRt = closeGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.85f, 0.88f);
            cRt.anchorMax = new Vector2(0.96f, 0.96f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            Image cImg = closeGo.AddComponent<Image>();
            cImg.color = new Color(0.7f, 0.2f, 0.2f, 1f);
            Button cBtn = closeGo.AddComponent<Button>();
            cBtn.onClick.AddListener(() => GameServices.Navigator?.Pop());
            CreateLabel(cRt, "Lbl", "關閉", Vector2.zero, Vector2.one, 13, Color.white, TextAnchor.MiddleCenter);

            // 語言設定欄 (四語系: zh-TW, en, ja, zh-CN)
            BuildLanguageSection(bRt);

            // 18+ 年齡確認彈窗
            BuildAgeGateModal();
        }

        private void BuildLanguageSection(RectTransform parent)
        {
            CreateLabel(parent, "LangTitle", "LANGUAGE // 語言設定", new Vector2(0.05f, 0.70f), new Vector2(0.40f, 0.78f), 15, Color.white, TextAnchor.MiddleLeft);

            string[] locales = new[] { "zh-TW", "en", "ja", "zh-CN" };
            string[] labels = new[] { "繁體中文", "English", "日本語", "简体中文" };

            for (int i = 0; i < locales.Length; i++)
            {
                string locKey = locales[i];
                string lblText = labels[i];
                float xMin = 0.05f + i * 0.23f;
                float xMax = xMin + 0.21f;

                GameObject btnGo = ProceduralUiKit.CreateUiObject("Btn_Lang_" + locKey, parent);
                RectTransform rt = btnGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(xMin, 0.58f);
                rt.anchorMax = new Vector2(xMax, 0.68f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                Image img = btnGo.AddComponent<Image>();
                img.color = new Color(0.18f, 0.24f, 0.35f, 1f);

                Button btn = btnGo.AddComponent<Button>();
                btn.onClick.AddListener(() => SelectLocale(locKey));

                CreateLabel(rt, "Lbl", lblText, Vector2.zero, Vector2.one, 13, Color.white, TextAnchor.MiddleCenter);
            }
        }

        private void BuildAgeGateModal()
        {
            _ageGateModal = ProceduralUiKit.CreateUiObject("AgeGateModal", transform);
            RectTransform mRt = _ageGateModal.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(mRt);
            Image mBg = _ageGateModal.AddComponent<Image>();
            mBg.color = new Color(0f, 0f, 0f, 0.92f);

            GameObject boxGo = ProceduralUiKit.CreateUiObject("AgeBox", _ageGateModal.transform);
            RectTransform bRt = boxGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.25f, 0.30f);
            bRt.anchorMax = new Vector2(0.75f, 0.70f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            Image bImg = boxGo.AddComponent<Image>();
            bImg.color = new Color(0.14f, 0.08f, 0.10f, 0.98f);

            CreateLabel(bRt, "Title", "⚠️ 年齡確認 (Age Verification)", new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.90f), 18, new Color(1f, 0.3f, 0.3f), TextAnchor.MiddleCenter);
            CreateLabel(bRt, "Notice", "本遊戲切換至 R18 分級包含成人向演出內容。\n您是否已年滿 18 歲（或當地法定成年年齡）？", new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.70f), 14, Color.white, TextAnchor.MiddleCenter);

            // 確認按鈕
            GameObject confGo = ProceduralUiKit.CreateUiObject("ConfirmBtn", bRt);
            RectTransform cfRt = confGo.GetComponent<RectTransform>();
            cfRt.anchorMin = new Vector2(0.12f, 0.10f);
            cfRt.anchorMax = new Vector2(0.46f, 0.28f);
            cfRt.offsetMin = Vector2.zero;
            cfRt.offsetMax = Vector2.zero;
            Image cfImg = confGo.AddComponent<Image>();
            cfImg.color = new Color(0.85f, 0.25f, 0.25f, 1f);
            _ageGateConfirmBtn = confGo.AddComponent<Button>();
            _ageGateConfirmBtn.onClick.AddListener(ConfirmAgeGate);
            CreateLabel(cfRt, "Lbl", "我已滿 18 歲", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            // 取消按鈕
            GameObject canGo = ProceduralUiKit.CreateUiObject("CancelBtn", bRt);
            RectTransform cnRt = canGo.GetComponent<RectTransform>();
            cnRt.anchorMin = new Vector2(0.54f, 0.10f);
            cnRt.anchorMax = new Vector2(0.88f, 0.28f);
            cnRt.offsetMin = Vector2.zero;
            cnRt.offsetMax = Vector2.zero;
            Image cnImg = canGo.AddComponent<Image>();
            cnImg.color = new Color(0.25f, 0.30f, 0.40f, 1f);
            _ageGateCancelBtn = canGo.AddComponent<Button>();
            _ageGateCancelBtn.onClick.AddListener(CancelAgeGate);
            CreateLabel(cnRt, "Lbl", "未滿 18 歲 (返回)", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            _ageGateModal.SetActive(false);
        }

        // =========================================================================================
        // 操作與設定介面
        // =========================================================================================

        public void SetBgmVolume(float val)
        {
            _bgmVolume = Mathf.Clamp01(val);
            if (_audio != null) _audio.SetVolumes(_audio.MasterVolume, _bgmVolume, _audio.SeVolume, _audio.VoiceVolume);
            if (_core?.Player?.Settings != null) _core.Player.Settings.Bgm = _bgmVolume;
        }

        public void SetVoiceVolume(float val)
        {
            _voiceVolume = Mathf.Clamp01(val);
            if (_audio != null) _audio.SetVolumes(_audio.MasterVolume, _audio.BgmVolume, _audio.SeVolume, _voiceVolume);
            if (_core?.Player?.Settings != null) _core.Player.Settings.Voice = _voiceVolume;
        }

        public void SelectLocale(string loc)
        {
            if (string.IsNullOrEmpty(loc)) return;
            _selectedLocale = loc;
            if (_loc != null)
            {
                _loc.SetLocale(loc);
                if (_titleLabel != null)
                {
                    _titleLabel.text = _loc.Get("ui.settings.title");
                }
            }
            if (_core?.Player?.Profile != null) _core.Player.Profile.Locale = loc;
        }

        public void SelectRating(Rating rating)
        {
            if (rating > Rating.G && !_ageGate.IsVerified)
            {
                // 未年齡認證，彈出確認視窗，暫不變更分級
                if (_ageGateModal != null) _ageGateModal.SetActive(true);
                return;
            }

            _selectedRating = rating;
            if (_core?.Player?.Profile != null)
            {
                _core.Player.Profile.RatingOptIn = rating;
            }
        }

        public void ConfirmAgeGate()
        {
            AgeGateService.Verify(_ageGate, Rating.R18, _core?.Clock);
            _selectedRating = Rating.R18;
            if (_core?.Player?.Profile != null)
            {
                _core.Player.Profile.RatingOptIn = Rating.R18;
                _core.Player.Profile.AgeConfirmedAtUtc = GameCore.Iso(_core.Clock.UtcNow);
            }
            if (_ageGateModal != null) _ageGateModal.SetActive(false);
        }

        public void CancelAgeGate()
        {
            _selectedRating = Rating.G;
            if (_core?.Player?.Profile != null)
            {
                _core.Player.Profile.RatingOptIn = Rating.G;
            }
            if (_ageGateModal != null) _ageGateModal.SetActive(false);
        }

        public override bool OnBackRequested()
        {
            if (IsAgeGateModalOpen)
            {
                CancelAgeGate();
                return true;
            }
            return false;
        }

        private static Text CreateLabel(Transform parent, string name, string initialText, Vector2 aMin, Vector2 aMax, int fontSize, Color color, TextAnchor align)
        {
            GameObject go = ProceduralUiKit.CreateUiObject(name, parent);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Text txt = go.AddComponent<Text>();
            txt.text = initialText;
            txt.font = ProceduralUiKit.GetDefaultFont();
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.raycastTarget = false;
            return txt;
        }
    }
}
#endif
