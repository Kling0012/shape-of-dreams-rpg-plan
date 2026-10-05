using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Only UI storage/configuration is supplied here. Notify, invalidation, decoration and
    // sorted satchel behavior are extracted from the production methods by syntax.
    internal sealed partial class DreamforgeUi
    {
        private sealed class Toast
        {
            public string Text;
            public float Until;
        }

        private sealed class ToastConfig { public bool showToasts = true; }
        private static readonly ToastConfig ToastConfiguration = new ToastConfig();
        private readonly Func<ToastConfig> _cfg = () => ToastConfiguration;
        private readonly List<Toast> _toasts = new List<Toast>();
        private readonly List<Hint> _hints = new List<Hint>();
        private float _nextHudRebuild;
        private readonly List<Relic> _satchelTop = new List<Relic>();
        private float _satchelTopUntil;
        private int _satchelTopCount = -1;
        private RunState _satchelTopRun;
        private readonly CodexView _codex = new CodexView();

        internal int ToastCount => _toasts.Count;
        internal string ToastText(int index) => _toasts[index].Text;
        internal float HudDeadline { get => _nextHudRebuild; set => _nextHudRebuild = value; }
        internal IReadOnlyList<Relic> CachedSatchel() => SortedSatchel();
    }

    internal sealed partial class CodexView
    {
        private bool _dirty = true;
    }
}
