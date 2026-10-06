using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class MemoryGimmicksV128Tests
    {
        private const string Memory = "St_Q_Fleche";
        private const string Hero = "Hero_Mist";

        private static GimmickEntry Entry(string star = "h.mist.test", GimmickEffect effect = GimmickEffect.Echo,
            int value = 25, float cooldown = 0, GimmickTrigger trigger = GimmickTrigger.OnHit) =>
            new GimmickEntry { StarId = star, Memory = Memory, Def = new GimmickDef
                { Trigger = trigger, Effect = effect, Value = value, Cooldown = cooldown } };

        [Theory]
        [InlineData(0, 3, true, 1, false)]
        [InlineData(1, 3, true, 2, false)]
        [InlineData(2, 3, true, 3, false)]
        [InlineData(3, 3, false, 3, false)]
        [InlineData(0, 1, true, 0, true)]
        [InlineData(1, 1, true, 1, true)]
        [InlineData(-1, 3, false, -1, false)]
        [InlineData(4, 3, false, 4, false)]
        [InlineData(0, 0, false, 0, false)]
        [InlineData(0, -1, false, 0, false)]
        public void Reload_restores_one_charge_without_overflow_or_uses_single_charge_reset(
            int current, int maximum, bool applies, int expected, bool reset)
        {
            Assert.Equal(applies, Gimmicks.TryReload(current, maximum, out int next, out bool resetCooldown));
            Assert.Equal(expected, next);
            Assert.Equal(reset, resetCooldown);
        }

        [Theory]
        [InlineData(4f, 10f, 25, 0.1f)]
        [InlineData(4f, 10f, 100, 0.4f)]
        [InlineData(4f, 10f, 200, 0.4f)]
        [InlineData(12f, 10f, 50, 0.6f)]
        [InlineData(0f, 10f, 25, 0f)]
        [InlineData(-1f, 10f, 25, 0f)]
        [InlineData(4f, 0f, 25, 0f)]
        [InlineData(4f, -1f, 25, 0f)]
        [InlineData(4f, 10f, 0, 0f)]
        [InlineData(4f, 10f, -1, 0f)]
        [InlineData(float.NaN, 10f, 25, 0f)]
        [InlineData(float.PositiveInfinity, 10f, 25, 0f)]
        [InlineData(4f, float.NaN, 25, 0f)]
        [InlineData(4f, float.PositiveInfinity, 25, 0f)]
        public void Remaining_cooldown_reduction_converts_to_native_maximum_based_ratio(
            float remaining, float maximum, int percent, float expected)
        {
            Assert.Equal(expected, Gimmicks.RemainingCooldownReductionRatio(remaining, maximum, percent));
        }

        [Theory]
        [InlineData("St_R_Parry", true, false, true)]
        [InlineData(Memory, true, false, false)]
        [InlineData("St_R_Parry", false, false, false)]
        [InlineData("St_Q_HandCannon", false, false, false)]
        [InlineData("St_D_SalamanderPowder", true, true, false)]
        [InlineData("St_X_Unknown", true, false, false)]
        [InlineData(null, true, false, false)]
        public void RechargeOther_excludes_source_and_non_normal_or_identity_categories_not_R_slots(
            string target, bool isNormalSkill, bool isIdentity, bool eligible)
        {
            Assert.Equal(eligible, Gimmicks.CanRechargeOther(Memory, target, isNormalSkill, isIdentity));
            Assert.False(Gimmicks.CanRechargeOther("St_X_Unknown", target, isNormalSkill, isIdentity));
        }





        [Fact]
        public void Star_identifier_boundary_is_preserved_and_invalid_local_entries_are_rejected()
        {
            var build = new Build();
            foreach (var invalid in new[] { null, Entry(new string('a', Gimmicks.MaxStarIdLength + 1)), Entry("bad+star") })
            {
                build.Gimmicks.Add(invalid);
                Assert.Throws<InvalidOperationException>(() => build.Encode());
                build.Gimmicks.Clear();
            }
            build.Gimmicks.Add(Entry(new string('a', Gimmicks.MaxStarIdLength)));
            var valid = Assert.Single(Build.Decode(build.Encode()).Gimmicks);
            Assert.Equal(new string('a', Gimmicks.MaxStarIdLength), valid.StarId);
        }

        [Fact]
        public void Cooldown_is_per_star_not_per_victim_and_generated_damage_cannot_chain()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry("h.mist.a", cooldown: 1), Entry("h.mist.b", cooldown: 2) });
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 80, false, requests);
            Assert.Equal(new[] { "h.mist.a", "h.mist.b" }, requests.Select(r => r.Entry.StarId));
            Assert.All(requests, r => { Assert.Equal(11, r.VictimId); Assert.Equal(80f, r.Damage); });
            requests.Clear();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0.5f, 22, 40, false, requests);
            Assert.Empty(requests);
            runtime.Fire(GimmickTrigger.OnHit, Memory, 1, 22, 40, true, requests);
            Assert.Empty(requests);
            runtime.Fire(GimmickTrigger.OnHit, "St_R_Parry", 1, 22, 40, false, requests);
            runtime.Fire(GimmickTrigger.OnCrit, Memory, 1, 22, 40, false, requests);
            Assert.Empty(requests);
            runtime.Fire(GimmickTrigger.OnHit, Memory, 1, 22, 40, false, requests);
            Assert.Equal("h.mist.a", Assert.Single(requests).Entry.StarId);
            requests.Clear();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 2, 33, 60, false, requests);
            Assert.Equal(2, requests.Count);
        }


        [Fact]
        public void Negative_entity_ids_are_valid_but_missing_targets_do_not_consume_cooldown()
        {
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { Entry(effect: GimmickEffect.Expose, value: 15, cooldown: 1) });
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 0, 10, false, requests);
            Assert.Empty(requests);
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, -42, 10, false, requests);
            Assert.Equal(-42, Assert.Single(requests).VictimId);
            Assert.Equal(15, runtime.ExposePercent(-42, 3.99f));
            Assert.Equal(0, runtime.ExposePercent(-42, 4f));
        }

        [Fact]
        public void Build_round_trip_uses_invariant_numbers_and_copies_sanitized_entries()
        {
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var build = new Build();
                var original = Entry(cooldown: 0.75f);
                build.Gimmicks.Add(original);
                var decoded = Build.Decode(build.Encode());
                var entry = Assert.Single(decoded.Gimmicks);
                Assert.Equal(original.StarId, entry.StarId);
                Assert.Equal(Memory, entry.Memory);
                Assert.Equal(GimmickTrigger.OnHit, entry.Def.Trigger);
                Assert.Equal(GimmickEffect.Echo, entry.Def.Effect);
                Assert.Equal(25, entry.Def.Value);
                Assert.Equal(0, entry.Def.Arg);
                Assert.Equal(0.75f, entry.Def.Cooldown);
                original.Def.Value = 99;
                Assert.Equal(25, entry.Def.Value);
                Assert.Empty(Build.Decode("s:;p:;h:0").Gimmicks);
            }
            finally { CultureInfo.CurrentCulture = oldCulture; }
        }

        [Theory]
        [InlineData("h.test:St_Q_Fleche:99:9:25000:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:0:9:25000:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:99:25000:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:0:25000:0:1:0:0:0:0")]
        [InlineData("h.test:St_X_Unknown:2:9:25000:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:1:25000:4:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:4:25000:2:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:25000:1:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:0:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:-5000:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:25000:0:NaN:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:25000:0:Infinity:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:25000:0:-1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:10:1000:1:0:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:10:0:0:0:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:11:25000:1:0:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:11:-1000:0:0:0:0:0:0")]
        [InlineData("h bad:St_Q_Fleche:2:9:25000:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:abc:0:1:0:0:0:0")]
        [InlineData("h.test:St_Q_Fleche:2:9:25")]
        public void Invalid_gimmicks_reject_the_whole_build_without_partial_application(string invalid)
        {
            var decoded = Build.Decode("s:0=7;p:;h:2;g:" + invalid + ",h.valid:St_Q_Fleche:2:9:15000:0:0:0:0:0:0");
            Assert.Null(decoded);
        }

        [Theory]
        [InlineData(GimmickEffect.Element, 600)]
        [InlineData(GimmickEffect.Burst, 1000)]
        [InlineData(GimmickEffect.Shield, 100)]
        [InlineData(GimmickEffect.Heal, 100)]
        [InlineData(GimmickEffect.Recharge, 100)]
        [InlineData(GimmickEffect.Quicken, 100)]
        [InlineData(GimmickEffect.Empower, 200)]
        [InlineData(GimmickEffect.Expose, 100)]
        [InlineData(GimmickEffect.Echo, 1000)]
        [InlineData(GimmickEffect.Reload, 1)]
        [InlineData(GimmickEffect.RechargeOther, 100)]
        public void Wire_caps_value_and_cooldown_by_effect(GimmickEffect effect, int cap)
        {
            var decoded = Build.Decode("g:h.test:St_Q_Fleche:2:" + (int)effect + ":2147483647:0:999:0:0:0:0");
            var entry = Assert.Single(decoded.Gimmicks);
            Assert.Equal(cap, entry.Def.Value);
            Assert.Equal(60f, entry.Def.Cooldown);
        }

        [Fact]
        public void Duplicate_stars_across_sections_are_not_applied_twice()
        {
            Assert.Null(Build.Decode("g:h.test.0:St_Q_Fleche:2:9:15000:0:0:0:0:0:0;g:h.test.0:St_Q_Fleche:2:9:25000:0:0:0:0:0:0"));
            Assert.Null(Build.Decode("g:h.test.0:St_Q_Fleche:2:9:15000:0:0:0:0:0:0,h.test.0:St_Q_Fleche:2:9:25000:0:0:0:0:0:0"));
            var build = new Build();
            build.Gimmicks.Add(Entry(value: 15));
            build.Gimmicks.Add(Entry(value: 35));
            Assert.Throws<InvalidOperationException>(() => build.Encode());
        }

        [Theory]
        [InlineData(GimmickEffect.Echo, 12, 36)]
        [InlineData(GimmickEffect.Reload, 1, 1)]
        [InlineData(GimmickEffect.RechargeOther, 40, 100)]
        public void Compute_collects_scaled_and_capped_unlocked_gimmicks_independently_of_link_path(
            GimmickEffect effect, int perRank, int expectedValue)
        {
            var star = HeroSigils.TreeFor(Hero).First(t => t.RouteMemory == Memory && t.RouteOrder == 4 && t.MaxRank >= 3 && !t.IsKeystone);
            var oldGimmick = star.Gimmick;
            var oldLink = star.LinkPerRank;
            try
            {
                star.Gimmick = Entry(effect: effect, value: perRank).Def;
                star.LinkPerRank = new LinkDef { Kind = LinkKind.MemoryDamage, Value = 5, Requires = new[] { Memory } };
                var p = Profile.CreateNew(128);
                p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(150);
                TreeTestPaths.Connect(p, Hero, star.Id);
                p.Hero(Hero).Talents[star.Id] = 3;
                var build = Build.Compute(p, Hero, 0);
                var entry = Assert.Single(build.Gimmicks, e => e.StarId == star.Id);
                Assert.Equal(Memory, entry.Memory);
                Assert.Equal(expectedValue, entry.Def.Value);
                decimal rankedLink = decimal.Round(15m * (1m + 1.5m * build.SpentStarPoints / 500), 3, MidpointRounding.AwayFromZero);
                Assert.Contains(build.Links, l => l.Kind == LinkKind.MemoryDamage && l.Value == rankedLink);
                Assert.Equal(perRank, star.Gimmick.Value);
                Assert.DoesNotContain(Build.Compute(p, "Hero_Cetus", 0).Gimmicks, e => e.StarId == star.Id);
                Rules.ResetTalents(p, Hero);
                p.Hero(Hero).Talents[star.Id] = 3;
                Assert.DoesNotContain(Build.Compute(p, Hero, 0).Gimmicks, e => e.StarId == star.Id);
            }
            finally { star.Gimmick = oldGimmick; star.LinkPerRank = oldLink; }
        }

        [Theory]
        [InlineData(1, 40, 72)]
        [InlineData(2, 64, 115.2)]
        [InlineData(3, 88, 158.4)]
        public void Memory_damage_source_caps_and_aggregate_wire_envelope_are_distinct(int count, int baseCap, double awakenedCap)
        {
            var targets = new[] { Memory, "St_R_Parry", "St_Q_HandCannon" }.Take(count).ToArray();
            Assert.Equal(baseCap, Links.Cap(LinkKind.MemoryDamage, count));
            Assert.Equal((decimal)awakenedCap, Links.EquippedCap(LinkKind.MemoryDamage, count));
            int maximum = BuildLimits.MaxLinkValueMilli(LinkKind.MemoryDamage, count);
            var link = Assert.Single(Build.Decode("l:5:" + maximum + ":" + string.Join("+", targets)).Links);
            Assert.Equal(maximum, link.ValueMilli);
            Assert.Null(Build.Decode("l:5:" + (maximum + 1) + ":" + string.Join("+", targets)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Memory_damage_caps_base_before_awakening_and_round_trips(int requirements)
        {
            var unique = Content.Uniques.First(u => u.Link == null);
            var oldLink = unique.Link;
            try
            {
                decimal baseCap = Links.Cap(LinkKind.MemoryDamage, requirements);
                unique.Link = new LinkDef { Kind = LinkKind.MemoryDamage, Value = baseCap,
                    Requires = new[] { Memory, "St_R_Parry", "St_Q_HandCannon" }.Take(requirements).ToArray() };
                var relic = Loot.RollUnique(new Rng(128), unique, 1);
                var p = Profile.CreateNew(128);
                p.Stash.Add(relic);
                Rules.Equip(p, Hero, relic.Uid);
                for (int level = 0; level <= Content.MaxAwakenLevel; level++)
                {
                    relic.AwakenLevel = level;
                    var link = Assert.Single(Build.Compute(p, Hero, 0).Links);
                    Assert.Equal(baseCap * Content.AwakenPowerPctAt(level) / 100m, link.Value);
                    var build = new Build();
                    build.Links.Add(link);
                    Assert.Equal(link.Value, Assert.Single(Build.Decode(build.Encode()).Links).Value);
                }
                unique.Link.ValueMilli = int.MaxValue;
                Assert.Equal(Links.EquippedCap(LinkKind.MemoryDamage, requirements) * 1.8m, Assert.Single(Build.Compute(p, Hero, 0).Links).Value);
            }
            finally { unique.Link = oldLink; }
        }
    }
}
