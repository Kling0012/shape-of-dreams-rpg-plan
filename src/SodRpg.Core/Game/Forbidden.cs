using System;
using System.Collections.Generic;
using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>禁忌化の型（#415）。0 は未禁忌化。値は保存と通信に使うので変えない。</summary>
    public enum ForbiddenPattern
    {
        None = 0,
        /// <summary>禁忌の契約：極端な利点と、はっきりした欠点の能力値を1組足す。</summary>
        Pact = 1,
        /// <summary>断絶：特性を1つ失い、固有効果を1つ得る。</summary>
        Severance = 2,
        /// <summary>過剰：特性を1つ失い、既存の固有効果が大きく強まる。</summary>
        Overcharge = 3,
        /// <summary>破滅の誓い：残る特性が大きく強まり、最大HPが減る。</summary>
        Ruin = 4,
    }

    /// <summary>遺物1つに一度だけ付く禁忌化の結果。元の特性・固有効果は書き換えず、効果の計算時に重ねる。</summary>
    public sealed class ForbiddenMark
    {
        public ForbiddenPattern Pattern { get; set; }
        /// <summary>禁忌の契約の組み合わせ（Forbidden.PactVariants の番号。Forbidden.TransmuteVariant は血の転化）。ほかの型は0。</summary>
        public int Variant { get; set; }
        /// <summary>失った特性の能力値。特性を失わない型（契約・破滅の誓い）では -1。断絶・過剰は特性のある遺物にしか選ばれないので、常に実際の能力値（検証でも実在する特性を要求する）。</summary>
        public int RemovedStat { get; set; } = -1;
        /// <summary>断絶で得た固有効果。ほかの型は None。</summary>
        public Power AddedPower { get; set; }
        public int AddedValue { get; set; }

        public ForbiddenMark Clone() => new ForbiddenMark
        {
            Pattern = Pattern, Variant = Variant, RemovedStat = RemovedStat, AddedPower = AddedPower, AddedValue = AddedValue,
        };

        public bool SameAs(ForbiddenMark other) => other != null
            && Pattern == other.Pattern && Variant == other.Variant && RemovedStat == other.RemovedStat
            && AddedPower == other.AddedPower && AddedValue == other.AddedValue;

        /// <summary>通信用の短い文字列（区切りは「.」）。</summary>
        public string Encode() => string.Join(".", new[]
        {
            ((int)Pattern).ToString(CultureInfo.InvariantCulture), Variant.ToString(CultureInfo.InvariantCulture),
            RemovedStat.ToString(CultureInfo.InvariantCulture), ((int)AddedPower).ToString(CultureInfo.InvariantCulture),
            AddedValue.ToString(CultureInfo.InvariantCulture),
        });

        public static ForbiddenMark Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var f = text.Split('.');
            if (f.Length != 5) throw new FormatException();
            int[] v = new int[5];
            for (int i = 0; i < 5; i++) v[i] = int.Parse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture);
            return new ForbiddenMark { Pattern = (ForbiddenPattern)v[0], Variant = v[1], RemovedStat = v[2], AddedPower = (Power)v[3], AddedValue = v[4] };
        }
    }

    /// <summary>
    /// 禁忌の装備と宿敵（#415）。対象はセットに属さない固有品（オレンジ枠）だけ。
    /// 工房の「禁忌の職人」を解放した後に、禁忌化・宿敵の出現・再調律の上限の引き上げが有効になる。
    /// 数値はレアな装備を手に入りにくくする側で決めた仮の値（実プレイで調整する）。
    /// 調整値は tools/balance/forbidden.json（ForbiddenBalance 生成表。#423）。ここには書かない。
    /// </summary>
    public static class Forbidden
    {
        // ───── 費用と上限 ─────
        /// <summary>禁忌の職人の解放費用（1段）。</summary>
        public static readonly (int Shards, int Tuning)[] SmithCosts = ForbiddenBalance.SmithCosts;
        public const int CoreCost = ForbiddenBalance.ForbidCoreCost;
        public const int ShardCost = ForbiddenBalance.ForbidShardCost;
        public const int TuningCost = ForbiddenBalance.ForbidTuningCost;
        /// <summary>禁忌の職人の解放後、オレンジ枠固有品の再調律の上限に足す回数。</summary>
        public const int OrangeRetuneBonus = ForbiddenBalance.OrangeRetuneBonus;

        // ───── 型ごとの強さ ─────
        /// <summary>過剰：既存の固有効果の倍率（%）。</summary>
        public const int OverchargePowerPct = ForbiddenBalance.OverchargePowerPct;
        /// <summary>過剰：固有効果を持たない遺物では、残る特性に掛ける倍率（%）。</summary>
        public const int OverchargeAffixFallbackPct = ForbiddenBalance.OverchargeAffixFallbackPct;
        /// <summary>破滅の誓い：残る特性の倍率（%）と最大HPの減少。</summary>
        public const int RuinAffixPct = ForbiddenBalance.RuinAffixPct;
        public const int RuinMaxHealthPct = ForbiddenBalance.RuinMaxHealthPct;
        /// <summary>最大HP%の合計の下限。禁忌の契約と破滅の誓いを6部位に重ねても、最大HPが0以下にならないようにする（Build.Compute で適用）。</summary>
        public const int MaxHealthPctFloor = ForbiddenBalance.MaxHealthPctFloor;

        /// <summary>禁忌の契約の組み合わせ（利点・欠点）。値は強化や覚醒で増減しない固定値。tools/balance/forbidden.json。</summary>
        public static readonly (StatLine Boon, StatLine Bane)[] PactVariants = ForbiddenBalance.PactVariants;

        /// <summary>
        /// 禁忌の契約の5つ目の組み合わせ「血の転化」：シールドを一切得られなくなる代わりに、受けた回復量の一部を
        /// 攻撃力へ変換する。能力値の組では表せないので、本体のシールド・回復の処理（ホストの受け取り処理）で適用する。
        /// </summary>
        public const int TransmuteVariant = 4;
        /// <summary>禁忌の契約の組み合わせの総数（能力値の組＋血の転化）。</summary>
        public static int PactVariantCount => PactVariants.Length + 1;
        /// <summary>血の転化：受けた回復のうち、回復せずに攻撃力へ回す割合（%）。</summary>
        public const int TransmuteHealPct = ForbiddenBalance.TransmuteHealPct;
        /// <summary>血の転化：転化した回復量（最大HP比1%）あたりの攻撃力（%）。</summary>
        public const float TransmuteAttackPerHealthPct = ForbiddenBalance.TransmuteAttackPerHealthPct;
        /// <summary>血の転化：攻撃力の上限（%）と、最後の転化からの持続（秒）。</summary>
        public const float TransmuteAttackCap = ForbiddenBalance.TransmuteAttackCap;
        public const float TransmuteDuration = ForbiddenBalance.TransmuteDuration;

        /// <summary>血の転化（シールド不可・回復の一部を攻撃力へ）を持つ禁忌化か。</summary>
        public static bool IsTransmute(Relic r) => r?.Forbidden != null && r.Forbidden.Pattern == ForbiddenPattern.Pact
            && r.Forbidden.Variant == TransmuteVariant;

        // ───── 宿敵 ─────
        /// <summary>ボスとエリート（ミニボス）の宿敵化の確率は別。</summary>
        public const double BossNemesisChance = ForbiddenBalance.BossNemesisChance;
        public const double MiniBossNemesisChance = ForbiddenBalance.MiniBossNemesisChance;
        public const int NemesisBossHealthPct = ForbiddenBalance.NemesisBossHealthPct;
        public const int NemesisBossAttackPct = ForbiddenBalance.NemesisBossAttackPct;
        public const int NemesisMiniBossHealthPct = ForbiddenBalance.NemesisMiniBossHealthPct;
        public const int NemesisMiniBossAttackPct = ForbiddenBalance.NemesisMiniBossAttackPct;
        public const int NemesisArmor = ForbiddenBalance.NemesisArmor;
        /// <summary>宿敵の撃破で得る禁忌の核。禁忌化には宿敵化したボスの撃破が要るので、核はボスだけが落とす（#415）。
        /// エリート（ミニボス）の宿敵は因縁を深め、追加報酬（欠片2倍・調律石・一段上の戦利品）を得られる。</summary>
        public const int BossCores = ForbiddenBalance.NemesisBossCores;
        /// <summary>宿敵の撃破で欠片を上乗せする割合（%）と、追加の調律石。</summary>
        public const int NemesisShardBonusPct = ForbiddenBalance.NemesisShardBonusPct;
        public const int NemesisTuningBonus = ForbiddenBalance.NemesisTuningBonus;

        // ───── 因縁（宿敵「深淵の追跡者」との） ─────
        /// <summary>宿敵の名。ボス・エリートを問わず、禁忌の職人を持つ者を追う同じ存在として現れる。</summary>
        public static string NemesisName => Loc.T("深淵の追跡者", "Abyssal Stalker");
        /// <summary>因縁の上限。宿敵を倒すたびに1つ深まる。</summary>
        public const int MaxGrudge = ForbiddenBalance.MaxGrudge;
        /// <summary>因縁1段ごとの宿敵化の確率の上乗せ（元の確率に対する%）。</summary>
        public const int GrudgeChanceStepPct = ForbiddenBalance.GrudgeChanceStepPct;
        /// <summary>因縁1段ごとの宿敵のHPと攻撃力の上乗せ（%）。</summary>
        public const int GrudgeHealthStepPct = ForbiddenBalance.GrudgeHealthStepPct;
        public const int GrudgeAttackStepPct = ForbiddenBalance.GrudgeAttackStepPct;
        /// <summary>この段以上の因縁で宿敵を倒すと、禁忌の核を1つ多く得る。</summary>
        public const int GrudgeBonusCoreAt = ForbiddenBalance.GrudgeBonusCoreAt;

        public static int ClampGrudge(int grudge) => Math.Max(0, Math.Min(MaxGrudge, grudge));

        /// <summary>因縁の表示（「深淵の追跡者との因縁 Lv2/5」）。</summary>
        public static string GrudgeLabel(int grudge) => Loc.T($"{NemesisName}との因縁 Lv{ClampGrudge(grudge)}/{MaxGrudge}",
            $"Grudge with the {NemesisName}: Lv{ClampGrudge(grudge)}/{MaxGrudge}");

        public static bool HasSmith(Profile p) => p != null && Workshop.Level(p, Upgrade.ForbiddenSmith) > 0;

        /// <summary>セットに属さない固有品（オレンジ枠）。セット品（赤枠）・ボスの技を持つ品は対象外。</summary>
        public static bool IsOrange(Relic r) => r?.UniqueId != null && Content.TryGetUnique(r.UniqueId, out var u)
            && u.SetId == null && u.BossMove == null;

        /// <summary>この遺物の再調律の上限。</summary>
        public static int RetuneCap(Profile p, Relic r) => Content.MaxRetunes + (HasSmith(p) && IsOrange(r) ? OrangeRetuneBonus : 0);

        /// <summary>保存・ホスト検証で受け付ける上限（職人の有無は相手側で分からないので、オレンジ枠は引き上げ後の値）。</summary>
        public static int RetuneCeiling(Relic r) => Content.MaxRetunes + (IsOrange(r) ? OrangeRetuneBonus : 0);

        public static string PatternName(ForbiddenPattern pattern)
        {
            switch (pattern)
            {
                case ForbiddenPattern.Pact: return Loc.T("禁忌の契約", "Forbidden Pact");
                case ForbiddenPattern.Severance: return Loc.T("断絶", "Severance");
                case ForbiddenPattern.Overcharge: return Loc.T("過剰", "Overcharge");
                case ForbiddenPattern.Ruin: return Loc.T("破滅の誓い", "Oath of Ruin");
                default: return "";
            }
        }

        public static string PatternDescription(ForbiddenPattern pattern)
        {
            switch (pattern)
            {
                case ForbiddenPattern.Pact: return Loc.T("極端な利点と、はっきりした欠点の能力が1組付く（シールド不可と回復の攻撃力への転化を含む）。", "Adds one extreme boon paired with a clear drawback (including no shields with healing turned into attack).");
                case ForbiddenPattern.Severance: return Loc.T("特性を1つ失い、固有効果を1つ得る。", "Loses one affix and gains one power.");
                case ForbiddenPattern.Overcharge: return Loc.T($"特性を1つ失い、固有効果が{OverchargePowerPct / 100m:0.##}倍になる。", $"Loses one affix; powers become x{OverchargePowerPct / 100m:0.##}.");
                case ForbiddenPattern.Ruin: return Loc.T($"特性が{RuinAffixPct / 100m:0.##}倍になる代わりに、最大HP{RuinMaxHealthPct}%。", $"Affixes become x{RuinAffixPct / 100m:0.##}, but max HP {RuinMaxHealthPct}%.");
                default: return "";
            }
        }

        /// <summary>禁忌化した遺物の説明（効果の一覧に添える1〜2行）。</summary>
        public static string Describe(Relic r)
        {
            var m = r?.Forbidden;
            if (m == null) return "";
            string text = Loc.T("禁忌化：", "Forbidden: ") + PatternName(m.Pattern);
            if (IsTransmute(r))
                text += Loc.T($"（血の転化：シールドを得られず、受けた回復の{TransmuteHealPct}%を攻撃力に変換）",
                    $" (Blood Transmutation: cannot gain shields; {TransmuteHealPct}% of healing received becomes attack)");
            int removed = RemovedAffixIndex(r);
            if (removed >= 0)
            {
                string lost = Content.FormatStat(r.Affixes[removed].Stat, r.Affixes[removed].Value);
                text += Loc.T($"（失った特性：{lost}）", $" (lost affix: {lost})");
            }
            return text;
        }

        // ───── 効果の計算 ─────

        /// <summary>禁忌化を反映した、強化・覚醒前の固有効果。未禁忌化なら元の一覧をそのまま返す（確保なし）。</summary>
        public static IReadOnlyList<PowerLine> SourcePowers(Relic r)
        {
            var m = r.Forbidden;
            if (m == null || (m.Pattern != ForbiddenPattern.Severance && m.Pattern != ForbiddenPattern.Overcharge)) return r.Powers;
            var list = new List<PowerLine>(r.Powers.Count + 1);
            foreach (var pw in r.Powers)
                list.Add(m.Pattern == ForbiddenPattern.Overcharge ? new PowerLine(pw.Power, Relic.Scale(pw.Value, OverchargePowerPct)) : pw);
            if (m.Pattern == ForbiddenPattern.Severance && m.AddedPower != Power.None && m.AddedValue > 0)
                list.Add(new PowerLine(m.AddedPower, m.AddedValue));
            return list;
        }

        /// <summary>特性を残すか（失った特性は計算から外す）。同じ能力値の特性が複数あれば、最初の1つだけ外す。</summary>
        internal static int RemovedAffixIndex(Relic r)
        {
            var m = r.Forbidden;
            if (m == null || m.RemovedStat < 0) return -1;
            for (int i = 0; i < r.Affixes.Count; i++)
                if ((int)r.Affixes[i].Stat == m.RemovedStat) return i;
            return -1;
        }

        /// <summary>
        /// 特性に掛ける倍率（%）。過剰の振り分け先は禁忌化した時点の状態に固定（#423）：固有効果の無い遺物は
        /// 特性に 1.4倍、ある遺物は固有効果 1.6倍。禁忌化した遺物には強化の節目でも固有効果が宿らず
        /// （代わりに特性が増える。Rules.GrantEnhanceMilestones）、固有効果を足す・替える夢の出来事の対象にも
        /// ならないので、後から倍率の対象（Powers の有無）が変わることはない。
        /// </summary>
        internal static int AffixPct(Relic r)
        {
            var m = r.Forbidden;
            if (m == null) return 100;
            if (m.Pattern == ForbiddenPattern.Ruin) return RuinAffixPct;
            if (m.Pattern == ForbiddenPattern.Overcharge && r.Powers.Count == 0) return OverchargeAffixFallbackPct;
            return 100;
        }

        /// <summary>禁忌化で足す固定の能力値（強化・覚醒の倍率を掛けない）。</summary>
        internal static IEnumerable<StatLine> ExtraStats(Relic r)
        {
            var m = r.Forbidden;
            if (m == null) yield break;
            if (m.Pattern == ForbiddenPattern.Pact && m.Variant >= 0 && m.Variant < PactVariants.Length)
            {
                yield return PactVariants[m.Variant].Boon;
                yield return PactVariants[m.Variant].Bane;
            }
            else if (m.Pattern == ForbiddenPattern.Ruin) yield return new StatLine(Stat.MaxHealthPct, RuinMaxHealthPct);
        }

        // ───── 判定と抽選 ─────

        /// <summary>禁忌化できない理由。できるなら null。</summary>
        public static string CannotForbid(Profile p, Relic r)
        {
            if (!HasSmith(p)) return Loc.T("工房で「禁忌の職人」を解放すると使えます。", "Unlock the Forbidden Smith in the workshop first.");
            if (r == null) return Loc.T("保管庫にない遺物です。", "That relic is not in your stash.");
            if (!IsOrange(r)) return Loc.T("禁忌化できるのは、セットに属さない固有品（オレンジ枠）だけです。", "Only non-set uniques (orange frame) can be made forbidden.");
            if (r.Forbidden != null) return Loc.T("この遺物はすでに禁忌化しています（1つの遺物に1回だけ）。", "This relic is already forbidden (once per relic).");
            if (p.Material(Materials.NemesisCore) < CoreCost || p.Material(Materials.Shard) < ShardCost || p.Material(Materials.Tuning) < TuningCost)
                return Loc.T($"素材が足りません（禁忌の核{CoreCost}・欠片{ShardCost}・調律石{TuningCost}）。禁忌の核は宿敵化したボスを倒すと手に入ります。",
                    $"Not enough materials ({CoreCost} forbidden cores, {ShardCost} shards, {TuningCost} tuning). Forbidden cores drop from nemesis bosses.");
            return null;
        }

        /// <summary>
        /// 4つの型から等確率で1つを選び、結果を作る。遺物は書き換えない。
        /// 適用条件と例外（#423）：特性を1つ失う型（断絶・過剰）は、特性の無い遺物では選ばれない
        /// （失う特性が無ければ「特性を1つ失う」という必須の欠点が成立せず、印の検証（RemovedValid は
        /// 実在する特性を要求する）にも落ちる）。この場合は等確率で契約か破滅の誓いへ振り替わる。
        /// 断絶は、その部位の池から「まだ持っていない固有効果」を1つ足す型なので、足せる効果が
        /// 無ければ（池の効果をすべて持っている、または池が空なら）過剰へ振り替わる。
        /// </summary>
        public static ForbiddenMark Roll(Rng rng, Relic r)
        {
            var pattern = (ForbiddenPattern)rng.Range(1, 4);
            // 特性を1つ失う型（断絶・過剰）は、特性が無い遺物では選ばない。失う特性が無ければ「特性を1つ失う」
            // という必須の欠点が成立せず、印の検証（RemovedValid は実在する特性を要求する）にも落ちるので、
            // 特性を失わない型へ振り替える。
            if (r.Affixes.Count == 0 && (pattern == ForbiddenPattern.Severance || pattern == ForbiddenPattern.Overcharge))
                pattern = rng.Range(0, 1) == 0 ? ForbiddenPattern.Pact : ForbiddenPattern.Ruin;
            // 断絶は足せる固有効果が無ければ過剰に替える。
            List<PowerRange> candidates = null;
            if (pattern == ForbiddenPattern.Severance)
            {
                candidates = SeveranceCandidates(r);
                if (candidates.Count == 0) pattern = ForbiddenPattern.Overcharge;
            }
            var m = new ForbiddenMark { Pattern = pattern };
            switch (pattern)
            {
                case ForbiddenPattern.Pact:
                    m.Variant = rng.Range(0, PactVariantCount - 1);
                    break;
                case ForbiddenPattern.Severance:
                    {
                        var def = candidates[rng.Range(0, candidates.Count - 1)];
                        m.AddedPower = def.Power;
                        SeveranceBounds(def, out int low, out int high);
                        m.AddedValue = Math.Max(1, rng.Range(low, high));
                        if (r.Affixes.Count > 0) m.RemovedStat = (int)r.Affixes[rng.Range(0, r.Affixes.Count - 1)].Stat;
                        break;
                    }
                case ForbiddenPattern.Overcharge:
                    if (r.Affixes.Count > 0) m.RemovedStat = (int)r.Affixes[rng.Range(0, r.Affixes.Count - 1)].Stat;
                    break;
            }
            return m;
        }

        /// <summary>断絶で足せる固有効果（その部位の池から、まだ持っていないもの）。</summary>
        public static List<PowerRange> SeveranceCandidates(Relic r)
        {
            var list = new List<PowerRange>();
            foreach (var def in Content.PowerPool(r.Slot))
            {
                if (!Content.PowerAllowedForRarity(def.Power, r.Rarity) || def.Max <= 0) continue;
                bool owned = false;
                foreach (var pw in r.Powers) if (pw.Power == def.Power) { owned = true; break; }
                if (!owned) list.Add(def);
            }
            return list;
        }

        /// <summary>断絶の固有効果の値の抽選範囲（両端を含む。定義範囲の上半分）。Roll と Valid で同じ範囲を使うための共用。</summary>
        private static void SeveranceBounds(PowerRange def, out int low, out int high)
        {
            low = Math.Max(def.Min, def.Min + (def.Max - def.Min) / 2);
            high = Math.Max(low, def.Max);
        }

        /// <summary>ホストが受け取った禁忌化の結果を検証する（作れない組み合わせは拒否）。</summary>
        public static bool Valid(Relic r, ForbiddenMark m)
        {
            if (m == null) return true;
            if (!IsOrange(r)) return false;
            switch (m.Pattern)
            {
                case ForbiddenPattern.Pact:
                    return m.Variant >= 0 && m.Variant < PactVariantCount && m.RemovedStat == -1
                        && m.AddedPower == Power.None && m.AddedValue == 0;
                case ForbiddenPattern.Ruin:
                    return m.Variant == 0 && m.RemovedStat == -1 && m.AddedPower == Power.None && m.AddedValue == 0;
                case ForbiddenPattern.Overcharge:
                    return m.Variant == 0 && RemovedValid(r, m) && m.AddedPower == Power.None && m.AddedValue == 0;
                case ForbiddenPattern.Severance:
                    if (m.Variant != 0 || !RemovedValid(r, m)) return false;
                    // 検証では AddedPower 自身も候補に含める（#423）。SeveranceCandidates は「新しく足す効果」を
                    // 選ぶ Roll の候補なので所持済みを除くが、検証を同じ集合に対照すると、禁忌化の後に同じ
                    // 固有効果が遺物に宿ったとき（現在の経路は禁忌化した遺物を除外しているが、旧保存や将来の
                    // 変更に備える）に、費用を払った正規の印が読み込み時・ホスト検証時に黙って捨てられる。
                    // 効果がその部位の池に定義され、抽選範囲内の値であることだけを要求する。
                    foreach (var def in Content.PowerPool(r.Slot))
                        if (def.Power == m.AddedPower && Content.PowerAllowedForRarity(def.Power, r.Rarity) && def.Max > 0)
                        {
                            // 通常の抽選で出ない値（定義範囲の下半分など）は作れない組み合わせとして拒否する。
                            SeveranceBounds(def, out int low, out int high);
                            return m.AddedValue >= Math.Max(1, low) && m.AddedValue <= Math.Max(1, high);
                        }
                    return false;
                default:
                    return false;
            }
        }

        private static bool RemovedValid(Relic r, ForbiddenMark m)
        {
            // 断絶・過剰は「特性を1つ失う」ことが必須（#415）。失った特性の能力値として、今も実在する
            // 特性を要求する。-1（失った特性が無い）は作れない印なので拒否する。Roll は特性の無い遺物に
            // この2型を選ばず、禁忌化した遺物の特性は減る経路が無い（再調律・洗い直し・特性を変える夢の
            // 出来事から外れている。強化の節目では増えるだけ）ので、正規に生成された印がこの判定で
            // 落ちることはない。
            if (m.RemovedStat < 0) return false;
            foreach (var a in r.Affixes) if ((int)a.Stat == m.RemovedStat) return true;
            return false;
        }

        /// <summary>宿敵化の確率。ボスとエリートで別。ほかの格は宿敵にならない。</summary>
        public static double NemesisChance(MonsterTier tier)
        {
            switch (tier)
            {
                case MonsterTier.Boss: return BossNemesisChance;
                case MonsterTier.MiniBoss: return MiniBossNemesisChance;
                default: return 0;
            }
        }

        /// <summary>宿敵化の抽選。パーティの誰かが禁忌の職人を解放しているときだけ起きる。因縁（パーティで最も深いもの）が深いほど現れやすい。</summary>
        public static bool RollNemesis(Rng rng, MonsterTier tier, bool anySmith, int grudge = 0)
        {
            double chance = NemesisChance(tier) * (100 + GrudgeChanceStepPct * ClampGrudge(grudge)) / 100.0;
            return anySmith && chance > 0 && rng.Chance(chance);
        }

        /// <summary>宿敵へ付ける能力（表示単位）。</summary>
        public static List<StatLine> NemesisStats(MonsterTier tier, int grudge = 0)
        {
            bool boss = tier >= MonsterTier.Boss;
            int g = ClampGrudge(grudge);
            return new List<StatLine>
            {
                new StatLine(Stat.MaxHealthPct, (boss ? NemesisBossHealthPct : NemesisMiniBossHealthPct) + GrudgeHealthStepPct * g),
                new StatLine(Stat.AttackPct, (boss ? NemesisBossAttackPct : NemesisMiniBossAttackPct) + GrudgeAttackStepPct * g),
                new StatLine(Stat.Armor, NemesisArmor),
            };
        }

        /// <summary>宿敵になれる格（ボスとエリート）。この格だけ宿敵化の抽選と撃破報酬がある。</summary>
        public static bool NemesisEligible(MonsterTier tier) => tier == MonsterTier.Boss || tier == MonsterTier.MiniBoss;

        /// <summary>宿敵の撃破で得る禁忌の核（因縁が深ければ1つ多い）。核を落とすのは宿敵化したボスだけ（#415）。</summary>
        public static int NemesisCores(MonsterTier tier, int grudge)
        {
            int cores = NemesisCores(tier);
            return cores > 0 && ClampGrudge(grudge) >= GrudgeBonusCoreAt ? cores + 1 : cores;
        }

        public static int NemesisCores(MonsterTier tier) => tier == MonsterTier.Boss ? BossCores : 0;
    }
}
