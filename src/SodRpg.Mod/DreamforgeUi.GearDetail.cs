using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        /// <summary>
        /// 装備タブの右の欄。操作ボタン（装着・外す・鍵）を上に、詳細・比較のスクロールを下に置く。
        /// 詳細のスクロールは高さに余りがないとはみ出すので、末尾の柔軟な欄にはみ出しを吸収させる。
        /// ボタンを下端に置くと、見出しの行が増えて画面（ウィンドウ）の高さが足りなくなったとき、
        /// 枠の外へ出て押せなくなった（鍵が見えない、#128）。
        /// </summary>
        internal void DrawGearDetail(Profile p, string hero, Relic sel)
        {
            var cur = Rules.EquippedRelic(p, hero, sel.Slot);
            bool equipped = cur != null && cur.Uid == sel.Uid;
            GUI.enabled = _s.CanEditLoadout && !_s.Trades.IsReserved(sel.Uid);
            GUILayout.BeginHorizontal();
            if (!equipped && GUILayout.Button(Loc.T("装着する", "Equip"), _st.Button, GUILayout.Height(32)))
            {
                try
                {
                    foreach (var e in Rules.Equip(p, hero, sel.Uid, _s.Trades)) _s.Emit(e);
                    _s.MarkDirty(true);
                }
                catch (AllocationValidationException ex) { OfferAllocationRefund(ex, p, hero, true, sel.Uid); }
                catch (InvalidOperationException ex) { SetStatus(ex.Message); }
            }
            if (equipped && GUILayout.Button(Loc.T("外す", "Unequip"), _st.Button, GUILayout.Height(32)))
            {
                try
                {
                    foreach (var e in Rules.Unequip(p, hero, sel.Slot)) _s.Emit(e);
                    _s.MarkDirty(true);
                }
                catch (AllocationValidationException ex) { OfferAllocationRefund(ex, p, hero, true, sel.Uid); }
                catch (InvalidOperationException ex) { SetStatus(ex.Message); }
            }
            GUI.enabled = !_s.Trades.IsReserved(sel.Uid);
            if (GUILayout.Button(sel.Locked ? Loc.T("鍵を外す", "Unlock") : Loc.T("鍵をかける", "Lock"), _st.Button, GUILayout.Height(32)))
            {
                Rules.ToggleLock(p, sel.Uid, _s.Trades);
                _s.MarkDirty(false);
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            _scrollRight = GUILayout.BeginScrollView(_scrollRight);
            RelicDetail(sel);
            if (cur != null && cur.Uid != sel.Uid) Comparison(sel, cur);
            GUILayout.EndScrollView();
        }
    }
}
