using System;
using System.Collections.Generic;
using System.Linq;
using Game.Domain.Conditions;

namespace Game.Domain.MasterData
{
    /// <summary>
    /// 主資料一致性檢查（交叉引用、條件語法、機率總和、分級規則）。
    /// 回傳錯誤清單；清單不是空的就禁止匯入或啟動（CI 閘門）。
    /// </summary>
    public static class MasterDataValidator
    {
        public static List<string> Validate(MasterDatabase m)
        {
            var err = new List<string>();
            var growth = m.Config.Growth;
            var known = new HashSet<string>(m.Characters.Keys);

            if (growth.LevelCapByAwaken.Count == 0) err.Add("config.growth.levelCapByAwaken is empty");
            if (growth.AwakenShardCost.Count < growth.LevelCapByAwaken.Count - 1)
                err.Add("config.growth.awakenShardCost must have levelCapByAwaken.Count-1 entries");
            if (growth.AffectionThresholds.Count == 0) err.Add("config.growth.affectionThresholds is empty");

            BattleRules br = m.Config.Battle;
            if (br.TicksPerSecond <= 0) err.Add("config.battle.ticksPerSecond must be > 0");
            if (br.EnergyMaxByLevel.Count == 0 || br.EnergyRegenPerSecByLevel.Count != br.EnergyMaxByLevel.Count)
                err.Add("config.battle energyMaxByLevel / energyRegenPerSecByLevel length mismatch");
            if (br.MaxBattleSec <= 0) err.Add("config.battle.maxBattleSec must be > 0");

            foreach (CharacterDef c in m.Characters.Values)
            {
                string at = "character " + c.Id;
                if (!Array.Exists(Rarity.Order, r => r == c.Rarity)) err.Add(at + ": unknown rarity " + c.Rarity);
                else if (!growth.ExpCurveByRarity.ContainsKey(c.Rarity)) err.Add(at + ": no exp curve for rarity " + c.Rarity);
                if (c.Battle == null) err.Add(at + ": battle stats missing");
                else if (c.Battle.GrowthCurve != null && !growth.StatCurvePerLevelPct.ContainsKey(c.Battle.GrowthCurve))
                    err.Add(at + ": unknown growthCurve " + c.Battle.GrowthCurve);
                if (c.PortraitStages.Count == 0) err.Add(at + ": needs at least one portrait stage");
                foreach (string ps in c.PortraitStages)
                {
                    PortraitStageDef p = m.PortraitStages.GetOrNull(ps);
                    if (p == null) err.Add(at + ": portrait stage " + ps + " not found");
                    else if (p.Character != c.Id) err.Add(at + ": portrait stage " + ps + " belongs to " + p.Character);
                }
                // 角色只能從轉蛋取得（或作為初始角色）；巧遇不招募角色
                foreach (string via in c.ObtainableBy)
                    if (via != "gacha" && via != "initial") err.Add(at + ": obtainableBy '" + via + "' is not allowed (gacha / initial only)");
                if (c.DefaultCostume != null && !m.Costumes.ContainsKey(c.DefaultCostume))
                    err.Add(at + ": defaultCostume " + c.DefaultCostume + " not found");
            }

            foreach (PortraitStageDef p in m.PortraitStages.Values)
            {
                string at = "portraitStage " + p.Id;
                if (!known.Contains(p.Character)) err.Add(at + ": character " + p.Character + " not found");
                // 立繪階段和戰力綁定，不可以是 R18（親密內容放在劇情 CG），見 design/06 §2
                if (p.Rating == Rating.R18) err.Add(at + ": portrait stages must not be R18");
                CheckCondition(p.Unlock, at, m, err);
            }
            foreach (CharacterDef c in m.Characters.Values)
            {
                // 第一階段必須不設條件、而且是 G，取得角色時才一定有可顯示的立繪
                List<PortraitStageDef> stages = m.StagesOf(c.Id);
                if (stages.Count > 0 && (stages[0].Unlock != null || stages[0].Rating != Rating.G))
                    err.Add("character " + c.Id + ": first portrait stage must have no unlock condition and rating G");
            }

            foreach (CostumeDef c in m.Costumes.Values)
            {
                string at = "costume " + c.Id;
                if (!known.Contains(c.Character)) err.Add(at + ": character " + c.Character + " not found");
                if (c.RequiresPortraitStage != null && !m.PortraitStages.ContainsKey(c.RequiresPortraitStage))
                    err.Add(at + ": requiresPortraitStage " + c.RequiresPortraitStage + " not found");
                CheckCondition(c.Unlock, at, m, err);
            }

            foreach (SceneDef s in m.Scenes.Values) CheckCondition(s.Unlock, "scene " + s.Id, m, err);
            foreach (SpotDef s in m.Spots.Values) CheckCondition(s.Unlock, "spot " + s.Id, m, err);

            foreach (StageDef s in m.Stages.Values)
            {
                string at = "stage " + s.Id;
                if (s.Length <= 0) err.Add(at + ": length must be > 0");
                if (s.EnemyBaseHp <= 0) err.Add(at + ": enemyBaseHp must be > 0");
                foreach (WaveDef w in s.Waves)
                {
                    if (!m.Enemies.ContainsKey(w.Enemy)) err.Add(at + ": enemy " + w.Enemy + " not found");
                    if (w.Count <= 0) err.Add(at + ": wave count must be > 0");
                    if (w.Trigger != null && w.Trigger.Type != "enemy_base_hp_below_pct")
                        err.Add(at + ": unsupported wave trigger " + w.Trigger.Type);
                }
                foreach (string u in s.UnlocksOnClear)
                    if (!m.Stages.ContainsKey(u) && !m.Spots.ContainsKey(u)) err.Add(at + ": unlocksOnClear " + u + " is neither stage nor spot");
                foreach (string sc in s.StarConditions)
                    if (!IsKnownStarCondition(sc)) err.Add(at + ": unknown star condition " + sc);
            }

            foreach (BannerDef b in m.Banners.Values)
            {
                string at = "banner " + b.Id;
                double sum = b.Rates.Values.Sum();
                if (Math.Abs(sum - 1.0) > 1e-9) err.Add(at + ": rates sum to " + sum + " (must be 1)");
                foreach (KeyValuePair<string, double> kv in b.Rates)
                {
                    List<string> pool;
                    if (kv.Value > 0 && (!b.Pool.TryGetValue(kv.Key, out pool) || pool.Count == 0))
                        err.Add(at + ": rarity " + kv.Key + " has rate but empty pool");
                }
                foreach (KeyValuePair<string, List<string>> kv in b.Pool)
                    foreach (string ch in kv.Value)
                    {
                        CharacterDef c = m.Characters.GetOrNull(ch);
                        if (c == null) err.Add(at + ": pool character " + ch + " not found");
                        else if (!b.AllowCrossRarity && c.Rarity != kv.Key) err.Add(at + ": " + ch + " is " + c.Rarity + " but listed under " + kv.Key);
                    }
                if (b.Pity != null && !b.Rates.ContainsKey(b.Pity.Rarity)) err.Add(at + ": pity rarity " + b.Pity.Rarity + " has no rate");
                if (b.Pickup != null)
                    foreach (string ch in b.Pickup.Chars)
                        if (!b.Pool.Values.Any(p => p.Contains(ch))) err.Add(at + ": pickup " + ch + " not in pool");
                if (b.Cost.Single.Count == 0) err.Add(at + ": single cost missing");
            }

            foreach (EncounterEventDef e in m.Encounters.Values)
            {
                string at = "encounter " + e.Id;
                if (!m.Spots.ContainsKey(e.Spot)) err.Add(at + ": spot " + e.Spot + " not found");
                if (e.Character != null && !known.Contains(e.Character)) err.Add(at + ": character " + e.Character + " not found");
                if (e.Outcomes.Count == 0) err.Add(at + ": needs at least one outcome");
                foreach (List<Outcome> list in e.Outcomes.Values)
                    foreach (Outcome o in list)
                    {
                        if (o.AffectionChar != null && !known.Contains(o.AffectionChar)) err.Add(at + ": affection char " + o.AffectionChar + " not found");
                        if (o.Costume != null && !m.Costumes.ContainsKey(o.Costume)) err.Add(at + ": costume " + o.Costume + " not found");
                    }
                CheckCondition(e.Conditions, at, m, err);
            }
            foreach (IGrouping<string, EncounterEventDef> g in m.Encounters.Values.Where(e => e.Series != null).GroupBy(e => e.Series))
            {
                List<int> orders = g.Select(e => e.Order).OrderBy(x => x).ToList();
                for (int i = 0; i < orders.Count; i++)
                    if (orders[i] != i + 1) { err.Add("encounter series " + g.Key + ": orders must be 1..N without gaps"); break; }
            }

            foreach (AchievementDef a in m.Achievements.Values)
            {
                string at = "achievement " + a.Id;
                if (a.Trigger == null) { err.Add(at + ": trigger missing"); continue; }
                string ct = a.Trigger.Counter;
                if (ct != "sum" && ct != "count" && ct != "distinct" && ct != "state") err.Add(at + ": unknown counter " + ct);
                if (ct == "state") CheckCondition(a.Trigger.Condition, at, m, err);
                else if (string.IsNullOrEmpty(a.Trigger.Event)) err.Add(at + ": event required for counter " + ct);
                if ((ct == "sum" || ct == "distinct") && string.IsNullOrEmpty(a.Trigger.Field)) err.Add(at + ": field required for counter " + ct);
                // Steam 成就清單是公開的，R18 成就不能對應 Steam（design/13 §3）
                if (a.Rating == Rating.R18 && a.SteamApiName != null) err.Add(at + ": R18 achievement must not have steamApiName");
            }

            InitialState init = m.Config.Initial;
            foreach (string ch in init.Characters) if (!known.Contains(ch)) err.Add("config.initial: character " + ch + " not found");
            foreach (string st in init.UnlockedStages) if (!m.Stages.ContainsKey(st)) err.Add("config.initial: stage " + st + " not found");
            foreach (string sp in init.UnlockedSpots) if (!m.Spots.ContainsKey(sp)) err.Add("config.initial: spot " + sp + " not found");
            foreach (string sc in init.Scenes) if (!m.Scenes.ContainsKey(sc)) err.Add("config.initial: scene " + sc + " not found");

            return err;
        }

