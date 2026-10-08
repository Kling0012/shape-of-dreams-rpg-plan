using System;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 「下がれ！」(St_R_BackOff) のチャージ中に、周囲の敵へ一定間隔で継続ダメージを出すための純粋な時間管理と計算。
    /// チャージの開始と終了はホスト側の接続層が <see cref="Begin"/> / <see cref="End"/> で知らせ、毎ティック <see cref="Poll"/> で
    /// 「いま何回分の脈動が期限に来たか」を受け取る。ゲーム本体の型には依存しない。
    /// 終了通知が届かなくても <see cref="MaxSeconds"/> で必ず止まり、描画落ちなどで時間が飛んでも <see cref="CatchUp"/> 回までしかまとめて出さない。
    /// </summary>
    public sealed class BackOffChargeRuntime
    {
        public const string Memory = "St_R_BackOff";
        // 新しい仕組みの調整値。ダメージの計算と出力はホストだけが行うので、通信の指紋には入れない。
        /// <summary>脈動の間隔（秒）。</summary>
        public const float Interval = 0.5f;
        /// <summary>チャージ開始からの打ち切り（秒）。終了通知が届かなくても必ず止まる安全弁。</summary>
        public const float MaxSeconds = 2.5f;
        /// <summary>ケトゥスを中心にした脈動の半径（m）。</summary>
        public const float Radius = 4.5f;
        /// <summary>1回の脈動が各敵へ与える量（高い方の攻撃力・魔力に対する%）。</summary>
        public const int DamagePercent = 40;
        /// <summary>1回の脈動で当たる敵の上限。</summary>
        public const int MaxTargets = 8;
        /// <summary>描画落ちなどで時間が飛んだとき、まとめて出す脈動の上限。</summary>
        public const int CatchUp = 2;

        private bool _charging;
        private float _start, _next;

        public bool Charging => _charging;

        /// <summary>チャージの開始。押し直しは最初から数え直す。最初の脈動は <see cref="Interval"/> 秒後（押してすぐ離す場合は何も出ない）。</summary>
        public void Begin(float now)
        {
            if (float.IsNaN(now) || float.IsInfinity(now)) { _charging = false; return; }
            _charging = true;
            _start = now;
            _next = now + Interval;
        }

        /// <summary>チャージの終了（振り抜き・中断・死亡・ゾーン移動）。</summary>
        public void End() => _charging = false;

        /// <summary>期限に来た脈動の回数。チャージ中でなければ0。<see cref="MaxSeconds"/> を過ぎるとチャージを終えたものとして止める。</summary>
        public int Poll(float now)
        {
            if (!_charging) return 0;
            if (float.IsNaN(now) || float.IsInfinity(now)) { _charging = false; return 0; }
            float limit = _start + MaxSeconds;
            int due = 0;
            while (_next <= now && _next <= limit) { due++; _next += Interval; }
            if (now >= limit) _charging = false;
            else if (due > CatchUp) _next = now + Interval;
            return Math.Min(due, CatchUp);
        }

        /// <summary>1回の脈動が各敵へ与える量。高い方の攻撃力・魔力の <see cref="DamagePercent"/>% に、星の消費による伸び率をかける。</summary>
        public static float TickDamage(float attackDamage, float abilityPower, decimal rankMultiplier)
        {
            float high = Math.Max(attackDamage, abilityPower);
            if (!(high > 0f) || float.IsInfinity(high) || rankMultiplier <= 0m) return 0f;
            return high * DamagePercent / 100f * (float)rankMultiplier;
        }
    }
}
