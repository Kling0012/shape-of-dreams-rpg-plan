using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Replace the exact receiver call so C07 sees every ordinary processor, including flat additions,
    // and runs before FinalDamageData's armor/caps. Raw original-amount multiplication is incorrect here.
    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class NativeAuthoredKeystoneDamage
    {
        internal static bool Bound { get; private set; }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Bound = false;
            var native = AccessTools.Method(typeof(Entity), nameof(Entity.ProcessReceivedDamage), new[] { typeof(DamageData).MakeByRefType(), typeof(Actor) });
            var wrapper = AccessTools.Method(typeof(NativeAuthoredKeystoneDamage), nameof(ProcessReceived));
            var result = new List<CodeInstruction>(); int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = wrapper; count++; }
                result.Add(instruction);
            }
            if (count != 1) throw new InvalidOperationException("C07 final native damage callsite is unavailable.");
            Bound = true; return result;
        }
        private static void ProcessReceived(Entity target, ref DamageData damage, Actor actor)
        {
            target.ProcessReceivedDamage(ref damage, actor);
            if (NetworkServer.active) HostAuthority.NativeInstance?.ApplyAuthoredFinalNativeDamage(ref damage, actor, target);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class AuthoredKeystoneBinding
        {
            internal Build Build;
            internal ScopedKeystoneModifiers Runtime;
            internal long NativeEpoch = -1;
            internal long Epoch;
            internal string Key;
            internal MechanismEquipment Equipment;
            internal string Admission;
            internal int HookSignature = -1;
            internal bool HasSacrificePayload;
        }
        private readonly Dictionary<Hero, AuthoredKeystoneBinding> _authoredKeystones = new Dictionary<Hero, AuthoredKeystoneBinding>();
        private long _authoredKeystoneEpoch;

        internal void ConfigureAuthoredKeystone(Hero hero, Build build)
        {
            if (hero == null || build == null) throw new ArgumentNullException();
            string signature = build.SelectedKeystones.Count == 0 ? null
                : string.Join("|", build.SelectedKeystones.Select(AuthoredKeystoneCodec.Encode));
            if (_authoredKeystones.TryGetValue(hero, out var existing) && existing.Key == signature)
            { existing.Build = build; RefreshAuthoredKeystone(hero); return; }
            StopSacrificeShield(hero);
            if (existing != null || signature != null) ClearAuthoredGimmickPrimed(hero);
            if (build.SelectedKeystones.Count == 0) { _authoredKeystones.Remove(hero); return; }
            var binding = new AuthoredKeystoneBinding { Build = build, Key = signature,
                Runtime = new ScopedKeystoneModifiers(build.SelectedKeystones), Epoch = checked(++_authoredKeystoneEpoch) };
            foreach (var key in build.SelectedKeystones)
                foreach (var payload in key.Payloads)
                    if (payload == KeystonePayloadKind.SacrificeShield) binding.HasSacrificePayload = true;
            _authoredKeystones[hero] = binding;
            RefreshAuthoredKeystone(hero);
        }

        internal void ClearAuthoredKeystone(Hero hero)
        {
            if (ReferenceEquals(hero, null)) return;
            StopSacrificeShield(hero);
            ClearAuthoredGimmickPrimed(hero);
            _authoredKeystones.Remove(hero);
        }

        private void ClearAuthoredGimmickPrimed(Hero hero)
        {
            if (_runtimes.TryGetValue(hero, out var owner)) owner.Powers.ClearGimmickPrimed();
        }

        internal void RefreshAuthoredKeystone(Hero hero)
        {
            if (hero == null || !_authoredKeystones.TryGetValue(hero, out var binding)) return;
            long nativeEpoch = EnsureMemoryAttributionEquipment(hero);
            var keys = binding.Build.SelectedKeystones;
            int hooks = binding.HasSacrificePayload
                ? (NativeAuthoredKeystoneDamage.Bound ? 1 : 0) | (NativeSacrificeShieldDispatch.GoldenBound ? 2 : 0)
                    | (NativeSacrificeShieldDispatch.ReductionBound ? 4 : 0) : 0;
            if (binding.NativeEpoch == nativeEpoch && binding.HookSignature == hooks) return;
            if (binding.NativeEpoch >= 0) ClearAuthoredGimmickPrimed(hero);
            binding.HookSignature = hooks;
            var equipment = binding.NativeEpoch == nativeEpoch && binding.Equipment != null
                ? binding.Equipment : CollectMechanismEquipment(hero, hero.GetInstanceID());
            // 入手確認（ネイティブアダプター）は刻印ごとに判定する。未承認の刻印だけ止めても、他は動かす。
            // #49: 比較は刻印ごとの許可ベクトルで行う。全体のAny(true)が同じでも個別許可の変化を再設定から漏らさない。
            var admission = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var key in keys) admission[key.KeystoneId] = SacrificeBindingAvailable(key, equipment);
            string admitted = string.Join("|", keys.Select(key => admission[key.KeystoneId] ? "1" : "0"));
            if (binding.NativeEpoch == nativeEpoch && binding.Admission == admitted) return;
            binding.NativeEpoch = nativeEpoch; binding.Admission = admitted;
            binding.Epoch = checked(++_authoredKeystoneEpoch); binding.Equipment = equipment;
            var prerequisites = keys.SelectMany(key => key.Prerequisites).Distinct(StringComparer.Ordinal);
            binding.Runtime.Configure(keys.Select(key => key.KeystoneId), binding.Epoch, equipment.Memories.Select(m => m.Memory),
                prerequisites, true, admission);
            foreach (var key in keys)
                if (key.Payloads.Contains(KeystonePayloadKind.SacrificeShield))
                {
                    if (!binding.Runtime.HasPayload(KeystonePayloadKind.SacrificeShield)) StopSacrificeShield(hero);
                    else if (_runtimes.ContainsKey(hero)) BindAuthoredSacrificeShield(hero);
                }
        }

        internal void ValidateAuthoredSacrificeBinding(Hero hero, Build build)
        {
            var keys = build?.SelectedKeystones.Where(key => key.Payloads.Contains(KeystonePayloadKind.SacrificeShield));
            if (keys == null) return;
            foreach (var key in keys)
            {
                if (key.KeystoneId != "h.aurena.key2") throw new InvalidOperationException("C11 requires its named authored keystone.");
                var equipment = CollectMechanismEquipment(hero, hero.GetInstanceID());
                foreach (string required in key.RequiredMemories) if (equipment.Find(required) == null) return;
                // Missing verified host adapters disable the whole key through Configure, never just its shield half.
            }
        }

        private static bool IsSacrificeMemory(string memory) => memory == "St_Q_GoldenBurst" || memory == "St_Q_Reduction";

        private static bool SacrificeBindingAvailable(KeystoneDefinition key, MechanismEquipment equipment)
        {
            if (!key.Payloads.Contains(KeystonePayloadKind.SacrificeShield)) return true;
            if (key.KeystoneId != "h.aurena.key2" || !NativeAuthoredKeystoneDamage.Bound) return false;
            bool source = false;
            foreach (var memory in equipment.Memories)
            {
                if (!IsSacrificeMemory(memory.Memory)) continue;
                source = true;
                if (!NativeSacrificeShieldDispatch.IsBoundFor(memory.Memory)) return false;
            }
            return source;
        }

        /// <summary>指定刻印が「許可済み・必要装備あり」を満たすか（#49: 全体Activeではなく刻印ごとに判定）。</summary>
        internal bool AuthoredKeystoneActive(Hero hero, string keystoneId)
        {
            RefreshAuthoredKeystone(hero);
            return hero != null && keystoneId != null && _authoredKeystones.TryGetValue(hero, out var binding)
                && binding.Runtime.IsKeystoneActive(keystoneId);
        }

        internal long AuthoredKeystoneEpoch(Hero hero)
        {
            RefreshAuthoredKeystone(hero);
            return hero != null && _authoredKeystones.TryGetValue(hero, out var binding) ? binding.Epoch : 0;
        }

        internal void BindAuthoredSacrificeShield(Hero hero)
        {
            RefreshAuthoredKeystone(hero);
            if (!_authoredKeystones.TryGetValue(hero, out var binding) || !binding.Runtime.HasPayload(KeystonePayloadKind.SacrificeShield))
            { StopSacrificeShield(hero); return; }
            if (_sacrificeBindings.TryGetValue(hero, out var existing) && ReferenceEquals(existing.Keystone, binding.Runtime)) return;
            bool Verified()
            {
                // #49: 犠牲シールドの接続確認も、その刻印自身の許可・装備（HasPayload）で判定する。
                RefreshAuthoredKeystone(hero);
                return _authoredKeystones.TryGetValue(hero, out var current) && ReferenceEquals(current, binding)
                    && binding.Runtime.HasPayload(KeystonePayloadKind.SacrificeShield);
            }
            BindSacrificeShield(hero, binding.Runtime, Verified);
        }

        internal KeystoneResult TransformAuthoredPayload(Hero hero, KeystonePayload payload, string source, string receiver,
            KeystoneSourceKind sourceKind, KeystoneRecipientKind recipient = KeystoneRecipientKind.Self)
        {
            RefreshAuthoredKeystone(hero);
            var result = !_authoredKeystones.TryGetValue(hero, out var binding) || !binding.Runtime.Active
                ? ScopedKeystoneModifiers.ApplyUnmodified(payload)
                : binding.Runtime.Apply(payload, new KeystoneContext(binding.Epoch, source, sourceKind, receiver, recipient,
                    binding.Equipment));
            return StarDamageScaling.ScaleResult(binding?.Build
                ?? (hero != null && _runtimes.TryGetValue(hero, out var owner) ? owner.Powers.Build : null), payload, result);
        }

        internal float TransformAuthoredMemoryDamage(Hero hero, string memory, float percent)
        {
            if (percent <= 0 || memory == null) return percent;
            var build = hero != null && _runtimes.TryGetValue(hero, out var owner) ? owner.Powers.Build : null;
            decimal basis = (decimal)percent / StarDamageScaling.Multiplier(build?.SpentStarPoints ?? 0);
            return (float)TransformAuthoredPayload(hero, new KeystonePayload(KeystoneLayer.StarMemoryDamage, basis,
                new KeystoneCaps(decimal.MaxValue)), memory, null, KeystoneSourceKind.NativeMemory).Value;
        }

        internal void ApplyAuthoredFinalNativeDamage(ref DamageData damage, Actor actor, Entity target)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || packet.Actor != actor || packet.Victim != target || !packet.Admitted
                || damage.currentAmount <= 0 || damage.IsAmountModifiedBy(typeof(NativeAuthoredKeystoneDamage))) return;
            var hero = AttributedOwner(packet.Identity.OwnerId);
            if (hero == null || !_authoredKeystones.ContainsKey(hero)) return;
            var identity = packet.Identity;
            var sourceKind = identity.NativePayloadKind == NativePayloadKind.MainBasicAttack ? KeystoneSourceKind.OwnedBasicAttack
                : identity.NativePayloadKind == NativePayloadKind.SummonAttack ? KeystoneSourceKind.OwnedSummon : KeystoneSourceKind.NativeMemory;
            string effectId = identity.NativeAdapterId;
            var family = NativeAuthoredKeystonePacketFamily.Current;
            if (family != null && family.Depth == 1 && family.Actor == actor && family.Victim == target
                && family.Memory == identity.SourceMemory) effectId = family.EffectId;
            var payload = new KeystonePayload(KeystoneLayer.NativeDamage, (decimal)damage.currentAmount,
                new KeystoneCaps(decimal.MaxValue), effectId: effectId);
            var result = TransformAuthoredPayload(hero, payload, identity.SourceMemory.Length == 0 ? null : identity.SourceMemory, null, sourceKind);
            float ratio = (float)(result.Value / payload.Value);
            if (ratio < 1f) damage.ApplyReduction(1f - ratio);
            else if (ratio > 1f) damage.ApplyAmplification(ratio - 1f);
            damage.SetAmountModifiedBy(typeof(NativeAuthoredKeystoneDamage));
        }

        internal GimmickDef TransformAuthoredGimmick(Hero hero, GimmickDef def, string source, string receiver,
            KeystoneSourceKind sourceKind, string effectId = null, float sourceCooldown = 0f)
        {
            return TransformAuthoredGimmickPayload(hero, def, AuthoredKeystoneComposer.GimmickPayload(def, effectId,
                durationBaseOverride: AuthoredGimmickDurationBase(def, sourceCooldown)),
                source, receiver, sourceKind);
        }

        internal GimmickDef TransformAuthoredGimmick(Hero hero, AuthoredMechanismSpec spec, string source, string receiver,
            KeystoneSourceKind sourceKind, float sourceCooldown = 0f)
        {
            if (spec?.Gimmick == null) throw new ArgumentException("A concrete gimmick mechanism is required.");
            return TransformAuthoredGimmickPayload(hero, spec.Gimmick, AuthoredKeystoneComposer.MechanismPayload(spec,
                durationBaseOverride: AuthoredGimmickDurationBase(spec.Gimmick, sourceCooldown)),
                source, receiver, sourceKind);
        }

        private static decimal? AuthoredGimmickDurationBase(GimmickDef def, float sourceCooldown) =>
            def.Effect == GimmickEffect.Crescendo
                ? (decimal)Math.Max(8f, Gimmicks.Finite(sourceCooldown) && sourceCooldown > 0 ? sourceCooldown * 1.5f : 0f)
                : (decimal?)null;

        private GimmickDef TransformAuthoredGimmickPayload(Hero hero, GimmickDef def, KeystonePayload payload,
            string source, string receiver, KeystoneSourceKind sourceKind)
        {
            var result = TransformAuthoredPayload(hero, payload, source, receiver, sourceKind);
            var effective = AuthoredKeystoneComposer.EffectiveGimmick(def, result);
            if (effective != null && (def.Effect == GimmickEffect.Heal || def.Effect == GimmickEffect.Siphon))
                effective.EffectiveAllyValuePercent = TransformAuthoredPayload(hero, payload, source, receiver, sourceKind,
                    KeystoneRecipientKind.AlliedHero).Value;
            return effective;
        }

        internal int TransformAuthoredEveryN(Hero hero, string source, AuthoredMechanismSpec spec,
            KeystoneSourceKind sourceKind = KeystoneSourceKind.NativeMemory)
        {
            return TransformAuthoredPayload(hero, AuthoredKeystoneComposer.MechanismPayload(spec),
                source, spec.Recharge?.Recipient.Memory ?? spec.Relay?.TargetMemory ?? source, sourceKind).EveryN;
        }

        internal float TransformAuthoredGeneratedDamage(Hero hero, float amount, string source, string effectId, GimmickEffect effect)
        {
            if (amount <= 0 || !_authoredKeystones.ContainsKey(hero)) return amount;
            return (float)TransformAuthoredPayload(hero, new KeystonePayload(KeystoneLayer.GeneratedDamage, (decimal)amount,
                new KeystoneCaps(decimal.MaxValue), effect: effect, effectId: effectId), source, null, KeystoneSourceKind.Generated).Value;
        }
    }
}
