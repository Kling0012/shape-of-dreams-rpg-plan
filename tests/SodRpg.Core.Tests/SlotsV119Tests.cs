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
    public class SlotsV119Tests
    {
        private const string HeroKey = "Hero_Vesper";

        [Theory]
        [InlineData(Slot.Weapon)]
        [InlineData(Slot.Armor)]
        [InlineData(Slot.Charm)]
        [InlineData(Slot.Head)]
        [InlineData(Slot.Hands)]
        [InlineData(Slot.Feet)]
        public void Every_slot_has_bases_affixes_and_rollable_epic_powers(Slot slot)
        {
            Assert.Equal(15, Content.BasesFor(slot).Count());
            Assert.True(Content.AffixPool(slot).Count >= 8);
            Assert.True(Content.PowerPool(slot).Count >= 8);
            var rng = new Rng((ulong)(119 + (int)slot));
            for (int i = 0; i < 200; i++)
            {
                var relic = Loot.RollRelic(rng, Rarity.Epic, 10, slot: slot);
                Assert.Equal(slot, relic.Slot);
                Assert.Equal(Rarity.Epic, relic.Rarity);
                var power = Assert.Single(relic.Powers);
                Assert.Contains(Content.PowerPool(slot), p => p.Power == power.Power &&
                    power.Value >= p.Min && power.Value <= p.Max);
            }
        }

        [Theory]
        [InlineData(Slot.Head)]
        [InlineData(Slot.Hands)]
        [InlineData(Slot.Feet)]
        public void New_slots_have_twelve_standalone_uniques_with_matching_bases(Slot slot)
        {
            var prefix = slot.ToString().ToLowerInvariant() + ".";
            var uniques = Content.Uniques.Where(u => u.BaseId.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            Assert.Equal(12, uniques.Count);
            foreach (var unique in uniques)
            {
                Assert.True(Content.TryGetBase(unique.BaseId, out var baseDef));
                Assert.Equal(slot, baseDef.Slot);
                Assert.Null(unique.SetId);
                var relic = Loot.RollUnique(new Rng(119), unique, 10);
                Assert.Equal(slot, relic.Slot);
                Assert.Equal(unique.Id, relic.UniqueId);
            }
        }

        private static Profile EquippedProfile()
        {
            var p = Profile.CreateNew(119);
            Onboarding.GrantStarterKit(p);
            Assert.True(Onboarding.AutoEquipStarter(p, HeroKey));
            return p;
        }

        [Fact]
        public void Six_equipped_slots_and_grant_flags_survive_clone_and_save_load()
        {
            var p = EquippedProfile();
            var clone = p.Clone();
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(ProfileCodec.Write(clone), notes);
            Assert.Empty(notes);
            Assert.True(clone.StarterV119Granted);
            Assert.True(loaded.StarterGranted);
            Assert.True(loaded.StarterV119Granted);
            Assert.Equal(6, loaded.Hero(HeroKey).Equipped.Length);
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                string uid = p.Hero(HeroKey).Equipped[(int)slot];
                Assert.Equal(uid, clone.Hero(HeroKey).Equipped[(int)slot]);
                Assert.Equal(uid, loaded.Hero(HeroKey).Equipped[(int)slot]);
                Assert.Equal(slot, Rules.EquippedRelic(loaded, HeroKey, slot).Slot);
            }
            clone.Hero(HeroKey).Equipped[(int)Slot.Head] = null;
            Assert.NotNull(p.Hero(HeroKey).Equipped[(int)Slot.Head]);
        }

        private static object LegacySlots(object value)
        {
            if (value is JsonObject obj)
            {
                var copy = new JsonObject();
                foreach (var kv in obj.Properties)
                {
                    if (kv.Key == "starterV119Granted") continue;
                    if (kv.Key == "equipped" && kv.Value is List<object> equipped)
                        copy.Add(kv.Key, equipped.Take(3).ToList());
                    else
                        copy.Add(kv.Key, LegacySlots(kv.Value));
                }
                return copy;
            }
            if (value is List<object> list) return list.Select(LegacySlots).ToList();
            return value;
        }

        private static string WithBody(object body)
        {
            string checksum;
            using (var sha = SHA256.Create())
            {
                checksum = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(body))))
                    .Replace("-", "").ToLowerInvariant();
            }
            return Json.Write(new JsonObject().Add("format", ProfileCodec.Format)
                .Add("version", (long)Profile.CurrentVersion).Add("checksum", checksum).Add("body", body));
        }

        [Fact]
        public void Old_three_entry_saves_keep_saved_indices_and_leave_new_slots_empty()
        {
            var p = EquippedProfile();
            var root = (JsonObject)Json.Parse(ProfileCodec.Write(p));
            Assert.True(root.TryGet("body", out object body));
            var notes = new List<string>();
            var loaded = ProfileCodec.Read(WithBody(LegacySlots(body)), notes);
            Assert.Empty(notes);
            Assert.True(loaded.StarterGranted);
            Assert.False(loaded.StarterV119Granted);
            foreach (var slot in new[] { Slot.Weapon, Slot.Armor, Slot.Charm })
            {
                Assert.Equal(p.Hero(HeroKey).Equipped[(int)slot], loaded.Hero(HeroKey).Equipped[(int)slot]);
                Assert.Equal(slot, Rules.EquippedRelic(loaded, HeroKey, slot).Slot);
            }
            foreach (var slot in new[] { Slot.Head, Slot.Hands, Slot.Feet })
                Assert.Null(loaded.Hero(HeroKey).Equipped[(int)slot]);
        }

        [Fact]
        public void Save_load_rejects_a_relic_stored_at_the_wrong_slot_index()
        {
            var p = EquippedProfile();
            var equipped = p.Hero(HeroKey).Equipped;
            string head = equipped[(int)Slot.Head];
            equipped[(int)Slot.Head] = equipped[(int)Slot.Feet];
            equipped[(int)Slot.Feet] = head;
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Null(loaded.Hero(HeroKey).Equipped[(int)Slot.Head]);
            Assert.Null(loaded.Hero(HeroKey).Equipped[(int)Slot.Feet]);
            Assert.Equal(p.Hero(HeroKey).Equipped[(int)Slot.Hands], loaded.Hero(HeroKey).Equipped[(int)Slot.Hands]);
        }

        [Fact]
        public void Existing_players_receive_new_slot_starters_once_even_after_reloading()
        {
            var p = Profile.CreateNew(119);
            p.StarterGranted = true;
            var rng = p.TakeRng();
            foreach (var slot in new[] { Slot.Weapon, Slot.Armor, Slot.Charm })
            {
                var relic = Loot.RollRelic(rng, Rarity.Uncommon, 1, slot);
                p.Stash.Add(relic);
                p.StarterUids.Add(relic.Uid);
                p.Codex.Add(relic.BaseId);
            }
            p.StoreRng(rng);
            Onboarding.AutoEquipStarter(p, HeroKey);
            var equippedBefore = p.Hero(HeroKey).Equipped.ToArray();
            var originalUids = p.Stash.Select(r => r.Uid).ToHashSet();
            var events = Onboarding.GrantNewSlotStarters(p);
            Assert.Single(events);
            Assert.Equal(EventKind.Info, events[0].Kind);
            var given = p.Stash.Where(r => !originalUids.Contains(r.Uid)).ToList();
            Assert.Equal(3, given.Count);
            Assert.Equal(new[] { Slot.Head, Slot.Hands, Slot.Feet }, given.Select(r => r.Slot).OrderBy(s => s));
            Assert.All(given, r =>
            {
                Assert.Equal(Rarity.Uncommon, r.Rarity);
                Assert.Equal(1, r.ItemLevel);
                Assert.Contains(r.Uid, p.StarterUids);
                Assert.Contains(r.BaseId, p.Codex);
            });
            Assert.Equal(equippedBefore, p.Hero(HeroKey).Equipped);
            Assert.True(p.StarterV119Granted);
            ulong state = p.RngState;
            Assert.Empty(Onboarding.GrantNewSlotStarters(p));
            Assert.Equal(6, p.Stash.Count);
            Assert.Equal(state, p.RngState);
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.True(loaded.StarterV119Granted);
            Assert.Empty(Onboarding.GrantNewSlotStarters(loaded));
            Assert.Equal(6, loaded.Stash.Count);
        }

        [Fact]
        public void New_players_get_six_starters_without_a_second_migration_grant()
        {
            var p = Profile.CreateNew(119);
            ulong state = p.RngState;
            Assert.Empty(Onboarding.GrantNewSlotStarters(p));
            Assert.Empty(p.Stash);
            Assert.False(p.StarterV119Granted);
            Assert.Equal(state, p.RngState);
            var given = Onboarding.GrantStarterKit(p);
            Assert.Equal(6, given.Count);
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                Assert.Single(given.Where(r => r.Slot == slot));
            Assert.All(given, r => Assert.Equal(Rarity.Uncommon, r.Rarity));
            Assert.True(p.StarterGranted);
            Assert.True(p.StarterV119Granted);
            state = p.RngState;
            Assert.Empty(Onboarding.GrantStarterKit(p));
            Assert.Empty(Onboarding.GrantNewSlotStarters(p));
            Assert.Equal(6, p.Stash.Count);
            Assert.Equal(state, p.RngState);
        }

        [Theory]
        [InlineData(Line.Offense, 2, 5, 5, 0, 0, 0, 0, 0, 0, 0)]
        [InlineData(Line.Offense, 3, 5, 5, 6, 0, 0, 0, 0, 0, 0)]
        [InlineData(Line.Offense, 4, 5, 5, 6, 4, 0, 0, 0, 0, 0)]
        [InlineData(Line.Offense, 5, 5, 5, 6, 4, 0, 0, 0, 0, 0)]
        [InlineData(Line.Offense, 6, 13, 13, 6, 4, 0, 0, 0, 0, 0)]
        [InlineData(Line.Guard, 2, 0, 0, 0, 0, 8, 0, 0, 0, 0)]
        [InlineData(Line.Guard, 3, 0, 0, 0, 0, 8, 6, 0, 0, 0)]
        [InlineData(Line.Guard, 4, 0, 0, 0, 0, 8, 6, 12, 0, 0)]
        [InlineData(Line.Guard, 5, 0, 0, 0, 0, 8, 6, 12, 0, 0)]
        [InlineData(Line.Guard, 6, 0, 0, 0, 0, 20, 12, 12, 0, 0)]
        [InlineData(Line.Resonance, 2, 0, 0, 0, 0, 0, 0, 0, 8, 0)]
        [InlineData(Line.Resonance, 3, 0, 0, 0, 0, 0, 0, 0, 8, 4)]
        [InlineData(Line.Resonance, 4, 0, 0, 0, 0, 0, 0, 0, 16, 4)]
        [InlineData(Line.Resonance, 5, 0, 0, 0, 0, 0, 0, 0, 16, 4)]
        [InlineData(Line.Resonance, 6, 0, 8, 0, 0, 0, 0, 0, 16, 8)]
        public void Line_bonus_tiers_are_cumulative(Line line, int count, int attack, int power,
            int attackSpeed, int crit, int armor, int health, int tenacity, int haste, int moveSpeed)
        {
            var expected = new Dictionary<Stat, int>
            {
                [Stat.AttackPct] = attack,
                [Stat.PowerPct] = power,
                [Stat.AttackSpeedPct] = attackSpeed,
                [Stat.CritChancePct] = crit,
                [Stat.Armor] = armor,
                [Stat.MaxHealthPct] = health,
                [Stat.Tenacity] = tenacity,
                [Stat.Haste] = haste,
                [Stat.MoveSpeedPct] = moveSpeed,
            };
            var bonuses = Content.SetBonus(line, count).ToList();
            foreach (Stat stat in Enum.GetValues(typeof(Stat)))
                Assert.Equal(expected.TryGetValue(stat, out int value) ? value : 0,
                    bonuses.Where(b => b.Stat == stat).Sum(b => b.Value));
        }

        [Fact]
        public void Unfocused_base_drops_are_distributed_over_all_six_slots()
        {
            const int draws = 6000;
            var rng = new Rng(119);
            var counts = new int[Content.SlotCount];
            for (int i = 0; i < draws; i++)
                counts[(int)Loot.RollRelic(rng, Rarity.Rare, 10).Slot]++;
            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
                Assert.InRange(counts[(int)slot] / (double)draws, 0.12, 0.21);
        }

        [Fact]
        public void Completed_sets_require_all_defined_pieces_not_duplicates_or_new_slots()
        {
            var p = Profile.CreateNew(119);
            var feat = Feats.All.First(f => f.Kind == FeatKind.SetsCompleted);
            var pieces = Content.Uniques.Where(u => u.SetId == Content.Sets[0].Id).ToList();
            var rng = new Rng(119);
            p.Stash.Add(Loot.RollUnique(rng, pieces[0], 10));
            p.Stash.Add(Loot.RollUnique(rng, pieces[1], 10));
            Assert.Equal(0, Feats.Progress(p, feat));
            p.Stash.Add(Loot.RollUnique(rng, pieces[0], 10));
            p.Stash.Add(Loot.RollRelic(rng, Rarity.Legendary, 10, Slot.Head));
            Assert.Equal(0, Feats.Progress(p, feat));
            var last = Loot.RollUnique(rng, pieces[2], 10);
            p.Stash.Add(last);
            Rules.Equip(p, HeroKey, last.Uid);
            Assert.Equal(1, Feats.Progress(p, feat));
            Rules.Unequip(p, HeroKey, last.Slot);
            p.Stash.Remove(last);
            p.Stash.Add(Loot.RollUnique(rng, pieces[1], 10));
            Assert.Equal(0, Feats.Progress(p, feat));
        }
    }
}
