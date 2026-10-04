using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using SodRpg.Core;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Line = SodRpg.Core.Game.Line;
    using Power = SodRpg.Core.Game.Power;
    using Rarity = SodRpg.Core.Game.Rarity;
    using Slot = SodRpg.Core.Game.Slot;
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>
    /// 各PCで動く処理。自分のプロフィールを読み書きし、撃破・ゾーン移動・勝敗をゲームから受け取って
    /// ルール（SodRpg.Core.Game.Rules）へ流す。協力時も報酬は各自が自分の分だけ抽選して自分の保存へ書く。
    /// </summary>
    internal sealed partial class ClientSession
    {
        private ProfileStore _store;
        private readonly Action<GameEvent> _notify;

        private ZoneManager _zone;
        private ClientEventManager _cem;
        private GameResultManager _results;
        private Actor _clientRpcOn;

        private readonly Action<EventInfoKill> _onDeath;
        private readonly Action<EventInfoLoadZone> _onZoneLoaded;
        private readonly Action _onClearedRoomsChanged;
        private readonly Action<DewGameResult> _onConcluded;
        private readonly Action<DreamforgeAppliedMsg> _onApplied;
        private readonly Action<DreamforgePressureMsg> _onPressure;
        private readonly Action<DreamforgeNightmareMsg> _onNightmare;
        private readonly Action<DreamforgeVariantMsg> _onVariant;
        private readonly Action<DreamforgeMonsterCueMsg> _onMonsterCue;
        private readonly Action<DreamforgeTradeResultMsg> _onTradeResult;
        private readonly Action<DreamforgeBountyReportMsg> _onBountyReport;
        private readonly List<PendingTrade> _dueTradeQueries = new List<PendingTrade>();
        private readonly TradeLedger _trades = new TradeLedger();
        public TradeLedger Trades => _trades;
        public bool HasPendingTrades => _trades.PendingCount > 0;
        /// <summary>応答待ちに加えて、期限切れで結果不明のまま残っている取引もあるか。プロフィールの切り替えなど、対価の行き先が変わる操作を止めるのに使う。</summary>
        public bool HasHeldTrades => _trades.HeldCount > 0;
        /// <summary>ホストが記録の有無を確かめられないと答えた取引の数（遅れて届く応答か、利用者の明示的な放棄でしか片付かない）。</summary>
        public int LostTradeCount => _trades.LostCount;
        /// <summary>
        /// いま接続しているホストの取引台帳の識別子（0 は未確認）。接続のたびに照会して知り、取引を送るときに取引へ結び付ける。
        /// ホストが再読み込みされた・接続の netId が変わったなどで台帳が替わると、結び付けた識別子と合わなくなり、記録がないことを未実行と見なさない（#36）。
        /// </summary>
        private long _hostLedgerId;
        private double _nextLedgerProbeAt;
        private readonly Action<DewPlayer> _onChaos;
        private readonly Action<Hero, Mirror.NetworkBehaviour> _onBought, _onUpgraded, _onDismantled;
        private readonly Action<Hero, Gem> _onMerged;
        private readonly Action _onHuntChanged;
        private int _lastHuntLevel = -1;

        /// <summary>悪夢化した敵（netId → 接頭効果）。名札の表示と撃破時の報酬に使う。</summary>
        public Dictionary<uint, NightmareAffix> Nightmare { get; } = new Dictionary<uint, NightmareAffix>();

        /// <summary>悪夢化の通知を受けた時刻（まだスポーンしていない敵を早まって消さないため）。</summary>
        public Dictionary<uint, float> NightmareSeenAt { get; } = new Dictionary<uint, float>();

        /// <summary>夢の変種（netId → Core の変種ID）。名札と撃破時の報酬に使う。</summary>
        public Dictionary<uint, string> Variant { get; } = new Dictionary<uint, string>();
        public Dictionary<uint, float> VariantSeenAt { get; } = new Dictionary<uint, float>();

        private sealed class VariantVisual
        {
            public Monster Monster;
            public EntityVisual Visual;
            public EntityColorModifier Color;
            public EntityTransformModifier Transform;
        }

        private readonly Dictionary<uint, VariantVisual> _variantVisuals = new Dictionary<uint, VariantVisual>();
        private readonly List<uint> _variantScratch = new List<uint>();
        private bool _loggedVariantVisualFailure;

        private readonly RoomCounter _rooms = new RoomCounter();
        private bool _dirty;
        private float _nextSave;
        private bool _buildDirty = true;
        private float _nextBuildSend;
        private int _sentDreamLevel = -1;
        private Hero _lastHero;

        public Profile Profile { get; private set; }
        public string ActiveRunId { get; private set; }
        public bool HostConfirmed { get; private set; }
        public string HostSummary { get; private set; }
        private readonly BuildTransferReceiver _appliedTransfer = new BuildTransferReceiver();
        public float PressureHealthMultiplier { get; private set; } = 1f;
        public float PressureDamageMultiplier { get; private set; } = 1f;
        public string LoadNotes { get; private set; }
        public string SaveError { get; private set; }

        public ClientSession(string saveDir, Action<GameEvent> notify)
        {
            _hostSession = this;
            _notify = notify;
            InitializeProfiles(saveDir);
            RestoreRunDurability();
            _onDeath = OnDeath;
            _onZoneLoaded = OnZoneLoaded;
            _onClearedRoomsChanged = OnClearedRoomsChanged;
            _onConcluded = OnConcluded;
            _onApplied = OnApplied;
            _onPressure = OnPressure;
            _onRunChoices = OnRunChoices;
            _grantPendingKill = GrantPendingKill;
            _onNightmare = OnNightmare;
            _onVariant = OnVariant;
            _onMonsterCue = OnMonsterCue;
            _onTradeResult = OnTradeResult;
            _onBountyReport = OnBountyReport;
            _onChaos = pl => { if (pl != null && pl == DewPlayer.local) GameAction(BountyKind.ChaosSeeker); };
            _onBought = (h, _) => { if (IsLocal(h)) GameAction(BountyKind.Patron); };
            _onUpgraded = (h, _) => { if (IsLocal(h)) GameAction(BountyKind.Refiner); };
            _onDismantled = (h, _) => { if (IsLocal(h)) GameAction(BountyKind.Recycler); };
            _onMerged = (h, _) => { if (IsLocal(h)) GameAction(BountyKind.Alchemist); };
            _onHuntChanged = OnHuntChanged;
        }

        private bool IsLocal(Hero h) => h != null && h == LocalHero;

        private void GameAction(BountyKind kind)
        {
            try
            {
                if (RunActive) Emit(Rules.OnGameAction(Profile, kind));
            }
            catch (Exception ex)
            {
                Log.Error("Client GameAction: " + ex.Message);
            }
        }

        private void OnHuntChanged()
        {
            if (_zone == null) return;
            int now = _zone.currentHuntLevel;
            if (_lastHuntLevel >= 0 && now > _lastHuntLevel) GameAction(BountyKind.HunterBait);
            _lastHuntLevel = now;
        }

        public string SavePath => _store?.Path;

        public static string HeroKeyOf(Hero h) => h != null ? h.GetType().Name : null;

        public Hero LocalHero
        {
            get
            {
                var lp = DewPlayer.local;
                return lp != null ? lp.hero : null;
            }
        }

        public bool InGame => NetworkedManagerBase<GameManager>.softInstance != null;

        /// <summary>ホストが返す取引の失敗理由を、遊ぶ人に分かる文にする。</summary>
        private static string TradeFailText(string reason)
        {
            switch (reason)
            {
                case "gold": return Loc.T("取引できませんでした。ゴールドが足りません。", "Trade failed: not enough gold.");
                case "dust": return Loc.T("取引できませんでした。ドリームダストが足りません。", "Trade failed: not enough Dream Dust.");
                case "protocol": return Loc.T("取引できませんでした。ホストと Dreamforge の版が違います。全員が同じ版を入れてください。", "Trade failed: the host runs a different Dreamforge version. Everyone needs the same version.");
                case "dup": return Loc.T("取引できませんでした。この遺物の分解は今回の遠征で受け付け済みです。", "Trade failed: this relic was already salvaged in this run.");
                case "unknown":
                case "cancelled": return Loc.T("取引は成立していませんでした。ゴールドとドリームダストは減っていません。", "The trade did not go through. Your gold and Dream Dust were not spent.");
                case "conflict": return Loc.T("取引できませんでした。取引の識別が重複しました。もう一度試してください。", "Trade failed: the trade id clashed. Please try again.");
                default: return Loc.T("取引できませんでした。もう一度試してください。", "Trade failed. Please try again.");
            }
        }

        public bool CanEditLoadout => Profile.Run == null || Profile.Run.AwaitingChoice || Profile.Run.GearWindow || !InGame;

        public bool CanEditTalents => Profile.Run == null || !InGame;

        public void MarkDirty(bool buildChanged)
        {
            _dirty = true;
            if (buildChanged)
            {
                _buildDirty = true;
                _buildCacheFrame = -1;
            }
            // 鍛冶・偉業の受け取り・確認用の遺物付与も、この変更通知を通る。
            Emit(Feats.Check(Profile));
        }

        // Each step runs on its own: one failing step must not stop Build sending or periodic saving for everyone (mp-ui-save #5).
        private Action[] _tickSteps;
        private string[] _tickStepNames;
        private float[] _tickStepNextLog;

        public void Tick()
        {
            if (_tickSteps == null)
            {
                _tickSteps = new Action[] { TickProfileSlots, Wire, TickGemSlotConflict, UpdateVariantVisuals, UpdateMonsterCues, TrackRun, TickRunChoices, TickCurseResync, TickSalvageExpiry, SendBuildIfNeeded, TickHello, TickPeriodicSave };
                _tickStepNames = new[] { "profile slots", "wire", "gem slot conflict", "variant visuals", "monster cues", "track run", "run choices", "curse resync", "salvage expiry", "send build", "hello", "periodic save" };
                _tickStepNextLog = new float[_tickSteps.Length];
            }
            for (int i = 0; i < _tickSteps.Length; i++)
            {
                try
                {
                    _tickSteps[i]();
                }
                catch (Exception ex)
                {
                    // Log at most every 10 s per step so a persistent fault does not flood the log every frame.
                    if (Time.unscaledTime >= _tickStepNextLog[i])
                    {
                        _tickStepNextLog[i] = Time.unscaledTime + 10f;
                        Log.Error("Client tick (" + _tickStepNames[i] + "): " + ex);
                    }
                }
            }
        }

        private void TickSalvageExpiry()
        {
            // GLM (mp-ui-save #7): the wait on screen is released for every trade kind after the timeout, but the trade
            // itself is kept as unresolved and queried against the host (#26): a payment the host already made still
            // grants its reward exactly once, and a trade the host never ran is returned.
            if (_trades.Expire(Time.unscaledTime) > 0)
            {
                Emit(new GameEvent(EventKind.Warning, Loc.T(
                    "取引の応答が遅れています。待ちは解除しました。ホストに結果を確認しています。",
                    "The trade response is late. The wait was released; checking the result with the host.")));
                SaveNow();
            }
            if (_hostLedgerId == 0) SendLedgerProbe();
            SendDueTradeQueries();
        }

        /// <summary>ホストの取引台帳の識別子を尋ねる（台帳には何も書かれない）。答えが届くまで、数秒おきに繰り返す。</summary>
        private void SendLedgerProbe()
        {
            if (_clientRpcOn == null || !NetworkClient.active || Time.unscaledTime < _nextLedgerProbeAt) return;
            _nextLedgerProbeAt = Time.unscaledTime + 2.0;
            try
            {
                TradeWire.EncodeQuery(0, out int spendGold, out int spendDust, out int earnDust);
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeTradeMsg
                {
                    token = TradeWire.ProbeToken, spendGold = spendGold, spendDust = spendDust, earnDust = earnDust, protocol = Protocol.Version,
                });
            }
            catch (Exception ex)
            {
                Log.Error("Client TradeLedgerProbe: " + ex.Message);
            }
        }

        /// <summary>結果不明の取引を、同じ取引idでホストへ照会する。接続していないときは次の機会まで待つ。</summary>
        private void SendDueTradeQueries()
        {
            if (_trades.UnresolvedCount == 0 || _clientRpcOn == null || !NetworkClient.active) return;
            _dueTradeQueries.Clear();
            if (_trades.CollectDueQueries(Time.unscaledTime, _dueTradeQueries) == 0) return;
            foreach (var t in _dueTradeQueries)
            {
                try
                {
                    // 送ったときに知っていた台帳の識別子を添える。ホストは、その台帳に記録がないときだけ「未実行」と答える。
                    TradeWire.EncodeQuery(t.LedgerId, out int spendGold, out int spendDust, out int earnDust);
                    _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeTradeMsg
                    {
                        token = t.Token, spendGold = spendGold, spendDust = spendDust, earnDust = earnDust, protocol = Protocol.Version,
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("Client TradeQuery: " + ex.Message);
                }
            }
        }

        private void TickPeriodicSave()
        {
            if (_dirty && Time.unscaledTime >= _nextSave) SaveNow();
        }

        private void Wire()
        {
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zone != _zone)
            {
                if (_zone != null)
                {
                    try
                    {
                        _zone.ClientEvent_OnZoneLoaded -= _onZoneLoaded;
                        _zone.ClientEvent_OnClearedCombatRoomsChanged -= _onClearedRoomsChanged;
                        _zone.ClientEvent_OnCurrentHuntLevelChanged -= _onHuntChanged;
                    }
                    catch (Exception) { }
                }
                _zone = zone;
                _lastHuntLevel = zone != null ? zone.currentHuntLevel : -1;
                _rooms.Reset(zone != null ? zone.clearedCombatRooms : -1);
                if (zone != null)
                {
                    zone.ClientEvent_OnZoneLoaded += _onZoneLoaded;
                    zone.ClientEvent_OnClearedCombatRoomsChanged += _onClearedRoomsChanged;
                    zone.ClientEvent_OnCurrentHuntLevelChanged += _onHuntChanged;
                }
            }
            var cem = NetworkedManagerBase<ClientEventManager>.softInstance;
            if (cem != _cem)
            {
                if (_cem != null)
                {
                    try { UnhookCem(_cem); } catch (Exception) { }
                }
                _cem = cem;
                if (cem != null)
                {
                    cem.OnDeath += _onDeath;
                    cem.OnChaosUsed += _onChaos;
                    cem.OnItemBought += _onBought;
                    cem.OnItemUpgraded += _onUpgraded;
                    cem.OnDismantled += _onDismantled;
                    cem.OnGemMergeUpgraded += _onMerged;
                }
            }
            var results = NetworkedManagerBase<GameResultManager>.softInstance;
            if (results != _results)
            {
                if (_results != null)
                {
                    try { _results.ClientEvent_OnGameConcluded -= _onConcluded; } catch (Exception) { }
                }
                _results = results;
                if (results != null) results.ClientEvent_OnGameConcluded += _onConcluded;
            }
            var am = NetworkedManagerBase<ActorManager>.softInstance;
            var actor = NetworkClient.active && am != null ? am.serverActor : null;
            if (!ReferenceEquals(actor, _clientRpcOn))
            {
                if (_clientRpcOn != null)
                {
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeAppliedMsg>(_onApplied); } catch (Exception) { }
                    UnregisterHello(_clientRpcOn);
                    UnregisterGemSlotConflict(_clientRpcOn);
                    UnregisterRunGrowth(_clientRpcOn);
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgePressureMsg>(_onPressure); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeRunChoicesMsg>(_onRunChoices); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeNightmareMsg>(_onNightmare); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeVariantMsg>(_onVariant); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeMonsterCueMsg>(_onMonsterCue); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeMonsterKillMsg>(OnMonsterKill); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeTradeResultMsg>(_onTradeResult); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeBountyReportMsg>(_onBountyReport); } catch (Exception) { }
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgePressureDividendMsg>(OnPressureDividend);
                }
                _clientRpcOn = actor;
                // 接続が替わったら、応答待ちの取引は結果不明にして新しい接続から照会する（捨てると、支払い済みの対価や預かった遺物を失う）。
                _trades.MarkAllUnresolved(Time.unscaledTime);
                _hostLedgerId = 0; // 接続先が替わったので、新しい接続の台帳の識別子を尋ね直す（それまで新しい取引は始めない）
                _nextLedgerProbeAt = 0;
                if (Profile.PendingSalvage.Count > 0)
                {
                    Emit(Rules.RestorePendingSalvage(Profile, keepUids: _trades.ReservedSalvageUids()));
                    SaveNow();
                }
                Nightmare.Clear();
                NightmareSeenAt.Clear();
                ClearVariants();
                ClearMonsterCues();
                _loggedVariantVisualFailure = false;
                HostConfirmed = false;
                HostSummary = null;
                ResetHello();
                ResetGemSlotConflict();
                ResetRunGrowthDisplay();
                _appliedTransfer.Reset();
                PressureHealthMultiplier = PressureDamageMultiplier = 1f;
                ResetRunChoiceConnection();
                ResetMonsterAuthorityConnection();
                _sentDreamLevel = -1;
                _buildDirty = true;
                if (actor != null)
                {
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeAppliedMsg>(_onApplied);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgePressureMsg>(_onPressure);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeRunChoicesMsg>(_onRunChoices);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeNightmareMsg>(_onNightmare);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeVariantMsg>(_onVariant);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeMonsterCueMsg>(_onMonsterCue);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeMonsterKillMsg>(OnMonsterKill);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeTradeResultMsg>(_onTradeResult);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBountyReportMsg>(_onBountyReport);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgePressureDividendMsg>(OnPressureDividend);
                    RegisterHello(actor);
                    RegisterGemSlotConflict(actor);
                    RegisterRunGrowth(actor);
                }
            }
        }

        private void UnhookCem(ClientEventManager cem)
        {
            cem.OnDeath -= _onDeath;
            cem.OnChaosUsed -= _onChaos;
            cem.OnItemBought -= _onBought;
            cem.OnItemUpgraded -= _onUpgraded;
            cem.OnDismantled -= _onDismantled;
            cem.OnGemMergeUpgraded -= _onMerged;
        }

        public void Unwire()
        {
            SaveNow();
            FlushSaves();
            _runDurabilityDetached = true;
            try
            {
                if (_zone != null)
                {
                    _zone.ClientEvent_OnZoneLoaded -= _onZoneLoaded;
                    _zone.ClientEvent_OnClearedCombatRoomsChanged -= _onClearedRoomsChanged;
                    _zone.ClientEvent_OnCurrentHuntLevelChanged -= _onHuntChanged;
                }
                if (_cem != null) UnhookCem(_cem);
                if (_results != null) _results.ClientEvent_OnGameConcluded -= _onConcluded;
                if (_clientRpcOn != null)
                {
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeAppliedMsg>(_onApplied);
                    UnregisterHello(_clientRpcOn);
                    UnregisterGemSlotConflict(_clientRpcOn);
                    UnregisterRunGrowth(_clientRpcOn);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgePressureMsg>(_onPressure);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeRunChoicesMsg>(_onRunChoices);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeNightmareMsg>(_onNightmare);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeVariantMsg>(_onVariant);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeMonsterCueMsg>(_onMonsterCue);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeMonsterKillMsg>(OnMonsterKill);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeTradeResultMsg>(_onTradeResult);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeBountyReportMsg>(_onBountyReport);
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgePressureDividendMsg>(OnPressureDividend);
                }
            }
            catch (Exception) { }
            _zone = null;
            _cem = null;
            _results = null;
            _clientRpcOn = null;
            HostConfirmed = false;
            HostSummary = null;
            ResetGemSlotConflict();
            _appliedTransfer.Reset();
            PressureHealthMultiplier = PressureDamageMultiplier = 1f;
            ResetRunChoiceConnection(resetHistory: true);
            ResetMonsterAuthorityConnection();
            if (ReferenceEquals(_hostSession, this)) _hostSession = null;
            _pendingRunRewards.Clear();
            _pendingPressureDividends.Clear();
            _sentDreamLevel = -1;
            _buildDirty = true;
            Nightmare.Clear();
            NightmareSeenAt.Clear();
            ClearVariants();
            ClearMonsterCues();
            _loggedVariantVisualFailure = false;
        }

        private void TrackRun()
        {
            var gm = NetworkedManagerBase<GameManager>.softInstance;
            if (gm == null)
            {
                ActiveRunId = null;
                return;
            }
            string runId = gm.runId;
            if (string.IsNullOrEmpty(runId) || runId == _completedRunId) return;
            if (LocalHero == null) return; // 観戦・ロード中は開始しない
            if (_zone != null && _zone.isInAnyTransition) return;
            // 参加者の PC では ZoneManager が遅れて届くことがある。ゾーン番号が分かるまで遠征を始めない（v1.30.3）。
            if (ChoiceZoneIndex < 0) return;
            // Remember zone history even while the initial host rules are still in transit.
            _runChoiceProgress.BeginRun(runId, ChoiceZoneIndex);
            if (runId == ActiveRunId) return;
            // The first reward must use the host's depth, including clients who join during an expedition.
            if (!CanChooseRunRules && (_receivedRunChoices == null || _receivedRunChoices.RunId != runId)) return;
            // 別のIDの未解決ランが残っていれば BeginRun の中で終わる。その契約の呪いを消す。
            int pacts = Profile.Run != null && Profile.Run.RunId != runId ? Profile.Run.Pacts.Count : 0;
            ActiveRunId = runId;
            PressureHealthMultiplier = PressureDamageMultiplier = 1f;
            Emit(Rules.BeginRun(Profile, runId, DailyDream.Today, ReadLimboDepth(), _trades.ReservedSalvageUids(), heroKey: HeroKeyOf(LocalHero),
                dreamDepth: CanChooseRunRules ? Profile.LastDreamDepth : _receivedRunChoices.Depth));
            ApplyHostRunChoices();
            if (pacts > 0) SendCurseClear();
            if (Onboarding.AutoEquipStarter(Profile, HeroKeyOf(LocalHero))) Emit(Rules.HintOnce(Profile, Hint.StarterGear));
            _buildDirty = true;
            SaveNow();
        }

        /// <summary>本体の Limbo 深度（Limbo でなければ0）。</summary>
        private static int ReadLimboDepth()
        {
            try
            {
                var limbo = UnityEngine.Object.FindObjectOfType<GameMod_Limbo>();
                return limbo != null ? Math.Max(0, limbo.depth) : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private void Emit(IEnumerable<GameEvent> events)
        {
            foreach (var e in events) Emit(e);
        }

        /// <summary>初めての起動：初期装備を配り、ようこその案内を出す。</summary>
        public void FirstLaunch()
        {
            // 前回の終了時に結果が出ていなかった取引は、起動後すぐにホストへ照会する（件数が MaxHeld を超えていても全件戻す）。
            int notRestored = _trades.Restore(Profile.PendingTrades, Time.unscaledTime);
            if (notRestored > 0) Log.Warn("Trade restore: " + notRestored + " saved trade(s) exceeded the restore limit and were not restored.");
            Emit(Rules.RestorePendingSalvage(Profile, keepUids: _trades.ReservedSalvageUids()));
            if (!Profile.StarterGranted)
            {
                Onboarding.GrantStarterKit(Profile);
                _dirty = true;
            }
            var newSlotStarters = Onboarding.GrantNewSlotStarters(Profile);
            if (Onboarding.BackfillStarterCodex(Profile) > 0) _dirty = true; // 以前の配布で銘品が図鑑に載らなかった分を補う
            // v1.20：以前に+3・+5にした遺物へ、強化の節目を一度だけ付ける。
            int milestones = Rules.ApplyEnhanceMilestones(Profile);
            if (milestones > 0) Emit(new GameEvent(EventKind.Info, Loc.T($"強化の節目を、これまでに+3・+5にした遺物{milestones}個に付けました（特性や固有効果が増えています）。", $"Enhancement milestones were applied to {milestones} relic(s) you had already enhanced.")));
            if (newSlotStarters.Count > 0)
            {
                Emit(newSlotStarters);
                _dirty = true;
            }
            Emit(Rules.HintOnce(Profile, Hint.Welcome));
            if (_dirty) SaveNow();
        }

        public void Emit(GameEvent e)
        {
            _dirty = true;
            // 潜行・覚醒・装備への出来事で変わった能力を次の送信へ反映する。
            if (e.Kind == EventKind.Delved || e.Kind == EventKind.LevelUp) MarkDirty(true);
            _notify?.Invoke(e);
            if (e.AdditionalEvents != null) Emit(e.AdditionalEvents);
        }

        private bool RunActive => Profile.Run != null && ActiveRunId != null && Profile.Run.RunId == ActiveRunId;

        private void OnDeath(EventInfoKill info)
        {
            try
            {
                if (!(info.victim is Monster m)) return;
                Nightmare.Remove(m.netId);
                NightmareSeenAt.Remove(m.netId);
                RemoveVariant(m.netId);
                RemoveMonsterCue(m.netId);
                _monsterAuthority.Remove(m.netId);
                // ゲーム本体が報酬を出さない敵（演出・召喚・ハンターの追加敵など）は対象外（PickupManager と同じ判定）。
                if (m.disableLoot) return;
                if (m.Status != null && m.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter) && !hunter.enableGoldAndExpDrops) return;
                var hero = LocalHero;
                if (hero == null) return;
                if (m.GetRelation(hero) != EntityRelation.Enemy) return;
                int level = m.Status != null ? m.Status.level : 1;
                var gm = NetworkedManagerBase<GameManager>.softInstance;
                if (gm == null || string.IsNullOrEmpty(gm.runId)) return;
                if (gm != null) level = Math.Max(level, gm.ambientLevel);
                var tier = (MonsterTier)Math.Min((int)MonsterTier.Boss, (int)m.type);
                string heroKey = HeroKeyOf(hero);
                if (gm.runId == _completedRunId) return;
                _runChoiceProgress.BeginRun(gm.runId, ChoiceZoneIndex);
                if (CanChooseRunRules) CommitCombatChoice();
                CaptureNativeKill(m.netId, new PendingRunKill(gm.runId, ChoiceZoneIndex, _zone?.currentRoomIndex ?? 0,
                    tier, level, NightmareAffix.None, null, heroKey));
            }
            catch (Exception ex)
            {
                Log.Error("Client OnDeath: " + ex.Message);
            }
        }

        private void OnZoneLoaded(EventInfoLoadZone info)
        {
            try
            {
                if (info.isLoadingFromSave || !info.isTraveling) return;
                string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
                if (string.IsNullOrEmpty(runId) || runId == _completedRunId) return;
                _runChoiceProgress.BeginRun(runId, ChoiceZoneIndex);
                _runChoiceProgress.Arrive(runId, ChoiceZoneIndex);
                TryFinishSecureArrival();
            }
            catch (Exception ex)
            {
                Log.Error("Client OnZoneLoaded: " + ex.Message);
            }
        }

        private void OnClearedRoomsChanged()
        {
            try
            {
                if (_zone == null) return;
                int added = _rooms.Observe(_zone.clearedCombatRooms);
                if (!RunActive || added <= 0) return;
                Emit(Rules.OnRoomsCleared(Profile, Profile.Run.RoomsCleared + added, _trades));
            }
            catch (Exception ex)
            {
                Log.Error("Client OnClearedRooms: " + ex.Message);
            }
        }

        private void OnConcluded(DewGameResult result)
        {
            try
            {
                if (result == null) return;
                _pendingRunVictory = IsVictory(result.result);
                _pendingResultRunId = ActiveRunId ?? NetworkedManagerBase<GameManager>.softInstance?.runId;
                FlushPendingRunRewards();
                TryConcludeRun();
            }
            catch (Exception ex)
            {
                Log.Error("Client OnConcluded: " + ex.Message);
            }
        }

        /// <summary>エンディングに到達した結果（ゲーム本体の GameResultManager と同じ判定）。</summary>
        internal static bool IsVictory(DewGameResult.ResultType r)
        {
            return r == DewGameResult.ResultType.PureWhiteDream || r == DewGameResult.ResultType.StarlessPath || r == DewGameResult.ResultType.UnknownFate;
        }

        // ───────── 本体の通貨での取引（ホストが支払いを確定したら、こちらの処理を確定する）─────────

        public int LocalGold => DewPlayer.local != null ? DewPlayer.local.gold : 0;
        public int LocalDust => DewPlayer.local != null ? DewPlayer.local.dreamDust : 0;
        public bool TradePending(TradeKind kind) => _trades.HasPending(kind);

        /// <summary>夢の商人をゴールドで買う（本体の難易度補正を通した価格）。</summary>
        public int MerchantPrice()
        {
            int heat = Profile.Run?.Heat ?? 0;
            var gm = NetworkedManagerBase<GameManager>.softInstance;
            float price = Economy.MerchantGoldBase(heat);
            try { if (gm != null) price = gm.GetAdjustedGoldAmount_Cost(price); } catch (Exception) { }
            return Math.Max(1, (int)Math.Round(price));
        }

        public string BuyFromMerchant()
        {
            if (TradePending(TradeKind.MerchantGold)) return Loc.T("取引の応答を待っています。", "Waiting for the trade to complete.");
            if (!DreamEvents.CanUse(Profile, DreamEvent.Merchant, true, out string reason, _trades)) return reason;
            int price = MerchantPrice();
            if (LocalGold < price) return Loc.T($"ゴールドが足りません（{price}G）。", $"Not enough gold ({price}G).");
            string blocked = TradeUnavailable();
            if (blocked != null) return blocked;
            return SendTrade(_trades.BeginMerchant(Profile.Run?.Heat ?? 0, price, Time.unscaledTime));
        }

        public string ConvertDust()
        {
            if (Profile.Run == null || (!Profile.Run.AwaitingChoice && !Profile.Run.GearWindow)) return Loc.T("確保地点でのみ換えられます。", "Only at a secure point.");
            int dust = (LocalDust / Economy.DustPerBatch) * Economy.DustPerBatch;
            if (dust <= 0) return Loc.T($"ドリームダストが{Economy.DustPerBatch}以上必要です。", $"Need at least {Economy.DustPerBatch} Dream Dust.");
            if (TradePending(TradeKind.DustToShards)) return Loc.T("取引の応答を待っています。", "Waiting for the trade to complete.");
            string blocked = TradeUnavailable();
            if (blocked != null) return blocked;
            return SendTrade(_trades.BeginDustToShards(Math.Min(dust / Economy.DustPerBatch, Economy.MaxBatchesPerTrade), Time.unscaledTime));
        }

        /// <summary>
        /// 新しい取引を始められない理由（なければ null）。上限・接続・台帳の識別子は、取引を台帳へ登録する前（通信・決済より前）に確かめる。
        /// 識別子が分からない間に送ると、あとで「記録がない」ことの意味を確かめられないので、分かるまで待たせる。
        /// </summary>
        private string TradeUnavailable()
        {
            if (!_trades.CanBegin)
                return Loc.T($"未確定の取引が{TradeLedger.MaxHeld}件に達しています。結果が確認できるまで、新しい取引はできません。",
                    $"There are already {TradeLedger.MaxHeld} unresolved trades. New trades are paused until their results are confirmed.");
            if (_clientRpcOn == null || !NetworkClient.active) return Loc.T("ゲームに接続していません。", "Not connected to a game.");
            if (_hostLedgerId == 0)
            {
                SendLedgerProbe();
                return Loc.T("ホストとの取引を準備しています。少し待ってからもう一度お試しください。", "Preparing trades with the host. Please try again in a moment.");
            }
            return null;
        }

        private string SendTrade(PendingTrade t)
        {
            t.LedgerId = _hostLedgerId; // 送った時点の台帳。あとの照会で「記録がない＝未実行」と言えるかの根拠になる
            if (_clientRpcOn == null || !NetworkClient.active)
            {
                RestoreSalvageTrade(_trades.Complete(t.Token, false));
                return Loc.T("ゲームに接続していません。", "Not connected to a game.");
            }
            try
            {
                TradeWire.Encode(t, out int spendGold, out int spendDust, out int earnDust);
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeTradeMsg
                {
                    token = t.Token, spendGold = spendGold, spendDust = spendDust, earnDust = earnDust, protocol = Protocol.Version,
                });
            }
            catch (Exception ex)
            {
                RestoreSalvageTrade(_trades.Complete(t.Token, false));
                Log.Error("Client SendTrade: " + ex.Message);
                return Loc.T("取引を送れませんでした。", "Could not send the trade.");
            }
            SaveNow(); // 送った取引は保存する：MODの再読み込みや終了をまたいでも、結果を照会して対価を受け取れる
            return null;
        }

        /// <summary>
        /// 確認不能の取引を、利用者の明示的な操作で手放す（コンソール dreamforge_trades_giveup）。対価は付かず、預かった遺物は戻る。
        /// ホストが実際には支払い済みだった場合、その分は戻らず、分解なら遺物とダストの両方が手元に残ることがある。自動では行わない。
        /// </summary>
        public int GiveUpLostTrades()
        {
            var taken = _trades.TakeLost();
            if (taken.Count == 0) return 0;
            foreach (var t in taken) RestoreSalvageTrade(t);
            Emit(new GameEvent(EventKind.Warning, Loc.T(
                $"結果を確認できない取引{taken.Count}件を手放しました（対価は付いていません）。",
                $"Gave up {taken.Count} trade(s) whose result could not be confirmed (no reward was granted).")));
            SaveNow();
            return taken.Count;
        }

        private void RestoreSalvageTrade(PendingTrade trade)
        {
            if (trade == null || trade.Kind != TradeKind.SalvageForDust) return;
            Emit(Rules.RestorePendingSalvage(Profile, trade.Uid));
        }

        private void OnTradeResult(DreamforgeTradeResultMsg msg)
        {
            try
            {
                if (msg == null) return;
                // reason は「コード@台帳の識別子」。どの結果にも、ホストの現在の台帳の識別子が付いてくる。
                TradeWire.SplitReason(msg.reason, out string code, out long ledgerId);
                if (ledgerId != 0) _hostLedgerId = ledgerId;
                if (msg.token == TradeWire.ProbeToken) return;
                var outcome = _trades.OnResult(msg.token, msg.ok, code, out var t);
                if (outcome == TradeOutcome.NotFound || outcome == TradeOutcome.AlreadyLost) return;
                if (outcome == TradeOutcome.Lost)
                {
                    // ホストは記録の有無を確かめられない：支払い済みかもしれないので、返却も対価も確定せず保留する（遺物は預かったまま）。
                    Emit(new GameEvent(EventKind.Warning, Loc.T(
                        "取引の結果をホストが確認できません（ホストが再読み込みされたか、接続が替わった可能性があります）。対価も返却も確定せず保留しています。",
                        "The host cannot confirm the result of a trade (it may have reloaded or the connection changed). The reward and the return are both on hold.")));
                    SaveNow();
                    return;
                }
                if (outcome == TradeOutcome.Failed)
                {
                    RestoreSalvageTrade(t);
                    Emit(new GameEvent(EventKind.Warning, TradeFailText(code)));
                    SaveNow();
                    return;
                }
                switch (t.Kind)
                {
                    case TradeKind.MerchantGold:
                        Emit(Rules.GrantPaidMerchant(Profile, _trades));
                        break;
                    case TradeKind.DustToShards:
                        Emit(Rules.GrantPaidDustShards(Profile, t.SpendDust));
                        break;
                    case TradeKind.SalvageForDust:
                        if (Rules.SalvageUnsecured(Profile, t.Uid) != null)
                            Emit(new GameEvent(EventKind.Info, Loc.T($"分解してドリームダスト+{t.EarnDust}", $"Salvaged for {t.EarnDust} Dream Dust")));
                        break;
                }
                SaveNow();
            }
            catch (InvalidOperationException ex)
            {
                Emit(new GameEvent(EventKind.Warning, ex.Message));
            }
        }

        /// <summary>未確保の遺物を予約し、成功応答を受けてから鞄か遠征終了後の預かりから取り除く。</summary>
        public string SalvageUnsecured(string uid)
        {
            try
            {
                if (_trades.IsReserved(uid) || Profile.PendingSalvage.Exists(s => s.Relic.Uid == uid))
                    return Loc.T("取引の応答を待っています。", "Waiting for the trade to complete.");
                var run = Profile.Run ?? throw new InvalidOperationException(Loc.T("遠征中のみ使えます。", "Only during an expedition."));
                var r = run.Satchel.Find(x => x.Uid == uid) ?? throw new InvalidOperationException(Loc.T("未確保の遺物ではありません。", "That relic is not in your satchel."));
                string blocked = TradeUnavailable();
                if (blocked != null) return blocked;
                return SendTrade(_trades.BeginSalvage(r, Time.unscaledTime));
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }

        private void OnNightmare(DreamforgeNightmareMsg msg)
        {
            if (msg == null || !ObserveMonsterAuthority(msg.authorityGeneration)
                || !_monsterAuthority.Set(msg.authorityGeneration, msg.netId, (NightmareAffix)msg.affixes, null)) return;
            var a = Nightmares.Sanitize(msg.affixes);
            if (a == NightmareAffix.None)
            {
                Nightmare.Remove(msg.netId);
                NightmareSeenAt.Remove(msg.netId);
                RemoveVariant(msg.netId);
                return;
            }
            RemoveVariant(msg.netId);
            Nightmare[msg.netId] = a;
            NightmareSeenAt[msg.netId] = Time.unscaledTime;
            if (Nightmare.Count > 300)
            {
                Nightmare.Clear(); // 取りこぼしで溜まり続けないように
                NightmareSeenAt.Clear();
            }
        }

        private void OnVariant(DreamforgeVariantMsg msg)
        {
            if (msg == null || !ObserveMonsterAuthority(msg.authorityGeneration)
                || !_monsterAuthority.Set(msg.authorityGeneration, msg.netId, NightmareAffix.None, msg.variantId)) return;
            var def = Variants.Get(msg.variantId);
            if (def == null)
            {
                Nightmare.Remove(msg.netId);
                NightmareSeenAt.Remove(msg.netId);
                RemoveVariant(msg.netId);
                return;
            }
            if (Variant.TryGetValue(msg.netId, out var previous) && previous != def.Id)
                RemoveVariant(msg.netId);
            Nightmare.Remove(msg.netId);
            NightmareSeenAt.Remove(msg.netId);
            Variant[msg.netId] = def.Id;
            VariantSeenAt[msg.netId] = Time.unscaledTime;
            if (Variant.Count > 300)
            {
                ClearVariants();
                return;
            }
            if (NetworkClient.spawned.TryGetValue(msg.netId, out var id) && id != null)
                ApplyVariantVisual(msg.netId, id.GetComponent<Monster>(), def);
        }

        private void UpdateVariantVisuals()
        {
            if (Variant.Count == 0) return;
            _variantScratch.Clear();
            foreach (var kv in Variant)
            {
                bool pending = !_variantVisuals.ContainsKey(kv.Key)
                    && VariantSeenAt.TryGetValue(kv.Key, out float seen) && Time.unscaledTime - seen <= 10f;
                if (!NetworkClient.spawned.TryGetValue(kv.Key, out var id) || id == null)
                {
                    if (!pending) _variantScratch.Add(kv.Key);
                    continue;
                }
                var m = id.GetComponent<Monster>();
                if (m == null || !m.isActive)
                {
                    if (m == null || !pending) _variantScratch.Add(kv.Key);
                    continue;
                }
                ApplyVariantVisual(kv.Key, m, Variants.Get(kv.Value));
            }
            foreach (uint netId in _variantScratch) RemoveVariant(netId);
        }

        private void ApplyVariantVisual(uint netId, Monster m, SodRpg.Core.Game.VariantDef def)
        {
            if (def == null || m == null || !m.isActive) return;
            var visual = m.Visual;
            if (_variantVisuals.TryGetValue(netId, out var old))
            {
                if (old.Monster == m && old.Visual == visual) return;
                StopVariantVisual(old);
                _variantVisuals.Remove(netId);
            }
            // A message can arrive before the entity or its model is ready.
            if (visual == null || visual.model == null) return;
            var state = new VariantVisual { Monster = m, Visual = visual };
            _variantVisuals[netId] = state;
            try
            {
                state.Color = visual.GetNewColorModifier();
                state.Color.baseColor = new Color(def.R, def.G, def.B, 1f);
                state.Transform = visual.GetNewTransformModifier();
                state.Transform.scaleMultiplier = Vector3.one * def.Scale;
            }
            catch (Exception ex)
            {
                // Retain the state to avoid repeatedly failing or stacking modifiers on resync.
                StopVariantVisual(state);
                LogVariantVisualFailure(ex);
            }
        }

        private void LogVariantVisualFailure(Exception ex)
        {
            if (_loggedVariantVisualFailure) return;
            _loggedVariantVisualFailure = true;
            Log.Warn("Variant visuals unavailable; keeping name tags: " + ex.Message);
        }

        private void StopVariantVisual(VariantVisual state)
        {
            try { state.Color?.Stop(); }
            catch (Exception ex) { LogVariantVisualFailure(ex); }
            try { state.Transform?.Stop(); }
            catch (Exception ex) { LogVariantVisualFailure(ex); }
            state.Color = null;
            state.Transform = null;
        }

        public void RemoveVariant(uint netId)
        {
            Variant.Remove(netId);
            VariantSeenAt.Remove(netId);
            if (!_variantVisuals.TryGetValue(netId, out var state)) return;
            _variantVisuals.Remove(netId);
            StopVariantVisual(state);
        }

        private void ClearVariants()
        {
            foreach (var state in _variantVisuals.Values) StopVariantVisual(state);
            _variantVisuals.Clear();
            Variant.Clear();
            VariantSeenAt.Clear();
            _variantScratch.Clear();
        }

        private void OnApplied(DreamforgeAppliedMsg msg)
        {
            if (msg == null || msg.protocol != Protocol.Version || !MechanismHandshakeAccepted) return;
            if (msg.heroNetId != 0 && (LocalHero == null || LocalHero.netId != msg.heroNetId)) return;
            if (_appliedTransfer.TryAccept(msg.ToPart(), out string summary) && summary != null)
                HostSummary = summary;
        }

        private void OnPressure(DreamforgePressureMsg msg)
        {
            if (msg == null || msg.protocol != Protocol.Version) return;
            PressureHealthMultiplier = msg.healthMultiplier;
            PressureDamageMultiplier = msg.damageMultiplier;
            ReceiveRunChoices(msg.runChoices);
            HostConfirmed = true;
            ReportPressureProgress();
        }

        private void ReportPressureProgress()
        {
            Emit(Rules.OnPressureReported(Profile, ActiveRunId, PressureHealthMultiplier));
        }

        private void OnBountyReport(DreamforgeBountyReportMsg msg)
        {
            if (msg == null || msg.protocol != Protocol.Version || !RunActive || msg.runId != ActiveRunId) return;
            var hero = LocalHero;
            if (hero == null || msg.heroNetId != hero.netId) return;
            switch ((BountyReportKind)msg.report)
            {
                case BountyReportKind.ElementalKill:
                    if (msg.value <= 0 || msg.value > 15) return;
                    Emit(Rules.OnElementalKill(Profile, (msg.value & 1) != 0, (msg.value & 2) != 0,
                        (msg.value & 4) != 0, (msg.value & 8) != 0));
                    break;
                case BountyReportKind.ShieldGranted:
                    if (msg.value == 1) Emit(Rules.OnSupportApplied(Profile, true, false, 1f));
                    break;
                case BountyReportKind.AllyHealed:
                    if (msg.value == 1) Emit(Rules.OnSupportApplied(Profile, false, true, 1f));
                    break;
                case BountyReportKind.MemoryUsed:
                    Emit(Rules.OnMemoryUsed(Profile, msg.value));
                    break;
                case BountyReportKind.LinksSatisfied:
                    Emit(Rules.OnLinksSatisfied(Profile, msg.value));
                    break;
                case BountyReportKind.GimmicksTriggered:
                    Emit(Rules.OnGimmicksTriggered(Profile, msg.value));
                    break;
            }
        }

        public string Secure()
        {
            if (Profile.Run == null) return null;
            if (!CanResolveSecureChoice) return SecureChoiceUnavailable();
            int pacts = Profile.Run.Pacts.Count;
            Emit(Rules.Secure(Profile));
            // 契約が1つでも解けたら、潜行で付いた呪いをホストから消す。
            if (pacts > 0) SendCurseClear();
            _buildDirty = true;
            SaveNow();
            PublishRunChoices();
            return null;
        }

        public string Delve(Pact pact = Pact.None)
        {
            if (Profile.Run == null) return null;
            if (!CanResolveSecureChoice) return SecureChoiceUnavailable();
            Emit(Rules.Delve(Profile, pact));
            var def = Pacts.Get(pact);
            if (def != null && _clientRpcOn != null && NetworkClient.active)
            {
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeCurseMsg { strength = def.CurseStrength, protocol = Protocol.Version });
                _curseSyncedKey = CurseKey(); // already applied by this Delve: no resync for the same run/hero/authority
            }
            _buildDirty = true;
            SaveNow();
            PublishRunChoices();
            return null;
        }

        private string _curseSyncedKey = "";

        private string CurseKey()
        {
            var hero = LocalHero;
            if (hero == null || _clientRpcOn == null || !NetworkClient.active || !RunActive) return "";
            return PactCurseSync.Key(ActiveRunId, hero.GetInstanceID(), _clientRpcOn.GetInstanceID());
        }

        /// <summary>再起動・ホスト権限の交代・復活のあとも契約が残っていれば、呪いを1回だけ付け直してもらう。</summary>
        private void TickCurseResync()
        {
            var run = Profile.Run;
            if (run == null || run.Pacts.Count == 0 || LocalHero == null || !LocalHero.isActive) return;
            string key = CurseKey();
            if (!PactCurseSync.ShouldResend(run.Pacts.Count, _curseSyncedKey, key)) return;
            _curseSyncedKey = key;
            for (int i = 0; i < run.Pacts.Count; i++)
            {
                var def = Pacts.Get(run.Pacts[i]);
                if (def == null) continue;
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeCurseMsg
                {
                    strength = PactCurseSync.Encode(def.CurseStrength, i + 1), protocol = Protocol.Version,
                });
            }
        }

        /// <summary>契約が解けたことをホストへ伝え、潜行で付いた呪いを消してもらう。</summary>
        private void SendCurseClear()
        {
            if (_clientRpcOn == null || !NetworkClient.active) return;
            try
            {
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeCurseClearMsg { protocol = Protocol.Version });
            }
            catch (Exception ex)
            {
                Log.Error("Client SendCurseClear: " + ex.Message);
            }
        }

        private Build _buildCache;
        private string _buildCacheHero;
        private int _buildCacheFrame = -1;

        /// <summary>今回の強さ（同じフレーム・同じキャラなら使い回す。OnGUI は1フレームに何度も呼ばれるため）。</summary>
        public Build CurrentBuild(string heroKey)
        {
            int frame = Time.frameCount;
            if (_buildCache != null && _buildCacheFrame == frame && _buildCacheHero == heroKey) return _buildCache;
            _buildCache = Build.Compute(Profile, heroKey, Profile.Run?.Heat ?? 0, Profile.Run?.Pacts, Profile.Run?.DailyId ?? 0);
            _buildCacheHero = heroKey;
            _buildCacheFrame = frame;
            return _buildCache;
        }

        private void SendBuildIfNeeded()
        {
            var hero = LocalHero;
            if (hero == null || _clientRpcOn == null || !NetworkClient.active) return;
            if (!MechanismHandshakeAccepted) return;
            if (_sentDreamLevel != Profile.DreamLevel)
            {
                _buildDirty = true;
                _buildCacheFrame = -1;
            }
            if (hero != _lastHero)
            {
                _lastHero = hero;
                _buildDirty = true;
            }
            float now = Time.unscaledTime;
            if (!_buildDirty && !_monsterAuthority.BuildResendRequired && now < _nextBuildSend) return;
            string encoded = HostBuildValidation.Encode(CurrentBuild(HeroKeyOf(hero)), Profile, HeroKeyOf(hero),
                Profile.Run?.Heat ?? 0, Profile.Run?.Pacts, Profile.Run?.DailyId ?? 0);
            foreach (var part in BuildTransfer.Split(encoded))
                _clientRpcOn.CustomRpc_SendMessageToServer(DreamforgeBuildMsg.FromPart(part));
            _buildDirty = false;
            _monsterAuthority.BuildSent();
            _sentDreamLevel = Profile.DreamLevel;
            _nextBuildSend = now + (HostConfirmed ? 30f : 5f);
        }

        private AsyncProfileWriter _writer;
        private double _saveMsTotal;
        private int _saveCount;

        /// <summary>メインスレッドで保存に使った平均時間（JSON化のみ。書き込みは別スレッド）。</summary>
        public double SaveMsAverage => _saveCount > 0 ? _saveMsTotal / _saveCount : 0;

        /// <summary>保存を予約する（ディスクへの書き込みは別スレッド）。</summary>
        public void SaveNow()
        {
            PersistRunDurability();
            Profile.PendingTrades.Clear();
            Profile.PendingTrades.AddRange(_trades.Snapshot());
            _dirty = false;
            _nextSave = Time.unscaledTime + 30f;
            if (_store == null) return;
            if (_writer == null) _writer = new AsyncProfileWriter(_store);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _writer.Enqueue(Profile);
            }
            catch (Exception ex)
            {
                SaveError = ex.Message;
                _dirty = true;
                Log.Error("Save failed: " + ex.Message);
            }
            _saveMsTotal += sw.Elapsed.TotalMilliseconds;
            _saveCount++;
            if (_writer.LastError != null) SaveError = _writer.LastError;
            else if (SaveError != null && _writer.WrittenRevision > 0) SaveError = null;
        }

        /// <summary>終了時：予約済みの保存を書き終えるまで待つ。</summary>
        public void FlushSaves()
        {
            _writer?.Flush();
        }
    }
}
