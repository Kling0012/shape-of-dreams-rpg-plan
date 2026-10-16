using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using Xunit;
using Xunit.Abstractions;

namespace SodRpg.Core.Tests
{
    // #506: run the linked production ledger and lifecycle prefixes. The native lifecycle
    // boundary follows Actor.OnDisable -> DewPool.ClearEventsAndProcessors, whose guard
    // skips ClearPooledEventsAndProcessors when both pooling and room reuse are disabled.
    public class BossNativeActorLifetimeReproTests
    {
        private readonly ITestOutputHelper _output;
        public BossNativeActorLifetimeReproTests(ITestOutputHelper output) { _output = output; }

        private static void FinishNativeLifetime(Entity target, bool usePooling, bool reuseInRoom)
        {
            // Actor.ICleanup.OnCleanup changes isActive; OnIsActiveChanged invokes this
            // completion prefix. These API doubles do not install Harmony automatically.
            target.isActive = false;
            ((Actor)target).isActive = false;
            typeof(BossNativeInstanceCompletion).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { target });

            // Actor.OnDisable calls DewPool.ClearEventsAndProcessors. A present SpawnManager
            // only dispatches the virtual clear (and thus its prefix) under this exact guard.
            if (usePooling || reuseInRoom)
                typeof(BossNativePooledLifetime).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
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
                for (int i = 0; i < 4096; i++)
                {
                    var target = new Entity { creationTime = i + 1, Relation = EntityRelation.Enemy };
                    ledger.MarkTarget(target, 1, 10f, 1f, "issue506", i + 1);
                    Assert.Equal(1, ledger.MarkCount(target, 1f, "issue506"));
                    FinishNativeLifetime(target, usePooling, reuseInRoom);
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
            }
            finally
            {
                HostAuthority.NativeInstance = previousHost;
                Mirror.NetworkServer.active = previousServer;
            }
        }
    }
}
