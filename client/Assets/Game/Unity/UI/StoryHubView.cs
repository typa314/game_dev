#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Core.Gallery;
using Game.Core.Story;
using Game.Domain;
using Game.Domain.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 劇情中心、CG 圖鑑與回憶紀錄畫面 (OPT-6 / ARCH-04 / ARCH-07)。
    /// 整合主線序章、角色 1～5 章好感路線進度 (CharacterRouteService)、
    /// 分級過濾 CG 圖鑑 (GalleryService)、以及多結局解鎖紀錄 (PlayerState.Endings)。
    /// </summary>
    public sealed class StoryHubView : UnityScreenBase
    {
        private GameCore _core;
        private GalleryService _galleryService;

        // 分頁索引: 0=主線, 1=角色路線, 2=CG圖鑑, 3=結局記錄
        public const int TAB_MAIN = 0;
        public const int TAB_ROUTES = 1;
        public const int TAB_GALLERY = 2;
        public const int TAB_ENDINGS = 3;

        private int _currentTab = TAB_MAIN;
        private Button[] _tabButtons = new Button[4];
        private RectTransform[] _tabPanels = new RectTransform[4];

        // CG 全螢幕預覽彈窗
        private GameObject _cgPreviewModal;
        private Image _cgPreviewImage;
        private Text _cgPreviewTitle;
        private Button _cgPreviewCloseBtn;

        // 分支拓樸圖彈窗 (M3-6)
        private GameObject _branchGraphModal;
        private Text _branchGraphTitle;
        private Text _branchGraphContentText;
        private Button _branchGraphCloseBtn;

        // 測試與檢核屬性
        public int CurrentTab => _currentTab;
        public GalleryService Gallery => _galleryService;
        public bool IsCgPreviewOpen => _cgPreviewModal != null && _cgPreviewModal.activeSelf;
        public bool IsBranchGraphModalOpen => _branchGraphModal != null && _branchGraphModal.activeSelf;
        public StoryBranchGraph CurrentBranchGraph { get; private set; }
        public string LastPreviewedCg { get; private set; }
        public string LastError { get; private set; }
        public override string ScreenId => "StoryHubUI";

        public void Initialize(GameCore core, GalleryService galleryService = null)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _galleryService = galleryService ?? new GalleryService();

            EnsureUiHierarchy();
            Refresh();
        }

        public void InitializeForTesting(GameCore core, GalleryService galleryService = null)
        {
            Initialize(core, galleryService);
        }

        private void Awake()
        {
            EnsureUiHierarchy();
        }

        private void EnsureUiHierarchy()
        {
            if (_tabPanels[0] != null) return;

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

            // 背景底圖
            GameObject bgGo = ProceduralUiKit.CreateUiObject("Background", transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(bgRt);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.06f, 0.08f, 0.12f, 1f);

            // 頂部導航欄 (標題 + 返回按鈕)
            BuildTopBar();

            // 分頁標籤切換欄
            BuildTabBar();

            // 四大分頁內容容器
            for (int i = 0; i < 4; i++)
            {
                string tabName = i switch
                {
                    TAB_MAIN => "MainStoryPanel",
                    TAB_ROUTES => "RoutesPanel",
                    TAB_GALLERY => "GalleryPanel",
                    TAB_ENDINGS => "EndingsPanel",
                    _ => "Panel_" + i
                };

                GameObject pGo = ProceduralUiKit.CreateUiObject(tabName, transform);
                RectTransform pRt = pGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0.04f, 0.05f);
                pRt.anchorMax = new Vector2(0.96f, 0.82f);
                pRt.offsetMin = Vector2.zero;
                pRt.offsetMax = Vector2.zero;
                _tabPanels[i] = pRt;
                pGo.SetActive(i == 0);
            }

            // CG 預覽彈窗
            BuildCgPreviewModal();

            // 分支拓樸圖檢視彈窗 (M3-6)
            BuildBranchGraphModal();
        }

        private void BuildTopBar()
        {
            GameObject barGo = ProceduralUiKit.CreateUiObject("TopBar", transform);
            RectTransform barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0.04f, 0.91f);
            barRt.anchorMax = new Vector2(0.96f, 0.98f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            // 返回大廳按鈕
            GameObject backBtnGo = ProceduralUiKit.CreateUiObject("BackBtn", barRt);
            RectTransform backRt = backBtnGo.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.0f, 0.1f);
            backRt.anchorMax = new Vector2(0.12f, 0.9f);
            backRt.offsetMin = Vector2.zero;
            backRt.offsetMax = Vector2.zero;

            Image backImg = backBtnGo.AddComponent<Image>();
            backImg.color = new Color(0.18f, 0.24f, 0.35f, 0.95f);
            Button backBtn = backBtnGo.AddComponent<Button>();
            backBtn.onClick.AddListener(() => GameServices.Navigator?.Pop());

            CreateLabel(backRt, "Lbl", "◀ 返回大廳", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            // 標題
            CreateLabel(barRt, "Title", "ARCHIVES & MEMORIES // 劇情圖鑑與回憶紀錄", new Vector2(0.15f, 0.0f), new Vector2(0.85f, 1.0f), 20, new Color(0.95f, 0.85f, 0.45f), TextAnchor.MiddleLeft);
        }

        private void BuildTabBar()
        {
            GameObject tabBarGo = ProceduralUiKit.CreateUiObject("TabBar", transform);
            RectTransform tabRt = tabBarGo.GetComponent<RectTransform>();
            tabRt.anchorMin = new Vector2(0.04f, 0.84f);
            tabRt.anchorMax = new Vector2(0.96f, 0.90f);
            tabRt.offsetMin = Vector2.zero;
            tabRt.offsetMax = Vector2.zero;

            string[] tabNames = new string[] { "📖 主線序章", "👥 角色路線", "🖼️ CG 圖鑑", "🏆 結局紀錄" };
            float normW = 1.0f / tabNames.Length;

            for (int i = 0; i < tabNames.Length; i++)
            {
                int tabIdx = i;
                float xMin = i * normW;
                float xMax = (i + 1) * normW - 0.01f;

                GameObject tGo = ProceduralUiKit.CreateUiObject("Tab_" + i, tabRt);
                RectTransform tRt = tGo.GetComponent<RectTransform>();
                tRt.anchorMin = new Vector2(xMin, 0.0f);
                tRt.anchorMax = new Vector2(xMax, 1.0f);
                tRt.offsetMin = Vector2.zero;
                tRt.offsetMax = Vector2.zero;

                Image tImg = tGo.AddComponent<Image>();
                tImg.color = i == 0 ? new Color(0.20f, 0.35f, 0.60f, 1f) : new Color(0.10f, 0.15f, 0.22f, 0.9f);
                Button btn = tGo.AddComponent<Button>();
                btn.onClick.AddListener(() => SwitchTab(tabIdx));

                CreateLabel(tRt, "Label", tabNames[i], Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);
                _tabButtons[i] = btn;
            }
        }

        private void BuildCgPreviewModal()
        {
            _cgPreviewModal = ProceduralUiKit.CreateUiObject("CgPreviewModal", transform);
            RectTransform mRt = _cgPreviewModal.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(mRt);

            Image mBg = _cgPreviewModal.AddComponent<Image>();
            mBg.color = new Color(0f, 0f, 0f, 0.95f);

            // CG 圖片展示
            GameObject imgGo = ProceduralUiKit.CreateUiObject("CgImage", _cgPreviewModal.transform);
            RectTransform imgRt = imgGo.GetComponent<RectTransform>();
            imgRt.anchorMin = new Vector2(0.08f, 0.08f);
            imgRt.anchorMax = new Vector2(0.92f, 0.90f);
            imgRt.offsetMin = Vector2.zero;
            imgRt.offsetMax = Vector2.zero;
            _cgPreviewImage = imgGo.AddComponent<Image>();
            _cgPreviewImage.color = Color.white;

            // 標題
            _cgPreviewTitle = CreateLabel(_cgPreviewModal.transform, "Title", "CG PREVIEW", new Vector2(0.08f, 0.92f), new Vector2(0.70f, 0.98f), 18, Color.white, TextAnchor.MiddleLeft);

            // 關閉按鈕
            GameObject closeGo = ProceduralUiKit.CreateUiObject("CloseBtn", _cgPreviewModal.transform);
            RectTransform cRt = closeGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.85f, 0.92f);
            cRt.anchorMax = new Vector2(0.95f, 0.98f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            Image cImg = closeGo.AddComponent<Image>();
            cImg.color = new Color(0.7f, 0.2f, 0.2f, 1f);
            _cgPreviewCloseBtn = closeGo.AddComponent<Button>();
            _cgPreviewCloseBtn.onClick.AddListener(CloseCgPreview);
            CreateLabel(cRt, "Lbl", "關閉", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            _cgPreviewModal.SetActive(false);
        }

        private void BuildBranchGraphModal()
        {
            _branchGraphModal = ProceduralUiKit.CreateUiObject("BranchGraphModal", transform);
            RectTransform mRt = _branchGraphModal.GetComponent<RectTransform>();
            mRt.anchorMin = new Vector2(0.10f, 0.10f);
            mRt.anchorMax = new Vector2(0.90f, 0.90f);
            mRt.offsetMin = Vector2.zero;
            mRt.offsetMax = Vector2.zero;

            Image mImg = _branchGraphModal.AddComponent<Image>();
            mImg.color = new Color(0.04f, 0.06f, 0.12f, 0.98f);

            _branchGraphTitle = CreateLabel(_branchGraphModal.transform, "Title", "BRANCH FLOWCHART (劇本分支拓樸)", new Vector2(0.05f, 0.90f), new Vector2(0.80f, 0.98f), 18, new Color(0.4f, 0.85f, 0.95f), TextAnchor.MiddleLeft);

            _branchGraphContentText = CreateLabel(_branchGraphModal.transform, "Content", "", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 14, Color.white, TextAnchor.UpperLeft);

            // 關閉按鈕
            GameObject closeGo = ProceduralUiKit.CreateUiObject("CloseBtn", _branchGraphModal.transform);
            RectTransform cRt = closeGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.85f, 0.91f);
            cRt.anchorMax = new Vector2(0.96f, 0.97f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            Image cImg = closeGo.AddComponent<Image>();
            cImg.color = new Color(0.7f, 0.2f, 0.2f, 1f);
            _branchGraphCloseBtn = closeGo.AddComponent<Button>();
            _branchGraphCloseBtn.onClick.AddListener(CloseBranchGraphModal);
            CreateLabel(cRt, "Lbl", "關閉", Vector2.zero, Vector2.one, 12, Color.white, TextAnchor.MiddleCenter);

            _branchGraphModal.SetActive(false);
        }

        public void OpenBranchGraphModal(string scriptText = null)
        {
            if (_branchGraphModal == null) return;
            if (string.IsNullOrEmpty(scriptText))
            {
                scriptText = TryReadStoryScriptText("prologue.txt");
            }

            var readNodes = _core?.Player?.Story?.ReadNodes;
            var endings = _core?.Player?.Endings;
            CurrentBranchGraph = StoryBranchGraphBuilder.Build(scriptText, readNodes, endings);

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

            _branchGraphModal.SetActive(true);
        }

        public void CloseBranchGraphModal()
        {
            if (_branchGraphModal != null)
            {
                _branchGraphModal.SetActive(false);
            }
        }


        // =========================================================================================
        // 分頁切換與內容刷新
        // =========================================================================================

        public void SwitchTab(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= 4) return;
            _currentTab = tabIndex;

            for (int i = 0; i < 4; i++)
            {
                bool active = (i == _currentTab);
                if (_tabPanels[i] != null) _tabPanels[i].gameObject.SetActive(active);
                if (_tabButtons[i] != null)
                {
                    Image img = _tabButtons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = active ? new Color(0.20f, 0.35f, 0.60f, 1f) : new Color(0.10f, 0.15f, 0.22f, 0.9f);
                    }
                }
            }

            Refresh();
        }

        public void Refresh()
        {
            if (_core == null) return;

            switch (_currentTab)
            {
                case TAB_MAIN:
                    PopulateMainStory();
                    break;
                case TAB_ROUTES:
                    PopulateCharacterRoutes();
                    break;
                case TAB_GALLERY:
                    PopulateGallery();
                    break;
                case TAB_ENDINGS:
                    PopulateEndings();
                    break;
            }
        }

        private void PopulateMainStory()
        {
            RectTransform p = _tabPanels[TAB_MAIN];
            ClearChildren(p);

            GameObject cardGo = ProceduralUiKit.CreateUiObject("PrologueCard", p);
            RectTransform rt = cardGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.60f);
            rt.anchorMax = new Vector2(0.95f, 0.95f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = cardGo.AddComponent<Image>();
            img.color = new Color(0.12f, 0.16f, 0.24f, 0.9f);

            CreateLabel(rt, "Title", "PROLOGUE // 星塔啟航：命運的召喚", new Vector2(0.04f, 0.60f), new Vector2(0.70f, 0.90f), 18, new Color(0.95f, 0.85f, 0.4f), TextAnchor.MiddleLeft);
            CreateLabel(rt, "Desc", "少女與少年於末日之際相遇，星辰運轉的齒輪在此刻悄然啟動...", new Vector2(0.04f, 0.15f), new Vector2(0.70f, 0.55f), 14, Color.white, TextAnchor.MiddleLeft);

            // 分支圖按鈕 (M3-6)
            GameObject graphBtnGo = ProceduralUiKit.CreateUiObject("GraphBtn", rt);
            RectTransform gRt = graphBtnGo.GetComponent<RectTransform>();
            gRt.anchorMin = new Vector2(0.58f, 0.25f);
            gRt.anchorMax = new Vector2(0.76f, 0.75f);
            gRt.offsetMin = Vector2.zero;
            gRt.offsetMax = Vector2.zero;
            Image gImg = graphBtnGo.AddComponent<Image>();
            gImg.color = new Color(0.20f, 0.40f, 0.65f, 1f);
            Button gBtn = graphBtnGo.AddComponent<Button>();
            gBtn.onClick.AddListener(() => OpenBranchGraphModal(null));
            CreateLabel(gRt, "Lbl", "📊 分支圖", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            // 播放按鈕
            GameObject playBtnGo = ProceduralUiKit.CreateUiObject("PlayBtn", rt);
            RectTransform bRt = playBtnGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.78f, 0.25f);
            bRt.anchorMax = new Vector2(0.95f, 0.75f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            Image bImg = playBtnGo.AddComponent<Image>();
            bImg.color = new Color(0.15f, 0.55f, 0.35f, 1f);
            Button btn = playBtnGo.AddComponent<Button>();
            btn.onClick.AddListener(PlayPrologue);
            CreateLabel(bRt, "Lbl", "▶ 開始播放", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);
        }

        public void PlayPrologue()
        {
            if (_core == null) return;

            var playerView = FindAnyObjectByType<StoryPlayerView>(FindObjectsInactive.Include);
            if (playerView == null)
            {
                var go = new GameObject("StoryPlayerUI");
                playerView = go.AddComponent<StoryPlayerView>();
            }

            string scriptText = null;
            string p1 = System.IO.Path.Combine(Application.streamingAssetsPath, "Story", "prologue.txt");
            string p2 = System.IO.Path.Combine(Application.dataPath, "..", "..", "data", "story", "prologue.txt");
            if (System.IO.File.Exists(p1))
            {
                scriptText = System.IO.File.ReadAllText(p1);
            }
            else if (System.IO.File.Exists(p2))
            {
                scriptText = System.IO.File.ReadAllText(p2);
            }
            else
            {
                scriptText = "[node Start]\nline l1 Claire welcome_to_command_center\nend\n";
            }

            var script = Game.Core.Story.MiniScript.MiniScript.Parse(scriptText);
            var runner = new Game.Core.Story.MiniScript.MiniScriptRunner(script);
            var controller = new StoryPlaybackController(runner, _core);
            var presenter = new StoryPresenter(controller);
            StoryCommandRegistry.Register(runner, _core, presenter);

            playerView.Initialize(presenter, GameServices.StorySaves);
            if (GameServices.Navigator != null)
            {
                GameServices.Navigator.Push(playerView);
            }
            else
            {
                playerView.gameObject.SetActive(true);
            }
            presenter.Start("Start");
        }

        private void PopulateCharacterRoutes()
        {
            RectTransform p = _tabPanels[TAB_ROUTES];
            ClearChildren(p);

            var chars = _core.Player?.Characters;
            if (chars == null || chars.Count == 0)
            {
                CreateLabel(p, "Empty", "尚未持有任何角色，無法解鎖角色專屬路線", new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f), 16, Color.gray, TextAnchor.MiddleCenter);
                return;
            }

            int index = 0;
            float rowHeightNorm = 0.22f;
            foreach (var kvp in chars)
            {
                string charId = kvp.Key;
                CharacterState cs = kvp.Value;
                int affLevel = _core.Growth.AffectionLevel(cs.Affection);

                float yMax = 1.0f - index * (rowHeightNorm + 0.02f);
                float yMin = yMax - rowHeightNorm;
                if (yMin < 0f) break;

                GameObject rowGo = ProceduralUiKit.CreateUiObject("Row_" + charId, p);
                RectTransform rRt = rowGo.GetComponent<RectTransform>();
                rRt.anchorMin = new Vector2(0.02f, yMin);
                rRt.anchorMax = new Vector2(0.98f, yMax);
                rRt.offsetMin = Vector2.zero;
                rRt.offsetMax = Vector2.zero;

                Image rImg = rowGo.AddComponent<Image>();
                rImg.color = new Color(0.10f, 0.14f, 0.20f, 0.9f);

                string charName = _core.Master?.Characters != null && _core.Master.Characters.TryGetValue(charId, out var def) ? (def.Name ?? charId) : charId;
                CreateLabel(rRt, "CharTitle", $"{charName} (好感等級 Lv.{affLevel} / {cs.Affection} pts)", new Vector2(0.02f, 0.60f), new Vector2(0.40f, 0.95f), 15, new Color(0.9f, 0.8f, 0.4f), TextAnchor.MiddleLeft);

                // 顯示 1～5 章狀態
                for (int ch = 1; ch <= CharacterRouteService.MaxChapters; ch++)
                {
                    bool isUnlocked = CharacterRouteService.IsChapterUnlocked(_core.Player, charId, ch, _core.Growth);
                    string chKey = CharacterRouteService.GetChapterKey(charId, ch);
                    bool isCompleted = _core.Player.Story.CompletedChapters.Contains(chKey);

                    float xMin = 0.42f + (ch - 1) * 0.11f;
                    float xMax = xMin + 0.10f;

                    GameObject chGo = ProceduralUiKit.CreateUiObject("Ch_" + ch, rRt);
                    RectTransform cRt = chGo.GetComponent<RectTransform>();
                    cRt.anchorMin = new Vector2(xMin, 0.15f);
                    cRt.anchorMax = new Vector2(xMax, 0.85f);
                    cRt.offsetMin = Vector2.zero;
                    cRt.offsetMax = Vector2.zero;

                    Image cImg = chGo.AddComponent<Image>();
                    cImg.color = isCompleted ? new Color(0.18f, 0.45f, 0.25f, 1f) : (isUnlocked ? new Color(0.20f, 0.35f, 0.55f, 1f) : new Color(0.08f, 0.10f, 0.14f, 0.8f));

                    Button chBtn = chGo.AddComponent<Button>();
                    chBtn.interactable = isUnlocked || isCompleted;
                    string routeCharId = charId;
                    int routeChapter = ch;
                    chBtn.onClick.AddListener(() => PlayCharacterRouteChapter(routeCharId, routeChapter));

                    string statusTxt = isCompleted ? $"第{ch}章\n[已通關]" : (isUnlocked ? $"第{ch}章\n[可挑戰]" : $"第{ch}章\n[🔒 Lv.{ch}]");
                    CreateLabel(cRt, "Lbl", statusTxt, Vector2.zero, Vector2.one, 11, isUnlocked ? Color.white : Color.gray, TextAnchor.MiddleCenter);
                }

                index++;
            }
        }

        public bool PlayCharacterRouteChapter(string charId, int chapter, StoryPlayerView targetPlayer = null)
        {
            if (_core == null || string.IsNullOrEmpty(charId)) return false;

            bool isUnlocked = CharacterRouteService.IsChapterUnlocked(_core.Player, charId, chapter, _core.Growth);
            string chKey = CharacterRouteService.GetChapterKey(charId, chapter);
            bool isCompleted = _core.Player.Story.CompletedChapters.Contains(chKey);

            if (!isUnlocked && !isCompleted)
            {
                LastError = "chapter_locked";
                return false;
            }

            string storyRoute = null;
            if (_core.Master != null && _core.Master.Characters != null && _core.Master.Characters.TryGetValue(charId, out var routeDef))
                storyRoute = routeDef.StoryRoute;
            string fileName = RouteScriptNames.FileName(charId, chapter, storyRoute);
            string scriptText = TryReadStoryScriptText(fileName);
            if (string.IsNullOrEmpty(scriptText))
            {
                scriptText = $"[node Start]\nline l1 {charId} route_dialogue\nend\n";
            }

            StoryPlayerView playerView = targetPlayer ?? FindAnyObjectByType<StoryPlayerView>(FindObjectsInactive.Include);
            if (playerView == null)
            {
                var go = new GameObject("StoryPlayerUI");
                playerView = go.AddComponent<StoryPlayerView>();
            }

            var script = Game.Core.Story.MiniScript.MiniScript.Parse(scriptText);
            return playerView.LoadAndPlay(
                fileName,
                script,
                _core,
                isRecollection: isCompleted,
                onCompleted: () =>
                {
                    if (!isCompleted)
                    {
                        CharacterRouteService.CompleteChapter(_core.Player, charId, chapter, _core.Growth);
                        Refresh();
                    }
                });
        }

        public static string TryReadStoryScriptText(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            try
            {
                string[] candidates = new[]
                {
                    System.IO.Path.Combine(Application.streamingAssetsPath, "Story", fileName),
                    System.IO.Path.Combine(Application.dataPath, "..", "..", "data", "story", fileName),
                    System.IO.Path.Combine("data", "story", fileName),
                    System.IO.Path.Combine("..", "data", "story", fileName),
                    System.IO.Path.Combine("..", "..", "data", "story", fileName),
                    System.IO.Path.Combine("client", "Assets", "StreamingAssets", "Story", fileName)
                };
                foreach (var p in candidates)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                            return System.IO.File.ReadAllText(p);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[StoryHubView] 讀取劇本檔案失敗 ({p}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StoryHubView] 解析劇本路徑失敗 ({fileName}): {ex.Message}");
            }
            return null;
        }

        private void PopulateGallery()
        {
            RectTransform p = _tabPanels[TAB_GALLERY];
            ClearChildren(p);

            var grouped = _galleryService.GetGroupedGallery(_core.Player, _core.Rating);
            if (grouped.Count == 0)
            {
                CreateLabel(p, "Empty", "目前分級下尚無解鎖之 CG 圖鑑", new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f), 16, Color.gray, TextAnchor.MiddleCenter);
                return;
            }

            int itemIdx = 0;
            int cols = 4;
            float colW = 1.0f / cols;
            float rowH = 0.30f;

            foreach (var kvp in grouped)
            {
                foreach (var gItem in kvp.Value)
                {
                    int r = itemIdx / cols;
                    int c = itemIdx % cols;

                    float xMin = c * colW + 0.01f;
                    float xMax = (c + 1) * colW - 0.01f;
                    float yMax = 1.0f - r * (rowH + 0.03f);
                    float yMin = yMax - rowH;
                    if (yMin < 0f) break;

                    GameObject gGo = ProceduralUiKit.CreateUiObject("CgItem_" + gItem.Def.Id, p);
                    RectTransform gRt = gGo.GetComponent<RectTransform>();
                    gRt.anchorMin = new Vector2(xMin, yMin);
                    gRt.anchorMax = new Vector2(xMax, yMax);
                    gRt.offsetMin = Vector2.zero;
                    gRt.offsetMax = Vector2.zero;

                    Image gImg = gGo.AddComponent<Image>();
                    gImg.color = gItem.IsUnlocked ? new Color(0.15f, 0.22f, 0.35f, 1f) : new Color(0.08f, 0.10f, 0.15f, 0.8f);

                    Button btn = gGo.AddComponent<Button>();
                    if (gItem.IsUnlocked)
                    {
                        string id = gItem.Def.Id;
                        btn.onClick.AddListener(() => PreviewCg(id));
                    }

                    string labelText = gItem.IsUnlocked ? gItem.Def.Title : "🔒 [未解鎖]";
                    CreateLabel(gRt, "Lbl", labelText, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.30f), 12, gItem.IsUnlocked ? Color.white : Color.gray, TextAnchor.MiddleCenter);

                    itemIdx++;
                }
            }
        }

        private void PopulateEndings()
        {
            RectTransform p = _tabPanels[TAB_ENDINGS];
            ClearChildren(p);

            var endings = _core.Player?.Endings;
            if (endings == null || endings.Count == 0)
            {
                CreateLabel(p, "Empty", "尚未達成任何劇情結局", new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f), 16, Color.gray, TextAnchor.MiddleCenter);
                return;
            }

            int index = 0;
            float rowHeight = 0.15f;
            foreach (var kvp in endings)
            {
                float yMax = 1.0f - index * (rowHeight + 0.02f);
                float yMin = yMax - rowHeight;
                if (yMin < 0f) break;

                GameObject eGo = ProceduralUiKit.CreateUiObject("Ending_" + kvp.Key, p);
                RectTransform eRt = eGo.GetComponent<RectTransform>();
                eRt.anchorMin = new Vector2(0.05f, yMin);
                eRt.anchorMax = new Vector2(0.95f, yMax);
                eRt.offsetMin = Vector2.zero;
                eRt.offsetMax = Vector2.zero;

                Image eImg = eGo.AddComponent<Image>();
                eImg.color = new Color(0.12f, 0.16f, 0.22f, 0.9f);

                CreateLabel(eRt, "Title", $"🏆 結局達成: 【{kvp.Key}】", new Vector2(0.04f, 0.50f), new Vector2(0.70f, 0.90f), 16, new Color(0.95f, 0.85f, 0.35f), TextAnchor.MiddleLeft);
                CreateLabel(eRt, "Time", $"達成時間: {kvp.Value}", new Vector2(0.04f, 0.10f), new Vector2(0.70f, 0.45f), 13, new Color(0.7f, 0.8f, 0.9f), TextAnchor.MiddleLeft);

                index++;
            }
        }

        public void PreviewCg(string cgId)
        {
            LastPreviewedCg = cgId;
            if (_cgPreviewModal != null)
            {
                _cgPreviewModal.SetActive(true);
                if (_cgPreviewTitle != null)
                {
                    _cgPreviewTitle.text = "CG PREVIEW // " + cgId;
                }
            }
        }

        public void CloseCgPreview()
        {
            if (_cgPreviewModal != null)
            {
                _cgPreviewModal.SetActive(false);
            }
        }

        public override bool OnBackRequested()
        {
            if (IsBranchGraphModalOpen)
            {
                CloseBranchGraphModal();
                return true;
            }
            if (IsCgPreviewOpen)
            {
                CloseCgPreview();
                return true;
            }
            return false;
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
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
