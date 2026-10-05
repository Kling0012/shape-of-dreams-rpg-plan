using System.Collections.Generic;
using SodRpg.Core.Game;

namespace UnityEngine
{
    internal partial struct Vector2 { public static Vector2 zero => default; }
    internal static class GUI { public static bool enabled = true; }
    internal sealed class GUILayoutOption { internal float Height; }
    internal static class GUILayout
    {
        internal static readonly List<string> Labels = new List<string>();
        internal static int PressButton = -1, ButtonIndex, LayoutDepth;
        internal static void BeginVertical(object style) => LayoutDepth++;
        internal static void EndVertical() => LayoutDepth--;
        internal static void BeginHorizontal() => LayoutDepth++;
        internal static void EndHorizontal() => LayoutDepth--;
        internal static void Label(string text, object style) => Labels.Add(text);
        internal static Vector2 BeginScrollView(Vector2 position, params GUILayoutOption[] options)
        { Ops.Add("scroll"); LayoutDepth++; return position; }
        internal static void EndScrollView() => LayoutDepth--;
        internal static GUILayoutOption MaxHeight(float height) => new GUILayoutOption { Height = height };
        internal static GUILayoutOption Height(float height) => new GUILayoutOption { Height = height };
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
        internal bool CanEditTalents = true, CanEditLoadout = true, Dirty;
        internal readonly List<GameEvent> Notices = new List<GameEvent>();
        internal void MarkDirty(bool build) => Dirty |= build;
        internal void Emit(GameEvent notice) => Notices.Add(notice);
    }
    internal sealed class RefundUiStyles
    {
        internal readonly object Panel = new object(), Label = new object(), Warn = new object(), Small = new object(), Button = new object();
    }
    internal sealed partial class DreamforgeUi
    {
        private readonly RefundUiSession _s;
        private readonly RefundUiStyles _st = new RefundUiStyles();
        private readonly string _hero;
        private bool _starDirty;
        private string _starChoiceId;
        internal string Status;
        private string HeroKey => _hero;
        private void SetStatus(string text) => Status = text;
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
    }
}
