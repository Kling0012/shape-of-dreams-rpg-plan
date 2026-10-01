using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// 多数のランを乱数で遊ばせ、どの時点でも壊れてはいけない性質（不変条件）を確かめる。
    /// </summary>
    public class GameSimulationTests
    {
        private static void AssertInvariants(Profile p)
        {
            var all = p.Stash.Concat(p.LostAndFound).Concat(p.Run?.Satchel ?? new List<Relic>()).ToList();
            Assert.Equal(all.Count, all.Select(r => r.Uid).Distinct().Count()); // 同じ遺物が二か所にない
            Assert.True(p.Stash.Count <= Workshop.StashCapacity(p));
            Assert.True(p.LostAndFound.Count <= Content.LostAndFoundCapacity);
            if (p.Run != null)
            {
                Assert.True(p.Run.Satchel.Count <= Workshop.SatchelCapacity(p));
                Assert.InRange(p.Run.Heat, 0, Content.MaxHeat);
                Assert.True(p.Run.SatchelShards >= 0 && p.Run.SatchelTuning >= 0);
            }
            Assert.All(p.Materials.Values, v => Assert.True(v >= 0));
            Assert.InRange(p.DreamLevel, 1, Content.MaxDreamLevel);
            foreach (var r in all)
            {
                Assert.True(Content.TryGetBase(r.BaseId, out _));
                Assert.InRange(r.Enhance, 0, Content.MaxEnhance);
                Assert.InRange(r.Retunes, 0, Content.MaxRetunes);
            }
            foreach (var h in p.Heroes.Values)
            {
                Assert.True(Rules.SpentPoints(h) <= p.TalentPoints);
                for (int i = 0; i < 3; i++)
                {
                    if (h.Equipped[i] == null) continue;
                    var r = p.FindStash(h.Equipped[i]);
                    Assert.NotNull(r);
                    Assert.Equal(i, (int)r.Slot);
                }
            }
        }

        private static void PlayRandomRun(Profile p, Rng rng, string runId)
        {
            Rules.BeginRun(p, runId);
            int zones = rng.Range(1, 5);
            for (int z = 0; z < zones; z++)
            {
                int kills = rng.Range(0, 120);
                for (int k = 0; k < kills; k++)
                {
                    double x = rng.NextDouble();
                    var tier = x < 0.5 ? MonsterTier.Lesser : x < 0.97 ? MonsterTier.Normal : x < 0.995 ? MonsterTier.MiniBoss : MonsterTier.Boss;
                    Rules.OnKill(p, tier, 1 + z * 8);
                }
                Rules.OnRoomsCleared(p, p.Run.RoomsCleared + rng.Range(0, 3));
                if (rng.Chance(0.15))
                {
                    Rules.EndRun(p, victory: false);
                    return;
                }
                if (Rules.ShouldOfferSecurePoint(p))
                {
                    Rules.ReachSecurePoint(p);
                    if (rng.Chance(0.5)) Rules.Secure(p);
                    else Rules.Delve(p);
                }
                AssertInvariants(p);
            }
            Rules.EndRun(p, victory: rng.Chance(0.5));
        }

        private static void RandomHubActions(Profile p, Rng rng)
        {
            string hero = "Hero_" + rng.Range(0, 3);
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    var r = p.Stash.Count > 0 ? p.Stash[rng.Range(0, p.Stash.Count - 1)] : null;
                    switch (rng.Range(0, 7))
                    {
                        case 0: if (r != null) Rules.Equip(p, hero, r.Uid); break;
                        case 1: if (r != null) Rules.Enhance(p, r.Uid); break;
                        case 2: if (r != null && r.Affixes.Count > 0) Rules.Retune(p, r.Uid, rng.Range(0, r.Affixes.Count - 1)); break;
                        case 3: if (r != null && p.Stash.Count > 20) Rules.Salvage(p, r.Uid); break;
                        case 4: Rules.Craft(p, (Slot)rng.Range(0, 2), rng.Chance(0.3)); break;
                        case 5: Rules.AddTalentRank(p, hero, Content.Talents.Where(t => !t.IsKeystone).ElementAt(rng.Range(0, 11)).Id); break;
                        case 6: Rules.SetKeystone(p, hero, Content.Talents.Where(t => t.IsKeystone).ElementAt(rng.Range(0, 2)).Id); break;
                        default: Rules.ToggleLock(p, r?.Uid); break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 素材不足・条件未達は正常な拒否。プロフィールが壊れていないことを下で確かめる。
                }
                AssertInvariants(p);
            }
        }

        [Theory]
        [InlineData(1UL)]
        [InlineData(2UL)]
        [InlineData(3UL)]
        [InlineData(4UL)]
        public void Hundred_random_runs_keep_invariants_and_roundtrip(ulong seed)
        {
            var p = Profile.CreateNew(seed);
            var rng = new Rng(seed * 7919);
            for (int run = 0; run < 100; run++)
            {
                PlayRandomRun(p, rng, "run-" + run);
                AssertInvariants(p);
                RandomHubActions(p, rng);
                if (run % 10 == 0)
                {
                    string text = ProfileCodec.Write(p);
                    var notes = new List<string>();
                    var q = ProfileCodec.Read(text, notes);
                    Assert.Empty(notes);
                    Assert.Equal(text, ProfileCodec.Write(q));
                }
            }
            Assert.Equal(100, p.Stats.Runs);
            Assert.Equal(p.Stats.Runs, p.Stats.Victories + p.Stats.Defeats);
            Assert.True(p.Stats.RelicsFound > 0);
        }

        [Fact]
        public void Build_from_any_random_profile_respects_caps_and_encodes()
        {
            var p = Profile.CreateNew(77);
            var rng = new Rng(77);
            for (int run = 0; run < 40; run++)
            {
                PlayRandomRun(p, rng, "r" + run);
                RandomHubActions(p, rng);
                foreach (var hero in p.Heroes.Keys)
                {
                    var b = Build.Compute(p, hero, rng.Range(0, 5));
                    foreach (var kv in b.Powers) Assert.InRange(kv.Value, 0, Content.PowerCap(kv.Key));
                    var d = Build.Decode(b.Encode());
                    Assert.Equal(b.Stats, d.Stats);
                    Assert.Equal(b.Powers, d.Powers);
                }
            }
        }

        [Fact]
        public void Crash_at_any_point_of_save_never_loses_the_previous_profile()
        {
            var mem = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(mem);
            var store = new ProfileStore(fs, "/s/profile.json", 9);
            var p = store.Load();
            p.AddMaterial(Materials.Shard, 10);
            store.Save(p); // rev1
            p.AddMaterial(Materials.Shard, 10);
            store.Save(p); // rev2

            foreach (FaultMode mode in Enum.GetValues(typeof(FaultMode)))
            {
                for (int op = 0; op < 4; op++)
                {
                    var before = ProfileCodec.Write(new ProfileStore(mem, "/s/profile.json", 9).Load());
                    var reloaded = new ProfileStore(fs, "/s/profile.json", 9).Load();
                    long rev = reloaded.Revision;
                    reloaded.AddMaterial(Materials.Shard, 1);
                    fs.Arm(op, mode);
                    try
                    {
                        new ProfileStore(fs, "/s/profile.json", 9).Save(reloaded);
                    }
                    catch (Exception)
                    {
                        // クラッシュ・保存失敗の注入。
                    }
                    fs.Disarm();
                    var after = new ProfileStore(mem, "/s/profile.json", 9).Load();
                    Assert.True(after.Revision == rev || after.Revision == rev + 1, $"{mode}@{op}: rev {after.Revision}");
                    if (after.Revision == rev) Assert.Equal(before, ProfileCodec.Write(after));
                    else Assert.Equal(reloaded.Material(Materials.Shard), after.Material(Materials.Shard));
                }
            }
        }

        [Fact]
        public void Failed_save_keeps_revision_so_retry_works()
        {
            var mem = new InMemoryFileSystem();
            var fs = new FaultyFileSystem(mem);
            var store = new ProfileStore(fs, "/s/p.json", 1);
            var p = store.Load();
            store.Save(p);
            fs.Arm(0, FaultMode.IoError);
            Assert.Throws<IOException>(() => store.Save(p));
            Assert.Equal(1, p.Revision);
            fs.Disarm();
            store.Save(p);
            Assert.Equal(2, new ProfileStore(mem, "/s/p.json", 1).Load().Revision);
        }

        [Fact]
        public void Garbage_inputs_never_crash_the_decoders()
        {
            var rng = new Rng(4242);
            const string alphabet = "{}[]\":,0123456789-abcdefghsp=;. truefalsenull\\";
            var sample = ProfileCodec.Write(Profile.CreateNew(3));
            for (int i = 0; i < 3000; i++)
            {
                string text;
                if (i % 2 == 0)
                {
                    var chars = Enumerable.Range(0, rng.Range(0, 80)).Select(_ => alphabet[rng.Range(0, alphabet.Length - 1)]);
                    text = new string(chars.ToArray());
                }
                else
                {
                    // 正しいJSONの一部を壊す
                    var arr = sample.ToCharArray();
                    for (int k = 0; k < 3; k++) arr[rng.Range(0, arr.Length - 1)] = alphabet[rng.Range(0, alphabet.Length - 1)];
                    text = new string(arr);
                }
                Build.Decode(text); // 例外を投げず null か値を返す
                try
                {
                    ProfileCodec.Read(text, new List<string>());
                }
                catch (LedgerFormatException)
                {
                    // 想定どおりの拒否
                }
            }
        }

        [Fact]
        public void Drop_rates_per_tier_match_design_targets()
        {
            var rng = new Rng(2024);
            double Rate(MonsterTier t, int n)
            {
                int pity = 0, drops = 0;
                for (int i = 0; i < n; i++) drops += Loot.RollKill(rng, t, 10, 0, ref pity).Relics.Count;
                return (double)drops / n;
            }
            Assert.InRange(Rate(MonsterTier.Lesser, 100000), 0.004, 0.008);
            Assert.InRange(Rate(MonsterTier.Normal, 50000), 0.017, 0.023);
            Assert.InRange(Rate(MonsterTier.MiniBoss, 5000), 0.32, 0.38);
            Assert.InRange(Rate(MonsterTier.Boss, 5000), 1.55, 1.65); // 1個確定＋60%で2個目
        }

        [Fact]
        public void Item_level_scales_affix_values()
        {
            var lo = new List<int>();
            var hi = new List<int>();
            var rng = new Rng(8);
            for (int i = 0; i < 2000; i++)
            {
                lo.Add(Loot.RollRelic(rng, Rarity.Rare, 1, Slot.Weapon).Affixes.Sum(a => a.Value));
                hi.Add(Loot.RollRelic(rng, Rarity.Rare, 40, Slot.Weapon).Affixes.Sum(a => a.Value));
            }
            Assert.True(hi.Average() > lo.Average() * 1.8);
        }

        [Fact]
        public void Every_unique_and_base_can_drop()
        {
            var rng = new Rng(31);
            var seen = new HashSet<string>();
            for (int i = 0; i < 20000; i++)
            {
                var r = Loot.RollRelic(rng, (Rarity)(i % 5), 10);
                seen.Add(r.UniqueId ?? r.BaseId);
            }
            foreach (var b in Content.Bases) Assert.Contains(b.Id, seen);
            foreach (var u in Content.Uniques) Assert.Contains(u.Id, seen);
        }

        [Fact]
        public void Every_stat_and_power_has_text_in_both_languages()
        {
            foreach (bool ja in new[] { true, false })
            {
                Loc.Japanese = ja;
                foreach (Stat s in Enum.GetValues(typeof(Stat))) Assert.False(string.IsNullOrWhiteSpace(Content.FormatStat(s, 5)));
                foreach (Power p in Enum.GetValues(typeof(Power)))
                {
                    if (p == Power.None) continue;
                    Assert.NotEqual("-", Content.PowerName(p));
                    Assert.NotEqual("-", Content.FormatPower(p, 10));
                }
            }
            Loc.Japanese = true;
        }

        [Fact]
        public void Content_tables_are_consistent()
        {
            Assert.Equal(18, Content.Bases.Count);
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
            {
                Assert.Equal(6, Content.BasesFor(s).Count());
                Assert.NotEmpty(Content.AffixPool(s));
                Assert.NotEmpty(Content.PowerPool(s));
                Assert.True(Content.AffixPool(s).Count >= 4, "Rare needs 3 affixes besides the implicit");
            }
            foreach (var u in Content.Uniques) Assert.True(Content.TryGetBase(u.BaseId, out _));
            foreach (Power p in Enum.GetValues(typeof(Power)))
                if (p != Power.None) Assert.True(Content.PowerCap(p) > 0);
            foreach (Stat s in Enum.GetValues(typeof(Stat))) Assert.True(Content.StatCap(s) > 0);
            foreach (Line l in Enum.GetValues(typeof(Line)))
            {
                var nodes = Content.Talents.Where(t => t.Route == l).ToList();
                Assert.Single(nodes, t => t.IsKeystone);
                Assert.True(nodes.Where(t => !t.IsKeystone).Sum(t => t.MaxRank) >= Content.KeystoneRouteRequirement);
            }
        }
    }
}
