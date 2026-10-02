using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.14.1：分解の返事待ちに遠征が終わったとき（issue #8）。</summary>
    public class SalvageEndRunV1141Tests
    {
        private static (Profile p, Relic reserved, Relic other) Setup(ulong seed = 6)
        {
            var p = Profile.CreateNew(seed);
            Rules.BeginRun(p, "salv");
            var a = Loot.RollRelic(new Rng(1), Rarity.Rare, 3);
            var b = Loot.RollRelic(new Rng(2), Rarity.Uncommon, 3);
            p.Run.Satchel.Add(a);
            p.Run.Satchel.Add(b);
            return (p, a, b);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Reserved_relic_is_held_out_of_the_run_settlement(bool victory)
        {
            var (p, reserved, other) = Setup();
            Rules.EndRun(p, victory, new HashSet<string> { reserved.Uid });
            Assert.DoesNotContain(p.Stash, r => r.Uid == reserved.Uid);
            Assert.DoesNotContain(p.LostAndFound, r => r.Uid == reserved.Uid);
            Assert.Single(p.PendingSalvage, s => s.Relic.Uid == reserved.Uid);
            Assert.True(p.Stash.Any(r => r.Uid == other.Uid) || p.LostAndFound.Any(r => r.Uid == other.Uid)); // 予約していない物は今まで通り
        }

        [Fact]
        public void Success_after_the_run_consumes_the_held_relic_once()
        {
            var (p, reserved, _) = Setup();
            Rules.EndRun(p, true, new HashSet<string> { reserved.Uid });
            Assert.Same(reserved, Rules.SalvageUnsecured(p, reserved.Uid));
            Assert.Empty(p.PendingSalvage);
            Assert.Null(Rules.SalvageUnsecured(p, reserved.Uid)); // 重複した返事
            Assert.DoesNotContain(p.Stash, r => r.Uid == reserved.Uid);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Failure_after_the_run_returns_the_relic_where_the_run_would_have_put_it(bool victory)
        {
            var (p, reserved, _) = Setup();
            Rules.EndRun(p, victory, new HashSet<string> { reserved.Uid });
            Rules.RestorePendingSalvage(p, reserved.Uid);
            Assert.Empty(p.PendingSalvage);
            if (victory) Assert.Contains(p.Stash, r => r.Uid == reserved.Uid);
            else Assert.Contains(p.LostAndFound, r => r.Uid == reserved.Uid);
        }

        [Fact]
        public void Held_relics_are_returned_on_startup_and_survive_save_load()
        {
            var (p, reserved, _) = Setup();
            Rules.EndRun(p, true, new HashSet<string> { reserved.Uid });
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Single(q.PendingSalvage);
            Rules.RestorePendingSalvage(q); // 起動時：取引台帳が無いので全部戻す
            Assert.Empty(q.PendingSalvage);
            Assert.Contains(q.Stash, r => r.Uid == reserved.Uid);
        }

        [Fact]
        public void Failure_during_the_run_keeps_the_relic_in_the_satchel()
        {
            var (p, reserved, _) = Setup();
            Rules.RestorePendingSalvage(p, reserved.Uid);
            Assert.Contains(reserved, p.Run.Satchel);
        }
    }
}
