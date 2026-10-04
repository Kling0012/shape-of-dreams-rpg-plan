using System;
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// Opt-in cosmetic replacement on existing, authoritative game Monsters. No custom network
    /// prefab, damage, animation event, AI, collider, spawn or reward is created by this class.
    /// Every peer must continuously acknowledge the same pack and host epoch before activation.
    /// </summary>
    internal sealed class MobModelSession : IDisposable
    {
        private const float HeartbeatSeconds = 1f, LeaseSeconds = 6f;
        private const string HostId = "host";
        private readonly string _modPath;
        private MobModelAssets _assets;
        private bool _enabled, _faulted, _restoreFailed;
        private Actor _actor;
        private ActorManager _manager;
        private ZoneManager _zone;
        private bool _wasServer;
        private readonly Action<DreamforgeMobHelloMsg, DewPlayer> _hello;
        private readonly Action<DreamforgeMobWelcomeMsg> _welcome;
        private readonly Action<DreamforgeMobSnapshotMsg> _snapshot;
        private readonly Action<Entity> _added, _removed;
        private readonly Action<EventInfoKill> _death;
        private readonly Action<EventInfoLoadZone> _zoneLoaded;
        private readonly Action<EventInfoLoadRoom> _roomLoaded;

        private string _serverSession, _clientNonce, _remoteSession;
        private long _roomEpoch, _remoteEpoch, _revision, _generation, _helloSequence, _minimumRevision;
        private bool _pinned;
        private MobCompatibility _remoteCompatibility;
        private MobSessionGate _gate;
        private readonly MobModelSnapshotState _state = new MobModelSnapshotState();
        private float _nextHello, _nextSnapshot, _lastSnapshot;
        private int _participantSequence;
        private readonly Dictionary<DewPlayer, string> _participantIds = new Dictionary<DewPlayer, string>();
        private readonly Dictionary<string, PeerHello> _peerHellos = new Dictionary<string, PeerHello>(StringComparer.Ordinal);
        private sealed class PeerHello
        {
            public string Nonce;
            public long Sequence;
            public readonly HashSet<string> Retired = new HashSet<string>(StringComparer.Ordinal);
        }
        private readonly Dictionary<string, float> _lastReady = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<Monster, MobModelAssignment> _hostAssignments = new Dictionary<Monster, MobModelAssignment>();
        private readonly HashSet<Monster> _observed = new HashSet<Monster>();
        // Keep identity after visual restoration: a delayed removal can arrive after netId reuse.
        private readonly Dictionary<Monster, MobModelAssignment> _observedAssignments = new Dictionary<Monster, MobModelAssignment>();
        private readonly HashSet<Monster> _dead = new HashSet<Monster>();
        private readonly List<Monster> _scratch = new List<Monster>();
        private readonly List<string> _roster = new List<string>();
        private readonly List<DewPlayer> _departed = new List<DewPlayer>();
        private readonly Dictionary<Monster, AppliedModel> _applied = new Dictionary<Monster, AppliedModel>();

        private sealed class AppliedModel
        {
            public uint NetId;
            public long Generation;
            public string ModelId;
            public EntityVisual Visual;
            public EntityModel OwnedModel;
        }

        public string Status { get; private set; } = "disabled";
        private bool Ready => _enabled && !_faulted && _assets != null && _assets.Ready;
        public MobModelSession(string modPath, bool enabled)
        {
            _modPath = modPath;
            _hello = OnHello; _welcome = OnWelcome; _snapshot = OnSnapshot;
            _added = OnAdded; _removed = OnRemoved; _death = OnDeath;
            _zoneLoaded = _ => ResetRoom(); _roomLoaded = _ => ResetRoom();
            Configure(enabled);
        }

        public void Configure(bool enabled)
        {
            if (_enabled == enabled && (_assets != null || !enabled)) return;
            if (!enabled && _enabled && _actor != null && NetworkServer.active)
                try { BroadcastSnapshot(true); } catch (Exception) { }
            _enabled = enabled;
            RestoreAll();
            // Load only once per enable cycle; never retry corrupt files each frame.
            if (enabled && _assets == null) _assets = new MobModelAssets(_modPath);
            Status = enabled ? (_assets?.Error ?? "waiting for matching participants") : "disabled";
            ResetRoom();
            _nextHello = _nextSnapshot = 0;
        }

        public void Tick()
        {
            try
            {
                Wire();
                if (_actor == null) return;
                float now = Time.unscaledTime;
                if (NetworkServer.active)
                {
                    RefreshRoster(now);
                    RefreshHostAssignments();
                }
                if (NetworkClient.active && (_enabled || _pinned) && now >= _nextHello)
                {
                    _nextHello = now + HeartbeatSeconds;
                    var message = new DreamforgeMobHelloMsg
                    {
                        protocol = MobCompatibility.CurrentProtocolVersion, clientNonce = _clientNonce, sequence = ++_helloSequence,
                        sessionNonce = _pinned ? _remoteSession : null, roomEpoch = _pinned ? _remoteEpoch : 0,
                        ready = Ready && _pinned && !_state.RequiresFallback && _assets.Compatibility.Matches(_remoteCompatibility),
                        compatibilityJson = Encode(_assets?.Compatibility)
                    };
                    if (NetworkServer.active) OnHello(message, DewPlayer.local);
                    else _actor.CustomRpc_SendMessageToServer(message);
                }
                if (NetworkServer.active && (_enabled || _peerHellos.Count > 0) && now >= _nextSnapshot)
                {
                    _nextSnapshot = now + HeartbeatSeconds;
                    BroadcastSnapshot();
                }
                if (_pinned && now - _lastSnapshot > LeaseSeconds)
                {
                    BeginBootstrap();
                    Status = "host model heartbeat expired; original models";
                }
                ApplySnapshot();
            }
            catch (Exception ex) { Fail("model session: " + ex.Message); }
        }

        private void Wire()
        {
            var manager = NetworkedManagerBase<ActorManager>.softInstance;
            var actor = (NetworkClient.active || NetworkServer.active) && manager != null ? manager.serverActor : null;
            bool server = NetworkServer.active;
            if (!ReferenceEquals(actor, _actor) || server != _wasServer)
            {
                UnwireActor();
                RestoreAll();
                _actor = actor; _wasServer = server;
                _serverSession = Guid.NewGuid().ToString("N"); _roomEpoch = 1; _revision = _generation = 0;
                _participantIds.Clear(); _peerHellos.Clear(); _lastReady.Clear(); _hostAssignments.Clear(); _observedAssignments.Clear();
                ResetGate(); BeginBootstrap();
                if (actor != null)
                {
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeMobWelcomeMsg>(_welcome);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeMobSnapshotMsg>(_snapshot);
                    if (server) actor.CustomRpc_RegisterServerMessageHandler<DreamforgeMobHelloMsg>(nameof(DreamforgeMobHelloMsg), _hello);
                }
            }
            if (manager != _manager)
            {
                UnwireManager(); _manager = manager;
                if (manager != null)
                {
                    manager.ClientEvent_OnEntityAdd += _added;
                    manager.ClientEvent_OnEntityRemove += _removed;
                    foreach (var entity in manager.allEntities) OnAdded(entity);
                }
            }
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zone != _zone)
            {
                UnwireZone(); _zone = zone;
                if (zone != null)
                {
                    zone.ClientEvent_OnZoneLoaded += _zoneLoaded;
                    zone.ClientEvent_OnRoomLoaded += _roomLoaded;
                }
                ResetRoom();
            }
        }

        private void ResetGate()
        {
            _gate = null;
            if (!Ready || string.IsNullOrEmpty(_serverSession)) return;
            _gate = new MobSessionGate();
            _gate.Reset(_serverSession, _roomEpoch, _assets.Compatibility, HostId);
        }

        private void ResetRoom()
        {
            RestoreAll(); _state.Suspend(); _observedAssignments.Clear();
            if (NetworkServer.active)
            {
                _roomEpoch++; _hostAssignments.Clear(); _lastReady.Clear(); _peerHellos.Clear(); ResetGate();
            }
            BeginBootstrap();
            _nextSnapshot = 0;
        }

        private void BeginBootstrap()
        {
            RestoreAll(); _state.Suspend();
            _clientNonce = Guid.NewGuid().ToString("N"); _pinned = false;
            _nextHello = 0; _lastSnapshot = Time.unscaledTime;
        }

        private string ParticipantId(DewPlayer player)
        {
            if (player == DewPlayer.local) return HostId;
            if (!_participantIds.TryGetValue(player, out var id))
            {
                id = "peer-" + (++_participantSequence);
                _participantIds.Add(player, id);
            }
            return id;
        }

        private void RefreshRoster(float now)
        {
            _roster.Clear(); _roster.Add(HostId);
            foreach (var player in DewPlayer.gamePlayers)
                if (player != null && player.isHumanPlayer)
                {
                    string id = ParticipantId(player);
                    if (!_roster.Contains(id)) _roster.Add(id);
                }
            _departed.Clear();
            foreach (var pair in _participantIds) if (!_roster.Contains(pair.Value)) _departed.Add(pair.Key);
            foreach (var player in _departed) { _lastReady.Remove(_participantIds[player]); _peerHellos.Remove(_participantIds[player]); _participantIds.Remove(player); }
            _gate?.SetParticipants(_roster);
            foreach (string id in _roster)
                if (!_lastReady.TryGetValue(id, out float seen) || now - seen > LeaseSeconds) _gate?.Invalidate(id);
        }

        private void OnHello(DreamforgeMobHelloMsg msg, DewPlayer caller)
        {
            if (!NetworkServer.active || _actor == null || caller == null || !caller.isHumanPlayer || msg == null
                || msg.protocol != MobCompatibility.CurrentProtocolVersion || !ValidNonce(msg.clientNonce) || msg.sequence <= 0) return;
            bool present = caller == DewPlayer.local;
            foreach (var player in DewPlayer.gamePlayers) if (player == caller) present = true;
            if (!present) return;
            string id = ParticipantId(caller);
            // Old room packets must not retire a current nonce or advance its sequence.
            if (msg.sessionNonce != _serverSession || msg.roomEpoch != _roomEpoch)
            {
                // A fresh bootstrap has no pin. Other epochs are stale and cannot change readiness.
                if (string.IsNullOrEmpty(msg.sessionNonce) && msg.roomEpoch == 0) _gate?.Invalidate(id);
                var welcome = new DreamforgeMobWelcomeMsg
                {
                    protocol = MobCompatibility.CurrentProtocolVersion, clientNonce = msg.clientNonce,
                    sessionNonce = _serverSession, roomEpoch = _roomEpoch, minimumRevision = _revision,
                    compatibilityJson = Encode(_assets?.Compatibility)
                };
                if (caller == DewPlayer.local) OnWelcome(welcome);
                else _actor.CustomRpc_SendMessageToClient(caller, welcome);
                return;
            }
            if (!_peerHellos.TryGetValue(id, out var peer))
            {
                peer = new PeerHello { Nonce = msg.clientNonce };
                _peerHellos.Add(id, peer);
            }
            if (peer.Nonce != msg.clientNonce)
            {
                if (peer.Retired.Contains(msg.clientNonce)) return;
                if (peer.Retired.Count >= 64) { _gate?.Invalidate(id); return; }
                peer.Retired.Add(peer.Nonce); peer.Nonce = msg.clientNonce; peer.Sequence = 0;
                _gate?.Invalidate(id);
            }
            if (msg.sequence <= peer.Sequence) return;
            peer.Sequence = msg.sequence;
            _lastReady[id] = Time.unscaledTime;
            _gate?.ReportReady(id, _serverSession, _roomEpoch, Decode<MobCompatibility>(msg.compatibilityJson, 2048), msg.ready && Ready);
            if (!msg.ready) _nextSnapshot = 0;
        }

        private void OnWelcome(DreamforgeMobWelcomeMsg msg)
        {
            if (msg == null || msg.protocol != MobCompatibility.CurrentProtocolVersion || _pinned
                || msg.clientNonce != _clientNonce || !ValidNonce(msg.sessionNonce) || msg.roomEpoch <= 0 || msg.minimumRevision < 0) return;
            _remoteSession = msg.sessionNonce; _remoteEpoch = msg.roomEpoch; _remoteCompatibility = Decode<MobCompatibility>(msg.compatibilityJson, 2048);
            _pinned = true; _minimumRevision = msg.minimumRevision; _lastSnapshot = Time.unscaledTime; _nextHello = 0;
            if (Ready && _assets.Compatibility.Matches(_remoteCompatibility))
            {
                if (_state.SessionNonce != _remoteSession || _state.RoomEpoch != _remoteEpoch)
                {
                    _observedAssignments.Clear();
                    _state.Reset(_remoteSession, _remoteEpoch, _assets.Compatibility, _assets.Manifest);
                }
            }
            else Status = "pack disabled, missing or incompatible; original models";
        }

        private void BroadcastSnapshot(bool forceDisabled = false)
        {
            bool enabled = !forceDisabled && Ready && _gate != null && _gate.AllReady && _hostAssignments.Count <= 300;
            var assignments = new List<MobModelAssignment>();
            if (enabled) foreach (var entry in _hostAssignments.Values) assignments.Add(entry);
            var msg = new DreamforgeMobSnapshotMsg
            {
                protocol = MobCompatibility.CurrentProtocolVersion,
                snapshotJson = Encode(new MobModelSnapshot
                {
                    SessionNonce = _serverSession, RoomEpoch = _roomEpoch, Revision = ++_revision,
                    Enabled = enabled, Compatibility = _assets?.Compatibility, Assignments = assignments.ToArray()
                })
            };
            _actor.CustomRpc_SendMessageToAllClients(msg);
            // Mirror host handlers do not necessarily follow the remote client path.
            OnSnapshot(msg);
        }

        private void OnSnapshot(DreamforgeMobSnapshotMsg msg)
        {
            var snapshot = Decode<MobModelSnapshot>(msg?.snapshotJson, 256 * 1024);
            if (msg == null || msg.protocol != MobCompatibility.CurrentProtocolVersion || snapshot == null
                || !ValidNonce(snapshot.SessionNonce) || snapshot.RoomEpoch <= 0) return;
            if (!_pinned) return;
            if (snapshot.SessionNonce != _remoteSession || snapshot.RoomEpoch != _remoteEpoch)
            {
                if (snapshot.SessionNonce == _remoteSession && snapshot.RoomEpoch < _remoteEpoch) return;
                BeginBootstrap(); return;
            }
            if (snapshot.Revision <= _minimumRevision) return;
            if (!Ready || (snapshot.Enabled && !_assets.Compatibility.Matches(snapshot.Compatibility)))
            {
                RestoreAll(); _state.Suspend(); return;
            }
            if (!_state.TryAccept(snapshot, out _)) return;
            _lastSnapshot = Time.unscaledTime;
            Status = _state.Enabled ? "matching pack active" : "waiting for all participants; original models";
            if (_state.RequiresFallback) _nextHello = 0;
            if (!snapshot.Enabled) RestoreAll();
        }

        private void OnAdded(Entity entity)
        {
            if (!(entity is Monster monster) || !_observed.Add(monster)) return;
            _dead.Remove(monster);
            monster.EntityEvent_OnDeath += _death;
            if (monster.netId == 0) return;
            // A new local object may precede the next host snapshot and the old removal callback.
            // Retire the old incarnation before the replacement can resolve its stale assignment.
            foreach (var previous in _observed)
            {
                if (ReferenceEquals(previous, monster)) continue;
                _observedAssignments.TryGetValue(previous, out var known);
                uint previousId = known != null ? known.NetId : previous != null ? previous.netId : 0;
                if (previousId == monster.netId) RetireMonster(previous, true);
            }
        }

        private void OnDeath(EventInfoKill info)
        {
            if (info.victim is Monster monster) RetireMonster(monster, true);
        }

        private void OnRemoved(Entity entity)
        {
            if (!(entity is Monster monster)) return;
            RetireMonster(monster, false);
            if (_observed.Remove(monster) && monster != null) monster.EntityEvent_OnDeath -= _death;
            _dead.Remove(monster); _observedAssignments.Remove(monster);
        }

        private void RetireMonster(Monster monster, bool dead)
        {
            _applied.TryGetValue(monster, out var applied);
            _observedAssignments.TryGetValue(monster, out var observed);
            uint id = applied != null ? applied.NetId : observed != null ? observed.NetId : monster.netId;
            long generation = applied != null ? applied.Generation : observed != null ? observed.SpawnGeneration : long.MaxValue;
            Restore(monster); _hostAssignments.Remove(monster);
            if (id != 0) _state.MarkDespawned(id, generation);
            if (dead) _dead.Add(monster);
            _nextSnapshot = 0;
        }

        private void RefreshHostAssignments()
        {
            _scratch.Clear();
            foreach (var pair in _hostAssignments)
                if (!Usable(pair.Key) || pair.Key.netId != pair.Value.NetId) _scratch.Add(pair.Key);
            foreach (var monster in _scratch) _hostAssignments.Remove(monster);
            if (!Ready) return;
            foreach (var monster in _observed)
            {
                if (!Usable(monster) || _hostAssignments.ContainsKey(monster)) continue;
                var model = _assets.ForMonster(monster.GetType().Name);
                if (model == null) continue;
                var assignment = new MobModelAssignment
                {
                    NetId = monster.netId, SpawnGeneration = ++_generation,
                    BaseMonsterType = monster.GetType().Name, ModelId = model.Id
                };
                _hostAssignments.Add(monster, assignment);
                _observedAssignments[monster] = assignment;
            }
        }

        private bool Usable(Monster monster) => monster != null && monster.isActive && monster.isAlive && monster.netId != 0
            && !_dead.Contains(monster) && monster.owner != null && !monster.owner.isHumanPlayer;

        private void ApplySnapshot()
        {
            if (!Ready || !_state.Enabled) { RestoreAll(); return; }
            _scratch.Clear();
            foreach (var pair in _applied)
                if (!Usable(pair.Key) || !_state.TryResolve(pair.Value.NetId, pair.Key.GetType().Name, out var entry)
                    || entry.SpawnGeneration != pair.Value.Generation || entry.ModelId != pair.Value.ModelId)
                    _scratch.Add(pair.Key);
            foreach (var monster in _scratch) Restore(monster);
            // Restoration may fault the session. Never load another model in that same tick.
            if (!Ready) { RestoreAll(); return; }
            foreach (var monster in _observed)
            {
                if (!Usable(monster) || !_state.TryResolve(monster.netId, monster.GetType().Name, out var assignment)) continue;
                if (_observedAssignments.TryGetValue(monster, out var observed))
                {
                    if (observed.NetId != assignment.NetId || observed.SpawnGeneration != assignment.SpawnGeneration) continue;
                }
                else _observedAssignments.Add(monster, assignment);
                var visual = monster.Visual;
                if (visual == null || visual.model == null) continue; // retry after native model setup
                if (_applied.TryGetValue(monster, out var old))
                {
                    if (old.Visual != visual || old.OwnedModel != visual.model)
                    {
                        Fail("another model replacement changed ownership; original-model fallback requested");
                        return;
                    }
                    continue;
                }
                var prefab = _assets.Get(assignment.ModelId);
                if (prefab == null || prefab.isInitialized) { Fail("fresh model prefab unavailable"); return; }
                // Never pass an initialized model back to LoadModelLocal, including on restoration.
                var state = new AppliedModel { NetId = monster.netId, Generation = assignment.SpawnGeneration,
                    ModelId = assignment.ModelId, Visual = visual };
                try
                {
                    var previousModel = visual.model;
                    visual.LoadModelLocal(prefab);
                    state.OwnedModel = visual.model;
                    _applied.Add(monster, state);
                    if (state.OwnedModel == null || state.OwnedModel == prefab || state.OwnedModel == previousModel || !state.OwnedModel.isInitialized)
                        throw new InvalidOperationException("model instance not initialized by game");
                }
                catch (Exception ex)
                {
                    // LoadModelLocal is not documented as transactional: request native restoration even if it threw.
                    try { visual.LoadModelDefaultLocal(); } catch (Exception) { _restoreFailed = true; }
                    _applied.Remove(monster); Fail("model application failed: " + ex.Message); return;
                }
            }
        }

        private void Restore(Monster monster)
        {
            if (!_applied.TryGetValue(monster, out var state)) return;
            _applied.Remove(monster);
            try
            {
                // Respect another mod's later replacement; do not overwrite a model we do not own.
                if (state.Visual != null && state.OwnedModel != null && state.Visual.model == state.OwnedModel)
                    state.Visual.LoadModelDefaultLocal();
            }
            catch (Exception ex)
            {
                _restoreFailed = true; _faulted = true;
                Log.Warn("Custom mob restore failed; retaining bundle assets for live references: " + ex.Message);
            }
        }

        private void RestoreAll()
        {
            _scratch.Clear(); _scratch.AddRange(_applied.Keys);
            foreach (var monster in _scratch) Restore(monster);
        }

        private void Fail(string error)
        {
            if (!_faulted) Log.Warn("Custom mobs disabled for this MOD load: " + error);
            _faulted = true; Status = error; RestoreAll(); _nextHello = _nextSnapshot = 0;
        }
        private static readonly JsonSerializerSettings WireJson = new JsonSerializerSettings
        { TypeNameHandling = TypeNameHandling.None, MaxDepth = 8, MissingMemberHandling = MissingMemberHandling.Error };
        private static string Encode(object value) => value == null ? null : JsonConvert.SerializeObject(value);
        private static T Decode<T>(string json, int limit) where T : class
        {
            if (string.IsNullOrEmpty(json) || json.Length > limit) return null;
            try { return JsonConvert.DeserializeObject<T>(json, WireJson); } catch (JsonException) { return null; }
        }
        private static bool ValidNonce(string nonce) => nonce != null && nonce.Length == 32 && Guid.TryParseExact(nonce, "N", out _);

        private void UnwireActor()
        {
            if (_actor == null) return;
            try { _actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeMobWelcomeMsg>(_welcome); } catch (Exception) { }
            try { _actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeMobSnapshotMsg>(_snapshot); } catch (Exception) { }
            if (_wasServer)
                try { _actor.CustomRpc_UnregisterServerMessageHandler<DreamforgeMobHelloMsg>(_hello); } catch (Exception) { }
            _actor = null;
        }
        private void UnwireManager()
        {
            if (_manager != null)
            {
                _manager.ClientEvent_OnEntityAdd -= _added;
                _manager.ClientEvent_OnEntityRemove -= _removed;
            }
            foreach (var monster in _observed) if (monster != null) monster.EntityEvent_OnDeath -= _death;
            _observed.Clear(); _dead.Clear(); _hostAssignments.Clear(); _observedAssignments.Clear(); _manager = null;
        }
        private void UnwireZone()
        {
            if (_zone != null)
            {
                _zone.ClientEvent_OnZoneLoaded -= _zoneLoaded;
                _zone.ClientEvent_OnRoomLoaded -= _roomLoaded;
            }
            _zone = null;
        }
        public void Dispose()
        {
            if (_actor != null && _wasServer && (_enabled || _peerHellos.Count > 0 || _pinned)) try { BroadcastSnapshot(true); } catch (Exception) { }
            RestoreAll(); UnwireActor(); UnwireManager(); UnwireZone();
            _assets?.Release(!_restoreFailed); _assets = null;
        }
    }
}
