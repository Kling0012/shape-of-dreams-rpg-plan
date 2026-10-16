using System;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>同行補正（CoopCatchup.cs）: 同等・優位は補正なし、生成定数どおりの倍率と上限、ソロと設定 OFF。</summary>
    public class CoopCatchupTests
    {
        private static double Constant(string key) => PressureBalanceTests.Number("catchup", key);
        private static readonly Build Veteran = new Build { DreamLevel = 30, SpentStarPoints = 500 };
        private static readonly Build Newcomer = new Build { DreamLevel = 1, SpentStarPoints = 0 };
        private static Build Player(int level, int spent) => new Build { DreamLevel = level, SpentStarPoints = spent };
        private static DreamPressure RoomOf(params Build[] builds) => DreamPressure.Average(builds);

        [Fact]
        public void Player_at_or_above_room_pressure_gets_no_correction()
        {
            // 部屋の圧と同じ成長の者は r=1、平均より上の熟練者は r<1。どちらも補正しない。
            var matched = DreamPressure.Average(new[] { Newcomer, Newcomer });
            Assert.False(CoopCatchup.ForPlayer(matched, 1, 0, 2).IsActive);

            var room = DreamPressure.Average(new[] { Veteran, Newcomer });
            var veteran = CoopCatchup.ForPlayer(room, Veteran.DreamLevel, Veteran.SpentStarPoints, 2);
            Assert.False(veteran.IsActive);
            Assert.Equal(1, veteran.OutgoingMultiplier, 10);
            Assert.Equal(1, veteran.IncomingMultiplier, 10);
        }

        [Fact]
        public void Ratio_at_or_below_the_threshold_gets_no_correction()
        {
            // 個人（夢1・星0）に対する部屋の圧の比が閾値（生成定数 1.05）を超えなければ補正しない。
            // 深度・道標・無限段は個人にも同じ係数で掛かるため、比は Build の差だけで決まる。
            var below = RoomOf(Player(5, 0), Player(6, 0), Player(6, 0), Player(6, 0)); // 比約 1.045
            var barely = RoomOf(Player(5, 0), Player(6, 0), Player(5, 0), Player(6, 0)); // 比約 1.03
            var above = RoomOf(Player(5, 0), Player(6, 0), Player(5, 0), Player(6, 3)); // 比約 1.051
            Assert.False(CoopCatchup.ForPlayer(below, 1, 0, 4).IsActive);
            Assert.False(CoopCatchup.ForPlayer(barely, 1, 0, 4).IsActive);
            var active = CoopCatchup.ForPlayer(above, 1, 0, 4);
            Assert.True(active.IsActive);
            Assert.True(active.OutgoingMultiplier > 1);
            Assert.True(active.IncomingMultiplier < 1);
        }

        [Fact]
        public void Multipliers_follow_the_generated_constants_below_the_caps()
        {
            // 与ダメ ×min(r,4)^0.85・被ダメ ×(1/min(r_dmg,3))^0.7。定数は tools/balance/pressure.json 生成値。
            var room = DreamPressure.Average(new[] { new Build { DreamLevel = 15, SpentStarPoints = 100 }, Newcomer });
            var catchup = CoopCatchup.ForPlayer(room, Newcomer.DreamLevel, Newcomer.SpentStarPoints, 2);
            double rHealth = PressureBalanceTests.Health(8, 50) / PressureBalanceTests.Health(1, 0);
            double rDamage = PressureBalanceTests.Damage(8, 50) / PressureBalanceTests.Damage(1, 0);
            Assert.InRange(rHealth, Constant("ratioThreshold"), Constant("outgoingRatioCap")); // 上限に触れない範囲
            Assert.InRange(rDamage, Constant("ratioThreshold"), Constant("incomingRatioCap"));
            Assert.Equal(Math.Pow(rHealth, Constant("outgoingExponent")), catchup.OutgoingMultiplier, 10);
            Assert.Equal(Math.Pow(1 / rDamage, Constant("incomingExponent")), catchup.IncomingMultiplier, 10);
            // 熟練者側は与ダメも被ダメも変わらない。
            var veteran = CoopCatchup.ForPlayer(room, 15, 100, 2);
            Assert.Equal(1, veteran.OutgoingMultiplier, 10);
            Assert.Equal(1, veteran.IncomingMultiplier, 10);
        }

        [Fact]
        public void Extreme_gaps_stop_at_the_caps()
        {
            // 差が上限を超えても倍率は頭打ち（9人の最大成長者と1人の新規参加者）。
            var room = DreamPressure.Average(Enumerable.Repeat(Veteran, 9).Concat(new[] { Newcomer }).ToArray());
            var capped = CoopCatchup.ForPlayer(room, Newcomer.DreamLevel, Newcomer.SpentStarPoints, 10);
            Assert.Equal(Math.Pow(Constant("outgoingRatioCap"), Constant("outgoingExponent")), capped.OutgoingMultiplier, 10);
            Assert.Equal(Math.Pow(1 / Constant("incomingRatioCap"), Constant("incomingExponent")), capped.IncomingMultiplier, 10);
            // さらに差が開いても同じ値。
            var further = DreamPressure.Average(Enumerable.Repeat(Veteran, 99).Concat(new[] { Newcomer }).ToArray());
            var wider = CoopCatchup.ForPlayer(further, Newcomer.DreamLevel, Newcomer.SpentStarPoints, 100);
            Assert.Equal(capped.OutgoingMultiplier, wider.OutgoingMultiplier, 10);
            Assert.Equal(capped.IncomingMultiplier, wider.IncomingMultiplier, 10);
        }

        [Fact]
        public void Solo_parties_never_get_a_correction()
        {
            // 部屋の圧が離れていても、参加者が1人なら補正しない。
            var room = DreamPressure.Average(new[] { Veteran, Newcomer });
            Assert.False(CoopCatchup.ForPlayer(room, Newcomer.DreamLevel, Newcomer.SpentStarPoints, 1).IsActive);
            Assert.False(CoopCatchup.ForPlayer(room, Newcomer.DreamLevel, Newcomer.SpentStarPoints, 0).IsActive);
        }

        [Fact]
        public void Disabled_setting_leaves_damage_untouched()
        {
            var room = DreamPressure.Average(new[] { new Build { DreamLevel = 15, SpentStarPoints = 100 }, Newcomer });
            var off = CoopCatchup.ForPlayer(room, Newcomer.DreamLevel, Newcomer.SpentStarPoints, 2, enabled: false);
            Assert.False(off.IsActive);
            Assert.Equal(1, off.OutgoingMultiplier, 10);
            Assert.Equal(1, off.IncomingMultiplier, 10);
        }
    }
}
