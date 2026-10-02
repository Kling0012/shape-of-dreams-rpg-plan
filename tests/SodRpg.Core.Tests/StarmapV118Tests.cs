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
            TreeTestPaths.Connect(p, Hero, id);
            for (int i = 0; i < ranks; i++) Rules.AddTalentRank(p, Hero, id);
        }


        [Theory]
        [InlineData(1, 6)]
        [InlineData(2, 12)]
        [InlineData(3, 18)]
        public void Ranked_power_nodes_add_power_without_adding_a_stat(int rank, int value)
        {
            var p = NewProfile();
            TreeTestPaths.Connect(p, Hero, DeepPower);
            var before = Build.Compute(p, Hero, 0);
            AddRanks(p, DeepPower, rank);
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(value, b.Get(Power.Bulwark));
            Assert.Equal(before.Stats, b.Stats);
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
            TreeTestPaths.Connect(p, Hero, DeepStat);
            TreeTestPaths.Connect(p, Hero, DeepPower);
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
        public void Build_ignores_disconnected_deep_ranks_until_connected()
        {
            var p = NewProfile(0);
            var h = p.Hero(Hero);
            h.Talents[DeepPower] = 3;
            Assert.Equal(0, Build.Compute(p, Hero, 0).Get(Power.Bulwark));
            Assert.Equal(3, h.Talents[DeepPower]);
            TreeTestPaths.Connect(p, Hero, DeepPower);
            Assert.Equal(18, Build.Compute(p, Hero, 0).Get(Power.Bulwark));
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
            Assert.True(Rules.KeystoneUnlocked(p, Hero, key));
            Assert.False(Rules.KeystoneUnlocked(p, "Hero_Cetus", key));

            h.Kills = 0;
            Assert.False(Rules.KeystoneUnlocked(p, Hero, key));
        }

        [Fact]
        public void Save_load_keeps_connected_deep_ranks()
        {
            var p = NewProfile();
            AddRanks(p, DeepStat, 2);
            AddRanks(p, DeepPower, 3);
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p), notes);
            Assert.Empty(notes);
            Assert.Equal(2, q.Hero(Hero).Talents[DeepStat]);
            Assert.Equal(3, q.Hero(Hero).Talents[DeepPower]);
            Assert.Equal(Rules.FreePoints(p, Hero), Rules.FreePoints(q, Hero));
            Assert.Equal(6, Build.Compute(q, Hero, 0).Get(Stat.AttackSpeedPct));
            Assert.Equal(18, Build.Compute(q, Hero, 0).Get(Power.Bulwark));
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
            var b = Build.Compute(p, Hero, 0);
            Assert.Equal(0, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(0, b.Get(Power.Bulwark));
            Assert.Equal(0, b.Get(Power.Retaliation));
        }
    }
}
