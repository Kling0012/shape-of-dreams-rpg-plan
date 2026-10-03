using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class AuthoredMechanismIntegrationTests
    {
        private const string Hero = "Hero_Yubar";
        private const string Q = "St_Q_EtherealInfluence";
        private const string R = "St_R_Tranquility";
        private const string Movement = "St_M_Flicker";
        private const string Root = "outer.authoredprobe.s1";
        private sealed class ShieldHandle { internal float Amount, Seconds; internal bool Alive = true; }
        private sealed class ShieldAdapter : IModShieldAdapter<ShieldHandle>
        {
            public bool IsAlive(ShieldHandle handle) => handle.Alive && handle.Amount > 0;
            public float Remaining(ShieldHandle handle) => handle.Amount;
            public float ProcessRaw(ShieldHandle handle, float rawAmount) => rawAmount;
            public ShieldHandle CreateRaw(float rawAmount, float seconds, float processedCap) =>
                new ShieldHandle { Amount = Math.Min(rawAmount, processedCap), Seconds = seconds };
            public void SetProcessed(ShieldHandle handle, float amount) => handle.Amount = amount;
            public void Refresh(ShieldHandle handle, float seconds) => handle.Seconds = seconds;
            public void Destroy(ShieldHandle handle) => handle.Alive = false;
        }
        private static AuthoredStarDef Node(string id, AuthoredMechanismSpec mechanism = null, ClusterStarDef effect = null) => new AuthoredStarDef
        {
            LocalStarId = id, HeroKey = Hero, ClusterId = "outer.authoredprobe", Region = ClusterRegion.Outer,
            AnchorId = id == Root ? null : Root, Shape = ClusterShape.Fan,
            Effect = effect ?? new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"), Mechanism = mechanism },
        };
        private static AuthoredStarDef RootNode() => Node(Root, effect: new ClusterStarDef
        { Kind = ClusterStarKind.Stat, Name = new Txt("試験", "Probe"), Stat = Stat.Armor, Amount = 1 });
        private static Profile Register(params AuthoredStarDef[] stars)
        {
            StarClusters.RegisterAuthored(Hero, new[] { RootNode() }.Concat(stars));
            var profile = Profile.CreateNew(1703);
            profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(300);
            foreach (var star in stars)
            {
                AuthoredStarContractTests.AllocatePath(profile.Hero(Hero), HeroSigils.TreeFor(Hero), star.LocalStarId);
                Rules.AddTalentRank(profile, Hero, star.LocalStarId);
            }
            return profile;
        }
        private static Build Roundtrip(Profile profile) => Build.Decode(Build.Compute(profile, Hero, 0).Encode());
        private static MemoryActivationEvent Hit(string source, long activation, long packet, long victim = 9, long epoch = 1) =>
            new MemoryActivationEvent(1, source, activation, packet, victim, MemoryEventKind.Hit,
                NativePayloadKind.Skill, GeneratedOrigin.None, epoch);
        private static MechanismEquipment Equipment() => new MechanismEquipment(1, 1, new[]
        {
            new EquippedMechanismMemory(Q, 11, MechanismMemorySlot.Q, true, false),
            new EquippedMechanismMemory(R, 12, MechanismMemorySlot.R, true, false),
            new EquippedMechanismMemory(Movement, 13, MechanismMemorySlot.Movement, true, false),
        });

        [Fact]
        public void Registered_fractional_filtered_recharge_reaches_actual_cooldown_with_activation_budget()
        {
            const string effectId = "outer.authoredprobe.s2", boostId = "outer.authoredprobe.s3";
            var selector = MemorySelector.Parse("@Q(" + Q + ")|@R(" + R + ")");
            var channel = new DirectedRechargeChannel("probe.recharge", selector, MemoryEventKind.Hit,
                MemorySelector.Parse("@M(" + Movement + ")"), new[] { 25 }, AttributionBudget.PerActivation, everyN: 2);
            try
            {
                var profile = Register(Node(effectId, new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge,
                    ChannelId = channel.ChannelId, Source = selector, Trigger = MemoryEventKind.Hit,
                    Budget = AttributionBudget.PerActivation, Recharge = channel, EveryN = 2 }),
                    Node(boostId, effect: new ClusterStarDef { Kind = ClusterStarKind.GimmickBoost,
                        Name = new Txt("試験", "Probe"), Memory = Q, ScopedModifier = new ScopedModifierDef
                        { ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Q, Amount = ModifierUnits.FromPercent(.5m),
                            TargetEffectIds = new[] { effectId }, TargetEffects = new[] { GimmickEffect.Recharge } } }));
                var decoded = Roundtrip(profile);
                var effective = Assert.Single(decoded.Mechanisms).Spec.Recharge;
                var runtime = new DirectedRechargeRuntime(); runtime.SetChannels(new[] { effective }); var equipment = Equipment();
                var requests = new List<DirectedRechargeRequest>();
                runtime.Notify(Hit(Q, 1, 101), equipment, new RechargeConditionContext(false, 0), () => 0, requests);
                runtime.Notify(Hit(Q, 1, 102, 10), equipment, new RechargeConditionContext(false, 0), () => 0, requests);
                Assert.Empty(requests);
                runtime.Notify(Hit(Q, 2, 103), equipment, new RechargeConditionContext(false, 0), () => 0, requests);
                var request = Assert.Single(requests);
                Assert.Equal(9.974875f, 10f - 20f * request.NativeRatio(10, 20), 6);
                requests.Clear();
                runtime.Notify(Hit(Q, 2, 104, 11), equipment, new RechargeConditionContext(false, 0), () => 0, requests);
                Assert.Empty(requests);
                Assert.Throws<ArgumentException>(() => new DirectedRechargeChannel("invalid", MemorySelector.Parse("@M"), MemoryEventKind.Hit,
                    MemorySelector.Parse("@Q"), new[] { 25 }, AttributionBudget.PerActivation));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Registered_preparation_and_relay_keep_source_exclusion_and_native_target_expiry_after_wire()
        {
            try
            {
                var profile = Register(Node("outer.authoredprobe.s2", new AuthoredMechanismSpec
                { Kind = AuthoredMechanismKind.MemoryPrimed, ChannelId = "probe.primed", Source = MemorySelector.Parse("@Q(" + Q + ")"),
                    Trigger = MemoryEventKind.Hit, Primed = new MemoryPrimedDefinition("probe.primed", Q, MemoryEventKind.Hit, 2000) }),
                    Node("outer.authoredprobe.s3", new AuthoredMechanismSpec
                    { Kind = AuthoredMechanismKind.RelayWindow, ChannelId = "probe.relay", Source = MemorySelector.Parse("@R(" + R + ")"),
                        Trigger = MemoryEventKind.ConfirmedUse, Relay = new RelayWindowDefinition("probe.relay", Q, 1000) }));
                var build = Roundtrip(profile);
                var preparation = new MemoryPrimedRuntime(1);
                preparation.Configure(build.Mechanisms.Where(e => e.Spec.Primed != null).Select(e => e.Spec.Primed));
                preparation.SetEquipment(new Dictionary<string, long> { [Q] = 1 });
                Assert.True(preparation.OnSourceEvent(Hit(Q, 1, 101), 0));
                var sourceAttack = new MemoryActivationEvent(1, "", 1, 102, 9, MemoryEventKind.OwnedBasicAttackHit,
                    NativePayloadKind.MainBasicAttack, GeneratedOrigin.None, 1);
                Assert.False(preparation.TryConsume(sourceAttack, 1, 150, null, out _));
                var nextAttack = new MemoryActivationEvent(1, "", 2, 103, 9, MemoryEventKind.OwnedBasicAttackHit,
                    NativePayloadKind.MainBasicAttack, GeneratedOrigin.None, 1);
                Assert.True(preparation.TryConsume(nextAttack, 1, 150, null, out var bonus));
                Assert.Equal(30, bonus.Damage);
                Assert.False(preparation.TryConsume(nextAttack, 1, 150, null, out _));
                var relay = new RelayWindowRuntime(1);
                relay.Configure(build.Mechanisms.Where(e => e.Spec.Relay != null).Select(e => e.Spec.Relay));
                relay.SetEquipment(1, Q, 1);
                Assert.True(relay.OnSourceEvent(new MemoryActivationEvent(1, R, 3, 0, 0, MemoryEventKind.ConfirmedUse,
                    NativePayloadKind.Skill, GeneratedOrigin.None, 1), 0));
                Assert.Equal(.1f, relay.DamageAmplification(Hit(Q, 4, 104), 3.999f), 6);
                Assert.Equal(0, relay.DamageAmplification(Hit(Q, 5, 105), 4));
                var generated = new MemoryActivationEvent(1, Q, 6, 106, 9, MemoryEventKind.Hit,
                    NativePayloadKind.Skill, GeneratedOrigin.Gimmick, 1);
                Assert.Equal(0, relay.DamageAmplification(generated, 1));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Registered_ward_retains_recipient_health_basis_and_fractional_dividend_roll_boundary()
        {
            try
            {
                var profile = Register(Node("outer.authoredprobe.s2", new AuthoredMechanismSpec
                { Kind = AuthoredMechanismKind.AlliedWard, ChannelId = "probe.ward", Source = MemorySelector.Parse("@Q(" + Q + ")"),
                    Ward = new AlliedWardDefinition("probe.ward", WardRecipientKind.AlliedTravelers,
                        WardAmountBasis.RecipientMaxHP, ModShieldPoolKind.Ordinary, 102, true) }),
                    Node("outer.authoredprobe.s3", new AuthoredMechanismSpec
                    { Kind = AuthoredMechanismKind.PressureDividend, ChannelId = "probe.dividend", Source = MemorySelector.Parse("@Q(" + Q + ")"),
                        Trigger = MemoryEventKind.Kill, Budget = AttributionBudget.PerKill,
                        Dividend = new PressureDividendChannel(new[] { new PressureDividendContribution("outer.authoredprobe.s3", Q, 100) }) }),
                    Node("outer.authoredprobe.s4", effect: new ClusterStarDef { Kind = ClusterStarKind.GimmickBoost,
                        Name = new Txt("試験", "Probe"), Memory = Q, ScopedModifier = new ScopedModifierDef
                        { ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Q, Amount = ModifierUnits.FromPercent(.5m),
                            TargetEffectIds = new[] { "outer.authoredprobe.s3" } } }));
                var decoded = Roundtrip(profile);
                var ward = decoded.Mechanisms.Single(e => e.Spec.Ward != null).Spec.Ward;
                var award = Assert.Single(AlliedWard.Select(ward, 1, true, 1000, 2000,
                    new[] { new WardCandidate(1, 0, true, false, true, true, 50, 100, 1) }));
                Assert.Equal(1.02f, award.RawAmount, 6);
                var dividends = decoded.Mechanisms.Where(e => e.Spec.Dividend != null).Select(e => e.Spec.Dividend).ToArray();
                var attribution = new PressureDividendAttribution("1", Q, PressureDividendKillOrigin.NativeMemory, PressureDividendVictimKind.NativeLootEnemy);
                var equipped = new HashSet<string> { Q };
                var enemy = new PressureDividendEnemy("run.probe", 0, 7); enemy.RecordAppliedHpMultiplier(1.25);
                var runtime = new PressureDividendRuntime();
                Assert.NotNull(runtime.TryAward(enemy.CaptureDeath(true), attribution, dividends, equipped, () => 100.25m, () => "reward.probe"));
                Assert.Null(runtime.TryAward(enemy.CaptureDeath(true), attribution, dividends, equipped,
                    () => throw new InvalidOperationException("The rewarded death must not roll again."), () => "reward.duplicate"));
                var boundaryEnemy = new PressureDividendEnemy("run.probe", 0, 8); boundaryEnemy.RecordAppliedHpMultiplier(1.25);
                Assert.Null(runtime.TryAward(boundaryEnemy.CaptureDeath(true), attribution, dividends, equipped, () => 100.5m, () => "reward.boundary"));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }
        [Fact]
        public void Three_hundred_real_paid_points_keep_every_recharge_application_inside_fixed_wire_envelope()
        {
            var stars = Enumerable.Range(2, 299).Select(n => Node("outer.authoredprobe.s" + n,
                new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "probe.bulk." + n,
                    Source = MemorySelector.Parse("@Q(" + Q + ")"), Recharge = new DirectedRechargeChannel("probe.bulk." + n,
                        MemorySelector.Parse("@Q(" + Q + ")"), MemoryEventKind.Hit, MemorySelector.Parse("@M(" + Movement + ")"),
                        new[] { 1 }) })).ToArray();
            try
            {
                var profile = Register();
                StarClusters.RegisterAuthored(Hero, new[] { RootNode() }.Concat(stars));
                var hero = profile.Hero(Hero);
                AuthoredStarContractTests.AllocatePath(hero, HeroSigils.TreeFor(Hero), Root);
                Rules.AddTalentRank(profile, Hero, Root);
                int count = 300 - Rules.SpentPoints(hero);
                foreach (var star in stars.Take(count)) Rules.AddTalentRank(profile, Hero, star.LocalStarId);
                Assert.Equal(300, Rules.SpentPoints(hero));
                var build = Roundtrip(profile);
                var runtime = new DirectedRechargeRuntime();
                runtime.SetChannels(build.Mechanisms.Select(e => e.Spec.Recharge));
                var requests = new List<DirectedRechargeRequest>();
                runtime.Notify(Hit(Q, 1, 101), Equipment(), new RechargeConditionContext(false, 0), () => 0, requests);
                float remaining = 100;
                foreach (var request in requests) remaining -= 100 * request.NativeRatio(remaining, 100);
                Assert.Equal((float)(100 * Math.Pow(.9999, count)), remaining, 3);
                Assert.True(remaining < 98);
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Redesigned_renewal_is_a_real_registered_pair_with_preserved_five_ranks_and_conditioned_extra()
        {
            const string heroKey = "Hero_Bismuth", bridgeId = "h.bismuth.ring.renewal", pairId = "probe.renewal.pair";
            const string source = "St_QR_Innocence", receiver = "St_M_Sprint";
            var baseline = HeroSigils.TreeFor(heroKey);
            var a = baseline.Single(t => t.RouteMemory == "St_D_PrismaticEyes" && t.RouteOrder == 4);
            var b = baseline.Single(t => t.RouteMemory == source && t.RouteOrder == 4);
            var retained = baseline.Single(t => t.Id == bridgeId);
            var selector = MemorySelector.Parse("@Q(" + source + ")|@R(" + source + ")");
            var definition = new BridgeSuccessDefinition(pairId,
                new[] { new BridgeEndpointRequirement(a.Id, a.RouteMemory), new BridgeEndpointRequirement(b.Id, b.RouteMemory) },
                1, BridgeGateKind.DirectReceiver, selector, MemoryEventKind.Hit, selector, MemoryEventKind.Hit,
                new BridgePayload("probe.renewal.recharge", BridgePayloadKind.Recharge, new[] { 100 },
                    recipient: MemorySelector.Parse("@M(" + receiver + ")")),
                new[] { new BridgePayload("probe.renewal.longshield", BridgePayloadKind.OrdinaryShield,
                    new[] { 100 }, capUnits: 1500, durationSeconds: 12) });
            var root = new AuthoredStarDef { LocalStarId = bridgeId, HeroKey = heroKey, ClusterId = "probe.renewal",
                Region = ClusterRegion.Bridge(bridgeId), AnchorId = bridgeId, RetainedLegacy = true,
                ReceiverOnlyBridge = true,
                MemoryOwnership = new MemoryOwnership { TargetMemory = receiver, SourceMemories = new[] { source } },
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"),
                    MaxRank = retained.MaxRank, RankCost = retained.RankCost,
                    Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess, ChannelId = "probe.renewal",
                        Bridge = definition, ValuesByRank = new[] { 100, 200, 300, 400, 500 } } } };
            var extra = new AuthoredStarDef { LocalStarId = "probe.renewal.extra", HeroKey = heroKey, ClusterId = root.ClusterId,
                Region = root.Region, AnchorId = bridgeId, MemoryOwnership = root.MemoryOwnership,
                ReceiverOnlyBridge = true,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"),
                    Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = "probe.renewal.shield",
                        Source = selector, Condition = AuthoredMechanismCondition.BridgeSuccess, PairId = bridgeId,
                        Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Shield, Value = .25m } } } };
            try
            {
                StarClusters.RegisterAuthored(heroKey, new[] { root, extra });
                var profile = Profile.CreateNew(1803); var allocation = profile.Hero(heroKey);
                allocation.StarXp = StarProgression.TotalXpForPoints(300);
                var tree = HeroSigils.TreeFor(heroKey);
                foreach (var endpoint in new[] { a, b })
                {
                    AuthoredStarContractTests.AllocatePath(allocation, tree, endpoint.Id);
                    allocation.Talents[endpoint.Id] = 1;
                }
                AuthoredStarContractTests.AllocatePath(allocation, tree, bridgeId);
                for (int rank = 0; rank < 5; rank++) Rules.AddTalentRank(profile, heroKey, bridgeId);
                Rules.AddTalentRank(profile, heroKey, extra.LocalStarId);
                var decoded = Build.Decode(Build.Compute(profile, heroKey, 0).Encode());
                var bridge = decoded.Mechanisms.Single(e => e.Spec.Bridge != null).Spec.Bridge;
                var runtime = new PairComboRuntime(); runtime.SetSuccessEffects(new[] { bridge });
                var equipment = new MechanismEquipment(1, 1, new[]
                {
                    new EquippedMechanismMemory(a.RouteMemory, 11, MechanismMemorySlot.Identity, true, false),
                    new EquippedMechanismMemory(source, 12, MechanismMemorySlot.Q, true, false),
                    new EquippedMechanismMemory(receiver, 13, MechanismMemorySlot.Movement, false, false),
                });
                var transactions = new List<BridgeSuccessTransaction>();
                runtime.FireAttributed(Hit(source, 1, 101), 0, 100, equipment, decoded.MechanismEndpointRanks, transactions);
                var transaction = Assert.Single(transactions); var requests = new List<DirectedRechargeRequest>();
                transaction.CreateRechargeRequests(equipment, decoded.MechanismEndpointRanks, requests);
                Assert.Equal(9.5f, 10 - 20 * Assert.Single(requests).NativeRatio(10, 20), 6);
                var shieldPayload = transaction.Payloads.Single(p => p.Kind == BridgePayloadKind.OrdinaryShield);
                var effectiveShield = AuthoredKeystoneComposer.TransformAllocationPayload(decoded,
                    AuthoredKeystoneComposer.BridgePayload(shieldPayload), source, equipment: equipment);
                var pools = new ModShieldPools<ShieldHandle>();
                var pool = new ModShieldPoolKey(1, 1, ModShieldPoolKind.Ordinary);
                pools.Apply(pool, new ShieldAdapter(), (float)effectiveShield.Value, 100, 0, (float)effectiveShield.DurationSeconds, 1);
                pools.Maintain(pool, 100, 8.001, 1, true);
                Assert.True(pools.Contains(pool));
                pools.Maintain(pool, 100, 12, 1, true);
                Assert.False(pools.Contains(pool));
                var shield = decoded.Mechanisms.Single(e => e.Spec.Gimmick != null);
                Assert.Equal(transaction.PairId, shield.Spec.PairId);
                Assert.True(transaction.IsCurrent(runtime, equipment, decoded.MechanismEndpointRanks));
                allocation.Talents.Remove(a.Id);
                var missingEndpoint = Build.Decode(Build.Compute(profile, heroKey, 0).Encode());
                runtime.RefreshSuccessPrerequisites(equipment, missingEndpoint.MechanismEndpointRanks);
                Assert.False(transaction.IsCurrent(runtime, equipment, missingEndpoint.MechanismEndpointRanks));
            }
            finally { StarClusters.RegisterAuthored(heroKey, Array.Empty<AuthoredStarDef>()); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Captured_purchase_cannot_commit_after_shared_registry_or_cap_identity_changes(bool changeCap)
        {
            try
            {
                var profile = Register();
                AuthoredStarContractTests.AllocatePath(profile.Hero(Hero), HeroSigils.TreeFor(Hero), Root);
                var engine = new EffectiveAllocationValidation(HeroSigils.TreeFor(Hero), layout: HeroTreeLayout.ForHero(Hero));
                var plan = engine.Preview(profile, Hero, new AllocationChange { Kind = AllocationChangeKind.Purchase, CandidateStarId = Root });
                if (changeCap)
                    FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile
                    { Id = "probe.pending.nativecap", Kind = LinkKind.MemoryDamage, Maximum = ValueUnits.FromPercent(10) });
                else
                    StarClusters.RegisterAuthored(Hero, new[] { Node(Root, effect: new ClusterStarDef
                        { Kind = ClusterStarKind.Stat, Name = new Txt("試験", "Probe"), Stat = Stat.Armor, Amount = 2 }) });
                Assert.Throws<InvalidOperationException>(() => engine.Commit(profile, plan));
                Assert.False(profile.Hero(Hero).Talents.ContainsKey(Root));
            }
            finally { StarClusters.RegisterAuthored(Hero, Array.Empty<AuthoredStarDef>()); }
        }
    }
}
