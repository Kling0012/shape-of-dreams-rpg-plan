using System;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private Action<DreamforgeGemSlotConflictMsg> _onGemSlotConflict;
        private Hero _gemSlotConflictHero;
        private uint _gemSlotConflictHeroNetId;
        private bool _gemSlotConflict;

        public bool GemSlotConflict
        {
            get
            {
                TickGemSlotConflict();
                return _gemSlotConflict;
            }
        }

        private void RegisterGemSlotConflict(Actor actor)
        {
            if (_onGemSlotConflict == null) _onGemSlotConflict = OnGemSlotConflict;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeGemSlotConflictMsg>(_onGemSlotConflict);
        }

        private void UnregisterGemSlotConflict(Actor actor)
        {
            if (_onGemSlotConflict != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeGemSlotConflictMsg>(_onGemSlotConflict); } catch (Exception) { }
        }

        private void ResetGemSlotConflict()
        {
            _gemSlotConflictHero = null;
            _gemSlotConflict = false;
            _gemSlotConflictHeroNetId = 0;
        }

        private void TickGemSlotConflict()
        {
            var hero = LocalHero;
            uint netId = hero != null ? hero.netId : 0;
            if (ReferenceEquals(hero, _gemSlotConflictHero) && netId == _gemSlotConflictHeroNetId) return;
            _gemSlotConflictHero = hero;
            _gemSlotConflictHeroNetId = netId;
            _gemSlotConflict = false;
        }

        private void OnGemSlotConflict(DreamforgeGemSlotConflictMsg msg)
        {
            TickGemSlotConflict();
            if (msg == null || msg.heroNetId == 0) return;
            var hero = _gemSlotConflictHero;
            if (hero == null || _gemSlotConflictHeroNetId != msg.heroNetId) return;
            Protocol.WarnMismatch(msg.protocol, nameof(DreamforgeGemSlotConflictMsg));
            _gemSlotConflict = msg.disabled;
        }
    }
}
