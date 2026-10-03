using System;

namespace SodRpg.Mod
{
    /// <summary>Optional host-to-owner slot warning; existing protocol messages remain unchanged.</summary>
    [Serializable]
    public class DreamforgeGemSlotConflictMsg
    {
        public uint heroNetId;
        public int protocol;
        public bool disabled;
    }
}
