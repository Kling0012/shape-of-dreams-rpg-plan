using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class BountyTests
    {
        private static Profile StartWith(params Bounty[] bounties)
        {
            var p = Profile.CreateNew(11);
            Rules.BeginRun(p, "b");
            p.Run.Bounties.Clear();
            p.Run.Bounties.AddRange(bounties);
            return p;
        }

        private static Bounty B(BountyKind k, int target) => new Bounty { Kind = k, Target = target, RewardShards = 10, RewardTuning = 1, RewardXp = 5 };

        [Fact]
        public void A_run_starts_with_three_distinct_bounties()
        {
            for (ulong seed = 1; seed < 50; seed++)
            {
                var p = Profile.CreateNew(seed);
                Rules.BeginRun(p, "r");
                Assert.Equal(Bounties.PerRun, p.Run.Bounties.Count);
                Assert.Equal(Bounties.PerRun, p.Run.Bounties.Select(b => b.Kind).Distinct().Count());
                Assert.All(p.Run.Bounties, b =>
                {
                    Assert.True(b.Target > 0);
                    Assert.True(b.RewardShards > 0 && b.RewardXp > 0);
                    Assert.False(b.Done);
                    Assert.False(string.IsNullOrWhiteSpace(b.Describe()));
                });
            }
        }

        [Fact]
        public void Bigger_targets_pay_more()
        {
            var rng = new Rng(1);
            var all = new List<Bounty>();
            for (int i = 0; i < 500; i++) all.AddRange(Bounties.Roll(rng, 7));
            foreach (var g in all.GroupBy(b => b.Kind))
            {
                var lo = g.Where(b => b.Target == g.Min(x => x.Target)).Max(b => b.RewardShards);
                var hi = g.Where(b => b.Target == g.Max(x => x.Target)).Min(b => b.RewardShards);
                Assert.True(hi >= lo, g.Key.ToString());
            }
        }

        [Fact]
        public void Kill_bounties_progress_by_tier_and_pay_into_satchel()
        {
            var p = StartWith(B(BountyKind.Slayer, 3), B(BountyKind.EliteHunter, 1), B(BountyKind.Bossbane, 1));
            var ev = new List<GameEvent>();
            ev.AddRange(Rules.OnKill(p, MonsterTier.Lesser, 1));
            ev.AddRange(Rules.OnKill(p, MonsterTier.Normal, 1));
            Assert.Equal(2, p.Run.Bounties[0].Progress);
            Assert.DoesNotContain(ev, e => e.Kind == EventKind.Bounty);
            int shards = p.Run.SatchelShards;
            ev.AddRange(Rules.OnKill(p, MonsterTier.Normal, 1));
            Assert.True(p.Run.Bounties[0].Done);
            Assert.Contains(ev, e => e.Kind == EventKind.Bounty);
            Assert.True(p.Run.SatchelShards >= shards + 10);
            Assert.True(p.Run.SatchelTuning >= 1);

            Rules.OnKill(p, MonsterTier.MiniBoss, 1);
            Assert.True(p.Run.Bounties[1].Done);
            Assert.False(p.Run.Bounties[2].Done);
            Rules.OnKill(p, MonsterTier.Boss, 1);
            Assert.True(p.Run.Bounties[2].Done);
        }

        [Fact]
        public void Completed_bounty_does_not_pay_twice()
        {
            var p = StartWith(B(BountyKind.Slayer, 1));
            Rules.OnKill(p, MonsterTier.Normal, 1);
            int tuning = p.Run.SatchelTuning;
            var ev = new List<GameEvent>();
            for (int i = 0; i < 20; i++) ev.AddRange(Rules.OnKill(p, MonsterTier.Lesser, 1));
            Assert.DoesNotContain(ev, e => e.Kind == EventKind.Bounty);
            Assert.Equal(tuning, p.Run.SatchelTuning);
            Assert.Equal(1, p.Run.Bounties[0].Progress);
        }

        [Fact]
        public void Secure_bounties_pay_directly_to_profile()
        {
            var p = StartWith(B(BountyKind.Collector, 2), B(BountyKind.DeepDiver, 2));
            var rng = new Rng(2);
            p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 1));
            Rules.Delve(p);
            Rules.Secure(p); // 深度1で2個確保 → 収集は達成、深度2は未達
            Assert.True(p.Run.Bounties[0].Done);
            Assert.False(p.Run.Bounties[1].Done);
            Assert.Equal(1, p.Run.Bounties[1].Progress);
            Assert.Equal(1, p.Material(Materials.Tuning));
            Assert.Equal(0, p.Run.SatchelTuning);

            Rules.Delve(p);
            Rules.Delve(p);
            Rules.Secure(p);
            Assert.True(p.Run.Bounties[1].Done);
            Assert.Equal(2, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Treasure_counts_rare_or_better_finds()
        {
            var p = StartWith(B(BountyKind.Treasure, 1));
            for (int i = 0; i < 60 && !p.Run.Bounties[0].Done; i++) Rules.OnKill(p, MonsterTier.Boss, 10);
            Assert.True(p.Run.Bounties[0].Done);
            Assert.Contains(p.Run.Satchel.Concat(p.Stash), r => r.Rarity >= Rarity.Rare);
        }

        [Fact]
        public void Pathfinder_counts_room_increments()
        {
            var p = StartWith(B(BountyKind.Pathfinder, 4));
            Rules.OnRoomsCleared(p, 2);
            Assert.Equal(2, p.Run.Bounties[0].Progress);
            Rules.OnRoomsCleared(p, 2); // 変化なし
            Assert.Equal(2, p.Run.Bounties[0].Progress);
            Rules.OnRoomsCleared(p, 5);
            Assert.True(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void Unsecured_bounty_rewards_are_lost_on_defeat()
        {
            var p = StartWith(B(BountyKind.Slayer, 1));
            Rules.OnKill(p, MonsterTier.Normal, 1);
            int satchelShards = p.Run.SatchelShards;
            Rules.EndRun(p, victory: false);
            Assert.Equal(Math.Max(1, (satchelShards + 3) / 4), p.Material(Materials.Shard));
            Assert.Equal(0, p.Material(Materials.Tuning));
            Assert.Equal(1, p.LastReport.BountiesDone);
            Assert.Equal(1, p.LastReport.BountiesTotal);
        }

        [Fact]
        public void Bounties_roundtrip_through_codec_and_reject_unknown_kinds()
        {
            var p = StartWith(B(BountyKind.Slayer, 5), B(BountyKind.DeepDiver, 3));
            Rules.OnKill(p, MonsterTier.Normal, 1);
            string text = ProfileCodec.Write(p);
            var q = ProfileCodec.Read(text, new List<string>());
            Assert.Equal(2, q.Run.Bounties.Count);
            Assert.Equal(1, q.Run.Bounties[0].Progress);
            Assert.Equal(BountyKind.DeepDiver, q.Run.Bounties[1].Kind);
            Assert.Equal(text, ProfileCodec.Write(q));

            p.Run.Bounties[0].Kind = (BountyKind)99;
            var notes = new List<string>();
            var r = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Single(r.Run.Bounties);
            Assert.NotEmpty(notes);
        }

        [Fact]
        public void Bounty_texts_exist_in_both_languages()
        {
            foreach (bool ja in new[] { true, false })
            {
                Loc.Japanese = ja;
                foreach (BountyKind k in Enum.GetValues(typeof(BountyKind)))
                {
                    var b = B(k, 3);
                    Assert.Contains("3", b.Describe());
                    Assert.False(string.IsNullOrWhiteSpace(b.RewardText()));
                }
            }
            Loc.Japanese = true;
        }
    }
}
