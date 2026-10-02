using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Core.Tests.Testing;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class GameRulesTests
    {
        private static Profile NewProfile(ulong seed = 42) => Profile.CreateNew(seed);

        private static Relic Give(Profile p, Rarity rarity, Slot slot, int ilvl = 10)
        {
            var rng = p.TakeRng();
            var r = Loot.RollRelic(rng, rarity, ilvl, slot);
            p.StoreRng(rng);
            p.Stash.Add(r);
            return r;
        }

        [Fact]
        public void Rolled_relics_have_affix_count_by_rarity_and_no_duplicate_stats()
        {
            var rng = new Rng(7);
            for (int i = 0; i < 2000; i++)
            {
                var rarity = (Rarity)(i % 5);
                var r = Loot.RollRelic(rng, rarity, 1 + i % 60);
                Assert.Equal(Content.AffixCount(r.Rarity), r.Affixes.Count);
                var stats = r.Affixes.Select(a => a.Stat).Append(r.Base.ImplicitStat).ToList();
                Assert.Equal(stats.Count, stats.Distinct().Count());
                if (r.Rarity == Rarity.Epic) Assert.Single(r.Powers);
                if (r.Rarity == Rarity.Legendary)
                {
                    Assert.NotNull(r.UniqueId);
                    Assert.True(Content.TryGetUnique(r.UniqueId, out var u));
                    Assert.Equal(u.SetId != null ? 0 : 2, r.Powers.Count);
                }
                if (r.Rarity < Rarity.Epic) Assert.Empty(r.Powers);
            }
        }

        [Fact]
        public void Rng_is_deterministic_for_same_state()
        {
            var a = new Rng(123);
            var b = new Rng(123);
            for (int i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
        }

        [Fact]
        public void Higher_heat_increases_drop_rate_and_rarity()
        {
            int Count(int heat, out int epics)
            {
                var rng = new Rng(99);
                int pity = 0, drops = 0;
                epics = 0;
                for (int i = 0; i < 40000; i++)
                {
                    var rw = Loot.RollKill(rng, MonsterTier.Normal, 10, heat, ref pity);
                    drops += rw.Relics.Count;
                    epics += rw.Relics.Count(r => r.Rarity >= Rarity.Epic);
                }
                return drops;
            }

            int d0 = Count(0, out int e0);
            int d5 = Count(5, out int e5);
            Assert.InRange(d0, 920, 1240); // 2.7%（v1.19）
            Assert.True(d5 > d0 * 2.2, $"heat5 drops {d5} vs {d0}");
            Assert.True((double)e5 / d5 > (double)e0 / d0 * 2, "heat raises epic share");
        }

        [Fact]
        public void Normal_monsters_never_drop_legendaries()
        {
            var rng = new Rng(5);
            int pity = 0;
            for (int i = 0; i < 50000; i++)
            {
                var rw = Loot.RollKill(rng, MonsterTier.Normal, 10, 5, ref pity);
                Assert.DoesNotContain(rw.Relics, r => r.Rarity == Rarity.Legendary);
            }
        }

        [Fact]
        public void Boss_epic_pity_guarantees_within_twenty_kills()
        {
            var rng = new Rng(11);
            for (int trial = 0; trial < 200; trial++)
            {
                int pity = 0;
                int kills = 0;
                bool got = false;
                while (!got)
                {
                    kills++;
                    var rw = Loot.RollKill(rng, MonsterTier.Boss, 10, 0, ref pity);
                    got = rw.Relics.Count > 0 && rw.Relics[0].Rarity >= Rarity.Epic;
                    Assert.True(kills <= 20, "pity must trigger by the 20th boss kill");
                }
            }
        }

        [Fact]
        public void Secure_moves_satchel_to_stash_with_heat_bonus()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "run-1");
            p.Run.Bounties.Clear();
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(1), Rarity.Rare, 5));
            p.Run.SatchelShards = 40;
            p.Run.SatchelTuning = 1;
            Rules.Delve(p);
            Rules.Delve(p);
            Assert.Equal(2, p.Run.Heat);

            var ev = Rules.Secure(p);
            Assert.Single(p.Stash);
            Assert.Equal(40 + 40 * 2 / 4, p.Material(Materials.Shard));
            Assert.Equal(1, p.Material(Materials.Tuning));
            Assert.Equal(0, p.Run.Heat);
            Assert.False(p.Run.HasUnsecured);
            Assert.Contains(ev, e => e.Kind == EventKind.Secured);
        }

        [Fact]
        public void Defeat_sends_relics_to_lost_and_found_and_keeps_quarter_shards()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "run-1");
            p.Run.Bounties.Clear();
            var rng = new Rng(3);
            for (int i = 0; i < 3; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Uncommon, 5));
            p.Run.SatchelShards = 10;
            p.Run.SatchelTuning = 2;

            Rules.EndRun(p, victory: false);
            Assert.Null(p.Run);
            Assert.Equal(3, p.LostAndFound.Count);
            Assert.Equal(3, p.Material(Materials.Shard)); // ceil(10/4)
            Assert.Equal(0, p.Material(Materials.Tuning));
            Assert.Equal(1, p.Stats.Defeats);
        }

        [Fact]
        public void Lost_and_found_is_capped_and_overflow_becomes_shards()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "a");
            p.Run.Bounties.Clear();
            var rng = new Rng(3);
            for (int i = 0; i < 14; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Common, 5));
            Rules.EndRun(p, victory: false);
            Assert.Equal(Content.LostAndFoundCapacity, p.LostAndFound.Count);
            Assert.Equal(4 * Content.SalvageShards(Rarity.Common), p.Material(Materials.Shard));
        }

        [Fact]
        public void Lost_relic_is_recovered_after_three_rooms_once_per_run()
        {
            var p = NewProfile();
            var rng = new Rng(3);
            p.LostAndFound.Add(Loot.RollRelic(rng, Rarity.Common, 5));
            var best = Loot.RollRelic(rng, Rarity.Epic, 5);
            p.LostAndFound.Add(best);

            Rules.BeginRun(p, "b");
            p.Run.Bounties.Clear();
            Assert.Empty(Rules.OnRoomsCleared(p, 2));
            var ev = Rules.OnRoomsCleared(p, 3);
            Assert.Contains(ev, e => e.Kind == EventKind.Recovered);
            Assert.Contains(best, p.Run.Satchel);
            Assert.Single(p.LostAndFound);
            Assert.Empty(Rules.OnRoomsCleared(p, 6));
        }

        [Fact]
        public void Starting_a_different_run_settles_the_unfinished_one_as_defeat()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "old");
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(1), Rarity.Rare, 5));
            Rules.BeginRun(p, "old"); // 同じランの再開は何もしない
            Assert.Single(p.Run.Satchel);

            Rules.BeginRun(p, "new");
            Assert.Equal("new", p.Run.RunId);
            Assert.Empty(p.Run.Satchel);
            Assert.Single(p.LostAndFound);
        }

        [Fact]
        public void Victory_secures_everything()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "v");
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(1), Rarity.Rare, 5));
            Rules.EndRun(p, victory: true);
            Assert.Single(p.Stash);
            Assert.Equal(1, p.Stats.Victories);
            Assert.Null(p.Run);
        }

        [Fact]
        public void Kills_grant_xp_and_level_ups()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "x");
            var events = new List<GameEvent>();
            for (int i = 0; i < 10; i++) events.AddRange(Rules.OnKill(p, MonsterTier.Boss, 10));
            Assert.True(p.DreamLevel > 1);
            Assert.Contains(events, e => e.Kind == EventKind.LevelUp);
            Assert.Equal(10, p.Run.Kills);
        }

        [Fact]
        public void Satchel_overflow_turns_worst_into_shards()
        {
            var p = NewProfile();
            Rules.BeginRun(p, "s");
            var rng = new Rng(2);
            for (int i = 0; i < Content.SatchelCapacity; i++) p.Run.Satchel.Add(Loot.RollRelic(rng, Rarity.Rare, 5));
            int before = p.Run.SatchelShards;
            for (int i = 0; i < 30 && p.Run.Satchel.Count <= Content.SatchelCapacity && p.Run.SatchelShards == before; i++)
                Rules.OnKill(p, MonsterTier.Boss, 5);
            Assert.True(p.Run.Satchel.Count <= Content.SatchelCapacity);
        }

        [Fact]
        public void Enhance_costs_shards_and_scales_stats()
        {
            var p = NewProfile();
            var r = Give(p, Rarity.Rare, Slot.Weapon);
            int before = r.EffectiveStats().Sum(s => s.Value);
            Assert.Throws<InvalidOperationException>(() => Rules.Enhance(p, r.Uid));
            p.AddMaterial(Materials.Shard, 1000);
            for (int i = 0; i < Content.MaxEnhance; i++) Rules.Enhance(p, r.Uid);
            Assert.Equal(5, r.Enhance);
            Assert.Equal(1000 - (20 + 35 + 60 + 90 + 130), p.Material(Materials.Shard));
            Assert.True(r.EffectiveStats().Sum(s => s.Value) > before);
            Assert.Throws<InvalidOperationException>(() => Rules.Enhance(p, r.Uid));
        }

        [Fact]
        public void Retune_replaces_one_affix_up_to_three_times()
        {
            var p = NewProfile();
            var r = Give(p, Rarity.Rare, Slot.Charm);
            p.AddMaterial(Materials.Tuning, 10);
            for (int i = 0; i < Content.MaxRetunes; i++)
            {
                Rules.Retune(p, r.Uid, 0);
                Rules.ChooseRetune(p, 0);
                var stats = r.Affixes.Select(a => a.Stat).Append(r.Base.ImplicitStat).ToList();
                Assert.Equal(stats.Count, stats.Distinct().Count());
            }
            Assert.Equal(10 - (1 + 2 + 3), p.Material(Materials.Tuning));
            Assert.Throws<InvalidOperationException>(() => Rules.Retune(p, r.Uid, 0));
        }

        [Fact]
        public void Salvage_unequips_and_refunds_and_respects_lock()
        {
            var p = NewProfile();
            var r = Give(p, Rarity.Epic, Slot.Armor);
            Rules.Equip(p, "Hero_A", r.Uid);
            Rules.Equip(p, "Hero_B", r.Uid);
            Rules.ToggleLock(p, r.Uid);
            Assert.Throws<InvalidOperationException>(() => Rules.Salvage(p, r.Uid));
            Rules.ToggleLock(p, r.Uid);
            Rules.Salvage(p, r.Uid);
            Assert.Empty(p.Stash);
            Assert.False(p.IsEquippedAnywhere(r.Uid));
            Assert.Equal(Content.SalvageShards(Rarity.Epic), p.Material(Materials.Shard));
            Assert.Equal(1, p.Material(Materials.Tuning));
        }

        [Fact]
        public void Craft_produces_requested_slot_with_rarity_floor()
        {
            var p = NewProfile();
            p.AddMaterial(Materials.Shard, 10000);
            p.AddMaterial(Materials.Tuning, 100);
            for (int i = 0; i < 30; i++)
            {
                Rules.Craft(p, Slot.Charm, fine: i % 2 == 1);
                var r = p.Stash.Last();
                Assert.Equal(Slot.Charm, r.Slot);
                Assert.True(r.Rarity >= (i % 2 == 1 ? Rarity.Rare : Rarity.Uncommon));
                Assert.NotEqual(Rarity.Legendary, r.Rarity);
            }
        }

        [Fact]
        public void Talents_respect_points_ranks_and_keystone_requirement()
        {
            var p = NewProfile();
            p.DreamLevel = 10; // 9 points
            Assert.Throws<InvalidOperationException>(() => Rules.SetKeystone(p, "H", "t.off.key"));
            Rules.AddTalentRank(p, "H", "t.off.edge");
            Rules.AddTalentRank(p, "H", "t.off.edge");
            Rules.AddTalentRank(p, "H", "t.off.edge");
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "H", "t.off.edge"));
            Rules.AddTalentRank(p, "H", "t.off.swift");
            Rules.AddTalentRank(p, "H", "t.off.swift");
            Rules.AddTalentRank(p, "H", "t.off.swift");
            Rules.SetKeystone(p, "H", "t.off.key");
            Assert.Equal(0, Rules.FreePoints(p, "H"));
            Assert.Throws<InvalidOperationException>(() => Rules.AddTalentRank(p, "H", "t.grd.iron"));

            var b = Build.Compute(p, "H", 0);
            Assert.Equal(9, b.Get(Stat.AttackPct));
            Assert.Equal(9, b.Get(Stat.AttackSpeedPct));
            Assert.Equal(4, b.Get(Power.Momentum));

            // 他のキャラは独立に配分できる
            Assert.Equal(9, Rules.FreePoints(p, "Other"));
            Rules.ResetTalents(p, "H");
            Assert.Equal(9, Rules.FreePoints(p, "H"));
        }

        [Fact]
        public void Build_sums_equipment_caps_and_applies_heat_penalty()
        {
            var p = NewProfile();
            Content.TryGetUnique("unique.endless_dance", out var ud);
            var w = Loot.RollUnique(new Rng(2001), ud, 40);
            p.Stash.Add(w);
            Rules.Equip(p, "H", w.Uid);
            var b0 = Build.Compute(p, "H", 0);
            Assert.Equal(w.Implicit.Value + w.Affixes.Where(a => a.Stat == w.Implicit.Stat).Sum(a => a.Value), b0.Get(w.Implicit.Stat));
            Assert.Equal(2, b0.Powers.Count);

            var b3 = Build.Compute(p, "H", 3);
            Assert.Equal(b0.Stats, b3.Stats);                       // 潜行は能力値を下げない
            Assert.Equal(1f, b0.DamageTakenMultiplier, 3);
            Assert.Equal(1.18f, b3.DamageTakenMultiplier, 3);      // 被ダメージ +6%/段
            Assert.Equal(3, Build.Decode(b3.Encode()).Heat);

            foreach (var kv in b0.Stats) Assert.True(kv.Value <= Content.StatCap(kv.Key));
        }

        [Fact]
        public void Build_encode_decode_roundtrip_and_rejects_garbage()
        {
            var p = NewProfile();
            Rules.Equip(p, "H", Give(p, Rarity.Epic, Slot.Weapon, 30).Uid);
            Rules.Equip(p, "H", Give(p, Rarity.Legendary, Slot.Charm, 30).Uid);
            var b = Build.Compute(p, "H", 2);
            var d = Build.Decode(b.Encode());
            Assert.NotNull(d);
            Assert.Equal(b.Stats, d.Stats);
            Assert.Equal(b.Powers, d.Powers);
            Assert.Equal(2, d.Heat);

            Assert.Null(Build.Decode("garbage"));
            Assert.Null(Build.Decode("s:x=1"));
            var clamped = Build.Decode("s:0=99999,999=5;p:1=99999,0=3;h:99");
            Assert.Equal(Content.StatCap(Stat.AttackPct), clamped.Get(Stat.AttackPct));
            Assert.Equal(Content.PowerCap(Power.Momentum), clamped.Get(Power.Momentum));
            Assert.Equal(Content.MaxHeat, clamped.Heat);
        }

        [Fact]
        public void Profile_roundtrips_through_codec()
        {
            var p = NewProfile();
            p.AddMaterial(Materials.Shard, 123);
            var w = Give(p, Rarity.Legendary, Slot.Weapon, 20);
            w.Enhance = 2;
            w.Locked = true;
            Rules.Equip(p, "Hero_Lacerta", w.Uid);
            p.DreamLevel = 5;
            Rules.AddTalentRank(p, "Hero_Lacerta", "h.lacerta.powder");
            Rules.BeginRun(p, "run-x");
            Rules.Delve(p);
            p.Run.Satchel.Add(Loot.RollRelic(new Rng(9), Rarity.Epic, 12));
            p.LostAndFound.Add(Loot.RollRelic(new Rng(10), Rarity.Rare, 12));
            p.Codex.Add("weapon.chain_sword");

            string text = ProfileCodec.Write(p);
            var notes = new List<string>();
            var q = ProfileCodec.Read(text, notes);
            Assert.Empty(notes);
            Assert.Equal(text, ProfileCodec.Write(q));
            Assert.Equal(w.Uid, q.Hero("Hero_Lacerta").Equipped[(int)Slot.Weapon]);
            Assert.Equal(1, q.Run.Heat);
            Assert.Equal(p.RngState, q.RngState);
        }

        [Fact]
        public void Codec_rejects_tampering_and_quarantines_unknown_relics()
        {
            var p = NewProfile();
            Give(p, Rarity.Rare, Slot.Armor);
            string text = ProfileCodec.Write(p);
            Assert.Throws<LedgerFormatException>(() => ProfileCodec.Read(text.Replace("\"dreamLevel\":1", "\"dreamLevel\":30"), null));

            // 正しいチェックサムのまま未知の基礎IDを含む場合は、その遺物だけ除外する
            var p2 = NewProfile();
            var r = Give(p2, Rarity.Rare, Slot.Armor);
            var good = Give(p2, Rarity.Common, Slot.Charm);
            r.BaseId = "armor.removed_in_future";
            var notes = new List<string>();
            var q = ProfileCodec.Read(ProfileCodec.Write(p2), notes);
            Assert.Single(q.Stash);
            Assert.Equal(good.Uid, q.Stash[0].Uid);
            Assert.NotEmpty(notes);
        }

        [Fact]
        public void Store_saves_atomically_and_recovers_from_backup()
        {
            var fs = new InMemoryFileSystem();
            var store = new ProfileStore(fs, "/save/profile.json", 1);
            var p = store.Load();
            p.AddMaterial(Materials.Shard, 5);
            store.Save(p);
            p.AddMaterial(Materials.Shard, 5);
            store.Save(p);
            Assert.Equal(2, p.Revision);
            Assert.False(fs.Exists(store.TempPath));

            fs.WriteAllText("/save/profile.json", "{broken");
            var store2 = new ProfileStore(fs, "/save/profile.json", 1);
            var q = store2.Load();
            Assert.Equal(1, q.Revision);
            Assert.Equal(5, q.Material(Materials.Shard));
            Assert.NotEmpty(store2.Notes);
        }

        [Fact]
        public void Store_starts_fresh_when_nothing_is_readable()
        {
            var fs = new InMemoryFileSystem();
            fs.WriteAllText("/save/profile.json", "nope");
            var store = new ProfileStore(fs, "/save/profile.json", 1);
            var p = store.Load();
            Assert.Equal(0, p.Revision);
            Assert.NotEmpty(store.Notes);
        }
    }
}
