using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

// Native API doubles for the orphaned-protection recovery (NativeEntityProtectionRecovery.cs,
// #1012): the host safety net runs against the Dew.Core surface it calls (r.1.4.0.13:
// StatusEffect.remainingDuration timers, EntityVisual spawn protections, PropEntity.hitInvulnerableTime).
namespace UnityEngine
{
    internal sealed class GameObject
    {
        public bool activeInHierarchy = true;
        public object Component;
        public T GetComponent<T>() where T : class => Component as T;
    }
}
namespace SodRpg.Mod
{
    internal sealed class EntityVisual
    {
        public bool invulnerableWhileSpawning;
        public float spawnDuration;
        public StatusEffect[] _spawnProtections = new StatusEffect[0];
    }
    internal partial class Entity
    {
        public EntityVisual Visual;
        public readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    }
    internal partial class StatusEffect { public float? remainingDuration; }
    internal sealed partial class EntityStatus { public bool isDead; }
    internal sealed partial class HostAuthority
    {
        internal void RunNativeProtectionScan() => TickNativeEntityProtection();
    }
    // NativeActorLifetime.cs is not linked here; only its warn hook is called by the patches.
    internal static class NativeActorLifetime { internal static void WarnOnce(string kind, Exception error) { } }
}

namespace SodRpg.Core.Tests
{
    // Regression tests for the permanently invulnerable stones and enemies (#1012): a protection
    // registration whose ownership broke (dead effect, retargeted victim) or whose short-lived
    // native timer stalled is recovered by the host scan and unlinked from its actual victim when
    // the owning status is destroyed, while merchants, MockEntity, bosses, a live prop_iframe and
    // a progressing spawn protection stay untouched.
    public class NativeEntityProtectionRecoveryTests
    {
        public NativeEntityProtectionRecoveryTests()
        {
            UnityEngine.Time.time = 0f;
            Mirror.NetworkServer.active = true;
        }

        // Register a protection on the victim the way the native CreateBasicEffect path and the
        // AddBasicEffect postfix would: container on the victim, effect in both registries.
        private static Se_GenericEffectContainer Attach(Entity victim, BasicEffectMask mask, string id,
            float duration, float? remaining = null)
        {
            var container = new Se_GenericEffectContainer
            {
                victim = victim,
                effect = new InvulnerableEffect { mask = mask },
                id = id,
                duration = duration,
                parentActor = victim,
                remainingDuration = remaining
            };
            ((StatusEffect)container).victim = victim;
            container.basicEffects.Add(container.effect);
            container.effect.victim = victim;
            container.effect.parent = container;
            container.effect.isAlive = true;
            victim.Status._basicEffects.Add(container.effect);
            return container;
        }

        private static void Destroy(StatusEffect parent)
        {
            var prefix = typeof(NativeProtectionDestroy).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            prefix.Invoke(null, new object[] { parent });
        }

        [Fact]
        public void Broken_ownership_or_dead_effects_are_recovered_after_grace()
        {
            UnityEngine.Time.time = 10f;
            var stone = new PropEntity();
            var enemy = new Monster();
            var retargeted = Attach(stone, BasicEffectMask.Invulnerable, null, 60f, 60f);
            var zombie = Attach(stone, BasicEffectMask.Protected, null, 60f, 60f);
            var enemyOrphan = Attach(enemy, BasicEffectMask.Untargetable, null, 60f, 60f);
            NativeEntityProtectionRecovery.Registered(retargeted.effect);
            NativeEntityProtectionRecovery.Registered(zombie.effect);
            NativeEntityProtectionRecovery.Registered(enemyOrphan.effect);
            // Ownership breaks: one container now targets another victim, another effect already
            // died while its registration was never unregistered from the status.
            ((StatusEffect)retargeted).victim = new Entity();
            ((StatusEffect)enemyOrphan).victim = new Entity();
            zombie.effect.isAlive = false;

            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(stone, retargeted.effect, 12.9f)); // still inside grace
            Assert.True(NativeEntityProtectionRecovery.ShouldRecover(stone, retargeted.effect, 13.1f));
            Assert.True(NativeEntityProtectionRecovery.ShouldRecover(stone, zombie.effect, 13.1f));
            Assert.True(NativeEntityProtectionRecovery.ShouldRecover(enemy, enemyOrphan.effect, 13.1f));

            NativeEntityProtectionRecovery.RemoveRegistration(stone, zombie.effect);
            Assert.False(zombie.effect.isAlive);
            Assert.DoesNotContain(zombie.effect, stone.Status._basicEffects);
            Assert.DoesNotContain(zombie.effect, zombie.basicEffects);
        }

        [Fact]
        public void Stalled_prop_iframe_and_spawn_protection_are_recovered()
        {
            UnityEngine.Time.time = 10f;
            var stone = new PropEntity();
            var iframe = Attach(stone, BasicEffectMask.Invulnerable, "prop_iframe", stone.hitInvulnerableTime, 0.1f);
            var monster = new Monster
            {
                Visual = new EntityVisual { invulnerableWhileSpawning = true, spawnDuration = 1f }
            };
            var spawn = Attach(monster, BasicEffectMask.Uncollidable, null, 1f, 1f);
            monster.Visual._spawnProtections = new StatusEffect[] { spawn };
            NativeEntityProtectionRecovery.Registered(iframe.effect);
            NativeEntityProtectionRecovery.Registered(spawn.effect);

            // Both timers froze past their deadline: the stone stays hittable, the enemy spawns in.
            Assert.True(NativeEntityProtectionRecovery.ShouldRecover(stone, iframe.effect, 20f));
            Assert.True(NativeEntityProtectionRecovery.ShouldRecover(monster, spawn.effect, 20f));
        }

