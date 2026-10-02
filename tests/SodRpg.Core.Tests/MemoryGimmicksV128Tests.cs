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
        [InlineData(true)]
        [InlineData(false)]
        public void Descriptions_show_scaled_probability_caps_and_shared_nonstacking_windows(bool japanese)
        {
            bool previous = Loc.Japanese;
            try
            {
                Loc.Japanese = japanese;
                var element = Entry(effect: GimmickEffect.Element, value: 80, cooldown: 0.75f).Def;
                string description = Gimmicks.Describe(element, Memory, 2);
                Assert.Contains("60%", description);
                Assert.DoesNotContain("160%", description);
                Assert.Contains("0.75", description);
                Assert.Contains(japanese ? "1つ" : "1 stack", description);
                Assert.Contains(japanese ? "確率" : "chance", description);
                Assert.Contains(japanese ? "星全体" : "per star", description);
                Assert.Contains(japanese ? Links.Name(Memory).Ja : Links.Name(Memory).En, description);
                Assert.Contains(japanese ? "1つ（さらに60%の確率でもう1つ）" : "plus a 60% chance of 1 more", description);
                var fractional = Entry(effect: GimmickEffect.Element, value: 60).Def;
                Assert.Contains(japanese ? "60%の確率で1つ" : "1 stack with a 60% chance", Gimmicks.Describe(fractional, Memory));
                Assert.Contains(japanese ? "間隔制限なし" : "no cooldown", Gimmicks.Describe(fractional, Memory));
                Assert.Equal(Gimmicks.Describe(element, Memory, 8),
                    Gimmicks.Describe(element, Memory, int.MaxValue));
                foreach (var effect in new[] { GimmickEffect.Quicken, GimmickEffect.Empower, GimmickEffect.Expose })
                {
                    var def = Entry(effect: effect, value: 8, cooldown: 1).Def;
                    var runtime = new GimmickRuntime();
                    runtime.SetBuild(new[] { Entry(effect: effect, value: 24, cooldown: 1) });
                    runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 10, false, new List<GimmickRequest>());
                    int effective = effect == GimmickEffect.Quicken ? runtime.QuickenPercent(0)
                        : effect == GimmickEffect.Empower ? runtime.EmpowerPercent(0) : runtime.ExposePercent(11, 0);
                    string text = Gimmicks.Describe(def, Memory, 3);
                    Assert.Contains(effective + "%", text);
                    Assert.Contains(japanese ? "4秒" : "4 seconds", text);
                    Assert.Contains(japanese ? "最大値" : "strongest", text);
                    Assert.Contains(japanese ? "延長" : "refresh", text);
                    Assert.Contains(japanese ? "重ならず" : "without stacking", text);
                }
            }
            finally { Loc.Japanese = previous; }
        }

        [Fact]
        public void Star_identifier_boundary_and_invalid_local_entries_are_sanitized()
        {
            var build = new Build();
            build.Gimmicks.Add(null);
            build.Gimmicks.Add(Entry(new string('a', Gimmicks.MaxStarIdLength + 1)));
            build.Gimmicks.Add(Entry("bad+star"));
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

        [Theory]
        [InlineData(GimmickEffect.Quicken)]
        [InlineData(GimmickEffect.Empower)]
        [InlineData(GimmickEffect.Expose)]
        public void Buffs_use_strongest_active_star_refresh_and_expire_independently(GimmickEffect effect)
        {
            var strong = Entry("h.mist.strong", effect, 20, trigger: GimmickTrigger.OnHit);
            var weak = Entry("h.mist.weak", effect, 8, trigger: GimmickTrigger.OnCrit);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(new[] { strong, weak });
            var requests = new List<GimmickRequest>();
            int Percent(float now, int victim = 11) => effect == GimmickEffect.Quicken ? runtime.QuickenPercent(now)
                : effect == GimmickEffect.Empower ? runtime.EmpowerPercent(now) : runtime.ExposePercent(victim, now);
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 11, 10, false, requests);
            runtime.Fire(GimmickTrigger.OnCrit, Memory, 1, 11, 10, false, requests);
            Assert.Equal(20, Percent(1));
            runtime.Fire(GimmickTrigger.OnHit, Memory, 2, 11, 10, true, requests);
            Assert.Equal(8, Percent(4));
            runtime.Fire(GimmickTrigger.OnCrit, Memory, 4, 11, 10, false, requests);
            Assert.Equal(8, Percent(5));
            if (effect == GimmickEffect.Expose)
            {
                Assert.Equal(0, Percent(5, 22));
                runtime.Fire(GimmickTrigger.OnHit, Memory, 5, 22, 10, false, requests);
                Assert.Equal(8, Percent(5, 11));
                Assert.Equal(20, Percent(5, 22));
            }
            runtime.PruneExpired(9);
            Assert.Equal(0, Percent(9));
            Assert.Equal(0, Percent(9, 22));
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
        [InlineData("h.test:St_Q_Fleche:99:9:25:0:1")]
        [InlineData("h.test:St_Q_Fleche:0:9:25:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:99:25:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:0:25:0:1")]
        [InlineData("h.test:St_X_Unknown:2:9:25:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:1:25:4:1")]
        [InlineData("h.test:St_Q_Fleche:2:4:25:2:1")]
        [InlineData("h.test:St_Q_Fleche:2:9:25:1:1")]
        [InlineData("h.test:St_Q_Fleche:2:9:0:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:9:-5:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:9:25:0:NaN")]
        [InlineData("h.test:St_Q_Fleche:2:9:25:0:Infinity")]
        [InlineData("h.test:St_Q_Fleche:2:9:25:0:-1")]
        [InlineData("h bad:St_Q_Fleche:2:9:25:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:9:abc:0:1")]
        [InlineData("h.test:St_Q_Fleche:2:9:25")]
        public void Invalid_gimmicks_are_dropped_without_losing_valid_build_data(string invalid)
        {
            var decoded = Build.Decode("s:0=7;p:;h:2;g:" + invalid + ",h.valid:St_Q_Fleche:2:9:15:0:0");
            Assert.NotNull(decoded);
            Assert.Equal(2, decoded.Heat);
            Assert.Equal(7, decoded.Get((Stat)0));
            Assert.Equal("h.valid", Assert.Single(decoded.Gimmicks).StarId);
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
        public void Wire_caps_value_and_cooldown_by_effect(GimmickEffect effect, int cap)
        {
            var decoded = Build.Decode("g:h.test:St_Q_Fleche:2:" + (int)effect + ":2147483647:0:999");
            var entry = Assert.Single(decoded.Gimmicks);
            Assert.Equal(cap, entry.Def.Value);
            Assert.Equal(60f, entry.Def.Cooldown);
        }

        [Fact]
        public void Duplicate_stars_across_sections_and_excess_entries_are_not_applied_twice()
        {
            var entries = Enumerable.Range(0, Gimmicks.MaxEntries + 4)
                .Select(i => "h.test." + i + ":St_Q_Fleche:2:9:25:0:0");
            var decoded = Build.Decode("g:h.test.0:St_Q_Fleche:2:9:15:0:0;g:" + string.Join(",", entries));
            Assert.Equal(Gimmicks.MaxEntries, decoded.Gimmicks.Count);
            Assert.Equal(15, decoded.Gimmicks[0].Def.Value);
            Assert.Equal(Enumerable.Range(0, Gimmicks.MaxEntries).Select(i => "h.test." + i), decoded.Gimmicks.Select(e => e.StarId));
            var build = new Build();
            build.Gimmicks.Add(Entry(value: 15));
            build.Gimmicks.Add(Entry(value: 35));
            for (int i = 0; i < Gimmicks.MaxEntries + 1; i++) build.Gimmicks.Add(Entry("h.extra." + i));
            var roundTrip = Build.Decode(build.Encode());
            Assert.Equal(Gimmicks.MaxEntries, roundTrip.Gimmicks.Count);
            Assert.Equal(15, roundTrip.Gimmicks[0].Def.Value);
        }

        [Fact]
        public void Compute_collects_scaled_unlocked_gimmicks_independently_of_link_path()
        {
            var star = HeroSigils.TreeFor(Hero).First(t => t.RouteMemory == Memory && t.RouteOrder == 4 && t.MaxRank >= 3 && !t.IsKeystone);
            var oldGimmick = star.Gimmick;
            var oldLink = star.LinkPerRank;
            try
            {
                star.Gimmick = Entry(effect: GimmickEffect.Echo, value: 12).Def;
                star.LinkPerRank = new LinkDef { Kind = LinkKind.MemoryDamage, Value = 5, Requires = new[] { Memory } };
                var p = Profile.CreateNew(128);
                p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(150);
                TreeTestPaths.Connect(p, Hero, star.Id);
                p.Hero(Hero).Talents[star.Id] = 3;
                var build = Build.Compute(p, Hero, 0);
                var entry = Assert.Single(build.Gimmicks, e => e.StarId == star.Id);
                Assert.Equal(Memory, entry.Memory);
                Assert.Equal(36, entry.Def.Value);
                Assert.Contains(build.Links, l => l.Kind == LinkKind.MemoryDamage && l.Value == 15);
                Assert.Equal(12, star.Gimmick.Value);
                Assert.DoesNotContain(Build.Compute(p, "Hero_Cetus", 0).Gimmicks, e => e.StarId == star.Id);
                Rules.ResetTalents(p, Hero);
                p.Hero(Hero).Talents[star.Id] = 3;
                Assert.DoesNotContain(Build.Compute(p, Hero, 0).Gimmicks, e => e.StarId == star.Id);
            }
            finally { star.Gimmick = oldGimmick; star.LinkPerRank = oldLink; }
        }

        [Theory]
        [InlineData(1, 40, 72)]
        [InlineData(2, 64, 115)]
        [InlineData(3, 88, 158)]
        public void Memory_damage_caps_scale_conditions_and_clamp_awakened_wire_values(int count, int baseCap, int awakenedCap)
        {
            var targets = new[] { Memory, "St_R_Parry", "St_Q_HandCannon" }.Take(count).ToArray();
            Assert.Equal(baseCap, Links.Cap(LinkKind.MemoryDamage, count));
            Assert.Equal(awakenedCap, Links.EquippedCap(LinkKind.MemoryDamage, count));
            var link = Assert.Single(Build.Decode("l:5:2147483647:" + string.Join("+", targets)).Links);
            Assert.Equal(awakenedCap, link.Value);
        }

        [Fact]
        public void Memory_damage_awakening_uses_equipped_cap_and_round_trips()
        {
            var unique = Content.Uniques.First(u => u.Link == null);
            var oldLink = unique.Link;
            try
            {
                unique.Link = new LinkDef { Kind = LinkKind.MemoryDamage, Value = 40, Requires = new[] { Memory } };
                var relic = Loot.RollUnique(new Rng(128), unique, 1);
                var p = Profile.CreateNew(128);
                p.Stash.Add(relic);
                Rules.Equip(p, Hero, relic.Uid);
                for (int level = 0; level <= Content.MaxAwakenLevel; level++)
                {
                    relic.AwakenLevel = level;
                    var link = Assert.Single(Build.Compute(p, Hero, 0).Links);
                    Assert.Equal(Math.Min(Links.EquippedCap(LinkKind.MemoryDamage, 1), 40 * Content.AwakenPowerPctAt(level) / 100), link.Value);
                    var build = new Build();
                    build.Links.Add(link);
                    Assert.Equal(link.Value, Assert.Single(Build.Decode(build.Encode()).Links).Value);
                }
                unique.Link.Value = int.MaxValue;
                Assert.Equal(Links.EquippedCap(LinkKind.MemoryDamage, 1), Assert.Single(Build.Compute(p, Hero, 0).Links).Value);
            }
            finally { unique.Link = oldLink; }
        }
    }
}
