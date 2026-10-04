using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // AddBasicEffect calls CalculateStats -> UpdateStatusEffectInfo before returning.
    // Status-added alone runs too early for Se_GenericEffectContainer.DoBasicEffect.
    [HarmonyPatch(typeof(EntityStatus), "AddBasicEffect")]
    internal static class NativeCalmStunAdmission
    {
        private static void Prefix(BasicEffect eff, out HostAuthority.CalmStunCapture __state)
        {
            __state = NetworkServer.active
                ? HostAuthority.NativeInstance?.CaptureCalmStun(eff) : null;
        }

        private static void Postfix(BasicEffect eff, HostAuthority.CalmStunCapture __state)
        {
            if (__state != null && NetworkServer.active)
                HostAuthority.NativeInstance?.CompleteCalmStun(eff, __state);
        }
    }

    internal sealed partial class HostAuthority
    {
        internal sealed class CalmStunCapture
        {
            internal Hero Hero;
            internal long EffectSerial, ActorId;
            internal MemoryActivationIdentity Identity;
            internal StunSourceSlot Slot;
        }

        private sealed class CalmBinding
        {
            internal StunSourceFilter Filter;
            internal Action<Actor, Hero, CalmShieldGrant> ApplyOrdinaryPool;
        }

        private readonly Dictionary<Hero, CalmBinding> _calmBindings = new Dictionary<Hero, CalmBinding>();
        private long _calmEffectSerial;

        /// <summary>
        /// C07 must supply the explicit selected-key identity and C06 its ordinary pool dispatcher.
        /// No binding is inferred from the aggregated StillWater power in the legacy Build.
        /// </summary>
        internal void ConfigureCalmStun(Hero hero, bool selected,
            Action<Actor, Hero, CalmShieldGrant> applyOrdinaryPool)
        {
            if (hero == null) throw new ArgumentNullException(nameof(hero));
            if (!selected) { _calmBindings.Remove(hero); return; }
            if (hero.GetType().Name != "Hero_Cetus")
                throw new InvalidOperationException("C08 is scoped to the Cetus Calm key.");
            if (applyOrdinaryPool == null) throw new ArgumentNullException(nameof(applyOrdinaryPool));
            long epoch = RefreshMemoryAttributionEquipment(hero);
            if (!_calmBindings.TryGetValue(hero, out var binding))
                _calmBindings.Add(hero, binding = new CalmBinding { Filter = new StunSourceFilter(hero.GetInstanceID()) });
            binding.ApplyOrdinaryPool = applyOrdinaryPool;
            binding.Filter.Configure(epoch, CalmMemoryAt(hero, HeroSkillLocation.Q), CalmMemoryAt(hero, HeroSkillLocation.R), true);
        }

        private static string CalmMemoryAt(Hero hero, HeroSkillLocation slot)
        {
            var skill = hero.Skill.GetSkill(slot);
            return skill != null ? skill.GetType().Name : null;
        }

        internal CalmStunCapture CaptureCalmStun(BasicEffect effect)
        {
            if (!(effect is StunEffect) || effect.parent == null || effect.victim == null) return null;
            Actor source = effect.parent.parentActor;
            if (source == null || !TryGetMemoryActivation(source, out var identity)
                || identity.GeneratedOrigin != GeneratedOrigin.None
                || identity.NativePayloadKind == NativePayloadKind.SummonAttack) return null;
            Hero hero = null;
            foreach (var candidate in _calmBindings.Keys)
                if (candidate != null && candidate.GetInstanceID() == identity.OwnerId) { hero = candidate; break; }
            if (hero == null || !Alive(hero) || effect.victim.GetRelation(hero) != EntityRelation.Enemy) return null;
            if (!_memoryAttribution.CanAdmit(identity, effect.parent.gem != null,
                effect.parent is ElementalStatusEffect, !effect.parent.chain.Equals(default(ReactionChain)))
                || !ExactNativeChainMatches(source, effect.parent.chain)) return null;
            long epoch = RefreshMemoryAttributionEquipment(hero);
            if (identity.EquipmentEpoch != epoch) return null;
            var q = CalmMemoryAt(hero, HeroSkillLocation.Q);
            var r = CalmMemoryAt(hero, HeroSkillLocation.R);
            _calmBindings[hero].Filter.Configure(epoch, q, r, true);
            StunSourceSlot slot = identity.SourceMemory == q ? StunSourceSlot.Q
                : identity.SourceMemory == r ? StunSourceSlot.R : StunSourceSlot.Unknown;
            return new CalmStunCapture { Hero = hero, Identity = identity, Slot = slot,
                EffectSerial = checked(++_calmEffectSerial), ActorId = source.GetInstanceID() };
        }

        internal void CompleteCalmStun(BasicEffect effect, CalmStunCapture capture)
        {
            var hero = capture.Hero;
            if (hero == null || !Alive(hero) || !_calmBindings.TryGetValue(hero, out var binding)) return;
            if (RefreshMemoryAttributionEquipment(hero) != capture.Identity.EquipmentEpoch) return;
            bool succeeded = effect != null && effect.isAlive && effect.parent != null && effect.parent.isActive
                && effect.victim != null && effect.victim.isActive && effect.victim.Status.hasStun
                && !effect.victim.Status.hasCrowdControlImmunity;
            var source = new StunSourceApplication(capture.Identity.OwnerId, capture.EffectSerial,
                capture.ActorId, capture.Identity.ActivationId, capture.Identity.SourceMemory,
                capture.Slot, capture.Identity.GeneratedOrigin, succeeded, capture.Identity.EquipmentEpoch);
            if (!succeeded) return;
            if (_am == null || _am.serverActor == null)
                throw new InvalidOperationException("C08 requires the native root server actor for the C06 ordinary pool.");
            if (binding.Filter.TryApply(source, Time.time, out var grant))
                binding.ApplyOrdinaryPool(_am.serverActor, hero, grant);
        }

        private void ClearCalmStunBindings() => _calmBindings.Clear();
    }
}
