using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        public static readonly FieldInfo Source = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.Feather));
        public static readonly FieldInfo Effect = typeof(CapturedNativeState).GetField(nameof(CapturedNativeState.Effect));
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
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerator OnCreateSequenced()
        {
            yield return new SI.WaitForSeconds(1);
            yield return new SI.WaitForSeconds(2);
        }
    }
}
namespace SI
{
    public sealed class WaitForSeconds { public WaitForSeconds(float seconds) { } }
}
