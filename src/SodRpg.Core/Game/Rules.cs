using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SodRpg.Core.Game
{
    public enum EventKind
    {
        Info,
        Drop,
        LevelUp,
        Secured,
        Delved,
        Lost,
        Recovered,
        Warning,
        Bounty,
        Hint,
    }

    /// <summary>画面に流す通知。文言は発生時の表示言語で作る。</summary>
    public sealed class GameEvent
    {
        public GameEvent(EventKind kind, string text, Rarity? rarity = null)
        {
            Kind = kind;
            Text = text;
            Rarity = rarity;
        }

        public EventKind Kind { get; }
        public string Text { get; }
        public Rarity? Rarity { get; }
        public Relic Relic { get; internal set; }
        /// <summary>Removed overflow relic, already converted into profile shards; null for ordinary notifications.</summary>
        public Relic SatchelOverflow { get; internal set; }
        public int SatchelOverflowShards { get; internal set; }
        /// <summary>Kind が Hint のときのヒント。</summary>
        public Hint? HintId { get; set; }
        /// <summary>単独の結果に付随する依頼報酬・レベルアップの通知。</summary>
        public IReadOnlyList<GameEvent> AdditionalEvents { get; internal set; }

        public override string ToString() => Text;
    }

    /// <summary>
    /// プロフィールに対する操作（遠征・鍛冶・装着・専門化）。すべて純粋なデータ操作で、
    /// 失敗時は InvalidOperationException を投げ、プロフィールを変更しない。
    /// </summary>
    public static partial class Rules
    {
        /// <summary>まだ見ていないヒントなら通知に加える。</summary>
        internal static void AddHint(Profile p, Hint h, List<GameEvent> ev)
        {
            if (!Onboarding.Show(p, h)) return;
            var d = Onboarding.Get(h);
            ev.Add(new GameEvent(EventKind.Hint, d?.Title.ToString() ?? h.ToString()) { HintId = h });
        }

        /// <summary>ヒントを1つだけ出す（他の操作に付随しない場面用）。</summary>
        public static List<GameEvent> HintOnce(Profile p, Hint h)
        {
            var ev = new List<GameEvent>();
            AddHint(p, h, ev);
            return ev;
        }

        /// <summary>達成済みの偉業の報酬を1回だけ受け取る。遠征中も受け取れる。</summary>
        public static GameEvent ClaimFeat(Profile p, string featId)
        {
            FeatDef feat = null;
            for (int i = 0; i < Feats.All.Count; i++)
            {
                if (Feats.All[i].Id != featId) continue;
                feat = Feats.All[i];
                break;
            }
            if (feat == null) throw new InvalidOperationException(Loc.T("その偉業は見つかりません。", "That feat does not exist."));
            if (!p.Feats.Contains(featId)) throw new InvalidOperationException(Loc.T("この偉業はまだ達成していません。", "You have not completed this feat yet."));
            if (p.FeatsClaimed.Contains(featId)) throw new InvalidOperationException(Loc.T("この偉業の報酬は、もう受け取っています。", "You already claimed this reward."));

            p.AddMaterial(Materials.Shard, feat.RewardShards);
            p.AddMaterial(Materials.Tuning, feat.RewardTuning);
            p.FeatsClaimed.Add(featId);
            string ja = feat.RewardTuning > 0 ? $"欠片{feat.RewardShards}と調律石{feat.RewardTuning}" : $"欠片{feat.RewardShards}";
            string en = feat.RewardTuning > 0 ? $"{feat.RewardShards} shards and {feat.RewardTuning} tuning" : $"{feat.RewardShards} shards";
            return new GameEvent(EventKind.Info, Loc.T($"偉業「{feat.Name}」の報酬として{ja}を受け取りました。", $"Claimed \"{feat.Name}\": {en}."));
        }

        // ───────────── 遠征 ─────────────

        /// <summary>
        /// ランの開始（または再開）。別のランIDの未解決ランが残っていれば、全滅と同じ扱いで精算する
        /// （途中で終了したランの未確保品は遺失物へ）。
        /// </summary>
        /// <summary>Limbo 深度1ごとの遺物ドロップ率の上乗せと幸運。</summary>
        public const double LimboDropBonus = 0.10;
        public const double LimboLuck = 0.2;

        public static List<GameEvent> BeginRun(Profile p, string runId, DailyDream daily = null, int limboDepth = 0, ISet<string> reservedUids = null, string heroKey = null, int? dreamDepth = null)
        {
            var ev = new List<GameEvent>();
            if (string.IsNullOrEmpty(runId)) runId = "unknown";
            if (runId == p.CompletedRunId) return ev;
            if (p.Run != null && p.Run.RunId == runId)
            {
                if (string.IsNullOrEmpty(p.Run.HeroKey) && !string.IsNullOrEmpty(heroKey)) p.Run.HeroKey = heroKey;
                return ev;
            }
            if (p.Run != null)
            {
                ev.Add(new GameEvent(EventKind.Warning, Loc.T("前回の遠征は確保されずに終わりました。", "Your previous expedition ended unsecured.")));
                ev.AddRange(EndRun(p, victory: false, reservedUids: reservedUids));
            }
            // 開始深度は v1.1 で廃止（本体の Limbo 深度に統合）。
            p.Run = new RunState { RunId = runId, HeroKey = heroKey, LevelAtStart = p.DreamLevel, DailyId = daily?.Id ?? 0, LimboDepth = Math.Max(0, limboDepth), DreamDepth = dreamDepth ?? p.LastDreamDepth };
            if (limboDepth > 0)
            {
                ev.Add(new GameEvent(EventKind.Info, Loc.T(
                    $"Limboの深さ{limboDepth}：遺物が{(int)(LimboDropBonus * 100 * limboDepth)}%出やすくなり、レア度も上がります。",
                    $"Limbo depth {limboDepth}: +{(int)(LimboDropBonus * 100 * limboDepth)}% relic drops, better rarity")));
            }
            if (daily != null) ev.Add(new GameEvent(EventKind.Info, Loc.T($"今日の夢「{daily.Name}」：{daily.Description}", $"Today's dream \"{daily.Name}\": {daily.Description}")));
            var brng = p.TakeRng();
            p.Run.Bounties.AddRange(Bounties.Roll(brng));
            p.StoreRng(brng);
            p.Stats.Runs++;
            if (p.LostAndFound.Count > 0)
            {
                ev.Add(new GameEvent(EventKind.Info, Loc.T(
                    $"遺失物が{p.LostAndFound.Count}個あります。戦闘部屋を{Workshop.RoomsToRecover(p)}つ突破すると1つ取り戻せます。",
                    $"You have {p.LostAndFound.Count} lost relic(s). Clear {Workshop.RoomsToRecover(p)} combat rooms to recover one.")));
            }
            return ev;
        }

        /// <summary>契約・出来事・Limbo・今日の夢を合わせた撃破報酬の補正。waypoint は戦ったときの道標（#71）。</summary>
        public static Pacts.Totals KillModifiers(RunState run, Waypoint? waypoint = null)
        {
            var t = Pacts.Sum(run.Pacts);
            t.DropBonus += LimboDropBonus * run.LimboDepth + run.EventDropBonus;
            t.Luck += LimboLuck * run.LimboDepth + run.EventLuck;
            t.Luck += DreamDepth.RarityLuck(run.DreamDepth) + Waypoints.Sum(waypoint ?? run.ActiveWaypoint).Luck;
            var d = DailyDream.Get(run.DailyId);
            if (d != null)
            {
                t.DropBonus += d.DropBonus;
                t.ShardMult *= d.ShardMult;
                t.XpMult *= d.XpMult;
            }
            return t;
        }

        /// <summary>撃破報酬。heat/waypoint は戦ったときの値（保留していた撃破の精算用、#71）。省略時は現在の状態。</summary>
        public static List<GameEvent> OnKill(Profile p, MonsterTier tier, int itemLevel, NightmareAffix nightmare = NightmareAffix.None, string heroKey = null, TradeLedger trades = null, string variantId = null, int? roomIndex = null, int? heat = null, Waypoint? waypoint = null,
            string bossTypeName = null, bool bossDropNightmare = false, int bossDropDepth = 0)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            if (string.IsNullOrEmpty(heroKey)) heroKey = run.HeroKey;
            var rng = p.TakeRng();
            var variant = Variants.Get(variantId);
            // 夢の変種は、悪夢と同じく一段上の戦利品・覚醒の力2倍・悪夢の依頼に数える。
            bool isNightmare = nightmare != NightmareAffix.None || variant != null;
            var rollTier = isNightmare ? Nightmares.RewardTier(tier) : tier;
            int killHeat = heat ?? run.Heat;
            Waypoint killWaypoint = waypoint ?? run.ActiveWaypoint;
            var focus = p.Focus ?? DailyDream.Get(run.DailyId)?.FeaturedLine;
            bool admitted = InfinityRewards.AdmitKill(p, tier, rollTier, killHeat, killWaypoint, isNightmare, bossTypeName, bossDropNightmare, bossDropDepth);
            var reward = admitted ? Loot.RollKill(rng, rollTier, itemLevel, killHeat, focus, KillModifiers(run, killWaypoint), p.Stash, run.Satchel, p.Codex) : new KillReward();
            if (admitted && variant != null && variant.ShardBonusPct != 100) reward.Shards = (int)Math.Min(int.MaxValue, (long)reward.Shards * variant.ShardBonusPct / 100 + 10);
            bool hoardPayout = killWaypoint == Waypoint.BossHoard
                && !run.WaypointHoardReleased && tier == MonsterTier.Boss;
            Waypoints.ApplyKill(p, tier, isNightmare, rng, reward, itemLevel, focus, roomIndex ?? run.RoomsCleared, killWaypoint, out int waypointStarXp, out int waypointAwakening, admitted);
            if (tier == MonsterTier.Boss && admitted)
            {
                var bossPiece = BossSets.RollDrop(rng, bossTypeName, bossDropNightmare, bossDropDepth, itemLevel);
                if (bossPiece != null)
                {
                    if (!InfinityRewards.Active(p) || p.InfinityRewardBudget.Relics + 1e-9 >= 1)
                    {
                        if (InfinityRewards.Active(p)) { p.InfinityRewardBudget.Relics = Math.Max(0, p.InfinityRewardBudget.Relics - 1); bossPiece.InfinityFreeSupply = true; }
                        reward.Relics.Add(bossPiece);
                    }
                }
            }
            if (!string.IsNullOrEmpty(heroKey))
            {
                var hs = p.Hero(heroKey);
                if (string.IsNullOrEmpty(run.HeroKey)) run.HeroKey = heroKey;
                AddStarXp(p, heroKey, admitted ? (int)Math.Min(int.MaxValue, (long)StarProgression.KillXp(tier, isNightmare) + waypointStarXp) : 0, ev);
                int points = DreamDepth.ScaleReward((admitted ? Content.AwakenPoints(tier, isNightmare) : 0) + waypointAwakening,
                    DreamDepth.AwakeningMultiplier(run.DreamDepth) * Waypoints.Sum(killWaypoint).AwakeningMultiplier);
                foreach (var uid in hs.Equipped)
                {
                    var r = p.FindStash(uid);
                    if (r == null || r.Rarity != Rarity.Legendary) continue;
                    ReachBounty(p, BountyKind.Awakener, r.AwakenLevel, false, ev);
                    if (r.AwakenLevel >= Content.MaxAwakenLevel) continue;
                    int grantedPoints = InfinityRewards.LimitAwakening(p, points);
                    r.AwakenPoints = (int)Math.Min(Content.AwakenThreshold, (long)r.AwakenPoints + grantedPoints);
                    int level = Content.AwakenLevelFor(r.AwakenPoints);
                    if (level <= r.AwakenLevel) continue;
                    if (r.AwakenLevel == 0) p.Stats.RelicsAwakened = SaturatingAdd(p.Stats.RelicsAwakened, 1);
                    r.AwakenLevel = level;
                    ReachBounty(p, BountyKind.Awakener, level, false, ev);
                    string powerMult = (Content.AwakenPowerPctAt(level) / 100m).ToString("0.##", CultureInfo.InvariantCulture);
                    string affixMult = (Content.AwakenAffixPctAt(level) / 100m).ToString("0.##", CultureInfo.InvariantCulture);
                    string numeral = Content.AwakenNumeral(level);
                    ev.Add(new GameEvent(EventKind.LevelUp, Loc.T(
                        $"「{r.PlainName}」が覚醒{numeral}になりました！固有効果が{powerMult}倍、特性が{affixMult}倍になります。",
                        $"\"{r.PlainName}\" reached Awakening {numeral}! Powers x{powerMult}, affixes x{affixMult}.")));
                }
                int before = Mastery.Level(hs.Kills);
                if (hs.Kills < int.MaxValue) hs.Kills++;
                int after = Mastery.Level(hs.Kills);
                if (after > before && after == HeroSigils.KeystoneMastery && HeroSigils.HasTree(heroKey)) AddHint(p, Hint.KeystoneReady, ev);
                if (after > before)
                {
                    string name = HeroNames.Display(heroKey);
                    ev.Add(new GameEvent(EventKind.LevelUp, Loc.T(
                        $"{name}の熟練度が{after}「{Mastery.Title(after)}」に上がりました。" + (after == HeroSigils.KeystoneMastery && HeroSigils.HasTree(heroKey) ? "到達刻印を選べるようになりました。" : ""),
                        $"{name} mastery {after} \"{Mastery.Title(after)}\"" + (after == HeroSigils.KeystoneMastery && HeroSigils.HasTree(heroKey) ? ": keystones unlocked" : ""))));
                }
            }
            GameEvent killNotice = null;
            if (variant != null)
            {
                p.Stats.VariantsSlain = SaturatingAdd(p.Stats.VariantsSlain, 1);
                killNotice = new GameEvent(EventKind.Info, Loc.T($"夢の変種「{variant.Name}」を倒しました！", $"Slew the dream variant \"{variant.Name}\"!"));
            }
            else if (isNightmare)
            {
                p.Stats.NightmaresSlain = SaturatingAdd(p.Stats.NightmaresSlain, 1);
                killNotice = new GameEvent(EventKind.Info, Loc.T($"{Nightmares.Label(nightmare)}を倒しました！", $"Slew a {Nightmares.Label(nightmare)}!"));
                AddHint(p, Hint.FirstNightmare, ev);
            }
            p.StoreRng(rng);

            run.GearWindow = false;
            run.StarSecureRewarded = false;
            if (run.Kills < int.MaxValue) run.Kills++;
            if (p.Stats.Kills < int.MaxValue) p.Stats.Kills++;
            run.SatchelShards = SaturatingAdd(run.SatchelShards, reward.Shards);
            run.SatchelTuning = SaturatingAdd(run.SatchelTuning, reward.Tuning);
            foreach (var relic in reward.Relics)
            {
                p.Stats.RelicsFound = SaturatingAdd(p.Stats.RelicsFound, 1);
                run.RelicsFound = SaturatingAdd(run.RelicsFound, 1);
                if (relic.Rarity == Rarity.Legendary) p.Stats.LegendariesFound = SaturatingAdd(p.Stats.LegendariesFound, 1);
                p.Codex.Add(relic.CodexId);
                p.BestItemLevel = Math.Max(p.BestItemLevel, relic.ItemLevel);
                ev.Add(new GameEvent(EventKind.Drop, Loc.T(
                    $"{Content.RarityName(relic.Rarity)}「{relic.DisplayName}」を拾いました（まだ持ち帰っていません）",
                    $"Found {Content.RarityName(relic.Rarity)} \"{relic.DisplayName}\" (unsecured)"), relic.Rarity) { Relic = relic });
                AddToSatchel(p, relic, ev, trades, Waypoints.Sum(killWaypoint).ShardMultiplier == 0);
                AddHint(p, Hint.FirstDrop, ev);
                AdvanceRelicBounties(p, relic, ev);
            }
            if (isNightmare) AdvanceBounty(p, BountyKind.NightmareHunter, 1, false, ev);
            if (variant != null) AdvanceBounty(p, BountyKind.VariantHunter, 1, false, ev);
            var traits = nightmare | (variant?.Affixes ?? NightmareAffix.None);
            if ((traits & NightmareAffix.Veiled) != 0) AdvanceBounty(p, BountyKind.VeilHunter, 1, false, ev);
            if ((traits & NightmareAffix.Packbound) != 0) AdvanceBounty(p, BountyKind.PackHunter, 1, false, ev);
            if ((traits & NightmareAffix.Pulsing) != 0) AdvanceBounty(p, BountyKind.PulseHunter, 1, false, ev);
            if ((traits & NightmareAffix.LastStand) != 0) AdvanceBounty(p, BountyKind.LastStandHunter, 1, false, ev);
            switch (tier)
            {
                case MonsterTier.MiniBoss: AdvanceBounty(p, BountyKind.EliteHunter, 1, false, ev); break;
                case MonsterTier.Boss: AdvanceBounty(p, BountyKind.Bossbane, 1, false, ev); break;
                default: AdvanceBounty(p, BountyKind.Slayer, 1, false, ev); break;
            }
            ev.AddRange(AddXp(p, reward.Xp));
            ev.AddRange(Feats.Check(p));
            if (hoardPayout)
                ev.Add(new GameEvent(EventKind.Info, Loc.T(
                    $"宝庫の払い出し ×3：遺物{reward.Relics.Count}個・欠片{reward.Shards}・調律石{reward.Tuning}（未確保。鞄を超えた遺物は欠片になります）。",
                    $"Hoard payout ×3: {reward.Relics.Count} relic(s), {reward.Shards} shards, {reward.Tuning} tuning (unsecured; relics beyond satchel capacity become shards).")));
            // 宝庫の一括払い出しで討伐通知がトーストの表示上限から押し出されないよう、最後に送る。
            if (killNotice != null) ev.Add(killNotice);
            return ev;
        }

        private static void AddToSatchel(Profile p, Relic relic, List<GameEvent> ev, TradeLedger trades = null, bool suppressShards = false)
        {
            var run = p.Run;
            run.Satchel.Add(relic);
            int capacity = Workshop.SatchelCapacity(p);
            while (run.Satchel.Count > capacity)
            {
                Relic worst = null;
                foreach (var candidate in run.Satchel)
                {
                    if (trades != null && trades.IsReserved(candidate.Uid)) continue;
                    if (worst == null || candidate.Rarity < worst.Rarity ||
                        (candidate.Rarity == worst.Rarity && candidate.Score < worst.Score)) worst = candidate;
                }
                if (worst == null) break; // Reservations take precedence over capacity.
                run.Satchel.Remove(worst);
                if (suppressShards)
                {
                    ev.Add(new GameEvent(EventKind.Info, Loc.T(
                        $"持ち歩ける数を超えたため、「{worst.DisplayName}」を手放しました。道標の効果で報酬は得られません。",
                        $"Satchel full: \"{worst.DisplayName}\" was discarded. The waypoint prevents rewards.")));
                    continue;
                }
                int shards = Content.SalvageShards(worst.Rarity);
                if (worst.InfinityFreeSupply) shards = InfinityRewards.LimitShards(p, shards);
                // Removal and local credit belong to the same profile snapshot, including continue saves.
                p.AddMaterial(Materials.Shard, shards);
                ev.Add(new GameEvent(EventKind.Info, Loc.T(
                    $"鞄があふれたため、「{worst.DisplayName}」を欠片+{shards}に換えました。",
                    $"Satchel overflow: \"{worst.DisplayName}\" was converted into {shards} shards."), worst.Rarity)
                { SatchelOverflow = worst, SatchelOverflowShards = shards });
            }
        }

        /// <summary>Legacy pending dust trades only: call after consuming a definitive successful result.</summary>
        public static GameEvent CompleteSatchelOverflowDust(PendingTrade trade)
        {
            if (trade == null || trade.Kind != TradeKind.SatchelOverflowDust) return null;
            string name = trade.Relic?.DisplayName ?? trade.Uid;
            string rarity = Content.RarityName((Rarity)trade.Rarity).ToString();
            return new GameEvent(EventKind.Info, Loc.T(
                $"以前の鞄あふれ取引を回復しました：『{name}』（{rarity}）の夢のダスト {trade.EarnDust} は付与済みです。",
                $"Recovered a legacy overflow trade: {trade.EarnDust} Dream Dust for \"{name}\" ({rarity}) has already been granted."), (Rarity)trade.Rarity);
        }

        /// <summary>Legacy failed dust trades only. Fallback shards were capped at removal time; call after consuming a failed result.</summary>
        public static GameEvent CompleteSatchelOverflowFallback(Profile p, Relic relic, string runId, int shards)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (relic == null) throw new ArgumentNullException(nameof(relic));
            if (shards < 0) throw new ArgumentOutOfRangeException(nameof(shards));
            if (p.Run != null && p.Run.RunId == runId)
                p.Run.SatchelShards = SaturatingAdd(p.Run.SatchelShards, shards);
            else p.AddMaterial(Materials.Shard, shards);
            return new GameEvent(EventKind.Warning, Loc.T(
                $"以前の鞄あふれ取引の夢のダストを付与できなかったため、「{relic.DisplayName}」を欠片+{shards}に換えました。",
                $"Could not grant Dream Dust for legacy overflowing \"{relic.DisplayName}\"; granted {shards} shards instead."), relic.Rarity);
        }

        /// <summary>新しく見つけた遺物を依頼へ反映する。鞄が満杯で欠片になった場合も数える。</summary>
        private static void AdvanceRelicBounties(Profile p, Relic relic, List<GameEvent> ev)
        {
            if (relic.Rarity >= Rarity.Rare) AdvanceBounty(p, BountyKind.Treasure, 1, false, ev);
            if (relic.Rarity == Rarity.Legendary) AdvanceBounty(p, BountyKind.LegendFinder, 1, false, ev);
            if (relic.Rarity >= Rarity.Epic) AdvanceBounty(p, BountyKind.EpicFinder, 1, false, ev);
            if (Content.TryGetUnique(relic.UniqueId, out var unique) && unique.SetId != null)
                AdvanceBounty(p, BountyKind.SetHunter, 1, false, ev);
        }

        /// <summary>
        /// 新しいゾーンに着いたとき確保地点にするか。まだ何も倒しておらず、未確保品も深度もなければ、
        /// 判断することがないので出さない（ラン開始直後の最初のゾーン）。
        /// </summary>
        public static bool ShouldOfferSecurePoint(Profile p, bool traveling = false)
        {
            var run = p.Run;
            if (run == null || run.AwaitingChoice || run.Infinity != null) return false;
            return traveling || run.Kills > 0 || run.HasUnsecured || run.Heat > run.StartDepth;
        }

        /// <summary>確保地点（新しいゾーン）に着いた。次の敵を倒すまで装備を整えられる。</summary>
        public static List<GameEvent> ReachSecurePoint(Profile p, TradeLedger trades = null)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            if (run.Infinity != null && run.Infinity.Phase != InfinityPhase.AwaitingChoice) return ev;
            if (run.Infinity != null && run.WaypointGeneration == int.MaxValue)
                throw new InvalidOperationException("Infinity waypoint generation exhausted.");
            if (run.AwaitingChoice) return ev;
            NotifyForfeitedHoard(run, ev);
            Waypoints.Expire(run);
            if (run.WaypointGeneration < int.MaxValue) run.WaypointGeneration++;
            run.StarSecureRewarded = false;
            run.AwaitingChoice = true;
            run.GearWindow = true;
            run.OfferedPacts.Clear();
            var rng = p.TakeRng();
            run.OfferedPacts.AddRange(Pacts.Offer(rng, run.Pacts, Workshop.PactsOffered(p)));
            run.OfferedEventId = Guid.NewGuid().ToString("N");
            run.OfferedEvent = DreamEvents.Roll(rng, p, trades);
            run.OfferedWaypoints.AddRange(Waypoints.Offer(rng));
            p.StoreRng(rng);
            AddHint(p, Hint.FirstSecurePoint, ev);
            return ev;
        }

        private static void NotifyForfeitedHoard(RunState run, List<GameEvent> ev)
        {
            if (run.DeferredWaypointRelics.Count == 0 && run.DeferredWaypointShards == 0
                && run.DeferredWaypointTuning == 0) return;
            ev.Add(new GameEvent(EventKind.Lost, Loc.T(
                $"宝庫が未開封のため、保留中の遺物{run.DeferredWaypointRelics.Count}個・欠片{run.DeferredWaypointShards}・調律石{run.DeferredWaypointTuning}を失いました。",
                $"Hoard left unopened: forfeited {run.DeferredWaypointRelics.Count} held relic(s), {run.DeferredWaypointShards} shards and {run.DeferredWaypointTuning} tuning.")));
        }

        /// <summary>確保する。分解予約中の遺物は預かりに隔離し、残りを保管庫へ移す。深度に応じて欠片の上乗せを受け、深度を0に戻す。</summary>
        public static List<GameEvent> Secure(Profile p, TradeLedger trades = null)
        {
            var run = p.Run;
            if (run != null && trades != null && trades.HeldCount > 0)
            {
                for (int i = 0; i < run.Satchel.Count; i++)
                {
                    var relic = run.Satchel[i];
                    if (!trades.IsReserved(relic.Uid)) continue;
                    p.PendingSalvage.Add(new PendingSalvage(relic, SalvageReturnTarget.Stash));
                    run.Satchel.RemoveAt(i--);
                }
            }
            return Secure(p, true);
        }

        /// <summary>Choose one offered waypoint, or None, for the zone entered after this secure point.</summary>
        public static List<GameEvent> PickWaypoint(Profile p, Waypoint waypoint)
        {
            var run = p.Run;
            if (run == null || !run.AwaitingChoice)
                throw new InvalidOperationException(Loc.T("道標は確保地点で選べます。", "Choose a waypoint at a secure point."));
            if (waypoint != Waypoint.None && !run.OfferedWaypoints.Contains(waypoint))
                throw new InvalidOperationException(Loc.T("その道標は提示されていません。", "That waypoint is not on offer."));
            if (!InfinityRewards.CanChooseWaypoint(p, waypoint, out string budgetReason)) throw new InvalidOperationException(budgetReason);
            run.PendingWaypoint = waypoint;
            run.WaypointChosen = true;
            return new List<GameEvent> { new GameEvent(EventKind.Info, waypoint == Waypoint.None
                ? Loc.T("次のゾーンは道標なしで進みます。", "Continue into the next zone without a waypoint.")
                : Loc.T($"次のゾーンの道標：{Waypoints.Get(waypoint).Name}", $"Next zone waypoint: {Waypoints.Get(waypoint).Name}")) };
        }

        private static List<GameEvent> Secure(Profile p, bool awardStarXp)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            if (awardStarXp && !run.StarSecureRewarded)
                AddStarXp(p, run.HeroKey, StarProgression.SecureXp, ev);
            run.StarSecureRewarded = true;
            int heat = run.Heat;
            int relics = run.Satchel.Count;
            int bonusShards = InfinityRewards.LimitShards(p, (int)Math.Min(int.MaxValue, (long)run.SatchelShards * heat / 4
                * (Pacts.Sum(run.Pacts).DoubleDepthBonus ? 2 : 1)));
            int shards = SaturatingAdd(run.SatchelShards, bonusShards);
            int tuning = run.SatchelTuning;

            var overflow = new List<Relic>();
            foreach (var r in run.Satchel.OrderByDescending(r => r.Score))
            {
                if (p.Stash.Count < Workshop.StashCapacity(p)) { r.InfinityFreeSupply = false; p.Stash.Add(r); }
                else overflow.Add(r);
            }
            foreach (var r in overflow) shards = SaturatingAdd(shards, r.InfinityFreeSupply
                ? InfinityRewards.LimitShards(p, Content.SalvageShards(r.Rarity)) : Content.SalvageShards(r.Rarity));

            p.AddMaterial(Materials.Shard, shards);
            p.AddMaterial(Materials.Tuning, tuning);
            int stored = relics - overflow.Count;
            run.RelicsSecured = SaturatingAdd(run.RelicsSecured, stored);
            run.ShardsSecured = SaturatingAdd(run.ShardsSecured, shards);
            run.Satchel.Clear();
            run.SatchelShards = 0;
            run.SatchelTuning = 0;
            run.EventDropBonus = 0;
            run.EventLuck = 0;
            run.Heat = run.StartDepth;
            Waypoints.Activate(run);
            run.AwaitingChoice = false;
            int pacts = run.Pacts.Count;
            run.Pacts.Clear();
            run.OfferedPacts.Clear();
            run.OfferedEvent = DreamEvent.None;
            run.SecuredCount = SaturatingAdd(run.SecuredCount, 1);
            p.Stats.BestHeatSecured = Math.Max(p.Stats.BestHeatSecured, heat);

            string bonus = bonusShards > 0 ? Loc.T($"（うち潜行ボーナス{bonusShards}）", $" (incl. +{bonusShards} delve bonus)") : "";
            ev.Add(new GameEvent(EventKind.Secured, Loc.T(
                $"遺物{stored}個・欠片{shards}{bonus}・調律石{tuning}を保管庫に持ち帰りました。",
                $"Secured {stored} relic(s), {shards} shards{bonus}, {tuning} tuning stone(s).")));
            if (overflow.Count > 0)
            {
                ev.Add(new GameEvent(EventKind.Warning, Loc.T(
                    $"保管庫が一杯のため{overflow.Count}個を欠片にしました。",
                    $"Stash full: {overflow.Count} relic(s) were turned into shards.")));
            }
            if (pacts > 0) ev.Add(new GameEvent(EventKind.Info, Loc.T($"結んでいた悪夢の契約{pacts}つが解けました。", $"{pacts} nightmare pact(s) dissolved.")));
            AddHint(p, Hint.FirstSecure, ev);
            if (p.Material(Materials.Shard) >= Onboarding.StarterShardsForForgeHint) AddHint(p, Hint.ForgeReady, ev);
            ev.AddRange(AddXp(p, Content.SecureXp));
            AdvanceBounty(p, BountyKind.Collector, stored, true, ev);
            if (heat > 0) ReachBounty(p, BountyKind.DeepDiver, heat, true, ev);
            AdvanceBounty(p, BountyKind.Securer, 1, true, ev);
            ReachBounty(p, BountyKind.PactKeeper, pacts, true, ev);
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        /// <summary>依頼の進捗を amount 進める。secured=true なら報酬を直接保管庫側へ（確保の最中に達成した場合）。</summary>
        private static void AdvanceBounty(Profile p, BountyKind kind, int amount, bool secured, List<GameEvent> ev)
        {
            if (amount <= 0 || p.Run == null) return;
            foreach (var b in p.Run.Bounties)
            {
                if (b.Kind != kind || b.Done) continue;
                b.Progress = (int)Math.Min(b.Target, (long)b.Progress + amount);
                if (b.Progress >= b.Target) CompleteBounty(p, b, secured, ev);
            }
        }

        /// <summary>「値が目標以上に達したか」で判定する依頼（深度など）。</summary>
        private static void ReachBounty(Profile p, BountyKind kind, int value, bool secured, List<GameEvent> ev)
        {
            if (p.Run == null) return;
            foreach (var b in p.Run.Bounties)
            {
                if (b.Kind != kind || b.Done) continue;
                b.Progress = Math.Max(b.Progress, Math.Min(b.Target, value));
                if (value >= b.Target) CompleteBounty(p, b, secured, ev);
            }
        }

        private static void CompleteBounty(Profile p, Bounty b, bool secured, List<GameEvent> ev)
        {
            b.Done = true;
            b.Progress = b.Target;
            p.Stats.BountiesDone = SaturatingAdd(p.Stats.BountiesDone, 1);
            double mult = DailyDream.Get(p.Run.DailyId)?.BountyMult ?? 1.0;
            int shards = InfinityRewards.LimitShards(p, (int)Math.Round(b.RewardShards * mult));
            int tuning = InfinityRewards.LimitTuning(p, (int)Math.Round(b.RewardTuning * mult));
            int xp = InfinityRewards.LimitXp(p, (int)Math.Round(b.RewardXp * mult));
            if (secured)
            {
                p.AddMaterial(Materials.Shard, shards);
                p.AddMaterial(Materials.Tuning, tuning);
                p.Run.ShardsSecured = SaturatingAdd(p.Run.ShardsSecured, shards);
            }
            else
            {
                p.Run.SatchelShards = SaturatingAdd(p.Run.SatchelShards, shards);
                p.Run.SatchelTuning = SaturatingAdd(p.Run.SatchelTuning, tuning);
            }
            string rewardText = InfinityRewards.Active(p) ? Loc.T($"欠片{shards}・調律石{tuning}・夢の経験{xp}", $"{shards} shards, {tuning} tuning, {xp} dream XP") : b.RewardText(mult);
            ev.Add(new GameEvent(EventKind.Bounty, Loc.T(
                $"依頼「{b.Describe()}」を達成しました。{rewardText}" + (secured ? "" : "（欠片と調律石はまだ持ち帰っていません）"),
                $"Bounty complete: {b.Describe()} ({rewardText}" + (secured ? ")" : ", unsecured)"))));
            ev.AddRange(AddXp(p, xp, false));
            AddHint(p, Hint.FirstBounty, ev);
            ev.AddRange(Feats.Check(p));
        }

        /// <summary>確保を見送り、さらに深く潜る。ドロップ率とレア度が上がるが、被ダメージも増える。</summary>
        public static List<GameEvent> Delve(Profile p, Pact pact = Pact.None)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            var infinity = run.Infinity;
            if (infinity != null && (!run.AwaitingChoice || infinity.Phase != InfinityPhase.AwaitingChoice
                || infinity.SegmentEpoch == long.MaxValue || infinity.ChoiceRevision == long.MaxValue
                || infinity.GraphEpoch == long.MaxValue || infinity.RoomEpoch == long.MaxValue)) return ev;
            if (pact != Pact.None)
            {
                if (!run.OfferedPacts.Contains(pact)) throw new InvalidOperationException(Loc.T("その契約は提示されていません。", "That pact is not on offer."));
                run.Pacts.Add(pact);
                p.Stats.PactsSworn = SaturatingAdd(p.Stats.PactsSworn, 1);
                AdvanceBounty(p, BountyKind.PactBearer, 1, false, ev);
                var d = Pacts.Get(pact);
                ev.Add(new GameEvent(EventKind.Delved, Loc.T($"悪夢の契約「{d.Name}」を結びました。{d.Description}", $"Swore the nightmare pact \"{d.Name}\". {d.Description}")));
            }
            run.OfferedPacts.Clear();
            run.OfferedEvent = DreamEvent.None;
            run.Heat = Loot.ClampHeat(run.Heat + 1);
            run.PeakHeat = Math.Max(run.PeakHeat, run.Heat);
            Waypoints.Activate(run);
            run.AwaitingChoice = false;
            if (infinity != null)
            {
                infinity.SettledSegmentEpoch = infinity.SegmentEpoch;
                infinity.SegmentEpoch++;
                infinity.ChoiceRevision++;
                infinity.ClearsInCycle = 0;
                infinity.SoulObserved = false;
                infinity.Phase = InfinityPhase.Transitioning;
                infinity.TransitionIntent = "delve";
            }
            AddHint(p, Hint.FirstDelve, ev);
            ev.Add(new GameEvent(EventKind.Delved, Loc.T(
                $"潜行{run.Heat}に進みました。遺物が{(int)(Loot.HeatDropBonus * 100 * run.Heat)}%出やすくなり、受けるダメージは{Build.DamageTakenPerDelvePct * run.Heat}%増えます（まだ持ち帰っていない遺物{run.Satchel.Count}個）。",
                $"Delve {run.Heat}: relics +{(int)(Loot.HeatDropBonus * 100 * run.Heat)}%, damage taken +{Build.DamageTakenPerDelvePct * run.Heat}% (carrying {run.Satchel.Count} unsecured relic(s)).")));
            AdvanceBounty(p, BountyKind.Delver, 1, false, ev);
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        /// <summary>戦闘部屋の突破数が増えた。条件を満たせば遺失物を1つ取り戻す（未確保として鞄へ）。</summary>
        public static List<GameEvent> OnRoomsCleared(Profile p, int clearedRooms, TradeLedger trades = null)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            int added = clearedRooms - run.RoomsCleared;
            run.RoomsCleared = Math.Max(run.RoomsCleared, clearedRooms);
            AdvanceBounty(p, BountyKind.Pathfinder, added, false, ev);
            if (run.LostRecovered || p.LostAndFound.Count == 0 || run.RoomsCleared < Workshop.RoomsToRecover(p)) return ev;
            var best = p.LostAndFound.OrderByDescending(r => r.Score).First();
            p.LostAndFound.Remove(best);
            run.LostRecovered = true;
            ev.Add(new GameEvent(EventKind.Recovered, Loc.T(
                $"遺失物「{best.DisplayName}」を取り戻しました（まだ持ち帰っていません）",
                $"Recovered lost relic \"{best.DisplayName}\" (unsecured)"), best.Rarity));
            AddToSatchel(p, best, ev, trades);
            return ev;
        }

        private static int SaturatingAdd(int value, int amount) => (int)Math.Min(int.MaxValue, (long)value + amount);

        public static List<GameEvent> ReachInfinityChoice(Profile p, TradeLedger trades = null)
        {
            if (p?.Run?.Infinity?.Phase != InfinityPhase.AwaitingChoice) return new List<GameEvent>();
            return ReachSecurePoint(p, trades);
        }

        /// <summary>Secures once and ends without victory, defeat, or their progression rewards.</summary>
        public static List<GameEvent> SecuredReturn(Profile p, ISet<string> reservedUids = null)
        {
            var run = p.Run;
            if (run == null) return new List<GameEvent>();
            var infinity = run.Infinity;
            if (infinity == null || !run.AwaitingChoice || infinity.Phase != InfinityPhase.AwaitingChoice
                || infinity.ChoiceRevision == long.MaxValue) return new List<GameEvent>();
            if (reservedUids != null)
                for (int i = 0; i < run.Satchel.Count; i++)
                {
                    var relic = run.Satchel[i];
                    if (!reservedUids.Contains(relic.Uid)) continue;
                    p.PendingSalvage.Add(new PendingSalvage(relic, SalvageReturnTarget.Stash));
                    run.Satchel.RemoveAt(i--);
                }
            InfinityRecords.RecordReturn(p, run);
            var ev = Secure(p, true);
            infinity.ChoiceRevision++;
            infinity.SettledSegmentEpoch = infinity.SegmentEpoch;
            infinity.Phase = InfinityPhase.Returning;
            infinity.TransitionIntent = "return";
            p.LastReport = new RunReport
            {
                SecuredReturn = true, Kills = run.Kills, RelicsFound = run.RelicsFound,
                RelicsSecured = run.RelicsSecured, ShardsSecured = run.ShardsSecured,
                PeakHeat = run.PeakHeat, SecuredCount = run.SecuredCount,
                LevelBefore = run.LevelAtStart > 0 ? run.LevelAtStart : p.DreamLevel, LevelAfter = p.DreamLevel,
                BountiesTotal = run.Bounties.Count, BountiesDone = run.Bounties.Count(b => b.Done),
            };
            NotifyForfeitedHoard(run, ev);
            Waypoints.Expire(run);
            p.CompletedRunId = run.RunId;
            p.CompletedRunSecuredReturn = true;
            p.Run = null;
            return ev;
        }

        public static List<GameEvent> EndRun(Profile p, bool victory, ISet<string> reservedUids = null)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            if (reservedUids != null)
            {
                var returnTarget = victory ? SalvageReturnTarget.Stash : SalvageReturnTarget.LostAndFound;
                for (int i = 0; i < run.Satchel.Count; i++)
                {
                    var relic = run.Satchel[i];
                    if (!reservedUids.Contains(relic.Uid)) continue;
                    p.PendingSalvage.Add(new PendingSalvage(relic, returnTarget));
                    run.Satchel.RemoveAt(i--);
                }
            }
            var report = new RunReport { Victory = victory, LevelBefore = run.LevelAtStart > 0 ? run.LevelAtStart : p.DreamLevel };
            if (victory)
            {
                ev.AddRange(Secure(p, false));
                AddStarXp(p, run.HeroKey, StarProgression.VictoryXp, ev);
                p.Stats.Victories++;
                p.Stats.BestVictoryStartDepth = Math.Max(p.Stats.BestVictoryStartDepth, run.StartDepth);
                ev.AddRange(AddXp(p, Content.VictoryXp));
                ev.Add(new GameEvent(EventKind.Info, Loc.T("夢を踏破しました！", "The dream is conquered!")));
            }
            else
            {
                p.Stats.Defeats++;
                int echo = Pacts.Sum(run.Pacts).NoEcho ? 0 : Workshop.Echo(p, run.SatchelShards);
                p.AddMaterial(Materials.Shard, echo);
                int lost = run.Satchel.Count;
                report.RelicsLost = lost;
                report.EchoShards = echo;
                foreach (var r in run.Satchel) p.LostAndFound.Add(r);
                int salvaged = TrimLostAndFound(p);
                foreach (var relic in p.LostAndFound) relic.InfinityFreeSupply = false;
                if (lost > 0 || echo > 0)
                {
                    ev.Add(new GameEvent(EventKind.Lost, Loc.T(
                        $"まだ持ち帰っていなかった遺物{lost}個は遺失物になりました。欠片の一部（{echo}）は残響として戻りました。",
                        $"{lost} unsecured relic(s) went to Lost & Found. {echo} shard(s) returned as echoes.")));
                }
                AddHint(p, Hint.FirstDefeat, ev);
                if (salvaged > 0)
                {
                    ev.Add(new GameEvent(EventKind.Warning, Loc.T(
                        $"遺失物が上限を超えたため{salvaged}個を欠片にしました。",
                        $"Lost & Found overflowed: {salvaged} relic(s) were turned into shards.")));
                }
            }
            report.Kills = run.Kills;
            report.RelicsFound = run.RelicsFound;
            report.RelicsSecured = run.RelicsSecured;
            report.ShardsSecured = run.ShardsSecured;
            report.PeakHeat = run.PeakHeat;
            report.SecuredCount = run.SecuredCount;
            report.LevelAfter = p.DreamLevel;
            report.BountiesTotal = run.Bounties.Count;
            report.BountiesDone = run.Bounties.Count(b => b.Done);
            p.LastReport = report;
            run.EventDropBonus = 0;
            run.EventLuck = 0;
            NotifyForfeitedHoard(run, ev);
            Waypoints.Expire(run);
            p.CompletedRunId = run.RunId;
            p.CompletedRunSecuredReturn = false;
            p.Run = null;
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        public static GameEvent BuyUpgrade(Profile p, Upgrade u, bool inExpedition = true)
        {
            if (inExpedition && p.Run != null) throw new InvalidOperationException(Loc.T("工房は遠征の外でのみ使えます。", "The workshop is only available outside expeditions."));
            var def = Workshop.Get(u);
            int lv = Workshop.Level(p, u);
            if (lv >= def.MaxLevel) throw new InvalidOperationException(Loc.T("これ以上強化できません。", "Already at max level."));
            var cost = def.Costs[lv];
            if (p.Material(Materials.Shard) < cost.Shards || p.Material(Materials.Tuning) < cost.Tuning)
                throw new InvalidOperationException(Loc.T($"素材が足りません（欠片{cost.Shards}・調律石{cost.Tuning}）。", $"Not enough materials ({cost.Shards} shards, {cost.Tuning} tuning)."));
            p.AddMaterial(Materials.Shard, -cost.Shards);
            p.AddMaterial(Materials.Tuning, -cost.Tuning);
            p.Upgrades[u] = lv + 1;
            return new GameEvent(EventKind.LevelUp, Loc.T($"工房で「{def.Name}」を解放しました（{lv + 1}/{def.MaxLevel}）。", $"Workshop: {def.Name} {lv + 1}/{def.MaxLevel}"));
        }

        /// <summary>確保地点の出来事を使う（1回だけ）。</summary>
        public static List<GameEvent> UseEvent(Profile p, DreamEvent e, bool goldPaid = false, TradeLedger trades = null)
        {
            if (!DreamEvents.CanUse(p, e, goldPaid, out string reason, trades)) throw new InvalidOperationException(reason);
            var run = p.Run;
            var ev = new List<GameEvent>();
            var rng = p.TakeRng();
            switch (e)
            {
                case DreamEvent.Merchant:
                {
                    if (!goldPaid) p.AddMaterial(Materials.Shard, -DreamEvents.MerchantCost(run.Heat));
                    GiveMerchantRelic(p, rng, ev, run.Heat, trades);
                    break;
                }
                case DreamEvent.Fountain:
                {
                    var sacrifice = run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderBy(r => r.Score).First();
                    run.Satchel.Remove(sacrifice);
                    var target = run.Satchel.Where(r => r.Enhance < Content.MaxEnhanceFor(r) && (trades == null || !trades.IsReserved(r.Uid))).OrderByDescending(r => r.Score).First();
                    target.Enhance++;
                    string fountainMilestone = GrantEnhanceMilestones(rng, target);
                    ev.Add(new GameEvent(EventKind.Info, Loc.T($"泉に「{sacrifice.DisplayName}」を捧げると、「{target.PlainName}」が+{target.Enhance}に強化されました。",
                        $"Offered \"{sacrifice.DisplayName}\"; \"{target.PlainName}\" was enhanced to +{target.Enhance}.") + MilestoneSuffix(fountainMilestone), target.Rarity));
                    AdvanceBounty(p, BountyKind.RelicEnhancer, 1, false, ev);
                    break;
                }
                case DreamEvent.Chalice:
                {
                    int bet = run.SatchelShards;
                    if (rng.Chance(0.5))
                    {
                        run.SatchelShards = SaturatingAdd(run.SatchelShards, InfinityRewards.LimitShards(p, bet));
                        ev.Add(new GameEvent(EventKind.Secured, Loc.T($"賭けに勝ちました！ まだ持ち帰っていない欠片が{bet}から{run.SatchelShards}に増えました。", $"You won! Unsecured shards {bet} -> {run.SatchelShards}")));
                    }
                    else
                    {
                        run.SatchelShards = 0;
                        ev.Add(new GameEvent(EventKind.Lost, Loc.T($"賭けに負けました…。まだ持ち帰っていない欠片{bet}を失いました。", $"You lost... {bet} unsecured shards are gone")));
                    }
                    break;
                }
                case DreamEvent.Lantern:
                {
                    var best = p.LostAndFound.OrderByDescending(r => r.Score).First();
                    p.LostAndFound.Remove(best);
                    ev.Add(new GameEvent(EventKind.Recovered, Loc.T($"迷い人の灯で、遺失物「{best.DisplayName}」を取り戻しました（まだ持ち帰っていません）。",
                        $"The lantern recovered the lost relic \"{best.DisplayName}\" (unsecured)."), best.Rarity));
                    AddToSatchel(p, best, ev, trades);
                    break;
                }
                case DreamEvent.ForgeShrine:
                {
                    var target = run.Satchel.Where(r => r.Enhance < Content.MaxEnhanceFor(r) && (trades == null || !trades.IsReserved(r.Uid))).OrderByDescending(r => r.Score).First();
                    run.SatchelShards -= target.Rarity >= Rarity.Epic ? 40 : 20;
                    target.Enhance++;
                    string shrineMilestone = GrantEnhanceMilestones(rng, target);
                    ev.Add(new GameEvent(EventKind.Info, Loc.T($"鍛冶の祠で「{target.PlainName}」を+{target.Enhance}に強化しました。",
                        $"The Forge Shrine enhanced \"{target.PlainName}\" to +{target.Enhance}.") + MilestoneSuffix(shrineMilestone), target.Rarity));
                    AdvanceBounty(p, BountyKind.RelicEnhancer, 1, false, ev);
                    break;
                }
                case DreamEvent.TwinMirror:
                {
                    var source = run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderByDescending(r => r.Score).First();
                    var rarity = source.Rarity == Rarity.Legendary ? Rarity.Epic : source.Rarity;
                    var relic = Loot.RollBaseRelic(rng, source.Base, rarity, source.ItemLevel);
                    run.SatchelShards -= source.Rarity >= Rarity.Epic ? 60 : 30;
                    RecordEventRelic(p, relic, ev, trades);
                    break;
                }
                case DreamEvent.Stargazer:
                    run.EventDropBonus += DreamEvents.StargazerDropBonus;
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    break;
                case DreamEvent.Cauldron:
                {
                    var parts = run.Satchel.Where(r => (r.Rarity == Rarity.Common || r.Rarity == Rarity.Uncommon) && (trades == null || !trades.IsReserved(r.Uid)))
                        .OrderBy(r => r.Score).Take(3).ToList();
                    var rarity = parts.Max(r => r.Rarity) + 1;
                    int itemLevel = parts.Max(r => r.ItemLevel);
                    foreach (var part in parts) run.Satchel.Remove(part);
                    var relic = Loot.RollRelic(rng, rarity, itemLevel, null,
                        p.Focus ?? DailyDream.Get(run.DailyId)?.FeaturedLine, p.Stash, run.Satchel, p.Codex);
                    RecordEventRelic(p, relic, ev, trades);
                    break;
                }
                case DreamEvent.Tapir:
                {
                    int count = 0;
                    int shards = 0;
                    for (int i = run.Satchel.Count - 1; i >= 0; i--)
                    {
                        var relic = run.Satchel[i];
                        if (relic.Rarity >= Rarity.Epic || (trades != null && trades.IsReserved(relic.Uid))) continue;
                        shards += Content.SalvageShards(relic.Rarity);
                        count++;
                        run.Satchel.RemoveAt(i);
                    }
                    int tuning = count / 3;
                    // 獏の欠片は保管庫へ直接入れ、確保時の潜行ボーナスを掛けない。
                    p.AddMaterial(Materials.Shard, shards);
                    run.SatchelTuning += tuning;
                    ev.Add(new GameEvent(EventKind.Info, Loc.T($"獏に遺物{count}個を食べさせ、保管庫の欠片{shards}と未確保の調律石{tuning}を得ました。",
                        $"Fed {count} relic(s) to the tapir: +{shards} stash shards, +{tuning} unsecured tuning.")));
                    break;
                }
                case DreamEvent.CourageGate:
                {
                    run.Heat = Loot.ClampHeat(run.Heat + 1);
                    run.PeakHeat = Math.Max(run.PeakHeat, run.Heat);
                    int granted = InfinityRewards.LimitShards(p, 40);
                    run.SatchelShards = SaturatingAdd(run.SatchelShards, granted);
                    ev.Add(new GameEvent(EventKind.Delved, Loc.T($"勇気の門をくぐり、潜行が{run.Heat}になりました。まだ持ち帰っていない欠片が{granted}増えました。",
                        $"Gate of Courage: delve {run.Heat}, +{granted} unsecured shards")));
                    break;
                }
                case DreamEvent.Archive:
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    ev.AddRange(AddXp(p, 40 + 20 * run.Heat));
                    break;
                case DreamEvent.LuckyStar:
                    run.EventLuck += DreamEvents.LuckyStarLuck;
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    break;
                case DreamEvent.MemoryWell:
                case DreamEvent.PowerCrucible:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    var pool = DreamEvents.ReplacementPowers(target)
                        .Where(x => Content.PowerAllowedForRarity(x.Power, target.Rarity)).ToList();
                    if (pool.Count == 0) throw new InvalidOperationException(Loc.T("別の固有効果を付けられません。", "No different power is available."));
                    var chosen = pool[rng.Range(0, pool.Count - 1)];
                    var replacement = new PowerLine(chosen.Power, rng.Range(chosen.Min, chosen.Max));
                    var old = target.Powers[0];
                    if (e == DreamEvent.MemoryWell) p.AddMaterial(Materials.Tuning, -(target.Rarity >= Rarity.Epic ? 2 : 1));
                    else target.Powers.RemoveAt(target.Powers.Count - 1);
                    target.Powers[0] = replacement;
                    ev.Add(new GameEvent(e == DreamEvent.MemoryWell ? EventKind.LevelUp : EventKind.Info, Loc.T(
                        $"「{target.PlainName}」の固有効果「{Content.PowerName(old.Power)}」を失い、「{Content.PowerName(replacement.Power)}」へ交換しました。",
                        $"\"{target.PlainName}\" lost \"{Content.PowerName(old.Power)}\" and gained \"{Content.PowerName(replacement.Power)}\"."), target.Rarity));
                    break;
                }
                case DreamEvent.ShadowExchange:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    var replacement = Loot.RollAffix(rng, target.Slot, target.Rarity, target.ItemLevel, DreamEvents.ShadowExcludedStats(target), target.Base.Family);
                    if (replacement == null) throw new InvalidOperationException(Loc.T("別の特性を付けられません。", "No different affix is available."));
                    var old = target.Affixes[0];
                    run.SatchelShards -= target.Rarity >= Rarity.Epic ? 50 : 25;
                    target.Retunes++;
                    target.Affixes[0] = replacement;
                    ev.Add(new GameEvent(EventKind.Info, Loc.T(
                        $"「{target.PlainName}」の特性を交換しました：{Content.FormatStat(old.Stat, old.Value)} → {Content.FormatStat(replacement.Stat, replacement.Value)}",
                        $"\"{target.PlainName}\" exchanged an affix: {Content.FormatStat(old.Stat, old.Value)} → {Content.FormatStat(replacement.Stat, replacement.Value)}"), target.Rarity));
                    break;
                }
                case DreamEvent.LostMausoleum:
                {
                    var recovered = DreamEvents.LostCandidates(p, trades).ToList();
                    run.SatchelShards -= 60;
                    foreach (var relic in recovered)
                    {
                        p.LostAndFound.Remove(relic);
                        relic.Enhance = 0;
                        ev.Add(new GameEvent(EventKind.Recovered, Loc.T(
                            $"霊廟から「{relic.PlainName}」を回収しました。強化は0になり、まだ持ち帰っていません。",
                            $"Recovered \"{relic.PlainName}\" from the mausoleum with enhancement reset to 0 (unsecured)."), relic.Rarity));
                        AddToSatchel(p, relic, ev, trades);
                    }
                    break;
                }
                case DreamEvent.RelicWager:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    if (rng.Chance(target.Rarity == Rarity.Rare ? 0.2 : 0.5))
                    {
                        var replacement = Loot.RollBaseRelic(rng, target.Base, target.Rarity + 1, target.ItemLevel);
                        run.Satchel.Remove(target);
                        ev.Add(new GameEvent(EventKind.Lost, Loc.T($"賭けに勝ち、「{target.DisplayName}」を新品へ交換しました。", $"Won the wager and exchanged \"{target.DisplayName}\" for a fresh relic.")));
                        RecordEventRelic(p, replacement, ev, trades);
                    }
                    else
                    {
                        int shards = SalvageValue(target);
                        run.Satchel.Remove(target);
                        run.SatchelShards += shards;
                        ev.Add(new GameEvent(EventKind.Lost, Loc.T($"賭けに負け、「{target.DisplayName}」は未確保の欠片{shards}になりました。", $"Lost the wager: \"{target.DisplayName}\" became {shards} unsecured shards.")));
                    }
                    break;
                }
                case DreamEvent.TemperingAltar:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    var lost = target.Affixes[0];
                    target.Affixes.RemoveAt(0);
                    target.Enhance += 2;
                    string milestone = GrantEnhanceMilestones(rng, target);
                    ev.Add(new GameEvent(EventKind.Info, Loc.T(
                        $"「{target.PlainName}」は特性「{Content.FormatStat(lost.Stat, lost.Value)}」を失い、+{target.Enhance}に強化されました。",
                        $"\"{target.PlainName}\" lost \"{Content.FormatStat(lost.Stat, lost.Value)}\" and was enhanced to +{target.Enhance}.") + MilestoneSuffix(milestone), target.Rarity));
                    AdvanceBounty(p, BountyKind.RelicEnhancer, 1, false, ev);
                    break;
                }
                case DreamEvent.StoneBroker:
                    run.SatchelShards -= 35;
                    run.SatchelTuning += 2;
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    break;
                case DreamEvent.ShardKiln:
                    run.SatchelTuning -= 2;
                    run.SatchelShards += 45;
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    break;
                case DreamEvent.StarOffering:
                case DreamEvent.DreamOffering:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    run.Satchel.Remove(target);
                    ev.Add(new GameEvent(EventKind.Lost, Loc.T($"「{target.DisplayName}」を供物として捧げました。", $"Sacrificed \"{target.DisplayName}\" as an offering."), target.Rarity));
                    if (e == DreamEvent.StarOffering)
                    {
                        AddStarXp(p, run.HeroKey, 40, ev, false);
                    }
                    else ev.AddRange(AddXp(p, 40 + 20 * run.Heat, false));
                    break;
                }
                case DreamEvent.AbyssalChest:
                {
                    var relic = Loot.RollRelic(rng, Rarity.Epic, p.BestItemLevel, null,
                        p.Focus ?? DailyDream.Get(run.DailyId)?.FeaturedLine, p.Stash, run.Satchel, p.Codex);
                    run.SatchelShards -= 75;
                    run.Heat++;
                    run.PeakHeat = Math.Max(run.PeakHeat, run.Heat);
                    ev.Add(new GameEvent(EventKind.Delved, Loc.T($"宝箱を開き、潜行が{run.Heat}になりました。", $"Opened the chest; delve is now {run.Heat}.")));
                    RecordEventRelic(p, relic, ev, trades);
                    AdvanceBounty(p, BountyKind.Delver, 1, false, ev);
                    break;
                }
                case DreamEvent.RelicExchange:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    var slot = (Slot)(((int)target.Slot + 1) % Content.SlotCount);
                    var relic = Loot.RollRelic(rng, target.Rarity, target.ItemLevel, slot,
                        p.Focus ?? DailyDream.Get(run.DailyId)?.FeaturedLine, p.Stash, run.Satchel, p.Codex);
                    run.Satchel.Remove(target);
                    ev.Add(new GameEvent(EventKind.Lost, Loc.T($"「{target.DisplayName}」を別の枠の遺物と交換しました。", $"Exchanged \"{target.DisplayName}\" for a relic in another slot."), target.Rarity));
                    RecordEventRelic(p, relic, ev, trades);
                    break;
                }
                case DreamEvent.SealedVault:
                {
                    var target = DreamEvents.TradeTarget(p, e, trades);
                    run.SatchelShards -= 40;
                    run.Satchel.Remove(target);
                    p.Stash.Add(target);
                    run.RelicsSecured++;
                    ev.Add(new GameEvent(EventKind.Secured, Loc.T($"「{target.DisplayName}」を保管庫へ送りました。他の荷物は未確保のままです。", $"Sent \"{target.DisplayName}\" to the stash; other cargo remains unsecured."), target.Rarity));
                    AdvanceBounty(p, BountyKind.Collector, 1, true, ev);
                    break;
                }
            }
            p.Stats.EventsUsed++;
            p.StoreRng(rng);
            run.OfferedEvent = DreamEvent.None;
            AdvanceBounty(p, BountyKind.EventTaker, 1, false, ev);
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        /// <summary>ホストが支払いを確定した商人の遺物を渡す。出来事や確保地点が終わっていても付与する。</summary>
        public static List<GameEvent> GrantPaidMerchant(Profile p, TradeLedger trades = null, PendingTrade purchase = null)
        {
            var ev = new List<GameEvent>();
            var rng = p.TakeRng();
            GiveMerchantRelic(p, rng, ev, purchase?.Heat ?? 0, trades);
            p.StoreRng(rng);
            p.Stats.EventsUsed++;
            // 旧取引は購入元不明。対価は渡すが、現在の提示を購入元と決めつけない。
            if (!string.IsNullOrEmpty(purchase?.MerchantOfferId)
                && p.Run?.OfferedEvent == DreamEvent.Merchant
                && p.Run.OfferedEventId == purchase.MerchantOfferId) p.Run.OfferedEvent = DreamEvent.None;
            AdvanceBounty(p, BountyKind.EventTaker, 1, false, ev);
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        private static void GiveMerchantRelic(Profile p, Rng rng, List<GameEvent> ev, int heat, TradeLedger trades)
        {
            var run = p.Run;
            var rarity = Loot.RollRarity(rng, 1.0 + Loot.HeatLuck * Loot.ClampHeat(heat), true, Rarity.Uncommon);
            var relic = Loot.RollRelic(rng, rarity, p.BestItemLevel, null,
                p.Focus ?? DailyDream.Get(run?.DailyId ?? 0)?.FeaturedLine, p.Stash, run?.Satchel, p.Codex);
            p.Codex.Add(relic.CodexId);
            p.Stats.RelicsFound++;
            if (relic.Rarity == Rarity.Legendary) p.Stats.LegendariesFound++;
            ev.Add(new GameEvent(EventKind.Drop, Loc.T(
                $"夢の商人から{Content.RarityName(relic.Rarity)}「{relic.DisplayName}」を買いました" + (run != null ? "（まだ持ち帰っていません）" : "（保管庫に入りました）"),
                $"Bought {Content.RarityName(relic.Rarity)} \"{relic.DisplayName}\" from the merchant" + (run != null ? " (unsecured)" : " (stash)")), relic.Rarity));
            if (run != null)
            {
                run.RelicsFound++;
                AddToSatchel(p, relic, ev, trades);
                AdvanceRelicBounties(p, relic, ev);
            }
            else
            {
                // 支払い済みの対価は、保管庫が満杯でも失わせない。
                p.Stash.Add(relic);
            }
        }

        private static void RecordEventRelic(Profile p, Relic relic, List<GameEvent> ev, TradeLedger trades = null)
        {
            p.Codex.Add(relic.CodexId);
            p.Run.RelicsFound++;
            p.Stats.RelicsFound++;
            if (relic.Rarity == Rarity.Legendary) p.Stats.LegendariesFound++;
            ev.Add(new GameEvent(EventKind.Drop, Loc.T(
                $"{Content.RarityName(relic.Rarity)}「{relic.DisplayName}」を手に入れました（まだ持ち帰っていません）",
                $"Gained {Content.RarityName(relic.Rarity)} \"{relic.DisplayName}\" (unsecured)"), relic.Rarity));
            AddToSatchel(p, relic, ev, trades);
            AdvanceRelicBounties(p, relic, ev);
        }

        /// <summary>本体での行動（祭壇・商人・強化・合成・分解・ハンター）を依頼へ反映する。</summary>
        public static List<GameEvent> OnGameAction(Profile p, BountyKind action)
        {
            var ev = new List<GameEvent>();
            if (p.Run == null) return ev;
            switch (action)
            {
                case BountyKind.ChaosSeeker:
                case BountyKind.Patron:
                case BountyKind.Refiner:
                case BountyKind.Alchemist:
                case BountyKind.Recycler:
                case BountyKind.HunterBait: break;
                default: return ev;
            }
            AdvanceBounty(p, action, 1, false, ev);
            return ev;
        }

        /// <summary>ホストが撃破時に採取した、敵の元素状態。</summary>
        public static List<GameEvent> OnElementalKill(Profile p, bool fire, bool cold, bool light, bool dark)
        {
            var ev = new List<GameEvent>();
            if (fire) AdvanceBounty(p, BountyKind.FireHunter, 1, false, ev);
            if (cold) AdvanceBounty(p, BountyKind.ColdHunter, 1, false, ev);
            if (light) AdvanceBounty(p, BountyKind.LightHunter, 1, false, ev);
            if (dark) AdvanceBounty(p, BountyKind.DarkHunter, 1, false, ev);
            return ev;
        }

        /// <summary>確定後の有効量が正の支援だけを数える。回復は他の旅人へのもの。</summary>
        public static List<GameEvent> OnSupportApplied(Profile p, bool shield, bool ally, float effectiveAmount)
        {
            var ev = new List<GameEvent>();
            if (float.IsNaN(effectiveAmount) || float.IsInfinity(effectiveAmount) || effectiveAmount <= 0) return ev;
            if (shield || ally) AdvanceBounty(p, shield ? BountyKind.ShieldGiver : BountyKind.AllyHealer, 1, false, ev);
            return ev;
        }

        /// <summary>Q・W・E・R の枠番号（0〜3）。装備した記憶の使用だけをホストが報告する。</summary>
        public static List<GameEvent> OnMemoryUsed(Profile p, int slotIndex)
        {
            var ev = new List<GameEvent>();
            if (slotIndex >= 0 && slotIndex <= 3)
                AdvanceBounty(p, (BountyKind)((int)BountyKind.MemoryQ + slotIndex), 1, false, ev);
            return ev;
        }

        public static List<GameEvent> OnZoneTravelled(Profile p)
        {
            var ev = new List<GameEvent>();
            AdvanceBounty(p, BountyKind.ZoneTraveler, 1, false, ev);
            return ev;
        }

        public static List<GameEvent> OnLinksSatisfied(Profile p, int count)
        {
            var ev = new List<GameEvent>();
            if (count > 0) ReachBounty(p, BountyKind.LinkWeaver, count, false, ev);
            return ev;
        }

        /// <summary>条件と間隔を通過して実際に発動した仕掛けの数。</summary>
        public static List<GameEvent> OnGimmicksTriggered(Profile p, int count)
        {
            var ev = new List<GameEvent>();
            AdvanceBounty(p, BountyKind.GimmickUser, count, false, ev);
            return ev;
        }

        /// <summary>現在の遠征の圧力を毎回評価する。達成済みの依頼は報酬を重複して受け取らない。</summary>
        public static List<GameEvent> OnPressureReported(Profile p, string activeRunId, float healthMultiplier)
        {
            var ev = new List<GameEvent>();
            if (p.Run == null || activeRunId == null || p.Run.RunId != activeRunId ||
                float.IsNaN(healthMultiplier) || float.IsInfinity(healthMultiplier)) return ev;
            double percent = Math.Floor(healthMultiplier * 100d + 0.0001d);
            if (percent <= 100 || percent > int.MaxValue) return ev;
            ReachBounty(p, BountyKind.PressureDiver, (int)percent, false, ev);
            return ev;
        }

        private static GameEvent WithBountyProgress(Profile p, GameEvent result, BountyKind kind)
        {
            if (p.Run == null) return result;
            var ev = new List<GameEvent>();
            AdvanceBounty(p, kind, 1, false, ev);
            if (ev.Count > 0) result.AdditionalEvents = ev;
            return result;
        }

        /// <summary>確保地点でドリームダストを欠片へ換える（ダストはホストが支払い済み）。欠片はそのまま保管庫側へ。</summary>
        public static GameEvent ConvertDust(Profile p, int dustPaid)
        {
            if (p.Run == null || (!p.Run.AwaitingChoice && !p.Run.GearWindow)) throw new InvalidOperationException(Loc.T("確保地点でのみ換えられます。", "Only at a secure point."));
            return GrantPaidDustShards(p, dustPaid);
        }

        /// <summary>支払い済みのダストの対価を保管庫側の素材へ渡す。現在のラン状態は問わない。</summary>
        public static GameEvent GrantPaidDustShards(Profile p, int dustPaid)
        {
            int batches = dustPaid / Economy.DustPerBatch;
            if (batches <= 0) throw new InvalidOperationException(Loc.T("ドリームダストが足りません。", "Not enough Dream Dust."));
            int shards = batches * Economy.ShardsPerBatch;
            p.AddMaterial(Materials.Shard, shards);
            return new GameEvent(EventKind.Secured, Loc.T($"ドリームダスト{batches * Economy.DustPerBatch}を欠片{shards}に換えました。", $"Converted {batches * Economy.DustPerBatch} Dream Dust into {shards} shards"));
        }

        /// <summary>成功応答の対象を鞄か預かりから1つだけ取り除く。既に無ければ null。</summary>
        public static Relic SalvageUnsecured(Profile p, string uid)
        {
            var relic = p.Run?.Satchel.Find(r => r.Uid == uid);
            if (relic != null)
            {
                p.Run.Satchel.Remove(relic);
                return relic;
            }
            int index = p.PendingSalvage.FindIndex(s => s.Relic.Uid == uid);
            if (index < 0) return null;
            relic = p.PendingSalvage[index].Relic;
            p.PendingSalvage.RemoveAt(index);
            return relic;
        }

        /// <summary>
        /// 分解失敗の預かり品を返す。uid が null なら台帳の無い起動時などに全品を返す。
        /// keepUids に挙げた遺物は、結果不明の取引がまだ握っているので返さない（ホストの確定結果が出るまで預かりのまま）。
        /// </summary>
        public static List<GameEvent> RestorePendingSalvage(Profile p, string uid = null, ISet<string> keepUids = null)
        {
            var ev = new List<GameEvent>();
            if (uid != null && p.Run?.Satchel.Find(r => r.Uid == uid) != null) return ev;
            bool restored = false;
            for (int i = 0; i < p.PendingSalvage.Count; i++)
            {
                var pending = p.PendingSalvage[i];
                if (uid != null && pending.Relic.Uid != uid) continue;
                if (keepUids != null && keepUids.Contains(pending.Relic.Uid)) continue;
                p.PendingSalvage.RemoveAt(i--);
                pending.Relic.InfinityFreeSupply = false;
                if (pending.ReturnTarget == SalvageReturnTarget.Stash && p.Stash.Count < Workshop.StashCapacity(p))
                    p.Stash.Add(pending.Relic);
                else
                    p.LostAndFound.Add(pending.Relic);
                restored = true;
                if (uid != null) break;
            }
            if (!restored) return ev;
            int salvaged = TrimLostAndFound(p);
            ev.Add(new GameEvent(EventKind.Info, Loc.T("分解待ちの遺物を戻しました。", "Pending salvage relics were returned.")));
            if (salvaged > 0)
                ev.Add(new GameEvent(EventKind.Warning, Loc.T(
                    $"遺失物が上限を超えたため{salvaged}個を欠片にしました。",
                    $"Lost & Found overflowed: {salvaged} relic(s) were turned into shards.")));
            return ev;
        }

        private static int TrimLostAndFound(Profile p)
        {
            int salvaged = 0;
            while (p.LostAndFound.Count > Content.LostAndFoundCapacity)
            {
                var worst = p.LostAndFound.OrderBy(r => r.Score).First();
                p.LostAndFound.Remove(worst);
                p.AddMaterial(Materials.Shard, worst.InfinityFreeSupply
                    ? InfinityRewards.LimitShards(p, Content.SalvageShards(worst.Rarity)) : Content.SalvageShards(worst.Rarity));
                salvaged++;
            }
            return salvaged;
        }

        public static int RerollsLeft(Profile p) => p.Run == null ? 0 : Math.Max(0, Workshop.RerollsPerRun(p) - p.Run.RerollsUsed);

        /// <summary>未達成の依頼を1つ、今ある依頼と重ならない種類で引き直す。</summary>
        public static GameEvent RerollBounty(Profile p, int index)
        {
            var run = p.Run ?? throw new InvalidOperationException(Loc.T("遠征中のみ使えます。", "Only during an expedition."));
            if (RerollsLeft(p) <= 0) throw new InvalidOperationException(Loc.T("引き直しの回数がありません（工房で解放）。", "No rerolls left (unlock in the workshop)."));
            if (index < 0 || index >= run.Bounties.Count || run.Bounties[index].Done)
                throw new InvalidOperationException(Loc.T("未達成の依頼を選んでください。", "Choose an unfinished bounty."));
            var rng = p.TakeRng();
            var existing = new HashSet<BountyKind>(run.Bounties.Select(b => b.Kind));
            Bounty replacement = null;
            for (int i = 0; i < 20 && replacement == null; i++)
            {
                var cand = Bounties.Roll(rng, 1)[0];
                if (!existing.Contains(cand.Kind)) replacement = cand;
            }
            p.StoreRng(rng);
            if (replacement == null) throw new InvalidOperationException(Loc.T("引き直せる依頼がありません。", "No other bounty available."));
            var old = run.Bounties[index];
            run.Bounties[index] = replacement;
            run.RerollsUsed++;
            return new GameEvent(EventKind.Bounty, Loc.T($"依頼を引き直しました：{old.Describe()} → {replacement.Describe()}", $"Bounty rerolled: {old.Describe()} -> {replacement.Describe()}"));
        }

        /// <summary>開始深度を選ぶ。遠征の外でのみ、確保できた最高深度まで。</summary>
        [Obsolete("v1.1 で廃止。本体の Limbo 深度を使う。")]
        public static void SetStartDepth(Profile p, int depth)
        {
            throw new InvalidOperationException(Loc.T("開始深度は廃止しました。本体の Limbo 深度が遺物に反映されます。", "Start depth was removed; the game's Limbo depth now boosts relics."));
        }

        /// <summary>狙い系統を選ぶ（null で解除）。遠征中は変えられない。ロビーにいる間は残ったランがあっても変えられる。</summary>
        public static void SetFocus(Profile p, Line? focus, bool inExpedition = true)
        {
            if (inExpedition && p.Run != null) throw new InvalidOperationException(Loc.T("狙い系統は遠征の外でのみ変更できます。", "Focus can only be changed outside expeditions."));
            p.Focus = focus;
        }

        private static void AddStarXp(Profile p, string heroKey, int amount, List<GameEvent> ev, bool freeSupply = true)
        {
            if (string.IsNullOrEmpty(heroKey)) return;
            amount = DreamDepth.ScaleReward(amount, DreamDepth.StarXpMultiplier(p.Run?.DreamDepth ?? 0));
            if (freeSupply) amount = InfinityRewards.LimitStarXp(p, amount);
            var hero = p.Hero(heroKey);
            int before = StarProgression.Points(hero.StarXp);
            StarProgression.AddXp(hero, amount);
            int gained = StarProgression.Points(hero.StarXp) - before;
            if (gained <= 0) return;
            string name = HeroNames.Display(heroKey);
            ev.Add(new GameEvent(EventKind.LevelUp, Loc.T(
                $"{name}の星図ポイントが{gained}増えました。",
                $"{name} earned {gained} star map point(s).")));
            AddHint(p, Hint.TalentPoints, ev);
        }

        public static List<GameEvent> AddXp(Profile p, int amount, bool freeSupply = true)
        {
            var ev = new List<GameEvent>();
            if (freeSupply) amount = InfinityRewards.LimitXp(p, amount);
            if (amount <= 0 || p.DreamLevel >= Content.MaxDreamLevel) return ev;
            p.DreamXp = (int)Math.Min(int.MaxValue, (long)p.DreamXp + amount);
            while (p.DreamLevel < Content.MaxDreamLevel && p.DreamXp >= Content.XpToNext(p.DreamLevel))
            {
                p.DreamXp -= Content.XpToNext(p.DreamLevel);
                p.DreamLevel++;
                ev.Add(new GameEvent(EventKind.LevelUp, Loc.T(
                    $"夢のレベルが{p.DreamLevel}に上がりました！",
                    $"Dream Level {p.DreamLevel}!")));
            }
            if (p.DreamLevel >= Content.MaxDreamLevel) p.DreamXp = 0;
            return ev;
        }

        // ───────────── 装着 ─────────────

        /// <summary>遠征中は、確保地点の選択待ちか、確保してから次の敵を倒すまでの間だけ、装備を変更できる。</summary>
        public static bool LoadoutLocked(Profile p, bool inGame) => p.Run != null && !p.Run.AwaitingChoice && !p.Run.GearWindow && inGame;

        public static IReadOnlyList<GameEvent> Equip(Profile p, string heroKey, string uid, TradeLedger trades = null,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            RequireUnreserved(trades, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            ApplyAllocationChange(p, heroKey, new AllocationChange
            {
                Kind = AllocationChangeKind.Equipment, EquipmentSlot = r.Slot, EquipmentUid = uid,
            }, approvedRefundIds);
            return Feats.Check(p);
        }

        public static IReadOnlyList<GameEvent> Unequip(Profile p, string heroKey, Slot slot,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            ApplyAllocationChange(p, heroKey, new AllocationChange
            {
                Kind = AllocationChangeKind.Equipment, EquipmentSlot = slot,
            }, approvedRefundIds);
            return Feats.Check(p);
        }

        public static Relic EquippedRelic(Profile p, string heroKey, Slot slot)
        {
            return p.FindStash(p.Hero(heroKey).Equipped[(int)slot]);
        }

        // ───────────── 鍛冶 ─────────────

        public static int SalvageValue(Relic r)
        {
            int refund = 0;
            for (int i = 0; i < r.Enhance; i++) refund += Content.EnhanceCost(i);
            return Content.SalvageShards(r.Rarity) + refund / 2;
        }

        public static GameEvent Salvage(Profile p, string uid, TradeLedger trades = null, bool loadoutLocked = false)
        {
            RequireUnreserved(trades, uid);
            RequireNoRetuneOffer(p, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            if (r.Locked) throw new InvalidOperationException(Loc.T("鍵のかかった遺物は分解できません。", "Locked relics cannot be salvaged."));
            if (loadoutLocked && p.IsEquippedAnywhere(uid))
            {
                throw new InvalidOperationException(Loc.T("装着中の遺物は、確保地点か遠征の外でしか分解できません。", "Equipped relics can only be salvaged at a secure point or outside an expedition."));
            }
            int shards = SalvageValue(r);
            int tuning = Content.SalvageTuning(r.Rarity);
            p.Stash.Remove(r);
            foreach (var h in p.Heroes.Values)
                for (int i = 0; i < h.Equipped.Length; i++)
                    if (h.Equipped[i] == uid) h.Equipped[i] = null;
            p.AddMaterial(Materials.Shard, shards);
            p.AddMaterial(Materials.Tuning, tuning);
            return new GameEvent(EventKind.Info, Loc.T(
                $"「{r.DisplayName}」を分解して、欠片{shards}" + (tuning > 0 ? $"と調律石{tuning}" : "") + "を得ました。",
                $"Salvaged \"{r.DisplayName}\": +{shards} shards" + (tuning > 0 ? $", +{tuning} tuning" : "")));
        }

        /// <summary>まとめて分解の対象（設定したレア度まで。鍵なし・未装着・取引中でも再調律の候補でもない物）。</summary>
        public static List<Relic> BulkSalvageCandidates(Profile p, TradeLedger trades = null)
        {
            string offered = p.RetuneOffer?.Uid;
            return p.Stash.Where(x => x.Rarity <= p.BulkSalvageMaxRarity && x.Rarity < Rarity.Legendary && !x.Locked && !p.IsEquippedAnywhere(x.Uid) && x.Uid != offered && (trades == null || !trades.IsReserved(x.Uid))).ToList();
        }

        public static void SetBulkSalvageMaxRarity(Profile p, Rarity r)
        {
            if (r < Rarity.Common || r > Rarity.Epic) throw new ArgumentOutOfRangeException(nameof(r), Loc.T("まとめて分解はコモンからエピックまでです。", "Bulk salvage covers Common to Epic only."));
            p.BulkSalvageMaxRarity = r;
        }

        /// <summary>候補を全部分解する。成功した物だけを数えて、得た欠片と調律石は素材の実際の増分で測る。</summary>
        public static GameEvent BulkSalvage(Profile p, TradeLedger trades = null, bool loadoutLocked = false)
        {
            var parts = BulkSalvageCandidates(p, trades);
            int shardsBefore = p.Material(Materials.Shard), tuningBefore = p.Material(Materials.Tuning);
            int done = 0;
            foreach (var r in parts)
            {
                try
                {
                    Salvage(p, r.Uid, trades, loadoutLocked);
                    done++;
                }
                catch (InvalidOperationException) { }
            }
            if (done == 0) return new GameEvent(EventKind.Info, Loc.T("分解できる遺物はありませんでした。", "Nothing could be salvaged."));
            int shards = p.Material(Materials.Shard) - shardsBefore;
            int tuning = p.Material(Materials.Tuning) - tuningBefore;
            return new GameEvent(EventKind.Info, Loc.T(
                $"{done}個をまとめて分解して、欠片{shards}" + (tuning > 0 ? $"と調律石{tuning}" : "") + "を得ました。",
                $"Salvaged {done} relics for {shards} shards" + (tuning > 0 ? $", +{tuning} tuning" : "") + "."));
        }

        /// <summary>鍛冶で次の強化値を目指すときの失敗率（%）。max(0, (目標強化値 − 3) × 3) を45%で頭打ち。上限では0。</summary>
        public static int EnhanceFailureChance(Relic relic)
        {
            if (relic.Enhance >= Content.MaxEnhanceFor(relic)) return 0;
            return Math.Min(45, Math.Max(0, (relic.Enhance - 2) * 3));
        }

        public static GameEvent Enhance(Profile p, string uid, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            RequireNoRetuneOffer(p, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            if (r.Enhance >= Content.MaxEnhanceFor(r)) throw new InvalidOperationException(Loc.T("これ以上強化できません。", "Already at maximum enhancement."));
            int cost = Content.EnhanceCost(r.Enhance) * (r.Rarity >= Rarity.Epic ? 2 : 1);
            if (p.Material(Materials.Shard) < cost) throw new InvalidOperationException(Loc.T($"欠片が足りません（{cost}必要）。", $"Not enough shards ({cost} needed)."));
            p.AddMaterial(Materials.Shard, -cost);
            var rng = p.TakeRng();
            if (rng.Chance(EnhanceFailureChance(r) / 100.0))
            {
                // 失敗しても半分は強化値そのまま。下がるかどうかも失敗判定と同じ rng から続けて引く。
                bool lowered = rng.Chance(0.5);
                if (lowered) r.Enhance = Math.Max(0, r.Enhance - 1);
                p.StoreRng(rng);
                return new GameEvent(EventKind.Info, lowered
                    ? Loc.T(
                        $"「{r.PlainName}」の強化に失敗し、強化値が1段下がりました（+{r.Enhance}）。欠片{cost}は消費されました。",
                        $"Enhancement failed for \"{r.PlainName}\" and lowered it by one level to +{r.Enhance}. The {cost} shards were spent.")
                    : Loc.T(
                        $"「{r.PlainName}」の強化に失敗しましたが、強化値は変わりませんでした（+{r.Enhance}）。欠片{cost}は消費されました。",
                        $"Enhancement failed for \"{r.PlainName}\" but its enhancement level is unchanged (+{r.Enhance}). The {cost} shards were spent."), r.Rarity);
            }
            r.Enhance++;
            string milestone = GrantEnhanceMilestones(rng, r);
            p.StoreRng(rng);
            return WithBountyProgress(p, new GameEvent(milestone != null ? EventKind.LevelUp : EventKind.Info,
                Loc.T($"「{r.PlainName}」の強化に成功し、+{r.Enhance}になりました。", $"Successfully enhanced \"{r.PlainName}\" to +{r.Enhance}.") + MilestoneSuffix(milestone), r.Rarity), BountyKind.RelicEnhancer);
        }

        private static string MilestoneSuffix(string milestone) => milestone == null ? "" : Loc.T("節目：", " Milestone: ") + milestone;

        /// <summary>限界突破の材料になる遺物（目標と同じ枠・同じレア度以上・鍵なし・未装着・取引中でない・再調律中でない）を弱い順に。</summary>
        public static List<Relic> LimitBreakCandidates(Profile p, Relic target, TradeLedger trades = null)
        {
            string offered = p.RetuneOffer?.Uid;
            return p.Stash.Where(x => x != target && x.Slot == target.Slot && x.Rarity >= target.Rarity && !x.Locked
                && !p.IsEquippedAnywhere(x.Uid) && x.Uid != offered && (trades == null || !trades.IsReserved(x.Uid)))
                .OrderBy(x => x.Score).ToList();
        }

        /// <summary>
        /// 限界突破（v1.27）。強化が上限に達した遺物の強化上限を+5広げる。
        /// 同じ枠の同じレア度以上の遺物1つを材料として消費し、調律石と欠片を払う。
        /// </summary>
        public static GameEvent LimitBreak(Profile p, string uid, string materialUid, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            RequireNoRetuneOffer(p, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            int maxBreaks = Content.MaxLimitBreaks(r.Rarity);
            if (maxBreaks <= 0) throw new InvalidOperationException(Loc.T("このレア度の遺物は限界突破できません。", "Relics of this rarity cannot limit break."));
            if (r.LimitBreaks >= maxBreaks) throw new InvalidOperationException(Loc.T("限界突破の回数が上限です。", "No limit breaks left."));
            if (r.Enhance < Content.MaxEnhanceFor(r)) throw new InvalidOperationException(Loc.T("強化が上限に達してから限界突破できます。", "Limit break is available at maximum enhancement."));
            var material = p.FindStash(materialUid) ?? throw new InvalidOperationException(Loc.T("材料の遺物が保管庫にありません。", "The material relic is not in your stash."));
            if (!LimitBreakCandidates(p, r, trades).Contains(material))
                throw new InvalidOperationException(Loc.T("材料は同じ枠で同じレア度以上の、鍵なし・未装着・取引中でない遺物です。", "The material must be an unlocked, unequipped, unreserved relic of the same slot and equal or higher rarity."));
            int n = r.LimitBreaks + 1;
            int tuningCost = Content.LimitBreakTuningCost(n) * (r.Rarity >= Rarity.Epic ? 2 : 1), shardCost = Content.LimitBreakShardCost(n) * (r.Rarity >= Rarity.Epic ? 2 : 1);
            if (p.Material(Materials.Tuning) < tuningCost) throw new InvalidOperationException(Loc.T($"調律石が足りません（{tuningCost}必要）。", $"Not enough tuning stones ({tuningCost} needed)."));
            if (p.Material(Materials.Shard) < shardCost) throw new InvalidOperationException(Loc.T($"欠片が足りません（{shardCost}必要）。", $"Not enough shards ({shardCost} needed)."));
            p.AddMaterial(Materials.Tuning, -tuningCost);
            p.AddMaterial(Materials.Shard, -shardCost);
            p.Stash.Remove(material);
            r.LimitBreaks = n;
            return WithBountyProgress(p, new GameEvent(EventKind.LevelUp, Loc.T(
                $"「{r.PlainName}」を限界突破しました（{n}/{maxBreaks}。素材「{material.PlainName}」。強化上限+{Content.MaxEnhanceFor(r)}）。",
                $"Limit broke \"{r.PlainName}\" ({n}/{maxBreaks}, used \"{material.PlainName}\". Enhancement cap +{Content.MaxEnhanceFor(r)})."), r.Rarity), BountyKind.LimitBreaker);
        }

        /// <summary>
        /// 強化の節目（+3：特性が1行。+5：固有効果を持たない遺物はその枠の固有効果を1つ得る、持っている遺物は特性がもう1行。
        /// +10・+15：特性が1行ずつ。+20：伝説だけ、固有効果1つの値が1.2倍）。
        /// 強化段階が上がったときと、起動時の一度だけの補完で呼ぶ。起きたことの文を返す（何もなければ null）。
        /// </summary>
        public static string GrantEnhanceMilestones(Rng rng, Relic r)
        {
            if (r.EnhanceMilestones >= 5) r.MilestonePowerApplied = true;
            if (r.MilestonePowerApplied) r.EnhanceMilestones = 5;
            var notes = new List<string>();
            if (r.Enhance >= Content.EnhanceMilestoneFirst && r.EnhanceMilestones < 1)
            {
                r.EnhanceMilestones = 1;
                var line = AddMilestoneAffix(rng, r);
                if (line != null) notes.Add(Loc.T($"特性「{Content.FormatStat(line.Stat, line.Value)}」が増えました。", $"gained \"{Content.FormatStat(line.Stat, line.Value)}\"."));
            }
            if (r.Enhance >= Content.EnhanceMilestoneSecond && r.EnhanceMilestones < 2)
            {
                r.EnhanceMilestones = 2;
                if (r.AuthoredEffectCount == 0)
                {
                    var pool = Content.PowerPool(r.Slot).Where(x => Content.PowerAllowedForRarity(x.Power, r.Rarity)).ToList();
                    var pr = pool[rng.Range(0, pool.Count - 1)];
                    r.Powers.Add(new PowerLine(pr.Power, pr.Min));
                    notes.Add(Loc.T($"固有効果「{Content.PowerName(pr.Power)}」が宿りました。", $"gained the power \"{Content.PowerName(pr.Power)}\"."));
                }
                else
                {
                    var line = AddMilestoneAffix(rng, r);
                    if (line != null) notes.Add(Loc.T($"特性「{Content.FormatStat(line.Stat, line.Value)}」が増えました。", $"gained \"{Content.FormatStat(line.Stat, line.Value)}\"."));
                }
            }
            if (r.Enhance >= Content.EnhanceMilestoneThird && r.EnhanceMilestones < 3)
            {
                r.EnhanceMilestones = 3;
                var line = AddMilestoneAffix(rng, r);
                if (line != null) notes.Add(Loc.T($"特性「{Content.FormatStat(line.Stat, line.Value)}」が増えました。", $"gained \"{Content.FormatStat(line.Stat, line.Value)}\"."));
            }
            if (r.Enhance >= Content.EnhanceMilestoneFourth && r.EnhanceMilestones < 4)
            {
                r.EnhanceMilestones = 4;
                var line = AddMilestoneAffix(rng, r);
                if (line != null) notes.Add(Loc.T($"特性「{Content.FormatStat(line.Stat, line.Value)}」が増えました。", $"gained \"{Content.FormatStat(line.Stat, line.Value)}\"."));
            }
            if (r.Enhance >= Content.EnhanceMilestoneFifth && !r.MilestonePowerApplied && r.Rarity == Rarity.Legendary)
            {
                if (r.BossMove != null)
                {
                    r.MilestonePowerApplied = true;
                    r.EnhanceMilestones = 5;
                    notes.Add(Loc.T("固有技のdamage/heal/shield係数に1.2倍の節目を適用しました（時間・距離・CDは固定）。",
                        "Applied the authored move's 1.2x damage/heal/shield milestone (time, range and cooldown stay fixed)."));
                }
                else
                {
                    var boosted = BoostMilestonePower(r);
                    if (boosted != null) notes.Add(Loc.T($"固有効果「{Content.PowerName(boosted.Power)}」の値が1.2倍になりました。", $"\"{Content.PowerName(boosted.Power)}\" grew 1.2x stronger."));
                }
            }
            return notes.Count == 0 ? null : string.Join(Loc.T("", " "), notes);
        }

        private static StatLine AddMilestoneAffix(Rng rng, Relic r)
        {
            var used = new HashSet<Stat> { r.Base.ImplicitStat };
            foreach (var a in r.Affixes) used.Add(a.Stat);
            var line = Loot.RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, used, r.Base.Family);
            if (line != null) r.Affixes.Add(line);
            return line;
        }

        /// <summary>+20の節目（伝説のみ）。1つ目の固有効果の保存値を一度だけ1.2倍にする。</summary>
        private static PowerLine BoostMilestonePower(Relic r)
        {
            if (r.MilestonePowerApplied || r.Powers.Count == 0) return null;
            var first = r.Powers[0];
            int value = Relic.Scale(first.Value, Content.LimitBreakPowerPct);
            r.Powers[0] = new PowerLine(first.Power, value);
            r.MilestonePowerApplied = true;
            r.EnhanceMilestones = 5;
            return r.Powers[0];
        }

        /// <summary>v1.20 より前に+3・+5にした遺物へ、節目を一度だけ付ける。付けた遺物の数を返す。</summary>
        public static int ApplyEnhanceMilestones(Profile p)
        {
            int n = 0;
            var rng = p.TakeRng();
            IEnumerable<Relic> all = p.Stash.Concat(p.LostAndFound);
            if (p.Run != null) all = all.Concat(p.Run.Satchel);
            foreach (var r in all.ToList())
            {
                int before = r.EnhanceMilestones;
                GrantEnhanceMilestones(rng, r);
                if (r.EnhanceMilestones != before) n++;
            }
            p.StoreRng(rng);
            return n;
        }

        private static void RequireNoRetuneOffer(Profile p, string uid)
        {
            if (p.RetuneOffer != null && p.RetuneOffer.Uid == uid)
                throw new InvalidOperationException(Loc.T("この遺物は再調律の候補を選んでいる途中です。先に候補を選んでください。", "Choose a retune option for this relic first."));
        }

        /// <summary>
        /// 再調律：調律石を払って、その特性の候補を3つ出す（できるだけ別の能力値）。候補は保存し、ChooseRetune で選ぶ。
        /// 払った調律石と回数は、選ばなくても戻らない。
        /// </summary>
        public static GameEvent Retune(Profile p, string uid, int affixIndex, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            if (p.RetuneOffer != null) throw new InvalidOperationException(Loc.T("先に、出ている再調律の候補を選んでください。", "Choose from the pending retune options first."));
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            if (affixIndex < 0 || affixIndex >= r.Affixes.Count) throw new InvalidOperationException(Loc.T("特性を選んでください。", "Choose an affix."));
            if (r.Retunes >= Content.MaxRetunes) throw new InvalidOperationException(Loc.T("再調律の回数を使い切りました。", "No retunes left."));
            int cost = Content.RetuneCost(r.Retunes) * (r.Rarity >= Rarity.Epic ? 2 : 1);
            if (p.Material(Materials.Tuning) < cost) throw new InvalidOperationException(Loc.T($"調律石が足りません（{cost}必要）。", $"Not enough tuning stones ({cost} needed)."));
            var others = new HashSet<Stat> { r.Base.ImplicitStat };
            for (int i = 0; i < r.Affixes.Count; i++) if (i != affixIndex) others.Add(r.Affixes[i].Stat);
            var rng = p.TakeRng();
            var offer = new RetuneOffer { Uid = uid, Index = affixIndex };
            var exclude = new HashSet<Stat>(others);
            for (int k = 0; k < Content.RetuneChoices; k++)
            {
                var line = Loot.RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, exclude, r.Base.Family)
                    ?? Loot.RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, others, r.Base.Family); // 能力値の種類が尽きたら、数値だけ違う候補にする
                if (line == null) break;
                exclude.Add(line.Stat);
                offer.Options.Add(line);
            }
            p.StoreRng(rng);
            if (offer.Options.Count == 0) throw new InvalidOperationException(Loc.T("候補を作れませんでした。", "No options available."));
            p.AddMaterial(Materials.Tuning, -cost);
            r.Retunes++;
            p.RetuneOffer = offer;
            return new GameEvent(EventKind.Info, Loc.T($"再調律の候補が{offer.Options.Count}つ出ました。1つ選ぶか、元のままにしてください。",
                $"{offer.Options.Count} retune options are ready. Pick one, or keep the original."), r.Rarity);
        }

        /// <summary>再調律の候補を選ぶ（choice が範囲外なら元のまま）。候補は消える。</summary>
        public static GameEvent ChooseRetune(Profile p, int choice)
        {
            var offer = p.RetuneOffer ?? throw new InvalidOperationException(Loc.T("再調律の候補がありません。", "No retune options pending."));
            p.RetuneOffer = null;
            var r = p.FindStash(offer.Uid);
            if (r == null || offer.Index >= r.Affixes.Count)
                return new GameEvent(EventKind.Info, Loc.T("候補の遺物が見つからないため、再調律を取りやめました。", "The relic is gone; the retune was cancelled."));
            if (choice < 0 || choice >= offer.Options.Count)
                return new GameEvent(EventKind.Info, Loc.T($"「{r.PlainName}」の特性は元のままにしました。", $"Kept the original affix on \"{r.PlainName}\"."), r.Rarity);
            var old = r.Affixes[offer.Index];
            var line = offer.Options[choice];
            r.Affixes[offer.Index] = line;
            return new GameEvent(EventKind.Info, Loc.T(
                $"再調律しました：{Content.FormatStat(old.Stat, old.Value)} → {Content.FormatStat(line.Stat, line.Value)}",
                $"Retuned: {Content.FormatStat(old.Stat, old.Value)} -> {Content.FormatStat(line.Stat, line.Value)}"), r.Rarity);
        }

        /// <summary>特性の洗い直しの費用（v1.31）：欠片 60×(レア度+1) と調律石 2×(レア度+1)（エピック以上は2倍）を、その遺物で済ませた回数ぶん1.5倍（切り上げ）する。</summary>
        public static (int Shards, int Tuning) AffixRerollCost(Relic r)
        {
            int times = Math.Max(0, r.AffixRerolls);
            return (TimesThreeHalves((r.Rarity >= Rarity.Epic ? 120 : 60) * ((int)r.Rarity + 1), times), TimesThreeHalves((r.Rarity >= Rarity.Epic ? 4 : 2) * ((int)r.Rarity + 1), times));
        }

        /// <summary>
        /// value × 1.5^times を整数だけで切り上げる（浮動小数点の誤差を持ち込まない）。
        /// 値を q + r/2^i（0 ≤ r &lt; 2^i）の形で持ち、1回ごとに×3/2 を正確に行うので、3^times や 2^times を一度も作らずに済む。
        /// q が int を超えたら（以後は単調に増えるだけなので）欠片の所持上限 int.MaxValue に張り付く。
        /// </summary>
        internal static int TimesThreeHalves(int value, int times)
        {
            if (value <= 0 || times <= 0) return Math.Max(0, value);
            long q = value, r = 0;
            for (int i = 0; i < times; i++)
            {
                if (q > int.MaxValue) return int.MaxValue; // 以後は増える一方。ここで止めるので i は高々60台で、2^i も long に収まる
                long den = 1L << i;
                long carry = 3 * r / den;                  // 3r/2^i = carry + r2/2^i
                long r2 = 3 * r % den;
                long top = 3 * q + carry;                  // x×3 = top + r2/2^i を、さらに2で割る
                q = top >> 1;
                r = ((top & 1) << i) + r2;                 // 割った余りは 2^(i+1) を分母にした値になる
            }
            long cost = q + (r > 0 ? 1 : 0);
            return (int)Math.Min(int.MaxValue, cost);
        }

        /// <summary>
        /// 特性の洗い直し（v1.31）：費用を払って、遺物の特性を全部まとめて引き直す。結果はすぐに確定し、取り消せない。
        /// 特性の数・レア度・土台・強化値・限界突破・覚醒・固有品の固有効果はそのまま、新しい遺物を作るときと同じ抽選で引き直す。
        /// 鍵つき・装着中・取引の予約中・再調律の候補が出ている遺物は対象外。
        /// </summary>
        public static GameEvent AffixReroll(Profile p, string uid, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            RequireNoRetuneOffer(p, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            if (r.Locked) throw new InvalidOperationException(Loc.T("鍵のかかった遺物は洗い直せません。", "Locked relics cannot be rerolled."));
            if (p.IsEquippedAnywhere(uid)) throw new InvalidOperationException(Loc.T("装着中の遺物は洗い直せません。", "Equipped relics cannot be rerolled."));
            var (shards, tuning) = AffixRerollCost(r);
            if (shards < 0 || tuning < 0) throw new InvalidOperationException(Loc.T("洗い直しの費用が不正です。", "Invalid reroll cost.")); // 負の費用は支払いが加算になるので受け付けない
            if (p.Material(Materials.Shard) < shards || p.Material(Materials.Tuning) < tuning)
                throw new InvalidOperationException(Loc.T($"素材が足りません（欠片{shards}・調律石{tuning}必要）。", $"Not enough materials ({shards} shards and {tuning} tuning stones needed)."));
            int count = r.Affixes.Count;
            string sep = Loc.T("、", ", ");
            var before = r.Affixes.Select(a => Content.FormatStat(a.Stat, a.Value)).ToList();
            var rng = p.TakeRng();
            r.Affixes.Clear();
            Loot.RollAffixes(rng, r, count); // 新しい遺物と同じ抽選（土台の暗黙値と同じ能力値は避ける）
            var implicitOnly = new HashSet<Stat> { r.Base.ImplicitStat };
            while (r.Affixes.Count < count) // 能力値の種類が尽きても数は守る：重複を許して埋める（再調律と同じ扱い）
            {
                var line = Loot.RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, implicitOnly, r.Base.Family);
                if (line == null) break;
                r.Affixes.Add(line);
            }
            p.StoreRng(rng);
            p.AddMaterial(Materials.Shard, -shards);
            p.AddMaterial(Materials.Tuning, -tuning);
            r.AffixRerolls++;
            string after = string.Join(sep, r.Affixes.Select(a => Content.FormatStat(a.Stat, a.Value)));
            return new GameEvent(EventKind.Info, Loc.T(
                $"「{r.PlainName}」の特性を洗い直しました：{string.Join(sep, before)} → {after}",
                $"Rerolled all affixes on \"{r.PlainName}\": {string.Join(sep, before)} -> {after}"), r.Rarity);
        }

        public static int CraftShardCost(bool fine) => fine ? 150 : 60;
        public static int CraftTuningCost(bool fine) => fine ? 2 : 0;

        /// <summary>製作。通常はアンコモン以上、上等はレア以上を、到達した最高アイテムレベルで作る。</summary>
        public static GameEvent Craft(Profile p, Slot slot, bool fine)
        {
            int shards = CraftShardCost(fine);
            int tuning = CraftTuningCost(fine);
            if (p.Stash.Count >= Workshop.StashCapacity(p)) throw new InvalidOperationException(Loc.T("保管庫が一杯です。", "Your stash is full."));
            if (p.Material(Materials.Shard) < shards || p.Material(Materials.Tuning) < tuning)
                throw new InvalidOperationException(Loc.T($"素材が足りません（欠片{shards}・調律石{tuning}）。", $"Not enough materials ({shards} shards, {tuning} tuning)."));
            var rng = p.TakeRng();
            var rarity = Loot.RollRarity(rng, fine ? 1.0 : 0.5, allowLegendary: false, fine ? Rarity.Rare : Rarity.Uncommon);
            var relic = Loot.RollRelic(rng, rarity, p.BestItemLevel, slot, ownedRelics: p.Stash, unsecuredRelics: p.Run?.Satchel, codex: p.Codex);
            p.StoreRng(rng);
            p.AddMaterial(Materials.Shard, -shards);
            p.AddMaterial(Materials.Tuning, -tuning);
            p.Stash.Add(relic);
            p.Codex.Add(relic.CodexId);
            return new GameEvent(EventKind.Drop, Loc.T(
                $"{Content.RarityName(relic.Rarity)}「{relic.DisplayName}」を作りました。",
                $"Crafted {Content.RarityName(relic.Rarity)} \"{relic.DisplayName}\""), relic.Rarity);
        }

        public static int TransmuteCost(Rarity r) => r >= Rarity.Epic ? 300 : r == Rarity.Rare ? 60 : 10 * ((int)r + 1);

        /// <summary>合成で要る調律石（固有品への合成だけ）。</summary>
        public static int TransmuteTuning(Rarity r) => r >= Rarity.Epic ? 4 : 0;

        /// <summary>合成の材料になる遺物（鍵なし・どこにも装着していない・同じレア度）を弱い順に。</summary>
        public static List<Relic> TransmuteCandidates(Profile p, Rarity r, TradeLedger trades = null)
        {
            string offered = p.RetuneOffer?.Uid;
            return p.Stash.Where(x => x.Rarity == r && !x.Locked && !p.IsEquippedAnywhere(x.Uid) && x.Uid != offered && (trades == null || !trades.IsReserved(x.Uid))).OrderBy(x => x.Score).ToList();
        }

        /// <summary>同じレア度の遺物 Content.TransmuteInputs 個（弱い順）を1つ上のレア度の遺物1つにする。エピックからは固有品。</summary>
        /// <summary>合成の費用。結果の枠を選ぶと TransmuteTargetCostPct 倍。</summary>
        public static int TransmuteCost(Rarity r, bool targeted) => targeted ? TransmuteCost(r) * Content.TransmuteTargetCostPct / 100 : TransmuteCost(r);

        public static GameEvent Transmute(Profile p, Rarity r, TradeLedger trades = null, Slot? target = null)
        {
            if (r >= Rarity.Legendary) throw new InvalidOperationException(Loc.T("固有品は合成できません。", "Legendaries cannot be transmuted."));
            int need = Content.TransmuteInputs(r);
            var parts = TransmuteCandidates(p, r, trades).Take(need).ToList();
            if (parts.Count < need) throw new InvalidOperationException(Loc.T($"材料が足りません（鍵なし・未装着の同じレア度が{need}つ必要）。", $"Need {need} unlocked, unequipped relics of the same rarity."));
            int cost = TransmuteCost(r, target != null);
            int tuningCost = TransmuteTuning(r);
            if (p.Material(Materials.Shard) < cost) throw new InvalidOperationException(Loc.T($"欠片が足りません（{cost}必要）。", $"Not enough shards ({cost} needed)."));
            if (p.Material(Materials.Tuning) < tuningCost) throw new InvalidOperationException(Loc.T($"調律石が足りません（{tuningCost}必要）。", $"Not enough tuning stones ({tuningCost} needed)."));
            int ilvl = parts.Max(x => x.ItemLevel);
            foreach (var x in parts) p.Stash.Remove(x);
            p.AddMaterial(Materials.Shard, -cost);
            if (tuningCost > 0) p.AddMaterial(Materials.Tuning, -tuningCost);
            var rng = p.TakeRng();
            var result = Loot.RollRelic(rng, r + 1, ilvl, target, p.Focus, p.Stash, p.Run?.Satchel, p.Codex);
            p.StoreRng(rng);
            p.Stash.Add(result);
            p.Codex.Add(result.CodexId);
            if (result.Rarity == Rarity.Legendary) p.Stats.LegendariesFound++;
            return new GameEvent(EventKind.Drop, Loc.T(
                $"合成して、{Content.RarityName(result.Rarity)}「{result.DisplayName}」ができました。",
                $"Transmuted into {Content.RarityName(result.Rarity)} \"{result.DisplayName}\""), result.Rarity);
        }

        public static void ToggleLock(Profile p, string uid, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            var r = p.FindStash(uid);
            if (r != null) r.Locked = !r.Locked;
        }

        private static void RequireUnreserved(TradeLedger trades, string uid)
        {
            if (trades != null && trades.IsReserved(uid))
                throw new InvalidOperationException(Loc.T("取引の応答を待っています。", "Waiting for the trade to complete."));
        }

        // ───────────── 専門化 ─────────────

        /// <summary>到達刻印のつながりと段数・熟練度の条件を確かめる。</summary>
        public static bool KeystoneUnlocked(Profile p, string heroKey, TalentDef key)
        {
            return AllocationValidationForHero(heroKey).KeystoneUnlocked(p.Hero(heroKey), heroKey, key);
        }

        /// <summary>装着中の遺物の覚醒の段の合計。段が上がったか（能力の送り直しが要るか）の判定に使う（issue #15）。</summary>
        public static int EquippedAwakenLevels(Profile p, string heroKey)
        {
            int n = 0;
            if (heroKey == null) return 0;
            foreach (string uid in p.Hero(heroKey).Equipped)
            {
                var r = uid != null ? p.FindStash(uid) : null;
                if (r != null) n += r.AwakenLevel;
            }
            return n;
        }

        /// <summary>その旅人の刻印ツリーに振った段数。奥の星を含み、到達刻印は含めない。</summary>
        public static int TreeRanks(HeroState h, string heroKey)
        {
            int n = 0;
            foreach (var kv in h.Talents)
                if (Content.TryGetTalent(heroKey, kv.Key, out var t) && t.HeroKey == heroKey && !t.IsKeystone) n += Math.Max(0, Math.Min(t.MaxRank, kv.Value));
            return n;
        }

        /// <summary>始まりから取得済みの星を通って届く星だけ、購入・発動できる。</summary>
        public static bool TalentUnlocked(HeroState h, string heroKey, TalentDef t)
        {
            return h != null && t != null && BelongsTo(t, heroKey)
                && AllocationValidationForHero(heroKey).CanReach(h, t);
        }

        /// <summary>取得済みの星すべてが始まりの星につながっているか。</summary>
        public static bool TalentsConnected(HeroState h, string heroKey)
        {
            return AllocationValidationForHero(heroKey).AllocationsConnected(h);
        }

        public static int RouteRanks(HeroState h, Line route)
        {
            int n = 0;
            foreach (var kv in h.Talents)
                if (Content.TryGetTalent(kv.Key, out var t) && t.HeroKey == null && t.Route == route && !t.IsKeystone) n += Math.Max(0, Math.Min(t.MaxRank, kv.Value));
            return n;
        }

        public static int SpentPoints(HeroState h)
        {
            long n = 0;
            foreach (var kv in h.Talents)
                n += (long)Math.Max(0, kv.Value) * (Content.TryGetTalent(kv.Key, out var t) ? t.RankCost : 1);
            foreach (string keystone in h.Keystones)
                if (keystone != null)
                    n += Content.TryGetTalent(keystone, out var key) ? key.KeystoneDefinition?.Cost ?? Content.KeystoneCost : Content.KeystoneCost;
            return (int)Math.Min(int.MaxValue, n);
        }

        /// <summary>Hero-qualified saved IDs retain their actual rank costs, including shared local Outer IDs.</summary>
        public static int SpentPoints(HeroState h, string heroKey) => AllocationValidationForHero(heroKey).SpentPoints(h);

        public static int FreePoints(Profile p, string heroKey) => p.TalentPoints(heroKey) - SpentPoints(p.Hero(heroKey), heroKey);

        private static readonly Dictionary<string, EffectiveAllocationValidation> allocationValidators =
            new Dictionary<string, EffectiveAllocationValidation>(StringComparer.Ordinal);
        private static readonly object allocationValidatorLock = new object();

        /// <summary>Install a reusable mechanism policy/engine before its data becomes purchasable. Null restores the canonical tree.</summary>
        public static void RegisterAllocationValidation(string heroKey, EffectiveAllocationValidation validation)
        {
            string key = string.IsNullOrEmpty(heroKey) ? "default" : heroKey;
            lock (allocationValidatorLock)
            {
                if (validation == null) allocationValidators.Remove(key);
                else allocationValidators[key] = validation;
            }
        }

        public static EffectiveAllocationValidation AllocationValidationForHero(string heroKey)
        {
            string key = string.IsNullOrEmpty(heroKey) ? "default" : heroKey;
            lock (allocationValidatorLock)
            {
                if (!allocationValidators.TryGetValue(key, out var validation))
                    allocationValidators.Add(key, validation = new EffectiveAllocationValidation(HeroSigils.TreeFor(heroKey), null, HeroTreeLayout.ForHero(heroKey)));
                return validation;
            }
        }

        /// <summary>UI/host preview of exact saturated IDs, original-cost refunds and prerequisite cascades. Does not mutate the profile.</summary>
        public static EffectiveAllocationPlan PreviewAllocationChange(Profile p, string heroKey, AllocationChange change,
            EffectiveAllocationValidation validation = null)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            var engine = validation ?? AllocationValidationForHero(heroKey);
            var talent = engine.Talent(change.CandidateStarId);
            if (p.Run != null && (change.Kind == AllocationChangeKind.Choice ||
                change.Kind == AllocationChangeKind.Purchase && talent?.IsChoice == true))
                throw new InvalidOperationException(Loc.T("遠征中は選択の星を変更できません。", "Choice stars cannot be changed during an expedition."));
            return engine.Preview(p, heroKey, change);
        }

        public static EffectiveAllocationPlan ApplyAllocationChange(Profile p, string heroKey, AllocationChange change,
            IReadOnlyCollection<string> approvedRefundIds = null, EffectiveAllocationValidation validation = null)
        {
            var engine = validation ?? AllocationValidationForHero(heroKey);
            var plan = PreviewAllocationChange(p, heroKey, change, engine);
            engine.Commit(p, plan, approvedRefundIds);
            return plan;
        }

        public static void AddTalentRank(Profile p, string heroKey, string talentId, int? choice = null,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            ApplyAllocationChange(p, heroKey, new AllocationChange
            {
                Kind = AllocationChangeKind.Purchase, CandidateStarId = talentId, SelectedOption = choice,
            }, approvedRefundIds);
        }

        /// <summary>Switch one explicit option for all allocated ranks, only outside expeditions; incompatible refunds need approval.</summary>
        public static void SetTalentChoice(Profile p, string heroKey, string talentId, int choice,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            ApplyAllocationChange(p, heroKey, new AllocationChange
            {
                Kind = AllocationChangeKind.Choice, CandidateStarId = talentId, SelectedOption = choice,
            }, approvedRefundIds);
        }

        /// <summary>The requested rank refund is explicit; any additional dependent refunds require separate approval.</summary>
        public static void RemoveTalentRank(Profile p, string heroKey, string talentId,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            var approval = approvedRefundIds == null
                ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(approvedRefundIds, StringComparer.Ordinal);
            approval.Add(talentId);
            ApplyAllocationChange(p, heroKey, new AllocationChange
            {
                Kind = AllocationChangeKind.Refund, CandidateStarId = talentId,
            }, approval);
        }

        /// <summary>
        /// 刻印を選ぶ。keystoneId は最初の空き枠に入る（重複不可・枠は星のレベルで増える）。
        /// null なら選んでいる刻印をすべて外す。変更で依存する星の払い戻しが必要な場合は C15 の承認（approvedRefundIds）が要る。
        /// </summary>
        public static void SetKeystone(Profile p, string heroKey, string keystoneId,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            var h = p.Hero(heroKey);
            if (keystoneId != null && h.HasKeystone(keystoneId))
                throw new InvalidOperationException(RuleMessages.KeystoneDuplicate.ToString());
            if (keystoneId != null && h.KeystoneCount >= h.KeystoneSlotCount)
                throw new InvalidOperationException(RuleMessages.KeystoneSlotsFull.ToString());
            IReadOnlyCollection<string> approval = approvedRefundIds;
            if (keystoneId == null && h.KeystoneCount > 0)
            {
                var ids = approvedRefundIds == null
                    ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(approvedRefundIds, StringComparer.Ordinal);
                foreach (string selected in h.Keystones) if (selected != null) ids.Add(selected);
                approval = ids;
            }
            ApplyAllocationChange(p, heroKey, new AllocationChange
            {
                Kind = AllocationChangeKind.Keystone, KeystoneId = keystoneId,
            }, approval);
        }

        /// <summary>選択中の刻印を1つだけ外す（費用は自動的に払い戻る）。外すもので星が無効になる場合は承認が要る。</summary>
        public static void RemoveKeystone(Profile p, string heroKey, string keystoneId,
            IReadOnlyCollection<string> approvedRefundIds = null)
        {
            var h = p.Hero(heroKey);
            if (keystoneId == null || !h.HasKeystone(keystoneId))
                throw new InvalidOperationException(RuleMessages.KeystoneNotAllocated.ToString());
            RemoveTalentRank(p, heroKey, keystoneId, approvedRefundIds);
        }

        /// <summary>ノードがその旅人のツリーに属するか。</summary>
        public static bool BelongsTo(TalentDef t, string heroKey)
        {
            return HeroSigils.HasTree(heroKey) ? t.HeroKey == heroKey : t.HeroKey == null;
        }

        public static void ResetTalents(Profile p, string heroKey)
        {
            var h = p.Hero(heroKey);
            h.Talents.Clear();
            h.TalentChoices.Clear();
            h.ClearKeystones();
        }
    }
}
