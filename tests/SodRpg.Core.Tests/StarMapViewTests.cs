using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    public class StarMapViewTests
    {
        [Theory]
        [InlineData(-20f, 6f, 20f, 6f)]
        [InlineData(-20f, 4f, 0f, 14f)] // Overlapping AABBs do not guarantee a segment intersection.
        [InlineData(11f, 0f, 11f, 0f)]
        public void Edge_clipping_rejects_nonintersections_even_when_bounding_boxes_overlap(
            float ax, float ay, float bx, float by)
        {
            Assert.False(StarMapMath.TryClipEdge(new StarMapPoint(ax, ay), new StarMapPoint(bx, by),
                new StarMapBounds(-10f, -5f, 10f, 5f), out _, out _));
        }

        [Theory]
        [InlineData("Solar", "Heal", "", false)]
        [InlineData(null, "回復 HEAL", "heal", true)]
        public void Search_matches_names_or_effects_without_case_or_language_restrictions(
            string name, string description, string query, bool expected)
        {
            Assert.Equal(expected, StarMapMath.Matches(name, description, query));
        }

        [Theory]
        [InlineData(0.15f, 0, 39, 0, 19, 128)]
        [InlineData(3f, 19, 20, 9, 10, 800)]
        public void Eight_hundred_node_map_culls_the_expected_grid_and_deduplicates_labels(
            float zoom, int firstColumn, int lastColumn, int firstRow, int lastRow, int expectedLabelCells)
        {
            var layout = StarMapMath.BuildSyntheticLayout();
            Assert.Equal(800, layout.Length);
            var viewport = new StarMapBounds(-400f, -225f, 400f, 225f);
            var visible = new List<int>();
            var expectedVisible = new List<int>();
            var labelCells = new HashSet<long>();
            for (int row = 0; row < 20; row++)
            {
                for (int column = 0; column < 40; column++)
                {
                    int index = row * 40 + column;
                    var point = layout[index];
                    float x = point.X * zoom;
                    float y = point.Y * zoom;
                    if (StarMapMath.IsVisible(x, y, 8f * zoom, viewport)) visible.Add(index);
                    if (column >= firstColumn && column <= lastColumn && row >= firstRow && row <= lastRow)
                        expectedVisible.Add(index);
                    labelCells.Add(StarMapMath.LabelCell(x, y));
                }
            }
            Assert.Equal(expectedVisible, visible);
            Assert.Equal(expectedLabelCells, labelCells.Count);
        }

        [Theory]
        [InlineData(1f, 8, 11)]
        [InlineData(3f, 9, 10)]
        public void Grid_spanning_edges_survive_culling_when_their_endpoints_are_offscreen(
            float zoom, int firstVisibleRow, int lastVisibleRow)
        {
            var layout = StarMapMath.BuildSyntheticLayout();
            var viewport = new StarMapBounds(-400f, -225f, 400f, 225f);
            for (int row = 0; row < 20; row++)
            {
                var left = layout[row * 40];
                var right = layout[row * 40 + 39];
                var a = new StarMapPoint(left.X * zoom, left.Y * zoom);
                var b = new StarMapPoint(right.X * zoom, right.Y * zoom);
                bool expected = row >= firstVisibleRow && row <= lastVisibleRow;
                Assert.Equal(expected, StarMapMath.TryClipEdge(a, b, viewport, out var clippedA, out var clippedB));
                if (!expected) continue;
                if (zoom >= 1f)
                {
                    Assert.False(StarMapMath.IsVisible(a.X, a.Y, 8f * zoom, viewport));
                    Assert.False(StarMapMath.IsVisible(b.X, b.Y, 8f * zoom, viewport));
                }
                AssertPoint(Math.Max(-400f, a.X), a.Y, clippedA);
                AssertPoint(Math.Min(400f, b.X), b.Y, clippedB);
            }
        }

        [Theory]
        [InlineData("hEaL", 7)]
        [InlineData("回復", 7)]
        [InlineData("missing", 0)]
        public void Searching_eight_hundred_nodes_returns_only_matching_names_or_effects(string query, int divisor)
        {
            var layout = StarMapMath.BuildSyntheticLayout();
            var actual = new List<int>();
            var expected = new List<int>();
            for (int index = 0; index < layout.Length; index++)
            {
                string name = index % 5 == 0 ? "Solar 星 " + index : "星 " + index;
                string effect = index % 7 == 0 ? "回復 HEAL" : "防御";
                if (StarMapMath.Matches(name, effect, query)) actual.Add(index);
                if (divisor != 0 && index % divisor == 0) expected.Add(index);
            }
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(3f)]
        public void Panning_to_each_of_eight_hundred_nodes_centers_it_at_every_zoom(float zoom)
        {
            foreach (var node in StarMapMath.BuildSyntheticLayout())
            {
                var pan = StarMapMath.PanToNode(node.X, node.Y, zoom);
                AssertPoint(640f, 360f, new StarMapPoint(640f + node.X * zoom + pan.X, 360f + node.Y * zoom + pan.Y));
            }
        }

        private static void AssertPoint(float expectedX, float expectedY, StarMapPoint actual)
        {
            Assert.InRange(actual.X, expectedX - 0.001f, expectedX + 0.001f);
            Assert.InRange(actual.Y, expectedY - 0.001f, expectedY + 0.001f);
        }

        [Theory]
        [InlineData(1000f, 700f, -100f, 500f, -400f, 200f)]
        [InlineData(300f, 900f, -250f, 250f, -250f, 250f)]
        public void Fit_view_keeps_every_extreme_star_inside_the_margin(float w, float h, float minX, float maxX, float minY, float maxY)
        {
            StarMapMath.FitView(w, h, minX, maxX, minY, maxY, 64f, 0.01f, 3f, out float zoom, out var pan);
            // 表示領域の中心を原点とした画面座標
            float left = minX * zoom + pan.X, right = maxX * zoom + pan.X, top = minY * zoom + pan.Y, bottom = maxY * zoom + pan.Y;
            Assert.True(left >= -w / 2f + 64f - 0.01f, "left");
            Assert.True(right <= w / 2f - 64f + 0.01f, "right");
            Assert.True(top >= -h / 2f + 64f - 0.01f, "top");
            Assert.True(bottom <= h / 2f - 64f + 0.01f, "bottom");
            Assert.InRange(left + right, -0.01f, 0.01f);
            Assert.InRange(top + bottom, -0.01f, 0.01f);
        }

        [Fact]
        public void Fit_view_clamps_zoom_and_survives_tiny_views()
        {
            StarMapMath.FitView(10f, 10f, -500f, 500f, -500f, 500f, 64f, 0.15f, 3f, out float small, out _);
            Assert.Equal(0.15f, small);
            StarMapMath.FitView(5000f, 5000f, -1f, 1f, -1f, 1f, 64f, 0.15f, 3f, out float large, out _);
            Assert.Equal(3f, large);
        }

        [Theory]
        [InlineData("Hero_Mist", "ミスト", "Mist")]
        [InlineData("Hero_Cetus", "ケトゥス", "Cetus")]
        [InlineData("Hero_Husk", "空殻", "Husk")]
        [InlineData("Hero_Unknown", "Unknown", "Unknown")]
        public void Hero_names_are_japanese_in_japanese(string key, string ja, string en)
        {
            var name = HeroNames.Of(key);
            Assert.Equal(ja, name.Ja);
            Assert.Equal(en, name.En);
        }


        [Theory]
        [InlineData(0f, 3f, 20f, 1000f, 300f, 60f)]
        [InlineData(100f, -3f, 20f, 1000f, 300f, 40f)]
        [InlineData(10f, -3f, 20f, 1000f, 300f, 0f)]
        [InlineData(690f, 3f, 20f, 1000f, 300f, 700f)]
        [InlineData(5f, 3f, 20f, 200f, 300f, 0f)]
        [InlineData(0f, 3f, 20f, float.PositiveInfinity, 300f, 60f)]
        public void Wheel_scroll_moves_and_clamps(float current, float delta, float step, float content, float view, float expected)
        {
            Assert.Equal(expected, ScrollMath.Wheel(current, delta, step, content, view));
        }


    }
}
