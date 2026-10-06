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
        public const int DustPerBatch = EconomyBalance.DustPerBatch;
        public const int ShardsPerBatch = EconomyBalance.ShardsPerBatch;

        /// <summary>1回の取引で受け取れるドリームダストの上限（ホスト側の検証に使う）。</summary>
        public const int MaxDustEarnPerTrade = 2000;

        /// <summary>1回の換金で売れる束数の上限（ドリームダスト DustPerBatch 単位）。</summary>
        public const int MaxBatchesPerTrade = 10;

        /// <summary>取引の応答待ちの期限（秒）。過ぎたら画面の待ちは解くが、取引は「結果不明」として残してホストの確定結果を照会する。</summary>
        public const double TradeTimeoutSeconds = 10.0;

        /// <summary>結果不明の取引をホストへ照会する間隔（秒）と、1つの取引あたりの照会回数の上限。</summary>
        public const double TradeQueryIntervalSeconds = 10.0;
        public const int MaxTradeQueries = 30;

        /// <summary>夢の商人の基本価格（ゴールド、難易度補正の前）。</summary>
        public static int MerchantGoldBase(int heat) => EconomyBalance.MerchantBaseGold + EconomyBalance.MerchantGoldPerHeat * Loot.ClampHeat(heat);

        /// <summary>遠征中に未確保の遺物を分解したときのドリームダスト。ホストは種別と引数からこれで計算する。</summary>
        public static int SalvageDust(Relic r) => SalvageDust(r.Rarity, r.Enhance);

        public static int SalvageDust(Rarity rarity, int enhance) => Content.SalvageShards(rarity) * EconomyBalance.SalvageDustPerShard + enhance * EconomyBalance.SalvageDustPerEnhance;

        /// <summary>Unenhanced rate for optional overflow bonuses and persisted legacy overflow trades.</summary>
        public static int SatchelOverflowDust(Rarity rarity) => SalvageDust(rarity, 0);
    }

    public enum TradeKind
    {
        MerchantGold = 0,
        DustToShards = 1,
        SalvageForDust = 2,
        // Persisted v2.3.1–v2.4.0 obligations only; new overflow credits shards and accumulates optional dust without trades.
        SatchelOverflowDust = 3,
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
        /// <summary>期限を過ぎても応答がなく、ホストの確定結果を照会している取引。画面の待ちからは外れるが、対価・返却は未確定のまま残る。</summary>
        public bool Unresolved;
        /// <summary>照会した回数と、次に照会する時刻（保存しない）。</summary>
        public int Queries;
        public double NextQueryAt;
        /// <summary>
        /// 送ったときに知っていたホストの取引台帳の識別子（0 は不明）。照会は「この台帳に記録がない」ときだけ「未実行」と言えるので、
        /// 台帳が替わった（ホストの再読み込み・接続の netId 変更など）取引は、記録がないことを未実行の証拠にしない。保存する。
        /// </summary>
        public long LedgerId;
        /// <summary>
        /// ホストが「記録の有無を確かめられない」と答えた取引。対価も返却も確定せず、遅れて届く成功・失敗の応答か、
        /// 利用者の明示的な放棄（TradeLedger.TakeLost）で初めて片付く。保存する。
        /// </summary>
        public bool Lost;
        // 以下は v1.31 のホスト検証に使う引数（金額ではなく、金額の計算に使う値）。
        /// <summary>MerchantGold：価格の計算に使った熱度。</summary>
        public int Heat;
        /// <summary>購入元の出来事の提示識別子。旧保存では null（購入元を推測しない）。</summary>
        public string MerchantOfferId;
        /// <summary>DustToShards：換える束数。</summary>
        public int Batches;
        /// <summary>SalvageForDust：分解対象の希少度（(int)Rarity）と強化値。</summary>
        public int Rarity, Enhance;
        public string RunId;
        public Relic Relic;
        public int FallbackShards;
        /// <summary>
        /// 全フィールドを写した写し。Profile.Clone と TradeLedger が使う。フィールドを足すときはここにも必ず並べる
        /// （試験が全 public フィールドの一致を見るので、コピー漏れはすぐ分かる）。
        /// </summary>
        public PendingTrade Clone() => new PendingTrade
        {
            Token = Token, Kind = Kind, SpendGold = SpendGold, SpendDust = SpendDust, EarnDust = EarnDust, Uid = Uid,
            StartedAt = StartedAt, Unresolved = Unresolved, Queries = Queries, NextQueryAt = NextQueryAt,
            LedgerId = LedgerId, Lost = Lost, Heat = Heat, MerchantOfferId = MerchantOfferId,
            Batches = Batches, Rarity = Rarity, Enhance = Enhance,
            RunId = RunId, Relic = Relic?.Clone(), FallbackShards = FallbackShards,
        };
    }

    /// <summary>ホストの応答を台帳へ反映した結果。</summary>
    public enum TradeOutcome
    {
        /// <summary>知らない取引id、または反映済みの重複応答。何もしない。</summary>
        NotFound = 0,
        /// <summary>ホストが支払いを確定した。取引は片付き、対価を一度だけ付ける。</summary>
        Paid = 1,
        /// <summary>ホストが実行していないと確定した。取引は片付き、予約した遺物を戻す。</summary>
        Failed = 2,
        /// <summary>ホストが記録の有無を確かめられないと答えた（初めて）。取引は片付けずに残す。</summary>
        Lost = 3,
        /// <summary>すでに確認不能として残している取引への、重ねての同じ答え。</summary>
        AlreadyLost = 4,
    }

    /// <summary>
    /// 取引の応答待ちを管理する。応答が来たら一度だけ確定し、重複・未知の応答は無視する（二重確定を防ぐ）。
    /// </summary>
    public sealed class TradeLedger
    {
        /// <summary>Maximum held manual trades. Automatic overflow settlements have a separate restore-size safety ceiling.</summary>
        public const int MaxHeld = 64;

        /// <summary>
        /// 保存から読み戻す取引の上限（壊れた保存での肥大化を防ぐだけの安全弁）。MaxHeld を超えて保存されている取引（旧版の保存など）も、
        /// 捨てずにここまで全件復元する。超えた分だけが復元できず、その場合は呼び出し側へ捨てた件数を返す。
        /// </summary>
        public const int MaxRestored = 4096;

        private readonly Dictionary<long, PendingTrade> _pending = new Dictionary<long, PendingTrade>();
        private readonly List<long> _expired = new List<long>();
        private long _next;

        /// <summary>
        /// 取引idは「世代（上位32bit）＋連番（下位32bit）」。世代はインスタンスごとに変えるので、MODの再読み込みなどで
        /// 台帳だけが作り直されても、ホストが覚えている過去の取引id（接続単位）と衝突しにくい。世代 0 は連番だけ（試験用）。
        /// </summary>
        public TradeLedger() : this(NewGeneration())
        {
        }

        public TradeLedger(int generation)
        {
            _next = ((long)(generation & 0x3FFFFFFF) << 32) + 1;
        }

        private static int NewGeneration()
        {
            int g = Guid.NewGuid().GetHashCode() & 0x3FFFFFFF;
            return g == 0 ? 1 : g;
        }

        /// <summary>Manual trades currently awaiting responses; automatic overflow does not block secure/delve operations.</summary>
        public int PendingCount
        {
            get
            {
                int n = 0;
                foreach (var t in _pending.Values)
                    if (!t.Unresolved && t.Kind != TradeKind.SatchelOverflowDust) n++;
                return n;
            }
        }

        /// <summary>期限切れで結果不明のまま残っている取引の数（ホストが確かめられないと答えた取引も含む）。</summary>
        public int UnresolvedCount
        {
            get
            {
                int n = 0;
                foreach (var t in _pending.Values) if (t.Unresolved) n++;
                return n;
            }
        }

        /// <summary>ホストが記録の有無を確かめられないと答えた取引の数。</summary>
        public int LostCount
        {
            get
            {
                int n = 0;
                foreach (var t in _pending.Values) if (t.Lost) n++;
                return n;
            }
        }

        /// <summary>Whether another manual trade fits its budget; overflow settlements do not consume that budget.</summary>
        public bool CanBegin
        {
            get
            {
                int n = 0;
                foreach (var t in _pending.Values)
                    if (t.Kind != TradeKind.SatchelOverflowDust) n++;
                return n < MaxHeld;
            }
        }

        /// <summary>応答待ち・結果不明を合わせて、まだ対価や返却が確定していない取引の数。プロフィールの切り替えなどを止めるのに使う。</summary>
        public int HeldCount => _pending.Count;

        public bool HasPending(TradeKind kind)
        {
            foreach (var t in _pending.Values)
                if (t.Kind == kind && !t.Unresolved) return true;
            return false;
        }

        /// <summary>同じ商人の購入を予約中か。結果不明・確認不能も含む。購入元不明の旧取引が残る間は安全のため再購入を止める。</summary>
        public bool IsMerchantReserved(string offerId)
        {
            foreach (var t in _pending.Values)
                if (t.Kind == TradeKind.MerchantGold
                    && (string.IsNullOrEmpty(t.MerchantOfferId) || t.MerchantOfferId == offerId)) return true;
            return false;
        }

        /// <summary>分解の応答待ち（結果不明を含む）で、別の操作に使えない遺物か。</summary>
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

        /// <summary>
        /// 応答のない取引を、全種別で「結果不明」にする。期限は Economy.TradeTimeoutSeconds。
        /// 取引は捨てない：ホストがすでに支払っていれば、遅れて来た成功応答（や照会の結果）で対価を一度だけ付ける。
        /// 新しく結果不明になった取引の数を返し、onUnresolved には新しい取引だけを渡す。
        /// </summary>
        public int Expire(double now, Action<PendingTrade> onUnresolved = null)
        {
            _expired.Clear();
            foreach (var t in _pending.Values)
                if (!t.Unresolved && now - t.StartedAt >= Economy.TradeTimeoutSeconds) _expired.Add(t.Token);
            foreach (var token in _expired)
            {
                var t = _pending[token];
                t.Unresolved = true;
                t.NextQueryAt = now;
                onUnresolved?.Invoke(t);
            }
            return _expired.Count;
        }

        /// <summary>
        /// ホストへ照会する時期が来た結果不明の取引を集める（集めた取引は照会回数と次の時刻を進める）。
        /// 接続していないときは呼ばない（呼び出し側が判断する）。上限回数に達した取引は照会しないが、遅れた応答は受け付け続ける。
        /// </summary>
        public int CollectDueQueries(double now, List<PendingTrade> into)
        {
            int n = 0;
            foreach (var t in _pending.Values)
            {
                if (!t.Unresolved || t.Lost || t.Queries >= Economy.MaxTradeQueries || now < t.NextQueryAt) continue;
                t.Queries++;
                t.NextQueryAt = now + Economy.TradeQueryIntervalSeconds;
                into.Add(t);
                n++;
            }
            return n;
        }

        /// <summary>接続の切り替えなど：保持している取引をすべて結果不明にして、すぐ照会できるようにする。</summary>
        public void MarkAllUnresolved(double now)
        {
            foreach (var t in _pending.Values)
            {
                t.Unresolved = true;
                if (t.Lost) continue; // 確かめられないと答えられた取引は、照会し直しても答えは変わらない
                t.Queries = 0;
                t.NextQueryAt = now;
            }
        }

        /// <summary>保存用：保持している取引の写し（トークン順）。</summary>
        public List<PendingTrade> Snapshot()
        {
            var list = new List<PendingTrade>();
            foreach (var t in _pending.Values) list.Add(t.Clone());
            list.Sort((x, y) => x.Token.CompareTo(y.Token));
            return list;
        }

        /// <summary>
        /// 保存から戻す。戻した取引は結果不明として、起動後すぐにホストへ照会する（確認不能と記録された取引は照会しない）。
        /// MaxHeld は新規の受付だけを止める上限なので、保存されている取引は MaxRestored まで全件戻す。戻せずに捨てた件数を返す。
        /// </summary>
        public int Restore(IEnumerable<PendingTrade> trades, double now)
        {
            int dropped = 0;
            if (trades == null) return dropped;
            foreach (var saved in trades)
            {
                if (saved == null || saved.Token <= 0 || _pending.ContainsKey(saved.Token)) continue;
                if (_pending.Count >= MaxRestored) { dropped++; continue; }
                var t = saved.Clone();
                t.Unresolved = true;
                t.Queries = 0;
                t.NextQueryAt = now;
                t.StartedAt = now;
                _pending[t.Token] = t;
            }
            return dropped;
        }


        /// <summary>上限に達していたら、取引の登録も通信も通貨の変更も始めさせない（呼び出し側は先に CanBegin で案内する）。</summary>
        private void EnsureRoom()
        {
            if (!CanBegin)
                throw new InvalidOperationException(Loc.T(
                    $"未確定の取引が{MaxHeld}件に達しています。結果が確認できるまで、新しい取引はできません。",
                    $"There are already {MaxHeld} unresolved trades. New trades are paused until their results are confirmed."));
        }

        public PendingTrade Begin(TradeKind kind, int spendGold, int spendDust, int earnDust, string uid = null, double now = 0)
        {
            if (spendGold < 0 || spendDust < 0 || earnDust < 0) throw new ArgumentOutOfRangeException();
            EnsureRoom();
            var t = new PendingTrade { Token = _next++, Kind = kind, SpendGold = spendGold, SpendDust = spendDust, EarnDust = earnDust, Uid = uid, StartedAt = now };
            _pending[t.Token] = t;
            return t;
        }

        /// <summary>商人の購入（v1.31）。価格は表示と同じ物を使い、ホストは熱度から自分で計算し直す。</summary>
        public PendingTrade BeginMerchant(int heat, int price, double now, string offerId = null)
        {
            if (heat < 0 || heat > Content.MaxHeat) throw new ArgumentOutOfRangeException(nameof(heat));
            if (price < 1) throw new ArgumentOutOfRangeException(nameof(price));
            if (!string.IsNullOrEmpty(offerId) && IsMerchantReserved(offerId))
                throw new InvalidOperationException(Loc.T("この商人の取引は未確定です。", "This merchant's trade is unresolved."));
            EnsureRoom();
            var t = new PendingTrade { Token = _next++, Kind = TradeKind.MerchantGold, SpendGold = price, Heat = heat, StartedAt = now, MerchantOfferId = offerId };
            _pending[t.Token] = t;
            return t;
        }

        /// <summary>ドリームダスト→欠片（v1.31）。支払うダストは束数から Economy の固定レートで計算する。</summary>
        public PendingTrade BeginDustToShards(int batches, double now)
        {
            if (batches < 1 || batches > Economy.MaxBatchesPerTrade) throw new ArgumentOutOfRangeException(nameof(batches));
            EnsureRoom();
            var t = new PendingTrade { Token = _next++, Kind = TradeKind.DustToShards, SpendDust = batches * Economy.DustPerBatch, Batches = batches, StartedAt = now };
            _pending[t.Token] = t;
            return t;
        }

        /// <summary>未確保の遺物の分解（v1.31）。受け取るダストは Economy.SalvageDust で計算する。</summary>
        public PendingTrade BeginSalvage(Relic r, double now) =>
            r == null ? throw new ArgumentNullException(nameof(r)) : BeginSalvage(r.Rarity, r.Enhance, r.Uid, now);

        public PendingTrade BeginSalvage(Rarity rarity, int enhance, string uid, double now)
        {
            if (string.IsNullOrEmpty(uid)) throw new ArgumentNullException(nameof(uid));
            EnsureRoom();
            var t = new PendingTrade { Token = _next++, Kind = TradeKind.SalvageForDust, EarnDust = Economy.SalvageDust(rarity, enhance), Uid = uid, Rarity = (int)rarity, Enhance = enhance, StartedAt = now };
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

        /// <summary>
        /// ホストの応答を台帳へ反映する。「確かめられない」（reasonCode が TradeWire.LostReason）なら取引は片付けず確認不能として残し、
        /// それ以外は成功・失敗とも取引を返して予約を解除する（Complete と同じ）。未知・重複の応答は NotFound。
        /// </summary>
        public TradeOutcome OnResult(long token, bool ok, string reasonCode, out PendingTrade trade)
        {
            if (!_pending.TryGetValue(token, out trade)) { trade = null; return TradeOutcome.NotFound; }
            if (!ok && reasonCode == TradeWire.LostReason)
            {
                bool first = !trade.Lost;
                trade.Lost = true;
                trade.Unresolved = true;
                return first ? TradeOutcome.Lost : TradeOutcome.AlreadyLost;
            }
            _pending.Remove(token);
            return ok ? TradeOutcome.Paid : TradeOutcome.Failed;
        }

        /// <summary>
        /// 確認不能の取引を、利用者の明示的な操作で手放す。取引は消え、対価は付かない（呼び出し側は予約した遺物を戻す）。
        /// 実際にはホストが支払い済みだった場合は、その分は戻らない。自動では呼ばない。
        /// </summary>
        public List<PendingTrade> TakeLost()
        {
            var taken = new List<PendingTrade>();
            foreach (var t in _pending.Values) if (t.Lost) taken.Add(t);
            foreach (var t in taken) _pending.Remove(t.Token);
            taken.Sort((x, y) => x.Token.CompareTo(y.Token));
            return taken;
        }

        /// <summary>応答待ちをすべて捨てる（試験や、結果を照会できない状況の最後の手段）。通常の接続切り替えでは MarkAllUnresolved を使う。</summary>
        public void Clear() => _pending.Clear();
    }
}
