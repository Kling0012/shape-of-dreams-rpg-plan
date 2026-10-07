using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class DreamEssencesTests
    {
        private const string Normal = "St_Q_Fleche";
        private const string Other = "St_Q_Discipline";
        private const string Movement = "St_M_FlashStep";
        private const string Identity = "St_D_ScarOfTheWind";

        private static DreamSocketing Socket(string memory, string id, int quality) =>
            new DreamSocketing { Memory = memory, EssenceId = id, Quality = quality };

        [Fact]
        public void Twelve_distinct_definitions_with_names_in_both_languages()
        {
            Assert.Equal(12, DreamEssences.All.Count);
            Assert.Equal(12, DreamEssences.All.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(12, DreamEssences.All.Select(d => d.Name.Ja).Distinct().Count());
            Assert.Equal(12, DreamEssences.All.Select(d => d.Name.En).Distinct().Count());
            foreach (var def in DreamEssences.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(def.Name.Ja));
                Assert.False(string.IsNullOrWhiteSpace(def.Name.En));
                Assert.False(string.IsNullOrWhiteSpace(def.Concept.Ja));
                Assert.False(string.IsNullOrWhiteSpace(def.Concept.En));
                Assert.DoesNotContain("Gem_", def.Name.Ja + def.Concept.Ja + def.Name.En + def.Concept.En);
                Assert.DoesNotContain("St_", def.Name.Ja + def.Concept.Ja + def.Name.En + def.Concept.En);
                Assert.Same(def, DreamEssences.Find(def.Id));
            }
            Assert.Null(DreamEssences.Find("nope"));
            Assert.Null(DreamEssences.Find(null));
        }

        [Fact]
        public void Each_definition_uses_a_different_existing_effect_except_where_the_trigger_differs()
        {
            // Resolve keeps one essence per effect family, so two essences sharing an effect would silently override each other.
            Assert.Equal(DreamEssences.All.Count, DreamEssences.All.Select(d => d.Effect).Distinct().Count());
        }

        [Fact]
        public void Every_quality_passes_the_existing_validation_and_is_never_clamped()
        {
            foreach (var def in DreamEssences.All)
                for (int q = DreamEssences.MinQuality; q <= DreamEssences.MaxQuality; q++)
                {
                    var gimmick = DreamEssences.ToGimmick(def, q);
                    Assert.True(Gimmicks.ValidDef(gimmick), def.Id + " q" + q);
                    Assert.True(gimmick.Value <= Gimmicks.Cap(def.Effect), def.Id + " q" + q + " exceeds the effect cap");
                    var entry = DreamEssences.ToEntry(def, q, Normal);
                    Assert.NotNull(entry);
                    Assert.Equal(gimmick.ValuePrecise, entry.Def.ValuePrecise);
                    Assert.Equal(Math.Max(gimmick.Cooldown, Gimmicks.MinimumCooldown(def.Effect)), entry.Def.Cooldown);
                    Assert.True(Gimmicks.ValidStarId(entry.StarId));
                    Assert.Equal(DreamEssences.StarId(def, q), entry.StarId);
                    Assert.Equal(Normal, entry.Memory);
                }
        }

        [Fact]
        public void Higher_quality_is_strictly_stronger_and_never_weaker_in_reach()
        {
            foreach (var def in DreamEssences.All)
            {
                Assert.True(def.Value(1) < def.Value(2) && def.Value(2) < def.Value(3), def.Id);
                Assert.True(def.Arg(1) <= def.Arg(2) && def.Arg(2) <= def.Arg(3), def.Id);
            }
        }

        [Fact]
        public void The_strongest_timed_effects_stay_inside_their_documented_ceilings()
        {
            Assert.True(DreamEssences.Find("harbinger").Value(3) <= 100);
            Assert.True(DreamEssences.Find("banked_embers").Value(3) <= 60);
            Assert.True(DreamEssences.Find("draught").Value(3) <= 8);
            Assert.True(DreamEssences.Find("weak_point_lamp").Value(3) <= 18);
        }

        [Fact]
        public void Crescendo_five_stacks_stay_under_its_own_forty_percent_maximum()
        {
            var tide = DreamEssences.Find("rising_tide");
            Assert.True(tide.Value(3) * 5 <= 40);
        }

        [Fact]
        public void Out_of_range_quality_and_unknown_definitions_produce_nothing()
        {
            var echo = DreamEssences.Find("echoing_hollow");
            Assert.Null(DreamEssences.ToGimmick(echo, 0));
            Assert.Null(DreamEssences.ToGimmick(echo, 4));
            Assert.Null(DreamEssences.ToGimmick(null, 1));
            Assert.Null(DreamEssences.ToEntry(echo, 1, "not a memory"));
            Assert.Null(DreamEssences.ToEntry(echo, 1, null));
            Assert.False(DreamEssences.ValidQuality(0));
            Assert.False(DreamEssences.ValidQuality(4));
        }

        [Fact]
        public void Effects_the_existing_engine_forbids_on_movement_memories_are_refused_there()
        {
            foreach (var def in DreamEssences.All)
                Assert.Equal(!Gimmicks.IsV129(def.Effect), DreamEssences.CanSocket(def, Movement));
            Assert.False(DreamEssences.CanSocket(DreamEssences.Find("banked_embers"), Movement));
            Assert.True(DreamEssences.CanSocket(DreamEssences.Find("banked_embers"), Identity));
        }

        [Fact]
        public void One_essence_per_memory_and_the_first_one_wins()
        {
            var entries = DreamEssences.Resolve(new[]
            {
                Socket(Normal, "echoing_hollow", 2),
                Socket(Normal, "seed_of_shelter", 3),
            }, out var rejected);
            var entry = Assert.Single(entries);
            Assert.Equal(DreamEssences.StarId(DreamEssences.Find("echoing_hollow"), 2), entry.StarId);
            Assert.Contains(rejected, r => r.Contains("already has a socketed essence"));
        }

        [Fact]
        public void The_same_effect_in_two_sockets_keeps_only_the_stronger_one()
        {
            var entries = DreamEssences.Resolve(new[]
            {
                Socket(Normal, "echoing_hollow", 1),
                Socket(Other, "echoing_hollow", 3),
            }, out var rejected);
            var entry = Assert.Single(entries);
            Assert.Equal(Other, entry.Memory);
            Assert.Equal(26m, entry.Def.Value);
            Assert.Single(rejected);
            Assert.Contains("overridden", rejected[0]);
        }

        [Fact]
        public void Equal_strength_keeps_the_earlier_socket()
        {
            var entries = DreamEssences.Resolve(new[]
            {
                Socket(Normal, "echoing_hollow", 2),
                Socket(Other, "echoing_hollow", 2),
            }, out _);
            Assert.Equal(Normal, Assert.Single(entries).Memory);
        }

        [Fact]
        public void Different_effects_all_stay_and_keep_the_input_order()
        {
            var entries = DreamEssences.Resolve(new[]
            {
                Socket(Normal, "circling_star", 1),
                Socket(Other, "seed_of_shelter", 3),
                Socket(Identity, "draught", 2),
            }, out var rejected);
            Assert.Empty(rejected);
            Assert.Equal(new[] { Normal, Other, Identity }, entries.Select(e => e.Memory));
        }

        [Fact]
        public void Bad_sockets_are_reported_not_silently_dropped()
        {
            var entries = DreamEssences.Resolve(new[]
            {
                Socket(Normal, "nope", 1),
                Socket(Other, "echoing_hollow", 0),
                Socket(Movement, "banked_embers", 1),
                Socket("St_Z_Missing", "echoing_hollow", 1),
                Socket(Identity, "echoing_hollow", 1),
            }, out var rejected);
            Assert.Equal(Identity, Assert.Single(entries).Memory);
            Assert.Equal(4, rejected.Count);
        }

        [Fact]
        public void No_more_than_six_sockets_are_read()
        {
            var memories = new[] { Normal, Other, Identity, "St_Q_Laceration", "St_R_Tranquility", "St_R_Cataclysm", "St_Q_Reduction" };
            var ids = DreamEssences.All.Select(d => d.Id).ToArray();
            var sockets = memories.Select((m, i) => Socket(m, ids[i], 1)).ToArray();
            var entries = DreamEssences.Resolve(sockets, out var rejected);
            Assert.Equal(DreamEssences.MaxSockets, entries.Count);
            Assert.Contains(rejected, r => r.Contains("socket count"));
        }

        [Fact]
        public void Null_input_is_an_empty_loadout()
        {
            Assert.Empty(DreamEssences.Resolve(null, out var rejected));
            Assert.Empty(rejected);
        }

        [Fact]
        public void Resolved_entries_travel_through_the_existing_build_wire_format_unchanged()
        {
            var build = new Build();
            foreach (var entry in DreamEssences.Resolve(new[]
            {
                Socket(Normal, "echoing_hollow", 3),
                Socket(Other, "stepping_stones", 3),
                Socket(Identity, "harbinger", 2),
            }, out _)) build.Gimmicks.Add(entry);
            var decoded = Build.Decode(build.Encode()).Gimmicks;
            Assert.Equal(3, decoded.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(build.Gimmicks[i].StarId, decoded[i].StarId);
                Assert.Equal(build.Gimmicks[i].Memory, decoded[i].Memory);
                Assert.Equal(build.Gimmicks[i].Def.Effect, decoded[i].Def.Effect);
                Assert.Equal(build.Gimmicks[i].Def.Value, decoded[i].Def.Value);
                Assert.Equal(build.Gimmicks[i].Def.Arg, decoded[i].Def.Arg);
            }
        }

        [Fact]
        public void Resolved_entries_run_in_the_existing_gimmick_runtime_for_their_own_memory_only()
        {
            var entries = DreamEssences.Resolve(new[]
            {
                Socket(Normal, "echoing_hollow", 3),
                Socket(Other, "circling_star", 2),
            }, out _);
            var runtime = new GimmickRuntime();
            runtime.SetBuild(entries);
            var hit = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Normal, 0f, 1, 100f, false, hit, 1, 0f, true, false);
            var request = Assert.Single(hit);
            Assert.Equal(GimmickEffect.Echo, request.Entry.Def.Effect);
            var otherHit = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnHit, Other, 0f, 2, 100f, false, otherHit, 2, 0f, true, false);
            Assert.Empty(otherHit);
            var kill = new List<GimmickRequest>();
            runtime.Fire(GimmickTrigger.OnKill, Other, 1f, 2, 100f, false, kill, 3, 0f, true, false);
            Assert.Equal(GimmickEffect.Recharge, Assert.Single(kill).Entry.Def.Effect);
        }
    }
}
