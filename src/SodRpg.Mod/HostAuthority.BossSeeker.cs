using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class SeekerState
        {
            internal int Phase;
            internal long MainActivation, DoubleVisual, PhaseVisual;
            internal float DoubleUntil;
            internal Vector3 DoublePoint;
            internal bool Echo;
            internal string DoubleProfile;
            internal void Reset()
            { Phase=0; MainActivation=DoubleVisual=PhaseVisual=0; DoubleUntil=0; DoublePoint=default; Echo=false; DoubleProfile=null; }
        }
        private readonly Dictionary<HeroRuntime,SeekerState> _seekerStates = new Dictionary<HeroRuntime,SeekerState>(64);
        private readonly BossObjectPool<SeekerState> _seekerPool = new BossObjectPool<SeekerState>(64,()=>new SeekerState());
        private SeekerState SeekerGet(HeroRuntime rt)
        {
            if (!_seekerStates.TryGetValue(rt,out var state))
            {
                if(_seekerStates.Count>=64 || (state=_seekerPool.Rent())==null) return null;
                _seekerStates.Add(rt,state);
            }
            return state;
        }
        private static bool SeekerReady(HeroRuntime rt,BossAction action,float now) => rt.Boss.Ready.TryGetValue(action.RuntimeKey,out var due) ? now>=due : rt.Boss.Ready.Count<128;
        private static void SeekerCommit(HeroRuntime rt,BossAction action,float now) => BossReady(rt,action.RuntimeKey,now,action.CooldownMillis);
        private Entity SeekerNearestEnemy(HeroRuntime rt,float radius)
        {
            ListReturnHandle<Entity> handle;
            var enemies=DewPhysics.OverlapCircleAllEntities(out handle,rt.Hero.agentPosition,radius,EnemyFilter,rt.Hero);
            Entity nearest=null; float best=float.MaxValue;
            try
            {
                for(int i=0,limit=Math.Min(256,enemies.Count);i<limit;i++)
                {
                    var enemy=enemies[i]; if(!BossAlive(enemy)) continue;
                    float d=(enemy.agentPosition-rt.Hero.agentPosition).Flattened().sqrMagnitude;
                    if(d<best || d==best && (nearest==null || enemy.netId<nearest.netId)) { best=d; nearest=enemy; }
                }
            }
            finally { handle.Return(); }
            return nearest;
        }
        private void SeekerRemoveDouble(HeroRuntime rt,SeekerState state,float now)
        {
            if(state.DoubleVisual!=0) PublishBossVisual(rt,state.DoubleVisual,8,state.DoublePoint,state.DoublePoint,.6f,now,now,true);
            state.DoubleVisual=0; state.DoubleUntil=0; state.Echo=false; state.DoubleProfile=null;
        }
        private void SeekerDouble(HeroRuntime rt,SeekerState state,Vector3 point,float now,int life,bool echo,string profile)
        {
            SeekerRemoveDouble(rt,state,now);
            state.DoublePoint=point; state.DoubleUntil=now+life/1000f; state.Echo=echo; state.DoubleProfile=profile; state.DoubleVisual=++rt.Boss.NextId;
            PublishBossVisual(rt,state.DoubleVisual,8,point,point,.6f,now,state.DoubleUntil,count:echo?1:0);
        }
        private bool SeekerPhaseAttack(HeroRuntime rt,BossMoveProfile phase,int index,Vector3 origin,Vector3 point,float amount,float now,string source)
        {
            if(!BossGround(origin,point,8,out var legal)) return false;
            var action=phase.Actions[index];
            if(index==0) return rt.Boss.Projectiles.Execute(this,rt,BossProfiles.SeekerSetId,action,origin,legal,amount,BossMagic(rt),now,explodeAtEnd:true,terminalRadius:1.5f,profile:source);
            var pulses=rt.Boss.Sequence.Reset();
            pulses.Add(new BossScheduledPulse(action,legal,legal,amount,BossMagic(rt),action.DelayMillis));
            return BossReserveSequence(rt,BossProfiles.SeekerSetId,source,pulses,now,1);
        }
        private void DispatchSeekerBoss(HeroRuntime rt,BossEvent kind,long activation,Entity victim,Vector3 point,float now)
        {
            if(!NetworkServer.active || activation==0 || !BossAlive(rt.Hero)) return;
            bool owned=false;
            for(int i=0;i<rt.Boss.Build.BossMoves.Count;i++) if(rt.Boss.Build.BossMoves[i].SetId==BossProfiles.SeekerSetId) { owned=true; break; }
            if(!owned) return;
            var state=SeekerGet(rt); if(state==null) return; var origin=rt.Hero.agentPosition;
            if(state.DoubleUntil>0 && now>=state.DoubleUntil) SeekerRemoveDouble(rt,state,now);
            if(kind==BossEvent.MainHit && state.MainActivation!=activation && BossFind(rt,"boss_seeker.stage2",out var phaseEntry,out var phase))
            {
                state.MainActivation=activation; int index=state.Phase; state.Phase=(state.Phase+1)%3;
                if(state.PhaseVisual==0) state.PhaseVisual=++rt.Boss.NextId;
                PublishBossVisual(rt,state.PhaseVisual,9,origin,origin,1,now,now+3,count:index+1,element:index==2?BossElement.Fire:BossElement.Neutral);
                if(SeekerReady(rt,phase.Actions[0],now) && SeekerPhaseAttack(rt,phase,index,origin,point,BossAmount(rt,phaseEntry,phase,"SeekerPhase"),now,phase.Id)) SeekerCommit(rt,phase.Actions[0],now);
                if(state.Echo && BossFind(rt,"boss_seeker.stage3",out var echoEntry,out var echo))
                {
                    SeekerPhaseAttack(rt,phase,index,state.DoublePoint,point,BossAmount(rt,echoEntry,echo,"SeekerEcho"),now,echo.Id);
                    SeekerRemoveDouble(rt,state,now);
                }
            }
            if(kind==BossEvent.MovementCompleted && rt.Boss.MovementOriginValid && BossFind(rt,"boss_seeker.stage3",out _,out var doubleProfile) && SeekerReady(rt,doubleProfile.Actions[0],now))
            {
                SeekerDouble(rt,state,rt.Boss.MovementOrigin,now,3000,true,doubleProfile.Id); SeekerCommit(rt,doubleProfile.Actions[0],now);
            }
            Entity nearest=null; bool searched=false;
            for(int bossMoveIndex=0;bossMoveIndex<rt.Powers.Build.BossMoves.Count;bossMoveIndex++)
            {
                var entry=rt.Powers.Build.BossMoves[bossMoveIndex];
                if(entry.SetId!=BossProfiles.SeekerSetId || entry.ProfileId=="boss_seeker.stage2" || entry.ProfileId=="boss_seeker.stage3" || !BossProfiles.TryGetMove(entry.ProfileId,out var profile)) continue;
                var action=profile.Actions[0]; if(action.Event!=kind || !SeekerReady(rt,action,now)) continue;
                float amount=BossAmount(rt,entry,profile,action.ChannelId); bool success=false;
                if(profile.Id=="boss_seeker.armor")
                {
                    success=rt.Boss.Defense.Execute(this,rt,profile.Id,action,amount,now);
                    if(success) SeekerDouble(rt,state,origin,now,2000,false,profile.Id);
                }
                else if(profile.Id=="boss_seeker.weapon")
                {
                    if(BossGround(origin,point,8,out var legal)) success=rt.Boss.Projectiles.Execute(this,rt,profile.SetId,action,origin,legal,amount,BossMagic(rt),now,explodeAtEnd:true,terminalRadius:1.5f,profile:profile.Id);
                }
                else if(profile.Id=="boss_seeker.feet")
                {
                    if(rt.Boss.MovementOriginValid)
                    {
                        var departure=rt.Boss.MovementOrigin; var pulses=rt.Boss.Sequence.Reset();
                        pulses.Add(new BossScheduledPulse(action,departure,departure,amount,BossMagic(rt),300,stunMillis:250));
                        success=BossReserveSequence(rt,profile.SetId,profile.Id,pulses,now,1);
                    }
                }
                else if(profile.Id=="boss_seeker.head")
                {
                    if(BossGround(origin,point,8,out var legal))
                    {
                        var pulses=rt.Boss.Sequence.Reset();
                        pulses.Add(new BossScheduledPulse(action,legal,legal,amount,BossMagic(rt),400));
                        success=BossReserveSequence(rt,profile.SetId,profile.Id,pulses,now,1);
                    }
                }
                else
                {
                    if(!searched) { nearest=SeekerNearestEnemy(rt,8); searched=true; }
                    if(nearest==null || !BossGround(origin,nearest.agentPosition,8,out var legal)) continue;
                    if(profile.Id=="boss_seeker.charm") success=rt.Boss.Projectiles.Execute(this,rt,profile.SetId,action,origin,legal,amount,BossMagic(rt),now,explodeAtEnd:true,terminalRadius:1.5f,profile:profile.Id,adoptedTarget:nearest,explodeOnHit:true,homing:true,expireAtLifetime:true);
                    else if(profile.Id=="boss_seeker.hands")
                    {
                        var pulses=rt.Boss.Sequence.Reset();
                        pulses.Add(new BossScheduledPulse(action,legal,legal,amount,BossMagic(rt),500));
                        pulses.Add(new BossScheduledPulse(action,legal,legal,amount,BossMagic(rt),1500));
                        success=BossReserveSequence(rt,profile.SetId,profile.Id,pulses,now,1,2000);
                    }
                    else if(profile.Id=="boss_seeker.stage6")
                    {
                        var direction=BossDirection(origin,legal); if(direction==Vector3.zero) direction=rt.Hero.transform.forward.Flattened().normalized;
                        var end=legal+direction*4;
                        if(BossGround(legal,end,4,out end))
                        {
                            var pulses=rt.Boss.Sequence.Reset();
                            pulses.Add(new BossScheduledPulse(action,legal,end,amount/3,BossMagic(rt),500));
                            pulses.Add(new BossScheduledPulse(action,legal,end,amount/3,BossMagic(rt),750));
                            pulses.Add(new BossScheduledPulse(action,legal,end,amount/3,BossMagic(rt),1000));
                            success=BossReserveSequence(rt,profile.SetId,profile.Id,pulses,now,1);
                            if(success) rt.Boss.Defense.Execute(this,rt,"boss_seeker.stage6.shield",profile.Actions[1],BossAmount(rt,entry,profile,"SeekerClawGuard"),now);
                        }
                    }
                }
                if(success) SeekerCommit(rt,action,now);
            }
        }
        private void TickSeekerBoss(HeroRuntime rt,float now)
        {
            if(_seekerStates.TryGetValue(rt,out var state))
            {
                if(state.DoubleUntil>0 && (now>=state.DoubleUntil || !BossFind(rt,state.DoubleProfile,out _,out _))) SeekerRemoveDouble(rt,state,now);
                if(!BossFind(rt,"boss_seeker.stage2",out _,out _)) state.Phase=0;
            }
            TickSeekerSoulPrison(rt,now);
        }
        private void ClearSeekerBoss(HeroRuntime rt,bool preserveRewards=false)
        {
            if(_seekerStates.TryGetValue(rt,out var state))
            {
                SeekerRemoveDouble(rt,state,Time.time); _seekerStates.Remove(rt);
                state.Reset(); _seekerPool.Return(state);
            }
            ClearSeekerSoulPrison(rt,preserveRewards);
        }
    }
}
