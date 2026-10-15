using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// 本体 AbilityTrigger のクールダウン面の境界スタブ（2026-10-16 の ilspycmd 逆コンパイル結果に基づく）。
// 実 Harmony デトゥアと実 NativeCooldownSafety に対して、本体の固定秒短縮・割合短縮・時間更新・RPC 送出の
// 数式と NetworkServer の前提だけを再現する。ゲーム専用の振る舞い（ロック・UI・同期下請け）は持たない。
namespace SodRpg.Mod
{
    public class TriggerConfig
    {
        public bool canReceiveCooldownReduction = true;
        public int maxCharges = 1;
        public int addedCharges = 1;
        public float maxCooldownTime = 10f;
    }

    public class AbilityTrigger
    {
        public float[] currentUnscaledCooldownTimes = { 10f };
        public int[] currentCharges = { 0 };
        public TriggerConfig[] configs = { new TriggerConfig() };
        // 本体の abilityHaste 系の時間倍率に相当する GetCooldownTimeMultiplier の戻り値源。
        public float multiplier = 1f;
        // 観測: 短縮・時間更新の本体ループが実際に使った倍率と、本体 RPC メソッドの送出順。
        public readonly List<float> UsedMultipliers = new List<float>();
        public readonly List<string> RpcLog = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual float GetCooldownTimeMultiplier(int configIndex) => multiplier;

        public float GetMaxCooldownTime(int configIndex) => configs[configIndex].maxCooldownTime;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void RpcSetCooldownTime(int configIndex, float time)
            => RpcLog.Add("time " + configIndex + " " + time.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void RpcSetCharge(int configIndex, int charge)
            => RpcLog.Add("charge " + configIndex + " " + charge);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void ApplyCooldownReductionWithMinimum_Imp(float amount, float minThreshold, bool scaled = true, bool ignoreCanReceiveCooldown = false)
        {
            if (!Mirror.NetworkServer.active) return; // 本体は [Server] 違反の警告だけ出して戻る
            for (int i = 0; i < configs.Length; i++)
            {
                if (!configs[i].canReceiveCooldownReduction && !ignoreCanReceiveCooldown) continue;
                float min = minThreshold, remaining = amount;
                if (scaled)
                {
                    float cooldownTimeMultiplier = GetCooldownTimeMultiplier(i);
                    UsedMultipliers.Add(cooldownTimeMultiplier);
                    min /= cooldownTimeMultiplier;
                    remaining /= cooldownTimeMultiplier;
                }
                if ((currentCharges[i] == 0 && currentUnscaledCooldownTimes[i] <= min) || currentCharges[i] >= configs[i].maxCharges)
                    continue;
                while (remaining > 0f && currentCharges[i] < configs[i].maxCharges)
                {
                    if (currentCharges[i] >= configs[i].maxCharges - 1)
                    {
                        currentUnscaledCooldownTimes[i] -= remaining;
                        if (currentUnscaledCooldownTimes[i] < min) currentUnscaledCooldownTimes[i] = min;
                        break;
                    }
                    currentUnscaledCooldownTimes[i] -= remaining;
                    remaining = 0f;
                    if (currentUnscaledCooldownTimes[i] < 0f)
                    {
                        remaining = 0f - currentUnscaledCooldownTimes[i];
                        currentCharges[i] = Math.Min(currentCharges[i] + configs[i].addedCharges, configs[i].maxCharges);
                        if (currentCharges[i] >= configs[i].maxCharges)
                        {
                            currentUnscaledCooldownTimes[i] = 0f;
                            break;
                        }
                        currentUnscaledCooldownTimes[i] = GetMaxCooldownTime(i);
                    }
                }
                RpcSetCooldownTime(i, currentUnscaledCooldownTimes[i]);
                RpcSetCharge(i, currentCharges[i]);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void ApplyCooldownReductionByRatio_Imp(float ratio, bool ignoreCanReceiveCooldown = false)
        {
            if (!Mirror.NetworkServer.active) return;
            for (int i = 0; i < configs.Length; i++)
            {
                if (!configs[i].canReceiveCooldownReduction && !ignoreCanReceiveCooldown) continue;
                if (currentCharges[i] >= configs[i].maxCharges) continue;
                // 割合短縮は本体と同じく非スケール最大時間を使い、時間倍率では割らない。
                float remaining = GetMaxCooldownTime(i) * ratio;
                while (remaining > 0f && currentCharges[i] < configs[i].maxCharges)
                {
                    currentUnscaledCooldownTimes[i] -= remaining;
                    remaining = 0f;
                    if (currentUnscaledCooldownTimes[i] < 0f)
                    {
                        remaining = 0f - currentUnscaledCooldownTimes[i];
                        currentCharges[i] = Math.Min(currentCharges[i] + configs[i].addedCharges, configs[i].maxCharges);
                        if (currentCharges[i] >= configs[i].maxCharges)
                        {
                            currentUnscaledCooldownTimes[i] = 0f;
                            break;
                        }
                        currentUnscaledCooldownTimes[i] = GetMaxCooldownTime(i);
                    }
                }
                RpcSetCooldownTime(i, currentUnscaledCooldownTimes[i]);
                RpcSetCharge(i, currentCharges[i]);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void LogicUpdate(float dt)
        {
            for (int i = 0; i < configs.Length; i++)
            {
                float cooldownTimeMultiplier = GetCooldownTimeMultiplier(i);
                UsedMultipliers.Add(cooldownTimeMultiplier);
                float maxDelta = dt / cooldownTimeMultiplier;
                currentUnscaledCooldownTimes[i] = MoveTowardsZero(currentUnscaledCooldownTimes[i], maxDelta);
            }
        }

        // 本体の Mathf.MoveTowards(time, 0, maxDelta) と同じ伝播: NaN は NaN、Infinity は 0。
        // System.Math.Sign は NaN で例外になるが、Unity の Mathf.Sign は -1 を返す。
        private static float MoveTowardsZero(float current, float maxDelta)
        {
            if (Math.Abs(current) <= maxDelta) return 0f;
            return current + ((0f - current) >= 0f ? 1f : -1f) * maxDelta;
        }
    }

    public class SkillTrigger : AbilityTrigger
    {
        // 本体 SkillTrigger は基底の倍率に SkillBonus の積を掛けた最終結果を返す。
        public float sbCooldownMultiplier = 1f;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override float GetCooldownTimeMultiplier(int configIndex)
            => base.GetCooldownTimeMultiplier(configIndex) * sbCooldownMultiplier;
    }

    // 境界モデル: 本体更新で AttackTrigger 側の宣言オーバーライドが無くなった状態。
    // 宣言が無ければ NativeCooldownSafety.Find はその対象を飛ばし、基底メソッドの補正だけが残る。
    public class AttackTrigger : AbilityTrigger { }
}
