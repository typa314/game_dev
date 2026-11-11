using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Game.Core.UI;
using Game.Domain.Battle;
using Game.Unity.UI;
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
        private sealed class UnitViewHolder
        {
            public RectTransform Root;
            public Image RootImage;
            public Transform VisualTransform;
            public Text HpLabel;
            public Text StatusLabel;
            public int LastHp = int.MinValue;
            public bool LastHealPulse;
            public int LastStatusMask = int.MinValue;
            public float LastScaleX;
        }

        private readonly List<Button> _slots = new List<Button>();
        private readonly List<Text> _slotLabels = new List<Text>();
        private readonly Dictionary<int, UnitViewHolder> _unitViews = new Dictionary<int, UnitViewHolder>();
        private readonly Dictionary<int, float> _unitHealPulseTimers = new Dictionary<int, float>();
        private readonly HashSet<int> _livingUids = new HashSet<int>();
        private readonly List<int> _removedUids = new List<int>();
        private readonly List<int> _pulseKeys = new List<int>();
        private readonly StringBuilder _statusSb = new StringBuilder(48);
        private readonly List<BattleEvent> _eventBuffer = new List<BattleEvent>();
        private GameObject _skillCutInPanel;
        private Text _skillCutInTitle;
        private Text _skillCutInSubtitle;
        private Text _baseAlertText;
        private Image _allyBaseImage;
        private Image _enemyBaseImage;
        private Text _allyBaseBoardLabel;
        private Text _enemyBaseBoardLabel;
        private bool _hasAllyBaseSprite;
        private bool _hasEnemyBaseSprite;
        private float _allyBaseHitTimer;
        private float _enemyBaseHitTimer;
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
            return _unitViews.TryGetValue(uid, out UnitViewHolder holder) ? holder.Root : null;
        }

        public string GetUnitStatusText(int uid)
        {
            if (_unitViews.TryGetValue(uid, out UnitViewHolder holder) && holder.StatusLabel != null)
                return holder.StatusLabel.text;
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

            // 1. 更新現有計時器 (上一幀累積至此幀之經過時間)
            float dt = Time.deltaTime;
            if (_cutInTimer > 0f)
            {
                _cutInTimer -= dt;
                if (_cutInTimer <= 0f && _skillCutInPanel != null)
                {
                    _skillCutInPanel.SetActive(false);
                }
            }
            if (_baseAlertTimer > 0f)
            {
                _baseAlertTimer -= dt;
                if (_baseAlertTimer <= 0f && _baseAlertText != null)
                {
                    _baseAlertText.gameObject.SetActive(false);
                }
            }
            if (_unitHealPulseTimers.Count > 0)
            {
                _pulseKeys.Clear();
                foreach (int k in _unitHealPulseTimers.Keys) _pulseKeys.Add(k);
                for (int i = 0; i < _pulseKeys.Count; i++)
                {
                    int k = _pulseKeys[i];
                    float remaining = _unitHealPulseTimers[k] - dt;
                    if (remaining <= 0f) _unitHealPulseTimers.Remove(k);
                    else _unitHealPulseTimers[k] = remaining;
                }
            }
            if (_allyBaseHitTimer > 0f) _allyBaseHitTimer -= dt;
            if (_enemyBaseHitTimer > 0f) _enemyBaseHitTimer -= dt;

            // 2. 消費並處理自上個更新週期累積的視圖事件
            _runner.ConsumeViewEvents(_eventBuffer);
            for (int i = 0; i < _eventBuffer.Count; i++)
            {
                ProcessEvent(_eventBuffer[i]);
            }

            // 3. 更新基本 UI 數據
            _title.text = "FIELD OPERATION  /  " + _runner.StageId;
            _clock.text = string.Format("{0:00}:{1:00}", sim.Tick / sim.TicksPerSecond / 60, sim.Tick / sim.TicksPerSecond % 60);
            _allyHp.text = "ALLY BASE (LV." + sim.AllyBaseLevel + ")  " + sim.AllyBaseHp + " / " + sim.AllyBaseMaxHp;
            _enemyHp.text = "ENEMY CORE  " + sim.EnemyBaseHp + " / " + sim.EnemyBaseMaxHp;
            if (_allyBaseBoardLabel != null) _allyBaseBoardLabel.text = "ALLY BASTION LV." + sim.AllyBaseLevel;
            if (_allyBaseImage != null)
            {
                Color allyNormal = _hasAllyBaseSprite ? Color.white : new Color(0.20f, 0.45f, 0.68f, 0.92f);
                _allyBaseImage.color = _allyBaseHitTimer > 0f ? new Color(1f, 0.45f, 0.45f, 1f) : allyNormal;
            }
            if (_enemyBaseImage != null)
            {
                Color enemyNormal = _hasEnemyBaseSprite ? Color.white : new Color(0.52f, 0.16f, 0.22f, 0.92f);
                _enemyBaseImage.color = _enemyBaseHitTimer > 0f ? new Color(1f, 0.45f, 0.45f, 1f) : enemyNormal;
            }
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

            _livingUids.Clear();
            for (int i = 0; i < sim.Units.Count; i++)
            {
                BattleUnit unit = sim.Units[i];
                _livingUids.Add(unit.Uid);
                UnitViewHolder holder;
                if (!_unitViews.TryGetValue(unit.Uid, out holder)) holder = CreateUnit(unit);
                float p = sim.LengthMilli > 0 ? Mathf.Clamp01((float)unit.X / sim.LengthMilli) : 0.5f;
                float laneY = (unit.T != null && unit.T.Type == "flying")
                    ? .28f
                    : (unit.Side == Side.Ally ? .22f : .25f);
                holder.Root.anchorMin = holder.Root.anchorMax = new Vector2(Mathf.Lerp(.04f, .96f, p), laneY);
                holder.Root.anchoredPosition = Vector2.zero;

                if (holder.VisualTransform != null)
                {
                    if (holder.RootImage != null && holder.RootImage.color != Color.clear)
                    {
                        holder.RootImage.color = Color.clear;
                    }
                    float targetScaleX = unit.Side == Side.Enemy ? -1f : 1f;
                    if (Mathf.Abs(targetScaleX - holder.LastScaleX) > 0.01f)
                    {
                        holder.VisualTransform.localScale = new Vector3(targetScaleX, 1f, 1f);
                        holder.LastScaleX = targetScaleX;
                    }
                }
                else if (holder.RootImage != null)
                {
                    Color sideColor = unit.Side == Side.Ally ? new Color(0.28f, 0.71f, 0.88f) : new Color(0.91f, 0.36f, 0.37f);
                    if (holder.RootImage.color != sideColor) holder.RootImage.color = sideColor;
                }

                if (holder.HpLabel != null)
                {
                    if (unit.Hp != holder.LastHp)
                    {
                        holder.HpLabel.text = unit.Hp.ToString();
                        holder.LastHp = unit.Hp;
                    }
                    bool isHealing = _unitHealPulseTimers.TryGetValue(unit.Uid, out float pulseTimer) && pulseTimer > 0f;
                    if (isHealing != holder.LastHealPulse)
                    {
                        holder.HpLabel.color = isHealing ? new Color(0.35f, 1f, 0.55f) : Color.white;
                        holder.LastHealPulse = isHealing;
                    }
                }

                // 狀態視覺化標籤 (位元遮罩比對，僅狀態變更時才組字串)
                if (holder.StatusLabel != null)
                {
                    int mask = (unit.AtkBuffTicks > 0 ? 1 : 0)
                        | (unit.SlowTicks > 0 ? 2 : 0)
                        | (unit.PoisonTicks > 0 ? 4 : 0)
                        | (unit.BurnTicks > 0 ? 8 : 0)
                        | (unit.StunTicks > 0 ? 16 : 0);
                    if (mask != holder.LastStatusMask)
                    {
                        holder.LastStatusMask = mask;
                        if (mask == 0)
                        {
                            holder.StatusLabel.text = string.Empty;
                        }
                        else
                        {
                            _statusSb.Clear();
                            if ((mask & 1) != 0) _statusSb.Append("[ATK↑]");
                            if ((mask & 2) != 0) _statusSb.Append("[SLOW]");
                            if ((mask & 4) != 0) _statusSb.Append("[PSN]");
                            if ((mask & 8) != 0) _statusSb.Append("[BRN]");
                            if ((mask & 16) != 0) _statusSb.Append("[STUN]");
                            holder.StatusLabel.text = _statusSb.ToString();
                        }
                    }
                }
            }

            _removedUids.Clear();
            foreach (KeyValuePair<int, UnitViewHolder> entry in _unitViews)
            {
                if (!_livingUids.Contains(entry.Key))
                {
                    SafeDestroy(entry.Value.Root.gameObject);
                    _removedUids.Add(entry.Key);
                }
            }
            for (int i = 0; i < _removedUids.Count; i++)
            {
                int uid = _removedUids[i];
                _unitViews.Remove(uid);
                _unitHealPulseTimers.Remove(uid);
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
                case BattleEventType.Heal:
                {
                    _unitHealPulseTimers[evt.Uid] = 0.45f;
                    break;
                }
                case BattleEventType.BaseHit:
                {
                    if (evt.Side == Side.Ally) _enemyBaseHitTimer = 0.18f;
                    else _allyBaseHitTimer = 0.18f;
                    break;
                }
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

        private static Sprite TryLoadCachedSprite(string cacheKey, string[] candidatePaths, Vector2 pivot)
        {
            return AssetService.ResolveStreamingSprite(cacheKey, candidatePaths, pivot, "[BattlePageView]");
        }

        public static Sprite TryLoadBattleUnitSprite(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return null;
            return TryLoadCachedSprite(
                $"battle_unit_{charId}",
                new[]
                {
                    $"Battle/Units/{charId}.png",
                    $"Portraits/{charId}.png"
                },
                new Vector2(0.5f, 0f));
        }

        public static Sprite TryLoadAuraSprite(string auraKey)
        {
            if (string.IsNullOrEmpty(auraKey)) return null;
            return TryLoadCachedSprite(
                $"aura_{auraKey}",
                new[] { $"Battle/Auras/{auraKey}.png" },
                new Vector2(0.5f, 0.5f));
        }

        public static Sprite TryLoadBaseSprite(string baseKey)
        {
            if (string.IsNullOrEmpty(baseKey)) return null;
            return TryLoadCachedSprite(
                $"base_{baseKey}",
                new[] { $"Battle/Bases/{baseKey}.png" },
                new Vector2(0.5f, 0f));
        }

        public static bool IsHealerUnit(BattleUnit unit)
        {
            return BattleSimulator.IsHealer(unit);
        }

        private static Vector2 GetBattleUnitDisplaySize(BattleUnit unit, bool hasSprite)
        {
            if (!hasSprite) return new Vector2(62, 76);
            if (unit?.T != null)
            {
                if (!string.IsNullOrEmpty(unit.T.ScaleCategory))
                {
                    if (unit.T.ScaleCategory == "boss") return new Vector2(142, 162);
                    if (unit.T.ScaleCategory == "small") return new Vector2(74, 84);
                    return new Vector2(96, 110);
                }
                if (unit.T.Type == "armored") return new Vector2(142, 162);
                if (unit.T.Id == "en_slime" || unit.T.Id == "en_bat") return new Vector2(74, 84);
            }
            return new Vector2(96, 110);
        }

        private UnitViewHolder CreateUnit(BattleUnit unit)
        {
            Sprite battleSprite = TryLoadBattleUnitSprite(unit.T.Id);
            bool hasSprite = battleSprite != null;

            Vector2 size = GetBattleUnitDisplaySize(unit, hasSprite);
            GameObject go = Make("BattleUnit_" + unit.Uid, _board, size, Vector2.zero);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0f);

            Image image = go.AddComponent<Image>();
            image.color = hasSprite
                ? Color.clear
                : (unit.Side == Side.Ally ? new Color(0.28f, 0.71f, 0.88f) : new Color(0.91f, 0.36f, 0.37f));

            bool isHealer = IsHealerUnit(unit);
            if (isHealer)
            {
                GameObject auraGo = new GameObject("HealAura", typeof(RectTransform), typeof(Image));
                auraGo.transform.SetParent(go.transform, false);
                RectTransform aRt = auraGo.GetComponent<RectTransform>();
                aRt.anchorMin = new Vector2(0.5f, 0f);
                aRt.anchorMax = new Vector2(0.5f, 0f);
                aRt.pivot = new Vector2(0.5f, 0.5f);
                float auraWidth = Mathf.Clamp(unit.T != null && unit.T.RangeMilli > 0 ? (unit.T.RangeMilli / 1000f) * 0.75f : 88f, 84f, 160f);

                aRt.sizeDelta = new Vector2(auraWidth, auraWidth * 0.30f);
                aRt.anchoredPosition = new Vector2(0f, 4f);
                Image aImg = auraGo.GetComponent<Image>();
                Sprite auraSp = TryLoadAuraSprite("heal_aura");
                if (auraSp != null)
                {
                    aImg.sprite = auraSp;
                    aImg.color = Color.white;
                }
                else
                {
                    aImg.color = new Color(0.25f, 0.95f, 0.55f, 0.50f);
                }
                auraGo.transform.SetAsFirstSibling();
            }

            Transform visualTransform = null;
            float initialScaleX = unit.Side == Side.Enemy ? -1f : 1f;
            if (hasSprite)
            {
                GameObject visualGo = new GameObject("Visual", typeof(RectTransform), typeof(Image));
                visualGo.transform.SetParent(go.transform, false);
                RectTransform vRt = visualGo.GetComponent<RectTransform>();
                ProceduralUiKit.StretchFull(vRt, 0f);
                vRt.pivot = new Vector2(0.5f, 0f);
                vRt.localScale = new Vector3(initialScaleX, 1f, 1f);
                Image vImg = visualGo.GetComponent<Image>();
                vImg.sprite = battleSprite;
                vImg.color = Color.white;
                vImg.preserveAspect = true;
                visualTransform = visualGo.transform;
            }
            else
            {
                string code = ShortName(unit.T.Id).Replace(" ", "");
                ProceduralUiKit.CreateText(go.transform, "FallbackCode", code.Substring(0, Mathf.Min(2, code.Length)), Vector2.zero, Vector2.one, 18, Color.white, TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
            }

            Text hp = ProceduralUiKit.CreateText(go.transform, "Hp", "", Vector2.zero, Vector2.one, 11, Color.white, TextAnchor.LowerCenter, HorizontalWrapMode.Overflow);
            RectTransform hpRt = hp.rectTransform;
            hpRt.anchorMin = new Vector2(0f, 0f);
            hpRt.anchorMax = new Vector2(1f, 0f);
            hpRt.pivot = new Vector2(0.5f, 1f);
            hpRt.anchoredPosition = new Vector2(0f, -2f);
            hpRt.sizeDelta = new Vector2(80f, 16f);

            // 狀態標籤
            Text status = ProceduralUiKit.CreateText(go.transform, "Status", "", Vector2.zero, Vector2.one, 10, new Color(1f, 0.88f, 0.25f), TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
            RectTransform sRt = status.rectTransform;
            sRt.anchorMin = new Vector2(0f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(0.5f, 0f);
            sRt.anchoredPosition = new Vector2(0f, 2f);
            sRt.sizeDelta = new Vector2(80f, 16f);

            var holder = new UnitViewHolder
            {
                Root = rt,
                RootImage = image,
                VisualTransform = visualTransform,
                HpLabel = hp,
                StatusLabel = status,
                LastScaleX = initialScaleX
            };
            _unitViews.Add(unit.Uid, holder);
            return holder;
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

            ProceduralUiKit.CreatePanel(root.transform, "Backdrop", new Vector2(0, 0), new Vector2(1, 1), new Color(0.035f, 0.055f, 0.085f));
            ProceduralUiKit.CreatePanel(root.transform, "TopBar", new Vector2(0, .86f), new Vector2(1, 1), new Color(.055f, .085f, .12f));
            _title = ProceduralUiKit.CreateText(root.transform, "Title", "FIELD OPERATION", new Vector2(.04f, .91f), new Vector2(.55f, .98f), 26, new Color(.86f, .91f, .96f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            _clock = ProceduralUiKit.CreateText(root.transform, "Clock", "00:00", new Vector2(.84f, .91f), new Vector2(.96f, .98f), 30, new Color(1f, .8f, .42f), TextAnchor.MiddleRight, HorizontalWrapMode.Overflow);

            _board = ProceduralUiKit.CreatePanel(root.transform, "Board", new Vector2(.035f, .29f), new Vector2(.965f, .83f), new Color(.08f, .12f, .16f)).GetComponent<RectTransform>();
            ProceduralUiKit.CreatePanel(_board, "Ground", new Vector2(.032f, .18f), new Vector2(.968f, .28f), new Color(.24f, .29f, .29f));

            // 我方主堡視覺建築節點
            Sprite allyBaseSp = TryLoadBaseSprite("ally_base");
            _hasAllyBaseSprite = allyBaseSp != null;
            GameObject allyBaseGo = Make("AllyBaseVisual", _board, new Vector2(210, 210), Vector2.zero);
            RectTransform abRt = allyBaseGo.GetComponent<RectTransform>();
            abRt.anchorMin = abRt.anchorMax = new Vector2(0.06f, 0.21f);
            abRt.pivot = new Vector2(0.5f, 0f);
            abRt.anchoredPosition = Vector2.zero;
            _allyBaseImage = allyBaseGo.AddComponent<Image>();
            if (_hasAllyBaseSprite)
            {
                _allyBaseImage.sprite = allyBaseSp;
                _allyBaseImage.color = Color.white;
                _allyBaseImage.preserveAspect = true;
            }
            else
            {
                _allyBaseImage.color = new Color(0.20f, 0.45f, 0.68f, 0.92f);
            }
            _allyBaseBoardLabel = ProceduralUiKit.CreateText(allyBaseGo.transform, "Label", "ALLY BASTION LV.1", Vector2.zero, Vector2.one, 12, new Color(.55f, .88f, 1f), TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
            RectTransform abLblRt = _allyBaseBoardLabel.rectTransform;
            abLblRt.anchorMin = new Vector2(0f, 0f);
            abLblRt.anchorMax = new Vector2(1f, 0f);
            abLblRt.pivot = new Vector2(0.5f, 1f);
            abLblRt.anchoredPosition = new Vector2(0f, -6f);
            abLblRt.sizeDelta = new Vector2(40f, 20f);

            // 敵方主堡視覺建築節點
            Sprite enemyBaseSp = TryLoadBaseSprite("enemy_base");
            _hasEnemyBaseSprite = enemyBaseSp != null;
            GameObject enemyBaseGo = Make("EnemyBaseVisual", _board, new Vector2(220, 220), Vector2.zero);
            RectTransform ebRt = enemyBaseGo.GetComponent<RectTransform>();
            ebRt.anchorMin = ebRt.anchorMax = new Vector2(0.94f, 0.21f);
            ebRt.pivot = new Vector2(0.5f, 0f);
            ebRt.anchoredPosition = Vector2.zero;
            _enemyBaseImage = enemyBaseGo.AddComponent<Image>();
            if (_hasEnemyBaseSprite)
            {
                _enemyBaseImage.sprite = enemyBaseSp;
                _enemyBaseImage.color = Color.white;
                _enemyBaseImage.preserveAspect = true;
            }
            else
            {
                _enemyBaseImage.color = new Color(0.52f, 0.16f, 0.22f, 0.92f);
            }
            _enemyBaseBoardLabel = ProceduralUiKit.CreateText(enemyBaseGo.transform, "Label", "ENEMY CORE", Vector2.zero, Vector2.one, 12, new Color(1f, .52f, .46f), TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
            RectTransform ebLblRt = _enemyBaseBoardLabel.rectTransform;
            ebLblRt.anchorMin = new Vector2(0f, 0f);
            ebLblRt.anchorMax = new Vector2(1f, 0f);
            ebLblRt.pivot = new Vector2(0.5f, 1f);
            ebLblRt.anchoredPosition = new Vector2(0f, -6f);
            ebLblRt.sizeDelta = new Vector2(40f, 20f);
            _allyHp = ProceduralUiKit.CreateText(root.transform, "AllyHp", "ALLY BASE", new Vector2(.05f, .82f), new Vector2(.46f, .86f), 16, Color.white, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            _enemyHp = ProceduralUiKit.CreateText(root.transform, "EnemyHp", "ENEMY CORE", new Vector2(.54f, .82f), new Vector2(.95f, .86f), 16, Color.white, TextAnchor.MiddleRight, HorizontalWrapMode.Overflow);
            _allyBar = Bar(root.transform, new Vector2(.05f, .79f), new Vector2(.46f, .81f), new Color(.23f, .67f, .82f));
            _enemyBar = Bar(root.transform, new Vector2(.54f, .79f), new Vector2(.95f, .81f), new Color(.83f, .29f, .31f));

            // 主堡特性警示橫幅
            _baseAlertText = ProceduralUiKit.CreateText(root.transform, "BaseAlert", "", new Vector2(0.20f, 0.70f), new Vector2(0.80f, 0.78f), 22, new Color(1f, 0.35f, 0.35f), TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
            _baseAlertText.gameObject.SetActive(false);

            // 大招 Cut-in 面板
            _skillCutInPanel = ProceduralUiKit.CreatePanel(root.transform, "SkillCutInPanel", new Vector2(0f, 0.40f), new Vector2(1f, 0.60f), new Color(0.04f, 0.08f, 0.14f, 0.94f));
            ProceduralUiKit.CreatePanel(_skillCutInPanel.transform, "GoldBorderTop", new Vector2(0f, 0.96f), new Vector2(1f, 1f), new Color(1f, 0.82f, 0.35f, 0.9f));
            ProceduralUiKit.CreatePanel(_skillCutInPanel.transform, "GoldBorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0.04f), new Color(1f, 0.82f, 0.35f, 0.9f));
            GameObject avatar = ProceduralUiKit.CreatePanel(_skillCutInPanel.transform, "AvatarPlaceholder", new Vector2(0.05f, 0.10f), new Vector2(0.18f, 0.90f), new Color(0.2f, 0.35f, 0.5f, 0.8f));
            ProceduralUiKit.CreateText(avatar.transform, "Label", "PORTRAIT", Vector2.zero, Vector2.one, 14, Color.white, TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);

            _skillCutInTitle = ProceduralUiKit.CreateText(_skillCutInPanel.transform, "CutInTitle", "", new Vector2(0.20f, 0.48f), new Vector2(0.95f, 0.90f), 28, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            _skillCutInSubtitle = ProceduralUiKit.CreateText(_skillCutInPanel.transform, "CutInSubtitle", "", new Vector2(0.20f, 0.12f), new Vector2(0.95f, 0.48f), 16, new Color(0.82f, 0.90f, 0.98f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            _skillCutInPanel.SetActive(false);

            ProceduralUiKit.CreatePanel(root.transform, "Bottom", new Vector2(0, 0), new Vector2(1, .27f), new Color(.055f, .08f, .11f));
            _energy = ProceduralUiKit.CreateText(root.transform, "Energy", "ENERGY", new Vector2(.04f, .215f), new Vector2(.38f, .255f), 18, new Color(1f, .83f, .48f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            _energyBar = Bar(root.transform, new Vector2(.04f, .19f), new Vector2(.42f, .205f), new Color(.91f, .59f, .22f));
            _status = ProceduralUiKit.CreateText(root.transform, "Status", "", new Vector2(.67f, .215f), new Vector2(.96f, .255f), 15, new Color(.7f, .78f, .83f), TextAnchor.MiddleRight, HorizontalWrapMode.Overflow);
            _upgradeButton = ProceduralUiKit.CreateButton(root.transform, "UpgradeBtn", "UPGRADE ENERGY", new Vector2(.46f, .19f), new Vector2(.62f, .25f), Upgrade, new Color(.16f, .34f, .39f), 13, 3f, HorizontalWrapMode.Overflow);
            _pauseButton = ProceduralUiKit.CreateButton(root.transform, "PauseBtn", "PAUSE", new Vector2(.72f, .12f), new Vector2(.80f, .18f), TogglePause, new Color(.16f, .23f, .29f), 14, 3f, HorizontalWrapMode.Overflow);
            _pauseLabel = _pauseButton.GetComponentInChildren<Text>();
            _speedButton = ProceduralUiKit.CreateButton(root.transform, "SpeedBtn", "SPEED  x1", new Vector2(.81f, .12f), new Vector2(.89f, .18f), ToggleSpeed, new Color(.16f, .23f, .29f), 14, 3f, HorizontalWrapMode.Overflow);
            _speedLabel = _speedButton.GetComponentInChildren<Text>();
            _retreatButton = ProceduralUiKit.CreateButton(root.transform, "RetreatBtn", "RETREAT", new Vector2(.90f, .12f), new Vector2(.97f, .18f), Retreat, new Color(.38f, .17f, .18f), 13, 3f, HorizontalWrapMode.Overflow);
            for (int i = 0; i < 10; i++)
            {
                int slot = i;
                float x0 = .04f + i * .092f;
                Button b = ProceduralUiKit.CreateButton(root.transform, "SlotBtn_" + i, "", new Vector2(x0, .025f), new Vector2(x0 + .084f, .11f), delegate { _runner.TapSlot(slot); }, new Color(.12f, .18f, .25f), 13, 3f, HorizontalWrapMode.Overflow);
                _slots.Add(b);
                _slotLabels.Add(b.GetComponentInChildren<Text>());
            }

            _resultPanel = ProceduralUiKit.CreatePanel(root.transform, "Result", new Vector2(.20f, .19f), new Vector2(.80f, .81f), new Color(.035f, .055f, .085f, .98f));
            _resultTitle = ProceduralUiKit.CreateText(_resultPanel.transform, "ResultTitle", "BATTLE REPORT", new Vector2(.06f, .84f), new Vector2(.94f, .97f), 30, new Color(1f, .82f, .4f), TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow);
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
            _resultLabel = ProceduralUiKit.CreateText(content.transform, "ResultBody", "", Vector2.zero, Vector2.one, 18, Color.white, TextAnchor.UpperLeft, HorizontalWrapMode.Overflow);
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
            _replayButton = ProceduralUiKit.CreateButton(_resultPanel.transform, "ReplayBtn", "REPLAY", new Vector2(.10f, .06f), new Vector2(.44f, .19f), Replay, new Color(.48f, .31f, .13f), 16, 3f, HorizontalWrapMode.Overflow);
            _nextStageButton = ProceduralUiKit.CreateButton(_resultPanel.transform, "NextStageBtn", "▶ NEXT STAGE", new Vector2(.36f, .06f), new Vector2(.64f, .19f), OnClickNextStage, new Color(.85f, .45f, .12f), 16, 3f, HorizontalWrapMode.Overflow);
            _nextStageButton.gameObject.SetActive(false);
            _lobbyButton = ProceduralUiKit.CreateButton(_resultPanel.transform, "LobbyBtn", "RETURN TO LOBBY", new Vector2(.56f, .06f), new Vector2(.90f, .19f), Retreat, new Color(.18f, .44f, .48f), 16, 3f, HorizontalWrapMode.Overflow);
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
            _unitViews.Clear();
            _unitHealPulseTimers.Clear();
            _allyBaseHitTimer = 0f;
            _enemyBaseHitTimer = 0f;
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
        private static string ShortName(string id) { return ProceduralUiKit.ResolveUnitName(id); }

        private static Image Bar(Transform parent, Vector2 min, Vector2 max, Color color)
        {
            GameObject back = ProceduralUiKit.CreatePanel(parent, "Bar", min, max, new Color(.12f, .16f, .19f));
            GameObject fill = ProceduralUiKit.CreatePanel(back.transform, "Fill", Vector2.zero, Vector2.one, color);
            return fill.GetComponent<Image>();
        }
        private static GameObject Make(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(.5f, 0); rt.sizeDelta = size; rt.anchoredPosition = pos; return go;
        }
        private static void SafeDestroy(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
