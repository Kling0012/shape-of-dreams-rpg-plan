using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Se_R_AnnihilationStance), "EntityEventOnAttackFired")]
    internal static class NativeAnnihilationProjectile
    {
        private static void Prefix(Se_R_AnnihilationStance __instance, EventInfoAttackFired obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginNativeProjectilePayload(__instance, "native.annihilation.projectile",
                    typeof(Ai_R_AnnihilationStance_Projectile), obj, null)
                    ?? new NativeMemoryPayloadScope { Source = __instance, ChildType = typeof(Ai_R_AnnihilationStance_Projectile), Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(Se_R_UnbreakableDetermination), "AttackEffect")]
    internal static class NativeDeterminationDischarge
    {
        private static void Prefix(Se_R_UnbreakableDetermination __instance, EventInfoAttackEffect obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginNativeProjectilePayload(__instance, "native.determination.discharge",
                    typeof(Ai_R_UnbreakableDetermination_SubExplosion), null, obj)
                    ?? new NativeMemoryPayloadScope { Source = __instance, ChildType = typeof(Ai_R_UnbreakableDetermination_SubExplosion), Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(Se_Q_IncendiaryRounds_EmpowerAttacks), "EntityEventOnAttackFired")]
    internal static class NativeIncendiaryPayload
    {
        private static void Prefix(Se_Q_IncendiaryRounds_EmpowerAttacks __instance, EventInfoAttackFired obj, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginNativeProjectilePayload(__instance, "native.incendiary.additional",
                    typeof(Ai_Q_IncendiaryRounds_Attack), obj, null)
                    ?? new NativeMemoryPayloadScope { Source = __instance, ChildType = typeof(Ai_Q_IncendiaryRounds_Attack), Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    // Native coroutine fields are exact inspected compiler-generated members, not a search by type-name prefix.
    internal static class NativeDoubleTapCoroutineContract
    {
        internal static readonly Type ClosureType, IteratorType;
        internal static readonly FieldInfo Source, Fired;
        internal static readonly MethodInfo Factory, MoveNext;
        internal static readonly bool Available;
        static NativeDoubleTapCoroutineContract()
        {
            try
            {
                ClosureType = typeof(Se_D_DoubleTap).GetNestedType("<>c__DisplayClass13_0", BindingFlags.NonPublic)
                    ?? throw new MissingMemberException("Native Double Tap coroutine closure is unavailable.");
                IteratorType = ClosureType.GetNestedType("<<EntityEventOnAttackFired>g__Routine|0>d", BindingFlags.NonPublic)
                    ?? throw new MissingMemberException("Native Double Tap coroutine iterator is unavailable.");
                Source = ClosureType.GetField("<>4__this", BindingFlags.Public | BindingFlags.Instance);
                Fired = ClosureType.GetField("obj", BindingFlags.Public | BindingFlags.Instance);
                if (Source == null || Source.IsStatic || Source.FieldType != typeof(Se_D_DoubleTap)
                    || Fired == null || Fired.IsStatic || Fired.FieldType != typeof(EventInfoAttackFired))
                    throw new MissingFieldException("Native Double Tap source/fired-event capture is unavailable.");
                Factory = ClosureType.GetMethod("<EntityEventOnAttackFired>g__Routine|0",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("Native Double Tap coroutine factory is unavailable.");
                MoveNext = IteratorType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("Native Double Tap coroutine continuation is unavailable.");
                Available = true;
            }
            catch (Exception ex) { Log.Warn("Double Tap coroutine attribution disabled: " + ex.Message); }
        }
        internal static readonly ConditionalWeakTable<Se_D_DoubleTap, object> Lifetimes = new ConditionalWeakTable<Se_D_DoubleTap, object>();
        internal static readonly ConditionalWeakTable<object, PendingDoubleTap> Pending = new ConditionalWeakTable<object, PendingDoubleTap>();
        internal static PendingDoubleTap Current;

        internal sealed class PendingDoubleTap
        {
            internal HostAuthority Host;
            internal Se_D_DoubleTap Source;
            internal SkillTrigger Skill;
            internal Hero Owner;
            internal object Lifetime;
            internal MemoryActivationIdentity OriginalAttack;
            internal bool Rejected;
        }
    }

    [HarmonyPatch(typeof(Se_D_DoubleTap), "OnCreate")]
    internal static class NativeDoubleTapLifetimeStart
    {
        private static void Prefix(Se_D_DoubleTap __instance)
        {
            NativeDoubleTapCoroutineContract.Lifetimes.Remove(__instance);
            NativeDoubleTapCoroutineContract.Lifetimes.Add(__instance, new object());
        }
    }
    [HarmonyPatch(typeof(Se_D_DoubleTap), "OnDestroyActor")]
    internal static class NativeDoubleTapLifetimeEnd
    {
        private static void Prefix(Se_D_DoubleTap __instance) { NativeDoubleTapCoroutineContract.Lifetimes.Remove(__instance); }
    }

    [HarmonyPatch]
    internal static class NativeDoubleTapCoroutineCapture
    {
        private static bool Prepare() => NativeDoubleTapCoroutineContract.Available;
        private static MethodBase TargetMethod() => NativeDoubleTapCoroutineContract.Factory;
        private static void Postfix(object __instance, IEnumerator __result)
        {
            if (!NetworkServer.active || __result == null) return;
            var source = (Se_D_DoubleTap)NativeDoubleTapCoroutineContract.Source.GetValue(__instance);
            var fired = (EventInfoAttackFired)NativeDoubleTapCoroutineContract.Fired.GetValue(__instance);
            var pending = HostAuthority.NativeInstance?.CaptureDoubleTapCoroutine(source, fired)
                ?? new NativeDoubleTapCoroutineContract.PendingDoubleTap { Source = source, Rejected = true };
            NativeDoubleTapCoroutineContract.Pending.Add(__result, pending);
        }
    }

    [HarmonyPatch]
    internal static class NativeDoubleTapCoroutineResume
    {
        private static bool Prepare() => NativeDoubleTapCoroutineContract.Available;
        private static MethodBase TargetMethod() => NativeDoubleTapCoroutineContract.MoveNext;
        private static void Prefix(object __instance, out NativeDoubleTapCoroutineContract.PendingDoubleTap __state)
        {
            __state = NativeDoubleTapCoroutineContract.Current;
            NativeDoubleTapCoroutineContract.Current = NetworkServer.active
                && NativeDoubleTapCoroutineContract.Pending.TryGetValue(__instance, out var pending) ? pending : null;
        }
        private static void Finalizer(NativeDoubleTapCoroutineContract.PendingDoubleTap __state) { NativeDoubleTapCoroutineContract.Current = __state; }
    }

    [HarmonyPatch(typeof(AttackTrigger), nameof(AttackTrigger.CallAttackCompleteBeforePrepareRoutines))]
    internal static class NativeDoubleTapSecondaryInstance
    {
        // C02's prefix creates the actual second shot serial first; this postfix retags just that fired instance before OnPrepare.
        private static void Postfix(AttackTrigger __instance, EventInfoCast cast)
        {
            if (NetworkServer.active && NativeDoubleTapCoroutineContract.Current != null)
                HostAuthority.NativeInstance?.BindDoubleTapSecondary(__instance, cast, NativeDoubleTapCoroutineContract.Current);
        }
    }

    internal sealed partial class HostAuthority
    {
        private static bool RequiresExactNativeProjectileScope(Actor actor) => actor is Ai_R_AnnihilationStance_Projectile
            || actor is Ai_R_UnbreakableDetermination_SubExplosion || actor is Ai_Q_IncendiaryRounds_Attack;

        private void RegisterNativeProjectileAdapters()
        {
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.annihilation.projectile", nameof(St_R_AnnihilationStance), NativePayloadKind.PassiveBatch));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.determination.discharge", nameof(St_R_UnbreakableDetermination), NativePayloadKind.PassiveBatch, true));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.incendiary.additional", nameof(St_Q_IncendiaryRounds), NativePayloadKind.AdditionalNative));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.doubletap.secondary", nameof(St_D_DoubleTap), NativePayloadKind.AdditionalNative));
        }

        internal NativeMemoryPayloadScope BeginNativeProjectilePayload(Actor source, string adapter, Type child,
            EventInfoAttackFired? fired, EventInfoAttackEffect? effect)
        {
            if (source == null || AttributionGeneratedOrigin() != GeneratedOrigin.None) return null;
            var trigger = source.FindFirstAncestorOfType<SkillTrigger>();
            if (trigger == null || !(trigger.owner is Hero hero) || !Alive(hero) || FindMemory(hero, trigger.GetType().Name) != trigger) return null;
            EnsureMemoryAttributionEquipment(hero);
            string expected;
            switch (adapter)
            {
                case "native.annihilation.projectile": expected = nameof(St_R_AnnihilationStance); break;
                case "native.determination.discharge": expected = nameof(St_R_UnbreakableDetermination); break;
                case "native.incendiary.additional": expected = nameof(St_Q_IncendiaryRounds); break;
                default: throw new InvalidOperationException("Unknown exact native projectile adapter.");
            }
            if (trigger.GetType().Name != expected) throw new InvalidOperationException("Native projectile source conflicts with its actual skill parent.");
            var scope = new NativeMemoryPayloadScope { Source = source, Owner = hero, Adapter = adapter, ChildType = child };
            if (fired.HasValue)
            {
                var notification = fired.Value;
                if (!(notification.actor is AttackTrigger attack) || attack.owner != hero || notification.info.caster != hero
                    || notification.instance == null || !_memoryAttribution.TryGetInstance(notification.instance.GetInstanceID(), out var original)
                    || original.OwnerId != hero.GetInstanceID() || !_memoryAttribution.IsCurrent(original)) return null;
                if (adapter == "native.incendiary.additional")
                {
                    scope.Identity = _memoryAttribution.DeriveNativePayload(original, adapter);
                    scope.HasIdentity = true;
                }
            }
            if (effect.HasValue)
            {
                var notification = effect.Value;
                if (notification.attacker != hero || !TryGetNativeTriggerActivation(notification, out var original)
                    || original.OwnerId != hero.GetInstanceID()) return null;
                // The exact native creation callback assigns obj.chain.New(this) to this child.
                scope.Chain = notification.chain.New(source);
            }
            return scope;
        }

        internal NativeDoubleTapCoroutineContract.PendingDoubleTap CaptureDoubleTapCoroutine(Se_D_DoubleTap source, EventInfoAttackFired fired)
        {
            if (source == null || !(source.victim is Hero hero) || !Alive(hero) || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || !(fired.actor is At_Atk_LacertaRifle attack) || attack.owner != hero || fired.info.caster != hero
                || fired.instance == null || !NativeDoubleTapCoroutineContract.Lifetimes.TryGetValue(source, out var lifetime)) return null;
            EnsureMemoryAttributionEquipment(hero);
            var trigger = source.FindFirstAncestorOfType<St_D_DoubleTap>();
            if (trigger == null || trigger.owner != hero || FindMemory(hero, nameof(St_D_DoubleTap)) != trigger
                || !_memoryAttribution.TryGetInstance(fired.instance.GetInstanceID(), out var original)
                || original.OwnerId != hero.GetInstanceID() || !_memoryAttribution.IsCurrent(original)) return null;
            var pending = new NativeDoubleTapCoroutineContract.PendingDoubleTap
            { Host = this, Source = source, Owner = hero, Skill = trigger, Lifetime = lifetime, OriginalAttack = original };
            RetainDeferredAttribution(pending, original);
            return pending;
        }

        internal void BindDoubleTapSecondary(AttackTrigger attack, EventInfoCast cast, NativeDoubleTapCoroutineContract.PendingDoubleTap pending)
        {
            if (!(attack is At_Atk_LacertaRifle) || !(cast.instance is Ai_Atk_LacertaRifle || cast.instance is Ai_Atk_LacertaRifle_Crit)) return;
            if (pending.Rejected || !ReferenceEquals(pending.Host, this) || attack.owner != pending.Owner || cast.info.caster != pending.Owner
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !Alive(pending.Owner)
                || !NativeDoubleTapCoroutineContract.Lifetimes.TryGetValue(pending.Source, out var lifetime)
                || !ReferenceEquals(lifetime, pending.Lifetime))
            {
                _memoryAttribution.EndInstanceLifetime(cast.instance.GetInstanceID());
                return;
            }
            EnsureMemoryAttributionEquipment(pending.Owner);
            if (!_memoryAttribution.IsCurrent(pending.OriginalAttack) || FindMemory(pending.Owner, nameof(St_D_DoubleTap)) != pending.Skill)
            {
                _memoryAttribution.EndInstanceLifetime(cast.instance.GetInstanceID());
                return;
            }
            BindExactNativeAttack(pending.Source, cast.instance, cast.info, "native.doubletap.secondary");
        }
    }
}
