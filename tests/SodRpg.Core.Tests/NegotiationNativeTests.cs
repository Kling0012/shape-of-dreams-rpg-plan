using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class NegotiationNativeTests
    {
        [Fact]
        public void Host_reports_compatibility_differences_and_clears_them_after_a_matching_hello()
        {
            var host = new HostAuthority();
            var actor = host.RegisterNegotiation();
            var peer = new DewPlayer();
            DewPlayer.gamePlayers.Add(peer);
            try
            {
                host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = 12, content = ContentFingerprint.Value, continueCheckpoints = true }, peer);
                Assert.Single(HostAuthority.VersionWarnings);
                var reply = Assert.IsType<DreamforgeHelloMsg>(actor.ClientMessages.Last());
                Assert.Equal(Protocol.Version, reply.protocol);
                Assert.Equal(ContentFingerprint.Value, reply.content);
                host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = ContentFingerprint.Value }, peer);
                Assert.Single(HostAuthority.VersionWarnings);
                host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = "different-registry", continueCheckpoints = true }, peer);
                Assert.Single(HostAuthority.VersionWarnings);
                host.ReceiveNegotiation(new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = ContentFingerprint.Value,
                    continueCheckpoints = true, infinityAvailable = true,
                }, peer);
                Assert.Empty(HostAuthority.VersionWarnings);
            }
            finally
            {
                host.DetachNegotiation();
                DewPlayer.gamePlayers.Remove(peer);
            }
            Assert.False(actor.ServerHandlers.ContainsKey(typeof(DreamforgeHelloMsg)));
        }

        [Fact]
        public void Cap_registry_changes_only_update_the_compatibility_warning()
        {
            var host = new HostAuthority();
            host.RegisterNegotiation();
            var peer = new DewPlayer();
            DewPlayer.gamePlayers.Add(peer);
            try
            {
                string previous = ContentFingerprint.Value;
                host.ReceiveNegotiation(new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = previous,
                    continueCheckpoints = true, infinityAvailable = true,
                }, peer);
                Assert.Empty(HostAuthority.VersionWarnings);
                FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile {
                    Id = "integration.negotiation.host", Kind = LinkKind.MemoryDamage,
                    Maximum = ValueUnits.FromPercent(80m)
                });
                host.ReceiveNegotiation(new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = previous,
                    continueCheckpoints = true, infinityAvailable = true,
                }, peer);
                Assert.Single(HostAuthority.VersionWarnings);
                host.ReceiveNegotiation(new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version, modVer = HostAuthority.ModVersion, content = ContentFingerprint.Value,
                    continueCheckpoints = true, infinityAvailable = true,
                }, peer);
                Assert.Empty(HostAuthority.VersionWarnings);
            }
            finally
            {
                host.DetachNegotiation();
                DewPlayer.gamePlayers.Remove(peer);
            }
        }
    }
}
