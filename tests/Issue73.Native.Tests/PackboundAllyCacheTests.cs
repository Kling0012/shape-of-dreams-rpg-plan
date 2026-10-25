using System;
using SodRpg.Core.Game;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// #55 の群れ味方キャッシュの窓判定（perf-loop/20261025-2030）。
    /// 行動ティックは眠っている・ゾーン遷移中の Packbound モンスターのキャッシュを更新しない。
    /// 起床直後の被弾が眠る前の古い味方判定でガードを決めないことは、読み出し側の窓判定
    /// （<see cref="MonsterBehavior.AllyCacheStale"/>、HostAuthority.PackboundAllyNear が呼ぶ）が保証する:
    /// 窓（<see cref="MonsterBehavior.AllyCacheSeconds"/>）を過ぎた最初の読み出しは必ず計算し直す。
    /// ハーネスは HostAuthority.Monsters.cs をコンパイルしないため、判定をこの関数に寄せて検証する
    /// （MonsterCueReplayTests と同じ扱い）。被弾・合図がすべてこの窓判定を通ることは
    /// PR 本文の「確認した読み取り経路」のとおりコードで確認済み。
    /// </summary>
    public sealed class PackboundAllyCacheTests
    {
        /// <summary>
        /// 眠り始める直前（＝最後の更新）から 0.15秒を超えて眠ったあとの起床直後の被弾は、
        /// どんな眠りの長さでも窓が切れている。つまり読み出しは眠る前のキャッシュ値を返せず、
        /// その場の全モンスター走査で計算し直す（変更前は睡眠中もティックが更新していたが、
        /// 読み出しの鮮度「最大 0.15秒前の値」は変わらない）。
        /// </summary>
        [Fact]
        public void FirstReadAfterSleepBeyondWindowIsAlwaysStale()
        {
            const float sleptAt = 10f; // 眠り始め直前の最後のキャッシュ更新時刻
            // 窓 + ティック1回分（0.16秒）から3秒の眠りまで、0.1秒刻みの任意の長さで確かめる。
            for (float gap = 0.16f; gap <= 3f; gap += 0.1f)
                Assert.True(MonsterBehavior.AllyCacheStale(sleptAt, sleptAt + gap),
                    "gap=" + gap.ToString("0.0") + " の睡眠後の最初の読み出しは計算し直すべき");
        }

        /// <summary>
        /// 窓の内側の読み出しはキャッシュをそのまま返す（起きている戦闘中の被弾・合図の挙動）。
        /// 行動ティックは0.1秒ごとに更新するので、起きている間の実効的な鮮度は最大 0.15秒のまま。
        /// </summary>
        [Fact]
        public void ReadWithinWindowKeepsUsingTheCache()
        {
            const float checkedAt = 10f;
            Assert.False(MonsterBehavior.AllyCacheStale(checkedAt, checkedAt));         // 更新と同時刻
            Assert.False(MonsterBehavior.AllyCacheStale(checkedAt, checkedAt + 0.1f)); // ティック1回分
            Assert.False(MonsterBehavior.AllyCacheStale(checkedAt, checkedAt + 0.1499f)); // 窓の内側
            Assert.True(MonsterBehavior.AllyCacheStale(checkedAt, checkedAt + 0.1501f));  // 窓の外側
        }

        /// <summary>未初期化（AllyCheckedAt の初期値 -1）は最初の読み出しで必ず計算する（生成直後の初回合図）。</summary>
        [Fact]
        public void UninitializedCacheIsStaleOnFirstRead()
        {
            Assert.True(MonsterBehavior.AllyCacheStale(-1f, 0f));
            Assert.True(MonsterBehavior.AllyCacheStale(-1f, 100f));
        }
    }
}
