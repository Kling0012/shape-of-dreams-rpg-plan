using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.29：内容を倍にしたデータの形（数・一意性・両言語の名前と説明・土台の基礎能力の種類）。</summary>
    public class DataV129Tests
    {
        [Fact]
        public void Bases_doubled_to_360_with_60_per_slot_and_unique_ids_and_names()
        {
            Assert.Equal(360, Content.Bases.Count);
            Assert.Equal(Content.Bases.Count, Content.Bases.Select(b => b.Id).Distinct().Count());
            Assert.Equal(Content.Bases.Count, Content.Bases.Select(b => b.Name.Ja).Distinct().Count());
            Assert.Equal(Content.Bases.Count, Content.Bases.Select(b => b.Name.En).Distinct().Count());
            foreach (var slot in Content.SlotOrder)
            {
                var bases = Content.Bases.Where(b => b.Slot == slot).ToList();
                Assert.Equal(60, bases.Count);
                // どの枠も3系統がそろっている（狙い系統をどれにしても外れがない）
                foreach (Line line in Enum.GetValues(typeof(Line)))
                    Assert.True(bases.Count(b => b.Line == line) >= 5, $"{slot}/{line}");
            }
        }

        [Fact]
        public void Base_implicits_stay_modest_and_never_use_attack_or_power_percent()
        {
            foreach (var b in Content.Bases)
            {
                Assert.NotEqual(Stat.AttackPct, b.ImplicitStat);
                Assert.NotEqual(Stat.PowerPct, b.ImplicitStat);
                Assert.True(b.ImplicitValue > 0, b.Id);
                Assert.True(b.ImplicitValue <= Content.StatCap(b.ImplicitStat), b.Id);
                Assert.False(string.IsNullOrWhiteSpace(b.Name.Ja), b.Id);
                Assert.False(string.IsNullOrWhiteSpace(b.Name.En), b.Id);
            }
        }

        [Fact]
        public void Pacts_doubled_to_40_with_distinct_ids_names_and_both_languages()
        {
            Assert.Equal(40, Pacts.All.Count);
            Assert.Equal(Pacts.All.Count, Pacts.All.Select(p => p.Id).Distinct().Count());
            Assert.Equal(Pacts.All.Count, Pacts.All.Select(p => p.Name.Ja).Distinct().Count());
            Assert.Equal(Pacts.All.Count, Pacts.All.Select(p => p.Name.En).Distinct().Count());
            foreach (bool ja in new[] { true, false })
            {
                Loc.Japanese = ja;
                foreach (var d in Pacts.All)
                {
                    Assert.False(string.IsNullOrWhiteSpace(d.Name.ToString()), d.Id.ToString());
                    Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Id.ToString());
                }
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Daily_dreams_doubled_to_60_with_distinct_ids_names_and_a_theme()
        {
            Assert.Equal(60, DailyDream.All.Count);
            Assert.Equal(DailyDream.All.Count, DailyDream.All.Select(d => d.Id).Distinct().Count());
            Assert.Equal(DailyDream.All.Count, DailyDream.All.Select(d => d.Name.Ja).Distinct().Count());
            Assert.Equal(DailyDream.All.Count, DailyDream.All.Select(d => d.Name.En).Distinct().Count());
            foreach (var d in DailyDream.All)
            {
                bool theme = d.BoostedPowers.Length > 0 || d.FeaturedLine.HasValue || d.DropBonus > 0
                    || d.ShardMult > 1 || d.XpMult > 1 || d.BountyMult > 1 || d.NightmareMult > 1;
                Assert.True(theme, d.Id.ToString());
            }
            foreach (bool ja in new[] { true, false })
            {
                Loc.Japanese = ja;
                foreach (var d in DailyDream.All) Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Id.ToString());
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Feats_doubled_to_90_and_stay_grouped_by_kind_with_ascending_targets()
        {
            Assert.Equal(90, Feats.All.Count);
            Assert.Equal(Feats.All.Count, Feats.All.Select(f => f.Id).Distinct().Count());
            Assert.Equal(Feats.All.Count, Feats.All.Select(f => f.Name.Ja).Distinct().Count());
            Assert.Equal(Feats.All.Count, Feats.All.Select(f => f.Name.En).Distinct().Count());
            var seen = new HashSet<FeatKind>();
            var last = Feats.All[0].Kind;
            int lastTarget = 0;
            foreach (var f in Feats.All)
            {
                if (f.Kind != last)
                {
                    seen.Add(last);
                    Assert.DoesNotContain(f.Kind, seen); // 種類ごとにまとまっている（Feats.Check の前提）
                    last = f.Kind;
                    lastTarget = 0;
                }
                Assert.True(f.Target > lastTarget, f.Id);
                lastTarget = f.Target;
                Assert.True(f.RewardShards > 0, f.Id);
                Assert.False(string.IsNullOrWhiteSpace(f.Name.Ja), f.Id);
                Assert.False(string.IsNullOrWhiteSpace(f.Name.En), f.Id);
            }
        }

        [Fact]
        public void Workshop_has_six_upgrades_and_the_new_ones_move_their_values()
        {
            Assert.Equal(6, Workshop.All.Count);
            Assert.Equal(Workshop.All.Count, Workshop.All.Select(d => d.Key).Distinct().Count());
            foreach (var d in Workshop.All)
            {
                Assert.NotEmpty(d.Costs);
                Assert.False(string.IsNullOrWhiteSpace(d.Name.Ja), d.Key);
                Assert.False(string.IsNullOrWhiteSpace(d.Name.En), d.Key);
                Assert.False(string.IsNullOrWhiteSpace(d.Description.Ja), d.Key);
                Assert.False(string.IsNullOrWhiteSpace(d.Description.En), d.Key);
            }

            var p = Profile.CreateNew(129);
            p.AddMaterial(Materials.Shard, 100000);
            p.AddMaterial(Materials.Tuning, 1000);
            Assert.Equal(25, Workshop.EchoPercent(p));
            Assert.Equal(Content.RoomsToRecoverLost, Workshop.RoomsToRecover(p));
            Assert.Equal(Pacts.Offered, Workshop.PactsOffered(p));

            Rules.BuyUpgrade(p, Upgrade.EchoLantern);
            Assert.Equal(30, Workshop.EchoPercent(p));
            Rules.BuyUpgrade(p, Upgrade.LostMap);
            Assert.Equal(2, Workshop.RoomsToRecover(p));
            Rules.BuyUpgrade(p, Upgrade.PactStars);
            Assert.Equal(Pacts.Offered + 1, Workshop.PactsOffered(p));
        }
    }
}