        public static bool IsKnownStarCondition(string sc)
        {
            return sc == "clear" || sc.StartsWith("no_base_damage_pct_", StringComparison.Ordinal) || sc.StartsWith("within_sec_", StringComparison.Ordinal);
        }

        private static void CheckCondition(Condition c, string at, MasterDatabase m, List<string> err)
        {
            if (c == null) return;
            if (!ConditionEvaluator.KnownTypes.Contains(c.Type)) { err.Add(at + ": unknown condition type " + c.Type); return; }
            switch (c.Type)
            {
                case "all":
                case "any":
                    if (c.Of == null || c.Of.Count == 0) err.Add(at + ": '" + c.Type + "' needs 'of'");
                    else foreach (Condition x in c.Of) CheckCondition(x, at, m, err);
                    break;
                case "not":
                    if (c.Cond == null) err.Add(at + ": 'not' needs 'cond'");
                    else CheckCondition(c.Cond, at, m, err);
                    break;
                case "char_owned":
                case "char_level_gte":
                case "char_awaken_gte":
                case "affection_gte":
                    if (c.Char != "self" && !m.Characters.ContainsKey(c.Char ?? "")) err.Add(at + ": condition char " + c.Char + " not found");
                    break;
                case "stage_cleared":
                    if (!m.Stages.ContainsKey(c.Stage ?? "")) err.Add(at + ": condition stage " + c.Stage + " not found");
                    break;
                case "achievement":
                    if (!m.Achievements.ContainsKey(c.Id ?? "")) err.Add(at + ": condition achievement " + c.Id + " not found");
                    break;
                case "story_flag":
                    if (string.IsNullOrEmpty(c.Key)) err.Add(at + ": condition 'story_flag' needs 'key'");
                    break;
                case "story_node":
                    if (string.IsNullOrEmpty(c.Node)) err.Add(at + ": condition 'story_node' needs 'node'");
                    break;
                case "ending_reached":
                    if (string.IsNullOrEmpty(c.Ending)) err.Add(at + ": condition 'ending_reached' needs 'ending'");
                    break;
                case "time_of_day":
                    if (c.Value != "morning" && c.Value != "day" && c.Value != "evening" && c.Value != "night")
                        err.Add(at + ": condition 'time_of_day' invalid value " + c.Value);
                    break;
                case "weekday":
                    if (string.IsNullOrEmpty(c.Value) || !Enum.TryParse<DayOfWeek>(c.Value, true, out _))
                        err.Add(at + ": condition 'weekday' invalid value " + c.Value);
                    break;
                case "rating_allowed":
                    string rStr = c.Rating ?? c.Value;
                    if (string.IsNullOrEmpty(rStr) || !Enum.TryParse<Rating>(rStr, true, out _))
                        err.Add(at + ": condition 'rating_allowed' invalid rating " + rStr);
                    break;
            }
        }
    }
}
