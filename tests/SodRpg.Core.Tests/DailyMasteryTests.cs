using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DailyMasteryTests
    {
        [Fact]
        public void Daily_dream_is_stable_for_a_date_and_varies_across_dates()
        {
            var d = new DateTime(2026, 10, 1);
            Assert.Same(DailyDream.For(d), DailyDream.For(d));
            var seen = new HashSet<int>();
            for (int i = 0; i < 120; i++) seen.Add(DailyDream.For(d.AddDays(i)).Id);
            Assert.True(seen.Count >= 6, "most dreams appear over four months");
        }

        [Fact]
        public void Daily_dream_ids_are_unique_and_texts_exist()
        {
            Assert.Equal(DailyDream.All.Count, DailyDream.All.Select(x => x.Id).Distinct().Count());
            Assert.DoesNotContain(DailyDream.All, x => x.Id == 0);
            foreach (bool ja in new[] { true, false })
            {
                Loc.Japanese = ja;
                foreach (var x in DailyDream.All)
                {
                    Assert.False(string.IsNullOrWhiteSpace(x.Name.ToString()));
                    Assert.False(string.IsNullOrWhiteSpace(x.Description.ToString()));
                }
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Run_remembers_the_dream_it_started_with()
        {
            var p = Profile.CreateNew(1);
            var dream = DailyDream.Get(5);
            var ev = Rules.BeginRun(p, "d", dream);
            Assert.Equal(5, p.Run.DailyId);
            Assert.Contains(ev, e => e.Text.Contains(dream.Name.ToString()));
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(5, q.Run.DailyId);
            Rules.BeginRun(p, "d");
            Assert.Equal(5, p.Run.DailyId); // 同じランの再開では変わらない
        }

        [Fact]
        public void No_daily_by_default()
        {
            var p = Profile.CreateNew(1);
            Rules.BeginRun(p, "n");
            Assert.Equal(0, p.Run.DailyId);
            Assert.Equal(1.0, Rules.KillModifiers(p.Run).ShardMult);
        }

        [Fact]
        public void Golden_dream_multiplies_kill_shards()
        {
            var golden = Profile.CreateNew(8);
            Rules.BeginRun(golden, "g", DailyDream.Get(4));
            Assert.Equal(1.5, Rules.KillModifiers(golden.Run).ShardMult);
            var plain = Profile.CreateNew(8);
            Rules.BeginRun(plain, "g");
            golden.Run.Bounties.Clear();
            plain.Run.Bounties.Clear();
            for (int i = 0; i < 20; i++)
            {
                Rules.OnKill(golden, MonsterTier.Boss, 10);
                Rules.OnKill(plain, MonsterTier.Boss, 10);
            }
            Assert.True(golden.Run.SatchelShards > plain.Run.SatchelShards * 1.3);
        }

        [Fact]
        public void Quiet_dream_doubles_bounty_rewards()
        {
            var p = Profile.CreateNew(8);
            Rules.BeginRun(p, "q", DailyDream.Get(8));
            p.Run.Bounties.Clear();
            p.Run.Bounties.Add(new Bounty { Kind = BountyKind.Collector, Target = 1, RewardShards = 20, RewardTuning = 1, RewardXp = 1 });
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(99), Rarity.Common, 1));
            Rules.Secure(p);
            Assert.Equal(40, p.Material(Materials.Shard));
            Assert.Equal(2, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Featured_line_applies_only_without_a_focus()
        {
            var p = Profile.CreateNew(3);
            Rules.BeginRun(p, "f", DailyDream.Get(2)); // 鋼の日：守勢
            p.Run.Bounties.Clear();
            var rng = new Rng(p.RngState);
            int pity = p.EpicPity;
            var expected = Loot.RollKill(rng, MonsterTier.Boss, 10, 0, ref pity, Line.Guard, Rules.KillModifiers(p.Run),
                p.Stash, p.Run.Satchel, p.Codex); // Rules.OnKill と同じ引数（銘品の重みは図鑑・所持で変わる）
            Rules.OnKill(p, MonsterTier.Boss, 10);
            Assert.Equal(expected.Relics.Select(r => r.Uid + r.BaseId), p.Run.Satchel.Select(r => r.Uid + r.BaseId));

            var q = Profile.CreateNew(3);
            q.Focus = Line.Offense;
            Rules.BeginRun(q, "f", DailyDream.Get(2));
            q.Run.Bounties.Clear();
            rng = new Rng(q.RngState);
            pity = q.EpicPity;
            expected = Loot.RollKill(rng, MonsterTier.Boss, 10, 0, ref pity, Line.Offense, Rules.KillModifiers(q.Run),
                q.Stash, q.Run.Satchel, q.Codex);
            Rules.OnKill(q, MonsterTier.Boss, 10);
            Assert.Equal(expected.Relics.Select(r => r.Uid + r.BaseId), q.Run.Satchel.Select(r => r.Uid + r.BaseId));
        }

        [Fact]
        public void Boosted_powers_grow_by_half_in_the_build()
        {
            var p = Profile.CreateNew(1);
            var r = new Relic { Uid = "u1", BaseId = "weapon.blaze_greatsword", Rarity = Rarity.Epic, ItemLevel = 1 };
            r.Powers.Add(new PowerLine(Power.Blaze, 60));
            p.Stash.Add(r);
            Rules.Equip(p, "H", r.Uid);
            Assert.Equal(60, Build.Compute(p, "H", 0).Get(Power.Blaze));
            Assert.Equal(90, Build.Compute(p, "H", 0, null, 1).Get(Power.Blaze));
            Assert.Equal(60, Build.Compute(p, "H", 0, null, 2).Get(Power.Blaze)); // 別の日
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(99, 0)]
        [InlineData(100, 1)]
        [InlineData(599, 2)]
        [InlineData(600, 3)]
        [InlineData(5500, 10)]
        [InlineData(99999, 10)]
        public void Mastery_levels_follow_thresholds(int kills, int level)
        {
            Assert.Equal(level, Mastery.Level(kills));
            if (level < Mastery.MaxLevel) Assert.True(Mastery.ToNext(kills) > 0);
            else Assert.Equal(0, Mastery.ToNext(kills));
            Assert.False(string.IsNullOrWhiteSpace(Mastery.Title(level)));
        }

        [Fact]
        public void Kills_raise_hero_mastery()
        {
            var p = Profile.CreateNew(2);
            Rules.BeginRun(p, "m");
            p.Run.Bounties.Clear();
            var events = new List<GameEvent>();
            for (int i = 0; i < 100; i++) events.AddRange(Rules.OnKill(p, MonsterTier.Lesser, 1, NightmareAffix.None, "Hero_Vesper"));
            Assert.Equal(100, p.Hero("Hero_Vesper").Kills);
            Assert.Contains(events, e => e.Kind == EventKind.LevelUp && e.Text.Contains("Vesper"));
            var b = Build.Compute(p, "Hero_Vesper", 0);
            Assert.Equal(0, b.Get(Stat.AttackPct)); // v1.2：熟練度は能力%ではなく到達刻印の解放条件
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(100, q.Hero("Hero_Vesper").Kills);
        }

        [Fact]
        public void Night_of_nightmares_doubles_the_chance()
        {
            int Count(double mult)
            {
                var rng = new Rng(12);
                int n = 0;
                for (int i = 0; i < 40000; i++)
                    if (Nightmares.Roll(rng, MonsterTier.Normal, 2, mult) != NightmareAffix.None) n++;
                return n;
            }
            double ratio = (double)Count(2.0) / Count(1.0);
            Assert.InRange(ratio, 1.8, 2.2);
        }
    }
}
