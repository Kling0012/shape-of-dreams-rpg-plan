using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed class NativeMemoryPayloadScope
    {
        internal static NativeMemoryPayloadScope Current;
        internal Actor Source;
        internal Hero Owner;
        internal string Adapter;
        internal Type ChildType;
        internal Entity Victim;
        internal ReactionChain Chain;
        internal MemoryActivationIdentity Identity;
        internal bool HasIdentity;
        internal bool DirectPacketClaimed;
        internal bool Rejected;
    }

    [HarmonyPatch(typeof(Se_D_ChargedAnguillian), "ActiveLogicUpdate")]
    internal static class NativeChargedDischargeBatch
    {
        private static void Prefix(Se_D_ChargedAnguillian __instance, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginExactNativePayload(__instance, "native.charged.discharge",
                    typeof(Ai_D_ChargedAnguillian_Lightning), null)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(Se_D_IcyVeins), "EntityEventOnAttackEffectTriggered")]
    internal static class NativeIcyPayload
    {
        private static void Prefix(Se_D_IcyVeins __instance, EventInfoAttackEffect obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginExactNativePayload(__instance, "native.icy.additional",
                    typeof(Ai_D_IcyVeins_Damage), obj)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(St_D_DisintegratingClaw), "EntityEventOnAttackEffectTriggered")]
    internal static class NativeClawPayload
    {
        private static void Prefix(St_D_DisintegratingClaw __instance, EventInfoAttackEffect obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginExactNativePayload(__instance, "native.claw.additional", null, obj)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(Ai_D_PrismaticEyes_Attack), "OnEntity")]
    internal static class NativePrismaticAddedPacket
    {
        private static void Prefix(Ai_D_PrismaticEyes_Attack __instance, Projectile.EntityHit hit, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginExactAttackAddedPayload(__instance, hit.entity, "native.prismatic.additional")
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(St_D_Resolve), "CheckFourth")]
    internal static class NativeResolveFourthAttack
    {
        private static void Prefix(St_D_Resolve __instance, EventInfoAttackFired obj)
        {
            if (NetworkServer.active && obj.isThisAttackFourthAttack)
                HostAuthority.NativeInstance?.BindExactNativeAttack(__instance, obj.instance, obj.info, "native.resolve.fourth");
        }
    }

    [HarmonyPatch(typeof(Se_D_ConvergencePoint), "EntityEventOnAttackFiredBeforePrepare")]
    internal static class NativeConvergenceEmpoweredAttack
    {
        private static void Prefix(Se_D_ConvergencePoint __instance, EventInfoAttackFired obj)
        {
            if (NetworkServer.active && __instance.isEmpowered && obj.instance is Ai_Atk_YubarStardust)
                HostAuthority.NativeInstance?.BindExactNativeAttack(__instance, obj.instance, obj.info, "native.convergence.empowered");
        }
    }

    internal sealed partial class HostAuthority
    {
        private readonly Dictionary<Actor, ReactionChain> _attributedNativeChains = new Dictionary<Actor, ReactionChain>();

        private void RegisterExactNativeMemoryAdapters()
        {
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.charged.discharge", nameof(St_D_ChargedAnguillian), NativePayloadKind.PassiveBatch));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.icy.additional", nameof(St_D_IcyVeins), NativePayloadKind.AdditionalNative, true));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.claw.additional", nameof(St_D_DisintegratingClaw), NativePayloadKind.AdditionalNative, true));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.prismatic.additional", nameof(St_D_PrismaticEyes), NativePayloadKind.AdditionalNative));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.frozen.main", nameof(St_R_FrozenFists), NativePayloadKind.MainBasicAttack));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.resolve.fourth", nameof(St_D_Resolve), NativePayloadKind.MainBasicAttack));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.convergence.empowered", nameof(St_D_ConvergencePoint), NativePayloadKind.MainBasicAttack));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.killing-flow.main", nameof(St_D_TheKillingFlow), NativePayloadKind.MainBasicAttack));
            RegisterNativeAdditionalAdapters();
            RegisterNativeEndingAdapters();
            RegisterNativeProjectileAdapters();
        }

        private void BindExactNativeBasicSource(AttackTrigger attack, AbilityInstance instance, Hero hero, MemoryActivationIdentity identity)
        {
            if (attack is At_R_FrozenFists_Attack && instance is Ai_R_FrozenFists_Attack
                && hero.Status.TryGetStatusEffect<Se_R_FrozenFists>(out var effect))
                BindExactNativeAttack(effect, instance, instance.info, "native.frozen.main");
        }

        internal void BindResolveBeforePrepare(AttackTrigger attack, EventInfoCast cast)
        {
            if (attack.owner is Hero hero && cast.info.caster == hero
                && hero.Skill.GetSkill(HeroSkillLocation.Identity) is St_D_Resolve source)
                BindExactNativeAttack(source, cast.instance, cast.info, "native.resolve.fourth");
        }

        internal void BindExactNativeAttack(Actor source, AbilityInstance instance, CastInfo info, string adapter)
        {
            if (instance == null || !(info.caster is Hero hero) || !Alive(hero) || AttributionGeneratedOrigin() != GeneratedOrigin.None) return;
            var trigger = source as SkillTrigger ?? source.FindFirstAncestorOfType<SkillTrigger>();
            if (trigger == null || trigger.owner != hero || FindMemory(hero, trigger.GetType().Name) != trigger
                || !_memoryAttribution.TryGetInstance(instance.GetInstanceID(), out var original)
                || original.OwnerId != hero.GetInstanceID()) return;
            var derived = _memoryAttribution.DeriveNativePayload(original, adapter);
            if (derived.SourceMemory != trigger.GetType().Name) throw new InvalidOperationException("C02 native attack adapter has a conflicting skill parent.");
            _memoryAttribution.BindInstance(instance.GetInstanceID(), derived);
        }

        internal NativeMemoryPayloadScope BeginExactAttackAddedPayload(AbilityInstance instance, Entity victim, string adapter)
        {
            if (!TryGetMemoryActivation(instance, out var original) || original.NativePayloadKind != NativePayloadKind.MainBasicAttack) return null;
            var trigger = instance.FindFirstAncestorOfType<SkillTrigger>();
            if (trigger == null || trigger.owner == null || trigger.owner.GetInstanceID() != original.OwnerId
                || FindMemory(trigger.owner, trigger.GetType().Name) != trigger) return null;
            var identity = _memoryAttribution.DeriveNativePayload(original, adapter);
            if (identity.SourceMemory != trigger.GetType().Name) throw new InvalidOperationException("C02 native added packet has a conflicting skill parent.");
            return new NativeMemoryPayloadScope { Source = instance, Owner = trigger.owner, Adapter = adapter,
                Victim = victim, Identity = identity, HasIdentity = true };
        }

        internal NativeMemoryPayloadScope BeginExactNativePayload(Actor source, string adapter, Type childType, EventInfoAttackEffect? input)
        {
            if (AttributionGeneratedOrigin() != GeneratedOrigin.None || source == null) return null;
            SkillTrigger trigger = source as SkillTrigger;
            if (trigger == null) trigger = source.FindFirstAncestorOfType<SkillTrigger>();
            if (trigger == null || !(trigger.owner is Hero hero) || !Alive(hero)) return null;
            EnsureMemoryAttributionEquipment(hero);
            if (FindMemory(hero, trigger.GetType().Name) != trigger) return null;
            string expected;
            switch (adapter)
            {
                case "native.charged.discharge": expected = nameof(St_D_ChargedAnguillian); break;
                case "native.icy.additional": expected = nameof(St_D_IcyVeins); break;
                case "native.claw.additional": expected = nameof(St_D_DisintegratingClaw); break;
                default: throw new InvalidOperationException("Unregistered exact native payload scope: " + adapter);
            }
            if (trigger.GetType().Name != expected) throw new InvalidOperationException("C02 exact native adapter source does not match its inspected SkillTrigger parent.");
            var result = new NativeMemoryPayloadScope { Source = source, Owner = hero, Adapter = adapter, ChildType = childType };
            if (input.HasValue)
            {
                var notification = input.Value;
                // A nonempty incoming chain needs the exact already-admitted native dispatch, not actor proximity.
                if (notification.attacker != hero || !TryGetNativeTriggerActivation(notification, out var original)) return null;
                result.Identity = _memoryAttribution.DeriveNativePayload(original, adapter);
                result.HasIdentity = true;
                result.Victim = notification.victim;
                result.Chain = notification.chain.New(source);
            }
            return result;
        }

        private bool BindExactNativePayload(EventInfoAbilityInstance info)
        {
            var scope = NativeMemoryPayloadScope.Current;
            if (scope != null && scope.Source == info.actor && scope.Rejected) return true;
            if (scope == null || scope.ChildType == null || info.actor != scope.Source
                || info.instance.GetType() != scope.ChildType) return false;
            if (!scope.HasIdentity)
            {
                scope.Identity = _memoryAttribution.BeginNativeActivation(scope.Owner.GetInstanceID(), scope.Adapter);
                scope.HasIdentity = true;
            }
            _memoryAttribution.BindInstance(info.instance.GetInstanceID(), scope.Identity);
            if (scope.Identity.AllowsNativeReactionChain) _attributedNativeChains[info.instance] = scope.Chain;
            return true;
        }

        private bool TryGetExactNativeDirectPayload(Actor actor, Entity target, ReactionChain chain, out MemoryActivationIdentity identity)
        {
            identity = default(MemoryActivationIdentity);
            var scope = NativeMemoryPayloadScope.Current;
            if (scope == null || scope.ChildType != null || actor != scope.Source || target != scope.Victim
                || scope.Rejected || scope.DirectPacketClaimed || !scope.HasIdentity || !chain.Equals(scope.Chain) || AttributionGeneratedOrigin() != GeneratedOrigin.None) return false;
            identity = scope.Identity;
            if (!_memoryAttribution.IsCurrent(identity)) return false;
            scope.DirectPacketClaimed = true;
            return true;
        }

        private bool ExactNativeChainMatches(Actor actor, ReactionChain chain)
        {
            if (chain.Equals(default(ReactionChain))) return true;
            return _attributedNativeChains.TryGetValue(actor, out var expected) && chain.Equals(expected);
        }
    }
}
