using System;

// #1050: 記録タブの自動分解設定（DreamforgeUi.cs から抽出した AutoSalvageChoices /
// DrawAutoSalvageSettings）を動かすための境界。設定は NativeEmbracePositionBoundary.cs の
// DreamforgeConfig 模倣（ここに autoSalvageRarity / toastMaxLines を足した）、描いた文字列は
// ProfileSlotUiBoundary.cs の GUILayout 代役が記録する。
namespace SodRpg.Mod
{
    // DreamforgeConfig.cs と同じ値（Rarity の番号に合わせ、「そのレア度未満」で分解する）。
    public enum AutoSalvageRarity
    {
        None = 0,
        BelowUncommon = 1,
        BelowRare = 2,
        BelowEpic = 3,
        BelowLegendary = 4,
    }

    internal sealed partial class DreamforgeUi
    {
        // 本体は ctor の引数で受け取る保存コールバック（DreamforgeUi.cs の _saveLevelEssenceConfig）。
        // この境界では未設定のままにする（描画に影響しない）。
        private readonly Action _saveLevelEssenceConfig;
        internal void Issue1050DrawAutoSalvageSettings(DreamforgeConfig cfg) => DrawAutoSalvageSettings(cfg);
        internal string[] Issue1050AutoSalvageChoices() => AutoSalvageChoices();
    }
}
