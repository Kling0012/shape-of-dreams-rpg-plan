using System;
using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.FxBudget.Tests
{
    // #1029: NativeProcSpawnSpread.cs is compiled into this assembly, so these
    // tests drive the real public enqueue path (Plain) and the real Tick drain
    // loop against the FlushBudgetBoundary doubles. Every frame feeds more
    // projectiles than the current per-frame drain limit, so the queue keeps a
    // backlog; when the feeding stops the queue empties and the widened drain
    // level must survive until three continuous seconds of an empty queue.
    public sealed class NativeProcSpawnSpreadTests : IDisposable
    {
        private static readonly FieldInfo LevelField = Field("_level");
        private static readonly FieldInfo QueueField = Field("Queue");
        private static readonly FieldInfo DisabledField = Field("_disabled");

        private readonly float _savedUnscaledTime;
        private readonly float _savedUnscaledDeltaTime;
        private readonly bool _savedServerActive;
        private readonly Actor _self = new Actor();
        private readonly CastInfo _info = new CastInfo();

        private static FieldInfo Field(string name)
            => typeof(NativeProcSpawnSpread).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

        public NativeProcSpawnSpreadTests()
        {
            _savedUnscaledTime = Time.unscaledTime;
            _savedUnscaledDeltaTime = Time.unscaledDeltaTime;
            _savedServerActive = NetworkServer.active;
            Reset();
        }

        public void Dispose()
        {
            Time.unscaledTime = _savedUnscaledTime;
            Time.unscaledDeltaTime = _savedUnscaledDeltaTime;
            NetworkServer.active = _savedServerActive;
        }

        // Fresh static state for every test: the queue, the budget level and
        // its timestamps all start from the host's first-Tick conditions.
        private void Reset()
        {
            ((IList)QueueField.GetValue(null)).Clear();
            LevelField.SetValue(null, 0);
            Field("_backlogSince").SetValue(null, -1f);
            Field("_drainedSince").SetValue(null, -1f);
            DisabledField.SetValue(null, false);
            Field("_heavy").SetValue(null, false);
            Field("_draining").SetValue(null, false);
            Field("_warned").SetValue(null, false);
            Field("_room").SetValue(null, null);
            Field("_diagnostics").SetValue(null, false);
            ManagerBase<GraphicsManager>.instance = null;
            Dew.Spawned = 0;
            // Host conditions: a heavy frame (over 1/30 s) makes Defer() queue
            // the projectiles instead of creating them immediately.
            NetworkServer.active = true;
            Time.unscaledDeltaTime = 0.05f;
            Time.unscaledTime = 0f;
        }

        private static int Level => (int)LevelField.GetValue(null);
        private static IList Queue => (IList)QueueField.GetValue(null);
        private static bool Disabled => (bool)DisabledField.GetValue(null);

        private void TickAt(float now)
        {
            Time.unscaledTime = now;
            NativeProcSpawnSpread.Tick();
        }

        private void Offer(int count, float now)
        {
            Time.unscaledTime = now;
            for (int i = 0; i < count; i++)
                NativeProcSpawnSpread.Plain<Ai_Gem_R_Spiral_Fireball>(_self, Vector3.zero, null, _info, null);
        }

        // Feeds 40 projectiles per 10 ms frame. The level-0 drain limit (16)
        // is always exceeded, so the queue holds a continuous backlog.
        private void CarryPhase(float from, int frames)
        {
            for (int k = 0; k < frames; k++)
            {
                float now = from + 0.01f * k;
                Offer(40, now);
                TickAt(now);
                Assert.True(Queue.Count > 0, $"carry must persist at t={now:0.00}");
            }
        }

        private float DrainAll(float now)
        {
            do
            {
                now += 0.01f;
                TickAt(now);
            } while (Queue.Count > 0);
            return now;
        }

        [Fact]
        public void Tick_KeepsWidenedDrainRightAfterQueueEmptiesAndNarrowsOnlyAfterThreeIdleSeconds()
        {
            TickAt(0f); // drained once: _drainedSince = 0
            CarryPhase(4.00f, 62); // 4.00..4.61: over 0.6 s of continuous carry
            Assert.Equal(1, Level); // widened to level 1 (0.6 s rule still works)

            float drainedAt = DrainAll(4.61f);
            Assert.Equal(0, Queue.Count);

            TickAt(drainedAt + 0.01f); // first idle tick after the drain
            Assert.Equal(1, Level); // the widened drain must survive this instant

            TickAt(drainedAt + 2.99f); // 2.99 s of continuous idle: still widened
            Assert.Equal(1, Level);

            TickAt(drainedAt + 3.01f); // 3.01 s of continuous idle: step back down
            Assert.Equal(0, Level);

            // Every offered projectile was created exactly once: the carry-over
            // never dropped anything, and the queue never disabled itself.
            Assert.Equal(62 * 40, Dew.Spawned);
            Assert.False(Disabled);
        }

        [Fact]
        public void Tick_RestartsIdleWindowWhenCarryReturnsDuringTheWait()
        {
            TickAt(0f);
            CarryPhase(4.00f, 62);
            Assert.Equal(1, Level);

            float firstDrainedAt = DrainAll(4.61f);
            TickAt(firstDrainedAt + 0.01f);
            Assert.Equal(1, Level); // still widened one idle tick after the drain

            // Carry returns briefly during the idle wait.
            CarryPhase(firstDrainedAt + 0.05f, 3);
            float secondDrainedAt = DrainAll(firstDrainedAt + 0.08f);

            TickAt(secondDrainedAt + 0.01f); // one idle tick after the second drain
            Assert.Equal(1, Level);

            TickAt(secondDrainedAt + 2.99f); // 2.99 s since the second drain
            Assert.Equal(1, Level);

            TickAt(secondDrainedAt + 3.01f); // 3.01 s since the second drain: narrow
            Assert.Equal(0, Level);
        }

        [Fact]
        public void Tick_ForceSpawnsProjectilesPastTheNativeWindowDespiteTheDrainLimit()
        {
            TickAt(0f); // a first heavy frame makes Defer() queue the offers
            Offer(50, 10.00f);
            Assert.Equal(50, Queue.Count);

            TickAt(10.09f); // oldest projectile is 0.09 s old: past the 0.07 s native window
            Assert.Equal(0, Queue.Count); // force-spawn drained everything despite the limit of 16
            Assert.Equal(50, Dew.Spawned);
            Assert.False(Disabled);
        }
    }
}
