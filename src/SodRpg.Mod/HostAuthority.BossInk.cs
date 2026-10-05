using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class InkDomain
        {
            internal string Profile;
            internal BossAction Action, ShieldAction;
            internal Vector3 Point;
            internal float Start, Mature, Until, Shield, H;
            internal bool OwnerShielded, AllyShielded;
            internal int Beats;
            internal long Visual;
        }
        private sealed class InkShield
        {
            internal Hero Target;
            internal float Creation, Normal, NormalUntil, Reward, RewardUntil;
            internal Se_GenericShield_OneShot Container;
            internal float ContainerCreation,ContainerUntil,ObservedAmount;
            internal ShieldEffect Observed;
            internal Action<float,float> OnAmountModified;
            internal bool RewardContainer;
            internal long Visual;
            internal long RewardLife;
            internal long ContainerLife,TargetLife;
        }
        private sealed class InkState
        {
            internal ulong WhiteSignature, DarkSignature;
            internal bool Initialized;
            internal bool ShieldVisualsDirty,MarkVisualDirty;
            internal Build SeenBuild;
            internal SkillTrigger SeenSkill;
            internal float SeenSkillCreation;
            internal readonly List<InkDomain> Domains = new List<InkDomain>(3);
            internal readonly List<InkShield> Shields = new List<InkShield>(8);
            internal Vector3 WhiteArrival, DarkArrival;
            internal float WhiteArrivalUntil, DarkArrivalUntil, LastForm, ChainUntil;
            internal int Form, Chain;
            internal Entity Mark;
            internal long MarkVisual,FormVisual,MarkLife;
            internal long FormSource,ChainSource;
            internal float MarkUntil;
            internal float NextNativeAdd,NextNativeSpear,NextNativeIllumination;
        }
        private readonly Dictionary<HeroRuntime, InkState> _inkStates = new Dictionary<HeroRuntime, InkState>();
        private InkState InkGet(HeroRuntime rt)
        {
            if (!_inkStates.TryGetValue(rt, out var state)) _inkStates.Add(rt, state = new InkState());
            return state;
        }
        private ulong InkSignature(HeroRuntime rt, string set, string reward)
        {
            ulong hash = 1469598103934665603UL;
            foreach (var move in rt.Powers.Build.BossMoves)
                if (move.SetId == set)
                {
                    foreach (char c in move.ProfileId) hash = (hash ^ c) * 1099511628211UL;
                    foreach (var channel in move.Channels) hash = (hash ^ (uint)channel.ValueMilli) * 1099511628211UL;
                }
            return (hash ^ (uint)BossRewardStage(rt,reward)) * 1099511628211UL;
        }
        private void InkReconcile(HeroRuntime rt, InkState state)
        {
            var skill=FindMemory(rt.Hero,nameof(St_U_BeamOfBalance));
            float creation=skill!=null?skill.creationTime:0;
            if(state.Initialized && ReferenceEquals(state.SeenBuild,rt.Powers.Build) && state.SeenSkill==skill && state.SeenSkillCreation==creation) return;
            state.SeenBuild=rt.Powers.Build; state.SeenSkill=skill; state.SeenSkillCreation=creation;
            ulong white = InkSignature(rt,BossProfiles.WhiteNightSetId,BossProfiles.WhiteNightRewardId);
            ulong dark = InkSignature(rt,BossProfiles.DarkMoonSetId,BossProfiles.DarkMoonRewardId);
            if (state.Initialized && state.WhiteSignature != white)
            {
                InkClearNormal(rt,state,true);
                InkClearShields(rt,state,false);
                InkReapplyBeamProfile(rt,true);
            }
            if (state.Initialized && state.DarkSignature != dark)
            {
                InkClearNormal(rt,state,false);
                InkReapplyBeamProfile(rt,false);
            }
            state.WhiteSignature=white; state.DarkSignature=dark; state.Initialized=true;
            InkReconcileBeamSkills(rt);
        }
        private void DispatchInkBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            var state=InkGet(rt); InkReconcile(rt,state);
            if (kind == BossEvent.MovementCompleted)
            {
                if (BossFind(rt,"boss_white_night.feet",out _,out var white)) { state.WhiteArrival=point; state.WhiteArrivalUntil=now+white.Actions[0].LifetimeMillis/1000f; }
                if (BossFind(rt,"boss_dark_moon.feet",out _,out var dark)) { state.DarkArrival=point; state.DarkArrivalUntil=now+dark.Actions[0].LifetimeMillis/1000f; }
                if (BossFind(rt,"boss_dark_moon.stage3",out _,out var advance) && BossFind(rt,"boss_dark_moon.stage2",out _,out var forms)
                    && BossReady(rt,advance.Id+".move",now,advance.Actions[1].CooldownMillis))
                {
                    InkResetIdle(state,forms.Actions[0],now);
                    state.Form=(state.Form+1)%forms.Actions[0].Count; state.LastForm=now; state.Chain=0; state.ChainUntil=0; state.FormSource=state.ChainSource=0;
                    if(state.FormVisual==0) state.FormVisual=++rt.Boss.NextId;
                    PublishBossVisual(rt,state.FormVisual,9,point,point,state.Form+1,now,now+forms.Actions[0].CounterLifetimeMillis/1000f,element:BossElement.Dark,count:state.Form+1);
                }
                return;
            }
            if (kind == BossEvent.MainHit)
            {
                InkWhiteStages(rt,state,activation,point,now);
                InkDarkForm(rt,state,activation,victim,point,now);
            }
            // Mature-domain consumption/stages are deliberately before all pieces and new domains.
            InkPieces(rt,state,true,kind,activation,victim,point,now);
            InkPieces(rt,state,false,kind,activation,victim,point,now);
            if (kind == BossEvent.MainHit) InkWhiteNewDomain(rt,state,activation,now);
        }
        private static bool InkAvailable(HeroRuntime rt,string key,float now)
            => !rt.Boss.Ready.TryGetValue(key,out float due) || now>=due;
        private bool InkDue(HeroRuntime rt,BossMoveProfile profile,BossAction a,long activation,float now)
        {
            string key=rt.Boss.ActionKeys[a];
            bool advanced=a.MainHits==0 || rt.Boss.Ledger.Advance(key,activation,a.MainHits,a.CounterLifetimeMillis/1000f,now);
            return advanced && InkAvailable(rt,key,now);
        }
        private static void InkCommit(HeroRuntime rt,BossAction a,float now)
        {
            string key=rt.Boss.ActionKeys[a];
            BossReady(rt,key,now,a.CooldownMillis);
            if(a.MainHits>0) rt.Boss.Ledger.Consume(key);
        }
        private bool InkPulse(HeroRuntime rt,BossMoveEntry entry,BossMoveProfile profile,BossAction action,Vector3 center,Vector3 end,float amount,bool magic,float now,int delay=-1,int lifetime=0,Entity target=null,long nativeLife=0)
        {
            bool clamp=action.Anchor==BossAnchor.Hit && action.RangeMilli>0 && target==null;
            Vector3 origin=clamp?rt.Hero.agentPosition:center;
            if(!BossGround(origin,center,clamp?action.RangeMilli/1000f:0,out center)) return false;
            int wait=delay<0?action.DelayMillis:delay;
            if(wait>0 || lifetime>0 || target!=null) return BossReserveSequence(rt,entry.SetId,profile.Id,new[]{new BossScheduledPulse(action,center,end,amount,magic,wait,target:target)},now,action.MaxInstances,lifetime,nativeLife:nativeLife);
            rt.Boss.Shapes.Execute(this,rt,action,center,end,amount,magic);
            PublishBossVisual(rt,++rt.Boss.NextId,action.Shape==BossShape.Line?6:1,center,end,action.RadiusMilli/1000f,now,now+0.25f,element:action.Element,shape:action.Shape,range:action.RangeMilli/1000f,width:action.WidthMilli/1000f,angle:action.AngleMilli/1000f,nativeLife:nativeLife);
            return true;
        }
        private void InkPieces(HeroRuntime rt,InkState state,bool white,BossEvent kind,long activation,Entity victim,Vector3 point,float now)
        {
            string prefix=white?"boss_white_night.":"boss_dark_moon.";
            foreach(var entry in rt.Powers.Build.BossMoves)
            {
                if(entry.SetId!=(white?BossProfiles.WhiteNightSetId:BossProfiles.DarkMoonSetId) || !BossProfiles.TryGetMove(entry.ProfileId,out var profile) || profile.Id.StartsWith(prefix+"stage",StringComparison.Ordinal)) continue;
                int last=profile.Actions.Count-1; var action=profile.Actions[last];
                if(action.Event!=kind) continue;
                if(profile.Id==prefix+"feet")
                {
                    float until=white?state.WhiteArrivalUntil:state.DarkArrivalUntil;
                    if(white) state.WhiteArrivalUntil=0; else state.DarkArrivalUntil=0;
                    if(until<=now || !InkDue(rt,profile,action,activation,now)) continue;
                    Vector3 arrival=white?state.WhiteArrival:state.DarkArrival;
                    if(InkPulse(rt,entry,profile,action,arrival,point,BossAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),now))
                    {
                        InkCommit(rt,action,now);
                        if(!white) PublishBossVisual(rt,++rt.Boss.NextId,8,arrival,point,action.WidthMilli/1000f,now,now+action.DelayMillis/1000f);
                    }
                    continue;
                }
                if(!InkDue(rt,profile,action,activation,now)) continue;
                Vector3 center=rt.Hero.agentPosition, end=point;
                if(kind==BossEvent.MemoryUse) end=BossCursor(rt);
                if(!white && kind==BossEvent.NativeDamageTaken) end=victim!=null?victim.position:point;
                if(white && (profile.Id==prefix+"armor" || profile.Id==prefix+"charm"))
                {
                    if(InkStartDomain(rt,state,entry,profile,action,action,center,now)) InkCommit(rt,action,now);
                    continue;
                }
                if(action.Anchor==BossAnchor.Hit && !(action.Mechanism==BossMechanism.Projectile)) center=end;
                if(white && profile.Id==prefix+"head")
                {
                    var pulses=new BossScheduledPulse[action.Count];
                    Vector3 direction=BossDirection(center,end); if(direction==Vector3.zero) direction=rt.Hero.transform.forward;
                    for(int i=0;i<pulses.Length;i++) pulses[i]=new BossScheduledPulse(action,center,center+Quaternion.Euler(0,(i-(pulses.Length-1)/2f)*action.MagnitudeMilli/1000f,0)*direction,BossAmount(rt,entry,profile,action.ChannelId)/action.Count,BossMagic(rt),i*action.IntervalMillis);
                    if(BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,action.MaxInstances)) InkCommit(rt,action,now);
                    continue;
                }
                if(action.Mechanism==BossMechanism.Projectile)
                {
                    if(!BossGround(center,center,0,out center) || !BossFinite(end)) continue;
                    bool ego=profile.Id=="boss_dark_moon.head";
                    if(rt.Boss.Projectiles.Execute(this,rt,entry.SetId,action,center,end,BossAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),now,explodeAtEnd:ego,terminalRadius:action.RadiusMilli/1000f,profile:profile.Id)) InkCommit(rt,action,now);
                    continue;
                }
                if(white && profile.Id==prefix+"hands")
                {
                    if(!BossGround(center,center,0,out center)) continue;
                    var pulse=new BossScheduledPulse(action,center,end,BossAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),action.DelayMillis);
                    if(!BossReserveSequence(rt,entry.SetId,profile.Id,new[]{pulse},now,action.MaxInstances)) continue;
                    InkCommit(rt,action,now);
                    rt.Boss.Shapes.Execute(this,rt,profile.Actions[0],center,end,0,BossMagic(rt));
                    continue;
                }
                if(InkPulse(rt,entry,profile,action,center,end,BossAmount(rt,entry,profile,action.ChannelId),BossMagic(rt),now))
                {
                    InkCommit(rt,action,now);
                }
            }
        }
        private bool InkStartDomain(HeroRuntime rt,InkState state,BossMoveEntry entry,BossMoveProfile profile,BossAction damage,BossAction shield,Vector3 center,float now)
        {
            foreach(var d in state.Domains) if(d.Profile==profile.Id) return false;
            if(!BossGround(rt.Hero.agentPosition,center,0,out center)) return false;
            bool palm=damage.Payload==BossPayload.Damage;
            int total=palm?damage.DelayMillis+damage.LifetimeMillis:damage.DelayMillis>0?damage.DelayMillis:damage.LifetimeMillis;
            var pulses=palm?new[]{new BossScheduledPulse(damage,center,center,BossAmount(rt,entry,profile,damage.ChannelId),BossMagic(rt),damage.DelayMillis)}:Array.Empty<BossScheduledPulse>();
            if(!BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,damage.MaxInstances,total)) return false;
            var domain=new InkDomain { Profile=profile.Id,Action=damage,ShieldAction=shield,Point=center,Start=now,Mature=now+damage.DelayMillis/1000f,Until=now+total/1000f,Shield=BossAmount(rt,entry,profile,shield.ChannelId),H=Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower),Visual=++rt.Boss.NextId };
            state.Domains.Add(domain);
            PublishBossVisual(rt,domain.Visual,4,center,center,damage.RadiusMilli/1000f,profile.Id=="boss_white_night.charm"?now:domain.Mature,domain.Until,element:BossElement.Light,finalRadius:profile.Id=="boss_white_night.charm"?damage.WidthMilli/1000f:-1f);
            if(damage.DelayMillis==0) InkDomainShields(rt,state,domain,now);
            return true;
        }
        private void InkWhiteNewDomain(HeroRuntime rt,InkState state,long activation,float now)
        {
            if(!BossFind(rt,"boss_white_night.stage2",out var entry,out var profile)) return;
            var action=profile.Actions[0];
            if(InkDue(rt,profile,action,activation,now) && InkStartDomain(rt,state,entry,profile,action,profile.Actions[1],rt.Hero.agentPosition,now)) InkCommit(rt,action,now);
        }
        private void InkWhiteStages(HeroRuntime rt,InkState state,long activation,Vector3 point,float now)
        {
            if(!BossFind(rt,"boss_white_night.stage3",out var waveEntry,out var wave)) return;
            InkDomain existing=null;
            foreach(var d in state.Domains) if(d.Profile=="boss_white_night.stage2" && d.Mature<=now && d.Until>now && BossFieldCount(rt,BossProfiles.WhiteNightSetId,d.Profile)>0 && BossFieldPulseCount(rt,BossProfiles.WhiteNightSetId,d.Profile)>0 && InkInside(rt.Hero,d.Point,d.Action.RadiusMilli/1000f)) { existing=d; break; }
            if(existing==null) return;
            if(BossFind(rt,"boss_white_night.stage6",out var finishEntry,out var finish))
            {
                var a=finish.Actions[0]; existing.Beats=Math.Min(a.MainHits,existing.Beats+1);
                if(existing.Beats>=a.MainHits && InkAvailable(rt,rt.Boss.ActionKeys[a],now))
                {
                    BossCancelProfileReservations(rt,BossProfiles.WhiteNightSetId,existing.Profile);
                    state.Domains.Remove(existing);
                    PublishBossVisual(rt,existing.Visual,4,existing.Point,existing.Point,existing.Action.RadiusMilli/1000f,now,now,true);
                    InkPulse(rt,finishEntry,finish,a,existing.Point,point,BossAmount(rt,finishEntry,finish,a.ChannelId),BossMagic(rt),now);
                    InkShieldDomainHeroes(rt,state,existing.Point,existing.Action.RadiusMilli/1000f,finish.Actions[1],BossAmount(rt,finishEntry,finish,finish.Actions[1].ChannelId),Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower),now);
                    InkCommit(rt,a,now);
                    return;
                }
            }
            var action=wave.Actions[0];
            if(InkAvailable(rt,rt.Boss.ActionKeys[action],now) && InkPulse(rt,waveEntry,wave,action,rt.Hero.agentPosition,point,BossAmount(rt,waveEntry,wave,action.ChannelId),BossMagic(rt),now)) InkCommit(rt,action,now);
        }
        private static bool InkInside(Hero hero,Vector3 center,float radius) => BossAlive(hero) && (hero.position-center).Flattened().sqrMagnitude<=radius*radius;
        private Hero InkNearestAlly(HeroRuntime rt,Vector3 center,float radius)
        {
            ListReturnHandle<Entity> handle;
            var entities=DewPhysics.OverlapCircleAllEntities(out handle,center,radius);
            Hero nearest=null; float best=float.MaxValue;
            try { foreach(var e in entities) if(e is Hero hero && hero!=rt.Hero && InkInside(hero,center,radius) && !rt.Hero.CheckEnemyOrNeutral(hero))
                { float distance=(hero.position-rt.Hero.position).Flattened().sqrMagnitude; if(distance<best || distance==best && (nearest==null || hero.netId<nearest.netId)) { nearest=hero; best=distance; } } }
            finally { handle.Return(); }
            return nearest;
        }
        private void InkShieldDomainHeroes(HeroRuntime rt,InkState state,Vector3 center,float radius,BossAction action,float amount,float h,float now)
        {
            if(InkInside(rt.Hero,center,radius)) InkGiveShield(rt,state,rt.Hero,amount,h,action.LifetimeMillis/1000f,false,now,action.MagnitudeMilli);
            if(action.MaxTargets>1) { var ally=InkNearestAlly(rt,center,radius); if(ally!=null) InkGiveShield(rt,state,ally,amount,h,action.LifetimeMillis/1000f,false,now,action.MagnitudeMilli); }
        }
        private void InkDomainShields(HeroRuntime rt,InkState state,InkDomain domain,float now)
        {
            bool shrinking=domain.Profile=="boss_white_night.charm";
            float radius=(shrinking?domain.Action.WidthMilli:domain.Action.RadiusMilli)/1000f;
            if(!domain.OwnerShielded && InkInside(rt.Hero,domain.Point,radius)) { InkGiveShield(rt,state,rt.Hero,domain.Shield,domain.H,domain.ShieldAction.LifetimeMillis/1000f,false,now,domain.ShieldAction.MagnitudeMilli); domain.OwnerShielded=true; }
            if(!domain.AllyShielded && domain.ShieldAction.MaxTargets>1) { var ally=InkNearestAlly(rt,domain.Point,radius); if(ally!=null) { InkGiveShield(rt,state,ally,domain.Shield,domain.H,domain.ShieldAction.LifetimeMillis/1000f,false,now,domain.ShieldAction.MagnitudeMilli); domain.AllyShielded=true; } }
        }
        private void InkResetIdle(InkState state,BossAction form,float now)
        {
            if(now-state.LastForm>=form.CounterLifetimeMillis/1000f) { state.Form=0; state.Chain=0; state.ChainUntil=0; state.FormSource=state.ChainSource=0; }
        }
        private bool InkDarkForm(HeroRuntime rt,InkState state,long activation,Entity victim,Vector3 point,float now,InkBeamState beam=null)
        {
            if(!BossFind(rt,"boss_dark_moon.stage2",out var entry,out var profile)) return false;
            var gate=profile.Actions[0]; InkResetIdle(state,gate,now);
            if(!InkAvailable(rt,rt.Boss.ActionKeys[gate],now)) return false;
            int phase=state.Form; var action=profile.Actions[phase];
            long nativeLife=beam?.Life??0;
            float h=beam?.H??Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower);
            bool magic=beam?.Magic??BossMagic(rt);
            Vector3 center=phase==2?point:rt.Hero.agentPosition;
            if(!InkPulse(rt,entry,profile,action,center,point,h*BossCoefficient(entry,profile,action.ChannelId),magic,now,nativeLife:nativeLife)) return false;
            InkCommit(rt,gate,now); state.Form=(phase+1)%gate.Count; state.LastForm=now; state.FormSource=nativeLife;
            if(BossFind(rt,"boss_dark_moon.stage3",out var markEntry,out var mark))
            {
                var a=mark.Actions[0];
                if(state.Mark!=victim || !BossNativeSameLife(victim,state.MarkLife))
                {
                    int old=rt.Boss.Ledger.MarkCount(state.Mark,now,mark.Id);
                    if(old>0) rt.Boss.Ledger.ConsumeMarks(state.Mark,old,now,mark.Id);
                    state.Mark=victim; state.MarkLife=victim!=null?BossNativeActorLife(victim):0;
                }
                if(rt.Boss.Ledger.MarkCount(victim,now,mark.Id)==0)
                {
                    if(state.MarkVisual!=0) PublishBossVisual(rt,state.MarkVisual,9,point,point,0,now,now,true);
                    state.MarkVisual=0;
                }
                rt.Boss.Ledger.MarkTarget(victim,1,a.LifetimeMillis/1000f,now,mark.Id,nativeLife);
                int count=rt.Boss.Ledger.MarkCount(victim,now,mark.Id);
                state.MarkUntil=now+a.LifetimeMillis/1000f;
                if(state.MarkVisual==0) state.MarkVisual=++rt.Boss.NextId;
                PublishBossVisual(rt,state.MarkVisual,9,point,point,count,now,now+a.LifetimeMillis/1000f,element:BossElement.Dark,count:count,targetNetId:victim!=null?victim.persistentNetId:0,nativeLife:rt.Boss.Ledger.MarkNativeSource(victim,now,mark.Id));
                if(count>=a.MainHits && InkAvailable(rt,rt.Boss.ActionKeys[a],now) && InkPulse(rt,markEntry,mark,a,point,point,h*BossCoefficient(markEntry,mark,a.ChannelId),magic,now,target:victim,nativeLife:nativeLife))
                {
                    rt.Boss.Ledger.ConsumeMarks(victim,a.MainHits,now,mark.Id); InkCommit(rt,a,now);
                    PublishBossVisual(rt,state.MarkVisual,9,point,point,0,now,now,true); state.MarkVisual=0;
                }
            }
            if(now>state.ChainUntil) { state.Chain=0; state.ChainSource=0; }
            if(phase==0) { state.Chain=1; state.ChainUntil=now+gate.CounterLifetimeMillis/1000f; state.ChainSource=nativeLife; }
            else if(phase==1 && state.Chain==1) { state.Chain=2; state.ChainSource=nativeLife; }
            else if(phase==2 && state.Chain==2)
            {
                state.Chain=0; state.ChainSource=0; InkDarkFinish(rt,point,now,beam);
            }
            else { state.Chain=0; state.ChainSource=0; }
            if(state.FormVisual==0) state.FormVisual=++rt.Boss.NextId;
            PublishBossVisual(rt,state.FormVisual,9,rt.Hero.agentPosition,point,phase+1,now,now+gate.CounterLifetimeMillis/1000f,element:BossElement.Dark,count:state.Form+1,budget:state.Chain,targetNetId:victim!=null?victim.persistentNetId:0,nativeLife:nativeLife);
            return true;
        }
        private void InkDarkFinish(HeroRuntime rt,Vector3 point,float now,InkBeamState beam=null)
        {
            if(!BossFind(rt,"boss_dark_moon.stage6",out var entry,out var profile)) return;
            var line=profile.Actions[0]; var hammer=profile.Actions[1];
            long nativeLife=beam?.Life??0;
            float h=beam?.H??Math.Max(rt.Hero.Status.attackDamage,rt.Hero.Status.abilityPower);
            bool magic=beam?.Magic??BossMagic(rt);
            if(!InkAvailable(rt,rt.Boss.ActionKeys[line],now) || !BossGround(point,point,0,out point)) return;
            Vector3 direction=BossDirection(rt.Hero.agentPosition,point); if(direction==Vector3.zero) direction=rt.Hero.transform.forward;
            Vector3 side=Vector3.Cross(Vector3.up,direction)*line.MagnitudeMilli/1000f;
            var pulses=new BossScheduledPulse[line.Count+1];
            for(int i=0;i<line.Count;i++)
            {
                Vector3 phantom=point+(i==0?-side:side);
                if(!BossGround(point,phantom,line.MagnitudeMilli/1000f,out var legal) || (legal-phantom).Flattened().sqrMagnitude>0.0001f) return;
                phantom=legal;
                pulses[i]=new BossScheduledPulse(line,phantom,point,h*BossCoefficient(entry,profile,line.ChannelId)/line.Count,magic,line.DelayMillis);
            }
            pulses[line.Count]=new BossScheduledPulse(hammer,point,point,h*BossCoefficient(entry,profile,hammer.ChannelId),magic,hammer.DelayMillis);
            if(!BossReserveSequence(rt,entry.SetId,profile.Id,pulses,now,line.MaxInstances,nativeLife:nativeLife)) return;
            InkCommit(rt,line,now);
            for(int i=0;i<line.Count;i++)
            {
                long visual=++rt.Boss.NextId;
                PublishBossVisual(rt,visual,8,pulses[i].Center,point,line.WidthMilli/1000f,now,now+line.DelayMillis/1000f,element:BossElement.Dark,nativeLife:nativeLife);
                InkRegisterBeamVisual(nativeLife,visual);
            }
        }
        private void TickInkBoss(HeroRuntime rt,float now)
        {
            if(!_inkStates.TryGetValue(rt,out var state)) { if(!BossNativeHasProfiles(rt)) { TickInkBeams(rt,now); return; } state=InkGet(rt); }
            InkReconcile(rt,state);
            if(state.MarkVisualDirty) { InkRefreshMarkVisual(rt,state,now); state.MarkVisualDirty=false; }
            for(int i=state.Domains.Count-1;i>=0;i--)
            {
                var d=state.Domains[i]; bool charm=d.Profile=="boss_white_night.charm";
                if(charm && now>=d.Mature) InkDomainShields(rt,state,d,now);
                if(now>=d.Until || BossFieldCount(rt,BossProfiles.WhiteNightSetId,d.Profile)==0)
                { PublishBossVisual(rt,d.Visual,4,d.Point,d.Point,0,now,now,true); state.Domains.RemoveAt(i); continue; }
                if(!charm && now>=d.Mature && (d.Action.Payload!=BossPayload.Damage || BossFieldPulseCount(rt,BossProfiles.WhiteNightSetId,d.Profile)>0)) InkDomainShields(rt,state,d,now);
            }
            for(int i=state.Shields.Count-1;i>=0;i--)
            {
                var shield=state.Shields[i];
                if(!BossAlive(shield.Target) || shield.Target.creationTime!=shield.Creation || !BossNativeSameLife(shield.Target,shield.TargetLife) || shield.NormalUntil<=now && shield.RewardUntil<=now)
                { InkDestroyShield(rt,shield,now); state.Shields.RemoveAt(i); continue; }
                InkRefreshShield(rt,shield,now);
                if(state.ShieldVisualsDirty && shield.Container!=null && shield.Container.isActive && BossNativeSameLife(shield.Container,shield.ContainerLife))
                    PublishBossVisual(rt,shield.Visual,7,shield.Target.position,shield.Target.position,0,now,shield.RewardContainer?shield.RewardUntil:shield.NormalUntil,element:BossElement.Light,targetNetId:shield.Target.netId,nativeLife:shield.RewardContainer?shield.RewardLife:0);
            }
            state.ShieldVisualsDirty=false;
            TickInkBeams(rt,now);
        }
        private void InkWhiteAfterFields(HeroRuntime rt,float now)
        {
            if(!_inkStates.TryGetValue(rt,out var state)) return;
            foreach(var domain in state.Domains)
                if(domain.Action.Payload==BossPayload.Damage && now>=domain.Mature && now<domain.Until && BossFieldPulseCount(rt,BossProfiles.WhiteNightSetId,domain.Profile)>0)
                    InkDomainShields(rt,state,domain,now);
        }
        private void ClearInkBoss(HeroRuntime rt,bool preserveRewards=false)
        {
            if(_inkStates.TryGetValue(rt,out var state))
            {
                InkClearNormal(rt,state,true);
                if(preserveRewards)
                {
                    if(state.FormSource==0)
                    {
                        state.Form=0; state.LastForm=0;
                        if(state.FormVisual!=0) PublishBossVisual(rt,state.FormVisual,9,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                        state.FormVisual=0;
                    }
                    if(state.ChainSource==0) { state.Chain=0; state.ChainUntil=0; }
                    state.DarkArrivalUntil=0; state.MarkVisualDirty=true;
                    if(state.MarkVisual!=0) SetBossVisualNativeSource(rt,state.MarkVisual,rt.Boss.Ledger.MarkNativeSource(state.Mark,Time.time,"boss_dark_moon.stage3"));
                }
                else InkClearNormal(rt,state,false);
                InkClearShields(rt,state,preserveRewards);
                if(preserveRewards) state.ShieldVisualsDirty=true;
                if(!preserveRewards) _inkStates.Remove(rt);
            }
            if(!preserveRewards) ClearInkBeams(rt);
        }
        private void InkClearNormal(HeroRuntime rt,InkState state,bool white)
        {
            if(white)
            {
                foreach(var d in state.Domains) { BossCancelProfileReservations(rt,BossProfiles.WhiteNightSetId,d.Profile); PublishBossVisual(rt,d.Visual,4,d.Point,d.Point,0,Time.time,Time.time,true); }
                state.Domains.Clear(); state.WhiteArrivalUntil=0;
            }
            else
            {
                int marks=rt.Boss.Ledger.MarkCount(state.Mark,Time.time,"boss_dark_moon.stage3");
                if(marks>0) rt.Boss.Ledger.ConsumeMarks(state.Mark,marks,Time.time,"boss_dark_moon.stage3");
                if(state.MarkVisual!=0) PublishBossVisual(rt,state.MarkVisual,9,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                if(state.FormVisual!=0) PublishBossVisual(rt,state.FormVisual,9,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                state.MarkVisual=0; state.FormVisual=0; state.MarkLife=0; state.FormSource=state.ChainSource=0; state.MarkUntil=0;
                state.Form=0; state.Chain=0; state.ChainUntil=0; state.LastForm=0; state.DarkArrivalUntil=0; state.Mark=null;
            }
        }
        private void InkClearNativeDarkLedger(HeroRuntime rt,InkState state,long life,float now)
        {
            int previous=rt.Boss.Ledger.MarkCount(state.Mark,now,"boss_dark_moon.stage3");
            rt.Boss.Ledger.CancelNativeMarks("boss_dark_moon.stage3",life);
            int count=rt.Boss.Ledger.MarkCount(state.Mark,now,"boss_dark_moon.stage3");
            if(count!=previous && state.MarkVisual!=0)
            {
                Vector3 point=state.Mark!=null?state.Mark.position:rt.Hero.position;
                PublishBossVisual(rt,state.MarkVisual,9,point,point,count,now,count>0?state.MarkUntil:now,removed:count==0,element:BossElement.Dark,count:count,targetNetId:state.Mark!=null?state.Mark.persistentNetId:0,nativeLife:rt.Boss.Ledger.MarkNativeSource(state.Mark,now,"boss_dark_moon.stage3"));
                if(count==0) { state.MarkVisual=0; state.Mark=null; state.MarkLife=0; }
            }
            if(state.FormSource==life)
            {
                state.Form=0; state.LastForm=0; state.FormSource=0;
                if(state.FormVisual!=0) PublishBossVisual(rt,state.FormVisual,9,rt.Hero.position,rt.Hero.position,0,now,now,true);
                state.FormVisual=0;
            }
            if(state.ChainSource==life) { state.Chain=0; state.ChainUntil=0; state.ChainSource=0; }
        }
        private void InkRefreshMarkVisual(HeroRuntime rt,InkState state,float now)
        {
            if(state.MarkVisual==0) return;
            int count=rt.Boss.Ledger.MarkCount(state.Mark,now,"boss_dark_moon.stage3");
            Vector3 point=state.Mark!=null?state.Mark.position:rt.Hero.position;
            PublishBossVisual(rt,state.MarkVisual,9,point,point,count,now,count>0?state.MarkUntil:now,removed:count==0,element:BossElement.Dark,count:count,targetNetId:state.Mark!=null?state.Mark.persistentNetId:0,nativeLife:rt.Boss.Ledger.MarkNativeSource(state.Mark,now,"boss_dark_moon.stage3"));
            if(count==0) { state.MarkVisual=0; state.Mark=null; state.MarkLife=0; }
        }
        private void InkClearShields(HeroRuntime rt,InkState state,bool preserveRewards)
        {
            for(int i=state.Shields.Count-1;i>=0;i--)
            {
                var shield=state.Shields[i]; InkCaptureShield(shield); shield.Normal=0; shield.NormalUntil=0;
                if(preserveRewards && shield.RewardUntil>Time.time) { InkRefreshShield(rt,shield,Time.time,publish:false); continue; }
                InkDestroyShield(rt,shield,Time.time); state.Shields.RemoveAt(i);
            }
        }
        private void InkGiveShield(HeroRuntime rt,InkState state,Hero target,float amount,float h,float duration,bool reward,float now,int capMilli,long rewardLife=0)
        {
            if(!BossAlive(target) || amount<=0 || duration<=0) return;
            InkShield shield=null;
            foreach(var s in state.Shields) if(s.Target==target && s.Creation==target.creationTime && BossNativeSameLife(target,s.TargetLife)) { shield=s; break; }
            if(shield==null) { if(state.Shields.Count>=64) return; state.Shields.Add(shield=new InkShield{Target=target,Creation=target.creationTime,TargetLife=BossNativeActorLife(target),Visual=++rt.Boss.NextId}); }
            InkCaptureShield(shield);
            amount=Math.Min(amount,h*capMilli/1000f);
            if(shield.Container!=null && shield.Container.isActive && shield.Container.creationTime==shield.ContainerCreation && BossNativeSameLife(shield.Container,shield.ContainerLife))
            {
                EnterGenerated(rt.Hero);
                try { amount=Math.Max(0,Math.Min(amount,shield.Container.ProcessShieldAmount(amount,target))); }
                finally { ExitGenerated(rt.Hero); }
            }
            if(amount<=0) return;
            if(reward)
            {
                if(shield.RewardUntil>now && amount<shield.Reward) return;
                if(shield.RewardContainer && shield.RewardLife!=rewardLife) SetBossVisualNativeSource(rt,shield.Visual,rewardLife);
                shield.Reward=amount; shield.RewardLife=rewardLife; shield.RewardUntil=now+duration;
            }
            else
            {
                if(shield.NormalUntil>now && amount<shield.Normal) return;
                shield.Normal=amount; shield.NormalUntil=now+duration;
            }
            InkRefreshShield(rt,shield,now,true);
        }
        private static void InkObserveShield(InkShield shield,float remaining)
        {
            remaining=Math.Max(0,remaining);
            float consumed=Math.Max(0,shield.ObservedAmount-remaining);
            shield.Normal=Math.Max(0,shield.Normal-consumed);
            shield.Reward=Math.Max(0,shield.Reward-consumed);
            shield.ObservedAmount=remaining;
        }
        private void InkCaptureShield(InkShield shield)
        {
            if(ReferenceEquals(shield.Container,null)) return;
            if(shield.Observed!=null && shield.Container!=null && shield.Container.isActive && shield.Container.creationTime==shield.ContainerCreation && BossNativeSameLife(shield.Container,shield.ContainerLife) && ReferenceEquals(shield.Container.shield,shield.Observed))
                InkObserveShield(shield,shield.Observed.amount);
            else if(Time.time<shield.ContainerUntil)
                shield.Normal=shield.Reward=0;
        }
        private void InkRefreshShield(HeroRuntime rt,InkShield shield,float now,bool update=false,bool publish=true)
        {
            if(!update) InkCaptureShield(shield);
            float normal=shield.NormalUntil>now?shield.Normal:0;
            float reward=shield.RewardUntil>now?shield.Reward:0;
            bool isReward=reward>normal; float amount=Math.Max(normal,reward);
            float due=isReward?shield.RewardUntil:shield.NormalUntil;
            if(amount<=0) { InkDestroyShield(rt,shield,now); return; }
            if(shield.Observed!=null && shield.Container!=null && shield.Container.isActive && shield.Container.creationTime==shield.ContainerCreation && BossNativeSameLife(shield.Container,shield.ContainerLife))
            {
                if(shield.RewardContainer==isReward && shield.ObservedAmount==amount && shield.ContainerUntil==due) return;
                // Both candidates are already native-processed remaining capacities. A replacement/expiry is not absorption.
                shield.Observed.onAmountModified-=shield.OnAmountModified;
                shield.Observed.amount=amount;
                shield.ObservedAmount=amount;
                shield.Observed.onAmountModified+=shield.OnAmountModified;
                shield.Container.SetTimer(due-now); shield.ContainerUntil=due; shield.RewardContainer=isReward;
                if(publish) PublishBossVisual(rt,shield.Visual,7,shield.Target.position,shield.Target.position,0,now,due,element:BossElement.Light,targetNetId:shield.Target.netId,nativeLife:isReward?shield.RewardLife:0);
                else SetBossVisualNativeSource(rt,shield.Visual,isReward?shield.RewardLife:0);
                return;
            }
            InkDestroyShield(rt,shield,now);
            EnterGenerated(rt.Hero);
            try { shield.Container=rt.Hero.GiveShield(shield.Target,amount,due-now); }
            finally { ExitGenerated(rt.Hero); }
            if(shield.Container!=null)
            {
                shield.ContainerCreation=shield.Container.creationTime;
                shield.ContainerLife=BossNativeActorLife(shield.Container);
                shield.ContainerUntil=due;
                shield.Observed=shield.Container.shield;
                if(shield.Observed!=null)
                {
                    // Native ShieldPower may reduce the initial grant; neither source may retain that nominal excess.
                    shield.Observed.amount=Math.Min(amount,shield.Observed.amount);
                    shield.ObservedAmount=amount;
                    InkObserveShield(shield,shield.Observed.amount);
                    if(shield.OnAmountModified==null) shield.OnAmountModified=(before,after)=>
                    {
                        if(shield.Container!=null && shield.Container.creationTime==shield.ContainerCreation && BossNativeSameLife(shield.Container,shield.ContainerLife) && ReferenceEquals(shield.Container.shield,shield.Observed))
                            InkObserveShield(shield,after);
                    };
                    shield.Observed.onAmountModified+=shield.OnAmountModified;
                }
                else { shield.Normal=shield.Reward=0; shield.ObservedAmount=0; }
            }
            shield.RewardContainer=isReward;
            if(publish) PublishBossVisual(rt,shield.Visual,7,shield.Target.position,shield.Target.position,0,now,due,element:BossElement.Light,targetNetId:shield.Target.netId,nativeLife:isReward?shield.RewardLife:0);
            else SetBossVisualNativeSource(rt,shield.Visual,isReward?shield.RewardLife:0);
        }
        private void InkDestroyShield(HeroRuntime rt,InkShield shield,float now)
        {
            if(shield.Observed!=null && shield.OnAmountModified!=null) shield.Observed.onAmountModified-=shield.OnAmountModified;
            shield.Observed=null; shield.ObservedAmount=0; shield.ContainerUntil=0;
            if(shield.Container!=null && shield.Container.isActive && shield.Container.creationTime==shield.ContainerCreation && BossNativeSameLife(shield.Container,shield.ContainerLife)) shield.Container.DestroyIfActive();
            shield.Container=null;
            PublishBossVisual(rt,shield.Visual,7,shield.Target!=null?shield.Target.position:rt.Hero.position,rt.Hero.position,0,now,now,true,targetNetId:shield.Target!=null?shield.Target.netId:0);
        }
    }
}
