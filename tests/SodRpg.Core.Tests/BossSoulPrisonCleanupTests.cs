using System;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// #99: an unconsumed Soul Prison rescue state must survive same-stage equipment and
// Build cleanup in bounded BossEnsure/cleanup work, keep exactly one barrier set for
// its remaining time, and be discarded on stage drop, manual unequip, death, or room
// change. Native consumption switches the keep rule but must not destroy the barriers.
namespace SodRpg.Core.Tests
{
    public class BossSoulPrisonCleanupTests
    {
        // BossEnsure frames per cleanup cycle are a handful; 64 nested entries means the
        // cleanup is re-entering itself, while staying far below real stack exhaustion.
        private const int MaxEnsureDepth = 64;

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            internal Profile Profile;
            internal void EquipGem(GemLocation location, Gem gem)
            {
                Hero.Skill.EquipGem(location, gem);
                Host.RefreshBossGemEquipment(Hero.Skill);
            }
            internal void Tick(float now)
            {
                Time.time = now;
                Invoke(Host, "TickBossEffects", Rt, now);
            }
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(target, args);
        }

        private static Rig Create(int pieces)
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            DewPhysics.Entities.Clear();
            EntityStatus.LiveStatusEffects.Clear();
            NativeAttributedDamagePacket.Current = null;
            BossNativeCastScope.Current = null;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "soul-prison-run";
            var profile = Profile.CreateNew(48);
            string[] slots = { "armor", "charm", "head", "weapon", "hands", "feet" };
            for (int i = 0; i < pieces; i++)
            {
                Assert.True(Content.TryGetUnique("unique.boss_seeker." + slots[i], out var unique));
                var relic = Loot.RollUnique(new Rng((ulong)(4800 + i)), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, "Hero_A", relic.Uid);
            }
            var build = Build.Compute(profile, "Hero_A", 0);
            var hero = new Hero { creationTime = 1f };
            hero.Skill.hero = hero;
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            Assert.True((bool)Invoke(host, "BossEnsure", rt));
            return new Rig { Host = host, Rt = rt, Hero = hero, Profile = profile };
        }

        private sealed class EnsureRecursionException : Exception
        {
            public EnsureRecursionException(int depth) : base($"BossEnsure was re-entered {depth} times during Soul Prison cleanup") { }
        }

        // BossEnsure reads Mirror.NetworkServer.active exactly once per entry, so armed
        // reads count nested ensure/cleanup cycles without exhausting the stack (#99).
        private static int Guarded(Action trigger)
        {
            int reads = 0;
            Mirror.NetworkServer.ActiveRead = thread =>
            {
                if (thread != Environment.CurrentManagedThreadId) return;
                if (++reads > MaxEnsureDepth) throw new EnsureRecursionException(reads);
            };
            try { trigger(); }
            finally { Mirror.NetworkServer.ActiveRead = null; }
            return reads;
        }

        // Stage-2 rescue (4 pieces): one self barrier worth 10 (discarded 40 * .25,
        // capped by h * .30 with h = 100) expiring 6s after the rescue.
        private static SeekerSoulRescueScope.Scope Rescue(Rig rig, float discarded = 40f)
        {
            rig.Hero.maxHealth = 5000f;
            rig.Hero.currentHealth = 100f;
            rig.Hero.Status.ShieldMultiplier = 10f;
            var location = new GemLocation { skill = HeroSkillLocation.Identity, index = 0 };
            var gem = new Gem_U_SoulPrison { owner = rig.Hero, location = location, creationTime = 4f };
            rig.EquipGem(location, gem);
            var status = new Se_Gem_U_SoulPrison_DeathInterrupt
            {
                gem = gem, victim = rig.Hero, parentActor = gem, creationTime = 5f,
                info = new CastInfo(rig.Hero),
            };
            var scope = rig.Host.BeginSeekerSoulRescue(status);
            Assert.NotNull(scope.Status);
            rig.Host.CompleteSeekerSoulHeal(scope, new EventInfoHeal
            {
                actor = status, target = rig.Hero, victim = rig.Hero, discardedAmount = discarded,
            });
            return scope;
        }

        [Fact]
        public void Unconsumed_rescue_survives_gem_swap_and_same_stage_build_update_without_recursing()
        {
            var rig = Create(4);
            Rescue(rig);
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);

            var other = new GemLocation { skill = HeroSkillLocation.Identity, index = 1 };
            int equipReads = Guarded(() =>
                rig.EquipGem(other, new Gem_U_SoulPrison { owner = rig.Hero, location = other, creationTime = 6f }));
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);

            // Same stage, but a fresh Build object: BossEnsure's changed path with the
            // rescue state still unconsumed in the dictionary.
            rig.Rt.Powers.SetBuild(Build.Compute(rig.Profile, "Hero_A", 0));
            int buildReads = Guarded(() => Invoke(rig.Host, "BossEnsure", rig.Rt));
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);

            Assert.InRange(equipReads, 1, 16);
            Assert.InRange(buildReads, 1, 16);

            rig.Tick(15f); // kept for its remaining time, not duplicated or refreshed
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);
            rig.Tick(16f); // expires at the original deadline
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);
        }

        [Fact]
        public void Unconsumed_rescue_barriers_are_discarded_on_stage_drop_manual_unequip_death_and_room_change()
        {
            // Stage drop: rebuild with a single seeker piece.
            var rig = Create(4);
            Rescue(rig);
            var reduced = Profile.CreateNew(48);
            Assert.True(Content.TryGetUnique("unique.boss_seeker.armor", out var unique));
            var relic = Loot.RollUnique(new Rng(4800), unique, 5);
            reduced.Stash.Add(relic);
            Rules.Equip(reduced, "Hero_A", relic.Uid);
            rig.Rt.Powers.SetBuild(Build.Compute(reduced, "Hero_A", 0));
            Guarded(() => Invoke(rig.Host, "BossEnsure", rig.Rt));
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);

            // Manual unequip: native UnequipGem removes the gem, then the unequip
            // observation runs without a native consumption scope.
            rig = Create(4);
            var scope = Rescue(rig);
            rig.Hero.Skill.gems.Remove(scope.Location);
            rig.Host.SeekerSoulUnequipped(scope.Gem, null);
            Guarded(() => rig.Host.RefreshBossGemEquipment(rig.Hero.Skill));
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);

            // Death.
            rig = Create(4);
            Rescue(rig);
            rig.Hero.currentHealth = 0f;
            Guarded(() => rig.Tick(11f));
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);

            // Room change within the same run.
            rig = Create(4);
            Rescue(rig);
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            Guarded(() => rig.Tick(11f));
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);
        }

        [Fact]
        public void Consumed_rescue_keeps_its_barriers_through_cleanup_and_expires_on_time()
        {
            var rig = Create(4);
            var scope = Rescue(rig);
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);
            // Native delayed consumption: the gem leaves its slot inside the consumption
            // callback, and the unequip observation records the consumption itself.
            rig.Hero.Skill.gems.Remove(scope.Location);
            rig.Host.SeekerSoulUnequipped(scope.Gem, scope.Status);
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);

            var other = new GemLocation { skill = HeroSkillLocation.Identity, index = 1 };
            Guarded(() => rig.EquipGem(other, new Gem_U_SoulPrison { owner = rig.Hero, location = other, creationTime = 6f }));
            rig.Rt.Powers.SetBuild(Build.Compute(rig.Profile, "Hero_A", 0));
            Guarded(() => Invoke(rig.Host, "BossEnsure", rig.Rt));
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);

            rig.Tick(15f);
            Assert.Equal(10f, rig.Hero.Status.currentShield, 3);
            rig.Tick(16f);
            Assert.Equal(0f, rig.Hero.Status.currentShield, 3);
        }
    }
}
