using System;
using System.Collections.Generic;
using System.Globalization;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>v1.32 B：ホストから届く遠征の鍛錬（RunGrowth）のスタック。表示専用（HUD と取得した効果の一覧）。</summary>
    internal sealed partial class ClientSession
    {
        private Action<DreamforgeRunGrowthMsg> _onRunGrowth;
        private readonly Dictionary<string, int> _growthStacks = new Dictionary<string, int>(StringComparer.Ordinal);
        private string _growthRunId;

        /// <summary>スタックが届くたびに増える。画面の作り直しの目印。</summary>
        public int RunGrowthVersion { get; private set; }

        public int GrowthStacks(string starId) => starId != null && _growthStacks.TryGetValue(starId, out int stacks) ? stacks : 0;

        private void RegisterRunGrowth(Actor actor)
        {
            if (_onRunGrowth == null) _onRunGrowth = OnRunGrowth;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeRunGrowthMsg>(_onRunGrowth);
        }

        private void UnregisterRunGrowth(Actor actor)
        {
            if (_onRunGrowth != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeRunGrowthMsg>(_onRunGrowth); } catch (Exception) { }
        }

        private void ResetRunGrowthDisplay()
        {
            if (_growthStacks.Count > 0 || _growthRunId != null) RunGrowthVersion++;
            _growthStacks.Clear();
            _growthRunId = null;
        }

        private void OnRunGrowth(DreamforgeRunGrowthMsg msg)
        {
            if (msg == null || msg.heroNetId == 0) return;
            var hero = LocalHero;
            if (hero == null || hero.netId != msg.heroNetId) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeRunGrowthMsg));
            var parsed = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(msg.stacks))
                foreach (string part in msg.stacks.Split(','))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0 || !Gimmicks.ValidStarId(part.Substring(0, eq))
                        || !int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stacks)
                        || stacks < 0 || stacks > RunGrowthDef.MaxCap)
                    {
                        Log.Warn("Client: rejected malformed run growth payload.");
                        return;
                    }
                    parsed[part.Substring(0, eq)] = stacks;
                }
            bool changed = _growthRunId != msg.runId || parsed.Count != _growthStacks.Count;
            if (!changed)
                foreach (var kv in parsed)
                    if (!_growthStacks.TryGetValue(kv.Key, out int old) || old != kv.Value) { changed = true; break; }
            if (!changed) return;
            _growthRunId = msg.runId;
            _growthStacks.Clear();
            foreach (var kv in parsed) _growthStacks[kv.Key] = kv.Value;
            RunGrowthVersion++;
        }
    }
}
