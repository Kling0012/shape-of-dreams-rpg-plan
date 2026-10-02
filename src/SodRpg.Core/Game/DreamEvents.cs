using System;
using System.Collections.Generic;
using System.Linq;

namespace SodRpg.Core.Game
{
    /// <summary>確保地点で起きることがある出来事（Slay the Spire の「？」部屋）。</summary>
    public enum DreamEvent
    {
        None = 0,
        /// <summary>夢の商人：欠片で正体不明の遺物を買う。</summary>
        Merchant = 1,
        /// <summary>祈りの泉：未確保の最も弱い遺物を捧げ、最も強い遺物の強化段階を+1。</summary>
        Fountain = 2,
        /// <summary>賭けの杯：未確保の欠片を賭ける。半分の確率で2倍、外れで失う。</summary>
        Chalice = 3,
        /// <summary>迷い人の灯：遺失物を1つその場で取り戻す。</summary>
        Lantern = 4,
        ForgeShrine = 5,
        TwinMirror = 6,
        Stargazer = 7,
        Cauldron = 8,
        Tapir = 9,
        CourageGate = 10,
        Archive = 11,
        LuckyStar = 12,
    }

    public static class DreamEvents
    {
        /// <summary>確保地点で出来事が現れる確率。</summary>
        public const double OfferChance = 0.5;

        public static int MerchantCost(int heat) => 50 + 10 * Loot.ClampHeat(heat);

        public static string Name(DreamEvent e)
        {
            switch (e)
            {
                case DreamEvent.Merchant: return Loc.T("夢の商人", "Dream Merchant");
                case DreamEvent.Fountain: return Loc.T("祈りの泉", "Fountain of Prayer");
                case DreamEvent.Chalice: return Loc.T("賭けの杯", "Gambler's Chalice");
                case DreamEvent.Lantern: return Loc.T("迷い人の灯", "Lantern of the Lost");
                case DreamEvent.ForgeShrine: return Loc.T("鍛冶の祠", "Forge Shrine");
                case DreamEvent.TwinMirror: return Loc.T("双子の鏡", "Twin Mirror");
                case DreamEvent.Stargazer: return Loc.T("星読みの塔", "Stargazer's Tower");
                case DreamEvent.Cauldron: return Loc.T("錬金の釜", "Alchemist's Cauldron");
                case DreamEvent.Tapir: return Loc.T("夢喰いの獏", "Dream Tapir");
                case DreamEvent.CourageGate: return Loc.T("勇気の門", "Gate of Courage");
                case DreamEvent.Archive: return Loc.T("記憶の書庫", "Archive of Memories");
                case DreamEvent.LuckyStar: return Loc.T("幸運の星", "Lucky Star");
                default: return "";
            }
        }

        public static string Describe(DreamEvent e, Profile p)
        {
            var run = p.Run;
            int heat = run?.Heat ?? 0;
            switch (e)
            {
                case DreamEvent.Merchant:
                    return Loc.T($"ゴールドで中身の分からない遺物を1つ買えます。必ずアンコモン以上で、深く潜っているほど良い物が出ます（基本価格{Economy.MerchantGoldBase(heat)}G）。",
                        $"Buy a mystery relic with gold (Uncommon+, better when deeper; base {Economy.MerchantGoldBase(heat)}G)");
                case DreamEvent.Fountain:
                    return Loc.T("まだ持ち帰っていない遺物のうち一番弱い物を捧げると、一番強い物が+1強化されます。", "Sacrifice your weakest unsecured relic to enhance your best one by +1");
                case DreamEvent.Chalice:
                    return Loc.T($"まだ持ち帰っていない欠片{run?.SatchelShards ?? 0}を賭けます。50%の確率で2倍になり、外れるとすべて失います。",
                        $"Wager your {run?.SatchelShards ?? 0} unsecured shards: 50% to double, else lose them");
                case DreamEvent.Lantern:
                    return Loc.T("遺失物を1つ、この場で取り戻せます（取り戻した物は、確保するまで未確保のままです）。", "Recover one lost relic right here (unsecured)");
                case DreamEvent.ForgeShrine:
                    return Loc.T("まだ持ち帰っていない欠片を20払うと、まだ持ち帰っていない遺物のうち一番強い物が+1強化されます。",
                        "Pay 20 unsecured shards to enhance your best unsecured relic by +1.");
                case DreamEvent.TwinMirror:
                    return Loc.T("まだ持ち帰っていない欠片を30払うと、まだ持ち帰っていない遺物のうち一番強い物と同じ種類・同じレア度の遺物が、もう1つ手に入ります（伝説はエピックになります）。",
                        "Pay 30 unsecured shards to get another relic of the same type and rarity as your best unsecured relic (legendaries become epic).");
                case DreamEvent.Stargazer:
                    return Loc.T("次に確保するまで、遺物が50%多く落ちます。",
                        "Until you next secure, relics drop 50% more often.");
                case DreamEvent.Cauldron:
                    return Loc.T("まだ持ち帰っていないコモンかアンコモンの遺物を3つ溶かして、1つ上のレア度の遺物を1つ作ります。",
                        "Melt 3 unsecured Common or Uncommon relics into one relic of the next rarity.");
                case DreamEvent.Tapir:
                    return Loc.T("まだ持ち帰っていない遺物をすべて獏に食べさせ、1つにつき欠片12と、3つにつき調律石1をもらいます（どちらも未確保）。",
                        "Feed all your unsecured relics to the tapir: 12 shards each and 1 tuning stone per 3 relics (both unsecured).");
                case DreamEvent.CourageGate:
                    return Loc.T("潜行が1段深くなる代わりに、まだ持ち帰っていない欠片が40増えます。",
                        "Your delve goes one level deeper, and you gain 40 unsecured shards.");
                case DreamEvent.Archive:
                    return Loc.T($"夢のレベルの経験値を{40 + 20 * heat}もらいます。",
                        $"Gain {40 + 20 * heat} Dream Level experience.");
                case DreamEvent.LuckyStar:
                    return Loc.T("次に確保するまで、レア度の高い遺物が出やすくなります。",
                        "Until you next secure, rarer relics drop more often.");
                default: return "";
            }
        }

