using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 正式 UGUI 關卡星圖選擇與戰前編隊整備畫面 (依據 design/14_ui_screens.md §3、OPT-3、M2-1 與 M2-7 規劃)。
    /// 支援章節切換分頁 (Chapter 1 / Chapter 2)、關卡選擇、敵方主堡特性警示膠囊
    /// （如震波擊退、遠程免疫、範圍免疫）、三星作戰條件一覽、首通獎勵預覽、1~10 人戰前快速編隊與多語系動態切換。
    /// </summary>
    [DisallowMultipleComponent]
    public class StageSelectView : UnityScreenBase, IStageSelectView
    {
        public override string ScreenId => "Screen_StageSelect";

        [Header("頂部戰術列")]
        [SerializeField] private Button _btnBack;
        [SerializeField] private Text _btnBackText;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _subTitleText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _gemText;
        [SerializeField] private Text _staminaText;

        [Header("左側章節分頁與關卡列表")]
        [SerializeField] private Transform _chapterTabRoot;
        [SerializeField] private Transform _stageListRoot;
        private readonly List<GameObject> _chapterTabItems = new List<GameObject>();
        private readonly List<GameObject> _stageButtonItems = new List<GameObject>();

        [Header("右上選中關卡作戰情資")]
        [SerializeField] private Text _selectedStageTitleText;
        [SerializeField] private Text _enemyBaseHpText;
        [SerializeField] private GameObject _threatBadgeRoot;
        [SerializeField] private Text _threatBadgeText;
        [SerializeField] private Image _threatBadgeBg;
        [SerializeField] private Text _starConditionsText;
        [SerializeField] private Text _rewardsText;

        [Header("右下出戰編隊整備區與主堡強化")]
        [SerializeField] private Text _teamCountText;
        [SerializeField] private Transform _rosterGridRoot;
        [SerializeField] private Button _btnStartBattle;
        [SerializeField] private Text _btnStartBattleText;
        [SerializeField] private Text _baseLevelText;
        [SerializeField] private Text _baseHpText;
        [SerializeField] private Button _btnUpgradeBase;
        [SerializeField] private Text _btnUpgradeBaseText;
        private readonly List<GameObject> _rosterCardItems = new List<GameObject>();

        public Button UpgradeBaseButton => _btnUpgradeBase;
        public Text BaseLevelText => _baseLevelText;
        public Text BaseHpText => _baseHpText;

        [Header("Toast 浮動提示")]
        [SerializeField] private GameObject _toastRoot;
        [SerializeField] private Text _toastText;
        [SerializeField] private CanvasGroup _toastCanvasGroup;

        // Presenter & State
        private StageSelectPresenter _presenter;
        public StageSelectPresenter Presenter => _presenter;
        public int CurrentChapter => _presenter != null ? _presenter.CurrentChapter : 1;
        public int DisplayedStageCount => _stageButtonItems.Count;
        public string TitleText => _titleText != null ? _titleText.text : string.Empty;

        private LobbyView _lobbyView;
        private bool _isInitialized;
        private bool _subscribedLocale;
        private Coroutine _toastCoroutine;

        private void Awake()
        {
            EnsureUIHierarchy();
        }

        private void Start()
        {
            InitPresenterIfReady();
            SubscribeLocalization();
        }

        private void OnDestroy()
        {
            UnsubscribeLocalization();
        }

        private void Update()
        {
            if (!_isInitialized && GameServices.IsReady && GameServices.Core != null)
            {
                Initialize(GameServices.Core);
            }

            if (!_subscribedLocale && GameServices.Localization != null)
            {
                SubscribeLocalization();
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
        }

        public void InitializeForTesting(GameCore core)
        {
            EnsureUIHierarchy();
            Initialize(core);
            SubscribeLocalization();
        }

        public void Initialize(GameCore core)
        {
            if (core == null) return;
            _presenter = new StageSelectPresenter(core);
            _presenter.AttachView(this);
            _presenter.Initialize();
            _isInitialized = true;

            SubscribeLocalization();
            RefreshCurrencies();
            ApplyLocalizedTexts();
        }

        private void SubscribeLocalization()
        {
            if (_subscribedLocale || GameServices.Localization == null) return;
            GameServices.Localization.LocaleChanged += OnLocaleChanged;
            _subscribedLocale = true;
        }

        private void UnsubscribeLocalization()
        {
            if (!_subscribedLocale || GameServices.Localization == null) return;
            GameServices.Localization.LocaleChanged -= OnLocaleChanged;
            _subscribedLocale = false;
        }

        private void OnLocaleChanged(string newLocale)
        {
            ApplyLocalizedTexts();
            _presenter?.Refresh();
        }

        public void ApplyLocalizedTexts()
        {
            var loc = GameServices.Localization;
            bool isEn = loc != null && string.Equals(loc.CurrentLocale, "en", StringComparison.OrdinalIgnoreCase);

            if (_titleText != null)
            {
                string title = loc != null && loc.Has("ui.stage.title")
                    ? loc.Get("ui.stage.title")
                    : (isEn ? "◈ THEATER SORTIE // STAGE SELECT ◈" : "◈ 作戰星圖 // THEATER SORTIE ◈");
                _titleText.text = title;
            }

            if (_subTitleText != null)
            {
                int ch = CurrentChapter;
                _subTitleText.text = isEn
                    ? $"OPERATION THEATER: CHAPTER 0{ch}"
                    : $"OPERATION THEATER: 學院守護戰役第 {ch} 章 (CHAPTER 0{ch})";
            }

            if (_btnBackText != null)
            {
                _btnBackText.text = loc != null && loc.Has("ui.common.back")
                    ? loc.Get("ui.common.back")
                    : (isEn ? "◀ BACK" : "◀ 返回大廳");
            }

            if (_btnStartBattleText != null)
            {
                _btnStartBattleText.text = loc != null && loc.Has("ui.stage.sortie")
                    ? loc.Get("ui.stage.sortie")
                    : (isEn ? "▶ SORTIE\n(LAUNCH)" : "▶ 開始作戰\n(SORTIE)");
            }
        }

        private void InitPresenterIfReady()
        {
            if (_isInitialized) return;
            if (GameServices.IsReady && GameServices.Core != null)
            {
                Initialize(GameServices.Core);
            }
        }

        public void Open(LobbyView lobby = null)
        {
            _lobbyView = lobby;
            EnsureUIHierarchy();
            gameObject.SetActive(true);
            if (_canvas != null) _canvas.enabled = true;

            InitPresenterIfReady();
            SubscribeLocalization();
            _presenter?.Refresh();
            RefreshCurrencies();
            ApplyLocalizedTexts();
        }

        public void Close()
        {
            if (GameServices.Navigator != null && GameServices.Navigator.CurrentScreen == this)
            {
                GameServices.Navigator.Pop();
                return;
            }

            gameObject.SetActive(false);
            if (_canvas != null) _canvas.enabled = false;

            if (_lobbyView != null)
            {
                _lobbyView.gameObject.SetActive(true);
                _lobbyView.RefreshPlayerHeader();
            }
        }

        public override void OnPush(object args)
        {
            base.OnPush(args);
            if (args is LobbyView lobby)
            {
                _lobbyView = lobby;
            }
            Open(_lobbyView);
        }

        public override void OnPop()
        {
            base.OnPop();
            if (_toastRoot != null) _toastRoot.SetActive(false);
        }

        public override bool OnBackRequested()
        {
            if (_toastRoot != null && _toastRoot.activeSelf)
            {
                _toastRoot.SetActive(false);
                return true;
            }

            return false;
        }

        public void RefreshCurrencies()
        {
            if (GameServices.Core?.Player == null) return;
            PlayerState player = GameServices.Core.Player;
            long gold = player.ItemCount("gold");
            long gem = player.ItemCount("gem");
            int stamina = player.Encounter?.Stamina ?? 5;
            int maxStamina = GameServices.Core.Master?.Config?.Encounter?.StaminaMax ?? 5;

            if (_goldText != null) _goldText.text = $"{gold:N0}";
            if (_gemText != null) _gemText.text = $"{gem:N0}";
            if (_staminaText != null) _staminaText.text = $"{stamina}/{maxStamina}";
        }

        public bool SelectChapter(int chapter)
        {
            if (_presenter == null) return false;
            bool ok = _presenter.SelectChapter(chapter);
            if (ok)
            {
                RefreshChapterTabs();
                ApplyLocalizedTexts();
            }
            return ok;
        }

        private void RefreshChapterTabs()
        {
            if (_chapterTabRoot == null) return;

            foreach (var go in _chapterTabItems)
            {
                if (go != null) SafeDestroy(go);
            }
            _chapterTabItems.Clear();

            IReadOnlyList<int> chapters = _presenter != null ? _presenter.GetChapters() : new List<int> { 1, 2 };
            int curChapter = CurrentChapter;
            Font font = ProceduralUiKit.GetDefaultFont();

            for (int i = 0; i < chapters.Count; i++)
            {
                int ch = chapters[i];
                bool isSelected = ch == curChapter;

                var tabGo = ProceduralUiKit.CreateUiObject($"ChapterTab_{ch}", _chapterTabRoot);
                var tabRt = tabGo.GetComponent<RectTransform>();
                tabRt.sizeDelta = new Vector2(184, 42);

                var img = tabGo.AddComponent<Image>();
                Color bgCol = isSelected ? new Color(0.18f, 0.35f, 0.58f, 0.98f) : new Color(0.08f, 0.11f, 0.18f, 0.90f);
                Color borderCol = isSelected ? new Color(0f, 0.92f, 1f, 1f) : new Color(0.25f, 0.35f, 0.48f, 0.5f);
                img.sprite = ProceduralUiKit.GetPillSprite(184, 42, bgCol, borderCol);

                var btn = tabGo.AddComponent<Button>();
                int capturedCh = ch;
                btn.onClick.AddListener(() => SelectChapter(capturedCh));

                var txtGo = ProceduralUiKit.CreateUiObject("Txt", tabGo.transform);
                ProceduralUiKit.StretchFull(txtGo.GetComponent<RectTransform>());
                var txt = txtGo.AddComponent<Text>();
                txt.font = font;
                txt.fontSize = 15;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = isSelected ? new Color(1f, 0.95f, 0.78f, 1f) : new Color(0.68f, 0.75f, 0.85f, 0.9f);
                txt.text = $"第 {ch} 章 (CH.{ch:00})";

                _chapterTabItems.Add(tabGo);
            }
        }

        #region IStageSelectView 實作

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        public void DisplayStages(IReadOnlyList<StageSummaryItem> stages, string selectedStageId)
        {
            if (_stageListRoot == null || stages == null) return;

            RefreshChapterTabs();
            ApplyLocalizedTexts();

            // 清理舊項目
            foreach (var go in _stageButtonItems)
            {
                if (go != null) SafeDestroy(go);
            }
            _stageButtonItems.Clear();

            Font font = ProceduralUiKit.GetDefaultFont();

            for (int i = 0; i < stages.Count; i++)
            {
                var st = stages[i];
                bool isSelected = st.StageId == selectedStageId;

                var itemGo = ProceduralUiKit.CreateUiObject($"StageItem_{st.StageId}", _stageListRoot);
                var itemRt = itemGo.GetComponent<RectTransform>();
                itemRt.sizeDelta = new Vector2(380, 80);

                var img = itemGo.AddComponent<Image>();
                Color bgCol = isSelected
                    ? new Color(0.18f, 0.28f, 0.45f, 0.98f)
                    : (st.IsUnlocked ? new Color(0.10f, 0.14f, 0.22f, 0.92f) : new Color(0.06f, 0.08f, 0.12f, 0.85f));
                Color borderCol = isSelected
                    ? new Color(0f, 0.9f, 1f, 1f)
                    : (st.IsUnlocked ? new Color(0.2f, 0.6f, 0.85f, 0.5f) : new Color(0.3f, 0.35f, 0.45f, 0.3f));

                img.sprite = ProceduralUiKit.GetGradientSprite(380, 80, bgCol, new Color(bgCol.r * 0.7f, bgCol.g * 0.7f, bgCol.b * 0.7f, bgCol.a), borderCol, isSelected ? 3 : 2, 8);

                var btn = itemGo.AddComponent<Button>();
                string capturedId = st.StageId;
                btn.onClick.AddListener(() => OnClickSelectStage(capturedId));

                // 標題與章節
                var titleGo = ProceduralUiKit.CreateUiObject("Title", itemGo.transform);
                var titleRt = titleGo.GetComponent<RectTransform>();
                titleRt.anchorMin = new Vector2(0f, 0.5f);
                titleRt.anchorMax = new Vector2(1f, 1f);
                titleRt.anchoredPosition = new Vector2(20, -2);
                var titleTxt = titleGo.AddComponent<Text>();
                titleTxt.font = font;
                titleTxt.fontSize = 18;
                titleTxt.fontStyle = FontStyle.Bold;
                titleTxt.alignment = TextAnchor.MiddleLeft;
                titleTxt.color = isSelected ? new Color(1f, 0.95f, 0.75f, 1f) : (st.IsUnlocked ? Color.white : new Color(0.6f, 0.65f, 0.75f, 0.8f));
                titleTxt.text = $"◈ {st.StageId.ToUpper()} // {st.NameKey}";

                // 副標與星數
                var subGo = ProceduralUiKit.CreateUiObject("Sub", itemGo.transform);
                var subRt = subGo.GetComponent<RectTransform>();
                subRt.anchorMin = new Vector2(0f, 0f);
                subRt.anchorMax = new Vector2(1f, 0.5f);
                subRt.anchoredPosition = new Vector2(20, 2);
                var subTxt = subGo.AddComponent<Text>();
                subTxt.font = font;
                subTxt.fontSize = 13;
                subTxt.alignment = TextAnchor.MiddleLeft;

                if (!st.IsUnlocked)
                {
                    subTxt.color = new Color(0.85f, 0.3f, 0.3f, 0.9f);
                    subTxt.text = "🔒 尚未解鎖 (需通關前置戰役)";
                }
                else
                {
                    string starStr = st.Stars switch
                    {
                        3 => "★★★ (三星完全作戰)",
                        2 => "★★☆ (二星作戰達成)",
                        1 => "★☆☆ (初戰突破)",
                        _ => "☆☆☆ (尚未作戰)"
                    };
                    subTxt.color = st.Stars > 0 ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.6f, 0.75f, 0.9f, 0.8f);
                    subTxt.text = $"作戰評價：{starStr}";
                }

                _stageButtonItems.Add(itemGo);
            }
        }

        public void DisplayStageDetail(StageSummaryItem summary, StageDef stageDef)
        {
            if (summary == null || stageDef == null) return;

            if (_selectedStageTitleText != null)
            {
                _selectedStageTitleText.text = $"【作戰區域：{summary.StageId.ToUpper()} // {summary.NameKey}】";
            }

            if (_enemyBaseHpText != null)
            {
                _enemyBaseHpText.text = $"◈ 敵方主堡構造強度：{stageDef.EnemyBaseHp:N0} HP  (戰場長度：{stageDef.Length} m)";
            }

            // 主堡特殊防衛特性警示膠囊
            if (_threatBadgeRoot != null && _threatBadgeText != null && _threatBadgeBg != null)
            {
                if (summary.EnemyBaseTraits != null && summary.EnemyBaseTraits.Contains("shockwave_on_hp_loss"))
                {
                    _threatBadgeRoot.SetActive(true);
                    _threatBadgeText.text = "⚡ 戰術警報：敵方主堡每損血 20% 觸發全屏震波擊退！建議控制出擊節奏！";
                    _threatBadgeBg.color = new Color(0.95f, 0.35f, 0.15f, 0.95f);
                }
                else if (summary.EnemyBaseTraits != null && summary.EnemyBaseTraits.Contains("immune_ranged"))
                {
                    _threatBadgeRoot.SetActive(true);
                    _threatBadgeText.text = "🛡️ 戰術警報：敵方主堡免疫遠程攻擊（射程>150無效）！請務必編組近戰角色！";
                    _threatBadgeBg.color = new Color(0.95f, 0.75f, 0.15f, 0.95f);
                }
                else if (summary.EnemyBaseTraits != null && summary.EnemyBaseTraits.Contains("immune_area"))
                {
                    _threatBadgeRoot.SetActive(true);
                    _threatBadgeText.text = "🛡️ 戰術警報：敵方主堡免疫範圍攻擊！請派遣單體輸出角色攻堅！";
                    _threatBadgeBg.color = new Color(0.85f, 0.35f, 0.95f, 0.95f);
                }
                else
                {
                    _threatBadgeRoot.SetActive(true);
                    _threatBadgeText.text = "◈ 戰情分析：敵方主堡無特殊外骨骼裝甲防衛機制，正常戰術推進即可。";
                    _threatBadgeBg.color = new Color(0.15f, 0.65f, 0.85f, 0.95f);
                }
            }

            // 三星作戰目標列表
            if (_starConditionsText != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("【作戰達成星級目標】");
                if (summary.StarConditions != null && summary.StarConditions.Count > 0)
                {
                    for (int i = 0; i < summary.StarConditions.Count; i++)
                    {
                        string desc = summary.StarConditions[i] switch
                        {
                            "win" => "1. 擊破敵方主堡取得作戰勝利",
                            "ally_base_hp_gte_50" => "2. 我方指揮部主堡剩餘血量高於 50%",
                            "clear_within_180s" => "3. 作戰時間在 180 秒內完成快速突擊",
                            _ => $"{i + 1}. {summary.StarConditions[i]}"
                        };
                        sb.AppendLine($"  ★ {desc}");
                    }
                }
                else
                {
                    sb.AppendLine("  ★ 1. 擊破敵方要塞取得完全勝利");
                }
                _starConditionsText.text = sb.ToString();
            }

            // 首次通關獎勵
            if (_rewardsText != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("【首通戰役榮譽獎勵】：");
                if (summary.FirstClearRewards != null && summary.FirstClearRewards.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var rw in summary.FirstClearRewards)
                    {
                        items.Add($"{rw.Item} x{rw.N}");
                    }
                    sb.Append(string.Join("、", items));
                }
                else
                {
                    sb.Append("鑽石 x300、初戰指揮官勳章");
                }
                _rewardsText.text = sb.ToString();
            }
        }

        public void DisplayTeam(IReadOnlyList<string> currentTeam, IReadOnlyList<string> ownedCharacters)
        {
            if (_rosterGridRoot == null || ownedCharacters == null) return;

            // 清理舊卡片
            foreach (var go in _rosterCardItems)
            {
                if (go != null) SafeDestroy(go);
            }
            _rosterCardItems.Clear();

            int teamCount = currentTeam?.Count ?? 0;
            if (_teamCountText != null)
            {
                _teamCountText.text = $"出戰小隊編組：{teamCount} / 10 人 (點擊角色卡切換上陣)";
            }

            Font font = ProceduralUiKit.GetDefaultFont();
            var teamSet = new HashSet<string>(currentTeam ?? Array.Empty<string>());

            for (int i = 0; i < ownedCharacters.Count; i++)
            {
                string charId = ownedCharacters[i];
                bool isDeployed = teamSet.Contains(charId);

                var cardGo = ProceduralUiKit.CreateUiObject($"Char_{charId}", _rosterGridRoot);
                var cardRt = cardGo.GetComponent<RectTransform>();
                cardRt.sizeDelta = new Vector2(96, 120);

                var cardImg = cardGo.AddComponent<Image>();
                Color borderCol = isDeployed ? new Color(0f, 0.95f, 1f, 1f) : new Color(0.3f, 0.4f, 0.5f, 0.4f);
                Color bgCol = isDeployed ? new Color(0.12f, 0.22f, 0.35f, 0.95f) : new Color(0.06f, 0.08f, 0.12f, 0.85f);
                cardImg.sprite = ProceduralUiKit.GetGradientSprite(96, 120, bgCol, new Color(bgCol.r * 0.7f, bgCol.g * 0.7f, bgCol.b * 0.7f, bgCol.a), borderCol, isDeployed ? 3 : 1, 6);

                var btn = cardGo.AddComponent<Button>();
                string capturedCharId = charId;
                btn.onClick.AddListener(() => OnClickToggleTeamMember(capturedCharId));

                // 角色簡稱/代號
                var nameGo = ProceduralUiKit.CreateUiObject("Name", cardGo.transform);
                var nameRt = nameGo.GetComponent<RectTransform>();
                nameRt.anchorMin = new Vector2(0f, 0.3f);
                nameRt.anchorMax = new Vector2(1f, 1f);
                nameRt.anchoredPosition = Vector2.zero;
                var nameTxt = nameGo.AddComponent<Text>();
                nameTxt.font = font;
                nameTxt.fontSize = 13;
                nameTxt.fontStyle = FontStyle.Bold;
                nameTxt.alignment = TextAnchor.MiddleCenter;
                nameTxt.color = isDeployed ? Color.white : new Color(0.7f, 0.75f, 0.85f, 0.8f);
                nameTxt.text = GetCharacterShortName(charId);

                // 上陣標記
                var tagGo = ProceduralUiKit.CreateUiObject("StatusTag", cardGo.transform);
                var tagRt = tagGo.GetComponent<RectTransform>();
                tagRt.anchorMin = new Vector2(0f, 0f);
                tagRt.anchorMax = new Vector2(1f, 0.3f);
                tagRt.anchoredPosition = Vector2.zero;
                var tagImg = tagGo.AddComponent<Image>();
                tagImg.color = isDeployed ? new Color(0f, 0.7f, 0.9f, 0.85f) : new Color(0.15f, 0.2f, 0.28f, 0.6f);

                var tagTxtGo = ProceduralUiKit.CreateUiObject("Txt", tagGo.transform);
                ProceduralUiKit.StretchFull(tagTxtGo.GetComponent<RectTransform>());
                var tagTxt = tagTxtGo.AddComponent<Text>();
                tagTxt.font = font;
                tagTxt.fontSize = 10;
                tagTxt.fontStyle = FontStyle.Bold;
                tagTxt.alignment = TextAnchor.MiddleCenter;
                tagTxt.color = isDeployed ? Color.white : new Color(0.6f, 0.7f, 0.8f, 0.8f);
                tagTxt.text = isDeployed ? "出擊中" : "待命";

                _rosterCardItems.Add(cardGo);
            }
        }

        public void DisplayBaseStatus(int baseLevel, int maxLevel, int currentHp, int nextHp, int upgradeGoldCost, bool canAfford)
        {
            RefreshCurrencies();
            bool isMax = baseLevel >= maxLevel;
            if (_baseLevelText != null)
            {
                _baseLevelText.text = isMax
                    ? $"🏰 我方主堡 LV.{baseLevel} (MAX)"
                    : $"🏰 我方主堡 LV.{baseLevel} / {maxLevel}";
            }
            if (_baseHpText != null)
            {
                _baseHpText.text = isMax
                    ? $"主堡耐久：{currentHp:N0} HP (已滿級)"
                    : $"耐久：{currentHp:N0} → {nextHp:N0} (+{nextHp - currentHp:N0})";
            }
            if (_btnUpgradeBase != null)
            {
                _btnUpgradeBase.interactable = !isMax && canAfford;
            }
            if (_btnUpgradeBaseText != null)
            {
                _btnUpgradeBaseText.text = isMax
                    ? "已達最高防禦等級"
                    : $"🔨 強化主堡 ({upgradeGoldCost:N0} G)";
            }
        }

        public void ShowError(string message)
        {
            ShowToast($"[提示] {message}");
        }

        #endregion

        #region 互動事件

        private void OnClickSelectStage(string stageId)
        {
            if (_presenter != null)
            {
                _presenter.SelectStage(stageId);
            }
        }

        private void OnClickToggleTeamMember(string charId)
        {
            if (_presenter != null)
            {
                _presenter.ToggleTeamMember(charId);
            }
        }

        public void OnClickUpgradeBase()
        {
            if (_presenter == null) return;
            var res = _presenter.UpgradeBase();
            if (res.Ok)
            {
                ShowToast($"🏰 主堡強化成功！等級提升至 LV.{res.Value}");
            }
        }

        public void OnClickStartBattle()
        {
            if (_presenter == null) return;

            if (!_presenter.CanStartSelectedStage())
            {
                var summary = _presenter.GetStages();
                var cur = summary?.FirstOrDefault(s => s.StageId == _presenter.SelectedStageId);
                if (cur != null && !cur.IsUnlocked)
                {
                    ShowToast("此關卡尚未解鎖！請依序突破前置戰役。");
                }
                else
                {
                    ShowToast("出戰部隊不可為空，請至少派遣 1 位作戰角色！");
                }
                return;
            }

            string stageId = _presenter.SelectedStageId;
            BattleRunner runner = FindAnyObjectByType<BattleRunner>();
            if (runner != null)
            {
                ShowToast($"▶ 啟動戰場突入：{stageId.ToUpper()}！");
                Close();
                runner.StartBattle(stageId);
            }
            else
            {
                ShowToast($"▶ 啟動出擊準備中 ({stageId})...");
            }
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

        private string GetCharacterShortName(string charId)
        {
            return ProceduralUiKit.ResolveCharacterName(charId);
        }

        #endregion

        #region 自動構建 UGUI 階層

        public void EnsureUIHierarchy()
        {
            if (_canvas != null && _stageListRoot != null) return;

            Font defaultFont = ProceduralUiKit.GetDefaultFont();

            // 1. Canvas 與 Scaler 設置
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 15;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (GetComponent<ResolutionAdapter>() == null) gameObject.AddComponent<ResolutionAdapter>();

            // 2. 全屏深色戰術星圖背景
            var bgGo = ProceduralUiKit.CreateUiObject("BackgroundPanel", transform);
            ProceduralUiKit.StretchFull(bgGo.GetComponent<RectTransform>());
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = ProceduralUiKit.GetGradientSprite(128, 128, new Color(0.05f, 0.08f, 0.15f, 1f), new Color(0.02f, 0.03f, 0.06f, 1f));

            // 3. 頂部戰術導航列
            var headerGo = ProceduralUiKit.CreateUiObject("TopHeaderBar", transform);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = Vector2.zero;
            headerRt.sizeDelta = new Vector2(0, 74);

            // 返回大廳按鈕
            var backBtnGo = ProceduralUiKit.CreateUiObject("BtnBack", headerGo.transform);
            var backRt = backBtnGo.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0f, 0.5f);
            backRt.anchorMax = new Vector2(0f, 0.5f);
            backRt.pivot = new Vector2(0f, 0.5f);
            backRt.anchoredPosition = new Vector2(25, 0);
            backRt.sizeDelta = new Vector2(140, 42);
            var backImg = backBtnGo.AddComponent<Image>();
            backImg.sprite = ProceduralUiKit.GetPillSprite(140, 42, new Color(0.15f, 0.18f, 0.28f, 0.95f), new Color(0.2f, 0.8f, 1f, 0.7f));
            _btnBack = backBtnGo.AddComponent<Button>();
            _btnBack.onClick.AddListener(Close);

            var bTxtGo = ProceduralUiKit.CreateUiObject("Txt", backBtnGo.transform);
            ProceduralUiKit.StretchFull(bTxtGo.GetComponent<RectTransform>());
            _btnBackText = bTxtGo.AddComponent<Text>();
            _btnBackText.font = defaultFont;
            _btnBackText.fontSize = 16;
            _btnBackText.alignment = TextAnchor.MiddleCenter;
            _btnBackText.color = Color.white;
            _btnBackText.text = "◀ 返回大廳";

            // 標題與副標題
            var titleBoxGo = ProceduralUiKit.CreateUiObject("TitleBox", headerGo.transform);
            var tBoxRt = titleBoxGo.GetComponent<RectTransform>();
            tBoxRt.anchorMin = new Vector2(0f, 0.5f);
            tBoxRt.anchorMax = new Vector2(0f, 0.5f);
            tBoxRt.pivot = new Vector2(0f, 0.5f);
            tBoxRt.anchoredPosition = new Vector2(185, 0);
            tBoxRt.sizeDelta = new Vector2(450, 60);

            var titleGo = ProceduralUiKit.CreateUiObject("Title", titleBoxGo.transform);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.anchoredPosition = new Vector2(0, 4);
            _titleText = titleGo.AddComponent<Text>();
            _titleText.font = defaultFont;
            _titleText.fontSize = 20;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = new Color(1f, 0.95f, 0.82f, 1f);
            _titleText.text = "◈ 作戰星圖 // THEATER SORTIE ◈";

            var subGo = ProceduralUiKit.CreateUiObject("SubTitle", titleBoxGo.transform);
            var subRt = subGo.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0f, 0f);
            subRt.anchorMax = new Vector2(1f, 0.5f);
            subRt.anchoredPosition = new Vector2(0, -2);
            _subTitleText = subGo.AddComponent<Text>();
            _subTitleText.font = defaultFont;
            _subTitleText.fontSize = 12;
            _subTitleText.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            _subTitleText.text = "OPERATION THEATER: 學院守護戰役第 1 章 (CHAPTER 01)";

            // 右上資源膠囊
            _gemText = CreatePill(headerGo.transform, "GemPill", new Vector2(-28, 0), "3,000", new Color(0.2f, 0.9f, 1f, 1f), defaultFont);
            _goldText = CreatePill(headerGo.transform, "GoldPill", new Vector2(-198, 0), "5,000", new Color(1f, 0.85f, 0.2f, 1f), defaultFont);
            _staminaText = CreatePill(headerGo.transform, "StaminaPill", new Vector2(-368, 0), "5/5", new Color(0.3f, 0.95f, 0.6f, 1f), defaultFont);

            // 4. 左側章節分頁列 (ChapterTabBar) 與關卡列表面板 (StageListPanel)
            var tabBarGo = ProceduralUiKit.CreateUiObject("ChapterTabBar", transform);
            var tabBarRt = tabBarGo.GetComponent<RectTransform>();
            tabBarRt.anchorMin = new Vector2(0f, 1f);
            tabBarRt.anchorMax = new Vector2(0f, 1f);
            tabBarRt.pivot = new Vector2(0f, 1f);
            tabBarRt.anchoredPosition = new Vector2(30, -90);
            tabBarRt.sizeDelta = new Vector2(400, 48);

            var tabHlg = tabBarGo.AddComponent<HorizontalLayoutGroup>();
            tabHlg.spacing = 12;
            tabHlg.childAlignment = TextAnchor.MiddleLeft;
            tabHlg.childControlWidth = false;
            tabHlg.childControlHeight = false;
            _chapterTabRoot = tabBarGo.transform;

            var listPanelGo = ProceduralUiKit.CreateUiObject("StageListPanel", transform);
            var listRt = listPanelGo.GetComponent<RectTransform>();
            listRt.anchorMin = new Vector2(0f, 0.5f);
            listRt.anchorMax = new Vector2(0f, 0.5f);
            listRt.pivot = new Vector2(0f, 0.5f);
            listRt.anchoredPosition = new Vector2(30, -50);
            listRt.sizeDelta = new Vector2(400, 820);

            var listVlg = listPanelGo.AddComponent<VerticalLayoutGroup>();
            listVlg.spacing = 16;
            listVlg.childAlignment = TextAnchor.UpperCenter;
            listVlg.childControlWidth = true;
            listVlg.childControlHeight = false;
            _stageListRoot = listPanelGo.transform;

            // 5. 右上作戰情資面板 (StageDetailPanel)
            var detailGo = ProceduralUiKit.CreateUiObject("StageDetailPanel", transform);
            var detailRt = detailGo.GetComponent<RectTransform>();
            detailRt.anchorMin = new Vector2(1f, 1f);
            detailRt.anchorMax = new Vector2(1f, 1f);
            detailRt.pivot = new Vector2(1f, 1f);
            detailRt.anchoredPosition = new Vector2(-30, -90);
            detailRt.sizeDelta = new Vector2(1420, 520);

            var detailBg = detailGo.AddComponent<Image>();
            detailBg.sprite = ProceduralUiKit.GetGradientSprite(1420, 520, new Color(0.08f, 0.12f, 0.20f, 0.95f), new Color(0.04f, 0.06f, 0.12f, 0.95f), new Color(0.2f, 0.7f, 0.95f, 0.6f), 2, 8);

            // 關卡標題
            var dtTitleGo = ProceduralUiKit.CreateUiObject("SelectedTitle", detailGo.transform);
            var dtTitleRt = dtTitleGo.GetComponent<RectTransform>();
            dtTitleRt.anchorMin = new Vector2(0f, 1f);
            dtTitleRt.anchorMax = new Vector2(1f, 1f);
            dtTitleRt.pivot = new Vector2(0f, 1f);
            dtTitleRt.anchoredPosition = new Vector2(25, -18);
            dtTitleRt.sizeDelta = new Vector2(-50, 36);
            _selectedStageTitleText = dtTitleGo.AddComponent<Text>();
            _selectedStageTitleText.font = defaultFont;
            _selectedStageTitleText.fontSize = 24;
            _selectedStageTitleText.fontStyle = FontStyle.Bold;
            _selectedStageTitleText.color = new Color(1f, 0.95f, 0.8f, 1f);
            _selectedStageTitleText.text = "【作戰區域：ST_1_01 // 學院正門突破】";

            // 敵方主堡血量
            var hpGo = ProceduralUiKit.CreateUiObject("EnemyBaseHpText", detailGo.transform);
            var hpRt = hpGo.GetComponent<RectTransform>();
            hpRt.anchorMin = new Vector2(0f, 1f);
            hpRt.anchorMax = new Vector2(1f, 1f);
            hpRt.pivot = new Vector2(0f, 1f);
            hpRt.anchoredPosition = new Vector2(25, -60);
            hpRt.sizeDelta = new Vector2(-50, 26);
            _enemyBaseHpText = hpGo.AddComponent<Text>();
            _enemyBaseHpText.font = defaultFont;
            _enemyBaseHpText.fontSize = 16;
            _enemyBaseHpText.color = new Color(0.85f, 0.92f, 1f, 0.9f);
            _enemyBaseHpText.text = "◈ 敵方主堡構造強度：5,000 HP (戰場長度：3000 m)";

            // 主堡特性警示膠囊
            var badgeGo = ProceduralUiKit.CreateUiObject("ThreatAlertBadge", detailGo.transform);
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.anchoredPosition = new Vector2(25, -95);
            badgeRt.sizeDelta = new Vector2(-50, 44);
            _threatBadgeBg = badgeGo.AddComponent<Image>();
            _threatBadgeBg.sprite = ProceduralUiKit.GetPillSprite(1370, 44, new Color(0.9f, 0.3f, 0.15f, 0.9f), Color.white);
            _threatBadgeRoot = badgeGo;

            var badgeTxtGo = ProceduralUiKit.CreateUiObject("Text", badgeGo.transform);
            ProceduralUiKit.StretchFull(badgeTxtGo.GetComponent<RectTransform>(), 6);
            _threatBadgeText = badgeTxtGo.AddComponent<Text>();
            _threatBadgeText.font = defaultFont;
            _threatBadgeText.fontSize = 16;
            _threatBadgeText.fontStyle = FontStyle.Bold;
            _threatBadgeText.alignment = TextAnchor.MiddleLeft;
            _threatBadgeText.color = Color.white;
            _threatBadgeText.text = "⚡ 戰術警報：敵方主堡每損血 20% 觸發全屏震波擊退！";

            // 三星作戰目標說明
            var starGo = ProceduralUiKit.CreateUiObject("StarConditionsText", detailGo.transform);
            var starRt = starGo.GetComponent<RectTransform>();
            starRt.anchorMin = new Vector2(0f, 0f);
            starRt.anchorMax = new Vector2(0.55f, 1f);
            starRt.offsetMin = new Vector2(25, 40);
            starRt.offsetMax = new Vector2(-15, -150);
            _starConditionsText = starGo.AddComponent<Text>();
            _starConditionsText.font = defaultFont;
            _starConditionsText.fontSize = 16;
            _starConditionsText.lineSpacing = 1.35f;
            _starConditionsText.color = new Color(0.9f, 0.95f, 1f, 0.95f);
            _starConditionsText.text = "【作戰達成星級目標】\n  ★ 1. 擊破敵方要塞取得完全勝利\n  ★ 2. 我方主堡血量高於 50%\n  ★ 3. 180 秒內速戰速決";

            // 首通獎勵一覽
            var rwGo = ProceduralUiKit.CreateUiObject("RewardsText", detailGo.transform);
            var rwRt = rwGo.GetComponent<RectTransform>();
            rwRt.anchorMin = new Vector2(0.55f, 0f);
            rwRt.anchorMax = new Vector2(1f, 1f);
            rwRt.offsetMin = new Vector2(15, 40);
            rwRt.offsetMax = new Vector2(-25, -150);
            _rewardsText = rwGo.AddComponent<Text>();
            _rewardsText.font = defaultFont;
            _rewardsText.fontSize = 16;
            _rewardsText.lineSpacing = 1.35f;
            _rewardsText.color = new Color(1f, 0.92f, 0.75f, 1f);
            _rewardsText.text = "【首通戰役榮譽獎勵】：\n  • ✦ 寶石 x300\n  • 榮譽指揮官晉升勳章";

            // 6. 右下出戰編隊整備區 (TeamRosterPanel)
            var teamPanelGo = ProceduralUiKit.CreateUiObject("TeamRosterPanel", transform);
            var teamRt = teamPanelGo.GetComponent<RectTransform>();
            teamRt.anchorMin = new Vector2(1f, 0f);
            teamRt.anchorMax = new Vector2(1f, 0f);
            teamRt.pivot = new Vector2(1f, 0f);
            teamRt.anchoredPosition = new Vector2(-30, 25);
            teamRt.sizeDelta = new Vector2(1420, 390);

            var teamBg = teamPanelGo.AddComponent<Image>();
            teamBg.sprite = ProceduralUiKit.GetGradientSprite(1420, 390, new Color(0.08f, 0.12f, 0.20f, 0.95f), new Color(0.04f, 0.06f, 0.12f, 0.95f), new Color(0.2f, 0.85f, 1f, 0.6f), 2, 8);

            // 編隊標題與人數
            var tCountGo = ProceduralUiKit.CreateUiObject("TeamCountTitle", teamPanelGo.transform);
            var tCountRt = tCountGo.GetComponent<RectTransform>();
            tCountRt.anchorMin = new Vector2(0f, 1f);
            tCountRt.anchorMax = new Vector2(1f, 1f);
            tCountRt.pivot = new Vector2(0f, 1f);
            tCountRt.anchoredPosition = new Vector2(25, -15);
            tCountRt.sizeDelta = new Vector2(-350, 30);
            _teamCountText = tCountGo.AddComponent<Text>();
            _teamCountText.font = defaultFont;
            _teamCountText.fontSize = 18;
            _teamCountText.fontStyle = FontStyle.Bold;
            _teamCountText.color = new Color(0.2f, 0.9f, 1f, 1f);
            _teamCountText.text = "出戰小隊編組：3 / 10 人 (點擊角色卡切換上陣)";

            // 角色列表 Grid
            var gridGo = ProceduralUiKit.CreateUiObject("RosterGrid", teamPanelGo.transform);
            var gRt = gridGo.GetComponent<RectTransform>();
            gRt.anchorMin = new Vector2(0f, 0f);
            gRt.anchorMax = new Vector2(1f, 1f);
            gRt.offsetMin = new Vector2(25, 20);
            gRt.offsetMax = new Vector2(-350, -55);

            var hlg = gridGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            _rosterGridRoot = gridGo.transform;

            // 我方主堡強化面板 (Bastion Fortify Card)
            var basePanelGo = ProceduralUiKit.CreateUiObject("BastionFortifyPanel", teamPanelGo.transform);
            var bpRt = basePanelGo.GetComponent<RectTransform>();
            bpRt.anchorMin = new Vector2(1f, 1f);
            bpRt.anchorMax = new Vector2(1f, 1f);
            bpRt.pivot = new Vector2(1f, 1f);
            bpRt.anchoredPosition = new Vector2(-25, -20);
            bpRt.sizeDelta = new Vector2(290, 210);
            var bpImg = basePanelGo.AddComponent<Image>();
            bpImg.sprite = ProceduralUiKit.GetGradientSprite(290, 210, new Color(0.10f, 0.16f, 0.26f, 0.96f), new Color(0.06f, 0.10f, 0.18f, 0.96f), new Color(0.35f, 0.85f, 1f, 0.7f), 2, 8);

            var blGo = ProceduralUiKit.CreateUiObject("BaseLevelText", basePanelGo.transform);
            var blRt = blGo.GetComponent<RectTransform>();
            blRt.anchorMin = new Vector2(0f, 1f);
            blRt.anchorMax = new Vector2(1f, 1f);
            blRt.pivot = new Vector2(0.5f, 1f);
            blRt.anchoredPosition = new Vector2(0, -14);
            blRt.sizeDelta = new Vector2(-24, 32);
            _baseLevelText = blGo.AddComponent<Text>();
            _baseLevelText.font = defaultFont;
            _baseLevelText.fontSize = 18;
            _baseLevelText.fontStyle = FontStyle.Bold;
            _baseLevelText.alignment = TextAnchor.MiddleLeft;
            _baseLevelText.color = new Color(0.45f, 0.92f, 1f, 1f);
            _baseLevelText.text = "🏰 我方主堡 LV.1 / 20";

            var bhpGo = ProceduralUiKit.CreateUiObject("BaseHpText", basePanelGo.transform);
            var bhpRt = bhpGo.GetComponent<RectTransform>();
            bhpRt.anchorMin = new Vector2(0f, 0.44f);
            bhpRt.anchorMax = new Vector2(1f, 0.78f);
            bhpRt.offsetMin = new Vector2(14, 0);
            bhpRt.offsetMax = new Vector2(-14, 0);
            _baseHpText = bhpGo.AddComponent<Text>();
            _baseHpText.font = defaultFont;
            _baseHpText.fontSize = 15;
            _baseHpText.alignment = TextAnchor.MiddleLeft;
            _baseHpText.color = new Color(0.92f, 0.97f, 1f, 0.95f);
            _baseHpText.text = "耐久：20,000 → 22,500 (+2,500)";

            var upGo = ProceduralUiKit.CreateUiObject("BtnUpgradeBase", basePanelGo.transform);
            var upRt = upGo.GetComponent<RectTransform>();
            upRt.anchorMin = new Vector2(0f, 0f);
            upRt.anchorMax = new Vector2(1f, 0f);
            upRt.pivot = new Vector2(0.5f, 0f);
            upRt.anchoredPosition = new Vector2(0, 14);
            upRt.sizeDelta = new Vector2(-24, 52);
            var upImg = upGo.AddComponent<Image>();
            upImg.sprite = ProceduralUiKit.GetGradientSprite(266, 52, new Color(0.14f, 0.55f, 0.78f, 1f), new Color(0.08f, 0.35f, 0.56f, 1f), new Color(0.5f, 0.95f, 1f, 0.9f), 2, 6);
            _btnUpgradeBase = upGo.AddComponent<Button>();
            _btnUpgradeBase.onClick.AddListener(OnClickUpgradeBase);

            var upTxtGo = ProceduralUiKit.CreateUiObject("Text", upGo.transform);
            ProceduralUiKit.StretchFull(upTxtGo.GetComponent<RectTransform>());
            _btnUpgradeBaseText = upTxtGo.AddComponent<Text>();
            _btnUpgradeBaseText.font = defaultFont;
            _btnUpgradeBaseText.fontSize = 16;
            _btnUpgradeBaseText.fontStyle = FontStyle.Bold;
            _btnUpgradeBaseText.alignment = TextAnchor.MiddleCenter;
            _btnUpgradeBaseText.color = Color.white;
            _btnUpgradeBaseText.text = "🔨 強化主堡 (500 G)";

            // 烈焰出擊大按鈕 (Sortie Action Button)
            var sortieGo = ProceduralUiKit.CreateUiObject("BtnStartBattle", teamPanelGo.transform);
            var sRt = sortieGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(1f, 0f);
            sRt.anchorMax = new Vector2(1f, 0f);
            sRt.pivot = new Vector2(1f, 0f);
            sRt.anchoredPosition = new Vector2(-25, 22);
            sRt.sizeDelta = new Vector2(290, 96);

            var sImg = sortieGo.AddComponent<Image>();
            sImg.sprite = ProceduralUiKit.GetGradientSprite(280, 100, new Color(1f, 0.75f, 0.12f, 1f), new Color(1f, 0.32f, 0.12f, 1f), new Color(1f, 0.95f, 0.6f, 1f), 3, 8);
            _btnStartBattle = sortieGo.AddComponent<Button>();
            _btnStartBattle.onClick.AddListener(OnClickStartBattle);

            var sTxtGo = ProceduralUiKit.CreateUiObject("Text", sortieGo.transform);
            ProceduralUiKit.StretchFull(sTxtGo.GetComponent<RectTransform>());
            _btnStartBattleText = sTxtGo.AddComponent<Text>();
            _btnStartBattleText.font = defaultFont;
            _btnStartBattleText.fontSize = 28;
            _btnStartBattleText.fontStyle = FontStyle.Bold;
            _btnStartBattleText.alignment = TextAnchor.MiddleCenter;
            _btnStartBattleText.color = new Color(0.12f, 0.08f, 0.04f, 1f);
            _btnStartBattleText.text = "▶ 開始作戰\n(SORTIE)";

            // 7. Toast Banner
            var toastGo = ProceduralUiKit.CreateUiObject("ToastBanner", transform);
            var toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(0.5f, 0.25f);
            toastRt.anchorMax = new Vector2(0.5f, 0.25f);
            toastRt.sizeDelta = new Vector2(620, 52);
            var toastImg = toastGo.AddComponent<Image>();
            toastImg.sprite = ProceduralUiKit.GetPillSprite(620, 52, new Color(0.06f, 0.09f, 0.15f, 0.96f), new Color(0f, 0.85f, 1f, 0.8f));
            _toastCanvasGroup = toastGo.AddComponent<CanvasGroup>();
            _toastRoot = toastGo;

            var tTxtGo = ProceduralUiKit.CreateUiObject("Txt", toastGo.transform);
            ProceduralUiKit.StretchFull(tTxtGo.GetComponent<RectTransform>(), 8);
            _toastText = tTxtGo.AddComponent<Text>();
            _toastText.font = defaultFont;
            _toastText.fontSize = 18;
            _toastText.alignment = TextAnchor.MiddleCenter;
            _toastText.color = new Color(1f, 0.95f, 0.75f, 1f);
            _toastText.text = "提示訊息";
            _toastRoot.SetActive(false);

            RefreshChapterTabs();
        }

        private Text CreatePill(Transform parent, string name, Vector2 pos, string initVal, Color textColor, Font font)
        {
            var pillGo = ProceduralUiKit.CreateUiObject(name, parent);
            var rt = pillGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(160, 40);

            var bgGo = ProceduralUiKit.CreateUiObject("Bg", pillGo.transform);
            ProceduralUiKit.StretchFull(bgGo.GetComponent<RectTransform>());
            var img = bgGo.AddComponent<Image>();
            img.sprite = ProceduralUiKit.GetPillSprite(160, 40, new Color(0.08f, 0.12f, 0.20f, 0.92f), new Color(textColor.r, textColor.g, textColor.b, 0.6f));

            var txt = pillGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 18;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = textColor;
            txt.text = initVal;

            return txt;
        }

        #endregion
    }
}
