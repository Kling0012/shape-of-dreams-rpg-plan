using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 星図のエッセンス枠（v1.27）。アイデンティティ記憶と回避（移動の記憶）に、エッセンスをもう1つはめられるようにする。
    /// ここでは足す数の集計とはみ出しの計算だけを行う。ゲームへの設定（SetMaxGemCount・UnequipGem）はホスト側の接続層。
    /// </summary>
    public static class EssenceSlots
    {
        /// <summary>全ソースを合算した、旅人1人あたりの追加枠上限。</summary>
        public const int MaxAdded = StarRankBalance.EssenceSlotsMaxAdded;

        /// <summary>1つの枠で足せる数の上限。</summary>
        public const int MaxPerLocation = StarRankBalance.EssenceSlotsMaxPerLocation;

        /// <summary>手作りの Build にも同じ共有上限を適用する。予算不足時はアイデンティティを先に数える。</summary>
        public static int AddedFrom(Build b, Stat s)
        {
            if (b == null) return 0;
            int identity = Math.Min(MaxAdded, ClampAdded(b.Get(Stat.EssenceSlotIdentity)));
            if (s == Stat.EssenceSlotIdentity) return identity;
            if (s == Stat.EssenceSlotMovement)
                return Math.Min(MaxAdded - identity, ClampAdded(b.Get(s)));
            return 0;
        }

        public static int ClampAdded(int added) => Math.Max(0, Math.Min(MaxPerLocation, added));

        /// <summary>全ソースの集計後に適用する。保存データや通信の形式は変えない。</summary>
        public static void Normalize(Build b)
        {
            int identity = AddedFrom(b, Stat.EssenceSlotIdentity);
            int movement = AddedFrom(b, Stat.EssenceSlotMovement);
            if (b.Stats.ContainsKey(Stat.EssenceSlotIdentity)) b.Stats[Stat.EssenceSlotIdentity] = identity;
            if (b.Stats.ContainsKey(Stat.EssenceSlotMovement)) b.Stats[Stat.EssenceSlotMovement] = movement;
        }

        /// <summary>上限が狭まったとき、はみ出すエッセンスの数（足元へ落とす分。壊さない）。</summary>
        public static int Overflow(int currentGems, int newMax) => Math.Max(0, currentGems - newMax);
    }
}
