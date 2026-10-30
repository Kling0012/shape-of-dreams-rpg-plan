using System;
using System.Collections.Generic;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    /// <summary>
    /// Harness surface for the real HostAuthority.Infinity.cs / HostAuthority.Hello.cs compiled
    /// into this project: the instance state those files touch on the not-compiled HostAuthority
    /// partials (registration, kill ledger) is modeled here. The roster and handshake logic under
    /// test is the production code, not a copy.
    /// </summary>
    internal sealed partial class HostAuthority
    {
        internal static HostAuthority NativeInstance;
        // HostAuthority.MeleeCoop.cs is not compiled here; hello replies carry the "not sent" value.
        internal int MeleeCoopWire => 0;
        internal Actor _registeredOn;

        private readonly SortedDictionary<long, AuthoritativeRunKill> _killUnacknowledged
            = new SortedDictionary<long, AuthoritativeRunKill>();
        private readonly Dictionary<string, KillReplayPeer> _killPeers
            = new Dictionary<string, KillReplayPeer>(StringComparer.Ordinal);
        private readonly Dictionary<DewPlayer, KillReplayCursor> _killReplayPlayers
            = new Dictionary<DewPlayer, KillReplayCursor>();
        private readonly List<AuthoritativeRunKill> _killHistory = new List<AuthoritativeRunKill>();
        private long _killSequence;

        private static bool PeerNeedsFact(KillReplayPeer peer, AuthoritativeRunKill fact) => false;
        private void BindKillObservationSession(DewPlayer player, string observationSessionId) { }
        private void RegisterOverflowBonus(Actor actor) { }
        private void UnregisterOverflowBonus(Actor actor) { }
        // HostAuthority.Continue.cs is not compiled here; OnTrade's Continue replay is a no-op in this harness.
        private void ApplyContinueTrades() { }

        private sealed class KillReplayCursor { public string PeerId; }
    }


    internal sealed partial class ClientSession
    {
        internal static ulong HostAuthorityGeneration;
        internal static string ContinueRunId => NetworkedManagerBase<GameManager>.softInstance?.runId;
        internal static string ContinueCheckpointId, ContinueResumeSession;
        internal static long HostKillReceiptForProgress(string streamId) => 0;
        internal void ConfigureOverflowBonus(bool enabled) { Profile.ReceiveOverflowDreamDust = enabled; }
        // #1010 自動分解の設定受け入れ。#1049 再現: 最後に渡された設定を記録してテストから見えるようにする。
        internal AutoSalvageRarity? AutoSalvageApplied;
        internal void ConfigureAutoSalvage(AutoSalvageRarity setting) { AutoSalvageApplied = setting; }
        // ヒーローコネクタ（spec hero-connector §6-8）: BuildInputCapability.cs は未リンクのため同じ式の模倣。
        internal bool? HeroConnectorApplied;
        internal void ConfigureHeroConnector(bool enabled) { HeroConnectorApplied = enabled; }
        // HostAuthority.Hello.cs の応答が呼ぶ。BuildInputCapability.cs と同じ組み立て。
        internal static DreamforgeHeroConnectorMsg CreateHeroConnectorMessage()
        {
            var heroes = SodRpg.Core.Game.ExtendedHeroes.Registered;
            var keys = new List<string>(heroes.Count);
            var hashes = new List<string>(heroes.Count);
            foreach (var hero in heroes) { keys.Add(hero.Key); hashes.Add(hero.DeclarationHash); }
            var order = new int[heroes.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => string.CompareOrdinal(keys[a], keys[b]));
            var sortedKeys = new string[heroes.Count];
            var sortedHashes = new string[heroes.Count];
            for (int i = 0; i < order.Length; i++) { sortedKeys[i] = keys[order[i]]; sortedHashes[i] = hashes[order[i]]; }
            return new DreamforgeHeroConnectorMsg { heroes = sortedKeys, hashes = sortedHashes };
        }
    }
}
