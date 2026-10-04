using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private Waypoints.Totals ActiveWaypointTotals => Waypoints.Sum(ClientSession.HostRun?.ActiveWaypoint ?? Waypoint.None);
        private RunState _modifierRun;
        private Waypoint _modifierWaypoint;
        private int _modifierDepth = -1;
        private int _partyDepthSeen = -1;
        private bool _resolvingWaypointCombat;

        private sealed class WaypointHeroRuntime
        {
            public DataProcessor<HealData, Actor, Entity> Heal, Shield;
            public DataProcessor<FinalStats> Health;
            public Action<EventInfoSkillUse> Skill;
            public Action<EventInfoSummon> OnSummon;
            public readonly Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>> Summons =
                new Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>>();
        }

        private readonly Dictionary<Hero, WaypointHeroRuntime> _waypointHeroes = new Dictionary<Hero, WaypointHeroRuntime>();
        private readonly List<Hero> _waypointHeroScratch = new List<Hero>();
        private readonly List<Hero> _waypointAddScratch = new List<Hero>();
        private readonly List<Summon> _waypointSummonScratch = new List<Summon>();

        private void EnsureWaypointCombatChoice()
        {
            if (_resolvingWaypointCombat || _gimmickDamageDepth != 0 || _reactionEffectDepth != 0
                || ClientSession.HostRun?.AwaitingChoice != true) return;
            _resolvingWaypointCombat = true;
            try
            {
                // 純白の入口は戦闑では選択を解決できない。確定しないままでも、必須の敵初期化だけは進める。
                if (!ClientSession.CommitHostCombatChoice() && !ClientSession.HostCombatChoiceSuspended) return;
                RefreshRunModifiers();
                RefreshPressure();
                ProcessSpawns();
            }
            finally { _resolvingWaypointCombat = false; }
        }

        private void RefreshRunModifiers()
        {
            var run = ClientSession.HostRun;
            int depth = run?.DreamDepth ?? 0;
            var waypoint = run?.ActiveWaypoint ?? Waypoint.None;
            if (!ReferenceEquals(run, _modifierRun) || depth != _modifierDepth || waypoint != _modifierWaypoint)
            {
                _modifierRun = run;
                _modifierDepth = depth;
                _modifierWaypoint = waypoint;
                _pressureDirty = true;
                foreach (var hero in _waypointHeroes.Keys)
                {
                    if (hero == null || hero.Status == null) continue;
                    hero.Status.MarkStatsDirty();
                    hero.Status.CalculateStatsIfDirty();
                }
            }
            AlignDepthBonuses(PartyDepth());
        }

        /// <summary>
        /// 潜行が深まったとき（確保／潜行の確定・協力プレイの深いBuildの到着）だけ、初期化済みの生存敵の
        /// 深度ボーナスを新しい深度へ置き換える。浅く確保しても下げないので、撃破報酬の深度と足並みが揃い、
        /// 二重適用も起きない。現在HPは保存されるため、戦闘中の置き換えで回復しない。
        /// </summary>
        private void AlignDepthBonuses(int partyDepth)
        {
            if (partyDepth <= _partyDepthSeen)
            {
                if (partyDepth < _partyDepthSeen) _partyDepthSeen = partyDepth;
                return;
            }
            bool firstSighting = _partyDepthSeen < 0;
            _partyDepthSeen = partyDepth;
            if (firstSighting) return; // 出現処理が現在の深度で初期化するため、置き換えは不要
            _monsterScratch.Clear();
            foreach (var kv in _monsters)
                if (kv.Value.SpawnProcessed && SpawnInitRules.RealignsDepthBonus(kv.Value.DepthApplied, partyDepth))
                    _monsterScratch.Add(kv.Key);
            foreach (var m in _monsterScratch)
            {
                if (m == null || !m.isActive || m.Status == null || m.Status.maxHealth <= 0
                    || !_monsters.TryGetValue(m, out var rt) || !rt.SpawnProcessed
                    || !SpawnInitRules.RealignsDepthBonus(rt.DepthApplied, partyDepth)) continue;
                try { ApplyDepthBonus(rt, (MonsterTier)Math.Min((int)MonsterTier.Boss, (int)m.type), partyDepth); }
                catch (Exception ex) { Log.Error("Host: align depth bonus " + ex); }
            }
        }

        private void SyncWaypointHeroes()
        {
            // Shared combat rules apply to every active hero; an accepted Build is not required (equipment powers are).
            _waypointHeroScratch.Clear();
            _waypointAddScratch.Clear();
            WaypointRoster.Diff(_waypointHeroes.Keys, _am != null ? _am.allHeroes : (IEnumerable<Hero>)Array.Empty<Hero>(),
                hero => hero != null && hero.isActive && hero.Status != null, _waypointAddScratch, _waypointHeroScratch);
            foreach (var hero in _waypointHeroScratch) RemoveWaypointHero(hero);
            foreach (var kv in _waypointHeroes) PruneWaypointSummons(kv.Value);
            foreach (var hero in _waypointAddScratch)
            {
                var effects = new WaypointHeroRuntime
                {
                    // Receiver hooks include heals/shields from the isolated serverActor path.
                    Heal = (ref HealData data, Actor actor, Entity target) =>
                        ScaleWaypointRecovery(ref data, ActiveWaypointTotals.HealingMultiplier),
                    Shield = (ref HealData data, Actor actor, Entity target) =>
                        ScaleWaypointRecovery(ref data, ActiveWaypointTotals.ShieldMultiplier),
                    Health = (ref FinalStats stats) => stats.maxHealth *= (float)ActiveWaypointTotals.HeroHealthMultiplier,
                };
                var captured = hero;
                // Heroes with an accepted Build get these two rules from their runtime (OnSkillUse and HookSummon).
                effects.Skill = info => { if (!_runtimes.ContainsKey(captured)) ApplyWaypointMemoryCooldown(captured, info); };
                effects.OnSummon = info => HookWaypointSummon(captured, effects, info.summon);
                _waypointHeroes.Add(hero, effects);
                hero.takenHealProcessor.Add(effects.Heal, int.MaxValue);
                hero.takenShieldProcessor.Add(effects.Shield, int.MaxValue);
                hero.Status.finalStatsProcessors.Add(effects.Health, int.MaxValue);
                hero.ClientHeroEvent_OnSkillUse += effects.Skill;
                hero.ActorEvent_OnSpawnSummon += effects.OnSummon;
                if (_am != null)
                    foreach (var entity in _am.allEntities)
                        if (entity is Summon summon) HookWaypointSummon(hero, effects, summon);
                hero.Status.CalculateStatsIfDirty();
            }
            _waypointAddScratch.Clear();
        }

        private void HookWaypointSummon(Hero hero, WaypointHeroRuntime effects, Summon summon)
        {
            if (summon == null || !summon.isActive || effects.Summons.ContainsKey(summon)
                || summon.FindFirstAncestorOfType<Hero>() != hero) return;
            DataProcessor<DamageData, Actor, Entity> processor = (ref DamageData damage, Actor source, Entity target) =>
            {
                if (_runtimes.ContainsKey(hero) || source == null || source.FindFirstOfType<Summon>() != summon) return;
                damage.ApplyAmplification((float)ActiveWaypointTotals.SummonPowerMultiplier - 1f);
            };
            effects.Summons.Add(summon, processor);
            summon.dealtDamageProcessor.Add(processor);
        }

        private void PruneWaypointSummons(WaypointHeroRuntime effects)
        {
            if (effects.Summons.Count == 0) return;
            _waypointSummonScratch.Clear();
            foreach (var kv in effects.Summons)
                if (kv.Key == null || !kv.Key.isActive) _waypointSummonScratch.Add(kv.Key);
            foreach (var summon in _waypointSummonScratch)
            {
                if (summon != null) summon.dealtDamageProcessor.Remove(effects.Summons[summon]);
                effects.Summons.Remove(summon);
            }
            _waypointSummonScratch.Clear();
        }

        private static void ScaleWaypointRecovery(ref HealData data, double multiplier)
        {
            // These multipliers include native flat additions; ApplyRawMultiplier changes only originalAmount.
            if (multiplier < 1) data.ApplyReduction((float)(1 - multiplier));
            else if (multiplier > 1) data.ApplyAmplification((float)(multiplier - 1));
        }

        private void RemoveWaypointHero(Hero hero)
        {
            if (ReferenceEquals(hero, null) || !_waypointHeroes.TryGetValue(hero, out var effects)) return;
            _waypointHeroes.Remove(hero);
            if (hero == null) return;
            hero.takenHealProcessor.Remove(effects.Heal);
            hero.takenShieldProcessor.Remove(effects.Shield);
            try { hero.ClientHeroEvent_OnSkillUse -= effects.Skill; } catch (Exception ex) { Log.Error("Host: waypoint skill unhook " + ex.Message); }
            try { hero.ActorEvent_OnSpawnSummon -= effects.OnSummon; } catch (Exception ex) { Log.Error("Host: waypoint summon unhook " + ex.Message); }
            foreach (var kv in effects.Summons)
                if (kv.Key != null) kv.Key.dealtDamageProcessor.Remove(kv.Value);
            effects.Summons.Clear();
            if (hero.Status != null)
            {
                hero.Status.finalStatsProcessors.Remove(effects.Health);
                hero.Status.CalculateStatsIfDirty();
            }
        }

        private void ClearWaypointHeroes()
        {
            _waypointHeroScratch.Clear();
            _waypointHeroScratch.AddRange(_waypointHeroes.Keys);
            foreach (var hero in _waypointHeroScratch) RemoveWaypointHero(hero);
            _waypointHeroScratch.Clear();
            _modifierRun = null;
            _modifierDepth = -1;
            _modifierWaypoint = Waypoint.None;
            _partyDepthSeen = -1;
        }

        private void ApplyWaypointMemoryCooldown(Hero hero, EventInfoSkillUse info)
        {
            var skill = info.skill;
            double multiplier = ActiveWaypointTotals.MemoryCooldownMultiplier;
            if (multiplier >= 1 || skill == null || skill.type != SkillType.Normal
                || info.type == HeroSkillLocation.Movement || info.type == HeroSkillLocation.Identity) return;
            // OnSkillUse follows the native cooldown assignment. Respect its reduction opt-out.
            float ratio = Gimmicks.RemainingCooldownReductionRatio(skill.currentConfigUnscaledCooldownTime,
                skill.currentConfigUnscaledMaxCooldownTime, (int)Math.Round((1 - multiplier) * 100));
            if (ratio > 0) hero.ApplyCooldownReductionByRatio(skill, ratio, false);
        }

        private NightmareAffix RollWaypointNightmare(MonsterTier tier, int depth, double chanceMultiplier)
        {
            var waypoint = ActiveWaypointTotals;
            if (waypoint.AllNightmares)
            {
                // One native nightmare trait keeps the opt-in rule bounded even at zero delve heat.
                return Nightmares.AllAffixes[_rng.Range(0, Nightmares.AllAffixes.Length - 1)];
            }
            int selectionDepth = waypoint.NightmareChanceMultiplier > 1 ? Math.Max(1, depth) : depth;
            return Nightmares.Roll(_rng, tier, selectionDepth, chanceMultiplier * waypoint.NightmareChanceMultiplier);
        }
    }
}
