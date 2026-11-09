#if UNITY_2021_1_OR_NEWER
using System;
using System.Collections.Generic;
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
    public sealed class ProceduralUiAndNavigationTests
    {
        private class TestScreenA : UnityScreenBase
        {
            public override string ScreenId => "Test_ScreenA";
            public int PushCount { get; private set; }
            public int PauseCount { get; private set; }
            public int ResumeCount { get; private set; }
            public int PopCount { get; private set; }

            public override void OnPush(object args)
            {
                base.OnPush(args);
                PushCount++;
            }

            public override void OnPause()
            {
                base.OnPause();
                PauseCount++;
            }

            public override void OnResume()
            {
                base.OnResume();
                ResumeCount++;
            }

            public override void OnPop()
            {
                base.OnPop();
                PopCount++;
            }
        }

        private class TestScreenB : UnityScreenBase
        {
            public override string ScreenId => "Test_ScreenB";
            public int PushCount { get; private set; }
            public int PauseCount { get; private set; }
            public int ResumeCount { get; private set; }
            public int PopCount { get; private set; }
            public bool HasSubModal { get; set; }

            public override void OnPush(object args)
            {
                base.OnPush(args);
                PushCount++;
            }

            public override void OnPause()
            {
                base.OnPause();
                PauseCount++;
            }

            public override void OnResume()
            {
                base.OnResume();
                ResumeCount++;
            }

            public override void OnPop()
            {
                base.OnPop();
                PopCount++;
            }

            public override bool OnBackRequested()
            {
                if (HasSubModal)
                {
                    HasSubModal = false;
                    return true;
                }
                return false;
            }
        }

        [TearDown]
        public void TearDown()
        {
            ProceduralUiKit.ClearCache();
            GameServices.Clear();
        }

        [Test]
        public void OPT2_1_ProceduralUiKit_SpriteCaching_ReusesSameInstanceAndTracksCount()
        {
            ProceduralUiKit.ClearCache();
            Assert.AreEqual(0, ProceduralUiKit.CachedSpriteCount, "清空快取後計數應為 0");

            // 1. Pill Sprite 快取測試
            Sprite pill1 = ProceduralUiKit.GetPillSprite(120, 36, Color.black, Color.cyan);
            Assert.IsNotNull(pill1, "第一次取得 Pill Sprite 不應為 null");
            Assert.AreEqual(1, ProceduralUiKit.CachedSpriteCount, "快取數量應為 1");

            Sprite pill2 = ProceduralUiKit.GetPillSprite(120, 36, Color.black, Color.cyan);
            Assert.AreSame(pill1, pill2, "相同參數取得之 Pill Sprite 應為同一個物件參考");
            Assert.AreEqual(1, ProceduralUiKit.CachedSpriteCount, "重複取用相同參數不應重複生成新實例");

            // 2. Gradient Sprite 快取測試
            Sprite grad1 = ProceduralUiKit.GetGradientSprite(200, 60, Color.red, Color.blue, Color.white, 2, 8);
            Assert.IsNotNull(grad1, "第一次取得 Gradient Sprite 不應為 null");
            Assert.AreEqual(2, ProceduralUiKit.CachedSpriteCount, "快取數量應增長至 2");

            Sprite grad2 = ProceduralUiKit.GetGradientSprite(200, 60, Color.red, Color.blue, Color.white, 2, 8);
            Assert.AreSame(grad1, grad2, "相同參數取得之 Gradient Sprite 應為同一個物件參考");
            Assert.AreEqual(2, ProceduralUiKit.CachedSpriteCount, "重複取用相同參數不應重複生成新實例");

            // 3. Circle Sprite 快取測試
            Sprite circle1 = ProceduralUiKit.GetCircleSprite(48, Color.yellow, Color.red, 2);
            Assert.IsNotNull(circle1, "第一次取得 Circle Sprite 不應為 null");
            Assert.AreEqual(3, ProceduralUiKit.CachedSpriteCount, "快取數量應增長至 3");

            Sprite circle2 = ProceduralUiKit.GetCircleSprite(48, Color.yellow, Color.red, 2);
            Assert.AreSame(circle1, circle2, "相同參數取得之 Circle Sprite 應為同一個物件參考");
            Assert.AreEqual(3, ProceduralUiKit.CachedSpriteCount, "重複取用相同參數不應重複生成新實例");

            // 4. 清理快取測試
            ProceduralUiKit.ClearCache();
            Assert.AreEqual(0, ProceduralUiKit.CachedSpriteCount, "ClearCache 後快取數量應重置為 0");
        }

        [Test]
        public void OPT2_2_UnityScreenBase_WithScreenNavigator_HandlesLifecycleAndBackRequested()
        {
            var nav = new ScreenNavigator();

            var goA = new GameObject("ScreenA");
            var screenA = goA.AddComponent<TestScreenA>();

            var goB = new GameObject("ScreenB");
            var screenB = goB.AddComponent<TestScreenB>();

            try
            {
                // Push ScreenA
                bool pushedA = nav.Push(screenA);
                Assert.IsTrue(pushedA);
                Assert.AreEqual(1, nav.Count);
                Assert.AreSame(screenA, nav.CurrentScreen);
                Assert.AreEqual(1, screenA.PushCount);
                Assert.IsTrue(screenA.IsVisible);

                // Push ScreenB
                bool pushedB = nav.Push(screenB);
                Assert.IsTrue(pushedB);
                Assert.AreEqual(2, nav.Count);
                Assert.AreSame(screenB, nav.CurrentScreen);
                Assert.AreEqual(1, screenA.PauseCount, "ScreenA 應收到 OnPause");
                Assert.AreEqual(1, screenB.PushCount, "ScreenB 應收到 OnPush");
                Assert.IsTrue(screenB.IsVisible);

                // 模擬 ScreenB 開啟子彈窗
                screenB.HasSubModal = true;

                // 第一次按下返回鍵：ScreenB 內部消費，不彈出畫面
                bool handled1 = nav.HandleBackRequest();
                Assert.IsTrue(handled1, "子彈窗存在時應被內部消費");
                Assert.IsFalse(screenB.HasSubModal, "子彈窗應已被關閉");
                Assert.AreEqual(2, nav.Count, "畫面堆疊層數不應改變");
                Assert.AreSame(screenB, nav.CurrentScreen);
                Assert.AreEqual(0, screenB.PopCount);

                // 第二次按下返回鍵：無子彈窗，ScreenB 應被 Pop 出堆疊，ScreenA 恢復
                bool handled2 = nav.HandleBackRequest();
                Assert.IsTrue(handled2, "無子彈窗時 Navigator 應執行標準 Pop");
                Assert.AreEqual(1, nav.Count, "堆疊層數應降為 1");
                Assert.AreSame(screenA, nav.CurrentScreen);
                Assert.AreEqual(1, screenB.PopCount, "ScreenB 應收到 OnPop");
                Assert.AreEqual(1, screenA.ResumeCount, "ScreenA 應收到 OnResume");

                // 第三次按下返回鍵：ScreenA 為 Root 畫面，不應被 Pop
                bool handled3 = nav.HandleBackRequest();
                Assert.IsFalse(handled3, "Root 畫面不可被 Pop");
                Assert.AreEqual(1, nav.Count);
                Assert.AreSame(screenA, nav.CurrentScreen);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(goA);
                UnityEngine.Object.DestroyImmediate(goB);
            }
        }

        [Test]
        public void OPT2_2_LobbyAndGacha_NavigatorIntegration_StackAndBackHandledCorrectly()
        {
            // 初始化 GameServices 與 GameCore
            MasterDatabase master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
            var clock = new SystemClock();
            PlayerState player = GameCore.NewPlayer(master, clock);
            player.AddItem("gem", 10000);
            var rating = new RatingPolicy(Rating.G, Rating.G);
            var rng = new SplitMix64Random(12345UL);
            var core = new GameCore(master, player, rng, clock, rating);

            var nav = new ScreenNavigator();
            string tempDir = System.IO.Path.Combine(Application.temporaryCachePath, "test_opt2_saves_" + Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(tempDir);
            GameServices.Install(core, store, new AssetService(rating), rating, new NullPlatformService(), null, navigator: nav);

            var lobbyGo = new GameObject("LobbyUI");
            var lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.EnsureUIHierarchy();
            lobbyView.Initialize(core);

            var gachaGo = new GameObject("GachaUI");
            var gachaView = gachaGo.AddComponent<GachaView>();
            gachaView.EnsureUIHierarchy();
            gachaView.Initialize(core);

            try
            {
                // 推入大廳作為 Root
                nav.Push(lobbyView);
                Assert.AreEqual(1, nav.Count);
                Assert.AreSame(lobbyView, nav.CurrentScreen);

                // 從大廳開啟抽卡
                lobbyView.OpenGacha();
                Assert.AreEqual(2, nav.Count, "打開抽卡後堆疊應有 2 層");
                Assert.AreSame(gachaView, nav.CurrentScreen, "頂端應為 GachaView");
                Assert.IsFalse(lobbyView.gameObject.activeSelf, "大廳應被隱藏");
                Assert.IsTrue(gachaView.gameObject.activeSelf, "抽卡介面應顯示");

                // 在抽卡介面開啟機率彈窗
                gachaView.OpenRateModal();
                Transform rateModal = gachaGo.transform.Find("RateDetailsModal");
                Assert.IsNotNull(rateModal);
                Assert.IsTrue(rateModal.gameObject.activeSelf, "機率彈窗應處於開啟狀態");

                // 觸發返回鍵（例如按下 Esc）：機率彈窗應被優先關閉，抽卡畫面保持在頂端
                bool back1 = nav.HandleBackRequest();
                Assert.IsTrue(back1, "返回鍵應被 GachaView 內部消費關閉彈窗");
                Assert.IsFalse(rateModal.gameObject.activeSelf, "機率彈窗應已關閉");
                Assert.AreEqual(2, nav.Count, "抽卡介面仍在堆疊頂端");

                // 再次觸發返回鍵：無子彈窗，GachaView 被 Pop，LobbyView 恢復頂端並重新顯示
                bool back2 = nav.HandleBackRequest();
                Assert.IsTrue(back2, "應成功 Pop 抽卡畫面");
                Assert.AreEqual(1, nav.Count, "堆疊應回到 1 層");
                Assert.AreSame(lobbyView, nav.CurrentScreen, "頂端回到 LobbyView");
                Assert.IsTrue(lobbyView.gameObject.activeSelf, "大廳應重新恢復顯示");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lobbyGo);
                UnityEngine.Object.DestroyImmediate(gachaGo);
                if (System.IO.Directory.Exists(tempDir))
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
#endif
