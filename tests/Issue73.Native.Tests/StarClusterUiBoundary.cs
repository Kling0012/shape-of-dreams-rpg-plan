using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // 星図の差分更新（#440）。RefreshStarState / EnsureStarTooltip / EnsureStarChoiceText と
    // RefreshStarClusters / DrawStarClusterList / StarRegionColor は NativePersistence.targets が
    // 実ソースから抽出する。ここは抽出メソッドの実行に必要な非公開状態（StarNode と _star* フィールド）
    // を同じ形で用意し、未抽出の周辺メソッド（再構築・装飾・スタイル初期化）だけを置き換える。
    internal sealed partial class DreamforgeUi
    {
        // DreamforgeUi.cs の StarNode と同じ形。テストから直接読むためだけに internal にする。
        internal sealed class StarNode
        {
            public int Rank, Choice = -1, TextRank = -1, TextChoice = -2, TooltipVersion = -1, ChoiceTextVersion = -1;
            public bool Allocated, Available, Unlocked, PairEquippedA, PairEquippedB, SearchMatch, SummaryHover;
            public string Description, DisplayBody, SearchDescription, EffectSummary, MissingRequirements;
            public int NavigationRank = -1;
            public bool NavigationAvailable;
            public string NavigationSummary;
            public PairComboDef PairDefinition;
            public ClusterRegionKind? Region;
            public GUIContent[] ChoiceOptions;
            public Texture2D Icon;
            public bool Keystone, Pair;
            public float TooltipWidth, TooltipHeight;
            public readonly GUIContent Name = new GUIContent();
            public readonly GUIContent RankLabel = new GUIContent();
            public readonly GUIContent Tooltip = new GUIContent();
            public readonly GUIContent KeystoneLabel = new GUIContent();
        }

        // DreamforgeUi.cs / DreamforgeUi.AuthoredStars.cs の抽出メソッドが参照する非公開状態。
        private HeroTreeLayout _starLayout;
        private StarNode[] _starNodes;
        private string _starHero, _starCapFingerprint, _starAuthoredFingerprint;
        private readonly string[] _starKeystoneState = new string[KeystoneSlots.Max];
        private bool[] _starReachable;
        private int _starSpent, _starTextVersion;
        private readonly GUIContent _starPoints = new GUIContent();
        private readonly GUIContent _starProgress = new GUIContent();
        private readonly GUIContent _starKeystoneSlots = new GUIContent();
        private HeroState _starState, _starTextState;
        private bool _starJapanese, _starDirty = true, _starSumDirty = true, _starSearchDirty = true;
        private int _starXp, _starKills, _starCodex, _starTestBonus, _starExtraPoints, _starFree;
        private bool _starSumOpen;
        private int _starSumSig;
        private readonly HashSet<string> _linkMemories = new HashSet<string>();
        private readonly HashSet<string> _linkEssences = new HashSet<string>();
        private readonly HashSet<string> _linkAllies = new HashSet<string>();
        private int[] _starNavigationNodes = Array.Empty<int>();
        private string[] _starNavigationContext = Array.Empty<string>();
        private GUIContent[] _starClusterLabels = Array.Empty<GUIContent>();
        private float[] _starClusterTops = Array.Empty<float>(), _starClusterHeights = Array.Empty<float>();
        private float _starClusterWidth = -1f, _starClusterHeight;
        private int _starClusterDirtyFrom;
        private Vector2 _starClusterScroll;
        private bool _starClustersOpen = true;
        private GUIStyle _starClusterRow, _starHelpStyle;
        private static readonly GUILayoutOption[] StarClusterSize = { GUILayout.Width(270) };
        private static readonly Color StarKeystone = new Color(0.8f, 0.55f, 1f);
        private static readonly Color StarBright = new Color(0.87f, 0.94f, 1f);

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
