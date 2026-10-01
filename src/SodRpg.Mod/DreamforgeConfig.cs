using UnityEngine.InputSystem;

namespace SodRpg.Mod
{
    /// <summary>ゲーム内のMOD設定画面に出る設定。保存はゲーム側（QuickSave/Mods）が行う。</summary>
    public class DreamforgeConfig : ModConfig
    {
        [LabelText("Menu key / 夢鍛メニュー")]
        public Key menuKey = Key.F6;

        [LabelText("Secure key / 確保する")]
        public Key secureKey = Key.F7;

        [LabelText("Delve key / 深く潜る")]
        public Key delveKey = Key.F8;

        [LabelText("Japanese UI / 日本語表示")]
        public bool japanese = true;

        [LabelText("UI scale / 表示倍率")]
        public float uiScale = 1f;

        [LabelText("Show drop toasts / 拾得通知")]
        public bool showToasts = true;
    }
}
