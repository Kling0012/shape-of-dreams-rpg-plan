using System;
using System.Collections;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // Recovery uses native reliable RPCs / SyncObject deltas, so older peers need no new reader.
    internal static class NativeSkillLockRecovery
    {
        internal static bool Enabled = true;

        private static readonly AccessTools.FieldRef<AbilityTrigger, SyncList<bool>> CastLocks =
            Field<AbilityTrigger, SyncList<bool>>("_configCastLocks");
        private static readonly AccessTools.FieldRef<AbilityTrigger, SyncList<bool>> CooldownLocks =
            Field<AbilityTrigger, SyncList<bool>>("_configCooldownLocks");
        private static readonly AccessTools.FieldRef<AbilityTrigger, bool> ConfigDirty =
            Field<AbilityTrigger, bool>("_isConfigDirty");
        private static readonly AccessTools.FieldRef<EntityAbility, SyncDictionary<int, SyncedNetworkBehaviour>> Abilities =
            Field<EntityAbility, SyncDictionary<int, SyncedNetworkBehaviour>>("_synedAbilities");
        private static readonly Action<SyncList<bool>, SyncList<bool>.Operation, int, bool, bool, bool> QueueLock =
            Method<Action<SyncList<bool>, SyncList<bool>.Operation, int, bool, bool, bool>>(
                typeof(SyncList<bool>), "AddOperation", typeof(SyncList<bool>.Operation), typeof(int), typeof(bool), typeof(bool), typeof(bool));
        private static readonly Action<AbilityTrigger, float[], int[], float[]> ArrayRpc =
            Method<Action<AbilityTrigger, float[], int[], float[]>>(typeof(AbilityTrigger),
                "RpcSyncAfterFrameDelayStates_Imp", typeof(float[]), typeof(int[]), typeof(float[]));
        private static readonly AccessTools.FieldRef<UI_InGame_SkillButton, SkillTrigger> UiTarget =
            Field<UI_InGame_SkillButton, SkillTrigger>("_target");
        private static readonly FieldInfo Instances = AccessTools.DeclaredField(typeof(AbilityTrigger), "_currentInstances");
        private static bool _locksFailed, _abilitiesFailed, _arraysFailed, _configFailed, _uiFailed, _reservationFailed,
            _diagnosticFailed, _snapshotFailed;

        internal static bool HasStaleUiTarget(SkillTrigger skill, int slot)
        {
            if (_uiFailed) return false;
            try
            {
                if (UiTarget == null) { Fail(ref _uiFailed, "UI target probe"); return false; }
                if (ManagerBase<EditSkillManager>.instance?.currentProvider != null) return false;
                var buttons = ManagerBase<UI_InGame_SkillButtons>.instance?.skillButtons;
                if (buttons == null) return false;
                for (int i = 0; i < buttons.Length; i++)
                {
                    var button = buttons[i];
                    if (button != null && (int)button.skillType == slot) return UiTarget(button) != skill;
                }
            }
            catch (Exception ex) { Fail(ref _uiFailed, "UI target probe", ex); }
            return false;
        }

        internal static bool IsUnexpectedlyUnreservable(SkillTrigger skill)
        {
            if (_reservationFailed) return false;
            try
            {
                int index = skill.currentConfigIndex;
                if (skill.owner == null || !TryLocks(skill, index, out _, out _)) return false;
                return !skill.CanBeReserved();
            }
            catch (Exception ex) { Fail(ref _reservationFailed, "reservation probe", ex); return false; }
        }

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name)
        {
            try
            {
                var field = AccessTools.DeclaredField(typeof(T), name);
                if (field != null && field.FieldType == typeof(F)) return AccessTools.FieldRefAccess<T, F>(field);
            }
            catch (Exception) { }
            return null;
        }

        private static T Method<T>(Type type, string name, params Type[] args) where T : Delegate
        {
            try
            {
                var method = AccessTools.DeclaredMethod(type, name, args);
                if (method != null && method.ReturnType == typeof(void))
                    return AccessTools.MethodDelegate<T>(method, null, virtualCall: false, delegateArgs: null);
            }
            catch (Exception) { }
            return null;
        }

        private static void Fail(ref bool failed, string feature, Exception ex = null)
        {
            if (failed) return;
            failed = true;
            Log.Warn("Skill lock recovery disabled for " + feature + ": " + (ex?.Message ?? "native shape unavailable"));
        }

        internal static bool TryLocks(AbilityTrigger skill, int index, out bool cast, out bool cooldown)
        {
            cast = cooldown = false;
            if (_locksFailed) return false;
            try
            {
                if (CastLocks == null || CooldownLocks == null || QueueLock == null)
                {
                    Fail(ref _locksFailed, "config locks");
                    return false;
                }
                var casts = CastLocks(skill);
                var cooldowns = CooldownLocks(skill);
                if (index < 0 || index >= casts.Count || index >= cooldowns.Count) return false;
                cast = casts[index];
                cooldown = cooldowns[index];
                return true;
            }
            catch (Exception ex) { Fail(ref _locksFailed, "config locks", ex); return false; }
        }

        internal static void ResendHero(Hero hero)
        {
            // SetDirty affects SyncVars, not SyncObjects. Keep authoritative locks/channels/effects intact.
            hero.SetDirty();
            hero.Ability?.SetDirty();
            hero.Control?.SetDirty();
            hero.Status?.SetDirty();
            if (_abilitiesFailed || hero.Ability == null) return;
            try
            {
                if (Abilities == null) { Fail(ref _abilitiesFailed, "ability references"); return; }
                var abilities = Abilities(hero.Ability);
                for (int i = 0; i < 6; i++)
                    if (abilities.TryGetValue(i, out var value)) abilities[i] = value;
                // Native SyncDictionary queues OP_SET even for equal values. This also refreshes UI targets.
            }
            catch (Exception ex) { Fail(ref _abilitiesFailed, "ability references", ex); }
        }

        // Returns true when the full-array RPC went out. The caller then skips the
        // per-index resends of the same sanitized values (NativeCooldownSafety.ResendAuthoritative).
        internal static bool ResendSkill(AbilityTrigger skill)
        {
            skill.SetDirty();
            bool arraysSent = ResendArrays(skill);
            if (!_configFailed)
            {
                try
                {
                    if (ConfigDirty == null) Fail(ref _configFailed, "config RPC");
                    else ConfigDirty(skill) = true; // Native LogicUpdate publishes its ConfigSyncData.
                }
                catch (Exception ex) { Fail(ref _configFailed, "config RPC", ex); }
            }
            if (_locksFailed) return arraysSent;
            try
            {
                if (CastLocks == null || CooldownLocks == null || QueueLock == null)
                { Fail(ref _locksFailed, "config locks"); return arraysSent; }
                ResendLocks(CastLocks(skill));
                ResendLocks(CooldownLocks(skill));
            }
            catch (Exception ex) { Fail(ref _locksFailed, "config locks", ex); }
            return arraysSent;
        }

        private static void ResendLocks(SyncList<bool> list)
        {
            // Append same-value SETs after pending operations. Never toggle locks or reset changesAhead.
            // Native Mirror preserves the delta ordering, including for clients receiving initial state.
            for (int i = 0; i < list.Count; i++)
                QueueLock(list, SyncList<bool>.Operation.OP_SET, i, list[i], list[i], true);
        }

        private static bool ResendArrays(AbilityTrigger skill)
        {
            if (_arraysFailed) return false;
            try
            {
                if (ArrayRpc == null) { Fail(ref _arraysFailed, "array RPC"); return false; }
                int count = skill.configs?.Length ?? 0;
                var times = skill.currentUnscaledCooldownTimes;
                var charges = skill.currentCharges;
                var delays = skill.currentMinimumDelays;
                if (count == 0 || times == null || charges == null || delays == null
                    || times.Length != count || charges.Length != count || delays.Length != count) return false;
                // Rare recovery only; the native full-array RPC also repairs a guest's array lengths.
                var safeTimes = new float[count];
                var safeCharges = new int[count];
                var safeDelays = new float[count];
                for (int i = 0; i < count; i++)
                {
                    safeTimes[i] = SafeTime(times[i]);
                    safeCharges[i] = Math.Max(charges[i], 0);
                    safeDelays[i] = SafeTime(delays[i]);
                }
                ArrayRpc(skill, safeTimes, safeCharges, safeDelays);
                return true;
            }
            catch (Exception ex) { Fail(ref _arraysFailed, "array RPC", ex); return false; }
        }

        private static float SafeTime(float value) => value >= 0f && !float.IsInfinity(value) ? value : 0f;

        private static void DescribeInstances(StringBuilder text, AbilityTrigger skill)
        {
            if (!NetworkServer.active || _diagnosticFailed) return;
            try
            {
                if (Instances == null || !(Instances.GetValue(skill) is IList instances))
                { Fail(ref _diagnosticFailed, "instance diagnostics"); return; }
                text.Append(" instances=").Append(instances.Count).Append('[');
                for (int i = 0; i < instances.Count && i < 8; i++)
                {
                    object entry = instances[i];
                    var type = entry.GetType();
                    var actor = AccessTools.DeclaredField(type, "instance")?.GetValue(entry) as AbilityInstance;
                    text.Append(actor == null ? "null" : actor.GetType().Name).Append(':')
                        .Append(actor?.netId).Append(':').Append(actor?.isActive).Append(':')
                        .Append(AccessTools.DeclaredField(type, "configIndex")?.GetValue(entry)).Append(':')
                        .Append(AccessTools.DeclaredField(type, "shouldLockCast")?.GetValue(entry)).Append(':')
                        .Append(AccessTools.DeclaredField(type, "shouldLockCooldown")?.GetValue(entry)).Append(';');
                }
                text.Append(']');
            }
            catch (Exception ex) { Fail(ref _diagnosticFailed, "instance diagnostics", ex); }
        }

        // Allocations are confined to rate-limited requests/replies, never the scan path.
        internal static DreamforgeSkillState Capture(Hero hero, SkillTrigger skill)
        {
            var state = new DreamforgeSkillState
            {
                mana = hero.currentMana,
                status = Convert.ToInt32(hero.Status?.basicEffectMask ?? 0),
                blockAbility = Convert.ToInt32(hero.Control?.Network_blockAbility),
                blockAbilityCancelable = Convert.ToInt32(hero.Control?.Network_blockAbilityCancelable),
                blockDodge = Convert.ToInt32(hero.Control?.Network_blockDodge),
                blockDodgeCancelable = Convert.ToInt32(hero.Control?.Network_blockDodgeCancelable),
                config = -1,
            };
            if (skill == null) return state;
            state.skillNetId = skill.netId;
            state.ownerNetId = skill.owner == null ? 0 : skill.owner.netId;
            state.active = skill.isActive;
            state.config = skill.currentConfigIndex;
            state.abilityIndex = skill.abilityIndex;
            state.casting = skill.Network_isCasting;
            state.cooldowns = skill.currentUnscaledCooldownTimes == null ? null : (float[])skill.currentUnscaledCooldownTimes.Clone();
            state.charges = skill.currentCharges == null ? null : (int[])skill.currentCharges.Clone();
            state.delays = skill.currentMinimumDelays == null ? null : (float[])skill.currentMinimumDelays.Clone();
            int index = state.config;
            if (skill.configs == null || index < 0 || index >= skill.configs.Length || skill.configs[index] == null) return state;
            var config = skill.configs[index];
            state.configActive = config.isActive;
            state.maxCharges = config.maxCharges;
            state.manaCost = config.manaCost;
            state.locksKnown = TryLocks(skill, index, out state.castLock, out state.cooldownLock);
            if (hero.Ability != null && state.abilityIndex >= 0 && state.abilityIndex < 6)
                state.abilityLock = hero.Ability.IsAbilityCastLocked(state.abilityIndex);
            state.validator = config.selfValidator.Evaluate(hero);
            if (state.locksKnown && skill.owner != null && state.charges != null && index < state.charges.Length
                && state.delays != null && index < state.delays.Length)
                state.reservable = skill.CanBeReserved();
            return state;
        }

        internal static bool Different(DreamforgeSkillState a, DreamforgeSkillState b)
        {
            if (a == null || b == null) return true;
            if (a.skillNetId != b.skillNetId || a.ownerNetId != b.ownerNetId || a.config != b.config
                || a.abilityIndex != b.abilityIndex || a.active != b.active || a.configActive != b.configActive
                || a.maxCharges != b.maxCharges || a.casting != b.casting || a.locksKnown != b.locksKnown
                || a.castLock != b.castLock || a.cooldownLock != b.cooldownLock || a.abilityLock != b.abilityLock
                || a.validator != b.validator || a.reservable != b.reservable || a.mana != b.mana
                || a.manaCost != b.manaCost || a.status != b.status || a.blockAbility != b.blockAbility
                || a.blockAbilityCancelable != b.blockAbilityCancelable || a.blockDodge != b.blockDodge
                || a.blockDodgeCancelable != b.blockDodgeCancelable) return true;
            return !Same(a.charges, b.charges) || !Same(a.cooldowns, b.cooldowns) || !Same(a.delays, b.delays);
        }

        private static bool Same<T>(T[] a, T[] b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!Equals(a[i], b[i])) return false;
            return true;
        }

        internal static string Diagnostic(Hero hero, SkillTrigger skill, int slot)
        {
            try { return Describe(hero, skill, slot); }
            catch (Exception ex) { Fail(ref _snapshotFailed, "state diagnostics", ex); return "unavailable"; }
        }

        internal static void LogSnapshot(Hero hero, SkillTrigger skill, int slot, string prefix)
        {
            if (_snapshotFailed) return;
            try { Log.Info(prefix + Describe(hero, skill, slot)); }
            catch (Exception ex) { Fail(ref _snapshotFailed, "state diagnostics", ex); }
        }

        // Called only once per diagnostic episode, never from the normal frame path.
        private static string Describe(Hero hero, SkillTrigger skill, int slot)
        {
            var text = new StringBuilder(512);
            text.Append("slot=").Append(slot).Append(" hero=").Append(hero.netId)
                .Append(" heroOwned=").Append(hero.isOwned)
                .Append(" mana=").Append(hero.currentMana)
                .Append(" abilityMask=").Append(hero.Ability?.Network_abilityCastLockBitmap)
                .Append(" status=").Append(hero.Status?.basicEffectMask)
                .Append(" channels=").Append(hero.Control?.ongoingChannels.Count)
                .Append(" blockAbility=").Append(hero.Control?.Network_blockAbility)
                .Append('/').Append(hero.Control?.Network_blockAbilityCancelable)
                .Append(" blockDodge=").Append(hero.Control?.Network_blockDodge)
                .Append('/').Append(hero.Control?.Network_blockDodgeCancelable)
                .Append(" airborne=").Append(hero.Control?.isAirborne)
                .Append(" dashing=").Append(hero.Control?.isDashing)
                .Append(" dodging=").Append(hero.Control?.isDodging);
            var controls = ManagerBase<ControlManager>.instance;
            if (!NetworkServer.active && controls != null)
                text.Append(" controllingHero=").Append(controls.controllingEntity == hero)
                    .Append(" inputEnabled=").Append(controls.isCharacterControlEnabled)
                    .Append(" mainDisabled=").Append(controls.isMainSkillDisabled)
                    .Append(" dodgeDisabled=").Append(controls.isDodgeDisabled);
            if (skill == null) return text.Append(" skill=null").ToString();
            text.Append(" skill=").Append(skill.netId).Append('/').Append(skill.GetType().Name)
                .Append(" active=").Append(skill.isActive).Append(" owner=").Append(skill.owner?.netId)
                .Append(" abilityIndex=").Append(skill.abilityIndex)
                .Append(" config=").Append(skill.currentConfigIndex)
                .Append(" casting=").Append(skill.Network_isCasting)
                .Append(" lengths=").Append(skill.configs?.Length).Append('/')
                .Append(skill.currentUnscaledCooldownTimes?.Length).Append('/')
                .Append(skill.currentCharges?.Length).Append('/').Append(skill.currentMinimumDelays?.Length);
            int index = skill.currentConfigIndex;
            if (index < 0 || skill.configs == null || index >= skill.configs.Length
                || skill.currentUnscaledCooldownTimes == null || index >= skill.currentUnscaledCooldownTimes.Length
                || skill.currentCharges == null || index >= skill.currentCharges.Length
                || skill.currentMinimumDelays == null || index >= skill.currentMinimumDelays.Length) return text.ToString();
            var config = skill.configs[index];
            if (config == null) return text.Append(" config=null").ToString();
            bool locksKnown = TryLocks(skill, index, out bool castLock, out bool cooldownLock);
            text.Append(" configActive=").Append(config.isActive).Append(" charges=").Append(skill.currentCharges[index])
                .Append('/').Append(config.maxCharges).Append(" cooldown=").Append(skill.currentUnscaledCooldownTimes[index])
                .Append(" delay=").Append(skill.currentMinimumDelays[index]).Append(" cost=").Append(config.manaCost)
                .Append(" locksKnown=").Append(locksKnown).Append(" castLock=").Append(castLock)
                .Append(" cooldownLock=").Append(cooldownLock)
                .Append(" uiStale=").Append(!NetworkServer.active && HasStaleUiTarget(skill, slot));
            DescribeInstances(text, skill);
            if (skill.owner != null && locksKnown)
                text.Append(" reserved=").Append(skill.CanBeReserved()).Append(" cast=").Append(skill.CanBeCast())
                    .Append(" validator=").Append(config.selfValidator.Evaluate(skill.owner))
                    .Append(" multiplier=").Append(skill.GetCooldownTimeMultiplier(index));
            return text.ToString();
        }
    }
}
