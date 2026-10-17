using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SodRpg.Mod
{
    // Bind native boss bar lifetimes at Setup; no scene search or per-frame component discovery.
    [HarmonyPatch(typeof(UI_InGame_BossBars_Item), nameof(UI_InGame_BossBars_Item.Setup))]
    internal static class BossShareBarBinding
    {
        internal sealed class Binding
        {
            internal UI_InGame_BossBars_Item Item;
            internal Entity Enemy;
            internal RectTransform Rect;
            internal Canvas Canvas;
            internal CanvasGroup Group;
        }

        internal static readonly List<Binding> Bars = new List<Binding>();

        private static void Postfix(UI_InGame_BossBars_Item __instance, Entity e)
        {
            try
            {
                for (int i = Bars.Count - 1; i >= 0; i--)
                    if (Bars[i].Item == null || Bars[i].Item == __instance) Bars.RemoveAt(i);
                var bar = __instance.GetComponentInChildren<UI_EntityHealthBar>();
                var rect = bar != null ? bar.transform as RectTransform : __instance.transform as RectTransform;
                if (rect == null) return;
                Bars.Add(new Binding { Item = __instance, Enemy = e, Rect = rect,
                    Canvas = rect.GetComponentInParent<Canvas>(), Group = __instance.GetComponent<CanvasGroup>() });
            }
            catch (Exception ex) { Log.Warn("Boss share bar binding ignored; native bar retained: " + ex.Message); }
        }
    }

    internal sealed partial class DreamforgeUi
    {
        private readonly Vector3[] _bossShareCorners = new Vector3[4];
        private static readonly Color[] BossShareColors = {
            new Color32(0x56, 0xB4, 0xE9, 255), new Color32(0xE6, 0x9F, 0, 255),
            new Color32(0, 0x9E, 0x73, 255), new Color32(0xCC, 0x79, 0xA7, 255) };
        private static readonly string[] BossShareLabels = MakeBossShareLabels();
        private readonly Dictionary<uint, Label3D> _miniBossShares = new Dictionary<uint, Label3D>();
        private readonly Dictionary<uint, int> _miniBossShareValues = new Dictionary<uint, int>();
        private bool _bossShareDrawingFailed;

        private static string[] MakeBossShareLabels()
        {
            var result = new string[8 * 102];
            for (int slot = 0; slot < 4; slot++)
                for (int own = 0; own < 2; own++)
                    for (int percent = 0; percent <= 101; percent++)
                        result[(slot * 2 + own) * 102 + percent] = (own == 1 ? "◆" : "◇") + (slot + 1) + " "
                            + (percent == 101 ? "<1" : percent.ToString()) + "%";
            return result;
        }

        private static int BossSharePercent(BossShareDisplay share) =>
            share.remaining > 0 && share.remaining < 10 ? 101 : Mathf.Clamp((share.remaining + 5) / 10, 0, 100);

        private static string BossShareText(BossShareDisplay share, bool own) =>
            BossShareLabels[((share.playerSlot - 1) * 2 + (own ? 1 : 0)) * 102 + BossSharePercent(share)];

        private static void ShareFill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawBossShareBars(float scale)
        {
            if (_bossShareDrawingFailed || !_cfg().showEnemyOwnershipMarks) return;
            try
            {
                var bars = BossShareBarBinding.Bars;
                for (int i = bars.Count - 1; i >= 0; i--)
                {
                    var binding = bars[i];
                    if (binding.Item == null || binding.Enemy == null) { bars.RemoveAt(i); continue; }
                    if (!binding.Item.isActiveAndEnabled || !binding.Enemy.isActive || !binding.Enemy.isAlive
                        || binding.Enemy.Status.isHealthHidden || binding.Enemy.Visual.isSpawning
                        || binding.Group != null && binding.Group.alpha <= 0
                        || !_s.EnemyOwners.TryGetValue(binding.Enemy.netId, out var ownership)
                        || ownership.BossShares == null) continue;
                    binding.Rect.GetWorldCorners(_bossShareCorners);
                    var camera = binding.Canvas != null && binding.Canvas.renderMode != RenderMode.ScreenSpaceOverlay
                        ? binding.Canvas.worldCamera : null;
                    var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _bossShareCorners[0]);
                    var topRight = RectTransformUtility.WorldToScreenPoint(camera, _bossShareCorners[2]);
                    float left = bottomLeft.x / scale, width = (topRight.x - bottomLeft.x) / scale;
                    float y = (Screen.height - bottomLeft.y) / scale + 6;
                    var shares = ownership.BossShares;
                    if (width < shares.Length * 76 || y + 25 > Screen.height / scale) continue;
                    float cell = (width - (shares.Length - 1) * 6) / shares.Length;
                    for (int j = 0; j < shares.Length; j++)
                    {
                        var share = shares[j];
                        bool own = DewPlayer.local != null && share.participantId == DewPlayer.local.guid;
                        float x = left + j * (cell + 6);
                        var track = new Rect(x, y + 17, cell, own ? 7 : 5);
                        ShareFill(new Rect(x - 2, y - 1, cell + 4, 27), new Color(0.035f, 0.04f, 0.06f, 0.94f));
                        if (own) ShareFill(new Rect(track.x - 1, track.y - 1, track.width + 2, track.height + 2), Color.white);
                        ShareFill(track, new Color(0.13f, 0.14f, 0.17f, 1));
                        ShareFill(new Rect(track.x, track.y, track.width * share.remaining / 1000f, track.height), BossShareColors[share.playerSlot - 1]);
                        GUI.Label(new Rect(x, y, cell, 18), BossShareText(share, own), _st.Small);
                    }
                }
            }
            catch (Exception ex)
            {
                _bossShareDrawingFailed = true;
                Log.Warn("Boss share display disabled; native HP bar retained: " + ex.Message);
            }
        }

        private Label3D MiniBossShareMark(uint netId)
        {
            if (_bossShareDrawingFailed || !_cfg().showEnemyOwnershipMarks || DewPlayer.local == null
                || !_s.EnemyOwners.TryGetValue(netId, out var ownership) || ownership.BossShares == null) return null;
            foreach (var share in ownership.BossShares)
            {
                if (share.participantId != DewPlayer.local.guid) continue;
                int percent = BossSharePercent(share);
                int key = share.playerSlot * 102 + percent;
                if (_miniBossShareValues.TryGetValue(netId, out int previous) && previous == key
                    && _miniBossShares.TryGetValue(netId, out var cached)) return cached;
                string text = UiStyles.Colored("◆" + share.playerSlot, OwnershipColors[share.playerSlot - 1])
                    + " " + (percent == 101 ? "<1" : percent.ToString()) + "%";
                var content = new GUIContent(text);
                var label = new Label3D { Text = text, Content = content, Size = _st.ToastMeasure.CalcSize(content) };
                _miniBossShareValues[netId] = key;
                return _miniBossShares[netId] = label;
            }
            return null;
        }
    }
}
