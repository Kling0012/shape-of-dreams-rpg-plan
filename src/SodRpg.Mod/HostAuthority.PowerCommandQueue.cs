using System;
using System.Collections.Generic;
using System.Diagnostics;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        internal struct PowerCommand
        {
            internal Entity Victim, Except;
            internal Vector3 Center;
            internal float Radius, Amount;
            internal int MaxTargets;
            internal bool Magic, Pure, Umbral;
            internal Action Callback;
            internal float QueuedAt;
        }

        internal sealed class PowerCommandQueue
        {
            private readonly List<PowerCommand> _items = new List<PowerCommand>();
            internal int Count => _items.Count;
            internal PowerCommand this[int index] => _items[index];
            internal void Add(PowerCommand command) { command.QueuedAt = Time.unscaledTime; _items.Add(command); }
            // Less frequent preparation callbacks share the same FIFO as damage.
            internal void Add(Action callback) => Add(new PowerCommand { Callback = callback });
            internal void Clear() => _items.Clear();
            internal void RemoveRange(int index, int count) => _items.RemoveRange(index, count);

            // 第2段: 予算の段階的な広げ・戻しの状態。flush で捌き切れなかった残りの寿命を測る。
            internal int Level;
            internal float BacklogSince = -1f;
            internal float DrainedSince = -1f;
            internal void ResetBudget() { Level = 0; BacklogSince = -1f; DrainedSince = -1f; }
        }

        // 第2段: 重いフレームで追加効果の予約を切り捨てず、残りを次フレームへ回す予算。
        // 件数と経過時間の両方で区切り、持ち越しが 0.6 秒続き続けば一段階広げる（最大4段階）。
        // 捌き切った状態が 3 秒続けば一段階戻す。通常時（予算内に収まる）は毎フレーム全件処理のまま。
        private static class PowerFlushBudget
        {
            private const int BaseItems = 64;
            private const int MaxLevel = 3;
            private const float WidenAfterSeconds = 0.6f;
            private const float NarrowAfterSeconds = 3f;

            internal static int Items(PowerCommandQueue queue)
                => BaseItems << Math.Min(queue.Level, MaxLevel);

            internal static long Ticks(PowerCommandQueue queue)
                => Stopwatch.Frequency * (3 << Math.Min(queue.Level, MaxLevel)) / 2000;

            internal static void Observe(PowerCommandQueue queue, int leftover)
            {
                float now = Time.unscaledTime;
                if (leftover > 0)
                {
                    // #1029: 持ち越しを観測した時点で空だった期間の記録を解除し、
                    // 次に捌き切った時刻から 3 秒を数え直す（直後の縮小を防ぐ）。
                    queue.DrainedSince = -1f;
                    if (queue.BacklogSince < 0f) queue.BacklogSince = now;
                    else if (now - queue.BacklogSince >= WidenAfterSeconds && queue.Level < MaxLevel)
                    {
                        queue.Level++;
                        queue.BacklogSince = now;
                    }
                }
                else
                {
                    queue.BacklogSince = -1f;
                    if (queue.DrainedSince < 0f) queue.DrainedSince = now;
                    else if (queue.Level > 0 && now - queue.DrainedSince >= NarrowAfterSeconds)
                    {
                        queue.Level--;
                        queue.DrainedSince = now;
                    }
                }
            }
        }

        private void DispatchPowerCommand(HeroRuntime rt, PowerCommand command)
        {
            if (command.Callback != null) { command.Callback(); return; }
            if (command.Umbral) { SpreadUmbral(rt, command.Victim, command.Center); return; }
            EnterGenerated(rt.Hero);
            try
            {
                if (command.Radius > 0f)
                {
                    if (!command.Pure)
                        DamageAround(rt.Hero, command.Center, command.Radius, command.Amount,
                            command.Except, command.MaxTargets, command.Magic, gimmick: true);
                    else
                    {
                        var found = DewPhysics.OverlapCircleAllEntities(out var handle,
                            command.Center, command.Radius, EnemyFilter, rt.Hero);
                        try
                        {
                            int count = 0;
                            foreach (var enemy in found)
                            {
                                if (enemy == command.Except || enemy == null || !enemy.isActive || enemy.currentHealth <= 0f) continue;
                                if (count++ >= command.MaxTargets) break;
                                rt.Hero.PureDamage(command.Amount, 0f).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(enemy);
                            }
                        }
                        finally { handle.Return(); }
                    }
                }
                else if (command.Victim != null && command.Victim.isActive && command.Victim.currentHealth > 0f
                    && command.Victim.GetRelation(rt.Hero) == EntityRelation.Enemy)
                {
                    var damage = command.Pure ? rt.Hero.PureDamage(command.Amount, 0f)
                        : command.Magic ? rt.Hero.MagicDamage(command.Amount, 0f) : rt.Hero.PhysicalDamage(command.Amount, 0f);
                    damage.SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(command.Victim);
                }
            }
            finally { ExitGenerated(rt.Hero); }
        }
    }
}
