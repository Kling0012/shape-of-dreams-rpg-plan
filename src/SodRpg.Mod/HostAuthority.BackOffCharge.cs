using System;
using System.Collections.Generic;
using System.Reflection;
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
        /// 取れない通知（本体の更新で名前や型が変わった場合など）があっても例外にせず、何も出さずに終わる。
        /// </summary>
        private sealed class BackOffChargeHost
        {
            public readonly BackOffChargeRuntime Runtime = new BackOffChargeRuntime();
            public Action<EventInfoCast> Start, Finish;
            public EventInfo StartEvent, CancelEvent;
        }

        private readonly Dictionary<HeroRuntime, BackOffChargeHost> _backOffCharge = new Dictionary<HeroRuntime, BackOffChargeHost>();

        private void InitializeBackOffCharge(HeroRuntime rt)
        {
            if (rt?.Hero == null || _backOffCharge.ContainsKey(rt)) return;
            var host = new BackOffChargeHost();
            host.Start = info => { if (IsBackOffCast(rt, info)) host.Runtime.Begin(Time.time); };
            host.Finish = info => { if (IsBackOffCast(rt, info)) host.Runtime.End(); };
            // 完了は既存の仕掛けも使っている通知。開始と中断は、型が合うときだけ購読する。
            rt.Hero.EntityEvent_OnCastCompleteBeforePrepare += host.Finish;
            host.StartEvent = SubscribeCastEvent(rt.Hero, "EntityEvent_OnCastStart", host.Start);
            host.CancelEvent = SubscribeCastEvent(rt.Hero, "EntityEvent_OnCastCancel", host.Finish);
            _backOffCharge.Add(rt, host);
        }

        private static EventInfo SubscribeCastEvent(Hero hero, string name, Action<EventInfoCast> handler)
        {
            try
            {
                var evt = typeof(Entity).GetEvent(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (evt == null || evt.EventHandlerType != typeof(Action<EventInfoCast>))
                {
                    Log.Warn($"Native event {name} unavailable; Back Off charge damage may not trigger.");
                    return null;
                }
                evt.AddEventHandler(hero, handler);
                return evt;
            }
            catch (Exception ex)
            {
                Log.Warn($"Native event {name} could not be subscribed; Back Off charge damage may not trigger: {ex.Message}");
                return null;
            }
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
            host.Runtime.End();
            if (rt.Hero == null) return;
            rt.Hero.EntityEvent_OnCastCompleteBeforePrepare -= host.Finish;
            try
            {
                host.StartEvent?.RemoveEventHandler(rt.Hero, host.Start);
                host.CancelEvent?.RemoveEventHandler(rt.Hero, host.Finish);
            }
            catch (Exception ex) { Log.Warn("Back Off charge unsubscribe failed: " + ex.Message); }
        }

        private void ClearBackOffCharge()
        {
            foreach (var host in _backOffCharge.Values) host.Runtime.End();
        }

        private void StageBackOffCharge()
        {
            foreach (var pair in _backOffCharge) TickBackOffCharge(pair.Key, pair.Value, Time.time);
        }

        private void TickBackOffCharge(HeroRuntime rt, BackOffChargeHost host, float now)
        {
            if (!host.Runtime.Charging) return;
            var hero = rt.Hero;
            if (!Alive(hero) || hero.Status == null || hero.Status.hasStun || FindMemory(hero, BackOffChargeRuntime.Memory) == null)
            {
                host.Runtime.End();
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
