using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Only game API dependencies are doubled. Instruction decoding, composition,
// class discovery, patch ownership and installation use the real Harmony library.
namespace SodRpg.Mod
{
    public sealed class Se_D_BeautifulThreat { }
    public sealed class EventInfoAttackEffect { }
    public sealed class Se_R_BaptismOfSun_Buff { }
    public sealed class Se_D_DoubleTap { }
    public sealed class EventInfoAttackFired { }
    internal sealed class CapturedNativeState
    {
        public Se_D_BeautifulThreat Feather;
        public EventInfoAttackEffect Effect;
        public Se_R_BaptismOfSun_Buff Buff;
        public CapturedNativeState Closure;
        public Se_D_DoubleTap DoubleTap;
        public EventInfoAttackFired Fired;
    }
    internal static class NativeFeatherDelayedContract
    {
        // Source is assignable so a test can model a captured-field contract that fails its check.
        public static FieldInfo Source = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.Feather));
        public static readonly FieldInfo Effect = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.Effect));
        internal static void Restore() => Source = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.Feather));
    }
    internal static class NativeBaptismCoroutineContract
    {
        public static readonly Type Closure = typeof(CapturedNativeState);
        public static readonly FieldInfo Buff = Closure.GetField(nameof(CapturedNativeState.Buff));
        public static readonly FieldInfo Effect = Closure.GetField(nameof(CapturedNativeState.Effect));
        public static readonly FieldInfo IteratorClosure = Closure.GetField(nameof(CapturedNativeState.Closure));
    }
    internal static class NativeDoubleTapCoroutineContract
    {
        public static readonly FieldInfo Source = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.DoubleTap));
        public static readonly FieldInfo Fired = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.Fired));
    }
    internal static class BossDisplacementReuse { public static void Prewarm() { } }
    internal static class BossBasicEffectReuse { public static void Prewarm() { } }
    internal static class NativeAttributedHpDamage { public static void Prewarm() { } }
    internal static class NativeModShieldCreationCap { public static void Prewarm() { } }
    internal static class SeekerSoulShieldCreation { public static readonly bool Prepare = true; }
    public sealed class Se_U_HerWorld_Blackhole { }
    public sealed class Ai_U_BeamOfBalance_Beam { }
    public struct DamageData { public void Dispatch() { } }
    public struct HealData { public void Dispatch() { } }
    public sealed class Ai_Gem_U_LastStarlight
    {
        // The native sequence's final completion step, observed to run as-is when unpatched.
        public static int Completions;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerator OnCreateSequenced()
        {
            yield return new SI.WaitForSeconds(1);
            yield return new SI.WaitForSeconds(2);
            Completions++;
        }
    }
    // Doubles of the mod's patch classes whose full names NativePatchPreflight maps to native features.
    // They only record registration and the wait adaptation; game-only doubles do not implement patch behavior.
    internal static class NativeFeatureTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)] internal static int Feather() => 11;
        [MethodImpl(MethodImplOptions.NoInlining)] internal static int Baptism() => 22;
    }
    [HarmonyPatch(typeof(NativeFeatureTarget), nameof(NativeFeatureTarget.Feather))]
    internal static class NativeFeatherLifetime
    {
        [HarmonyPostfix] internal static void Postfix(ref int __result) => __result += 1;
    }
    [HarmonyPatch(typeof(NativeFeatureTarget), nameof(NativeFeatureTarget.Feather))]
    internal static class NativeFeatherDispatch
    {
        [HarmonyPostfix] internal static void Postfix(ref int __result) => __result += 2;
    }
    [HarmonyPatch(typeof(NativeFeatureTarget), nameof(NativeFeatureTarget.Baptism))]
    internal static class NativeBaptismCoroutineCapture
    {
        [HarmonyPostfix] internal static void Postfix(ref int __result) => __result += 4;
    }
    [HarmonyPatch(typeof(Ai_Gem_U_LastStarlight), "OnCreateSequenced")]
    internal static class ErebosLastStarlightSequence
    {
        internal static int Captures, WaitAdaptations;
        private static void Prefix(out object __state) { Captures++; __state = new object(); }
        private static void Postfix(ref IEnumerator __result, object __state)
        { if (__state != null) __result = new LastStarlightWaitAdapter(__result); }
    }
    // Double of the production wait adapter: it replaces the native sequence's two waits and
    // passes every other yield of the native iterator through unchanged.
    internal sealed class LastStarlightWaitAdapter : IEnumerator
    {
        private readonly IEnumerator _native;
        private int _waits;
        internal LastStarlightWaitAdapter(IEnumerator native) { _native = native; }
        public object Current { get; private set; }
        public bool MoveNext()
        {
            if (!_native.MoveNext()) { Current = null; return false; }
            if (_native.Current is SI.WaitForSeconds && ++_waits <= 2)
            { ErebosLastStarlightSequence.WaitAdaptations++; Current = new SI.WaitForCondition(() => true); return true; }
            Current = _native.Current;
            return true;
        }
        public void Reset() => throw new NotSupportedException();
    }
}
namespace SI
{
    public sealed class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public sealed class WaitForCondition { public WaitForCondition(Func<bool> predicate) { } }
}
