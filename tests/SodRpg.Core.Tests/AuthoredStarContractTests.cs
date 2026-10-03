using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class AuthoredStarContractTests
    {
        private const string Hero = "Hero_Cetus";
        private const string Memory = "St_D_IcyVeins";
        private static Txt Name => new Txt("試験の星", "Test Star");
        private static IReadOnlyList<TalentDef> Base(string hero = Hero) => HeroSigils.TreeFor(hero);
        private static AuthoredStarDef Star(string id, ClusterStarDef effect, string hero = Hero) => new AuthoredStarDef
        {
            HeroKey = hero, LocalStarId = id, ClusterId = "test.authored", Region = ClusterRegion.Outer,
            AnchorId = "outer.test.s1", Shape = ClusterShape.Ring, Effect = effect
        };
        private static AuthoredStarDef Anchor(string hero = Hero) => Star("outer.test.s1", new ClusterStarDef
        { Kind = ClusterStarKind.Stat, Name = Name, Stat = Stat.Armor, Amount = 1 }, hero);
        private static ClusterStarDef Damage(string memory = Memory) => new ClusterStarDef
        { Kind = ClusterStarKind.MemoryDamage, Name = Name, Memory = memory, Amount = 3 };

        [Fact]
        public void SavedSuffixRemainsStableAfterReorderAndUsesHeroQualifiedOuterKeys()
        {
            var a = Anchor();
            var d = Star("outer.test.damage", Damage());
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, d.LocalStarId) };
            var otherAnchor = Anchor("Hero_Vesper");
            var other = Star(d.LocalStarId, Damage("St_D_Resolve"), "Hero_Vesper");
            otherAnchor.Edges = new[] { new AuthoredStarEdge(otherAnchor.LocalStarId, other.LocalStarId) };
            var bases = Base().Concat(Base("Hero_Vesper")).ToArray();
            var first = StarClusters.GenerateAuthored(new[] { a, d, otherAnchor, other }, bases);
            var reordered = StarClusters.GenerateAuthored(new[] { other, d, otherAnchor, a }, bases);
            Assert.True(first.TryGet(Hero, d.LocalStarId, out var saved));
            Assert.True(reordered.TryGet(Hero, saved.Id, out var restored));
            Assert.Equal(Memory, restored.LinkPerRank.Requires[0]);
            Assert.True(reordered.TryGet("Hero_Vesper", saved.Id, out var independent));
            Assert.Equal("St_D_Resolve", independent.LinkPerRank.Requires[0]);
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { a, d, d }, Base()));
        }

        [Fact]
        public void CommonOuterOwnershipAcceptsVerifiedIdentityAndRejectsUnverifiedHeroMemory()
        {
            var a = Anchor();
            var common = Star("outer.test.common", Damage("St_C_FlashFreeze"));
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, common.LocalStarId) };
            var registry = StarClusters.GenerateAuthored(new[] { a, common }, Base());
            Assert.True(registry.TryGet(Hero, common.LocalStarId, out var node));
            Assert.Equal("St_C_FlashFreeze", node.LinkPerRank.Requires[0]);
            common.Effect = Damage("St_D_Resolve");
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { a, common }, Base()));
            common.Effect = Damage("St_C_Unverified");
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { a, common }, Base()));
        }

        [Fact]
        public void ReverseRingEntryDoesNotBypassPrerequisiteAndMigrationCascadesExactRefund()
        {
            var a = Anchor();
            var prerequisite = Star("outer.test.notable", new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = Name,
                Memory = Memory, Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Shield, Value = 1 } });
            var dependent = Star("outer.test.dependent", Damage());
            dependent.RequiredStarIds = new[] { prerequisite.LocalStarId };
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, prerequisite.LocalStarId),
                new AuthoredStarEdge(prerequisite.LocalStarId, dependent.LocalStarId), new AuthoredStarEdge(dependent.LocalStarId, a.LocalStarId) };
            var tree = StarClusters.GenerateAuthored(new[] { a, prerequisite, dependent }, Base()).TreeFor(Hero);
            var p = new Profile();
            AllocatePath(p.Hero(Hero), tree, a.LocalStarId);
            var h = p.Hero(Hero);
            h.Talents[a.LocalStarId] = 1;
            decimal before = Build.ComputeForTree(p, Hero, 0, tree).Links
                .Where(x => x.Kind == LinkKind.MemoryDamage && x.Requires.Contains(Memory)).Sum(x => x.Value);
            h.Talents[dependent.LocalStarId] = 1;
            Assert.Equal(before, Build.ComputeForTree(p, Hero, 0, tree).Links
                .Where(x => x.Kind == LinkKind.MemoryDamage && x.Requires.Contains(Memory)).Sum(x => x.Value));
            var refund = AuthoredStarMigration.Apply(h, tree, Array.Empty<LegacyStarMigration>());
            Assert.Equal(new[] { dependent.LocalStarId }, refund.StarIds);
            Assert.Equal(1, refund.RefundCost);
            Assert.True(h.Talents.ContainsKey(a.LocalStarId));
            Assert.False(h.Talents.ContainsKey(dependent.LocalStarId));
        }

        [Fact]
        public void RankedChoicePreservesThreeRanksAndCostUsesOneExplicitOptionOrRefundsAll()
        {
            var a = Anchor();
            var choice = Star("outer.test.choice", new ClusterStarDef { Kind = ClusterStarKind.Choice, Name = Name, MaxRank = 3, RankCost = 2,
                Options = new[] { new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = Name, Stat = Stat.Armor, Amount = 2, MaxRank = 3 },
                    new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = Name, Stat = Stat.MaxHealthPct, Amount = 3, MaxRank = 3 } } });
            choice.RetainedLegacy = true;
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, choice.LocalStarId) };
            var original = new TalentDef(choice.LocalStarId, Line.Guard, Name, Stat.Armor, 2, 3)
                { HeroKey = Hero, RankCost = 2 };
            var existing = Base().Concat(new[] { original }).ToArray();
            var tree = StarClusters.GenerateAuthored(new[] { a, choice }, existing).TreeFor(Hero);
            var oldPosition = HeroTreeLayout.ForTalents(existing).Nodes.Single(n => n.Id == choice.LocalStarId);
            var retainedPosition = HeroTreeLayout.ForTalents(tree).Nodes.Single(n => n.Id == choice.LocalStarId);
            Assert.Equal(oldPosition.X, retainedPosition.X);
            Assert.Equal(oldPosition.Y, retainedPosition.Y);
            var p = new Profile(); var h = p.Hero(Hero);
            AllocatePath(h, tree, a.LocalStarId, choice.LocalStarId);
            h.Talents[a.LocalStarId] = 1;
            h.Talents[choice.LocalStarId] = 3; h.TalentChoices[choice.LocalStarId] = 1;
            var migration = new[] { new LegacyStarMigration(choice.LocalStarId, 3, 2, true) };
            var before = Build.ComputeForTree(p, Hero, 0, tree);
            Assert.Equal(0, AuthoredStarMigration.Apply(h, tree, migration).RefundCost);
            Assert.Equal(3, h.Talents[choice.LocalStarId]);
            Assert.Equal(1, h.TalentChoices[choice.LocalStarId]);
            h.TalentChoices.Remove(choice.LocalStarId);
            var refund = AuthoredStarMigration.Apply(h, tree, migration);
            Assert.Equal(6, refund.RefundCost);
            Assert.Equal(new[] { choice.LocalStarId }, refund.StarIds);
            var after = Build.ComputeForTree(p, Hero, 0, tree);
            Assert.Equal(9, before.Get(Stat.MaxHealthPct) - after.Get(Stat.MaxHealthPct));
            Assert.Equal(0, AuthoredStarMigration.Apply(h, tree, migration).RefundCost);
        }

        [Fact]
        public void RequiredAnyAllowsOneAlternativeButMissingReferencesFailBeforePublishing()
        {
            var a = Anchor(); var b = Star("outer.test.b", Damage()); var c = Star("outer.test.c", Damage());
            c.RequiredAnyStarIds = new[] { a.LocalStarId, b.LocalStarId };
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, b.LocalStarId), new AuthoredStarEdge(a.LocalStarId, c.LocalStarId) };
            var registry = StarClusters.GenerateAuthored(new[] { a, b, c }, Base());
            registry.TryGet(Hero, c.LocalStarId, out var node);
            var hero = new HeroState();
            Assert.False(AuthoredStarContract.PrerequisitesMet(hero, node));
            hero.Talents[b.LocalStarId] = 1;
            Assert.True(AuthoredStarContract.PrerequisitesMet(hero, node));
            c.RequiredStarIds = new[] { "outer.test.missing" };
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { a, b, c }, Base()));
        }

        [Fact]
        public void CircularPrerequisitesAreRejectedEvenWhenTheRingHasAnEntry()
        {
            var a = Anchor(); var b = Star("outer.test.b", Damage()); var c = Star("outer.test.c", Damage());
            b.RequiredStarIds = new[] { c.LocalStarId }; c.RequiredStarIds = new[] { b.LocalStarId };
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, b.LocalStarId), new AuthoredStarEdge(a.LocalStarId, c.LocalStarId) };
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { a, b, c }, Base()));
        }

        [Fact]
        public void CrossSourceDamageCannotDisguiseItsRegionAsAMovementReceiverAndFakePairsFail()
        {
            var star = new AuthoredStarDef
            {
                HeroKey = Hero, LocalStarId = "test.receiver.damage", ClusterId = "test.receiver",
                Region = ClusterRegion.Memory("h.cetus.route.frost-charge"), AnchorId = "h.cetus.route.frost-charge.7",
                Shape = ClusterShape.Chain, Effect = Damage(),
                MemoryOwnership = new MemoryOwnership { TargetMemory = "St_M_FrostyCharge", SourceMemories = new[] { Memory } },
                Edges = new[] { new AuthoredStarEdge("h.cetus.route.frost-charge.7", "test.receiver.damage") }
            };
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { star }, Base()));
            star.Region = ClusterRegion.Bridge("test.unregistered-renewal"); star.AnchorId = "test.unregistered-renewal";
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { star }, Base()));
        }

        [Fact]
        public void ChangedNonChoiceMigrationRevisionPersistsAndFreshPurchasesAreNotRefundedAgain()
        {
            var a = Anchor(); var changed = Star("outer.test.changed", Damage());
            a.Edges = new[] { new AuthoredStarEdge(a.LocalStarId, changed.LocalStarId) };
            var tree = StarClusters.GenerateAuthored(new[] { a, changed }, Base()).TreeFor(Hero);
            var profile = new Profile(); var hero = profile.Hero(Hero);
            AllocatePath(hero, tree, a.LocalStarId); hero.Talents[a.LocalStarId] = 1;
            hero.Talents[changed.LocalStarId] = 1;
            var rules = new[] { new LegacyStarMigration(changed.LocalStarId, 1, 1, true) };
            Assert.Equal(1, AuthoredStarMigration.Apply(hero, tree, rules).RefundCost);
            Assert.False(hero.Talents.ContainsKey(changed.LocalStarId));
            Assert.Equal(1, hero.AuthoredMigrationVersion);
            // Newly purchased current effects are not legacy effects on the next load.
            hero.Talents[changed.LocalStarId] = 1;
            var loaded = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            // Custom registry IDs are not installed content; the revision marker itself is persisted independently.
            Assert.Equal(1, loaded.Hero(Hero).AuthoredMigrationVersion);
            var restored = hero.Clone();
            Assert.Equal(0, AuthoredStarMigration.Apply(restored, tree, rules).RefundCost);
            Assert.Equal(1, restored.Talents[changed.LocalStarId]);
        }

        [Fact]
        public void RetainedLegacyCannotInventAnOriginalRankOrPointCost()
        {
            var a = Anchor();
            a.RetainedLegacy = true;
            Assert.Throws<InvalidOperationException>(() => StarClusters.GenerateAuthored(new[] { a }, Base()));
        }

        internal static void AllocatePath(HeroState hero, IReadOnlyList<TalentDef> tree, string target, string excludedId = null)
        {
            var layout = HeroTreeLayout.ForTalents(tree);
            var predecessor = new Dictionary<int, int> { [0] = -1 };
            var queue = new Queue<int>(); queue.Enqueue(0);
            int end = -1;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (layout.Nodes[current].Id == target) { end = current; break; }
                foreach (int next in layout.Nodes[current].Neighbors)
                {
                    var talent = layout.Nodes[next].Talent;
                    if (predecessor.ContainsKey(next) || talent?.IsKeystone == true || talent?.Id == excludedId) continue;
                    predecessor.Add(next, current); queue.Enqueue(next);
                }
            }
            Assert.True(end >= 0);
            for (int current = predecessor[end]; current > 0; current = predecessor[current])
            {
                var talent = layout.Nodes[current].Talent;
                hero.Talents[talent.Id] = 1;
                if (talent.IsChoice) hero.TalentChoices[talent.Id] = 0;
            }
        }
    }
}
