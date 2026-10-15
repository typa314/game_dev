using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Domain.MasterData;
using Game.Domain.Player;

namespace Game.Domain.Conditions
{
    /// <summary>評估條件時需要的上下文。</summary>
    public sealed class ConditionContext
    {
        public PlayerState Player;
        public MasterDatabase Master;
        public RatingPolicy Rating;
        public IClock Clock;
        /// <summary>Char = "self" 時代表的角色。</summary>
        public string SelfChar;
        /// <summary>劇情存檔欄位的變數（選填）；找不到時再查全域旗標。</summary>
        public Func<string, double?> StoryVariable;
        /// <summary>戰鬥中的敵方據點 HP 百分比（只有 wave trigger 會用到）。</summary>
        public int? EnemyBaseHpPct;
    }

    /// <summary>通用條件語言的評估器，語法見 design/04_data_model.md §4。</summary>
    public static class ConditionEvaluator
    {
        public static readonly HashSet<string> KnownTypes = new HashSet<string>
        {
            "all", "any", "not", "char_owned", "char_level_gte", "char_awaken_gte", "affection_gte",
            "stage_cleared", "story_flag", "story_node", "ending_reached", "achievement",
            "time_of_day", "weekday", "rating_allowed", "enemy_base_hp_below_pct",
        };

        /// <summary>null 條件視為成立。未知的 type 丟出例外（主資料驗證器會事先擋下）。</summary>
        public static bool Evaluate(Condition c, ConditionContext ctx)
        {
            if (c == null) return true;
            switch (c.Type)
            {
                case "all":
                    if (c.Of != null) foreach (Condition x in c.Of) if (!Evaluate(x, ctx)) return false;
                    return true;
                case "any":
                    if (c.Of != null) foreach (Condition x in c.Of) if (Evaluate(x, ctx)) return true;
                    return false;
                case "not":
                    return !Evaluate(c.Cond, ctx);
                case "char_owned":
                    return CharState(c, ctx) != null;
                case "char_level_gte":
                {
                    CharacterState s = CharState(c, ctx);
                    return s != null && s.Level >= Num(c.Value);
                }
                case "char_awaken_gte":
                {
                    CharacterState s = CharState(c, ctx);
                    return s != null && s.Awaken >= Num(c.Value);
                }
                case "affection_gte":
                {
                    CharacterState s = CharState(c, ctx);
                    return s != null && s.Affection >= Num(c.Value);
                }
                case "stage_cleared":
                {
                    if (string.IsNullOrEmpty(c.Stage)) return false;
                    StageProgress p;
                    return ctx.Player.Stages.TryGetValue(c.Stage, out p) && p.Cleared;
                }
                case "story_flag":
                    if (string.IsNullOrEmpty(c.Key)) return false;
                    return Compare(StoryValue(c.Key, ctx), c.Op, Num(c.Value));
                case "story_node":
                    if (string.IsNullOrEmpty(c.Node)) return false;
                    return ctx.Player.Story.ReadNodes.Contains(c.Node);
                case "ending_reached":
                    if (string.IsNullOrEmpty(c.Ending)) return false;
                    return ctx.Player.Endings.ContainsKey(c.Ending);
                case "achievement":
                {
                    if (string.IsNullOrEmpty(c.Id)) return false;
                    AchievementProgress a;
                    return ctx.Player.Achievements.TryGetValue(c.Id, out a) && a.UnlockedAtUtc != null;
                }
                case "time_of_day":
                    return c.Value != null && TimeOfDay(ctx.Clock.LocalNow.Hour) == c.Value;
                case "weekday":
                    return c.Value != null && string.Equals(ctx.Clock.LocalNow.DayOfWeek.ToString(), c.Value, StringComparison.OrdinalIgnoreCase);
                case "rating_allowed":
                {
                    string rStr = c.Rating ?? c.Value;
                    if (string.IsNullOrEmpty(rStr)) return false;
                    if (!Enum.TryParse(rStr, true, out Rating r)) return false;
                    return ctx.Rating.Allows(r);
                }
                case "enemy_base_hp_below_pct":
                    return ctx.EnemyBaseHpPct.HasValue && ctx.EnemyBaseHpPct.Value < Num(c.Value);
                default:
                    throw new InvalidOperationException("unknown condition type: " + c.Type);
            }
        }

        /// <summary>morning 5–10、day 11–16、evening 17–19、night 20–4（裝置當地時間的小時）。</summary>
        public static string TimeOfDay(int hour)
        {
            if (hour >= 5 && hour < 11) return "morning";
            if (hour >= 11 && hour < 17) return "day";
            if (hour >= 17 && hour < 20) return "evening";
            return "night";
        }

        private static CharacterState CharState(Condition c, ConditionContext ctx)
        {
            string id = c.Char == "self" ? ctx.SelfChar : c.Char;
            return ctx.Player.Char(id);
        }

        private static double StoryValue(string key, ConditionContext ctx)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            if (ctx.StoryVariable != null)
            {
                double? v = ctx.StoryVariable(key);
                if (v.HasValue) return v.Value;
            }
            double g;
            return ctx.Player.Story.Flags.TryGetValue(key, out g) ? g : 0;
        }

        private static double Num(string s)
        {
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool Compare(double a, string op, double b)
        {
            switch (op ?? ">=")
            {
                case ">=": return a >= b;
                case ">": return a > b;
                case "<=": return a <= b;
                case "<": return a < b;
                case "==": return a == b;
                case "!=": return a != b;
                default: throw new InvalidOperationException("unknown op: " + op);
            }
        }
    }
}
