using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private static ClientSession _hostSession;
        private readonly Action<DreamforgeRunChoicesMsg> _onRunChoices;
        private readonly RunChoiceProgress _runChoiceProgress = new RunChoiceProgress();
        private RunChoiceSnapshot _receivedRunChoices => _runChoiceProgress.Received;
        private readonly RunChoicePublisher _choicePublisher = new RunChoicePublisher();
        private string _encodedRunChoices;
        private float _nextChoicesSync;
        private PendingRunRewards _pendingRunRewards => _runChoiceProgress.Rewards;
        private readonly Action<PendingRunKill> _grantPendingKill;
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
        public bool WaypointChoicesReady => CanChooseRunRules || _runChoiceProgress.ChoicesReady(Profile.Run, ChoiceZoneIndex);
        private int ChoiceZoneIndex => _zone != null ? _zone.currentZoneIndex : -1;

        public bool CanResolveSecureChoice => RunActive && !HasPendingTrades
            && _runChoiceProgress.CanResolveChoice(Profile.Run, ChoiceZoneIndex, CanChooseRunRules);

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

        private string EncodeRunChoices() => _encodedRunChoices = _choicePublisher.Encode(
            RunActive ? Profile.Run : null, Profile.LastDreamDepth, _runChoiceProgress.ZoneIndex);

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
            _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
            {
                protocol = Protocol.Version,
                choices = _choicePublisher.EncodeFinalizedZone(Profile.Run, Profile.LastDreamDepth, zoneIndex),
            });
        }

        private void TickRunChoices()
        {
            NotifyPersonalDreamEvent();
            TryFinishSecureArrival();
            if (NetworkServer.active)
            {
                string before = _encodedRunChoices;
                string current = EncodeRunChoices();
                if (current != before || Time.unscaledTime >= _nextChoicesSync) PublishRunChoices();
            }
            else
            {
                ApplyHostRunChoices();
            }
            FlushPendingRunRewards();
            TryConcludeRun();
        }

        private void FlushPendingRunRewards()
        {
            if (!RunActive) return;
            FlushPendingPressureDividends();
            _runChoiceProgress.FlushRewards(Profile, ChoiceZoneIndex, CanChooseRunRules, Emit, _grantPendingKill);
        }

        private bool CommitCombatChoice(bool publish = true)
        {
            if (!RunActive || !_runChoiceProgress.CanResolveChoice(Profile.Run, ChoiceZoneIndex, CanChooseRunRules)) return false;
            // Continuing combat chooses no pact. Delve leaves inventory and reserved trades untouched.
            Emit(Rules.Delve(Profile, Pact.None));
            MarkDirty(true);
            SaveNow();
            if (publish) PublishRunChoices();
            return true;
        }

        private void TryFinishSecureArrival()
        {
            if (!RunActive || _runChoiceProgress.TryAdvance(Profile, CanChooseRunRules, _trades,
                Emit, _grantPendingKill, PublishRunChoicesForZone) == 0) return;
            MarkDirty(true);
            _nextDreamEventNotice = 0f;
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
            if (!_runChoiceProgress.Receive(snapshot)) return;
            TryFinishSecureArrival();
            ApplyHostRunChoices();
        }

        private void ApplyHostRunChoices()
        {
            if (!RunActive || (_zone != null && _zone.isInAnyTransition)) return;
            if (!_runChoiceProgress.ApplyCurrent(Profile, ChoiceZoneIndex)) return;
            MarkDirty(true);
            SaveNow();
        }

        private void ResetRunChoiceConnection(bool resetHistory = false)
        {
            _runChoiceProgress.ResetConnection(resetHistory);
            _choicePublisher.Invalidate();
            _encodedRunChoices = null;
            _nextChoicesSync = 0;
        }

        private void TryConcludeRun()
        {
            if (!_pendingRunVictory.HasValue || !RunActive || ActiveRunId != _pendingResultRunId) return;
            if (!_runChoiceProgress.CanConclude(ActiveRunId)) return;
            bool victory = _pendingRunVictory.Value;
            _completedRunId = ActiveRunId;
            _pendingRunVictory = null;
            _pendingResultRunId = null;
            int pacts = Profile.Run.Pacts.Count;
            Emit(Rules.EndRun(Profile, victory, _trades.ReservedSalvageUids()));
            if (pacts > 0) SendCurseClear();
            ActiveRunId = null;
            _runChoiceProgress.ClearRun();
            PublishRunChoices();
            SaveNow();
        }

        private string SecureChoiceUnavailable() => HasPendingTrades
            ? Loc.T("取引の応答を待っています。", "Waiting for the trade to complete.")
            : Loc.T("ホストが道標を決めて確保または潜行を選ぶまでお待ちください。", "Waiting for the host to confirm a waypoint and choose Secure or Delve.");
    }
}
