using System;
using System.Collections.Generic;
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
        /// <summary>Kind が Hint のときのヒント。</summary>
        public Hint? HintId { get; set; }

        public override string ToString() => Text;
    }

    /// <summary>
    /// プロフィールに対する操作（遠征・鍛冶・装着・専門化）。すべて純粋なデータ操作で、
    /// 失敗時は InvalidOperationException を投げ、プロフィールを変更しない。
    /// </summary>
    public static class Rules
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

        public static List<GameEvent> BeginRun(Profile p, string runId, DailyDream daily = null, int limboDepth = 0, ISet<string> reservedUids = null)
        {
            var ev = new List<GameEvent>();
            if (string.IsNullOrEmpty(runId)) runId = "unknown";
            if (p.Run != null && p.Run.RunId == runId) return ev;
            if (p.Run != null)
            {
                ev.Add(new GameEvent(EventKind.Warning, Loc.T("前回の遠征は確保されずに終わりました。", "Your previous expedition ended unsecured.")));
                ev.AddRange(EndRun(p, victory: false, reservedUids: reservedUids));
            }
            // 開始深度は v1.1 で廃止（本体の Limbo 深度に統合）。
            p.Run = new RunState { RunId = runId, LevelAtStart = p.DreamLevel, DailyId = daily?.Id ?? 0, LimboDepth = Math.Max(0, limboDepth) };
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

        /// <summary>契約・出来事・Limbo・今日の夢を合わせた撃破報酬の補正。</summary>
        public static Pacts.Totals KillModifiers(RunState run)
        {
            var t = Pacts.Sum(run.Pacts);
            t.DropBonus += LimboDropBonus * run.LimboDepth + run.EventDropBonus;
            t.Luck += LimboLuck * run.LimboDepth + run.EventLuck;
            var d = DailyDream.Get(run.DailyId);
            if (d != null)
            {
                t.DropBonus += d.DropBonus;
                t.ShardMult *= d.ShardMult;
                t.XpMult *= d.XpMult;
            }
            return t;
        }

        public static List<GameEvent> OnKill(Profile p, MonsterTier tier, int itemLevel, NightmareAffix nightmare = NightmareAffix.None, string heroKey = null, TradeLedger trades = null)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            var rng = p.TakeRng();
            int pity = p.EpicPity;
            bool isNightmare = nightmare != NightmareAffix.None;
            var rollTier = isNightmare ? Nightmares.RewardTier(tier) : tier;
            var focus = p.Focus ?? DailyDream.Get(run.DailyId)?.FeaturedLine;
            var reward = Loot.RollKill(rng, rollTier, itemLevel, run.Heat, ref pity, focus, KillModifiers(run), p.Stash, run.Satchel);
            if (heroKey != null)
            {
                var hs = p.Hero(heroKey);
                int before = Mastery.Level(hs.Kills);
                hs.Kills++;
                int after = Mastery.Level(hs.Kills);
                if (after > before && after == HeroSigils.KeystoneMastery && HeroSigils.HasTree(heroKey)) AddHint(p, Hint.KeystoneReady, ev);
                if (after > before)
                {
                    string name = heroKey.StartsWith("Hero_") ? heroKey.Substring(5) : heroKey;
                    ev.Add(new GameEvent(EventKind.LevelUp, Loc.T(
                        $"{name}の熟練度が{after}「{Mastery.Title(after)}」に上がりました。" + (after == HeroSigils.KeystoneMastery && HeroSigils.HasTree(heroKey) ? "到達刻印を選べるようになりました。" : ""),
                        $"{name} mastery {after} \"{Mastery.Title(after)}\"" + (after == HeroSigils.KeystoneMastery && HeroSigils.HasTree(heroKey) ? ": keystones unlocked" : ""))));
                }
            }
            if (isNightmare)
            {
                p.Stats.NightmaresSlain++;
                ev.Add(new GameEvent(EventKind.Info, Loc.T($"{Nightmares.Label(nightmare)}を倒しました！", $"Slew a {Nightmares.Label(nightmare)}!")));
                AddHint(p, Hint.FirstNightmare, ev);
            }
            p.EpicPity = pity;
            p.StoreRng(rng);

            run.Kills++;
            p.Stats.Kills++;
            run.SatchelShards += reward.Shards;
            run.SatchelTuning += reward.Tuning;
            foreach (var relic in reward.Relics)
            {
                p.Stats.RelicsFound++;
                run.RelicsFound++;
                if (relic.Rarity == Rarity.Legendary) p.Stats.LegendariesFound++;
                p.Codex.Add(relic.UniqueId ?? relic.BaseId);
                p.BestItemLevel = Math.Max(p.BestItemLevel, relic.ItemLevel);
                ev.Add(new GameEvent(EventKind.Drop, Loc.T(
                    $"{Content.RarityName(relic.Rarity)}「{relic.DisplayName}」を拾いました（まだ持ち帰っていません）",
                    $"Found {Content.RarityName(relic.Rarity)} \"{relic.DisplayName}\" (unsecured)"), relic.Rarity));
                AddToSatchel(p, relic, ev, trades);
                AddHint(p, Hint.FirstDrop, ev);
                AdvanceRelicBounties(p, relic, ev);
            }
            if (isNightmare) AdvanceBounty(p, BountyKind.NightmareHunter, 1, false, ev);
            switch (tier)
            {
                case MonsterTier.MiniBoss: AdvanceBounty(p, BountyKind.EliteHunter, 1, false, ev); break;
                case MonsterTier.Boss: AdvanceBounty(p, BountyKind.Bossbane, 1, false, ev); break;
                default: AdvanceBounty(p, BountyKind.Slayer, 1, false, ev); break;
            }
            ev.AddRange(AddXp(p, reward.Xp));
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        private static void AddToSatchel(Profile p, Relic relic, List<GameEvent> ev, TradeLedger trades = null)
        {
            var run = p.Run;
            run.Satchel.Add(relic);
            if (run.Satchel.Count <= Workshop.SatchelCapacity(p)) return;
            var worst = run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderBy(r => r.Score).FirstOrDefault();
            if (worst == null) return; // 全品予約中なら、容量より予約対象の保護を優先する。
            run.Satchel.Remove(worst);
            run.SatchelShards += Content.SalvageShards(worst.Rarity);
            ev.Add(new GameEvent(EventKind.Info, Loc.T(
                $"持ち歩ける数を超えたため、一番弱い「{worst.DisplayName}」を欠片に換えました。",
                $"Satchel full: \"{worst.DisplayName}\" was turned into shards.")));
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
        public static bool ShouldOfferSecurePoint(Profile p)
        {
            var run = p.Run;
            if (run == null || run.AwaitingChoice) return false;
            return run.Kills > 0 || run.HasUnsecured || run.Heat > run.StartDepth;
        }

        /// <summary>確保地点（新しいゾーン）に着いた。選ぶまで装備を整えられる。</summary>
        public static List<GameEvent> ReachSecurePoint(Profile p)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            run.AwaitingChoice = true;
            run.OfferedPacts.Clear();
            var rng = p.TakeRng();
            run.OfferedPacts.AddRange(Pacts.Offer(rng, run.Pacts, Workshop.PactsOffered(p)));
            run.OfferedEvent = DreamEvents.Roll(rng, p);
            p.StoreRng(rng);
            AddHint(p, Hint.FirstSecurePoint, ev);
            return ev;
        }

        /// <summary>確保する。未確保品を保管庫へ移し、深度に応じて欠片の上乗せを受け、深度を0に戻す。</summary>
        public static List<GameEvent> Secure(Profile p)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            int heat = run.Heat;
            int relics = run.Satchel.Count;
            int bonusShards = run.SatchelShards * heat / 4;
            if (Pacts.Sum(run.Pacts).DoubleDepthBonus) bonusShards *= 2;
            int shards = run.SatchelShards + bonusShards;
            int tuning = run.SatchelTuning;

            var overflow = new List<Relic>();
            foreach (var r in run.Satchel.OrderByDescending(r => r.Score))
            {
                if (p.Stash.Count < Workshop.StashCapacity(p)) p.Stash.Add(r);
                else overflow.Add(r);
            }
            foreach (var r in overflow) shards += Content.SalvageShards(r.Rarity);

            p.AddMaterial(Materials.Shard, shards);
            p.AddMaterial(Materials.Tuning, tuning);
            int stored = relics - overflow.Count;
            run.RelicsSecured += stored;
            run.ShardsSecured += shards;
            run.Satchel.Clear();
            run.SatchelShards = 0;
            run.SatchelTuning = 0;
            run.EventDropBonus = 0;
            run.EventLuck = 0;
            run.Heat = run.StartDepth;
            run.AwaitingChoice = false;
            int pacts = run.Pacts.Count;
            run.Pacts.Clear();
            run.OfferedPacts.Clear();
            run.OfferedEvent = DreamEvent.None;
            run.SecuredCount++;
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
                b.Progress = Math.Min(b.Target, b.Progress + amount);
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
            p.Stats.BountiesDone++;
            double mult = DailyDream.Get(p.Run.DailyId)?.BountyMult ?? 1.0;
            int shards = (int)Math.Round(b.RewardShards * mult);
            int tuning = (int)Math.Round(b.RewardTuning * mult);
            if (secured)
            {
                p.AddMaterial(Materials.Shard, shards);
                p.AddMaterial(Materials.Tuning, tuning);
                p.Run.ShardsSecured += shards;
            }
            else
            {
                p.Run.SatchelShards += shards;
                p.Run.SatchelTuning += tuning;
            }
            ev.Add(new GameEvent(EventKind.Bounty, Loc.T(
                $"依頼「{b.Describe()}」を達成しました。{b.RewardText(mult)}" + (secured ? "" : "（欠片と調律石はまだ持ち帰っていません）"),
                $"Bounty complete: {b.Describe()} ({b.RewardText(mult)}" + (secured ? ")" : ", unsecured)"))));
            ev.AddRange(AddXp(p, (int)Math.Round(b.RewardXp * mult)));
            AddHint(p, Hint.FirstBounty, ev);
            ev.AddRange(Feats.Check(p));
        }

        /// <summary>確保を見送り、さらに深く潜る。ドロップ率とレア度が上がるが、被ダメージも増える。</summary>
        public static List<GameEvent> Delve(Profile p, Pact pact = Pact.None)
        {
            var ev = new List<GameEvent>();
            var run = p.Run;
            if (run == null) return ev;
            if (pact != Pact.None)
            {
                if (!run.OfferedPacts.Contains(pact)) throw new InvalidOperationException(Loc.T("その契約は提示されていません。", "That pact is not on offer."));
                run.Pacts.Add(pact);
                p.Stats.PactsSworn++;
                AdvanceBounty(p, BountyKind.PactBearer, 1, false, ev);
                var d = Pacts.Get(pact);
                ev.Add(new GameEvent(EventKind.Delved, Loc.T($"悪夢の契約「{d.Name}」を結びました。{d.Description}", $"Swore the nightmare pact \"{d.Name}\". {d.Description}")));
            }
            run.OfferedPacts.Clear();
            run.OfferedEvent = DreamEvent.None;
            run.Heat = Loot.ClampHeat(run.Heat + 1);
            run.PeakHeat = Math.Max(run.PeakHeat, run.Heat);
            run.AwaitingChoice = false;
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
                ev.AddRange(Secure(p));
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
            p.Run = null;
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        public static GameEvent BuyUpgrade(Profile p, Upgrade u)
        {
            if (p.Run != null) throw new InvalidOperationException(Loc.T("工房は遠征の外でのみ使えます。", "The workshop is only available outside expeditions."));
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
                    GiveMerchantRelic(p, rng, ev, trades);
                    break;
                }
                case DreamEvent.Fountain:
                {
                    var sacrifice = run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderBy(r => r.Score).First();
                    run.Satchel.Remove(sacrifice);
                    var target = run.Satchel.Where(r => r.Enhance < Content.MaxEnhance && (trades == null || !trades.IsReserved(r.Uid))).OrderByDescending(r => r.Score).First();
                    target.Enhance++;
                    ev.Add(new GameEvent(EventKind.Info, Loc.T($"泉に「{sacrifice.DisplayName}」を捧げると、「{target.PlainName}」が+{target.Enhance}に強化されました。",
                        $"Offered \"{sacrifice.DisplayName}\"; \"{target.PlainName}\" was enhanced to +{target.Enhance}."), target.Rarity));
                    break;
                }
                case DreamEvent.Chalice:
                {
                    int bet = run.SatchelShards;
                    if (rng.Chance(0.5))
                    {
                        run.SatchelShards += bet;
                        ev.Add(new GameEvent(EventKind.Secured, Loc.T($"賭けに勝ちました！ まだ持ち帰っていない欠片が{bet}から{bet * 2}に増えました。", $"You won! Unsecured shards {bet} -> {bet * 2}")));
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
                    var target = run.Satchel.Where(r => r.Enhance < Content.MaxEnhance && (trades == null || !trades.IsReserved(r.Uid))).OrderByDescending(r => r.Score).First();
                    run.SatchelShards -= 20;
                    target.Enhance++;
                    ev.Add(new GameEvent(EventKind.Info, Loc.T($"鍛冶の祠で「{target.PlainName}」を+{target.Enhance}に強化しました。",
                        $"The Forge Shrine enhanced \"{target.PlainName}\" to +{target.Enhance}."), target.Rarity));
                    break;
                }
                case DreamEvent.TwinMirror:
                {
                    var source = run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderByDescending(r => r.Score).First();
                    var rarity = source.Rarity == Rarity.Legendary ? Rarity.Epic : source.Rarity;
                    var relic = Loot.RollBaseRelic(rng, source.Base, rarity, source.ItemLevel);
                    run.SatchelShards -= 30;
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
                        p.Focus ?? DailyDream.Get(run.DailyId)?.FeaturedLine, p.Stash, run.Satchel);
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
                    run.Heat = Loot.ClampHeat(run.Heat + 1);
                    run.PeakHeat = Math.Max(run.PeakHeat, run.Heat);
                    run.SatchelShards += 40;
                    ev.Add(new GameEvent(EventKind.Delved, Loc.T($"勇気の門をくぐり、潜行が{run.Heat}になりました。まだ持ち帰っていない欠片が40増えました。",
                        $"Gate of Courage: delve {run.Heat}, +40 unsecured shards")));
                    break;
                case DreamEvent.Archive:
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    ev.AddRange(AddXp(p, 40 + 20 * run.Heat));
                    break;
                case DreamEvent.LuckyStar:
                    run.EventLuck += DreamEvents.LuckyStarLuck;
                    ev.Add(new GameEvent(EventKind.Info, DreamEvents.Describe(e, p)));
                    break;
            }
            p.Stats.EventsUsed++;
            p.StoreRng(rng);
            run.OfferedEvent = DreamEvent.None;
            AdvanceBounty(p, BountyKind.EventTaker, 1, false, ev);
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        /// <summary>ホストが支払いを確定した商人の遺物を渡す。出来事や確保地点が終わっていても付与する。</summary>
        public static List<GameEvent> GrantPaidMerchant(Profile p, TradeLedger trades = null)
        {
            var ev = new List<GameEvent>();
            var rng = p.TakeRng();
            GiveMerchantRelic(p, rng, ev, trades);
            p.StoreRng(rng);
            p.Stats.EventsUsed++;
            if (p.Run?.OfferedEvent == DreamEvent.Merchant) p.Run.OfferedEvent = DreamEvent.None;
            AdvanceBounty(p, BountyKind.EventTaker, 1, false, ev);
            ev.AddRange(Feats.Check(p));
            return ev;
        }

        private static void GiveMerchantRelic(Profile p, Rng rng, List<GameEvent> ev, TradeLedger trades)
        {
            var run = p.Run;
            var rarity = Loot.RollRarity(rng, 1.0 + Loot.HeatLuck * (run?.Heat ?? 0), true, Rarity.Uncommon);
            var relic = Loot.RollRelic(rng, rarity, p.BestItemLevel, null,
                p.Focus ?? DailyDream.Get(run?.DailyId ?? 0)?.FeaturedLine, p.Stash, run?.Satchel);
            p.Codex.Add(relic.UniqueId ?? relic.BaseId);
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
            p.Codex.Add(relic.UniqueId ?? relic.BaseId);
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
            AdvanceBounty(p, action, 1, false, ev);
            return ev;
        }

        /// <summary>確保地点でドリームダストを欠片へ換える（ダストはホストが支払い済み）。欠片はそのまま保管庫側へ。</summary>
        public static GameEvent ConvertDust(Profile p, int dustPaid)
        {
            if (p.Run == null || !p.Run.AwaitingChoice) throw new InvalidOperationException(Loc.T("確保地点でのみ換えられます。", "Only at a secure point."));
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

        /// <summary>分解失敗の預かり品を返す。uid が null なら台帳の無い起動時などに全品を返す。</summary>
        public static List<GameEvent> RestorePendingSalvage(Profile p, string uid = null)
        {
            var ev = new List<GameEvent>();
            if (uid != null && p.Run?.Satchel.Find(r => r.Uid == uid) != null) return ev;
            bool restored = false;
            for (int i = 0; i < p.PendingSalvage.Count; i++)
            {
                var pending = p.PendingSalvage[i];
                if (uid != null && pending.Relic.Uid != uid) continue;
                p.PendingSalvage.RemoveAt(i--);
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
                p.AddMaterial(Materials.Shard, Content.SalvageShards(worst.Rarity));
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

        /// <summary>狙い系統を選ぶ（null で解除）。遠征中は変えられない。</summary>
        public static void SetFocus(Profile p, Line? focus)
        {
            if (p.Run != null) throw new InvalidOperationException(Loc.T("狙い系統は遠征の外でのみ変更できます。", "Focus can only be changed outside expeditions."));
            p.Focus = focus;
        }

        public static List<GameEvent> AddXp(Profile p, int amount)
        {
            var ev = new List<GameEvent>();
            if (amount <= 0 || p.DreamLevel >= Content.MaxDreamLevel) return ev;
            p.DreamXp += amount;
            while (p.DreamLevel < Content.MaxDreamLevel && p.DreamXp >= Content.XpToNext(p.DreamLevel))
            {
                p.DreamXp -= Content.XpToNext(p.DreamLevel);
                p.DreamLevel++;
                ev.Add(new GameEvent(EventKind.LevelUp, Loc.T(
                    $"夢のレベルが{p.DreamLevel}に上がりました！ 星図のポイントが1増えました。",
                    $"Dream Level {p.DreamLevel}! +1 star map point")));
                AddHint(p, Hint.TalentPoints, ev);
            }
            if (p.DreamLevel >= Content.MaxDreamLevel) p.DreamXp = 0;
            return ev;
        }

        // ───────────── 装着 ─────────────

        /// <summary>遠征中は確保地点の選択待ちか、ゲームの外でのみ装備を変更できる。</summary>
        public static bool LoadoutLocked(Profile p, bool inGame) => p.Run != null && !p.Run.AwaitingChoice && inGame;

        public static IReadOnlyList<GameEvent> Equip(Profile p, string heroKey, string uid, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            p.Hero(heroKey).Equipped[(int)r.Slot] = uid;
            return Feats.Check(p);
        }

        public static IReadOnlyList<GameEvent> Unequip(Profile p, string heroKey, Slot slot)
        {
            p.Hero(heroKey).Equipped[(int)slot] = null;
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

        public static GameEvent Enhance(Profile p, string uid, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            if (r.Enhance >= Content.MaxEnhance) throw new InvalidOperationException(Loc.T("これ以上強化できません。", "Already at maximum enhancement."));
            int cost = Content.EnhanceCost(r.Enhance);
            if (p.Material(Materials.Shard) < cost) throw new InvalidOperationException(Loc.T($"欠片が足りません（{cost}必要）。", $"Not enough shards ({cost} needed)."));
            p.AddMaterial(Materials.Shard, -cost);
            r.Enhance++;
            return new GameEvent(EventKind.Info, Loc.T($"「{r.PlainName}」を+{r.Enhance}に強化しました。", $"Enhanced \"{r.PlainName}\" to +{r.Enhance}."), r.Rarity);
        }

        public static GameEvent Retune(Profile p, string uid, int affixIndex, TradeLedger trades = null)
        {
            RequireUnreserved(trades, uid);
            var r = p.FindStash(uid) ?? throw new InvalidOperationException(Loc.T("保管庫にない遺物です。", "That relic is not in your stash."));
            if (affixIndex < 0 || affixIndex >= r.Affixes.Count) throw new InvalidOperationException(Loc.T("特性を選んでください。", "Choose an affix."));
            if (r.Retunes >= Content.MaxRetunes) throw new InvalidOperationException(Loc.T("再調律の回数を使い切りました。", "No retunes left."));
            int cost = Content.RetuneCost(r.Retunes);
            if (p.Material(Materials.Tuning) < cost) throw new InvalidOperationException(Loc.T($"調律石が足りません（{cost}必要）。", $"Not enough tuning stones ({cost} needed)."));
            var exclude = new HashSet<Stat> { r.Base.ImplicitStat };
            foreach (var a in r.Affixes) exclude.Add(a.Stat);
            var rng = p.TakeRng();
            var line = Loot.RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, exclude);
            if (line == null)
            {
                // 候補が尽きた場合は同じ能力値で数値だけ引き直す。
                exclude.Remove(r.Affixes[affixIndex].Stat);
                line = Loot.RollAffix(rng, r.Slot, r.Rarity, r.ItemLevel, exclude);
            }
            p.StoreRng(rng);
            p.AddMaterial(Materials.Tuning, -cost);
            var old = r.Affixes[affixIndex];
            r.Affixes[affixIndex] = line;
            r.Retunes++;
            return new GameEvent(EventKind.Info, Loc.T(
                $"再調律しました：{Content.FormatStat(old.Stat, old.Value)} → {Content.FormatStat(line.Stat, line.Value)}",
                $"Retuned: {Content.FormatStat(old.Stat, old.Value)} -> {Content.FormatStat(line.Stat, line.Value)}"), r.Rarity);
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
            var relic = Loot.RollRelic(rng, rarity, p.BestItemLevel, slot, ownedRelics: p.Stash, unsecuredRelics: p.Run?.Satchel);
            p.StoreRng(rng);
            p.AddMaterial(Materials.Shard, -shards);
            p.AddMaterial(Materials.Tuning, -tuning);
            p.Stash.Add(relic);
            p.Codex.Add(relic.BaseId);
            return new GameEvent(EventKind.Drop, Loc.T(
                $"{Content.RarityName(relic.Rarity)}「{relic.DisplayName}」を作りました。",
                $"Crafted {Content.RarityName(relic.Rarity)} \"{relic.DisplayName}\""), relic.Rarity);
        }

        public static int TransmuteCost(Rarity r) => 10 * ((int)r + 1);

        /// <summary>合成の材料になる遺物（鍵なし・どこにも装着していない・同じレア度）を弱い順に。</summary>
        public static List<Relic> TransmuteCandidates(Profile p, Rarity r, TradeLedger trades = null)
        {
            return p.Stash.Where(x => x.Rarity == r && !x.Locked && !p.IsEquippedAnywhere(x.Uid) && (trades == null || !trades.IsReserved(x.Uid))).OrderBy(x => x.Score).ToList();
        }

        /// <summary>同じレア度の遺物3つ（弱い順）を1つ上のレア度の遺物1つにする。エピック3つからは固有品。</summary>
        public static GameEvent Transmute(Profile p, Rarity r, TradeLedger trades = null)
        {
            if (r >= Rarity.Legendary) throw new InvalidOperationException(Loc.T("固有品は合成できません。", "Legendaries cannot be transmuted."));
            var parts = TransmuteCandidates(p, r, trades).Take(3).ToList();
            if (parts.Count < 3) throw new InvalidOperationException(Loc.T("材料が3つ足りません（鍵なし・未装着の同じレア度）。", "Need 3 unlocked, unequipped relics of the same rarity."));
            int cost = TransmuteCost(r);
            if (p.Material(Materials.Shard) < cost) throw new InvalidOperationException(Loc.T($"欠片が足りません（{cost}必要）。", $"Not enough shards ({cost} needed)."));
            int ilvl = parts.Max(x => x.ItemLevel);
            foreach (var x in parts) p.Stash.Remove(x);
            p.AddMaterial(Materials.Shard, -cost);
            var rng = p.TakeRng();
            var result = Loot.RollRelic(rng, r + 1, ilvl, null, p.Focus, p.Stash, p.Run?.Satchel);
            p.StoreRng(rng);
            p.Stash.Add(result);
            p.Codex.Add(result.UniqueId ?? result.BaseId);
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

        /// <summary>到達刻印を選べるか。旅人の刻印はツリーに6pt＋熟練度3、汎用は同じルートに6pt。</summary>
        public static bool KeystoneUnlocked(Profile p, string heroKey, TalentDef key)
        {
            var h = p.Hero(heroKey);
            if (key.HeroKey != null)
            {
                if (key.HeroKey != heroKey) return false;
                int ranks = 0;
                foreach (var kv in h.Talents)
                    if (Content.TryGetTalent(kv.Key, out var t) && t.HeroKey == heroKey && !t.IsKeystone) ranks += kv.Value;
                return ranks >= Content.KeystoneRouteRequirement && Mastery.Level(h.Kills) >= HeroSigils.KeystoneMastery;
            }
            if (HeroSigils.HasTree(heroKey)) return false;
            return RouteRanks(h, key.Route) >= Content.KeystoneRouteRequirement;
        }

        public static int RouteRanks(HeroState h, Line route)
        {
            int n = 0;
            foreach (var kv in h.Talents)
                if (Content.TryGetTalent(kv.Key, out var t) && t.HeroKey == null && t.Route == route && !t.IsKeystone) n += kv.Value;
            return n;
        }

        public static int SpentPoints(HeroState h)
        {
            int n = 0;
            foreach (var kv in h.Talents) n += kv.Value;
            if (h.Keystone != null) n += Content.KeystoneCost;
            return n;
        }

        public static int FreePoints(Profile p, string heroKey) => p.TalentPoints - SpentPoints(p.Hero(heroKey));

        public static void AddTalentRank(Profile p, string heroKey, string talentId)
        {
            if (!Content.TryGetTalent(talentId, out var t) || t.IsKeystone) throw new InvalidOperationException("未知のノード: " + talentId);
            if (!BelongsTo(t, heroKey)) throw new InvalidOperationException(Loc.T("この旅人のノードではありません。", "That node is not in this Traveler's tree."));
            var h = p.Hero(heroKey);
            int cur = h.Talents.TryGetValue(talentId, out int c) ? c : 0;
            if (cur >= t.MaxRank) throw new InvalidOperationException(Loc.T("最大段階です。", "Already at max rank."));
            if (FreePoints(p, heroKey) < 1) throw new InvalidOperationException(Loc.T("ポイントが足りません。", "Not enough points."));
            h.Talents[talentId] = cur + 1;
        }

        /// <summary>刻印（到達ノード）を選ぶ。そのルートに6ポイント以上必要。付け替えは追加費用なし。</summary>
        public static void SetKeystone(Profile p, string heroKey, string keystoneId)
        {
            var h = p.Hero(heroKey);
            if (keystoneId == null)
            {
                h.Keystone = null;
                return;
            }
            if (!Content.TryGetTalent(keystoneId, out var t) || !t.IsKeystone) throw new InvalidOperationException("未知の刻印: " + keystoneId);
            if (!BelongsTo(t, heroKey)) throw new InvalidOperationException(Loc.T("この旅人の刻印ではありません。", "That keystone is not in this Traveler's tree."));
            if (!KeystoneUnlocked(p, heroKey, t))
                throw new InvalidOperationException(t.HeroKey != null
                    ? Loc.T($"このツリーに{Content.KeystoneRouteRequirement}ポイント以上振り、熟練度を{HeroSigils.KeystoneMastery}以上にする必要があります。", $"Requires {Content.KeystoneRouteRequirement}+ points and mastery {HeroSigils.KeystoneMastery}+.")
                    : Loc.T($"{Content.LineName(t.Route)}に{Content.KeystoneRouteRequirement}ポイント以上必要です。", $"Requires {Content.KeystoneRouteRequirement}+ points in {Content.LineName(t.Route)}."));
            if (h.Keystone == null && FreePoints(p, heroKey) < Content.KeystoneCost)
                throw new InvalidOperationException(Loc.T($"ポイントが足りません（{Content.KeystoneCost}必要）。", $"Not enough points ({Content.KeystoneCost} needed)."));
            h.Keystone = keystoneId;
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
            h.Keystone = null;
        }
    }
}
