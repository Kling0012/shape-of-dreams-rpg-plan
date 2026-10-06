using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarClustersV131Tests
    {
        private const string Hero = "Hero_Cetus";
        private const string Memory = "St_D_IcyVeins";
        private const string MemoryCluster = "h.cetus.cluster.icy-veins";
        private const string BridgeCluster = "h.cetus.cluster.frozen-recall";
        private const string OuterCluster = "h.cetus.cluster.abyssal-shell";
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
        private static decimal LinkValue(Build build, string memory, LinkKind kind) => build.Links
            .Where(l => l.Kind == kind && l.Requires.Contains(memory)).Sum(l => l.Value);
        private static GimmickEntry Entry(Build build, string id) => build.Gimmicks.Single(g => g.StarId == id);
        private static IReadOnlyList<TalentDef> BaseTree(string hero = Hero) => HeroSigils.TreeFor(hero)
            .Where(t => t.Cluster == null).ToArray();
        private static ClusterStarDef Damage(string memory = Memory) => new ClusterStarDef
        {
            Kind = ClusterStarKind.MemoryDamage, Name = new Txt("記憶の冴え", "Memory Damage"), Memory = memory, Amount = 3
        };
        private static StarClusterDef Definition() => new StarClusterDef
        {
            Id = "test.cetus.cluster", HeroKey = Hero, Region = ClusterRegion.Memory("h.cetus.route.icy-veins"),
            Anchor = "h.cetus.route.icy-veins.4", Shape = ClusterShape.Chain, Stars = new[] { Damage() }
        };

        [Theory]
        [InlineData(MemoryCluster + ".1", Memory, LinkKind.MemoryDamage, 3)]
        [InlineData(BridgeCluster + ".1", "St_Q_EmbracingTheChill", LinkKind.MemoryHaste, 2)]
        public void Memory_links_add_only_their_scoped_value(string id, string memory, LinkKind kind, int amount)
        {
            var p = Funded();
            TreeTestPaths.Connect(p, Hero, id);
            var previous = Build.Compute(p, Hero, 0);
            decimal before = LinkValue(previous, memory, kind);
            Rules.AddTalentRank(p, Hero, id);
            var build = Build.Compute(p, Hero, 0);
            decimal expected = kind == LinkKind.MemoryDamage
                ? HeroSigils.TreeFor(Hero).Where(t => t.LinkPerRank?.Kind == kind && t.LinkPerRank.Requires.Contains(memory))
                    .Sum(t => decimal.Round(t.LinkPerRank.Value * (p.Hero(Hero).Talents.TryGetValue(t.Id, out int ranks) ? ranks : 0)
                        * (1m + 1.5m * build.SpentStarPoints / 500), 3, MidpointRounding.AwayFromZero))
                : before + amount;
            Assert.Equal(expected, LinkValue(build, memory, kind));
            var decoded = Build.Decode(build.Encode());
            Assert.Equal(expected, LinkValue(decoded, memory, kind));
            Assert.DoesNotContain(decoded.Links.Where(l => l.Kind == kind), l => l.Requires.Contains("St_R_FrozenFists"));
        }

        [Fact]
        public void Boosts_add_by_memory_and_modify_route_and_cluster_notables_before_host_encoding()
        {
            var p = Funded();
            Allocate(p, BridgeCluster + ".2");
            Allocate(p, MemoryCluster + ".6");
            var build = Build.Compute(p, Hero, 0);
            Assert.Equal(33, Entry(build, "h.cetus.route.icy-veins.2").Def.Value);
            Assert.Equal(13.2m, Entry(build, BridgeCluster + ".2").Def.Value);
            Assert.Equal(13.2m, Entry(build, "h.cetus.route.icy-veins.7").Def.Value);
            Allocate(p, BridgeCluster + ".3");
            Allocate(p, OuterCluster + ".3");
            var decoded = Build.Decode(Build.Compute(p, Hero, 0).Encode());
            var element = Entry(decoded, "h.cetus.route.icy-veins.2").Def;
            Assert.Equal(5, element.ChancePercent);
            Assert.Equal(1, Gimmicks.ElementStacks(element, 0.37f));
            Assert.Equal(0, Gimmicks.ElementStacks(element, 0.39f));
            Assert.Equal(4.4f, Gimmicks.Duration(Entry(decoded, "h.cetus.route.icy-veins.4").Def, 4f), 4);
            Assert.Equal(4.4f, Gimmicks.Radius(Entry(decoded, BridgeCluster + ".2").Def, 4f), 4);
            Assert.Equal(2, Gimmicks.TargetLimit(Entry(decoded, OuterCluster + ".2").Def));
            Assert.Equal(6, Entry(decoded, OuterCluster + ".2").Def.Value);
        }

        [Fact]
        public void Outer_stat_and_power_notables_use_existing_build_paths()
        {
            var p = Funded();
            TreeTestPaths.Connect(p, Hero, OuterCluster + ".1");
            var before = Build.Compute(p, Hero, 0);
            Rules.AddTalentRank(p, Hero, OuterCluster + ".1");
            Assert.Equal(before.Get(Stat.Armor) + 2, Build.Compute(p, Hero, 0).Get(Stat.Armor));
            TreeTestPaths.Connect(p, Hero, MemoryCluster + ".5");
            int barrier = Build.Compute(p, Hero, 0).Get(Power.Barrier);
            Rules.AddTalentRank(p, Hero, MemoryCluster + ".5", 1);
            Assert.Equal(barrier + 2, Build.Compute(p, Hero, 0).Get(Power.Barrier));
            Assert.DoesNotContain(Build.Compute(p, Hero, 0).Gimmicks, g => g.StarId == MemoryCluster + ".5");
        }

        [Fact]
        public void Choice_requires_selection_switches_for_free_persists_and_respec_clears_it()
        {
            var p = Funded();
            string id = MemoryCluster + ".5";
            TreeTestPaths.Connect(p, Hero, id);
            int spent = Rules.SpentPoints(p.Hero(Hero), Hero);
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, id));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, id, 2));
            Assert.Throws<InvalidOperationException>(() => Rules.SetTalentChoice(p, Hero, id, 0));
            Assert.Equal(spent, Rules.SpentPoints(p.Hero(Hero), Hero));
            Rules.AddTalentRank(p, Hero, id, 0);
            Assert.Equal(GimmickEffect.Shield, Entry(Build.Compute(p, Hero, 0), id).Def.Effect);
            Assert.Equal(1.1m, Entry(Build.Compute(p, Hero, 0), id).Def.Value);
            spent = Rules.SpentPoints(p.Hero(Hero), Hero);
            int barrier = Build.Compute(p, Hero, 0).Get(Power.Barrier);
            Rules.SetTalentChoice(p, Hero, id, 1);
            Assert.Equal(spent, Rules.SpentPoints(p.Hero(Hero), Hero));
            Assert.Equal(barrier + 2, Build.Compute(p, Hero, 0).Get(Power.Barrier));
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(notes);
            Assert.Equal(1, loaded.Hero(Hero).TalentChoices[id]);
            Assert.Equal(barrier + 2, Build.Compute(loaded, Hero, 0).Get(Power.Barrier));
            var clone = loaded.Hero(Hero).Clone();
            clone.TalentChoices[id] = 0;
            Assert.Equal(1, loaded.Hero(Hero).TalentChoices[id]);
            loaded.Run = new RunState { HeroKey = Hero };
            Assert.Throws<InvalidOperationException>(() => Rules.SetTalentChoice(loaded, Hero, id, 0));
            Assert.Equal(1, loaded.Hero(Hero).TalentChoices[id]);
            loaded.Run = null;
            Rules.RemoveTalentRank(loaded, Hero, id);
            Assert.False(loaded.Hero(Hero).TalentChoices.ContainsKey(id));
            Rules.AddTalentRank(loaded, Hero, id, 0);
            Rules.ResetTalents(loaded, Hero);
            Assert.Empty(loaded.Hero(Hero).Talents);
            Assert.Empty(loaded.Hero(Hero).TalentChoices);
            Assert.Empty(Build.Compute(loaded, Hero, 0).Gimmicks);
            Assert.Equal(0, Rules.SpentPoints(loaded.Hero(Hero), Hero));
        }

        [Fact]
        public void Refund_rejects_disconnecting_cluster_and_accepts_leaf_refund()
        {
            var p = Funded();
            Allocate(p, BridgeCluster + ".3");
            int spent = Rules.SpentPoints(p.Hero(Hero), Hero);
            string beforeRefund = ProfileCodec.Write(p);
            foreach (string id in new[] { BridgeCluster + ".1", "h.cetus.ring.force" })
            {
                var refund = Rules.PreviewAllocationChange(p, Hero,
                    new AllocationChange { Kind = AllocationChangeKind.Refund, CandidateStarId = id });
                Assert.Contains(BridgeCluster + ".3", refund.AffectedRefundIds);
                Assert.NotNull(Record.Exception(() => Rules.RemoveTalentRank(p, Hero, id)));
                Assert.Equal(beforeRefund, ProfileCodec.Write(p));
            }
            Assert.Equal(spent, Rules.SpentPoints(p.Hero(Hero), Hero));
            Rules.RemoveTalentRank(p, Hero, BridgeCluster + ".3");
            Rules.RemoveTalentRank(p, Hero, BridgeCluster + ".2");
            Rules.RemoveTalentRank(p, Hero, BridgeCluster + ".1");
            Assert.Equal(spent - 3, Rules.SpentPoints(p.Hero(Hero), Hero));
            Assert.True(Rules.TalentsConnected(p.Hero(Hero), Hero));
        }

        [Fact]
        public void Old_profile_without_choices_keeps_existing_allocations_and_build()
        {
            var p = Funded();
            Allocate(p, "h.cetus.route.icy-veins.4");
            string oldSave = WithoutChoices(ProfileCodec.Write(p));
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(oldSave, notes);
            Assert.Empty(notes);
            Assert.Equal(p.Hero(Hero).Talents, loaded.Hero(Hero).Talents);
            Assert.Empty(loaded.Hero(Hero).TalentChoices);
            Assert.Equal(Build.Compute(p, Hero, 0).Encode(), Build.Compute(loaded, Hero, 0).Encode());
        }

        [Fact]
        public void Missing_choice_in_new_profile_is_refunded_not_silently_defaulted()
        {
            var p = Funded();
            Allocate(p, MemoryCluster + ".5", 1);
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(WithoutChoices(ProfileCodec.Write(p)), notes);
            Assert.False(loaded.Hero(Hero).Talents.ContainsKey(MemoryCluster + ".5"));
            Assert.True(Rules.TalentsConnected(loaded.Hero(Hero), Hero));
            Assert.Single(notes);
        }

        [Theory]
        [InlineData("duplicate-cluster")]
        [InlineData("anchor")]
        [InlineData("nested-choice")]
        public void Invalid_design_fails_loudly(string failure)
        {
            var def = Definition();
            var defs = new List<StarClusterDef> { def };
            switch (failure)
            {
                case "duplicate-cluster": defs.Add(Definition()); break;
                case "anchor": def.Anchor = "missing.anchor"; break;
                case "nested-choice": def.Stars = new[] { new ClusterStarDef { Kind = ClusterStarKind.Choice, Name = new Txt("選択", "Choice"), Options = new[] { Damage(), new ClusterStarDef { Kind = ClusterStarKind.Choice, Name = new Txt("選択", "Choice"), Options = new[] { Damage(), Damage() } } } } }; break;
            }
            Assert.Throws<InvalidOperationException>(() => StarClusters.Generate(defs, BaseTree()));
        }

        [Fact]
        public void Parameter_validation_includes_notables_in_other_supplied_clusters()
        {
            var param = Definition();
            param.Stars = new[] { new ClusterStarDef { Kind = ClusterStarKind.GimmickParam, Name = new Txt("対象", "Targets"), Memory = Memory, Param = GimmickParam.ExtraTargets, Amount = 1 } };
            var notable = Definition();
            notable.Id = "test.cetus.notable";
            notable.Stars = new[] { new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("跳弾", "Ricochet"), Memory = Memory,
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Ricochet, Value = 6, Arg = 1 } } };
            var generated = StarClusters.Generate(new[] { param, notable }, BaseTree());
            Assert.Equal(GimmickParam.ExtraTargets, generated[0].GimmickParameter);
            Assert.Equal(GimmickEffect.Ricochet, generated[1].Gimmick.Effect);
        }

        public static IEnumerable<object[]> Heroes => HeroStarRoutes.All.Select(t => t.HeroKey).Distinct().Select(h => new object[] { h });
        [Theory]
        [MemberData(nameof(Heroes))]
        public void Ninety_eight_star_clusters_per_hero_preserve_spacing_and_reachability(string hero)
        {
            var existing = BaseTree(hero);
            var routes = existing.Where(t => t.RouteId != null && t.RouteOrder == 4).ToArray();
            var clusters = new List<StarClusterDef>();
            for (int i = 0; i < 90; i++)
            {
                var anchor = routes[i % routes.Length];
                clusters.Add(new StarClusterDef
                {
                    Id = "stress." + hero.ToLowerInvariant() + "." + i, HeroKey = hero, Anchor = anchor.Id,
                    Region = ClusterRegion.Memory(anchor.RouteId), Shape = (ClusterShape)(i % 3),
                    Stars = Enumerable.Range(0, 8).Select(_ => Damage(anchor.RouteMemory)).ToArray()
                });
            }
            var generated = StarClusters.Generate(clusters, existing);
            var layout = HeroTreeLayout.ForTalents(existing.Concat(generated).ToArray());
            var reached = new HashSet<int> { layout.StartIndex };
            var queue = new Queue<int>();
            queue.Enqueue(layout.StartIndex);
            while (queue.Count > 0)
                foreach (int neighbor in layout.Nodes[queue.Dequeue()].Neighbors)
                    if (reached.Add(neighbor)) queue.Enqueue(neighbor);
            Assert.Equal(existing.Count + 721, reached.Count);
            for (int i = 0; i < layout.Nodes.Count; i++)
                for (int j = i + 1; j < layout.Nodes.Count; j++)
                {
                    float dx = layout.Nodes[i].X - layout.Nodes[j].X, dy = layout.Nodes[i].Y - layout.Nodes[j].Y;
                    Assert.True(dx * dx + dy * dy >= HeroTreeLayout.MinimumSpacing * HeroTreeLayout.MinimumSpacing * 0.81f,
                        layout.Nodes[i].Id + " overlaps " + layout.Nodes[j].Id);
                }
        }

        [Fact]
        public void Modified_runtime_windows_radius_and_target_limits_are_exercised()
        {
            var runtime = new GimmickRuntime();
            GimmickEntry Make(string id, GimmickEffect effect, int value) => new GimmickEntry
            {
                StarId = id, Memory = Memory,
                Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = effect, Value = value,
                    DurationPercent = 50, RadiusPercent = 25, ExtraTargets = 2 }
            };
            runtime.SetBuild(new[] { Make("test.expose", GimmickEffect.Expose, 7), Make("test.burst", GimmickEffect.Burst, 12),
                Make("test.rampart", GimmickEffect.Rampart, 1), Make("test.crescendo", GimmickEffect.Crescendo, 4) });
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0f, 10, 100, false, requests, activationId: 1);
            Assert.Equal(5f, requests.Single(r => r.Entry.StarId == "test.burst").AreaRadius);
            for (int victim = 11; victim <= 17; victim++)
                runtime.Fire(GimmickTrigger.OnHit, Memory, 0f, victim, 100, false, requests, activationId: 1);
            Assert.Equal(Enumerable.Range(1, 7), requests.Where(r => r.Entry.StarId == "test.rampart").Select(r => r.TargetCount));
            Assert.Equal(7, runtime.ExposePercent(10, 5.99f));
            Assert.Equal(0, runtime.ExposePercent(10, 6f));
            Assert.Equal(4, runtime.CrescendoPercent(Memory, 11.99f));
            Assert.Equal(0, runtime.CrescendoPercent(Memory, 12f));
            var wound = new GimmickWoundRuntime();
            var ticks = new List<GimmickWoundRuntime.Tick>();
            wound.Apply(10, 0f, 60f, false, 6f);
            wound.Update(6f, ticks);
            Assert.Equal(120f, ticks.Sum(t => t.Damage));
        }

        [Fact]
        public void Large_packets_preserve_all_allocatable_gimmicks_and_reject_security_overflow()
        {
            var build = new Build();
            for (int i = 0; i < StarProgression.MaxPoints; i++)
                build.Gimmicks.Add(new GimmickEntry { StarId = "test.packet." + i, Memory = Memory,
                    Def = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Expose, Value = 3, DurationPercent = 10 } });
            var decoded = Build.Decode(build.Encode());
            Assert.Equal(StarProgression.MaxPoints, decoded.Gimmicks.Count);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(decoded.Gimmicks);
            var requests = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Memory, 0, 10, 1, false, requests);
            Assert.Equal(build.Gimmicks.Select(g => g.StarId), requests.Select(r => r.Entry.StarId));
            string packet = string.Join(",", Enumerable.Range(0, Gimmicks.MaxEntries)
                .Select(i => "test.limit." + i + ":" + Memory + ":2:8:3000:0:0:0:0:0:0"));
            Assert.NotNull(Build.Decode("g:" + packet));
            Assert.Null(Build.Decode("g:" + packet + ",test.overflow:" + Memory + ":2:8:5000:0:0:0:0:0:0"));
            Assert.Null(Build.Decode("g:" + packet + ";g:test.limit.0:" + Memory + ":2:8:5000:0:0:0:0:0:0"));
        }

        private static string WithoutChoices(string encoded)
        {
            var root = (JsonObject)Json.Parse(encoded);
            root.TryGet("body", out object body);
            object Strip(object value)
            {
                if (value is JsonObject obj)
                {
                    var copy = new JsonObject();
                    foreach (var kv in obj.Properties) if (kv.Key != "talentChoices") copy.Add(kv.Key, Strip(kv.Value));
                    return copy;
                }
                if (value is List<object> list) return list.Select(Strip).ToList();
                return value;
            }
            var old = Strip(body);
            string checksum;
            using (var sha = SHA256.Create())
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(old)))).Replace("-", "").ToLowerInvariant();
            return Json.Write(new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", checksum).Add("body", old));
        }
    }
}
