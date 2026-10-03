using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

// Compile the production HostAuthority.GemSlots.cs adapter itself against a narrow native API double.
namespace UnityEngine { public partial struct Vector3 { } }
namespace SodRpg.Mod
{
    internal enum HeroSkillLocation { Identity, Movement, Q, W, E, R }
    internal struct GemLocation { public HeroSkillLocation skill; public int index; }
    internal sealed class Gem : Actor { }
    internal sealed partial class Hero : Entity { public HeroSkill Skill = new HeroSkill(); }
    internal sealed partial class HeroSkill
    {
        public readonly Dictionary<GemLocation, Gem> gems = new Dictionary<GemLocation, Gem>();
        public readonly List<Gem> Dropped = new List<Gem>();
        public readonly List<int> DropIndices = new List<int>();
        public readonly int[] Caps = { 2, 2 };
        public int Writes;
        public HeroSkillLocation? FailSetBefore, FailSetAfter;
        public bool FailDrop;
        public int GetMaxGemCount(HeroSkillLocation location) => Caps[(int)location];
        public void SetMaxGemCount(HeroSkillLocation location, int count)
        {
            if (FailSetBefore == location) { FailSetBefore = null; throw new InvalidOperationException("before assignment"); }
            Writes++;
            Caps[(int)location] = count;
            if (FailSetAfter == location) { FailSetAfter = null; throw new InvalidOperationException("after assignment"); }
        }
        public Gem UnequipGem(GemLocation location, UnityEngine.Vector3 position)
        {
            if (FailDrop) { FailDrop = false; throw new InvalidOperationException("before dropping"); }
            var gem = gems[location]; gems.Remove(location); Dropped.Add(gem); DropIndices.Add(location.index); return gem;
        }
        public void Fill(HeroSkillLocation location)
        {
            for (int i = 0; i < GetMaxGemCount(location); i++) gems[new GemLocation { skill = location, index = i }] = new Gem();
        }
    }
    internal static class Log { public static void Info(string text) { } public static void Error(string text) { } public static void Warn(string text) { } }
    internal sealed partial class HostAuthority
    {
        internal sealed partial class HeroRuntime
        {
            public Hero Hero;
            public string HeroKey = "Hero_Cetus";
            public HeroSkill GemSlotOwner;
            public GemBuildForTest AppliedBuild;
        }
        internal sealed class GemBuildForTest { internal Build Build; }
        internal void ApplyForTest(HeroRuntime runtime, Build build)
        {
            runtime.AppliedBuild = new GemBuildForTest { Build = build };
            Track(runtime);
            ApplyGemSlots(runtime, build);
        }
        internal void TickGemSlotsForTest() => TickGemSlots();
        internal void DetachGemSlotsForTest() => DetachGemSlots();
        internal void SendGemSlotConflict(Hero hero, bool disabled) { }
        internal void RestoreForTest(HeroRuntime runtime) => RestoreGemSlots(runtime);
    }
}
namespace SodRpg.Core.Tests
{
    public class EssenceSlotLifecycleTests
    {
        public EssenceSlotLifecycleTests() { UnityEngine.Time.unscaledTime = 0; }
        private static Build Both()
        {
            var build = new Build(); build.Stats[Stat.EssenceSlotIdentity] = 1; build.Stats[Stat.EssenceSlotMovement] = 1; return build;
        }
        private static HostAuthority.HeroRuntime Runtime(Hero hero) => new HostAuthority.HeroRuntime { Hero = hero };

        [Fact]
        public void Repeated_reload_preserves_other_bonuses_without_accumulating_our_two_slots()
        {
            var hero = new Hero(); hero.Skill.Caps[0]++; hero.Skill.Caps[1]++;
            for (int i = 0; i < 5; i++)
            {
                var host = new HostAuthority(); var rt = Runtime(hero);
                host.ApplyForTest(rt, Both()); host.ApplyForTest(rt, Both());
                Assert.Equal(new[] { 4, 4 }, hero.Skill.Caps);
                host.RestoreForTest(rt); host.RestoreForTest(rt);
                Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            }
        }

        [Fact]
        public void Failed_cleanup_then_runtime_recreation_does_not_add_the_same_slot_again()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            hero.Skill.FailSetBefore = HeroSkillLocation.Identity;
            host.RestoreForTest(rt);
            host.ApplyForTest(Runtime(hero), Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
        }

