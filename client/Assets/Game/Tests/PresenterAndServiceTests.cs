using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Audio;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class PresenterAndServiceTests
    {
        private MasterDatabase _master;
        private PlayerState _player;
        private GameCore _core;

        [SetUp]
        public void Setup()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            _player = GameCore.NewPlayer(_master, clock);
            _player.AddItem("item_gold", 100000);
            _player.AddItem("item_dia_free", 10000);
            _player.Encounter.Stamina = 120;
            _player.Scenes.Add("scene_cafe");

            var rating = new RatingPolicy(Rating.R18, Rating.R18);
            var rng = new SplitMix64Random(123456789UL);
            _core = new GameCore(_master, _player, rng, clock, rating);
            _core.OnLoaded();
        }

        // =========================================================================================
        // H8: ADV 劇情播放器 (StoryPresenter)
        // =========================================================================================
        private class MockStoryView : IStoryView
        {
            public bool IsVisible { get; set; } = true;
            public string Speaker { get; private set; }
            public string Dialogue { get; private set; }
            public List<string> Options { get; private set; } = new List<string>();
            public bool UiVisible { get; private set; } = true;
            public int BacklogCount { get; private set; }

            public void SetSpeaker(string name) => Speaker = name;
            public void SetDialogue(string textKey) => Dialogue = textKey;
            public void ShowOptions(IReadOnlyList<string> options) => Options = new List<string>(options);
            public void HideOptions() => Options.Clear();
            public void SetBackground(string bgId, string variant) { }
            public void SetCharacterVisible(string characterId, bool visible) { }
            public void SetCharacterExpression(string characterId, string expression) { }
            public void SetCharacterMotion(string characterId, string motion) { }
            public void SetCg(string cgId) { }
            public void SetUiVisible(bool visible) => UiVisible = visible;
            public void UpdateBacklog(IReadOnlyList<BacklogEntry> history) => BacklogCount = history.Count;
            public void PlayBgm(string bgmId) { }
            public void PlaySe(string seId) { }
            public void PlayVoice(string voiceId) { }
        }

        [Test]
        public void H8_StoryPresenter_AdvancesDialogue_MaintainsBacklog_AndHandlesModes()
        {
            const string scriptText = @"
[node Start]
line l1 Claire ""Hello commander!""
line l2 Claire ""Ready for the mission?""
option opt1 -> NodeYes
option opt2 -> NodeWait

[node NodeYes]
line l3 Claire ""Understood!""

[node NodeWait]
line l4 Claire ""Take your time.""
";
            var script = MiniScript.Parse(scriptText);
            var runner = new MiniScriptRunner(script);
            runner.Start("Start");
            var controller = new StoryPlaybackController(runner, _core);
            var presenter = new StoryPresenter(controller);
            var view = new MockStoryView();
            presenter.AttachView(view);
            presenter.Initialize();

            // 1. 第 1 句台詞
            Assert.AreEqual("Claire", view.Speaker);
            Assert.AreEqual("Hello commander!", view.Dialogue);
            Assert.AreEqual(1, presenter.Backlog.Count);
            Assert.AreEqual("Hello commander!", presenter.Backlog[0].TextKey);

            // 2. 推進到第 2 句
            presenter.Advance();
            Assert.AreEqual("Ready for the mission?", view.Dialogue);
            Assert.AreEqual(2, presenter.Backlog.Count);

            // 3. 推進到選項
            presenter.Advance();
            Assert.AreEqual(2, view.Options.Count);
            Assert.AreEqual("opt1", view.Options[0]);

            // 4. 存檔 Snapshot
            var snapshot = presenter.CreateSnapshot();
            Assert.IsNotNull(snapshot, "劇情快照應成功建立");

            // 5. 選擇選項 0 -> 進入 NodeYes
            presenter.ChooseOption(0);
            Assert.AreEqual(0, view.Options.Count);
            Assert.AreEqual("Understood!", view.Dialogue);
            Assert.AreEqual(3, presenter.Backlog.Count);

            // 6. 讀檔回溯到選項前
            presenter.RestoreSnapshot(snapshot);
            Assert.AreEqual(2, view.Options.Count);

            // 7. UI 隱藏與恢復
            presenter.ToggleHideUi();
            Assert.IsTrue(presenter.IsUiHidden);
            Assert.IsFalse(view.UiVisible);
            presenter.RestoreUi();
            Assert.IsFalse(presenter.IsUiHidden);
            Assert.IsTrue(view.UiVisible);

            // 8. 模式切換（AutoPlay 與 FastForward 互斥）
            presenter.ToggleAutoPlay();
            Assert.IsTrue(presenter.IsAutoPlay);
            Assert.IsFalse(presenter.IsFastForward);

            presenter.SetFastForward(true);
            Assert.IsTrue(presenter.IsFastForward);
            Assert.IsFalse(presenter.IsAutoPlay);
        }

        // =========================================================================================
        // H9: LobbyPresenter & WardrobePresenter
        // =========================================================================================
        private class MockLobbyView : ILobbyView
        {
            public bool IsVisible { get; set; } = true;
            public string BackgroundScene { get; private set; }
            public TimeOfDay TimeOfDay { get; private set; }
            public string PortraitArt { get; private set; }
            public string Greeting { get; private set; }
            public string LastError { get; private set; }

            public void SetBackground(string sceneId, TimeOfDay timeOfDay)
            {
                BackgroundScene = sceneId;
                TimeOfDay = timeOfDay;
            }
            public void SetCharacterPortrait(string artKey) => PortraitArt = artKey;
            public void SetGreeting(string greetingText) => Greeting = greetingText;
            public void ShowError(string message) => LastError = message;
        }

        [Test]
        public void H9_LobbyPresenter_RefreshesBasedOnTime_AndResolvesPortrait()
        {
            var lobby = new LobbyPresenter(_core);
            var view = new MockLobbyView();
            lobby.AttachView(view);
            lobby.Initialize();

            Assert.IsNotNull(view.BackgroundScene);
            Assert.IsNotNull(view.PortraitArt);

            // 測試點擊看板娘觸發問候
            lobby.OnCharacterClicked();
            Assert.IsTrue(view.Greeting.Contains("Greeting from"));

            // 陽性對照：嘗試換成不存在之角色，必須回傳錯誤
            var badRes = lobby.ChangeLobby("scene_cafe", "ghost_char_999");
            Assert.IsFalse(badRes.Ok);
            Assert.AreEqual("character_not_owned", view.LastError);
        }

        // =========================================================================================
        // H10: GachaPresenter & CharacterDetailPresenter
        // =========================================================================================
        private class MockGachaView : IGachaView
        {
            public bool IsVisible { get; set; } = true;
            public IReadOnlyList<GachaResultEntry> LastResults { get; private set; }
            public string LastError { get; private set; }

            public void ShowGachaResult(IReadOnlyList<GachaResultEntry> results) => LastResults = results;
            public void ShowError(string message) => LastError = message;
        }

        [Test]
        public void H10_GachaPresenter_PerformsPulls_AndDisplaysResults()
        {
            var gacha = new GachaPresenter(_core);
            var view = new MockGachaView();
            gacha.AttachView(view);

            // 正向：十連抽
            var res = gacha.Pull("banner_std", 10);
            Assert.IsTrue(res.Ok);
            Assert.IsNotNull(view.LastResults);
            Assert.AreEqual(10, view.LastResults.Count);

            // 陽性對照：抽出不存在的卡池
            var badRes = gacha.Pull("banner_nonexistent", 10);
            Assert.IsFalse(badRes.Ok);
            Assert.AreEqual("banner_not_found", view.LastError);
        }

        // =========================================================================================
        // H11: DialogManager & ToastService & RedDotService
        // =========================================================================================
        [Test]
        public void H11_DialogManager_HandlesQueue_AndCallbacks()
        {
            var dm = new DialogManager();
            bool confirmTriggered = false;
            bool cancelTriggered = false;

            // 1. Confirm 對話框
            dm.ShowConfirm("Title1", "Msg1", () => confirmTriggered = true, () => cancelTriggered = true);
            Assert.IsTrue(dm.HasActiveDialog);
            Assert.AreEqual("Title1", dm.CurrentDialog.Title);

            dm.Confirm();
            Assert.IsTrue(confirmTriggered);
            Assert.IsFalse(cancelTriggered);
            Assert.IsFalse(dm.HasActiveDialog);

            // 2. Alert 對話框
            bool alertClosed = false;
            dm.ShowAlert("AlertTitle", "AlertMsg", () => alertClosed = true);
            Assert.IsTrue(dm.HasActiveDialog);
            dm.Confirm();
            Assert.IsTrue(alertClosed);
            Assert.IsFalse(dm.HasActiveDialog);
        }

        [Test]
        public void H11_ToastService_QueuesDuringStory_AndFlushesAfterward()
        {
            var toast = new ToastService();
            var displayed = new List<string>();
            toast.ToastDisplayed += t => displayed.Add(t.Message);

            // 1. 一般狀態下即時顯示
            toast.ShowToast("Normal Toast 1");
            Assert.AreEqual(1, displayed.Count);
            Assert.AreEqual("Normal Toast 1", displayed[0]);

            // 2. 進入劇情模式：Toast 暫存於佇列（陽性對照：不觸發 ToastDisplayed）
            toast.IsInStoryPlayback = true;
            toast.ShowToast("Story Toast 1");
            toast.ShowToast("Story Toast 2");
            Assert.AreEqual(1, displayed.Count, "劇情中不應直接彈出 Toast");
            Assert.AreEqual(2, toast.PendingCount, "應有 2 則暫存 Toast");

            // 3. 離開劇情模式：批次釋放
            toast.IsInStoryPlayback = false;
            Assert.AreEqual(0, toast.PendingCount);
            Assert.AreEqual(3, displayed.Count);
            Assert.AreEqual("Story Toast 1", displayed[1]);
            Assert.AreEqual("Story Toast 2", displayed[2]);
        }

        // =========================================================================================
        // H12: AudioService
        // =========================================================================================
        private class MockAudioDriver : IAudioDriver
        {
            public string CurrentBgm { get; private set; }
            public float BgmVolume { get; private set; }
            public string LastSe { get; private set; }
            public float SeVolume { get; private set; }
            public string LastVoice { get; private set; }
            public float VoiceVolume { get; private set; }

            public void PlayBgm(string bgmKey, float volume, bool loop)
            {
                CurrentBgm = bgmKey;
                BgmVolume = volume;
            }
            public void StopBgm() => CurrentBgm = null;
            public void SetBgmVolume(float volume) => BgmVolume = volume;
            public void PlaySe(string seKey, float volume)
            {
                LastSe = seKey;
                SeVolume = volume;
            }
            public void PlayVoice(string voiceKey, float volume)
            {
                LastVoice = voiceKey;
                VoiceVolume = volume;
            }
            public void StopVoice() => LastVoice = null;
        }

        [Test]
        public void H12_AudioService_VolumeCalculation_AndBgmSwitching()
        {
            var driver = new MockAudioDriver();
            var audio = new AudioService(driver);

            // 1. 音量相乘計算
            audio.SetVolumes(master: 0.5f, bgm: 0.8f, se: 0.6f, voice: 1.0f);
            Assert.AreEqual(0.5f, audio.MasterVolume);
            Assert.AreEqual(0.4f, audio.EffectiveBgmVolume, 0.001f);
            Assert.AreEqual(0.3f, audio.EffectiveSeVolume, 0.001f);
            Assert.AreEqual(0.5f, audio.EffectiveVoiceVolume, 0.001f);

            // 2. 播放 BGM
            audio.PlayBgm("bgm_main");
            Assert.AreEqual("bgm_main", driver.CurrentBgm);
            Assert.AreEqual(0.4f, driver.BgmVolume, 0.001f);

            // 3. 播放 SE 與 Voice
            audio.PlaySe("se_click");
            Assert.AreEqual("se_click", driver.LastSe);
            Assert.AreEqual(0.3f, driver.SeVolume, 0.001f);

            audio.PlayVoice("voice_claire_001");
            Assert.AreEqual("voice_claire_001", driver.LastVoice);
            Assert.AreEqual(0.5f, driver.VoiceVolume, 0.001f);

            // 4. 設定即時連動
            audio.SetVolumes(master: 1.0f, bgm: 1.0f, se: 1.0f, voice: 1.0f);
            Assert.AreEqual(1.0f, driver.BgmVolume, 0.001f);
        }
    }
}
