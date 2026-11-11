#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
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
    public sealed class CharacterAndWardrobeViewTests
    {
        private List<GameObject> _createdObjects;
        private MasterDatabase _master;
        private GameCore _core;

        private sealed class SystemClock : IClock
        {
            public DateTime UtcNow => DateTime.UtcNow;
            public DateTime LocalNow => DateTime.Now;
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

            var nav = new ScreenNavigator();
            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_opt5_saves_" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(tempDir);
            GameServices.Install(_core, store, new AssetService(rating), rating, new NullPlatformService(), null, navigator: nav);
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
        public void OPT5_1_CharacterRoster_LevelUp_Awaken_And_Advance_UpdatesUiAndState()
        {
            string charId = "char01_claire";
            CharacterState c = _core.Player.Char(charId);
            Assert.IsNotNull(c, "初始玩家應持有 Claire");

            // 給予充足的經驗藥水、金幣、碎片與同卡
            _core.Player.AddItem("exp_l", 200);
            _core.Player.AddItem("gold", 1000000);
            _core.Player.AddItem("shard_char01_claire", 500);
            _core.Player.AddItem("awaken_shard_char01_claire", 500);
            _core.Player.AddItem("char_char01_claire", 5);

            GameObject go = Track(new GameObject("TestCharacterRoster"));
            CharacterRosterView rosterView = go.AddComponent<CharacterRosterView>();
            rosterView.InitializeForTesting(_core);
            rosterView.SelectCharacter(charId);

            // 1. 驗證初始狀態 (Lv.1, SSR, Awaken=1)
            Assert.AreEqual(1, c.Level);
            Assert.AreEqual("SSR", c.DisplayGrade);
            Assert.AreEqual(1, c.Awaken);
            StringAssert.Contains("LV. 1", rosterView.LevelLabel.text);
            StringAssert.Contains("SSR", rosterView.GradeLabel.text);

            // 2. 升級至 Lv.30
            c.Level = 30;
            rosterView.SelectCharacter(charId);
            Assert.AreEqual(30, c.Level, "角色等級應設為 Lv.30");
            StringAssert.Contains("LV. 30", rosterView.LevelLabel.text);

            // 3. 覺醒至 ★2 (解鎖 S2 立繪條件: Lv.30 + Awaken 2)
            c.Awaken = 1;
            var awakenRes = _core.Awaken(charId);
            Assert.IsTrue(awakenRes.Ok, "覺醒應成功");
            rosterView.SelectCharacter(charId);

            Assert.AreEqual(2, c.Awaken, "角色星級應為 ★2");
            StringAssert.Contains("★★", rosterView.AwakenLabel.text);
            Assert.IsTrue(c.UnlockedPortraitStages.Contains("ps_char01_s2"), "達 Lv30 ★2 後應自動解鎖 S2 立繪 (ps_char01_s2)");

            // 4. 同卡進階 (Advance -> SSR+1)
            var advRes = _core.AdvanceCharacter(charId);
            Assert.IsTrue(advRes.Ok, "進階應成功");
            rosterView.SelectCharacter(charId);

            Assert.AreEqual("SSR+1", c.DisplayGrade, "進階後品階應為 SSR+1");
            StringAssert.Contains("SSR+1", rosterView.GradeLabel.text);
        }

        [Test]
        public void OPT5_2_Wardrobe_SwitchStage_And_SetLobby_UpdatesLobbySecretary()
        {
            string charId = "char01_claire";
            CharacterState c = _core.Player.Char(charId);
            Assert.IsNotNull(c);

            // 解鎖場景與 S2 立繪
            _core.Player.Scenes.Add("scene_cafe");
            c.UnlockedPortraitStages.Add("ps_char01_s2");

            GameObject wardrobeGo = Track(new GameObject("TestWardrobe"));
            WardrobeView wardrobeView = wardrobeGo.AddComponent<WardrobeView>();
            wardrobeView.InitializeForTesting(_core);
            wardrobeView.SelectCharacter(charId);

            // 1. 切換立繪至 S2
            wardrobeView.SelectPortraitStage("ps_char01_s2");
            Assert.AreEqual("ps_char01_s2", c.PortraitStage, "角色立繪階段應切換至 S2");
            Assert.AreEqual("PORTRAIT_STAGE_UPDATED", wardrobeView.LastMessage);

            // 2. 切換大廳背景場景
            wardrobeView.SelectScene("scene_cafe");
            Assert.AreEqual("scene_cafe", wardrobeView.SelectedSceneId);

            // 3. 點擊「設為大廳秘書官」
            wardrobeView.OnClickSetAsLobby();
            Assert.AreEqual("SET_LOBBY_SUCCESS", wardrobeView.LastMessage);
            Assert.AreEqual(charId, _core.Player.Lobby.Character, "大廳秘書官應設為 Claire");
            Assert.AreEqual("scene_cafe", _core.Player.Lobby.Scene, "大廳場景應設為 scene_cafe");

            // 4. 連動 LobbyView: OnResume 刷新
            GameObject lobbyGo = Track(new GameObject("TestLobby"));
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.Initialize(_core);
            lobbyView.OnResume();

            // 驗證大廳設定已生效
            Assert.AreEqual(charId, _core.Player.Lobby.Character);
            Assert.AreEqual("scene_cafe", _core.Player.Lobby.Scene);
        }

        [Test]
        public void OPT5_3_Wardrobe_SetUnownedCharacterAsLobby_Fails_PositiveControl()
        {
            GameObject wardrobeGo = Track(new GameObject("TestWardrobe"));
            WardrobeView wardrobeView = wardrobeGo.AddComponent<WardrobeView>();
            wardrobeView.InitializeForTesting(_core);

            // 陽性對照：對未持有角色呼叫設定大廳秘書官應失敗
            string unownedCharId = "char_unowned_dummy";
            var wardrobePresenter = new WardrobePresenter(_core);
            wardrobePresenter.AttachView(wardrobeView);

            var result = wardrobePresenter.SetAsLobbySecretary(unownedCharId, "scene_lobby_default");
            Assert.IsFalse(result.Ok, "未持有角色設定秘書官應失敗");
            Assert.AreEqual("character_not_owned", result.Error);
            Assert.AreEqual("character_not_owned", wardrobeView.LastError);
        }

        [Test]
        public void OPT5_4_Lobby_NavigationToRosterAndWardrobe_PushesScreensCorrectly()
        {
            GameObject lobbyGo = Track(new GameObject("TestLobby"));
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.Initialize(_core);

            var nav = GameServices.Navigator;
            nav.Push(lobbyView);
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(lobbyView, nav.CurrentScreen);

            // 1. 開啟角色名冊
            lobbyView.OpenCharacterRoster();
            Assert.AreEqual(2, nav.Count);
            Assert.IsInstanceOf<CharacterRosterView>(nav.CurrentScreen);

            // 按下返回鍵回到大廳
            bool back1 = nav.HandleBackRequest();
            Assert.IsTrue(back1);
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(lobbyView, nav.CurrentScreen);

            // 2. 開啟換裝衣櫥
            lobbyView.OpenWardrobe();
            Assert.AreEqual(2, nav.Count);
            Assert.IsInstanceOf<WardrobeView>(nav.CurrentScreen);

            // 按下返回鍵回到大廳
            bool back2 = nav.HandleBackRequest();
            Assert.IsTrue(back2);
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(lobbyView, nav.CurrentScreen);
        }

        [Test]
        public void M2_4_WardrobeView_EquipCostume_And_SelectTimeOfDay_UpdatesLobbyTint()
        {
            string charId = "char01_claire";
            GameObject lobbyGo = Track(new GameObject("TestLobbyM2"));
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.EnsureUIHierarchy();
            lobbyView.Initialize(_core);

            GameObject wardrobeGo = Track(new GameObject("TestWardrobeM2"));
            WardrobeView wardrobeView = wardrobeGo.AddComponent<WardrobeView>();
            wardrobeView.InitializeForTesting(_core);
            wardrobeView.SelectCharacter(charId);

            // 1. 陽性對照：穿戴未解鎖服裝應失敗並回傳 costume_locked
            _core.Player.Costumes.Remove("cos_char01_academy");
            var failEquip = wardrobeView.EquipCostume("cos_char01_academy");
            Assert.IsFalse(failEquip.Ok, "未解鎖服裝不可穿戴");
            Assert.AreEqual("costume_locked", failEquip.Error);

            // 2. 解鎖服裝後穿戴應成功並更新角色 Costume
            _core.Player.Costumes.Add("cos_char01_academy");
            wardrobeView.SelectCharacter(charId);
            var okEquip = wardrobeView.EquipCostume("cos_char01_academy");
            Assert.IsTrue(okEquip.Ok, "已解鎖服裝應成功穿戴");
            Assert.AreEqual("cos_char01_academy", _core.Player.Char(charId).Costume);

            // 3. 切換四時段燈光並驗證 LobbyView 背景色調即時連動
            wardrobeView.SelectTimeOfDay("evening");
            Assert.AreEqual("evening", wardrobeView.SelectedTimeOfDay);
            Assert.AreEqual(new Color(1f, 0.88f, 0.82f, 1f), lobbyView.CurrentBackgroundTint, "切換至夕暮應連動更新大廳暖橘色調");

            wardrobeView.SelectTimeOfDay("night");
            Assert.AreEqual("night", wardrobeView.SelectedTimeOfDay);
            Assert.AreEqual(new Color(0.78f, 0.82f, 1f, 1f), lobbyView.CurrentBackgroundTint, "切換至夜間應連動更新大廳冷藍色調");
        }

        [Test]
        public void CharacterRosterAndWardrobe_ShareStreamingSpriteCache_AndResolveLocalizedName()
        {
            var rosterGo = Track(new GameObject("TestRosterSharedCache"));
            var rosterView = rosterGo.AddComponent<CharacterRosterView>();
            rosterView.InitializeForTesting(_core);
            rosterView.SelectCharacter("char01_claire");

            var wardrobeGo = Track(new GameObject("TestWardrobeSharedCache"));
            var wardrobeView = wardrobeGo.AddComponent<WardrobeView>();
            wardrobeView.InitializeForTesting(_core);
            wardrobeView.SelectCharacter("char01_claire");

            Image rosterPortrait = rosterGo.transform.Find("CharacterRosterCanvas/CenterPanel/Portrait").GetComponent<Image>();
            Image wardrobePortrait = wardrobeGo.transform.Find("WardrobeCanvas/CenterPanel/Portrait").GetComponent<Image>();
            Assert.IsNotNull(rosterPortrait.sprite);
            Assert.AreSame(rosterPortrait.sprite, wardrobePortrait.sprite, "名冊與更衣室應透過 AssetService 共用同一個 Sprite 快取實例");
            Assert.AreSame(rosterPortrait.sprite.texture, wardrobePortrait.sprite.texture, "應共用同一個 Texture2D 原生紋理實例");

            Text rosterTitle = rosterGo.transform.Find("CharacterRosterCanvas/CenterPanel/CharName").GetComponent<Text>();
            Text wardrobeTitle = wardrobeGo.transform.Find("WardrobeCanvas/CenterPanel/CharTitle").GetComponent<Text>();
            Assert.AreEqual("克萊兒", rosterTitle.text, "角色名稱應解析為中文譯名而非 LOC:/... 原文");
            Assert.AreEqual("克萊兒", wardrobeTitle.text, "更衣室角色名稱應解析為中文譯名");
        }
    }
}
#endif
