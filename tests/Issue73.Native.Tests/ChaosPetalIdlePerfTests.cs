using System;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1101）堕落した混沌の神殿の花びらアニメで毎フレームの子Transform探しをキャッシュに
    /// 置き換える NativeChaosPetalIdlePerf の検証。境界（NativeBoundary.cs）の模倣
    /// Shrine_CorruptedChaos_PetalAnimator（本体の逆コンパイルと同じ式の Start/FrameUpdate）
    /// と子の一覧を持つ Transform に対して: (1) Prefix の書き込みが本体の式と全部の子で
    /// ビット単位で一致する（Time.time の積の繰り上げを含む。時刻はフレーム内で一定）、
    /// (2) そのとき Prefix は false を返して本体を飛ばす、(2b) 子の一覧が変わってキャッシュを
    /// 作り直す場面でも同じく一致する、(3) 子が Start の把握より減ったフレームは true を
    /// 返して何も書かない、(4) 設定が無効のときは true を返して何も書かない。
    /// </summary>
    public sealed class ChaosPetalIdlePerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly float _savedTime;

        // パッチ側の静的な欄（無効化・警告）を毎テストで初期化し、Prepare を模倣型に対して
        // 通す（実機では Harmony が呼ぶのと同じ準備。FieldRefAccess の _originals がここで
        // 決まる）。Dispose で設定と時刻を既定へ戻す。テスト間で持ち越すものはない
        // （神殿と花びらの実体は毎テスト作り直す）。
        public ChaosPetalIdlePerfTests()
        {
            _savedTime = Time.time;
            ResetPatchState();
            NativePerfOptions.ChaosPetalIdle = true;
            Log.Warnings.Clear();
            Assert.True((bool)typeof(NativeChaosPetalIdlePerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            ResetPatchState();
            NativePerfOptions.ChaosPetalIdle = true;
            Log.Warnings.Clear();
            Time.time = _savedTime;
        }

        private static void ResetPatchState()
        {
            var type = typeof(NativeChaosPetalIdlePerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
        }

        private static bool PrefixOf(Shrine_CorruptedChaos_PetalAnimator animator)
            => (bool)typeof(NativeChaosPetalIdlePerf).GetMethod("Prefix", Hidden)
                .Invoke(null, new object[] { animator });

        // 花びら petals 枚の神殿を1つ作る。子の初期スケールは子ごとに異なる値（0 だと掛けても
        // 差が見えないため）。sin 系の係数は呼び出し側から渡す。
        private static Shrine_CorruptedChaos_PetalAnimator Shrine(
            int petals, float s0t, float s0o, float s0m, float s1t, float s1o, float s1m)
        {
            var animator = new Shrine_CorruptedChaos_PetalAnimator
            {
                sin0Time = s0t, sin0Offset = s0o, sin0Mag = s0m,
                sin1Time = s1t, sin1Offset = s1o, sin1Mag = s1m,
            };
            for (int i = 0; i < petals; i++)
                animator.transform.children.Add(new Transform { localScale = OriginalScale(i) });
            animator.Start();
            return animator;
        }

        private static Vector3 OriginalScale(int i) => new Vector3(1f + i * 0.37f, 0.5f - i * 0.11f, 2f + i * 0.23f);

        private static void AssertSameBits(Vector3 expected, Vector3 actual, int index)
        {
            Assert.True(
                BitConverter.SingleToInt32Bits(expected.x) == BitConverter.SingleToInt32Bits(actual.x)
                    && BitConverter.SingleToInt32Bits(expected.y) == BitConverter.SingleToInt32Bits(actual.y)
                    && BitConverter.SingleToInt32Bits(expected.z) == BitConverter.SingleToInt32Bits(actual.z),
                $"child {index}: expected ({expected.x}, {expected.y}, {expected.z}) but got ({actual.x}, {actual.y}, {actual.z})");
        }

        // (1)(2) Prefix の書き込みが本体と同じ式の結果と全部の子でビット単位で一致し、
        // false を返す。係数3組 × 時刻3フレームで確かめる（i に掛かるオフセットと
        // Time.time の積の組合せが毎回変わる。2つ目は時刻0の波・負の倍率・自明でない
        // 初期スケールの組合せ）。
        [Fact]
        public void ThePrefixWritesExactlyTheVanillaScalesAndSkipsTheOriginal()
        {
            var seeds = new (float S0t, float S0o, float S0m, float S1t, float S1o, float S1m)[]
            {
                (1.7f, 0.31f, 0.21f, 2.3f, -0.17f, 0.13f),
                (0f, 1f, 0.5f, 0.5f, 0.25f, -0.5f),
                (6.28318f, 2f, 1f, 3.1f, 0.7f, 0.9f),
            };
            foreach (var seed in seeds)
            {
                var vanilla = Shrine(5, seed.S0t, seed.S0o, seed.S0m, seed.S1t, seed.S1o, seed.S1m);
                var patched = Shrine(5, seed.S0t, seed.S0o, seed.S0m, seed.S1t, seed.S1o, seed.S1m);
                foreach (float t in new[] { 0f, 1.33f, 1234.567f })
                {
                    Time.time = t;
                    Assert.False(PrefixOf(patched));
                    vanilla.FrameUpdate();
                    Assert.Equal(vanilla.transform.childCount, patched.transform.childCount);
                    for (int i = 0; i < vanilla.transform.childCount; i++)
                        AssertSameBits(vanilla.transform.GetChild(i).localScale, patched.transform.GetChild(i).localScale, i);
                }
            }
            Assert.Empty(Log.Warnings);
        }

        // (2b) 子の一覧が変わり、キャッシュを作り直す場面でも本体と一致する（本体の GetChild が
        // 読むのと同じ並びから作り直す）。先頭への挿入（本体が読む子がずれる）と末尾への追加
        // （本体が読む子は変わらない）の両方。
        [Fact]
        public void ThePrefixRebuildsTheCacheFromTheLiveOrderWhenChildrenChange()
        {
            var vanilla = Shrine(4, 1.2f, 0.4f, 0.3f, 2.1f, 0.6f, 0.25f);
            var patched = Shrine(4, 1.2f, 0.4f, 0.3f, 2.1f, 0.6f, 0.25f);
            Time.time = 0.8f;
            Assert.False(PrefixOf(patched));
            vanilla.FrameUpdate();

            vanilla.transform.children.Insert(0, new Transform { localScale = new Vector3(0.75f, 1.25f, 3.5f) });
            patched.transform.children.Insert(0, new Transform { localScale = new Vector3(0.75f, 1.25f, 3.5f) });
            Time.time = 5.5f;
            Assert.False(PrefixOf(patched));
            vanilla.FrameUpdate();
            for (int i = 0; i < vanilla.transform.childCount; i++)
                AssertSameBits(vanilla.transform.GetChild(i).localScale, patched.transform.GetChild(i).localScale, i);

            vanilla.transform.children.Add(new Transform { localScale = new Vector3(1.5f, 0.25f, 0.5f) });
            patched.transform.children.Add(new Transform { localScale = new Vector3(1.5f, 0.25f, 0.5f) });
            Time.time = 9.25f;
            Assert.False(PrefixOf(patched));
            vanilla.FrameUpdate();
            for (int i = 0; i < vanilla.transform.childCount; i++)
                AssertSameBits(vanilla.transform.GetChild(i).localScale, patched.transform.GetChild(i).localScale, i);
            Assert.Empty(Log.Warnings);
        }

        // (3) 子が Start の把握より減ったフレームは true を返して何も書かない（この場面の本体は
        // GetChild が把握を超えて例外になるため、パッチ無効側とは比べられない。書かないことは
        // localScale の値と書き込み回数の両方で確かめる）。キャッシュ済みと未構築の両方で。
        [Fact]
        public void ThePrefixHandsBackAndWritesNothingWhenChildrenShrunk()
        {
            var animator = Shrine(4, 1.1f, 0.3f, 0.2f, 1.9f, 0.5f, 0.15f);
            Time.time = 0.4f;
            Assert.False(PrefixOf(animator));

            animator.transform.children.RemoveRange(1, 3);
            Time.time = 3.6f;
            var remaining = animator.transform.GetChild(0);
            Vector3 before = remaining.localScale;
            int writes = remaining.localScaleWrites;
            Assert.True(PrefixOf(animator));
            Assert.Equal(writes, remaining.localScaleWrites);
            Assert.Equal(before.x, remaining.localScale.x);
            Assert.Equal(before.y, remaining.localScale.y);
            Assert.Equal(before.z, remaining.localScale.z);

            // 未構築のまま減っている場合も同じ（Start の後に減って1フレームも走っていない）。
            var fresh = Shrine(3, 1.1f, 0.3f, 0.2f, 1.9f, 0.5f, 0.15f);
            fresh.transform.children.RemoveRange(0, 2);
            var only = fresh.transform.GetChild(0);
            only.localScale = new Vector3(7f, 8f, 9f);
            int onlyWrites = only.localScaleWrites;
            Assert.True(PrefixOf(fresh));
            Assert.Equal(onlyWrites, only.localScaleWrites);
            Assert.Equal(7f, only.localScale.x);
            Assert.Equal(8f, only.localScale.y);
            Assert.Equal(9f, only.localScale.z);
            Assert.Empty(Log.Warnings);
        }

        // (4) 設定が無効のときは true を返して何も書かない。
        [Fact]
        public void ThePrefixHandsBackWhenTheConfigIsOff()
        {
            NativePerfOptions.ChaosPetalIdle = false;
            var animator = Shrine(3, 1.4f, 0.2f, 0.3f, 0.8f, 0.9f, 0.4f);
            Time.time = 2.2f;
            Assert.True(PrefixOf(animator));
            for (int i = 0; i < animator.transform.childCount; i++)
            {
                var child = animator.transform.GetChild(i);
                // 書き込み回数は Shrine の初期化子の1回のまま（Start は読むだけで書かない）。
                Assert.Equal(1, child.localScaleWrites);
                Assert.Equal(OriginalScale(i).x, child.localScale.x);
                Assert.Equal(OriginalScale(i).y, child.localScale.y);
                Assert.Equal(OriginalScale(i).z, child.localScale.z);
            }
            Assert.Empty(Log.Warnings);
        }
    }
}
