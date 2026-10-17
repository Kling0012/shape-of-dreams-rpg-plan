using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#700: 担当外の減衰は、その周回で入った世界の累計（0始まり）で決まり、ラン内で戻らない。</summary>
    public class RunWorldOwnershipTests
    {
        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(1, 0.7)]
        [InlineData(2, 0.4)]
        [InlineData(3, 0.1)]
        [InlineData(4, 0.1)]
        [InlineData(7, 0.1)]
        public void Helper_factor_follows_the_world_total_and_repeats_the_last_stage(int world, double factor)
        {
            Assert.Equal(factor, OwnershipHelp.Factor(world), 5);
        }

        [Fact]
        public void Outside_a_run_no_decay_applies()
        {
            Assert.Equal(0, OwnershipHelp.DecayPercent(-1));
            Assert.Equal(1.0, OwnershipHelp.Factor(-1), 5);
        }

        [Fact]
        public void Native_zone_index_restarting_does_not_restore_the_lighter_decay()
        {
            var counter = new RunWorldCounter();
            for (int zone = 0; zone < 4; zone++) Assert.True(counter.Observe("run", zone, "Zone" + zone));
            Assert.Equal(3, counter.Index);
            Assert.Equal(0.1, OwnershipHelp.Factor(counter.Index), 5);

            // インフィニティなどで本体の世界番号が0へ戻っても、累計は増え続ける。
            Assert.True(counter.Observe("run", 0, "Zone9"));
            Assert.Equal(4, counter.Index);
            Assert.Equal(0.1, OwnershipHelp.Factor(counter.Index), 5);
            Assert.True(counter.Observe("run", 0, "Zone0"));
            Assert.Equal(5, counter.Index);
        }

        [Fact]
        public void The_same_world_is_counted_once_and_only_a_new_run_restarts_the_total()
        {
            var counter = new RunWorldCounter();
            Assert.False(counter.Observe(null, 0, "Zone0"));
            Assert.False(counter.Observe("run", -1, null));
            Assert.Equal(-1, counter.Index);
            Assert.True(counter.Observe("run", 0, "Zone0"));
            Assert.False(counter.Observe("run", 0, "Zone0")); // 部屋移動・再読み込み・重複通知
            Assert.True(counter.Observe("run", 1, "Zone1"));
            Assert.Equal(1, counter.Index);

            Assert.True(counter.Observe("next", 1, "Zone1"));
            Assert.Equal(0, counter.Index);
        }

        [Fact]
        public void A_saved_world_total_survives_continue_and_old_saves_omit_it()
        {
            var p = Profile.CreateNew(11);
            Rules.BeginRun(p, "run");
            Assert.Equal(-1, p.Run.WorldIndex);
            string blank = ProfileCodec.Write(p);
            Assert.DoesNotContain("worldIndex", blank);
            Assert.Equal(-1, ProfileCodec.Read(blank, new List<string>()).Run.WorldIndex);

            p.Run.WorldIndex = 4;
            p.Run.WorldZoneIndex = 0;
            p.Run.WorldZoneName = "Zone9";
            var run = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>()).Run;
            Assert.Equal(4, run.WorldIndex);
            Assert.Equal(0, run.WorldZoneIndex);
            Assert.Equal("Zone9", run.WorldZoneName);
            Assert.Equal(4, run.Clone().WorldIndex);

            // 再開後の同じ世界は数え直さず、次の世界で続きから数える。
            var counter = new RunWorldCounter();
            Assert.True(counter.Restore("run", run.WorldIndex, run.WorldZoneIndex, run.WorldZoneName));
            Assert.False(counter.Observe("run", 0, "Zone9"));
            Assert.True(counter.Observe("run", 1, "Zone10"));
            Assert.Equal(5, counter.Index);
        }

        [Fact]
        public void The_stage_table_is_built_from_the_authored_list()
        {
            Assert.Equal(4, CoopBalance.Ownership.HelpDecayWorlds);
            Assert.Equal("世界1 0% ・ 2 30% ・ 3 60% ・ 4以降 90%",
                OwnershipHelp.StageTable(CoopBalance.Ownership.HelpDecayPercent, CoopBalance.Ownership.HelpDecayWorlds, "世界", "以降"));
        }
    }
}
