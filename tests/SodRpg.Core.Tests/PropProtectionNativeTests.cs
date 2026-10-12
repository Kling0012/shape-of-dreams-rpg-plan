using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

// Native API doubles for the prop-protection guard (HostAuthority.PropProtection.cs, #1012):
// the admission prefix and the recovery scan run against the Dew.Core surface they call
// (r.1.4.0.13: BasicEffectMask flags, PropEntity hierarchy, EntityStatus registry).
namespace SodRpg.Mod
{
    [Flags]
    internal enum BasicEffectMask : uint
    {
        Slow = 1u,
        Invulnerable = 0x800u,
        Unstoppable = 0x1000u,
        Protected = 0x2000u,
        Uncollidable = 0x4000u,
        Untargetable = 0x8000u
    }

    internal partial class BasicEffect { public BasicEffectMask mask; }

    internal class PropEntity : Entity { public float hitInvulnerableTime = 0.1f; }
    internal class PropEnt_Merchant_Base : PropEntity { }
    internal class MockEntity : Entity { }
    internal sealed class InvulnerableEffect : BasicEffect { }

    internal sealed partial class EntityStatus
    {
        // The registry the recovery scan reads through HostAuthority.NativeShieldEffects.
        public readonly List<BasicEffect> _basicEffects = new List<BasicEffect>();
        internal void RemoveBasicEffect(BasicEffect eff) { eff.isAlive = false; _basicEffects.Remove(eff); }
    }

    internal sealed partial class HostAuthority
    {
        internal float _tickNow;
        internal static readonly Func<EntityStatus, List<BasicEffect>> NativeShieldEffects = status => status._basicEffects;
        internal void RegisterEntity(Entity entity) => _am.allEntities.Add(entity);
        internal void RunPropProtectionScan() => TickPropProtection();
    }
}

namespace SodRpg.Core.Tests
{
    // Regression tests for the permanently invulnerable breakable props (#1012): a
    // DreamforgeRPG-owned Invulnerable/Unstoppable/Untargetable effect registered on a stone
    // must be rejected at admission and unregistered by the recovery scan, while the native
    // prop_iframe lifecycle, merchants and MockEntity stay untouched.
    public class PropProtectionNativeTests
    {
        public PropProtectionNativeTests()
        {
            UnityEngine.Time.time = 0f;
            Mirror.NetworkServer.active = true;
        }

        private static BasicEffect Effect(BasicEffectMask mask) => new InvulnerableEffect { mask = mask };

        // Invoke the production admission prefix the way Harmony would.
        private static bool Admit(Entity victim, BasicEffect eff, string id)
        {
            var prefix = typeof(ModPropProtectionAdmission).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)prefix.Invoke(null, new object[] { victim, eff, id, null });
        }

        // Register an effect on the victim's status the way the native CreateBasicEffect path would.
        private static void Register(Entity victim, BasicEffect eff, string id, float creationTime)
        {
            var container = new Se_GenericEffectContainer { victim = victim, effect = eff, id = id, duration = 60f };
            ((StatusEffect)container).victim = victim;
            container.creationTime = creationTime;
            eff.victim = victim;
            eff.parent = container;
            eff.isAlive = true;
            victim.Status._basicEffects.Add(eff);
        }

        [Fact]
        public void Admission_rejects_only_mod_owned_protection_on_breakable_props()
        {
            foreach (var mask in new[] { BasicEffectMask.Invulnerable, BasicEffectMask.Unstoppable, BasicEffectMask.Untargetable })
                Assert.False(Admit(new PropEntity(), Effect(mask), "DreamforgeRPG.Polaris.InvulnerableEffect"));
            // Native lifecycle (prop_iframe has no id, game ids are not DreamforgeRPG.*) stays admitted.
            Assert.True(Admit(new PropEntity(), Effect(BasicEffectMask.Invulnerable), null));
            Assert.True(Admit(new PropEntity(), Effect(BasicEffectMask.Invulnerable), "Se_SomeNativeEffect"));
            Assert.True(Admit(new PropEntity(), new InvulnerableEffect(), "DreamforgeRPG.Polaris.InvulnerableEffect")); // no protection mask
            Assert.True(Admit(new Entity(), Effect(BasicEffectMask.Invulnerable), "DreamforgeRPG.Polaris.InvulnerableEffect")); // not a prop
            Mirror.NetworkServer.active = false;
            Assert.True(Admit(new PropEntity(), Effect(BasicEffectMask.Invulnerable), "DreamforgeRPG.Polaris.InvulnerableEffect"));
        }

        [Fact]
        public void Merchants_and_mock_entities_stay_out_of_the_guard()
        {
            Assert.True(Admit(new PropEnt_Merchant_Base(), Effect(BasicEffectMask.Invulnerable), "DreamforgeRPG.Polaris.InvulnerableEffect"));
            Assert.True(Admit(new MockEntity(), Effect(BasicEffectMask.Invulnerable), "DreamforgeRPG.Polaris.InvulnerableEffect"));

            var host = new HostAuthority();
            var merchant = new PropEnt_Merchant_Base();
            var mock = new MockEntity();
            host.RegisterEntity(merchant);
            host.RegisterEntity(mock);
            Register(merchant, Effect(BasicEffectMask.Invulnerable), "DreamforgeRPG.Polaris.InvulnerableEffect", 0f);
            Register(mock, Effect(BasicEffectMask.Invulnerable), "DreamforgeRPG.Polaris.InvulnerableEffect", 0f);
            host._tickNow = 10f;
            host.RunPropProtectionScan();
            Assert.True(merchant.Status._basicEffects[0].isAlive);
            Assert.True(mock.Status._basicEffects[0].isAlive);
        }

        [Fact]
        public void Recovery_scan_unregisters_only_stale_mod_protection()
        {
            var host = new HostAuthority();
            var stone = new PropEntity();
            host.RegisterEntity(stone);
            host._tickNow = 10f;

            // A MOD protection that slipped past admission (pre-fix registration) is unregistered.
            var stale = Effect(BasicEffectMask.Invulnerable);
            Register(stone, stale, "DreamforgeRPG.Polaris.InvulnerableEffect", 5f);
            // A fresh MOD registration and the native prop_iframe registration are left alone.
            var young = Effect(BasicEffectMask.Unstoppable);
            Register(stone, young, "DreamforgeRPG.UnbowedMind", 9.5f);
            var iframe = Effect(BasicEffectMask.Invulnerable);
            Register(stone, iframe, null, 0f);
            // A stale registration parented elsewhere is removed through EntityStatus.RemoveBasicEffect.
            var elsewhere = Effect(BasicEffectMask.Invulnerable);
            Register(new Entity(), elsewhere, "DreamforgeRPG.Polaris.InvulnerableEffect", 4f);
            stone.Status._basicEffects.Add(elsewhere);

            host.RunPropProtectionScan();

            Assert.False(stale.isAlive);
            Assert.True(((Se_GenericEffectContainer)stale.parent).isActive); // the shared container is never destroyed
            Assert.True(young.isAlive);
            Assert.True(iframe.isAlive);
            Assert.False(elsewhere.isAlive);
            Assert.DoesNotContain(elsewhere, stone.Status._basicEffects);
        }
    }
}
