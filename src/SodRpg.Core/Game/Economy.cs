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
        /// <summary>応答待ちを始めた時刻（保存・通信には含めない）。</summary>
        public double StartedAt;
    }

    /// <summary>
    /// 取引の応答待ちを管理する。応答が来たら一度だけ確定し、重複・未知の応答は無視する（二重確定を防ぐ）。
    /// </summary>
    public sealed class TradeLedger
    {
        private readonly Dictionary<long, PendingTrade> _pending = new Dictionary<long, PendingTrade>();
        private readonly List<long> _expired = new List<long>();
        private long _next = 1;

        public int PendingCount => _pending.Count;

        public bool HasPending(TradeKind kind)
        {
            foreach (var t in _pending.Values)
                if (t.Kind == kind) return true;
            return false;
        }

        /// <summary>分解の応答待ちで、別の操作に使えない遺物か。</summary>
        public bool IsReserved(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            foreach (var t in _pending.Values)
                if (t.Kind == TradeKind.SalvageForDust && t.Uid == uid) return true;
            return false;
        }

        public HashSet<string> ReservedSalvageUids()
        {
            var uids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in _pending.Values)
                if (t.Kind == TradeKind.SalvageForDust && !string.IsNullOrEmpty(t.Uid)) uids.Add(t.Uid);
            return uids;
        }

        /// <summary>30秒返事がない分解予約を解除し、返却処理へ取引を渡す。支払い待ちの取引は捨てない。</summary>
        public int ExpireSalvage(double now, Action<PendingTrade> onExpired = null)
        {
            _expired.Clear();
            foreach (var t in _pending.Values)
                if (t.Kind == TradeKind.SalvageForDust && now - t.StartedAt >= 30.0) _expired.Add(t.Token);
            foreach (var token in _expired)
            {
                var t = _pending[token];
                _pending.Remove(token);
                onExpired?.Invoke(t);
            }
            return _expired.Count;
        }

        public PendingTrade Begin(TradeKind kind, int spendGold, int spendDust, int earnDust, string uid = null, double now = 0)
        {
            if (spendGold < 0 || spendDust < 0 || earnDust < 0) throw new ArgumentOutOfRangeException();
            var t = new PendingTrade { Token = _next++, Kind = kind, SpendGold = spendGold, SpendDust = spendDust, EarnDust = earnDust, Uid = uid, StartedAt = now };
            _pending[t.Token] = t;
            return t;
        }

        /// <summary>成功・失敗とも取引を返して予約を解除する。未知・重複の応答は null。</summary>
        public PendingTrade Complete(long token, bool ok)
        {
            if (!_pending.TryGetValue(token, out var t)) return null;
            _pending.Remove(token);
            return t;
        }

        /// <summary>接続が切れたときなど、応答待ちを捨てる。</summary>
        public void Clear() => _pending.Clear();
    }
}
