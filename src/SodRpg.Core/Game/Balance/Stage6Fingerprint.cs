using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    internal static class Stage6Fingerprint
    {
        internal static void AddRecords(List<string> records)
        {
            // Original values add nothing: retain the existing negotiated identity.
            // Boss-set drop values already have the existing boss-drop record.
            if (LootBalance.ContentFingerprintRecord != null) records.Add(LootBalance.ContentFingerprintRecord);
            if (EconomyBalance.ContentFingerprintRecord != null) records.Add(EconomyBalance.ContentFingerprintRecord);
            if (PactBalance.ContentFingerprintRecord != null) records.Add(PactBalance.ContentFingerprintRecord);
            if (DailyDreamBalance.ContentFingerprintRecord != null) records.Add(DailyDreamBalance.ContentFingerprintRecord);
            if (WaypointBalance.ContentFingerprintRecord != null) records.Add(WaypointBalance.ContentFingerprintRecord);
            if (EventsBalance.ContentFingerprintRecord != null) records.Add(EventsBalance.ContentFingerprintRecord);
        }
    }
}
