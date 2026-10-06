using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.27：星図でエッセンスの枠を増やす（アイデンティティ記憶と回避）。仕組みだけを試す。</summary>
    public class EssenceSlotV127Tests
    {

        [Fact]
        public void Slot_bonuses_round_trip_and_negative_bonuses_cannot_remove_native_slots()
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
            big.Stats[Stat.EssenceSlotIdentity] = int.MaxValue;
            big.Stats[Stat.EssenceSlotMovement] = int.MinValue;
            var back2 = Build.Decode(big.Encode());
            Assert.Equal(1, back2.Get(Stat.EssenceSlotIdentity));
            Assert.Equal(0, back2.Get(Stat.EssenceSlotMovement));
        }

        [Theory]
        [InlineData(int.MaxValue, int.MaxValue, 1, 1)]
        [InlineData(int.MinValue, int.MaxValue, 0, 1)]
        [InlineData(int.MaxValue, int.MinValue, 1, 0)]
        public void Hand_built_and_wire_builds_share_the_combined_slot_budget(int identity, int movement, int expectedIdentity, int expectedMovement)
        {
            var build = new Build();
            build.Stats[Stat.EssenceSlotIdentity] = identity;
            build.Stats[Stat.EssenceSlotMovement] = movement;

            Assert.Equal(expectedIdentity, EssenceSlots.AddedFrom(build, Stat.EssenceSlotIdentity));
            Assert.Equal(expectedMovement, EssenceSlots.AddedFrom(build, Stat.EssenceSlotMovement));
            var decoded = Build.Decode(build.Encode());
            Assert.NotNull(decoded);
            Assert.Equal(expectedIdentity, decoded.Get(Stat.EssenceSlotIdentity));
            Assert.Equal(expectedMovement, decoded.Get(Stat.EssenceSlotMovement));
        }

        [Fact]
        public void Existing_wire_stat_ids_remain_readable_and_excess_slots_are_silently_clamped()
        {
            var decoded = Build.Decode("h:0;s:18=9,19=9");
            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.Get(Stat.EssenceSlotIdentity));
            Assert.Equal(1, decoded.Get(Stat.EssenceSlotMovement));
        }


    }
}
