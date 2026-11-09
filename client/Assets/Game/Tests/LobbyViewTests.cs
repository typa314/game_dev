#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.Audio;
using Game.Core.Persistence;
using Game.Core.Platform;
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
    public sealed class LobbyViewTests
    {
        private GameObject _uiHolder;
        private LobbyView _lobbyView;
        private GameCore _core;
        private string _tempSaveDir;

        [SetUp]
        public void SetUp()
        {
            _uiHolder = new GameObject("Test_LobbyUI");
            _lobbyView = _uiHolder.AddComponent<LobbyView>();

            // 建立測試用 GameCore
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            PlayerState player = GameCore.NewPlayer(master, clock);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            _core = new GameCore(master, player, rng, clock, rating);

            _tempSaveDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_lobby_saves_" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(_tempSaveDir);
            GameServices.Install(_core, store, new AssetService(rating), rating, new NullPlatformService(), null);
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.Clear();
            if (_uiHolder != null)
            {
                UnityEngine.Object.DestroyImmediate(_uiHolder);
            }
            if (!string.IsNullOrEmpty(_tempSaveDir) && System.IO.Directory.Exists(_tempSaveDir))
            {
                try { System.IO.Directory.Delete(_tempSaveDir, true); } catch { }
            }
        }

        [Test]
        public void M1_Lobby_1_AutoBuildsUIHierarchy_WithAllRequiredComponents()
        {
            // 驗證 EnsureUIHierarchy 自動構建所有關鍵節點
            _lobbyView.EnsureUIHierarchy();

            Canvas canvas = _uiHolder.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, "應自動掛載 Canvas");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);

            CanvasScaler scaler = _uiHolder.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "應自動掛載 CanvasScaler");
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);

            ResolutionAdapter adapter = _uiHolder.GetComponent<ResolutionAdapter>();
            Assert.IsNotNull(adapter, "應自動掛載 F10 解析度自適應組件");

            // 檢查關鍵子節點
            Transform bgPanel = _uiHolder.transform.Find("BackgroundPanel");
            Assert.IsNotNull(bgPanel, "應建立 BackgroundPanel");

            Transform charArea = _uiHolder.transform.Find("CharacterStandArea");
            Assert.IsNotNull(charArea, "應建立 CharacterStandArea");
            Assert.IsNotNull(charArea.GetComponent<Button>(), "看板娘應有點擊感應按鈕");

            Transform headerBar = _uiHolder.transform.Find("TopHeaderBar");
            Assert.IsNotNull(headerBar, "應建立 TopHeaderBar");

            Transform rightNav = _uiHolder.transform.Find("RightNavigationPanel");
            Assert.IsNotNull(rightNav, "應建立 RightNavigationPanel");

            Transform battleBtn = _uiHolder.transform.Find("BtnBattleAction");
            Assert.IsNotNull(battleBtn, "應建立 BtnBattleAction 出擊大按鈕");

            Transform speechBubble = _uiHolder.transform.Find("SpeechBubble");
            Assert.IsNotNull(speechBubble, "應建立 SpeechBubble");
        }

        [Test]
        public void M1_Lobby_2_SetBackground_AppliesTimeOfDayLightingTints()
        {
            _lobbyView.EnsureUIHierarchy();
            Image bg = _uiHolder.transform.Find("BackgroundPanel").GetComponent<Image>();

            _lobbyView.SetBackground("scene_lobby_default", TimeOfDay.Morning);
            Assert.AreEqual(new Color(1f, 0.97f, 0.90f, 1f), bg.color, "早晨應有暖金朝目光照");

            _lobbyView.SetBackground("scene_lobby_default", TimeOfDay.Afternoon);
            Assert.AreEqual(Color.white, bg.color, "午後應為澄澈白光");

            _lobbyView.SetBackground("scene_lobby_default", TimeOfDay.Evening);
            Assert.AreEqual(new Color(1f, 0.88f, 0.82f, 1f), bg.color, "夕暮應有暖橘紅霞光照");

            _lobbyView.SetBackground("scene_lobby_default", TimeOfDay.Night);
            Assert.AreEqual(new Color(0.78f, 0.82f, 1f, 1f), bg.color, "夜間應為靜謐冷藍光照");
        }

        [Test]
        public void M1_Lobby_3_SetGreeting_ActivatesSpeechBubble()
        {
            _lobbyView.EnsureUIHierarchy();
            GameObject bubble = _uiHolder.transform.Find("SpeechBubble").gameObject;
            Text bubbleText = bubble.GetComponentInChildren<Text>();

            Assert.IsFalse(bubble.activeSelf, "初始對話氣泡應為隱藏");

            _lobbyView.SetGreeting("指揮官，今天也要加油喔！");
            Assert.IsTrue(bubble.activeSelf, "呼叫問候後對話氣泡應立即激活顯示");
            Assert.AreEqual("指揮官，今天也要加油喔！", bubbleText.text);
        }

        [Test]
        public void M1_Lobby_4_RefreshPlayerHeader_BindsDomainStateCorrectly()
        {
            _lobbyView.EnsureUIHierarchy();
            _lobbyView.Initialize(_core);

            Text goldText = _uiHolder.transform.Find("TopHeaderBar/GoldLabel").GetComponent<Text>();
            Text gemText = _uiHolder.transform.Find("TopHeaderBar/GemLabel").GetComponent<Text>();
            Text staText = _uiHolder.transform.Find("TopHeaderBar/StaminaLabel").GetComponent<Text>();

            // 驗證初始道具數值 (純數字，圖片由 Icon 節點呈現)
            Assert.AreEqual($"{_core.Player.ItemCount("gold"):N0}", goldText.text);
            Assert.AreEqual($"{_core.Player.ItemCount("gem"):N0}", gemText.text);
            Assert.AreEqual($"{_core.Player.Encounter.Stamina}/5", staText.text);

            // 驗證右上角膠囊均已嵌入獨立彩色 Icon 節點
            Assert.IsNotNull(_uiHolder.transform.Find("TopHeaderBar/GoldLabel/Icon"), "金幣膠囊應包含獨立 Icon 圖片節點");
            Assert.IsNotNull(_uiHolder.transform.Find("TopHeaderBar/GemLabel/Icon"), "鑽石膠囊應包含獨立 Icon 圖片節點");
            Assert.IsNotNull(_uiHolder.transform.Find("TopHeaderBar/StaminaLabel/Icon"), "體力膠囊應包含獨立 Icon 圖片節點");

            // 驗證契約招募入口具有專屬召喚水晶圖示與 UP! 特選標籤
            Assert.IsNotNull(_uiHolder.transform.Find("RightNavigationPanel/BtnGacha/NavIcon"), "主介面契約招募應嵌入專屬召喚圖示");
            Assert.IsNotNull(_uiHolder.transform.Find("RightNavigationPanel/BtnGacha/UpBadge"), "主介面契約招募應具有 UP! 特選角標");

            // 修改道具並觸發刷新
            _core.Player.AddItem("gold", 8888);
            _core.Player.AddItem("gem", 999);
            _lobbyView.RefreshPlayerHeader();

            Assert.AreEqual($"{_core.Player.ItemCount("gold"):N0}", goldText.text);
            Assert.AreEqual($"{_core.Player.ItemCount("gem"):N0}", gemText.text);
        }

        [Test]
        public void M1_Lobby_5_CharacterTapped_InvokesPresenterGreeting()
        {
            _lobbyView.EnsureUIHierarchy();
            _lobbyView.Initialize(_core);

            GameObject bubble = _uiHolder.transform.Find("SpeechBubble").gameObject;
            Text bubbleText = bubble.GetComponentInChildren<Text>();

            _lobbyView.OnCharacterTapped();

            Assert.IsTrue(bubble.activeSelf, "點擊看板娘應觸發問候對話氣泡顯示");
            Assert.IsTrue(bubbleText.text.Contains("Greeting from"), "氣泡文字應包含問候內容");
        }

        [Test]
        public void M2_7_LobbyView_And_StageSelectView_LocaleChanged_RefreshesUiLabelsInRealTime()
        {
            var loc = new Game.Core.Localization.LocalizationService();
            string locDir = System.IO.Path.Combine(TestPaths.MasterDataDir(), "..", "loc");
            loc.LoadFromDirectory(locDir);

            var rating = new RatingPolicy(Rating.G, Rating.G);
            var store = new FileSaveStore(_tempSaveDir);
            GameServices.Install(_core, store, new AssetService(rating), rating, new NullPlatformService(), null, localization: loc);

            _lobbyView.EnsureUIHierarchy();
            _lobbyView.Initialize(_core);

            GameObject stageGo = new GameObject("TestStageSelectLoc");
            StageSelectView stageView = stageGo.AddComponent<StageSelectView>();
            stageView.InitializeForTesting(_core);

            try
            {
                loc.SetLocale("zh-TW");
                _lobbyView.ApplyLocalizedTexts();
                stageView.ApplyLocalizedTexts();
                StringAssert.Contains("出擊作戰", _lobbyView.BattleButtonText);
                StringAssert.Contains("作戰選關", stageView.TitleText);

                loc.SetLocale("en");
                StringAssert.Contains("Sortie Battle", _lobbyView.BattleButtonText);
                StringAssert.Contains("Stage Select", stageView.TitleText);

                loc.SetLocale("zh-TW");
                StringAssert.Contains("出擊作戰", _lobbyView.BattleButtonText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stageGo);
            }
        }

        private sealed class RecordingDriver : IAudioDriver
        {
            public readonly List<string> Voices = new List<string>();
            public void PlayBgm(string k, float v, bool l) { }
            public void StopBgm() { }
            public void SetBgmVolume(float v) { }
            public void PlaySe(string k, float v) { }
            public void PlayVoice(string k, float v) { Voices.Add(k); }
            public void StopVoice() { }
        }

        [Test]
        public void M3_Fix_SpriteCache_ReusesTexture_And_LogsWarningOnException()
        {
            _lobbyView.EnsureUIHierarchy();
            Image charImg = _uiHolder.transform.Find("CharacterStandArea").GetComponent<Image>();
            Image bgImg1 = _uiHolder.transform.Find("BackgroundPanel").GetComponent<Image>();

            _lobbyView.SetCharacterPortrait("char01_claire");
            Sprite firstCharSprite = charImg.sprite;
            Assert.IsNotNull(firstCharSprite, "應成功載入 char01_claire 立繪");

            _lobbyView.SetCharacterPortrait("char01_claire");
            Sprite secondCharSprite = charImg.sprite;

            // 檢查第 1 點：重複載入同一角色立繪與背景時，應重用快取的 Sprite 與 Texture2D，不重複分配
            Assert.AreSame(firstCharSprite, secondCharSprite, "重複設定同一角色立繪應回傳快取的同一個 Sprite 實例");
            Assert.AreSame(firstCharSprite.texture, secondCharSprite.texture, "重複設定同一角色立繪應重用同一個 Texture2D 實例");

            GameObject secondHolder = new GameObject("Test_LobbyUI_2");
            try
            {
                LobbyView secondLobby = secondHolder.AddComponent<LobbyView>();
                secondLobby.EnsureUIHierarchy();
                Image bgImg2 = secondHolder.transform.Find("BackgroundPanel").GetComponent<Image>();
                Assert.AreSame(bgImg1.sprite, bgImg2.sprite, "背景圖應重用快取的同一個 Sprite 實例");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondHolder);
            }

            // 檢查第 3 點：當路徑含非法字元拋出例外時，catch 塊應記錄 Debug.LogWarning 而非靜默吞掉
            var warnings = new List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) =>
            {
                if (type == LogType.Warning)
                {
                    warnings.Add(condition);
                }
            };

            Application.logMessageReceived += handler;
            try
            {
                Sprite badIcon = LobbyView.TryLoadIconSprite("bad\0icon");
                Assert.IsNull(badIcon);
                _lobbyView.SetCharacterPortrait("bad\0char");
                string badScript = StoryHubView.TryReadStoryScriptText("bad\0script.txt");
                Assert.IsNull(badScript);
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            Assert.GreaterOrEqual(warnings.Count, 3, "載入圖片與劇本發生例外時應輸出至少 3 筆 Debug.LogWarning");
            StringAssert.Contains("[LobbyView]", warnings[0]);
            StringAssert.Contains("[StoryHubView]", warnings[warnings.Count - 1]);
        }

        [Test]
        public void M1_Lobby_6_CharacterTapped_AlternatesTapVoices_WithoutRealAudio()
        {
            var rec = new RecordingDriver();
            GameServices.Clear();
            var rating = new RatingPolicy(Rating.G, Rating.G);
            GameServices.Install(_core, new FileSaveStore(_tempSaveDir), new AssetService(rating), rating, new NullPlatformService(), audio: new AudioService(rec));

            _lobbyView.EnsureUIHierarchy();
            _lobbyView.Initialize(_core);
            _lobbyView.OnCharacterTapped();
            _lobbyView.OnCharacterTapped();
            _lobbyView.OnCharacterTapped();

            CollectionAssert.AreEqual(
                new[] { "char18_sakura_miko/tap_1", "char18_sakura_miko/tap_2", "char18_sakura_miko/tap_1" },
                rec.Voices);
        }
    }
}
#endif
