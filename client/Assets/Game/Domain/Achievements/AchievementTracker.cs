using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Domain.Conditions;
using Game.Domain.Events;
using Game.Domain.MasterData;
using Game.Domain.Player;

namespace Game.Domain.Achievements
{
    /// <summary>
    /// 訂閱領域事件、更新成就進度。其他系統完全不知道成就的存在。設計見 design/13_achievements.md。
    /// </summary>
    public sealed class AchievementTracker
    {
        private readonly MasterDatabase _master;
        private readonly ConditionContext _ctx;
        private readonly EventBus _bus;
        private readonly IClock _clock;

        public AchievementTracker(MasterDatabase master, ConditionContext ctx, EventBus bus, IClock clock)
        {
            _master = master;
            _ctx = ctx;
            _bus = bus;
            _clock = clock;
        }

        public void Attach() { _bus.Subscribe(OnEvent); }

        public void Detach() { _bus.Unsubscribe(OnEvent); }

        private PlayerState Player { get { return _ctx.Player; } }

        public void OnEvent(DomainEvent e)
        {
            foreach (AchievementDef a in _master.Achievements.Values)
            {
                if (a.Trigger == null) continue;
                AchievementProgress p = Progress(a.Id);
                if (p.UnlockedAtUtc != null) continue;
                if (a.Trigger.Counter == "state")
                {
                    // state 類型：在指定事件（未指定則任何事件）之後重新判斷條件
                    if (a.Trigger.Event != null && a.Trigger.Event != e.Name) continue;
                    if (ConditionEvaluator.Evaluate(a.Trigger.Condition, _ctx)) Unlock(a, p);
                    continue;
                }
                if (a.Trigger.Event != e.Name || !MatchesFilter(a.Trigger.Filter, e)) continue;

                switch (a.Trigger.Counter)
                {
                    case "count":
                        p.Progress++;
                        break;
                    case "sum":
                    {
                        long v;
                        if (long.TryParse(e.Get(a.Trigger.Field), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) p.Progress += v;
                        break;
                    }
                    case "distinct":
                    {
                        string v = e.Get(a.Trigger.Field);
                        if (v == null) break;
                        if (p.Distinct == null) p.Distinct = new HashSet<string>();
                        p.Distinct.Add(v);
                        p.Progress = p.Distinct.Count;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("unknown counter: " + a.Trigger.Counter);
                }
                if (p.Progress >= a.Trigger.Target) Unlock(a, p);
            }
        }

        /// <summary>
        /// 載入存檔後呼叫：重新判斷所有 state 類型的成就，讓新加入的成就也能追溯解鎖。
        /// （distinct 類型需要歷史資料，只能從之後的事件開始累計。）
        /// </summary>
        public void Reevaluate()
        {
            foreach (AchievementDef a in _master.Achievements.Values)
            {
                if (a.Trigger == null || a.Trigger.Counter != "state") continue;
                AchievementProgress p = Progress(a.Id);
                if (p.UnlockedAtUtc == null && ConditionEvaluator.Evaluate(a.Trigger.Condition, _ctx)) Unlock(a, p);
            }
        }

        private AchievementProgress Progress(string id)
        {
            AchievementProgress p;
            if (!Player.Achievements.TryGetValue(id, out p))
            {
                p = new AchievementProgress();
                Player.Achievements[id] = p;
            }
            return p;
        }

        private void Unlock(AchievementDef a, AchievementProgress p)
        {
            // 分級不允許的成就先不解鎖（進度保留），之後分級允許時由 Reevaluate 或下一次事件補上
            if (!_ctx.Rating.Allows(a.Rating)) return;
            p.UnlockedAtUtc = _clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _bus.Publish(new DomainEvent(Ev.AchievementUnlocked).With("achievement", a.Id));
        }

        /// <summary>filter 的值支援結尾 '*' 前綴比對，例如 "st_3_*"。</summary>
        public static bool MatchesFilter(Dictionary<string, string> filter, DomainEvent e)
        {
            if (filter == null) return true;
            foreach (KeyValuePair<string, string> kv in filter)
            {
                string v = e.Get(kv.Key);
                if (v == null) return false;
                if (kv.Value.EndsWith("*", StringComparison.Ordinal))
                {
                    if (!v.StartsWith(kv.Value.Substring(0, kv.Value.Length - 1), StringComparison.Ordinal)) return false;
                }
                else if (v != kv.Value) return false;
            }
            return true;
        }
    }
}
