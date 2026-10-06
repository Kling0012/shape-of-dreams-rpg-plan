using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Transient v1.29 state; the host supplies facts and applies returned effects without chaining.</summary>
    public sealed partial class PowerRuntime
    {
        public const int ConditionalPowerCap = 120;
        private readonly Dictionary<int, float> _wanderReady = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _watchReady = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _relayReady = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _stardustReady = new Dictionary<int, float>();
        private readonly Dictionary<int, CritSequence> _wounds = new Dictionary<int, CritSequence>();
        private readonly HashSet<string> _medleyMemories = new HashSet<string>(StringComparer.Ordinal);
        private struct CritSequence { public int Count; public float Last; }
        private int? _lastBasicTarget;
        private int _focusHits, _lastEnemyCount, _pilingLuck, _memoryRepeat;
        private string _lastMemory;
        private float _lastBasicAt = float.NegativeInfinity, _stillSince = float.NaN;
        private float _walked, _toll, _medleyUntil = float.NegativeInfinity;
        private float _shieldbreakReady = float.NegativeInfinity, _breakoutReady = float.NegativeInfinity;
        private float _unbowedReady = float.NegativeInfinity, _lastCombat = float.NegativeInfinity;
        private float _stardustGlobalReady = float.NegativeInfinity, _prismReady = float.NegativeInfinity;
        private int _lastElement = -1;
        private float _spellsweepUntil = float.NegativeInfinity, _bareReady = float.NegativeInfinity;
        private float _shardReady = float.NegativeInfinity, _lifelineUntil = float.NegativeInfinity;
        private float _primedUntil = float.NegativeInfinity;
        private float _primedPercent;
        private bool _runUp, _openingReady;

        public float ShieldAmount { get; set; }
        public int NearbyEnemies8 { get; set; }
        public bool IsMoving { get; private set; }
        public bool AllNormalMemoriesCooling { get; private set; }
        public bool UltimateReady { get; private set; }
        public bool IsVanguard { get; set; }
        public bool IsRearguard { get; set; }
        public int LivingPartySize { get; set; } = 1;
        public int PilingLuckStacks => _pilingLuck;
        public float WalkedDistance => _walked;
        public bool RunUpReady => _runUp;
        private float _resonanceSharedBase;

        private int ConditionalAttributes(float now)
        {
            double actual = 0, before = 0;
            void Add(Power power, int contribution)
            {
                if (contribution <= 0) return;
                actual += contribution;
                int effective = Build.Get(power);
                before += effective <= 0 ? contribution : contribution * Math.Min(effective, Math.Max(0, Build.ConditionalBase(power))) / (double)effective;
            }
            Add(Power.Retaliation, now < _retaliationUntil ? NewValue(Power.Retaliation) : 0);
            Add(Power.Vigor, HealthRatio >= VigorThreshold ? NewValue(Power.Vigor) : 0);
            Add(Power.Resonance, ResonanceSelf);
            actual += Math.Max(0, ResonanceShared);
            before += _resonanceSharedBase > 0 ? Math.Min(ResonanceShared, _resonanceSharedBase) : Math.Max(0, ResonanceShared);
            Add(Power.UltimateSurge, now < _surgeUntil ? NewValue(Power.UltimateSurge) : 0);
            Add(Power.Overload, now < _overloadUntil ? NewValue(Power.Overload) : 0);
            Add(Power.CrystalResonance, NewValue(Power.CrystalResonance) * Math.Min(CrystalResonanceMaxTiers, Math.Max(0, GemQualityTotal) / 100));
            Add(Power.PreyPride, NewValue(Power.PreyPride) * Math.Min(PreyPrideMaxLevel, Math.Max(0, HuntLevel)));
            Add(Power.Devotion, NewValue(Power.Devotion) * DevotionStacks);
            Add(Power.LucidBoon, Math.Min(Content.PowerCap(Power.LucidBoon), NewValue(Power.LucidBoon) * Math.Min(LucidBoonMaxDreams, Math.Max(0, EvilDreamCount))));
            Add(Power.GleamingWard, ShieldAmount > 0 ? NewValue(Power.GleamingWard) : 0);
            Add(Power.Medley, NewValue(Power.Medley) * MedleyStacks(now));
            return (int)Math.Min(int.MaxValue, Math.Floor(actual * (before > ConditionalPowerCap ? ConditionalPowerCap / before : 1d) + .000001d));
        }

        private int NewValue(Power p)
        {
            Build.StarPowers.TryGetValue(p, out int stars);
            return Math.Max(0, Math.Min(Content.PowerCap(p), Build.Get(p) - stars)) + stars;
        }
        private static float Percent(float amount, float percent) => Math.Max(0, amount) * percent / 100f;
        private static bool Gate(Dictionary<int, float> gates, int id, float now, float interval)
        {
            if (gates.TryGetValue(id, out float ready) && now < ready) return false;
            if (gates.Count > 256)
            {
                var expired = new List<int>();
                foreach (var pair in gates) if (now >= pair.Value) expired.Add(pair.Key);
                foreach (int key in expired) gates.Remove(key);
            }
            gates[id] = now + interval;
            return true;
        }

        /// <summary>Call from regular movement sampling. distance excludes native dodge/dash/teleport movement.</summary>
        public void ObserveMovement(float now, bool moving, float walkedDistance, bool specialMovement = false)
        {
            IsMoving = moving || specialMovement;
            if (IsMoving) _stillSince = float.NaN;
            else if (float.IsNaN(_stillSince)) _stillSince = now;
            if (!specialMovement && moving && !_runUp && NewValue(Power.RunUp) > 0
                && !float.IsNaN(walkedDistance) && !float.IsInfinity(walkedDistance))
            {
                _walked = Math.Min(12f, _walked + Math.Max(0, walkedDistance));
                if (_walked >= 12f) _runUp = true;
            }
        }
        public bool ImmovableActive(float now) => !IsMoving && !float.IsNaN(_stillSince) && now - _stillSince >= 1f;
        public float OutgoingDamageAmplification(float now, bool normalMemory, bool summon)
        {
            if (summon) return now < _lifelineUntil ? NewValue(Power.Lifeline) / 100f : 0;
            int pct = ImmovableActive(now) ? NewValue(Power.ImmovableStance) : 0;
            if (normalMemory)
            {
                if (UltimateReady) pct += NewValue(Power.AceInHand);
                if (LivingPartySize > 1 && IsRearguard) pct += NewValue(Power.RearguardsWay);
            }
            return pct / 100f;
        }
        public float IncomingDamageReduction(float now)
        {
            if (!ImmovableActive(now)) return 0;
            Build.StarPowers.TryGetValue(Power.ImmovableStance, out int ranked);
            Build.UnrankedStarPowers.TryGetValue(Power.ImmovableStance, out int basis);
            return Math.Min(10, Math.Max(0, Build.Get(Power.ImmovableStance) - ranked + basis) / 2) / 100f;
        }
        public float ShieldGainAmplification() => IsVanguard ? NewValue(Power.VanguardsOath) / (LivingPartySize <= 1 ? 200f : 100f) : 0;
        public float PilingLuckCriticalChance => NewValue(Power.PilingLuck) * _pilingLuck / 100f;

        public void ObserveMemoryReadiness(int normalCount, bool allReady, bool allCooling, bool ultimateReady)
        {
            AllNormalMemoriesCooling = normalCount > 0 && allCooling;
            UltimateReady = ultimateReady;
            _openingReady = normalCount > 0 && allReady;
        }
        public int MedleyStacks(float now)
        {
            if (now >= _medleyUntil) _medleyMemories.Clear();
            return Math.Min(3, _medleyMemories.Count);
        }
        /// <summary>Call alongside the existing OnSkillUsed once per real use. Returns remaining-CD fraction for the used memory.</summary>
        public float OnNewMemoryUsed(float now, string memory, bool isMovement, bool isUltimate)
        {
            if (string.IsNullOrEmpty(memory)) return 0;
            if (isMovement) { _lastMemory = null; _memoryRepeat = 0; return 0; }
            bool normal = !isMovement && !isUltimate;
            float cut = 0;
            if (normal)
            {
                MedleyStacks(now);
                if (NewValue(Power.Medley) > 0 && !_medleyMemories.Contains(memory))
                {
                    _medleyMemories.Add(memory);
                    _medleyUntil = now + 12f;
                }
                if (NewValue(Power.Spellsweep) > 0) _spellsweepUntil = now + 5f;
                if (_openingReady) { cut += NewValue(Power.OpeningSalvo) / 100f; _openingReady = false; }
            }
            if (NewValue(Power.PileOn) > 0)
            {
                if (_lastMemory == memory) _memoryRepeat++;
                else { _lastMemory = memory; _memoryRepeat = 1; }
            }
            if (_memoryRepeat >= 3)
            {
                _memoryRepeat = 0;
                cut += NewValue(Power.PileOn) / 100f;
            }
            return Math.Min(1f, cut);
        }
        public float CoStarCooldownFraction(bool self) => NewValue(Power.CoStar) / (self ? 200f : 100f);
        public float TriumphSongHeal(float maxHealth) => Percent(maxHealth, NewValue(Power.TriumphSong));
        public float TakeWatchfulHand(float now, int allyId, float allyHealthRatio, float maxHealth)
        {
            int v = NewValue(Power.WatchfulHand);
            return v > 0 && allyHealthRatio > 0 && allyHealthRatio < .3f && maxHealth > 0 && Gate(_watchReady, allyId, now, 45f)
                ? Percent(maxHealth, v) : 0;
        }
        public float KindnessReturnsHeal(float actualAllyHealing) => Percent(actualAllyHealing, NewValue(Power.KindnessReturns));
        public float TakeRelayHand(float now, int allyId) => NewValue(Power.RelayHand) > 0 && Gate(_relayReady, allyId, now, 2f)
            ? NewValue(Power.RelayHand) / 100f : 0;
        public float TakeShieldbreak(float now, float shieldBefore, float maxHealth, bool brokenByEnemy)
        {
            int v = NewValue(Power.ShieldbreakBurst);
            if (v <= 0 || !brokenByEnemy || shieldBefore <= 0 || now < _shieldbreakReady) return 0;
            _shieldbreakReady = now + 6f;
            return Percent(Math.Max(shieldBefore, maxHealth), v);
        }
        public float SharedWardShield(float gainedShield, bool sharedShield = false) => sharedShield ? 0 : Percent(gainedShield, NewValue(Power.SharedWard));
        public float TakeBreakout(float now, int enemies6m, float maxHealth)
        {
            bool entered = _lastEnemyCount <= 3 && enemies6m >= 4;
            _lastEnemyCount = Math.Max(0, enemies6m);
            int v = NewValue(Power.Breakout);
            if (!entered || v <= 0 || maxHealth <= 0 || now < _breakoutReady) return 0;
            _breakoutReady = now + 10f;
            return Percent(maxHealth, v);
        }
        public float TakeTollOfGrudge(float actualHealthLost, float maxHealth)
        {
            int v = NewValue(Power.TollOfGrudge);
            if (v <= 0 || maxHealth <= 0 || actualHealthLost <= 0) return 0;
            _toll += actualHealthLost;
            int bursts = (int)(_toll / (maxHealth * .6f));
            _toll -= bursts * maxHealth * .6f;
            return Percent(maxHealth, v) * bursts;
        }
        public const float UnbowedMindDuration = 4f;
        public const float UnbowedMindCooldown = 8f;

        public float TakeUnbowedMind(float now, float maxHealth, bool enemySource, bool stun = true, bool immune = false)
        {
            int v = NewValue(Power.UnbowedMind);
            if (!enemySource || !stun || immune || v <= 0 || maxHealth <= 0 || now < _unbowedReady) return 0;
            _unbowedReady = now + UnbowedMindCooldown;
            return Percent(maxHealth, v);
        }
        /// <summary>Call only for actual outgoing or incoming hostile damage, including shield absorption.</summary>
        public float TakeReadyGuard(float now, float maxHealth)
        {
            bool ready = now - _lastCombat >= 5f;
            _lastCombat = now;
            return ready ? Percent(maxHealth, NewValue(Power.ReadyGuard)) : 0;
        }
        public float TakeShardBoon(float now, float maxHealth)
        {
            int v = NewValue(Power.ShardBoon);
            if (v <= 0 || maxHealth <= 0 || now < _shardReady) return 0;
            _shardReady = now + .3f;
            return maxHealth * v / 1000f;
        }
        public void OnOverheal(float now, float discardedAmount)
        {
            if (discardedAmount > 0 && NewValue(Power.Lifeline) > 0) _lifelineUntil = now + 4f;
        }
        public float CrystalCircuitCooldownFraction => NewValue(Power.CrystalCircuit) / 100f;
        public float DreamOmenCooldownFraction => NewValue(Power.DreamOmen) / 100f;
        public float DreamOmenShield(float maxHealth) => maxHealth * NewValue(Power.DreamOmen) / 200f;
        public float ApothecaryCooldownFraction => NewValue(Power.Apothecary) / 100f;
        public float ApothecaryAllyHeal(float potionHealing) => NewValue(Power.Apothecary) > 0 ? Math.Max(0, potionHealing) / 2f : 0;
        public float ReturningBladeCooldownFraction(bool attributedMemory) => attributedMemory ? NewValue(Power.ReturningBlade) / 100f : 0;
        public float PackFeastShield(float maxHealth, bool summonKill) => summonKill ? Percent(maxHealth, NewValue(Power.PackFeast)) : 0;
        public float DeathBloomDamage(float higher, bool killedByEnemy) => killedByEnemy ? Percent(higher, NewValue(Power.DeathBloom)) : 0;
        public float SpilloverDamage(float finalHitDamage, float victimHealthBefore) => victimHealthBefore > 0
            ? Percent(Math.Max(0, finalHitDamage - victimHealthBefore), NewValue(Power.SpilloverStrike)) : 0;
        public float ElementalHarvestDamage(float higher, int elementTypes) => elementTypes >= 2
            ? Percent(higher, NewValue(Power.ElementalHarvest)) * Math.Min(4, elementTypes) : 0;
        public bool RollUmbralHeritage(int deadDarkStacks, double roll) => deadDarkStacks >= 2 && roll >= 0
            && roll < NewValue(Power.UmbralHeritage) / 100d;
        public struct ElementPowerResult { public float Shield, LongestCooldownFraction; }
        /// <summary>element: Fire=0, Cold=1, Light=2, Dark=3. Call only for an actual native application.</summary>
        public ElementPowerResult OnElementApplied(float now, int victimId, int element, int before, int after, float maxHealth)
        {
            var result = new ElementPowerResult();
            if (element < 0 || element > 3 || after <= 0) return result;
            if (element == 2 && before < 5 && after >= 5 && NewValue(Power.StardustCycle) > 0
                && now >= _stardustGlobalReady && Gate(_stardustReady, victimId, now, 5f))
            {
                _stardustGlobalReady = now + 1f;
                result.LongestCooldownFraction = NewValue(Power.StardustCycle) / 100f;
            }
            if (_lastElement >= 0 && _lastElement != element && now >= _prismReady && NewValue(Power.PrismShift) > 0)
            {
                _prismReady = now + 1f;
                result.Shield = Percent(maxHealth, NewValue(Power.PrismShift));
            }
            _lastElement = element;
            return result;
        }
        public float BrittleIceDamage(float higher, bool critical, bool cold) => critical && cold ? Percent(higher, NewValue(Power.BrittleIce)) : 0;
        public float TakeWeakPointWound(float now, int victimId, float higher, bool critical)
        {
            int v = NewValue(Power.WeakPointWound);
            if (!critical || v <= 0) return 0;
            _wounds.TryGetValue(victimId, out var state);
            if (now - state.Last >= 6f) state.Count = 0;
            state.Last = now;
            state.Count++;
            float damage = state.Count >= 3 ? Percent(higher, v) : 0;
            if (state.Count >= 3) state.Count = 0;
            _wounds[victimId] = state;
            if (_wounds.Count > 256)
            {
                var expired = new List<int>();
                foreach (var pair in _wounds) if (now - pair.Value.Last >= 6f) expired.Add(pair.Key);
                foreach (int id in expired) _wounds.Remove(id);
            }
            return damage;
        }
        public void ForgetNewPowerTarget(int victimId)
        {
            _wounds.Remove(victimId);
            _wanderReady.Remove(victimId);
            _stardustReady.Remove(victimId);
        }
        public struct NewBasicResult
        {
            public float DirectDamage, SplashDamage, NormalCooldownSeconds;
            public int SlowPercent;
        }
        /// <summary>One call per primary basic hit after final damage. Other critical memory hits use TakeWeakPointWound/BrittleIceDamage separately.</summary>
        public NewBasicResult OnNewBasicHit(float now, int victimId, float higher, float finalDamage, bool isCrit, bool guaranteedCrit = false, bool primary = true, bool targetWithin8 = true)
        {
            var r = new NewBasicResult();
            if (!primary) return r;
            bool switched = _lastBasicTarget.HasValue && _lastBasicTarget.Value != victimId;
            if (switched && NewValue(Power.WanderersEdge) > 0 && Gate(_wanderReady, victimId, now, 1f))
                r.DirectDamage += Percent(higher, NewValue(Power.WanderersEdge));
            if (!_lastBasicTarget.HasValue || switched || now - _lastBasicAt >= 4f) _focusHits = 0;
            _lastBasicTarget = victimId; _lastBasicAt = now;
            if (NewValue(Power.FocusFire) > 0) _focusHits++;
            else _focusHits = 0;
            if (_focusHits >= 5) { _focusHits = 0; r.DirectDamage += Percent(higher, NewValue(Power.FocusFire)); }
            r.DirectDamage += Percent(ShieldAmount, NewValue(Power.ShieldBash));
            if (NearbyEnemies8 == 1 && targetWithin8) r.DirectDamage += Percent(higher, NewValue(Power.DuelistsWay));
            if (IsMoving) r.SlowPercent = NewValue(Power.StrafeShot);
            if (now < _spellsweepUntil) { r.SplashDamage += Percent(finalDamage, NewValue(Power.Spellsweep)); _spellsweepUntil = float.NegativeInfinity; }
            if (isCrit) r.SplashDamage += Percent(finalDamage, NewValue(Power.CritSplash));
            if (AllNormalMemoriesCooling && NewValue(Power.BareHandedPride) > 0 && now >= _bareReady)
            { r.NormalCooldownSeconds = NewValue(Power.BareHandedPride) / 10f; _bareReady = now + .3f; }
            if (!guaranteedCrit && NewValue(Power.PilingLuck) > 0) _pilingLuck = isCrit ? 0 : Math.Min(10, _pilingLuck + 1);
            return r;
        }
        public void PrimeNextBasic(float now, float percent, float duration = 5f)
        {
            if (!Gimmicks.Finite(now) || !Gimmicks.Finite(percent) || !Gimmicks.Finite(duration) || percent <= 0 || duration <= 0) return;
            if (now >= _primedUntil) _primedPercent = 0;
            _primedPercent = Math.Max(_primedPercent, Math.Min(120f * (float)StarDamageScaling.Multiplier(Build.SpentStarPoints), percent));
            _primedUntil = now + Math.Min(5f * (1f + Gimmicks.MaxParameterPercent / 100f), duration);
        }
        private void TakeLargestNextBasic(float now, float higher, ref HitResult result)
        {
            int echo = now < _echoUntil ? NewValue(Power.EchoingDodge) : 0;
            int shadow = now < _shadowStepUntil ? NewValue(Power.ShadowStep) : 0;
            int run = _runUp ? NewValue(Power.RunUp) : 0;
            float primed = now < _primedUntil ? _primedPercent : 0;
            float best = Math.Max(Math.Max(echo, shadow), Math.Max(run, primed));
            if (best <= 0) return;
            if (echo == best) { result.EchoDamage = Percent(higher, best); _echoUntil = float.NegativeInfinity; }
            else if (shadow == best) { result.ShadowStepDamage = Percent(higher, best); _shadowStepUntil = float.NegativeInfinity; }
            else if (run == best) { result.RunUpDamage = Percent(higher, best); _runUp = false; _walked = 0; }
            else { result.PrimedDamage = Percent(higher, best); _primedUntil = float.NegativeInfinity; _primedPercent = 0; }
        }
        private void ResetNewPowersForZone()
        {
            _wanderReady.Clear(); _stardustReady.Clear(); _wounds.Clear();
            // Ally cooldowns survive travel: the same living hero must still wait the full interval.
            _medleyMemories.Clear(); _lastBasicTarget = null; _focusHits = _lastEnemyCount = _pilingLuck = _memoryRepeat = 0;
            _lastMemory = null; _walked = _toll = 0; _runUp = _openingReady = false; _lastElement = -1;
            _stillSince = float.NaN;
            _lastBasicAt = _medleyUntil = _spellsweepUntil = _lifelineUntil = _primedUntil = float.NegativeInfinity;
            _lastCombat = float.NegativeInfinity;
        }
        private void RetainEquippedNewPowerState()
        {
            if (NewValue(Power.RunUp) <= 0) { _runUp = false; _walked = 0; }
            if (NewValue(Power.Medley) <= 0) { _medleyMemories.Clear(); _medleyUntil = float.NegativeInfinity; }
            if (NewValue(Power.Spellsweep) <= 0) _spellsweepUntil = float.NegativeInfinity;
            if (NewValue(Power.PileOn) <= 0) { _lastMemory = null; _memoryRepeat = 0; }
            if (NewValue(Power.FocusFire) <= 0) _focusHits = 0;
            if (NewValue(Power.PilingLuck) <= 0) _pilingLuck = 0;
            if (NewValue(Power.WeakPointWound) <= 0) _wounds.Clear();
            if (NewValue(Power.Lifeline) <= 0) _lifelineUntil = float.NegativeInfinity;
            if (NewValue(Power.TollOfGrudge) <= 0) _toll = 0;
        }
    }
}
