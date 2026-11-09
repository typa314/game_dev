#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core.Persistence;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 多槽位雙層存讀檔彈窗介面 (design/14_ui_screens.md §12、Task M3-2)。
    /// 支援 Save（存檔模式）與 Load（讀檔模式），
    /// 列出手動槽位 slot_001～slot_006 與快速/自動槽位 quick_1、auto_1。
    /// 讀取空槽位時安全攔截並回傳 slot_not_found，保護當前劇本進度。
    /// </summary>
    public sealed class SaveLoadModalView : UnityScreenBase
    {
        public enum ModalMode
        {
            Save,
            Load
        }

        private static readonly string[] DefaultSlots = new string[]
        {
            "slot_001", "slot_002", "slot_003", "slot_004", "slot_005", "slot_006", "quick_1", "auto_1"
        };

        private RectTransform _modalPanel;
        private Text _titleText;
        private Button _closeButton;
        private Text _statusText;

        private readonly List<GameObject> _slotItemObjects = new List<GameObject>();
        private readonly List<Text> _slotLabelTexts = new List<Text>();
        private readonly List<Text> _slotDetailTexts = new List<Text>();
        private readonly List<Button> _slotActionButtons = new List<Button>();
        private readonly List<Text> _slotActionButtonTexts = new List<Text>();

        private StoryPlayerView _storyPlayerView;
        private StorySaveService _saveService;
        private MiniScript _currentScript;

        public ModalMode CurrentMode { get; private set; } = ModalMode.Save;
        public string LastError { get; private set; } = string.Empty;
        public IReadOnlyList<string> SupportedSlots => DefaultSlots;

        public override string ScreenId => "SaveLoadModalUI";

        public void Open(ModalMode mode, StoryPlayerView playerView, StorySaveService saveService, MiniScript script)
        {
            CurrentMode = mode;
            _storyPlayerView = playerView;
            _saveService = saveService ?? GameServices.StorySaves;
            _currentScript = script;
            LastError = string.Empty;

            EnsureUIHierarchy();
            RefreshSlots();
            gameObject.SetActive(true);
        }

        public void Close()
        {
            gameObject.SetActive(false);
            if (GameServices.Navigator != null && ReferenceEquals(GameServices.Navigator.CurrentScreen, this))
            {
                GameServices.Navigator.Pop();
            }
        }

        public override bool OnBackRequested()
        {
            if (gameObject.activeSelf)
            {
                Close();
                return true;
            }
            return false;
        }

        public void EnsureUIHierarchy()
        {
            if (_modalPanel != null) return;

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

            // 1. 半透明遮罩
            GameObject maskGo = ProceduralUiKit.CreateUiObject("DimMask", transform);
            RectTransform maskRt = maskGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(maskRt);
            Image maskImg = maskGo.AddComponent<Image>();
            maskImg.color = new Color(0f, 0f, 0f, 0.75f);
            Button maskBtn = maskGo.AddComponent<Button>();
            maskBtn.onClick.AddListener(Close);

            // 2. 主彈窗面板 (中央 80% 區域)
            GameObject modalGo = ProceduralUiKit.CreateUiObject("ModalPanel", transform);
            _modalPanel = modalGo.GetComponent<RectTransform>();
            _modalPanel.anchorMin = new Vector2(0.12f, 0.08f);
            _modalPanel.anchorMax = new Vector2(0.88f, 0.92f);
            _modalPanel.offsetMin = Vector2.zero;
            _modalPanel.offsetMax = Vector2.zero;

            Image mImg = modalGo.AddComponent<Image>();
            mImg.color = new Color(0.06f, 0.09f, 0.16f, 0.98f);

            // 標題列
            _titleText = CreateLabel(_modalPanel, "TitleText", "RECORD MANAGEMENT (存讀檔管理)", new Vector2(0.04f, 0.91f), new Vector2(0.70f, 0.98f), 20, new Color(0.95f, 0.85f, 0.35f), TextAnchor.MiddleLeft);

            // 關閉按鈕
            GameObject cGo = ProceduralUiKit.CreateUiObject("CloseBtn", _modalPanel);
            RectTransform cRt = cGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.88f, 0.92f);
            cRt.anchorMax = new Vector2(0.97f, 0.97f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            Image cImg = cGo.AddComponent<Image>();
            cImg.color = new Color(0.7f, 0.2f, 0.2f, 0.95f);
            _closeButton = cGo.AddComponent<Button>();
            _closeButton.onClick.AddListener(Close);
            CreateLabel(cRt, "Lbl", "關閉", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            // 狀態列
            _statusText = CreateLabel(_modalPanel, "StatusText", "", new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.06f), 13, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleLeft);

            // 槽位列表容器
            GameObject scrollGo = ProceduralUiKit.CreateUiObject("SlotsContainer", _modalPanel);
            RectTransform sRt = scrollGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.04f, 0.08f);
            sRt.anchorMax = new Vector2(0.96f, 0.89f);
            sRt.offsetMin = Vector2.zero;
            sRt.offsetMax = Vector2.zero;

            float slotHeightNorm = 1.0f / DefaultSlots.Length;
            for (int i = 0; i < DefaultSlots.Length; i++)
            {
                int index = i;
                string slotId = DefaultSlots[i];

                float yMax = 1.0f - i * slotHeightNorm - 0.01f;
                float yMin = 1.0f - (i + 1) * slotHeightNorm + 0.01f;

                GameObject slotGo = ProceduralUiKit.CreateUiObject("Slot_" + slotId, sRt);
                RectTransform slotRt = slotGo.GetComponent<RectTransform>();
                slotRt.anchorMin = new Vector2(0f, yMin);
                slotRt.anchorMax = new Vector2(1f, yMax);
                slotRt.offsetMin = Vector2.zero;
                slotRt.offsetMax = Vector2.zero;

                Image slotBg = slotGo.AddComponent<Image>();
                slotBg.color = new Color(0.12f, 0.16f, 0.24f, 0.9f);

                // Slot ID 標籤
                Text lbl = CreateLabel(slotRt, "SlotIdText", slotId.ToUpper(), new Vector2(0.02f, 0.1f), new Vector2(0.20f, 0.9f), 15, new Color(0.3f, 0.8f, 1f), TextAnchor.MiddleLeft);

                // Slot 詳細資訊
                Text detail = CreateLabel(slotRt, "DetailText", "[ 空白槽位 EMPTY ]", new Vector2(0.22f, 0.1f), new Vector2(0.78f, 0.9f), 13, Color.white, TextAnchor.MiddleLeft);

                // 操作按鈕
                GameObject actGo = ProceduralUiKit.CreateUiObject("ActionBtn", slotRt);
                RectTransform actRt = actGo.GetComponent<RectTransform>();
                actRt.anchorMin = new Vector2(0.80f, 0.15f);
                actRt.anchorMax = new Vector2(0.98f, 0.85f);
                actRt.offsetMin = Vector2.zero;
                actRt.offsetMax = Vector2.zero;

                Image actImg = actGo.AddComponent<Image>();
                actImg.color = new Color(0.2f, 0.5f, 0.8f, 0.95f);
                Button actBtn = actGo.AddComponent<Button>();
                actBtn.onClick.AddListener(() => OnClickSlotAction(slotId));

                Text actTxt = CreateLabel(actRt, "Txt", "執行", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

                _slotItemObjects.Add(slotGo);
                _slotLabelTexts.Add(lbl);
                _slotDetailTexts.Add(detail);
                _slotActionButtons.Add(actBtn);
                _slotActionButtonTexts.Add(actTxt);
            }
        }

        public void RefreshSlots()
        {
            EnsureUIHierarchy();

            if (_titleText != null)
            {
                _titleText.text = CurrentMode == ModalMode.Save
                    ? "RECORD MANAGEMENT - SAVE (儲存進度)"
                    : "RECORD MANAGEMENT - LOAD (讀取進度)";
            }

            for (int i = 0; i < DefaultSlots.Length; i++)
            {
                string slotId = DefaultSlots[i];
                StorySlot slotData = _saveService != null ? _saveService.Load(slotId) : null;

                if (_slotActionButtonTexts[i] != null)
                {
                    _slotActionButtonTexts[i].text = CurrentMode == ModalMode.Save ? "儲存 (SAVE)" : "讀取 (LOAD)";
                }

                if (_slotDetailTexts[i] != null)
                {
                    if (slotData != null)
                    {
                        string memoStr = !string.IsNullOrEmpty(slotData.Memo) ? $" [{slotData.Memo}]" : string.Empty;
                        _slotDetailTexts[i].text = $"節點: {slotData.Node}{memoStr} | 存檔時間: {slotData.SavedAtUtc}";
                        _slotDetailTexts[i].color = Color.white;
                    }
                    else
                    {
                        _slotDetailTexts[i].text = "[ 空白槽位 EMPTY ]";
                        _slotDetailTexts[i].color = new Color(0.6f, 0.6f, 0.6f);
                    }
                }
            }
        }

        private void OnClickSlotAction(string slotId)
        {
            if (CurrentMode == ModalMode.Save)
            {
                SaveToSlot(slotId, "手動存檔");
            }
            else
            {
                LoadFromSlot(slotId);
            }
        }

        public bool SaveToSlot(string slotId, string memo = null)
        {
            if (_saveService == null && _storyPlayerView?.SaveService != null)
            {
                _saveService = _storyPlayerView.SaveService;
            }
            if (_saveService == null)
            {
                LastError = "save_service_null";
                return false;
            }

            IStoryRunner runner = _storyPlayerView?.Presenter?.Runner;
            if (runner == null)
            {
                LastError = "runner_null";
                return false;
            }

            var slot = _saveService.Save(slotId, runner, null, null, memo);
            if (slot != null)
            {
                LastError = string.Empty;
                if (_statusText != null) _statusText.text = $"成功儲存至槽位 {slotId}！";
                RefreshSlots();
                return true;
            }

            LastError = "save_failed";
            return false;
        }

        public bool LoadFromSlot(string slotId)
        {
            if (_saveService == null && _storyPlayerView?.SaveService != null)
            {
                _saveService = _storyPlayerView.SaveService;
            }
            if (_saveService == null)
            {
                LastError = "save_service_null";
                return false;
            }

            StorySlot slot = _saveService.Load(slotId);
            if (slot == null)
            {
                LastError = "slot_not_found";
                if (_statusText != null) _statusText.text = $"槽位 {slotId} 為空白，無法讀取！";
                return false;
            }

            if (_storyPlayerView != null)
            {
                MiniScript script = _currentScript ?? _storyPlayerView.CurrentScript;
                IStoryRunner runner = _storyPlayerView.Presenter?.Runner;
                if (runner != null && script != null)
                {
                    bool ok = _saveService.ApplyToRunner(slot, runner, script);
                    if (ok)
                    {
                        _storyPlayerView.Presenter.Initialize();
                        LastError = string.Empty;
                        Close();
                        return true;
                    }
                }
            }

            LastError = "load_apply_failed";
            return false;
        }

        public StorySlot GetSlotData(string slotId)
        {
            return _saveService != null ? _saveService.Load(slotId) : null;
        }

        private static Text CreateLabel(Transform parent, string name, string text, Vector2 aMin, Vector2 aMax, int fontSize, Color color, TextAnchor align)
        {
            GameObject go = ProceduralUiKit.CreateUiObject(name, parent);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Text txt = go.AddComponent<Text>();
            txt.font = ProceduralUiKit.GetDefaultFont();
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = color;
            txt.text = text;
            return txt;
        }
    }
}
#endif
