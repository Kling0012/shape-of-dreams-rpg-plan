using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.32 家系（Family）による特性・固有効果の抽選の色づけ。</summary>
    public class FamilyRollV132Tests
    {
        // ---- v1.31 時点の抽選の凍結コピー（回帰の基準。ここは変更しないこと）----
        private static Relic OldRollBaseRelic(Rng rng, BaseDef baseDef, Rarity rarity, int itemLevel)
        {
            var r = new Relic
            {
                Uid = rng.NextUid(),
                BaseId = baseDef.Id,
                Rarity = rarity,
                ItemLevel = Loot.ClampLevel(itemLevel),
            };
            var used = new HashSet<Stat> { baseDef.ImplicitStat };
            int count = Content.AffixCount(rarity);
            while (r.Affixes.Count < count)
            {
                var line = OldRollAffix(rng, baseDef.Slot, r.Rarity, r.ItemLevel, used);
                if (line == null) break;
                used.Add(line.Stat);
                r.Affixes.Add(line);
            }
            var pool = Content.PowerPool(baseDef.Slot);
            if (rarity == Rarity.Epic)
            {
                var pr = pool[rng.Range(0, pool.Count - 1)];
                r.Powers.Add(new PowerLine(pr.Power, rng.Range(pr.Min, pr.Max)));
                OldAddMinorPower(rng, r, pool);
            }
            else if (rarity == Rarity.Rare)
            {
                OldAddMinorPower(rng, r, pool);
            }
            return r;
        }

        private static void OldAddMinorPower(Rng rng, Relic r, IReadOnlyList<PowerRange> pool)
        {
            var candidates = new List<PowerRange>();
            foreach (var pr in pool)
                if (Content.PowerAllowedForRarity(pr.Power, r.Rarity) && !r.Powers.Exists(x => x.Power == pr.Power)) candidates.Add(pr);
            if (candidates.Count == 0) return;
            var pick = candidates[rng.Range(0, candidates.Count - 1)];
            int top = Math.Max(pick.Min, pick.Min + (pick.Max - pick.Min) / 2);
            r.Powers.Add(new PowerLine(pick.Power, rng.Range(pick.Min, top)));
        }

        private static StatLine OldRollAffix(Rng rng, Slot slot, Rarity rarity, int itemLevel, ICollection<Stat> exclude)
        {
            var pool = Content.AffixPool(slot);
            int total = 0;
            foreach (var a in pool)
                if (!exclude.Contains(a.Stat) && rarity >= a.MinRarity) total += a.Weight;
            if (total <= 0) return null;
            int x = rng.Range(0, total - 1);
            foreach (var a in pool)
            {
                if (exclude.Contains(a.Stat) || rarity < a.MinRarity) continue;
                if (x < a.Weight)
                {
                    int raw = rng.Range(a.Min, a.Max);
                    int level = Content.ScalesWithItemLevel(a.Stat) ? Content.LevelScalePct(itemLevel) : 100;
                    int pct = Content.RarityValuePct(rarity) * level / 100;
                    return new StatLine(a.Stat, Relic.Scale(raw, pct));
                }
                x -= a.Weight;
            }
            return null;
        }

        private static string Describe(Relic r) =>
            r.Uid + "|" + r.BaseId + "|" + r.Rarity + "|" + r.ItemLevel
            + "|" + string.Join(",", r.Affixes.Select(a => a.Stat + "=" + a.Value))
            + "|" + string.Join(",", r.Powers.Select(p => p.Power + "=" + p.Value));

        private static readonly Rarity[] LowRarities = { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic };

        [Fact]
        public void Plain_bases_roll_exactly_like_before()
        {
            var plain = Content.Bases.Where(b => b.Family == Family.Plain).ToList();
            Assert.NotEmpty(plain);
            for (int i = 0; i < 1200; i++)
            {
                var b = plain[(i * 7) % plain.Count];
                var rarity = LowRarities[i % LowRarities.Length];
                int level = 1 + (i * 13) % Content.MaxItemLevel;
                var oldRng = new Rng((ulong)(1000 + i));
                var newRng = new Rng((ulong)(1000 + i));
                var oldRelic = OldRollBaseRelic(oldRng, b, rarity, level);
                var newRelic = Loot.RollBaseRelic(newRng, b, rarity, level);
                Assert.Equal(Describe(oldRelic), Describe(newRelic));
                Assert.Equal(oldRng.State, newRng.State); // 乱数の消費も同じ
            }
        }

        [Fact]
        public void Uncommon_and_rare_never_roll_percent_attributes_or_conditional_attribute_powers()
        {
            var families = (Family[])Enum.GetValues(typeof(Family));
            var slots = (Slot[])Enum.GetValues(typeof(Slot));
            int checkedRelics = 0;
            foreach (var family in families)
                foreach (var slot in slots)
                    foreach (var rarity in new[] { Rarity.Uncommon, Rarity.Rare })
                    {
                        var b = new BaseDef("test." + family + "." + slot, slot, Line.Offense, new Txt("試験", "Test"), Stat.Haste, 4, family);
                        for (int seed = 0; seed < 150; seed++)
                        {
                            var r = Loot.RollBaseRelic(new Rng((ulong)(seed * 31 + 5)), b, rarity, 1 + seed % Content.MaxItemLevel);
                            Assert.DoesNotContain(r.Affixes, a => a.Stat == Stat.AttackPct || a.Stat == Stat.PowerPct);
                            Assert.DoesNotContain(r.Powers, p => NewPowersV129.IsConditionalAttribute(p.Power));
                            checkedRelics++;
                        }
                    }
            Assert.Equal(families.Length * slots.Length * 2 * 150, checkedRelics);
        }

        [Fact]
        public void Preferred_stats_and_powers_appear_noticeably_more_often_for_a_coloured_family()
        {
            foreach (var family in new[] { Family.Frost, Family.Flame, Family.Guard, Family.Gale })
            {
                // その家系の好む固有効果がもっとも多く入っている枠で比べる
                var slot = ((Slot[])Enum.GetValues(typeof(Slot)))
                    .OrderByDescending(sl => Content.PowerPool(sl).Count(pr => FamilyPrefs.PrefersPower(family, pr.Power))).First();
                var plainBase = new BaseDef("test.plain", slot, Line.Offense, new Txt("試験", "Test"), Stat.PowerFlat, 4, Family.Plain);
                var colouredBase = new BaseDef("test.coloured", slot, Line.Offense, new Txt("試験", "Test"), Stat.PowerFlat, 4, family);
                double plainStat = 0, colouredStat = 0, plainPower = 0, colouredPower = 0;
                int n = 4000;
                for (int i = 0; i < n; i++)
                {
                    var rp = Loot.RollBaseRelic(new Rng((ulong)(77 + i)), plainBase, Rarity.Rare, 20);
                    var rc = Loot.RollBaseRelic(new Rng((ulong)(77 + i)), colouredBase, Rarity.Rare, 20);
                    plainStat += rp.Affixes.Count(a => FamilyPrefs.PrefersStat(family, a.Stat));
                    colouredStat += rc.Affixes.Count(a => FamilyPrefs.PrefersStat(family, a.Stat));
                    plainPower += rp.Powers.Count(p => FamilyPrefs.PrefersPower(family, p.Power));
                    colouredPower += rc.Powers.Count(p => FamilyPrefs.PrefersPower(family, p.Power));
                }
                Assert.True(colouredStat > plainStat * 1.1, $"{family}: stats {colouredStat} vs {plainStat}");
                Assert.True(colouredPower > plainPower * 1.4, $"{family}: powers {colouredPower} vs {plainPower}");
            }
        }

        [Fact]
        public void Preferences_are_tendencies_not_bans_and_tables_are_well_formed()
        {
            foreach (var family in (Family[])Enum.GetValues(typeof(Family)))
            {
                var stats = FamilyPrefs.PreferredStats(family);
                var powers = FamilyPrefs.PreferredPowers(family);
                if (family == Family.Plain) { Assert.Empty(stats); Assert.Empty(powers); Assert.Null(FamilyPrefs.Label(family)); continue; }
                Assert.InRange(stats.Count, 4, 6);
                Assert.InRange(powers.Count, 4, 6);
                Assert.Equal(stats.Count, stats.Distinct().Count());
                Assert.Equal(powers.Count, powers.Distinct().Count());
                Assert.All(powers, p => Assert.True(Content.IsPowerDroppable(p)));
                Assert.NotNull(FamilyPrefs.Label(family));
            }
            // 好まない能力値も出る
            var b = new BaseDef("test.frost", Slot.Weapon, Line.Offense, new Txt("試験", "Test"), Stat.PowerFlat, 4, Family.Frost);
            var seen = new HashSet<Stat>();
            for (int i = 0; i < 500; i++)
                foreach (var a in Loot.RollBaseRelic(new Rng((ulong)i), b, Rarity.Rare, 20).Affixes) seen.Add(a.Stat);
            Assert.Contains(seen, s => !FamilyPrefs.PrefersStat(Family.Frost, s));
        }
    }
}
