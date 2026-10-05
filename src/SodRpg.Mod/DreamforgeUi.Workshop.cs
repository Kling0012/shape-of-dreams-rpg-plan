using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private Vector2 _scrollWorkshop;

        private void DrawWorkshopTab()
        {
            var p = _s.Profile;
            GUILayout.BeginVertical(_st.Panel);
            // 強化の行はスクロールに入れる（#147）：スクロールがないと、強化の追加や説明の長文化で
            // 一番下の行の「解放」ボタンから順に枠の外へ出る。遠征中の警告はスクロールの外に残す。
            _scrollWorkshop = GUILayout.BeginScrollView(_scrollWorkshop);
            foreach (var def in Workshop.All)
            {
                int lv = Workshop.Level(p, def.Id);
                GUILayout.BeginHorizontal();
                string pips = new string('●', lv) + new string('○', def.MaxLevel - lv);
                GUILayout.Label($"<b>{def.Name}</b>  <color=#ffd36e>{pips}</color>  {WorkshopValue(p, def.Id, lv < def.MaxLevel)}\n<color=#ccd>{def.Description}</color>", _st.Small, GUILayout.Width(560));
                if (lv < def.MaxLevel)
                {
                    var cost = def.Costs[lv];
                    bool afford = p.Material(Materials.Shard) >= cost.Shards && p.Material(Materials.Tuning) >= cost.Tuning;
                    GUI.enabled = afford && (p.Run == null || !_s.InGame);
                    string label = Loc.T($"解放（欠片{cost.Shards}" + (cost.Tuning > 0 ? $"・調律石{cost.Tuning}" : "") + "）",
                        $"Unlock ({cost.Shards} shards" + (cost.Tuning > 0 ? $", {cost.Tuning} tuning" : "") + ")");
                    if (GUILayout.Button(label, _st.Button, GUILayout.Width(300), GUILayout.Height(40))) Act(() => Rules.BuyUpgrade(p, def.Id, _s.InGame && p.Run != null), false);
                    GUI.enabled = true;
                }
                else GUILayout.Label(Loc.T("すべて解放済み", "Maxed"), _st.Header, GUILayout.Width(300));
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
                case Upgrade.BigSatchel: now = Workshop.SatchelCapacity(p); next = now + 5; ja = "鞄の上限"; en = "Satchel"; break;
                case Upgrade.WideStash: now = Workshop.StashCapacity(p); next = now + 20; ja = "保管庫の上限"; en = "Stash"; break;
                case Upgrade.BountyReroll: now = Workshop.RerollsPerRun(p); next = now + 1; ja = "引き直し"; en = "Rerolls"; break;
                default: return "";
            }
            string arrow = canRaise ? $" → <color=#7cf07c>{next}</color>" : "";
            return $"<color=#cfd3ee>{Loc.T(ja, en)} {now}{arrow}</color>";
        }
    }
}
