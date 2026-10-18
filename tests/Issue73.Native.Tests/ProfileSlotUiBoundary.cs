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
        internal static GUILayoutOption ExpandHeight(bool expand) => new GUILayoutOption();
        internal static GUILayoutOption MinHeight(float height) => new GUILayoutOption();
        // #771: 検索欄の IME 再現で抽出した検索欄描画が使う、残りのレイアウト幅。
        internal static GUILayoutOption Height(float height) => new GUILayoutOption();
        internal static GUILayoutOption MinWidth(float width) => new GUILayoutOption();
        internal static GUILayoutOption ExpandWidth(bool expand) => new GUILayoutOption();
        // テキスト欄は描くたびにコントロール ID を1つ進め、名前付けを受けて編集前の文字列を返す。
        internal static int TextFieldCount;
        internal static string TextField(string text, int maxLength, params GUILayoutOption[] options) => DrawnTextField(text);
        internal static string TextField(string text, GUIStyle style, params GUILayoutOption[] options) => DrawnTextField(text);
        private static string DrawnTextField(string text)
        {
            int id = ++TextFieldCount;
            GUI.NameNextDrawnControl(id);
            return text;
        }
        internal static void Label(string text, GUIStyle style, params GUILayoutOption[] options) => Labels.Add(text);
        internal static void Label(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => Labels.Add(content.text);
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
        // #771: 交換タブの検索欄（DrawCoopTradeFilters）が使う折り返しボタン。
        public readonly object ButtonWrap = new object();
    }
}
