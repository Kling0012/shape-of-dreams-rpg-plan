using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 本体のラン内通貨（ゴールド・ドリームダスト）との橋渡し。通貨を動かすのはホストだけなので、
    /// クライアントは「取引」を依頼し、ホストの成功応答を受けてから遺物側の処理を確定する。
    /// </summary>
    public static class Economy
    {
        /// <summary>ドリームダスト→欠片の換算単位。</summary>
        public const int DustPerBatch = 100;
        public const int ShardsPerBatch = 10;

        /// <summary>1回の取引で受け取れるドリームダストの上限（ホスト側の検証に使う）。</summary>
        public const int MaxDustEarnPerTrade = 2000;

        /// <summary>夢の商人の基本価格（ゴールド、難易度補正の前）。</summary>
        public static int MerchantGoldBase(int heat) => 60 + 15 * Loot.ClampHeat(heat);

        /// <summary>遠征中に未確保の遺物を分解したときのドリームダスト。</summary>
        public static int SalvageDust(Relic r) => Content.SalvageShards(r.Rarity) * 5 + r.Enhance * 10;
    }

    public enum TradeKind
    {
        MerchantGold = 0,
        DustToShards = 1,
        SalvageForDust = 2,
    }

    /// <summary>ホストの応答待ちの取引。</summary>
    public sealed class PendingTrade
    {
        public long Token;
        public TradeKind Kind;
        public int SpendGold;
        public int SpendDust;
        public int EarnDust;
        /// <summary>分解する遺物など、確定時に使う対象。</summary>
        public string Uid;
    }

    /// <summary>
    /// 取引の応答待ちを管理する。応答が来たら一度だけ確定し、重複・未知の応答は無視する（二重確定を防ぐ）。
    /// </summary>
    public sealed class TradeLedger
    {
        private readonly Dictionary<long, PendingTrade> _pending = new Dictionary<long, PendingTrade>();
        private long _next = 1;

        public int PendingCount => _pending.Count;

        public bool HasPending(TradeKind kind)
        {
            foreach (var t in _pending.Values)
                if (t.Kind == kind) return true;
            return false;
        }

        public PendingTrade Begin(TradeKind kind, int spendGold, int spendDust, int earnDust, string uid = null)
        {
            if (spendGold < 0 || spendDust < 0 || earnDust < 0) throw new ArgumentOutOfRangeException();
            var t = new PendingTrade { Token = _next++, Kind = kind, SpendGold = spendGold, SpendDust = spendDust, EarnDust = earnDust, Uid = uid };
            _pending[t.Token] = t;
            return t;
        }

        /// <summary>ホストの応答。成功なら確定すべき取引を返す。失敗・未知・重複なら null。</summary>
        public PendingTrade Complete(long token, bool ok)
        {
            if (!_pending.TryGetValue(token, out var t)) return null;
            _pending.Remove(token);
            return ok ? t : null;
        }

        /// <summary>接続が切れたときなど、応答待ちを捨てる。</summary>
        public void Clear() => _pending.Clear();
    }
}
