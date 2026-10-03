using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Line = SodRpg.Core.Game.Line;
    using Power = SodRpg.Core.Game.Power;
    using Rarity = SodRpg.Core.Game.Rarity;
    using Slot = SodRpg.Core.Game.Slot;
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>IMGUI の見た目。日本語を表示できるよう OS のフォントを動的に読み込む。</summary>
    internal sealed class UiStyles
    {
        private static readonly string[] FontCandidates =
        {
            "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "Microsoft YaHei UI", "Microsoft YaHei",
            "Hiragino Sans", "Noto Sans CJK JP", "Noto Sans JP", "Arial",
        };

        public Font Font;
        public GUIStyle ToastMeasure, Panel, Window, Title, Label, Small, Header, Button, ButtonSel, Tab, TabSel, Row, RowWrap, RowSel, Toast, Hud, Warn, ButtonWrap, ButtonWrapSel, OptionChosen, OptionIdle, TagChosen, TagBlocked;
        private bool _built;
        private readonly System.Collections.Generic.List<Texture2D> _textures = new System.Collections.Generic.List<Texture2D>();

        /// <summary>作ったフォントとテクスチャを破棄する（ライブリロード時に残さない）。</summary>
        public void Dispose()
        {
            foreach (var t in _textures)
                if (t != null) UnityEngine.Object.Destroy(t);
            _textures.Clear();
            if (Font != null) UnityEngine.Object.Destroy(Font);
            Font = null;
            _built = false;
        }

        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            try
            {
                Font = Font.CreateDynamicFontFromOSFont(FontCandidates, 16);
            }
            catch (Exception)
            {
                Font = null;
            }

            var bgDark = Tex(new Color(0.06f, 0.06f, 0.10f, 0.94f));
            var bgPanel = Tex(new Color(0.11f, 0.11f, 0.17f, 0.92f));
            var bgHud = Tex(new Color(0.05f, 0.05f, 0.09f, 0.72f));
            var bgBtn = Tex(new Color(0.20f, 0.20f, 0.30f, 1f));
            var bgBtnHover = Tex(new Color(0.28f, 0.28f, 0.42f, 1f));
            var bgBtnSel = Tex(new Color(0.42f, 0.33f, 0.62f, 1f));
            var bgRowSel = Tex(new Color(0.30f, 0.27f, 0.20f, 1f));
            var bgRow = Tex(new Color(0.14f, 0.14f, 0.21f, 0.9f));
            var bgRowHover = Tex(new Color(0.22f, 0.22f, 0.33f, 0.95f));

            Window = new GUIStyle { normal = { background = bgDark }, padding = new RectOffset(14, 14, 12, 12), font = Font };
            Panel = new GUIStyle { normal = { background = bgPanel }, padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(4, 4, 4, 4), font = Font };
            Hud = new GUIStyle
            {
                normal = { background = bgHud, textColor = new Color(0.92f, 0.92f, 0.96f) },
                padding = new RectOffset(10, 10, 6, 8),
                font = Font,
                fontSize = 15,
                richText = true,
                wordWrap = true,
            };
            Title = MakeLabel(22, FontStyle.Bold, new Color(1f, 0.86f, 0.55f));
            Header = MakeLabel(17, FontStyle.Bold, new Color(0.85f, 0.8f, 1f));
            Label = MakeLabel(15, FontStyle.Normal, new Color(0.92f, 0.92f, 0.96f));
            Small = MakeLabel(13, FontStyle.Normal, new Color(0.75f, 0.75f, 0.82f));
            Warn = MakeLabel(14, FontStyle.Bold, new Color(1f, 0.55f, 0.45f));
            Toast = MakeLabel(18, FontStyle.Bold, Color.white); // v1.25.2：小さくて読みにくいという声があったので大きく
            Toast.normal.background = bgHud;
            Toast.padding = new RectOffset(10, 10, 5, 5);
            ToastMeasure = new GUIStyle(Toast) { wordWrap = false };

            Button = new GUIStyle
            {
                font = Font,
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                padding = new RectOffset(10, 10, 5, 5),
                margin = new RectOffset(3, 3, 3, 3),
                normal = { background = bgBtn, textColor = Color.white },
                hover = { background = bgBtnHover, textColor = Color.white },
                active = { background = bgBtnSel, textColor = Color.white },
            };
            ButtonSel = new GUIStyle(Button) { normal = { background = bgBtnSel, textColor = Color.white } };
            Tab = new GUIStyle(Button) { fontSize = 16, padding = new RectOffset(16, 16, 7, 7) };
            TabSel = new GUIStyle(Tab) { normal = { background = bgBtnSel, textColor = new Color(1f, 0.92f, 0.7f) } };
            Row = new GUIStyle(Button)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { background = bgRow, textColor = Color.white },
                hover = { background = bgRowHover, textColor = Color.white },
                margin = new RectOffset(2, 2, 1, 1),
            };
            RowWrap = new GUIStyle(Row) { wordWrap = true, fontSize = 13, padding = new RectOffset(10, 10, 4, 4) };
            // 長い文でも右端で切れないよう折り返すボタン（鍛冶の特性ボタンなど）。幅の下限は最長の語で決まる。
            ButtonWrap = new GUIStyle(Button) { wordWrap = true, fontSize = 14 };
            ButtonWrapSel = new GUIStyle(ButtonSel) { wordWrap = true, fontSize = 14 };
            // 選択の星：選択中の効果の枠（緑がかった地に金の文字）、そうでない効果の枠。
            OptionChosen = new GUIStyle(Panel) { normal = { background = Tex(new Color(0.17f, 0.27f, 0.18f, 0.96f)) } };
            OptionIdle = new GUIStyle(Panel);
            // 「選択中」の印と、押せない理由の表示。ボタンではなく枠にして、グレーアウトのボタンと見分ける。
            TagChosen = new GUIStyle(Button)
            {
                wordWrap = true, fontStyle = FontStyle.Bold,
                normal = { background = Tex(new Color(0.22f, 0.45f, 0.27f, 1f)), textColor = new Color(1f, 0.93f, 0.62f) },
                hover = { background = Tex(new Color(0.22f, 0.45f, 0.27f, 1f)), textColor = new Color(1f, 0.93f, 0.62f) },
                active = { background = Tex(new Color(0.22f, 0.45f, 0.27f, 1f)), textColor = new Color(1f, 0.93f, 0.62f) },
            };
            TagBlocked = new GUIStyle(Button)
            {
                wordWrap = true, fontStyle = FontStyle.Italic,
                normal = { background = Tex(new Color(0.09f, 0.09f, 0.12f, 1f)), textColor = new Color(0.78f, 0.62f, 0.62f) },
                hover = { background = Tex(new Color(0.09f, 0.09f, 0.12f, 1f)), textColor = new Color(0.78f, 0.62f, 0.62f) },
                active = { background = Tex(new Color(0.09f, 0.09f, 0.12f, 1f)), textColor = new Color(0.78f, 0.62f, 0.62f) },
            };
            RowSel = new GUIStyle(Row) { normal = { background = bgRowSel, textColor = Color.white }, hover = { background = bgRowSel, textColor = Color.white } };
        }

        private GUIStyle MakeLabel(int size, FontStyle style, Color color)
        {
            return new GUIStyle
            {
                font = Font,
                fontSize = size,
                fontStyle = style,
                richText = true,
                wordWrap = true,
                normal = { textColor = color },
                padding = new RectOffset(2, 2, 2, 2),
            };
        }

        private Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            _textures.Add(t);
            return t;
        }

        public static string RarityHex(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return "#d6d6d6";
                case Rarity.Uncommon: return "#62d962";
                case Rarity.Rare: return "#4fa8ff";
                case Rarity.Epic: return "#c475ff";
                default: return "#ffd24a";
            }
        }

        public static string Colored(string text, string hex) => "<color=" + hex + ">" + text + "</color>";

        /// <summary>遺物の色。セット品は橙で、ほかの伝説（金）と見分ける。</summary>
        public static string RelicHex(Relic r)
        {
            if (r.UniqueId != null && Content.TryGetUnique(r.UniqueId, out var u) && u.SetId != null) return "#ff8a3d";
            return RarityHex(r.Rarity);
        }

        /// <summary>遺物の名前（覚醒済みなら頭に ✦）。</summary>
        public static string RelicTitle(Relic r) => r.Awakened
            ? "<color=#ffe17a>✦" + Content.AwakenNumeral(r.AwakenLevel) + "</color>" + Colored(r.DisplayName, RelicHex(r))
            : Colored(r.DisplayName, RelicHex(r));
    }
}
