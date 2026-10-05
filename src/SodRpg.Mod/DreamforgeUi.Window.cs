using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private void DrawWindow(float w, float h, DreamforgeConfig cfg)
        {
            // The star map needs room: it uses most of the screen and hides the expedition-only rows.
            bool starTab = _tab == 2;
            bool codexTab = _tab == 4 && _codexOpen; // 図鑑は一覧が見やすいよう、少し大きく開く
            // 星図は画面いっぱいに使う（余白は左右上下8だけ）。鍛冶と装備は、左の一覧と右の操作欄が収まる高さまで広げる
            // （装備の見出しは遠征の外で行が増え、固定の720では下端の操作・鍵のボタンが枠の外へ出た #128）。
            bool forgeTab = _tab == 1;
            bool gearTab = _tab == 0;
            float ww = starTab ? w - 16 : Mathf.Min(codexTab ? 1180 : 1060, w - 20);
            float wh = starTab ? h - 16 : Mathf.Min(codexTab ? 900 : forgeTab || gearTab ? 900 : 720, h - 20);
            _windowHeight = wh;
            _windowWidth = ww;
            var rect = new Rect((w - ww) / 2, (h - wh) / 2, ww, wh);
            GUILayout.BeginArea(rect, _st.Window);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Dreamforge ─ 夢の遺物", "Dreamforge ─ Relics of the Dream"), _st.Title, GUILayout.Width(330));
            string[] tabs = { Loc.T("装備", "Gear"), Loc.T("鍛冶", "Forge"), Loc.T("星図", "Star Map"), Loc.T("工房", "Workshop"), Loc.T("記録", "Records") };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], i == _tab ? _st.TabSel : _st.Tab)) { CancelStarDrag(); _tab = i; _confirmSalvage = null; _confirmEnhance = null; _confirmBulk = false; _retuneIndex = -1; _confirmAffixReroll = null; }
            if (GUILayout.Button(Loc.T($"閉じる [{cfg.menuKey}]", $"Close [{cfg.menuKey}]"), _st.Button)) Close();
            GUILayout.EndHorizontal();
            DrawProfileSlots();

            var p = _s.Profile;
            bool compact = forgeTab && wh < 640f;
            if (!starTab && !compact && !forgeTab) DrawDreamDepthChoice(); // 鍛冶では一覧の高さを優先する
            if (!starTab && !compact) GUILayout.Label(Loc.T(
                $"欠片 {p.Material(Materials.Shard)}　調律石 {p.Material(Materials.Tuning)}　保管庫 {p.Stash.Count}/{Workshop.StashCapacity(p)}　旅人：{HeroName(HeroKey)}",
                $"Shards {p.Material(Materials.Shard)}   Tuning {p.Material(Materials.Tuning)}   Stash {p.Stash.Count}/{Workshop.StashCapacity(p)}   Traveler: {HeroName(HeroKey)}"), _st.Small);
            GUILayout.Label(UiStyles.Colored(TabIntro(_tab), "#c8d0ff"), _st.Small);

            // 変更と払い戻しの承認・取消ボタンとステータス行は、タブの中身より先に描く（#147）。
            // IMGUI は中身がウィンドウより高いとき下端から順に切るので、末尾に置いたパネルのボタンが
            // 最初に消えて押せなくなっていた（装備タブのロビーで約230px、鍛冶でも約100pxはみ出す。#128 と同じ型）。
            DrawAllocationRefund();
            GUILayout.Label(_status != null && Time.unscaledTime < _statusUntil ? _status : " ", _st.Warn);

            switch (_tab)
            {
                case 0: DrawGearTab(); break;
                case 1: DrawForgeTab(); break;
                case 2: DrawTalentTab(); break;
                case 3: DrawWorkshopTab(); break;
                default: DrawRecordsTab(cfg); break;
            }
            GUILayout.EndArea();
        }
    }
}
