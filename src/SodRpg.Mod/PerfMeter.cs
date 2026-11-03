using System;
using System.Diagnostics;

namespace SodRpg.Mod
{
    /// <summary>このMODが1フレームに使う時間を測る（dreamforge_perf で表示）。計測自体は Stopwatch の差分だけで軽い。</summary>
    internal sealed class PerfMeter
    {
        private long _start;
        private double _updateTotal, _guiTotal, _updateMax, _guiMax;
        private int _updateCount, _guiCount;

        public void Begin() => _start = Stopwatch.GetTimestamp();

        private double ElapsedMs() => (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;

        private double _frameTotal, _frameMax;
        private int _frames;

        /// <summary>Update の内訳（ClientSession.Tick / HostAuthority.Tick）。</summary>
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

        public readonly TickTimings ClientStages = new TickTimings();
        public readonly TickTimings HostStages = new TickTimings(
            "NativeProcSpawnSpread.Tick", "Detach", "TickCoopTrade", "EnsureRegistered",
            "AutocastPressure.TickHost", "BuildTickStages");

        public void BeginClient()
        {
            ClientStages.Begin();
            _clientStart = Stopwatch.GetTimestamp();
        }

        public void EndClient()
        {
            double ms = (Stopwatch.GetTimestamp() - _clientStart) * 1000.0 / Stopwatch.Frequency;
            _clientTotal += ms;
            _clientCount++;
            if (ms > _clientMax) _clientMax = ms;
            ClientStages.End(ms);
        }

        public void BeginHost()
        {
            HostStages.Begin();
            _hostStart = Stopwatch.GetTimestamp();
        }

        public void EndHost()
        {
            double ms = (Stopwatch.GetTimestamp() - _hostStart) * 1000.0 / Stopwatch.Frequency;
            _hostTotal += ms;
            _hostCount++;
            if (ms > _hostMax) _hostMax = ms;
            HostStages.End(ms);
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

        public string ReportSpikes() => ClientStages.Report("client") + HostStages.Report("host");

        internal sealed class TickTimings
        {
            // Enough for the registered Tick stages and host prelude; reused for every frame.
            private readonly long[] _current = new long[64];
            private readonly long[] _spike = new long[64];
            private readonly string[] _prefixNames;
            private Action[] _steps;
            private double _maxMs;

            public TickTimings(params string[] prefixNames) => _prefixNames = prefixNames;

            public void SetSteps(Action[] steps) => _steps = steps;

            public void Begin() => Array.Clear(_current, 0, _current.Length);

            public long BeginStage() => Stopwatch.GetTimestamp();

            public void EndStage(int index, long start)
            {
                if ((uint)index < (uint)_current.Length)
                    _current[index] += Stopwatch.GetTimestamp() - start;
            }

            public void End(double ms)
            {
                if (ms < 5 || ms <= _maxMs) return;
                _maxMs = ms;
                Array.Copy(_current, _spike, _current.Length);
            }

            public string Report(string label)
            {
                if (_maxMs < 5) return string.Empty;
                int first = -1, second = -1, third = -1;
                for (int i = 0; i < _spike.Length; i++)
                {
                    if (_spike[i] <= 0) continue;
                    if (first < 0 || _spike[i] > _spike[first])
                    {
                        third = second; second = first; first = i;
                    }
                    else if (second < 0 || _spike[i] > _spike[second])
                    {
                        third = second; second = i;
                    }
                    else if (third < 0 || _spike[i] > _spike[third]) third = i;
                }
                string result = " | " + label + " spike " +
                    _maxMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "ms:";
                if (first >= 0) result += " " + StageReport(first);
                if (second >= 0) result += ", " + StageReport(second);
                if (third >= 0) result += ", " + StageReport(third);
                _maxMs = 0;
                return result;
            }

            private string StageReport(int index)
            {
                string name = index < _prefixNames.Length ? _prefixNames[index]
                    : _steps != null && index - _prefixNames.Length < _steps.Length
                        ? _steps[index - _prefixNames.Length].Method.Name : "unknown";
                return name + " " + (_spike[index] * 1000.0 / Stopwatch.Frequency)
                    .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
