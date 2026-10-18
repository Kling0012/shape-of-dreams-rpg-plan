using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>Shared distance curve. State is transient and belongs to one ordered hero pair.</summary>
    public static class CoopDistance
    {
        public static string Description => Describe(Loc.Japanese);

        public static string Describe(bool japanese) => japanese
            ? $" 距離減衰が有効な場合、味方への効果量は{CoopBalance.FullRange}mまで全量、{CoopBalance.FullRange}～{CoopBalance.OuterRange}mで線形に0まで減衰する。範囲外へ出てから{CoopBalance.GraceSeconds}秒は直前の係数を保持する。距離減衰が無効な場合は{CoopBalance.FullRange}mまで全量で、{CoopBalance.FullRange}mを超えると猶予なしで切れる。"
            : $" With distance falloff enabled, allied effects are full through {CoopBalance.FullRange}m, fade linearly to zero from {CoopBalance.FullRange} to {CoopBalance.OuterRange}m, and retain the last coefficient for {CoopBalance.GraceSeconds}s after leaving range. With falloff disabled, they are full through {CoopBalance.FullRange}m and end beyond {CoopBalance.FullRange}m with no grace.";

        /// <param name="fullRange">The effect's own full-strength radius (a radius upgrade widens it);
        /// the fade-out edge scales with it so the curve keeps its shape.</param>
        public static float Coefficient(double distance, bool enabled = true, double fullRange = CoopBalance.FullRange)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0) return 0f;
            if (double.IsNaN(fullRange) || double.IsInfinity(fullRange) || fullRange <= 0) return 0f;
            if (!enabled) return distance <= fullRange ? 1f : 0f;
            if (distance <= fullRange) return 1f;
            double outer = fullRange * CoopBalance.OuterRange / CoopBalance.FullRange;
            return (float)Math.Max(0, (outer - distance) / (outer - fullRange));
        }
    }

    /// <summary>Reusable transient pair history, shared by the host and its UI estimate.</summary>
    public sealed class CoopDistanceTracker
    {
        private readonly Dictionary<ulong, CoopDistanceState> _pairs = new Dictionary<ulong, CoopDistanceState>();

        public float Update(int sourceId, int targetId, double distance, float now, bool eligible, bool enabled = true,
            double fullRange = CoopBalance.FullRange)
        {
            ulong key = ((ulong)(uint)sourceId << 32) | (uint)targetId;
            _pairs.TryGetValue(key, out var state);
            float coefficient = state.Update(distance, now, eligible, enabled, fullRange);
            if (eligible && enabled) _pairs[key] = state;
            else _pairs.Remove(key);
            return coefficient;
        }

        public void Clear() => _pairs.Clear();
    }

    public struct CoopDistanceState
    {
        private float _lastCoefficient;
        private float _outsideSince;
        private bool _outside;

        public float Update(double distance, float now, bool eligible, bool enabled = true,
            double fullRange = CoopBalance.FullRange)
        {
            if (!eligible || float.IsNaN(now) || float.IsInfinity(now))
            {
                this = default;
                return 0f;
            }
            float coefficient = CoopDistance.Coefficient(distance, enabled, fullRange);
            if (!enabled) { this = default; return coefficient; }
            if (coefficient > 0f)
            {
                _lastCoefficient = coefficient;
                _outside = false;
                return coefficient;
            }
            if (!_outside) { _outside = true; _outsideSince = now; }
            return now >= _outsideSince && now - _outsideSince < CoopBalance.GraceSeconds ? _lastCoefficient : 0f;
        }
    }
}
