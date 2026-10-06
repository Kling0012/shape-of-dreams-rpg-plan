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

    }
}
