using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private const string NyxMarkerProfile = "boss_nyx.stage2.marker";
        private struct NyxMovedTarget
        {
            internal Entity Target;
            internal long Life;
            internal float Moved;
        }
        private sealed class NyxChannelState
        {
            internal long Reservation, Visual;
            internal Vector3 Center;
            internal float Start, Last, End, Damage, Shield;
            internal bool Magic, Selected;
            internal BossMoveProfile Profile;
            internal readonly NyxMovedTarget[] Targets = new NyxMovedTarget[3];
        }
        private sealed class NyxState
        {
            internal long MarkerReservation, MarkerVisual, SeedReservation, SeedVisual, MarkVisual;
            internal Vector3 Marker, Seed, SeedDirection, DashLast;
            internal float MarkerUntil, MarksUntil, SeedDue, SeedDamage, DashUntil, DashDamage, NextMarkVisual;
            internal int Marks;
            internal bool Relocated, SeedMagic, DashMagic;
            internal BossAction SeedAction;
            internal Displacement Dash;
            internal HostAuthority Host;
            internal HeroRuntime Runtime;
            internal readonly Action<Displacement> DashStopped;
            internal readonly NyxChannelState OwnedChannel = new NyxChannelState();
            internal NyxChannelState Channel;
            internal readonly Dictionary<Entity,long> DashHits = new Dictionary<Entity,long>(64);
            internal NyxState() { DashStopped = OnDashStopped; }
            private void OnDashStopped(Displacement displacement)
            { if (Host!=null && Runtime!=null && ReferenceEquals(Dash,displacement)) Host.NyxTickDash(Runtime,this,Time.time); }
            internal void Reset()
            {
                Host=null; Runtime=null; MarkerReservation=MarkerVisual=SeedReservation=SeedVisual=MarkVisual=0;
                Marker=Seed=SeedDirection=DashLast=default; MarkerUntil=MarksUntil=SeedDue=SeedDamage=DashUntil=DashDamage=NextMarkVisual=0;
                Marks=0; Relocated=SeedMagic=DashMagic=false; SeedAction=null; Dash=null; Channel=null; DashHits.Clear();
                OwnedChannel.Profile=null; Array.Clear(OwnedChannel.Targets,0,OwnedChannel.Targets.Length);
            }
        }
        private readonly Dictionary<HeroRuntime,NyxState> _nyxStates = new Dictionary<HeroRuntime,NyxState>(64);
        private readonly BossObjectPool<NyxState> _nyxPool = new BossObjectPool<NyxState>(64,()=>new NyxState());
        private NyxState NyxGet(HeroRuntime rt)
        {
            if (!_nyxStates.TryGetValue(rt,out var state))
            {
                if (_nyxStates.Count>=64 || (state=_nyxPool.Rent())==null) return null;
                state.Host=this; state.Runtime=rt; _nyxStates.Add(rt,state);
            }
            return state;
        }
        private static bool NyxAvailable(HeroRuntime rt,BossAction action,float now)
            => rt.Boss.Ready.TryGetValue(action.RuntimeKey,out var ready) ? now>=ready : rt.Boss.Ready.Count<128;
        private bool NyxReserve(HeroRuntime rt,string profile,BossPulseBuffer pulses,float now,int lifetime=0)
            => BossReserveSequence(rt,BossProfiles.NyxSetId,profile,pulses,now,maxInstances:1,lifetimeMillis:lifetime,maxSetInstances:2,replaceOldest:true);
        private bool NyxCommitReserve(HeroRuntime rt,BossMoveProfile profile,BossAction action,BossPulseBuffer pulses,float now)
        {
            if (!NyxAvailable(rt,action,now) || !NyxReserve(rt,profile.Id,pulses,now)) return false;
            BossReady(rt,action.RuntimeKey,now,action.CooldownMillis);
            return true;
        }
        private static Vector3 NyxFacing(HeroRuntime rt)
        {
            var direction=BossDirection(rt.Hero.agentPosition,BossCursor(rt));
            return direction==Vector3.zero ? rt.Hero.transform.forward.Flattened().normalized : direction;
        }
        private void NyxRemoveMarker(HeroRuntime rt,NyxState state,float now)
        {
            BossCancelReservation(rt,state.MarkerReservation);
            if(state.MarkerVisual!=0) PublishBossVisual(rt,state.MarkerVisual,4,state.Marker,state.Marker,2,now,now,true);
            state.MarkerReservation=state.MarkerVisual=0; state.Relocated=false;
        }
        private void NyxRemoveSeed(HeroRuntime rt,NyxState state,float now)
        {
            BossCancelReservation(rt,state.SeedReservation);
            if(state.SeedVisual!=0) PublishBossVisual(rt,state.SeedVisual,3,state.Seed,state.Seed,.25f,now,now,true);
            state.SeedReservation=state.SeedVisual=0; state.SeedAction=null;
        }
        private void NyxRemoveChannel(HeroRuntime rt,NyxState state,float now)
        {
            var channel=state.Channel;
            if(channel==null) return;
            BossCancelReservation(rt,channel.Reservation);
            PublishBossVisual(rt,channel.Visual,4,channel.Center,channel.Center,3,now,now,true);
            state.Channel=null;
        }
        private void NyxReconcileTokens(HeroRuntime rt,NyxState state,float now)
        {
            if(state.MarkerReservation!=0 && (now>=state.MarkerUntil || !BossReservationExists(rt,state.MarkerReservation))) NyxRemoveMarker(rt,state,now);
            if(state.SeedReservation!=0 && !BossReservationExists(rt,state.SeedReservation)) NyxRemoveSeed(rt,state,now);
            if(state.Channel!=null && !BossReservationExists(rt,state.Channel.Reservation)) NyxRemoveChannel(rt,state,now);
            if(state.Marks>0 && now>=state.MarksUntil) { state.Marks=0; NyxPublishMarks(rt,state,now); }
        }
        private void NyxPublishMarks(HeroRuntime rt,NyxState state,float now)
        {
            if(state.MarkVisual==0) state.MarkVisual=++rt.Boss.NextId;
            state.NextMarkVisual=now+.25f;
            PublishBossVisual(rt,state.MarkVisual,9,rt.Hero.agentPosition,rt.Hero.agentPosition,state.Marks,now,state.MarksUntil,
                state.Marks==0,element:BossElement.Light,count:state.Marks);
        }
        private void NyxPlaceMarker(HeroRuntime rt,NyxState state,Vector3 point,float now)
        {
            NyxRemoveMarker(rt,state,now);
            if(!NyxReserve(rt,NyxMarkerProfile,rt.Boss.Sequence.Reset(),now,6000)) return;
            state.MarkerReservation=BossReservationId(rt,BossProfiles.NyxSetId,NyxMarkerProfile);
            state.Marker=point; state.MarkerUntil=now+6; state.MarkerVisual=++rt.Boss.NextId;
            PublishBossVisual(rt,state.MarkerVisual,4,point,point,2,now,state.MarkerUntil,element:BossElement.Light,count:1);
        }
        private void NyxShortPull(HeroRuntime rt,Vector3 center,Vector3 destination,int maxTargets,float now)
        {
            var entities=DewPhysics.OverlapCircleAllEntities(out var handle,center,2,EnemyFilter,rt.Hero);
            try
            {
                int moved=0;
                for(int i=0,limit=Math.Min(256,entities.Count);i<limit;i++)
                    if(rt.Boss.EnemyMovement.Execute(rt,entities[i],destination,true,.6f,now,duration:.3f) && ++moved>=maxTargets) break;
            }
            finally { handle.Return(); }
        }
        // Both channels use only the additional component. Native movement is never replaced,
        // and a terrain-clipped/immune/occupied target spends only actual accepted distance.
        private float NyxAdditionalMove(HeroRuntime rt,Entity target,long life,Vector3 center,float requested)
        {
            if(requested<=0 || !BossAlive(target) || !BossNativeSameLife(target,life) || target==rt.Hero
                || target.GetRelation(rt.Hero)!=EntityRelation.Enemy || target.IsAnyBoss() || target.Status.hasCrowdControlImmunity
                || target.Status.hasUncollidable || target.Control.isDisplacing || !target.Control.isLocalMovementProcessor) return 0;
            var start=target.agentPosition; var direction=BossDirection(start,center);
            float distance=Math.Min(requested,BossDirectionDistance(start,center));
            if(direction==Vector3.zero || !BossGround(start,start+direction*distance,distance,out var point)) return 0;
            target.Control.SetAgentPosition(point);
            return Math.Min(distance,BossDirectionDistance(start,target.agentPosition));
        }
        private void NyxStartChannel(HeroRuntime rt,NyxState state,BossMoveEntry entry,BossMoveProfile profile,Vector3 point,float now)
        {
            var action=profile.Actions[0];
            if(!NyxAvailable(rt,action,now) || !NyxReserve(rt,profile.Id,rt.Boss.Sequence.Reset(),now,1500)) return;
            NyxRemoveChannel(rt,state,now);
            // The new reservation remains distinct from any previously replaced channel.
            long reservation=BossReservationId(rt,BossProfiles.NyxSetId,profile.Id);
            if(reservation==0) return;
            var channel=state.OwnedChannel;
            channel.Reservation=reservation; channel.Visual=++rt.Boss.NextId; channel.Center=point; channel.Start=now+.5f;
            channel.Last=now+.5f; channel.End=now+1.5f; channel.Profile=profile;
            channel.Damage=BossAmount(rt,entry,profile,profile.Actions[1].ChannelId);
            channel.Shield=BossAmount(rt,entry,profile,profile.Actions[2].ChannelId); channel.Magic=BossMagic(rt); channel.Selected=false;
            Array.Clear(channel.Targets,0,channel.Targets.Length); state.Channel=channel;
            state.Marks=0; state.MarksUntil=now; NyxPublishMarks(rt,state,now);
            BossReady(rt,action.RuntimeKey,now,action.CooldownMillis);
            PublishBossVisual(rt,state.Channel.Visual,4,point,point,3,state.Channel.Start,state.Channel.End,element:BossElement.Light,count:3,budget:2);
        }
        private void NyxTickChannel(HeroRuntime rt,NyxState state,float now)
        {
            var channel=state.Channel;
            if(channel==null || now<channel.Start) return;
            if(!channel.Selected)
            {
                channel.Selected=true;
                var entities=DewPhysics.OverlapCircleAllEntities(out var handle,channel.Center,3,EnemyFilter,rt.Hero);
                try
                {
                    int slot=0;
                    for(int i=0,limit=Math.Min(256,entities.Count);i<limit;i++)
                    {
                        var target=entities[i];
                        if(!BossAlive(target) || target.IsAnyBoss() || target.Status.hasCrowdControlImmunity || target.Status.hasUncollidable) continue;
                        channel.Targets[slot++]=new NyxMovedTarget { Target=target,Life=BossNativeActorLife(target) };
                        if(slot==channel.Targets.Length) break;
                    }
                }
                finally { handle.Return(); }
            }
            float end=Math.Min(now,channel.End),dt=Math.Max(0,end-channel.Last);
            channel.Last=end;
            for(int i=0;i<channel.Targets.Length;i++)
            {
                ref var row=ref channel.Targets[i];
                if(BossAlive(row.Target) && BossDirectionDistance(channel.Center,row.Target.agentPosition)<=3)
                    row.Moved+=NyxAdditionalMove(rt,row.Target,row.Life,channel.Center,Math.Min(2-row.Moved,2*dt));
            }
            if(now<channel.End) return;
            long channelReservation=channel.Reservation;
            rt.Boss.Shapes.Execute(this,rt,channel.Profile.Actions[1],channel.Center,channel.Center,channel.Damage,channel.Magic);
            if(!BossAlive(rt.Hero) || rt.Boss.Build==null || !ReferenceEquals(state.Channel,channel) || channel.Reservation!=channelReservation) return;
            rt.Boss.Defense.Execute(this,rt,channel.Profile.Id,channel.Profile.Actions[2],channel.Shield,now);
            NyxRemoveChannel(rt,state,now);
        }
        private void NyxTickSeed(HeroRuntime rt,NyxState state,float now)
        {
            if(state.SeedAction==null || now<state.SeedDue) return;
            var action=state.SeedAction; state.SeedAction=null;
            PublishBossVisual(rt,state.SeedVisual,3,state.Seed,state.Seed,.25f,now,now,true);
            state.SeedVisual=0;
            rt.Boss.Projectiles.Execute(this,rt,BossProfiles.NyxSetId,action,state.Seed,state.Seed+state.SeedDirection,
                state.SeedDamage,state.SeedMagic,state.SeedDue,maxHitsPerTarget:1,profile:"boss_nyx.charm",delayedAlready:true,reservationId:state.SeedReservation);
        }
        private void NyxStopDash(HeroRuntime rt,NyxState state,bool cancel)
        {
            if(state.Dash==null) return;
            rt.Hero.Control.ClientEvent_OnDisplacementFinished-=state.DashStopped;
            rt.Hero.Control.ClientEvent_OnDisplacementCanceled-=state.DashStopped;
            if(cancel && ReferenceEquals(rt.Hero.Control.ongoingDisplacement,state.Dash)) rt.Hero.Control.CancelOngoingDisplacement();
            state.Dash=null; state.DashHits.Clear();
        }
        private void NyxTickDash(HeroRuntime rt,NyxState state,float now)
        {
            if(state.Dash==null) return;
            if(!BossFind(rt,"boss_nyx.feet",out _,out _) || rt.Hero.Control.ongoingDisplacement!=state.Dash && state.Dash.isAlive)
            { NyxStopDash(rt,state,true); return; }
            var end=rt.Hero.agentPosition; var delta=end-state.DashLast; delta.y=0;
            if(delta.sqrMagnitude>.000001f)
            {
                var entities=DewPhysics.SphereCastAllEntities(out var handle,state.DashLast,.3f,delta.normalized,delta.magnitude,EnemyFilter,rt.Hero);
                try
                {
                    for(int i=0,limit=Math.Min(256,entities.Count);i<limit;i++)
                    {
                        var target=entities[i];
                        if(!BossAlive(target)) continue;
                        long life=BossNativeActorLife(target);
                        if(state.DashHits.Count>=64) break;
                        if(state.DashHits.TryGetValue(target,out var previous) && previous==life) continue;
                        state.DashHits[target]=life; BossDamage(rt,target,state.DashDamage,state.DashMagic,BossElement.Light);
                        if(state.Dash==null || !BossAlive(rt.Hero) || rt.Boss.Build==null) return;
                    }
                }
                finally { handle.Return(); }
            }
            state.DashLast=end;
            if(state.Dash!=null && (!state.Dash.isAlive || now>=state.DashUntil)) NyxStopDash(rt,state,false);
        }
        private void DispatchNyxBoss(HeroRuntime rt,BossEvent kind,long activation,Entity victim,Vector3 point,float now)
        {
            if(!NetworkServer.active || activation==0 || !BossAlive(rt.Hero)) return;
            bool enabled=false;
            for(int i=0;i<rt.Powers.Build.BossMoves.Count;i++)
                if(rt.Powers.Build.BossMoves[i].SetId==BossProfiles.NyxSetId) { enabled=true; break; }
            if(!enabled) return;
            var state=NyxGet(rt); if(state==null) return; NyxReconcileTokens(rt,state,now);
            var origin=rt.Hero.agentPosition;
            BossMoveEntry finishEntry=null; BossMoveProfile finishProfile=null; Vector3 finishPoint=default;
            if(kind==BossEvent.MemoryUse && state.Marks>=3 && BossFind(rt,"boss_nyx.stage6",out finishEntry,out finishProfile))
                if(!NyxAvailable(rt,finishProfile.Actions[0],now) || !BossGround(origin,BossCursor(rt),8,out finishPoint)) finishProfile=null;
            if(BossFind(rt,"boss_nyx.stage2",out var stageEntry,out var stageProfile))
            {
                if(kind==BossEvent.MainHit && state.MarkerReservation!=0 && NyxAvailable(rt,stageProfile.Actions[1],now))
                {
                    var center=state.Marker;
                    long consumedReservation=state.MarkerReservation;
                    var first=stageProfile.Actions[1]; var second=stageProfile.Actions[2];
                    float amount=BossAmount(rt,stageEntry,stageProfile,first.ChannelId); bool magic=BossMagic(rt);
                    var pulses=rt.Boss.Sequence.Reset();
                    pulses.Add(new BossScheduledPulse(first,center,center,amount,magic,350));
                    pulses.Add(new BossScheduledPulse(second,center,center,amount,magic,750));
                    if(NyxCommitReserve(rt,stageProfile,first,pulses,now))
                    {
                        if(state.MarkerReservation==consumedReservation) NyxRemoveMarker(rt,state,now);
                        if(BossFind(rt,"boss_nyx.stage3",out _,out _))
                        {
                            NyxShortPull(rt,center,center,64,now);
                            state.Marks=Math.Min(3,state.Marks+1); state.MarksUntil=now+12; NyxPublishMarks(rt,state,now);
                        }
                    }
                }
                else if(kind==BossEvent.MemoryUse && BossGround(origin,BossCursor(rt),8,out var marker)) NyxPlaceMarker(rt,state,marker,now);
                else if(kind==BossEvent.MovementCompleted && state.MarkerReservation!=0 && !state.Relocated && BossFind(rt,"boss_nyx.stage3",out _,out _)
                    && BossGround(point,point+rt.Hero.transform.forward.Flattened().normalized*2,2,out var relocated))
                {
                    state.Marker=relocated; state.Relocated=true;
                    PublishBossVisual(rt,state.MarkerVisual,4,relocated,relocated,2,now,state.MarkerUntil,element:BossElement.Light,count:1);
                }
            }
            for(int bossMoveIndex=0;bossMoveIndex<rt.Powers.Build.BossMoves.Count;bossMoveIndex++)
            {
                var entry=rt.Powers.Build.BossMoves[bossMoveIndex];
                if(entry.SetId!=BossProfiles.NyxSetId || !BossProfiles.TryGetMove(entry.ProfileId,out var profile)
                    || profile.Id=="boss_nyx.stage2" || profile.Id=="boss_nyx.stage3" || profile.Id=="boss_nyx.stage6") continue;
                var action=profile.Actions[0]; if(action.Event!=kind || !NyxAvailable(rt,action,now)) continue;
                float amount=BossAmount(rt,entry,profile,action.ChannelId); bool magic=BossMagic(rt);
                if(profile.Id=="boss_nyx.weapon")
                {
                    if(!BossGround(origin,point,8,out var center)) continue;
                    var echo=profile.Actions[1];
                    var pulses=rt.Boss.Sequence.Reset();
                    pulses.Add(new BossScheduledPulse(action,center,center,amount,magic,350));
                    pulses.Add(new BossScheduledPulse(echo,center,center,BossAmount(rt,entry,profile,echo.ChannelId),magic,750));
                    NyxCommitReserve(rt,profile,action,pulses,now);
                }
                else if(profile.Id=="boss_nyx.armor")
                {
                    var burst=profile.Actions[1];
                    var pulses=rt.Boss.Sequence.Reset();
                    pulses.Add(new BossScheduledPulse(burst,origin,origin,BossAmount(rt,entry,profile,burst.ChannelId),magic,400));
                    if(NyxCommitReserve(rt,profile,action,pulses,now))
                        rt.Boss.Defense.Execute(this,rt,profile.Id,action,amount,now);
                }
                else if(profile.Id=="boss_nyx.charm")
                {
                    if(!BossGround(origin,BossCursor(rt),8,out var center)) continue;
                    NyxRemoveSeed(rt,state,now);
                    if(!NyxReserve(rt,profile.Id,rt.Boss.Sequence.Reset(),now,1100)) continue;
                    state.SeedReservation=BossReservationId(rt,BossProfiles.NyxSetId,profile.Id); state.Seed=center;
                    state.SeedAction=action; state.SeedDue=now+.6f; state.SeedDirection=NyxFacing(rt); state.SeedDamage=amount; state.SeedMagic=magic;
                    state.SeedVisual=++rt.Boss.NextId;
                    PublishBossVisual(rt,state.SeedVisual,3,center,center,.25f,state.SeedDue,state.SeedDue,element:BossElement.Light,count:3);
                    BossReady(rt,action.RuntimeKey,now,action.CooldownMillis);
                }
                else if(profile.Id=="boss_nyx.head")
                {
                    if(BossGround(point,point+rt.Hero.transform.forward.Flattened().normalized*2,2,out var center))
                    {
                        var pulses=rt.Boss.Sequence.Reset();
                        pulses.Add(new BossScheduledPulse(action,center,center,amount,magic,300));
                        NyxCommitReserve(rt,profile,action,pulses,now);
                    }
                }
                else if(profile.Id=="boss_nyx.hands")
                {
                    if(!BossGround(point,point,0,out var center)) continue;
                    var pulses=rt.Boss.Sequence.Reset();
                    pulses.Add(new BossScheduledPulse(action,center,center,amount,magic,400));
                    if(NyxCommitReserve(rt,profile,action,pulses,now)) NyxShortPull(rt,center,origin,3,now);
                }
                else if(profile.Id=="boss_nyx.feet" && rt.Boss.Movement.Execute(this,rt,action,origin+NyxFacing(rt)*3,now))
                {
                    NyxStopDash(rt,state,false);
                    state.Dash=rt.Hero.Control.ongoingDisplacement; state.DashLast=origin; state.DashUntil=now+.2f;
                    state.DashDamage=amount; state.DashMagic=magic;
                    rt.Hero.Control.ClientEvent_OnDisplacementFinished+=state.DashStopped;
                    rt.Hero.Control.ClientEvent_OnDisplacementCanceled+=state.DashStopped;
                    BossReady(rt,action.RuntimeKey,now,action.CooldownMillis);
                }
            }
            if(finishProfile!=null && state.Marks>=3) NyxStartChannel(rt,state,finishEntry,finishProfile,finishPoint,now);
            NyxReconcileTokens(rt,state,now);
        }
        private void TickNyxBoss(HeroRuntime rt,float now)
        {
            TickNyxWorlds(rt,now);
            if(!_nyxStates.TryGetValue(rt,out var state)) return;
            NyxReconcileTokens(rt,state,now);
            if(!BossAlive(rt.Hero) || rt.Boss.Build==null) return;
            NyxTickSeed(rt,state,now); NyxTickChannel(rt,state,now); NyxTickDash(rt,state,now);
            if(state.Marks>0 && now>=state.NextMarkVisual) NyxPublishMarks(rt,state,now);
        }
        private void ClearNyxBoss(HeroRuntime rt,bool preserveRewards=false)
        {
            ClearNyxWorlds(rt,preserveRewards);
            if(!_nyxStates.TryGetValue(rt,out var state)) return;
            NyxRemoveMarker(rt,state,Time.time); NyxRemoveSeed(rt,state,Time.time); NyxRemoveChannel(rt,state,Time.time); NyxStopDash(rt,state,true);
            state.Marks=0; NyxPublishMarks(rt,state,Time.time);
            rt.Boss.Defense.CancelSource(this,rt,"boss_nyx.armor"); rt.Boss.Defense.CancelSource(this,rt,"boss_nyx.stage6");
            _nyxStates.Remove(rt);
            state.Reset(); _nyxPool.Return(state);
        }
    }
}
