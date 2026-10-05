namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private void InitializeAssignedMechanisms()
        {
            InitializeDirectedRecharge();
            InitializeBridgeSuccessEffects();
            InitializeMemoryPrimedRelay();
            MemoryActivationPublished += OnAuthoredMechanismEvent;
            MemoryAttributionEquipmentChanged += OnAuthoredMechanismEquipmentChanged;
        }

        private void ClearAssignedMechanismTransients()
        {
            ResetMemoryAttribution();
            foreach (var runtime in _directedRecharges.Values) runtime.ClearTransient();
            foreach (var state in _bridgeSuccessEffects.Values) state.Runtime.ClearSuccessEffectsTransient();
            foreach (var binding in _calmBindings.Values) binding.Filter.Reset();
            ClearMemoryPrimedRelay();
            ClearAuthoredMechanismTransients();
            foreach (var rt in _runtimes.Values) ClearBossEffects(rt);
            _nativePressureLootSpawns.Clear();
        }

        private void OnAssignedMechanismDeath(Hero hero)
        {
            if (ReferenceEquals(hero, null)) return;
            _memoryAttribution.InvalidateOwner(hero.GetInstanceID());
            _attributionEquipment.Remove(hero);
            _mechanismEquipment.Remove(hero);
            _attributionMemoryIds.Remove(hero);
            ClearDirectedRecharge(hero);
            ClearBridgeSuccessEffects(hero);
            ClearMemoryPrimedRelay(hero);
            ClearAuthoredMechanismTransients(hero);
            if (_runtimes.TryGetValue(hero, out var rt)) ClearBossEffects(rt);
            if (_calmBindings.TryGetValue(hero, out var binding)) binding.Filter.Reset();
        }

        private void ForgetAssignedMechanismOwner(Hero hero)
        {
            if (ReferenceEquals(hero, null)) return;
            OnAssignedMechanismDeath(hero);
            _directedRecharges.Remove(hero);
            _bridgeSuccessEffects.Remove(hero);
            _calmBindings.Remove(hero);
            RemoveMemoryPrimedRelay(hero);
            ClearAuthoredKeystone(hero);
            _authoredMechanisms.Remove(hero);
            ForgetIdentityStrikes(hero);
            ForgetMemoryTunings(hero);
        }

        private void ClearAssignedMechanismSession()
        {
            ClearAssignedMechanismTransients();
            foreach (var hero in _authoredMechanisms.Keys) ClearAuthoredKeystone(hero);
            _directedRecharges.Clear();
            _bridgeSuccessEffects.Clear();
            ClearCalmStunBindings();
            _memoryPrimedRelay.Clear();
            _authoredMechanisms.Clear();
            _identityStrikes.Clear();
            ClearMemoryTunings();
            _identityDashBonus.Clear();
            // Subscriptions belong to this HostAuthority instance and survive reconnects.
        }
    }
}
