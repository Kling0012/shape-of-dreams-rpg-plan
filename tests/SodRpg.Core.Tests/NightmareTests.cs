using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class NightmareTests
    {
        [Fact]
        public void No_nightmares_without_depth_and_never_on_bosses()
        {
            var rng = new Rng(1);
            for (int i = 0; i < 5000; i++)
            {
                Assert.Equal(NightmareAffix.None, Nightmares.Roll(rng, MonsterTier.MiniBoss, 0));
                Assert.Equal(NightmareAffix.None, Nightmares.Roll(rng, MonsterTier.Boss, 5));
            }
        }

        [Theory]
        [InlineData(MonsterTier.Normal, 1)]
        [InlineData(MonsterTier.MiniBoss, 5)]
        public void Nightmare_rate_matches_depth(MonsterTier tier, int depth)
        {
            var rng = new Rng(7);
            double expected = tier == MonsterTier.Normal ? MonsterBalanceTableTests.Double("nightmare", "NormalChancePerDepth") * depth
                : MonsterBalanceTableTests.Double("nightmare", "MiniBossBaseChance") + MonsterBalanceTableTests.Double("nightmare", "MiniBossChancePerDepth") * (depth - 1);
            int n = 40000, hits = 0;
            for (int i = 0; i < n; i++)
                if (Nightmares.Roll(rng, tier, depth) != NightmareAffix.None) hits++;
            Assert.InRange((double)hits / n, Math.Max(0, expected - 0.01), Math.Min(1, expected + 0.01));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        public void Deeper_nightmares_have_more_affixes(int depth)
        {
            var rng = new Rng(3);
            int affixes = MonsterBalanceTableTests.AffixCount(depth);
            double chance = MonsterBalanceTableTests.Double("nightmare", "MiniBossBaseChance") + MonsterBalanceTableTests.Double("nightmare", "MiniBossChancePerDepth") * (depth - 1);
            int seen = 0;
            for (int i = 0; i < 4000 && seen < 50; i++)
            {
                var a = Nightmares.Roll(rng, MonsterTier.MiniBoss, depth, chance > 0 ? 1 / chance : 1);
                if (a == NightmareAffix.None) continue;
                Assert.Equal(affixes, Nightmares.Count(a));
                seen++;
            }
            Assert.Equal(chance == 0 ? 0 : 50, seen);
        }

        [Fact]
        public void Monster_stats_include_common_health_and_regeneration_when_selected()
        {
            foreach (var a in Nightmares.AllAffixes)
            {
                var stats = Nightmares.MonsterStats(a, out float regen);
                Assert.Contains(stats, s => s.Stat == Stat.MaxHealthPct && s.Value == Nightmares.BaseHealthPct);
                if (a == NightmareAffix.Regenerating) Assert.Equal(MonsterBalanceTableTests.Float("nightmare", "RegenerationPctPerSecond"), regen);
            }
        }


        [Fact]
        public void Sanitize_drops_unknown_bits()
        {
            Assert.Equal(NightmareAffix.Berserk, Nightmares.Sanitize((int)NightmareAffix.Berserk | (1 << 20)));
            Assert.Equal(NightmareAffix.None, Nightmares.Sanitize(1 << 30));
        }

        [Fact]
        public void Slaying_a_nightmare_rolls_one_tier_higher_loot()
        {
            var normal = Profile.CreateNew(4);
            var nightmare = Profile.CreateNew(4);
            Rules.BeginRun(normal, "n");
            Rules.BeginRun(nightmare, "n");
            normal.Run.Bounties.Clear();
            nightmare.Run.Bounties.Clear();
            for (int i = 0; i < 300; i++)
            {
                Rules.OnKill(normal, MonsterTier.Normal, 10);
                Rules.OnKill(nightmare, MonsterTier.Normal, 10, NightmareAffix.Swift);
            }
            Assert.Equal(300, nightmare.Stats.NightmaresSlain);
            Assert.True(nightmare.Stats.RelicsFound > normal.Stats.RelicsFound * 5);
            Assert.True(nightmare.DreamXp + nightmare.DreamLevel * 1000 > normal.DreamXp + normal.DreamLevel * 1000);
        }

        [Fact]
        public void Nightmare_hunter_bounty_progresses_only_on_nightmares()
        {
            var p = Profile.CreateNew(4);
            Rules.BeginRun(p, "h");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.NightmareHunter, Target = 2, RewardShards = 5, RewardXp = 5 });
            Rules.OnKill(p, MonsterTier.Normal, 1);
            Assert.Equal(0, p.Run.Bounties[0].Progress);
            Rules.OnKill(p, MonsterTier.Normal, 1, NightmareAffix.Ironclad);
            Rules.OnKill(p, MonsterTier.MiniBoss, 1, NightmareAffix.Arcane | NightmareAffix.Colossal);
            Assert.True(p.Run.Bounties[0].Done);
        }

        [Fact]
        public void Nightmare_count_survives_codec()
        {
            var p = Profile.CreateNew(4);
            p.Stats.NightmaresSlain = 12;
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(12, q.Stats.NightmaresSlain);
        }
    }
}
