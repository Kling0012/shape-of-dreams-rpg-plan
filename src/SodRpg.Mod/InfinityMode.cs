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
        private static string _runId;
        private static InfinityRunState _initial;
        private static bool _restoring;
        private static bool _newInfinity;
        private static bool _refresh;
        private static Zone _refreshTarget;
        private static Zone _refreshOrigin;
        private static bool _zoneSwitchWarned;
        private static bool _unavailable;
        // Startup failures (a native patch that could not be installed) last until the game restarts.
        private static bool _permanentlyUnavailable;
        // A runtime check that stopped Infinity may only stop it until the expedition that hit it is over.
        private static bool _gameSeenSinceDisable;
        private static string _lastDisableLog;
        private static string _generationReportedRun;
        private static readonly Type[] NativePatchClasses =
        {
            typeof(InfinityNextZone), typeof(InfinityGenerated), typeof(InfinityRoomClear),
            typeof(InfinityRoomIdentity), typeof(InfinityTravel), typeof(InfinityNoSpecialRift),
            typeof(InfinityResult), typeof(InfinityExit), typeof(InfinityNativeSave),
            typeof(InfinityNativeRestore), typeof(InfinityZoneTravel), typeof(InfinityNoSpecialInvitation),
            typeof(InfinityRevealArrival), typeof(InfinityRevealWorld),
            typeof(InfinityTravelCommand), typeof(InfinityRevealQuestOverride),
            typeof(InfinityMapRefresh), typeof(InfinityMapDisable),
            typeof(InfinityMapNodeSetup), typeof(InfinityMapTravelSelection), typeof(InfinityMapMoveSelection),
            typeof(InfinityMapClosestNode), typeof(InfinityMapHover), typeof(InfinityMapNodeTooltip),
            typeof(InfinityMapTooltip), typeof(InfinityMapTravelTooltip), typeof(InfinityMapDescription),
            typeof(InfinityMapPingPosition), typeof(InfinityMapCacheChanged), typeof(InfinityMapEdgeStatus),
            // InfinityHunterAdvance is deliberately absent: its failure degrades only the
            // hunter adjustment (see DreamforgeMod.PatchEachClass), never Infinity itself.
            // InfinityLobbyStartCondition is absent for the same reason: it only adds lobby start
            // messages, so a game build it cannot attach to must not switch Infinity off.
         };

        internal static bool Available { get; private set; }
        internal static string UnavailableReason { get; private set; }
        // #144: the lobby line must identify the cause when a player reports it. The first reason is
        // appended in its internal wording, cut at 120 chars so the one-line report stays readable.
        private const int NoticeReasonLimit = 120;
        internal static string UnavailableNotice => Loc.T(
            "インフィニティは無効です。通常モードは利用できます。", "Infinity is disabled. Normal mode remains available.")
            + (string.IsNullOrEmpty(UnavailableReason) ? ""
                : " " + Loc.T("（理由: ", "(Reason: ") + NoticeReason(UnavailableReason) + Loc.T("）", ")") + MoreReasons);

        private static readonly HashSet<string> DisableReasons = new HashSet<string>(StringComparer.Ordinal);
        private static string MoreReasons => DisableReasons.Count > 1
            ? Loc.T($"（ほか{DisableReasons.Count - 1}件はログ）", $" (+{DisableReasons.Count - 1} more in the log)") : "";

        private static string NoticeReason(string reason) => reason.Length <= NoticeReasonLimit
            ? reason : reason.Substring(0, NoticeReasonLimit);

        internal static bool IsNativePatch(Type type) => Array.IndexOf(NativePatchClasses, type) >= 0;

        // The lobby start guard (InfinityLobbyStartCondition) only turns a start into a message: "Infinity is
        // unavailable" and "not together with Limbo". When its patch cannot be installed on the player's game build,
        // Infinity keeps working and the one rule that must still hold (no Infinity in Limbo) is applied here instead.
        internal static bool LobbyStartGuardMissing { get; private set; }

        internal static void LobbyStartGuardUnavailable(string reason)
        {
            if (LobbyStartGuardMissing) return;
            LobbyStartGuardMissing = true;
            Log.Warn("Infinity lobby start guard unavailable; Infinity continues without its start messages. " + reason);
        }

        internal static bool LimboBlocksInfinity => LobbyStartGuardMissing
            && NetworkedManagerBase<GameSettingsManager>.softInstance?.difficulty == "diffLimbo";

        internal static void CompletePatchInstallation(int installedCount)
        {
            if (installedCount != NativePatchClasses.Length)
                DisablePermanently("Infinity native interception is incomplete.");
            if (!_unavailable) Available = true;
        }

        internal static void DisableFeature(string reason) => DisableFeature(reason, null);

        /// <summary>A native patch is missing: nothing the player does in game can bring it back, so the stop outlasts expeditions.</summary>
        internal static void DisablePermanently(string reason)
        {
            _permanentlyUnavailable = true;
            DisableFeature(reason, null);
        }

        /// <summary>
        /// A check that stops Infinity while an expedition runs (save receipts, native hooks, a transient exception) used
        /// to keep it off until the game was restarted, so every later lobby showed "Infinity is disabled" and could
        /// not start it. The stop now ends with the expedition: once a game has run since the stop and the player is back
        /// outside of it, Infinity is available again. A stop that happens again simply stops it again, and a missing
        /// native patch (DisablePermanently) is never lifted. The game-seen gate keeps a fault that fires on every
        /// lobby frame from flipping the feature on and off.
        /// </summary>
        internal static void RecoverAfterExpedition(bool inGame)
        {
            if (!_unavailable || _permanentlyUnavailable) return;
            if (inGame) { _gameSeenSinceDisable = true; return; }
            if (!_gameSeenSinceDisable) return;
            string reason = UnavailableReason;
            _unavailable = false;
            _gameSeenSinceDisable = false;
            UnavailableReason = null;
            _lastDisableLog = null;
            DisableReasons.Clear();
            PresentationFailures.Clear();
            Available = true;
            Log.Info("Infinity is available again after the expedition that stopped it. Earlier reason: " + reason);
        }

        internal static void DisableFeature(string reason, Exception error)
        {
            Available = false;
            ClearPendingTravel();
            _restoring = false;
            _refresh = false;
            _newInfinity = false;
            // #144: every distinct check that stops Infinity is logged by name; the first reason
            // stays in the lobby notice. Later checks used to be swallowed silently, hiding which
            // patch or interception actually disabled the feature on a player's machine.
            DisableReasons.Add(reason);
            if (reason != _lastDisableLog)
            {
                _lastDisableLog = reason;
                Log.Warn("Infinity disabled; normal mode remains available. " + reason + FirstStackFrames(error));
            }
            if (_unavailable) return;
            _unavailable = true;
            _gameSeenSinceDisable = NetworkedManagerBase<GameManager>.softInstance != null;
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

        // The map display hooks only decide what the world map shows (the server routes every travel itself), and
        // they run every frame on native UI objects that can be mid-rebuild. One bad frame must not end Infinity for
        // the session; a hook that keeps failing still stops it, so it cannot throw on every frame.
        private const int PresentationFailureLimit = 5;
        private static readonly Dictionary<string, int> PresentationFailures = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static void PresentationFailed(string hook, Exception error)
        {
            PresentationFailures.TryGetValue(hook, out int count);
            PresentationFailures[hook] = ++count;
            if (count >= PresentationFailureLimit) { InterceptionFailed(hook, error); return; }
            Log.Warn($"Infinity map display hook {hook} failed ({count}/{PresentationFailureLimit}); Infinity continues: {error.Message}");
        }

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
        private const float ChoiceAckGraceSeconds = 30f;
        private static InfinityChoice _ackWaitingChoice;
        private static float _ackWaitStarted;
        private static bool _ackWaitReleased;
        private static string _choiceText;
        private static InfinityChoice _choice;
        private static bool HasNextRoom(ZoneManager zone) => RevealedNext(zone) >= 0;

        internal static InfinityRunState State
        {
            get
            {
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
        internal static bool Enabled => Available && (NetworkServer.active
            ? State != null || NativeEnvelopePresent || _newInfinity
                || NetworkedManagerBase<GameManager>.softInstance == null && ClientSession.HostChosenInfinityEnabled
            : NativeEnvelopePresent || NetworkedManagerBase<GameManager>.softInstance == null
                && NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.TryGetValue("dreamforge.infinity.enabled", out var enabled) == true && enabled == "1")
            && (NetworkServer.active
                || NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.ContainsKey(HaltKey) != true);
        internal static bool NativeSaveAgreement => Available && !_restoring
            && (NetworkServer.active
                || NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.ContainsKey(HaltKey) != true);
        internal static bool IsTechnicalRefresh => _refresh;
        internal static bool CanAdvance => NativeSaveAgreement && ClientSession.HostInfinityCanAdvance && PersonalChoicesSettled;
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
            public List<InfinityPersonalChoice> PersonalChoices;
            public bool PersonalTimedOut;
            public float PersonalAckGraceSeconds = ChoiceAckGraceSeconds;
            [JsonIgnore] public RunChoiceSnapshot Before;
        }

        internal static InfinityChoice CurrentChoice
        {
            get
            {
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings == null || !settings.customData.TryGetValue(ChoiceKey, out var text)) return null;
                if (text == _choiceText) return _choice;
                _choiceText = text;
                try { _choice = JsonConvert.DeserializeObject<InfinityChoice>(text); }
                catch (JsonException ex) { _choice = null; Log.Warn("Infinity: discarded unreadable choice envelope: " + ex.Message); }
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
            ClearPendingTravel();
            ResetServiceRoomWarnings();
            ResetPersonalChoices();
            ResetPersonalWaypoints(clearSettings: true);
            _restoring = false; _refresh = false;
            _hunterAdjustSuspended = false;
            _hunterMoveCounter = 0;
            _newInfinity = ClientSession.HostChosenInfinityEnabled;
            _initial = null; _runId = null;
            _generationReportedRun = null;
            _choice = null; _choiceText = null; Acks.Clear();
            _ackWaitingChoice = null;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (NetworkServer.active && settings != null)
            {
                settings.customData.Remove(RuntimeKey);
                settings.customData.Remove(ChoiceKey);
                settings.customData.Remove(HaltKey);
                settings.customData.Remove(BossKey);
            }
            HostAuthority.CheckInfinityRunCompatibility();
        }

        internal static void BeginRestore()
        {
            _restoring = true; _initial = null; _newInfinity = false; _refresh = false;
            ClearPendingTravel();
            ResetPersonalChoices();
            ResetPersonalWaypoints(clearSettings: false);
            _hunterAdjustSuspended = false;
            _hunterMoveCounter = 0;
            Acks.Clear(); _choice = null; _choiceText = null;
            _ackWaitingChoice = null;
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
            // ApplyGameData's completion already follows native room restoration. Waiting again
            // on GameManager.CallOnReady can strand even normal runs behind this global flag.
            _restoring = false;
            ClientSession.FinishNativeContinueRestore();
        }

        internal static bool TryReadEnvelope(out Envelope envelope)
        {
            envelope = null;
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null || !settings.customData.TryGetValue(RuntimeKey, out var text)) return false;
            try { envelope = JsonConvert.DeserializeObject<Envelope>(text); }
            catch (JsonException ex) { Log.Warn("Infinity: discarded unreadable runtime envelope: " + ex.Message); return false; }
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
            if (!HasNativeRoomPools(asset))
            {
                if (_refresh) throw new InvalidOperationException("Infinity target has no native start/combat/boss pools.");
                DisableFeature("Infinity requires native start, combat and boss pools."); return;
            }
            if (State == null) _initial = new InfinityRunState
            {
                FixedZoneId = asset.name, Interval = ClientSession.HostChosenInfinityInterval,
                DifficultyId = NetworkedManagerBase<GameManager>.softInstance?.difficulty?.name,
            };
            if (State.FixedZoneId != asset.name && (!_refresh || asset != _refreshTarget))
            { DisableFeature("Infinity current-zone identity changed outside its transition."); return; }
            AttachInitialToHostRun();
            if (zone.nodes.Count > InfinityRunState.MaximumGraphNodes || _refresh && zone.nodes.Count < 3)
            {
                if (_refresh) throw new InvalidOperationException("Infinity target graph has an unsupported node count.");
                DisableFeature("Infinity generated graph exceeds its bounded node limit."); return;
            }
            if (_refresh)
            {
                if (!State.CompleteGraphTransition(State.GraphEpoch + 1))
                { DisableFeature("Infinity graph generation did not match its transition intent."); return; }
                if (State.FixedZoneId != asset.name)
                {
                    // Native prewarm retires monster ability pools by zoneIndex, which noAdvance
                    // retains. Invalidate its public marker only on an actual biome change.
                    var spawn = ManagerBase<SpawnManager>.instance;
                    if (spawn != null) spawn.lastClearedZoneIndex = -1;
                }
                State.FixedZoneId = asset.name;
                _refresh = false;
            }
            if (IsBossFinaleZone(asset, State.Interval) && State.PromoteFinaleBoss())
            {
                // A special-generation graph too small for the cycle (the pure-white route) ends
                // at its boss: the cycle is due on arrival, so the map reveals the boss node
                // instead of dead-ending the party into a forced boundary regeneration.
                ClientSession.PersistHostInfinityState();
            }
            ApplyBossRoom(zone);
            EnsureServiceRooms(zone);
            ReferencedModifiers.Clear(); RetiredModifiers.Clear();
            foreach (var node in zone.nodes)
                if (node.modifiers != null)
                    foreach (var modifier in node.modifiers) ReferencedModifiers.Add(modifier.id);
            // Generation runs after native SerializeRoomData/StopRoom and destruction of room-local actors.
            foreach (var pair in zone.modifierServerData)
                if (!ReferencedModifiers.Contains(pair.Key)) RetiredModifiers.Add(pair.Key);
            foreach (int idToRemove in RetiredModifiers) zone.modifierServerData.Remove(idToRemove);
            // #229: restart the hunt from the far side of the entry instead of the native
            // exit-farthest (entry-side) start that spawns on top of the one-room reveal path.
            _hunterMoveCounter = 0;
            RelocateHunterStart(zone);
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
                TickPersonalChoices();
                TickPendingTravel();
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
            if (!NativeSaveAgreement || !ClientSession.HostInfinitySaveHoldSatisfied) return;
            if (zone.isInAnyTransition) return;
            if (zone.currentZone == null || zone.currentZone.name != state.FixedZoneId)
            { DisableFeature("Infinity native zone does not match the fixed graph."); return; }
            var room = SingletonDewNetworkBehaviour<Room>.softInstance;
            if (room == null || !room.isActive || zone.currentNodeIndex < 0) return;
            if (state.ClearedCombatTotal == long.MaxValue)
            { DisableFeature("Infinity combat-clear counter exhausted."); return; }
            if (zone.currentNode.type == WorldNodeType.ExitBoss)
            {
                // The pure-white preparation gate (Shrine_PrimusDoor) loads the boss node
                // directly, bypassing RouteTravel: arriving there makes this graph's boss the
                // cycle boss (finale graphs are due on arrival) and starts the fight.
                var beforeArrival = state.Phase;
                if (IsBossFinaleZone(zone.currentZone, state.Interval)) state.PromoteFinaleBoss();
                state.TryEnterBoss();
                if (beforeArrival != state.Phase) ClientSession.PersistHostInfinityState();
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
                    // Arrival already chose the next room. Only the boss threshold changes
                    // that projection; ordinary clears need no graph scan or SyncList writes.
                    if (state.Phase == InfinityPhase.BossDue)
                        RefreshReveal(zone, zone.currentNodeIndex);
                    ClientSession.CountHostInfinityRoom();
                }
            }
            else if (zone.currentNode.type == WorldNodeType.ExitBoss && room.isActive && !zone.isInAnyTransition)
            {
                if (state.ObserveBossClear()) ClientSession.PersistHostInfinityState();
            }
        }

        internal static bool RouteTravel(ZoneManager zone, ref int to, bool isSidetrackTransition,
            bool advanceTurn, bool ignoreInterrupts)
        {
            if (!Enabled || !NetworkServer.active) return true;
            var state = State;
            if (isSidetrackTransition || !IsTravelRequestValid(zone, state, to)) return false;
            StartPersonalTravelWait();
            if (!CanAdvance || !ClientSession.HostInfinityRewardsSettled)
            {
                HoldTravel(zone, state, to, advanceTurn, ignoreInterrupts);
                return false;
            }
            ClearPendingTravel();
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
            if (!ClientSession.PersistHostInfinityState()) { _refresh = false; return false; }
            _refreshOrigin = zone.currentZone;
            _refreshTarget = _refreshOrigin;
            _zoneSwitchWarned = false;
            if (intent == "delve")
                try
                {
                    _refreshTarget = PrepareBossTarget(_refreshOrigin, state);
                }
                catch (Exception ex) { WarnZoneSwitch(ex.Message); }
            else if (!CanHostInterval(_refreshOrigin, state.Interval))
            {
                // The current zone (e.g. a save made before too-small special zones were excluded
                // from the draw) cannot host the cycle: regenerating it would dead-end again and
                // force yet another move. Leave for a zone the run can actually play.
                try { _refreshTarget = ChooseNativeZone(state); }
                catch (Exception ex) { WarnZoneSwitch(ex.Message); }
            }
            // noAdvance retains native index/tier/loop/ambient difficulty. The selected prefab owns
            // enemies, scenes and boss pools; depth and Infinity pressure remain the scaling axes.
            return TravelGraph(zone);
        }

        private static bool HasNativeRoomPools(Zone asset)
            => asset != null && asset.startRooms != null && asset.startRooms.Count > 0
                && asset.combatRooms != null && asset.combatRooms.Count > 0
                && (!asset.useSpecialGeneration || asset.specialNodes >= 3)
                && asset.bossRooms != null && asset.bossRooms.Count > 0;

        private static void WarnZoneSwitch(string reason)
        {
            if (_zoneSwitchWarned) return;
            _zoneSwitchWarned = true;
            Log.Warn("Infinity zone switch failed; continuing in the previous zone for this graph. " + reason);
        }

        internal static bool HandleZoneGenerationFailure(Exception error)
        {
            if (!_refresh || _refreshTarget == _refreshOrigin) return false;
            WarnZoneSwitch(error.Message);
            return true;
        }

        internal static void GenerateRefresh(ZoneManager zone)
        {
            uint seed = RefreshWorldSeed;
            try
            {
                zone.GenerateWorldWithSeed(seed);
                if (zone.nodes.Count < 3 || zone.nodes.Count > InfinityRunState.MaximumGraphNodes)
                    throw new InvalidOperationException("Infinity target graph has an unsupported node count.");
            }
            catch (Exception ex)
            {
                if (_refreshTarget == _refreshOrigin) throw;
                WarnZoneSwitch(ex.Message);
                _refreshTarget = _refreshOrigin;
                FallBackBoss(_refreshOrigin, ex.Message);
                // Generation precedes scene load/native Continue serialization. Restore the
                // public native zone and live LoadNode request before either can save a
                // target-zone identity with a previous-zone graph after a caught native error.
                zone.currentZone = _refreshOrigin;
                zone.lastLoadNodeSettings.newZone = _refreshOrigin;
                zone.GenerateWorldWithSeed(seed);
            }
        }

        private static bool TravelGraph(ZoneManager zone)
        {
            try
            {
                zone.TravelToZone(_refreshTarget, noAdvance: true);
                zone.CallOnReadyAfterTransition(() =>
                {
                    // LoadNode catches native generation errors inside its coroutine. Detect a
                    // rejected/incomplete switch too, not only synchronous API exceptions.
                    if (_refresh && _refreshTarget != _refreshOrigin)
                        FallbackGraph(zone, "Native zone generation did not complete.");
                });
                return true;
            }
            catch (Exception ex)
            {
                if (_refreshTarget != _refreshOrigin) return FallbackGraph(zone, ex.Message);
                _refresh = false;
                WarnZoneSwitch(ex.Message);
                return false;
            }
        }

        private static bool FallbackGraph(ZoneManager zone, string reason)
        {
            WarnZoneSwitch(reason);
            _refreshTarget = _refreshOrigin;
            FallBackBoss(_refreshOrigin, reason);
            return TravelGraph(zone);
        }

        internal static bool IsRefreshTarget(Zone prefab) => _refresh && prefab == _refreshTarget;

        internal static uint RefreshWorldSeed
        {
            get
            {
                var rng = new Rng(Rng.SeedFrom(ClientSession.HostRun.RunId + ":infinity-world:")
                    + unchecked((ulong)(State.GraphEpoch + 1)));
                uint seed = (uint)rng.NextULong();
                return seed == 0 ? 1u : seed;
            }
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
                    || msg == null || choice == null
                    || msg.runId != choice.RunId || msg.revision != choice.Revision
                    || msg.graphEpoch != choice.GraphEpoch || msg.boundary != choice.Boundary) return;
                Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeInfinityAckMsg));
                Acks[caller.guid] = msg.revision;
            }
            catch (Exception ex) { Log.Warn("Infinity: discarded invalid ACK: " + ex.Message); }
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
            if (choice == null) return false;
            if (!ReferenceEquals(choice, _ackWaitingChoice))
            {
                _ackWaitingChoice = choice;
                _ackWaitStarted = UnityEngine.Time.unscaledTime;
                _ackWaitReleased = false;
            }
            DepartedAcks.Clear();
            foreach (var pair in Acks)
            {
                bool present = false;
                foreach (var player in DewPlayer.gamePlayers) if (player.guid == pair.Key) { present = true; break; }
                if (!present) DepartedAcks.Add(pair.Key);
            }
            foreach (var key in DepartedAcks) Acks.Remove(key);
            bool missingRemote = false;
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (player == null || !player.isHumanPlayer
                    || Acks.TryGetValue(player.guid, out long revision) && revision == choice.Revision) continue;
                // A remote timeout cannot manufacture the host's local save/choice receipt.
                if (player == DewPlayer.local) return false;
                missingRemote = true;
            }
            if (!missingRemote) return true;
            // Personal no-response already consumed its full 60-second allowance.
            // Do not add another remote save ACK grace period to that same no-response.
            if (choice.PersonalTimedOut) return true;
            float remoteGrace = Math.Max(0f, Math.Min(ChoiceAckGraceSeconds, choice.PersonalAckGraceSeconds));
            if (UnityEngine.Time.unscaledTime - _ackWaitStarted < remoteGrace) return false;
            if (!_ackWaitReleased)
            {
                _ackWaitReleased = true;
                Log.Warn($"Infinity: choice ACK wait exceeded {remoteGrace:0} seconds; host progression continues without remote confirmation.");
            }
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
            InfinityMode.DisablePermanently("Infinity native modifier identity field is unavailable.");
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
                if (InfinityMode.NativeSaveAgreement)
                {
                    if (!InfinityMode.IsTechnicalRefresh) return true;
                    // Public seeded generation shares native graph/boss logic, without consuming
                    // a mutable cached random cursor that Continue or a failed switch could alter.
                    InfinityMode.GenerateRefresh(__instance);
                    return false;
                }
                __state = false;
                return false;
            }
            catch (Exception ex)
            {
                if (InfinityMode.HandleZoneGenerationFailure(ex)) { __state = false; return false; }
                InfinityMode.InterceptionFailed(nameof(InfinityGenerated), ex);
                return true;
            }
        }

        private static void Postfix(ZoneManager __instance, bool __state)
        {
            if (!InfinityMode.Available || !__state) return;
            try { InfinityMode.OnGenerated(__instance); }
            catch (Exception ex)
            {
                if (!InfinityMode.HandleZoneGenerationFailure(ex))
                    InfinityMode.InterceptionFailed(nameof(InfinityGenerated), ex);
            }
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
        private static bool Prefix(ZoneManager __instance, ref int to, bool isSidetrackTransition,
            bool advanceTurn, bool ignoreInterrupts)
        {
            if (!InfinityMode.Available) return true;
            int original = to;
            try { return InfinityMode.RouteTravel(__instance, ref to, isSidetrackTransition, advanceTurn, ignoreInterrupts); }
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
                        && noAdvance && InfinityMode.IsRefreshTarget(prefab);
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

    [HarmonyPatch]
    internal static class InfinityBossSoulDeath
    {
        private static Action<BossMonster, EventInfoKill> _nativeDeath;
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Mon_Primus_BossPrimusAeron), "OnDeath");
            yield return AccessTools.Method(typeof(Mon_Special_BossMaw), "OnDeath");
            yield return AccessTools.Method(typeof(Mon_Special_BossPolaris), "OnDeath");
        }
        private static bool Prepare()
        {
            try
            {
                _nativeDeath = AccessTools.MethodDelegate<Action<BossMonster, EventInfoKill>>(
                    AccessTools.Method(typeof(BossMonster), "OnDeath"), virtualCall: false);
                return _nativeDeath != null;
            }
            catch (Exception ex)
            {
                Log.Warn("Infinity generic boss-soul hook unavailable; affected draws use the zone's native boss. " + ex.Message);
                return false;
            }
        }

        internal static bool IsInstalled(string bossTypeName)
        {
            if (_nativeDeath == null) return false;
            Type type = bossTypeName == "Mon_Primus_BossPrimusAeron" ? typeof(Mon_Primus_BossPrimusAeron)
                : bossTypeName == "Mon_Special_BossMaw" ? typeof(Mon_Special_BossMaw)
                : bossTypeName == "Mon_Special_BossPolaris" ? typeof(Mon_Special_BossPolaris) : null;
            if (type == null) return false;
            var info = Harmony.GetPatchInfo(AccessTools.Method(type, "OnDeath"));
            if (info == null) return false;
            foreach (var patch in info.Prefixes)
                if (PatchMethodOwnership.DeclaresPatch(patch, typeof(InfinityBossSoulDeath))) return true;
            return false;
        }

        private static bool Prefix(BossMonster __instance, EventInfoKill info)
        {
            if (!NetworkServer.active || !InfinityMode.Enabled) return true;
            // These bosses skip the normal soul or award Dust directly. Bypass the specialized
            // death flow: one actual-type soul/reward, no ending or duplicate native payout.
            __instance.skipBossSoulFlow = false;
            _nativeDeath(__instance, info);
            return false;
        }
    }
}
