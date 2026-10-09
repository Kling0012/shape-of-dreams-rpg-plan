using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class StarDamageScalingTests
    {
        private const string Hero = "Hero_Cetus", Memory = "St_D_IcyVeins";
        private static TalentDef Effect(string id, GimmickEffect effect, decimal value) =>
            new TalentDef(id, Line.Offense, new Txt("試験", "Test"), Stat.Armor, 0, 1)
            { HeroKey = Hero, RouteMemory = Memory,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value } };

        [Fact]
        public void Maximum_paid_rank_caps_star_sources_before_scaling_and_preserves_equipment_and_non_damage()
        {
            const string capId = "test.rank.native";
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile
                { Id = capId, Kind = LinkKind.MemoryDamage, Maximum = ValueUnits.FromPercent(120.05m) });
            var damage = new TalentDef("test.rank.power", Line.Offense, new Txt("試験", "Test"), Power.ShieldBash,
                Content.PowerCap(Power.ShieldBash) * 2, 1) { HeroKey = Hero };
            var stance = new TalentDef("test.rank.stance", Line.Offense, new Txt("試験", "Test"), Power.ImmovableStance,
                Content.PowerCap(Power.ImmovableStance), 1) { HeroKey = Hero };
            var link = new TalentDef("test.rank.link", Line.Offense, new Txt("試験", "Test"),
                new LinkDef { Kind = LinkKind.MemoryDamage, Value = 30.01m, Requires = new[] { Memory } }, 1) { HeroKey = Hero };
            var native = new TalentDef("test.rank.native", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 0, 1)
                { HeroKey = Hero, RouteMemory = Memory, NativeModifier = new NativeMemoryModifierDef
                    { Memory = Memory, Kind = LinkKind.MemoryDamage, CapProfileId = capId, Value = ValueUnits.FromPercent(150m) } };
            var burst = Effect("test.rank.burst", GimmickEffect.Burst, 2000m);
            var element = Effect("test.rank.element", GimmickEffect.Element, 250m);
            var quicken = Effect("test.rank.quicken", GimmickEffect.Quicken, 25m);
            var filler = new TalentDef("test.rank.filler", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1)
                { HeroKey = Hero, RankCost = 497 };
            var selected = new[] { damage, stance, link, native, burst, element, quicken, filler };
            var tree = HeroSigils.TreeFor(Hero).Where(t => t.Cluster == null).Concat(selected).ToArray();
            var profile = Profile.CreateNew(117);
            var hero = profile.Hero(Hero);
            foreach (var talent in selected) hero.Talents.Add(talent.Id, 1);
            var relic = new Relic { Uid = "rank.equipment", BaseId = Content.Bases.First(b => b.Slot == Slot.Weapon).Id,
                Rarity = Rarity.Rare, ItemLevel = 1 };
            relic.Powers.Add(new PowerLine(Power.ShieldBash, 20));
            profile.Stash.Add(relic); hero.Equipped[(int)Slot.Weapon] = relic.Uid;
            var build = Build.ComputeForTree(profile, Hero, 0, tree);
            Assert.Equal(504, build.SpentStarPoints);
            Assert.Equal(20 + (int)(Content.PowerCap(Power.ShieldBash) * 2.512m), build.Get(Power.ShieldBash));
            Assert.Equal(75.385m, Assert.Single(build.Links).Value);
            Assert.Equal(301566, Assert.Single(build.NativeModifiers).ValueMilli);
            Assert.Equal(2512m, build.Gimmicks.Single(e => e.StarId == burst.Id).Def.EffectiveValueOrAuthored);
            var powers = new PowerRuntime(build, 0) { ShieldAmount = 100f };
            Assert.Equal((float)build.Get(Power.ShieldBash), powers.OnNewBasicHit(0, 19, 100, 100, false).DirectDamage);
            powers.ObserveMovement(0, false, 0);
            Assert.Equal(.1f, powers.IncomingDamageReduction(2));
            Assert.Equal(build.Get(Power.ImmovableStance) / 100f, powers.OutgoingDamageAmplification(2, false, false));
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(build.Encode(), decoded.Encode());
            Assert.Equal(build.Get(Power.ShieldBash), decoded.Get(Power.ShieldBash));
            var runtime = new GimmickRuntime(); runtime.SetBuild(decoded.Gimmicks);
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 19, 100, false, requests);
            Assert.Equal(2512m, requests.Single(r => r.Entry.StarId == burst.Id).Entry.Def.EffectiveValueOrAuthored);
            Assert.Equal(25f, runtime.QuickenPercent(0));
            var packedElement = requests.Single(r => r.Entry.StarId == element.Id).Entry.Def;
            Assert.Equal(3, Gimmicks.ElementStacks(packedElement, .49));
            Assert.Equal(2, Gimmicks.ElementStacks(packedElement, .51));
            Assert.True(HostBuildValidation.TryValidateAllocation(hero, Hero, new EffectiveAllocationValidation(tree),
                out int spent, out string reason), reason);
            Assert.Equal(504, spent);
        }

        [Fact]
        public void Crescendo_never_recaps_the_star_layer_and_keeps_its_own_stack_window()
        {
            var entry = new GimmickEntry { StarId = "test.rank.crescendo", Memory = Memory,
                Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Crescendo, Value = 8m } };
            var runtime = new GimmickRuntime(); runtime.SetBuild(new[] { entry });
            var requests = new List<GimmickRequest>();
            for (int i = 1; i <= 6; i++)
                runtime.Fire(GimmickTrigger.OnHit, Memory, i, 1, 100, false, requests, activationId: i, memoryCooldown: 20f);
            Assert.Equal(40f, runtime.CrescendoPercent(Memory, 6));
            Assert.Equal(575f, runtime.CombinedMemoryDamagePercent(Memory, 6, 555f, 455f));
            Assert.Equal(555f, runtime.CombinedMemoryDamagePercent(Memory, 40, 555f, 455f));
        }

        [Fact]
        public void Authored_value_precedes_rank_while_native_baseline_and_timing_remain_unchanged()
        {
            var build = new Build { SpentStarPoints = 504 };
            var echo = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo,
                Value = 1100m, ExtraTargets = 0, ChanceUnits = 3700 };
            var payload = AuthoredKeystoneComposer.GimmickPayload(echo, everyN: 3);
            var result = AuthoredKeystoneComposer.TransformAllocationPayload(build, payload, Memory);
            Assert.Equal(2763.2m, result.Value);
            Assert.Equal(3, result.EveryN);
            Assert.Equal(37m, result.ProbabilityPercent);
            var key = new KeystoneDefinition("test.rank.key", new[] { Memory },
                new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value,
                    KeystoneMagnitude.FromPercent(30m), new KeystoneScope(targetMemorySet: new[] { Memory })) }, cost: 1);
            build.SelectedKeystone = key;
            var native = AuthoredKeystoneComposer.TransformAllocationPayload(build,
                new KeystonePayload(KeystoneLayer.NativeDamage, 1m, new KeystoneCaps(10m)), Memory);
            Assert.Equal(1.7536m, native.Value);
        }

        [Fact]
        public void Unrelated_paid_point_updates_authored_identity_damage_in_allocation_projection()
        {
            const string heroKey = "Hero_Husk";
            var strike = IdentityStrikeTests.WindStrike("test.rank.identity");
            var source = new TalentDef("test.rank.identity.star", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 0, 1)
                { HeroKey = heroKey, Mechanism = IdentityStrikeTests.Spec(strike) };
            var paid = new TalentDef("test.rank.identity.paid", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1)
                { HeroKey = heroKey, RankCost = 498 };
            var added = new TalentDef("test.rank.identity.added", Line.Offense, new Txt("試験", "Test"), Stat.MaxHealthFlat, 1, 1)
                { HeroKey = heroKey };
            var tree = HeroSigils.TreeFor(heroKey).Where(t => t.Cluster == null).Concat(new[] { source, paid, added }).ToArray();
            var profile = Profile.CreateNew(118);
            var hero = profile.Hero(heroKey);
            hero.StarXp = StarProgression.TotalXpForPoints(500);
            hero.Talents.Add(source.Id, 1); hero.Talents.Add(paid.Id, 1);
            var engine = new EffectiveAllocationValidation(tree);
            var plan = engine.Preview(profile, heroKey, new AllocationChange
                { Kind = AllocationChangeKind.Purchase, CandidateStarId = added.Id });
            Assert.True(plan.CanApply);
            const decimal coefficientMilli = 60m * BuildPrecision.Scale;
            Assert.Equal(coefficientMilli * (1m + 1.5m * 499m / 500m),
                Assert.Single(plan.OldEffectiveChannels, c => c.StarId == source.Id).ValueMilli);
            Assert.Equal(coefficientMilli * (1m + 1.5m * 500m / 500m),
                Assert.Single(plan.NewEffectiveChannels, c => c.StarId == source.Id).ValueMilli);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_locally_capped_point_requires_a_real_quantized_damage_gain(bool milliLink)
        {
            var source = milliLink
                ? new TalentDef("test.rank.quantized", Line.Offense, new Txt("試験", "Test"),
                    new LinkDef { Kind = LinkKind.MemoryDamage, Value = .001m, Requires = new[] { Memory } }, 1)
                : new TalentDef("test.rank.quantized", Line.Offense, new Txt("試験", "Test"), Power.ShieldBash, 1, 1);
            source.HeroKey = Hero;
            var capped = new TalentDef("test.rank.capped", Line.Offense, new Txt("試験", "Test"), Stat.Armor,
                Content.StatCap(Stat.Armor), 1) { HeroKey = Hero, RankCost = 498 };
            var added = new TalentDef("test.rank.capped.added", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1)
                { HeroKey = Hero };
            var tree = HeroSigils.TreeFor(Hero).Where(t => t.Cluster == null).Concat(new[] { source, capped, added }).ToArray();
            var profile = Profile.CreateNew(119);
            var hero = profile.Hero(Hero);
            hero.StarXp = StarProgression.TotalXpForPoints(500);
            hero.Talents.Add(source.Id, 1); hero.Talents.Add(capped.Id, 1);
            var engine = new EffectiveAllocationValidation(tree);
            var plan = engine.Preview(profile, Hero, new AllocationChange
                { Kind = AllocationChangeKind.Purchase, CandidateStarId = added.Id });
            Assert.Equal(milliLink, plan.CanApply);
            if (milliLink)
            {
                Assert.Equal(2m, plan.OldEffectiveChannels.Single(c => c.Key.StartsWith("link:", StringComparison.Ordinal)).ValueMilli);
                Assert.Equal(3m, plan.NewEffectiveChannels.Single(c => c.Key.StartsWith("link:", StringComparison.Ordinal)).ValueMilli);
                engine.Commit(profile, plan);
                var refund = engine.Preview(profile, Hero, new AllocationChange
                    { Kind = AllocationChangeKind.Refund, CandidateStarId = added.Id });
                Assert.Equal(3m, refund.OldEffectiveChannels.Single(c => c.Key.StartsWith("link:", StringComparison.Ordinal)).ValueMilli);
                Assert.Equal(2m, refund.NewEffectiveChannels.Single(c => c.Key.StartsWith("link:", StringComparison.Ordinal)).ValueMilli);
            }
            else
            {
                Assert.Equal(2m, plan.OldEffectiveChannels.Single(c => c.Key == "power:" + (int)Power.ShieldBash).ValueMilli);
                Assert.Equal(2m, plan.NewEffectiveChannels.Single(c => c.Key == "power:" + (int)Power.ShieldBash).ValueMilli);
            }
        }

        [Fact]
        public void A_paid_point_ranks_a_native_keystone_uplift_even_without_other_star_damage()
        {
            var paid = new TalentDef("test.rank.native.paid", Line.Offense, new Txt("試験", "Test"), Stat.Armor,
                Content.StatCap(Stat.Armor) / Content.KeystoneRouteRequirement, Content.KeystoneRouteRequirement)
                { HeroKey = Hero, RankCost = 83 };
            var key = new TalentDef("test.rank.native.key", Line.Offense, new Txt("試験", "Test"), Power.None, 0, new Txt("試験", "Test"))
                { HeroKey = Hero, KeystoneDefinition = new KeystoneDefinition("test.rank.native.key", new[] { Memory },
                    new[] { KeystoneTransform.Scale(KeystoneLayer.NativeDamage, KeystoneField.Value,
                        KeystoneMagnitude.FromPercent(30m), new KeystoneScope(targetMemorySet: new[] { Memory })) }, cost: 1) };
            var added = new TalentDef("test.rank.native.added", Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1)
                { HeroKey = Hero };
            var tree = HeroSigils.TreeFor(Hero).Where(t => t.Cluster == null).Concat(new[] { paid, key, added }).ToArray();
            var profile = Profile.CreateNew(120);
            var hero = profile.Hero(Hero);
            hero.StarXp = StarProgression.TotalXpForPoints(500);
            hero.Kills = 1000000;
            hero.Talents.Add(paid.Id, Content.KeystoneRouteRequirement);
            hero.AddKeystone(key.Id);
            var engine = new EffectiveAllocationValidation(tree);
            var plan = engine.Preview(profile, Hero, new AllocationChange
                { Kind = AllocationChangeKind.Purchase, CandidateStarId = added.Id });
            Assert.True(plan.CanApply);
            Assert.Equal(499, engine.SpentPoints(plan.Original));
            Assert.Equal(500, engine.SpentPoints(plan.Proposed));
            Assert.Equal(.3m * (1m + 1.5m * 499m / 500m) * BuildPrecision.Scale,
                plan.OldEffectiveChannels.First(c => c.Key.StartsWith("native-uplift:" + Memory + "@", StringComparison.Ordinal)).ValueMilli);
            Assert.Equal(.3m * (1m + 1.5m * 500m / 500m) * BuildPrecision.Scale,
                plan.NewEffectiveChannels.First(c => c.Key.StartsWith("native-uplift:" + Memory + "@", StringComparison.Ordinal)).ValueMilli);
            Assert.Equal(plan.OldEffectiveChannels.Single(c => c.Key == "stat:" + (int)Stat.Armor).ValueMilli,
                plan.NewEffectiveChannels.Single(c => c.Key == "stat:" + (int)Stat.Armor).ValueMilli);
            engine.Commit(profile, plan);
            var refund = engine.Preview(profile, Hero, new AllocationChange
                { Kind = AllocationChangeKind.Refund, CandidateStarId = added.Id });
            Assert.Equal(.3m * (1m + 1.5m * 499m / 500m) * BuildPrecision.Scale,
                refund.NewEffectiveChannels.First(c => c.Key.StartsWith("native-uplift:" + Memory + "@", StringComparison.Ordinal)).ValueMilli);
        }
    }
}
