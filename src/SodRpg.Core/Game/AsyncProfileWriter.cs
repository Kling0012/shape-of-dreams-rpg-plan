using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// プロフィールの保存をメインスレッドから外す。JSON化（速い）は呼び出し側のスレッドで行い、
    /// ディスクへの書き込み・読み戻し・置換（遅い）は1本の作業スレッドで順に行う。
    /// 書き込み中に次の保存が来たら、最新の1件だけを残して古いものは捨てる（中間状態は保存しなくてよい）。
    /// </summary>
    public sealed class AsyncProfileWriter
    {
        private readonly ProfileStore _store;
        private readonly object _lock = new object();
        private string _pendingText;
        private long _pendingRevision;
        private bool _running;
        private readonly ManualResetEvent _idle = new ManualResetEvent(true);

        public AsyncProfileWriter(ProfileStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>最後の書き込みの失敗（成功したら null に戻る）。</summary>
        public string LastError { get; private set; }

        /// <summary>ディスクへ書き終えた最新のリビジョン。</summary>
        public long WrittenRevision { get; private set; }

        public int Coalesced { get; private set; }

        public double LastWriteMs { get; private set; }

        /// <summary>保存を予約する。p のリビジョンを1進め、JSON を作って作業スレッドへ渡す。</summary>
        public void Enqueue(Profile p)
        {
            p.Revision++;
            string text = ProfileCodec.Write(p);
            long rev = p.Revision;
            lock (_lock)
            {
                if (_pendingText != null) Coalesced++;
                _pendingText = text;
                _pendingRevision = rev;
                if (_running) return;
                _running = true;
                _idle.Reset();
            }
            ThreadPool.QueueUserWorkItem(_ => Work());
        }

        private void Work()
        {
            while (true)
            {
                string text;
                long rev;
                lock (_lock)
                {
                    if (_pendingText == null)
                    {
                        _running = false;
                        _idle.Set();
                        return;
                    }
                    text = _pendingText;
                    rev = _pendingRevision;
                    _pendingText = null;
                }
                var sw = Stopwatch.StartNew();
                try
                {
                    _store.WriteText(text, rev);
                    WrittenRevision = rev;
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
                LastWriteMs = sw.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>予約済みの保存がすべて書き終わるまで待つ（終了時用）。</summary>
        public bool Flush(int timeoutMs = 5000) => _idle.WaitOne(timeoutMs);
    }
}
