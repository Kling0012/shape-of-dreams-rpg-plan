using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // The cast scope is established before OnPrepare can dispatch native damage.
    [HarmonyPatch(typeof(SkillTrigger), nameof(SkillTrigger.OnCastComplete))]
    internal static class NativeAttributedMemoryCast
    {
        internal sealed class Cast
        {
            internal SkillTrigger Skill;
            internal MemoryActivationIdentity Identity;
            internal CastInfo Info;
        }
        internal static Cast Current;
        private static void Prefix(SkillTrigger __instance, CastInfo info, out Cast __state)
        {
            __state = Current;
            Current = NetworkServer.active ? HostAuthority.NativeInstance?.BeginAttributedMemoryCast(__instance) : null;
            if (Current != null) Current.Info = info;
        }
        private static void Postfix()
        {
            if (Current != null) HostAuthority.NativeInstance?.PublishAttributedMemoryUse(Current);
        }
        private static void Finalizer(Cast __state)
        {
            HostAuthority.NativeInstance?.EndAttributedMemoryCast(Current);
            Current = __state;
        }
    }

    internal sealed partial class HostAuthority
    {
        private readonly BossObjectPool<NativeAttributedMemoryCast.Cast> _attributionCastPool =
            new BossObjectPool<NativeAttributedMemoryCast.Cast>(64, () => new NativeAttributedMemoryCast.Cast());
        internal NativeAttributedMemoryCast.Cast BeginAttributedMemoryCast(SkillTrigger skill)
        {
            if (!(skill.owner is Hero hero) || !Alive(hero) || AttributionGeneratedOrigin() != GeneratedOrigin.None) return null;
            EnsureMemoryAttributionEquipment(hero);
            bool source = false;
            foreach (var pair in _attributionEquipment[hero])
                if (pair.Value == skill) { source = pair.Key != HeroSkillLocation.Movement; break; }
            if (!source) return null;
            var cast = _attributionCastPool.Rent();
            if (cast == null) return null;
            cast.Skill = skill;
            cast.Identity = _memoryAttribution.BeginActivation(hero.GetInstanceID(), NativeActorTypeName(skill));
            return cast;
        }
        internal void EndAttributedMemoryCast(NativeAttributedMemoryCast.Cast cast)
        {
            if (cast == null) return;
            _memoryAttribution.ForgetNotification("native.publish", cast.Identity.Event(MemoryEventKind.ConfirmedUse));
            cast.Skill = null; cast.Identity = default; cast.Info = default;
            _attributionCastPool.Return(cast);
        }

        internal void PublishAttributedMemoryUse(NativeAttributedMemoryCast.Cast cast)
        {
            if (cast.Skill.owner is Hero hero) PublishMemoryActivation(cast.Identity.Event(MemoryEventKind.ConfirmedUse), hero, null);
            PublishBossConfirmedMemoryUse(cast);
        }
        internal void PublishAttributedBasicFired(AttackTrigger attack, AbilityInstance instance, CastInfo info)
        {
            if (!(attack.owner is Hero hero) || info.caster != hero || instance == null || !Alive(hero)) return;
            if (TryGetMemoryActivation(instance, out var identity) && identity.NativePayloadKind == NativePayloadKind.MainBasicAttack)
            {
                if (hero.Skill.GetSkill(HeroSkillLocation.Identity) is St_D_CircleOfLife)
                {
                    foreach (var summon in hero.summons)
                        if (summon != null && summon.isActive && summon.currentHealth > 0f
                            && summon.FindFirstAncestorOfType<Hero>() == hero)
                        {
                            identity = _memoryAttribution.ProjectOwnedBasicSource(identity, "native.circle-life.owned-fired");
                            break;
                        }
                }
                var notification = identity.Event(MemoryEventKind.OwnedBasicAttackFired);
                try { PublishMemoryActivation(notification, hero, null); }
                finally { _memoryAttribution.ForgetNotification("native.publish", notification); }
            }
        }

        private void PublishMemoryActivation(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage = 0f)
        {
            if (!_memoryAttribution.TryAdmitNotification("native.publish", notification)) return;
            var packet = NativeAttributedDamagePacket.Current;
            if (packet != null && notification.DamagePacketId == packet.Serial)
            {
                packet.NotificationVictim = notification.VictimId;
                packet.Notifications |= 1 << (int)notification.EventKind;
            }
            MemoryActivationPublished?.Invoke(notification, hero, victim, nativeDamage);
        }
    }
}
