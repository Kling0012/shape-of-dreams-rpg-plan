using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        /// <summary>
        /// 「下がれ！」のチャージ中の継続ダメージ。本体の詠唱開始・完了の通知でチャージの区間を取り、区間の間だけ
        /// <see cref="BackOffChargeRuntime"/> が数える脈動ごとに、ケトゥスの周囲の敵へ継続ダメージを出す。
        /// 開始通知を取れない場合は、この機能だけを無効にする。中断はチャージ中の記憶の詠唱状態で確認する。
        /// </summary>
        private sealed class BackOffChargeHost
        {
            public readonly BackOffChargeRuntime Runtime = new BackOffChargeRuntime();
            public Action<EventInfoCast> Start, Finish;
            public SafeAction<EventInfoCast> StartAction;
            public bool Enabled, Staged;
            public SkillTrigger ChargingSkill;

            public void End() { Runtime.End(); ChargingSkill = null; }
        }

        private readonly Dictionary<HeroRuntime, BackOffChargeHost> _backOffCharge = new Dictionary<HeroRuntime, BackOffChargeHost>();
        private readonly List<KeyValuePair<HeroRuntime, BackOffChargeHost>> _chargingBackOff = new List<KeyValuePair<HeroRuntime, BackOffChargeHost>>();
        private static bool _backOffChargeWarningIssued;

        private void InitializeBackOffCharge(HeroRuntime rt)
        {
            if (rt?.Hero == null || _backOffCharge.ContainsKey(rt)) return;
            var host = new BackOffChargeHost();
            host.Start = info =>
            {
                if (!host.Enabled || !IsBackOffCast(rt, info)) return;
                host.ChargingSkill = (SkillTrigger)info.trigger;
                host.Runtime.Begin(Time.time);
                if (!host.Staged)
                {
                    host.Staged = true;
                    _chargingBackOff.Add(new KeyValuePair<HeroRuntime, BackOffChargeHost>(rt, host));
                }
            };
            // 装備の交換後に届いた完了も、開始した実体に対する通知なら受け取る。
            host.Finish = info => { if (ReferenceEquals(info.trigger, host.ChargingSkill)) host.End(); };
            host.StartAction = SubscribeBackOffCastStart(rt.Hero, host.Start);
            if (host.StartAction == null) return;
            try { rt.Hero.EntityEvent_OnCastCompleteBeforePrepare += host.Finish; }
            catch (Exception ex)
            {
                RemoveBackOffChargeHandlers(rt.Hero, host);
                WarnBackOffChargeUnavailable(ex.Message);
                return;
            }
            host.Enabled = true;
            _backOffCharge.Add(rt, host);
        }

        private static SafeAction<EventInfoCast> SubscribeBackOffCastStart(Hero hero, Action<EventInfoCast> handler)
        {
            try
            {
                var field = AccessTools.Field(typeof(Entity), "EntityEvent_OnCastStart");
                if (field == null || field.FieldType != typeof(SafeAction<EventInfoCast>))
                {
                    WarnBackOffChargeUnavailable("Native cast start SafeAction field unavailable.");
                    return null;
                }
                var action = (SafeAction<EventInfoCast>)field.GetValue(hero);
                if (action == null)
                {
                    action = new SafeAction<EventInfoCast>();
                    field.SetValue(hero, action);
                }
                action.Add(handler);
                return action;
            }
            catch (Exception ex)
            {
                WarnBackOffChargeUnavailable(ex.Message);
                return null;
            }
        }

        private static void WarnBackOffChargeUnavailable(string reason)
        {
            if (_backOffChargeWarningIssued) return;
            _backOffChargeWarningIssued = true;
            Log.Warn("Back Off charge damage disabled: " + reason);
        }

        private bool IsBackOffCast(HeroRuntime rt, EventInfoCast info)
        {
            if (!NetworkServer.active || rt.Hero == null || !(info.trigger is SkillTrigger skill)) return false;
            return ReferenceEquals(skill, FindMemory(rt.Hero, BackOffChargeRuntime.Memory));
        }

        private void UnhookBackOffCharge(HeroRuntime rt)
        {
            if (rt == null || !_backOffCharge.TryGetValue(rt, out var host)) return;
            _backOffCharge.Remove(rt);
            RemoveBackOffChargeHandlers(rt.Hero, host);
        }

        private static void RemoveBackOffChargeHandlers(Hero hero, BackOffChargeHost host)
        {
            // 解除が本体側で失敗しても、残った開始ハンドラーから状態を再開させない。
            host.Enabled = false;
            host.End();
            try { if (hero != null) hero.EntityEvent_OnCastCompleteBeforePrepare -= host.Finish; }
            catch (Exception ex) { Log.Warn("Back Off charge unsubscribe failed: " + ex.Message); }
            try { host.StartAction?.Remove(host.Start); }
            catch (Exception ex) { Log.Warn("Back Off charge unsubscribe failed: " + ex.Message); }
        }

        private void ClearBackOffCharge()
        {
            foreach (var host in _backOffCharge.Values) { host.End(); host.Staged = false; }
            _chargingBackOff.Clear();
        }

        private void StageBackOffCharge()
        {
            if (_chargingBackOff.Count == 0) return;
            float now = Time.time;
            for (int i = _chargingBackOff.Count - 1; i >= 0; i--)
            {
                var pair = _chargingBackOff[i];
                TickBackOffCharge(pair.Key, pair.Value, now);
                if (!pair.Value.Runtime.Charging)
                {
                    pair.Value.End();
                    pair.Value.Staged = false;
                    _chargingBackOff.RemoveAt(i);
                }
            }
        }

        private void TickBackOffCharge(HeroRuntime rt, BackOffChargeHost host, float now)
        {
            if (!host.Runtime.Charging) return;
            var hero = rt.Hero;
            if (!Alive(hero) || hero.Status == null || hero.Status.hasStun
                || !ReferenceEquals(host.ChargingSkill, FindMemory(hero, BackOffChargeRuntime.Memory))
                || !host.ChargingSkill.Network_isCasting)
            {
                host.End();
                return;
            }
            int pulses = host.Runtime.Poll(now);
            if (pulses <= 0) return;
            float damage = BackOffChargeRuntime.TickDamage(hero.Status.attackDamage, hero.Status.abilityPower,
                StarDamageScaling.Multiplier(rt.Powers.Build.SpentStarPoints));
            if (damage <= 0f) return;
            damage = TransformAuthoredGeneratedDamage(hero, damage, BackOffChargeRuntime.Memory, null, GimmickEffect.Wound);
            bool magic = hero.Status.abilityPower > hero.Status.attackDamage;
            // 生成ダメージの区間に入れて、このダメージから爆発・反響などの仕掛けが連鎖しないようにする。
            EnterGenerated(hero);
            try
            {
                for (int pulse = 0; pulse < pulses; pulse++)
                {
                    ListReturnHandle<Entity> handle;
                    var enemies = DewPhysics.OverlapCircleAllEntities(out handle, hero.agentPosition, BackOffChargeRuntime.Radius, EnemyFilter, hero);
                    try
                    {
                        int remaining = BackOffChargeRuntime.MaxTargets;
                        foreach (var enemy in enemies)
                        {
                            if (enemy == null || !enemy.isActive || enemy.currentHealth <= 0) continue;
                            DispatchGimmickDamage(hero, enemy, damage, magic, true);
                            if (--remaining == 0) break;
                        }
                    }
                    finally { handle.Return(); }
                }
            }
            finally { ExitGenerated(hero); }
        }
    }
}
