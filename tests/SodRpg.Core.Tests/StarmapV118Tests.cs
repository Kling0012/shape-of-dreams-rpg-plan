using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarmapV118Tests
    {
        private const string Hero = "Hero_Vesper";
        private const string DeepStat = "h.vesper.deep.tenacity";
        private const string DeepPower = "h.vesper.deep.bulwark";

        private static Profile NewProfile(int firstStarRanks = 6)
        {
            var p = Profile.CreateNew(1);
            p.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(29);
            for (int i = 0; i < firstStarRanks; i++)
                Rules.AddTalentRank(p, Hero, i < 3 ? "h.vesper.fire" : "h.vesper.wall");
            return p;
        }

        private static void AddRanks(Profile p, string id, int ranks)
        {
            for (int i = 0; i < ranks; i++) Rules.AddTalentRank(p, Hero, id);
        }

        [Theory]
        [InlineData(DeepStat)]
        [InlineData(DeepPower)]
        public void Deep_stars_require_six_first_star_ranks_in_their_own_tree(string id)
        {
            var p = NewProfile(5);
            var h = p.Hero(Hero);
            h.Talents["h.cetus.cold"] = 3;
            h.Talents["t.off.edge"] = 3;
            h.Keystone = "h.vesper.key";
            p.Hero("Hero_Cetus").Talents["h.cetus.shell"] = 3;

            Assert.Equal(5, Rules.Tier1Ranks(h, Hero));
            Assert.False(Rules.DeepStarsOpen(p, Hero));
            int free = Rules.FreePoints(p, Hero);
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, id));
            Assert.False(h.Talents.ContainsKey(id));
            Assert.Equal(free, Rules.FreePoints(p, Hero));

            Rules.AddTalentRank(p, Hero, "h.vesper.wall");
            Assert.Equal(6, Rules.Tier1Ranks(h, Hero));
            Assert.True(Rules.DeepStarsOpen(p, Hero));
            Rules.AddTalentRank(p, Hero, id);
            Assert.Equal(1, h.Talents[id]);
            Assert.Equal(free - 2, Rules.FreePoints(p, Hero));
        }

        [Fact]
        public void Deep_ranks_cannot_unlock_more_deep_stars()
        {
            var p = NewProfile(5);
            p.Hero(Hero).Talents[DeepPower] = 3;
            Assert.Equal(8, Rules.TreeRanks(p.Hero(Hero), Hero));
            Assert.Equal(5, Rules.Tier1Ranks(p.Hero(Hero), Hero));
            Assert.False(Rules.DeepStarsOpen(p, Hero));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, DeepStat));
        }

        [Theory]
        [InlineData(1, 6)]
        [InlineData(2, 12)]
        [InlineData(3, 18)]
        public void Ranked_power_nodes_add_power_without_adding_a_stat(int rank, int value)
        {
            var p = NewProfile();
            AddRanks(p, DeepPower, rank);
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(value, b.Get(Power.Bulwark));
            Assert.Equal(9, b.Get(Stat.AttackPct));
            Assert.Equal(0, b.Get(Stat.PowerPct));
            Assert.Equal(24, b.Get(Stat.CritDamagePct));
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(2, 6)]
        [InlineData(3, 9)]
        public void Deep_stat_nodes_keep_stat_rank_scaling(int rank, int value)
        {
            var p = NewProfile();
            AddRanks(p, DeepStat, rank);
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(value, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(0, b.Get(Power.Bulwark));
        }

        [Theory]
        [InlineData(DeepStat)]
        [InlineData(DeepPower)]
        public void Deep_nodes_reject_a_fourth_rank_without_spending_a_point(string id)
        {
            var p = NewProfile();
            AddRanks(p, id, 3);
            int free = Rules.FreePoints(p, Hero);
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, Hero, id));
            Assert.Equal(3, p.Hero(Hero).Talents[id]);
            Assert.Equal(free, Rules.FreePoints(p, Hero));
        }

        [Fact]
        public void Build_clamps_forged_ranks_to_each_nodes_maximum()
        {
            var p = NewProfile();
            p.Hero(Hero).Talents[DeepStat] = 99;
            p.Hero(Hero).Talents[DeepPower] = 99;
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(9, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(18, b.Get(Power.Bulwark));
        }

        [Theory]
        [InlineData(0, 3, 2, 27)]
        [InlineData(10, 2, 2, 33)]
        [InlineData(70, 3, 0, 80)]
        [InlineData(40, 3, 2, 80)]
        public void Ranked_powers_stack_with_relics_before_daily_boosts_and_the_final_cap(int relicValue, int ranks, int dailyId, int expected)
        {
            var p = NewProfile();
            AddRanks(p, DeepPower, ranks);
            if (relicValue > 0)
            {
                var r = new Relic { Uid = "thorns", BaseId = "weapon.blaze_greatsword", Rarity = Rarity.Epic, ItemLevel = 1 };
                r.Powers.Add(new PowerLine(Power.Bulwark, relicValue));
                p.Stash.Add(r);
                Rules.Equip(p, Hero, r.Uid);
            }
            var b = Build.Compute(p, Hero, 0, null, dailyId);
            Assert.Equal(expected, b.Get(Power.Bulwark));
            Assert.True(b.Get(Power.Bulwark) <= Content.PowerCap(Power.Bulwark));
        }

        [Fact]
        public void Build_ignores_locked_deep_ranks_without_erasing_them_and_activates_them_at_six()
        {
            var p = NewProfile(5);
            var h = p.Hero(Hero);
            h.Talents[DeepStat] = 2;
            h.Talents[DeepPower] = 3;
            var b = Build.Compute(p, Hero, 0, null, 2);
            Assert.Equal(0, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(0, b.Get(Power.Bulwark));
            Assert.Equal(9, b.Get(Stat.AttackPct));
            Assert.Equal(16, b.Get(Stat.CritDamagePct));
            Assert.Equal(2, h.Talents[DeepStat]);
            Assert.Equal(3, h.Talents[DeepPower]);

            Rules.AddTalentRank(p, Hero, "h.vesper.wall");
            b = Build.Compute(p, Hero, 0, null, 2);
            Assert.Equal(6, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(27, b.Get(Power.Bulwark));
        }

        [Fact]
        public void Keystone_unlock_counts_deep_ranks_but_still_requires_mastery()
        {
            var p = NewProfile(5);
            var h = p.Hero(Hero);
            Assert.True(Content.TryGetTalent("h.vesper.key", out var key));
            h.Kills = 600;
            Assert.False(Rules.KeystoneUnlocked(p, Hero, key));

            // 保存状態を直接作り、奥の星も到達刻印の段数には含むことを確かめる。
            h.Talents[DeepPower] = 1;
            h.Talents["h.cetus.cold"] = 3;
            h.Talents["t.off.edge"] = 3;
            h.Talents["h.vesper.key2"] = 1;
            Assert.Equal(6, Rules.TreeRanks(h, Hero));
            Assert.Equal(5, Rules.Tier1Ranks(h, Hero));
            Assert.True(Rules.KeystoneUnlocked(p, Hero, key));
            Assert.False(Rules.KeystoneUnlocked(p, "Hero_Cetus", key));

            h.Kills = 0;
            Assert.False(Rules.KeystoneUnlocked(p, Hero, key));
        }

        [Theory]
        [InlineData(5, 0, 0)]
        [InlineData(6, 6, 18)]
        public void Save_load_keeps_deep_ranks_even_when_their_requirement_is_unmet(int firstStarRanks, int statValue, int powerValue)
        {
            var p = NewProfile(firstStarRanks);
            p.Hero(Hero).Talents[DeepStat] = 2;
            p.Hero(Hero).Talents[DeepPower] = 3;
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(notes);
            Assert.Equal(2, q.Hero(Hero).Talents[DeepStat]);
            Assert.Equal(3, q.Hero(Hero).Talents[DeepPower]);
            Assert.Equal(firstStarRanks, Rules.Tier1Ranks(q.Hero(Hero), Hero));
            Assert.Equal(Rules.FreePoints(p, Hero), Rules.FreePoints(q, Hero));
            var b = Build.Compute(q, Hero, 0);
            Assert.Equal(statValue, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(powerValue, b.Get(Power.Bulwark));
        }

        [Fact]
        public void Full_respec_can_remove_first_and_deep_stars_together()
        {
            var p = NewProfile();
            AddRanks(p, DeepStat, 2);
            AddRanks(p, DeepPower, 3);
            p.Hero(Hero).Kills = 600;
            Rules.SetKeystone(p, Hero, "h.vesper.key");
            Rules.ResetTalents(p, Hero);
            Assert.Empty(p.Hero(Hero).Talents);
            Assert.Null(p.Hero(Hero).Keystone);
            Assert.Equal(p.TalentPoints(Hero), Rules.FreePoints(p, Hero));
            Assert.False(Rules.DeepStarsOpen(p, Hero));
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(0, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(0, b.Get(Power.Bulwark));
            Assert.Equal(0, b.Get(Power.Retaliation));
        }
    }
}
