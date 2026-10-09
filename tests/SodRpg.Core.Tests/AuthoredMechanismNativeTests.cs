using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class AuthoredMechanismNativeTests
    {
        private const string Identity = "St_D_Resolve", Q = "St_Q_CruelSun";
        private static MemorySelector Source(string memory) => new MemorySelector(MemorySelectorKind.Memory, memory);
        private static Build Decoded(params AuthoredMechanismSpec[] specs)
        {
            var build = new Build();
            foreach (var spec in specs) build.Mechanisms.Add(new AuthoredMechanismEntry
                { StarId = "test." + spec.ChannelId, ContributorIds = new[] { "test." + spec.ChannelId }, Spec = spec });
            return Build.Decode(build.Encode());
        }
        private static Build Allocated(AuthoredMechanismSpec spec, out Profile profile) =>
            Allocated("Hero_Vesper", new[] { spec }, out profile);

        private static Build Allocated(string heroKey, AuthoredMechanismSpec[] specs, out Profile profile,
            KeystoneDefinition key = null)
        {
            const string anchor = "outer.native.s1";
            var ids = specs.Select((spec, i) => "outer.native.effect." + i).ToArray();
            var definitions = new List<AuthoredStarDef>
            {
                new AuthoredStarDef { HeroKey = heroKey, LocalStarId = anchor, ClusterId = "outer.native", Region = ClusterRegion.Outer,
                    AnchorId = anchor, Shape = ClusterShape.Fan, Edges = ids.Select(id => new AuthoredStarEdge(anchor, id)).ToArray(),
                    Effect = new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("試験の入口", "Test entrance"), Stat = Stat.Armor, Amount = 1 } }
            };
            for (int i = 0; i < specs.Length; i++)
                definitions.Add(new AuthoredStarDef { HeroKey = heroKey, LocalStarId = ids[i], ClusterId = "outer.native",
                    Region = ClusterRegion.Outer, AnchorId = anchor, Shape = ClusterShape.Fan, Mechanism = specs[i],
                    Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の効果", "Test effect") } });
            var legacyTree = HeroSigils.TreeFor(heroKey);
            var originalKey = key == null ? null : legacyTree.FirstOrDefault(t => t.Id == key.KeystoneId);
            if (key != null)
                definitions.Add(new AuthoredStarDef { HeroKey = heroKey, LocalStarId = key.KeystoneId,
                    ClusterId = "native.keys", Region = new ClusterRegion { Kind = ClusterRegionKind.Keystone },
                    AnchorId = legacyTree.First(t => t.RouteOrder == 7).Id, Shape = ClusterShape.Fan,
                    RetainedLegacy = originalKey != null, KeystoneDefinition = key,
                    Effect = new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("試験の刻印", "Test Keystone"),
                        KeystoneDefinition = key, MaxRank = originalKey?.MaxRank ?? 1, RankCost = originalKey?.RankCost ?? key.Cost } });
            var tree = StarClusters.RegisterAuthored(heroKey, definitions).TreeFor(heroKey);
            try
            {
                profile = new Profile(); var allocation = profile.Hero(heroKey);
                allocation.StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
                allocation.Kills = 1000000;
                foreach (string id in ids)
                {
                    AuthoredStarContractTests.AllocatePath(allocation, tree, id);
                    Rules.AddTalentRank(profile, heroKey, id);
                }
                if (key != null)
                {
                    if (originalKey == null)
                        AuthoredStarContractTests.AllocatePath(allocation, tree, key.KeystoneId);
                    else
                    {
                        var core = legacyTree.Where(t => !t.IsKeystone && t.RouteId == null && t.Tier == 1).ToArray();
                        int CoreRanks(HeroState state) => core.Sum(node => state.Talents.TryGetValue(node.Id, out int rank)
                            ? Math.Max(0, Math.Min(node.MaxRank, rank)) : 0);
                        foreach (var node in core)
                        {
                            if (CoreRanks(profile.Hero(heroKey)) >= Content.KeystoneRouteRequirement) break;
                            TreeTestPaths.Connect(profile, heroKey, node.Id);
                            while ((!profile.Hero(heroKey).Talents.TryGetValue(node.Id, out int rank) || rank < node.MaxRank)
                                && CoreRanks(profile.Hero(heroKey)) < Content.KeystoneRouteRequirement)
                                Rules.AddTalentRank(profile, heroKey, node.Id);
                        }
                        string access = tree.First(t => t.Id == key.KeystoneId).AuthoredStar.AnchorId;
                        TreeTestPaths.Connect(profile, heroKey, access);
                        if (!profile.Hero(heroKey).Talents.TryGetValue(access, out int accessRank) || accessRank <= 0)
                            Rules.AddTalentRank(profile, heroKey, access);
                    }
                    Rules.SetKeystone(profile, heroKey, key.KeystoneId);
                }
                return Build.Decode(Build.Compute(profile, heroKey, 0).Encode());
            }
            finally { StarClusters.RegisterAuthored(heroKey, Array.Empty<AuthoredStarDef>()); }
        }
        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime) Setup(Build build, Hero hero = null)
        {
            Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0; DewPlayer.gamePlayers.Clear(); DewPhysics.Entities.Clear();
            NativeAttributedDamagePacket.Current = null; NativeAuthoredKeystonePacketFamily.Current = null;
            hero = hero ?? new Hero();
            hero.Skill.Skills[HeroSkillLocation.Identity] = hero is Hero_Cetus
                ? (SkillTrigger)new St_D_IcyVeins { owner = hero } : new St_D_Resolve { owner = hero };
            hero.Skill.Skills[HeroSkillLocation.Q] = hero is Hero_Cetus
                ? (SkillTrigger)new St_Q_EmbracingTheChill { owner = hero } : new St_Q_CruelSun { owner = hero };
            hero.Skill.Skills[HeroSkillLocation.Movement] = hero is Hero_Cetus
                ? (SkillTrigger)new St_M_FrostyCharge { owner = hero } : new St_M_DreamyWaltz { owner = hero };
            hero.owner = new DewPlayer { hero = hero, isHumanPlayer = true };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, HeroKey = hero is Hero_Cetus ? "Hero_Cetus" : "Hero_Vesper", Powers = new PowerRuntime(build, 0) };
            var host = new HostAuthority(); host.BindAuthored(runtime, build);
            return (host, runtime);
        }
        private static Entity Enemy() => new Entity { Relation = EntityRelation.Enemy };
        private static MemoryActivationEvent Event(HostAuthority host, MemoryActivationIdentity activation, MemoryEventKind kind, Entity victim = null)
            => activation.Event(kind, host.Packet(), victim != null ? victim.GetInstanceID() : 0);
        private static AuthoredMechanismSpec Gimmick(string id, GimmickEffect effect, decimal value,
            AuthoredMechanismCondition condition = AuthoredMechanismCondition.Always, string pair = null)
            => new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.Gimmick, ChannelId = id, Source = Source(Identity), Trigger = MemoryEventKind.Hit,
                Budget = AttributionBudget.PerActivation, Condition = condition, PairId = pair,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value } };
        private static KeystoneDefinition Key(string id, string memory = Q, KeystonePayloadKind[] payloads = null,
            AuthoredMechanismSpec[] grants = null) => new KeystoneDefinition(id, new[] { memory },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, new KeystoneMagnitude(10000),
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo })) }, payloads: payloads, grants: grants);

        [Fact]
        public void Authored_allocation_roundtrip_changes_real_remaining_cooldown_and_retransmission_keeps_every_n()
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "native.recharge", Source = Source(Identity),
                Trigger = MemoryEventKind.ConfirmedUse, Recharge = new DirectedRechargeChannel("native.recharge", Source(Identity),
                    MemoryEventKind.ConfirmedUse, new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 2000 }, everyN: 2) };
            var decoded = Allocated(spec, out var allocation);
            var (host, runtime) = Setup(decoded); var movement = runtime.Hero.Skill.GetSkill(HeroSkillLocation.Movement);
            var first = host.Activation(runtime.Hero, Identity); host.NotifyAuthored(runtime, Event(host, first, MemoryEventKind.ConfirmedUse));
            Assert.Equal(10, movement.currentConfigUnscaledCooldownTime);
            host.BindAuthored(runtime, Build.Decode(decoded.Encode()));
            var second = host.Activation(runtime.Hero, Identity); host.NotifyAuthored(runtime, Event(host, second, MemoryEventKind.ConfirmedUse));
            Assert.Equal(8, movement.currentConfigUnscaledCooldownTime, 5);
            host.NotifyAuthored(runtime, Event(host, second, MemoryEventKind.ConfirmedUse));
            Assert.Equal(8, movement.currentConfigUnscaledCooldownTime, 5);
        }
        [Fact]
        public void Native_memory_slot_changes_are_temporary_predicates_and_re_equip_restores_paid_authored_effect()
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "native.equipment",
                Source = Source(Identity), Trigger = MemoryEventKind.ConfirmedUse,
                Recharge = new DirectedRechargeChannel("native.equipment", Source(Identity), MemoryEventKind.ConfirmedUse,
                    new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 2000 }) };
            var build = Allocated(spec, out var profile);
            var ranks = profile.Hero("Hero_Vesper").Talents.OrderBy(pair => pair.Key).ToArray();
            var (host, runtime) = Setup(build); var hero = runtime.Hero;
            var removed = hero.Skill.UnequipSkill(HeroSkillLocation.Identity);
            Assert.Null(host.BeginAttributedMemoryCast(removed));
            Assert.Equal(ranks, profile.Hero("Hero_Vesper").Talents.OrderBy(pair => pair.Key).ToArray());
            Assert.Equal(10, hero.Skill.GetSkill(HeroSkillLocation.Movement).currentConfigUnscaledCooldownTime);
            var replacement = new St_D_Resolve { owner = hero }; hero.Skill.EquipSkill(HeroSkillLocation.Identity, replacement);
            var cast = host.BeginAttributedMemoryCast(replacement); Assert.NotNull(cast);
            host.PublishAttributedMemoryUse(cast);
            Assert.Equal(8, hero.Skill.GetSkill(HeroSkillLocation.Movement).currentConfigUnscaledCooldownTime, 5);
            Assert.Equal(ranks, profile.Hero("Hero_Vesper").Talents.OrderBy(pair => pair.Key).ToArray());
        }

        [Fact]
        public void Duplicate_memory_name_in_second_slot_keeps_other_memory_events_admitted()
        {
            // #163: equip a second instance of an already-equipped memory name; the equipment
            // snapshot must not throw and later events of the other (and duplicated) memories
            // must stay admitted at the new equipment generation.
            var identitySpec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "native.dup.identity",
                Source = Source(Identity), Trigger = MemoryEventKind.ConfirmedUse,
                Recharge = new DirectedRechargeChannel("native.dup.identity", Source(Identity), MemoryEventKind.ConfirmedUse,
                    new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 2000 }) };
            var duplicateSpec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "native.dup.memory",
                Source = Source(Q), Trigger = MemoryEventKind.ConfirmedUse,
                Recharge = new DirectedRechargeChannel("native.dup.memory", Source(Q), MemoryEventKind.ConfirmedUse,
                    new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 2000 }) };
            var (host, runtime) = Setup(Allocated("Hero_Vesper", new[] { identitySpec, duplicateSpec }, out _));
            var hero = runtime.Hero;
            var movement = hero.Skill.GetSkill(HeroSkillLocation.Movement);
            void Use(string memory) =>
                host.NotifyAuthored(runtime, Event(host, host.Activation(hero, memory), MemoryEventKind.ConfirmedUse));
            void ResetCooldown() => movement.currentConfigUnscaledCooldownTime = 10;

            Use(Identity); Assert.Equal(8, movement.currentConfigUnscaledCooldownTime, 5); // normal equipment fires
            ResetCooldown();
            hero.Skill.EquipSkill(HeroSkillLocation.R, new St_R_BaptismOfSun { owner = hero }); // normal update, distinct memory
            Use(Identity); Assert.Equal(8, movement.currentConfigUnscaledCooldownTime, 5);
            ResetCooldown();
            // Replace R with another instance of the memory already in Q: duplicate name across slots (#163).
            hero.Skill.EquipSkill(HeroSkillLocation.R, new St_Q_CruelSun { owner = hero });
            Use(Identity); Assert.Equal(8, movement.currentConfigUnscaledCooldownTime, 5); // other memory stays admitted
            ResetCooldown();
            Use(Q); Assert.Equal(8, movement.currentConfigUnscaledCooldownTime, 5); // duplicated memory resolves to its first slot
        }

        [Fact]
        public void Circle_identity_uses_published_empty_source_owned_basic_firing_and_live_summon_every_four()
        {
            const string circle = "St_D_CircleOfLife";
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "native.circle.receiver",
                Source = Source(circle), Trigger = MemoryEventKind.OwnedBasicAttackFired, EveryN = 4, Budget = AttributionBudget.PerOwnedBasicAttack,
                TriggerByIdentity = new Dictionary<string, MemoryEventKind> { [circle] = MemoryEventKind.OwnedBasicAttackFired },
                Recharge = new DirectedRechargeChannel("native.circle.receiver", Source(circle), MemoryEventKind.OwnedBasicAttackFired,
                    new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 500 }, AttributionBudget.PerOwnedBasicAttack) };
            var (host, runtime) = Setup(Allocated("Hero_Nachia", new[] { spec }, out _));
            runtime.Hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_CircleOfLife { owner = runtime.Hero };
            for (int i = 0; i < 4; i++)
                host.FireOwnedBasic(runtime.Hero);
            var movement = runtime.Hero.Skill.GetSkill(HeroSkillLocation.Movement);
            Assert.Equal(10, movement.currentConfigUnscaledCooldownTime);
            runtime.Hero.summons.Add(new Summon { Owner = runtime.Hero, parentActor = runtime.Hero });
            for (int i = 0; i < 3; i++)
                host.FireOwnedBasic(runtime.Hero);
            Assert.Equal(10, movement.currentConfigUnscaledCooldownTime);
            var fourth = host.FireOwnedBasic(runtime.Hero);
            Assert.Equal("", fourth.Identity.SourceMemory);
            Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);
            host.FireOwnedBasic(runtime.Hero, fourth.Actor); Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);
            runtime.Hero.summons.Clear();
            for (int i = 0; i < 4; i++)
                host.FireOwnedBasic(runtime.Hero);
            Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);
        }


        [Fact]
        public void Owned_basic_keystone_every_two_does_not_change_heart_identity_every_four_hits()
        {
            var source = MemorySelector.Parse("St_D_CircleOfLife|St_D_HeartOfThePack");
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.DirectedRecharge, ChannelId = "native.cadence",
                Source = source, Trigger = MemoryEventKind.Hit, EveryN = 4,
                TriggerByIdentity = new Dictionary<string, MemoryEventKind> { ["St_D_CircleOfLife"] = MemoryEventKind.OwnedBasicAttackFired,
                    ["St_D_HeartOfThePack"] = MemoryEventKind.Hit },
                Recharge = new DirectedRechargeChannel("native.cadence", source, MemoryEventKind.Hit,
                    new MemorySelector(MemorySelectorKind.EquippedMovement), new[] { 500 }, everyN: 4) };
            var key = new KeystoneDefinition("test.cadence.key", new[] { Q },
                new[] { KeystoneTransform.SetEveryN(2, new KeystoneScope(payloadKind: KeystonePayloadKind.DirectedRecharge,
                    sourceKind: KeystoneSourceKind.OwnedBasicAttack)) });
            var build = Decoded(spec); build.SelectedKeystone = key;
            var (host, runtime) = Setup(Build.Decode(build.Encode()));
            runtime.Hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_CircleOfLife { owner = runtime.Hero };
            runtime.Hero.summons.Add(new Summon { Owner = runtime.Hero, parentActor = runtime.Hero });
            var movement = runtime.Hero.Skill.GetSkill(HeroSkillLocation.Movement);
            host.FireOwnedBasic(runtime.Hero); Assert.Equal(10, movement.currentConfigUnscaledCooldownTime);
            host.FireOwnedBasic(runtime.Hero); Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);
            runtime.Hero.Skill.Skills[HeroSkillLocation.Identity] = new St_D_HeartOfThePack { owner = runtime.Hero };
            for (int i = 0; i < 3; i++)
                host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, "St_D_HeartOfThePack"), MemoryEventKind.Hit, Enemy()));
            Assert.Equal(9.5f, movement.currentConfigUnscaledCooldownTime, 5);
            host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, "St_D_HeartOfThePack"), MemoryEventKind.Hit, Enemy()));
            Assert.Equal(9.025f, movement.currentConfigUnscaledCooldownTime, 5);
        }

        [Theory]
        [InlineData(AuthoredMechanismCondition.BridgeSuccess, BridgeGateKind.Mark)]
        [InlineData(AuthoredMechanismCondition.BridgeMark, BridgeGateKind.Mark)]
        [InlineData(AuthoredMechanismCondition.BridgeWindow, BridgeGateKind.Window)]
        public void Real_pair_predicates_gate_existing_heal_burst_and_fractional_ordinary_shield_dispatch(AuthoredMechanismCondition condition, BridgeGateKind gate)
        {
            var pair = PairCombos.Get("h.vesper.pair.1");
            var endpoints = new[] { new BridgeEndpointRequirement(pair.StarA, Identity), new BridgeEndpointRequirement(pair.StarB, Q) };
            var burst = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Burst, Value = 20 };
            var bridge = new BridgeSuccessDefinition(pair.Id, endpoints, 1, gate, Source(Q),
                gate == BridgeGateKind.Window ? MemoryEventKind.ConfirmedUse : MemoryEventKind.Hit, Source(Identity), MemoryEventKind.Hit,
                BridgePayload.FromEffective("native.pool", BridgePayloadKind.OrdinaryShield, 102m, durationSeconds: 4),
                new[] { new BridgePayload("native.burst", BridgePayloadKind.Gimmick, new[] { 2000 }, gimmick: burst) });
            var extra = Gimmick("native.heal", GimmickEffect.Heal, 2, condition, pair.Id);
            const string extraId = "native.bridge.heal";
            var originalBridge = HeroSigils.TreeFor("Hero_Vesper").Single(t => t.Id == pair.BridgeId);
            var bridgeStar = new AuthoredStarDef { HeroKey = "Hero_Vesper", LocalStarId = pair.BridgeId, ClusterId = "native.bridge",
                Region = ClusterRegion.Bridge(pair.BridgeId), AnchorId = pair.BridgeId, RetainedLegacy = true,
                MemoryOwnership = new MemoryOwnership { TargetMemory = Identity, SourceMemories = new[] { Q } },
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験", "Probe"),
                    MaxRank = originalBridge.MaxRank, RankCost = originalBridge.RankCost,
                    Mechanism = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.BridgeSuccess, ChannelId = "native.bridge", Bridge = bridge } } };
            var extraStar = new AuthoredStarDef { HeroKey = "Hero_Vesper", LocalStarId = extraId, ClusterId = bridgeStar.ClusterId,
                Region = bridgeStar.Region, AnchorId = pair.BridgeId, MemoryOwnership = bridgeStar.MemoryOwnership,
                RequiredStarIds = new[] { pair.BridgeId }, Mechanism = extra,
                Effect = new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("試験の回復", "Probe heal") } };
            Build build;
            try
            {
                var tree = StarClusters.RegisterAuthored("Hero_Vesper", new[] { bridgeStar, extraStar }).TreeFor("Hero_Vesper");
                var profile = new Profile(); var allocation = profile.Hero("Hero_Vesper");
                allocation.StarXp = StarProgression.TotalXpForPoints(300);
                foreach (string id in new[] { pair.StarA, pair.StarB, pair.BridgeId, extraId })
                {
                    AuthoredStarContractTests.AllocatePath(allocation, tree, id);
                    Rules.AddTalentRank(profile, "Hero_Vesper", id);
                }
                build = Build.Decode(Build.Compute(profile, "Hero_Vesper", 0).Encode());
            }
            finally { StarClusters.RegisterAuthored("Hero_Vesper", Array.Empty<AuthoredStarDef>()); }
            var (host, runtime) = Setup(build); runtime.Hero.currentHealth = 500;
            float burstDamage = (float)(20m * (1m + 1.5m * build.SpentStarPoints / 500));
            var enemy = Enemy(); DewPhysics.Entities.Add(enemy);
            var failed = host.Activation(runtime.Hero, Identity); host.NotifyAuthored(runtime, Event(host, failed, MemoryEventKind.Hit, enemy)); host.FlushAuthored(runtime);
            Assert.Equal(500, runtime.Hero.currentHealth); Assert.Equal(1000, enemy.currentHealth);
            var opening = host.Activation(runtime.Hero, Q); host.NotifyAuthored(runtime,
                Event(host, opening, gate == BridgeGateKind.Window ? MemoryEventKind.ConfirmedUse : MemoryEventKind.Hit, enemy));
            var payoff = host.Activation(runtime.Hero, Identity); var value = Event(host, payoff, MemoryEventKind.Hit, enemy);
            host.NotifyAuthored(runtime, value, enemy); host.FlushAuthored(runtime);
            Assert.Equal(520, runtime.Hero.currentHealth); Assert.Equal(1000 - burstDamage, enemy.currentHealth, 3);
            Assert.Equal(10.2f, runtime.Hero.Status.currentShield, 4);
            host.NotifyAuthored(runtime, value, enemy); host.FlushAuthored(runtime);
            Assert.Equal(520, runtime.Hero.currentHealth); Assert.Equal(1000 - burstDamage, enemy.currentHealth, 3);
            var generated = new MemoryActivationEvent(value.OwnerId, value.SourceMemory, value.ActivationId, host.Packet(), value.VictimId,
                value.EventKind, value.NativePayloadKind, GeneratedOrigin.Bridge, value.EquipmentEpoch);
            host.NotifyAuthored(runtime, generated, enemy); host.FlushAuthored(runtime);
            Assert.Equal(1000 - burstDamage, enemy.currentHealth, 3);
        }

        [Fact]
        public void Memory_preparations_choose_one_real_next_hit_and_relay_changes_only_native_named_q_in_window()
        {
            var preparedA = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.MemoryPrimed, ChannelId = "native.prime.a", Source = Source(Identity),
                Trigger = MemoryEventKind.ConfirmedUse, Primed = new MemoryPrimedDefinition("native.prime.a", Identity, MemoryEventKind.ConfirmedUse, 2000) };
            var preparedB = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.MemoryPrimed, ChannelId = "native.prime.b", Source = Source(Q),
                Trigger = MemoryEventKind.ConfirmedUse, Primed = new MemoryPrimedDefinition("native.prime.b", Q, MemoryEventKind.ConfirmedUse, 3000) };
            var preparedBuild = Allocated("Hero_Vesper", new[] { preparedA, preparedB }, out _);
            float preparedRank = (float)(1m + 1.5m * preparedBuild.SpentStarPoints / 500);
            var (host, runtime) = Setup(preparedBuild); var enemy = Enemy();
            host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, Identity), MemoryEventKind.ConfirmedUse));
            host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, Q), MemoryEventKind.ConfirmedUse));
            host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, "", NativePayloadKind.MainBasicAttack), MemoryEventKind.OwnedBasicAttackHit, enemy), enemy);
            host.FlushAuthored(runtime); Assert.Equal(1000 - 30 * preparedRank, enemy.currentHealth, 3);
            host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, "", NativePayloadKind.MainBasicAttack), MemoryEventKind.OwnedBasicAttackHit, enemy), enemy);
            host.FlushAuthored(runtime); Assert.Equal(1000 - 50 * preparedRank, enemy.currentHealth, 3);
            var relay = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.RelayWindow, ChannelId = "native.relay", Source = Source("St_R_Tranquility"),
                Trigger = MemoryEventKind.ConfirmedUse, Relay = new RelayWindowDefinition("native.relay", "St_Q_SuperNova", 2500) };
            runtime.Hero.Skill.Skills[HeroSkillLocation.R] = new St_R_Tranquility { owner = runtime.Hero };
            runtime.Hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_SuperNova { owner = runtime.Hero };
            var relayBuild = Allocated("Hero_Yubar", new[] { relay }, out _);
            host.BindAuthored(runtime, relayBuild);
            host.NotifyAuthored(runtime, Event(host, host.Activation(runtime.Hero, "St_R_Tranquility"), MemoryEventKind.ConfirmedUse));
            var hit = host.Activation(runtime.Hero, "St_Q_SuperNova");
            Assert.Equal(100 + (float)(25m * (1m + 1.5m * relayBuild.SpentStarPoints / 500)), host.RelayDamage(runtime, enemy, hit, 100), 3);
            UnityEngine.Time.time = 4; Assert.Equal(100, host.RelayDamage(runtime, enemy, hit, 100));
        }

        [Theory]
        [InlineData(WardAmountBasis.CasterMaxOffense, ModShieldPoolKind.Allied, 2500, 25f)]
        [InlineData(WardAmountBasis.RecipientMaxHP, ModShieldPoolKind.Allied, 250, 50f)]
        [InlineData(WardAmountBasis.RecipientMaxHP, ModShieldPoolKind.Ordinary, 2000, 400f)]
        public void Ward_preserves_basis_and_pool_through_decode_and_real_recipient_dispatch(WardAmountBasis basis, ModShieldPoolKind pool, int units, float amount)
        {
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.AlliedWard, ChannelId = "native.ward", Source = Source(Identity),
                Trigger = MemoryEventKind.ConfirmedUse, Ward = new AlliedWardDefinition("native.ward", WardRecipientKind.AlliedTravelers, basis, pool,
                    units, false) };
            var (host, runtime) = Setup(Allocated(spec, out _)); var ally = new Hero { maxHealth = 2000, currentHealth = 1000 };
            DewPlayer.gamePlayers.Add(new DewPlayer { hero = ally });
            var cast = host.Activation(runtime.Hero, Identity); host.NotifyAuthored(runtime, Event(host, cast, MemoryEventKind.ConfirmedUse));
            Assert.Equal(amount, ally.Status.currentShield, 4); Assert.Equal(0, runtime.Hero.Status.currentShield);
            host.NotifyAuthored(runtime, Event(host, cast, MemoryEventKind.ConfirmedUse)); Assert.Equal(amount, ally.Status.currentShield, 4);
        }

        [Fact]
        public void Selected_calm_uses_real_successful_q_provenance_and_one_ordinary_pool_then_removal_disables_it()
        {
            const string q = "St_Q_EmbracingTheChill";
            var flag = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.StunSourceFilter, ChannelId = "native.calm" };
            var key = Key(CalmShieldGrant.ConsumerId, q, grants: new[] { flag });
            var build = Allocated("Hero_Cetus", Array.Empty<AuthoredMechanismSpec>(), out _, key);
            var (host, runtime) = Setup(build, new Hero_Cetus());
            runtime.Hero.Status.hasStun = false;
            var enemy = Enemy(); enemy.Status.hasStun = true;
            var actor = new Ai_Q_EmbracingTheChill_Explosion
                { parentActor = runtime.Hero.Skill.GetSkill(HeroSkillLocation.Q), info = new CastInfo(runtime.Hero) };
            var identity = host.Activation(runtime.Hero, q); host.AdmitNativeScope(actor, identity);
            var effect = new StunEffect { victim = enemy, parent = new StatusEffect { parentActor = actor } };
            var capture = host.CaptureCalmStun(effect); Assert.NotNull(capture);
            enemy.Status.hasStun = false;
            float expected = runtime.Hero.maxHealth * (float)(MemoryDamageBalance.Effect_legacy_h_cetus_key2_native_value / 100m) * (1f + build.Get(Stat.ShieldPower) / 100f);
            host.CompleteCalmStun(effect, capture); Assert.Equal(0, runtime.Hero.Status.currentShield);
            enemy.Status.hasStun = true;
            host.CompleteCalmStun(effect, capture); Assert.Equal(expected, runtime.Hero.Status.currentShield, 4);
            host.CompleteCalmStun(effect, capture); Assert.Equal(expected, runtime.Hero.Status.currentShield, 4);
            host.BindAuthored(runtime, new Build()); UnityEngine.Time.time = 3;
            Assert.Null(host.CaptureCalmStun(effect)); Assert.Equal(0, runtime.Hero.Status.currentShield);
        }

        [Fact]
        public void Native_keystone_and_generated_echo_layers_execute_together_and_deferred_payload_recomputes_after_removal()
        {
            var spec = Gimmick("native.echo", GimmickEffect.Echo, 40); spec.Source = Source(Q);
            var build = Allocated("Hero_Vesper", new[] { spec }, out _, Key("test.native.key"));
            float rank = (float)(1m + 1.5m * build.SpentStarPoints / 500);
            var (host, runtime) = Setup(build); var enemy = Enemy();
            var activation = host.Activation(runtime.Hero, Q); var native = new DamageData(120);
            host.NativeKeyDamage(runtime, enemy, activation, ref native); Assert.Equal(120, native.currentAmount, 4);
            host.NotifyAuthored(runtime, Event(host, activation, MemoryEventKind.Hit, enemy), enemy, native.currentAmount);
            UnityEngine.Time.time = .3f; host.FlushAuthored(runtime); Assert.Equal(1000 - 96 * rank, enemy.currentHealth, 3);
            var next = host.Activation(runtime.Hero, Q); host.NotifyAuthored(runtime, Event(host, next, MemoryEventKind.Hit, enemy), enemy, 120);
            build.SelectedKeystone = null; host.BindAuthored(runtime, Build.Decode(build.Encode()));
            UnityEngine.Time.time = .6f; host.FlushAuthored(runtime); Assert.Equal(1000 - (96 + 48) * rank, enemy.currentHealth, 3);
        }

        [Fact]
        public void Fine_typed_echo_coefficient_survives_real_purchase_wire_key_and_native_damage_dispatch()
        {
            var spec = Gimmick("native.fine.echo", GimmickEffect.Echo, .0000001m);
            spec.Source = Source(Q); spec.Gimmick.UncappedValue = .00000001m;
            var key = new KeystoneDefinition("test.fine.echo.key", new[] { Q },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, new KeystoneMagnitude(1),
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo })) });
            var build = Allocated("Hero_Vesper", new[] { spec }, out _, key);
            var (host, runtime) = Setup(build);
            var enemy = Enemy(); enemy.currentHealth = .0000001f;
            var activation = host.Activation(runtime.Hero, Q); var damage = new DamageData(100);
            host.NativeKeyDamage(runtime, enemy, activation, ref damage);
            host.NotifyAuthored(runtime, Event(host, activation, MemoryEventKind.Hit, enemy), enemy, damage.currentAmount);
            UnityEngine.Time.time = .3f; host.FlushAuthored(runtime);
            float expected = .0000001f - (float)(.000000010001m * (1m + 1.5m * build.SpentStarPoints / 500));
            Assert.InRange(enemy.currentHealth, expected - .0000000000001f, expected + .0000000000001f);
        }

        [Fact]
        public void Retained_legacy_echo_uses_precap_value_and_current_key_when_deferred_native_effect_executes()
        {
            var key = new KeystoneDefinition("test.legacy.cap.key", new[] { Q },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, new KeystoneMagnitude(-7500),
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo })) });
            var build = new Build { SelectedKeystone = key };
            int cap = Gimmicks.Cap(GimmickEffect.Echo);
            build.Gimmicks.Add(new GimmickEntry { StarId = "test.legacy.echo", Memory = Q,
                Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = cap, UncappedValue = cap * 2 } });
            var (host, runtime) = Setup(Build.Decode(build.Encode())); var enemy = Enemy();
            var identity = host.Activation(runtime.Hero, Q); var native = new DamageData(100);
            host.NativeKeyDamage(runtime, enemy, identity, ref native); Assert.Equal(100, native.currentAmount, 4);
            host.LegacyNativeHit(runtime, enemy, identity, native.currentAmount);
            float afterFirst = 1000f - cap / 2f;
            UnityEngine.Time.time = .3f; host.FlushAuthored(runtime); Assert.Equal(afterFirst, enemy.currentHealth, 4);
            identity = host.Activation(runtime.Hero, Q); host.LegacyNativeHit(runtime, enemy, identity, 10);
            build.SelectedKeystone = null; host.BindAuthored(runtime, Build.Decode(build.Encode()));
            UnityEngine.Time.time = .6f; host.FlushAuthored(runtime); Assert.Equal(afterFirst - cap / 10f, enemy.currentHealth, 4);
        }

        [Fact]
        public void Retained_preparation_survives_equal_build_but_key_and_equipment_epochs_clear_only_its_cached_bonus()
        {
            var key = new KeystoneDefinition("test.legacy.primed.key", new[] { Q },
                new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, new KeystoneMagnitude(10000),
                    new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Primed })) });
            var build = new Build { SelectedKeystone = key };
            build.Powers.Add(Power.EchoingDodge, 10);
            build.Gimmicks.Add(new GimmickEntry { StarId = "test.legacy.preparation", Memory = Q,
                Def = new GimmickDef { Trigger = GimmickTrigger.OnUse, Effect = GimmickEffect.Primed, Value = 40 } });
            var (host, runtime) = Setup(Build.Decode(build.Encode())); var enemy = Enemy();
            void Arm()
            {
                host.LegacyNativeUse(runtime, Q);
                host.FlushAuthored(runtime);
            }
            Arm(); host.BindAuthored(runtime, Build.Decode(build.Encode()));
            Assert.Equal(80, runtime.Powers.OnAttackHit(0, 1000, 100, 100, 1).PrimedDamage);
            Arm(); runtime.Powers.OnSkillUsed(0, true, false);
            build.SelectedKeystone = null; host.BindAuthored(runtime, Build.Decode(build.Encode()));
            var cleared = runtime.Powers.OnAttackHit(0, 1000, 100, 100, 1);
            Assert.Equal(0, cleared.PrimedDamage); Assert.Equal(10, cleared.EchoDamage);
            Arm(); Assert.Equal(40, runtime.Powers.OnAttackHit(0, 1000, 100, 100, 1).PrimedDamage);
            build.SelectedKeystone = key; host.BindAuthored(runtime, Build.Decode(build.Encode())); Arm();
            runtime.Hero.Skill.UnequipSkill(HeroSkillLocation.Q);
            Assert.False(host.AuthoredKeystoneActive(runtime.Hero, key.KeystoneId));
            Assert.Equal(0, runtime.Powers.OnAttackHit(0, 1000, 100, 100, 1).PrimedDamage);
            runtime.Hero.Skill.EquipSkill(HeroSkillLocation.Q, new St_Q_CruelSun { owner = runtime.Hero });
            Assert.True(host.AuthoredKeystoneActive(runtime.Hero, key.KeystoneId));
            Arm(); Assert.Equal(80, runtime.Powers.OnAttackHit(0, 1000, 100, 100, 1).PrimedDamage);
        }


        [Fact]
        public void Exact_native_sacrifice_wrapper_awards_next_update_without_reducing_native_damage()
        {
            HostAuthority.InstallFinalNativeKeyHook();
            HostAuthority.InstallNativeSacrificeHook();
            var grant = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.SacrificeShield, ChannelId = "native.sacrifice" };
            var key = Key("h.aurena.key2", "St_Q_GoldenBurst", new[] { KeystonePayloadKind.SacrificeShield }, new[] { grant });
            var build = Allocated("Hero_Aurena", Array.Empty<AuthoredMechanismSpec>(), out _, key);
            var hero = new Hero(); hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_GoldenBurst { owner = hero };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, HeroKey = "Hero_Aurena", Powers = new PowerRuntime(build, 0) };
            var host = new HostAuthority(); Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0;
            host.BindAuthored(runtime, Build.Decode(build.Encode()));
            var native = new DamageData(100); var identity = host.Activation(hero, "St_Q_GoldenBurst");
            host.NativeKeyDamage(runtime, Enemy(), identity, ref native); Assert.Equal(100, native.currentAmount, 4);
            host.PaySacrifice(runtime, 20); Assert.Equal(980, hero.currentHealth); Assert.Equal(0, hero.Status.currentShield);
            host.FlushAuthored(runtime); Assert.Equal(10, hero.Status.currentShield);
            host.FlushAuthored(runtime); Assert.Equal(10, hero.Status.currentShield);
            host.BindAuthored(runtime, new Build()); host.PaySacrifice(runtime, 20); host.FlushAuthored(runtime);
            Assert.Equal(960, hero.currentHealth); Assert.Equal(0, hero.Status.currentShield);
        }

        [Fact]
        public void Missing_verified_hp_payment_callsite_disables_shield_upside_without_cancelling_native_cost()
        {
            HostAuthority.InstallFinalNativeKeyHook(); HostAuthority.InstallNativeSacrificeHook();
            var build = new Build { SelectedKeystone = Key("h.aurena.key2", "St_Q_GoldenBurst", new[] { KeystonePayloadKind.SacrificeShield }) };
            var hero = new Hero(); hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_GoldenBurst { owner = hero };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0) };
            var host = new HostAuthority(); Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0;
            host.BindAuthored(runtime, Build.Decode(build.Encode()));
            Assert.Throws<InvalidOperationException>(() => HostAuthority.InstallNativeSacrificeHook(false));
            try
            {
                host.BindAuthored(runtime, Build.Decode(build.Encode()));
                var data = new DamageData(100);
                host.NativeKeyDamage(runtime, Enemy(), host.Activation(hero, "St_Q_GoldenBurst"), ref data);
                Assert.Equal(100, data.currentAmount, 4);
                host.PaySacrifice(runtime, 20); host.FlushAuthored(runtime);
                Assert.Equal(980, hero.currentHealth); Assert.Equal(0, hero.Status.currentShield);
            }
            finally { HostAuthority.InstallNativeSacrificeHook(); }
        }

        [Fact]
        public void Unadmitted_chalice_keystone_stays_fully_off_while_sibling_keystone_keeps_working()
        {
            HostAuthority.InstallFinalNativeKeyHook();
            Assert.Throws<InvalidOperationException>(() => HostAuthority.InstallNativeSacrificeHook(false)); // 支払いアダプター未接続 → 満ちた聖杯は個別に不許可
            try
            {
                var sibling = new KeystoneDefinition("h.aurena.key", Array.Empty<string>(),
                    new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Value, new KeystoneMagnitude(10000),
                        new KeystoneScope(targetEffectSet: new[] { GimmickEffect.Echo })) });
                var chalice = new KeystoneDefinition("h.aurena.key2", Array.Empty<string>(),
                    new[] { KeystoneTransform.Scale(KeystoneLayer.ModEffect, KeystoneField.Radius, new KeystoneMagnitude(10000),
                        new KeystoneScope(payloadKind: KeystonePayloadKind.AlliedWard)) },
                    payloads: new[] { KeystonePayloadKind.SacrificeShield });
                var build = new Build { SelectedKeystone = sibling };
                build.AddSelectedKeystone(chalice);
                var (host, runtime) = Setup(Build.Decode(build.Encode()));
                var hero = runtime.Hero;
                var echo = new KeystonePayload(KeystoneLayer.ModEffect, 40, new KeystoneCaps(120), KeystonePayloadKind.Gimmick, GimmickEffect.Echo);
                var ward = new KeystonePayload(KeystoneLayer.ModEffect, 2, new KeystoneCaps(1000, radiusMetres: 100), KeystonePayloadKind.AlliedWard, radiusMetres: 10);
                decimal Echo(string source) => host.TransformAuthoredPayload(hero, echo, source, null, KeystoneSourceKind.NativeMemory).Value;
                decimal Ward(string source) => host.TransformAuthoredPayload(hero, ward, source, null, KeystoneSourceKind.NativeMemory).RadiusMetres;
                // 1) 犠牲記憶なし: 聖杯は不許可、兄弟刻印だけ有効。
                Assert.True(host.AuthoredKeystoneActive(hero, "h.aurena.key"));
                Assert.False(host.AuthoredKeystoneActive(hero, "h.aurena.key2"));
                Assert.Equal(80m, Echo(Q));
                Assert.Equal(10m, Ward(Q));
                // 2) GoldenBurst装備（装備変更の再設定経路）でも聖杯は不許可のまま: 支払いは起きるがシールドは付かない。
                hero.Skill.EquipSkill(HeroSkillLocation.Q, new St_Q_GoldenBurst { owner = hero });
                host.AuthoredKeystoneEpoch(hero);
                Assert.False(host.AuthoredKeystoneActive(hero, "h.aurena.key2"));
                host.PaySacrifice(runtime, 20); host.FlushAuthored(runtime);
                Assert.Equal(980, hero.currentHealth); Assert.Equal(0, hero.Status.currentShield);
                // 3) 同じnative epochで支払いアダプターが接続: 全体のAny(true)は不変のまま聖杯だけが切り替わる。
                HostAuthority.InstallNativeSacrificeHook();
                host.AuthoredKeystoneEpoch(hero);
                Assert.True(host.AuthoredKeystoneActive(hero, "h.aurena.key2"));
                Assert.Equal(80m, Echo("St_Q_GoldenBurst"));
                Assert.Equal(20m, Ward("St_Q_GoldenBurst"));
                host.PaySacrifice(runtime, 20); host.FlushAuthored(runtime);
                Assert.Equal(960, hero.currentHealth); Assert.Equal(10, hero.Status.currentShield);
                // 4) 再び不許可: 変換とシールドは即座に外れ、兄弟刻印は動き続ける。
                Assert.Throws<InvalidOperationException>(() => HostAuthority.InstallNativeSacrificeHook(false));
                host.AuthoredKeystoneEpoch(hero);
                Assert.False(host.AuthoredKeystoneActive(hero, "h.aurena.key2"));
                Assert.Equal(80m, Echo("St_Q_GoldenBurst"));
                Assert.Equal(10m, Ward("St_Q_GoldenBurst"));
                host.PaySacrifice(runtime, 20); host.FlushAuthored(runtime);
                Assert.Equal(940, hero.currentHealth); Assert.Equal(10, hero.Status.currentShield);
            }
            finally { HostAuthority.InstallNativeSacrificeHook(); }
        }

        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime, Actor Transport,
            AbilityInstance Native, HostAuthority.WinningRandom Random) DividendLifecycle(bool ultimate, bool remote = false)
        {
            string memory = ultimate ? "St_U_ShoutOfOblivion" : "St_L_CoinExplosion";
            var channel = new PressureDividendChannel(new[] { new PressureDividendContribution("native.dividend", memory, 4000) });
            var spec = new AuthoredMechanismSpec { Kind = AuthoredMechanismKind.PressureDividend, ChannelId = "native.dividend", Source = Source(memory),
                Trigger = MemoryEventKind.Kill, Budget = AttributionBudget.PerKill, Dividend = channel };
            var (host, runtime) = Setup(Allocated(spec, out _));
            UnityEngine.Time.unscaledTime = 100;
            runtime.Hero.netId = remote ? 2u : 1u;
            DewPlayer.local = remote ? new DewPlayer { hero = new Hero() } : runtime.Hero.owner;
            DewPlayer.gamePlayers.Add(DewPlayer.local);
            if (remote) DewPlayer.gamePlayers.Add(runtime.Hero.owner);
            var transport = host.RegisterNegotiation();
            SkillTrigger installed = ultimate ? (SkillTrigger)new St_U_ShoutOfOblivion { type = SkillType.Ultimate }
                : new St_L_CoinExplosion { type = SkillType.Normal };
            installed.owner = runtime.Hero;
            runtime.Hero.Skill.Skills[ultimate ? HeroSkillLocation.R : HeroSkillLocation.W] = installed;
            var cast = host.BeginAttributedMemoryCast(installed);
            AbilityInstance native = ultimate ? (AbilityInstance)new Ai_U_ShoutOfOblivion { parentActor = installed, info = new CastInfo(runtime.Hero) }
                : new Ai_L_CoinExplosion_Explosion { parentActor = installed, info = new CastInfo(runtime.Hero) };
            host.AdmitNativeScope(native, cast.Identity);
            return (host, runtime, transport, native, host.StartIssue62Lifecycle());
        }

        private static EventInfoKill DividendDeath(HostAuthority host, AbilityInstance native, Monster enemy, bool admitted = true)
        {
            Assert.True(native.PureDamage(2000, 1).currentAmount > enemy.currentHealth);
            native.PureDamage(2000, 1).Dispatch(enemy);
            Assert.True(enemy.currentHealth <= 0);
            Assert.True(host.TryIssue62Activation(native, out var identity));
            NativeAttributedDamagePacket.Current = new NativeAttributedDamagePacket.Packet { Actor = native, Victim = enemy,
                Identity = identity, Admitted = admitted, Serial = host.Packet(), DamageAmount = 2000 };
            var kill = new EventInfoKill { actor = native, victim = enemy };
            enemy.RaiseDeath(kill);
            Assert.Equal(0, host.Issue62Records.Combat);
            Assert.Equal(0, host.Issue62Records.Queue);
            return kill;
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Pressure_dividend_survives_death_then_native_kill_then_entity_removal(bool ultimate, bool remote)
        {
            var (host, runtime, transport, native, random) = DividendLifecycle(ultimate, remote);
            var enemy = new Monster { Relation = EntityRelation.Enemy, owner = new DewPlayer { isHumanPlayer = false } };
            host.SpawnIssue62Enemy(enemy, 1.25);
            var kill = DividendDeath(host, native, enemy);
            Assert.Equal(0, random.Rolls);
            Assert.Empty(transport.ClientMessages);
            var fact = Assert.Single(host.OrdinaryKillFacts);
            var profile = Profile.CreateNew(15); Rules.BeginRun(profile, fact.RunId);
            var expected = Profile.CreateNew(15); Rules.BeginRun(expected, fact.RunId);
            profile.Run.Bounties.Clear(); expected.Run.Bounties.Clear();
            var ledger = new KillClassificationLedger();
            Assert.True(ledger.ObserveDeath(new PendingMonsterDeath(enemy.netId,
                new PendingRunKill(fact.RunId, 0, 0, MonsterTier.Normal, 1, NightmareAffix.None, null, runtime.HeroKey),
                fact.StreamId), now: 100));
            Assert.True(ledger.ReceiveFact(fact, now: 100));
            Assert.True(ledger.TryResolve(out var ordinary, now: 100));
            Rules.OnKill(profile, ordinary.Tier, ordinary.Level, ordinary.Nightmare, ordinary.HeroKey,
                variantId: ordinary.VariantId, roomIndex: ordinary.RoomIndex);
            Rules.OnKill(expected, MonsterTier.Normal, 1, heroKey: runtime.HeroKey, roomIndex: 0);
            Assert.Equal(1, profile.Run.Kills);
            Assert.Equal(expected.Run.SatchelShards, profile.Run.SatchelShards);
            Assert.Equal(expected.Hero(runtime.HeroKey).StarXp, profile.Hero(runtime.HeroKey).StarXp);

            native.InvokeOnKill(kill);
            native.InvokeOnKill(kill); // Repeated native packet.
            NativeAttributedDamagePacket.Current.Serial = host.Packet();
            native.InvokeOnKill(kill); // A second packet for the same death still cannot roll again.
            native.parentActor.InvokeOnKill(kill); // Parent propagation retains the original damage actor.
            Assert.Equal(1, random.Rolls);
            var receipt = Assert.IsType<DreamforgePressureDividendMsg>(Assert.Single(transport.ClientMessages));
            Assert.Same(runtime.Hero.owner, Assert.Single(transport.ClientRecipients));
            Assert.Equal(runtime.Hero.netId, receipt.heroNetId);
            Assert.Equal(runtime.Hero.netId.ToString(), receipt.ownerId);
            Assert.Equal(1, receipt.ToReward().ShardCount);
            var pending = new PendingPressureDividends();
            Assert.True(pending.AddAuthenticated(receipt.ToReward(), receipt.runId, receipt.ownerId));
            Assert.Equal(1, pending.Drain(profile));
            Assert.Equal(expected.Run.SatchelShards + 1, profile.Run.SatchelShards);
            Assert.Equal(1, profile.Run.Kills);
            Assert.Equal(expected.Hero(runtime.HeroKey).StarXp, profile.Hero(runtime.HeroKey).StarXp);
            Assert.False(pending.AddAuthenticated(receipt.ToReward(), receipt.runId, receipt.ownerId));
            host.RemoveIssue62Entity(enemy);
            Assert.Equal((0, 0, 0, 0, 0, 0), host.Issue62Records);
            native.InvokeOnKill(kill);
            Assert.Single(transport.ClientMessages);
            Assert.Equal(1, random.Rolls);
        }

        [Theory]
        [InlineData(false, "pressure")]
        [InlineData(true, "pressure")]
        [InlineData(false, "loot")]
        [InlineData(true, "loot")]
        [InlineData(false, "summon")]
        [InlineData(true, "summon")]
        [InlineData(false, "generated")]
        [InlineData(true, "generated")]
        [InlineData(false, "unknown")]
        [InlineData(true, "unknown")]
        [InlineData(false, "unattributed")]
        [InlineData(true, "unattributed")]
        public void Pressure_dividend_exclusions_do_not_roll_after_real_death(bool ultimate, string exclusion)
        {
            var (host, runtime, transport, native, random) = DividendLifecycle(ultimate);
            var enemy = new Monster { Relation = EntityRelation.Enemy,
                owner = new DewPlayer { isHumanPlayer = exclusion == "summon" }, disableLoot = exclusion == "loot" };
            host.SpawnIssue62Enemy(enemy, exclusion == "pressure" ? 1.249 : 1.25,
                roomSpawn: exclusion != "summon" && exclusion != "unknown");
            var kill = DividendDeath(host, native, enemy, admitted: exclusion != "generated");
            if (exclusion == "unattributed") NativeAttributedDamagePacket.Current = null;
            native.InvokeOnKill(kill);
            native.InvokeOnKill(kill);
            Assert.Empty(transport.ClientMessages);
            Assert.Equal(0, random.Rolls);
            host.RemoveIssue62Entity(enemy);
            Assert.Equal((0, 0, 0, 0, 0, 0), host.Issue62Records);
        }

        [Fact]
        public void Pressure_dividend_deaths_expire_at_ten_unscaled_seconds_without_entity_removal()
        {
            var (host, runtime, transport, native, random) = DividendLifecycle(false);
            for (int expedition = 0; expedition < 32; expedition++)
            {
                float start = 100 + expedition * 10;
                UnityEngine.Time.unscaledTime = start;
                var enemy = new Monster { Relation = EntityRelation.Enemy, owner = new DewPlayer { isHumanPlayer = false } };
                host.SpawnIssue62Enemy(enemy, 1.25);
                var kill = DividendDeath(host, native, enemy);
                UnityEngine.Time.unscaledTime = start + 9.99f;
                host.PruneIssue62Deaths();
                Assert.Equal(1, host.Issue62Records.Spawns);
                Assert.Equal(1, host.Issue62Records.Expiry);
                if (expedition % 2 == 0) native.InvokeOnKill(kill); // Clean both paid and unclaimed deaths.
                UnityEngine.Time.unscaledTime = start + 10;
                host.PruneIssue62Deaths();
                Assert.Equal((0, 0, 0, 0, 0, 0), host.Issue62Records);
                int receipts = transport.ClientMessages.Count;
                native.InvokeOnKill(kill);
                Assert.Equal(receipts, transport.ClientMessages.Count);
            }
            Assert.Equal(16, random.Rolls);
            Assert.Equal(0, UnityEngine.Time.time); // Paused/scaled time does not extend retention.
        }

        [Fact]
        public void Typed_wound_ticks_element_edge_and_sap_use_existing_native_handlers_without_generated_feedback()
        {
            var wound = Gimmick("native.wound", GimmickEffect.Wound, 30);
            var sap = Gimmick("native.sap", GimmickEffect.Sap, 10);
            var edge = Gimmick("native.edge", GimmickEffect.ElementEdge, 10);
            var build = Allocated("Hero_Vesper", new[] { wound, sap, edge }, out _);
            float rank = (float)(1m + 1.5m * build.SpentStarPoints / 500);
            var (host, runtime) = Setup(build); var enemy = Enemy();
            enemy.Status.fireStack = 1; enemy.Status.hasCold = true;
            var hit = Event(host, host.Activation(runtime.Hero, Identity), MemoryEventKind.Hit, enemy);
            host.NotifyAuthored(runtime, hit, enemy); host.FlushAuthored(runtime);
            Assert.Equal(1000 - 20 * rank, enemy.currentHealth, 3);
            var outgoing = new DamageData(100);
            foreach (var processor in enemy.dealtDamageProcessor.Entries) processor(ref outgoing, enemy, runtime.Hero);
            Assert.Equal(90, outgoing.currentAmount, 4);
            UnityEngine.Time.time = .5f; host.FlushAuthored(runtime); Assert.Equal(1000 - 25 * rank, enemy.currentHealth, 3);
            UnityEngine.Time.time = 3; host.FlushAuthored(runtime); Assert.Equal(1000 - 50 * rank, enemy.currentHealth, 3);
        }

        [Fact]
        public void Three_hundred_all_source_channels_do_not_expand_runtime_entries_or_drop_paid_heals()
        {
            var specs = Enumerable.Range(0, 300).Select(i =>
            { var spec = Gimmick("native.all." + i, GimmickEffect.Heal, .01m); spec.Source = null; return spec; }).ToArray();
            var (host, runtime) = Setup(Decoded(specs)); runtime.Hero.currentHealth = 500;
            var hit = Event(host, host.Activation(runtime.Hero, Identity), MemoryEventKind.Hit, Enemy());
            host.NotifyAuthored(runtime, hit); host.FlushAuthored(runtime);
            Assert.Equal(530, runtime.Hero.currentHealth, 2);
            host.NotifyAuthored(runtime, hit); host.FlushAuthored(runtime); Assert.Equal(530, runtime.Hero.currentHealth, 2);
        }
    }
}