        [Fact]
        public void Live_timers_and_scripted_protections_are_never_cleared()
        {
            UnityEngine.Time.time = 10f;
            var stone = new PropEntity();
            var freshIframe = Attach(stone, BasicEffectMask.Invulnerable, "prop_iframe", stone.hitInvulnerableTime, 0.1f);
            var refreshedIframe = Attach(stone, BasicEffectMask.Invulnerable, "prop_iframe", stone.hitInvulnerableTime, 0.1f);
            var scripted = Attach(stone, BasicEffectMask.Invulnerable, "Se_ScriptedBossPhase", 60f, 60f);
            var longIframe = Attach(stone, BasicEffectMask.Invulnerable, "prop_iframe", 60f, 60f);
            var monster = new Monster
            {
                Visual = new EntityVisual { invulnerableWhileSpawning = true, spawnDuration = 1f }
            };
            var spawn = Attach(monster, BasicEffectMask.Uncollidable, null, 1f, 1f);
            monster.Visual._spawnProtections = new StatusEffect[] { spawn };
            NativeEntityProtectionRecovery.Registered(freshIframe.effect);
            NativeEntityProtectionRecovery.Registered(refreshedIframe.effect);
            NativeEntityProtectionRecovery.Registered(scripted.effect);
            NativeEntityProtectionRecovery.Registered(longIframe.effect);
            NativeEntityProtectionRecovery.Registered(spawn.effect);

            // A fresh hit iframe inside its window, and a spawn protection inside its duration.
            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(stone, freshIframe.effect, 10.05f));
            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(monster, spawn.effect, 11.5f));
            // A hit chain keeps refreshing the timer: the registration progresses, never stalls.
            refreshedIframe.remainingDuration = 5f;
            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(stone, refreshedIframe.effect, 40f));
            spawn.remainingDuration = 2f;
            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(monster, spawn.effect, 20f));
            // Scripted containers and non-native iframe lengths never get a deadline imposed.
            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(stone, scripted.effect, 100f));
            Assert.False(NativeEntityProtectionRecovery.ShouldRecover(stone, longIframe.effect, 100f));
        }

        [Fact]
        public void Host_scan_recovers_only_eligible_entities()
        {
            UnityEngine.Time.time = 10f;
            BasicEffect OrphanOn(Entity entity)
            {
                var container = Attach(entity, BasicEffectMask.Invulnerable, null, 60f, 60f);
                NativeEntityProtectionRecovery.Registered(container.effect);
                ((StatusEffect)container).victim = new Entity(); // ownership broke
                return container.effect;
            }
            var host = new HostAuthority();
            var stone = new PropEntity();
            var merchant = new PropEnt_Merchant_Base();
            var mock = new MockEntity();
            var boss = new Monster { isAnyBoss = true };
            var dead = new PropEntity();
            var stoneOrphan = OrphanOn(stone);
            var merchantOrphan = OrphanOn(merchant);
            var mockOrphan = OrphanOn(mock);
            var bossOrphan = OrphanOn(boss);
            var deadOrphan = OrphanOn(dead);
            dead.Status.isDead = true;
            foreach (var entity in new Entity[] { stone, merchant, mock, boss, dead }) host.RegisterEntity(entity);

            host._tickNow = 20f;
            host.RunNativeProtectionScan();

            Assert.False(stoneOrphan.isAlive);
            Assert.DoesNotContain(stoneOrphan, stone.Status._basicEffects);
            Assert.True(merchantOrphan.isAlive);
            Assert.True(mockOrphan.isAlive);
            Assert.True(bossOrphan.isAlive);
            Assert.True(deadOrphan.isAlive);
            Assert.Contains(Log.Warnings, warning => warning.Contains("Recovered native entity protection"));
        }

        [Fact]
        public void Destroyed_status_unlinks_orphans_from_their_actual_victim()
        {
            UnityEngine.Time.time = 10f;
            var actual = new Monster();
            var orphan = Attach(actual, BasicEffectMask.Invulnerable, null, 60f, 60f);
            // The container's victim reference went stale; native OnDestroyActor would skip the
            // unregister for exactly such effects (Dew.Core StatusEffect.OnDestroyActor).
            ((StatusEffect)orphan).victim = new Entity();
            var kept = new InvulnerableEffect { mask = BasicEffectMask.Invulnerable, victim = ((StatusEffect)orphan).victim, parent = orphan, isAlive = true };
            var unresolved = new InvulnerableEffect { mask = BasicEffectMask.Invulnerable, victim = null, parent = orphan, isAlive = true };
            orphan.basicEffects.Add(kept);
            orphan.basicEffects.Add(unresolved);

            Destroy(orphan);

            Assert.False(orphan.effect.isAlive);
            Assert.DoesNotContain(orphan.effect, actual.Status._basicEffects);
            Assert.DoesNotContain(orphan.effect, orphan.basicEffects);
            Assert.True(kept.isAlive);
            Assert.Contains(kept, orphan.basicEffects);
            Assert.Contains(unresolved, orphan.basicEffects); // a null victim cannot be unlinked
        }
    }
}
