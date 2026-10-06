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
        public long WrittenRevision { get { lock (_lock) return _writtenRevision; } }
        /// <summary>Latest revision actually handed to the worker, excluding serialization failures.</summary>
        public long EnqueuedRevision { get { lock (_lock) return _enqueuedRevision; } }
        private long _enqueuedRevision;

        private long _writtenRevision;
        private long _attemptedRevision;
        private bool _failurePending;

        /// <summary>
        /// 最後の書き込みが失敗したままか（成功すると false に戻る）。失敗したテキストは破棄されるので、
        /// 呼び出し側はこの印を見て保存を予約し直す必要がある（#72）。
        /// </summary>
        public bool HasPendingFailure { get { lock (_lock) return _failurePending; } }

        /// <summary>A completed failure, not a timeout or an older failed write.</summary>
        public bool HasFailedRevision(long revision)
        {
            lock (_lock) return _attemptedRevision >= revision && _writtenRevision < revision;
        }

        public int Coalesced { get; private set; }

        public double LastWriteMs { get; private set; }

        /// <summary>保存を予約する。p のリビジョンを1進め、JSON を作って作業スレッドへ渡す。</summary>
        public void Enqueue(Profile p) => EnqueueRevision(p);

        /// <summary>
        /// 保存を予約し、そのリビジョン（またはそれ以降）がディスクへ書き終わるまで待つ。
        /// 書き込みに失敗した・時間切れになった場合は false（その内容は確定していない）。外部の決済を始める前の「準備の保存」に使う。
        /// </summary>
        public bool EnqueueAndConfirm(Profile p, int timeoutMs = 3000)
        {
            long rev = EnqueueRevision(p);
            return WaitForRevision(rev, timeoutMs);
        }

        /// <summary>
        /// 外部の決済を始める前の準備保存。writer が無い（保存先が無効な）ときは書き込みが一度も行われないので、確定できたことにはならず false。
        /// </summary>
        public static bool ConfirmPrepared(AsyncProfileWriter writer, Profile p, int timeoutMs = 3000)
            => writer != null && writer.EnqueueAndConfirm(p, timeoutMs);

        /// <summary>指定リビジョン以降が書き終わったら true。その書き込みが失敗に終わった、または時間切れなら false。</summary>
        public bool WaitForRevision(long revision, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            lock (_lock)
            {
                while (true)
                {
                    if (_writtenRevision >= revision) return true;
                    if (_attemptedRevision >= revision) return false; // 試みたが書けなかった
                    int left = timeoutMs - (int)sw.ElapsedMilliseconds;
                    if (left <= 0) return false;
                    Monitor.Wait(_lock, left);
                }
            }
        }

        private long EnqueueRevision(Profile p)
        {
            p.Revision++;
            string text = ProfileCodec.Write(p);
            long rev = p.Revision;
            lock (_lock)
            {
                if (_pendingText != null) Coalesced++;
                _pendingText = text;
                _pendingRevision = rev;
                _enqueuedRevision = rev;
                if (_running) return rev;
                _running = true;
                _idle.Reset();
            }
            ThreadPool.QueueUserWorkItem(_ => Work());
            return rev;
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
                    lock (_lock)
                    {
                        _writtenRevision = rev;
                        _attemptedRevision = rev;
                        _failurePending = false;
                        LastError = null;
                        Monitor.PulseAll(_lock);
                    }
                }
                catch (Exception ex)
                {
                    lock (_lock)
                    {
                        _attemptedRevision = rev;
                        _failurePending = true;
                        LastError = ex.Message;
                        Monitor.PulseAll(_lock);
                    }
                }
                LastWriteMs = sw.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>予約済みの保存がすべて書き終わるまで待つ（終了時用）。書き込みの成功は意味しない：成功したかは WrittenRevision / LastError で確かめる。</summary>
        public bool Flush(int timeoutMs = 5000) => _idle.WaitOne(timeoutMs);
    }
}
