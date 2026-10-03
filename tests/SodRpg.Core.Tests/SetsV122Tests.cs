using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.22：新しい枠（頭・手・足）を使うセットを12種追加。</summary>
    public class SetsV122Tests
    {
        [Fact]
        public void There_are_48_sets_and_all_use_the_new_slots()
        {
            Assert.Equal(48, Content.Sets.Count); // Includes all reviewed P37 content.
            int usesNew = 0;
            foreach (var set in Content.Sets)
            {
                var slots = Content.Uniques.Where(u => u.SetId == set.Id).Select(u => Content.GetBase(u.BaseId).Slot).ToList();
                if (slots.Any(s => s == Slot.Head || s == Slot.Hands || s == Slot.Feet)) usesNew++;
                Assert.Equal(2, set.TwoPiece.Length);
                Assert.InRange(set.ThreePiece.Length, 2, 3); // v1.29 のセットは3効果
                foreach (var pw in set.ThreePiece) Assert.True(pw.Value <= Content.PowerCap(pw.Power), set.Id);
            }
            Assert.Equal(48, usesNew); // 6部位化で全セットが頭・手・足を含む
        }

        [Fact]
        public void A_new_slot_set_completes_and_grants_its_three_piece_powers()
        {
            var p = Profile.CreateNew(22);
            var set = Content.Sets.First(s => s.Id == "set.gale");
            ulong seed = 1;
            foreach (var u in Content.Uniques.Where(u => u.SetId == set.Id))
            {
                var r = Loot.RollUnique(new Rng(seed++), u, 5); // 部位ごとに別の乱数（Uid が重ならないように）
                p.Stash.Add(r);
                Rules.Equip(p, "Hero_A", r.Uid);
            }
            var b = Build.Compute(p, "Hero_A", 0);
            foreach (var pw in set.ThreePiece) Assert.True(b.Get(pw.Power) >= pw.Value, pw.Power.ToString());
            Assert.Equal(1, Feats.Progress(p, Feats.All.First(f => f.Kind == FeatKind.SetsCompleted)));
        }
    }
}
