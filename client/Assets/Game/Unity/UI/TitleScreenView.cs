#if UNITY_2021_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 遊戲標題啟動畫面 (design/14_ui_screens.md §1、Task M3-2)。
    /// 包含主視覺標題、版本號、以及 4 個主選單按鈕：
    /// 開始作戰 (START)、讀取紀錄 (LOAD)、系統設定 (SETTINGS)、結束遊戲 (EXIT)。
    /// 整合首次啟動 DLC 年齡確認閘門檢查。
    /// </summary>
    public sealed class TitleScreenView : UnityScreenBase
    {
        private Image _bgImage;
        private Text _titleText;
        private Text _versionText;

        private Button _btnStart;
        private Button _btnLoad;
        private Button _btnSettings;
        private Button _btnExit;

        private SaveLoadModalView _saveLoadModal;
        private SettingsModalView _settingsModal;

        public Button BtnStart => _btnStart;
        public Button BtnLoad => _btnLoad;
        public Button BtnSettings => _btnSettings;
        public Button BtnExit => _btnExit;

        public override string ScreenId => "TitleScreenUI";

        public void Initialize(SaveLoadModalView saveLoadModal = null, SettingsModalView settingsModal = null)
        {
            _saveLoadModal = saveLoadModal;
            _settingsModal = settingsModal;
            EnsureUIHierarchy();
        }

        private void Awake()
        {
            EnsureUIHierarchy();
        }

        public void EnsureUIHierarchy()
        {
            if (_bgImage != null) return;

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

            // 1. 背景層 (深黑紫漸層)
            GameObject bgGo = ProceduralUiKit.CreateUiObject("Background", transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(bgRt);
            _bgImage = bgGo.AddComponent<Image>();
            _bgImage.color = new Color(0.04f, 0.05f, 0.10f, 1f);

            // 2. 標題文字
            GameObject titleGo = ProceduralUiKit.CreateUiObject("TitleText", transform);
            RectTransform titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.1f, 0.65f);
            titleRt.anchorMax = new Vector2(0.9f, 0.85f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            _titleText = titleGo.AddComponent<Text>();
            _titleText.font = ProceduralUiKit.GetDefaultFont();
            _titleText.fontSize = 42;
            _titleText.alignment = TextAnchor.MiddleCenter;
            _titleText.color = new Color(0.95f, 0.85f, 0.40f, 1f);
            _titleText.text = "女武神作戰指揮所\nVALKYRIE TACTICS";

            // 3. 版本號
            GameObject verGo = ProceduralUiKit.CreateUiObject("VersionText", transform);
            RectTransform verRt = verGo.GetComponent<RectTransform>();
            verRt.anchorMin = new Vector2(0.1f, 0.58f);
            verRt.anchorMax = new Vector2(0.9f, 0.64f);
            verRt.offsetMin = Vector2.zero;
            verRt.offsetMax = Vector2.zero;
            _versionText = verGo.AddComponent<Text>();
            _versionText.font = ProceduralUiKit.GetDefaultFont();
            _versionText.fontSize = 16;
            _versionText.alignment = TextAnchor.MiddleCenter;
            _versionText.color = new Color(0.6f, 0.7f, 0.85f, 0.9f);
            _versionText.text = "Version 1.0.0 (M3 Feature & Content Complete)";

            // 4. 按鈕清單容器
            GameObject menuGo = ProceduralUiKit.CreateUiObject("MenuContainer", transform);
            RectTransform menuRt = menuGo.GetComponent<RectTransform>();
            menuRt.anchorMin = new Vector2(0.35f, 0.12f);
            menuRt.anchorMax = new Vector2(0.65f, 0.52f);
            menuRt.offsetMin = Vector2.zero;
            menuRt.offsetMax = Vector2.zero;

            _btnStart = CreateMenuButton(menuRt, "BtnStart", "▶ 開始作戰 (START)", 0.76f, 0.98f, new Color(0.85f, 0.25f, 0.40f, 0.95f), OnClickStart);
            _btnLoad = CreateMenuButton(menuRt, "BtnLoad", "📂 讀取紀錄 (LOAD)", 0.51f, 0.73f, new Color(0.15f, 0.35f, 0.60f, 0.95f), OnClickLoad);
            _btnSettings = CreateMenuButton(menuRt, "BtnSettings", "⚙ 系統設定 (SETTINGS)", 0.26f, 0.48f, new Color(0.18f, 0.25f, 0.38f, 0.95f), OnClickSettings);
            _btnExit = CreateMenuButton(menuRt, "BtnExit", "✖ 結束遊戲 (EXIT)", 0.01f, 0.23f, new Color(0.25f, 0.15f, 0.20f, 0.95f), OnClickExit);
        }

        private Button CreateMenuButton(Transform parent, string name, string label, float yMin, float yMax, Color bgColor, Action onClick)
        {
            GameObject bGo = ProceduralUiKit.CreateUiObject(name, parent);
            RectTransform rt = bGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, yMin);
            rt.anchorMax = new Vector2(0.95f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = bGo.AddComponent<Image>();
            img.color = bgColor;
            Button btn = bGo.AddComponent<Button>();
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            GameObject lblGo = ProceduralUiKit.CreateUiObject("Text", bGo.transform);
            RectTransform lRt = lblGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(lRt);
            Text txt = lblGo.AddComponent<Text>();
            txt.font = ProceduralUiKit.GetDefaultFont();
            txt.fontSize = 18;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;

            return btn;
        }

        public void OnClickStart()
        {
            // 檢查成人 DLC 年齡閘門狀態
            bool isDlcInstalled = GameServices.Platform != null && GameServices.Platform.IsDlcInstalled("dlc_adult");
            bool isAgeVerified = GameServices.AgeGate != null && GameServices.AgeGate.IsVerified;

            if (isDlcInstalled && !isAgeVerified)
            {
                // 首次啟動引導至年齡確認或設定介面
                OnClickSettings();
                return;
            }

            // 導航推進至大廳
            if (GameServices.Navigator != null)
            {
                var lobby = FindAnyObjectByType<LobbyView>(FindObjectsInactive.Include);
                if (lobby != null)
                {
                    GameServices.Navigator.Push(lobby);
                }
            }
        }

        public void OnClickLoad()
        {
            if (_saveLoadModal == null)
            {
                _saveLoadModal = FindAnyObjectByType<SaveLoadModalView>(FindObjectsInactive.Include);
            }

            if (_saveLoadModal != null)
            {
                if (GameServices.Navigator != null)
                {
                    GameServices.Navigator.Push(_saveLoadModal);
                }
                _saveLoadModal.Open(SaveLoadModalView.ModalMode.Load, null, GameServices.StorySaves, null);
            }
        }

        public void OnClickSettings()
        {
            if (_settingsModal == null)
            {
                _settingsModal = FindAnyObjectByType<SettingsModalView>(FindObjectsInactive.Include);
            }

            if (_settingsModal != null)
            {
                if (GameServices.Navigator != null)
                {
                    GameServices.Navigator.Push(_settingsModal);
                }
                else
                {
                    _settingsModal.gameObject.SetActive(true);
                }
            }
        }

        public void OnClickExit()
        {
            Application.Quit();
        }
    }
}
#endif
