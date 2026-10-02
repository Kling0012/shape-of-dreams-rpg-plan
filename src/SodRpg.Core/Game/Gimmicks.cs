namespace SodRpg.Core.Game
{
    /// <summary>記憶の仕掛けのきっかけ（v1.28）。仕掛けは、その星のルートの記憶に反応する。</summary>
    public enum GimmickTrigger
    {
        None = 0,
        /// <summary>その記憶を使ったとき。</summary>
        OnUse = 1,
        /// <summary>その記憶のダメージが敵に当たったとき。</summary>
        OnHit = 2,
        /// <summary>その記憶のダメージで敵を倒したとき。</summary>
        OnKill = 3,
        /// <summary>その記憶のダメージが会心したとき。</summary>
        OnCrit = 4,
    }

    /// <summary>記憶の仕掛けで起きること（v1.28）。値の意味は docs/specs/v1.28-memory-gimmicks.md。</summary>
    public enum GimmickEffect
    {
        None = 0,
        /// <summary>属性を付ける。Arg：0 火・1 冷気・2 光・3 闇。Value：100 ごとに1つ、残りは%の確率でもう1つ。</summary>
        Element = 1,
        /// <summary>周り4mに、攻撃力か魔力の高い方の Value% の追加ダメージ。</summary>
        Burst = 2,
        /// <summary>自分に最大HPの Value% の障壁（4秒）。</summary>
        Shield = 3,
        /// <summary>自分を最大HPの Value% 回復。Arg=1 なら近くの味方も。</summary>
        Heal = 4,
        /// <summary>その記憶の残りクールダウンを Value% 縮める。</summary>
        Recharge = 5,
        /// <summary>4秒間、攻撃速度 +Value%（重ならず時間を延長）。</summary>
        Quicken = 6,
        /// <summary>4秒間、攻撃力・魔力 +Value%（重ならず時間を延長）。</summary>
        Empower = 7,
        /// <summary>当てた敵は4秒間、自分から受けるダメージが Value% 増える（重ならず時間を延長）。</summary>
        Expose = 8,
        /// <summary>当てたダメージの Value% を、0.3秒後にもう一度与える。</summary>
        Echo = 9,
    }

    /// <summary>記憶の仕掛け1つ分の定義（v1.28）。</summary>
    public sealed class GimmickDef
    {
        public GimmickTrigger Trigger { get; set; }
        public GimmickEffect Effect { get; set; }
        /// <summary>1段あたりの値。</summary>
        public int Value { get; set; }
        /// <summary>効果の補足（属性の種類、味方も回復するか）。</summary>
        public int Arg { get; set; }
        /// <summary>内部の間隔（秒）。0 なら制限なし。</summary>
        public float Cooldown { get; set; }
    }
}
