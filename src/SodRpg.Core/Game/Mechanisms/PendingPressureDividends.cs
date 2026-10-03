using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Receipts enter only after host transport and local run/owner identity checks.</summary>
    public sealed class PendingPressureDividends
    {
        private readonly Queue<PressureDividendReward> _pending = new Queue<PressureDividendReward>();
        private readonly HashSet<string> _nonces = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _deaths = new HashSet<string>(StringComparer.Ordinal);
        private string _runId;
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
            if (_nonces.Contains(reward.RewardNonce) || _deaths.Contains(reward.DeathOwnerKey)) return false;
            _nonces.Add(reward.RewardNonce);
            _deaths.Add(reward.DeathOwnerKey);
            _pending.Enqueue(reward);
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
        }
    }
}
