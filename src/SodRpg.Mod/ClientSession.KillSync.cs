using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private readonly MonsterAuthorityState _monsterAuthority = new MonsterAuthorityState();
        private readonly KillClassificationLedger _killClassifications = new KillClassificationLedger();
        private readonly List<PendingMonsterDeath> _missingKillDeaths = new List<PendingMonsterDeath>();
        private sealed class NativeDeathLifetime { public uint NetId; public string RunId, StreamId, ObservationSessionId; }
        private struct NativeDeathBinding
        {
            public NativeDeathLifetime Lifetime;
            public uint NetId;
            public string RunId, PreviousStreamId, ObservationSessionId;
        }
        private readonly List<NativeDeathBinding> _unidentifiedDeathLifetimes = new List<NativeDeathBinding>();
        private readonly HashSet<uint> _repeatedKillVictims = new HashSet<uint>();
        private Actor _killObservationActor;
        private string _killObservationSessionId;
        private readonly ConditionalWeakTable<Monster, NativeDeathLifetime> _nativeDeathLifetimes =
            new ConditionalWeakTable<Monster, NativeDeathLifetime>();
        private static readonly ConditionalWeakTable<Monster, NativeDeathLifetime>.CreateValueCallback CreateDeathLifetime =
            monster => new NativeDeathLifetime();
        private bool HasPendingKillClassification => _killClassifications.PendingCount > 0;
        private long _killSaveRevision;
        private long _killExpirationRevision;
        private IReadOnlyList<KillReceiptState> _killSavedReceipts = Array.Empty<KillReceiptState>();
        private IReadOnlyList<KillReceiptState> _killDurableReceipts = Array.Empty<KillReceiptState>();
        private string _killSavedRunId, _killDurableRunId, _currentKillStreamId;
        private float _nextKillReceipt, _nextKillMissing, _nextKillAck, _nextKillHeartbeat;
        private string _killReceiptRun, _killReceiptObservation;
        private Actor _killReceiptActor;
        private sealed class MissingKillRetry
        {
            public float Next, Delay = 0.5f;
        }
        private readonly Dictionary<KillVictimKey, MissingKillRetry> _killMissingRetries = new Dictionary<KillVictimKey, MissingKillRetry>();
        private readonly HashSet<KillVictimKey> _killMissingKeys = new HashSet<KillVictimKey>();
        private readonly List<KillVictimKey> _killMissingRetired = new List<KillVictimKey>();
        private readonly List<PendingMonsterDeath> _killMissingRequests = new List<PendingMonsterDeath>(32);
        private readonly Dictionary<string, long> _killSentReceipts = new Dictionary<string, long>(StringComparer.Ordinal);
        private IReadOnlyList<KillReceiptState> _killReceiptScanSource;

        private void ResetKillReceiptSchedule()
        {
            _nextKillReceipt = _nextKillMissing = _nextKillAck = _nextKillHeartbeat = 0f;
            _killReceiptRun = _killReceiptObservation = null;
            _killReceiptActor = null;
            _killMissingRetries.Clear();
            _killSentReceipts.Clear();
            _killReceiptScanSource = null;
        }
        private float _lastKillProfileSave = float.NegativeInfinity;
        private string _killRetirementRunId;
        private int _killRetirementZone = -1;
        private KillClassificationCheckpoint _hostKillRestore;
        private long _killRetirementGraph;

        private long _dividendRetirementGraph;
        internal static KillClassificationCheckpoint TakeHostKillClassification(string runId)
        {
            if (_hostSession == null) return null;
            var saved = _hostSession._hostKillRestore;
            if (saved == null || saved.RunId != runId) return _hostSession.Profile.KillClassification;
            _hostSession._hostKillRestore = null;
            return saved;
        }

        internal static long DurableHostKillReceipt(string streamId)
        {
            if (_hostSession == null || _hostSession._killDurableRunId != NetworkedManagerBase<GameManager>.softInstance?.runId) return 0;
            foreach (var receipt in _hostSession._killDurableReceipts)
                if (receipt.StreamId == streamId) return receipt.AcknowledgedThrough;
            return 0;
        }

        private IReadOnlyList<KillReceiptState> KillReceiptsForProgress(string runId)
        {
            // Only the failed Infinity expedition may acknowledge settled in-memory rewards.
            // SaveNow captures rewards and these deduplication frontiers together; Continue
            // restores both and resets the waiver. Never manufacture a durable frontier.
            if (InfinitySaveHoldReleased && Profile.KillClassification?.RunId == runId)
                return Profile.KillClassification.Receipts;
            return _killDurableRunId == runId ? _killDurableReceipts : Array.Empty<KillReceiptState>();
        }

        internal static long HostKillReceiptForProgress(string streamId)
        {
            if (_hostSession == null) return 0;
            foreach (var receipt in _hostSession.KillReceiptsForProgress(NetworkedManagerBase<GameManager>.softInstance?.runId))
                if (receipt.StreamId == streamId) return receipt.AcknowledgedThrough;
            return 0;
        }

        internal static void PrepareHostKillStream(string runId, string streamId, long baseline)
        {
            if (_hostSession == null) return;
            _hostSession._currentKillStreamId = streamId;
            _hostSession._killClassifications.BeginRun(runId);
            _hostSession.BindNativeKillStream(streamId);
            _hostSession._killClassifications.AcceptReceiptBaseline(streamId, baseline, Time.unscaledTime);
            _hostSession.DeferKillSave();
        }

        internal static void DirtyHostKillReplay() => _hostSession?.DeferKillSave();

        internal readonly Dictionary<uint, MonsterOwnership> EnemyOwners = new Dictionary<uint, MonsterOwnership>();
        private bool _ownershipDisplayFailed;
        private DynamicDifficultyDisplay[] _dynamicDifficultyDisplay;
        private long _dynamicDifficultyRevision;
        private string _dynamicDifficultyRun;

        internal double DynamicDifficultyFor(string participant)
        {
            if (_dynamicDifficultyRun != NetworkedManagerBase<GameManager>.softInstance?.runId) return 1;
            if (_dynamicDifficultyDisplay != null)
                foreach (var state in _dynamicDifficultyDisplay)
                    if (state.participantId == participant) return state.factor / 100d;
            return 1;
        }

        private DynamicDifficultyStatus _dynamicStatus;
        private string _dynamicStatusRun;

        /// <summary>The host's synced factor for this run only; false when nothing was received (menu shows "—").</summary>
        internal bool TryDynamicDifficultyFor(string participant, out double factor)
        {
            factor = 1;
            if (_dynamicDifficultyDisplay == null || string.IsNullOrEmpty(participant)
                || _dynamicDifficultyRun != NetworkedManagerBase<GameManager>.softInstance?.runId) return false;
            foreach (var state in _dynamicDifficultyDisplay)
                if (state.participantId == participant) { factor = state.factor / 100d; return true; }
            return false;
        }

        /// <summary>The host's synced status (#700) for this run, or null when not received.</summary>
        internal DynamicDifficultyStatus DynamicStatus =>
            _dynamicStatus != null && _dynamicStatusRun == NetworkedManagerBase<GameManager>.softInstance?.runId ? _dynamicStatus : null;

        // The reliable ordered channel delivers the latest status last; a rebuilt host may restart its revision.
        private void ReceiveDynamicStatus(DynamicDifficultyStatus status)
        {
            if (status == null || !status.Valid()) return;
            _dynamicStatus = status;
            _dynamicStatusRun = NetworkedManagerBase<GameManager>.softInstance?.runId;
        }

        private void ReceiveDynamicDifficulty(DynamicDifficultyDisplay[] display, long revision)
        {
            if (display == null && revision > _dynamicDifficultyRevision)
            {
                _dynamicDifficultyDisplay = null;
                _dynamicStatus = null;
                return;
            }
            if (display == null || display.Length > 4 || revision <= 0 || revision < _dynamicDifficultyRevision) return;
            foreach (var state in display)
                if (state == null || string.IsNullOrEmpty(state.participantId) || state.factor < 1
                    || state.factor > CoopBalance.DynamicDifficulty.SafetyMaxFactor * 100) return;
            _dynamicDifficultyDisplay = display;
            _dynamicDifficultyRevision = revision;
            _dynamicDifficultyRun = NetworkedManagerBase<GameManager>.softInstance?.runId;
        }

        private void ReceiveMonsterOwnership(uint netId, string participantId, long revision, bool protectedState, int slot,
            BossShareDisplay[] bossShares = null)
        {
            if (_ownershipDisplayFailed || netId == 0) return;
            try
            {
                if (EnemyOwners.TryGetValue(netId, out var previous) && previous.Revision > revision) return;
                // Old hosts omit the additive fields. Keep no ownership state for their messages.
                if (revision <= 0) { EnemyOwners.Remove(netId); return; }
                if (bossShares != null)
                {
                    bool valid = bossShares.Length >= 1 && bossShares.Length <= 4;
                    for (int i = 0; valid && i < bossShares.Length; i++)
                    {
                        var share = bossShares[i];
                        valid = share != null && !string.IsNullOrEmpty(share.participantId)
                            && share.playerSlot >= 1 && share.playerSlot <= 4 && share.remaining >= 0 && share.remaining <= 1000;
                        for (int j = 0; valid && j < i; j++)
                            valid = bossShares[j].participantId != share.participantId && bossShares[j].playerSlot != share.playerSlot;
                    }
                    if (!valid) bossShares = null;
                }
                EnemyOwners[netId] = new MonsterOwnership(participantId, revision,
                    protectedState && !string.IsNullOrEmpty(participantId) && slot >= 1 && slot <= 4, slot, bossShares);
            }
            catch (Exception ex)
            {
                _ownershipDisplayFailed = true;
                EnemyOwners.Clear();
                Log.Warn("Enemy ownership display unavailable; classification and host rules continue: " + ex.Message);
            }
        }

        private bool ObserveMonsterAuthority(ulong generation)
        {
            if (!_monsterAuthority.Observe(generation, out bool changed)) return false;
            if (!changed) return true;
            _currentKillStreamId = generation.ToString(CultureInfo.InvariantCulture);
            BindNativeKillStream(_currentKillStreamId);
            Nightmare.Clear();
            EnemyOwners.Clear();
            _dynamicDifficultyDisplay = null;
            _dynamicDifficultyRevision = 0;
            ClearVariants();
            ClearMonsterCues();
            ClearBossDisplay();
            ResetHelloSchedule();
            _nextHello = 0f;
            ResetKillReceiptSchedule();
            _buildDirty = true;
            return true;
        }

        private void ResetMonsterAuthorityConnection()
        {
            _monsterAuthority.ResetConnection();
            EnemyOwners.Clear();
            _ownershipDisplayFailed = false;
            _dynamicDifficultyDisplay = null;
            _dynamicDifficultyRevision = 0;
            _currentKillStreamId = null;
            ResetKillReceiptSchedule();
        }

        private string KillObservationSessionId(Actor actor)
        {
            if (!ReferenceEquals(actor, _killObservationActor) || _killObservationSessionId == null)
            {
                _killObservationActor = actor;
                _killObservationSessionId = Guid.NewGuid().ToString("N");
            }
            return _killObservationSessionId;
        }

        private void BindNativeKillStream(string streamId)
        {
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId)) return;
            _killClassifications.BeginRun(runId);
            string observationSessionId = KillObservationSessionId(NetworkedManagerBase<ActorManager>.softInstance?.serverActor);
            _repeatedKillVictims.Clear();
            foreach (var binding in _unidentifiedDeathLifetimes)
            {
                if (binding.RunId != runId || binding.ObservationSessionId != observationSessionId) continue;
                if (binding.PreviousStreamId == streamId) _repeatedKillVictims.Add(binding.NetId);
                var lifetime = binding.Lifetime;
                if (lifetime.RunId == runId && lifetime.NetId == binding.NetId
                    && lifetime.StreamId == PendingMonsterDeath.UnidentifiedStreamId
                    && lifetime.ObservationSessionId == observationSessionId)
                {
                    lifetime.StreamId = streamId;
                    lifetime.ObservationSessionId = null;
                }
            }
            _unidentifiedDeathLifetimes.Clear();
            _killClassifications.BindUnidentifiedDeaths(streamId, _repeatedKillVictims, observationSessionId, Time.unscaledTime);
            ResolveClassifiedDeaths();
        }

        private void OnKillReplayStart(DreamforgeKillReplayStartMsg msg)
        {
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (msg == null || string.IsNullOrEmpty(runId)
                || msg.runId != runId || runId == _completedRunId || msg.baseline < -1
                || msg.streamId != msg.authorityGeneration.ToString(CultureInfo.InvariantCulture)
                || !ObserveMonsterAuthority(msg.authorityGeneration)) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeKillReplayStartMsg));
            if (msg.clearLiveState)
            {
                Nightmare.Clear();
                EnemyOwners.Clear();
                ClearVariants();
                ClearMonsterCues();
                ClearBossDisplay();
                return;
            }
            _currentKillStreamId = msg.streamId;
            _killClassifications.BeginRun(runId);
            BindNativeKillStream(msg.streamId);
            if (msg.baseline < 0) return;
            _killClassifications.AcceptReceiptBaseline(msg.streamId, msg.baseline, Time.unscaledTime);
            DeferKillSave();
        }

        private void OnMonsterKill(DreamforgeMonsterKillMsg msg)
        {
            if (msg == null || msg.netId == 0 || msg.sequence <= 0
                || string.IsNullOrEmpty(msg.streamId) || string.IsNullOrEmpty(msg.runId) || string.IsNullOrEmpty(msg.eventId)) return;
            string gameRunId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(gameRunId) || msg.runId != gameRunId || msg.runId == _completedRunId) return;
            if (!ObserveMonsterAuthority(msg.authorityGeneration)) return;
            Protocol.WarnContentMismatch(msg.protocol, msg.content, nameof(DreamforgeMonsterKillMsg));
            if (msg.recoveredUnknown) _killClassifications.BindRecoveredDeath(msg.netId, msg.streamId, msg.observationSessionId, Time.unscaledTime);
            ReceiveAuthoritativeKill(msg.ToFact());
        }

        internal static void PublishHostKillFact(AuthoritativeRunKill fact)
        {
            if (NetworkServer.active && _hostSession != null) _hostSession.ReceiveAuthoritativeKill(fact);
        }

        private void ReceiveAuthoritativeKill(AuthoritativeRunKill fact)
        {
            bool added = _killClassifications.ReceiveFact(fact, Time.unscaledTime);
            ResolveClassifiedDeaths();
            if (added) DeferKillSave();
        }

        private void CaptureNativeKill(Monster monster, PendingRunKill kill)
        {
            var lifetime = _nativeDeathLifetimes.GetValue(monster, CreateDeathLifetime);
            uint monsterNetId = monster.netId;
            var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            string streamId = (NetworkServer.active || ReferenceEquals(actor, _clientRpcOn) ? _currentKillStreamId : null)
                ?? PendingMonsterDeath.UnidentifiedStreamId;
            string observationSessionId = KillObservationSessionId(actor);
            bool sameLifetime = lifetime.NetId == monsterNetId && lifetime.RunId == kill.RunId;
            if (sameLifetime && ((lifetime.StreamId == streamId
                && (streamId != PendingMonsterDeath.UnidentifiedStreamId || lifetime.ObservationSessionId == observationSessionId))
                || (lifetime.StreamId == PendingMonsterDeath.UnidentifiedStreamId
                    && streamId != PendingMonsterDeath.UnidentifiedStreamId && lifetime.ObservationSessionId == observationSessionId)))
            {
                lifetime.StreamId = streamId;
                if (streamId != PendingMonsterDeath.UnidentifiedStreamId) lifetime.ObservationSessionId = null;
                return;
            }
            string previousStream = sameLifetime && lifetime.StreamId != PendingMonsterDeath.UnidentifiedStreamId ? lifetime.StreamId : null;
            lifetime.NetId = monsterNetId;
            lifetime.RunId = kill.RunId;
            lifetime.StreamId = streamId;
            lifetime.ObservationSessionId = streamId == PendingMonsterDeath.UnidentifiedStreamId ? observationSessionId : null;
            if (!_killClassifications.ObserveDeath(new PendingMonsterDeath(monsterNetId, kill, streamId, lifetime.ObservationSessionId), Time.unscaledTime)) return;
            if (streamId == PendingMonsterDeath.UnidentifiedStreamId)
                _unidentifiedDeathLifetimes.Add(new NativeDeathBinding
                {
                    Lifetime = lifetime, NetId = monsterNetId, RunId = kill.RunId, PreviousStreamId = previousStream,
                    ObservationSessionId = observationSessionId,
                });
            ResolveClassifiedDeaths();
            DeferKillSave();
        }

        /// <summary>Coalesce kill facts and urgent rewards without saving more often than once per two seconds.</summary>
        private void DeferKillSave(bool urgent = false)
        {
            MarkDirty(false);
            float now = Time.unscaledTime;
            float deadline = Math.Max(now + (urgent ? 0f : 5f), _lastKillProfileSave + 2f);
            if (_nextSave > deadline) _nextSave = deadline;
        }

        private void TickKillClassification()
        {
            int before = _killClassifications.PendingCount;
            if (before == 0 && _killExpirationRevision == _killClassifications.ExpirationRevision) return;
            DrainClassifiedDeaths();
            if (_killClassifications.PendingCount != before) DeferKillSave();
        }

        private void DrainClassifiedDeaths()
        {
            while (_killClassifications.TryResolve(out var kill, Time.unscaledTime)) _pendingRunRewards.Add(kill);
            _killClassifications.ReleaseResolvedNativeLifetimes();
            if (_killExpirationRevision != _killClassifications.ExpirationRevision)
            {
                _killExpirationRevision = _killClassifications.ExpirationRevision;
                DeferKillSave();
            }
            if (_unidentifiedDeathLifetimes.Count > _killClassifications.UnidentifiedPendingCount)
                for (int i = _unidentifiedDeathLifetimes.Count - 1; i >= 0; i--)
                {
                    var binding = _unidentifiedDeathLifetimes[i];
                    if (binding.RunId != _killClassifications.RunId
                        || !_killClassifications.HasPendingUnidentifiedDeath(binding.NetId, binding.ObservationSessionId))
                        _unidentifiedDeathLifetimes.RemoveAt(i);
                }
        }

        private void ResolveClassifiedDeaths()
        {
            DrainClassifiedDeaths();
            FlushPendingRunRewards();
            TryFinishSecureArrival();
            TryConcludeRun();
        }

        private void CaptureKillClassification()
        {
            var saved = _killClassifications.Capture(Time.unscaledTime);
            if (NetworkServer.active)
            {
                var host = HostAuthority.NativeInstance;
                if (host != null) host.CaptureKillReplay(saved);
                else if (_hostKillRestore != null && _hostKillRestore.RunId == saved.RunId)
                {
                    // First-launch/slot saves can precede the first host Tick; preserve restored recovery state.
                    saved.HostSequence = _hostKillRestore.HostSequence;
                    saved.HostFacts.AddRange(_hostKillRestore.HostFacts);
                    if (_hostKillRestore.HostSequence == 0) saved.HostFacts.AddRange(_hostKillRestore.Facts);
                    foreach (var peer in _hostKillRestore.HostPeers) saved.HostPeers.Add(peer.Clone());
                }
            }
            Profile.KillClassification = saved;
            _killExpirationRevision = _killClassifications.ExpirationRevision;
        }

        private void RecordKillSaveRevision()
        {
            _killSaveRevision = Profile.Revision;
            _killSavedReceipts = (IReadOnlyList<KillReceiptState>)Profile.KillClassification?.Receipts ?? Array.Empty<KillReceiptState>();
            _killSavedRunId = Profile.KillClassification?.RunId;
        }

        private void TickKillSync()
        {
            if (_zone != null && !_zone.isInAnyTransition && _killClassifications.RunId != null)
            {
                if (_killRetirementRunId != _killClassifications.RunId)
                {
                    _killRetirementRunId = _killClassifications.RunId;
                    _killRetirementZone = -1;
                    _killRetirementGraph = 0;
                    _dividendRetirementGraph = 0;
                }
                if (_zone.currentZoneIndex > _killRetirementZone)
                {
                    _killRetirementZone = _zone.currentZoneIndex;
                    _killClassifications.RetireUnobservedFactsBeforeZone(_killRetirementZone);
                }
                long graph = Profile.Run?.Infinity?.GraphEpoch ?? 0;
                if (graph > _killRetirementGraph)
                {
                    _killRetirementGraph = graph;
                    _killClassifications.RetireUnobservedFactsBeforeGraph(graph);
                    DeferKillSave();
                }
                if (graph > _dividendRetirementGraph && _pendingPressureDividends.RetireBeforeGraph(graph))
                {
                    _dividendRetirementGraph = graph;
                    DeferKillSave();
                }
            }
            if (_writer != null && _killSaveRevision > 0 && _writer.WrittenRevision >= _killSaveRevision)
            {
                _killDurableReceipts = _killSavedReceipts;
                _killDurableRunId = _killSavedRunId;
            }
            if (_clientRpcOn == null || !NetworkClient.active || !_monsterAuthority.HasAuthority) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId) || runId == _completedRunId) return;
            if (_hostKillReceiptDelta)
            {
                TickKillReceiptDelta(runId);
                return;
            }
            if (Time.unscaledTime < _nextKillReceipt) return;
            // Old hosts merge frontiers cumulatively and hold kill replay only until the first
            // receipt, so re-sending an unchanged frontier every five seconds carries no new
            // information. Send when a frontier moved, when a missing-fact request must go out,
            // or as a bounded liveness heartbeat; transport and authority-generation resets clear
            // the sent marks (ResetKillReceiptSchedule) and make the next scan send immediately.
            string observation = KillObservationSessionId(_clientRpcOn);
            if (_killReceiptRun != runId || !ReferenceEquals(_killReceiptActor, _clientRpcOn)
                || _killReceiptObservation != observation)
            {
                ResetKillReceiptSchedule();
                _killReceiptRun = runId;
                _killReceiptActor = _clientRpcOn;
                _killReceiptObservation = observation;
            }
            float now = Time.unscaledTime;
            _nextKillReceipt = now + 5f;
            bool heartbeat = now >= _nextKillHeartbeat;
            _missingKillDeaths.Clear();
            _killClassifications.CollectMissingDeaths(_missingKillDeaths, 32);
            var acknowledged = KillReceiptsForProgress(runId);
            bool dirty = false;
            for (int i = 0; i < acknowledged.Count; i++)
                if (!_killSentReceipts.TryGetValue(acknowledged[i].StreamId, out long sent)
                    || sent != acknowledged[i].AcknowledgedThrough) { dirty = true; break; }
            if (!heartbeat && !dirty && _missingKillDeaths.Count == 0) return;
            var missing = _missingKillDeaths.Count == 0 ? null : new DreamforgeMissingKillFact[_missingKillDeaths.Count];
            if (missing != null)
                for (int i = 0; i < missing.Length; i++)
                    missing[i] = new DreamforgeMissingKillFact
                    {
                        streamId = _missingKillDeaths[i].StreamId, netId = _missingKillDeaths[i].MonsterNetId,
                        observationSessionId = _missingKillDeaths[i].ObservationSessionId,
                    };
            var receipts = acknowledged.Count == 0 ? Array.Empty<DreamforgeKillStreamReceipt>()
                : new DreamforgeKillStreamReceipt[acknowledged.Count];
            for (int i = 0; i < receipts.Length; i++)
                receipts[i] = new DreamforgeKillStreamReceipt
                {
                    streamId = acknowledged[i].StreamId,
                    receivedThrough = acknowledged[i].AcknowledgedThrough,
                };
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeKillReceiptMsg
            {
                protocol = Protocol.Version, authorityGeneration = _monsterAuthority.AuthorityGeneration,
                runId = runId, clientId = _killClassifications.ClientId, receipts = receipts,
                observationSessionId = observation,
                missingFacts = missing,
            });
            for (int i = 0; i < receipts.Length; i++)
                _killSentReceipts[receipts[i].streamId] = receipts[i].receivedThrough;
            if (heartbeat) _nextKillHeartbeat = now + 25f;
        }

        private void TickKillReceiptDelta(string runId)
        {
            string observation = KillObservationSessionId(_clientRpcOn);
            if (_killReceiptRun != runId || !ReferenceEquals(_killReceiptActor, _clientRpcOn)
                || _killReceiptObservation != observation)
            {
                ResetKillReceiptSchedule();
                _killReceiptRun = runId;
                _killReceiptActor = _clientRpcOn;
                _killReceiptObservation = observation;
            }
            float now = Time.unscaledTime;
            bool heartbeat = now >= _nextKillReceipt;
            var acknowledged = KillReceiptsForProgress(runId);
            int dirty = 0;
            // Frontiers only change by replacing the whole list (capture, durable promotion, restore),
            // so an unchanged reference cannot turn this scan dirty; wait for the next ack gate instead.
            // KillClassificationTests.Receipt_snapshots_change_by_replacement_and_stay_frozen_once_published
            // guards this premise at every publication source (Capture, Clone, save round-trip).
            if (heartbeat || now >= _nextKillAck || !ReferenceEquals(acknowledged, _killReceiptScanSource))
            {
                _killReceiptScanSource = acknowledged;
                for (int i = 0; i < acknowledged.Count; i++)
                    if (heartbeat || !_killSentReceipts.TryGetValue(acknowledged[i].StreamId, out long sent)
                        || sent != acknowledged[i].AcknowledgedThrough) dirty++;
                if (dirty == 0) _nextKillAck = now + 0.25f;
            }
            _killMissingRequests.Clear();
            if (now >= _nextKillMissing)
            {
                _nextKillMissing = now + 0.5f;
                _missingKillDeaths.Clear();
                _killClassifications.CollectMissingDeaths(_missingKillDeaths, int.MaxValue);
                _killMissingKeys.Clear();
                foreach (var death in _missingKillDeaths)
                {
                    var key = new KillVictimKey(death.StreamId, death.MonsterNetId, death.ObservationSessionId);
                    _killMissingKeys.Add(key);
                    if (!_killMissingRetries.TryGetValue(key, out var retry))
                        _killMissingRetries.Add(key, retry = new MissingKillRetry());
                    if (now >= retry.Next && _killMissingRequests.Count < 32) _killMissingRequests.Add(death);
                }
                _killMissingRetired.Clear();
                foreach (var key in _killMissingRetries.Keys)
                    if (!_killMissingKeys.Contains(key)) _killMissingRetired.Add(key);
                foreach (var key in _killMissingRetired) _killMissingRetries.Remove(key);
            }
            if (!heartbeat && dirty == 0 && _killMissingRequests.Count == 0) return;
            var receipts = dirty == 0 ? Array.Empty<DreamforgeKillStreamReceipt>() : new DreamforgeKillStreamReceipt[dirty];
            int index = 0;
            for (int i = 0; i < acknowledged.Count && index < receipts.Length; i++)
                if (heartbeat || !_killSentReceipts.TryGetValue(acknowledged[i].StreamId, out long sent)
                    || sent != acknowledged[i].AcknowledgedThrough)
                    receipts[index++] = new DreamforgeKillStreamReceipt
                    { streamId = acknowledged[i].StreamId, receivedThrough = acknowledged[i].AcknowledgedThrough };
            var missing = _killMissingRequests.Count == 0 ? null : new DreamforgeMissingKillFact[_killMissingRequests.Count];
            if (missing != null)
                for (int i = 0; i < missing.Length; i++)
                {
                    var death = _killMissingRequests[i];
                    missing[i] = new DreamforgeMissingKillFact
                    { streamId = death.StreamId, netId = death.MonsterNetId, observationSessionId = death.ObservationSessionId };
                }
            // The existing host merges frontiers cumulatively. Empty receipts never acknowledge a received-only fact.
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeKillReceiptMsg
            {
                protocol = Protocol.Version, authorityGeneration = _monsterAuthority.AuthorityGeneration,
                runId = runId, clientId = _killClassifications.ClientId, receipts = receipts,
                observationSessionId = KillObservationSessionId(_clientRpcOn), missingFacts = missing,
            });
            foreach (var receipt in receipts) _killSentReceipts[receipt.streamId] = receipt.receivedThrough;
            foreach (var death in _killMissingRequests)
            {
                var retry = _killMissingRetries[new KillVictimKey(death.StreamId, death.MonsterNetId, death.ObservationSessionId)];
                retry.Next = now + retry.Delay;
                retry.Delay = Math.Min(5f, retry.Delay * 2f);
            }
            if (heartbeat) _nextKillReceipt = now + 30f;
            if (dirty > 0) _nextKillAck = now + 0.25f;
        }

        private void RestoreKillClassification()
        {
            _hostKillRestore = Profile.KillClassification;
            _killClassifications.Restore(Profile.KillClassification, Time.unscaledTime);
            _killDurableRunId = Profile.KillClassification?.RunId;
            _killDurableReceipts = (IReadOnlyList<KillReceiptState>)Profile.KillClassification?.Receipts ?? Array.Empty<KillReceiptState>();
            _killSaveRevision = 0;
            ResetKillReceiptSchedule();
            // A restored eligible death and its already-cached fact may have arrived in either order.
            DrainClassifiedDeaths();
        }
    }
}
