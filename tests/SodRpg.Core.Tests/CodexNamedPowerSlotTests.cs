using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class CodexNamedPowerSlotTests
    {
        /// <summary>銘品だけがその部位で持つ効果も、図鑑の効果の部位に入る（部位の絞り込みで消えない）。</summary>
        [Fact]
        public void Power_slot_mask_includes_named_item_carriers()
        {
            var powers = CodexQuery.Entries(CodexCategory.Powers);
            int checkedCount = 0;
            foreach (var n in NamedItems.All)
            {
                Assert.True(Content.TryGetBase(n.BaseId, out var b));
                foreach (var pw in n.Powers)
                {
                    var entry = powers.FirstOrDefault(e => e.Power == pw.Power);
                    Assert.NotNull(entry);
                    Assert.True((entry.SlotMask & (1 << (int)b.Slot)) != 0, $"{n.Id} {pw.Power} {b.Slot}");
                    checkedCount++;
                }
            }
            Assert.True(checkedCount > 0);
        }
    }
}
