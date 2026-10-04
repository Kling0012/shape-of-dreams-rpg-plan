using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.27：星図でエッセンスの枠を増やす（アイデンティティ記憶と回避）。仕組みだけを試す。</summary>
    public class EssenceSlotV127Tests
    {
        [Fact]
        public void New_stats_keep_saved_ids_and_caps_of_one()
        {
            Assert.Equal(18, (int)Stat.EssenceSlotIdentity); // 並びは保存の値なので変えない
            Assert.Equal(19, (int)Stat.EssenceSlotMovement);
            Assert.Equal(1, Content.StatCap(Stat.EssenceSlotIdentity));
            Assert.Equal(1, Content.StatCap(Stat.EssenceSlotMovement));
        }

        [Fact]
        public void New_stats_travel_in_the_build_string_and_clamp_to_one()
        {
            var b = new Build();
            b.Stats[Stat.EssenceSlotIdentity] = 1;
            b.Stats[Stat.EssenceSlotMovement] = 1;
            b.Stats[Stat.AttackPct] = 12;
            var back = Build.Decode(b.Encode());
            Assert.NotNull(back);
            Assert.Equal(1, back.Get(Stat.EssenceSlotIdentity));
            Assert.Equal(1, back.Get(Stat.EssenceSlotMovement));
            Assert.Equal(12, back.Get(Stat.AttackPct));

            // 大きすぎる値・負の値は上限で切る（他のクライアントからの値を信用しすぎない）
            var big = new Build();
            big.Stats[Stat.EssenceSlotIdentity] = 5;
            big.Stats[Stat.EssenceSlotMovement] = -3;
            var back2 = Build.Decode(big.Encode());
            Assert.Equal(1, back2.Get(Stat.EssenceSlotIdentity));
            Assert.Equal(-1, back2.Get(Stat.EssenceSlotMovement));
        }

        [Fact]
        public void Added_slots_clamp_to_one_even_if_both_apex_stars_are_taken()
        {
            var b = new Build();
            b.Stats[Stat.EssenceSlotIdentity] = 3; // 2本の頂点を両方取っても +1 まで
            b.Stats[Stat.EssenceSlotMovement] = 1;
            Assert.Equal(1, EssenceSlots.AddedFrom(b, Stat.EssenceSlotIdentity));
            Assert.Equal(1, EssenceSlots.AddedFrom(b, Stat.EssenceSlotMovement));
            Assert.Equal(0, EssenceSlots.ClampAdded(0));
            Assert.Equal(1, EssenceSlots.ClampAdded(1));
            Assert.Equal(1, EssenceSlots.ClampAdded(2));
            Assert.Equal(0, EssenceSlots.ClampAdded(-1));
            Assert.Equal(0, EssenceSlots.AddedFrom(new Build(), Stat.EssenceSlotIdentity)); // 無ければ0
        }

        [Fact]
        public void Overflow_gems_are_counted_when_the_cap_shrinks()
        {
            Assert.Equal(0, EssenceSlots.Overflow(2, 2));
            Assert.Equal(0, EssenceSlots.Overflow(1, 3)); // 枠が増えるときは落とさない
            Assert.Equal(1, EssenceSlots.Overflow(3, 2)); // 星を外して枠が1つ減り、3つ目がはみ出す
            Assert.Equal(2, EssenceSlots.Overflow(3, 1));
        }
    }
}
