using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.22：新しい枠（頭・手・足）を使うセットを12種追加。</summary>
    public class SetsV122Tests
    {
        [Fact]
        public void There_are_48_generic_sets_and_all_use_the_new_slots()
        {
            Assert.Equal(62, Content.Sets.Count); // 48 generic sets + all 14 #48 boss sets.
            int boss = 0, usesNew = 0;
            foreach (var set in Content.Sets)
            {
                if (set.BossTypeName != null) { boss++; continue; } // boss sets have no stat/power rows
                var slots = Content.Uniques.Where(u => u.SetId == set.Id).Select(u => Content.GetBase(u.BaseId).Slot).ToList();
                if (slots.Any(s => s == Slot.Head || s == Slot.Hands || s == Slot.Feet)) usesNew++;
                Assert.Equal(2, set.TwoPiece.Length);
                Assert.InRange(set.ThreePiece.Length, 2, 3); // v1.29 のセットは3効果
                foreach (var pw in set.ThreePiece) Assert.True(pw.Value <= Content.PowerCap(pw.Power), set.Id);
            }
            Assert.Equal(14, boss);
            Assert.Equal(48, usesNew); // 6部位化で全セットが頭・手・足を含む
        }

    }
}
