using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Power = SodRpg.Core.Game.Power;
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>
    /// ホスト側の処理。各プレイヤーから届いた Build を、そのプレイヤーのキャラへ能力補正（StatBonus）として付け、
    /// 固有効果を戦闘イベントから発動する。効果の計算は SodRpg.Core の PowerRuntime が行い、ここはゲームへの作用だけを持つ。
    /// 能力値の変更はホストだけが行う（Mirror の権限モデル）。
    /// </summary>
    internal sealed class HostAuthority
    {
        private sealed class HeroRuntime
        {
            public Hero Hero;
            public PowerRuntime Powers;
            public StatBonus BaseBonus;
            public StatBonus DynBonus;
        }

        private readonly Dictionary<DewPlayer, Build> _builds = new Dictionary<DewPlayer, Build>();
        private readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        private readonly List<Hero> _scratch = new List<Hero>();

        private Actor _registeredOn;
        private ClientEventManager _cem;
        private readonly Action<DreamforgeBuildMsg, DewPlayer> _onBuild;
        private readonly Action<EventInfoKill> _onDeath;
        private readonly Action<EventInfoDamage> _onTakeDamage;
        private readonly Action<EventInfoAttackHit> _onAttackHit;
        private float _nextAreaScan;

        public HostAuthority()
        {
            _onBuild = OnBuild;
            _onDeath = OnDeath;
            _onTakeDamage = OnTakeDamage;
            _onAttackHit = OnAttackHit;
        }

        public bool IsActive => _registeredOn != null;

        public void Tick()
        {
            if (!NetworkServer.active)
            {
                if (_registeredOn != null || _cem != null) Detach();
                return;
            }
            EnsureRegistered();
            if (_registeredOn == null) return;
            float now = Time.time;
            PruneAndApplyPending();
            bool scan = now >= _nextAreaScan;
            if (scan)
            {
                _nextAreaScan = now + 0.25f;
                ScanArea();
            }
            foreach (var rt in _runtimes.Values) UpdateRuntime(rt, now);
        }

        private void EnsureRegistered()
        {
            var am = NetworkedManagerBase<ActorManager>.instance;
            var actor = am != null ? am.serverActor : null;
            if (actor != _registeredOn)
            {
                if (_registeredOn != null)
                {
                    try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeBuildMsg>(_onBuild); } catch (Exception) { }
                }
                _registeredOn = actor;
                if (actor != null)
                {
                    actor.CustomRpc_RegisterServerMessageHandler<DreamforgeBuildMsg>(nameof(DreamforgeBuildMsg), _onBuild);
                    Log.Info("Host: registered build handler.");
                }
            }
            var cem = NetworkedManagerBase<ClientEventManager>.instance;
            if (cem != _cem)
            {
                Unsubscribe();
                _cem = cem;
                if (cem != null)
                {
                    cem.OnDeath += _onDeath;
                    cem.OnTakeDamage += _onTakeDamage;
                    cem.OnAttackHit += _onAttackHit;
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
                _cem.OnAttackHit -= _onAttackHit;
            }
            catch (Exception) { }
            _cem = null;
        }

        /// <summary>全キャラから補正を外し、登録を解除する（MODの再読み込み・終了時）。</summary>
        public void Detach()
        {
            foreach (var rt in _runtimes.Values) RemoveBonuses(rt);
            _runtimes.Clear();
            _builds.Clear();
            Unsubscribe();
            if (_registeredOn != null)
            {
                try { _registeredOn.CustomRpc_UnregisterServerMessageHandler<DreamforgeBuildMsg>(_onBuild); } catch (Exception) { }
                _registeredOn = null;
            }
        }

        private void OnBuild(DreamforgeBuildMsg msg, DewPlayer caller)
        {
            try
            {
                if (caller == null || msg == null) return;
                if (msg.protocol != Protocol.Version)
                {
                    Log.Warn($"Host: ignored build from {caller.playerName} (protocol {msg.protocol}, expected {Protocol.Version}). Different mod versions?");
                    return;
                }
                var build = Build.Decode(msg.build);
                if (build == null)
                {
                    Log.Warn("Host: rejected malformed build from " + caller.playerName);
                    return;
                }
                _builds[caller] = build;
                var hero = caller.hero;
                if (hero != null && !hero.IsNullOrInactive()) Apply(hero, build);
                _registeredOn?.CustomRpc_SendMessageToClient(caller, new DreamforgeAppliedMsg
                {
                    heroNetId = hero != null ? hero.netId : 0,
                    summary = build.Encode(),
                });
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnBuild failed: " + ex);
            }
        }

        private void PruneAndApplyPending()
        {
            _scratch.Clear();
            foreach (var kv in _runtimes)
                if (kv.Key == null || !kv.Key.isActive) _scratch.Add(kv.Key);
            foreach (var h in _scratch) _runtimes.Remove(h);

            // 新しく生まれたキャラ（ラン開始・復帰）へ、届いている Build を付け直す。
            foreach (var kv in _builds)
            {
                var hero = kv.Key != null ? kv.Key.hero : null;
                if (hero == null || !hero.isActive) continue;
                if (!_runtimes.ContainsKey(hero)) Apply(hero, kv.Value);
            }
        }

        private void Apply(Hero hero, Build build)
        {
            if (!_runtimes.TryGetValue(hero, out var rt))
            {
                rt = new HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, Time.time) };
                _runtimes[hero] = rt;
            }
            RemoveBonuses(rt);
            rt.Powers.SetBuild(build);
            rt.BaseBonus = ToStatBonus(build);
            rt.DynBonus = new StatBonus();
            hero.Status.AddStatBonus(rt.BaseBonus);
            hero.Status.AddStatBonus(rt.DynBonus);
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
                    case Stat.Armor: s.armorFlat += v; break;
                    case Stat.HealthRegen: s.healthRegenFlat += v; break;
                    case Stat.Haste: s.abilityHasteFlat += v; break;
                    case Stat.MoveSpeedPct: s.movementSpeedPercentage += v; break;
                    case Stat.Tenacity: s.tenacityFlat += v; break;
                    case Stat.FireAmp: s.fireEffectAmpFlat += v; break;
                    case Stat.ColdAmp: s.coldEffectAmpFlat += v; break;
                    case Stat.LightAmp: s.lightEffectAmpFlat += v; break;
                    case Stat.DarkAmp: s.darkEffectAmpFlat += v; break;
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
            var dyn = p.Current(now);
            var d = rt.DynBonus;
            d.attackSpeedPercentage = dyn.AttackSpeedPct;
            d.attackDamagePercentage = dyn.AttackPct;
            d.abilityPowerPercentage = dyn.PowerPct;
            d.movementSpeedPercentage = dyn.MoveSpeedPct;
            d.armorFlat = dyn.Armor;
            hero.Status.CalculateStatsIfDirty();

            float shield = p.TakeBarrier(now, hero.maxHealth);
            if (shield > 0) hero.GiveShield(hero, shield, PowerRuntime.BarrierInterval);
            float heal = p.TakeSecondWind(now, hero.currentHealth, hero.maxHealth);
            if (heal > 0) hero.Heal(heal).Dispatch(hero);
        }

        /// <summary>鉄の輪の敵数と、共鳴の距離判定（0.25秒ごと）。</summary>
        private void ScanArea()
        {
            var list = new List<HeroRuntime>(_runtimes.Values);
            foreach (var rt in list)
                rt.Powers.NearbyEnemies = rt.Powers.Build.Get(Power.Bulwark) > 0 && Alive(rt.Hero) ? CountEnemiesNear(rt.Hero, 6f) : 0;

            var powers = new PowerRuntime[list.Count];
            for (int i = 0; i < list.Count; i++) powers[i] = list[i].Powers;
            PowerRuntime.DistributeResonance(powers, (i, j) =>
                Alive(list[i].Hero) && Alive(list[j].Hero)
                && Vector3.Distance(list[i].Hero.agentPosition, list[j].Hero.agentPosition) <= PowerRuntime.ResonanceRange);
            // MOD未導入の味方が近くにいても、自分の共鳴は全量にする。
            foreach (var rt in list)
            {
                int v = rt.Powers.Build.Get(Power.Resonance);
                if (v > 0 && rt.Powers.ResonanceSelf < v && AnyAllyNear(rt.Hero)) rt.Powers.ResonanceSelf = v;
            }
        }

        private static int CountEnemiesNear(Hero hero, float radius)
        {
            ListReturnHandle<Entity> handle;
            var found = DewPhysics.OverlapCircleAllEntities(out handle, hero.agentPosition, radius,
                (Func<Entity, bool>)(e => e != null && e.isActive && e.GetRelation(hero) == EntityRelation.Enemy));
            int n = found.Count;
            handle.Return();
            return n;
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

        private HeroRuntime RuntimeOf(Actor actor)
        {
            if (actor == null) return null;
            Entity e = actor as Entity ?? actor.firstEntity;
            return e is Hero h && _runtimes.TryGetValue(h, out var rt) ? rt : null;
        }

        private void OnDeath(EventInfoKill info)
        {
            try
            {
                if (!(info.victim is Monster)) return;
                RuntimeOf(info.actor)?.Powers.OnKill(Time.time);
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
                float reflect = rt.Powers.OnDamaged(Time.time, info.damage.amount, enemy);
                if (reflect > 0) hero.PureDamage(reflect, 0f).Dispatch(attacker);
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnTakeDamage " + ex.Message);
            }
        }

        private void OnAttackHit(EventInfoAttackHit info)
        {
            try
            {
                if (!(info.attacker is Hero hero) || !_runtimes.TryGetValue(hero, out var rt) || !Alive(hero)) return;
                var victim = info.victim;
                if (victim == null || !victim.isActive) return;
                float ratio = victim.maxHealth > 0 ? victim.currentHealth / victim.maxHealth : 1f;
                var r = rt.Powers.OnAttackHit(Time.time, hero.maxHealth, hero.Status.attackDamage, ratio);
                if (r.Heal > 0) hero.Heal(r.Heal).Dispatch(hero);
                if (r.ExecuteDamage > 0) hero.PureDamage(r.ExecuteDamage, 0f).Dispatch(victim);
                if (r.BlazeDamage > 0 && victim.isActive) hero.MagicDamage(r.BlazeDamage, 0f).Dispatch(victim);
            }
            catch (Exception ex)
            {
                Log.Error("Host: OnAttackHit " + ex.Message);
            }
        }
    }
}
