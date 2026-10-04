using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.13：2つ目の到達刻印と、新しい依頼。</summary>
    public class SigilBountyV113Tests
    {
        private static Profile Run(BountyKind kind, int target, ulong seed = 4)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "v113");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = kind, Target = target, RewardShards = 10, RewardXp = 5 });
            return p;
        }

        [Fact]
        public void A_second_keystone_needs_a_free_slot_and_switching_refunds_the_first()
        {
            var p = Profile.CreateNew(1);
            p.Hero("Hero_Mist").StarXp = StarProgression.TotalXpForPoints(19);
            const string hero = "Hero_Mist";
            p.Hero(hero).Kills = 100000;
            foreach (var n in HeroSigils.TreeFor(hero).Where(t => !t.IsKeystone && t.Tier == 1))
                for (int i = 0; i < n.MaxRank; i++)
                    if (Rules.FreePoints(p, hero) > 0) Rules.AddTalentRank(p, hero, n.Id);
            var keys = HeroSigils.TreeFor(hero).Where(t => t.IsKeystone).ToList();
            Assert.Equal(2, keys.Count);
            // 星のレベル19では枠は1つ。枠を超えて選ぶことも、同じ刻印を重ねることもできない。
            Rules.SetKeystone(p, hero, keys[1].Id);
            Assert.Equal(keys[1].Id, p.Hero(hero).Keystone);
            Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, hero, keys[0].Id));
            Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, hero, keys[1].Id));
            // 外せば費用は戻り、別の刻印を選べる。
            int spent = Rules.SpentPoints(p.Hero(hero), hero);
            Rules.RemoveKeystone(p, hero, keys[1].Id);
            Assert.Equal(spent - Content.KeystoneCost, Rules.SpentPoints(p.Hero(hero), hero));
            Rules.SetKeystone(p, hero, keys[0].Id);
            Assert.Equal(keys[0].Id, p.Hero(hero).Keystone);
            var b = Build.Compute(p, hero, 0);
            Assert.True(b.Get(Power.OpeningStrike) > 0); // v1.28：回避で発動する刻印をやめ、先手の型に
            Assert.Equal(0, b.Get(Power.EchoingDodge));
        }

        [Fact]
        public void Secure_and_delve_bounties_advance()
        {
            var p = Run(BountyKind.Securer, 2);
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(1), Rarity.Common, 1));
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.Equal(1, p.Run.Bounties[0].Progress);

            var q = Run(BountyKind.Delver, 1);
            Rules.ReachSecurePoint(q);
            Rules.Delve(q);
            Assert.True(q.Run.Bounties[0].Done);
        }

        [Fact]
        public void Event_taker_advances_when_an_event_is_used()
        {
            var p = Run(BountyKind.EventTaker, 1);
            Rules.ReachSecurePoint(p);
            p.Run.OfferedEvent = DreamEvent.Archive;
            Rules.UseEvent(p, DreamEvent.Archive);
            Assert.True(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void Pact_keeper_is_reached_by_securing_with_pacts()
        {
            var p = Run(BountyKind.PactKeeper, 1);
            Rules.ReachSecurePoint(p);
            var pact = p.Run.OfferedPacts.FirstOrDefault();
            if (pact == Pact.None) p.Run.OfferedPacts.Add(pact = Pact.GlassHeart);
            Rules.Delve(p, pact);
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.True(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void New_bounties_have_text_and_can_be_rolled()
        {
            var seen = new HashSet<BountyKind>();
            var rng = new Rng(3);
            for (int i = 0; i < 2000; i++) foreach (var b in Bounties.Roll(rng)) seen.Add(b.Kind);
            foreach (var k in new[] { BountyKind.LegendFinder, BountyKind.EpicFinder, BountyKind.EventTaker, BountyKind.Securer, BountyKind.Delver, BountyKind.SetHunter, BountyKind.PactKeeper })
            {
                Assert.Contains(k, seen);
                Assert.False(string.IsNullOrWhiteSpace(new Bounty { Kind = k, Target = 1 }.Describe()));
            }
        }
    }
}
