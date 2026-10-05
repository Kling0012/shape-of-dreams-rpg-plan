using System;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // Use the server mutation, not the delayed client unequip RPC.
    [HarmonyPatch(typeof(HeroSkill), "UnequipSkill")]
    internal static class NativePairMemoryUnequip
    {
        private static void Postfix(HeroSkill __instance, SkillTrigger __result)
        {
            if (NetworkServer.active && __result != null)
                HostAuthority.NativeInstance?.OnPairMemoryUnequipped(__instance);
        }
    }

    internal sealed partial class HostAuthority
    {
        internal void OnPairMemoryUnequipped(HeroSkill skill)
        {
            try
            {
                if (skill == null || skill.hero == null || !_runtimes.TryGetValue(skill.hero, out var rt)) return;
                CollectPairMemories(rt);
                rt.PairCombos.RefreshEquipment(rt.PairMemories);
            }
            catch (Exception ex) { Log.Error("Host: pair memory unequip " + ex.Message); }
        }

        private void OnAttackFired(HeroRuntime rt, EventInfoAttackFired info)
        {
            try
            {
                if (!NetworkServer.active || !Alive(rt.Hero)
                    || !(info.actor is AttackTrigger attack) || attack.owner != rt.Hero
                    || info.info.caster != rt.Hero) return;
                rt.Powers.OnAttackFired(info.isThisAttackFourthAttack);
                if (_gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reactionEffectDepth != 0
                    || rt.HeroKey != "Hero_Nachia" || rt.Powers.Build.PairCombos.Count == 0) return;
                CollectPairMemories(rt);
                rt.GimmickRequests.Clear();
                // AttackTrigger emits this once per completed shot, even when it has no target.
                rt.PairCombos.Fire(PairComboTrigger.OnBasicAttack, null, Time.time, 0, 0f, false,
                    rt.PairMemories, HasOwnSummons(rt), rt.GimmickRequests);
                QueueGimmickRequests(rt, null, Time.time);
            }
            catch (Exception ex) { Log.Error("Host: OnAttackFired " + ex.Message); }
        }

        private void ApplyExposeDamage(HeroRuntime rt, ref DamageData damage, Entity victim, float additionalExposePercent = 0f)
        {
            if (_gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reactionEffectDepth != 0
                || damage.IsAmountModifiedBy(typeof(GimmickRuntime)) || IsPairReactionSource(damage.actor)) return;
            float now = Time.time;
            CollectPairMemories(rt);
            rt.PairCombos.RefreshEquipment(rt.PairMemories);
            int id = victim.GetInstanceID();
            // Expose sources share the strongest vulnerability; distinct pair IDs do not stack.
            float expose = Math.Max(Math.Max(rt.Gimmicks.ExposePercent(id, now), rt.Reactions.ExposePercent(id, now)),
                rt.PairCombos.ExposePercent(id, now));
            expose = Math.Max(expose, additionalExposePercent);
            if (expose > 0) damage.ApplyAmplification(expose / 100f);
        }

        private void QueueGimmicks(HeroRuntime rt, GimmickTrigger trigger, string memory, Entity victim, float damage, bool pairGenerated = false,
            Actor actor = null, bool direct = true)
        {
            var build = rt.Powers.Build;
            if (build.Gimmicks.Count == 0 && build.PairCombos.Count == 0) return;
            var requests = rt.GimmickRequests;
            requests.Clear();
            float now = Time.time;
            if (build.Gimmicks.Count > 0 && !pairGenerated && _pairDamageDepth == 0
                && FindMemory(rt.Hero, memory) != null
                && !(trigger == GimmickTrigger.OnUse
                    && rt.Hero.Skill.GetSkill(HeroSkillLocation.Movement)?.GetType().Name == memory))
                FireGimmicksV129(rt, trigger, memory, victim, damage, actor, direct, requests);
            if (build.PairCombos.Count > 0)
            {
                CollectPairMemories(rt);
                rt.PairCombos.Fire((PairComboTrigger)trigger, memory, now,
                    victim != null ? victim.GetInstanceID() : 0, damage,
                    pairGenerated || _gimmickDamageDepth != 0 || _pairDamageDepth != 0,
                    rt.PairMemories, HasOwnSummons(rt), requests, PairActivation(rt, actor), PairHitKind(actor));
            }
            QueueGimmickRequests(rt, victim, now);
        }

        private void QueueGimmickRequests(HeroRuntime rt, Entity victim, float now)
        {
            var requests = rt.GimmickRequests;
            if (requests.Count > 0) SendBountyReport(rt, BountyReportKind.GimmicksTriggered, requests.Count);
            foreach (var request in requests)
            {
                var effect = request.Entry.Def.Effect;
                if (effect == GimmickEffect.Quicken || effect == GimmickEffect.Empower || effect == GimmickEffect.Expose) continue;
                var pending = new PendingGimmick
                {
                    ShieldEquipmentEpoch = rt.ShieldEquipmentEpoch,
                    QueuedAt = now,
                    Request = request,
                    Victim = victim,
                    Pair = PairForRequest(rt, request.Entry.StarId),
                    Center = request.AreaAroundHero ? rt.Hero.agentPosition
                        : victim != null ? victim.position : rt.Hero.agentPosition,
                    Due = now + (request.Entry.Def.Effect == GimmickEffect.Echo ? request.Entry.Def.EffectiveDelaySeconds ?? 0.3f : 0f),
                };
                var configured = LegacyGimmickForRequest(rt, request.Entry.StarId);
                if (configured != null)
                {
                    long epoch = RefreshMemoryAttributionEquipment(rt.Hero);
                    string key = BuildAggregation.GimmickStateKey(configured);
                    bool hadKey = rt.Powers.Build.SelectedKeystones.Count > 0;
                    pending.AuthoredChannelId = configured.StarId;
                    pending.AuthoredIsCurrent = () => epoch == RefreshMemoryAttributionEquipment(rt.Hero)
                        && LegacyGimmickForRequest(rt, configured.StarId) is GimmickEntry current
                        && (BuildAggregation.GimmickStateKey(current) == key
                            || hadKey && rt.Powers.Build.SelectedKeystones.Count == 0 && SameLegacyPendingBaseline(configured, current));
                    pending.AuthoredDefinition = () => TransformLegacyGimmick(rt,
                        LegacyGimmickForRequest(rt, configured.StarId)?.Def, request.Entry.Memory,
                        request.SourceKind ?? KeystoneSourceKind.NativeMemory, configured.StarId);
                }
                rt.PendingGimmicks.Add(pending);
            }
            requests.Clear();
        }

        private static GimmickEntry LegacyGimmickForRequest(HeroRuntime rt, string id)
        {
            foreach (var entry in rt.Powers.Build.Gimmicks) if (entry.StarId == id) return entry;
            return null;
        }

        private static bool SameLegacyPendingBaseline(GimmickEntry before, GimmickEntry after)
        {
            var a = before.Def; var b = after.Def;
            return before.Memory == after.Memory && a.Trigger == b.Trigger && a.Effect == b.Effect
                && a.Arg == b.Arg && a.Cooldown == b.Cooldown && a.ValuePrecise == b.ValuePrecise
                && a.DurationUnits == b.DurationUnits && a.RadiusUnits == b.RadiusUnits
                && a.ExtraTargets == b.ExtraTargets && a.ChanceUnits == b.ChanceUnits
                && (!b.UncappedValue.HasValue || a.UncappedValue == b.UncappedValue)
                && (!b.UncappedDurationUnits.HasValue || a.UncappedDurationUnits == b.UncappedDurationUnits)
                && (!b.UncappedRadiusUnits.HasValue || a.UncappedRadiusUnits == b.UncappedRadiusUnits)
                && (!b.UncappedExtraTargets.HasValue || a.UncappedExtraTargets == b.UncappedExtraTargets)
                && (!b.UncappedChanceUnits.HasValue || a.UncappedChanceUnits == b.UncappedChanceUnits)
                && (before.Channel == null) == (after.Channel == null)
                && (before.Channel == null || FractionalScopedModifiers.ChannelKey(before) == FractionalScopedModifiers.ChannelKey(after));
        }
        private static PairComboDef PairForRequest(HeroRuntime rt, string id)
        {
            foreach (var entry in rt.Powers.Build.PairCombos)
                if (entry.Def.Id == id) return entry.Def;
            return null;
        }

        private static void CollectPairMemories(HeroRuntime rt)
        {
            rt.PairMemories.Clear();
            if (rt.Hero.Skill == null) return;
            foreach (var slot in LinkSkills)
            {
                var skill = rt.Hero.Skill.GetSkill(slot);
                if (skill != null) rt.PairMemories.Add(skill.GetType().Name);
            }
        }

        private static bool HasOwnSummons(HeroRuntime rt)
        {
            foreach (var summon in rt.Hero.summons)
                if (summon != null && summon.isActive && summon.currentHealth > 0f
                    && summon.FindFirstAncestorOfType<Hero>() == rt.Hero) return true;
            return false;
        }
        private void ApplyPendingGimmicks(HeroRuntime rt, float now)
        {
            var pending = rt.PendingGimmicks;
            if (!Alive(rt.Hero)) { pending.Clear(); return; }
            // Remove before dispatch: nested game events must never replay this request.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var effect = pending[i];
                if (effect.AuthoredIsCurrent != null && !effect.AuthoredIsCurrent()) { pending.RemoveAt(i); continue; }
                if (effect.AuthoredDefinition != null)
                {
                    var effective = effect.AuthoredDefinition();
                    if (effective == null) { pending.RemoveAt(i); continue; }
                    effect.Request.Entry = new GimmickEntry { StarId = effect.Request.Entry.StarId, Memory = effect.Request.Entry.Memory, Def = effective };
                    if (effective.Effect == GimmickEffect.Burst || effect.Request.AreaRadius > 0)
                        effect.Request.AreaRadius = Gimmicks.Radius(effective, Gimmicks.AreaRadius);
                    if (effective.Effect == GimmickEffect.Echo)
                        effect.Due = effect.QueuedAt + (effective.EffectiveDelaySeconds ?? 0.3f);
                }
                if (effect.Due > now) { pending[i] = effect; continue; }
                pending.RemoveAt(i);
                if (effect.Pair != null && (FindMemory(rt.Hero, effect.Pair.RouteA) == null
                    || FindMemory(rt.Hero, effect.Pair.RouteB) == null)) continue;
                _pairDamageDepth++;
                try { ApplyGimmick(rt, effect); }
                catch (Exception ex) { Log.Error("Host: memory effect " + ex); }
                finally { _pairDamageDepth--; }
            }
        }

        private void ApplyGimmick(HeroRuntime rt, PendingGimmick pending)
        {
            var hero = rt.Hero;
            var request = pending.Request;
            var def = request.Entry.Def;
            var victim = pending.Victim;
            bool liveTarget = victim != null && victim.isActive && victim.currentHealth > 0f
                && victim.GetRelation(hero) == EntityRelation.Enemy;
            switch (def.Effect)
            {
                case GimmickEffect.Element:
                    int stacks = Gimmicks.ElementStacks(def, _rng.NextDouble());
                    if (stacks <= 0) break;
                    var element = def.Arg == 0 ? ElementalType.Fire : def.Arg == 1 ? ElementalType.Cold
                        : def.Arg == 2 ? ElementalType.Light : ElementalType.Dark;
                    if (request.AreaRadius > 0f)
                    {
                        ListReturnHandle<Entity> handle;
                        var found = DewPhysics.OverlapCircleAllEntities(out handle, pending.Center, request.AreaRadius, EnemyFilter, hero);
                        try
                        {
                            foreach (var enemy in found)
                                if (enemy != victim && enemy != null && enemy.isActive && enemy.currentHealth > 0f)
                                    hero.ApplyElemental(element, enemy, stacks);
                        }
                        finally { handle.Return(); }
                    }
                    else if (liveTarget) hero.ApplyElemental(element, victim, stacks);
                    break;
                case GimmickEffect.Burst:
                    EnterGenerated(hero);
                    try
                    {
                        DamageAround(hero, pending.Center, request.AreaRadius,
                            TransformAuthoredGeneratedDamage(hero, Math.Max(hero.Status.attackDamage, hero.Status.abilityPower) * def.ValuePercent / 100f,
                                request.Entry.Memory, pending.AuthoredChannelId ?? request.Entry.StarId, GimmickEffect.Burst),
                            null, int.MaxValue, hero.Status.abilityPower > hero.Status.attackDamage, gimmick: true);
                    }
                    finally { ExitGenerated(hero); }
                    break;
                case GimmickEffect.Shield:
                    ApplyGimmickV129(rt, pending);
                    break;
                case GimmickEffect.Heal:
                    var support = ActorManager.instance.serverActor;
                    // 出どころを変えたので、旅人の回復量はここで掛ける（v1.27.1 の能力値）。
                    int healPower = rt.Powers.Build.Get(Stat.HealPower);
                    support.Heal(SupportStats.AmplifyHeal(hero.maxHealth * def.ValuePercent / 100f, healPower)).Dispatch(hero);
                    float healRadius = Gimmicks.Radius(def, 10f);
                    if (def.Arg == 1)
                        foreach (var player in DewPlayer.gamePlayers)
                        {
                            var ally = player != null ? player.hero : null;
                            if (ally == hero || !Alive(ally) || ally.GetRelation(hero) != EntityRelation.Ally
                                || (ally.agentPosition - hero.agentPosition).sqrMagnitude > healRadius * healRadius) continue;
                            float allyBefore = ally.currentHealth;
                            support.Heal(SupportStats.AmplifyHeal(ally.maxHealth * (float)(def.EffectiveAllyValuePercent ?? def.EffectiveValueOrAuthored) / 100f, healPower)).Dispatch(ally);
                            CreditHealRestored(rt, ally, allyBefore);
                        }
                    break;
                case GimmickEffect.Recharge:
                    ReduceMemoryCooldown(hero, FindMemory(hero, request.Entry.Memory), def.ValuePercent);
                    break;
                case GimmickEffect.Reload:
                    var skill = FindMemory(hero, request.Entry.Memory);
                    if (skill != null && Gimmicks.TryReload(skill.currentConfigCurrentCharge, skill.currentConfig.maxCharges,
                        out int nextCharges, out bool resetCooldown))
                    {
                        if (resetCooldown) hero.ResetCooldown(skill);
                        else skill.SetCharge(skill.currentConfigIndex, nextCharges);
                    }
                    break;
                case GimmickEffect.RechargeOther:
                    if (hero.Skill == null) break;
                    foreach (var slot in LinkSkills)
                    {
                        var other = hero.Skill.GetSkill(slot);
                        if (other == null || slot == HeroSkillLocation.Movement) continue;
                        if (Gimmicks.CanRechargeOther(request.Entry.Memory, other.GetType().Name,
                            other.type == SkillType.Normal, slot == HeroSkillLocation.Identity))
                            ReduceMemoryCooldown(hero, other, def.ValuePercent);
                    }
                    break;
                case GimmickEffect.Echo:
                    if (!liveTarget || request.Damage <= 0f) break;
                    EnterGenerated(hero);
                    try
                    {
                        // Final damage is already armor-adjusted; repeat that amount without a second armor reduction.
                        hero.PureDamage(TransformAuthoredGeneratedDamage(hero, request.Damage * def.ValuePercent / 100f,
                            request.Entry.Memory, pending.AuthoredChannelId ?? request.Entry.StarId, GimmickEffect.Echo), 0f)
                            .SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
                    }
                    finally { ExitGenerated(hero); }
                    break;
                // Quicken/Empower/Expose windows are registered by the pure runtime.
                default: ApplyGimmickV129(rt, pending); break;
            }
        }

        private static void ReduceMemoryCooldown(Hero hero, SkillTrigger skill, float percent)
        {
            if (skill == null) return;
            // The native ratio is a fraction of maximum cooldown, not remaining cooldown.
            float ratio = Gimmicks.RemainingCooldownReductionRatio(skill.currentConfigUnscaledCooldownTime,
                skill.currentConfigUnscaledMaxCooldownTime, percent);
            if (ratio > 0f) hero.ApplyCooldownReductionByRatio(skill, ratio, false);
        }
        private static bool IsPairReactionSource(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (actor is ElementalStatusEffect || actor is Gem
                    || actor is AbilityInstance instance && instance.gem != null) return true;
            return false;
        }
        private static SkillTrigger FindMemory(Hero hero, string memory)
        {
            if (hero.Skill == null) return null;
            foreach (var slot in LinkSkills)
            {
                var skill = hero.Skill.GetSkill(slot);
                if (skill != null && NativeActorTypeName(skill) == memory) return skill;
            }
            return null;
        }
        private void OnAttackHit(EventInfoAttackHit info)
        {
            try
            {
                if (!(info.attacker is Hero hero) || !_runtimes.TryGetValue(hero, out var rt) || !Alive(hero)) return;
                var victim = info.victim;
                if (victim == null || !victim.isActive) return;
                if (victim.GetRelation(hero) != EntityRelation.Enemy || _gimmickDamageDepth != 0 || _pairDamageDepth != 0
                    || BasicAttackContext.Current != null && !BasicAttackContext.Current.Primary) return;
                float criticalEcho = rt.Powers.TakeCriticalEcho(Time.time, info.isCrit);
                if (ReduceMemoryCooldowns(hero, criticalEcho)) LogPowerTrigger(Power.CriticalEcho);
                float ratio = victim.maxHealth > 0 ? victim.currentHealth / victim.maxHealth : 1f;
                var r = rt.Powers.OnAttackHit(Time.time, hero.maxHealth, hero.Status.attackDamage, hero.Status.abilityPower,
                    ratio, info.isCrit, _rng.NextDouble(), consumeNextBasic: !rt.Powers.UsesMemoryPreparationLedger);
                _pairDamageDepth++;
                try
                {
                    if (r.ChainDamage > 0) DamageAround(hero, victim.position, PowerRuntime.ChainRange, r.ChainDamage, victim, PowerRuntime.ChainTargets, magic: true);
                    if (r.Heal > 0)
                    {
                        // On-hit healing (lifesteal): the Killing Flow heal scale applies to exactly this dispatch.
                        _onHitHealDepth++;
                        try { hero.Heal(r.Heal).Dispatch(hero); } finally { _onHitHealDepth--; }
                    }
                    if (r.ExecuteDamage > 0) hero.PureDamage(r.ExecuteDamage, 0f).Dispatch(victim);
                    if (r.BlazeDamage > 0 && victim.isActive) hero.MagicDamage(r.BlazeDamage, 0f).Dispatch(victim);
                    if (victim.isActive)
                    {
                        if (r.FireStacks > 0) hero.ApplyElemental(ElementalType.Fire, victim, r.FireStacks);
                        if (r.ColdStacks > 0) hero.ApplyElemental(ElementalType.Cold, victim, r.ColdStacks);
                        if (r.LightStacks > 0) hero.ApplyElemental(ElementalType.Light, victim, r.LightStacks);
                        if (r.DarkStacks > 0) hero.ApplyElemental(ElementalType.Dark, victim, r.DarkStacks);
                    }
                    if (r.OpeningDamage > 0 && victim.isActive && victim.GetRelation(hero) == EntityRelation.Enemy)
                        hero.PureDamage(r.OpeningDamage, 0f).Dispatch(victim);
                    QueuePowerDamage(rt, victim, victim.position, 0f,
                        r.EchoDamage + r.ShadowStepDamage + r.RunUpDamage + r.PrimedDamage, pure: true);
                }
                finally { _pairDamageDepth--; }
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnAttackHit " + ex.Message);
            }
        }
    }
}
