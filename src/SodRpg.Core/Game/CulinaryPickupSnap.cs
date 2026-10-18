namespace SodRpg.Core.Game
{
    /// <summary>
    /// 料理のエッセンスの食材を持ち主へ吸い寄せる主な経路の距離判定。
    /// ホストが毎ティック、生きている持ち主から3mを超えて離れた食材を持ち主の位置へ移す。判定だけを持ち、ゲームの型は使わない。
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
