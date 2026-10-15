using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    // 星図の差分更新（#440）への直接回帰テスト。NativePersistence.targets が
    // RefreshStarState / RefreshStarClusters / EnsureStarTooltip / EnsureStarChoiceText /
    // DrawStarClusterList を実ソースから抽出し、このテストはそのまま実行する。
    // 境界（StarNode・_star*・IMGUI スタブ）は StarClusterUiBoundary.cs と NativeBoundary.cs。
    public sealed class StarClusterDiffUpdateTests : IDisposable
    {
        private const string Hero = "Hero_Cetus";
        private readonly bool _previousJapanese = Loc.Japanese;

        public void Dispose()
        {
            Loc.Japanese = _previousJapanese;
            Event.current = new Event();
            GUILayoutUtility.ClusterListArea = new Rect(0f, 0f, 270f, 226f);
            GUIStyle.ResetCalcHeightCalls();
        }

        private static HeroTreeLayout Tree => HeroTreeLayout.ForHero(Hero);

        private static DreamforgeUi BuildUi(out Profile profile)
        {
            profile = Profile.CreateNew(31);
            profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            var ui = new DreamforgeUi(new ClientSession { Profile = profile });
            ui.SeedStarTree(Hero);
            ui.UseDeterministicClusterRowStyle();
            return ui;
        }

        private static bool IsPlain(int index)
        {
            var t = Tree.Nodes[index].Talent;
            return t != null && !t.IsKeystone && !t.IsChoice && PairCombos.ForBridge(t.Id) == null;
        }

        // 割り振り経路と同じ規則（刻印と未取得の橋は通らない）で始まりからの距離を求める。
        private static (int[] Distance, int[] Parent) BfsFromStart()
        {
            var tree = Tree;
            var distance = new int[tree.Nodes.Count];
            var parent = new int[tree.Nodes.Count];
            for (int i = 0; i < distance.Length; i++) { distance[i] = -1; parent[i] = -1; }
            var queue = new Queue<int>();
            queue.Enqueue(tree.StartIndex);
            distance[tree.StartIndex] = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in tree.Nodes[current].Neighbors)
                {
                    if (distance[next] >= 0 || tree.Nodes[next].Talent?.IsKeystone == true) continue;
                    var t = tree.Nodes[next].Talent;
                    if (t != null && PairCombos.ForBridge(t.Id) != null && next != current) continue;
                    distance[next] = distance[current] + 1;
                    parent[next] = current;
                    queue.Enqueue(next);
                }
            }
            return (distance, parent);
        }

        // 始まりに近い順の普通の星（二択でも刻印でも橋でもない）。
        private static int[] PlainRows(int count)
        {
            var tree = Tree;
            var (distance, _) = BfsFromStart();
            return tree.Nodes.Select((_, i) => i)
                .Where(i => distance[i] >= 1 && IsPlain(i))
                .OrderBy(i => distance[i])
                .Take(count)
                .ToArray();
        }

        // 始まりから id までの経路を取得でつなぎ、確保した星の id を返す（TreeTestPaths と同じ BFS）。
        private static HashSet<string> Connect(Profile p, string id)
        {
            var tree = HeroTreeLayout.ForHero(Hero);
            var parents = new int[tree.Nodes.Count];
            for (int i = 0; i < parents.Length; i++) parents[i] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(tree.StartIndex);
            parents[tree.StartIndex] = tree.StartIndex;
            int target = -1;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (tree.Nodes[current].Id == id) { target = current; break; }
                foreach (int next in tree.Nodes[current].Neighbors)
                {
                    if (parents[next] >= 0 || tree.Nodes[next].Talent?.IsKeystone == true) continue;
                    var nextTalent = tree.Nodes[next].Talent;
                    if (nextTalent != null && nextTalent.Id != id && PairCombos.ForBridge(nextTalent.Id) != null &&
                        (!p.Hero(Hero).Talents.TryGetValue(nextTalent.Id, out int bridgeRank) || bridgeRank <= 0)) continue;
                    parents[next] = current;
                    queue.Enqueue(next);
                }
            }
            if (target < 0) throw new InvalidOperationException(id);
            var allocated = new HashSet<string>();
            var path = new Stack<int>();
            for (int at = parents[target]; at != tree.StartIndex; at = parents[at]) path.Push(at);
            while (path.Count > 0)
            {
                var node = tree.Nodes[path.Pop()];
                if (!p.Hero(Hero).Talents.TryGetValue(node.Id, out int rank) || rank <= 0)
                {
                    Rules.AddTalentRank(p, Hero, node.Id, node.Talent.IsChoice ? 0 : (int?)null);
                    allocated.Add(node.Id);
                }
            }
            return allocated;
        }

        // 経路を確保したうえで星を1段加点する。確保した id（経路と対象）を返す。
        private static HashSet<string> AllocateStar(Profile p, string id, int? option = null)
        {
            var allocated = Connect(p, id);
            Rules.AddTalentRank(p, Hero, id, option);
            allocated.Add(id);
            return allocated;
        }

        private static string[] RowContexts(int[] rows) =>
            rows.Select(r => Tree.Nodes[r].Talent.Name.ToString()).ToArray();

        private static void RefreshAtLayout(DreamforgeUi ui, Profile profile)
        {
            Event.current.type = EventType.Layout;
            ui.RunRefreshStarState(profile, Hero, profile.Hero(Hero));
        }

        private static void DrawListAtRepaint(DreamforgeUi ui)
        {
            Event.current.type = EventType.Repaint;
            Event.current.mousePosition = new Vector2(-1000f, -1000f);
            ui.RunDrawStarClusterList();
        }

        // ---- 左一覧の高さの部分再計算（変更行だけ計測し、_starClusterDirtyFrom から位置を加算） ----

        [Fact]
        public void Cluster_list_measures_only_the_changed_row_and_shifts_positions_from_the_dirty_row()
        {
            Loc.Japanese = false; // 英語の状態文言は長さが変わるので、高さの差を確定させる。
            var ui = BuildUi(out var profile);
            var tree = Tree;
            var (distance, _) = BfsFromStart();
            int[] rows = PlainRows(5);
            Assert.True(rows.Length >= 5);
            Assert.Equal(1, distance[rows[2]]); // 加点する行は始まりに隣接する星（経路の確保が要らない）。
            ui.SeedStarNavigation(rows, RowContexts(rows));

            RefreshAtLayout(ui, profile);
            Assert.Equal(0, ui.StarClusterDirtyFrom);
            DrawListAtRepaint(ui);
            Assert.Equal(5, GUIStyle.CalcHeightCalls); // 初回（幅の確定）は全行を計測する。
            float[] tops = rows.Select((_, i) => ui.StarClusterTopAt(i)).ToArray();
            float[] heights = rows.Select((_, i) => ui.StarClusterHeightAt(i)).ToArray();
            string[] labels = rows.Select((_, i) => ui.StarClusterLabel(i)).ToArray();

            // 3行目の星を加点する。変更行は添え字2。
            Rules.AddTalentRank(profile, Hero, tree.Nodes[rows[2]].Talent.Id);
            RefreshAtLayout(ui, profile);
            Assert.Equal(2, ui.StarClusterDirtyFrom); // 再計算は最初の変更行から始まる。
            Assert.Equal(0f, ui.StarClusterHeightAt(2)); // 変更行の高さだけが無効化される。

            GUIStyle.ResetCalcHeightCalls();
            DrawListAtRepaint(ui);
            Assert.Equal(1, GUIStyle.CalcHeightCalls); // 高さ計測は変更行だけに走る。

            // 未変更行の高さ・位置・ラベルはそのまま保たれる。
            Assert.Equal(tops[0], ui.StarClusterTopAt(0), 3);
            Assert.Equal(tops[1], ui.StarClusterTopAt(1), 3);
            Assert.Equal(heights[0], ui.StarClusterHeightAt(0), 3);
            Assert.Equal(heights[1], ui.StarClusterHeightAt(1), 3);
            Assert.Equal(heights[3], ui.StarClusterHeightAt(3), 3);
            Assert.Equal(heights[4], ui.StarClusterHeightAt(4), 3);
            Assert.Same(labels[0], ui.StarClusterLabel(0));
            Assert.Same(labels[1], ui.StarClusterLabel(1));
            Assert.Same(labels[3], ui.StarClusterLabel(3));

            // 変更行は新しいラベルで計測し、以降の行は保持した高さで位置を加算し直す。
            string newLabel2 = ui.StarClusterLabel(2);
            Assert.NotSame(labels[2], newLabel2);
            float newHeight2 = GUIStyle.Measure(newLabel2) + 4f;
            Assert.Equal(newHeight2, ui.StarClusterHeightAt(2), 3);
            Assert.NotEqual(heights[2], newHeight2, 3); // ラベルが変わったので高さも変わる。
            Assert.Equal(tops[2], ui.StarClusterTopAt(2), 3); // 変更行自身の位置は前の行までで決まる。
            Assert.Equal(tops[2] + newHeight2, ui.StarClusterTopAt(3), 3); // 保持した高さで位置を加算し直す。
            Assert.Equal(tops[2] + newHeight2 + heights[3], ui.StarClusterTopAt(4), 3);
        }

        [Fact]
        public void Cluster_list_remeasures_every_row_when_width_changes()
        {
            var ui = BuildUi(out var profile);
            int[] rows = PlainRows(4);
            ui.SeedStarNavigation(rows, RowContexts(rows));
            RefreshAtLayout(ui, profile);
            DrawListAtRepaint(ui);
            Assert.Equal(4, GUIStyle.CalcHeightCalls);

            GUILayoutUtility.ClusterListArea = new Rect(0f, 0f, 300f, 226f); // 幅を変える。
            GUIStyle.ResetCalcHeightCalls();
            DrawListAtRepaint(ui);
            Assert.Equal(4, GUIStyle.CalcHeightCalls); // 幅変更時は従来どおり全行を計測する。
            Assert.Equal(rows.Length, ui.StarClusterDirtyFrom);
        }

        // ---- TextRank / TextChoice による本文更新スキップ ----

        [Fact]
        public void Unchanged_stars_keep_their_display_body_instance_while_changed_stars_regenerate()
        {
            var ui = BuildUi(out var profile);
            var tree = Tree;
            int[] plain = PlainRows(6);
            int[] choice = ChoiceNodes(2);
            Assert.True(choice.Length >= 2);
            Assert.True(plain.Length >= 3);

            RefreshAtLayout(ui, profile);
            int version = ui.StarTextVersion;
            string plainBody0 = ui.StarAt(plain[0]).DisplayBody;
            string plainBody2 = ui.StarAt(plain[2]).DisplayBody;
            string choiceBody0 = ui.StarAt(choice[0]).DisplayBody;
            string choiceBody1 = ui.StarAt(choice[1]).DisplayBody;
            Assert.NotNull(plainBody0);
            Assert.NotNull(choiceBody0);
            Assert.NotNull(choiceBody1);

            // 変更のない Layout では世代も本文も動かない。
            RefreshAtLayout(ui, profile);
            Assert.Equal(version, ui.StarTextVersion);
            Assert.Same(plainBody0, ui.StarAt(plain[0]).DisplayBody);
            Assert.Same(choiceBody0, ui.StarAt(choice[0]).DisplayBody);

            // 普通の星を1つ加点：全体は更新されるが、変わっていない星の本文は同じインスタンスのまま。
            // plain[0] は始まりに最も近いので、その経路に他の検証対象は乗らない。
            var touched = AllocateStar(profile, tree.Nodes[plain[0]].Talent.Id);
            RefreshAtLayout(ui, profile);
            Assert.Equal(version + 1, ui.StarTextVersion);
            Assert.DoesNotContain(tree.Nodes[plain[2]].Talent.Id, touched);
            Assert.Same(plainBody2, ui.StarAt(plain[2]).DisplayBody); // TextRank/TextChoice スキップ。
            Assert.Same(choiceBody0, ui.StarAt(choice[0]).DisplayBody);
            Assert.Same(choiceBody1, ui.StarAt(choice[1]).DisplayBody);

            // 二択の星を加点：その星の本文だけが作り直される。
            var choiceTouched = AllocateStar(profile, tree.Nodes[choice[0]].Talent.Id, 0);
            RefreshAtLayout(ui, profile);
            Assert.Equal(version + 2, ui.StarTextVersion);
            Assert.NotSame(choiceBody0, ui.StarAt(choice[0]).DisplayBody);
            Assert.Equal("1/" + tree.Nodes[choice[0]].Talent.MaxRank, ui.StarAt(choice[0]).RankLabel.text);
            Assert.DoesNotContain(tree.Nodes[plain[2]].Talent.Id, choiceTouched);
            Assert.Same(plainBody2, ui.StarAt(plain[2]).DisplayBody); // 変わっていない星は依然として同じ。
            if (!choiceTouched.Contains(tree.Nodes[choice[1]].Talent.Id))
                Assert.Same(choiceBody1, ui.StarAt(choice[1]).DisplayBody);
        }

        private static int[] ChoiceNodes(int count)
        {
            var tree = Tree;
            var nodes = new List<int>();
            for (int i = 0; i < tree.Nodes.Count && nodes.Count < count; i++)
                if (tree.Nodes[i].Talent?.IsChoice == true) nodes.Add(i);
            return nodes.ToArray();
        }

        // ---- ツールチップの世代管理（TooltipVersion / _starTextVersion） ----

        [Fact]
        public void Tooltip_regenerates_only_when_its_generation_expires()
        {
            var ui = BuildUi(out var profile);
            var tree = Tree;
            var (distance, parent) = BfsFromStart();
            // 始まりから2歩目の普通の星（条件不足のままホバーする想定）と、その手前（1歩目）の星。
            int hovered = tree.Nodes.Select((_, i) => i)
                .First(i => distance[i] == 2 && IsPlain(i)
                    && tree.Nodes[i].Neighbors.Any(n => distance[n] == 1 && IsPlain(n)));
            int gate = tree.Nodes[hovered].Neighbors.First(n => distance[n] == 1 && IsPlain(n));
            Assert.Equal(2, distance[hovered]);
            Assert.Equal(1, distance[gate]);

            RefreshAtLayout(ui, profile);
            ui.RunEnsureStarTooltip(hovered);
            string first = ui.StarAt(hovered).Tooltip.text;
            int version = ui.StarTextVersion;
            Assert.Equal(version, ui.StarAt(hovered).TooltipVersion);
            Assert.Contains("使用済み 0 点", first); // ダメージ注記は使用済み0点の世代。
            Assert.Contains("隣の星が未取得です", first); // 条件文は未接続の内容。

            // 同じ世代のうちは再生成しない（同じ文字列インスタンスのまま）。
            ui.RunEnsureStarTooltip(hovered);
            Assert.Same(first, ui.StarAt(hovered).Tooltip.text);

            // ホバーしたまま手前の星を加点：世代が進み、条件文もダメージ注記も作り直される。
            Rules.AddTalentRank(profile, Hero, tree.Nodes[gate].Talent.Id);
            RefreshAtLayout(ui, profile);
            Assert.Equal(version + 1, ui.StarTextVersion);
            Assert.Equal(version, ui.StarAt(hovered).TooltipVersion); // まだ前の世代。
            ui.RunEnsureStarTooltip(hovered);
            string second = ui.StarAt(hovered).Tooltip.text;
            int spent = Rules.SpentPoints(profile.Hero(Hero), Hero);
            Assert.True(spent > 0);
            Assert.NotSame(first, second);
            Assert.Equal(version + 1, ui.StarAt(hovered).TooltipVersion);
            Assert.DoesNotContain("隣の星が未取得です", second); // 条件文が再生成された。
            Assert.Contains($"使用済み {spent} 点", second); // ダメージ注記が再生成された。
            Assert.DoesNotContain("使用済み 0 点", second);
        }

        // ---- 二択の本文と候補の再生成 ----

        [Fact]
        public void Choice_change_regenerates_body_and_option_texts()
        {
            var ui = BuildUi(out var profile);
            int[] choice = ChoiceNodes(1);
            string choiceId = Tree.Nodes[choice[0]].Talent.Id;
            var talent = Tree.Nodes[choice[0]].Talent;

            RefreshAtLayout(ui, profile);
            string bodyUnallocated = ui.StarAt(choice[0]).DisplayBody;
            ui.RunEnsureStarChoiceText(choice[0]);
            string optionA1 = ui.StarAt(choice[0]).ChoiceOptions[0].text;
            string optionB1 = ui.StarAt(choice[0]).ChoiceOptions[1].text;
            Assert.Contains("使用済み 0 点", optionA1);
            Assert.Contains("使用済み 0 点", optionB1);

            // 同じ世代のうちは候補を再利用する（世代が進まなければ作り直さない）。
            ui.RunEnsureStarChoiceText(choice[0]);
            Assert.Same(optionA1, ui.StarAt(choice[0]).ChoiceOptions[0].text);
            Assert.Same(optionB1, ui.StarAt(choice[0]).ChoiceOptions[1].text);

            // 1段目を効果Aで取得：本文と両候補が作り直される。
            int spentBefore = Rules.SpentPoints(profile.Hero(Hero), Hero);
            AllocateStar(profile, choiceId, 0);
            RefreshAtLayout(ui, profile);
            ui.RunEnsureStarChoiceText(choice[0]);
            Assert.NotSame(bodyUnallocated, ui.StarAt(choice[0]).DisplayBody);
            string optionA2 = ui.StarAt(choice[0]).ChoiceOptions[0].text;
            string optionB2 = ui.StarAt(choice[0]).ChoiceOptions[1].text;
            Assert.NotSame(optionA1, optionA2);
            Assert.NotSame(optionB1, optionB2);
            int spentAfterAllocation = Rules.SpentPoints(profile.Hero(Hero), Hero);
            Assert.True(spentAfterAllocation > spentBefore);
            Assert.Contains($"使用済み {spentAfterAllocation} 点", optionA2);
            Assert.Contains($"使用済み {spentAfterAllocation} 点", optionB2);

            // 二択を効果Bへ切り替える（無料）：本文・両候補・ツールチップがすべて作り直される。
            ui.RunEnsureStarTooltip(choice[0]);
            string tooltipBeforeSwitch = ui.StarAt(choice[0]).Tooltip.text;
            string bodyBeforeSwitch = ui.StarAt(choice[0]).DisplayBody;
            Rules.SetTalentChoice(profile, Hero, choiceId, 1);
            RefreshAtLayout(ui, profile);
            Assert.NotSame(bodyBeforeSwitch, ui.StarAt(choice[0]).DisplayBody); // 本文は再生成。
            Assert.Equal(StarMapPresentation.EffectSummary(talent, 1), ui.StarAt(choice[0]).EffectSummary);
            // 使用済みポイントは変わらずとも世代は進むので、両候補も作り直される。
            ui.RunEnsureStarChoiceText(choice[0]);
            string optionA3 = ui.StarAt(choice[0]).ChoiceOptions[0].text;
            string optionB3 = ui.StarAt(choice[0]).ChoiceOptions[1].text;
            Assert.NotSame(optionA2, optionA3); // 候補は再生成。
            Assert.NotSame(optionB2, optionB3);
            Assert.Contains($"使用済み {spentAfterAllocation} 点", optionA3);
            Assert.Contains($"使用済み {spentAfterAllocation} 点", optionB3);
            // ツールチップも世代が進んだので作り直される。
            Assert.NotSame(tooltipBeforeSwitch, ui.StarAt(choice[0]).Tooltip.text);
            Assert.Equal("1/" + talent.MaxRank, ui.StarAt(choice[0]).RankLabel.text);
        }
    }
}
