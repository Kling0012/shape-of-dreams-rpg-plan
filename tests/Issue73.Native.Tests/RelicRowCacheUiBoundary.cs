using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Supply storage and fixed view inputs only. CacheStamp, SortedCached, RowTexts and
    // title formatting are extracted from production source by NativePersistence.targets.
    internal sealed partial class DreamforgeUi
    {
        private Slot _slot = Slot.Weapon;
        private bool _forgeAllSlots = true;
        private string HeroKey => "hero";
        private string _selected = null;
        private readonly Dictionary<float, List<Relic>> _sortedCache = new Dictionary<float, List<Relic>>();
        private readonly Dictionary<float, string> _sortedKey = new Dictionary<float, string>();
        private float _sortedUntil;
        private readonly Dictionary<float, List<string>> _rowCache = new Dictionary<float, List<string>>();
        private readonly Dictionary<float, List<Relic>> _rowCacheFor = new Dictionary<float, List<Relic>>();
        private readonly Dictionary<float, string> _rowCacheKey = new Dictionary<float, string>();
        private readonly HashSet<string> _seenUids = new HashSet<string>();
        private bool _seenInit;

        internal List<string> CachedRelicRows(IEnumerable<Relic> relics, float id)
            => RowTexts(SortedCached(relics, id), HeroKey, id);
    }
}
