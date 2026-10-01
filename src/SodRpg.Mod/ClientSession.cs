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
    internal sealed class ClientSession
    {
        private readonly ProfileStore _store;
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
        private readonly Action<DreamforgeNightmareMsg> _onNightmare;
        private readonly Action<DewPlayer> _onChaos;
        private readonly Action<Hero, Mirror.NetworkBehaviour> _onBought, _onUpgraded, _onDismantled;
        private readonly Action<Hero, Gem> _onMerged;
        private readonly Action _onHuntChanged;
        private int _lastHuntLevel = -1;

        /// <summary>悪夢化した敵（netId → 接頭効果）。名札の表示と撃破時の報酬に使う。</summary>
        public Dictionary<uint, NightmareAffix> Nightmare { get; } = new Dictionary<uint, NightmareAffix>();

        /// <summary>悪夢化の通知を受けた時刻（まだスポーンしていない敵を早まって消さないため）。</summary>
        public Dictionary<uint, float> NightmareSeenAt { get; } = new Dictionary<uint, float>();

        private readonly RoomCounter _rooms = new RoomCounter();
        private bool _dirty;
        private float _nextSave;
        private bool _buildDirty = true;
        private float _nextBuildSend;
        private Hero _lastHero;
        private string _lastSentBuild;

        public Profile Profile { get; private set; }
        public string ActiveRunId { get; private set; }
        public bool HostConfirmed { get; private set; }
        public string HostSummary { get; private set; }
        public string LoadNotes { get; private set; }
        public string SaveError { get; private set; }

        public ClientSession(string saveDir, Action<GameEvent> notify)
        {
            _notify = notify;
            string path = Path.Combine(saveDir, "profile.json");
            ulong seed = Rng.SeedFrom(SystemInfo.deviceUniqueIdentifier + "|" + DateTime.UtcNow.Ticks);
            _store = new ProfileStore(new RealFileSystem(), path, seed);
            try
            {
                Profile = _store.Load();
            }
            catch (LedgerVersionException ex)
            {
                // 新しい版で保存されたデータ。上書きして壊さないよう、読み取り専用の空プロフィールで動く。
                Profile = Profile.CreateNew(seed);
                SaveError = "新しい版のMODで保存されたデータです。上書きを防ぐため保存を止めています: " + ex.Message;
                _store = null;
            }
            LoadNotes = _store != null && _store.Notes.Count > 0 ? string.Join("\n", _store.Notes) : null;
            if (LoadNotes != null) Log.Warn("Profile load notes:\n" + LoadNotes);
            _onDeath = OnDeath;
            _onZoneLoaded = OnZoneLoaded;
            _onClearedRoomsChanged = OnClearedRoomsChanged;
            _onConcluded = OnConcluded;
            _onApplied = OnApplied;
            _onNightmare = OnNightmare;
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

        public bool InGame => NetworkedManagerBase<GameManager>.instance != null;

        public bool CanEditLoadout => Profile.Run == null || Profile.Run.AwaitingChoice || !InGame;

        public bool CanEditTalents => Profile.Run == null || !InGame;

        public void MarkDirty(bool buildChanged)
        {
            _dirty = true;
            if (buildChanged) _buildDirty = true;
        }

        public void Tick()
        {
            try
            {
                Wire();
                TrackRun();
                SendBuildIfNeeded();
                if (_dirty && Time.unscaledTime >= _nextSave) SaveNow();
            }
            catch (Exception ex)
            {
                Log.Error("Client tick: " + ex);
            }
        }

        private void Wire()
        {
            var zone = NetworkedManagerBase<ZoneManager>.instance;
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
            var cem = NetworkedManagerBase<ClientEventManager>.instance;
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
            var results = NetworkedManagerBase<GameResultManager>.instance;
            if (results != _results)
            {
                if (_results != null)
                {
                    try { _results.ClientEvent_OnGameConcluded -= _onConcluded; } catch (Exception) { }
                }
                _results = results;
                if (results != null) results.ClientEvent_OnGameConcluded += _onConcluded;
            }
            var am = NetworkedManagerBase<ActorManager>.instance;
            var actor = am != null ? am.serverActor : null;
            if (actor != _clientRpcOn)
            {
                if (_clientRpcOn != null)
                {
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeAppliedMsg>(_onApplied); } catch (Exception) { }
                    try { _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeNightmareMsg>(_onNightmare); } catch (Exception) { }
                }
                _clientRpcOn = actor;
                Nightmare.Clear();
                HostConfirmed = false;
                HostSummary = null;
                _buildDirty = true;
                if (actor != null)
                {
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeAppliedMsg>(_onApplied);
                    actor.CustomRpc_RegisterClientMessageHandler<DreamforgeNightmareMsg>(_onNightmare);
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
                    _clientRpcOn.CustomRpc_UnregisterClientMessageHandler<DreamforgeNightmareMsg>(_onNightmare);
                }
            }
            catch (Exception) { }
            _zone = null;
            _cem = null;
            _results = null;
            _clientRpcOn = null;
        }

        private void TrackRun()
        {
            var gm = NetworkedManagerBase<GameManager>.instance;
            if (gm == null)
            {
                ActiveRunId = null;
                return;
            }
            string runId = gm.runId;
            if (string.IsNullOrEmpty(runId) || runId == ActiveRunId) return;
            if (LocalHero == null) return; // 観戦・ロード中は開始しない
            ActiveRunId = runId;
            Emit(Rules.BeginRun(Profile, runId, DailyDream.Today, ReadLimboDepth()));
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
            foreach (var e in events)
            {
                _dirty = true;
                _notify?.Invoke(e);
            }
        }

        public void Emit(GameEvent e)
        {
            _dirty = true;
            _notify?.Invoke(e);
        }

        private bool RunActive => Profile.Run != null && ActiveRunId != null && Profile.Run.RunId == ActiveRunId;

        private void OnDeath(EventInfoKill info)
        {
            try
            {
                if (!RunActive) return;
                if (!(info.victim is Monster m)) return;
                // ゲーム本体が報酬を出さない敵（演出・召喚・ハンターの追加敵など）は対象外（PickupManager と同じ判定）。
                if (m.disableLoot) return;
                if (m.Status != null && m.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter) && !hunter.enableGoldAndExpDrops) return;
                var hero = LocalHero;
                if (hero == null) return;
                if (m.GetRelation(hero) != EntityRelation.Enemy) return;
                int level = m.Status != null ? m.Status.level : 1;
                var gm = NetworkedManagerBase<GameManager>.instance;
                if (gm != null) level = Math.Max(level, gm.ambientLevel);
                var tier = (MonsterTier)Math.Min((int)MonsterTier.Boss, (int)m.type);
                Nightmare.TryGetValue(m.netId, out var nightmare);
                Nightmare.Remove(m.netId);
                NightmareSeenAt.Remove(m.netId);
                string heroKey = HeroKeyOf(hero);
                int masteryBefore = Mastery.Level(Profile.Hero(heroKey).Kills);
                Emit(Rules.OnKill(Profile, tier, level, nightmare, heroKey));
                if (Mastery.Level(Profile.Hero(heroKey).Kills) > masteryBefore) _buildDirty = true;
                if (tier >= MonsterTier.MiniBoss) _nextSave = 0;
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
                if (!RunActive || info.isLoadingFromSave) return;
                if (!info.isTraveling) return;
                if (!Rules.ShouldOfferSecurePoint(Profile)) return;
                Rules.ReachSecurePoint(Profile);
                _notify?.Invoke(new GameEvent(EventKind.Info, Loc.T(
                    "確保地点に到着。未確保の戦利品を「確保」するか、「深く潜る」かを選んでください。",
                    "Secure point reached. Choose to Secure your loot or Delve deeper.")));
                SaveNow();
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
                Emit(Rules.OnRoomsCleared(Profile, Profile.Run.RoomsCleared + added));
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
                if (!RunActive || result == null) return;
                bool victory = IsVictory(result.result);
                Emit(Rules.EndRun(Profile, victory));
                ActiveRunId = null;
                SaveNow();
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

        private void OnNightmare(DreamforgeNightmareMsg msg)
        {
            if (msg == null) return;
            var a = Nightmares.Sanitize(msg.affixes);
            if (a == NightmareAffix.None) return;
            Nightmare[msg.netId] = a;
            NightmareSeenAt[msg.netId] = Time.unscaledTime;
            if (Nightmare.Count > 300)
            {
                Nightmare.Clear(); // 取りこぼしで溜まり続けないように
                NightmareSeenAt.Clear();
            }
        }

        private void OnApplied(DreamforgeAppliedMsg msg)
        {
            HostConfirmed = true;
            HostSummary = msg?.summary;
        }

        public void Secure()
        {
            if (Profile.Run == null) return;
            Emit(Rules.Secure(Profile));
            _buildDirty = true;
            SaveNow();
        }

        public void Delve(Pact pact = Pact.None)
        {
            if (Profile.Run == null) return;
            Emit(Rules.Delve(Profile, pact));
            var def = Pacts.Get(pact);
            if (def != null && _clientRpcOn != null && NetworkClient.active)
                _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeCurseMsg { strength = def.CurseStrength, protocol = Protocol.Version });
            _buildDirty = true;
            SaveNow();
        }

        public Build CurrentBuild(string heroKey) => Build.Compute(Profile, heroKey, Profile.Run?.Heat ?? 0, Profile.Run?.Pacts, Profile.Run?.DailyId ?? 0);

        private void SendBuildIfNeeded()
        {
            var hero = LocalHero;
            if (hero == null || _clientRpcOn == null || !NetworkClient.active) return;
            if (hero != _lastHero)
            {
                _lastHero = hero;
                _buildDirty = true;
            }
            float now = Time.unscaledTime;
            if (!_buildDirty && now < _nextBuildSend) return;
            string encoded = CurrentBuild(HeroKeyOf(hero)).Encode();
            if (!_buildDirty && encoded == _lastSentBuild && HostConfirmed)
            {
                _nextBuildSend = now + 20f;
                return;
            }
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeBuildMsg { build = encoded, protocol = Protocol.Version });
            _lastSentBuild = encoded;
            _buildDirty = false;
            _nextBuildSend = now + (HostConfirmed ? 20f : 5f);
        }

        public void SaveNow()
        {
            _dirty = false;
            _nextSave = Time.unscaledTime + 10f;
            if (_store == null) return;
            try
            {
                _store.Save(Profile);
                SaveError = null;
            }
            catch (IOException ex)
            {
                SaveError = ex.Message;
                _dirty = true;
                Log.Error("Save failed: " + ex.Message);
            }
        }
    }
}
