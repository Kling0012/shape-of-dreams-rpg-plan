using System;
using System.Collections.Generic;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#890）ボス予告の線分描画の軽量化（隣接線分が共有する頂点の投影を1回にまとめる）の等価性の検証。
    /// 実ソースから切り出してリンクした純関数 BossSegmentPlacement.TryPlace そのものに、
    /// カリング・矩形・回転が従来の式のまま残っていることと、各図形（リング・ファン・ストリップ・
    /// 放射スポーク）の頂点を1回ずつ投影して使い回しても、線分1本ごとに両端点を投影していた
    /// 従来の経路と、描く線分の本数・順番・向き・矩形・角度が1つも変わらないことを確かめる。
    /// 投影は WorldToScreenPoint の決定的な代役（同じ Repaint 内で同じワールド点には常に同じ
    /// 画面座標を返す）で置き換える。WorldToScreenPoint は native 呼び出しのためテストからは
    /// 呼べないが、この決定性こそが「使い回しても画面座標が変わらない」ことの前提である。
    /// あわせて、予算が尽きたときにスポークの中心投影をスキップする挙動（旧経路の線分ごとの
    /// 早期リターンと新経路のガード）が一致することを確かめる。
    /// </summary>
    public sealed class BossSegmentPlacementTests
    {
        private const int ScreenWidth = 1920, ScreenHeight = 1080;

        private readonly struct Placement
        {
            internal readonly bool Placed;
            internal readonly Vector2 Pivot;
            internal readonly float Angle;
            internal readonly Rect Rect;
            internal Placement(bool placed, Vector2 pivot, float angle, Rect rect)
            { Placed = placed; Pivot = pivot; Angle = angle; Rect = rect; }
        }

        // Camera.WorldToScreenPoint の決定的な代役。同じワールド点を2回投影すれば同じ値を返し
        // （使い回しの等価性の前提）、呼び出し回数も数える（軽量化の効果の確認用）。
        // ワールド XZ 平面を画面へ8倍で写し、Y（高さ）は手前の深度へ反映する。
        private sealed class FakeCamera
        {
            internal int Projections;
            internal Vector3 Project(Vector3 world)
            {
                Projections++;
                return new Vector3(world.x * 8f + 960f, world.z * 8f + 540f, 60f - world.y);
            }
        }

        private static Placement Place(Vector3 a, Vector3 b, float width)
        {
            bool placed = BossSegmentPlacement.TryPlace(a, b, width, ScreenWidth, ScreenHeight,
                out var pivot, out float angle, out var rect);
            return new Placement(placed, pivot, angle, rect);
        }

        // 従来の経路（DrawBossWorldLine）: 線分1本ごとに両端点を新しく投影してから配置に渡す。
        private static List<Placement> PerLine(FakeCamera camera, Vector3[] vertices, int[][] pairs, float width)
        {
            var placements = new List<Placement>();
            foreach (var pair in pairs)
                placements.Add(Place(camera.Project(vertices[pair[0]]), camera.Project(vertices[pair[1]]), width));
            return placements;
        }

        // 新しい経路（DrawBossWorldSegment と各図形ヘルパー）: 頂点を1回ずつ投影して使い回す。
        // pairs は実ソースの図形ヘルパーが線分を足す順番・向きと同じ (fromIndex, toIndex) の列。
        private static List<Placement> Shared(FakeCamera camera, Vector3[] vertices, int[][] pairs, float width)
        {
            var screen = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) screen[i] = camera.Project(vertices[i]);
            var placements = new List<Placement>();
            foreach (var pair in pairs) placements.Add(Place(screen[pair[0]], screen[pair[1]], width));
            return placements;
        }

        private static void AssertSamePlacements(List<Placement> expected, List<Placement> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Placed, actual[i].Placed);
                if (!expected[i].Placed) continue;
                Assert.Equal(expected[i].Pivot.x, actual[i].Pivot.x);
                Assert.Equal(expected[i].Pivot.y, actual[i].Pivot.y);
                Assert.Equal(expected[i].Angle, actual[i].Angle);
                Assert.Equal(expected[i].Rect.x, actual[i].Rect.x);
                Assert.Equal(expected[i].Rect.y, actual[i].Rect.y);
                Assert.Equal(expected[i].Rect.width, actual[i].Rect.width);
                Assert.Equal(expected[i].Rect.height, actual[i].Rect.height);
            }
        }

        // 図形（従来経路・新経路とも同じ期待値）を両経路で走らせ、描画の並びが一致し、
        // 投影回数が期待どおり減ることを確かめる。
        private static void AssertFigureIsEquivalent(Vector3[] vertices, int[][] pairs, float width,
            int expectedSharedProjections)
        {
            var perLineCamera = new FakeCamera();
            var perLine = PerLine(perLineCamera, vertices, pairs, width);
            var sharedCamera = new FakeCamera();
            var shared = Shared(sharedCamera, vertices, pairs, width);
            AssertSamePlacements(perLine, shared);
            Assert.Equal(pairs.Length * 2, perLineCamera.Projections);
            Assert.Equal(expectedSharedProjections, sharedCamera.Projections);
        }

        // Quaternion.Euler(0, degree, 0) * v と同じ Y 軸回転（ワールド XZ 平面）。
        private static Vector3 RotateAroundY(Vector3 v, float degrees)
        {
            float radians = (float)(degrees * Math.PI / 180.0), c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
        }

        private static Vector3[] RingVertices(Vector3 center, float radius, int segments)
        {
            var vertices = new Vector3[segments + 1];
            vertices[0] = new Vector3(center.x + radius, center.y, center.z);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * (float)(Math.PI * 2.0 / segments);
                vertices[i] = new Vector3(center.x + (float)Math.Cos(angle) * radius,
                    center.y + .03f, center.z + (float)Math.Sin(angle) * radius);
            }
            return vertices;
        }

        private static int[][] RingPairs(int segments)
        {
            var pairs = new int[segments][];
            for (int i = 0; i < segments; i++) pairs[i] = new[] { i, i + 1 };
            return pairs;
        }

        [Fact]
        public void TryPlaceMatchesTheHandComputedQuadForAnOnScreenDiagonal()
        {
            var placement = Place(new Vector3(100f, 200f, 10f), new Vector3(300f, 100f, 10f), 4f);
            Assert.True(placement.Placed);
            Assert.Equal(100f, placement.Pivot.x);
            Assert.Equal(ScreenHeight - 200f, placement.Pivot.y);
            Assert.Equal((float)Math.Atan2(100f, 200f) * (float)(180.0 / Math.PI), placement.Angle);
            Assert.Equal(100f, placement.Rect.x);
            Assert.Equal(ScreenHeight - 200f - 2f, placement.Rect.y);
            Assert.Equal((float)Math.Sqrt(200f * 200f + 100f * 100f), placement.Rect.width);
            Assert.Equal(4f, placement.Rect.height);
        }

        [Fact]
        public void TryPlaceCullsOnlyWhenBothEndpointsLeaveTheSameEdgeOrTheCamera()
        {
            // 片方だけ画面外 → 描く。
            Assert.True(Place(new Vector3(-50f, 300f, 10f), new Vector3(100f, 300f, 10f), 2f).Placed);
            Assert.True(Place(new Vector3(1500f, 300f, 10f), new Vector3(2500f, 300f, 10f), 2f).Placed);
            // 両端が同じ外側 → 描かない。
            Assert.False(Place(new Vector3(-50f, 300f, 10f), new Vector3(-20f, 300f, 10f), 2f).Placed);
            Assert.False(Place(new Vector3(2000f, 300f, 10f), new Vector3(2500f, 300f, 10f), 2f).Placed);
            Assert.False(Place(new Vector3(100f, 1200f, 10f), new Vector3(100f, 1300f, 10f), 2f).Placed);
            Assert.False(Place(new Vector3(100f, -10f, 10f), new Vector3(100f, -50f, 10f), 2f).Placed);
            // カメラの後ろ（z <= 0）→ 描かない。
            Assert.False(Place(new Vector3(100f, 300f, 0f), new Vector3(100f, 300f, 10f), 2f).Placed);
            Assert.False(Place(new Vector3(100f, 300f, 10f), new Vector3(100f, 300f, -1f), 2f).Placed);
            // 非有限値 → 描かない。
            Assert.False(Place(new Vector3(float.NaN, 300f, 10f), new Vector3(100f, 300f, 10f), 2f).Placed);
            Assert.False(Place(new Vector3(100f, 300f, 10f), new Vector3(100f, float.PositiveInfinity, 10f), 2f).Placed);
        }

        [Fact]
        public void TryPlaceSkipsDegenerateSegments()
        {
            // 同一点、および長さ 0.1 未満の線分は描かない（本体の .1f しきい値と同じ）。
            Assert.False(Place(new Vector3(100f, 300f, 10f), new Vector3(100f, 300f, 10f), 2f).Placed);
            Assert.False(Place(new Vector3(100f, 300f, 10f), new Vector3(100.03f, 300.04f, 10f), 2f).Placed);
            Assert.True(Place(new Vector3(100f, 300f, 10f), new Vector3(100.5f, 300f, 10f), 2f).Placed);
        }

        [Fact]
        public void SharedRingVerticesKeepThePerLineArrangement()
        {
            // 画面内のリング: 24線分の順番・向き・矩形が1つも変わらず、投影は 48→25 回。
            AssertFigureIsEquivalent(RingVertices(new Vector3(0f, 0f, 0f), 12f, 24), RingPairs(24), 2f, 25);
            // 画面の右へ外れたリング: 全線分がカリングされるが、その並びも従来どおり。
            AssertFigureIsEquivalent(RingVertices(new Vector3(140f, 0f, 0f), 12f, 24), RingPairs(24), 2f, 25);
            // カメラの後ろにあるリング: 深度カリングが従来どおり各線分に働く。
            AssertFigureIsEquivalent(RingVertices(new Vector3(0f, 80f, 0f), 12f, 24), RingPairs(24), 2f, 25);
        }

        [Fact]
        public void SharedFanVerticesKeepThePerLineArrangement()
        {
            const int segments = 16;
            // 中心（頂点0）・弧の両端（1..s+1）。線分は 中心→弧0 → 弧0..弧s → 弧s→中心 の順。
            var vertices = new Vector3[segments + 2];
            var center = new Vector3(110f, 0f, 0f);
            vertices[0] = center;
            for (int i = 0; i <= segments; i++)
            {
                float degrees = -90f + 180f * i / segments;
                var rotated = RotateAroundY(new Vector3(1f, 0f, 0f), degrees);
                vertices[i + 1] = new Vector3(center.x + rotated.x * 30f, 0f, center.z + rotated.z * 30f);
            }
            var pairs = new int[segments + 2][];
            pairs[0] = new[] { 0, 1 };
            for (int i = 1; i <= segments; i++) pairs[i] = new[] { i, i + 1 };
            pairs[segments + 1] = new[] { segments + 1, 0 };
            // 画面内と右端にまたがるファン（描く線分とカリングされる線分が混ざる）。投影は 36→18 回。
            AssertFigureIsEquivalent(vertices, pairs, 2f, segments + 2);
        }

        [Fact]
        public void SharedStripCornersKeepThePerLineArrangement()
        {
            // ストリップの4隅（start左・start右・end左・end右）を1回ずつ投影して
            // 長辺2本→両端のふた2本の順。投影は 8→4 回。
            var vertices = new[]
            {
                new Vector3(-20f, 0f, -13f), new Vector3(-20f, 0f, -7f),
                new Vector3(20f, 0f, -13f), new Vector3(20f, 0f, -7f)
            };
            var pairs = new[] { new[] { 0, 2 }, new[] { 1, 3 }, new[] { 0, 1 }, new[] { 2, 3 } };
            AssertFigureIsEquivalent(vertices, pairs, 4f, 4);
        }

        [Fact]
        public void SharedSpokeVerticesKeepThePerLineArrangement()
        {
            const int count = 8, spokes = 8;
            var center = new Vector3(0f, 0f, 0f);
            var direction = new Vector3(0f, 0f, 1f);
            // 矢頭なし（kind != 4）: 中心→各先端のみ。投影は 16→9 回。
            var plain = new Vector3[spokes + 1];
            var plainPairs = new int[spokes][];
            plain[0] = center;
            for (int i = 0; i < spokes; i++)
            {
                var spoke = RotateAroundY(direction, 360f * (i * count / spokes) / count);
                plain[i + 1] = new Vector3(center.x + spoke.x * 20f, 0f, center.z + spoke.z * 20f);
                plainPairs[i] = new[] { 0, i + 1 };
            }
            AssertFigureIsEquivalent(plain, plainPairs, 2f, spokes + 1);

            // 矢頭あり（kind == 4）・外向き: 矢頭の頭は2線分で共有する。投影は 48→33 回。
            AssertArrowSpokes(center, direction, count, spokes, inward: false);
            // 矢頭あり・内向き（angle == 180）: 頭は中心近くの別頂点。投影は 48→33 回。
            AssertArrowSpokes(center, direction, count, spokes, inward: true);
        }

        private static void AssertArrowSpokes(Vector3 center, Vector3 direction, int count, int spokes, bool inward)
        {
            // 頂点は 中心 + スポークごとに 先端・頭・翼2 → 1 + spokes*4 個。
            // 外向きでは頭の座標は先端と同じだが、本体（ClientSession.BossVisuals.cs）は
            // 主線の先端と矢頭の頭を別々に投影するため、ここも別頂点として数える。
            var vertices = new Vector3[1 + spokes * 4];
            var pairs = new int[spokes * 3][];
            vertices[0] = center;
            for (int i = 0; i < spokes; i++)
            {
                var spoke = RotateAroundY(direction, 360f * (i * count / spokes) / count);
                var tip = new Vector3(center.x + spoke.x * 20f, 0f, center.z + spoke.z * 20f);
                // 旧コードと同じ式: head は内向きなら中心近く、外向きなら先端そのもの。
                var head = inward
                    ? new Vector3(center.x + spoke.x * .4f, 0f, center.z + spoke.z * .4f)
                    : tip;
                var arrow = inward ? new Vector3(-spoke.x, 0f, -spoke.z) : spoke;
                var wing1 = Minus(head, Multiply(RotateAroundY(arrow, 35f), .35f));
                var wing2 = Minus(head, Multiply(RotateAroundY(arrow, -35f), .35f));
                int tipIndex = 1 + i * 4, headIndex = tipIndex + 1, wing1Index = headIndex + 1, wing2Index = wing1Index + 1;
                vertices[tipIndex] = tip; vertices[headIndex] = head;
                vertices[wing1Index] = wing1; vertices[wing2Index] = wing2;
                pairs[i * 3] = new[] { 0, tipIndex };
                pairs[i * 3 + 1] = new[] { headIndex, wing1Index };
                pairs[i * 3 + 2] = new[] { headIndex, wing2Index };
            }
            AssertFigureIsEquivalent(vertices, pairs, 2f, 1 + spokes * 4);
        }

        private static Vector3 Minus(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        private static Vector3 Multiply(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);

        [Fact]
        public void SpokeCenterProjectionIsSkippedWhenTheBudgetCannotFundASpoke()
        {
            // 予算が尽きた状態でのスポーク: 旧経路（線分ごとの早期リターン）も新経路のガード
            // （spokes > 0 かつ予算が必要数以上のときだけ中心を投影）も、1本も描けないときは
            // 投影0回・描画0本で一致する。spokes=0 のときも中心は投影しない。
            Assert.Equal((0, 0), OldSpokePath(8, true, 2));
            Assert.Equal((0, 0), NewSpokePath(8, true, 2));
            Assert.Equal((0, 0), OldSpokePath(8, false, 0));
            Assert.Equal((0, 0), NewSpokePath(8, false, 0));
            Assert.Equal((0, 0), OldSpokePath(0, true, 2048));
            Assert.Equal((0, 0), NewSpokePath(0, true, 2048));
            // 予算が途中で尽きる場合も、描く本数と消費は同じで投影だけ減る。
            Assert.Equal((6, 3), OldSpokePath(4, true, 4));
            Assert.Equal((5, 3), NewSpokePath(4, true, 4));
            Assert.Equal((18, 9), OldSpokePath(3, true, 2048));
            Assert.Equal((13, 9), NewSpokePath(3, true, 2048));
            Assert.Equal((16, 8), OldSpokePath(8, false, 2048));
            Assert.Equal((9, 8), NewSpokePath(8, false, 2048));
        }

        // 旧経路: ループ条件はそのままに、各線分（旧 DrawBossWorldLine）は予算 <= 0 なら投影しない。
        private static (int Projections, int Drawn) OldSpokePath(int spokes, bool arrows, int budget)
        {
            var camera = new FakeCamera();
            int drawn = 0;
            for (int i = 0; i < spokes && budget >= (arrows ? 3 : 1); i++)
            {
                if (budget > 0) { camera.Project(new Vector3(0f, 0f, 0f)); camera.Project(new Vector3(i, 0f, 20f)); budget--; drawn++; }
                if (arrows)
                {
                    if (budget > 0) { camera.Project(new Vector3(i, 0f, 20f)); camera.Project(new Vector3(i + .1f, 0f, 19f)); budget--; drawn++; }
                    if (budget > 0) { camera.Project(new Vector3(i, 0f, 20f)); camera.Project(new Vector3(i - .1f, 0f, 19f)); budget--; drawn++; }
                }
            }
            return (camera.Projections, drawn);
        }

        // 新経路: 中心の投影はガードの後ろで1回だけ。各線分は使い回した頂点を渡す。
        private static (int Projections, int Drawn) NewSpokePath(int spokes, bool arrows, int budget)
        {
            var camera = new FakeCamera();
            int drawn = 0;
            if (spokes > 0 && budget >= (arrows ? 3 : 1))
            {
                camera.Project(new Vector3(0f, 0f, 0f));
                for (int i = 0; i < spokes && budget >= (arrows ? 3 : 1); i++)
                {
                    camera.Project(new Vector3(i, 0f, 20f)); budget--; drawn++;
                    if (arrows)
                    {
                        // 矢頭の頭は1回だけ投影して2線分で使い回す。
                        camera.Project(new Vector3(i, 0f, 20f));
                        camera.Project(new Vector3(i + .1f, 0f, 19f)); budget--; drawn++;
                        camera.Project(new Vector3(i - .1f, 0f, 19f)); budget--; drawn++;
                    }
                }
            }
            return (camera.Projections, drawn);
        }
    }
}
