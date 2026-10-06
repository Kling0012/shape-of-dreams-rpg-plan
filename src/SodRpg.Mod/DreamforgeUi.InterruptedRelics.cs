using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private static readonly GUILayoutOption[] InterruptedRelicButtonSize = { GUILayout.ExpandWidth(true) };

        private int _interruptedRelicLabelCount = -1;
        private bool _interruptedRelicLabelJapanese;
        private string _interruptedRelicLabel;

        private void DrawInterruptedRelics()
        {
            if (!_s.HasInterruptedRelics) return;
            int count = _s.Profile.InterruptedRelics.Count;
            bool japanese = Loc.Japanese;
            if (_interruptedRelicLabel == null || _interruptedRelicLabelCount != count
                || _interruptedRelicLabelJapanese != japanese)
            {
                _interruptedRelicLabelCount = count;
                _interruptedRelicLabelJapanese = japanese;
                _interruptedRelicLabel = Loc.T($"中断した遠征の遺物を受け取る（{count}個）",
                    $"Claim interrupted expedition relics ({count})");
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _s.CanClaimInterruptedRelics;
            try
            {
                // One flexible row before tab contents: both languages fit the default window.
                if (GUILayout.Button(_interruptedRelicLabel, _st.Button, InterruptedRelicButtonSize))
                {
                    string error = _s.ClaimInterruptedRelics();
                    if (error != null) SetStatus(error);
                }
            }
            finally { GUI.enabled = enabled; }
        }
    }
}
