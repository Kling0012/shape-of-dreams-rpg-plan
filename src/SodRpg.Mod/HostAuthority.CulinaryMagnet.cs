using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

        private bool _culinaryDumped;

        /// <summary>
        /// 食材の所持上限（カンスト）の実装を調べるための記録。料理のエッセンスが現れたとき、
        /// 数値・真偽値の項目の名前と値を1回だけログへ出す（値は変えない）。
        /// </summary>
        private void DumpCulinaryEssence(Actor actor)
        {
            if (_culinaryDumped) return;
            _culinaryDumped = true;
            try
            {
                var sb = new StringBuilder("culinary essence members of " + actor.GetType().FullName + ":");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                for (var t = actor.GetType(); t != null && t != typeof(object) && t.Namespace == actor.GetType().Namespace; t = t.BaseType)
                {
                    if (t.Name == "Gem" || t.Name == "Actor") break;
                    foreach (var f in t.GetFields(flags | BindingFlags.DeclaredOnly))
                        if (f.FieldType.IsPrimitive || f.FieldType == typeof(string))
                            sb.Append("\n  field ").Append(t.Name).Append('.').Append(f.Name).Append(" : ").Append(f.FieldType.Name)
                              .Append(f.IsStatic ? " (static) = " : " = ").Append(f.GetValue(f.IsStatic ? null : actor));
                    foreach (var pr in t.GetProperties(flags | BindingFlags.DeclaredOnly))
                        if (pr.GetIndexParameters().Length == 0 && pr.CanRead && (pr.PropertyType.IsPrimitive || pr.PropertyType == typeof(string)))
                            sb.Append("\n  property ").Append(t.Name).Append('.').Append(pr.Name).Append(" : ").Append(pr.PropertyType.Name)
                              .Append(" = ").Append(pr.GetValue(pr.GetMethod.IsStatic ? null : actor));
                }
                Log.Info(sb.ToString());
            }
            catch (Exception ex) { Log.Warn("culinary essence dump failed: " + ex.Message); }
        }

        private void TrackCulinaryPickup(Actor actor)
        {
            if (actor == null) return;
            string type = actor.GetType().Name;
            if (type == "Gem_L_Culinary") DumpCulinaryEssence(actor);
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
