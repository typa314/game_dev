using System.Collections.Generic;
using Game.Core.UI;
using Game.Domain.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity
{
    /// <summary>Playable battle page. The board and unit markers are replaceable presentation placeholders until battle sprites arrive.</summary>
    public sealed class BattlePageView : MonoBehaviour
    {
        private BattleRunner _runner;
        private Canvas _canvas;
        private RectTransform _board;
        private BattleSimulator _visibleSimulator;
        private Text _title, _clock, _allyHp, _enemyHp, _energy, _status;
        private Text _pauseLabel, _speedLabel;
        private Text _resultTitle, _resultLabel;
        private Image _allyBar, _enemyBar, _energyBar;
        private Button _upgradeButton, _pauseButton, _speedButton, _retreatButton;
        private Button _replayButton, _nextStageButton, _lobbyButton;
        private string _testNextStageId;
        private readonly List<Button> _slots = new List<Button>();
        private readonly List<Text> _slotLabels = new List<Text>();
        private readonly Dictionary<int, RectTransform> _units = new Dictionary<int, RectTransform>();
        private readonly Dictionary<int, Text> _unitHpLabels = new Dictionary<int, Text>();
        private readonly Dictionary<int, Text> _unitStatusLabels = new Dictionary<int, Text>();
        private readonly List<BattleEvent> _eventBuffer = new List<BattleEvent>();
        private GameObject _skillCutInPanel;
        private Text _skillCutInTitle;
        private Text _skillCutInSubtitle;
        private Text _baseAlertText;
        private float _cutInTimer;
        private float _baseAlertTimer;
        private GameObject _resultPanel;
        private RectTransform _resultViewport, _resultContent;
        private ScrollRect _resultScroll;
        private GameObject _lobby;
        private bool _wasActive;
        private bool _resultPopulated;

        public string LastSkillCutInText { get; private set; }
        public string LastBaseAlertText { get; private set; }
        public GameObject SkillCutInPanel { get { return _skillCutInPanel; } }
        public Text BaseAlertLabel { get { return _baseAlertText; } }
        public Button NextStageButton { get { return _nextStageButton; } }

        public void SetNextStageIdForTesting(string nextStageId)
        {
            _testNextStageId = nextStageId;
        }

        public string GetNextUnlockedStageId()
        {
            if (!string.IsNullOrEmpty(_testNextStageId)) return _testNextStageId;
            if (GameServices.IsReady && GameServices.Core != null && _runner != null && !string.IsNullOrEmpty(_runner.StageId))
            {
                StageSelectPresenter presenter = new StageSelectPresenter(GameServices.Core);
                return presenter.GetNextUnlockedStageId(_runner.StageId);
            }
            return null;
        }

        public void AttachRunnerForTesting(BattleRunner runner)
        {
            _runner = runner;
            if (_canvas == null) BuildUi();
        }

        public void InitializeForTesting(BattleRunner runner = null)
        {
            _runner = runner ?? GetComponent<BattleRunner>();
            if (_canvas == null) BuildUi();
        }

        public RectTransform GetUnitTransform(int uid)
        {
            RectTransform rt;
            _units.TryGetValue(uid, out rt);
            return rt;
        }

        public string GetUnitStatusText(int uid)
        {
            Text label;
            if (_unitStatusLabels.TryGetValue(uid, out label) && label != null)
                return label.text;
            return string.Empty;
        }

        private void Awake()
        {
            _runner = GetComponent<BattleRunner>();
            BuildUi();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_runner == null || _runner.Sim == null)
            {
                if (_wasActive) LeaveBattle();
                return;
            }

            if (!_wasActive) EnterBattle();
            if (_visibleSimulator != _runner.Sim)
            {
                ClearUnitMarkers();
                _resultPanel.SetActive(false);
                _resultPopulated = false;
                _visibleSimulator = _runner.Sim;
            }
            Refresh();
        }

        private void EnterBattle()
        {
            _wasActive = true;
            _resultPopulated = false;
            _visibleSimulator = _runner.Sim;
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            _lobby = GameObject.Find("LobbyUI");
            if (_lobby != null) _lobby.SetActive(false);
            DebugMenu menu = FindAnyObjectByType<DebugMenu>();
            if (menu != null) menu.enabled = false;
            if (_resultPanel != null) _resultPanel.SetActive(false);
        }

        private void LeaveBattle()
        {
            _wasActive = false;
            _resultPopulated = false;
            _visibleSimulator = null;
            ClearUnitMarkers();
            if (_skillCutInPanel != null) _skillCutInPanel.SetActive(false);
            if (_baseAlertText != null) _baseAlertText.gameObject.SetActive(false);
            _cutInTimer = 0f;
            _baseAlertTimer = 0f;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (_lobby != null) _lobby.SetActive(true);
            DebugMenu menu = FindAnyObjectByType<DebugMenu>();
            if (menu != null) menu.enabled = true;
        }

        public void Refresh()
        {
            if (_runner == null) _runner = GetComponent<BattleRunner>();
            if (_canvas == null) BuildUi();
            if (_runner == null || _runner.Sim == null) return;
            if (!_wasActive) EnterBattle();
            if (_visibleSimulator != _runner.Sim)
            {
                ClearUnitMarkers();
                if (_resultPanel != null) _resultPanel.SetActive(false);
                _resultPopulated = false;
                _visibleSimulator = _runner.Sim;
            }

            BattleSimulator sim = _runner.Sim;

            // 1. 消費並處理自上個更新週期累積的視圖事件
            _runner.ConsumeViewEvents(_eventBuffer);
            for (int i = 0; i < _eventBuffer.Count; i++)
            {
                ProcessEvent(_eventBuffer[i]);
            }

            // 2. 更新 Cut-in 與主堡警報計時器
            if (_cutInTimer > 0f)
            {
                _cutInTimer -= Time.deltaTime;
                if (_cutInTimer <= 0f && _skillCutInPanel != null)
                {
                    _skillCutInPanel.SetActive(false);
                }
            }
            if (_baseAlertTimer > 0f)
            {
                _baseAlertTimer -= Time.deltaTime;
                if (_baseAlertTimer <= 0f && _baseAlertText != null)
                {
                    _baseAlertText.gameObject.SetActive(false);
                }
            }

            // 3. 更新基本 UI 數據
            _title.text = "FIELD OPERATION  /  " + _runner.StageId;
            _clock.text = string.Format("{0:00}:{1:00}", sim.Tick / sim.TicksPerSecond / 60, sim.Tick / sim.TicksPerSecond % 60);
            _allyHp.text = "ALLY BASE  " + sim.AllyBaseHp + " / " + sim.AllyBaseMaxHp;
            _enemyHp.text = "ENEMY CORE  " + sim.EnemyBaseHp + " / " + sim.EnemyBaseMaxHp;
            SetFill(_allyBar, Ratio(sim.AllyBaseHp, sim.AllyBaseMaxHp));
            SetFill(_enemyBar, Ratio(sim.EnemyBaseHp, sim.EnemyBaseMaxHp));
            _energy.text = "ENERGY  " + sim.EnergyMilli / 1000 + " / " + sim.EnergyMax + "   •   LV " + (sim.EnergyLevel + 1);
            SetFill(_energyBar, sim.EnergyMax > 0 ? Mathf.Clamp01((float)sim.EnergyMilli / (sim.EnergyMax * 1000f)) : 0);
            _status.text = "DEFEATED " + sim.EnemiesDefeated + "   /   DEPLOYED " + sim.Deploys;
            _pauseLabel.text = _runner.Paused ? "RESUME" : "PAUSE";
            _speedLabel.text = "SPEED  x" + _runner.Speed.ToString("0");
            bool running = _runner.IsRunning;
            _upgradeButton.interactable = running && sim.EnergyUpgradeCost.HasValue;
            _pauseButton.interactable = running;
            _speedButton.interactable = running;
            _retreatButton.interactable = running;

            IReadOnlyList<string> team = _runner.TeamSnapshot;
            for (int i = 0; i < _slots.Count; i++)
            {
                bool exists = team != null && i < team.Count && !string.IsNullOrEmpty(team[i]);
                bool ready = exists && sim.IsSkillReady(i);
                int full = exists ? sim.SkillFullTicksOf(i) : 0;
                int pct = full > 0 ? Mathf.Clamp(sim.SkillChargeOf(i) * 100 / full, 0, 100) : 0;
                int cooldownTicks = exists ? sim.CooldownOf(i) : 0;
                int cd = cooldownTicks > 0 ? (cooldownTicks + sim.TicksPerSecond - 1) / sim.TicksPerSecond : 0;
                _slots[i].interactable = _runner.IsRunning && exists && (ready || sim.CanDeploy(i));
                _slotLabels[i].text = exists
                    ? (i + 1).ToString("00") + "   " + ShortName(team[i]) + "\n" + (ready ? "SKILL READY" : cd > 0 ? "RECOVERY " + cd + "s" : "SKILL " + pct + "%")
                    : (i + 1).ToString("00") + "   EMPTY\nNO UNIT";
                _slots[i].image.color = ready ? new Color(0.82f, 0.49f, 0.16f) : exists ? new Color(0.12f, 0.18f, 0.25f) : new Color(0.08f, 0.10f, 0.14f);
            }

            HashSet<int> living = new HashSet<int>();
            foreach (BattleUnit unit in sim.Units)
            {
                living.Add(unit.Uid);
                RectTransform rt;
                if (!_units.TryGetValue(unit.Uid, out rt)) rt = CreateUnit(unit);
                float p = sim.LengthMilli > 0 ? Mathf.Clamp01((float)unit.X / sim.LengthMilli) : 0.5f;
                float laneY = unit.Side == Side.Ally ? .23f : .48f;
                rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Lerp(.04f, .96f, p), laneY);
                rt.anchoredPosition = Vector2.zero;
                Image icon = rt.GetComponent<Image>();
                icon.color = unit.Side == Side.Ally ? new Color(0.28f, 0.71f, 0.88f) : new Color(0.91f, 0.36f, 0.37f);
                Text hpLabel;
                if (_unitHpLabels.TryGetValue(unit.Uid, out hpLabel)) hpLabel.text = unit.Hp.ToString();

                // 狀態視覺化標籤
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                if (unit.AtkBuffTicks > 0) sb.Append("[ATK↑]");
                if (unit.SlowTicks > 0) sb.Append("[SLOW]");
                if (unit.PoisonTicks > 0) sb.Append("[PSN]");
                if (unit.BurnTicks > 0) sb.Append("[BRN]");
                if (unit.StunTicks > 0) sb.Append("[STUN]");
                Text statusLabel;
                if (_unitStatusLabels.TryGetValue(unit.Uid, out statusLabel))
                {
                    statusLabel.text = sb.ToString();
                }
            }

            List<int> removed = new List<int>();
            foreach (KeyValuePair<int, RectTransform> entry in _units)
            {
                if (!living.Contains(entry.Key))
                {
                    SafeDestroy(entry.Value.gameObject);
                    removed.Add(entry.Key);
                }
            }
            foreach (int uid in removed)
            {
                _units.Remove(uid);
                _unitHpLabels.Remove(uid);
                _unitStatusLabels.Remove(uid);
            }

            if (!_runner.IsRunning && _runner.HasSubmitted)
            {
                _resultPanel.SetActive(true);
                _resultPanel.transform.SetAsLastSibling();
                if (!_resultPopulated)
                {
                    RefreshResult(sim);
                    _resultPopulated = true;
                }
            }
        }

        private void ProcessEvent(BattleEvent evt)
        {
            switch (evt.Type)
            {
                case BattleEventType.SkillCast:
                {
                    int slot = evt.Value;
                    string skillName = _runner != null ? _runner.GetSkillName(slot) : "BURST";
                    IReadOnlyList<string> team = _runner != null ? _runner.TeamSnapshot : null;
                    string charId = (team != null && slot >= 0 && slot < team.Count) ? team[slot] : null;
                    string charName = !string.IsNullOrEmpty(charId) ? ShortName(charId) : "UNIT";

                    string fullText = "★ SKILL CAST // " + skillName;
                    LastSkillCutInText = fullText;
                    if (_skillCutInTitle != null) _skillCutInTitle.text = fullText;
                    if (_skillCutInSubtitle != null) _skillCutInSubtitle.text = charName.ToUpperInvariant() + " ACTIVATES BURST";
                    if (_skillCutInPanel != null) _skillCutInPanel.SetActive(true);
                    _cutInTimer = 1.2f;
                    break;
                }
                case BattleEventType.BaseImmune:
                {
                    string text = "🛡️ BASE IMMUNE! (主堡免疫此攻擊類型)";
                    LastBaseAlertText = text;
                    if (_baseAlertText != null)
                    {
                        _baseAlertText.text = text;
                        _baseAlertText.color = new Color(1f, 0.85f, 0.3f);
                        _baseAlertText.gameObject.SetActive(true);
                    }
                    _baseAlertTimer = 1.5f;
                    break;
                }
                case BattleEventType.BaseShockwave:
                {
                    string text = "⚡ BASE SHOCKWAVE! (主堡反擊衝擊波)";
                    LastBaseAlertText = text;
                    if (_baseAlertText != null)
                    {
                        _baseAlertText.text = text;
                        _baseAlertText.color = new Color(1f, 0.35f, 0.35f);
                        _baseAlertText.gameObject.SetActive(true);
                    }
                    _baseAlertTimer = 1.5f;
                    break;
                }
            }
        }

        private void RefreshResult(BattleSimulator sim)
        {
            bool submitted = _runner.LastReward != null && _runner.LastReward.Ok;
            Game.Core.BattleReward reward = submitted ? _runner.LastReward.Value : null;
            BattleResult result = reward != null && reward.Result != null ? reward.Result : sim.Result();
            bool won = result.Outcome == BattleOutcome.Win;
            _resultTitle.text = won ? "OPERATION COMPLETE" : "OPERATION FAILED";
            _resultTitle.color = won ? new Color(1f, .82f, .4f) : new Color(1f, .48f, .43f);

            int seconds = result.Ticks / Mathf.Max(1, sim.TicksPerSecond);
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.Append("STAGE  ").Append(_runner.StageId).Append("      CLEAR TIME  ")
                .Append(seconds / 60).Append(':').Append((seconds % 60).ToString("00"));
            report.Append("\nENEMIES DEFEATED  ").Append(result.EnemiesDefeated)
                .Append("      UNITS DEPLOYED  ").Append(result.Deploys);
            report.Append("\nALLY BASE  ").Append(result.AllyBaseHp).Append(" / ").Append(result.AllyBaseMaxHp);
            report.Append("\n\nSTARS EARNED  ");
            if (!won) report.Append('0');
            else if (reward != null) report.Append(reward.Stars);
            else report.Append("UNAVAILABLE");

            if (reward != null)
            {
                if (won)
                {
                    report.Append("\n").Append(reward.FirstClear ? "FIRST CLEAR BONUS" : "STAGE CLEARED AGAIN");
                }

                if (reward.Items != null && reward.Items.Count > 0)
                {
                    report.Append("\n\nREWARDS");
                    foreach (Game.Domain.ItemStack item in reward.Items)
                        report.Append("\n  ").Append(ShortName(item.Item)).Append("  x").Append(item.N);
                }
                else report.Append(won ? "\n\nNO ITEMS THIS RUN" : "\n\nNO CLEAR REWARDS");

                if (reward.Unlocked != null && reward.Unlocked.Count > 0)
                {
                    report.Append("\n\nNEWLY UNLOCKED");
                    foreach (string unlocked in reward.Unlocked) report.Append("\n  ").Append(ShortName(unlocked));
                }
            }
            else
            {
                report.Append("\n\n").Append(submitted ? "NO CLEAR REWARDS" : "REWARD VALIDATION UNAVAILABLE");
                if (_runner.LastReward != null && !_runner.LastReward.Ok)
                    report.Append("\n").Append(_runner.LastReward.Error);
            }

            _resultLabel.text = report.ToString();
            Canvas.ForceUpdateCanvases();
            float textHeight = _resultLabel.preferredHeight + 24f;
            _resultContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(_resultViewport.rect.height, textHeight));
            _resultLabel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight - 24f);
            Canvas.ForceUpdateCanvases();
            _resultScroll.verticalNormalizedPosition = 1f;
            _replayButton.interactable = true;
            _lobbyButton.interactable = true;

            string nextStageId = GetNextUnlockedStageId();
            bool hasNextStage = won && !string.IsNullOrEmpty(nextStageId);
            if (_nextStageButton != null)
            {
                _nextStageButton.gameObject.SetActive(hasNextStage);
                _nextStageButton.interactable = hasNextStage;
            }

            if (hasNextStage)
            {
                SetButtonAnchors(_replayButton, new Vector2(.06f, .06f), new Vector2(.32f, .19f));
                SetButtonAnchors(_nextStageButton, new Vector2(.36f, .06f), new Vector2(.64f, .19f));
                SetButtonAnchors(_lobbyButton, new Vector2(.68f, .06f), new Vector2(.94f, .19f));
            }
            else
            {
                SetButtonAnchors(_replayButton, new Vector2(.10f, .06f), new Vector2(.44f, .19f));
                SetButtonAnchors(_lobbyButton, new Vector2(.56f, .06f), new Vector2(.90f, .19f));
            }
        }

        private RectTransform CreateUnit(BattleUnit unit)
        {
            GameObject go = Make("BattleUnit_" + unit.Uid, _board, new Vector2(62, 76), Vector2.zero);
            Image image = go.AddComponent<Image>();
            image.color = unit.Side == Side.Ally ? new Color(0.28f, 0.71f, 0.88f) : new Color(0.91f, 0.36f, 0.37f);
            string code = ShortName(unit.T.Id).Replace(" ", "");
            Text label = Label(go.transform, code.Substring(0, Mathf.Min(2, code.Length)), 18, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 2);
            Text hp = Label(go.transform, "", 11, Color.white, TextAnchor.LowerCenter);
            hp.name = "Hp";
            _unitHpLabels[unit.Uid] = hp;

            // 狀態視覺化標籤
            Text status = Label(go.transform, "", 10, new Color(1f, 0.88f, 0.25f), TextAnchor.MiddleCenter);
            status.name = "Status";
            RectTransform sRt = status.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0.5f, 0f);
            sRt.anchoredPosition = new Vector2(0f, 2f);
            sRt.sizeDelta = new Vector2(80f, 16f);
            _unitStatusLabels[unit.Uid] = status;

            RectTransform rt = go.GetComponent<RectTransform>();
            _units.Add(unit.Uid, rt);
            return rt;
        }

        private void BuildUi()
        {
            if (_canvas != null) return;
            GameObject root = new GameObject("BattlePageCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            Panel(root.transform, "Backdrop", new Vector2(0, 0), new Vector2(1, 1), new Color(0.035f, 0.055f, 0.085f));
            Panel(root.transform, "TopBar", new Vector2(0, .86f), new Vector2(1, 1), new Color(.055f, .085f, .12f));
            _title = AnchoredLabel(root.transform, "FIELD OPERATION", new Vector2(.04f, .91f), new Vector2(.55f, .98f), 26, new Color(.86f, .91f, .96f), TextAnchor.MiddleLeft);
            _clock = AnchoredLabel(root.transform, "00:00", new Vector2(.84f, .91f), new Vector2(.96f, .98f), 30, new Color(1f, .8f, .42f), TextAnchor.MiddleRight);

            _board = Panel(root.transform, "Board", new Vector2(.035f, .29f), new Vector2(.965f, .83f), new Color(.08f, .12f, .16f)).GetComponent<RectTransform>();
            Panel(_board, "Ground", new Vector2(.032f, .18f), new Vector2(.968f, .28f), new Color(.24f, .29f, .29f));
            AnchoredLabel(root.transform, "ALLY BASE", new Vector2(.05f, .46f), new Vector2(.22f, .52f), 17, new Color(.48f, .82f, .96f), TextAnchor.MiddleLeft);
            AnchoredLabel(root.transform, "ENEMY CORE", new Vector2(.78f, .46f), new Vector2(.95f, .52f), 17, new Color(1f, .52f, .46f), TextAnchor.MiddleRight);
            _allyHp = AnchoredLabel(root.transform, "ALLY BASE", new Vector2(.05f, .82f), new Vector2(.46f, .86f), 16, Color.white, TextAnchor.MiddleLeft);
            _enemyHp = AnchoredLabel(root.transform, "ENEMY CORE", new Vector2(.54f, .82f), new Vector2(.95f, .86f), 16, Color.white, TextAnchor.MiddleRight);
            _allyBar = Bar(root.transform, new Vector2(.05f, .79f), new Vector2(.46f, .81f), new Color(.23f, .67f, .82f));
            _enemyBar = Bar(root.transform, new Vector2(.54f, .79f), new Vector2(.95f, .81f), new Color(.83f, .29f, .31f));

            // 主堡特性警示橫幅
            _baseAlertText = AnchoredLabel(root.transform, "", new Vector2(0.20f, 0.70f), new Vector2(0.80f, 0.78f), 22, new Color(1f, 0.35f, 0.35f), TextAnchor.MiddleCenter);
            _baseAlertText.gameObject.SetActive(false);

            // 大招 Cut-in 面板
            _skillCutInPanel = Panel(root.transform, "SkillCutInPanel", new Vector2(0f, 0.40f), new Vector2(1f, 0.60f), new Color(0.04f, 0.08f, 0.14f, 0.94f));
            Panel(_skillCutInPanel.transform, "GoldBorderTop", new Vector2(0f, 0.96f), new Vector2(1f, 1f), new Color(1f, 0.82f, 0.35f, 0.9f));
            Panel(_skillCutInPanel.transform, "GoldBorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0.04f), new Color(1f, 0.82f, 0.35f, 0.9f));
            GameObject avatar = Panel(_skillCutInPanel.transform, "AvatarPlaceholder", new Vector2(0.05f, 0.10f), new Vector2(0.18f, 0.90f), new Color(0.2f, 0.35f, 0.5f, 0.8f));
            Text avatarTxt = Label(avatar.transform, "PORTRAIT", 14, Color.white, TextAnchor.MiddleCenter);
            Stretch(avatarTxt.rectTransform, 0);

            _skillCutInTitle = AnchoredLabel(_skillCutInPanel.transform, "", new Vector2(0.20f, 0.48f), new Vector2(0.95f, 0.90f), 28, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleLeft);
            _skillCutInSubtitle = AnchoredLabel(_skillCutInPanel.transform, "", new Vector2(0.20f, 0.12f), new Vector2(0.95f, 0.48f), 16, new Color(0.82f, 0.90f, 0.98f), TextAnchor.MiddleLeft);
            _skillCutInPanel.SetActive(false);

            Panel(root.transform, "Bottom", new Vector2(0, 0), new Vector2(1, .27f), new Color(.055f, .08f, .11f));
            _energy = AnchoredLabel(root.transform, "ENERGY", new Vector2(.04f, .215f), new Vector2(.38f, .255f), 18, new Color(1f, .83f, .48f), TextAnchor.MiddleLeft);
            _energyBar = Bar(root.transform, new Vector2(.04f, .19f), new Vector2(.42f, .205f), new Color(.91f, .59f, .22f));
            _status = AnchoredLabel(root.transform, "", new Vector2(.67f, .215f), new Vector2(.96f, .255f), 15, new Color(.7f, .78f, .83f), TextAnchor.MiddleRight);
            _upgradeButton = ButtonAt(root.transform, "UPGRADE ENERGY", new Vector2(.46f, .19f), new Vector2(.62f, .25f), Upgrade, new Color(.16f, .34f, .39f), 13);
            _pauseButton = ButtonAt(root.transform, "PAUSE", new Vector2(.72f, .12f), new Vector2(.80f, .18f), TogglePause, new Color(.16f, .23f, .29f), 14);
            _pauseLabel = _pauseButton.GetComponentInChildren<Text>();
            _speedButton = ButtonAt(root.transform, "SPEED  x1", new Vector2(.81f, .12f), new Vector2(.89f, .18f), ToggleSpeed, new Color(.16f, .23f, .29f), 14);
            _speedLabel = _speedButton.GetComponentInChildren<Text>();
            _retreatButton = ButtonAt(root.transform, "RETREAT", new Vector2(.90f, .12f), new Vector2(.97f, .18f), Retreat, new Color(.38f, .17f, .18f), 13);
            for (int i = 0; i < 10; i++)
            {
                int slot = i;
                float x0 = .04f + i * .092f;
                Button b = ButtonAt(root.transform, "", new Vector2(x0, .025f), new Vector2(x0 + .084f, .11f), delegate { _runner.TapSlot(slot); }, new Color(.12f, .18f, .25f), 13);
                _slots.Add(b);
                _slotLabels.Add(b.GetComponentInChildren<Text>());
            }

            _resultPanel = Panel(root.transform, "Result", new Vector2(.20f, .19f), new Vector2(.80f, .81f), new Color(.035f, .055f, .085f, .98f));
            _resultTitle = AnchoredLabel(_resultPanel.transform, "BATTLE REPORT", new Vector2(.06f, .84f), new Vector2(.94f, .97f), 30, new Color(1f, .82f, .4f), TextAnchor.MiddleCenter);
            GameObject viewport = new GameObject("ResultViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(_resultPanel.transform, false);
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;
            _resultViewport = viewport.GetComponent<RectTransform>();
            _resultViewport.anchorMin = new Vector2(.08f, .24f);
            _resultViewport.anchorMax = new Vector2(.92f, .81f);
            _resultViewport.offsetMin = _resultViewport.offsetMax = Vector2.zero;
            GameObject content = new GameObject("ResultContent", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _resultContent = content.GetComponent<RectTransform>();
            _resultContent.anchorMin = new Vector2(0f, 1f);
            _resultContent.anchorMax = new Vector2(1f, 1f);
            _resultContent.pivot = new Vector2(.5f, 1f);
            _resultContent.sizeDelta = new Vector2(0f, _resultViewport.rect.height);
            _resultLabel = AnchoredLabel(content.transform, "", Vector2.zero, Vector2.one, 18, Color.white, TextAnchor.UpperLeft);
            _resultLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            _resultLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _resultLabel.rectTransform.pivot = new Vector2(.5f, 1f);
            _resultLabel.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            _resultLabel.rectTransform.sizeDelta = new Vector2(-28f, _resultViewport.rect.height - 24f);
            _resultLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _resultScroll = _resultPanel.AddComponent<ScrollRect>();
            _resultScroll.viewport = _resultViewport;
            _resultScroll.content = _resultContent;
            _resultScroll.horizontal = false;
            _resultScroll.vertical = true;
            _resultScroll.movementType = ScrollRect.MovementType.Clamped;
            _resultScroll.inertia = false;
            _replayButton = ButtonAt(_resultPanel.transform, "REPLAY", new Vector2(.10f, .06f), new Vector2(.44f, .19f), Replay, new Color(.48f, .31f, .13f), 16);
            _nextStageButton = ButtonAt(_resultPanel.transform, "▶ NEXT STAGE", new Vector2(.36f, .06f), new Vector2(.64f, .19f), OnClickNextStage, new Color(.85f, .45f, .12f), 16);
            _nextStageButton.gameObject.SetActive(false);
            _lobbyButton = ButtonAt(_resultPanel.transform, "RETURN TO LOBBY", new Vector2(.56f, .06f), new Vector2(.90f, .19f), Retreat, new Color(.18f, .44f, .48f), 16);
        }

        private void Upgrade() { if (_runner.Sim != null && _runner.Sim.EnergyUpgradeCost.HasValue) _runner.UpgradeEnergy(); }
        private void TogglePause() { _runner.Paused = !_runner.Paused; }
        private void ToggleSpeed() { _runner.Speed = _runner.Speed >= 2f ? 1f : 2f; }
        private void Retreat() { _runner.Abort(); }
        private void Replay()
        {
            string stageId = _runner.StageId;
            if (!string.IsNullOrEmpty(stageId)) _runner.StartBattle(stageId);
        }
        public void OnClickNextStage()
        {
            string nextStageId = GetNextUnlockedStageId();
            if (!string.IsNullOrEmpty(nextStageId) && _runner != null)
            {
                _runner.StartBattle(nextStageId);
            }
        }
        private static void SetButtonAnchors(Button btn, Vector2 min, Vector2 max)
        {
            if (btn == null) return;
            RectTransform rt = btn.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = min;
                rt.anchorMax = max;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
        }
        private void ClearUnitMarkers()
        {
            if (_board != null)
                foreach (Transform child in _board)
                    if (child.name.StartsWith("BattleUnit_")) SafeDestroy(child.gameObject);
            _units.Clear();
            _unitHpLabels.Clear();
            _unitStatusLabels.Clear();
        }
        private static float Ratio(int value, int max) { return max > 0 ? Mathf.Clamp01((float)value / max) : 0; }
        private static void SetFill(Image image, float value)
        {
            RectTransform rt = image.rectTransform;
            Vector2 max = rt.anchorMax;
            max.x = Mathf.Clamp01(value);
            rt.anchorMax = max;
            rt.offsetMax = new Vector2(0, rt.offsetMax.y);
        }
        private static string ShortName(string id) { return id == null ? "UNIT" : id.Replace("char", "").Replace("_", " ").ToUpperInvariant(); }

        private static GameObject Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>(); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color; return go;
        }
        private static Image Bar(Transform parent, Vector2 min, Vector2 max, Color color)
        {
            GameObject back = Panel(parent, "Bar", min, max, new Color(.12f, .16f, .19f));
            GameObject fill = Panel(back.transform, "Fill", Vector2.zero, Vector2.one, color);
            return fill.GetComponent<Image>();
        }
        private static Text AnchoredLabel(Transform parent, string value, Vector2 min, Vector2 max, int size, Color color, TextAnchor align)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>(); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
            Text t = go.GetComponent<Text>(); t.font = DefaultFont(); t.fontSize = size; t.color = color; t.alignment = align; t.text = value; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false; return t;
        }
        private static Text Label(Transform parent, string value, int size, Color color, TextAnchor align)
        {
            Text t = AnchoredLabel(parent, value, Vector2.zero, Vector2.one, size, color, align); return t;
        }
        private static Button ButtonAt(Transform parent, string value, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, Color color, int size)
        {
            GameObject go = Panel(parent, "Button", min, max, color); Button b = go.AddComponent<Button>(); b.onClick.AddListener(action);
            Text t = Label(go.transform, value, size, Color.white, TextAnchor.MiddleCenter); Stretch(t.rectTransform, 3); return b;
        }
        private static GameObject Make(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(.5f, 0); rt.sizeDelta = size; rt.anchoredPosition = pos; return go;
        }
        private static void Stretch(RectTransform rt, float margin)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(margin, margin); rt.offsetMax = new Vector2(-margin, -margin); }
        private static Font DefaultFont()
        { Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        private static void SafeDestroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
