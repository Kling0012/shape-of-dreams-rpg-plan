using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>
    /// v1.32 B「遠征の鍛錬」（RunGrowth）のホスト側。スタックは <see cref="RunGrowthLedger"/> に旅人の持ち主（DewPlayer）ごとに持ち、
    /// HeroRuntime が捨てられても（復活・再生成・装備の付け直し）残る。遠征の識別子（GameManager.runId）が変わると0に戻る。
    /// 溜まる契機：
    ///   DamageTakenMaxHpPct / ShieldAbsorbedMaxHpPct … Hero.EntityEvent_OnTakeDamage（OnTakeDamage）の EventInfoDamage。
    ///       失ったHP = damage.amount - negatedAmountByShield、障壁の吸収 = negatedAmountByShield（どちらも敵の攻撃だけ）。
    ///   ParrySuccess … Actor.ActorEvent_OnAbilityInstanceCreated で Se_R_Parry_End（St_R_Parry の成功時に生成。blockedDamage &gt; 0）。
    ///   CritBasicAttackKill … 基本攻撃の会心（BasicAttackContext + DamageAttribute.IsCrit）で与えたダメージの直後の ActorEvent_OnKill。
    /// 能力値は専用の StatBonus（GrowthBonus）としてヒーローへ足し、回復・シールド・召喚獣の強さだけは既存の処理器が読む。
    /// </summary>
    internal sealed partial class HostAuthority
    {
        internal static readonly RunGrowthLedger RunGrowthLedger = new RunGrowthLedger();

        private static string GrowthOwner(HeroRuntime rt)
        {
            var owner = rt.Hero != null ? rt.Hero.owner : null;
            if (ReferenceEquals(owner, null) || owner == null) return null;
            if (rt.GrowthOwnerKey == null || rt.GrowthOwnerNetId != owner.netId)
            {
                rt.GrowthOwnerNetId = owner.netId;
                rt.GrowthOwnerKey = owner.netId.ToString(CultureInfo.InvariantCulture);
            }
            return rt.GrowthOwnerKey;
        }
        private static void EnsureGrowthRun()
        {
            var game = NetworkedManagerBase<GameManager>.softInstance;
            RunGrowthLedger.EnsureRun(game != null ? game.runId : null);
        }

        private void GainRunGrowth(HeroRuntime rt, RunGrowthTrigger trigger, double units)
        {
            var build = rt.Powers?.Build;
            if (build == null || build.RunGrowths.Count == 0 || !(units > 0d)) return;
            string owner = GrowthOwner(rt);
            if (owner == null) return;
            EnsureGrowthRun();
            if (RunGrowthLedger.RunId == null) return;
            foreach (var entry in build.RunGrowths)
                if (entry.Trigger == trigger) RunGrowthLedger.Gain(owner, entry, trigger, units);
        }

        /// <summary>敵から受けたダメージ：失ったHPと、障壁が吸収した量を最大HPに対する%で数える（TollOfGrudge と同じ見方）。</summary>
        private void OnRunGrowthDamage(HeroRuntime rt, EventInfoDamage info, bool enemy)
        {
            var build = rt.Powers?.Build;
            if (!enemy || build == null || build.RunGrowths.Count == 0 || info.damage.amount <= 0f) return;
            var hero = rt.Hero;
            float absorbed = Math.Max(0f, info.negatedAmountByShield);
            float lost = Math.Max(0f, info.damage.amount - absorbed);
            var native = NativeDamageContext.Current;
            if (native != null && native.Target == hero) lost = Math.Min(lost, Math.Max(0f, native.Health));
            GainRunGrowth(rt, RunGrowthTrigger.DamageTakenMaxHpPct, RunGrowth.HealthPercent(lost, hero.maxHealth));
            GainRunGrowth(rt, RunGrowthTrigger.ShieldAbsorbedMaxHpPct, RunGrowth.HealthPercent(absorbed, hero.maxHealth));
        }

        /// <summary>St_R_Parry の成功：Se_R_Parry_Start が防いだダメージを持つ Se_R_Parry_End を作った瞬間（本体の「チャージされたサーベル」と同じ検出）。</summary>
        private void OnRunGrowthAbility(HeroRuntime rt, EventInfoAbilityInstance info)
        {
            try
            {
                if (!Alive(rt.Hero) || !(info.instance is Se_R_Parry_End end) || end.victim != rt.Hero || end.blockedDamage <= 0f) return;
                GainRunGrowth(rt, RunGrowthTrigger.ParrySuccess, 1d);
            }
            catch (Exception ex) { Log.Error("Host: RunGrowth parry " + ex); }
        }

        /// <summary>会心の基本攻撃のダメージを覚えておき、そのとどめ（ActorEvent_OnKill）で数える。</summary>
        private void TrackCritBasicDamage(HeroRuntime rt, EventInfoDamage info, int victimId)
        {
            var basic = BasicAttackContext.Current;
            if (basic != null && basic.From == rt.Hero && basic.Target == info.victim && info.damage.HasAttr(DamageAttribute.IsCrit))
                rt.CritBasicVictims.Add(victimId);
            else rt.CritBasicVictims.Remove(victimId);
        }

        private void TrackCritBasicKill(HeroRuntime rt, int victimId)
        {
            if (rt.CritBasicVictims.Remove(victimId)) GainRunGrowth(rt, RunGrowthTrigger.CritBasicAttackKill, 1d);
        }

        /// <summary>スタックが変わったら能力値へ反映し、持ち主へ知らせる（毎フレーム）。</summary>
        private void ApplyRunGrowth(HeroRuntime rt, float now)
        {
            var hero = rt.Hero;
            if (hero == null || hero.Status == null || rt.Powers == null) return;
            var build = rt.Powers.Build;
            EnsureGrowthRun();
            int version = RunGrowthLedger.Version;
            if (rt.GrowthVersion != version || !ReferenceEquals(rt.GrowthBuild, build))
            {
                try
                {
                    string owner = GrowthOwner(rt);
                    rt.GrowthTotals.Clear();
                    var bonus = new StatBonus();
                    foreach (var entry in build.RunGrowths)
                    {
                        int stacks = RunGrowthLedger.Stacks(owner, entry.StarId);
                        if (stacks <= 0) continue;
                        foreach (var effect in entry.Effects)
                        {
                            double total = RunGrowth.StatTotal(entry, effect, stacks);
                            rt.GrowthTotals.TryGetValue(effect.Stat, out double sum);
                            rt.GrowthTotals[effect.Stat] = sum + total;
                            AddNativeStat(bonus, effect.Stat, StatUnits.ToGame(effect.Stat, total));
                        }
                    }
                    if (rt.GrowthBonus != null) hero.Status.RemoveStatBonus(rt.GrowthBonus);
                    rt.GrowthBonus = null;
                    if (rt.GrowthTotals.Count > 0)
                    {
                        hero.Status.AddStatBonus(bonus);
                        rt.GrowthBonus = bonus;
                    }
                    hero.Status.CalculateStatsIfDirty();
                    rt.GrowthVersion = version;
                    rt.GrowthBuild = build;
                }
                catch (Exception ex)
                {
                    rt.GrowthVersion = version;
                    rt.GrowthBuild = build;
                    Log.Error("Host: RunGrowth apply " + ex);
                }
            }
            SendRunGrowth(rt, now, version, build);
        }

        private void SendRunGrowth(HeroRuntime rt, float now, int version, Build build)
        {
            if (build.RunGrowths.Count == 0 || now - rt.GrowthSentAt < 0.5f) return;
            // 変わった時は速く、変わらなくても5秒ごとに再送する（途中参加・取りこぼし向け）。
            if (rt.GrowthSentVersion == version && now - rt.GrowthSentAt < 5f) return;
            var player = rt.Hero != null ? rt.Hero.owner : null;
            if (ReferenceEquals(player, null) || player == null || _registeredOn == null) return;
            string owner = GrowthOwner(rt);
            var sb = new StringBuilder();
            foreach (var entry in build.RunGrowths)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(entry.StarId).Append('=').Append(RunGrowthLedger.Stacks(owner, entry.StarId).ToString(CultureInfo.InvariantCulture));
            }
            rt.GrowthSentAt = now;
            rt.GrowthSentVersion = version;
            _registeredOn.CustomRpc_SendMessageToClient(player, new DreamforgeRunGrowthMsg
            {
                protocol = Protocol.Version, heroNetId = rt.Hero.netId, runId = RunGrowthLedger.RunId ?? "", stacks = sb.ToString(),
            });
        }

        /// <summary>回復・シールド・召喚獣の強さへ足す、鍛錬の分（%）。</summary>
        private static float GrowthSupport(HeroRuntime rt, Stat stat)
            => rt.GrowthTotals.TryGetValue(stat, out double value) ? (float)value : 0f;
    }
}
