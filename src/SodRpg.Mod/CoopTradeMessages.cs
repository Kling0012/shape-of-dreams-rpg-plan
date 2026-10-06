using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Separate optional RPC family: older Hello/Protocol readers are unchanged.
    [Serializable]
    public sealed class DreamforgeCoopTradeCommand
    {
        public int version = 1;
        public string action, id, target, offer, data, transferId;
        public int revision, index, count, totalLength;
        public bool safe;
        public BuildTransferPart Part() => new BuildTransferPart
        { Data = data, TransferId = transferId, Index = index, Count = count, TotalLength = totalLength };
    }

    [Serializable]
    public sealed class DreamforgeCoopTradeState
    {
        public int version = 1;
        public string kind, id, hostKey, partner, ownOffer, otherOffer, offerAck, data, transferId, status;
        public int revision, index, count, totalLength;
        public bool incoming, accepted, ownConfirmed, otherConfirmed, preparing;
        public string[] keys, names;
        public bool[] available;
        public BuildTransferPart Part() => new BuildTransferPart
        { Data = data, TransferId = transferId, Index = index, Count = count, TotalLength = totalLength };
    }

    internal sealed class CoopTradePeer
    {
        public string Key, Name;
        public bool Available;
    }

    internal sealed class CoopTradeView
    {
        public string Id, PartnerName, Status;
        public bool Incoming, Accepted, OwnConfirmed, OtherConfirmed, Preparing;
        public int Revision;
        public CoopTradeOffer OwnOffer = new CoopTradeOffer(), OtherOffer = new CoopTradeOffer();
        public Profile OtherProfile;
    }
}
