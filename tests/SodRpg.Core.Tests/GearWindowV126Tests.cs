using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.26：確保地点に着いてから次の撃破までは、装備を変えられる。</summary>
    public class GearWindowV126Tests
    {
        [Fact]
        public void Gear_window_stays_open_after_secure_until_first_kill()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "Hero_A");
            Assert.True(Rules.LoadoutLocked(p, true));

            Rules.ReachSecurePoint(p);
            Rules.Secure(p);

            Assert.False(p.Run.AwaitingChoice);
            Assert.True(p.Run.GearWindow);
            Assert.False(Rules.LoadoutLocked(p, true));
            Assert.False(Rules.LoadoutLocked(p, false)); // ゲームの外なら常に変更可

            Rules.OnKill(p, MonsterTier.Normal, 5);
            Assert.False(p.Run.GearWindow);
            Assert.True(Rules.LoadoutLocked(p, true));
        }

        [Fact]
        public void Delve_keeps_the_gear_window_open()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "Hero_A");

            Rules.ReachSecurePoint(p);
            Rules.Delve(p);

            Assert.False(p.Run.AwaitingChoice);
            Assert.True(p.Run.GearWindow);
            Assert.False(Rules.LoadoutLocked(p, true));
        }

        [Fact]
        public void New_run_starts_with_the_gear_window_closed()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "a");
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.True(p.Run.GearWindow);

            Rules.EndRun(p, victory: true);
            Rules.BeginRun(p, "b");
            Assert.False(p.Run.GearWindow);
            Assert.True(Rules.LoadoutLocked(p, true));
        }

        [Fact]
        public void Gear_window_survives_save_and_load()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "Hero_A");
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.True(p.Run.GearWindow);

            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.True(q.Run.GearWindow);

            Rules.OnKill(q, MonsterTier.Normal, 5);
            var r = ProfileCodec.Read(ProfileCodec.Write(q), new List<string>());
            Assert.False(r.Run.GearWindow);
        }

        [Fact]
        public void Dust_conversion_is_allowed_during_the_gear_window()
        {
            var p = Profile.CreateNew(12);
            Rules.BeginRun(p, "Hero_A");
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.False(p.Run.AwaitingChoice);

            int shards = p.Material(Materials.Shard);
            Rules.ConvertDust(p, Economy.DustPerBatch * 2);
            Assert.Equal(shards + Economy.ShardsPerBatch * 2, p.Material(Materials.Shard));

            Rules.OnKill(p, MonsterTier.Normal, 5);
            Assert.Throws<InvalidOperationException>(() => Rules.ConvertDust(p, Economy.DustPerBatch * 2));
        }
    }
}
