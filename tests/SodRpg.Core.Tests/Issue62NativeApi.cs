using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // Existing native scenarios model ordinary runs only; their native Infinity state is OFF.
    internal static class InfinityMode
    {
        internal static InfinityRunState State => null;
        internal static bool Available => false;
    }
    internal struct EventInfoKill { public Actor actor; public Entity victim; }
    internal partial class Entity
    {
        public event Action<EventInfoKill> EntityEvent_OnDeath;
        public event Action<EventInfoDamage> EntityEvent_OnTakeDamage;
        internal void RaiseDeath(EventInfoKill info) => EntityEvent_OnDeath?.Invoke(info);
    }
    internal partial class Actor
    {
        public readonly Dictionary<string, string> persistentSyncedData = new Dictionary<string, string>();
        public void InvokeOnKill(EventInfoKill info) => typeof(NativeAttributedKill)
            .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { info });
        public void CustomRpc_SendMessageToAllClients<T>(T message) => ClientMessages.Add(message);
    }
    internal sealed partial class HostAuthority
    {
        internal sealed class WinningRandom : Random
        {
            internal int Rolls;
            public override double NextDouble() { Rolls++; return 0; }
        }
        private readonly Dictionary<Monster, MonsterRuntime> _monsters = new Dictionary<Monster, MonsterRuntime>();
        private readonly Dictionary<Monster, NightmareAffix> _nightmares = new Dictionary<Monster, NightmareAffix>();
        private readonly Dictionary<Monster, float> _regen = new Dictionary<Monster, float>();
        private readonly HashSet<Entity> _pairEntities = new HashSet<Entity>(), _nativeDeathEntities = new HashSet<Entity>();
        private readonly HashSet<int> _generatedPairDeaths = new HashSet<int>();
        private readonly List<KeyValuePair<Monster, float>> _spawnQueue = new List<KeyValuePair<Monster, float>>();
        private readonly List<Hero> _deathBurstHeroes = new List<Hero>();
        private bool _loggedDeathBurst;
        private Action<EventInfoDamage> _onPairEnemyDamage = _ => { };
        private Action<EventInfoKill> _onPairEnemyDeath = _ => { }, _onDeath = _ => { }, _onMonsterDeath;
        private string _killRunId, _killStreamId = "issue62-stream";
        private long _killSequence;
        private sealed class KillPeer
        {
            internal bool ControlSent = true;
            internal bool ReceiptWaitReleased;
            internal readonly ParticipationRange Participation = new ParticipationRange();
        }
        private sealed class ParticipationRange { internal long Through; }
        private readonly Dictionary<DewPlayer, KillPeer> _killReplayPlayers = new Dictionary<DewPlayer, KillPeer>();
        internal readonly List<AuthoritativeRunKill> OrdinaryKillFacts = new List<AuthoritativeRunKill>();
        private double ShardDropMultiplierForKill() => 1;
        private void EnsureKillRun() => _killRunId = NetworkedManagerBase<GameManager>.softInstance.runId;
        private void RegisterKillPeer(DewPlayer player) { }
        private void SendKillStreamControl(DewPlayer player, KillPeer peer) { }
        private void RestoreHostFact(AuthoritativeRunKill fact) => OrdinaryKillFacts.Add(fact);
        private void GrantEliteKillGold(Monster monster) { } // Tests use ordinary, non-elite monsters.
        private void SendMonsterRemoval(MonsterRuntime runtime) { }
        private void Unhook(MonsterRuntime runtime) => runtime.Monster.EntityEvent_OnDeath -= _onMonsterDeath;

        internal WinningRandom StartIssue62Lifecycle()
        {
            _onMonsterDeath = OnMonsterDeath;
            var random = new WinningRandom();
            _rng = random;
            return random;
        }
        internal void SpawnIssue62Enemy(Monster monster, double multiplier, bool roomSpawn = true)
        {
            OnEntityAdd(monster);
            _pressureDividendSpawns[monster].RecordAppliedHpMultiplier(multiplier);
            if (roomSpawn) typeof(NativePressureDividendLootSpawn)
                .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { monster });
        }
        internal void RemoveIssue62Entity(Entity entity) => OnEntityRemove(entity);
        internal void PruneIssue62Deaths() => PrunePressureDividendDeaths();
        internal bool TryIssue62Activation(Actor actor, out MemoryActivationIdentity identity) =>
            TryGetMemoryActivation(actor, out identity);
        internal (int Combat, int Queue, int Spawns, int Expiry, int Native, int Rolls) Issue62Records =>
            (_monsters.Count, _spawnQueue.Count, _pressureDividendSpawns.Count, _pressureDividendDeathExpiry.Count,
             _nativePressureLootSpawns.Count, _pressureDividendRolls.Count);
    }
    internal sealed partial class ClientSession
    {
        internal static RunState HostRun;
        internal static void PublishHostKillFact(AuthoritativeRunKill fact) { }
    }
}
