using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    public partial class DreamforgeMod
    {
        partial void StartNativeKillRewards()
        {
            try { NativeKillRewards.Install(harmony); }
            catch (Exception ex) { NativeKillRewards.Fail(ex); }
        }

        partial void StopNativeKillRewards() => NativeKillRewards.Stop();
    }

    // Optional, effect-only hooks: never replay Actor.OnKill, Hero's RPC, or statistics.
    internal static partial class NativeKillRewards
    {
        internal sealed class Effect
        {
            internal Type Type;
            internal MethodInfo Callback;
            internal FieldInfo Tracker;
            internal int Special;
        }

        private static readonly List<Effect> Effects = new List<Effect>();
        private static FieldInfo _handlers, _callback, _stopped;
        private static bool _enabled, _reported;

        internal static bool Active => _enabled && NetworkServer.active && DewPlayer.gamePlayers.Count > 1;

        internal static void Install(Harmony harmony)
        {
            _enabled = false;
            _reported = false;
            Effects.Clear();
            var patches = new List<KeyValuePair<MethodInfo, MethodInfo>>();
            try
            {
                _handlers = RequireField(typeof(SafeAction<EventInfoKill>), "_handlers", typeof(List<Action<EventInfoKill>>));
                _callback = RequireField(typeof(KillTracker), "_callback", typeof(Action<EventInfoKill>));
                _stopped = RequireField(typeof(KillTracker), "_isStopped", typeof(bool));
                AddTracker(typeof(Gem_E_Metal), "KillCallback");
                AddTracker(typeof(Gem_E_Predation), "OnKill");
                AddTracker(typeof(Gem_L_Culinary), "OnKill");
                AddTracker(typeof(Gem_L_DivineFaith), "IncreaseAmp");
                AddTracker(typeof(Gem_R_Wealth), "Callback");
                AddEvent(typeof(Gem_E_OurStory_Unfinished));
                AddEvent(typeof(Gem_U_GuidingCompass_NotCharged));
                AddEvent(typeof(Se_E_LizardlyBlessing));
                AddEvent(typeof(Se_E_UmbralEdge));
                AddEvent(typeof(Se_D_TheKillingFlow));
                AddEvent(typeof(Se_Star_I_DreamDustOnKillChance));
                AddEvent(typeof(Se_Star_I_FirstBossGemDrop), "OnKillOrAssist");
                AddEvent(typeof(Se_Star_I_GemOnMiniBossKill));
                AddEvent(typeof(Se_Star_L_HealOnKill));
                AddEvent(typeof(Se_Star_Bismuth_I_KnowledgeBook));
                AddEvent(typeof(Se_Star_Cetus_F_FR_ChangeToUltHitReset));
                AddEvent(typeof(Se_Star_Cetus_I_GetDreamDustOnBossKill));
                AddEvent(typeof(Se_Star_Husk_F_AS_NoShockwaveFasterAttack));
                AddEvent(typeof(Se_Star_Husk_I_BossKillPlatinumCoin));
                AddEvent(typeof(Se_Star_Husk_L_DodgeCooldownOnKillOrAttack));
                AddEvent(typeof(Se_Star_Husk_L_DodgeCooldownOnKillWithEmptyCharges));
                AddEvent(typeof(Se_Star_Lacerta_I_BossBounty));
                AddEvent(typeof(Se_Star_Nachia_I_ForestKillDreamDust));
                AddEvent(typeof(Se_Star_Nachia_I_SummonKillGold));
                AddEvent(typeof(Se_Star_Yubar_I_StarCookie));
                AddEvent(typeof(Star_Global_KillDreamDustBonus));
                AddEvent(typeof(SkillTrigger), "ActorEventOnKill");
                Effects.Add(new Effect { Type = typeof(Se_Star_Aurena_F_CR_NoHealCooldownReduction), Special = 1 });
                Effects.Add(new Effect { Type = typeof(Se_Star_Lacerta_F_PS_CooldownReset), Special = 2 });
                Effects.Add(new Effect { Type = typeof(Se_Star_Cetus_F_BA_CdReductionOnKill), Special = 3 });

                Patch(RequireCallback(typeof(SafeAction<EventInfoKill>), "Invoke"), nameof(BeforeDeath));
                Patch(RequireCallback(typeof(KillTracker), "ReportKill"), nameof(BeforeTrackedKill));
                Patch(AccessTools.DeclaredMethod(typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors))
                    ?? throw new MissingMethodException(typeof(Actor).FullName, nameof(Actor.ClearPooledEventsAndProcessors)), nameof(BeforePoolClear));
                Patch(AccessTools.DeclaredMethod(typeof(Actor), "OnDisable")
                    ?? throw new MissingMethodException(typeof(Actor).FullName, "OnDisable"), nameof(BeforePoolClear));
                foreach (var effect in Effects)
                    if (effect.Callback != null && effect.Tracker == null)
                        Patch(effect.Callback, nameof(BeforeEventReward));
                Patch(RequireCallback(typeof(Se_Star_Cetus_F_BA_CdReductionOnKill), "OnVictimDeath"), nameof(BeforeCetusReward));
                InstallGold(harmony, patches);
                _enabled = true;
            }
            catch (Exception ex)
            {
                // A missing optional API disables only this extension. Originals keep running.
                for (int i = patches.Count - 1; i >= 0; i--)
                    try { harmony.Unpatch(patches[i].Key, patches[i].Value); }
                    catch (Exception cleanup) { Log.Warn("Native kill reward cleanup: " + cleanup.Message); }
                Fail(ex);
            }

            void Patch(MethodInfo target, string name)
            {
                var prefix = AccessTools.DeclaredMethod(typeof(NativeKillRewards), name);
                patches.Add(new KeyValuePair<MethodInfo, MethodInfo>(target, prefix));
                harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
            }
        }

        private static void AddTracker(Type type, string method)
        {
            Effects.Add(new Effect { Type = type, Callback = RequireCallback(type, method),
                Tracker = RequireField(type, "_tracker", typeof(KillTracker)) });
        }

        private static void AddEvent(Type type, string method = "ClientHeroEventOnKillOrAssist")
        {
            Effects.Add(new Effect { Type = type, Callback = RequireCallback(type, method) });
        }

        private static FieldInfo RequireField(Type type, string name, Type fieldType)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireCallback(Type type, string name)
        {
            var method = AccessTools.DeclaredMethod(type, name, new[] { typeof(EventInfoKill) });
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        internal static void Stop() { _enabled = false; ClearGold(); }

        internal static void Fail(Exception ex)
        {
            _enabled = false;
            if (_reported) return;
            _reported = true;
            Log.Warn("Native kill reward sharing disabled; native rewards remain active: " + ex.Message);
        }

        internal static Effect Find(MethodInfo callback)
        {
            for (int i = 0; i < Effects.Count; i++)
                if (Effects[i].Callback == callback) return Effects[i];
            // These trackers use compiler-generated closures, not an Actor as their target.
            for (var type = callback.DeclaringType; type != null; type = type.DeclaringType)
                for (int i = 0; i < Effects.Count; i++)
                    if (Effects[i].Special != 0 && Effects[i].Type == type) return Effects[i];
            return null;
        }

        internal static List<Action<EventInfoKill>> Handlers(SafeAction<EventInfoKill> action)
            => action == null ? null : (List<Action<EventInfoKill>>)_handlers.GetValue(action);

        internal static Action<EventInfoKill> TrackedCallback(Effect effect, Actor actor)
        {
            var tracker = (KillTracker)effect.Tracker.GetValue(actor);
            return tracker == null || (bool)_stopped.GetValue(tracker) ? null
                : (Action<EventInfoKill>)_callback.GetValue(tracker);
        }

        internal static List<Effect> KnownEffects => Effects;

        private static void BeforePoolClear(Actor __instance)
        {
            if (__instance is Entity entity) HostAuthority.NativeInstance?.ForgetNativeKillRewards(entity);
        }

        private static void BeforeDeath(SafeAction<EventInfoKill> __instance, EventInfoKill __0)
        {
            if (!Active || __0.victim == null || !ReferenceEquals(__instance, __0.victim.EntityEvent_OnDeath)) return;
            HostAuthority.NativeInstance?.PrepareNativeKillRewards(__0);
        }

        private static bool BeforeTrackedKill(EventInfoKill obj, Actor ____target, bool ____isStopped,
            Dictionary<Entity, float> ____trackingDict, float ____gracePeriod, Action<EventInfoKill> ____callback)
        {
            if (!Active || ____isStopped || ____callback == null || obj.victim == null
                || !____trackingDict.TryGetValue(obj.victim, out float at) || Time.time - at > ____gracePeriod) return true;
            try
            {
                var effect = Find(____callback.Method);
                if (effect == null) return true;
                var hero = Owner(____target);
                object receiver = ____callback.Target;
                if (effect.Special != 0)
                    receiver = hero?.Status.GetStatusEffect(effect.Type);
                bool run = HostAuthority.NativeInstance?.BeforeNativeKillReward(obj, hero, receiver, effect, ____callback) ?? true;
                if (!run) ____trackingDict.Remove(obj.victim);
                return run;
            }
            catch (Exception ex) { Fail(ex); return true; }
        }

        private static bool BeforeEventReward(object __instance, MethodBase __originalMethod, EventInfoKill __0)
        {
            if (!Active) return true;
            try
            {
                var effect = Find((MethodInfo)__originalMethod);
                return HostAuthority.NativeInstance?.BeforeNativeKillReward(__0, Recipient(__instance), __instance, effect, null) ?? true;
            }
            catch (Exception ex) { Fail(ex); return true; }
        }

        private static bool BeforeCetusReward(Se_Star_Cetus_F_BA_CdReductionOnKill __instance, EventInfoKill obj,
            Dictionary<Entity, float> ____hitTimes, SkillTrigger ____skill)
        {
            if (!Active || obj.victim == null || !____hitTimes.TryGetValue(obj.victim, out float at)
                || Time.time - at > __instance.trackingGracePeriod || ____skill.IsNullOrInactive()) return true;
            try
            {
                var effect = Effects[Effects.Count - 1];
                bool run = HostAuthority.NativeInstance?.BeforeNativeKillReward(obj, __instance.hero, __instance, effect, null) ?? true;
                if (!run) ____hitTimes.Remove(obj.victim);
                return run;
            }
            catch (Exception ex) { Fail(ex); return true; }
        }

        internal static Hero Owner(Actor actor)
        {
            for (int depth = 0; actor != null && depth < 128; depth++, actor = actor.parentActor)
                if (actor is Hero hero) return hero;
            return null;
        }

        private static Hero Recipient(object receiver)
        {
            if (receiver is Gem gem) return gem.owner;
            if (receiver is SkillTrigger skill) return skill.owner as Hero;
            if (receiver is Se_E_UmbralEdge edge) return edge.info.caster as Hero;
            if (receiver is StatusEffect status) return status.victim as Hero;
            if (receiver is DewStarItemOld oldStar) return oldStar.hero;
            return null;
        }
    }

    internal sealed partial class HostAuthority
    {
        private sealed class NativeKillReward
        {
            internal Hero Hero;
            internal object Receiver;
            internal NativeKillRewards.Effect Effect;
            internal Action<EventInfoKill> Callback;
            internal Actor Source;
            internal uint ReceiverLifetime, SourceLifetime;
            internal bool Applied;
        }

        private sealed class NativeKillDeath
        {
            internal Entity Victim;
            internal uint Lifetime;
            internal Hero Killer;
            internal bool Dispatched;
            internal readonly List<NativeKillReward> Rewards = new List<NativeKillReward>();
        }

        private readonly Dictionary<Entity, NativeKillDeath> _nativeKillDeaths = new Dictionary<Entity, NativeKillDeath>();
        private readonly Stack<NativeKillDeath> _nativeKillDeathPool = new Stack<NativeKillDeath>();
        private readonly Stack<NativeKillReward> _nativeKillReceiverPool = new Stack<NativeKillReward>();
        private NativeKillReward _invokingNativeKillReward;

        internal void PrepareNativeKillRewards(EventInfoKill info)
        {
            try { CaptureKillAssists(info); }
            catch (Exception ex) { NativeKillRewards.Fail(ex); }
        }

        partial void CaptureKillAssistActorLifetime(Actor actor, ref uint lifetime)
        {
            if (actor != null) lifetime = actor.persistentNetId;
        }

        partial void CaptureNativeKillRewards(EventInfoKill info, KillAssistVictim state)
        {
            if (!NativeKillRewards.Active || state.Failed || state.Killer == null) return;
            try
            {
                if (_nativeKillDeaths.ContainsKey(info.victim)) return;
                var death = _nativeKillDeathPool.Count > 0 ? _nativeKillDeathPool.Pop() : new NativeKillDeath();
                death.Victim = info.victim; death.Lifetime = info.victim.persistentNetId;
                death.Killer = state.Killer; death.Dispatched = false;
                _nativeKillDeaths.Add(info.victim, death);
                var rewards = death.Rewards;
                // Freeze equipment as well as eligibility before any native OnDeath listener runs.
                CaptureNativeKillRecipient(rewards, state.Killer, info.actor);
                for (int i = 0; i < state.Contacts.Length; i++)
                {
                    var contact = state.Contacts[i];
                    if (!contact.Eligible) continue;
                    Actor source = contact.HasHit && RecentKillAssist(state.DiedAt, contact.HitAt)
                        && contact.HitActor != null && contact.HitActor.persistentNetId == contact.HitActorLifetime
                        && KillAssistOwner(contact.HitActor) == contact.Hero
                        ? contact.HitActor : contact.Hero;
                    CaptureNativeKillRecipient(rewards, contact.Hero, source);
                }
            }
            catch (Exception ex) { NativeKillRewards.Fail(ex); }
        }

        private void CaptureNativeKillRecipient(List<NativeKillReward> rewards, Hero hero, Actor source)
        {
            var handlers = NativeKillRewards.Handlers(hero.ClientHeroEvent_OnKillOrAssist);
            if (handlers != null)
                for (int i = 0; i < handlers.Count; i++)
                {
                    var handler = handlers[i];
                    if (handler == null) continue;
                    var effect = NativeKillRewards.Find(handler.Method);
                    if (effect != null) AddNativeKillReward(rewards, hero, handler.Target, effect, handler, source);
                }
            var killHandlers = NativeKillRewards.Handlers(hero.ActorEvent_OnKill);
            if (killHandlers != null)
                for (int i = 0; i < killHandlers.Count; i++)
                {
                    var handler = killHandlers[i];
                    if (handler == null || !(handler.Target is SkillTrigger skill) || skill.type != SkillType.Ultimate) continue;
                    var effect = NativeKillRewards.Find(handler.Method);
                    if (effect != null) AddNativeKillReward(rewards, hero, skill, effect, handler, source);
                }
            if (hero.Skill != null)
                foreach (var pair in hero.Skill.gems)
                {
                    var gem = pair.Value;
                    if (gem == null || !gem.isActive || gem.owner != hero) continue;
                    var effects = NativeKillRewards.KnownEffects;
                    for (int i = 0; i < effects.Count; i++)
                    {
                        var effect = effects[i];
                        if (effect.Tracker == null || !effect.Type.IsInstanceOfType(gem)) continue;
                        var callback = NativeKillRewards.TrackedCallback(effect, gem);
                        if (callback != null) AddNativeKillReward(rewards, hero, gem, effect, callback, source);
                    }
                }
            if (hero.Status == null) return;
            var statuses = hero.Status.statusEffects;
            for (int i = 0; i < statuses.Count; i++)
            {
                var status = statuses[i];
                if (status == null || !status.isActive) continue;
                var effects = NativeKillRewards.KnownEffects;
                for (int j = 0; j < effects.Count; j++)
                    if (effects[j].Special != 0 && effects[j].Type.IsInstanceOfType(status))
                        AddNativeKillReward(rewards, hero, status, effects[j], null, source);
            }
        }

        private void AddNativeKillReward(List<NativeKillReward> rewards, Hero hero, object receiver,
            NativeKillRewards.Effect effect, Action<EventInfoKill> callback, Actor source)
        {
            for (int i = 0; i < rewards.Count; i++)
                if (ReferenceEquals(rewards[i].Receiver, receiver) && rewards[i].Effect == effect) return;
            var reward = _nativeKillReceiverPool.Count > 0 ? _nativeKillReceiverPool.Pop() : new NativeKillReward();
            reward.Hero = hero; reward.Receiver = receiver; reward.Effect = effect;
            reward.Callback = callback; reward.Source = source; reward.Applied = false;
            reward.ReceiverLifetime = receiver is Actor actor ? actor.persistentNetId : 0;
            reward.SourceLifetime = source != null ? source.persistentNetId : 0;
            rewards.Add(reward);
        }

        internal bool BeforeNativeKillReward(EventInfoKill info, Hero hero, object receiver,
            NativeKillRewards.Effect effect, Action<EventInfoKill> callback)
        {
            if (hero == null || receiver == null || effect == null || info.victim == null) return true;
            if (_invokingNativeKillReward != null && ReferenceEquals(_invokingNativeKillReward.Receiver, receiver)
                && _invokingNativeKillReward.Effect == effect && _invokingNativeKillReward.Hero == hero
                && info.victim == _nativeKillRewardVictim) return true;
            PrepareNativeKillRewards(info);
            if (!NativeKillRewards.Active || !_nativeKillDeaths.TryGetValue(info.victim, out var death)
                || death.Lifetime != info.victim.persistentNetId) return true;
            var rewards = death.Rewards;
            for (int i = 0; i < rewards.Count; i++)
            {
                var reward = rewards[i];
                if (reward.Hero != hero || !ReferenceEquals(reward.Receiver, receiver) || reward.Effect != effect) continue;
                if (reward.Applied) return false;
                if (hero != death.Killer)
                {
                    // Defer native assists too: the original packet still names the killer.
                    // Dispatch below must supply this recipient's own summon/skill source.
                    if (effect.Special != 0 && callback != null) reward.Callback = callback;
                    return false;
                }
                reward.Applied = true;
                return true;
            }
            // Keep the killer's original rewards even when equipment changed in another listener.
            // Other native 30-second assists must not create rewards outside the frozen 5-second set.
            return hero == death.Killer;
        }

        private Entity _nativeKillRewardVictim;

        partial void DispatchNativeKillRewards(EventInfoKill info, KillAssistVictim state)
        {
            if (!NativeKillRewards.Active || !_nativeKillDeaths.TryGetValue(info.victim, out var death)
                || death.Dispatched || death.Lifetime != info.victim.persistentNetId) return;
            death.Dispatched = true;
            var rewards = death.Rewards;
            try
            {
                for (int i = 0; i < rewards.Count; i++)
                {
                    var reward = rewards[i];
                    if (reward.Applied || reward.Hero == state.Killer || !Alive(reward.Hero)) continue;
                    if (reward.Receiver is Actor actor && (actor == null || !actor.isActive
                        || actor.persistentNetId != reward.ReceiverLifetime)) continue;
                    if (reward.Receiver is Gem gem && (gem.owner != reward.Hero || !gem.isValid)) continue;
                    if (reward.Receiver is SkillTrigger skill && skill.owner != reward.Hero) continue;
                    reward.Applied = true;
                    var previous = _invokingNativeKillReward;
                    var previousVictim = _nativeKillRewardVictim;
                    _invokingNativeKillReward = reward;
                    _nativeKillRewardVictim = info.victim;
                    try
                    {
                        var ownKill = new EventInfoKill { actor = reward.Source != null
                            && reward.Source.persistentNetId == reward.SourceLifetime
                            && KillAssistOwner(reward.Source) == reward.Hero ? reward.Source : reward.Hero, victim = info.victim };
                        if (reward.Callback != null) reward.Callback(ownKill);
                        else ApplyNativeSpecialKillReward(reward, ownKill);
                    }
                    finally
                    {
                        _invokingNativeKillReward = previous;
                        _nativeKillRewardVictim = previousVictim;
                    }
                    if (!NativeKillRewards.Active) break;
                }
            }
            catch (Exception ex) { NativeKillRewards.Fail(ex); }
        }

        private static void ApplyNativeSpecialKillReward(NativeKillReward reward, EventInfoKill info)
        {
            // These three receivers hide the callback in a per-projectile closure/hit table.
            // Reuse their public native cooldown APIs and current star values without fake hits.
            if (reward.Receiver is Se_Star_Aurena_F_CR_NoHealCooldownReduction aurena)
            {
                if (info.victim is Monster && aurena.skill != null)
                    aurena.ApplyCooldownReductionByRatio(aurena.skill, aurena.cooldownReductionRatio);
            }
            else if (reward.Receiver is Se_Star_Lacerta_F_PS_CooldownReset lacerta)
            {
                if (lacerta.skill != null) lacerta.ResetCooldown(lacerta.skill);
            }
            else if (reward.Receiver is Se_Star_Cetus_F_BA_CdReductionOnKill cetus)
            {
                if (!cetus.skill.IsNullOrInactive()) cetus.ApplyCooldownReduction(cetus.skill, cetus.cdReductionAmount);
            }
        }

        internal Hero[] NativeKillGoldRecipients(EventInfoKill info, out Hero killer)
        {
            killer = null;
            PrepareNativeKillRewards(info);
            if (!_killAssistVictims.TryGetValue(info.victim, out var state) || state.Failed || state.Killer == null) return null;
            int count = 0;
            for (int i = 0; i < state.Contacts.Length; i++) if (state.Contacts[i].Eligible) count++;
            if (count == 0) return null;
            var heroes = new Hero[count];
            count = 0;
            for (int i = 0; i < state.Contacts.Length; i++)
                if (state.Contacts[i].Eligible) heroes[count++] = state.Contacts[i].Hero;
            killer = state.Killer;
            return heroes;
        }

        internal void ForgetNativeKillRewards(Entity victim)
        {
            if (ReferenceEquals(victim, null) || !_nativeKillDeaths.TryGetValue(victim, out var death)) return;
            _nativeKillDeaths.Remove(victim);
            ReleaseNativeKillDeath(death);
        }

        private void ReleaseNativeKillDeath(NativeKillDeath death)
        {
            var list = death.Rewards;
            for (int i = 0; i < list.Count; i++)
            {
                var reward = list[i];
                reward.Hero = null; reward.Receiver = null; reward.Effect = null;
                reward.Callback = null; reward.Source = null; reward.Applied = false;
                _nativeKillReceiverPool.Push(reward);
            }
            list.Clear();
            death.Victim = null; death.Killer = null; death.Dispatched = false;
            _nativeKillDeathPool.Push(death);
        }

        partial void ClearNativeKillRewards()
        {
            foreach (var pair in _nativeKillDeaths) ReleaseNativeKillDeath(pair.Value);
            _nativeKillDeaths.Clear();
            _nativeKillDeathPool.Clear();
            _nativeKillReceiverPool.Clear();
            NativeKillRewards.ClearGold();
        }
    }
}
