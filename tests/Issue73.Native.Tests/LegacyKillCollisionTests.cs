using System;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    public sealed partial class NativeAcceptanceTests
    {
        [Fact]
        public void Legacy_facts_sharing_a_net_id_across_graphs_restore_without_throwing()
        {
            // Core の台帳は (流れ, netId, 区画の世代) で区別するので、旧形式の事実が同じ netId を持つことがある。
            var saved = new KillClassificationCheckpoint { RunId = "run", HostSequence = 0 };
            saved.Facts.Add(new AuthoritativeRunKill("run", "legacy-a", 5u, 0, NightmareAffix.None, null, 0, null, graphEpoch: 1));
            saved.Facts.Add(new AuthoritativeRunKill("run", "legacy-b", 5u, 1, NightmareAffix.None, null, 0, null, graphEpoch: 2));
            saved.Facts.Add(new AuthoritativeRunKill("run", "legacy-c", 6u, 1, NightmareAffix.None, null, 0, null, graphEpoch: 2));
            var session = Session();
            session.Profile.KillClassification = saved;
            Set(typeof(ClientSession), "_hostSession", session);
            var host = Host(new Actor());
            var outstanding = new KillClassificationCheckpoint { RunId = "run" };

            Assert.Null(Record.Exception(() => host.CaptureKillReplay(outstanding)));
            Assert.Equal(3, outstanding.HostFacts.Count);
        }
    }
}
