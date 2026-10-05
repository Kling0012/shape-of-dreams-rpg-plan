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
        private float _nextKillReceipt;
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

        private bool ObserveMonsterAuthority(ulong generation)
        {
            if (!_monsterAuthority.Observe(generation, out bool changed)) return false;
            if (!changed) return true;
            _currentKillStreamId = generation.ToString(CultureInfo.InvariantCulture);
            BindNativeKillStream(_currentKillStreamId);
            Nightmare.Clear();
            ClearVariants();
            ClearMonsterCues();
            ClearBossDisplay();
            _nextKillReceipt = 0f;
            _buildDirty = true;
            return true;
        }

        private void ResetMonsterAuthorityConnection()
        {
            _monsterAuthority.ResetConnection();
            _currentKillStreamId = null;
            _nextKillReceipt = 0f;
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
            if (msg == null || msg.protocol != Protocol.Version || string.IsNullOrEmpty(runId)
                || msg.runId != runId || runId == _completedRunId || msg.baseline < -1
                || msg.streamId != msg.authorityGeneration.ToString(CultureInfo.InvariantCulture)
                || !ObserveMonsterAuthority(msg.authorityGeneration)) return;
            if (msg.clearLiveState)
            {
                Nightmare.Clear();
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
            if (msg == null || msg.protocol != Protocol.Version || msg.netId == 0 || msg.sequence <= 0
                || string.IsNullOrEmpty(msg.streamId) || string.IsNullOrEmpty(msg.runId) || string.IsNullOrEmpty(msg.eventId)
                || !string.IsNullOrEmpty(msg.bossTypeName) && (!MechanismHandshakeAccepted
                    || !ContentFingerprint.Matches(msg.protocol, msg.content, Protocol.Version))) return;
            string gameRunId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(gameRunId) || msg.runId != gameRunId || msg.runId == _completedRunId) return;
            if (!ObserveMonsterAuthority(msg.authorityGeneration)) return;
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

        /// <summary>Coalesce kill facts, rewards and pressure dividends into the same five-second save window.</summary>
        private void DeferKillSave()
        {
            MarkDirty(false);
            float deadline = Time.unscaledTime + 5f;
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
            if (_clientRpcOn == null || !NetworkClient.active || !MechanismHandshakeAccepted
                || !_monsterAuthority.HasAuthority || Time.unscaledTime < _nextKillReceipt) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId) || runId == _completedRunId) return;
            _nextKillReceipt = Time.unscaledTime + 5f;
            _missingKillDeaths.Clear();
            _killClassifications.CollectMissingDeaths(_missingKillDeaths, 32);
            var missing = _missingKillDeaths.Count == 0 ? null : new DreamforgeMissingKillFact[_missingKillDeaths.Count];
            if (missing != null)
                for (int i = 0; i < missing.Length; i++)
                    missing[i] = new DreamforgeMissingKillFact
                    {
                        streamId = _missingKillDeaths[i].StreamId, netId = _missingKillDeaths[i].MonsterNetId,
                        observationSessionId = _missingKillDeaths[i].ObservationSessionId,
                    };
            var receipts = _killDurableRunId == runId
                ? new DreamforgeKillStreamReceipt[_killDurableReceipts.Count] : Array.Empty<DreamforgeKillStreamReceipt>();
            for (int i = 0; i < receipts.Length; i++)
                receipts[i] = new DreamforgeKillStreamReceipt
                {
                    streamId = _killDurableReceipts[i].StreamId,
                    receivedThrough = _killDurableReceipts[i].AcknowledgedThrough,
                };
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeKillReceiptMsg
            {
                protocol = Protocol.Version, authorityGeneration = _monsterAuthority.AuthorityGeneration,
                runId = runId, clientId = _killClassifications.ClientId, receipts = receipts,
                observationSessionId = KillObservationSessionId(_clientRpcOn),
                missingFacts = missing,
            });
        }

        private void RestoreKillClassification()
        {
            _hostKillRestore = Profile.KillClassification;
            _killClassifications.Restore(Profile.KillClassification, Time.unscaledTime);
            _killDurableRunId = Profile.KillClassification?.RunId;
            _killDurableReceipts = (IReadOnlyList<KillReceiptState>)Profile.KillClassification?.Receipts ?? Array.Empty<KillReceiptState>();
            _killSaveRevision = 0;
            _nextKillReceipt = 0f;
            // A restored eligible death and its already-cached fact may have arrived in either order.
            DrainClassifiedDeaths();
        }
    }
}
