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
        /// <summary>遺物と星の連携。装着条件はホストで判定し、常時の能力値には加えない。</summary>
        public List<LinkDef> Links { get; } = new List<LinkDef>();
        /// <summary>振ったルートの星が持つ、記憶に反応する仕掛け。</summary>
        public List<GimmickEntry> Gimmicks { get; } = new List<GimmickEntry>();
        /// <summary>橋と両隣の星を取得した合わせ技。記憶の装備条件は各イベントで判定する。</summary>
        public List<PairComboEntry> PairCombos { get; } = new List<PairComboEntry>();
        public int Heat { get; set; }
        /// <summary>夢の圧へ送る進行度。欠けている旧データは夢1・星0。</summary>
        public int DreamLevel { get; set; } = 1;
        public int SpentStarPoints { get; set; }

        public int Get(Stat s) => Stats.TryGetValue(s, out int v) ? v : 0;
        public int Get(Power p) => Powers.TryGetValue(p, out int v) ? v : 0;

        /// <summary>潜行1段ごとの被ダメージ増加（%）。本体 Limbo の「被ダメージ増加」と同じ表現。</summary>
        public const int DamageTakenPerDelvePct = 6;

        /// <summary>被ダメージの倍率（1.0 = 増減なし）。</summary>
        public float DamageTakenMultiplier => 1f + DamageTakenPerDelvePct * Heat / 100f;

        public static Build Compute(Profile p, string heroKey, int heat, IEnumerable<Pact> pacts = null, int dailyId = 0)
        {
            var h = p.Hero(heroKey);
            var b = new Build
            {
                Heat = Loot.ClampHeat(heat),
                DreamLevel = Math.Max(1, Math.Min(Content.MaxDreamLevel, p.DreamLevel)),
                SpentStarPoints = Math.Max(0, Math.Min(StarProgression.MaxPoints, Rules.SpentPoints(h))),
            };
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
                    long scaled = r.Awakened ? (long)link.Value * Content.AwakenPowerPctAt(r.AwakenLevel) / 100 : link.Value;
                    int value = (int)Math.Max(0, Math.Min(global::SodRpg.Core.Game.Links.EquippedCap(link.Kind, link.Requires.Length), scaled));
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
            foreach (var kv in h.Talents)
            {
                if (kv.Value <= 0 || !Content.TryGetTalent(kv.Key, out var t) || t.IsKeystone
                    || !Rules.TalentUnlocked(h, heroKey, t)) continue;
                int rank = Math.Min(kv.Value, t.MaxRank);
                var pair = global::SodRpg.Core.Game.PairCombos.ForBridge(t.Id);
                if (pair != null)
                {
                    var entry = global::SodRpg.Core.Game.PairCombos.Activate(pair, h, rank);
                    if (entry != null && Content.TryGetTalent(pair.StarA, out var starA)
                        && Content.TryGetTalent(pair.StarB, out var starB)
                        && Rules.TalentUnlocked(h, heroKey, starA) && Rules.TalentUnlocked(h, heroKey, starB))
                        b.PairCombos.Add(entry);
                    continue; // Inner bridges are combos, never their old unconditional ring stats.
                }
                if (t.Gimmick != null && t.Gimmick.Value > 0 && b.Gimmicks.Count < global::SodRpg.Core.Game.Gimmicks.MaxEntries)
                {
                    var entry = global::SodRpg.Core.Game.Gimmicks.Clamp(new GimmickEntry
                    {
                        StarId = t.Id,
                        Memory = t.RouteMemory,
                        Def = new GimmickDef
                        {
                            Trigger = t.Gimmick.Trigger,
                            Effect = t.Gimmick.Effect,
                            Value = (int)Math.Min(int.MaxValue, (long)t.Gimmick.Value * rank),
                            Arg = t.Gimmick.Arg,
                            Cooldown = t.Gimmick.Cooldown,
                        },
                    });
                    if (entry != null) b.Gimmicks.Add(entry);
                }
                if (t.LinkPerRank != null)
                {
                    var link = new LinkDef
                    {
                        Requires = t.LinkPerRank.Requires,
                        Kind = t.LinkPerRank.Kind,
                        Value = t.LinkPerRank.Value * rank,
                    };
                    if (global::SodRpg.Core.Game.Links.Validate(link)) b.Links.Add(link);
                }
                else if (t.IsPowerNode) Add(rawPowers, t.RankPower, t.PerRank * rank);
                else Add(rawStats, t.Stat, t.PerRank * rank);
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
        /// 通信用の短い文字列表現。"s:0=12,3=4;p:1=4;h:2;d:30;a:150;l:3:22:St_X+Gem_Y" の形。
        /// d は夢のレベル、a は使用済み星ポイント。l は「種類:値:条件+条件+条件」。
        /// c は合わせ技の「ID:段数」。効果は正規の定義から復元し、クライアントからの効果量は受け取らない。
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
            sb.Append(";d:").Append(DreamLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(";a:").Append(SpentStarPoints.ToString(CultureInfo.InvariantCulture));
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
            sb.Append(";g:");
            first = true;
            var stars = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (var raw in Gimmicks)
            {
                if (count >= global::SodRpg.Core.Game.Gimmicks.MaxEntries) break;
                var entry = global::SodRpg.Core.Game.Gimmicks.Clamp(raw);
                if (entry == null || !stars.Add(entry.StarId)) continue;
                if (!first) sb.Append(',');
                first = false;
                count++;
                sb.Append(entry.StarId).Append(':').Append(entry.Memory).Append(':')
                    .Append(((int)entry.Def.Trigger).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(((int)entry.Def.Effect).ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Def.Value.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Def.Arg.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(entry.Def.Cooldown.ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append(";c:");
            first = true;
            stars.Clear();
            count = 0;
            foreach (var raw in PairCombos)
            {
                if (count >= global::SodRpg.Core.Game.PairCombos.MaxEntries) break;
                var entry = global::SodRpg.Core.Game.PairCombos.Clamp(raw);
                if (entry == null || !stars.Add(entry.Def.Id)) continue;
                if (!first) sb.Append(',');
                first = false;
                count++;
                sb.Append(entry.Def.Id).Append(':').Append(entry.Ranks.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Encode の逆。未知のIDは捨て、値は上限で切る（他のクライアントから来た値を信用しすぎない）。
        /// 形式が壊れていれば null。
        /// </summary>
        public static Build Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 16384) return null;
            var b = new Build();
            var stars = new HashSet<string>(StringComparer.Ordinal);
            var pairs = new HashSet<string>(StringComparer.Ordinal);
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
                    if (kind == "d")
                    {
                        b.DreamLevel = Math.Max(1, Math.Min(Content.MaxDreamLevel, int.Parse(body, NumberStyles.Integer, CultureInfo.InvariantCulture)));
                        continue;
                    }
                    if (kind == "a")
                    {
                        b.SpentStarPoints = Math.Max(0, Math.Min(StarProgression.MaxPoints, int.Parse(body, NumberStyles.Integer, CultureInfo.InvariantCulture)));
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
                    if (kind == "g")
                    {
                        if (body.Length == 0) continue;
                        foreach (string encoded in body.Split(','))
                        {
                            if (b.Gimmicks.Count >= global::SodRpg.Core.Game.Gimmicks.MaxEntries) break;
                            string[] fields = encoded.Split(':');
                            if (fields.Length != 7
                                || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int trigger)
                                || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int effect)
                                || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                                || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int arg)
                                || !float.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float cooldown)) continue;
                            var entry = global::SodRpg.Core.Game.Gimmicks.Clamp(new GimmickEntry
                            {
                                StarId = fields[0],
                                Memory = fields[1],
                                Def = new GimmickDef
                                {
                                    Trigger = (GimmickTrigger)trigger,
                                    Effect = (GimmickEffect)effect,
                                    Value = value,
                                    Arg = arg,
                                    Cooldown = cooldown,
                                },
                            });
                            if (entry != null && stars.Add(entry.StarId)) b.Gimmicks.Add(entry);
                        }
                        continue;
                    }
                    if (kind == "c")
                    {
                        if (body.Length == 0) continue;
                        foreach (string encoded in body.Split(','))
                        {
                            if (b.PairCombos.Count >= global::SodRpg.Core.Game.PairCombos.MaxEntries) break;
                            string[] fields = encoded.Split(':');
                            if (fields.Length != 2 || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ranks)) continue;
                            var entry = global::SodRpg.Core.Game.PairCombos.Clamp(new PairComboEntry
                            {
                                Def = global::SodRpg.Core.Game.PairCombos.Get(fields[0]), Ranks = ranks
                            });
                            if (entry != null && pairs.Add(entry.Def.Id)) b.PairCombos.Add(entry);
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
