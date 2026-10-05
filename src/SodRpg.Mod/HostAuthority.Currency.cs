using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    using Power = SodRpg.Core.Game.Power;

    /// <summary>
    /// v1.32 A「巡る富」。ホストが旅人（DewPlayer）ごとに、星図の通貨の効果を本体へ適用する。
    /// 撃破ゴールドは monsterKillGoldMultiplier への加減算（本体の「富の蓄え」と同じ方式）、
    /// 夢のダストは Pickup_DreamDust.onGiveDreamDust（本体の「童話の神」と同じ手法）、
    /// エリート・ボスの追加ゴールドは撃破時にホストが EarnGold で上乗せする。
    /// </summary>
    internal sealed partial class HostAuthority
    {
        private readonly AdditiveMultiplierLedger<DewPlayer> _killGoldLedger = new AdditiveMultiplierLedger<DewPlayer>();
        private readonly List<DewPlayer> _currencyScratch = new List<DewPlayer>();
        private readonly HashSet<DewPlayer> _currencySeen = new HashSet<DewPlayer>();
        private readonly Dictionary<Pickup_DreamDust, Action<DewPlayer, int>> _dustHandlers = new Dictionary<Pickup_DreamDust, Action<DewPlayer, int>>();
        private readonly List<Pickup_DreamDust> _dustScratch = new List<Pickup_DreamDust>();
        private float _nextCurrencySync;

        /// <summary>0.25秒ごと：各人の Build から撃破ゴールドの倍率を合わせる。去った人・Build が変わった人は正確に元へ戻す。</summary>
        private void SyncCurrency(float now)
        {
            if (now < _nextCurrencySync) return;
            _nextCurrencySync = now + 0.25f;
            _currencySeen.Clear();
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (ReferenceEquals(player, null) || !player.isHumanPlayer) continue;
                _currencySeen.Add(player);
                float delta = !InfinityMode.Enabled && _builds.TryGetValue(player, out var received)
                    ? CurrencyStars.KillGoldMultiplierDelta(received.Build) : 0f;
                if (_killGoldLedger.AppliedDelta(player) == delta) continue;
                try
                {
                    player.monsterKillGoldMultiplier = delta == 0f
                        ? _killGoldLedger.Release(player, player.monsterKillGoldMultiplier)
                        : _killGoldLedger.Apply(player, player.monsterKillGoldMultiplier, delta);
                }
                catch (Exception ex) { Log.Error("Host: kill gold multiplier " + ex); }
            }
            _currencyScratch.Clear();
            foreach (var player in _killGoldLedger.Keys) if (!_currencySeen.Contains(player)) _currencyScratch.Add(player);
            foreach (var player in _currencyScratch) ForgetKillGold(player);
        }

        private void ForgetKillGold(DewPlayer player)
        {
            try
            {
                // A destroyed player has nothing left to restore.
                if (!ReferenceEquals(player, null) && player != null)
                    player.monsterKillGoldMultiplier = _killGoldLedger.Release(player, player.monsterKillGoldMultiplier);
            }
            catch (Exception ex) { Log.Error("Host: restore kill gold multiplier " + ex); }
            _killGoldLedger.Forget(player);
        }

        /// <summary>登録の解除・MODの終了：本体の倍率を加えた分だけ戻し、夢のダストの購読を外す。</summary>
        private void ReleaseCurrency()
        {
            _currencyScratch.Clear();
            foreach (var player in _killGoldLedger.Keys) _currencyScratch.Add(player);
            foreach (var player in _currencyScratch) ForgetKillGold(player);
            _killGoldLedger.Clear();
            _dustScratch.Clear();
            foreach (var pickup in _dustHandlers.Keys) _dustScratch.Add(pickup);
            foreach (var pickup in _dustScratch) UnhookDreamDust(pickup);
            _dustHandlers.Clear();
            _nextCurrencySync = 0f;
        }

        // ───── 夢のダスト ─────

        private void HookDreamDust(Pickup_DreamDust pickup)
        {
            if (pickup == null) return;
            UnhookDreamDust(pickup);
            Action<DewPlayer, int> handler = (player, amount) => OnDreamDustGiven(pickup, player, amount);
            pickup.onGiveDreamDust += handler;
            _dustHandlers[pickup] = handler;
        }

        private void UnhookDreamDust(Pickup_DreamDust pickup)
        {
            if (pickup != null && _dustHandlers.TryGetValue(pickup, out var handler))
            {
                _dustHandlers.Remove(pickup);
                try { pickup.onGiveDreamDust -= handler; } catch (Exception) { }
            }
        }

        /// <summary>
        /// 自分で拾った分だけに上乗せする（本体の「童話の神」と同じ。ほかのプレイヤーの分は無視）。仲間からの贈り物は拾得の時点の
        /// isGivenByOtherPlayer で除く。MODの取引の夢のダストは Pickup を通らないので、この経路には来ない。
        /// </summary>
        private void OnDreamDustGiven(Pickup_DreamDust pickup, DewPlayer player, int amount)
        {
            try
            {
                if (InfinityMode.Enabled) return; // Conservative cap: no MOD-created native currency.
                if (ReferenceEquals(player, null) || player == null || amount <= 0 || !_builds.TryGetValue(player, out var received)) return;
                int bonus = CurrencyStars.DreamDustBonusForPickup(received.Build, CurrencyStars.IsDelving(ClientSession.HostRun),
                    amount, pickup.isGivenByOtherPlayer, _rng.NextDouble());
                if (bonus > 0) player.EarnDreamDust(bonus);
            }
            catch (Exception ex) { Log.Error("Host: dream dust bonus " + ex); }
        }

        // ───── エリート・ボスの撃破ゴールド ─────

        /// <summary>
        /// 本体のゴールドは宝珠として落ち、拾う時点では「どの敵の分か」が分からない（Pickup_BaseGoldOrb.GrantGold）。
        /// 撃破の瞬間にホストが、その敵の撃破ゴールド（GameManager.GetKillGoldAmount と同じ式。乱数のぶれは別に引き直す）を
        /// 人数で割り、各人の倍率を掛けた額の「黄金の嗅覚」%分を EarnGold で上乗せする。
        /// </summary>
        private void GrantEliteKillGold(Monster monster)
        {
            if (InfinityMode.Enabled) return;
            if (monster == null || (monster.type != Monster.MonsterType.MiniBoss && monster.type != Monster.MonsterType.Boss)
                || monster.disableLoot) return;
            var status = monster.Status;
            if (status != null && status.TryGetStatusEffect<Se_HunterBuff>(out var hunter) && !hunter.enableGoldAndExpDrops) return;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            if (game == null) return;
            bool any = false;
            foreach (var player in DewPlayer.gamePlayers)
                if (!ReferenceEquals(player, null) && player.isHumanPlayer && _builds.TryGetValue(player, out var b)
                    && CurrencyStars.EliteKillGoldPercent(b.Build) > 0) { any = true; break; }
            if (!any) return;
            int killGold = game.GetKillGoldAmount(monster);
            int players = DewPlayer.gamePlayers.Count;
            float profile = DewBuildProfile.current != null ? DewBuildProfile.current.killGoldMultiplier : 1f;
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (ReferenceEquals(player, null) || !player.isHumanPlayer || !_builds.TryGetValue(player, out var received)) continue;
                int percent = CurrencyStars.EliteKillGoldPercent(received.Build);
                if (percent <= 0) continue;
                double extra = CurrencyStars.EliteKillGoldBonus(killGold, players, profile, player.monsterKillGoldMultiplier, percent);
                int gold = CurrencyStars.RandomRound(extra, _rng.NextDouble());
                if (gold > 0) player.EarnGold(gold);
            }
        }
    }
}
