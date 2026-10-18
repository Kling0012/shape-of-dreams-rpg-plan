using SodRpg.Core.Game;
using UnityEngine;

namespace UnityEngine
{
    // #771: MOD の検索欄での IME（日本語入力）の再現に必要な、Unity の入力 API の境界。
    // 本物の Input.imeCompositionMode は OS の IME 変換入力の有効・無効を切り替え、
    // Input.compositionString は変換中の未確定文字列を返す（製品コード DreamforgeUi.cs の
    // 星検索でも既に読まれている）。実 Unity プロセスは動かさないため、ここでは状態だけを保持する。
    internal enum IMECompositionMode
    {
        Auto = 0,
        On = 1,
        Off = 2,
    }

    internal static class Input
    {
        internal static IMECompositionMode imeCompositionMode = IMECompositionMode.Auto;
        internal static string compositionString = "";
    }

    internal static class GUIUtility
    {
        // IMGUI のキーボードフォーカス中のコントロール ID。0 は「どのテキスト欄にもフォーカスなし」。
        internal static int keyboardControl;
    }

    internal enum KeyCode
    {
        None,
        Return,
        KeypadEnter,
        Escape,
        Backspace,
        Tab,
    }
}

namespace SodRpg.Mod
{
    // #771: 装備・鍛冶共通の名前検索・星図の星検索・交換タブの検索を NativePersistence.targets が
    // 実ソースから抽出する。ここでは抽出していない周辺（検索結果の再計算・星へのジャンプ）と、
    // 星検索の描画が持つ検索状態だけを置き換える。IME・フォーカス・レイアウトは共用スタブの境界。
    internal sealed partial class DreamforgeUi
    {
        // ---- 未抽出の周辺メソッドの境界 ----
        private void StarRefreshSearch() { }

        // ---- 検索語の状態（_relicSearchFolded は RelicRowCacheUiBoundary の定義を使う） ----
        private string _relicSearch = "";

        // ---- 星検索の状態（DreamforgeUi.cs の宣言と同じ形） ----
        private string _starSearch = "";
        private int[] _starMatches = new int[0];
        private int _starMatchCount, _starMatchCursor = -1;
        private readonly GUIContent _starSearchStatus = new GUIContent { text = "" };
        private GUIStyle _starSearchStyle;
        private static readonly GUILayoutOption[] StarRowHeight = { GUILayout.Height(30) };
        private static readonly GUILayoutOption[] StarKeystoneWidth = { GUILayout.Width(110) };
        private static readonly GUILayoutOption[] StarKeystoneHeaderWidth = { GUILayout.Width(190f) };
        private static readonly GUILayoutOption[] StarFill = { GUILayout.MinWidth(0), GUILayout.ExpandWidth(true) };

        // ---- テストへの内部窓口 ----
        internal void Issue771DrawRelicSearch() => DrawRelicSearch();
        internal void Issue771DrawStarSearch() => StarDrawSearch();
        internal void Issue771DrawCoopTradeFilters() => DrawCoopTradeFilters();
    }
}
