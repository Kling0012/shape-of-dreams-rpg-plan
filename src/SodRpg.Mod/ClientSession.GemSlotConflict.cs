using System;

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

        // 「星を取ったのに枠が増えない」報告の切り分け用。HUD の技ボタンが各記憶の枠を何個まで並べられるかを
        // 1回だけ Player.log に残す（本体の枠並びは現在値がこの数を超えると枠を1つも描かない）。
        private static bool _gemSlotHudLogged;

        internal void TickGemSlotHudProbe()
        {
            if (_gemSlotHudLogged || LocalHero == null) return;
            var buttons = ManagerBase<UI_InGame_SkillButtons>.softInstance;
            if (buttons == null || buttons.skillButtons == null || buttons.skillButtons.Length == 0) return;
            _gemSlotHudLogged = true;
            var parts = new System.Text.StringBuilder();
            foreach (var button in buttons.skillButtons)
            {
                if (parts.Length > 0) parts.Append(", ");
                if (button == null) { parts.Append("null"); continue; }
                var group = button.transform.parent != null
                    ? button.transform.parent.GetComponentInChildren<UI_InGame_SkillButton_GemGroup>()
                    : null;
                parts.Append(button.skillType).Append(':')
                    .Append(group != null && group.groups != null ? group.groups.Length.ToString() : "no-group");
            }
            Log.Info("Client: gem slots HUD: buttons " + buttons.skillButtons.Length + " [" + parts + "]");
        }
    }
}
