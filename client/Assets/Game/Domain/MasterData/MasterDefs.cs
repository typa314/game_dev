using System.Collections.Generic;

namespace Game.Domain.MasterData
{
    // 主資料定義（唯讀，由 data/master/*.json 載入）。欄位設計見 design/04_data_model.md。
    // 用 public field 是為了讓 JSON 反序列化在 Unity 與 .NET 兩邊行為一致。

    public sealed class Condition
    {
        public string Type;
        public List<Condition> Of;      // all / any
        public Condition Cond;          // not
        public string Char;             // "self" = 目前角色
        public string Stage;
        public string Key;
        public string Op;               // >= > <= < == !=
        public string Value;            // 數字也以字串保存，由評估器轉型
        public string Id;
        public string Node;
        public string Ending;
        public string Rating;
    }

    public sealed class BattleStats
    {
        public int Cost;
        public double CooldownSec;
        public int Hp;
        public int Atk;
        public int Range;
        public double AttackIntervalSec;
        public int MoveSpeed;            // 單位 / 秒
        public int Knockbacks;
        public string Target = "single"; // single / area
        public List<string> Traits = new List<string>();
        public string GrowthCurve;
    }

    public sealed class PortraitArt
    {
        public string Stand;
        public string Bust;
        public string Live2d;
    }

    public sealed class CharacterDef
    {
        public string Id;
        public string Name;              // loc key
        public string Rarity;
        public string Role;
        public BattleStats Battle;
        public List<string> PortraitStages = new List<string>();
        public string DefaultCostume;
        public string StoryRoute;
        public List<string> ObtainableBy = new List<string>();
    }

    public sealed class StatBonus
    {
        public int AtkPct;
        public int HpPct;
    }

    public sealed class PortraitStageDef
    {
        public string Id;
        public string Character;
        public int Order;
        public Condition Unlock;         // null = 取得角色即解鎖
        public Rating Rating;
        public PortraitArt Art;
        public StatBonus StatBonus;
    }

    public sealed class CostumeDef
    {
        public string Id;
        public string Character;
        public Rating Rating;
        public Condition Unlock;
        public string RequiresPortraitStage;
        public PortraitArt Art;
        public string BattleSkin;
    }

    public sealed class SceneDef
    {
        public string Id;
        public string Name;
        public Rating Rating;
        public Dictionary<string, string> Variants = new Dictionary<string, string>();
        public bool AutoVariantByClock;
        public string Bgm;
        public Condition Unlock;
    }

    public sealed class EnemyDef
    {
        public string Id;
        public string Type;              // normal / flying / armored ...
        public BattleStats Battle;
        public int DefeatGold;
    }

    public sealed class WaveDef
    {
        public string Enemy;
        public double AtSec;
        public int Count = 1;
        public double IntervalSec;
        public double RepeatEverySec;    // 0 = 不重複
        public Condition Trigger;        // 目前支援 enemy_base_hp_below_pct；有 trigger 時忽略 AtSec
    }

    public sealed class Drop
    {
        public string Item;
        public long N;
        public double Rate = 1.0;
    }

    public sealed class StageRewards
    {
        public List<ItemStack> FirstClear = new List<ItemStack>();
        public List<Drop> Drop = new List<Drop>();
        public int TeamExp;
    }

    public sealed class StageDef
    {
        public string Id;
        public int Chapter;
        public string Name;
        public string Bg;
        public string Bgm;
        public int Length = 3000;        // 戰場長度（單位）
        public int EnemyBaseHp;
        public int AllyBaseHp;           // 0 = 用 BattleRules.DefaultAllyBaseHp
        public double TimeLimitSec;      // 0 = 無限制
        public int EnemyStatPct = 100;
        public List<WaveDef> Waves = new List<WaveDef>();
        public StageRewards Rewards = new StageRewards();
        public List<string> StarConditions = new List<string>();
        public List<string> UnlocksOnClear = new List<string>();
    }

    public sealed class BannerCost
    {
        public List<ItemStack> Single = new List<ItemStack>();
        public List<ItemStack> Ten = new List<ItemStack>();
    }

    public sealed class BannerPickup
    {
        public List<string> Chars = new List<string>();
        public double ShareOfRarity;
    }

    public sealed class BannerPity
    {
        public int HardAt;
        public string Rarity;
    }

