using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class HostBuildValidationTests
    {
        private const string Hero = "Hero_Cetus";
        private static Profile Funded()
        {
            var p = Profile.CreateNew(31);
            p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            return p;
        }
        private static void Allocate(Profile p, string id, int? option = null)
        {
            TreeTestPaths.Connect(p, Hero, id);
            Rules.AddTalentRank(p, Hero, id, option);
        }
        private static string Submit(Profile p, Build build = null) =>
            HostBuildValidation.Encode(build ?? Build.Compute(p, Hero, 0), p, Hero, 0);
        private static void Reject(string packet, string reason = null)
        {
            Assert.False(HostBuildValidation.TryAccept(packet, Hero, out var build, out var actualReason));
            Assert.Null(build);
            if (reason != null) Assert.Equal(reason, actualReason);
        }

        [Theory]
        [InlineData("a:0;d:1;g:fake1:St_D_IcyVeins:2:2:1000000:0:0:0:0:0:0")]
        [InlineData("d:1;a:0;g:audit0:St_L_Blizzard:1:2:1000000:0:60:0:0:0:0,audit1:St_L_Blizzard:1:2:1000000:0:60:0:0:0:0")]
        public void Fabricated_zero_cost_effects_are_rejected(string packet)
        {
            // These exact review packets were accepted by the old host's Build.Decode boundary.
            Reject(packet);
            string inputs = Submit(Funded()).Split('|')[1];
            Reject(packet + "|" + inputs, "derived-effects");
        }

        [Fact]
        public void Three_hundred_independently_firing_fake_effects_are_rejected()
        {
            var build = new Build();
            for (int i = 0; i < 300; i++) build.Gimmicks.Add(new GimmickEntry
            {
                StarId = "fake" + i, Memory = "St_L_Blizzard",
                Def = new GimmickDef { Trigger = GimmickTrigger.OnUse, Effect = GimmickEffect.Burst, Value = 1000, Cooldown = 60 }
            });
            Reject(Submit(Funded(), build), "derived-effects");
        }

        [Fact]
        public void Connected_route_cluster_choice_and_pair_derive_the_same_combat_and_pressure()
        {
            var p = Funded();
            Allocate(p, "h.cetus.cluster.icy-veins.6");
            Allocate(p, "h.cetus.cluster.icy-veins.5", 0);
            Allocate(p, "h.cetus.cluster.frozen-recall.3");
            var original = Build.Compute(p, Hero, 0);
            var receiver = new BuildTransferReceiver();
            string complete = null;
            foreach (var part in BuildTransfer.Split(Submit(p)).Reverse()) Assert.True(receiver.TryAccept(part, out complete));
            Assert.True(HostBuildValidation.TryAccept(complete, Hero, out var accepted, out var reason), reason);
            Assert.Equal(original.Encode(), accepted.Encode());
            Assert.Equal(Rules.SpentPoints(p.Hero(Hero), Hero), accepted.SpentStarPoints);
            Assert.Equal(DreamPressure.Average(new[] { original }).HealthMultiplier,
                DreamPressure.Average(new[] { accepted }).HealthMultiplier);
            Assert.Contains(accepted.PairCombos, c => c.Def.HeroKey == Hero);
            Assert.Contains(accepted.Gimmicks, g => g.Def.Value != decimal.Truncate(g.Def.Value));
        }

        [Theory]
        [InlineData("id")]
        [InlineData("memory")]
        [InlineData("trigger")]
        [InlineData("effect")]
        [InlineData("value")]
        [InlineData("arg")]
        [InlineData("cooldown")]
        [InlineData("duration")]
        [InlineData("radius")]
        [InlineData("targets")]
        [InlineData("chance")]
        public void Existing_allocated_star_cannot_be_rebound_or_amplified(string field)
        {
            var p = Funded(); Allocate(p, "h.cetus.route.icy-veins.4");
            // Edit the wire directly: some fields are intentionally invalid even to Encode/Clamp.
            string valid = Submit(p);
            string[] halves = valid.Split('|');
            var sections = halves[0].Split(';');
            int gimmickSection = Array.FindIndex(sections, s => s.StartsWith("g:"));
            var records = sections[gimmickSection].Substring(2).Split(',');
            int record = Array.FindIndex(records, s => s.StartsWith("h.cetus.route.icy-veins.2:"));
            var f = records[record].Split(':');
            string[] fields = { "id", "memory", "trigger", "effect", "value", "arg", "cooldown", "duration", "radius", "targets", "chance" };
            int index = Array.IndexOf(fields, field);
            f[index] = index < 2 ? field == "id" ? "fake1" : "St_L_Blizzard" :
                (int.Parse(f[index]) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            records[record] = string.Join(":", f);
            sections[gimmickSection] = "g:" + string.Join(",", records);
            Reject(string.Join(";", sections) + "|" + halves[1]);
        }

        [Theory]
        [InlineData("link")]
        [InlineData("pair")]
        [InlineData("cluster")]
        public void Link_pair_and_cluster_values_require_their_actual_ranks(string category)
        {
            var p = Funded(); Allocate(p, "h.cetus.cluster.icy-veins.6");
            Allocate(p, "h.cetus.cluster.frozen-recall.3");
            var build = Build.Compute(p, Hero, 0);
            if (category == "link") build.Links[0].ValueMilli++;
            if (category == "pair") build.PairCombos[0].Ranks++;
            if (category == "cluster") build.Gimmicks.Single(g => g.StarId == "h.cetus.cluster.frozen-recall.2").Def.ValuePrecise += 1;
            Reject(Submit(p, build), "derived-effects");
        }

        [Fact]
        public void Generic_valid_native_entries_and_fabricated_channel_contributors_are_not_entitlements()
        {
            FractionalScopedModifiers.RegisterCapProfile(new NativeStarCapProfile
            {
                Id = "test.host-unallocated-native", Kind = LinkKind.MemoryDamage, Maximum = ValueUnits.FromPercent(10)
            });
            var p = Funded(); Allocate(p, "h.cetus.route.icy-veins.4");
            var build = Build.Compute(p, Hero, 0);
            build.NativeModifiers.Add(new NativeMemoryModifierEntry
            {
                Memory = "St_D_IcyVeins", Kind = LinkKind.MemoryDamage, ValueMilli = 1000,
                CapProfileId = "test.host-unallocated-native"
            });
            Reject(Submit(p, build), "derived-effects");
            build.NativeModifiers.Clear();
            var effect = build.Gimmicks.First();
            effect.Channel = new EffectChannelDef
            {
                SourceMemory = effect.Memory, ReceiverMemory = effect.Memory, ChannelId = "test.fake-channel"
            };
            effect.ContributorIds = new[] { effect.StarId, "fake1" };
            Reject(Submit(p, build), "derived-effects");
        }

        [Fact]
        public void Empty_allocation_cannot_claim_an_existing_pair_or_link()
        {
            var pair = PairCombos.All.First(p => p.HeroKey == Hero);
            var build = new Build();
            build.PairCombos.Add(new PairComboEntry { Def = pair, Ranks = 1 });
            Reject(Submit(Funded(), build), "derived-effects");
            build.PairCombos.Clear();
            build.Links.Add(new LinkDef { Kind = LinkKind.MemoryDamage, Value = 1, Requires = new[] { "St_D_IcyVeins" } });
            Reject(Submit(Funded(), build), "derived-effects");
        }

        [Fact]
        public void Sender_hero_tree_and_allocation_identity_are_authoritative()
        {
            var p = Funded(); Allocate(p, "h.cetus.route.icy-veins.4");
            Assert.False(HostBuildValidation.TryAccept(Submit(p), "Hero_Mist", out _, out var reason));
            Assert.Equal("allocation", reason);
            p.Hero(Hero).Talents["fake1"] = 1;
            Reject(Submit(p), "allocation");
        }

        private static (Profile Profile, TalentDef[] Tree, TalentDef[] Extra) BudgetTree()
        {
            var p = Funded();
            Allocate(p, "h.cetus.cluster.icy-veins.5", 1);
            var canonical = HeroSigils.TreeFor(Hero);
            var cluster = new StarClusterDef
            {
                Id = "test.host-budget", HeroKey = Hero, Anchor = "h.cetus.route.icy-veins.4",
                Region = ClusterRegion.Memory("h.cetus.route.icy-veins"), Shape = ClusterShape.Chain,
                Stars = Enumerable.Range(0, StarProgression.MaxSpendablePoints).Select(_ => new ClusterStarDef
                {
                    Kind = ClusterStarKind.MemoryDamage, Name = new Txt("記憶の冴え", "Memory Damage"),
                    Memory = "St_D_IcyVeins", Amount = 1
                }).ToArray()
            };
            var extra = StarClusters.Generate(new[] { cluster }, canonical).ToArray();
            return (p, canonical.Concat(extra).ToArray(), extra);
        }

        [Fact]
        public void Rank_budget_includes_stat_power_and_effect_stars_together()
        {
            // Today's authored tree cannot spend all 304 points. Generated registered definitions
            // exercise the same host allocation boundary without inventing invalid per-star ranks.
            var fixture = BudgetTree(); var h = fixture.Profile.Hero(Hero);
            int remaining = StarProgression.MaxSpendablePoints - Rules.SpentPoints(h, Hero);
            foreach (var def in fixture.Extra.Take(remaining + 1)) h.Talents[def.Id] = 1;
            var engine = new EffectiveAllocationValidation(fixture.Tree);
            Assert.False(HostBuildValidation.TryValidateAllocation(h, Hero, engine, out var spent, out var reason));
            Assert.Equal("point-budget", reason);
            Assert.Equal(0, spent);
        }

        [Fact]
        public void Codex_bonus_points_survive_host_allocation_and_build_decoding()
        {
            var fixture = BudgetTree(); var h = fixture.Profile.Hero(Hero);
            int remaining = StarProgression.MaxSpendablePoints - Rules.SpentPoints(h, Hero);
            foreach (var def in fixture.Extra.Take(remaining)) h.Talents[def.Id] = 1;
            var engine = new EffectiveAllocationValidation(fixture.Tree);
            Assert.True(HostBuildValidation.TryValidateAllocation(h, Hero, engine, out var spent, out var reason), reason);
            Assert.Equal(StarProgression.MaxSpendablePoints, spent);
            var computed = Build.ComputeForTree(fixture.Profile, Hero, 0, fixture.Tree);
            Assert.Equal(spent, computed.SpentStarPoints);
            Assert.Equal(spent, Build.Decode(computed.Encode()).SpentStarPoints);
        }

        [Fact]
        public void Understated_spent_points_do_not_suppress_pressure()
        {
            var p = Funded(); Allocate(p, "h.cetus.route.icy-veins.4");
            var build = Build.Compute(p, Hero, 0); build.SpentStarPoints = 0;
            Reject(Submit(p, build), "progression");
        }

        [Theory]
        [InlineData("disconnected")]
        [InlineData("rank")]
        [InlineData("choice")]
        [InlineData("keystone")]
        public void Invalid_allocation_prerequisites_are_rejected(string kind)
        {
            var p = Funded(); var h = p.Hero(Hero);
            if (kind == "disconnected") h.Talents["h.cetus.route.icy-veins.4"] = 1;
            if (kind == "rank")
            {
                var def = HeroSigils.TreeFor(Hero).First(t => !t.IsKeystone);
                h.Talents[def.Id] = def.MaxRank + 1;
            }
            if (kind == "choice") h.Talents["h.cetus.cluster.icy-veins.5"] = 1;
            if (kind == "keystone") h.Keystone = HeroSigils.TreeFor(Hero).First(t => t.IsKeystone).Id;
            Reject(Submit(p, new Build()));
        }

        [Fact]
        public void Scalar_and_conditional_totals_are_reconstructed_from_gear_and_stars()
        {
            var p = Funded(); Allocate(p, "h.cetus.cluster.icy-veins.5", 1);
            var relic = Loot.RollRelic(new Rng(37), Rarity.Epic, 10, Slot.Weapon);
            p.Stash.Add(relic); p.Hero(Hero).Equipped[(int)relic.Slot] = relic.Uid;
            var expected = Build.Compute(p, Hero, 0); var claimed = Build.Decode(expected.Encode());
            foreach (Stat stat in Enum.GetValues(typeof(Stat))) claimed.Stats[stat] = Content.StatCap(stat);
            foreach (Power power in Enum.GetValues(typeof(Power)))
                if (power != Power.None) claimed.Powers[power] = Content.PowerCap(power);
            foreach (Power power in Enum.GetValues(typeof(Power)))
                if (NewPowersV129.IsConditionalAttribute(power)) claimed.ConditionalBasePowers[power] = 0;
            Assert.True(HostBuildValidation.TryAccept(Submit(p, claimed), Hero, out var accepted, out var reason), reason);
            Assert.Equal(expected.Encode(), accepted.Encode());
        }

        [Fact]
        public void Duplicate_equipped_sources_and_slot_mismatches_are_rejected()
        {
            var p = Funded(); var relic = Loot.RollRelic(new Rng(38), Rarity.Rare, 1, Slot.Weapon);
            p.Stash.Add(relic); p.Hero(Hero).Equipped[(int)Slot.Weapon] = relic.Uid;
            p.Hero(Hero).Equipped[(int)Slot.Head] = relic.Uid;
            Reject(Submit(p));
            p.Hero(Hero).Equipped[(int)Slot.Weapon] = null;
            Reject(Submit(p));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("x")]
        [InlineData("s:|D:1;H:0;M:0;K:;T:;R:;P:;Y:0;D:1")]
        [InlineData("s:|D:1;H:0;M:0;K:;T:fake=2147483647=-1;R:;P:;Y:0")]
        [InlineData("s:|D:1;H:0;M:0;K:;T:;R:0:x:x::2147483647:1:0:0:0:0:0::;P:;Y:0")]
        public void Malformed_packets_return_rejection_without_throwing(string packet) => Reject(packet);

        [Fact]
        public void Rejected_player_does_not_affect_another_players_latest_legal_update()
        {
            var invalid = new BuildUpdateCoalescer(); var valid = new BuildUpdateCoalescer();
            invalid.Submit("bad");
            var p = Funded(); Allocate(p, "h.cetus.route.icy-veins.4"); valid.Submit(Submit(p));
            Assert.True(invalid.TryTake(0, out var bad)); Reject(bad);
            Assert.True(valid.TryTake(0, out var good));
            Assert.True(HostBuildValidation.TryAccept(good, Hero, out var accepted, out var reason), reason);
            Assert.Equal(Rules.SpentPoints(p.Hero(Hero), Hero), accepted.SpentStarPoints);
        }
    }
}
