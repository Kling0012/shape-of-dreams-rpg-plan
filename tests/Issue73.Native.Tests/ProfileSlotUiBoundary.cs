using System.Collections.Generic;
using SodRpg.Core.Game;

namespace UnityEngine
{
    // Records the labels the extracted ProfileSlotView draw methods render; layout is not needed.
    internal sealed class GUILayoutOption { }
    internal static class GUILayout
    {
        internal static readonly List<string> Labels = new List<string>();
        internal static void BeginHorizontal() { }
        internal static void EndHorizontal() { }
        internal static void FlexibleSpace() { }
        internal static void Label(string text, object style, params GUILayoutOption[] options) => Labels.Add(text);
        internal static bool Button(string text, object style, params GUILayoutOption[] options)
        {
            Labels.Add(text);
            return false;
        }
        internal static GUILayoutOption Width(float width) => new GUILayoutOption();
    }
}

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        // Profile-slot-facing surface used by the extracted DrawProfileSlots (ProfileSlotView.cs).
        // The real computations live in the unlinked ClientSession partials; the UI test sets the outcomes.
        public ProfileSlot ActiveProfileSlot { get; set; } = ProfileSlot.Solo;
        public ProfileSlotMode ProfileMode { get; set; } = ProfileSlotMode.Auto;
        public bool ProfileSwitchDeferred { get; set; }
        public bool CanCopySoloProfile { get; set; }
        public void RequestSoloProfileCopy() { }
        public string ChooseProfileMode(ProfileSlotMode mode) => null;
    }

    internal sealed partial class DreamforgeUi
    {
        private readonly ClientSession _s;
        private readonly SlotUiStyles _st = new SlotUiStyles();
        private bool _confirmProfileCopy;
        internal string Status;
        internal DreamforgeUi(ClientSession session) { _s = session; }
        internal void DrawProfileSlotBar() => DrawProfileSlots();
        private void SetStatus(string text) => Status = text;
    }

    internal sealed class SlotUiStyles
    {
        public readonly object Header = new object(), Small = new object(), Warn = new object(),
            Button = new object(), ButtonSel = new object();
    }
}
