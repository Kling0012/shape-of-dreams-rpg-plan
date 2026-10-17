using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    // ゲスト側の安全装置（docs/specs/guest-skill-lock.md）。
    // チャージ・クールダウン・最低遅延はイベント性の RPC でしか届かないため、手元の値が
    // 暴走・凍結・取りこぼしすると、次のイベントが来るまで黒いアイコンのまま戻らない。
    // ここでは「手元では充填が終わっていて使えるはずなのに長く使えない」状態と
    // 「手元の値そのものが壊れている」状態を見つけて、ホストに権威値の再送を頼む。
    // 依頼は 1 秒に 1 回の走査で、10 秒続きそうなときだけ送る（応答は本体の RPC 形式そのまま）。
    internal sealed partial class ClientSession
    {
        private const float SkillLockScanSeconds = 1f;
        private const float SkillLockStuckSeconds = 10f;
        // 本来あり得ない大きさ。本体の最大クールダウン（錬成術 ~120 秒）より十分上。
        private const float SkillLockRunawaySeconds = 600f;

        private float _nextSkillLockScan;
        private float _skillLockSince = float.NegativeInfinity;
        private bool _skillLockLogged;

        private readonly SkillTrigger[] _skillLockScratch = new SkillTrigger[6];

        private void TickSkillLockResync()
        {
            // ホスト自身は権威側なので対象外。_clientRpcOn は Wire() が決める送信元。
            if (NetworkServer.active || !NetworkClient.active || _clientRpcOn == null || !InGame) return;
            float now = Time.unscaledTime;
            if (now < _nextSkillLockScan) return;
            _nextSkillLockScan = now + SkillLockScanSeconds;

            string stuck = FindStuckSkill();
            if (stuck == null)
            {
                _skillLockSince = float.NegativeInfinity;
                return;
            }
            if (_skillLockSince == float.NegativeInfinity)
            {
                _skillLockSince = now;
                return;
            }
            if (now - _skillLockSince < SkillLockStuckSeconds) return;

            // 送ったら窓を立て直す。次の依頼には最低でももう 10 秒の停滞が要る。
            _skillLockSince = float.NegativeInfinity;
            var hero = LocalHero;
            if (hero == null || !hero.isActive) return;
            _clientRpcOn.CustomRpc_SendMessageToServer(new DreamforgeSkillResyncMsg
            {
                version = 1,
                heroNetId = hero.netId,
            });
            if (!_skillLockLogged)
            {
                _skillLockLogged = true;
                Log.Info("Skill lock watchdog: requested a skill resync from the host (" + stuck + ").");
            }
        }

        /// <summary>停滞しているスキルの種別（無ければ null）。条件は docs/specs/guest-skill-lock.md の検知式。</summary>
        private string FindStuckSkill()
        {
            var hero = LocalHero;
            if (hero == null || !hero.isActive || hero.IsNullInactiveDeadOrKnockedOut()) return null;
            // 移動中の画面替わり・長い詠唱中は安定しない値しか取れないので見ない。
            var zone = NetworkedManagerBase<ZoneManager>.instance;
            if (zone != null && zone.isInAnyTransition) return null;
            if (hero.Control != null && hero.Control.ongoingChannels.Count > 0) return null;

            var manager = hero.Skill;
            if (manager == null) return null;
            _skillLockScratch[0] = manager.Q;
            _skillLockScratch[1] = manager.W;
            _skillLockScratch[2] = manager.E;
            _skillLockScratch[3] = manager.R;
            _skillLockScratch[4] = manager.Identity;
            _skillLockScratch[5] = manager.Movement;
            for (int s = 0; s < _skillLockScratch.Length; s++)
            {
                var skill = _skillLockScratch[s];
                if (skill.IsNullOrInactive() || skill.configs == null
                    || skill.currentUnscaledCooldownTimes == null || skill.currentCharges == null
                    || skill.currentMinimumDelays == null) continue;
                if (HasBrokenValue(skill)) return "runaway value";
                if (LooksUndercharged(skill)) return "ready but uncharged";
            }
            return null;
        }

        // 手元の配列そのものが壊れている（非有限・負・あり得ない大きさ）。
        private static bool HasBrokenValue(SkillTrigger skill)
        {
            var times = skill.currentUnscaledCooldownTimes;
            for (int i = 0; i < times.Length; i++)
            {
                float time = times[i];
                if (float.IsNaN(time) || float.IsInfinity(time) || time < -0.001f || time > SkillLockRunawaySeconds)
                    return true;
            }
            var delays = skill.currentMinimumDelays;
            for (int i = 0; i < delays.Length; i++)
            {
                float delay = delays[i];
                if (float.IsNaN(delay) || float.IsInfinity(delay) || delay < -0.001f || delay > SkillLockRunawaySeconds)
                    return true;
            }
            return false;
        }

        // クールダウンも最低遅延も終わっているのにチャージが 0 のまま（本体側は充填しているはず）。
        private static bool LooksUndercharged(SkillTrigger skill)
        {
            if (skill.Network_isCasting) return false;
            int index = skill.currentConfigIndex;
            var configs = skill.configs;
            var times = skill.currentUnscaledCooldownTimes;
            var delays = skill.currentMinimumDelays;
            var charges = skill.currentCharges;
            if (index < 0 || index >= configs.Length || index >= times.Length || index >= delays.Length
                || index >= charges.Length) return false;
            var config = configs[index];
            if (!config.isActive || config.maxCharges <= 0) return false;
            return charges[index] == 0 && times[index] <= 0.05f && delays[index] <= 0.001f;
        }
    }
}
