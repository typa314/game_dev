using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Domain.MasterData;
using Newtonsoft.Json;

namespace Game.Core
{
    /// <summary>
    /// 從資料夾載入主資料 JSON（來源：F:\game\data\master，Unity 執行期為 StreamingAssets/MasterData）。
    /// 缺少的檔案視為空集合；config.json 必須存在。
    /// </summary>
    public static class MasterDataLoader
    {
        public static MasterDatabase LoadFromDirectory(string dir)
        {
            return MasterDatabase.Build(
                Read<GameConfig>(dir, "config.json", true),
                ReadList<CharacterDef>(dir, "characters.json"),
                ReadList<PortraitStageDef>(dir, "portrait_stages.json"),
                ReadList<CostumeDef>(dir, "costumes.json"),
                ReadList<SceneDef>(dir, "scenes.json"),
                ReadList<EnemyDef>(dir, "enemies.json"),
                ReadList<StageDef>(dir, "stages.json"),
                ReadList<BannerDef>(dir, "banners.json"),
                ReadList<SpotDef>(dir, "spots.json"),
                ReadList<EncounterEventDef>(dir, "encounters.json"),
                ReadList<AchievementDef>(dir, "achievements.json"));
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // 主資料打錯欄位名稱時直接報錯，避免默默被忽略
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        private static T Read<T>(string dir, string file, bool required) where T : class
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path))
            {
                if (required) throw new FileNotFoundException("master data missing: " + path);
                return null;
            }
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(path, Encoding.UTF8), Settings); }
            catch (JsonException e) { throw new InvalidDataException(file + ": " + e.Message, e); }
        }

        private static List<T> ReadList<T>(string dir, string file)
        {
            return Read<List<T>>(dir, file, false) ?? new List<T>();
        }
    }
}