    public sealed class BannerDef
    {
        public string Id;
        public string Type;
        public string Name;
        public BannerCost Cost = new BannerCost();
        public Dictionary<string, double> Rates = new Dictionary<string, double>();
        public Dictionary<string, List<string>> Pool = new Dictionary<string, List<string>>();
        public BannerPickup Pickup;
        public BannerPity Pity;
        public string TenPullGuarantee;
        public string PeriodStartUtc;    // ISO 8601，null = 常駐
        public string PeriodEndUtc;
        public bool AllowCrossRarity;    // 是否允許同角色跨稀有度抽取 (R/SR/SSR/UR)
    }

    public sealed class Outcome
    {
        public string Item;
        public long N;
        public string AffectionChar;     // 有值時代表增加好感度 N
        public string Costume;           // 有值時代表取得服裝
    }

    public sealed class EncounterEventDef
    {
        public string Id;
        public string Spot;
        public string Character;         // 有值時，只有已持有該角色才會出現（巧遇不招募角色）
        public string Series;            // null = 一般事件
        public int Order;
        public int Weight = 10;
        public Condition Conditions;
        public string Story;             // Yarn 節點名
        public Dictionary<string, List<Outcome>> Outcomes = new Dictionary<string, List<Outcome>>();
        public Rating Rating;
        public bool Repeatable;
    }

    public sealed class SpotDef
    {
        public string Id;
        public string Name;
        public Condition Unlock;
    }

    public sealed class AchievementTrigger
    {
        public string Event;
        public string Counter;           // sum / count / distinct / state
        public string Field;
        public long Target = 1;
        public Dictionary<string, string> Filter = new Dictionary<string, string>();
        public Condition Condition;      // counter = state 時使用
    }

    public sealed class AchievementDef
    {
        public string Id;
        public string Category;
        public string Name;
        public string Desc;
        public Rating Rating;
        public bool Hidden;
        public AchievementTrigger Trigger;
        public List<ItemStack> Reward = new List<ItemStack>();
        public string SteamApiName;
    }

    // ---- 全域設定（config.json） ----

    public sealed class ExpCurve
    {
        public double Base;
        public double Exponent;
    }

    public sealed class GrowthConfig
    {
        public Dictionary<string, ExpCurve> ExpCurveByRarity = new Dictionary<string, ExpCurve>();
        public Dictionary<string, int> StatCurvePerLevelPct = new Dictionary<string, int>();
        public List<int> LevelCapByAwaken = new List<int>();
        public List<int> AwakenShardCost = new List<int>();     // index i = 從 ★i 升到 ★(i+1) 的碎片數
        public List<int> AwakenStatBonusPct = new List<int>();  // index = ★
        public List<int> AffectionThresholds = new List<int>(); // index = 好感等級 - 1
        public Dictionary<string, int> ExpItems = new Dictionary<string, int>();
        public int GoldPerExp;
        public string GoldItem = "gold";
        public string ShardItemPrefix = "awaken_shard_";
        public int ShardsPerDuplicate;
        public string MaxedDuplicateItem;
        public int MaxedDuplicateCount;
        public int AffectionPerWin;
    }

    public sealed class EncounterConfig
    {
        public int StaminaMax;
        public int StaminaRegenMinutes;
        public int PityThreshold;
        public List<ItemStack> FallbackReward = new List<ItemStack>();
    }

    public sealed class BattleRules
    {
        public int TicksPerSecond = 30;
        public int DefaultAllyBaseHp;
        public List<int> EnergyMaxByLevel = new List<int>();
        public List<int> EnergyRegenPerSecByLevel = new List<int>();
        public List<int> EnergyUpgradeCost = new List<int>();   // index i = 從 Lv(i+1) 升級的花費
        public int StartEnergy;
        public double CannonChargeSec;
        public int CannonDamage;
        public int CannonRange;
        public int KnockbackDistance;
        public int MaxUnitsPerSide;
        public double MaxBattleSec;
    }

    public sealed class InitialState
    {
        public List<ItemStack> Items = new List<ItemStack>();
        public List<string> Characters = new List<string>();
        public List<string> UnlockedStages = new List<string>();
        public List<string> UnlockedSpots = new List<string>();
        public List<string> Scenes = new List<string>();
    }

    public sealed class GameConfig
    {
        public int MasterDataVersion;
        public GrowthConfig Growth = new GrowthConfig();
        public EncounterConfig Encounter = new EncounterConfig();
        public BattleRules Battle = new BattleRules();
        public InitialState Initial = new InitialState();
        public int GachaHistoryLimit = 200;
    }
}
