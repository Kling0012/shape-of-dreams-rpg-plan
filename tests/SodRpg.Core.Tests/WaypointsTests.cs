using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
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
            Waypoints.ApplyKill(p, tier, nightmare, rng, reward, 10, null, room, out _, out _);
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
                Assert.Contains("ゾーン", d.Description.Ja);
                Assert.Contains("zone", d.Description.En);
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
            Assert.Equal(3, a.Run.OfferedWaypoints.Distinct().Count());
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
            var unavailable = Waypoints.All.First(x => !p.Run.OfferedWaypoints.Contains(x.Id)).Id;
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
        [InlineData(Waypoint.CharmRoad, Slot.Charm)]
        [InlineData(Waypoint.HeadRoad, Slot.Head)]
        [InlineData(Waypoint.HandsRoad, Slot.Hands)]
        [InlineData(Waypoint.FeetRoad, Slot.Feet)]
        public void Slot_roads_filter_real_loot_including_uniques_and_add_luck(Waypoint waypoint, Slot slot)
        {
            var p = Run(waypoint);
            var r = Reward(3, Rarity.Legendary);
            Apply(p, r);
            Assert.All(r.Relics, relic => Assert.Equal(slot, relic.Slot));
            Assert.All(r.Relics, relic => Assert.Equal(Rarity.Legendary, relic.Rarity));
            Assert.Equal(1, Rules.KillModifiers(p.Run).Luck);
        }

        [Fact]
        public void Combat_rules_expose_exact_host_values_without_allocating_per_read()
        {
            Assert.Equal(2, Waypoints.Sum(Waypoint.NightmareHunt).NightmareChanceMultiplier);
            Assert.Equal(.5, Waypoints.Sum(Waypoint.GlassAegis).HealingMultiplier);
            Assert.Equal(2, Waypoints.Sum(Waypoint.GlassAegis).ShieldMultiplier);
            Assert.Equal(2, Waypoints.Sum(Waypoint.ResonantRoad).ReactionMultiplier);
            Assert.True(Waypoints.Sum(Waypoint.EndlessNight).AllNightmares);
            Assert.Equal(3, Waypoints.Sum(Waypoint.EndlessNight).AwakeningMultiplier);
            Assert.Equal(.8, Waypoints.Sum(Waypoint.FleetingMemories).MemoryCooldownMultiplier);
            Assert.Equal(1.25, Waypoints.Sum(Waypoint.FleetingMemories).PressureMultiplier);
            Assert.Equal(1.5, Waypoints.Sum(Waypoint.SummonerTrail).SummonPowerMultiplier);
            Assert.Equal(.85, Waypoints.Sum(Waypoint.SummonerTrail).HeroHealthMultiplier);
        }

        [Fact]
        public void Nightmare_hunt_doubles_only_nightmare_loot_with_unique_copy_ids()
        {
            var ordinary = Reward();
            var nightmare = Reward();
            Apply(Run(Waypoint.NightmareHunt), ordinary);
            Apply(Run(Waypoint.NightmareHunt), nightmare, nightmare: true);
            Assert.Single(ordinary.Relics);
            Assert.Equal(2, nightmare.Relics.Count);
            Assert.Equal(2, nightmare.Relics.Select(r => r.Uid).Distinct().Count());
            Assert.Equal(ordinary.Shards * 2, nightmare.Shards);
            Assert.Equal(ordinary.Tuning * 2, nightmare.Tuning);
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
            Assert.Equal(6, boss.Relics.Count);
            Assert.Equal(6, boss.Relics.Select(r => r.Uid).Distinct().Count());
            Assert.Equal(60, boss.Shards);
            Assert.Equal(12, boss.Tuning);
            Assert.Empty(p.Run.DeferredWaypointRelics);
            var next = Reward(seed: 314161);
            Apply(p, next, MonsterTier.Boss);
            Assert.Equal(3, next.Relics.Count);
            Assert.Equal(30, next.Shards);
        }

        [Fact]
        public void Room_limits_survive_clear_callbacks_room_revisits_and_save_load()
        {
            var p = Run(Waypoint.TemperedFinds);
            var first = Reward(3);
            Apply(p, first, room: 9);
            Assert.Equal(2, Assert.Single(first.Relics).Enhance);
            Rules.OnRoomsCleared(p, 1);
            var same = Reward();
            Apply(p, same, room: 9);
            Assert.Empty(same.Relics);
            var next = Reward();
            Apply(p, next, room: 10);
            Assert.Single(next.Relics);
            p = ProfileCodec.Read(ProfileCodec.Write(p), new List<string>());
            var revisit = Reward();
            Apply(p, revisit, room: 9);
            Assert.Empty(revisit.Relics);
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
            int expected = 10 + Content.SalvageShards(shards.Relics[0].Rarity) * 3;
            Apply(Run(Waypoint.ShardRoad), shards);
            Assert.Empty(shards.Relics);
            Assert.Equal(expected, shards.Shards);
            var twin = Reward();
            Apply(Run(Waypoint.TwinCache), twin);
            Assert.Equal(2, twin.Relics.Count);
            Assert.NotEqual(twin.Relics[0].Uid, twin.Relics[1].Uid);
            Assert.Equal(twin.Relics[0].BaseId, twin.Relics[1].BaseId);
            Assert.Equal(0, twin.Tuning);
            var humble = Reward(2, Rarity.Legendary);
            Apply(Run(Waypoint.HumbleForge), humble);
            Assert.All(humble.Relics, r =>
            {
                Assert.Equal(Rarity.Common, r.Rarity);
                Assert.Equal(4, r.Enhance);
                Assert.Equal(1, r.EnhanceMilestones);
                Assert.Equal(Content.AffixCount(Rarity.Common) + 1, r.Affixes.Count);
            });
            var tribute = Reward(2);
            Apply(Run(Waypoint.BossTribute), tribute);
            Assert.Empty(tribute.Relics);
            Assert.Equal(4, tribute.Tuning);
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
            Assert.Equal(.5, Rules.KillModifiers(p.Run).Luck);
        }

        [Fact]
        public void Star_and_awakening_offerings_convert_the_resources()
        {
            var star = Reward();
            Waypoints.ApplyKill(Run(Waypoint.StarOffering), MonsterTier.Normal, false, new Rng(17), star, 10, null, 1, out int starXp, out int awakening);
            Assert.Equal(40, starXp);
            Assert.Equal(0, star.Shards);
            Assert.Equal(0, awakening);
            var pilgrim = Reward(2);
            Waypoints.ApplyKill(Run(Waypoint.AwakeningPilgrimage), MonsterTier.Normal, false, new Rng(17), pilgrim, 10, null, 1, out starXp, out awakening);
            Assert.Equal(0, starXp);
            Assert.Equal(40, awakening);
            Assert.Empty(pilgrim.Relics);
        }

        [Fact]
        public void First_claim_guarantees_one_rare_item_and_supplies_convert_only_boss_relics()
        {
            var p = Run(Waypoint.FirstClaim);
            var first = Reward(0);
            Apply(p, first);
            Assert.Equal(Rarity.Rare, Assert.Single(first.Relics).Rarity);
            var next = Reward(3, Rarity.Legendary);
            Apply(p, next);
            Assert.Empty(next.Relics);
            var supplies = Reward();
            int expected = 10 + Content.SalvageShards(supplies.Relics[0].Rarity) * 4;
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
            Assert.Equal(DreamDepth.ScaleReward(Content.AwakenPoints(MonsterTier.Normal, true), 3 * 1.25), legendary.AwakenPoints);
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
            Assert.Equal((1 + StarProgression.SecureXp + StarProgression.VictoryXp) * 2, p.Hero("Hero_Lacerta").StarXp);
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
