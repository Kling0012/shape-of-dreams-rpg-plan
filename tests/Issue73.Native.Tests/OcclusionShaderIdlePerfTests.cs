using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#823）NativeOcclusionShaderIdlePerf のガードの境界検証。Shader スタブの呼び出し
    /// 記録で、前回送った値と完全一致したときだけネイティブ呼び出しを省くこと、値・
    /// プロパティID・成分のどれかが変われば送ること、設定 OFF の素通し、null と長さ違い
    /// の配列でのキャッシュ無効化、Prefix のインスタンス交代と OnDestroy の Postfix に
    /// よるキャッシュ破棄を確かめる。Vector4 の省略判定は Unity の operator==（成分差の
    /// 二乗和が kEpsilon² 未満を一致とする近似）を使わず成分ごとの完全一致で行うため、
    /// 1e-6 程度の変化も省かれずに送られることがここで保証される。
    /// </summary>
    public sealed class OcclusionShaderIdlePerfTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // LogicUpdateOcclusionTest の Prefix。インスタンスが前回と違えばキャッシュを捨てる。
        private static void Tick(CameraManager camera)
            => typeof(NativeOcclusionShaderIdlePerf).GetMethod("Prefix", Hidden).Invoke(null, new object[] { camera });

        // NativeOcclusionShaderIdlePerfReset の Postfix（CameraManager.OnDestroy の後片付け）。
        private static void OnDestroyPostfix()
            => typeof(NativeOcclusionShaderIdlePerfReset).GetMethod("Postfix", Hidden).Invoke(null, null);

        // テストごとに新しいカメラで Tick し、必ずキャッシュを空の状態から始める。
        private static void ResetState(CameraManager camera)
        {
            NativePerfOptions.OcclusionIdle = true;
            Shader.ResetCalls();
            Tick(camera);
        }

        [Fact]
        public void GuardIntSkipsOnlyExactRepeats()
        {
            var camera = new CameraManager();
            ResetState(camera);
            NativeOcclusionShaderIdlePerf.GuardInt(7, 3);
            NativeOcclusionShaderIdlePerf.GuardInt(7, 3); // 完全一致: 省く
            NativeOcclusionShaderIdlePerf.GuardInt(7, 4); // 値が変わった: 送る
            NativeOcclusionShaderIdlePerf.GuardInt(8, 4); // ID が変わった: 送る
            NativeOcclusionShaderIdlePerf.GuardInt(8, 4); // 再び一致: 省く
            Assert.Equal(new[] { "int 7 3", "int 7 4", "int 8 4" }, Shader.Calls);
        }

        [Fact]
        public void GuardFloatSkipsOnlyExactRepeats()
        {
            var camera = new CameraManager();
            ResetState(camera);
            NativeOcclusionShaderIdlePerf.GuardFloat(9, 1.5f);
            NativeOcclusionShaderIdlePerf.GuardFloat(9, 1.5f); // 完全一致: 省く
            NativeOcclusionShaderIdlePerf.GuardFloat(9, 1.5f + 1e-6f); // わずかな変化: 送る
            NativeOcclusionShaderIdlePerf.GuardFloat(10, 1.5f + 1e-6f); // ID が変わった: 送る
            Assert.Equal(new[]
            {
                "float 9 1.5",
                "float 9 " + (1.5f + 1e-6f).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                "float 10 " + (1.5f + 1e-6f).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            }, Shader.Calls);
        }

        [Fact]
        public void GuardVectorSkipsOnlyExactRepeats()
        {
            var camera = new CameraManager();
            ResetState(camera);
            var v = new Vector4(1f, 2f, 3f, 4f);
            NativeOcclusionShaderIdlePerf.GuardVector(11, v);
            NativeOcclusionShaderIdlePerf.GuardVector(11, v); // 完全一致: 省く
            // 成分をどれか1つだけ 1e-6 動かす。Unity の operator== の近似（kEpsilon 未満を
            // 一致と見なす）なら省かれる差だが、成分ごとの完全一致ならすべて送る。
            NativeOcclusionShaderIdlePerf.GuardVector(11, new Vector4(v.x + 1e-6f, v.y, v.z, v.w));
            NativeOcclusionShaderIdlePerf.GuardVector(11, new Vector4(v.x, v.y + 1e-6f, v.z, v.w));
            NativeOcclusionShaderIdlePerf.GuardVector(11, new Vector4(v.x, v.y, v.z + 1e-6f, v.w));
            NativeOcclusionShaderIdlePerf.GuardVector(11, new Vector4(v.x, v.y, v.z, v.w + 1e-6f));
            // 最後に送った値がキャッシュになり、まったく同じ値なら再び省く。
            var moved = new Vector4(v.x, v.y, v.z, v.w + 1e-6f);
            NativeOcclusionShaderIdlePerf.GuardVector(11, moved);
            NativeOcclusionShaderIdlePerf.GuardVector(11, moved);
            Assert.Equal(new[]
            {
                "vector 11 " + Shader.Format(v),
                "vector 11 " + Shader.Format(new Vector4(v.x + 1e-6f, v.y, v.z, v.w)),
                "vector 11 " + Shader.Format(new Vector4(v.x, v.y + 1e-6f, v.z, v.w)),
                "vector 11 " + Shader.Format(new Vector4(v.x, v.y, v.z + 1e-6f, v.w)),
                "vector 11 " + Shader.Format(moved),
            }, Shader.Calls);
        }

        [Fact]
        public void GuardVectorArraySkipsOnlyExactRepeats()
        {
            var camera = new CameraManager();
            ResetState(camera);
            var values = new Vector4[16];
            for (int i = 0; i < values.Length; i++) values[i] = new Vector4(i, i, i, 0.5f);
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, values);
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, values); // 完全一致: 省く
            var changed = (Vector4[])values.Clone();
            changed[15] = new Vector4(15f, 15f, 15f, 0.5f + 1e-6f); // 1要素の1成分だけ: 送る
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, changed);
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, changed); // 再び一致: 省く
            // null と長さ違いは本体では起きない形。素通しで送り、キャッシュを無効化する。
            // 次の正しい配列は前回と同じ内容でも送り直される。
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, null);
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, changed);
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, new Vector4[4]);
            NativeOcclusionShaderIdlePerf.GuardVectorArray(12, changed);
            Assert.Equal(new[]
            {
                "vectorarray 12 16",
                "vectorarray 12 16",
                "vectorarray 12 null",
                "vectorarray 12 16",
                "vectorarray 12 4",
                "vectorarray 12 16",
            }, Shader.Calls);
        }

        [Fact]
        public void DisabledOptionPassesEveryCallThrough()
        {
            var camera = new CameraManager();
            ResetState(camera);
            NativePerfOptions.OcclusionIdle = false;
            try
            {
                NativeOcclusionShaderIdlePerf.GuardInt(3, 1);
                NativeOcclusionShaderIdlePerf.GuardInt(3, 1); // 同値でも素通し
                var values = new Vector4[16];
                NativeOcclusionShaderIdlePerf.GuardVectorArray(4, values);
                NativeOcclusionShaderIdlePerf.GuardVectorArray(4, values);
                Assert.Equal(new[] { "int 3 1", "int 3 1", "vectorarray 4 16", "vectorarray 4 16" }, Shader.Calls);
            }
            finally { NativePerfOptions.OcclusionIdle = true; }
        }

        [Fact]
        public void PrefixDropsCacheWhenManagerInstanceChanges()
        {
            var first = new CameraManager();
            ResetState(first);
            NativeOcclusionShaderIdlePerf.GuardInt(5, 9);
            NativeOcclusionShaderIdlePerf.GuardInt(5, 9); // 同じティック内では省く
            var second = new CameraManager();
            Tick(second); // 別インスタンスの初回ティック
            NativeOcclusionShaderIdlePerf.GuardInt(5, 9); // 計算値が同じでも送り直す
            NativeOcclusionShaderIdlePerf.GuardInt(5, 9); // 以後は省く
            Assert.Equal(new[] { "int 5 9", "int 5 9" }, Shader.Calls);
        }

        [Fact]
        public void OnDestroyPostfixDropsCacheAfterDirectReset()
        {
            // レビューで指摘された経路の再現: ティックを持たない重複 CameraManager が
            // OnDestroy で件数0・係数1をガードの外へ直接書く。Postfix がキャッシュを
            // 捨てないと、生きているインスタンスの計算値が変わらない間は省略が続き、
            // GPU 側は 0/1 のまま遮蔽透過が効かなくなる。
            var live = new CameraManager();
            ResetState(live);
            NativeOcclusionShaderIdlePerf.GuardInt(6, 3);
            NativeOcclusionShaderIdlePerf.GuardInt(6, 3); // 省く
            OnDestroyPostfix(); // 重複インスタンスの OnDestroy の直後に動く Postfix
            NativeOcclusionShaderIdlePerf.GuardInt(6, 3); // 計算値は同じでも送り直す
            NativeOcclusionShaderIdlePerf.GuardInt(6, 3); // 以後は省く
            Assert.Equal(new[] { "int 6 3", "int 6 3" }, Shader.Calls);
            // OnDestroy 後の最初のティックでも Prefix はキャッシュを捨て直す（owner が
            // null になっているため、どのインスタンスでも初回扱いになる）。
            Tick(live);
            NativeOcclusionShaderIdlePerf.GuardInt(6, 3);
            Assert.Equal(new[] { "int 6 3", "int 6 3", "int 6 3" }, Shader.Calls);
        }
    }
}
