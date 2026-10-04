using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// ホストが受け付ける取引の依頼（v1.31）。金額は含まず、種別と Economy の固定レートで金額を計算するための引数だけを運ぶ。
    /// 旧やりとりの「クライアント申告の金額」（spendGold/spendDust/earnDust）は検証にも使わない。
    /// </summary>
    public sealed class TradeRequest
    {
        public long Token;
        public TradeKind Kind;
        /// <summary>MerchantGold：価格の計算に使った熱度（0〜Content.MaxHeat）。</summary>
        public int Heat;
        /// <summary>DustToShards：換える束数（1〜Economy.MaxBatchesPerTrade）。</summary>
        public int Batches;
        /// <summary>SalvageForDust：分解対象を指す識別子（Uid の 64bit。取引台帳の重複排除に使う）。</summary>
        public ulong SalvageUid;
        /// <summary>SalvageForDust：分解対象の希少度（(int)Rarity）と強化値。</summary>
        public int Rarity, Enhance;
        /// <summary>
        /// true なら取引の実行ではなく「この取引idの結果の照会」。実行済みなら記録済みの結果を返し、未実行ならその取引idを取り消す
        /// （あとから届いた元の要求は実行しない）。結果不明の取引の対価・返却を、二重にも欠落もなく決めるのに使う。
        /// </summary>
        public bool Query;
    }

    /// <summary>ホストが下した取引の判定。Replayed=true は同一トークンの再送で、記録済みの結果を返すだけ（通貨は動かさない）。</summary>
    public sealed class TradeDecision
    {
        public bool Ok;
        public bool Replayed;
        public string Reason;
        public int SpendGold, SpendDust, EarnDust;
    }

    /// <summary>
    /// 取引の通信上の符号化（プロトコル12のまま、NetMessages の形は変えない）。
    /// 旧項目（spendGold/spendDust/earnDust）を流用し、spendGold が負の物を v1.31 の「種別付き要求」とする。
    /// 旧ホストは負の支払いを「invalid」で安全に拒否し、旧クライアントの金額申告（spendGold ≥ 0）は新ホストが「protocol」で拒否する。
    /// 符号：spendGold = -(Tag + kind + rarity*4 + enhance*32)、spendDust/earnDust は種別ごとの引数か Uid の下位/上位32bit。
    /// </summary>
    public static class TradeWire
    {
        /// <summary>種別付き要求の目印（この値を超える負の spendGold）。</summary>
        public const int KindTag = 1000;

        /// <summary>結果の照会（種別の番号。取引の種別とは別に、同じ符号の枠に置く）。</summary>
        public const int QueryKind = 3;

        /// <summary>結果不明の取引idの照会を通信値へ落とす。</summary>
        public static void EncodeQuery(out int spendGold, out int spendDust, out int earnDust)
        {
            spendGold = -(KindTag + QueryKind);
            spendDust = 0;
            earnDust = 0;
        }

        /// <summary>保留中の取引を通信値へ落とす。クライアントの送信に使う。</summary>
        public static void Encode(PendingTrade t, out int spendGold, out int spendDust, out int earnDust)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            switch (t.Kind)
            {
                case TradeKind.MerchantGold:
                    spendGold = -(KindTag + (int)TradeKind.MerchantGold);
                    spendDust = t.Heat;
                    earnDust = 0;
                    break;
                case TradeKind.DustToShards:
                    spendGold = -(KindTag + (int)TradeKind.DustToShards);
                    spendDust = t.Batches;
                    earnDust = 0;
                    break;
                case TradeKind.SalvageForDust:
                    spendGold = -(KindTag + (int)TradeKind.SalvageForDust + t.Rarity * 4 + t.Enhance * 32);
                    ulong uid = PackUid(t.Uid);
                    spendDust = unchecked((int)uid);
                    earnDust = unchecked((int)(uid >> 32));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(t));
            }
        }

        /// <summary>通信値を要求へ直す。種別付き要求でなければ（旧クライアントの金額申告など）false。</summary>
        public static bool TryDecode(long token, int spendGold, int spendDust, int earnDust, out TradeRequest req)
        {
            req = null;
            if (spendGold >= 0 || spendGold <= int.MinValue + KindTag) return false;
            int payload = -spendGold - KindTag;
            int kind = payload % 4;
            int rarity = (payload / 4) % 8;
            int enhance = payload / 32;
            if (kind == QueryKind)
            {
                if (rarity != 0 || enhance != 0 || spendDust != 0 || earnDust != 0) return false;
                req = new TradeRequest { Token = token, Query = true };
                return true;
            }
            var r = new TradeRequest { Token = token, Kind = (TradeKind)kind, Heat = spendDust, Batches = spendDust, SalvageUid = ((ulong)(uint)earnDust << 32) | (uint)spendDust };
            switch (r.Kind)
            {
                case TradeKind.MerchantGold:
                    if (rarity != 0 || enhance != 0 || earnDust != 0) return false;
                    break;
                case TradeKind.DustToShards:
                    if (rarity != 0 || enhance != 0 || earnDust != 0) return false;
                    break;
                case TradeKind.SalvageForDust:
                    r.Rarity = rarity;
                    r.Enhance = enhance;
                    break;
                default:
                    return false;
            }
            req = r;
            return true;
        }
        /// <summary>Uid を 64bit へ詰める。本体の形式（"r"+16桁の16進）ならその値、それ以外は FNV-1a 64bit（Rng.SeedFrom）。</summary>
        public static ulong PackUid(string uid)
        {
            if (uid != null && uid.Length == 17 && uid[0] == 'r')
            {
                ulong v = 0;
                bool ok = true;
                for (int i = 1; i < 17; i++)
                {
                    char c = uid[i];
                    int d;
                    if (c >= '0' && c <= '9') d = c - '0';
                    else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                    else { ok = false; break; }
                    v = (v << 4) | (uint)d;
                }
                if (ok) return v;
            }
            return Rng.SeedFrom(uid);
        }

        /// <summary>64bit から Uid の文字列へ戻す（本体の形式）。復元できない物（代替ハッシュ）は正規形の鍵を返す。</summary>
        public static string UnpackUid(ulong bits) => "r" + bits.ToString("x16");
    }

    /// <summary>
    /// ホスト側の取引の裁定（v1.31）。金額は常に Economy の固定レート・残高・本体の価格補正から計り、クライアント申告の金額は使わない。
    /// 取引id（トークン）は1プレイヤーにつき1回だけ実行し、再送には記録済みの結果を返す（台帳は上限付き）。
    /// 分解は同じ遺物（Uid）を1ランにつき1回だけ受け付ける。すべて純粋な C# で、判定はこのクラスに集める。
    /// </summary>
    public sealed class TradeAuthority
    {
        /// <summary>1プレイヤーが台帳に残す実行済みトークンの上限（ランをまたいで保持。上限を超えた古い物から忘れる）。</summary>
        public const int MaxTokensPerPlayer = 256;
        /// <summary>1プレイヤー・1ランが台帳に残す分解済み Uid の上限。</summary>
        public const int MaxSalvagedUidsPerRun = 256;
        /// <summary>追跡するプレイヤー（接続）の上限。超えたら全部忘れる（通常の協力プレイでは起こらない）。</summary>
        public const int MaxTrackedPlayers = 32;

        private sealed class PlayerLedger
        {
            public string RunId;
            public readonly Queue<long> Order = new Queue<long>();
            public readonly Dictionary<long, TradeDecision> Executed = new Dictionary<long, TradeDecision>();
            /// <summary>実行済みの取引idの要求の中身（種別・引数）。同じidで別の要求が来たときに、過去の成功を流用しないための照合に使う。</summary>
            public readonly Dictionary<long, string> Fingerprints = new Dictionary<long, string>();
            /// <summary>照会で「未実行」と答えて取り消した取引id。あとから元の要求が届いても実行しない。</summary>
            public readonly Queue<long> CancelledOrder = new Queue<long>();
            public readonly HashSet<long> Cancelled = new HashSet<long>();
            public readonly Queue<ulong> SalvageOrder = new Queue<ulong>();
            public readonly HashSet<ulong> SalvagedUids = new HashSet<ulong>();
        }

        private readonly Dictionary<string, PlayerLedger> _players = new Dictionary<string, PlayerLedger>();

        /// <summary>検証用：そのプレイヤーの実行済みトークン数（台帳の上限が効いていることの確認に使う）。</summary>
        public int TrackedTokenCount(string playerKey) => _players.TryGetValue(playerKey, out var l) ? l.Executed.Count : 0;

        /// <summary>検証用：そのプレイヤー・ランの分解済み Uid 数。</summary>
        public int TrackedSalvagedUidCount(string playerKey, string runId) =>
            _players.TryGetValue(playerKey, out var l) && l.RunId == runId ? l.SalvagedUids.Count : 0;

        /// <summary>
        /// 取引を裁定する。金額・可否はこの場で決まり、呼び出し側は Ok &amp;&amp; !Replayed のときだけ本体の通貨を動かす。
        /// </summary>
        /// <param name="playerKey">プレイヤー（接続）を区切る鍵。ホストは本体の netId を使う。</param>
        /// <param name="runId">現在のランの識別子。分解の重複排除はラン単位。</param>
        /// <param name="req">要求。</param>
        /// <param name="gold">そのプレイヤーの現在のゴールド（本体の権威ある値）。</param>
        /// <param name="dust">そのプレイヤーの現在のドリームダスト（同上）。</param>
        /// <param name="goldCostScale">本体の価格補正（GameManager.GetAdjustedGoldAmount_Cost(1f)。ホスト側の値）。</param>
        public TradeDecision Evaluate(string playerKey, string runId, TradeRequest req, int gold, int dust, float goldCostScale = 1f)
        {
            var d = new TradeDecision();
            if (req == null || req.Token <= 0) { d.Reason = "invalid"; return d; }
            if (req.Query) return Resolve(playerKey, runId, req.Token);
            switch (req.Kind)
            {
                case TradeKind.MerchantGold:
                    if (req.Heat < 0 || req.Heat > Content.MaxHeat) { d.Reason = "invalid"; return d; }
                    d.SpendGold = Math.Max(1, (int)Math.Round(Economy.MerchantGoldBase(req.Heat) * Math.Max(0f, goldCostScale)));
                    break;
                case TradeKind.DustToShards:
                    if (req.Batches < 1 || req.Batches > Economy.MaxBatchesPerTrade) { d.Reason = "invalid"; return d; }
                    d.SpendDust = req.Batches * Economy.DustPerBatch;
                    break;
                case TradeKind.SalvageForDust:
                    if (req.Rarity < 0 || req.Rarity > (int)Rarity.Legendary || req.Enhance < 0
                        || req.Enhance > Content.MaxEnhanceFor((Rarity)req.Rarity, Content.MaxLimitBreaks((Rarity)req.Rarity)))
                    { d.Reason = "invalid"; return d; }
                    d.EarnDust = Economy.SalvageDust((Rarity)req.Rarity, req.Enhance);
                    if (d.EarnDust > Economy.MaxDustEarnPerTrade) { d.Reason = "invalid"; return d; }
                    break;
                default:
                    d.Reason = "invalid";
                    return d;
            }
            var ledger = Ledger(playerKey, runId);
            string fingerprint = Fingerprint(req);
            if (ledger.Executed.TryGetValue(req.Token, out var recorded))
            {
                // 同じ取引idの再送だけを冪等に扱う。idが同じでも要求の中身が違えば、過去の成功を流用せずに断る。
                if (!ledger.Fingerprints.TryGetValue(req.Token, out var recordedFingerprint) || recordedFingerprint != fingerprint)
                { d.Reason = "conflict"; return d; }
                return new TradeDecision { Ok = true, Replayed = true, SpendGold = recorded.SpendGold, SpendDust = recorded.SpendDust, EarnDust = recorded.EarnDust };
            }
            if (ledger.Cancelled.Contains(req.Token)) { d.Reason = "cancelled"; return d; }
            if (req.Kind == TradeKind.SalvageForDust && ledger.SalvagedUids.Contains(req.SalvageUid))
            { d.Reason = "dup"; return d; }
            if (d.SpendGold > gold) { d.Reason = "gold"; return d; }
            if (d.SpendDust > dust) { d.Reason = "dust"; return d; }
            d.Ok = true;
            ledger.Executed[req.Token] = d;
            ledger.Fingerprints[req.Token] = fingerprint;
            ledger.Order.Enqueue(req.Token);
            while (ledger.Order.Count > MaxTokensPerPlayer)
            {
                long old = ledger.Order.Dequeue();
                ledger.Executed.Remove(old);
                ledger.Fingerprints.Remove(old);
            }
            if (req.Kind == TradeKind.SalvageForDust)
            {
                ledger.SalvagedUids.Add(req.SalvageUid);
                ledger.SalvageOrder.Enqueue(req.SalvageUid);
                while (ledger.SalvageOrder.Count > MaxSalvagedUidsPerRun)
                    ledger.SalvagedUids.Remove(ledger.SalvageOrder.Dequeue());
            }
            return d;
        }

        /// <summary>
        /// 取引idの結果の照会。実行済みなら記録済みの結果（Replayed=true の成功）を返す。
        /// 未実行なら「unknown」で答えると同時にその取引idを取り消し、あとから届く元の要求も実行しない
        /// （クライアントは「ホストは何も支払っていない」として返却できる）。
        /// </summary>
        private TradeDecision Resolve(string playerKey, string runId, long token)
        {
            var ledger = Ledger(playerKey, runId);
            if (ledger.Executed.TryGetValue(token, out var recorded))
                return new TradeDecision { Ok = true, Replayed = true, SpendGold = recorded.SpendGold, SpendDust = recorded.SpendDust, EarnDust = recorded.EarnDust };
            if (ledger.Cancelled.Add(token))
            {
                ledger.CancelledOrder.Enqueue(token);
                while (ledger.CancelledOrder.Count > MaxTokensPerPlayer)
                    ledger.Cancelled.Remove(ledger.CancelledOrder.Dequeue());
            }
            return new TradeDecision { Reason = "unknown" };
        }

        /// <summary>要求の中身（種別と、金額の計算に使う引数）の照合用の文字列。取引id（Token）は含めない。</summary>
        private static string Fingerprint(TradeRequest req)
        {
            switch (req.Kind)
            {
                case TradeKind.MerchantGold: return "m:" + req.Heat;
                case TradeKind.DustToShards: return "d:" + req.Batches;
                default: return "s:" + req.SalvageUid + ":" + req.Rarity + ":" + req.Enhance;
            }
        }

        private PlayerLedger Ledger(string playerKey, string runId)
        {
            if (!_players.TryGetValue(playerKey, out var ledger))
            {
                if (_players.Count >= MaxTrackedPlayers) _players.Clear();
                _players[playerKey] = ledger = new PlayerLedger();
            }
            if (ledger.RunId != runId)
            {
                // ランが変わったら分解の重複排除だけ初めから（トークンの台帳はランをまたいで保持する）。
                ledger.RunId = runId;
                ledger.SalvageOrder.Clear();
                ledger.SalvagedUids.Clear();
            }
            return ledger;
        }
    }
}
