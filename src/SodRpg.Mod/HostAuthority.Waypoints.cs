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
        private bool _resolvingWaypointCombat;

        private sealed class WaypointHeroRuntime
        {
            public DataProcessor<HealData, Actor, Entity> Heal, Shield;
            public DataProcessor<FinalStats> Health;
        }

        private readonly Dictionary<Hero, WaypointHeroRuntime> _waypointHeroes = new Dictionary<Hero, WaypointHeroRuntime>();
        private readonly List<Hero> _waypointHeroScratch = new List<Hero>();

        private void EnsureWaypointCombatChoice()
        {
            if (_resolvingWaypointCombat || _gimmickDamageDepth != 0 || _reactionEffectDepth != 0
                || ClientSession.HostRun?.AwaitingChoice != true) return;
            _resolvingWaypointCombat = true;
            try
            {
                if (!ClientSession.CommitHostCombatChoice()) return;
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
            if (ReferenceEquals(run, _modifierRun) && depth == _modifierDepth && waypoint == _modifierWaypoint) return;
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

        private void SyncWaypointHeroes()
        {
            _waypointHeroScratch.Clear();
            foreach (var hero in _waypointHeroes.Keys)
                if (hero == null || !hero.isActive || !_runtimes.ContainsKey(hero)) _waypointHeroScratch.Add(hero);
            foreach (var hero in _waypointHeroScratch) RemoveWaypointHero(hero);
            foreach (var hero in _runtimes.Keys)
            {
                if (hero == null || hero.Status == null || _waypointHeroes.ContainsKey(hero)) continue;
                var effects = new WaypointHeroRuntime
                {
                    // Receiver hooks include heals/shields from the isolated serverActor path.
                    Heal = (ref HealData data, Actor actor, Entity target) =>
                        ScaleWaypointRecovery(ref data, ActiveWaypointTotals.HealingMultiplier),
                    Shield = (ref HealData data, Actor actor, Entity target) =>
                        ScaleWaypointRecovery(ref data, ActiveWaypointTotals.ShieldMultiplier),
                    Health = (ref FinalStats stats) => stats.maxHealth *= (float)ActiveWaypointTotals.HeroHealthMultiplier,
                };
                _waypointHeroes.Add(hero, effects);
                hero.takenHealProcessor.Add(effects.Heal, int.MaxValue);
                hero.takenShieldProcessor.Add(effects.Shield, int.MaxValue);
                hero.Status.finalStatsProcessors.Add(effects.Health, int.MaxValue);
                hero.Status.CalculateStatsIfDirty();
            }
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
        }

        private void ApplyWaypointMemoryCooldown(HeroRuntime runtime, EventInfoSkillUse info)
        {
            var skill = info.skill;
            double multiplier = ActiveWaypointTotals.MemoryCooldownMultiplier;
            if (multiplier >= 1 || skill == null || skill.type != SkillType.Normal
                || info.type == HeroSkillLocation.Movement || info.type == HeroSkillLocation.Identity) return;
            // OnSkillUse follows the native cooldown assignment. Respect its reduction opt-out.
            float ratio = Gimmicks.RemainingCooldownReductionRatio(skill.currentConfigUnscaledCooldownTime,
                skill.currentConfigUnscaledMaxCooldownTime, (int)Math.Round((1 - multiplier) * 100));
            if (ratio > 0) runtime.Hero.ApplyCooldownReductionByRatio(skill, ratio, false);
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
