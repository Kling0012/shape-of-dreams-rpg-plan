using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// リレー系の装備選択（RelayMemorySelection）。
    /// ビスマスは Q と R で同じ候補（St_QR_*）を共有し、同じ本を2つ装備できる
    /// （docs/specs/v1.27-traveler-kits.md・本体 HeroLoadoutData の検証は添字の範囲のみ）。
    /// その状態で例外を投げると、ホストの EntityAbility.SetAbility パッチ経由で
    /// HeroSkill.OnLateStartServer の残りのロードアウト装備が止まり、
    /// 記憶スロットが空のままになる。最初の枠で縮退させる選択を固定する。
    /// </summary>
    public class RelayMemorySelectionTests
    {
        private static List<KeyValuePair<string, T>> Pairs<T>(params (string Memory, T Value)[] slots)
        {
            var list = new List<KeyValuePair<string, T>>(slots.Length);
            foreach (var slot in slots) list.Add(new KeyValuePair<string, T>(slot.Memory, slot.Value));
            return list;
        }

        [Fact]
        public void BismuthDuplicateBooks_KeepFirstSlotAndReportDuplicate()
        {
            var q = new object();
            var r = new object();
            var identity = new object();
            var selected = new Dictionary<string, object>();

            RelayMemorySelection.SelectFirstSlotPerMemory(
                Pairs(("St_QR_Innocence", q), ("St_QR_Innocence", r), ("St_D_PrismaticEyes", identity)),
                selected, out bool hadDuplicates);

            Assert.True(hadDuplicates);
            Assert.Equal(2, selected.Count);
            Assert.Same(q, selected["St_QR_Innocence"]);
            Assert.Same(identity, selected["St_D_PrismaticEyes"]);
        }

        [Fact]
        public void DistinctMemories_SelectAllWithoutDuplicates()
        {
            var q = new object();
            var r = new object();
            var selected = new Dictionary<string, object>();

            RelayMemorySelection.SelectFirstSlotPerMemory(
                Pairs(("St_QR_Innocence", q), ("St_QR_ValiantHeart", r)),
                selected, out bool hadDuplicates);

            Assert.False(hadDuplicates);
            Assert.Equal(2, selected.Count);
            Assert.Same(q, selected["St_QR_Innocence"]);
            Assert.Same(r, selected["St_QR_ValiantHeart"]);
        }

        [Fact]
        public void EmptyEquipment_SelectsNothing()
        {
            var selected = new Dictionary<string, object>();

            RelayMemorySelection.SelectFirstSlotPerMemory(Pairs<object>(), selected, out bool hadDuplicates);

            Assert.False(hadDuplicates);
            Assert.Empty(selected);
        }

        [Fact]
        public void DuplicateDroppedMemoriesInWAndE_KeepFirstSlot()
        {
            var w = new object();
            var e = new object();
            var selected = new Dictionary<string, object>();

            RelayMemorySelection.SelectFirstSlotPerMemory(
                Pairs(("St_Q_StarFall", w), ("St_Q_StarFall", e)),
                selected, out bool hadDuplicates);

            Assert.True(hadDuplicates);
            Assert.Single(selected);
            Assert.Same(w, selected["St_Q_StarFall"]);
        }
    }
}
