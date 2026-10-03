using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed class NativeFeatherTriggerCapture
    {
        [ThreadStatic] internal static NativeFeatherTriggerCapture Current;
        internal Se_D_BeautifulThreat Source;
        internal Entity Victim;
        internal ReactionChain Chain;
        internal MemoryActivationIdentity Input;
        internal HostAuthority Host;
        internal long Lifetime;
        internal bool Admitted;
    }

    internal static class NativeFeatherDelayedContract
    {
        internal static readonly Type Closure = typeof(Se_D_BeautifulThreat).GetNestedType("<>c__DisplayClass19_0", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("C02 native Feather delayed closure was not found.");
        internal static readonly FieldInfo Source = AccessTools.Field(Closure, "<>4__this")
            ?? throw new MissingFieldException("C02 native Feather source capture was not found.");
        internal static readonly FieldInfo Effect = AccessTools.Field(Closure, "obj")
            ?? throw new MissingFieldException("C02 native Feather attack-effect capture was not found.");
        internal static readonly ConditionalWeakTable<object, NativeFeatherTriggerCapture> Captures =
            new ConditionalWeakTable<object, NativeFeatherTriggerCapture>();
        internal static readonly ConditionalWeakTable<Actor, NativeBaptismCoroutineContract.Lifetime> Lifetimes =
            new ConditionalWeakTable<Actor, NativeBaptismCoroutineContract.Lifetime>();
    }

    [HarmonyPatch(typeof(Se_D_BeautifulThreat), "OnCreate")]
    internal static class NativeFeatherLifetime
    {
        private static void Prefix(Se_D_BeautifulThreat __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BeginNativeFeatherLifetime(__instance);
        }
    }

    [HarmonyPatch(typeof(Se_D_BeautifulThreat), "EntityEventOnAttackEffectTriggered")]
    internal static class NativeFeatherDelayedCapture
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(Dew), nameof(Dew.CallDelayed), new[] { typeof(Action), typeof(int) })
                ?? throw new MissingMethodException("C02 native delayed Action method was not found.");
            var replacement = AccessTools.Method(typeof(NativeFeatherDelayedCapture), nameof(CaptureAndDelay));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(target)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("C02 Feather requires exactly one verified native delayed callback.");
        }
        private static void CaptureAndDelay(Action callback, int frames)
        {
            if (NetworkServer.active)
            {
                if (callback == null || callback.Target == null || callback.Target.GetType() != NativeFeatherDelayedContract.Closure)
                    throw new InvalidOperationException("C02 Feather delayed callback has an unexpected native closure.");
                var source = (Se_D_BeautifulThreat)NativeFeatherDelayedContract.Source.GetValue(callback.Target);
                var effect = (EventInfoAttackEffect)NativeFeatherDelayedContract.Effect.GetValue(callback.Target);
                var capture = HostAuthority.NativeInstance?.CaptureNativeFeatherTrigger(source, effect)
                    ?? new NativeFeatherTriggerCapture { Source = source };
                NativeFeatherDelayedContract.Captures.Add(callback.Target, capture);
            }
            Dew.CallDelayed(callback, frames);
        }
    }

    [HarmonyPatch]
    internal static class NativeFeatherDelayedDispatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(NativeFeatherDelayedContract.Closure, "<EntityEventOnAttackEffectTriggered>b__0")
            ?? throw new MissingMethodException("C02 native Feather delayed dispatch was not found.");
        private static void Prefix(object __instance, out NativeFeatherTriggerCapture __state)
        {
            __state = NativeFeatherTriggerCapture.Current;
            NativeFeatherTriggerCapture.Current = null;
            if (!NetworkServer.active) return;
            NativeFeatherTriggerCapture.Current = NativeFeatherDelayedContract.Captures.TryGetValue(__instance, out var capture)
                ? capture : new NativeFeatherTriggerCapture { Source = (Se_D_BeautifulThreat)NativeFeatherDelayedContract.Source.GetValue(__instance) };
        }
        private static void Finalizer(NativeFeatherTriggerCapture __state) { NativeFeatherTriggerCapture.Current = __state; }
    }

    [HarmonyPatch(typeof(Se_D_BeautifulThreat), "CheckTarget")]
    internal static class NativeFeatherDispatch
    {
        private static void Prefix(Se_D_BeautifulThreat __instance, Entity target, ReactionChain eventChain,
            out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginNativeFeatherDispatch(__instance, target, eventChain)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    internal static class NativeBaptismCoroutineContract
    {
        internal static readonly Type Closure = typeof(Se_R_BaptismOfSun_Buff).GetNestedType("<>c__DisplayClass15_0", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("C02 native Baptism callback closure was not found.");
        internal static readonly Type Iterator = Closure.GetNestedType("<<OnCreate>g__Routine|2>d", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("C02 native Baptism damage iterator was not found.");
        internal static readonly FieldInfo Buff = AccessTools.Field(Closure, "<>4__this")
            ?? throw new MissingFieldException("C02 native Baptism buff capture was not found.");
        internal static readonly FieldInfo Effect = AccessTools.Field(Closure, "effect")
            ?? throw new MissingFieldException("C02 native Baptism attack-effect capture was not found.");
        internal static readonly FieldInfo IteratorClosure = AccessTools.Field(Iterator, "<>4__this")
            ?? throw new MissingFieldException("C02 native Baptism iterator closure was not found.");
        internal sealed class Lifetime { internal long Serial; internal HostAuthority Host; }
        internal static readonly ConditionalWeakTable<Actor, Lifetime> BuffLifetimes = new ConditionalWeakTable<Actor, Lifetime>();
        internal static readonly ConditionalWeakTable<NativeMemoryPayloadScope, Lifetime> CaptureLifetimes =
            new ConditionalWeakTable<NativeMemoryPayloadScope, Lifetime>();
        internal static readonly ConditionalWeakTable<object, NativeMemoryPayloadScope> Captures =
            new ConditionalWeakTable<object, NativeMemoryPayloadScope>();
    }

    [HarmonyPatch(typeof(Se_R_BaptismOfSun_Buff), "OnCreate")]
    internal static class NativeBaptismBuffLifetime
    {
        private static void Prefix(Se_R_BaptismOfSun_Buff __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BeginNativeBaptismBuffLifetime(__instance);
        }
    }

    [HarmonyPatch]
    internal static class NativeBaptismCoroutineCapture
    {
        private static MethodBase TargetMethod() => AccessTools.Method(NativeBaptismCoroutineContract.Closure, "<OnCreate>g__Routine|2")
            ?? throw new MissingMethodException("C02 native Baptism coroutine factory was not found.");
        private static void Postfix(object __instance, IEnumerator __result)
        {
            if (!NetworkServer.active || __result == null) return;
            var buff = (Se_R_BaptismOfSun_Buff)NativeBaptismCoroutineContract.Buff.GetValue(__instance);
            var effect = (EventInfoAttackEffect)NativeBaptismCoroutineContract.Effect.GetValue(__instance);
            var capture = HostAuthority.NativeInstance?.CaptureNativeBaptismDamage(buff, effect)
                ?? new NativeMemoryPayloadScope { Source = buff, Rejected = true };
            NativeBaptismCoroutineContract.Captures.Add(__result, capture);
        }
    }

    [HarmonyPatch]
    internal static class NativeBaptismCoroutineDispatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(NativeBaptismCoroutineContract.Iterator, "MoveNext")
            ?? throw new MissingMethodException("C02 native Baptism damage iterator method was not found.");
        private static void Prefix(object __instance, out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = null;
            if (!NetworkServer.active) return;
            if (NativeBaptismCoroutineContract.Captures.TryGetValue(__instance, out var capture))
                NativeMemoryPayloadScope.Current = HostAuthority.NativeInstance != null
                    && HostAuthority.NativeInstance.NativeBaptismCaptureIsCurrent(capture)
                        ? capture : new NativeMemoryPayloadScope { Source = capture.Source, Rejected = true };
            else
            {
                var closure = NativeBaptismCoroutineContract.IteratorClosure.GetValue(__instance);
                NativeMemoryPayloadScope.Current = new NativeMemoryPayloadScope
                    { Source = (Actor)NativeBaptismCoroutineContract.Buff.GetValue(closure), Rejected = true };
            }
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    [HarmonyPatch(typeof(Se_Star_Vesper_F_BS_ExplosionOnEnd), "OnCreated")]
    internal static class NativeBaptismEndingCapture
    {
        private static void Prefix(Se_Star_Vesper_F_BS_ExplosionOnEnd __instance, EventInfoAbilityInstance obj)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.CaptureNativeBaptismEnding(__instance, obj);
        }
    }

    [HarmonyPatch(typeof(Se_Star_Vesper_F_BS_ExplosionOnEnd), "<OnCreated>b__9_0")]
    internal static class NativeBaptismEndingDispatch
    {
        private static void Prefix(Se_Star_Vesper_F_BS_ExplosionOnEnd __instance, Actor __0,
            out NativeMemoryPayloadScope __state)
        {
            __state = NativeMemoryPayloadScope.Current;
            NativeMemoryPayloadScope.Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginNativeBaptismEnding(__instance, __0)
                    ?? new NativeMemoryPayloadScope { Source = __instance, Rejected = true } : null;
        }
        private static void Finalizer(NativeMemoryPayloadScope __state) { NativeMemoryPayloadScope.Current = __state; }
    }

    internal sealed partial class HostAuthority
    {
        private readonly Dictionary<(Actor Star, Actor Buff), MemoryActivationIdentity> _nativeBaptismEndings =
            new Dictionary<(Actor, Actor), MemoryActivationIdentity>();

        private void RegisterNativeEndingAdapters()
        {
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.feather.dispatch", nameof(St_D_BeautifulThreat), NativePayloadKind.AdditionalNative, true));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.baptism.buff", nameof(St_R_BaptismOfSun), NativePayloadKind.AdditionalNative, true));
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.baptism.ending", nameof(St_R_BaptismOfSun), NativePayloadKind.NativeEndingPhase));
        }

        internal NativeMemoryPayloadScope BeginNativeFeatherDispatch(Se_D_BeautifulThreat source, Entity target, ReactionChain chain)
        {
            if (source == null || target == null || !source.isActive || AttributionGeneratedOrigin() != GeneratedOrigin.None) return null;
            var captured = NativeFeatherTriggerCapture.Current;
            if (captured != null && captured.Source == source)
            {
                if (!captured.Admitted || !ReferenceEquals(captured.Host, this) || captured.Victim != target || !captured.Chain.Equals(chain)
                    || !_memoryAttribution.IsCurrent(captured.Input)
                    || !NativeFeatherDelayedContract.Lifetimes.TryGetValue(source, out var lifetime)
                    || captured.Lifetime != lifetime.Serial) return null;
            }
            else if (!chain.Equals(default(ReactionChain))) return null;
            var trigger = source.FindFirstAncestorOfType<SkillTrigger>();
            if (!(trigger is St_D_BeautifulThreat) || !(trigger.owner is Hero hero) || !Alive(hero)
                || source.info.caster != hero || FindMemory(hero, nameof(St_D_BeautifulThreat)) != trigger) return null;
            RefreshMemoryAttributionEquipment(hero);
            return new NativeMemoryPayloadScope
            {
                Source = source, Owner = hero, Adapter = "native.feather.dispatch", ChildType = typeof(Ai_D_BeautifulThreat_Feather),
                Victim = target, Chain = chain.New(source)
            };
        }

        internal void BeginNativeFeatherLifetime(Se_D_BeautifulThreat source)
        {
            var lifetime = NativeFeatherDelayedContract.Lifetimes.GetValue(source, _ => new NativeBaptismCoroutineContract.Lifetime());
            lifetime.Serial = _memoryAttribution.NewPacketId(); lifetime.Host = this;
        }

        internal NativeFeatherTriggerCapture CaptureNativeFeatherTrigger(Se_D_BeautifulThreat source, EventInfoAttackEffect effect)
        {
            if (source == null || !source.isActive || !(source.info.caster is Hero hero) || effect.attacker != hero
                || !TryGetNativeTriggerActivation(effect, out var input) || input.OwnerId != hero.GetInstanceID()) return null;
            if (!NativeFeatherDelayedContract.Lifetimes.TryGetValue(source, out var lifetime) || !ReferenceEquals(lifetime.Host, this))
                throw new InvalidOperationException("C02 Feather has no captured native creation lifetime.");
            return new NativeFeatherTriggerCapture
                { Source = source, Victim = effect.victim, Chain = effect.chain, Input = input, Lifetime = lifetime.Serial, Host = this, Admitted = true };
        }

        internal NativeMemoryPayloadScope CaptureNativeBaptismDamage(Se_R_BaptismOfSun_Buff buff, EventInfoAttackEffect effect)
        {
            if (buff == null || effect.victim == null || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || !TryGetMemoryActivation(buff, out var original)
                || original.SourceMemory != nameof(St_R_BaptismOfSun) || !(effect.attacker is Hero hero)
                || hero.GetInstanceID() != original.OwnerId || effect.attacker != buff.info.caster
                || !TryGetNativeTriggerActivation(effect, out var attack) || attack.OwnerId != original.OwnerId) return null;
            if (!NativeBaptismCoroutineContract.BuffLifetimes.TryGetValue(buff, out var lifetime) || !ReferenceEquals(lifetime.Host, this))
                throw new InvalidOperationException("C02 Baptism buff has no captured native creation lifetime.");
            var capture = new NativeMemoryPayloadScope
            {
                Source = buff, Owner = hero, Adapter = "native.baptism.buff", Victim = effect.victim,
                Chain = effect.chain.New(buff), HasIdentity = true,
                Identity = _memoryAttribution.DeriveNativePayload(original, "native.baptism.buff")
            };
            NativeBaptismCoroutineContract.CaptureLifetimes.Add(capture,
                new NativeBaptismCoroutineContract.Lifetime { Serial = lifetime.Serial, Host = this });
            return capture;
        }

        internal void BeginNativeBaptismBuffLifetime(Se_R_BaptismOfSun_Buff buff)
        {
            var lifetime = NativeBaptismCoroutineContract.BuffLifetimes.GetValue(buff, _ => new NativeBaptismCoroutineContract.Lifetime());
            lifetime.Serial = _memoryAttribution.NewPacketId(); lifetime.Host = this;
        }

        internal bool NativeBaptismCaptureIsCurrent(NativeMemoryPayloadScope capture)
            => !capture.Rejected && !ReferenceEquals(capture.Source, null) && Alive(capture.Owner) && _memoryAttribution.IsCurrent(capture.Identity)
                && NativeBaptismCoroutineContract.BuffLifetimes.TryGetValue(capture.Source, out var current)
                && NativeBaptismCoroutineContract.CaptureLifetimes.TryGetValue(capture, out var captured)
                && ReferenceEquals(current.Host, this) && ReferenceEquals(captured.Host, this) && current.Serial == captured.Serial;

        internal void CaptureNativeBaptismEnding(Se_Star_Vesper_F_BS_ExplosionOnEnd star, EventInfoAbilityInstance info)
        {
            if (star == null || !(info.instance is Se_R_BaptismOfSun_Buff buff)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !Alive(star.hero)
                || !TryGetMemoryActivation(buff, out var identity) || identity.SourceMemory != nameof(St_R_BaptismOfSun)
                || identity.OwnerId != star.hero.GetInstanceID() || buff.info.caster != star.hero) return;
            _nativeBaptismEndings[(star, buff)] = identity;
        }

        internal NativeMemoryPayloadScope BeginNativeBaptismEnding(Se_Star_Vesper_F_BS_ExplosionOnEnd star, Actor buff)
        {
            if (star == null || ReferenceEquals(buff, null) || !_nativeBaptismEndings.TryGetValue((star, buff), out var original)) return null;
            _nativeBaptismEndings.Remove((star, buff));
            if (!Alive(star.hero) || AttributionGeneratedOrigin() != GeneratedOrigin.None || !_memoryAttribution.IsCurrent(original)
                || star.hero.GetInstanceID() != original.OwnerId) return null;
            return new NativeMemoryPayloadScope
            {
                Source = star, Owner = star.hero, Adapter = "native.baptism.ending", ChildType = typeof(Ai_R_BaptismOfSun),
                Identity = _memoryAttribution.DeriveNativePayload(original, "native.baptism.ending"), HasIdentity = true
            };
        }

        private void ClearNativeEndingActor(Actor actor)
        {
            var remove = new List<(Actor, Actor)>();
            foreach (var entry in _nativeBaptismEndings)
                if (ReferenceEquals(entry.Key.Star, actor) || ReferenceEquals(entry.Key.Buff, actor)) remove.Add(entry.Key);
            foreach (var key in remove) _nativeBaptismEndings.Remove(key);
        }

        private void ResetNativeEndingAdapters() => _nativeBaptismEndings.Clear();
    }
}
