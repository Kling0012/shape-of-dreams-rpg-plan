using System;
using System.Collections.Generic;
using System.IO;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 遺物のアイコン（MODフォルダの icons/&lt;土台id&gt;.png）。必要になったときに一度だけ読み込み、使い回す。
    /// 見つからない・読めない土台は null を覚えておき、二度と探さない（毎フレームのファイルアクセスを避ける）。
    /// </summary>
    internal static class RelicIcons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static string _dir;
        private static Texture2D _white;

        public static void Init(string modPath)
        {
            _dir = string.IsNullOrEmpty(modPath) ? null : Path.Combine(modPath, "icons");
        }

        public static Texture2D For(Relic r) => r == null ? null : For(r.BaseId);

        public static Texture2D For(string baseId)
        {
            if (string.IsNullOrEmpty(baseId) || _dir == null) return null;
            if (Cache.TryGetValue(baseId, out var tex)) return tex;
            tex = null;
            try
            {
                string file = Path.Combine(_dir, baseId + ".png");
                if (File.Exists(file))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    if (!tex.LoadImage(File.ReadAllBytes(file))) tex = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Icon " + baseId + ": " + ex.Message);
                tex = null;
            }
            Cache[baseId] = tex;
            return tex;
        }

        /// <summary>挿絵（例：events/Merchant）を枠なしで描く。無ければ何も描かない。</summary>
        public static bool DrawArt(Rect rect, string key)
        {
            var tex = For(key);
            if (tex == null) return false;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
            return true;
        }

        /// <summary>レア度の色の枠つきでアイコンを描く。アイコンが無ければ何も描かない（呼び出し側は文字だけで表示する）。</summary>
        public static bool Draw(Rect rect, Relic r)
        {
            var tex = For(r);
            if (tex == null) return false;
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var old = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            GUI.DrawTexture(rect, _white);
            ColorUtility.TryParseHtmlString(UiStyles.RelicHex(r), out var frame);
            GUI.color = frame;
            float b = rect.width >= 48 ? 2f : 1f;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, b), _white);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - b, rect.width, b), _white);
            GUI.DrawTexture(new Rect(rect.x, rect.y, b, rect.height), _white);
            GUI.DrawTexture(new Rect(rect.xMax - b, rect.y, b, rect.height), _white);
            GUI.color = old;
            GUI.DrawTexture(new Rect(rect.x + b, rect.y + b, rect.width - 2 * b, rect.height - 2 * b), tex, ScaleMode.ScaleToFit, true);
            return true;
        }
    }
}
