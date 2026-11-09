#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Persistence;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 全螢幕 ADV 劇情播放器畫面 (OPT-6 / ARCH-04)。
    /// 實作 IStoryView，包含全螢幕對話框、角色立繪/表情、選項分支、CG 全景、Backlog 歷史彈窗、
    /// 以及雙層劇情存讀檔 (StorySaveService) 介面。
    /// </summary>
    public sealed class StoryPlayerView : UnityScreenBase, IStoryView
    {
        private StoryPresenter _presenter;
        private StorySaveService _saveService;

        // UI 根節點
        private Image _bgImage;
        private Image _cgImage;
        private RectTransform _charContainer;
        private Dictionary<string, Image> _charImages = new Dictionary<string, Image>();

        // Live2D 立繪串接 (Task M3-1)
        private Live2DPortraitView _activeLive2DView;
        private readonly Dictionary<string, Live2DPortraitView> _charLive2DViews = new Dictionary<string, Live2DPortraitView>(StringComparer.OrdinalIgnoreCase);

        public Live2DPortraitView ActiveLive2DView => _activeLive2DView;

        public void BindLive2DCharacter(string characterId, Live2DPortraitView live2DView)
        {
            if (string.IsNullOrEmpty(characterId) || live2DView == null) return;
            _charLive2DViews[characterId] = live2DView;
            _activeLive2DView = live2DView;
        }

        public Live2DPortraitView GetLive2DView(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return _activeLive2DView;
            if (_charLive2DViews.TryGetValue(characterId, out var view)) return view;
            return _activeLive2DView;
        }

        // 對話框面板
        private RectTransform _dialoguePanel;
        private Text _speakerText;
        private Text _dialogueText;
        private Button _advanceButton;

        // 控制按鈕欄
        private Button _btnAuto;
        private Button _btnSkip;
        private Button _btnBacklog;
        private Button _btnHideUi;
        private Button _btnSave;
        private Button _btnLoad;
        private Button _btnExit;

        // 分支選項面板
        private RectTransform _optionsPanel;
        private List<Button> _optionButtons = new List<Button>();
        private List<Text> _optionLabels = new List<Text>();

        private GameCore _core;
        public GameCore Core => _core;
        public void SetCoreForTesting(GameCore core) { _core = core; }

        // M3-6 分支拓樸圖與選項標記
        private RectTransform _branchGraphPanel;
        private Text _branchGraphTitle;
        private Text _branchGraphContentText;
        private Button _branchGraphCloseButton;
        private Button _btnBranchGraph;
        private readonly List<bool> _optionIsRead = new List<bool>();

        public bool IsBranchGraphModalOpen => _branchGraphPanel != null && _branchGraphPanel.gameObject.activeSelf;
        public StoryBranchGraph CurrentBranchGraph { get; private set; }
        public IReadOnlyList<bool> OptionIsRead => _optionIsRead;
        public bool IsOptionRead(int index) => index >= 0 && index < _optionIsRead.Count && _optionIsRead[index];

        // Backlog 歷史彈窗
        private RectTransform _backlogPanel;
        private Text _backlogContentText;
        private Button _backlogCloseButton;

        // 劇情存讀檔彈窗
        private SaveLoadModalView _saveLoadModalView;
        public SaveLoadModalView SaveLoadModal => _saveLoadModalView;
        public void SetSaveLoadModal(SaveLoadModalView modal) { _saveLoadModalView = modal; }

        // 公開屬性供測試驗證 (G3 品質閘門)
        public StoryPresenter Presenter => _presenter;
        public StorySaveService SaveService => _saveService;
        public void SetSaveService(StorySaveService saveService) { _saveService = saveService; }
        public MiniScript CurrentScript { get; private set; }
        public string CurrentScriptId { get; private set; } = string.Empty;
        public bool IsRecollectionMode { get; private set; }
        public string CurrentSpeaker { get; private set; } = string.Empty;
        public string CurrentDialogue { get; private set; } = string.Empty;
        public string CurrentBackground { get; private set; } = string.Empty;
        public string CurrentCg { get; private set; } = string.Empty;
        public bool IsOptionsVisible { get; private set; }
        public bool IsUiVisible { get; private set; } = true;
        public bool IsBacklogOpen => _backlogPanel != null && _backlogPanel.gameObject.activeSelf;
        public IReadOnlyList<Button> OptionButtons => _optionButtons;
        public int OptionCount => _optionButtons.Count;
        public string LastError { get; private set; }

        private Action _onCompleted;
        private bool _completionInvoked;
        private Dictionary<string, double> _savedFlagsSnapshot;
        private bool _savedDirtySnapshot;

        public override string ScreenId => "StoryPlayerUI";

        public void Initialize(StoryPresenter presenter, StorySaveService saveService = null, GameCore core = null)
        {
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            _saveService = saveService;
            _core = core ?? _core ?? GameServices.Core;
            _presenter.AttachView(this);

            EnsureUiHierarchy();
            _presenter.Initialize();
        }

        /// <summary>
        /// M2-3: 載入並播放指定劇本，支援回憶重播模式 (isRecollection) 與完成回呼。
        /// 回憶模式下自動備份並還原 Story.Flags，不寫入任何養成/好感/旗標變更。
        /// </summary>
        public bool LoadAndPlay(string scriptId, MiniScript script, GameCore core, bool isRecollection, Action onCompleted = null)
        {
            if (script == null || core == null) return false;

            _core = core;
            CurrentScript = script;
            CurrentScriptId = scriptId ?? string.Empty;
            IsRecollectionMode = isRecollection;
            _onCompleted = onCompleted;
            _completionInvoked = false;

            if (isRecollection && core.Player?.Story?.Flags != null)
            {
                _savedFlagsSnapshot = new Dictionary<string, double>(core.Player.Story.Flags);
                _savedDirtySnapshot = core.Dirty;
            }
            else
            {
                _savedFlagsSnapshot = null;
            }

            var runner = new MiniScriptRunner(script, core.Rating);
            var controller = new StoryPlaybackController(runner, core, isRecallMode: isRecollection);
            var presenter = new StoryPresenter(controller);
            StoryCommandRegistry.Register(runner, core, presenter, isRecallMode: isRecollection);

            Initialize(presenter, GameServices.StorySaves);
            if (GameServices.Navigator != null)
            {
                if (!ReferenceEquals(GameServices.Navigator.CurrentScreen, this))
                {
                    GameServices.Navigator.Push(this);
                }
            }
            else
            {
                gameObject.SetActive(true);
            }

            presenter.Start("Start");
            CheckAndHandleStoryCompletion();
            return true;
        }

        private void Awake()
        {
            EnsureUiHierarchy();
        }

        private void EnsureUiHierarchy()
        {
            if (_bgImage != null) return;

            // 確保自身掛有 Canvas
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

            // 1. 背景層
            GameObject bgGo = ProceduralUiKit.CreateUiObject("Background", transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(bgRt);
            _bgImage = bgGo.AddComponent<Image>();
            _bgImage.color = new Color(0.05f, 0.05f, 0.08f, 1f);

            // 2. CG 全景層 (預設隱藏)
            GameObject cgGo = ProceduralUiKit.CreateUiObject("CgLayer", transform);
            RectTransform cgRt = cgGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(cgRt);
            _cgImage = cgGo.AddComponent<Image>();
            _cgImage.gameObject.SetActive(false);

            // 3. 角色立繪層
            GameObject charContainerGo = ProceduralUiKit.CreateUiObject("CharContainer", transform);
            _charContainer = charContainerGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(_charContainer);

            // 4. 全螢幕推進點擊熱區
            GameObject advHitGo = ProceduralUiKit.CreateUiObject("AdvanceHitArea", transform);
            RectTransform advRt = advHitGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(advRt);
            _advanceButton = advHitGo.AddComponent<Button>();
            Image hitImg = advHitGo.AddComponent<Image>();
            hitImg.color = Color.clear;
            _advanceButton.onClick.AddListener(Advance);

            // 5. 對話框面板 (底部 28% 高度)
            GameObject dialGo = ProceduralUiKit.CreateUiObject("DialoguePanel", transform);
            _dialoguePanel = dialGo.GetComponent<RectTransform>();
            _dialoguePanel.anchorMin = new Vector2(0.05f, 0.04f);
            _dialoguePanel.anchorMax = new Vector2(0.95f, 0.30f);
            _dialoguePanel.offsetMin = Vector2.zero;
            _dialoguePanel.offsetMax = Vector2.zero;

            Image dialBg = dialGo.AddComponent<Image>();
            dialBg.color = new Color(0.04f, 0.06f, 0.12f, 0.88f);

            // 說話者名字標籤
            _speakerText = CreateLabel(_dialoguePanel, "SpeakerText", "旁白", new Vector2(0.03f, 0.72f), new Vector2(0.40f, 0.95f), 18, new Color(0.95f, 0.80f, 0.30f), TextAnchor.MiddleLeft);

            // 台詞文字
            _dialogueText = CreateLabel(_dialoguePanel, "DialogueText", "", new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.70f), 16, Color.white, TextAnchor.UpperLeft);

            // 6. 控制按鈕欄 (對話框上方)
            BuildControlToolbar();

            // 7. 分支選項面板
            GameObject optGo = ProceduralUiKit.CreateUiObject("OptionsPanel", transform);
            _optionsPanel = optGo.GetComponent<RectTransform>();
            _optionsPanel.anchorMin = new Vector2(0.20f, 0.32f);
            _optionsPanel.anchorMax = new Vector2(0.80f, 0.72f);
            _optionsPanel.offsetMin = Vector2.zero;
            _optionsPanel.offsetMax = Vector2.zero;
            _optionsPanel.gameObject.SetActive(false);

            // 8. Backlog 歷史彈窗
            BuildBacklogPanel();

            // 9. 分支拓樸圖彈窗 (M3-6)
            BuildBranchGraphPanel();
        }

        private void BuildControlToolbar()
        {
            GameObject barGo = ProceduralUiKit.CreateUiObject("ControlToolbar", transform);
            RectTransform barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0.50f, 0.31f);
            barRt.anchorMax = new Vector2(0.95f, 0.36f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            string[] btnTitles = new string[] { "AUTO", "SKIP", "LOG", "FLOW", "HIDE", "SAVE", "LOAD", "EXIT" };
            Button[] btnTargets = new Button[8];

            float btnWidthNorm = 1.0f / btnTitles.Length;
            for (int i = 0; i < btnTitles.Length; i++)
            {
                int idx = i;
                float xMin = i * btnWidthNorm;
                float xMax = (i + 1) * btnWidthNorm - 0.01f;

                GameObject bGo = ProceduralUiKit.CreateUiObject("Btn_" + btnTitles[i], barRt);
                RectTransform bRt = bGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(xMin, 0.1f);
                bRt.anchorMax = new Vector2(xMax, 0.9f);
                bRt.offsetMin = Vector2.zero;
                bRt.offsetMax = Vector2.zero;

                Image img = bGo.AddComponent<Image>();
                img.color = new Color(0.12f, 0.18f, 0.28f, 0.9f);
                Button btn = bGo.AddComponent<Button>();
                btnTargets[i] = btn;

                CreateLabel(bRt, "Label", btnTitles[i], Vector2.zero, Vector2.one, 11, Color.white, TextAnchor.MiddleCenter);
            }

            _btnAuto = btnTargets[0];
            _btnSkip = btnTargets[1];
            _btnBacklog = btnTargets[2];
            _btnBranchGraph = btnTargets[3];
            _btnHideUi = btnTargets[4];
            _btnSave = btnTargets[5];
            _btnLoad = btnTargets[6];
            _btnExit = btnTargets[7];

            _btnAuto.onClick.AddListener(ToggleAutoPlay);
            _btnSkip.onClick.AddListener(() => SetFastForward(!(_presenter?.IsFastForward ?? false)));
            _btnBacklog.onClick.AddListener(OpenBacklog);
            _btnBranchGraph.onClick.AddListener(OpenBranchGraphModal);
            _btnHideUi.onClick.AddListener(ToggleHideUi);
            _btnSave.onClick.AddListener(OpenSaveModal);
            _btnLoad.onClick.AddListener(OpenLoadModal);
            _btnExit.onClick.AddListener(ExitStory);
        }

        private void BuildBacklogPanel()
        {
            GameObject go = ProceduralUiKit.CreateUiObject("BacklogModal", transform);
            _backlogPanel = go.GetComponent<RectTransform>();
            _backlogPanel.anchorMin = new Vector2(0.10f, 0.10f);
            _backlogPanel.anchorMax = new Vector2(0.90f, 0.90f);
            _backlogPanel.offsetMin = Vector2.zero;
            _backlogPanel.offsetMax = Vector2.zero;

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.04f, 0.08f, 0.96f);

            CreateLabel(_backlogPanel, "Title", "HISTORICAL LOG (歷史對話回顧)", new Vector2(0.05f, 0.90f), new Vector2(0.80f, 0.98f), 18, new Color(0.9f, 0.8f, 0.4f), TextAnchor.MiddleLeft);

            _backlogContentText = CreateLabel(_backlogPanel, "Content", "", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 14, Color.white, TextAnchor.UpperLeft);

            // 關閉按鈕
            GameObject closeGo = ProceduralUiKit.CreateUiObject("CloseBtn", _backlogPanel);
            RectTransform cRt = closeGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.85f, 0.91f);
            cRt.anchorMax = new Vector2(0.96f, 0.97f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            Image cImg = closeGo.AddComponent<Image>();
            cImg.color = new Color(0.7f, 0.2f, 0.2f, 1f);
            _backlogCloseButton = closeGo.AddComponent<Button>();
            _backlogCloseButton.onClick.AddListener(CloseBacklog);
            CreateLabel(cRt, "Lbl", "關閉", Vector2.zero, Vector2.one, 12, Color.white, TextAnchor.MiddleCenter);

            _backlogPanel.gameObject.SetActive(false);
        }

        private void BuildBranchGraphPanel()
        {
            GameObject go = ProceduralUiKit.CreateUiObject("BranchGraphModal", transform);
            _branchGraphPanel = go.GetComponent<RectTransform>();
            _branchGraphPanel.anchorMin = new Vector2(0.10f, 0.10f);
            _branchGraphPanel.anchorMax = new Vector2(0.90f, 0.90f);
            _branchGraphPanel.offsetMin = Vector2.zero;
            _branchGraphPanel.offsetMax = Vector2.zero;

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.05f, 0.10f, 0.96f);

            _branchGraphTitle = CreateLabel(_branchGraphPanel, "Title", "BRANCH FLOWCHART (劇本分支拓樸)", new Vector2(0.05f, 0.90f), new Vector2(0.80f, 0.98f), 18, new Color(0.4f, 0.85f, 0.95f), TextAnchor.MiddleLeft);

            _branchGraphContentText = CreateLabel(_branchGraphPanel, "Content", "", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 14, Color.white, TextAnchor.UpperLeft);

            // 關閉按鈕
            GameObject closeGo = ProceduralUiKit.CreateUiObject("CloseBtn", _branchGraphPanel);
            RectTransform cRt = closeGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.85f, 0.91f);
            cRt.anchorMax = new Vector2(0.96f, 0.97f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            Image cImg = closeGo.AddComponent<Image>();
            cImg.color = new Color(0.7f, 0.2f, 0.2f, 1f);
            _branchGraphCloseButton = closeGo.AddComponent<Button>();
            _branchGraphCloseButton.onClick.AddListener(CloseBranchGraphModal);
            CreateLabel(cRt, "Lbl", "關閉", Vector2.zero, Vector2.one, 12, Color.white, TextAnchor.MiddleCenter);

            _branchGraphPanel.gameObject.SetActive(false);
        }

        public void OpenBranchGraphModal()
        {
            if (_branchGraphPanel == null) return;
            var readNodes = _core?.Player?.Story?.ReadNodes;
            var endings = _core?.Player?.Endings;

            CurrentBranchGraph = StoryBranchGraphBuilder.Build(CurrentScript, readNodes, endings);

            int visited = CurrentBranchGraph.VisitedNodes;
            int total = CurrentBranchGraph.TotalNodes;
            if (_branchGraphTitle != null)
            {
                _branchGraphTitle.text = $"BRANCH FLOWCHART (探索度: {visited}/{total})";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== 探索進度: {visited}/{total} ({CurrentBranchGraph.CompletionRate:P0}) ===");
            sb.AppendLine();
            foreach (var node in CurrentBranchGraph.Nodes)
            {
                if (node.IsVisited)
                {
                    string tag = node.IsEnding ? (string.IsNullOrEmpty(node.EndingId) ? "[🏆 結局]" : $"[🏆 結局: {node.EndingId}]") : "[節點]";
                    string ratingTag = node.Rating >= Rating.R18 ? " [R18]" : "";
                    sb.AppendLine($"✓ {node.NodeId} {tag}{ratingTag}");
                }
                else
                {
                    sb.AppendLine("[🔒 未探索分支] (探索後解鎖)");
                }

                var edges = CurrentBranchGraph.GetOutgoingEdges(node.NodeId);
                for (int e = 0; e < edges.Count; e++)
                {
                    var edge = edges[e];
                    if (edge.IsTaken)
                    {
                        sb.AppendLine($"   └──> {edge.ToNode} (選項: {edge.OptionTextKey ?? "繼續"}) [✓ 已探索]");
                    }
                    else if (node.IsVisited)
                    {
                        sb.AppendLine($"   └──> [🔒 未探索分支] (選項: {edge.OptionTextKey ?? "分支"})");
                    }
                }
            }

            if (_branchGraphContentText != null)
            {
                _branchGraphContentText.text = sb.ToString();
            }

            _branchGraphPanel.gameObject.SetActive(true);
        }

        public void CloseBranchGraphModal()
        {
            if (_branchGraphPanel != null)
            {
                _branchGraphPanel.gameObject.SetActive(false);
            }
        }


        // =========================================================================================
        // 操作與控制方法
        // =========================================================================================

        public void Advance()
        {
            if (IsBacklogOpen)
            {
                CloseBacklog();
                return;
            }

            if (!IsUiVisible)
            {
                ToggleHideUi();
                return;
            }

            if (_presenter != null)
            {
                _presenter.Advance();
                CheckAndHandleStoryCompletion();
            }
        }

        public void ChooseOption(int index)
        {
            if (_presenter != null && index >= 0)
            {
                _presenter.ChooseOption(index);
                CheckAndHandleStoryCompletion();
            }
        }

        public void ToggleAutoPlay()
        {
            _presenter?.ToggleAutoPlay();
        }

        public void SetFastForward(bool enabled)
        {
            _presenter?.SetFastForward(enabled);
        }

        public void ToggleHideUi()
        {
            IsUiVisible = !IsUiVisible;
            SetUiVisible(IsUiVisible);
        }

        public void OpenBacklog()
        {
            if (_backlogPanel != null)
            {
                _backlogPanel.gameObject.SetActive(true);
            }
        }

        public void CloseBacklog()
        {
            if (_backlogPanel != null)
            {
                _backlogPanel.gameObject.SetActive(false);
            }
        }

        public StorySlot SaveSlot(string slotId, string memo = null)
        {
            if (_saveService == null || _presenter?.Runner == null) return null;
            return _saveService.Save(slotId, _presenter.Runner, null, null, memo);
        }

        public void OpenSaveModal()
        {
            if (_saveLoadModalView != null)
            {
                _saveLoadModalView.Open(SaveLoadModalView.ModalMode.Save, this, _saveService, CurrentScript);
            }
            else
            {
                SaveSlot("slot_001", "快捷存檔");
            }
        }

        public void OpenLoadModal()
        {
            if (_saveLoadModalView != null)
            {
                _saveLoadModalView.Open(SaveLoadModalView.ModalMode.Load, this, _saveService, CurrentScript);
            }
        }

        public bool LoadSlot(string slotId, MiniScript script)
        {
            if (_saveService == null || _presenter?.Runner == null || script == null) return false;
            StorySlot slot = _saveService.Load(slotId);
            if (slot == null) return false;

            bool ok = _saveService.ApplyToRunner(slot, _presenter.Runner, script);
            if (ok)
            {
                _presenter.Initialize();
            }
            return ok;
        }

        public void ExitStory()
        {
            RestoreRecollectionSnapshotIfNeeded();
            GameServices.Navigator?.Pop();
        }

        private void CheckAndHandleStoryCompletion()
        {
            if (_presenter?.Runner == null || !_presenter.Runner.IsEnded) return;
            RestoreRecollectionSnapshotIfNeeded();
            if (!_completionInvoked)
            {
                _completionInvoked = true;
                _onCompleted?.Invoke();
            }
        }

        private void RestoreRecollectionSnapshotIfNeeded()
        {
            if (!IsRecollectionMode || _savedFlagsSnapshot == null || _presenter?.Core?.Player?.Story == null) return;
            var flags = _presenter.Core.Player.Story.Flags;
            flags.Clear();
            foreach (var kvp in _savedFlagsSnapshot)
            {
                flags[kvp.Key] = kvp.Value;
            }
            _presenter.Core.Dirty = _savedDirtySnapshot;
        }

        public override bool OnBackRequested()
        {
            if (IsBacklogOpen)
            {
                CloseBacklog();
                return true;
            }
            if (!IsUiVisible)
            {
                ToggleHideUi();
                return true;
            }
            return false;
        }

        // =========================================================================================
        // IStoryView 介面實作
        // =========================================================================================

        public void SetSpeaker(string name)
        {
            CurrentSpeaker = name ?? string.Empty;
            if (_speakerText != null)
            {
                _speakerText.text = CurrentSpeaker;
            }
        }

        public void SetDialogue(string textKey)
        {
            CurrentDialogue = textKey ?? string.Empty;
            if (_dialogueText != null)
            {
                _dialogueText.text = CurrentDialogue;
            }
            if (_activeLive2DView != null)
            {
                _activeLive2DView.SetLipSync(!string.IsNullOrEmpty(CurrentDialogue) ? 0.6f : 0f);
            }
        }

        public void ShowOptions(IReadOnlyList<string> optionKeys)
        {
            IsOptionsVisible = true;
            if (_optionsPanel == null) return;

            _optionsPanel.gameObject.SetActive(true);
            _optionIsRead.Clear();

            int count = optionKeys != null ? optionKeys.Count : 0;
            while (_optionButtons.Count < count)
            {
                int btnIndex = _optionButtons.Count;
                GameObject btnGo = ProceduralUiKit.CreateUiObject("OptionBtn_" + btnIndex, _optionsPanel);
                RectTransform bRt = btnGo.GetComponent<RectTransform>();
                Image img = btnGo.AddComponent<Image>();
                img.color = new Color(0.10f, 0.20f, 0.35f, 0.95f);
                Button btn = btnGo.AddComponent<Button>();
                btn.onClick.AddListener(() => ChooseOption(btnIndex));

                Text lbl = CreateLabel(bRt, "Label", "", Vector2.zero, Vector2.one, 15, Color.white, TextAnchor.MiddleCenter);

                _optionButtons.Add(btn);
                _optionLabels.Add(lbl);
            }

            var currentOpts = _presenter?.Runner?.CurrentOptions;
            var readNodes = _core?.Player?.Story?.ReadNodes;

            float normH = 1.0f / Math.Max(1, count);
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                if (i < count)
                {
                    _optionButtons[i].gameObject.SetActive(true);
                    string optText = optionKeys[i];
                    bool isRead = false;

                    if (currentOpts != null && i < currentOpts.Count)
                    {
                        string targetNode = currentOpts[i].TargetNode;
                        if (!string.IsNullOrEmpty(targetNode) && readNodes != null && readNodes.Contains(targetNode))
                        {
                            isRead = true;
                        }
                    }

                    _optionIsRead.Add(isRead);
                    _optionLabels[i].text = isRead ? "✓ " + optText : optText;

                    RectTransform rt = _optionButtons[i].GetComponent<RectTransform>();
                    float yMin = 1.0f - (i + 1) * normH + 0.03f;
                    float yMax = 1.0f - i * normH - 0.03f;
                    rt.anchorMin = new Vector2(0.05f, yMin);
                    rt.anchorMax = new Vector2(0.95f, yMax);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
                else
                {
                    _optionButtons[i].gameObject.SetActive(false);
                }
            }
        }

        public void HideOptions()
        {
            IsOptionsVisible = false;
            if (_optionsPanel != null)
            {
                _optionsPanel.gameObject.SetActive(false);
            }
        }

        public void SetBackground(string bgId, string variant)
        {
            CurrentBackground = bgId;
            if (_bgImage != null)
            {
                // 若有背景色設定或資源載入
                _bgImage.gameObject.SetActive(true);
            }
        }

        public void SetCharacterVisible(string characterId, bool visible)
        {
            if (string.IsNullOrEmpty(characterId) || _charContainer == null) return;

            if (!_charImages.TryGetValue(characterId, out Image img))
            {
                if (!visible) return;
                GameObject cGo = ProceduralUiKit.CreateUiObject("Char_" + characterId, _charContainer);
                RectTransform cRt = cGo.GetComponent<RectTransform>();
                cRt.anchorMin = new Vector2(0.25f, 0.05f);
                cRt.anchorMax = new Vector2(0.75f, 0.95f);
                cRt.offsetMin = Vector2.zero;
                cRt.offsetMax = Vector2.zero;
                img = cGo.AddComponent<Image>();
                img.color = Color.white;
                _charImages[characterId] = img;
            }

            img.gameObject.SetActive(visible);
        }

        public void SetCharacterExpression(string characterId, string expression)
        {
            Live2DPortraitView l2d = GetLive2DView(characterId);
            if (l2d != null)
            {
                l2d.SetExpression(expression);
            }
        }

        public void SetCharacterMotion(string characterId, string motion)
        {
            Live2DPortraitView l2d = GetLive2DView(characterId);
            if (l2d != null)
            {
                l2d.PlayMotion(motion);
            }
        }

        public void SetCg(string cgId)
        {
            CurrentCg = cgId;
            if (_cgImage != null)
            {
                bool hasCg = !string.IsNullOrEmpty(cgId);
                _cgImage.gameObject.SetActive(hasCg);
            }
        }

        public void SetUiVisible(bool visible)
        {
            IsUiVisible = visible;
            if (_dialoguePanel != null) _dialoguePanel.gameObject.SetActive(visible);
            if (_optionsPanel != null && IsOptionsVisible) _optionsPanel.gameObject.SetActive(visible);
        }

        public void UpdateBacklog(IReadOnlyList<BacklogEntry> history)
        {
            if (_backlogContentText == null || history == null) return;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                if (!string.IsNullOrEmpty(entry.Speaker))
                {
                    sb.AppendLine($"【{entry.Speaker}】");
                }
                sb.AppendLine(entry.TextKey);
                sb.AppendLine();
            }
            _backlogContentText.text = sb.ToString();
        }

        public void PlayBgm(string bgmId) { }
        public void PlaySe(string seId) { }
        public void PlayVoice(string voiceId)
        {
            if (GameServices.Audio != null && !string.IsNullOrEmpty(voiceId))
            {
                GameServices.Audio.PlayVoice(voiceId);
            }
            if (_activeLive2DView != null)
            {
                _activeLive2DView.SetLipSync(!string.IsNullOrEmpty(voiceId) ? 0.85f : 0f);
            }
        }

        public void ShowError(string message)
        {
            LastError = message;
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
