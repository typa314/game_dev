using System.Collections.Generic;
using System.Linq;

namespace Game.Domain.MasterData
{
    /// <summary>所有主資料的唯讀索引。由 Game.Core 的 MasterDataLoader 建立。</summary>
    public sealed class MasterDatabase
    {
        public GameConfig Config = new GameConfig();
        public Dictionary<string, CharacterDef> Characters = new Dictionary<string, CharacterDef>();
        public Dictionary<string, PortraitStageDef> PortraitStages = new Dictionary<string, PortraitStageDef>();
        public Dictionary<string, CostumeDef> Costumes = new Dictionary<string, CostumeDef>();
        public Dictionary<string, SceneDef> Scenes = new Dictionary<string, SceneDef>();
        public Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public Dictionary<string, StageDef> Stages = new Dictionary<string, StageDef>();
        public Dictionary<string, BannerDef> Banners = new Dictionary<string, BannerDef>();
        public Dictionary<string, SpotDef> Spots = new Dictionary<string, SpotDef>();
        public Dictionary<string, EncounterEventDef> Encounters = new Dictionary<string, EncounterEventDef>();
        public Dictionary<string, AchievementDef> Achievements = new Dictionary<string, AchievementDef>();

        public static MasterDatabase Build(
            GameConfig config,
            IEnumerable<CharacterDef> characters,
            IEnumerable<PortraitStageDef> portraitStages,
            IEnumerable<CostumeDef> costumes,
            IEnumerable<SceneDef> scenes,
            IEnumerable<EnemyDef> enemies,
            IEnumerable<StageDef> stages,
            IEnumerable<BannerDef> banners,
            IEnumerable<SpotDef> spots,
            IEnumerable<EncounterEventDef> encounters,
            IEnumerable<AchievementDef> achievements)
        {
            // 重複 ID 在這裡就會丟出例外（ToDictionary），不會默默覆蓋。
            return new MasterDatabase
            {
                Config = config ?? new GameConfig(),
                Characters = (characters ?? Enumerable.Empty<CharacterDef>()).ToDictionary(x => x.Id),
                PortraitStages = (portraitStages ?? Enumerable.Empty<PortraitStageDef>()).ToDictionary(x => x.Id),
                Costumes = (costumes ?? Enumerable.Empty<CostumeDef>()).ToDictionary(x => x.Id),
                Scenes = (scenes ?? Enumerable.Empty<SceneDef>()).ToDictionary(x => x.Id),
                Enemies = (enemies ?? Enumerable.Empty<EnemyDef>()).ToDictionary(x => x.Id),
                Stages = (stages ?? Enumerable.Empty<StageDef>()).ToDictionary(x => x.Id),
                Banners = (banners ?? Enumerable.Empty<BannerDef>()).ToDictionary(x => x.Id),
                Spots = (spots ?? Enumerable.Empty<SpotDef>()).ToDictionary(x => x.Id),
                Encounters = (encounters ?? Enumerable.Empty<EncounterEventDef>()).ToDictionary(x => x.Id),
                Achievements = (achievements ?? Enumerable.Empty<AchievementDef>()).ToDictionary(x => x.Id),
            };
        }

        /// <summary>角色的立繪階段，依 Order 排序。</summary>
        public List<PortraitStageDef> StagesOf(string charId)
        {
            CharacterDef c = Characters.GetOrNull(charId);
            if (c == null) return new List<PortraitStageDef>();
            return c.PortraitStages
                .Select(id => PortraitStages.GetOrNull(id))
                .Where(p => p != null)
                .OrderBy(p => p.Order)
                .ToList();
        }
    }
}
