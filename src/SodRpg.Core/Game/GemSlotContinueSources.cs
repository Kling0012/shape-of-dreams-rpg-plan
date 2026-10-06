using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Tracks checkpoint source confirmation for restored players on their exact peer objects.</summary>
    public sealed class GemSlotContinueSources
    {
        private enum Phase : byte
        {
            AwaitingReceipt,
            ReceiptReceived,
            FreshBuildQueued,
            Committed,
        }

        private struct Member
        {
            public object Peer;
            public Phase Phase;
        }

        private readonly Dictionary<string, Member> _members = new Dictionary<string, Member>(StringComparer.Ordinal);
        private string _runId;
        private string _checkpointId;
        private string _resumeSession;

        public bool HasParticipants => _members.Count != 0;

        public void Reset()
        {
            _members.Clear();
            _runId = null;
            _checkpointId = null;
            _resumeSession = null;
        }

        public void Begin(string runId, string checkpointId, string resumeSession)
        {
            Reset();
            if (string.IsNullOrEmpty(runId) || string.IsNullOrEmpty(checkpointId) || string.IsNullOrEmpty(resumeSession)) return;

            _runId = runId;
            _checkpointId = checkpointId;
            _resumeSession = resumeSession;
        }

        public void Include(string guid)
        {
            if (_runId == null || string.IsNullOrEmpty(guid) || _members.ContainsKey(guid)) return;
            _members.Add(guid, default(Member));
        }

        public bool IsPending(string guid, object peer, string currentRunId)
        {
            return TryGetMember(guid, peer, currentRunId, out var member) && member.Phase != Phase.Committed;
        }

        public void ObserveReceipt(string guid, object peer, string currentRunId, string receiptRunId, string checkpointId, string resumeSession)
        {
            if (!TryGetMember(guid, peer, currentRunId, out var member) || member.Phase != Phase.AwaitingReceipt) return;
            if (!string.Equals(_runId, receiptRunId, StringComparison.Ordinal)
                || !string.Equals(_checkpointId, checkpointId, StringComparison.Ordinal)
                || !string.Equals(_resumeSession, resumeSession, StringComparison.Ordinal)) return;

            member.Phase = Phase.ReceiptReceived;
            _members[guid] = member;
        }

        /// <summary>Only the first complete build after a matching receipt starts fresh-build validation.</summary>
        public bool QueueFreshBuild(string guid, object peer, string currentRunId)
        {
            if (!TryGetMember(guid, peer, currentRunId, out var member) || member.Phase != Phase.ReceiptReceived) return false;

            member.Phase = Phase.FreshBuildQueued;
            _members[guid] = member;
            return true;
        }

        /// <summary>Call only after a queued fresh build passes host validation; rejected builds remain pending.</summary>
        public void CommitFreshBuild(string guid, object peer, string currentRunId)
        {
            if (!TryGetMember(guid, peer, currentRunId, out var member) || member.Phase != Phase.FreshBuildQueued) return;

            member.Phase = Phase.Committed;
            _members[guid] = member;
        }

        private bool TryGetMember(string guid, object peer, string currentRunId, out Member member)
        {
            member = default(Member);
            // Reject stale-run traffic before it can replace the active peer's confirmation.
            if (_runId == null || peer == null || string.IsNullOrEmpty(guid)
                || !string.Equals(_runId, currentRunId, StringComparison.Ordinal)
                || !_members.TryGetValue(guid, out member)) return false;

            if (!ReferenceEquals(member.Peer, peer))
            {
                member.Peer = peer;
                member.Phase = Phase.AwaitingReceipt;
                _members[guid] = member;
            }
            return true;
        }
    }
}
