using System;
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

        /// <summary>Update の内訳（ClientSession.Tick / HostAuthority.Tick）。Stopwatch の読み取り2回だけで測る。</summary>
        private long _clientStart, _hostStart;
        private double _clientTotal, _hostTotal, _clientMax, _hostMax;
        private int _clientCount, _hostCount;

        // GC 割り当ての計測はサンプリングするときだけ動く（dreamforge_perf か perf.flag）。
        // メインスレッド全体（本体を含む）の1秒あたりの割り当て量で、MOD 有無での比較に使う。
        private double _gcTotal, _gcSeconds;
        private long _gcLast = -1;
        private int _gcSamples;
        private bool _gcBroken;

        /// <summary>ゲーム全体の1フレームの時間（Time.unscaledDeltaTime）を記録する。</summary>
        public void Frame(float dt)
        {
            double ms = dt * 1000.0;
            _frameTotal += ms;
            _frames++;
            _gcSeconds += dt;
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

        public void BeginClient() => _clientStart = _sw.ElapsedTicks;

        public void EndClient()
        {
            double ms = (_sw.ElapsedTicks - _clientStart) * 1000.0 / Stopwatch.Frequency;
            _clientTotal += ms;
            _clientCount++;
            if (ms > _clientMax) _clientMax = ms;
        }

        public void BeginHost() => _hostStart = _sw.ElapsedTicks;

        public void EndHost()
        {
            double ms = (_sw.ElapsedTicks - _hostStart) * 1000.0 / Stopwatch.Frequency;
            _hostTotal += ms;
            _hostCount++;
            if (ms > _hostMax) _hostMax = ms;
        }

        /// <summary>メインスレッドの割り当て量を1フレームぶん記録する。動かない環境では1回の失敗で止まる（fail-soft）。</summary>
        public void SampleGC()
        {
            if (_gcBroken) return;
            try
            {
                long now = GC.GetAllocatedBytesForCurrentThread();
                if (_gcLast >= 0) _gcTotal += Math.Max(0, now - _gcLast);
                _gcLast = now;
                _gcSamples++;
            }
            catch (Exception)
            {
                _gcBroken = true;
                _gcLast = -1;
            }
        }

        public string Report()
        {
            string r = $"Update avg {(_updateCount > 0 ? _updateTotal / _updateCount : 0):0.000}ms max {_updateMax:0.00}ms ({_updateCount} frames" +
                       $" | client {(_clientCount > 0 ? _clientTotal / _clientCount : 0):0.000}/{_clientMax:0.00}" +
                       $" host {(_hostCount > 0 ? _hostTotal / _hostCount : 0):0.000}/{_hostMax:0.00}) | " +
                       $"OnGUI avg {(_guiCount > 0 ? _guiTotal / _guiCount : 0):0.000}ms max {_guiMax:0.00}ms ({_guiCount} calls)";
            if (_gcSamples > 0)
                r += _gcBroken
                    ? " | GC alloc n/a"
                    : $" | GC alloc(main) {(_gcSeconds > 0.01 ? _gcTotal / _gcSeconds / 1048576.0 : 0):0.0}MB/s";
            double fms = _frames > 0 ? _frameTotal / _frames : 0;
            r += $" | frame avg {fms:0.00}ms ({(fms > 0 ? 1000 / fms : 0):0} fps) max {_frameMax:0.0}ms";
            _updateTotal = _guiTotal = _updateMax = _guiMax = 0;
            _updateCount = _guiCount = 0;
            _frameTotal = _frameMax = 0;
            _frames = 0;
            _clientTotal = _hostTotal = _clientMax = _hostMax = 0;
            _clientCount = _hostCount = 0;
            _gcTotal = _gcSeconds = 0;
            _gcSamples = 0;
            return r;
        }
    }
}
