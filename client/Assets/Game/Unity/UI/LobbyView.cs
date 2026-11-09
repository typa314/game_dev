using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.Events;
using Game.Domain.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 正式 UGUI 大廳首頁畫面（依據 design/14_ui_screens.md §2 規劃設計）。
    /// 二次元戰術指揮中心風格：高畫質角色立繪、動態時段光影背景、全息地台、
    /// 頂部資源膠囊、二次元戰術導航卡牌、氣泡對話框以及烈焰出擊大按鈕。
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyView : UnityScreenBase, ILobbyView
    {
        public override string ScreenId => "Screen_Lobby";
        [Header("UI 元件參照")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasScaler _canvasScaler;
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Text _sceneNameText;
        [SerializeField] private Image _characterPortraitImage;
        [SerializeField] private Button _characterClickArea;
        [SerializeField] private GameObject _speechBubbleRoot;
        [SerializeField] private Text _speechBubbleText;
        [SerializeField] private CanvasGroup _speechBubbleCanvasGroup;
        [SerializeField] private LobbyDynamicPortrait _dynamicPortrait;
        [SerializeField] private Live2DPortraitView _live2DPortrait;

        public LobbyDynamicPortrait DynamicPortrait => _dynamicPortrait;
        public Live2DPortraitView Live2DPortrait => _live2DPortrait;

        public void BindLive2DPortrait(Live2DPortraitView view)
        {
            _live2DPortrait = view;
        }

        [Header("頂部資訊列")]
        [SerializeField] private Text _playerNameText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _gemText;
        [SerializeField] private Text _staminaText;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _toggleDebugButton;

        [Header("右側主導航列")]
        [SerializeField] private Button _btnStory;
        [SerializeField] private Button _btnEncounter;
        [SerializeField] private Button _btnGacha;
        [SerializeField] private Button _btnCharacters;
        [SerializeField] private Button _btnWardrobe;
        [SerializeField] private Button _btnAchievements;

        [Header("右下角出擊主按鈕")]
        [SerializeField] private Button _btnBattle;

        [Header("Toast 浮動提示")]
        [SerializeField] private GameObject _toastRoot;
        [SerializeField] private Text _toastText;
        [SerializeField] private CanvasGroup _toastCanvasGroup;

        // Presenter
        private LobbyPresenter _presenter;
        public LobbyPresenter Presenter => _presenter;

        // M2-4 / M2-7 測試與狀態暴露屬性
        public TimeOfDay? TimeOfDayOverride { get; private set; }
        public Color CurrentBackgroundTint => _backgroundImage != null ? _backgroundImage.color : Color.white;
        public string BattleButtonText
        {
            get
            {
                if (_btnBattle == null) return string.Empty;
                var tr = _btnBattle.transform.Find("BattleText");
                return tr != null && tr.GetComponent<Text>() != null ? tr.GetComponent<Text>().text : string.Empty;
            }
        }

        // 狀態與計時
        private Coroutine _speechCoroutine;
        private Coroutine _toastCoroutine;
        private bool _isInitialized;

        public override bool IsVisible
        {
            get => _canvas != null && _canvas.enabled;
            set
            {
                if (_canvas != null) _canvas.enabled = value;
            }
        }

        private void Awake()
        {
            EnsureUIHierarchy();
        }

        private void Start()
        {
            InitPresenterIfReady();
        }

        private void OnEnable()
        {
            RegisterBusEvents();
            RefreshPlayerHeader();
        }

        private void OnDisable()
        {
            UnregisterBusEvents();
        }

        public void Initialize(GameCore core)
        {
            if (core == null) return;
            _presenter = new LobbyPresenter(core);
            _presenter.AttachView(this);
            _presenter.Initialize();
            _isInitialized = true;
            RegisterBusEvents();
            RefreshPlayerHeader();
            ApplyLocalizedTexts();
        }

        private void InitPresenterIfReady()
        {
            if (_isInitialized) return;
            if (GameServices.IsReady && GameServices.Core != null)
            {
                Initialize(GameServices.Core);
            }
        }

        public override void OnPush(object args)
        {
            base.OnPush(args);
            EnsureUIHierarchy();
            InitPresenterIfReady();
            RefreshPlayerHeader();
        }

        public override void OnPause()
        {
            base.OnPause();
            gameObject.SetActive(false);
        }

        public override void OnResume()
        {
            base.OnResume();
            gameObject.SetActive(true);
            RefreshPlayerHeader();
            if (_presenter != null)
            {
                _presenter.Refresh();
            }
        }

        public override bool OnBackRequested()
        {
            if (_speechBubbleRoot != null && _speechBubbleRoot.activeSelf)
            {
                _speechBubbleRoot.SetActive(false);
                return true;
            }
            if (_toastRoot != null && _toastRoot.activeSelf)
            {
                _toastRoot.SetActive(false);
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (!_isInitialized && GameServices.IsReady && GameServices.Core != null)
            {
                Initialize(GameServices.Core);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (GameServices.Navigator != null && GameServices.Navigator.CurrentScreen == this)
                {
                    GameServices.Navigator.HandleBackRequest();
                }
                else
                {
                    OnBackRequested();
                }
            }

            if (_live2DPortrait != null && _live2DPortrait.enabled && _live2DPortrait.gameObject.activeInHierarchy)
            {
                _live2DPortrait.LookAt(Input.mousePosition);
                _live2DPortrait.StepModelUpdate(Time.deltaTime);
            }
        }

        #region ILobbyView 實作

        public void SetTimeOfDayOverride(TimeOfDay? tod)
        {
            TimeOfDayOverride = tod;
            if (_presenter != null)
            {
                _presenter.Refresh();
            }
            else
            {
                SetBackground("scene_lobby_default", tod ?? TimeOfDay.Morning);
            }
        }

        public void SetBackground(string sceneId, TimeOfDay timeOfDay)
        {
            if (_backgroundImage == null) return;
            if (TimeOfDayOverride.HasValue)
            {
                timeOfDay = TimeOfDayOverride.Value;
            }

            // 時段燈光調色乘數（design/10 §1）
            Color tint;
            string todName;
            switch (timeOfDay)
            {
                case TimeOfDay.Morning:
                    tint = new Color(1f, 0.97f, 0.90f, 1f); // 晨曦暖金
                    todName = "早晨";
                    break;
                case TimeOfDay.Afternoon:
                    tint = Color.white; // 午後明亮
                    todName = "日間";
                    break;
                case TimeOfDay.Evening:
                    tint = new Color(1f, 0.88f, 0.82f, 1f); // 夕陽橘紅
                    todName = "夕暮";
                    break;
                case TimeOfDay.Night:
                default:
                    tint = new Color(0.78f, 0.82f, 1f, 1f); // 靜夜深藍
                    todName = "夜間";
                    break;
            }

            _backgroundImage.color = tint;

            if (_sceneNameText != null)
            {
                string locScene = sceneId switch
                {
                    "scene_lobby_default" => "作戰指揮部 · 學院核心",
                    "scene_cafe" => "中央大道 · 街角咖啡廳",
                    "scene_park" => "綠蔭中央公園",
                    _ => sceneId
                };
                _sceneNameText.text = $"◈ {locScene} ({todName})";
            }
        }

        public void SetCharacterPortrait(string artKey)
        {
            if (_characterPortraitImage == null) return;

            // 優先嘗試從 StreamingAssets 載入立繪圖
            Sprite sp = TryLoadCharacterSprite(artKey);
            if (sp != null)
            {
                _characterPortraitImage.sprite = sp;
                _characterPortraitImage.color = Color.white;
                _characterPortraitImage.preserveAspect = true;

                // 隱藏佔位文字
                Transform ph = _characterPortraitImage.transform.Find("PlaceholderText");
                if (ph != null) ph.gameObject.SetActive(false);
            }
        }

        public void SetCharacterPortrait(string characterId, int stage)
        {
            SetCharacterPortrait($"{characterId}_s{stage}");
        }

        public void SetGreeting(string greetingText)
        {
            ShowSpeechBubble(greetingText);
        }

        public void ShowError(string message)
        {
            ShowToast($"[提示] {message}");
        }

        #endregion

        #region 互動與操作事件

        private int _tapVoiceIndex = 0;

        public void OnCharacterTapped()
        {
            if (_live2DPortrait != null && _live2DPortrait.enabled && _live2DPortrait.gameObject.activeInHierarchy)
            {
                string hitArea = _tapVoiceIndex % 2 == 0 ? "Head" : "Body";
                _live2DPortrait.OnTouched(hitArea);
            }
            else if (_dynamicPortrait != null)
            {
                _dynamicPortrait.TriggerTapReaction();
            }

            string voiceKey = _tapVoiceIndex % 2 == 0 ? "char18_sakura_miko/tap_1" : "char18_sakura_miko/tap_2";
            _tapVoiceIndex++;

            if (GameServices.Audio != null)
            {
                GameServices.Audio.PlayVoice(voiceKey);
            }

            if (_presenter != null)
            {
                _presenter.OnCharacterClicked();
            }
            else
            {
                string text = _tapVoiceIndex % 2 == 1
                    ? "「ふふっ、指揮官の手、とっても温かいです。\n今日も一緒に頑張りましょうね！」"
                    : "「あっ、指揮官！この桜のお守り、受け取ってください。\n肌身離さず持っていてほしいな…」";
                ShowSpeechBubble(text);
            }
        }

        public void RefreshPlayerHeader()
        {
            if (!GameServices.IsReady || GameServices.Core?.Player == null)
            {
                if (_playerNameText != null) _playerNameText.text = "指揮官 (Commander) Lv.1";
                if (_goldText != null) _goldText.text = "5,000";
                if (_gemText != null) _gemText.text = "3,000";
                if (_staminaText != null) _staminaText.text = "5/5";
                return;
            }

            PlayerState player = GameServices.Core.Player;
            long gold = player.ItemCount("gold");
            long gem = player.ItemCount("gem");
            int stamina = player.Encounter?.Stamina ?? 5;
            int maxStamina = GameServices.Core.Master?.Config?.Encounter?.StaminaMax ?? 5;

            if (_playerNameText != null)
                _playerNameText.text = "指揮官 (Commander)";
            if (_goldText != null)
                _goldText.text = $"{gold:N0}";
            if (_gemText != null)
                _gemText.text = $"{gem:N0}";
            if (_staminaText != null)
                _staminaText.text = $"{stamina}/{maxStamina}";

            // OPT-8: 戰役榮譽按鈕紅點徽章動態更新
            if (_btnAchievements != null)
            {
                Transform redDotTr = _btnAchievements.transform.Find("RedDotBadge");
                if (redDotTr != null)
                {
                    bool hasRedDot = false;
                    if (GameServices.IsReady && GameServices.RedDots != null)
                    {
                        hasRedDot = GameServices.RedDots.HasClaimableAchievements();
                    }
                    else if (GameServices.Core?.Player != null)
                    {
                        foreach (var kvp in GameServices.Core.Player.Achievements)
                        {
                            if (!string.IsNullOrEmpty(kvp.Value.UnlockedAtUtc) && !kvp.Value.Claimed)
                            {
                                hasRedDot = true;
                                break;
                            }
                        }
                    }
                    redDotTr.gameObject.SetActive(hasRedDot);
                }
            }
        }

        private void RegisterBusEvents()
        {
            if (GameServices.Localization != null)
            {
                GameServices.Localization.LocaleChanged -= OnLocaleChanged;
                GameServices.Localization.LocaleChanged += OnLocaleChanged;
            }
            if (!GameServices.IsReady || GameServices.Core?.Bus == null) return;
            GameServices.Core.Bus.Subscribe(OnDomainEventChanged);
        }

        private void UnregisterBusEvents()
        {
            if (GameServices.Localization != null)
            {
                GameServices.Localization.LocaleChanged -= OnLocaleChanged;
            }
            if (!GameServices.IsReady || GameServices.Core?.Bus == null) return;
            GameServices.Core.Bus.Unsubscribe(OnDomainEventChanged);
        }

        private void OnLocaleChanged(string newLocale)
        {
            ApplyLocalizedTexts();
        }

        public void ApplyLocalizedTexts()
        {
            var loc = GameServices.Localization;
            if (loc == null) return;

            UpdateNavButtonTitle(_btnStory, loc.Get("ui.lobby.story"));
            UpdateNavButtonTitle(_btnEncounter, loc.Get("ui.lobby.encounter"));
            UpdateNavButtonTitle(_btnGacha, loc.Get("ui.lobby.gacha"));
            UpdateNavButtonTitle(_btnCharacters, loc.Get("ui.lobby.roster"));
            UpdateNavButtonTitle(_btnWardrobe, loc.Get("ui.lobby.wardrobe"));
            UpdateNavButtonTitle(_btnAchievements, loc.Get("ui.lobby.achievement"));

            if (_btnBattle != null)
            {
                var tr = _btnBattle.transform.Find("BattleText");
                if (tr != null && tr.GetComponent<Text>() != null)
                {
                    tr.GetComponent<Text>().text = loc.Get("ui.lobby.battle");
                }
            }
        }

        private static void UpdateNavButtonTitle(Button btn, string text)
        {
            if (btn == null || string.IsNullOrEmpty(text)) return;
            var tr = btn.transform.Find("Title");
            if (tr != null && tr.GetComponent<Text>() != null)
            {
                tr.GetComponent<Text>().text = text;
            }
        }

        private void OnDomainEventChanged(DomainEvent e)
        {
            RefreshPlayerHeader();
            if (_presenter != null)
            {
                _presenter.Refresh();
            }
        }

        public void ShowSpeechBubble(string text, float duration = 4.5f)
        {
            if (_speechBubbleRoot == null || _speechBubbleText == null) return;

            string displayText = text;
            if (displayText.Contains("！各小隊"))
            {
                displayText = displayText.Replace("！各小隊", "！\n各小隊");
            }

            _speechBubbleText.text = displayText;
            _speechBubbleRoot.SetActive(true);
            if (_speechBubbleCanvasGroup != null) _speechBubbleCanvasGroup.alpha = 1f;

            if (Application.isPlaying)
            {
                if (_speechCoroutine != null) StopCoroutine(_speechCoroutine);
                _speechCoroutine = StartCoroutine(DoSpeechBubble(displayText, duration));
            }
        }

        private IEnumerator DoSpeechBubble(string text, float duration)
        {
            _speechBubbleText.text = text;
            _speechBubbleRoot.SetActive(true);
            if (_speechBubbleCanvasGroup != null) _speechBubbleCanvasGroup.alpha = 1f;

            yield return new WaitForSeconds(duration);

            if (_speechBubbleCanvasGroup != null)
            {
                float t = 1f;
                while (t > 0f)
                {
                    t -= Time.deltaTime * 3f;
                    _speechBubbleCanvasGroup.alpha = Mathf.Clamp01(t);
                    yield return null;
                }
            }
            _speechBubbleRoot.SetActive(false);
        }

        public void ShowToast(string message, float duration = 2.5f)
        {
            if (_toastRoot == null || _toastText == null) return;

            if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
            _toastCoroutine = StartCoroutine(DoToast(message, duration));
        }

        private IEnumerator DoToast(string message, float duration)
        {
            _toastText.text = message;
            _toastRoot.SetActive(true);
            if (_toastCanvasGroup != null) _toastCanvasGroup.alpha = 1f;

            yield return new WaitForSeconds(duration);

            if (_toastCanvasGroup != null)
            {
                float t = 1f;
                while (t > 0f)
                {
                    t -= Time.deltaTime * 3f;
                    _toastCanvasGroup.alpha = Mathf.Clamp01(t);
                    yield return null;
                }
            }
            _toastRoot.SetActive(false);
        }

        #endregion

        #region 按鈕導航事件

        private void OnClickStory()
        {
            OpenStoryHub();
        }

        public void OpenStoryHub()
        {
            var hub = FindAnyObjectByType<StoryHubView>(FindObjectsInactive.Include);
            if (hub == null)
            {
                var go = new GameObject("StoryHubUI");
                hub = go.AddComponent<StoryHubView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                if (GameServices.Core != null)
                {
                    hub.Initialize(GameServices.Core);
                }
                GameServices.Navigator.Push(hub);
            }
        }

        private void OnClickEncounter()
        {
            OpenEncounter();
        }

        public void OpenEncounter()
        {
            var enc = FindAnyObjectByType<EncounterView>(FindObjectsInactive.Include);
            if (enc == null)
            {
                var go = new GameObject("EncounterUI");
                enc = go.AddComponent<EncounterView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                if (GameServices.Core != null)
                {
                    enc.Initialize(GameServices.Core);
                }
                GameServices.Navigator.Push(enc);
            }
        }

         private void OnClickGacha()
        {
            OpenGacha();
        }

        public void OpenGacha()
        {
            var gacha = FindAnyObjectByType<GachaView>(FindObjectsInactive.Include);
            if (gacha == null)
            {
                var gachaGo = new GameObject("GachaUI");
                gacha = gachaGo.AddComponent<GachaView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                GameServices.Navigator.Push(gacha, this);
            }
            else
            {
                gacha.Open(this);
                gameObject.SetActive(false);
            }
        }

        private void OnClickCharacters()
        {
            OpenCharacterRoster();
        }

        public void OpenCharacterRoster(string charId = null)
        {
            var roster = FindAnyObjectByType<CharacterRosterView>(FindObjectsInactive.Include);
            if (roster == null)
            {
                var go = new GameObject("CharacterRosterUI");
                roster = go.AddComponent<CharacterRosterView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                GameServices.Navigator.Push(roster, charId);
            }
            else
            {
                roster.OnPush(charId);
                gameObject.SetActive(false);
            }
        }

        private void OnClickWardrobe()
        {
            OpenWardrobe();
        }

        public void OpenWardrobe(string charId = null)
        {
            var wardrobe = FindAnyObjectByType<WardrobeView>(FindObjectsInactive.Include);
            if (wardrobe == null)
            {
                var go = new GameObject("WardrobeUI");
                wardrobe = go.AddComponent<WardrobeView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                GameServices.Navigator.Push(wardrobe, charId);
            }
            else
            {
                wardrobe.OnPush(charId);
                gameObject.SetActive(false);
            }
        }

        private void OnClickAchievements()
        {
            OpenAchievements();
        }

        public void OpenAchievements()
        {
            var ach = FindAnyObjectByType<AchievementView>(FindObjectsInactive.Include);
            if (ach == null)
            {
                var go = new GameObject("AchievementUI");
                ach = go.AddComponent<AchievementView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                if (GameServices.Core != null)
                {
                    ach.Initialize(GameServices.Core);
                }
                GameServices.Navigator.Push(ach);
            }
        }

        private void OnClickBattle()
        {
            OpenStageSelect();
        }

        public void OpenStageSelect()
        {
            var stageSelect = FindAnyObjectByType<StageSelectView>(FindObjectsInactive.Include);
            if (stageSelect == null)
            {
                var go = new GameObject("StageSelectUI");
                stageSelect = go.AddComponent<StageSelectView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                GameServices.Navigator.Push(stageSelect, this);
            }
            else
            {
                stageSelect.Open(this);
                gameObject.SetActive(false);
            }
        }

        private void OnClickSettings()
        {
            OpenSettings();
        }

        public void OpenSettings()
        {
            var setView = FindAnyObjectByType<SettingsModalView>(FindObjectsInactive.Include);
            if (setView == null)
            {
                var go = new GameObject("SettingsModalUI");
                setView = go.AddComponent<SettingsModalView>();
            }
            if (GameServices.Navigator != null)
            {
                if (GameServices.Navigator.CurrentScreen == null)
                {
                    GameServices.Navigator.Push(this);
                }
                if (GameServices.Core != null)
                {
                    setView.Initialize(GameServices.Core, GameServices.Audio, GameServices.Localization, GameServices.AgeGate, GameServices.Settings);
                }
                GameServices.Navigator.Push(setView);
            }
        }

        private void OnToggleDebugMenu()
        {
            DebugMenu dm = FindAnyObjectByType<DebugMenu>();
            if (dm != null)
            {
                dm.enabled = !dm.enabled;
                ShowToast(dm.enabled ? "已啟用除錯控制面板" : "已隱藏除錯控制面板");
            }
            else
            {
                ShowToast("未找到除錯控制面板組件");
            }
        }

        #endregion

        #region 自動構建 UGUI 階層 (二次元戰術風格美化昇華)

        public void EnsureUIHierarchy()
        {
            if (_canvas != null && _backgroundImage != null && _characterPortraitImage != null)
                return;

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 1. Canvas 設置 (1920x1080 基準解析度)
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 0;

            _canvasScaler = GetComponent<CanvasScaler>();
            if (_canvasScaler == null) _canvasScaler = gameObject.AddComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920, 1080);
            _canvasScaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            // 掛載解析度自適應組件 (F10)
            if (GetComponent<ResolutionAdapter>() == null)
                gameObject.AddComponent<ResolutionAdapter>();

            // 確保 EventSystem 存在
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // 2. Background Panel (背景底層 + 戰術遮罩)
            var bgGo = CreateUIObject("BackgroundPanel", transform);
            StretchFull(bgGo.GetComponent<RectTransform>());
            _backgroundImage = bgGo.AddComponent<Image>();

            // 嘗試載入背景圖
            Sprite bgSprite = TryLoadBackgroundSprite();
            if (bgSprite != null)
            {
                _backgroundImage.sprite = bgSprite;
                _backgroundImage.color = Color.white;
            }
            else
            {
                _backgroundImage.sprite = CreateGradientSprite(64, 64, new Color(0.12f, 0.15f, 0.22f), new Color(0.06f, 0.08f, 0.12f));
                _backgroundImage.color = new Color(1f, 0.97f, 0.90f, 1f); // 晨曦暖金
            }

            // 戰術遮罩層（加強文字對比度）
            var bgMaskGo = CreateUIObject("BgVignetteMask", bgGo.transform);
            StretchFull(bgMaskGo.GetComponent<RectTransform>());
            var bgMask = bgMaskGo.AddComponent<Image>();
            bgMask.color = new Color(0.02f, 0.04f, 0.08f, 0.35f);

            // 場景標籤 (圓角半透明膠囊標籤)
            var sceneNameGo = CreateUIObject("SceneNameLabel", bgGo.transform);
            var sceneNameRt = sceneNameGo.GetComponent<RectTransform>();
            sceneNameRt.anchorMin = new Vector2(0f, 1f);
            sceneNameRt.anchorMax = new Vector2(0f, 1f);
            sceneNameRt.pivot = new Vector2(0f, 1f);
            sceneNameRt.anchoredPosition = new Vector2(28, -88);
            sceneNameRt.sizeDelta = new Vector2(340, 38);

            var sceneTagImg = sceneNameGo.AddComponent<Image>();
            sceneTagImg.sprite = CreatePillSprite(340, 38, new Color(0.08f, 0.12f, 0.20f, 0.88f), new Color(0f, 0.9f, 1f, 0.65f));

            var sceneTxtGo = CreateUIObject("Text", sceneNameGo.transform);
            StretchFull(sceneTxtGo.GetComponent<RectTransform>(), 4);
            _sceneNameText = sceneTxtGo.AddComponent<Text>();
            _sceneNameText.font = defaultFont;
            _sceneNameText.fontSize = 15;
            _sceneNameText.alignment = TextAnchor.MiddleCenter;
            _sceneNameText.color = new Color(0.88f, 0.94f, 1f, 1f);
            _sceneNameText.text = "◈ 作戰指揮部 · 學院核心 (早晨)";

            // 2.8 全息科技底座 (Hologram Pedestal Ring) - 放在看板娘身後渲染
            var pedestalGo = CreateUIObject("HoloPedestal", transform);
            var pedRt = pedestalGo.GetComponent<RectTransform>();
            pedRt.anchorMin = new Vector2(0.42f, 0f);
            pedRt.anchorMax = new Vector2(0.42f, 0f);
            pedRt.pivot = new Vector2(0.5f, 0f);
            pedRt.anchoredPosition = new Vector2(0, -15);
            pedRt.sizeDelta = new Vector2(760, 160);
            var pedImg = pedestalGo.AddComponent<Image>();
            pedImg.sprite = CreateHoloRingSprite(760, 160, new Color(0f, 0.85f, 1f, 0.55f));
            pedImg.color = Color.white;

            // 3. Character Area (看板娘全身立繪區)
            var charGo = CreateUIObject("CharacterStandArea", transform);
            var charRt = charGo.GetComponent<RectTransform>();
            charRt.anchorMin = new Vector2(0.42f, 0f);
            charRt.anchorMax = new Vector2(0.42f, 0f);
            charRt.pivot = new Vector2(0.5f, 0f);
            charRt.anchoredPosition = new Vector2(0, -50);
            charRt.sizeDelta = new Vector2(880, 1140);

            // 看板娘本體 Image
            _characterPortraitImage = charGo.AddComponent<Image>();
            _characterPortraitImage.color = Color.white;
            _characterPortraitImage.preserveAspect = true;

            // 點擊感應按鈕
            _characterClickArea = charGo.AddComponent<Button>();
            _characterClickArea.onClick.AddListener(OnCharacterTapped);

            // 嘗試載入 Sakura 或看板娘立繪圖
            Sprite portraitSp = TryLoadCharacterSprite("char18_sakura");
            if (portraitSp == null)
            {
                portraitSp = TryLoadCharacterSprite("char01_claire");
            }
            if (portraitSp != null)
            {
                _characterPortraitImage.sprite = portraitSp;
            }
            else
            {
                _characterPortraitImage.sprite = CreateSilhouetteSprite(880, 1140);
                var charLabelGo = CreateUIObject("PlaceholderText", charGo.transform);
                var charLabelRt = charLabelGo.GetComponent<RectTransform>();
                StretchFull(charLabelRt);
                var charLabelText = charLabelGo.AddComponent<Text>();
                charLabelText.font = defaultFont;
                charLabelText.fontSize = 22;
                charLabelText.alignment = TextAnchor.MiddleCenter;
                charLabelText.color = new Color(0.2f, 0.3f, 0.45f, 0.7f);
                charLabelText.text = "【看板娘立繪展示區】\n點擊互動聆聽語音";
            }

            // 看板娘身分金標膠囊 (Name Badge) - 放在立繪前方
            var badgeGo = CreateUIObject("CharBadge", transform);
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0.42f, 0f);
            badgeRt.anchorMax = new Vector2(0.42f, 0f);
            badgeRt.pivot = new Vector2(0.5f, 0f);
            badgeRt.anchoredPosition = new Vector2(0, 30);
            badgeRt.sizeDelta = new Vector2(360, 42);
            var badgeImg = badgeGo.AddComponent<Image>();
            badgeImg.sprite = CreatePillSprite(360, 42, new Color(0.08f, 0.12f, 0.20f, 0.92f), new Color(1f, 0.8f, 0.2f, 0.88f));

            var badgeTxtGo = CreateUIObject("BadgeText", badgeGo.transform);
            StretchFull(badgeTxtGo.GetComponent<RectTransform>(), 4);
            var badgeTxt = badgeTxtGo.AddComponent<Text>();
            badgeTxt.font = defaultFont;
            badgeTxt.fontSize = 17;
            badgeTxt.alignment = TextAnchor.MiddleCenter;
            badgeTxt.fontStyle = FontStyle.Bold;
            badgeTxt.color = new Color(1f, 0.92f, 0.65f, 1f);
            badgeTxt.text = "★ SSR 櫻 (Sakura) · 巫女";

            // 4. Speech Bubble (對話氣泡：二次元玻璃擬態 + 指針)
            var bubbleGo = CreateUIObject("SpeechBubble", transform);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchorMin = new Vector2(0.42f, 0.65f);
            bubbleRt.anchorMax = new Vector2(0.42f, 0.65f);
            bubbleRt.pivot = new Vector2(0f, 0.5f);
            bubbleRt.anchoredPosition = new Vector2(170, 0);
            bubbleRt.sizeDelta = new Vector2(490, 125);

            var bubbleImg = bubbleGo.AddComponent<Image>();
            bubbleImg.sprite = CreateSpeechBubbleSprite(490, 125, new Color(0.08f, 0.12f, 0.22f, 0.94f), new Color(0f, 0.85f, 1f, 0.8f));
            _speechBubbleCanvasGroup = bubbleGo.AddComponent<CanvasGroup>();
            _speechBubbleRoot = bubbleGo;

            // 4.1 對話內文 (第一個子物件，讓 GetComponentInChildren<Text>() 首選對話內文)
            var bubbleTextGo = CreateUIObject("BubbleText", bubbleGo.transform);
            var bubbleTextRt = bubbleTextGo.GetComponent<RectTransform>();
            StretchFull(bubbleTextRt, 16);
            bubbleTextRt.offsetMin = new Vector2(24, 14);
            bubbleTextRt.offsetMax = new Vector2(-20, -14);
            _speechBubbleText = bubbleTextGo.AddComponent<Text>();
            _speechBubbleText.font = defaultFont;
            _speechBubbleText.fontSize = 18;
            _speechBubbleText.fontStyle = FontStyle.Bold;
            _speechBubbleText.lineSpacing = 1.25f;
            _speechBubbleText.alignment = TextAnchor.MiddleLeft;
            _speechBubbleText.color = Color.white;
            _speechBubbleText.text = "指揮官，歡迎回到作戰指揮部！\n各小隊整備完畢，隨時準備出擊！";

            var bubbleShadow = bubbleTextGo.AddComponent<Shadow>();
            bubbleShadow.effectColor = new Color(0f, 0.05f, 0.15f, 0.95f);
            bubbleShadow.effectDistance = new Vector2(1, -1);

            // 4.2 氣泡角色名標籤
            var bubbleTagGo = CreateUIObject("SpeakerTag", bubbleGo.transform);
            var bubbleTagRt = bubbleTagGo.GetComponent<RectTransform>();
            bubbleTagRt.anchorMin = new Vector2(0f, 1f);
            bubbleTagRt.anchorMax = new Vector2(0f, 1f);
            bubbleTagRt.pivot = new Vector2(0f, 0f);
            bubbleTagRt.anchoredPosition = new Vector2(25, -5);
            bubbleTagRt.sizeDelta = new Vector2(160, 24);
            var bTagImg = bubbleTagGo.AddComponent<Image>();
            bTagImg.sprite = CreatePillSprite(160, 24, new Color(0f, 0.55f, 0.85f, 0.95f), Color.white);

            var bTagTxtGo = CreateUIObject("TagText", bubbleTagGo.transform);
            StretchFull(bTagTxtGo.GetComponent<RectTransform>());
            var bTagTxt = bTagTxtGo.AddComponent<Text>();
            bTagTxt.font = defaultFont;
            bTagTxt.fontSize = 13;
            bTagTxt.alignment = TextAnchor.MiddleCenter;
            bTagTxt.fontStyle = FontStyle.Bold;
            bTagTxt.color = Color.white;
            bTagTxt.text = "SAKURA // 櫻粉巫女";
            _speechBubbleText.color = new Color(0.95f, 0.98f, 1f, 1f);
            _speechBubbleText.text = "「指揮官，歡迎回到作戰指揮部！\n作戰前要先去神社祈福求個大吉嗎？櫻會一直守護您的！」";
            _speechBubbleRoot.SetActive(false);

            // 4.5 掛載 2.5D 看板娘動態控制器 (L0 擬真呼吸、重心搖擺、視線跟隨與彈性互動)
            _dynamicPortrait = charGo.GetComponent<LobbyDynamicPortrait>();
            if (_dynamicPortrait == null)
            {
                _dynamicPortrait = charGo.AddComponent<LobbyDynamicPortrait>();
            }
            _dynamicPortrait.Initialize(charRt, pedRt, pedImg, bubbleRt);

            // 5. Top Header Bar (頂部戰術列：100% 通透全息懸浮，完全融合融入背景)
            var headerGo = CreateUIObject("TopHeaderBar", transform);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = Vector2.zero;
            headerRt.sizeDelta = new Vector2(0, 74);
            // 不添加任何長條底圖或黑帶，各項資源膠囊與頭像純懸浮於背景星空前，完美融為一體

            // 指揮官頭像與資訊 (左側)
            var avatarBoxGo = CreateUIObject("AvatarBox", headerGo.transform);
            var avatarBoxRt = avatarBoxGo.GetComponent<RectTransform>();
            avatarBoxRt.anchorMin = new Vector2(0f, 0.5f);
            avatarBoxRt.anchorMax = new Vector2(0f, 0.5f);
            avatarBoxRt.pivot = new Vector2(0f, 0.5f);
            avatarBoxRt.anchoredPosition = new Vector2(25, 0);
            avatarBoxRt.sizeDelta = new Vector2(56, 56);
            var avImg = avatarBoxGo.AddComponent<Image>();
            avImg.sprite = CreateCircleSprite(56, new Color(0.12f, 0.18f, 0.28f, 1f), new Color(1f, 0.8f, 0.2f, 1f), 3);

            // 頭像圖標
            Sprite bustSp = TryLoadCharacterSprite("char18_sakura_bust");
            if (bustSp == null)
            {
                bustSp = TryLoadCharacterSprite("char01_claire_bust");
            }
            if (bustSp != null)
            {
                var iconGo = CreateUIObject("Icon", avatarBoxGo.transform);
                StretchFull(iconGo.GetComponent<RectTransform>(), 4);
                var ic = iconGo.AddComponent<Image>();
                ic.sprite = bustSp;
                ic.preserveAspect = true;
            }

            // 玩家名稱與等級
            var pNameGo = CreateUIObject("PlayerName", headerGo.transform);
            var pNameRt = pNameGo.GetComponent<RectTransform>();
            pNameRt.anchorMin = new Vector2(0f, 0.5f);
            pNameRt.anchorMax = new Vector2(0f, 0.5f);
            pNameRt.pivot = new Vector2(0f, 0.5f);
            pNameRt.anchoredPosition = new Vector2(92, 7);
            pNameRt.sizeDelta = new Vector2(200, 26);
            _playerNameText = pNameGo.AddComponent<Text>();
            _playerNameText.font = defaultFont;
            _playerNameText.fontSize = 20;
            _playerNameText.fontStyle = FontStyle.Bold;
            _playerNameText.color = new Color(1f, 0.95f, 0.82f, 1f);
            _playerNameText.text = "指揮官 (Commander)";
            var pShadow = pNameGo.AddComponent<Outline>();
            pShadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            pShadow.effectDistance = new Vector2(1, -1);

            // 副標題 UID / EXP
            var subUidGo = CreateUIObject("SubUid", headerGo.transform);
            var subUidRt = subUidGo.GetComponent<RectTransform>();
            subUidRt.anchorMin = new Vector2(0f, 0.5f);
            subUidRt.anchorMax = new Vector2(0f, 0.5f);
            subUidRt.pivot = new Vector2(0f, 0.5f);
            subUidRt.anchoredPosition = new Vector2(92, -16);
            subUidRt.sizeDelta = new Vector2(200, 18);
            var subUidTxt = subUidGo.AddComponent<Text>();
            subUidTxt.font = defaultFont;
            subUidTxt.fontSize = 13;
            subUidTxt.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            subUidTxt.text = "LV.25 // UID: 10008892";

            // 右上角功能按鈕 (設定)
            var setBtnGo = CreateTacticalHeaderButton("BtnSettings", headerGo.transform, "設定", "icon_settings", new Vector2(-20, 0), new Color(0.15f, 0.18f, 0.25f, 0.95f), new Color(0.7f, 0.8f, 0.95f, 0.5f), defaultFont, 86);
            _settingsButton = setBtnGo.GetComponent<Button>();
            _settingsButton.onClick.AddListener(OnClickSettings);

            // 右上角三大資源膠囊 (鑽石、金幣、體力) - 嵌入真實彩色圖示
            _gemText = CreateResourcePill(headerGo.transform, "GemLabel", new Vector2(-116, 0), "3,450", "icon_gem", new Color(0.92f, 0.98f, 1f, 1f), new Color(0f, 0.85f, 1f, 0.7f), defaultFont);
            _goldText = CreateResourcePill(headerGo.transform, "GoldLabel", new Vector2(-296, 0), "128,500", "icon_gold", new Color(1f, 0.96f, 0.65f, 1f), new Color(1f, 0.85f, 0.25f, 0.7f), defaultFont);
            _staminaText = CreateResourcePill(headerGo.transform, "StaminaLabel", new Vector2(-476, 0), "5/5", "icon_stamina", new Color(0.85f, 1f, 0.90f, 1f), new Color(0.25f, 0.95f, 0.5f, 0.7f), defaultFont);

            // 除錯面板移到體力膠囊左側
            var debugBtnGo = CreateTacticalHeaderButton("BtnDebugToggle", headerGo.transform, "除錯", "icon_debug", new Vector2(-656, 0), new Color(0.12f, 0.25f, 0.20f, 0.95f), new Color(0.3f, 0.95f, 0.6f, 0.8f), defaultFont, 86);
            _toggleDebugButton = debugBtnGo.GetComponent<Button>();
            _toggleDebugButton.onClick.AddListener(OnToggleDebugMenu);

            // 6. Right Navigation Panel (右側戰術導航卡牌)
            var rightNavGo = CreateUIObject("RightNavigationPanel", transform);
            var rightNavRt = rightNavGo.GetComponent<RectTransform>();
            rightNavRt.anchorMin = new Vector2(1f, 0.52f);
            rightNavRt.anchorMax = new Vector2(1f, 0.52f);
            rightNavRt.pivot = new Vector2(1f, 0.5f);
            rightNavRt.anchoredPosition = new Vector2(-28, 0);
            rightNavRt.sizeDelta = new Vector2(230, 460);

            var vlg = rightNavGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 15;
            vlg.childAlignment = TextAnchor.UpperRight;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            _btnStory = CreateTacticalNavButton(rightNavGo.transform, "BtnStory", "主線劇情", "TACTICAL STORY", new Color(0.12f, 0.18f, 0.28f, 0.92f), new Color(0f, 0.8f, 1f, 0.6f), defaultFont, OnClickStory);
            _btnEncounter = CreateTacticalNavButton(rightNavGo.transform, "BtnEncounter", "巧遇約會", "ENCOUNTER // ADV", new Color(0.12f, 0.18f, 0.28f, 0.92f), new Color(0f, 0.8f, 1f, 0.6f), defaultFont, OnClickEncounter);
            _btnGacha = CreateTacticalNavButton(rightNavGo.transform, "BtnGacha", "契約招募", "SPECIAL RECRUIT // UP", new Color(0.88f, 0.18f, 0.45f, 0.98f), new Color(1f, 0.88f, 0.35f, 1f), defaultFont, OnClickGacha, true, "icon_gacha");
            _btnCharacters = CreateTacticalNavButton(rightNavGo.transform, "BtnCharacters", "部隊編組", "ROSTER & FORMATION", new Color(0.12f, 0.18f, 0.28f, 0.92f), new Color(0f, 0.8f, 1f, 0.6f), defaultFont, OnClickCharacters);
            _btnWardrobe = CreateTacticalNavButton(rightNavGo.transform, "BtnWardrobe", "換裝衣櫥", "WARDROBE & STAGE", new Color(0.12f, 0.18f, 0.28f, 0.92f), new Color(0f, 0.8f, 1f, 0.6f), defaultFont, OnClickWardrobe);
            _btnAchievements = CreateTacticalNavButton(rightNavGo.transform, "BtnAchievements", "戰役榮譽", "RECORD & TROPHY", new Color(0.12f, 0.18f, 0.28f, 0.92f), new Color(0f, 0.8f, 1f, 0.6f), defaultFont, OnClickAchievements);
            var achRedDotGo = CreateUIObject("RedDotBadge", _btnAchievements.transform);
            var achRedDotRt = achRedDotGo.GetComponent<RectTransform>();
            achRedDotRt.anchorMin = new Vector2(1f, 1f);
            achRedDotRt.anchorMax = new Vector2(1f, 1f);
            achRedDotRt.pivot = new Vector2(0.5f, 0.5f);
            achRedDotRt.anchoredPosition = new Vector2(-6, -6);
            achRedDotRt.sizeDelta = new Vector2(14, 14);
            var achRedDotImg = achRedDotGo.AddComponent<Image>();
            achRedDotImg.sprite = CreateCircleSprite(14, new Color(1f, 0.2f, 0.2f, 1f), Color.white, 1);
            achRedDotGo.SetActive(false);

            // 7. Bottom Right Battle Action Button (烈焰戰術出擊大按鈕)
            var battleBtnGo = CreateUIObject("BtnBattleAction", transform);
            var battleRt = battleBtnGo.GetComponent<RectTransform>();
            battleRt.anchorMin = new Vector2(1f, 0f);
            battleRt.anchorMax = new Vector2(1f, 0f);
            battleRt.pivot = new Vector2(1f, 0f);
            battleRt.anchoredPosition = new Vector2(-28, 28);
            battleRt.sizeDelta = new Vector2(260, 84);

            var battleImg = battleBtnGo.AddComponent<Image>();
            battleImg.sprite = CreateGradientSprite(260, 84, new Color(1f, 0.75f, 0.12f, 1f), new Color(1f, 0.32f, 0.12f, 1f), new Color(1f, 0.95f, 0.6f, 1f), 2, 8);
            _btnBattle = battleBtnGo.AddComponent<Button>();
            _btnBattle.onClick.AddListener(OnClickBattle);

            // 雙行文字排版
            var bTextGo = CreateUIObject("BattleText", battleBtnGo.transform);
            var bTextRt = bTextGo.GetComponent<RectTransform>();
            StretchFull(bTextRt, 6);
            bTextRt.anchoredPosition = new Vector2(0, 6);
            var battleTxt = bTextGo.AddComponent<Text>();
            battleTxt.font = defaultFont;
            battleTxt.fontSize = 28;
            battleTxt.fontStyle = FontStyle.Bold;
            battleTxt.alignment = TextAnchor.MiddleCenter;
            battleTxt.color = new Color(0.12f, 0.08f, 0.04f, 1f);
            battleTxt.text = "▶ 出擊 (BATTLE)";

            var bSubGo = CreateUIObject("SubText", battleBtnGo.transform);
            var bSubRt = bSubGo.GetComponent<RectTransform>();
            bSubRt.anchorMin = new Vector2(0f, 0f);
            bSubRt.anchorMax = new Vector2(1f, 0f);
            bSubRt.pivot = new Vector2(0.5f, 0f);
            bSubRt.anchoredPosition = new Vector2(0, 10);
            bSubRt.sizeDelta = new Vector2(0, 16);
            var bSubTxt = bSubGo.AddComponent<Text>();
            bSubTxt.font = defaultFont;
            bSubTxt.fontSize = 12;
            bSubTxt.alignment = TextAnchor.MiddleCenter;
            bSubTxt.fontStyle = FontStyle.Bold;
            bSubTxt.color = new Color(0.32f, 0.12f, 0.02f, 1f);
            bSubTxt.text = "SORTIE // 01 學院正門";

            // 8. Toast Panel (浮動提示)
            var toastGo = CreateUIObject("ToastBanner", transform);
            var toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(0.5f, 0.18f);
            toastRt.anchorMax = new Vector2(0.5f, 0.18f);
            toastRt.pivot = new Vector2(0.5f, 0.5f);
            toastRt.anchoredPosition = Vector2.zero;
            toastRt.sizeDelta = new Vector2(620, 52);

            var toastImg = toastGo.AddComponent<Image>();
            toastImg.sprite = CreatePillSprite(620, 52, new Color(0.06f, 0.09f, 0.15f, 0.95f), new Color(0f, 0.85f, 1f, 0.75f));
            _toastCanvasGroup = toastGo.AddComponent<CanvasGroup>();
            _toastRoot = toastGo;

            var toastTextGo = CreateUIObject("ToastText", toastGo.transform);
            var toastTextRt = toastTextGo.GetComponent<RectTransform>();
            StretchFull(toastTextRt, 8);
            _toastText = toastTextGo.AddComponent<Text>();
            _toastText.font = defaultFont;
            _toastText.fontSize = 19;
            _toastText.alignment = TextAnchor.MiddleCenter;
            _toastText.color = new Color(1f, 0.95f, 0.75f, 1f);
            _toastText.text = "歡迎回到作戰指揮部！";
            _toastRoot.SetActive(false);
        }

        #endregion

        #region 程序化精緻 UI 紋理與輔助生成器

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFull(RectTransform rt, float margin = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }

        private static Text CreateResourcePill(Transform parent, string name, Vector2 pos, string initVal, string iconName, Color textColor, Color borderColor, Font font)
        {
            var pillGo = CreateUIObject(name, parent);
            var rt = pillGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(170, 42);

            // 背景底圖放在子物件 Bg 上
            var bgGo = CreateUIObject("Bg", pillGo.transform);
            StretchFull(bgGo.GetComponent<RectTransform>());
            var img = bgGo.AddComponent<Image>();
            // 採用極淡微透全息底板 (Alpha 0.12)，星空背景完美穿透，文字 100% 澄澈清晰不被壓暗
            img.sprite = CreatePillSprite(170, 42, new Color(0.02f, 0.05f, 0.10f, 0.12f), borderColor);

            // 左側專屬彩色圖示 Icon
            Sprite iconSp = TryLoadIconSprite(iconName);
            var iconGo = CreateUIObject("Icon", pillGo.transform);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(8, 0);
            iconRt.sizeDelta = new Vector2(28, 28);
            var iconImg = iconGo.AddComponent<Image>();
            if (iconSp != null)
            {
                iconImg.sprite = iconSp;
                iconImg.preserveAspect = true;
            }
            else
            {
                iconImg.color = borderColor;
            }

            // Text 直接掛在 pillGo 上，滿足 GetComponent<Text>()
            var txt = pillGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 20;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = textColor;
            txt.text = initVal;
            // 採用柔和投影 Shadow，不侵蝕字型內部高光，數字極度清晰
            var shadow = pillGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            // 右側加號
            var plusGo = CreateUIObject("PlusBtn", pillGo.transform);
            var pRt = plusGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(1f, 0.5f);
            pRt.anchorMax = new Vector2(1f, 0.5f);
            pRt.pivot = new Vector2(1f, 0.5f);
            pRt.anchoredPosition = new Vector2(-8, 0);
            pRt.sizeDelta = new Vector2(24, 24);
            var pImg = plusGo.AddComponent<Image>();
            pImg.sprite = CreateCircleSprite(24, new Color(1f, 1f, 1f, 0.15f), borderColor, 1);
            var pTxtGo = CreateUIObject("PText", plusGo.transform);
            StretchFull(pTxtGo.GetComponent<RectTransform>());
            var pt = pTxtGo.AddComponent<Text>();
            pt.font = font;
            pt.fontSize = 16;
            pt.alignment = TextAnchor.MiddleCenter;
            pt.color = textColor;
            pt.text = "+";

            return txt;
        }

        private static GameObject CreateTacticalHeaderButton(string name, Transform parent, string label, string iconName, Vector2 pos, Color bgColor, Color borderColor, Font font, float width = 118)
        {
            var btnGo = CreateUIObject(name, parent);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 42);

            var img = btnGo.AddComponent<Image>();
            img.sprite = CreatePillSprite((int)width, 42, bgColor, borderColor);
            btnGo.AddComponent<Button>();

            Sprite iconSp = TryLoadIconSprite(iconName);
            if (iconSp != null)
            {
                var iconGo = CreateUIObject("Icon", btnGo.transform);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(10, 0);
                iconRt.sizeDelta = new Vector2(22, 22);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = iconSp;
                iconImg.preserveAspect = true;

                var txtGo = CreateUIObject("Text", btnGo.transform);
                var txtRt = txtGo.GetComponent<RectTransform>();
                StretchFull(txtRt);
                txtRt.offsetMin = new Vector2(28, 0);
                var txt = txtGo.AddComponent<Text>();
                txt.font = font;
                txt.fontSize = 15;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.text = label;
            }
            else
            {
                var txtGo = CreateUIObject("Text", btnGo.transform);
                StretchFull(txtGo.GetComponent<RectTransform>());
                var txt = txtGo.AddComponent<Text>();
                txt.font = font;
                txt.fontSize = 16;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.text = label;
            }

            return btnGo;
        }

        private static Button CreateTacticalNavButton(Transform parent, string name, string label, string subLabel, Color bgColor, Color borderColor, Font font, UnityEngine.Events.UnityAction onClick, bool isHighlight = false, string iconName = null)
        {
            var btnGo = CreateUIObject(name, parent);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(230, 56);

            var img = btnGo.AddComponent<Image>();
            img.sprite = CreateGradientSprite(230, 56, bgColor, new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f, bgColor.a), borderColor, isHighlight ? 3 : 2, 8);

            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            // 左側彩條 Accent Bar
            var barGo = CreateUIObject("AccentBar", btnGo.transform);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0f);
            barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.anchoredPosition = new Vector2(8, 0);
            barRt.sizeDelta = new Vector2(6, -10);
            var barImg = barGo.AddComponent<Image>();
            barImg.color = isHighlight ? new Color(1f, 0.92f, 0.3f, 1f) : borderColor;

            float textOffsetX = 24f;

            // 內嵌圖示 (例如契約招募圖示)
            Sprite iconSp = TryLoadIconSprite(iconName);
            if (iconSp != null)
            {
                var iconGo = CreateUIObject("NavIcon", btnGo.transform);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(18, 0);
                iconRt.sizeDelta = new Vector2(34, 34);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = iconSp;
                iconImg.preserveAspect = true;
                textOffsetX = 56f;
            }

            if (isHighlight)
            {
                // 右上角醒目黃金 UP! 特選彩帶標籤
                var upTagGo = CreateUIObject("UpBadge", btnGo.transform);
                var upRt = upTagGo.GetComponent<RectTransform>();
                upRt.anchorMin = new Vector2(1f, 1f);
                upRt.anchorMax = new Vector2(1f, 1f);
                upRt.pivot = new Vector2(1f, 1f);
                upRt.anchoredPosition = new Vector2(4, 4);
                upRt.sizeDelta = new Vector2(66, 20);
                var upImg = upTagGo.AddComponent<Image>();
                upImg.color = new Color(1f, 0.85f, 0.15f, 0.98f);

                var upTxtGo = CreateUIObject("Txt", upTagGo.transform);
                StretchFull(upTxtGo.GetComponent<RectTransform>());
                var upTxt = upTxtGo.AddComponent<Text>();
                upTxt.font = font;
                upTxt.fontSize = 11;
                upTxt.fontStyle = FontStyle.Bold;
                upTxt.alignment = TextAnchor.MiddleCenter;
                upTxt.color = new Color(0.18f, 0.08f, 0.02f, 1f);
                upTxt.text = "UP! 特選";
            }

            // 主標題
            var titleGo = CreateUIObject("Title", btnGo.transform);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(textOffsetX, 7);
            titleRt.sizeDelta = new Vector2(-textOffsetX - 12, 26);
            var titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = isHighlight ? 20 : 18;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleTxt.verticalOverflow = VerticalWrapMode.Overflow;
            titleTxt.color = isHighlight ? new Color(1f, 0.96f, 0.82f, 1f) : Color.white;
            titleTxt.text = label;

            // 副標題小字
            var subGo = CreateUIObject("SubTitle", btnGo.transform);
            var subRt = subGo.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(1f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(textOffsetX, -13);
            subRt.sizeDelta = new Vector2(-textOffsetX - 12, 16);
            var subTxt = subGo.AddComponent<Text>();
            subTxt.font = font;
            subTxt.fontSize = 10;
            subTxt.alignment = TextAnchor.MiddleLeft;
            subTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            subTxt.verticalOverflow = VerticalWrapMode.Overflow;
            subTxt.color = isHighlight ? new Color(1f, 0.85f, 0.95f, 0.9f) : new Color(0.6f, 0.75f, 0.95f, 0.7f);
            subTxt.text = subLabel;

            return btn;
        }

        #endregion

        #region 程序化紋理產生工具

        private static Sprite CreatePillSprite(int w, int h, Color fill, Color border)
        {
            return ProceduralUiKit.GetPillSprite(w, h, fill, border);
        }

        private static Sprite CreateCircleSprite(int size, Color fill, Color border, int borderWidth)
        {
            return ProceduralUiKit.GetCircleSprite(size, fill, border, borderWidth);
        }

        private static Sprite CreateGradientSprite(int w, int h, Color top, Color bottom, Color? border = null, int borderWidth = 0, int radius = 0)
        {
            return ProceduralUiKit.GetGradientSprite(w, h, top, bottom, border, borderWidth, radius);
        }

        private static Sprite CreateSpeechBubbleSprite(int w, int h, Color fill, Color border)
        {
            return ProceduralUiKit.GetSpeechBubbleSprite(w, h, fill, border);
        }

        private static Sprite CreateHoloRingSprite(int w, int h, Color color)
        {
            return ProceduralUiKit.GetHoloRingSprite(w, h, color);
        }

        private static Sprite CreateSilhouetteSprite(int w, int h)
        {
            return ProceduralUiKit.GetSilhouetteSprite(w, h);
        }

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _iconCache = new System.Collections.Generic.Dictionary<string, Sprite>();
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _characterSpriteCache = new System.Collections.Generic.Dictionary<string, Sprite>();
        private static Sprite _backgroundSpriteCache;

        public static Sprite TryLoadIconSprite(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;
            if (_iconCache.TryGetValue(iconName, out var cached) && cached != null)
                return cached;

            try
            {
                string p = Path.Combine(Application.streamingAssetsPath, "UI", "Icons", iconName + ".png");
                if (File.Exists(p))
                {
                    byte[] data = File.ReadAllBytes(p);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (tex.LoadImage(data))
                    {
                        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                        _iconCache[iconName] = sp;
                        return sp;
                    }
                    DestroyImmediate(tex);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] 載入圖示失敗 ({iconName}): {ex.Message}");
            }
            return null;
        }

        private static Sprite TryLoadCharacterSprite(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return null;
            if (_characterSpriteCache.TryGetValue(charId, out var cached) && cached != null)
                return cached;

            try
            {
                string p = Path.Combine(Application.streamingAssetsPath, "Portraits", charId + ".png");
                if (!File.Exists(p))
                {
                    bool isBust = charId.Contains("bust");
                    if (charId.Contains("sakura") || charId.Contains("18"))
                    {
                        string fn = isBust ? "char18_sakura_bust.png" : "char18_sakura.png";
                        p = Path.Combine(Application.streamingAssetsPath, "Portraits", fn);
                    }
                    else if (charId.Contains("claire") || charId.Contains("01"))
                    {
                        string fn = isBust ? "char01_claire_bust.png" : "char01_claire.png";
                        p = Path.Combine(Application.streamingAssetsPath, "Portraits", fn);
                    }
                }
                if (_characterSpriteCache.TryGetValue(p, out cached) && cached != null)
                {
                    _characterSpriteCache[charId] = cached;
                    return cached;
                }
                if (File.Exists(p))
                {
                    byte[] data = File.ReadAllBytes(p);
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(data))
                    {
                        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f));
                        _characterSpriteCache[p] = sp;
                        _characterSpriteCache[charId] = sp;
                        return sp;
                    }
                    DestroyImmediate(tex);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] 載入角色立繪失敗 ({charId}): {ex.Message}");
            }
            return null;
        }

        private static Sprite TryLoadBackgroundSprite()
        {
            if (_backgroundSpriteCache != null)
                return _backgroundSpriteCache;

            try
            {
                string p = Path.Combine(Application.streamingAssetsPath, "Backgrounds", "lobby_default.png");
                if (File.Exists(p))
                {
                    byte[] data = File.ReadAllBytes(p);
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(data))
                    {
                        _backgroundSpriteCache = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                        return _backgroundSpriteCache;
                    }
                    DestroyImmediate(tex);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] 載入背景圖失敗: {ex.Message}");
            }
            return null;
        }

        #endregion
    }
}
