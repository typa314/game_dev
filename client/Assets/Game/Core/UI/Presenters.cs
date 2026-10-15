using System;
using System.Collections.Generic;
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
        Afternoon,  // 12:00 - 17:59
        Evening,    // 18:00 - 20:59
        Night       // 21:00 - 05:59
    }

    public interface ILobbyView : IView
    {
        void SetBackground(string sceneId, TimeOfDay timeOfDay);
        void SetCharacterPortrait(string artKey);
        void SetGreeting(string greetingText);
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

        public TimeOfDay GetCurrentTimeOfDay()
        {
            int hour = _core.Clock.UtcNow.Hour;
            if (hour >= 6 && hour < 12) return TimeOfDay.Morning;
            if (hour >= 12 && hour < 18) return TimeOfDay.Afternoon;
            if (hour >= 18 && hour < 21) return TimeOfDay.Evening;
            return TimeOfDay.Night;
        }

        public void Refresh()
        {
            if (View == null || _core.Player == null) return;

            string sceneId = _core.Player.Lobby?.Scene ?? "scene_cafe";
            string charId = _core.Player.Lobby?.Character ?? "char_claire";
            string costumeId = _core.Player.Lobby?.Costume;

            TimeOfDay tod = GetCurrentTimeOfDay();
            View.SetBackground(sceneId, tod);

            var resolved = PortraitResolver.Resolve(_core.Master, _core.Player, _core.Rating, charId, costumeId);
            View.SetCharacterPortrait(resolved?.Art?.Stand ?? "placeholder_portrait");
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

        public void OnCharacterClicked()
        {
            // 點擊看板娘觸發問候
            TimeOfDay tod = GetCurrentTimeOfDay();
            string charId = _core.Player.Lobby?.Character ?? "char_claire";
            View?.SetGreeting($"Greeting from {charId} at {tod}");
        }
    }

    public interface IWardrobeView : IView
    {
        void DisplayCharacter(string charId, string currentArtKey, IReadOnlyList<string> unlockedStages, IReadOnlyList<string> costumes);
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

        public GachaPresenter(GameCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
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
        void DisplayDetail(CharacterState character, int maxLevel, string currentArtKey);
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

        public Result<List<ItemStack>> Claim(string achievementId)
        {
            var res = _core.ClaimAchievement(achievementId);
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
    }
}
