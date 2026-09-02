using System;
using MobileDemo.Core.Events;
using NUnit.Framework;

namespace MobileDemo.Tests.EditMode
{
    public class EventBusTests
    {
        // Test-local events: the production catalogue in GameEvents.cs describes the game, and
        // tests should not break when the game's payloads are retuned.
        readonly struct Ping : IEvent
        {
            public readonly int Value;
            public Ping(int value) => Value = value;
        }

        readonly struct Pong : IEvent
        {
        }

        [TearDown]
        public void TearDown() => EventBus.ClearAll();

        [Test]
        public void Publish_InvokesSubscriber_WithPayload()
        {
            int received = 0;
            EventBus<Ping>.Subscribe(e => received = e.Value);

            EventBus<Ping>.Publish(new Ping(42));

            Assert.AreEqual(42, received);
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus<Ping>.Publish(new Ping(1)));
        }

        [Test]
        public void Publish_InvokesEverySubscriber()
        {
            int calls = 0;
            EventBus<Ping>.Subscribe(_ => calls++);
            EventBus<Ping>.Subscribe(_ => calls++);

            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(2, calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            int calls = 0;
            Action<Ping> handler = _ => calls++;
            EventBus<Ping>.Subscribe(handler);

            EventBus<Ping>.Unsubscribe(handler);
            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Unsubscribe_HandlerThatWasNeverSubscribed_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus<Ping>.Unsubscribe(_ => { }));
        }

        [Test]
        public void Buses_AreIsolatedPerEventType()
        {
            int pings = 0;
            EventBus<Ping>.Subscribe(_ => pings++);

            EventBus<Pong>.Publish(new Pong());

            Assert.AreEqual(0, pings);
        }

        /// <summary>
        /// Locks in the multicast-delegate guarantee the implementation relies on: a handler
        /// that unsubscribes while being notified must not disturb the dispatch already in
        /// flight. Swapping to a mutable list without a defensive copy would break this.
        /// </summary>
        [Test]
        public void HandlerUnsubscribingDuringPublish_StillNotifiesLaterSubscribers()
        {
            int secondCalls = 0;
            Action<Ping> second = _ => secondCalls++;
            Action<Ping> first = null;
            first = _ => EventBus<Ping>.Unsubscribe(first);

            EventBus<Ping>.Subscribe(first);
            EventBus<Ping>.Subscribe(second);

            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(1, secondCalls, "the in-flight invocation list must be a snapshot");
        }

        /// <summary>
        /// A handler unsubscribed mid-publish is gone from the *next* publish — the snapshot
        /// above is per-call, not a permanent copy.
        /// </summary>
        [Test]
        public void HandlerUnsubscribingDuringPublish_IsNotNotifiedAgain()
        {
            int calls = 0;
            Action<Ping> handler = null;
            handler = _ =>
            {
                calls++;
                EventBus<Ping>.Unsubscribe(handler);
            };
            EventBus<Ping>.Subscribe(handler);

            EventBus<Ping>.Publish(new Ping(0));
            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ClearAll_RemovesSubscribersOfEveryEventType()
        {
            int calls = 0;
            EventBus<Ping>.Subscribe(_ => calls++);
            EventBus<Pong>.Subscribe(_ => calls++);

            EventBus.ClearAll();
            EventBus<Ping>.Publish(new Ping(0));
            EventBus<Pong>.Publish(new Pong());

            Assert.AreEqual(0, calls, "stale statics are what breaks the second Play with domain reload off");
        }

        [Test]
        public void ClearAll_LeavesTheBusUsable()
        {
            EventBus.ClearAll();

            int calls = 0;
            EventBus<Ping>.Subscribe(_ => calls++);
            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(1, calls);
        }

        /// <summary>
        /// Documents the deliberate non-behaviour from <c>Subscribe</c>'s remarks: the bus does
        /// not de-duplicate. If this test ever fails, someone added a hidden guard and the
        /// reasoning recorded there needs revisiting.
        /// </summary>
        [Test]
        public void Subscribe_SameHandlerTwice_IsInvokedTwice()
        {
            int calls = 0;
            Action<Ping> handler = _ => calls++;
            EventBus<Ping>.Subscribe(handler);
            EventBus<Ping>.Subscribe(handler);

            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(2, calls);
        }

        [Test]
        public void PublishingFromInsideAHandler_IsDelivered()
        {
            int pongs = 0;
            EventBus<Ping>.Subscribe(_ => EventBus<Pong>.Publish(new Pong()));
            EventBus<Pong>.Subscribe(_ => pongs++);

            EventBus<Ping>.Publish(new Ping(0));

            Assert.AreEqual(1, pongs);
        }
    }
}
