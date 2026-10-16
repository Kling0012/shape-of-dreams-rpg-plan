using System;
using System.Collections.Generic;
using System.Globalization;
using SodRpg.Core.Game;
using UnityEngine;

// ───── UnityEngine の代役 ─────
// 本物の GUILayout は行ごとに折り返しラベルの高さ計算を行う。ここでは呼び出しを記録するだけの
// 代役で、1回の OnGUI イベント（= ReproDrawOwnRelics の1呼び出し）で何行描画されたかを数える。
namespace UnityEngine
{
    internal struct Vector2
    {
        internal float x, y;
        internal Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    internal static class Mathf
    {
        internal static float Max(float a, float b) => a > b ? a : b;
        internal static float Min(float a, float b) => a < b ? a : b;
    }

    internal class GUIStyle
    {
        internal readonly string Name;
        internal GUIStyle(string name) { Name = name; }
    }

    internal class GUILayoutOption
    {
        internal readonly string Name;
        private GUILayoutOption(string name) { Name = name; }
        internal static GUILayoutOption Height(float v) => new GUILayoutOption("height:" + Number(v));
        internal static GUILayoutOption Width(float v) => new GUILayoutOption("width:" + Number(v));
        internal static GUILayoutOption MinHeight(float v) => new GUILayoutOption("min-height:" + Number(v));
        internal static GUILayoutOption MinWidth(float v) => new GUILayoutOption("min-width:" + Number(v));
        internal static GUILayoutOption ExpandWidth(bool v = true) => new GUILayoutOption("expand-width:" + v);
        internal static GUILayoutOption ExpandHeight(bool v = true) => new GUILayoutOption("expand-height:" + v);
        private static string Number(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static class GUI
    {
        internal static bool enabled = true;
    }

    internal static class GUILayout
    {
        internal static readonly List<string> Log = new List<string>();
        // Button の描画文本がこれと一致したらクリックされたことにする（無効時は本物と同じく押せない）。
        internal static string ClickText;

        internal static void Reset()
        {
            Log.Clear();
            ClickText = null;
            GUI.enabled = true;
        }

        internal static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) => Log.Add("vertical[" + style.Name + "]");
        internal static void EndVertical() => Log.Add("/vertical");
        internal static void Space(float pixels) => Log.Add("space:" + pixels.ToString("0.###", CultureInfo.InvariantCulture));

        // 本物と同じ GUILayout.Height(...) の形で呼べるようにする転送。
        internal static GUILayoutOption Height(float v) => GUILayoutOption.Height(v);
        internal static GUILayoutOption Width(float v) => GUILayoutOption.Width(v);
        internal static GUILayoutOption MinHeight(float v) => GUILayoutOption.MinHeight(v);
        internal static GUILayoutOption MinWidth(float v) => GUILayoutOption.MinWidth(v);
        internal static GUILayoutOption ExpandWidth(bool v = true) => GUILayoutOption.ExpandWidth(v);
        internal static GUILayoutOption ExpandHeight(bool v = true) => GUILayoutOption.ExpandHeight(v);

        internal static bool Button(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            Log.Add("button[" + style.Name + "]:" + text);
            return GUI.enabled && text == ClickText;
        }

        internal static void Label(string text, GUIStyle style, params GUILayoutOption[] options) => Log.Add("label[" + style.Name + "]:" + text);
    }
}

// ───── SodRpg.Mod 側の代役と、ハーネスが用意する DreamforgeUi の残り部分 ─────
namespace SodRpg.Mod
{
    // CoopTradeMessages.cs の表示用ビュー。本件で触る公開メンバーだけの代役。
    internal sealed class CoopTradeView
    {
        public string Id, PartnerName, Status;
        public bool Incoming, Accepted, OwnConfirmed, OtherConfirmed, Preparing, PartnerFusion;
        public int Revision;
        public CoopTradeOffer OwnOffer = new CoopTradeOffer(), OtherOffer = new CoopTradeOffer();
        public Profile OtherProfile;
    }

