using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class HeroSigilTests
    {
        private static readonly string[] Heroes =
        {
            "Hero_Vesper", "Hero_Lacerta", "Hero_Cetus", "Hero_Yubar", "Hero_Husk", "Hero_Mist", "Hero_Nachia", "Hero_Aurena", "Hero_Bismuth",
        };

        [Fact]
        public void Every_base_traveler_has_five_nodes_and_two_keystones()
        {
            foreach (var h in Heroes)
            {
                Assert.True(HeroSigils.HasTree(h), h);
                var tree = HeroSigils.TreeFor(h).ToList();
                Assert.Equal(5, tree.Count(t => !t.IsKeystone));
                Assert.Equal(2, tree.Count(t => t.IsKeystone)); // v1.13：到達刻印は2つから1つを選ぶ
                Assert.True(tree.Where(t => !t.IsKeystone).Sum(t => t.MaxRank) >= Content.KeystoneRouteRequirement, h);
                Assert.All(tree, t => Assert.Equal(h, t.HeroKey));
                foreach (bool ja in new[] { true, false })
                {
                    Loc.Japanese = ja;
                    Assert.All(tree, t => Assert.False(string.IsNullOrWhiteSpace(t.Name.ToString())));
                    Assert.All(tree.Where(t => t.IsKeystone), k => Assert.False(string.IsNullOrWhiteSpace(k.Description.ToString())));
                }
            }
            Loc.Japanese = true;
            Assert.Equal(HeroSigils.All.Count, HeroSigils.All.Select(t => t.Id).Distinct().Count());
        }

        [Fact]
        public void Unknown_travelers_keep_the_generic_tree()
        {
            Assert.False(HeroSigils.HasTree("Hero_Kindred"));
            Assert.Equal(Content.Talents.Count, HeroSigils.TreeFor("Hero_Kindred").Count());
        }

        [Fact]
        public void Nodes_only_fit_their_own_traveler()
        {
            var p = Profile.CreateNew(1);
            p.DreamLevel = 20;
            Rules.AddTalentRank(p, "Hero_Lacerta", "h.lacerta.powder");
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "Hero_Vesper", "h.lacerta.powder"));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "Hero_Vesper", "t.off.edge")); // 汎用ノードは本体の旅人に使えない
            Rules.AddTalentRank(p, "Hero_Kindred", "t.off.edge");
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "Hero_Kindred", "h.vesper.fire"));
        }

        [Fact]
        public void Keystone_needs_six_points_and_mastery_three()
        {
            var p = Profile.CreateNew(1);
            p.DreamLevel = 20;
            const string h = "Hero_Cetus";
            foreach (var id in new[] { "h.cetus.cold", "h.cetus.cold", "h.cetus.cold", "h.cetus.shell", "h.cetus.shell", "h.cetus.shell" })
                Rules.AddTalentRank(p, h, id);
            Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, h, "h.cetus.key")); // 熟練度0
            p.Hero(h).Kills = 600; // 熟練度3
            Rules.SetKeystone(p, h, "h.cetus.key");
            var b = Build.Compute(p, h, 0);
            Assert.Equal(24, b.Get(Stat.ColdAmp));
            Assert.Equal(12, b.Get(Stat.MaxHealthPct));
            Assert.Equal(35, b.Get(Power.Frost));
        }

        [Fact]
        public void Fourth_attack_shift_is_capped_and_converts_to_game_index()
        {
            var p = Profile.CreateNew(1);
            p.DreamLevel = 20;
            Rules.AddTalentRank(p, "Hero_Vesper", "h.vesper.fourth");
            Rules.AddTalentRank(p, "Hero_Vesper", "h.vesper.fourth");
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "Hero_Vesper", "h.vesper.fourth"));
            var b = Build.Compute(p, "Hero_Vesper", 0);
            Assert.Equal(2, b.Get(Stat.FourthAttackShift));
            Assert.Equal(2f, StatUnits.ToGame(Stat.FourthAttackShift, 2));
            Assert.Equal(2, Build.Decode("s:17=99;p:;h:0").Get(Stat.FourthAttackShift));
            Assert.Equal(5f, StatUnits.ToGame(Stat.AttackRangePct, 5));
        }

        [Fact]
        public void Generic_points_on_base_travelers_are_refunded_on_load()
        {
            var p = Profile.CreateNew(1);
            p.DreamLevel = 10;
            // v1.1 までの保存：本体の旅人に汎用ノードと汎用の刻印
            p.Hero("Hero_Mist").Talents["t.off.edge"] = 3;
            p.Hero("Hero_Mist").Talents["t.off.swift"] = 3;
            p.Hero("Hero_Mist").Keystone = "t.off.key";
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(q.Hero("Hero_Mist").Talents);
            Assert.Null(q.Hero("Hero_Mist").Keystone);
            Assert.Equal(q.TalentPoints, Rules.FreePoints(q, "Hero_Mist"));
            Assert.NotEmpty(notes);
        }
    }
}
