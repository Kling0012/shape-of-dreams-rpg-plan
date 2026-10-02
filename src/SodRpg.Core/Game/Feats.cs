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
            Def("feat.kills.1", FeatKind.Kills, 100, 1, "夢の狩人見習い", "Apprentice Hunter"),
            Def("feat.kills.2", FeatKind.Kills, 500, 2, "夢の狩人", "Dream Hunter"),
            Def("feat.kills.3", FeatKind.Kills, 2000, 3, "悪夢を払う者", "Nightmare Sweeper"),
            Def("feat.kills.4", FeatKind.Kills, 10000, 4, "千の夢を斬る者", "Slayer of a Thousand Dreams"),
            Def("feat.runs.1", FeatKind.Runs, 1, 1, "最初の一歩", "First Step"),
            Def("feat.runs.2", FeatKind.Runs, 10, 2, "旅慣れた者", "Seasoned Traveler"),
            Def("feat.runs.3", FeatKind.Runs, 30, 3, "夢の常連", "Dream Regular"),
            Def("feat.runs.4", FeatKind.Runs, 100, 4, "終わらない旅", "Endless Journey"),
            Def("feat.victories.1", FeatKind.Victories, 1, 1, "夢を越えて", "Beyond the Dream"),
            Def("feat.victories.2", FeatKind.Victories, 5, 2, "踏破者", "Conqueror"),
            Def("feat.victories.3", FeatKind.Victories, 20, 3, "夢の覇者", "Master of Dreams"),
            Def("feat.legendaries.1", FeatKind.Legendaries, 1, 1, "伝説との出会い", "First Legend"),
            Def("feat.legendaries.2", FeatKind.Legendaries, 10, 2, "伝説の収集家", "Legend Collector"),
            Def("feat.legendaries.3", FeatKind.Legendaries, 30, 3, "伝説の守り手", "Keeper of Legends"),
            Def("feat.legendaries.4", FeatKind.Legendaries, 60, 4, "生ける伝説", "Living Legend"),
            Def("feat.relics.1", FeatKind.Relics, 50, 1, "遺物拾い", "Relic Gatherer"),
            Def("feat.relics.2", FeatKind.Relics, 300, 2, "遺物の目利き", "Relic Connoisseur"),
            Def("feat.relics.3", FeatKind.Relics, 1000, 3, "夢の倉庫番", "Keeper of the Dream Vault"),
            Def("feat.codex.1", FeatKind.CodexPct, 25, 1, "図鑑の書き出し", "Opening Pages"),
            Def("feat.codex.2", FeatKind.CodexPct, 50, 2, "半分の夢", "Half the Dream"),
            Def("feat.codex.3", FeatKind.CodexPct, 75, 3, "図鑑の大家", "Codex Scholar"),
            Def("feat.codex.4", FeatKind.CodexPct, 100, 4, "すべての夢を知る者", "Knower of All Dreams"),
            Def("feat.depth.1", FeatKind.DepthSecured, 2, 1, "深みへ", "Into the Depths"),
            Def("feat.depth.2", FeatKind.DepthSecured, 3, 2, "深淵を覗く者", "Abyss Gazer"),
            Def("feat.depth.3", FeatKind.DepthSecured, 5, 3, "底なしの夢", "Bottomless Dream"),
            Def("feat.nightmares.1", FeatKind.Nightmares, 10, 1, "悪夢狩り", "Nightmare Hunter"),
            Def("feat.nightmares.2", FeatKind.Nightmares, 100, 2, "悪夢の天敵", "Bane of Nightmares"),
            Def("feat.nightmares.3", FeatKind.Nightmares, 500, 3, "悪夢を喰らう者", "Devourer of Nightmares"),
            Def("feat.level.1", FeatKind.DreamLevel, 10, 1, "夢見る者", "Dreamer"),
            Def("feat.level.2", FeatKind.DreamLevel, 20, 2, "夢を織る者", "Dreamweaver"),
            Def("feat.level.3", FeatKind.DreamLevel, 30, 3, "夢の主", "Lord of Dreams"),
            Def("feat.pacts.1", FeatKind.Pacts, 1, 1, "最初の契約", "First Pact"),
            Def("feat.pacts.2", FeatKind.Pacts, 10, 2, "契約に慣れた者", "Pact Veteran"),
            Def("feat.pacts.3", FeatKind.Pacts, 50, 3, "悪夢と結ばれし者", "Bound to Nightmares"),
            Def("feat.events.1", FeatKind.Events, 1, 1, "不思議な出会い", "Strange Encounter"),
            Def("feat.events.2", FeatKind.Events, 10, 2, "寄り道好き", "Fond of Detours"),
            Def("feat.events.3", FeatKind.Events, 50, 3, "夢の語り部", "Teller of Dreams"),
            Def("feat.bounties.1", FeatKind.Bounties, 10, 1, "頼れる手", "Helping Hand"),
            Def("feat.bounties.2", FeatKind.Bounties, 50, 2, "依頼の達人", "Bounty Expert"),
            Def("feat.bounties.3", FeatKind.Bounties, 150, 3, "夢の便利屋", "Dream Errand Master"),
            Def("feat.sets.1", FeatKind.SetsCompleted, 1, 1, "揃いの装い", "Matching Set"),
            Def("feat.sets.2", FeatKind.SetsCompleted, 4, 2, "衣装持ち", "Well Dressed"),
            Def("feat.sets.3", FeatKind.SetsCompleted, 12, 3, "すべての装束", "Every Raiment"),
        };

        private static FeatDef Def(string id, FeatKind kind, int target, int stage, string ja, string en)
        {
            return new FeatDef
            {
                Id = id,
                Name = new Txt(ja, en),
                Kind = kind,
                Target = target,
                RewardShards = stage == 1 ? 30 : stage == 2 ? 80 : stage == 3 ? 200 : 400,
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
                case FeatKind.CodexPct: return (int)((long)p.Codex.Count * 100 / (Content.Bases.Count + Content.Uniques.Count));
                case FeatKind.DepthSecured: return p.Stats.BestHeatSecured;
                case FeatKind.Nightmares: return p.Stats.NightmaresSlain;
                case FeatKind.DreamLevel: return p.DreamLevel;
                case FeatKind.Pacts: return p.Stats.PactsSworn;
                case FeatKind.Events: return p.Stats.EventsUsed;
                case FeatKind.Bounties: return p.Stats.BountiesDone;
                case FeatKind.SetsCompleted: return CompletedSets(p);
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
            if (p.Stash.Count < 3) return 0;
            int completed = 0;
            for (int i = 0; i < Content.Sets.Count; i++)
            {
                int slots = 0;
                string setId = Content.Sets[i].Id;
                foreach (var relic in p.Stash)
                {
                    if (!Content.TryGetUnique(relic.UniqueId, out var unique) || unique.SetId != setId) continue;
                    slots |= 1 << (int)relic.Slot;
                    if (slots == 7)
                    {
                        completed++;
                        break;
                    }
                }
            }
            return completed;
        }

        public static string Describe(FeatDef f)
        {
            int n = f.Target;
            switch (f.Kind)
            {
                case FeatKind.Kills: return Loc.T($"敵を合計{n}体倒す", $"Defeat {n} enemies in total");
                case FeatKind.Runs: return Loc.T($"遠征に{n}回出る", $"Go on {n} expeditions");
                case FeatKind.Victories: return Loc.T($"夢を{n}回踏破する", $"Conquer the dream {n} times");
                case FeatKind.Legendaries: return Loc.T($"固有品を合計{n}個見つける", $"Find {n} legendaries in total");
                case FeatKind.Relics: return Loc.T($"遺物を合計{n}個見つける", $"Find {n} relics in total");
                case FeatKind.CodexPct: return Loc.T($"図鑑を{n}%埋める", $"Fill {n}% of the codex");
                case FeatKind.DepthSecured: return Loc.T($"潜行{n}以上で確保する", $"Secure at delve {n} or deeper");
                case FeatKind.Nightmares: return Loc.T($"悪夢化した敵を合計{n}体倒す", $"Slay {n} nightmares in total");
                case FeatKind.DreamLevel: return Loc.T($"夢のレベルを{n}にする", $"Reach Dream Level {n}");
                case FeatKind.Pacts: return Loc.T($"悪夢の契約を合計{n}回結ぶ", $"Swear {n} nightmare pacts");
                case FeatKind.Events: return Loc.T($"夢の出来事を合計{n}回選ぶ", $"Take {n} dream events");
                case FeatKind.Bounties: return Loc.T($"依頼を合計{n}回達成する", $"Complete {n} bounties");
                case FeatKind.SetsCompleted: return Loc.T($"セットを{n}種類そろえる（3部位）", $"Complete {n} different sets (3 pieces)");
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
                    $"Feat \"{f.Name}\" complete! Claim the reward in the Records tab.")));
            }
            return ev ?? (IReadOnlyList<GameEvent>)Array.Empty<GameEvent>();
        }
    }
}
