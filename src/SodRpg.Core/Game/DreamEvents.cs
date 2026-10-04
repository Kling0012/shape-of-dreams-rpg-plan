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
        MemoryWell = 13,
        ShadowExchange = 14,
        LostMausoleum = 15,
        RelicWager = 16,
        TemperingAltar = 17,
        StoneBroker = 18,
        ShardKiln = 19,
        StarOffering = 20,
        DreamOffering = 21,
        AbyssalChest = 22,
        RelicExchange = 23,
        SealedVault = 24,
        PowerCrucible = 25,
    }

    public static class DreamEvents
    {
        /// <summary>確保地点で出来事が現れる確率。</summary>
        public const double OfferChance = 0.5;
        /// <summary>星読みの塔：次に確保するまでの遺物ドロップ率の上乗せ（説明文と効果の両方で使う）。</summary>
        public const double StargazerDropBonus = 0.3;
        /// <summary>幸運の星：次に確保するまでの幸運（レア度の上振れ）。</summary>
        public const double LuckyStarLuck = 0.4;

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
                case DreamEvent.MemoryWell: return Loc.T("記憶の井戸", "Memory Well");
                case DreamEvent.ShadowExchange: return Loc.T("影の取引所", "Shadow Exchange");
                case DreamEvent.LostMausoleum: return Loc.T("遺失物の霊廟", "Lost Mausoleum");
                case DreamEvent.RelicWager: return Loc.T("遺物の賭場", "Relic Wager");
                case DreamEvent.TemperingAltar: return Loc.T("焼き入れの祭壇", "Tempering Altar");
                case DreamEvent.StoneBroker: return Loc.T("調律石の市場", "Tuning Stone Market");
                case DreamEvent.ShardKiln: return Loc.T("欠片の窯", "Shard Kiln");
                case DreamEvent.StarOffering: return Loc.T("星への供物", "Star Offering");
                case DreamEvent.DreamOffering: return Loc.T("夢への供物", "Dream Offering");
                case DreamEvent.AbyssalChest: return Loc.T("深淵の宝箱", "Abyssal Chest");
                case DreamEvent.RelicExchange: return Loc.T("遺物の交換所", "Relic Exchange");
                case DreamEvent.SealedVault: return Loc.T("封印の保管庫", "Sealed Vault");
                case DreamEvent.PowerCrucible: return Loc.T("固有効果のるつぼ", "Power Crucible");
                default: return "";
            }
        }

        /// <summary>出来事を選ぶボタンの文字。何をするのかが分かる動詞にする。</summary>
        public static string ActionLabel(DreamEvent e)
        {
            switch (e)
            {
                case DreamEvent.Fountain: return Loc.T("一番弱い遺物を捧げる", "Offer the weakest relic");
                case DreamEvent.Chalice: return Loc.T("欠片を賭ける", "Wager the shards");
                case DreamEvent.Lantern: return Loc.T("遺失物を取り戻す", "Recover a lost relic");
                case DreamEvent.ForgeShrine: return Loc.T("欠片20を払って強化する", "Pay 20 shards to enhance");
                case DreamEvent.TwinMirror: return Loc.T("欠片30を払って写し取る", "Pay 30 shards to copy");
                case DreamEvent.Stargazer: return Loc.T("星を読む", "Read the stars");
                case DreamEvent.Cauldron: return Loc.T("3つを溶かす", "Melt three relics");
                case DreamEvent.Tapir: return Loc.T("獏に食べさせる", "Feed the tapir");
                case DreamEvent.CourageGate: return Loc.T("門をくぐる（潜行が1段深くなる）", "Pass the gate (delve +1)");
                case DreamEvent.Archive: return Loc.T("記憶を読む", "Read the memories");
                case DreamEvent.LuckyStar: return Loc.T("星に願う", "Wish upon the star");
                case DreamEvent.MemoryWell: return Loc.T("調律石1で固有効果を交換する", "Spend 1 tuning stone to swap a power");
                case DreamEvent.ShadowExchange: return Loc.T("欠片25で最初の特性を引き直す", "Pay 25 shards to reroll the first affix");
                case DreamEvent.LostMausoleum: return Loc.T("欠片60を払い、強化を失って回収する", "Pay 60 shards and recover with lost enhancement");
                case DreamEvent.RelicWager: return Loc.T("一番強い対象の遺物を賭ける", "Wager the strongest eligible relic");
                case DreamEvent.TemperingAltar: return Loc.T("最初の特性を捧げて2段階強化する", "Sacrifice the first affix to enhance twice");
                case DreamEvent.StoneBroker: return Loc.T("欠片35を調律石2に換える", "Trade 35 shards for 2 tuning stones");
                case DreamEvent.ShardKiln: return Loc.T("調律石2を欠片45に換える", "Trade 2 tuning stones for 45 shards");
                case DreamEvent.StarOffering: return Loc.T("遺物を捧げて星の経験を得る", "Sacrifice a relic for star experience");
                case DreamEvent.DreamOffering: return Loc.T("遺物を捧げて夢の経験を得る", "Sacrifice a relic for dream experience");
                case DreamEvent.AbyssalChest: return Loc.T("欠片30を払い、1段深く潜る", "Pay 30 shards and delve one level deeper");
                case DreamEvent.RelicExchange: return Loc.T("一番弱い対象の遺物を交換する", "Exchange the weakest eligible relic");
                case DreamEvent.SealedVault: return Loc.T("欠片40で遺物1つを保管庫へ送る", "Pay 40 shards to send one relic to the stash");
                case DreamEvent.PowerCrucible: return Loc.T("最後の固有効果を捧げ、最初を交換する", "Sacrifice the last power to swap the first");
                default: return Loc.T("この出来事を選ぶ", "Take this event");
            }
        }

        /// <summary>遺物や欠片を失う、取り返せない出来事。ボタンは2回押しで確定する。</summary>
        public static bool NeedsConfirm(DreamEvent e) =>
            e == DreamEvent.Fountain || e == DreamEvent.Chalice || e == DreamEvent.Cauldron || e == DreamEvent.Tapir
            || (e >= DreamEvent.MemoryWell && e <= DreamEvent.PowerCrucible);

        public static string Describe(DreamEvent e, Profile p)
        {
            string text = DescribeTrade(e, p);
            if (e < DreamEvent.MemoryWell || e > DreamEvent.PowerCrucible) return text;
            return text + Loc.T(" 対象は鍵なし・取引中でない・再調律の候補待ちでない遺物です。井戸以外は未装着のみ。同じ強さなら個体ID順で選びます。",
                " Eligible relics are unlocked, not reserved for a trade and not awaiting a retune choice. Except at the Well, equipped relics are excluded. Score ties use ordinal relic ID order.");
        }

        private static string DescribeTrade(DreamEvent e, Profile p)
        {
            var run = p.Run;
            int heat = run?.Heat ?? 0;
            switch (e)
            {
                case DreamEvent.Merchant:
                    return Loc.T($"ゴールドで中身の分からない遺物を1つ買えます。必ずアンコモン以上で、深く潜っているほど良い物が出ます。",
                        "Buy a mystery relic with gold (Uncommon or better; better when deeper).");
                case DreamEvent.Fountain:
                    return Loc.T("まだ持ち帰っていない遺物のうち一番弱い物を捧げると、一番強い物が+1強化されます。", "Sacrifice your weakest unsecured relic to enhance your best one by +1.");
                case DreamEvent.Chalice:
                    return Loc.T($"まだ持ち帰っていない欠片{run?.SatchelShards ?? 0}を賭けます。50%の確率で2倍になり、外れるとすべて失います。",
                        $"Wager your {run?.SatchelShards ?? 0} unsecured shards: 50% to double them, otherwise you lose them all.");
                case DreamEvent.Lantern:
                    return Loc.T("遺失物のうち一番良い物を1つ、この場で取り戻せます（確保するまでは、まだ持ち帰っていない扱いです）。", "Recover your best lost relic right here (it stays unsecured until you secure).");
                case DreamEvent.ForgeShrine:
                    return Loc.T("まだ持ち帰っていない欠片を20払うと、まだ持ち帰っていない遺物のうち一番強い物が+1強化されます。",
                        "Pay 20 unsecured shards to enhance your best unsecured relic by +1.");
                case DreamEvent.TwinMirror:
                    return Loc.T("まだ持ち帰っていない欠片を30払うと、まだ持ち帰っていない遺物のうち一番強い物と同じ種類・同じレア度の遺物が、もう1つ手に入ります（固有品の場合はエピックになります）。",
                        "Pay 30 unsecured shards to get another relic of the same type and rarity as your best unsecured relic (legendaries become epic).");
                case DreamEvent.Stargazer:
                    return Loc.T($"次に確保するまで、遺物が{(int)Math.Round(StargazerDropBonus * 100)}%多く落ちます。",
                        $"Until you next secure, relics drop {(int)Math.Round(StargazerDropBonus * 100)}% more often.");
                case DreamEvent.Cauldron:
                    return Loc.T("まだ持ち帰っていないコモンかアンコモンの遺物を3つ溶かして、1つ上のレア度の遺物を1つ作ります。",
                        "Melt 3 unsecured Common or Uncommon relics into one relic of the next rarity.");
                case DreamEvent.Tapir:
                    return Loc.T("まだ持ち帰っていないエピック未満の遺物をすべて獏に食べさせ、分解と同じだけの欠片と、3つごとに調律石1をもらいます（欠片はすぐ保管庫に入ります。エピック以上は食べません）。",
                        "Feed all unsecured relics below Epic to the tapir: shards equal to salvaging them, plus 1 tuning stone per 3 relics (the shards go straight to your stash; Epic and above are spared).");
                case DreamEvent.CourageGate:
                    return Loc.T("潜行が1段深くなる代わりに、まだ持ち帰っていない欠片が40増えます。",
                        "Your delve goes one level deeper, and you gain 40 unsecured shards.");
                case DreamEvent.Archive:
                    return Loc.T($"夢のレベルの経験値を{40 + 20 * heat}もらいます。",
                        $"Gain {40 + 20 * heat} Dream Level experience.");
                case DreamEvent.LuckyStar:
                    return Loc.T("次に確保するまで、レア度の高い遺物が少し出やすくなります。",
                        "Until you next secure, rarer relics drop slightly more often.");
                case DreamEvent.MemoryWell:
                    return Loc.T("保管庫の調律石1を払い、この遠征の旅人が装着している対象のうち一番強い遺物の最初の固有効果を、同じ枠の別の効果へ交換します。固有品は対象外。元の効果と値は失います。",
                        "Pay 1 stash tuning stone to replace the first power of this expedition hero's strongest eligible equipped relic with a different power from the same slot. Unique relics are excluded; the original power and value are lost.");
                case DreamEvent.ShadowExchange:
                    return Loc.T("未確保の欠片25を払い、対象のうち一番強い未確保の遺物の最初の特性を、別の能力値へ引き直します。再調律を1回消費し、元の特性は失います。",
                        "Pay 25 unsecured shards to reroll the first affix of your strongest eligible unsecured relic into a different stat. Uses one retune; the old affix is lost.");
                case DreamEvent.LostMausoleum:
                    return Loc.T("未確保の欠片60を払い、対象の遺失物をすべて未確保として回収します。各遺物の強化は0になります（獲得済みの節目は残ります）。全品を入れる鞄の空きが必要です。",
                        "Pay 60 unsecured shards to recover all eligible lost relics as unsecured. Each loses all enhancement (earned milestones remain). Your satchel must have room for all of them.");
                case DreamEvent.RelicWager:
                    return Loc.T("エピック未満・固有品以外の対象から一番強い未確保の遺物を賭けます。50%で同じ土台・レベルの1つ上のレア度の新品に交換し、外れると分解相当の未確保の欠片になります。元の遺物と強化は失います。",
                        "Wager your strongest eligible unsecured non-unique relic below Epic. 50%: a fresh relic of the same base and level, one rarity higher; otherwise, unsecured shards equal to its salvage value. The original relic and enhancements are lost.");
                case DreamEvent.TemperingAltar:
                    return Loc.T("対象のうち一番強い未確保の遺物の最初の特性を失う代わりに、強化を2段階進めます。強化の節目は通常どおり得られます。",
                        "Lose the first affix of your strongest eligible unsecured relic to enhance it twice. Enhancement milestones are granted normally.");
                case DreamEvent.StoneBroker:
                    return Loc.T("未確保の欠片35を失い、未確保の調律石2を得ます。", "Trade 35 unsecured shards for 2 unsecured tuning stones.");
                case DreamEvent.ShardKiln:
                    return Loc.T("未確保の調律石2を失い、未確保の欠片45を得ます。", "Trade 2 unsecured tuning stones for 45 unsecured shards.");
                case DreamEvent.StarOffering:
                    return Loc.T("レア以上の対象から一番弱い未確保の遺物を捧げ、この遠征の旅人の星の経験を40得ます。", "Sacrifice your weakest eligible unsecured relic of Rare or better for 40 permanent star experience for this expedition's hero.");
                case DreamEvent.DreamOffering:
                    return Loc.T($"対象のうち一番弱い未確保の遺物を捧げ、夢のレベルの経験値を{40 + 20 * heat}得ます。", $"Sacrifice your weakest eligible unsecured relic for {40 + 20 * heat} Dream Level experience.");
                case DreamEvent.AbyssalChest:
                    return Loc.T("未確保の欠片30を払い、潜行が1段深くなります。代わりにエピックの遺物1つを未確保で得ます。鞄の空きが必要です。",
                        "Pay 30 unsecured shards and delve one level deeper to gain one unsecured Epic relic. Requires a free satchel slot.");
                case DreamEvent.RelicExchange:
                    return Loc.T("レアかエピックの対象から一番弱い未確保の遺物を失い、同じレア度・レベルの次の枠の新品を得ます（武器→防具→装飾→頭→手→足→武器）。強化などは引き継ぎません。",
                        "Lose your weakest eligible unsecured Rare or Epic relic for a fresh relic of the same rarity and level in the next slot (Weapon → Armor → Charm → Head → Hands → Feet → Weapon). Enhancements and other investments are not carried over.");
                case DreamEvent.SealedVault:
                    return Loc.T("未確保の欠片40を払い、対象のうち一番強い未確保の遺物を直接保管庫へ送ります。他の荷物や欠片は確保されず、潜行や契約も変わりません。",
                        "Pay 40 unsecured shards to send your strongest eligible unsecured relic directly to the stash. Other cargo stays unsecured; delve and pacts remain unchanged.");
                case DreamEvent.PowerCrucible:
                    return Loc.T("固有品以外の対象から一番強い未確保の遺物の最後の固有効果を失い、最初の固有効果を同じ枠の別の効果へ交換します。交換前の効果と値は失います。",
                        "Lose the last power of your strongest eligible unsecured non-unique relic to replace its first power with a different power from the same slot. The original first power and value are also lost.");
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
                        || !run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderBy(r => r.Score).Skip(1).Any(r => r.Enhance < Content.MaxEnhanceFor(r)))
                        reason = Loc.T("まだ持ち帰っていない遺物が2つ以上必要です（そのうち1つは、まだ強化できる物）。", "Need 2+ unsecured relics (one enhanceable).");
                    break;
                case DreamEvent.Chalice:
                    if (run.SatchelShards <= 0) reason = Loc.T("賭ける欠片がありません。", "No shards to wager.");
                    break;
                case DreamEvent.Lantern:
                    if (p.LostAndFound.Count == 0) reason = Loc.T("遺失物がありません。", "No lost relics.");
                    break;
                case DreamEvent.ForgeShrine:
                    if (run.SatchelShards < 20) reason = Loc.T($"まだ持ち帰っていない欠片が20必要です（いま{run.SatchelShards}）。", $"Need 20 unsecured shards (you have {run.SatchelShards}).");
                    else if (!run.Satchel.Any(r => r.Enhance < Content.MaxEnhanceFor(r) && (trades == null || !trades.IsReserved(r.Uid))))
                        reason = Loc.T("まだ強化できる未確保の遺物が必要です。", "Need an enhanceable unsecured relic.");
                    break;
                case DreamEvent.TwinMirror:
                    if (run.SatchelShards < 30) reason = Loc.T($"まだ持ち帰っていない欠片が30必要です（いま{run.SatchelShards}）。", $"Need 30 unsecured shards (you have {run.SatchelShards}).");
                    else if (!run.Satchel.Any(r => trades == null || !trades.IsReserved(r.Uid))) reason = Loc.T("未確保の遺物が必要です。", "Need an unsecured relic.");
                    break;
                case DreamEvent.Cauldron:
                    if (run.Satchel.Count(r => (r.Rarity == Rarity.Common || r.Rarity == Rarity.Uncommon) && (trades == null || !trades.IsReserved(r.Uid))) < 3)
                        reason = Loc.T("コモンかアンコモンの未確保の遺物が3つ必要です。", "Need 3 unsecured Common or Uncommon relics.");
                    break;
                case DreamEvent.Tapir:
                    if (!run.Satchel.Any(r => r.Rarity < Rarity.Epic && (trades == null || !trades.IsReserved(r.Uid)))) reason = Loc.T("まだ持ち帰っていない、エピック未満の遺物が必要です。", "Need an unsecured relic below Epic.");
                    break;
                case DreamEvent.CourageGate:
                    if (run.Heat >= Content.MaxHeat) reason = Loc.T("これ以上深く潜れません。", "Cannot delve any deeper.");
                    break;
                case DreamEvent.Stargazer:
                case DreamEvent.Archive:
                case DreamEvent.LuckyStar:
                    break;
                case DreamEvent.MemoryWell:
                    if (p.Material(Materials.Tuning) < 1) reason = Loc.T("保管庫の調律石1が必要です。", "Need 1 stash tuning stone.");
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.ShadowExchange:
                    if (run.SatchelShards < 25) reason = NeedShards(25);
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.LostMausoleum:
                {
                    int count = LostCandidates(p, trades).Count();
                    if (run.SatchelShards < 60) reason = NeedShards(60);
                    else if (count == 0) reason = NoTradeTarget();
                    else if (run.Satchel.Count + count > Workshop.SatchelCapacity(p)) reason = NoSatchelRoom();
                    break;
                }
                case DreamEvent.RelicWager:
                case DreamEvent.RelicExchange:
                    if (run.Satchel.Count > Workshop.SatchelCapacity(p)) reason = NoSatchelRoom();
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.TemperingAltar:
                case DreamEvent.PowerCrucible:
                    if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.StoneBroker:
                    if (run.SatchelShards < 35) reason = NeedShards(35);
                    break;
                case DreamEvent.ShardKiln:
                    if (run.SatchelTuning < 2) reason = Loc.T("未確保の調律石2が必要です。", "Need 2 unsecured tuning stones.");
                    break;
                case DreamEvent.StarOffering:
                    if (string.IsNullOrEmpty(run.HeroKey) || !p.Heroes.TryGetValue(run.HeroKey, out var hero)
                        || hero.StarXp >= StarProgression.TotalXpForPoints(StarProgression.MaxPoints))
                        reason = Loc.T("星の経験を得られる遠征の旅人が必要です。", "Need an expedition hero who can still gain star experience.");
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.DreamOffering:
                    if (p.DreamLevel >= Content.MaxDreamLevel) reason = Loc.T("夢のレベルは上限です。", "Dream Level is at its cap.");
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.AbyssalChest:
                    if (run.SatchelShards < 30) reason = NeedShards(30);
                    else if (run.Heat >= Content.MaxHeat) reason = Loc.T("これ以上深く潜れません。", "Cannot delve any deeper.");
                    else if (run.Satchel.Count >= Workshop.SatchelCapacity(p)) reason = NoSatchelRoom();
                    break;
                case DreamEvent.SealedVault:
                    if (run.SatchelShards < 40) reason = NeedShards(40);
                    else if (p.Stash.Count >= Workshop.StashCapacity(p)) reason = Loc.T("保管庫に空きがありません。", "The stash is full.");
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                default:
                    reason = Loc.T("この出来事は今はありません。", "That event is not available.");
                    break;
            }
            return reason;
        }

        private static string NeedShards(int count) => Loc.T($"未確保の欠片{count}が必要です。", $"Need {count} unsecured shards.");
        private static string NoTradeTarget() => Loc.T("取引の対象になる遺物がありません。", "No eligible relic for this trade.");
        private static string NoSatchelRoom() => Loc.T("鞄に必要な空きがありません。", "Not enough room in the satchel.");

        internal static bool SafeTradeRelic(Profile p, Relic r, TradeLedger trades, bool equipped = false) =>
            !r.Locked && r.Uid != p.RetuneOffer?.Uid && (trades == null || !trades.IsReserved(r.Uid))
            && (equipped || !p.IsEquippedAnywhere(r.Uid));

        internal static IEnumerable<Relic> LostCandidates(Profile p, TradeLedger trades) =>
            p.LostAndFound.Where(r => SafeTradeRelic(p, r, trades)).OrderBy(r => r.Uid, StringComparer.Ordinal);

        internal static IEnumerable<PowerRange> ReplacementPowers(Relic r) =>
            Content.PowerPool(r.Slot).Where(x => !r.Powers.Any(p => p.Power == x.Power));

        internal static HashSet<Stat> ShadowExcludedStats(Relic r)
        {
            var used = new HashSet<Stat> { r.Base.ImplicitStat };
            foreach (var a in r.Affixes) used.Add(a.Stat);
            return used;
        }

        internal static Relic TradeTarget(Profile p, DreamEvent e, TradeLedger trades = null)
        {
            if (p.Run == null) return null;
            IEnumerable<Relic> pool = p.Run.Satchel.Where(r => SafeTradeRelic(p, r, trades));
            bool weakest = e == DreamEvent.StarOffering || e == DreamEvent.DreamOffering || e == DreamEvent.RelicExchange;
            switch (e)
            {
                case DreamEvent.MemoryWell:
                    if (string.IsNullOrEmpty(p.Run.HeroKey) || !p.Heroes.TryGetValue(p.Run.HeroKey, out var hero)) return null;
                    pool = p.Stash.Where(r => hero.Equipped.Contains(r.Uid) && SafeTradeRelic(p, r, trades, true)
                        && !r.HasFixedPowers && r.Powers.Count > 0 && ReplacementPowers(r).Any());
                    break;
                case DreamEvent.ShadowExchange:
                    pool = pool.Where(r => r.Affixes.Count > 0 && r.Retunes < Content.MaxRetunes
                        && Content.AffixPool(r.Slot).Any(a => r.Rarity >= a.MinRarity && a.Stat != r.Base.ImplicitStat
                            && !r.Affixes.Any(line => line.Stat == a.Stat)));
                    break;
                case DreamEvent.RelicWager:
                    pool = pool.Where(r => r.UniqueId == null && r.Rarity < Rarity.Epic);
                    break;
                case DreamEvent.TemperingAltar:
                    pool = pool.Where(r => r.Affixes.Count > 0 && r.Enhance + 2 <= Content.MaxEnhanceFor(r));
                    break;
                case DreamEvent.StarOffering:
                    pool = pool.Where(r => r.Rarity >= Rarity.Rare);
                    break;
                case DreamEvent.RelicExchange:
                    pool = pool.Where(r => r.UniqueId == null && (r.Rarity == Rarity.Rare || r.Rarity == Rarity.Epic));
                    break;
                case DreamEvent.PowerCrucible:
                    pool = pool.Where(r => !r.HasFixedPowers && r.Powers.Count >= 2 && ReplacementPowers(r).Any());
                    break;
            }
            Relic selected = null;
            foreach (var relic in pool)
            {
                if (selected == null || (weakest ? relic.Score < selected.Score : relic.Score > selected.Score)
                    || (relic.Score == selected.Score && StringComparer.Ordinal.Compare(relic.Uid, selected.Uid) < 0))
                    selected = relic;
            }
            return selected;
        }

        /// <summary>重みは条件を満たす出来事同士で比較する。0 は抽選しない。</summary>
        public static int OfferWeight(DreamEvent e)
        {
            switch (e)
            {
                case DreamEvent.Merchant: return 8;
                case DreamEvent.Fountain: return 8;
                case DreamEvent.Chalice: return 8;
                case DreamEvent.Lantern: return 8;
                case DreamEvent.ForgeShrine: return 8;
                case DreamEvent.TwinMirror: return 8;
                case DreamEvent.Stargazer: return 8;
                case DreamEvent.Cauldron: return 8;
                case DreamEvent.Tapir: return 8;
                case DreamEvent.CourageGate: return 8;
                case DreamEvent.Archive: return 8;
                case DreamEvent.LuckyStar: return 8;
                case DreamEvent.MemoryWell: return 6;
                case DreamEvent.ShadowExchange: return 8;
                case DreamEvent.LostMausoleum: return 5;
                case DreamEvent.RelicWager: return 6;
                case DreamEvent.TemperingAltar: return 6;
                case DreamEvent.StoneBroker: return 8;
                case DreamEvent.ShardKiln: return 8;
                case DreamEvent.StarOffering: return 6;
                case DreamEvent.DreamOffering: return 6;
                case DreamEvent.AbyssalChest: return 5;
                case DreamEvent.RelicExchange: return 8;
                case DreamEvent.SealedVault: return 5;
                case DreamEvent.PowerCrucible: return 5;
                default: return 0;
            }
        }

        /// <summary>確保地点に着いたとき、使用可能な出来事だけを重みつきで抽選する。</summary>
        public static DreamEvent Roll(Rng rng, Profile p, TradeLedger trades = null)
        {
            if (p.Run == null || !rng.Chance(OfferChance)) return DreamEvent.None;
            var pool = new List<(DreamEvent Event, int Weight)>();
            int total = 0;
            for (int value = (int)DreamEvent.Merchant; value <= (int)DreamEvent.PowerCrucible; value++)
            {
                var e = (DreamEvent)value;
                int weight = OfferWeight(e);
                if (weight <= 0 || UnavailableReason(p, e, e == DreamEvent.Merchant, trades) != null) continue;
                pool.Add((e, weight));
                total += weight;
            }
            if (total == 0) return DreamEvent.None;
            int pick = rng.Range(0, total - 1);
            foreach (var entry in pool)
            {
                if (pick < entry.Weight) return entry.Event;
                pick -= entry.Weight;
            }
            return DreamEvent.None;
        }
    }
}