    // ClientSession.cs の代役。提示の送信を記録するだけで、実際の通信は行わない。
    internal sealed class ClientSession
    {
        internal Profile Profile;
        internal readonly List<CoopTradeOffer> Offers = new List<CoopTradeOffer>();
        internal string SetCoopTradeOffer(CoopTradeOffer offer)
        {
            Offers.Add(offer);
            return "offer-recorded";
        }
    }

    // UiStyles.cs の代役。抽出した製品コードが触るスタイルだけ。
    internal sealed class UiStyles
    {
        internal readonly GUIStyle Panel = new GUIStyle("panel"), Small = new GUIStyle("small"), Header = new GUIStyle("header"),
            ButtonWrap = new GUIStyle("button-wrap"), RowWrap = new GUIStyle("row-wrap"), ButtonWrapSel = new GUIStyle("row-wrap-sel");
    }

    // 製品コード（ProductionDraw.cs、自動生成）と組み合わせる DreamforgeUi の残りの部分。
    // ここにあるものはすべて本件の測定に不要な簡易版で、製品の該当部分の動作を代弁しない。
    internal sealed partial class DreamforgeUi
    {
        internal readonly ClientSession _s = new ClientSession();
        internal readonly UiStyles _st = new UiStyles();
        // 取引タブの自分側スクロール位置。修正後の仮想化はここから可視範囲を計算する
        // （装備・鍛冶の RelicList における _scrollList と同じ役割）。
        internal Vector2 _tradeOwnScroll;
        internal float _windowWidth = 1830f, _windowHeight = 1010f;
        private readonly Dictionary<Relic, TradeRelicText> _tradeRelicTexts = new Dictionary<Relic, TradeRelicText>();
        internal string _tradeShards = "0", _tradeTuning = "0";
        internal string _tradeSearch = "";
        internal int _tradeRarityFilter = -1, _tradeSlotFilter = -1;
        internal string LastStatus;
        private static readonly GUILayoutOption[] TradeRowSize = { GUILayout.MinHeight(58), GUILayout.MinWidth(0) };
        private static readonly Rarity[] TradeRarities =
        {
            Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary,
        };
        private static readonly Slot[] TradeSlots = { Slot.Weapon, Slot.Head, Slot.Armor, Slot.Hands, Slot.Feet, Slot.Charm };

        private void SetStatus(string status) => LastStatus = status;

        // 本番の CoopTradeRelicText は UiStyles/Links 依存の全文組み立て＋辞書キャッシュ。
        // 行数の計測に文言は要らないので、タイトル形式（○ /● ）とキャッシュだけ本番に合わせた簡易版。
        // 行の識別ができるよう、タイトルに Uid を添える。
        private TradeRelicText CoopTradeRelicText(Relic r)
        {
            if (_tradeRelicTexts.TryGetValue(r, out var text)) return text;
            string title = r.DisplayName + " <" + r.Uid + ">";
            text = new TradeRelicText
            {
                Uid = r.Uid, Name = r.DisplayName, Rarity = r.Rarity, Slot = r.Slot,
                Title = title, IdleTitle = "○ " + title, SelectedTitle = "● " + title,
                Details = title + " Lv" + r.ItemLevel.ToString(CultureInfo.InvariantCulture) + "\n効果1\n効果2",
            };
            _tradeRelicTexts[r] = text;
            return text;
        }

        private sealed class TradeRelicText
        {
            public string Uid, Name, Title, SelectedTitle, IdleTitle, Details;
            public Rarity Rarity;
            public Slot Slot;
        }

        // 選択済み遺物の表示は本件の範囲外。呼ばれたことだけ記録する。
        private void DrawCoopTradeOfferedRelics(Profile profile, CoopTradeOffer offer)
            => GUILayout.Label("offered:" + (offer?.RelicUids.Count ?? 0), _st.Small);

        // テスト駆動口。製品の DrawCoopTradeTab が自分側スクロール欄の中で呼ぶのと同じ呼び方。
        internal void ReproDrawOwnRelics(CoopTradeView trade, bool editable) => DrawCoopTradeOwnRelics(trade, editable);
    }
}
