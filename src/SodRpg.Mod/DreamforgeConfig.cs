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

        [LabelText("HUD / 左の夢鍛パネル")]
        public HudMode hudMode = HudMode.Full;

        [LabelText("ゲームが裏にあるときのFPS上限（0で無効。20〜60）")]
        [UnityEngine.Range(0, 60)]
        public int backgroundFps = 20;

        [LabelText("軽量化（Off：なし／Light：軽め／Strong：強め／Max：最大）")]
        public LightweightMode lightweight = LightweightMode.Off;
    }

    public enum HudMode
    {
        Full = 0,
        Compact = 1,
        Off = 2,
    }

    public enum LightweightMode
    {
        Off = 0,
        Light = 1,
        Strong = 2,
        Max = 3,
    }
}
