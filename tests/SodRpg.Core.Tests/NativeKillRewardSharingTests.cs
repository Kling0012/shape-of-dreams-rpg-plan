using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    using SodRpg.Mod;

    /// <summary>
    /// 撃破報酬の共有（HostAuthority.NativeKillRewards.cs）。本体の30秒アシストと通常撃破ゴールドには
    /// パッチしないこと、一時効果（B）の5種だけが同じ撃破で同じ人に1回出ること、捕食（A）と
    /// RunGrowth の撃破カウントが5秒アシストに出ないことを確かめる。
    /// </summary>
    public class NativeKillRewardSharingTests
    {
        private static HostAuthority NewHost(string runId)
        {
            var host = KillAssistSupportTestRuntime.NewHost(runId);
            // Active はホストかつ複数人だけ。
            DewPlayer.gamePlayers.Add(new DewPlayer());
            DewPlayer.gamePlayers.Add(new DewPlayer());
            return host;
        }

        private static bool InvokeBeforeTrackedKill(EventInfoKill kill, KillTracker tracker)
        {
            var method = typeof(NativeKillRewards).GetMethod("BeforeTrackedKill", BindingFlags.Static | BindingFlags.NonPublic);
            return (bool)method.Invoke(null, new object[] { kill, tracker.target, tracker._isStopped, tracker.trackingDict, tracker.gracePeriod, tracker._callback });
        }

        [Fact]
        public void Install_patches_only_the_six_temporary_reward_targets()
        {
            NewHost("nkr-install");
            var harmony = new Harmony("nkr-install");
            NativeKillRewards.Install(harmony);
            // 失敗時は fail-soft で Log に理由が残る（Native kill reward sharing disabled: ...）。
            Assert.True(NativeKillRewards.Active, string.Join(" | ", Log.Warnings));

            // 一時効果の配線だけ。ゴールド（宝珠・分配）と30秒アシスト（ClientHeroEvent_OnKillOrAssist）は対象外。
            Assert.Equal(6, harmony.Applied.Count);
            Assert.Equal(6, harmony.Applied.Select(pair => pair.Target).Distinct().Count());
            var targets = harmony.Applied.Select(pair => pair.Target.Name).ToArray();
            Assert.Contains("Invoke", targets);                              // SafeAction&lt;EventInfoKill&gt;（敵の死亡イベント）
            Assert.Contains("ReportKill", targets);                          // KillTracker（金属の本体追跡）
            Assert.Contains("ClearPooledEventsAndProcessors", targets);      // 敵プール解放まで受領記録を保持
            Assert.Contains("OnDisable", targets);
            Assert.Contains("ActorEventOnKill", targets);                    // 究極の撃破チャージ
            Assert.Contains("OnVictimDeath", targets);                       // Cetus星図の本体追跡
            Assert.DoesNotContain(targets, name => name.Contains("Gold", StringComparison.Ordinal)
                || name.Contains("KillOrAssist", StringComparison.Ordinal) || name.Contains("DropGold", StringComparison.Ordinal));

            // 共有リストは B の5種だけ。A（捕食など4種）は Find にも引っかからない。
            var effects = NativeKillRewards.KnownEffects;
            Assert.Equal(5, effects.Count);
            Assert.Contains(effects, effect => effect.Type == typeof(Gem_E_Metal));
            Assert.Contains(effects, effect => effect.Type == typeof(SkillTrigger));
            Assert.Contains(effects, effect => effect.Type == typeof(Se_Star_Aurena_F_CR_NoHealCooldownReduction));
            Assert.Contains(effects, effect => effect.Type == typeof(Se_Star_Lacerta_F_PS_CooldownReset));
            Assert.Contains(effects, effect => effect.Type == typeof(Se_Star_Cetus_F_BA_CdReductionOnKill));
            Assert.Null(NativeKillRewards.Find(typeof(Gem_E_Predation).GetMethod("KillCallback")));
            Assert.NotNull(NativeKillRewards.Find(typeof(Gem_E_Metal).GetMethod("KillCallback")));
            DewPlayer.gamePlayers.Clear(); // 他クラスの単独前提に戻す
        }

        [Fact]
        public void Temporary_rewards_fire_once_per_death_for_assists_and_never_for_predation()
        {
            var host = NewHost("nkr-once");
            var harmony = new Harmony("nkr-once");
            NativeKillRewards.Install(harmony);
            Assert.True(NativeKillRewards.Active);

            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var striker = KillAssistSupportTestRuntime.HeroWithOwner(2);
            KillAssistSupportTestRuntime.TrackRuntime(host, striker, ("growth.kill", RunGrowthTrigger.EnemyKill));
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);

            // 撃破者とアシスト者、両方に金属のジェム（B）。
            var killerMetal = new Gem_E_Metal { owner = killer };
            killerMetal._tracker = new KillTracker { target = killer, gracePeriod = 10f, _callback = killerMetal.KillCallback };
            killerMetal._tracker.trackingDict[enemy] = 100f; // 本体の金属追跡（猶予10秒）が敵を把握済み
            killer.Skill.gems[new GemLocation { skill = HeroSkillLocation.Q, index = 0 }] = killerMetal;
            var strikerMetal = new Gem_E_Metal { owner = striker };
            strikerMetal._tracker = new KillTracker { target = striker, gracePeriod = 10f, _callback = strikerMetal.KillCallback };
            striker.Skill.gems[new GemLocation { skill = HeroSkillLocation.Q, index = 0 }] = strikerMetal;
            // アシスト者の究極（撃破チャージ）と Lacerta 星図（CDリセット）。
            var ultimate = new SkillTrigger { owner = striker, parentActor = striker, type = SkillType.Ultimate };
            striker.ActorEvent_OnKill.Add(ultimate.ActorEventOnKill);
            var strikerSkill = new SkillTrigger { owner = striker, parentActor = striker, currentConfigUnscaledCooldownTime = 7 };
            striker.Status.statusEffects.Add(new Se_Star_Lacerta_F_PS_CooldownReset { skill = strikerSkill });
            // アシスト者の捕食（A: MODの共有に出ない）。
            var predation = new Gem_E_Predation { owner = striker };
            striker.Skill.gems[new GemLocation { skill = HeroSkillLocation.W, index = 0 }] = predation;

            UnityEngine.Time.time = 100;
            striker.InvokeOnDealDamage(new EventInfoDamage { actor = striker, victim = enemy, damage = new DamageData(40) });
            var kill = new EventInfoKill { actor = killer, victim = enemy };
            enemy.RaiseDeath(kill); // 凍結: 対象者・装備を本体リスナーより先に確定

            // 撃破者の本体コールバックは元の場で1回だけ。
            Assert.True(InvokeBeforeTrackedKill(kill, killerMetal._tracker));
            killerMetal._tracker.ReportKill(kill);
            Assert.Equal(1, killerMetal.Callbacks);
            Assert.False(InvokeBeforeTrackedKill(kill, killerMetal._tracker)); // 受領済みは本体側でも再実行しない

            // 5秒アシスト配信: まだ受けていない B だけを同じ撃破で1回。
            host.DispatchKillAssists(kill);
            Assert.Equal(1, strikerMetal.Callbacks);               // アシスト者の金属は1回
            Assert.Equal(1, killerMetal.Callbacks);                // 撃破者は増えない
            Assert.Equal(1, ultimate.NativeKillEventCalls);        // 究極の撃破チャージ1回
            Assert.Equal(striker, ultimate.LastNativeKillEvent.Value.actor);
            Assert.Equal(0, strikerSkill.currentConfigUnscaledCooldownTime); // CDリセット1回
            Assert.Equal(0, predation.Callbacks);                  // 捕食（A）は出ない
            Assert.Equal(0, KillAssistSupportTestRuntime.Stacks(striker, "growth.kill")); // RunGrowth も出ない

            // 同じ撃破の再配信では誰も受け取らない。
            strikerSkill.currentConfigUnscaledCooldownTime = 7;
            host.DispatchKillAssists(kill);
            Assert.Equal(1, strikerMetal.Callbacks);
            Assert.Equal(1, ultimate.NativeKillEventCalls);
            Assert.Equal(7, strikerSkill.currentConfigUnscaledCooldownTime);
            DewPlayer.gamePlayers.Clear(); // 他クラスの単独前提に戻す
        }

        [Fact]
        public void An_assist_reward_already_fired_natively_is_not_repeated_by_the_dispatch()
        {
            var host = NewHost("nkr-native-first");
            NativeKillRewards.Install(new Harmony("nkr-native-first"));
            Assert.True(NativeKillRewards.Active);

            var killer = KillAssistSupportTestRuntime.HeroWithOwner(1);
            var striker = KillAssistSupportTestRuntime.HeroWithOwner(2);
            KillAssistSupportTestRuntime.TrackRuntime(host, killer);
            KillAssistSupportTestRuntime.TrackRuntime(host, striker);
            var enemy = KillAssistSupportTestRuntime.Enemy();
            KillAssistSupportTestRuntime.TrackVictim(host, enemy);

            var strikerMetal = new Gem_E_Metal { owner = striker };
            strikerMetal._tracker = new KillTracker { target = striker, gracePeriod = 10f, _callback = strikerMetal.KillCallback };
            strikerMetal._tracker.trackingDict[enemy] = 100f; // 本体の金属追跡（猶予10秒）が敵を把握済み
            striker.Skill.gems[new GemLocation { skill = HeroSkillLocation.Q, index = 0 }] = strikerMetal;

            UnityEngine.Time.time = 100;
            striker.InvokeOnDealDamage(new EventInfoDamage { actor = striker, victim = enemy, damage = new DamageData(40) });
            var kill = new EventInfoKill { actor = killer, victim = enemy };
            enemy.RaiseDeath(kill);

            // 本体の金属追跡（猶予10秒）が先にアシスト者へ配った場合。
            Assert.True(InvokeBeforeTrackedKill(kill, strikerMetal._tracker));
            strikerMetal._tracker.ReportKill(kill);
            Assert.Equal(1, strikerMetal.Callbacks);

            host.DispatchKillAssists(kill);
            Assert.Equal(1, strikerMetal.Callbacks); // 5秒アシストからの同じ人への重複なし
            DewPlayer.gamePlayers.Clear(); // 他クラスの単独前提に戻す
        }
    }
}
