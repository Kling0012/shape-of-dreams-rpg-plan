using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class ErebosField
        {
            internal long Token;
            internal BossMoveEntry Entry;
            internal BossMoveProfile Profile;
            internal Vector3 Center, Direction, Origin;
            internal float Created, Until, Amount, SecondAmount, ThirdAmount;
            internal bool Pull, Changed, Magic;
            internal int Next;
            internal readonly long[] Visuals = new long[3];
        }
        private sealed class ErebosCombat
        {
            internal readonly List<ErebosField> Fields = new List<ErebosField>(2);
            internal readonly BossNativeSeen MainInputs = new BossNativeSeen(), MemoryInputs = new BossNativeSeen(),
                MovementInputs = new BossNativeSeen(), DamageInputs = new BossNativeSeen();
            internal int Boundary;
            internal float BoundaryUntil;
            internal long BoundaryVisual;
        }
        private readonly Dictionary<HeroRuntime,ErebosCombat> _erebosCombat = new Dictionary<HeroRuntime,ErebosCombat>();
        private ErebosCombat ErebosState(HeroRuntime rt)
        {
            if (!_erebosCombat.TryGetValue(rt,out var state)) _erebosCombat.Add(rt,state=new ErebosCombat());
            return state;
        }
        private static bool ErebosAvailable(HeroRuntime rt, BossAction action, float now)
            => !rt.Boss.Ready.TryGetValue(rt.Boss.ActionKeys[action],out float due) || now >= due;
        private ErebosField ErebosReserve(HeroRuntime rt, ErebosCombat state, BossMoveEntry entry, BossMoveProfile profile,
            Vector3 center, Vector3 direction, float now, int lifetime)
        {
            if (!BossReserveSequence(rt,entry.SetId,profile.Id,Array.Empty<BossScheduledPulse>(),now,2,lifetime,
                maxSetInstances:2,replaceOldest:true)) return null;
            ErebosReconcile(rt,state,now);
            var field = new ErebosField { Token=BossReservationId(rt,entry.SetId,profile.Id),Entry=entry,Profile=profile,
                Center=center,Origin=rt.Hero.agentPosition,Direction=direction,Created=now,Until=now+lifetime/1000f,Magic=BossMagic(rt) };
            state.Fields.Add(field);
            return field;
        }
        private void ErebosRemove(HeroRuntime rt, ErebosCombat state, int index, float now, bool cancel = true)
        {
            var field=state.Fields[index];
            if (cancel) BossCancelReservation(rt,field.Token);
            for (int i=0;i<field.Visuals.Length;i++) if (field.Visuals[i]!=0)
                PublishBossVisual(rt,field.Visuals[i],4,field.Center,field.Center,0,now,now,true);
            state.Fields.RemoveAt(index);
        }
        private void ErebosReconcile(HeroRuntime rt, ErebosCombat state, float now)
        {
            for (int i=state.Fields.Count-1;i>=0;i--)
                if (!BossReservationExists(rt,state.Fields[i].Token) || !BossFind(rt,state.Fields[i].Profile.Id,out _,out _))
                    ErebosRemove(rt,state,i,now);
            if (state.Boundary>0 && (now>=state.BoundaryUntil || !BossFind(rt,"boss_erebos.stage3",out _,out _)))
            { state.Boundary=0; ErebosBoundaryVisual(rt,state,now); }
            if (!BossFind(rt,"boss_erebos.stage6",out _,out _)) rt.Boss.Defense.CancelSource(this,rt,"boss_erebos.stage6");
        }
        private void DispatchErebosBoss(HeroRuntime rt, BossEvent kind, long activation, Entity victim, Vector3 point, float now)
        {
            if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
            if (kind!=BossEvent.MainHit && kind!=BossEvent.MemoryUse && kind!=BossEvent.MovementCompleted && kind!=BossEvent.NativeDamageTaken) return;
            bool equipped=false;
            foreach (var move in rt.Powers.Build.BossMoves) if (move.SetId==BossProfiles.ErebosSetId) { equipped=true; break; }
            if (!equipped) return;
            var state=ErebosState(rt);
            ErebosReconcile(rt,state,now);
            var inputs=kind==BossEvent.MainHit?state.MainInputs:kind==BossEvent.MemoryUse?state.MemoryInputs:
                kind==BossEvent.MovementCompleted?state.MovementInputs:state.DamageInputs;
            if (!inputs.Add(activation)) return;
            if (kind==BossEvent.MovementCompleted)
            {
                RelocateLastStarlight(rt,point,now);
                foreach (var field in state.Fields)
                {
                    if (field.Profile.Id=="boss_erebos.stage2" && field.Next==0 && !field.Changed && now<field.Until
                        && BossFind(rt,"boss_erebos.stage3",out _,out _))
                    { field.Pull=true; field.Changed=true; ErebosMarkerVisual(rt,field,now); }
                    else if (field.Profile.Id=="boss_erebos.stage6" && !field.Changed && now<field.Created+2.5f)
                    {
                        var direction=BossDirection(field.Center,point);
                        if (direction==Vector3.zero) continue;
                        field.Direction=direction; field.Changed=true;
                        if (field.Next>0) ErebosLineVisuals(rt,field,field.Created+2.5f);
                    }
                }
            }
            if (kind==BossEvent.MainHit) ErebosConsumeMarker(rt,state,now);
            if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
            BossMoveEntry finaleEntry=default;
            BossMoveProfile finaleProfile=null;
            Vector3 finaleCenter=default;
            bool finale=kind==BossEvent.MemoryUse && state.Boundary==3 && now<state.BoundaryUntil
                && BossFind(rt,"boss_erebos.stage6",out finaleEntry,out finaleProfile)
                && ErebosAvailable(rt,finaleProfile.Actions[0],now)
                && BossGround(rt.Hero.agentPosition,BossCursor(rt),8,out finaleCenter);
            if (finale) { state.Boundary=0; ErebosBoundaryVisual(rt,state,now); }
            if (kind==BossEvent.MemoryUse) ErebosMemory(rt,state,now);
            foreach (var entry in rt.Powers.Build.BossMoves)
            {
                if (entry.SetId!=BossProfiles.ErebosSetId || !BossProfiles.TryGetMove(entry.ProfileId,out var profile)
                    || profile.Id=="boss_erebos.stage2" || profile.Id=="boss_erebos.stage3" || profile.Id=="boss_erebos.stage6") continue;
                var action=profile.Actions[0];
                if (action.Event!=kind || !ErebosAvailable(rt,action,now)) continue;
                Vector3 origin=rt.Hero.agentPosition,center=kind==BossEvent.MovementCompleted?point:origin;
                var direction=BossDirection(origin,kind==BossEvent.MainHit?point:BossCursor(rt));
                if (direction==Vector3.zero) direction=rt.Hero.transform.forward;
                if (profile.Id=="boss_erebos.head" || profile.Id=="boss_erebos.hands")
                    if (!BossGround(origin,profile.Id=="boss_erebos.head"?BossCursor(rt):point,8,out center)) continue;
                int lifetime=profile.Id=="boss_erebos.hands"?1201:Math.Max(1,action.DelayMillis+(profile.Actions.Count>1?400:(action.Count-1)*action.IntervalMillis)+1);
                var field=ErebosReserve(rt,state,entry,profile,center,direction,now,lifetime);
                if (field==null) continue;
                field.Amount=BossAmount(rt,entry,profile,action.ChannelId);
                if (profile.Id=="boss_erebos.feet")
                {
                    field.SecondAmount=BossAmount(rt,entry,profile,profile.Actions[1].ChannelId);
                    field.ThirdAmount=BossAmount(rt,entry,profile,profile.Actions[2].ChannelId);
                }
                BossReady(rt,rt.Boss.ActionKeys[action],now,action.CooldownMillis);
                if (profile.Id=="boss_erebos.hands")
                {
                    float arrival=now+BossDirectionDistance(origin,center)/12;
                    for (int i=0;i<2;i++)
                        PublishBossVisual(rt,field.Visuals[i]=++rt.Boss.NextId,4,center,center,1,arrival+i*.2f,arrival+i*.2f+.25f,element:BossElement.Light);
                }
                else if (profile.Id=="boss_erebos.feet")
                    for (int i=0;i<3;i++) ErebosPulseVisual(rt,field,i,profile.Actions[i],now+profile.Actions[i].DelayMillis/1000f);
                else for (int i=0;i<action.Count;i++) ErebosPulseVisual(rt,field,i,action,now+(action.DelayMillis+i*action.IntervalMillis)/1000f);
            }
            // Reserve the finale last: ordinary E2 marker/parts remain cumulative,
            // and the literal total-two oldest replacement cannot evict it immediately.
            if (finale) ErebosFinale(rt,state,finaleEntry,finaleProfile,finaleCenter,now);
        }
        private void ErebosMemory(HeroRuntime rt, ErebosCombat state, float now)
        {
            if (!BossFind(rt,"boss_erebos.stage2",out var entry,out var profile)
                || !BossGround(rt.Hero.agentPosition,BossCursor(rt),8,out var center)) return;
            // A new E2 replaces only its own pending marker, not all owned fields.
            for (int i=state.Fields.Count-1;i>=0;i--) if (state.Fields[i].Profile.Id==profile.Id && state.Fields[i].Next==0) ErebosRemove(rt,state,i,now);
            var marker=ErebosReserve(rt,state,entry,profile,center,Vector3.zero,now,4001);
            if (marker!=null) { marker.Until=now+4; ErebosMarkerVisual(rt,marker,now); }
        }
        private void ErebosFinale(HeroRuntime rt,ErebosCombat state,BossMoveEntry entry,BossMoveProfile profile,Vector3 center,float now)
        {
            var direction=BossDirection(rt.Hero.agentPosition,center);
            if (direction==Vector3.zero) direction=rt.Hero.transform.forward;
            var field=ErebosReserve(rt,state,entry,profile,center,direction,now,3001);
            if (field==null)
            {
                state.Boundary=3; ErebosBoundaryVisual(rt,state,now); return;
            }
            field.Amount=BossAmount(rt,entry,profile,"ErebosFinale");
            BossReady(rt,rt.Boss.ActionKeys[profile.Actions[0]],now,10000);
            rt.Boss.Defense.Execute(this,rt,profile.Id,profile.Actions[2],BossAmount(rt,entry,profile,"ErebosFinaleShield"),now);
            PublishBossVisual(rt,field.Visuals[0]=++rt.Boss.NextId,4,center,center,4,now,now+3,element:BossElement.Light);
        }
        private void ErebosConsumeMarker(HeroRuntime rt, ErebosCombat state, float now)
        {
            for (int i=0;i<state.Fields.Count;i++)
            {
                var marker=state.Fields[i];
                if (marker.Profile.Id!="boss_erebos.stage2" || marker.Next!=0 || now>=marker.Until) continue;
                var action=marker.Profile.Actions[1];
                if (!ErebosAvailable(rt,action,now)) return;
                // The consumed marker becomes a fresh short-lived ripple token. In
                // particular, consuming just before the four-second deadline must
                // not discard the second ripple when the old token expires.
                BossCancelReservation(rt,marker.Token);
                if (!BossReserveSequence(rt,marker.Entry.SetId,marker.Profile.Id,Array.Empty<BossScheduledPulse>(),now,2,301,
                    maxSetInstances:2,replaceOldest:true)) { ErebosRemove(rt,state,i,now); return; }
                marker.Token=BossReservationId(rt,marker.Entry.SetId,marker.Profile.Id);
                marker.SecondAmount=BossAmount(rt,marker.Entry,marker.Profile,marker.Profile.Actions[2].ChannelId);
                bool stage3=BossFind(rt,"boss_erebos.stage3",out _,out _);
                if (stage3) ErebosMoveEnemies(rt,marker.Center,3,3,marker.Pull,.8f,.3f,now);
                rt.Boss.Shapes.Execute(this,rt,action,marker.Center,marker.Center,BossAmount(rt,marker.Entry,marker.Profile,action.ChannelId),marker.Magic);
                if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
                PublishBossVisual(rt,++rt.Boss.NextId,1,marker.Center,marker.Center,2,now,now+.25f,element:BossElement.Light);
                BossReady(rt,rt.Boss.ActionKeys[action],now,6000);
                marker.Next=1; marker.Created=now; marker.Until=now+.301f;
                ErebosPulseVisual(rt,marker,0,marker.Profile.Actions[2],now+.3f);
                if (stage3) { state.Boundary=Math.Min(3,state.Boundary+1); state.BoundaryUntil=now+12; ErebosBoundaryVisual(rt,state,now); }
                return;
            }
        }
        private void ErebosMoveEnemies(HeroRuntime rt, Vector3 center, float radius, int max, bool pull, float distance, float duration, float now)
        {
            ListReturnHandle<Entity> handle;
            var enemies=DewPhysics.OverlapCircleAllEntities(out handle,center,radius,EnemyFilter,rt.Hero);
            try
            {
                int moved=0;
                foreach (var enemy in enemies)
                    if (rt.Boss.EnemyMovement.Execute(rt,enemy,center,pull,distance,now,duration:duration) && ++moved>=max) break;
            }
            finally { handle.Return(); }
        }
        private void ErebosMarkerVisual(HeroRuntime rt, ErebosField field, float now)
        {
            if (field.Visuals[0]==0) field.Visuals[0]=++rt.Boss.NextId;
            PublishBossVisual(rt,field.Visuals[0],4,field.Center,field.Center+Vector3.forward,2,now,field.Until,element:BossElement.Light,
                shape:BossShape.Radial,count:4,range:2,angle:field.Pull?180:0);
        }
        private void ErebosBoundaryVisual(HeroRuntime rt, ErebosCombat state, float now)
        {
            if (state.BoundaryVisual==0 && state.Boundary>0) state.BoundaryVisual=++rt.Boss.NextId;
            if (state.BoundaryVisual==0) return;
            PublishBossVisual(rt,state.BoundaryVisual,9,rt.Hero.agentPosition,rt.Hero.agentPosition,state.Boundary,now,state.Boundary>0?state.BoundaryUntil:now,
                state.Boundary==0,element:BossElement.Light,count:state.Boundary);
            if (state.Boundary==0) state.BoundaryVisual=0;
        }
        private void ErebosPulseVisual(HeroRuntime rt,ErebosField field,int index,BossAction action,float due)
        {
            if (field.Visuals[index]==0) field.Visuals[index]=++rt.Boss.NextId;
            PublishBossVisual(rt,field.Visuals[index],action.Shape==BossShape.Line?6:4,field.Center,field.Center+field.Direction*(action.RangeMilli/1000f),
                action.RadiusMilli/1000f,due,due+.25f,element:action.Element,shape:action.Shape,range:action.RangeMilli/1000f,width:action.WidthMilli/1000f);
        }
        private void ErebosLineVisuals(HeroRuntime rt,ErebosField field,float due)
        {
            for (int i=0;i<2;i++)
            {
                Vector3 direction=i==0?field.Direction:Quaternion.AngleAxis(90,Vector3.up)*field.Direction;
                if (field.Visuals[i+1]==0) field.Visuals[i+1]=++rt.Boss.NextId;
                PublishBossVisual(rt,field.Visuals[i+1],6,field.Center-direction*3,field.Center+direction*3,0,due,due+.25f,
                    element:BossElement.Light,shape:BossShape.Line,range:6,width:.6f);
            }
        }
        private void TickErebosBoss(HeroRuntime rt,float now)
        {
            TickLastStarlight(rt,now);
            if (!_erebosCombat.TryGetValue(rt,out var state)) return;
            ErebosReconcile(rt,state,now);
            for (int i=state.Fields.Count-1;i>=0;i--)
            {
                var field=state.Fields[i]; var action=field.Profile.Actions[0]; string id=field.Profile.Id;
                if (id=="boss_erebos.stage2")
                {
                    if (field.Next==1 && now>=field.Created+.3f)
                    {
                        var ripple=field.Profile.Actions[2];
                        rt.Boss.Shapes.Execute(this,rt,ripple,field.Center,field.Center,field.SecondAmount,field.Magic);
                        if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
                        ErebosRemove(rt,state,i,now);
                    }
                    else if (field.Next==0 && now>=field.Until) ErebosRemove(rt,state,i,now);
                    continue;
                }
                if (id=="boss_erebos.stage6")
                {
                    if (field.Next==0 && now>=field.Created+2) { ErebosLineVisuals(rt,field,field.Created+2.5f); field.Next=1; }
                    if (field.Next==1 && now>=field.Created+2.5f)
                    {
                        var line=field.Profile.Actions[1];
                        for (int j=0;j<2;j++)
                        {
                            var direction=j==0?field.Direction:Quaternion.AngleAxis(90,Vector3.up)*field.Direction;
                            rt.Boss.Shapes.Execute(this,rt,line,field.Center-direction*3,field.Center+direction*3,field.Amount/4,field.Magic);
                            if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
                        }
                        field.Next=2;
                    }
                    if (field.Next==2 && now>=field.Created+3)
                    {
                        rt.Boss.Shapes.Execute(this,rt,action,field.Center,field.Center,field.Amount/2,field.Magic);
                        if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
                        ErebosRemove(rt,state,i,now);
                    }
                    continue;
                }
                if (id=="boss_erebos.hands")
                {
                    while (field.Next<2 && now>=field.Created+field.Next*.2f)
                    {
                        rt.Boss.Projectiles.Execute(this,rt,field.Entry.SetId,action,field.Origin,field.Center,field.Amount,field.Magic,field.Created+field.Next*.2f,
                            shotCount:1,maxHitsPerTarget:1,explodeAtEnd:true,terminalRadius:1,reservationId:field.Token,delayedAlready:true);
                        field.Next++;
                    }
                    if (now>=field.Until) ErebosRemove(rt,state,i,now,cancel:false);
                    continue;
                }
                int count=id=="boss_erebos.feet"?3:action.Count;
                while (field.Next<count)
                {
                    var pulse=id=="boss_erebos.feet"?field.Profile.Actions[field.Next]:action;
                    int delay=id=="boss_erebos.feet"?pulse.DelayMillis:action.DelayMillis+field.Next*action.IntervalMillis;
                    if (now<field.Created+delay/1000f) break;
                    rt.Boss.Shapes.Execute(this,rt,pulse,field.Center,field.Center+field.Direction*(pulse.RangeMilli/1000f),
                        id=="boss_erebos.feet"?(field.Next==0?field.Amount:field.Next==1?field.SecondAmount:field.ThirdAmount):field.Amount,field.Magic);
                    if (rt.Boss.Build==null || !BossAlive(rt.Hero)) return;
                    if (id=="boss_erebos.armor") ErebosMoveEnemies(rt,field.Center,2,64,false,.5f,.2f,now);
                    else if (id=="boss_erebos.charm") ErebosMoveEnemies(rt,field.Center,2.5f,3,false,.6f,.3f,now);
                    field.Next++;
                }
                if (field.Next==count) ErebosRemove(rt,state,i,now);
            }
        }
        private void ClearErebosBoss(HeroRuntime rt,bool preserveRewards=false)
        {
            rt.Boss.Defense.CancelSource(this,rt,"boss_erebos.stage6");
            if (_erebosCombat.TryGetValue(rt,out var state))
            {
                for (int i=state.Fields.Count-1;i>=0;i--) ErebosRemove(rt,state,i,Time.time);
                state.Boundary=0; ErebosBoundaryVisual(rt,state,Time.time); _erebosCombat.Remove(rt);
            }
            if (!preserveRewards) ClearLastStarlight(rt);
        }
    }
}
