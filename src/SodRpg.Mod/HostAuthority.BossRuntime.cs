using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        internal sealed class BossCombatState
        {
            public Build Build;
            public long Epoch, NextId, Revision;
            public string Run;
            public Room Room;
            public readonly SkillTrigger[] Memories = new SkillTrigger[LinkSkills.Length];
            public readonly List<Gem> Essences = new List<Gem>(32);
            public readonly BossShapeAttack Shapes = new BossShapeAttack();
            public readonly BossProjectileExecutor Projectiles = new BossProjectileExecutor();
            public readonly BossFieldExecutor Fields = new BossFieldExecutor();
            public readonly BossMovementExecutor Movement = new BossMovementExecutor();
            public readonly BossEnemyMovementExecutor EnemyMovement = new BossEnemyMovementExecutor();
            public readonly BossDeployableExecutor Deployables = new BossDeployableExecutor();
            public readonly BossDefenseExecutor Defense = new BossDefenseExecutor();
            public readonly BossProgressLedger Ledger = new BossProgressLedger();
            public readonly Dictionary<string, float> Ready = new Dictionary<string, float>(128, StringComparer.Ordinal);
            public readonly BossPulseBuffer Sequence = new BossPulseBuffer();
            public readonly Vector3[] MarchPoints = new Vector3[3];
            public uint SetMask;
            public float MainHpDamage;
            public float ArrivalUntil;
            public long HysteriaState;
            public bool PreviousClawLeft;
            public int Mode;
            public float ModeUntil;
            public Vector3 MovementOrigin, MemoryDirection;
            public bool MovementOriginValid, MemoryDirectionValid;
            public readonly List<BossClawPair> ClawPairs = new List<BossClawPair>(8);
        }

        internal sealed class BossProgressLedger
        {
            private struct Progress { public int Count; public float Updated; public long Activation; }
            private struct Mark
            {
                public Entity Victim; public string Ledger; public float Until, Creation; public int Count;
                public long Life, Source0, Source1, Source2;
            }
            private readonly Dictionary<string, Progress> _progress = new Dictionary<string, Progress>(64, StringComparer.Ordinal);
            private readonly Mark[] _marks = new Mark[8];
            private readonly long[,] _seen = new long[8, 64];
            private readonly int[] _next = new int[8];
            public bool Admit(BossEvent kind, long activation)
            {
                if (activation == 0) return false;
                int row = (int)kind;
                for (int i = 0; i < 64; i++) if (_seen[row, i] == activation) return false;
                _seen[row, _next[row]] = activation;
                _next[row] = (_next[row] + 1) % 64;
                return true;
            }
            public bool Advance(string key, long activation, int required, float lifetime, float now)
            {
                if (!_progress.TryGetValue(key, out var p))
                {
                    if (_progress.Count >= 64) return false;
                    _progress.Add(key, p = default);
                }
                if (p.Activation == activation) return false;
                p.Activation = activation;
                if (lifetime > 0 && now - p.Updated > lifetime) p.Count = 0;
                p.Count = Math.Min(Math.Min(6, Math.Max(1, required)), p.Count + 1);
                p.Updated = now;
                _progress[key] = p;
                return p.Count >= required;
            }
            public void Consume(string key) { if (_progress.TryGetValue(key, out var p)) { p.Count = 0; _progress[key] = p; } }
            public void MarkTarget(Entity victim, int count, float lifetime, float now, string ledger, long nativeSource = 0)
            {
                if (victim == null || count <= 0 || lifetime <= 0) return;
                long life = NativeInstance.BossNativeActorLife(victim);
                int slot = -1;
                for (int i = 0; i < _marks.Length; i++)
                {
                    if (_marks[i].Victim == victim && _marks[i].Ledger == ledger && _marks[i].Creation == victim.creationTime && _marks[i].Life == life && _marks[i].Until > now) { slot = i; break; }
                    if (slot < 0 && (_marks[i].Until <= now || !BossAlive(_marks[i].Victim) || _marks[i].Creation != _marks[i].Victim.creationTime
                        || !NativeInstance.BossNativeSameLife(_marks[i].Victim, _marks[i].Life))) slot = i;
                }
                if (slot < 0) return;
                var mark = _marks[slot];
                if (mark.Victim != victim || mark.Ledger != ledger || mark.Creation != victim.creationTime || mark.Life != life || mark.Until <= now)
                { mark.Count = 0; mark.Source0 = mark.Source1 = mark.Source2 = 0; }
                mark.Victim = victim; mark.Ledger = ledger; mark.Creation = victim.creationTime; mark.Life = life; mark.Until = now + lifetime;
                for (int i = 0; i < count && mark.Count < 3; i++) AppendMark(ref mark, nativeSource);
                _marks[slot] = mark;
            }
            public int MarkCount(Entity victim, float now, string ledger)
            {
                if (victim == null) return 0;
                for (int i = 0; i < _marks.Length; i++)
                    if (_marks[i].Victim == victim && _marks[i].Ledger == ledger && _marks[i].Until > now && _marks[i].Creation == victim.creationTime
                        && NativeInstance.BossNativeSameLife(victim, _marks[i].Life)) return _marks[i].Count;
                return 0;
            }
            public long MarkNativeSource(Entity victim, float now, string ledger)
            {
                if (victim == null) return 0;
                for (int i = 0; i < _marks.Length; i++)
                {
                    var mark = _marks[i];
                    if (mark.Count == 0 || mark.Victim != victim || mark.Ledger != ledger || mark.Until <= now
                        || mark.Creation != victim.creationTime || !NativeInstance.BossNativeSameLife(victim, mark.Life)) continue;
                    return mark.Source0 > 0 ? mark.Source0 : mark.Source1 > 0 ? mark.Source1 : mark.Source2;
                }
                return 0;
            }
            public bool ConsumeMarks(Entity victim, int required, float now, string ledger)
            {
                if (required <= 0 || MarkCount(victim, now, ledger) < required) return false;
                for (int i = 0; i < _marks.Length; i++)
                    if (_marks[i].Victim == victim && _marks[i].Ledger == ledger && _marks[i].Until > now && _marks[i].Creation == victim.creationTime
                        && NativeInstance.BossNativeSameLife(victim, _marks[i].Life))
                    {
                        var mark = _marks[i];
                        for (int j = 0; j < required; j++) { mark.Source0 = mark.Source1; mark.Source1 = mark.Source2; mark.Source2 = 0; mark.Count--; }
                        _marks[i] = mark; return true;
                    }
                return false;
            }
            private static void AppendMark(ref Mark mark, long source)
            {
                if (mark.Count == 0) mark.Source0 = source;
                else if (mark.Count == 1) mark.Source1 = source;
                else mark.Source2 = source;
                mark.Count++;
            }
            public void CancelNativeMarks(string ledger, long nativeSource)
            {
                if (nativeSource <= 0) return;
                for (int i = 0; i < _marks.Length; i++)
                {
                    var mark = _marks[i];
                    if (mark.Ledger != ledger || mark.Count == 0) continue;
                    int count = mark.Count;
                    long source0 = mark.Source0, source1 = mark.Source1, source2 = mark.Source2;
                    mark.Count = 0; mark.Source0 = mark.Source1 = mark.Source2 = 0;
                    for (int j = 0; j < count; j++)
                    {
                        long source = j == 0 ? source0 : j == 1 ? source1 : source2;
                        if (source != nativeSource) AppendMark(ref mark, source);
                    }
                    _marks[i] = mark;
                }
            }
            public Entity NearestMark(Vector3 point, float now, string ledger = null)
            {
                Entity result = null; float best = float.MaxValue;
                for (int i = 0; i < _marks.Length; i++)
                    if (_marks[i].Count > 0 && (ledger == null || _marks[i].Ledger == ledger) && _marks[i].Until > now && BossAlive(_marks[i].Victim)
                        && _marks[i].Creation == _marks[i].Victim.creationTime && NativeInstance.BossNativeSameLife(_marks[i].Victim, _marks[i].Life))
                    {
                        float distance = (_marks[i].Victim.position - point).sqrMagnitude;
                        if (distance < best) { best = distance; result = _marks[i].Victim; }
                    }
                return result;
            }
            public void Clear(bool preserveNative = false)
            {
                _progress.Clear(); Array.Clear(_seen, 0, _seen.Length); Array.Clear(_next, 0, _next.Length);
                if (!preserveNative) { Array.Clear(_marks, 0, _marks.Length); return; }
                for (int i = 0; i < _marks.Length; i++)
                {
                    var mark = _marks[i]; int count = mark.Count;
                    long source0 = mark.Source0, source1 = mark.Source1, source2 = mark.Source2;
                    mark.Count = 0; mark.Source0 = mark.Source1 = mark.Source2 = 0;
                    for (int j = 0; j < count; j++)
                    {
                        long source = j == 0 ? source0 : j == 1 ? source1 : source2;
                        if (source > 0) AppendMark(ref mark, source);
                    }
                    _marks[i] = mark;
                }
            }
        }

        private bool BossEnsure(HeroRuntime rt)
        {
            if (!NetworkServer.active || rt == null || !BossAlive(rt.Hero)) { if (rt != null && rt.Boss.Build != null) ClearBossEffects(rt); return false; }
            var state = rt.Boss;
            if (NetworkedManagerBase<ZoneManager>.softInstance?.isInAnyTransition == true)
            {
                if (state.Build != null) ClearBossEffects(rt);
                return false;
            }
            var build = rt.Powers.Build;
            var room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            bool changed = state.Build != build || state.Epoch != rt.ShieldEquipmentEpoch || state.Room != room || state.Run != run;
            for (int i = 0; i < LinkSkills.Length; i++)
                if (state.Memories[i] != rt.Hero.Skill?.GetSkill(LinkSkills[i])) changed = true;
            if (!BossEssenceSnapshot(rt, state, false, ref changed)) { ClearBossEffects(rt); return false; }
            if (changed)
            {
                if (state.Build != null) ClearBossEffects(rt, state.Room == room && state.Run == run && BossAlive(rt.Hero));
                state.Build = build; state.Epoch = rt.ShieldEquipmentEpoch; state.Room = room; state.Run = run;
                for (int i = 0; i < LinkSkills.Length; i++) state.Memories[i] = rt.Hero.Skill?.GetSkill(LinkSkills[i]);
                BossEssenceSnapshot(rt, state, true, ref changed);
                state.SetMask = 0;
                if (build != null)
                {
                    for (int i = 0; i < build.BossMoves.Count; i++) state.SetMask |= BossProfiles.SetMask(build.BossMoves[i].SetId);
                    for (int i = 0; i < build.BossRewards.Count; i++) state.SetMask |= BossProfiles.SetMask(build.BossRewards[i].SetId);
                }
            }
            return build != null && (build.BossMoves.Count != 0 || build.BossRewards.Count != 0);
        }

        private static bool BossEssenceSnapshot(HeroRuntime rt, BossCombatState state, bool copy, ref bool changed)
        {
            if (copy) state.Essences.Clear();
            int count = 0;
            if (rt.Hero.Skill != null)
            {
                for (int s = 0; s < 6; s++)
                {
                    var skill = (HeroSkillLocation)s; int slots = rt.Hero.Skill.GetMaxGemCount(skill);
                    if (slots < 0 || slots > 32) return false;
                    for (int i = 0; i < slots; i++)
                    {
                        if (!rt.Hero.Skill.TryGetGem(new GemLocation { skill = skill, index = i }, out var gem)) continue;
                        if (count >= 32) return false;
                        if (copy) state.Essences.Add(gem);
                        else if (count >= state.Essences.Count || !ReferenceEquals(state.Essences[count], gem)) changed = true;
                        count++;
                    }
                }
                if (count != rt.Hero.Skill.gems.Count) return false;
            }
            if (!copy && count != state.Essences.Count) changed = true;
            return true;
        }

        private void TickBossEffects(HeroRuntime rt, float now)
        {
            if (!BossEnsure(rt))
            {
                TickBossSets(rt, now, false);
                RefreshBossVisualEquipment(rt); return;
            }
            TickBossNativeAdapters(rt);
            TickBossSets(rt, now, true);
            RefreshBossVisualEquipment(rt);
            rt.Boss.Fields.Tick(this, rt, now);
            if (rt.Boss.Build != null && BossAlive(rt.Hero)) InkWhiteAfterFields(rt, now);
            rt.Boss.Projectiles.Tick(this, rt, now);
            rt.Boss.Deployables.Tick(this, rt, now);
            rt.Boss.Defense.Tick(this, rt, now);
            if (rt.Boss.ModeUntil > 0 && now >= rt.Boss.ModeUntil)
            {
                rt.Boss.Mode = 0; rt.Boss.ModeUntil = 0;
                BossDispatch(rt, BossEvent.ModeTransition, 0, null, rt.Hero.agentPosition, now, false);
            }
            BossDispatch(rt, BossEvent.Clock, 0, null, rt.Hero.agentPosition, now, false);
        }

        private void TickBossSets(HeroRuntime rt, float now, bool full)
        {
            uint mask = rt.Boss.SetMask;
            if (full && (mask & (1u << 1)) != 0) TickSkollBoss(rt, now);
            if (full && (mask & (1u << 2)) != 0) TickInfernusBoss(rt, now);
            if ((mask & ((1u << 3) | (1u << 4))) != 0) TickInkBoss(rt, now);
            if ((mask & (1u << 5)) != 0) TickNyxBoss(rt, now);
            if ((mask & (1u << 6)) != 0) TickErebosBoss(rt, now);
            if ((mask & (1u << 7)) != 0) TickSeekerBoss(rt, now);
            if ((mask & (1u << 8)) != 0) TickAzurakBoss(rt, now);
            if ((mask & (1u << 9)) != 0) TickPrimusBoss(rt, now);
            if ((mask & (1u << 10)) != 0) TickLightBoss(rt, now);
            if ((mask & (1u << 11)) != 0) TickMawBoss(rt, now);
            if ((mask & (1u << 12)) != 0) TickObliviaxBoss(rt, now);
            if ((mask & (1u << 13)) != 0) TickPolarisBoss(rt, now);
        }

        private void ClearBossEffects(HeroRuntime rt, bool preserveRewards = false)
        {
            rt.Boss.Revision++;
            ClearBossNativeAdapters(rt);
            ClearSkollBoss(rt);
            ClearInfernusBoss(rt);
            ClearInkBoss(rt, preserveRewards);
            ClearNyxBoss(rt, preserveRewards); ClearErebosBoss(rt, preserveRewards);
            ClearSeekerBoss(rt, preserveRewards); ClearAzurakBoss(rt, preserveRewards); ClearPrimusBoss(rt, preserveRewards);
            ClearLightBoss(rt, preserveRewards); ClearMawBoss(rt, preserveRewards);
            ClearObliviaxBoss(rt, preserveRewards); ClearPolarisBoss(rt, preserveRewards);
            var s = rt.Boss;
            s.Fields.Clear(preserveRewards); s.Projectiles.Clear(preserveRewards); s.Deployables.Clear();
            s.Defense.Clear(this, rt); s.EnemyMovement.Clear(); s.Ledger.Clear(preserveRewards);
            if (!preserveRewards) { s.Ready.Clear(); s.SetMask = 0; }
            s.Sequence.Reset(); s.MainHpDamage = 0;
            s.Build = null; s.ArrivalUntil = 0; s.HysteriaState = 0; s.Mode = 0; s.ModeUntil = 0;
            s.PreviousClawLeft = false;
            s.ClawPairs.Clear();
            s.Essences.Clear();
            Array.Clear(s.Memories, 0, s.Memories.Length);
            ClearBossVisuals(rt, preserveRewards);
        }

        private int BossRewardStage(HeroRuntime rt, string profileId)
        {
            if (rt == null || !Alive(rt.Hero) || rt.Hero.isKnockedOut || !BossProfiles.TryGetReward(profileId, out var profile)) return 0;
            bool equipped = FindMemory(rt.Hero, profile.Requires) != null;
            if (!equipped)
                for (int i = 0; i < rt.Boss.Essences.Count; i++)
                    if (rt.Boss.Essences[i] != null && NativeActorTypeName(rt.Boss.Essences[i]) == profile.Requires) { equipped = true; break; }
            if (!equipped) return 0;
            var rewards = rt.Powers.Build.BossRewards;
            for (int i = 0; i < rewards.Count; i++)
                if (rewards[i].ProfileId == profile.Id && rewards[i].SetId == profile.SetId) return rewards[i].Stage;
            return 0;
        }

        private static bool BossReady(HeroRuntime rt, string key, float now, int cooldown)
        {
            if (rt.Boss.Ready.TryGetValue(key, out float due) && now < due) return false;
            if (!rt.Boss.Ready.ContainsKey(key) && rt.Boss.Ready.Count >= 128) return false;
            rt.Boss.Ready[key] = now + cooldown / 1000f;
            return true;
        }
        private static Vector3 BossCursor(HeroRuntime rt) => rt.Hero.owner != null ? rt.Hero.owner.cursorWorldPos : rt.Hero.agentPosition;
        private static Vector3 BossDirection(Vector3 from, Vector3 to) { var d = to - from; d.y = 0; return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero; }
        private static float BossDirectionDistance(Vector3 from, Vector3 to) { var d = to - from; d.y = 0; return d.magnitude; }
        private static bool BossGround(Vector3 origin, Vector3 desired, float range, out Vector3 point)
        {
            var delta = desired - origin; delta.y = 0;
            point = Dew.GetValidAgentDestination_LinearSweep(origin, origin + Vector3.ClampMagnitude(delta, range));
            return BossFinite(point) && Mathf.Abs(point.y - origin.y) <= 2f && !Physics.Linecast(origin + Vector3.up * 0.2f, point + Vector3.up * 0.2f, LayerMasks.Ground);
        }
        private static bool BossFinite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        private static float BossCoefficient(BossMoveEntry entry, BossMoveProfile profile, string channel)
        {
            for (int i = 0; i < entry.Channels.Count; i++)
                if (entry.Channels[i].ChannelId == channel)
                    for (int j = 0; j < profile.Channels.Count; j++)
                        if (profile.Channels[j].ChannelId == channel)
                            return Math.Min(entry.Channels[i].ValueMilli, profile.SetId == BossProfiles.DemonSetId ? profile.Channels[j].CapMilli : profile.Channels[j].CapMilli * 3L) / 100000f;
            return 0;
        }
        private static float BossAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channel)
        {
            for (int i = 0; i < profile.Channels.Count; i++)
                if (profile.Channels[i].ChannelId == channel && profile.Channels[i].Kind != BossCoefficientKind.Damage
                    && profile.Channels[i].Kind != BossCoefficientKind.Heal && profile.Channels[i].Kind != BossCoefficientKind.Shield)
                    return BossCoefficient(entry, profile, channel) * 100f;
            return Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower) * BossCoefficient(entry, profile, channel);
        }
        private static bool BossMagic(HeroRuntime rt) => rt.Hero.Status.abilityPower > rt.Hero.Status.attackDamage;
        internal static bool IsBossGeneratedDamage(DamageData damage) => damage.IsAmountModifiedBy(typeof(BossCombatState));
        private Entity _bossDamageVictim;
        private Hero _bossDamageOwner;
        private bool _bossDamageAccepted;
        private long _bossDamageSerial;
        private float _bossDamageHp;
        internal void ObserveBossGeneratedDispatch(NativeAttributedDamagePacket.Packet packet, DamageData damage)
        {
            if (_bossDamageSerial == 0 && packet != null && packet.Actor == _bossDamageOwner && packet.Victim == _bossDamageVictim
                && IsBossGeneratedDamage(damage)) _bossDamageSerial = packet.Serial;
        }
        private void ObserveBossGeneratedDamage(EventInfoDamage info)
        {
            var packet = NativeAttributedDamagePacket.Current;
            if (info.actor == _bossDamageOwner && info.victim == _bossDamageVictim && info.damage.amount > 0
                && packet != null && packet.Serial == _bossDamageSerial)
            {
                _bossDamageAccepted = true;
                _bossDamageHp = packet.HpDamage;
            }
        }
        private bool BossDamage(HeroRuntime rt, Entity victim, float amount, bool magic, BossElement element = BossElement.Neutral) =>
            BossDamageHp(rt, victim, amount, magic, element, out _);
        private bool BossDamageHp(HeroRuntime rt, Entity victim, float amount, bool magic, BossElement element, out float hpDamage)
        {
            hpDamage = 0f;
            if (amount <= 0 || !BossAlive(rt.Hero) || !BossAlive(victim) || victim == rt.Hero || victim.GetRelation(rt.Hero) != EntityRelation.Enemy) return false;
            var previousVictim = _bossDamageVictim; var previousOwner = _bossDamageOwner;
            bool previousAccepted = _bossDamageAccepted; long previousSerial = _bossDamageSerial;
            float previousHp = _bossDamageHp;
            _bossDamageVictim = victim; _bossDamageOwner = rt.Hero; _bossDamageAccepted = false; _bossDamageSerial = 0; _bossDamageHp = 0f;
            EnterGenerated(rt.Hero);
            try
            {
                var damage = magic ? rt.Hero.MagicDamage(amount, 0f) : rt.Hero.PhysicalDamage(amount, 0f);
                ElementalType? native = element == BossElement.Fire ? ElementalType.Fire : element == BossElement.Cold ? ElementalType.Cold
                    : element == BossElement.Light ? ElementalType.Light : element == BossElement.Dark ? ElementalType.Dark : (ElementalType?)null;
                damage.SetElemental(native).SetAmountModifiedBy(typeof(BossCombatState)).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
                hpDamage = _bossDamageAccepted ? _bossDamageHp : 0f;
                return _bossDamageAccepted;
            }
            finally
            {
                _bossDamageVictim = previousVictim; _bossDamageOwner = previousOwner; _bossDamageAccepted = previousAccepted;
                _bossDamageSerial = previousSerial; _bossDamageHp = previousHp;
                ExitGenerated(rt.Hero);
            }
        }
    }
}
