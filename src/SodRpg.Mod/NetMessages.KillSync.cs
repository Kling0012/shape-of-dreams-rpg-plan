using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>Replayable classification for one native death; receiving it alone never grants loot.</summary>
    [Serializable]
    public sealed class DreamforgeMonsterKillMsg
    {
        public int protocol;
        public ulong authorityGeneration;
        public string runId;
        public string eventId;
        public uint netId;
        public int zoneIndex;
        public int affixes;
        public string variantId;
        public long sequence;
        public string streamId;
        public bool recoveredUnknown;
        public string observationSessionId;

        public static DreamforgeMonsterKillMsg FromFact(AuthoritativeRunKill fact, ulong authority) => new DreamforgeMonsterKillMsg
        {
            protocol = Protocol.Version, authorityGeneration = authority, runId = fact.RunId,
            eventId = fact.EventId, netId = fact.MonsterNetId, zoneIndex = fact.ZoneIndex,
            affixes = (int)fact.Nightmare, variantId = fact.VariantId, sequence = fact.Sequence, streamId = fact.StreamId,
        };

        public AuthoritativeRunKill ToFact() => new AuthoritativeRunKill(runId, eventId, netId,
            zoneIndex, (NightmareAffix)affixes, variantId, sequence, streamId);
    }

    [Serializable]
    public sealed class DreamforgeKillStreamReceipt
    {
        public string streamId;
        public long receivedThrough;
    }

    [Serializable]
    public sealed class DreamforgeMissingKillFact
    {
        public string streamId;
        public uint netId;
        public string observationSessionId;
    }

    /// <summary>A durable receipt frontier plus explicit missing-fact recovery requests.</summary>
    [Serializable]
    public sealed class DreamforgeKillReceiptMsg
    {
        public int protocol;
        public ulong authorityGeneration;
        public string runId;
        public string clientId;
        public string observationSessionId;
        public DreamforgeKillStreamReceipt[] receipts;
        public DreamforgeMissingKillFact[] missingFacts;
    }

    [Serializable]
    public sealed class DreamforgeKillReplayStartMsg
    {
        public int protocol;
        public bool clearLiveState;
        public ulong authorityGeneration;
        public string runId;
        public long baseline;
        public string streamId;
    }
}
