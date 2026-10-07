using System;
using System.Collections.Generic;
using Mirror;
using Newtonsoft.Json;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static partial class InfinityMode
    {
        internal const string PersonalWaitKey = "dreamforge.infinity.personal-wait";
        private static InfinityPersonalChoiceBarrier _personalBarrier;
        private static InfinityChoice _personalIntent;
        private static Actor _personalActor;
        private static bool _personalServerRegistered, _personalClientRegistered, _personalWarned;
        private static string _personalWaitText;
        private static PersonalChoiceWait _personalWait;
        private static float _nextPersonalWaitPublish;
        private static readonly Action<DreamforgeInfinityPersonalChoiceMsg, DewPlayer> OnPersonalChoice = ReceivePersonalChoice;
        private static readonly Action<DreamforgeInfinityPersonalChoiceAckMsg> OnPersonalChoiceAck = ReceivePersonalChoiceAck;

        internal sealed class PersonalChoiceWait
        {
            public int Version = 1;
            public string RunId;
            public long GraphEpoch, SegmentEpoch, Revision;
            public bool Started;
            public int RemainingSeconds;
        }

        internal static PersonalChoiceWait PersonalWait
        {
            get
            {
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings == null || !settings.customData.TryGetValue(PersonalWaitKey, out var text)) return null;
                if (text == _personalWaitText) return _personalWait;
                _personalWaitText = text;
                try { _personalWait = JsonConvert.DeserializeObject<PersonalChoiceWait>(text); }
                catch (JsonException ex) { _personalWait = null; WarnPersonalChoices("unreadable wait request: " + ex.Message); }
                if (_personalWait?.Version != 1) { _personalWait = null; WarnPersonalChoices("unsupported wait request"); }
                return _personalWait;
            }
        }

        internal static bool PersonalChoicesSettled => _personalBarrier == null || _personalBarrier.Released;
        internal static InfinityPersonalChoiceBarrier PersonalBarrier => _personalBarrier;
        internal static bool PersonalIntentPending => _personalIntent != null;

        internal static void BeginPersonalChoice(RunState run)
        {
            var state = run?.Infinity;
            if (!NetworkServer.active || state == null || state.Phase != InfinityPhase.AwaitingChoice) return;
            if (_personalBarrier?.Matches(run.RunId, state.GraphEpoch, state.SegmentEpoch, state.ChoiceRevision + 1) == true) return;
            ResetPersonalChoices();
            _personalBarrier = new InfinityPersonalChoiceBarrier(run.RunId, state.GraphEpoch, state.SegmentEpoch, state.ChoiceRevision + 1);
            PublishPersonalWait();
        }

        internal static void QueuePersonalIntent(InfinityChoice choice)
        {
            if (choice == null || _personalBarrier == null || _personalIntent != null) return;
            _personalIntent = choice;
            _personalBarrier.Start(Time.unscaledTime);
            _nextPersonalWaitPublish = 0;
            TickPersonalChoices();
        }

        internal static void ResetPersonalChoices()
        {
            _personalBarrier = null; _personalIntent = null;
            _personalWait = null; _personalWaitText = null; _nextPersonalWaitPublish = 0;
            _personalWarned = false;
            if (NetworkServer.active)
                NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.Remove(PersonalWaitKey);
        }

        private static readonly List<string> PersonalPlayerScratch = new List<string>();

        // The host decides through the party choice or by travelling; it never fills in a personal
        // panel, so waiting for its receipt only makes a solo run (or a fully-ready party) idle for the deadline.
        internal static bool IsPersonalChoiceHost(DewPlayer player) =>
            NetworkServer.active && player != null && ReferenceEquals(player, DewPlayer.local);

        internal static void StartPersonalTravelWait()
        {
            if (_personalBarrier == null || _personalBarrier.Released) return;
            _personalBarrier.Start(Time.unscaledTime);
            _nextPersonalWaitPublish = 0;
        }

        internal static void TickPersonalChoices()
        {
            try { TickPersonalChoicesNative(); }
            catch (Exception ex) { WarnPersonalChoices("personal wait unavailable: " + ex.Message); }
        }

        private static void TickPersonalChoicesNative()
        {
            RegisterPersonalChoices();
            if (!NetworkServer.active || _personalBarrier == null) return;
            var state = ClientSession.HostRun?.Infinity;
            if (NetworkedManagerBase<GameManager>.softInstance?.runId != _personalBarrier.RunId
                || state == null || state.GraphEpoch != _personalBarrier.GraphEpoch
                || state.SegmentEpoch != _personalBarrier.SegmentEpoch)
            {
                ResetPersonalChoices();
                return;
            }
            PersonalPlayerScratch.Clear();
            foreach (var player in DewPlayer.gamePlayers)
                if (player != null && player.isHumanPlayer && !IsPersonalChoiceHost(player)) PersonalPlayerScratch.Add(player.guid);
            if (_personalBarrier.TryRelease(PersonalPlayerScratch, Time.unscaledTime))
            {
                if (_personalBarrier.TimedOut) WarnPersonalChoices("personal selection wait exceeded 60 seconds; missing players continue without a choice");
                if (_personalIntent != null)
                {
                    var intent = _personalIntent;
                    intent.PersonalChoices = new List<InfinityPersonalChoice>(_personalBarrier.Accepted);
                    intent.PersonalTimedOut = _personalBarrier.TimedOut;
                    intent.PersonalAckGraceSeconds = Math.Max(0f, Math.Min(30f, (float)(_personalBarrier.Deadline - Time.unscaledTime)));
                    PublishChoice(intent);
                    _personalIntent = null;
                }
                NetworkedManagerBase<GameSettingsManager>.softInstance?.customData.Remove(PersonalWaitKey);
            }
            else if (!_personalBarrier.Released && Time.unscaledTime >= _nextPersonalWaitPublish) PublishPersonalWait();
        }

        private static void PublishPersonalWait()
        {
            var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
            if (settings == null || _personalBarrier == null) return;
            settings.customData[PersonalWaitKey] = JsonConvert.SerializeObject(new PersonalChoiceWait
            {
                RunId = _personalBarrier.RunId, GraphEpoch = _personalBarrier.GraphEpoch,
                SegmentEpoch = _personalBarrier.SegmentEpoch, Revision = _personalBarrier.Revision,
                Started = _personalBarrier.Started,
                RemainingSeconds = _personalBarrier.Started
                    ? Math.Max(0, (int)Math.Ceiling(_personalBarrier.Deadline - Time.unscaledTime)) : 60,
            });
            _nextPersonalWaitPublish = Time.unscaledTime + 1f;
        }

        private static void RegisterPersonalChoices()
        {
            var actor = NetworkedManagerBase<ActorManager>.softInstance?.serverActor;
            if (!ReferenceEquals(actor, _personalActor))
            {
                if (_personalActor != null)
                {
                    if (_personalServerRegistered)
                        try { _personalActor.CustomRpc_UnregisterServerMessageHandler<DreamforgeInfinityPersonalChoiceMsg>(OnPersonalChoice); } catch (Exception) { }
                    if (_personalClientRegistered)
                        try { _personalActor.CustomRpc_UnregisterClientMessageHandler<DreamforgeInfinityPersonalChoiceAckMsg>(OnPersonalChoiceAck); } catch (Exception) { }
                }
                _personalActor = actor;
                _personalServerRegistered = _personalClientRegistered = false;
            }
            if (actor == null) return;
            try
            {
                if (NetworkServer.active && !_personalServerRegistered)
                {
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeInfinityPersonalChoiceMsg>(nameof(DreamforgeInfinityPersonalChoiceMsg), OnPersonalChoice);
                    _personalServerRegistered = true;
                }
                if (!_personalClientRegistered)
                {
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeInfinityPersonalChoiceAckMsg>(OnPersonalChoiceAck);
                    _personalClientRegistered = true;
                }
            }
            catch (Exception ex) { WarnPersonalChoices("transport registration: " + ex.Message); }
        }

        internal static void SubmitPersonalChoice(DreamforgeInfinityPersonalChoiceMsg msg)
        {
            try
            {
                if (NetworkServer.active) ReceivePersonalChoice(msg, DewPlayer.local);
                else if (_personalActor != null) _personalActor.CustomRpc_SendMessageToServer(msg);
                else WarnPersonalChoices("transport unavailable; selections remain pending until the host deadline");
            }
            catch (Exception ex) { WarnPersonalChoices("transport send: " + ex.Message); }
        }

        private static void ReceivePersonalChoice(DreamforgeInfinityPersonalChoiceMsg msg, DewPlayer caller)
        {
            try
            {
                if (!NetworkServer.active || caller == null || !caller.isHumanPlayer || !DewPlayer.gamePlayers.Contains(caller)
                    || msg == null || msg.version != 1 || msg.playerId != caller.guid || _personalBarrier == null) return;
                Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeInfinityPersonalChoiceMsg));
                if (msg.protocol != Protocol.Version) { WarnPersonalChoices("guest protocol mismatch; deadline remains bounded"); return; }
                if ((msg.pact != Pact.None && Pacts.Get(msg.pact) == null)
                    || (msg.waypoint != Waypoint.None && Waypoints.Get(msg.waypoint) == null)
                    || !Enum.IsDefined(typeof(DreamEvent), msg.dreamEvent)
                    || msg.skip && (msg.pact != Pact.None || msg.waypoint != Waypoint.None)) return;
                if (!_personalBarrier.TryAccept(msg.runId, msg.graphEpoch, msg.segmentEpoch, msg.revision,
                    new InfinityPersonalChoice
                    {
                        PlayerId = caller.guid, Pact = msg.pact, Waypoint = msg.waypoint,
                        Event = msg.dreamEvent, EventCompleted = msg.eventCompleted, Skipped = msg.skip,
                    }, Time.unscaledTime)) return;
                var ack = new DreamforgeInfinityPersonalChoiceAckMsg
                {
                    version = 1, protocol = Protocol.Version, playerId = caller.guid, runId = msg.runId,
                    graphEpoch = msg.graphEpoch, segmentEpoch = msg.segmentEpoch, revision = msg.revision,
                };
                if (caller == DewPlayer.local) ReceivePersonalChoiceAck(ack);
                else _personalActor?.CustomRpc_SendMessageToAllClients(ack);
            }
            catch (Exception ex) { WarnPersonalChoices("discarded personal choice: " + ex.Message); }
        }

        private static void ReceivePersonalChoiceAck(DreamforgeInfinityPersonalChoiceAckMsg msg) =>
            ClientSession.ReceiveInfinityPersonalChoiceAck(msg);

        internal static void WarnPersonalChoices(string reason)
        {
            if (_personalWarned) return;
            _personalWarned = true;
            Log.Warn("Infinity personal choices: " + reason + ". Other features remain available.");
        }
    }

    [Serializable]
    public sealed class DreamforgeInfinityPersonalChoiceMsg
    {
        public int version = 1, protocol;
        public string playerId, runId;
        public long graphEpoch, segmentEpoch, revision;
        public Pact pact;
        public Waypoint waypoint;
        public DreamEvent dreamEvent;
        public bool eventCompleted, skip;
    }

    [Serializable]
    public sealed class DreamforgeInfinityPersonalChoiceAckMsg
    {
        public int version, protocol;
        public string playerId, runId;
        public long graphEpoch, segmentEpoch, revision;
    }
}
