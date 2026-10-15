using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Domain.Conditions;
using Game.Domain.MasterData;
using Game.Domain.Player;

namespace Game.Domain.Encounter
{
    /// <summary>
    /// 巧遇事件的挑選與體力計算。規則見 design/07_gacha_and_encounter.md §2.3。
    /// 巧遇不招募角色：指定角色的事件只會在已持有該角色時出現。
    /// </summary>
    public static class EncounterEngine
    {
        /// <summary>
        /// 依「地點、條件、分級、系列順序」篩選候選事件，再加權抽一個。
        /// 回傳 null 代表沒有任何事件可以觸發（由呼叫端給予 FallbackReward）。
        /// 會更新 MissStreak（巧遇保底計數）。
        /// </summary>
        public static EncounterEventDef Select(string spot, ConditionContext ctx, IRandom rng)
        {
            EncounterConfig cfg = ctx.Master.Config.Encounter;
            EncounterState st = ctx.Player.Encounter;
            List<EncounterEventDef> candidates = Candidates(spot, ctx);
            List<EncounterEventDef> series = candidates.Where(e => e.Series != null).ToList();

            List<EncounterEventDef> pickFrom = candidates;
            if (cfg.PityThreshold > 0 && st.MissStreak >= cfg.PityThreshold && series.Count > 0)
                pickFrom = series;

            EncounterEventDef chosen = WeightedPick(pickFrom, ctx, rng);
            if (chosen != null && chosen.Series != null) st.MissStreak = 0;
            else st.MissStreak++;
            return chosen;
        }

        public static List<EncounterEventDef> Candidates(string spot, ConditionContext ctx)
        {
            EncounterState st = ctx.Player.Encounter;
            var list = new List<EncounterEventDef>();
            foreach (EncounterEventDef e in ctx.Master.Encounters.Values)
            {
                if (e.Spot != spot) continue;
                if (!e.Repeatable && st.Seen.Contains(e.Id)) continue;
                if (!ctx.Rating.Allows(e.Rating)) continue;
                if (e.Character != null && ctx.Player.Char(e.Character) == null) continue;
                if (e.Series != null && e.Order != NextOrderInSeries(e.Series, ctx)) continue;
                string prevSelf = ctx.SelfChar;
                ctx.SelfChar = e.Character;
                bool ok;
                try { ok = ConditionEvaluator.Evaluate(e.Conditions, ctx); }
                finally { ctx.SelfChar = prevSelf; }
                if (ok) list.Add(e);
            }
            // 依 ID 排序，讓同一個 seed 的結果不受 Dictionary 列舉順序影響
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        /// <summary>系列中下一個應該出現的 order（已看過的數量 + 1）。</summary>
        public static int NextOrderInSeries(string series, ConditionContext ctx)
        {
            int seen = 0;
            foreach (EncounterEventDef e in ctx.Master.Encounters.Values)
                if (e.Series == series && ctx.Player.Encounter.Seen.Contains(e.Id)) seen++;
            return seen + 1;
        }

        private static EncounterEventDef WeightedPick(List<EncounterEventDef> list, ConditionContext ctx, IRandom rng)
        {
            if (list.Count == 0) return null;
            var weights = new List<int>(list.Count);
            int total = 0;
            foreach (EncounterEventDef e in list)
            {
                int w = Math.Max(0, e.Weight);
                weights.Add(w);
                total += w;
            }
            if (total == 0) return null;
            int r = rng.NextInt(total);
            for (int i = 0; i < list.Count; i++)
            {
                if (r < weights[i]) return list[i];
                r -= weights[i];
            }
            return list[list.Count - 1];
        }

        /// <summary>依經過時間回復體力（現實時間；單機版用本機時鐘）。</summary>
        public static void RegenStamina(EncounterState st, EncounterConfig cfg, DateTime utcNow)
        {
            if (st.StaminaUpdatedAtUtc == null || st.Stamina >= cfg.StaminaMax || cfg.StaminaRegenMinutes <= 0)
            {
                st.StaminaUpdatedAtUtc = utcNow.ToString("o", CultureInfo.InvariantCulture);
                return;
            }
            DateTime last = DateTime.Parse(st.StaminaUpdatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (utcNow < last)
            {
                // 系統時間被調回過去：不回復，並把基準點重設到現在
                st.StaminaUpdatedAtUtc = utcNow.ToString("o", CultureInfo.InvariantCulture);
                return;
            }
            long periods = (long)((utcNow - last).TotalMinutes / cfg.StaminaRegenMinutes);
            if (periods <= 0) return;
            long newStamina = Math.Min(cfg.StaminaMax, st.Stamina + periods);
            st.Stamina = (int)newStamina;
            st.StaminaUpdatedAtUtc = st.Stamina >= cfg.StaminaMax
                ? utcNow.ToString("o", CultureInfo.InvariantCulture)
                : last.AddMinutes(periods * cfg.StaminaRegenMinutes).ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
