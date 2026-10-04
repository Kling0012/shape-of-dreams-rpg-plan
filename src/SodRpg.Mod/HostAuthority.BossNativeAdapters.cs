using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // The base cast surrounds preparation for both hero movement skills and enemy native abilities.
    [HarmonyPatch(typeof(AbilityTrigger), nameof(AbilityTrigger.OnCastComplete))]
    internal static class BossNativeCastScope
    {
        internal static HostAuthority.BossNativeCast Current;
        private static void Prefix(AbilityTrigger __instance, CastInfo info, out HostAuthority.BossNativeCast __state)
        {
            __state = Current;
            Current = NetworkServer.active ? HostAuthority.NativeInstance?.BeginBossNativeCast(__instance, info) : null;
        }
        private static void Finalizer(HostAuthority.BossNativeCast __state) { Current = __state; }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.InvokeOnAbilityInstanceBeforePrepare))]
    internal static class BossNativeInstanceBinding
    {
        private static void Prefix(EventInfoAbilityInstance info)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BindBossNativeInstance(info);
        }
    }

    // A coroutine's source must be scoped per continuation, never retained across a yield.
    // Select only native Actor methods which actually request movement; binding below still
    // requires the owner's equipped movement skill and its live native cast/instance lifetime.
    [HarmonyPatch]
    internal static class BossNativeMovementSource
    {
        internal static Actor Current;
        private static readonly Dictionary<Type, FieldInfo> Sources = new Dictionary<Type, FieldInfo>();
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Actor), nameof(Actor.LogicUpdate));
            yield return AccessTools.DeclaredMethod(typeof(Actor), nameof(Actor.FrameUpdate));
            yield return AccessTools.DeclaredMethod(typeof(Actor), "InvokeOnCreateIfDidnt");
            var assemblies = new HashSet<Assembly> { typeof(Actor).Assembly, typeof(Se_U_Hysteria).Assembly };
            foreach (var assembly in assemblies)
                foreach (var type in assembly.GetTypes())
                {
                    bool actor = typeof(Actor).IsAssignableFrom(type);
                    FieldInfo source = null;
                    if (!actor)
                    {
                        source = AccessTools.DeclaredField(type, "<>4__this");
                        if (source == null || !typeof(Actor).IsAssignableFrom(source.FieldType)) continue;
                        Sources[type] = source;
                    }
                    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null
                            || method.DeclaringType == typeof(Actor) && (method.Name == nameof(Actor.LogicUpdate)
                                || method.Name == nameof(Actor.FrameUpdate) || method.Name == "InvokeOnCreateIfDidnt")) continue;
                        foreach (var instruction in PatchProcessor.ReadMethodBody(method))
                        {
                            if (instruction.Key != OpCodes.Call && instruction.Key != OpCodes.Callvirt) continue;
                            if (!(instruction.Value is MethodInfo called)) continue;
                            if (called.DeclaringType == typeof(EntityControl)
                                    && (called.Name == nameof(EntityControl.StartDisplacement) || called.Name == nameof(EntityControl.Teleport))
                                || called.DeclaringType == typeof(Dash)
                                    && (called.Name == nameof(Dash.ApplyByDirection) || called.Name == nameof(Dash.ApplyByDestination))
                                || called.DeclaringType == typeof(Actor) && called.Name == nameof(Actor.Teleport))
                            {
                                yield return method;
                                break;
                            }
                        }
                    }
                }
        }
        private static void Prefix(object __instance, out Actor __state)
        {
            __state = Current;
            Current = __instance as Actor;
            if (Current == null && Sources.TryGetValue(__instance.GetType(), out var field))
                Current = field.GetValue(__instance) as Actor;
        }
        private static void Finalizer(Actor __state) { Current = __state; }
    }

    [HarmonyPatch(typeof(EntityControl), nameof(EntityControl.StartDisplacement))]
    internal static class BossNativeDisplacementBinding
    {
        private static void Prefix(EntityControl __instance, Displacement disp)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BindBossNativeDisplacement(__instance.entity, disp);
        }
    }

    [HarmonyPatch(typeof(EntityControl), nameof(EntityControl.Teleport))]
    internal static class BossNativeTeleportCompletion
    {
        private static void Prefix(EntityControl __instance, out Vector3 __state) { __state = __instance.entity.position; }
        private static void Postfix(EntityControl __instance, Vector3 __state)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.PublishBossNativeTeleport(__instance.entity, __state);
        }
    }

    [HarmonyPatch(typeof(Se_U_Hysteria), "OnCreate")]
    internal static class BossHysteriaCreation
    {
        internal static Se_U_Hysteria Current;
        private static void Prefix(Se_U_Hysteria __instance, out Se_U_Hysteria __state)
        {
            __state = Current;
            Current = NetworkServer.active ? __instance : null;
            if (Current != null) HostAuthority.NativeInstance?.BeginBossHysteria(__instance);
        }
        private static void Postfix(Se_U_Hysteria __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.RefreshBossHysteria(__instance);
        }
        private static void Finalizer(Se_U_Hysteria __state) { Current = __state; }
    }

    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.DoSpeed))]
    internal static class BossHysteriaOwnSpeed
    {
        private static void Postfix(StatusEffect __instance, float strength, SpeedEffect __result)
        {
            if (NetworkServer.active && strength == -50f && __instance == BossHysteriaCreation.Current && __result != null)
                HostAuthority.NativeInstance?.CaptureBossHysteriaSpeed((Se_U_Hysteria)__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Ai_U_Hysteria_Claw), "OnCreate")]
    internal static class BossHysteriaClawCreation
    {
        private static void Prefix(Ai_U_Hysteria_Claw __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.BeginBossHysteriaClaw(__instance);
        }
    }

    // Identify DamageInstance.OnHit's actual dispatch, not its pre-damage OnHit/Others proc.
    [HarmonyPatch(typeof(DamageInstance), "OnHit")]
    internal static class BossHysteriaNativePacket
    {
        internal sealed class Scope
        {
            internal Ai_U_Hysteria_Claw Claw;
            internal Entity Victim;
            internal long Packet;
            internal bool Claimed;
        }
        internal static Scope Current;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var dispatch = AccessTools.Method(typeof(DamageData), nameof(DamageData.Dispatch));
            var replacement = AccessTools.Method(typeof(BossHysteriaNativePacket), nameof(Dispatch));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(dispatch)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Hysteria adapter expected one native OnHit damage dispatch.");
        }
        private static void Dispatch(ref DamageData damage, Entity victim, ReactionChain chain)
        {
            var previous = Current;
            Current = damage.actor is Ai_U_Hysteria_Claw claw
                && HostAuthority.NativeInstance?.IsTrackedBossHysteriaClaw(claw, ref damage) == true
                ? new Scope { Claw = claw, Victim = victim } : null;
            try { damage.Dispatch(victim, chain); }
            finally { Current = previous; }
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.DealDamage))]
    internal static class BossHysteriaNativePacketClaim
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Actor __instance, Entity target)
        {
            var scope = BossHysteriaNativePacket.Current;
            if (scope == null || scope.Claimed) return;
            scope.Claimed = true;
            var packet = NativeAttributedDamagePacket.Current;
            if (__instance == scope.Claw && target == scope.Victim && packet != null
                && packet.Actor == __instance && packet.Victim == target) scope.Packet = packet.Serial;
        }
    }

    [HarmonyPatch(typeof(Actor), "InvokeOnDestroyActorIfDidnt")]
    internal static class BossNativeInstanceCompletion
    {
        private static void Prefix(Actor __instance)
        {
            if (NetworkServer.active) HostAuthority.NativeInstance?.CompleteBossNativeActor(__instance);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors))]
    internal static class BossNativePooledLifetime
    {
        private static void Prefix(Actor __instance) { HostAuthority.NativeInstance?.ClearBossNativeActor(__instance); }
    }

    internal sealed partial class HostAuthority
    {
        internal sealed class BossNativeCast
        {
            internal AbilityTrigger Trigger;
            internal Entity Owner;
            internal long Activation, TriggerLife, EquipmentEpoch;
            internal Actor NativeRoot;
            internal long NativeRootLife;
            internal Room Room;
            internal string Run;
            internal bool Movement;
        }
        private sealed class BossNativeSource
        {
            internal AbilityInstance Instance;
            internal Actor Parent;
            internal long ParentLife, Life;
            internal BossNativeCast Cast;
        }
        private sealed class BossNativeSeen
        {
            private readonly long[] _ids = new long[64];
            private int _next;
            internal bool Add(long id)
            {
                if (id <= 0) return false;
                for (int i = 0; i < _ids.Length; i++) if (_ids[i] == id) return false;
                _ids[_next] = id;
                _next = (_next + 1) % _ids.Length;
                return true;
            }
        }
        private sealed class BossNativeOwner
        {
            internal readonly BossNativeSeen MainHits = new BossNativeSeen();
            internal readonly BossNativeSeen MemoryUses = new BossNativeSeen();
            internal readonly BossNativeSeen DamageOut = new BossNativeSeen();
            internal readonly BossNativeSeen DamageIn = new BossNativeSeen();
            internal readonly BossNativeSeen Movement = new BossNativeSeen();
        }
        private sealed class BossNativeDisplacement
        {
            internal HeroRuntime Runtime;
            internal BossNativeSource Source;
            internal BossNativeCast Cast;
            internal Vector3 Start;
            internal Action Finished, Canceled;
        }
        private sealed class BossHysteriaState
        {
            internal Se_U_Hysteria State;
            internal HeroRuntime Runtime;
            internal St_U_Hysteria Skill;
            internal Actor Parent;
            internal long StateActorLife, HeroLife, ParentLife, SkillLife, Life, EquipmentEpoch;
            internal Room Room;
            internal string Run;
            internal SpeedEffect Speed;
            internal float SpeedStrength = -50f;
        }
        private sealed class BossNativeClaw
        {
            internal Ai_U_Hysteria_Claw Claw;
            internal BossHysteriaState State;
            internal long StateLife, Life, ActorLife;
            internal bool Right, Hit;
        }
        // Capture-only, bounded and allocation-free even when Hysteria starts before any Boss gear.
        private struct BossHysteriaSpeedCapture
        {
            internal Se_U_Hysteria State;
            internal Hero Hero;
            internal Actor Parent;
            internal St_U_Hysteria Skill;
            internal SpeedEffect Speed;
            internal float Strength;
            internal Room Room;
            internal string Run;
        }
        private readonly BossHysteriaSpeedCapture[] _bossHysteriaSpeedCaptures = new BossHysteriaSpeedCapture[64];

        private readonly Dictionary<Actor, long> _bossNativeActorLives = new Dictionary<Actor, long>();
        private readonly Dictionary<Actor, BossNativeSource> _bossNativeSources = new Dictionary<Actor, BossNativeSource>();
        private readonly Dictionary<HeroRuntime, BossNativeOwner> _bossNativeOwners = new Dictionary<HeroRuntime, BossNativeOwner>();
        private readonly Dictionary<Displacement, BossNativeDisplacement> _bossNativeDisplacements = new Dictionary<Displacement, BossNativeDisplacement>();
        private readonly Dictionary<Se_U_Hysteria, BossHysteriaState> _bossHysteriaStates = new Dictionary<Se_U_Hysteria, BossHysteriaState>();
        private readonly Dictionary<Ai_U_Hysteria_Claw, BossNativeClaw> _bossHysteriaClaws = new Dictionary<Ai_U_Hysteria_Claw, BossNativeClaw>();
        private readonly List<Actor> _bossNativeActorScratch = new List<Actor>();
        private readonly List<Displacement> _bossNativeDisplacementScratch = new List<Displacement>();
        private readonly List<Se_U_Hysteria> _bossHysteriaScratch = new List<Se_U_Hysteria>();

        private long BossNativeActorLife(Actor actor)
        {
            if (!_bossNativeActorLives.TryGetValue(actor, out long life))
                _bossNativeActorLives.Add(actor, life = _memoryAttribution.NewPacketId());
            return life;
        }
        private bool BossNativeSameLife(Actor actor, long life) => actor != null
            && _bossNativeActorLives.TryGetValue(actor, out long current) && current == life;
        private BossNativeOwner BossNativeOwnerState(HeroRuntime rt)
        {
            if (!_bossNativeOwners.TryGetValue(rt, out var state))
                _bossNativeOwners.Add(rt, state = new BossNativeOwner());
            return state;
        }
        private bool BossNativeRuntime(Hero hero, out HeroRuntime rt)
        {
            rt = null;
            return NetworkServer.active && Alive(hero) && _runtimes.TryGetValue(hero, out rt) && BossNativeHasProfiles(rt);
        }
        private static bool BossNativeHasProfiles(HeroRuntime rt) => rt.AppliedBuild != null
            && (rt.AppliedBuild.Build.BossMoves.Count != 0 || rt.AppliedBuild.Build.BossRewards.Count != 0);
        private bool BossNativeAnyProfiles()
        {
            foreach (var rt in _runtimes.Values) if (BossNativeHasProfiles(rt) && Alive(rt.Hero)) return true;
            return false;
        }
        private bool BossNativeCastCurrent(BossNativeCast cast)
        {
            if (cast == null || cast.Owner.IsNullInactiveDeadOrKnockedOut()
                || !BossNativeContextCurrent(cast.Room, cast.Run)) return false;
            if (cast.Trigger == null)
                return !(cast.Owner is Hero) && cast.NativeRoot != null && cast.NativeRoot.isActive
                    && BossNativeSameLife(cast.NativeRoot, cast.NativeRootLife);
            if (!cast.Trigger.isActive || cast.Trigger.owner != cast.Owner
                || !BossNativeSameLife(cast.Trigger, cast.TriggerLife)) return false;
            if (!(cast.Owner is Hero hero)) return true;
            return BossNativeRuntime(hero, out var rt) && rt.ShieldEquipmentEpoch == cast.EquipmentEpoch
                && cast.Trigger is SkillTrigger skill && BossNativeEquippedSkill(hero, skill);
        }
        private static bool BossNativeContextCurrent(Room room, string run) =>
            NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom == room
            && NetworkedManagerBase<GameManager>.softInstance?.runId == run
            && NetworkedManagerBase<ZoneManager>.softInstance?.isInAnyTransition != true;
        private static bool BossNativeEquippedSkill(Hero hero, SkillTrigger skill)
        {
            if (skill == null || skill is St_U_Hysteria || skill.owner != hero) return false;
            foreach (var location in LinkSkills) if (hero.Skill.GetSkill(location) == skill) return true;
            return false;
        }
        internal BossNativeCast BeginBossNativeCast(AbilityTrigger trigger, CastInfo info)
        {
            if (trigger == null || info.caster != trigger.owner || trigger.owner.IsNullInactiveDeadOrKnockedOut()
                || AttributionGeneratedOrigin() != GeneratedOrigin.None) return null;
            bool movement = trigger is SkillTrigger skill && skill.owner != null && BossNativeEquippedSkill(skill.owner, skill);
            long epoch = 0;
            if (trigger.owner is Hero hero)
            {
                if (!movement || !BossNativeRuntime(hero, out var rt)) return null;
                if (!BossEnsure(rt)) return null;
                epoch = rt.ShieldEquipmentEpoch;
            }
            else if (!BossNativeAnyProfiles()) return null;
            var memoryCast = NativeAttributedMemoryCast.Current;
            long activation = memoryCast != null && memoryCast.Skill == trigger && _memoryAttribution.IsCurrent(memoryCast.Identity)
                ? memoryCast.Identity.ActivationId : _memoryAttribution.NewPacketId();
            return new BossNativeCast { Trigger = trigger, Owner = trigger.owner, Movement = movement,
                EquipmentEpoch = epoch, TriggerLife = BossNativeActorLife(trigger), Activation = activation,
                Room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,
                Run = NetworkedManagerBase<GameManager>.softInstance?.runId };
        }
        internal void BindBossNativeInstance(EventInfoAbilityInstance info)
        {
            if (info.instance == null || info.actor == null || info.instance.gem != null
                || AttributionGeneratedOrigin() != GeneratedOrigin.None) return;
            BossNativeCast cast = null;
            var scope = BossNativeCastScope.Current;
            if (scope != null && info.actor == scope.Trigger) cast = scope;
            else if (_bossNativeSources.TryGetValue(info.actor, out var parent) && BossNativeSourceCurrent(parent, true)) cast = parent.Cast;
            else if (!(info.instance.info.caster is Hero) && !info.instance.info.caster.IsNullInactiveDeadOrKnockedOut()
                && BossNativeAnyProfiles())
            {
                // Native enemy emissions outside a trigger cast still have one activation per
                // spawned instance lifetime. Its descendants share that activation, never packet ids.
                for (var ancestor = info.actor; ancestor != null; ancestor = ancestor.parentActor)
                    if (ancestor is Gem || ancestor is ElementalStatusEffect
                        || ancestor is AbilityInstance ability && ability.gem != null) return;
                cast = new BossNativeCast { Owner = info.instance.info.caster, NativeRoot = info.actor,
                    NativeRootLife = BossNativeActorLife(info.actor), Activation = _memoryAttribution.NewPacketId(),
                    Room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,
                    Run = NetworkedManagerBase<GameManager>.softInstance?.runId };
            }
            if (cast == null || info.instance.info.caster != cast.Owner || !BossNativeCastCurrent(cast)) return;
            _bossNativeSources[info.instance] = new BossNativeSource { Instance = info.instance, Parent = info.actor,
                ParentLife = BossNativeActorLife(info.actor), Life = BossNativeActorLife(info.instance), Cast = cast };
        }
        private bool BossNativeSourceCurrent(BossNativeSource source, bool active)
        {
            return source != null && source.Instance != null && (!active || source.Instance.isActive)
                && source.Instance.info.caster == source.Cast.Owner && source.Instance.gem == null
                && source.Instance.parentActor == source.Parent && BossNativeSameLife(source.Instance, source.Life)
                && BossNativeSameLife(source.Parent, source.ParentLife) && BossNativeCastCurrent(source.Cast);
        }
        private BossNativeSource BossNativeMovementBinding(Hero hero)
        {
            var actor = TeleportInitiator.Current ?? BossNativeMovementSource.Current;
            if (actor == null || actor is Ai_U_Hysteria_Claw || actor is Se_U_Hysteria) return null;
            return _bossNativeSources.TryGetValue(actor, out var source) && source.Cast.Movement
                && source.Cast.Owner == hero && BossNativeSourceCurrent(source, true) ? source : null;
        }
        internal void BindBossNativeDisplacement(Entity entity, Displacement displacement)
        {
            if (!(entity is Hero hero) || displacement == null || displacement.hasStarted || !displacement.isFriendly
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || !BossNativeRuntime(hero, out var rt)) return;
            var source = BossNativeMovementBinding(hero);
            var cast = source?.Cast;
            if (cast == null)
            {
                var current = BossNativeCastScope.Current;
                if (current != null && current.Movement && current.Owner == hero && BossNativeCastCurrent(current)
                    && (BossNativeMovementSource.Current == null || BossNativeMovementSource.Current == current.Trigger)) cast = current;
            }
            if (cast == null || _bossNativeDisplacements.ContainsKey(displacement)) return;
            var binding = new BossNativeDisplacement { Runtime = rt, Source = source, Cast = cast, Start = hero.position };
            binding.Finished = () => FinishBossNativeDisplacement(displacement, binding);
            binding.Canceled = () => RemoveBossNativeDisplacement(displacement, binding);
            _bossNativeDisplacements.Add(displacement, binding);
            displacement.onFinish += binding.Finished;
            displacement.onCancel += binding.Canceled;
        }
        private void RemoveBossNativeDisplacement(Displacement displacement, BossNativeDisplacement binding)
        {
            displacement.onFinish -= binding.Finished;
            displacement.onCancel -= binding.Canceled;
            _bossNativeDisplacements.Remove(displacement);
        }
        private void FinishBossNativeDisplacement(Displacement displacement, BossNativeDisplacement binding)
        {
            if (!_bossNativeDisplacements.TryGetValue(displacement, out var current) || current != binding) return;
            RemoveBossNativeDisplacement(displacement, binding);
            if (!BossNativeCastCurrent(binding.Cast) || binding.Source != null && !BossNativeSourceCurrent(binding.Source, false)
                || !BossNativeRuntime(binding.Runtime.Hero, out var rt) || rt != binding.Runtime
                || (rt.Hero.position - binding.Start).sqrMagnitude < .0001f) return;
            PublishBossMovement(rt, binding.Cast.Activation, rt.Hero.position);
        }
        internal void PublishBossNativeTeleport(Entity entity, Vector3 from)
        {
            if (!(entity is Hero hero) || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || !BossNativeRuntime(hero, out var rt) || (hero.position - from).sqrMagnitude < .0001f) return;
            // DispByTarget's finish/cancel correction is not an independent teleport activation.
            if (hero.Control.ongoingDisplacement != null) return;
            var source = BossNativeMovementBinding(hero);
            if (source != null) PublishBossMovement(rt, source.Cast.Activation, hero.position);
            else
            {
                var cast = BossNativeCastScope.Current;
                var actor = TeleportInitiator.Current ?? BossNativeMovementSource.Current;
                if (cast != null && cast.Movement && cast.Owner == hero && actor == cast.Trigger && BossNativeCastCurrent(cast))
                    PublishBossMovement(rt, cast.Activation, hero.position);
            }
        }
        private void PublishBossMovement(HeroRuntime rt, long activation, Vector3 destination)
        {
            if (BossNativeOwnerState(rt).Movement.Add(activation)) BossMovementCompleted(rt, activation, destination);
        }

        // Called from the existing NativeMemoryCasts confirmation, not from ability/claw generation.
        internal void PublishBossConfirmedMemoryUse(NativeAttributedMemoryCast.Cast cast)
        {
            if (cast == null || !(cast.Skill.owner is Hero hero) || !_memoryAttribution.IsCurrent(cast.Identity)
                || !BossNativeRuntime(hero, out var rt) || AttributionGeneratedOrigin() != GeneratedOrigin.None || !BossEnsure(rt)) return;
            if (BossNativeOwnerState(rt).MemoryUses.Add(cast.Identity.ActivationId))
                BossConfirmedMemoryUse(rt, cast.Identity.ActivationId, cast.Skill);
        }

        // Called before PublishAttributedFinalDamage's hero-only admission return; incoming enemy
        // activations have their own native cast lifetime but deliberately never become memory procs.
        internal void PublishBossNativeDamage(EventInfoDamage info)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (packet == null || packet.Actor != info.actor || packet.Victim != info.victim || info.actor == null
                || info.victim == null || info.damage.amount <= 0f || AttributionGeneratedOrigin() != GeneratedOrigin.None
                || !info.chain.Equals(packet.Chain)) return;
            if (!BossNativeAnyProfiles()) return;
            if (info.actor is Ai_U_Hysteria_Claw claw) PublishBossHysteriaHit(claw, info);
            if (packet.Admitted && _memoryAttribution.IsCurrent(packet.Identity)
                && packet.Identity.GeneratedOrigin == GeneratedOrigin.None
                && packet.Identity.NativePayloadKind != NativePayloadKind.SummonAttack
                && AttributedOwner(packet.Identity.OwnerId) is Hero owner && info.victim.GetRelation(owner) == EntityRelation.Enemy
                && BossNativeRuntime(owner, out var outgoing) && BossEnsure(outgoing))
            {
                var state = BossNativeOwnerState(outgoing);
                if (packet.MainBasic && state.MainHits.Add(packet.Identity.ActivationId))
                    BossNativeMainHit(outgoing, packet.Identity.ActivationId, info.victim);
                if (state.DamageOut.Add(packet.Identity.ActivationId))
                    BossNativeDamage(outgoing, packet.Identity.ActivationId, info.victim, false);
            }
            else if (info.chain.Equals(default(ReactionChain))
                && _bossNativeSources.TryGetValue(info.actor, out var movement) && movement.Cast.Movement
                && BossNativeSourceCurrent(movement, true) && movement.Cast.Owner is Hero mover
                && info.victim.GetRelation(mover) == EntityRelation.Enemy && BossNativeRuntime(mover, out var moved)
                && BossNativeOwnerState(moved).DamageOut.Add(movement.Cast.Activation))
                BossNativeDamage(moved, movement.Cast.Activation, info.victim, false);
            if (!(info.victim is Hero victim) || !BossNativeRuntime(victim, out var incoming)
                || !info.chain.Equals(default(ReactionChain)) || !BossEnsure(incoming)) return;
            long activation;
            Entity attacker;
            if (_bossNativeSources.TryGetValue(info.actor, out var native) && BossNativeSourceCurrent(native, true))
            {
                activation = native.Cast.Activation;
                attacker = native.Cast.Owner;
            }
            else if (BossNativeCastScope.Current is BossNativeCast cast && cast.Trigger == info.actor && BossNativeCastCurrent(cast))
            {
                activation = cast.Activation;
                attacker = cast.Owner;
            }
            else return;
            if (attacker == null || attacker.GetRelation(victim) != EntityRelation.Enemy) return;
            if (BossNativeOwnerState(incoming).DamageIn.Add(activation)) BossNativeDamage(incoming, activation, attacker, true);
        }

        internal void BeginBossHysteria(Se_U_Hysteria state)
        {
            if (!(state.info.caster is Hero hero) || state.victim != hero || !BossNativeRuntime(hero, out var rt)
                || AttributionGeneratedOrigin() != GeneratedOrigin.None || state.gem != null
                || !TryGetMemoryActivation(state, out var identity) || identity.SourceMemory != nameof(St_U_Hysteria)
                || !BossEnsure(rt)) return;
            var skill = state.FindFirstAncestorOfType<SkillTrigger>() as St_U_Hysteria;
            if (skill == null || skill.owner != hero || FindMemory(hero, nameof(St_U_Hysteria)) != skill) return;
            if (_bossHysteriaStates.ContainsKey(state)) ClearBossNativeActor(state);
            _bossHysteriaStates.Add(state, new BossHysteriaState { State = state, Runtime = rt, Skill = skill,
                Parent = state.parentActor, ParentLife = BossNativeActorLife(state.parentActor),
                StateActorLife = BossNativeActorLife(state), HeroLife = BossNativeActorLife(hero), SkillLife = BossNativeActorLife(skill),
                Life = _memoryAttribution.NewPacketId(), EquipmentEpoch = rt.ShieldEquipmentEpoch,
                Room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,
                Run = NetworkedManagerBase<GameManager>.softInstance?.runId });
        }
        internal void CaptureBossHysteriaSpeed(Se_U_Hysteria state, SpeedEffect speed)
        {
            if (!(state.info.caster is Hero hero) || state.victim != hero || speed.parent != state || speed.victim != hero) return;
            St_U_Hysteria skill = null;
            int depth = 0;
            for (var actor = state.parentActor; actor != null && depth++ < 32; actor = actor.parentActor)
            {
                if (actor is Gem || actor is ElementalStatusEffect || actor is AbilityInstance ability && ability.gem != null) return;
                if (actor is SkillTrigger trigger) { skill = trigger as St_U_Hysteria; break; }
            }
            if (skill == null || skill.owner != hero || FindMemory(hero, nameof(St_U_Hysteria)) != skill) return;
            int slot = -1;
            for (int i = 0; i < _bossHysteriaSpeedCaptures.Length; i++)
            {
                if (_bossHysteriaSpeedCaptures[i].State == state) { slot = i; break; }
                if (slot < 0 && (_bossHysteriaSpeedCaptures[i].State == null || !_bossHysteriaSpeedCaptures[i].State.isActive)) slot = i;
            }
            if (slot >= 0)
                _bossHysteriaSpeedCaptures[slot] = new BossHysteriaSpeedCapture { State = state, Hero = hero, Skill = skill,
                    Parent = state.parentActor, Speed = speed, Strength = -50f,
                    Room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom,
                    Run = NetworkedManagerBase<GameManager>.softInstance?.runId };
            if (_bossHysteriaStates.TryGetValue(state, out var entry) && entry.Speed == null) entry.Speed = speed;
        }
        private void ClearBossHysteriaCapture(Actor actor)
        {
            for (int i = 0; i < _bossHysteriaSpeedCaptures.Length; i++)
            {
                ref var capture = ref _bossHysteriaSpeedCaptures[i];
                if (capture.State == actor || capture.Hero == actor || capture.Parent == actor || capture.Skill == actor)
                    capture = default;
            }
        }
        private void DiscoverBossHysteria(HeroRuntime rt)
        {
            if (!BossNativeHasProfiles(rt) || !rt.Hero.Status.TryGetStatusEffect<Se_U_Hysteria>(out var state)
                || _bossHysteriaStates.ContainsKey(state)) return;
            for (int i = 0; i < _bossHysteriaSpeedCaptures.Length; i++)
            {
                ref var capture = ref _bossHysteriaSpeedCaptures[i];
                if (capture.State != state || capture.Hero != rt.Hero || capture.Parent != state.parentActor
                    || capture.Skill == null || capture.Skill.owner != rt.Hero || capture.Speed == null
                    || capture.Speed.parent != state || capture.Speed.victim != rt.Hero
                    || !BossNativeContextCurrent(capture.Room, capture.Run)) continue;
                bool own = false;
                foreach (var effect in state.basicEffects) if (ReferenceEquals(effect, capture.Speed)) { own = true; break; }
                if (!own) continue;
                BeginBossHysteria(state);
                if (_bossHysteriaStates.TryGetValue(state, out var attached))
                {
                    attached.Speed = capture.Speed;
                    attached.SpeedStrength = capture.Strength;
                }
                return;
            }
        }
        private bool BossHysteriaSpeedCurrent(BossHysteriaState state) =>
            state.State != null && state.State.isActive && state.State.info.caster == state.Runtime.Hero
            && state.State.victim == state.Runtime.Hero && BossNativeSameLife(state.State, state.StateActorLife)
            && BossNativeSameLife(state.Runtime.Hero, state.HeroLife);
        private bool BossHysteriaNativeCurrent(BossHysteriaState state)
        {
            return state.State != null && state.State.isActive && state.State.parentActor == state.Parent
                && state.State.info.caster == state.Runtime.Hero && state.State.victim == state.Runtime.Hero
                && BossNativeSameLife(state.State, state.StateActorLife) && BossNativeSameLife(state.Skill, state.SkillLife)
                && BossNativeSameLife(state.Parent, state.ParentLife)
                && state.Skill.owner == state.Runtime.Hero && state.State.FindFirstAncestorOfType<SkillTrigger>() == state.Skill;
        }
        private bool BossHysteriaEligible(BossHysteriaState state)
        {
            return BossHysteriaNativeCurrent(state) && BossNativeRuntime(state.Runtime.Hero, out var rt) && rt == state.Runtime
                && rt.ShieldEquipmentEpoch == state.EquipmentEpoch && FindMemory(rt.Hero, nameof(St_U_Hysteria)) == state.Skill
                && BossNativeContextCurrent(state.Room, state.Run)
                && BossRewardStage(rt, BossProfiles.DemonRewardId) > 0;
        }
        private void SetBossHysteriaSpeed(BossHysteriaState state, float strength)
        {
            if (state.Speed == null || state.SpeedStrength == strength || !BossHysteriaSpeedCurrent(state)) return;
            bool own = false;
            foreach (var effect in state.State.basicEffects) if (ReferenceEquals(effect, state.Speed)) { own = true; break; }
            if (!own || state.Speed.parent != state.State || state.Speed.victim != state.Runtime.Hero) return;
            state.State.StopBasicEffect(state.Speed);
            state.Speed = state.State.DoSpeed(strength);
            state.SpeedStrength = strength;
            for (int i = 0; i < _bossHysteriaSpeedCaptures.Length; i++)
                if (_bossHysteriaSpeedCaptures[i].State == state.State)
                {
                    _bossHysteriaSpeedCaptures[i].Speed = state.Speed;
                    _bossHysteriaSpeedCaptures[i].Strength = strength;
                    break;
                }
        }
        internal void RefreshBossHysteria(Se_U_Hysteria state)
        {
            if (_bossHysteriaStates.TryGetValue(state, out var entry))
                SetBossHysteriaSpeed(entry, BossHysteriaEligible(entry) && BossRewardStage(entry.Runtime, BossProfiles.DemonRewardId) == 3 ? -25f : -50f);
        }
        internal void BeginBossHysteriaClaw(Ai_U_Hysteria_Claw claw)
        {
            if (!(claw.parentActor is Se_U_Hysteria parent) || !_bossHysteriaStates.TryGetValue(parent, out var state)
                || !BossHysteriaEligible(state) || claw.info.caster != state.Runtime.Hero || claw.gem != null
                || AttributionGeneratedOrigin() != GeneratedOrigin.None) return;
            var entry = new BossNativeClaw { Claw = claw, State = state, StateLife = state.Life,
                Life = _memoryAttribution.NewPacketId(), ActorLife = BossNativeActorLife(claw), Right = claw.isRight };
            _bossHysteriaClaws[claw] = entry;
            BossHysteriaClaw(state.Runtime, state.Life, entry.Life, entry.Right, false, claw.position, false);
        }
        private bool BossHysteriaClawCurrent(BossNativeClaw claw)
        {
            return BossHysteriaEligible(claw.State) && claw.StateLife == claw.State.Life
                && BossNativeSameLife(claw.Claw, claw.ActorLife) && claw.Claw.parentActor == claw.State.State
                && claw.Claw.info.caster == claw.State.Runtime.Hero && claw.Claw.gem == null;
        }
        private void PublishBossHysteriaHit(Ai_U_Hysteria_Claw claw, EventInfoDamage info)
        {
            var packet = NativeAttributedDamagePacket.Current;
            var dispatch = BossHysteriaNativePacket.Current;
            if (!_bossHysteriaClaws.TryGetValue(claw, out var entry) || entry.Hit || !claw.isActive
                || !BossHysteriaClawCurrent(entry) || !info.chain.Equals(default(ReactionChain))
                || packet == null || dispatch == null || dispatch.Claw != claw || dispatch.Victim != info.victim
                || dispatch.Packet != packet.Serial
                || info.victim.GetRelation(entry.State.Runtime.Hero) != EntityRelation.Enemy) return;
            entry.Hit = true;
            BossHysteriaClaw(entry.State.Runtime, entry.StateLife, entry.Life, entry.Right, true, info.victim.position, false);
        }
        internal bool IsTrackedBossHysteriaClaw(Ai_U_Hysteria_Claw claw, ref DamageData damage) =>
            _bossHysteriaClaws.ContainsKey(claw) && AttributionGeneratedOrigin() == GeneratedOrigin.None
            && !damage.IsAmountModifiedBy(typeof(BossCombatState)) && !damage.IsAmountModifiedBy(typeof(GimmickRuntime));
        internal void CompleteBossNativeActor(Actor actor)
        {
            if (actor is Se_U_Hysteria) ClearBossHysteriaCapture(actor);
            if (actor is Ai_U_Hysteria_Claw claw && _bossHysteriaClaws.TryGetValue(claw, out var entry))
            {
                _bossHysteriaClaws.Remove(claw);
                if (BossHysteriaClawCurrent(entry))
                    BossHysteriaClaw(entry.State.Runtime, entry.StateLife, entry.Life, entry.Right, false, claw.position, true);
            }
            if (actor is Se_U_Hysteria state && _bossHysteriaStates.TryGetValue(state, out var ended))
            {
                BossHysteriaStateEnded(ended.Runtime, ended.Life);
                _bossHysteriaStates.Remove(state);
            }
        }
        internal void ClearBossNativeActor(Actor actor)
        {
            if (ReferenceEquals(actor, null)) return;
            ClearBossHysteriaCapture(actor);
            _bossNativeSources.Remove(actor);
            _bossNativeActorLives.Remove(actor);
            if (actor is Ai_U_Hysteria_Claw claw) _bossHysteriaClaws.Remove(claw);
            if (actor is Se_U_Hysteria state && _bossHysteriaStates.TryGetValue(state, out var ended))
            {
                BossHysteriaStateEnded(ended.Runtime, ended.Life);
                _bossHysteriaStates.Remove(state);
            }
            if (actor is Hero hero && _runtimes.TryGetValue(hero, out var rt)) ClearBossNativeAdapters(rt);
        }
        private void TickBossNativeAdapters(HeroRuntime rt)
        {
            DiscoverBossHysteria(rt);
            _bossHysteriaScratch.Clear();
            foreach (var pair in _bossHysteriaStates) if (pair.Value.Runtime == rt) _bossHysteriaScratch.Add(pair.Key);
            foreach (var key in _bossHysteriaScratch)
            {
                var state = _bossHysteriaStates[key];
                if (!BossHysteriaNativeCurrent(state))
                {
                    SetBossHysteriaSpeed(state, -50f);
                    BossHysteriaStateEnded(state.Runtime, state.Life);
                    _bossHysteriaStates.Remove(key);
                    continue;
                }
                if (state.EquipmentEpoch != rt.ShieldEquipmentEpoch)
                {
                    SetBossHysteriaSpeed(state, -50f);
                    BossHysteriaStateEnded(state.Runtime, state.Life);
                    state.Life = _memoryAttribution.NewPacketId();
                    state.EquipmentEpoch = rt.ShieldEquipmentEpoch;
                }
                RefreshBossHysteria(key);
            }
            _bossNativeDisplacementScratch.Clear();
            foreach (var pair in _bossNativeDisplacements)
                if (pair.Value.Runtime == rt && (!pair.Key.isAlive && pair.Key.hasStarted || !BossNativeCastCurrent(pair.Value.Cast)))
                    _bossNativeDisplacementScratch.Add(pair.Key);
            foreach (var key in _bossNativeDisplacementScratch) RemoveBossNativeDisplacement(key, _bossNativeDisplacements[key]);
        }
        private void ClearBossNativeAdapters(HeroRuntime rt)
        {
            _bossNativeOwners.Remove(rt);
            _bossNativeDisplacementScratch.Clear();
            foreach (var pair in _bossNativeDisplacements) if (pair.Value.Runtime == rt) _bossNativeDisplacementScratch.Add(pair.Key);
            foreach (var key in _bossNativeDisplacementScratch) RemoveBossNativeDisplacement(key, _bossNativeDisplacements[key]);
            _bossNativeActorScratch.Clear();
            foreach (var pair in _bossNativeSources) if (pair.Value.Cast.Owner == rt.Hero) _bossNativeActorScratch.Add(pair.Key);
            foreach (var key in _bossNativeActorScratch) _bossNativeSources.Remove(key);
            foreach (var state in _bossHysteriaStates.Values)
                if (state.Runtime == rt)
                {
                    SetBossHysteriaSpeed(state, -50f);
                    BossHysteriaStateEnded(state.Runtime, state.Life);
                    // Invalidate already-created claws without ending or restarting the native state.
                    state.Life = _memoryAttribution.NewPacketId();
                    state.EquipmentEpoch = rt.ShieldEquipmentEpoch;
                }
            _bossNativeActorScratch.Clear();
            foreach (var pair in _bossHysteriaClaws) if (pair.Value.State.Runtime == rt) _bossNativeActorScratch.Add(pair.Key);
            foreach (var key in _bossNativeActorScratch) _bossHysteriaClaws.Remove((Ai_U_Hysteria_Claw)key);
        }
    }
}
