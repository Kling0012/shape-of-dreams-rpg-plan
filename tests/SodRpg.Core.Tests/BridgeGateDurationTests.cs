using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// WindowDuration / MarkDuration lengthen one bridge pair's own gate (its Window / its Mark) and nothing else:
    /// not another bridge, not a payload's duration, and only on a pair that has that gate.
    /// Nachia pair 1 (ring.force) is a Mark pair, pair 2 (ring.insight) a Window pair.
    /// </summary>
    public sealed class BridgeGateDurationTests
    {
        private const string Nachia = "Hero_Nachia", Force = "h.nachia.ring.force", Insight = "h.nachia.ring.insight";
        private const string Heart = "St_D_HeartOfThePack", Sylvan = "St_Q_SylvanCall", Whisper = "St_R_NaturesWhisper";

        private static AuthoredStarDef Gate(string id, string bridge, string payoffMemory, GimmickParam parameter) => new AuthoredStarDef
        {
            HeroKey = Nachia, LocalStarId = id, ClusterId = "test.gate." + id, Region = ClusterRegion.Bridge(bridge), AnchorId = bridge,
            Shape = ClusterShape.Fan, RequiredStarIds = new[] { bridge },
            Effect = new ClusterStarDef { Kind = ClusterStarKind.GimmickParam, Name = new Txt("試験", "Test"), MaxRank = 1, RankCost = 1, Memory = payoffMemory,
                ScopedModifier = new ScopedModifierDef { ScopeKind = ScopeKind.EffectChannel, ScopeMemory = payoffMemory, TargetEffectIds = new[] { bridge },
                    TargetEffects = Array.Empty<GimmickEffect>(), Param = parameter, Amount = ModifierUnits.FromPercent(20m) } }
        };

        private static AuthoredStarDef[] Definitions(params AuthoredStarDef[] gates) =>
            new[] { StarClusters.ManifestBaselinePair(Nachia, Force), StarClusters.ManifestBaselinePair(Nachia, Insight) }.Concat(gates).ToArray();

        private static Build Compute(AuthoredStarDef[] definitions, params string[] purchases)
        {
            var tree = StarClusters.RegisterAuthored(Nachia, definitions).TreeFor(Nachia);
            var profile = Profile.CreateNew(2031); var allocation = profile.Hero(Nachia);
            allocation.StarXp = StarProgression.TotalXpForPoints(300);
            foreach (string bridge in new[] { Force, Insight })
            {
                var pair = PairCombos.ForBridge(bridge);
                foreach (string endpoint in new[] { pair.StarA, pair.StarB })
                {
                    AuthoredStarContractTests.AllocatePath(allocation, tree, endpoint);
                    allocation.Talents[endpoint] = 1;
                }
                AuthoredStarContractTests.AllocatePath(allocation, tree, bridge);
                Rules.AddTalentRank(profile, Nachia, bridge);
            }
            foreach (string id in purchases) Rules.AddTalentRank(profile, Nachia, id);
            return Build.Decode(Build.Compute(profile, Nachia, 0).Encode());
        }

        private static BridgeSuccessDefinition Bridge(Build build, string bridge)
        {
            string pairId = PairCombos.ForBridge(bridge).Id;
            return build.Mechanisms.Select(e => e.Spec.Bridge).Single(b => b != null && b.PairId == pairId);
        }

        private static void Restore() => StarClusters.RegisterAuthored(Nachia, Array.Empty<AuthoredStarDef>());

        [Fact]
        public void The_contract_supports_each_gate_duration_only_on_a_pair_with_that_gate()
        {
            try
            {
                Compute(Definitions());
                var window = Bridge(Compute(Definitions()), Insight); var mark = Bridge(Compute(Definitions()), Force);
                Assert.Equal(BridgeGateKind.Window, window.GateKind); Assert.Equal(BridgeGateKind.Mark, mark.GateKind);
                var windowSpec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess, Bridge = window };
                var markSpec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess, Bridge = mark };
                Assert.True(AuthoredMechanisms.Supports(windowSpec, GimmickParam.WindowDuration));
                Assert.False(AuthoredMechanisms.Supports(windowSpec, GimmickParam.MarkDuration));
                Assert.True(AuthoredMechanisms.Supports(markSpec, GimmickParam.MarkDuration));
                Assert.False(AuthoredMechanisms.Supports(markSpec, GimmickParam.WindowDuration));
            }
            finally { Restore(); }
        }

        [Fact]
        public void A_gate_duration_star_on_a_pair_without_that_gate_is_rejected_at_registration()
        {
            try
            {
                Assert.Throws<InvalidOperationException>(() => StarClusters.RegisterAuthored(Nachia, Definitions(Gate("test.mark.on.window", Insight, Whisper, GimmickParam.MarkDuration))));
                Assert.Throws<InvalidOperationException>(() => StarClusters.RegisterAuthored(Nachia, Definitions(Gate("test.window.on.mark", Force, Heart, GimmickParam.WindowDuration))));
            }
            finally { Restore(); }
        }

        [Fact]
        public void WindowDuration_lengthens_only_its_bridge_window_and_the_host_runtime_keeps_the_window_open_longer()
        {
            try
            {
                var plain = Compute(Definitions());
                var gated = Compute(Definitions(Gate("test.window", Insight, Sylvan, GimmickParam.WindowDuration)), "test.window");
                var window = Bridge(gated, Insight);
                Assert.Equal(Bridge(plain, Insight).WindowSeconds * 1.2f, window.WindowSeconds, 4);
                Assert.Equal(Bridge(plain, Insight).MarkSeconds, window.MarkSeconds);
                Assert.Equal(Bridge(plain, Force).Key, Bridge(gated, Force).Key); // the other bridge is untouched
                Assert.NotEqual(Bridge(plain, Insight).Key, window.Key);
                var roundTrip = AuthoredMechanismCodec.DecodeSpec(AuthoredMechanismCodec.EncodeSpec(new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess, Bridge = window,
                    ChannelId = window.PairId, Source = window.PayoffSource, Trigger = window.PayoffTrigger, Budget = window.Budget }));
                Assert.Equal(window.Key, roundTrip.Bridge.Key);

                var equipment = new MechanismEquipment(1, 1, new[]
                {
                    new EquippedMechanismMemory(Heart, 11, MechanismMemorySlot.Identity, true, false),
                    new EquippedMechanismMemory(Sylvan, 12, MechanismMemorySlot.Q, true, false),
                    new EquippedMechanismMemory(Whisper, 13, MechanismMemorySlot.R, true, false),
                });
                bool OpenAt(Build build, float query)
                {
                    var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { Bridge(build, Insight) });
                    var opening = new MemoryActivationEvent(1, Whisper, 1, 1, 0, MemoryEventKind.ConfirmedUse, NativePayloadKind.Skill, GeneratedOrigin.None, 1);
                    runtime.FireAttributed(opening, 0, 0, equipment, gated.MechanismEndpointRanks, new List<BridgeSuccessTransaction>());
                    return runtime.HasBridgeWindow(PairCombos.ForBridge(Insight).Id, query, equipment, gated.MechanismEndpointRanks);
                }
                float between = (Bridge(plain, Insight).WindowSeconds + window.WindowSeconds) / 2f;
                Assert.False(OpenAt(plain, between));
                Assert.True(OpenAt(gated, between));
            }
            finally { Restore(); }
        }

        [Fact]
        public void MarkDuration_lengthens_only_its_bridge_mark_and_the_host_runtime_keeps_the_mark_longer()
        {
            try
            {
                var plain = Compute(Definitions());
                var gated = Compute(Definitions(Gate("test.mark", Force, Heart, GimmickParam.MarkDuration)), "test.mark");
                var mark = Bridge(gated, Force);
                Assert.Equal(BridgeSuccessDefinition.BaseMarkSeconds * 1.2f, mark.MarkSeconds, 4);
                Assert.Equal(Bridge(plain, Force).WindowSeconds, mark.WindowSeconds);
                Assert.Equal(Bridge(plain, Insight).Key, Bridge(gated, Insight).Key);

                var equipment = new MechanismEquipment(1, 1, new[]
                {
                    new EquippedMechanismMemory(Heart, 11, MechanismMemorySlot.Identity, true, false),
                    new EquippedMechanismMemory(Sylvan, 12, MechanismMemorySlot.Q, true, false),
                    new EquippedMechanismMemory(Whisper, 13, MechanismMemorySlot.R, true, false),
                });
                bool MarkedAt(Build build, float query)
                {
                    var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { Bridge(build, Force) });
                    var opening = new MemoryActivationEvent(1, Sylvan, 1, 1, 101, MemoryEventKind.Hit, NativePayloadKind.Skill, GeneratedOrigin.None, 1);
                    runtime.FireAttributed(opening, 0, 100, equipment, gated.MechanismEndpointRanks, new List<BridgeSuccessTransaction>());
                    return runtime.HasBridgeMark(PairCombos.ForBridge(Force).Id, 101, query, equipment, gated.MechanismEndpointRanks);
                }
                Assert.True(MarkedAt(plain, 3.9f)); Assert.False(MarkedAt(plain, 4.4f));
                Assert.True(MarkedAt(gated, 4.4f)); Assert.False(MarkedAt(gated, 4.9f));
            }
            finally { Restore(); }
        }
    }
}
