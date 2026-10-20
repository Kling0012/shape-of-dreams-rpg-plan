using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SodRpg.Core.Game
{
    public enum FeatKind
    {
        Kills = 0,
        Runs = 1,
        Victories = 2,
        Legendaries = 3,
        Relics = 4,
        CodexPct = 5,
        DepthSecured = 6,
        Nightmares = 7,
        DreamLevel = 8,
        Pacts = 9,
        Events = 10,
        Bounties = 11,
        SetsCompleted = 12,
        RelicsAwakened = 13,
    }

    public sealed class FeatDef
    {
        public string Id;
        public Txt Name;
        public FeatKind Kind;
        public int Target;
        public int RewardShards;
        public int RewardTuning;
    }

    public static class Feats
    {
        public static readonly IReadOnlyList<FeatDef> All = new[]
        {
            Def("feat.kills.1", FeatKind.Kills, 100, 1, "夢の狩人見習い", "Apprentice Hunter", "梦境猎手见习"),
            Def("feat.kills.5", FeatKind.Kills, 300, 2, "夜の深みで", "Deep in the Night", "在夜之深处"),
            Def("feat.kills.2", FeatKind.Kills, 500, 2, "夢の狩人", "Dream Hunter", "梦境猎手"),
            Def("feat.kills.6", FeatKind.Kills, 1000, 3, "夢の大群を払う者", "Dream Horde Sweeper", "扫荡梦境大军者"),
            Def("feat.kills.3", FeatKind.Kills, 2000, 3, "悪夢を払う者", "Nightmare Sweeper", "驱散噩梦者"),
            Def("feat.kills.7", FeatKind.Kills, 5000, 4, "五千の夢を越えて", "Beyond Five Thousand Dreams", "跨越五千之梦"),
            Def("feat.kills.4", FeatKind.Kills, 10000, 4, "万の夢を斬る者", "Slayer of Ten Thousand Dreams", "斩却万梦者"),
            Def("feat.kills.8", FeatKind.Kills, 20000, 4, "夢の向こうがわ", "The Far Side of Dreams", "梦的彼岸"),
            Def("feat.runs.1", FeatKind.Runs, 1, 1, "最初の一歩", "First Step", "最初的一步"),
            Def("feat.runs.5", FeatKind.Runs, 3, 1, "足慣らし", "Warming Up", "热身准备"),
            Def("feat.runs.2", FeatKind.Runs, 10, 2, "旅慣れた者", "Seasoned Traveler", "老练的旅人"),
            Def("feat.runs.6", FeatKind.Runs, 20, 2, "夢へ通う者", "Dream Commuter", "常入梦境者"),
            Def("feat.runs.3", FeatKind.Runs, 30, 3, "夢の常連", "Dream Regular", "梦境常客"),
            Def("feat.runs.7", FeatKind.Runs, 60, 3, "幾度も潜る者", "Delver of Many Runs", "屡屡下潜者"),
            Def("feat.runs.4", FeatKind.Runs, 100, 4, "終わらない旅", "Endless Journey", "无尽的旅程"),
            Def("feat.runs.8", FeatKind.Runs, 200, 4, "尽きせぬ旅路", "Unending Road", "不会穷尽的旅途"),
            Def("feat.victories.1", FeatKind.Victories, 1, 1, "夢を越えて", "Beyond the Dream", "越过梦境"),
            Def("feat.victories.4", FeatKind.Victories, 2, 1, "もう一度越えて", "Over Again", "再度越过"),
            Def("feat.victories.2", FeatKind.Victories, 5, 2, "踏破者", "Conqueror", "踏破者"),
            Def("feat.victories.5", FeatKind.Victories, 10, 2, "夢を制す者", "Dream Tamer", "制梦者"),
            Def("feat.victories.3", FeatKind.Victories, 20, 3, "夢の覇者", "Master of Dreams", "梦境霸者"),
            Def("feat.victories.6", FeatKind.Victories, 35, 3, "夢の征服者", "Conqueror of Dreams", "征服梦境者"),
            Def("feat.victories.7", FeatKind.Victories, 75, 4, "揺るがぬ覇者", "Unshaken Champion", "不动摇的霸者"),
            Def("feat.legendaries.1", FeatKind.Legendaries, 1, 1, "伝説との出会い", "First Legend", "与传说相遇"),
            Def("feat.legendaries.5", FeatKind.Legendaries, 5, 2, "伝説を重ねて", "Legends Piling Up", "传说迭出"),
            Def("feat.legendaries.2", FeatKind.Legendaries, 10, 2, "伝説の収集家", "Legend Collector", "传说收藏家"),
            Def("feat.legendaries.6", FeatKind.Legendaries, 20, 3, "伝説を束ねる者", "Binder of Legends", "束起传说者"),
            Def("feat.legendaries.3", FeatKind.Legendaries, 30, 3, "伝説の守り手", "Keeper of Legends", "传说的守护者"),
            Def("feat.legendaries.7", FeatKind.Legendaries, 45, 3, "伝説の山", "Mountain of Legends", "传说之山"),
            Def("feat.legendaries.4", FeatKind.Legendaries, 60, 4, "生ける伝説", "Living Legend", "活着的传说"),
            Def("feat.legendaries.8", FeatKind.Legendaries, 90, 4, "夢の宝庫", "Vault of Dreams", "梦境宝库"),
            Def("feat.relics.1", FeatKind.Relics, 50, 1, "遺物拾い", "Relic Gatherer", "拾取遗物"),
            Def("feat.relics.4", FeatKind.Relics, 150, 2, "遺物を担ぐ者", "Relic Porter", "背负遗物者"),
            Def("feat.relics.2", FeatKind.Relics, 300, 2, "遺物の目利き", "Relic Connoisseur", "遗物鉴定家"),
            Def("feat.relics.5", FeatKind.Relics, 600, 3, "夢の蒐集家", "Dream Collector", "梦境收藏家"),
            Def("feat.relics.3", FeatKind.Relics, 1000, 3, "夢の倉庫番", "Keeper of the Dream Vault", "梦境仓库管理员"),
            Def("feat.relics.6", FeatKind.Relics, 2000, 4, "遺物の海", "Sea of Relics", "遗物之海"),
            Def("feat.relics.7", FeatKind.Relics, 5000, 4, "残さず拾う者", "Leaves Nothing Behind", "一件不剩的拾取者"),
            Def("feat.relics.8", FeatKind.Relics, 10000, 4, "拾わぬ夢はない", "No Dream Left Behind", "无梦不拾"),
            Def("feat.codex.1", FeatKind.CodexPct, 25, 1, "図鑑の書き出し", "Opening Pages", "图鉴的开篇"),
            Def("feat.codex.2", FeatKind.CodexPct, 50, 2, "半分の夢", "Half the Dream", "一半的梦"),
            Def("feat.codex.3", FeatKind.CodexPct, 75, 3, "図鑑の大家", "Codex Scholar", "图鉴大家"),
            Def("feat.codex.4", FeatKind.CodexPct, 100, 4, "すべての夢を知る者", "Knower of All Dreams", "知晓所有梦者"),
            Def("feat.depth.1", FeatKind.DepthSecured, 2, 1, "深みへ", "Into the Depths", "走向深处"),
            Def("feat.depth.2", FeatKind.DepthSecured, 3, 2, "深淵を覗く者", "Abyss Gazer", "窥视深渊者"),
            Def("feat.depth.3", FeatKind.DepthSecured, 5, 3, "底なしの夢", "Bottomless Dream", "无底的梦"),
            Def("feat.nightmares.1", FeatKind.Nightmares, 10, 1, "悪夢狩り", "Nightmare Hunter", "猎杀噩梦"),
            Def("feat.nightmares.2", FeatKind.Nightmares, 100, 2, "悪夢の天敵", "Bane of Nightmares", "噩梦的克星"),
            Def("feat.nightmares.3", FeatKind.Nightmares, 500, 3, "悪夢を喰らう者", "Devourer of Nightmares", "吞食噩梦者"),
            Def("feat.nightmares.4", FeatKind.Nightmares, 1000, 4, "夜を灼く者", "Scorcher of the Night", "灼烧黑夜者"),
            Def("feat.nightmares.5", FeatKind.Nightmares, 2000, 4, "悪夢を滅ぼす者", "Vanquisher of Nightmares", "讨灭噩梦者"),
            Def("feat.level.1", FeatKind.DreamLevel, 10, 1, "夢見る者", "Dreamer", "做梦的人"),
            Def("feat.level.4", FeatKind.DreamLevel, 15, 2, "夢の中路", "Midway of Dreams", "梦的中途"),
            Def("feat.level.2", FeatKind.DreamLevel, 20, 2, "夢を織る者", "Dreamweaver", "织梦者"),
            Def("feat.level.5", FeatKind.DreamLevel, 25, 2, "夢の坂道", "The Slope of Dreams", "梦的坡道"),
            Def("feat.level.3", FeatKind.DreamLevel, 30, 3, "夢の主", "Lord of Dreams", "梦境之主"),
            Def("feat.pacts.1", FeatKind.Pacts, 1, 1, "最初の契約", "First Pact", "最初的契约"),
            Def("feat.pacts.4", FeatKind.Pacts, 5, 2, "いくつもの契約", "A Handful of Pacts", "数份契约"),
            Def("feat.pacts.2", FeatKind.Pacts, 10, 2, "契約に慣れた者", "Pact Veteran", "习惯契约者"),
            Def("feat.pacts.5", FeatKind.Pacts, 25, 3, "悪夢と語らう者", "Speaker with Nightmares", "与噩梦交谈者"),
            Def("feat.pacts.3", FeatKind.Pacts, 50, 3, "悪夢と結ばれし者", "Bound to Nightmares", "与噩梦相连者"),
            Def("feat.pacts.6", FeatKind.Pacts, 100, 4, "契約に生きる者", "One Who Lives by Pacts", "以契约为生者"),
            Def("feat.pacts.7", FeatKind.Pacts, 200, 4, "数え切れぬ契約", "Countless Pacts", "数不清的契约"),
            Def("feat.events.1", FeatKind.Events, 1, 1, "不思議な出会い", "Strange Encounter", "奇妙的相遇"),
            Def("feat.events.4", FeatKind.Events, 3, 1, "夢の分かれ道", "Dream Crossroads", "梦的岔路"),
            Def("feat.events.2", FeatKind.Events, 10, 2, "寄り道好き", "Fond of Detours", "喜欢绕路的人"),
            Def("feat.events.5", FeatKind.Events, 25, 2, "出来事を巡る者", "Traveler of Happenings", "巡游诸事者"),
            Def("feat.events.3", FeatKind.Events, 50, 3, "夢の語り部", "Teller of Dreams", "梦的讲述者"),
            Def("feat.events.6", FeatKind.Events, 100, 3, "夢の道しるべ", "Dream Wayfinder", "梦的路标"),
            Def("feat.events.7", FeatKind.Events, 250, 4, "夢の案内人", "Guide of Dreams", "梦的引路人"),
            Def("feat.bounties.1", FeatKind.Bounties, 10, 1, "頼れる手", "Helping Hand", "可靠的帮手"),
            Def("feat.bounties.4", FeatKind.Bounties, 25, 2, "腕の見せ所", "A Chance to Shine", "大显身手之处"),
            Def("feat.bounties.2", FeatKind.Bounties, 50, 2, "依頼の達人", "Bounty Expert", "委托达人"),
            Def("feat.bounties.5", FeatKind.Bounties, 100, 3, "夢の何でも屋", "Fixer of Dreams", "梦境万事通"),
            Def("feat.bounties.3", FeatKind.Bounties, 150, 3, "夢の便利屋", "Dream Errand Master", "梦境杂务能手"),
            Def("feat.bounties.6", FeatKind.Bounties, 300, 4, "依頼の大黒柱", "Pillar of Errands", "委托的顶梁柱"),
            Def("feat.bounties.7", FeatKind.Bounties, 600, 4, "頼られる重鎮", "Trusted Mainstay", "受人倚重的中坚"),
            Def("feat.sets.1", FeatKind.SetsCompleted, 1, 1, "揃いの装い", "Matching Set", "成套的装束"),
            Def("feat.sets.2", FeatKind.SetsCompleted, 4, 2, "衣装持ち", "Well Dressed", "衣着讲究者"),
            Def("feat.sets.4", FeatKind.SetsCompleted, 8, 3, "揃いを重ねて", "Stacking Sets", "成套又成套"),
            Def("feat.sets.3", FeatKind.SetsCompleted, 12, 3, "すべての装束", "Every Raiment", "全部装束"),
            Def("feat.sets.5", FeatKind.SetsCompleted, 16, 3, "揃いに囲まれて", "Surrounded by Sets", "被成套环绕"),
            Def("feat.sets.6", FeatKind.SetsCompleted, 24, 4, "衣装部屋の主", "Master of the Wardrobe", "衣柜之主"),
            Def("feat.sets.7", FeatKind.SetsCompleted, 36, 4, "装い極めし者", "Dressed to Perfection", "装束至上者"),
            Def("feat.awakening.1", FeatKind.RelicsAwakened, 1, 1, "目覚めの刻", "First Awakening", "觉醒之刻", rewardShards: 60),
            Def("feat.awakening.2", FeatKind.RelicsAwakened, 5, 3, "覚醒の主", "Master of Awakening", "觉醒之主", rewardShards: 150),
            Def("feat.awakening.3", FeatKind.RelicsAwakened, 10, 3, "覚醒の連なり", "Chain of Awakenings", "觉醒的相连", rewardShards: 250),
            Def("feat.awakening.4", FeatKind.RelicsAwakened, 20, 4, "覚醒の深み", "Depths of Awakening", "觉醒的深处", rewardShards: 400),
            Def("feat.awakening.5", FeatKind.RelicsAwakened, 40, 4, "覚醒の果て", "The Far End of Awakening", "觉醒的尽头", rewardShards: 600),
            Def("feat.awakening.6", FeatKind.RelicsAwakened, 60, 4, "覚醒の彼方", "Beyond Awakening", "觉醒的彼方", rewardShards: 800),
        };

        private static FeatDef Def(string id, FeatKind kind, int target, int stage, string ja, string en, string zh = null, int? rewardShards = null)
        {
            return new FeatDef
            {
                Id = id,
                Name = new Txt(ja, en, zh),
                Kind = kind,
                Target = target,
                RewardShards = rewardShards ?? (stage == 1 ? 30 : stage == 2 ? 80 : stage == 3 ? 200 : 400),
                RewardTuning = stage == 1 ? 0 : stage == 2 ? 1 : stage == 3 ? 3 : 5,
            };
        }

        public static int Progress(Profile p, FeatDef f)
        {
            switch (f.Kind)
            {
                case FeatKind.Kills: return p.Stats.Kills;
                case FeatKind.Runs: return p.Stats.Runs;
                case FeatKind.Victories: return p.Stats.Victories;
                case FeatKind.Legendaries: return p.Stats.LegendariesFound;
                case FeatKind.Relics: return p.Stats.RelicsFound;
                case FeatKind.CodexPct: return (int)((long)p.Codex.Count * 100 / (Content.Bases.Count + Content.Uniques.Count + NamedItems.All.Count));
                case FeatKind.DepthSecured: return p.Stats.BestHeatSecured;
                case FeatKind.Nightmares: return p.Stats.NightmaresSlain;
                case FeatKind.DreamLevel: return p.DreamLevel;
                case FeatKind.Pacts: return p.Stats.PactsSworn;
                case FeatKind.Events: return p.Stats.EventsUsed;
                case FeatKind.Bounties: return p.Stats.BountiesDone;
                case FeatKind.SetsCompleted: return CompletedSets(p);
                case FeatKind.RelicsAwakened: return p.Stats.RelicsAwakened;
                default: throw new ArgumentOutOfRangeException(nameof(f), "Unknown feat kind.");
            }
        }

        private struct SetRelicState
        {
            public Relic Relic;
            public string BaseId;
            public string UniqueId;
        }

        private sealed class SetProgressCache
        {
            public readonly List<SetRelicState> Stash = new List<SetRelicState>();
            public int Completed;
        }

        private static readonly ConditionalWeakTable<Profile, SetProgressCache> SetProgress =
            new ConditionalWeakTable<Profile, SetProgressCache>();

        private static int CompletedSets(Profile p)
        {
            var cache = SetProgress.GetValue(p, _ => new SetProgressCache());
            bool unchanged = cache.Stash.Count == p.Stash.Count;
            if (unchanged)
            {
                for (int i = 0; i < p.Stash.Count; i++)
                {
                    var relic = p.Stash[i];
                    var previous = cache.Stash[i];
                    if (ReferenceEquals(previous.Relic, relic) && previous.BaseId == relic.BaseId && previous.UniqueId == relic.UniqueId) continue;
                    unchanged = false;
                    break;
                }
            }
            if (unchanged) return cache.Completed;

            // Stash は公開 List。個数だけでなく、同数の入れ替えとセット識別情報の変更も検出する。
            cache.Stash.Clear();
            foreach (var relic in p.Stash)
                cache.Stash.Add(new SetRelicState { Relic = relic, BaseId = relic.BaseId, UniqueId = relic.UniqueId });
            cache.Completed = CountCompletedSets(p);
            return cache.Completed;
        }

        private static int CountCompletedSets(Profile p)
        {
            int completed = 0;
            for (int i = 0; i < Content.Sets.Count; i++)
            {
                bool hasPieces = false;
                bool complete = true;
                string setId = Content.Sets[i].Id;
                foreach (var piece in Content.Uniques)
                {
                    if (piece.SetId != setId) continue;
                    hasPieces = true;
                    bool owned = false;
                    foreach (var relic in p.Stash)
                    {
                        if (relic.UniqueId != piece.Id || relic.BaseId != piece.BaseId) continue;
                        owned = true;
                        break;
                    }
                    if (owned) continue;
                    complete = false;
                    break;
                }
                if (hasPieces && complete) completed++;
            }
            return completed;
        }

        public static string Describe(FeatDef f)
        {
            int n = f.Target;
            switch (f.Kind)
            {
                case FeatKind.Kills: return Loc.T($"敵を合計{n}体倒す", $"Defeat {n} enemies in total", $"合计击败{n}个敌人");
                case FeatKind.Runs: return n == 1 ? Loc.T("遠征に1回出る", "Go on 1 expedition", "出1次远征") : Loc.T($"遠征に{n}回出る", $"Go on {n} expeditions", $"出{n}次远征");
                case FeatKind.Victories: return Loc.T($"夢を{n}回踏破する", $"Conquer the dream {n} times", $"踏破梦境{n}次");
                case FeatKind.Legendaries: return Loc.T($"固有品を合計{n}個見つける", $"Find {n} legendaries in total", $"合计找到{n}件传说");
                case FeatKind.Relics: return Loc.T($"遺物を合計{n}個見つける", $"Find {n} relics in total", $"合计找到{n}件遗物");
                case FeatKind.CodexPct: return Loc.T($"図鑑を{n}%埋める", $"Fill {n}% of the codex", $"填满图鉴的{n}%");
                case FeatKind.DepthSecured: return Loc.T($"潜行{n}以上で確保する", $"Secure at delve {n} or deeper", $"在深度{n}以上存入仓库");
                case FeatKind.Nightmares: return Loc.T($"悪夢化した敵を合計{n}体倒す", $"Slay {n} nightmares in total", $"合计击败{n}个梦魇化敌人");
                case FeatKind.DreamLevel: return Loc.T($"夢のレベルを{n}にする", $"Reach Dream Level {n}", $"将梦境等级达到{n}");
                case FeatKind.Pacts: return Loc.T($"悪夢の契約を合計{n}回結ぶ", $"Swear {n} nightmare pacts", $"合计结下{n}次梦魇契约");
                case FeatKind.Events: return Loc.T($"夢の出来事を合計{n}回選ぶ", $"Take {n} dream events", $"合计选择{n}次梦境事件");
                case FeatKind.Bounties: return Loc.T($"依頼を合計{n}回達成する", $"Complete {n} bounties", $"合计完成{n}次委托");
                case FeatKind.SetsCompleted: return Loc.T($"セットを{n}種類そろえる（全部位）", $"Complete {n} different sets (all pieces)", $"集齐{n}种套装（全部位）");
                case FeatKind.RelicsAwakened: return n == 1
                    ? Loc.T("固有品を1つ覚醒させる", "Awaken 1 legendary relic", "觉醒1件传说")
                    : Loc.T($"固有品を合計{n}つ覚醒させる", $"Awaken {n} legendary relics in total", $"合计觉醒{n}件传说");
                default: throw new ArgumentOutOfRangeException(nameof(f), "Unknown feat kind.");
            }
        }

        public static int Unclaimed(Profile p)
        {
            int count = 0;
            for (int i = 0; i < All.Count; i++)
            {
                string id = All[i].Id;
                if (p.Feats.Contains(id) && !p.FeatsClaimed.Contains(id)) count++;
            }
            return count;
        }

        public static IReadOnlyList<GameEvent> Check(Profile p)
        {
            List<GameEvent> ev = null;
            var lastKind = (FeatKind)(-1);
            int progress = 0;
            for (int i = 0; i < All.Count; i++)
            {
                var f = All[i];
                if (p.Feats.Contains(f.Id)) continue;
                // All は種類ごとに並ぶ。同じ進捗（特にセットの所持判定）は1回だけ調べる。
                if (f.Kind != lastKind)
                {
                    lastKind = f.Kind;
                    progress = Progress(p, f);
                }
                if (progress < f.Target) continue;
                p.Feats.Add(f.Id);
                if (ev == null) ev = new List<GameEvent>();
                ev.Add(new GameEvent(EventKind.Info, Loc.T(
                    $"偉業「{f.Name}」を達成！ 記録タブで報酬を受け取れます",
                    $"Feat \"{f.Name}\" complete! Claim the reward in the Records tab.",
                    $"达成伟业“{f.Name}”！可在记录页领取奖励")));
            }
            return ev ?? (IReadOnlyList<GameEvent>)Array.Empty<GameEvent>();
        }
    }
}
