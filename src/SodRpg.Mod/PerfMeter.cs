using System.Diagnostics;

namespace SodRpg.Mod
{
    /// <summary>このMODが1フレームに使う時間を測る（dreamforge_perf で表示）。計測自体は Stopwatch の差分だけで軽い。</summary>
    internal sealed class PerfMeter
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _start;
        private double _updateTotal, _guiTotal, _updateMax, _guiMax;
        private int _updateCount, _guiCount;

        public void Begin() => _start = _sw.ElapsedTicks;

        private double ElapsedMs() => (_sw.ElapsedTicks - _start) * 1000.0 / Stopwatch.Frequency;

        private double _frameTotal, _frameMax;
        private int _frames;

        /// <summary>ゲーム全体の1フレームの時間（Time.unscaledDeltaTime）を記録する。</summary>
        public void Frame(float dt)
        {
            double ms = dt * 1000.0;
            _frameTotal += ms;
            _frames++;
            if (ms > _frameMax) _frameMax = ms;
        }

        public void EndUpdate()
        {
            double ms = ElapsedMs();
            _updateTotal += ms;
            _updateCount++;
            if (ms > _updateMax) _updateMax = ms;
        }

        public void EndGui()
        {
            double ms = ElapsedMs();
            _guiTotal += ms;
            _guiCount++;
            if (ms > _guiMax) _guiMax = ms;
        }

        public string Report()
        {
            string r = $"Update avg {(_updateCount > 0 ? _updateTotal / _updateCount : 0):0.000}ms max {_updateMax:0.00}ms ({_updateCount} frames) | " +
                       $"OnGUI avg {(_guiCount > 0 ? _guiTotal / _guiCount : 0):0.000}ms max {_guiMax:0.00}ms ({_guiCount} calls)";
            double fms = _frames > 0 ? _frameTotal / _frames : 0;
            r += $" | frame avg {fms:0.00}ms ({(fms > 0 ? 1000 / fms : 0):0} fps) max {_frameMax:0.0}ms";
            _updateTotal = _guiTotal = _updateMax = _guiMax = 0;
            _updateCount = _guiCount = 0;
            _frameTotal = _frameMax = 0;
            _frames = 0;
            return r;
        }
    }
}
