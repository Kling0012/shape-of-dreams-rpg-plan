using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // ホスト側の安全装置（docs/specs/guest-skill-lock.md）。
    // 参加者から DreamforgeSkillResyncMsg を受け取ったら、その人のキャラのスキルについて
    // 権威値（配列・ロック・詠唱・所有者・状態異常）を本体の RPC / 同期差分で送り直す。
    // 応じるのは Hello を交わした（同じ MOD を積んだ）参加者だけで、プレイヤーごとに頻度を限る。
    internal sealed partial class HostAuthority
    {
        private const float SkillResyncMinInterval = 8f;

        private Action<DreamforgeSkillResyncMsg, DewPlayer> _onSkillResync;
        private readonly Dictionary<DewPlayer, float> _skillResyncNext = new Dictionary<DewPlayer, float>();
        private readonly HashSet<DewPlayer> _skillResyncLogged = new HashSet<DewPlayer>();
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
            _skillResyncLogged.Clear();
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
                if (!NativeSkillLockRecovery.Enabled || _skillResyncUnavailable
                    || msg == null || msg.version != 1 || caller == null || !caller.isHumanPlayer) return;
                // Hello を交わしていない相手（旧い版・他MOD）はこの型を送らないが、念のため弾く。
                if (!_helloPeers.Contains(caller)) return;
                var hero = caller.hero;
                if (hero == null || !hero.isActive || !hero.isServer) return;
                if (msg.heroNetId != 0 && hero.netId != msg.heroNetId) return;
                float now = Time.unscaledTime;
                if (_skillResyncNext.TryGetValue(caller, out float next) && now < next) return;
                // Never clear another caller's deadline when the table is full.
                if (!_skillResyncNext.ContainsKey(caller) && _skillResyncNext.Count >= 32) return;
                _skillResyncNext[caller] = now + SkillResyncMinInterval;
                bool diagnose = _skillResyncLogged.Add(caller);
                NativeSkillLockRecovery.ResendHero(hero);
                ResendHeroSkills(hero, diagnose);
            }
            catch (Exception ex)
            {
                WarnSkillResyncUnavailable(ex.Message);
            }
        }

        private static void ResendHeroSkills(Hero hero, bool diagnose)
        {
            var manager = hero.Skill;
            if (manager == null) return;
            Resend(hero, manager.Q, 0, diagnose);
            Resend(hero, manager.W, 1, diagnose);
            Resend(hero, manager.E, 2, diagnose);
            Resend(hero, manager.R, 3, diagnose);
            Resend(hero, manager.Identity, 4, diagnose);
            Resend(hero, manager.Movement, 5, diagnose);
        }

        private static void Resend(Hero hero, SkillTrigger skill, int slot, bool diagnose)
        {
            if (!skill.IsNullOrInactive() && skill.isServer)
            {
                NativeSkillLockRecovery.ResendSkill(skill);
                NativeCooldownSafety.ResendAuthoritative(skill);
            }
            if (diagnose) NativeSkillLockRecovery.LogSnapshot(hero, skill, slot, "Skill lock host: ");
        }
    }
}
