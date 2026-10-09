using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class EffectiveAllocationValidationV131Tests
    {
        private const string Hero = "Hero_Cetus";
        private const string Memory = "St_D_IcyVeins";
        private static Profile Funded()
        {
            var p = Profile.CreateNew(15031);
            p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            p.Hero(Hero).Kills = 1000000;
            return p;
        }
        private static TalentDef StatStar(string id, int amount = 1, int ranks = 1, int cost = 1, Stat stat = Stat.Armor) =>
            new TalentDef(id, Line.Offense, new Txt("試験", "Test"), stat, amount, ranks) { HeroKey = Hero, RankCost = cost };
        private static TalentDef Effect(string id, GimmickEffect effect, decimal value, int ranks = 1, int cost = 1, string memory = Memory)
        {
            var t = StatStar(id, 0, ranks, cost);
            t.RouteMemory = memory;
            t.Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value };
            return t;
        }
        private static TalentDef Modifier(string id, string recipient, GimmickParam? field, decimal amount, int ranks = 1, int cost = 1)
        {
            var t = StatStar(id, 0, ranks, cost);
            t.RouteMemory = Memory;
            t.ScopedModifier = new ScopedModifierDef
            {
                ScopeKind = ScopeKind.EffectChannel, ScopeMemory = Memory, TargetEffectIds = new[] { recipient },
                Param = field, Amount = ModifierUnits.FromPercent(amount),
            };
            return t;
        }
        private static TalentDef Choice(string id, TalentDef a, TalentDef b, int ranks = 1, int cost = 1)
        {
            var t = StatStar(id, 0, ranks, cost);
            t.ClusterStar = new ClusterStarDef { Kind = ClusterStarKind.Choice };
            t.Choices = new[] { a, b };
            return t;
        }
        private static TalentDef Key(string id) => new TalentDef(id, Line.Offense, new Txt("試験核", "Test Key"),
            Power.Aegis, 3, new Txt("試験効果", "Test effect")) { HeroKey = Hero };
        private static TalentDef Requires(TalentDef talent, string prerequisite)
        {
            talent.AuthoredStar = new AuthoredStarDef { RequiredStarIds = new[] { prerequisite } };
            return talent;
        }
        private static IReadOnlyList<TalentDef> Tree(params TalentDef[] talents) => HeroSigils.TreeFor(Hero)
            .Where(t => t.Cluster == null).Concat(talents).ToArray();
        private static EffectiveAllocationValidation Engine(params TalentDef[] talents) => new EffectiveAllocationValidation(Tree(talents));
        private static AllocationChange Purchase(string id, int? option = null) => new AllocationChange
        { Kind = AllocationChangeKind.Purchase, CandidateStarId = id, SelectedOption = option };
        private static void Add(Profile p, EffectiveAllocationValidation engine, string id, int? option = null) =>
            Rules.ApplyAllocationChange(p, Hero, Purchase(id, option), validation: engine);
        private static string State(Profile p) => ProfileCodec.Write(p);

        [Fact]
        public void Fractional_boost_has_positive_sub_milli_output_and_remains_effective_without_current_equipment()
        {
            var effect = Effect("test.fractional.effect", GimmickEffect.Shield, 0.25m);
            var boost = Modifier("test.fractional.boost", effect.Id, null, 0.5m);
            var tree = Tree(effect, boost);
            var engine = new EffectiveAllocationValidation(tree);
            var p = Funded();
            Assert.All(p.Hero(Hero).Equipped, uid => Assert.Null(uid));
            Add(p, engine, effect.Id);
            var plan = engine.Preview(p, Hero, Purchase(boost.Id));
            Assert.True(plan.CanApply);
            Assert.Equal(250m, plan.OldEffectiveChannels.Single(c => c.StarId == effect.Id).ValueMilli);
            Assert.Equal(251.25m, plan.NewEffectiveChannels.Single(c => c.StarId == effect.Id).ValueMilli);
            engine.Commit(p, plan);
            var build = Build.ComputeForTree(p, Hero, 0, tree);
            Assert.Equal(0.25125m, build.Gimmicks.Single().Def.Value);
            Assert.Equal(0.25125m, Build.Decode(build.Encode()).Gimmicks.Single().Def.Value);
        }

        [Fact]
        public void Each_rank_requires_gain_partial_cap_rank_is_allowed_and_fully_capped_rank_is_atomic_rejection()
        {
            var sap = Effect("test.sap", GimmickEffect.Sap, Gimmicks.Cap(GimmickEffect.Sap) * 0.4m, 4, 2);
            var engine = Engine(sap);
            var p = Funded();
            for (int i = 0; i < 3; i++) Add(p, engine, sap.Id);
            string before = State(p);
            var plan = engine.Preview(p, Hero, Purchase(sap.Id));
            Assert.False(plan.CandidateEffective);
            var cap = plan.SaturationDetails.First(d => d.StarId == sap.Id && d.Field == AllocationEffectiveField.Value);
            Assert.Equal(4, cap.Rank);
            Assert.Equal(AllocationInertReason.Saturated, cap.Reason);
            Assert.Equal(Gimmicks.Cap(GimmickEffect.Sap) * 1000m, cap.EffectiveValue);
            Assert.Equal(Gimmicks.Cap(GimmickEffect.Sap) * 1000m, cap.Ceiling);
            Assert.Throws<AllocationValidationException>(() => engine.Commit(p, plan));
            Assert.Equal(before, State(p));
            Assert.Equal(6, engine.SpentPoints(p.Hero(Hero)));
        }

        [Fact]
        public void Mandatory_stronger_predecessor_keeps_a_locally_dominated_expose_effective_through_paid_rank()
        {
            var strong = Effect("test.expose.strong", GimmickEffect.Expose, 10m);
            var weak = Requires(Effect("test.expose.weak", GimmickEffect.Expose, 5m), strong.Id);
            var p = Funded();
            var engine = Engine(strong, weak);
            Add(p, engine, strong.Id);
            var plan = engine.Preview(p, Hero, Purchase(weak.Id));
            Assert.True(plan.CanApply);
            Assert.Empty(plan.SaturationDetails);
            Assert.Equal(10m * 1000m * (1m + 1.5m * 1m / 500m),
                plan.OldEffectiveChannels.First(c => c.StarId == strong.Id).ValueMilli);
            Assert.Equal(10m * 1000m * (1m + 1.5m * 2m / 500m),
                plan.NewEffectiveChannels.First(c => c.StarId == strong.Id).ValueMilli);
            engine.Commit(p, plan);
            Assert.Equal(1, p.Hero(Hero).Talents[weak.Id]);
        }

        [Fact]
        public void An_interval_on_a_locally_dominated_damage_source_does_not_remove_its_paid_rank_benefit()
        {
            var strong = Effect("test.clock.strong", GimmickEffect.Expose, 10m);
            var weak = Requires(Effect("test.clock.weak", GimmickEffect.Expose, 5m), strong.Id);
            weak.Gimmick.Cooldown = 1f;
            var p = Funded();
            var engine = Engine(strong, weak);
            Add(p, engine, strong.Id);
            var plan = engine.Preview(p, Hero, Purchase(weak.Id));
            Assert.True(plan.CanApply);
            Assert.Empty(plan.SaturationDetails);
            Assert.Equal(10m * 1000m * (1m + 1.5m * 2m / 500m),
                plan.NewEffectiveChannels.First(c => c.StarId == strong.Id).ValueMilli);
        }

        [Fact]
        public void Different_normal_source_conditions_are_attainable_alternatives_not_permanent_domination()
        {
            var strong = Effect("test.expose.other", GimmickEffect.Expose, 10m, memory: "St_Q_EmbracingTheChill");
            var weak = Effect("test.expose.alternative", GimmickEffect.Expose, 5m);
            var p = Funded();
            var engine = Engine(strong, weak);
            Add(p, engine, strong.Id);
            Add(p, engine, weak.Id);
            Assert.Equal(1, p.Hero(Hero).Talents[weak.Id]);
        }

        [Fact]
        public void Parameter_requires_an_owned_meaningful_recipient_not_merely_a_table_definition()
        {
            var shield = Effect("test.recipient", GimmickEffect.Shield, 1m);
            var duration = Modifier("test.duration", shield.Id, GimmickParam.Duration, 10m);
            var p = Funded();
            var engine = Engine(shield, duration);
            var missing = engine.Preview(p, Hero, Purchase(duration.Id));
            Assert.False(missing.CanApply);
            Assert.Contains(missing.SaturationDetails, d => d.StarId == duration.Id && d.Reason == AllocationInertReason.MissingOwnedRecipient);
            Assert.Empty(p.Hero(Hero).Talents);
            Add(p, engine, shield.Id);
            Add(p, engine, duration.Id);
        }

        [Fact]
        public void Unsupported_parameter_is_not_silently_projected_to_zero_or_purchased()
        {
            var recharge = Effect("test.no.radius", GimmickEffect.Recharge, 10m);
            var radius = Modifier("test.unsupported.radius", recharge.Id, GimmickParam.Radius, 10m);
            var p = Funded();
            string before = State(p);
            var engine = Engine(recharge, radius);
            Assert.Throws<InvalidOperationException>(() => engine.Preview(p, Hero, Purchase(radius.Id)));
            Assert.Equal(before, State(p));
        }

        [Fact]
        public void Saturated_choice_option_is_refused_but_other_explicit_option_remains_purchasable()
        {
            var capped = StatStar("test.capped.stat", Content.StatCap(Stat.Armor));
            const string id = "test.choice";
            var choice = Choice(id, StatStar(id, 1), Effect(id, GimmickEffect.Heal, 1m));
            var p = Funded();
            var engine = Engine(capped, choice);
            Add(p, engine, capped.Id);
            var optionA = engine.Preview(p, Hero, Purchase(id, 0));
            Assert.False(optionA.CanApply);
            Assert.Contains(optionA.SaturationDetails, d => d.StarId == id && d.Ceiling == Content.StatCap(Stat.Armor));
            Assert.Throws<AllocationValidationException>(() => engine.Commit(p, optionA));
            Assert.False(p.Hero(Hero).TalentChoices.ContainsKey(id));
            Add(p, engine, id, 1);
            Assert.Equal(1, p.Hero(Hero).TalentChoices[id]);
        }

        [Fact]
        public void Ranked_choice_uses_one_option_for_all_ranks_and_add_rank_cannot_switch_it()
        {
            const string id = "test.ranked.choice";
            var choice = Choice(id, Effect(id, GimmickEffect.Shield, 1m), Effect(id, GimmickEffect.Heal, 2m), 3, 2);
            var tree = Tree(choice);
            var engine = new EffectiveAllocationValidation(tree);
            var p = Funded();
            var identity = p.Hero(Hero);
            Add(p, engine, id, 0);
            string before = State(p);
            Assert.Throws<InvalidOperationException>(() => Add(p, engine, id, 1));
            Assert.Equal(before, State(p));
            Add(p, engine, id, 0);
            Add(p, engine, id, 0);
            Rules.ApplyAllocationChange(p, Hero, new AllocationChange { Kind = AllocationChangeKind.Choice, CandidateStarId = id, SelectedOption = 1 }, validation: engine);
            Assert.Same(identity, p.Hero(Hero));
            Assert.Equal(3, identity.Talents[id]);
            Assert.Equal(1, identity.TalentChoices[id]);
            Assert.Equal(6, engine.SpentPoints(identity));
            var output = Assert.Single(Build.ComputeForTree(p, Hero, 0, tree).Gimmicks);
            Assert.Equal(GimmickEffect.Heal, output.Def.Effect);
            Assert.Equal(6m, output.Def.Value);
        }

        [Fact]
        public void Keystone_disabled_effect_and_prerequisite_dependents_require_exact_original_cost_atomic_refund()
        {
            var root = StatStar("test.key.root", 1, 6);
            var echo = Effect("test.key.echo", GimmickEffect.Echo, 5m, 2, 3);
            var leaf = Requires(StatStar("test.key.leaf", 1, cost: 4), echo.Id);
            var key = Key("test.key.selected");
            var engine = new EffectiveAllocationValidation(Tree(root, echo, leaf, key), new EffectiveAllocationPolicy
            {
                PermanentDisables = new[] { new AllocationDisableRule { KeystoneId = key.Id, Effect = GimmickEffect.Echo } },
            });
            var p = Funded();
            for (int i = 0; i < 6; i++) Add(p, engine, root.Id);
            Add(p, engine, echo.Id);
            Add(p, engine, echo.Id);
            Add(p, engine, leaf.Id);
            int spent = engine.SpentPoints(p.Hero(Hero));
            string before = State(p);
            var plan = Rules.PreviewAllocationChange(p, Hero, new AllocationChange { Kind = AllocationChangeKind.Keystone, KeystoneId = key.Id }, engine);
            Assert.True(plan.CanApply);
            Assert.Equal(new[] { echo.Id, leaf.Id }, plan.AffectedRefundIds);
            Assert.Equal(10, plan.RefundCost);
            Assert.Equal(6, plan.Refunds.Single(r => r.StarId == echo.Id).Cost);
            Assert.Equal(AllocationInertReason.PermanentlyDisabled, plan.SaturationDetails.Single(d => d.StarId == echo.Id).Reason);
            Assert.Throws<AllocationValidationException>(() => engine.Commit(p, plan));
            Assert.Throws<AllocationValidationException>(() => engine.Commit(p, plan, new[] { echo.Id }));
            Assert.Equal(before, State(p));
            engine.Commit(p, plan, plan.AffectedRefundIds);
            Assert.Equal(key.Id, p.Hero(Hero).Keystone);
            Assert.DoesNotContain(echo.Id, p.Hero(Hero).Talents.Keys);
            Assert.DoesNotContain(leaf.Id, p.Hero(Hero).Talents.Keys);
            Assert.Equal(spent - 10 + Content.KeystoneCost, engine.SpentPoints(p.Hero(Hero)));
            Assert.True(engine.AllocationsConnected(p.Hero(Hero)));
        }

        [Theory]
        [InlineData(GimmickEffect.Expose, true)]
        [InlineData(GimmickEffect.Sap, false)]
        public void Choice_change_keeps_lost_recipient_points_only_when_they_still_rank_damage(GimmickEffect alternative, bool ranksDamage)
        {
            const string id = "test.receiver.choice";
            var choice = Choice(id, Effect(id, GimmickEffect.Shield, 1m), Effect(id, alternative, 5m));
            var duration = Modifier("test.receiver.duration", id, GimmickParam.Duration, 10m, 3, 2);
            duration.ScopedModifier.TargetEffects = new[] { GimmickEffect.Shield };
            var leaf = Requires(StatStar("test.receiver.leaf", 1, cost: 4), duration.Id);
            var p = Funded();
            var engine = Engine(choice, duration, leaf);
            Add(p, engine, id, 0);
            for (int i = 0; i < 3; i++) Add(p, engine, duration.Id);
            Add(p, engine, leaf.Id);
            string before = State(p);
            var change = new AllocationChange { Kind = AllocationChangeKind.Choice, CandidateStarId = id, SelectedOption = 1 };
            var plan = engine.Preview(p, Hero, change);
            Assert.True(plan.CanApply);
            if (ranksDamage)
            {
                Assert.Empty(plan.AffectedRefundIds);
                engine.Commit(p, plan);
                Assert.Equal(3, p.Hero(Hero).Talents[duration.Id]);
                Assert.Equal(1, p.Hero(Hero).Talents[leaf.Id]);
                Assert.Equal(5m * 1000m * (1m + 1.5m * 11m / 500m),
                    plan.NewEffectiveChannels.First(c => c.StarId == id).ValueMilli);
            }
            else
            {
                Assert.Equal(10, plan.RefundCost);
                Assert.Equal(new[] { duration.Id, leaf.Id }, plan.AffectedRefundIds);
                Assert.Throws<AllocationValidationException>(() => engine.Commit(p, plan));
                Assert.Equal(before, State(p));
                engine.Commit(p, plan, plan.AffectedRefundIds);
                Assert.False(p.Hero(Hero).Talents.ContainsKey(duration.Id));
                Assert.False(p.Hero(Hero).Talents.ContainsKey(leaf.Id));
            }
            Assert.Equal(1, p.Hero(Hero).TalentChoices[id]);
            Assert.Equal(1, p.Hero(Hero).Talents[id]);
        }

        [Fact]
        public void Production_refund_path_refuses_unapproved_prerequisite_cascade_then_applies_complete_explicit_plan()
        {
            var root = StatStar("test.refund.root", 1, cost: 3);
            var leaf = Requires(StatStar("test.refund.leaf", 1, cost: 2), root.Id);
            var p = Funded();
            var engine = Engine(root, leaf);
            Rules.RegisterAllocationValidation(Hero, engine);
            try
            {
                Rules.AddTalentRank(p, Hero, root.Id);
                Rules.AddTalentRank(p, Hero, leaf.Id);
                string before = State(p);
                var failure = Assert.Throws<AllocationValidationException>(() => Rules.RemoveTalentRank(p, Hero, root.Id));
                Assert.Equal(5, failure.Plan.RefundCost);
                Assert.Equal(before, State(p));
                Rules.RemoveTalentRank(p, Hero, root.Id, failure.Plan.AffectedRefundIds);
                Assert.Empty(p.Hero(Hero).Talents);
                Assert.Equal(0, Rules.SpentPoints(p.Hero(Hero), Hero));
            }
            finally { Rules.RegisterAllocationValidation(Hero, null); }
        }

        [Fact]
        public void Production_equipment_path_uses_installed_policy_and_never_mutates_before_explicit_refund_approval()
        {
            var echo = Effect("test.gear.echo", GimmickEffect.Echo, 5m, 2, 3);
            var leaf = Requires(StatStar("test.gear.leaf", 1, cost: 2), echo.Id);
            var p = Funded();
            var relic = Loot.RollRelic(new Rng(15031), Rarity.Common, 1, slot: Slot.Weapon);
            p.Stash.Add(relic);
            var engine = new EffectiveAllocationValidation(Tree(echo, leaf), new EffectiveAllocationPolicy
            {
                PermanentDisables = new[] { new AllocationDisableRule { EquippedUid = relic.Uid, StarIds = new[] { echo.Id } } },
            });
            Rules.RegisterAllocationValidation(Hero, engine);
            try
            {
                Rules.AddTalentRank(p, Hero, echo.Id);
                Rules.AddTalentRank(p, Hero, echo.Id);
                Rules.AddTalentRank(p, Hero, leaf.Id);
                string before = State(p);
                var failure = Assert.Throws<AllocationValidationException>(() => Rules.Equip(p, Hero, relic.Uid));
                Assert.Equal(8, failure.Plan.RefundCost);
                Assert.Equal(before, State(p));
                Rules.Equip(p, Hero, relic.Uid, approvedRefundIds: failure.Plan.AffectedRefundIds);
                Assert.Equal(relic.Uid, p.Hero(Hero).Equipped[(int)Slot.Weapon]);
                Assert.Empty(p.Hero(Hero).Talents);
                Rules.Unequip(p, Hero, Slot.Weapon);
                Assert.Null(p.Hero(Hero).Equipped[(int)Slot.Weapon]);
            }
            finally { Rules.RegisterAllocationValidation(Hero, null); }
        }

        [Fact]
        public void Stale_refund_preview_cannot_overwrite_intervening_choice_or_allocation()
        {
            var a = StatStar("test.stale.a");
            var b = StatStar("test.stale.b");
            var engine = Engine(a, b);
            var p = Funded();
            var plan = engine.Preview(p, Hero, Purchase(a.Id));
            Add(p, engine, b.Id);
            string before = State(p);
            Assert.Throws<InvalidOperationException>(() => engine.Commit(p, plan));
            Assert.Equal(before, State(p));
        }

        [Fact]
        public void Native_and_link_haste_share_final_on_use_cap_not_two_separately_positive_snapshots()
        {
            const string profileId = "test.c15.haste.cap";
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile
            { Id = profileId, Kind = LinkKind.MemoryHaste, Maximum = ValueUnits.FromPercent(100m) });
            var link = new TalentDef("test.haste.link", Line.Offense, new Txt("加速", "Haste"),
                new LinkDef { Kind = LinkKind.MemoryHaste, Value = 20m, Requires = new[] { Memory } }, 5) { HeroKey = Hero };
            var native = StatStar("test.haste.native", 0);
            native.RouteMemory = Memory;
            native.NativeModifier = new NativeMemoryModifierDef
            { Memory = Memory, Kind = LinkKind.MemoryHaste, Value = ValueUnits.FromPercent(1m), CapProfileId = profileId };
            var p = Funded();
            var engine = Engine(link, native);
            for (int i = 0; i < 5; i++) Add(p, engine, link.Id);
            var plan = engine.Preview(p, Hero, Purchase(native.Id));
            Assert.False(plan.CanApply);
            Assert.Equal(100000m, plan.NewEffectiveChannels.Single(c => c.Key.StartsWith("haste:" + Memory + ":", StringComparison.Ordinal)).ValueMilli);
            Assert.False(p.Hero(Hero).Talents.ContainsKey(native.Id));
        }

        [Fact]
        public void Headroom_evaluates_unallocated_ranked_variants_and_reports_exact_first_inert_rank_without_mutation()
        {
            var sap = Effect("test.headroom.sap", GimmickEffect.Sap, Gimmicks.Cap(GimmickEffect.Sap) * 0.4m, 4, 2);
            var p = Funded();
            var engine = Engine(sap);
            string before = State(p);
            var row = engine.AnalyzeHeadroom(p, Hero).Single(r => r.CandidateStarId == sap.Id);
            Assert.True(row.Reachable);
            Assert.Equal(3, row.AttainableRanks);
            Assert.Equal(2, row.RankCost);
            Assert.Contains(row.SaturationDetails, d => d.StarId == sap.Id && d.Rank == 4 &&
                d.Ceiling == Gimmicks.Cap(GimmickEffect.Sap) * 1000m);
            Assert.Equal(before, State(p));
        }

        [Fact]
        public void Wound_duration_above_the_lifetime_cap_still_has_a_real_paid_rank_damage_benefit()
        {
            var wound = Effect("test.wound", GimmickEffect.Wound, Gimmicks.Cap(GimmickEffect.Wound));
            wound.Gimmick.DurationUnits = Gimmicks.MaxParameterPercent * 100;
            var duration = Modifier("test.wound.duration", wound.Id, GimmickParam.Duration, 1m);
            var engine = Engine(wound, duration);
            var p = Funded();
            Add(p, engine, wound.Id);
            var plan = engine.Preview(p, Hero, Purchase(duration.Id));
            Assert.True(plan.CanApply);
            var before = plan.OldEffectiveChannels.First(c => c.StarId == wound.Id);
            var after = plan.NewEffectiveChannels.First(c => c.StarId == wound.Id);
            Assert.Equal(before.DurationUnits, after.DurationUnits);
            Assert.Equal(before.ValueMilli / (1m + 1.5m / 500m) * (1m + 1.5m * 2m / 500m), after.ValueMilli);
        }

        [Fact]
        public void Capped_explicit_additive_channel_does_not_gain_when_representative_star_id_changes()
        {
            var existing = Effect("test.channel.z", GimmickEffect.Sap, Gimmicks.Cap(GimmickEffect.Sap));
            var candidate = Effect("test.channel.a", GimmickEffect.Sap, 1m);
            foreach (var talent in new[] { existing, candidate }) talent.EffectChannel = new EffectChannelDef
            { ChannelId = "test.c15.shared.sap", SourceMemory = Memory, ReceiverMemory = Memory };
            var engine = Engine(existing, candidate);
            var p = Funded();
            Add(p, engine, existing.Id);
            var plan = engine.Preview(p, Hero, Purchase(candidate.Id));
            Assert.False(plan.CanApply);
            Assert.Contains(plan.SaturationDetails, d => d.StarId == candidate.Id &&
                d.Ceiling == Gimmicks.Cap(GimmickEffect.Sap) * 1000m);
            Assert.False(p.Hero(Hero).Talents.ContainsKey(candidate.Id));
        }


        [Fact]
        public void A_locally_dominated_wound_still_ranks_the_mandatory_stronger_wound()
        {
            var strong = Effect("test.budget.strong", GimmickEffect.Wound, 100m);
            strong.Gimmick.DurationUnits = 2000;
            var weak = Requires(Effect("test.budget.weak", GimmickEffect.Wound, 60m), strong.Id);
            weak.Gimmick.DurationUnits = 10000;
            var engine = Engine(strong, weak);
            var p = Funded();
            Add(p, engine, strong.Id);
            var plan = engine.Preview(p, Hero, Purchase(weak.Id));
            Assert.True(plan.CanApply);
            var before = plan.OldEffectiveChannels.First(c => c.StarId == strong.Id);
            var after = plan.NewEffectiveChannels.First(c => c.StarId == strong.Id);
            Assert.Equal(before.ValueMilli / (1m + 1.5m / 500m) * (1m + 1.5m * 2m / 500m), after.ValueMilli);
            engine.Commit(p, plan);
            Assert.Equal(1, p.Hero(Hero).Talents[weak.Id]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Unrelated_change_does_not_migrate_preexisting_dormant_or_already_inert_saved_ranks(bool connected)
        {
            var root = StatStar("test.dormant.root");
            var dormant = Requires(Effect("test.dormant.saved", GimmickEffect.Sap,
                Gimmicks.Cap(GimmickEffect.Sap) * 0.6m, 3, 3), root.Id);
            var unrelated = StatStar("test.dormant.other");
            var engine = Engine(root, dormant, unrelated);
            var p = Funded();
            if (connected) Add(p, engine, root.Id);
            p.Hero(Hero).Talents[dormant.Id] = 3;
            var plan = engine.Preview(p, Hero, Purchase(unrelated.Id));
            Assert.Empty(plan.AffectedRefundIds);
            engine.Commit(p, plan);
            Assert.Equal(3, p.Hero(Hero).Talents[dormant.Id]);
            Assert.Equal(1, p.Hero(Hero).Talents[unrelated.Id]);
            if (!connected) Add(p, engine, root.Id);
            Assert.Equal(3, p.Hero(Hero).Talents[dormant.Id]);
            Assert.Equal(Gimmicks.Cap(GimmickEffect.Sap), Build.ComputeForTree(p, Hero, 0, Tree(root, dormant, unrelated)).Gimmicks
                .First(e => e.StarId == dormant.Id).Def.Value);
        }

        [Fact]
        public void Declared_scoped_boost_cap_allows_partial_rank_then_rejects_saturation_with_exact_profile_units()
        {
            const string profileId = "test.c15.scoped.gb";
            FractionalScopedModifiers.RegisterScopedCapProfile(new ScopedModifierCapProfile
            { Id = profileId, MaximumModifier = ModifierUnits.FromPercent(10m) });
            var shield = Effect("test.scoped.cap.shield", GimmickEffect.Shield, 1m);
            var boost = Modifier("test.scoped.cap.boost", shield.Id, null, 6m, 3, 2);
            boost.ScopedModifier.CapProfileId = profileId;
            var tree = Tree(shield, boost);
            var engine = new EffectiveAllocationValidation(tree);
            var p = Funded();
            Add(p, engine, shield.Id);
            Add(p, engine, boost.Id);
            Assert.Equal(1.06m, Build.ComputeForTree(p, Hero, 0, tree).Gimmicks.First(e => e.StarId == shield.Id).Def.Value);
            Add(p, engine, boost.Id);
            Assert.Equal(1.10m, Build.ComputeForTree(p, Hero, 0, tree).Gimmicks.First(e => e.StarId == shield.Id).Def.Value);
            string before = State(p);
            var plan = engine.Preview(p, Hero, Purchase(boost.Id));
            Assert.False(plan.CanApply);
            Assert.Contains(plan.SaturationDetails, d => d.StarId == boost.Id && d.Rank == 3 &&
                d.CapProfileId == profileId && d.DeclaredModifierCeilingUnits == 1000 &&
                d.DeclaredModifierUnit == AllocationValueUnit.Hundredths);
            Assert.NotNull(Record.Exception(() => engine.Commit(p, plan)));
            Assert.Equal(before, State(p));
            Assert.Equal(2, p.Hero(Hero).Talents[boost.Id]);
        }
    }
}
