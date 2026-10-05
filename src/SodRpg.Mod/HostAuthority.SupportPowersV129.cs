using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    using Power = SodRpg.Core.Game.Power;

    internal sealed partial class HostAuthority
    {
        // Expiry calls Entity.Kill using its stale last attacker. Retain the actual lethal damage scope instead.
        private readonly Dictionary<Summon, long> _summonLethalHits =
            new Dictionary<Summon, long>();

        private void HookSupportSummonV129(Summon summon)
        {
            summon.EntityEvent_OnTakeDamage += OnSupportDamageV129;
            summon.EntityEvent_OnDeath += OnSupportDeathV129;
        }

        private void UnhookSupportSummonV129(Summon summon)
        {
            if (summon == null) return;
            summon.EntityEvent_OnTakeDamage -= OnSupportDamageV129;
            summon.EntityEvent_OnDeath -= OnSupportDeathV129;
            _summonLethalHits.Remove(summon);
        }

        private void OnSupportDamageV129(EventInfoDamage info)
        {
            if (!(info.victim is Summon summon)) return;
            _summonLethalHits.Remove(summon);
            var hit = NativeDamageContext.Current;
            if (_gimmickDamageDepth != 0 || _reactionEffectDepth != 0 || hit == null || hit.Target != summon
                || hit.Health <= 0f || summon.currentHealth > .00001f || info.damage.amount <= 0f) return;
            var attacker = info.actor != null ? info.actor.firstEntity : null;
            if (attacker != null && attacker.GetRelation(summon) == EntityRelation.Enemy)
                _summonLethalHits[summon] = hit.Serial;
        }

        private void OnSupportDeathV129(EventInfoKill info)
        {
            if (info.victim is Summon summon)
            {
                if (!_summonLethalHits.TryGetValue(summon, out var serial)) return;
                _summonLethalHits.Remove(summon);
                var hit = NativeDamageContext.Current;
                if (hit == null || hit.Serial != serial || hit.Target != summon
                    || _gimmickDamageDepth != 0 || _reactionEffectDepth != 0) return;
                var rt = summon.hero != null && _runtimes.TryGetValue(summon.hero, out var owner) ? owner : null;
                if (rt == null || !Alive(rt.Hero)) return;
                float amount = rt.Powers.DeathBloomDamage(Higher(rt.Hero), true);
                QueuePowerDamage(rt, null, summon.agentPosition, 4f, amount);
                if (amount > 0f) LogPowerTrigger(Power.DeathBloom);
                return;
            }
            if (!(info.victim is PropEnt_Stone_Gold) && !(info.victim is PropEnt_Stone_DreamDust)) return;
            if (_gimmickDamageDepth != 0 || _reactionEffectDepth != 0) return;
            var breaker = RuntimeOf(info.actor);
            if (breaker == null || !Alive(breaker.Hero)) return;
            float heal = breaker.Powers.TakeShardBoon(UnityEngine.Time.time, breaker.Hero.maxHealth);
            PowerHeal(breaker, breaker.Hero, heal);
            if (heal > 0f) LogPowerTrigger(Power.ShardBoon);
        }

        private void OnSupportHealingV129(HeroRuntime rt, EventInfoHeal info)
        {
            if (_powerSupportDepth != 0 || _gimmickDamageDepth != 0 || _reactionEffectDepth != 0
                || !Alive(rt.Hero) || info.target == rt.Hero
                || !(info.target is Hero) && !(info.target is Summon)
                || info.target.GetRelation(rt.Hero) != EntityRelation.Ally
                || MemorySource(info.actor) == null
                || info.amount <= 0f || float.IsNaN(info.amount) || float.IsInfinity(info.amount)) return;
            float heal = rt.Powers.KindnessReturnsHeal(info.amount);
            PowerHeal(rt, rt.Hero, heal);
            if (heal > 0f) LogPowerTrigger(Power.KindnessReturns);
        }

        internal void OnNativeGemUseV129(Gem gem)
        {
            try
            {
                var hero = gem != null ? gem.owner : null;
                if (!NetworkServer.active || _powerSupportDepth != 0 || _gimmickDamageDepth != 0
                    || !Alive(hero) || !_runtimes.TryGetValue(hero, out var rt)) return;
                float percent = rt.Powers.CrystalCircuitCooldownFraction * 100f;
                ReduceNormalMemories(hero, percent);
                if (percent > 0f) LogPowerTrigger(Power.CrystalCircuit);
            }
            catch (Exception ex) { Log.Error("Host: essence power " + ex); }
        }

        internal void OnNativePotionPickupV129(Hero hero)
        {
            try
            {
                if (!NetworkServer.active || !Alive(hero) || !_runtimes.TryGetValue(hero, out var rt)) return;
                float percent = rt.Powers.ApothecaryCooldownFraction * 100f;
                ReduceNormalMemories(hero, percent);
                if (percent > 0f) LogPowerTrigger(Power.Apothecary);
            }
            catch (Exception ex) { Log.Error("Host: potion power " + ex); }
        }

        private void OnPotionHealV129(HeroRuntime rt, EventInfoHeal info)
        {
            if (_powerSupportDepth != 0 || _gimmickDamageDepth != 0 || info.target != rt.Hero || !Alive(rt.Hero)
                || !(info.actor is Se_GenericHealOverTime)
                || !(info.actor.parentActor is Ai_RegenOrb_Projectile projectile)
                || projectile.info.caster != rt.Hero || info.amount <= 0f
                || float.IsNaN(info.amount) || float.IsInfinity(info.amount)) return;
            // Each native potion tick shares half the collector's actual healing. Other recipients do not retrigger it.
            float heal = rt.Powers.ApothecaryAllyHeal(info.amount);
            if (heal <= 0f) return;
            foreach (var ally in LivingAllies(rt.Hero, 10f)) PowerHeal(rt, ally, heal);
        }

        private void ClearSupportPowerStateV129() => _summonLethalHits.Clear();
    }

    [HarmonyPatch(typeof(Gem), nameof(Gem.NotifyUse))]
    internal static class CrystalCircuitNativeUse
    {
        private static void Postfix(Gem __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnNativeGemUseV129(__instance);
        }
    }

    [HarmonyPatch(typeof(Pickup_RegenOrb), "OnPickup")]
    internal static class ApothecaryNativePickup
    {
        private static void Postfix(Hero hero)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnNativePotionPickupV129(hero);
        }
    }
}
