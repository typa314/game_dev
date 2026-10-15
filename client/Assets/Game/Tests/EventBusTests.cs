using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Domain.Events;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A1：事件匯流排與領域事件（DomainEvents.cs）單元測試
    /// 涵蓋查核點 A1-5, A1-6, A1-7, A1-9
    /// </summary>
    [TestFixture]
    public class EventBusTests
    {
        // -------------------------------------------------------------
        // A1-5: EventBus 有兩個處理器 H1 和 H2；H1 收到事件 E1 時發佈 E2。預期的呼叫順序是 H1:E1, H2:E1, H1:E2, H2:E2（不會巢狀派送）。
        // -------------------------------------------------------------
        [Test]
        public void A1_5_EventBus_NestedPublish_DispatchesSequentiallyWithoutNesting()
        {
            EventBus bus = new EventBus();
            List<string> order = new List<string>();

            DomainEvent e1 = new DomainEvent("E1");
            DomainEvent e2 = new DomainEvent("E2");

            Action<DomainEvent> h1 = e =>
            {
                order.Add("H1:" + e.Name);
                if (e.Name == "E1")
                {
                    bus.Publish(e2);
                }
            };

            Action<DomainEvent> h2 = e =>
            {
                order.Add("H2:" + e.Name);
            };

            bus.Subscribe(h1);
            bus.Subscribe(h2);

            bus.Publish(e1);

            string[] expected = new string[] { "H1:E1", "H2:E1", "H1:E2", "H2:E2" };
            CollectionAssert.AreEqual(expected, order);
        }

        // -------------------------------------------------------------
        // A1-6: 處理器收到 E1 時丟出例外（例外會往外傳到 Publish 的呼叫端）。在丟出例外之前，另一個處理器已經發佈了 E2，E2 會留在佇列中。驗證：例外發生後再發佈 E3，處理器只會收到 E3，不會收到殘留的 E2。
        // -------------------------------------------------------------
        [Test]
        public void A1_6_EventBus_HandlerException_ClearsResidualQueuedEvents()
        {
            EventBus bus = new EventBus();
            List<string> received = new List<string>();

            DomainEvent e1 = new DomainEvent("E1");
            DomainEvent e2 = new DomainEvent("E2");
            DomainEvent e3 = new DomainEvent("E3");

            Action<DomainEvent> h1 = e =>
            {
                received.Add("H1:" + e.Name);
                if (e.Name == "E1")
                {
                    // 在另一個處理器丟出例外之前，另一個處理器已發佈了 E2，留在佇列中
                    bus.Publish(e2);
                }
            };

            Action<DomainEvent> h2 = e =>
            {
                received.Add("H2:" + e.Name);
                if (e.Name == "E1")
                {
                    // 收到 E1 時丟出例外
                    throw new InvalidOperationException("Handler H2 failed on E1");
                }
            };

            bus.Subscribe(h1);
            bus.Subscribe(h2);

            // 例外會往外傳到 Publish 的呼叫端
            Assert.Throws<InvalidOperationException>(() => bus.Publish(e1));

            // 清空記錄，準備驗證後續發佈 E3
            received.Clear();

            // 例外發生後再發佈 E3
            bus.Publish(e3);

            // 處理器只會收到 E3，不會收到殘留的 E2
            string[] expected = new string[] { "H1:E3", "H2:E3" };
            CollectionAssert.AreEqual(expected, received);
            Assert.IsFalse(received.Contains("H1:E2"));
            Assert.IsFalse(received.Contains("H2:E2"));
        }

        // -------------------------------------------------------------
        // A1-7: 處理器在派送途中取消訂閱自己，不會丟出例外；之後的事件不會再送到這個處理器。
        // -------------------------------------------------------------
        [Test]
        public void A1_7_EventBus_HandlerUnsubscribingSelfDuringDispatch_DoesNotThrowAndStopsReceiving()
        {
            EventBus bus = new EventBus();
            List<string> received = new List<string>();

            Action<DomainEvent> selfHandler = null;
            selfHandler = e =>
            {
                received.Add("Self:" + e.Name);
                bus.Unsubscribe(selfHandler);
            };

            Action<DomainEvent> otherHandler = e =>
            {
                received.Add("Other:" + e.Name);
            };

            bus.Subscribe(selfHandler);
            bus.Subscribe(otherHandler);

            // 處理器在派送途中取消訂閱自己，不會丟出例外
            Assert.DoesNotThrow(() => bus.Publish(new DomainEvent("E1")));
            CollectionAssert.AreEqual(new string[] { "Self:E1", "Other:E1" }, received);

            // 之後的事件不會再送到這個處理器
            bus.Publish(new DomainEvent("E2"));
            CollectionAssert.AreEqual(new string[] { "Self:E1", "Other:E1", "Other:E2" }, received);
        }

        // -------------------------------------------------------------
        // A1-9: DomainEvent.With("k", 1.5) 會以 InvariantCulture 轉成 "1.5"（測試時把 CultureInfo.CurrentCulture 暫時設為 de-DE，結束後在 finally 中還原）；Get("missing") 回傳 null。
        // -------------------------------------------------------------
        [Test]
        public void A1_9_DomainEvent_With_UsesInvariantCulture()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                // 暫時將當前文化設為 de-DE（德國文化小數點為逗號 ','）
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                DomainEvent ev = new DomainEvent("TestEvent").With("k", 1.5);
                Assert.AreEqual("1.5", ev.Get("k"));
            }
            finally
            {
                // 結束後在 finally 中還原
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void A1_9_DomainEvent_Get_MissingKey_ReturnsNull()
        {
            DomainEvent ev = new DomainEvent("TestEvent");
            Assert.IsNull(ev.Get("missing"));
        }
    }
}
