using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Se_D_AstridsMasterpieceEnGarde), "CheckExposed")]
    internal static class NativeEnGardeAddedPacket
    {
        private static void Prefix(Se_D_AstridsMasterpieceEnGarde __instance, EventInfoAttackEffect obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginEnGardeAddedPacket(__instance, obj)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) => NativeMemoryPayloadScope.Current = __state;
    }

    [HarmonyPatch(typeof(Se_D_AstridsMasterpiecePriorite), "CheckExposed")]
    internal static class NativePrioriteAddedPacket
    {
        private static void Prefix(Se_D_AstridsMasterpiecePriorite __instance, EventInfoAttackEffect obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginPrioriteAddedPacket(__instance, obj)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) => NativeMemoryPayloadScope.Current = __state;
    }

    [HarmonyPatch(typeof(Se_R_FrozenFists), "<OnCreate>b__20_0")]
    internal static class NativeFrozenFistsAddedPacket
    {
        private static void Prefix(Se_R_FrozenFists __instance, EventInfoAttackEffect effect, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginFrozenFistsAddedPacket(__instance, effect)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) => NativeMemoryPayloadScope.Current = __state;
    }

    [HarmonyPatch(typeof(Se_R_DangerousTheory), "<EntityEventOnAttackFiredBeforePrepare>b__21_0")]
    internal static class NativeDangerousTheoryAddedPacket
    {
        private static void Prefix(Se_R_DangerousTheory __instance, EventInfoAttackEffect effect, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginDangerousTheoryAddedPacket(__instance, effect)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) => NativeMemoryPayloadScope.Current = __state;
    }

    [HarmonyPatch(typeof(Ai_D_ScarOfTheWind_DashAtk), "OnHit")]
    internal static class NativeWindScarAddedPacket
    {
        private static void Prefix(Ai_D_ScarOfTheWind_DashAtk __instance, Entity entity, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginWindScarAddedPacket(__instance, entity)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) => NativeMemoryPayloadScope.Current = __state;
    }

    internal sealed partial class HostAuthority
    {
        private void RegisterNativeAdditionalAdapters()
        {
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.en-garde.additional",
                nameof(St_D_AstridsMasterpieceEnGarde), NativePayloadKind.AdditionalNative));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.priorite.additional",
                nameof(St_D_AstridsMasterpiecePriorite), NativePayloadKind.AdditionalNative));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.frozen.additional",
                nameof(St_R_FrozenFists), NativePayloadKind.AdditionalNative, true));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.theory.additional",
                nameof(St_R_DangerousTheory), NativePayloadKind.AdditionalNative));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.wind-scar.additional",
                nameof(St_D_ScarOfTheWind), NativePayloadKind.AdditionalNative));
        }

        internal NativeMemoryPayloadScope BeginEnGardeAddedPacket(Se_D_AstridsMasterpieceEnGarde source, EventInfoAttackEffect input)
        {
            if (!(source.firstTrigger is St_D_AstridsMasterpieceEnGarde trigger)) return null;
            return BeginVerifiedStatusAddedPacket(source, trigger, input, "native.en-garde.additional", default);
        }

        internal NativeMemoryPayloadScope BeginPrioriteAddedPacket(Se_D_AstridsMasterpiecePriorite source, EventInfoAttackEffect input)
        {
            if (!(source.firstTrigger is St_D_AstridsMasterpiecePriorite trigger)) return null;
            return BeginVerifiedStatusAddedPacket(source, trigger, input, "native.priorite.additional", default);
        }

        internal NativeMemoryPayloadScope BeginFrozenFistsAddedPacket(Se_R_FrozenFists source, EventInfoAttackEffect input)
        {
            if (!(source.firstTrigger is St_R_FrozenFists trigger)) return null;
            return BeginVerifiedStatusAddedPacket(source, trigger, input, "native.frozen.additional", input.chain.New(source));
        }

        internal NativeMemoryPayloadScope BeginDangerousTheoryAddedPacket(Se_R_DangerousTheory source, EventInfoAttackEffect input)
        {
            if (!(source.firstTrigger is St_R_DangerousTheory trigger)) return null;
            // The inspected native delegate constructs a fresh default chain and sets its own crit condition.
            return BeginVerifiedStatusAddedPacket(source, trigger, input, "native.theory.additional", default);
        }

        private NativeMemoryPayloadScope BeginVerifiedStatusAddedPacket(StatusEffect source, SkillTrigger trigger,
            EventInfoAttackEffect input, string adapter, ReactionChain outputChain)
        {
            if (!(source.info.caster is Hero hero) || input.attacker != hero || trigger.owner != hero
                || !Alive(hero) || !source.isActive || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || FindMemory(hero, trigger.GetType().Name) != trigger || input.victim == null) return null;
            if (!TryGetNativeTriggerActivation(input, out var original)) return null;
            if (original.OwnerId != hero.GetInstanceID()) return null;
            var identity = _memoryAttribution.DeriveNativePayload(original, adapter);
            return new NativeMemoryPayloadScope { Source = source, Owner = hero, Adapter = adapter,
                Victim = input.victim, Chain = outputChain, Identity = identity, HasIdentity = true };
        }

        internal NativeMemoryPayloadScope BeginWindScarAddedPacket(Ai_D_ScarOfTheWind_DashAtk instance, Entity victim)
        {
            if (!(instance.info.caster is Hero hero) || !Alive(hero) || victim == null
                || !(instance.parentActor is At_D_ScarOfTheWind_DashAtk attack) || attack.owner != hero
                || !(hero.Skill.GetSkill(HeroSkillLocation.Identity) is St_D_ScarOfTheWind)
                || !TryGetMemoryActivation(instance, out var original)
                || original.NativePayloadKind != NativePayloadKind.MainBasicAttack) return null;
            // OnHit first dispatches the real basic hit, then one separately authored Wind Scar packet.
            // C02's main-dispatch guard prevents this scope from relabelling the primary packet.
            return new NativeMemoryPayloadScope { Source = instance, Owner = hero, Adapter = "native.wind-scar.additional",
                Victim = victim, Identity = _memoryAttribution.DeriveNativePayload(original, "native.wind-scar.additional"),
                HasIdentity = true, Chain = default };
        }
    }
}
