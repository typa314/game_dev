using Game.Domain.MasterData;
using Game.Domain.Player;

namespace Game.Domain.Portrait
{
    /// <summary>決定角色「現在要顯示哪一套造型」的結果。</summary>
    public sealed class ResolvedPortrait
    {
        public string Character;
        public string PortraitStage;     // 目前選用的立繪階段
        public string Costume;           // null = 沒有套用服裝
        public PortraitArt Art;          // 可能為 null（主資料還沒填美術）→ 呈現層顯示 placeholder
        public Rating Rating;
    }

    /// <summary>
    /// 造型決定規則（design/10_costume_and_scene.md §1）：
    /// 劇情強制服裝 → 玩家選用的服裝 → 立繪階段的預設造型；任何一層分級不允許就往下一層退。
    /// </summary>
    public static class PortraitResolver
    {
        public static ResolvedPortrait Resolve(MasterDatabase master, PlayerState player, RatingPolicy rating,
            string charId, string forcedCostume = null)
        {
            CharacterState s = player.Char(charId);
            PortraitStageDef stage = SelectedStage(master, s, charId, rating);

            CostumeDef costume = TryCostume(master, player, forcedCostume, charId, rating, s, true)
                                 ?? (s != null ? TryCostume(master, player, s.Costume, charId, rating, s, false) : null);

            var r = new ResolvedPortrait { Character = charId, PortraitStage = stage != null ? stage.Id : null };
            if (costume != null)
            {
                r.Costume = costume.Id;
                r.Art = costume.Art;
                r.Rating = costume.Rating;
            }
            else if (stage != null)
            {
                r.Art = stage.Art;
                r.Rating = stage.Rating;
            }
            return r;
        }

        private static PortraitStageDef SelectedStage(MasterDatabase master, CharacterState s, string charId, RatingPolicy rating)
        {
            PortraitStageDef selected = s != null ? master.PortraitStages.GetOrNull(s.PortraitStage) : null;
            if (selected != null && rating.Allows(selected.Rating) && s.UnlockedPortraitStages.Contains(selected.Id)) return selected;
            // 退回：已解鎖且分級允許的最低階段；還沒持有角色（例如轉蛋預覽）就用第一階段
            foreach (PortraitStageDef p in master.StagesOf(charId))
            {
                if (!rating.Allows(p.Rating)) continue;
                if (s == null || s.UnlockedPortraitStages.Contains(p.Id)) return p;
            }
            return null;
        }

        private static CostumeDef TryCostume(MasterDatabase master, PlayerState player, string id, string charId,
            RatingPolicy rating, CharacterState s, bool forced)
        {
            CostumeDef c = master.Costumes.GetOrNull(id);
            if (c == null || c.Character != charId || !rating.Allows(c.Rating)) return null;
            if (forced) return c; // 劇情強制：不檢查持有和立繪階段需求
            if (!player.Costumes.Contains(c.Id)) return null;
            if (c.RequiresPortraitStage != null && (s == null || !s.UnlockedPortraitStages.Contains(c.RequiresPortraitStage))) return null;
            return c;
        }
    }
}
