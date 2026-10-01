using System.Diagnostics;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>ゲーム中に毎回走る処理が十分に速いことを確かめる（上限は余裕を持たせた値）。</summary>
    public class PerformanceTests
    {
        private static Profile Loaded()
        {
            var p = Profile.CreateNew(9);
            var rng = new Rng(9009);
            for (int i = 0; i < Workshop.StashCapacity(p); i++) p.Stash.Add(Loot.RollRelic(rng, (Rarity)(i % 5), 30));
            for (int i = 0; i < 10; i++) p.LostAndFound.Add(Loot.RollRelic(rng, Rarity.Rare, 30));
            p.DreamLevel = 30;
            foreach (var r in p.Stash) if (p.Hero("Hero_Vesper").Equipped[(int)r.Slot] == null) Rules.Equip(p, "Hero_Vesper", r.Uid);
            Rules.BeginRun(p, "perf", DailyDream.Get(1), 3);
            for (int i = 0; i < Content.SatchelCapacity; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Epic, 30));
            return p;
        }

        [Fact]
        public void A_kill_takes_well_under_a_tenth_of_a_millisecond()
        {
            var p = Loaded();
            for (int i = 0; i < 200; i++) Rules.OnKill(p, MonsterTier.Normal, 30, NightmareAffix.None, "Hero_Vesper");
            var sw = Stopwatch.StartNew();
            const int n = 20000;
            for (int i = 0; i < n; i++) Rules.OnKill(p, MonsterTier.Normal, 30, NightmareAffix.None, "Hero_Vesper");
            double perKillMs = sw.Elapsed.TotalMilliseconds / n;
            Assert.True(perKillMs < 0.1, $"{perKillMs:0.0000} ms per kill");
        }

        [Fact]
        public void Build_and_encode_are_fast()
        {
            var p = Loaded();
            var sw = Stopwatch.StartNew();
            const int n = 5000;
            for (int i = 0; i < n; i++) Build.Compute(p, "Hero_Vesper", 3, p.Run.Pacts, 1).Encode();
            double ms = sw.Elapsed.TotalMilliseconds / n;
            Assert.True(ms < 0.2, $"{ms:0.0000} ms per build");
        }

        [Fact]
        public void Serializing_a_full_profile_takes_a_few_milliseconds()
        {
            var p = Loaded();
            ProfileCodec.Write(p);
            var sw = Stopwatch.StartNew();
            const int n = 50;
            for (int i = 0; i < n; i++) ProfileCodec.Write(p);
            double ms = sw.Elapsed.TotalMilliseconds / n;
            Assert.True(ms < 15, $"{ms:0.00} ms per save serialization");
        }
    }
}
