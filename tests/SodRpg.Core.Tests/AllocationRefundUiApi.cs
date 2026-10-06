using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace UnityEngine
{
    internal partial struct Vector2 { public static Vector2 zero => default; }
    internal struct Rect
    {
        public Rect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        internal float X, Y, Width, Height;
    }
    internal static class GUI { public static bool enabled = true; }
    internal sealed class GUILayoutOption { internal float Height, Width; }
    internal sealed class GUIContent { public string text; }
    internal static class GUILayout
    {
        internal static readonly List<string> Labels = new List<string>();
        internal static int PressButton = -1, ButtonIndex, LayoutDepth;
        internal static void BeginVertical(object style) => LayoutDepth++;
        internal static void BeginVertical(object style, params GUILayoutOption[] options) => LayoutDepth++;
        internal static void EndVertical() => LayoutDepth--;
        internal static void BeginHorizontal() => LayoutDepth++;
        internal static void EndHorizontal() => LayoutDepth--;
        internal static void BeginArea(Rect rect, object style) { Ops.Add("begin-area"); LayoutDepth++; }
        internal static void EndArea() => LayoutDepth--;
        internal static void Label(string text, object style) { Labels.Add(text); Ops.Add("label:" + text); }
        internal static void Label(string text, object style, params GUILayoutOption[] options) => Label(text, style);
        internal static void Label(GUIContent content, object style, params GUILayoutOption[] options) => Label(content.text, style);
        internal static Vector2 BeginScrollView(Vector2 position, params GUILayoutOption[] options)
        { Ops.Add("scroll"); LayoutDepth++; return position; }
        internal static void EndScrollView() { LayoutDepth--; Ops.Add("endscroll"); }
        internal static void Space(float pixels) { }
        internal static GUILayoutOption MaxHeight(float height) => new GUILayoutOption { Height = height };
        internal static GUILayoutOption MinHeight(float height) => new GUILayoutOption { Height = height };
        internal static GUILayoutOption Height(float height) => new GUILayoutOption { Height = height };
        internal static GUILayoutOption Width(float width) => new GUILayoutOption { Width = width };
        internal static GUILayoutOption ExpandHeight(bool expand) => new GUILayoutOption();
        internal static GUILayoutOption ExpandWidth(bool expand) => new GUILayoutOption();
        internal static bool Button(string text, object style) => Button(text, style, null);
        internal static bool Button(string text, object style, params GUILayoutOption[] options)
        { Ops.Add("button:" + text); int index = ButtonIndex++; return GUI.enabled && index == PressButton; }
        internal static void Input(int button)
        { PressButton = button; ButtonIndex = 0; Labels.Clear(); Ops.Clear(); GUI.enabled = true; }
        /// <summary>描画した順の記録（ボタンとスクロールの前後関係の試験に使う）。</summary>
        internal static readonly List<string> Ops = new List<string>();
    }
}
namespace SodRpg.Mod
{
    internal sealed class RefundUiSession
    {
        internal Profile Profile;
        internal readonly TradeLedger Trades = new TradeLedger();
        internal bool CanEditTalents = true, CanEditLoadout = true, Dirty, InGame;
        internal object LocalHero;
        internal bool CoopTradeLocked => Profile.CoopTradePending != null;
        internal readonly List<GameEvent> Notices = new List<GameEvent>();
        internal void MarkDirty(bool build) => Dirty |= build;
        internal void Emit(GameEvent notice) => Notices.Add(notice);
        internal Build CurrentBuild(string hero) => new Build();
    }
    internal sealed class RefundUiStyles
    {
        internal readonly object Panel = new object(), Label = new object(), Warn = new object(), Small = new object(), Button = new object(),
            Window = new object(), Title = new object(), Tab = new object(), TabSel = new object(), Header = new object(),
            Row = new object(), RowSel = new object(), ButtonSel = new object();
    }
    // 実機の DreamforgeConfig（InputSystem 依存）の代わり。メニューの描画で読むのは menuKey だけ。
    internal class DreamforgeConfig { public string menuKey = "F6"; }
    internal sealed class UiStyles { internal static string Colored(string text, string hex) => text; internal static string RelicTitle(Relic r) => r.Uid; }
    internal sealed partial class DreamforgeUi
    {
        private readonly RefundUiSession _s;
        private readonly RefundUiStyles _st = new RefundUiStyles();
        private readonly string _hero;
        private bool _starDirty;
        private string _starChoiceId;
        internal string Status => _status;
        private string _status;
        private float _statusUntil;
        private int _tab;
        private bool _codexOpen;
        private float _windowHeight, _windowWidth;
        private int _retuneIndex;
        private string _confirmSalvage, _confirmAffixReroll;
        private Relic _confirmEnhance;
        private bool _confirmBulk;
        internal string HeroKey => _hero;
        private void SetStatus(string text) { _status = text; _statusUntil = UnityEngine.Time.unscaledTime + 5f; }
        internal DreamforgeUi(RefundUiSession session, string hero) { _s = session; _hero = hero; }
        internal bool HasRefund => _allocationRefund != null;
        internal void OfferRefund(AllocationValidationException error, bool equipment = false, string relic = null)
            => OfferAllocationRefund(error, _s.Profile, _hero, equipment, relic);
        internal void DrawRefund(int pressedButton)
        { UnityEngine.GUILayout.Input(pressedButton); DrawAllocationRefund(); }
        // 装備タブの右の欄（DreamforgeUi.GearDetail.cs）を試験で動かすための足りない部品。
        private UnityEngine.Vector2 _scrollRight;
        internal void DrawGear(int pressedButton)
        { UnityEngine.GUILayout.Input(pressedButton); DrawGearDetail(_s.Profile, _hero, _s.Profile.Stash[0]); }
        private void RelicDetail(Relic r) { }
        private void Comparison(Relic next, Relic cur) { }
        // 装備タブの左の欄（DreamforgeUi.GearColumn.cs）を試験で動かすための足りない部品。
        private Slot _slot;
        private string _selected, _heroSel, _linkHeroKey;
        private readonly HashSet<string> _linkMemories = new HashSet<string>();
        private readonly HashSet<string> _linkEssences = new HashSet<string>();
        private readonly HashSet<string> _linkAllies = new HashSet<string>();
        private static void IconSlot(Relic r, float size) { }
        private Func<string, bool> LinkMarks() => null;
        private void DrawSetLinkProgress(SetDef set, int count) { }
        internal void DrawGearColumnUi(int pressedButton)
        { UnityEngine.GUILayout.Input(pressedButton); DrawGearColumn(_s.Profile, _hero); }
        // メニューの窓（DreamforgeUi.Window.cs）を試験で動かすための足りない部品。
        internal void DrawMenu(int tab, int pressedButton)
        { UnityEngine.GUILayout.Input(pressedButton); _tab = tab; DrawWindow(1920f, 1080f, new DreamforgeConfig()); }
        private void DrawProfileSlots() { }
        private void DrawDreamDepthChoice() { }
        private static string HeroName(string key) => key;
        private static string TabIntro(int tab) => "";
        private void CancelStarDrag() { }
        private void Close() { }
        private void Act(Func<GameEvent> action, bool affectsBuild) { }
        private void DrawGearTab() { UnityEngine.GUILayout.Ops.Add("tab-content:0"); }
        private void DrawForgeTab() { UnityEngine.GUILayout.Ops.Add("tab-content:1"); }
        private void DrawTalentTab() { UnityEngine.GUILayout.Ops.Add("tab-content:2"); }
        private void DrawRecordsTab(DreamforgeConfig cfg) { UnityEngine.GUILayout.Ops.Add("tab-content:4"); }
        private static string CoopTradeTabLabel() => Loc.T("交換", "Trade");
        private void DrawCoopTradeTab() => throw new NotSupportedException("Cooperative trade rendering is outside the refund fixture.");
    }
}
