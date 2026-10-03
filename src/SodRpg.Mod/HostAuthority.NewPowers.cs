using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Power = SodRpg.Core.Game.Power;
    using Stat = SodRpg.Core.Game.Stat;

    internal sealed partial class HostAuthority
    {
        internal static HostAuthority NativeInstance;
        internal static float CurrentNativeShield(Entity entity)
        {
            if (entity == null || entity.Status == null) return 0f;
            float amount = 0f;
            foreach (var status in entity.Status.statusEffects)
                if (status != null && status.isActive)
                    foreach (var effect in status.basicEffects)
                        if (effect is ShieldEffect shield && shield.isAlive) amount += Math.Max(0f, shield.amount);
            return amount;
        }
        private readonly HashSet<Entity> _nativeDeathEntities = new HashSet<Entity>();
        internal void OnNativeElementApplied(EventInfoApplyElemental info) => OnApplyElemental(info);

        private void ClearNativeDeathHooks()
        {
            foreach (var entity in _nativeDeathEntities)
                if (entity != null) entity.EntityEvent_OnDeath -= _onDeath;
            _nativeDeathEntities.Clear();
        }
        private static readonly HeroSkillLocation[] NormalMemorySlots =
            { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R };

        private sealed class NewPowerHostState
        {
            internal Vector3 Position;
            internal float SampleTime;
            internal bool PositionKnown;
            internal float SpecialMovementUntil;
            internal string OmenRun;
            internal int OmenGeneration = -1;
            internal Action<EventInfoShield> TakeShield;
            internal DataProcessor<HealData, Actor, Entity> ShieldReceived;
            internal readonly Dictionary<int, float> Overkill = new Dictionary<int, float>();
            internal readonly List<Action> Pending = new List<Action>();
        }

        private readonly Dictionary<long, Se_GenericShield_OneShot> _powerShields = new Dictionary<long, Se_GenericShield_OneShot>();
        private int _powerSupportDepth;

        private static IEnumerable<SkillTrigger> NormalMemories(Hero hero)
        {
            if (hero == null || hero.Skill == null) yield break;
            foreach (var loc in NormalMemorySlots)
            {
                var skill = hero.Skill.GetSkill(loc);
                if (skill != null && skill.type == SkillType.Normal) yield return skill;
            }
        }

        internal static bool AllNormalMemoriesReady(Entity entity)
        {
            bool any = false;
            foreach (var skill in NormalMemories(entity as Hero))
            {
                any = true;
                if (skill.currentConfigUnscaledCooldownTime > 0f && skill.currentConfigCurrentCharge <= 0) return false;
            }
            return any;
        }

        private static bool AllNormalMemoriesCooling(Hero hero)
        {
            bool any = false;
            foreach (var skill in NormalMemories(hero))
            {
                any = true;
                if (skill.currentConfigUnscaledCooldownTime <= 0f || skill.currentConfigCurrentCharge > 0) return false;
            }
            return any;
        }

        private static bool UltimateReady(Hero hero)
        {
            if (hero.Skill == null) return false;
            foreach (var loc in LinkSkills)
            {
                var skill = hero.Skill.GetSkill(loc);
                if (skill != null && skill.type == SkillType.Ultimate && skill.currentConfigUnscaledCooldownTime <= 0f)
                    return true;
            }
            return false;
        }

        private static bool IsNormalMemory(Hero hero, string memory)
        {
            if (memory == null) return false;
            foreach (var skill in NormalMemories(hero)) if (skill.GetType().Name == memory) return true;
            return false;
        }

        private static void ReduceNormalMemories(Hero hero, float percent, bool longestOnly = false)
        {
            if (percent <= 0f) return;
            SkillTrigger longest = null;
            foreach (var skill in NormalMemories(hero))
            {
                if (longestOnly)
                {
                    if (longest == null || skill.currentConfigUnscaledCooldownTime > longest.currentConfigUnscaledCooldownTime)
                        longest = skill;
                }
                else ReduceNormalMemory(hero, skill, percent);
            }
            if (longest != null) ReduceNormalMemory(hero, longest, percent);
        }

        private static void ReduceNormalMemory(Hero hero, SkillTrigger skill, float percent)
        {
            float maximum = skill.currentConfigUnscaledMaxCooldownTime;
            if (maximum <= 0f) return;
            float ratio = Math.Min(1f, Math.Max(0f, percent / 100f)) * skill.currentConfigUnscaledCooldownTime / maximum;
            if (ratio > 0f) hero.ApplyCooldownReductionByRatio(skill, ratio, false);
        }

        private IEnumerable<Hero> LivingAllies(Hero hero, float range, bool includeSelf = false)
        {
            if (_am == null) yield break;
            foreach (var other in _am.allHeroes)
                if (Alive(other) && (includeSelf && other == hero || other != hero && other.GetRelation(hero) == EntityRelation.Ally)
                    && (range <= 0f || (other.agentPosition - hero.agentPosition).sqrMagnitude <= range * range))
                    yield return other;
        }

        private static float Higher(Hero hero) => Math.Max(hero.Status.attackDamage, hero.Status.abilityPower);

        private void PowerHeal(HeroRuntime rt, Entity target, float amount)
        {
            if (amount <= 0f || target == null || !target.isActive || target.currentHealth <= 0f) return;
            var support = _am != null ? _am.serverActor : null;
            if (support == null) return;
            _powerSupportDepth++;
            try { support.Heal(SupportStats.AmplifyHeal(amount, rt.Powers.Build.Get(Stat.HealPower))).Dispatch(target); }
            finally { _powerSupportDepth--; }
        }

        private void PowerShield(HeroRuntime rt, Entity target, Power power, float amount, float duration)
        {
            if (amount <= 0f || target == null || !target.isActive || target.currentHealth <= 0f) return;
            var support = _am != null ? _am.serverActor : null;
            if (support == null) return;
            long key = ((long)target.GetInstanceID() << 32) | (uint)power;
            _powerSupportDepth++;
            try
            {
                var shield = support.GiveShield(target, SupportStats.AmplifyShield(amount,
                    rt.Powers.Build.Get(Stat.ShieldPower)), duration);
                if (shield == null) return;
                if (_powerShields.TryGetValue(key, out var old) && old != null && old.isActive && old.shield != null
                    && shield.shield != null && old.shield.amount >= shield.shield.amount)
                {
                    old.SetTimer(duration);
                    shield.Destroy();
                    return;
                }
                if (old != null && old.isActive) old.Destroy();
                _powerShields[key] = shield;
                // Native callbacks were suppressed until the non-stacking winner was known.
                // Newly accepted personal wards may be shared once; shared wards never reshare.
                if (power != Power.SharedWard && target is Hero recipient && shield.shield != null)
                    ShareAcceptedWard(recipient, shield.shield.amount);
            }
            finally { _powerSupportDepth--; }
        }

        private void QueuePowerDamage(HeroRuntime rt, Entity victim, Vector3 center, float radius,
            float amount, bool magic = false, int maxTargets = int.MaxValue, Entity except = null, bool pure = false)
        {
            if (amount <= 0f) return;
            rt.NewPowers.Pending.Add(() =>
            {
                _gimmickDamageDepth++;
                try
                {
                    if (radius > 0f)
                    {
                        if (!pure) DamageAround(rt.Hero, center, radius, amount, except, maxTargets, magic, gimmick: true);
                        else
                        {
                            ListReturnHandle<Entity> handle;
                            var found = DewPhysics.OverlapCircleAllEntities(out handle, center, radius, EnemyFilter, rt.Hero);
                            try
                            {
                                int count = 0;
                                foreach (var enemy in found)
                                {
                                    if (enemy == except || enemy == null || !enemy.isActive || enemy.currentHealth <= 0f) continue;
                                    if (count++ >= maxTargets) break;
                                    rt.Hero.PureDamage(amount, 0f).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(enemy);
                                }
                            }
                            finally { handle.Return(); }
                        }
                    }
                    else if (victim != null && victim.isActive && victim.currentHealth > 0f
                        && victim.GetRelation(rt.Hero) == EntityRelation.Enemy)
                    {
                        var damage = pure ? rt.Hero.PureDamage(amount, 0f)
                            : magic ? rt.Hero.MagicDamage(amount, 0f) : rt.Hero.PhysicalDamage(amount, 0f);
                        damage.SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
                    }
                }
                finally { _gimmickDamageDepth--; }
            });
        }

        private void FlushNewPowers(HeroRuntime rt)
        {
            var pending = rt.NewPowers.Pending;
            if (!Alive(rt.Hero)) { pending.Clear(); return; }
            int count = pending.Count;
            for (int i = 0; i < count; i++)
            {
                try { pending[i](); }
                catch (Exception ex) { Log.Error("Host: new power dispatch " + ex); }
            }
            pending.RemoveRange(0, count);
            rt.NewPowers.Overkill.Clear();
        }

        private void InitializeNewPowers(HeroRuntime rt)
        {
            rt.NewPowers.TakeShield = info =>
            {
                if (_powerSupportDepth != 0 || _gimmickDamageDepth != 0 || info.target != rt.Hero) return;
                ShareAcceptedWard(rt.Hero, info.finalAmount);
            };
            rt.Hero.EntityEvent_OnTakeShield += rt.NewPowers.TakeShield;
            rt.Hero.EntityEvent_OnTakeDamage += _onTakeDamage;
            rt.NewPowers.ShieldReceived = (ref HealData data, Actor actor, Entity target) =>
            {
                float bonus = rt.Powers.ShieldGainAmplification();
                if (bonus > 0f) data.ApplyAmplification(bonus);
            };
            rt.Hero.takenShieldProcessor.Add(rt.NewPowers.ShieldReceived);
        }

        private void ShareAcceptedWard(Hero recipient, float amount)
        {
            if (!Alive(recipient) || !_runtimes.TryGetValue(recipient, out var owner)) return;
            float share = owner.Powers.SharedWardShield(amount);
            if (share <= 0f) return;
            foreach (var ally in LivingAllies(recipient, 10f)) PowerShield(owner, ally, Power.SharedWard, share, 4f);
        }

        private void UnhookNewPowers(HeroRuntime rt)
        {
            if (rt.Hero != null)
            {
                rt.Hero.EntityEvent_OnTakeShield -= rt.NewPowers.TakeShield;
                rt.Hero.EntityEvent_OnTakeDamage -= _onTakeDamage;
                rt.Hero.takenShieldProcessor.Remove(rt.NewPowers.ShieldReceived);
            }
            rt.NewPowers.Pending.Clear();
            rt.NewPowers.Overkill.Clear();
        }

        private void ClearNewPowerZone()
        {
            foreach (var rt in _runtimes.Values)
            {
                rt.NewPowers.Pending.Clear();
                rt.NewPowers.Overkill.Clear();
                rt.NewPowers.PositionKnown = false;
            }
            _powerShields.Clear();
        }

        private void UpdateNewPowerFacts(HeroRuntime rt, float now)
        {
            var hero = rt.Hero;
            if (!Alive(hero)) { rt.NewPowers.PositionKnown = false; return; }
            var p = rt.Powers;
            var state = rt.NewPowers;
            float distance = state.PositionKnown ? Vector3.Distance(state.Position, hero.agentPosition) : 0f;
            float elapsed = Math.Max(0f, now - state.SampleTime);
            bool special = hero.Control == null || hero.Control.ongoingDisplacement != null
                || now < state.SpecialMovementUntil
                || distance > Math.Max(1f, hero.Control.currentMaxAgentSpeed * elapsed * 1.5f);
            bool moving = distance > .005f || hero.Control != null && hero.Control.agentVelocity.sqrMagnitude > .01f;
            p.ObserveMovement(now, moving, distance, special);
            state.Position = hero.agentPosition; state.SampleTime = now; state.PositionKnown = true;
            p.ShieldAmount = CurrentNativeShield(hero);
            int normalCount = 0;
            foreach (var skill in NormalMemories(hero)) normalCount++;
            p.ObserveMemoryReadiness(normalCount, AllNormalMemoriesReady(hero), AllNormalMemoriesCooling(hero), UltimateReady(hero));
        }

        private float NearestEnemyDistance(Hero hero)
        {
            float closest = float.PositiveInfinity;
            if (_am == null) return closest;
            foreach (var entity in _am.allEntities)
                if (entity is Monster && entity.isActive && entity.currentHealth > 0f && entity.GetRelation(hero) == EntityRelation.Enemy)
                    closest = Math.Min(closest, (entity.agentPosition - hero.agentPosition).sqrMagnitude);
            return closest;
        }

        private void ScanNewPowerFacts(HeroRuntime rt, float now)
        {
            var hero = rt.Hero;
            var p = rt.Powers;
            if (!Alive(hero)) { p.IsVanguard = p.IsRearguard = false; return; }
            p.NearbyEnemies8 = CountEnemiesNear(hero, 8f);
            float shield = p.TakeBreakout(now, CountEnemiesNear(hero, 6f), hero.maxHealth);
            if (shield > 0f)
            {
                PowerShield(rt, hero, Power.Breakout, shield, 5f);
                ListReturnHandle<Entity> handle;
                var found = DewPhysics.OverlapCircleAllEntities(out handle, hero.agentPosition, 6f, EnemyFilter, hero);
                try
                {
                    foreach (var enemy in found)
                        if (enemy != null && enemy.isActive && enemy.currentHealth > 0f)
                            hero.CreateBasicEffect(enemy, new SlowEffect { strength = 30f }, 2f,
                                "dreamforge.power.breakout", DuplicateEffectBehavior.UsePrevious);
                }
                finally { handle.Return(); }
            }
            float selfDistance = NearestEnemyDistance(hero);
            bool front = !float.IsPositiveInfinity(selfDistance), rear = front;
            int party = 1;
            foreach (var ally in LivingAllies(hero, 0f))
            {
                party++;
                float distance = NearestEnemyDistance(ally);
                if (distance < selfDistance) front = false;
                if (distance > selfDistance) rear = false;
                float ward = p.TakeWatchfulHand(now, ally.GetInstanceID(), ally.maxHealth > 0f ? ally.currentHealth / ally.maxHealth : 1f, hero.maxHealth);
                PowerShield(rt, ally, Power.WatchfulHand, ward, 6f);
            }
            p.IsVanguard = front; p.IsRearguard = rear; p.LivingPartySize = party;
        }

        internal void OnNativeMemoryUsed(SkillTrigger skill, bool allReadyBefore)
        {
            if (!(skill.owner is Hero hero) || !Alive(hero)) return;
            HeroSkillLocation location = HeroSkillLocation.Identity;
            bool equipped = false;
            foreach (var loc in LinkSkills)
                if (hero.Skill.GetSkill(loc) == skill) { location = loc; equipped = true; break; }
            if (!equipped) return;
            bool ultimate = skill.type == SkillType.Ultimate;
            if (ultimate)
                foreach (var recipient in _runtimes.Values)
                    if (Alive(recipient.Hero) && (recipient.Hero == hero || recipient.Hero.GetRelation(hero) == EntityRelation.Ally))
                        ReduceNormalMemories(recipient.Hero, recipient.Powers.CoStarCooldownFraction(recipient.Hero == hero) * 100f);
            if (!_runtimes.TryGetValue(hero, out var rt)) return;
            int count = 0;
            foreach (var normal in NormalMemories(hero)) count++;
            rt.Powers.ObserveMemoryReadiness(count, allReadyBefore, AllNormalMemoriesCooling(hero), UltimateReady(hero));
            bool normalSlot = Array.IndexOf(NormalMemorySlots, location) >= 0;
            float cut = rt.Powers.OnNewMemoryUsed(Time.time, skill.GetType().Name,
                location == HeroSkillLocation.Movement || !normalSlot && !ultimate, ultimate);
            OnSkillUse(rt, new EventInfoSkillUse { skill = skill, type = location });
            ReduceNormalMemory(hero, skill, cut * 100f);
            if (ultimate)
            {
                float heal = rt.Powers.TriumphSongHeal(hero.maxHealth);
                foreach (var ally in LivingAllies(hero, 10f, includeSelf: true)) PowerHeal(rt, ally, heal);
            }
        }

        private void OnNewPowerDamage(HeroRuntime rt, EventInfoDamage info)
        {
            if (_gimmickDamageDepth != 0 || _reactionEffectDepth != 0 || !Alive(rt.Hero) || info.victim == null
                || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy || info.damage.amount <= 0f) return;
            var hero = rt.Hero;
            var p = rt.Powers;
            var victim = info.victim;
            float now = Time.time;
            PowerShield(rt, hero, Power.ReadyGuard, p.TakeReadyGuard(now, hero.maxHealth), 8f);
            // Native discardedAmount already subtracts health AND shields after armor.
            rt.NewPowers.Overkill[victim.GetInstanceID()] = Math.Max(0f, info.damage.discardedAmount);
            bool crit = info.damage.HasAttr(DamageAttribute.IsCrit);
            float extra = p.BrittleIceDamage(Higher(hero), crit, victim.Status.hasCold)
                + p.TakeWeakPointWound(now, victim.GetInstanceID(), Higher(hero), crit);
            QueuePowerDamage(rt, victim, victim.position, 0f, extra);
            var basic = BasicAttackContext.Current;
            if (basic == null || basic.Actor != info.actor || basic.Target != victim || basic.From != hero || !basic.Primary) return;
            p.ShieldAmount = CurrentNativeShield(hero);
            p.NearbyEnemies8 = CountEnemiesNear(hero, 8f);
            p.ObserveMemoryReadiness(1, false, AllNormalMemoriesCooling(hero), UltimateReady(hero));
            var result = p.OnNewBasicHit(now, victim.GetInstanceID(), Higher(hero), info.damage.amount,
                crit, basic.Guaranteed || !basic.CriticalAtNative && crit, primary: true,
                targetWithin8: (victim.agentPosition - hero.agentPosition).sqrMagnitude <= 64f);
            QueuePowerDamage(rt, victim, victim.position, 0f, result.DirectDamage, magic: true);
            QueuePowerDamage(rt, null, victim.position, 3f, result.SplashDamage, except: victim, pure: true);
            if (result.NormalCooldownSeconds > 0f)
                foreach (var skill in NormalMemories(hero)) hero.ApplyCooldownReduction(skill, result.NormalCooldownSeconds);
            if (result.SlowPercent > 0 && victim.isActive && victim.currentHealth > 0f)
                hero.CreateBasicEffect(victim, new SlowEffect { strength = result.SlowPercent }, 1f,
                    "dreamforge.power.strafe", DuplicateEffectBehavior.UsePrevious);
        }

        private void OnNewPowerTaken(HeroRuntime rt, EventInfoDamage info, bool enemy)
        {
            if (!enemy || _gimmickDamageDepth != 0 || info.damage.amount <= 0f) return;
            var hero = rt.Hero;
            var p = rt.Powers;
            var native = NativeDamageContext.Current;
            float beforeShield = native != null && native.Target == hero ? native.Shield : 0f;
            // The native event preserves HP damage even if another synchronous reaction has already healed it.
            float lost = Math.Max(0f, info.damage.amount - info.negatedAmountByShield);
            if (native != null && native.Target == hero) lost = Math.Min(lost, Math.Max(0f, native.Health));
            bool broken = beforeShield > 0f && info.negatedAmountByShield >= beforeShield - .0001f;
            float burst = p.TakeShieldbreak(Time.time, beforeShield, hero.maxHealth, broken);
            QueuePowerDamage(rt, null, hero.agentPosition, 5f, burst);
            QueuePowerDamage(rt, null, hero.agentPosition, 6f, p.TakeTollOfGrudge(lost, hero.maxHealth), magic: true);
            PowerShield(rt, hero, Power.ReadyGuard, p.TakeReadyGuard(Time.time, hero.maxHealth), 8f);
        }

        private void OnNewPowerKill(HeroRuntime rt, EventInfoKill info)
        {
            if (_gimmickDamageDepth != 0 || _reactionEffectDepth != 0 || !Alive(rt.Hero) || !(info.victim is Monster victim)
                || victim.GetRelation(rt.Hero) != EntityRelation.Enemy) return;
            var hero = rt.Hero;
            var p = rt.Powers;
            string memory = MemorySource(info.actor);
            if (memory != null)
            {
                var skill = FindMemory(hero, memory);
                if (skill != null) ReduceNormalMemory(hero, skill, p.ReturningBladeCooldownFraction(true) * 100f);
            }
            bool summonKill = info.actor != null && info.actor.FindFirstOfType<Summon>() is Summon summon
                && summon.FindFirstAncestorOfType<Hero>() == hero;
            PowerShield(rt, hero, Power.PackFeast, p.PackFeastShield(hero.maxHealth, summonKill), 4f);
            Hero nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var ally in LivingAllies(hero, 0f))
            {
                float d = (ally.agentPosition - hero.agentPosition).sqrMagnitude;
                if (d < distance) { nearest = ally; distance = d; }
            }
            if (nearest != null) ReduceNormalMemories(nearest, p.TakeRelayHand(Time.time, nearest.GetInstanceID()) * 100f, longestOnly: true);
            var st = victim.Status;
            int types = (st.fireStack > 0 ? 1 : 0) + (st.hasCold ? 1 : 0) + (st.lightStack > 0 ? 1 : 0) + (st.darkStack > 0 ? 1 : 0);
            QueuePowerDamage(rt, null, victim.position, 4f, p.ElementalHarvestDamage(Higher(hero), types), except: victim);
            if (rt.NewPowers.Overkill.TryGetValue(victim.GetInstanceID(), out float overkill) && overkill > 0f)
            {
                float amount = p.SpilloverDamage(overkill + 1f, 1f);
                QueueSpillover(rt, victim, amount);
            }
            rt.NewPowers.Overkill.Remove(victim.GetInstanceID());
            if (st.darkStack >= 2 && p.Build.Get(Power.UmbralHeritage) > 0)
            {
                Vector3 center = victim.position;
                rt.NewPowers.Pending.Add(() => SpreadUmbral(rt, victim, center));
            }
        }

        private void QueueSpillover(HeroRuntime rt, Entity dead, float amount)
        {
            if (amount <= 0f) return;
            Entity nearest = null;
            float distance = 36f;
            if (_am == null) return;
            foreach (var enemy in _am.allEntities)
            {
                if (enemy == dead || !enemy.isActive || enemy.currentHealth <= 0f || enemy.GetRelation(rt.Hero) != EntityRelation.Enemy) continue;
                float d = (enemy.position - dead.position).sqrMagnitude;
                if (d <= distance) { nearest = enemy; distance = d; }
            }
            if (nearest != null) QueuePowerDamage(rt, nearest, nearest.position, 0f, amount, pure: true);
        }

        private void SpreadUmbral(HeroRuntime rt, Entity dead, Vector3 center)
        {
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, center, 6f, EnemyFilter, rt.Hero);
            _reactionEffectDepth++;
            try
            {
                int count = 0;
                foreach (var enemy in found)
                {
                    if (enemy == dead || !enemy.isActive || enemy.currentHealth <= 0f) continue;
                    if (count++ >= 5) break;
                    if (rt.Powers.RollUmbralHeritage(2, _rng.NextDouble())) rt.Hero.ApplyElemental(ElementalType.Dark, enemy, 1);
                }
            }
            finally { _reactionEffectDepth--; handle.Return(); }
        }

        private void OnNewPowerElement(HeroRuntime rt, EventInfoApplyElemental info)
        {
            var status = info.victim.Status;
            int element = info.type == ElementalType.Fire ? 0 : info.type == ElementalType.Cold ? 1 : info.type == ElementalType.Light ? 2 : 3;
            int after = element == 0 ? status.fireStack : element == 1 ? (status.hasCold ? 1 : 0) : element == 2 ? status.lightStack : status.darkStack;
            var application = ElementApplicationContext.Current;
            // addedStack is the requested count, not the actual capped increment. Only a native pre-snapshot proves a crossing.
            int before = application != null && application.Matches(info) ? application.Before : after;
            var result = rt.Powers.OnElementApplied(Time.time, info.victim.GetInstanceID(), element,
                before, after, rt.Hero.maxHealth);
            ReduceNormalMemories(rt.Hero, result.LongestCooldownFraction * 100f, longestOnly: true);
            PowerShield(rt, rt.Hero, Power.PrismShift, result.Shield, 3f);
        }

        private void OnPersonalDreamEvent(DreamforgeDreamEventStartedMsg message, DewPlayer caller)
        {
            var run = ClientSession.HostRun;
            if (message == null || message.protocol != Protocol.Version || caller == null || run == null
                || !run.AwaitingChoice || message.runId != run.RunId || message.generation != run.WaypointGeneration
                || message.dreamEvent == (int)DreamEvent.None || !Enum.IsDefined(typeof(DreamEvent), message.dreamEvent)
                || !Alive(caller.hero) || !_runtimes.TryGetValue(caller.hero, out var rt)) return;
            var state = rt.NewPowers;
            if (state.OmenRun == message.runId && state.OmenGeneration >= message.generation) return;
            state.OmenRun = message.runId; state.OmenGeneration = message.generation;
            ReduceNormalMemories(rt.Hero, rt.Powers.DreamOmenCooldownFraction * 100f);
            PowerShield(rt, rt.Hero, Power.DreamOmen, rt.Powers.DreamOmenShield(rt.Hero.maxHealth), 10f);
        }
    }
}
