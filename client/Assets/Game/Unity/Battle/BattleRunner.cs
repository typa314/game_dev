using System;
using System.Collections.Generic;
using Game.Core;
using Game.Domain;
using Game.Domain.Battle;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 即時戰鬥驅動（design/05）：以固定 tick（BattleSimulator.TicksPerSecond）累積 Time.deltaTime 推進模擬，
    /// 支援倍速與暫停；結束時組成 BattleReport 交給 GameCore.SubmitBattle（由 GameCore 重新模擬後發放獎勵）。
    /// 表現層（BattlePageView、之後的正式 BattleView）讀取 Sim 的狀態並從 ConsumeViewEvents 取出 TickEvents。
    /// </summary>
    public sealed class BattleRunner : MonoBehaviour
    {
        private const int MaxStepsPerFrame = 240;   // 防止卡頓後一次追太多 tick
        private const int MaxPendingEvents = 256;   // 視圖事件佇列上限，防止無限增長

        [SerializeField] private float speed = 1f;

        private float _accumulator;
        private string _stageId;
        private ulong _seed;
        private List<string> _team;
        private bool _submitted;
        private readonly List<BattleEvent> _pendingViewEvents = new List<BattleEvent>();

        public BattleSimulator Sim { get; private set; }
        public BattleSetup Setup { get; private set; }
        public IReadOnlyList<string> TeamSnapshot { get { return _team; } }
        public bool Paused { get; set; }
        public float Speed { get { return speed; } set { speed = Mathf.Max(0f, value); } }
        public string StageId { get { return _stageId; } }
        public bool IsRunning { get { return Sim != null && Sim.Outcome == BattleOutcome.Ongoing; } }
        public bool HasSubmitted { get { return _submitted; } }
        public int PendingViewEventCount { get { return _pendingViewEvents.Count; } }
        /// <summary>最近一場戰鬥的提交結果（null = 尚未結束或尚未提交）。</summary>
        public Result<BattleReward> LastReward { get; private set; }

        private void Awake()
        {
            if (GetComponent<BattlePageView>() == null) gameObject.AddComponent<BattlePageView>();
        }

        /// <summary>開始一場戰鬥。成功回傳 true；失敗原因寫在 log。</summary>
        public bool StartBattle(string stageId)
        {
            GameCore core = GameServices.Core;
            if (core == null) { Debug.LogWarning("[Battle] GameServices 尚未就緒"); return false; }

            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            Result<BattleSetup> setup = core.PrepareBattle(stageId, seed);
            if (!setup.Ok)
            {
                Debug.LogWarning("[Battle] 無法開始 " + stageId + "：" + setup.Error);
                return false;
            }

            Setup = setup.Value;
            Sim = new BattleSimulator(setup.Value);
            _stageId = stageId;
            _seed = seed;
            // PrepareBattle 用的是當下的 Player.Team；複製一份，避免戰鬥中編隊被改動造成回放不一致
            _team = new List<string>(core.Player.Team);
            _accumulator = 0f;
            _submitted = false;
            Paused = false;
            LastReward = null;
            _pendingViewEvents.Clear();
            Debug.Log("[Battle] 開始 " + stageId + " seed=" + seed);
            return true;
        }

        /// <summary>測試或手動初始化 BattleRunner（無需走 GameServices）。</summary>
        public void InitializeForTesting(BattleSimulator sim, BattleSetup setup = null, List<string> team = null)
        {
            Sim = sim;
            Setup = setup;
            if (team != null)
            {
                _team = new List<string>(team);
            }
            else if (setup != null && setup.Team != null)
            {
                _team = new List<string>();
                for (int i = 0; i < setup.Team.Count; i++)
                {
                    _team.Add(setup.Team[i] != null ? setup.Team[i].Id : null);
                }
            }
            else
            {
                _team = new List<string>();
            }

            _stageId = setup != null && setup.Stage != null ? setup.Stage.Id : "test_stage";
            _seed = setup != null ? setup.Seed : 0;
            _accumulator = 0f;
            _submitted = false;
            Paused = false;
            LastReward = null;
            _pendingViewEvents.Clear();
        }

        public void Deploy(int slot) { if (IsRunning) Sim.Enqueue(CommandType.Deploy, slot); }
        public void TapSlot(int slot) { if (IsRunning) Sim.TapSlot(slot); }
        public void CastSkill(int slot) { if (IsRunning) Sim.Enqueue(CommandType.CastSkill, slot); }
        public void UpgradeEnergy() { if (IsRunning) Sim.Enqueue(CommandType.UpgradeEnergy); }
        public void FireCannon() { if (IsRunning) Sim.Enqueue(CommandType.FireCannon); }

        /// <summary>放棄目前戰鬥（不提交）。</summary>
        public void Abort()
        {
            Sim = null;
            Setup = null;
            _stageId = null;
            _pendingViewEvents.Clear();
        }

        /// <summary>手動單步推進模擬並記錄視圖事件（供測試或外部精確控制）。</summary>
        public void StepSim()
        {
            if (Sim == null) return;
            Sim.Step();
            CaptureTickEvents();
        }

        /// <summary>將當前 tick 產生的 TickEvents 累積至視圖緩衝佇列中（上限 256 筆）。</summary>
        public void CaptureTickEvents()
        {
            if (Sim == null || Sim.TickEvents == null || Sim.TickEvents.Count == 0) return;
            _pendingViewEvents.AddRange(Sim.TickEvents);
            if (_pendingViewEvents.Count > MaxPendingEvents)
            {
                int excess = _pendingViewEvents.Count - MaxPendingEvents;
                _pendingViewEvents.RemoveRange(0, excess);
            }
        }

        /// <summary>將累積的視圖事件複製給呼叫端 buffer，並清空內部暫存。</summary>
        public void ConsumeViewEvents(List<BattleEvent> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            buffer.AddRange(_pendingViewEvents);
            _pendingViewEvents.Clear();
        }

        /// <summary>取得指定 Slot 技能名稱。</summary>
        public string GetSkillName(int slot)
        {
            if (Setup != null && Setup.Team != null && slot >= 0 && slot < Setup.Team.Count)
            {
                UnitTemplate t = Setup.Team[slot];
                if (t != null && t.Skill != null && !string.IsNullOrEmpty(t.Skill.Name))
                    return t.Skill.Name;
            }
            GameCore core = GameServices.Core;
            if (core != null && core.Master != null && _team != null && slot >= 0 && slot < _team.Count)
            {
                string charId = _team[slot];
                if (!string.IsNullOrEmpty(charId) && core.Master.Characters.TryGetValue(charId, out var charDef))
                {
                    if (charDef.Battle != null && charDef.Battle.Skill != null && !string.IsNullOrEmpty(charDef.Battle.Skill.Name))
                    {
                        return charDef.Battle.Skill.Name;
                    }
                }
            }
            return "BURST";
        }

        private void Update()
        {
            if (Sim == null) return;
            if (IsRunning && !Paused)
            {
                float tickSec = 1f / Sim.TicksPerSecond;
                _accumulator += Time.deltaTime * speed;
                int steps = 0;
                while (_accumulator >= tickSec && Sim.Outcome == BattleOutcome.Ongoing && steps < MaxStepsPerFrame)
                {
                    StepSim();
                    _accumulator -= tickSec;
                    steps++;
                }
                if (steps >= MaxStepsPerFrame) _accumulator = 0f;
            }
            if (!IsRunning && !_submitted) Submit();
        }

        public void Submit()
        {
            _submitted = true;
            GameCore core = GameServices.Core;
            if (core == null) return;

            var report = new BattleReport
            {
                StageId = _stageId,
                Seed = _seed,
                Team = _team,
                Commands = new List<BattleCommand>(Sim.Recorded),
                MasterDataVersion = core.Master.Config.MasterDataVersion,
            };
            LastReward = core.SubmitBattle(report);
            if (!LastReward.Ok)
            {
                Debug.LogWarning("[Battle] SubmitBattle 失敗：" + LastReward.Error);
                return;
            }

            BattleReward r = LastReward.Value;
            var items = new List<string>();
            foreach (ItemStack it in r.Items) items.Add(it.Item + "x" + it.N);
            Debug.Log("[Battle] 結束 " + _stageId + " 即時=" + Sim.Outcome + " 驗證=" + r.Result.Outcome
                      + " stars=" + r.Stars + " firstClear=" + r.FirstClear
                      + " items=[" + string.Join(", ", items) + "] unlocked=[" + string.Join(", ", r.Unlocked) + "]");
            if (r.Result.Outcome != Sim.Outcome)
                Debug.LogWarning("[Battle] 即時模擬與重新模擬結果不一致（決定性問題）");
        }
    }
}
