using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // Actor.DealDamage (Dew.Core/Actor.cs:900) exits at or below this amount.
        // Keep the lethal floor above that exit; native death uses HP <= 0.00001f (:1054).
        // Recheck both native thresholds when updating the game reference.
        private const double NativeSmallDamageCutoff = 0.0001;
        private const double BossShareLethalDamageFloor = 2 * NativeSmallDamageCutoff;

        private sealed class BossShares
        {
            internal readonly DewPlayer[] Players = new DewPlayer[4];
            internal readonly double[] Solo = new double[4], Capacity = new double[4], Remaining = new double[4], Dynamic = new double[4];
            internal double Multiplier, BaseMultiplier, Maximum;
            internal bool Failed;
        }

        private readonly Dictionary<Monster, StatBonus> _bossNativeHealth = new Dictionary<Monster, StatBonus>();

        internal bool RecordBossNativeHealth(Monster monster, StatBonus bonus)
        {
            try
            {
                if (monster == null || !monster.IsAnyBoss() || bonus == null || OwnershipBossParent(monster)) return false;
                _bossNativeHealth[monster] = bonus;
                PrepareEnemyOwnership(monster);
                FinalizeEnemyOwnership(monster);
                monster.Status.MarkStatsDirty();
                return HasBossShares(monster);
            }
            catch (Exception ex) { Log.Warn("Host: boss solo health capture ignored: " + ex.Message); return false; }
        }

        private BossShares PrepareBossShares(EnemyOwnership state)
        {
            if (state.Shares != null) return state.Shares.Failed ? null : state.Shares;
            if (!OwnershipRunning() || !NativeAttributedHpDamage.BossShareSupported || !BossShareDamageScope.NotificationSupported
                || !(state.Enemy is Monster monster) || !monster.IsAnyBoss()
                || !_bossNativeHealth.ContainsKey(monster) || OwnershipBossParent(monster)) return null;
            var shares = new BossShares();
            state.Shares = shares;
            try
            {
                int count = 0;
                double sum = 0, baseSum = 0;
                foreach (var player in DewPlayer.gamePlayers)
                {
                    if (!OwnershipConnected(player)) continue;
                    if (count == shares.Players.Length) throw new InvalidOperationException("Boss shares support up to four travelers.");
                    double factor = DynamicFactor(player);
                    double baseline = OwnershipBasePressure(player, sharedEnemy: true).HealthMultiplier;
                    double solo = baseline * factor;
                    if (!(solo > 0) || double.IsInfinity(solo)) throw new InvalidOperationException("Invalid solo pressure.");
                    shares.Players[count] = player;
                    shares.Dynamic[count] = factor;
                    shares.Solo[count++] = solo;
                    sum += solo;
                    baseSum += baseline;
                }
                if (count < 2) { state.Shares = null; return null; }
                shares.Multiplier = sum * CoopBalance.Ownership.BossShareFactor;
                shares.BaseMultiplier = baseSum * CoopBalance.Ownership.BossShareFactor;
                for (int i = 0; i < count; i++) shares.Capacity[i] = shares.Remaining[i] = shares.Solo[i] / sum;
                return shares;
            }
            catch (Exception ex) { FailBossShares(state, ex); return null; }
        }

        private bool OwnershipBossParent(Entity entity)
        {
            Actor parent = entity.parentActor;
            for (int depth = 0; parent != null && depth < 128; depth++, parent = parent.parentActor)
                if (parent is Entity ancestor && (ancestor.IsAnyBoss()
                    || _enemyOwnership.TryGetValue(ancestor, out var state) && state.Excluded)) return true;
            return parent != null;
        }

        private void FailBossShares(EnemyOwnership state, Exception ex)
        {
            if (state == null) { Log.Warn("Host: boss shares ignored: " + ex.Message); return; }
            if (state.Shares == null) state.Shares = new BossShares();
            if (state.Shares.Failed) return;
            state.Shares.Failed = true;
            state.Enemy?.Status?.MarkStatsDirty();
            Log.Warn("Host: boss shares disabled for this enemy; room HP retained: " + ex.Message);
        }

        private bool TryBossShares(Entity enemy, out EnemyOwnership state, out BossShares shares)
        {
            state = null;
            shares = null;
            if (enemy == null || !_enemyOwnership.TryGetValue(enemy, out state)) return false;
            // One OwnershipRunning() evaluation per damage event: cached shares check it here,
            // and the cold path is gated by the same check at the top of PrepareBossShares
            // (kept for ScanBossShares), which bails out without side effects when not running.
            if (state.Shares != null)
            {
                if (!OwnershipRunning() || state.Shares.Failed) return false;
                shares = state.Shares;
                return true;
            }
            shares = PrepareBossShares(state);
            return shares != null;
        }

        private void OverrideBossShareHealth(Entity enemy, ref float multiplier)
        {
            if (!_enemyOwnership.TryGetValue(enemy, out var state)) return;
            try
            {
                if (!TryBossShares(enemy, out _, out var shares)) return;
                var monster = (Monster)enemy;
                // Native multiplayer HP is additive with other StatBonuses. Remove exactly its
                // captured contribution before multiplying the sum of the solo pressures.
                double native = _bossNativeHealth[monster].maxHealthPercentage;
                double denominator = 100d + monster.Status.bonusStats.maxHealthPercentage;
                double soloBonus = denominator - native;
                double multiplierWithDynamic = DynamicApplicationRunning() ? shares.Multiplier : shares.BaseMultiplier;
                double result = multiplierWithDynamic * soloBonus / denominator;
                if (!(result > 0) || double.IsInfinity(result) || result > float.MaxValue)
                    throw new InvalidOperationException("Invalid boss health multiplier.");
                multiplier = (float)result;
            }
            catch (Exception ex) { FailBossShares(state, ex); }
        }

        private static double BossRemaining(BossShares shares)
        {
            double total = 0;
            for (int i = 0; i < shares.Players.Length; i++) total += shares.Remaining[i];
            return total;
        }

        private static void ReconcileBossShares(EnemyOwnership state, BossShares shares)
        {
            double maximum = state.Enemy.maxHealth;
            double health = Math.Max(0, state.Enemy.currentHealth);
            if (!(maximum > 0) || double.IsNaN(health) || double.IsInfinity(maximum))
                throw new InvalidOperationException("Invalid boss HP.");
            if (shares.Maximum != maximum)
            {
                double scale = shares.Maximum > 0 ? maximum / shares.Maximum : maximum;
                for (int i = 0; i < shares.Players.Length; i++)
                { shares.Capacity[i] *= scale; shares.Remaining[i] *= scale; }
                shares.Maximum = maximum;
            }
            double remaining = BossRemaining(shares);
            // Native healing, direct HP changes and stat recalculation keep the same distribution.
            if (remaining > 0)
            {
                double scale = health / remaining;
                for (int i = 0; i < shares.Players.Length; i++) shares.Remaining[i] *= scale;
            }
            else if (health > 0)
                for (int i = 0; i < shares.Players.Length; i++)
                    shares.Remaining[i] = health * shares.Capacity[i] / maximum;
        }

        private static int LargestBossShare(BossShares shares, double[] planned)
        {
            int best = -1;
            double fraction = -1;
            for (int i = 0; i < shares.Players.Length; i++)
            {
                double remaining = shares.Remaining[i] - planned[i];
                double ratio = shares.Capacity[i] > 0 ? remaining / shares.Capacity[i] : 0;
                if (shares.Players[i] != null && remaining > 0 && ratio > fraction)
                { best = i; fraction = ratio; }
            }
            return best;
        }

        private double PlanBossDamage(BossShares shares, DewPlayer player, double budget, double[] planned)
        {
            Array.Clear(planned, 0, planned.Length);
            int own = Array.IndexOf(shares.Players, player);
            double total = 0;
            if (player != null && own >= 0)
            {
                double take = Math.Min(budget, shares.Remaining[own]);
                planned[own] = take;
                total += take;
                budget -= take;
            }
            double factor = player == null ? 1 : OwnershipHelpFactor();
            for (int step = 0; step < shares.Players.Length && budget > 0; step++)
            {
                int target = LargestBossShare(shares, planned);
                if (target < 0) break;
                if (!(factor > 0)) throw new InvalidOperationException("Invalid boss help factor.");
                double take = Math.Min(budget * factor, shares.Remaining[target] - planned[target]);
                planned[target] += take;
                total += take;
                budget = Math.Max(0, budget - take / factor);
            }
            return total;
        }

        internal bool HasBossShares(Entity enemy)
        {
            EnemyOwnership state = null;
            try { return TryBossShares(enemy, out state, out _); }
            catch (Exception ex) { FailBossShares(state, ex); return false; }
        }

        private void ApplyBossShareReceivedDamage(Entity victim, Actor actor, ref DamageData damage)
        {
            var scope = BossShareDamageScope.Current;
            if (scope == null || scope.Victim != victim) return;
            EnemyOwnership state = null;
            var before = damage;
            try
            {
                if (!TryBossShares(victim, out state, out var shares)) return;
                victim.Status.CalculateStatsIfDirty();
                ReconcileBossShares(state, shares);
                if (damage.HasAttr(DamageAttribute.DamageShieldOnly) || damage.currentAmount <= 0) return;
                var player = OwnershipHuman(damage.actor ?? actor);
                float armor = victim.Status.finalStats.armor;
                double armorFactor = damage.HasAttr(DamageAttribute.IgnoreArmor) || damage.source == DamageData.SourceType.Pure
                    ? 1 : Mathf.Max(100f / (100f + armor), 0f);
                double amount = damage.currentAmount * armorFactor;
                if (!(amount > 0) || double.IsInfinity(amount)) return;
                double shield = damage.HasAttr(DamageAttribute.IgnoreShield) ? 0 : victim.Status.currentShield;
                double absorbed = shield > 0 ? damage.damageToShieldMultiplier > 0
                    ? Math.Min(amount, shield / damage.damageToShieldMultiplier) : amount : 0;
                double budget = Math.Max(0, amount - absorbed);
                double accepted = PlanBossDamage(shares, player, budget, scope.Planned);
                // Retain a little native overkill for a lethal plan below the small-hit exit.
                // Otherwise a floating-point HP remainder can be unkillable.
                // The HP setter still settles only the planned shares after immunity and shields.
                double retained = accepted + absorbed;
                if (accepted > 0 && LargestBossShare(shares, scope.Planned) < 0)
                    retained = Math.Max(retained, BossShareLethalDamageFloor);
                double keep = Math.Min(1, retained / amount);
                // Keep very small help factors representable without rounding 1 - keep to 1.
                if (keep <= 0) damage.ApplyReduction(1);
                else
                {
                    while (keep < 0.5) { damage.ApplyReduction(0.5f); keep *= 2; }
                    damage.ApplyReduction(1f - (float)keep);
                }
                scope.Player = player;
                scope.RawBudget = budget;
                scope.PlannedDamage = accepted;
                scope.Ready = true;
            }
            catch (Exception ex) { damage = before; scope.Ready = false; FailBossShares(state, ex); }
        }

        // Called at the existing native DealDamage HP setter, after immunity and shields.
        // No shares are consumed by a negated hit or by a shield-only hit.
        partial void CommitBossShareHealth(EntityStatus status, ref float health)
        {
            var scope = BossShareDamageScope.Current;
            if (scope == null || scope.Victim == null || scope.Victim.Status != status || !scope.Ready) return;
            scope.Ready = false;
            EnemyOwnership state = null;
            try
            {
                if (!TryBossShares(scope.Victim, out state, out var shares)) return;
                ReconcileBossShares(state, shares);
                double actual = Math.Max(0, status.currentHealth - health);
                double scale = scope.PlannedDamage > 0 ? Math.Min(1, actual / scope.PlannedDamage) : 0;
                // Re-plan after native attack effects: their nested damage may have finished our
                // own slice. Such overflow still gets the helper factor instead of a free transfer.
                double accepted = PlanBossDamage(shares, scope.Player, scope.RawBudget * scale, scope.Planned);
                if (accepted > actual && accepted > 0)
                    for (int i = 0; i < scope.Planned.Length; i++) scope.Planned[i] *= actual / accepted;
                for (int i = 0; i < shares.Players.Length; i++)
                    shares.Remaining[i] = Math.Max(0, shares.Remaining[i] - scope.Planned[i]);
                health = (float)Math.Max(0, BossRemaining(shares));
                scope.DamageCorrection = (float)Math.Max(0, actual - Math.Max(0, status.currentHealth - health));
            }
            catch (Exception ex) { FailBossShares(state, ex); }
        }

        private void ScanBossShares(EnemyOwnership state, bool running, DewPlayer departed = null)
        {
            if (!running) { state.Shares = null; return; }
            var shares = PrepareBossShares(state);
            if (shares == null) return;
            try
            {
                ReconcileBossShares(state, shares);
                double capacity = 0, remaining = 0, weight = 0;
                for (int i = 0; i < shares.Players.Length; i++)
                {
                    var player = shares.Players[i];
                    if (ReferenceEquals(player, null)) continue;
                    if (player == departed || !OwnershipConnected(player))
                    {
                        capacity += shares.Capacity[i]; remaining += shares.Remaining[i];
                        shares.Players[i] = null;
                        shares.Capacity[i] = shares.Remaining[i] = shares.Solo[i] = 0;
                    }
                    else weight += shares.Solo[i];
                }
                if (capacity <= 0) return;
                if (weight <= 0) throw new InvalidOperationException("No remaining boss share participants.");
                for (int i = 0; i < shares.Players.Length; i++)
                {
                    if (shares.Players[i] == null) continue;
                    double ratio = shares.Solo[i] / weight;
                    shares.Capacity[i] += capacity * ratio;
                    shares.Remaining[i] += remaining * ratio;
                }
            }
            catch (Exception ex) { FailBossShares(state, ex); }
        }

        private void ApplyBossPressureDamage(Entity victim, Actor actor, ref DamageData damage)
        {
            var before = damage;
            try
            {
                if (!(victim is Hero) || !OwnershipAvailable(OwnershipPart.Stray)) return;
                var player = OwnershipHuman(victim);
                var source = OwnershipSource(damage.actor ?? actor);
                if (player == null || source == null || !OwnershipExcluded(source)) return;
                // Normalize both sides through the helper: Infinity co-op excludes personal waypoints,
                // even when _pressure still contains the host waypoint before RefreshPressure runs.
                float factor = Mathf.Clamp((float)(OwnershipPressure(player, sharedEnemy: true).HealthMultiplier
                    / OwnershipBasePressure(null).HealthMultiplier),
                    CoopBalance.Ownership.BossDamageMin, CoopBalance.Ownership.BossDamageMax);
                if (factor <= 1) damage.ApplyReduction(1f - factor);
                else damage.ApplyAmplification(factor - 1f);
            }
            catch (Exception ex)
            {
                damage = before;
                FailOwnership(OwnershipPart.Stray, ex);
            }
        }
    }
}
