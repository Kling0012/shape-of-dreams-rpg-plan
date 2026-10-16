using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // The constructor runs after both processor chains and authored final damage, before armor/overkill.
    // Multiplying here covers flat damage too, without changing proc coefficients or elemental stacks.
    [HarmonyPatch(typeof(FinalDamageData), MethodType.Constructor,
        new[] { typeof(DamageData), typeof(float), typeof(Entity) })]
    internal static class NativeCatchupDamage
    {
        private static void Prefix(ref DamageData data, Entity victim)
        {
            if (NetworkServer.active)
                HostAuthority.NativeInstance?.ApplyCatchupDamage(ref data, victim);
        }
    }

    internal sealed partial class HostAuthority
    {
        internal static bool CatchupEnabled = true;
        internal static bool CatchupHooksAvailable = true;
        private readonly Dictionary<DewPlayer, CoopCatchup> _catchupPlayers = new Dictionary<DewPlayer, CoopCatchup>();
        private Room _catchupRoom;
        private string _catchupRun;
        private int _catchupZone = -1, _catchupNode = -1, _catchupRoomIndex = -1;
        private long _catchupGraph;
        private bool _catchupLocked, _catchupPendingBuilds, _catchupFailed;
        private bool? _catchupSentSetting;
        private uint[] _catchupPlayerIds = Array.Empty<uint>();
        private float[] _catchupOutgoing = Array.Empty<float>(), _catchupIncoming = Array.Empty<float>();

        partial void TickCatchup(bool pressureChanged)
        {
            if (!NetworkServer.active || !CatchupHooksAvailable || _catchupFailed) return;
            try
            {
                if (_catchupSentSetting != CatchupEnabled)
                {
                    _catchupSentSetting = CatchupEnabled;
                    SendPressure();
                }
                var run = NetworkedManagerBase<GameManager>.softInstance?.runId;
                if (string.IsNullOrEmpty(run) || _zone == null || _zone.currentRoom == null) return;
                if (_zone.isInAnyTransition || ClientSession.NativeContinueRestoring) return;
                long graph = ClientSession.HostRun?.Infinity?.GraphEpoch ?? 0;
                bool boundary = run != _catchupRun || !ReferenceEquals(_catchupRoom, _zone.currentRoom)
                    || _catchupZone != _zone.currentZoneIndex || _catchupNode != _zone.currentNodeIndex
                    || _catchupRoomIndex != _zone.currentRoomIndex || _catchupGraph != graph;
                if (!boundary && (!_catchupPendingBuilds || _catchupLocked || !pressureChanged)) return;
                // Pressure may synchronize here: retain the old snapshot identity until recapture is ready.
                RefreshPressure();
                _catchupRoom = _zone.currentRoom;
                _catchupRun = run;
                _catchupZone = _zone.currentZoneIndex;
                _catchupNode = _zone.currentNodeIndex;
                _catchupRoomIndex = _zone.currentRoomIndex;
                _catchupGraph = graph;
                if (boundary) _catchupLocked = false;
                CaptureCatchup();
            }
            catch (Exception ex) { DisableCatchup(ex); }
        }

        private void CaptureCatchup()
        {
            _catchupPlayers.Clear();
            _catchupPendingBuilds = false;
            foreach (var player in _pressurePlayers)
            {
                var build = _builds.TryGetValue(player, out var received) ? received.Build : null;
                if (build == null) _catchupPendingBuilds = true;
                // Missing data must not masquerade as a confirmed low-growth player.
                _catchupPlayers[player] = build == null ? CoopCatchup.Neutral
                    : CoopCatchup.ForPlayer(_pressure, build.DreamLevel, build.SpentStarPoints, _pressurePlayerCount);
            }
            int count = _catchupPlayers.Count;
            _catchupPlayerIds = new uint[count];
            _catchupOutgoing = new float[count];
            _catchupIncoming = new float[count];
            int index = 0;
            foreach (var pair in _catchupPlayers)
            {
                _catchupPlayerIds[index] = pair.Key.netId;
                _catchupOutgoing[index] = (float)pair.Value.OutgoingMultiplier;
                _catchupIncoming[index++] = (float)pair.Value.IncomingMultiplier;
            }
            SendPressure();
        }

        internal void ApplyCatchupDamage(ref DamageData damage, Entity target)
        {
            if (_catchupFailed || !CatchupHooksAvailable || !NetworkServer.active || target == null || damage.currentAmount <= 0) return;
            try
            {
                // Use the dispatch actor as authority; packet.actor can describe an inherited effect.
                var context = NativeDamageContext.Current;
                var actor = context != null && context.Target == target ? context.Actor : damage.actor;
                if (actor == null) return;
                var source = CatchupSource(actor);
                var hero = source as Hero;
                bool outgoing = hero != null && target.GetRelation(hero) == EntityRelation.Enemy;
                var victim = target as Hero;
                bool incoming = victim != null && source != victim
                    && (source == null || source.GetRelation(victim) != EntityRelation.Ally);
                if (!outgoing && !incoming) return;
                // A late first build can be admitted only before combat begins, never midway through it.
                TickCatchup(_pressureDirty);
                if (_zone == null || _zone.isInAnyTransition || ClientSession.NativeContinueRestoring
                    || !ReferenceEquals(_catchupRoom, _zone.currentRoom)
                    || _catchupRun != NetworkedManagerBase<GameManager>.softInstance?.runId
                    || _catchupZone != _zone.currentZoneIndex || _catchupNode != _zone.currentNodeIndex
                    || _catchupRoomIndex != _zone.currentRoomIndex
                    || _catchupGraph != (ClientSession.HostRun?.Infinity?.GraphEpoch ?? 0)) return;
                _catchupLocked = true;
                if (!CatchupEnabled || _catchupPlayers.Count <= 1 || _pressurePlayerCount <= 1) return;
                float multiplier = 1f;
                if (outgoing && hero.owner != null && _catchupPlayers.TryGetValue(hero.owner, out var dealt))
                    multiplier *= (float)dealt.OutgoingMultiplier;
                // Self-sacrifice/health costs are not hostile damage and keep their authored amount.
                if (incoming && !IsHealthSacrifice(actor, victim) && victim.owner != null
                    && _catchupPlayers.TryGetValue(victim.owner, out var taken))
                    multiplier *= (float)taken.IncomingMultiplier;
                if (multiplier > 1f) damage.ApplyAmplification(multiplier - 1f);
                else if (multiplier < 1f) damage.ApplyReduction(1f - multiplier);
            }
            catch (Exception ex) { DisableCatchup(ex); }
        }

        private static Entity CatchupSource(Actor actor)
        {
            // CastInfo identifies the attacker even when a debuff is parented beneath its victim.
            // Bound the traversal so corrupt native parenting disables only this feature.
            for (int depth = 0; actor != null && depth < 100; depth++)
            {
                if (actor is Hero hero) return hero;
                if (actor is Summon summon && summon.info.caster != null && summon.info.caster != actor)
                    actor = summon.info.caster;
                else if (actor is AbilityInstance ability && ability.info.caster != null && ability.info.caster != actor)
                    actor = ability.info.caster;
                else if (actor is Entity entity) return entity;
                else actor = actor.parentActor;
            }
            return null;
        }

        private void DisableCatchup(Exception ex)
        {
            _catchupFailed = true;
            _catchupPlayers.Clear();
            _catchupPlayerIds = Array.Empty<uint>();
            _catchupOutgoing = _catchupIncoming = Array.Empty<float>();
            Log.Warn("Co-op catch-up disabled; other MOD features continue: " + ex.Message);
        }

        partial void AddCatchupSnapshot(DreamforgePressureMsg msg)
        {
            msg.catchupAvailable = CatchupHooksAvailable && !_catchupFailed;
            msg.catchupEnabled = CatchupEnabled;
            msg.catchupRunId = _catchupRun;
            msg.catchupZone = _catchupZone;
            msg.catchupNode = _catchupNode;
            msg.catchupRoom = _catchupRoomIndex;
            msg.catchupGraph = _catchupGraph;
            if (!CatchupEnabled || !msg.catchupAvailable || _pressurePlayerCount <= 1) return;
            msg.catchupPlayerIds = _catchupPlayerIds;
            msg.catchupOutgoing = _catchupOutgoing;
            msg.catchupIncoming = _catchupIncoming;
        }

        partial void ResetCatchup()
        {
            _catchupPlayers.Clear();
            _catchupRoom = null;
            _catchupRun = null;
            _catchupZone = _catchupNode = _catchupRoomIndex = -1;
            _catchupGraph = 0;
            _catchupFailed = _catchupLocked = _catchupPendingBuilds = false;
            _catchupSentSetting = null;
            _catchupPlayerIds = Array.Empty<uint>();
            _catchupOutgoing = _catchupIncoming = Array.Empty<float>();
        }
    }
}
