using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private bool _rangeToSpeedEnabled = true;
        private bool _distanceFalloffEnabled = true;
        private bool _meleeBreakEnabled = true;
        private bool _meleeGuardEnabled = true;
        private readonly Dictionary<Entity, MeleeBreakState> _meleeBreaks = new Dictionary<Entity, MeleeBreakState>();
        private readonly List<Entity> _expiredMeleeBreaks = new List<Entity>();

        private sealed class MeleeBreakState
        {
            internal Hero Source;
            internal float Until;
            internal DataProcessor<DamageData, Actor, Entity> Processor;
        }

        internal void ConfigureMeleeCoop(bool rangeToSpeed, bool distanceFalloff, bool meleeBreak, bool meleeGuard)
        {
            _rangeToSpeedEnabled = rangeToSpeed;
            if (_distanceFalloffEnabled != distanceFalloff) CoopDistanceNative.Clear();
            _distanceFalloffEnabled = distanceFalloff;
            CoopDistanceNative.Enabled = distanceFalloff;
            _meleeBreakEnabled = meleeBreak;
            _meleeGuardEnabled = meleeGuard;
            if (!meleeBreak) ClearMeleeBreaks();
        }

        /// <summary>The four toggles as sent in the hello reply, so clients display what the host applies.</summary>
        internal int MeleeCoopWire => MeleeCoopSettings.Encode(_rangeToSpeedEnabled, _distanceFalloffEnabled,
            _meleeBreakEnabled, _meleeGuardEnabled);

        private Room _coopRoom;

        private void ScanCoopDistances()
        {
            try
            {
                var room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
                if (_coopRoom != room)
                {
                    ClearMeleeCoop();
                    _coopRoom = room;
                }
                var actors = NetworkedManagerBase<ActorManager>.softInstance;
                if (actors == null) { CoopDistanceNative.Clear(); return; }
                foreach (var source in actors.allHeroes)
                    foreach (var target in actors.allHeroes)
                        if (source != null && target != null && source != target)
                            CoopDistanceNative.Coefficient(source, target);
            }
            catch (Exception ex)
            {
                CoopDistanceNative.Clear();
                Log.Warn("Host: ally distance history unavailable for this scan: " + ex.Message);
            }
        }

        /// <summary>Build 適用で HeroRuntime を作った直後に呼ぶ。イベント登録から初回走査
        /// （最大 ScanSeconds 後）までの命中でも、崩しの判定が既定値 false のまま近接を
        /// 見逃さないように、近接種別を先に確定させる（#1130）。</summary>
        internal void InitializeMeleeTraveler(HeroRuntime rt) => rt.IsMeleeTraveler = TravelerCombatNative.IsMelee(rt.Hero);

        private void ScanMeleeCoop(HeroRuntime rt, bool alive)
        {
            // 崩しの命中経路はこの判定を使い回す（被弾・攻撃ごとに攻撃アセットを読み直さない）。
            // 差し替えは次の走査で反映され、MeleeGuard と同じ間隔になる。どの機能の有効フラグ
            // （射程→攻撃速度変換・崩し・踏み込み）にも載せず、走査のたびに必ず更新する。
            rt.IsMeleeTraveler = TravelerCombatNative.IsMelee(rt.Hero);
            bool melee = alive && rt.IsMeleeTraveler;
            try
            {
                float converted = _rangeToSpeedEnabled && melee
                    ? StatUnits.ToGame(Stat.AttackRangePct, rt.Powers.Build.Get(Stat.AttackRangePct)) : 0f;
                if (rt.BaseBonus != null && rt.ConvertedAttackRange != converted)
                {
                    float delta = converted - rt.ConvertedAttackRange;
                    rt.BaseBonus.attackRangePercentage -= delta;
                    rt.BaseBonus.attackSpeedPercentage = Math.Min(
                        StatUnits.ToGame(Stat.AttackSpeedPct, Content.StatCap(Stat.AttackSpeedPct)),
                        StatUnits.ToGame(Stat.AttackSpeedPct, rt.Powers.Build.Get(Stat.AttackSpeedPct))
                            + converted * CoopBalance.RangeToSpeedMultiplier);
                    rt.ConvertedAttackRange = converted;
                    rt.Hero.Status.CalculateStatsIfDirty();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Host: melee range conversion unavailable for this scan: " + ex.Message);
            }
            try
            {
                // Hits reuse this result (up to ScanSeconds old) instead of re-checking the attack per hit.
                rt.MeleeGuard = _meleeGuardEnabled && melee
                    && CountEnemiesNear(rt.Hero, CoopBalance.MeleeGuardRange) > 0;
            }
            catch (Exception ex)
            {
                rt.MeleeGuard = false;
                Log.Warn("Host: melee proximity unavailable for this scan: " + ex.Message);
            }
        }

        private void ApplyMeleeGuard(HeroRuntime rt, ref DamageData damage, Actor actor)
        {
            try
            {
                if (!_meleeGuardEnabled || !rt.MeleeGuard) return;
                if ((damage.actor ?? actor)?.firstEntity == rt.Hero) return;
                damage.ApplyReduction(CoopBalance.MeleeGuardReductionPercent / 100f);
            }
            catch (Exception ex)
            {
                rt.MeleeGuard = false;
                Log.Warn("Host: melee proximity unavailable for this hit: " + ex.Message);
            }
        }

        private static bool IsNativeBasicDamage(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
            {
                if (actor is Gem || actor is AbilityInstance instance && instance.gem != null) return false;
                if (actor is AttackTrigger || actor is MeleeAttackInstance || actor is AttackProjectile) return true;
                if (actor is SkillTrigger) return false;
            }
            return false;
        }

        /// <summary>Callers check <c>_meleeBreakEnabled</c> and the melee traveler first. Only
        /// <see cref="HostAuthority.OnMemoryDamage"/> calls this in production; the assert (dev builds
        /// only, compiled out in release) catches a future caller that skips the melee check (#542).</summary>
        private void MarkMeleeBreak(Hero hero, Entity victim)
        {
            if (!_meleeBreakEnabled || !Alive(hero)
                || victim == null || !victim.isActive || victim.currentHealth <= 0f
                || victim.GetRelation(hero) != EntityRelation.Enemy) return;
            // The caller's gate is the scanned classification, which may lag a basic-attack swap
            // by up to ScanSeconds, so check the swap-invariant traveler designation (#542, #1130).
            Debug.Assert(TravelerCombat.IsMelee(hero.GetType().Name, true),
                "MarkMeleeBreak on a traveler who is never melee; callers must check the melee classification first");
            try
            {
                if (!_meleeBreaks.TryGetValue(victim, out var state))
                {
                    state = new MeleeBreakState();
                    var captured = state;
                    state.Processor = (ref DamageData damage, Actor actor, Entity target) =>
                    {
                        try
                        {
                            if (!_meleeBreakEnabled || Time.time >= captured.Until || captured.Source == null) return;
                            var attacker = (damage.actor ?? actor)?.firstEntity as Hero;
                            if (attacker == null || attacker == captured.Source
                                || attacker.GetRelation(captured.Source) != EntityRelation.Ally
                                || target.GetRelation(attacker) != EntityRelation.Enemy) return;
                            damage.ApplyAmplification(CoopBalance.BreakDamagePercent / 100f);
                        }
                        catch (Exception ex)
                        {
                            captured.Until = 0f;
                            Log.Warn("Host: melee break unavailable for this hit: " + ex.Message);
                        }
                    };
                    victim.takenDamageProcessor.Add(state.Processor);
                    _meleeBreaks.Add(victim, state);
                }
                // One active source per enemy: refresh/replacement never stacks the bonus.
                state.Source = hero;
                state.Until = Time.time + CoopBalance.BreakSeconds;
            }
            catch (Exception ex) { Log.Warn("Host: melee break unavailable for this victim: " + ex.Message); }
        }

        private void PruneMeleeBreaks()
        {
            _expiredMeleeBreaks.Clear();
            foreach (var pair in _meleeBreaks)
                if (pair.Key == null || !pair.Key.isActive || pair.Key.currentHealth <= 0f || Time.time >= pair.Value.Until)
                    _expiredMeleeBreaks.Add(pair.Key);
            foreach (var victim in _expiredMeleeBreaks)
            {
                try { if (victim != null) victim.takenDamageProcessor.Remove(_meleeBreaks[victim].Processor); }
                catch (Exception) { }
                _meleeBreaks.Remove(victim);
            }
        }

        private void ClearMeleeBreaks()
        {
            foreach (var pair in _meleeBreaks)
                try { if (pair.Key != null) pair.Key.takenDamageProcessor.Remove(pair.Value.Processor); }
                catch (Exception) { }
            _meleeBreaks.Clear();
            _expiredMeleeBreaks.Clear();
        }

        private void ClearMeleeCoop()
        {
            ClearMeleeBreaks();
            CoopDistanceNative.Clear();
            foreach (var rt in _runtimes.Values) rt.MeleeGuard = false;
        }
    }
}
