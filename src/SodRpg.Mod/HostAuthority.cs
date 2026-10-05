using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Power = SodRpg.Core.Game.Power;
    using Stat = SodRpg.Core.Game.Stat;
    using VariantDef = SodRpg.Core.Game.VariantDef;

    /// <summary>
    /// ホスト側の処理。各プレイヤーから届いた Build を、そのプレイヤーのキャラへ能力補正（StatBonus）として付け、
    /// 固有効果を戦闘イベントから発動する。効果の計算は SodRpg.Core の PowerRuntime が行い、ここはゲームへの作用だけを持つ。
    /// 能力値の変更はホストだけが行う（Mirror の権限モデル）。
    /// </summary>
    internal sealed partial class HostAuthority
    {
        internal sealed class ReceivedBuild
        {
            public Build Build;
            public string Encoded;
            public string Summary;
            public string HeroKey;
            public bool ApplyFailed;
        }

        internal sealed class HeroRuntime
        {
            public Hero Hero;
            public PowerRuntime Powers;
            public readonly NewPowerHostState NewPowers = new NewPowerHostState();
            public readonly GimmickRuntime Gimmicks = new GimmickRuntime();
            public readonly ElementReactionRuntime Reactions = new ElementReactionRuntime();
            public readonly List<PendingReaction> PendingReactions = new List<PendingReaction>();
            public readonly Dictionary<int, Entity> ReactionVictims = new Dictionary<int, Entity>();
            public readonly PairComboRuntime PairCombos = new PairComboRuntime();
            public readonly HashSet<string> PairMemories = new HashSet<string>();
            public readonly HashSet<int> GeneratedKillVictims = new HashSet<int>();
            public readonly List<GimmickRequest> GimmickRequests = new List<GimmickRequest>();
            public readonly List<PendingGimmick> PendingGimmicks = new List<PendingGimmick>();
            public readonly BossCombatState Boss = new BossCombatState();
            internal readonly BossNativeOwner BossNative = new BossNativeOwner();
            public long ShieldEquipmentEpoch;
            public Action<EventInfoDamage> OnMemoryDamage;
            public Action<EventInfoKill> OnMemoryKill;
            public readonly Dictionary<int, float> MemoryHitAmounts = new Dictionary<int, float>();
            public ReceivedBuild AppliedBuild;
            public StatBonus BaseBonus;
            public StatBonus DynBonus;
            public Action<EventInfoAttackFired> OnFired;
            public Action<EventInfoAttackHit> OnHit;
            public Action<EventInfoSkillUse> OnSkill;
            public Action<Vector3, Vector3> OnTeleport;
            public Action<Displacement> OnDisplacement;
            public Action<EventInfoHeal> OnHeal;
            public Action<EventInfoHeal> OnSupportHeal;
            public Action<EventInfoShield> OnSupportShield;
            public Action<EventInfoDamageNegatedByImmunity> OnImmunity;
            public DewPlayer Player;
            public Action<int> OnSpendGold;
            public DataProcessor<DamageData, Actor, Entity> DamageDealt;
            public DataProcessor<DamageData, Actor, Entity> DamageTaken;
            public DataProcessor<HealData, Actor, Entity> HealDealt;
            public DataProcessor<HealData, Actor, Entity> ShieldDealt;
            public Action<EventInfoSummon> OnSummon;
            public readonly Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>> Summons =
                new Dictionary<Summon, DataProcessor<DamageData, Actor, Entity>>();
            // 連携（v1.26）：旅人の型名と、装着中の記憶・エッセンスの型名（0.25秒ごとに集め直す）。
            public string HeroKey;
            public readonly HashSet<string> LinkMemories = new HashSet<string>();
            public readonly HashSet<string> LinkEssences = new HashSet<string>();
            public readonly List<LinkDef> SatisfiedLinks = new List<LinkDef>();
            public string BountyRunId;
            public int ReportedLinks;
            // Runtime retains only the component reference; ownership lives in its weak-key ledger.
            public HeroSkill GemSlotOwner;
            // v1.32 B: the stacks live in HostAuthority.RunGrowthLedger (outside this runtime); only their applied effect is kept here.
            public StatBonus GrowthBonus;
            public readonly Dictionary<Stat, double> GrowthTotals = new Dictionary<Stat, double>();
            public int GrowthVersion = -1;
            public Build GrowthBuild;
            public Action<EventInfoAbilityInstance> OnAbilityCreated;
            public readonly HashSet<int> CritBasicVictims = new HashSet<int>();
            public float GrowthSentAt;
            public int GrowthSentVersion = -1;
            public uint GrowthOwnerNetId;
            public string GrowthOwnerKey;
        }

        internal struct PendingGimmick
        {
            public long ShieldEquipmentEpoch;
            public GimmickRequest Request;
            public Entity Victim;
            public PairComboDef Pair;
            public float Due;
            public Vector3 Center;
            public AuthoredPendingGimmick Authored;
            public string AuthoredChannelId;
            public float QueuedAt;
        }

        private sealed class MonsterRuntime
        {
            public Monster Monster;
            public float QueuedAt;
            public bool SpawnProcessed;
            public int DepthApplied;
            public StatBonus DepthBonus;
            public StatBonus SpecialBonus;
            public bool PressureApplied;
            public DataProcessor<FinalStats> PressureHealth;
            public VariantDef Variant;
            public DataProcessor<DamageData, Actor, Entity> HitCap;
            public bool DeathBurstTriggered;
            public string KillEventId;
            public string KillEventStreamId;
            public uint KillEventNetId;
            public long GraphEpoch, SegmentEpoch, RoomEpoch;
            public uint SyncNetId;
            public bool ClassificationQueued;
            public NightmareAffix ClassificationNightmare;
            public string ClassificationVariant;
            public Se_GenericShield_OneShot Ward;
            public Action<EventInfoDamage> OnDamageDealt;
            public bool Reflects;
            public bool Sunders;
            public MonsterBehaviorRuntime Behavior;
            public Se_GenericShield_OneShot BehaviorShield;
            // v1.29 wave 2：弱点・耐性の被ダメージ・与ダメージ処理。
            public DataProcessor<DamageData, Actor, Entity> TagDamage;
            public DataProcessor<DamageData, Actor, Entity> TagDealt;
        }

        private sealed class SunderRuntime
        {
            public StatBonus Bonus;
            public float Until;
        }

        private sealed class EnemyValidator : IBinaryEntityValidator
        {
            public bool Evaluate(Entity self, Entity target) =>
                target != null && target.isActive && target.GetRelation(self) == EntityRelation.Enemy;
        }

        private static readonly IBinaryEntityValidator EnemyFilter = new EnemyValidator();
        private static readonly HeroSkillLocation[] CooldownSkills =
            { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E };
        // 連携の判定に見る枠（Identity と Movement も記憶の対象になる）。
        private static readonly HeroSkillLocation[] LinkSkills =
            { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R, HeroSkillLocation.Identity, HeroSkillLocation.Movement };
        private readonly Func<int, int, bool> _resonanceNear;
        private PowerRuntime[] _scanPowers = Array.Empty<PowerRuntime>();
        private readonly HashSet<LinkKind> _triggeredLinks = new HashSet<LinkKind>();

        private readonly Dictionary<DewPlayer, ReceivedBuild> _builds = new Dictionary<DewPlayer, ReceivedBuild>();
        private readonly Dictionary<DewPlayer, BuildTransferReceiver> _incomingBuilds = new Dictionary<DewPlayer, BuildTransferReceiver>();
        private readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        private readonly List<Hero> _scratch = new List<Hero>();
        private readonly List<Build> _pressureBuilds = new List<Build>();
        private readonly HashSet<DewPlayer> _pressurePlayers = new HashSet<DewPlayer>();
        private readonly List<DewPlayer> _departedPlayers = new List<DewPlayer>();
        private DreamPressure _pressure = DreamPressure.Neutral;
        private int _pressurePlayerCount = -1;
        private readonly DataProcessor<DamageData, Actor, Entity> _pressureDamage;
        private readonly Action<DewPlayer> _onPressurePlayerAdded;
        private readonly Action<DewPlayer> _onPressurePlayerRemoved;
        private bool _pressureDirty = true;
        private int _infinityPressureStage;

        private Actor _registeredOn;
        private ClientEventManager _cem;
        private readonly Action<DreamforgeBuildMsg, DewPlayer> _onBuild;
        private readonly Action<DreamforgeCurseMsg, DewPlayer> _onCurse;
        private readonly Action<DreamforgeCurseClearMsg, DewPlayer> _onCurseClear;
        private readonly Action<DreamforgeTradeMsg, DewPlayer> _onTrade;
        private readonly Action<DreamforgeDreamEventStartedMsg, DewPlayer> _onPersonalDreamEvent;
        private readonly Action<EventInfoKill> _onDeath;
        private readonly Action<EventInfoDamage> _onTakeDamage;
        private readonly Action<EventInfoDamage> _onPairEnemyDamage;
        private readonly Action<EventInfoKill> _onPairEnemyDeath;
        private readonly HashSet<Entity> _pairEntities = new HashSet<Entity>();
        private readonly HashSet<int> _generatedPairDeaths = new HashSet<int>();
        private int _pairDamageDepth;
        private readonly Action<EventInfoAttackHit> _onAttackHit;
        private readonly Action<EventInfoApplyElemental> _onApplyElemental;
        private float _nextAreaScan;
        private readonly HashSet<Power> _triggeredPowers = new HashSet<Power>();
        private readonly HashSet<Shrine> _shrines = new HashSet<Shrine>();
        private readonly Action<Actor> _onActorAdd;
        private readonly Action<Actor> _onActorRemove;
        private readonly Action<Entity> _onShrineUsed;
        private readonly Action<EventInfoLoadZone> _onZoneLoaded;
        private readonly Action<EventInfoLoadRoom> _onRoomLoaded;
        private ZoneManager _zone;
        private bool _spreadingFire;
        private bool _shattering;
        // Actor damage/kill events are synchronous. Also suppress nested identity reactions.
        private int _gimmickDamageDepth;
        private readonly Dictionary<Type, string> _memorySourceTypes = new Dictionary<Type, string>();

        // 悪夢化エリート・夢の変種
        private ActorManager _am;
        private readonly Action<Entity> _onEntityAdd;
        private readonly Action<Entity> _onEntityRemove;
        private readonly Action<EventInfoKill> _onMonsterDeath;
        private readonly Action<EventInfoDamage> _onMonsterDamageTaken;
        private readonly Action<EventInfoAttackHit> _onMonsterAttackHit;
        private readonly Dictionary<Monster, MonsterRuntime> _monsters = new Dictionary<Monster, MonsterRuntime>();
        private readonly List<Monster> _monsterScratch = new List<Monster>();
        private readonly Dictionary<Hero, SunderRuntime> _sunders = new Dictionary<Hero, SunderRuntime>();
        private readonly List<Hero> _sunderScratch = new List<Hero>();
        private readonly HashSet<NightmareAffix> _triggeredAffixes = new HashSet<NightmareAffix>();
        private bool _reflectingDamage;
        private Dictionary<string, LucidDreamType> _lucidTypes;
        private readonly List<KeyValuePair<Monster, float>> _spawnQueue = new List<KeyValuePair<Monster, float>>();
        private readonly Dictionary<Monster, NightmareAffix> _nightmares = new Dictionary<Monster, NightmareAffix>();
        private float _nextNightmareSync;
        private readonly Func<int> _dailyIdOfHost;
        private readonly Dictionary<Monster, float> _regen = new Dictionary<Monster, float>();
        private readonly List<Monster> _regenScratch = new List<Monster>();
        private readonly Rng _rng = new Rng(Rng.SeedFrom(DateTime.UtcNow.Ticks.ToString()));
        private float _nextRegen;
        private bool _roomHasVariant;
        private bool _loggedVariantSpawn, _loggedDeathBurst;
        private readonly List<Hero> _deathBurstHeroes = new List<Hero>();

        public HostAuthority(Func<int> dailyIdOfHost)
        {
            InitializeAssignedMechanisms();
            InitializeMemoryAttribution();
            BasicAttackContext.Prewarm(); NativeDamageContext.Prewarm(); ElementApplicationContext.Prewarm();
            BossDisplacementReuse.Prewarm(); BossBasicEffectReuse.Prewarm();
            NativeAttributedHpDamage.Prewarm(); PrewarmNativeShieldSnapshot();
            _ = _runtimes.Values; _ = _attributionEquipment.Keys; _ = _attributionEquipment.Values;
            _ = _bossHysteriaStates.Values; _ = _lastStarlights.Values;
            _dailyIdOfHost = dailyIdOfHost;
            _pressureDamage = (ref DamageData damage, Actor actor, Entity target) =>
                damage.ApplyAmplification((float)_pressure.DamageMultiplier - 1f);
            _onPressurePlayerAdded = player =>
            {
                _pressureDirty = true;
                if (player != null) RegisterKillPeer(player);
            };
            _onPressurePlayerRemoved = player =>
            {
                if (!ReferenceEquals(player, null))
                {
                    _builds.Remove(player);
                    _incomingBuilds.Remove(player);
                    RemoveBuildValidationPeer(player);
                    RemoveKillPeer(player);
                }
                _pressureDirty = true;
            };
            _onBuild = OnBuild;
            _onCurse = OnCurse;
            _onCurseClear = OnCurseClear;
            _onTrade = OnTrade;
            _onPersonalDreamEvent = OnPersonalDreamEvent;
            _onDeath = OnDeath;
            _onTakeDamage = OnTakeDamage;
            _onPairEnemyDamage = OnPairEnemyDamage;
            _onPairEnemyDeath = OnPairEnemyDeath;
            _onAttackHit = OnAttackHit;
            _onEntityAdd = OnEntityAdd;
            _onEntityRemove = OnEntityRemove;
            _onMonsterDeath = OnMonsterDeath;
            _onMonsterDamageTaken = OnMonsterDamageTaken;
            _onMonsterAttackHit = OnMonsterAttackHit;
            _onApplyElemental = OnApplyElemental;
            _resonanceNear = RuntimesNear;
            _onActorAdd = OnActorAdd;
            _onActorRemove = OnActorRemove;
            _onShrineUsed = OnShrineUsed;
            _onZoneLoaded = OnZoneLoaded;
            _onRoomLoaded = info => _roomHasVariant = false;
        }

        public bool IsActive => _registeredOn != null;

        // Each stage runs on its own: one persistently failing stage must not stop monster pruning,
        // behaviors or the periodic resync for everyone, and must not flood the log (#74).
        // Same shape as ClientSession.Tick: per-stage try/catch with a 10 s log limit per stage.
        private Action[] _tickStages;
        private string[] _tickStageNames;
        private TickGuard _tickGuard;
        private float _tickNow;

        public void Tick()
        {
            if (!NetworkServer.active)
            {
                if (_pressurePlayerCount >= 0 || _registeredOn != null || _cem != null || _am != null || _zone != null) Detach();
                return;
            }
            EnsureRegistered();
            NativeInstance = this;
            if (_registeredOn == null) return;
            if (_tickStages == null) BuildTickStages();
            _tickNow = Time.time;
            _tickGuard.Run(Time.unscaledTime);
        }

        private void BuildTickStages()
        {
            _tickStageNames = new[]
            {
                "sacrifice shields", "build updates", "run modifiers", "pressure", "pending builds",
                "gem slots", "waypoint heroes", "area scan", "new powers", "reactions", "gimmick apply",
                "boss effects", "boss visuals", "identity strikes", "gimmicks v129", "sap prune", "attribution prune", "runtime", "run growth", "currency",
                "shield pools", "spawns", "monster prune", "monster behaviors", "kill replay", "sunders",
                "nightmare regen", "classification resync",
            };
            _tickStages = new Action[]
            {
                UpdateSacrificeShields, StageBuildUpdates, RefreshRunModifiers, StagePressure, PruneAndApplyPending,
                TickGemSlots, SyncWaypointHeroes, StageAreaScan, StageNewPowers, StageReactions, StageGimmickApply,
                StageBossEffects, TickBossVisualSnapshots, UpdateIdentityStrikes, StageGimmicksV129, StageSapPrune, PruneMemoryAttribution, StageRuntimes, StageRunGrowth, StageCurrency,
                StageModShieldPools, ProcessSpawns, StageMonsterPrune, StageMonsterBehaviors, TickKillReplay, StageSunders,
                StageNightmareRegen, StageClassificationResync,
            };
            _tickGuard = new TickGuard(_tickStages, _tickStageNames, 10f, message => Log.Error("Host tick " + message));
        }

        private void StageBuildUpdates() => ProcessBuildUpdates(Time.unscaledTime);

        private void StagePressure()
        {
            int stage = ClientSession.HostRun?.Infinity?.PressureStage ?? 0;
            if (_infinityPressureStage != stage)
            {
                _infinityPressureStage = stage;
                _pressureDirty = true;
            }
            if (_pressureDirty) RefreshPressure();
        }

        private void StageAreaScan()
        {
            if (_tickNow < _nextAreaScan) return;
            _nextAreaScan = _tickNow + 0.25f;
            ScanArea();
        }

        private void StageNewPowers()
        {
            foreach (var rt in _runtimes.Values) FlushNewPowers(rt);
        }

        private void StageReactions()
        {
            foreach (var rt in _runtimes.Values) UpdateReactions(rt);
        }

        private void StageGimmickApply()
        {
            foreach (var rt in _runtimes.Values) ApplyPendingGimmicks(rt, _tickNow);
        }

        private void StageBossEffects()
        {
            foreach (var rt in _runtimes.Values) TickBossEffects(rt, _tickNow);
        }

        private void StageGimmicksV129()
        {
            foreach (var rt in _runtimes.Values) UpdateGimmicksV129(rt, _tickNow);
        }

        private void StageSapPrune() => PruneSapProcessors(_tickNow);

        private void StageRuntimes()
        {
            foreach (var rt in _runtimes.Values) UpdateRuntime(rt, _tickNow);
        }

        private void StageRunGrowth()
        {
            foreach (var rt in _runtimes.Values) ApplyRunGrowth(rt, _tickNow);
        }

        private void StageCurrency() => SyncCurrency(_tickNow);

        private void StageModShieldPools() => UpdateModShieldPools(_tickNow);

        private void StageMonsterPrune()
        {
            PruneMonsters(_tickNow);
            PrunePressureDividendDeaths();
        }

        private void StageMonsterBehaviors() => TickMonsterBehaviors(_tickNow);

        private void StageSunders() => ExpireSunders(_tickNow);

        private void StageNightmareRegen()
        {
            if (_tickNow < _nextRegen) return;
            _nextRegen = _tickNow + 0.5f;
            RegenNightmares();
        }

        private void StageClassificationResync()
        {
            if (_tickNow < _nextNightmareSync) return;
            _nextNightmareSync = _tickNow + 5f;
            ResyncMonsterClassifications();
            SendPressure();
        }

        private void RefreshPressure(bool synchronize = false)
        {
            _pressureDirty = false;
            _pressureBuilds.Clear();
            _pressurePlayers.Clear();
            // The game roster includes dead participants, but excludes lobby players and spectators.
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (player == null || !player.isHumanPlayer || !_pressurePlayers.Add(player)) continue;
                _pressureBuilds.Add(_builds.TryGetValue(player, out var received) ? received.Build : null);
            }
            _departedPlayers.Clear();
            foreach (var player in _builds.Keys)
                if (!_pressurePlayers.Contains(player)) _departedPlayers.Add(player);
            foreach (var player in _incomingBuilds.Keys)
                if (!_pressurePlayers.Contains(player)) _departedPlayers.Add(player);
            foreach (var player in _departedPlayers)
            {
                _builds.Remove(player);
                _incomingBuilds.Remove(player);
                RemoveBuildValidationPeer(player);
            }
            var pressure = DreamPressure.Average(_pressureBuilds)
                .WithRunModifiers(ClientSession.HostRun?.DreamDepth ?? 0, ActiveWaypointTotals.PressureMultiplier)
                .WithInfinityPressure(_infinityPressureStage);
            bool changed = pressure.HealthMultiplier != _pressure.HealthMultiplier
                || pressure.DamageMultiplier != _pressure.DamageMultiplier;
            synchronize |= changed || _pressurePlayerCount != _pressureBuilds.Count;
            _pressurePlayerCount = _pressureBuilds.Count;
            _pressure = pressure;
            if (changed)
            {
                foreach (var rt in _monsters.Values)
                {
                    if (!rt.PressureApplied || rt.Monster == null || rt.Monster.Status == null) continue;
                    // CalculateStats starts from base stats and preserves current/max health itself.
                    rt.Monster.Status.MarkStatsDirty();
                    rt.Monster.Status.CalculateStatsIfDirty();
                }
            }
            if (synchronize) SendPressure();
        }

        private void SendPressure()
        {
            _registeredOn?.CustomRpc_SendMessageToAllClients(new DreamforgePressureMsg
            {
                protocol = Protocol.Version,
                healthMultiplier = (float)_pressure.HealthMultiplier,
                damageMultiplier = (float)_pressure.DamageMultiplier,
                runChoices = ClientSession.HostRunChoices
            });
            // Reuse the infrequent state resync for players whose run started after the first report.
            var runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            foreach (var rt in _runtimes.Values)
                if (rt.BountyRunId == runId && rt.ReportedLinks > 0)
                    SendBountyReport(rt, BountyReportKind.LinksSatisfied, rt.ReportedLinks);
        }

        private void ApplyPressure(MonsterRuntime rt)
        {
            if (rt.PressureApplied) return;
            var monster = rt.Monster;
            // Final processors multiply after native multiplayer, depth, nightmare and variant stats.
            InstallPressureHealthProcessor(rt);
            monster.dealtDamageProcessor.Add(_pressureDamage);
            rt.PressureApplied = true;
            monster.Status.CalculateStatsIfDirty();
        }

        /// <summary>パーティの最大の夢の深度（MOD導入者の Build から）。</summary>
        private int PartyDepth()
        {
            var hostRun = ClientSession.HostRun;
            int d = hostRun?.Heat ?? 0;
            foreach (var kv in _builds)
                if (kv.Key != null && kv.Key.hero != null && (hostRun == null || kv.Key != DewPlayer.local))
                    d = Math.Max(d, kv.Value.Build.Heat);
            return d;
        }

        private double PartyGearChanceMult()
        {
            double mult = 1.0;
            foreach (var kv in _builds)
                if (kv.Key != null && kv.Key.hero != null)
                    mult = Math.Max(mult, Nightmares.GearChanceMult(kv.Value.Build));
            return mult;
        }

        private void OnEntityAdd(Entity e)
        {
            if (e != null && _pairEntities.Add(e))
            {
                e.EntityEvent_OnTakeDamage += _onPairEnemyDamage;
                e.EntityEvent_OnDeath += _onPairEnemyDeath;
            }
            if (e != null && _nativeDeathEntities.Add(e)) e.EntityEvent_OnDeath += _onDeath;
            if (!(e is Monster m) || _monsters.ContainsKey(m)) return;
            try
            {
                var infinity = InfinityMode.State;
                var rt = new MonsterRuntime { Monster = m, QueuedAt = Time.time,
                    GraphEpoch = infinity?.GraphEpoch ?? 0, SegmentEpoch = infinity?.SegmentEpoch ?? 0,
                    RoomEpoch = infinity?.RoomEpoch ?? 0 };
                _monsters[m] = rt;
                TrackPressureDividendSpawn(m);
                m.EntityEvent_OnDeath += _onMonsterDeath;
                _spawnQueue.Add(new KeyValuePair<Monster, float>(m, rt.QueuedAt));
            }
            catch (Exception ex) { Log.Error("Host: OnEntityAdd " + ex); }
        }

        private void OnEntityRemove(Entity e)
        {
            if (e != null && _pairEntities.Remove(e))
            {
                e.EntityEvent_OnTakeDamage -= _onPairEnemyDamage;
                e.EntityEvent_OnDeath -= _onPairEnemyDeath;
                _generatedPairDeaths.Remove(e.GetInstanceID());
            }
            if (e != null)
                foreach (var heroRuntime in _runtimes.Values) heroRuntime.Powers.ForgetNewPowerTarget(e.GetInstanceID());
            if (!ReferenceEquals(e, null) && _nativeDeathEntities.Remove(e)) e.EntityEvent_OnDeath -= _onDeath;
            if (e is Monster m) RemoveMonster(m);
            if (e is Monster removedMonster) ForgetPressureDividendSpawn(removedMonster);
        }

        private void OnMonsterDeath(EventInfoKill info)
        {
            if (!(info.victim is Monster m)) return;
            try { GrantEliteKillGold(m); }
            catch (Exception ex) { Log.Error("Host: elite kill gold " + ex); }
            try
            {
                CaptureAuthoritativeRunKill(m);
                CapturePressureDividendDeath(m);
                if (!_monsters.TryGetValue(m, out var rt) || rt.Variant == null
                    || (rt.Variant.Traits & VariantTrait.DeathBurst) == 0 || rt.DeathBurstTriggered) return;
                rt.DeathBurstTriggered = true;
                if (!_loggedDeathBurst)
                {
                    _loggedDeathBurst = true;
                    Log.Info($"variant DeathBurst triggered: {rt.Variant.Id} netId={m.netId}");
                }
                if (_am == null) return;
                // Damage can remove heroes or trigger another burst. Nested calls borrow a suffix.
                int start = _deathBurstHeroes.Count;
                _deathBurstHeroes.AddRange(_am.allHeroes);
                int end = _deathBurstHeroes.Count;
                var center = m.position;
                float radiusSq = Variants.DeathBurstRadius * Variants.DeathBurstRadius;
                try
                {
                    for (int i = start; i < end; i++)
                    {
                        var hero = _deathBurstHeroes[i];
                        if (!Alive(hero) || hero.GetRelation(m) != EntityRelation.Enemy
                            || (hero.position - center).sqrMagnitude > radiusSq) continue;
                        m.PureDamage(hero.maxHealth * Variants.DeathBurstPct / 100f, 0f)
                            .Dispatch(hero, default(ReactionChain));
                    }
                }
                finally { _deathBurstHeroes.RemoveRange(start, end - start); }
            }
            catch (Exception ex) { Log.Error("Host: variant DeathBurst " + ex); }
            finally { RemoveMonster(m); }
        }

        private void LogAffixTrigger(NightmareAffix affix, string action)
        {
            if (_triggeredAffixes.Add(affix)) Log.Info("nightmare " + affix + " " + action);
        }

        private void RemoveMonster(Monster m)
        {
            if (ReferenceEquals(m, null)) return;
            if (_monsters.TryGetValue(m, out var rt))
            {
                _monsters.Remove(m);
                SendMonsterRemoval(rt);
                Unhook(rt);
            }
            _nightmares.Remove(m);
            _regen.Remove(m);
            // Combat hooks are released on death, before Actor.InvokeOnKill attributes the memory.
            // Keep the immutable reward snapshot until entity removal or its bounded expiry.
            if (!_pressureDividendSpawns.TryGetValue(m, out var spawn) || spawn.Death == null)
                ForgetPressureDividendSpawn(m);
            _generatedPairDeaths.Remove(m.GetInstanceID());
            if (_pairEntities.Remove(m))
            {
                m.EntityEvent_OnTakeDamage -= _onPairEnemyDamage;
                m.EntityEvent_OnDeath -= _onPairEnemyDeath;
            }
            if (_nativeDeathEntities.Remove(m)) m.EntityEvent_OnDeath -= _onDeath;
            for (int i = _spawnQueue.Count - 1; i >= 0; i--)
                if (_spawnQueue[i].Key == m) _spawnQueue.RemoveAt(i);
        }

        private void UnhookMonsters()
        {
            foreach (var rt in _monsters.Values) { SendMonsterRemoval(rt); Unhook(rt); }
            _monsters.Clear();
            ClearPressureDividendSpawns();
            foreach (var entity in _pairEntities)
            {
                if (entity == null) continue;
                entity.EntityEvent_OnTakeDamage -= _onPairEnemyDamage;
                entity.EntityEvent_OnDeath -= _onPairEnemyDeath;
            }
            _pairEntities.Clear();
            _generatedPairDeaths.Clear();
        }

        private void Unhook(MonsterRuntime rt)
        {
            var m = rt.Monster;
            if (m == null) return;
            RemoveMonsterBehavior(rt);
            RemoveVariantTags(rt);
            if (rt.PressureApplied)
            {
                try
                {
                    m.Status.finalStatsProcessors.Remove(rt.PressureHealth);
                    m.Status.CalculateStatsIfDirty();
                }
                catch (Exception ex) { Log.Error("Host: unhook pressure health " + ex); }
                try { m.dealtDamageProcessor.Remove(_pressureDamage); }
                catch (Exception ex) { Log.Error("Host: unhook pressure damage " + ex); }
                rt.PressureApplied = false;
            }
            // Each removal is independent: one failed cleanup must not leave other hooks attached.
            try { m.EntityEvent_OnDeath -= _onMonsterDeath; }
            catch (Exception ex) { Log.Error("Host: unhook monster death " + ex); }
            try { if (rt.Reflects) m.EntityEvent_OnTakeDamage -= _onMonsterDamageTaken; }
            catch (Exception ex) { Log.Error("Host: unhook monster Thorns " + ex); }
            try { if (rt.OnDamageDealt != null) m.ActorEvent_OnDealDamage -= rt.OnDamageDealt; }
            catch (Exception ex) { Log.Error("Host: unhook monster leech " + ex); }
            try { if (rt.Sunders) m.EntityEvent_OnAttackHit -= _onMonsterAttackHit; }
            catch (Exception ex) { Log.Error("Host: unhook monster Sunder " + ex); }
            try { if (rt.HitCap != null) m.takenDamageProcessor.Remove(rt.HitCap); }
            catch (Exception ex) { Log.Error("Host: unhook variant HitCap " + ex); }
            try { if (rt.Ward != null && rt.Ward.isActive) rt.Ward.Destroy(); }
            catch (Exception ex) { Log.Error("Host: remove nightmare Ward " + ex); }
            ReleaseMirageSkin(m);
            try
            {
                if (m.Status != null)
                {
                    if (rt.DepthBonus != null) m.Status.RemoveStatBonus(rt.DepthBonus);
                    if (rt.SpecialBonus != null) m.Status.RemoveStatBonus(rt.SpecialBonus);
                }
            }
            catch (Exception ex) { Log.Error("Host: remove monster bonuses " + ex); }
        }

        private void PruneMonsters(float now)
        {
            _monsterScratch.Clear();
            foreach (var kv in _monsters)
                if (kv.Key == null || (!kv.Key.isActive && (kv.Value.SpawnProcessed || now - kv.Value.QueuedAt >= 20f)))
                    _monsterScratch.Add(kv.Key);
            foreach (var m in _monsterScratch) RemoveMonster(m);
        }

        private readonly Dictionary<KeyValuePair<Monster, Hero>, float> _thornsNextReflect = new Dictionary<KeyValuePair<Monster, Hero>, float>();
        private readonly List<KeyValuePair<Monster, Hero>> _thornsScratch = new List<KeyValuePair<Monster, Hero>>();

        private void PruneThornsTimers(float now)
        {
            _thornsScratch.Clear();
            foreach (var kv in _thornsNextReflect)
                if (kv.Value < now || kv.Key.Key == null || kv.Key.Value == null) _thornsScratch.Add(kv.Key);
            foreach (var k in _thornsScratch) _thornsNextReflect.Remove(k);
        }

        private void OnMonsterDamageTaken(EventInfoDamage info)
        {
            try
            {
                if (_reflectingDamage || (_registeredOn != null && info.chain.DidReact(_registeredOn, false))
                    || info.damage.amount <= 0 || !(info.victim is Monster m)
                    || !_monsters.TryGetValue(m, out var rt) || !rt.Reflects) return;
                var attacker = info.actor != null ? info.actor as Entity ?? info.actor.firstEntity : null;
                if (!(attacker is Hero hero) || !Alive(hero) || hero.GetRelation(m) != EntityRelation.Enemy) return;
                float now = Time.time;
                var key = new KeyValuePair<Monster, Hero>(m, hero);
                if (_thornsNextReflect.TryGetValue(key, out float next) && now < next) return;
                _thornsNextReflect[key] = now + Nightmares.ThornsReflectInterval;
                if (_thornsNextReflect.Count > 256) PruneThornsTimers(now);
                float amount = Nightmares.ThornsReflectAmount(info.damage.amount, hero.maxHealth);
                if (amount <= 0f) return;
                _reflectingDamage = true;
                try
                {
                    m.PureDamage(amount, 0f)
                        .Dispatch(hero, _registeredOn != null ? info.chain.New(_registeredOn) : info.chain);
                    LogAffixTrigger(NightmareAffix.Thorned, "reflected");
                }
                finally { _reflectingDamage = false; }
            }
            catch (Exception ex) { Log.Error("Host: nightmare Thorned " + ex); }
        }

        private void OnMonsterDamageDealt(MonsterRuntime rt, EventInfoDamage info)
        {
            try
            {
                var m = rt.Monster;
                if (m == null || !m.isActive || m.currentHealth <= 0 || info.damage.amount <= 0
                    || info.victim == null || info.victim.GetRelation(m) != EntityRelation.Enemy) return;
                m.Heal(info.damage.amount * Nightmares.RavenousLeechPct / 100f).Dispatch(m, info.chain);
                LogAffixTrigger(NightmareAffix.Ravenous, "healed");
            }
            catch (Exception ex) { Log.Error("Host: nightmare Ravenous " + ex); }
        }

        private void OnMonsterAttackHit(EventInfoAttackHit info)
        {
            try
            {
                if (!(info.attacker is Monster m) || !_monsters.TryGetValue(m, out var rt) || !rt.Sunders
                    || !(info.victim is Hero hero) || !Alive(hero) || hero.GetRelation(m) != EntityRelation.Enemy
                    || hero.Status == null) return;
                if (!_sunders.TryGetValue(hero, out var sunder))
                {
                    sunder = new SunderRuntime { Bonus = new StatBonus { armorFlat = -Nightmares.SunderArmor } };
                    hero.Status.AddStatBonus(sunder.Bonus);
                    _sunders[hero] = sunder;
                    hero.Status.CalculateStatsIfDirty();
                }
                sunder.Until = Time.time + Nightmares.SunderSeconds;
                LogAffixTrigger(NightmareAffix.Sundering, "armor reduced");
            }
            catch (Exception ex) { Log.Error("Host: nightmare Sundering " + ex); }
        }

        private void RemoveSunder(Hero hero)
        {
            if (!_sunders.TryGetValue(hero, out var sunder)) return;
            _sunders.Remove(hero);
            try
            {
                if (hero != null && hero.Status != null)
                {
                    hero.Status.RemoveStatBonus(sunder.Bonus);
                    hero.Status.CalculateStatsIfDirty();
                }
            }
            catch (Exception ex) { Log.Error("Host: remove Sunder " + ex); }
        }

        private void ExpireSunders(float now)
        {
            _sunderScratch.Clear();
            foreach (var kv in _sunders)
                if (!Alive(kv.Key) || now >= kv.Value.Until) _sunderScratch.Add(kv.Key);
            foreach (var hero in _sunderScratch) RemoveSunder(hero);
        }

        private void ClearSunders()
        {
            _sunderScratch.Clear();
            foreach (var hero in _sunders.Keys) _sunderScratch.Add(hero);
            foreach (var hero in _sunderScratch) RemoveSunder(hero);
        }

        private void LogPowerTrigger(Power power)
        {
            if (_triggeredPowers.Add(power)) Log.Info("power " + power + " triggered");
        }

        private void OnActorAdd(Actor actor)
        {
            if (actor is Summon summon)
            {
                var hero = summon.FindFirstAncestorOfType<Hero>();
                if (hero != null && _runtimes.TryGetValue(hero, out var rt)) HookSummon(rt, summon);
            }
            if (actor is Shrine shrine && _shrines.Add(shrine))
                shrine.ClientEvent_OnSuccessfulUse += _onShrineUsed;
            if (actor is Pickup_DreamDust dust) HookDreamDust(dust);
        }

        private void OnActorRemove(Actor actor)
        {
            if (actor is Summon summon)
            {
                UnhookSupportSummonV129(summon);
                foreach (var rt in _runtimes.Values)
                    if (rt.Summons.TryGetValue(summon, out var processor))
                    {
                        summon.dealtDamageProcessor.Remove(processor);
                        rt.Summons.Remove(summon);
                    }
            }
            if (actor is Monster m) RemoveMonster(m);
            if (actor is Pickup_DreamDust dust) UnhookDreamDust(dust);
            if (actor is Shrine shrine && _shrines.Remove(shrine))
                shrine.ClientEvent_OnSuccessfulUse -= _onShrineUsed;
        }

        private void UnhookShrines()
        {
            foreach (var shrine in _shrines)
            {
                if (shrine == null) continue;
                try { shrine.ClientEvent_OnSuccessfulUse -= _onShrineUsed; } catch (Exception) { }
            }
            _shrines.Clear();
        }

        /// <summary>途中からホスト処理を付けた場合も、すでにある聖堂を拾う。</summary>
        private void ScanShrines()
        {
            foreach (var shrine in UnityEngine.Object.FindObjectsOfType<Shrine>()) OnActorAdd(shrine);
        }

        private void OnShrineUsed(Entity user)
        {
            if (!(user is Hero hero) || !_runtimes.TryGetValue(hero, out var rt) || !Alive(hero)) return;
            if (rt.Powers.OnShrineUsed()) LogPowerTrigger(Power.Devotion);
        }

        private void OnZoneLoaded(EventInfoLoadZone info)
        {
            ClearAssignedMechanismTransients();
            _roomHasVariant = false;
            ClearZoneReactions();
            foreach (var rt in _runtimes.Values) rt.Powers.OnZoneLoaded(Time.time);
            UnhookShrines();
            ScanShrines();
        }

        private void ProcessSpawns()
        {
            if (_spawnQueue.Count == 0) return;
            if (_zone != null && _zone.isInAnyTransition) return;
            // 戦闑では選択を解決できない純白の入口でも、戦う敵への必須の圧・深度補正は確定を待たない。
            if (!SpawnInitRules.ProcessesWhileAwaitingChoice(
                ClientSession.HostRun?.AwaitingChoice == true, ClientSession.HostCombatChoiceSuspended)) return;
            float now = Time.time;
            int depth = PartyDepth();
            int dailyId = _dailyIdOfHost != null ? _dailyIdOfHost() : 0;
            double mult = (DailyDream.Get(dailyId)?.NightmareMult ?? 1.0) * PartyGearChanceMult();
            for (int i = _spawnQueue.Count - 1; i >= 0; i--)
            {
                var m = _spawnQueue[i].Key;
                if (m == null)
                {
                    _spawnQueue.RemoveAt(i);
                    continue;
                }
                if (!m.isActive || m.Status == null || m.Status.maxHealth <= 0)
                {
                    // 出現しないまま残る（プールに戻された等）ものは20秒で捨て、待ち行列を溜めない。
                    if (now - _spawnQueue[i].Value > 20f) _spawnQueue.RemoveAt(i);
                    continue;
                }
                if (m.owner == null || m.owner.isHumanPlayer) { _spawnQueue.RemoveAt(i); continue; }
                if (!_monsters.TryGetValue(m, out var rt)) { _spawnQueue.RemoveAt(i); continue; }
                ApplyPressure(rt);
                // Pressure is already active; give the initial depth build time to arrive.
                if (_builds.Count == 0 && now - _spawnQueue[i].Value < 15f) continue;
                _spawnQueue.RemoveAt(i);
                var tier = (MonsterTier)Math.Min((int)MonsterTier.Boss, (int)m.type);
                if (rt.SpawnProcessed) continue;
                // 深さ0では深度ボーナス・悪夢化の初期化をしないが、処理済みとして印を付ける（#71）。
                // 潜行で深さが1以上になれば、#60 の揃え直しの対象になる。
                rt.SpawnProcessed = true;
                if (SpawnInitRules.SkipsDepthInit(depth, ActiveWaypointTotals.AllNightmares, ActiveWaypointTotals.NightmareChanceMultiplier)) continue;
                try
                {
                    ApplyDepthBonus(rt, tier, depth);
                    var variant = ActiveWaypointTotals.AllNightmares ? null : Variants.Roll(_rng, m.GetType().Name, depth, _roomHasVariant);
                    if (variant != null)
                    {
                        _roomHasVariant = true;
                        MakeVariant(rt, variant);
                        continue;
                    }
                    var affix = RollWaypointNightmare(m, tier, depth, mult);
                    if (affix != NightmareAffix.None) MakeNightmare(m, affix);
                }
                catch (Exception ex)
                {
                    Log.Error("Host: ProcessSpawns " + ex);
                }
            }
        }

        /// <summary>
        /// 深度ボーナスを置き換える。出現時も、潜行が深まった確定後の再適用もこの入口を通る。
        /// 古いボーナスを外してから足し直すため、二重には掛からない。本体の再計算はHPの割合を保つため、
        /// 再計算後には現在HPを置き換え前の絶対値へ戻す（満タンの敵は新しい最大HPのまま、#68）。
        /// </summary>
        private void ApplyDepthBonus(MonsterRuntime rt, MonsterTier tier, int depth)
        {
            rt.DepthApplied = depth;
            var m = rt.Monster;
            var stats = Nightmares.DepthBonus(tier, depth);
            if (m == null || m.Status == null) return;
            float healthBefore = m.Status.currentHealth;
            float maxHealthBefore = m.Status.maxHealth;
            if (rt.DepthBonus != null)
            {
                m.Status.RemoveStatBonus(rt.DepthBonus);
                rt.DepthBonus = null;
            }
            if (stats.Count > 0)
            {
                var bonus = ToMonsterStatBonus(stats);
                rt.DepthBonus = bonus;
                m.Status.AddStatBonus(bonus);
            }
            m.Status.CalculateStatsIfDirty();
            float healthAfter = SpawnInitRules.HealthAfterDepthRealign(healthBefore, maxHealthBefore, m.Status.maxHealth);
            if (healthAfter != m.Status.currentHealth) m.Status.SetHealth(healthAfter);
        }

        private static StatBonus ToMonsterStatBonus(IReadOnlyList<StatLine> stats)
        {
            var bonus = new StatBonus();
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                float v = StatUnits.ToGame(s.Stat, s.Value);
                switch (s.Stat)
                {
                    case Stat.MaxHealthPct: bonus.maxHealthPercentage += v; break;
                    case Stat.Armor: bonus.armorFlat += v; break;
                    case Stat.AttackPct: bonus.attackDamagePercentage += v; break;
                    case Stat.AttackSpeedPct: bonus.attackSpeedPercentage += v; break;
                    case Stat.MoveSpeedPct: bonus.movementSpeedPercentage += v; break;
                    case Stat.PowerPct: bonus.abilityPowerPercentage += v; break;
                    case Stat.Haste: bonus.abilityHasteFlat += v; break;
                }
            }
            return bonus;
        }

        private void MakeNightmare(Monster m, NightmareAffix affix)
        {
            var rt = _monsters[m];
            rt.SpecialBonus = ToMonsterStatBonus(Nightmares.MonsterStats(affix, out float regen));
            m.Status.AddStatBonus(rt.SpecialBonus);
            if (!Nightmares.HasBehavior(affix) || (affix & LegacyNightmareAffixes) != 0)
                AttachMirageSkin(m, Nightmares.Count(affix) >= 2);
            _nightmares[m] = affix;
            ApplyMonsterAffixes(rt, affix, regen);
            Log.Info($"Nightmare: {m.GetType().Name} netId={m.netId} affixes={affix}");
            SendMonsterClassification(rt);
        }

        private void MakeVariant(MonsterRuntime rt, VariantDef variant)
        {
            var m = rt.Monster;
            rt.Variant = variant;
            rt.SpecialBonus = ToMonsterStatBonus(variant.Stats);
            m.Status.AddStatBonus(rt.SpecialBonus);
            if (!Variants.IsExpanded(variant))
                AttachMirageSkin(m, Nightmares.Count(variant.Affixes) >= 2);
            m.Status.CalculateStatsIfDirty();
            float regen = 0f;
            if ((variant.Affixes & NightmareAffix.Regenerating) != 0)
                Nightmares.MonsterStats(variant.Affixes, out regen);
            ApplyMonsterAffixes(rt, variant.Affixes, regen);
            if ((variant.Traits & VariantTrait.HitCap) != 0)
            {
                rt.HitCap = (ref DamageData damage, Actor actor, Entity target) =>
                {
                    float amount = damage.currentAmount;
                    float cap = m.maxHealth * Variants.HitCapPct / 100f;
                    if (amount > cap && cap > 0f) damage = damage.ApplyRawMultiplier(cap / amount);
                };
                m.takenDamageProcessor.Add(rt.HitCap);
            }
            ApplyVariantTags(rt, variant.Tags);
            if (!_loggedVariantSpawn)
            {
                _loggedVariantSpawn = true;
                Log.Info($"variant spawned: {variant.Id} {m.GetType().Name} netId={m.netId}");
            }
            SendMonsterClassification(rt);
        }

        private void ApplyMonsterAffixes(MonsterRuntime rt, NightmareAffix affix, float regen)
        {
            var m = rt.Monster;
            if (regen > 0) _regen[m] = regen;
            if ((affix & NightmareAffix.Thorned) != 0)
            {
                rt.Reflects = true;
                m.EntityEvent_OnTakeDamage += _onMonsterDamageTaken;
            }
            if ((affix & NightmareAffix.Ravenous) != 0)
            {
                rt.OnDamageDealt = info => OnMonsterDamageDealt(rt, info);
                m.ActorEvent_OnDealDamage += rt.OnDamageDealt;
            }
            if ((affix & NightmareAffix.Sundering) != 0)
            {
                rt.Sunders = true;
                m.EntityEvent_OnAttackHit += _onMonsterAttackHit;
            }
            if ((affix & NightmareAffix.Warded) != 0)
            {
                m.Status.CalculateStatsIfDirty();
                rt.Ward = m.GiveShield(m, m.maxHealth * Nightmares.WardShieldPct / 100f,
                    3600f, false, default(ReactionChain));
                if (rt.Ward != null) LogAffixTrigger(NightmareAffix.Warded, "shield granted");
            }
            ApplyMonsterBehavior(rt, affix);
        }

        private List<MirageSkinEffect> _mirageTier0, _mirageTier1;

        /// <summary>
        /// 悪夢化した敵に本体のエリート効果（MirageSkin：見た目と専用攻撃）を付ける。
        /// 本体がすでに付けている敵には重ねない。深い悪夢（接頭2つ以上）は tier 1 も候補にする。
        /// </summary>
        private void RegenNightmares()
        {
            if (_regen.Count == 0) return;
            _regenScratch.Clear();
            foreach (var kv in _regen)
            {
                var m = kv.Key;
                if (m == null || !m.isActive)
                {
                    _regenScratch.Add(m);
                    continue;
                }
                if (m.currentHealth < m.maxHealth) m.Heal(m.maxHealth * kv.Value / 100f * 0.5f).Dispatch(m);
            }
            foreach (var m in _regenScratch) _regen.Remove(m);
        }

        private void EnsureRegistered()
        {
            var am = NetworkedManagerBase<ActorManager>.softInstance;
            var actor = am != null ? am.serverActor : null;
            if (!ReferenceEquals(actor, _registeredOn))
            {
                ClearWaypointHeroes();
                DewPlayer.onGamePlayerAdded -= _onPressurePlayerAdded;
                DewPlayer.onGamePlayerRemoved -= _onPressurePlayerRemoved;
                if (_registeredOn != null)
                {
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeBuildMsg>(_onBuild); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeCurseMsg>(_onCurse); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeCurseClearMsg>(_onCurseClear); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeTradeMsg>(_onTrade); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeDreamEventStartedMsg>(_onPersonalDreamEvent); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeKillReceiptMsg>(OnKillReceipt); } catch (Exception) { }
                }
                foreach (var rt in _runtimes.Values) { RemoveBonuses(rt); Unhook(rt); }
                _runtimes.Clear();
                ReleaseCurrency();
                _builds.Clear();
                _incomingBuilds.Clear();
                ClearBuildValidationPeers();
                _pressurePlayerCount = -1;
                _pressureDirty = true;
                ResetKillReplayConnections();
                _registeredOn = actor;
                if (actor != null)
                {
                    DewPlayer.onGamePlayerAdded += _onPressurePlayerAdded;
                    DewPlayer.onGamePlayerRemoved += _onPressurePlayerRemoved;
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeBuildMsg>(nameof(DreamforgeBuildMsg), _onBuild);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeCurseMsg>(nameof(DreamforgeCurseMsg), _onCurse);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeCurseClearMsg>(nameof(DreamforgeCurseClearMsg), _onCurseClear);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeTradeMsg>(nameof(DreamforgeTradeMsg), _onTrade);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeDreamEventStartedMsg>(nameof(DreamforgeDreamEventStartedMsg), _onPersonalDreamEvent);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeKillReceiptMsg>(nameof(DreamforgeKillReceiptMsg), OnKillReceipt);
                    RegisterHello(actor);
                    Log.Info("Host: registered build handler.");
                }
            }
            if (am != _am)
            {
                if (_am != null)
                {
                    try { _am.ClientEvent_OnEntityAdd -= _onEntityAdd; } catch (Exception) { }
                    try { _am.ClientEvent_OnEntityRemove -= _onEntityRemove; }
                    catch (Exception ex) { Log.Error("Host: unhook entity removal " + ex); }
                    try { _am.ClientEvent_OnActorAdd -= _onActorAdd; } catch (Exception) { }
                    try { _am.ClientEvent_OnActorRemove -= _onActorRemove; } catch (Exception) { }
                }
                UnhookShrines();
                UnhookMonsters();
                ClearQueuedMonsterSync();
                ClearSunders();
                ClearNativeDeathHooks();
                _am = am;
                _spawnQueue.Clear();
                _regen.Clear();
                _nightmares.Clear();
                _roomHasVariant = false;
                if (am != null)
                {
                    am.ClientEvent_OnEntityAdd += _onEntityAdd;
                    am.ClientEvent_OnEntityRemove += _onEntityRemove;
                    am.ClientEvent_OnActorAdd += _onActorAdd;
                    am.ClientEvent_OnActorRemove += _onActorRemove;
                    ScanShrines();
                    foreach (var e in am.allEntities) OnEntityAdd(e);
                }
            }
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            if (zone != _zone)
            {
                if (_zone != null)
                {
                    try { _zone.ClientEvent_OnZoneLoaded -= _onZoneLoaded; } catch (Exception) { }
                    try { _zone.ClientEvent_OnRoomLoaded -= _onRoomLoaded; } catch (Exception) { }
                }
                _zone = zone;
                _roomHasVariant = false;
                ClearAssignedMechanismTransients();
                ClearZoneReactions();
                foreach (var rt in _runtimes.Values) rt.Powers.OnZoneLoaded(Time.time);
                if (zone != null)
                {
                    zone.ClientEvent_OnZoneLoaded += _onZoneLoaded;
                    zone.ClientEvent_OnRoomLoaded += _onRoomLoaded;
                }
            }
            var cem = NetworkedManagerBase<ClientEventManager>.softInstance;
            if (cem != _cem)
            {
                Unsubscribe();
                _cem = cem;
                if (cem != null)
                {
                    // Combat powers use synchronous native callbacks; client RPCs cannot retain source scopes.
                }
            }
        }

        private void Unsubscribe()
        {
            if (_cem == null) return;
            try
            {
                _cem.OnDeath -= _onDeath;
                _cem.OnTakeDamage -= _onTakeDamage;
                _cem.OnApplyElemental -= _onApplyElemental;
            }
            catch (Exception) { }
            _cem = null;
        }

        /// <summary>全キャラから補正を外し、登録を解除する（MODの再読み込み・終了時）。</summary>
        public void Detach()
        {
            ClearAssignedMechanismSession();
            if (NativeInstance == this) NativeInstance = null;
            ReleasePowerShields();
            ClearNewPowerZone();
            ClearZoneGimmicksV129();
            ClearSupportPowerStateV129();
            ClearNativeDeathHooks();
            ClearWaypointHeroes();
            DewPlayer.onGamePlayerAdded -= _onPressurePlayerAdded;
            DewPlayer.onGamePlayerRemoved -= _onPressurePlayerRemoved;
            _pressureDirty = true;
            foreach (var rt in _runtimes.Values)
            {
                RemoveBonuses(rt);
                Unhook(rt);
            }
            _runtimes.Clear();
            ReleaseCurrency();
            DetachGemSlots();
            _scanList.Clear();
            _scanPowers = Array.Empty<PowerRuntime>();
            _builds.Clear();
            _incomingBuilds.Clear();
            ClearBuildValidationPeers();
            _pressure = DreamPressure.Neutral;
            _pressurePlayerCount = -1;
            _pressureBuilds.Clear();
            _pressurePlayers.Clear();
            _departedPlayers.Clear();
            ReleasePactCurses();
            Unsubscribe();
            UnhookShrines();
            ClearRemoteMonsterState();
            UnhookMonsters();
            ResetKillReplayConnections(true);
            ClearSunders();
            if (_zone != null)
            {
                try { _zone.ClientEvent_OnZoneLoaded -= _onZoneLoaded; } catch (Exception) { }
                try { _zone.ClientEvent_OnRoomLoaded -= _onRoomLoaded; } catch (Exception) { }
                _zone = null;
            }
            _triggeredPowers.Clear();
            _spreadingFire = false;
            _triggeredAffixes.Clear();
            _roomHasVariant = false;
            _loggedVariantSpawn = _loggedDeathBurst = false;
            _deathBurstHeroes.Clear();
            _reflectingDamage = false;
            _lucidTypes = null;
            if (_am != null)
            {
                try { _am.ClientEvent_OnEntityAdd -= _onEntityAdd; } catch (Exception) { }
                try { _am.ClientEvent_OnEntityRemove -= _onEntityRemove; }
                catch (Exception ex) { Log.Error("Host: unhook entity removal " + ex); }
                try { _am.ClientEvent_OnActorAdd -= _onActorAdd; } catch (Exception) { }
                try { _am.ClientEvent_OnActorRemove -= _onActorRemove; } catch (Exception) { }
                _am = null;
            }
            _spawnQueue.Clear();
            _regen.Clear();
            _nightmares.Clear();
            if (_registeredOn != null)
            {
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeBuildMsg>(_onBuild); } catch (Exception) { }
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeCurseMsg>(_onCurse); } catch (Exception) { }
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeCurseClearMsg>(_onCurseClear); } catch (Exception) { }
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeTradeMsg>(_onTrade); } catch (Exception) { }
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeDreamEventStartedMsg>(_onPersonalDreamEvent); } catch (Exception) { }
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeKillReceiptMsg>(OnKillReceipt); } catch (Exception) { }
                UnregisterHello(_registeredOn);
                _registeredOn = null;
            }
        }

        private void OnBuild(DreamforgeBuildMsg msg, DewPlayer caller)
        {
            ReceiveBuildUpdate(msg, caller);
        }

        private void SendApplied(DewPlayer caller, Hero hero, ReceivedBuild build)
        {
            foreach (var part in BuildTransfer.Split(build.Summary))
                _registeredOn?.CustomRpc_SendMessageToClient(caller,
                    DreamforgeAppliedMsg.FromPart(part, hero != null ? hero.netId : 0));
        }

        /// <summary>悪夢の契約の代償：送ってきたプレイヤーのキャラへ、本体の呪いをランダムに1つ付ける（Hatred の祭壇と同じもの）。</summary>

        private List<CurseStatusEffect> _curseCache;

        // 潜行の契約で付けた呪い（プレイヤーごと）。契約が解けたら（確保・遠征の終わり）まとめて消す。
        private readonly OwnedEffectRegistry<DewPlayer, StatusEffect> _pactCurses = new OwnedEffectRegistry<DewPlayer, StatusEffect>();

        private void OnCurse(DreamforgeCurseMsg msg, DewPlayer caller)
        {
            try
            {
                if (caller == null || msg == null || msg.protocol != Protocol.Version) return;
                var hero = caller.hero;
                if (hero == null || !hero.isActive) return;
                // Resync (ordinal > 0) is idempotent per player: skip if enough live pact curses already exist.
                int ordinal = PactCurseSync.Ordinal(msg.strength);
                if (!PactCurseSync.ShouldApply(ordinal, _pactCurses.CountLive(caller, se => se != null && !se.isDestroyed && se.isActive))) return;
                int rawStrength = PactCurseSync.Strength(msg.strength);
                var strength = rawStrength >= 3 ? HatredStrengthType.Powerful : rawStrength == 2 ? HatredStrengthType.Potent : HatredStrengthType.Mild;
                if (_curseCache == null)
                {
                    _curseCache = new List<CurseStatusEffect>();
                    foreach (var c in DewResources.FindAllByType<CurseStatusEffect>())
                        if (c != null) _curseCache.Add(c);
                }
                var pool = new List<CurseStatusEffect>();
                foreach (var c in _curseCache)
                {
                    if (c == null || (c.availableStrengths & strength) == 0) continue;
                    if (hero.Status.HasStatusEffect(c.GetType())) continue;
                    try { if (!c.IsViable(hero)) continue; } catch (Exception) { continue; }
                    pool.Add(c);
                }
                if (pool.Count == 0)
                {
                    Log.Warn("Curse: no viable curse for strength " + strength);
                    return;
                }
                float total = 0;
                foreach (var c in pool) total += Math.Max(0.01f, c.chanceWeight);
                float x = (float)_rng.NextDouble() * total;
                var pick = pool[pool.Count - 1];
                foreach (var c in pool)
                {
                    x -= Math.Max(0.01f, c.chanceWeight);
                    if (x <= 0) { pick = c; break; }
                }
                var effect = hero.CreateStatusEffect(pick.GetType(), hero, new CastInfo(hero), se =>
                {
                    if (se is CurseStatusEffect curse) curse.currentStrength = strength;
                });
                if (effect != null) _pactCurses.Add(caller, effect);
                Log.Info($"Curse: {pick.GetType().Name} ({strength}) on {caller.playerName}");
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnCurse " + ex);
            }
        }

        /// <summary>契約が解けたので、その契約で付けた呪いを送ってきたプレイヤーのキャラから消す。</summary>
        private void OnCurseClear(DreamforgeCurseClearMsg msg, DewPlayer caller)
        {
            try
            {
                if (caller == null || msg == null || msg.protocol != Protocol.Version) return;
                // 既に消えているもの（ゲームの終了・部屋の切り替え・呪いの解除など）は数えない。
                int cleared = _pactCurses.Release(caller, se => se != null && !se.isDestroyed && se.isActive, se => se.Destroy(),
                    ex => Log.Error("Host: clear pact curse " + ex));
                if (cleared > 0) Log.Info($"pact curses cleared: {cleared}");
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnCurseClear " + ex);
            }
        }

        private readonly Dictionary<Hero, float> _applyRetryAt = new Dictionary<Hero, float>();

        private void PruneAndApplyPending()
        {
            _scratch.Clear();
            foreach (var kv in _runtimes)
                if (kv.Key == null || !kv.Key.isActive) _scratch.Add(kv.Key);
            foreach (var h in _scratch)
            {
                try { Unhook(_runtimes[h]); }
                catch (Exception ex) { Log.Error("Host: unhook " + ex); }
                _runtimes.Remove(h);
            }

            // 新しく生まれたキャラ（ラン開始・復帰）へ、届いている Build を付け直す。
            // 1人の Build で例外が出ても、ほかの全員のホスト処理は止めない（mp-ui-save #6）。失敗した人は5秒おきに再試行する。
            float now = Time.time;
            foreach (var kv in _builds)
            {
                var hero = kv.Key != null ? kv.Key.hero : null;
                if (hero == null || !hero.isActive || _runtimes.ContainsKey(hero)) continue;
                if (_applyRetryAt.TryGetValue(hero, out float retry) && now < retry) continue;
                try
                {
                    ApplyValidatedBuild(kv.Key, hero, kv.Value);
                    _applyRetryAt.Remove(hero);
                }
                catch (Exception ex)
                {
                    _applyRetryAt[hero] = now + 5f;
                    if (_runtimes.TryGetValue(hero, out var partial))
                    {
                        try { Unhook(partial); } catch (Exception) { }
                        _runtimes.Remove(hero);
                    }
                    Log.Error("Host: could not apply a player's build (retrying in 5 s): " + ex);
                }
            }
            _scratch.Clear();
            foreach (var h in _applyRetryAt.Keys) if (h == null || !h.isActive) _scratch.Add(h);
            foreach (var h in _scratch) _applyRetryAt.Remove(h);
            foreach (var rt in _runtimes.Values) BindGoldSpend(rt);
        }

        private void Apply(Hero hero, ReceivedBuild received)
        {
            var build = received.Build;
            ValidateAuthoredHostBuild(build);
            ValidateAuthoredSacrificeBinding(hero, build);
            if (!_runtimes.TryGetValue(hero, out var rt))
            {
                rt = new HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, Time.time, hero.netId + 1UL) };
                RefreshMemoryAttributionEquipment(hero);
                var captured = rt;
                rt.OnFired = info => OnAttackFired(captured, info);
                rt.OnSkill = info => OnSkillUse(captured, info);
                rt.OnHit = _onAttackHit;
                hero.EntityEvent_OnAttackFired += rt.OnFired;
                // Actor.DoBasicAttackHit は、この同期イベントの後で DealDamage を呼ぶ。
                // RPC 通知の配信時点に依存せず、命中前の HP で判定する。
                hero.EntityEvent_OnAttackHit += rt.OnHit;
                // NativeMemoryUse dispatches authoritative casts, including allies without a build.
                rt.OnTeleport = (from, to) =>
                {
                    captured.NewPowers.PositionKnown = false;
                    captured.NewPowers.SpecialMovementUntil = Time.time + .1f;
                    OnHeroTeleport(captured);
                    OnIdentityStrikeDisplacement(captured.Hero);
                };
                rt.OnDisplacement = disp =>
                {
                    captured.NewPowers.SpecialMovementUntil = Time.time + .1f;
                    // Displacement has no source: the game's own Husk crit effects use isFriendly.
                    if (disp != null && disp.isFriendly) { OnHeroSelfMovement(captured); OnIdentityStrikeDisplacement(captured.Hero); }
                };
                hero.Control.ClientEvent_OnTeleport += rt.OnTeleport;
                hero.Control.ClientEvent_OnDisplacementStarted += rt.OnDisplacement;
                rt.DamageTaken = (ref DamageData d, Actor a, Entity t) =>
                {
                    if (a != null && a.firstEntity != null && a.firstEntity.GetRelation(captured.Hero) == EntityRelation.Enemy)
                        EnsureWaypointCombatChoice();
                    float reduction = captured.Powers.IncomingDamageReduction(Time.time);
                    if (reduction > 0f) d.ApplyReduction(reduction);
                    float mult = captured.Powers.Build.DamageTakenMultiplier;
                    if (mult > 1f) d.ApplyAmplification(mult - 1f);
                    // These verified sources dispatch HP sacrifice as self-damage, not enemy attacks.
                    if (t == captured.Hero && IsHealthSacrifice(d.actor, captured.Hero))
                        d = d.ApplyRawMultiplier(SupportStats.ReduceSacrifice(1f,
                            captured.Powers.Build.Get(Stat.SacrificeReduction)));
                };
                hero.takenDamageProcessor.Add(rt.DamageTaken);
                InitializeNewPowers(rt);
                InitializeGimmicksV129(rt);
                rt.DamageDealt = (ref DamageData d, Actor a, Entity t) =>
                {
                    if (!Alive(captured.Hero) || t == null || !t.isActive || t.Status == null
                        || t.GetRelation(captured.Hero) != EntityRelation.Enemy) return;
                    if (!d.IsAmountModifiedBy(typeof(GimmickRuntime))) EnsureWaypointCombatChoice();
                    var status = t.Status;
                    float amp = captured.Powers.FettersAmplification(status.hasStun || status.hasSlow || status.hasCold);
                    if (amp > 0)
                    {
                        d.ApplyAmplification(amp);
                        LogPowerTrigger(Power.Fetters);
                    }
                    if (_gimmickDamageDepth != 0 || d.IsAmountModifiedBy(typeof(GimmickRuntime))) return;
                    ApplyMemoryPacketCorrections(captured, ref d, t, MemorySource(d.actor ?? a));
                    ApplyExposeDamage(captured, ref d, t, BridgeSuccessExposePercent(captured.Hero, t));
                };
                hero.dealtDamageProcessor.Add(rt.DamageDealt);
                rt.OnMemoryDamage = info => OnMemoryDamage(captured, info);
                rt.OnMemoryKill = info => OnMemoryKill(captured, info);
                hero.ActorEvent_OnDealDamage += rt.OnMemoryDamage;
                hero.ActorEvent_OnKill += rt.OnMemoryKill;
                rt.OnSupportHeal = info =>
                {
                    OnSupportHealingV129(captured, info);
                    var target = info.target as Hero;
                    if (target != null && target != hero && target.GetRelation(hero) == EntityRelation.Ally
                        && info.amount > 0f && !float.IsNaN(info.amount) && !float.IsInfinity(info.amount))
                        SendBountyReport(captured, BountyReportKind.AllyHealed, 1);
                };
                rt.OnSupportShield = info =>
                {
                    var target = info.target;
                    if (target != null && (target == hero || target.GetRelation(hero) == EntityRelation.Ally)
                        && info.finalAmount > 0f && !float.IsNaN(info.finalAmount) && !float.IsInfinity(info.finalAmount))
                        SendBountyReport(captured, BountyReportKind.ShieldGranted, 1);
                };
                hero.ActorEvent_OnDoHeal += rt.OnSupportHeal;
                hero.ActorEvent_OnGiveShield += rt.OnSupportShield;
                // Actor walks its ancestor processors, including for Heal().Dispatch and GiveShield.
                // Mod-created recovery therefore uses these hooks too; do not multiply at call sites.
                rt.HealDealt = (ref HealData heal, Actor a, Entity t) =>
                    heal.ApplyAmplification(SupportStats.AmplifyHeal(1f, captured.Powers.Build.Get(Stat.HealPower) + GrowthSupport(captured, Stat.HealPower)) - 1f);
                rt.ShieldDealt = (ref HealData shield, Actor a, Entity t) =>
                    shield.ApplyAmplification(SupportStats.AmplifyShield(1f, captured.Powers.Build.Get(Stat.ShieldPower) + GrowthSupport(captured, Stat.ShieldPower)) - 1f);
                hero.dealtHealProcessor.Add(rt.HealDealt);
                hero.dealtShieldProcessor.Add(rt.ShieldDealt);
                rt.OnSummon = info => HookSummon(captured, info.summon);
                hero.ActorEvent_OnSpawnSummon += rt.OnSummon;
                rt.OnHeal = info => OnHealTaken(captured, info);
                hero.EntityEvent_OnTakeHeal += rt.OnHeal;
                rt.OnImmunity = info => OnDamageNegated(captured, info);
                hero.EntityEvent_OnDamageNegatedByImmunity += rt.OnImmunity;
                rt.OnAbilityCreated = info => OnRunGrowthAbility(captured, info);
                hero.ActorEvent_OnAbilityInstanceCreated += rt.OnAbilityCreated;
                _runtimes[hero] = rt;
            }
            RemoveBonuses(rt);
            // 連携の判定結果は Build と装着に紐付くので、付け直すときに一旦空にする。
            rt.HeroKey = hero.GetType().Name;
            rt.SatisfiedLinks.Clear();
            rt.LinkMemories.Clear();
            rt.LinkEssences.Clear();
            rt.Powers.SetBuild(build);
            rt.PairCombos.SetBuild(build.PairCombos);
            rt.GeneratedKillVictims.Clear();
            rt.PendingGimmicks.Clear();
            ModShieldEquipmentEpoch(rt);
            rt.BaseBonus = ToStatBonus(build);
            rt.DynBonus = new StatBonus();
            hero.Status.AddStatBonus(rt.BaseBonus);
            hero.Status.AddStatBonus(rt.DynBonus);
            rt.AppliedBuild = received;
            ConfigureAuthoredKeystone(hero, build);
            ConfigureAuthoredMechanisms(hero, build);
            BindGoldSpend(rt);
            ApplyGemSlots(rt, build);
            // Builds can arrive after a persistent summon (for example Fenrir) has spawned.
            if (_am != null)
                foreach (var entity in _am.allEntities)
                    if (entity is Summon summon) HookSummon(rt, summon);
            BossEnsure(rt);
        }

        private static bool IsHealthSacrifice(Actor source, Hero hero)
        {
            if (source is Se_HealthCost cost) return cost.victim == hero;
            if (source is Ai_Q_GoldenBurst burst) return burst.info.caster == hero;
            if (source is Ai_Q_Reduction_Spawner reduction) return reduction.info.caster == hero;
            return false;
        }

        private void HookSummon(HeroRuntime rt, Summon summon)
        {
            if (summon == null || !summon.isActive
                || summon.FindFirstAncestorOfType<Hero>() != rt.Hero || rt.Summons.ContainsKey(summon)) return;
            DataProcessor<DamageData, Actor, Entity> processor = (ref DamageData damage, Actor source, Entity target) =>
            {
                // Only the nearest summon handles a hit: child summons also inherit ancestor processors.
                if (source == null || source.FindFirstOfType<Summon>() != summon
                    || summon.FindFirstAncestorOfType<Hero>() != rt.Hero) return;
                damage.ApplyAmplification(SupportStats.AmplifySummonDamage(1f,
                    rt.Powers.Build.Get(Stat.SummonPower) + GrowthSupport(rt, Stat.SummonPower)) - 1f);
                damage.ApplyAmplification((float)ActiveWaypointTotals.SummonPowerMultiplier - 1f);
                if (_gimmickDamageDepth == 0 && !damage.IsAmountModifiedBy(typeof(GimmickRuntime)))
                    damage.ApplyAmplification(rt.Powers.OutgoingDamageAmplification(Time.time, false, true));
            };
            rt.Summons.Add(summon, processor);
            HookSupportSummonV129(summon);
            summon.dealtDamageProcessor.Add(processor);
        }

        private void Unhook(HeroRuntime rt)
        {
            ClearBossEffects(rt);
            ForgetAssignedMechanismOwner(rt.Hero);
            RestoreGemSlots(rt);
            UnhookNewPowers(rt);
            UnhookGimmicksV129(rt);
            UnhookGoldSpend(rt);
            foreach (var summon in rt.Summons)
                if (summon.Key != null)
                {
                    summon.Key.dealtDamageProcessor.Remove(summon.Value);
                    UnhookSupportSummonV129(summon.Key);
                }
            rt.Summons.Clear();
            var hero = rt.Hero;
            if (hero == null) return;
            try
            {
                if (rt.OnImmunity != null) hero.EntityEvent_OnDamageNegatedByImmunity -= rt.OnImmunity;
                if (rt.OnAbilityCreated != null) hero.ActorEvent_OnAbilityInstanceCreated -= rt.OnAbilityCreated;
            }
            catch (Exception ex) { Log.Error("Host: unhook immunity " + ex); }
            try
            {
                if (rt.OnFired != null) hero.EntityEvent_OnAttackFired -= rt.OnFired;
                if (rt.OnHit != null) hero.EntityEvent_OnAttackHit -= rt.OnHit;
                if (rt.OnMemoryDamage != null) hero.ActorEvent_OnDealDamage -= rt.OnMemoryDamage;
                if (rt.OnMemoryKill != null) hero.ActorEvent_OnKill -= rt.OnMemoryKill;
                if (rt.OnSupportHeal != null) hero.ActorEvent_OnDoHeal -= rt.OnSupportHeal;
                if (rt.OnSupportShield != null) hero.ActorEvent_OnGiveShield -= rt.OnSupportShield;
                if (rt.OnSkill != null) hero.ClientHeroEvent_OnSkillUse -= rt.OnSkill;
                if (rt.OnTeleport != null) hero.Control.ClientEvent_OnTeleport -= rt.OnTeleport;
                if (rt.OnDisplacement != null) hero.Control.ClientEvent_OnDisplacementStarted -= rt.OnDisplacement;
                if (rt.DamageTaken != null) hero.takenDamageProcessor.Remove(rt.DamageTaken);
                if (rt.DamageDealt != null) hero.dealtDamageProcessor.Remove(rt.DamageDealt);
                if (rt.HealDealt != null) hero.dealtHealProcessor.Remove(rt.HealDealt);
                if (rt.ShieldDealt != null) hero.dealtShieldProcessor.Remove(rt.ShieldDealt);
                if (rt.OnSummon != null) hero.ActorEvent_OnSpawnSummon -= rt.OnSummon;
                if (rt.OnHeal != null) hero.EntityEvent_OnTakeHeal -= rt.OnHeal;
            }
            catch (Exception ex) { Log.Error("Host: Unhook hero " + ex); }
            rt.OnFired = null;
            rt.OnHit = null;
            rt.OnSkill = null;
            rt.OnTeleport = null;
            rt.OnDisplacement = null;
            rt.DamageTaken = null;
            rt.DamageDealt = null;
            rt.OnHeal = null;
            rt.OnImmunity = null;
            rt.OnAbilityCreated = null;
            rt.CritBasicVictims.Clear();
            rt.HealDealt = null;
            rt.ShieldDealt = null;
            rt.OnSummon = null;
            rt.OnMemoryDamage = null;
            rt.OnMemoryKill = null;
            rt.OnSupportHeal = null;
            rt.OnSupportShield = null;
            rt.PendingGimmicks.Clear();
            rt.PendingReactions.Clear();
            rt.ReactionVictims.Clear();
            rt.Reactions.Clear();
        }

        private void BindGoldSpend(HeroRuntime rt)
        {
            try
            {
                var player = rt.Hero != null ? rt.Hero.owner : null;
                if (player != null && player.hero != rt.Hero) player = null;
                if (rt.Player == player) return;
                UnhookGoldSpend(rt);
                if (player == null) return;
                rt.Player = player;
                rt.OnSpendGold = amount => OnGoldSpent(player, amount);
                player.ClientEvent_OnSpendGold += rt.OnSpendGold;
            }
            catch (Exception ex) { Log.Error("Host: bind gold spending " + ex); }
        }

        private static void UnhookGoldSpend(HeroRuntime rt)
        {
            try
            {
                if (rt.Player != null && rt.OnSpendGold != null)
                    rt.Player.ClientEvent_OnSpendGold -= rt.OnSpendGold;
            }
            catch (Exception ex) { Log.Error("Host: unhook gold spending " + ex); }
            rt.Player = null;
            rt.OnSpendGold = null;
        }

        private void OnGoldSpent(DewPlayer player, int amount)
        {
            try
            {
                var hero = player != null ? player.hero : null;
                if (!Alive(hero) || !_runtimes.TryGetValue(hero, out var rt)) return;
                float shield = rt.Powers.TakeSpendersWard(Time.time, amount, hero.maxHealth);
                if (shield <= 0) return;
                hero.GiveShield(hero, shield, PowerRuntime.SpendersWardDuration, false, default(ReactionChain));
                LogPowerTrigger(Power.SpendersWard);
            }
            catch (Exception ex) { Log.Error("Host: SpendersWard " + ex); }
        }

        private void OnDamageNegated(HeroRuntime rt, EventInfoDamageNegatedByImmunity info)
        {
            try
            {
                if (!Alive(rt.Hero) || info.victim != rt.Hero || info.data.amount <= 0) return;
                bool invulnerable = info.effect != null && (info.effect.mask & BasicEffectMask.Invulnerable) != 0;
                if (!rt.Powers.TakePerfectRead(Time.time, invulnerable)) return;
                UpdateRuntime(rt, Time.time);
                LogPowerTrigger(Power.PerfectRead);
            }
            catch (Exception ex) { Log.Error("Host: PerfectRead " + ex); }
        }

        private int ReadEvilDreamCount()
        {
            try
            {
                var settings = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (settings != null)
                {
                    if (settings.activeLucidDreams.Count == 0) return 0;
                    if (_lucidTypes == null)
                    {
                        var types = new Dictionary<string, LucidDreamType>(StringComparer.Ordinal);
                        foreach (var dream in DewResources.FindAllByType<LucidDream>())
                        {
                            if (dream == null) continue;
                            var type = dream.GetType();
                            types[type.Name] = dream.type;
                            if (type.FullName != null) types[type.FullName] = dream.type;
                        }
                        _lucidTypes = types;
                    }
                    int evil = 0;
                    bool resolved = true;
                    foreach (string id in settings.activeLucidDreams)
                    {
                        if (id == null || !_lucidTypes.TryGetValue(id, out var type)) { resolved = false; break; }
                        if (type == LucidDreamType.Evil) evil++;
                    }
                    if (resolved) return Math.Min(PowerRuntime.LucidBoonMaxDreams, evil);
                }
            }
            catch (Exception ex) { Log.Error("Host: resolve Evil lucid dreams " + ex); }
            // The dumps do not guarantee the string identifier format; never guess an unresolved dream's type.
            var limbo = GameMod_Limbo.softInstance;
            return limbo != null ? Math.Min(PowerRuntime.LucidBoonMaxDreams, Math.Max(0, limbo.depth)) : 0;
        }

        private void OnHeroSelfMovement(HeroRuntime rt)
        {
            if (!NetworkServer.active || !Alive(rt.Hero)) return;
            var zone = NetworkedManagerBase<ZoneManager>.softInstance;
            var transition = ManagerBase<TransitionManager>.instance;
            if (zone == null || zone.isInAnyTransition
                || transition == null || transition.state == TransitionManager.StateType.Loading) return;
            rt.Powers.OnSelfMovement(Time.time);
        }

        private void OnHeroTeleport(HeroRuntime rt)
        {
            if (!Alive(rt.Hero)) return;
            // DispByTarget can teleport to resolve an unreachable target before clearing itself.
            // Do not turn an enemy pull's final correction into a voluntary movement trigger.
            var displacement = rt.Hero.Control.ongoingDisplacement;
            // Position-only events cannot establish who requested a standalone teleport.
            // Actor.Teleport retains its caster for this synchronous callback; unowned warps fail closed.
            if (TeleportInitiator.Current != null)
            {
                if (!TeleportInitiator.IsSelf(rt.Hero)) return;
            }
            else if (displacement == null || !displacement.isFriendly) return;
            OnHeroSelfMovement(rt);
        }

        /// <summary>回避・Memory・Ultimate の固有効果。</summary>
        private void OnSkillUse(HeroRuntime rt, EventInfoSkillUse info)
        {
            try
            {
                var hero = rt.Hero;
                if (!Alive(hero)) return;
                if (_gimmickDamageDepth == 0 && info.skill != null && Links.IsMemory(info.skill.GetType().Name))
                {
                    int memorySlot = info.type == HeroSkillLocation.Q ? 0 : info.type == HeroSkillLocation.W ? 1
                        : info.type == HeroSkillLocation.E ? 2 : info.type == HeroSkillLocation.R ? 3 : -1;
                    if (memorySlot >= 0) SendBountyReport(rt, BountyReportKind.MemoryUsed, memorySlot);
                }
                ApplyWaypointMemoryCooldown(rt.Hero, info);
                if (_gimmickDamageDepth == 0 && info.type != HeroSkillLocation.Movement && info.skill != null)
                    QueueGimmicks(rt, GimmickTrigger.OnUse, info.skill.GetType().Name, null, 0f);
                var r = rt.Powers.OnSkillUsed(Time.time, info.type == HeroSkillLocation.Movement, info.skill != null && info.skill.type == SkillType.Ultimate,
                    Math.Max(hero.Status.attackDamage, hero.Status.abilityPower), hero.maxHealth,
                    info.type == HeroSkillLocation.Identity);
                if (r.Shield > 0) hero.GiveShield(hero, r.Shield, PowerRuntime.StarShieldDuration);
                if (r.WhirlwindDamage > 0)
                {
                    _pairDamageDepth++;
                    try { DamageAround(hero, hero.agentPosition, PowerRuntime.WhirlwindRadius, r.WhirlwindDamage, null, int.MaxValue, magic: hero.Status.abilityPower > hero.Status.attackDamage); }
                    finally { _pairDamageDepth--; }
                }
                if (hero.Skill == null) return;
                int slot = info.type == HeroSkillLocation.Q ? 0 : info.type == HeroSkillLocation.W ? 1
                    : info.type == HeroSkillLocation.E ? 2 : -1;
                float finale = rt.Powers.TakeFinale(Time.time, slot);
                if (finale > 0)
                {
                    var ultimate = hero.Skill.GetSkill(HeroSkillLocation.R);
                    if (ultimate != null)
                    {
                        ReduceNormalMemory(hero, ultimate, finale * 100f);
                        LogPowerTrigger(Power.Finale);
                    }
                }
                // 連携（v1.26）：使った記憶が条件に入っている連携だけを発動する。
                UpdateLinks(rt, true);
                if (info.skill != null)
                {
                    string used = info.skill.GetType().Name;
                    float haste = Math.Min(100f, PowerRuntime.LinkHastePercent(rt.SatisfiedLinks, used)
                        + FractionalScopedModifiers.NativePercent(rt.AppliedBuild?.Build.NativeModifiers, used, LinkKind.MemoryHaste));
                    if (haste > 0) hero.ApplyCooldownReductionByRatio(info.skill, haste / 100f, false);
                    foreach (var link in rt.SatisfiedLinks)
                    {
                        if (link.Kind != LinkKind.MemoryHaste && link.Kind != LinkKind.MemorySurge) continue;
                        if (Array.IndexOf(link.Requires, used) < 0) continue;
                        if (link.Kind == LinkKind.MemorySurge) rt.Powers.OnLinkSurge(Time.time, link);
                        LogLinkApplied(link);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnSkillUse " + ex.Message);
            }
        }

        private static bool ReduceMemoryCooldowns(Hero hero, float amount)
        {
            if (amount <= 0 || hero.Skill == null) return false;
            bool applied = false;
            foreach (var loc in CooldownSkills)
            {
                var skill = hero.Skill.GetSkill(loc);
                if (skill == null) continue;
                hero.ApplyCooldownReduction(skill, amount);
                applied = true;
            }
            return applied;
        }

        private void OnHealTaken(HeroRuntime rt, EventInfoHeal info)
        {
            try
            {
                var hero = rt.Hero;
                if (!Alive(hero) || info.target != hero) return;
                OnPotionHealV129(rt, info);
                rt.Powers.OnOverheal(Time.time, info.discardedAmount);
                float shield = rt.Powers.TakeOverflowingLife(info.discardedAmount, hero.maxHealth);
                if (shield <= 0) return;
                hero.GiveShield(hero, shield, PowerRuntime.OverflowingLifeDuration);
                LogPowerTrigger(Power.OverflowingLife);
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnHealTaken " + ex.Message);
            }
        }

        private static void RemoveBonuses(HeroRuntime rt)
        {
            var hero = rt.Hero;
            if (hero == null || hero.Status == null) return;
            try
            {
                if (rt.BaseBonus != null) hero.Status.RemoveStatBonus(rt.BaseBonus);
                if (rt.DynBonus != null) hero.Status.RemoveStatBonus(rt.DynBonus);
                if (rt.GrowthBonus != null) hero.Status.RemoveStatBonus(rt.GrowthBonus);
            }
            catch (Exception) { }
            rt.BaseBonus = null;
            rt.DynBonus = null;
            rt.GrowthBonus = null;
            rt.GrowthTotals.Clear();
            rt.GrowthVersion = -1;
            rt.GrowthBuild = null;
            rt.GrowthSentVersion = -1;
        }

        internal static StatBonus ToStatBonus(Build b)
        {
            var s = new StatBonus();
            foreach (var kv in b.Stats) AddNativeStat(s, kv.Key, StatUnits.ToGame(kv.Key, kv.Value));
            return s;
        }

        /// <summary>1つの能力値を本体の StatBonus へ足す（ゲームの単位）。ネイティブに入らない能力値は何もしない。</summary>
        internal static void AddNativeStat(StatBonus s, Stat stat, float v)
        {
            switch (stat)
            {
                case Stat.AttackPct: s.attackDamagePercentage += v; break;
                case Stat.PowerPct: s.abilityPowerPercentage += v; break;
                case Stat.AttackSpeedPct: s.attackSpeedPercentage += v; break;
                case Stat.CritChancePct: s.critChanceFlat += v; break;
                case Stat.CritDamagePct: s.critAmpFlat += v; break;
                case Stat.MaxHealthPct: s.maxHealthPercentage += v; break;
                case Stat.MaxHealthFlat: s.maxHealthFlat += v; break;
                case Stat.AttackFlat: s.attackDamageFlat += v; break;
                case Stat.PowerFlat: s.abilityPowerFlat += v; break;
                case Stat.Armor: s.armorFlat += v; break;
                case Stat.HealthRegen: s.healthRegenFlat += v; break;
                case Stat.Haste: s.abilityHasteFlat += v; break;
                case Stat.MoveSpeedPct: s.movementSpeedPercentage += v; break;
                case Stat.Tenacity: s.tenacityFlat += v; break;
                case Stat.FireAmp: s.fireEffectAmpFlat += v; break;
                case Stat.ColdAmp: s.coldEffectAmpFlat += v; break;
                case Stat.LightAmp: s.lightEffectAmpFlat += v; break;
                case Stat.DarkAmp: s.darkEffectAmpFlat += v; break;
                case Stat.AttackRangePct: s.attackRangePercentage += v; break;
                case Stat.FourthAttackShift: s.everyFourAttackStartIndexFlat += (int)v; break;
                // エッセンス枠（v1.27）は能力補正ではなく ApplyGemSlots が枠の数として扱う。
                case Stat.EssenceSlotIdentity:
                case Stat.EssenceSlotMovement:
                // Support stats are handled by combat processors, not native StatBonus fields.
                case Stat.HealPower:
                case Stat.ShieldPower:
                case Stat.SummonPower:
                case Stat.SacrificeReduction:
                    break;
            }
        }

        private static bool Alive(Hero h) => h != null && h.isActive && !h.isKnockedOut;

        private void UpdateRuntime(HeroRuntime rt, float now)
        {
            var hero = rt.Hero;
            if (!Alive(hero) || rt.DynBonus == null) return;
            UpdateNewPowerFacts(rt, now);
            var p = rt.Powers;
            p.HealthRatio = hero.maxHealth > 0 ? hero.currentHealth / hero.maxHealth : 1f;
            var dyn = p.Current(now);
            dyn.AttackSpeedPct += rt.Gimmicks.QuickenPercent(now);
            float empower = rt.Gimmicks.EmpowerPercent(now);
            dyn.AttackPct += empower;
            dyn.PowerPct += empower;
            // Kill() follows the synchronous hit event; do not retain old victims between frames.
            rt.MemoryHitAmounts.Clear();
            rt.GeneratedKillVictims.Clear();
            rt.CritBasicVictims.Clear();
            var d = rt.DynBonus;
            // 値が変わったときだけ能力を再計算する（StatBonus は同じ値の代入では汚れない）。
            if (d.attackSpeedPercentage != dyn.AttackSpeedPct || d.attackDamagePercentage != dyn.AttackPct || d.abilityPowerPercentage != dyn.PowerPct
                || d.movementSpeedPercentage != dyn.MoveSpeedPct || d.maxHealthPercentage != dyn.MaxHealthPct || d.armorFlat != dyn.Armor)
            {
                d.attackSpeedPercentage = dyn.AttackSpeedPct;
                d.attackDamagePercentage = dyn.AttackPct;
                d.abilityPowerPercentage = dyn.PowerPct;
                d.movementSpeedPercentage = dyn.MoveSpeedPct;
                d.maxHealthPercentage = dyn.MaxHealthPct;
                d.armorFlat = dyn.Armor;
                hero.Status.CalculateStatsIfDirty();
            }

            float shield = p.TakeBarrier(now, hero.maxHealth);
            if (shield > 0) hero.GiveShield(hero, shield, PowerRuntime.BarrierDuration);
            float heal = p.TakeSecondWind(now, hero.currentHealth, hero.maxHealth);
            if (heal > 0) hero.Heal(heal).Dispatch(hero);
        }

        /// <summary>鉄の輪・乱戦の敵数と、共鳴の距離判定（0.25秒ごと）。</summary>
        private readonly List<HeroRuntime> _scanList = new List<HeroRuntime>();

        private void ScanArea()
        {
            var list = _scanList;
            list.Clear();
            foreach (var rt in _runtimes.Values) list.Add(rt);
            if (list.Count == 0)
            {
                _scanPowers = Array.Empty<PowerRuntime>();
                return;
            }
            int huntLevel = _zone != null ? _zone.currentHuntLevel : 0;
            bool needsLucidCount = false;
            foreach (var rt in list)
                if (rt.Powers.Build.Get(Power.LucidBoon) > 0) { needsLucidCount = true; break; }
            int evilDreams = needsLucidCount ? ReadEvilDreamCount() : 0;
            foreach (var rt in list)
            {
                var p = rt.Powers;
                var hero = rt.Hero;
                bool alive = Alive(hero);
                p.NearbyEnemies = (p.Build.Get(Power.Bulwark) > 0 || p.Build.Get(Power.Frenzy) > 0)
                    && alive ? CountEnemiesNear(hero, 6f) : 0;
                long quality = 0;
                if (p.Build.Get(Power.CrystalResonance) > 0 && alive && hero.Skill != null)
                {
                    foreach (var kv in hero.Skill.gems)
                        if (kv.Value != null) quality += Math.Max(0, kv.Value.quality);
                }
                p.GemQualityTotal = (int)Math.Min(int.MaxValue, quality);
                p.HuntLevel = huntLevel;
                p.EvilDreamCount = evilDreams;
                if (alive && p.Build.Get(Power.LucidBoon) > 0 && evilDreams > 0)
                    LogPowerTrigger(Power.LucidBoon);
                if (alive && p.Build.Get(Power.CrystalResonance) > 0 && p.GemQualityTotal >= 100)
                    LogPowerTrigger(Power.CrystalResonance);
                if (alive && p.Build.Get(Power.PreyPride) > 0 && huntLevel > 0)
                    LogPowerTrigger(Power.PreyPride);
                UpdateLinks(rt, alive);
                ScanNewPowerFacts(rt, Time.time);
            }

            // DistributeResonance は配列全体を使うため、人数が変わったときだけ長さを合わせる。
            if (_scanPowers.Length != list.Count) _scanPowers = new PowerRuntime[list.Count];
            for (int i = 0; i < list.Count; i++) _scanPowers[i] = list[i].Powers;
            PowerRuntime.DistributeResonance(_scanPowers, _resonanceNear);
            // MOD未導入の味方が近くにいても、自分の共鳴は全量にする。
            foreach (var rt in list)
            {
                int v = rt.Powers.Build.Get(Power.Resonance);
                if (v > 0 && rt.Powers.ResonanceSelf < v && AnyAllyNear(rt.Hero)) rt.Powers.ResonanceSelf = v;
            }
        }

        /// <summary>
        /// 連携（v1.26）：装っている記憶・エッセンスの型名と旅人の型名から、満たしている連携を選ぶ（0.25秒ごと）。
        /// 同調と守りはここで補正に足し、外れたら次の走査で消える。記憶を使う2種は OnSkillUse で使う。
        /// </summary>
        private void UpdateLinks(HeroRuntime rt, bool alive)
        {
            var runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (rt.BountyRunId != runId)
            {
                rt.BountyRunId = runId;
                rt.ReportedLinks = 0;
            }
            var p = rt.Powers;
            var links = p.Build.Links;
            if (links.Count == 0 && p.Build.Gimmicks.Count == 0)
            {
                if (rt.SatisfiedLinks.Count > 0)
                {
                    rt.SatisfiedLinks.Clear();
                    p.LinkAttunePct = 0;
                    p.LinkGuardHealthPct = 0;
                    p.LinkGuardArmor = 0;
                }
                p.RetainLinkSurges(rt.SatisfiedLinks, Time.time);
                return;
            }
            var hero = rt.Hero;
            var memories = rt.LinkMemories;
            var essences = rt.LinkEssences;
            var nearbyAllies = CollectNearbyBondHeroes(hero, alive);
            memories.Clear();
            essences.Clear();
            if (alive && hero.Skill != null)
            {
                foreach (var loc in LinkSkills)
                {
                    var skill = hero.Skill.GetSkill(loc);
                    if (skill != null) memories.Add(skill.GetType().Name);
                }
                foreach (var kv in hero.Skill.gems)
                    if (kv.Value != null) essences.Add(kv.Value.GetType().Name);
            }
            rt.SatisfiedLinks.Clear();
            long attuneMilli = 0, guardMilli = 0;
            foreach (var link in links)
            {
                if (!alive || !Links.Satisfied(link, rt.HeroKey, memories, essences, nearbyAllies)) continue;
                rt.SatisfiedLinks.Add(link);
                switch (link.Kind)
                {
                    case LinkKind.Attune:
                        attuneMilli += link.ValueMilli;
                        LogLinkApplied(link);
                        break;
                    case LinkKind.Guard:
                        guardMilli += link.ValueMilli;
                        LogLinkApplied(link);
                        break;
                }
            }
            p.RetainLinkSurges(rt.SatisfiedLinks, Time.time);
            p.LinkAttunePct = attuneMilli / (float)BuildPrecision.Scale;
            p.LinkGuardHealthPct = guardMilli / (float)BuildPrecision.Scale;
            p.LinkGuardArmor = guardMilli / (float)BuildPrecision.Scale;
            if (rt.SatisfiedLinks.Count > rt.ReportedLinks)
            {
                rt.ReportedLinks = rt.SatisfiedLinks.Count;
                SendBountyReport(rt, BountyReportKind.LinksSatisfied, rt.ReportedLinks);
            }
        }

        /// <summary>連携が最初に効いたときだけ、種類ごとに1行ログを残す。</summary>
        private void LogLinkApplied(LinkDef link)
        {
            if (_triggeredLinks.Add(link.Kind))
                Log.Info("[DreamforgeRPG] link " + link.Kind + " applied: " + string.Join("+", link.Requires));
        }

        private bool RuntimesNear(int i, int j) =>
            Alive(_scanList[i].Hero) && Alive(_scanList[j].Hero)
            && Vector3.Distance(_scanList[i].Hero.agentPosition, _scanList[j].Hero.agentPosition) <= PowerRuntime.ResonanceRange;

        private static int CountEnemiesNear(Hero hero, float radius)
        {
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, hero.agentPosition, radius,
                EnemyFilter, hero);
            try
            {
                return found.Count;
            }
            finally
            {
                handle.Return();
            }
        }

        private static bool AnyAllyNear(Hero hero)
        {
            var players = DewPlayer.allHumanPlayers;
            if (players == null) return false;
            foreach (var p in players)
            {
                var h = p != null ? p.hero : null;
                if (h == null || h == hero || !Alive(h)) continue;
                if (Vector3.Distance(h.agentPosition, hero.agentPosition) <= PowerRuntime.ResonanceRange) return true;
            }
            return false;
        }

        /// <summary>Use the creating actor chain, never guess St_* from an Ai_* name.</summary>
        private string MemorySource(Actor actor)
        {
            if (TryGetAttributedDamageSource(actor, out string attributed)) return attributed;
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
            {
                // Gems can borrow a skill as their parent; their own damage is not that memory.
                if (actor is Gem || (actor is AbilityInstance instance && instance.gem != null)) return null;
                if (!(actor is SkillTrigger)) continue;
                var type = actor.GetType();
                if (!_memorySourceTypes.TryGetValue(type, out string memory))
                {
                    string name = type.Name;
                    memory = Links.IsMemory(name) ? name : null;
                    _memorySourceTypes[type] = memory;
                }
                return memory;
            }
            return null;
        }

        private void OnMemoryDamage(HeroRuntime rt, EventInfoDamage info)
        {
            try
            {
                if (!Alive(rt.Hero) || info.victim == null
                    || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy || info.damage.amount <= 0f) return;
                int victimId = info.victim.GetInstanceID();
                bool generated = _gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reflectingDamage || _shattering
                    || (!info.chain.Equals(default(ReactionChain)) && !IsAttributedNativePacket(info.actor, info.victim))
                    || IsPairReactionSource(info.actor);
                if (generated && info.victim.currentHealth <= 0.00001f) rt.GeneratedKillVictims.Add(victimId);
                else rt.GeneratedKillVictims.Remove(victimId);
                if (generated) return;
                TrackCritBasicDamage(rt, info, victimId);
                OnNewPowerDamage(rt, info);
                string memory = MemorySource(info.actor);
                if (memory == null) return;
                if (rt.Powers.Build.Gimmicks.Count > 0 || rt.Powers.Build.PairCombos.Count > 0)
                    rt.MemoryHitAmounts[victimId] = info.damage.amount;
                QueueGimmicks(rt, GimmickTrigger.OnHit, memory, info.victim, info.damage.amount, generated, info.actor, !info.damage.HasAttr(DamageAttribute.DamageOverTime));
                if (info.damage.HasAttr(DamageAttribute.IsCrit))
                    QueueGimmicks(rt, GimmickTrigger.OnCrit, memory, info.victim, info.damage.amount, generated, info.actor, !info.damage.HasAttr(DamageAttribute.DamageOverTime));
            }
            catch (Exception ex) { Log.Error("Host: memory hit " + ex); }
        }

        private void OnMemoryKill(HeroRuntime rt, EventInfoKill info)
        {
            try
            {
                if (!Alive(rt.Hero) || info.victim == null
                    || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy) return;
                int victimId = info.victim.GetInstanceID();
                bool generated = rt.GeneratedKillVictims.Remove(victimId)
                    || _gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reflectingDamage || _shattering
                    || IsPairReactionSource(info.actor);
                rt.MemoryHitAmounts.TryGetValue(victimId, out float damage);
                rt.MemoryHitAmounts.Remove(victimId);
                if (generated) return;
                TrackCritBasicKill(rt, victimId);
                OnNewPowerKill(rt, info);
                string memory = MemorySource(info.actor);
                if (memory != null) QueueGimmicks(rt, GimmickTrigger.OnKill, memory, info.victim, damage, generated, info.actor);
            }
            catch (Exception ex) { Log.Error("Host: memory kill " + ex); }
        }



        private void OnPairEnemyDamage(EventInfoDamage info)
        {
            if (info.victim == null || info.victim.currentHealth > 0.00001f) return;
            int id = info.victim.GetInstanceID();
            if (_gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reflectingDamage || _shattering
                || (!info.chain.Equals(default(ReactionChain)) && !IsAttributedNativePacket(info.actor, info.victim))
                || IsPairReactionSource(info.actor)) _generatedPairDeaths.Add(id);
            else _generatedPairDeaths.Remove(id);
        }

        private void OnPairEnemyDeath(EventInfoKill info)
        {
            if (info.victim == null) return;
            bool generated = _generatedPairDeaths.Remove(info.victim.GetInstanceID());
            if (generated || _gimmickDamageDepth != 0 || _pairDamageDepth != 0 || _reflectingDamage || _shattering
                || IsPairReactionSource(info.actor)) return;
            // This server event precedes actor kill propagation and Destroy, unlike the client RPC.
            foreach (var rt in _runtimes.Values)
            {
                if (!Alive(rt.Hero) || rt.Powers.Build.PairCombos.Count == 0
                    || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy) continue;
                CollectPairMemories(rt);
                rt.GimmickRequests.Clear();
                rt.PairCombos.Fire(PairComboTrigger.OnKill, null, Time.time,
                    info.victim.GetInstanceID(), 0f, false, rt.PairMemories, HasOwnSummons(rt), rt.GimmickRequests);
                QueueGimmickRequests(rt, info.victim, Time.time);
            }
        }



        /// <summary>中心の周りの敵へダメージ（except を除き、最大 maxTargets 体）。</summary>
        private static void DamageAround(Hero hero, Vector3 center, float radius, float amount, Entity except, int maxTargets, bool magic, bool gimmick = false)
        {
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, center, radius,
                EnemyFilter, hero);
            // 検索結果そのものがプールのリスト。Dispatch 中も借りたままにし、再入した検索と共有しない。
            try
            {
                int n = 0;
                foreach (var e in found)
                {
                    if (e == except) continue;
                    if (n++ >= maxTargets) break;
                    var damage = magic ? hero.MagicDamage(amount, 0f) : hero.PhysicalDamage(amount, 0f);
                    if (gimmick) damage = damage.SetAmountModifiedBy(typeof(GimmickRuntime));
                    damage.Dispatch(e);
                }
            }
            finally
            {
                handle.Return();
            }
        }

        private HeroRuntime RuntimeOf(Actor actor)
        {
            if (actor == null) return null;
            Entity e = actor as Entity ?? actor.firstEntity;
            return e is Hero h && _runtimes.TryGetValue(h, out var rt) ? rt : null;
        }

        private void SendBountyReport(HeroRuntime rt, BountyReportKind kind, int value)
        {
            var hero = rt.Hero;
            var player = hero != null ? hero.owner : null;
            if (player == null || !player.isHumanPlayer || player.hero != hero) return;
            _registeredOn?.CustomRpc_SendMessageToClient(player, new DreamforgeBountyReportMsg
            {
                protocol = Protocol.Version,
                heroNetId = hero.netId,
                runId = NetworkedManagerBase<GameManager>.softInstance?.runId,
                report = (int)kind,
                value = value,
            });
        }

        private void ReportElementalDeath(Monster monster)
        {
            if (monster.disableLoot || monster.Status == null) return;
            if (monster.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter) && !hunter.enableGoldAndExpDrops) return;
            var status = monster.Status;
            int mask = (status.fireStack > 0 ? 1 : 0) | (status.hasCold ? 2 : 0)
                | (status.lightStack > 0 ? 4 : 0) | (status.darkStack > 0 ? 8 : 0);
            if (mask == 0) return;
            // Like shared kill loot, every participating enemy-facing player receives this kill.
            // The synchronous server death event runs before Destroy clears the victim's state.
            foreach (var rt in _runtimes.Values)
                if (rt.Hero != null && monster.GetRelation(rt.Hero) == EntityRelation.Enemy)
                    SendBountyReport(rt, BountyReportKind.ElementalKill, mask);
        }

        private void OnDeath(EventInfoKill info)
        {
            try
            {
                if (info.victim is Hero deadHero) OnAssignedMechanismDeath(deadHero);
                if (!(info.victim is Summon)) OnSupportDeathV129(info);
                if (!(info.victim is Monster monster)) return;
                CaptureAuthoritativeRunKill(monster);
                ReportElementalDeath(monster);
                OnReactionDeath(info.victim);
                if (_gimmickDamageDepth != 0 || _reactionEffectDepth != 0) return;
                var rt = OwnerRuntimeOf(info.actor);
                if (rt == null || !Alive(rt.Hero)) return;
                var r = rt.Powers.OnKill(Time.time, Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower), rt.Hero.maxHealth);
                if (r.Heal > 0) rt.Hero.Heal(r.Heal).Dispatch(rt.Hero);
                // 爆砕で倒した敵からは爆砕しない（同時に倒した群れ全体へ連鎖しないように。v1.28）
                if (r.ShatterDamage > 0 && !_shattering)
                {
                    _shattering = true;
                    try { DamageAround(rt.Hero, info.victim.position, PowerRuntime.ShatterRadius, r.ShatterDamage, null, int.MaxValue, magic: rt.Hero.Status.abilityPower > rt.Hero.Status.attackDamage); }
                    finally { _shattering = false; }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnDeath " + ex.Message);
            }
        }

        private void OnTakeDamage(EventInfoDamage info)
        {
            try
            {
                if (!(info.victim is Hero hero) || !_runtimes.TryGetValue(hero, out var rt) || !Alive(hero)) return;
                var attacker = info.actor != null ? (info.actor as Entity ?? info.actor.firstEntity) : null;
                bool enemy = attacker != null && attacker.isActive && attacker.GetRelation(hero) == EntityRelation.Enemy;
                bool reflected = _reflectingDamage || (_registeredOn != null && info.chain.DidReact(_registeredOn, false));
                OnNewPowerTaken(rt, info, enemy);
                OnRunGrowthDamage(rt, info, enemy);
                float reflect = rt.Powers.OnDamaged(Time.time, info.damage.amount, enemy && !reflected, enemy);
                if (reflect > 0)
                {
                    _reflectingDamage = true;
                    try
                    {
                        hero.PureDamage(reflect, 0f).Dispatch(attacker,
                            _registeredOn != null ? info.chain.New(_registeredOn) : info.chain);
                    }
                    finally { _reflectingDamage = false; }
                }
                float aegis = rt.Powers.TakeAegis(Time.time, info.damage.amount, hero.maxHealth);
                if (aegis > 0) hero.GiveShield(hero, aegis, 6f);
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnTakeDamage " + ex.Message);
            }
        }

        /// <summary>四元の共鳴：属性が付いた瞬間、4属性が揃っていれば爆発。</summary>
        private void OnApplyElemental(EventInfoApplyElemental info)
        {
            try
            {
                if (_gimmickDamageDepth != 0 || _reactionEffectDepth != 0) return;
                var rt = OwnerRuntimeOf(info.actor);
                if (rt == null || !Alive(rt.Hero)) return;
                var victim = info.victim;
                if (victim == null || !victim.isActive || victim.Status == null
                    || victim.GetRelation(rt.Hero) != EntityRelation.Enemy) return;
                OnNewPowerElement(rt, info);
                QueueElementReactions(rt, victim);
                var st = victim.Status;
                bool all = st.fireStack > 0 && st.hasCold && st.lightStack > 0 && st.darkStack > 0;
                float dmg = rt.Powers.TakeConvergence(Time.time, (int)victim.netId, all,
                    Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower));
                if (dmg > 0)
                {
                    _pairDamageDepth++;
                    try { rt.Hero.PureDamage(dmg, 0f).Dispatch(victim); }
                    finally { _pairDamageDepth--; }
                }
                if (!_spreadingFire && info.type == ElementalType.Fire && info.addedStack > 0
                    && victim.isActive && victim.GetRelation(rt.Hero) == EntityRelation.Enemy
                    && rt.Powers.Build.Get(Power.Wildfire) > 0 && st.fireStack >= PowerRuntime.WildfireMinStacks)
                    SpreadWildfire(rt, victim);
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnApplyElemental " + ex.Message);
            }
        }

        private void SpreadWildfire(HeroRuntime rt, Entity victim)
        {
            var hero = rt.Hero;
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, victim.agentPosition, PowerRuntime.WildfireRange,
                EnemyFilter, hero);
            try
            {
                Entity nearest = null;
                float nearestDistance = float.PositiveInfinity;
                foreach (var other in found)
                {
                    if (other == victim || !other.isActive) continue;
                    float distance = (other.agentPosition - victim.agentPosition).sqrMagnitude;
                    if (distance > PowerRuntime.WildfireRange * PowerRuntime.WildfireRange) continue;
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearest = other;
                }
                if (nearest == null || !rt.Powers.TakeWildfire(Time.time, (int)victim.netId,
                    victim.Status.fireStack, _rng.NextDouble())) return;
                _spreadingFire = true;
                try
                {
                    hero.ApplyElemental(ElementalType.Fire, nearest, 1);
                    LogPowerTrigger(Power.Wildfire);
                }
                finally
                {
                    _spreadingFire = false;
                }
            }
            finally
            {
                handle.Return();
            }
        }

    }
}
