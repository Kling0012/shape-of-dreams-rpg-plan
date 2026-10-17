using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private Vector2 _scrollWorkshop;
        private static readonly GUILayoutOption[] WorkshopTextWidth = { GUILayout.Width(560) };
        private static readonly GUILayoutOption[] WorkshopButtonSize = { GUILayout.Width(300), GUILayout.Height(40) };
        private static readonly GUILayoutOption[] WorkshopMaxWidth = { GUILayout.Width(300) };
        private sealed class WorkshopRowText
        {
            internal int Level = -1, Grudge, Cores;
            internal bool Japanese;
            internal string Text, Button;
        }
        private readonly WorkshopRowText[] _workshopRows = new WorkshopRowText[Workshop.All.Count];

        private WorkshopRowText WorkshopText(Profile p, UpgradeDef def, int index, int level)
        {
            var row = _workshopRows[index] ?? (_workshopRows[index] = new WorkshopRowText());
            int cores = p.Material(Materials.NemesisCore);
            if (row.Text != null && row.Level == level && row.Japanese == Loc.Japanese
                && row.Grudge == p.NemesisGrudge && row.Cores == cores) return row;
            row.Level = level; row.Japanese = Loc.Japanese; row.Grudge = p.NemesisGrudge; row.Cores = cores;
            string pips = new string('●', level) + new string('○', def.MaxLevel - level);
            row.Text = $"<b>{def.Name}</b>  <color=#ffd36e>{pips}</color>  {WorkshopValue(p, def.Id, level < def.MaxLevel)}\n<color=#ccd>{def.Description}</color>";
            if (level < def.MaxLevel)
            {
                var cost = def.Costs[level];
                row.Button = Loc.T($"解放（欠片{cost.Shards}" + (cost.Tuning > 0 ? $"・調律石{cost.Tuning}" : "") + "）",
                    $"Unlock ({cost.Shards} shards" + (cost.Tuning > 0 ? $", {cost.Tuning} tuning" : "") + ")");
            }
            return row;
        }

        private void DrawWorkshopTab()
        {
            var p = _s.Profile;
            GUILayout.BeginVertical(_st.Panel);
            // 強化の行はスクロールに入れる（#147）：スクロールがないと、強化の追加や説明の長文化で
            // 一番下の行の「解放」ボタンから順に枠の外へ出る。遠征中の警告はスクロールの外に残す。
            _scrollWorkshop = GUILayout.BeginScrollView(_scrollWorkshop);
            for (int i = 0; i < Workshop.All.Count; i++)
            {
                var def = Workshop.All[i];
                int lv = Workshop.Level(p, def.Id);
                var row = WorkshopText(p, def, i, lv);
                GUILayout.BeginHorizontal();
                GUILayout.Label(row.Text, _st.Small, WorkshopTextWidth);
                if (lv < def.MaxLevel)
                {
                    var cost = def.Costs[lv];
                    bool afford = p.Material(Materials.Shard) >= cost.Shards && p.Material(Materials.Tuning) >= cost.Tuning;
                    GUI.enabled = afford && (p.Run == null || !_s.InGame);
                    if (GUILayout.Button(row.Button, _st.Button, WorkshopButtonSize)) Act(() => Rules.BuyUpgrade(p, def.Id, _s.InGame && p.Run != null), false);
                    GUI.enabled = true;
                }
                else GUILayout.Label(Loc.T("すべて解放済み", "Maxed"), _st.Header, WorkshopMaxWidth);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            if (p.Run != null && _s.InGame) GUILayout.Label(Loc.T("遠征中は工房を使えません。遠征から戻ってから利用してください。", "The workshop is closed during expeditions."), _st.Warn);
        }

        /// <summary>工房の強化の「いまの値 → 解放後の値」。</summary>
        private static string WorkshopValue(Profile p, Upgrade u, bool canRaise)
        {
            int now, next;
            string ja, en;
            switch (u)
            {
                case Upgrade.BigSatchel: now = Workshop.SatchelCapacity(p); next = Workshop.SatchelCapacityAt(Workshop.Level(p, u) + 1); ja = "鞄の上限"; en = "Satchel"; break;
                case Upgrade.WideStash: now = Workshop.StashCapacity(p); next = Content.StashCapacity + Workshop.StashBonus(Workshop.Level(p, u) + 1); ja = "保管庫の上限"; en = "Stash"; break;
                case Upgrade.BountyReroll: now = Workshop.RerollsPerRun(p); next = Workshop.RerollsAt(Workshop.Level(p, u) + 1); ja = "引き直し"; en = "Rerolls"; break;
                // 禁忌の職人（#415）：解放後は、宿敵との因縁と手持ちの禁忌の核を出す。
                case Upgrade.ForbiddenSmith:
                    return canRaise ? "" : $"<color=#ff9a9a>{Forbidden.GrudgeLabel(p.NemesisGrudge)} · {Loc.T("禁忌の核", "Forbidden cores")} {p.Material(Materials.NemesisCore)}</color>";
                default: return "";
            }
            string arrow = canRaise ? $" → <color=#7cf07c>{next}</color>" : "";
            return $"<color=#cfd3ee>{Loc.T(ja, en)} {now}{arrow}</color>";
        }
    }
}
