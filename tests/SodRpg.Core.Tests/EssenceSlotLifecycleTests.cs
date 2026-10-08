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
    internal partial class Gem : Actor { }
    internal partial class Hero : Entity { public HeroSkill Skill = new HeroSkill(); }
    internal sealed partial class HeroSkill
    {
        public readonly Dictionary<GemLocation, Gem> gems = new Dictionary<GemLocation, Gem>();
        public readonly List<Gem> Dropped = new List<Gem>();
        public readonly List<int> DropIndices = new List<int>();
        public readonly int[] Caps = { 2, 2 };
        private readonly int[] MemoryCaps = { 2, 2, 2, 2 };
        public int Writes;
        public HeroSkillLocation? FailSetBefore, FailSetAfter;
        public bool FailDrop;
        public int GetMaxGemCountCalls;
        public int GetMaxGemCount(HeroSkillLocation location)
        {
            GetMaxGemCountCalls++;
            return (int)location < Caps.Length ? Caps[(int)location] : MemoryCaps[(int)location - Caps.Length];
        }
        public void SetMaxGemCount(HeroSkillLocation location, int count)
        {
            if (FailSetBefore == location) { FailSetBefore = null; throw new InvalidOperationException("before assignment"); }
            Writes++;
            if ((int)location < Caps.Length) Caps[(int)location] = count;
            else MemoryCaps[(int)location - Caps.Length] = count;
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
    internal sealed class Se_Shrine_Chaos_StatBonus : StatusEffect
    {
        public int currentAddedGemSlotIdentity;
    }
    internal sealed partial class ClientSession
    {
        internal static bool NativeContinueRestoring;
    }
    internal static partial class Log { public static void Info(string text) { } public static void Error(string text) { } }
    internal sealed partial class HostAuthority
    {
        internal static readonly GemSlotContinueSources GemContinueSources = new GemSlotContinueSources();
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
        internal void ClearGemSlotConflict(Hero hero) { }
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

        private static Build MoveOnly()
        {
            var build = new Build(); build.Stats[Stat.EssenceSlotMovement] = 1; return build;
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
        public void Inactive_cleanup_retains_ownership_for_reactivation_without_recapturing_our_bonus()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            hero.isActive = false;
            host.RestoreForTest(rt); host.DetachGemSlotsForTest();
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            Assert.Equal(2, hero.Skill.Writes);
            hero.isActive = true;
            var nextHost = new HostAuthority(); var nextRuntime = Runtime(hero);
            nextHost.ApplyForTest(nextRuntime, Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            nextHost.RestoreForTest(nextRuntime);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Periodic_absolute_rewrites_keep_both_bonuses_and_cleanup_preserves_reset_native_caps()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            for (int i = 1; i <= 20; i++)
            {
                UnityEngine.Time.unscaledTime = i;
                hero.Skill.Caps[0] = 2;
                host.TickGemSlotsForTest();
                Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            }
            hero.Skill.Caps[0] = 2;
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Repeated_external_additions_preserve_foreign_slots_and_respect_the_native_ceiling()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            for (int i = 1; i <= 10; i++)
            {
                hero.Skill.Caps[0]++;
                host.ApplyForTest(rt, Both());
                int external = 2 + i;
                Assert.Equal(new[] { external == 4 ? 4 : external + 1, 3 }, hero.Skill.Caps);
            }
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 12, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Native_four_slot_locations_stay_unchanged_through_ticks_and_cleanup()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = hero.Skill.Caps[1] = 4;
            for (int tick = 0; tick < 20; tick++)
            {
                host.ApplyForTest(rt, Both());
                UnityEngine.Time.unscaledTime = tick + 1;
                host.TickGemSlotsForTest();
                Assert.Equal(new[] { 4, 4 }, hero.Skill.Caps);
                Assert.Equal(0, hero.Skill.Writes);
            }
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 4, 4 }, hero.Skill.Caps);
            Assert.Equal(0, hero.Skill.Writes);
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

        [Fact]
        public void Loaded_legacy_overflow_is_dropped_without_a_native_cap_shrink()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = hero.Skill.Caps[1] = 0;
            // Native resume restores each saved location, but does not persist MOD slot caps.
            var valid = new Gem(); var excess = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = valid;
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 7 }] = excess;
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { 1, 1 }, hero.Skill.Caps);
            Assert.Same(excess, Assert.Single(hero.Skill.Dropped));
            Assert.True(hero.Skill.gems.ContainsValue(valid));
            host.ApplyForTest(rt, Both());
            Assert.Single(hero.Skill.Dropped);
        }

        [Fact]
        public void Overflow_loaded_after_first_application_is_repaired_and_external_slots_are_preserved()
        {
            var hero = new Hero(); var host = new HostAuthority();
            hero.Skill.Caps[0] = 8;
            host.ApplyForTest(Runtime(hero), Both());
            var external = new Gem(); var overflow = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 8 }] = external;
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 9 }] = overflow;
            UnityEngine.Time.unscaledTime = 1;
            host.TickGemSlotsForTest();
            Assert.Equal(9, hero.Skill.Caps[0]);
            Assert.Same(overflow, Assert.Single(hero.Skill.Dropped));
            Assert.True(hero.Skill.gems.ContainsValue(external));
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
        public void Saved_native_counter_survives_refund_without_a_rewrite_notification()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = 0;
            host.ApplyForTest(rt, Both());
            var native = new Se_Shrine_Chaos_StatBonus { victim = hero, isActive = true, currentAddedGemSlotIdentity = 1 };
            EntityStatus.LiveStatusEffects.Add(native);
            try
            {
                // The optional observer did not run after the native absolute assignment.
                hero.Skill.Caps[0] = native.currentAddedGemSlotIdentity;
                var gem = new Gem();
                hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = gem;
                host.ApplyForTest(rt, new Build());
                host.RestoreForTest(rt);
                Assert.Equal(1, hero.Skill.Caps[0]);
                Assert.True(hero.Skill.gems.ContainsValue(gem));
                Assert.Empty(hero.Skill.Dropped);
            }
            finally { EntityStatus.LiveStatusEffects.Remove(native); }
        }

        [Fact]
        public void Native_restore_keeps_saved_gems_until_the_matching_profile_build_is_available()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = hero.Skill.Caps[1] = 0;
            var gem = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = gem;
            ClientSession.NativeContinueRestoring = true;
            try
            {
                host.ApplyForTest(rt, new Build());
                UnityEngine.Time.unscaledTime = 1;
                host.TickGemSlotsForTest();
                host.RestoreForTest(rt);
                Assert.True(hero.Skill.gems.ContainsValue(gem));
                Assert.Empty(hero.Skill.Dropped);
            }
            finally { ClientSession.NativeContinueRestoring = false; }
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { 1, 1 }, hero.Skill.Caps);
            Assert.True(hero.Skill.gems.ContainsValue(gem));
            Assert.Empty(hero.Skill.Dropped);
        }

        [Fact]
        public void Unconfirmed_guest_source_preserves_saved_gems_until_validated_reconciliation()
        {
            var hero = new Hero(); var peer = new DewPlayer { guid = "slot-resume-guest", hero = hero };
            hero.owner = peer;
            var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = 1; hero.Skill.Caps[1] = 0;
            var native = new Se_Shrine_Chaos_StatBonus { victim = hero, isActive = true, currentAddedGemSlotIdentity = 1 };
            var nativeGem = new Gem(); var identityGem = new Gem(); var movementGem = new Gem(); var legacyGem = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = nativeGem;
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 1 }] = identityGem;
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Movement, index = 0 }] = movementGem;
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 9 }] = legacyGem;
            string previousRun = ClientSession.ContinueRunId;
            EntityStatus.LiveStatusEffects.Add(native);
            DewPlayer.gamePlayers.Add(peer);
            HostAuthority.GemContinueSources.Begin("slot-run", "slot-checkpoint", "slot-resume");
            HostAuthority.GemContinueSources.Include(peer.guid);
            ClientSession.ContinueRunId = "slot-run";
            try
            {
                host.RegisterNegotiation();
                host.ApplyForTest(rt, new Build());
                Assert.Equal(new[] { 2, 1 }, hero.Skill.Caps);
                Assert.True(hero.Skill.gems.ContainsValue(nativeGem));
                Assert.True(hero.Skill.gems.ContainsValue(identityGem));
                Assert.True(hero.Skill.gems.ContainsValue(movementGem));
                Assert.Same(legacyGem, Assert.Single(hero.Skill.Dropped));
                var receipt = new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version + 1, modVer = "different", content = "different",
                    continueRunId = "slot-run", continueCheckpointId = "slot-checkpoint", continueResumeSession = "wrong-resume",
                };
                host.ReceiveNegotiation(receipt, peer);
                host.RestoreForTest(rt);
                Assert.Equal(new[] { 2, 1 }, hero.Skill.Caps);
                receipt.continueResumeSession = "slot-resume";
                host.ReceiveNegotiation(receipt, peer);
                host.ApplyForTest(rt, new Build());
                Assert.Equal(new[] { 2, 1 }, hero.Skill.Caps); // Receipt alone must not apply the old zero-slot input.
                Assert.True(HostAuthority.GemContinueSources.QueueFreshBuild(peer.guid, peer, "slot-run"));
                Assert.False(HostBuildValidation.TryAccept("invalid", "Hero_Cetus", out _, out _));
                host.ApplyForTest(rt, new Build());
                Assert.True(hero.Skill.gems.ContainsValue(identityGem));
                Assert.True(hero.Skill.gems.ContainsValue(movementGem));
                var profile = Profile.CreateNew(248);
                var computed = Build.Compute(profile, "Hero_Cetus", 0);
                string encoded = HostBuildValidation.Encode(computed, profile, "Hero_Cetus", 0);
                Assert.True(HostBuildValidation.TryAccept(encoded, "Hero_Cetus", out var accepted, out var reason), reason);
                HostAuthority.GemContinueSources.CommitFreshBuild(peer.guid, peer, "slot-run");
                host.ApplyForTest(rt, accepted);
                host.ApplyForTest(rt, accepted);
                host.RestoreForTest(rt);
                Assert.Equal(new[] { 1, 0 }, hero.Skill.Caps);
                Assert.Same(nativeGem, Assert.Single(hero.Skill.gems).Value);
                Assert.Equal(new[] { legacyGem, identityGem, movementGem }, hero.Skill.Dropped);
            }
            finally
            {
                host.DetachNegotiation();
                HostAuthority.GemContinueSources.Reset();
                ClientSession.ContinueRunId = previousRun;
                DewPlayer.gamePlayers.Remove(peer);
                EntityStatus.LiveStatusEffects.Remove(native);
            }
        }

        [Fact]
        public void Pending_continue_keeps_other_mod_baseline_without_readding_or_shrinking()
        {
            // Another mod raised both caps before the continue source is confirmed.
            var hero = new Hero(); var peer = new DewPlayer { guid = "dew-resume-guest", hero = hero };
            hero.owner = peer;
            var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = 3; hero.Skill.Caps[1] = 2;
            var native = new Se_Shrine_Chaos_StatBonus { victim = hero, isActive = true, currentAddedGemSlotIdentity = 1 };
            var nativeGem = new Gem(); var housedGem = new Gem(); var moveGem = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = nativeGem;
            // A gem from a former MOD slot now sits inside the other mod's raised baseline.
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 2 }] = housedGem;
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Movement, index = 0 }] = moveGem;
            string previousRun = ClientSession.ContinueRunId;
            EntityStatus.LiveStatusEffects.Add(native);
            DewPlayer.gamePlayers.Add(peer);
            HostAuthority.GemContinueSources.Begin("dew-run", "dew-checkpoint", "dew-resume");
            HostAuthority.GemContinueSources.Include(peer.guid);
            ClientSession.ContinueRunId = "dew-run";
            try
            {
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    host.ApplyForTest(rt, new Build());
                    Assert.Equal(new[] { 3, 2 }, hero.Skill.Caps);
                    Assert.Equal(0, hero.Skill.Writes);
                    Assert.True(hero.Skill.gems.ContainsValue(nativeGem));
                    Assert.True(hero.Skill.gems.ContainsValue(housedGem));
                    Assert.True(hero.Skill.gems.ContainsValue(moveGem));
                    Assert.Empty(hero.Skill.Dropped);
                }
            }
            finally
            {
                HostAuthority.GemContinueSources.Reset();
                ClientSession.ContinueRunId = previousRun;
                DewPlayer.gamePlayers.Remove(peer);
                EntityStatus.LiveStatusEffects.Remove(native);
            }
        }

        [Fact]
        public void Foreign_writeback_after_our_bonus_is_resupplied_idempotently()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = 3; hero.Skill.Caps[1] = 2;
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { 4, 3 }, hero.Skill.Caps);
            var ourGem = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 3 }] = ourGem;
            for (int i = 1; i <= 3; i++)
            {
                // The other mod rewrites its own absolute config, dropping our bonus with it.
                hero.Skill.Caps[0] = 3; hero.Skill.Caps[1] = 2;
                UnityEngine.Time.unscaledTime = i;
                host.TickGemSlotsForTest();
                Assert.Equal(new[] { 4, 3 }, hero.Skill.Caps);
                Assert.True(hero.Skill.gems.ContainsValue(ourGem));
                Assert.Empty(hero.Skill.Dropped);
            }
        }

        [Fact]
        public void Pending_source_then_validated_build_respec_and_detach_preserve_other_mod_slots()
        {
            var hero = new Hero(); var peer = new DewPlayer { guid = "dew-cycle-guest", hero = hero };
            hero.owner = peer;
            var host = new HostAuthority(); var rt = Runtime(hero);
            hero.Skill.Caps[0] = 3; hero.Skill.Caps[1] = 2;
            var native = new Se_Shrine_Chaos_StatBonus { victim = hero, isActive = true, currentAddedGemSlotIdentity = 1 };
            var kept = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = kept;
            string previousRun = ClientSession.ContinueRunId;
            EntityStatus.LiveStatusEffects.Add(native);
            DewPlayer.gamePlayers.Add(peer);
            HostAuthority.GemContinueSources.Begin("dew-cycle", "dew-cp", "dew-rs");
            HostAuthority.GemContinueSources.Include(peer.guid);
            ClientSession.ContinueRunId = "dew-cycle";
            try
            {
                host.RegisterNegotiation();
                host.ApplyForTest(rt, new Build());
                Assert.Equal(new[] { 3, 2 }, hero.Skill.Caps);
                var receipt = new DreamforgeHelloMsg
                {
                    protocol = Protocol.Version + 1, modVer = "different", content = "different",
                    continueRunId = "dew-cycle", continueCheckpointId = "dew-cp", continueResumeSession = "dew-rs",
                };
                host.ReceiveNegotiation(receipt, peer);
                Assert.True(HostAuthority.GemContinueSources.QueueFreshBuild(peer.guid, peer, "dew-cycle"));
                var profile = Profile.CreateNew(248);
                var computed = Build.Compute(profile, "Hero_Cetus", 0);
                string encoded = HostBuildValidation.Encode(computed, profile, "Hero_Cetus", 0);
                Assert.True(HostBuildValidation.TryAccept(encoded, "Hero_Cetus", out var accepted, out var reason), reason);
                HostAuthority.GemContinueSources.CommitFreshBuild(peer.guid, peer, "dew-cycle");
                host.ApplyForTest(rt, Both());
                Assert.Equal(new[] { 4, 3 }, hero.Skill.Caps);
                var ourGem = new Gem();
                hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 3 }] = ourGem;
                host.ApplyForTest(rt, new Build());
                Assert.Equal(new[] { 3, 2 }, hero.Skill.Caps);
                Assert.Same(ourGem, Assert.Single(hero.Skill.Dropped));
                Assert.True(hero.Skill.gems.ContainsValue(kept));
                host.DetachGemSlotsForTest();
                Assert.Equal(new[] { 3, 2 }, hero.Skill.Caps);
                Assert.Single(hero.Skill.Dropped);
            }
            finally
            {
                host.DetachNegotiation();
                HostAuthority.GemContinueSources.Reset();
                ClientSession.ContinueRunId = previousRun;
                DewPlayer.gamePlayers.Remove(peer);
                EntityStatus.LiveStatusEffects.Remove(native);
            }
        }

        [Fact]
        public void Reconnected_saved_guid_cannot_reuse_the_previous_peer_source_receipt()
        {
            var first = new DewPlayer { guid = "slot-rejoin-guest" };
            var next = new DewPlayer { guid = first.guid };
            var hero = new Hero { owner = next }; next.hero = hero;
            hero.Skill.Caps[0] = hero.Skill.Caps[1] = 0;
            var gem = new Gem();
            hero.Skill.gems[new GemLocation { skill = HeroSkillLocation.Identity, index = 0 }] = gem;
            var host = new HostAuthority(); var rt = Runtime(hero);
            string previousRun = ClientSession.ContinueRunId;
            ClientSession.ContinueRunId = "slot-rejoin-run";
            var sources = HostAuthority.GemContinueSources;
            sources.Begin("slot-rejoin-run", "checkpoint", "resume");
            sources.Include(first.guid);
            try
            {
                sources.ObserveReceipt(first.guid, first, "slot-rejoin-run", "slot-rejoin-run", "checkpoint", "resume");
                Assert.True(sources.QueueFreshBuild(first.guid, first, "slot-rejoin-run"));
                sources.CommitFreshBuild(first.guid, first, "slot-rejoin-run");
                // Native reconnect creates a new peer/skill but retains the saved GUID.
                host.ApplyForTest(rt, new Build());
                Assert.Equal(new[] { 1, 0 }, hero.Skill.Caps);
                Assert.True(hero.Skill.gems.ContainsValue(gem));
                Assert.Empty(hero.Skill.Dropped);
                sources.CommitFreshBuild(next.guid, next, "slot-rejoin-run");
                host.ApplyForTest(rt, new Build());
                Assert.True(hero.Skill.gems.ContainsValue(gem));
                sources.ObserveReceipt(next.guid, next, "stale-run", "slot-rejoin-run", "checkpoint", "resume");
                Assert.False(sources.QueueFreshBuild(next.guid, next, "slot-rejoin-run"));
                sources.ObserveReceipt(next.guid, next, "slot-rejoin-run", "slot-rejoin-run", "checkpoint", "resume");
                Assert.True(sources.QueueFreshBuild(next.guid, next, "slot-rejoin-run"));
                sources.CommitFreshBuild(next.guid, next, "slot-rejoin-run");
                host.ApplyForTest(rt, new Build());
                host.RestoreForTest(rt);
                Assert.Equal(new[] { 0, 0 }, hero.Skill.Caps);
                Assert.Same(gem, Assert.Single(hero.Skill.Dropped));
                Assert.Empty(hero.Skill.gems);
            }
            finally
            {
                sources.Reset();
                ClientSession.ContinueRunId = previousRun;
            }
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

        // ---- 協力プレイの順序と再読み込み（報告：星図で回避のスロットを増やす星が反映されないことがある）----

        private static void SimulateModReload()
        {
            // ゲームの再読み込みは同じ MOD を別のアセンブリコピーとして読み込む。コピーごとの
            // 作業台帳は空で始まる（ここでは同等の状態を作る）。
            var field = typeof(HostAuthority).GetField("GemSlotLedgers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var table = field.GetValue(null);
            table.GetType().GetMethod("Clear").Invoke(table, null);
        }

        [Fact]
        public void Reload_leftover_slots_converge_to_the_designed_value()
        {
            // 再読み込み時に書き戻せなかった自分の枠（ゾーン移動中など本体が非アクティブ）は
            // 次のコピーの台帳で自分の持ち分として引き継がれ、再度上乗せされない。
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            SimulateModReload();
            var reloaded = new HostAuthority(); var rt2 = Runtime(hero);
            reloaded.ApplyForTest(rt2, Both());
            UnityEngine.Time.unscaledTime = 1;
            reloaded.TickGemSlotsForTest();
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            // 解除は自分の +1 だけを外す。残りは他MOD・本体分として残る。
            reloaded.RestoreForTest(rt2);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Reload_leftover_then_respec_converges()
        {
            var hero = new Hero(); var host = new HostAuthority();
            host.ApplyForTest(Runtime(hero), Both());
            SimulateModReload();
            var reloaded = new HostAuthority(); var rt = Runtime(hero);
            reloaded.ApplyForTest(rt, Both());
            // 振り直し：アイデンティティの星を返却し、回避の星だけ残す。
            reloaded.ApplyForTest(rt, MoveOnly());
            Assert.Equal(new[] { 2, 3 }, hero.Skill.Caps);
            reloaded.RestoreForTest(rt);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
        }

        [Fact]
        public void Reload_after_clean_detach_reapplies_normally()
        {
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 2, 2 }, hero.Skill.Caps);
            SimulateModReload();
            var reloaded = new HostAuthority();
            reloaded.ApplyForTest(Runtime(hero), Both());
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
        }

        [Fact]
        public void Build_before_the_skill_component_heals_on_the_next_tick()
        {
            // 参加者の参加直後：Build が先に届き、HeroSkill はまだ無い。1秒ごとの確認で付与される。
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            var skill = hero.Skill;
            hero.Skill = null;
            host.ApplyForTest(rt, Both());
            Assert.Equal(new[] { 2, 2 }, skill.Caps);
            hero.Skill = skill;
            UnityEngine.Time.unscaledTime = 1;
            host.TickGemSlotsForTest();
            Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
        }

        [Fact]
        public void Skill_recreated_with_foreign_carried_caps_preserves_them()
        {
            // 本体交換で運ばれてきた枠が他MOD由来なら、それは基地として保持し、自分の分だけを足す。
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            var carried = new HeroSkill { hero = hero };
            carried.Caps[0] = hero.Skill.Caps[0];
            carried.Caps[1] = hero.Skill.Caps[1];
            hero.Skill = carried;
            UnityEngine.Time.unscaledTime = 1;
            host.TickGemSlotsForTest();
            Assert.Equal(new[] { 4, 4 }, carried.Caps);
            host.RestoreForTest(rt);
            Assert.Equal(new[] { 3, 3 }, carried.Caps);
        }

        [Fact]
        public void Client_view_returns_to_the_design_after_a_native_reset()
        {
            // 参加者の画面はホストが書いた SyncVar を描く。本体・他MODが値を戻しても
            // 確認のたびに設計値へ戻る（＝参加者側でも枠が消えたままにならない）。
            var hero = new Hero(); var host = new HostAuthority(); var rt = Runtime(hero);
            host.ApplyForTest(rt, Both());
            for (int i = 1; i <= 3; i++)
            {
                hero.Skill.Caps[0] = 2; hero.Skill.Caps[1] = 2;
                UnityEngine.Time.unscaledTime = i;
                host.TickGemSlotsForTest();
                Assert.Equal(new[] { 3, 3 }, hero.Skill.Caps);
            }
        }
    }
}
