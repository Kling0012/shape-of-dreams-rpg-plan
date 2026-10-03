using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

// Exercise the production native adapter, using only the API surface it calls.
namespace HarmonyLib
{
    internal sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
namespace Mirror { internal static class NetworkServer { public static bool active = true; } }
namespace UnityEngine { internal static class Time { public static float time; } }
namespace SodRpg.Mod
{
    internal enum EntityRelation { Ally, Enemy }
    internal partial class Entity : Actor
    {
        public bool isActive = true;
        public float maxHealth = 1000;
        public EntityRelation Relation = EntityRelation.Ally;
        public EntityStatus Status = new EntityStatus();
        public EntityRelation GetRelation(Entity other) => Relation;
        public Se_GenericEffectContainer CreateBasicEffect(Entity victim, BasicEffect effect, float duration, string id)
            => new Se_GenericEffectContainer { victim = victim, duration = duration, id = id, effect = effect };
    }
    internal sealed partial class EntityStatus { public bool hasCrowdControlImmunity; }
    internal class BasicEffect { public Entity victim; public StatusEffect parent; public bool isAlive = true; }
    internal sealed class StunEffect : BasicEffect { }
    internal sealed class SlowEffect : BasicEffect { }
    internal sealed class UnstoppableEffect : BasicEffect { }
    internal sealed class CastInfo { public Entity caster; }
    internal class StatusEffect : Actor { public CastInfo info = new CastInfo(); }
    internal sealed class Se_GenericEffectContainer : StatusEffect
    {
        public Entity victim;
        public BasicEffect effect;
        public string id;
        public float duration;
        public bool isActive = true;
        public void Destroy() => isActive = false;
    }
    internal sealed partial class HostAuthority
    {
        internal static HostAuthority NativeInstance;
        private ActorManager _am = new ActorManager();
        private readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        internal sealed class NewPowerHostState { public Se_GenericEffectContainer UnbowedGuard; }
        internal sealed partial class HeroRuntime
        {
            public NewPowerHostState NewPowers = new NewPowerHostState();
            public PowerRuntime Powers;
        }
        public int Shields;
        public float ShieldAmount, ShieldDuration;
        private static bool Alive(Hero hero) => hero != null && hero.isActive;
        private void LogPowerTrigger(Power power) { }
        private void PowerShield(HeroRuntime rt, Entity target, Power power, float amount, float duration)
        { Shields++; ShieldAmount = amount; ShieldDuration = duration; }
        internal void Track(HeroRuntime rt) => _runtimes[rt.Hero] = rt;
        internal void ClearGuard(HeroRuntime rt) => ClearUnbowedGuard(rt);
    }
}
namespace SodRpg.Core.Tests
{
    public class UnbowedNativeTests
    {
        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime) Setup()
        {
            var build = new Build(); build.Powers[Power.UnbowedMind] = 8;
            var rt = new HostAuthority.HeroRuntime { Hero = new Hero(), Powers = new PowerRuntime(build, 0) };
            var host = new HostAuthority(); host.Track(rt); HostAuthority.NativeInstance = host;
            UnityEngine.Time.time = 0; Mirror.NetworkServer.active = true;
            return (host, rt);
        }
        private static BasicEffect Stun(HostAuthority.HeroRuntime rt, Entity source) => new StunEffect
        { victim = rt.Hero, parent = new StatusEffect { info = new CastInfo { caster = source } } };
        private static void Apply(BasicEffect effect)
        {
            var prefix = typeof(NativeUnbowedMind).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            var postfix = typeof(NativeUnbowedMind).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
            var args = new object[] { effect, false }; prefix.Invoke(null, args);
            postfix.Invoke(null, args);
        }
        [Fact]
        public void Enemy_stun_grants_two_independent_equal_duration_effects_once_per_cooldown()
        {
            var (host, rt) = Setup();
            var stun = Stun(rt, new Entity { Relation = EntityRelation.Enemy });
            Apply(stun);
            Assert.Equal(80, host.ShieldAmount); Assert.Equal(4, host.ShieldDuration);
            var guard = rt.NewPowers.UnbowedGuard;
            Assert.IsType<UnstoppableEffect>(guard.effect); Assert.Equal(host.ShieldDuration, guard.duration);
            host.ShieldAmount = 0; // absorbing the shield does not own or destroy the native guard
            Assert.True(guard.isActive);
            UnityEngine.Time.time = 7.999f; Apply(stun); Assert.Equal(1, host.Shields);
            UnityEngine.Time.time = 8; Apply(stun); Assert.Equal(2, host.Shields);
            var current = rt.NewPowers.UnbowedGuard;
            host.ClearGuard(rt); Assert.False(current.isActive); Assert.Null(rt.NewPowers.UnbowedGuard);
        }
        [Fact]
        public void Non_enemy_non_stun_inactive_and_already_immune_applications_are_ignored()
        {
            var (host, rt) = Setup();
            Apply(Stun(rt, rt.Hero)); Apply(Stun(rt, new Entity())); Apply(Stun(rt, null));
            Apply(new SlowEffect { victim = rt.Hero, parent = new StatusEffect { info = new CastInfo { caster = new Entity { Relation = EntityRelation.Enemy } } } });
            var stun = Stun(rt, new Entity { Relation = EntityRelation.Enemy });
            rt.Hero.Status.hasCrowdControlImmunity = true; Apply(stun);
            rt.Hero.Status.hasCrowdControlImmunity = false;
            Mirror.NetworkServer.active = false; Apply(stun); Mirror.NetworkServer.active = true;
            stun.isAlive = false; Apply(stun); stun.isAlive = true;
            Assert.Equal(0, host.Shields); Assert.Null(rt.NewPowers.UnbowedGuard);
            Apply(stun); Assert.Equal(1, host.Shields);
        }
        [Fact]
        public void Cleanup_does_not_destroy_a_container_reused_by_another_effect()
        {
            var (host, rt) = Setup(); Apply(Stun(rt, new Entity { Relation = EntityRelation.Enemy }));
            var guard = rt.NewPowers.UnbowedGuard; guard.id = "AnotherEffect";
            host.ClearGuard(rt); Assert.True(guard.isActive); Assert.Null(rt.NewPowers.UnbowedGuard);
        }
    }
}
