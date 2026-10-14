using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        internal static bool RoomOmensEnabled { get => RoomOmens.Enabled; set => RoomOmens.Enabled = value; }
        partial void SendRoomOmenPeer(DewPlayer player) => SendRoomOmen(player);
        private sealed class OmenHeroRuntime
        {
            internal StatBonus Bonus;
            internal DataProcessor<HealData, Actor, Entity> Heal;
        }
        private readonly Dictionary<Hero, OmenHeroRuntime> _omenHeroes = new Dictionary<Hero, OmenHeroRuntime>();
        private readonly List<Hero> _omenHeroScratch = new List<Hero>();
        private RoomOmen _roomOmen;
        private Room _omenRoom;
        private string _omenRunId;
        private bool _omensFailed, _omenSetting;
        private float _nextOmenTick;

        private void OnRoomOmenLoaded(EventInfoLoadRoom info)
        {
            _roomHasVariant = false;
            RefreshRoomOmen(force: true);
        }

        private bool EligibleOmenRoom()
        {
            if (_zone == null || _zone.currentRoom == null || _zone.isSidetracking
                || _zone.currentNodeIndex < 0 || _zone.currentNodeIndex >= _zone.nodes.Count) return false;
            var node = _zone.currentNode;
            if (node.type != WorldNodeType.Combat || InfinityMode.IsWellRoom(node)) return false;
            var sections = _zone.currentRoom.sections;
            if (sections == null) return false;
            for (int i = 0; i < sections.Count; i++)
            {
                var monsters = sections[i]?.monsters;
                if (monsters != null && (monsters.isMarkedAsCombatArea || monsters.combatAreaSettings == SectionCombatAreaType.Yes))
                    return true;
            }
            return false;
        }

        private void TickRoomOmens()
        {
            if (Time.unscaledTime < _nextOmenTick) return;
            _nextOmenTick = Time.unscaledTime + 0.25f;
            RefreshRoomOmen(force: false);
        }

        private void RefreshRoomOmen(bool force)
        {
            if (!NetworkServer.active) return;
            try
            {
                var run = ClientSession.HostRun;
                if (_omensFailed || !RoomOmensEnabled || run == null)
                {
                    if (_roomOmen != RoomOmen.None || _omenHeroes.Count > 0) ClearRoomOmen();
                    _omenRoom = null;
                    _omenRunId = null;
                    _omenSetting = false;
                    return;
                }
                var room = _zone?.currentRoom;
                bool publish = false;
                if (force || !ReferenceEquals(room, _omenRoom) || _omenRunId != run.RunId || !_omenSetting)
                {
                    if (!force && _zone != null && _zone.isInAnyTransition) return;
                    ClearRoomOmen();
                    _omenRoom = room;
                    _omenRunId = run.RunId;
                    _omenSetting = true;
                    if (EligibleOmenRoom()) _roomOmen = RoomOmens.Roll(Rng.SeedFrom(run.RunId), _zone.currentRoomIndex);
                    RoomOmens.SetKillModifier(this, _roomOmen);
                    foreach (var monster in _monsters.Values) ApplyRoomOmenMonster(monster);
                    publish = true;
                }
                SyncRoomOmenHeroes();
                if (publish)
                {
                    ClientSession.SetHostRoomOmen(_roomOmen, run.RunId, _zone?.currentRoomIndex ?? -1);
                    SendRoomOmen();
                }
            }
            catch (Exception ex) { DisableRoomOmens(ex); }
        }

        private void SyncRoomOmenHeroes()
        {
            _omenHeroScratch.Clear();
            foreach (var pair in _omenHeroes)
                if (pair.Key == null || !pair.Key.isActive || pair.Key.owner == null || !pair.Key.owner.isHumanPlayer
                    || _am == null || !_am.allHeroes.Contains(pair.Key)) _omenHeroScratch.Add(pair.Key);
            foreach (var hero in _omenHeroScratch) RemoveRoomOmenHero(hero);
            if (_roomOmen == RoomOmen.None || _am == null) return;
            foreach (var hero in _am.allHeroes)
            {
                if (hero == null || !hero.isActive || hero.Status == null || hero.owner == null
                    || !hero.owner.isHumanPlayer || _omenHeroes.ContainsKey(hero)) continue;
                ApplyRoomOmenHero(hero);
            }
        }

        private void ApplyRoomOmenHero(Hero hero)
        {
            float scale = RoomOmens.IsTrial(_roomOmen) ? (float)RoomOmens.TrialScale(ClientSession.HeroKeyOf(hero)) : 1f;
            float value = (float)RoomOmens.Value(_roomOmen) * scale;
            var state = new OmenHeroRuntime();
            _omenHeroes.Add(hero, state);
            switch (_roomOmen)
            {
                case RoomOmen.Vigor: state.Bonus = new StatBonus { attackDamagePercentage = value, abilityPowerPercentage = value }; break;
                case RoomOmen.Swiftness:
                case RoomOmen.Sluggishness: state.Bonus = new StatBonus { movementSpeedPercentage = value }; break;
                case RoomOmen.Focus:
                case RoomOmen.Dulling: state.Bonus = new StatBonus { abilityHasteFlat = value }; break;
                case RoomOmen.Vitality:
                case RoomOmen.Feebleness: state.Bonus = new StatBonus { maxHealthPercentage = value }; break;
                case RoomOmen.Fortitude:
                case RoomOmen.Fragility: state.Bonus = new StatBonus { armorFlat = value }; break;
                case RoomOmen.Alacrity:
                case RoomOmen.Weariness: state.Bonus = new StatBonus { attackSpeedPercentage = value }; break;
                case RoomOmen.Healing:
                case RoomOmen.Drought:
                    state.Heal = (ref HealData data, Actor actor, Entity target) => ScaleWaypointRecovery(ref data, 1 + value / 100f);
                    hero.takenHealProcessor.Add(state.Heal, int.MaxValue);
                    break;
            }
            if (state.Bonus != null) hero.Status.AddStatBonus(state.Bonus);
            hero.Status.CalculateStatsIfDirty();
        }

        private void RemoveRoomOmenHero(Hero hero)
        {
            if (!_omenHeroes.TryGetValue(hero, out var state)) return;
            if (hero != null)
            {
                if (state.Bonus != null && hero.Status != null) hero.Status.RemoveStatBonus(state.Bonus);
                if (state.Heal != null) hero.takenHealProcessor.Remove(state.Heal);
                hero.Status?.CalculateStatsIfDirty();
            }
            _omenHeroes.Remove(hero);
        }

        private void ApplyRoomOmenMonster(MonsterRuntime rt)
        {
            if (_omensFailed || rt.OmenBonus != null || rt.Monster == null || rt.Monster is BossMonster
                || rt.Monster.owner == null || rt.Monster.owner.isHumanPlayer || rt.Monster.Status == null
                || !rt.Monster.isActive || rt.Monster.Status.maxHealth <= 0
                || _roomOmen != RoomOmen.RestlessEnemies && _roomOmen != RoomOmen.SturdyEnemies) return;
            try
            {
                float scale = 1f;
                // Shared enemies use the least severe receiver coefficient in the active party.
                if (_am != null)
                    foreach (var hero in _am.allHeroes)
                        if (hero != null && hero.owner != null && hero.owner.isHumanPlayer)
                            scale = Math.Min(scale, (float)RoomOmens.TrialScale(ClientSession.HeroKeyOf(hero)));
                float value = (float)RoomOmens.Value(_roomOmen) * scale;
                rt.OmenBonus = _roomOmen == RoomOmen.RestlessEnemies
                    ? new StatBonus { movementSpeedPercentage = value } : new StatBonus { maxHealthPercentage = value };
                rt.Monster.Status.AddStatBonus(rt.OmenBonus);
                rt.Monster.Status.CalculateStatsIfDirty();
            }
            catch (Exception ex) { DisableRoomOmens(ex); }
        }

        private void RemoveRoomOmenMonster(MonsterRuntime rt)
        {
            if (rt.OmenBonus == null) return;
            if (rt.Monster != null && rt.Monster.Status != null)
            {
                rt.Monster.Status.RemoveStatBonus(rt.OmenBonus);
                rt.Monster.Status.CalculateStatsIfDirty();
            }
            rt.OmenBonus = null;
        }

        private void ClearRoomOmen()
        {
            _roomOmen = RoomOmen.None;
            RoomOmens.SetKillModifier(this, RoomOmen.None);
            _omenHeroScratch.Clear();
            _omenHeroScratch.AddRange(_omenHeroes.Keys);
            foreach (var hero in _omenHeroScratch)
                try { RemoveRoomOmenHero(hero); } catch (Exception ex) { Log.Warn("Room Omen cleanup: " + ex.Message); }
            foreach (var rt in _monsters.Values)
                try { RemoveRoomOmenMonster(rt); } catch (Exception ex) { Log.Warn("Room Omen cleanup: " + ex.Message); }
            try { ClientSession.SetHostRoomOmen(RoomOmen.None, _omenRunId, _zone?.currentRoomIndex ?? -1); }
            catch (Exception ex) { Log.Warn("Room Omen display cleanup: " + ex.Message); }
            try { SendRoomOmen(); }
            catch (Exception ex) { Log.Warn("Room Omen sync cleanup: " + ex.Message); }
        }

        private void DisableRoomOmens(Exception error)
        {
            if (_omensFailed) return;
            _omensFailed = true;
            ClearRoomOmen();
            Log.Warn("Room Omens disabled; other MOD features remain active: " + error.Message);
        }

        private void SendRoomOmen(DewPlayer target = null)
        {
            if (_registeredOn == null) return;
            var msg = new DreamforgeRoomOmenMsg { omen = (int)_roomOmen, runId = ClientSession.HostRun?.RunId,
                room = _zone?.currentRoomIndex ?? -1, zone = _zone?.currentZoneIndex ?? -1, node = _zone?.currentNodeIndex ?? -1 };
            if (target != null)
            {
                if (_roomOmenPeers.Contains(target))
                    try { _registeredOn.CustomRpc_SendMessageToClient(target, msg); } catch (Exception) { }
                return;
            }
            foreach (var peer in _roomOmenPeers)
                if (peer != null && peer != DewPlayer.local)
                    try { _registeredOn.CustomRpc_SendMessageToClient(peer, msg); } catch (Exception) { }
        }
    }
}
