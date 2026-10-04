using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        // #55: replaying every kill fact of the run to every client on each 5 s resync made the traffic grow
        // linearly with the total kill count. Facts published this session are replayed for one cycle; facts from
        // earlier (including the profile-restored history) go only to players we have not replayed to yet.
        private readonly List<string> _killFactPrune = new List<string>();
        private readonly Dictionary<string, float> _killFactPublishedAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<DewPlayer> _killReplayDeparted = new List<DewPlayer>();
        private readonly HashSet<DewPlayer> _killReplayPlayers = new HashSet<DewPlayer>();
        private float _lastKillReplayAt;

        private void CaptureAuthoritativeRunKill(Monster monster)
        {
            if (monster == null || !_monsters.TryGetValue(monster, out var runtime)
                || runtime.KillEventId != null || monster.disableLoot) return;
            if (monster.Status != null && monster.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter)
                && !hunter.enableGoldAndExpDrops) return;
            var game = NetworkedManagerBase<GameManager>.softInstance;
            string runId = game?.runId;
            if (string.IsNullOrEmpty(runId)) return;
            runtime.KillEventId = Guid.NewGuid().ToString("N");
            _nightmares.TryGetValue(monster, out var nightmare);
            string bossTypeName = null;
            bool bossDropNightmare = false;
            int bossDropDepth = 0;
            if (monster is BossMonster && monster.type == Monster.MonsterType.Boss)
            {
                string typeName = monster.GetType().Name;
                if (BossSets.TryGetSet(typeName, out _))
                {
                    bossTypeName = typeName;
                    // Native difficulty names are also used by GameResultManager and DewSave.
                    string difficulty = game.difficulty?.name;
                    bossDropNightmare = difficulty == "diffNightmare" || difficulty == "diffLimbo";
                    bossDropDepth = DreamDepth.Clamp(ClientSession.HostRun?.DreamDepth ?? 0);
                }
            }
            var fact = new AuthoritativeRunKill(runId, runtime.KillEventId, monster.netId,
                _zone?.currentZoneIndex ?? -1, nightmare, runtime.Variant?.Id,
                bossTypeName, bossDropNightmare, bossDropDepth);
            _killFactPublishedAt[fact.EventId] = Time.time;
            // Record before native object cleanup. Reload restores this immutable history from the profile.
            ClientSession.PublishHostKillFact(fact);
            _registeredOn?.CustomRpc_SendMessageToAllClients(
                DreamforgeMonsterKillMsg.FromFact(fact, ClientSession.HostAuthorityGeneration));
        }

        private void ResyncMonsterClassifications()
        {
            if (_registeredOn == null) return;
            ulong authority = ClientSession.HostAuthorityGeneration;
            foreach (var runtime in _monsters.Values)
            {
                var monster = runtime.Monster;
                if (monster == null || !monster.isActive || !monster.isAlive) continue;
                if (runtime.Variant != null)
                    _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeVariantMsg
                    {
                        netId = monster.netId, variantId = runtime.Variant.Id, authorityGeneration = authority,
                    });
                else
                {
                    _nightmares.TryGetValue(monster, out var nightmare);
                    // Ordinary entries are explicit removals, not an absence clients must interpret.
                    _registeredOn.CustomRpc_SendMessageToAllClients(new DreamforgeNightmareMsg
                    {
                        netId = monster.netId, affixes = (int)nightmare, authorityGeneration = authority,
                    });
                }
                SendMonsterBehaviorCue(runtime, true);
            }
            ReplayAuthoritativeRunKills();
        }

        /// <summary>
        /// 5秒ごとの再送（#55）。今のセッションで発行した事実は1周期だけ再送し、それ以前の全履歴は
        /// 初めて見る参加者（途中参加・再接続・ホスト再登録後）にだけ全件送る。参加していないプレイヤー分は捨てる。
        /// </summary>
        private void ReplayAuthoritativeRunKills()
        {
            if (_registeredOn == null) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            ulong authority = ClientSession.HostAuthorityGeneration;
            _killReplayDeparted.Clear();
            foreach (var player in _killReplayPlayers)
                if (player == null || !DewPlayer.gamePlayers.Contains(player)) _killReplayDeparted.Add(player);
            foreach (var player in _killReplayDeparted) _killReplayPlayers.Remove(player);
            foreach (var player in DewPlayer.gamePlayers)
            {
                if (player == null || !player.isHumanPlayer || !_killReplayPlayers.Add(player)) continue;
                foreach (var fact in ClientSession.ReplayableHostKillFacts)
                    if (fact.RunId == runId)
                        _registeredOn.CustomRpc_SendMessageToClient(player, DreamforgeMonsterKillMsg.FromFact(fact, authority));
            }
            // One overlap cycle covers facts published while this resync was being prepared.
            float since = _lastKillReplayAt - 5f;
            foreach (var fact in ClientSession.ReplayableHostKillFacts)
                if (fact.RunId == runId && _killFactPublishedAt.TryGetValue(fact.EventId, out float at) && at >= since)
                    _registeredOn.CustomRpc_SendMessageToAllClients(DreamforgeMonsterKillMsg.FromFact(fact, authority));
            _killFactPrune.Clear();
            foreach (var kv in _killFactPublishedAt)
                if (kv.Value < since) _killFactPrune.Add(kv.Key);
            foreach (string eventId in _killFactPrune) _killFactPublishedAt.Remove(eventId);
            _lastKillReplayAt = Time.time;
        }

        private void ReplayAuthoritativeBossKills(DewPlayer player)
        {
            if (_registeredOn == null) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            foreach (var fact in ClientSession.ReplayableHostKillFacts)
                if (fact.RunId == runId && fact.BossTypeName != null)
                    _registeredOn.CustomRpc_SendMessageToClient(player,
                        DreamforgeMonsterKillMsg.FromFact(fact, ClientSession.HostAuthorityGeneration));
        }
    }
}
