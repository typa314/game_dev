using System.Collections.Generic;

namespace Game.Domain.Player
{
    // 玩家狀態（Profile 存檔的內容）。結構說明見 design/04_data_model.md §3、design/11_save_load.md。
    // 只能由 Game.Core.GameCore 的命令修改；UI 只讀取。

    public sealed class PlayerState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public ProfileInfo Profile = new ProfileInfo();
        public Dictionary<string, long> Items = new Dictionary<string, long>();
        public Dictionary<string, CharacterState> Characters = new Dictionary<string, CharacterState>();
        public HashSet<string> Costumes = new HashSet<string>();
        public HashSet<string> Scenes = new HashSet<string>();
        public LobbyState Lobby = new LobbyState();
        public List<string> Team = new List<string>();
        public Dictionary<string, StageProgress> Stages = new Dictionary<string, StageProgress>();
        public GachaState Gacha = new GachaState();
        public EncounterState Encounter = new EncounterState();
        public StoryGlobalState Story = new StoryGlobalState();
        public Dictionary<string, string> Endings = new Dictionary<string, string>();   // endingId -> 達成時間 ISO
        public HashSet<string> Gallery = new HashSet<string>();
        public Dictionary<string, AchievementProgress> Achievements = new Dictionary<string, AchievementProgress>();
        public SettingsState Settings = new SettingsState();

        public long ItemCount(string item)
        {
            long n;
            return Items.TryGetValue(item, out n) ? n : 0;
        }

        public void AddItem(string item, long n)
        {
            long cur = ItemCount(item) + n;
            if (cur == 0) Items.Remove(item); else Items[item] = cur;
        }

        public CharacterState Char(string id)
        {
            CharacterState c;
            return id != null && Characters.TryGetValue(id, out c) ? c : null;
        }
    }

    public sealed class ProfileInfo
    {
        public string CreatedAtUtc;
        public string Locale = "zh-TW";
        public Rating RatingOptIn = Rating.G;
        public string AgeConfirmedAtUtc;
    }

    public sealed class CharacterState
    {
        public int Level = 1;
        public long Exp;
        public int Awaken;
        public int Affection;
        public string PortraitStage;
        public string Costume;           // null = 使用立繪階段的預設造型
        public HashSet<string> UnlockedPortraitStages = new HashSet<string>();
        public string ObtainedAtUtc;

        // 稀有度與進階階級 (R -> SR -> SSR -> UR; +0 ~ +5)
        public string CurrentRarity = "R";
        public int Tier = 0; // 0 = +0, 1..5 = +1..+5

        public string DisplayGrade => Tier > 0 ? $"{CurrentRarity}+{Tier}" : CurrentRarity;
    }

    public sealed class LobbyState
    {
        public string Scene;
        public string Character;
        public string Costume;
    }

    public sealed class StageProgress
    {
        public bool Unlocked;
        public bool Cleared;
        public int Stars;
        public int ClearCount;
    }

    public sealed class GachaHistoryEntry
    {
        public string Banner;
        public string Char;
        public string Rarity;
        public string AtUtc;
    }

    public sealed class GachaState
    {
        public Dictionary<string, int> Pity = new Dictionary<string, int>();
        public List<GachaHistoryEntry> History = new List<GachaHistoryEntry>();
        public long TotalPulls;
    }

    public sealed class EncounterState
    {
        public int Stamina;
        public string StaminaUpdatedAtUtc;
        public int MissStreak;
        public HashSet<string> UnlockedSpots = new HashSet<string>();
        public HashSet<string> Seen = new HashSet<string>();
        public string Pending;           // 已選出、尚未結算的事件 ID
    }

    public sealed class StoryGlobalState
    {
        public Dictionary<string, double> Flags = new Dictionary<string, double>();
        public HashSet<string> ReadNodes = new HashSet<string>();
        public HashSet<string> ReadLines = new HashSet<string>();
        public HashSet<string> CompletedChapters = new HashSet<string>();
    }

    public sealed class AchievementProgress
    {
        public long Progress;
        public HashSet<string> Distinct;
        public string UnlockedAtUtc;
        public bool Claimed;
    }

    public sealed class SettingsState
    {
        public float Bgm = 0.8f;
        public float Se = 0.8f;
        public float Voice = 1.0f;
        public int TextSpeed = 40;
        public float AutoSpeed = 1.0f;
        public bool SkipUnread;
        public bool UseLive2d = true;
    }
}
