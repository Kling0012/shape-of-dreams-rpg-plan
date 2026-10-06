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
