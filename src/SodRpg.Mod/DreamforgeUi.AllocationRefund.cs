using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private EffectiveAllocationPlan _allocationRefund;
        private Profile _allocationRefundProfile;
        private string _allocationRefundHero;
        private Vector2 _allocationRefundScroll;
        private bool _allocationRefundEquipment;
        private string _allocationRefundRelic;

        private void OfferAllocationRefund(AllocationValidationException error, Profile profile, string hero,
            bool equipment = false, string relicUid = null)
        {
            SetStatus(error.Message);
            if (!error.Plan.CanApply || error.Plan.AffectedRefundIds.Count == 0) return;
            _allocationRefund = error.Plan;
            _allocationRefundProfile = profile;
            _allocationRefundHero = hero;
            _allocationRefundScroll = Vector2.zero;
            _allocationRefundEquipment = equipment;
            _allocationRefundRelic = relicUid;
        }

        private void DrawAllocationRefund()
        {
            var plan = _allocationRefund;
            if (plan == null) return;
            if (!ReferenceEquals(_s.Profile, _allocationRefundProfile) || HeroKey != _allocationRefundHero)
            {
                _allocationRefund = null;
                return;
            }
            GUILayout.BeginVertical(_st.Panel);
            GUILayout.Label(Loc.T("変更と払い戻しを一括承認", "Approve configuration change and atomic refund"), _st.Label);
            GUILayout.Label(Loc.T($"払い戻し：{plan.RefundCost} ポイント。承認するまで変更されません。",
                $"Refund: {plan.RefundCost} points. Nothing changes until approved."), _st.Warn);
            _allocationRefundScroll = GUILayout.BeginScrollView(_allocationRefundScroll, GUILayout.MaxHeight(120));
            foreach (var refund in plan.Refunds)
            {
                var talent = Rules.AllocationValidationForHero(_allocationRefundHero).Talent(refund.StarId);
                GUILayout.Label((talent?.Name.ToString() ?? refund.StarId) + $" — {refund.Ranks} × {refund.Cost / refund.Ranks}", _st.Small);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && (_allocationRefundEquipment
                ? _s.CanEditLoadout && (_allocationRefundRelic == null
                    || !_s.Trades.IsReserved(_allocationRefundRelic) && _s.Profile.FindStash(_allocationRefundRelic) != null)
                : _s.CanEditTalents && _s.Profile.Run == null);
            if (GUILayout.Button(Loc.T("変更と全払い戻しを承認", "Approve change and all refunds"), _st.Button))
            {
                try
                {
                    Rules.AllocationValidationForHero(_allocationRefundHero).Commit(_s.Profile, plan, plan.AffectedRefundIds);
                    foreach (var notice in Feats.Check(_s.Profile)) _s.Emit(notice);
                    _starDirty = true;
                    _starChoiceId = null;
                    _s.MarkDirty(true);
                    _allocationRefund = null;
                }
                catch (InvalidOperationException error)
                {
                    _allocationRefund = null;
                    SetStatus(error.Message);
                }
            }
            GUI.enabled = enabled;
            if (GUILayout.Button(Loc.T("取り消す", "Cancel"), _st.Button)) _allocationRefund = null;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
