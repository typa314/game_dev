using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Core.UI;
using Game.Domain.MasterData;
using Game.Domain.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 部隊名冊與角色養成系統 (OPT-5, ARCH-04)
    /// 包含左側角色列表滾動切換、中央立繪/徽章展示、右側屬性/技能詳情，
    /// 以及「升級」、「覺醒突破」與「同卡進階」三大核心養成操作。
    /// </summary>
    public sealed class CharacterRosterView : UnityScreenBase, ICharacterDetailView
    {
        public override string ScreenId => "Screen_CharacterRoster";

        private CharacterDetailPresenter _presenter;
        private GameCore _core;

        // UI 組件參照
        private GameObject _canvasRoot;
        private Transform _rosterContent;
        private Text _charNameTitle;
        private Text _rarityBadge;
        private Text _levelLabel;
        private Text _hpStatLabel;
        private Text _atkStatLabel;
        private Text _rangeStatLabel;
        private Text _costStatLabel;
        private Text _roleStatLabel;
        private Text _skillNameLabel;
        private Text _skillDescLabel;
        private Text _awakenStarsLabel;
        private Text _affectionLabel;
        private Text _messageLabel;
        private Image _portraitImage;
        private Button _levelUpButton;
        private Button _awakenButton;
        private Button _advanceButton;
        private Button _backButton;

        private readonly Dictionary<string, Button> _characterCardButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Text> _cardInfoTexts = new Dictionary<string, Text>();

        // 當前選取狀態與測試屬性
        public string SelectedCharId { get; private set; }
        public CharacterState CurrentCharacterState { get; private set; }
        public int CurrentMaxLevel { get; private set; }
        public string LastMessage { get; private set; }
        public string LastError { get; private set; }

        public Text LevelLabel => _levelLabel;
        public Text GradeLabel => _rarityBadge;
        public Text AwakenLabel => _awakenStarsLabel;
        public Button LevelUpButton => _levelUpButton;
        public Button AwakenButton => _awakenButton;
        public Button AdvanceButton => _advanceButton;

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
                var owned = _presenter.GetAllCharacters().FirstOrDefault(c => _presenter.GetCharacterState(c.Id) != null);
                initialChar = owned != null ? owned.Id : "char01_claire";
            }

            RefreshRosterList();
            SelectCharacter(initialChar);
        }

        public override void OnResume()
        {
            base.OnResume();
            RefreshRosterList();
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

        public void InitializeForTesting(GameCore core = null, CharacterDetailPresenter presenter = null)
        {
            _core = core ?? GameServices.Core;
            _presenter = presenter ?? new CharacterDetailPresenter(_core);
            _presenter.AttachView(this);
            if (_canvasRoot == null) BuildUi();
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
            RefreshRosterList();
        }

        private void EnsurePresenter()
        {
            if (_presenter == null)
            {
                _core = GameServices.Core;
                if (_core != null)
                {
                    _presenter = new CharacterDetailPresenter(_core);
                    _presenter.AttachView(this);
                }
            }
        }

        public void SelectCharacter(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return;
            SelectedCharId = charId;
            EnsurePresenter();
            if (_presenter == null) return;

            CharacterDef def = _presenter.GetCharacterDef(charId);
            CharacterState state = _presenter.GetCharacterState(charId);

            if (state != null)
            {
                _presenter.LoadCharacter(charId);
            }
            else
            {
                DisplayUnowned(def);
            }

            HighlightSelectedCard(charId);
        }

        public void RefreshRosterList()
        {
            if (_presenter == null || _rosterContent == null) return;
            var all = _presenter.GetAllCharacters();

            foreach (var def in all)
            {
                CharacterState state = _presenter.GetCharacterState(def.Id);
                bool owned = state != null;

                Button btn;
                if (!_characterCardButtons.TryGetValue(def.Id, out btn))
                {
                    btn = CreateCharacterCard(def);
                    _characterCardButtons[def.Id] = btn;
                }

                Text infoTxt;
                if (_cardInfoTexts.TryGetValue(def.Id, out infoTxt))
                {
                    string grade = owned ? state.DisplayGrade : def.Rarity;
                    string lvStr = owned ? $"Lv.{state.Level}" : "未招募";
                    infoTxt.text = $"[{grade}] {ShortName(def.Name ?? def.Id)}\n{lvStr}";
                    infoTxt.color = owned ? Color.white : new Color(0.6f, 0.65f, 0.7f, 0.8f);
                }

                btn.image.color = owned ? new Color(0.12f, 0.18f, 0.26f, 0.95f) : new Color(0.06f, 0.08f, 0.12f, 0.75f);
            }
        }

        private void HighlightSelectedCard(string charId)
        {
            foreach (var kvp in _characterCardButtons)
            {
                bool isSelected = kvp.Key == charId;
                kvp.Value.transform.localScale = isSelected ? new Vector3(1.05f, 1.05f, 1f) : Vector3.one;
            }
        }

        // ---- ICharacterDetailView 實作 ----

        public void DisplayDetail(CharacterState character, int maxLevel, string currentArtKey)
        {
            CurrentCharacterState = character;
            CurrentMaxLevel = maxLevel;

            CharacterDef def = _presenter.GetCharacterDef(SelectedCharId);
            string charName = def != null ? (def.Name ?? def.Id) : SelectedCharId;
            _charNameTitle.text = charName.ToUpperInvariant();
            _rarityBadge.text = $"★ {character.DisplayGrade} // CLASS";
            _levelLabel.text = $"LV. {character.Level} / {maxLevel}";
            _awakenStarsLabel.text = $"AWAKEN: " + new string('★', Mathf.Max(1, character.Awaken)) + new string('☆', Mathf.Max(0, 5 - character.Awaken));
            _affectionLabel.text = $"AFFECTION: {character.Affection} pts";

            if (def != null && def.Battle != null)
            {
                _hpStatLabel.text = $"HP  {def.Battle.Hp + character.Level * 15}";
                _atkStatLabel.text = $"ATK  {def.Battle.Atk + character.Level * 5}";
                _rangeStatLabel.text = $"RANGE  {def.Battle.Range}";
                _costStatLabel.text = $"COST  {def.Battle.Cost}";
                _roleStatLabel.text = $"ROLE  {def.Role ?? "ASSAULT"}";

                if (def.Battle.Skill != null)
                {
                    _skillNameLabel.text = $"★ {def.Battle.Skill.Name ?? "BURST"}";
                    _skillDescLabel.text = $"Effect: {def.Battle.Skill.EffectType} (Value: {def.Battle.Skill.Value})";
                }
                else
                {
                    _skillNameLabel.text = "NORMAL ATTACK";
                    _skillDescLabel.text = "Standard combat strike.";
                }
            }

            _levelUpButton.interactable = character.Level < maxLevel;
            _awakenButton.interactable = character.Awaken < 5;
            _advanceButton.interactable = !(character.CurrentRarity == "UR" && character.Tier >= 5);

            TryLoadPortrait(SelectedCharId, currentArtKey);
        }

        private void DisplayUnowned(CharacterDef def)
        {
            CurrentCharacterState = null;
            CurrentMaxLevel = 0;
            string name = def != null ? (def.Name ?? def.Id) : SelectedCharId;
            _charNameTitle.text = $"{name.ToUpperInvariant()} (未持有)";
            _rarityBadge.text = $"★ {def?.Rarity ?? "R"} // LOCKED";
            _levelLabel.text = "LV. 0 / 0";
            _awakenStarsLabel.text = "AWAKEN: 尚未覺醒";
            _affectionLabel.text = "AFFECTION: 0 pts";

            if (def != null && def.Battle != null)
            {
                _hpStatLabel.text = $"HP  {def.Battle.Hp}";
                _atkStatLabel.text = $"ATK  {def.Battle.Atk}";
                _rangeStatLabel.text = $"RANGE  {def.Battle.Range}";
                _costStatLabel.text = $"COST  {def.Battle.Cost}";
                _roleStatLabel.text = $"ROLE  {def.Role ?? "UNKNOWN"}";
            }

            _levelUpButton.interactable = false;
            _awakenButton.interactable = false;
            _advanceButton.interactable = false;
            TryLoadPortrait(SelectedCharId, null);
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

        // ---- 操作按鈕事件 ----

        private void OnClickLevelUp()
        {
            if (string.IsNullOrEmpty(SelectedCharId) || _presenter == null) return;
            _presenter.QuickLevelUp(SelectedCharId);
        }

        private void OnClickAwaken()
        {
            if (string.IsNullOrEmpty(SelectedCharId) || _presenter == null) return;
            _presenter.Awaken(SelectedCharId);
        }

        private void OnClickAdvance()
        {
            if (string.IsNullOrEmpty(SelectedCharId) || _presenter == null) return;
            _presenter.Advance(SelectedCharId);
        }

        // ---- UI 構建 ----

        private void BuildUi()
        {
            _canvasRoot = new GameObject("CharacterRosterCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            CreateLabel(topBar.transform, "Title", "◆ 部隊名冊與角色養成 // CHARACTER ROSTER", new Vector2(0.15f, 0.15f), new Vector2(0.60f, 0.85f), 24, new Color(0.9f, 0.94f, 1f), TextAnchor.MiddleLeft);

            // 3. 左側名冊滾動清單 (ScrollView)
            GameObject rosterPanel = CreatePanel(_canvasRoot.transform, "RosterPanel", new Vector2(0.02f, 0.04f), new Vector2(0.26f, 0.90f), new Color(0.05f, 0.08f, 0.12f, 0.85f));
            CreateLabel(rosterPanel.transform, "Header", "ROSTER LIST", new Vector2(0.05f, 0.93f), new Vector2(0.95f, 0.99f), 16, new Color(0.6f, 0.75f, 0.9f), TextAnchor.MiddleLeft);

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
            contentRt.sizeDelta = new Vector2(0f, 0f);

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
            _rosterContent = contentGo.transform;

            // 4. 中央立繪展示區
            GameObject centerPanel = CreatePanel(_canvasRoot.transform, "CenterPanel", new Vector2(0.28f, 0.04f), new Vector2(0.62f, 0.90f), new Color(0.05f, 0.07f, 0.11f, 0.6f));
            GameObject portraitGo = CreatePanel(centerPanel.transform, "Portrait", new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.95f), new Color(0.1f, 0.15f, 0.22f, 0.5f));
            _portraitImage = portraitGo.GetComponent<Image>();

            _charNameTitle = CreateLabel(centerPanel.transform, "CharName", "CLAIRE", new Vector2(0.05f, 0.08f), new Vector2(0.70f, 0.15f), 30, Color.white, TextAnchor.MiddleLeft);
            _rarityBadge = CreateLabel(centerPanel.transform, "RarityBadge", "★ SSR // CLASS", new Vector2(0.05f, 0.02f), new Vector2(0.70f, 0.08f), 18, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleLeft);

            // 5. 右側養成與屬性面板
            GameObject rightPanel = CreatePanel(_canvasRoot.transform, "RightPanel", new Vector2(0.64f, 0.04f), new Vector2(0.98f, 0.90f), new Color(0.05f, 0.08f, 0.13f, 0.95f));

            _levelLabel = CreateLabel(rightPanel.transform, "LevelLabel", "LV. 1 / 30", new Vector2(0.05f, 0.91f), new Vector2(0.95f, 0.97f), 22, new Color(0.35f, 0.85f, 1f), TextAnchor.MiddleLeft);
            _awakenStarsLabel = CreateLabel(rightPanel.transform, "AwakenLabel", "AWAKEN: ★☆☆☆☆", new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.91f), 18, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleLeft);
            _affectionLabel = CreateLabel(rightPanel.transform, "AffectionLabel", "AFFECTION: 0 pts", new Vector2(0.05f, 0.79f), new Vector2(0.95f, 0.85f), 16, new Color(1f, 0.6f, 0.8f), TextAnchor.MiddleLeft);

            // 戰鬥屬性
            GameObject statsBox = CreatePanel(rightPanel.transform, "StatsBox", new Vector2(0.05f, 0.52f), new Vector2(0.95f, 0.77f), new Color(0.08f, 0.12f, 0.18f, 0.9f));
            _hpStatLabel = CreateLabel(statsBox.transform, "Hp", "HP  1000", new Vector2(0.05f, 0.68f), new Vector2(0.48f, 0.95f), 18, Color.white, TextAnchor.MiddleLeft);
            _atkStatLabel = CreateLabel(statsBox.transform, "Atk", "ATK  150", new Vector2(0.52f, 0.68f), new Vector2(0.95f, 0.95f), 18, Color.white, TextAnchor.MiddleLeft);
            _rangeStatLabel = CreateLabel(statsBox.transform, "Range", "RANGE  150", new Vector2(0.05f, 0.36f), new Vector2(0.48f, 0.64f), 18, Color.white, TextAnchor.MiddleLeft);
            _costStatLabel = CreateLabel(statsBox.transform, "Cost", "COST  200", new Vector2(0.52f, 0.36f), new Vector2(0.95f, 0.64f), 18, Color.white, TextAnchor.MiddleLeft);
            _roleStatLabel = CreateLabel(statsBox.transform, "Role", "ROLE  ASSAULT", new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.32f), 16, new Color(0.7f, 0.82f, 0.95f), TextAnchor.MiddleLeft);

            // 技能面板
            GameObject skillBox = CreatePanel(rightPanel.transform, "SkillBox", new Vector2(0.05f, 0.32f), new Vector2(0.95f, 0.50f), new Color(0.08f, 0.12f, 0.18f, 0.9f));
            _skillNameLabel = CreateLabel(skillBox.transform, "SkillName", "★ SKILL", new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.92f), 18, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleLeft);
            _skillDescLabel = CreateLabel(skillBox.transform, "SkillDesc", "Skill description text", new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.52f), 14, new Color(0.8f, 0.85f, 0.9f), TextAnchor.UpperLeft);

            // 養成操作大按鈕 (LevelUp, Awaken, Advance)
            _levelUpButton = CreateButton(rightPanel.transform, "BtnLevelUp", "⬆️ 升級 (Level Up)", new Vector2(0.05f, 0.21f), new Vector2(0.95f, 0.29f), OnClickLevelUp, new Color(0.18f, 0.44f, 0.60f), 18);
            _awakenButton = CreateButton(rightPanel.transform, "BtnAwaken", "⭐ 覺醒突破 (Awaken)", new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.20f), OnClickAwaken, new Color(0.55f, 0.36f, 0.14f), 18);
            _advanceButton = CreateButton(rightPanel.transform, "BtnAdvance", "👑 同卡進階 (Advance)", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.11f), OnClickAdvance, new Color(0.48f, 0.20f, 0.52f), 18);

            // 狀態與錯誤提示訊息
            _messageLabel = CreateLabel(rightPanel.transform, "MessageLabel", "", new Vector2(0.05f, 0.00f), new Vector2(0.95f, 0.03f), 14, Color.green, TextAnchor.MiddleCenter);
        }

        private Button CreateCharacterCard(CharacterDef def)
        {
            GameObject card = new GameObject("Card_" + def.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            card.transform.SetParent(_rosterContent, false);
            LayoutElement le = card.GetComponent<LayoutElement>();
            le.minHeight = 72f;
            le.preferredHeight = 72f;

            Image img = card.GetComponent<Image>();
            img.color = new Color(0.12f, 0.18f, 0.26f, 0.95f);

            Button btn = card.GetComponent<Button>();
            string charId = def.Id;
            btn.onClick.AddListener(() => SelectCharacter(charId));

            Text txt = CreateLabel(card.transform, "Info", $"[{def.Rarity}] {ShortName(def.Name ?? def.Id)}", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), 15, Color.white, TextAnchor.MiddleLeft);
            _cardInfoTexts[def.Id] = txt;

            return btn;
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
