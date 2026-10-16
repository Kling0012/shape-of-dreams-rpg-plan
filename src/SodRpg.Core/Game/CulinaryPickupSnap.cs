namespace SodRpg.Core.Game
{
    /// <summary>
    /// 料理のエッセンスの食材が、本体の吸い寄せから逃げられたときの保険（ブリンクなどの瞬間移動で置いていかれた場合）。
    /// 本体モーターの吸引範囲を超えた食材だけを移し、その位置を本体のRPCで同期する。判定だけを持ち、ゲームの型は使わない。
    /// </summary>
    public static class CulinaryPickupSnap
    {
        /// <summary>食材モーターへ設定する吸引範囲（m）。この範囲内では通常の同期飛行に任せる。</summary>
        public const float SnapDistance = 500f;

        /// <summary>持ち主が生きていて、位置が正しく、離れすぎているときだけ移す。</summary>
        public static bool ShouldSnap(bool ownerAlive, float distance) =>
            ownerAlive && !float.IsNaN(distance) && !float.IsInfinity(distance) && distance > SnapDistance;
    }
}
