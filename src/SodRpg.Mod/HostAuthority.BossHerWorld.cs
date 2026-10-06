using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    [HarmonyPatch(typeof(Actor),nameof(Actor.InvokeOnAbilityInstanceBeforePrepare))]
    internal static class NyxWorldBeforePrepare
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Actor __instance,EventInfoAbilityInstance info)
        {
            if(NetworkServer.active && __instance==info.actor) HostAuthority.NativeInstance?.PrepareNyxWorld(info);
        }
    }
    [HarmonyPatch(typeof(Se_U_HerWorld_Blackhole),"OnCreate")]
    internal static class NyxWorldCreation
    {
        private static void Prefix(Se_U_HerWorld_Blackhole __instance)
        {
            if(NetworkServer.active) HostAuthority.NativeInstance?.CreateNyxWorld(__instance);
        }
    }
    [HarmonyPatch(typeof(Se_U_HerWorld_Blackhole),"<OnCreate>b__17_0")]
    internal static class NyxWorldDeathInterrupt
    {
        private static void Prefix(Se_U_HerWorld_Blackhole __instance)
        {
            if(NetworkServer.active) HostAuthority.NativeInstance?.InterruptNyxWorld(__instance);
        }
    }
    // Managed 1.4.0.13 FrameUpdate: IL_0062 forced Destroy, IL_00e8 StopTimer,
    // IL_00ee natural Destroy. StopTimer has already nulled remainingDuration.
    [HarmonyPatch(typeof(StatusEffect),nameof(StatusEffect.FrameUpdate))]
    internal static class NyxWorldNaturalExpiration
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var destroy=AccessTools.Method(typeof(Actor),nameof(Actor.Destroy));
            var stop=AccessTools.Method(typeof(StatusEffect),nameof(StatusEffect.StopTimer));
            var replacement=AccessTools.Method(typeof(NyxWorldNaturalExpiration),nameof(NaturalDestroy));
            int stops=0,destroys=0,natural=0; bool afterStop=false;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(stop)) { stops++; afterStop=true; }
                if(instruction.Calls(destroy))
                {
                    destroys++;
                    if(afterStop) { instruction.opcode=OpCodes.Call; instruction.operand=replacement; natural++; }
                    afterStop=false;
                }
                yield return instruction;
            }
            if(stops!=1 || destroys!=2 || natural!=1) throw new InvalidOperationException("HerWorld adapter expected the exact native timer-expiry Destroy site.");
        }
        private static void NaturalDestroy(Actor actor)
        {
            if(NetworkServer.active && actor is Se_U_HerWorld_Blackhole status) HostAuthority.NativeInstance?.ExpireNyxWorld(status);
            actor.Destroy();
        }
    }
    [HarmonyPatch(typeof(Se_U_HerWorld_Blackhole),"ActiveLogicUpdate")]
    internal static class NyxWorldNativeTick
    {
        internal struct Scope
        {
            internal Se_U_HerWorld_Blackhole Status;
            internal Entity Victim;
            internal long Packet;
            internal bool Claimed;
        }
        internal static Scope Current;
        private static bool Prepare()
        {
            var method=AccessTools.DeclaredMethod(typeof(Se_U_HerWorld_Blackhole),"ActiveLogicUpdate");
            var body=method?.GetMethodBody();
            // NativeMove consumes ldloc.1 as an Entity; this is required by this
            // adapter, independently of the advisory feature-wide preflight.
            if(body==null || body.LocalVariables.Count<=1 || !typeof(Entity).IsAssignableFrom(body.LocalVariables[1].LocalType))
                throw new InvalidOperationException("HerWorld native attraction target local contract changed.");
            return true;
        }
        private static void Prefix(Se_U_HerWorld_Blackhole __instance)
        {
            if(NetworkServer.active) HostAuthority.NativeInstance?.RefreshNyxWorld(__instance);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var move=AccessTools.Method(typeof(EntityControl),nameof(EntityControl.SetAgentPosition),new[] { typeof(Vector3) });
            var dispatch=AccessTools.Method(typeof(DamageData),nameof(DamageData.Dispatch));
            var moveReplacement=AccessTools.Method(typeof(NyxWorldNativeTick),nameof(NativeMove));
            var damageReplacement=AccessTools.Method(typeof(NyxWorldNativeTick),nameof(NativeDamage));
            int moves=0,damages=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(move))
                {
                    // Native IL_013b stack is Control, native destination; loc1 is this enemy.
                    var load=new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    instruction.opcode=OpCodes.Call; instruction.operand=moveReplacement; moves++;
                }
                else if(instruction.Calls(dispatch))
                {
                    var load=new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    instruction.opcode=OpCodes.Call; instruction.operand=damageReplacement; damages++;
                }
                yield return instruction;
            }
            if(moves!=1 || damages!=1) throw new InvalidOperationException("HerWorld adapter expected one native movement call and one native tick dispatch.");
        }
        private static void NativeMove(EntityControl control,Vector3 nativePoint,Se_U_HerWorld_Blackhole status,float dt,Entity target)
        {
            control.SetAgentPosition(nativePoint);
            if(NetworkServer.active) HostAuthority.NativeInstance?.AttractNyxWorld(status,target,dt);
        }
        private static void NativeDamage(ref DamageData damage,Entity victim,ReactionChain chain,Se_U_HerWorld_Blackhole status)
        {
            var previous=Current;
            Current=NetworkServer.active && damage.actor==status ? new Scope { Status=status,Victim=victim } : default;
            try { damage.Dispatch(victim,chain); }
            finally { Current=previous; }
        }
    }
    [HarmonyPatch(typeof(Actor),nameof(Actor.DealDamage))]
    internal static class NyxWorldTickPacketClaim
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Actor __instance,Entity target)
        {
            ref var scope=ref NyxWorldNativeTick.Current;
            if(scope.Status==null || scope.Claimed) return;
            scope.Claimed=true;
            var packet=NativeAttributedDamagePacket.Current;
            if(__instance==scope.Status && target==scope.Victim && packet!=null && packet.Actor==__instance && packet.Victim==target) scope.Packet=packet.Serial;
        }
    }
    [HarmonyPatch(typeof(Se_U_HerWorld_Blackhole),"OnDestroyActor")]
    internal static class NyxWorldNativeEnd
    {
        internal static Se_U_HerWorld_Blackhole Current;
        private static void Prefix(Se_U_HerWorld_Blackhole __instance,out Se_U_HerWorld_Blackhole __state)
        {
            __state=Current; Current=__instance;
            if(NetworkServer.active) HostAuthority.NativeInstance?.BeginNyxWorldEnd(__instance);
        }
        private static void Finalizer(Se_U_HerWorld_Blackhole __instance,Se_U_HerWorld_Blackhole __state)
        {
            if(NetworkServer.active) HostAuthority.NativeInstance?.FinishNyxWorldEnd(__instance);
            Current=__state;
        }
    }
    [HarmonyPatch(typeof(Se_U_HerWorld_Blackhole),"OnDisable")]
    internal static class NyxWorldDisable
    {
        private static void Prefix(Se_U_HerWorld_Blackhole __instance) => HostAuthority.NativeInstance?.DisableNyxWorld(__instance);
    }
    [HarmonyPatch(typeof(Ai_U_HerWorld_Explosion),"OnHit")]
    internal static class NyxWorldExplosionHit
    {
        internal struct Snapshot { internal float Native; internal long Life; internal bool Changed; internal Ai_U_HerWorld_Explosion Previous; }
        private static Ai_U_HerWorld_Explosion Current;
        private static void Prefix(Ai_U_HerWorld_Explosion __instance,out Snapshot __state)
        {
            __state=default; __state.Previous=Current; Current=__instance;
            if(NetworkServer.active && __state.Previous!=__instance)
                HostAuthority.NativeInstance?.PrepareNyxWorldHit(__instance,out __state.Native,out __state.Life,out __state.Changed);
        }
        private static void Postfix(Ai_U_HerWorld_Explosion __instance,Entity entity)
        {
            if(NetworkServer.active) HostAuthority.NativeInstance?.HitNyxWorldExplosion(__instance,entity);
        }
        private static void Finalizer(Ai_U_HerWorld_Explosion __instance,Snapshot __state)
        {
            Current=__state.Previous;
            if(__state.Changed) HostAuthority.NativeInstance?.RestoreNyxWorldStun(__instance,__state.Native,__state.Life);
        }
    }
    [HarmonyPatch(typeof(Actor),nameof(Actor.ClearPooledEventsAndProcessors))]
    internal static class NyxWorldPooledCleanup
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Actor __instance) => HostAuthority.NativeInstance?.ClearNyxWorldActor(__instance);
    }
    [HarmonyPatch(typeof(Actor),"InvokeOnDestroyActorIfDidnt")]
    internal static class NyxWorldChildCleanup
    {
        private static void Postfix(Actor __instance)
        {
            if(__instance is Ai_U_HerWorld_Explosion child) HostAuthority.NativeInstance?.ClearNyxWorldActor(child);
        }
    }

    internal sealed partial class HostAuthority
    {
        private struct NyxWorldTarget
        {
            internal Entity Target;
            internal long Life;
            internal uint NetId;
            internal long Visual;
            internal bool Used;
        }
        private sealed class NyxWorldState
        {
            internal Se_U_HerWorld_Blackhole Status;
            internal HeroRuntime Runtime;
            internal St_U_HerWorld Skill;
            internal Actor Parent;
            internal long StatusLife,OwnerLife,SkillLife,ParentLife,Epoch,Visual;
            internal Room Room;
            internal string Run;
            internal int Stage,Recorded;
            internal bool Initialized,Natural,Interrupted,Ending,ChildBound,Detached;
            internal float RadiusDelta,NextVisual,LastRefresh = float.NegativeInfinity;
            internal HostAuthority Host;
            internal object Build;
            internal readonly Action<EventInfoDamage> Damaged;
            internal readonly NyxWorldTarget[] Hits=new NyxWorldTarget[3];
            internal readonly NyxMovedTarget[] Movement=new NyxMovedTarget[64];
            internal NyxWorldState() { Damaged=OnDamaged; }
            private void OnDamaged(EventInfoDamage info) { Host?.NyxWorldTickSuccess(this,info); }
            internal void Reset()
            {
                Status=null; Runtime=null; Skill=null; Parent=null; Host=null; Room=null; Run=null; Build=null;
                StatusLife=OwnerLife=SkillLife=ParentLife=Epoch=Visual=0; Stage=Recorded=0;
                Initialized=Natural=Interrupted=Ending=ChildBound=Detached=false;
                RadiusDelta=NextVisual=0; LastRefresh=float.NegativeInfinity;
                Array.Clear(Hits,0,Hits.Length); Array.Clear(Movement,0,Movement.Length);
            }
        }
        private sealed class NyxWorldEndToken
        {
            internal NyxWorldState State;
            internal Ai_U_HerWorld_Explosion Child;
            internal long ChildLife;
            internal float Until,H;
            internal bool Magic;
        }
        private readonly Dictionary<Se_U_HerWorld_Blackhole,NyxWorldState> _nyxWorlds=new Dictionary<Se_U_HerWorld_Blackhole,NyxWorldState>(64);
        private readonly Dictionary<Ai_U_HerWorld_Explosion,NyxWorldEndToken> _nyxWorldEnds=new Dictionary<Ai_U_HerWorld_Explosion,NyxWorldEndToken>(64);
        private readonly BossObjectPool<NyxWorldState> _nyxWorldPool=new BossObjectPool<NyxWorldState>(128,()=>new NyxWorldState());
        private readonly BossObjectPool<NyxWorldEndToken> _nyxWorldEndPool=new BossObjectPool<NyxWorldEndToken>(64,()=>new NyxWorldEndToken());
        private readonly List<Se_U_HerWorld_Blackhole> _nyxWorldScratch=new List<Se_U_HerWorld_Blackhole>(64);
        private readonly List<Ai_U_HerWorld_Explosion> _nyxWorldEndScratch=new List<Ai_U_HerWorld_Explosion>(64);
        private bool NyxWorldOwnerCurrent(NyxWorldState state)
            => NetworkServer.active && BossAlive(state.Runtime.Hero) && BossNativeSameLife(state.Runtime.Hero,state.OwnerLife)
                && BossNativeContextCurrent(state.Room,state.Run) && state.Skill!=null && state.Skill.isActive && state.Skill.owner==state.Runtime.Hero
                && BossNativeSameLife(state.Skill,state.SkillLife) && FindMemory(state.Runtime.Hero,nameof(St_U_HerWorld))==state.Skill;
        private bool NyxWorldNativeCurrent(NyxWorldState state,bool requireActive=true)
            => NyxWorldOwnerCurrent(state) && state.Status!=null && (!requireActive || state.Status.isActive) && !state.Ending
                && BossNativeSameLife(state.Status,state.StatusLife) && state.Status.info.caster==state.Runtime.Hero && state.Status.victim==state.Runtime.Hero
                && state.Status.firstTrigger==state.Skill && state.Status.gem==null && state.Status.parentActor==state.Parent
                && state.Parent!=null && BossNativeSameLife(state.Parent,state.ParentLife);
        private bool NyxWorldEligible(NyxWorldState state)
            => NyxWorldNativeCurrent(state) && BossEnsure(state.Runtime) && state.Stage>0
                && BossRewardStage(state.Runtime,BossProfiles.NyxRewardId)==state.Stage && AttributionGeneratedOrigin()==GeneratedOrigin.None;
        internal void PrepareNyxWorld(EventInfoAbilityInstance info)
        {
            if(AttributionGeneratedOrigin()!=GeneratedOrigin.None) return;
            if(info.instance is Se_U_HerWorld_Blackhole status)
            {
                if(!(status.info.caster is Hero hero) || status.victim!=hero || !(status.firstTrigger is St_U_HerWorld skill)
                    || skill.owner!=hero || FindMemory(hero,nameof(St_U_HerWorld))!=skill || status.gem!=null || status.parentActor!=info.actor
                    || !_runtimes.TryGetValue(hero,out var rt) || !BossAlive(hero)) return;
                DisableNyxWorld(status);
                if(_nyxWorlds.Count>=64) return;
                var capture=_nyxWorldPool.Rent(); if(capture==null) return;
                capture.Host=this; capture.Status=status; capture.Runtime=rt; capture.Skill=skill; capture.Parent=info.actor;
                capture.StatusLife=BossNativeActorLife(status); capture.OwnerLife=BossNativeActorLife(hero);
                capture.SkillLife=BossNativeActorLife(skill); capture.ParentLife=BossNativeActorLife(info.actor);
                capture.Epoch=rt.ShieldEquipmentEpoch; capture.Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
                capture.Run=NetworkedManagerBase<GameManager>.softInstance?.runId; capture.Visual=++rt.Boss.NextId;
                _nyxWorlds.Add(status,capture);
                return;
            }
            if(!(info.instance is Ai_U_HerWorld_Explosion child) || !(info.actor is Se_U_HerWorld_Blackhole parent)
                || NyxWorldNativeEnd.Current!=parent || !_nyxWorlds.TryGetValue(parent,out var state) || !state.Ending || state.ChildBound
                || !state.Natural || state.Interrupted || state.Stage!=3 || child.parentActor!=parent || child.info.caster!=state.Runtime.Hero
                || child.gem!=null || !NyxWorldOwnerCurrent(state)
                || BossRewardStage(state.Runtime,BossProfiles.NyxRewardId)!=3 || !BossNativeSameLife(parent,state.StatusLife) || _nyxWorldEnds.Count>=64) return;
            NyxWorldRemoveEnd(child);
            var token=_nyxWorldEndPool.Rent(); if(token==null) return;
            state.ChildBound=true;
            token.State=state; token.Child=child; token.ChildLife=BossNativeActorLife(child);
            token.Until=Time.time+1; token.H=Math.Max(state.Runtime.Hero.Status.attackDamage,state.Runtime.Hero.Status.abilityPower);
            token.Magic=BossMagic(state.Runtime); _nyxWorldEnds.Add(child,token);
            NyxWorldPublishTargets(state,Time.time,Time.time+1);
        }
        internal void CreateNyxWorld(Se_U_HerWorld_Blackhole status)
        {
            if(!_nyxWorlds.TryGetValue(status,out var state) || !BossNativeSameLife(status,state.StatusLife) || state.Initialized) return;
            state.Initialized=true;
            status.ActorEvent_OnDealDamage+=state.Damaged;
            RefreshNyxWorld(status);
        }
        private void NyxWorldRestoreRadius(NyxWorldState state)
        {
            if(state.RadiusDelta==0) return;
            if(state.Status!=null && BossNativeSameLife(state.Status,state.StatusLife)) state.Status.tickDamageRadius-=state.RadiusDelta;
            state.RadiusDelta=0;
        }
        internal void RefreshNyxWorld(Se_U_HerWorld_Blackhole status)
        {
            if(!_nyxWorlds.TryGetValue(status,out var state) || !state.Initialized) return;
            float now=Time.time;
            if(state.LastRefresh==now && state.Epoch==state.Runtime.ShieldEquipmentEpoch && ReferenceEquals(state.Build,state.Runtime.Powers.Build)) return;
            state.LastRefresh=now;
            if(!NyxWorldNativeCurrent(state)) { NyxWorldRestoreRadius(state); return; }
            bool enabled=BossEnsure(state.Runtime);
            int stage=enabled ? BossRewardStage(state.Runtime,BossProfiles.NyxRewardId) : 0;
            if(state.Stage!=stage)
            {
                NyxWorldRestoreRadius(state);
                if(stage<3) NyxWorldClearTargets(state,Time.time);
                state.Stage=stage;
            }
            state.Epoch=state.Runtime.ShieldEquipmentEpoch;
            state.Build=state.Runtime.Powers.Build;
            float native=status.tickDamageRadius-state.RadiusDelta;
            float delta=stage>=2 && !float.IsNaN(native) && !float.IsInfinity(native) ? Math.Min(1,Math.Max(0,8-native)) : 0;
            status.tickDamageRadius=native+delta; state.RadiusDelta=delta;
            NyxWorldPublish(state,Time.time);
        }
        internal void AttractNyxWorld(Se_U_HerWorld_Blackhole status,Entity target,float dt)
        {
            if(!_nyxWorlds.TryGetValue(status,out var state) || !NyxWorldEligible(state) || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt)
                || !BossAlive(target) || target.IsAnyBoss() || target.Status.hasCrowdControlImmunity || target.Status.hasUncollidable
                || float.IsNaN(status.tickDamageRadius) || float.IsInfinity(status.tickDamageRadius)
                || BossDirectionDistance(state.Runtime.Hero.position,target.agentPosition)>status.tickDamageRadius) return;
            long life=BossNativeActorLife(target); int slot=-1;
            for(int i=0;i<state.Movement.Length;i++)
            {
                var row=state.Movement[i];
                if(row.Target==target && row.Life==life) { slot=i; break; }
                if(slot<0 && row.Target==null) slot=i;
            }
            if(slot<0) return;
            ref var movement=ref state.Movement[slot];
            if(movement.Target==null) { movement.Target=target; movement.Life=life; }
            movement.Moved+=NyxAdditionalMove(state.Runtime,target,life,state.Runtime.Hero.position,Math.Min(2-movement.Moved,dt));
        }
        private void NyxWorldTickSuccess(NyxWorldState state,EventInfoDamage info)
        {
            ref var scope=ref NyxWorldNativeTick.Current;
            var packet=NativeAttributedDamagePacket.Current;
            if(state.Stage!=3 || !NyxWorldEligible(state) || info.actor!=state.Status || info.victim==null || info.damage.amount<=0
                || info.victim.GetRelation(state.Runtime.Hero)!=EntityRelation.Enemy || !info.chain.Equals(default(ReactionChain))
                || scope.Status!=state.Status || scope.Victim!=info.victim || packet==null || packet.Serial!=scope.Packet
                || packet.Actor!=state.Status || packet.Victim!=info.victim || state.Recorded>=3) return;
            long life=BossNativeActorLife(info.victim);
            for(int i=0;i<state.Recorded;i++) if(state.Hits[i].Target==info.victim && state.Hits[i].Life==life) return;
            state.Hits[state.Recorded++]=new NyxWorldTarget { Target=info.victim,Life=life,NetId=info.victim.persistentNetId,Visual=++state.Runtime.Boss.NextId };
            NyxWorldPublishTargets(state,Time.time,Time.time+Math.Max(0,NyxWorldRemaining(state)));
        }
        private static float NyxWorldRemaining(NyxWorldState state) => state.Status.remainingDuration ?? 0;
        internal void InterruptNyxWorld(Se_U_HerWorld_Blackhole status)
        {
            if(!_nyxWorlds.TryGetValue(status,out var state)) return;
            state.Interrupted=true; state.Natural=false; NyxWorldClearTargets(state,Time.time);
        }
        internal void ExpireNyxWorld(Se_U_HerWorld_Blackhole status)
        {
            if(_nyxWorlds.TryGetValue(status,out var state) && !state.Interrupted && NyxWorldEligible(state)) state.Natural=true;
        }
        internal void BeginNyxWorldEnd(Se_U_HerWorld_Blackhole status)
        {
            if(!_nyxWorlds.TryGetValue(status,out var state)) return;
            if(!NyxWorldNativeCurrent(state,false) || !BossEnsure(state.Runtime)
                || BossRewardStage(state.Runtime,BossProfiles.NyxRewardId)!=3) state.Natural=false;
            state.Ending=true; NyxWorldRestoreRadius(state);
            PublishBossVisual(state.Runtime,state.Visual,4,state.Runtime.Hero.position,state.Runtime.Hero.position,0,Time.time,Time.time,true);
        }
        internal void FinishNyxWorldEnd(Se_U_HerWorld_Blackhole status)
        {
            if(!_nyxWorlds.TryGetValue(status,out var state)) return;
            if(BossNativeSameLife(status,state.StatusLife)) status.ActorEvent_OnDealDamage-=state.Damaged;
            if(!state.ChildBound) NyxWorldClearTargets(state,Time.time);
        }
        internal void DisableNyxWorld(Se_U_HerWorld_Blackhole status)
        {
            if(ReferenceEquals(status,null) || !_nyxWorlds.TryGetValue(status,out var state)) return;
            NyxWorldRestoreRadius(state);
            if(BossNativeSameLife(status,state.StatusLife) && state.Damaged!=null) status.ActorEvent_OnDealDamage-=state.Damaged;
            PublishBossVisual(state.Runtime,state.Visual,4,state.Runtime.Hero.position,state.Runtime.Hero.position,0,Time.time,Time.time,true);
            if(!state.ChildBound) NyxWorldClearTargets(state,Time.time);
            _nyxWorlds.Remove(status);
            state.Detached=true;
            if(!state.ChildBound) { state.Reset(); _nyxWorldPool.Return(state); }
        }
        private bool NyxWorldEndCurrent(NyxWorldEndToken token)
        {
            var state=token.State;
            if(!NetworkServer.active || Time.time>token.Until || !NyxWorldOwnerCurrent(state) || state.Runtime.Boss.Build==null
                || BossRewardStage(state.Runtime,BossProfiles.NyxRewardId)!=3
                || !state.Natural || state.Interrupted || token.Child==null || !token.Child.isActive || !BossNativeSameLife(token.Child,token.ChildLife)
                || token.Child.parentActor!=state.Status || token.Child.info.caster!=state.Runtime.Hero || token.Child.gem!=null) return false;
            // The parent may be disabled in the pool already. Absence is legal; a new
            // generation of that same pooled reference must never inherit its end token.
            return !_bossNativeActorLives.TryGetValue(state.Status,out var currentLife) || currentLife==state.StatusLife;
        }
        internal void PrepareNyxWorldHit(Ai_U_HerWorld_Explosion child,out float native,out long life,out bool changed)
        {
            native=child.stunDuration; life=0; changed=false;
            if(!_nyxWorldEnds.TryGetValue(child,out var token) || !BossEnsure(token.State.Runtime) || !NyxWorldEndCurrent(token) || AttributionGeneratedOrigin()!=GeneratedOrigin.None
                || float.IsNaN(native) || float.IsInfinity(native)) return;
            life=token.ChildLife; float delta=Math.Min(.25f,Math.Max(0,3-native));
            child.stunDuration=native+delta; changed=delta>0;
        }
        internal void RestoreNyxWorldStun(Ai_U_HerWorld_Explosion child,float native,long life)
        {
            if(child!=null && BossNativeSameLife(child,life)) child.stunDuration-=Math.Min(.25f,Math.Max(0,3-native));
        }
        internal void HitNyxWorldExplosion(Ai_U_HerWorld_Explosion child,Entity target)
        {
            if(!_nyxWorldEnds.TryGetValue(child,out var token) || !BossEnsure(token.State.Runtime) || !NyxWorldEndCurrent(token) || AttributionGeneratedOrigin()!=GeneratedOrigin.None
                || !BossAlive(target) || target.GetRelation(token.State.Runtime.Hero)!=EntityRelation.Enemy) return;
            for(int i=0;i<token.State.Hits.Length;i++)
            {
                ref var row=ref token.State.Hits[i];
                if(row.Target==null || row.Used || row.Target!=target || !BossNativeSameLife(target,row.Life) || target.persistentNetId!=row.NetId) continue;
                row.Used=true;
                PublishBossVisual(token.State.Runtime,row.Visual,9,target.position,target.position,0,Time.time,Time.time,true);
                BossDamage(token.State.Runtime,target,token.H*.45f,token.Magic,BossElement.Light);
                break;
            }
        }
        private void NyxWorldPublish(NyxWorldState state,float now)
        {
            if(now<state.NextVisual) return;
            state.NextVisual=now+.25f;
            var center=state.Runtime.Hero.position;
            PublishBossVisual(state.Runtime,state.Visual,4,center,center,Math.Max(0,state.Status.tickDamageRadius),now,
                now+Math.Max(.05f,NyxWorldRemaining(state)),state.Stage==0,element:BossElement.Light,count:state.Recorded,budget:2,nativeLife:state.StatusLife);
            if(state.Stage==3) NyxWorldPublishTargets(state,now,now+Math.Max(.05f,NyxWorldRemaining(state)));
        }
        private void NyxWorldPublishTargets(NyxWorldState state,float now,float until)
        {
            foreach(var row in state.Hits)
                if(row.Target!=null && !row.Used && BossAlive(row.Target) && BossNativeSameLife(row.Target,row.Life))
                    PublishBossVisual(state.Runtime,row.Visual,9,row.Target.position,row.Target.position,1,now,until,
                        element:BossElement.Light,count:1,targetNetId:row.NetId,nativeLife:state.StatusLife);
        }
        private void NyxWorldClearTargets(NyxWorldState state,float now)
        {
            foreach(var row in state.Hits)
                if(row.Target!=null) PublishBossVisual(state.Runtime,row.Visual,9,Vector3.zero,Vector3.zero,0,now,now,true);
            Array.Clear(state.Hits,0,state.Hits.Length); state.Recorded=0;
        }
        private void NyxWorldRemoveEnd(Ai_U_HerWorld_Explosion child)
        {
            if(ReferenceEquals(child,null) || !_nyxWorldEnds.TryGetValue(child,out var token)) return;
            var state=token.State;
            NyxWorldClearTargets(state,Time.time); _nyxWorldEnds.Remove(child); state.ChildBound=false;
            token.State=null; token.Child=null; token.ChildLife=0; token.Until=token.H=0; token.Magic=false;
            _nyxWorldEndPool.Return(token);
            if(state.Detached) { state.Reset(); _nyxWorldPool.Return(state); }
        }
        internal void ClearNyxWorldActor(Actor actor)
        {
            if(actor is Se_U_HerWorld_Blackhole status) DisableNyxWorld(status);
            if(actor is Ai_U_HerWorld_Explosion child) NyxWorldRemoveEnd(child);
        }
        private void TickNyxWorlds(HeroRuntime rt,float now)
        {
            _nyxWorldScratch.Clear();
            foreach(var pair in _nyxWorlds) if(pair.Value.Runtime==rt) _nyxWorldScratch.Add(pair.Key);
            foreach(var status in _nyxWorldScratch)
            {
                if(!_nyxWorlds.TryGetValue(status,out var state)) continue;
                if(state.Ending) continue;
                if(!NyxWorldNativeCurrent(state)) DisableNyxWorld(status);
                else RefreshNyxWorld(status);
            }
            _nyxWorldEndScratch.Clear();
            foreach(var pair in _nyxWorldEnds) if(pair.Value.State.Runtime==rt && !NyxWorldEndCurrent(pair.Value)) _nyxWorldEndScratch.Add(pair.Key);
            foreach(var child in _nyxWorldEndScratch) NyxWorldRemoveEnd(child);
        }
        private void ClearNyxWorlds(HeroRuntime rt,bool preserveRewards)
        {
            _nyxWorldScratch.Clear();
            foreach(var pair in _nyxWorlds) if(pair.Value.Runtime==rt) _nyxWorldScratch.Add(pair.Key);
            foreach(var status in _nyxWorldScratch)
            {
                if(!_nyxWorlds.TryGetValue(status,out var state)) continue;
                bool keep=preserveRewards && NyxWorldOwnerCurrent(state) && BossNativeSameLife(status,state.StatusLife)
                    && (state.Ending || status.isActive) && status.parentActor==state.Parent && BossNativeSameLife(state.Parent,state.ParentLife);
                if(!keep) { DisableNyxWorld(status); continue; }
                // Unchanged reward/native parent retains recorded hits, its lifetime
                // budget and any already-bound natural-end child.
                state.Epoch=rt.ShieldEquipmentEpoch; state.LastRefresh=float.NegativeInfinity;
            }
            _nyxWorldEndScratch.Clear();
            foreach(var pair in _nyxWorldEnds)
                if(pair.Value.State.Runtime==rt && (!preserveRewards || !NyxWorldEndCurrent(pair.Value))) _nyxWorldEndScratch.Add(pair.Key);
            foreach(var child in _nyxWorldEndScratch) NyxWorldRemoveEnd(child);
        }
    }
}
