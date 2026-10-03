using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>Optional host TargetRpc; it does not change the existing Build grammar.</summary>
    [Serializable]
    public sealed class DreamforgePressureDividendMsg
    {
        public int protocol;
        public uint heroNetId;
        public string runId;
        public int zoneId;
        public long spawnId;
        public string ownerId;
        public string rewardNonce;
        public int shardCount;

        public static DreamforgePressureDividendMsg FromReward(PressureDividendReward reward, uint heroNetId) => new DreamforgePressureDividendMsg
        {
            protocol = Protocol.Version, heroNetId = heroNetId, runId = reward.RunId, zoneId = reward.ZoneId,
            spawnId = reward.SpawnId, ownerId = reward.OwnerId, rewardNonce = reward.RewardNonce, shardCount = reward.ShardCount,
        };

        public PressureDividendReward ToReward()
        {
            if (protocol != Protocol.Version || shardCount != 1) throw new InvalidOperationException("Invalid pressure dividend receipt.");
            return new PressureDividendReward(runId, zoneId, spawnId, ownerId, rewardNonce);
        }
    }
}
