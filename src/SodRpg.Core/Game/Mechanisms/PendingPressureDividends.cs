using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Receipts enter only after host transport and local run/owner identity checks.</summary>
    public sealed partial class PendingPressureDividends
    {
        private readonly Queue<PressureDividendReward> _pending = new Queue<PressureDividendReward>();
        private readonly HashSet<string> _nonces = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _deaths = new HashSet<string>(StringComparer.Ordinal);
        private string _runId;
        private long _retiredBeforeGraph;
        private readonly Dictionary<string, long> _receiptGraphs = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<string> _retired = new List<string>();
        public int Count => _pending.Count;

        public bool AddAuthenticated(PressureDividendReward reward, string authenticatedRunId, string authenticatedOwnerId)
        {
            if (reward == null) throw new ArgumentNullException(nameof(reward));
            if (reward.RunId != authenticatedRunId || reward.OwnerId != authenticatedOwnerId) return false;
            if (_runId != authenticatedRunId)
            {
                Clear();
                _runId = authenticatedRunId;
            }
            if (reward.GraphEpoch < _retiredBeforeGraph) return false;
            if (_nonces.Contains(reward.RewardNonce) || _deaths.Contains(reward.DeathOwnerKey)) return false;
            _nonces.Add(reward.RewardNonce);
            _deaths.Add(reward.DeathOwnerKey);
            _receiptGraphs["n:" + reward.RewardNonce] = reward.GraphEpoch;
            _receiptGraphs["d:" + reward.DeathOwnerKey] = reward.GraphEpoch;
            _pending.Enqueue(reward);
            return true;
        }

        public bool RetireBeforeGraph(long graphEpoch)
        {
            if (graphEpoch <= _retiredBeforeGraph) return true;
            foreach (var pending in _pending) if (pending.GraphEpoch < graphEpoch) return false;
            _retiredBeforeGraph = graphEpoch;
            _retired.Clear();
            foreach (var entry in _receiptGraphs) if (entry.Value < graphEpoch) _retired.Add(entry.Key);
            foreach (string key in _retired)
            {
                if (key.StartsWith("n:", StringComparison.Ordinal)) _nonces.Remove(key.Substring(2));
                else _deaths.Remove(key.Substring(2));
                _receiptGraphs.Remove(key);
            }
            _retired.Clear();
            return true;
        }

        public int Drain(Profile profile, Action<GameEvent> notify = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Run == null || profile.Run.RunId != _runId) return 0;
            int count = 0;
            while (_pending.Count > 0)
            {
                var reward = _pending.Peek();
                if (profile.Run.Infinity != null && reward.GraphEpoch > profile.Run.Infinity.GraphEpoch) break;
                var result = Rules.ApplyPressureDividend(profile, reward);
                _pending.Dequeue();
                count++;
                notify?.Invoke(result);
            }
            return count;
        }

        public void Clear()
        {
            _pending.Clear(); _nonces.Clear(); _deaths.Clear(); _runId = null;
            _receiptGraphs.Clear(); _retiredBeforeGraph = 0;
        }
    }
}
