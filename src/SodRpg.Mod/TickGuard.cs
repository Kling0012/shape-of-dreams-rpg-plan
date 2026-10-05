using System;
using System.Collections.Generic;

namespace SodRpg.Mod
{
    /// <summary>
    /// 毎フレームの更新処理を工程ごとに例外から守る実行器（#74）。1つの工程が例外を出し続けても
    /// 以降の工程は毎フレーム動き続け、ログは工程ごとに logInterval 秒に1回に絞る。
    /// ClientSession.Tick と同じ形の保護をホスト側にも入れるために使う。
    /// </summary>
    internal sealed class TickGuard
    {
        private readonly Action[] _steps;
        private readonly string[] _names;
        private readonly float[] _nextLogAt;
        private readonly float _logInterval;
        private readonly Action<string> _error;

        /// <param name="steps">毎フレーム順に実行する工程。</param>
        /// <param name="names">工程名（steps と同じ長さ）。ログに使う。</param>
        /// <param name="logInterval">同じ工程のログをもう1回出すまでの最小間隔（秒）。</param>
        /// <param name="error">ログ出力。「(工程名): 例外」の形の文言を受け取る。</param>
        public TickGuard(Action[] steps, string[] names, float logInterval, Action<string> error)
        {
            if (names.Length != steps.Length)
                throw new ArgumentException("names must match steps", nameof(names));
            _steps = steps;
            _names = names;
            _nextLogAt = new float[steps.Length];
            _logInterval = logInterval;
            _error = error;
        }

        /// <param name="now">ログ間引きに使う時刻（Time.unscaledTime）。</param>
        public void Run(float now)
        {
            for (int i = 0; i < _steps.Length; i++)
            {
                try
                {
                    _steps[i]();
                }
                catch (Exception ex)
                {
                    // Log at most once per interval per step so a persistent fault does not flood the log every frame.
                    if (now >= _nextLogAt[i])
                    {
                        _nextLogAt[i] = now + _logInterval;
                        _error("(" + _names[i] + "): " + ex);
                    }
                }
            }
        }
    }
}
