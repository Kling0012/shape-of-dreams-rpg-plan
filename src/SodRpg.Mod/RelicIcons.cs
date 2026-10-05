using System;
using System.Collections.Generic;
using System.IO;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 遺物のアイコン（MODフォルダの icons/&lt;土台id&gt;.png、固有品は icons/uniques/&lt;固有品id&gt;.png を優先）。
    /// 必要になったときに一度だけ読み込み、使い回す。見つからない・読めないキーは null を覚えておき、
    /// 二度と探さない（毎フレームのファイルアクセスを避ける）。
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

        /// <summary>同梱の画像をすべて先に読み込む（メニューを初めて開いたときに一度に読み込んで止まらないように）。</summary>
        public static void Preload()
        {
            if (_dir == null || !Directory.Exists(_dir)) return;
            try
            {
                foreach (var f in Directory.GetFiles(_dir, "*.png")) For(Path.GetFileNameWithoutExtension(f));
                foreach (string sub in new[] { "events", "stars", "uniques" })
                {
                    string dir = Path.Combine(_dir, sub);
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, "*.png")) For(sub + "/" + Path.GetFileNameWithoutExtension(f));
                }
            }
            catch (Exception ex) { Log.Warn("Icon preload: " + ex.Message); }
        }

        /// <summary>読み込んだ画像を捨てる（MODの読み直し・終了時）。</summary>
        public static void Dispose()
        {
            foreach (var t in Cache.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Cache.Clear();
            if (_white != null) UnityEngine.Object.Destroy(_white);
            _white = null;
        }

        public static Texture2D For(Relic r) => r == null ? null : For(r.UniqueId, r.BaseId);

        /// <summary>固有品専用のアイコン（uniques/&lt;固有品id&gt;）があればそれを、無ければ土台のアイコンを返す。</summary>
        public static Texture2D For(string uniqueId, string baseId)
        {
            foreach (string key in RelicIconKeys.Preference(uniqueId, baseId))
            {
                var tex = For(key);
                if (tex != null) return tex;
            }
            return null;
        }

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
                    if (!tex.LoadImage(File.ReadAllBytes(file)))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Icon " + baseId + ": " + ex.Message);
                if (tex != null) UnityEngine.Object.Destroy(tex);
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

        /// <summary>
        /// レア度の色の枠つきでアイコンを描く。遺物が無い（空の枠）ときは灰色の枠だけ、
        /// アイコン画像が無いときは枠だけを描く（場所が空いて見えないように）。
        /// </summary>
        public static bool Draw(Rect rect, Relic r)
        {
            var tex = r != null ? For(r) : null;
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var old = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            GUI.DrawTexture(rect, _white);
            Color frame = new Color(0.32f, 0.32f, 0.42f, 1f);
            if (r != null) ColorUtility.TryParseHtmlString(UiStyles.RelicHex(r), out frame);
            GUI.color = frame;
            float b = rect.width >= 48 ? 2f : 1f;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, b), _white);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - b, rect.width, b), _white);
            GUI.DrawTexture(new Rect(rect.x, rect.y, b, rect.height), _white);
            GUI.DrawTexture(new Rect(rect.xMax - b, rect.y, b, rect.height), _white);
            GUI.color = old;
            if (tex == null) return false;
            GUI.DrawTexture(new Rect(rect.x + b, rect.y + b, rect.width - 2 * b, rect.height - 2 * b), tex, ScaleMode.ScaleToFit, true);
            return true;
        }
    }
}
