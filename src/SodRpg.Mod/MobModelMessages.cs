using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // A separate protocol namespace deliberately leaves the existing gameplay protocol unchanged.
    [Serializable]
    public sealed class DreamforgeMobHelloMsg
    {
        public int protocol;
        public string clientNonce;
        public long sequence;
        public string sessionNonce;
        public long roomEpoch;
        public bool ready;
        public string compatibilityJson;
    }

    [Serializable]
    public sealed class DreamforgeMobWelcomeMsg
    {
        public int protocol;
        public string clientNonce;
        public string sessionNonce;
        public long roomEpoch;
        // Snapshot must be strictly newer than the host revision at this challenge response.
        public long minimumRevision;
        public string compatibilityJson;
    }

    [Serializable]
    public sealed class DreamforgeMobSnapshotMsg
    {
        public int protocol;
        public string snapshotJson;
    }
}
