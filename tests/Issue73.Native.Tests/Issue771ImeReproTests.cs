using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #771: MOD の画面の検索欄（装備・鍛冶・交換・星図）に日本語（IME）を入力できない再現。
    /// 本体はゲームプレイ中、ControlManager.FrameUpdate（逆コンパイル資料 sod-decomp の
    /// Dew.Core/ControlManager.cs:448）で毎フレーム Input.imeCompositionMode を Off にし続ける。
    /// MOD はパネルを開いても検索欄にフォーカスしても IME を On に戻す箇所がなく、OS の IME が
    /// 無効のままになるため日本語が入力できない。装備・鍛冶共通の名前検索・星図の星検索・交換タブの
    /// 検索を NativePersistence.targets が実ソースから抽出し、フォーカスあり・なしで動かして確かめる。
    /// </summary>
    public sealed class Issue771ImeReproTests
    {
        public Issue771ImeReproTests()
        {
            // フレームの始まり。本体の ControlManager.FrameUpdate と同じ効果で、ゲームプレイ中は
            // 毎フレーム IME が切られ、どのコントロールにもフォーカスしていない状態から始める。
            GUIUtility.keyboardControl = 0;
            GUILayout.TextFieldCount = 0;
            GUI.ResetNamedControls();
            GUI.enabled = true;
            Input.compositionString = "";
            Input.imeCompositionMode = IMECompositionMode.Off;
            Event.current = new Event { type = EventType.Repaint };
        }

        /// <summary>検索欄をクリックしてフォーカスする。検索欄は各描画で最初に描かれるテキスト欄。</summary>
        private static void FocusSearchField() => GUIUtility.keyboardControl = 1;

        [Fact]
        public void GearAndForgeSearchKeepsImeEnabledWhileFocused()
        {
            var ui = new DreamforgeUi(new ClientSession());
            ui.Issue771DrawRelicSearch(); // 1フレーム目: 検索欄を描いてコントロールを確定させる
            FocusSearchField();
            ui.Issue771DrawRelicSearch(); // 2フレーム目: フォーカスしている間の描画
            Assert.True(Input.imeCompositionMode == IMECompositionMode.On,
                "装備・鍛冶タブの名前検索にフォーカスしている間は IME が有効（Input.imeCompositionMode == On）であること");
        }

        [Fact]
        public void StarSearchKeepsImeEnabledWhileFocused()
        {
            var ui = new DreamforgeUi(new ClientSession());
            ui.Issue771DrawStarSearch();
            FocusSearchField();
            // 星検索は製品コードが GUI.SetNextControlName で名前を付けている（Enter 判定と同じ名前）。
            Assert.Equal("DreamforgeStarSearch", GUI.GetNameOfFocusedControl());
            ui.Issue771DrawStarSearch();
            Assert.True(Input.imeCompositionMode == IMECompositionMode.On,
                "星図タブの星検索にフォーカスしている間は IME が有効（Input.imeCompositionMode == On）であること");
        }

        [Fact]
        public void TradeSearchKeepsImeEnabledWhileFocused()
        {
            var ui = new DreamforgeUi(new ClientSession());
            ui.Issue771DrawCoopTradeFilters();
            FocusSearchField();
            ui.Issue771DrawCoopTradeFilters();
            Assert.True(Input.imeCompositionMode == IMECompositionMode.On,
                "交換タブの名前検索にフォーカスしている間は IME が有効（Input.imeCompositionMode == On）であること");
        }

        [Fact]
        public void ImeReturnsToGameModeWhenSearchFieldLosesFocus()
        {
            var ui = new DreamforgeUi(new ClientSession());
            ui.Issue771DrawRelicSearch();
            FocusSearchField();
            ui.Issue771DrawRelicSearch(); // フォーカス中（修正後は IME が On になる）
            GUIUtility.keyboardControl = 0; // 検索欄の外をクリックしてフォーカスを外す
            ui.Issue771DrawRelicSearch();
            Assert.True(Input.imeCompositionMode == IMECompositionMode.Off,
                "フォーカスが外れたら本体が設定したモード（ゲームプレイ中は Off）へ戻すこと");
        }

        [Fact]
        public void StarSearchEnterDuringCompositionDoesNotJump()
        {
            var ui = new DreamforgeUi(new ClientSession());
            ui.Issue771DrawStarSearch();
            FocusSearchField();
            Input.compositionString = "ほし"; // IME 変換中の未確定文字列
            Event.current.type = EventType.KeyDown;
            Event.current.keyCode = KeyCode.Return; // 変換を確定する Enter
            ui.Issue771DrawStarSearch();
            Assert.Equal(EventType.KeyDown, Event.current.type); // イベントを消費して「次の星へ」進まないこと
        }
    }
}
