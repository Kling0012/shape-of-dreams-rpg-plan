using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 1キャラ分の「今回の強さ」。装着中の遺物・専門化・夢の深度から毎回組み立てる。
    /// ゲームへ渡すのはこの結果だけで、能力補正そのものは保存しない（計画書 付録C）。
    /// </summary>
    public sealed class Build
    {
        public SortedDictionary<Stat, int> Stats { get; } = new SortedDictionary<Stat, int>();
        public SortedDictionary<Power, int> Powers { get; } = new SortedDictionary<Power, int>();
        /// <summary>系統ごとの装着数（2以上でセット効果）。表示用で、通信には含めない。</summary>
        public SortedDictionary<Line, int> Lines { get; } = new SortedDictionary<Line, int>();
        /// <summary>セット遺物の装着数（表示用）。</summary>
        public SortedDictionary<string, int> Sets { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>装着中の遺物の連携（v1.26）。覚醒の倍率を反映した値で、正しくない物は入れない。</summary>
        public List<LinkDef> Links { get; } = new List<LinkDef>();
        public int Heat { get; set; }

        public int Get(Stat s) => Stats.TryGetValue(s, out int v) ? v : 0;
        public int Get(Power p) => Powers.TryGetValue(p, out int v) ? v : 0;

        /// <summary>潜行1段ごとの被ダメージ増加（%）。本体 Limbo の「被ダメージ増加」と同じ表現。</summary>
        public const int DamageTakenPerDelvePct = 6;

        /// <summary>被ダメージの倍率（1.0 = 増減なし）。</summary>
        public float DamageTakenMultiplier => 1f + DamageTakenPerDelvePct * Heat / 100f;

        public static Build Compute(Profile p, string heroKey, int heat, IEnumerable<Pact> pacts = null, int dailyId = 0)
        {
            var b = new Build { Heat = Loot.ClampHeat(heat) };
            var h = p.Hero(heroKey);
            var rawStats = new Dictionary<Stat, int>();
            var rawPowers = new Dictionary<Power, int>();

            foreach (string uid in h.Equipped)
            {
                var r = p.FindStash(uid);
                if (r == null) continue;
                foreach (var s in r.EffectiveStats()) Add(rawStats, s.Stat, s.Value);
                foreach (var pw in r.EffectivePowers()) Add(rawPowers, pw.Power, pw.Value);
                // 連携（v1.26）：強化では伸びず、覚醒だけが値を掛ける。正しくない定義は無視する。
                var link = r.Link;
                if (link != null)
                {
                    int value = r.Awakened ? (int)((long)link.Value * Content.AwakenPowerPctAt(r.AwakenLevel) / 100) : link.Value;
                    value = Math.Min(global::SodRpg.Core.Game.Links.EquippedCap(link.Kind, link.Requires.Length), value); // ホストの受信と同じ上限
                    var equipped = new LinkDef { Requires = link.Requires, Kind = link.Kind, Value = value };
                    if (global::SodRpg.Core.Game.Links.Validate(equipped)) b.Links.Add(equipped);
                }
                b.Lines.TryGetValue(r.Base.Line, out int n);
                b.Lines[r.Base.Line] = n + 1;
            }
            foreach (var kv in b.Lines)
                foreach (var s in Content.SetBonus(kv.Key, kv.Value)) Add(rawStats, s.Stat, s.Value);
            var setCounts = new Dictionary<string, int>();
            foreach (string uid in h.Equipped)
            {
                var r = p.FindStash(uid);
                if (r?.UniqueId == null || !Content.TryGetUnique(r.UniqueId, out var u) || u.SetId == null) continue;
                setCounts.TryGetValue(u.SetId, out int n);
                setCounts[u.SetId] = n + 1;
            }
            foreach (var kv in setCounts)
            {
                b.Sets[kv.Key] = kv.Value;
                var set = Content.GetSet(kv.Key);
                if (set == null) continue;
                if (kv.Value >= 2) foreach (var s in set.TwoPiece) Add(rawStats, s.Stat, s.Value);
                if (kv.Value >= 3) foreach (var pw in set.ThreePiece) Add(rawPowers, pw.Power, pw.Value);
            }
            bool deepStarsOpen = Rules.Tier1Ranks(h, heroKey) >= Content.DeepStarRequirement;
            foreach (var kv in h.Talents)
            {
                if (!Content.TryGetTalent(kv.Key, out var t) || t.IsKeystone || !Rules.BelongsTo(t, heroKey)) continue;
                if (t.Tier == 2 && !deepStarsOpen) continue;
                int value = t.PerRank * Math.Min(kv.Value, t.MaxRank);
                if (t.IsPowerNode) Add(rawPowers, t.RankPower, value);
                else Add(rawStats, t.Stat, value);
            }
            if (h.Keystone != null && Content.TryGetTalent(h.Keystone, out var key) && key.IsKeystone
                && Rules.BelongsTo(key, heroKey) && Rules.KeystoneUnlocked(p, heroKey, key))
            {
                Add(rawPowers, key.Power, key.PowerValue);
            }

            var daily = DailyDream.Get(dailyId);
            if (daily != null)
            {
                foreach (var pw in daily.BoostedPowers)
                    if (rawPowers.TryGetValue(pw, out int v)) rawPowers[pw] = v + v * DailyDream.PowerBoostPct / 100;
            }
            var pactList = pacts != null ? new List<Pact>(pacts) : new List<Pact>();
            foreach (var id in pactList)
            {
                var d = Game.Pacts.Get(id);
                if (d == null) continue;
                foreach (var s in d.Boons) Add(rawStats, s.Stat, s.Value);
            }
            foreach (var kv in rawStats)
            {
                int cap = Content.StatCap(kv.Key);
                b.Stats[kv.Key] = cap > 0 ? Math.Min(kv.Value, cap) : kv.Value;
            }
            foreach (var kv in rawPowers)
            {
                int cap = Content.PowerCap(kv.Key);
                b.Powers[kv.Key] = cap > 0 ? Math.Min(kv.Value, cap) : kv.Value;
            }
            foreach (var id in pactList)
            {
                var d = Game.Pacts.Get(id);
                if (d == null) continue;
                foreach (var s in d.Penalties) b.Stats[s.Stat] = b.Get(s.Stat) + s.Value;
            }
            return b;
        }

        private static void Add<T>(Dictionary<T, int> d, T key, int v)
        {
            d.TryGetValue(key, out int cur);
            d[key] = cur + v;
        }

        /// <summary>
        /// 通信用の短い文字列表現。"s:0=12,3=4;p:1=4;h:2;l:3=22:St_X+Gem_Y" の形。
        /// l 区間の各要素は「種類:値:条件+条件+条件」（v1.26 の連携）。
        /// ホストはこれを検証してから能力補正へ変換する。
        /// </summary>
        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append("s:");
            bool first = true;
            foreach (var kv in Stats)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append((int)kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";p:");
            first = true;
            foreach (var kv in Powers)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append((int)kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";h:").Append(Heat.ToString(CultureInfo.InvariantCulture));
            sb.Append(";l:");
            first = true;
            foreach (var link in Links)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(((int)link.Kind).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(link.Value.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(string.Join("+", link.Requires));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Encode の逆。未知のIDは捨て、値は上限で切る（他のクライアントから来た値を信用しすぎない）。
        /// 形式が壊れていれば null。
        /// </summary>
        public static Build Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 2000) return null;
            var b = new Build();
            try
            {
                foreach (string part in text.Split(';'))
                {
                    int colon = part.IndexOf(':');
                    if (colon < 0) return null;
                    string kind = part.Substring(0, colon);
                    string body = part.Substring(colon + 1);
                    if (kind == "h")
                    {
                        b.Heat = Loot.ClampHeat(int.Parse(body, NumberStyles.Integer, CultureInfo.InvariantCulture));
                        continue;
                    }
                    if (kind == "l")
                    {
                        if (body.Length == 0) continue;
                        foreach (string entry in body.Split(','))
                        {
                            if (b.Links.Count >= global::SodRpg.Core.Game.Links.MaxLinks) break; // 多すぎる分は切り捨てる
                            int c1 = entry.IndexOf(':');
                            int c2 = c1 < 0 ? -1 : entry.IndexOf(':', c1 + 1);
                            if (c2 < 0) return null;
                            var linkKind = (LinkKind)int.Parse(entry.Substring(0, c1), NumberStyles.Integer, CultureInfo.InvariantCulture);
                            if (linkKind == LinkKind.None || !Enum.IsDefined(typeof(LinkKind), linkKind)) continue;
                            int v = int.Parse(entry.Substring(c1 + 1, c2 - c1 - 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
                            string[] targets = entry.Substring(c2 + 1).Split('+');
                            for (int i = 0; i < targets.Length; i++) targets[i] = global::SodRpg.Core.Game.Links.Canon(targets[i]);
                            var def = new LinkDef
                            {
                                Requires = targets,
                                Kind = linkKind,
                                Value = Math.Max(0, Math.Min(global::SodRpg.Core.Game.Links.EquippedCap(linkKind, targets.Length), v)),
                            };
                            if (global::SodRpg.Core.Game.Links.Validate(def)) b.Links.Add(def);
                        }
                        continue;
                    }
                    if (body.Length == 0) continue;
                    foreach (string pair in body.Split(','))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq < 0) return null;
                        int id = int.Parse(pair.Substring(0, eq), NumberStyles.Integer, CultureInfo.InvariantCulture);
                        int v = int.Parse(pair.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
                        if (kind == "s" && Enum.IsDefined(typeof(Stat), id))
                        {
                            var s = (Stat)id;
                            int cap = Content.StatCap(s);
                            b.Stats[s] = Math.Max(-cap, Math.Min(cap, v));
                        }
                        else if (kind == "p" && Enum.IsDefined(typeof(Power), id) && id != 0)
                        {
                            var pw = (Power)id;
                            b.Powers[pw] = Math.Max(0, Math.Min(Content.PowerCap(pw), v));
                        }
                    }
                }
            }
            catch (FormatException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
            return b;
        }
    }
}
