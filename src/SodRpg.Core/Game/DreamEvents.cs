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
                    return Loc.T($"欠片{MerchantCost(heat)}で正体不明の遺物を買う（アンコモン以上・深いほど良い）",
                        $"Buy a mystery relic for {MerchantCost(heat)} shards (Uncommon+, better when deeper)");
                case DreamEvent.Fountain:
                    return Loc.T("未確保の最も弱い遺物を捧げ、最も強い未確保の遺物を+1強化", "Sacrifice your weakest unsecured relic to enhance your best one by +1");
                case DreamEvent.Chalice:
                    return Loc.T($"未確保の欠片{run?.SatchelShards ?? 0}を賭ける：50%で2倍、外れると失う",
                        $"Wager your {run?.SatchelShards ?? 0} unsecured shards: 50% to double, else lose them");
                case DreamEvent.Lantern:
                    return Loc.T("遺失物を1つ、この場で取り戻す（未確保）", "Recover one lost relic right here (unsecured)");
                default: return "";
            }
        }

        /// <summary>今この出来事を使えるか（理由つき）。</summary>
        public static bool CanUse(Profile p, DreamEvent e, out string reason)
        {
            reason = null;
            var run = p.Run;
            if (run == null || run.OfferedEvent != e || e == DreamEvent.None)
            {
                reason = Loc.T("この出来事は今はありません。", "That event is not available.");
                return false;
            }
            switch (e)
            {
                case DreamEvent.Merchant:
                    if (p.Material(Materials.Shard) < MerchantCost(run.Heat)) reason = Loc.T("欠片が足りません。", "Not enough shards.");
                    break;
                case DreamEvent.Fountain:
                    if (run.Satchel.Count < 2 || run.Satchel.All(r => r.Enhance >= Content.MaxEnhance))
                        reason = Loc.T("未確保の遺物が2つ以上必要です（強化可能なもの）。", "Need 2+ unsecured relics (one enhanceable).");
                    break;
                case DreamEvent.Chalice:
                    if (run.SatchelShards <= 0) reason = Loc.T("賭ける欠片がありません。", "No shards to wager.");
                    break;
                case DreamEvent.Lantern:
                    if (p.LostAndFound.Count == 0) reason = Loc.T("遺失物がありません。", "No lost relics.");
                    break;
            }
            return reason == null;
        }

        /// <summary>確保地点に着いたとき、出来事を抽選する。</summary>
        public static DreamEvent Roll(Rng rng, Profile p)
        {
            if (!rng.Chance(OfferChance)) return DreamEvent.None;
            var pool = new List<DreamEvent> { DreamEvent.Merchant, DreamEvent.Fountain, DreamEvent.Chalice };
            if (p.LostAndFound.Count > 0) pool.Add(DreamEvent.Lantern);
            return pool[rng.Range(0, pool.Count - 1)];
        }
    }
}
