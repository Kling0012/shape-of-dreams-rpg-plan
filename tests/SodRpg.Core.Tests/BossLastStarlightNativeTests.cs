using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #109: LastStarlight の捕捉は正のエレボス報酬段階と正規装着を必須とし、
    /// 想定外の3回目の待機ではアダプタだけを外して本体列挙子と完了処理をそのまま通す。
    /// リンクされた本体の HostAuthority.BossLastStarlight.cs を直接駆動する。
    /// </summary>
    public class BossLastStarlightNativeTests
    {
        private const string Hero = "Hero_A";
        private static int _completions;
        private static SI.WaitForSeconds _thirdWait;
        private static readonly object NativeTail = new object();

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Ai_Gem_U_LastStarlight Instance;
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(target, args);
        }

        // A registered living hero with a properly equipped LastStarlight gem. The Erebos reward
        // stage comes from the equipped set pieces; another set's pieces keep the boss runtime
        // alive for the stage-zero case.
        private static Rig Create(int erebosPieces, int otherSetPieces)
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "starlight-run";
            Log.Warnings.Clear();
            _completions = 0;
            var profile = Profile.CreateNew(48);
            ulong seed = 42000;
            foreach (var setId in new[] { BossProfiles.SkollSetId, BossProfiles.ErebosSetId })
            {
                int pieces = setId == BossProfiles.ErebosSetId ? erebosPieces : otherSetPieces;
                var order = Content.SlotOrder;
                int Rank(Slot slot) { for (int i = 0; i < order.Count; i++) if (order[i] == slot) return i; return order.Count; }
                var ids = Content.Uniques.Where(u => u.SetId == setId)
                    .OrderBy(u => Rank(Content.GetBase(u.BaseId).Slot)).ToList();
                for (int i = 0; i < pieces; i++)
                {
                    Content.TryGetUnique(ids[i].Id, out var unique);
                    var relic = Loot.RollUnique(new Rng(seed++), unique, 5);
                    profile.Stash.Add(relic);
                    Rules.Equip(profile, Hero, relic.Uid);
                }
            }
            var build = Build.Compute(profile, Hero, 0);
            var hero = new Hero { creationTime = 1f };
            hero.Skill.hero = hero;
            var skill = new SkillTrigger { owner = hero };
            hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
            var location = new GemLocation { skill = HeroSkillLocation.Identity, index = 0 };
            var gem = new Gem_U_LastStarlight { owner = hero, skill = skill, location = location };
            hero.Skill.EquipGem(location, gem);
            var instance = new Ai_Gem_U_LastStarlight { parentActor = gem, info = new CastInfo(hero), creationTime = 1f };
            instance.gem = gem;
            instance.delay = 1f;
            instance.duration = 2f;
            instance.attractionRadius = 10f;
            instance.tickDamageRadius = 6f;
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            Assert.True((bool)Invoke(host, "BossEnsure", rt));
            return new Rig { Host = host, Rt = rt, Instance = instance };
        }

        private static IEnumerator TwoWaitSequence()
        {
            yield return new SI.WaitForSeconds();
            yield return new SI.WaitForSeconds();
            _completions++;
        }

        private static IEnumerator ThreeWaitSequence()
        {
            yield return new SI.WaitForSeconds();
            yield return new SI.WaitForSeconds();
            _thirdWait = new SI.WaitForSeconds();
            yield return _thirdWait;
            yield return NativeTail;
            _completions++;
        }

        private static int TrackedLastStarlights(HostAuthority host)
            => ((IDictionary)typeof(HostAuthority).GetField("_lastStarlights",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host)).Count;

        [Theory]
        [InlineData(0, 2, false)] // ordinary LastStarlight without an Erebos reward stage
        [InlineData(1, 2, false)] // one Erebos piece still has no reward stage
        [InlineData(2, 0, true)]  // stage 1: the existing integration captures
        [InlineData(6, 0, true)]  // stage 3
        public void Capture_requires_a_positive_Erebos_reward_stage(int erebosPieces, int otherSetPieces, bool captured)
        {
            var rig = Create(erebosPieces, otherSetPieces);

            Assert.Equal(captured, rig.Host.CaptureLastStarlight(rig.Instance) != null);
            Assert.Equal(captured ? 1 : 0, TrackedLastStarlights(rig.Host));
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Unexpected_third_native_wait_detaches_the_adapter_and_finishes_the_native_sequence()
        {
            var rig = Create(6, 0);
            var capture = rig.Host.CaptureLastStarlight(rig.Instance);
            Assert.NotNull(capture);
            var wrapper = (IEnumerator)rig.Host.WrapLastStarlight(ThreeWaitSequence(), capture);

            // The first two waits stay adapted by the wrapper.
            Assert.True(wrapper.MoveNext());
            Assert.IsNotType<SI.WaitForSeconds>(wrapper.Current);
            Time.time += 1f;
            Assert.True(wrapper.MoveNext());
            Assert.IsNotType<SI.WaitForSeconds>(wrapper.Current);
            // The unexpected third wait is the native's own object, passed through unchanged,
            // and the adapter releases its deltas and its registration instead of throwing.
            Time.time += 1f;
            Assert.True(wrapper.MoveNext());
            Assert.Same(_thirdWait, wrapper.Current);
            Assert.Contains(Log.Warnings, message => message.Contains("LastStarlight wait adaptation disabled"));
            Assert.Equal(1f, rig.Instance.delay, 3);
            Assert.Equal(2f, rig.Instance.duration, 3);
            Assert.Equal(10f, rig.Instance.attractionRadius, 3);
            Assert.Equal(6f, rig.Instance.tickDamageRadius, 3);
            Assert.Equal(0, TrackedLastStarlights(rig.Host));
            // Every remaining native yield and the final completion run exactly as the native emits them.
            Assert.True(wrapper.MoveNext());
            Assert.Same(NativeTail, wrapper.Current);
            Assert.False(wrapper.MoveNext());
            Assert.Equal(1, _completions);
        }

        [Fact]
        public void Two_wait_native_sequence_stays_adapted_and_restores_native_values_on_completion()
        {
            var rig = Create(6, 0);
            var capture = rig.Host.CaptureLastStarlight(rig.Instance);
            Assert.NotNull(capture);
            // The existing stage effects: shorter delay, longer duration, wider radii (stage 3).
            Assert.Equal(0.8f, rig.Instance.delay, 3);
            Assert.Equal(3f, rig.Instance.duration, 3);
            Assert.Equal(11f, rig.Instance.attractionRadius, 3);
            Assert.Equal(6.5f, rig.Instance.tickDamageRadius, 3);
            var wrapper = (IEnumerator)rig.Host.WrapLastStarlight(TwoWaitSequence(), capture);
            Assert.True(wrapper.MoveNext());
            Assert.IsNotType<SI.WaitForSeconds>(wrapper.Current);
            Time.time += 1f;
            Assert.True(wrapper.MoveNext());
            Assert.IsNotType<SI.WaitForSeconds>(wrapper.Current);
            Assert.Equal(1, TrackedLastStarlights(rig.Host));
            // Native completion releases the capture and restores the native values.
            Time.time += 1f;
            Assert.False(wrapper.MoveNext());
            Assert.Equal(1, _completions);
            Assert.Equal(0, TrackedLastStarlights(rig.Host));
            Assert.Equal(1f, rig.Instance.delay, 3);
            Assert.Equal(2f, rig.Instance.duration, 3);
            Assert.Equal(10f, rig.Instance.attractionRadius, 3);
            Assert.Equal(6f, rig.Instance.tickDamageRadius, 3);
            Assert.DoesNotContain(Log.Warnings, message => message.Contains("LastStarlight"));
        }
    }
}
