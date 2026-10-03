namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private void InitializeAssignedMechanisms()
        {
            InitializeDirectedRecharge();
            InitializeBridgeSuccessEffects();
            InitializeMemoryPrimedRelay();
        }

        private void ClearAssignedMechanismTransients()
        {
            ResetMemoryAttribution();
            foreach (var runtime in _directedRecharges.Values) runtime.ClearTransient();
            foreach (var state in _bridgeSuccessEffects.Values) state.Runtime.ClearSuccessEffectsTransient();
            foreach (var binding in _calmBindings.Values) binding.Filter.Reset();
            ClearMemoryPrimedRelay();
        }

        private void OnAssignedMechanismDeath(Hero hero)
        {
            if (ReferenceEquals(hero, null)) return;
            _memoryAttribution.InvalidateOwner(hero.GetInstanceID());
            _attributionEquipment.Remove(hero);
            ClearDirectedRecharge(hero);
            ClearBridgeSuccessEffects(hero);
            ClearMemoryPrimedRelay(hero);
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
        }

        private void ClearAssignedMechanismSession()
        {
            ClearAssignedMechanismTransients();
            _directedRecharges.Clear();
            _bridgeSuccessEffects.Clear();
            ClearCalmStunBindings();
            _memoryPrimedRelay.Clear();
            // Subscriptions belong to this HostAuthority instance and survive reconnects.
        }
    }
}
