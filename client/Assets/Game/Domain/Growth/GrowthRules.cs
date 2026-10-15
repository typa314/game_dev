using System;
using System.Collections.Generic;
using Game.Domain.Conditions;
using Game.Domain.MasterData;
using Game.Domain.Player;

namespace Game.Domain.Growth
{
    /// <summary>計算後的戰鬥數值（已套用等級、覺醒、立繪階段加成）。</summary>
    public sealed class ComputedStats
    {
        public int Hp;
        public int Atk;
    }

    /// <summary>養成公式。設計見 design/06_character_growth_portrait.md。</summary>
    public sealed class GrowthRules
    {
        private readonly GrowthConfig _cfg;

        public GrowthRules(GrowthConfig cfg) { _cfg = cfg; }

        public int MaxAwaken { get { return _cfg.LevelCapByAwaken.Count - 1; } }

        public int LevelCap(int awaken)
        {
            return _cfg.LevelCapByAwaken[Math.Min(awaken, MaxAwaken)];
        }

        /// <summary>從 level 升到 level+1 需要的經驗。</summary>
        public long ExpToNext(string rarity, int level)
        {
            ExpCurve c = _cfg.ExpCurveByRarity[rarity];
            return (long)Math.Round(c.Base * Math.Pow(level, c.Exponent));
        }

        /// <summary>
        /// 增加經驗並自動升級。到達等級上限時多餘的經驗捨棄（Exp 歸零）。
        /// 回傳升了幾級。
        /// </summary>
        public int AddExp(CharacterState s, string rarity, long exp)
        {
            int cap = LevelCap(s.Awaken);
            int gained = 0;
            if (s.Level >= cap) { s.Exp = 0; return 0; }
            s.Exp += exp;
            while (s.Level < cap)
            {
                long need = ExpToNext(rarity, s.Level);
                if (s.Exp < need) break;
                s.Exp -= need;
                s.Level++;
                gained++;
            }
            if (s.Level >= cap) s.Exp = 0;
            return gained;
        }

        /// <summary>升到等級上限總共還需要多少經驗。</summary>
        public long ExpToCap(CharacterState s, string rarity)
        {
            int cap = LevelCap(s.Awaken);
            long total = 0;
            for (int lv = s.Level; lv < cap; lv++) total += ExpToNext(rarity, lv);
            return Math.Max(0, total - s.Exp);
        }

        public int AwakenCost(int currentAwaken)
        {
            return currentAwaken < _cfg.AwakenShardCost.Count ? _cfg.AwakenShardCost[currentAwaken] : int.MaxValue;
        }

        public string ShardItem(string charId) { return _cfg.ShardItemPrefix + charId; }

        public ComputedStats ComputeStats(CharacterDef def, CharacterState s, PortraitStageDef selectedStage)
        {
            int perLevel = 0;
            if (def.Battle.GrowthCurve != null) _cfg.StatCurvePerLevelPct.TryGetValue(def.Battle.GrowthCurve, out perLevel);
            long levelPct = 100 + (long)perLevel * (s.Level - 1);
            int awakenPct = s.Awaken < _cfg.AwakenStatBonusPct.Count ? _cfg.AwakenStatBonusPct[s.Awaken] : 0;
            int stageAtk = selectedStage != null && selectedStage.StatBonus != null ? selectedStage.StatBonus.AtkPct : 0;
            int stageHp = selectedStage != null && selectedStage.StatBonus != null ? selectedStage.StatBonus.HpPct : 0;
            return new ComputedStats
            {
                Hp = (int)(def.Battle.Hp * levelPct / 100 * (100 + awakenPct + stageHp) / 100),
                Atk = (int)(def.Battle.Atk * levelPct / 100 * (100 + awakenPct + stageAtk) / 100),
            };
        }

        /// <summary>好感等級 1..N。</summary>
        public int AffectionLevel(int affection)
        {
            int lv = 0;
            for (int i = 0; i < _cfg.AffectionThresholds.Count; i++)
                if (affection >= _cfg.AffectionThresholds[i]) lv = i + 1;
            return Math.Max(1, lv);
        }

        /// <summary>
        /// 檢查並解鎖符合條件的立繪階段（條件成立而且分級允許）。回傳這次新解鎖的階段 ID。
        /// 分級不允許的階段先不解鎖，等分級允許時（例如安裝 DLC）再呼叫一次就會補上。
        /// </summary>
        public List<string> RefreshPortraitUnlocks(string charId, CharacterState s, ConditionContext ctx)
        {
            var newly = new List<string>();
            string prevSelf = ctx.SelfChar;
            ctx.SelfChar = charId;
            try
            {
                foreach (PortraitStageDef p in ctx.Master.StagesOf(charId))
                {
                    if (s.UnlockedPortraitStages.Contains(p.Id)) continue;
                    if (!ctx.Rating.Allows(p.Rating)) continue;
                    if (!ConditionEvaluator.Evaluate(p.Unlock, ctx)) continue;
                    s.UnlockedPortraitStages.Add(p.Id);
                    newly.Add(p.Id);
                }
            }
            finally { ctx.SelfChar = prevSelf; }
            return newly;
        }
    }
}
