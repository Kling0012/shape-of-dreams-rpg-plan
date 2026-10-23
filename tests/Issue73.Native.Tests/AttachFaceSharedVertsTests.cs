using System;
using System.Collections;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1084）焼き直しフレームの全頂点コピーをスキンごとの共有にする
    /// NativeAttachFaceSharedVerts の検証。模倣 Dew.GetBakedMeshOptimized に焼き直しの窓
    /// （Dew.BakedMesh / Dew.BakedMeshIsNew）を開けて Prefix の isNew 経路を直接動かし、
    /// (1) 同じスキン・同じフレームなら Mesh.GetVertices は1回だけ走り、それぞれの
    /// vertexIndex の頂点から位置と向きを出すこと、(2) 転写した算術（中心計算・
    /// GetFaceRotation・TransformPointUnscaled・t.rotation * faceRotation * relRotation の
    /// 合成順序）が回転として意味のある Quaternion 境界で手計算の期待値と一致すること、
    /// (3) isNew=false・記録なし・間隔内の省略窓・設定 OFF・付け先なし・vertexIndex0 が
    /// 負のときは本体に返ること、(4) 頂点番号が範囲外ならその呼び出しだけ本体に
    /// 返り、正常な装飾品では共有を続けること、(5) 時刻が変わる（次の焼き直しの）フレームでは埋め直すこと、
    /// (6) 件数がしきい値を超えたら、まだ焼き直しが続いている項目は残して
    /// 間隔の数倍を過ぎた項目だけを掃除すること、を確かめる。
    /// </summary>
    public sealed class AttachFaceSharedVertsTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        // √2/2（90° 回転の成分）、sin67.5°・cos67.5°（Z 軸 135° 回転の成分）。
        private const float S = 0.70710677f;
        private const float Az = 0.92387953f;
        private const float Bz = 0.38268343f;

        private static IDictionary SharedTable()
            => (IDictionary)typeof(NativeAttachFaceSharedVerts).GetField("Shared", Hidden).GetValue(null);

        private static void ResetPruneClock()
            => typeof(NativeAttachFaceSharedVerts).GetField("_lastPruneTime", Hidden).SetValue(null, 0f);

        private static bool Prefix(AttachToFace face, out Vector3 position, out Quaternion rotation, out bool result)
        {
            var method = typeof(NativeAttachFaceSharedVerts).GetMethod("Prefix", Hidden);
            Assert.NotNull(method);
            object[] args = { face, null, null, false };
            bool runsOriginal = (bool)method.Invoke(null, args);
            position = (Vector3)args[1];
            rotation = (Quaternion)args[2];
            result = (bool)args[3];
            return runsOriginal;
        }

        private static Quaternion GetFaceRotation(Vector3 a, Vector3 b, Vector3 c)
        {
            var method = typeof(NativeAttachFaceSharedVerts).GetMethod("GetFaceRotation", Hidden);
            Assert.NotNull(method);
            return (Quaternion)method.Invoke(null, new object[] { a, b, c });
        }

        private static Mesh BakedMeshOf(params Vector3[] vertices)
        {
            var mesh = new Mesh();
            mesh.Vertices.AddRange(vertices);
            return mesh;
        }

        // 前回の焼き直しから間隔が過ぎたスキンに、このフレームで焼き直しが起きた
        // （isNew=true・mesh を返す）模倣の窓を開く。Prefix が読む記録は Prefix 自身の
        // GetBakedMeshOptimized 呼び出しより前の状態（前回の焼き直し時刻）を写す。
        private static void ArmBake(SkinnedMeshRenderer skin, Mesh mesh, float previousBakeAt, float now, bool isNew = true)
        {
            NativeAttachFaceIdlePerf.LastNewBake[skin.GetInstanceID()] = previousBakeAt;
            Time.unscaledTime = now;
            Dew.BakedMesh = mesh;
            Dew.BakedMeshIsNew = isNew;
        }

        private static AttachToFace Face(SkinnedMeshRenderer skin, int i0, int i1, int i2,
            Vector3 relPosition, Quaternion relRotation)
            => new AttachToFace
            {
                targetRenderer = skin,
                vertexIndex0 = i0,
                vertexIndex1 = i1,
                vertexIndex2 = i2,
                relPosition = relPosition,
                relRotation = relRotation,
            };

        private static void Near(float expected, float actual) => Assert.Equal(expected, actual, 4);

        private static void Near(Vector3 expected, Vector3 actual)
        {
            Near(expected.x, actual.x);
            Near(expected.y, actual.y);
            Near(expected.z, actual.z);
        }

        private static void Near(Quaternion expected, Quaternion actual)
        {
            Near(expected.x, actual.x);
            Near(expected.y, actual.y);
            Near(expected.z, actual.z);
            Near(expected.w, actual.w);
        }

        /// <summary>模倣と追跡器の静的状態を既定へ戻し、使い終わったら元へ復元する。</summary>
        private sealed class Scenario : IDisposable
        {
            private readonly float _savedTime;
            private readonly float _savedInterval;
            private readonly Mesh _savedMesh;
            private readonly bool _savedIsNew;
            private readonly bool _savedIdleOption;
            private readonly bool _savedSharedOption;
            private readonly bool _savedIdleDisabled;
            private readonly bool _savedDisabled;

            public Scenario()
            {
                _savedTime = Time.unscaledTime;
                _savedInterval = Dew.BakeMeshInterval;
                _savedMesh = Dew.BakedMesh;
                _savedIsNew = Dew.BakedMeshIsNew;
                _savedIdleOption = NativePerfOptions.AttachFaceIdle;
                _savedSharedOption = NativePerfOptions.AttachFaceSharedVerts;
                _savedIdleDisabled = NativeAttachFaceIdlePerf.Disabled;
                _savedDisabled = NativeAttachFaceSharedVerts.Disabled;
                Reset();
            }

            private static void Reset()
            {
                Dew.BakeMeshInterval = 0.1f;
                Dew.BakedMesh = null;
                Dew.BakedMeshIsNew = false;
                NativePerfOptions.AttachFaceIdle = true;
                NativePerfOptions.AttachFaceSharedVerts = true;
                NativeAttachFaceIdlePerf.Disabled = false;
                NativeAttachFaceSharedVerts.Disabled = false;
                NativeAttachFaceIdlePerf.LastNewBake.Clear();
                SharedTable().Clear();
                ResetPruneClock();
                Time.unscaledTime = 30f;
            }

            public void Dispose()
            {
                Time.unscaledTime = _savedTime;
                Dew.BakeMeshInterval = _savedInterval;
                Dew.BakedMesh = _savedMesh;
                Dew.BakedMeshIsNew = _savedIsNew;
                NativePerfOptions.AttachFaceIdle = _savedIdleOption;
                NativePerfOptions.AttachFaceSharedVerts = _savedSharedOption;
                NativeAttachFaceIdlePerf.Disabled = _savedIdleDisabled;
                NativeAttachFaceSharedVerts.Disabled = _savedDisabled;
                NativeAttachFaceIdlePerf.LastNewBake.Clear();
                SharedTable().Clear();
                ResetPruneClock();
            }
        }

        [Fact]
        public void PrefixFillsTheVerticesOncePerSkinAndFrameAndServesEachFaceItsOwnVertices()
        {
            using var scenario = new Scenario();
            var skin = new SkinnedMeshRenderer();
            // 付け先の姿勢: 位置 (10,0,0)、X 軸 90° 回転（(x,y,z) → (x,−z,y) に写す）。
            skin.transform.position = new Vector3(10f, 0f, 0f);
            skin.transform.rotation = new Quaternion(S, 0f, 0f, S);
            // 原点の直角三角形と、同じ形を (10,20,30) へ動かした三角形。同じ形なので
            // 面向き（GetFaceRotation）はどちらも Z 軸 135° 回転になる。
            var mesh = BakedMeshOf(
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(10f, 20f, 30f), new Vector3(11f, 20f, 30f), new Vector3(10f, 21f, 30f));
            ArmBake(skin, mesh, previousBakeAt: 29.8f, now: 30f);

            var atOrigin = Face(skin, 0, 1, 2, Vector3.zero, Quaternion.identity);
            var shifted = Face(skin, 3, 4, 5, Vector3.zero, Quaternion.identity);
            var offset = Face(skin, 0, 1, 2, new Vector3(2f, 0f, 0f), new Quaternion(0f, 0f, S, S));

            Assert.False(Prefix(atOrigin, out Vector3 p1, out Quaternion r1, out bool ok1)); // 本体を置き換える
            Assert.True(ok1);
            Assert.False(Prefix(shifted, out Vector3 p2, out Quaternion r2, out bool ok2));
            Assert.True(ok2);
            Assert.False(Prefix(offset, out Vector3 p3, out Quaternion r3, out bool ok3));
            Assert.True(ok3);
            Assert.Equal(1, mesh.GetVerticesCalls); // 同じスキン・同じフレームの全頂点コピーは1回だけ

            // 手計算の期待値。中心 (1/3,1/3,0)・(31/3,61/3,30)、面向き Rz135、付け先 Rx90。
            Near(new Vector3(10f + 1f / 3f, 0f, 1f / 3f), p1);
            Near(new Quaternion(0.27059805f, -0.65328148f, 0.65328148f, 0.27059805f), r1); // Rx90 * Rz135
            Near(new Vector3(20f + 1f / 3f, -30f, 20f + 1f / 3f), p2); // それぞれの vertexIndex の頂点から出す
            Near(r1, r2); // 同じ形の面なので向きは同じ
            // relPosition は面向きで回してから付け先に移す: Rz135*(2,0,0) = (−√2,√2,0)、
            // それを Rx90 で回して (x,−z,y) へ写し、位置 (10,0,0) を足す。
            Near(new Vector3(8.91911977f, 0f, 1.7475469f), p3);
            Near(new Quaternion(-0.27059805f, -0.65328148f, 0.65328148f, -0.27059805f), r3); // Rx90 * Rz135 * Rz90

            // 合成順序の検証（数値の別証）: r3 は relRotation（Z 軸 90°）を先に、
            // 面向き（Z 軸 135°）を次に、付け先（X 軸 90°）を最後に適用する。up を
            // (0.7071,0,−0.7071) へ動かすのはこの順序だけ（順序を入れ替えると
            // (0.7071,−0.7071,0) になる）。
            Near(new Vector3(S, 0f, -S), r3 * Vector3.up);
        }

        [Fact]
        public void GetFaceRotationPointsForwardAlongTheFaceNormal()
        {
            using var scenario = new Scenario();
            // (0,0,0),(1,0,0),(0,1,0) の面: 法線 +Z、二等分線の反対 (−√2/2,−√2/2,0)。
            // 本体の LookRotation は +Z を法線へ、+Y を上へ向けるので、結果は Z 軸 135° 回転。
            Quaternion q = GetFaceRotation(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));
            Near(new Quaternion(0f, 0f, Az, Bz), q);
            Near(new Vector3(0f, 0f, 1f), q * Vector3.forward); // +Z が面の法線を向く
            Near(new Vector3(-S, -S, 0f), q * Vector3.up); // +Y が二等分線の反対を向く

            // 一直線な面（法線なし）は本体どおり identity。
            Quaternion degenerate = GetFaceRotation(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f));
            Assert.Equal(Quaternion.identity, degenerate);
        }

        [Fact]
        public void PrefixReturnsToTheOriginalWhenTheFrameHasNoNewBake()
        {
            using var scenario = new Scenario();
            var skin = new SkinnedMeshRenderer();
            var mesh = BakedMeshOf(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));
            var face = Face(skin, 0, 1, 2, Vector3.zero, Quaternion.identity);

            ArmBake(skin, mesh, previousBakeAt: 29.8f, now: 30f, isNew: false); // 焼き直しのない isNew=false
            Assert.True(Prefix(face, out _, out _, out bool result)); // 本体に返る
            Assert.False(result); // __result は書き換えない
            Assert.Equal(0, mesh.GetVerticesCalls); // 頂点の埋め起こしもしない

            ArmBake(skin, mesh, previousBakeAt: 29.8f, now: 30f);
            NativeAttachFaceIdlePerf.LastNewBake.Remove(skin.GetInstanceID());
            Assert.True(Prefix(face, out _, out _, out _)); // 記録がないスキンは本体に返る
            Assert.Equal(0, mesh.GetVerticesCalls);

            ArmBake(skin, mesh, previousBakeAt: 30f, now: 30.05f); // 焼き直し（30.0）の後の間隔内の省略窓
            Assert.True(Prefix(face, out _, out _, out _));
            Assert.Equal(0, mesh.GetVerticesCalls);

            ArmBake(skin, mesh, previousBakeAt: 29.8f, now: 30f);
            NativePerfOptions.AttachFaceSharedVerts = false; // 設定 OFF
            Assert.True(Prefix(face, out _, out _, out _));
            Assert.Equal(0, mesh.GetVerticesCalls);

            NativePerfOptions.AttachFaceSharedVerts = true;
            Assert.True(Prefix(Face(skin, -1, 1, 2, Vector3.zero, Quaternion.identity), out _, out _, out _)); // 付け先の頂点がない
            Assert.True(Prefix(new AttachToFace { vertexIndex0 = 0 }, out _, out _, out _)); // 付け先の skin がない
            NativeAttachFaceSharedVerts.Disabled = true;
            Assert.True(Prefix(Face(skin, 0, 1, 2, Vector3.zero, Quaternion.identity), out _, out _, out _)); // Disabled でも本体に返る
            Assert.Equal(0, mesh.GetVerticesCalls);
        }

        [Fact]
        public void PrefixFallsBackPerAttachmentWhenAVertexIndexIsOutOfRange()
        {
            using var scenario = new Scenario();
            var skin = new SkinnedMeshRenderer();
            var mesh = BakedMeshOf(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));
            ArmBake(skin, mesh, previousBakeAt: 29.8f, now: 30f);
            int warnings = Log.Warnings.Count;
            var broken = Face(skin, 0, 1, 99, Vector3.zero, Quaternion.identity); // mesh は3頂点しかない

            Assert.True(Prefix(broken, out _, out _, out _)); // この呼び出しだけ本体に返る
            Assert.False(NativeAttachFaceSharedVerts.Disabled); // 正常な装飾品の共有は無効にしない
            Assert.Equal(warnings, Log.Warnings.Count);

            var healthy = Face(skin, 0, 1, 2, Vector3.zero, Quaternion.identity);
            Assert.False(Prefix(healthy, out _, out _, out bool result)); // 正常な装飾品では共有を続ける
            Assert.True(result);
        }

        [Fact]
        public void PrefixRefillsTheVerticesWhenTheNextBakeArrives()
        {
            using var scenario = new Scenario();
            var skin = new SkinnedMeshRenderer();
            skin.transform.position = Vector3.zero;
            skin.transform.rotation = Quaternion.identity;
            var mesh = BakedMeshOf(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));
            var face = Face(skin, 0, 1, 2, Vector3.zero, Quaternion.identity);

            ArmBake(skin, mesh, previousBakeAt: 29.8f, now: 30f);
            Assert.False(Prefix(face, out Vector3 first, out _, out _));
            Near(new Vector3(1f / 3f, 1f / 3f, 0f), first);
            Assert.Equal(1, mesh.GetVerticesCalls);

            // 次の焼き直し（0.2 秒後）で頂点が動く。埋め直して新しい頂点を読む。
            mesh.Vertices.Clear();
            mesh.Vertices.AddRange(new[]
            {
                new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f),
            });
            ArmBake(skin, mesh, previousBakeAt: 30f, now: 30.2f);
            Assert.False(Prefix(face, out Vector3 second, out _, out _));
            Near(new Vector3(5f + 1f / 3f, 1f / 3f, 0f), second);
            Assert.Equal(2, mesh.GetVerticesCalls); // 時刻が変わるフレームでは埋め直す
        }

        [Fact]
        public void PruneStaleKeepsEntriesStillBakingAndDropsOnlyLongStaleOnes()
        {
            using var scenario = new Scenario();
            var mesh = BakedMeshOf(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));

            SkinnedMeshRenderer Fill(float previousBakeAt, float now)
            {
                var skin = new SkinnedMeshRenderer();
                ArmBake(skin, mesh, previousBakeAt, now);
                Assert.False(Prefix(Face(skin, 0, 1, 2, Vector3.zero, Quaternion.identity), out _, out _, out _));
                return skin;
            }

            var old = Fill(previousBakeAt: 99.8f, now: 100f); // 掃除の瞬間から 1.6 秒前の焼き直し
            var recent = Fill(previousBakeAt: 101.25f, now: 101.45f); // 0.15 秒前（間隔 0.1 秒は超えている）
            for (int i = 0; i < 15; i++)
                Fill(previousBakeAt: 101.35f, now: 101.55f); // 0.05 秒前
            Assert.Equal(17, SharedTable().Count); // しきい値（16）を超えても表は消去しない

            var extra = Fill(previousBakeAt: 101.4f, now: 101.6f); // この追加で掃除が走る

            var table = SharedTable();
            Assert.Equal(17, table.Count); // 全消去ではなく古い項目だけが消える
            Assert.False(table.Contains(old.GetInstanceID())); // 間隔の10倍（1秒）を過ぎた項目だけ消える
            // まだ焼き直しが続いている項目（0.15 秒前・0.05 秒前）は残る。掃除の基準が
            // 間隔そのもの（0.1 秒）だと 0.15 秒前の項目も消え、次の焼き直しで全頂点
            // リストを作り直すことになる（原則 II）。
            Assert.True(table.Contains(recent.GetInstanceID()));
            Assert.True(table.Contains(extra.GetInstanceID()));

            var healthy = Face(recent, 0, 1, 2, Vector3.zero, Quaternion.identity);
            ArmBake(recent, mesh, previousBakeAt: 101.45f, now: 101.75f); // recent の次の焼き直し
            Assert.False(Prefix(healthy, out _, out _, out _)); // 掃除の後も共有は続く
        }
    }
}
