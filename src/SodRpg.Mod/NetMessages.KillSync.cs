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

        public static DreamforgeMonsterKillMsg FromFact(AuthoritativeRunKill fact, ulong authority) => new DreamforgeMonsterKillMsg
        {
            protocol = Protocol.Version, authorityGeneration = authority, runId = fact.RunId,
            eventId = fact.EventId, netId = fact.MonsterNetId, zoneIndex = fact.ZoneIndex,
            affixes = (int)fact.Nightmare, variantId = fact.VariantId,
        };

        public AuthoritativeRunKill ToFact() => new AuthoritativeRunKill(runId, eventId, netId,
            zoneIndex, (NightmareAffix)affixes, variantId);
    }
}
