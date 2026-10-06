using System;

namespace SodRpg.Mod
{
    /// <summary>Legacy optional slot warning retained for wire compatibility; new hosts always clear it.</summary>
    [Serializable]
    public class DreamforgeGemSlotConflictMsg
    {
        public uint heroNetId;
        public int protocol;
        public bool disabled;
    }
}
