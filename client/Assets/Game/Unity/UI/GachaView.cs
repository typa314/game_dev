using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.Gacha;
using Game.Domain.MasterData;
using Game.Domain.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 正式 UGUI 抽卡契約招募介面（依據 design/07_gacha_and_encounter.md 與 design/14_ui_screens.md §1.2 規劃設計）。
    /// 二次元戰術終端科技感風格：多卡池切換、特選角色立繪大看板、保底天井進度條、
    /// 矩陣充能稀有度色階光效演出、單抽全螢幕角色登場揭曉（含台詞與 NEW! 徽章）、
    /// 十連抽取戰術卡牌陣列結算、動態機率詳情彈窗與招募歷史紀錄查詢。
    /// </summary>
    [DisallowMultipleComponent]
    public class GachaView : UnityScreenBase, IGachaView
    {
        public override string ScreenId => "Screen_Gacha";
        [Header("UI 基礎元件")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasScaler _canvasScaler;
        [SerializeField] private Image _backgroundImage;

        [Header("頂部戰術列")]
        [SerializeField] private Button _btnBack;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _subTitleText;
        [SerializeField] private Text _gemText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _btnAddGemsDebug;
        [SerializeField] private Button _btnRates;
        [SerializeField] private Button _btnHistory;

        [Header("卡池切換標籤列")]
        [SerializeField] private Transform _bannerTabsRoot;
        [SerializeField] private readonly List<Button> _bannerTabButtons = new List<Button>();

        [Header("中央特選角色展示看板")]
        [SerializeField] private Image _featuredPortraitImage;
        [SerializeField] private Image _pedestalHologram;
        [SerializeField] private Text _featuredRarityText;
        [SerializeField] private Text _featuredNameText;
        [SerializeField] private Text _featuredTitleText;
        [SerializeField] private Text _featuredSkillText;

        [Header("保底進度條與說明")]
        [SerializeField] private Image _pityProgressBarFill;
        [SerializeField] private Text _pityProgressText;
        [SerializeField] private Text _pityNoticeText;

        [Header("底部招募操作區")]
        [SerializeField] private Toggle _skipAnimToggle;
        [SerializeField] private Button _btnPullSingle;
        [SerializeField] private Text _pullSingleCostText;
        [SerializeField] private Button _btnPullTen;
        [SerializeField] private Text _pullTenCostText;

        [Header("召喚矩陣光效演出層")]
        [SerializeField] private GameObject _summonAnimLayer;
        [SerializeField] private Image _summonMatrixImage;
        [SerializeField] private Image _summonFlareImage;
        [SerializeField] private Text _summonStatusText;
        [SerializeField] private Button _btnSkipAnim;

        [Header("單抽登場揭曉彈窗 (Single Reveal)")]
        [SerializeField] private GameObject _singleRevealModal;
        [SerializeField] private Image _revealPortraitImage;
        [SerializeField] private Text _revealRarityBadge;
        [SerializeField] private Text _revealCharacterName;
        [SerializeField] private Text _revealRoleTag;
        [SerializeField] private Text _revealQuoteText;
        [SerializeField] private GameObject _revealNewBadge;
        [SerializeField] private GameObject _revealDuplicateBadge;
        [SerializeField] private Button _revealClickAnywhereButton;

        [Header("十連抽戰術卡牌結算彈窗 (Ten Results)")]
        [SerializeField] private GameObject _tenResultModal;
        [SerializeField] private Transform _resultGridRoot;
        [SerializeField] private Button _btnResultRePull;
        [SerializeField] private Button _btnResultConfirm;

        [Header("機率詳情彈窗")]
        [SerializeField] private GameObject _rateModal;
        [SerializeField] private Text _rateDetailsText;
        [SerializeField] private Button _btnRateModalClose;

        [Header("招募歷史彈窗")]
        [SerializeField] private GameObject _historyModal;
        [SerializeField] private Text _historyListText;
        [SerializeField] private Button _btnHistoryModalClose;

        [Header("浮動提示 (Toast)")]
        [SerializeField] private GameObject _toastRoot;
        [SerializeField] private Text _toastText;
        [SerializeField] private CanvasGroup _toastCanvasGroup;

        // Presenter & State
        private GachaPresenter _presenter;
        public GachaPresenter Presenter => _presenter;

        // M2-6 測試與狀態暴露屬性
        public string CurrentBannerId => _currentBannerId;
        public string FeaturedCharacterId { get; private set; } = "char01_claire";
        public string FeaturedCharacterName => _featuredNameText != null ? _featuredNameText.text : string.Empty;
        public string FeaturedSkillDescription => _featuredSkillText != null ? _featuredSkillText.text : string.Empty;

        private LobbyView _lobbyView;
        private string _currentBannerId = "banner_std";
        private Coroutine _toastCoroutine;
        private Coroutine _animCoroutine;
        private bool _isInitialized;
        private IReadOnlyList<GachaResultEntry> _lastPullResults;

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
            CloseAllModals();
        }

        public override bool OnBackRequested()
        {
            if (_rateModal != null && _rateModal.activeSelf)
            {
                _rateModal.SetActive(false);
                return true;
            }
            if (_historyModal != null && _historyModal.activeSelf)
            {
                _historyModal.SetActive(false);
                return true;
            }
            if (_singleRevealModal != null && _singleRevealModal.activeSelf)
            {
                _singleRevealModal.SetActive(false);
                return true;
            }
            if (_tenResultModal != null && _tenResultModal.activeSelf)
            {
                _tenResultModal.SetActive(false);
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

            // 鍵盤快捷鍵：Esc 返回上一層
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (GameServices.Navigator != null && GameServices.Navigator.CurrentScreen == this)
                {
                    GameServices.Navigator.HandleBackRequest();
                }
                else
                {
                    HandleEscapeKey();
                }
            }
        }

        public void Initialize(GameCore core)
        {
            if (core == null) return;
            _presenter = new GachaPresenter(core);
            _presenter.AttachView(this);
            _isInitialized = true;

            RefreshAll();
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
            RefreshAll();
        }

        public void Close()
        {
            // 關閉所有彈窗
            CloseAllModals();

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

        private void HandleEscapeKey()
        {
            if (_rateModal != null && _rateModal.activeSelf)
            {
                _rateModal.SetActive(false);
                return;
            }
            if (_historyModal != null && _historyModal.activeSelf)
            {
                _historyModal.SetActive(false);
                return;
            }
            if (_singleRevealModal != null && _singleRevealModal.activeSelf)
            {
                _singleRevealModal.SetActive(false);
                return;
            }
            if (_tenResultModal != null && _tenResultModal.activeSelf)
            {
                _tenResultModal.SetActive(false);
                return;
            }
            Close();
        }

        public void RefreshAll()
        {
            RefreshHeaderCurrencies();
            RefreshBannerDisplay(_currentBannerId);
        }

        public void RefreshHeaderCurrencies()
        {
            if (_presenter == null) return;

            long gems = _presenter.GetGemCount();
            long gold = _presenter.GetGoldCount();

            if (_gemText != null) _gemText.text = $"{gems:N0}";
            if (_goldText != null) _goldText.text = $"{gold:N0}";
        }

        public void SwitchBanner(string bannerId)
        {
            _currentBannerId = bannerId;
            RefreshBannerDisplay(bannerId);
        }

        public void RefreshBannerDisplay(string bannerId)
        {
            if (_presenter == null) return;

            var banner = _presenter.GetBanner(bannerId);
            if (banner == null) return;

            // 標題與副標
            if (_titleText != null) _titleText.text = "✦ 契約招募 // TACTICAL RECRUIT ✦";
            if (_subTitleText != null)
            {
                string bannerName = banner.Id switch
                {
                    "banner_ur" => "全階進階特選卡池 (UR ADVANCED SPEC)",
                    "banner_sakura_up" => "春櫻祈願特選卡池 (SAKURA PICKUP)",
                    _ => "常駐契約卡池 (STANDARD PROTOCOL)"
                };
                _subTitleText.text = $"ACTIVE PROTOCOL: {bannerName}";
            }

            // 保底天井
            int pity = _presenter.GetPityCount(bannerId);
            int hardAt = _presenter.GetPityLimit(bannerId);
            float ratio = hardAt > 0 ? Mathf.Clamp01((float)pity / hardAt) : 0f;

            if (_pityProgressBarFill != null)
            {
                _pityProgressBarFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
            }
            if (_pityProgressText != null)
            {
                int remaining = Math.Max(0, hardAt - pity);
                _pityProgressText.text = $"保底進度：{pity} / {hardAt} 抽  (距必得最高階角色還需 {remaining} 抽)";
            }
            if (_pityNoticeText != null)
            {
                _pityNoticeText.text = "※ 每次十連招募必定獲得 SR 或以上契約角色！若抽中最高階角色保底計數重置。";
            }

            // 特選角色展示 (M2-6 動態查詢主打角色)
            var featuredChar = _presenter.GetFeaturedCharacter(bannerId);
            string featuredId = featuredChar?.Id ?? "char01_claire";
            FeaturedCharacterId = featuredId;
            string displayName = GetCharacterDisplayName(featuredId);
            if (featuredId == "char18_sakura_miko" || banner.Id == "banner_sakura_up")
            {
                if (_featuredRarityText != null) _featuredRarityText.text = "★★★ SSR PICKUP ★★★";
                if (_featuredRarityText != null) _featuredRarityText.color = new Color(1f, 0.72f, 0.85f, 1f);
                if (_featuredNameText != null) _featuredNameText.text = $"{displayName} // SAKURA (SSR)";
                if (_featuredTitleText != null) _featuredTitleText.text = "「神代櫻花巫女・破魔祈願」 // 範圍輔助";
                if (_featuredSkillText != null) _featuredSkillText.text = "★ 專屬大招【千本櫻神樂】：召喚櫻花結界淨化戰場並造成範圍傷害！";
                if (_featuredPortraitImage != null)
                {
                    Sprite sp = TryLoadCharacterSprite("char18_sakura") ?? TryLoadCharacterSprite(featuredId);
                    if (sp != null)
                    {
                        _featuredPortraitImage.sprite = sp;
                        _featuredPortraitImage.color = Color.white;
                    }
                }
                return;
            }
            if (banner.Id == "banner_ur")
            {
                if (_featuredRarityText != null) _featuredRarityText.text = "★★★ UR ★★★";
                if (_featuredRarityText != null) _featuredRarityText.color = new Color(1f, 0.4f, 0.9f, 1f);
                if (_featuredNameText != null) _featuredNameText.text = "克萊兒 // CLAIRE (UR)";
                if (_featuredTitleText != null) _featuredTitleText.text = "「終焉女武神・全領域破滅降臨」";
                if (_featuredSkillText != null) _featuredSkillText.text = "★ 全域特異解放：全屬性大幅躍進，支援全角色全階進階契約！";
            }
            else
            {
                if (_featuredRarityText != null) _featuredRarityText.text = "★★★ SSR ★★★";
                if (_featuredRarityText != null) _featuredRarityText.color = new Color(1f, 0.85f, 0.2f, 1f);
                if (_featuredNameText != null) _featuredNameText.text = "克萊兒 // CLAIRE (SSR)";
                if (_featuredTitleText != null) _featuredTitleText.text = "「神聖女武神・審判之刃」 // 遠程穿甲";
                if (_featuredSkillText != null) _featuredSkillText.text = "★ 專屬大招【女武神戰歌】：全體友軍攻擊力 +50%，持續 8 秒！";
            }

            // 嘗試載入立繪
            if (_featuredPortraitImage != null)
            {
                Sprite sp = TryLoadCharacterSprite("char01_claire");
                if (sp != null)
                {
                    _featuredPortraitImage.sprite = sp;
                    _featuredPortraitImage.color = Color.white;
                }
            }
        }

        #region 招募按鈕與演出流程

        public void OnClickPullSingle()
        {
            if (_presenter == null) return;
            long gems = _presenter.GetGemCount();
            if (gems < 150)
            {
                ShowToast($"✦ 寶石不足！單抽需要 150 寶石（當前持有：{gems}）。可點擊上方 [+] 補充測試！");
                return;
            }

            ExecutePull(1);
        }

        public void OnClickPullTen()
        {
            if (_presenter == null) return;
            long gems = _presenter.GetGemCount();
            if (gems < 1500)
            {
                ShowToast($"✦ 寶石不足！十連招募需要 1500 寶石（當前持有：{gems}）。可點擊上方 [+] 補充測試！");
                return;
            }

            ExecutePull(10);
        }

        private void ExecutePull(int count)
        {
            var res = _presenter.Pull(_currentBannerId, count);
            if (res.Ok)
            {
                _lastPullResults = res.Value;
                RefreshHeaderCurrencies();
                RefreshBannerDisplay(_currentBannerId);

                bool skipAnim = (_skipAnimToggle != null && _skipAnimToggle.isOn) || !Application.isPlaying;
                if (skipAnim)
                {
                    DisplayPullResultsImmediate(res.Value);
                }
                else
                {
                    StartSummonAnimation(res.Value);
                }
            }
            else
            {
                ShowToast($"招募失敗：{res.Error}");
            }
        }

        private void StartSummonAnimation(IReadOnlyList<GachaResultEntry> results)
        {
            if (_animCoroutine != null) StopCoroutine(_animCoroutine);
            _animCoroutine = StartCoroutine(CoSummonAnimation(results));
        }

        private IEnumerator CoSummonAnimation(IReadOnlyList<GachaResultEntry> results)
        {
            if (_summonAnimLayer == null)
            {
                DisplayPullResultsImmediate(results);
                yield break;
            }

            _summonAnimLayer.SetActive(true);

            // 判斷本次抽取最高稀有度
            string highestRarity = "R";
            foreach (var r in results)
            {
                if (r.Draw.Rarity == "UR") { highestRarity = "UR"; break; }
                if (r.Draw.Rarity == "SSR") highestRarity = "SSR";
                else if (r.Draw.Rarity == "SR" && highestRarity != "SSR") highestRarity = "SR";
            }

            Color flareColor = GetRarityColor(highestRarity);
            if (_summonFlareImage != null) _summonFlareImage.color = new Color(flareColor.r, flareColor.g, flareColor.b, 0.85f);
            if (_summonStatusText != null) _summonStatusText.text = $"⚡ 矩陣共鳴中... 偵測到 {highestRarity} 級特異能量光譜！";

            // 播放旋轉與淡入充能
            float duration = 1.2f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                if (_summonMatrixImage != null)
                {
                    _summonMatrixImage.rectTransform.Rotate(Vector3.forward, -240f * Time.deltaTime);
                    float scale = 0.8f + 0.3f * Mathf.Sin(t * Mathf.PI);
                    _summonMatrixImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
                }

                yield return null;
            }

            _summonAnimLayer.SetActive(false);
            DisplayPullResultsImmediate(results);
        }

        public void SkipSummonAnimation()
        {
            if (_animCoroutine != null)
            {
                StopCoroutine(_animCoroutine);
                _animCoroutine = null;
            }
            if (_summonAnimLayer != null) _summonAnimLayer.SetActive(false);
            if (_lastPullResults != null)
            {
                DisplayPullResultsImmediate(_lastPullResults);
            }
        }

        private void DisplayPullResultsImmediate(IReadOnlyList<GachaResultEntry> results)
        {
            if (results == null || results.Count == 0) return;

            if (results.Count == 1)
            {
                // 單抽大卡面全螢幕展示
                ShowSingleRevealModal(results[0]);
            }
            else
            {
                // 十連戰術卡牌結算
                ShowTenResultsModal(results);
            }
        }

        #endregion

        #region 單抽大卡面與十連結算呈現

        public void ShowSingleRevealModal(GachaResultEntry entry)
        {
            if (_singleRevealModal == null) return;
            _singleRevealModal.SetActive(true);

            string charId = entry.Draw.Char;
            string rarity = entry.Draw.Rarity;
            string charName = GetCharacterDisplayName(charId);

            if (_revealRarityBadge != null)
            {
                _revealRarityBadge.text = $"★ {rarity} ★";
                _revealRarityBadge.color = GetRarityColor(rarity);
            }
            if (_revealCharacterName != null) _revealCharacterName.text = $"{charName} // {charId.ToUpper()}";
            if (_revealRoleTag != null) _revealRoleTag.text = GetRoleDisplayTitle(charId);
            if (_revealQuoteText != null) _revealQuoteText.text = GetCharacterDebutQuote(charId);

            if (_revealNewBadge != null) _revealNewBadge.SetActive(entry.IsNew);
            if (_revealDuplicateBadge != null) _revealDuplicateBadge.SetActive(!entry.IsNew);

            if (_revealPortraitImage != null)
            {
                Sprite sp = TryLoadCharacterSprite(charId);
                if (sp != null)
                {
                    _revealPortraitImage.sprite = sp;
                    _revealPortraitImage.color = Color.white;
                }
            }
        }

        public void ShowTenResultsModal(IReadOnlyList<GachaResultEntry> results)
        {
            if (_tenResultModal == null || _resultGridRoot == null) return;
            _tenResultModal.SetActive(true);

            // 清理舊卡片子節點
            foreach (Transform child in _resultGridRoot)
            {
                Destroy(child.gameObject);
            }

            // 動態構建 10 張戰術卡牌
            for (int i = 0; i < results.Count; i++)
            {
                var entry = results[i];
                CreateResultCardUI(_resultGridRoot, i, entry);
            }
        }

        private void CreateResultCardUI(Transform parent, int index, GachaResultEntry entry)
        {
            string charId = entry.Draw.Char;
            string rarity = entry.Draw.Rarity;
            Color rarityColor = GetRarityColor(rarity);
            string charName = GetCharacterDisplayName(charId);

            var cardGo = CreateUIObject($"Card_{index}_{charId}", parent);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.sizeDelta = new Vector2(150, 215);

            // 卡牌底圖
            var cardImg = cardGo.AddComponent<Image>();
            cardImg.sprite = CreateGradientSprite(150, 215, new Color(0.08f, 0.12f, 0.20f, 0.95f), new Color(0.04f, 0.06f, 0.10f, 0.95f), rarityColor, 2, 6);

            // 頂部稀有度邊籤
            var rBadgeGo = CreateUIObject("RarityBadge", cardGo.transform);
            var rBadgeRt = rBadgeGo.GetComponent<RectTransform>();
            rBadgeRt.anchorMin = new Vector2(0f, 1f);
            rBadgeRt.anchorMax = new Vector2(1f, 1f);
            rBadgeRt.pivot = new Vector2(0.5f, 1f);
            rBadgeRt.anchoredPosition = new Vector2(0, -6);
            rBadgeRt.sizeDelta = new Vector2(-16, 26);
            var rBadgeImg = rBadgeGo.AddComponent<Image>();
            rBadgeImg.color = new Color(rarityColor.r * 0.25f, rarityColor.g * 0.25f, rarityColor.b * 0.25f, 0.9f);

            var rTxtGo = CreateUIObject("RarityText", rBadgeGo.transform);
            StretchFull(rTxtGo.GetComponent<RectTransform>());
            var rTxt = rTxtGo.AddComponent<Text>();
            rTxt.font = GetDefaultFont();
            rTxt.fontSize = 16;
            rTxt.fontStyle = FontStyle.Bold;
            rTxt.alignment = TextAnchor.MiddleCenter;
            rTxt.color = rarityColor;
            rTxt.text = $"★ {rarity} ★";

            // 角色胸像圖標
            var avatarGo = CreateUIObject("Avatar", cardGo.transform);
            var avatarRt = avatarGo.GetComponent<RectTransform>();
            avatarRt.anchorMin = new Vector2(0.5f, 0.55f);
            avatarRt.anchorMax = new Vector2(0.5f, 0.55f);
            avatarRt.pivot = new Vector2(0.5f, 0.5f);
            avatarRt.sizeDelta = new Vector2(110, 110);
            var avImg = avatarGo.AddComponent<Image>();
            avImg.sprite = CreateCircleSprite(110, new Color(0.12f, 0.18f, 0.28f, 1f), rarityColor, 2);

            Sprite bustSp = TryLoadCharacterSprite($"{charId}_bust");
            if (bustSp == null) bustSp = TryLoadCharacterSprite(charId);
            if (bustSp != null)
            {
                var iconGo = CreateUIObject("Icon", avatarGo.transform);
                StretchFull(iconGo.GetComponent<RectTransform>(), 4);
                var ic = iconGo.AddComponent<Image>();
                ic.sprite = bustSp;
                ic.preserveAspect = true;
            }
            else
            {
                // 無立繪時的備用二次元戰術幾何徽章
                var initialGo = CreateUIObject("Initial", avatarGo.transform);
                StretchFull(initialGo.GetComponent<RectTransform>());
                var initTxt = initialGo.AddComponent<Text>();
                initTxt.font = GetDefaultFont();
                initTxt.fontSize = 32;
                initTxt.fontStyle = FontStyle.Bold;
                initTxt.alignment = TextAnchor.MiddleCenter;
                initTxt.color = rarityColor;
                initTxt.text = charName.Substring(0, Math.Min(1, charName.Length));
            }

            // 底部名稱列
            var nameGo = CreateUIObject("Name", cardGo.transform);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0, 24);
            nameRt.sizeDelta = new Vector2(-12, 22);
            var nameTxt = nameGo.AddComponent<Text>();
            nameTxt.font = GetDefaultFont();
            nameTxt.fontSize = 15;
            nameTxt.fontStyle = FontStyle.Bold;
            nameTxt.alignment = TextAnchor.MiddleCenter;
            nameTxt.color = Color.white;
            nameTxt.text = charName;

            // 底部狀態（NEW! 或是重複轉換）
            var statusGo = CreateUIObject("Status", cardGo.transform);
            var statusRt = statusGo.GetComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(1f, 0f);
            statusRt.pivot = new Vector2(0.5f, 0f);
            statusRt.anchoredPosition = new Vector2(0, 5);
            statusRt.sizeDelta = new Vector2(-12, 18);
            var statusTxt = statusGo.AddComponent<Text>();
            statusTxt.font = GetDefaultFont();
            statusTxt.fontSize = 11;
            statusTxt.alignment = TextAnchor.MiddleCenter;

            if (entry.IsNew)
            {
                statusTxt.color = new Color(1f, 0.35f, 0.2f, 1f);
                statusTxt.fontStyle = FontStyle.Bold;
                statusTxt.text = "✦ NEW 契約成立 ✦";

                // 右上角加 NEW! 彩帶
                var newTagGo = CreateUIObject("NewRibbon", cardGo.transform);
                var newRt = newTagGo.GetComponent<RectTransform>();
                newRt.anchorMin = new Vector2(1f, 1f);
                newRt.anchorMax = new Vector2(1f, 1f);
                newRt.pivot = new Vector2(1f, 1f);
                newRt.anchoredPosition = new Vector2(4, 4);
                newRt.sizeDelta = new Vector2(50, 20);
                var newImg = newTagGo.AddComponent<Image>();
                newImg.color = new Color(1f, 0.25f, 0.25f, 0.95f);
                var nTxtGo = CreateUIObject("NTxt", newTagGo.transform);
                StretchFull(nTxtGo.GetComponent<RectTransform>());
                var nTxt = nTxtGo.AddComponent<Text>();
                nTxt.font = GetDefaultFont();
                nTxt.fontSize = 11;
                nTxt.fontStyle = FontStyle.Bold;
                nTxt.alignment = TextAnchor.MiddleCenter;
                nTxt.color = Color.white;
                nTxt.text = "NEW!";
            }
            else
            {
                statusTxt.color = new Color(0.2f, 0.85f, 1f, 0.85f);
                statusTxt.text = "◆ 進階卡片 +1";
            }
        }

        #endregion

        #region 機率與歷史彈窗

        public void OpenRateModal()
        {
            if (_rateModal == null || _presenter == null) return;
            _rateModal.SetActive(true);

            var banner = _presenter.GetBanner(_currentBannerId);
            if (banner == null) return;

            string info = $"【{banner.Id} 卡池機率公開規格】\n\n" +
                          $"◆ 卡池類型：{banner.Type.ToUpper()}\n" +
                          $"◆ 單抽花費：✦ {banner.Cost.Single[0].N} 寶石\n" +
                          $"◆ 十連花費：✦ {banner.Cost.Ten[0].N} 寶石\n\n" +
                          $"【綜合稀有度獲得機率】\n";

            foreach (var kvp in banner.Rates)
            {
                info += $"  • {kvp.Key} 稀有度：{kvp.Value * 100.0:F2}%\n";
            }

            info += $"\n【保底與保證機制】\n" +
                    $"  • 天井保底 (Hard Pity)：連續 {banner.Pity.HardAt} 抽未獲得 {banner.Pity.Rarity} 時，第 {banner.Pity.HardAt} 抽必定獲得 {banner.Pity.Rarity}！\n" +
                    $"  • 十連保底：每十連招募最後一抽必定為 {banner.TenPullGuarantee} 或以上角色！\n" +
                    $"  • 重複角色轉換：已獲得之角色將自動轉化為進階專屬卡片與覺醒素材。\n\n" +
                    $"【各稀有度收錄角色】\n";

            foreach (var pool in banner.Pool)
            {
                info += $"  [{pool.Key}]：{string.Join(", ", pool.Value)}\n";
            }

            if (_rateDetailsText != null) _rateDetailsText.text = info;
        }

        public void OpenHistoryModal()
        {
            if (_historyModal == null || _presenter == null) return;
            _historyModal.SetActive(true);

            var history = _presenter.GetHistory(40);
            if (history == null || history.Count == 0)
            {
                if (_historyListText != null) _historyListText.text = "「暫無招募歷史紀錄，立即進行特異契約招募吧！」";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("最近 40 筆契約招募紀錄 (由新至舊)：\n");
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var h = history[i];
                string charName = GetCharacterDisplayName(h.Char);
                sb.AppendLine($"• [{h.Rarity}] {charName} ({h.Char})  |  卡池: {h.Banner}  |  {h.AtUtc}");
            }

            if (_historyListText != null) _historyListText.text = sb.ToString();
        }

        public void CloseAllModals()
        {
            if (_summonAnimLayer != null) _summonAnimLayer.SetActive(false);
            if (_singleRevealModal != null) _singleRevealModal.SetActive(false);
            if (_tenResultModal != null) _tenResultModal.SetActive(false);
            if (_rateModal != null) _rateModal.SetActive(false);
            if (_historyModal != null) _historyModal.SetActive(false);
        }

        #endregion

        #region IGachaView 實作

        public void ShowGachaResult(IReadOnlyList<GachaResultEntry> results)
        {
            _lastPullResults = results;
        }

        public void ShowError(string message)
        {
            ShowToast($"招募提示：{message}");
        }

        #endregion

        #region 輔助資訊與音效/台詞

        private string GetCharacterDisplayName(string charId)
        {
            return ProceduralUiKit.ResolveCharacterName(charId);
        }

        private string GetRoleDisplayTitle(string charId)
        {
            switch (charId)
            {
                case "char01_claire": return "ROLE: 遠程穿甲 // VALKYRIE LEADER";
                case "char02_luna": return "ROLE: 範圍法術 // MOONLIGHT MAGE";
                case "char03_scarlet": return "ROLE: 守護巨砲 // CRIMSON CANNON";
                case "char04_elysia": return "ROLE: 前鋒突擊 // HOLY KNIGHT";
                case "char05_rin": return "ROLE: 近戰狂戰 // ONI BERSERKER";
                default: return "ROLE: 特異戰術小隊隊員 // TACTICAL AGENT";
            }
        }

        private string GetCharacterDebutQuote(string charId)
        {
            switch (charId)
            {
                case "char01_claire": return "「我是克萊兒，從今刻起，將我的榮耀與利劍交付給您，指揮官。」";
                case "char02_luna": return "「月影所照之處，皆為極寒領域... 指揮官，準備好凍結敵人了嗎？」";
                case "char03_scarlet": return "「哼，呼叫我嗎？準備好見證猩紅巨砲的破壞威力了嗎，長官？」";
                case "char04_elysia": return "「指揮官，願聖靈之光常伴隨您身旁，我會守護全隊的安危！」";
                case "char05_rin": return "「鬼神之怒，無人可擋！指揮官，隨時可以為您撕碎敵陣！」";
                default: return "「特異契約者報到！隨時聽候指揮官調遣出擊！」";
            }
        }

        private Color GetRarityColor(string rarity)
        {
            switch (rarity)
            {
                case "UR": return new Color(1f, 0.45f, 0.95f, 1f);     // 幻彩霓虹粉紫
                case "SSR": return new Color(1f, 0.85f, 0.2f, 1f);     // 炫金耀眼
                case "SR": return new Color(0.72f, 0.4f, 1f, 1f);      // 皇室紫
                case "R": return new Color(0.15f, 0.85f, 1f, 1f);      // 戰術青藍
                default: return Color.white;
            }
        }

        public void OnClickAddDebugGems()
        {
            if (GameServices.Core?.Player != null)
            {
                GameServices.Core.Player.AddItem("gem", 1500);
                RefreshHeaderCurrencies();
                ShowToast("已補充 ✦ 1,500 寶石！立即體驗十連招募！");
            }
        }

        public void ShowToast(string message)
        {
            if (_toastRoot == null || _toastText == null) return;
            _toastText.text = message;
            _toastRoot.SetActive(true);

            if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
            _toastCoroutine = StartCoroutine(CoShowToast());
        }

        private IEnumerator CoShowToast()
        {
            if (_toastCanvasGroup != null) _toastCanvasGroup.alpha = 1f;
            yield return new WaitForSeconds(2.5f);
            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                if (_toastCanvasGroup != null) _toastCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / 0.5f);
                yield return null;
            }
            if (_toastRoot != null) _toastRoot.SetActive(false);
        }

        private Sprite TryLoadCharacterSprite(string artKey)
        {
            if (string.IsNullOrEmpty(artKey)) return null;
            return AssetService.ResolveStreamingSprite(
                $"portrait_{artKey}",
                new[] { $"Portraits/{artKey}.png" },
                new Vector2(0.5f, 0.5f),
                "[GachaView]");
        }

        #endregion

        #region 程序化 UI 階層構建 (EnsureUIHierarchy)

        public void EnsureUIHierarchy()
        {
            if (_canvas != null && _backgroundImage != null) return;

            Font defaultFont = GetDefaultFont();
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 1. Canvas & Scaler & Adapter
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;

            _canvasScaler = GetComponent<CanvasScaler>();
            if (_canvasScaler == null) _canvasScaler = gameObject.AddComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920, 1080);
            _canvasScaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (GetComponent<ResolutionAdapter>() == null)
            {
                gameObject.AddComponent<ResolutionAdapter>();
            }

            // 2. Background Panel
            var bgGo = CreateUIObject("BackgroundPanel", transform);
            StretchFull(bgGo.GetComponent<RectTransform>());
            _backgroundImage = bgGo.AddComponent<Image>();
            _backgroundImage.sprite = CreateGradientSprite(128, 128, new Color(0.05f, 0.07f, 0.12f, 1f), new Color(0.02f, 0.03f, 0.06f, 1f));

            // 3. Top Header Bar
            var headerGo = CreateUIObject("TopHeaderBar", transform);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.sizeDelta = new Vector2(0, 74);
            // 頂部戰術列完全透明懸浮，自然融入背景
            // var headerImg = headerGo.AddComponent<Image>();

            // 返回按鈕
            var backBtnGo = CreateTacticalButton("BtnBack", headerGo.transform, "◀ 返回大廳", new Vector2(25, 0), new Vector2(130, 42), new Color(0.18f, 0.12f, 0.15f, 0.95f), new Color(1f, 0.4f, 0.5f, 0.8f), defaultFont);
            _btnBack = backBtnGo.GetComponent<Button>();
            _btnBack.onClick.AddListener(Close);

            // 標題與副標
            var titleBoxGo = CreateUIObject("TitleBox", headerGo.transform);
            var titleBoxRt = titleBoxGo.GetComponent<RectTransform>();
            titleBoxRt.anchorMin = new Vector2(0f, 0.5f);
            titleBoxRt.anchorMax = new Vector2(0f, 0.5f);
            titleBoxRt.pivot = new Vector2(0f, 0.5f);
            titleBoxRt.anchoredPosition = new Vector2(175, 0);
            titleBoxRt.sizeDelta = new Vector2(400, 60);

            var titleGo = CreateUIObject("Title", titleBoxGo.transform);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.anchoredPosition = new Vector2(0, 4);
            _titleText = titleGo.AddComponent<Text>();
            _titleText.font = defaultFont;
            _titleText.fontSize = 20;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = new Color(1f, 0.95f, 0.82f, 1f);
            _titleText.text = "✦ 契約招募 // TACTICAL RECRUIT ✦";

            var subGo = CreateUIObject("SubTitle", titleBoxGo.transform);
            var subRt = subGo.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0f, 0f);
            subRt.anchorMax = new Vector2(1f, 0.5f);
            subRt.anchoredPosition = new Vector2(0, -2);
            _subTitleText = subGo.AddComponent<Text>();
            _subTitleText.font = defaultFont;
            _subTitleText.fontSize = 12;
            _subTitleText.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            _subTitleText.text = "ACTIVE PROTOCOL: 常駐特異召喚 (STANDARD PROTOCOL)";

            // 右側功能按鈕 (招募紀錄、機率詳情)
            var histBtnGo = CreateTacticalHeaderButton("BtnHistory", headerGo.transform, "招募紀錄", new Vector2(-20, 0), new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.2f, 0.8f, 1f, 0.7f), defaultFont);
            _btnHistory = histBtnGo.GetComponent<Button>();
            _btnHistory.onClick.AddListener(OpenHistoryModal);

            var rateBtnGo = CreateTacticalHeaderButton("BtnRates", headerGo.transform, "機率詳情", new Vector2(-148, 0), new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.2f, 0.8f, 1f, 0.7f), defaultFont);
            _btnRates = rateBtnGo.GetComponent<Button>();
            _btnRates.onClick.AddListener(OpenRateModal);

            // 右上角三大資源膠囊 (鑽石、金幣、+1500除錯加值) - 嵌入真實彩色圖示
            _gemText = CreateResourcePill(headerGo.transform, "GemLabel", new Vector2(-276, 0), "3,450", "icon_gem", new Color(0.2f, 0.9f, 1f, 1f), new Color(0f, 0.8f, 1f, 0.5f), defaultFont);
            _goldText = CreateResourcePill(headerGo.transform, "GoldLabel", new Vector2(-446, 0), "128,500", "icon_gold", new Color(1f, 0.85f, 0.2f, 1f), new Color(1f, 0.8f, 0.2f, 0.5f), defaultFont);

            var addGemGo = CreateTacticalButton("BtnAddGemsDebug", headerGo.transform, "+1500 鑽石", new Vector2(-616, 0), new Vector2(100, 36), new Color(0.12f, 0.25f, 0.20f, 0.95f), new Color(0.3f, 0.95f, 0.6f, 0.8f), defaultFont);
            var addRt = addGemGo.GetComponent<RectTransform>();
            addRt.anchorMin = new Vector2(1f, 0.5f);
            addRt.anchorMax = new Vector2(1f, 0.5f);
            addRt.pivot = new Vector2(1f, 0.5f);
            addRt.anchoredPosition = new Vector2(-616, 0);
            _btnAddGemsDebug = addGemGo.GetComponent<Button>();
            _btnAddGemsDebug.onClick.AddListener(OnClickAddDebugGems);

            // 4. 卡池切換標籤 (左側導航)
            var tabsGo = CreateUIObject("BannerTabsBar", transform);
            var tabsRt = tabsGo.GetComponent<RectTransform>();
            tabsRt.anchorMin = new Vector2(0f, 0.5f);
            tabsRt.anchorMax = new Vector2(0f, 0.5f);
            tabsRt.pivot = new Vector2(0f, 0.5f);
            tabsRt.anchoredPosition = new Vector2(30, 80);
            tabsRt.sizeDelta = new Vector2(250, 400);
            _bannerTabsRoot = tabsGo.transform;

            // 建立標準卡池 Tab
            var btnTab1 = CreateBannerTabButton(tabsGo.transform, "Tab_banner_std", "✨ 常駐特異契約", "SSR 克萊兒 登場率 UP!", new Vector2(0, 100), defaultFont, () => SwitchBanner("banner_std"));
            _bannerTabButtons.Add(btnTab1);

            // 建立進階卡池 Tab
            var btnTab2 = CreateBannerTabButton(tabsGo.transform, "Tab_banner_ur", "⚡ 全階進階特選", "UR & 全階進階解放", new Vector2(0, 0), defaultFont, () => SwitchBanner("banner_ur"));
            _bannerTabButtons.Add(btnTab2);
            var btnTab3 = CreateBannerTabButton(tabsGo.transform, "Tab_banner_sakura_up", "🌸 春櫻祈願特選", "SSR 櫻 機率 70% UP!", new Vector2(0, -100), defaultFont, () => SwitchBanner("banner_sakura_up"));
            _bannerTabButtons.Add(btnTab3);

            // 5. 中央主展示看板 (CenterBannerShowcase)
            var centerGo = CreateUIObject("CenterBannerShowcase", transform);
            var centerRt = centerGo.GetComponent<RectTransform>();
            centerRt.anchorMin = new Vector2(0.5f, 0.5f);
            centerRt.anchorMax = new Vector2(0.5f, 0.5f);
            centerRt.pivot = new Vector2(0.5f, 0.5f);
            centerRt.anchoredPosition = new Vector2(60, 40);
            centerRt.sizeDelta = new Vector2(960, 680);

            // 全息底台
            var pedGo = CreateUIObject("PedestalHologram", centerGo.transform);
            var pedRt = pedGo.GetComponent<RectTransform>();
            pedRt.anchorMin = new Vector2(0.5f, 0.2f);
            pedRt.anchorMax = new Vector2(0.5f, 0.2f);
            pedRt.pivot = new Vector2(0.5f, 0.5f);
            pedRt.sizeDelta = new Vector2(560, 120);
            _pedestalHologram = pedGo.AddComponent<Image>();
            _pedestalHologram.sprite = CreateCircleSprite(120, new Color(0f, 0.7f, 1f, 0.15f), new Color(0f, 0.9f, 1f, 0.8f), 3);

            // 看板立繪
            var portraitGo = CreateUIObject("FeaturedPortrait", centerGo.transform);
            var portRt = portraitGo.GetComponent<RectTransform>();
            portRt.anchorMin = new Vector2(0.5f, 0.52f);
            portRt.anchorMax = new Vector2(0.5f, 0.52f);
            portRt.pivot = new Vector2(0.5f, 0.5f);
            portRt.sizeDelta = new Vector2(520, 640);
            _featuredPortraitImage = portraitGo.AddComponent<Image>();
            _featuredPortraitImage.preserveAspect = true;

            // 特選角色資訊板
            var infoBoxGo = CreateUIObject("FeaturedInfoPlate", centerGo.transform);
            var infoRt = infoBoxGo.GetComponent<RectTransform>();
            infoRt.anchorMin = new Vector2(0.05f, 0.45f);
            infoRt.anchorMax = new Vector2(0.48f, 0.75f);
            infoRt.anchoredPosition = Vector2.zero;

            var rTextGo = CreateUIObject("Rarity", infoBoxGo.transform);
            var rTextRt = rTextGo.GetComponent<RectTransform>();
            rTextRt.anchorMin = new Vector2(0f, 1f);
            rTextRt.anchorMax = new Vector2(1f, 1f);
            rTextRt.pivot = new Vector2(0f, 1f);
            rTextRt.anchoredPosition = new Vector2(0, 0);
            rTextRt.sizeDelta = new Vector2(0, 32);
            _featuredRarityText = rTextGo.AddComponent<Text>();
            _featuredRarityText.font = defaultFont;
            _featuredRarityText.fontSize = 24;
            _featuredRarityText.fontStyle = FontStyle.Bold;
            _featuredRarityText.color = new Color(1f, 0.85f, 0.2f, 1f);
            _featuredRarityText.text = "★★★ SSR ★★★";

            var nameTextGo = CreateUIObject("Name", infoBoxGo.transform);
            var nameRt = nameTextGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0f, 1f);
            nameRt.anchoredPosition = new Vector2(0, -32);
            nameRt.sizeDelta = new Vector2(0, 40);
            _featuredNameText = nameTextGo.AddComponent<Text>();
            _featuredNameText.font = defaultFont;
            _featuredNameText.fontSize = 32;
            _featuredNameText.fontStyle = FontStyle.Bold;
            _featuredNameText.color = Color.white;
            _featuredNameText.text = "克萊兒 // CLAIRE";

            var titleTextGo = CreateUIObject("Title", infoBoxGo.transform);
            var featuredTitleRt = titleTextGo.GetComponent<RectTransform>();
            featuredTitleRt.anchorMin = new Vector2(0f, 1f);
            featuredTitleRt.anchorMax = new Vector2(1f, 1f);
            featuredTitleRt.pivot = new Vector2(0f, 1f);
            featuredTitleRt.anchoredPosition = new Vector2(0, -74);
            featuredTitleRt.sizeDelta = new Vector2(0, 26);
            _featuredTitleText = titleTextGo.AddComponent<Text>();
            _featuredTitleText.font = defaultFont;
            _featuredTitleText.fontSize = 17;
            _featuredTitleText.fontStyle = FontStyle.Bold;
            _featuredTitleText.color = new Color(0.2f, 0.85f, 1f, 1f);
            _featuredTitleText.text = "「神聖女武神・審判之刃」 // 遠程穿甲";

            var skillTextGo = CreateUIObject("Skill", infoBoxGo.transform);
            var skillRt = skillTextGo.GetComponent<RectTransform>();
            skillRt.anchorMin = new Vector2(0f, 1f);
            skillRt.anchorMax = new Vector2(1f, 1f);
            skillRt.pivot = new Vector2(0f, 1f);
            skillRt.anchoredPosition = new Vector2(0, -104);
            skillRt.sizeDelta = new Vector2(0, 30);
            _featuredSkillText = skillTextGo.AddComponent<Text>();
            _featuredSkillText.font = defaultFont;
            _featuredSkillText.fontSize = 15;
            _featuredSkillText.color = new Color(1f, 0.95f, 0.75f, 0.95f);
            _featuredSkillText.text = "★ 專屬大招【女武神戰歌】：全體友軍攻擊力 +50%，持續 8 秒！";

            // 6. 保底天井進度條
            var pityBoxGo = CreateUIObject("PityGaugeBox", centerGo.transform);
            var pityBoxRt = pityBoxGo.GetComponent<RectTransform>();
            pityBoxRt.anchorMin = new Vector2(0.1f, 0.05f);
            pityBoxRt.anchorMax = new Vector2(0.9f, 0.16f);
            pityBoxRt.anchoredPosition = Vector2.zero;

            var pBgGo = CreateUIObject("PityBarBg", pityBoxGo.transform);
            var pBgRt = pBgGo.GetComponent<RectTransform>();
            pBgRt.anchorMin = new Vector2(0f, 0.5f);
            pBgRt.anchorMax = new Vector2(1f, 0.5f);
            pBgRt.sizeDelta = new Vector2(0, 16);
            var pBgImg = pBgGo.AddComponent<Image>();
            pBgImg.color = new Color(0.08f, 0.12f, 0.20f, 0.9f);

            var pFillGo = CreateUIObject("PityBarFill", pBgGo.transform);
            var pFillRt = pFillGo.GetComponent<RectTransform>();
            pFillRt.anchorMin = new Vector2(0f, 0f);
            pFillRt.anchorMax = new Vector2(0.24f, 1f);
            pFillRt.offsetMin = Vector2.zero;
            pFillRt.offsetMax = Vector2.zero;
            _pityProgressBarFill = pFillGo.AddComponent<Image>();
            _pityProgressBarFill.color = new Color(1f, 0.85f, 0.2f, 1f);

            var pTxtGo = CreateUIObject("PityText", pityBoxGo.transform);
            var pTxtRt = pTxtGo.GetComponent<RectTransform>();
            pTxtRt.anchorMin = new Vector2(0f, 1f);
            pTxtRt.anchorMax = new Vector2(1f, 1f);
            pTxtRt.pivot = new Vector2(0.5f, 1f);
            pTxtRt.anchoredPosition = new Vector2(0, 2);
            pTxtRt.sizeDelta = new Vector2(0, 22);
            _pityProgressText = pTxtGo.AddComponent<Text>();
            _pityProgressText.font = defaultFont;
            _pityProgressText.fontSize = 15;
            _pityProgressText.fontStyle = FontStyle.Bold;
            _pityProgressText.alignment = TextAnchor.MiddleCenter;
            _pityProgressText.color = new Color(1f, 0.95f, 0.8f, 1f);
            _pityProgressText.text = "保底進度：0 / 100 抽  (距必得最高階角色還需 100 抽)";

            var pNoticeGo = CreateUIObject("PityNotice", pityBoxGo.transform);
            var pNoticeRt = pNoticeGo.GetComponent<RectTransform>();
            pNoticeRt.anchorMin = new Vector2(0f, 0f);
            pNoticeRt.anchorMax = new Vector2(1f, 0f);
            pNoticeRt.pivot = new Vector2(0.5f, 0f);
            pNoticeRt.anchoredPosition = new Vector2(0, -6);
            pNoticeRt.sizeDelta = new Vector2(0, 18);
            _pityNoticeText = pNoticeGo.AddComponent<Text>();
            _pityNoticeText.font = defaultFont;
            _pityNoticeText.fontSize = 12;
            _pityNoticeText.alignment = TextAnchor.MiddleCenter;
            _pityNoticeText.color = new Color(0.2f, 0.85f, 1f, 0.75f);
            _pityNoticeText.text = "※ 每次十連招募必定獲得 SR 或以上契約角色！若抽中最高階角色保底計數重置。";

            // 7. 底部招募按鈕區 (BottomRecruitPanel)
            var recruitGo = CreateUIObject("BottomRecruitPanel", transform);
            var recruitRt = recruitGo.GetComponent<RectTransform>();
            recruitRt.anchorMin = new Vector2(0.5f, 0f);
            recruitRt.anchorMax = new Vector2(0.5f, 0f);
            recruitRt.pivot = new Vector2(0.5f, 0f);
            recruitRt.anchoredPosition = new Vector2(60, 25);
            recruitRt.sizeDelta = new Vector2(800, 110);

            // 略過動畫勾選框
            var skipGo = CreateUIObject("ToggleSkipAnim", recruitGo.transform);
            var skipRt = skipGo.GetComponent<RectTransform>();
            skipRt.anchorMin = new Vector2(0f, 0.5f);
            skipRt.anchorMax = new Vector2(0f, 0.5f);
            skipRt.pivot = new Vector2(0f, 0.5f);
            skipRt.anchoredPosition = new Vector2(-15, 0);
            skipRt.sizeDelta = new Vector2(170, 32);
            _skipAnimToggle = skipGo.AddComponent<Toggle>();

            var skipBgGo = CreateUIObject("Bg", skipGo.transform);
            var skipBgRt = skipBgGo.GetComponent<RectTransform>();
            skipBgRt.anchorMin = new Vector2(0f, 0.5f);
            skipBgRt.anchorMax = new Vector2(0f, 0.5f);
            skipBgRt.pivot = new Vector2(0f, 0.5f);
            skipBgRt.sizeDelta = new Vector2(24, 24);
            var skipBgImg = skipBgGo.AddComponent<Image>();
            skipBgImg.sprite = CreateCircleSprite(24, new Color(0.1f, 0.15f, 0.25f, 1f), new Color(0f, 0.85f, 1f, 0.8f), 2);
            _skipAnimToggle.targetGraphic = skipBgImg;

            var checkGo = CreateUIObject("Check", skipBgGo.transform);
            StretchFull(checkGo.GetComponent<RectTransform>(), 4);
            var chkImg = checkGo.AddComponent<Image>();
            chkImg.color = new Color(0f, 0.9f, 1f, 1f);
            _skipAnimToggle.graphic = chkImg;
            _skipAnimToggle.isOn = false;

            var skipTxtGo = CreateUIObject("Label", skipGo.transform);
            var skipTxtRt = skipTxtGo.GetComponent<RectTransform>();
            skipTxtRt.anchorMin = new Vector2(0f, 0.5f);
            skipTxtRt.anchorMax = new Vector2(1f, 0.5f);
            skipTxtRt.pivot = new Vector2(0f, 0.5f);
            skipTxtRt.anchoredPosition = new Vector2(32, 0);
            skipTxtRt.sizeDelta = new Vector2(140, 24);
            var sTxt = skipTxtGo.AddComponent<Text>();
            sTxt.font = defaultFont;
            sTxt.fontSize = 14;
            sTxt.color = new Color(0.85f, 0.95f, 1f, 0.9f);
            sTxt.text = "快速揭曉 (跳過動畫)";

            // 單抽按鈕
            var singleBtnGo = CreateUIObject("BtnPullSingle", recruitGo.transform);
            var singleRt = singleBtnGo.GetComponent<RectTransform>();
            singleRt.anchorMin = new Vector2(0.5f, 0.5f);
            singleRt.anchorMax = new Vector2(0.5f, 0.5f);
            singleRt.pivot = new Vector2(0.5f, 0.5f);
            singleRt.anchoredPosition = new Vector2(-110, 0);
            singleRt.sizeDelta = new Vector2(240, 74);

            var sImg = singleBtnGo.AddComponent<Image>();
            sImg.sprite = CreateGradientSprite(240, 74, new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.06f, 0.09f, 0.16f, 0.95f), new Color(0.2f, 0.85f, 1f, 0.7f), 2, 8);
            _btnPullSingle = singleBtnGo.AddComponent<Button>();
            _btnPullSingle.onClick.AddListener(OnClickPullSingle);

            var sTxt1Go = CreateUIObject("MainTxt", singleBtnGo.transform);
            var sTxt1Rt = sTxt1Go.GetComponent<RectTransform>();
            sTxt1Rt.anchorMin = new Vector2(0f, 0.5f);
            sTxt1Rt.anchorMax = new Vector2(1f, 1f);
            sTxt1Rt.anchoredPosition = new Vector2(0, -2);
            var st1 = sTxt1Go.AddComponent<Text>();
            st1.font = defaultFont;
            st1.fontSize = 20;
            st1.fontStyle = FontStyle.Bold;
            st1.alignment = TextAnchor.MiddleCenter;
            st1.color = Color.white;
            st1.text = "單次契約招募";

            var sTxt2Go = CreateUIObject("CostTxt", singleBtnGo.transform);
            var sTxt2Rt = sTxt2Go.GetComponent<RectTransform>();
            sTxt2Rt.anchorMin = new Vector2(0f, 0f);
            sTxt2Rt.anchorMax = new Vector2(1f, 0.5f);
            sTxt2Rt.anchoredPosition = new Vector2(0, 2);
            _pullSingleCostText = sTxt2Go.AddComponent<Text>();
            _pullSingleCostText.font = defaultFont;
            _pullSingleCostText.fontSize = 15;
            _pullSingleCostText.fontStyle = FontStyle.Bold;
            _pullSingleCostText.alignment = TextAnchor.MiddleCenter;
            _pullSingleCostText.color = new Color(0.2f, 0.9f, 1f, 1f);
            _pullSingleCostText.text = "✦ 150 寶石";

            // 十連抽按鈕 (烈焰黃金醒目高能)
            var tenBtnGo = CreateUIObject("BtnPullTen", recruitGo.transform);
            var tenRt = tenBtnGo.GetComponent<RectTransform>();
            tenRt.anchorMin = new Vector2(0.5f, 0.5f);
            tenRt.anchorMax = new Vector2(0.5f, 0.5f);
            tenRt.pivot = new Vector2(0.5f, 0.5f);
            tenRt.anchoredPosition = new Vector2(170, 0);
            tenRt.sizeDelta = new Vector2(270, 74);

            var tImg = tenBtnGo.AddComponent<Image>();
            tImg.sprite = CreateGradientSprite(270, 74, new Color(0.85f, 0.45f, 0.08f, 0.98f), new Color(0.6f, 0.25f, 0.05f, 0.98f), new Color(1f, 0.9f, 0.4f, 1f), 3, 8);
            _btnPullTen = tenBtnGo.AddComponent<Button>();
            _btnPullTen.onClick.AddListener(OnClickPullTen);

            var tTxt1Go = CreateUIObject("MainTxt", tenBtnGo.transform);
            var tTxt1Rt = tTxt1Go.GetComponent<RectTransform>();
            tTxt1Rt.anchorMin = new Vector2(0f, 0.5f);
            tTxt1Rt.anchorMax = new Vector2(1f, 1f);
            tTxt1Rt.anchoredPosition = new Vector2(0, -2);
            var tt1 = tTxt1Go.AddComponent<Text>();
            tt1.font = defaultFont;
            tt1.fontSize = 22;
            tt1.fontStyle = FontStyle.Bold;
            tt1.alignment = TextAnchor.MiddleCenter;
            tt1.color = new Color(1f, 0.98f, 0.9f, 1f);
            tt1.text = "十連特異契約";

            var tTxt2Go = CreateUIObject("CostTxt", tenBtnGo.transform);
            var tTxt2Rt = tTxt2Go.GetComponent<RectTransform>();
            tTxt2Rt.anchorMin = new Vector2(0f, 0f);
            tTxt2Rt.anchorMax = new Vector2(1f, 0.5f);
            tTxt2Rt.anchoredPosition = new Vector2(0, 2);
            _pullTenCostText = tTxt2Go.AddComponent<Text>();
            _pullTenCostText.font = defaultFont;
            _pullTenCostText.fontSize = 15;
            _pullTenCostText.fontStyle = FontStyle.Bold;
            _pullTenCostText.alignment = TextAnchor.MiddleCenter;
            _pullTenCostText.color = new Color(1f, 0.95f, 0.4f, 1f);
            _pullTenCostText.text = "✦ 1,500 寶石 (必中 SR+)";

            // 8. 召喚矩陣光效演出層 (SummonAnimLayer)
            var animGo = CreateUIObject("SummonAnimLayer", transform);
            StretchFull(animGo.GetComponent<RectTransform>());
            var animBg = animGo.AddComponent<Image>();
            animBg.color = new Color(0.02f, 0.04f, 0.08f, 0.96f);
            _summonAnimLayer = animGo;

            var matrixGo = CreateUIObject("Matrix", animGo.transform);
            var matRt = matrixGo.GetComponent<RectTransform>();
            matRt.anchorMin = new Vector2(0.5f, 0.5f);
            matRt.anchorMax = new Vector2(0.5f, 0.5f);
            matRt.sizeDelta = new Vector2(500, 500);
            _summonMatrixImage = matrixGo.AddComponent<Image>();
            _summonMatrixImage.sprite = CreateCircleSprite(500, new Color(0f, 0.7f, 1f, 0.1f), new Color(0.2f, 0.85f, 1f, 0.9f), 4);

            var flareGo = CreateUIObject("Flare", animGo.transform);
            var flareRt = flareGo.GetComponent<RectTransform>();
            flareRt.anchorMin = new Vector2(0.5f, 0.5f);
            flareRt.anchorMax = new Vector2(0.5f, 0.5f);
            flareRt.sizeDelta = new Vector2(650, 650);
            _summonFlareImage = flareGo.AddComponent<Image>();
            _summonFlareImage.sprite = CreateCircleSprite(650, new Color(1f, 0.85f, 0.2f, 0.2f), new Color(1f, 0.9f, 0.5f, 0.8f), 6);

            var aTxtGo = CreateUIObject("StatusText", animGo.transform);
            var aTxtRt = aTxtGo.GetComponent<RectTransform>();
            aTxtRt.anchorMin = new Vector2(0.5f, 0.18f);
            aTxtRt.anchorMax = new Vector2(0.5f, 0.18f);
            aTxtRt.sizeDelta = new Vector2(600, 40);
            _summonStatusText = aTxtGo.AddComponent<Text>();
            _summonStatusText.font = defaultFont;
            _summonStatusText.fontSize = 22;
            _summonStatusText.fontStyle = FontStyle.Bold;
            _summonStatusText.alignment = TextAnchor.MiddleCenter;
            _summonStatusText.color = Color.white;
            _summonStatusText.text = "⚡ 矩陣共鳴充能中... 準備開啟特異契約門扉！";

            var skipBtnGo = CreateTacticalButton("BtnSkipAnim", animGo.transform, "⏩ 略過演出", new Vector2(-40, -40), new Vector2(130, 42), new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.2f, 0.8f, 1f, 0.8f), defaultFont);
            var skRt = skipBtnGo.GetComponent<RectTransform>();
            skRt.anchorMin = new Vector2(1f, 1f);
            skRt.anchorMax = new Vector2(1f, 1f);
            skRt.pivot = new Vector2(1f, 1f);
            _btnSkipAnim = skipBtnGo.GetComponent<Button>();
            _btnSkipAnim.onClick.AddListener(SkipSummonAnimation);

            _summonAnimLayer.SetActive(false);

            // 9. 單抽登場揭曉彈窗 (SingleRevealModal)
            var singleRevGo = CreateUIObject("SingleRevealModal", transform);
            StretchFull(singleRevGo.GetComponent<RectTransform>());
            var srBg = singleRevGo.AddComponent<Image>();
            srBg.color = new Color(0.03f, 0.05f, 0.10f, 0.98f);
            _singleRevealModal = singleRevGo;

            _revealClickAnywhereButton = singleRevGo.AddComponent<Button>();
            _revealClickAnywhereButton.onClick.AddListener(() => _singleRevealModal.SetActive(false));

            var sPortGo = CreateUIObject("Portrait", singleRevGo.transform);
            var spRt = sPortGo.GetComponent<RectTransform>();
            spRt.anchorMin = new Vector2(0.5f, 0.52f);
            spRt.anchorMax = new Vector2(0.5f, 0.52f);
            spRt.sizeDelta = new Vector2(600, 750);
            _revealPortraitImage = sPortGo.AddComponent<Image>();
            _revealPortraitImage.preserveAspect = true;

            var rBadgeBoxGo = CreateUIObject("RarityBadge", singleRevGo.transform);
            var rBadgeBoxRt = rBadgeBoxGo.GetComponent<RectTransform>();
            rBadgeBoxRt.anchorMin = new Vector2(0.5f, 0.88f);
            rBadgeBoxRt.anchorMax = new Vector2(0.5f, 0.88f);
            rBadgeBoxRt.sizeDelta = new Vector2(400, 50);
            _revealRarityBadge = rBadgeBoxGo.AddComponent<Text>();
            _revealRarityBadge.font = defaultFont;
            _revealRarityBadge.fontSize = 36;
            _revealRarityBadge.fontStyle = FontStyle.Bold;
            _revealRarityBadge.alignment = TextAnchor.MiddleCenter;
            _revealRarityBadge.color = new Color(1f, 0.85f, 0.2f, 1f);
            _revealRarityBadge.text = "★★★ SSR ★★★";

            var rNameBoxGo = CreateUIObject("CharacterName", singleRevGo.transform);
            var rNameBoxRt = rNameBoxGo.GetComponent<RectTransform>();
            rNameBoxRt.anchorMin = new Vector2(0.5f, 0.82f);
            rNameBoxRt.anchorMax = new Vector2(0.5f, 0.82f);
            rNameBoxRt.sizeDelta = new Vector2(600, 42);
            _revealCharacterName = rNameBoxGo.AddComponent<Text>();
            _revealCharacterName.font = defaultFont;
            _revealCharacterName.fontSize = 32;
            _revealCharacterName.fontStyle = FontStyle.Bold;
            _revealCharacterName.alignment = TextAnchor.MiddleCenter;
            _revealCharacterName.color = Color.white;
            _revealCharacterName.text = "克萊兒 // CHAR01_CLAIRE";

            var rRoleBoxGo = CreateUIObject("RoleTag", singleRevGo.transform);
            var rRoleBoxRt = rRoleBoxGo.GetComponent<RectTransform>();
            rRoleBoxRt.anchorMin = new Vector2(0.5f, 0.77f);
            rRoleBoxRt.anchorMax = new Vector2(0.5f, 0.77f);
            rRoleBoxRt.sizeDelta = new Vector2(600, 30);
            _revealRoleTag = rRoleBoxGo.AddComponent<Text>();
            _revealRoleTag.font = defaultFont;
            _revealRoleTag.fontSize = 18;
            _revealRoleTag.alignment = TextAnchor.MiddleCenter;
            _revealRoleTag.color = new Color(0.2f, 0.85f, 1f, 1f);
            _revealRoleTag.text = "ROLE: 遠程穿甲 // VALKYRIE LEADER";

            var rQuoteBoxGo = CreateUIObject("QuoteBox", singleRevGo.transform);
            var rQuoteBoxRt = rQuoteBoxGo.GetComponent<RectTransform>();
            rQuoteBoxRt.anchorMin = new Vector2(0.5f, 0.16f);
            rQuoteBoxRt.anchorMax = new Vector2(0.5f, 0.16f);
            rQuoteBoxRt.sizeDelta = new Vector2(850, 70);
            var qBg = rQuoteBoxGo.AddComponent<Image>();
            qBg.sprite = CreateGradientSprite(850, 70, new Color(0.06f, 0.09f, 0.16f, 0.95f), new Color(0.03f, 0.05f, 0.10f, 0.95f), new Color(0.2f, 0.85f, 1f, 0.6f), 2, 8);

            var qTxtGo = CreateUIObject("QuoteText", rQuoteBoxGo.transform);
            StretchFull(qTxtGo.GetComponent<RectTransform>(), 10);
            _revealQuoteText = qTxtGo.AddComponent<Text>();
            _revealQuoteText.font = defaultFont;
            _revealQuoteText.fontSize = 18;
            _revealQuoteText.fontStyle = FontStyle.Bold;
            _revealQuoteText.alignment = TextAnchor.MiddleCenter;
            _revealQuoteText.color = new Color(1f, 0.95f, 0.8f, 1f);
            _revealQuoteText.text = "「我是克萊兒，從今刻起，將我的榮耀與利劍交付給您，指揮官。」";

            // NEW 標籤
            var newBadgeGo = CreateUIObject("NewBadge", singleRevGo.transform);
            var nbRt = newBadgeGo.GetComponent<RectTransform>();
            nbRt.anchorMin = new Vector2(0.78f, 0.78f);
            nbRt.anchorMax = new Vector2(0.78f, 0.78f);
            nbRt.sizeDelta = new Vector2(110, 42);
            var nbImg = newBadgeGo.AddComponent<Image>();
            nbImg.sprite = CreatePillSprite(110, 42, new Color(1f, 0.25f, 0.25f, 0.95f), new Color(1f, 0.7f, 0.3f, 1f));
            var nbTxtGo = CreateUIObject("Txt", newBadgeGo.transform);
            StretchFull(nbTxtGo.GetComponent<RectTransform>());
            var nbTxt = nbTxtGo.AddComponent<Text>();
            nbTxt.font = defaultFont;
            nbTxt.fontSize = 18;
            nbTxt.fontStyle = FontStyle.Bold;
            nbTxt.alignment = TextAnchor.MiddleCenter;
            nbTxt.color = Color.white;
            nbTxt.text = "NEW!";
            _revealNewBadge = newBadgeGo;

            // 重複標籤
            var dupBadgeGo = CreateUIObject("DuplicateBadge", singleRevGo.transform);
            var dbRt = dupBadgeGo.GetComponent<RectTransform>();
            dbRt.anchorMin = new Vector2(0.78f, 0.78f);
            dbRt.anchorMax = new Vector2(0.78f, 0.78f);
            dbRt.sizeDelta = new Vector2(180, 42);
            var dbImg = dupBadgeGo.AddComponent<Image>();
            dbImg.sprite = CreatePillSprite(180, 42, new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.2f, 0.85f, 1f, 0.9f));
            var dbTxtGo = CreateUIObject("Txt", dupBadgeGo.transform);
            StretchFull(dbTxtGo.GetComponent<RectTransform>());
            var dbTxt = dbTxtGo.AddComponent<Text>();
            dbTxt.font = defaultFont;
            dbTxt.fontSize = 15;
            dbTxt.fontStyle = FontStyle.Bold;
            dbTxt.alignment = TextAnchor.MiddleCenter;
            dbTxt.color = new Color(0.2f, 0.9f, 1f, 1f);
            dbTxt.text = "◆ 進階卡片 +1";
            _revealDuplicateBadge = dupBadgeGo;

            var tipGo = CreateUIObject("ContinueTip", singleRevGo.transform);
            var tipRt = tipGo.GetComponent<RectTransform>();
            tipRt.anchorMin = new Vector2(0.5f, 0.05f);
            tipRt.anchorMax = new Vector2(0.5f, 0.05f);
            tipRt.sizeDelta = new Vector2(400, 30);
            var tipTxt = tipGo.AddComponent<Text>();
            tipTxt.font = defaultFont;
            tipTxt.fontSize = 16;
            tipTxt.alignment = TextAnchor.MiddleCenter;
            tipTxt.color = new Color(0.8f, 0.9f, 1f, 0.6f);
            tipTxt.text = "[ 點擊畫面任意處繼續 // TAP ANYWHERE TO CONTINUE ]";

            _singleRevealModal.SetActive(false);

            // 10. 十連抽戰術卡牌結算彈窗 (TenResultModal)
            var tenResGo = CreateUIObject("TenResultModal", transform);
            StretchFull(tenResGo.GetComponent<RectTransform>());
            var trBg = tenResGo.AddComponent<Image>();
            trBg.color = new Color(0.04f, 0.06f, 0.12f, 0.98f);
            _tenResultModal = tenResGo;

            var trTitleGo = CreateUIObject("ResultTitle", tenResGo.transform);
            var trTitleRt = trTitleGo.GetComponent<RectTransform>();
            trTitleRt.anchorMin = new Vector2(0.5f, 0.92f);
            trTitleRt.anchorMax = new Vector2(0.5f, 0.92f);
            trTitleRt.sizeDelta = new Vector2(600, 40);
            var trTitleTxt = trTitleGo.AddComponent<Text>();
            trTitleTxt.font = defaultFont;
            trTitleTxt.fontSize = 28;
            trTitleTxt.fontStyle = FontStyle.Bold;
            trTitleTxt.alignment = TextAnchor.MiddleCenter;
            trTitleTxt.color = new Color(1f, 0.92f, 0.7f, 1f);
            trTitleTxt.text = "✦ 特異招募結算報告 // RECRUIT REPORT ✦";

            // 卡牌放置網格根節點 (Grid)
            var gridGo = CreateUIObject("GridRoot", tenResGo.transform);
            var gridRt = gridGo.GetComponent<RectTransform>();
            gridRt.anchorMin = new Vector2(0.5f, 0.52f);
            gridRt.anchorMax = new Vector2(0.5f, 0.52f);
            gridRt.sizeDelta = new Vector2(920, 500);

            var glg = gridGo.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(165, 230);
            glg.spacing = new Vector2(18, 18);
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.MiddleCenter;
            glg.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            glg.constraintCount = 2;
            _resultGridRoot = gridGo.transform;

            // 底部按鈕 (再抽一次與確認)
            var rePullBtnGo = CreateTacticalButton("BtnResultRePull", tenResGo.transform, "✦ 再抽十連 (✦ 1500)", new Vector2(-150, 45), new Vector2(250, 58), new Color(0.85f, 0.45f, 0.08f, 0.98f), new Color(1f, 0.9f, 0.4f, 1f), defaultFont);
            var reRt = rePullBtnGo.GetComponent<RectTransform>();
            reRt.anchorMin = new Vector2(0.5f, 0f);
            reRt.anchorMax = new Vector2(0.5f, 0f);
            _btnResultRePull = rePullBtnGo.GetComponent<Button>();
            _btnResultRePull.onClick.AddListener(() =>
            {
                _tenResultModal.SetActive(false);
                OnClickPullTen();
            });

            var confBtnGo = CreateTacticalButton("BtnResultConfirm", tenResGo.transform, "✔ 確認完成 (CONFIRM)", new Vector2(150, 45), new Vector2(240, 58), new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.2f, 0.85f, 1f, 0.8f), defaultFont);
            var cfRt = confBtnGo.GetComponent<RectTransform>();
            cfRt.anchorMin = new Vector2(0.5f, 0f);
            cfRt.anchorMax = new Vector2(0.5f, 0f);
            _btnResultConfirm = confBtnGo.GetComponent<Button>();
            _btnResultConfirm.onClick.AddListener(() => _tenResultModal.SetActive(false));

            _tenResultModal.SetActive(false);

            // 11. 機率詳情彈窗 (RateModal)
            _rateModal = CreateDialogModal("RateDetailsModal", transform, "【卡池機率公開與保底說明 // GACHA RATES】", out _rateDetailsText, out _btnRateModalClose, defaultFont);
            _btnRateModalClose.onClick.AddListener(() => _rateModal.SetActive(false));
            _rateModal.SetActive(false);

            // 12. 招募歷史彈窗 (HistoryModal)
            _historyModal = CreateDialogModal("HistoryModal", transform, "【特異契約招募歷史紀錄 // PULL HISTORY】", out _historyListText, out _btnHistoryModalClose, defaultFont);
            _btnHistoryModalClose.onClick.AddListener(() => _historyModal.SetActive(false));
            _historyModal.SetActive(false);

            // 13. Toast 提示
            var toastGo = CreateUIObject("ToastBanner", transform);
            var toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(0.5f, 0.22f);
            toastRt.anchorMax = new Vector2(0.5f, 0.22f);
            toastRt.sizeDelta = new Vector2(650, 54);
            var toastImg = toastGo.AddComponent<Image>();
            toastImg.sprite = CreatePillSprite(650, 54, new Color(0.06f, 0.09f, 0.15f, 0.96f), new Color(0f, 0.85f, 1f, 0.8f));
            _toastCanvasGroup = toastGo.AddComponent<CanvasGroup>();
            _toastRoot = toastGo;

            var toastTxtGo = CreateUIObject("ToastText", toastGo.transform);
            StretchFull(toastTxtGo.GetComponent<RectTransform>(), 8);
            _toastText = toastTxtGo.AddComponent<Text>();
            _toastText.font = defaultFont;
            _toastText.fontSize = 18;
            _toastText.alignment = TextAnchor.MiddleCenter;
            _toastText.color = new Color(1f, 0.95f, 0.75f, 1f);
            _toastText.text = "提示訊息";
            _toastRoot.SetActive(false);
        }

        #endregion

        private static Font GetDefaultFont()
        {
            return ProceduralUiKit.GetDefaultFont();
        }

        #region 程序化紋理與元件生成器

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            return ProceduralUiKit.CreateUiObject(name, parent);
        }

        private static void StretchFull(RectTransform rt, float margin = 0f)
        {
            ProceduralUiKit.StretchFull(rt, margin);
        }

        private static Text CreateResourcePill(Transform parent, string name, Vector2 pos, string initVal, string iconName, Color textColor, Color borderColor, Font font)
        {
            var pillGo = CreateUIObject(name, parent);
            var rt = pillGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(160, 40);

            var bgGo = CreateUIObject("Bg", pillGo.transform);
            StretchFull(bgGo.GetComponent<RectTransform>());
            var img = bgGo.AddComponent<Image>();
            img.sprite = CreatePillSprite(160, 40, new Color(0.02f, 0.05f, 0.10f, 0.12f), borderColor);

            // 左側專屬彩色圖示 Icon
            Sprite iconSp = LobbyView.TryLoadIconSprite(iconName);
            var iconGo = CreateUIObject("Icon", pillGo.transform);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(8, 0);
            iconRt.sizeDelta = new Vector2(26, 26);
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

            var txt = pillGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 20;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = textColor;
            txt.text = initVal;
            var shadow = pillGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);

            return txt;
        }

        private static GameObject CreateTacticalHeaderButton(string name, Transform parent, string label, Vector2 pos, Color bgColor, Color borderColor, Font font)
        {
            var btnGo = CreateUIObject(name, parent);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(120, 40);

            var img = btnGo.AddComponent<Image>();
            img.sprite = CreatePillSprite(120, 40, bgColor, borderColor);
            btnGo.AddComponent<Button>();

            var txtGo = CreateUIObject("Text", btnGo.transform);
            StretchFull(txtGo.GetComponent<RectTransform>());
            var txt = txtGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 15;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;

            return btnGo;
        }

        private static GameObject CreateTacticalButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color bgColor, Color borderColor, Font font)
        {
            var btnGo = CreateUIObject(name, parent);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = btnGo.AddComponent<Image>();
            img.sprite = CreateGradientSprite((int)size.x, (int)size.y, bgColor, new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f, bgColor.a), borderColor, 2, 6);
            btnGo.AddComponent<Button>();

            var txtGo = CreateUIObject("Text", btnGo.transform);
            StretchFull(txtGo.GetComponent<RectTransform>());
            var txt = txtGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 17;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;

            return btnGo;
        }

        private static Button CreateBannerTabButton(Transform parent, string name, string title, string sub, Vector2 pos, Font font, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = CreateUIObject(name, parent);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(240, 68);

            var img = btnGo.AddComponent<Image>();
            img.sprite = CreateGradientSprite(240, 68, new Color(0.12f, 0.18f, 0.28f, 0.95f), new Color(0.06f, 0.09f, 0.16f, 0.95f), new Color(0.2f, 0.85f, 1f, 0.6f), 2, 6);

            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var tGo = CreateUIObject("Title", btnGo.transform);
            var tRt = tGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0.5f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.anchoredPosition = new Vector2(15, -2);
            var tTxt = tGo.AddComponent<Text>();
            tTxt.font = font;
            tTxt.fontSize = 17;
            tTxt.fontStyle = FontStyle.Bold;
            tTxt.color = Color.white;
            tTxt.text = title;

            var sGo = CreateUIObject("Sub", btnGo.transform);
            var sRt = sGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 0.5f);
            sRt.anchoredPosition = new Vector2(15, 2);
            var sTxt = sGo.AddComponent<Text>();
            sTxt.font = font;
            sTxt.fontSize = 12;
            sTxt.color = new Color(0.2f, 0.85f, 1f, 0.85f);
            sTxt.text = sub;

            return btn;
        }

        private static GameObject CreateDialogModal(string name, Transform parent, string title, out Text contentText, out Button closeButton, Font font)
        {
            var modalGo = CreateUIObject(name, parent);
            StretchFull(modalGo.GetComponent<RectTransform>());

            var backdrop = modalGo.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.04f, 0.08f, 0.88f);

            var winGo = CreateUIObject("Window", modalGo.transform);
            var winRt = winGo.GetComponent<RectTransform>();
            winRt.anchorMin = new Vector2(0.5f, 0.5f);
            winRt.anchorMax = new Vector2(0.5f, 0.5f);
            winRt.sizeDelta = new Vector2(800, 560);
            var winImg = winGo.AddComponent<Image>();
            winImg.sprite = CreateGradientSprite(800, 560, new Color(0.08f, 0.12f, 0.20f, 0.98f), new Color(0.04f, 0.06f, 0.12f, 0.98f), new Color(0.2f, 0.85f, 1f, 0.8f), 2, 8);

            var tGo = CreateUIObject("Title", winGo.transform);
            var tRt = tGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0, -12);
            tRt.sizeDelta = new Vector2(-40, 36);
            var tTxt = tGo.AddComponent<Text>();
            tTxt.font = font;
            tTxt.fontSize = 20;
            tTxt.fontStyle = FontStyle.Bold;
            tTxt.color = new Color(1f, 0.92f, 0.75f, 1f);
            tTxt.text = title;

            var cGo = CreateUIObject("Content", winGo.transform);
            var cRt = cGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.offsetMin = new Vector2(30, 80);
            cRt.offsetMax = new Vector2(-30, -60);
            contentText = cGo.AddComponent<Text>();
            contentText.font = font;
            contentText.fontSize = 16;
            contentText.lineSpacing = 1.25f;
            contentText.color = new Color(0.9f, 0.95f, 1f, 0.95f);

            var btnCloseGo = CreateTacticalButton("BtnClose", winGo.transform, "✕ 關閉", new Vector2(0, 30), new Vector2(160, 44), new Color(0.18f, 0.12f, 0.15f, 0.95f), new Color(1f, 0.4f, 0.5f, 0.8f), font);
            var bRt = btnCloseGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.5f, 0f);
            bRt.anchorMax = new Vector2(0.5f, 0f);
            closeButton = btnCloseGo.GetComponent<Button>();

            return modalGo;
        }

        private static Sprite CreateGradientSprite(int w, int h, Color top, Color bottom, Color? border = null, int borderWidth = 0, int radius = 0)
        {
            return ProceduralUiKit.GetGradientSprite(w, h, top, bottom, border, borderWidth, radius);
        }

        private static Sprite CreatePillSprite(int w, int h, Color bg, Color border)
        {
            return ProceduralUiKit.GetPillSprite(w, h, bg, border);
        }

        private static Sprite CreateCircleSprite(int size, Color fill, Color border, int borderWidth)
        {
            return ProceduralUiKit.GetCircleSprite(size, fill, border, borderWidth);
        }

        #endregion
    }
}
