using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class NegotiationNativeTests
    {
        [Fact]
        public void Host_requires_matching_protocol_and_full_registry_before_accepting_builds()
        {
            var host = new HostAuthority();
            var actor = host.RegisterNegotiation();
            var peer = new DewPlayer();
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = 12, content = ContentFingerprint.Value, continueCheckpoints = true }, peer);
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            // 中断チェックポイント非対応の旧クライアントは登録が同じでも受け入れない（Protocol 17 相互確認）。
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = ContentFingerprint.Value }, peer);
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = "different-registry", continueCheckpoints = true }, peer);
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = ContentFingerprint.Value, continueCheckpoints = true }, peer);
            Assert.True(host.AcceptsNegotiatedBuild(peer));
            var reply = Assert.IsType<DreamforgeHelloMsg>(actor.ClientMessages.Last());
            Assert.True(ContentFingerprint.Matches(reply.protocol, reply.content, Protocol.Version));
            host.DetachNegotiation();
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            Assert.False(actor.ServerHandlers.ContainsKey(typeof(DreamforgeHelloMsg)));
        }

        [Fact]
        public void Cap_registry_change_revokes_old_negotiation_until_both_sides_renegotiate()
        {
            var host = new HostAuthority();
            host.RegisterNegotiation();
            var peer = new DewPlayer();
            string previous = ContentFingerprint.Value;
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = previous, continueCheckpoints = true }, peer);
            Assert.True(host.AcceptsNegotiatedBuild(peer));
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile {
                Id = "integration.negotiation.host", Kind = LinkKind.MemoryDamage,
                Maximum = ValueUnits.FromPercent(80m)
            });
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = previous, continueCheckpoints = true }, peer);
            Assert.False(host.AcceptsNegotiatedBuild(peer));
            host.ReceiveNegotiation(new DreamforgeHelloMsg { protocol = Protocol.Version, content = ContentFingerprint.Value, continueCheckpoints = true }, peer);
            Assert.True(host.AcceptsNegotiatedBuild(peer));
            host.DetachNegotiation();
        }
    }
}
