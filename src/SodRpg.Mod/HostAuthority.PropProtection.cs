using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Only Dreamforge-owned protections are admitted here. Native prop_iframe and
    // the prop's permanent crowd-control immunity retain their native lifecycle.
    [HarmonyPatch(typeof(Actor), nameof(Actor.CreateBasicEffect))]
    internal static class ModPropProtectionAdmission
    {
        private sealed class Protection
        {
            internal float Created;
        }

        private static readonly ConditionalWeakTable<BasicEffect, Protection> Protections =
            new ConditionalWeakTable<BasicEffect, Protection>();
        private static readonly ConditionalWeakTable<BasicEffect, Protection>.CreateValueCallback CreateProtection =
            _ => new Protection();

        internal static bool IsBreakableProp(Entity entity) => entity is PropEntity
            && !(entity is PropEnt_Merchant_Base) && !(entity is MockEntity);

        private static bool Prefix(Entity victim, BasicEffect eff, string id, ref Se_GenericEffectContainer __result)
        {
            if (!NetworkServer.active || eff == null
                || (eff.mask & (BasicEffectMask.Invulnerable | BasicEffectMask.Unstoppable | BasicEffectMask.Untargetable)) == 0
                || id == null || !id.StartsWith("DreamforgeRPG.", StringComparison.Ordinal)) return true;
            if (IsBreakableProp(victim))
            {
                __result = null;
                Log.Warn("Rejected Dreamforge prop protection: " + victim.GetType().Name + " id=" + id);
                return false;
            }
            Protections.GetValue(eff, CreateProtection).Created = Time.time;
            return true;
        }

        internal static bool IsStaleModProtection(BasicEffect effect, float now)
        {
            if (effect == null || !effect.isAlive
                || (effect.mask & (BasicEffectMask.Invulnerable | BasicEffectMask.Unstoppable | BasicEffectMask.Untargetable)) == 0)
                return false;
            // BasicEffect identity survives even if its pooled container is reused.
            if (Protections.TryGetValue(effect, out var protection)) return now - protection.Created >= 3f;
            return effect.parent is Se_GenericEffectContainer container && container.effect == effect
                && container.id != null && container.id.StartsWith("DreamforgeRPG.", StringComparison.Ordinal)
                && now - container.creationTime >= 3f;
        }
    }

    internal sealed partial class HostAuthority
    {
        private float _nextPropProtectionScan;
        private static readonly Action<EntityStatus, BasicEffect> RemovePropProtection = FindRemovePropProtection();

        private static Action<EntityStatus, BasicEffect> FindRemovePropProtection()
        {
            try
            {
                return (Action<EntityStatus, BasicEffect>)Delegate.CreateDelegate(typeof(Action<EntityStatus, BasicEffect>),
                    AccessTools.DeclaredMethod(typeof(EntityStatus), "RemoveBasicEffect"));
            }
            catch (Exception ex)
            {
                Log.Warn("Prop protection recovery unavailable: " + ex.Message);
                return null;
            }
        }

        private void TickPropProtection()
        {
            if (_tickNow < _nextPropProtectionScan || _am == null || NativeShieldEffects == null || RemovePropProtection == null) return;
            _nextPropProtectionScan = _tickNow + 1f;
            foreach (var entity in _am.allEntities)
            {
                if (!ModPropProtectionAdmission.IsBreakableProp(entity) || !entity.isActive || entity.Status == null) continue;
                var effects = NativeShieldEffects(entity.Status);
                for (int i = effects.Count - 1; i >= 0; i--)
                {
                    var effect = effects[i];
                    if (!ModPropProtectionAdmission.IsStaleModProtection(effect, _tickNow)) continue;
                    // Remove the individual registration, never destroy a possibly reused container.
                    var parent = effect.parent;
                    if (parent != null && parent.victim == entity) parent.StopBasicEffect(effect);
                    else RemovePropProtection(entity.Status, effect);
                    entity.Status.CalculateStatsIfDirty();
                    Log.Warn("Recovered Dreamforge prop protection: " + entity.GetType().Name + " effect=" + effect.GetType().Name);
                }
            }
        }
    }
}
