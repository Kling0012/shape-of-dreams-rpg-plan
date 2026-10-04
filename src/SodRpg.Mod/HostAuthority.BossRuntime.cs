using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private sealed class BossCombatState
        {
            public Build Build;
            public long Epoch, NextId;
            public string Run;
            public Room Room;
            public readonly SkillTrigger[] Memories = new SkillTrigger[LinkSkills.Length];
            public readonly List<Gem> Essences = new List<Gem>();
            public readonly BossShapeAttack Shapes = new BossShapeAttack();
            public readonly BossProjectileExecutor Projectiles = new BossProjectileExecutor();
            public readonly BossFieldExecutor Fields = new BossFieldExecutor();
            public readonly BossMovementExecutor Movement = new BossMovementExecutor();
            public readonly BossEnemyMovementExecutor EnemyMovement = new BossEnemyMovementExecutor();
            public readonly BossDeployableExecutor Deployables = new BossDeployableExecutor();
            public readonly BossDefenseExecutor Defense = new BossDefenseExecutor();
            public readonly BossProgressLedger Ledger = new BossProgressLedger();
            public readonly Dictionary<string, float> Ready = new Dictionary<string, float>(StringComparer.Ordinal);
            public readonly Dictionary<BossAction, string> ActionKeys = new Dictionary<BossAction, string>();
            public float ArrivalUntil;
            public long HysteriaState;
            public bool PreviousClawLeft;
            public int Mode;
            public float ModeUntil;
            public readonly List<BossClawPair> ClawPairs = new List<BossClawPair>(8);
        }

        private sealed class BossProgressLedger
        {
            private sealed class Progress { public int Count; public float Updated; public long Activation; }
            private struct Mark { public Entity Victim; public string Ledger; public float Until, Creation; public int Count; }
            private readonly Dictionary<string, Progress> _progress = new Dictionary<string, Progress>(StringComparer.Ordinal);
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
                    _progress.Add(key, p = new Progress());
                }
                if (p.Activation == activation) return false;
                p.Activation = activation;
                if (lifetime > 0 && now - p.Updated > lifetime) p.Count = 0;
                p.Count = Math.Min(Math.Min(6, Math.Max(1, required)), p.Count + 1);
                p.Updated = now;
                return p.Count >= required;
            }
            public void Consume(string key) { if (_progress.TryGetValue(key, out var p)) p.Count = 0; }
            public void MarkTarget(Entity victim, int count, float lifetime, float now, string ledger)
            {
                if (victim == null || count <= 0 || lifetime <= 0) return;
                int slot = -1;
                for (int i = 0; i < _marks.Length; i++)
                {
                    if (_marks[i].Victim == victim && _marks[i].Ledger == ledger && _marks[i].Creation == victim.creationTime && _marks[i].Until > now) { slot = i; break; }
                    if (slot < 0 && (_marks[i].Until <= now || !BossAlive(_marks[i].Victim) || _marks[i].Creation != _marks[i].Victim.creationTime)) slot = i;
                }
                if (slot < 0) return;
                var mark = _marks[slot];
                if (mark.Victim != victim || mark.Ledger != ledger || mark.Creation != victim.creationTime || mark.Until <= now) mark.Count = 0;
                mark.Victim = victim; mark.Ledger = ledger; mark.Creation = victim.creationTime; mark.Until = now + lifetime; mark.Count = Math.Min(3, mark.Count + count);
                _marks[slot] = mark;
            }
            public int MarkCount(Entity victim, float now, string ledger)
            {
                if (victim == null) return 0;
                for (int i = 0; i < _marks.Length; i++)
                    if (_marks[i].Victim == victim && _marks[i].Ledger == ledger && _marks[i].Until > now && _marks[i].Creation == victim.creationTime) return _marks[i].Count;
                return 0;
            }
            public bool ConsumeMarks(Entity victim, int required, float now, string ledger)
            {
                if (required <= 0 || MarkCount(victim, now, ledger) < required) return false;
                for (int i = 0; i < _marks.Length; i++)
                    if (_marks[i].Victim == victim && _marks[i].Ledger == ledger && _marks[i].Until > now && _marks[i].Creation == victim.creationTime)
                    {
                        var mark = _marks[i]; mark.Count -= required; _marks[i] = mark; return true;
                    }
                return false;
            }
            public Entity NearestMark(Vector3 point, float now, string ledger = null)
            {
                Entity result = null; float best = float.MaxValue;
                for (int i = 0; i < _marks.Length; i++)
                    if (_marks[i].Count > 0 && (ledger == null || _marks[i].Ledger == ledger) && _marks[i].Until > now && BossAlive(_marks[i].Victim) && _marks[i].Creation == _marks[i].Victim.creationTime)
                    {
                        float distance = (_marks[i].Victim.position - point).sqrMagnitude;
                        if (distance < best) { best = distance; result = _marks[i].Victim; }
                    }
                return result;
            }
            public void Clear() { _progress.Clear(); Array.Clear(_marks, 0, _marks.Length); Array.Clear(_seen, 0, _seen.Length); Array.Clear(_next, 0, _next.Length); }
        }

        private bool BossEnsure(HeroRuntime rt)
        {
            if (!NetworkServer.active || rt == null || !BossAlive(rt.Hero)) { if (rt != null && rt.Boss.Build != null) ClearBossEffects(rt); return false; }
            var state = rt.Boss;
            var build = rt.Powers.Build;
            var room = NetworkedManagerBase<ZoneManager>.softInstance?.currentRoom;
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            bool changed = state.Build != build || state.Epoch != rt.ShieldEquipmentEpoch || state.Room != room || state.Run != run;
            for (int i = 0; i < LinkSkills.Length; i++)
                if (state.Memories[i] != rt.Hero.Skill?.GetSkill(LinkSkills[i])) changed = true;
            int essenceIndex = 0;
            if (rt.Hero.Skill != null)
                foreach (var slot in rt.Hero.Skill.gems)
                {
                    if (essenceIndex >= state.Essences.Count || !ReferenceEquals(state.Essences[essenceIndex], slot.Value)) changed = true;
                    essenceIndex++;
                }
            if (essenceIndex != state.Essences.Count) changed = true;
            if (changed)
            {
                if (state.Build != null) ClearBossEffects(rt);
                state.Build = build; state.Epoch = rt.ShieldEquipmentEpoch; state.Room = room; state.Run = run;
                for (int i = 0; i < LinkSkills.Length; i++) state.Memories[i] = rt.Hero.Skill?.GetSkill(LinkSkills[i]);
                state.Essences.Clear();
                if (rt.Hero.Skill != null)
                    foreach (var slot in rt.Hero.Skill.gems) state.Essences.Add(slot.Value);
                if (build != null)
                    foreach (var entry in build.BossMoves)
                        if (BossProfiles.TryGetMove(entry.ProfileId, out var profile))
                            for (int i = 0; i < profile.Actions.Count; i++) state.ActionKeys[profile.Actions[i]] = profile.Id + "." + i;
            }
            return build != null && (build.BossMoves.Count != 0 || build.BossRewards.Count != 0);
        }

        private void TickBossEffects(HeroRuntime rt, float now)
        {
            if (!BossEnsure(rt)) return;
            TickBossNativeAdapters(rt);
            rt.Boss.Fields.Tick(this, rt, now);
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

        private void ClearBossEffects(HeroRuntime rt)
        {
            ClearBossNativeAdapters(rt);
            var s = rt.Boss;
            s.Fields.Clear(); s.Projectiles.Clear(); s.Deployables.Clear(); s.Defense.Clear(rt); s.EnemyMovement.Clear(); s.Ledger.Clear(); s.Ready.Clear();
            s.Build = null; s.ArrivalUntil = 0; s.HysteriaState = 0; s.Mode = 0; s.ModeUntil = 0;
            s.PreviousClawLeft = false;
            s.ClawPairs.Clear();
            s.ActionKeys.Clear();
            s.Essences.Clear();
            Array.Clear(s.Memories, 0, s.Memories.Length);
            ClearBossVisuals(rt);
        }

        private int BossRewardStage(HeroRuntime rt, string profileId)
        {
            if (rt == null || !Alive(rt.Hero) || rt.Hero.isKnockedOut || !BossProfiles.TryGetReward(profileId, out var profile)) return 0;
            bool equipped = FindMemory(rt.Hero, profile.Requires) != null;
            if (!equipped && rt.Hero.Skill != null)
                foreach (var slot in rt.Hero.Skill.gems)
                    if (slot.Value != null && slot.Value.GetType().Name == profile.Requires) { equipped = true; break; }
            if (!equipped) return 0;
            foreach (var entry in rt.Powers.Build.BossRewards)
                if (entry.ProfileId == profile.Id && entry.SetId == profile.SetId) return entry.Stage;
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
        private static bool BossGround(Vector3 origin, Vector3 desired, float range, out Vector3 point)
        {
            var delta = desired - origin; delta.y = 0;
            point = Dew.GetValidAgentDestination_LinearSweep(origin, origin + Vector3.ClampMagnitude(delta, range));
            return BossFinite(point) && Mathf.Abs(point.y - origin.y) <= 2f && !Physics.Linecast(origin + Vector3.up * 0.2f, point + Vector3.up * 0.2f, LayerMasks.Ground);
        }
        private static bool BossFinite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        private static float BossCoefficient(BossMoveEntry entry, BossMoveProfile profile, string channel)
        {
            foreach (var value in entry.Channels)
                if (value.ChannelId == channel)
                    foreach (var def in profile.Channels)
                        if (def.ChannelId == channel)
                            return Math.Min(value.ValueMilli, profile.SetId == BossProfiles.DemonSetId ? def.CapMilli : def.CapMilli * 3L) / 100000f;
            return 0;
        }
        private static float BossAmount(HeroRuntime rt, BossMoveEntry entry, BossMoveProfile profile, string channel)
        {
            foreach (var definition in profile.Channels)
                if (definition.ChannelId == channel && definition.Kind != BossCoefficientKind.Damage && definition.Kind != BossCoefficientKind.Heal && definition.Kind != BossCoefficientKind.Shield)
                    return BossCoefficient(entry, profile, channel) * 100f;
            return Math.Max(rt.Hero.Status.attackDamage, rt.Hero.Status.abilityPower) * BossCoefficient(entry, profile, channel);
        }
        private static bool BossMagic(HeroRuntime rt) => rt.Hero.Status.abilityPower > rt.Hero.Status.attackDamage;
        private void BossDamage(HeroRuntime rt, Entity victim, float amount, bool magic, BossElement element = BossElement.Neutral)
        {
            if (amount <= 0 || !BossAlive(victim) || victim == rt.Hero || victim.GetRelation(rt.Hero) != EntityRelation.Enemy) return;
            EnterGenerated(rt.Hero);
            try
            {
                var damage = magic ? rt.Hero.MagicDamage(amount, 0f) : rt.Hero.PhysicalDamage(amount, 0f);
                ElementalType? native = element == BossElement.Fire ? ElementalType.Fire : element == BossElement.Cold ? ElementalType.Cold
                    : element == BossElement.Light ? ElementalType.Light : element == BossElement.Dark ? ElementalType.Dark : (ElementalType?)null;
                damage.SetElemental(native).SetAmountModifiedBy(typeof(BossCombatState)).SetAmountModifiedBy(typeof(GimmickRuntime)).Dispatch(victim);
            }
            finally { ExitGenerated(rt.Hero); }
        }
    }
}
