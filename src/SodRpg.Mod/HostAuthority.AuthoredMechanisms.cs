using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class AuthoredChannelState
        {
            internal AuthoredMechanismEntry Entry;
            internal string Key;
            internal long Generation;
            internal int Count;
            internal string Quota;
            internal readonly HashSet<long> Counted = new HashSet<long>();
        }
        private sealed class AuthoredOwnerState
        {
            internal Build Build;
            internal long EquipmentEpoch;
            internal long KeystoneEpoch;
            internal readonly Dictionary<string, AuthoredChannelState> Channels = new Dictionary<string, AuthoredChannelState>(StringComparer.Ordinal);
            internal readonly Dictionary<string, AuthoredChannelState> Gimmicks = new Dictionary<string, AuthoredChannelState>(StringComparer.Ordinal);
            internal readonly Dictionary<string, string> GimmickIds = new Dictionary<string, string>(StringComparer.Ordinal);
            internal AuthoredChannelState[] DispatchChannels = Array.Empty<AuthoredChannelState>();
        }
        internal struct AuthoredPendingGimmick
        {
            internal bool Enabled;
            internal MemoryActivationEvent Notification;
            internal long Generation, EquipmentEpoch;
            internal AuthoredMechanismSpec Spec;
            internal BridgeSuccessTransaction Transaction;
            internal BridgePayload Payload;
            internal string Source, LegacyKey;
            internal KeystoneSourceKind SourceKind;
            internal GimmickEntry Legacy;
            internal bool HadKeystone;
        }
        private sealed class AuthoredGimmickDispatch
        {
            private readonly HostAuthority _host;
            internal HeroRuntime LegacyRuntime;
            internal Hero Hero;
            internal AuthoredOwnerState Owner;
            internal AuthoredChannelState Channel;
            internal MemoryActivationEvent Notification;
            internal BridgePayload Payload;
            internal string Source, RuntimeId;
            internal KeystoneSourceKind SourceKind;
            internal float Cooldown;
            internal readonly Func<GimmickEntry, bool> Filter, Admit;
            internal readonly Func<GimmickEntry, GimmickDef> Transform;
            internal AuthoredGimmickDispatch(HostAuthority host)
            { _host = host; Filter = Matches; Admit = Spend; Transform = Effective; }
            private bool Matches(GimmickEntry entry) => LegacyRuntime != null ? !_host.IsAuthoredGimmick(Hero, entry)
                : Payload != null ? entry.StarId == RuntimeId
                : Owner.Gimmicks.TryGetValue(entry.StarId, out var candidate) && candidate == Channel;
            private bool Spend(GimmickEntry entry) => _host._memoryAttribution.TrySpend(Channel.Quota,
                Channel.Entry.Spec.Once ? AttributionBudget.PerActivation : Channel.Entry.Spec.Budget, Notification, true);
            private GimmickDef Effective(GimmickEntry entry) => LegacyRuntime != null
                ? _host.TransformLegacyGimmick(LegacyRuntime, entry.Def, Source, SourceKind, entry.StarId, Cooldown)
                : Payload != null ? _host.TransformBridgeGimmick(Hero, Payload, Source)
                : _host.TransformAuthoredGimmick(Hero, Channel.Entry.Spec, Source, Source, SourceKind, sourceCooldown: Cooldown);
            internal void Clear()
            { Hero = null; Owner = null; Channel = null; Payload = null; Source = null; RuntimeId = null; LegacyRuntime = null; Notification = default; }
        }
        private readonly Dictionary<Hero, AuthoredOwnerState> _authoredMechanisms = new Dictionary<Hero, AuthoredOwnerState>();
        private long _authoredGeneration;
        private bool _authoredMechanismConfiguring;
        private void OnAuthoredMechanismEquipmentChanged(Hero hero, long epoch)
        {
            if (!_authoredMechanismConfiguring && _authoredMechanisms.TryGetValue(hero, out var state) && state.Build != null)
                ConfigureAuthoredMechanisms(hero, state.Build);
        }
        private static bool AuthoredKeyContributor(AuthoredMechanismEntry entry, string key)
        {
            if (key == null) return false;
            if (entry.StarId == key) return true;
            foreach (string contributor in entry.ContributorIds) if (contributor == key) return true;
            return false;
        }
        private static void ValidateAuthoredHostBuild(Build build)
        {
            foreach (var selected in build.SelectedKeystones)
                foreach (var payload in selected.Payloads)
                    if (payload == KeystonePayloadKind.SacrificeShield && selected.KeystoneId != "h.aurena.key2")
                        throw new InvalidOperationException("The native sacrifice adapter belongs only to its selected key.");
            foreach (var entry in build.Mechanisms)
            {
                var spec = entry.Spec;
                AuthoredMechanisms.Validate(spec);
                if (spec.Kind == AuthoredMechanismKind.StunSourceFilter || spec.Kind == AuthoredMechanismKind.SacrificeShield)
                {
                    var key = build.SelectedKeystones.FirstOrDefault(k => AuthoredKeyContributor(entry, k.KeystoneId));
                    if (key == null)
                        throw new InvalidOperationException("A key-only payload requires its selected contributor.");
                    bool declared = false;
                    foreach (var grant in key.Grants)
                        if (grant.Kind == spec.Kind && grant.ChannelId == spec.ChannelId) declared = true;
                    if (spec.Kind == AuthoredMechanismKind.SacrificeShield)
                        foreach (var payload in key.Payloads) if (payload == KeystonePayloadKind.SacrificeShield) declared = true;
                    if (!declared || spec.Kind == AuthoredMechanismKind.StunSourceFilter && key.KeystoneId != CalmShieldGrant.ConsumerId)
                        throw new InvalidOperationException("The selected key does not declare this key-only payload.");
                }
                if (spec.Bridge == null) continue;
                ValidateBridgeNativeDefinition(spec.Bridge);
                if (spec.Bridge.RetainedFiveRanks)
                {
                    var registered = PairCombos.Get(spec.Bridge.PairId);
                    if (registered?.AuthoredDefinition == null || !registered.AuthoredDefinition.RetainedFiveRanks)
                        throw new InvalidOperationException("Only a registered retained five-rank pair may declare five ranks.");
                }
                foreach (var legacy in build.PairCombos)
                    if (legacy.Def.Id == spec.Bridge.PairId)
                        throw new InvalidOperationException("A bridge cannot retain its legacy payoff.");
            }
        }

        private void ConfigureAuthoredMechanisms(Hero hero, Build build)
        {
            ValidateAuthoredHostBuild(build);
            _authoredMechanismConfiguring = true;
            try
            {
            if (!_authoredMechanisms.TryGetValue(hero, out var state))
                _authoredMechanisms.Add(hero, state = new AuthoredOwnerState());
            long keystoneEpoch = AuthoredKeystoneEpoch(hero);
            if (state.KeystoneEpoch != keystoneEpoch && _runtimes.TryGetValue(hero, out var previousOwner))
            {
                ClearModShieldPools(previousOwner);
                previousOwner.Gimmicks.ClearTransient();
                if (_gimmickV129.TryGetValue(previousOwner, out var gimmicks)) gimmicks.Wounds.Clear();
            }
            state.KeystoneEpoch = keystoneEpoch;
            var next = new Dictionary<string, AuthoredChannelState>(StringComparer.Ordinal);
            var recharge = new List<DirectedRechargeChannel>();
            var bridges = new List<BridgeSuccessDefinition>();
            var primed = new List<MemoryPrimedDefinition>();
            var relay = new List<RelayWindowDefinition>();
            var strikes = new List<KeyValuePair<string, IdentityStrikeDefinition>>();
            var tunings = new List<MemoryTuningDefinition>();
            bool calm = false, sacrifice = false;
            foreach (var entry in build.Mechanisms)
            {
                var spec = entry.Spec;
                string key = AuthoredMechanisms.Key(spec);
                if (!state.Channels.TryGetValue(spec.ChannelId, out var channel) || channel.Key != key)
                {
                    channel = new AuthoredChannelState { Key = key, Generation = checked(++_authoredGeneration) };
                    channel.Quota = "auth." + channel.Generation.ToString(CultureInfo.InvariantCulture);
                }
                channel.Entry = entry;
                next.Add(spec.ChannelId, channel);
                switch (spec.Kind)
                {
                    case AuthoredMechanismKind.DirectedRecharge: recharge.Add(spec.Recharge); break;
                    case AuthoredMechanismKind.BridgeSuccess: bridges.Add(spec.Bridge); break;
                    case AuthoredMechanismKind.MemoryPrimed:
                        var preparation = spec.Primed;
                        var primedResult = TransformAuthoredPayload(hero, AuthoredKeystoneComposer.MechanismPayload(spec),
                            preparation.SourceMemory, null, KeystoneSourceKind.NativeMemory);
                        if (primedResult.Value > 0)
                            primed.Add(new MemoryPrimedDefinition(preparation.ChannelId, preparation.SourceMemory, preparation.Trigger,
                                primedResult.Value * 100m, (float)primedResult.DurationSeconds, preparation.Budget,
                                damageMultiplier: StarDamageScaling.Multiplier(build.SpentStarPoints)));
                        break;
                    case AuthoredMechanismKind.RelayWindow:
                        var window = spec.Relay;
                        var relayResult = TransformAuthoredPayload(hero, AuthoredKeystoneComposer.MechanismPayload(spec),
                            RelayWindowDefinition.SourceMemory, window.TargetMemory, KeystoneSourceKind.NativeMemory);
                        if (relayResult.Value > 0)
                            relay.Add(RelayWindowDefinition.FromEffective(window.ChannelId, window.TargetMemory,
                                relayResult.Value * 100m, (float)relayResult.DurationSeconds,
                                StarDamageScaling.Multiplier(build.SpentStarPoints)));
                        break;
                    case AuthoredMechanismKind.MemoryTuning: tunings.Add(spec.Tuning); break;
                    case AuthoredMechanismKind.IdentityStrike: strikes.Add(new KeyValuePair<string, IdentityStrikeDefinition>(key, spec.IdentityStrike)); break;
                    case AuthoredMechanismKind.StunSourceFilter: calm |= build.SelectedKeystones.Any(k => AuthoredKeyContributor(entry, k.KeystoneId)); break;
                    case AuthoredMechanismKind.SacrificeShield: sacrifice |= build.SelectedKeystones.Any(k => AuthoredKeyContributor(entry, k.KeystoneId)); break;
                }
            }
            bool removed = false;
            foreach (var old in state.Channels)
                if (!next.TryGetValue(old.Key, out var current) || current != old.Value) { removed = true; break; }
            if (removed && _runtimes.TryGetValue(hero, out var owner)) ClearModShieldPools(owner);
            state.Channels.Clear();
            foreach (var pair in next) state.Channels.Add(pair.Key, pair.Value);
            state.DispatchChannels = next.Values.ToArray();
            state.Build = build;
            SetDirectedRechargeChannels(hero, recharge);
            SetBridgeSuccessEffects(hero, bridges, build.MechanismEndpointRanks);
            ConfigureMemoryPreparations(hero, primed);
            ConfigureRelayWindows(hero, relay);
            ConfigureIdentityStrikes(hero, strikes);
            ConfigureMemoryTunings(hero, tunings);
            calm = calm && AuthoredKeystoneActive(hero, CalmShieldGrant.ConsumerId);
            foreach (var selected in build.SelectedKeystones)
                foreach (var payload in selected.Payloads) if (payload == KeystonePayloadKind.SacrificeShield) sacrifice = true;
            ConfigureCalmStun(hero, calm, (root, recipient, grant) =>
            {
                if (!_runtimes.TryGetValue(hero, out var rt)) return;
                AwardModShield(rt, recipient, ModShieldPoolKind.Ordinary,
                    SupportStats.AmplifyShield(recipient.maxHealth * (float)CalmShieldGrant.ValueUnits / 10000f, rt.Powers.Build.Get(Stat.ShieldPower)),
                    CalmShieldGrant.DurationSeconds, null, ModShieldEquipmentEpoch(rt));
            });
            if (sacrifice) BindAuthoredSacrificeShield(hero); else StopSacrificeShield(hero);
            RefreshAuthoredGimmicks(hero, state, true);
            }
            finally { _authoredMechanismConfiguring = false; }
        }

        private static string AuthoredRuntimeId(string channel, string memory)
        {
            using (var hash = SHA256.Create())
            {
                var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(channel + "|" + memory));
                return "auth." + BitConverter.ToString(bytes, 0, 16).Replace("-", "").ToLowerInvariant();
            }
        }
        private void RefreshAuthoredGimmicks(Hero hero, AuthoredOwnerState state, bool force = false)
        {
            var equipment = CollectMechanismEquipment(hero, hero.GetInstanceID());
            if (!force && state.EquipmentEpoch == equipment.EquipmentEpoch) return;
            if (state.EquipmentEpoch != 0 && state.EquipmentEpoch != equipment.EquipmentEpoch)
            {
                foreach (var channel in state.Channels.Values) ResetAuthoredChannel(channel);
                _runtimes[hero].Gimmicks.ClearTransient();
            }
            state.EquipmentEpoch = equipment.EquipmentEpoch;
            var entries = new List<GimmickEntry>(state.Build.Gimmicks);
            state.Gimmicks.Clear();
            state.GimmickIds.Clear();
            foreach (var channel in state.Channels.Values)
            {
                var spec = channel.Entry.Spec;
                if (state.Build.HasSelectedKeystone(channel.Entry.StarId) && !AuthoredKeystoneActive(hero, channel.Entry.StarId)) continue;
                if (spec.Kind == AuthoredMechanismKind.Gimmick)
                    InstallAuthoredGimmick(channel, spec.ChannelId, spec.Gimmick, entries, state);
                else if (spec.Kind == AuthoredMechanismKind.BridgeSuccess)
                {
                    if (spec.Bridge.BasePayoff.Gimmick != null)
                        InstallAuthoredGimmick(channel, spec.Bridge.BasePayoff.ChannelId, spec.Bridge.BasePayoff.Gimmick, entries, state);
                    foreach (var payload in spec.Bridge.Extras)
                        if (payload.Gimmick != null) InstallAuthoredGimmick(channel, payload.ChannelId, payload.Gimmick, entries, state);
                }
            }
            _runtimes[hero].Gimmicks.SetBuild(entries);
        }
        private void InstallAuthoredGimmick(AuthoredChannelState channel, string id, GimmickDef pristine,
            List<GimmickEntry> entries, AuthoredOwnerState state)
        {
            string runtimeId = AuthoredRuntimeId(id, null);
            entries.Add(new GimmickEntry { StarId = runtimeId, Def = pristine, AdmittedSource = true });
            state.GimmickIds[id] = runtimeId;
            state.Gimmicks.Add(runtimeId, channel);
        }
        private GimmickDef TransformBridgeGimmick(Hero hero, BridgePayload payload, string source)
        {
            var skill = payload.Gimmick.Effect == GimmickEffect.Crescendo ? FindMemory(hero, source) : null;
            var result = TransformAuthoredPayload(hero, AuthoredKeystoneComposer.BridgePayload(payload,
                    durationBaseOverride: AuthoredGimmickDurationBase(payload.Gimmick, skill?.currentConfigMaxCooldownTime ?? 0f)),
                source, source, KeystoneSourceKind.NativeMemory);
            return AuthoredKeystoneComposer.EffectiveGimmick(payload.Gimmick, result);
        }
        private void DispatchBridgeGimmick(HeroRuntime owner, Entity victim, BridgeSuccessTransaction transaction, BridgePayload payload)
        {
            if (!_authoredMechanisms.TryGetValue(owner.Hero, out var state)) return;
            var equipment = CollectMechanismEquipment(owner.Hero, transaction.Notification.OwnerId);
            var source = equipment.ResolveEventSource(transaction.Notification, HasOwnSummons(owner));
            if (source == null) return;
            if (!state.GimmickIds.TryGetValue(payload.ChannelId, out string id)) return;
            owner.GimmickRequests.Clear();
            var buffers = RentMechanismDispatchBuffers();
            try
            {
            var dispatch = buffers.Gimmick;
            dispatch.Hero = owner.Hero; dispatch.Owner = state; dispatch.Payload = payload;
            dispatch.Source = source.Memory; dispatch.RuntimeId = id;
            owner.Gimmicks.Fire(payload.Gimmick.Trigger, source.Memory, Time.time, victim != null ? victim.GetInstanceID() : 0,
                transaction.NativeDamage, false, owner.GimmickRequests, -transaction.Notification.ActivationId,
                FindMemory(owner.Hero, source.Memory)?.currentConfigMaxCooldownTime ?? 0f,
                transaction.Notification.NativePayloadKind != NativePayloadKind.AdditionalNative,
                IsGimmickBoss(victim), CountGimmickElements(victim),
                filter: dispatch.Filter, transform: dispatch.Transform);
            if (owner.GimmickRequests.Count > 0 && payload.Gimmick.Effect == GimmickEffect.Sap && victim != null) EnsureSapProcessor(victim);
            if (owner.GimmickRequests.Count > 0 && payload.Gimmick.Effect == GimmickEffect.Primed)
            {
                var accepted = owner.GimmickRequests[0].Entry.Def;
                owner.Powers.PrimeNextBasic(Time.time, accepted.ValuePercent, Gimmicks.Duration(accepted, 5f));
            }
            int start = owner.PendingGimmicks.Count;
            QueueGimmickRequests(owner, victim, Time.time);
            for (int i = start; i < owner.PendingGimmicks.Count; i++)
            {
                var pending = owner.PendingGimmicks[i];
                pending.AuthoredChannelId = payload.ChannelId;
                pending.Authored = new AuthoredPendingGimmick { Enabled = true, Transaction = transaction, Payload = payload,
                    Notification = transaction.Notification, Source = source.Memory, SourceKind = KeystoneSourceKind.NativeMemory };
                owner.PendingGimmicks[i] = pending;
            }
            }
            finally { ReturnMechanismDispatchBuffers(buffers); }
        }
        private bool IsAuthoredGimmick(Hero hero, GimmickEntry entry) =>
            _authoredMechanisms.TryGetValue(hero, out var state) && state.Gimmicks.ContainsKey(entry.StarId);

        private void OnAuthoredMechanismEvent(MemoryActivationEvent notification, Hero hero, Entity victim, float nativeDamage)
        {
            if (!NetworkServer.active || !Alive(hero) || !_memoryAttribution.IsCurrent(notification)
                || notification.GeneratedOrigin != GeneratedOrigin.None || !_authoredMechanisms.TryGetValue(hero, out var state)
                || !_runtimes.TryGetValue(hero, out var rt)) return;
            var equipment = CollectMechanismEquipment(hero, notification.OwnerId);
            if (state.EquipmentEpoch != equipment.EquipmentEpoch) ConfigureAuthoredMechanisms(hero, state.Build);
            bool summons = HasOwnSummons(rt);
            var source = equipment.ResolveEventSource(notification, summons);
            if (source == null) return;
            var buffers = RentMechanismDispatchBuffers();
            try
            {
            var dividends = notification.EventKind == MemoryEventKind.Kill ? buffers.Dividends : null;
            foreach (var channel in state.DispatchChannels)
            {
                var spec = channel.Entry.Spec;
                if (spec.Kind != AuthoredMechanismKind.Gimmick && spec.Kind != AuthoredMechanismKind.AlliedWard
                    && spec.Kind != AuthoredMechanismKind.PressureDividend && spec.Kind != AuthoredMechanismKind.DirectedRecharge
                    && spec.Kind != AuthoredMechanismKind.MemoryPrimed && spec.Kind != AuthoredMechanismKind.RelayWindow) continue;
                if (spec.Condition == AuthoredMechanismCondition.BridgeSuccess) continue;
                DispatchAuthoredChannel(rt, state, channel, notification, source, victim, nativeDamage, equipment, null, dividends);
            }
            DispatchAuthoredDividends(rt, notification, victim, equipment, dividends);
            }
            finally { ReturnMechanismDispatchBuffers(buffers); }
        }
        private void DispatchAuthoredBridgeChannels(Hero hero, Entity victim, BridgeSuccessTransaction transaction)
        {
            if (!_authoredMechanisms.TryGetValue(hero, out var state) || !_runtimes.TryGetValue(hero, out var rt)) return;
            var equipment = CollectMechanismEquipment(hero, transaction.Notification.OwnerId);
            var source = equipment.ResolveEventSource(transaction.Notification, HasOwnSummons(rt));
            if (source == null) return;
            var buffers = RentMechanismDispatchBuffers();
            try
            {
            var dividends = transaction.Notification.EventKind == MemoryEventKind.Kill ? buffers.Dividends : null;
            foreach (var channel in state.DispatchChannels)
                if (channel.Entry.Spec.Condition != AuthoredMechanismCondition.Always && channel.Entry.Spec.PairId == transaction.PairId
                    || channel.Entry.Spec.Kind == AuthoredMechanismKind.PressureDividend && channel.Entry.Spec.Condition == AuthoredMechanismCondition.Always)
                    DispatchAuthoredChannel(rt, state, channel, transaction.Notification, source, victim, transaction.NativeDamage, equipment, transaction, dividends);
            DispatchAuthoredDividends(rt, transaction.Notification, victim, equipment, dividends);
            }
            finally { ReturnMechanismDispatchBuffers(buffers); }
        }
        private void DispatchAuthoredDividends(HeroRuntime rt, MemoryActivationEvent notification, Entity victim,
            MechanismEquipment equipment, List<PressureDividendChannel> channels)
        {
            if (channels == null || channels.Count == 0 || !(victim is Monster monster)
                || !_pressureDividendSpawns.TryGetValue(monster, out var spawn) || !_nativePressureLootSpawns.Contains(spawn)) return;
            var equipped = _attributionMemoryIds[rt.Hero];
            AdmitPressureDividendNativeKill(rt.Hero, monster, new PressureDividendAttribution(
                rt.Hero.netId.ToString(CultureInfo.InvariantCulture), notification.SourceMemory,
                PressureDividendKillOrigin.NativeMemory, PressureDividendVictimKind.NativeLootEnemy), channels, equipped);
        }
        private void DispatchAuthoredChannel(HeroRuntime rt, AuthoredOwnerState state, AuthoredChannelState channel,
            MemoryActivationEvent notification, EquippedMechanismMemory source, Entity victim, float nativeDamage,
            MechanismEquipment equipment, BridgeSuccessTransaction transaction, List<PressureDividendChannel> dividends)
        {
            if (!_memoryAttribution.IsCurrent(notification)) return;
            var spec = channel.Entry.Spec;
            MemoryEventKind trigger = spec.Trigger;
            var identity = rt.Hero.Skill.GetSkill(HeroSkillLocation.Identity);
            if (identity != null && spec.TriggerByIdentity.TryGetValue(identity.GetType().Name, out var selected)) trigger = selected;
            if (trigger != notification.EventKind || spec.Source != null && !spec.Source.Matches(source)) return;
            if (state.Build.HasSelectedKeystone(channel.Entry.StarId) && !AuthoredKeystoneActive(rt.Hero, channel.Entry.StarId)) return;
            foreach (string required in spec.RequiredMemories) if (equipment.Find(required) == null) return;
            if (spec.Condition != AuthoredMechanismCondition.Always)
            {
                if (!_bridgeSuccessEffects.TryGetValue(rt.Hero, out var bridge)) return;
                if (spec.Condition == AuthoredMechanismCondition.BridgeSuccess
                    && (transaction == null || !transaction.IsCurrent(bridge.Runtime, equipment, bridge.EndpointRanks))) return;
                bool committedGate = false;
                if (transaction != null && transaction.IsCurrent(bridge.Runtime, equipment, bridge.EndpointRanks))
                    foreach (var definition in state.Channels.Values)
                        if (definition.Entry.Spec.Bridge != null && definition.Entry.Spec.Bridge.PairId == spec.PairId
                            && (spec.Condition == AuthoredMechanismCondition.BridgeMark && definition.Entry.Spec.Bridge.GateKind == BridgeGateKind.Mark
                                || spec.Condition == AuthoredMechanismCondition.BridgeWindow && definition.Entry.Spec.Bridge.GateKind == BridgeGateKind.Window))
                            committedGate = true;
                if (spec.Condition == AuthoredMechanismCondition.BridgeMark && !committedGate
                    && !bridge.Runtime.HasBridgeMark(spec.PairId, notification.VictimId, Time.time, equipment, bridge.EndpointRanks)) return;
                if (spec.Condition == AuthoredMechanismCondition.BridgeWindow && !committedGate
                    && !bridge.Runtime.HasBridgeWindow(spec.PairId, Time.time, equipment, bridge.EndpointRanks)) return;
            }
            var sourceKind = notification.NativePayloadKind == NativePayloadKind.MainBasicAttack ? KeystoneSourceKind.OwnedBasicAttack
                : notification.NativePayloadKind == NativePayloadKind.SummonAttack ? KeystoneSourceKind.OwnedSummon : KeystoneSourceKind.NativeMemory;
            int everyN = spec.Kind == AuthoredMechanismKind.DirectedRecharge ? 1 : TransformAuthoredEveryN(rt.Hero, source.Memory, spec, sourceKind);
            if (everyN > 1)
            {
                if (!channel.Counted.Add(notification.ActivationId)) return;
                if (++channel.Count < everyN) return;
                channel.Count = 0;
            }
            string quota = channel.Quota;
            if (spec.Kind == AuthoredMechanismKind.Gimmick)
            {
                rt.GimmickRequests.Clear();
                var buffers = RentMechanismDispatchBuffers();
                try
                {
                var dispatch = buffers.Gimmick;
                dispatch.Hero = rt.Hero; dispatch.Owner = state; dispatch.Channel = channel;
                dispatch.Notification = notification; dispatch.Source = source.Memory; dispatch.SourceKind = sourceKind;
                var skill = FindMemory(rt.Hero, source.Memory);
                dispatch.Cooldown = skill != null ? skill.currentConfigMaxCooldownTime : 0f;
                rt.Gimmicks.Fire(spec.Gimmick.Trigger, source.Memory, Time.time, victim != null ? victim.GetInstanceID() : 0,
                    nativeDamage, false, rt.GimmickRequests, -notification.ActivationId,
                    skill != null ? skill.currentConfigMaxCooldownTime : 0f,
                    notification.NativePayloadKind != NativePayloadKind.AdditionalNative, IsGimmickBoss(victim),
                    CountGimmickElements(victim),
                    filter: dispatch.Filter, admit: dispatch.Admit, transform: dispatch.Transform);
                if (rt.GimmickRequests.Count == 0) return;
                if (spec.Gimmick.Effect == GimmickEffect.Sap && victim != null) EnsureSapProcessor(victim);
                if (spec.Gimmick.Effect == GimmickEffect.Primed)
                {
                    var accepted = rt.GimmickRequests[0].Entry.Def;
                    rt.Powers.PrimeNextBasic(Time.time, accepted.ValuePercent, Gimmicks.Duration(accepted, 5f));
                }
                long generation = channel.Generation;
                int start = rt.PendingGimmicks.Count;
                QueueGimmickRequests(rt, victim, Time.time);
                for (int i = start; i < rt.PendingGimmicks.Count; i++)
                {
                    var pending = rt.PendingGimmicks[i];
                    pending.AuthoredChannelId = spec.ChannelId;
                    pending.Authored = new AuthoredPendingGimmick { Enabled = true, Notification = notification,
                        Generation = generation, Spec = spec, Transaction = transaction, Source = source.Memory, SourceKind = sourceKind };
                    rt.PendingGimmicks[i] = pending;
                }
                }
                finally { ReturnMechanismDispatchBuffers(buffers); }
            }
            else if (spec.Kind == AuthoredMechanismKind.AlliedWard)
            {
                if (DispatchAdmittedWard(rt, spec.Ward, source.Memory, ModShieldEquipmentEpoch(rt),
                    authored: spec, sourceKind: sourceKind, quota: quota, notification: notification,
                    budget: spec.Once ? AttributionBudget.PerActivation : spec.Budget) == 0) return;
            }
            else if (spec.Kind == AuthoredMechanismKind.PressureDividend)
            {
                if (dividends != null && _memoryAttribution.TrySpend(quota,
                    spec.Once ? AttributionBudget.PerActivation : spec.Budget, notification, true))
                {
                    var probability = TransformAuthoredPayload(rt.Hero, AuthoredKeystoneComposer.MechanismPayload(spec),
                        source.Memory, null, sourceKind);
                    if (probability.ProbabilityPercent > 0)
                        dividends.Add(PressureDividendChannel.FromEffective(spec.Dividend.SourceMemory, spec.Dividend.RequiredMemories,
                            spec.Dividend.ContributorIds, probability.ProbabilityPercent * 100m));
                }
            }
            else if (spec.Kind == AuthoredMechanismKind.DirectedRecharge)
                DispatchDirectedRechargeEvent(notification, rt.Hero, victim, nativeDamage, spec.ChannelId);
            else if (spec.Kind == AuthoredMechanismKind.MemoryPrimed || spec.Kind == AuthoredMechanismKind.RelayWindow)
            {
                var preparations = GetMemoryPrimedRelay(rt.Hero);
                var admitted = WithPreparationEpoch(notification, preparations);
                if (spec.Kind == AuthoredMechanismKind.MemoryPrimed)
                    preparations.Primed.OnSourceEvent(admitted, Time.time, triggerAlreadyAdmitted: true, channelId: spec.ChannelId);
                else preparations.Relay.OnSourceEvent(admitted, Time.time, channelId: spec.ChannelId);
            }
        }
        private bool RefreshAuthoredPendingGimmick(HeroRuntime runtime, ref PendingGimmick pending)
        {
            var context = pending.Authored;
            if (!context.Enabled) return true;
            var hero = runtime.Hero;
            GimmickDef effective;
            if (context.Legacy != null)
            {
                var current = LegacyGimmickForRequest(runtime, context.Legacy.StarId);
                if (context.EquipmentEpoch != EnsureMemoryAttributionEquipment(hero) || current == null
                    || BuildAggregation.GimmickStateKey(current) != context.LegacyKey
                        && !(context.HadKeystone && runtime.Powers.Build.SelectedKeystones.Count == 0
                            && SameLegacyPendingBaseline(context.Legacy, current))) return false;
                effective = TransformLegacyGimmick(runtime, current.Def, pending.Request.Entry.Memory,
                    pending.Request.SourceKind ?? KeystoneSourceKind.NativeMemory, context.Legacy.StarId);
            }
            else
            {
                if (!_memoryAttribution.IsCurrent(context.Notification) || !_authoredMechanisms.TryGetValue(hero, out var owner)) return false;
                if (context.Spec != null && (!owner.Channels.TryGetValue(context.Spec.ChannelId, out var channel)
                    || channel.Generation != context.Generation)) return false;
                if (context.Transaction != null && !context.Transaction.IsCurrent(
                    CollectMechanismEquipment(hero, context.Notification.OwnerId), owner.Build.MechanismEndpointRanks)) return false;
                effective = context.Payload != null ? TransformBridgeGimmick(hero, context.Payload, context.Source)
                    : TransformAuthoredGimmick(hero, context.Spec, context.Source, context.Source, context.SourceKind,
                        sourceCooldown: FindMemory(hero, context.Source)?.currentConfigMaxCooldownTime ?? 0f);
            }
            if (effective == null) return false;
            pending.Request.Entry = new GimmickEntry { StarId = pending.Request.Entry.StarId,
                Memory = pending.Request.Entry.Memory, Def = effective };
            if (effective.Effect == GimmickEffect.Burst || pending.Request.AreaRadius > 0)
                pending.Request.AreaRadius = Gimmicks.Radius(effective, Gimmicks.AreaRadius);
            if (effective.Effect == GimmickEffect.Echo)
                pending.Due = pending.QueuedAt + (effective.EffectiveDelaySeconds ?? 0.3f);
            return true;
        }
        private static void ResetAuthoredChannel(AuthoredChannelState channel)
        { channel.Count = 0; channel.Counted.Clear(); }
        private void ClearAuthoredMechanismTransients(Hero hero = null)
        {
            ResetIdentityStrikes(hero);
            foreach (var pair in _authoredMechanisms)
                if (hero == null || pair.Key == hero)
                    foreach (var channel in pair.Value.Channels.Values) ResetAuthoredChannel(channel);
        }
    }
}
