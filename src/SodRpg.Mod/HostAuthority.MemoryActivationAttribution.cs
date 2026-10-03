using System;
using System.Collections.Generic;
using System.Reflection.Emit;
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

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnAbilityInstanceBeforePrepare))]
    internal static class NativeAttributedInstance
    {
        private static void Prefix(EventInfoAbilityInstance info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BindAttributedNativeInstance(info);
        }
    }

    [HarmonyPatch(typeof(AttackTrigger), nameof(AttackTrigger.CallAttackCompleteBeforePrepareRoutines))]
    internal static class NativeAttributedBasicPrepare
    {
        private static void Prefix(AttackTrigger __instance, EventInfoCast cast)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BindAttributedBasicAttack(__instance, cast.instance, cast.info);
        }
        private static void Postfix(AttackTrigger __instance, EventInfoCast cast, int ____nextEveryFourAttackIndex)
        {
            if (NetworkServer.active && ____nextEveryFourAttackIndex == 3)
                HostAuthority.NativeInstance?.BindResolveBeforePrepare(__instance, cast);
        }
    }

    [HarmonyPatch(typeof(AttackTrigger), nameof(AttackTrigger.CallAttackCompleteRoutines))]
    internal static class NativeAttributedBasicFired
    {
        private static void Postfix(AttackTrigger __instance, AbilityInstance newInstance, CastInfo info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.PublishAttributedBasicFired(__instance, newInstance, info);
        }
    }

    // The exact DoBasicAttackHit -> DealDamage call identifies the main packet even when attackEffect is zero.
    // Nested native added damage cannot claim this scope; the first DealDamage entry consumes its marker.
    [HarmonyPatch(typeof(Actor), nameof(Actor.DoBasicAttackHit))]
    internal static class NativeAttributedBasicPacket
    {
        internal sealed class Dispatch { internal bool Claimed; internal bool Main; internal Entity Owner; }
        internal static Dispatch Current;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(Actor), nameof(Actor.DealDamage), new[] { typeof(DamageData), typeof(Entity), typeof(ReactionChain) });
            var replacement = AccessTools.Method(typeof(NativeAttributedBasicPacket), nameof(DispatchDamage));
            int count = 0;
            var result = new List<CodeInstruction>();
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
                result.Add(instruction);
            }
            if (count != 1) throw new InvalidOperationException("C02 expected exactly one native main-attack damage dispatch.");
            return result;
        }
        private static void DispatchDamage(Actor actor, DamageData damage, Entity target, ReactionChain chain)
        {
            var previous = Current;
            var basic = BasicAttackContext.Current;
            Current = new Dispatch { Main = basic != null && basic.Actor == actor && basic.Primary,
                Owner = basic != null ? basic.From : null };
            try { actor.DealDamage(damage, target, chain); }
            finally { Current = previous; }
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class NativeAttributedDamagePacket
    {
        internal sealed class Packet
        {
            internal Actor Actor;
            internal Entity Victim;
            internal long Serial;
            internal MemoryActivationIdentity Identity;
            internal ReactionChain Chain;
            internal bool Admitted, MainBasic;
            internal float DamageAmount;
        }
        internal static Packet Current;
        private static void Prefix(Actor __instance, Entity target, ReactionChain chain, out Packet __state)
        {
            __state = Current;
            bool main = false;
            long basicOwner = 0;
            var basic = NativeAttributedBasicPacket.Current;
            if (basic != null && !basic.Claimed)
            {
                basic.Claimed = true;
                main = basic.Main && basic.Owner is Hero;
                if (main) basicOwner = basic.Owner.GetInstanceID();
            }
            Current = NetworkServer.active
                ? HostAuthority.NativeInstance?.BeginAttributedDamagePacket(__instance, target, chain, main, basicOwner) : null;
        }
        private static void Finalizer(Packet __state) { Current = __state; }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnDealDamage))]
    internal static class NativeAttributedFinalDamage
    {
        private static void Postfix(EventInfoDamage info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.PublishAttributedFinalDamage(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnKill))]
    internal static class NativeAttributedKill
    {
        private static void Postfix(EventInfoKill info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.PublishAttributedKill(info);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors))]
    internal static class NativeAttributedPooledLifetime
    {
        private static void Prefix(Actor __instance) { HostAuthority.NativeInstance?.ClearAttributedActorLifetime(__instance); }
    }

    [HarmonyPatch(typeof(HeroSkill), nameof(HeroSkill.UnequipSkill))]
    internal static class NativeAttributedUnequip
    {
        private static void Postfix(HeroSkill __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshMemoryAttributionEquipment(__instance.hero);
        }
    }

    [HarmonyPatch(typeof(HeroSkill), nameof(HeroSkill.EquipSkill))]
    internal static class NativeAttributedEquip
    {
        private static void Postfix(HeroSkill __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshMemoryAttributionEquipment(__instance.hero);
        }
    }

    internal sealed partial class HostAuthority
    {
        private readonly MemoryActivationAttribution _memoryAttribution = new MemoryActivationAttribution();
        private readonly Dictionary<Hero, Dictionary<HeroSkillLocation, SkillTrigger>> _attributionEquipment =
            new Dictionary<Hero, Dictionary<HeroSkillLocation, SkillTrigger>>();
        private readonly Dictionary<Entity, long> _attributionVictimLifetimes = new Dictionary<Entity, long>();
        private bool _attributionAdaptersRegistered;
        internal event Action<MemoryActivationEvent, Hero, Entity, float> MemoryActivationPublished;
        internal event Action<Hero, long> MemoryAttributionEquipmentChanged;

        internal long RefreshMemoryAttributionEquipment(Hero hero)
        {
            if (hero == null) return 0;
            if (!_attributionAdaptersRegistered)
            {
                _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.circle-life.owned-fired",
                    nameof(St_D_CircleOfLife), NativePayloadKind.MainBasicAttack));
                RegisterExactNativeMemoryAdapters();
                _attributionAdaptersRegistered = true;
            }
            var equipment = new Dictionary<HeroSkillLocation, SkillTrigger>();
            var memories = new List<string>();
            foreach (var location in LinkSkills)
            {
                var skill = hero.Skill.GetSkill(location);
                if (skill == null) continue;
                equipment.Add(location, skill);
                memories.Add(skill.GetType().Name);
            }
            bool changed = !_attributionEquipment.TryGetValue(hero, out var previous) || previous.Count != equipment.Count;
            if (!changed)
                foreach (var pair in equipment)
                    if (!previous.TryGetValue(pair.Key, out var old) || old != pair.Value) { changed = true; break; }
            if (changed)
            {
                _memoryAttribution.InvalidateOwner(hero.GetInstanceID());
                _attributionEquipment[hero] = equipment;
            }
            long epoch = _memoryAttribution.SetEquipment(hero.GetInstanceID(), memories);
            if (changed) MemoryAttributionEquipmentChanged?.Invoke(hero, epoch);
            return epoch;
        }

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

        internal void BindAttributedNativeInstance(EventInfoAbilityInstance info)
        {
            if (info.instance == null || info.instance.gem != null || info.actor is Gem
                || AttributionGeneratedOrigin() != GeneratedOrigin.None) return;
            if (BindExactNativePayload(info)) return;
            if (RequiresExactNativeInstanceScope(info.instance)) return;
            var cast = NativeAttributedMemoryCast.Current;
            if (cast != null && info.actor == cast.Skill)
                _memoryAttribution.BindInstance(info.instance.GetInstanceID(), cast.Identity);
            else if (TryGetMemoryActivation(info.actor, out var identity))
                _memoryAttribution.BindInstance(info.instance.GetInstanceID(), identity);
        }

        internal void BindAttributedBasicAttack(AttackTrigger attack, AbilityInstance instance, CastInfo info)
        {
            if (attack.owner is Summon summon)
            {
                BindAttributedSummonAttack(summon, instance, info);
                return;
            }
            if (instance == null || !(attack.owner is Hero hero) || info.caster != hero || !Alive(hero)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None) return;
            RefreshMemoryAttributionEquipment(hero);
            var identity = _memoryAttribution.BeginActivation(hero.GetInstanceID(), null, NativePayloadKind.MainBasicAttack);
            _memoryAttribution.BindInstance(instance.GetInstanceID(), identity);
            BindExactNativeBasicSource(attack, instance, hero, identity);
        }

        private void BindAttributedSummonAttack(Summon summon, AbilityInstance instance, CastInfo info)
        {
            if (instance == null || instance.gem != null || info.caster != summon || !summon.isActive || summon.currentHealth <= 0f
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !(summon.info.caster is Hero hero) || !Alive(hero)) return;
            var trigger = summon.FindFirstAncestorOfType<SkillTrigger>();
            if (trigger == null || trigger.owner != hero || FindMemory(hero, trigger.GetType().Name) != trigger) return;
            foreach (var location in LinkSkills)
                if (hero.Skill.GetSkill(location) == trigger && location == HeroSkillLocation.Movement) return;
            for (var current = (Actor)summon; current != null; current = current.parentActor)
                if (current is Gem || current is ElementalStatusEffect || current is AbilityInstance ability && ability.gem != null) return;
            RefreshMemoryAttributionEquipment(hero);
            _memoryAttribution.BindInstance(instance.GetInstanceID(),
                _memoryAttribution.BeginActivation(hero.GetInstanceID(), trigger.GetType().Name, NativePayloadKind.SummonAttack));
        }

        internal bool TryGetMemoryActivation(Actor actor, out MemoryActivationIdentity identity)
        {
            identity = default(MemoryActivationIdentity);
            if (actor == null || AttributionGeneratedOrigin() != GeneratedOrigin.None) return false;
            bool found = false;
            string authoritativeMemory = null;
            Hero owner = null;
            var visited = new HashSet<Actor>();
            for (var current = actor; current != null; current = current.parentActor)
            {
                if (!visited.Add(current)) throw new InvalidOperationException("Native actor ancestry contains a cycle.");
                if (current is Gem || current is ElementalStatusEffect
                    || current is AbilityInstance ability && ability.gem != null) return false;
                bool tagged = _memoryAttribution.TryGetInstance(current.GetInstanceID(), out var tag);
                if (RequiresExactNativeInstanceScope(current) && !tagged) return false;
                if (!found && tagged) { identity = tag; found = true; }
                if (current is Summon && (!found || identity.NativePayloadKind != NativePayloadKind.SummonAttack)) return false;
                if (current is SkillTrigger skill && authoritativeMemory == null)
                {
                    authoritativeMemory = skill.GetType().Name;
                    owner = skill.owner as Hero;
                }
                if (current is Hero hero && owner == null) owner = hero;
            }
            if (found && owner == null) owner = AttributedOwner(identity.OwnerId);
            if (!found || owner == null || !Alive(owner) || owner.GetInstanceID() != identity.OwnerId) return false;
            RefreshMemoryAttributionEquipment(owner);
            if (!_memoryAttribution.IsCurrent(identity)) return false;
            // The actual SkillTrigger parent is authoritative; never replace it using an Ai type-name guess.
            return identity.SourceMemory.Length == 0 && identity.NativePayloadKind == NativePayloadKind.MainBasicAttack
                || authoritativeMemory == null || authoritativeMemory == identity.SourceMemory;
        }

        private GeneratedOrigin AttributionGeneratedOrigin()
        {
            if (_gimmickDamageDepth != 0) return GeneratedOrigin.Gimmick;
            if (_pairDamageDepth != 0) return GeneratedOrigin.Bridge;
            if (_reactionEffectDepth != 0 || _reflectingDamage || _shattering) return GeneratedOrigin.Reaction;
            return GeneratedOrigin.None;
        }

        private static bool RequiresExactNativeInstanceScope(Actor actor)
        {
            var type = actor.GetType();
            return type == typeof(Ai_D_ChargedAnguillian_Lightning) || type == typeof(Ai_D_IcyVeins_Damage)
                || type == typeof(Ai_D_BeautifulThreat_Feather) || RequiresExactNativeProjectileScope(actor);
        }

        // Migrate verified scopes only; unrelated legacy routes retain their existing source handling until explicitly migrated.
        private bool TryGetAttributedDamageSource(Actor actor, out string memory)
        {
            memory = null;
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || packet.Actor != actor) return false;
            if (packet.Admitted)
            {
                if (packet.Identity.SourceMemory.Length != 0) memory = packet.Identity.SourceMemory;
                return true;
            }
            var exact = NativeMemoryPayloadScope.Current;
            return exact != null && exact.Source == actor && exact.ChildType == null;
        }

        private bool IsAttributedNativePacket(Actor actor, Entity victim)
        {
            var packet = NativeAttributedDamagePacket.Current;
            return packet != null && packet.Actor == actor && packet.Victim == victim && packet.Admitted;
        }

        private long AttributedActivationSerial(Actor actor)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet != null && packet.Actor == actor) return packet.Admitted ? packet.Identity.ActivationId : 0;
            return TryGetMemoryActivation(actor, out var identity) ? identity.ActivationId : 0;
        }

        internal NativeAttributedDamagePacket.Packet BeginAttributedDamagePacket(Actor actor, Entity target, ReactionChain chain, bool main, long basicOwner)
        {
            var packet = new NativeAttributedDamagePacket.Packet { Actor = actor, Victim = target,
                Serial = _memoryAttribution.NewPacketId(), Chain = chain };
            var direct = NativeMemoryPayloadScope.Current;
            if (!main && direct != null && direct.Source == actor && direct.ChildType == null)
                packet.Admitted = TryGetExactNativeDirectPayload(actor, target, chain, out packet.Identity);
            else
                packet.Admitted = TryGetMemoryActivation(actor, out packet.Identity) && ExactNativeChainMatches(actor, chain)
                    && !IsPairReactionSource(actor) && _memoryAttribution.CanAdmit(packet.Identity, false, false, !chain.Equals(default(ReactionChain)));
            if (main && packet.Identity.OwnerId != basicOwner) packet.Admitted = false;
            packet.MainBasic = main && packet.Identity.OwnerId == basicOwner && packet.Identity.NativePayloadKind == NativePayloadKind.MainBasicAttack;
            if (packet.Admitted && packet.MainBasic && packet.Identity.SourceMemory.Length == 0
                && AttributedOwner(packet.Identity.OwnerId) is Hero owner
                && owner.Skill.GetSkill(HeroSkillLocation.Identity) is St_D_TheKillingFlow source
                && source.owner == owner)
                packet.Identity = _memoryAttribution.ProjectOwnedBasicSource(packet.Identity, "native.killing-flow.main");
            return packet;
        }

        private bool TryGetNativeTriggerActivation(EventInfoAttackEffect input, out MemoryActivationIdentity identity)
        {
            identity = default(MemoryActivationIdentity);
            if (AttributionGeneratedOrigin() != GeneratedOrigin.None) return false;
            var packet = NativeAttributedDamagePacket.Current;
            if (packet != null && packet.Actor == input.actor && packet.Victim == input.victim && packet.Chain.Equals(input.chain))
            {
                if (!packet.Admitted) return false;
                identity = packet.Identity;
                return _memoryAttribution.IsCurrent(identity);
            }
            return input.chain.Equals(default(ReactionChain)) && TryGetMemoryActivation(input.actor, out identity);
        }

        internal void PublishAttributedMemoryUse(NativeAttributedMemoryCast.Cast cast)
        {
            if (cast.Skill.owner is Hero hero) PublishMemoryActivation(cast.Identity.Event(MemoryEventKind.ConfirmedUse), hero, null);
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

        internal void PublishAttributedFinalDamage(EventInfoDamage info)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || !packet.Admitted || packet.Actor != info.actor || packet.Victim != info.victim
                || info.victim == null || info.damage.amount <= 0f) return;
            var hero = AttributedOwner(packet.Identity.OwnerId);
            if (hero == null || info.victim.GetRelation(hero) != EntityRelation.Enemy) return;
            packet.DamageAmount = info.damage.amount;
            long victim = AttributedVictimLifetime(info.victim);
            if (packet.MainBasic)
                PublishMemoryActivation(packet.Identity.Event(MemoryEventKind.OwnedBasicAttackHit, packet.Serial, victim), hero, info.victim, info.damage.amount);
            if (packet.Identity.SourceMemory.Length != 0)
            {
                PublishMemoryActivation(packet.Identity.Event(MemoryEventKind.Hit, packet.Serial, victim), hero, info.victim, info.damage.amount);
                if (info.damage.HasAttr(DamageAttribute.IsCrit))
                    PublishMemoryActivation(packet.Identity.Event(MemoryEventKind.CriticalHit, packet.Serial, victim), hero, info.victim, info.damage.amount);
            }
        }

        internal void PublishAttributedKill(EventInfoKill info)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || !packet.Admitted || packet.Actor != info.actor || packet.Victim != info.victim || info.victim == null) return;
            var hero = AttributedOwner(packet.Identity.OwnerId);
            if (hero != null && info.victim.GetRelation(hero) == EntityRelation.Enemy)
                PublishMemoryActivation(packet.Identity.Event(MemoryEventKind.Kill, packet.Serial, AttributedVictimLifetime(info.victim)), hero, info.victim, packet.DamageAmount);
        }

        private Hero AttributedOwner(long ownerId)
        {
            foreach (var hero in _attributionEquipment.Keys) if (hero != null && hero.GetInstanceID() == ownerId && Alive(hero)) return hero;
            return null;
        }

        private long AttributedVictimLifetime(Entity victim)
        {
            if (!_attributionVictimLifetimes.TryGetValue(victim, out long id)) _attributionVictimLifetimes[victim] = id = _memoryAttribution.NewPacketId();
            return id;
        }

        private void PublishMemoryActivation(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage = 0f)
        {
            if (!_memoryAttribution.TryAdmitNotification("native.publish", notification)) return;
            MemoryActivationPublished?.Invoke(notification, hero, victim, nativeDamage);
        }

        internal void ClearAttributedActorLifetime(Actor actor)
        {
            if (actor == null) return;
            _memoryAttribution.EndInstanceLifetime(actor.GetInstanceID());
            _attributedNativeChains.Remove(actor);
            ClearNativeEndingActor(actor);
            if (actor is Entity entity) _attributionVictimLifetimes.Remove(entity);
            if (actor is Hero hero)
            {
                _memoryAttribution.InvalidateOwner(hero.GetInstanceID());
                _attributionEquipment.Remove(hero);
            }
        }

        private void ResetMemoryAttribution()
        {
            _memoryAttribution.Reset(); _attributionEquipment.Clear(); _attributionVictimLifetimes.Clear(); _attributedNativeChains.Clear();
            ResetNativeEndingAdapters();
        }
    }
}
