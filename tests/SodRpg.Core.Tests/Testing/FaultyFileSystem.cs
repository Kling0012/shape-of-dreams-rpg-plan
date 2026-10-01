using System;
using System.IO;
using SodRpg.Core;

namespace SodRpg.Core.Tests.Testing
{
    /// <summary>プロセスの強制終了を表す。保存側は握りつぶさずに伝播させる（IOExceptionとは別系統）。</summary>
    public sealed class CrashException : Exception
    {
        public CrashException(string message) : base(message) { }
    }

    public enum FaultMode
    {
        /// <summary>操作の直前にプロセスが死ぬ。</summary>
        CrashBefore,
        /// <summary>操作は完了したが、戻る前にプロセスが死ぬ。</summary>
        CrashAfter,
        /// <summary>書込み操作が途中まで（前半のみ）で死ぬ。書込み以外は CrashBefore と同じ。</summary>
        CrashTorn,
        /// <summary>操作の直前に IOException（ディスクフル等の保存失敗。プロセスは生きている）。</summary>
        IoError,
    }

    /// <summary>変更系の操作（書込み・置換・コピー・削除）に番号を付け、指定した番号で障害を起こす。</summary>
    public sealed class FaultyFileSystem : IFileSystem
    {
        private readonly IFileSystem _inner;
        private int _count;
        private int _faultAt = -1;
        private FaultMode _mode;

        public FaultyFileSystem(IFileSystem inner) { _inner = inner; }

        /// <summary>Arm 以降に実行した変更系の操作の数。</summary>
        public int OpCount => _count;

        public void Arm(int opIndex, FaultMode mode)
        {
            _count = 0;
            _faultAt = opIndex;
            _mode = mode;
        }

        public void Disarm()
        {
            _faultAt = -1;
        }

        public bool Exists(string path) => _inner.Exists(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string contents)
        {
            Step(() => _inner.WriteAllText(path, contents), () => _inner.WriteAllText(path, contents.Substring(0, contents.Length / 2)));
        }

        public void Replace(string temp, string dest, string backupOrNull) => Step(() => _inner.Replace(temp, dest, backupOrNull), null);

        public void Copy(string source, string dest, bool overwrite) => Step(() => _inner.Copy(source, dest, overwrite), null);

        public void Delete(string path) => Step(() => _inner.Delete(path), null);

        private void Step(Action action, Action tornAction)
        {
            int idx = _count++;
            if (idx != _faultAt)
            {
                action();
                return;
            }

            switch (_mode)
            {
                case FaultMode.CrashBefore:
                    throw new CrashException("クラッシュ(操作前) #" + idx);
                case FaultMode.CrashAfter:
                    action();
                    throw new CrashException("クラッシュ(操作後) #" + idx);
                case FaultMode.CrashTorn:
                    tornAction?.Invoke();
                    throw new CrashException("クラッシュ(書込み途中) #" + idx);
                case FaultMode.IoError:
                    throw new IOException("注入された保存失敗 #" + idx);
            }
        }
    }
}
