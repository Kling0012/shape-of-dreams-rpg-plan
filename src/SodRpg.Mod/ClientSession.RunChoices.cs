using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private static ClientSession _hostSession;
        private readonly Action<DreamforgeRunChoicesMsg> _onRunChoices;
        private RunChoiceSnapshot _receivedRunChoices;
        private RunChoiceSnapshot _appliedRunChoices;
        private RunChoiceSnapshot _encodedChoicesState;
        private string _encodedRunChoices;
        private int _choiceRevision;
        private float _nextChoicesSync;
        private readonly PendingRunRewards _pendingRunRewards = new PendingRunRewards();
        private readonly Action<PendingRunKill> _grantPendingKill;
        private int _lastChoiceZoneIndex = -1;
        private readonly Dictionary<int, RunChoiceSnapshot> _committedZoneChoices = new Dictionary<int, RunChoiceSnapshot>();
        private bool _secureArrivalPending;
        private bool? _pendingRunVictory;
        private string _pendingResultRunId;
        private string _completedRunId;

        internal static RunState HostRun => NetworkServer.active && _hostSession != null && _hostSession.RunActive
            ? _hostSession.Profile.Run : null;
        internal static int HostChosenDepth => NetworkServer.active && _hostSession != null
            ? DreamDepth.Clamp(HostRun?.DreamDepth ?? _hostSession.Profile.LastDreamDepth) : 0;
        internal static string HostRunChoices => NetworkServer.active ? _hostSession?.EncodeRunChoices() : null;
        internal static bool CommitHostCombatChoice() => NetworkServer.active && _hostSession != null
            && _hostSession.CommitCombatChoice();

        public bool CanChooseRunRules => NetworkServer.active || !NetworkClient.active;
        public bool CanChooseDepth => !InGame && CanChooseRunRules;
        public int ChosenDreamDepth => RunActive ? Profile.Run.DreamDepth
            : CanChooseRunRules ? DreamDepth.Clamp(Profile.LastDreamDepth) : _receivedRunChoices?.Depth ?? 0;
        public bool HasHostRunChoices => CanChooseRunRules || _receivedRunChoices != null;
        public bool WaypointChoicesReady => CanChooseRunRules || (_receivedRunChoices != null
            && ReferenceEquals(_receivedRunChoices, _appliedRunChoices) && _receivedRunChoices.AppliesTo(Profile.Run, ChoiceZoneIndex));
        private int ChoiceZoneIndex => _zone != null ? _zone.currentZoneIndex : -1;

        public bool CanResolveSecureChoice => RunActive && Profile.Run.AwaitingChoice && !HasPendingTrades
            && (CanChooseRunRules || (WaypointChoicesReady && _receivedRunChoices != null
                && _receivedRunChoices.AppliesTo(Profile.Run, ChoiceZoneIndex) && _receivedRunChoices.Settled));

        public string ChooseDreamDepth(int depth)
        {
            if (!CanChooseDepth) return Loc.T("夢の深さは遠征前にホストが選びます。", "The host chooses dream depth before the expedition.");
            Profile.LastDreamDepth = DreamDepth.Clamp(depth);
            MarkDirty(false);
            SaveNow();
            PublishRunChoices();
            return null;
        }

        public string ChooseWaypoint(Waypoint waypoint)
        {
            if (!CanChooseRunRules) return Loc.T("道標はホストが選びます。", "The host chooses the waypoint.");
            if (!RunActive || !Profile.Run.AwaitingChoice) return Loc.T("道標は確保地点で選べます。", "Choose a waypoint at a secure point.");
            try { Emit(Rules.PickWaypoint(Profile, waypoint)); }
            catch (InvalidOperationException ex) { return ex.Message; }
            MarkDirty(true);
            SaveNow();
            PublishRunChoices();
            return null;
        }

        private string EncodeRunChoices()
        {
            var run = RunActive ? Profile.Run : null;
            int depth = DreamDepth.Clamp(run?.DreamDepth ?? Profile.LastDreamDepth);
            int zone = run == null ? -1 : ChoiceZoneIndex;
            var old = _encodedChoicesState;
            bool unchanged = old != null && old.RunId == (run?.RunId ?? "") && old.Depth == depth
                && old.ZoneIndex == zone && old.Generation == (run?.WaypointGeneration ?? 0)
                && old.Active == (run?.ActiveWaypoint ?? Waypoint.None)
                && old.Pending == (run?.PendingWaypoint ?? Waypoint.None)
                && old.Chosen == (run?.WaypointChosen ?? false)
                && old.Settled == (run != null && run.WaypointGeneration > 0 && !run.AwaitingChoice)
                && old.Offers.Count == (run?.OfferedWaypoints.Count ?? 0);
            if (unchanged && run != null)
                for (int i = 0; i < old.Offers.Count; i++)
                    if (old.Offers[i] != run.OfferedWaypoints[i]) { unchanged = false; break; }
            if (unchanged) return _encodedRunChoices;
            _encodedChoicesState = RunChoiceSnapshot.Capture(run, depth, zone, ++_choiceRevision);
            return _encodedRunChoices = _encodedChoicesState.Encode();
        }

        private void PublishRunChoices()
        {
            if (!NetworkServer.active || _clientRpcOn == null) return;
            _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
            {
                protocol = Protocol.Version, choices = EncodeRunChoices(),
            });
            _nextChoicesSync = Time.unscaledTime + 5f;
        }

        private void PublishRunChoicesForZone(int zoneIndex)
        {
            if (!NetworkServer.active || _clientRpcOn == null || !RunActive) return;
            var snapshot = RunChoiceSnapshot.Capture(Profile.Run, Profile.LastDreamDepth, zoneIndex, ++_choiceRevision);
            _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
            {
                protocol = Protocol.Version, choices = snapshot.Encode(),
            });
            _encodedChoicesState = null;
        }

        private void TickRunChoices()
        {
            if (NetworkServer.active)
            {
                string before = _encodedRunChoices;
                string current = EncodeRunChoices();
                if (current != before || Time.unscaledTime >= _nextChoicesSync) PublishRunChoices();
            }
            else
            {
                TryFinishSecureArrival();
                ApplyHostRunChoices();
            }
            FlushPendingRunRewards();
            TryConcludeRun();
        }

        private bool CanGrantRunRewards => RunActive && (CanChooseRunRules
            ? !Profile.Run.AwaitingChoice
            : WaypointChoicesReady && (_receivedRunChoices.Generation == 0 || _receivedRunChoices.Settled));

        private void FlushPendingRunRewards()
        {
            if (_pendingRunRewards.HasFor(ActiveRunId, ChoiceZoneIndex) && CanGrantRunRewards)
                CommitCombatChoice();
            if (CanGrantRunRewards) _pendingRunRewards.Drain(ActiveRunId, ChoiceZoneIndex, _grantPendingKill);
        }

        private bool CommitCombatChoice(bool publish = true)
        {
            if (!RunActive || !Profile.Run.AwaitingChoice) return false;
            if (!CanChooseRunRules && (!WaypointChoicesReady || !_receivedRunChoices.Settled)) return false;
            // Continuing combat chooses no pact. Delve leaves inventory and reserved trades untouched.
            Emit(Rules.Delve(Profile, Pact.None));
            MarkDirty(true);
            SaveNow();
            if (publish) PublishRunChoices();
            return true;
        }

        private void TryFinishSecureArrival()
        {
            if (!_secureArrivalPending || !RunActive) return;
            if (!CanChooseRunRules)
            {
                if (!_committedZoneChoices.TryGetValue(_lastChoiceZoneIndex, out var prior)
                    || prior.RunId != ActiveRunId) return;
                prior.ApplyTo(Profile.Run, _lastChoiceZoneIndex);
            }
            if (Profile.Run.AwaitingChoice)
            {
                // Leaving the zone commits its pending selection before the next offer expires it.
                Emit(Rules.Delve(Profile, Pact.None));
                MarkDirty(true);
            }
            if (_lastChoiceZoneIndex >= 0)
            {
                _pendingRunRewards.Drain(ActiveRunId, _lastChoiceZoneIndex, _grantPendingKill);
                PublishRunChoicesForZone(_lastChoiceZoneIndex);
            }
            _lastChoiceZoneIndex = ChoiceZoneIndex;
            _secureArrivalPending = false;
            if (!Rules.ShouldOfferSecurePoint(Profile, traveling: true)) return;
            Emit(Rules.ReachSecurePoint(Profile, _trades));
            _appliedRunChoices = null;
            ApplyHostRunChoices();
            PublishRunChoices();
            _notify?.Invoke(new GameEvent(EventKind.Info, Loc.T(
                "確保地点に到着。未確保の戦利品を「確保」するか、「深く潜る」かを選んでください。",
                "Secure point reached. Choose to Secure your loot or Delve deeper.")));
            SaveNow();
        }

        private void GrantPendingKill(PendingRunKill kill)
        {
            int masteryBefore = Mastery.Level(Profile.Hero(kill.HeroKey).Kills);
            int awakenBefore = Rules.EquippedAwakenLevels(Profile, kill.HeroKey);
            Emit(Rules.OnKill(Profile, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey, _trades,
                variantId: kill.VariantId, roomIndex: kill.RoomIndex));
            if (Mastery.Level(Profile.Hero(kill.HeroKey).Kills) > masteryBefore) _buildDirty = true;
            if (Rules.EquippedAwakenLevels(Profile, kill.HeroKey) > awakenBefore)
            {
                _buildDirty = true;
                _nextSave = 0;
            }
            if (kill.Tier >= MonsterTier.MiniBoss) _nextSave = 0;
        }

        private void OnRunChoices(DreamforgeRunChoicesMsg msg)
        {
            if (msg == null || msg.protocol != Protocol.Version) return;
            ReceiveRunChoices(msg.choices);
        }

        private void ReceiveRunChoices(string encoded)
        {
            if (NetworkServer.active || !RunChoiceSnapshot.TryDecode(encoded, out var snapshot)) return;
            string gameRunId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (!string.IsNullOrEmpty(snapshot.RunId) && !string.IsNullOrEmpty(gameRunId) && snapshot.RunId != gameRunId) return;
            if (!string.IsNullOrEmpty(snapshot.RunId) && snapshot.RunId == _completedRunId) return;
            // Preserve the final zone's committed rules until its last rewards and result have been settled.
            if (string.IsNullOrEmpty(snapshot.RunId) && (RunActive || _pendingRunRewards.Count > 0)) return;
            if (snapshot.Generation == 0 || snapshot.Settled)
            {
                if (!_committedZoneChoices.TryGetValue(snapshot.ZoneIndex, out var prior)
                    || snapshot.IsNewerThan(prior)) _committedZoneChoices[snapshot.ZoneIndex] = snapshot;
            }
            if (!snapshot.IsNewerThan(_receivedRunChoices))
            {
                // An older global revision can still be the missing commit for the preceding zone.
                TryFinishSecureArrival();
                return;
            }
            _receivedRunChoices = snapshot;
            TryFinishSecureArrival();
            ApplyHostRunChoices();
        }

        private void ApplyHostRunChoices()
        {
            if (_receivedRunChoices == null || ReferenceEquals(_appliedRunChoices, _receivedRunChoices)
                || !RunActive || _secureArrivalPending || (_zone != null && _zone.isInAnyTransition)) return;
            if (!_receivedRunChoices.ApplyTo(Profile.Run, ChoiceZoneIndex)) return;
            _appliedRunChoices = _receivedRunChoices;
            MarkDirty(true);
            SaveNow();
        }

        private void ResetRunChoiceConnection(bool resetHistory = false)
        {
            _receivedRunChoices = _appliedRunChoices = _encodedChoicesState = null;
            _encodedRunChoices = null;
            _nextChoicesSync = 0;
            if (resetHistory)
            {
                _committedZoneChoices.Clear();
                _secureArrivalPending = false;
            }
        }

        private void TryConcludeRun()
        {
            if (!_pendingRunVictory.HasValue || !RunActive || ActiveRunId != _pendingResultRunId) return;
            if (_pendingRunRewards.Count > 0) return;
            bool victory = _pendingRunVictory.Value;
            _completedRunId = ActiveRunId;
            _pendingRunVictory = null;
            _pendingResultRunId = null;
            int pacts = Profile.Run.Pacts.Count;
            Emit(Rules.EndRun(Profile, victory, _trades.ReservedSalvageUids()));
            if (pacts > 0) SendCurseClear();
            ActiveRunId = null;
            _committedZoneChoices.Clear();
            _secureArrivalPending = false;
            PublishRunChoices();
            SaveNow();
        }

        private string SecureChoiceUnavailable() => HasPendingTrades
            ? Loc.T("取引の応答を待っています。", "Waiting for the trade to complete.")
            : Loc.T("ホストが道標を決めて確保または潜行を選ぶまでお待ちください。", "Waiting for the host to confirm a waypoint and choose Secure or Delve.");
    }
}
