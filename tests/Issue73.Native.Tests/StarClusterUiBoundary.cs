using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // 星図の差分更新（#440）。RefreshStarState / EnsureStarTooltip / EnsureStarChoiceText と
    // RefreshStarClusters / DrawStarClusterList / StarRegionColor は NativePersistence.targets が
    // 実ソースから抽出する。StarNode と依存フィールドも型・初期値を含めて抽出し、
    // ここでは未抽出の周辺メソッド（再構築・装飾・スタイル初期化）だけを置き換える。
    internal sealed partial class DreamforgeUi
    {
        // ---- 未抽出の周辺メソッドの境界。再構築は呼ばれない前提で、呼び出しを数えるだけ。 ----
        private int _rebuildStarTreeCalls;
        private void RebuildStarTree(string hero) { _rebuildStarTreeCalls++; }
        private void EnsureStarTextStyles() { }
        private Func<string, bool> LinkMarks() => null;
        private void StarFillRect(Rect rect, Color color) { }
        private void StarJumpTo(int index) { }

        // ---- テストへの内部窓口 ----
        internal int RebuildStarTreeCalls => _rebuildStarTreeCalls;
        internal int StarTextVersion => _starTextVersion;
        internal int StarSpent => _starSpent;
        internal int StarClusterDirtyFrom => _starClusterDirtyFrom;
        internal float StarClusterTopAt(int row) => _starClusterTops[row];
        internal float StarClusterHeightAt(int row) => _starClusterHeights[row];
        internal string StarClusterLabel(int row) => _starClusterLabels[row].text;
        internal StarNode StarAt(int index) => _starNodes[index];
        internal void UseDeterministicClusterRowStyle() { _starClusterRow = new GUIStyle(); _starHelpStyle = new GUIStyle(); }
        internal void RunRefreshStarState(Profile p, string hero, HeroState hs) => RefreshStarState(p, hero, hs);
        internal void RunEnsureStarTooltip(int index) => EnsureStarTooltip(index);
        internal void RunEnsureStarChoiceText(int index) => EnsureStarChoiceText(index);
        internal void RunDrawStarClusterList() => DrawStarClusterList();

        // RebuildStarTree 相当の初期状態。本文・EffectSummary は本物と同じ式で作る。
        // 保守契約: RebuildStarTree / RebuildStarClusters の変更時は SeedStarTree / SeedStarNavigation の
        // 対応する初期化式も比較する。2つの registry fingerprint は登録内容の指紋で、初期化式のずれは検知しない。
        // 自動検知するなら Roslyn で対応する代入式・StarNode 初期化子を抽出して構文比較する。
        // Icon・座標・スタイルと一覧の行選択は境界で省略・差し替えるため、メソッド全体の比較にはしない。
        internal void SeedStarTree(string heroKey)
        {
            _starLayout = HeroTreeLayout.ForHero(heroKey);
            _starHero = heroKey;
            _starJapanese = Loc.Japanese;
            _starCapFingerprint = FractionalScopedModifiers.CapRegistryFingerprint;
            _starAuthoredFingerprint = StarClusters.AuthoredRegistryFingerprint;
            _starNodes = new StarNode[_starLayout.Nodes.Count];
            for (int i = 0; i < _starNodes.Length; i++)
            {
                var node = _starLayout.Nodes[i];
                var t = node.Talent;
                var pair = t == null ? null : PairCombos.ForBridge(t.Id);
                _starNodes[i] = new StarNode
                {
                    Description = t == null ? null : StarMapPresentation.DisplayDescription(t),
                    ChoiceOptions = t != null && t.IsChoice ? new[] { new GUIContent(), new GUIContent() } : null,
                    Keystone = t != null && t.IsKeystone,
                    Pair = pair != null,
                    PairDefinition = pair,
                    Region = t?.Cluster?.Region.Kind,
                };
                _starNodes[i].Name.text = t == null ? Loc.T("始まり", "Start")
                    : pair != null ? pair.Name.ToString() : t.Name.ToString();
                _starNodes[i].EffectSummary = t == null ? "" : t.IsChoice ? StarMapPresentation.EffectSummary(t)
                    : StarMapPresentation.EffectSummary(_starNodes[i].Description);
            }
        }

        // RebuildStarClusters 相当の左一覧の配列。並べる行は呼び出し側が選ぶ。
        internal void SeedStarNavigation(int[] rows, string[] context)
        {
            _starNavigationNodes = rows;
            _starNavigationContext = context;
            _starClusterLabels = new GUIContent[rows.Length];
            for (int i = 0; i < rows.Length; i++) _starClusterLabels[i] = new GUIContent();
            _starClusterTops = new float[rows.Length];
            _starClusterHeights = new float[rows.Length];
            _starClusterWidth = -1f;
            _starClusterDirtyFrom = 0;
            _starClusterScroll = Vector2.zero;
        }
    }

    // RefreshStarState が読む ClientSession の表面（ClientSession.RunGrowth.cs はリンク外）。
    internal sealed partial class ClientSession
    {
        internal int RunGrowthVersion { get; set; }
    }
}
