#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.AgeGate;
using Game.Core.Audio;
using Game.Core.Localization;
using Game.Core.Persistence;
using Game.Core.Platform;
using Game.Core.Settings;
using Game.Core.UI;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Unity;
using Game.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    [TestFixture]
    public sealed class EncounterAchievementSettingsViewTests
    {
        private List<GameObject> _createdObjects;
        private MasterDatabase _master;
        private GameCore _core;
        private ScreenNavigator _nav;
        private AgeGateState _ageGate;
        private AudioService _audio;
        private FileSaveStore _saveStore;

        private sealed class SystemClock : IClock
        {
            public DateTime UtcNow => DateTime.UtcNow;
            public DateTime LocalNow => DateTime.Now;
        }

        private sealed class DummyAudioDriver : IAudioDriver
        {
            public void PlayBgm(string bgmKey, float volume, bool loop) { }
            public void StopBgm() { }
            public void SetBgmVolume(float volume) { }
            public void PlaySe(string seKey, float volume) { }
            public void PlayVoice(string voiceKey, float volume) { }
            public void StopVoice() { }
        }

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            PlayerState player = GameCore.NewPlayer(_master, clock);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            _core = new GameCore(_master, player, rng, clock, rating);

            if (!_core.Player.Characters.ContainsKey("char01_claire"))
            {
                _core.Player.Characters["char01_claire"] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Awaken = 1,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char01_s1" }
                };
            }

            _nav = new ScreenNavigator();
            _ageGate = new AgeGateState();
            _audio = new AudioService(new DummyAudioDriver());

            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_opt7_saves_" + Guid.NewGuid().ToString("N"));
            _saveStore = new FileSaveStore(tempDir);
            GameServices.Install(_core, _saveStore, new AssetService(rating), rating, new NullPlatformService(), null,
                navigator: _nav, audio: _audio, ageGate: _ageGate);
        }

        [TearDown]
        public void TearDown()
        {
            if (_createdObjects != null)
            {
                for (int i = 0; i < _createdObjects.Count; i++)
                {
                    if (_createdObjects[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                    }
                }
                _createdObjects.Clear();
            }
        }

        private GameObject Track(GameObject go)
        {
            _createdObjects.Add(go);
            return go;
        }

        [Test]
        public void OPT7_1_EncounterView_StartAndResolve_ConsumesStamina_IncreasesAffection_PreservesCharCount()
        {
            GameObject go = Track(new GameObject("TestEncounterView"));
            EncounterView encView = go.AddComponent<EncounterView>();
            encView.InitializeForTesting(_core);

            int staminaBefore = _core.Player.Encounter.Stamina;
            int initialCharCount = _core.Player.Characters.Count;
            int affBefore = _core.Player.Char("char04_elysia")?.Affection ?? 0;

            // 1. 啟動公園巧遇
            encView.StartSpot("spot_park");

            Assert.IsTrue(encView.IsEventModalOpen, "巧遇事件彈窗應開啟");
            Assert.AreEqual("spot_park", encView.SelectedSpotId);
            Assert.IsNotNull(encView.CurrentPendingEvent);
            Assert.AreEqual(staminaBefore - 1, _core.Player.Encounter.Stamina, "啟動巧遇應扣除 1 點體力");
            string pendingEventId = encView.CurrentPendingEvent.Id;

            // 1.5 驗證中途關閉彈窗後再次點擊 spot_park，會直接接續原 Pending 事件，且絕不重複扣除第 2 點體力
            encView.CloseModals();
            Assert.IsFalse(encView.IsEventModalOpen);
            encView.StartSpot("spot_park");
            Assert.IsTrue(encView.IsEventModalOpen, "再次點擊應重新開啟原 Pending 事件彈窗");
            Assert.AreEqual(pendingEventId, encView.CurrentPendingEvent.Id, "應接續同一個 Pending 事件");
            Assert.AreEqual(staminaBefore - 1, _core.Player.Encounter.Stamina, "接續 Pending 事件時體力絕對不會被扣除第二次");

            // 2. 進行選項結算 (choice0)
            encView.Choose("choice0");

            Assert.IsFalse(encView.IsEventModalOpen, "結算後事件彈窗應關閉");
            Assert.IsTrue(encView.IsResultModalOpen, "結算後收穫彈窗應開啟");
            Assert.IsNull(_core.Player.Encounter.Pending, "Pending 狀態應被清除");

            // 3. 驗證好感度增加
            int affAfter = _core.Player.Char("char04_elysia")?.Affection ?? 0;
            Assert.Greater(affAfter, affBefore, "約會結算後少女好感度應增加");

            // 4. 關鍵查核點：角色持有數量絕對不變（巧遇不招募角色！）
            int finalCharCount = _core.Player.Characters.Count;
            Assert.AreEqual(initialCharCount, finalCharCount, "巧遇事件僅增加好感與道具，絕不變更角色持有清單");

            encView.CloseModals();
            Assert.IsFalse(encView.IsResultModalOpen);
        }

        [Test]
        public void OPT7_2_AchievementView_ClaimUnlocked_IncreasesGems_LockedReturnsError_PositiveControl()
        {
            GameObject go = Track(new GameObject("TestAchievementView"));
            AchievementView achView = go.AddComponent<AchievementView>();
            achView.InitializeForTesting(_core);

            // 1. 陽性對照：嘗試領取尚未解鎖之真實成就 (ach_first_pull)，必須失敗並回傳 achievement_locked
            string lockedAchId = "ach_first_pull";
            achView.Claim(lockedAchId);
            Assert.AreEqual("achievement_locked", achView.LastError, "未解鎖成就呼叫領取應失敗");

            // 2. 解鎖成就 ach_first_pull
            long gemBefore = _core.Player.ItemCount("gem");
            _core.Player.Achievements[lockedAchId] = new AchievementProgress
            {
                Progress = 1,
                UnlockedAtUtc = GameCore.Iso(_core.Clock.UtcNow),
                Claimed = false
            };
            achView.Presenter.Refresh();

            // 3. 領取成就獎勵
            achView.Claim(lockedAchId);
            Assert.IsNotNull(achView.LastClaimedRewards, "應回傳領取之獎勵清單");
            Assert.IsTrue(_core.Player.Achievements[lockedAchId].Claimed, "成就應標記為已領取");

            // 4. 驗證寶石入帳
            long gemAfter = _core.Player.ItemCount("gem");
            Assert.Greater(gemAfter, gemBefore, "領取成就後寶石數量應增加");

            // 5. 重複領取應失敗
            achView.Claim(lockedAchId);
            Assert.AreEqual("already_claimed", achView.LastError, "已領取成就不可重複領取");
        }

        [Test]
        public void OPT7_3_SettingsModalView_VolumeAndLocale_And_AgeGateDefense()
        {
            GameObject go = Track(new GameObject("TestSettingsModal"));
            SettingsModalView setView = go.AddComponent<SettingsModalView>();
            setView.InitializeForTesting(_core, _audio, null, _ageGate);

            // 1. 調整音量
            setView.SetBgmVolume(0.65f);
            setView.SetVoiceVolume(0.85f);
            Assert.AreEqual(0.65f, setView.BgmVolume, 0.001f);
            Assert.AreEqual(0.85f, setView.VoiceVolume, 0.001f);
            Assert.AreEqual(0.65f, _audio.BgmVolume, 0.001f, "BgmVolume 應同步至 AudioService");
            Assert.AreEqual(0.85f, _audio.VoiceVolume, 0.001f, "VoiceVolume 應同步至 AudioService");

            // 2. 切換語言並驗證 ILocalizationService.LocaleChanged 觸發
            var locService = new LocalizationService();
            bool localeChangedFired = false;
            string changedToLocale = null;
            locService.LocaleChanged += loc => { localeChangedFired = true; changedToLocale = loc; };
            setView.InitializeForTesting(_core, _audio, locService, _ageGate);

            setView.SelectLocale("en");
            Assert.AreEqual("en", setView.SelectedLocale);
            Assert.AreEqual("en", _core.Player.Profile.Locale);
            Assert.IsTrue(localeChangedFired, "切換語言應觸發 ILocalizationService.LocaleChanged 事件");
            Assert.AreEqual("en", changedToLocale);

            // 3. 內容分級防線：未通過 AgeGate 前嘗試選擇 R18
            Assert.IsFalse(_ageGate.IsVerified);
            setView.SelectRating(Rating.R18);

            // 驗證彈出 18+ 確認視窗，但分級依然維持 G
            Assert.IsTrue(setView.IsAgeGateModalOpen, "未驗證年齡前切換 R18 應彈出年齡確認視窗");
            Assert.AreEqual(Rating.G, setView.SelectedRating, "未點選確認前分級必須維持 G");

            // 4. 取消年齡確認
            setView.CancelAgeGate();
            Assert.IsFalse(setView.IsAgeGateModalOpen);
            Assert.AreEqual(Rating.G, setView.SelectedRating);

            // 5. 再次切換並點選「我已年滿 18 歲」
            setView.SelectRating(Rating.R18);
            Assert.IsTrue(setView.IsAgeGateModalOpen);
            setView.ConfirmAgeGate();

            Assert.IsFalse(setView.IsAgeGateModalOpen);
            Assert.IsTrue(_ageGate.IsVerified, "年齡狀態應已通過驗證");
            Assert.AreEqual(Rating.R18, setView.SelectedRating, "年齡驗證通過後分級升級為 R18");
            Assert.AreEqual(Rating.R18, _core.Player.Profile.RatingOptIn);
        }

        [Test]
        public void OPT7_4_Lobby_NavigationToEncounterAchievementAndSettings()
        {
            GameObject lobbyGo = Track(new GameObject("TestLobby"));
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.Initialize(_core);

            _nav.Push(lobbyView);
            Assert.AreEqual(1, _nav.Count);

            // 1. 開啟巧遇地圖
            lobbyView.OpenEncounter();
            Assert.AreEqual(2, _nav.Count);
            Assert.IsInstanceOf<EncounterView>(_nav.CurrentScreen);
            Assert.IsTrue(_nav.HandleBackRequest());
            Assert.AreSame(lobbyView, _nav.CurrentScreen);

            // 2. 開啟榮譽成就
            lobbyView.OpenAchievements();
            Assert.AreEqual(2, _nav.Count);
            Assert.IsInstanceOf<AchievementView>(_nav.CurrentScreen);
            Assert.IsTrue(_nav.HandleBackRequest());
            Assert.AreSame(lobbyView, _nav.CurrentScreen);

            // 3. 開啟系統設定
            lobbyView.OpenSettings();
            Assert.AreEqual(2, _nav.Count);
            Assert.IsInstanceOf<SettingsModalView>(_nav.CurrentScreen);
            Assert.IsTrue(_nav.HandleBackRequest());
            Assert.AreSame(lobbyView, _nav.CurrentScreen);
        }

        [Test]
        public void M2_2_EncounterView_SpotShrine_And_PlayEncounterStory_BridgesToStoryPlayer()
        {
            GameObject go = Track(new GameObject("TestEncounterViewM2"));
            EncounterView encView = go.AddComponent<EncounterView>();
            encView.InitializeForTesting(_core);

            // 1. 陽性對照：未解鎖 spot_shrine 時呼叫 StartSpot 應失敗
            encView.StartSpot("spot_shrine");
            Assert.AreEqual("spot_locked", encView.LastError, "未解鎖 spot_shrine 時應回傳 spot_locked");

            // 2. 解鎖 spot_shrine 並確保持有 char18_sakura_miko
            _core.Player.Encounter.UnlockedSpots.Add("spot_shrine");
            if (_core.Player.Char("char18_sakura_miko") == null)
            {
                _core.Player.Characters["char18_sakura_miko"] = new CharacterState
                {
                    CurrentRarity = "SSR",
                    Level = 1,
                    Awaken = 1,
                    UnlockedPortraitStages = new HashSet<string> { "ps_char18_s1" }
                };
            }

            encView.Refresh();
            encView.StartSpot("spot_shrine");
            Assert.IsTrue(encView.IsEventModalOpen, "解鎖後點選 spot_shrine 應開啟事件彈窗");
            Assert.IsNotNull(encView.WatchStoryButton, "事件彈窗應具備觀看巧遇劇情按鈕");

            // 3. 點擊觀看巧遇劇情，驗證橋接載入 encounter_shrine.txt 至 StoryPlayerView
            GameObject storyGo = Track(new GameObject("TestStoryPlayerM2"));
            StoryPlayerView storyPlayer = storyGo.AddComponent<StoryPlayerView>();
            bool played = encView.PlayEncounterStory(storyPlayer);
            Assert.IsTrue(played, "PlayEncounterStory 應成功啟動劇本播放");
            Assert.AreEqual("encounter_shrine.txt", encView.LastPlayedStoryScriptId);
            Assert.AreEqual("Sakura", storyPlayer.CurrentSpeaker);
        }

        [Test]
        public void M2_5_AchievementView_ClaimAll_ClaimsAllUnlockedAchievements_AndClearsRedDot()
        {
            var redDotService = new RedDotService(_core);
            GameObject go = Track(new GameObject("TestAchievementViewM2"));
            AchievementView achView = go.AddComponent<AchievementView>();
            achView.InitializeForTesting(_core);

            // 解鎖 3 個成就 (包含 M2-5 新增的 ach_ch2_clear 與 ach_advance_any)
            string[] toUnlock = new[] { "ach_first_pull", "ach_ch2_clear", "ach_advance_any" };
            foreach (string id in toUnlock)
            {
                _core.Player.Achievements[id] = new AchievementProgress
                {
                    Progress = 1,
                    UnlockedAtUtc = GameCore.Iso(_core.Clock.UtcNow),
                    Claimed = false
                };
            }
            achView.Presenter.Refresh();
            Assert.IsTrue(redDotService.HasClaimableAchievements(), "領取前應有紅點提示");

            long gemBefore = _core.Player.ItemCount("gem");
            int claimedCount = achView.ClaimAll();
            Assert.AreEqual(3, claimedCount, "ClaimAll 應一次領取全部 3 個已解鎖成就");

            foreach (string id in toUnlock)
            {
                Assert.IsTrue(_core.Player.Achievements[id].Claimed, $"成就 {id} 應標記為已領取");
            }
            Assert.Greater(_core.Player.ItemCount("gem"), gemBefore, "一鍵全領後寶石總數應增加");
            Assert.IsFalse(redDotService.HasClaimableAchievements(), "一鍵全領後成就紅點應立即歸零");
            Assert.AreEqual(0, achView.ClaimAll(), "再次點擊 ClaimAll 應回傳 0 且不拋錯");
        }

        [Test]
        public void M3_4_3_SettingsModalView_SwitchLocale_JaAndZhCn_UpdatesLobbyNavText()
        {
            var loc = new LocalizationService("zh-TW");
            string locDir = System.IO.Path.Combine(TestPaths.DataDir(), "loc");
            loc.LoadFromDirectory(locDir);

            // 重新安裝服務，注入包含 4 語系的 LocalizationService
            GameServices.Install(_core, _saveStore, new AssetService(_core.Rating), _core.Rating, new NullPlatformService(), navigator: _nav, localization: loc);

            var lobbyGo = Track(new GameObject("Test_LobbyUI_M3_4"));
            var lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.EnsureUIHierarchy();
            lobbyView.Initialize(_core);

            var settingsGo = Track(new GameObject("Test_SettingsModalUI_M3_4"));
            var settingsView = settingsGo.AddComponent<SettingsModalView>();
                        settingsView.Initialize(_core, _audio, loc, _ageGate);

            // 1. 切換至 ja (日文)
            settingsView.SelectLocale("ja");
            Assert.AreEqual("ja", settingsView.SelectedLocale);
            Assert.AreEqual("ja", _core.Player.Profile.Locale);
            Text storyTxt = lobbyGo.transform.Find("RightNavigationPanel/BtnStory/Title")?.GetComponent<Text>();
            Assert.IsNotNull(storyTxt);
            Assert.AreEqual("📖 ストーリー", storyTxt.text, "切換至 ja 時大廳劇情按鈕應即時更新為日文");

            // 2. 切換至 zh-CN (簡體中文)
            settingsView.SelectLocale("zh-CN");
            Assert.AreEqual("zh-CN", settingsView.SelectedLocale);
            Assert.AreEqual("zh-CN", _core.Player.Profile.Locale);
            Assert.AreEqual("📖 故事剧情", storyTxt.text, "切換至 zh-CN 時大廳劇情按鈕應即時更新為簡體中文");

            // 3. 恢復至 zh-TW
            settingsView.SelectLocale("zh-TW");
            Assert.AreEqual("📖 故事劇情", storyTxt.text, "切換回 zh-TW 時大廳劇情按鈕應即時更新為繁體中文");
        }
    }
}
#endif
