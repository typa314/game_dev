#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain.Encounter;
using Game.Domain.MasterData;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 巧遇探索與少女約會地圖畫面 (OPT-7 / ARCH-04)。
    /// 顯示探索地點、體力值 (Stamina)、保底計數 (MissStreak)，
    /// 觸發地點巧遇事件並進行選項結算 (增加好感度與獲得道具，不改變角色持有數量)。
    /// </summary>
    public sealed class EncounterView : UnityScreenBase
    {
        private GameCore _core;

        // 頂部體力與資訊欄
        private Text _staminaLabel;
        private Text _missStreakLabel;

        // 地點按鈕清單
        private RectTransform _spotsContainer;
        private Dictionary<string, Button> _spotButtons = new Dictionary<string, Button>();

        // 巧遇事件彈窗
        private GameObject _eventModal;
        private Text _eventTitleLabel;
        private Text _eventDescLabel;
        private Button _watchStoryBtn;
        private RectTransform _optionsContainer;
        private List<Button> _optionButtons = new List<Button>();

        // 結算獎勵彈窗
        private GameObject _resultModal;
        private Text _resultTitleLabel;
        private Text _resultDetailsLabel;
        private Button _resultConfirmBtn;

        // 檢核與測試屬性 (G3 品質閘門)
        public string SelectedSpotId { get; private set; }
        public EncounterEventDef CurrentPendingEvent { get; private set; }
        public bool IsEventModalOpen => _eventModal != null && _eventModal.activeSelf;
        public bool IsResultModalOpen => _resultModal != null && _resultModal.activeSelf;
        public IReadOnlyList<Outcome> LastResultOutcomes { get; private set; }
        public string LastPlayedStoryScriptId { get; private set; }
        public Button WatchStoryButton => _watchStoryBtn;
        public string LastError { get; private set; }

        public override string ScreenId => "EncounterUI";

        public void Initialize(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            EnsureUiHierarchy();
            Refresh();
        }

        public void InitializeForTesting(GameCore core)
        {
            Initialize(core);
        }

        private void Awake()
        {
            EnsureUiHierarchy();
        }

        private void EnsureUiHierarchy()
        {
            if (_staminaLabel != null) return;

            Canvas canvas = GetComponent<Canvas>();
            if ( canvas == null )
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
            bgImg.color = new Color(0.05f, 0.08f, 0.12f, 1f);

            // 頂部導航與體力資訊
            BuildTopBar();

            // 地點卡片網格區域
            GameObject spotsGo = ProceduralUiKit.CreateUiObject("SpotsContainer", transform);
            _spotsContainer = spotsGo.GetComponent<RectTransform>();
            _spotsContainer.anchorMin = new Vector2(0.08f, 0.10f);
            _spotsContainer.anchorMax = new Vector2(0.92f, 0.82f);
            _spotsContainer.offsetMin = Vector2.zero;
            _spotsContainer.offsetMax = Vector2.zero;

            // 巧遇事件彈窗
            BuildEventModal();

            // 結算獎勵彈窗
            BuildResultModal();
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
            CreateLabel(tRt, "Title", "ENCOUNTER // 巧遇約會地圖", new Vector2(0.14f, 0f), new Vector2(0.50f, 1f), 20, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleLeft);

            // 體力與保底顯示
            _staminaLabel = CreateLabel(tRt, "Stamina", "⚡ 體力: 10 / 10", new Vector2(0.55f, 0.1f), new Vector2(0.75f, 0.9f), 16, new Color(0.4f, 0.9f, 0.5f), TextAnchor.MiddleCenter);
            _missStreakLabel = CreateLabel(tRt, "Streak", "保底計數: 0", new Vector2(0.76f, 0.1f), new Vector2(0.98f, 0.9f), 14, new Color(0.7f, 0.8f, 0.95f), TextAnchor.MiddleCenter);
        }

        private void BuildEventModal()
        {
            _eventModal = ProceduralUiKit.CreateUiObject("EventModal", transform);
            RectTransform mRt = _eventModal.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(mRt);
            Image mBg = _eventModal.AddComponent<Image>();
            mBg.color = new Color(0f, 0f, 0f, 0.85f);

            GameObject boxGo = ProceduralUiKit.CreateUiObject("Box", _eventModal.transform);
            RectTransform bRt = boxGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.20f, 0.20f);
            bRt.anchorMax = new Vector2(0.80f, 0.80f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            Image bImg = boxGo.AddComponent<Image>();
            bImg.color = new Color(0.08f, 0.12f, 0.20f, 0.98f);

            _eventTitleLabel = CreateLabel(bRt, "Title", "巧遇事件", new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.95f), 18, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter);
            _eventDescLabel = CreateLabel(bRt, "Desc", "少女向你走來...", new Vector2(0.08f, 0.56f), new Vector2(0.92f, 0.80f), 15, Color.white, TextAnchor.UpperLeft);

            // M2-2: 觀看巧遇劇情演出按鈕
            GameObject watchGo = ProceduralUiKit.CreateUiObject("WatchStoryBtn", bRt);
            RectTransform wRt = watchGo.GetComponent<RectTransform>();
            wRt.anchorMin = new Vector2(0.08f, 0.44f);
            wRt.anchorMax = new Vector2(0.92f, 0.54f);
            wRt.offsetMin = Vector2.zero;
            wRt.offsetMax = Vector2.zero;
            Image wImg = watchGo.AddComponent<Image>();
            wImg.color = new Color(0.55f, 0.25f, 0.45f, 1f);
            _watchStoryBtn = watchGo.AddComponent<Button>();
            _watchStoryBtn.onClick.AddListener(() => PlayEncounterStory());
            CreateLabel(wRt, "Lbl", "📖 觀看巧遇劇情 (ADV STORY)", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);

            GameObject optsGo = ProceduralUiKit.CreateUiObject("OptionsContainer", bRt);
            _optionsContainer = optsGo.GetComponent<RectTransform>();
            _optionsContainer.anchorMin = new Vector2(0.08f, 0.06f);
            _optionsContainer.anchorMax = new Vector2(0.92f, 0.41f);
            _optionsContainer.offsetMin = Vector2.zero;
            _optionsContainer.offsetMax = Vector2.zero;

            _eventModal.SetActive(false);
        }

        private void BuildResultModal()
        {
            _resultModal = ProceduralUiKit.CreateUiObject("ResultModal", transform);
            RectTransform mRt = _resultModal.GetComponent<RectTransform>();
            ProceduralUiKit.StretchFull(mRt);
            Image mBg = _resultModal.AddComponent<Image>();
            mBg.color = new Color(0f, 0f, 0f, 0.85f);

            GameObject boxGo = ProceduralUiKit.CreateUiObject("Box", _resultModal.transform);
            RectTransform bRt = boxGo.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0.25f, 0.25f);
            bRt.anchorMax = new Vector2(0.75f, 0.75f);
            bRt.offsetMin = Vector2.zero;
            bRt.offsetMax = Vector2.zero;
            Image bImg = boxGo.AddComponent<Image>();
            bImg.color = new Color(0.08f, 0.15f, 0.22f, 0.98f);

            _resultTitleLabel = CreateLabel(bRt, "Title", "約會結算", new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.95f), 20, new Color(0.3f, 0.9f, 0.5f), TextAnchor.MiddleCenter);
            _resultDetailsLabel = CreateLabel(bRt, "Details", "", new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.75f), 15, Color.white, TextAnchor.MiddleCenter);

            GameObject btnGo = ProceduralUiKit.CreateUiObject("ConfirmBtn", bRt);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.35f, 0.08f);
            btnRt.anchorMax = new Vector2(0.65f, 0.24f);
            btnRt.offsetMin = Vector2.zero;
            btnRt.offsetMax = Vector2.zero;
            Image btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.18f, 0.50f, 0.30f, 1f);
            _resultConfirmBtn = btnGo.AddComponent<Button>();
            _resultConfirmBtn.onClick.AddListener(CloseModals);
            CreateLabel(btnRt, "Lbl", "確定", Vector2.zero, Vector2.one, 15, Color.white, TextAnchor.MiddleCenter);

            _resultModal.SetActive(false);
        }

        public void Refresh()
        {
            if (_core == null) return;

            // 1. 刷新頂部體力
            int curStamina = _core.Player.Encounter.Stamina;
            _staminaLabel.text = $"⚡ 體力: {curStamina} / 10";
            _missStreakLabel.text = $"保底計數: {_core.Player.Encounter.MissStreak}";

            // 2. 刷新地點卡片
            ClearChildren(_spotsContainer);
            _spotButtons.Clear();

            var spots = _core.Master?.Spots;
            if (spots == null || spots.Count == 0) return;

            int index = 0;
            float cardW = 0.46f;
            float cardH = 0.40f;

            foreach (var kvp in spots)
            {
                string spotId = kvp.Key;
                SpotDef def = kvp.Value;
                bool isUnlocked = _core.Player.Encounter.UnlockedSpots.Contains(spotId);

                int r = index / 2;
                int c = index % 2;

                float xMin = c * 0.50f + 0.02f;
                float xMax = xMin + cardW;
                float yMax = 1.0f - r * 0.45f;
                float yMin = yMax - cardH;

                GameObject cardGo = ProceduralUiKit.CreateUiObject("SpotCard_" + spotId, _spotsContainer);
                RectTransform cRt = cardGo.GetComponent<RectTransform>();
                cRt.anchorMin = new Vector2(xMin, yMin);
                cRt.anchorMax = new Vector2(xMax, yMax);
                cRt.offsetMin = Vector2.zero;
                cRt.offsetMax = Vector2.zero;

                Image cImg = cardGo.AddComponent<Image>();
                cImg.color = isUnlocked ? new Color(0.12f, 0.18f, 0.28f, 0.95f) : new Color(0.06f, 0.08f, 0.12f, 0.8f);

                Button btn = cardGo.AddComponent<Button>();
                btn.interactable = isUnlocked;
                btn.onClick.AddListener(() => StartSpot(spotId));
                _spotButtons[spotId] = btn;

                string spotName = def.Name ?? spotId;
                CreateLabel(cRt, "Title", spotName, new Vector2(0.05f, 0.70f), new Vector2(0.95f, 0.92f), 16, isUnlocked ? new Color(1f, 0.85f, 0.4f) : Color.gray, TextAnchor.MiddleLeft);
                CreateLabel(cRt, "Status", isUnlocked ? "【探索開放中】消耗 1 點體力" : "【🔒 未解鎖】", new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.65f), 13, isUnlocked ? Color.white : Color.gray, TextAnchor.MiddleLeft);

                index++;
            }
        }

        public void StartSpot(string spotId)
        {
            if (_core == null || string.IsNullOrEmpty(spotId)) return;
            SelectedSpotId = spotId;

            var res = _core.StartEncounter(spotId);
            if (!res.Ok)
            {
                LastError = res.Error;
                return;
            }

            CurrentPendingEvent = res.Value.Event;
            ShowEventModal(CurrentPendingEvent);
            Refresh();
        }

        private void ShowEventModal(EncounterEventDef evt)
        {
            if (evt == null) return;
            _eventModal.SetActive(true);
            _eventTitleLabel.text = evt.Id;
            _eventDescLabel.text = $"遇見了神秘的身影... 請選擇應對方式：";

            ClearChildren(_optionsContainer);
            _optionButtons.Clear();

            int optIdx = 0;
            float optH = 0.42f;

            foreach (var kvp in evt.Outcomes)
            {
                string choiceKey = kvp.Key;
                float yMax = 1.0f - optIdx * 0.50f;
                float yMin = yMax - optH;

                GameObject btnGo = ProceduralUiKit.CreateUiObject("ChoiceBtn_" + choiceKey, _optionsContainer);
                RectTransform bRt = btnGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0.05f, yMin);
                bRt.anchorMax = new Vector2(0.95f, yMax);
                bRt.offsetMin = Vector2.zero;
                bRt.offsetMax = Vector2.zero;

                Image img = btnGo.AddComponent<Image>();
                img.color = new Color(0.15f, 0.30f, 0.50f, 1f);
                Button btn = btnGo.AddComponent<Button>();
                btn.onClick.AddListener(() => Choose(choiceKey));
                _optionButtons.Add(btn);

                CreateLabel(bRt, "Lbl", $"[選項 {optIdx + 1}] 與少女熱情交談 ({choiceKey})", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter);
                optIdx++;
            }
        }

        public void Choose(string choiceKey)
        {
            if (_core == null || string.IsNullOrEmpty(choiceKey)) return;

            var res = _core.ResolveEncounter(choiceKey);
            if (!res.Ok)
            {
                LastError = res.Error;
                return;
            }

            LastResultOutcomes = res.Value;
            _eventModal.SetActive(false);

            ShowResultModal(LastResultOutcomes);
            Refresh();
        }

        public bool PlayEncounterStory(StoryPlayerView targetPlayer = null)
        {
            if (_core == null) return false;

            string spotId = SelectedSpotId;
            if (string.IsNullOrEmpty(spotId) && CurrentPendingEvent != null)
            {
                spotId = CurrentPendingEvent.Spot;
            }

            string fileName = spotId switch
            {
                "spot_cafe" => "encounter_cafe.txt",
                "spot_shrine" => "encounter_shrine.txt",
                _ => "encounter_park.txt"
            };

            string p1 = System.IO.Path.Combine(Application.streamingAssetsPath, "Story", fileName);
            string p2 = System.IO.Path.Combine(Application.dataPath, "..", "..", "data", "story", fileName);
            string scriptText = null;
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
                scriptText = "[node Start]\nline l1 Elysia commander_spotted_in_park\nend\n";
            }

            StoryPlayerView playerView = targetPlayer ?? FindAnyObjectByType<StoryPlayerView>(FindObjectsInactive.Include);
            if (playerView == null)
            {
                var go = new GameObject("StoryPlayerUI");
                playerView = go.AddComponent<StoryPlayerView>();
            }

            LastPlayedStoryScriptId = fileName;
            var script = MiniScript.Parse(scriptText);
            return playerView.LoadAndPlay(fileName, script, _core, isRecollection: false);
        }

        private void ShowResultModal(IReadOnlyList<Outcome> outcomes)
        {
            _resultModal.SetActive(true);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("本次約會收穫：");

            if (outcomes != null)
            {
                for (int i = 0; i < outcomes.Count; i++)
                {
                    var o = outcomes[i];
                    if (o.AffectionChar != null)
                    {
                        sb.AppendLine($"❤️ {o.AffectionChar} 好感度 +{o.N}！");
                    }
                    if (o.Item != null)
                    {
                        sb.AppendLine($"🎁 獲得道具: {o.Item} x{o.N}");
                    }
                }
            }

            _resultDetailsLabel.text = sb.ToString();
        }

        public void CloseModals()
        {
            if (_eventModal != null) _eventModal.SetActive(false);
            if (_resultModal != null) _resultModal.SetActive(false);
        }

        public override bool OnBackRequested()
        {
            if (IsResultModalOpen || IsEventModalOpen)
            {
                CloseModals();
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
