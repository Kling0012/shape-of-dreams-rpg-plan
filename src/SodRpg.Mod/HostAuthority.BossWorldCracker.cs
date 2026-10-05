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
    [HarmonyPatch(typeof(Ai_U_WorldCracker),"OnCreate")]
    internal static class LightWorldCrackerCreate
    {
        private static void Prefix(Ai_U_WorldCracker __instance)
        { if(NetworkServer.active) HostAuthority.NativeInstance?.BeginWorldCracker(__instance); }
    }
    [HarmonyPatch(typeof(Ai_U_WorldCracker),"OnDestroyActor")]
    internal static class LightWorldCrackerEnd
    {
        private static void Prefix(Ai_U_WorldCracker __instance) => HostAuthority.NativeInstance?.EndWorldCracker(__instance);
    }
    [HarmonyPatch(typeof(Ai_U_WorldCracker),"OnDisable")]
    internal static class LightWorldCrackerDisable
    {
        private static void Prefix(Ai_U_WorldCracker __instance) => HostAuthority.NativeInstance?.EndWorldCracker(__instance);
    }
    [HarmonyPatch(typeof(Actor),nameof(Actor.ClearPooledEventsAndProcessors))]
    internal static class LightWorldCrackerPoolCleanup
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Actor __instance)
        { if(__instance is Ai_U_WorldCracker beam) HostAuthority.NativeInstance?.EndWorldCracker(beam); }
    }
    // Actual Dew.Contents IL: maxDistance -> loc1 at0152; native ground hit.distance+2
    // -> loc1 at0190; SphereCast uses loc1 at01af; sole DamageData.Dispatch at029a.
    // SolvePosition's unrelated100m visual endpoint is deliberately never consulted.
    [HarmonyPatch(typeof(Ai_U_WorldCracker),"ActiveLogicUpdate")]
    internal static class LightWorldCrackerNativeTick
    {
        internal struct Scope
        {
            internal Ai_U_WorldCracker Beam;
            internal Entity Target;
            internal Vector3 End;
        }
        internal static Scope Current;
        private static bool Prepare()
        {
            var method=AccessTools.DeclaredMethod(typeof(Ai_U_WorldCracker),"ActiveLogicUpdate");
            var body=method.GetMethodBody();
            if(body==null || body.LocalVariables.Count!=9 || body.LocalVariables[1].LocalType!=typeof(float))
                throw new InvalidOperationException("WorldCracker clipped distance local contract changed.");
            return true;
        }
        private static void Prefix(Ai_U_WorldCracker __instance)
        { if(NetworkServer.active) HostAuthority.NativeInstance?.RefreshWorldCracker(__instance); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var dispatch=AccessTools.Method(typeof(DamageData),nameof(DamageData.Dispatch));
            var replacement=AccessTools.DeclaredMethod(typeof(LightWorldCrackerNativeTick),nameof(NativeDispatch));
            int sites=0,clipStores=0,casts=0;
            foreach(var instruction in instructions)
            {
                if(instruction.opcode==OpCodes.Stloc_1) clipStores++;
                if(instruction.operand is MethodInfo call && call.DeclaringType==typeof(DewPhysics) && call.Name==nameof(DewPhysics.SphereCastAllEntities)) casts++;
                if(instruction.Calls(dispatch))
                {
                    var load=new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load; yield return new CodeInstruction(OpCodes.Ldloc_1);
                    instruction.opcode=OpCodes.Call; instruction.operand=replacement; sites++;
                }
                yield return instruction;
            }
            if(sites!=1 || clipStores!=2 || casts!=1)
                throw new InvalidOperationException("WorldCracker expected exact native clipped distance and single tick dispatch.");
        }
        private static void NativeDispatch(ref DamageData damage,Entity target,ReactionChain chain,Ai_U_WorldCracker beam,float clippedDistance)
        {
            var previous=Current;
            Current=NetworkServer.active && damage.actor==beam && clippedDistance>0 && !float.IsNaN(clippedDistance) && !float.IsInfinity(clippedDistance)
                ? new Scope { Beam=beam,Target=target,End=beam.transform.position+beam.transform.forward*clippedDistance } : default;
            try { damage.Dispatch(target,chain); }
            finally { Current=previous; }
        }
    }
    internal sealed partial class HostAuthority
    {
        private struct WorldCrackerTicks
        {
            internal Entity Target;
            internal long Life;
            internal int Count;
            internal float Last;
        }
        private sealed class WorldCrackerBinding
        {
            internal readonly WorldCrackerTicks[] Ticks=new WorldCrackerTicks[3];
            internal readonly Action<EventInfoDamage> Damaged;
            internal HostAuthority Host;
            internal HeroRuntime Runtime;
            internal Ai_U_WorldCracker Beam;
            internal St_U_WorldCracker Skill;
            internal Actor Parent;
            internal Room Room;
            internal string Run;
            internal long BeamLife,SkillLife,ParentLife,HeroLife,Life,Epoch,Pending,LastPacket;
            internal float Turn,Radius,H;
            internal int Stage,Pulses;
            internal bool Magic;
            internal WorldCrackerBinding() { Damaged=OnDamage; }
            private void OnDamage(EventInfoDamage info) { Host?.WorldCrackerDamage(this,info); }
        }
        private static readonly BossAction WorldCrackerPulse = new BossAction(BossEvent.NativeDamageDealt,BossMechanism.ShapeAttack,
            BossPayload.Damage,delayMillis:350,maxTargets:8,maxInstances:1,radiusMilli:1800,element:BossElement.Light);
        private const string WorldCrackerPulseKey="boss_light_elemental.world_cracker.pulse";
        private readonly BossObjectPool<WorldCrackerBinding> _worldCrackerPool=new BossObjectPool<WorldCrackerBinding>(128,()=>new WorldCrackerBinding());
        private readonly Dictionary<Ai_U_WorldCracker,WorldCrackerBinding> _worldCrackers=new Dictionary<Ai_U_WorldCracker,WorldCrackerBinding>(128);
        private readonly Ai_U_WorldCracker[] _worldCrackerScratch=new Ai_U_WorldCracker[128];
        internal void BeginWorldCracker(Ai_U_WorldCracker beam)
        {
            if(beam==null || !(beam.info.caster is Hero hero) || !BossAlive(hero) || beam.gem!=null || AttributionGeneratedOrigin()!=GeneratedOrigin.None
                || !_runtimes.TryGetValue(hero,out var rt)) return;
            var skill=beam.FindFirstAncestorOfType<SkillTrigger>() as St_U_WorldCracker;
            if(skill==null || skill.owner!=hero || FindMemory(hero,nameof(St_U_WorldCracker))!=skill || beam.parentActor==null) return;
            EndWorldCracker(beam);
            BossEnsure(rt);
            int stage=BossRewardStage(rt,BossProfiles.LightRewardId);
            if(stage<=0 || _worldCrackers.Count>=128) return;
            var state=_worldCrackerPool.Rent(); if(state==null) return;
            state.Host=this; state.Runtime=rt; state.Beam=beam; state.Skill=skill; state.Parent=beam.parentActor;
            state.Turn=beam.angleSpeed; state.Radius=beam.radius; state.H=Math.Max(0,Math.Max(hero.Status.attackDamage,hero.Status.abilityPower)); state.Magic=BossMagic(rt);
            state.BeamLife=BossNativeActorLife(beam); state.SkillLife=BossNativeActorLife(skill); state.ParentLife=BossNativeActorLife(state.Parent); state.HeroLife=BossNativeActorLife(hero);
            state.Life=_memoryAttribution.NewPacketId(); state.Epoch=rt.ShieldEquipmentEpoch; state.Stage=stage;
            state.Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom; state.Run=NetworkedManagerBase<GameManager>.softInstance?.runId;
            _worldCrackers.Add(beam,state); beam.ActorEvent_OnDealDamage+=state.Damaged;
            WorldCrackerConfigure(state,stage);
        }
        private static void WorldCrackerConfigure(WorldCrackerBinding state,int stage)
        {
            state.Stage=stage;
            state.Beam.angleSpeed=stage>=1 && state.Turn>0 && !float.IsInfinity(state.Turn) ? Math.Min(state.Turn+30f,state.Turn*1.5f) : state.Turn;
            state.Beam.radius=stage>=2 && state.Radius>0 && !float.IsInfinity(state.Radius) ? Math.Min(state.Radius+.30f,state.Radius*1.5f) : state.Radius;
        }
        private bool WorldCrackerCurrent(WorldCrackerBinding state)
        {
            var rt=state.Runtime; var beam=state.Beam;
            return NetworkServer.active && rt!=null && BossAlive(rt.Hero) && beam!=null && beam.isActive && beam.info.caster==rt.Hero && beam.gem==null
                && state.Parent!=null && state.Parent.isActive && beam.parentActor==state.Parent && state.Skill!=null && state.Skill.isActive && state.Skill.owner==rt.Hero
                && FindMemory(rt.Hero,nameof(St_U_WorldCracker))==state.Skill && beam.FindFirstAncestorOfType<SkillTrigger>()==state.Skill
                && BossNativeSameLife(beam,state.BeamLife) && BossNativeSameLife(state.Skill,state.SkillLife)
                && BossNativeSameLife(state.Parent,state.ParentLife) && BossNativeSameLife(rt.Hero,state.HeroLife)
                && _runtimes.TryGetValue(rt.Hero,out var current) && current==rt && BossNativeContextCurrent(state.Room,state.Run);
        }
        internal void RefreshWorldCracker(Ai_U_WorldCracker beam)
        {
            if(!_worldCrackers.TryGetValue(beam,out var state)) return;
            if(!WorldCrackerCurrent(state)) { EndWorldCracker(beam); return; }
            BossEnsure(state.Runtime);
            if(!_worldCrackers.TryGetValue(beam,out var current) || current!=state) return;
            if(!WorldCrackerCurrent(state)) { EndWorldCracker(beam); return; }
            int stage=BossRewardStage(state.Runtime,BossProfiles.LightRewardId);
            if(stage<=0) { state.Runtime.Boss.Ready.Remove(WorldCrackerPulseKey); EndWorldCracker(beam); return; }
            state.Epoch=state.Runtime.ShieldEquipmentEpoch;
            if(stage!=state.Stage)
            {
                WorldCrackerConfigure(state,stage);
                if(stage<3)
                {
                    BossCancelNativeReservations(state.Runtime,BossProfiles.LightSetId,state.Life);
                    state.Pending=0; Array.Clear(state.Ticks,0,state.Ticks.Length);
                }
            }
            if(state.Pending!=0 && !BossReservationExists(state.Runtime,state.Pending)) state.Pending=0;
        }
        private void WorldCrackerDamage(WorldCrackerBinding state,EventInfoDamage info)
        {
            var scope=LightWorldCrackerNativeTick.Current;
            if(scope.Beam!=state.Beam || scope.Target!=info.victim || info.actor!=state.Beam || info.damage.amount<=0 || !info.chain.Equals(default(ReactionChain))
                || AttributionGeneratedOrigin()!=GeneratedOrigin.None || !WorldCrackerCurrent(state)) return;
            RefreshWorldCracker(state.Beam);
            if(state.Host!=this || state.Beam!=scope.Beam || state.Stage<3 || state.Epoch!=state.Runtime.ShieldEquipmentEpoch) return;
            var packet=NativeAttributedDamagePacket.Current;
            if(packet==null || packet.Actor!=state.Beam || packet.Victim!=info.victim || packet.Serial==state.LastPacket) return;
            state.LastPacket=packet.Serial;
            var victim=info.victim; if(victim==null || !state.Runtime.Hero.CheckEnemyOrNeutral(victim)) return;
            float now=Time.time; long life=BossNativeActorLife(victim); if(life<=0) return;
            int slot=-1,free=-1;
            for(int i=0;i<3;i++)
            {
                var row=state.Ticks[i];
                if(row.Target==victim && row.Life==life) { slot=i; break; }
                if(free<0 && (row.Target==null || !BossNativeSameLife(row.Target,row.Life) || !row.Target.isActive || now-row.Last>1f)) free=i;
            }
            if(slot<0) slot=free; if(slot<0) return;
            var tick=state.Ticks[slot];
            if(tick.Target!=victim || tick.Life!=life || now-tick.Last>1f) tick=new WorldCrackerTicks { Target=victim,Life=life };
            tick.Last=now; tick.Count=Math.Min(3,tick.Count+1); state.Ticks[slot]=tick;
            if(tick.Count<3) return;
            // Busy completion is consumed; it never queues an extra pulse behind the current one.
            tick.Count=0; state.Ticks[slot]=tick;
            if(state.Pulses>=2 || state.Pending!=0 || state.Runtime.Boss.Ready.TryGetValue(WorldCrackerPulseKey,out var ready) && now<ready) return;
            if(!state.Runtime.Boss.Ready.ContainsKey(WorldCrackerPulseKey) && state.Runtime.Boss.Ready.Count>=128) return;
            var endpoint=scope.End;
            if(!BossFinite(endpoint)) return;
            // Projection validates the captured damage endpoint; it does not recompute native num.
            var grounded=Dew.GetPositionOnGround(endpoint);
            if(!BossFinite(grounded) || Mathf.Abs(grounded.y-endpoint.y)>2f) return;
            var sequence=state.Runtime.Boss.Sequence.Reset();
            sequence.Add(new BossScheduledPulse(WorldCrackerPulse,grounded,grounded,state.H*.45f,state.Magic,350));
            if(!BossReserveSequence(state.Runtime,BossProfiles.LightSetId,BossProfiles.LightRewardId,sequence,now,nativeLife:state.Life)) return;
            state.Pending=BossReservationId(state.Runtime,BossProfiles.LightSetId,BossProfiles.LightRewardId); state.Pulses++;
            if(state.Runtime.Boss.Ready.ContainsKey(WorldCrackerPulseKey) || state.Runtime.Boss.Ready.Count<128) state.Runtime.Boss.Ready[WorldCrackerPulseKey]=now+6f;
        }
        internal void EndWorldCracker(Ai_U_WorldCracker beam)
        {
            if(ReferenceEquals(beam,null) || !_worldCrackers.TryGetValue(beam,out var state)) return;
            if(beam!=null && BossNativeSameLife(beam,state.BeamLife))
            {
                beam.ActorEvent_OnDealDamage-=state.Damaged;
                beam.angleSpeed=state.Turn; beam.radius=state.Radius;
            }
            BossCancelNativeReservations(state.Runtime,BossProfiles.LightSetId,state.Life);
            _worldCrackers.Remove(beam);
            state.Host=null; state.Runtime=null; state.Beam=null; state.Skill=null; state.Parent=null; state.Room=null; state.Run=null;
            state.BeamLife=state.SkillLife=state.ParentLife=state.HeroLife=state.Life=state.Epoch=state.Pending=state.LastPacket=0;
            state.Stage=state.Pulses=0; state.Turn=state.Radius=state.H=0; state.Magic=false; Array.Clear(state.Ticks,0,state.Ticks.Length);
            _worldCrackerPool.Return(state);
        }
        private void TickWorldCrackers(HeroRuntime rt,float now)
        {
            if (now < rt.BossNative.WorldPoll) return;
            rt.BossNative.WorldPoll = now + .1f;
            int count=0;
            foreach(var pair in _worldCrackers) if(pair.Value.Runtime==rt) _worldCrackerScratch[count++]=pair.Key;
            for(int i=0;i<count;i++) { var beam=_worldCrackerScratch[i]; _worldCrackerScratch[i]=null; RefreshWorldCracker(beam); }
        }
        private void ClearWorldCrackers(HeroRuntime rt)
        {
            int count=0;
            foreach(var pair in _worldCrackers) if(pair.Value.Runtime==rt) _worldCrackerScratch[count++]=pair.Key;
            for(int i=0;i<count;i++) { var beam=_worldCrackerScratch[i]; _worldCrackerScratch[i]=null; EndWorldCracker(beam); }
            rt.Boss.Ready.Remove(WorldCrackerPulseKey);
        }
    }
}
