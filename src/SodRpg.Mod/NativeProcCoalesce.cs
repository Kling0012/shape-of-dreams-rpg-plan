using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // 第3段: 同じフレーム・同じ生成元・同じ出発位置・同じ対象へ向かう同種の反応弾がしきい値を超えたとき、
    // 実体を1本にまとめる。命中時にまとめた分のダメージ通知を1件ずつ行うので、
    // 与えるダメージの合計、命中時効果の抽選と発動回数、状態異常の付与はまとめ前と同じである。
    // まとめはホスト側だけで行い、まとめ側の1本は本体の通常の同期でゲストへ届く。
    internal static class NativeProcCoalesce
    {
        // 同じキーで1フレームにこの数を超えた分から1本にまとめる。それ以前は1本ずつ作る。
        internal const int Threshold = 4;

        internal enum Role { Individual, Carrier, Appended }

        internal sealed class Group
        {
            internal int Count;
            internal AbilityInstance Carrier;
            internal Vector3 BaseScale;
            internal float Scale = 1f;
            internal List<Delegate> Payloads;
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly Type _type;
            private readonly int _creator, _source, _caster, _target;
            private readonly int _startX, _startY, _startZ;
            internal Key(Type type, int creator, int source, int caster, int target, int startX, int startY, int startZ)
            {
                _type = type; _creator = creator; _source = source; _caster = caster; _target = target;
                _startX = startX; _startY = startY; _startZ = startZ;
            }
            public bool Equals(Key other) => _type == other._type && _creator == other._creator
                && _source == other._source && _caster == other._caster && _target == other._target
                && _startX == other._startX && _startY == other._startY && _startZ == other._startZ;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode()
                => unchecked(((((((_creator * 397 ^ _source) * 397 ^ _caster) * 397 ^ _target)
                    * 397 ^ _startX) * 397 ^ _startY) * 397 ^ _startZ) ^ RuntimeHelpers.GetHashCode(_type));
        }

        private static readonly Dictionary<Key, int> Counts = new Dictionary<Key, int>(32);
        private static readonly Dictionary<Key, Group> Carriers = new Dictionary<Key, Group>(8);
        private static readonly ConditionalWeakTable<AbilityInstance, Group> Attached =
            new ConditionalWeakTable<AbilityInstance, Group>();
        private static readonly HashSet<Type> ReadyTypes = new HashSet<Type>();
        // 炭火弾の強さは本体で internal なので、読み込みだけフィールド参照を経由する。
        private static AccessTools.FieldRef<Ai_Gem_C_Charcoal_Projectile, float> _charcoalStrength;
        private static int _frame = -1;
        private static bool _warned;

        // 部屋ごとの診断（本体の開発者モードが有効なときだけログに出す）。
        internal static int RoomMerged;
        internal static int RoomPeakGroup;

        // 命中側の受け取り側（Drain パッチ）が形を確認できた種類だけをまとめ対象にする。
        // 確認できない種類はここが false のままなので、1本ずつの生成が保たれる。
        internal static bool SetReady(Type type)
        {
            try
            {
                if (type == typeof(Ai_Gem_C_Charcoal_Projectile))
                {
                    // 炭火弾の強さは本体で internal なので、読み込みだけフィールド参照を経由する。
                    _charcoalStrength = AccessTools.FieldRefAccess<Ai_Gem_C_Charcoal_Projectile, float>("_strength");
                    if (_charcoalStrength == null) return false;
                }
                ReadyTypes.Add(type);
                return true;
            }
            catch { return false; }
        }

        internal static void NotReady(Type type)
        {
            try { ReadyTypes.Remove(type); } catch { }
        }

        internal static Role Offer(Actor creator, Actor source, CastInfo info, Type type, Vector3? start,
            Delegate repeatApply, out Group group)
        {
            group = null;
            try
            {
                if (!NativePerfOptions.ProcCoalesce || !NetworkServer.active || !ReadyTypes.Contains(type)
                    || creator == null || info == null || info.caster == null || info.target == null)
                    return Role.Individual;
                if (!start.HasValue || !TryQuantize(start.Value.x, out int startX)
                    || !TryQuantize(start.Value.y, out int startY) || !TryQuantize(start.Value.z, out int startZ))
                {
                    WarnOnce("start position unavailable; individual creation retained.");
                    return Role.Individual;
                }
                if (_frame != Time.frameCount)
                {
                    Counts.Clear();
                    Carriers.Clear();
                    _frame = Time.frameCount;
                }
                var key = new Key(type, creator.GetInstanceID(), source == null ? 0 : source.GetInstanceID(),
                    info.caster.GetInstanceID(), info.target.GetInstanceID(), startX, startY, startZ);
                // しきい値までは数えるだけにして、普段の経路の確保を増やさない。
                Counts.TryGetValue(key, out int count);
                count++;
                Counts[key] = count;
                if (count <= Threshold) return Role.Individual;
                if (count == Threshold + 1)
                {
                    // しきい値を超えた最初の1本がまとめ側の実体になる。この分の通知は
                    // 実体の作成時設定から本体の OnEntity がそのまま行う。
                    group = new Group { Count = count };
                    Carriers[key] = group;
                    return Role.Carrier;
                }
                if (!Carriers.TryGetValue(key, out group))
                {
                    // 前後関係が崩れたときは、その分は1本ずつの作成に戻す。
                    return Role.Individual;
                }
                group.Count = count;
                // まとめ側の実体へ追加する。通知は命中時にこの delegate で設定し直してから行う。
                (group.Payloads ?? (group.Payloads = new List<Delegate>(8))).Add(repeatApply);
                RoomMerged++;
                if (count > RoomPeakGroup) RoomPeakGroup = count;
                if (group.Carrier != null) GrowCarrier(group);
                return Role.Appended;
            }
            catch (Exception ex)
            {
                NotReady(type);
                WarnOnce("coalescing unavailable; individual creation retained: " + ex.Message);
                return Role.Individual;
            }
        }

        // キーだけを1cm単位に丸める。実体に渡す開始位置は変えない。
        private static bool TryQuantize(float coordinate, out int quantized)
        {
            quantized = 0;
            double rounded = Math.Round((double)coordinate * 100d);
            if (double.IsNaN(rounded) || rounded < int.MinValue || rounded > int.MaxValue) return false;
            quantized = (int)rounded;
            return true;
        }

        internal static Action<T> Wrap<T>(Group group, Action<T> beforePrepare) where T : AbilityInstance
            => instance =>
            {
                beforePrepare?.Invoke(instance);
                Attach(group, instance);
            };

        private static void Attach(Group group, AbilityInstance carrier)
        {
            group.Carrier = carrier;
            Attached.Remove(carrier);
            Attached.Add(carrier, group);
            // 見た目の補い（最小限）: まとめ側は本数分少し大きく見せる。判定と数値には影響しない。
            try
            {
                group.BaseScale = carrier.transform.localScale;
                group.Scale = 1.15f;
                carrier.transform.localScale = group.BaseScale * group.Scale;
            }
            catch { }
        }

        private static void GrowCarrier(Group group)
        {
            try
            {
                if (group.Scale >= 1.75f) return;
                group.Scale = Mathf.Min(group.Scale * 1.04f, 1.75f);
                group.Carrier.transform.localScale = group.BaseScale * group.Scale;
            }
            catch { }
        }

        // 命中時にまとめ分を取り出す。取り出した時点で登録を外し、見た目の拡大を戻す。
        private static List<Delegate> Take(AbilityInstance instance)
        {
            if (!Attached.TryGetValue(instance, out var group)) return null;
            Attached.Remove(instance);
            RestoreScale(instance, group);
            group.Carrier = null;
            if (group.Payloads == null || group.Payloads.Count == 0) return null;
            return group.Payloads;
        }

        internal static void Discard(AbilityInstance instance)
        {
            if (!Attached.TryGetValue(instance, out var group)) return;
            Attached.Remove(instance);
            RestoreScale(instance, group);
            // 命中しなかったまとめ分。本体でも対象が消えた弾は通知しないため、捨てて同じである。
            group.Payloads?.Clear();
            group.Carrier = null;
        }

        private static void RestoreScale(AbilityInstance instance, Group group)
        {
            try { instance.transform.localScale = group.BaseScale; } catch { }
        }

        internal static void ResetRoom()
        {
            RoomMerged = 0;
            RoomPeakGroup = 0;
        }

        private static void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn("Proc coalescing: " + message);
        }

        // 以下、まとめ分の通知。本体の各 OnEntity が行う処理から、実体の破棄と
        // 命中演出の基本部（onEntity・effectOnEntity）を除いたものを1件ずつ行う。
        // 通知の回数が本体の本数と同じため、抽選・発動・状態異常の回数は変わらない。

        internal static void DrainRicochet(Ai_Gem_R_Ricochet instance, Projectile.EntityHit hit)
        {
            var payloads = Take(instance);
            if (payloads == null) return;
            try
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    ((Action<Ai_Gem_R_Ricochet>)payloads[i])?.Invoke(instance);
                    payloads[i] = null;
                    if (instance.isHeal) continue;
                    var origin = instance.originDamage;
                    var damage = instance.MagicDamage(
                        (origin.amount + origin.discardedAmount) * instance.GetValue(instance.ricochetRatio))
                        .SetSourceType(origin.type).SetAmountOrigin(origin);
                    if (origin.elemental.HasValue) damage = damage.SetElemental(origin.elemental.Value);
                    damage.Dispatch(hit.entity, instance.chain);
                }
            }
            catch (Exception ex)
            {
                NotReady(typeof(Ai_Gem_R_Ricochet));
                WarnOnce("ricochet drain failed; remaining payloads dropped like a missed native projectile: " + ex.Message);
            }
            finally { payloads.Clear(); }
        }

        internal static void DrainAccuracy(Ai_Gem_R_Accuracy_Projectile instance, Projectile.EntityHit hit)
        {
            var payloads = Take(instance);
            if (payloads == null) return;
            try
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    ((Action<Ai_Gem_R_Accuracy_Projectile>)payloads[i])?.Invoke(instance);
                    payloads[i] = null;
                    var data = instance.data;
                    float value = instance.GetValue(instance.damageRatio);
                    var damage = new DamageData(data.type,
                        (data.amount + data.discardedAmount) * value,
                        Mathf.Clamp01(data.procCoefficient * value));
                    if (data.HasAttr(DamageAttribute.IsCrit)) damage = damage.SetAttr(DamageAttribute.IsCrit);
                    if (data.HasAttr(DamageAttribute.AreaOfEffect)) damage = damage.SetAttr(DamageAttribute.AreaOfEffect);
                    if (data.HasAttr(DamageAttribute.DamageOverTime)) damage = damage.SetAttr(DamageAttribute.DamageOverTime);
                    if (data.elemental.HasValue) damage = damage.SetElemental(data.elemental.Value);
                    damage.SetActor(instance).SetDirection(instance.rotation).SetAmountOrigin(data)
                        .Dispatch(hit.entity, instance.chain);
                }
            }
            catch (Exception ex)
            {
                NotReady(typeof(Ai_Gem_R_Accuracy_Projectile));
                WarnOnce("accuracy drain failed; remaining payloads dropped like a missed native projectile: " + ex.Message);
            }
            finally { payloads.Clear(); }
        }

        internal static void DrainCharcoal(Ai_Gem_C_Charcoal_Projectile instance, Projectile.EntityHit hit)
        {
            var payloads = Take(instance);
            if (payloads == null || _charcoalStrength == null) return;
            try
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    ((Action<Ai_Gem_C_Charcoal_Projectile>)payloads[i])?.Invoke(instance);
                    payloads[i] = null;
                    float strength = _charcoalStrength == null ? 1f : _charcoalStrength(instance);
                    // 燃焼の有無は通知のたびに本体と同じように見る（途中で付く場合も同じ結果になる）。
                    if (hit.entity.Status.fireStack > 0)
                    {
                        instance.MagicDamage(instance.GetValue(instance.fireDamage) * strength,
                            instance.procCoefficient * strength)
                            .SetDirection(instance.rotation).SetElemental(ElementalType.Fire)
                            .SetAttr(DamageAttribute.ForceMergeNumber)
                            .Dispatch(hit.entity, instance.chain);
                        instance.FxPlayNewNetworked(instance.fireHitEffect, hit.entity);
                    }
                    else
                    {
                        instance.MagicDamage(instance.GetValue(instance.normalDamage),
                            instance.procCoefficient * strength)
                            .SetDirection(instance.rotation).SetAttr(DamageAttribute.ForceMergeNumber)
                            .Dispatch(hit.entity, instance.chain);
                    }
                }
            }
            catch (Exception ex)
            {
                NotReady(typeof(Ai_Gem_C_Charcoal_Projectile));
                WarnOnce("charcoal drain failed; remaining payloads dropped like a missed native projectile: " + ex.Message);
            }
            finally { payloads.Clear(); }
        }

        internal static void DrainSpiral(Ai_Gem_R_Spiral_Fireball instance, Projectile.EntityHit hit)
        {
            var payloads = Take(instance);
            if (payloads == null) return;
            try
            {
                // Spiral の弾は通知ごとの個別のデータを持たない。本数の分だけ通知を繰り返す。
                for (int i = 0; i < payloads.Count; i++)
                {
                    ((Action<Ai_Gem_R_Spiral_Fireball>)payloads[i])?.Invoke(instance);
                    payloads[i] = null;
                    instance.Damage(instance.damage, instance.procCoefficient)
                        .SetDirection(instance.rotation).SetElemental(ElementalType.Fire)
                        .Dispatch(hit.entity);
                }
            }
            catch (Exception ex)
            {
                NotReady(typeof(Ai_Gem_R_Spiral_Fireball));
                WarnOnce("spiral drain failed; remaining payloads dropped like a missed native projectile: " + ex.Message);
            }
            finally { payloads.Clear(); }
        }
    }

    // 命中側の受け取り。各 OnEntity の形を起動時に確認し、確認できない種類は
    // その種類だけまとめない（NativeProcCoalesce.SetReady が呼ばれないため）。
    [HarmonyPatch]
    internal static class NativeProcCoalesceRicochetDrain
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = AccessTools.DeclaredMethod(typeof(Ai_Gem_R_Ricochet), "OnEntity",
                new[] { typeof(Projectile.EntityHit) });
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "ricochet OnEntity");
                return false;
            }
            int dispatches = 0, magics = 0, values = 0;
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(_target))
            {
                if (!(instruction.operand is MethodInfo method)) continue;
                if (method.DeclaringType == typeof(DamageData) && method.Name == "Dispatch") dispatches++;
                if (method.DeclaringType == typeof(Actor) && method.Name == "MagicDamage") magics++;
                if (method.DeclaringType == typeof(AbilityInstance) && method.Name == "GetValue") values++;
            }
            // GetValue は回復とダメージの両枝に1回ずつある。
            if (dispatches != 1 || magics != 1 || values != 2)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "ricochet OnEntity shape changed");
                return false;
            }
            NativeProcCoalesce.SetReady(typeof(Ai_Gem_R_Ricochet));
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(Ai_Gem_R_Ricochet __instance, Projectile.EntityHit hit)
            => NativeProcCoalesce.DrainRicochet(__instance, hit);
    }

    [HarmonyPatch]
    internal static class NativeProcCoalesceAccuracyDrain
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = AccessTools.DeclaredMethod(typeof(Ai_Gem_R_Accuracy_Projectile), "OnEntity",
                new[] { typeof(Projectile.EntityHit) });
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "accuracy OnEntity");
                return false;
            }
            int dispatches = 0, ctors = 0, actors = 0, values = 0;
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(_target))
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(DamageData))
                {
                    if (method.Name == "Dispatch") dispatches++;
                    if (method.Name == "SetActor") actors++;
                }
                if (instruction.operand is ConstructorInfo ctor && ctor.DeclaringType == typeof(DamageData)
                    && ctor.GetParameters().Length == 3) ctors++;
                if (instruction.operand is MethodInfo value && value.DeclaringType == typeof(AbilityInstance)
                    && value.Name == "GetValue") values++;
            }
            if (dispatches != 1 || ctors != 1 || actors != 1 || values != 1)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "accuracy OnEntity shape changed");
                return false;
            }
            NativeProcCoalesce.SetReady(typeof(Ai_Gem_R_Accuracy_Projectile));
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(Ai_Gem_R_Accuracy_Projectile __instance, Projectile.EntityHit hit)
            => NativeProcCoalesce.DrainAccuracy(__instance, hit);
    }

    [HarmonyPatch]
    internal static class NativeProcCoalesceCharcoalDrain
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = AccessTools.DeclaredMethod(typeof(Ai_Gem_C_Charcoal_Projectile), "OnEntity",
                new[] { typeof(Projectile.EntityHit) });
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "charcoal OnEntity");
                return false;
            }
            int dispatches = 0, magics = 0, effects = 0, values = 0;
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(_target))
            {
                if (!(instruction.operand is MethodInfo method)) continue;
                if (method.DeclaringType == typeof(DamageData) && method.Name == "Dispatch") dispatches++;
                if (method.DeclaringType == typeof(Actor) && method.Name == "MagicDamage") magics++;
                if (method.DeclaringType == typeof(DewNetworkBehaviour) && method.Name == "FxPlayNewNetworked") effects++;
                if (method.DeclaringType == typeof(AbilityInstance) && method.Name == "GetValue") values++;
            }
            if (dispatches != 2 || magics != 2 || effects != 1 || values != 2)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "charcoal OnEntity shape changed");
                return false;
            }
            if (!NativeProcCoalesce.SetReady(typeof(Ai_Gem_C_Charcoal_Projectile)))
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "charcoal strength field unavailable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(Ai_Gem_C_Charcoal_Projectile __instance, Projectile.EntityHit hit)
            => NativeProcCoalesce.DrainCharcoal(__instance, hit);
    }

    [HarmonyPatch]
    internal static class NativeProcCoalesceSpiralDrain
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = AccessTools.DeclaredMethod(typeof(Ai_Gem_R_Spiral_Fireball), "OnEntity",
                new[] { typeof(Projectile.EntityHit) });
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "spiral OnEntity");
                return false;
            }
            int dispatches = 0, damages = 0;
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(_target))
            {
                if (!(instruction.operand is MethodInfo method)) continue;
                if (method.DeclaringType == typeof(DamageData) && method.Name == "Dispatch") dispatches++;
                if (method.DeclaringType == typeof(AbilityInstance) && method.Name == "Damage") damages++;
            }
            if (dispatches != 1 || damages != 1)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "spiral OnEntity shape changed");
                return false;
            }
            NativeProcCoalesce.SetReady(typeof(Ai_Gem_R_Spiral_Fireball));
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(Ai_Gem_R_Spiral_Fireball __instance, Projectile.EntityHit hit)
            => NativeProcCoalesce.DrainSpiral(__instance, hit);
    }

    // まとめ側の実体が命中以外で消えたとき（対象との途中の消滅など）に状態を捨てる。
    // 本体でもそのような弾は通知しないため、結果は同じである。
    [HarmonyPatch]
    internal static class NativeProcCoalesceDiscardPatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = AccessTools.DeclaredMethod(typeof(Projectile), "OnDisable");
            if (_target == null || _target.GetMethodBody() == null)
            {
                NativeProcCoalescePatch.WarnUnavailable(ref _warned, "projectile OnDisable");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => _target;
        private static void Postfix(Projectile __instance) => NativeProcCoalesce.Discard(__instance);
    }

    internal static class NativeProcCoalescePatch
    {
        internal static void WarnUnavailable(ref bool warned, string what)
        {
            if (warned) return;
            warned = true;
            Log.Warn("Proc coalescing unavailable, individual creation retained: " + what);
        }
    }
}
