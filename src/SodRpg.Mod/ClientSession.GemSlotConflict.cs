using System;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private Action<DreamforgeGemSlotConflictMsg> _onGemSlotConflict;

        private void RegisterGemSlotConflict(Actor actor)
        {
            if (_onGemSlotConflict == null) _onGemSlotConflict = OnGemSlotConflict;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeGemSlotConflictMsg>(_onGemSlotConflict);
        }

        private void UnregisterGemSlotConflict(Actor actor)
        {
            if (_onGemSlotConflict != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeGemSlotConflictMsg>(_onGemSlotConflict); } catch (Exception) { }
        }

        private void OnGemSlotConflict(DreamforgeGemSlotConflictMsg msg)
        {
            if (msg == null || msg.heroNetId == 0) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeGemSlotConflictMsg));
            // Older hosts may still send this optional message. Decode it for wire
            // compatibility, but a cap mismatch no longer disables or hides extra slots.
        }

        // 「星を取ったのに枠が増えない」「装着の画面で回避だけ隠れる」報告の切り分けと保険。
        // HUD の技ボタンが各記憶の枠を何個まで並べられるかを1回だけ Player.log に残す（本体の枠並びは現在値がこの数を超えると
        // 枠を1つも描かない）。あわせて回避（移動）の枠が1つ以上あるのに、その技ボタンの枠並びが非表示のままなら表示へ戻す。
        // 本体UIの内部構造は実機で未確認のため、どの参照も欠けていれば何もせず、例外は握って次の周期へ回す。
        private static bool _gemSlotHudLogged, _movementGroupHealLogged;
        private float _nextGemSlotHudCheck;

        internal void TickGemSlotHudProbe()
        {
            if (LocalHero == null || Time.unscaledTime < _nextGemSlotHudCheck) return;
            _nextGemSlotHudCheck = Time.unscaledTime + 1f;
            try
            {
                var buttons = ManagerBase<UI_InGame_SkillButtons>.softInstance;
                if (buttons == null || buttons.skillButtons == null || buttons.skillButtons.Length == 0) return;
                bool log = !_gemSlotHudLogged;
                _gemSlotHudLogged = true;
                var parts = log ? new System.Text.StringBuilder() : null;
                bool movementButton = false;
                foreach (var button in buttons.skillButtons)
                {
                    if (button == null) { if (log) AppendHudPart(parts, "null"); continue; }
                    var group = button.transform.parent != null
                        ? button.transform.parent.GetComponentInChildren<UI_InGame_SkillButton_GemGroup>(true)
                        : null;
                    if (log)
                        AppendHudPart(parts, button.skillType + ":" + (group != null && group.groups != null ? group.groups.Length.ToString() : "no-group")
                            + (group != null ? (group.gameObject.activeInHierarchy ? "" : "(hidden)") : ""));
                    if (button.skillType != HeroSkillLocation.Movement) continue;
                    movementButton = true;
                    HealMovementGroup(button, group);
                }
                if (log)
                    Log.Info("Client: gem slots HUD: buttons " + buttons.skillButtons.Length + " [" + parts + "]"
                        + (movementButton ? "" : " (no Movement button)"));
            }
            catch (Exception ex)
            {
                if (!_movementGroupHealLogged) { _movementGroupHealLogged = true; Log.Warn("Client: gem slots HUD check failed: " + ex.Message); }
            }
        }

        private static void AppendHudPart(System.Text.StringBuilder parts, string part)
        {
            if (parts.Length > 0) parts.Append(", ");
            parts.Append(part);
        }

        private void HealMovementGroup(Component button, UI_InGame_SkillButton_GemGroup group)
        {
            if (group == null || group.gameObject.activeSelf || !button.gameObject.activeInHierarchy) return;
            var skill = LocalHero.Skill;
            if (skill == null) return;
            int cap = skill.GetMaxGemCount(HeroSkillLocation.Movement);
            if (cap < 1 || cap > EssenceSlots.NativeCapPerLocation) return;
            group.gameObject.SetActive(true);
            if (_movementGroupHealLogged) return;
            _movementGroupHealLogged = true;
            Log.Info("Client: gem slots HUD: Movement slot group was hidden with " + cap + " slot(s); shown again");
        }
    }
}
