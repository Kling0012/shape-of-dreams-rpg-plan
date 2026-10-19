using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private static readonly GUILayoutOption[] InterruptedRelicButtonSize = { GUILayout.ExpandWidth(true) };

        private int _interruptedRelicLabelCount = -1;
        private LocLanguage _interruptedRelicLabelLanguage;
        private string _interruptedRelicLabel;

        private void DrawInterruptedRelics()
        {
            if (!_s.HasInterruptedRelics) return;
            int count = _s.Profile.InterruptedRelics.Count;
            LocLanguage language = Loc.Language; // 3値（#877）。真偽値だと英語⇔中国語で古い表示が残る。
            if (_interruptedRelicLabel == null || _interruptedRelicLabelCount != count
                || _interruptedRelicLabelLanguage != language)
            {
                _interruptedRelicLabelCount = count;
                _interruptedRelicLabelLanguage = language;
                _interruptedRelicLabel = Loc.T($"中断した遠征の遺物を受け取る（{count}個）",
                    $"Claim interrupted expedition relics ({count})",
                    $"领取中断远征的遗物（{count}件）");
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
