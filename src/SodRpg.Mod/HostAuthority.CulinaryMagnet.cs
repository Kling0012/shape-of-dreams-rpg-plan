using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    /// <summary>
    /// 本体の「料理のエッセンス」（Gem_L_Culinary）が落とす食材を、近くの生存している旅人へ吸い寄せる。
    /// ホストだけが動かす。食材は型名（Pickup_*Ingredient* など。CulinaryMagnet.IsIngredientTypeName）で見分け、
    /// 落ちてから SpawnDelay 秒後に、StartRadius 内で最も近い旅人へ向けて位置を進める。旅人の位置に着いたあとの
    /// 拾得は本体の判定に任せる（吸い寄せは食材の位置を動かすだけで、拾得処理や効果には触れない）。
    /// 食材の型名が想定と違うと動かないので、「Pickup_」で始まる型を初めて見たときにログへ出す。
    /// </summary>
    internal sealed partial class HostAuthority
    {
        private sealed class MagnetState
        {
            internal float Born;
            internal float Pulled;
        }

        private readonly Dictionary<Actor, MagnetState> _magnets = new Dictionary<Actor, MagnetState>();
        private readonly List<Actor> _magnetScratch = new List<Actor>();
        private readonly List<MagnetTarget> _magnetTargets = new List<MagnetTarget>();
        private readonly HashSet<string> _pickupTypesSeen = new HashSet<string>(StringComparer.Ordinal);
        private float _lastMagnetTick = -1f;

        private void TrackCulinaryPickup(Actor actor)
        {
            if (actor == null) return;
            string type = actor.GetType().Name;
            if (!type.StartsWith("Pickup_", StringComparison.Ordinal)) return;
            bool ingredient = CulinaryMagnet.IsIngredientTypeName(type);
            if (_pickupTypesSeen.Add(type))
                Log.Info("culinary magnet: pickup type " + type + (ingredient ? " -> pulled toward travelers" : " -> ignored"));
            if (ingredient) _magnets[actor] = new MagnetState { Born = Time.time };
        }

        private void UntrackCulinaryPickup(Actor actor)
        {
            if (actor != null) _magnets.Remove(actor);
        }

        private void ReleaseCulinaryMagnets()
        {
            _magnets.Clear();
            _lastMagnetTick = -1f;
        }

        /// <summary>毎ティック：待ち時間を過ぎた食材を、最も近い生存旅人へ近づける。</summary>
        private void StageCulinaryMagnet()
        {
            float now = _tickNow;
            float dt = _lastMagnetTick < 0f ? 0f : Mathf.Min(now - _lastMagnetTick, 0.25f);
            _lastMagnetTick = now;
            if (_magnets.Count == 0 || dt <= 0f) return;

            _magnetTargets.Clear();
            foreach (var hero in _runtimes.Keys)
            {
                if (!Alive(hero)) continue;
                var p = hero.agentPosition;
                _magnetTargets.Add(new MagnetTarget(p.x, p.y, p.z));
            }

            _magnetScratch.Clear();
            foreach (var actor in _magnets.Keys) _magnetScratch.Add(actor);
            foreach (var actor in _magnetScratch)
            {
                // 拾われた・消えた食材は取り除く（Unity の破棄済みオブジェクトも null と等しい）。
                if (actor == null || !actor.gameObject.activeInHierarchy) { _magnets.Remove(actor); continue; }
                var state = _magnets[actor];
                if (now - state.Born < CulinaryMagnet.SpawnDelay || _magnetTargets.Count == 0) { state.Pulled = 0f; continue; }
                var here = actor.transform.position;
                int index = CulinaryMagnet.SelectTarget(here.x, here.y, here.z, _magnetTargets);
                if (index < 0) { state.Pulled = 0f; continue; }
                var next = CulinaryMagnet.Step(here.x, here.y, here.z, _magnetTargets[index], dt, state.Pulled);
                state.Pulled += dt;
                actor.transform.position = new Vector3(next.X, next.Y, next.Z);
            }
        }
    }
}
