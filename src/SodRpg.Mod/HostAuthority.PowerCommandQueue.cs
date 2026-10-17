using System;
using System.Collections.Generic;
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
        }

        internal sealed class PowerCommandQueue
        {
            private readonly List<PowerCommand> _items = new List<PowerCommand>();
            internal int Count => _items.Count;
            internal PowerCommand this[int index] => _items[index];
            internal void Add(PowerCommand command) => _items.Add(command);
            // Less frequent preparation callbacks share the same FIFO as damage.
            internal void Add(Action callback) => _items.Add(new PowerCommand { Callback = callback });
            internal void Clear() => _items.Clear();
            internal void RemoveRange(int index, int count) => _items.RemoveRange(index, count);
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
