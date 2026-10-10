namespace SodRpg.Mod
{
    internal static class NetworkTrafficOptions
    {
        internal static bool SkipUnchanged = true;
        internal static bool Coalesce = true;
        internal static bool OwnerOnly = true;
        internal static bool Compact = true;

        internal static void Configure(bool skipUnchanged, bool coalesce, bool ownerOnly, bool compact)
        {
            SkipUnchanged = skipUnchanged;
            Coalesce = coalesce;
            OwnerOnly = ownerOnly;
            Compact = compact;
        }
    }
}