        [Fact]
        public void Runtime_recreation_without_cleanup_keeps_one_contribution_and_skips_unchanged_writes()
        {
            var hero = new Hero(); var host = new HostAuthority();
            for (int i = 0; i < 20; i++) host.ApplyForTest(Runtime(hero), Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            Assert.Equal(2, hero.Skill.Writes);
        }

        [Fact]
        public void Failed_cleanup_then_authority_recreation_keeps_the_component_ledger()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            hero.Skill.FailSetBefore = HeroSkillLocation.Identity;
            host.RestoreForTest(rt);
            var nextHost = new HostAuthority();
            nextHost.ApplyForTest(Runtime(hero), Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            nextHost.DetachGemSlotsForTest();
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Inactive_cleanup_drops_ownership_without_writing_or_recapturing_the_same_component()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            hero.isActive = false;
            host.RestoreForTest(rt); host.DetachGemSlotsForTest();
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            Assert.Equal(2, hero.Skill.Writes);
            hero.isActive = true;
            new HostAuthority().ApplyForTest(Runtime(hero), Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            Assert.Equal(2, hero.Skill.Writes);
        }

        [Fact]
        public void Periodic_absolute_rewrites_disable_both_locations_without_more_writes_or_growth()
        {
            var hero = new Hero(); var host = new HostAuthority();
            host.ApplyForTest(Runtime(hero), Both());
            for (int i = 1; i <= 3; i++)
            {
                UnityEngine.Time.unscaledTime = i;
                hero.Skill.Caps[0] = 2;
                host.TickGemSlotsForTest();
            }
            Assert.True(host.IsGemSlotConflict(hero));
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
            int writes = hero.Skill.Writes;
            hero.Skill.Caps[0] = 7;
            UnityEngine.Time.unscaledTime = 4;
            host.TickGemSlotsForTest();
            host.ApplyForTest(Runtime(hero), Both());
            host.DetachGemSlotsForTest();
            Assert.Equal(new[] { 7, 2 }, hero.Skill.Caps);
            Assert.Equal(writes, hero.Skill.Writes);
        }

        [Fact]
        public void Delta_preserving_external_writer_latches_and_cleanup_subtracts_only_our_current_part()
        {
            var hero = new Hero(); var host = new HostAuthority();
            host.ApplyForTest(Runtime(hero), Both());
            for (int i = 1; i <= 3; i++)
            {
                UnityEngine.Time.unscaledTime = i;
                hero.Skill.Caps[0]++;
                host.ApplyForTest(Runtime(hero), Both());
            }
            Assert.True(host.IsGemSlotConflict(hero));
            Assert.Equal(new[] { 5, 2 }, hero.Skill.Caps);
            int writes = hero.Skill.Writes;
            host.ApplyForTest(Runtime(hero), Both());
            Assert.Equal(new[] { 5, 2 }, hero.Skill.Caps);
            Assert.Equal(writes, hero.Skill.Writes);
        }

        [Fact]
        public void Full_slots_drop_original_gem_objects_and_preserve_later_external_slots()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both()); hero.Skill.Caps[0]++; hero.Skill.Caps[1]++;
            hero.Skill.Fill(HeroSkillLocation.Identity); hero.Skill.Fill(HeroSkillLocation.Movement);
            var all = hero.Skill.gems.Values.ToArray();
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            Assert.Equal(2, hero.Skill.Dropped.Count);
            Assert.Equal(new[] { 3, 3 }, hero.Skill.DropIndices);
            Assert.Equal(all.Length, hero.Skill.gems.Count + hero.Skill.Dropped.Count);
            Assert.All(all, gem => Assert.True(hero.Skill.gems.ContainsValue(gem) || hero.Skill.Dropped.Contains(gem)));
            host.RestoreForTest(rt); Assert.Equal(2, hero.Skill.Dropped.Count);
        }

        [Fact]
        public void Sparse_high_index_is_dropped_even_when_total_gem_count_fits()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            var gem = new Gem(); hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 2 }] = gem;
            host.RestoreForTest(rt);
            Assert.Same(gem, Assert.Single(hero.Skill.Dropped));
            Assert.Empty(hero.Skill.gems); Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Partial_application_records_native_assignment_and_restores_only_our_mutation(bool after)
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            if (after) hero.Skill.FailSetAfter = HeroSkillLocation.Identity;
            else hero.Skill.FailSetBefore = HeroSkillLocation.Identity;
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { after ? 3 : 2, 3 }, hero.Skill.Caps);
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
            host.RestoreForTest(rt); Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Drop_failure_keeps_updated_ledger_and_does_not_prevent_other_location_cleanup()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both()); hero.Skill.Fill(HeroSkillLocation.Identity); hero.Skill.Fill(HeroSkillLocation.Movement);
            hero.Skill.FailDrop = true;
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
            Assert.Single(hero.Skill.Dropped);
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps); Assert.Equal(2, hero.Skill.Dropped.Count);
        }

        [Fact]
        public void Respec_then_detach_is_idempotent_and_replacement_components_have_separate_ledgers()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both()); host.ApplyForTest(rt, new Build());
            host.RestoreForTest(rt); Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
            host.ApplyForTest(rt, Both());
            var previous = hero.Skill; hero.Skill = new HeroSkill { hero = hero };
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { 2, 2 }, previous.Caps); Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            host.RestoreForTest(rt); Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Cleanup_before_first_build_does_not_change_native_slots()
        {
            var hero = new Hero(); var host = new HostAuthority();
            host.RestoreForTest(Runtime(hero)); Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }
    }
}
