using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // ホスト側の安全装置（docs/specs/guest-skill-lock.md）。
    // 参加者から DreamforgeSkillResyncMsg を受け取ったら、その人のキャラのスキルについて
    // 権威値（チャージ・クールダウン・最低遅延）を本体の RPC 形式で送り直す。
    // 応じるのは Hello を交わした（同じ MOD を積んだ）参加者だけで、プレイヤーごとに頻度を限る。
    internal sealed partial class HostAuthority
    {
        private const float SkillResyncMinInterval = 8f;

        private Action<DreamforgeSkillResyncMsg, DewPlayer> _onSkillResync;
        private readonly Dictionary<DewPlayer, float> _skillResyncNext = new Dictionary<DewPlayer, float>();
        private bool _skillResyncUnavailable;

        private void RegisterSkillResync(Actor actor)
        {
            if (ReferenceEquals(actor, _skillResyncActor)) return;
            UnregisterSkillResync(_skillResyncActor);
            _skillResyncActor = actor;
            if (actor == null) return;
            try
            {
                if (_onSkillResync == null) _onSkillResync = OnSkillResync;
                actor.CustomRpc_RegisterServerMessageHandler<DreamforgeSkillResyncMsg>(nameof(DreamforgeSkillResyncMsg), _onSkillResync);
            }
            catch (Exception ex)
            {
                WarnSkillResyncUnavailable(ex.Message);
            }
        }

        private void UnregisterSkillResync(Actor actor)
        {
            if (actor != null && _onSkillResync != null)
                try { actor.CustomRpc_UnregisterServerMessageHandler<DreamforgeSkillResyncMsg>(_onSkillResync); } catch (Exception) { }
            _skillResyncActor = null;
            _skillResyncNext.Clear();
        }

        private Actor _skillResyncActor;

        private void WarnSkillResyncUnavailable(string reason)
        {
            if (_skillResyncUnavailable) return;
            _skillResyncUnavailable = true;
            Log.Warn("Skill lock watchdog disabled on host: " + reason);
        }

        private void OnSkillResync(DreamforgeSkillResyncMsg msg, DewPlayer caller)
        {
            try
            {
                if (msg == null || msg.version != 1 || caller == null || !caller.isHumanPlayer) return;
                // Hello を交わしていない相手（旧い版・他MOD）はこの型を送らないが、念のため弾く。
                if (!_helloPeers.Contains(caller)) return;
                var hero = caller.hero;
                if (hero == null || !hero.isActive || !hero.isServer) return;
                if (msg.heroNetId != 0 && hero.netId != msg.heroNetId) return;
                float now = Time.unscaledTime;
                if (_skillResyncNext.TryGetValue(caller, out float next) && now < next) return;
                if (_skillResyncNext.Count > 32) _skillResyncNext.Clear();
                _skillResyncNext[caller] = now + SkillResyncMinInterval;
                hero.Ability?.SetDirty();
                hero.Control?.SetDirty();
                hero.Status?.SetDirty();
                int sent = ResendHeroSkills(hero, msg.diagnosticSlots);
                if (sent > 0 && msg.diagnosticSlots != 0)
                    Log.Info("Skill lock watchdog: resent " + sent + " authoritative skill entries for hero=" + hero.netId
                        + NativeSkillLockState.DescribeChannels(hero)
                        + " abilityLocks=" + (hero.Ability == null ? 0UL : hero.Ability.Network_abilityCastLockBitmap)
                        + " stun=" + (hero.Status != null && hero.Status.hasStun) + ".");
            }
            catch (Exception ex)
            {
                WarnSkillResyncUnavailable(ex.Message);
            }
        }

        private static int ResendHeroSkills(Hero hero, int diagnosticSlots)
        {
            var manager = hero.Skill;
            if (manager == null) return 0;
            return Resend(hero, manager.Q, diagnosticSlots, 0) + Resend(hero, manager.W, diagnosticSlots, 1)
                + Resend(hero, manager.E, diagnosticSlots, 2) + Resend(hero, manager.R, diagnosticSlots, 3)
                + Resend(hero, manager.Identity, diagnosticSlots, 4) + Resend(hero, manager.Movement, diagnosticSlots, 5);
        }

        private static int Resend(Hero hero, SkillTrigger skill, int diagnosticSlots, int slot)
        {
            if (skill.IsNullOrInactive() || !skill.isServer) return 0;
            if ((diagnosticSlots & (1 << slot)) != 0)
                Log.Info("Skill lock watchdog: host snapshot hero=" + hero.netId + " slot=" + slot
                    + " skill=" + skill.netId + " casting=" + skill.Network_isCasting
                    + NativeSkillLockState.DescribeInstances(skill));
            NativeSkillLockState.Resend(hero, skill);
            return NativeCooldownSafety.ResendAuthoritative(skill);
        }
    }
}
