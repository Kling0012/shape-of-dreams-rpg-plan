using System;

namespace SodRpg.Core.Game
{
    /// <summary>スクロール位置の計算（マウスホイール用）。UnityのGUIに依らない純粋な計算。</summary>
    public static class ScrollMath
    {
        /// <summary>
        /// ホイールの目盛り <paramref name="wheelDelta"/>（下向きが正）ぶん動かした縦の位置。
        /// 0 から（内容の高さ - 表示の高さ）の範囲に収める。内容が表示に収まるなら 0。内容の高さが不明なら無限大を渡す。
        /// </summary>
        public static float Wheel(float current, float wheelDelta, float step, float contentHeight, float viewHeight)
        {
            if (float.IsNaN(current) || float.IsNaN(wheelDelta) || float.IsNaN(step) || float.IsNaN(contentHeight) || float.IsNaN(viewHeight)) return 0f;
            float max = contentHeight - viewHeight;
            if (!(max > 0f)) return 0f;
            return Math.Max(0f, Math.Min(max, current + wheelDelta * step));
        }
    }
}
