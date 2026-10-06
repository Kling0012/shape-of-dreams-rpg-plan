using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.24：夢の変種（本体の敵をもとにした強い敵）。</summary>
    public class VariantsV124Tests
    {
        [Fact]
        public void Every_variant_is_well_formed_and_unique_per_monster_type()
        {
            Assert.True(Variants.All.Count >= 12);
            Assert.Equal(Variants.All.Count, Variants.All.Select(v => v.Id).Distinct().Count());
            Assert.Equal(Variants.All.Count, Variants.All.Select(v => v.MonsterType).Distinct().Count());
            Assert.Equal(Variants.All.Count, Variants.All.Select(v => v.Name.Ja).Distinct().Count());
            foreach (var v in Variants.All)
            {
                Assert.StartsWith("var.", v.Id);
                Assert.StartsWith("Mon_", v.MonsterType);
                Assert.False(string.IsNullOrWhiteSpace(v.Description.Ja));
                Assert.False(string.IsNullOrWhiteSpace(v.Description.En));
                Assert.NotEmpty(v.Stats);
                Assert.InRange(v.Scale, 1.0f, 1.5f);
                Assert.Same(v, Variants.Get(v.Id));
                Assert.Same(v, Variants.ForMonsterType(v.MonsterType));
                Assert.Equal(v.Affixes, Nightmares.Sanitize((int)v.Affixes));
            }
        }

        [Fact]
        public void Variants_appear_from_depth_two_and_at_most_once_per_room()
        {
            var v = Variants.All[0];
            int minimum = MonsterBalanceTableTests.Int("variants", "MinDepth");
            double baseline = MonsterBalanceTableTests.Double("variants", "BaseChance");
            double perDepth = MonsterBalanceTableTests.Double("variants", "ChancePerDepth");
            Assert.Equal(0, Variants.Chance(minimum - 1));
            Assert.Equal(baseline, Variants.Chance(minimum), 3);
            double expected = baseline + perDepth * (5 - minimum);
            Assert.Equal(expected, Variants.Chance(5), 3);
            var rng = new Rng(9);
            int hits = 0;
            for (int i = 0; i < 5000; i++) if (Variants.Roll(rng, v.MonsterType, 5, false) != null) hits++;
            Assert.InRange(hits / 5000.0, System.Math.Max(0, expected - 0.03), System.Math.Min(1, expected + 0.03));
            for (int i = 0; i < 200; i++) Assert.Null(Variants.Roll(rng, v.MonsterType, 5, true));
            Assert.Null(Variants.Roll(rng, "Mon_Unknown", 5, false));
        }

        [Fact]
        public void Killing_a_variant_rewards_like_a_nightmare_and_counts()
        {
            var p = Profile.CreateNew(24);
            Rules.BeginRun(p, "Hero_A");
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.NightmareHunter, Target = 1, RewardShards = 5, RewardXp = 5 });
            var ev = Rules.OnKill(p, MonsterTier.Normal, 5, variantId: "var.corroding_hound");
            Assert.Equal(1, p.Stats.VariantsSlain);
            Assert.Contains(ev, e => e.Text.Contains("蝕む猟犬"));
            Assert.True(p.Run.Bounties[0].Done);
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(1, q.Stats.VariantsSlain);
        }

        [Fact]
        public void Shard_devourer_pays_out_more_shards()
        {
            int Normal(ulong seed)
            {
                var p = Profile.CreateNew(seed);
                Rules.BeginRun(p, "Hero_A");
                p.Run.Bounties.Clear();
                Rules.OnKill(p, MonsterTier.Normal, 5, NightmareAffix.Ironclad);
                return p.Run.SatchelShards;
            }
            int Devourer(ulong seed)
            {
                var p = Profile.CreateNew(seed);
                Rules.BeginRun(p, "Hero_A");
                p.Run.Bounties.Clear();
                Rules.OnKill(p, MonsterTier.Normal, 5, variantId: "var.shard_devourer");
                return p.Run.SatchelShards;
            }
            int percent = MonsterBalanceTableTests.Int("variants", "DevourerShardBonusPct");
            int bonus = MonsterBalanceTableTests.Int("variants", "BonusShards");
            for (ulong s = 1; s <= 40; s++)
            {
                int normal = Normal(s);
                int expected = percent == 100 ? normal : (int)System.Math.Min(int.MaxValue, (long)normal * percent / 100 + bonus);
                Assert.Equal(expected, Devourer(s));
            }
        }
    }
}
