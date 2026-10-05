using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Newtonsoft.Json;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>Owns the native, finite graph. Profile receipts remain the reward authority.</summary>
    internal static partial class InfinityMode
    {
        internal const string RuntimeKey = "dreamforge.infinity.runtime";
        internal const string ChoiceKey = "dreamforge.infinity.choice";
        private const string HaltKey = "dreamforge.infinity.halted";
        private const string RosterHaltKey = "dreamforge.infinity.rosterHalted";
        internal static bool ExpeditionHalted
            => NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.ContainsKey(RosterHaltKey) == true;
        internal static string ExpeditionHaltNotice
            => NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.TryGetValue(RosterHaltKey, out var reason) == true
                ? reason : null;

        internal static void StopExpedition(string reason)
        {
            if (!NetworkServer.active || ExpeditionHalted) return;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null) return;
            settings.customData[RosterHaltKey] = reason;
            settings.customData.Remove(RuntimeKey);
            settings.customData.Remove(ChoiceKey);
            _initial = null; _newInfinity = false; _refresh = false; _restoring = false;
            _choice = null; _choiceText = null; Acks.Clear();
            Log.Warn("Infinity stopped for this expedition; normal mode continues. " + reason);
            ClientSession.StopInfinityRun(reason);
        }
        private static string _runId;
        private static InfinityRunState _initial;
        private static bool _restoring;
        private static bool _newInfinity;
        private static bool _refresh;
        private static bool _unavailable;
        private static string _lastDisableLog;
        private static string _generationReportedRun;
        private static readonly Type[] NativePatchClasses =
        {
            typeof(InfinityNextZone), typeof(InfinityGenerated), typeof(InfinityRoomClear),
            typeof(InfinityRoomIdentity), typeof(InfinityTravel), typeof(InfinityNoSpecialRift),
            typeof(InfinityResult), typeof(InfinityExit), typeof(InfinityNativeSave),
            typeof(InfinityNativeRestore), typeof(InfinityZoneTravel), typeof(InfinityNoSpecialInvitation),
            typeof(InfinityLobbyStartCondition), typeof(InfinityRevealArrival), typeof(InfinityRevealWorld),
            typeof(InfinityTravelCommand), typeof(InfinityRevealQuestOverride),
            typeof(InfinityMapRefresh), typeof(InfinityMapDisable),
            typeof(InfinityMapNodeSetup), typeof(InfinityMapTravelSelection), typeof(InfinityMapMoveSelection),
            typeof(InfinityMapClosestNode), typeof(InfinityMapHover), typeof(InfinityMapNodeTooltip),
            typeof(InfinityMapTooltip), typeof(InfinityMapTravelTooltip), typeof(InfinityMapDescription),
            typeof(InfinityMapPingPosition), typeof(InfinityMapCacheChanged), typeof(InfinityMapEdgeStatus),
        };

        internal static bool Available { get; private set; }
        internal static string UnavailableReason { get; private set; }
        // #144: the lobby line must identify the cause when a player reports it. The first reason is
        // appended in its internal wording, cut at 120 chars so the one-line report stays readable.
        private const int NoticeReasonLimit = 120;
        internal static string UnavailableNotice => Loc.T(
            "インフィニティは無効です。通常モードは利用できます。", "Infinity is disabled. Normal mode remains available.")
            + (string.IsNullOrEmpty(UnavailableReason) ? ""
                : " " + Loc.T("（理由: ", "(Reason: ") + NoticeReason(UnavailableReason) + Loc.T("）", ")"));

        private static string NoticeReason(string reason) => reason.Length <= NoticeReasonLimit
            ? reason : reason.Substring(0, NoticeReasonLimit);

        internal static bool IsNativePatch(Type type) => Array.IndexOf(NativePatchClasses, type) >= 0;

        internal static void CompletePatchInstallation(int installedCount)
        {
            if (installedCount != NativePatchClasses.Length)
                DisableFeature("Infinity native interception is incomplete.");
            if (!_unavailable) Available = true;
        }

        internal static void DisableFeature(string reason) => DisableFeature(reason, null);

        internal static void DisableFeature(string reason, Exception error)
        {
            Available = false;
            _restoring = false;
            _refresh = false;
            _newInfinity = false;
            // #144: every distinct check that stops Infinity is logged by name; the first reason
            // stays in the lobby notice. Later checks used to be swallowed silently, hiding which
            // patch or interception actually disabled the feature on a player's machine.
            if (reason != _lastDisableLog)
            {
                _lastDisableLog = reason;
                Log.Warn("Infinity disabled; normal mode remains available. " + reason + FirstStackFrames(error));
            }
            if (_unavailable) return;
            _unavailable = true;
            UnavailableReason = reason;
            if (NetworkServer.active)
                try
                {
                    var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                    if (settings != null) settings.customData[HaltKey] = "1";
                }
                catch (Exception ex) { Log.Warn("Infinity disabled-state announcement unavailable: " + ex.Message); }
        }

        internal static void InterceptionFailed(string hook, Exception error)
            => DisableFeature(hook + ": " + error.Message, error);

        // #157: an interception exception logs its first stack frames so the next Player.log
        // pinpoints the failing line. Best effort: empty when the runtime stripped the trace.
        // Rides the once-per-distinct-reason gate above; the lobby notice stays reason-only.
        private static string FirstStackFrames(Exception error)
        {
            if (error?.StackTrace == null) return "";
            string[] frames = error.StackTrace.Split('\n');
            var sb = new System.Text.StringBuilder();
            int appended = 0;
            for (int i = 0; i < frames.Length && appended < 3; i++)
            {
                string frame = frames[i].Trim();
                if (frame.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(frame.Length <= 160 ? frame : frame.Substring(0, 160));
                appended++;
            }
            return appended == 0 ? "" : " | stack: " + sb;
        }
        private static readonly HashSet<int> ReferencedModifiers = new HashSet<int>();
        private static readonly List<int> RetiredModifiers = new List<int>();
        private static Actor _ackActor;
        private static readonly Action<DreamforgeInfinityAckMsg, DewPlayer> OnAck = ReceiveAck;
        private static readonly Dictionary<string, long> Acks = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly List<string> DepartedAcks = new List<string>();
        private static string _choiceText;
        private static InfinityChoice _choice;
        private static bool HasNextRoom(ZoneManager zone) => RevealedNext(zone) >= 0;

        internal static InfinityRunState State
        {
            get
            {
                if (ExpeditionHalted) return null;
                var run = ClientSession.HostRun;
                // The first zone can generate after BeginRun already created the run but before
                // InitializeInfinityRun could attach the state (#144: that ordering made this
                // getter return null and OnGenerated's identity check crashed, disabling the
                // feature and leaving a fully normal map). Fall back to the generation template
                // until the run carries its own Infinity state.
                if (run != null && run.RunId == NetworkedManagerBase<GameManager>.softInstance?.runId)
                    return run.Infinity ?? _initial;
                return _initial;
            }
        }
        internal static bool Enabled => Available && !ExpeditionHalted && (NetworkServer.active
            ? State != null || NativeEnvelopePresent || _newInfinity
                || NetworkedManagerBase<GameManager>.softInstance == null && ClientSession.HostChosenInfinityEnabled
            : NativeEnvelopePresent || NetworkedManagerBase<GameManager>.softInstance == null
                && NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.TryGetValue("dreamforge.infinity.enabled", out var enabled) == true && enabled == "1")
            && (NetworkServer.active || ClientSession.RemoteHostInfinityAvailable
                && NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.ContainsKey(HaltKey) != true);
        internal static bool NativeSaveAgreement => Available && !ExpeditionHalted && !_restoring
            && (NetworkServer.active || ClientSession.RemoteHostInfinityAvailable
                && NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.ContainsKey(HaltKey) != true);
        internal static bool IsTechnicalRefresh => _refresh;
        internal static bool CanAdvance => NativeSaveAgreement && ClientSession.HostInfinityCanAdvance;
        internal static bool Restoring => _restoring;
        internal static bool NativeEnvelopePresent => NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.ContainsKey(RuntimeKey) == true;

        internal sealed class Envelope
        {
            public string RunId;
            public int NativeZoneIndex;
            public uint WorldSeed;
            public InfinityRunState State;
        }

        internal sealed class InfinityChoice
        {
            public string RunId;
            public long Revision;
            public long SegmentEpoch;
            public long GraphEpoch;
            public bool Boundary;
            public bool Secure;
            public Pact Pact;
            public string BeforeChoices;
            [JsonIgnore] public RunChoiceSnapshot Before;
        }

        internal static InfinityChoice CurrentChoice
        {
            get
            {
                if (ExpeditionHalted) return null;
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings == null || !settings.customData.TryGetValue(ChoiceKey, out var text)) return null;
                if (text == _choiceText) return _choice;
                _choiceText = text;
                try { _choice = JsonConvert.DeserializeObject<InfinityChoice>(text); }
                catch (JsonException) { _choice = null; DisableFeature("Invalid Infinity choice envelope."); }
                if (_choice != null && RunChoiceSnapshot.TryDecode(_choice.BeforeChoices, out var before)) _choice.Before = before;
                return _choice;
            }
        }

        internal static void ReportGenerationMode(ZoneManager zone, bool active)
        {
            if (!NetworkServer.active) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId) || _generationReportedRun == runId) return;
            _generationReportedRun = runId;
            string context = "run=" + runId + " zone=" + zone.currentZone?.name
                + " selected=" + ClientSession.HostChosenInfinityEnabled
                + " interval=" + ClientSession.HostChosenInfinityInterval;
            if (active)
                Log.Info("Infinity initial map active; " + context + " nodes=" + zone.nodes.Count);
            else
            {
                string reason = !Available ? UnavailableReason
                    : ExpeditionHalted ? ExpeditionHaltNotice
                    : !ClientSession.HostChosenInfinityEnabled ? "lobby selection is OFF"
                    : "Infinity was not armed before initial generation";
                string message = "Infinity initial map uses normal mode; " + context + " reason=" + reason;
                if (ClientSession.HostChosenInfinityEnabled) Log.Warn(message);
                else Log.Info(message);
            }
        }

        internal static void StartNewGame()
        {
            if (!Available) return;
            _restoring = false; _refresh = false;
            _newInfinity = ClientSession.HostChosenInfinityEnabled;
            _initial = null; _runId = null;
            _generationReportedRun = null;
            _choice = null; _choiceText = null; Acks.Clear();
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (NetworkServer.active && settings != null)
            {
                settings.customData.Remove(RuntimeKey);
                settings.customData.Remove(ChoiceKey);
                settings.customData.Remove(HaltKey);
                settings.customData.Remove(RosterHaltKey);
            }
            HostAuthority.CheckInfinityRunCompatibility();
        }

        internal static void BeginRestore()
        {
            _restoring = true; _initial = null; _newInfinity = false; _refresh = false;
            Acks.Clear(); _choice = null; _choiceText = null;
        }
        internal static void FinishRestore()
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (!Available) return;
            if (zone == null)
            {
                _restoring = false;
                if (NativeEnvelopePresent || State != null) DisableFeature("Infinity continue has no native graph.");
                return;
            }
            zone.CallOnReadyAfterTransition(() =>
            {
                try
                {
                    _restoring = false;
                    if (Available) ClientSession.FinishNativeContinueRestore();
                }
                catch (Exception ex) { InterceptionFailed(nameof(FinishRestore), ex); }
            });
        }

        internal static bool TryReadEnvelope(out Envelope envelope)
        {
            envelope = null;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null || !settings.customData.TryGetValue(RuntimeKey, out var text)) return false;
            try { envelope = JsonConvert.DeserializeObject<Envelope>(text); }
            catch (JsonException) { return false; }
            return envelope?.State != null;
        }

        internal static bool MatchesProfile(RunState run)
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (run?.Infinity == null || zone == null || !TryReadEnvelope(out var native)) return false;
            var a = native.State; var b = run.Infinity;
            return native.RunId == run.RunId && native.NativeZoneIndex == zone.currentZoneIndex
                && native.WorldSeed == zone.worldSeed && zone.currentZone != null && zone.currentZone.name == b.FixedZoneId
                && a.FixedZoneId == b.FixedZoneId && a.Interval == b.Interval
                && a.DifficultyId == b.DifficultyId
                && (b.DifficultyId == null || b.DifficultyId == NetworkedManagerBase<GameManager>.softInstance?.difficulty?.name)
                && a.GraphEpoch == b.GraphEpoch && a.SegmentEpoch == b.SegmentEpoch && a.RoomEpoch == b.RoomEpoch
                && a.ClearedCombatTotal == b.ClearedCombatTotal && a.ClearsInCycle == b.ClearsInCycle
                && a.Phase == b.Phase && a.ChoiceRevision == b.ChoiceRevision
                && a.TransitionIntent == b.TransitionIntent && a.SoulObserved == b.SoulObserved
                && a.SettledGraphEpoch == b.SettledGraphEpoch && a.SettledSegmentEpoch == b.SettledSegmentEpoch
                && a.ClearedNodes.SetEquals(b.ClearedNodes);
        }

        internal static void ConfirmAgreement()
        {
            if (!NetworkServer.active) return;
            NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.Remove(HaltKey);
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zone != null) RefreshReveal(zone, zone.currentNodeIndex, RevealedNext(zone));
        }

        internal static void CompleteReturn(InfinityRunState state)
        {
            _initial = state;
            WriteEnvelope();
        }

        internal static void WriteEnvelope()
        {
            if (!NetworkServer.active || !NativeSaveAgreement || State == null) return;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            var gm = NetworkedManagerBase<GameManager>.softInstance;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (zone == null || gm == null || settings == null) return;
            settings.customData[RuntimeKey] = JsonConvert.SerializeObject(new Envelope
            {
                RunId = gm.runId, NativeZoneIndex = zone.currentZoneIndex, WorldSeed = zone.worldSeed, State = State,
            });
        }

        // #144: BeginRun can fire (TrackRun) before the first zone generates, so
        // InitializeInfinityRun found no InitialState to clone and Profile.Run.Infinity stayed
        // null forever. Attach the template as soon as generation produces it.
        private static void AttachInitialToHostRun()
        {
            var run = ClientSession.HostRun;
            if (run == null || run.Infinity != null || _initial == null || _restoring) return;
            if (run.RunId != NetworkedManagerBase<GameManager>.softInstance?.runId) return;
            run.Infinity = _initial.Clone();
            ClientSession.PersistHostInfinityState();
        }
        internal static InfinityRunState InitialState => _initial;

        internal static void OnGenerated(ZoneManager zone)
        {
            HostAuthority.CheckInfinityRunCompatibility();
            if (!NetworkServer.active || !Enabled || _restoring) return;
            var id = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (_runId != id)
            {
                _runId = id; _initial = null;
            }
            var asset = zone.currentZone;
            if (asset == null || asset.useSpecialGeneration || asset.name == "Zone_Primus"
                || asset.startRooms == null || asset.startRooms.Count == 0
                || asset.combatRooms == null || asset.combatRooms.Count == 0
                || asset.bossRooms == null || asset.bossRooms.Count == 0)
            {
                DisableFeature("Infinity requires a normal zone with native start, combat and boss pools."); return;
            }
            if (State == null) _initial = new InfinityRunState
            {
                FixedZoneId = asset.name, Interval = ClientSession.HostChosenInfinityInterval,
                DifficultyId = NetworkedManagerBase<GameManager>.softInstance?.difficulty?.name,
            };
            if (State.FixedZoneId != asset.name) { DisableFeature("Infinity fixed-zone identity changed."); return; }
            AttachInitialToHostRun();
            if (_refresh)
            {
                if (!State.CompleteGraphTransition(State.GraphEpoch + 1))
                { DisableFeature("Infinity graph generation did not match its transition intent."); return; }
                _refresh = false;
            }
            if (zone.nodes.Count > InfinityRunState.MaximumGraphNodes)
            { DisableFeature("Infinity generated graph exceeds its bounded node limit."); return; }
            ReferencedModifiers.Clear(); RetiredModifiers.Clear();
            foreach (var node in zone.nodes)
                if (node.modifiers != null)
                    foreach (var modifier in node.modifiers) ReferencedModifiers.Add(modifier.id);
            // Generation runs after native SerializeRoomData/StopRoom and destruction of room-local actors.
            foreach (var pair in zone.modifierServerData)
                if (!ReferencedModifiers.Contains(pair.Key)) RetiredModifiers.Add(pair.Key);
            foreach (int idToRemove in RetiredModifiers) zone.modifierServerData.Remove(idToRemove);
            ReferencedModifiers.Clear(); RetiredModifiers.Clear();
            RefreshReveal(zone, 0);
            WriteEnvelope();
            ReportGenerationMode(zone, true);
        }

        internal static void Tick()
        {
            if (!Available) return;
            try
            {
                HostAuthority.CheckInfinityRunCompatibility();
                TickNative();
            }
            catch (Exception ex) { InterceptionFailed(nameof(Tick), ex); }
        }

        private static void TickNative()
        {
            if (!NetworkServer.active || !Enabled) return;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            var state = State;
            if (zone == null || state == null) return;
            RegisterAcks();
            if (!NativeSaveAgreement) return;
            if (zone.isInAnyTransition) return;
            if (zone.currentZone == null || zone.currentZone.name != state.FixedZoneId)
            { DisableFeature("Infinity native zone does not match the fixed graph."); return; }
            var room = SingletonDewNetworkBehaviour<Room>.softInstance;
            if (room == null || !room.isActive || zone.currentNodeIndex < 0) return;
            if (state.ClearedCombatTotal == long.MaxValue)
            { DisableFeature("Infinity combat-clear counter exhausted."); return; }
            if (zone.currentNode.type == WorldNodeType.ExitBoss)
            {
                bool soul = false;
                var actors = NetworkedManagerBase<ActorManager>.softInstance;
                if (actors != null && (state.Phase == InfinityPhase.BossFight || state.Phase == InfinityPhase.WaitingSoulFinish))
                    foreach (var actor in actors.allActors)
                        if (actor is Shrine_BossSoul && actor.isActive) { soul = true; break; }
                var before = state.Phase;
                bool seen = state.SoulObserved;
                state.ObserveSoul(soul, room.didClearRoom, Rift_RoomExit.instance != null && !Rift_RoomExit.instance.isLocked);
                if (before != state.Phase || seen != state.SoulObserved) ClientSession.PersistHostInfinityState();
                if (state.Phase == InfinityPhase.AwaitingChoice) ClientSession.OpenHostInfinityChoice();
            }
            else if (state.Phase == InfinityPhase.Exploring
                && room.didClearRoom && !HasNextRoom(zone))
            {
                var choice = CurrentChoice;
                if (choice == null || choice.RunId != ClientSession.HostRun?.RunId || !choice.Boundary || choice.GraphEpoch != state.GraphEpoch)
                    PublishChoice(new InfinityChoice
                    {
                        RunId = ClientSession.HostRun?.RunId, GraphEpoch = state.GraphEpoch,
                        SegmentEpoch = state.SegmentEpoch, Revision = state.ChoiceRevision, Boundary = true,
                    });
            }
        }


        internal static void OnRoomClear(Room room)
        {
            if (!NetworkServer.active || !Enabled || !NativeSaveAgreement) return;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            var state = State;
            if (zone == null || state == null || room != SingletonDewNetworkBehaviour<Room>.softInstance || zone.currentNodeIndex < 0) return;
            if (zone.currentNode.type == WorldNodeType.Combat)
            {
                if (state.TryCountCombatClear(state.GraphEpoch, zone.currentNodeIndex, room.isActive, zone.isInAnyTransition, room.isRevisit))
                {
                    RefreshReveal(zone, zone.currentNodeIndex, RevealedNext(zone));
                    ClientSession.CountHostInfinityRoom();
                }
            }
            else if (zone.currentNode.type == WorldNodeType.ExitBoss && room.isActive && !zone.isInAnyTransition)
            {
                state.ObserveBossClear();
                ClientSession.PersistHostInfinityState();
            }
        }

        internal static bool RouteTravel(ZoneManager zone, ref int to, bool isSidetrackTransition)
        {
            if (!Enabled || !NetworkServer.active) return true;
            var state = State;
            if (state == null || !CanAdvance || !ClientSession.HostInfinityRewardsSettled
                || zone.isInAnyTransition || to < 0 || to >= zone.nodes.Count || isSidetrackTransition) return false;
            if (CurrentChoice != null && CurrentChoice.GraphEpoch == state.GraphEpoch) return false;
            if (state.Phase != InfinityPhase.Exploring && state.Phase != InfinityPhase.BossDue) return false;
            var room = SingletonDewNetworkBehaviour<Room>.softInstance;
            if (room == null || !room.didClearRoom) return false;
            if (!IsRevealDestination(zone, to)) return false;
            if (zone.nodes[to].type == WorldNodeType.ExitBoss && state.Phase != InfinityPhase.BossDue) return false;
            if (state.Phase == InfinityPhase.BossDue && zone.nodes[to].type == WorldNodeType.ExitBoss)
            {
                if (!state.TryEnterBoss()) return false;
                ClientSession.PersistHostInfinityState();
            }
            return true;
        }

        internal static bool Regenerate(string intent)
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            var state = State;
            if (zone == null || state == null || zone.isInAnyTransition || !CanAdvance
                || !ClientSession.HostInfinityBoundarySettled || !ClientSession.HostInfinityRewardsSettled) return false;
            if (state.GraphEpoch == long.MaxValue) { DisableFeature("Infinity graph epoch exhausted."); return false; }
            if (state.Phase != InfinityPhase.Transitioning && !state.BeginGraphTransition(intent)) return false;
            _refresh = true;
            if (!ClientSession.PersistHostInfinityState()) { _refresh = false; DisableFeature("Infinity transition receipt could not be saved."); return false; }
            // noAdvance explicitly retains zoneIndex, tier, loop and ambientLevel. Native LoadNode adopts
            // KO-only revival and resets hunter credit/status/turn; healthy heroes are not fully healed.
            try { zone.TravelToZone(zone.currentZone, noAdvance: true); return true; }
            catch (Exception ex) { InterceptionFailed(nameof(Regenerate), ex); return false; }
        }

        internal static void PublishChoice(InfinityChoice choice)
        {
            Acks.Clear();
            // Freeze terminal participation before the host clears its run profile.
            // Native lobby clients cannot join a run whose party is awaiting return ACKs.
            if (choice.Secure)
                NetworkedManagerBase<GameSettingsManager>.instance.midJoinBanType = MidJoinBanType.GameHasEnded;
            NetworkedManagerBase<GameSettingsManager>.instance.customData[ChoiceKey] = JsonConvert.SerializeObject(choice);
        }

        private static void RegisterAcks()
        {
            var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            if (actor == null || ReferenceEquals(actor, _ackActor)) return;
            if (_ackActor != null)
                try { _ackActor.CustomRpc_UnregisterServerMessageHandler<DreamforgeInfinityAckMsg>(OnAck); }
                catch (Exception) { }
            _ackActor = actor;
            actor.CustomRpc_RegisterServerMessageHandler<DreamforgeInfinityAckMsg>(nameof(DreamforgeInfinityAckMsg), OnAck);
        }

        private static void ReceiveAck(DreamforgeInfinityAckMsg msg, DewPlayer caller)
        {
            if (!Available) return;
            try
            {
                var choice = CurrentChoice;
                if (caller == null || !caller.isHumanPlayer || !DewPlayer.gamePlayers.Contains(caller)
                    || msg == null || msg.protocol != Protocol.Version || choice == null
                    || msg.runId != choice.RunId || msg.revision != choice.Revision
                    || msg.graphEpoch != choice.GraphEpoch || msg.boundary != choice.Boundary) return;
                Acks[caller.guid] = msg.revision;
            }
            catch (Exception ex) { InterceptionFailed(nameof(ReceiveAck), ex); }
        }

        internal static void AcknowledgeLocal(InfinityChoice choice)
        {
            if (DewPlayer.local == null) return;
            if (NetworkServer.active) Acks[DewPlayer.local.guid] = choice.Revision;
            else NetworkedManagerBase<ActorManager>.softInstance?.serverActor?.CustomRpc_SendMessageToServer(
                new DreamforgeInfinityAckMsg
                {
                    protocol = Protocol.Version, runId = choice.RunId, revision = choice.Revision,
                    graphEpoch = choice.GraphEpoch, boundary = choice.Boundary,
                });
        }

        internal static bool PartyAcknowledged(InfinityChoice choice)
        {
            DepartedAcks.Clear();
            foreach (var pair in Acks)
            {
                bool present = false;
                foreach (var player in DewPlayer.gamePlayers) if (player.guid == pair.Key) { present = true; break; }
                if (!present) DepartedAcks.Add(pair.Key);
            }
            foreach (var key in DepartedAcks) Acks.Remove(key);
            foreach (var player in DewPlayer.gamePlayers)
                if (player.isHumanPlayer && (!Acks.TryGetValue(player.guid, out long revision) || revision != choice.Revision)) return false;
            return true;
        }
    }

    [Serializable]
    public sealed class DreamforgeInfinityAckMsg
    {
        public int protocol;
        public string runId;
        public long revision;
        public long graphEpoch;
        public bool boundary;
    }

    [HarmonyPatch(typeof(PlayGameManager), nameof(PlayGameManager.LoadNextZone))]
    internal static class InfinityNextZone
    {
        private static bool Prefix()
        {
            if (!InfinityMode.Available) return true;
            try
            {
                var zone = NetworkedManagerBase<ZoneManager>.softInstance;
                if (zone == null || zone.currentZone == null)
                {
                    if (!InfinityMode.Restoring) InfinityMode.StartNewGame();
                    return true;
                }
                return !InfinityMode.Enabled;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityNextZone), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.GenerateWorldAuto))]
    internal static class InfinityGenerated
    {
        private static readonly AccessTools.FieldRef<ZoneManager, int> NextModifier = SafeReflection.FieldRef<ZoneManager, int>("_nextModifierId");

        private static bool Prepare()
        {
            if (NextModifier != null) return true;
            InfinityMode.DisableFeature("Infinity native modifier identity field is unavailable.");
            return false;
        }

        private static bool Prefix(ZoneManager __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (!InfinityMode.Available)
                {
                    InfinityMode.ReportGenerationMode(__instance, false);
                    return true;
                }
                __state = true;
                if (!InfinityMode.Enabled)
                {
                    InfinityMode.ReportGenerationMode(__instance, false);
                    return true;
                }
                if (NextModifier == null
                    || NextModifier(__instance) >= int.MaxValue - InfinityRunState.MaximumGraphNodes * 16)
                {
                    InfinityMode.DisableFeature("Infinity modifier generation identity is unavailable or exhausted.");
                    return true;
                }
                if (InfinityMode.NativeSaveAgreement) return true;
                __state = false;
                return false;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityGenerated), ex); return true; }
        }

        private static void Postfix(ZoneManager __instance, bool __state)
        {
            if (!InfinityMode.Available || !__state) return;
            try { InfinityMode.OnGenerated(__instance); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityGenerated), ex); }
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.OnStartServer))]
    internal static class InfinityRoomClear
    {
        private static void Postfix(Room __instance)
        {
            if (!InfinityMode.Available) return;
            try
            {
                var room = __instance;
                room.onRoomClear.AddListener(() =>
                {
                    if (!InfinityMode.Available) return;
                    try { InfinityMode.OnRoomClear(room); }
                    catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRoomClear), ex); }
                });
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRoomClear), ex); }
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.StartRoom))]
    internal static class InfinityRoomIdentity
    {
        private static void Prefix()
        {
            if (!InfinityMode.Available) return;
            try
            {
                if (!NetworkServer.active || !InfinityMode.Enabled || !InfinityMode.NativeSaveAgreement) return;
                var state = InfinityMode.State;
                if (state == null) return;
                if (state.RoomEpoch == long.MaxValue)
                { InfinityMode.DisableFeature("Infinity room epoch exhausted."); return; }
                state.RoomEpoch++;
                InfinityMode.WriteEnvelope();
                ClientSession.PersistHostInfinityState();
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityRoomIdentity), ex); }
        }
    }

    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.TravelToNode))]
    internal static class InfinityTravel
    {
        private static bool Prefix(ZoneManager __instance, ref int to, bool isSidetrackTransition)
        {
            if (!InfinityMode.Available) return true;
            int original = to;
            try { return InfinityMode.RouteTravel(__instance, ref to, isSidetrackTransition); }
            catch (Exception ex)
            {
                to = original;
                InfinityMode.InterceptionFailed(nameof(InfinityTravel), ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(RoomRifts), nameof(RoomRifts.CreateSidetrackRift))]
    internal static class InfinityNoSpecialRift
    {
        private static bool Prefix()
        {
            if (!InfinityMode.Available) return true;
            try { return !InfinityMode.Enabled; }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityNoSpecialRift), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.WrapUpAndShowResult))]
    internal static class InfinityResult
    {
        private static bool Prefix(DewGameResult.ResultType type)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                return !InfinityMode.Enabled || type == DewGameResult.ResultType.GameOver
                    || type == DewGameResult.ResultType.Conceded && ClientSession.HostInfinityReturnCommitted;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityResult), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(Rift_RoomExit), "UserCode_TpcInteract__NetworkConnectionToClient")]
    internal static class InfinityExit
    {
        private static bool Prefix()
        {
            if (!InfinityMode.Available) return true;
            try
            {
                if (!InfinityMode.Enabled) return true;
                var zone = NetworkedManagerBase<ZoneManager>.softInstance;
                return zone == null || zone.currentNodeIndex < 0 || zone.currentNode.type != WorldNodeType.ExitBoss;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityExit), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(DewPersistence), nameof(DewPersistence.SerializeGameData))]
    internal static class InfinityNativeSave
    {
        private static void Prefix()
        {
            if (!InfinityMode.Available) return;
            try { ClientSession.PrepareNativeInfinityContinue(); }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityNativeSave), ex); }
        }
    }

    [HarmonyPatch(typeof(DewPersistence), nameof(DewPersistence.ApplyGameData))]
    internal static class InfinityNativeRestore
    {
        private static void Prefix(ref Action onFinish)
        {
            if (!InfinityMode.Available) return;
            try
            {
                InfinityMode.BeginRestore();
                var original = onFinish;
                onFinish = () =>
                {
                    try { original?.Invoke(); }
                    finally
                    {
                        if (InfinityMode.Available)
                            try { InfinityMode.FinishRestore(); }
                            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityNativeRestore), ex); }
                    }
                };
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityNativeRestore), ex); }
        }
    }

    [HarmonyPatch(typeof(ZoneManager), nameof(ZoneManager.TravelToZone))]
    internal static class InfinityZoneTravel
    {
        private static bool Prefix(ZoneManager __instance, Zone prefab, bool noAdvance)
        {
            if (!InfinityMode.Available) return true;
            try
            {
                return !InfinityMode.Enabled || __instance.currentZone == null
                    || InfinityMode.NativeSaveAgreement && InfinityMode.IsTechnicalRefresh
                        && noAdvance && prefab == __instance.currentZone;
            }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityZoneTravel), ex); return true; }
        }
    }

    [HarmonyPatch(typeof(GameMod_StarlessPath), "ClientEventOnActorAdd")]
    internal static class InfinityNoSpecialInvitation
    {
        private static bool Prefix()
        {
            if (!InfinityMode.Available) return true;
            try { return !InfinityMode.Enabled; }
            catch (Exception ex) { InfinityMode.InterceptionFailed(nameof(InfinityNoSpecialInvitation), ex); return true; }
        }
    }
}
