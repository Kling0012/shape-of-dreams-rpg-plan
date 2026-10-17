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
                float now = Time.time;
                if (_skillResyncNext.TryGetValue(caller, out float next) && now < next) return;
                if (_skillResyncNext.Count > 32) _skillResyncNext.Clear();
                _skillResyncNext[caller] = now + SkillResyncMinInterval;
                int sent = ResendHeroSkills(hero);
                if (sent > 0)
                    Log.Info("Skill lock watchdog: resent " + sent + " skill state entr" + (sent == 1 ? "y" : "ies") + " for " + caller.playerName + ".");
            }
            catch (Exception ex)
            {
                WarnSkillResyncUnavailable(ex.Message);
            }
        }

        private static int ResendHeroSkills(Hero hero)
        {
            var manager = hero.Skill;
            if (manager == null) return 0;
            return Resend(manager.Q) + Resend(manager.W) + Resend(manager.E) + Resend(manager.R)
                + Resend(manager.Identity) + Resend(manager.Movement);
        }

        private static int Resend(SkillTrigger skill)
            => skill.IsNullOrInactive() || !skill.isServer ? 0 : NativeCooldownSafety.ResendAuthoritative(skill);
    }
}
