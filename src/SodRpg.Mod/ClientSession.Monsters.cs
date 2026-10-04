using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    internal static class MonsterCues
    {
        public static Color ColorFor(int cue)
        {
            switch (cue)
            {
                case 1: return new Color(1f, 0.7f, 0.1f);
                case 2: return new Color(0.8f, 0.35f, 0.05f);
                case 3: return new Color(0.1f, 0.7f, 1f);
                case 4: return new Color(0.1f, 0.9f, 0.25f);
                default: return Color.black;
            }
        }
    }

    internal sealed partial class ClientSession
    {
        private sealed class MonsterCueVisual
        {
            public int Cue;
            public float SeenAt;
            public Monster Monster;
            public EntityVisual Visual;
            public EntityColorModifier Color;
        }

        private readonly Dictionary<uint, MonsterCueVisual> _monsterCues = new Dictionary<uint, MonsterCueVisual>();
        private readonly List<uint> _monsterCueScratch = new List<uint>();

        private void OnMonsterCue(DreamforgeMonsterCueMsg msg)
        {
            if (msg == null || msg.netId == 0 || msg.cue < 0 || msg.cue > 4) return;
            if (!ObserveMonsterAuthority(msg.authorityGeneration)) return;
            if (msg.cue == 0)
            {
                RemoveMonsterCue(msg.netId);
                return;
            }
            if (!_monsterCues.TryGetValue(msg.netId, out var state))
            {
                if (_monsterCues.Count >= 300) ClearMonsterCues();
                state = new MonsterCueVisual();
                _monsterCues[msg.netId] = state;
            }
            state.Cue = msg.cue;
            state.SeenAt = Time.unscaledTime;
            // Handles messages arriving before spawn/model, not just already-visible monsters.
            if (state.Color != null) state.Color.emission = MonsterCues.ColorFor(state.Cue);
        }

        private void UpdateMonsterCues()
        {
            if (_monsterCues.Count == 0) return;
            _monsterCueScratch.Clear();
            foreach (var kv in _monsterCues)
            {
                var state = kv.Value;
                bool pending = state.Monster == null && Time.unscaledTime - state.SeenAt <= 10f;
                if (!NetworkClient.spawned.TryGetValue(kv.Key, out var identity) || identity == null)
                {
                    if (!pending) _monsterCueScratch.Add(kv.Key);
                    continue;
                }
                var m = identity.GetComponent<Monster>();
                if (m == null || !m.isActive || !m.isAlive)
                {
                    if (m == null || !pending) _monsterCueScratch.Add(kv.Key);
                    continue;
                }
                var visual = m.Visual;
                if (state.Monster == m && state.Visual == visual && state.Color != null) continue;
                if (visual == null || visual.model == null) continue;
                StopMonsterCue(state);
                state.Monster = m;
                state.Visual = visual;
                try
                {
                    state.Color = visual.GetNewColorModifier();
                    state.Color.emission = MonsterCues.ColorFor(state.Cue);
                }
                catch (Exception ex)
                {
                    LogVariantVisualFailure(ex);
                    _monsterCueScratch.Add(kv.Key);
                }
            }
            foreach (uint id in _monsterCueScratch) RemoveMonsterCue(id);
        }

        private void RemoveMonsterCue(uint id)
        {
            if (!_monsterCues.TryGetValue(id, out var state)) return;
            _monsterCues.Remove(id);
            StopMonsterCue(state);
        }

        private void StopMonsterCue(MonsterCueVisual state)
        {
            try { state.Color?.Stop(); }
            catch (Exception ex) { LogVariantVisualFailure(ex); }
            state.Color = null;
            state.Monster = null;
            state.Visual = null;
        }

        private void ClearMonsterCues()
        {
            foreach (var state in _monsterCues.Values) StopMonsterCue(state);
            _monsterCues.Clear();
            _monsterCueScratch.Clear();
        }
    }
}
