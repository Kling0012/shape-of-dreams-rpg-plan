using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{

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
        internal struct Dispatch { internal bool Claimed; internal bool Main; internal Entity Owner; }
        internal static Dispatch Current;
        private static readonly List<Dispatch> Scopes = new List<Dispatch>();
        private static int _depth;
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
            if (_depth == Scopes.Count) Scopes.Add(new Dispatch());
            var scope = Scopes[_depth++];
            scope.Claimed = false;
            scope.Main = basic != null && basic.Actor == actor && basic.Primary;
            scope.Owner = basic != null ? basic.From : null;
            Current = scope;
            try { actor.DealDamage(damage, target, chain); }
            finally { Current = previous; scope.Owner = null; _depth--; }
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
            internal float HpDamage;
            internal long NotificationVictim;
            internal int Notifications;
        }
        internal static Packet Current;
        private static readonly List<Packet> Packets = new List<Packet>();
        private static int _depth;
        private struct Scope
        {
            internal Packet Previous, Rented;
            internal bool Started;
        }
        private static void Prefix(Actor __instance, DamageData damage, Entity target, ReactionChain chain, out Scope __state)
        {
            __state = new Scope { Previous = Current, Started = true };
            Current = null;
            if (!NetworkServer.active || HostAuthority.NativeInstance == null) return;
            bool main = false;
            long basicOwner = 0;
            ref var basic = ref NativeAttributedBasicPacket.Current;
            if (basic.Owner != null && !basic.Claimed)
            {
                basic.Claimed = true;
                main = basic.Main && basic.Owner is Hero;
                if (main) basicOwner = basic.Owner.GetInstanceID();
            }
            if (_depth == Packets.Count) Packets.Add(new Packet());
            var packet = Packets[_depth++];
            __state.Rented = packet;
            packet.Actor = null; packet.Victim = null; packet.Identity = default;
            packet.Chain = default; packet.Serial = 0; packet.Admitted = false; packet.MainBasic = false;
            packet.DamageAmount = packet.HpDamage = 0; packet.NotificationVictim = 0; packet.Notifications = 0;
            HostAuthority.NativeInstance.BeginAttributedDamagePacket(packet, __instance, target, chain, main, basicOwner);
            Current = packet;
            HostAuthority.NativeInstance.ObserveBossGeneratedDispatch(packet, damage);
        }
        private static void Finalizer(Scope __state)
        {
            if (!__state.Started) return;
            var packet = __state.Rented;
            if (packet != null)
            {
                HostAuthority.NativeInstance?.EndAttributedDamagePacket(packet);
                _depth--;
            }
            Current = __state.Previous;
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class NativeAttributedHpDamage
    {
        private static readonly Action<EntityStatus, float> NativeSetHealth =
            (Action<EntityStatus, float>)Delegate.CreateDelegate(typeof(Action<EntityStatus, float>),
                AccessTools.PropertySetter(typeof(EntityStatus), nameof(EntityStatus.currentHealth)));
        internal static void Prewarm() { _ = NativeSetHealth; }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setter = AccessTools.PropertySetter(typeof(EntityStatus), nameof(EntityStatus.currentHealth));
            var replacement = AccessTools.Method(typeof(NativeAttributedHpDamage), nameof(SetHealth));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(setter)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Native HP attribution requires exactly one DealDamage health setter.");
        }
        private static void SetHealth(EntityStatus status, float health)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet != null && packet.Victim != null && packet.Victim.Status == status)
                packet.HpDamage = Math.Max(0f, status.currentHealth - Math.Max(0f, health));
            NativeSetHealth(status, health);
        }
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

    // Direct memory destruction also removes abilities here. Set's postfix waits for owner/index linkage;
    // its nested Remove publishes the empty slot before the replacement is equipped.
    [HarmonyPatch(typeof(EntityAbility), nameof(EntityAbility.RemoveAbility), new[] { typeof(int) })]
    internal static class NativeAttributedAbilityRemoved
    {
        private static void Postfix(EntityAbility __instance, int index)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshNativeMemoryAttributionEquipment(__instance, index);
        }
    }

    [HarmonyPatch(typeof(EntityAbility), nameof(EntityAbility.SetAbility), new[] { typeof(int), typeof(AbilityTrigger) })]
    internal static class NativeAttributedAbilitySet
    {
        private static void Postfix(EntityAbility __instance, int index)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshNativeMemoryAttributionEquipment(__instance, index);
        }
    }

    internal sealed partial class HostAuthority
    {
        private readonly MemoryActivationAttribution _memoryAttribution = new MemoryActivationAttribution();
        private readonly Dictionary<Hero, Dictionary<HeroSkillLocation, SkillTrigger>> _attributionEquipment =
            new Dictionary<Hero, Dictionary<HeroSkillLocation, SkillTrigger>>();
        private readonly Dictionary<Entity, long> _attributionVictimLifetimes = new Dictionary<Entity, long>();
        private readonly Dictionary<Hero, MechanismEquipment> _mechanismEquipment = new Dictionary<Hero, MechanismEquipment>();
        private readonly Dictionary<Hero, HashSet<string>> _attributionMemoryIds = new Dictionary<Hero, HashSet<string>>();
        private readonly List<(WeakReference Capture, MemoryActivationIdentity Identity)> _deferredAttribution =
            new List<(WeakReference, MemoryActivationIdentity)>();
        private readonly HashSet<long> _deferredActivationScratch = new HashSet<long>();
        private readonly HashSet<long> _liveVictimScratch = new HashSet<long>();
        private readonly List<long> _attributionSerialScratch = new List<long>();
        private long _lastAttributionPrune;
        private bool _attributionAdaptersRegistered;
        private static readonly Dictionary<Type, string> NativeActorNames = CreateNativeActorNames();
        private readonly SkillTrigger[] _nativeEquipmentScratch = new SkillTrigger[LinkSkills.Length];

        private static Dictionary<Type, string> CreateNativeActorNames()
        {
            var names = new Dictionary<Type, string>(4096);
            var types = typeof(St_U_Hysteria).Assembly.GetTypes();
            for (int i = 0; i < types.Length; i++)
                if (typeof(Actor).IsAssignableFrom(types[i])) names.Add(types[i], types[i].Name);
            return names;
        }
        private static string NativeActorTypeName(Actor actor) =>
            actor != null && NativeActorNames.TryGetValue(actor.GetType(), out var name) ? name : string.Empty;
        internal event Action<MemoryActivationEvent, Hero, Entity, float> MemoryActivationPublished;
        internal event Action<Hero, long> MemoryAttributionEquipmentChanged;

        internal void RefreshNativeMemoryAttributionEquipment(EntityAbility ability, int index)
        {
            if (index < (int)HeroSkillLocation.Q || index > (int)HeroSkillLocation.Movement
                || ability == null || !(ability.entity is Hero hero) || hero == null || !hero.isActive
                || hero.Ability != ability || hero.Skill == null) return;
            RefreshMemoryAttributionEquipment(hero);
        }

        private void InitializeMemoryAttribution()
        {
            if (_attributionAdaptersRegistered) return;
            _memoryAttribution.RegisterAdapter(new NativeMemoryAdapter("native.circle-life.owned-fired",
                nameof(St_D_CircleOfLife), NativePayloadKind.MainBasicAttack));
            RegisterExactNativeMemoryAdapters();
            _attributionAdaptersRegistered = true;
        }

        internal long RefreshMemoryAttributionEquipment(Hero hero)
        {
            if (hero == null || hero.Skill == null) return 0;
            InitializeMemoryAttribution();
            bool changed = !_attributionEquipment.TryGetValue(hero, out var equipment);
            int count = 0;
            for (int i = 0; i < LinkSkills.Length; i++)
            {
                var skill = hero.Skill.GetSkill(LinkSkills[i]);
                _nativeEquipmentScratch[i] = skill;
                if (skill == null) continue;
                count++;
                if (!changed && (!equipment.TryGetValue(LinkSkills[i], out var previous) || previous != skill)) changed = true;
            }
            changed |= equipment != null && equipment.Count != count;
            if (!changed) return _memoryAttribution.EquipmentEpoch(hero.GetInstanceID());
            if (equipment == null) equipment = new Dictionary<HeroSkillLocation, SkillTrigger>(LinkSkills.Length);
            else equipment.Clear();
            var memories = new List<string>(count);
            var mechanisms = new List<EquippedMechanismMemory>(count);
            for (int i = 0; i < LinkSkills.Length; i++)
            {
                var skill = _nativeEquipmentScratch[i];
                if (skill == null) continue;
                equipment.Add(LinkSkills[i], skill);
                string memory = NativeActorTypeName(skill);
                memories.Add(memory);
                mechanisms.Add(new EquippedMechanismMemory(memory, skill.GetInstanceID(), ToMechanismSlot(LinkSkills[i]),
                    skill.type == SkillType.Normal, skill.type == SkillType.Ultimate));
            }
            _memoryAttribution.InvalidateOwner(hero.GetInstanceID());
            long epoch = _memoryAttribution.SetEquipment(hero.GetInstanceID(), memories);
            _attributionEquipment[hero] = equipment;
            _mechanismEquipment[hero] = new MechanismEquipment(hero.GetInstanceID(), epoch, mechanisms);
            _attributionMemoryIds[hero] = new HashSet<string>(memories, StringComparer.Ordinal);
            if (_runtimes.TryGetValue(hero, out var rt))
            {
                ModShieldEquipmentEpoch(rt);
                BossEnsure(rt);
            }
            MemoryAttributionEquipmentChanged?.Invoke(hero, epoch);
            return epoch;
        }

        private long EnsureMemoryAttributionEquipment(Hero hero)
        {
            long epoch = _memoryAttribution.EquipmentEpoch(hero.GetInstanceID());
            return epoch != 0 ? epoch : RefreshMemoryAttributionEquipment(hero);
        }

        private void RetainDeferredAttribution(object capture, MemoryActivationIdentity identity)
            => _deferredAttribution.Add((new WeakReference(capture), identity));

        private void PruneMemoryAttribution()
        {
            if (_memoryAttribution.Serial - _lastAttributionPrune < 256) return;
            _lastAttributionPrune = _memoryAttribution.Serial;
            _deferredActivationScratch.Clear();
            for (int i = _deferredAttribution.Count - 1; i >= 0; i--)
            {
                var capture = _deferredAttribution[i];
                if (!capture.Capture.IsAlive || !_memoryAttribution.IsCurrent(capture.Identity)) _deferredAttribution.RemoveAt(i);
                else _deferredActivationScratch.Add(capture.Identity.ActivationId);
            }
            foreach (var ending in _nativeBaptismEndings.Values) _deferredActivationScratch.Add(ending.ActivationId);
            if (NativeAttributedMemoryCast.Current != null) _deferredActivationScratch.Add(NativeAttributedMemoryCast.Current.Identity.ActivationId);
            if (NativeAttributedDamagePacket.Current != null) _deferredActivationScratch.Add(NativeAttributedDamagePacket.Current.Identity.ActivationId);
            foreach (var runtime in _runtimes.Values)
                foreach (var pending in runtime.PendingGimmicks)
                    if (pending.Authored.Enabled && pending.Authored.Legacy == null)
                        _deferredActivationScratch.Add(pending.Authored.Notification.ActivationId);
            _liveVictimScratch.Clear();
            foreach (var victim in _attributionVictimLifetimes)
                if (victim.Key != null && victim.Key.isActive && victim.Key.currentHealth > 0) _liveVictimScratch.Add(victim.Value);
            _memoryAttribution.PruneLedgers(_deferredActivationScratch, _liveVictimScratch);
            foreach (var owner in _authoredMechanisms.Values)
                foreach (var channel in owner.Channels.Values)
                {
                    _attributionSerialScratch.Clear();
                    foreach (long activation in channel.Counted)
                        if (!_memoryAttribution.IsActivationRetained(activation)) _attributionSerialScratch.Add(activation);
                    foreach (long activation in _attributionSerialScratch) channel.Counted.Remove(activation);
                }
            foreach (var owner in _gimmickV129)
            {
                owner.Key.Gimmicks.PruneAttribution(_memoryAttribution);
                owner.Key.PairCombos.PruneAttributedActivations(_memoryAttribution);
            }
            foreach (var runtime in _directedRecharges.Values) runtime.PruneAttribution(_memoryAttribution);
            foreach (var state in _bridgeSuccessEffects.Values) state.Runtime.PruneSuccessAttribution(_memoryAttribution);
            foreach (var state in _memoryPrimedRelay.Values)
            { state.Primed.PruneAttribution(_memoryAttribution); state.Relay.PruneAttribution(_memoryAttribution); }
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
            EnsureMemoryAttributionEquipment(hero);
            var identity = _memoryAttribution.BeginActivation(hero.GetInstanceID(), null, NativePayloadKind.MainBasicAttack);
            _memoryAttribution.BindInstance(instance.GetInstanceID(), identity);
            BindExactNativeBasicSource(attack, instance, hero, identity);
        }

        private void BindAttributedSummonAttack(Summon summon, AbilityInstance instance, CastInfo info)
        {
            if (instance == null || instance.gem != null || info.caster != summon || !summon.isActive || summon.currentHealth <= 0f
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !(summon.info.caster is Hero hero) || !Alive(hero)) return;
            var trigger = summon.FindFirstAncestorOfType<SkillTrigger>();
            if (trigger == null || trigger.owner != hero || !BossNativeEquippedSkill(hero, trigger)) return;
            foreach (var location in LinkSkills)
                if (hero.Skill.GetSkill(location) == trigger && location == HeroSkillLocation.Movement) return;
            int depth = 0;
            for (var current = (Actor)summon; current != null; current = current.parentActor)
                if (++depth > 64 || current is Gem || current is ElementalStatusEffect || current is AbilityInstance ability && ability.gem != null) return;
            EnsureMemoryAttributionEquipment(hero);
            _memoryAttribution.BindInstance(instance.GetInstanceID(),
                _memoryAttribution.BeginActivation(hero.GetInstanceID(), NativeActorTypeName(trigger), NativePayloadKind.SummonAttack));
        }

        internal bool TryGetMemoryActivation(Actor actor, out MemoryActivationIdentity identity)
        {
            identity = default(MemoryActivationIdentity);
            if (actor == null || AttributionGeneratedOrigin() != GeneratedOrigin.None) return false;
            bool found = false;
            string authoritativeMemory = null;
            Hero owner = null;
            Actor slow = actor, fast = actor;
            for (var current = actor; current != null; current = current.parentActor)
            {
                slow = slow != null ? slow.parentActor : null;
                fast = fast != null && fast.parentActor != null ? fast.parentActor.parentActor : null;
                if (slow != null && slow == fast) throw new InvalidOperationException("Native actor ancestry contains a cycle.");
                if (current is Gem || current is ElementalStatusEffect
                    || current is AbilityInstance ability && ability.gem != null) return false;
                bool tagged = _memoryAttribution.TryGetInstance(current.GetInstanceID(), out var tag);
                if (RequiresExactNativeInstanceScope(current) && !tagged) return false;
                if (!found && tagged) { identity = tag; found = true; }
                if (current is Summon && (!found || identity.NativePayloadKind != NativePayloadKind.SummonAttack)) return false;
                if (current is SkillTrigger skill && authoritativeMemory == null)
                {
                    authoritativeMemory = NativeActorTypeName(skill);
                    owner = skill.owner as Hero;
                }
                if (current is Hero hero && owner == null) owner = hero;
            }
            if (found && owner == null) owner = AttributedOwner(identity.OwnerId);
            if (!found || owner == null || !Alive(owner) || owner.GetInstanceID() != identity.OwnerId) return false;
            EnsureMemoryAttributionEquipment(owner);
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

        internal void BeginAttributedDamagePacket(NativeAttributedDamagePacket.Packet packet, Actor actor, Entity target, ReactionChain chain, bool main, long basicOwner)
        {
            packet.Actor = actor; packet.Victim = target;
            packet.Serial = _memoryAttribution.NewPacketId(); packet.Chain = chain;
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
        }
        internal void EndAttributedDamagePacket(NativeAttributedDamagePacket.Packet packet)
        {
            if (packet == null) return;
            for (int i = 0; i <= (int)MemoryEventKind.OwnedBasicAttackHit; i++)
                if ((packet.Notifications & (1 << i)) != 0)
                    _memoryAttribution.ForgetNotification("native.publish", packet.Identity.Event((MemoryEventKind)i, packet.Serial, packet.NotificationVictim));
            packet.Actor = null; packet.Victim = null; packet.Serial = packet.NotificationVictim = 0;
            packet.Identity = default; packet.Chain = default; packet.Admitted = packet.MainBasic = false;
            packet.DamageAmount = packet.HpDamage = 0f; packet.Notifications = 0;
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



        internal void PublishAttributedFinalDamage(EventInfoDamage info)
        {
            ObserveBossGeneratedDamage(info);
            PublishBossNativeDamage(info);
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || !packet.Admitted || packet.Actor != info.actor || packet.Victim != info.victim
                || info.victim == null || info.damage.amount <= 0f) return;
            var hero = AttributedOwner(packet.Identity.OwnerId);
            if (hero == null || info.victim.GetRelation(hero) != EntityRelation.Enemy) return;
            packet.DamageAmount = info.damage.amount;
            long victim = AttributedVictimLifetime(info.victim);
            if (packet.MainBasic)
            {
                PublishMemoryActivation(packet.Identity.Event(MemoryEventKind.OwnedBasicAttackHit, packet.Serial, victim), hero, info.victim, info.damage.amount);
                OnIdentityStrikeBasicHit(hero, info.victim, packet.Identity.ActivationId, info.damage.HasAttr(DamageAttribute.IsCrit), victim);
            }
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
            if (!_attributionVictimLifetimes.TryGetValue(victim, out long id))
            {
                if (_attributionVictimLifetimes.Count >= 4096) return 0;
                _attributionVictimLifetimes[victim] = id = _memoryAttribution.NewPacketId();
            }
            return id;
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
                _mechanismEquipment.Remove(hero);
                _attributionMemoryIds.Remove(hero);
            }
        }

        private void ResetMemoryAttribution()
        {
            _memoryAttribution.Reset(); _attributionEquipment.Clear(); _mechanismEquipment.Clear();
            _attributionMemoryIds.Clear();
            _attributionVictimLifetimes.Clear(); _attributedNativeChains.Clear(); _deferredAttribution.Clear();
            ResetNativeEndingAdapters();
        }
    }
}
