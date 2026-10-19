using System;
using System.Collections.Generic;
using HarmonyLib;
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
        private RunChoicePublisher _choicePublisher = new RunChoicePublisher();
        private Actor _hostAuthorityActor;
        private bool _hostAuthorityNeedsRenewal;
        private string _encodedRunChoices;
        private float _nextChoicesSync;
        // 参加者が受け取った選択状態のうち、検査を通過して処理済み（適用済みか同一 Revision 棄却か）の
        // 文字列。同じ内容の定期再送をデコード前に読み捨てるために使う。進行の復元・接続のやり直し・
        // 遠征の確定（ClearRun）で同期状態を作り直すので、そのときは解除する。
        private string _processedChoicesPayload;
        private Zone _pureWhiteZone;
        private bool _pureWhiteZoneIsPrimus;
        private sealed class CurrentChoicePeer
        {
            internal Actor Actor;
            internal Hero Hero;
            internal object Connection;
            internal string Choices;
            internal int CapabilityEpoch;
            internal float Next;
        }
        private readonly System.Collections.Generic.Dictionary<DewPlayer, CurrentChoicePeer> _currentChoicePeers
            = new System.Collections.Generic.Dictionary<DewPlayer, CurrentChoicePeer>();
        private readonly System.Collections.Generic.List<DewPlayer> _currentChoiceDeparted = new System.Collections.Generic.List<DewPlayer>();
        private PendingRunRewards _pendingRunRewards => _runChoiceProgress.Rewards;
        private readonly Action<PendingRunKill> _grantPendingKill;
        // C# 9 has no cached method group conversions: passing Emit/PublishRunChoicesForZone directly into the
        // per-frame settlement calls allocates a fresh delegate on every tick. Bind once, lazily, so sessions
        // assembled without the production constructor (native tests) still work.
        private Action<IEnumerable<GameEvent>> _emitEvents;
        private Action<int> _publishZoneChoices;
        private Action<IEnumerable<GameEvent>> EmitEvents => _emitEvents ??= Emit;
        private Action<int> PublishZoneChoices => _publishZoneChoices ??= PublishRunChoicesForZone;
        private bool? _pendingRunVictory;
        private string _pendingResultRunId;
        private string _completedRunId;

        internal static RunState HostRun => NetworkServer.active && _hostSession != null && _hostSession.RunActive
            ? _hostSession.Profile.Run : null;
        internal static int HostChosenDepth => NetworkServer.active && _hostSession != null
            ? DreamDepth.Clamp(HostRun?.DreamDepth ?? _hostSession.Profile.LastDreamDepth) : 0;
        /// <summary>ホスト自身が禁忌の職人を解放しているか（#415）。</summary>
        internal static bool HostHasForbiddenSmith => NetworkServer.active && _hostSession != null
            && Forbidden.HasSmith(_hostSession.Profile);
        /// <summary>ホスト自身の宿敵との因縁（#415）。</summary>
        internal static int HostNemesisGrudge => NetworkServer.active && _hostSession != null ? _hostSession.Profile.NemesisGrudge : 0;
        internal static string HostRunChoices => NetworkServer.active ? _hostSession?.EncodeRunChoices() : null;
        internal static ulong HostAuthorityGeneration => NetworkServer.active && _hostSession != null
            ? _hostSession.ObserveHostAuthorityPublisher() : 0;
        internal static bool CommitHostCombatChoice() => NetworkServer.active && _hostSession != null
            && _hostSession.CommitCombatChoice();
        /// <summary>戦闑では選択を解決できない経路（純白の入口）。この間は敵の必須初期化を確定待ちで止めない。</summary>
        internal static bool HostCombatChoiceSuspended => NetworkServer.active && _hostSession != null
            && _hostSession.RunActive && _hostSession.InPureWhiteRoute;

        public bool CanChooseRunRules => LobbyReturnPending ? Profile.LobbyReturnAuthority
            : NetworkServer.active || !NetworkClient.active;
        public bool CanChooseDepth => !InGame && CanChooseRunRules;
        public int ChosenDreamDepth => RunActive ? Profile.Run.DreamDepth
            : CanChooseRunRules ? DreamDepth.Clamp(Profile.LastDreamDepth) : _receivedRunChoices?.Depth ?? 0;
        public bool HasHostRunChoices => CanChooseRunRules || _receivedRunChoices != null;
        public bool WaypointChoicesReady => CanChooseRunRules || Profile.Run?.Infinity != null && Profile.Run.AwaitingChoice
            || _runChoiceProgress.ChoicesReady(Profile.Run, ChoiceZoneIndex);
        private int ChoiceZoneIndex => LobbyReturnPending
            && NetworkedManagerBase<GameManager>.softInstance?.runId != _pendingResultRunId
                ? _runChoiceProgress.ZoneIndex : _zone != null ? _zone.currentZoneIndex : -1;
        private bool InPureWhiteRoute => Profile.Run?.Infinity == null
            && _zone != null && IsPureWhiteZone(_zone.currentZone);
        // currentZone.name は Unity 側の呼び出しで毎回新しい文字列を確保する。名前は Zone ごとに不変なので、
        // Zone が入れ替わったときだけ名前を調べ、同じ Zone では結果を使い回す。キャッシュは強参照を保持するため、
        // 破棄済み Zone のラッパーと別の Zone が参照同一になって誤ることもない。
        private bool IsPureWhiteZone(Zone zone)
        {
            if (zone == null) return false;
            if (ReferenceEquals(zone, _pureWhiteZone)) return _pureWhiteZoneIsPrimus;
            _pureWhiteZone = zone;
            return _pureWhiteZoneIsPrimus = zone.name == "Zone_Primus";
        }

        internal static void OnPureWhiteBossDefeated()
        {
            if (!NetworkServer.active || _hostSession == null || !_hostSession.RunActive) return;
            if (_hostSession.Profile.Run.Infinity != null) return;
            if (_hostSession.LobbyReturnPending) return;
            if (_hostSession._pendingRunVictory == true && _hostSession._pendingResultRunId == _hostSession.ActiveRunId) return;
            _hostSession._pendingRunVictory = true;
            _hostSession._pendingResultRunId = _hostSession.ActiveRunId;
            // Persist the native victory before reward settlement; replay uses the normal terminal snapshot.
            _hostSession.SaveNow();
            _hostSession.TryConcludeRun();
        }

        public bool CanResolveSecureChoice => RunActive && !HasPendingTrades
            && (Profile.Run.Infinity == null || CanChooseRunRules && InfinityMode.NativeSaveAgreement)
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
            if (!CanChooseRunRules && !CanChooseInfinityPersonal) return Loc.T("道標はホストが選びます。", "The host chooses the waypoint.");
            if (Profile.Run?.Infinity != null && (InfinityPersonalSelectionMatchesCurrent || InfinityMode.PersonalIntentPending))
                return Loc.T("個人の選択はすでに完了しています。", "Personal choices are already complete.");
            if (!RunActive || !Profile.Run.AwaitingChoice) return Loc.T("道標は確保地点で選べます。", "Choose a waypoint at a secure point.");
            try { Emit(Rules.PickWaypoint(Profile, waypoint)); }
            catch (InvalidOperationException ex) { return ex.Message; }
            MarkDirty(true);
            SaveNow();
            PublishRunChoices();
            return null;
        }

        private ulong ObserveHostAuthorityPublisher()
        {
            var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            if (actor == null || ReferenceEquals(actor, _hostAuthorityActor)) return _choicePublisher.AuthorityGeneration;
            if (!ReferenceEquals(_hostAuthorityActor, null) || _hostAuthorityNeedsRenewal)
            {
                var previous = _choicePublisher;
                _choicePublisher = new RunChoicePublisher();
                // Only transport authority/revisions change; run, zone and waypoint generations remain intact.
                _choicePublisher.RestoreFinalized(previous.ExportFinalized(), previous.TerminalChoices, previous.TerminalVictory);
                _encodedRunChoices = null;
                _nextChoicesSync = 0;
            }
            _hostAuthorityActor = actor;
            _hostAuthorityNeedsRenewal = false;
            return _choicePublisher.AuthorityGeneration;
        }

        private string EncodeRunChoices()
        {
            if (NetworkServer.active && Profile.Run?.Infinity != null && !InfinitySaveHoldSatisfied)
                return _encodedRunChoices;
            if (NetworkServer.active) ObserveHostAuthorityPublisher();
            return _encodedRunChoices = _choicePublisher.Encode(
                RunActive ? Profile.Run : null, Profile.LastDreamDepth, _runChoiceProgress.ZoneIndex);
        }

        private void PublishRunChoices() => PublishCurrentRunChoices(true);

        private void PublishCurrentRunChoices(bool refreshHistory)
        {
            if (!NetworkServer.active || _clientRpcOn == null) return;
            if (Profile.Run?.Infinity != null && !InfinitySaveHoldSatisfied) return;
            string choices = EncodeRunChoices();
            int epoch = HostAuthority.StateSnapshotEpoch;
            float now = Time.unscaledTime;
            _currentChoiceDeparted.Clear();
            foreach (var player in _currentChoicePeers.Keys)
                if (player == null || !DewPlayer.gamePlayers.Contains(player) && !DewPlayer.lobbyPlayers.Contains(player))
                    _currentChoiceDeparted.Add(player);
            foreach (var player in _currentChoiceDeparted) _currentChoicePeers.Remove(player);
            foreach (var player in DewPlayer.allHumanPlayers)
            {
                if (player == null || !player.isHumanPlayer) continue;
                if (!_currentChoicePeers.TryGetValue(player, out var peer))
                    _currentChoicePeers.Add(player, peer = new CurrentChoicePeer());
                if (ReferenceEquals(peer.Actor, _clientRpcOn) && ReferenceEquals(peer.Hero, player.hero)
                    && ReferenceEquals(peer.Connection, player.connectionToClient) && peer.Choices == choices
                    && peer.CapabilityEpoch == epoch && now < peer.Next) continue;
                _clientRpcOn.CustomRpc_SendMessageToClient(player, new DreamforgeRunChoicesMsg
                { protocol = Protocol.Version, choices = choices });
                peer.Actor = _clientRpcOn; peer.Hero = player.hero; peer.Connection = player.connectionToClient;
                peer.Choices = choices; peer.CapabilityEpoch = epoch;
                peer.Next = now + (HostAuthority.ReducedStateSnapshots(player) ? 30f : 5f);
            }
            if (refreshHistory)
            {
                if (DewPlayer.allHumanPlayers.Count == 0)
                    _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
                    { protocol = Protocol.Version, choices = choices });
                _nextChoicesSync = now + 5f;
            }
            PublishRunChoiceHistory();
        }

        private void PublishRunChoicesForZone(int zoneIndex)
        {
            if (!NetworkServer.active || _clientRpcOn == null || !RunActive) return;
            if (Profile.Run.Infinity != null && !InfinitySaveHoldSatisfied) return;
            _clientRpcOn.CustomRpc_SendMessageToAllClients(new DreamforgeRunChoicesMsg
            {
                protocol = Protocol.Version,
                choices = _choicePublisher.EncodeFinalizedZone(Profile.Run, Profile.LastDreamDepth, zoneIndex),
            });
        }

        private void TickRunChoices()
        {
            if (!ContinueReady || _nativeContinueCheckpoint != null) return;
            TickInfinity();
            TickKillClassification();
            NotifyPersonalDreamEvent();
            TryFinishSecureArrival();
            if (CanChooseRunRules && InPureWhiteRoute && InGameUIManager.instance != null
                && InGameUIManager.instance.isDoingEnding) OnPureWhiteBossDefeated();
            EnsurePureWhiteChoice();
            if (NetworkServer.active)
            {
                string before = _encodedRunChoices;
                string current = EncodeRunChoices();
                PublishCurrentRunChoices(current != before || Time.unscaledTime >= _nextChoicesSync);
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
            if (Profile.Run.Infinity != null)
            {
                if (!InfinityMode.NativeSaveAgreement) return;
                FlushPendingPressureDividends();
                _pendingRunRewards.Drain(Profile.Run.RunId, ChoiceZoneIndex, _grantPendingKill, Profile.Run.Infinity.SegmentEpoch);
                return;
            }
            // 勝利の確定は潜行しない（#71）。選択待ちだけを解けば、保留中の撃破は戦った深度のまま精算される。
            // ホストも参加者もここで解くため、確定後の深度・確保ボーナスが両者で一致する。
            if (_pendingRunVictory == true && Profile.Run.AwaitingChoice) Profile.Run.AwaitingChoice = false;
            FlushPendingPressureDividends();
            // Shared waypoint settlement unlocks the participant's buttons, not their personal choice (#88).
            // Pure White keeps the personal choice pending until an explicit choice or the run's conclusion.
            if (InPureWhiteRoute && Profile.Run.AwaitingChoice && !_pendingRunVictory.HasValue) return;
            _runChoiceProgress.FlushRewards(Profile, ChoiceZoneIndex, CanChooseRunRules, EmitEvents, _grantPendingKill);
        }

        private bool CommitCombatChoice(bool publish = true, bool concluding = false)
        {
            if (!RunActive || !_runChoiceProgress.CanResolveChoice(Profile.Run, ChoiceZoneIndex, CanChooseRunRules)) return false;
            if (Profile.Run.Infinity != null && !concluding) return false;
            // Primus can begin combat at the entrance. Combat must not silently skip this route's choice.
            if (InPureWhiteRoute && !concluding) return false;
            // 勝利の確定は戦った深度のまま確保する。潜行で深さを増やさず、選択待ちだけを解く（#71）。
            if (concluding) Profile.Run.AwaitingChoice = false;
            // Continuing combat chooses no pact. Delve leaves inventory and reserved trades untouched.
            else Emit(Rules.Delve(Profile, Pact.None));
            MarkDirty(true);
            SaveNow();
            if (publish) PublishRunChoices();
            return true;
        }

        private void TryFinishSecureArrival()
        {
            if (Profile.Run?.Infinity != null) return;
            if (!RunActive || HasPendingKillClassification || _runChoiceProgress.TryAdvance(Profile, CanChooseRunRules, _trades,
                EmitEvents, _grantPendingKill, PublishZoneChoices) == 0) return;
            MarkDirty(true);
            _nextDreamEventNotice = 0f;
            ApplyHostRunChoices();
            PublishRunChoices();
            _notify?.Invoke(new GameEvent(EventKind.Info, Loc.T(
                "確保地点に到着。未確保の戦利品を「確保」するか、「深く潜る」かを選んでください。",
                "Secure point reached. Choose to Secure your loot or Delve deeper.")));
            SaveNow();
        }

        private void EnsurePureWhiteChoice()
        {
            if (!RunActive || !InPureWhiteRoute || _zone.isInAnyTransition || _pendingRunVictory.HasValue
                || Profile.Run.Infinity != null
                || Profile.Run.PureWhiteChoiceReached || HasPendingKillClassification
                || _runChoiceProgress.HasPendingArrival || _runChoiceProgress.ZoneIndex != ChoiceZoneIndex) return;
            if (!CanChooseRunRules && _runChoiceProgress.Snapshots.GetForZone(ChoiceZoneIndex) == null) return;
            // Resume the existing run and satchel. Legacy saves may have missed or auto-settled the entrance.
            if (!Profile.Run.AwaitingChoice) Emit(Rules.ReachSecurePoint(Profile, _trades));
            Profile.Run.PureWhiteChoiceReached = true;
            if (!CanChooseRunRules)
                _runChoiceProgress.Snapshots.GetForZone(ChoiceZoneIndex).ApplyTo(Profile.Run, ChoiceZoneIndex);
            MarkDirty(true);
            _nextDreamEventNotice = 0f;
            PublishRunChoices();
            SaveNow();
        }

        private void GrantPendingKill(PendingRunKill kill)
        {
            if (Profile.Run?.Infinity != null && !InfinityMode.NativeSaveAgreement)
                throw new InvalidOperationException("Infinity save receipts disagree; rewards remain pending.");
            int masteryBefore = Mastery.Level(Profile.Hero(kill.HeroKey).Kills);
            int awakenBefore = Rules.EquippedAwakenLevels(Profile, kill.HeroKey);
            Emit(Rules.OnKill(Profile, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey, _trades,
                variantId: kill.VariantId, roomIndex: kill.RoomIndex, heat: kill.Heat, waypoint: kill.Waypoint,
                bossTypeName: kill.BossTypeName, bossDropNightmare: kill.BossDropNightmare, bossDropDepth: kill.BossDropDepth,
                rewardScale: PressureCountRewards.ScaleFromEventId(kill.EventId),
                shardDropMultiplier: PressureCountRewards.ShardDropMultiplierFromEventId(kill.EventId),
                roomOmenDropBonus: RoomOmens.DropBonusFromEventId(kill.EventId)));
            if (Mastery.Level(Profile.Hero(kill.HeroKey).Kills) > masteryBefore) _buildDirty = true;
            if (Rules.EquippedAwakenLevels(Profile, kill.HeroKey) > awakenBefore)
            {
                _buildDirty = true;
                DeferKillSave(true);
            }
            if (kill.Tier >= MonsterTier.MiniBoss) DeferKillSave(true);
        }

        private void OnRunChoices(DreamforgeRunChoicesMsg msg)
        {
            if (msg == null) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeRunChoicesMsg));
            if (!string.IsNullOrEmpty(msg.lobbyReturnRunId))
            {
                try
                {
                    if (!ReceiveLobbyReturn(msg)) return;
                    ReceiveRunChoices(msg.choices, false);
                    FlushPendingRunRewards();
                    TryConcludeRun();
                    SaveNow();
                }
                catch (Exception ex) { DisableLobbyReturn(ex.ToString()); }
                return;
            }
            ReceiveRunChoices(msg.choices, msg.terminal ? (bool?)msg.victory : null);
        }

        private void ReceiveRunChoices(string encoded, bool? victory = null)
        {
            if (NetworkServer.active || !ContinueReady) return;
            // ホストは内容が変わらなくても選択状態を定期再送する（選択メッセージと圧力メッセージの2経路）。
            // 検査を通過した内容の再送は進行側で同一 Revision として棄てられるだけで、base64 や JSON の
            // 展開は毎回無駄になる。結果の通知（terminal・帰還）を伴う受信は常に最後まで処理する。
            if (victory == null && !string.IsNullOrEmpty(encoded)
                && string.Equals(encoded, _processedChoicesPayload, StringComparison.Ordinal)) return;
            if (!RunChoiceSnapshot.TryDecode(encoded, out var snapshot))
            {
                Log.Warn("Client: rejected malformed run choices payload.");
                return;
            }
            string gameRunId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (!string.IsNullOrEmpty(snapshot.RunId) && !string.IsNullOrEmpty(gameRunId) && snapshot.RunId != gameRunId) return;
            if (!string.IsNullOrEmpty(snapshot.RunId) && snapshot.RunId == _completedRunId) return;
            if (!ObserveMonsterAuthority(snapshot.AuthorityGeneration)) return;
            // Preserve the final zone's committed rules until its last rewards and result have been settled.
            if (string.IsNullOrEmpty(snapshot.RunId) && (RunActive || _pendingRunRewards.Count > 0)) return;
            // ここまでの検査を通過した文字列は、再送されても適用済みか同一 Revision 棄却かのどちらかに
            // なる。次回からは冒頭の一致判定でデコードを省く（復元・接続のやり直しでは解除される）。
            if (victory == null) _processedChoicesPayload = encoded;
            bool received = _runChoiceProgress.Receive(snapshot);
            if (victory.HasValue && _runChoiceProgress.AcceptsResult(snapshot))
            {
                _pendingRunVictory = LobbyReturnPending ? false : victory;
                _pendingResultRunId = snapshot.RunId;
            }
            if (!received) return;
            TryFinishSecureArrival();
            EnsurePureWhiteChoice();
            ApplyHostRunChoices();
        }

        private void ApplyHostRunChoices()
        {
            // Infinity waypoints belong to the individual player, never the shared host snapshot.
            if (Profile.Run?.Infinity != null) return;
            if (!RunActive || (_zone != null && _zone.isInAnyTransition)) return;
            if (!_runChoiceProgress.ApplyCurrent(Profile, ChoiceZoneIndex)) return;
            EnsureReachableBounties(); // 参加者にインフィニティ設定が後から届いた場合も対象にする。
            MarkDirty(true);
            SaveNow();
        }

        private void ResetRunChoiceConnection(bool resetHistory = false)
        {
            _runChoiceProgress.ResetConnection(resetHistory);
            if (resetHistory) ResetInfinityPersonalChoice();
            _choicePublisher.Invalidate();
            _encodedRunChoices = null;
            _processedChoicesPayload = null;
            _nextChoicesSync = 0;
        }

        private void TryConcludeRun()
        {
            if (Profile.Run?.Infinity != null && !InfinityMode.NativeSaveAgreement) return;
            if (!_pendingRunVictory.HasValue || !RunActive || HasPendingKillClassification || ActiveRunId != _pendingResultRunId) return;
            if (CanChooseRunRules)
            {
                CommitCombatChoice(publish: false, concluding: true);
                FlushPendingRunRewards();
            }
            if (!_runChoiceProgress.CanConclude(ActiveRunId, ChoiceZoneIndex, CanChooseRunRules)) return;
            bool victory = _pendingRunVictory.Value;
            DiagnoseExpedition("client-settle-before-" + victory);
            _completedRunId = ActiveRunId;
            Profile.CompletedRunId = _completedRunId;
            if (NetworkServer.active)
                _choicePublisher.CaptureTerminal(Profile.Run, Profile.LastDreamDepth, _runChoiceProgress.ZoneIndex, victory);
            _pendingRunVictory = null;
            _pendingResultRunId = null;
            int pacts = Profile.Run.Pacts.Count;
            TickOverflowBonus(); // Keep the final cumulative obligation before EndRun clears Run.
            Emit(Rules.EndRun(Profile, victory, _trades.ReservedSalvageUids()));
            if (pacts > 0) SendCurseClear();
            ActiveRunId = null;
            _runChoiceProgress.ClearRun();
            _processedChoicesPayload = null; // 進行を作り直すので、処理済み再送の読み捨てを解除する。
            PublishRunChoices();
            SaveNow();
            DiagnoseExpedition("client-settle-after");
        }

        private string SecureChoiceUnavailable() => HasPendingTrades
            ? Loc.T("取引の応答を待っています。", "Waiting for the trade to complete.")
            : Loc.T("ホストが道標を決めて確保または潜行を選ぶまでお待ちください。", "Waiting for the host to confirm a waypoint and choose Secure or Delve.");
    }

    /// <summary>Primus death starts an ending sequence; the native result arrives only after its cutscene.</summary>
    [HarmonyPatch(typeof(Primus_Ending), nameof(Primus_Ending.StartPrimusDeath))]
    internal static class SecurePureWhiteBossVictory
    {
        private static void Postfix() => ClientSession.OnPureWhiteBossDefeated();
    }
}