        /// <summary>今この出来事を使えるか（理由つき）。</summary>
        public static bool CanUse(Profile p, DreamEvent e, out string reason) => CanUse(p, e, false, out reason);

        /// <summary>今この出来事を使えるか。goldPaid=true なら夢の商人の代金はホストがゴールドで受け取り済み。</summary>
        public static bool CanUse(Profile p, DreamEvent e, bool goldPaid, out string reason, TradeLedger trades = null)
        {
            reason = null;
            var run = p.Run;
            if (run == null || run.OfferedEvent != e || e == DreamEvent.None)
            {
                reason = Loc.T("この出来事は今はありません。", "That event is not available.");
                return false;
            }
            reason = UnavailableReason(p, e, goldPaid, trades);
            return reason == null;
        }

        private static string UnavailableReason(Profile p, DreamEvent e, bool goldPaid, TradeLedger trades = null)
        {
            var run = p.Run;
            string reason = null;
            if (run == null) return Loc.T("遠征中のみ使えます。", "Only during an expedition.");
            switch (e)
            {
                case DreamEvent.Merchant:
                    if (!goldPaid && p.Material(Materials.Shard) < MerchantCost(run.Heat)) reason = Loc.T("欠片が足りません。", "Not enough shards.");
                    break;
                case DreamEvent.Fountain:
                    if (run.Satchel.Count(r => trades == null || !trades.IsReserved(r.Uid)) < 2
                        || !run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderBy(r => r.Score).Skip(1).Any(r => r.Enhance < Content.MaxEnhance))
                        reason = Loc.T("まだ持ち帰っていない遺物が2つ以上必要です（そのうち1つは、まだ強化できる物）。", "Need 2+ unsecured relics (one enhanceable).");
                    break;
                case DreamEvent.Chalice:
                    if (run.SatchelShards <= 0) reason = Loc.T("賭ける欠片がありません。", "No shards to wager.");
                    break;
                case DreamEvent.Lantern:
                    if (p.LostAndFound.Count == 0) reason = Loc.T("遺失物がありません。", "No lost relics.");
                    break;
                case DreamEvent.ForgeShrine:
                    if (run.SatchelShards < 20) reason = Loc.T("未確保の欠片が20必要です。", "Need 20 unsecured shards.");
                    else if (!run.Satchel.Any(r => r.Enhance < Content.MaxEnhance && (trades == null || !trades.IsReserved(r.Uid))))
                        reason = Loc.T("まだ強化できる未確保の遺物が必要です。", "Need an enhanceable unsecured relic.");
                    break;
                case DreamEvent.TwinMirror:
                    if (run.SatchelShards < 30) reason = Loc.T("未確保の欠片が30必要です。", "Need 30 unsecured shards.");
                    else if (!run.Satchel.Any(r => trades == null || !trades.IsReserved(r.Uid))) reason = Loc.T("未確保の遺物が必要です。", "Need an unsecured relic.");
                    break;
                case DreamEvent.Cauldron:
                    if (run.Satchel.Count(r => (r.Rarity == Rarity.Common || r.Rarity == Rarity.Uncommon) && (trades == null || !trades.IsReserved(r.Uid))) < 3)
                        reason = Loc.T("コモンかアンコモンの未確保の遺物が3つ必要です。", "Need 3 unsecured Common or Uncommon relics.");
                    break;
                case DreamEvent.Tapir:
                    if (!run.Satchel.Any(r => trades == null || !trades.IsReserved(r.Uid))) reason = Loc.T("未確保の遺物が必要です。", "Need an unsecured relic.");
                    break;
                case DreamEvent.CourageGate:
                    if (run.Heat >= Content.MaxHeat) reason = Loc.T("これ以上深く潜れません。", "Cannot delve any deeper.");
                    break;
                case DreamEvent.Stargazer:
                case DreamEvent.Archive:
                case DreamEvent.LuckyStar:
                    break;
                default:
                    reason = Loc.T("この出来事は今はありません。", "That event is not available.");
                    break;
            }
            return reason;
        }

        /// <summary>確保地点に着いたとき、出来事を抽選する。</summary>
        public static DreamEvent Roll(Rng rng, Profile p)
        {
            if (p.Run == null || !rng.Chance(OfferChance)) return DreamEvent.None;
            var pool = new List<DreamEvent>();
            for (int value = (int)DreamEvent.Merchant; value <= (int)DreamEvent.LuckyStar; value++)
            {
                var e = (DreamEvent)value;
                if (UnavailableReason(p, e, e == DreamEvent.Merchant) == null) pool.Add(e);
            }
            return pool.Count == 0 ? DreamEvent.None : pool[rng.Range(0, pool.Count - 1)];
        }
    }
}
