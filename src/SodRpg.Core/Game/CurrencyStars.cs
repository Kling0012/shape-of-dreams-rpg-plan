using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// v1.32 A「巡る富」：獲得ゴールド・夢のダストを増やす星の効果（Power KillGoldPct〜DreamDustDelvePct）。
    /// 値はすべて星図だけの合計（装備には付かない）。ホストが旅人ごとに Build から読み取って適用する。
    /// このクラスは文言・合計・丸めだけを持ち、ゲームへの作用（monsterKillGoldMultiplier の加減算、
    /// Pickup_DreamDust.onGiveDreamDust、エリート・ボスの追加ゴールド）は SodRpg.Mod の HostAuthority.Currency が行う。
    /// </summary>
    public static class CurrencyStars
    {
        public static bool IsPower(Power p) => p >= Power.KillGoldPct && p <= Power.DreamDustDelvePct;

        private static readonly Txt[] Names =
        {
            new Txt("巡る富", "Circulating Wealth"),
            new Txt("黄金の嗅覚", "Golden Nose"),
            new Txt("夢屑の集め", "Dust Gathering"),
            new Txt("夢屑の籠", "Dust Basket"),
        };

        public static string Name(Power p) => IsPower(p) ? Names[(int)p - (int)Power.KillGoldPct].ToString() : "-";

        public static string Describe(Power p, int v)
        {
            string body;
            switch (p)
            {
                case Power.KillGoldPct:
                    body = Loc.T($"撃破獲得ゴールド +{v}%／敵を倒したとき。本体の星『富の蓄え』などの撃破ゴールド倍率に加算",
                        $"Kill gold +{v}% when enemies are killed; adds to the base game's kill-gold multiplier bonuses");
                    break;
                case Power.EliteKillGoldPct:
                    body = Loc.T($"エリート・ボスの撃破獲得ゴールド +{v}%／自分に分配される撃破ゴールドを、撃破ゴールド倍率を適用した後の額からさらに増やす",
                        $"Elite and Boss kill gold +{v}% of your share after applying your kill-gold multipliers");
                    break;
                case Power.DreamDustPct:
                    body = Loc.T($"夢のダスト拾得量 +{v}%／自分で拾ったとき。仲間からの贈り物は対象外",
                        $"Dream Dust picked up +{v}% when you collect it yourself; excludes gifts from allies");
                    break;
                case Power.DreamDustDelvePct:
                    body = Loc.T($"夢のダスト拾得量 +{v}%（潜行中は合計+{v * 2}%）／自分で拾ったとき。確保せず開始深度より深く進んでいる間は、同じ増加量をもう1回加算。仲間からの贈り物は対象外",
                        $"Dream Dust picked up +{v}% (+{v * 2}% total while delving) when you collect it yourself; adds the same bonus again while proceeding beyond your starting depth without securing the run; excludes gifts from allies");
                    break;
                default: return "-";
            }
            string cap = p == Power.DreamDustDelvePct
                ? Loc.T($"（同じ効果の星を合算し、通常時の増加量は{Content.PowerCap(p)}%、潜行中は{Content.PowerCap(p) * 2}%まで。夢のダストの他の増加効果とは別枠）",
                    $" (combined stars of this effect capped at +{Content.PowerCap(p)}% normally and +{Content.PowerCap(p) * 2}% while delving, separate from other Dream Dust bonuses)")
                : Loc.T($"（同じ効果の星の合計上限{Content.PowerCap(p)}%。他の獲得量増加とは別枠）",
                    $" (combined stars of this effect capped at {Content.PowerCap(p)}%, separate from other acquisition bonuses)");
            return body + cap;
        }

        /// <summary>撃破ゴールドの倍率へ足す量（1.0 = +100%）。本体の「富の蓄え」（+4% = 0.04）と同じ単位。</summary>
        public static float KillGoldMultiplierDelta(Build build) => build == null ? 0f : build.Get(Power.KillGoldPct) / 100f;

        /// <summary>エリート・ボスの撃破ゴールドに足す%。</summary>
        public static int EliteKillGoldPercent(Build build) => build == null ? 0 : build.Get(Power.EliteKillGoldPct);

        /// <summary>自分で拾う夢のダストに掛ける%。夢屑の籠は常に+X%、潜行中はさらに+X%。</summary>
        public static int DreamDustPercent(Build build, bool delving)
        {
            if (build == null) return 0;
            int basket = build.Get(Power.DreamDustDelvePct);
            return build.Get(Power.DreamDustPct) + basket + (delving ? basket : 0);
        }

        /// <summary>
        /// 潜行中か（確保せず次のゾーンへ進み、深度が開始深度を超えている）。ホスト自身の遠征の状態で判定する。
        /// </summary>
        public static bool IsDelving(RunState run) => run != null && run.Heat > run.StartDepth;

        /// <summary>本体の DewMath.RandomRoundToInt と同じ確率丸め。roll は [0,1) の乱数。</summary>
        public static int RandomRound(double value, double roll)
        {
            if (value <= 0d || double.IsNaN(value) || double.IsInfinity(value)) return 0;
            double floor = Math.Floor(value);
            if (floor >= int.MaxValue) return int.MaxValue;
            return (int)floor + (roll < value - floor ? 1 : 0);
        }

        /// <summary>夢のダスト1回の拾得に上乗せする量。</summary>
        public static int DreamDustBonus(int amount, int percent, double roll)
            => amount <= 0 || percent <= 0 ? 0 : RandomRound(amount * (double)percent / 100d, roll);

        /// <summary>
        /// 拾った夢のダスト1回分に上乗せする量。仲間からの贈り物（Pickup_DreamDust.isGivenByOtherPlayer）には掛けない。
        /// MODの取引（HostAuthority.Trades）は Pickup を通らないので、そもそもここへ来ない。
        /// </summary>
        public static int DreamDustBonusForPickup(Build build, bool delving, int amount, bool isGivenByOtherPlayer, double roll)
            => isGivenByOtherPlayer ? 0 : DreamDustBonus(amount, DreamDustPercent(build, delving), roll);

        /// <summary>
        /// エリート・ボス1体の撃破ゴールドのうち、この旅人に上乗せする量（丸め前）。本体の GrantGold と同じく、
        /// 共有ドロップは人数で割り、プロファイルの倍率とこの旅人の monsterKillGoldMultiplier を掛けた額の percent% を足す。
        /// </summary>
        public static double EliteKillGoldBonus(int killGold, int playerCount, float profileMultiplier, float playerMultiplier, int percent)
        {
            if (killGold <= 0 || playerCount <= 0 || percent <= 0) return 0d;
            double share = killGold / (double)playerCount * profileMultiplier * playerMultiplier;
            return Math.Max(0d, share * percent / 100d);
        }
    }

    /// <summary>
    /// 本体の倍率プロパティへ「足して、あとで引く」ための帳簿。他の効果（本体の星など）が同じ値を同時に動かしても壊さず、
    /// 他に誰も触っていなければ、解除で元の値に正確に戻す（浮動小数点の足し引きによるずれを残さない）。
    /// </summary>
    public sealed class AdditiveMultiplierLedger<TKey>
    {
        private sealed class Entry
        {
            public float Baseline, Delta, Set;
        }

        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();

        public int Count => _entries.Count;
        public IEnumerable<TKey> Keys => _entries.Keys;
        public bool Contains(TKey key) => _entries.ContainsKey(key);
        public float AppliedDelta(TKey key) => _entries.TryGetValue(key, out var e) ? e.Delta : 0f;

        /// <summary>現在値に delta を足した新しい値を返す。すでに適用中なら先に解除してから足し直す。</summary>
        public float Apply(TKey key, float current, float delta)
        {
            current = Release(key, current);
            if (delta == 0f) return current;
            float set = current + delta;
            _entries[key] = new Entry { Baseline = current, Delta = delta, Set = set };
            return set;
        }

        /// <summary>適用を解除した新しい値を返す。適用していなければ current のまま。</summary>
        public float Release(TKey key, float current)
        {
            if (!_entries.TryGetValue(key, out var entry)) return current;
            _entries.Remove(key);
            // 誰も触っていなければ最初の値そのもの。他者が動かしていたら差分だけ引く。
            return current == entry.Set ? entry.Baseline : current - entry.Delta;
        }

        /// <summary>帳簿だけを捨てる（相手の値が消えた、プレイヤーが去った等で戻す必要がないとき）。</summary>
        public void Forget(TKey key) => _entries.Remove(key);

        public void Clear() => _entries.Clear();
    }
}
