namespace SodRpg.Core.Game
{
    /// <summary>
    /// 料理のエッセンスの食材が、本体の吸い寄せから逃げられたときの保険（ブリンクなどの瞬間移動で置いていかれた場合）。
    /// ホストが毎フレーム、持ち主から離れすぎている食材だけを持ち主の位置へ移す。判定だけを持ち、ゲームの型は使わない。
    /// </summary>
    public static class CulinaryPickupSnap
    {
        /// <summary>これより離れている食材を持ち主の位置へ移す距離（m）。本体の拾得距離より十分に大きい。</summary>
        public const float SnapDistance = 3f;

        /// <summary>持ち主が生きていて、位置が正しく、離れすぎているときだけ移す。</summary>
        public static bool ShouldSnap(bool ownerAlive, float distance) =>
            ownerAlive && !float.IsNaN(distance) && !float.IsInfinity(distance) && distance > SnapDistance;
    }
}
