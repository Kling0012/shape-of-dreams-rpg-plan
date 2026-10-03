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
        private sealed class ReceivedBuild
        {
            public Build Build;
            public string Encoded;
            public string Summary;
        }

        private sealed class HeroRuntime
        {
            public Hero Hero;
            public PowerRuntime Powers;
            public readonly GimmickRuntime Gimmicks = new GimmickRuntime();
            public readonly List<GimmickRequest> GimmickRequests = new List<GimmickRequest>();
            public readonly List<PendingGimmick> PendingGimmicks = new List<PendingGimmick>();
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
            // 星図のエッセンス枠（v1.27）。旅人の最初の枠の数と、前回こちらが足した分。
            public bool GemSlotsCaptured;
            public int BaseGemIdentity, BaseGemMovement;
            public int AddedGemIdentity, AddedGemMovement;
        }

        private struct PendingGimmick
        {
            public GimmickRequest Request;
            public Entity Victim;
            public float Due;
            public Vector3 Center;
        }

        private sealed class MonsterRuntime
        {
            public Monster Monster;
            public float QueuedAt;
            public bool SpawnProcessed;
            public StatBonus DepthBonus;
            public StatBonus SpecialBonus;
            public bool PressureApplied;
            public VariantDef Variant;
            public DataProcessor<DamageData, Actor, Entity> HitCap;
            public bool DeathBurstTriggered;
            public Se_GenericShield_OneShot Ward;
            public Action<EventInfoDamage> OnDamageDealt;
            public bool Reflects;
            public bool Sunders;
            public MonsterBehaviorRuntime Behavior;
            public Se_GenericShield_OneShot BehaviorShield;
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
        private readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        private readonly List<Hero> _scratch = new List<Hero>();
        private readonly List<Build> _pressureBuilds = new List<Build>();
        private readonly HashSet<DewPlayer> _pressurePlayers = new HashSet<DewPlayer>();
        private readonly List<DewPlayer> _departedPlayers = new List<DewPlayer>();
        private DreamPressure _pressure = DreamPressure.Neutral;
        private int _pressurePlayerCount = -1;
        private readonly DataProcessor<FinalStats> _pressureHealth;
        private readonly DataProcessor<DamageData, Actor, Entity> _pressureDamage;
        private readonly Action<DewPlayer> _onPressurePlayerAdded;
        private readonly Action<DewPlayer> _onPressurePlayerRemoved;
        private bool _pressureDirty = true;

        private Actor _registeredOn;
        private ClientEventManager _cem;
        private readonly Action<DreamforgeBuildMsg, DewPlayer> _onBuild;
        private readonly Action<DreamforgeCurseMsg, DewPlayer> _onCurse;
        private readonly Action<DreamforgeCurseClearMsg, DewPlayer> _onCurseClear;
        private readonly Action<DreamforgeTradeMsg, DewPlayer> _onTrade;
        private readonly Action<EventInfoKill> _onDeath;
        private readonly Action<EventInfoDamage> _onTakeDamage;
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
        private readonly Action<EventInfoStatusEffect> _onEnemyStatusAdded;
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
        private readonly List<Monster> _nightmareScratch = new List<Monster>();
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
            _dailyIdOfHost = dailyIdOfHost;
            _pressureHealth = (ref FinalStats stats) => stats.maxHealth *= (float)_pressure.HealthMultiplier;
            _pressureDamage = (ref DamageData damage, Actor actor, Entity target) =>
                damage.ApplyAmplification((float)_pressure.DamageMultiplier - 1f);
            _onPressurePlayerAdded = player => _pressureDirty = true;
            _onPressurePlayerRemoved = player =>
            {
                if (!ReferenceEquals(player, null)) _builds.Remove(player);
                _pressureDirty = true;
            };
            _onBuild = OnBuild;
            _onCurse = OnCurse;
            _onCurseClear = OnCurseClear;
            _onTrade = OnTrade;
            _onDeath = OnDeath;
            _onTakeDamage = OnTakeDamage;
            _onAttackHit = OnAttackHit;
            _onEntityAdd = OnEntityAdd;
            _onEntityRemove = OnEntityRemove;
            _onMonsterDeath = OnMonsterDeath;
            _onEnemyStatusAdded = OnEnemyStatusAdded;
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

        public void Tick()
        {
            if (!NetworkServer.active)
            {
                if (_pressurePlayerCount >= 0 || _registeredOn != null || _cem != null || _am != null || _zone != null) Detach();
                return;
            }
            EnsureRegistered();
            if (_registeredOn == null) return;
            float now = Time.time;
            if (_pressureDirty) RefreshPressure();
            PruneAndApplyPending();
            bool scan = now >= _nextAreaScan;
            if (scan)
            {
                _nextAreaScan = now + 0.25f;
                ScanArea();
            }
            foreach (var rt in _runtimes.Values) ApplyPendingGimmicks(rt, now);
            foreach (var rt in _runtimes.Values) UpdateRuntime(rt, now);
            ProcessSpawns();
            PruneMonsters(now);
            TickMonsterBehaviors(now);
            ExpireSunders(now);
            if (now >= _nextRegen)
            {
                _nextRegen = now + 0.5f;
                RegenNightmares();
            }
            if (now >= _nextNightmareSync)
            {
                _nextNightmareSync = now + 5f;
                ResyncNightmares();
                SendPressure();
            }
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
            foreach (var player in _departedPlayers) _builds.Remove(player);
            var pressure = DreamPressure.Average(_pressureBuilds);
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
                damageMultiplier = (float)_pressure.DamageMultiplier
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
            monster.Status.finalStatsProcessors.Add(_pressureHealth, int.MaxValue);
            monster.dealtDamageProcessor.Add(_pressureDamage);
            rt.PressureApplied = true;
            monster.Status.CalculateStatsIfDirty();
        }

        /// <summary>生きている悪夢・変種を定期的に全員へ送り直す（途中参加・取りこぼし対策）。</summary>
        private void ResyncNightmares()
        {
            if (_registeredOn == null) return;
            _nightmareScratch.Clear();
            foreach (var kv in _nightmares)
            {
                if (kv.Key == null || !kv.Key.isActive)
                {
                    _nightmareScratch.Add(kv.Key);
                    continue;
                }
                _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeNightmareMsg { netId = kv.Key.netId, affixes = (int)kv.Value });
                if (_monsters.TryGetValue(kv.Key, out var nightmareRuntime))
                    SendMonsterBehaviorCue(nightmareRuntime, true);
            }
            foreach (var rt in _monsters.Values)
            {
                if (rt.Variant == null) continue;
                var m = rt.Monster;
                if (m == null || !m.isActive)
                {
                    _nightmareScratch.Add(m);
                    continue;
                }
                _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeVariantMsg { netId = m.netId, variantId = rt.Variant.Id });
                SendMonsterBehaviorCue(rt, true);
            }
            foreach (var m in _nightmareScratch) RemoveMonster(m);
        }

        /// <summary>パーティの最大の夢の深度（MOD導入者の Build から）。</summary>
        private int PartyDepth()
        {
            int d = 0;
            foreach (var kv in _builds)
                if (kv.Key != null && kv.Key.hero != null) d = Math.Max(d, kv.Value.Build.Heat);
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
            if (!(e is Monster m) || _monsters.ContainsKey(m)) return;
            try
            {
                var rt = new MonsterRuntime { Monster = m, QueuedAt = Time.time };
                _monsters[m] = rt;
                m.ClientEntityEvent_OnStatusEffectAdded += _onEnemyStatusAdded;
                m.EntityEvent_OnDeath += _onMonsterDeath;
                _spawnQueue.Add(new KeyValuePair<Monster, float>(m, rt.QueuedAt));
            }
            catch (Exception ex) { Log.Error("Host: OnEntityAdd " + ex); }
        }

        private void OnEntityRemove(Entity e)
        {
            if (e is Monster m) RemoveMonster(m);
        }

        private void OnMonsterDeath(EventInfoKill info)
        {
            if (!(info.victim is Monster m)) return;
            try
            {
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
                Unhook(rt);
            }
            _nightmares.Remove(m);
            _regen.Remove(m);
            for (int i = _spawnQueue.Count - 1; i >= 0; i--)
                if (_spawnQueue[i].Key == m) _spawnQueue.RemoveAt(i);
        }

        private void UnhookMonsters()
        {
            foreach (var rt in _monsters.Values) Unhook(rt);
            _monsters.Clear();
        }

        private void Unhook(MonsterRuntime rt)
        {
            var m = rt.Monster;
            if (m == null) return;
            RemoveMonsterBehavior(rt);
            if (rt.PressureApplied)
            {
                try
                {
                    m.Status.finalStatsProcessors.Remove(_pressureHealth);
                    m.Status.CalculateStatsIfDirty();
                }
                catch (Exception ex) { Log.Error("Host: unhook pressure health " + ex); }
                try { m.dealtDamageProcessor.Remove(_pressureDamage); }
                catch (Exception ex) { Log.Error("Host: unhook pressure damage " + ex); }
                rt.PressureApplied = false;
            }
            // Each removal is independent: one failed cleanup must not leave other hooks attached.
            try { m.ClientEntityEvent_OnStatusEffectAdded -= _onEnemyStatusAdded; }
            catch (Exception ex) { Log.Error("Host: unhook monster status " + ex); }
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

        private void OnMonsterDamageTaken(EventInfoDamage info)
        {
            try
            {
                if (_reflectingDamage || (_registeredOn != null && info.chain.DidReact(_registeredOn, false))
                    || info.damage.amount <= 0 || !(info.victim is Monster m)
                    || !_monsters.TryGetValue(m, out var rt) || !rt.Reflects) return;
                var attacker = info.actor != null ? info.actor as Entity ?? info.actor.firstEntity : null;
                if (!(attacker is Hero hero) || !Alive(hero) || hero.GetRelation(m) != EntityRelation.Enemy) return;
                _reflectingDamage = true;
                try
                {
                    m.PureDamage(info.damage.amount * Nightmares.ThornsReflectPct / 100f, 0f)
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
        }

        private void OnActorRemove(Actor actor)
        {
            if (actor is Summon summon)
                foreach (var rt in _runtimes.Values)
                    if (rt.Summons.TryGetValue(summon, out var processor))
                    {
                        summon.dealtDamageProcessor.Remove(processor);
                        rt.Summons.Remove(summon);
                    }
            if (actor is Monster m) RemoveMonster(m);
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
            _roomHasVariant = false;
            foreach (var rt in _runtimes.Values) rt.Powers.OnZoneLoaded();
            UnhookShrines();
            ScanShrines();
        }

        private void ProcessSpawns()
        {
            if (_spawnQueue.Count == 0) return;
            if (_zone != null && _zone.isInAnyTransition) return;
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
                if (depth <= 0) continue;
                var tier = (MonsterTier)Math.Min((int)MonsterTier.Boss, (int)m.type);
                if (rt.SpawnProcessed) continue;
                rt.SpawnProcessed = true;
                try
                {
                    var stats = Nightmares.DepthBonus(tier, depth);
                    if (stats.Count > 0)
                    {
                        var bonus = ToMonsterStatBonus(stats);
                        rt.DepthBonus = bonus;
                        m.Status.AddStatBonus(bonus);
                        m.Status.CalculateStatsIfDirty();
                    }
                    var variant = Variants.Roll(_rng, m.GetType().Name, depth, _roomHasVariant);
                    if (variant != null)
                    {
                        _roomHasVariant = true;
                        MakeVariant(rt, variant);
                        continue;
                    }
                    var affix = Nightmares.Roll(_rng, tier, depth, mult);
                    if (affix != NightmareAffix.None) MakeNightmare(m, affix);
                }
                catch (Exception ex)
                {
                    Log.Error("Host: ProcessSpawns " + ex);
                }
            }
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
            _registeredOn?.CustomRpc_SendMessageToAllClients(new DreamforgeNightmareMsg { netId = m.netId, affixes = (int)affix });
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
            if (!_loggedVariantSpawn)
            {
                _loggedVariantSpawn = true;
                Log.Info($"variant spawned: {variant.Id} {m.GetType().Name} netId={m.netId}");
            }
            _registeredOn?.CustomRpc_SendMessageToAllClients(new DreamforgeVariantMsg { netId = m.netId, variantId = variant.Id });
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
        private void AttachMirageSkin(Monster m, bool allowTier1)
        {
            try
            {
                if (m.Status.HasStatusEffect<MirageSkinEffect>()) return;
                if (_mirageTier0 == null)
                {
                    _mirageTier0 = new List<MirageSkinEffect>();
                    _mirageTier1 = new List<MirageSkinEffect>();
                    foreach (var e in DewResources.FindAllByType<MirageSkinEffect>())
                    {
                        if (e == null) continue;
                        if (e.tier <= 0) _mirageTier0.Add(e);
                        else _mirageTier1.Add(e);
                    }
                    Log.Info($"MirageSkin pool: tier0={_mirageTier0.Count} tier1={_mirageTier1.Count}");
                }
                var pool = new List<MirageSkinEffect>(_mirageTier0);
                if (allowTier1) pool.AddRange(_mirageTier1);
                if (pool.Count == 0) return;
                var pick = pool[_rng.Range(0, pool.Count - 1)];
                m.CreateStatusEffect(pick.GetType(), m, new CastInfo(m));
            }
            catch (Exception ex)
            {
                Log.Warn("MirageSkin attach failed: " + ex.Message);
            }
        }

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
                DewPlayer.onGamePlayerAdded -= _onPressurePlayerAdded;
                DewPlayer.onGamePlayerRemoved -= _onPressurePlayerRemoved;
                if (_registeredOn != null)
                {
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeBuildMsg>(_onBuild); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeCurseMsg>(_onCurse); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeCurseClearMsg>(_onCurseClear); } catch (Exception) { }
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeTradeMsg>(_onTrade); } catch (Exception) { }
                }
                foreach (var rt in _runtimes.Values) { RemoveBonuses(rt); Unhook(rt); }
                _runtimes.Clear();
                _builds.Clear();
                _pressurePlayerCount = -1;
                _pressureDirty = true;
                _registeredOn = actor;
                if (actor != null)
                {
                    DewPlayer.onGamePlayerAdded += _onPressurePlayerAdded;
                    DewPlayer.onGamePlayerRemoved += _onPressurePlayerRemoved;
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeBuildMsg>(nameof(DreamforgeBuildMsg), _onBuild);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeCurseMsg>(nameof(DreamforgeCurseMsg), _onCurse);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeCurseClearMsg>(nameof(DreamforgeCurseClearMsg), _onCurseClear);
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeTradeMsg>(nameof(DreamforgeTradeMsg), _onTrade);
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
                ClearSunders();
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
                foreach (var rt in _runtimes.Values) rt.Powers.OnZoneLoaded();
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
                    cem.OnDeath += _onDeath;
                    cem.OnTakeDamage += _onTakeDamage;
                    cem.OnApplyElemental += _onApplyElemental;
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
            DewPlayer.onGamePlayerAdded -= _onPressurePlayerAdded;
            DewPlayer.onGamePlayerRemoved -= _onPressurePlayerRemoved;
            _pressureDirty = true;
            foreach (var rt in _runtimes.Values)
            {
                RemoveBonuses(rt);
                Unhook(rt);
            }
            _runtimes.Clear();
            _scanList.Clear();
            _scanPowers = Array.Empty<PowerRuntime>();
            _builds.Clear();
            _pressure = DreamPressure.Neutral;
            _pressurePlayerCount = -1;
            _pressureBuilds.Clear();
            _pressurePlayers.Clear();
            _departedPlayers.Clear();
            _pactCurses.Clear();
            Unsubscribe();
            UnhookShrines();
            UnhookMonsters();
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
                _registeredOn = null;
            }
        }

        private void OnBuild(DreamforgeBuildMsg msg, DewPlayer caller)
        {
            try
            {
                if (caller == null || msg == null || !caller.isHumanPlayer || !DewPlayer.gamePlayers.Contains(caller)) return;
                if (msg.protocol != Protocol.Version)
                {
                    Log.Warn($"Host: ignored build from {caller.playerName} (protocol {msg.protocol}, expected {Protocol.Version}). Different mod versions?");
                    return;
                }
                var hero = caller.hero;
                if (hero != null && !hero.IsNullOrInactive()
                    && _runtimes.TryGetValue(hero, out var rt) && rt.AppliedBuild != null && rt.AppliedBuild.Encoded == msg.build)
                {
                    // 定期再送は確認だけ返す。固有効果のスタックやクールダウンをリセットしない。
                    _builds[caller] = rt.AppliedBuild;
                    RefreshPressure(true);
                    SendApplied(caller, hero, rt.AppliedBuild);
                    return;
                }
                var build = Build.Decode(msg.build);
                if (build == null)
                {
                    Log.Warn("Host: rejected malformed build from " + caller.playerName);
                    return;
                }
                var received = new ReceivedBuild { Build = build, Encoded = msg.build, Summary = build.Encode() };
                _builds[caller] = received;
                RefreshPressure(true);
                if (hero != null && !hero.IsNullOrInactive()) Apply(hero, received);
                SendApplied(caller, hero, received);
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnBuild failed: " + ex);
            }
        }

        private void SendApplied(DewPlayer caller, Hero hero, ReceivedBuild build)
        {
            _registeredOn?.CustomRpc_SendMessageToClient(caller, new DreamforgeAppliedMsg
            {
                heroNetId = hero != null ? hero.netId : 0,
                summary = build.Summary,
            });
        }

        /// <summary>悪夢の契約の代償：送ってきたプレイヤーのキャラへ、本体の呪いをランダムに1つ付ける（Hatred の祭壇と同じもの）。</summary>
        /// <summary>本体の通貨での取引。残高を確かめて支払い・受け取りを行い、結果を返す（通貨を動かすのはホストだけ）。</summary>
        private void OnTrade(DreamforgeTradeMsg msg, DewPlayer caller)
        {
            if (caller == null || msg == null) return;
            bool ok = false;
            string reason = null;
            try
            {
                if (msg.protocol != Protocol.Version) reason = "protocol";
                else if (msg.spendGold < 0 || msg.spendDust < 0 || msg.earnDust < 0 || msg.earnDust > Economy.MaxDustEarnPerTrade) reason = "invalid";
                else if (caller.gold < msg.spendGold) reason = "gold";
                else if (caller.dreamDust < msg.spendDust) reason = "dust";
                else
                {
                    if (msg.spendGold > 0) caller.SpendGold(msg.spendGold);
                    if (msg.spendDust > 0) caller.SpendDreamDust(msg.spendDust);
                    if (msg.earnDust > 0) caller.EarnDreamDust(msg.earnDust);
                    ok = true;
                }
            }
            catch (Exception ex)
            {
                reason = "error";
                Log.Error("Host: OnTrade " + ex.Message);
            }
            _registeredOn?.CustomRpc_SendMessageToClient(caller, new DreamforgeTradeResultMsg { token = msg.token, ok = ok, reason = reason });
        }

        private List<CurseStatusEffect> _curseCache;

        // 潜行の契約で付けた呪い（プレイヤーごと）。契約が解けたら（確保・遠征の終わり）まとめて消す。
        private readonly Dictionary<DewPlayer, List<StatusEffect>> _pactCurses = new Dictionary<DewPlayer, List<StatusEffect>>();

        private void OnCurse(DreamforgeCurseMsg msg, DewPlayer caller)
        {
            try
            {
                if (caller == null || msg == null || msg.protocol != Protocol.Version) return;
                var hero = caller.hero;
                if (hero == null || !hero.isActive) return;
                var strength = msg.strength >= 3 ? HatredStrengthType.Powerful : msg.strength == 2 ? HatredStrengthType.Potent : HatredStrengthType.Mild;
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
                if (effect != null)
                {
                    if (!_pactCurses.TryGetValue(caller, out var list)) _pactCurses[caller] = list = new List<StatusEffect>();
                    list.Add(effect);
                }
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
                if (!_pactCurses.TryGetValue(caller, out var curses) || curses.Count == 0) return;
                int cleared = 0;
                foreach (var se in curses)
                {
                    // 既に消えているもの（ゲームの終了・部屋の切り替え・呪いの解除など）は数えない。
                    if (se == null || se.isDestroyed || !se.isActive) continue;
                    try
                    {
                        se.Destroy();
                        cleared++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Host: clear pact curse " + ex);
                    }
                }
                curses.Clear();
                if (cleared > 0) Log.Info($"pact curses cleared: {cleared}");
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnCurseClear " + ex);
            }
        }

        private void PruneAndApplyPending()
        {
            _scratch.Clear();
            foreach (var kv in _runtimes)
                if (kv.Key == null || !kv.Key.isActive) _scratch.Add(kv.Key);
            foreach (var h in _scratch)
            {
                Unhook(_runtimes[h]);
                _runtimes.Remove(h);
            }

            // 新しく生まれたキャラ（ラン開始・復帰）へ、届いている Build を付け直す。
            foreach (var kv in _builds)
            {
                var hero = kv.Key != null ? kv.Key.hero : null;
                if (hero == null || !hero.isActive) continue;
                if (!_runtimes.ContainsKey(hero)) Apply(hero, kv.Value);
            }
            foreach (var rt in _runtimes.Values) BindGoldSpend(rt);
        }

        private void Apply(Hero hero, ReceivedBuild received)
        {
            var build = received.Build;
            if (!_runtimes.TryGetValue(hero, out var rt))
            {
                rt = new HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, Time.time, hero.netId + 1UL) };
                var captured = rt;
                rt.OnFired = info => captured.Powers.OnAttackFired(info.isThisAttackFourthAttack);
                rt.OnSkill = info => OnSkillUse(captured, info);
                rt.OnHit = _onAttackHit;
                hero.EntityEvent_OnAttackFired += rt.OnFired;
                // Actor.DoBasicAttackHit は、この同期イベントの後で DealDamage を呼ぶ。
                // RPC 通知の配信時点に依存せず、命中前の HP で判定する。
                hero.EntityEvent_OnAttackHit += rt.OnHit;
                hero.ClientHeroEvent_OnSkillUse += rt.OnSkill;
                rt.OnTeleport = (from, to) => OnHeroTeleport(captured);
                rt.OnDisplacement = disp =>
                {
                    // Displacement has no source: the game's own Husk crit effects use isFriendly.
                    if (disp != null && disp.isFriendly) OnHeroSelfMovement(captured);
                };
                hero.Control.ClientEvent_OnTeleport += rt.OnTeleport;
                hero.Control.ClientEvent_OnDisplacementStarted += rt.OnDisplacement;
                rt.DamageTaken = (ref DamageData d, Actor a, Entity t) =>
                {
                    float mult = captured.Powers.Build.DamageTakenMultiplier;
                    if (mult > 1f) d.ApplyAmplification(mult - 1f);
                    // These verified sources dispatch HP sacrifice as self-damage, not enemy attacks.
                    if (t == captured.Hero && IsHealthSacrifice(d.actor, captured.Hero))
                        d = d.ApplyRawMultiplier(SupportStats.ReduceSacrifice(1f,
                            captured.Powers.Build.Get(Stat.SacrificeReduction)));
                };
                hero.takenDamageProcessor.Add(rt.DamageTaken);
                rt.DamageDealt = (ref DamageData d, Actor a, Entity t) =>
                {
                    if (!Alive(captured.Hero) || t == null || !t.isActive || t.Status == null
                        || t.GetRelation(captured.Hero) != EntityRelation.Enemy) return;
                    var status = t.Status;
                    float amp = captured.Powers.FettersAmplification(status.hasStun || status.hasSlow || status.hasCold);
                    if (amp > 0)
                    {
                        d.ApplyAmplification(amp);
                        LogPowerTrigger(Power.Fetters);
                    }
                    if (_gimmickDamageDepth != 0 || d.IsAmountModifiedBy(typeof(GimmickRuntime))) return;
                    string memory = MemorySource(d.actor ?? a);
                    int memoryAmp = 0;
                    if (memory != null)
                        foreach (var link in captured.SatisfiedLinks)
                            if (link.Kind == LinkKind.MemoryDamage && Array.IndexOf(link.Requires, memory) >= 0)
                            {
                                memoryAmp += link.Value;
                                LogLinkApplied(link);
                            }
                    if (memoryAmp > 0) d.ApplyAmplification(memoryAmp / 100f);
                    int expose = captured.Gimmicks.ExposePercent(t.GetInstanceID(), Time.time);
                    if (expose > 0) d.ApplyAmplification(expose / 100f);
                };
                hero.dealtDamageProcessor.Add(rt.DamageDealt);
                rt.OnMemoryDamage = info => OnMemoryDamage(captured, info);
                rt.OnMemoryKill = info => OnMemoryKill(captured, info);
                hero.ActorEvent_OnDealDamage += rt.OnMemoryDamage;
                hero.ActorEvent_OnKill += rt.OnMemoryKill;
                rt.OnSupportHeal = info =>
                {
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
                    heal.ApplyAmplification(SupportStats.AmplifyHeal(1f, captured.Powers.Build.Get(Stat.HealPower)) - 1f);
                rt.ShieldDealt = (ref HealData shield, Actor a, Entity t) =>
                    shield.ApplyAmplification(SupportStats.AmplifyShield(1f, captured.Powers.Build.Get(Stat.ShieldPower)) - 1f);
                hero.dealtHealProcessor.Add(rt.HealDealt);
                hero.dealtShieldProcessor.Add(rt.ShieldDealt);
                rt.OnSummon = info => HookSummon(captured, info.summon);
                hero.ActorEvent_OnSpawnSummon += rt.OnSummon;
                rt.OnHeal = info => OnHealTaken(captured, info);
                hero.EntityEvent_OnTakeHeal += rt.OnHeal;
                rt.OnImmunity = info => OnDamageNegated(captured, info);
                hero.EntityEvent_OnDamageNegatedByImmunity += rt.OnImmunity;
                _runtimes[hero] = rt;
            }
            RemoveBonuses(rt);
            // 連携の判定結果は Build と装着に紐付くので、付け直すときに一旦空にする。
            rt.HeroKey = hero.GetType().Name;
            rt.SatisfiedLinks.Clear();
            rt.LinkMemories.Clear();
            rt.LinkEssences.Clear();
            rt.Powers.SetBuild(build);
            rt.Gimmicks.SetBuild(build.Gimmicks);
            rt.PendingGimmicks.Clear();
            rt.BaseBonus = ToStatBonus(build);
            rt.DynBonus = new StatBonus();
            hero.Status.AddStatBonus(rt.BaseBonus);
            hero.Status.AddStatBonus(rt.DynBonus);
            rt.AppliedBuild = received;
            BindGoldSpend(rt);
            ApplyGemSlots(rt, build);
            // Builds can arrive after a persistent summon (for example Fenrir) has spawned.
            if (_am != null)
                foreach (var entity in _am.allEntities)
                    if (entity is Summon summon) HookSummon(rt, summon);
        }

        private static bool IsHealthSacrifice(Actor source, Hero hero)
        {
            if (source is Se_HealthCost cost) return cost.victim == hero;
            if (source is Ai_Q_GoldenBurst burst) return burst.info.caster == hero;
            if (source is Ai_Q_Reduction_Spawner reduction) return reduction.info.caster == hero;
            return false;
        }

        private static void HookSummon(HeroRuntime rt, Summon summon)
        {
            if (summon == null || !summon.isActive
                || summon.FindFirstAncestorOfType<Hero>() != rt.Hero || rt.Summons.ContainsKey(summon)) return;
            DataProcessor<DamageData, Actor, Entity> processor = (ref DamageData damage, Actor source, Entity target) =>
            {
                // Only the nearest summon handles a hit: child summons also inherit ancestor processors.
                if (source == null || source.FindFirstOfType<Summon>() != summon
                    || summon.FindFirstAncestorOfType<Hero>() != rt.Hero) return;
                damage.ApplyAmplification(SupportStats.AmplifySummonDamage(1f,
                    rt.Powers.Build.Get(Stat.SummonPower)) - 1f);
            };
            rt.Summons.Add(summon, processor);
            summon.dealtDamageProcessor.Add(processor);
        }

        /// <summary>
        /// 星図のエッセンス枠（v1.27）。Build の分だけ SetMaxGemCount を広げる。
        /// 旅人の最初の枠の数は初回だけ覚える。本体や他の効果（混沌の聖堂など）が足した分は
        /// 「いまの枠 −（元の枠 + 前回こちらが足した分）」として取り出して、壊さずに保つ。
        /// 枠が減ってはみ出たエッセンスは足元へ落とす（壊さない）。ホストだけで行う。
        /// </summary>
        private void ApplyGemSlots(HeroRuntime rt, Build build)
        {
            try
            {
                var skill = rt.Hero != null ? rt.Hero.Skill : null;
                if (skill == null) return;
                if (!rt.GemSlotsCaptured)
                {
                    rt.GemSlotsCaptured = true;
                    rt.BaseGemIdentity = skill.GetMaxGemCount(HeroSkillLocation.Identity);
                    rt.BaseGemMovement = skill.GetMaxGemCount(HeroSkillLocation.Movement);
                    rt.AddedGemIdentity = 0;
                    rt.AddedGemMovement = 0;
                }
                int addedIdentity = EssenceSlots.AddedFrom(build, Stat.EssenceSlotIdentity);
                int addedMovement = EssenceSlots.AddedFrom(build, Stat.EssenceSlotMovement);
                ApplyGemSlot(rt, skill, HeroSkillLocation.Identity, rt.BaseGemIdentity, rt.AddedGemIdentity, addedIdentity);
                ApplyGemSlot(rt, skill, HeroSkillLocation.Movement, rt.BaseGemMovement, rt.AddedGemMovement, addedMovement);
            }
            catch (Exception ex)
            {
                Log.Error("Host: gem slots " + ex);
            }
        }

        private void ApplyGemSlot(HeroRuntime rt, HeroSkill skill, HeroSkillLocation loc, int original, int previouslyAdded, int added)
        {
            int current = skill.GetMaxGemCount(loc);
            int target = EssenceSlots.TargetMax(original, previouslyAdded, added, current);
            if (target != current) skill.SetMaxGemCount(loc, target);
            // 減ったとき、はみ出たエッセンスは番号が大きい枠から旅人の足元へ落とす。
            int overflow = EssenceSlots.Overflow(skill.GetCurrentGemCount(loc), target);
            if (overflow > 0)
            {
                var slots = new List<KeyValuePair<GemLocation, Gem>>();
                foreach (var kv in skill.gems)
                    if (kv.Key.skill == loc && kv.Value != null) slots.Add(kv);
                slots.Sort((a, b) => b.Key.index.CompareTo(a.Key.index));
                var dropAt = rt.Hero != null ? rt.Hero.position : default(Vector3);
                for (int i = 0; i < overflow && i < slots.Count; i++) skill.UnequipGem(slots[i].Key, dropAt);
            }
            if (target != current || overflow > 0)
                Log.Info($"Host: gem slots {rt.HeroKey}/{loc} {current} -> {target} (base {original}, stars +{added})" + (overflow > 0 ? $", dropped {overflow} essence(s) at the hero's feet" : ""));
            // 次回の「他の効果の分」の計算は、いま設定した状態から。
            if (loc == HeroSkillLocation.Identity) rt.AddedGemIdentity = added;
            else rt.AddedGemMovement = added;
        }

        private static void Unhook(HeroRuntime rt)
        {
            UnhookGoldSpend(rt);
            foreach (var summon in rt.Summons)
                if (summon.Key != null) summon.Key.dealtDamageProcessor.Remove(summon.Value);
            rt.Summons.Clear();
            var hero = rt.Hero;
            if (hero == null) return;
            try
            {
                if (rt.OnImmunity != null) hero.EntityEvent_OnDamageNegatedByImmunity -= rt.OnImmunity;
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
            rt.HealDealt = null;
            rt.ShieldDealt = null;
            rt.OnSummon = null;
            rt.OnMemoryDamage = null;
            rt.OnMemoryKill = null;
            rt.OnSupportHeal = null;
            rt.OnSupportShield = null;
            rt.PendingGimmicks.Clear();
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

        private void OnEnemyStatusAdded(EventInfoStatusEffect info)
        {
            try
            {
                var effect = info.effect;
                if (info.victim == null || !info.victim.isActive || effect == null
                    || !(effect.info.caster is Hero hero) || !Alive(hero)
                    || info.victim.GetRelation(hero) != EntityRelation.Enemy
                    || !_runtimes.TryGetValue(hero, out var rt)) return;
                bool stun = false;
                foreach (var basic in effect.basicEffects)
                    if (basic != null && basic.isAlive && (basic.mask & BasicEffectMask.Stun) != 0)
                    { stun = true; break; }
                float shield = rt.Powers.TakeStillWater(Time.time, stun, hero.maxHealth);
                if (shield <= 0) return;
                hero.GiveShield(hero, shield, PowerRuntime.StillWaterDuration, false, default(ReactionChain));
                LogPowerTrigger(Power.StillWater);
            }
            catch (Exception ex) { Log.Error("Host: StillWater " + ex); }
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
                if (_gimmickDamageDepth == 0 && info.type != HeroSkillLocation.Movement && info.skill != null)
                    QueueGimmicks(rt, GimmickTrigger.OnUse, info.skill.GetType().Name, null, 0f);
                var r = rt.Powers.OnSkillUsed(Time.time, info.type == HeroSkillLocation.Movement, info.type == HeroSkillLocation.R,
                    Math.Max(hero.Status.attackDamage, hero.Status.abilityPower), hero.maxHealth);
                if (r.Shield > 0) hero.GiveShield(hero, r.Shield, PowerRuntime.StarShieldDuration);
                if (r.WhirlwindDamage > 0) DamageAround(hero, hero.agentPosition, PowerRuntime.WhirlwindRadius, r.WhirlwindDamage, null, int.MaxValue, magic: hero.Status.abilityPower > hero.Status.attackDamage);
                if (hero.Skill == null) return;
                int slot = info.type == HeroSkillLocation.Q ? 0 : info.type == HeroSkillLocation.W ? 1
                    : info.type == HeroSkillLocation.E ? 2 : -1;
                float finale = rt.Powers.TakeFinale(Time.time, slot);
                if (finale > 0)
                {
                    var ultimate = hero.Skill.GetSkill(HeroSkillLocation.R);
                    if (ultimate != null)
                    {
                        hero.ApplyCooldownReductionByRatio(ultimate, finale, false);
                        LogPowerTrigger(Power.Finale);
                    }
                }
                // 連携（v1.26）：使った記憶が条件に入っている連携だけを発動する。
                UpdateLinks(rt, true);
                if (rt.SatisfiedLinks.Count > 0 && info.skill != null)
                {
                    string used = info.skill.GetType().Name;
                    int haste = PowerRuntime.LinkHastePercent(rt.SatisfiedLinks, used);
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
            }
            catch (Exception) { }
            rt.BaseBonus = null;
            rt.DynBonus = null;
        }

        internal static StatBonus ToStatBonus(Build b)
        {
            var s = new StatBonus();
            foreach (var kv in b.Stats)
            {
                float v = StatUnits.ToGame(kv.Key, kv.Value);
                switch (kv.Key)
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
            return s;
        }

        private static bool Alive(Hero h) => h != null && h.isActive && !h.isKnockedOut;

        private void UpdateRuntime(HeroRuntime rt, float now)
        {
            var hero = rt.Hero;
            if (!Alive(hero) || rt.DynBonus == null) return;
            var p = rt.Powers;
            p.HealthRatio = hero.maxHealth > 0 ? hero.currentHealth / hero.maxHealth : 1f;
            var dyn = p.Current(now);
            dyn.AttackSpeedPct += rt.Gimmicks.QuickenPercent(now);
            int empower = rt.Gimmicks.EmpowerPercent(now);
            dyn.AttackPct += empower;
            dyn.PowerPct += empower;
            // Kill() follows the synchronous hit event; do not retain old victims between frames.
            rt.MemoryHitAmounts.Clear();
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
            int attune = 0, guardHealth = 0, guardArmor = 0;
            foreach (var link in links)
            {
                if (!Links.Satisfied(link, rt.HeroKey, memories, essences)) continue;
                rt.SatisfiedLinks.Add(link);
                switch (link.Kind)
                {
                    case LinkKind.Attune:
                        attune += link.Value;
                        LogLinkApplied(link);
                        break;
                    case LinkKind.Guard:
                        guardHealth += link.Value;
                        guardArmor += link.Value;
                        LogLinkApplied(link);
                        break;
                }
            }
            p.RetainLinkSurges(rt.SatisfiedLinks, Time.time);
            p.LinkAttunePct = attune;
            p.LinkGuardHealthPct = guardHealth;
            p.LinkGuardArmor = guardArmor;
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
                if (_gimmickDamageDepth != 0 || !Alive(rt.Hero) || info.victim == null
                    || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy || info.damage.amount <= 0f) return;
                string memory = MemorySource(info.actor);
                if (memory == null) return;
                if (rt.Powers.Build.Gimmicks.Count > 0)
                    rt.MemoryHitAmounts[info.victim.GetInstanceID()] = info.damage.amount;
                QueueGimmicks(rt, GimmickTrigger.OnHit, memory, info.victim, info.damage.amount);
                if (info.damage.HasAttr(DamageAttribute.IsCrit))
                    QueueGimmicks(rt, GimmickTrigger.OnCrit, memory, info.victim, info.damage.amount);
            }
            catch (Exception ex) { Log.Error("Host: memory hit " + ex); }
        }

        private void OnMemoryKill(HeroRuntime rt, EventInfoKill info)
        {
            try
            {
                if (_gimmickDamageDepth != 0 || !Alive(rt.Hero) || info.victim == null
                    || info.victim.GetRelation(rt.Hero) != EntityRelation.Enemy) return;
                string memory = MemorySource(info.actor);
                int victimId = info.victim.GetInstanceID();
                rt.MemoryHitAmounts.TryGetValue(victimId, out float damage);
                rt.MemoryHitAmounts.Remove(victimId);
                if (memory != null) QueueGimmicks(rt, GimmickTrigger.OnKill, memory, info.victim, damage);
            }
            catch (Exception ex) { Log.Error("Host: memory kill " + ex); }
        }

        private void QueueGimmicks(HeroRuntime rt, GimmickTrigger trigger, string memory, Entity victim, float damage)
        {
            if (rt.Powers.Build.Gimmicks.Count == 0 || FindMemory(rt.Hero, memory) == null) return;
            var requests = rt.GimmickRequests;
            requests.Clear();
            float now = Time.time;
            rt.Gimmicks.Fire(trigger, memory, now, victim != null ? victim.GetInstanceID() : 0,
                damage, _gimmickDamageDepth != 0, requests);
            if (requests.Count > 0) SendBountyReport(rt, BountyReportKind.GimmicksTriggered, requests.Count);
            foreach (var request in requests)
            {
                var effect = request.Entry.Def.Effect;
                if (effect == GimmickEffect.Quicken || effect == GimmickEffect.Empower || effect == GimmickEffect.Expose) continue;
                rt.PendingGimmicks.Add(new PendingGimmick
                {
                    Request = request,
                    Victim = victim,
                    Center = request.AreaAroundHero ? rt.Hero.agentPosition
                        : victim != null ? victim.position : rt.Hero.agentPosition,
                    Due = now + (request.Entry.Def.Effect == GimmickEffect.Echo ? 0.3f : 0f),
                });
            }
            requests.Clear();
        }

        private static SkillTrigger FindMemory(Hero hero, string memory)
        {
            if (hero.Skill == null) return null;
            foreach (var slot in LinkSkills)
            {
                var skill = hero.Skill.GetSkill(slot);
                if (skill != null && skill.GetType().Name == memory) return skill;
            }
            return null;
        }

        private void ApplyPendingGimmicks(HeroRuntime rt, float now)
        {
            var pending = rt.PendingGimmicks;
            if (!Alive(rt.Hero)) { pending.Clear(); return; }
            // Remove before dispatch: nested game events must never replay this request.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var effect = pending[i];
                if (effect.Due > now) continue;
                pending.RemoveAt(i);
                try { ApplyGimmick(rt, effect); }
                catch (Exception ex) { Log.Error("Host: memory effect " + ex); }
            }
        }

        private void ApplyGimmick(HeroRuntime rt, PendingGimmick pending)
        {
            var hero = rt.Hero;
            var request = pending.Request;
            var def = request.Entry.Def;
            var victim = pending.Victim;
            bool liveTarget = victim != null && victim.isActive && victim.currentHealth > 0f
                && victim.GetRelation(hero) == EntityRelation.Enemy;
            switch (def.Effect)
            {
                case GimmickEffect.Element:
                    int stacks = def.Value / 100;
                    if (_rng.NextDouble() * 100 < def.Value % 100) stacks++;
                    if (stacks <= 0) break;
                    var element = def.Arg == 0 ? ElementalType.Fire : def.Arg == 1 ? ElementalType.Cold
                        : def.Arg == 2 ? ElementalType.Light : ElementalType.Dark;
                    if (request.AreaRadius > 0f)
                    {
                        ListReturnHandle<Entity> handle;
                        var found = DewPhysics.OverlapCircleAllEntities(out handle, pending.Center, request.AreaRadius, EnemyFilter, hero);
                        try
                        {
                            foreach (var enemy in found)
                                if (enemy != victim && enemy != null && enemy.isActive && enemy.currentHealth > 0f)
                                    hero.ApplyElemental(element, enemy, stacks);
                        }
                        finally { handle.Return(); }
                    }
                    else if (liveTarget) hero.ApplyElemental(element, victim, stacks);
                    break;
                case GimmickEffect.Burst:
                    _gimmickDamageDepth++;
                    try
                    {
                        DamageAround(hero, pending.Center, 4f,
                            Math.Max(hero.Status.attackDamage, hero.Status.abilityPower) * def.Value / 100f,
                            null, int.MaxValue, hero.Status.abilityPower > hero.Status.attackDamage, gimmick: true);
                    }
                    finally { _gimmickDamageDepth--; }
                    break;
                case GimmickEffect.Shield:
                    // The root server actor has no hero ancestors: native support cannot grant Heart of the Pack.
                    // 出どころを変えたので、旅人のシールド量はここで掛ける（v1.27.1 の能力値）。
                    ActorManager.instance.serverActor.GiveShield(hero,
                        SupportStats.AmplifyShield(hero.maxHealth * def.Value / 100f, rt.Powers.Build.Get(Stat.ShieldPower)), 4f);
                    break;
                case GimmickEffect.Heal:
                    var support = ActorManager.instance.serverActor;
                    // 出どころを変えたので、旅人の回復量はここで掛ける（v1.27.1 の能力値）。
                    int healPower = rt.Powers.Build.Get(Stat.HealPower);
                    support.Heal(SupportStats.AmplifyHeal(hero.maxHealth * def.Value / 100f, healPower)).Dispatch(hero);
                    if (def.Arg == 1)
                        foreach (var player in DewPlayer.gamePlayers)
                        {
                            var ally = player != null ? player.hero : null;
                            if (ally == hero || !Alive(ally) || ally.GetRelation(hero) != EntityRelation.Ally
                                || (ally.agentPosition - hero.agentPosition).sqrMagnitude > 100f) continue;
                            support.Heal(SupportStats.AmplifyHeal(ally.maxHealth * def.Value / 100f, healPower)).Dispatch(ally);
                        }
                    break;
                case GimmickEffect.Recharge:
                    ReduceMemoryCooldown(hero, FindMemory(hero, request.Entry.Memory), def.Value);
                    break;
                case GimmickEffect.Reload:
                    var skill = FindMemory(hero, request.Entry.Memory);
                    if (skill != null && Gimmicks.TryReload(skill.currentConfigCurrentCharge, skill.currentConfig.maxCharges,
                        out int nextCharges, out bool resetCooldown))
                    {
                        if (resetCooldown) hero.ResetCooldown(skill);
                        else skill.SetCharge(skill.currentConfigIndex, nextCharges);
                    }
                    break;
                case GimmickEffect.RechargeOther:
                    if (hero.Skill == null) break;
                    foreach (var slot in LinkSkills)
                    {
                        var other = hero.Skill.GetSkill(slot);
                        if (other == null || slot == HeroSkillLocation.Movement) continue;
                        if (Gimmicks.CanRechargeOther(request.Entry.Memory, other.GetType().Name,
                            other.type == SkillType.Normal, slot == HeroSkillLocation.Identity))
                            ReduceMemoryCooldown(hero, other, def.Value);
                    }
                    break;
                case GimmickEffect.Echo:
                    if (!liveTarget || request.Damage <= 0f) break;
                    _gimmickDamageDepth++;
                    try
                    {
                        // Final damage is already armor-adjusted; repeat that amount without a second armor reduction.
                        hero.PureDamage(request.Damage * def.Value / 100f, 0f)
                            .SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
                    }
                    finally { _gimmickDamageDepth--; }
                    break;
                // Quicken/Empower/Expose windows are registered by the pure runtime.
            }
        }

        private static void ReduceMemoryCooldown(Hero hero, SkillTrigger skill, int percent)
        {
            if (skill == null) return;
            // The native ratio is a fraction of maximum cooldown, not remaining cooldown.
            float ratio = Gimmicks.RemainingCooldownReductionRatio(skill.currentConfigUnscaledCooldownTime,
                skill.currentConfigUnscaledMaxCooldownTime, percent);
            if (ratio > 0f) hero.ApplyCooldownReductionByRatio(skill, ratio, false);
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
                if (!(info.victim is Monster monster)) return;
                ReportElementalDeath(monster);
                var rt = RuntimeOf(info.actor);
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
                float reflect = rt.Powers.OnDamaged(Time.time, info.damage.amount, enemy && !reflected);
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
                var rt = RuntimeOf(info.actor);
                if (rt == null || !Alive(rt.Hero)) return;
                var victim = info.victim;
                if (victim == null || !victim.isActive || victim.Status == null) return;
                var st = victim.Status;
                bool all = st.fireStack > 0 && st.hasCold && st.lightStack > 0 && st.darkStack > 0;
                float dmg = rt.Powers.TakeConvergence(Time.time, (int)victim.netId, all,
                    Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower));
                if (dmg > 0) rt.Hero.PureDamage(dmg, 0f).Dispatch(victim);
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

        private void OnAttackHit(EventInfoAttackHit info)
        {
            try
            {
                if (!(info.attacker is Hero hero) || !_runtimes.TryGetValue(hero, out var rt) || !Alive(hero)) return;
                var victim = info.victim;
                if (victim == null || !victim.isActive) return;
                float criticalEcho = rt.Powers.TakeCriticalEcho(Time.time, info.isCrit);
                if (ReduceMemoryCooldowns(hero, criticalEcho)) LogPowerTrigger(Power.CriticalEcho);
                float ratio = victim.maxHealth > 0 ? victim.currentHealth / victim.maxHealth : 1f;
                var r = rt.Powers.OnAttackHit(Time.time, hero.maxHealth, hero.Status.attackDamage, hero.Status.abilityPower,
                    ratio, info.isCrit, _rng.NextDouble());
                if (r.ChainDamage > 0) DamageAround(hero, victim.position, PowerRuntime.ChainRange, r.ChainDamage, victim, PowerRuntime.ChainTargets, magic: true);
                if (r.Heal > 0) hero.Heal(r.Heal).Dispatch(hero);
                if (r.ExecuteDamage > 0) hero.PureDamage(r.ExecuteDamage, 0f).Dispatch(victim);
                if (r.BlazeDamage > 0 && victim.isActive) hero.MagicDamage(r.BlazeDamage, 0f).Dispatch(victim);
                if (victim.isActive)
                {
                    if (r.FireStacks > 0) hero.ApplyElemental(ElementalType.Fire, victim, r.FireStacks);
                    if (r.ColdStacks > 0) hero.ApplyElemental(ElementalType.Cold, victim, r.ColdStacks);
                    if (r.LightStacks > 0) hero.ApplyElemental(ElementalType.Light, victim, r.LightStacks);
                    if (r.DarkStacks > 0) hero.ApplyElemental(ElementalType.Dark, victim, r.DarkStacks);
                }
                if (r.OpeningDamage > 0 && victim.isActive && victim.GetRelation(hero) == EntityRelation.Enemy)
                    hero.PureDamage(r.OpeningDamage, 0f).Dispatch(victim);
                if (r.EchoDamage > 0 && victim.isActive && victim.GetRelation(hero) == EntityRelation.Enemy)
                {
                    hero.PureDamage(r.EchoDamage, 0f).Dispatch(victim);
                    LogPowerTrigger(Power.EchoingDodge);
                }
                if (r.ShadowStepDamage > 0 && victim.isActive && victim.GetRelation(hero) == EntityRelation.Enemy)
                {
                    hero.PureDamage(r.ShadowStepDamage, 0f).Dispatch(victim);
                    LogPowerTrigger(Power.ShadowStep);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnAttackHit " + ex.Message);
            }
        }
    }
}
