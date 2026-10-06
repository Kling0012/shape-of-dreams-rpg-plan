using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    internal static class PactDailyWaypointTestValues
    {
        internal static JsonElement Raw(string table)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "tools", "balance", table + ".json");
                if (!File.Exists(path)) continue;
                using (var document = JsonDocument.Parse(File.ReadAllText(path)))
                    return document.RootElement.Clone();
            }
            throw new InvalidOperationException("Balance table not found: " + table);
        }

        internal static double Number(string table, string path)
        {
            var value = Raw(table);
            foreach (string key in path.Split('.')) value = value.GetProperty(key);
            return value.GetDouble();
        }

        internal static int Integer(string table, string path) => checked((int)Number(table, path));
        internal static double Waypoint(Waypoint id, string field) => Number("waypoints", "definitions." + id + "." + field);
        internal static int Reward(string field) => Integer("waypoints", "rewards." + field);
    }

    public class WaypointsTests
    {
        private static Profile Run(Waypoint waypoint = Waypoint.None, int depth = 0)
        {
            var p = Profile.CreateNew(9137);
            p.LastDreamDepth = depth;
            Rules.BeginRun(p, "waypoint-run", heroKey: "Hero_Lacerta");
            p.Run.Bounties.Clear();
            p.Run.ActiveWaypoint = waypoint;
            return p;
        }

        private static KillReward Reward(int count = 1, Rarity rarity = Rarity.Rare, ulong seed = 314159)
        {
            var reward = new KillReward { Shards = 10, Tuning = 2, Xp = 5 };
            var rng = new Rng(seed);
            for (int i = 0; i < count; i++) reward.Relics.Add(Loot.RollRelic(rng, rarity, 10));
            return reward;
        }

        private static void Apply(Profile p, KillReward reward, MonsterTier tier = MonsterTier.Normal, bool nightmare = false, int room = 4)
        {
            var rng = p.TakeRng();
            Waypoints.ApplyKill(p, tier, nightmare, rng, reward, 10, null, room, p.Run.ActiveWaypoint, out _, out _);
            p.StoreRng(rng);
        }

        [Fact]
        public void Twenty_four_cards_have_unique_ids_names_and_complete_bilingual_text()
        {
            Assert.Equal(24, Waypoints.All.Count);
            Assert.Equal(24, Waypoints.All.Select(x => x.Id).Distinct().Count());
            Assert.Equal(24, Waypoints.All.Select(x => x.Name.Ja).Distinct().Count());
            Assert.Equal(24, Waypoints.All.Select(x => x.Name.En).Distinct().Count());
            Assert.All(Waypoints.All, d =>
            {
                Assert.Same(d, Waypoints.Get(d.Id));
                Assert.Same(d.Effects, Waypoints.Sum(d.Id));
                Assert.False(string.IsNullOrWhiteSpace(d.Name.Ja));
                Assert.False(string.IsNullOrWhiteSpace(d.Name.En));
            });
            Assert.Null(Waypoints.Get((Waypoint)999));
            Assert.Equal(1, Waypoints.Sum((Waypoint)999).ReactionMultiplier);
        }

        [Fact]
        public void Offers_are_deterministic_unique_and_advance_rng_once_per_secure_point()
        {
            var a = Run();
            var b = a.Clone();
            Rules.ReachSecurePoint(a);
            Rules.ReachSecurePoint(b);
            Assert.Equal(a.Run.OfferedWaypoints, b.Run.OfferedWaypoints);
            Assert.Equal(PactDailyWaypointTestValues.Integer("waypoints", "offered"), a.Run.OfferedWaypoints.Distinct().Count());
            Assert.Equal(a.RngState, b.RngState);
            ulong rng = a.RngState;
            Rules.ReachSecurePoint(a);
            Assert.Equal(rng, a.RngState);
            Assert.Equal(1, a.Run.WaypointGeneration);
            var loaded = ProfileCodec.Read(ProfileCodec.Write(a), new List<string>());
            Assert.Equal(a.Run.OfferedWaypoints, loaded.Run.OfferedWaypoints);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Pick_is_pending_until_departure_then_expires_at_next_secure_point(bool secure)
        {
            var p = Run();
            Rules.ReachSecurePoint(p);
            var chosen = p.Run.OfferedWaypoints[0];
            Rules.PickWaypoint(p, chosen);
            Assert.Equal(Waypoint.None, p.Run.ActiveWaypoint);
            Assert.Equal(chosen, p.Run.PendingWaypoint);
            if (secure) Rules.Secure(p); else Rules.Delve(p);
            Assert.Equal(chosen, p.Run.ActiveWaypoint);
            Assert.Empty(p.Run.OfferedWaypoints);
            Rules.Secure(p);
            Assert.Equal(chosen, p.Run.ActiveWaypoint);
            Rules.ReachSecurePoint(p);
            Assert.Equal(Waypoint.None, p.Run.ActiveWaypoint);
            Assert.Equal(Waypoint.None, p.Run.PendingWaypoint);
            Assert.False(p.Run.WaypointChosen);
            Assert.Equal(2, p.Run.WaypointGeneration);
        }

        [Fact]
        public void None_and_omitted_choice_are_valid_and_unoffered_choice_does_not_mutate()
        {
            var p = Run();
            Assert.Throws<InvalidOperationException>(() => Rules.PickWaypoint(p, Waypoint.WeaponRoad));
            Rules.ReachSecurePoint(p);
            string before = ProfileCodec.Write(p);
            var unavailable = Waypoints.All.Select(x => x.Id).Where(x => !p.Run.OfferedWaypoints.Contains(x)).DefaultIfEmpty((Waypoint)999).First();
            Assert.Throws<InvalidOperationException>(() => Rules.PickWaypoint(p, unavailable));
            Assert.Equal(before, ProfileCodec.Write(p));
            Rules.PickWaypoint(p, Waypoint.None);
            Assert.True(p.Run.WaypointChosen);
            Rules.Delve(p);
            Assert.Equal(Waypoint.None, p.Run.ActiveWaypoint);
            Rules.ReachSecurePoint(p);
            Rules.Secure(p);
            Assert.Equal(Waypoint.None, p.Run.ActiveWaypoint);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Run_end_clears_all_waypoint_state_even_when_referenced_by_host(bool victory)
        {
            var p = Run(Waypoint.BossHoard);
            var run = p.Run;
            Apply(p, Reward());
            Assert.NotEmpty(run.DeferredWaypointRelics);
            Rules.EndRun(p, victory);
            Assert.Null(p.Run);
            Assert.Equal(Waypoint.None, run.ActiveWaypoint);
            Assert.Empty(run.DeferredWaypointRelics);
            Assert.Equal(0, run.DeferredWaypointShards);
        }

        [Theory]
        [InlineData(Waypoint.WeaponRoad, Slot.Weapon)]
        [InlineData(Waypoint.ArmorRoad, Slot.Armor)]
        [InlineData(Waypoint.FeetRoad, Slot.Feet)]
        public void Slot_roads_filter_real_loot_including_uniques_and_add_luck(Waypoint waypoint, Slot slot)
        {
            var p = Run(waypoint);
            var r = Reward(3, Rarity.Legendary);
            Apply(p, r);
            Assert.All(r.Relics, relic => Assert.Equal(slot, relic.Slot));
            Assert.All(r.Relics, relic => Assert.Equal(Rarity.Legendary, relic.Rarity));
            Assert.Equal(PactDailyWaypointTestValues.Waypoint(waypoint, "luck"), Rules.KillModifiers(p.Run).Luck);
        }


        [Fact]
        public void Nightmare_hunt_doubles_only_nightmare_loot_with_unique_copy_ids()
        {
            var ordinary = Reward();
            var nightmare = Reward();
            Apply(Run(Waypoint.NightmareHunt), ordinary);
            Apply(Run(Waypoint.NightmareHunt), nightmare, nightmare: true);
            Assert.Single(ordinary.Relics);
            double multiplier = PactDailyWaypointTestValues.Waypoint(Waypoint.NightmareHunt, "nightmareRewardMultiplier");
            Assert.Equal((int)multiplier, nightmare.Relics.Count);
            Assert.Equal((int)multiplier, nightmare.Relics.Select(r => r.Uid).Distinct().Count());
            Assert.Equal((int)Math.Round(ordinary.Shards * multiplier, MidpointRounding.AwayFromZero), nightmare.Shards);
            Assert.Equal((int)Math.Round(ordinary.Tuning * multiplier, MidpointRounding.AwayFromZero), nightmare.Tuning);
        }

        [Fact]
        public void Hoard_waits_for_actual_boss_not_reward_tier_then_pays_once()
        {
            var p = Run(Waypoint.BossHoard);
            var first = Reward();
            Apply(p, first, MonsterTier.MiniBoss, true);
            Assert.True(first.IsEmpty);
            Assert.Equal(5, first.Xp);
            var boss = Reward(seed: 314160);
            Apply(p, boss, MonsterTier.Boss);
            int multiplier = PactDailyWaypointTestValues.Reward("hoardRewardMultiplier");
            Assert.Equal(2 * multiplier, boss.Relics.Count);
            Assert.Equal(2 * multiplier, boss.Relics.Select(r => r.Uid).Distinct().Count());
            Assert.Equal(20 * multiplier, boss.Shards);
            Assert.Equal(4 * multiplier, boss.Tuning);
            Assert.Empty(p.Run.DeferredWaypointRelics);
            var next = Reward(seed: 314161);
            Apply(p, next, MonsterTier.Boss);
            Assert.Equal(multiplier, next.Relics.Count);
            Assert.Equal(10 * multiplier, next.Shards);
        }

        [Fact]
        public void Room_limits_survive_clear_callbacks_room_revisits_and_save_load()
        {
            var p = Run(Waypoint.TemperedFinds);
            int limit = (int)PactDailyWaypointTestValues.Waypoint(Waypoint.TemperedFinds, "maxRelicsPerRoom");
            p.Run.WaypointRoom = 9;
            p.Run.WaypointRelicsInRoom = Math.Max(0, limit - 3);
            var first = Reward(5);
            Apply(p, first, room: 9);
            Assert.Equal(Math.Min(3, limit), first.Relics.Count);
            Assert.All(first.Relics, r => Assert.Equal(Math.Min(Content.MaxEnhanceFor(r.Rarity, r.LimitBreaks),
                (int)PactDailyWaypointTestValues.Waypoint(Waypoint.TemperedFinds, "enhancement")), r.Enhance));
            Rules.OnRoomsCleared(p, 1);
            var same = Reward();
            Apply(p, same, room: 9);
            Assert.Empty(same.Relics);
            var next = Reward();
            Apply(p, next, room: 10);
            Assert.Equal(Math.Min(1, limit), next.Relics.Count);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var revisit = Reward();
            Apply(p, revisit, room: 9);
            Assert.Equal(Math.Min(1, Math.Max(0, limit - 1)), revisit.Relics.Count);
        }

        [Fact]
        public void Epic_mirage_has_floor_and_no_shard_leaks_from_variants_or_satchel_overflow()
        {
            var p = Run(Waypoint.EpicMirage);
            for (int i = 0; i < Workshop.SatchelCapacity(p); i++) p.Run.Satchel.Add(Reward().Relics[0]);
            var variant = Variants.All.FirstOrDefault(x => x.ShardBonusPct != 100);
            Rules.OnKill(p, MonsterTier.Boss, 10, variantId: variant?.Id);
            Assert.Equal(0, p.Run.SatchelShards);
            Assert.All(p.Run.Satchel.Where(r => r.Rarity != Rarity.Rare), r => Assert.True(r.Rarity >= Rarity.Epic));
            var reward = Reward(2, Rarity.Common);
            Apply(Run(Waypoint.EpicMirage), reward);
            Assert.All(reward.Relics, r => Assert.True(r.Rarity >= Rarity.Epic));
            Assert.Equal(0, reward.Shards);
        }

        [Fact]
        public void Salvage_twin_humble_and_boss_tribute_transform_the_actual_items()
        {
            var shards = Reward();
            int expected = 10 + Content.SalvageShards(shards.Relics[0].Rarity) * (int)PactDailyWaypointTestValues.Waypoint(Waypoint.ShardRoad, "relicSalvageMultiplier");
            Apply(Run(Waypoint.ShardRoad), shards);
            if (PactDailyWaypointTestValues.Waypoint(Waypoint.ShardRoad, "relicSalvageMultiplier") > 0) Assert.Empty(shards.Relics);
            else Assert.Single(shards.Relics);
            Assert.Equal(expected, shards.Shards);
            var twin = Reward();
            Apply(Run(Waypoint.TwinCache), twin);
            Assert.Equal(PactDailyWaypointTestValues.Reward("twinRelicCopies"), twin.Relics.Count);
            Assert.Equal(twin.Relics.Count, twin.Relics.Select(r => r.Uid).Distinct().Count());
            Assert.All(twin.Relics, r => Assert.Equal(twin.Relics[0].BaseId, r.BaseId));
            double tuningMultiplier = PactDailyWaypointTestValues.Waypoint(Waypoint.TwinCache, "tuningMultiplier");
            Assert.Equal(tuningMultiplier <= 0 ? 0 : Math.Max(1, (int)Math.Round(2 * tuningMultiplier, MidpointRounding.AwayFromZero)), twin.Tuning);
            var humble = Reward(2, Rarity.Legendary);
            Apply(Run(Waypoint.HumbleForge), humble);
            Assert.All(humble.Relics, r =>
            {
                Assert.Equal(Rarity.Common, r.Rarity);
                int enhancement = Math.Min(Content.MaxEnhanceFor(Rarity.Common, 0), (int)PactDailyWaypointTestValues.Waypoint(Waypoint.HumbleForge, "enhancement"));
                int milestones = ForgeBalanceTests.Milestones(enhancement);
                Assert.Equal(enhancement, r.Enhance);
                Assert.Equal(milestones, r.EnhanceMilestones);
                Assert.Equal(Content.AffixCount(Rarity.Common) + milestones, r.Affixes.Count);
            });
            var tribute = Reward(2);
            Apply(Run(Waypoint.BossTribute), tribute);
            Assert.Empty(tribute.Relics);
            Assert.Equal(2 + 2 * PactDailyWaypointTestValues.Reward("tuningPerNonBossRelic"), tribute.Tuning);
            var boss = Reward(2, Rarity.Common);
            Apply(Run(Waypoint.BossTribute), boss, MonsterTier.Boss);
            Assert.All(boss.Relics, r => Assert.True(r.Rarity >= Rarity.Epic));
        }

        [Fact]
        public void Sixfold_cycles_slots_across_kills_and_save_load()
        {
            var p = Run(Waypoint.SixfoldRoad);
            var first = Reward(4);
            Apply(p, first);
            Assert.Equal(new[] { Slot.Weapon, Slot.Armor, Slot.Charm, Slot.Head }, first.Relics.Select(r => r.Slot));
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var next = Reward(3);
            Apply(p, next);
            Assert.Equal(new[] { Slot.Hands, Slot.Feet, Slot.Weapon }, next.Relics.Select(r => r.Slot));
            Assert.Equal(PactDailyWaypointTestValues.Waypoint(Waypoint.SixfoldRoad, "luck"), Rules.KillModifiers(p.Run).Luck);
        }

        [Fact]
        public void Star_and_awakening_offerings_convert_the_resources()
        {
            var star = Reward();
            var offering = Run(Waypoint.StarOffering);
            Waypoints.ApplyKill(offering, MonsterTier.Normal, false, new Rng(17), star, 10, null, 1, Waypoint.StarOffering, out int starXp, out int awakening);
            Assert.Equal((int)Math.Min(int.MaxValue, 10L * PactDailyWaypointTestValues.Reward("starXpPerShard")), starXp);
            Assert.Equal(0, star.Shards);
            Assert.Equal(0, awakening);
            var pilgrim = Reward(2);
            Waypoints.ApplyKill(Run(Waypoint.AwakeningPilgrimage), MonsterTier.Normal, false, new Rng(17), pilgrim, 10, null, 1, Waypoint.AwakeningPilgrimage, out starXp, out awakening);
            Assert.Equal(0, starXp);
            Assert.Equal(2 * (int)PactDailyWaypointTestValues.Waypoint(Waypoint.AwakeningPilgrimage, "awakeningPerRelic"), awakening);
            if (PactDailyWaypointTestValues.Waypoint(Waypoint.AwakeningPilgrimage, "awakeningPerRelic") > 0) Assert.Empty(pilgrim.Relics);
            else Assert.Equal(2, pilgrim.Relics.Count);
        }

        [Fact]
        public void First_claim_guarantees_one_rare_item_and_supplies_convert_only_boss_relics()
        {
            var p = Run(Waypoint.FirstClaim);
            var first = Reward(0);
            Apply(p, first);
            Assert.Equal(Math.Min(1, (int)PactDailyWaypointTestValues.Waypoint(Waypoint.FirstClaim, "maxRelicsPerRoom")), first.Relics.Count);
            Assert.All(first.Relics, r => Assert.Equal(Rarity.Rare, r.Rarity));
            var next = Reward(3, Rarity.Legendary);
            Apply(p, next);
            Assert.Equal(Math.Min(3, Math.Max(0, (int)PactDailyWaypointTestValues.Waypoint(Waypoint.FirstClaim, "maxRelicsPerRoom") - first.Relics.Count)), next.Relics.Count);
            var supplies = Reward();
            int expected = 10 + Content.SalvageShards(supplies.Relics[0].Rarity) * PactDailyWaypointTestValues.Reward("bossSalvageMultiplier");
            Apply(Run(Waypoint.SupplyLine), supplies, MonsterTier.Boss);
            Assert.Empty(supplies.Relics);
            Assert.Equal(expected, supplies.Shards);
            var normal = Reward();
            Apply(Run(Waypoint.SupplyLine), normal);
            Assert.Single(normal.Relics);
        }

        [Fact]
        public void Depth_and_waypoint_awaken_multipliers_round_only_after_combining()
        {
            var p = Run(Waypoint.EndlessNight, 1);
            var legendary = Loot.RollRelic(new Rng(42), Rarity.Legendary, 10);
            p.Stash.Add(legendary);
            p.Hero("Hero_Lacerta").Equipped[(int)legendary.Slot] = legendary.Uid;
            Rules.OnKill(p, MonsterTier.Normal, 10, NightmareAffix.Berserk);
            double multiplier = PactDailyWaypointTestValues.Waypoint(Waypoint.EndlessNight, "awakeningMultiplier") * DreamDepth.AwakeningMultiplier(1);
            Assert.Equal((int)Math.Round(Content.AwakenPoints(MonsterTier.Normal, true) * multiplier, MidpointRounding.AwayFromZero), legendary.AwakenPoints);
            Assert.Equal(DreamDepth.ScaleReward(StarProgression.KillXp(MonsterTier.Normal, true), 1.2), p.Hero("Hero_Lacerta").StarXp);
        }

        [Fact]
        public void Depth_clamps_persists_and_applies_to_kill_secure_and_victory_star_rewards()
        {
            var p = Run(depth: 99);
            Assert.Equal(5, p.LastDreamDepth);
            Assert.Equal(5, p.Run.DreamDepth);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(5, p.LastDreamDepth);
            Assert.Equal(5, p.Run.DreamDepth);
            Assert.Equal(1.25, Rules.KillModifiers(p.Run).Luck);
            Rules.OnKill(p, MonsterTier.Normal, 10);
            Rules.Secure(p);
            Rules.EndRun(p, true);
            Assert.Equal((StarProgressionBalanceTests.KillXp(MonsterTier.Normal) + StarProgressionBalanceTests.Number("rewards", "secureXp")
                + StarProgressionBalanceTests.Number("rewards", "victoryXp")) * 2, p.Hero("Hero_Lacerta").StarXp);
            Rules.BeginRun(p, "second", dreamDepth: -4);
            Assert.Equal(0, p.Run.DreamDepth);
            Assert.Equal(5, p.LastDreamDepth);
        }

        [Fact]
        public void Clone_and_codec_preserve_pending_decision_and_deferred_hoard_independently()
        {
            var p = Run(Waypoint.BossHoard, 3);
            Apply(p, Reward());
            var clone = p.Clone();
            Assert.NotSame(p.Run.DeferredWaypointRelics[0], clone.Run.DeferredWaypointRelics[0]);
            var loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(ProfileCodec.Write(p), ProfileCodec.Write(loaded));
            Rules.ReachSecurePoint(p);
            Rules.PickWaypoint(p, p.Run.OfferedWaypoints[0]);
            loaded = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            Assert.Equal(p.Run.PendingWaypoint, loaded.Run.PendingWaypoint);
            Assert.True(loaded.Run.WaypointChosen);
            Assert.Equal(p.Run.OfferedWaypoints, loaded.Run.OfferedWaypoints);
        }
    }
}
