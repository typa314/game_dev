using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 戰術衣櫥與秘書官設定系統 (OPT-5, ARCH-04)
    /// 支援角色切換、已解鎖立繪階段 (S1~S3) 切換、服裝穿戴、大廳場景選擇，
    /// 以及「📌 設為大廳秘書官」之全域連動。
    /// </summary>
    public sealed class WardrobeView : UnityScreenBase, IWardrobeView
    {
        public override string ScreenId => "Screen_Wardrobe";

        private WardrobePresenter _wardrobePresenter;
        private GameCore _core;

        // UI 組件
        private GameObject _canvasRoot;
        private Transform _charListContent;
        private Transform _stageButtonsContainer;
        private Transform _costumeButtonsContainer;
        private Text _charTitleLabel;
        private Text _sceneStatusLabel;
        private Text _messageLabel;
        private Image _portraitImage;
        private Button _backButton;
        private Button _setLobbyButton;

        private readonly Dictionary<string, Button> _charCardButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _stageButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _costumeButtons = new Dictionary<string, Button>();
        private readonly List<string> _currentUnlockedStages = new List<string>();
        private readonly List<string> _currentUnlockedCostumes = new List<string>();

        // 當前狀態與測試暴露屬性
        public string SelectedCharId { get; private set; }
        public string SelectedStageId { get; private set; }
        public string SelectedCostumeId { get; private set; }
        public string SelectedSceneId { get; private set; } = "scene_lobby_default";
        public string SelectedTimeOfDay { get; private set; } = "morning";
        public string CurrentArtKey { get; private set; }
        public string LastMessage { get; private set; }
        public string LastError { get; private set; }

        public Button SetLobbyButton => _setLobbyButton;
        public IReadOnlyDictionary<string, Button> StageButtons => _stageButtons;
        public IReadOnlyDictionary<string, Button> CostumeButtons => _costumeButtons;

        private void Awake()
        {
            if (_canvasRoot == null) BuildUi();
        }

        public override void OnPush(object args)
        {
            base.OnPush(args);
            EnsurePresenter();
            if (_canvasRoot == null) BuildUi();
            if (_canvasRoot != null) _canvasRoot.SetActive(true);

            string initialChar = args as string;
            if (string.IsNullOrEmpty(initialChar))
            {
                initialChar = _core?.Player?.Lobby?.Character ?? "char01_claire";
            }

            RefreshCharacterList();
            SelectCharacter(initialChar);
        }

        public override void OnResume()
        {
            base.OnResume();
            RefreshCharacterList();
            if (!string.IsNullOrEmpty(SelectedCharId))
            {
                SelectCharacter(SelectedCharId);
            }
        }

        public override void OnPop()
        {
            base.OnPop();
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
        }

        public override bool OnBackRequested()
        {
            return false;
        }

        public void Close()
        {
            if (GameServices.Navigator != null && GameServices.Navigator.CurrentScreen != null && ReferenceEquals(GameServices.Navigator.CurrentScreen, this))
            {
                GameServices.Navigator.Pop();
            }
            else
            {
                if (_canvasRoot != null) _canvasRoot.SetActive(false);
            }
        }

        public void InitializeForTesting(GameCore core = null, WardrobePresenter presenter = null)
        {
            _core = core ?? GameServices.Core;
            _wardrobePresenter = presenter ?? new WardrobePresenter(_core);
            _wardrobePresenter.AttachView(this);
            if (_canvasRoot == null) BuildUi();
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
            RefreshCharacterList();
        }

        private void EnsurePresenter()
        {
            if (_wardrobePresenter == null)
            {
                _core = GameServices.Core;
                if (_core != null)
                {
                    _wardrobePresenter = new WardrobePresenter(_core);
                    _wardrobePresenter.AttachView(this);
                }
            }
        }

        public void SelectCharacter(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return;
            SelectedCharId = charId;
            EnsurePresenter();
            if (_wardrobePresenter == null) return;

            _wardrobePresenter.SelectCharacter(charId);
            HighlightSelectedCharCard(charId);
        }

        public void SelectPortraitStage(string stageId)
        {
            if (string.IsNullOrEmpty(SelectedCharId) || string.IsNullOrEmpty(stageId)) return;
            EnsurePresenter();
            if (_wardrobePresenter == null) return;

            SelectedStageId = stageId;
            _wardrobePresenter.SwitchPortraitStage(SelectedCharId, stageId);
        }

        public Result EquipCostume(string costumeId)
        {
            if (string.IsNullOrEmpty(SelectedCharId)) return Result.Fail("no_character_selected");
            EnsurePresenter();
            if (_wardrobePresenter == null) return Result.Fail("no_presenter");

            SelectedCostumeId = costumeId;
            return _wardrobePresenter.Equip(SelectedCharId, costumeId);
        }

        public void SelectTimeOfDay(string timeOfDay)
        {
            if (string.IsNullOrEmpty(timeOfDay)) return;
            SelectedTimeOfDay = timeOfDay.ToLowerInvariant();

            TimeOfDay tod = SelectedTimeOfDay switch
            {
                "morning" => TimeOfDay.Morning,
                "noon" or "afternoon" or "day" => TimeOfDay.Afternoon,
                "evening" => TimeOfDay.Evening,
                "night" => TimeOfDay.Night,
                _ => TimeOfDay.Morning
            };

            var lobby = FindAnyObjectByType<LobbyView>(FindObjectsInactive.Include);
            if (lobby != null)
            {
                lobby.SetTimeOfDayOverride(tod);
            }
        }

        public void SelectScene(string sceneId)
        {
            SelectedSceneId = sceneId;
            if (_sceneStatusLabel != null)
            {
                _sceneStatusLabel.text = "SCENE: " + (sceneId == "scene_cafe" ? "午後咖啡館" : "天文台大廳");
            }
        }

        public void OnClickSetAsLobby()
        {
            EnsurePresenter();
            if (string.IsNullOrEmpty(SelectedCharId) || _wardrobePresenter == null) return;
            _wardrobePresenter.SetAsLobbySecretary(SelectedCharId, SelectedSceneId);
        }

        // ---- IWardrobeView 實作 ----

        public void DisplayCharacter(string charId, string currentArtKey, IReadOnlyList<string> unlockedStages, IReadOnlyList<string> costumes)
        {
            SelectedCharId = charId;
            CurrentArtKey = currentArtKey;
            _currentUnlockedStages.Clear();
            if (unlockedStages != null) _currentUnlockedStages.AddRange(unlockedStages);
            _currentUnlockedCostumes.Clear();
            if (costumes != null) _currentUnlockedCostumes.AddRange(costumes);

            CharacterDef def = null;
            if (_core?.Master?.Characters != null) _core.Master.Characters.TryGetValue(charId, out def);
            string charName = def != null ? (def.Name ?? def.Id) : charId;
            _charTitleLabel.text = charName.ToUpperInvariant();

            // 構建立繪階段按鈕 (S1, S2, S3)
            RebuildStageButtons(def);

            // 構建服裝穿戴按鈕 (M2-4)
            RebuildCostumeButtons(charId);

            // 載入立繪圖片
            TryLoadPortrait(charId, currentArtKey);
        }

        public void ShowSuccessMessage(string message)
        {
            LastMessage = message;
            LastError = null;
            if (_messageLabel != null)
            {
                _messageLabel.text = "✔ " + message;
                _messageLabel.color = new Color(0.3f, 0.95f, 0.5f);
            }
        }

        public void ShowError(string message)
        {
            LastError = message;
            if (_messageLabel != null)
            {
                _messageLabel.text = "⚠ " + message;
                _messageLabel.color = new Color(1f, 0.4f, 0.4f);
            }
        }

        // ---- UI 構建與刷新 ----

        private void BuildUi()
        {
            _canvasRoot = new GameObject("WardrobeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot.transform.SetParent(transform, false);
            Canvas canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // 1. 背景底板
            CreatePanel(_canvasRoot.transform, "Backdrop", Vector2.zero, Vector2.one, new Color(0.04f, 0.06f, 0.09f, 0.98f));

            // 2. 頂部導航列
            GameObject topBar = CreatePanel(_canvasRoot.transform, "TopBar", new Vector2(0f, 0.92f), new Vector2(1f, 1f), new Color(0.06f, 0.09f, 0.14f, 0.95f));
            _backButton = CreateButton(topBar.transform, "BackButton", "◀ 返回大廳", new Vector2(0.02f, 0.15f), new Vector2(0.12f, 0.85f), Close, new Color(0.18f, 0.28f, 0.38f));
            CreateLabel(topBar.transform, "Title", "◆ 戰術更衣室與秘書官設定 // WARDROBE & SUITE", new Vector2(0.15f, 0.15f), new Vector2(0.60f, 0.85f), 24, new Color(0.9f, 0.94f, 1f), TextAnchor.MiddleLeft);

            // 3. 左側角色清單 (ScrollView)
            GameObject rosterPanel = CreatePanel(_canvasRoot.transform, "RosterPanel", new Vector2(0.02f, 0.04f), new Vector2(0.24f, 0.90f), new Color(0.05f, 0.08f, 0.12f, 0.85f));
            CreateLabel(rosterPanel.transform, "Header", "SELECT HERO", new Vector2(0.05f, 0.93f), new Vector2(0.95f, 0.99f), 16, new Color(0.6f, 0.75f, 0.9f), TextAnchor.MiddleLeft);

            GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(rosterPanel.transform, false);
            RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.04f, 0.02f);
            scrollRt.anchorMax = new Vector2(0.96f, 0.92f);
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            RectTransform contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;

            ContentSizeFitter csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            sr.content = contentRt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            _charListContent = contentGo.transform;

            // 4. 中央大立繪展示區
            GameObject centerPanel = CreatePanel(_canvasRoot.transform, "CenterPanel", new Vector2(0.26f, 0.04f), new Vector2(0.64f, 0.90f), new Color(0.05f, 0.07f, 0.11f, 0.6f));
            GameObject portraitGo = CreatePanel(centerPanel.transform, "Portrait", new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.95f), new Color(0.1f, 0.15f, 0.22f, 0.5f));
            _portraitImage = portraitGo.GetComponent<Image>();
            _charTitleLabel = CreateLabel(centerPanel.transform, "CharTitle", "CLAIRE", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.08f), 28, Color.white, TextAnchor.MiddleCenter);

            // 5. 右側控制面板 (立繪階段、背景、設為秘書官)
            GameObject rightPanel = CreatePanel(_canvasRoot.transform, "RightPanel", new Vector2(0.66f, 0.04f), new Vector2(0.98f, 0.90f), new Color(0.05f, 0.08f, 0.13f, 0.95f));

            CreateLabel(rightPanel.transform, "StageHeader", "◆ 立繪階段切換 (PORTRAIT STAGES)", new Vector2(0.05f, 0.92f), new Vector2(0.95f, 0.98f), 18, new Color(0.4f, 0.85f, 1f), TextAnchor.MiddleLeft);
            GameObject stagesContainer = new GameObject("StagesContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            stagesContainer.transform.SetParent(rightPanel.transform, false);
            RectTransform stRt = stagesContainer.GetComponent<RectTransform>();
            stRt.anchorMin = new Vector2(0.05f, 0.65f);
            stRt.anchorMax = new Vector2(0.95f, 0.91f);
            stRt.offsetMin = stRt.offsetMax = Vector2.zero;
            VerticalLayoutGroup stVlg = stagesContainer.GetComponent<VerticalLayoutGroup>();
            stVlg.spacing = 8f;
            stVlg.childForceExpandHeight = false;
            stVlg.childForceExpandWidth = true;
            stVlg.childControlHeight = false;
            stVlg.childControlWidth = true;
            _stageButtonsContainer = stagesContainer.transform;

            // 大廳場景選擇
            CreateLabel(rightPanel.transform, "SceneHeader", "◆ 大廳場景選擇 (LOBBY SCENE)", new Vector2(0.05f, 0.57f), new Vector2(0.95f, 0.63f), 18, new Color(0.4f, 0.85f, 1f), TextAnchor.MiddleLeft);
            CreateButton(rightPanel.transform, "BtnScene1", "🌌 天文台大廳 (Observatory)", new Vector2(0.05f, 0.49f), new Vector2(0.95f, 0.56f), () => SelectScene("scene_lobby_default"), new Color(0.16f, 0.28f, 0.38f));
            CreateButton(rightPanel.transform, "BtnScene2", "☕ 午後咖啡館 (Afternoon Cafe)", new Vector2(0.05f, 0.40f), new Vector2(0.95f, 0.47f), () => SelectScene("scene_cafe"), new Color(0.24f, 0.28f, 0.22f));
            _sceneStatusLabel = CreateLabel(rightPanel.transform, "SceneStatus", "SCENE: 天文台大廳", new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.39f), 14, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleLeft);

            // M2-4: 服裝穿戴選擇區与時段切換按鈕
            GameObject costumesContainer = new GameObject("CostumesContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            costumesContainer.transform.SetParent(rightPanel.transform, false);
            RectTransform cosRt = costumesContainer.GetComponent<RectTransform>();
            cosRt.anchorMin = new Vector2(0.05f, 0.26f);
            cosRt.anchorMax = new Vector2(0.95f, 0.34f);
            cosRt.offsetMin = cosRt.offsetMax = Vector2.zero;
            VerticalLayoutGroup cosVlg = costumesContainer.GetComponent<VerticalLayoutGroup>();
            cosVlg.spacing = 4f;
            cosVlg.childForceExpandHeight = false;
            cosVlg.childForceExpandWidth = true;
            cosVlg.childControlHeight = false;
            cosVlg.childControlWidth = true;
            _costumeButtonsContainer = costumesContainer.transform;

            CreateButton(rightPanel.transform, "BtnTodMorning", "晨", new Vector2(0.05f, 0.21f), new Vector2(0.26f, 0.25f), () => SelectTimeOfDay("morning"), new Color(0.35f, 0.30f, 0.15f), 13);
            CreateButton(rightPanel.transform, "BtnTodNoon", "晝", new Vector2(0.28f, 0.21f), new Vector2(0.49f, 0.25f), () => SelectTimeOfDay("noon"), new Color(0.20f, 0.35f, 0.45f), 13);
            CreateButton(rightPanel.transform, "BtnTodEvening", "夕", new Vector2(0.51f, 0.21f), new Vector2(0.72f, 0.25f), () => SelectTimeOfDay("evening"), new Color(0.45f, 0.22f, 0.15f), 13);
            CreateButton(rightPanel.transform, "BtnTodNight", "夜", new Vector2(0.74f, 0.21f), new Vector2(0.95f, 0.25f), () => SelectTimeOfDay("night"), new Color(0.12f, 0.16f, 0.35f), 13);

            // 核心操作：設為大廳秘書官
            _setLobbyButton = CreateButton(rightPanel.transform, "BtnSetLobby", "📌 設為大廳秘書官 (APPLY SECRETARY)", new Vector2(0.05f, 0.11f), new Vector2(0.95f, 0.19f), OnClickSetAsLobby, new Color(0.82f, 0.49f, 0.16f), 18);

            // 提示訊息
            _messageLabel = CreateLabel(rightPanel.transform, "MessageLabel", "", new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.10f), 15, Color.green, TextAnchor.MiddleCenter);
        }

        public void RefreshCharacterList()
        {
            if (_wardrobePresenter == null || _charListContent == null) return;
            var all = _wardrobePresenter.GetAllCharacters();

            foreach (var def in all)
            {
                CharacterState state = _wardrobePresenter.GetCharacterState(def.Id);
                bool owned = state != null;

                Button btn;
                if (!_charCardButtons.TryGetValue(def.Id, out btn))
                {
                    btn = CreateCharButton(def);
                    _charCardButtons[def.Id] = btn;
                }
                btn.image.color = owned ? new Color(0.12f, 0.18f, 0.26f, 0.95f) : new Color(0.06f, 0.08f, 0.12f, 0.75f);
            }
        }

        private Button CreateCharButton(CharacterDef def)
        {
            GameObject card = new GameObject("Card_" + def.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            card.transform.SetParent(_charListContent, false);
            LayoutElement le = card.GetComponent<LayoutElement>();
            le.minHeight = 60f;
            le.preferredHeight = 60f;

            Image img = card.GetComponent<Image>();
            img.color = new Color(0.12f, 0.18f, 0.26f, 0.95f);

            Button btn = card.GetComponent<Button>();
            string charId = def.Id;
            btn.onClick.AddListener(() => SelectCharacter(charId));

            CreateLabel(card.transform, "Label", ShortName(def.Name ?? def.Id), new Vector2(0.08f, 0.1f), new Vector2(0.92f, 0.9f), 16, Color.white, TextAnchor.MiddleLeft);
            return btn;
        }

        private void HighlightSelectedCharCard(string charId)
        {
            foreach (var kvp in _charCardButtons)
            {
                bool isSelected = kvp.Key == charId;
                kvp.Value.transform.localScale = isSelected ? new Vector3(1.05f, 1.05f, 1f) : Vector3.one;
            }
        }

        private void RebuildStageButtons(CharacterDef def)
        {
            if (_stageButtonsContainer == null) return;

            // 清理舊按鈕
            foreach (Transform child in _stageButtonsContainer)
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            _stageButtons.Clear();

            List<string> stages = (def != null && def.PortraitStages != null && def.PortraitStages.Count > 0)
                ? def.PortraitStages
                : new List<string> { $"ps_{def?.Id}_s1", $"ps_{def?.Id}_s2", $"ps_{def?.Id}_s3" };

            int index = 1;
            foreach (string stId in stages)
            {
                string stageId = stId;
                bool unlocked = _currentUnlockedStages.Contains(stageId) || index == 1; // 預設 S1 一律開放

                GameObject btnGo = new GameObject("BtnStage_" + stageId, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                btnGo.transform.SetParent(_stageButtonsContainer, false);
                LayoutElement le = btnGo.GetComponent<LayoutElement>();
                le.minHeight = 44f;
                le.preferredHeight = 44f;

                Image img = btnGo.GetComponent<Image>();
                Color bgColor = unlocked ? new Color(0.15f, 0.26f, 0.38f) : new Color(0.08f, 0.10f, 0.14f);
                img.color = bgColor;

                Button btn = btnGo.GetComponent<Button>();
                btn.interactable = unlocked;
                btn.onClick.AddListener(() => SelectPortraitStage(stageId));

                string stageName = $"STAGE {index}" + (unlocked ? " [已解鎖]" : " [🔒 鎖定]");
                if (index == 2) stageName = "STAGE 2 (覺醒突破 Lv30 ★2)" + (unlocked ? " [已解鎖]" : " [🔒 鎖定]");
                else if (index == 3) stageName = "STAGE 3 (誓約花嫁)" + (unlocked ? " [已解鎖]" : " [🔒 鎖定]");

                CreateLabel(btnGo.transform, "Label", stageName, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), 14, unlocked ? Color.white : new Color(0.5f, 0.55f, 0.6f), TextAnchor.MiddleLeft);

                _stageButtons[stageId] = btn;
                index++;
            }
        }

        private void RebuildCostumeButtons(string charId)
        {
            if (_costumeButtonsContainer == null || _wardrobePresenter == null) return;

            foreach (Transform child in _costumeButtonsContainer)
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            _costumeButtons.Clear();

            var costumes = _wardrobePresenter.GetCostumesForCharacter(charId);
            foreach (var cos in costumes)
            {
                string cid = cos.Id;
                bool unlocked = _currentUnlockedCostumes.Contains(cid);

                GameObject btnGo = new GameObject("BtnCostume_" + cid, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                btnGo.transform.SetParent(_costumeButtonsContainer, false);
                LayoutElement le = btnGo.GetComponent<LayoutElement>();
                le.minHeight = 32f;
                le.preferredHeight = 32f;

                Image img = btnGo.GetComponent<Image>();
                img.color = unlocked ? new Color(0.22f, 0.18f, 0.35f) : new Color(0.08f, 0.10f, 0.14f);

                Button btn = btnGo.GetComponent<Button>();
                btn.interactable = unlocked;
                btn.onClick.AddListener(() => EquipCostume(cid));

                string label = $"👗 {cid}" + (unlocked ? " [可穿戴]" : " [🔒 未持有]");
                CreateLabel(btnGo.transform, "Label", label, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), 13, unlocked ? Color.white : Color.gray, TextAnchor.MiddleLeft);

                _costumeButtons[cid] = btn;
            }
        }

        private void TryLoadPortrait(string charId, string artKey)
        {
            if (_portraitImage == null) return;
            string path = Path.Combine(Application.streamingAssetsPath, "Portraits", $"{charId}.png");
            if (File.Exists(path))
            {
                byte[] data = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    _portraitImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    _portraitImage.color = Color.white;
                    return;
                }
            }
            _portraitImage.sprite = ProceduralUiKit.GetSilhouetteSprite(360, 500);
            _portraitImage.color = Color.white;
        }

        private static string ShortName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "HERO";
            return id.Replace("char", "").Replace("_", " ").Trim().ToUpperInvariant();
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static Text CreateLabel(Transform parent, string name, string value, Vector2 min, Vector2 max, int size, Color color, TextAnchor align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Text t = go.GetComponent<Text>();
            t.font = ProceduralUiKit.GetDefaultFont();
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = value;
            t.raycastTarget = false;
            return t;
        }

        private static Button CreateButton(Transform parent, string name, string text, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, Color color, int size = 16)
        {
            GameObject go = CreatePanel(parent, name, min, max, color);
            Button b = go.AddComponent<Button>();
            if (action != null) b.onClick.AddListener(action);
            CreateLabel(go.transform, "Label", text, Vector2.zero, Vector2.one, size, Color.white, TextAnchor.MiddleCenter);
            return b;
        }
    }
}
