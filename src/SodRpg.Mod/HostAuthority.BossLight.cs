using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class LightBeamState
        {
            internal readonly long[] Hits = new long[8];
            internal BossAction Action, Terminal, PhaseAction, PhaseShield, PhaseCrystal;
            internal string Profile;
            internal Vector3 Origin, Direction;
            internal Vector3 FirstCrystal, SecondCrystal;
            internal float PhaseAmount, ShieldAmount;
            internal float Due, Until, StartAngle, EndAngle, LastAngle, Amount, TerminalAmount;
            internal bool Magic, Active, Started;
            internal int HitCount;
            internal long Visual;
        }
        private struct LightCrystal
        {
            internal Vector3 Point;
            internal float Due, Until, Amount;
            internal bool Armed, Magic;
            internal long Visual;
            internal BossAction Action;
        }
        private sealed class LightCombat
        {
            internal readonly LightBeamState[] Beams = new LightBeamState[12];
            internal readonly LightCrystal[] Crystals = new LightCrystal[2];
            internal readonly RaycastHit2D[] Walls = new RaycastHit2D[32];
            internal readonly long[,] Seen = new long[4,64];
            internal readonly int[] Next = new int[4];
            internal long OwnerLife, Epoch, ChargeVisual;
            internal Room Room;
            internal string Run;
            internal int Charges, HandsCount, Mask;
            internal float ChargeUntil, HandsUntil, ChargeRefresh;
            internal LightCombat() { for (int i=0;i<Beams.Length;i++) Beams[i]=new LightBeamState(); }
            internal bool Admit(int row,long id)
            {
                for(int i=0;i<64;i++) if(Seen[row,i]==id) return false;
                Seen[row,Next[row]]=id; Next[row]=(Next[row]+1)%64; return true;
            }
        }
        private static readonly string[] LightProfiles =
        {
            "boss_light_elemental.weapon","boss_light_elemental.armor","boss_light_elemental.charm",
            "boss_light_elemental.head","boss_light_elemental.hands","boss_light_elemental.feet",
            "boss_light_elemental.stage2","boss_light_elemental.stage3","boss_light_elemental.stage6",
        };
        private readonly BossObjectPool<LightCombat> _lightPool = new BossObjectPool<LightCombat>(64,()=>new LightCombat());
        private readonly Dictionary<HeroRuntime,LightCombat> _lightCombat = new Dictionary<HeroRuntime,LightCombat>(64);
        private static bool LightAvailable(HeroRuntime rt,BossAction action,float now)
            => rt.Boss.Ready.TryGetValue(action.RuntimeKey,out var due) ? now>=due : rt.Boss.Ready.Count<128;
        private static void LightCommit(HeroRuntime rt,BossAction action,float now)
        {
            if(rt.Boss.Ready.ContainsKey(action.RuntimeKey) || rt.Boss.Ready.Count<128)
                rt.Boss.Ready[action.RuntimeKey]=now+action.CooldownMillis/1000f;
        }
        private static float LightAmount(HeroRuntime rt,BossMoveEntry entry,BossMoveProfile profile,string channel)
            => Math.Max(0,Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower))
                * BossCoefficient(entry,profile,channel);
        private LightCombat LightState(HeroRuntime rt)
        {
            if(_lightCombat.TryGetValue(rt,out var state)) return state;
            int mask=0;
            for(int i=0;i<LightProfiles.Length;i++) if(BossFind(rt,LightProfiles[i],out _,out _)) mask|=1<<i;
            if(mask==0 || _lightCombat.Count>=64 || (state=_lightPool.Rent())==null) return null;
            state.Mask=mask; state.Epoch=rt.Boss.Epoch; state.OwnerLife=BossNativeActorLife(rt.Hero);
            state.Room=rt.Boss.Room; state.Run=rt.Boss.Run;
            _lightCombat.Add(rt,state); return state;
        }
        private static Vector3 LightDirection(HeroRuntime rt,BossEvent kind,Vector3 point)
        {
            Vector3 direction=kind==BossEvent.MemoryUse && rt.Boss.MemoryDirectionValid ? rt.Boss.MemoryDirection
                : kind==BossEvent.MovementCompleted && rt.Boss.MovementOriginValid ? point-rt.Boss.MovementOrigin
                : point-rt.Hero.agentPosition;
            direction.y=0;
            if(!BossFinite(direction) || direction.sqrMagnitude<.0001f) direction=rt.Hero.transform.forward;
            direction.y=0; return direction.sqrMagnitude>.0001f ? direction.normalized : Vector3.forward;
        }
        private void DispatchLightBoss(HeroRuntime rt,BossEvent kind,long activation,Entity victim,Vector3 point,float now)
        {
            if(!NetworkServer.active || rt.Boss.Build==null || !BossAlive(rt.Hero) || activation<=0) return;
            int row=kind==BossEvent.MainHit?0:kind==BossEvent.MemoryUse?1:kind==BossEvent.MovementCompleted?2:kind==BossEvent.NativeDamageTaken?3:-1;
            if(row<0) return;
            var state=LightState(rt); if(state==null || !state.Admit(row,activation)) return;
            var origin=kind==BossEvent.MovementCompleted?point:rt.Hero.agentPosition;
            if(!BossFinite(origin) || row==2 && !rt.Boss.MovementOriginValid) return;
            var direction=LightDirection(rt,kind,point);
            if(row<2) LightChargeInput(rt,state,origin,direction,now);
            for(int index=0;index<6;index++)
            {
                if(!BossFind(rt,LightProfiles[index],out var entry,out var profile)) continue;
                var action=profile.Actions[0];
                if(action.Event!=kind) continue;
                if(index==4)
                {
                    if(now>=state.HandsUntil) state.HandsCount=0;
                    if(state.HandsCount==0) state.HandsUntil=now+6f;
                    state.HandsCount=Math.Min(3,state.HandsCount+1);
                    if(state.HandsCount<3) continue;
                }
                if(!LightAvailable(rt,action,now)) continue;
                float amount=LightAmount(rt,entry,profile,action.ChannelId); bool success=false;
                if(index==1) success=rt.Boss.Defense.Execute(this,rt,profile.Id,action,amount,now);
                else if(index==2) success=LightPlantCrystal(rt,state,action,origin,direction,amount,now);
                else if(index==3)
                {
                    if(!BossGround(origin,point,8f,out var center)) continue;
                    var sequence=rt.Boss.Sequence.Reset();
                    sequence.Add(new BossScheduledPulse(action,center,center,amount/2f,BossMagic(rt),450));
                    sequence.Add(new BossScheduledPulse(action,center,center,amount/2f,BossMagic(rt),750));
                    success=BossReserveSequence(rt,BossProfiles.LightSetId,profile.Id,sequence,now);
                }
                else if(!LightBeamActive(state,profile.Id) && LightFreeBeams(state)>=action.Count)
                {
                    for(int i=0;i<action.Count;i++)
                    {
                        float start=index==4?-25f:action.Count==3?-20f+i*20f:0f;
                        float end=index==4?25f:start;
                        LightAddBeam(rt,state,profile.Id,action,origin,direction,amount/action.Count,now+action.DelayMillis/1000f,start,end);
                    }
                    success=true;
                }
                if(success) { LightCommit(rt,action,now); if(index==4) state.HandsCount=0; }
            }
        }
        private void LightChargeInput(HeroRuntime rt,LightCombat state,Vector3 origin,Vector3 direction,float now)
        {
            if(!BossFind(rt,LightProfiles[6],out var entry,out var profile)) return;
            if(now>=state.ChargeUntil) state.Charges=0;
            var action=profile.Actions[0];
            if(state.Charges<3)
            {
                state.Charges++; state.ChargeUntil=now+6f; LightPublishCharges(rt,state,now); return;
            }
            if(!LightAvailable(rt,action,now) || LightBeamActive(state,profile.Id) || LightFreeBeams(state)==0) return;
            var beam=LightAddBeam(rt,state,profile.Id,action,origin,direction,LightAmount(rt,entry,profile,action.ChannelId),now+.35f,0,0);
            if(BossFind(rt,LightProfiles[7],out var terminalEntry,out var terminalProfile))
            {
                beam.Terminal=terminalProfile.Actions[0]; beam.TerminalAmount=LightAmount(rt,terminalEntry,terminalProfile,beam.Terminal.ChannelId);
            }
            state.Charges=0; state.ChargeUntil=0; LightPublishCharges(rt,state,now); LightCommit(rt,action,now);
            if(!BossFind(rt,LightProfiles[8],out var phaseEntry,out var phaseProfile)) return;
            var sweep=phaseProfile.Actions[0];
            if(!LightAvailable(rt,sweep,now) || LightBeamActive(state,phaseProfile.Id) || LightFreeBeams(state)<3) return;
            if(!LightSupportPoints(origin,direction,out var first,out var second)) return;
            float amount=LightAmount(rt,phaseEntry,phaseProfile,sweep.ChannelId)/3f;
            beam.PhaseAction=sweep; beam.PhaseShield=phaseProfile.Actions[1]; beam.PhaseCrystal=phaseProfile.Actions[2];
            beam.PhaseAmount=amount; beam.ShieldAmount=LightAmount(rt,phaseEntry,phaseProfile,beam.PhaseShield.ChannelId);
            beam.FirstCrystal=first; beam.SecondCrystal=second;
            LightCommit(rt,sweep,now);
        }
        private static bool LightSupportPoints(Vector3 origin,Vector3 direction,out Vector3 first,out Vector3 second)
        {
            var side=Quaternion.Euler(0,90,0)*direction;
            bool a=BossGround(origin,origin+side*2f,2f,out first);
            bool b=BossGround(origin,origin-side*2f,2f,out second);
            return a && b;
        }
        private void LightStartPhase(HeroRuntime rt,LightCombat state,LightBeamState source,float now)
        {
            if(source.PhaseAction==null || !BossFind(rt,LightProfiles[8],out _,out _)
                || LightBeamActive(state,LightProfiles[8]) || LightFreeBeams(state)<3) return;
            for(int i=0;i<3;i++)
            {
                var beam=LightAddBeam(rt,state,LightProfiles[8],source.PhaseAction,source.Origin,source.Direction,
                    source.PhaseAmount,now+.3f+i*.4f,-45f+i*30f,-15f+i*30f);
                beam.Magic=source.Magic;
            }
            LightClearCrystals(rt,state,now);
            LightSetCrystal(rt,state,0,source.FirstCrystal,now,source.PhaseCrystal,0,false);
            LightSetCrystal(rt,state,1,source.SecondCrystal,now,source.PhaseCrystal,0,false);
            rt.Boss.Defense.Execute(this,rt,LightProfiles[8],source.PhaseShield,source.ShieldAmount,now);
        }
        private bool LightPlantCrystal(HeroRuntime rt,LightCombat state,BossAction action,Vector3 origin,Vector3 direction,float amount,float now)
        {
            // Support crystals occupy the same two owner slots; the charm never adds a third.
            if(state.Crystals[0].Visual!=0 || state.Crystals[1].Visual!=0 || !BossGround(origin,origin+direction*2f,2f,out var legal)) return false;
            LightSetCrystal(rt,state,0,legal,now,action,amount,true); return true;
        }
        private void LightSetCrystal(HeroRuntime rt,LightCombat state,int slot,Vector3 point,float now,BossAction action,float amount,bool armed)
        {
            state.Crystals[slot]=new LightCrystal { Point=point,Due=now+.5f,Until=now+3f,Amount=amount,Magic=BossMagic(rt),Armed=armed,Action=action,Visual=++rt.Boss.NextId };
            PublishBossVisual(rt,state.Crystals[slot].Visual,6,point,point,.5f,now,now+3f,element:BossElement.Light,count:1);
        }
        private static int LightFreeBeams(LightCombat state)
        { int n=0; for(int i=0;i<state.Beams.Length;i++) if(!state.Beams[i].Active) n++; return n; }
        private static bool LightBeamActive(LightCombat state,string profile)
        { for(int i=0;i<state.Beams.Length;i++) if(state.Beams[i].Active && state.Beams[i].Profile==profile) return true; return false; }
        private LightBeamState LightAddBeam(HeroRuntime rt,LightCombat state,string profile,BossAction action,Vector3 origin,Vector3 direction,float amount,float due,float start,float end)
        {
            for(int i=0;i<state.Beams.Length;i++)
            {
                var beam=state.Beams[i]; if(beam.Active) continue;
                beam.Active=true; beam.Started=false; beam.HitCount=0; beam.Action=action; beam.Profile=profile;
                beam.Terminal=null; beam.PhaseAction=beam.PhaseShield=beam.PhaseCrystal=null;
                beam.TerminalAmount=beam.PhaseAmount=beam.ShieldAmount=0; beam.Origin=origin; beam.Direction=direction;
                beam.Due=due; beam.Until=due+action.LifetimeMillis/1000f; beam.StartAngle=start; beam.EndAngle=end; beam.LastAngle=start;
                beam.Amount=amount; beam.Magic=BossMagic(rt); beam.Visual=++rt.Boss.NextId;
                var aim=Quaternion.Euler(0,start,0)*direction;
                float length=LightClip(state,origin,aim,action.RangeMilli/1000f,action.WidthMilli/2000f);
                PublishBossVisual(rt,beam.Visual,2,origin,origin+aim*length,action.WidthMilli/2000f,Time.time,due,
                    element:BossElement.Light,shape:BossShape.Line,range:length,width:action.WidthMilli/1000f,angle:end-start,count:1);
                return beam;
            }
            return null;
        }
        private static float LightClip(LightCombat state,Vector3 origin,Vector3 direction,float range,float radius)
        {
            var legal=Dew.GetValidAgentDestination_LinearSweep(origin,origin+direction*range);
            if(!BossFinite(legal) || Mathf.Abs(legal.y-origin.y)>2f) return 0;
            range=Math.Min(range,BossDirectionDistance(origin,legal));
            if(Physics.Raycast(origin+Vector3.up*.2f,direction,out var ground,range,LayerMasks.Ground|LayerMasks.IncludeInNavigation)) range=ground.distance;
            var filter=new ContactFilter2D { useLayerMask=true,layerMask=LayerMasks.CollidableWithProjectile,useTriggers=true };
            int walls=Physics2D.CircleCast(origin.ToXY(),radius,direction.ToXY(),filter,state.Walls,range);
            // Saturated broadphase is a closed admission failure, never a guessed unobstructed beam.
            if(walls>=state.Walls.Length) return 0;
            for(int i=0;i<walls;i++)
                if(!DewPhysics.TryGetEntity(state.Walls[i].collider,out _) && DewPhysics.TryGetCollidableWithProjectile(state.Walls[i].collider,out _))
                    range=Math.Min(range,state.Walls[i].distance);
            return Math.Max(0,range);
        }
        private void LightTickBeam(HeroRuntime rt,LightCombat state,LightBeamState beam,float now)
        {
            if(now<beam.Due) return;
            float angle=beam.Until>beam.Due?Mathf.Lerp(beam.StartAngle,beam.EndAngle,Mathf.Clamp01((now-beam.Due)/(beam.Until-beam.Due))):beam.EndAngle;
            var direction=Quaternion.Euler(0,angle,0)*beam.Direction;
            float range=LightClip(state,beam.Origin,direction,beam.Action.RangeMilli/1000f,beam.Action.WidthMilli/2000f);
            var actualEnd=beam.Origin+direction*range;
            if(!beam.Started)
            {
                beam.Started=true;
                LightStartPhase(rt,state,beam,now);
                if(beam.Terminal!=null && BossFind(rt,LightProfiles[7],out _,out _))
                {
                    var sequence=rt.Boss.Sequence.Reset();
                    sequence.Add(new BossScheduledPulse(beam.Terminal,actualEnd,actualEnd,beam.TerminalAmount,beam.Magic,650));
                    BossReserveSequence(rt,BossProfiles.LightSetId,LightProfiles[7],sequence,now);
                }
            }
            ListReturnHandle<Entity> handle;
            var found=DewPhysics.OverlapCircleAllEntities(out handle,beam.Origin,beam.Action.RangeMilli/1000f,EnemyFilter,rt.Hero);
            try
            {
                int scan=Math.Min(64,found.Count);
                for(int i=0;i<scan && beam.HitCount<8;i++)
                {
                    var target=found[i]; if(!BossAlive(target)) continue;
                    long life=BossNativeActorLife(target); if(life<=0) continue;
                    bool hit=false; for(int j=0;j<beam.HitCount;j++) if(beam.Hits[j]==life) { hit=true; break; }
                    if(hit) continue;
                    var delta=target.agentPosition-beam.Origin; delta.y=0;
                    float targetAngle=Vector3.SignedAngle(beam.Direction,delta,Vector3.up);
                    float closest=Mathf.Clamp(targetAngle,Math.Min(beam.LastAngle,angle),Math.Max(beam.LastAngle,angle));
                    var ray=Quaternion.Euler(0,closest,0)*beam.Direction;
                    float along=Vector3.Dot(delta,ray), radius=beam.Action.WidthMilli/2000f;
                    if(along<0 || (delta-ray*along).sqrMagnitude>radius*radius) continue;
                    float clip=LightClip(state,beam.Origin,ray,beam.Action.RangeMilli/1000f,radius);
                    if(along>clip || clip<=.0001f) continue;
                    beam.Hits[beam.HitCount++]=life;
                    long visual=beam.Visual;
                    BossDamage(rt,target,beam.Amount,beam.Magic,BossElement.Light);
                    if(rt.Boss.Build==null || !BossAlive(rt.Hero) || !beam.Active || beam.Visual!=visual
                        || !_lightCombat.TryGetValue(rt,out var current) || current!=state) return;
                }
            }
            finally { handle.Return(); }
            beam.LastAngle=angle;
            PublishBossVisual(rt,beam.Visual,2,beam.Origin,actualEnd,beam.Action.WidthMilli/2000f,beam.Due,Math.Max(beam.Due+.05f,beam.Until),
                element:BossElement.Light,shape:BossShape.Line,range:range,width:beam.Action.WidthMilli/1000f,angle:beam.EndAngle-beam.StartAngle,budget:8-beam.HitCount,coalesce:true);
            if(now>=beam.Until) LightStopBeam(rt,beam,now);
        }
        private void LightStopBeam(HeroRuntime rt,LightBeamState beam,float now)
        {
            if(beam.Visual!=0) PublishBossVisual(rt,beam.Visual,2,beam.Origin,beam.Origin,0,now,now,true);
            beam.Active=false; beam.Visual=0; beam.Action=null; beam.Terminal=null; beam.Profile=null;
            beam.PhaseAction=beam.PhaseShield=beam.PhaseCrystal=null;
        }
        private void LightClearCrystals(HeroRuntime rt,LightCombat state,float now)
        {
            for(int i=0;i<2;i++)
            {
                var crystal=state.Crystals[i];
                if(crystal.Visual!=0) PublishBossVisual(rt,crystal.Visual,6,crystal.Point,crystal.Point,0,now,now,true);
                state.Crystals[i]=default;
            }
        }
        private void LightTickCrystals(HeroRuntime rt,LightCombat state,float now)
        {
            for(int i=0;i<2;i++)
            {
                var crystal=state.Crystals[i]; if(crystal.Visual==0) continue;
                if(now>=crystal.Until)
                {
                    PublishBossVisual(rt,crystal.Visual,6,crystal.Point,crystal.Point,0,now,now,true); state.Crystals[i]=default; continue;
                }
                if(!crystal.Armed || now<crystal.Due) continue;
                crystal.Armed=false; state.Crystals[i]=crystal;
                if(LightFreeBeams(state)==0) continue;
                Entity nearest=null; float distance=36f; ListReturnHandle<Entity> handle;
                var found=DewPhysics.OverlapCircleAllEntities(out handle,crystal.Point,6f,EnemyFilter,rt.Hero);
                try
                {
                    for(int j=0;j<Math.Min(64,found.Count);j++)
                    {
                        var target=found[j]; if(!BossAlive(target)) continue;
                        float candidate=(target.agentPosition-crystal.Point).sqrMagnitude;
                        if(candidate<distance || candidate==distance && (nearest==null || target.netId<nearest.netId)) { nearest=target; distance=candidate; }
                    }
                }
                finally { handle.Return(); }
                if(nearest==null) continue;
                var beam=LightAddBeam(rt,state,LightProfiles[2],crystal.Action,crystal.Point,BossDirection(crystal.Point,nearest.agentPosition),crystal.Amount,now,0,0);
                beam.Until=now; beam.Magic=crystal.Magic;
            }
        }
        private void LightPublishCharges(HeroRuntime rt,LightCombat state,float now)
        {
            if(state.ChargeVisual==0 && state.Charges>0) state.ChargeVisual=++rt.Boss.NextId;
            if(state.ChargeVisual==0) return;
            var center=rt.Hero.agentPosition;
            PublishBossVisual(rt,state.ChargeVisual,9,center,center,.5f,now,state.Charges>0?state.ChargeUntil:now,state.Charges==0,
                element:BossElement.Light,count:state.Charges,budget:3-state.Charges,targetNetId:rt.Hero.netId);
            state.ChargeRefresh=now+1f; if(state.Charges==0) state.ChargeVisual=0;
        }
        private void LightCancelProfile(HeroRuntime rt,LightCombat state,int index,float now)
        {
            string id=LightProfiles[index]; BossCancelProfileReservations(rt,BossProfiles.LightSetId,id); rt.Boss.Defense.CancelSource(this,rt,id);
            for(int i=0;i<state.Beams.Length;i++) if(state.Beams[i].Active && state.Beams[i].Profile==id) LightStopBeam(rt,state.Beams[i],now);
            if(BossProfiles.TryGetMove(id,out var profile)) for(int i=0;i<profile.Actions.Count;i++) rt.Boss.Ready.Remove(profile.Actions[i].RuntimeKey);
            if(index==2 || index==8) LightClearCrystals(rt,state,now);
            if(index==4) { state.HandsCount=0; state.HandsUntil=0; }
            if(index==6) { state.Charges=0; state.ChargeUntil=0; LightPublishCharges(rt,state,now); }
        }
        private void TickLightBoss(HeroRuntime rt,float now)
        {
            TickWorldCrackers(rt,now);
            if(!_lightCombat.TryGetValue(rt,out var state)) return;
            if(rt.Boss.Build==null || !BossAlive(rt.Hero) || state.Epoch!=rt.Boss.Epoch || !BossNativeSameLife(rt.Hero,state.OwnerLife) || !BossNativeContextCurrent(state.Room,state.Run)) { ClearLightBoss(rt,true); return; }
            int mask=0;
            for(int i=0;i<LightProfiles.Length;i++)
            {
                if(BossFind(rt,LightProfiles[i],out _,out _)) mask|=1<<i;
                else if((state.Mask&(1<<i))!=0) LightCancelProfile(rt,state,i,now);
            }
            state.Mask=mask; if(mask==0) { ClearLightBoss(rt,true); return; }
            if(state.Charges>0 && now>=state.ChargeUntil) { state.Charges=0; LightPublishCharges(rt,state,now); }
            else if(state.Charges>0 && now>=state.ChargeRefresh) LightPublishCharges(rt,state,now);
            LightTickCrystals(rt,state,now);
            for(int i=0;i<state.Beams.Length;i++)
            {
                if(state.Beams[i].Active) LightTickBeam(rt,state,state.Beams[i],now);
                if(!_lightCombat.TryGetValue(rt,out var current) || current!=state) return;
            }
        }
        private void ClearLightBoss(HeroRuntime rt,bool preserveRewards=false)
        {
            if(!preserveRewards) ClearWorldCrackers(rt);
            if(!_lightCombat.TryGetValue(rt,out var state)) return;
            float now=Time.time;
            for(int i=0;i<LightProfiles.Length;i++) LightCancelProfile(rt,state,i,now);
            LightClearCrystals(rt,state,now); state.Charges=0; LightPublishCharges(rt,state,now);
            state.HandsCount=0; state.ChargeUntil=state.HandsUntil=0; state.Mask=0; state.Epoch=state.OwnerLife=0;
            state.Room=null; state.Run=null; Array.Clear(state.Seen,0,state.Seen.Length); Array.Clear(state.Next,0,state.Next.Length);
            _lightCombat.Remove(rt); _lightPool.Return(state);
        }
    }
}
