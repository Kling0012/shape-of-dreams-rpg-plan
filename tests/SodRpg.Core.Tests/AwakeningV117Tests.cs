using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>v1.17：装着中の伝説の遺物の覚醒、倍率、保存と偉業。</summary>
    public class AwakeningV117Tests
    {
        private const string HeroKey = "Hero_Vesper";

        private static Relic Legendary(ulong seed = 20)
        {
            var unique = Content.Uniques.First(u => u.Powers.Count > 0);
            return Loot.RollUnique(new Rng(seed), unique, 1);
        }

        private static Profile Equipped(Relic relic)
        {
            var p = Profile.CreateNew(12);
            p.Stash.Add(relic);
            Rules.Equip(p, HeroKey, relic.Uid);
            Rules.BeginRun(p, HeroKey);
            p.Run.Bounties.Clear();
            return p;
        }

        private static bool IsAwakening(GameEvent e)
        {
            return e.Kind == EventKind.LevelUp &&
                (e.Text.Contains("」が覚醒") || e.Text.Contains("reached Awakening"));
        }

        [Fact]
        public void Boss_kills_climb_three_awakening_levels_and_stop_at_the_last()
        {
            var r = Legendary();
            var p = Equipped(r);
            var events = new List<GameEvent>();
            void Kill(int n) { for (int i = 0; i < n; i++) events.AddRange(Rules.OnKill(p, MonsterTier.Boss, 1, NightmareAffix.None, HeroKey)); }

            Kill(249);
            Assert.Equal(4980, r.AwakenPoints);
            Assert.False(r.Awakened);
            Assert.DoesNotContain(events, IsAwakening);

            Kill(1); // 5000：覚醒Ⅰ
            Assert.Equal(1, r.AwakenLevel);
            Assert.Equal(1, p.Stats.RelicsAwakened);
            Assert.Contains("feat.awakening.1", p.Feats);
            Assert.Single(events, IsAwakening);

            Kill(499);
            Assert.Equal(14980, r.AwakenPoints);
            Assert.Equal(1, r.AwakenLevel);
            Kill(1); // 15000：覚醒Ⅱ
            Assert.Equal(2, r.AwakenLevel);
            Kill(1124);
            Assert.Equal(37480, r.AwakenPoints);
            Assert.Equal(2, r.AwakenLevel);
            Kill(1); // 37500：覚醒Ⅲ
            Assert.Equal(3, r.AwakenLevel);
            Assert.Equal(Content.AwakenThreshold, r.AwakenPoints);
            Assert.Equal(3, events.Count(IsAwakening));
            Assert.Equal(1, p.Stats.RelicsAwakened); // 実績は最初の覚醒だけ数える

            Kill(5);
            Assert.Equal(Content.AwakenThreshold, r.AwakenPoints);
            Assert.Equal(3, events.Count(IsAwakening));
        }

        [Theory]
        [InlineData(Rarity.Epic)]
        public void Nonlegendary_equipment_gains_no_points(Rarity rarity)
        {
            var r = Loot.RollRelic(new Rng(20), rarity, 1);
            var p = Equipped(r);
            Rules.OnKill(p, MonsterTier.Boss, 1, NightmareAffix.Ironclad, HeroKey);
            Assert.Equal(0, r.AwakenPoints);
            Assert.False(r.Awakened);
            Assert.Equal(0, p.Stats.RelicsAwakened);
        }

        [Fact]
        public void Unequipped_and_other_heroes_relics_gain_no_points()
        {
            var r = Legendary();
            var unequipped = Legendary(21);
            var p = Equipped(r);
            p.Stash.Add(unequipped);
            Rules.OnKill(p, MonsterTier.Boss, 1, NightmareAffix.None, "Hero_Mist");
            Assert.Equal(0, r.AwakenPoints);
            Assert.Equal(0, unequipped.AwakenPoints);
            Rules.OnKill(p, MonsterTier.Boss, 1, NightmareAffix.None, HeroKey);
            Assert.Equal(20, r.AwakenPoints);
            Assert.Equal(0, unequipped.AwakenPoints);
        }

        [Fact]
        public void Kills_without_a_hero_or_run_gain_no_points()
        {
            var r = Legendary();
            var p = Equipped(r);
            Rules.OnKill(p, MonsterTier.Boss, 1, NightmareAffix.Ironclad, null);
            Assert.Equal(0, r.AwakenPoints);
            p.Run = null;
            Rules.OnKill(p, MonsterTier.Boss, 1, NightmareAffix.Ironclad, HeroKey);
            Assert.Equal(0, r.AwakenPoints);
            Assert.False(r.Awakened);
            Assert.Equal(0, p.Stats.RelicsAwakened);
        }

        [Theory]
        [InlineData(3)]
        public void Awakening_truncates_enhanced_powers_and_affixes_but_not_the_implicit(int enhance)
        {
            var r = Legendary();
            r.Enhance = enhance;
            var affixStat = r.Affixes[0].Stat;
            var negativeStat = r.Affixes[1].Stat;
            r.Affixes.Clear();
            r.Affixes.Add(new StatLine(affixStat, 7));
            r.Affixes.Add(new StatLine(negativeStat, -7));
            var power = r.Powers[0].Power;
            var secondPower = r.Powers[1].Power;
            r.Powers.Clear();
            r.Powers.Add(new PowerLine(power, 7));
            r.Powers.Add(new PowerLine(secondPower, 11));
            var stats = r.EffectiveStats().ToArray();
            var powers = r.EffectivePowers().ToArray();

            r.Awakened = true;
            var awakened = r.Clone();
            var awakenedStats = awakened.EffectiveStats().ToArray();
            var awakenedPowers = awakened.EffectivePowers().ToArray();
            Assert.Equal(stats[0].Stat, awakenedStats[0].Stat);
            Assert.Equal(stats[0].Value, awakenedStats[0].Value);
            for (int i = 1; i < stats.Length; i++)
            {
                Assert.Equal(stats[i].Stat, awakenedStats[i].Stat);
                Assert.Equal(stats[i].Value * 120 / 100, awakenedStats[i].Value);
            }
            for (int i = 0; i < powers.Length; i++)
            {
                Assert.Equal(powers[i].Power, awakenedPowers[i].Power);
                Assert.Equal(powers[i].Value * 150 / 100, awakenedPowers[i].Value);
            }
        }

        [Theory]
        [InlineData(123, false, 123)]
        [InlineData(6000, true, 15000)]
        public void Save_round_trip_preserves_awakening_and_progress_continues(int points, bool awakened, int expectedPoints)
        {
            var r = Legendary();
            var p = Equipped(r);
            r.AwakenPoints = points;
            r.Awakened = awakened;
            p.Stats.RelicsAwakened = awakened ? 1 : 0;
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p.Clone()), notes);
            var loaded = q.FindStash(r.Uid);
            Assert.Empty(notes);
            Assert.Equal(expectedPoints, loaded.AwakenPoints);
            Assert.Equal(r.AwakenLevel, loaded.AwakenLevel);
            Assert.Equal(awakened, loaded.Awakened);
            Assert.Equal(p.Stats.RelicsAwakened, q.Stats.RelicsAwakened);
            Rules.OnKill(q, MonsterTier.Boss, 1, NightmareAffix.None, HeroKey);
            Assert.Equal(expectedPoints + 20, loaded.AwakenPoints);
            Assert.Equal(r.AwakenLevel, loaded.AwakenLevel);
            Assert.Equal(awakened ? 1 : 0, q.Stats.RelicsAwakened);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(15001, 15001)]
        [InlineData(37500, 37500)]
        [InlineData(37501, 37500)]
        [InlineData(int.MaxValue, 37500)]
        public void Loading_clamps_awakening_points(int saved, int expected)
        {
            var r = Legendary();
            var p = Equipped(r);
            r.AwakenPoints = saved;
            var q = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(expected, q.FindStash(r.Uid).AwakenPoints);
        }

        private static object WithoutAwakening(object value)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var kv in obj.Properties)
                {
                    if (kv.Key == "awaken" || kv.Key == "awakened" || kv.Key == "awakenLevel" || kv.Key == "relicsAwakened") continue;
                    copy.Add(kv.Key, WithoutAwakening(kv.Value));
                }
                return copy;
            }
            if (value is List<object> list) return list.Select(WithoutAwakening).ToList();
            return value;
        }

        [Fact]
        public void Old_saves_default_to_zero_points_and_not_awakened()
        {
            var r = Legendary();
            var p = Equipped(r);
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(p));
            Assert.True(root.TryGet("body", out object body));
            var oldBody = WithoutAwakening(body);
            string checksum;
            using (var sha = SHA256.Create())
            {
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(oldBody))))
                    .Replace("-", "").ToLowerInvariant();
            }
            var oldSave = new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", checksum).Add("body", oldBody);
            var q = ProfileCodec.Read(Json.Write(oldSave), new List<string>());
            Assert.Equal(0, q.FindStash(r.Uid).AwakenPoints);
            Assert.False(q.FindStash(r.Uid).Awakened);
            Assert.Equal(0, q.Stats.RelicsAwakened);
        }

        [Fact]
        public void Awakening_feats_complete_at_one_and_five_and_pay_their_rewards_once()
        {
            var p = Profile.CreateNew(12);
            var first = Feats.All.Single(f => f.Id == "feat.awakening.1");
            var master = Feats.All.Single(f => f.Id == "feat.awakening.2");
            Feats.Check(p);
            Assert.DoesNotContain(first.Id, p.Feats);
            Assert.DoesNotContain(master.Id, p.Feats);
            p.Stats.RelicsAwakened = 1;
            Feats.Check(p);
            Assert.Equal(1, Feats.Progress(p, first));
            Assert.Contains(first.Id, p.Feats);
            Assert.DoesNotContain(master.Id, p.Feats);
            Rules.ClaimFeat(p, first.Id);
            Assert.Equal(60, p.Material(Materials.Shard));
            Assert.Equal(0, p.Material(Materials.Tuning));
            p.Stats.RelicsAwakened = 4;
            Feats.Check(p);
            Assert.DoesNotContain(master.Id, p.Feats);
            p.Stats.RelicsAwakened = 5;
            Feats.Check(p);
            Assert.Equal(5, Feats.Progress(p, master));
            Assert.Contains(master.Id, p.Feats);
            Rules.ClaimFeat(p, master.Id);
            Assert.Equal(210, p.Material(Materials.Shard));
            Assert.Equal(3, p.Material(Materials.Tuning));
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimFeat(p, first.Id));
            Assert.Throws<InvalidOperationException>(() => Rules.ClaimFeat(p, master.Id));
            Assert.Equal(210, p.Material(Materials.Shard));
            Assert.Equal(3, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Legacy_awakened_relics_become_level_two_and_keep_climbing()
        {
            var r = Legendary();
            var p = Equipped(r);
            r.AwakenPoints = 500;
            r.Awakened = true;
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(p));
            // awakenLevel を持たない v1.26 までの保存を作る
            Assert.True(root.TryGet("body", out object body));
            var oldBody = StripKey(body, "awakenLevel");
            string checksum;
            using (var sha = SHA256.Create())
            {
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(oldBody))))
                    .Replace("-", "").ToLowerInvariant();
            }
            var oldSave = new JsonObject().Add("format", ProfileCodec.Format).Add("version", (long)Profile.CurrentVersion)
                .Add("checksum", checksum).Add("body", oldBody);
            var q = ProfileCodec.Read(Json.Write(oldSave), new List<string>());
            var loaded = q.FindStash(r.Uid);
            Assert.Equal(Content.LegacyAwakenLevel, loaded.AwakenLevel);
            Assert.Equal(15000, loaded.AwakenPoints);
            Assert.Equal(150, Content.AwakenPowerPctAt(loaded.AwakenLevel)); // 当時と同じ倍率
            Rules.OnKill(q, MonsterTier.Boss, 1, NightmareAffix.None, HeroKey);
            Assert.Equal(15020, loaded.AwakenPoints);
            Assert.Equal(Content.LegacyAwakenLevel, loaded.AwakenLevel);
        }

        private static object StripKey(object value, string key)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var kv in obj.Properties)
                    if (kv.Key != key) copy.Add(kv.Key, StripKey(kv.Value, key));
                return copy;
            }
            if (value is List<object> list) return list.Select(v => StripKey(v, key)).ToList();
            return value;
        }
    }
}
