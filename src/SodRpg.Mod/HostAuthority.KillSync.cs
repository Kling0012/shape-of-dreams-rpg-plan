using System;
using System.Collections.Generic;
using System.Globalization;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private const int KillReplayMessagesPerFrame = 32;
        private readonly struct MonsterClassificationDelta
        {
            public readonly NightmareAffix Nightmare;
            public readonly string Variant;
            public MonsterClassificationDelta(NightmareAffix nightmare, string variant) { Nightmare = nightmare; Variant = variant; }
        }

        // Updating a pending ID preserves its FIFO position; cancellation removes it without leaving tombstones.
        private sealed class MonsterSyncQueue<T>
        {
            private struct Entry
            {
                public T Value;
                public LinkedListNode<uint> Node;
            }

            private readonly Dictionary<uint, Entry> _pending = new Dictionary<uint, Entry>();
            private readonly LinkedList<uint> _order = new LinkedList<uint>();
            private readonly Stack<LinkedListNode<uint>> _free = new Stack<LinkedListNode<uint>>();
            public int Count => _pending.Count;
            public bool Contains(uint netId) => _pending.ContainsKey(netId);

            public void Set(uint netId, T value)
            {
                if (_pending.TryGetValue(netId, out var entry))
                {
                    entry.Value = value;
                    _pending[netId] = entry;
                    return;
                }
                var node = _free.Count == 0 ? new LinkedListNode<uint>(netId) : _free.Pop();
                node.Value = netId;
                _order.AddLast(node);
                _pending.Add(netId, new Entry { Value = value, Node = node });
            }

            public void Remove(uint netId)
            {
                if (!_pending.TryGetValue(netId, out var entry)) return;
                _pending.Remove(netId);
                _order.Remove(entry.Node);
                _free.Push(entry.Node);
            }

            public bool TryTake(out uint netId, out T value)
            {
                var first = _order.First;
                if (first == null) { netId = 0; value = default; return false; }
                netId = first.Value;
                value = _pending[netId].Value;
                _pending.Remove(netId);
                _order.RemoveFirst();
                _free.Push(first);
                return true;
            }

            public void Clear()
            {
                while (_order.First != null)
                {
                    var node = _order.First;
                    _order.RemoveFirst();
                    _free.Push(node);
                }
                _pending.Clear();
            }
        }
        private readonly struct RequestedKillFact
        {
            public readonly AuthoritativeRunKill Fact;
            public readonly bool RecoveredUnknown;
            public readonly string ObservationSessionId;
            public RequestedKillFact(AuthoritativeRunKill fact, bool recoveredUnknown, string observationSessionId)
            {
                Fact = fact; RecoveredUnknown = recoveredUnknown; ObservationSessionId = observationSessionId;
            }
        }
        private sealed class KillReplayCursor
        {
            public string PeerId, ObservationSessionId;
            public int FactIndex, MonsterIndex;
            public long JoinSequence, Baseline, LastFactSequence;
            public KillParticipationRange Participation;
            public bool Started, HeardReceipt, ControlSent;
            public float NextStartRetry;
            public readonly List<MonsterRuntime> Monsters = new List<MonsterRuntime>();
            public readonly Queue<RequestedKillFact> Requested = new Queue<RequestedKillFact>();
        }

        // Only outstanding facts for native participation intervals survive in memory or on disk.
        private readonly List<AuthoritativeRunKill> _killHistory = new List<AuthoritativeRunKill>();
        private readonly Dictionary<KillVictimKey, AuthoritativeRunKill> _killHistoryByVictim = new Dictionary<KillVictimKey, AuthoritativeRunKill>();
        private readonly Dictionary<uint, AuthoritativeRunKill> _legacyKillHistoryByVictim = new Dictionary<uint, AuthoritativeRunKill>();
        private readonly HashSet<string> _killHistoryEvents = new HashSet<string>(StringComparer.Ordinal);
        private readonly SortedDictionary<long, AuthoritativeRunKill> _killUnacknowledged = new SortedDictionary<long, AuthoritativeRunKill>();
        private readonly Dictionary<string, KillReplayPeer> _killPeers = new Dictionary<string, KillReplayPeer>(StringComparer.Ordinal);
        private readonly Dictionary<DewPlayer, KillReplayCursor> _killReplayPlayers = new Dictionary<DewPlayer, KillReplayCursor>();
        private readonly List<DewPlayer> _killReplayDeparted = new List<DewPlayer>();
        private readonly List<long> _killAckPrune = new List<long>();
        private readonly List<string> _emptyKillPeers = new List<string>();
        private readonly List<KillReplayCursor> _killReplayRound = new List<KillReplayCursor>();
        private readonly List<DewPlayer> _killReplayTargets = new List<DewPlayer>();
        private readonly MonsterSyncQueue<MonsterClassificationDelta> _queuedMonsterClassifications = new MonsterSyncQueue<MonsterClassificationDelta>();
        private readonly MonsterSyncQueue<int> _queuedMonsterCues = new MonsterSyncQueue<int>();
        private readonly MonsterSyncQueue<bool> _queuedMonsterRemovals = new MonsterSyncQueue<bool>();
        private int _monsterSyncFrame = -1, _monsterSyncMessages, _monsterSyncCategoryTurn;
        private int _killReplayTurn;
        private long _killSequence;
        private long _killStreamBaseline;
        private long _killLocalReplaySequence;
        private ulong _killAuthorityGeneration;
        private string _killStreamId;
        private string _killLegacyStreamId;
        private string _killRunId;

        private void EnsureKillRun()
        {
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            ulong generation = ClientSession.HostAuthorityGeneration;
            if (_killRunId == runId && _killAuthorityGeneration == generation) return;
            if (_killRunId == runId)
            {
                _killAuthorityGeneration = generation;
                _killStreamId = generation.ToString(CultureInfo.InvariantCulture);
                _killLegacyStreamId = _killStreamId + ".legacy";
                _killStreamBaseline = _killSequence;
                foreach (var cursor in _killReplayPlayers.Values)
                {
                    cursor.JoinSequence = _killSequence;
                    cursor.Participation = new KillParticipationRange
                        { StreamId = _killStreamId, ObservationSessionId = cursor.ObservationSessionId, After = _killSequence, Through = _killSequence };
                    _killPeers[cursor.PeerId].Participation.Add(cursor.Participation);
                }
                ResetKillReplayConnections();
                ClientSession.PrepareHostKillStream(runId, _killStreamId, _killStreamBaseline);
                return;
            }
            _killRunId = runId;
            _killSequence = 0;
            _killStreamBaseline = 0;
            _killAuthorityGeneration = generation;
            _killStreamId = generation.ToString(CultureInfo.InvariantCulture);
            _killLegacyStreamId = _killStreamId + ".legacy";
            _killLocalReplaySequence = 0;
            _killHistory.Clear();
            _killHistoryByVictim.Clear();
            _legacyKillHistoryByVictim.Clear();
            _killHistoryEvents.Clear();
            _killUnacknowledged.Clear();
            _killPeers.Clear();
            ResetKillReplayConnections(true);
            var saved = ClientSession.TakeHostKillClassification(runId);
            if (saved == null || saved.RunId != runId)
            {
                if (!string.IsNullOrEmpty(runId)) ClientSession.PrepareHostKillStream(runId, _killStreamId, 0);
                return;
            }
            _killSequence = saved.HostSequence;
            _killStreamBaseline = saved.HostSequence;
            foreach (var peer in saved.HostPeers)
                if (!string.IsNullOrEmpty(peer.Id) && ShouldPersistKillPeer(peer)) _killPeers[peer.Id] = peer.Clone();
            foreach (var fact in saved.HostFacts) RestoreHostFact(fact);
            // Legacy saves kept replay history in the local classification ledger.
            foreach (var fact in saved.Facts)
                if (fact.Sequence == 0 || saved.HostSequence == 0) RestoreHostFact(fact);
            ClientSession.PrepareHostKillStream(runId, _killStreamId, _killStreamBaseline);
        }

        private void RestoreHostFact(AuthoritativeRunKill fact)
        {
            if (fact.RunId != _killRunId || !_killHistoryEvents.Add(fact.EventId)) return;
            if (fact.Sequence == 0) fact = new AuthoritativeRunKill(fact.RunId, fact.EventId,
                fact.MonsterNetId, fact.ZoneIndex, fact.Nightmare, fact.VariantId, ++_killSequence, _killLegacyStreamId,
                fact.BossTypeName, fact.BossDropNightmare, fact.BossDropDepth, fact.GraphEpoch, fact.SegmentEpoch, fact.RoomEpoch);
            _killHistoryByVictim.Add(new KillVictimKey(fact.StreamId, fact.MonsterNetId), fact);
            if (fact.StreamId.EndsWith(".legacy", StringComparison.Ordinal))
                _legacyKillHistoryByVictim[fact.MonsterNetId] = fact;
            _killSequence = Math.Max(_killSequence, fact.Sequence);
            if (fact.StreamId != _killStreamId) _killStreamBaseline = Math.Max(_killStreamBaseline, fact.Sequence);
            if (fact.Sequence > 0)
            {
                _killHistory.Add(fact);
                _killUnacknowledged[fact.Sequence] = fact;
            }
        }

        private void ResetKillReplayConnections(bool retire = false)
        {
            if (retire) ClearQueuedMonsterSync();
            if (retire) _killReplayPlayers.Clear();
            else
                foreach (var pair in _killReplayPlayers)
                {
                    var cursor = pair.Value;
                    // An RPC actor replacement is not a native disconnect: keep its participation interval open.
                    cursor.Started = cursor.HeardReceipt = cursor.ControlSent = false;
                    cursor.MonsterIndex = 0;
                    cursor.Monsters.Clear();
                    if (MechanismHandshakeAccepted(pair.Key))
                        foreach (var runtime in _monsters.Values) cursor.Monsters.Add(runtime);
                    cursor.Requested.Clear();
                }
            _killReplayDeparted.Clear();
            _killReplayRound.Clear();
            _killReplayTargets.Clear();
            _killReplayTurn = 0;
            foreach (var runtime in _monsters.Values)
            {
                runtime.ClassificationQueued = false;
                if (runtime.Behavior != null) runtime.Behavior.CueQueued = false;
            }
        }

        private void RegisterKillPeer(DewPlayer player)
        {
            EnsureKillRun();
            if (player == null || !player.isHumanPlayer || string.IsNullOrEmpty(_killRunId)
                || !DewPlayer.gamePlayers.Contains(player) || _versionMismatches.ContainsKey(player)
                || _killReplayPlayers.ContainsKey(player)) return;
            string id = "connection." + ClientSession.HostAuthorityGeneration.ToString(CultureInfo.InvariantCulture)
                + "." + player.netId.ToString(CultureInfo.InvariantCulture);
            var cursor = new KillReplayCursor { PeerId = id, JoinSequence = _killSequence };
            cursor.Participation = new KillParticipationRange { StreamId = _killStreamId, After = _killSequence, Through = _killSequence };
            _killReplayPlayers.Add(player, cursor);
            if (!_killPeers.ContainsKey(id)) _killPeers.Add(id, new KillReplayPeer { Id = id, NativeOwnerId = player.guid });
            _killPeers[id].Participation.Add(cursor.Participation);
            if (_registeredOn != null) SendKillStreamControl(player, cursor);
        }

        private static bool IsProvisionalKillPeer(KillReplayPeer peer) =>
            peer.Id.StartsWith("connection.", StringComparison.Ordinal);

        private static bool ShouldPersistKillPeer(KillReplayPeer peer)
        {
            if (!IsProvisionalKillPeer(peer)) return true;
            foreach (var range in peer.Participation)
                if (range.Through > range.After && range.Through > peer.ReceivedThrough(range.StreamId)) return true;
            return false;
        }

        private bool IsKillPeerActive(string peerId)
        {
            foreach (var cursor in _killReplayPlayers.Values)
                if (cursor.PeerId == peerId) return true;
            return false;
        }

        private void PruneKillParticipation(KillReplayPeer peer)
        {
            for (int i = peer.Participation.Count - 1; i >= 0; i--)
            {
                var range = peer.Participation[i];
                // An empty interval has no receipt to wait for, even when its frontier is nonzero.
                if (range.Through > range.After && range.Through > peer.ReceivedThrough(range.StreamId)) continue;
                bool active = false;
                foreach (var cursor in _killReplayPlayers.Values)
                    if (ReferenceEquals(cursor.Participation, range)) { active = true; break; }
                if (!active) peer.Participation.RemoveAt(i);
            }
        }

        private void RemoveKillPeer(DewPlayer player)
        {
            if (!_killReplayPlayers.TryGetValue(player, out var cursor)) return;
            _killReplayPlayers.Remove(player);
            cursor.Monsters.Clear();
            cursor.Requested.Clear();
            if (!_killPeers.TryGetValue(cursor.PeerId, out var peer)) return;
            PruneKillParticipation(peer);
            if (IsProvisionalKillPeer(peer) && peer.Participation.Count == 0 && !IsKillPeerActive(peer.Id))
                _killPeers.Remove(peer.Id);
        }

        private void BindKillObservationSession(DewPlayer player, string observationSessionId)
        {
            if (string.IsNullOrEmpty(observationSessionId) || observationSessionId.Length > 64) return;
            RegisterKillPeer(player);
            if (!_killReplayPlayers.TryGetValue(player, out var cursor) || cursor.ObservationSessionId == observationSessionId) return;
            var peer = _killPeers[cursor.PeerId];
            peer.NativeOwnerId = player.guid;
            cursor.ObservationSessionId = observationSessionId;
            cursor.JoinSequence = _killSequence;
            cursor.Participation = new KillParticipationRange
                { StreamId = _killStreamId, ObservationSessionId = observationSessionId, After = _killSequence, Through = _killSequence };
            peer.Participation.Add(cursor.Participation);
            cursor.Started = cursor.HeardReceipt = false;
            cursor.MonsterIndex = 0;
            cursor.Monsters.Clear();
            foreach (var runtime in _monsters.Values) cursor.Monsters.Add(runtime);
            cursor.Requested.Clear();
            ClientSession.DirtyHostKillReplay();
            if (_registeredOn != null) SendKillStreamControl(player, cursor);
        }

        private void SendKillStreamControl(DewPlayer player, KillReplayCursor cursor)
        {
            var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            if (actor == null) return;
            actor.CustomRpc_SendMessageToClient(player, new DreamforgeKillReplayStartMsg
            {
                protocol = Protocol.Version, authorityGeneration = ClientSession.HostAuthorityGeneration, runId = _killRunId,
                baseline = cursor.HeardReceipt ? cursor.Baseline : -1, streamId = _killStreamId,
            });
            cursor.Started = true;
            cursor.ControlSent = true;
            cursor.NextStartRetry = Time.unscaledTime + 5f;
        }

        private bool TryRecoverUnidentifiedKill(DreamforgeMissingKillFact request, DewPlayer caller, out AuthoritativeRunKill recovered)
        {
            recovered = default;
            bool found = false;
            foreach (var fact in _killHistory)
            {
                if (fact.MonsterNetId != request.netId) continue;
                bool participated = false;
                foreach (var peer in _killPeers.Values)
                {
                    if (peer.NativeOwnerId != caller.guid) continue;
                    foreach (var range in peer.Participation)
                    {
                        if (range.StreamId != fact.StreamId || fact.Sequence <= range.After || fact.Sequence > range.Through) continue;
                        bool exactSession = !string.IsNullOrEmpty(request.observationSessionId)
                            && range.ObservationSessionId == request.observationSessionId;
                        bool historicalUnknownSession = range.ObservationSessionId == null && fact.StreamId != _killStreamId;
                        if (exactSession || historicalUnknownSession) { participated = true; break; }
                    }
                    if (participated) break;
                }
                if (!participated) continue;
                if (found && recovered.EventId != fact.EventId) return false;
                recovered = fact;
                found = true;
            }
            return found;
        }

        private int FindFirstKillAfter(long sequence)
        {
            int low = 0, high = _killHistory.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_killHistory[middle].Sequence <= sequence) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private void OnKillReceipt(DreamforgeKillReceiptMsg msg, DewPlayer caller)
        {
            EnsureKillRun();
            if (msg == null || msg.protocol != Protocol.Version || caller == null || !caller.isHumanPlayer
                || !DewPlayer.gamePlayers.Contains(caller) || msg.runId != _killRunId
                || msg.authorityGeneration != ClientSession.HostAuthorityGeneration || string.IsNullOrEmpty(caller.guid)
                || string.IsNullOrEmpty(msg.clientId) || msg.clientId.Length > 64 || !MechanismHandshakeAccepted(caller)) return;
            if (msg.receipts != null)
                foreach (var receipt in msg.receipts)
                    if (receipt == null || string.IsNullOrEmpty(receipt.streamId) || receipt.streamId.Length > 64
                        || receipt.receivedThrough < 0 || (receipt.streamId == _killStreamId && receipt.receivedThrough > _killSequence)) return;
            RegisterKillPeer(caller);
            BindKillObservationSession(caller, msg.observationSessionId);
            if (!_killReplayPlayers.TryGetValue(caller, out var cursor)) return;
            string peerId = caller.guid + "." + msg.clientId;
            bool knownPeer = _killPeers.ContainsKey(peerId);
            bool changed = !cursor.HeardReceipt;
            if (cursor.PeerId != peerId)
            {
                var previous = _killPeers[cursor.PeerId];
                bool provisional = cursor.PeerId.StartsWith("connection.", StringComparison.Ordinal);
                if (provisional) _killPeers.Remove(cursor.PeerId);
                changed = true;
                if (cursor.HeardReceipt)
                {
                    cursor.JoinSequence = _killSequence;
                    cursor.Participation = new KillParticipationRange
                        { StreamId = _killStreamId, ObservationSessionId = cursor.ObservationSessionId, After = _killSequence, Through = _killSequence };
                }
                cursor.HeardReceipt = false;
                cursor.PeerId = peerId;
                if (!_killPeers.ContainsKey(peerId)) _killPeers.Add(peerId, new KillReplayPeer { Id = peerId, NativeOwnerId = caller.guid });
                if (provisional) _killPeers[peerId].Participation.AddRange(previous.Participation);
                else _killPeers[peerId].Participation.Add(cursor.Participation);
            }
            var peer = _killPeers[cursor.PeerId];
            if (!cursor.HeardReceipt)
            {
                cursor.HeardReceipt = true;
                cursor.Started = false;
                // Only pre-join participation is replayed; native deaths explicitly request their original stream.
                cursor.Baseline = cursor.JoinSequence;
                long replayAfter = knownPeer ? 0 : cursor.Baseline;
                cursor.LastFactSequence = replayAfter;
                cursor.FactIndex = FindFirstKillAfter(replayAfter);
                cursor.MonsterIndex = 0;
            }
            if (msg.receipts != null)
                foreach (var receipt in msg.receipts) changed |= peer.AcceptReceipt(receipt.streamId, receipt.receivedThrough);
            // Requests bypass the historical cursor, including facts missed during a zone/authority transition.
            if (msg.missingFacts != null && cursor.Requested.Count == 0)
                for (int i = 0; i < Math.Min(32, msg.missingFacts.Length); i++)
                {
                    var request = msg.missingFacts[i];
                    if (request == null || request.netId == 0) continue;
                    AuthoritativeRunKill fact;
                    bool recovery = request.streamId == PendingMonsterDeath.UnidentifiedRecoveryStreamId;
                    bool found = recovery ? TryRecoverUnidentifiedKill(request, caller, out fact)
                        : request.streamId == PendingMonsterDeath.LegacyStreamId
                            ? _legacyKillHistoryByVictim.TryGetValue(request.netId, out fact)
                            : _killHistoryByVictim.TryGetValue(new KillVictimKey(request.streamId, request.netId), out fact);
                    if (!found) continue;
                    cursor.Requested.Enqueue(new RequestedKillFact(fact, recovery, request.observationSessionId));
                    if (!PeerNeedsFact(peer, fact)) AddRequiredFact(peer, fact);
                }
            PruneAcknowledgedKills();
            if (changed) ClientSession.DirtyHostKillReplay();
        }

        private void PruneAcknowledgedKills()
        {
            _killAckPrune.Clear();
            foreach (var pair in _killUnacknowledged)
            {
                // Protocol 15 recorded no absent-client frontiers: this fixed migration set must survive until run end.
                if (pair.Value.StreamId.EndsWith(".legacy", StringComparison.Ordinal)) continue;
                if (pair.Key > ClientSession.DurableHostKillReceipt(pair.Value.StreamId)) continue;
                bool needed = false;
                foreach (var peer in _killPeers.Values)
                    if (PeerNeedsFact(peer, pair.Value)) { needed = true; break; }
                if (!needed) _killAckPrune.Add(pair.Key);
            }
            foreach (long sequence in _killAckPrune) _killUnacknowledged.Remove(sequence);
            if (_killAckPrune.Count > 0)
            {
                int kept = 0;
                for (int i = 0; i < _killHistory.Count; i++)
                {
                    var fact = _killHistory[i];
                    if (_killUnacknowledged.ContainsKey(fact.Sequence)) _killHistory[kept++] = fact;
                    else
                    {
                        _killHistoryByVictim.Remove(new KillVictimKey(fact.StreamId, fact.MonsterNetId));
                        _killHistoryEvents.Remove(fact.EventId);
                        if (_legacyKillHistoryByVictim.TryGetValue(fact.MonsterNetId, out var legacy) && legacy.EventId == fact.EventId)
                            _legacyKillHistoryByVictim.Remove(fact.MonsterNetId);
                    }
                }
                _killHistory.RemoveRange(kept, _killHistory.Count - kept);
                foreach (var cursor in _killReplayPlayers.Values) cursor.FactIndex = FindFirstKillAfter(cursor.LastFactSequence);
            }
            _emptyKillPeers.Clear();
            foreach (var peer in _killPeers.Values)
            {
                PruneKillParticipation(peer);
                if (IsProvisionalKillPeer(peer) && peer.Participation.Count == 0 && !IsKillPeerActive(peer.Id))
                    _emptyKillPeers.Add(peer.Id);
            }
            foreach (string id in _emptyKillPeers) _killPeers.Remove(id);
            _emptyKillPeers.Clear();
        }

        private static bool PeerNeedsFact(KillReplayPeer peer, AuthoritativeRunKill fact)
        {
            if (fact.Sequence <= peer.ReceivedThrough(fact.StreamId)) return false;
            foreach (var range in peer.Participation)
                if (range.StreamId == fact.StreamId && fact.Sequence > range.After && fact.Sequence <= range.Through) return true;
            return false;
        }

        private static void AddRequiredFact(KillReplayPeer peer, AuthoritativeRunKill fact)
        {
            foreach (var range in peer.Participation)
                if (range.StreamId == fact.StreamId && fact.Sequence > range.After
                    && (fact.Sequence <= range.Through || (range.Through < long.MaxValue && fact.Sequence == range.Through + 1)))
                {
                    range.Through = Math.Max(range.Through, fact.Sequence);
                    return;
                }
            peer.Participation.Add(new KillParticipationRange { StreamId = fact.StreamId, After = fact.Sequence - 1, Through = fact.Sequence });
        }

        internal void CaptureKillReplay(KillClassificationCheckpoint saved)
        {
            EnsureKillRun();
            if (saved.RunId != _killRunId) return;
            PruneAcknowledgedKills();
            saved.HostSequence = _killSequence;
            foreach (var fact in _killUnacknowledged.Values) saved.HostFacts.Add(fact);
            foreach (var peer in _killPeers.Values)
                if (ShouldPersistKillPeer(peer)) saved.HostPeers.Add(peer.Clone());
        }

        private void CaptureAuthoritativeRunKill(Monster monster)
        {
            if (!NetworkServer.active || monster == null || !_monsters.TryGetValue(monster, out var runtime)
                || monster.disableLoot) return;
            if (monster.Status != null && monster.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter)
                && !hunter.enableGoldAndExpDrops) return;
            EnsureKillRun();
            if (runtime.KillEventId != null && runtime.KillEventStreamId == _killStreamId
                && runtime.KillEventNetId == monster.netId) return;
            if (string.IsNullOrEmpty(_killRunId)) return;
            foreach (var player in DewPlayer.gamePlayers) RegisterKillPeer(player);
            foreach (var pair in _killReplayPlayers)
                if (_registeredOn != null && !pair.Value.ControlSent)
                    SendKillStreamControl(pair.Key, pair.Value);
            runtime.KillEventId = Guid.NewGuid().ToString("N");
            runtime.KillEventStreamId = _killStreamId;
            runtime.KillEventNetId = monster.netId;
            runtime.SyncNetId = monster.netId;
            _nightmares.TryGetValue(monster, out var nightmare);
            string bossTypeName = null;
            bool bossDropNightmare = false;
            int bossDropDepth = 0;
            if (monster is BossMonster && monster.type == Monster.MonsterType.Boss)
            {
                string typeName = monster.GetType().Name;
                if (BossSets.TryGetSet(typeName, out _))
                {
                    bossTypeName = typeName;
                    // Native difficulty names are also used by GameResultManager and DewSave.
                    string difficulty = NetworkedManagerBase<GameManager>.softInstance?.difficulty?.name;
                    bossDropNightmare = difficulty == "diffNightmare" || difficulty == "diffLimbo";
                    bossDropDepth = DreamDepth.Clamp(ClientSession.HostRun?.DreamDepth ?? 0);
                }
            }
            var fact = new AuthoritativeRunKill(_killRunId, runtime.KillEventId, monster.netId,
                _zone?.currentZoneIndex ?? -1, nightmare, runtime.Variant?.Id, ++_killSequence, _killStreamId,
                bossTypeName, bossDropNightmare, bossDropDepth, runtime.GraphEpoch, runtime.SegmentEpoch, runtime.RoomEpoch);
            RestoreHostFact(fact);
            foreach (var pair in _killReplayPlayers)
                if (MechanismHandshakeAccepted(pair.Key) || !pair.Value.ControlSent) pair.Value.Participation.Through = fact.Sequence;
            ClientSession.PublishHostKillFact(fact);
            var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            actor?.CustomRpc_SendMessageToAllClients(
                DreamforgeMonsterKillMsg.FromFact(fact, ClientSession.HostAuthorityGeneration));
        }

        private void SendMonsterClassification(MonsterRuntime runtime, DewPlayer target = null)
        {
            var monster = runtime.Monster;
            if (_registeredOn == null || !Alive(monster)) return;
            if (monster.netId == 0) return;
            _nightmares.TryGetValue(monster, out var nightmare);
            string variant = runtime.Variant?.Id;
            if (target == null && runtime.ClassificationQueued && runtime.SyncNetId == monster.netId
                && runtime.ClassificationNightmare == nightmare && runtime.ClassificationVariant == variant) return;
            if (target == null)
            {
                runtime.SyncNetId = monster.netId;
                runtime.ClassificationQueued = true;
                runtime.ClassificationNightmare = nightmare;
                runtime.ClassificationVariant = variant;
                _queuedMonsterRemovals.Remove(monster.netId);
                _queuedMonsterClassifications.Set(monster.netId, new MonsterClassificationDelta(nightmare, variant));
                return;
            }
            if (!ReserveMonsterSyncMessage()) return;
            if (variant != null)
            {
                var msg = new DreamforgeVariantMsg
                {
                    protocol = Protocol.Version, netId = monster.netId, variantId = variant,
                    authorityGeneration = ClientSession.HostAuthorityGeneration,
                };
                _registeredOn.CustomRpc_SendMessageToClient(target, msg);
            }
            else
            {
                var msg = new DreamforgeNightmareMsg
                {
                    protocol = Protocol.Version, netId = monster.netId, affixes = (int)nightmare,
                    authorityGeneration = ClientSession.HostAuthorityGeneration,
                };
                _registeredOn.CustomRpc_SendMessageToClient(target, msg);
            }
        }

        private void SendMonsterRemoval(MonsterRuntime runtime)
        {
            uint netId = runtime.SyncNetId;
            if (netId == 0) return;
            _queuedMonsterClassifications.Remove(netId);
            _queuedMonsterCues.Remove(netId);
            _queuedMonsterRemovals.Set(netId, true);
        }

        private void ClearQueuedMonsterSync()
        {
            _queuedMonsterClassifications.Clear();
            _queuedMonsterCues.Clear();
            _queuedMonsterRemovals.Clear();
        }

        private void ClearRemoteMonsterState()
        {
            if (_registeredOn == null || !NetworkServer.active || string.IsNullOrEmpty(_killRunId)) return;
            _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeKillReplayStartMsg
            {
                protocol = Protocol.Version, authorityGeneration = _killAuthorityGeneration,
                runId = _killRunId, streamId = _killStreamId, baseline = -1, clearLiveState = true,
            });
        }

        private int RemainingMonsterSyncMessages()
        {
            if (_monsterSyncFrame != Time.frameCount)
            {
                _monsterSyncFrame = Time.frameCount;
                _monsterSyncMessages = 0;
            }
            return KillReplayMessagesPerFrame - _monsterSyncMessages;
        }

        private bool ReserveMonsterSyncMessage()
        {
            if (RemainingMonsterSyncMessages() <= 0) return false;
            _monsterSyncMessages++;
            return true;
        }

        private void QueueMonsterCue(uint netId, int cue)
        {
            if (netId != 0 && !_queuedMonsterRemovals.Contains(netId)) _queuedMonsterCues.Set(netId, cue);
        }

        private void FlushMonsterSync(int maxMessages)
        {
            int budget = Math.Min(RemainingMonsterSyncMessages(), maxMessages);
            int emptyCategories = 0;
            // Three constant-time category probes per send at most; neither stale IDs nor dictionary scans accumulate.
            while (budget > 0 && emptyCategories < 3)
            {
                int category = _monsterSyncCategoryTurn;
                _monsterSyncCategoryTurn = (_monsterSyncCategoryTurn + 1) % 3;
                int count = category == 0 ? _queuedMonsterRemovals.Count
                    : category == 1 ? _queuedMonsterClassifications.Count : _queuedMonsterCues.Count;
                if (count == 0) { emptyCategories++; continue; }
                if (!ReserveMonsterSyncMessage()) break;
                emptyCategories = 0;
                budget--;
                if (category == 0)
                {
                    _queuedMonsterRemovals.TryTake(out uint netId, out _);
                    _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeNightmareMsg
                    {
                        protocol = Protocol.Version, netId = netId, removed = true,
                        authorityGeneration = ClientSession.HostAuthorityGeneration,
                    });
                }
                else if (category == 1)
                {
                    _queuedMonsterClassifications.TryTake(out uint netId, out var delta);
                    if (delta.Variant != null)
                        _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeVariantMsg
                        {
                            protocol = Protocol.Version, netId = netId, variantId = delta.Variant,
                            authorityGeneration = ClientSession.HostAuthorityGeneration,
                        });
                    else
                        _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeNightmareMsg
                        {
                            protocol = Protocol.Version, netId = netId, affixes = (int)delta.Nightmare,
                            authorityGeneration = ClientSession.HostAuthorityGeneration,
                        });
                }
                else
                {
                    _queuedMonsterCues.TryTake(out uint netId, out int cue);
                    _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeMonsterCueMsg
                    {
                        protocol = Protocol.Version, netId = netId, cue = cue,
                        authorityGeneration = ClientSession.HostAuthorityGeneration,
                    });
                }
            }
        }

        private void ResyncMonsterClassifications()
        {
            foreach (var runtime in _monsters.Values)
            {
                SendMonsterClassification(runtime);
                SendMonsterBehaviorCue(runtime, false);
            }
        }

        private void TickKillReplay()
        {
            EnsureKillRun();
            if (_registeredOn == null || string.IsNullOrEmpty(_killRunId)) return;
            _killReplayDeparted.Clear();
            foreach (var player in _killReplayPlayers.Keys)
                if (player == null || !DewPlayer.gamePlayers.Contains(player)) _killReplayDeparted.Add(player);
            foreach (var player in _killReplayDeparted) RemoveKillPeer(player);
            foreach (var player in DewPlayer.gamePlayers)
                if (player != null) RegisterKillPeer(player);
            _killReplayRound.Clear();
            _killReplayTargets.Clear();
            foreach (var pair in _killReplayPlayers)
            {
                _killReplayTargets.Add(pair.Key);
                _killReplayRound.Add(pair.Value);
            }
            int count = _killReplayRound.Count;
            FlushMonsterSync(count == 0 ? KillReplayMessagesPerFrame : KillReplayMessagesPerFrame / 2);
            int budget = RemainingMonsterSyncMessages();
            int localBudget = count == 0 ? budget : 8;
            int localIndex = FindFirstKillAfter(_killLocalReplaySequence);
            while (localIndex < _killHistory.Count && localBudget-- > 0)
            {
                var fact = _killHistory[localIndex++];
                if (fact.StreamId != _killStreamId) ClientSession.PublishHostKillFact(fact);
                _killLocalReplaySequence = fact.Sequence;
                budget--;
            }
            if (count == 0) return;
            int idle = 0;
            while (budget >= 2 && idle < count)
            {
                int index = _killReplayTurn++ % count;
                var cursor = _killReplayRound[index];
                var player = _killReplayTargets[index];
                int sent = SendNextKillReplay(player, cursor);
                if (sent == 0) { idle++; continue; }
                budget -= sent;
                idle = 0;
            }
            if (_killReplayTurn > 1000000) _killReplayTurn %= count;
        }

        private int SendNextKillReplay(DewPlayer player, KillReplayCursor cursor)
        {
            ulong authority = ClientSession.HostAuthorityGeneration;
            if (!cursor.ControlSent)
            {
                if (!ReserveMonsterSyncMessage()) return 0;
                SendKillStreamControl(player, cursor);
                return 1;
            }
            if (!MechanismHandshakeAccepted(player)) return 0;
            if (!cursor.Started || (!cursor.HeardReceipt && Time.unscaledTime >= cursor.NextStartRetry))
            {
                if (!ReserveMonsterSyncMessage()) return 0;
                SendKillStreamControl(player, cursor);
                return 1;
            }
            if (!cursor.HeardReceipt) return 0;
            if (cursor.Requested.Count > 0)
            {
                if (!ReserveMonsterSyncMessage()) return 0;
                var requested = cursor.Requested.Dequeue();
                var message = DreamforgeMonsterKillMsg.FromFact(requested.Fact, authority);
                message.recoveredUnknown = requested.RecoveredUnknown;
                message.observationSessionId = requested.ObservationSessionId;
                _registeredOn.CustomRpc_SendMessageToClient(player, message);
                return 1;
            }
            if (cursor.MonsterIndex < cursor.Monsters.Count)
            {
                var runtime = cursor.Monsters[cursor.MonsterIndex++];
                // Both messages count against the shared frame budget; unchanged broadcast caches are untouched.
                if (Alive(runtime.Monster) && _monsters.TryGetValue(runtime.Monster, out var current)
                    && ReferenceEquals(runtime, current))
                {
                    SendMonsterClassification(runtime, player);
                    SendMonsterBehaviorCue(runtime, true, player);
                }
                if (cursor.MonsterIndex == cursor.Monsters.Count) { cursor.Monsters.Clear(); cursor.MonsterIndex = 0; }
                return 2;
            }
            if (cursor.FactIndex >= _killHistory.Count) return 0;
            var fact = _killHistory[cursor.FactIndex];
            bool needed = PeerNeedsFact(_killPeers[cursor.PeerId], fact);
            if (needed && !ReserveMonsterSyncMessage()) return 0;
            cursor.FactIndex++;
            cursor.LastFactSequence = fact.Sequence;
            if (needed) _registeredOn.CustomRpc_SendMessageToClient(player, DreamforgeMonsterKillMsg.FromFact(fact, authority));
            return 1;
        }

    }
}
