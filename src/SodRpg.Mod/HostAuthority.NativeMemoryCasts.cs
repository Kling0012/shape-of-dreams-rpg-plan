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
        }
        internal static Cast Current;
        private static void Prefix(SkillTrigger __instance, out Cast __state)
        {
            __state = Current;
            Current = NetworkServer.active ? HostAuthority.NativeInstance?.BeginAttributedMemoryCast(__instance) : null;
        }
        private static void Postfix()
        {
            if (Current != null) HostAuthority.NativeInstance?.PublishAttributedMemoryUse(Current);
        }
        private static void Finalizer(Cast __state) { Current = __state; }
    }

    internal sealed partial class HostAuthority
    {
        internal NativeAttributedMemoryCast.Cast BeginAttributedMemoryCast(SkillTrigger skill)
        {
            if (!(skill.owner is Hero hero) || !Alive(hero) || AttributionGeneratedOrigin() != GeneratedOrigin.None) return null;
            RefreshMemoryAttributionEquipment(hero);
            bool source = false;
            foreach (var pair in _attributionEquipment[hero])
                if (pair.Value == skill) { source = pair.Key != HeroSkillLocation.Movement; break; }
            if (!source) return null;
            return new NativeAttributedMemoryCast.Cast { Skill = skill,
                Identity = _memoryAttribution.BeginActivation(hero.GetInstanceID(), skill.GetType().Name) };
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
                PublishMemoryActivation(identity.Event(MemoryEventKind.OwnedBasicAttackFired), hero, null);
            }
        }

        private void PublishMemoryActivation(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage = 0f)
        {
            if (!_memoryAttribution.TryAdmitNotification("native.publish", notification)) return;
            MemoryActivationPublished?.Invoke(notification, hero, victim, nativeDamage);
        }
    }
}
