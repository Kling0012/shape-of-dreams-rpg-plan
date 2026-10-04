using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Only the verified native self-sacrifice Dispatch call in each iterator is replaced.
    // Enemy damage, HealthCost, the rest of the skill, and the original dispatch stay native.
    [HarmonyPatch]
    internal static class NativeSacrificeShieldDispatch
    {
        private static readonly MethodInfo Dispatch = AccessTools.Method(typeof(DamageData), nameof(DamageData.Dispatch));
        private static readonly MethodInfo PureDamage = AccessTools.Method(typeof(Actor), nameof(Actor.PureDamage), new[] { typeof(float), typeof(float) });
        private static readonly MethodInfo SetAttr = AccessTools.Method(typeof(DamageData), nameof(DamageData.SetAttr));
        private static readonly MethodInfo Wrapper = AccessTools.Method(typeof(NativeSacrificeShieldDispatch), nameof(DispatchPayment));
        internal static bool GoldenBound { get; private set; }
        internal static bool ReductionBound { get; private set; }
        internal static bool IsBoundFor(string memory) => memory == "St_Q_GoldenBurst" ? GoldenBound
            : memory == "St_Q_Reduction" && ReductionBound;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return NativeIterator(typeof(Ai_Q_GoldenBurst));
            yield return NativeIterator(typeof(Ai_Q_Reduction_Spawner));
        }

        internal static MethodInfo NativeIterator(Type type)
        {
            var method = AccessTools.DeclaredMethod(type, "OnCreateSequenced");
            var iterator = method?.GetCustomAttribute<IteratorStateMachineAttribute>();
            var moveNext = iterator == null ? null : AccessTools.DeclaredMethod(iterator.StateMachineType, "MoveNext");
            if (moveNext == null) throw new InvalidOperationException("Verified sacrifice iterator is unavailable: " + type.Name);
            return moveNext;
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var code = new List<CodeInstruction>(instructions);
            bool golden = original == NativeIterator(typeof(Ai_Q_GoldenBurst));
            if (!golden && original != NativeIterator(typeof(Ai_Q_Reduction_Spawner)))
                throw new InvalidOperationException("Unverified sacrifice method.");
            if (golden) GoldenBound = false; else ReductionBound = false;
            int pureIndex = -1, pureCount = 0, dispatchIndex = -1, dispatchCount = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(PureDamage)) { pureIndex = i; pureCount++; }
                if (code[i].Calls(Dispatch)) { if (dispatchIndex < 0) dispatchIndex = i; dispatchCount++; }
            }
            if (pureCount != 1 || dispatchCount != (golden ? 2 : 1) || pureIndex >= dispatchIndex)
                throw new InvalidOperationException("Native sacrifice callsite changed; no C11 patch was applied.");
            var attributes = new List<int>();
            bool casterTarget = false;
            for (int i = pureIndex + 1; i < dispatchIndex; i++)
            {
                if (code[i].Calls(SetAttr))
                {
                    if (i < 2 || code[i - 1].opcode != OpCodes.Conv_I8)
                        throw new InvalidOperationException("Unverified sacrifice attribute encoding.");
                    var constant = code[i - 2].opcode;
                    attributes.Add(constant == OpCodes.Ldc_I4_2 ? 2 : constant == OpCodes.Ldc_I4_4 ? 4 : constant == OpCodes.Ldc_I4_8 ? 8 : -1);
                }
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo field
                    && field.DeclaringType == typeof(CastInfo) && field.Name == nameof(CastInfo.caster)) casterTarget = true;
            }
            if (!casterTarget || (golden ? attributes.Count != 1 || attributes[0] != 4
                : attributes.Count != 3 || attributes[0] != 2 || attributes[1] != 4 || attributes[2] != 8))
                throw new InvalidOperationException("Native sacrifice target or attributes changed; no C11 patch was applied.");
            // Instance struct call consumes DamageData&, Entity, ReactionChain; the static wrapper has exactly that signature.
            code[dispatchIndex].opcode = OpCodes.Call;
            code[dispatchIndex].operand = Wrapper;
            if (golden) GoldenBound = true; else ReductionBound = true;
            return code;
        }

        private static void DispatchPayment(ref DamageData damage, Entity victim, ReactionChain chain)
        {
            var host = NetworkServer.active ? HostAuthority.NativeInstance : null;
            var hero = victim as Hero;
            SacrificeShieldRuntime.Capture capture = null;
            try
            {
                var native = damage.actor as AbilityInstance;
                SacrificeShieldSource source;
                if (native != null && native.GetType() == typeof(Ai_Q_GoldenBurst)) source = SacrificeShieldSource.GoldenBurst;
                else if (native != null && native.GetType() == typeof(Ai_Q_Reduction_Spawner)) source = SacrificeShieldSource.Reduction;
                else throw new InvalidOperationException("Unexpected actor at the verified sacrifice callsite.");
                if (!ReferenceEquals(native.info.caster, victim))
                    throw new InvalidOperationException("Native sacrifice dispatch no longer targets its caster.");
                capture = host?.BeginSacrificeShield(native, hero, source);
            }
            catch (Exception ex) { Stop(host, hero, ex); }
            try
            {
                // C11 failures stop the MOD item explicitly, but never cancel or repeat this native payment.
                damage.Dispatch(victim, chain);
                if (capture != null)
                    try { host.CompleteSacrificeShield(capture, victim); }
                    catch (Exception ex) { Stop(host, hero, ex); }
            }
            finally
            {
                if (capture != null)
                    try { host.AbortSacrificeShield(capture); }
                    catch (Exception ex) { Stop(host, hero, ex); }
            }
        }

        private static void Stop(HostAuthority host, Hero hero, Exception error)
        {
            host?.StopSacrificeShield(hero);
            Log.Error("C11 SacrificeShield stopped; native sacrifice remains unchanged. " + error);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class SacrificeBinding
        {
            public HeroRuntime Owner;
            public ScopedKeystoneModifiers Keystone;
            public Func<bool> Verified;
            public long KeystoneEpoch = -1, ShieldEpoch = -1, CaptureEpoch;
        }

        private readonly SacrificeShieldRuntime _sacrificeShields = new SacrificeShieldRuntime();
        private readonly Dictionary<Hero, SacrificeBinding> _sacrificeBindings = new Dictionary<Hero, SacrificeBinding>();
        private readonly List<SacrificeBinding> _sacrificeBindingScratch = new List<SacrificeBinding>();
        private long _sacrificeEpoch;

        // C02 integration installs the exact native self-sacrifice adapters and provides their live verification.
        // There are deliberately no default registrations, and a missing counterpart rejects binding.
        internal void BindSacrificeShield(Hero hero, ScopedKeystoneModifiers keystone, Func<bool> verified)
        {
            if (hero == null || keystone == null) throw new ArgumentNullException(hero == null ? nameof(hero) : nameof(keystone));
            if (verified == null || !verified())
                throw new InvalidOperationException("C11 requires its verified host adapter binding.");
            if (!_runtimes.TryGetValue(hero, out var owner)) throw new InvalidOperationException("Sacrifice shield owner is not attached.");
            var binding = new SacrificeBinding { Owner = owner, Keystone = keystone, Verified = verified };
            _sacrificeShields.RemoveOwner(hero.GetInstanceID());
            _sacrificeBindings.Remove(hero);
            RefreshSacrificeBinding(binding);
            _sacrificeBindings.Add(hero, binding);
        }

        private void RefreshSacrificeBinding(SacrificeBinding binding)
        {
            long nativeEpoch = ModShieldEquipmentEpoch(binding.Owner);
            if (binding.KeystoneEpoch != binding.Keystone.EquipmentEpoch || binding.ShieldEpoch != nativeEpoch)
            {
                binding.KeystoneEpoch = binding.Keystone.EquipmentEpoch;
                binding.ShieldEpoch = nativeEpoch;
                binding.CaptureEpoch = checked(++_sacrificeEpoch);
            }
            bool enabled = Alive(binding.Owner.Hero) && binding.Keystone.HasPayload(KeystonePayloadKind.SacrificeShield);
            if (enabled && !binding.Verified())
                throw new InvalidOperationException("C11 host adapter binding was lost while its shield remains selected.");
            if (enabled)
                foreach (var definition in binding.Keystone.SelectedDefinitions)
                    foreach (string memory in definition.RequiredMemories)
                        if (FindMemory(binding.Owner.Hero, memory) == null) { enabled = false; break; }
            _sacrificeShields.Configure(binding.Owner.Hero.GetInstanceID(), binding.CaptureEpoch, enabled);
        }

        internal SacrificeShieldRuntime.Capture BeginSacrificeShield(AbilityInstance native, Hero hero, SacrificeShieldSource source)
        {
            if (hero == null || !_sacrificeBindings.TryGetValue(hero, out var binding)) return null;
            RefreshSacrificeBinding(binding);
            string memory = source == SacrificeShieldSource.GoldenBurst ? "St_Q_GoldenBurst" : "St_Q_Reduction";
            var equipped = FindMemory(hero, memory);
            if (equipped == null || !ReferenceEquals(native.firstTrigger, equipped)) return null;
            return _sacrificeShields.Begin(hero.GetInstanceID(), source, native.GetInstanceID(), binding.CaptureEpoch,
                hero.currentHealth, hero.maxHealth);
        }

        internal void CompleteSacrificeShield(SacrificeShieldRuntime.Capture capture, Entity victim) =>
            _sacrificeShields.Complete(capture, victim.currentHealth, victim.maxHealth);
        internal void AbortSacrificeShield(SacrificeShieldRuntime.Capture capture) => _sacrificeShields.Abort(capture);

        private void UpdateSacrificeShields()
        {
            // #55: this runs every host tick; reuse the scratch instead of allocating the binding list per frame.
            if (_sacrificeBindings.Count > 0)
            {
                _sacrificeBindingScratch.Clear();
                _sacrificeBindingScratch.AddRange(_sacrificeBindings.Values);
                foreach (var binding in _sacrificeBindingScratch)
                    try { RefreshSacrificeBinding(binding); }
                    catch (Exception ex)
                    {
                        StopSacrificeShield(binding.Owner.Hero);
                        Log.Error("C11 SacrificeShield stopped; native sacrifice remains unchanged. " + ex);
                    }
            }
            foreach (var award in _sacrificeShields.TakePendingForHostUpdate())
            {
                SacrificeBinding binding = null;
                foreach (var candidate in _sacrificeBindings.Values)
                    if (candidate.Owner.Hero.GetInstanceID() == award.OwnerId) { binding = candidate; break; }
                if (binding == null) throw new InvalidOperationException("C11 award has no current host binding.");
                try
                {
                    float amount = SupportStats.AmplifyShield(award.RawAmount, binding.Owner.Powers.Build.Get(Stat.ShieldPower));
                    AwardModShield(binding.Owner, binding.Owner.Hero, ModShieldPoolKind.Ordinary, amount,
                        award.DurationSeconds, award.SourceMemory, binding.ShieldEpoch, award.NewAwardCapRatio);
                }
                catch (Exception ex)
                {
                    StopSacrificeShield(binding.Owner.Hero);
                    Log.Error("C11 SacrificeShield stopped; native sacrifice remains unchanged. " + ex);
                }
            }
        }

        internal void StopSacrificeShield(Hero hero)
        {
            if (ReferenceEquals(hero, null)) return;
            _sacrificeShields.RemoveOwner(hero.GetInstanceID());
            _sacrificeBindings.Remove(hero);
        }

        private void ClearSacrificeShields(HeroRuntime owner = null)
        {
            // A zone transition clears transient captures/awards. Attached authored bindings survive;
            // detaching a hero removes its binding through the owner-specific path below.
            if (owner == null) { _sacrificeShields.Clear(); return; }
            if (owner.Hero == null) return;
            _sacrificeShields.RemoveOwner(owner.Hero.GetInstanceID());
            _sacrificeBindings.Remove(owner.Hero);
        }
    }
}
