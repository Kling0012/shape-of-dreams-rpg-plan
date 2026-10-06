using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod
{
    // Native doubles for the Bismuth renewal pair (a Q/R memory and the movement receiver).
    internal sealed class St_QR_Innocence : SkillTrigger { }
    internal sealed class St_M_Sprint : SkillTrigger { }
}

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// Redesigned outer bridges that never were PairCombos entries (v1.31): Bismuth renewal is a real authored
    /// pair, Mist renewal is a receiver-only bridge with explicitly declared ownership and no pair at all.
    /// Definitions are the exact shapes tools/star-manifest/gen_cs.py emits.
    /// </summary>
    public sealed class BridgeRenewalAuthoredTests
    {
        private const string Bismuth = "Hero_Bismuth", BismuthBridge = "h.bismuth.ring.renewal", BismuthPair = "h.bismuth.pair.8";
        private const string Innocence = "St_QR_Innocence", Sprint = "St_M_Sprint";
        private const string InnocenceStar = "h.bismuth.route.innocence.6", SprintStar = "h.bismuth.route.distorting-sprint.6";
        private const string Mist = "Hero_Mist", MistBridge = "h.mist.ring.renewal", Fleche = "St_Q_Fleche", Lunge = "St_Q_Lunge";
        private static readonly int[] RankValues = { 100, 200, 300, 400, 500 };

        // ---- Bismuth b8 ---------------------------------------------------------------------------------------

        private static AuthoredStarDef BismuthCenter() => StarClusters.ManifestRetained(Bismuth, BismuthBridge,
            new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("夢輪の再生", "Dream Ring Renewal"), MaxRank = 5, RankCost = 1,
                Mechanism = StarClusters.ManifestNewDirectRechargePair(Bismuth, BismuthBridge, BismuthPair, InnocenceStar, Innocence,
                    SprintStar, Sprint, Innocence, MemoryEventKind.Hit, Sprint, RankValues, true) },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(),
            new MemoryOwnership { TargetMemory = Sprint, SourceMemories = new[] { Innocence } },
            "docs/specs/v1.31-clusters-bismuth.md", new[] { "C01", "C02", "C04" }, "");

        // bismuth.bridge.b8.n1: G(I,Hit,ReceiverRecharge,2,1,0)->S, once per I activation, only on the pair's success.
        private static AuthoredStarDef BismuthExtra() => new AuthoredStarDef
        {
            HeroKey = Bismuth, LocalStarId = "bismuth.bridge.b8.n1", ClusterId = "bismuth.bridge.b8", Region = ClusterRegion.Bridge(BismuthBridge),
            AnchorId = BismuthBridge, Shape = ClusterShape.Fan, RequiredStarIds = new[] { BismuthBridge },
            MemoryOwnership = new MemoryOwnership { TargetMemory = Sprint, SourceMemories = new[] { Innocence } },
            Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("色重ねの小道", "Path of Layered Hues"), MaxRank = 1, RankCost = 1,
                Mechanism = new AuthoredMechanismSpec
                {
                    ChannelId = "bismuth.bridge.b8.n1", Source = MemorySelector.Parse(Innocence), Trigger = MemoryEventKind.Hit,
                    Budget = AttributionBudget.PerActivation, Condition = AuthoredMechanismCondition.BridgeSuccess, PairId = BismuthPair,
                    RequiredMemories = new[] { Innocence, Sprint }, Once = true, Kind = AuthoredMechanismKind.DirectedRecharge,
                    Recharge = new DirectedRechargeChannel("bismuth.bridge.b8.n1", MemorySelector.Parse(Innocence), MemoryEventKind.Hit,
                        MemorySelector.Parse(Sprint), new[] { 200 }, AttributionBudget.PerActivation, probabilityUnits: 5000,
                        condition: RechargeConditionKind.ElementTypesAtLeast, requiredElementTypes: 2)
                } }
        };

        private static void Restore(string hero) => StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());

        private static Build BuildFor(int centerRanks, bool dropEndpointAfterPurchase = false)
        {
            var tree = StarClusters.RegisterAuthored(Bismuth, new[] { BismuthCenter(), BismuthExtra() }).TreeFor(Bismuth);
            var profile = Profile.CreateNew(1831); var allocation = profile.Hero(Bismuth);
            allocation.StarXp = StarProgression.TotalXpForPoints(300);
            foreach (string endpoint in new[] { InnocenceStar, SprintStar })
            {
                AuthoredStarContractTests.AllocatePath(allocation, tree, endpoint);
                allocation.Talents[endpoint] = 1;
            }
            AuthoredStarContractTests.AllocatePath(allocation, tree, BismuthBridge);
            for (int rank = 0; rank < centerRanks; rank++) Rules.AddTalentRank(profile, Bismuth, BismuthBridge);
            Assert.Equal(centerRanks, allocation.Talents[BismuthBridge]);
            Rules.AddTalentRank(profile, Bismuth, "bismuth.bridge.b8.n1"); Assert.Equal(1, allocation.Talents["bismuth.bridge.b8.n1"]);
            if (dropEndpointAfterPurchase) allocation.Talents.Remove(SprintStar);
            return Build.Decode(Build.Compute(profile, Bismuth, 0).Encode());
        }

        private static MechanismEquipment Equipment(bool movement = true) => new MechanismEquipment(1, 1, new[]
        {
            new EquippedMechanismMemory("St_D_PrismaticEyes", 11, MechanismMemorySlot.Identity, true, false),
            new EquippedMechanismMemory(Innocence, 12, MechanismMemorySlot.Q, true, false),
        }.Concat(movement ? new[] { new EquippedMechanismMemory(Sprint, 13, MechanismMemorySlot.Movement, false, false) }
            : Array.Empty<EquippedMechanismMemory>()));

        private static MemoryActivationEvent Event(string memory, long activation, MemoryEventKind kind = MemoryEventKind.Hit, long victim = 101) =>
            new MemoryActivationEvent(1, memory, activation, activation, victim, kind, NativePayloadKind.Skill, GeneratedOrigin.None, 1);

        [Fact]
        public void Bismuth_renewal_is_a_registered_authored_pair_beside_the_untouched_legacy_pairs_and_changes_negotiated_content()
        {
            int legacy = PairCombos.All.Count;
            string prior = ContentFingerprint.Value;
            Assert.Null(PairCombos.ForBridge(BismuthBridge));
            try
            {
                StarClusters.RegisterAuthored(Bismuth, new[] { BismuthCenter(), BismuthExtra() });
                var pair = PairCombos.ForBridge(BismuthBridge);
                Assert.NotNull(pair);
                Assert.Same(pair, PairCombos.Get(BismuthPair));
                Assert.Equal(BismuthPair, pair.Id); Assert.Equal(Bismuth, pair.HeroKey); Assert.Equal(BismuthBridge, pair.BridgeId);
                // Registered endpoints are canonically ordered by star id.
                Assert.Equal(new[] { SprintStar, InnocenceStar }, new[] { pair.StarA, pair.StarB });
                Assert.Equal(new[] { Sprint, Innocence }, new[] { pair.RouteA, pair.RouteB });
                var definition = pair.AuthoredDefinition;
                Assert.Equal(BridgeGateKind.DirectReceiver, definition.GateKind);
                Assert.Equal(MemoryEventKind.Hit, definition.PayoffTrigger);
                Assert.Equal(AttributionBudget.PerActivation, definition.Budget);
                Assert.Equal(new[] { SprintStar, InnocenceStar }, definition.Endpoints.Select(e => e.StarId).ToArray());
                Assert.Equal(BridgePayloadKind.Recharge, definition.BasePayoff.Kind);
                Assert.Equal(Sprint, definition.BasePayoff.Recipient.Memory);
                Assert.Empty(definition.Extras);
                // The 62 legacy combos are not extended; the registered one is only visible through the registry.
                Assert.Equal(legacy, PairCombos.All.Count);
                Assert.DoesNotContain(PairCombos.All, p => p.Id == BismuthPair);
                Assert.Contains(PairCombos.RegisteredAll, p => p.Id == BismuthPair);
                Assert.Equal(1, PairCombos.RegisteredAll.Count(p => p.HeroKey == Bismuth && p.BridgeIndex == 0));
                Assert.NotEqual(prior, ContentFingerprint.Value);
            }
            finally { Restore(Bismuth); }
            Assert.Null(PairCombos.ForBridge(BismuthBridge));
            Assert.Null(PairCombos.Get(BismuthPair));
            Assert.Equal(prior, ContentFingerprint.Value);
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void Bismuth_renewal_keeps_all_five_ranks_through_transport_and_recharges_exactly_that_percent_of_the_remaining_cooldown(int rank)
        {
            try
            {
                var build = BuildFor(rank);
                var entry = build.Mechanisms.Single(e => e.Spec.Bridge != null);
                var bridge = entry.Spec.Bridge;
                Assert.Equal(BismuthPair, bridge.PairId); Assert.Equal(rank, bridge.Rank);
                Assert.Equal(BridgeGateKind.DirectReceiver, bridge.GateKind);
                Assert.Equal(rank * 100, bridge.BasePayoff.ValueUnits);
                Assert.Equal(1, build.MechanismEndpointRanks[InnocenceStar]); Assert.Equal(1, build.MechanismEndpointRanks[SprintStar]);
                var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { bridge });
                var equipment = Equipment(); var results = new List<BridgeSuccessTransaction>();
                runtime.FireAttributed(Event(Innocence, 1), 0, 100, equipment, build.MechanismEndpointRanks, results);
                var transaction = Assert.Single(results); var requests = new List<DirectedRechargeRequest>();
                transaction.CreateRechargeRequests(equipment, build.MechanismEndpointRanks, requests);
                Assert.Equal(10 - 10 * rank / 100f, 10 - 20 * Assert.Single(requests).NativeRatio(10, 20), 5);
            }
            finally { Restore(Bismuth); }
        }



        [Fact]
        public void Bismuth_renewal_runs_through_the_real_host_dispatch()
        {
            try
            {
                var build = BuildFor(5);
                Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0; DewPlayer.gamePlayers.Clear(); DewPhysics.Entities.Clear();
                NativeAttributedDamagePacket.Current = null; NativeAuthoredKeystonePacketFamily.Current = null;
                var hero = new Hero();
                hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_Resolve { owner = hero };
                hero.Skill.Skills[HeroSkillLocation.Q] = new St_QR_Innocence { owner = hero };
                hero.Skill.Skills[HeroSkillLocation.Movement] = new St_M_Sprint { owner = hero };
                hero.owner = new DewPlayer { hero = hero, isHumanPlayer = true };
                var runtime = new HostAuthority.HeroRuntime { Hero = hero, HeroKey = Bismuth, Powers = new PowerRuntime(build, 0) };
                var host = new HostAuthority(); host.BindAuthored(runtime, build);
                var movement = hero.Skill.GetSkill(HeroSkillLocation.Movement);
                var enemy = new Entity { Relation = EntityRelation.Enemy }; DewPhysics.Entities.Add(enemy);
                MemoryActivationEvent Hit(string memory, MemoryEventKind kind = MemoryEventKind.Hit) =>
                    host.Activation(hero, memory).Event(kind, host.Packet(), enemy.GetInstanceID());

                host.NotifyAuthored(runtime, Hit(Innocence, MemoryEventKind.Kill), enemy); host.FlushAuthored(runtime);
                Assert.Equal(10, movement.currentConfigUnscaledCooldownTime);                // a kill is not this pair's trigger
                var hit = Hit(Innocence);
                host.NotifyAuthored(runtime, hit, enemy); host.FlushAuthored(runtime);
                Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);           // rank five: 5% of the remaining cooldown
                host.NotifyAuthored(runtime, hit, enemy); host.FlushAuthored(runtime);
                Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);           // the same activation pays once
                var generated = new MemoryActivationEvent(hit.OwnerId, hit.SourceMemory, hit.ActivationId, host.Packet(), hit.VictimId,
                    hit.EventKind, hit.NativePayloadKind, GeneratedOrigin.Bridge, hit.EquipmentEpoch);
                host.NotifyAuthored(runtime, generated, enemy); host.FlushAuthored(runtime);
                Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);           // generated payoffs cannot start a pair
                host.NotifyAuthored(runtime, Hit(Innocence), enemy); host.FlushAuthored(runtime);
                Assert.Equal(9.025f, movement.currentConfigUnscaledCooldownTime, 5);         // a new activation pays again, on the remaining time
                hero.Skill.Skills.Remove(HeroSkillLocation.Movement);                        // receiver unequipped: nothing to recharge
                var other = new St_M_Sprint { owner = hero, currentConfigUnscaledCooldownTime = 10 };
                host.NotifyAuthored(runtime, Hit(Innocence), enemy); host.FlushAuthored(runtime);
                Assert.Equal(10, other.currentConfigUnscaledCooldownTime);
            }
            finally { Restore(Bismuth); }
        }

        [Fact]
        public void A_marked_pair_still_rejects_more_than_three_ranks_and_unresolved_bridges_stay_unregistrable()
        {
            var selector = MemorySelector.Parse(Innocence);
            BridgeSuccessDefinition Marked(int rank) => new BridgeSuccessDefinition("probe.marked",
                new[] { new BridgeEndpointRequirement("a", Innocence), new BridgeEndpointRequirement("b", Sprint) }, rank, BridgeGateKind.Mark,
                selector, MemoryEventKind.Hit, selector, MemoryEventKind.Hit,
                new BridgePayload("probe.marked.base", BridgePayloadKind.Recharge, new[] { 100 }, recipient: MemorySelector.Parse(Sprint)), Array.Empty<BridgePayload>());
            Marked(3);
            Assert.Throws<ArgumentException>(() => Marked(4));
            // Aurena renewal, Bismuth resolve and Nachia renewal have neither a registered pair nor an ownership declaration.
            foreach (var (hero, bridge, memory) in new[] { ("Hero_Aurena", "h.aurena.ring.renewal", "St_Q_GoldenBurst"),
                (Bismuth, "h.bismuth.ring.resolve", Innocence), ("Hero_Nachia", "h.nachia.ring.renewal", "St_Q_SylvanCall") })
            {
                var star = new AuthoredStarDef { HeroKey = hero, LocalStarId = "probe.unresolved.e1", ClusterId = "probe.unresolved",
                    Region = ClusterRegion.Bridge(bridge), AnchorId = bridge, Shape = ClusterShape.Fan, RequiredStarIds = new[] { bridge },
                    Effect = new ClusterStarDef { Kind = ClusterStarKind.MemoryDamage, Name = new Txt("試験", "Probe"), MaxRank = 1, RankCost = 1,
                        Memory = memory, Amount = 1 } };
                Assert.Contains("registered real pair", Assert.Throws<InvalidOperationException>(() => StarClusters.RegisterAuthored(hero, new[] { star })).Message);
                Restore(hero);
            }
        }

        // ---- Mist renewal: receiver-only bridge, no pair -------------------------------------------------------

        private static AuthoredStarDef MistStar(string id, ClusterStarDef effect, string[] edges, bool entry = false, string[] required = null,
            bool receiverOnly = true, string[] sources = null) => new AuthoredStarDef
        {
            HeroKey = Mist, LocalStarId = id, ClusterId = "mist.bridge.renewal", Region = ClusterRegion.Bridge(MistBridge),
            AnchorId = entry ? MistBridge : null, Shape = ClusterShape.Ring,
            RequiredStarIds = required ?? (entry ? new[] { MistBridge } : Array.Empty<string>()),
            Edges = edges.Select(e => new AuthoredStarEdge(id, e)).ToArray(), ReceiverOnlyBridge = receiverOnly,
            MemoryOwnership = new MemoryOwnership { TargetMemory = Fleche, SourceMemories = sources ?? new[] { Lunge } }, Effect = effect
        };

        private static ClusterStarDef Damage(string memory) => new ClusterStarDef { Kind = ClusterStarKind.MemoryDamage,
            Name = new Txt("試験", "Probe"), MaxRank = 1, RankCost = 1, Memory = memory, Amount = 1 };

        private static ClusterStarDef Heal(string memory, string channel) => new ClusterStarDef { Kind = ClusterStarKind.Notable,
            Name = new Txt("試験の息", "Probe breath"), MaxRank = 1, RankCost = 1,
            Mechanism = new AuthoredMechanismSpec { ChannelId = channel, Source = MemorySelector.Parse(memory), Trigger = MemoryEventKind.Kill,
                Budget = AttributionBudget.PerKill, Kind = AuthoredMechanismKind.Gimmick,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnKill, Effect = GimmickEffect.Heal, Value = 1m } } };

        private static AuthoredStarDef[] MistCluster() => new[]
        {
            MistStar("mist.bridge.renewal.e1", Damage(Fleche), new[] { "mist.bridge.renewal.e2" }, entry: true),
            MistStar("mist.bridge.renewal.e2", Damage(Fleche), new[] { "mist.bridge.renewal.e3" }, required: new[] { "mist.bridge.renewal.e1" }),
            MistStar("mist.bridge.renewal.e3", Damage(Fleche), new[] { "mist.bridge.renewal.n1" }, required: new[] { "mist.bridge.renewal.e2" }),
            MistStar("mist.bridge.renewal.n1", Heal(Fleche, "mist.bridge.renewal.n1"), Array.Empty<string>(), required: new[] { "mist.bridge.renewal.e3" }),
            MistStar("mist.bridge.renewal.e4", Damage(Lunge), new[] { "mist.bridge.renewal.e5" }, entry: true),
            MistStar("mist.bridge.renewal.e5", Damage(Lunge), new[] { "mist.bridge.renewal.e6" }, required: new[] { "mist.bridge.renewal.e4" }),
            MistStar("mist.bridge.renewal.e6", Damage(Lunge), new[] { "mist.bridge.renewal.n2" }, required: new[] { "mist.bridge.renewal.e5" }),
            MistStar("mist.bridge.renewal.n2", Heal(Lunge, "mist.bridge.renewal.n2"), Array.Empty<string>(), required: new[] { "mist.bridge.renewal.e6" }),
        };

        [Fact]
        public void Mist_renewal_registers_as_a_receiver_only_bridge_without_any_pair_and_runs_in_a_real_build()
        {
            string prior = ContentFingerprint.Value;
            int legacyMist = PairCombos.RegisteredAll.Count(p => p.HeroKey == Mist);
            Assert.Equal(7, legacyMist);
            try
            {
                var tree = StarClusters.RegisterAuthored(Mist, MistCluster()).TreeFor(Mist);
                Assert.Null(PairCombos.ForBridge(MistBridge));                                   // no fake pair
                Assert.Equal(legacyMist, PairCombos.RegisteredAll.Count(p => p.HeroKey == Mist));
                Assert.NotEqual(prior, ContentFingerprint.Value);
                var profile = Profile.CreateNew(1832); var allocation = profile.Hero(Mist);
                allocation.StarXp = StarProgression.TotalXpForPoints(300);
                AuthoredStarContractTests.AllocatePath(allocation, tree, MistBridge);
                allocation.Talents[MistBridge] = 1;
                foreach (string id in new[] { "mist.bridge.renewal.e1", "mist.bridge.renewal.e2", "mist.bridge.renewal.e3", "mist.bridge.renewal.n1",
                    "mist.bridge.renewal.e4", "mist.bridge.renewal.e5", "mist.bridge.renewal.e6", "mist.bridge.renewal.n2" })
                    { Rules.AddTalentRank(profile, Mist, id); Assert.Equal(1, allocation.Talents[id]); }
                var build = Build.Decode(Build.Compute(profile, Mist, 0).Encode());
                foreach (string id in new[] { "mist.bridge.renewal.n1", "mist.bridge.renewal.n2" })
                {
                    var spec = build.Mechanisms.Single(e => e.StarId == id).Spec;
                    Assert.Equal(AuthoredMechanismCondition.Always, spec.Condition);               // no pair success to wait for
                    Assert.Null(spec.PairId); Assert.Equal(MemoryEventKind.Kill, spec.Trigger);
                }
                Assert.Empty(build.PairCombos.Where(e => e.Def.BridgeId == MistBridge));
                Assert.DoesNotContain(build.Mechanisms, e => e.Spec.Bridge != null);
            }
            finally { Restore(Mist); }
            Assert.Equal(prior, ContentFingerprint.Value);
        }

        [Fact]
        public void Mist_renewal_ownership_is_explicit_and_neither_widens_nor_replaces_the_pair_requirement()
        {
            try
            {
                var cluster = MistCluster();
                // A memory the cluster did not declare is rejected.
                var foreign = MistStar("mist.bridge.renewal.e1", Damage("St_R_Parry"), Array.Empty<string>(), entry: true);
                Assert.ThrowsAny<InvalidOperationException>(() => StarClusters.RegisterAuthored(Mist, new[] { foreign }));
                // Without the receiver-only declaration the same cluster needs a real pair and is rejected.
                var undeclared = MistStar("mist.bridge.renewal.e1", Damage(Fleche), Array.Empty<string>(), entry: true, receiverOnly: false);
                Assert.ThrowsAny<InvalidOperationException>(() => StarClusters.RegisterAuthored(Mist, new[] { undeclared }));
                // A declaration with no verified source list is rejected too.
                var noSources = MistStar("mist.bridge.renewal.e1", Damage(Fleche), Array.Empty<string>(), entry: true, sources: Array.Empty<string>());
                Assert.ThrowsAny<InvalidOperationException>(() => StarClusters.RegisterAuthored(Mist, new[] { noSources }));
                // A declaration can only hang on an existing real bridge anchor.
                var missingAnchor = MistStar("mist.bridge.renewal.e1", Damage(Fleche), Array.Empty<string>(), entry: true);
                missingAnchor.Region = ClusterRegion.Bridge("h.mist.ring.missing"); missingAnchor.AnchorId = "h.mist.ring.missing";
                missingAnchor.RequiredStarIds = Array.Empty<string>();
                Assert.ThrowsAny<Exception>(() => StarClusters.RegisterAuthored(Mist, new[] { missingAnchor }));
                StarClusters.RegisterAuthored(Mist, cluster);
            }
            finally { Restore(Mist); }
        }
    }
}
