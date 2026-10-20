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
        public const double OfferChance = EventsBalance.OfferChance;
        /// <summary>星読みの塔：次に確保するまでの遺物ドロップ率の上乗せ（説明文と効果の両方で使う）。</summary>
        public const double StargazerDropBonus = EventsBalance.StargazerDropBonus;
        /// <summary>幸運の星：次に確保するまでの幸運（レア度の上振れ）。</summary>
        public const double LuckyStarLuck = EventsBalance.LuckyStarLuck;

        public static int MerchantCost(int heat) => EventsBalance.MerchantBaseShards + EventsBalance.MerchantShardsPerHeat * Loot.ClampHeat(heat);
        public static int TwinMirrorCost(Rarity rarity) => rarity >= Rarity.Epic ? EventsBalance.TwinMirrorEpicShards : EventsBalance.TwinMirrorShards;
        public static int MemoryWellCost(Rarity rarity) => rarity >= Rarity.Epic ? EventsBalance.MemoryWellEpicTuning : EventsBalance.MemoryWellTuning;
        public static int ShadowExchangeCost(Rarity rarity) => rarity >= Rarity.Epic ? EventsBalance.ShadowExchangeEpicShards : EventsBalance.ShadowExchangeShards;
        public static int ArchiveXp(int heat) => EventsBalance.ArchiveBaseXp + EventsBalance.ArchiveXpPerHeat * heat;
        public static int DreamOfferingXp(int heat) => EventsBalance.DreamOfferingBaseXp + EventsBalance.DreamOfferingXpPerHeat * heat;
        public static double RelicWagerChance(Rarity rarity) => rarity == Rarity.Rare ? EventsBalance.RelicWagerRareWinChance : EventsBalance.RelicWagerLowerWinChance;

        public static string Name(DreamEvent e)
        {
            switch (e)
            {
                case DreamEvent.Merchant: return Loc.T("夢の商人", "Dream Merchant", "梦之商人");
                case DreamEvent.Fountain: return Loc.T("祈りの泉", "Fountain of Prayer", "祈祷之泉");
                case DreamEvent.Chalice: return Loc.T("賭けの杯", "Gambler's Chalice", "赌徒之杯");
                case DreamEvent.Lantern: return Loc.T("迷い人の灯", "Lantern of the Lost", "迷途者之灯");
                case DreamEvent.ForgeShrine: return Loc.T("鍛冶の祠", "Forge Shrine", "锻造之祠");
                case DreamEvent.TwinMirror: return Loc.T("双子の鏡", "Twin Mirror", "双子之镜");
                case DreamEvent.Stargazer: return Loc.T("星読みの塔", "Stargazer's Tower", "观星之塔");
                case DreamEvent.Cauldron: return Loc.T("錬金の釜", "Alchemist's Cauldron", "炼金之釜");
                case DreamEvent.Tapir: return Loc.T("夢喰いの獏", "Dream Tapir", "食梦的獏");
                case DreamEvent.CourageGate: return Loc.T("勇気の門", "Gate of Courage", "勇气之门");
                case DreamEvent.Archive: return Loc.T("記憶の書庫", "Archive of Memories", "记忆书库");
                case DreamEvent.LuckyStar: return Loc.T("幸運の星", "Lucky Star", "幸运之星");
                case DreamEvent.MemoryWell: return Loc.T("記憶の井戸", "Memory Well", "记忆之井");
                case DreamEvent.ShadowExchange: return Loc.T("影の取引所", "Shadow Exchange", "暗影交易所");
                case DreamEvent.LostMausoleum: return Loc.T("遺失物の霊廟", "Lost Mausoleum", "遗失物灵庙");
                case DreamEvent.RelicWager: return Loc.T("遺物の賭場", "Relic Wager", "遗物赌场");
                case DreamEvent.TemperingAltar: return Loc.T("焼き入れの祭壇", "Tempering Altar", "淬火祭坛");
                case DreamEvent.StoneBroker: return Loc.T("調律石の市場", "Tuning Stone Market", "调律石市场");
                case DreamEvent.ShardKiln: return Loc.T("欠片の窯", "Shard Kiln", "碎片之窑");
                case DreamEvent.StarOffering: return Loc.T("星への供物", "Star Offering", "星之供品");
                case DreamEvent.DreamOffering: return Loc.T("夢への供物", "Dream Offering", "梦之供品");
                case DreamEvent.AbyssalChest: return Loc.T("深淵の宝箱", "Abyssal Chest", "深渊宝箱");
                case DreamEvent.RelicExchange: return Loc.T("遺物の交換所", "Relic Exchange", "遗物交易所");
                case DreamEvent.SealedVault: return Loc.T("封印の保管庫", "Sealed Vault", "封印仓库");
                case DreamEvent.PowerCrucible: return Loc.T("固有効果のるつぼ", "Power Crucible", "固有效果熔炉");
                default: return "";
            }
        }

        /// <summary>出来事を選ぶボタンの文字。何をするのかが分かる動詞にする。</summary>
        public static string ActionLabel(DreamEvent e)
        {
            switch (e)
            {
                case DreamEvent.Fountain: return Loc.T("一番弱い遺物を捧げる", "Offer the weakest relic", "献上最弱的遗物");
                case DreamEvent.Chalice: return Loc.T("欠片を賭ける", "Wager the shards", "押注碎片");
                case DreamEvent.Lantern: return Loc.T("遺失物を取り戻す", "Recover a lost relic", "取回遗失物");
                case DreamEvent.ForgeShrine: return Loc.T($"欠片{Content.GuaranteedEnhanceCost(e, Rarity.Common)}（エピック以上は{Content.GuaranteedEnhanceCost(e, Rarity.Epic)}）で強化する", $"Enhance for {Content.GuaranteedEnhanceCost(e, Rarity.Common)} shards ({Content.GuaranteedEnhanceCost(e, Rarity.Epic)} for Epic+)", $"花{Content.GuaranteedEnhanceCost(e, Rarity.Common)}碎片进行强化（史诗以上为{Content.GuaranteedEnhanceCost(e, Rarity.Epic)}）");
                case DreamEvent.TwinMirror: return Loc.T($"欠片{EventsBalance.TwinMirrorShards}（エピック以上は{EventsBalance.TwinMirrorEpicShards}）で写し取る", $"Copy for {EventsBalance.TwinMirrorShards} shards ({EventsBalance.TwinMirrorEpicShards} for Epic+)", $"花{EventsBalance.TwinMirrorShards}碎片复制（史诗以上为{EventsBalance.TwinMirrorEpicShards}）");
                case DreamEvent.Stargazer: return Loc.T("星を読む", "Read the stars", "观读星辰");
                case DreamEvent.Cauldron: return Loc.T($"{EventsBalance.CauldronRelicCount}つを溶かす", $"Melt {(EventsBalance.CauldronRelicCount == 3 ? "three" : EventsBalance.CauldronRelicCount.ToString())} relics", $"熔炼{EventsBalance.CauldronRelicCount}件遗物");
                case DreamEvent.Tapir: return Loc.T("獏に食べさせる", "Feed the tapir", "喂给獏吃");
                case DreamEvent.CourageGate: return Loc.T($"門をくぐる（潜行が{EventsBalance.CourageGateHeatIncrement}段深くなる）", $"Pass the gate (delve +{EventsBalance.CourageGateHeatIncrement})", $"穿过此门（下潜加深{EventsBalance.CourageGateHeatIncrement}段）");
                case DreamEvent.Archive: return Loc.T("記憶を読む", "Read the memories", "阅读记忆");
                case DreamEvent.LuckyStar: return Loc.T("星に願う", "Wish upon the star", "向星星许愿");
                case DreamEvent.MemoryWell: return Loc.T($"調律石{EventsBalance.MemoryWellTuning}（エピック以上は{EventsBalance.MemoryWellEpicTuning}）で固有効果を交換する", $"Swap a power for {EventsBalance.MemoryWellTuning} tuning stone ({EventsBalance.MemoryWellEpicTuning} for Epic+)", $"花{EventsBalance.MemoryWellTuning}颗调律石交换固有效果（史诗以上为{EventsBalance.MemoryWellEpicTuning}）");
                case DreamEvent.ShadowExchange: return Loc.T($"欠片{EventsBalance.ShadowExchangeShards}（エピック以上は{EventsBalance.ShadowExchangeEpicShards}）で最初の特性を引き直す", $"Reroll the first affix for {EventsBalance.ShadowExchangeShards} shards ({EventsBalance.ShadowExchangeEpicShards} for Epic+)", $"花{EventsBalance.ShadowExchangeShards}碎片重掷第一条词条（史诗以上为{EventsBalance.ShadowExchangeEpicShards}）");
                case DreamEvent.LostMausoleum: return Loc.T($"欠片{EventsBalance.LostMausoleumShards}を払い、強化を失って回収する", $"Pay {EventsBalance.LostMausoleumShards} shards and recover with lost enhancement", $"支付{EventsBalance.LostMausoleumShards}碎片，失去强化并回收");
                case DreamEvent.RelicWager: return Loc.T("一番強い対象の遺物を賭ける", "Wager the strongest eligible relic", "押注对象中最强的遗物");
                case DreamEvent.TemperingAltar: return Loc.T($"最初の特性を捧げて{Content.GuaranteedEnhanceSteps(e)}段階強化する", $"Sacrifice the first affix to enhance by {Content.GuaranteedEnhanceSteps(e)} levels", $"献上第一条词条，强化{Content.GuaranteedEnhanceSteps(e)}级");
                case DreamEvent.StoneBroker: return Loc.T($"欠片{EventsBalance.StoneBrokerShards}を調律石{EventsBalance.StoneBrokerTuning}に換える", $"Trade {EventsBalance.StoneBrokerShards} shards for {EventsBalance.StoneBrokerTuning} tuning stones", $"将{EventsBalance.StoneBrokerShards}碎片换成{EventsBalance.StoneBrokerTuning}颗调律石");
                case DreamEvent.ShardKiln: return Loc.T($"調律石{EventsBalance.ShardKilnTuning}を欠片{EventsBalance.ShardKilnShards}に換える", $"Trade {EventsBalance.ShardKilnTuning} tuning stones for {EventsBalance.ShardKilnShards} shards", $"将{EventsBalance.ShardKilnTuning}颗调律石换成{EventsBalance.ShardKilnShards}枚碎片");
                case DreamEvent.StarOffering: return Loc.T("遺物を捧げて星の経験を得る", "Sacrifice a relic for star experience", "献上遗物以获得星辰经验");
                case DreamEvent.DreamOffering: return Loc.T("遺物を捧げて夢の経験を得る", "Sacrifice a relic for dream experience", "献上遗物以获得梦境经验");
                case DreamEvent.AbyssalChest: return Loc.T($"欠片{EventsBalance.AbyssalChestShards}を払い、{EventsBalance.AbyssalChestHeatIncrement}段深く潜る", $"Pay {EventsBalance.AbyssalChestShards} shards and delve {(EventsBalance.AbyssalChestHeatIncrement == 1 ? "one" : EventsBalance.AbyssalChestHeatIncrement.ToString())} level deeper", $"支付{EventsBalance.AbyssalChestShards}碎片，下潜加深{EventsBalance.AbyssalChestHeatIncrement}段");
                case DreamEvent.RelicExchange: return Loc.T("一番弱い対象の遺物を交換する", "Exchange the weakest eligible relic", "交换对象中最弱的遗物");
                case DreamEvent.SealedVault: return Loc.T($"欠片{EventsBalance.SealedVaultShards}で遺物1つを保管庫へ送る", $"Pay {EventsBalance.SealedVaultShards} shards to send one relic to the stash", $"花{EventsBalance.SealedVaultShards}碎片将1件遗物送入仓库");
                case DreamEvent.PowerCrucible: return Loc.T("最後の固有効果を捧げ、最初を交換する", "Sacrifice the last power to swap the first", "献上最后一条固有效果，交换第一条");
                default: return Loc.T("この出来事を選ぶ", "Take this event", "选择此事");
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
            return text + Loc.T(" 対象は鍵なし・取引中でない・再調律の候補待ちでない遺物。井戸以外は未装着のみ。同じ強さなら個体ID順で選ぶ。",
                " Eligible relics are unlocked, not reserved for a trade and not awaiting a retune choice. Except at the Well, equipped relics are excluded. Score ties use ordinal relic ID order.", " 对象为未上锁、未处于交易中、且不在等待再调律选择的遗物。井以外的场合仅限未装备。强度相同时按个体ID顺序选择。");
        }

        private static string DescribeTrade(DreamEvent e, Profile p)
        {
            var run = p.Run;
            int heat = run?.Heat ?? 0;
            switch (e)
            {
                case DreamEvent.Merchant:
                    return Loc.T($"ゴールドで中身の分からない遺物を1つ買える。必ずアンコモン以上で、深く潜っているほど良い物が出る。",
                        "Buy a mystery relic with gold (Uncommon or better; better when deeper).", "可用金币购买1件内容不明的遗物。必定为优秀以上，下潜越深越容易出好东西。");
                case DreamEvent.Fountain:
                    return Loc.T($"まだ持ち帰っていない遺物のうち一番弱い物を捧げると、一番強い物が+{Content.GuaranteedEnhanceSteps(e)}強化される。", $"Sacrifice your weakest unsecured relic to enhance your best one by +{Content.GuaranteedEnhanceSteps(e)}.", $"献上尚未带回的遗物中最弱的一件，最强的一件将强化+{Content.GuaranteedEnhanceSteps(e)}。");
                case DreamEvent.Chalice:
                    return Loc.T($"まだ持ち帰っていない欠片{run?.SatchelShards ?? 0}を賭ける。{EventsBalance.ChaliceWinChance * 100:g}%の確率で{1 + EventsBalance.ChaliceBonusMultiplier}倍になり、外れるとすべて失う。",
                        $"Wager your {run?.SatchelShards ?? 0} unsecured shards: {EventsBalance.ChaliceWinChance * 100:g}% to {(EventsBalance.ChaliceBonusMultiplier == 1 ? "double them" : $"multiply them by {1 + EventsBalance.ChaliceBonusMultiplier}")}, otherwise you lose them all.", $"赌上尚未带回的{run?.SatchelShards ?? 0}枚碎片：{EventsBalance.ChaliceWinChance * 100:g}%的概率变为{1 + EventsBalance.ChaliceBonusMultiplier}倍，否则全部失去。");
                case DreamEvent.Lantern:
                    return Loc.T("遺失物のうち一番良い物を1つ、この場で取り戻せる（確保するまでは、まだ持ち帰っていない扱い）。", "Recover your best lost relic right here (it stays unsecured until you secure).", "可在现场取回1件遗失物中最好的（存入仓库之前仍视为尚未带回）。");
                case DreamEvent.ForgeShrine:
                    return Loc.T($"まだ持ち帰っていない欠片を{Content.GuaranteedEnhanceCost(e, Rarity.Common)}（対象がエピック以上なら{Content.GuaranteedEnhanceCost(e, Rarity.Epic)}）払うと、まだ持ち帰っていない遺物のうち一番強い物が+{Content.GuaranteedEnhanceSteps(e)}強化される。",
                        $"Pay {Content.GuaranteedEnhanceCost(e, Rarity.Common)} unsecured shards ({Content.GuaranteedEnhanceCost(e, Rarity.Epic)} if the target is Epic or better) to enhance your best unsecured relic by +{Content.GuaranteedEnhanceSteps(e)}.", $"支付{Content.GuaranteedEnhanceCost(e, Rarity.Common)}（对象为史诗以上时为{Content.GuaranteedEnhanceCost(e, Rarity.Epic)}）尚未带回的碎片，尚未带回的遗物中最强的一件将强化+{Content.GuaranteedEnhanceSteps(e)}。");
                case DreamEvent.TwinMirror:
                    return Loc.T($"まだ持ち帰っていない欠片を{EventsBalance.TwinMirrorShards}（元の遺物がエピック以上なら{EventsBalance.TwinMirrorEpicShards}）払うと、まだ持ち帰っていない遺物のうち一番強い物と同じ種類・同じレア度の遺物が、もう1つ手に入る（固有品の場合はエピックになる）。",
                        $"Pay {EventsBalance.TwinMirrorShards} unsecured shards ({EventsBalance.TwinMirrorEpicShards} if the source is Epic or better) to get another relic of the same type and rarity as your best unsecured relic (legendaries become epic).", $"支付{EventsBalance.TwinMirrorShards}（原遗物为史诗以上时为{EventsBalance.TwinMirrorEpicShards}）尚未带回的碎片，可获得1件与尚未带回的最强遗物同种类、同稀有度的遗物（传说遗物会变为史诗）。");
                case DreamEvent.Stargazer:
                    return Loc.T($"次に確保するまで、遺物が{(int)Math.Round(StargazerDropBonus * 100)}%多く落ちる。",
                        $"Until you next secure, relics drop {(int)Math.Round(StargazerDropBonus * 100)}% more often.", $"在下次存入仓库之前，遗物掉落增加{(int)Math.Round(StargazerDropBonus * 100)}%。");
                case DreamEvent.Cauldron:
                    return Loc.T($"まだ持ち帰っていないコモンかアンコモンの遺物を{EventsBalance.CauldronRelicCount}つ溶かして、1つ上のレア度の遺物を1つ作る。",
                        $"Melt {EventsBalance.CauldronRelicCount} unsecured Common or Uncommon relics into one relic of the next rarity.", $"熔炼{EventsBalance.CauldronRelicCount}件尚未带回的普通或优秀遗物，制作出1件高一档稀有度的遗物。");
                case DreamEvent.Tapir:
                    return Loc.T($"まだ持ち帰っていないエピック未満の遺物をすべて獏に食べさせ、分解と同じだけの欠片と、{EventsBalance.TapirRelicsPerTuning}つごとに調律石1をもらう（欠片はすぐ保管庫に入る。エピック以上は食べない）。",
                        $"Feed all unsecured relics below Epic to the tapir: shards equal to salvaging them, plus 1 tuning stone per {EventsBalance.TapirRelicsPerTuning} relics (the shards go straight to your stash; Epic and above are spared).", $"将尚未带回的全部史诗以下遗物喂给獏，获得与分解等量的碎片，且每{EventsBalance.TapirRelicsPerTuning}件额外获得1颗调律石（碎片直接进入仓库。史诗以上不会被吃掉）。");
                case DreamEvent.CourageGate:
                    return Loc.T($"潜行が{EventsBalance.CourageGateHeatIncrement}段深くなる代わりに、まだ持ち帰っていない欠片が{EventsBalance.CourageGateShards}増える。",
                        $"Your delve goes {(EventsBalance.CourageGateHeatIncrement == 1 ? "one" : EventsBalance.CourageGateHeatIncrement.ToString())} level deeper, and you gain {EventsBalance.CourageGateShards} unsecured shards.", $"下潜加深{EventsBalance.CourageGateHeatIncrement}段，同时获得{EventsBalance.CourageGateShards}枚尚未带回的碎片。");
                case DreamEvent.Archive:
                    return Loc.T($"夢のレベルの経験値を{ArchiveXp(heat)}もらう。",
                        $"Gain {ArchiveXp(heat)} Dream Level experience.", $"获得{ArchiveXp(heat)}点梦境等级经验值。");
                case DreamEvent.LuckyStar:
                    return Loc.T("次に確保するまで、レア度の高い遺物が少し出やすくなる。",
                        "Until you next secure, rarer relics drop slightly more often.", "在下次存入仓库之前，高稀有度遗物的出现率略微提高。");
                case DreamEvent.MemoryWell:
                    return Loc.T($"保管庫の調律石{EventsBalance.MemoryWellTuning}（対象がエピック以上なら{EventsBalance.MemoryWellEpicTuning}）を払い、この遠征の旅人が装着している対象のうち一番強い遺物の最初の固有効果を、同じ枠の別の効果へ交換する。固有品は対象外。元の効果と値は失う。",
                        $"Pay {EventsBalance.MemoryWellTuning} stash tuning stone ({EventsBalance.MemoryWellEpicTuning} if the target is Epic or better) to replace the first power of this expedition hero's strongest eligible equipped relic with a different power from the same slot. Unique relics are excluded; the original power and value are lost.", $"支付{EventsBalance.MemoryWellTuning}颗仓库调律石（对象为史诗以上时为{EventsBalance.MemoryWellEpicTuning}），将本次远征旅人装备的对象中最强遗物的第一条固有效果，交换为同部位的其他效果。固有遗物不在对象内。原有效果与数值将失去。");
                case DreamEvent.ShadowExchange:
                    return Loc.T($"未確保の欠片{EventsBalance.ShadowExchangeShards}（対象がエピック以上なら{EventsBalance.ShadowExchangeEpicShards}）を払い、対象のうち一番強い未確保の遺物の最初の特性を、別の能力値へ引き直す。再調律を1回消費し、元の特性は失う。",
                        $"Pay {EventsBalance.ShadowExchangeShards} unsecured shards ({EventsBalance.ShadowExchangeEpicShards} if the target is Epic or better) to reroll the first affix of your strongest eligible unsecured relic into a different stat. Uses one retune; the old affix is lost.", $"支付{EventsBalance.ShadowExchangeShards}枚未存入的碎片（对象为史诗以上时为{EventsBalance.ShadowExchangeEpicShards}），将对象中最强的未存入遗物的第一条词条重掷为其他属性。消耗1次再调律，原词条将失去。");
                case DreamEvent.LostMausoleum:
                    return Loc.T($"未確保の欠片{EventsBalance.LostMausoleumShards}を払い、対象の遺失物をすべて未確保として回収する。各遺物の強化は0になる（獲得済みの節目は残る）。全品を入れる鞄の空きが必要。",
                        $"Pay {EventsBalance.LostMausoleumShards} unsecured shards to recover all eligible lost relics as unsecured. Each loses all enhancement (earned milestones remain). Your satchel must have room for all of them.", $"支付{EventsBalance.LostMausoleumShards}枚未存入的碎片，将对象遗失物全部以未存入状态回收。各遗物的强化归零（已获得的里程碑保留）。背包需要有容纳全部物品的空间。");
                case DreamEvent.RelicWager:
                    return Loc.T($"エピック未満・固有品以外の対象から一番強い未確保の遺物を賭ける。対象がレアなら{EventsBalance.RelicWagerRareWinChance * 100:g}%、それ未満なら{EventsBalance.RelicWagerLowerWinChance * 100:g}%で同じ土台・レベルの1つ上のレア度の新品に交換し、外れると分解相当の未確保の欠片になる。元の遺物と強化は失う。",
                        $"Wager your strongest eligible unsecured non-unique relic below Epic. A Rare target has a {EventsBalance.RelicWagerRareWinChance * 100:g}% chance ({EventsBalance.RelicWagerLowerWinChance * 100:g}% below Rare) to become a fresh relic of the same base and level, one rarity higher; otherwise, unsecured shards equal to its salvage value. The original relic and enhancements are lost.", $"在史诗以下、非固有遗物的对象中押注最强的未存入遗物。对象为稀有时有{EventsBalance.RelicWagerRareWinChance * 100:g}%（稀有以下为{EventsBalance.RelicWagerLowerWinChance * 100:g}%）的概率变为同基础、同等级、高一档稀有度的全新遗物，未中则变为相当于分解价值的未存入碎片。原遗物与强化将失去。");
                case DreamEvent.TemperingAltar:
                    return Loc.T($"対象のうち一番強い未確保の遺物の最初の特性を失う代わりに、強化を{Content.GuaranteedEnhanceSteps(e)}段階進める。強化の節目は通常どおり得られる。",
                        $"Lose the first affix of your strongest eligible unsecured relic to enhance it by {Content.GuaranteedEnhanceSteps(e)} levels. Enhancement milestones are granted normally.", $"失去对象中最强的未存入遗物的第一条词条，换取强化{Content.GuaranteedEnhanceSteps(e)}级。强化的里程碑照常获得。");
                case DreamEvent.StoneBroker:
                    return Loc.T($"未確保の欠片{EventsBalance.StoneBrokerShards}を失い、未確保の調律石{EventsBalance.StoneBrokerTuning}を得る。", $"Trade {EventsBalance.StoneBrokerShards} unsecured shards for {EventsBalance.StoneBrokerTuning} unsecured tuning stones.", $"失去{EventsBalance.StoneBrokerShards}枚未存入的碎片，获得{EventsBalance.StoneBrokerTuning}颗未存入的调律石。");
                case DreamEvent.ShardKiln:
                    return Loc.T($"未確保の調律石{EventsBalance.ShardKilnTuning}を失い、未確保の欠片{EventsBalance.ShardKilnShards}を得る。", $"Trade {EventsBalance.ShardKilnTuning} unsecured tuning stones for {EventsBalance.ShardKilnShards} unsecured shards.", $"失去{EventsBalance.ShardKilnTuning}颗未存入的调律石，获得{EventsBalance.ShardKilnShards}枚未存入的碎片。");
                case DreamEvent.StarOffering:
                    return Loc.T($"レア以上の対象から一番弱い未確保の遺物を捧げ、この遠征の旅人の星の経験を{EventsBalance.StarOfferingXp}得る。夢の深さの経験倍率も適用される。", $"Sacrifice your weakest eligible unsecured relic of Rare or better for {EventsBalance.StarOfferingXp} permanent star experience for this expedition's hero. The dream depth experience multiplier also applies.", $"献上稀有以上对象中最弱的未存入遗物，为本次远征的旅人获得{EventsBalance.StarOfferingXp}点永久的星辰经验。梦境深度的经验倍率同样适用。");
                case DreamEvent.DreamOffering:
                    return Loc.T($"対象のうち一番弱い未確保の遺物を捧げ、夢のレベルの経験値を{DreamOfferingXp(heat)}得る。", $"Sacrifice your weakest eligible unsecured relic for {DreamOfferingXp(heat)} Dream Level experience.", $"献上对象中最弱的未存入遗物，获得{DreamOfferingXp(heat)}点梦境等级经验值。");
                case DreamEvent.AbyssalChest:
                    return Loc.T($"未確保の欠片{EventsBalance.AbyssalChestShards}を払い、潜行が{EventsBalance.AbyssalChestHeatIncrement}段深くなる。代わりにエピックの遺物1つを未確保で得る。鞄の空きが必要。",
                        $"Pay {EventsBalance.AbyssalChestShards} unsecured shards and delve {(EventsBalance.AbyssalChestHeatIncrement == 1 ? "one" : EventsBalance.AbyssalChestHeatIncrement.ToString())} level deeper to gain one unsecured Epic relic. Requires a free satchel slot.", $"支付{EventsBalance.AbyssalChestShards}枚未存入的碎片，下潜加深{EventsBalance.AbyssalChestHeatIncrement}段，并获得1件未存入的史诗遗物。需要背包有空位。");
                case DreamEvent.RelicExchange:
                    return Loc.T("レアかエピックの対象から一番弱い未確保の遺物を失い、同じレア度・レベルの次の枠の新品を得る（武器→防具→装飾→頭→手→足→武器）。強化などは引き継がない。",
                        "Lose your weakest eligible unsecured Rare or Epic relic for a fresh relic of the same rarity and level in the next slot (Weapon → Armor → Charm → Head → Hands → Feet → Weapon). Enhancements and other investments are not carried over.", "失去稀有或史诗对象中最弱的未存入遗物，获得同稀有度、同等级的下个部位的全新遗物（武器→防具→饰品→头部→手部→足部→武器）。强化等不会继承。");
                case DreamEvent.SealedVault:
                    return Loc.T($"未確保の欠片{EventsBalance.SealedVaultShards}を払い、対象のうち一番強い未確保の遺物を直接保管庫へ送る。他の荷物や欠片は確保されず、潜行や契約も変わらない。",
                        $"Pay {EventsBalance.SealedVaultShards} unsecured shards to send your strongest eligible unsecured relic directly to the stash. Other cargo stays unsecured; delve and pacts remain unchanged.", $"支付{EventsBalance.SealedVaultShards}枚未存入的碎片，将对象中最强的未存入遗物直接送入仓库。其他行李和碎片不会被存入，下潜与契约也不变。");
                case DreamEvent.PowerCrucible:
                    return Loc.T("固有品以外の対象から一番強い未確保の遺物の最後の固有効果を失い、最初の固有効果を同じ枠の別の効果へ交換する。交換前の効果と値は失う。",
                        "Lose the last power of your strongest eligible unsecured non-unique relic to replace its first power with a different power from the same slot. The original first power and value are also lost.", "失去非固有遗物对象中最强未存入遗物的最后一条固有效果，并将第一条固有效果交换为同部位的其他效果。交换前的效果与数值将失去。");
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
                reason = Loc.T("この出来事は今はありません。", "That event is not available.", "此事件现在不存在。");
                return false;
            }
            reason = UnavailableReason(p, e, goldPaid, trades);
            return reason == null;
        }

        private static string UnavailableReason(Profile p, DreamEvent e, bool goldPaid, TradeLedger trades = null)
        {
            var run = p.Run;
            string reason = null;
            if (run == null) return Loc.T("遠征中のみ使えます。", "Only during an expedition.", "仅在远征中可用。");
            switch (e)
            {
                case DreamEvent.Merchant:
                    if (trades != null && trades.IsMerchantReserved(run.OfferedEventId))
                        return Loc.T("この商人の取引は未確定です。", "This merchant's trade is unresolved.", "此商人的交易尚未确定。");
                    if (!goldPaid && p.Material(Materials.Shard) < MerchantCost(run.Heat)) reason = Loc.T("欠片が足りません。", "Not enough shards.", "碎片不足。");
                    break;
                case DreamEvent.Fountain:
                    if (run.Satchel.Count(r => trades == null || !trades.IsReserved(r.Uid)) < 2
                        || !run.Satchel.Where(r => trades == null || !trades.IsReserved(r.Uid)).OrderBy(r => r.Score).Skip(1).Any(r => Content.CanGuaranteedEnhance(r, e)))
                        reason = Loc.T("まだ持ち帰っていない遺物が2つ以上必要です（そのうち1つはまだ強化できる物）。", "Need 2+ unsecured relics (one enhanceable).", "需要2件以上尚未带回的遗物（其中1件须可继续强化）。");
                    break;
                case DreamEvent.Chalice:
                    if (run.SatchelShards <= 0) reason = Loc.T("賭ける欠片がありません。", "No shards to wager.", "没有可押注的碎片。");
                    break;
                case DreamEvent.Lantern:
                    if (p.LostAndFound.Count == 0) reason = Loc.T("遺失物がありません。", "No lost relics.", "没有遗失物。");
                    break;
                case DreamEvent.ForgeShrine:
                {
                    Relic target = null;
                    foreach (var relic in run.Satchel)
                    {
                        if (!Content.CanGuaranteedEnhance(relic, e) || (trades != null && trades.IsReserved(relic.Uid))) continue;
                        if (target == null || relic.Score > target.Score) target = relic;
                    }
                    int cost = Content.GuaranteedEnhanceCost(e, target?.Rarity ?? Rarity.Common);
                    if (run.SatchelShards < cost) reason = Loc.T($"まだ持ち帰っていない欠片が{cost}必要です（いま{run.SatchelShards}）。", $"Need {cost} unsecured shards (you have {run.SatchelShards}).", $"需要{cost}枚尚未带回的碎片（现有{run.SatchelShards}）。");
                    else if (target == null) reason = Loc.T("まだ強化できる未確保の遺物が必要です。", "Need an enhanceable unsecured relic.", "需要1件可继续强化的未存入遗物。");
                    break;
                }
                case DreamEvent.TwinMirror:
                {
                    Relic source = null;
                    foreach (var relic in run.Satchel)
                    {
                        if (trades != null && trades.IsReserved(relic.Uid)) continue;
                        if (source == null || relic.Score > source.Score) source = relic;
                    }
                    int cost = TwinMirrorCost(source?.Rarity ?? Rarity.Common);
                    if (run.SatchelShards < cost) reason = Loc.T($"まだ持ち帰っていない欠片が{cost}必要です（いま{run.SatchelShards}）。", $"Need {cost} unsecured shards (you have {run.SatchelShards}).", $"需要{cost}枚尚未带回的碎片（现有{run.SatchelShards}）。");
                    else if (source == null) reason = Loc.T("未確保の遺物が必要です。", "Need an unsecured relic.", "需要1件未存入的遗物。");
                    break;
                }
                case DreamEvent.Cauldron:
                    if (run.Satchel.Count(r => (r.Rarity == Rarity.Common || r.Rarity == Rarity.Uncommon) && (trades == null || !trades.IsReserved(r.Uid))) < EventsBalance.CauldronRelicCount)
                        reason = Loc.T($"コモンかアンコモンの未確保の遺物が{EventsBalance.CauldronRelicCount}つ必要です。", $"Need {EventsBalance.CauldronRelicCount} unsecured Common or Uncommon relics.", $"需要{EventsBalance.CauldronRelicCount}件普通或优秀的未存入遗物。");
                    break;
                case DreamEvent.Tapir:
                    if (!run.Satchel.Any(r => r.Rarity < Rarity.Epic && (trades == null || !trades.IsReserved(r.Uid)))) reason = Loc.T("まだ持ち帰っていない、エピック未満の遺物が必要です。", "Need an unsecured relic below Epic.", "需要1件尚未带回的史诗以下遗物。");
                    break;
                case DreamEvent.CourageGate:
                    if (run.Heat > Content.MaxHeat - EventsBalance.CourageGateHeatIncrement) reason = Loc.T("これ以上深く潜れません。", "Cannot delve any deeper.", "无法再继续下潜。");
                    break;
                case DreamEvent.Archive:
                    // 経験値は上限の夢のレベルでは入らない（夢への供物と同じ）。
                    if (p.DreamLevel >= Content.MaxDreamLevel) reason = Loc.T("夢のレベルは上限です。", "Dream Level is at its cap.", "梦境等级已达上限。");
                    break;
                case DreamEvent.Stargazer:
                case DreamEvent.LuckyStar:
                    break;
                case DreamEvent.MemoryWell:
                {
                    var target = TradeTarget(p, e, trades);
                    int cost = MemoryWellCost(target?.Rarity ?? Rarity.Common);
                    if (p.Material(Materials.Tuning) < cost) reason = Loc.T($"保管庫の調律石{cost}が必要です。", $"Need {cost} stash tuning stones.", $"需要{cost}颗仓库调律石。");
                    else if (target == null) reason = NoTradeTarget();
                    break;
                }
                case DreamEvent.ShadowExchange:
                {
                    var target = TradeTarget(p, e, trades);
                    int cost = ShadowExchangeCost(target?.Rarity ?? Rarity.Common);
                    if (run.SatchelShards < cost) reason = NeedShards(cost);
                    else if (target == null) reason = NoTradeTarget();
                    break;
                }
                case DreamEvent.LostMausoleum:
                {
                    int count = LostCandidates(p, trades).Count();
                    if (run.SatchelShards < EventsBalance.LostMausoleumShards) reason = NeedShards(EventsBalance.LostMausoleumShards);
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
                    if (run.SatchelShards < EventsBalance.StoneBrokerShards) reason = NeedShards(EventsBalance.StoneBrokerShards);
                    break;
                case DreamEvent.ShardKiln:
                    if (run.SatchelTuning < EventsBalance.ShardKilnTuning) reason = Loc.T($"未確保の調律石{EventsBalance.ShardKilnTuning}が必要です。", $"Need {EventsBalance.ShardKilnTuning} unsecured tuning stones.", $"需要{EventsBalance.ShardKilnTuning}颗未存入的调律石。");
                    break;
                case DreamEvent.StarOffering:
                    if (string.IsNullOrEmpty(run.HeroKey) || !p.Heroes.TryGetValue(run.HeroKey, out var hero)
                        || hero.StarXp >= StarProgression.TotalXpForPoints(StarProgression.MaxPoints))
                        reason = Loc.T("星の経験を得られる遠征の旅人が必要です。", "Need an expedition hero who can still gain star experience.", "需要一位还能获得星辰经验的远征旅人。");
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.DreamOffering:
                    if (p.DreamLevel >= Content.MaxDreamLevel) reason = Loc.T("夢のレベルは上限です。", "Dream Level is at its cap.", "梦境等级已达上限。");
                    else if (TradeTarget(p, e, trades) == null) reason = NoTradeTarget();
                    break;
                case DreamEvent.AbyssalChest:
                    if (run.SatchelShards < EventsBalance.AbyssalChestShards) reason = NeedShards(EventsBalance.AbyssalChestShards);
                    else if (run.Heat > Content.MaxHeat - EventsBalance.AbyssalChestHeatIncrement) reason = Loc.T("これ以上深く潜れません。", "Cannot delve any deeper.", "无法再继续下潜。");
                    else if (run.Satchel.Count >= Workshop.SatchelCapacity(p)) reason = NoSatchelRoom();
                    break;
                case DreamEvent.SealedVault:
                    if (run.SatchelShards < EventsBalance.SealedVaultShards) reason = NeedShards(EventsBalance.SealedVaultShards);
                    else if (p.Stash.Count >= Workshop.StashCapacity(p)) reason = Loc.T("保管庫に空きがありません。", "The stash is full.", "仓库已满。");
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
                    // 禁忌化した遺物は対象外（#415）: 固有効果を差し替えると断絶の印（SeveranceCandidates は
                    // 所持済みの効果を除く）の検証が壊れる。オレンジ枠は HasFixedPowers でも弾かれるが、明示的に外す。
                    pool = p.Stash.Where(r => hero.Equipped.Contains(r.Uid) && SafeTradeRelic(p, r, trades, true)
                        && r.Forbidden == null && !r.HasFixedPowers && r.Powers.Count > 0 && ReplacementPowers(r).Any());
                    break;
                case DreamEvent.ShadowExchange:
                    // 再調律と同じ上限（#415：職人解放後のオレンジ枠は引き上げ後の回数を使う）。
                    pool = pool.Where(r => r.Affixes.Count > 0 && r.Retunes < Forbidden.RetuneCap(p, r) && r.Forbidden == null
                        && Content.AffixPool(r.Slot).Any(a => r.Rarity >= a.MinRarity && a.Stat != r.Base.ImplicitStat
                            && !r.Affixes.Any(line => line.Stat == a.Stat)));
                    break;
                case DreamEvent.RelicWager:
                    pool = pool.Where(r => r.UniqueId == null && r.Rarity < Rarity.Epic);
                    break;
                case DreamEvent.TemperingAltar:
                    // 禁忌化した遺物は対象外（#415）：特性を1つ失うと失った特性の印（RemovedStat）が壊れることがある。
                    pool = pool.Where(r => r.Forbidden == null && r.Affixes.Count > 0 && Content.CanGuaranteedEnhance(r, e));
                    break;
                case DreamEvent.StarOffering:
                    pool = pool.Where(r => r.Rarity >= Rarity.Rare);
                    break;
                case DreamEvent.RelicExchange:
                    pool = pool.Where(r => r.UniqueId == null && (r.Rarity == Rarity.Rare || r.Rarity == Rarity.Epic));
                    break;
                case DreamEvent.PowerCrucible:
                    // 禁忌化した遺物は対象外（#415。MemoryWell と同じ理由で固有効果を差し替えられない）。
                    pool = pool.Where(r => r.Forbidden == null && !r.HasFixedPowers && r.Powers.Count >= 2 && ReplacementPowers(r).Any());
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
                case DreamEvent.Merchant: return EventsBalance.OfferWeightsMerchant;
                case DreamEvent.Fountain: return EventsBalance.OfferWeightsFountain;
                case DreamEvent.Chalice: return EventsBalance.OfferWeightsChalice;
                case DreamEvent.Lantern: return EventsBalance.OfferWeightsLantern;
                case DreamEvent.ForgeShrine: return EventsBalance.OfferWeightsForgeShrine;
                case DreamEvent.TwinMirror: return EventsBalance.OfferWeightsTwinMirror;
                case DreamEvent.Stargazer: return EventsBalance.OfferWeightsStargazer;
                case DreamEvent.Cauldron: return EventsBalance.OfferWeightsCauldron;
                case DreamEvent.Tapir: return EventsBalance.OfferWeightsTapir;
                case DreamEvent.CourageGate: return EventsBalance.OfferWeightsCourageGate;
                case DreamEvent.Archive: return EventsBalance.OfferWeightsArchive;
                case DreamEvent.LuckyStar: return EventsBalance.OfferWeightsLuckyStar;
                case DreamEvent.MemoryWell: return EventsBalance.OfferWeightsMemoryWell;
                case DreamEvent.ShadowExchange: return EventsBalance.OfferWeightsShadowExchange;
                case DreamEvent.LostMausoleum: return EventsBalance.OfferWeightsLostMausoleum;
                case DreamEvent.RelicWager: return EventsBalance.OfferWeightsRelicWager;
                case DreamEvent.TemperingAltar: return EventsBalance.OfferWeightsTemperingAltar;
                case DreamEvent.StoneBroker: return EventsBalance.OfferWeightsStoneBroker;
                case DreamEvent.ShardKiln: return EventsBalance.OfferWeightsShardKiln;
                case DreamEvent.StarOffering: return EventsBalance.OfferWeightsStarOffering;
                case DreamEvent.DreamOffering: return EventsBalance.OfferWeightsDreamOffering;
                case DreamEvent.AbyssalChest: return EventsBalance.OfferWeightsAbyssalChest;
                case DreamEvent.RelicExchange: return EventsBalance.OfferWeightsRelicExchange;
                case DreamEvent.SealedVault: return EventsBalance.OfferWeightsSealedVault;
                case DreamEvent.PowerCrucible: return EventsBalance.OfferWeightsPowerCrucible;
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
