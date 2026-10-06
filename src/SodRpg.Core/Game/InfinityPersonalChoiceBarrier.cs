using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Owner-reported selections; profile effects remain authoritative on that owner.</summary>
    public sealed class InfinityPersonalChoice
    {
        public string PlayerId;
        public Pact Pact;
        public Waypoint Waypoint;
        public DreamEvent Event;
        public bool EventCompleted;
        public bool Skipped;
    }

    /// <summary>The personal completion deadline is independent of the final decision's save ACK.</summary>
    public sealed class InfinityPersonalChoiceBarrier
    {
        public const double WaitSeconds = 60;
        private readonly Dictionary<string, InfinityPersonalChoice> _accepted =
            new Dictionary<string, InfinityPersonalChoice>(StringComparer.Ordinal);
        public string RunId { get; }
        public long GraphEpoch { get; }
        public long SegmentEpoch { get; }
        public long Revision { get; }
        public bool Started { get; private set; }
        public bool Released { get; private set; }
        public bool TimedOut { get; private set; }
        public double Deadline { get; private set; }
        public IEnumerable<InfinityPersonalChoice> Accepted => _accepted.Values;

        public InfinityPersonalChoiceBarrier(string runId, long graphEpoch, long segmentEpoch, long revision)
        {
            RunId = runId; GraphEpoch = graphEpoch; SegmentEpoch = segmentEpoch; Revision = revision;
        }

        public bool Matches(string runId, long graphEpoch, long segmentEpoch, long revision) =>
            RunId == runId && GraphEpoch == graphEpoch && SegmentEpoch == segmentEpoch && Revision == revision;

        public void Start(double now)
        {
            if (Started) return;
            Started = true;
            Deadline = now + WaitSeconds;
        }

        public bool TryAccept(string runId, long graphEpoch, long segmentEpoch, long revision,
            InfinityPersonalChoice choice, double now)
        {
            if (!Matches(runId, graphEpoch, segmentEpoch, revision) || choice == null
                || string.IsNullOrEmpty(choice.PlayerId)) return false;
            if (_accepted.TryGetValue(choice.PlayerId, out var previous))
                return previous.Pact == choice.Pact && previous.Waypoint == choice.Waypoint
                    && previous.Event == choice.Event && previous.EventCompleted == choice.EventCompleted
                    && previous.Skipped == choice.Skipped;
            if (Released || Started && now >= Deadline) return false;
            // Freeze the accepted receipt: retransmission must never mutate an earlier choice.
            _accepted.Add(choice.PlayerId, new InfinityPersonalChoice
            {
                PlayerId = choice.PlayerId, Pact = choice.Pact, Waypoint = choice.Waypoint,
                Event = choice.Event, EventCompleted = choice.EventCompleted, Skipped = choice.Skipped,
            });
            return true;
        }

        public bool HasCompleted(string playerId) => playerId != null && _accepted.ContainsKey(playerId);

        public bool TryRelease(IReadOnlyList<string> connectedPlayers, double now)
        {
            if (Released) return true;
            if (!Started) return false;
            bool expired = now >= Deadline;
            for (int i = 0; i < connectedPlayers.Count; i++)
            {
                string player = connectedPlayers[i];
                if (string.IsNullOrEmpty(player) || _accepted.ContainsKey(player)) continue;
                if (!expired) return false;
                _accepted.Add(player, new InfinityPersonalChoice { PlayerId = player, Skipped = true });
                TimedOut = true;
            }
            Released = true;
            return true;
        }
    }
}
