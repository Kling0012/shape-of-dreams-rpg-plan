using System;
using SodRpg.Core;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// MOD 起動時の初回のプロフィール読み込みを守る（issue #89）。
    /// <see cref="ProfileSlots"/> の生成と最初のスロット切り替えをまとめて試し、ファイルのロック・権限・壊れなどで
    /// 例外が出ても外へ投げない（MOD の初期化を止めない）。失敗中は本体を渡さないので呼び出し側は保存を止め、
    /// 代わりに <see cref="FallbackProfile"/> を使う。既存のセーブデータには書き込まない。
    /// 一時的なロックなら解けることがあるので、失敗したら <see cref="NextRetryAt"/> まで待って
    /// それ以降の呼び出しで同じ初回処理をやり直す。
    /// </summary>
    public sealed class ProfileSlotLoader
    {
        private readonly IFileSystem _fs;
        private readonly string _saveDir;
        private readonly ulong _soloSeed;
        private readonly ulong _multiSeed;
        private readonly double _retrySeconds;
        private double _nextRetryAt = double.MinValue;
        private Profile _fallback;

        public ProfileSlotLoader(IFileSystem fs, string saveDir, ulong soloSeed, ulong multiSeed, double retrySeconds = 5)
        {
            if (retrySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(retrySeconds));
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _saveDir = saveDir ?? throw new ArgumentNullException(nameof(saveDir));
            _soloSeed = soloSeed;
            _multiSeed = multiSeed;
            _retrySeconds = retrySeconds;
        }

        /// <summary>初回の読み込みに成功するまで null。成功したら以後は呼び出し側がこれを直接使う。</summary>
        public ProfileSlots Slots { get; private set; }

        /// <summary>失敗中に代わりに使う新しいプロフィール。保存には使わない（何度取っても同じもの）。</summary>
        public Profile FallbackProfile => _fallback ?? (_fallback = Profile.CreateNew(_soloSeed));

        /// <summary>最後に失敗した理由（画面への通知に使う）。一度でも成功したら null に戻る。</summary>
        public string Error { get; private set; }

        /// <summary>前回の失敗から、次の再試行をしてよい時刻（呼び出し側の秒）。初回はすぐに試してよい。</summary>
        public double NextRetryAt => _nextRetryAt;

        /// <summary>
        /// 初回の読み込み（と最初のスロット切り替え）を試す。成功したら true を返し、以降の呼び出しは true のまま何もしない。
        /// now が <see cref="NextRetryAt"/> 未満なら再試行せず false を返す（一時的なロックのために少し待つ）。
        /// </summary>
        public bool TryLoad(double now, ProfileSessionKind session, Action flushOldWriter)
        {
            if (Slots != null) return true;
            if (now < _nextRetryAt) return false;
            try
            {
                var slots = new ProfileSlots(_fs, _saveDir, _soloSeed, _multiSeed);
                slots.TrySwitch(session, flushOldWriter);
                Slots = slots;
                Error = null;
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _nextRetryAt = now + _retrySeconds;
                return false;
            }
        }
    }
}
