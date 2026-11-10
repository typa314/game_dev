using System;
using System.Collections.Generic;
using System.Linq;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Domain.Portrait;

namespace Game.Core.UI
{
    // =========================================================================================
    // H9: LobbyPresenter & WardrobePresenter
    // =========================================================================================
    public enum TimeOfDay
    {
        Morning,    // 06:00 - 11:59
        Afternoon,  // 12:00 - 16:59
        Evening,    // 17:00 - 19:59
        Night       // 20:00 - 05:59
    }

    public interface ILobbyView : IView
    {
        void SetBackground(string sceneId, TimeOfDay timeOfDay);
        void SetCharacterPortrait(string artKey);
        void SetGreeting(string messageKey);
        void ShowError(string message);
    }

    public sealed class LobbyPresenter : PresenterBase<ILobbyView>
    {
        private readonly GameCore _core;

        public LobbyPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public override void Initialize()
        {
            base.Initialize();
            Refresh();
        }

        public void Refresh()
        {
            if (View == null || _core.Player == null) return;

            string sceneId = _core.Player.Lobby?.Scene;
            if (string.IsNullOrEmpty(sceneId))
            {
                sceneId = _core.Master?.Config?.Initial?.Scenes != null && _core.Master.Config.Initial.Scenes.Count > 0
                    ? _core.Master.Config.Initial.Scenes[0]
                    : "scene_lobby_default";
            }

            string charId = _core.Player.Lobby?.Character;
            if (string.IsNullOrEmpty(charId))
            {
                if (_core.Master?.Characters != null && _core.Master.Characters.ContainsKey("char01_claire"))
                    charId = "char01_claire";
                else
                    charId = "char_claire";
            }
            string costumeId = _core.Player.Lobby?.Costume;

            TimeOfDay tod = GetCurrentTimeOfDay();
            View.SetBackground(sceneId, tod);

            // 依 PortraitResolver 算出的立繪資源解析 Key
            var resolved = PortraitResolver.Resolve(_core.Master, _core.Player, _core.Rating, charId);
            View.SetCharacterPortrait(resolved?.Art?.Stand);
        }

        public TimeOfDay GetCurrentTimeOfDay()
        {
            int hour = _core.Clock.LocalNow.Hour;
            if (hour >= 6 && hour < 12) return TimeOfDay.Morning;
            if (hour >= 12 && hour < 17) return TimeOfDay.Afternoon;
            if (hour >= 17 && hour < 20) return TimeOfDay.Evening;
            return TimeOfDay.Night;
        }

        public void OnCharacterClicked()
        {
            // 點擊看板娘觸發問候
            TimeOfDay tod = GetCurrentTimeOfDay();
            string charId = _core.Player.Lobby?.Character;
            if (string.IsNullOrEmpty(charId))
            {
                if (_core.Master?.Characters != null && _core.Master.Characters.ContainsKey("char01_claire"))
                    charId = "char01_claire";
                else
                    charId = "char_claire";
            }
            View?.SetGreeting($"Greeting from {charId} at {tod}");
        }

        public Result ChangeLobby(string sceneId, string charId, string costumeId = null)
        {
            var res = _core.SetLobby(sceneId, charId, costumeId);
            if (res.Ok)
            {
                Refresh();
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }
    }

    public interface IWardrobeView : IView
    {
        void DisplayCharacter(string charId, string currentArtKey, IReadOnlyList<string> unlockedStages, IReadOnlyList<string> costumes);
        void ShowSuccessMessage(string message);
        void ShowError(string message);
    }

    public sealed class WardrobePresenter : PresenterBase<IWardrobeView>
    {
        private readonly GameCore _core;

        public WardrobePresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public void SelectCharacter(string charId)
        {
            if (View == null || _core.Player == null) return;
            var c = _core.Player.Char(charId);
            if (c == null)
            {
                View.ShowError("ERR_CHARACTER_NOT_OWNED");
                return;
            }

            var resolved = PortraitResolver.Resolve(_core.Master, _core.Player, _core.Rating, charId);
            var stages = new List<string>(c.UnlockedPortraitStages);
            var costumes = new List<string>(_core.Player.Costumes);

            View.DisplayCharacter(charId, resolved?.Art?.Stand, stages, costumes);
        }

