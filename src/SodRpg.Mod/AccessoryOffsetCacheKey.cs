namespace SodRpg.Mod
{
    internal readonly struct AccessoryOffsetCacheKey
    {
        private readonly string _name;
        private readonly object _offsets;
        private readonly int _count;

        internal AccessoryOffsetCacheKey(string name, object offsets, int count)
        {
            _name = name;
            _offsets = offsets;
            _count = count;
        }

        internal bool Matches(string name, object offsets, int count)
            => ReferenceEquals(_name, name) && ReferenceEquals(_offsets, offsets) && _count == count;
    }
}
