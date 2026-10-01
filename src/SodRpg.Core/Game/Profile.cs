using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>キャラ（Traveler）ごとの装着と専門化。キーはゲーム側の Hero 型名（例: Hero_Lacerta）。</summary>
    public sealed class HeroState
    {
        /// <summary>枠ごとの装着中の遺物Uid。未装着は null。保管庫の遺物を参照する。</summary>
        public string[] Equipped { get; } = new string[3];

        /// <summary>小ノードの段階。到達ノード（刻印）はここに含めず Keystone に持つ。</summary>
        public SortedDictionary<string, int> Talents { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public string Keystone { get; set; }

        /// <summary>このキャラでの撃破数（熟練度）。</summary>
        public int Kills { get; set; }

        public HeroState Clone()
        {
            var c = new HeroState { Keystone = Keystone, Kills = Kills };
            Array.Copy(Equipped, c.Equipped, 3);
            foreach (var kv in Talents) c.Talents[kv.Key] = kv.Value;
            return c;
        }
    }

    /// <summary>
    /// 進行中の遠征（1回のラン）。持ち物は「未確保」で、確保地点（ゾーンの切り替わり）か勝利で保管庫へ移る。
    /// 全滅すると遺物は遺失物へ、素材は25%だけ持ち帰る（計画書 第5章）。
    /// </summary>
    public sealed class RunState
    {
        public string RunId { get; set; }
        /// <summary>夢の深度（0〜5）。確保を見送って潜り続けるほど上がる。</summary>
        public int Heat { get; set; }
        public List<Relic> Satchel { get; } = new List<Relic>();
        /// <summary>このランの依頼。</summary>
        public List<Bounty> Bounties { get; } = new List<Bounty>();
        /// <summary>結んでいる悪夢の契約（次の確保で解ける）。</summary>
        public List<Pact> Pacts { get; } = new List<Pact>();
        /// <summary>確保地点で提示中の契約。</summary>
        public List<Pact> OfferedPacts { get; } = new List<Pact>();
        public int SatchelShards { get; set; }
        public int SatchelTuning { get; set; }
        public int RoomsCleared { get; set; }
        public bool LostRecovered { get; set; }
        public int SecuredCount { get; set; }
        public int Kills { get; set; }
        /// <summary>このランで最も深かった深度。</summary>
        public int PeakHeat { get; set; }
        public int RelicsFound { get; set; }
        public int RelicsSecured { get; set; }
        public int ShardsSecured { get; set; }
        /// <summary>ラン開始時の夢のレベル（結果表示用）。</summary>
        public int LevelAtStart { get; set; }
        /// <summary>このランの「今日の夢」（開始日に決まる）。0 はなし。</summary>
        public int DailyId { get; set; }
        /// <summary>このランの開始深度。確保するとここまで戻る。</summary>
        public int StartDepth { get; set; }
        /// <summary>確保地点で選択待ちか。選ぶまで装備の変更ができる。</summary>
        public bool AwaitingChoice { get; set; }

        public bool HasUnsecured => Satchel.Count > 0 || SatchelShards > 0 || SatchelTuning > 0;

        public RunState Clone()
        {
            var c = new RunState
            {
                RunId = RunId,
                Heat = Heat,
                SatchelShards = SatchelShards,
                SatchelTuning = SatchelTuning,
                RoomsCleared = RoomsCleared,
                LostRecovered = LostRecovered,
                SecuredCount = SecuredCount,
                Kills = Kills,
                PeakHeat = PeakHeat,
                RelicsFound = RelicsFound,
                RelicsSecured = RelicsSecured,
                ShardsSecured = ShardsSecured,
                LevelAtStart = LevelAtStart,
                DailyId = DailyId,
                StartDepth = StartDepth,
                AwaitingChoice = AwaitingChoice,
            };
            foreach (var r in Satchel) c.Satchel.Add(r.Clone());
            foreach (var b in Bounties) c.Bounties.Add(b.Clone());
            c.Pacts.AddRange(Pacts);
            c.OfferedPacts.AddRange(OfferedPacts);
            return c;
        }
    }

    /// <summary>1回のランの結果。</summary>
    public sealed class RunReport
    {
        public bool Victory { get; set; }
        public int Kills { get; set; }
        public int RelicsFound { get; set; }
        public int RelicsSecured { get; set; }
        public int RelicsLost { get; set; }
        public int ShardsSecured { get; set; }
        public int EchoShards { get; set; }
        public int PeakHeat { get; set; }
        public int SecuredCount { get; set; }
        public int LevelBefore { get; set; }
        public int LevelAfter { get; set; }
        public int BountiesDone { get; set; }
        public int BountiesTotal { get; set; }
    }

    public sealed class ProfileStats
    {
        public int Runs { get; set; }
        public int Victories { get; set; }
        public int Defeats { get; set; }
        public int RelicsFound { get; set; }
        public int LegendariesFound { get; set; }
        public int BestHeatSecured { get; set; }
        public int Kills { get; set; }
        public int NightmaresSlain { get; set; }
        /// <summary>踏破したときの最も深い開始深度（-1は未踏破）。</summary>
        public int BestVictoryStartDepth { get; set; } = -1;

        public ProfileStats Clone() => (ProfileStats)MemberwiseClone();
    }

    /// <summary>
    /// 1人のプレイヤーの恒久データ。各PCが自分の分だけを保存する（協力時もホストは他人の保存に触れない）。
    /// </summary>
    public sealed class Profile
    {
        public const int CurrentVersion = 1;

        public long Revision { get; set; }
        public ulong RngState { get; set; }
        public int DreamLevel { get; set; } = 1;
        public int DreamXp { get; set; }
        public int EpicPity { get; set; }
        public int BestItemLevel { get; set; } = 1;
        public bool Japanese { get; set; } = true;
        /// <summary>遠征を始めるときの夢の深度（深淵の段階）。確保できた最高深度まで選べる。</summary>
        public int StartDepth { get; set; }

        public SortedDictionary<string, int> Materials { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public List<Relic> Stash { get; } = new List<Relic>();
        public List<Relic> LostAndFound { get; } = new List<Relic>();
        public SortedDictionary<string, HeroState> Heroes { get; } = new SortedDictionary<string, HeroState>(StringComparer.Ordinal);
        public SortedSet<string> Codex { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public ProfileStats Stats { get; private set; } = new ProfileStats();
        public RunState Run { get; set; }

        /// <summary>狙い系統。設定するとその系統の装備が出やすくなる。null は狙いなし。</summary>
        public Line? Focus { get; set; }

        /// <summary>直前に終わったランの結果（表示用。保存しない）。</summary>
        public RunReport LastReport { get; set; }

        public static Profile CreateNew(ulong seed)
        {
            return new Profile { RngState = seed == 0 ? 0x5EED5EEDUL : seed };
        }

        public int Material(string id) => Materials.TryGetValue(id, out int n) ? n : 0;

        public void AddMaterial(string id, int amount)
        {
            if (amount == 0) return;
            long next = (long)Material(id) + amount;
            if (next < 0) throw new InvalidOperationException("素材が足りません: " + id);
            Materials[id] = (int)Math.Min(int.MaxValue, next);
        }

        public HeroState Hero(string heroKey)
        {
            if (string.IsNullOrEmpty(heroKey)) heroKey = "default";
            if (!Heroes.TryGetValue(heroKey, out var h))
            {
                h = new HeroState();
                Heroes[heroKey] = h;
            }
            return h;
        }

        public Relic FindStash(string uid)
        {
            if (uid == null) return null;
            foreach (var r in Stash)
                if (r.Uid == uid) return r;
            return null;
        }

        public bool IsEquippedAnywhere(string uid)
        {
            foreach (var h in Heroes.Values)
                foreach (var e in h.Equipped)
                    if (e == uid) return true;
            return false;
        }

        /// <summary>使える専門化ポイントの総数（夢のレベル−1）。全キャラ共通の総数を、キャラごとに配分する。</summary>
        public int TalentPoints => Math.Max(0, DreamLevel - 1) + CodexBonusPoints;

        /// <summary>図鑑の節目（6種ごと）で得る星図ポイント。最大4。</summary>
        public int CodexBonusPoints => Math.Min(Content.MaxCodexBonus, Codex.Count / Content.CodexPerPoint);

        public Rng TakeRng() => new Rng(RngState);

        public void StoreRng(Rng rng) => RngState = rng.State;

        public Profile Clone()
        {
            var c = new Profile
            {
                Revision = Revision,
                RngState = RngState,
                DreamLevel = DreamLevel,
                DreamXp = DreamXp,
                EpicPity = EpicPity,
                BestItemLevel = BestItemLevel,
                Japanese = Japanese,
                StartDepth = StartDepth,
                Stats = Stats.Clone(),
                Run = Run?.Clone(),
                Focus = Focus,
                LastReport = LastReport,
            };
            foreach (var kv in Materials) c.Materials[kv.Key] = kv.Value;
            foreach (var r in Stash) c.Stash.Add(r.Clone());
            foreach (var r in LostAndFound) c.LostAndFound.Add(r.Clone());
            foreach (var kv in Heroes) c.Heroes[kv.Key] = kv.Value.Clone();
            foreach (var s in Codex) c.Codex.Add(s);
            return c;
        }
    }
}
