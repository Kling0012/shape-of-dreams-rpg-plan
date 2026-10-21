using System;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.FxBudget.Tests
{
    // #1029: PowerFlushBudget.Observe (private nested in the linked
    // HostAuthority.PowerCommandQueue.cs) is driven through reflection so the
    // production widen/narrow bookkeeping, including its Time.unscaledTime
    // reads, runs unmodified. Each test follows the issue's timeline:
    // drained -> carry starts -> level widens -> drained again. Right after
    // the second drain the widened budget must survive; it may only step back
    // down after three continuous seconds of an empty queue.
    public sealed class PowerFlushBudgetTests : IDisposable
    {
        private static readonly MethodInfo ObserveMethod;

        private readonly float _savedUnscaledTime;
        private readonly HostAuthority.PowerCommandQueue _queue = new HostAuthority.PowerCommandQueue();

        static PowerFlushBudgetTests()
        {
            var budget = typeof(HostAuthority).GetNestedType("PowerFlushBudget", BindingFlags.NonPublic);
            ObserveMethod = budget.GetMethod("Observe", BindingFlags.NonPublic | BindingFlags.Static);
        }

        public PowerFlushBudgetTests()
        {
            _savedUnscaledTime = Time.unscaledTime;
            Time.unscaledTime = 0f;
        }

        public void Dispose()
        {
            Time.unscaledTime = _savedUnscaledTime;
        }

        private void ObserveAt(float now, int leftover)
        {
            Time.unscaledTime = now;
            ObserveMethod.Invoke(null, new object[] { _queue, leftover });
        }

        [Fact]
        public void Observe_KeepsWidenedBudgetRightAfterDrainAndNarrowsOnlyAfterThreeIdleSeconds()
        {
            ObserveAt(0f, 0);    // drained once: DrainedSince = 0
            ObserveAt(4.0f, 30); // carry starts; DrainedSince is reset to -1f
            ObserveAt(4.4f, 30); // 0.4 s of carry: below the widen threshold
            Assert.Equal(0, _queue.Level);

            ObserveAt(4.7f, 30); // 0.7 s of carry: widen to level 1
            Assert.Equal(1, _queue.Level);

            ObserveAt(4.8f, 0);  // drained again at 4.8: the widened budget must survive this instant
            Assert.Equal(1, _queue.Level);

            ObserveAt(7.79f, 0); // 2.99 s since the fresh drain: still widened
            Assert.Equal(1, _queue.Level);

            ObserveAt(7.81f, 0); // 3.01 s of continuous idle: step back down
            Assert.Equal(0, _queue.Level);
        }

        [Fact]
        public void Observe_RestartsIdleWindowWhenCarryReturnsDuringTheWait()
        {
            ObserveAt(0f, 0);    // drained: DrainedSince = 0
            ObserveAt(1.0f, 9);  // carry starts while the queue is idle-waiting
            ObserveAt(1.6f, 9);  // 0.6 s of carry: widen to level 1
            Assert.Equal(1, _queue.Level);

            ObserveAt(1.7f, 0);  // drained again: the idle window starts over here
            ObserveAt(2.0f, 5);  // carry returns during the wait: window restarts
            ObserveAt(2.05f, 0); // drained again

            ObserveAt(5.04f, 0); // 2.99 s since 2.05: still widened
            Assert.Equal(1, _queue.Level);

            ObserveAt(5.05f, 0); // 3.0 s since 2.05: step back down
            Assert.Equal(0, _queue.Level);
        }

        [Fact]
        public void Observe_KeepsWidenAfterPointSixSecondsAndNarrowAfterThreeFreshIdleSeconds()
        {
            ObserveAt(0.0f, 5);  // carry from a never-drained queue
            ObserveAt(0.5f, 5);  // 0.5 s: below the widen threshold
            Assert.Equal(0, _queue.Level);

            ObserveAt(0.6f, 5);  // 0.6 s of carry: widen to level 1
            Assert.Equal(1, _queue.Level);

            ObserveAt(0.7f, 0);  // first drain ever: DrainedSince = 0.7
            ObserveAt(3.69f, 0); // 2.99 s idle: still widened
            Assert.Equal(1, _queue.Level);

            ObserveAt(3.7f, 0);  // 3.0 s idle: step back down
            Assert.Equal(0, _queue.Level);
        }
    }
}