        public Result Equip(string charId, string costumeId)
        {
            var res = _core.EquipCostume(charId, costumeId);
            if (res.Ok)
            {
                SelectCharacter(charId);
                View?.ShowSuccessMessage("COSTUME_EQUIPPED");
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }

        public Result SwitchPortraitStage(string charId, string stageId)
        {
            var res = _core.SetPortraitStage(charId, stageId);
            if (res.Ok)
            {
                SelectCharacter(charId);
                View?.ShowSuccessMessage("PORTRAIT_STAGE_UPDATED");
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }

        public IReadOnlyList<CharacterDef> GetAllCharacters()
        {
            if (_core?.Master?.Characters == null) return Array.Empty<CharacterDef>();
            return _core.Master.Characters.Values.ToList();
        }

        public CharacterState GetCharacterState(string charId)
        {
            return _core?.Player?.Char(charId);
        }

        public IReadOnlyList<CostumeDef> GetCostumesForCharacter(string charId)
        {
            if (string.IsNullOrEmpty(charId) || _core?.Master?.Costumes == null)
                return Array.Empty<CostumeDef>();

            return _core.Master.Costumes.Values
                .Where(c => c != null && string.Equals(c.Character, charId, StringComparison.Ordinal))
                .ToList();
        }

        public Result SetAsLobbySecretary(string charId, string sceneId = null)
        {
            if (_core?.Player == null) return Result.Fail("no_player");
            string scene = !string.IsNullOrEmpty(sceneId) ? sceneId : (_core.Player.Lobby?.Scene ?? "scene_lobby_default");
            string costume = _core.Player.Char(charId)?.Costume;
            var res = _core.SetLobby(scene, charId, costume);
            if (res.Ok)
            {
                View?.ShowSuccessMessage("SET_LOBBY_SUCCESS");
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }
    }

    // =========================================================================================
    // H10: GachaPresenter, CharacterDetailPresenter, AchievementPresenter
    // =========================================================================================
    public interface IGachaView : IView
    {
        void ShowGachaResult(IReadOnlyList<GachaResultEntry> results);
        void ShowError(string message);
    }

    public sealed class GachaPresenter : PresenterBase<IGachaView>
    {
        private readonly GameCore _core;

        public GameCore Core => _core;

        public GachaPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public IReadOnlyList<BannerDef> GetAvailableBanners()
        {
            if (_core?.Master?.Banners == null) return Array.Empty<BannerDef>();
            var list = new List<BannerDef>();
            foreach (var banner in _core.Master.Banners.Values)
            {
                if (_core.InPeriod(banner))
                {
                    list.Add(banner);
                }
            }
            return list;
        }

        public BannerDef GetBanner(string bannerId)
        {
            if (_core?.Master?.Banners == null) return null;
            return _core.Master.Banners.GetOrNull(bannerId);
        }

        public CharacterDef GetFeaturedCharacter(string bannerId)
        {
            var banner = GetBanner(bannerId);
            if (banner == null || _core?.Master?.Characters == null) return null;

            if (banner.Pickup?.Chars != null && banner.Pickup.Chars.Count > 0)
            {
                string pickupId = banner.Pickup.Chars[0];
                if (!string.IsNullOrEmpty(pickupId) && _core.Master.Characters.TryGetValue(pickupId, out var pickupChar))
                {
                    return pickupChar;
                }
            }

            if (banner.Pool != null)
            {
                string[] order = new[] { "UR", "SSR", "SR", "R" };
                for (int i = 0; i < order.Length; i++)
                {
                    if (banner.Pool.TryGetValue(order[i], out var list) && list != null && list.Count > 0)
                    {
                        if (_core.Master.Characters.TryGetValue(list[0], out var poolChar))
                        {
                            return poolChar;
                        }
                    }
                }
            }

            return _core.Master.Characters.Values.FirstOrDefault();
        }

        public int GetPityCount(string bannerId)
        {
            if (_core?.Player?.Gacha?.Pity == null) return 0;
            _core.Player.Gacha.Pity.TryGetValue(bannerId, out int pity);
            return pity;
        }

        public int GetPityLimit(string bannerId)
        {
            var b = GetBanner(bannerId);
            return b?.Pity?.HardAt ?? 100;
        }

        public long GetGemCount()
        {
            return _core?.Player?.ItemCount("gem") ?? 0;
        }

        public long GetGoldCount()
        {
            return _core?.Player?.ItemCount("gold") ?? 0;
        }

        public IReadOnlyList<GachaHistoryEntry> GetHistory(int maxEntries = 50)
        {
            if (_core?.Player?.Gacha?.History == null) return Array.Empty<GachaHistoryEntry>();
            var history = _core.Player.Gacha.History;
            if (history.Count <= maxEntries) return history;
            return history.GetRange(history.Count - maxEntries, maxEntries);
        }

        public Result<List<GachaResultEntry>> Pull(string bannerId, int count)
        {
            var res = _core.PullGacha(bannerId, count);
            if (res.Ok)
            {
                View?.ShowGachaResult(res.Value);
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }
    }

    public interface ICharacterDetailView : IView
    {
        void DisplayDetail(CharacterState character, int maxLevel, string artKey);
        void ShowSuccessMessage(string message);
        void ShowError(string message);
    }

    public sealed class CharacterDetailPresenter : PresenterBase<ICharacterDetailView>
    {
        private readonly GameCore _core;

        public CharacterDetailPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public void LoadCharacter(string charId)
        {
            if (View == null || _core.Player == null) return;
            var c = _core.Player.Char(charId);
            if (c == null)
            {
                View.ShowError("ERR_CHARACTER_NOT_OWNED");
                return;
            }

            int maxLv = _core.Growth.LevelCap(c.Awaken);
            var resolved = PortraitResolver.Resolve(_core.Master, _core.Player, _core.Rating, charId);
            View.DisplayDetail(c, maxLv, resolved?.Art?.Stand);
        }

        public Result<int> LevelUp(string charId, List<ItemStack> materials)
        {
            var res = _core.LevelUp(charId, materials);
            if (res.Ok)
            {
                LoadCharacter(charId);
                View?.ShowSuccessMessage($"Level Up! New Level: {res.Value}");
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }

        public Result Awaken(string charId)
        {
            var res = _core.Awaken(charId);
            if (res.Ok)
            {
                LoadCharacter(charId);
                View?.ShowSuccessMessage("Awakening Succeeded!");
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }

        public Result<GameCore.AdvanceResult> Advance(string charId)
        {
            var res = _core.AdvanceCharacter(charId);
            if (res.Ok)
            {
                LoadCharacter(charId);
                View?.ShowSuccessMessage($"Advance Succeeded! Tier: {res.Value.DisplayGrade}");
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }

        public IReadOnlyList<CharacterDef> GetAllCharacters()
        {
            if (_core?.Master?.Characters == null) return Array.Empty<CharacterDef>();
            return _core.Master.Characters.Values.ToList();
        }

        public CharacterState GetCharacterState(string charId)
        {
            return _core?.Player?.Char(charId);
        }

        public CharacterDef GetCharacterDef(string charId)
        {
            if (string.IsNullOrEmpty(charId) || _core?.Master?.Characters == null) return null;
            CharacterDef def;
            return _core.Master.Characters.TryGetValue(charId, out def) ? def : null;
        }

        public Result<int> QuickLevelUp(string charId, int targetLevel = 30)
        {
            if (_core?.Player == null) return Result<int>.Fail("no_player");
            var c = _core.Player.Char(charId);
            if (c == null) return Result<int>.Fail("character_not_owned");

            int maxLv = _core.Growth.LevelCap(c.Awaken);
            int goal = Math.Min(targetLevel, maxLv);
            if (c.Level >= goal) return Result<int>.Fail("already_reached_level");

            var materials = new List<ItemStack>();
            long expNeed = 0;
            for (int lv = c.Level; lv < goal; lv++)
            {
                expNeed += _core.Growth.ExpToNext(c.CurrentRarity, lv);
            }
            expNeed = Math.Max(0, expNeed - c.Exp);

            long ownedL = _core.Player.ItemCount("exp_l");
            long needL = (long)Math.Ceiling((double)expNeed / 1000.0);
            long useL = Math.Min(ownedL, needL);
            if (useL > 0)
            {
                materials.Add(new ItemStack("exp_l", (int)useL));
            }

            return LevelUp(charId, materials);
        }
    }

    public interface IAchievementView : IView
    {
        void DisplayAchievements(IReadOnlyList<AchievementDef> all, Dictionary<string, AchievementProgress> progress);
        void ShowClaimReward(IReadOnlyList<ItemStack> rewards);
        void ShowError(string message);
    }

    public sealed class AchievementPresenter : PresenterBase<IAchievementView>
    {
        private readonly GameCore _core;

        public AchievementPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public override void Initialize()
        {
            base.Initialize();
            Refresh();
        }

        public void Refresh()
        {
            if (View == null || _core.Player == null) return;
            var all = new List<AchievementDef>(_core.Master.Achievements.Values);
            View.DisplayAchievements(all, _core.Player.Achievements);
        }

        public Result ClaimReward(string achId)
        {
            var res = _core.ClaimAchievement(achId);
            if (res.Ok)
            {
                Refresh();
                View?.ShowClaimReward(res.Value);
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }

        public int ClaimAll()
        {
            if (_core?.Player?.Achievements == null || _core.Master?.Achievements == null) return 0;

            int claimedCount = 0;
            var totalRewards = new List<ItemStack>();

            foreach (var ach in _core.Master.Achievements.Values)
            {
                if (_core.Player.Achievements.TryGetValue(ach.Id, out var prog) &&
                    !string.IsNullOrEmpty(prog.UnlockedAtUtc) &&
                    !prog.Claimed)
                {
                    var res = _core.ClaimAchievement(ach.Id);
                    if (res.Ok)
                    {
                        claimedCount++;
                        if (res.Value != null)
                        {
                            totalRewards.AddRange(res.Value);
                        }
                    }
                }
            }

            if (claimedCount > 0)
            {
                Refresh();
                View?.ShowClaimReward(totalRewards);
            }

            return claimedCount;
        }
    }

    // =========================================================================================
    // OPT-3: StageSelectPresenter (關卡星圖與戰前編隊)
    // =========================================================================================
    public sealed class StageSummaryItem
    {
        public string StageId { get; set; }
        public string NameKey { get; set; }
        public int Chapter { get; set; }
        public bool IsUnlocked { get; set; }
        public int Stars { get; set; }
        public int EnemyBaseHp { get; set; }
        public IReadOnlyList<string> EnemyBaseTraits { get; set; }
        public IReadOnlyList<string> StarConditions { get; set; }
        public IReadOnlyList<ItemStack> FirstClearRewards { get; set; }
    }

    public interface IStageSelectView : IView
    {
        void DisplayStages(IReadOnlyList<StageSummaryItem> stages, string selectedStageId);
        void DisplayStageDetail(StageSummaryItem summary, StageDef stageDef);
        void DisplayTeam(IReadOnlyList<string> teamCharacterIds, IReadOnlyList<string> allOwnedCharacterIds);
        void DisplayBaseStatus(int baseLevel, int maxLevel, int currentHp, int nextHp, int upgradeGoldCost, bool canAfford);
        void ShowError(string message);
    }

    public sealed class StageSelectPresenter : PresenterBase<IStageSelectView>
    {
        private readonly GameCore _core;
        private string _selectedStageId;
        private int _currentChapter = 1;

        public GameCore Core => _core;
        public string SelectedStageId => _selectedStageId;
        public int CurrentChapter => _currentChapter;
        public StageDef SelectedStage => !string.IsNullOrEmpty(_selectedStageId) && _core.Master?.Stages != null
            ? _core.Master.Stages.GetOrNull(_selectedStageId)
            : null;

        public StageSelectPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        public override void Initialize()
        {
            base.Initialize();
            Refresh();
        }

        public IReadOnlyList<int> GetChapters()
        {
            if (_core?.Master?.Stages == null || _core.Master.Stages.Count == 0)
                return new List<int> { 1 };

            return _core.Master.Stages.Values
                .Select(s => s.Chapter)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }

        public IReadOnlyList<StageSummaryItem> GetStages()
        {
            var list = new List<StageSummaryItem>();
            if (_core?.Master?.Stages == null) return list;

            var orderedStages = _core.Master.Stages.Values
                .OrderBy(s => s.Chapter)
                .ThenBy(s => s.Id);

            foreach (var st in orderedStages)
            {
                bool isUnlocked = false;
                int stars = 0;
                if (_core.Player?.Stages != null && _core.Player.Stages.TryGetValue(st.Id, out var prog))
                {
                    isUnlocked = prog.Unlocked;
                    stars = prog.Stars;
                }

                list.Add(new StageSummaryItem
                {
                    StageId = st.Id,
                    NameKey = st.Name,
                    Chapter = st.Chapter,
                    IsUnlocked = isUnlocked,
                    Stars = stars,
                    EnemyBaseHp = st.EnemyBaseHp,
                    EnemyBaseTraits = st.EnemyBaseTraits != null ? new List<string>(st.EnemyBaseTraits) : (IReadOnlyList<string>)Array.Empty<string>(),
                    StarConditions = st.StarConditions != null ? new List<string>(st.StarConditions) : (IReadOnlyList<string>)Array.Empty<string>(),
                    FirstClearRewards = st.Rewards?.FirstClear != null ? new List<ItemStack>(st.Rewards.FirstClear) : (IReadOnlyList<ItemStack>)Array.Empty<ItemStack>()
                });
            }

            return list;
        }

        public IReadOnlyList<StageSummaryItem> GetStagesByChapter(int chapter)
        {
            return GetStages().Where(s => s.Chapter == chapter).ToList();
        }

        public bool SelectChapter(int chapter)
        {
            var chapters = GetChapters();
            if (!chapters.Contains(chapter)) return false;

            _currentChapter = chapter;
            var chapterStages = GetStagesByChapter(chapter);
            if (chapterStages.Count > 0 && !chapterStages.Any(s => s.StageId == _selectedStageId))
            {
                var firstUnlocked = chapterStages.FirstOrDefault(s => s.IsUnlocked);
                _selectedStageId = firstUnlocked?.StageId ?? chapterStages.First().StageId;
            }

            View?.DisplayStages(chapterStages, _selectedStageId);
            var currentItem = GetStages().FirstOrDefault(s => s.StageId == _selectedStageId);
            View?.DisplayStageDetail(currentItem, SelectedStage);
            return true;
        }

        public string GetNextUnlockedStageId(string currentStageId)
        {
            if (string.IsNullOrEmpty(currentStageId) || _core?.Master?.Stages == null) return null;

            var orderedStages = _core.Master.Stages.Values
                .OrderBy(s => s.Chapter)
                .ThenBy(s => s.Id)
                .ToList();

            int currentIndex = orderedStages.FindIndex(s => s.Id == currentStageId);
            if (currentIndex < 0 || currentIndex >= orderedStages.Count - 1) return null;

            var nextStage = orderedStages[currentIndex + 1];
            if (_core.Player?.Stages != null && _core.Player.Stages.TryGetValue(nextStage.Id, out var prog) && prog.Unlocked)
            {
                return nextStage.Id;
            }

            return null;
        }

        public void Refresh()
        {
            var allStages = GetStages();
            if (string.IsNullOrEmpty(_selectedStageId) || !allStages.Any(s => s.StageId == _selectedStageId))
            {
                var firstUnlocked = allStages.FirstOrDefault(s => s.IsUnlocked);
                _selectedStageId = firstUnlocked?.StageId ?? allStages.FirstOrDefault()?.StageId;
            }

            var currentItem = allStages.FirstOrDefault(s => s.StageId == _selectedStageId);
            if (currentItem != null && currentItem.Chapter > 0)
            {
                _currentChapter = currentItem.Chapter;
            }

            View?.DisplayStages(GetStagesByChapter(_currentChapter), _selectedStageId);
            View?.DisplayStageDetail(currentItem, SelectedStage);

            View?.DisplayTeam(GetTeam(), GetOwnedCharacterIds());
            RefreshBaseStatus();
        }

        public void RefreshBaseStatus()
        {
            if (_core == null || View == null) return;
            int lv = _core.GetAllyBaseLevel();
            int maxLv = _core.Growth.BaseMaxLevel;
            int curHp = _core.GetAllyBaseMaxHp();
            int nextHp = lv < maxLv
                ? _core.Growth.ComputeBaseMaxHp(lv + 1, _core.Master.Config.Battle.DefaultAllyBaseHp)
                : curHp;
            int cost = lv < maxLv ? _core.GetNextBaseUpgradeCost() : 0;
            bool canAfford = lv < maxLv && _core.Player.ItemCount(_core.Master.Config.Growth.GoldItem) >= cost;
            View.DisplayBaseStatus(lv, maxLv, curHp, nextHp, cost, canAfford);
        }

        public Result<int> UpgradeBase()
        {
            if (_core == null) return Result<int>.Fail("no_core");
            var res = _core.UpgradeBase();
            if (res.Ok)
            {
                RefreshBaseStatus();
            }
            else
            {
                string msg = res.Error switch
                {
                    "base_max_level" => "我方主堡已達最高強化等級",
                    "insufficient_item" => "金幣不足，無法強化主堡",
                    _ => res.Error,
                };
                View?.ShowError(msg);
            }
            return res;
        }

        public bool SelectStage(string stageId)
        {
            if (string.IsNullOrEmpty(stageId) || _core.Master?.Stages == null || !_core.Master.Stages.ContainsKey(stageId))
            {
                return false;
            }

            _selectedStageId = stageId;
            var stDef = _core.Master.Stages.GetOrNull(stageId);
            if (stDef != null)
            {
                _currentChapter = stDef.Chapter;
            }

            var stages = GetStages();
            var currentItem = stages.FirstOrDefault(s => s.StageId == _selectedStageId);

            View?.DisplayStages(GetStagesByChapter(_currentChapter), _selectedStageId);
            View?.DisplayStageDetail(currentItem, SelectedStage);
            return true;
        }

        public bool CanStartSelectedStage()
        {
            if (string.IsNullOrEmpty(_selectedStageId) || _core.Player == null) return false;
            if (!_core.Player.Stages.TryGetValue(_selectedStageId, out var prog) || !prog.Unlocked) return false;
            if (_core.Player.Team == null || !_core.Player.Team.Any(c => !string.IsNullOrEmpty(c))) return false;
            return true;
        }

        public IReadOnlyList<string> GetTeam()
        {
            return _core.Player?.Team != null ? new List<string>(_core.Player.Team) : (IReadOnlyList<string>)Array.Empty<string>();
        }

        public IReadOnlyList<string> GetOwnedCharacterIds()
        {
            return _core.Player?.Characters != null
                ? new List<string>(_core.Player.Characters.Keys)
                : (IReadOnlyList<string>)Array.Empty<string>();
        }

        public Result ToggleTeamMember(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return Result.Fail("invalid_character");
            if (_core.Player == null || _core.Player.Char(charId) == null)
            {
                return Result.Fail("character_not_owned");
            }

            var currentTeam = new List<string>(_core.Player.Team ?? new List<string>());
            if (currentTeam.Contains(charId))
            {
                if (currentTeam.Count <= 1)
                {
                    View?.ShowError("隊伍至少需保留 1 名出戰角色");
                    return Result.Fail("invalid_team");
                }
                currentTeam.Remove(charId);
            }
            else
            {
                if (currentTeam.Count >= 10)
                {
                    View?.ShowError("出戰隊伍上限為 10 人");
                    return Result.Fail("team_full");
                }
                currentTeam.Add(charId);
            }

            var res = _core.SetTeam(currentTeam);
            if (res.Ok)
            {
                View?.DisplayTeam(GetTeam(), GetOwnedCharacterIds());
            }
            else
            {
                View?.ShowError(res.Error);
            }
            return res;
        }
    }
}
