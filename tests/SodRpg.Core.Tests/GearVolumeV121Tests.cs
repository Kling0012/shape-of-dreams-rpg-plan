using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.21：各枠の装備を大幅に増やす（土台各枠25、固有品各枠40以上）。</summary>
    public class GearVolumeV121Tests
    {
        [Fact]
        public void Every_slot_has_25_bases_and_at_least_40_general_uniques()
        {
            foreach (var slot in Content.SlotOrder)
            {
                Assert.Equal(60, Content.BasesFor(slot).Count());
                int uniques = Content.Uniques.Count(u => u.SetId == null && Content.GetBase(u.BaseId).Slot == slot);
                Assert.True(uniques >= 40, $"{slot}: {uniques}");
            }
        }

        [Fact]
        public void Every_slot_has_13_affix_kinds()
        {
            foreach (var slot in Content.SlotOrder)
            {
                var pool = Content.AffixPool(slot);
                Assert.True(pool.Count >= 13, slot.ToString());
                Assert.Equal(pool.Count, pool.Select(a => a.Stat).Distinct().Count());
                // v1.27：どの枠でも攻撃力・魔力の両方を引ける（魔力で伸びる旅人が多いため）
                Assert.Contains(pool, a => a.Stat == Stat.AttackPct);
                Assert.Contains(pool, a => a.Stat == Stat.PowerPct);
            }
        }

        [Fact]
        public void Every_base_has_at_least_one_unique()
        {
            var used = new HashSet<string>(Content.Uniques.Where(u => u.SetId == null).Select(u => u.BaseId));
            // v1.29 の2段目で新しい土台にも固有品が付く。それまでは v1.28 までの土台に保証する。
            foreach (var b in Content.Bases.Take(Content.PreV129BaseCount)) Assert.True(used.Contains(b.Id), b.Id);
        }

        [Fact]
        public void Names_ids_and_power_pairs_are_distinct_and_within_caps()
        {
            Assert.Equal(Content.Uniques.Count, Content.Uniques.Select(u => u.Id).Distinct().Count());
            Assert.Equal(Content.Uniques.Count, Content.Uniques.Select(u => u.Name.Ja).Distinct().Count());
            Assert.Equal(Content.Bases.Count, Content.Bases.Select(b => b.Name.Ja).Distinct().Count());
            foreach (var slot in Content.SlotOrder)
            {
                var pairs = new HashSet<string>();
                foreach (var u in Content.Uniques.Where(x => x.SetId == null && Content.GetBase(x.BaseId).Slot == slot))
                {
                    var ps = u.Powers.Select(p => p.Power).OrderBy(p => p).ToList();
                    Assert.Equal(2, ps.Distinct().Count());
                    Assert.True(pairs.Add(string.Join("+", ps)), $"{slot} repeats {string.Join("+", ps)} ({u.Id})");
                    foreach (var p in u.Powers)
                    {
                        int cap = Content.PowerCap(p.Power);
                        Assert.True(cap == 0 || p.Value <= cap, $"{u.Id} {p.Power} {p.Value} > {cap}");
                    }
                }
            }
        }
    }
}
