using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>キャラ（Traveler）ごとの装着と専門化。キーはゲーム側の Hero 型名（例: Hero_Lacerta）。</summary>
    public sealed class HeroState
    {
        /// <summary>枠ごとの装着中の遺物Uid。未装着は null。保管庫の遺物を参照する。</summary>
        public string[] Equipped { get; } = new string[Content.SlotCount];

        /// <summary>小ノードの段階。到達ノード（刻印）はここに含めず Keystone に持つ。</summary>
        public SortedDictionary<string, int> Talents { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Allocated choice stars: zero-based option indices.</summary>
        public Dictionary<string, int> TalentChoices { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>選択中の到達刻印（枠）。前詰めで、Keystones[i] が i+1 個目。未選択は null。枠数は星のレベルで増える（KeystoneSlots）。</summary>
        public string[] Keystones { get; } = new string[KeystoneSlots.Max];

        /// <summary>1つ目の刻印（Keystones[0]）。互換のためプロパティのまま残す。</summary>
        public string Keystone { get => Keystones[0]; set => Keystones[0] = value; }
        /// <summary>Last explicit authored-effect migration applied to this hero's saved local IDs.</summary>
        public int AuthoredMigrationVersion { get; set; }

        /// <summary>このキャラでの撃破数（熟練度）。</summary>
        public int Kills { get; set; }

        /// <summary>この旅人の星の経験。獲得ポイントは StarProgression で求める。</summary>
        private int _starXp;
        public int StarXp { get => _starXp; set => _starXp = Math.Max(0, value); }
        public HeroState Clone()
        {
            var c = new HeroState { Kills = Kills, StarXp = StarXp, AuthoredMigrationVersion = AuthoredMigrationVersion };
            c.CopyKeystonesFrom(this);
            Array.Copy(Equipped, c.Equipped, Equipped.Length);
            foreach (var kv in Talents) c.Talents[kv.Key] = kv.Value;
            foreach (var kv in TalentChoices) c.TalentChoices[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>選択中の刻印の数（前詰めなので、null の前の連続した枠の数）。</summary>
        public int KeystoneCount
        {
            get { int n = 0; while (n < Keystones.Length && Keystones[n] != null) n++; return n; }
        }

        /// <summary>星のレベル（StarProgression.Points(StarXp)）で決まる、この旅人の刻印の枠数。</summary>
        public int KeystoneSlotCount => KeystoneSlots.CountFor(StarProgression.Points(StarXp));

        public bool HasKeystone(string id) => id != null && Array.IndexOf(Keystones, id) >= 0;

        /// <summary>空き枠へ刻印を追加する（重複はここでは確かめない）。追加した枠の番号、枠が無ければ -1。</summary>
        public int AddKeystone(string id)
        {
            int slot = KeystoneCount;
            if (slot >= Keystones.Length) return -1;
            Keystones[slot] = id;
            return slot;
        }

        /// <summary>選択中の刻印を1つ外し、残りを前詰めに詰める。外せたら true。</summary>
        public bool RemoveKeystone(string id)
        {
            int index = id == null ? -1 : Array.IndexOf(Keystones, id);
            if (index < 0) return false;
            for (int i = index; i < Keystones.Length - 1; i++) Keystones[i] = Keystones[i + 1];
            Keystones[Keystones.Length - 1] = null;
            return true;
        }

        public void ClearKeystones() => Array.Clear(Keystones, 0, Keystones.Length);

        /// <summary>他の状態の刻印枠をそのままコピーする（Clone と任意の一部コピーの共用）。</summary>
        public void CopyKeystonesFrom(HeroState other)
        {
            ClearKeystones();
            for (int i = 0; i < Keystones.Length && i < other.Keystones.Length; i++) Keystones[i] = other.Keystones[i];
        }
    }

    /// <summary>
    /// 進行中の遠征（1回のラン）。持ち物は「未確保」で、確保地点（ゾーンの切り替わり）か勝利で保管庫へ移る。
    /// 全滅すると遺物は遺失物へ、素材は25%だけ持ち帰る（計画書 第5章）。
    /// </summary>
    public sealed class RunState
    {
        public string RunId { get; set; }
        /// <summary>遠征を始めた旅人。撃破がない遠征の精算にも使う。</summary>
        public string HeroKey { get; set; }
        /// <summary>同じ確保の繰り返しで星の経験を二重に得ないための印。</summary>
        public bool StarSecureRewarded { get; set; }
        /// <summary>夢の深度（0〜5）。確保を見送って潜り続けるほど上がる。</summary>
        public int Heat { get; set; }
        private int _dreamDepth;
        /// <summary>Run-start difficulty; independent of delve heat and Limbo.</summary>
        public int DreamDepth { get => _dreamDepth; set => _dreamDepth = Game.DreamDepth.Clamp(value); }
        public Waypoint ActiveWaypoint { get; set; }
        public Waypoint PendingWaypoint { get; set; }
        public List<Waypoint> OfferedWaypoints { get; } = new List<Waypoint>();
        public bool WaypointChosen { get; set; }
        public int WaypointGeneration { get; set; }
        public int WaypointRoom { get; set; } = -1;
        public int WaypointRelicsInRoom { get; set; }
        public HashSet<int> WaypointLootRooms { get; } = new HashSet<int>();
        public int WaypointSlotCursor { get; set; }
        public bool WaypointHoardReleased { get; set; }
        public List<Relic> DeferredWaypointRelics { get; } = new List<Relic>();
        public int DeferredWaypointShards { get; set; }
        public int DeferredWaypointTuning { get; set; }
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
        /// <summary>このランで使った依頼の引き直し回数。</summary>
        public int RerollsUsed { get; set; }
        /// <summary>確保地点に現れている出来事（選択待ちの間だけ）。</summary>
        public DreamEvent OfferedEvent { get; set; }
        /// <summary>個人の出来事の提示識別子。共有の道標世代から独立し、次の提示では更新する。</summary>
        public string OfferedEventId { get; set; }
        /// <summary>出来事による撃破時の遺物ドロップ率の上乗せ（次の確保まで）。</summary>
        public double EventDropBonus { get; set; }
        /// <summary>出来事による撃破時の幸運（次の確保まで）。</summary>
        public double EventLuck { get; set; }
        /// <summary>本体の Limbo 深度（ラン開始時に読む。0 は Limbo 以外）。</summary>
        public int LimboDepth { get; set; }
        /// <summary>確保地点で選択待ちか。選ぶまで装備の変更ができる。</summary>
        public bool AwaitingChoice { get; set; }
        /// <summary>純白の入口の明示選択を提示済みか。旧保存では一度だけ補う。</summary>
        public bool PureWhiteChoiceReached { get; set; }
        /// <summary>確保地点での選択が終わってから、次の敵を倒すまで装備を整えられる。</summary>
        public bool GearWindow { get; set; }

        public bool HasUnsecured => Satchel.Count > 0 || SatchelShards > 0 || SatchelTuning > 0;

        public RunState Clone()
        {
            var c = new RunState
            {
                RunId = RunId,
                HeroKey = HeroKey,
                StarSecureRewarded = StarSecureRewarded,
                Heat = Heat,
                DreamDepth = DreamDepth,
                ActiveWaypoint = ActiveWaypoint,
                PendingWaypoint = PendingWaypoint,
                WaypointChosen = WaypointChosen,
                WaypointGeneration = WaypointGeneration,
                WaypointRoom = WaypointRoom,
                WaypointRelicsInRoom = WaypointRelicsInRoom,
                WaypointSlotCursor = WaypointSlotCursor,
                WaypointHoardReleased = WaypointHoardReleased,
                DeferredWaypointShards = DeferredWaypointShards,
                DeferredWaypointTuning = DeferredWaypointTuning,
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
                RerollsUsed = RerollsUsed,
                OfferedEvent = OfferedEvent,
                OfferedEventId = OfferedEventId,
                EventDropBonus = EventDropBonus,
                EventLuck = EventLuck,
                LimboDepth = LimboDepth,
                AwaitingChoice = AwaitingChoice,
                PureWhiteChoiceReached = PureWhiteChoiceReached,
                GearWindow = GearWindow,
            };
            foreach (var r in Satchel) c.Satchel.Add(r.Clone());
            foreach (var b in Bounties) c.Bounties.Add(b.Clone());
            c.Pacts.AddRange(Pacts);
            c.OfferedPacts.AddRange(OfferedPacts);
            c.OfferedWaypoints.AddRange(OfferedWaypoints);
            foreach (int room in WaypointLootRooms) c.WaypointLootRooms.Add(room);
            foreach (var r in DeferredWaypointRelics) c.DeferredWaypointRelics.Add(r.Clone());
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
        /// <summary>覚醒させた伝説の遺物の累計。</summary>
        public int RelicsAwakened { get; set; }
        /// <summary>倒した夢の変種の数（v1.24）。</summary>
        public int VariantsSlain { get; set; }
        public int BestHeatSecured { get; set; }
        public int Kills { get; set; }
        public int NightmaresSlain { get; set; }
        public int PactsSworn { get; set; }
        public int EventsUsed { get; set; }
        public int BountiesDone { get; set; }
        /// <summary>踏破したときの最も深い開始深度（-1は未踏破）。</summary>
        public int BestVictoryStartDepth { get; set; } = -1;

        public ProfileStats Clone() => (ProfileStats)MemberwiseClone();
    }

    public enum SalvageReturnTarget
    {
        Stash,
        LostAndFound,
    }

    /// <summary>確保・遠征の精算から外した、分解の応答待ちの遺物。</summary>
    public sealed class PendingSalvage
    {
        public PendingSalvage(Relic relic, SalvageReturnTarget returnTarget)
        {
            Relic = relic;
            ReturnTarget = returnTarget;
        }

        public Relic Relic { get; }
        public SalvageReturnTarget ReturnTarget { get; }

        public PendingSalvage Clone() => new PendingSalvage(Relic.Clone(), ReturnTarget);
    }

    /// <summary>
    /// 1人のプレイヤーの恒久データ。各PCが自分の分だけを保存する（協力時もホストは他人の保存に触れない）。
    /// </summary>
    /// <summary>再調律で出た候補（選ぶまで保存する。読み直しで引き直せないように）。</summary>
    public sealed class RetuneOffer
    {
        public string Uid { get; set; }
        public int Index { get; set; }
        public List<StatLine> Options { get; } = new List<StatLine>();

        public RetuneOffer Clone()
        {
            var c = new RetuneOffer { Uid = Uid, Index = Index };
            c.Options.AddRange(Options);
            return c;
        }
    }

    public sealed class Profile
    {
        /// <summary>選んでいない再調律の候補。なければ null。</summary>
        public RetuneOffer RetuneOffer { get; set; }

        /// <summary>
        /// 保存の版。v1.27 で 2、v1.28 で 3、v1.31 で 4、撃破受領フロンティアの保存で 5 に上げた。
        /// 古いMODは新しい版を読み取り専用で開き（LedgerVersionException）、知らない星や遺物を捨てて上書きしない。
        /// 3→4→5 はリセットしない（ResetBeforeVersion は 3 のまま）。
        /// </summary>
        public const int CurrentVersion = 5;

        /// <summary>この版より古い保存は読み込まず、写しを残して新しいプロフィールで始める。</summary>
        public const int ResetBeforeVersion = 3;

        /// <summary>読み込んだ保存の版（保存しない）。新しく作ったプロフィールは CurrentVersion。</summary>
        public int LoadedVersion { get; set; } = CurrentVersion;

        public long Revision { get; set; }
        public ulong RngState { get; set; }
        public int DreamLevel { get; set; } = 1;
        public int DreamXp { get; set; }
        /// <summary>旧版のボス救済（天井）のカウンタ。天井は撤廃済みで、保存互換のために読み書きだけ続ける。抽選には使わない。</summary>
        public int EpicPity { get; set; }
        /// <summary>まとめて分解の対象にする最高のレア度（コモン〜エピック。固有品は入らない）。</summary>
        public Rarity BulkSalvageMaxRarity { get; set; } = Rarity.Uncommon;
        public int BestItemLevel { get; set; } = 1;
        public bool Japanese { get; set; } = true;
        /// <summary>遠征を始めるときの夢の深度（深淵の段階）。確保できた最高深度まで選べる。</summary>
        public int StartDepth { get; set; }

        private int _lastDreamDepth;
        public int LastDreamDepth { get => _lastDreamDepth; set => _lastDreamDepth = DreamDepth.Clamp(value); }

        public SortedDictionary<string, int> Materials { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public List<Relic> Stash { get; } = new List<Relic>();
        public List<Relic> LostAndFound { get; } = new List<Relic>();
        /// <summary>分解の応答待ち。装着・鍛冶・出来事の対象には含めない。</summary>
        public List<PendingSalvage> PendingSalvage { get; } = new List<PendingSalvage>();
        /// <summary>
        /// ホストの確定結果をまだ受け取っていない取引（期限切れで結果不明のものを含む）。MODの再読み込みや再起動をまたいで、
        /// 支払い済みの対価・返却を取りこぼさないために保存する。起動後にホストへ照会して解決する。
        /// </summary>
        public List<PendingTrade> PendingTrades { get; } = new List<PendingTrade>();
        public SortedDictionary<string, HeroState> Heroes { get; } = new SortedDictionary<string, HeroState>(StringComparer.Ordinal);
        public SortedSet<string> Codex { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>達成済みの偉業のID。</summary>
        public SortedSet<string> Feats { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>報酬を受け取り済みの偉業のID。</summary>
        public SortedSet<string> FeatsClaimed { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>夢の工房の段階。</summary>
        public SortedDictionary<Upgrade, int> Upgrades { get; } = new SortedDictionary<Upgrade, int>();
        /// <summary>見たヒント（Hint の値）。</summary>
        public SortedSet<int> SeenHints { get; } = new SortedSet<int>();
        /// <summary>ヒントを出さない（設定）。</summary>
        public bool HintsOff { get; set; }
        /// <summary>初期装備を配ったか。</summary>
        public bool StarterGranted { get; set; }
        /// <summary>頭・手・足の初期装備を配ったか。</summary>
        public bool StarterV119Granted { get; set; }
        /// <summary>初期装備の遺物の個体ID（自動装備に使う）。</summary>
        public List<string> StarterUids { get; } = new List<string>();
        public ProfileStats Stats { get; private set; } = new ProfileStats();
        public RunState Run { get; set; }
        public string CompletedRunId { get; set; }
        /// <summary>Expeditions already settled by returning to the lobby; native continue must not grant them another MOD run.</summary>
        public SortedSet<string> LobbyReturnedRunIds { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>Whether this slot had host authority when its lobby return began.</summary>
        public bool LobbyReturnAuthority { get; set; }
        public RunRecoveryState RunRecovery { get; set; }
        public KillClassificationCheckpoint KillClassification { get; set; }

        /// <summary>Native continue points, oldest first. Snapshot strings are immutable and shared by Clone.</summary>
        public List<RunCheckpoint> ContinueCheckpoints { get; } = new List<RunCheckpoint>();
        /// <summary>Profile at the stable lobby boundary, without nested continue data.</summary>
        public string ContinueLobbyBaseline { get; set; }
        /// <summary>The native resume session already applied locally.</summary>
        public string ContinueResumeSession { get; set; }

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
            if (next < 0) throw new InvalidOperationException(RuleMessages.NotEnoughMaterial.ToString() + id);
            Materials[id] = (int)Math.Min(int.MaxValue, next);
        }

        public HeroState Hero(string heroKey)
        {
            if (string.IsNullOrEmpty(heroKey)) heroKey = "default";
            if (!Heroes.TryGetValue(heroKey, out var h))
            {
                // A hero created now never held stars under a redefined effect, so it starts at the current revision.
                h = new HeroState { AuthoredMigrationVersion = StarClusters.MigrationsFor(heroKey).Count > 0 ? AuthoredStarMigration.CurrentVersion : 0 };
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

        /// <summary>その旅人の星ポイントに、図鑑とテスト設定の共通ボーナスを加える。</summary>
        public int TalentPoints(string heroKey) => (int)Math.Min(StarProgression.MaxSpendablePoints,
            (long)StarProgression.Points(Hero(heroKey).StarXp) + CodexBonusPoints + Math.Max(0, TestBonusPoints));

        /// <summary>確認用に足す星図ポイント（保存しない）。0〜100。</summary>
        public static int TestBonusPoints { get; set; }

        /// <summary>図鑑の節目（6種ごと）で得る星図ポイント。最大4。</summary>
        public int CodexBonusPoints => Math.Min(Content.MaxCodexBonus, Codex.Count / Content.CodexPerPoint);

        public Rng TakeRng() => new Rng(RngState, this);

        public void StoreRng(Rng rng) => RngState = rng.State;

        internal bool ContainsRelicUid(string uid)
        {
            foreach (var relic in Stash)
                if (relic.Uid == uid) return true;
            foreach (var relic in LostAndFound)
                if (relic.Uid == uid) return true;
            foreach (var pending in PendingSalvage)
                if (pending.Relic.Uid == uid) return true;
            if (Run != null)
            {
                foreach (var relic in Run.Satchel)
                    if (relic.Uid == uid) return true;
                foreach (var relic in Run.DeferredWaypointRelics)
                    if (relic.Uid == uid) return true;
            }
            return false;
        }

        /// <summary>Installs an owned, decoded checkpoint without replacing the slot's Profile reference.</summary>
        internal void RestoreFrom(Profile source)
        {
            LoadedVersion = source.LoadedVersion;
            Revision = source.Revision;
            RngState = source.RngState;
            DreamLevel = source.DreamLevel;
            DreamXp = source.DreamXp;
            EpicPity = source.EpicPity;
            BulkSalvageMaxRarity = source.BulkSalvageMaxRarity;
            BestItemLevel = source.BestItemLevel;
            Japanese = source.Japanese;
            StartDepth = source.StartDepth;
            LastDreamDepth = source.LastDreamDepth;
            Focus = source.Focus;
            HintsOff = source.HintsOff;
            StarterGranted = source.StarterGranted;
            StarterV119Granted = source.StarterV119Granted;
            Stats = source.Stats;
            Run = source.Run;
            CompletedRunId = source.CompletedRunId;
            LobbyReturnAuthority = source.LobbyReturnAuthority;
            RunRecovery = source.RunRecovery;
            KillClassification = source.KillClassification;
            LastReport = source.LastReport;
            RetuneOffer = source.RetuneOffer;
            ContinueLobbyBaseline = source.ContinueLobbyBaseline;
            ContinueResumeSession = source.ContinueResumeSession;
            Materials.Clear();
            foreach (var kv in source.Materials) Materials.Add(kv.Key, kv.Value);
            Stash.Clear();
            Stash.AddRange(source.Stash);
            LostAndFound.Clear();
            LostAndFound.AddRange(source.LostAndFound);
            PendingSalvage.Clear();
            PendingSalvage.AddRange(source.PendingSalvage);
            PendingTrades.Clear();
            PendingTrades.AddRange(source.PendingTrades);
            Heroes.Clear();
            foreach (var kv in source.Heroes) Heroes.Add(kv.Key, kv.Value);
            Codex.Clear();
            Codex.UnionWith(source.Codex);
            Feats.Clear();
            Feats.UnionWith(source.Feats);
            FeatsClaimed.Clear();
            FeatsClaimed.UnionWith(source.FeatsClaimed);
            LobbyReturnedRunIds.Clear();
            LobbyReturnedRunIds.UnionWith(source.LobbyReturnedRunIds);
            Upgrades.Clear();
            foreach (var kv in source.Upgrades) Upgrades.Add(kv.Key, kv.Value);
            SeenHints.Clear();
            SeenHints.UnionWith(source.SeenHints);
            StarterUids.Clear();
            StarterUids.AddRange(source.StarterUids);
            ContinueCheckpoints.Clear();
            ContinueCheckpoints.AddRange(source.ContinueCheckpoints);
        }

        public Profile Clone()
        {
            var c = new Profile
            {
                Revision = Revision,
                RngState = RngState,
                DreamLevel = DreamLevel,
                DreamXp = DreamXp,
                EpicPity = EpicPity,
                BulkSalvageMaxRarity = BulkSalvageMaxRarity,
                BestItemLevel = BestItemLevel,
                Japanese = Japanese,
                StartDepth = StartDepth,
                LastDreamDepth = LastDreamDepth,
                Stats = Stats.Clone(),
                Run = Run?.Clone(),
                CompletedRunId = CompletedRunId,
                LobbyReturnAuthority = LobbyReturnAuthority,
                RunRecovery = RunRecovery?.Clone(),
                KillClassification = KillClassification?.Clone(),
                ContinueLobbyBaseline = ContinueLobbyBaseline,
                ContinueResumeSession = ContinueResumeSession,
                Focus = Focus,
                LastReport = LastReport,
                RetuneOffer = RetuneOffer?.Clone(),
            };
            foreach (var kv in Materials) c.Materials[kv.Key] = kv.Value;
            c.ContinueCheckpoints.AddRange(ContinueCheckpoints);
            foreach (var r in Stash) c.Stash.Add(r.Clone());
            foreach (var r in LostAndFound) c.LostAndFound.Add(r.Clone());
            foreach (var pending in PendingSalvage) c.PendingSalvage.Add(pending.Clone());
            foreach (var trade in PendingTrades) c.PendingTrades.Add(trade.Clone());
            foreach (var kv in Heroes) c.Heroes[kv.Key] = kv.Value.Clone();
            foreach (var s in Codex) c.Codex.Add(s);
            foreach (var f in Feats) c.Feats.Add(f);
            foreach (var f in FeatsClaimed) c.FeatsClaimed.Add(f);
            c.LobbyReturnedRunIds.UnionWith(LobbyReturnedRunIds);
            foreach (var kv in Upgrades) c.Upgrades[kv.Key] = kv.Value;
            foreach (var h in SeenHints) c.SeenHints.Add(h);
            c.StarterUids.AddRange(StarterUids);
            c.HintsOff = HintsOff;
            c.StarterGranted = StarterGranted;
            c.StarterV119Granted = StarterV119Granted;
            return c;
        }
    }
}
