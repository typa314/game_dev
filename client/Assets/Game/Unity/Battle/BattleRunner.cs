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
    /// 表現層（BattleDebugView、之後的 BattleView）只讀取 Sim 的狀態與 TickEvents。
    /// TODO: 正式版改在 Battle 場景（additive 載入）中運作；TickEvents 轉給特效/音效。
    /// </summary>
    public sealed class BattleRunner : MonoBehaviour
    {
        private const int MaxStepsPerFrame = 240;   // 防止卡頓後一次追太多 tick

        [SerializeField] private float speed = 1f;

        private float _accumulator;
        private string _stageId;
        private ulong _seed;
        private List<string> _team;
        private bool _submitted;

        public BattleSimulator Sim { get; private set; }
        public bool Paused { get; set; }
        public float Speed { get { return speed; } set { speed = Mathf.Max(0f, value); } }
        public string StageId { get { return _stageId; } }
        public bool IsRunning { get { return Sim != null && Sim.Outcome == BattleOutcome.Ongoing; } }
        /// <summary>最近一場戰鬥的提交結果（null = 尚未結束或尚未提交）。</summary>
        public Result<BattleReward> LastReward { get; private set; }

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

            Sim = new BattleSimulator(setup.Value);
            _stageId = stageId;
            _seed = seed;
            // PrepareBattle 用的是當下的 Player.Team；複製一份，避免戰鬥中編隊被改動造成回放不一致
            _team = new List<string>(core.Player.Team);
            _accumulator = 0f;
            _submitted = false;
            Paused = false;
            LastReward = null;
            Debug.Log("[Battle] 開始 " + stageId + " seed=" + seed);
            return true;
        }

        public void Deploy(int slot) { if (IsRunning) Sim.Enqueue(CommandType.Deploy, slot); }
        public void UpgradeEnergy() { if (IsRunning) Sim.Enqueue(CommandType.UpgradeEnergy); }
        public void FireCannon() { if (IsRunning) Sim.Enqueue(CommandType.FireCannon); }

        /// <summary>放棄目前戰鬥（不提交）。</summary>
        public void Abort()
        {
            Sim = null;
            _stageId = null;
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
                    Sim.Step();
                    // TODO: 把 Sim.TickEvents 轉給 BattleView（每個 Step 都會換一個新的 List）。
                    _accumulator -= tickSec;
                    steps++;
                }
                if (steps >= MaxStepsPerFrame) _accumulator = 0f;
            }
            if (!IsRunning && !_submitted) Submit();
        }

        private void Submit()
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
