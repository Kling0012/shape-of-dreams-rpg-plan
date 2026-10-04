using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 星図のエッセンス枠（v1.27）。アイデンティティ記憶と回避（移動の記憶）に、エッセンスをもう1つはめられるようにする。
    /// ここでは足す数の集計とはみ出しの計算だけを行う。ゲームへの設定（SetMaxGemCount・UnequipGem）はホスト側の接続層。
    /// </summary>
    public static class EssenceSlots
    {
        /// <summary>1つの枠で足せる数の上限（旅人ごとにアイデンティティ +1・移動 +1）。</summary>
        public const int MaxPerLocation = 1;

        /// <summary>Build の能力値から、その枠をいくつ足すか（0〜1。負や過大な値は切り詰める）。</summary>
        public static int AddedFrom(Build b, Stat s) => ClampAdded(b.Get(s));

        public static int ClampAdded(int added) => Math.Max(0, Math.Min(MaxPerLocation, added));

        /// <summary>上限が狭まったとき、はみ出すエッセンスの数（足元へ落とす分。壊さない）。</summary>
        public static int Overflow(int currentGems, int newMax) => Math.Max(0, currentGems - newMax);
    }
}
