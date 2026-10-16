using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Entity), nameof(Entity.ProcessReceivedDamage))]
    internal static class NativeCoopKeystoneDamage
    {
        private static void Postfix(Entity __instance, ref DamageData __0, Actor __1)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.ApplyCoopKeystoneDamage(__instance, ref __0, __1);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnDealDamage))]
    internal static class NativeCoopKeystoneHit
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(EventInfoDamage info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnCoopKeystoneHit(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnKill))]
    internal static class NativeCoopKeystoneKill
    {
        private static void Prefix(EventInfoKill info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnCoopKeystoneKill(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnDoHeal))]
    internal static class NativeCoopKeystoneHeal
    {
        private static void Prefix(EventInfoHeal info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnCoopKeystoneHeal(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnAbilityInstanceCreated))]
    internal static class NativeCoopKeystoneAbility
    {
        private static void Postfix(EventInfoAbilityInstance info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.OnCoopKeystoneAbility(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ApplyElemental))]
    internal static class NativeCoopKeystoneCold
    {
        private static void Postfix(Actor __instance, ElementalType __0, Entity __1, ElementalStatusEffect __result)
        {
            if (NetworkServer.active && __0 == ElementalType.Cold && __result != null)
                HostAuthority.NativeInstance?.OnCoopColdApplied(__instance, __1, __result);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors))]
    internal static class NativeCoopKeystoneLifetime
    {
        private static void Prefix(Actor __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.ForgetCoopKeystoneActor(__instance);
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class CoopKeyState
        {
            internal Hero Hero;
            internal KeystoneDefinition Definition;
            internal CoopKeystoneKind Kind;
            internal bool Disabled, MemoryReady, RecoverFields;
            internal float CooldownUntil, PowerUntil;
            internal StatBonus PowerBonus;
            internal Hero PowerPartner;
            internal readonly Dictionary<NativeAttributedDamagePacket.Packet, float> Stardust = new Dictionary<NativeAttributedDamagePacket.Packet, float>();
            internal readonly Dictionary<Entity, CoopMark> Marks = new Dictionary<Entity, CoopMark>();
            internal readonly Dictionary<Hero, CoopAlly> Allies = new Dictionary<Hero, CoopAlly>();
            internal readonly List<Ai_QR_InfernalTales> Fields = new List<Ai_QR_InfernalTales>();
            internal readonly List<Entity> Expired = new List<Entity>();
            internal readonly List<Hero> Gone = new List<Hero>();
            internal readonly List<Entity> Splash = new List<Entity>();
            internal float V(string name) => CoopKeystones.Value(Definition, name);
        }

        private sealed class CoopMark
        {
            internal Hero Partner;
            internal float Until;
            internal bool Critical;
            internal StatusEffect Cold;
        }

        private sealed class CoopAlly
        {
            internal readonly StatBonus Bonus = new StatBonus();
            internal bool Attached;
            internal float ParryUntil, TrustUntil;
        }

        private readonly Dictionary<Hero, CoopKeyState> _coopKeys = new Dictionary<Hero, CoopKeyState>();
        private Room _coopKeyRoom;
        private int _coopSplashDepth;

        partial void ConfigureCoopKeystones(Hero hero, Build build)
        {
            ForgetCoopKeystones(hero);
            foreach (var key in build.SelectedKeystones)
            {
                if (!CoopKeystones.TryKind(key.KeystoneId, out var kind)) continue;
                var state = new CoopKeyState { Hero = hero, Definition = key, Kind = kind };
                _coopKeys[hero] = state;
                try
                {
                    // Description reads every required quantity and rejects missing/duplicate settings before combat.
                    CoopKeystones.Describe(key);
                    RequireCoopHooks(kind);
                    _coopKeyRoom = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
                    RecoverCoopFields(state);
                }
                catch (Exception ex) { DisableCoopKey(state, ex); }
            }
        }

        private static void RequireCoopHook(Type adapter, Type target, string method)
        {
            var patches = Harmony.GetPatchInfo(AccessTools.Method(target, method));
            if (patches != null)
            {
                foreach (var patch in patches.Prefixes) if (patch.PatchMethod.DeclaringType == adapter) return;
                foreach (var patch in patches.Postfixes) if (patch.PatchMethod.DeclaringType == adapter) return;
                foreach (var patch in patches.Finalizers) if (patch.PatchMethod.DeclaringType == adapter) return;
            }
            throw new InvalidOperationException("Missing Coop adapter: " + adapter.Name);
        }

        private static void RequireCoopHooks(CoopKeystoneKind kind)
        {
            RequireCoopHook(typeof(NativeCoopKeystoneLifetime), typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors));
            RequireCoopHook(typeof(NativeCoopKeystoneDamage), typeof(Entity), nameof(Entity.ProcessReceivedDamage));
            RequireCoopHook(typeof(NativeCoopKeystoneHit), typeof(Actor), nameof(Actor.InvokeOnDealDamage));
            if (kind == CoopKeystoneKind.SharedSights)
                RequireCoopHook(typeof(NativeCoopKeystoneKill), typeof(Actor), nameof(Actor.InvokeOnKill));
            if (kind == CoopKeystoneKind.GoldenTrust)
                RequireCoopHook(typeof(NativeCoopKeystoneHeal), typeof(Actor), nameof(Actor.InvokeOnDoHeal));
            if (kind == CoopKeystoneKind.FrostResonance)
                RequireCoopHook(typeof(NativeCoopKeystoneCold), typeof(Actor), nameof(Actor.ApplyElemental));
            if (kind == CoopKeystoneKind.ParrySignal || kind == CoopKeystoneKind.SharedCrystalField || kind == CoopKeystoneKind.ShieldWall)
                RequireCoopHook(typeof(NativeCoopKeystoneAbility), typeof(Actor), nameof(Actor.InvokeOnAbilityInstanceCreated));
            if (kind == CoopKeystoneKind.ResonantStardust)
                RequireCoopHook(typeof(NativeAttributedDamagePacket), typeof(Actor), nameof(Actor.DealDamage));
        }

        private bool CoopActive(CoopKeyState state) => !state.Disabled && Alive(state.Hero)
            && AuthoredKeystoneActive(state.Hero, state.Definition.KeystoneId);

        private static float CoopPair(Hero source, Hero ally, float radius = 0f)
        {
            if (source == ally || !Alive(source) || !Alive(ally)
                || source.GetRelation(ally) != EntityRelation.Ally) return 0f;
            if (radius > 0f && (source.agentPosition - ally.agentPosition).sqrMagnitude > radius * radius) return 0f;
            return CoopDistanceNative.Coefficient(source, ally);
        }

        private float CoopNearby(CoopKeyState state)
        {
            float result = 0f;
            if (_am != null)
                foreach (var ally in _am.allHeroes) result = Math.Max(result, CoopPair(state.Hero, ally));
            return result;
        }

        private static bool CoopEnemy(Hero hero, Entity enemy) => enemy != null && enemy.isActive
            && enemy.GetRelation(hero) == EntityRelation.Enemy;

        private static SkillTrigger CoopMemory(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
            {
                if (actor is Gem || actor is AbilityInstance a && a.gem != null) return null;
                if (actor is SkillTrigger skill) return skill;
                if (actor is Summon) return null;
            }
            return null;
        }

        private bool CoopRecentAlly(Hero hero, Entity enemy, float seconds, out Hero partner)
        {
            partner = null;
            if (!_killAssistVictims.TryGetValue(enemy, out var victim)) return false;
            float best = -1f;
            foreach (var hit in victim.Contacts)
                if (hit.HasHit && hit.HitAt <= Time.time && Time.time - hit.HitAt <= seconds
                    && hit.HitAt > best && CoopPair(hero, hit.Hero) > 0f)
                { partner = hit.Hero; best = hit.HitAt; }
            return partner != null;
        }

        private bool CoopAbsent(Hero hero, Entity enemy, float seconds)
        {
            if (_killAssistVictims.TryGetValue(enemy, out var victim))
                foreach (var hit in victim.Contacts)
                    if (hit.Hero == hero && hit.HasHit && Time.time - hit.HitAt < seconds) return false;
            return true;
        }

        private static CoopMark CoopMarkFor(CoopKeyState state, Entity target)
        {
            if (!state.Marks.TryGetValue(target, out var mark))
            { mark = new CoopMark(); state.Marks.Add(target, mark); }
            return mark;
        }

        private static CoopAlly CoopAllyFor(CoopKeyState state, Hero ally)
        {
            if (!state.Allies.TryGetValue(ally, out var buff))
            { buff = new CoopAlly(); state.Allies.Add(ally, buff); }
            return buff;
        }

        internal void ApplyCoopKeystoneDamage(Entity target, ref DamageData damage, Actor actor)
        {
            if (!(damage.currentAmount > 0f) || target == null || _coopKeys.Count == 0
                || !damage.HasAttr(DamageAttribute.IgnoreDamageImmunity)
                    && (damage.isBlockedByImmunity || target.Status.hasDamageImmunity)) return;
            var source = KillAssistOwner(damage.actor ?? actor);
            if (!Alive(source) || !CoopEnemy(source, target)) return;
            foreach (var state in _coopKeys.Values)
            {
                try
                {
                    if (!CoopActive(state)) continue;
                    float pair = CoopPair(state.Hero, source);
                    switch (state.Kind)
                    {
                        case CoopKeystoneKind.SharedSights:
                            if (pair > 0f && state.Marks.TryGetValue(target, out var sight) && Time.time < sight.Until)
                                damage.ApplyAmplification(state.V("allyDamagePercent") / 100f * pair);
                            if (source == state.Hero && state.MemoryReady && CoopNearby(state) > 0f && CoopMemory(damage.actor ?? actor) != null)
                            {
                                state.MemoryReady = false;
                                damage.ApplyAmplification(state.V("memoryDamagePercent") / 100f * CoopNearby(state));
                            }
                            break;
                        case CoopKeystoneKind.FrostResonance:
                            if (pair > 0f && CoopColdEffect(state, target) != null && damage.elemental != ElementalType.Cold)
                                damage.ApplyAmplification(state.V("allyDamagePercent") / 100f * pair);
                            break;
                        case CoopKeystoneKind.ShadowCue:
                            if (source == state.Hero && state.Marks.TryGetValue(target, out var cue) && cue.Critical
                                && CoopPair(source, cue.Partner) > 0f)
                            {
                                var memory = CoopMemory(damage.actor ?? actor);
                                var identity = source.Skill.GetSkill(HeroSkillLocation.Identity);
                                if (identity != null && (identity == memory || identity is St_D_TheKillingFlow
                                    && CoopBasicPacket(source, damage.actor ?? actor, target)))
                                {
                                    cue.Critical = false;
                                    if (!damage.HasAttr(DamageAttribute.IsCrit))
                                    {
                                        damage.SetAttr(DamageAttribute.IsCrit);
                                        float critical = source.Status.critChance + (source.Status.hasAttackCritical ? 1f : 0f);
                                        damage.ApplyAmplification(critical > 1f ? DewMath.RandomRoundToInt(critical) : 1f);
                                        damage.ApplyAmplification(source.Status.critAmp);
                                    }
                                }
                            }
                            break;
                        case CoopKeystoneKind.ParrySignal:
                            if (pair > 0f && state.Allies.TryGetValue(source, out var signal) && Time.time < signal.ParryUntil)
                            {
                                signal.ParryUntil = 0f;
                                damage.ApplyAmplification(state.V("damagePercent") / 100f * pair);
                            }
                            break;
                        case CoopKeystoneKind.GoldenTrust:
                            if (pair > 0f && state.Allies.TryGetValue(source, out var trust) && Time.time < trust.TrustUntil)
                                damage.ApplyAmplification(state.V("damagePercent") / 100f * pair);
                            break;
                        case CoopKeystoneKind.ResonantStardust:
                            if (source == state.Hero && _coopSplashDepth == 0 && Time.time >= state.CooldownUntil
                                && CoopBasicPacket(source, damage.actor ?? actor, target)
                                && CoopRecentAlly(source, target, state.V("contactSeconds"), out var partner))
                            {
                                var packet = NativeAttributedDamagePacket.Current;
                                if (packet != null && packet.Actor == actor && packet.Victim == target)
                                {
                                    float coefficient = CoopPair(source, partner);
                                    state.CooldownUntil = Time.time + state.V("cooldownSeconds");
                                    state.Stardust[packet] = coefficient;
                                    damage.ApplyAmplification(state.V("damagePercent") / 100f * coefficient);
                                }
                            }
                            break;
                        case CoopKeystoneKind.SharedCrystalField:
                            if (pair > 0f && CoopInField(state, source))
                                damage.ApplyAmplification(state.V("allyDamagePercent") / 100f * pair);
                            if (source == state.Hero && state.Marks.TryGetValue(target, out var crystal)
                                && Time.time < crystal.Until && CoopPair(source, crystal.Partner) > 0f)
                                damage.ApplyAmplification(state.V("damagePercent") / 100f * CoopPair(source, crystal.Partner));
                            break;
                    }
                }
                catch (Exception ex) { DisableCoopKey(state, ex); }
            }
        }

        private static bool CoopBasicPacket(Hero hero, Actor actor, Entity target)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet != null) return packet.Actor == actor && packet.Victim == target && packet.MainBasic;
            return actor != null && KillAssistOwner(actor) == hero && IsNativeBasicDamage(actor);
        }

        internal void OnCoopKeystoneHit(EventInfoDamage info)
        {
            if (!(info.damage.amount > 0f) || float.IsInfinity(info.damage.amount) || info.victim == null) return;
            var source = KillAssistOwner(info.actor);
            foreach (var state in _coopKeys.Values)
            {
                try
                {
                    if (!CoopActive(state)) continue;
                    if (state.Kind == CoopKeystoneKind.PackGuard && info.victim is Summon summon
                        && KillAssistOwner(summon) == state.Hero && CoopEnemy(state.Hero, info.actor?.firstEntity))
                    {
                        var enemy = info.actor.firstEntity;
                        var threatened = enemy.AI?.context.targetEnemy as Hero;
                        if (CoopPair(state.Hero, threatened) > 0f)
                        {
                            state.PowerPartner = threatened;
                            state.PowerUntil = Time.time + state.V("seconds");
                            UpdateCoopPower(state);
                        }
                    }
                    if (source == null || !CoopEnemy(source, info.victim)) continue;
                    float pair = CoopPair(state.Hero, source);
                    switch (state.Kind)
                    {
                        case CoopKeystoneKind.SharedSights:
                            if (source == state.Hero && CoopNearby(state) > 0f)
                            {
                                CoopMarkFor(state, info.victim).Until = Time.time + state.V("markSeconds");
                            }
                            break;
                        case CoopKeystoneKind.FrostResonance:
                            if (pair > 0f)
                            {
                                var cold = CoopColdEffect(state, info.victim);
                                if (cold is StackedStatusEffect stacks)
                                    stacks.Network_lastStackDecayTime += state.V("extendSeconds") * pair;
                                else if (cold != null && cold.remainingDuration.HasValue)
                                {
                                    float duration = cold.remainingDuration.Value + state.V("extendSeconds") * pair;
                                    cold.SetTimer(Math.Max(cold.maxDuration ?? duration, duration), duration);
                                }
                            }
                            break;
                        case CoopKeystoneKind.ShadowCue:
                            if (pair > 0f && Time.time >= state.CooldownUntil
                                && CoopAbsent(state.Hero, info.victim, state.V("absenceSeconds")))
                            {
                                var cue = CoopMarkFor(state, info.victim);
                                cue.Critical = true; cue.Partner = source;
                                state.CooldownUntil = Time.time + state.V("cooldownSeconds");
                                var movement = state.Hero.Skill.GetSkill(HeroSkillLocation.Movement);
                                if (movement != null && movement.owner == state.Hero)
                                {
                                    int index = movement.currentConfigIndex;
                                    movement.SetCharge(index, movement.currentCharges[index] + (int)state.V("charges"));
                                }
                            }
                            break;
                        case CoopKeystoneKind.GoldenTrust:
                            if (pair > 0f && state.Allies.TryGetValue(source, out var trust) && Time.time < trust.TrustUntil)
                                state.Hero.Heal(info.damage.amount * state.V("healPercent") / 100f * pair).Dispatch(state.Hero);
                            break;
                        case CoopKeystoneKind.ResonantStardust:
                            var packet = NativeAttributedDamagePacket.Current;
                            if (source == state.Hero && packet != null && packet.Actor == info.actor
                                && packet.Victim == info.victim && state.Stardust.TryGetValue(packet, out float coefficient))
                            {
                                state.Stardust.Remove(packet);
                                CoopStardust(state, info.victim, coefficient);
                            }
                            break;
                        case CoopKeystoneKind.SharedCrystalField:
                            if (pair > 0f && CoopInField(state, source))
                            {
                                var crystal = CoopMarkFor(state, info.victim);
                                crystal.Until = Time.time + state.V("seconds"); crystal.Partner = source;
                            }
                            break;
                    }
                }
                catch (Exception ex) { DisableCoopKey(state, ex); }
            }
        }

        internal void OnCoopColdApplied(Actor actor, Entity target, StatusEffect cold)
        {
            var hero = KillAssistOwner(actor);
            if (hero == null || target == null || !_coopKeys.TryGetValue(hero, out var state)
                || state.Kind != CoopKeystoneKind.FrostResonance) return;
            try
            {
                if (CoopActive(state) && CoopNearby(state) > 0f)
                    CoopMarkFor(state, target).Cold = cold;
            }
            catch (Exception ex) { DisableCoopKey(state, ex); }
        }

        private static StatusEffect CoopColdEffect(CoopKeyState state, Entity target)
        {
            if (target?.Status == null) return null;
            if (state.Marks.TryGetValue(target, out var mark) && mark.Cold is Se_Elm_Cold marked
                && marked.isActive && marked.victim == target && marked.stack > 0) return marked;
            foreach (var effect in target.Status.statusEffects)
            {
                if (effect == null || !effect.isActive) continue;
                if (effect is Se_Elm_Cold cold && cold.stack > 0 && KillAssistOwner(effect.info.caster) == state.Hero)
                    return effect;
                bool frostbite = false;
                Actor actor = effect;
                for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                    if (actor is Ai_R_Frostbite_Freeze || actor is Ai_R_Frostbite_Freeze_Projectile)
                    { frostbite = KillAssistOwner(actor) == state.Hero; break; }
                if (!frostbite) continue;
                for (int i = 0; i < effect.basicEffects.Count; i++)
                    if (effect.basicEffects[i] is StunEffect) return effect;
            }
            return null;
        }

        partial void EndCoopKeystonePacket(NativeAttributedDamagePacket.Packet packet)
        {
            foreach (var state in _coopKeys.Values) state.Stardust.Remove(packet);
        }

        internal void OnCoopKeystoneKill(EventInfoKill info)
        {
            var killer = KillAssistOwner(info.actor);
            foreach (var state in _coopKeys.Values)
                try
                {
                    if (state.Kind != CoopKeystoneKind.SharedSights || !CoopActive(state)
                        || CoopPair(state.Hero, killer) <= 0f || info.victim == null
                        || !state.Marks.TryGetValue(info.victim, out var mark) || Time.time >= mark.Until) continue;
                    state.MemoryReady = true;
                }
                catch (Exception ex) { DisableCoopKey(state, ex); }
        }

        internal void OnCoopKeystoneHeal(EventInfoHeal info)
        {
            var source = KillAssistOwner(info.actor);
            if (!(info.amount > 0f) || !(info.target is Hero ally) || source == null
                || !_coopKeys.TryGetValue(source, out var state) || state.Kind != CoopKeystoneKind.GoldenTrust) return;
            try
            {
                if (CoopActive(state) && CoopPair(source, ally) > 0f)
                    CoopAllyFor(state, ally).TrustUntil = Time.time + state.V("seconds");
            }
            catch (Exception ex) { DisableCoopKey(state, ex); }
        }

        internal void OnCoopKeystoneAbility(EventInfoAbilityInstance info)
        {
            var instance = info.instance;
            var hero = KillAssistOwner(instance);
            if (instance == null || hero == null || !_coopKeys.TryGetValue(hero, out var state)) return;
            try
            {
                if (!CoopActive(state)) return;
                if (state.Kind == CoopKeystoneKind.ParrySignal && instance is Se_R_Parry_End parry
                    && parry.victim == hero && parry.blockedDamage > 0f && _am != null)
                    foreach (var ally in _am.allHeroes)
                        if (CoopPair(hero, ally, state.V("radius")) > 0f)
                            CoopAllyFor(state, ally).ParryUntil = Time.time + state.V("seconds");
                if (state.Kind == CoopKeystoneKind.SharedCrystalField && instance is Ai_QR_InfernalTales field)
                {
                    if (!state.Fields.Contains(field)) state.Fields.Add(field);
                    state.RecoverFields = false;
                }
                if (state.Kind == CoopKeystoneKind.ShieldWall && instance is Ai_R_BaptismOfSun) CoopDrawThreats(state);
            }
            catch (Exception ex) { DisableCoopKey(state, ex); }
        }

        partial void RecordOwnershipTaunt(Entity enemy, Hero hero, float seconds);

        private void CoopDrawThreats(CoopKeyState state)
        {
            if (_am == null) return;
            foreach (var enemy in _am.allEntities)
            {
                if (!CoopEnemy(state.Hero, enemy) || enemy.currentHealth <= 0f) continue;
                var ally = enemy.AI?.context.targetEnemy as Hero;
                if (CoopPair(state.Hero, ally, state.V("radius")) > 0f)
                {
                    RecordOwnershipTaunt(enemy, state.Hero, CoopBalance.ShieldWallTauntSeconds);
                    enemy.AI.Aggro(state.Hero);
                }
            }
        }

        private void RecoverCoopFields(CoopKeyState state)
        {
            if (state.Kind != CoopKeystoneKind.SharedCrystalField || _am == null) return;
            state.RecoverFields = false;
            foreach (var actor in _am.allActors)
                if (actor is Ai_QR_InfernalTales field && field.isActive
                    && KillAssistOwner(field) == state.Hero && !state.Fields.Contains(field)) state.Fields.Add(field);
        }

        private static bool CoopInField(CoopKeyState state, Hero ally)
        {
            foreach (var field in state.Fields)
                if (field != null && field.isActive && field.info.caster == state.Hero
                    && field.range != null && field.range.OverlapPoint(ally.agentPosition.ToXY())) return true;
            return false;
        }

        private void CoopStardust(CoopKeyState state, Entity victim, float coefficient)
        {
            if (_am == null) return;
            state.Splash.Clear();
            float radius = state.V("radius"), amount = state.Hero.Status.abilityPower * state.V("powerPercent") / 100f * coefficient;
            var center = victim.agentPosition;
            foreach (var enemy in _am.allEntities)
                if (CoopEnemy(state.Hero, enemy) && enemy.currentHealth > 0f
                    && (enemy.agentPosition - center).sqrMagnitude <= radius * radius) state.Splash.Add(enemy);
            _coopSplashDepth++;
            EnterGenerated(state.Hero);
            try
            {
                foreach (var enemy in state.Splash)
                    if (enemy != null && enemy.isActive && enemy.currentHealth > 0f)
                        state.Hero.MagicDamage(amount, 0f).Dispatch(enemy);
            }
            finally { ExitGenerated(state.Hero); _coopSplashDepth--; state.Splash.Clear(); }
        }

        partial void ScanCoopKeystones()
        {
            var room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
            if (_coopKeyRoom != room)
            {
                ClearCoopKeystones(); _coopKeyRoom = room;
                foreach (var state in _coopKeys.Values)
                    try { RecoverCoopFields(state); } catch (Exception ex) { DisableCoopKey(state, ex); }
            }
            foreach (var state in _coopKeys.Values)
            {
                try
                {
                    if (!CoopActive(state)) { ResetCoopKey(state); continue; }
                    if (state.RecoverFields) RecoverCoopFields(state);
                    state.Expired.Clear();
                    foreach (var mark in state.Marks)
                        if (mark.Key == null || !mark.Key.isActive || mark.Key.currentHealth <= 0f
                            || !mark.Value.Critical && Time.time >= mark.Value.Until
                                && (mark.Value.Cold == null || !mark.Value.Cold.isActive)) state.Expired.Add(mark.Key);
                    foreach (var enemy in state.Expired) state.Marks.Remove(enemy);
                    for (int i = state.Fields.Count - 1; i >= 0; i--)
                        if (state.Fields[i] == null || !state.Fields[i].isActive) state.Fields.RemoveAt(i);
                    UpdateCoopPower(state);
                    if (state.Kind == CoopKeystoneKind.PackGuard) CoopGuardTarget(state);
                    if (state.Kind == CoopKeystoneKind.ShieldWall || state.Kind == CoopKeystoneKind.SharedCrystalField)
                        ScanCoopAllyStats(state);
                }
                catch (Exception ex) { DisableCoopKey(state, ex); }
            }
        }

        private void UpdateCoopPower(CoopKeyState state)
        {
            if (state.Kind != CoopKeystoneKind.PackGuard) return;
            float coefficient = CoopPair(state.Hero, state.PowerPartner);
            if (Time.time < state.PowerUntil && coefficient > 0f)
            {
                if (state.PowerBonus == null)
                { state.PowerBonus = new StatBonus(); state.Hero.Status.AddStatBonus(state.PowerBonus); }
                state.PowerBonus.abilityPowerPercentage = state.V("powerPercent") * coefficient;
                state.Hero.Status.CalculateStatsIfDirty();
            }
            else if (state.PowerBonus != null)
            {
                state.Hero.Status.RemoveStatBonus(state.PowerBonus); state.PowerBonus = null;
                state.Hero.Status.CalculateStatsIfDirty();
            }
        }

        partial void RecordSummonGuardTarget(Summon summon, Entity enemy, float seconds);

        private void CoopGuardTarget(CoopKeyState state)
        {
            if (_am == null || !_runtimes.TryGetValue(state.Hero, out var rt)) return;
            Hero weakest = null; float health = float.MaxValue;
            foreach (var ally in _am.allHeroes)
                if (CoopPair(state.Hero, ally) > 0f && ally.maxHealth > 0f && ally.currentHealth / ally.maxHealth < health)
                { weakest = ally; health = ally.currentHealth / ally.maxHealth; }
            if (weakest == null) return;
            foreach (var summon in rt.Summons.Keys)
            {
                if (summon == null || !summon.isActive || summon.currentHealth <= 0f || summon.AI == null) continue;
                Entity selected = null; float nearest = float.MaxValue;
                foreach (var enemy in _am.allEntities)
                {
                    if (!CoopEnemy(state.Hero, enemy) || enemy.currentHealth <= 0f || enemy.AI?.context.targetEnemy != weakest) continue;
                    float distance = (enemy.agentPosition - summon.agentPosition).sqrMagnitude;
                    if (distance < nearest && summon.CanAggroTo(enemy)) { selected = enemy; nearest = distance; }
                }
                if (selected == null) continue;
                RecordSummonGuardTarget(summon, selected, 0.25f);
                if (summon.AI.context.targetEnemy != selected) summon.AI.Aggro(selected);
            }
        }

        private float CoopUnsharedArmor(Hero hero)
        {
            hero.Status.CalculateStatsIfDirty();
            float shared = 0f;
            foreach (var state in _coopKeys.Values)
                if (state.Kind == CoopKeystoneKind.ShieldWall && state.Allies.TryGetValue(hero, out var buff) && buff.Attached)
                    shared += buff.Bonus.armorFlat;
            // Native flat bonuses are multiplied by the recipient's armor percentage before final armor.
            return Math.Max(0f, hero.Status.armor - shared * (1f + hero.Status.bonusStats.armorPercentage / 100f));
        }

        private void ScanCoopAllyStats(CoopKeyState state)
        {
            if (_am == null) return;
            float sourceArmor = state.Kind == CoopKeystoneKind.ShieldWall ? CoopUnsharedArmor(state.Hero) : 0f;
            foreach (var ally in _am.allHeroes)
            {
                float pair = CoopPair(state.Hero, ally, state.Kind == CoopKeystoneKind.ShieldWall ? state.V("radius") : 0f);
                bool active = pair > 0f && (state.Kind != CoopKeystoneKind.SharedCrystalField || CoopInField(state, ally));
                if (active) CoopAllyFor(state, ally);
            }
            state.Gone.Clear();
            foreach (var entry in state.Allies)
            {
                var ally = entry.Key; var buff = entry.Value;
                float pair = CoopPair(state.Hero, ally, state.Kind == CoopKeystoneKind.ShieldWall ? state.V("radius") : 0f);
                bool active = pair > 0f && (state.Kind != CoopKeystoneKind.SharedCrystalField || CoopInField(state, ally));
                float armor = active && state.Kind == CoopKeystoneKind.ShieldWall
                    ? sourceArmor * state.V("armorPercent") / 100f * pair : 0f;
                float haste = active && state.Kind == CoopKeystoneKind.SharedCrystalField ? state.V("haste") * pair : 0f;
                if (active)
                {
                    buff.Bonus.armorFlat = armor; buff.Bonus.abilityHasteFlat = haste;
                    if (!buff.Attached) { ally.Status.AddStatBonus(buff.Bonus); buff.Attached = true; }
                    ally.Status.CalculateStatsIfDirty();
                }
                else
                {
                    RemoveCoopAllyBonus(ally, buff); state.Gone.Add(ally);
                }
            }
            foreach (var ally in state.Gone) state.Allies.Remove(ally);
        }

        private static void RemoveCoopAllyBonus(Hero ally, CoopAlly buff)
        {
            if (buff.Attached && ally != null && ally.Status != null)
            { ally.Status.RemoveStatBonus(buff.Bonus); ally.Status.CalculateStatsIfDirty(); }
            buff.Attached = false;
        }

        private static void ResetCoopKey(CoopKeyState state)
        {
            foreach (var ally in state.Allies) RemoveCoopAllyBonus(ally.Key, ally.Value);
            state.Allies.Clear(); state.Marks.Clear(); state.Fields.Clear(); state.Stardust.Clear();
            state.PowerPartner = null; state.RecoverFields = true;
            state.MemoryReady = false; state.PowerUntil = state.CooldownUntil = 0f;
            if (state.PowerBonus != null && state.Hero != null)
            { state.Hero.Status.RemoveStatBonus(state.PowerBonus); state.Hero.Status.CalculateStatsIfDirty(); }
            state.PowerBonus = null;
        }

        private static void DisableCoopKey(CoopKeyState state, Exception ex)
        {
            if (state.Disabled) return;
            state.Disabled = true;
            try { ResetCoopKey(state); } catch (Exception) { }
            Log.Warn("Host: Coop keystone disabled: " + state.Definition.KeystoneId + ": " + ex.Message);
        }

        partial void ClearCoopKeystones()
        {
            foreach (var state in _coopKeys.Values)
                try { ResetCoopKey(state); } catch (Exception ex) { DisableCoopKey(state, ex); }
        }

        partial void ForgetCoopKeystones(Hero hero)
        {
            if (ReferenceEquals(hero, null) || !_coopKeys.TryGetValue(hero, out var state)) return;
            try { ResetCoopKey(state); } catch (Exception ex) { DisableCoopKey(state, ex); }
            _coopKeys.Remove(hero);
        }

        internal void ForgetCoopKeystoneActor(Actor actor)
        {
            foreach (var state in _coopKeys.Values)
                try
                {
                    if (actor == state.Hero) ResetCoopKey(state);
                    if (actor is StatusEffect cold)
                        foreach (var mark in state.Marks.Values)
                            if (ReferenceEquals(mark.Cold, cold)) mark.Cold = null;
                    if (actor is Entity enemy) state.Marks.Remove(enemy);
                    if (actor is Hero ally && state.Allies.TryGetValue(ally, out var buff))
                    { RemoveCoopAllyBonus(ally, buff); state.Allies.Remove(ally); }
                    if (actor is Ai_QR_InfernalTales field) state.Fields.Remove(field);
                }
                catch (Exception ex) { DisableCoopKey(state, ex); }
        }
    }
}
