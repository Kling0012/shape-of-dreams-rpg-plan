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
    // The native Hit has exactly one damage dispatch and one heal dispatch. Scope that
    // actual branch, not generic ancestor damage/heal events or guessed main attacks.
    [HarmonyPatch(typeof(Ai_U_BeamOfBalance_Beam),"Hit")]
    internal static class InkBeamNativeHit
    {
        internal struct Scope { internal Ai_U_BeamOfBalance_Beam Beam; internal Entity Target; internal bool Enemy; }
        internal static Scope Current;
        private static bool Prepare()
        {
            MethodInfo method=AccessTools.DeclaredMethod(typeof(Ai_U_BeamOfBalance_Beam),"Hit");
            int damage=0,heal=0,branch=0;
            foreach(var instruction in PatchProcessor.ReadMethodBody(method))
                if((instruction.Key==OpCodes.Call || instruction.Key==OpCodes.Callvirt) && instruction.Value is MethodInfo called)
                {
                    if(called.DeclaringType==typeof(DamageData) && called.Name==nameof(DamageData.Dispatch)) damage++;
                    if(called.DeclaringType==typeof(HealData) && called.Name==nameof(HealData.Dispatch)) heal++;
                    if(called.Name==nameof(Entity.CheckEnemyOrNeutral)) branch++;
                }
            if(damage!=1 || heal!=1 || branch!=1) throw new InvalidOperationException("Beam native Hit contract changed; refusing reward interception.");
            return true;
        }
        private static bool ObserveNativeBranch(Entity caster,Entity target)
        {
            bool enemy=caster.CheckEnemyOrNeutral(target);
            if(Current.Beam!=null && Current.Target==target) Current.Enemy=enemy;
            return enemy;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var observe=AccessTools.DeclaredMethod(typeof(InkBeamNativeHit),nameof(ObserveNativeBranch));
            int sites=0;
            foreach(var instruction in instructions)
            {
                if((instruction.opcode==OpCodes.Call || instruction.opcode==OpCodes.Callvirt) && instruction.operand is MethodInfo called
                    && called.DeclaringType==typeof(Entity) && called.Name==nameof(Entity.CheckEnemyOrNeutral))
                { instruction.opcode=OpCodes.Call; instruction.operand=observe; sites++; }
                yield return instruction;
            }
            if(sites!=1) throw new InvalidOperationException("Beam native branch extraction changed; refusing reward interception.");
        }
        private static void Prefix(Ai_U_BeamOfBalance_Beam __instance,Entity e,out Scope __state)
        {
            __state=Current;
            Current=NetworkServer.active && __instance.info.caster!=null
                ? new Scope{Beam=__instance,Target=e}:default(Scope);
            if(NetworkServer.active) HostAuthority.NativeInstance?.PrepareInkBeamHit(__instance);
        }
        private static void Finalizer(Scope __state) { Current=__state; }
    }
    [HarmonyPatch(typeof(Se_U_BeamOfBalance),"OnCreate")]
    internal static class InkBeamParentCreation
    {
        private static void Prefix(Se_U_BeamOfBalance __instance) { if(NetworkServer.active) HostAuthority.NativeInstance?.BeginInkBeam(__instance); }
    }
    [HarmonyPatch(typeof(Se_U_BeamOfBalance),"OnDestroyActor")]
    internal static class InkBeamParentCompletion
    {
        private static void Prefix(Se_U_BeamOfBalance __instance) { if(NetworkServer.active) HostAuthority.NativeInstance?.EndInkBeam(__instance); }
    }
    [HarmonyPatch(typeof(Ai_U_BeamOfBalance_Beam),"OnDisable")]
    internal static class InkBeamPoolCompletion
    {
        private static void Prefix(Ai_U_BeamOfBalance_Beam __instance) { if(NetworkServer.active) HostAuthority.NativeInstance?.EndInkBeamChild(__instance); }
    }
    internal sealed partial class HostAuthority
    {
        private struct InkDwell
        {
            internal Entity Target;
            internal float Creation,First,Last;
            internal long TargetLife;
        }
        private sealed class InkBeamState
        {
            internal HostAuthority Host;
            internal HeroRuntime Runtime;
            internal Se_U_BeamOfBalance Parent;
            internal Ai_U_BeamOfBalance_Beam Beam;
            internal St_U_BeamOfBalance Skill;
            internal Actor ParentActor;
            internal float ParentCreation,BeamCreation,SkillCreation,H;
            internal Room Room;
            internal string Run;
            internal long Life,LastAddedPacket,Visual,DarkVisual;
            internal long ParentLife,BeamLife,SkillLife,ParentActorLife;
            internal readonly long[] DwellVisuals;
            internal bool Magic;
            internal bool WhiteDisabled,DarkDisabled;
            internal int WhiteStage,DarkStage,Adds,Spears,Illuminations,WhiteTargets;
            internal float WhiteSpent,DarkSpent,NextVisual;
            internal readonly Hero[] WhiteHeroes;
            internal readonly long[] WhiteHeroLives;
            internal readonly InkDwell[] Dwell;
            internal BossRewardAction White,Flat,Spear,Illumination;
            internal BossAction SpearAction;
            internal Action<EventInfoAbilityInstance> Created;
            internal Action<EventInfoDamage> Damaged;
            internal Action<EventInfoHeal> Healed;
            internal DataProcessor<DamageData,Actor,Entity> Processor;
            internal bool ProcessorAttached,DamageAttached,HealAttached;
            internal readonly List<long> PhantomVisuals=new List<long>(12);
            internal InkBeamState()
            {
                int heroes=InkRewardAction(BossProfiles.WhiteNightRewardId,3,BossRewardActionKind.NativeShield).Count;
                int enemies=InkRewardAction(BossProfiles.DarkMoonRewardId,3,BossRewardActionKind.NativeDamage).MagnitudeMilli;
                WhiteHeroes=new Hero[heroes]; WhiteHeroLives=new long[heroes];
                Dwell=new InkDwell[enemies]; DwellVisuals=new long[enemies];
                Created=OnCreated; Damaged=OnDamage; Healed=OnHeal; Processor=OnProcess;
            }
            private void OnCreated(EventInfoAbilityInstance info) => Host.BindInkBeam(this,info);
            private void OnDamage(EventInfoDamage info) => Host.InkBeamDamage(this,info);
            private void OnHeal(EventInfoHeal info) => Host.InkBeamHeal(this,info);
            private void OnProcess(ref DamageData damage,Actor actor,Entity target) => Host.InkBeamFlat(this,ref damage,actor,target);
        }
        private static readonly BossRewardProfile InkWhiteRewardProfile=LoadInkRewardProfile(BossProfiles.WhiteNightRewardId);
        private static readonly BossRewardProfile InkDarkRewardProfile=LoadInkRewardProfile(BossProfiles.DarkMoonRewardId);
        private static readonly int InkWhiteShieldCap=LoadInkWhiteShieldCap();
        private static BossRewardProfile LoadInkRewardProfile(string id)
        {
            BossProfiles.TryGetReward(id,out var profile);
            return profile;
        }
        private static int LoadInkWhiteShieldCap()
        {
            BossProfiles.TryGetMove("boss_white_night.stage6",out var profile);
            return profile.Actions[1].MagnitudeMilli;
        }
        private static readonly BossAction InkNativeSpearAction=CreateInkNativeSpearAction();
        private static BossAction CreateInkNativeSpearAction()
        {
            var spear=InkRewardAction(BossProfiles.DarkMoonRewardId,2,BossRewardActionKind.NativeTarget);
            return new BossAction(BossEvent.NativeDamageDealt,BossMechanism.Projectile,BossPayload.Damage,maxInstances:1,rangeMilli:spear.RangeMilli,widthMilli:spear.WidthMilli,speedMilli:spear.SpeedMilli,firstHitOnly:true,element:BossElement.Dark);
        }
        private readonly BossObjectPool<InkBeamState> _inkBeamPool=new BossObjectPool<InkBeamState>(64, () => new InkBeamState());
        private readonly Dictionary<Se_U_BeamOfBalance,InkBeamState> _inkBeams=new Dictionary<Se_U_BeamOfBalance,InkBeamState>(64);
        private readonly List<Se_U_BeamOfBalance> _inkBeamScratch=new List<Se_U_BeamOfBalance>(64);
        internal void BeginInkBeam(Se_U_BeamOfBalance parent)
        {
            if(parent==null || !(parent.info.caster is Hero hero) || !Alive(hero) || !_runtimes.TryGetValue(hero,out var rt) || AttributionGeneratedOrigin()!=GeneratedOrigin.None || parent.gem!=null) return;
            var skill=parent.FindFirstAncestorOfType<SkillTrigger>() as St_U_BeamOfBalance;
            if(skill==null || skill.owner!=hero || FindMemory(hero,nameof(St_U_BeamOfBalance))!=skill) return;
            EndInkBeam(parent);
            if(_inkBeams.Count>=64) return;
            var ink=InkGet(rt); if(ink==null) return; InkReconcile(rt,ink);
            var state=_inkBeamPool.Rent(); if(state==null) return;
            state.Host=this; state.Runtime=rt; state.Parent=parent; state.ParentCreation=parent.creationTime; state.ParentActor=parent.parentActor; state.Skill=skill; state.SkillCreation=skill.creationTime;
            state.H=Math.Max(hero.Status.attackDamage,hero.Status.abilityPower); state.Room=NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom; state.Run=NetworkedManagerBase<GameManager>.softInstance?.runId; state.Life=_memoryAttribution.NewPacketId(); state.Visual=++rt.Boss.NextId;
            state.ParentLife=BossNativeActorLife(parent); state.ParentActorLife=BossNativeActorLife(parent.parentActor); state.SkillLife=BossNativeActorLife(skill);
            state.DarkVisual=++rt.Boss.NextId;
            for(int i=0;i<state.DwellVisuals.Length;i++) state.DwellVisuals[i]=++rt.Boss.NextId;
            state.WhiteStage=BossRewardStage(rt,BossProfiles.WhiteNightRewardId); state.DarkStage=BossRewardStage(rt,BossProfiles.DarkMoonRewardId);
            InkConfigureBeam(state);
            parent.ActorEvent_OnAbilityInstanceCreated+=state.Created;
            _inkBeams.Add(parent,state);
            ink.Beams.Add(state);
        }
        private static BossRewardAction InkRewardAction(string id,int stage,BossRewardActionKind kind)
        {
            if(stage<=0) return null;
            var profile=id==BossProfiles.WhiteNightRewardId?InkWhiteRewardProfile:InkDarkRewardProfile;
            var actions=profile.Stages[stage-1].Actions;
            for(int i=0;i<actions.Count;i++) if(actions[i].Kind==kind) return actions[i];
            return null;
        }
        private static void InkConfigureBeam(InkBeamState state)
        {
            state.White=InkRewardAction(BossProfiles.WhiteNightRewardId,state.WhiteStage,BossRewardActionKind.NativeShield);
            state.Flat=InkRewardAction(BossProfiles.DarkMoonRewardId,state.DarkStage,BossRewardActionKind.NativeDamage);
            var spear=InkRewardAction(BossProfiles.DarkMoonRewardId,state.DarkStage,BossRewardActionKind.NativeTarget);
            state.Spear=spear;
            state.Illumination=InkRewardAction(BossProfiles.DarkMoonRewardId,state.DarkStage,BossRewardActionKind.NativeMode);
            state.SpearAction=state.Spear!=null?InkNativeSpearAction:null;
        }
        private void BindInkBeam(InkBeamState state,EventInfoAbilityInstance info)
        {
            if(state.Beam!=null || info.actor!=state.Parent || !(info.instance is Ai_U_BeamOfBalance_Beam beam) || beam.info.caster!=state.Runtime.Hero || beam.parentActor!=state.Parent || beam.gem!=null || !InkBeamParentCurrent(state)) return;
            state.Beam=beam; state.BeamCreation=beam.creationTime;
            state.BeamLife=BossNativeActorLife(beam);
            state.H=Math.Max(state.Runtime.Hero.Status.attackDamage,state.Runtime.Hero.Status.abilityPower); state.Magic=BossMagic(state.Runtime);
            InkSyncBeamSubscriptions(state);
            InkPublishBeam(state,Time.time);
        }
        private void InkSyncBeamSubscriptions(InkBeamState state)
        {
            if(state.Beam==null || !BossNativeSameLife(state.Beam,state.BeamLife)) return;
            bool dark=state.Flat!=null && !state.DarkDisabled,white=state.White!=null && !state.WhiteDisabled;
            if(dark!=state.ProcessorAttached)
            {
                if(dark) state.Beam.dealtDamageProcessor.Add(state.Processor);
                else state.Beam.dealtDamageProcessor.Remove(state.Processor);
                state.ProcessorAttached=dark;
            }
            if(dark!=state.DamageAttached)
            {
                if(dark) state.Beam.ActorEvent_OnDealDamage+=state.Damaged;
                else state.Beam.ActorEvent_OnDealDamage-=state.Damaged;
                state.DamageAttached=dark;
            }
            if(white!=state.HealAttached)
            {
                if(white) state.Beam.ActorEvent_OnDoHeal+=state.Healed;
                else state.Beam.ActorEvent_OnDoHeal-=state.Healed;
                state.HealAttached=white;
            }
        }
        internal void PrepareInkBeamHit(Ai_U_BeamOfBalance_Beam beam)
        {
            if(beam==null || !(beam.parentActor is Se_U_BeamOfBalance parent) || !_inkBeams.TryGetValue(parent,out var state) || AttributionGeneratedOrigin()!=GeneratedOrigin.None) return;
            BossEnsure(state.Runtime);
            if(!InkBeamCurrent(state)) return;
            InkReconcile(state.Runtime,InkGet(state.Runtime));
            InkSyncBeamSubscriptions(state);
        }
        private bool InkBeamParentCurrent(InkBeamState state)
        {
            return NetworkServer.active && state.Parent!=null && state.Parent.isActive && state.Parent.creationTime==state.ParentCreation && state.Parent.parentActor==state.ParentActor
                && BossNativeSameLife(state.Parent,state.ParentLife) && BossNativeSameLife(state.ParentActor,state.ParentActorLife) && BossNativeSameLife(state.Skill,state.SkillLife)
                && state.Parent.info.caster==state.Runtime.Hero && Alive(state.Runtime.Hero) && !state.Runtime.Hero.isKnockedOut && state.Skill!=null && state.Skill.isActive && state.Skill.creationTime==state.SkillCreation
                && state.Skill.owner==state.Runtime.Hero && FindMemory(state.Runtime.Hero,nameof(St_U_BeamOfBalance))==state.Skill
                && _runtimes.TryGetValue(state.Runtime.Hero,out var rt) && rt==state.Runtime
                && BossNativeContextCurrent(state.Room,state.Run);
        }
        private bool InkBeamCurrent(InkBeamState state)
            => InkBeamParentCurrent(state) && state.Beam!=null && state.Beam.isActive && state.Beam.creationTime==state.BeamCreation && BossNativeSameLife(state.Beam,state.BeamLife) && state.Beam.parentActor==state.Parent && state.Beam.info.caster==state.Runtime.Hero && state.Beam.gem==null;
        private bool InkBeamNative(InkBeamState state,Actor actor,Entity target,bool enemy)
        {
            var hit=InkBeamNativeHit.Current;
            if(hit.Beam!=state.Beam || hit.Target!=target || hit.Enemy!=enemy || actor!=state.Beam || AttributionGeneratedOrigin()!=GeneratedOrigin.None || !InkBeamCurrent(state)) return false;
            if(!BossEnsure(state.Runtime)) return false;
            InkReconcile(state.Runtime,InkGet(state.Runtime));
            return true;
        }
        private float InkDwellGap(InkBeamState state)
            => state.Beam.hitInterval+state.Beam.checkInterval+state.Flat.GapToleranceMillis/1000f;
        private int InkDwellSlot(InkBeamState state,Entity target,float now,bool allocate)
        {
            if(state.Flat==null) return -1;
            float gap=InkDwellGap(state); int slot=-1;
            for(int i=0;i<state.Dwell.Length;i++)
            {
                var row=state.Dwell[i];
                if(row.Target==target && row.Creation==target.creationTime && BossNativeSameLife(target,row.TargetLife) && now-row.Last<=gap) return i;
                bool expired=row.Target==null || !row.Target.isActive || row.Target.creationTime!=row.Creation || !BossNativeSameLife(row.Target,row.TargetLife) || now-row.Last>gap;
                if(!allocate || !expired) continue;
                if(slot<0 || row.Last<state.Dwell[slot].Last || row.Last==state.Dwell[slot].Last && (row.Target==null?0:row.Target.netId)<(state.Dwell[slot].Target==null?0:state.Dwell[slot].Target.netId)) slot=i;
            }
            return slot;
        }
        private void InkBeamFlat(InkBeamState state,ref DamageData damage,Actor from,Entity target)
        {
            if(!InkBeamNative(state,from,target,true) || state.DarkDisabled || state.Flat==null || target==null) return;
            var packet=NativeAttributedDamagePacket.Current;
            if(packet==null || packet.Actor!=state.Beam || packet.Victim!=target || !packet.Chain.Equals(default(ReactionChain)) || packet.Serial==state.LastAddedPacket) return;
            float now=Time.time;
            var ink=InkGet(state.Runtime);
            if(state.Adds>=state.Flat.Count || now<ink.NextNativeAdd) return;
            int slot=InkDwellSlot(state,target,now,false); if(slot<0) return;
            var row=state.Dwell[slot];
            if(now-row.First<state.Flat.DwellMillis/1000f) return;
            float amount=Math.Min(state.H*state.Flat.ValueMilli/1000f,state.H*state.Flat.BudgetMilli/1000f-state.DarkSpent);
            if(amount<=0) return;
            damage.AddFlatAmount(amount);
            // Spend on the packet; an immune/failed dispatch never refunds this independent gate.
            state.Adds++; state.DarkSpent+=amount; ink.NextNativeAdd=now+state.Flat.CooldownMillis/1000f; state.LastAddedPacket=packet.Serial;
            InkPublishBeam(state,now);
        }
        private void InkBeamHeal(InkBeamState state,EventInfoHeal info)
        {
            if(!InkBeamNative(state,info.actor,info.target,false) || state.WhiteDisabled || state.White==null || !info.chain.Equals(default(ReactionChain)) || !(info.target is Hero target) || !BossAlive(target) || info.amount+info.discardedAmount<=0) return;
            int slot=-1;
            for(int i=0;i<state.WhiteTargets;i++) if(state.WhiteHeroes[i]==target && BossNativeSameLife(target,state.WhiteHeroLives[i])) { slot=i; break; }
            if(slot<0)
            {
                if(state.WhiteTargets>=state.White.Count) return;
                slot=state.WhiteTargets++; state.WhiteHeroes[slot]=target; state.WhiteHeroLives[slot]=BossNativeActorLife(target);
            }
            if(slot>=state.White.Count) return;
            if(info.discardedAmount<=0) return;
            float amount=Math.Min(info.discardedAmount*state.White.ValueMilli/1000f,Math.Min(state.H*state.White.TargetCapMilli/1000f,state.H*state.White.BudgetMilli/1000f-state.WhiteSpent));
            if(amount<=0) return;
            state.WhiteSpent+=amount;
            InkGiveShield(state.Runtime,InkGet(state.Runtime),target,amount,state.H,state.White.DurationMillis/1000f,true,Time.time,InkWhiteShieldCap,state.Life);
            InkPublishBeam(state,Time.time);
        }
        private void InkBeamDamage(InkBeamState state,EventInfoDamage info)
        {
            if(!InkBeamNative(state,info.actor,info.victim,true) || state.DarkDisabled || state.Flat==null || !info.chain.Equals(default(ReactionChain)) || info.damage.amount<=0) return;
            var packet=NativeAttributedDamagePacket.Current;
            if(packet==null || packet.Actor!=state.Beam || packet.Victim!=info.victim) return;
            float now=Time.time; int slot=InkDwellSlot(state,info.victim,now,true);
            if(slot>=0)
            {
                var row=state.Dwell[slot];
                if(row.Target!=info.victim || row.Creation!=info.victim.creationTime || !BossNativeSameLife(info.victim,row.TargetLife) || now-row.Last>InkDwellGap(state)) row=new InkDwell{Target=info.victim,Creation=info.victim.creationTime,TargetLife=BossNativeActorLife(info.victim),First=now};
                row.Last=now; state.Dwell[slot]=row;
            }
            // Spear and illumination are independent of dwell qualification/addition budget.
            // Cadence target selection is this host event's victim; clients never choose a first enemy.
            Vector3 point=info.victim.position;
            var ink=InkGet(state.Runtime);
            if(state.Spear!=null && state.Spears<state.Spear.Count && now>=ink.NextNativeSpear)
            {
                if(state.Runtime.Boss.Projectiles.Execute(this,state.Runtime,BossProfiles.DarkMoonSetId,state.SpearAction,state.Beam.position,point,state.H*state.Spear.ValueMilli/1000f,state.Magic,now,profile:BossProfiles.DarkMoonRewardId,nativeLife:state.Life,adoptedTarget:info.victim))
                { state.Spears++; ink.NextNativeSpear=now+state.Spear.CooldownMillis/1000f; }
            }
            if(state.Illumination!=null && state.Illuminations<state.Illumination.Count && now>=ink.NextNativeIllumination)
            {
                if(InkDarkForm(state.Runtime,ink,packet.Serial,info.victim,point,now,state))
                { state.Illuminations++; ink.NextNativeIllumination=now+state.Illumination.CooldownMillis/1000f; }
            }
            InkPublishBeam(state,now);
        }
        private void InkPublishBeam(InkBeamState state,float now)
        {
            if(state.Beam==null || !state.Beam.isActive) return;
            float due=state.Beam.hitEndTime;
            if(float.IsInfinity(due) || float.IsNaN(due)) return;
            if(now<state.NextVisual) return;
            state.NextVisual=now+1f;
            if(state.White!=null && !state.WhiteDisabled) PublishBossVisual(state.Runtime,state.Visual,9,state.Beam.position,state.Beam.position,0,now,due,element:BossElement.Light,count:Math.Max(0,state.White.Count-state.WhiteTargets),budget:state.H<=0?0:Math.Max(0,state.White.BudgetMilli-state.WhiteSpent/state.H*1000),nativeLife:state.Life);
            if(state.Flat!=null && !state.DarkDisabled)
            {
                PublishBossVisual(state.Runtime,state.DarkVisual,9,state.Beam.position,state.Beam.position,state.Flat.DwellMillis/1000f,now,due,element:BossElement.Dark,count:Math.Max(0,state.Flat.Count-state.Adds),budget:state.H<=0?0:Math.Max(0,state.Flat.BudgetMilli-state.DarkSpent/state.H*1000),nativeLife:state.Life);
                for(int i=0;i<state.Dwell.Length;i++)
                {
                    var row=state.Dwell[i];
                    if(row.Target!=null && row.Target.isActive && BossNativeSameLife(row.Target,row.TargetLife) && now-row.Last<=InkDwellGap(state))
                        PublishBossVisual(state.Runtime,state.DwellVisuals[i],9,state.Beam.position,row.Target.position,Math.Min(state.Flat.DwellMillis/1000f,now-row.First),row.First,row.Last+InkDwellGap(state),element:BossElement.Dark,count:state.Spear==null?0:state.Spear.Count-state.Spears,budget:state.Illumination==null?0:state.Illumination.Count-state.Illuminations,targetNetId:row.Target.persistentNetId,nativeLife:state.Life);
                }
            }
        }
        private static void InkRegisterBeamVisual(InkBeamState state,long visual)
        {
            if(state!=null && state.PhantomVisuals.Count<12) state.PhantomVisuals.Add(visual);
        }
        private void InkReapplyBeamProfile(HeroRuntime rt,bool white)
        {
            if(!white) BossCancelProfileReservations(rt,BossProfiles.DarkMoonSetId,BossProfiles.DarkMoonRewardId);
            if(!_inkStates.TryGetValue(rt,out var owner)) return;
            foreach(var state in owner.Beams)
            {
                if(white)
                {
                    state.WhiteStage=BossRewardStage(rt,BossProfiles.WhiteNightRewardId);
                    PublishBossVisual(rt,state.Visual,9,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                }
                else
                {
                    state.DarkStage=BossRewardStage(rt,BossProfiles.DarkMoonRewardId);
                    Array.Clear(state.Dwell,0,state.Dwell.Length);
                    BossCancelNativeReservations(rt,BossProfiles.DarkMoonSetId,state.Life);
                    foreach(long visual in state.DwellVisuals) PublishBossVisual(rt,visual,9,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                    foreach(long visual in state.PhantomVisuals) PublishBossVisual(rt,visual,8,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                    state.PhantomVisuals.Clear();
                    PublishBossVisual(rt,state.DarkVisual,9,rt.Hero.position,rt.Hero.position,0,Time.time,Time.time,true);
                }
                InkConfigureBeam(state);
                state.NextVisual=0;
                state.WhiteDisabled=state.WhiteStage==0; state.DarkDisabled=state.DarkStage==0;
                InkSyncBeamSubscriptions(state);
            }
        }
        private void InkReconcileBeamSkills(HeroRuntime rt)
        {
            if(!_inkStates.TryGetValue(rt,out var owner)) return;
            foreach(var state in owner.Beams)
            {
                if(FindMemory(rt.Hero,nameof(St_U_BeamOfBalance))!=state.Skill) { state.WhiteDisabled=true; state.DarkDisabled=true; InkSyncBeamSubscriptions(state); continue; }
                int white=BossRewardStage(rt,BossProfiles.WhiteNightRewardId),dark=BossRewardStage(rt,BossProfiles.DarkMoonRewardId);
                if(white==state.WhiteStage && dark==state.DarkStage) continue;
                if(dark!=state.DarkStage) Array.Clear(state.Dwell,0,state.Dwell.Length);
                state.WhiteStage=white; state.DarkStage=dark;
                InkConfigureBeam(state);
                state.WhiteDisabled=white==0; state.DarkDisabled=dark==0;
                InkSyncBeamSubscriptions(state);
            }
        }
        private void TickInkBeams(HeroRuntime rt,float now)
        {
            if(!_inkStates.TryGetValue(rt,out var owner)) return;
            for(int i=owner.Beams.Count-1;i>=0;i--)
            {
                var state=owner.Beams[i];
                if(!InkBeamCurrent(state)) EndInkBeam(state.Parent);
                else InkPublishBeam(state,now);
            }
        }
        internal void EndInkBeamChild(Ai_U_BeamOfBalance_Beam beam)
        {
            _inkBeamScratch.Clear(); foreach(var pair in _inkBeams) if(pair.Value.Beam==beam) _inkBeamScratch.Add(pair.Key);
            foreach(var parent in _inkBeamScratch) EndInkBeam(parent);
        }
        internal void EndInkBeam(Se_U_BeamOfBalance parent)
        {
            if(ReferenceEquals(parent,null) || !_inkBeams.TryGetValue(parent,out var state)) return;
            _inkBeams.Remove(parent);
            if(_inkStates.TryGetValue(state.Runtime,out var owner)) owner.Beams.Remove(state);
            if(state.Parent!=null && state.Parent.creationTime==state.ParentCreation && BossNativeSameLife(state.Parent,state.ParentLife)) state.Parent.ActorEvent_OnAbilityInstanceCreated-=state.Created;
            if(state.Beam!=null && state.Beam.creationTime==state.BeamCreation && BossNativeSameLife(state.Beam,state.BeamLife))
            {
                state.Beam.dealtDamageProcessor.Remove(state.Processor);
                state.Beam.ActorEvent_OnDealDamage-=state.Damaged;
                state.Beam.ActorEvent_OnDoHeal-=state.Healed;
            }
            if(_inkStates.TryGetValue(state.Runtime,out var ink))
            {
                foreach(var shield in ink.Shields) if(shield.RewardLife==state.Life) { shield.Reward=0; shield.RewardUntil=0; InkRefreshShield(state.Runtime,shield,Time.time); }
                InkClearNativeDarkLedger(state.Runtime,ink,state.Life,Time.time);
            }
            PublishBossVisual(state.Runtime,state.Visual,9,state.Runtime.Hero.position,state.Runtime.Hero.position,0,Time.time,Time.time,true);
            PublishBossVisual(state.Runtime,state.DarkVisual,9,state.Runtime.Hero.position,state.Runtime.Hero.position,0,Time.time,Time.time,true);
            foreach(long visual in state.DwellVisuals) PublishBossVisual(state.Runtime,visual,9,state.Runtime.Hero.position,state.Runtime.Hero.position,0,Time.time,Time.time,true);
            foreach(long visual in state.PhantomVisuals) PublishBossVisual(state.Runtime,visual,8,state.Runtime.Hero.position,state.Runtime.Hero.position,0,Time.time,Time.time,true);
            BossCancelNativeReservations(state.Runtime,BossProfiles.DarkMoonSetId,state.Life);
            state.Host=null; state.Runtime=null; state.Parent=null; state.Beam=null; state.Skill=null; state.ParentActor=null; state.Room=null; state.Run=null;
            state.ParentCreation=state.BeamCreation=state.SkillCreation=state.H=state.WhiteSpent=state.DarkSpent=state.NextVisual=0;
            state.Life=state.LastAddedPacket=state.Visual=state.DarkVisual=state.ParentLife=state.BeamLife=state.SkillLife=state.ParentActorLife=0;
            state.Magic=state.WhiteDisabled=state.DarkDisabled=state.ProcessorAttached=state.DamageAttached=state.HealAttached=false;
            state.WhiteStage=state.DarkStage=state.Adds=state.Spears=state.Illuminations=state.WhiteTargets=0;
            state.White=state.Flat=state.Spear=state.Illumination=null; state.SpearAction=null;
            Array.Clear(state.WhiteHeroes,0,state.WhiteHeroes.Length); Array.Clear(state.WhiteHeroLives,0,state.WhiteHeroLives.Length);
            Array.Clear(state.Dwell,0,state.Dwell.Length); Array.Clear(state.DwellVisuals,0,state.DwellVisuals.Length); state.PhantomVisuals.Clear();
            _inkBeamPool.Return(state);
        }
        private void ClearInkBeams(HeroRuntime rt)
        {
            _inkBeamScratch.Clear(); foreach(var pair in _inkBeams) if(pair.Value.Runtime==rt) _inkBeamScratch.Add(pair.Key);
            foreach(var parent in _inkBeamScratch) EndInkBeam(parent);
        }
    }
}
