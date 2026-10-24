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
        private readonly Dictionary<string, int> _growthParseScratch = new Dictionary<string, int>(StringComparer.Ordinal); // OnRunGrowth の解析の置き場。changed のときしか届かない解析で毎回確保しないために使い回す。
        private string _growthRunId;
        private string _growthRawStacks; // 最後に適用した stacks の生の文字列。同じ内容の再送の解析を省く。

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
            _growthRawStacks = null;
        }

        private void OnRunGrowth(DreamforgeRunGrowthMsg msg)
        {
            if (msg == null || msg.heroNetId == 0) return;
            var hero = LocalHero;
            if (hero == null || hero.netId != msg.heroNetId) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeRunGrowthMsg));
            // ホストは値が変わらなくても定期再送する。生のペイロードが前回の適用と同じなら解析結果も同じなので、確保なしで抜ける。
            // RunGrowth.IsRemovedStar（起動時に固定の一覧）と Gimmicks.ValidStarId（文字種と長さの判定だけ）は
            // 実行中に結果が変わらないため、同じ文字列を後から解析し直しても同じ結果になる。
            if (msg.runId == _growthRunId && string.Equals(msg.stacks, _growthRawStacks, StringComparison.Ordinal)) return;
            // 解析は changed のときだけ届く（ホストが0.5秒に1回以下）。Split や Substring で毎回確保しないため、
            // 生の文字列を位置で切りながら、結果の辞書も前回のものを使い回す。末尾のコンマは Split と同じく空の要素になる。
            var parsed = _growthParseScratch;
            parsed.Clear();
            string stacks = msg.stacks;
            if (!string.IsNullOrEmpty(stacks))
                for (int start = 0; start <= stacks.Length;)
                {
                    int comma = stacks.IndexOf(',', start);
                    int end = comma >= 0 ? comma : stacks.Length;
                    var part = stacks.AsSpan(start, end - start);
                    start = end + 1;
                    int eq = part.IndexOf('=');
                    string id = eq > 0 ? part.Slice(0, eq).ToString() : null;
                    if (id != null && RunGrowth.IsRemovedStar(id)) continue;
                    if (eq <= 0 || !Gimmicks.ValidStarId(id)
                        || !int.TryParse(part.Slice(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                        || count < 0)
                    {
                        Log.Warn("Client: rejected malformed run growth payload.");
                        return;
                    }
                    parsed[id] = count;
                    if (comma < 0) break;
                }
            bool changed = _growthRunId != msg.runId || parsed.Count != _growthStacks.Count;
            if (!changed)
                foreach (var kv in parsed)
                    if (!_growthStacks.TryGetValue(kv.Key, out int old) || old != kv.Value) { changed = true; break; }
            // 内容が同じでも並び順だけ違う文字列（例: b=2,a=1）はここで解析し直される。次の同じ内容が
            // 文字列比較だけで済むように、現在の状態と同じ結果になった生の文字列も覚えておく。
            _growthRawStacks = msg.stacks;
            if (!changed) return;
            _growthRunId = msg.runId;
            _growthStacks.Clear();
            foreach (var kv in parsed) _growthStacks[kv.Key] = kv.Value;
            RunGrowthVersion++;
        }
    }
}
