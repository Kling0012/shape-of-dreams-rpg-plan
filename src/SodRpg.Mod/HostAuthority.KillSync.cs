using System;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private void CaptureAuthoritativeRunKill(Monster monster)
        {
            if (monster == null || !_monsters.TryGetValue(monster, out var runtime)
                || runtime.KillEventId != null || monster.disableLoot) return;
            if (monster.Status != null && monster.Status.TryGetStatusEffect<Se_HunterBuff>(out var hunter)
                && !hunter.enableGoldAndExpDrops) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            if (string.IsNullOrEmpty(runId)) return;
            runtime.KillEventId = Guid.NewGuid().ToString("N");
            _nightmares.TryGetValue(monster, out var nightmare);
            var fact = new AuthoritativeRunKill(runId, runtime.KillEventId, monster.netId,
                _zone?.currentZoneIndex ?? -1, nightmare, runtime.Variant?.Id);
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

        private void ReplayAuthoritativeRunKills()
        {
            if (_registeredOn == null) return;
            string runId = NetworkedManagerBase<GameManager>.softInstance?.runId;
            ulong authority = ClientSession.HostAuthorityGeneration;
            foreach (var fact in ClientSession.ReplayableHostKillFacts)
                if (fact.RunId == runId)
                    _registeredOn.CustomRpc_SendMessageToAllClients(DreamforgeMonsterKillMsg.FromFact(fact, authority));
        }
    }
}
