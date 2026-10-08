using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public sealed class InfinityIntervalScalingTests
    {
        [Theory]
        [InlineData(20, 0, 0, 1)]
        [InlineData(15, 2, 2, 1.5)]
        [InlineData(10, 4, 4, 2)]
        public void Continue_derives_pressure_and_additive_uncapped_count_from_saved_interval(int interval, int offset, double bonus, double relics)
        {
            var p = Profile.CreateNew(270);
            Rules.BeginRun(p, "interval-continue");
            p.Run.Infinity = new InfinityRunState { FixedZoneId = "Zone_Forest", Interval = interval, ClearedCombatTotal = interval * 3 };
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(interval, loaded.Run.Infinity.Interval);
            Assert.Equal(3 + offset, loaded.Run.Infinity.PressureStage);
            var pressure = DreamPressure.ForPlayer(30, 500).WithInfinityPressure(loaded.Run.Infinity.PressureStage);
            Assert.Equal(1.6 + bonus, InfinityIntervalScaling.EnemyCountMultiplier(pressure, interval), 10);
            Assert.Equal(relics, InfinityIntervalScaling.RelicMultiplier(loaded.Run.Infinity.Interval));
            loaded.Run.Infinity.ClearedCombatTotal = long.MaxValue;
            Assert.Equal(100, loaded.Run.Infinity.PressureStage);
        }

        [Fact]
        public void Extra_ordinary_rolls_increase_equipment_without_an_extra_Legendary_opportunity()
        {
            long ordinaryBefore = 0, ordinaryAfter = 0;
            for (ulong seed = 1; seed <= 4000; seed++)
            {
                var before = Loot.RollKill(new Rng(seed), MonsterTier.Boss, 1, 0);
                var after = Loot.RollKill(new Rng(seed), MonsterTier.Boss, 1, 0, ordinaryRelicMultiplier: 2);
                Assert.Equal(before.Relics.Count(r => r.Rarity == Rarity.Legendary), after.Relics.Count(r => r.Rarity == Rarity.Legendary));
                ordinaryBefore += before.Relics.Count(r => r.Rarity <= Rarity.Epic);
                ordinaryAfter += after.Relics.Count(r => r.Rarity <= Rarity.Epic);
                Assert.All(after.Relics.Skip(before.Relics.Count), r => Assert.True(r.Rarity <= Rarity.Epic));
            }
            Assert.InRange((double)ordinaryAfter / ordinaryBefore, 1.94, 2.06);
        }
    }
}
