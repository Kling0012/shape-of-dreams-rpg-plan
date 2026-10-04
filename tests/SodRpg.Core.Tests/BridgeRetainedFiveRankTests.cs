using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod
{
    internal sealed class St_D_DisintegratingClaw : SkillTrigger { }
}

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Retained five-rank ring centers that became marked or window pairs (Aurena renewal, Bismuth resolve, Nachia renewal),
    /// the AlliedWard bridge payload and the single documented exception to the three-rank limit.
    /// Definitions are the exact shapes tools/star-manifest/gen_cs.py emits.
    /// </summary>
    public sealed class BridgeRetainedFiveRankTests
    {
        private sealed class Case
        {
            public string Hero, Bridge, PairId, StarA, MemA, StarB, MemB, Opening, Payoff, Recipient;
            public BridgeGateKind Gate; public MemoryEventKind OpeningTrigger, PayoffTrigger; public BridgePayloadKind Kind;
            public GimmickEffect Effect = GimmickEffect.None; public int[] Values; public bool Once; public MemoryOwnership Ownership;
        }

        private static readonly Case Aurena = new Case
        {
            Hero = "Hero_Aurena", Bridge = "h.aurena.ring.renewal", PairId = "h.aurena.pair.8",
            StarA = "h.aurena.route.claw.6", MemA = "St_D_DisintegratingClaw", StarB = "h.aurena.route.golden-burst.6", MemB = "St_Q_GoldenBurst",
            Gate = BridgeGateKind.Mark, Opening = "St_Q_GoldenBurst", OpeningTrigger = MemoryEventKind.Hit,
            Payoff = "St_D_DisintegratingClaw", PayoffTrigger = MemoryEventKind.Hit, Kind = BridgePayloadKind.AlliedWard,
            Values = new[] { 400, 500, 600, 700, 800 }, Once = true
        };
        private static readonly Case BismuthResolve = new Case
        {
            Hero = "Hero_Bismuth", Bridge = "h.bismuth.ring.resolve", PairId = "h.bismuth.pair.7",
            StarA = "h.bismuth.route.prismatic-eyes.6", MemA = "St_D_PrismaticEyes", StarB = "h.bismuth.route.innocence.6", MemB = "St_QR_Innocence",
            Gate = BridgeGateKind.Mark, Opening = "St_D_PrismaticEyes", OpeningTrigger = MemoryEventKind.Hit,
            Payoff = "St_QR_Innocence", PayoffTrigger = MemoryEventKind.Hit, Kind = BridgePayloadKind.Gimmick, Effect = GimmickEffect.Sap,
            Values = new[] { 100, 200, 300, 400, 500 }, Once = true
        };
        private static readonly Case Nachia = new Case
        {
            Hero = "Hero_Nachia", Bridge = "h.nachia.ring.renewal", PairId = "h.nachia.pair.8",
            StarA = "h.nachia.route.circle-life.4", MemA = "St_D_CircleOfLife", StarB = "h.nachia.route.sylvan-call.4", MemB = "St_Q_SylvanCall",
            Gate = BridgeGateKind.Window, Opening = "St_Q_SylvanCall", OpeningTrigger = MemoryEventKind.ConfirmedUse,
            Payoff = "St_D_CircleOfLife", PayoffTrigger = MemoryEventKind.OwnedBasicAttackFired, Kind = BridgePayloadKind.Recharge,
            Recipient = "St_Q_SylvanCall", Values = new[] { 100, 200, 300, 400, 500 },
            Ownership = new MemoryOwnership { TargetMemory = "St_Q_SylvanCall", SourceMemories = new[] { "St_D_CircleOfLife" } }
        };

        private static AuthoredStarDef Center(Case c, bool flagless = false) => StarClusters.ManifestRetained(c.Hero, c.Bridge,
            new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の輪", "Probe ring"), MaxRank = 5, RankCost = 1,
                Mechanism = StarClusters.ManifestNewPair(c.Hero, c.Bridge, c.PairId, c.StarA, c.MemA, c.StarB, c.MemB, c.Gate,
                    c.Opening, c.OpeningTrigger, c.Payoff, c.PayoffTrigger, c.Kind, c.Recipient, c.Effect, 0, c.Values, c.Once) },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), c.Ownership, "docs/specs/v1.31-clusters-test.md", new[] { "C05" }, "");

        private static Build BuildFor(Case c, int ranks, params AuthoredStarDef[] extra)
        {
            var tree = StarClusters.RegisterAuthored(c.Hero, new[] { Center(c) }.Concat(extra)).TreeFor(c.Hero);
            var profile = Profile.CreateNew(1841); var allocation = profile.Hero(c.Hero);
            allocation.StarXp = StarProgression.TotalXpForPoints(300);
            foreach (string endpoint in new[] { c.StarA, c.StarB })
            {
                AuthoredStarContractTests.AllocatePath(allocation, tree, endpoint);
                allocation.Talents[endpoint] = 1;
            }
            AuthoredStarContractTests.AllocatePath(allocation, tree, c.Bridge);
            for (int rank = 0; rank < ranks; rank++) Rules.AddTalentRank(profile, c.Hero, c.Bridge);
            Assert.Equal(ranks, allocation.Talents[c.Bridge]);
            foreach (var star in extra) Rules.AddTalentRank(profile, c.Hero, star.LocalStarId);
            return Build.Decode(Build.Compute(profile, c.Hero, 0).Encode());
        }

        private static MechanismEquipment Equipment(Case c) => new MechanismEquipment(1, 1, new[]
        {
            new EquippedMechanismMemory(c.MemA, 11, c.MemA.StartsWith("St_D_", StringComparison.Ordinal) ? MechanismMemorySlot.Identity : MechanismMemorySlot.Q, true, false),
            new EquippedMechanismMemory(c.MemB, 12, c.MemB.StartsWith("St_D_", StringComparison.Ordinal) ? MechanismMemorySlot.Identity : MechanismMemorySlot.R, true, false),
        });

        private static MemoryActivationEvent Event(string memory, long activation, MemoryEventKind kind = MemoryEventKind.Hit, long victim = 101,
            NativePayloadKind payload = NativePayloadKind.Skill) =>
            new MemoryActivationEvent(1, memory, activation, activation, victim, kind, payload, GeneratedOrigin.None, 1);

        private static void Restore(string hero) => StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void Aurena_renewal_marks_with_expose_by_rank_and_pays_an_allied_ward_per_rank(int rank)
        {
            try
            {
                var build = BuildFor(Aurena, rank);
                var bridge = build.Mechanisms.Single(e => e.Spec.Bridge != null).Spec.Bridge;
                Assert.Equal(rank, bridge.Rank); Assert.True(bridge.RetainedFiveRanks);
                Assert.Equal(BridgeGateKind.Mark, bridge.GateKind);
                Assert.Equal(BridgePayloadKind.AlliedWard, bridge.BasePayoff.Kind);
                Assert.Equal(300 + 100 * rank, bridge.BasePayoff.Ward.ValueUnits);          // AlliedWard 4/5/6/7/8%
                Assert.Equal(WardRecipientKind.AlliedTravelers, bridge.BasePayoff.Ward.RecipientKind);
                Assert.Equal(ModShieldPoolKind.Allied, bridge.BasePayoff.Ward.PoolKind);
                Assert.Equal(4f, bridge.BasePayoff.Ward.DurationSeconds); Assert.Equal(3, bridge.BasePayoff.Ward.Targets);
                var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { bridge });
                var equipment = Equipment(Aurena); var ranks = build.MechanismEndpointRanks;
                var results = new List<BridgeSuccessTransaction>();
                runtime.FireAttributed(Event(Aurena.MemA, 1), 0, 100, equipment, ranks, results);
                Assert.Empty(results);                                                        // the payoff memory alone has no mark
                runtime.FireAttributed(Event(Aurena.MemB, 2), 0, 100, equipment, ranks, results);
                Assert.Empty(results);                                                        // opening only marks
                Assert.Equal((rank + 1) * 100m, runtime.BridgeExposeUnits(101, 0, equipment, ranks));   // Expose 2/3/4/5/6%
                runtime.FireAttributed(Event(Aurena.MemA, 3), 0, 100, equipment, ranks, results);
                var success = Assert.Single(results);
                Assert.Equal(BridgePayloadKind.AlliedWard, success.Payloads[0].Kind);
                Assert.Equal(300 + 100 * rank, success.Payloads[0].Ward.ValueUnits);
            }
            finally { Restore(Aurena.Hero); }
        }

        [Fact]
        public void Aurena_renewal_ward_runs_through_the_real_c12_host_path_once_per_success()
        {
            try
            {
                var build = BuildFor(Aurena, 5);
                Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0; DewPlayer.gamePlayers.Clear(); DewPhysics.Entities.Clear();
                NativeAttributedDamagePacket.Current = null; NativeAuthoredKeystonePacketFamily.Current = null;
                var hero = new Hero { maxHealth = 1000, currentHealth = 1000 };
                hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_DisintegratingClaw { owner = hero };
                hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_GoldenBurst { owner = hero };
                hero.owner = new DewPlayer { hero = hero, isHumanPlayer = true };
                DewPlayer.gamePlayers.Add(hero.owner);
                var ally = new Hero { maxHealth = 2000, currentHealth = 1000 }; DewPlayer.gamePlayers.Add(new DewPlayer { hero = ally });
                var runtime = new HostAuthority.HeroRuntime { Hero = hero, HeroKey = Aurena.Hero, Powers = new PowerRuntime(build, 0) };
                var host = new HostAuthority(); host.BindAuthored(runtime, build);
                var enemy = new Entity { Relation = EntityRelation.Enemy }; DewPhysics.Entities.Add(enemy);
                MemoryActivationEvent Hit(string memory) => host.Activation(hero, memory).Event(MemoryEventKind.Hit, host.Packet(), enemy.GetInstanceID());

                host.NotifyAuthored(runtime, Hit(Aurena.MemA), enemy); host.FlushAuthored(runtime);
                Assert.Equal(0, ally.Status.currentShield);                                   // no mark yet
                host.NotifyAuthored(runtime, Hit(Aurena.MemB), enemy); host.FlushAuthored(runtime);
                Assert.Equal(0, ally.Status.currentShield);                                   // the opening alone pays nothing
                var payoff = Hit(Aurena.MemA);
                host.NotifyAuthored(runtime, payoff, enemy); host.FlushAuthored(runtime);
                Assert.Equal(8f, ally.Status.currentShield, 4);                               // rank 5: 8% of the higher of AD/AP (100)
                Assert.Equal(8f, hero.Status.currentShield, 4);                               // the caster is included
                host.NotifyAuthored(runtime, payoff, enemy); host.FlushAuthored(runtime);
                Assert.Equal(8f, ally.Status.currentShield, 4);                               // one ward per activation
                var generated = new MemoryActivationEvent(payoff.OwnerId, payoff.SourceMemory, payoff.ActivationId, host.Packet(), payoff.VictimId,
                    payoff.EventKind, payoff.NativePayloadKind, GeneratedOrigin.Bridge, payoff.EquipmentEpoch);
                host.NotifyAuthored(runtime, generated, enemy); host.FlushAuthored(runtime);
                Assert.Equal(8f, ally.Status.currentShield, 4);
            }
            finally { Restore(Aurena.Hero); }
        }

        [Theory]
        [InlineData(1)] [InlineData(3)] [InlineData(5)]
        public void Bismuth_resolve_keeps_five_ranks_with_expose_by_rank_and_its_mark_conditioned_extra(int rank)
        {
            var extra = new AuthoredStarDef
            {
                HeroKey = BismuthResolve.Hero, LocalStarId = "bismuth.bridge.b7.n1", ClusterId = "bismuth.bridge.b7", Region = ClusterRegion.Bridge(BismuthResolve.Bridge),
                AnchorId = BismuthResolve.Bridge, Shape = ClusterShape.Fan, RequiredStarIds = new[] { BismuthResolve.Bridge },
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"), MaxRank = 1, RankCost = 1,
                    Mechanism = new AuthoredMechanismSpec
                    {
                        ChannelId = "bismuth.bridge.b7.n1", Source = MemorySelector.Parse("St_QR_Innocence"), Trigger = MemoryEventKind.Hit,
                        Budget = AttributionBudget.PerActivation, Condition = AuthoredMechanismCondition.BridgeMark, PairId = BismuthResolve.PairId,
                        RequiredMemories = new[] { "St_D_PrismaticEyes", "St_QR_Innocence" }, Once = true, Kind = AuthoredMechanismKind.Gimmick,
                        Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Wound, Value = 20m }
                    } }
            };
            try
            {
                var build = BuildFor(BismuthResolve, rank, extra);
                var bridge = build.Mechanisms.Single(e => e.Spec.Bridge != null).Spec.Bridge;
                Assert.Equal(rank, bridge.Rank); Assert.True(bridge.RetainedFiveRanks);
                Assert.Equal(rank * 100, bridge.BasePayoff.ValueUnits);                       // Sap 1/2/3/4/5%
                var conditioned = build.Mechanisms.Single(e => e.StarId == "bismuth.bridge.b7.n1").Spec;
                Assert.Equal(AuthoredMechanismCondition.BridgeMark, conditioned.Condition); Assert.Equal(BismuthResolve.PairId, conditioned.PairId);
                var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { bridge });
                var equipment = Equipment(BismuthResolve); var ranks = build.MechanismEndpointRanks;
                var results = new List<BridgeSuccessTransaction>();
                runtime.FireAttributed(Event(BismuthResolve.MemB, 1), 0, 100, equipment, ranks, results);
                Assert.Empty(results);                                                        // an unmarked enemy
                Assert.False(runtime.HasBridgeMark(BismuthResolve.PairId, 101, 0, equipment, ranks));
                runtime.FireAttributed(Event(BismuthResolve.MemA, 2), 0, 100, equipment, ranks, results);
                Assert.True(runtime.HasBridgeMark(BismuthResolve.PairId, 101, 0, equipment, ranks));
                Assert.Equal((rank + 1) * 100m, runtime.BridgeExposeUnits(101, 0, equipment, ranks));
                Assert.False(runtime.HasBridgeMark(BismuthResolve.PairId, 102, 0, equipment, ranks));    // another enemy
                runtime.FireAttributed(Event(BismuthResolve.MemB, 3), 0, 100, equipment, ranks, results);
                Assert.Equal(BismuthResolve.PairId, Assert.Single(results).PairId);
                runtime.FireAttributed(Event(BismuthResolve.MemB, 4), 4.5f, 100, equipment, ranks, results);   // the mark has expired
                Assert.Single(results);
            }
            finally { Restore(BismuthResolve.Hero); }
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void Nachia_renewal_is_a_window_pair_of_the_two_named_memories_with_all_five_ranks(int rank)
        {
            try
            {
                var build = BuildFor(Nachia, rank);
                var bridge = build.Mechanisms.Single(e => e.Spec.Bridge != null).Spec.Bridge;
                Assert.Equal(rank, bridge.Rank); Assert.True(bridge.RetainedFiveRanks);
                Assert.Equal(BridgeGateKind.Window, bridge.GateKind);
                Assert.Equal(new[] { "h.nachia.route.circle-life.4", "h.nachia.route.sylvan-call.4" }, bridge.Endpoints.Select(e => e.StarId).ToArray());
                var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { bridge });
                var equipment = Equipment(Nachia); var ranks = build.MechanismEndpointRanks;
                var results = new List<BridgeSuccessTransaction>();
                var fired = Event(Nachia.MemA, 1, MemoryEventKind.OwnedBasicAttackFired, 0, NativePayloadKind.MainBasicAttack);
                runtime.FireAttributed(fired, 0, 0, equipment, ranks, results, hasOwnedSummon: true);
                Assert.Empty(results);                                                        // no window yet
                runtime.FireAttributed(Event(Nachia.MemB, 2, MemoryEventKind.ConfirmedUse, 0), 0, 0, equipment, ranks, results);
                Assert.Empty(results);                                                        // use only opens the window
                Assert.True(runtime.HasBridgeWindow(Nachia.PairId, 1f, equipment, ranks));
                Assert.Equal(0, runtime.BridgeExposeUnits(0, 1f, equipment, ranks));          // no mark in a window pair
                runtime.FireAttributed(Event(Nachia.MemA, 3, MemoryEventKind.OwnedBasicAttackFired, 0, NativePayloadKind.MainBasicAttack),
                    1f, 0, equipment, ranks, results, hasOwnedSummon: true);
                var success = Assert.Single(results); var requests = new List<DirectedRechargeRequest>();
                success.CreateRechargeRequests(equipment, ranks, requests);
                Assert.Equal(10 - 10 * rank / 100f, 10 - 20 * Assert.Single(requests).NativeRatio(10, 20), 5);
                var late = new List<BridgeSuccessTransaction>();
                runtime.FireAttributed(Event(Nachia.MemA, 4, MemoryEventKind.OwnedBasicAttackFired, 0, NativePayloadKind.MainBasicAttack),
                    4.5f, 0, equipment, ranks, late, hasOwnedSummon: true);
                Assert.Empty(late);                                                           // the four second window is over
            }
            finally { Restore(Nachia.Hero); }
        }

        [Fact]
        public void The_five_rank_exception_is_only_for_a_retained_ring_center_and_everything_else_stays_limited_to_three_ranks()
        {
            var selector = MemorySelector.Parse("St_QR_Innocence");
            BridgeSuccessDefinition Pair(BridgeGateKind gate, int rank, bool retained) => new BridgeSuccessDefinition("probe.pair",
                new[] { new BridgeEndpointRequirement("a", "St_QR_Innocence"), new BridgeEndpointRequirement("b", "St_M_Sprint") }, rank, gate,
                selector, MemoryEventKind.Hit, selector, MemoryEventKind.Hit,
                new BridgePayload("probe.pair.base", BridgePayloadKind.Recharge, new[] { 100 }, recipient: MemorySelector.Parse("St_M_Sprint")),
                Array.Empty<BridgePayload>(), retainedFiveRanks: retained);
            Pair(BridgeGateKind.Mark, 3, false); Pair(BridgeGateKind.Window, 3, false); Pair(BridgeGateKind.DirectReceiver, 5, false);
            Assert.Throws<ArgumentException>(() => Pair(BridgeGateKind.Mark, 4, false));
            Assert.Throws<ArgumentException>(() => Pair(BridgeGateKind.Window, 4, false));
            Assert.Throws<ArgumentException>(() => Pair(BridgeGateKind.Mark, 6, true));
            Assert.Throws<ArgumentException>(() => Pair(BridgeGateKind.DirectReceiver, 5, true));   // direct pairs never need the exception
            Assert.True(Pair(BridgeGateKind.Mark, 5, true).RetainedFiveRanks);

            // A new (non-retained) star cannot carry the exception, and a retained five-rank center cannot omit it.
            var spec = StarClusters.ManifestNewPair(BismuthResolve.Hero, BismuthResolve.Bridge, BismuthResolve.PairId, BismuthResolve.StarA, BismuthResolve.MemA,
                BismuthResolve.StarB, BismuthResolve.MemB, BridgeGateKind.Mark, BismuthResolve.Opening, MemoryEventKind.Hit, BismuthResolve.Payoff, MemoryEventKind.Hit,
                BridgePayloadKind.Gimmick, null, GimmickEffect.Sap, 0, BismuthResolve.Values, true);
            Assert.True(spec.Bridge.RetainedFiveRanks);
            var fresh = new AuthoredStarDef { HeroKey = BismuthResolve.Hero, LocalStarId = "probe.fresh.pair", ClusterId = "probe.fresh", Region = ClusterRegion.Bridge("probe.fresh.pair"),
                AnchorId = "h.bismuth.ring.resolve", Shape = ClusterShape.Fan,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"), MaxRank = 1, RankCost = 1, Mechanism = spec } };
            try { Assert.ThrowsAny<InvalidOperationException>(() => StarClusters.RegisterAuthored(BismuthResolve.Hero, new[] { fresh })); }
            finally { Restore(BismuthResolve.Hero); }

            // Transport and host admission: a flagged definition is only accepted while its pair is a registered retained five-rank pair.
            Build decoded;
            try { decoded = BuildFor(BismuthResolve, 5); }
            finally { Restore(BismuthResolve.Hero); }
            Assert.True(decoded.Mechanisms.Single(e => e.Spec.Bridge != null).Spec.Bridge.RetainedFiveRanks);   // transported
            Mirror.NetworkServer.active = true;
            var host = new HostAuthority();
            var runtime = new HostAuthority.HeroRuntime { Hero = new Hero(), HeroKey = BismuthResolve.Hero, Powers = new PowerRuntime(decoded, 0) };
            Assert.Throws<InvalidOperationException>(() => host.BindAuthored(runtime, decoded));
            try { StarClusters.RegisterAuthored(BismuthResolve.Hero, new[] { Center(BismuthResolve) }); host.BindAuthored(runtime, decoded); }
            finally { Restore(BismuthResolve.Hero); }
        }


        [Fact]
        public void A_registered_legacy_pair_center_is_its_typed_base_and_a_self_conditioned_gimmick_is_rejected()
        {
            const string hero = "Hero_Aurena", bridge = "h.aurena.ring.force", pair = "h.aurena.pair.1";
            AuthoredStarDef LegacyCenter(AuthoredMechanismSpec mechanism) => StarClusters.ManifestRetained(hero, bridge,
                new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("夢輪の剛力", "Dream Ring Strength"), MaxRank = 3, RankCost = 1, Mechanism = mechanism },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), null, "docs/specs/v1.31-clusters-aurena.md", new[] { "C05" }, "");
            var extra = new AuthoredStarDef
            {
                HeroKey = hero, LocalStarId = "aurena.bridge.claw-golden.n1", ClusterId = "aurena.bridge.claw-golden", Region = ClusterRegion.Bridge(bridge),
                AnchorId = bridge, Shape = ClusterShape.Fan, RequiredStarIds = new[] { bridge },
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の盾", "Probe ward"), MaxRank = 1, RankCost = 1,
                    Mechanism = new AuthoredMechanismSpec
                    {
                        ChannelId = "aurena.bridge.claw-golden.n1", Source = MemorySelector.Parse("St_D_DisintegratingClaw"), Trigger = MemoryEventKind.Hit,
                        Budget = AttributionBudget.PerActivation, Condition = AuthoredMechanismCondition.BridgeSuccess, PairId = pair,
                        RequiredMemories = new[] { "St_D_DisintegratingClaw", "St_Q_GoldenBurst" }, Once = true, Kind = AuthoredMechanismKind.AlliedWard,
                        Ward = new AlliedWardDefinition("aurena.bridge.claw-golden.n1", WardRecipientKind.AlliedTravelers, WardAmountBasis.CasterMaxOffense,
                            ModShieldPoolKind.Allied, 500, true)
                    } }
            };
            try
            {
                StarClusters.RegisterAuthored(hero, new[] { LegacyCenter(StarClusters.ManifestPair(hero, bridge)), extra });
                Assert.Equal(pair, PairCombos.ForBridge(bridge).Id);
            }
            finally { Restore(hero); }
            var selfConditioned = new AuthoredMechanismSpec
            {
                ChannelId = bridge, Source = MemorySelector.Parse("St_D_DisintegratingClaw"), Trigger = MemoryEventKind.Hit, Condition = AuthoredMechanismCondition.BridgeSuccess,
                PairId = pair, RequiredMemories = new[] { "St_D_DisintegratingClaw", "St_Q_GoldenBurst" }, Once = true, ValuesByRank = new[] { 4000, 7000, 10000 },
                Kind = AuthoredMechanismKind.Gimmick, Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Burst, Value = 40m }
            };
            try { Assert.ThrowsAny<InvalidOperationException>(() => StarClusters.RegisterAuthored(hero, new[] { LegacyCenter(selfConditioned) })); }
            finally { Restore(hero); }
        }

        [Fact]
        public void Aurena_b2_migrates_the_mark_trigger_from_crit_to_hit_only_when_the_design_override_is_given()
        {
            const string hero = "Hero_Aurena", bridge = "h.aurena.ring.insight";
            Assert.Equal(MemoryEventKind.CriticalHit, StarClusters.ManifestPair(hero, bridge).Bridge.OpeningTrigger);
            Assert.Equal(MemoryEventKind.Hit, StarClusters.ManifestPair(hero, bridge, openingOverride: MemoryEventKind.Hit).Bridge.OpeningTrigger);
        }

        [Fact]
        public void The_five_rank_pairs_change_negotiated_content_and_are_visible_only_through_the_registry()
        {
            string prior = ContentFingerprint.Value; int legacy = PairCombos.All.Count;
            foreach (var c in new[] { Aurena, BismuthResolve, Nachia })
            {
                try
                {
                    StarClusters.RegisterAuthored(c.Hero, new[] { Center(c) });
                    var pair = PairCombos.ForBridge(c.Bridge);
                    Assert.NotNull(pair); Assert.Equal(c.PairId, pair.Id); Assert.Equal(c.Hero, pair.HeroKey);
                    Assert.Equal(new[] { c.StarA, c.StarB }.OrderBy(x => x, StringComparer.Ordinal), new[] { pair.StarA, pair.StarB });
                    Assert.NotEqual(prior, ContentFingerprint.Value);
                    Assert.Equal(legacy, PairCombos.All.Count);
                }
                finally { Restore(c.Hero); }
                Assert.Equal(prior, ContentFingerprint.Value);
            }
        }
    }
}
