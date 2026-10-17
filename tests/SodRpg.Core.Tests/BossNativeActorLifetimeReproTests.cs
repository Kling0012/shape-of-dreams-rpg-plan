using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    // #506: run the linked production ledger and lifecycle prefixes. The native lifecycle
    // boundary follows Dew.StepDestroy (Actor.ICleanup.OnCleanup) and Actor.OnDisable ->
    // DewPool.ClearEventsAndProcessors, whose guard skips ClearPooledEventsAndProcessors
    // when both pooling and room reuse are disabled.
    public class BossNativeActorLifetimeReproTests
    {
        private readonly ITestOutputHelper _output;
        public BossNativeActorLifetimeReproTests(ITestOutputHelper output) { _output = output; }

        private static void FinishNativeLifetime(Entity target, bool usePooling, bool reuseInRoom, bool hiddenBeforeDestroy)
        {
            if (hiddenBeforeDestroy)
            {
                // A plain GameObject.SetActive(false) runs OnDisable while the entity is
                // still active, so the postfix keeps the identity for reactivation. Unity
                // does not call OnDisable again when the already-hidden object is later
                // destroyed or parked in a pool.
                typeof(BossNativeDisabledLifetime).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { target });
            }

            // Actor.ICleanup.OnCleanup changes isActive; OnIsActiveChanged invokes this
            // completion prefix. These API doubles do not install Harmony automatically.
            target.isActive = false;
            ((Actor)target).isActive = false;
            typeof(BossNativeInstanceCompletion).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { target });

            // The cleanup postfix releases the record inside StepDestroy even when the
            // hidden GameObject never runs a second OnDisable.
            typeof(BossNativeCleanedLifetime).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { target });

            if (hiddenBeforeDestroy) return;

            // Actor.OnDisable calls DewPool.ClearEventsAndProcessors. A present SpawnManager
            // only dispatches the virtual clear (and thus its prefix) under this exact guard.
            if (usePooling || reuseInRoom)
                typeof(BossNativePooledLifetime).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { target });

            // The production OnDisable postfix runs after the native method, including when
            // DewPool skips the virtual clear. Keep this after the destruction callbacks above.
            typeof(BossNativeDisabledLifetime).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { target });
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void Sequential_native_actor_destruction_keeps_marks_working_after_4096_lifetimes(
            bool usePooling, bool reuseInRoom)
        {
            var previousHost = HostAuthority.NativeInstance;
            bool previousServer = Mirror.NetworkServer.active;
            try
            {
                Mirror.NetworkServer.active = true;
                var host = new HostAuthority();
                HostAuthority.NativeInstance = host;
                var ledger = new HostAuthority.BossProgressLedger();
                var lives = (Dictionary<Actor, long>)typeof(HostAuthority)
                    .GetField("_bossNativeActorLives", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);

                // The production boss-mark entry point allocates a native identity for each
                // target. Only one target is active at a time; this never needs 4096 live actors.
                // Every other lifetime ends while the GameObject is already hidden, the path
                // where no second OnDisable runs (review on PR #689).
                for (int i = 0; i < 4096; i++)
                {
                    var target = new Entity { creationTime = i + 1, Relation = EntityRelation.Enemy };
                    ledger.MarkTarget(target, 1, 10f, 1f, "issue506", i + 1);
                    Assert.Equal(1, ledger.MarkCount(target, 1f, "issue506"));
                    bool hidden = (i & 1) == 1;
                    FinishNativeLifetime(target, usePooling, reuseInRoom, hidden);
                    Assert.True(lives.Count == 0,
                        $"iteration {i + 1} (hidden={hidden}) left {lives.Count} retained identities " +
                        $"(usePooling={usePooling}, reuseInRoom={reuseInRoom}).");
                    if ((i + 1) % 1024 == 0)
                        _output.WriteLine("finished={0}, retained actor identities={1}", i + 1, lives.Count);
                }

                var next = new Entity { creationTime = 4097, Relation = EntityRelation.Enemy };
                ledger.MarkTarget(next, 1, 10f, 1f, "issue506", 4097);
                int marks = ledger.MarkCount(next, 1f, "issue506");
                lives.TryGetValue(next, out long life);
                _output.WriteLine("usePooling={0}, reuseInRoom={1}, retained={2}, nextLife={3}, nextMarks={4}",
                    usePooling, reuseInRoom, lives.Count, life, marks);
                Assert.True(marks == 1,
                    $"After 4096 completed actor lifetimes (usePooling={usePooling}, reuseInRoom={reuseInRoom}), " +
                    $"the next active target must retain one boss mark; actual marks={marks}, " +
                    $"native life={life}, retained identities={lives.Count}.");

                // A temporarily hidden target keeps its number until it is re-enabled, and a
                // hidden destroy without another OnDisable still releases the record.
                var resting = new Entity { creationTime = 4098, Relation = EntityRelation.Enemy };
                ledger.MarkTarget(resting, 1, 10f, 1f, "issue506", 4098);
                lives.TryGetValue(resting, out long restingLife);
                Assert.True(restingLife > 0, "A fresh active target must receive a native life.");
                typeof(BossNativeDisabledLifetime).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { resting });
                lives.TryGetValue(resting, out long hiddenLife);
                int restingMarks = ledger.MarkCount(resting, 1f, "issue506");
                _output.WriteLine("usePooling={0}, reuseInRoom={1}, restingLife={2}, hiddenLife={3}, hiddenMarks={4}",
                    usePooling, reuseInRoom, restingLife, hiddenLife, restingMarks);
                Assert.Equal(restingLife, hiddenLife);
                Assert.Equal(1, restingMarks);
                FinishNativeLifetime(resting, usePooling, reuseInRoom, hiddenBeforeDestroy: true);
                Assert.False(lives.ContainsKey(resting),
                    $"A hidden destroy without a second OnDisable must release the lifetime record " +
                    $"(usePooling={usePooling}, reuseInRoom={reuseInRoom}).");
            }
            finally
            {
                HostAuthority.NativeInstance = previousHost;
                Mirror.NetworkServer.active = previousServer;
            }
        }
    }
}
