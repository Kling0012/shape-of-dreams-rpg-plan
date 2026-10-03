using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BuildLimitsV131Tests
    {
        [Fact]
        public void Native_messages_use_protocol_twelve_for_thousandth_values()
        {
            Assert.Equal(12, SodRpg.Mod.Protocol.Version);
        }

        [Fact]
        public void Registered_tree_maxima_match_independent_first_rank_enumeration()
        {
            var talents = Content.Talents.Concat(HeroSigils.All).ToArray();
            var capacity = BuildLimits.Analyze(talents);
            // Every current output category emits zero or one entry, so choosing the cheapest
            // eligible stars independently computes the same relaxed maximum as the knapsack.
            Assert.Equal(IndependentMaximum(talents, t => t.Gimmick != null && t.PairCombo == null), capacity.GimmickEntries);
            Assert.Equal(IndependentMaximum(talents, t => t.LinkPerRank != null && t.PairCombo == null), capacity.LinkEntries);
            Assert.Equal(IndependentMaximum(talents, t => t.PairCombo != null), capacity.PairComboEntries);
            Assert.Equal(IndependentMaximum(talents, t => t.GimmickBoost != 0 || t.GimmickParameter.HasValue), capacity.ClusterModifierStars);
            Assert.Equal(24, capacity.GimmickEntries);
            Assert.Equal(25, capacity.LinkEntries);
            Assert.Equal(7, capacity.PairComboEntries);
            Assert.Equal(6, capacity.ClusterModifierStars);
            Assert.Equal(56, capacity.TotalEntries);
            Assert.Equal(talents.Length, capacity.TalentCount);
            Assert.Equal(1, capacity.MinimumRankCost);
            Assert.Equal(1, capacity.MaximumGimmicksPerStar);
            Assert.Equal(1, capacity.MaximumLinksPerStar);
            Assert.Equal(capacity.GimmickEntries, BuildLimits.Registered.GimmickEntries);
            Assert.True(capacity.GimmickEntries <= BuildLimits.MaxGimmickEntries);
            Assert.True(capacity.LinkEntries + Content.SlotCount <= BuildLimits.MaxLinkEntries);
            Assert.True(capacity.PairComboEntries <= BuildLimits.MaxPairComboEntries);
            Assert.Equal(StarProgression.MaxSpendablePoints, BuildLimits.MaxGimmickEntries);
            Assert.Equal(StarProgression.MaxSpendablePoints + Content.SlotCount, BuildLimits.MaxLinkEntries);
            Assert.Equal(PairCombos.All.Count, BuildLimits.MaxPairComboEntries);
            Assert.Equal(Enum.GetValues(typeof(Stat)).Length, BuildLimits.MaxStatEntries);
            Assert.Equal(Enum.GetValues(typeof(Power)).Cast<Power>().Count(p => p != Power.None), BuildLimits.MaxPowerEntries);
            Assert.Equal(Enum.GetValues(typeof(Power)).Cast<Power>().Count(NewPowersV129.IsConditionalAttribute), BuildLimits.MaxConditionalPowerEntries);
        }

        [Fact]
        public void Engine_generated_three_hundred_star_tree_has_three_hundred_entry_capacity()
        {
            const string hero = "Hero_Cetus";
            const string memory = "St_D_IcyVeins";
            var anchor = HeroSigils.TreeFor(hero).Single(t => t.Id == "h.cetus.route.icy-veins.4");
            var stars = Enumerable.Range(0, StarProgression.MaxSpendablePoints).Select(i => new ClusterStarDef
            {
                Kind = ClusterStarKind.Notable,
                Name = new Txt("効果", "Effect"),
                Memory = memory,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Expose, Value = 1 },
            }).ToArray();
            var cluster = new StarClusterDef
            {
                Id = "test.capacity.cluster", HeroKey = hero, Anchor = anchor.Id,
                Region = ClusterRegion.Memory(anchor.RouteId), Shape = ClusterShape.Chain, Stars = stars,
            };
            var generated = StarClusters.Generate(new[] { cluster }, new[] { anchor });
            Assert.Equal(StarProgression.MaxSpendablePoints, BuildLimits.Analyze(generated).GimmickEntries);
            Assert.Equal(BuildLimits.MaxGimmickEntries, BuildLimits.Analyze(generated).TotalEntries);
            foreach (var star in stars)
            {
                star.Kind = ClusterStarKind.MemoryDamage;
                star.Gimmick = null;
                star.Amount = 1;
            }
            generated = StarClusters.Generate(new[] { cluster }, new[] { anchor });
            Assert.Equal(StarProgression.MaxSpendablePoints, BuildLimits.Analyze(generated).LinkEntries);
            Assert.Equal(BuildLimits.MaxLinkEntries, BuildLimits.Analyze(generated).LinkEntries + Content.SlotCount);
        }

        [Fact]
        public void Analysis_counts_choices_once_and_accounts_for_rank_cost_and_output_arity()
        {
            var combined = Talent("test.capacity.dual", 4, 9);
            combined.LinkPerRank = new LinkDef { Kind = LinkKind.MemoryDamage, Value = 13, Requires = new[] { "St_D_IcyVeins" } };
            var choice = Talent("test.capacity.choice", 3, 1);
            choice.Gimmick = null;
            choice.ClusterStar = new ClusterStarDef { Kind = ClusterStarKind.Choice };
            choice.Choices = new[]
            {
                Talent("test.capacity.choice", 1, 1),
                new TalentDef("test.capacity.choice", Line.Offense, new Txt("効果", "Effect"),
                    new LinkDef { Kind = LinkKind.MemoryDamage, Value = 10, Requires = new[] { "St_D_IcyVeins" } }, 1),
            };
            var capacity = BuildLimits.Analyze(new[] { combined, choice }, 7);
            Assert.Equal(2, capacity.GimmickEntries);
            Assert.Equal(2, capacity.LinkEntries);
            Assert.Equal(3, capacity.TotalEntries);
            Assert.Equal(2, capacity.MaximumOutputArity);
            Assert.Equal(4, capacity.LinkValuePerPoint(LinkKind.MemoryDamage, 1));
            capacity = BuildLimits.Analyze(new[] { combined, choice }, 3);
            Assert.Equal(1, capacity.GimmickEntries);
            Assert.Equal(1, capacity.LinkEntries);
            Assert.Equal(1, capacity.TotalEntries);
        }

        [Fact]
        public void Aggregate_link_precision_capacity_includes_all_point_and_equipment_sources()
        {
            foreach (LinkKind kind in Enum.GetValues(typeof(LinkKind)))
            {
                if (kind == LinkKind.None) continue;
                for (int requirements = 1; requirements <= BuildLimits.MaxLinkRequirements; requirements++)
                {
                    decimal fromEquipment = Content.SlotCount * Links.EquippedCap(kind, requirements);
                    decimal fromStars = StarProgression.MaxSpendablePoints * Math.Max(
                        Links.EquippedCap(kind, requirements), BuildLimits.Registered.LinkValuePerPoint(kind, requirements));
                    Assert.Equal(1000L * (fromEquipment + fromStars), BuildLimits.MaxLinkValueMilli(kind, requirements));
                    Assert.InRange(BuildLimits.MaxLinkValueMilli(kind, requirements), 1, int.MaxValue);
                }
            }
            Assert.Equal(0, BuildLimits.MaxLinkValueMilli(LinkKind.None, 1));
            Assert.Equal(0, BuildLimits.MaxLinkValueMilli(LinkKind.MemoryDamage, BuildLimits.MaxLinkRequirements + 1));
        }

        [Fact]
        public void Registered_identifiers_fit_the_bounded_wire_tokens()
        {
            foreach (var talent in Content.Talents.Concat(HeroSigils.All))
            {
                Assert.InRange(talent.Id.Length, 1, BuildLimits.MaxStarIdLength);
                foreach (var effect in Effects(talent))
                {
                    if (effect.RouteMemory != null) Assert.InRange(effect.RouteMemory.Length, 1, BuildLimits.MaxTokenLength);
                    if (effect.LinkPerRank != null)
                    {
                        Assert.InRange(effect.LinkPerRank.Requires.Length, 1, BuildLimits.MaxLinkRequirements);
                        Assert.All(effect.LinkPerRank.Requires, token => Assert.InRange(token.Length, 1, BuildLimits.MaxTokenLength));
                    }
                }
            }
            Assert.All(PairCombos.All, pair => Assert.InRange(pair.Id.Length, 1, BuildLimits.MaxPairIdLength));
            Assert.Equal(BuildLimits.MaxEncodedChars, BuildLimits.MaxEncodedBytes);
        }

        [Fact]
        public void Entire_structural_envelope_round_trips_within_the_documented_message_bound()
        {
            // This deliberately exceeds what any one hero can equip: every list is full at once,
            // including the whole pair registry. Every legitimate allocation is smaller.
            var build = new Build { DreamLevel = Content.MaxDreamLevel, SpentStarPoints = StarProgression.MaxPoints,
                Heat = Loot.ClampHeat(int.MaxValue) };
            foreach (Stat stat in Enum.GetValues(typeof(Stat))) build.Stats.Add(stat, -Content.StatCap(stat));
            foreach (Power power in Enum.GetValues(typeof(Power)))
            {
                if (power == Power.None) continue;
                build.Powers.Add(power, Content.PowerCap(power));
                if (NewPowersV129.IsConditionalAttribute(power)) build.ConditionalBasePowers.Add(power, Content.PowerCap(power));
            }
            var memories = HeroSigils.All.Select(t => t.RouteMemory).Where(Links.IsMemory)
                .Distinct().OrderByDescending(m => m.Length).Take(BuildLimits.MaxLinkRequirements).ToArray();
            Assert.Equal(BuildLimits.MaxLinkRequirements, memories.Length);
            for (int i = 0; i < BuildLimits.MaxGimmickEntries; i++)
                build.Gimmicks.Add(new GimmickEntry
                {
                    StarId = i.ToString(CultureInfo.InvariantCulture).PadLeft(BuildLimits.MaxStarIdLength, 'x'),
                    Memory = memories[0], Def = new GimmickDef
                    {
                        Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Expose,
                        Value = Gimmicks.Cap(GimmickEffect.Expose), Cooldown = 59.1234567f,
                        DurationPercent = Gimmicks.MaxParameterPercent,
                    },
                });
            for (int i = 0; i < BuildLimits.MaxLinkEntries; i++)
                build.Links.Add(new LinkDef { Kind = LinkKind.MemorySurge, Requires = memories,
                    ValueMilli = BuildLimits.MaxLinkValueMilli(LinkKind.MemorySurge, memories.Length) });
            foreach (var pair in PairCombos.All) build.PairCombos.Add(new PairComboEntry { Def = pair, Ranks = PairCombos.MaxRanks });

            string encoded = build.Encode();
            Assert.InRange(encoded.Length, 1, BuildLimits.MaxEncodedChars);
            Assert.InRange(Encoding.UTF8.GetByteCount(encoded), 1, BuildLimits.MaxEncodedBytes);
            var decoded = Build.Decode(encoded);
            Assert.NotNull(decoded);
            Assert.Equal(BuildLimits.MaxGimmickEntries, decoded.Gimmicks.Count);
            Assert.Equal(BuildLimits.MaxLinkEntries, decoded.Links.Count);
            Assert.Equal(BuildLimits.MaxPairComboEntries, decoded.PairCombos.Count);
            Assert.Equal(BuildLimits.MaxStatEntries, decoded.Stats.Count);
            Assert.Equal(BuildLimits.MaxPowerEntries, decoded.Powers.Count);
            Assert.Equal(BuildLimits.MaxConditionalPowerEntries, decoded.ConditionalBasePowers.Count);
            Assert.Equal(encoded, decoded.Encode());
        }

        [Theory]
        [InlineData("g")]
        [InlineData("l")]
        [InlineData("c")]
        [InlineData("s")]
        [InlineData("p")]
        [InlineData("u")]
        public void Raw_list_limits_reject_excess_before_aggregation_and_reject_invalid_spam(string section)
        {
            string[] entries = FullSectionEntries(section);
            string maximum = section + ":" + string.Join(",", entries);
            var accepted = Build.Decode(maximum);
            Assert.NotNull(accepted);
            if (section == "l")
            {
                Assert.Single(accepted.Links);
                Assert.Equal(BuildLimits.MaxLinkEntries * BuildPrecision.Scale, accepted.Links[0].ValueMilli);
            }
            Assert.Null(Build.Decode(maximum + "," + entries[0]));
            Assert.Null(Build.Decode(section + ":" + string.Join(",", Enumerable.Repeat("invalid", entries.Length + 1))));
            Assert.Null(Build.Decode(maximum + ";" + section + ":" + entries[0]));
            if (section != "l")
            {
                entries[entries.Length - 1] = entries[0];
                Assert.Null(Build.Decode(section + ":" + string.Join(",", entries)));
            }
        }

        [Theory]
        [InlineData("g")]
        [InlineData("l")]
        [InlineData("c")]
        [InlineData("s")]
        [InlineData("p")]
        [InlineData("u")]
        public void Encoder_rejects_each_oversized_list_instead_of_dropping_entries(string section)
        {
            var build = Build.Decode(section + ":" + string.Join(",", FullSectionEntries(section)));
            Assert.NotNull(build);
            switch (section)
            {
                case "g": build.Gimmicks.Add(build.Gimmicks[0]); break;
                case "l":
                    // The decoder aggregates this section, so restore all independent inputs.
                    build.Links.Clear();
                    for (int i = 0; i <= BuildLimits.MaxLinkEntries; i++)
                        build.Links.Add(new LinkDef { Kind = LinkKind.MemoryDamage, Value = 1, Requires = new[] { "St_D_IcyVeins" } });
                    break;
                case "c": build.PairCombos.Add(build.PairCombos[0]); break;
                case "s": build.Stats.Add((Stat)int.MaxValue, 1); break;
                case "p": build.Powers.Add((Power)int.MaxValue, 1); break;
                case "u": build.ConditionalBasePowers.Add((Power)int.MaxValue, 1); break;
            }
            Assert.Throws<InvalidOperationException>(() => build.Encode());
        }

        [Theory]
        [InlineData("h:0")]
        [InlineData("d:1")]
        [InlineData("a:0")]
        [InlineData("g:")]
        [InlineData("l:")]
        [InlineData("c:")]
        [InlineData("s:")]
        [InlineData("p:")]
        [InlineData("u:")]
        public void Every_duplicate_section_is_rejected_even_when_empty(string section)
        {
            Assert.NotNull(Build.Decode(section));
            Assert.Null(Build.Decode(section + ";" + section));
        }

        [Fact]
        public void Oversized_characters_utf8_bytes_and_tokens_are_rejected()
        {
            string characters = "g:" + new string('x', BuildLimits.MaxEncodedChars - 1);
            Assert.Equal(BuildLimits.MaxEncodedChars + 1, characters.Length);
            Assert.Null(Build.Decode(characters));
            string bytes = "g:" + new string('\u00e9', BuildLimits.MaxEncodedBytes / 2);
            Assert.True(bytes.Length < BuildLimits.MaxEncodedChars);
            Assert.True(Encoding.UTF8.GetByteCount(bytes) > BuildLimits.MaxEncodedBytes);
            Assert.Null(Build.Decode(bytes));
            Assert.Null(Build.Decode("g:" + new string('x', BuildLimits.MaxStarIdLength + 1)
                + ":St_D_IcyVeins:2:8:1000:0:0:0:0:0:0"));
            Assert.Null(Build.Decode("l:5:1000:" + new string('x', BuildLimits.MaxTokenLength + 1)));
            Assert.Null(Build.Decode("g:test.packet:St_D_IcyVeins:2:8:2147483648:0:0:0:0:0:0"));
        }

        [Fact]
        public void Convergence_preserves_over_two_hundred_live_victim_cooldowns_when_expired_entries_are_pruned()
        {
            var build = new Build();
            build.Powers.Add(Power.Convergence, 150);
            var runtime = new PowerRuntime(build, 0f);
            for (int victim = 1; victim <= StarProgression.MaxPoints; victim++)
                Assert.Equal(150f, runtime.TakeConvergence(victim <= 150 ? 0f : 2f, victim, true, 100f));
            for (int victim = 1; victim <= StarProgression.MaxPoints; victim++)
                Assert.Equal(0f, runtime.TakeConvergence(3f, victim, true, 100f));
            // A new victim after six seconds permits pruning only the older expired half.
            Assert.Equal(150f, runtime.TakeConvergence(6f, StarProgression.MaxPoints + 1, true, 100f));
            for (int victim = 151; victim <= StarProgression.MaxPoints; victim++)
                Assert.Equal(0f, runtime.TakeConvergence(6f, victim, true, 100f));
            for (int victim = 1; victim <= 150; victim++)
                Assert.Equal(150f, runtime.TakeConvergence(6f, victim, true, 100f));
            for (int victim = 151; victim <= StarProgression.MaxPoints; victim++)
                Assert.Equal(150f, runtime.TakeConvergence(8f, victim, true, 100f));
        }

        private static string[] FullSectionEntries(string section)
        {
            switch (section)
            {
                case "g": return Enumerable.Range(0, BuildLimits.MaxGimmickEntries)
                    .Select(i => "test.packet." + i.ToString(CultureInfo.InvariantCulture) + ":St_D_IcyVeins:2:8:1000:0:0:0:0:0:0").ToArray();
                case "l": return Enumerable.Repeat("5:1000:St_D_IcyVeins", BuildLimits.MaxLinkEntries).ToArray();
                case "c": return PairCombos.All.Select(pair => pair.Id + ":1").ToArray();
                case "s": return Enum.GetValues(typeof(Stat)).Cast<Stat>().Select(s => ((int)s).ToString(CultureInfo.InvariantCulture) + "=1").ToArray();
                case "p": return Enum.GetValues(typeof(Power)).Cast<Power>().Where(p => p != Power.None)
                    .Select(p => ((int)p).ToString(CultureInfo.InvariantCulture) + "=1").ToArray();
                case "u": return Enum.GetValues(typeof(Power)).Cast<Power>().Where(NewPowersV129.IsConditionalAttribute)
                    .Select(p => ((int)p).ToString(CultureInfo.InvariantCulture) + "=1").ToArray();
                default: throw new ArgumentOutOfRangeException(nameof(section));
            }
        }

        private static TalentDef Talent(string id, int cost, int ranks) =>
            new TalentDef(id, Line.Offense, new Txt("効果", "Effect"), Stat.AttackPct, 0, ranks)
            {
                RankCost = cost,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Expose, Value = 1 },
            };

        private static IEnumerable<TalentDef> Effects(TalentDef talent) => talent.IsChoice ? talent.Choices : new[] { talent };

        private static int IndependentMaximum(IEnumerable<TalentDef> talents, Func<TalentDef, bool> predicate)
        {
            int maximum = 0;
            foreach (var tree in talents.Where(t => !t.IsKeystone).GroupBy(t => t.HeroKey ?? ""))
            {
                int points = 0, count = 0;
                foreach (int cost in tree.Where(t => Effects(t).Any(predicate)).Select(t => t.RankCost).OrderBy(c => c))
                {
                    if (points + cost > StarProgression.MaxPoints) break;
                    points += cost;
                    count++;
                }
                maximum = Math.Max(maximum, count);
            }
            return maximum;
        }
    }
}
