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
        public bool fusion;
        // Set by a participant that holds the current view but not the partner's profile for it;
        // older hosts ignore the flag and answer with the plain view.
        public bool wantsProfile;
        public BuildTransferPart Part() => new BuildTransferPart
        { Data = data, TransferId = transferId, Index = index, Count = count, TotalLength = totalLength };
    }

    [Serializable]
    public sealed class DreamforgeCoopTradeState
    {
        public int version = 1;
        public string kind, id, hostKey, partner, ownOffer, otherOffer, offerAck, data, transferId, status;
        // A fixed reason code next to the host-language status, so each player can read it in their own language.
        // Older peers ignore it; a missing or unknown code falls back to status.
        public string reasonCode;
        public string LocalizedStatus() => StatusText()?.ToString();
        internal Txt StatusText() => CoopTradeRules.IsKnownReason(reasonCode)
            ? CoopTradeRules.ReasonMessage(reasonCode) : status == null ? null : new Txt(status, status);
        /// <summary>
        /// The one place where the host pairs a status with its code — for every kind (error, cancelled,
        /// view, roster), so those displays localize the same way. Unknown reasons keep no code and
        /// travel as the raw text, which every receiver can still show.
        /// </summary>
        public DreamforgeCoopTradeState WithReason(string reason)
        {
            status = CoopTradeRules.ReasonText(reason);
            reasonCode = CoopTradeRules.IsKnownReason(reason) ? reason : null;
            return this;
        }
        public int revision, index, count, totalLength;
        public bool incoming, accepted, ownConfirmed, otherConfirmed, preparing;
        public bool fusion, partnerFusion;
        public string[] keys, names;
        public bool[] available, ready;
        public BuildTransferPart Part() => new BuildTransferPart
        { Data = data, TransferId = transferId, Index = index, Count = count, TotalLength = totalLength };
    }

    internal sealed class CoopTradePeer
    {
        public string Key, Name;
        public bool Available, Ready;
    }

    internal sealed class CoopTradeView
    {
        public string Id, PartnerName, Status;
        public bool Incoming, Accepted, OwnConfirmed, OtherConfirmed, Preparing, PartnerFusion;
        public int Revision;
        public CoopTradeOffer OwnOffer = new CoopTradeOffer(), OtherOffer = new CoopTradeOffer();
        public Profile OtherProfile;
    }
}
