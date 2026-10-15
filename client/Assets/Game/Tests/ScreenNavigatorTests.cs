using System;
using System.Collections.Generic;
using Game.Core.UI;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class ScreenNavigatorTests
    {
        private class TestScreen : IScreen
        {
            public string ScreenId { get; }
            public List<string> CallLog { get; } = new List<string>();
            public bool ConsumeBack { get; set; }

            public TestScreen(string id)
            {
                ScreenId = id;
            }

            public void OnPush(object args) => CallLog.Add($"OnPush:{args}");
            public void OnPop() => CallLog.Add("OnPop");
            public void OnPause() => CallLog.Add("OnPause");
            public void OnResume() => CallLog.Add("OnResume");
            public bool OnBackRequested()
            {
                CallLog.Add($"OnBackRequested:{ConsumeBack}");
                return ConsumeBack;
            }
        }

        private class MockView : IView
        {
            public bool IsVisible { get; set; }
        }

        private class MockPresenter : PresenterBase<MockView>
        {
            public bool Initialized { get; private set; }
            public bool Disposed { get; private set; }

            public override void Initialize()
            {
                Initialized = true;
            }

            public override void Dispose()
            {
                base.Dispose();
                Disposed = true;
            }
        }

        private class MockScreen : ScreenBase<MockView, MockPresenter>
        {
            public override string ScreenId => "MockScreen";
            public MockScreen(MockView view, MockPresenter presenter) : base(view, presenter) { }
        }

        // =========================================================================================
        // H5-1: 堆疊基本操作 (Push, Pop, Replace, PopToRoot) 與生命週期呼叫順序
        // =========================================================================================
        [Test]
        public void H5_Navigator_PushPopReplace_MaintainsLifecycleOrder()
        {
            var nav = new ScreenNavigator();
            var screenA = new TestScreen("ScreenA");
            var screenB = new TestScreen("ScreenB");

            // 1. Push ScreenA
            bool pushedA = nav.Push(screenA, "initA");
            Assert.IsTrue(pushedA);
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(screenA, nav.CurrentScreen);
            Assert.AreEqual("OnPush:initA", screenA.CallLog[0]);

            // 2. Push ScreenB (A 應 OnPause, B 應 OnPush)
            bool pushedB = nav.Push(screenB, "initB");
            Assert.IsTrue(pushedB);
            Assert.AreEqual(2, nav.Count);
            Assert.AreSame(screenB, nav.CurrentScreen);
            Assert.AreEqual("OnPause", screenA.CallLog[1]);
            Assert.AreEqual("OnPush:initB", screenB.CallLog[0]);

            // 3. Pop ScreenB (B 應 OnPop, A 應 OnResume)
            bool poppedB = nav.Pop();
            Assert.IsTrue(poppedB);
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(screenA, nav.CurrentScreen);
            Assert.AreEqual("OnPop", screenB.CallLog[1]);
            Assert.AreEqual("OnResume", screenA.CallLog[2]);

            // 4. Replace ScreenA with ScreenB
            var screenC = new TestScreen("ScreenC");
            bool replaced = nav.Replace(screenC, "initC");
            Assert.IsTrue(replaced);
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(screenC, nav.CurrentScreen);
            Assert.AreEqual("OnPop", screenA.CallLog[3]);
            Assert.AreEqual("OnPush:initC", screenC.CallLog[0]);
        }

        // =========================================================================================
        // H5-2: 轉場鎖定（Transition Lock）與輸入阻擋（陽性對照）
        // =========================================================================================
        [Test]
        public void H5_Navigator_TransitionLock_BlocksAllMutationsAndBackInput()
        {
            var nav = new ScreenNavigator();
            var screenA = new TestScreen("ScreenA");
            var screenB = new TestScreen("ScreenB");

            nav.Push(screenA);
            Assert.AreEqual(1, nav.Count);

            // 開啟轉場鎖
            nav.BeginTransition();
            Assert.IsTrue(nav.IsTransitioning);

            // 陽性對照 1: 轉場期間 Push 必須被拒絕
            bool pushRejected = nav.Push(screenB);
            Assert.IsFalse(pushRejected, "轉場期間 Push 應被拒絕");
            Assert.AreEqual(1, nav.Count);

            // 陽性對照 2: 轉場期間 Pop 必須被拒絕
            bool popRejected = nav.Pop();
            Assert.IsFalse(popRejected, "轉場期間 Pop 應被拒絕");
            Assert.AreEqual(1, nav.Count);

            // 陽性對照 3: 轉場期間 Replace 必須被拒絕
            bool replaceRejected = nav.Replace(screenB);
            Assert.IsFalse(replaceRejected, "轉場期間 Replace 應被拒絕");

            // 陽性對照 4: 轉場期間 Esc 返回操作必須被擋下
            bool backRejected = nav.HandleBackRequest();
            Assert.IsFalse(backRejected, "轉場期間 Back 應被阻擋");

            // 結束轉場鎖後恢復正常
            nav.EndTransition();
            Assert.IsFalse(nav.IsTransitioning);
            Assert.IsTrue(nav.Push(screenB));
            Assert.AreEqual(2, nav.Count);
        }

        // =========================================================================================
        // H5-3: Esc 返回鍵行為（消費事件 vs 自動 Pop vs Root 不退）
        // =========================================================================================
        [Test]
        public void H5_Navigator_BackKey_HandlesCustomConsumption_AndRootProtection()
        {
            var nav = new ScreenNavigator();
            var root = new TestScreen("Root");
            var sub = new TestScreen("Sub");

            nav.Push(root);

            // 1. Root 狀態下按返回鍵：保護不退出，回傳 false
            bool rootBack = nav.HandleBackRequest();
            Assert.IsFalse(rootBack, "Root 畫面不可直接被自動 Pop");
            Assert.AreEqual(1, nav.Count);

            // 2. Push Sub 畫面，但 Sub 正在開啟子浮窗（ConsumeBack = true）
            nav.Push(sub);
            sub.ConsumeBack = true;

            bool subCustomBack = nav.HandleBackRequest();
            Assert.IsTrue(subCustomBack, "畫面自行消費返回鍵時應回傳 true");
            Assert.AreEqual(2, nav.Count, "自行消費時堆疊不應被 Pop");

            // 3. Sub 子浮窗已關閉（ConsumeBack = false），再次按返回鍵應自動 Pop
            sub.ConsumeBack = false;
            bool subAutoPop = nav.HandleBackRequest();
            Assert.IsTrue(subAutoPop, "無子浮窗時應自動執行 Pop");
            Assert.AreEqual(1, nav.Count);
            Assert.AreSame(root, nav.CurrentScreen);
        }

        // =========================================================================================
        // H6: MVP ScreenBase 樣板與生命週期串接
        // =========================================================================================
        [Test]
        public void H6_MvpScreenBase_IntegratesPresenterAndLifecycle()
        {
            var view = new MockView();
            var presenter = new MockPresenter();
            var screen = new MockScreen(view, presenter);

            var nav = new ScreenNavigator();

            // 1. Push Screen: View 可見，Presenter 初始化
            nav.Push(screen);
            Assert.IsTrue(view.IsVisible);
            Assert.IsTrue(presenter.Initialized);

            // 2. Pop Screen: View 隱藏，Presenter 釋放
            nav.Pop();
            Assert.IsFalse(view.IsVisible);
            Assert.IsTrue(presenter.Disposed);
        }
    }
}
