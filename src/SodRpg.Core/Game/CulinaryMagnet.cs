using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>吸い寄せ先の候補1人分（生存している旅人の現在位置）。</summary>
    public struct MagnetTarget
    {
        public readonly float X, Y, Z;
        public MagnetTarget(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// 本体の「料理のエッセンス」（Gem_L_Culinary）が落とす食材を、近くの旅人へ自動で吸い寄せる判定。
    /// 食材の見分け・位置の更新・どの旅人が生存しているかは SodRpg.Mod の HostAuthority.CulinaryMagnet が行い、
    /// このクラスは「どの旅人へ」「1ティックでどれだけ動くか」だけを決める（ゲームの型を使わないので試験できる）。
    /// ホストだけが動かし、協力プレイではホスト以外の端末には位置の同期で届く。
    /// </summary>
    public static class CulinaryMagnet
    {
        /// <summary>吸い寄せを始める距離（m）。画面の半分ほど。これより遠い食材は動かさない。</summary>
        public const float StartRadius = 9f;
        /// <summary>落ちてから吸い寄せを始めるまでの待ち（秒）。落下の演出を邪魔しない。</summary>
        public const float SpawnDelay = 0.6f;
        /// <summary>吸い寄せ開始直後の速さ（m/秒）。</summary>
        public const float MinSpeed = 7f;
        /// <summary>1秒ごとに増える速さ（m/秒²）。</summary>
        public const float Acceleration = 30f;
        /// <summary>速さの上限（m/秒）。近づくほど急に速くなりすぎない。</summary>
        public const float MaxSpeed = 24f;

        /// <summary>
        /// 食材の拾得物かどうか。ゲームの型名（例 Pickup_Ingredient）で見分ける。
        /// 「Pickup_」で始まり、Ingredient・Food・Culinary のどれかを含むものだけ。
        /// 夢のダスト・ポーションなど他の拾得物は対象にしない。
        /// </summary>
        public static bool IsIngredientTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName) || !typeName.StartsWith("Pickup_", StringComparison.Ordinal)) return false;
            return typeName.IndexOf("Ingredient", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Food", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Culinary", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 食材から最も近い旅人の番号（targets の添字）。範囲内に誰もいなければ -1。
        /// 同じ距離なら番号の小さい方。呼び出し側は生存している旅人だけを渡す。
        /// </summary>
        public static int SelectTarget(float x, float y, float z, IReadOnlyList<MagnetTarget> targets, float radius = StartRadius)
        {
            if (targets == null || !IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !(radius > 0f)) return -1;
            int best = -1;
            float radiusSq = radius * radius, bestSq = 0f;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (!IsFinite(t.X) || !IsFinite(t.Y) || !IsFinite(t.Z)) continue;
                float sq = DistanceSq(x, y, z, t);
                if (sq <= radiusSq && (best < 0 || sq < bestSq)) { best = i; bestSq = sq; }
            }
            return best;
        }

        /// <summary>吸い寄せ中の速さ（m/秒）。pulledSeconds は吸い寄せが続いている秒数。</summary>
        public static float Speed(float pulledSeconds)
        {
            if (!(pulledSeconds > 0f)) return MinSpeed;
            return Math.Min(MaxSpeed, MinSpeed + Acceleration * pulledSeconds);
        }

        /// <summary>
        /// 1ティック（dt 秒）ぶん target へ近づけた位置。届く距離なら target の位置にぴったり置く
        /// （本体の拾得判定に任せるため、行き過ぎない）。値が不正なら今の位置のまま。
        /// </summary>
        public static MagnetTarget Step(float x, float y, float z, MagnetTarget target, float dt, float pulledSeconds)
        {
            var here = new MagnetTarget(x, y, z);
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(target.X) || !IsFinite(target.Y) || !IsFinite(target.Z)
                || !(dt > 0f) || float.IsInfinity(dt)) return here;
            float dx = target.X - x, dy = target.Y - y, dz = target.Z - z;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float move = Speed(pulledSeconds) * dt;
            if (dist <= move || dist <= 1e-4f) return target;
            float k = move / dist;
            return new MagnetTarget(x + dx * k, y + dy * k, z + dz * k);
        }

        private static float DistanceSq(float x, float y, float z, MagnetTarget t)
        {
            float dx = t.X - x, dy = t.Y - y, dz = t.Z - z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
