#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
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
    /// 戰役榮譽與成就畫面 (OPT-7 / ARCH-04)。
    /// 實作 IAchievementView，列出所有成就進度、寶石/道具獎勵，支援一鍵領取與紅點更新。
    /// </summary>
    public sealed class AchievementView : UnityScreenBase, IAchievementView
    {
        private GameCore _core;
        private AchievementPresenter _presenter;

        private RectTransform _listContainer;
        private Button _claimAllBtn;
        private Dictionary<string, Button> _claimButtons = new Dictionary<string, Button>();

        public IReadOnlyList<ItemStack> LastClaimedRewards { get; private set; }
        public string LastError { get; private set; }
        public AchievementPresenter Presenter => _presenter;
        public Button ClaimAllButton => _claimAllBtn;

        public override string ScreenId => "AchievementUI";

        public void Initialize(GameCore core, AchievementPresenter presenter = null)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _presenter = presenter ?? new AchievementPresenter(_core);
            _presenter.AttachView(this);

            EnsureUiHierarchy();
            _presenter.Initialize();
        }

        public void InitializeForTesting(GameCore core, AchievementPresenter presenter = null)
        {
            Initialize(core, presenter);
        }

        private void Awake()
        {
            EnsureUiHierarchy();
        }

        private void EnsureUiHierarchy()
        {
            if (_listContainer != null) return;

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

            // 背景
            GameObject bgGo = ProceduralUiKit.CreateUiObject("Background", transform);
            RectTransform bgRt = bgGo.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(bgRt);
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.06f, 0.08f, 0.12f, 1f);

            // 頂部導航
            BuildTopBar();

            // 成就列表容器
            GameObject listGo = ProceduralUiKit.CreateUiObject("ListContainer", transform);
            _listContainer = listGo.GetComponent<RectTransform>();
            _listContainer.anchorMin = new Vector2(0.06f, 0.06f);
            _listContainer.anchorMax = new Vector2(0.94f, 0.86f);
            _listContainer.offsetMin = Vector2.zero;
            _listContainer.offsetMax = Vector2.zero;
        }

        private void BuildTopBar()
        {
            GameObject topGo = ProceduralUiKit.CreateUiObject("TopBar", transform);
            RectTransform tRt = topGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0.04f, 0.88f);
            tRt.anchorMax = new Vector2(0.96f, 0.98f);
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            // 返回按鈕
            GameObject backBtnGo = ProceduralUiKit.CreateUiObject("BackBtn", tRt);
            RectTransform bRt = backBtnGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0f, 0.15f);
            bRt.anchorMax = new Vector2(0.12f, 0.85f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            Image bImg = backBtnGo.AddComponent<Image>();
            bImg.color = new Color(0.18f, 0.24f, 0.35f, 0.95f);
            Button backBtn = backBtnGo.AddComponent<Button>();
            backBtn.onClick.AddListener(() => GameServices.Navigator?.Pop());
            CreateLabel(bRt, "Lbl", "◀ 返回大廳", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            // 標題
            CreateLabel(tRt, "Title", "ACHIEVEMENTS // 戰役榮譽與成就", new Vector2(0.15f, 0f), new Vector2(0.75f, 1f), 20, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleLeft);

            // M2-5: 一鍵全部領取按鈕
            GameObject claimAllGo = ProceduralUiKit.CreateUiObject("ClaimAllBtn", tRt);
            RectTransform caRt = claimAllGo.GetComponent<RectTransform>();
            caRt.anchorMin = new Vector2(0.78f, 0.15f);
            caRt.anchorMax = new Vector2(0.98f, 0.85f);
            caRt.offsetMin = Vector2.zero;
            caRt.offsetMax = Vector2.zero;
            Image caImg = claimAllGo.AddComponent<Image>();
            caImg.color = new Color(0.85f, 0.52f, 0.15f, 1f);
            _claimAllBtn = claimAllGo.AddComponent<Button>();
            _claimAllBtn.onClick.AddListener(() => ClaimAll());
            CreateLabel(caRt, "Lbl", "★ 全部領取 (CLAIM ALL)", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);
        }

        public void Claim(string achId)
        {
            if (_presenter != null && !string.IsNullOrEmpty(achId))
            {
                _presenter.ClaimReward(achId);
            }
        }

        public int ClaimAll()
        {
            if (_presenter == null) return 0;
            return _presenter.ClaimAll();
        }

        // ---- IAchievementView 介面實作 ----

        public void DisplayAchievements(IReadOnlyList<AchievementDef> all, Dictionary<string, AchievementProgress> progress)
        {
            ClearChildren(_listContainer);
            _claimButtons.Clear();

            if (all == null || all.Count == 0)
            {
                CreateLabel(_listContainer, "Empty", "目前尚無成就資料", new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f), 16, Color.gray, TextAnchor.MiddleCenter);
                return;
            }

            float rowH = 0.18f;
            for (int i = 0; i < all.Count; i++)
            {
                var ach = all[i];
                string achId = ach.Id;
                AchievementProgress p = null;
                if (progress != null) progress.TryGetValue(achId, out p);

                bool isUnlocked = p != null && p.UnlockedAtUtc != null;
                bool isClaimed = p != null && p.Claimed;

                float yMax = 1.0f - i * (rowH + 0.02f);
                float yMin = yMax - rowH;
                if (yMin < 0f) break;

                GameObject rowGo = ProceduralUiKit.CreateUiObject("AchRow_" + achId, _listContainer);
                RectTransform rRt = rowGo.GetComponent<RectTransform>();
                rRt.anchorMin = new Vector2(0.02f, yMin);
                rRt.anchorMax = new Vector2(0.98f, yMax);
                rRt.offsetMin = Vector2.zero;
                rRt.offsetMax = Vector2.zero;

                Image rImg = rowGo.AddComponent<Image>();
                rImg.color = isClaimed ? new Color(0.08f, 0.10f, 0.14f, 0.8f) : (isUnlocked ? new Color(0.14f, 0.22f, 0.32f, 0.95f) : new Color(0.10f, 0.12f, 0.16f, 0.9f));

                // 標題與說明
                string title = ach.Name ?? ach.Id;
                CreateLabel(rRt, "Title", title, new Vector2(0.03f, 0.55f), new Vector2(0.65f, 0.92f), 15, isUnlocked ? new Color(1f, 0.85f, 0.35f) : Color.white, TextAnchor.MiddleLeft);

                string rewardDesc = "";
                if (ach.Reward != null && ach.Reward.Count > 0)
                {
                    rewardDesc = $"獎勵: {ach.Reward[0].Item} x{ach.Reward[0].N}";
                }
                CreateLabel(rRt, "Reward", rewardDesc, new Vector2(0.03f, 0.15f), new Vector2(0.65f, 0.50f), 13, new Color(0.4f, 0.9f, 0.5f), TextAnchor.MiddleLeft);

                // 領取按鈕
                GameObject btnGo = ProceduralUiKit.CreateUiObject("ClaimBtn_" + achId, rRt);
                RectTransform bRt = btnGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0.75f, 0.20f);
                bRt.anchorMax = new Vector2(0.96f, 0.80f);
                bRt.offsetMin = Vector2.zero;
                bRt.offsetMax = Vector2.zero;

                Image bImg = btnGo.AddComponent<Image>();
                Button btn = btnGo.AddComponent<Button>();
                _claimButtons[achId] = btn;

                if (isClaimed)
                {
                    bImg.color = new Color(0.2f, 0.2f, 0.2f, 0.6f);
                    btn.interactable = false;
                    CreateLabel(bRt, "Lbl", "已領取", Vector2.zero, Vector2.one, 14, Color.gray, TextAnchor.MiddleCenter);
                }
                else if (isUnlocked)
                {
                    bImg.color = new Color(0.85f, 0.55f, 0.15f, 1f);
                    btn.interactable = true;
                    btn.onClick.AddListener(() => Claim(achId));
                    CreateLabel(bRt, "Lbl", "★ 領取", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);
                }
                else
                {
                    bImg.color = new Color(0.12f, 0.15f, 0.20f, 0.8f);
                    btn.interactable = false;
                    CreateLabel(bRt, "Lbl", "進行中", Vector2.zero, Vector2.one, 13, Color.gray, TextAnchor.MiddleCenter);
                }
            }
        }

        public void ShowClaimReward(IReadOnlyList<ItemStack> rewards)
        {
            LastClaimedRewards = rewards;
        }

        public void ShowError(string message)
        {
            LastError = message;
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child.gameObject);
                else UnityEngine.Object.DestroyImmediate(child.gameObject);
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
