using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class DreamforgeUi
    {
        private static readonly GUILayoutOption[] DirectStashButtonSize = { GUILayout.ExpandWidth(true) };
        /// <summary>確認表示へ切り替えてから自動で戻すまでの秒数。</summary>
        private const float DirectStashConfirmSeconds = 5f;

        private enum DirectStashLabel { Ready, Confirm, Used }

        private DirectStashLabel _directStashLabelKind = DirectStashLabel.Ready;
        private bool _directStashLabelJapanese;
        private string _directStashLabel;
        private float _directStashArmedAt = -1f;

        private void DrawDirectStash()
        {
            if (!_s.HasDirectStash)
            {
                _directStashArmedAt = -1f;
                return;
            }
            // 5秒以内の2回押しで実行。期限が切れたら通常の表示へ戻す。
            if (_directStashArmedAt >= 0f && Time.unscaledTime - _directStashArmedAt > DirectStashConfirmSeconds)
                _directStashArmedAt = -1f;
            bool can = _s.CanStashSatchelNow;
            var kind = _s.DirectStashUsedThisRun ? DirectStashLabel.Used
                : can && _directStashArmedAt >= 0f ? DirectStashLabel.Confirm
                : DirectStashLabel.Ready;
            bool japanese = Loc.Japanese;
            if (_directStashLabel == null || _directStashLabelKind != kind || _directStashLabelJapanese != japanese)
            {
                _directStashLabelKind = kind;
                _directStashLabelJapanese = japanese;
                _directStashLabel = kind == DirectStashLabel.Confirm ? Loc.T(
                    "もう一度押すと保管庫へ送ります", "Press again to send to the stash")
                    : kind == DirectStashLabel.Used ? Loc.T("この遠征では使用済み", "Used this expedition")
                    : Loc.T("鞄の遺物を保管庫へ送る（この遠征で1回）", "Send satchel relics to the stash (once per expedition)");
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && can;
            try
            {
                // One flexible row before tab contents: both languages fit the default window.
                if (GUILayout.Button(_directStashLabel, _st.Button, DirectStashButtonSize))
                {
                    if (_directStashArmedAt < 0f) _directStashArmedAt = Time.unscaledTime;
                    else
                    {
                        _directStashArmedAt = -1f;
                        string error = _s.StashSatchelNow();
                        if (error != null) SetStatus(error);
                    }
                }
            }
            finally { GUI.enabled = enabled; }
        }
    }
}
