using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// #1075 原則 C-II: Light の軽量化（LightScan キャッシュ）が結果を変えないことの検証。
// キャッシュされた mask/Entries/Profiles が従来のイベントごとの BossFind 9 回の結果と一致すること、
// 装備エポックが変わった最初のティックだけ再走査して消えた区分を LightCancelProfile で撤去すること、
// 区分を1つも持たない rt では状態を作らないことを、実API相当の double（BossNativeRuntimeApi.cs）経由で確かめる。
namespace SodRpg.Core.Tests
{
    public class BossLightScanCacheTests
    {
        private static readonly string[] Profiles = (string[])typeof(HostAuthority)
            .GetField("LightProfiles", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            // TickLightBoss 単体を呼ぶ: TickBossEffects 経由だと BossEnsure が先に走り、装備エポックの
            // 変化で状態全体が破棄されてしまい、ティック側の再走査が観測できない（Seeker テストと同じ理屈）。
            internal void LightTick(float now)
            {
                Time.time = now;
                Invoke(Host, "TickLightBoss", Rt, now);
            }
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(target, args);
        }

        private static object Field(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(field);
            return field.GetValue(instance);
        }

        private static bool BossFind(HostAuthority host, HostAuthority.HeroRuntime rt, string id,
            out BossMoveEntry entry, out BossMoveProfile profile)
        {
            var method = typeof(HostAuthority).GetMethod("BossFind", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            var args = new object[] { rt, id, null, null };
            bool found = (bool)method.Invoke(host, args);
            entry = (BossMoveEntry)args[2];
            profile = (BossMoveProfile)args[3];
            return found;
        }

        private static bool LightTracked(Rig rig)
        {
            var field = typeof(HostAuthority).GetField("_lightCombat", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return ((System.Collections.IDictionary)field.GetValue(rig.Host)).Contains(rig.Rt);
        }

        private static Rig Create(int pieces, params string[] slots)
        {
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            DewPhysics.Entities.Clear();
            EntityStatus.LiveStatusEffects.Clear();
            NativeAttributedDamagePacket.Current = null;
            BossNativeCastScope.Current = null;
            MawBigChompNativeDelay.Current = default;
            LightWorldCrackerNativeTick.Current = default;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "light-scan-run";
            var profile = Profile.CreateNew(48);
            string[] order = slots.Length == 0 ? new[] { "armor", "charm", "head", "weapon", "hands", "feet" } : slots;
            for (int i = 0; i < pieces; i++)
            {
                Assert.True(Content.TryGetUnique("set.boss_light_elemental." + order[i], out var unique));
                var relic = Loot.RollUnique(new Rng((ulong)(7300 + i)), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, "Hero_A", relic.Uid);
            }
            var build = Build.Compute(profile, "Hero_A", 0);
            var hero = new Hero { creationTime = 1f };
            hero.Skill.hero = hero;
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            Assert.True((bool)Invoke(host, "BossEnsure", rt));
            return new Rig { Host = host, Rt = rt };
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(6)]
        public void LightScan_cache_matches_nine_BossFind_results_on_the_live_build(int pieces)
        {
            var rig = Create(pieces);
            var state = Invoke(rig.Host, "LightState", rig.Rt);
            Assert.NotNull(state);
            // 検証に使う2つのカウンタを別々に記録していること（Epoch=Boss.Epoch、EquipEpoch=装備エポック）。
            Assert.Equal(rig.Rt.Boss.Epoch, (long)Field(state, "Epoch"));
            Assert.Equal(rig.Rt.ShieldEquipmentEpoch, (long)Field(state, "EquipEpoch"));
            var mask = (int)Field(state, "Mask");
            var entries = (BossMoveEntry[])Field(state, "Entries");
            var profiles = (BossMoveProfile[])Field(state, "Profiles");
            int expected = 0;
            for (int i = 0; i < Profiles.Length; i++)
            {
                bool found = BossFind(rig.Host, rig.Rt, Profiles[i], out var entry, out var profile);
                if (found)
                {
                    expected |= 1 << i;
                    Assert.Same(entry, entries[i]);
                    Assert.Same(profile, profiles[i]);
                }
                else
                {
                    Assert.Null(entries[i]);
                    Assert.Null(profiles[i]);
                }
            }
            Assert.Equal(expected, mask);
            // 6ピースでは stage2→3→6 のチャージ連鎖も含めて9区分すべてがキャッシュに入っている。
            if (pieces == 6) Assert.Equal(511, mask);
        }

        [Fact]
        public void LightState_creates_no_state_when_no_division_exists()
        {
            var rig = Create(2, "weapon", "armor");
            // テスト用に Build 生成後の BossMoves を書き換え、9区分すべてを不在にする（本番では起こらない操作）。
            rig.Rt.Powers.Build.BossMoves.Clear();
            var state = Invoke(rig.Host, "LightState", rig.Rt);
            Assert.Null(state);
            Assert.False(LightTracked(rig));
        }

        [Fact]
        public void TickLightBoss_rescans_once_per_equipment_epoch_change_and_cancels_removed_divisions()
        {
            var rig = Create(2, "weapon", "armor");
            var state = Invoke(rig.Host, "LightState", rig.Rt);
            Assert.NotNull(state);
            // 2ピース（weapon+armor）の固定枠は stage2 まで。stage3 は3ピース、stage6 は6ピースから。
            Assert.Equal((1 << 0) | (1 << 1) | (1 << 6), (int)Field(state, "Mask"));
            // 撤去の観測用に、9区分すべてのクールダウン鍵を先に入れておく。
            var keys = new Dictionary<int, List<string>>();
            for (int i = 0; i < Profiles.Length; i++)
            {
                Assert.True(BossProfiles.TryGetMove(Profiles[i], out var profile));
                var list = new List<string>();
                for (int a = 0; a < profile.Actions.Count; a++)
                {
                    list.Add(profile.Actions[a].RuntimeKey);
                    rig.Rt.Boss.Ready[profile.Actions[a].RuntimeKey] = 20f;
                }
                keys[i] = list;
            }
            // BossEnsure が済んだあとのティック内での装備変化: BossMoves が置き換わり、装備エポックだけが
            // 進む（Boss.Epoch は次の BossEnsure まで古いまま）。
            var moves = rig.Rt.Powers.Build.BossMoves;
            var weapon = moves.Single(m => m.ProfileId == Profiles[0]);
            moves.Remove(weapon);
            rig.Rt.ShieldEquipmentEpoch++;
            rig.LightTick(11f);
            // 消えた区分（weapon）だけが LightCancelProfile で撤去され、クールダウンも消える。
            foreach (var key in keys[0]) Assert.False(rig.Rt.Boss.Ready.ContainsKey(key));
            // 残りの区分は状態もクールダウンもそのまま（状態全体の破棄ではない）。
            for (int i = 1; i < Profiles.Length; i++)
                foreach (var key in keys[i]) Assert.True(rig.Rt.Boss.Ready.ContainsKey(key));
            Assert.Equal((1 << 1) | (1 << 6), (int)Field(state, "Mask"));
            Assert.True(LightTracked(rig));
            // エポックが変わらない間はキャッシュを信じる: 戻した区分は再走査で拾わない。
            moves.Add(weapon);
            rig.LightTick(11.1f);
            Assert.Equal((1 << 1) | (1 << 6), (int)Field(state, "Mask"));
            // 次のエポック変化でもう一度だけ走査する（一度きりのフラグではない）。
            rig.Rt.ShieldEquipmentEpoch++;
            rig.LightTick(11.2f);
            Assert.Equal((1 << 0) | (1 << 1) | (1 << 6), (int)Field(state, "Mask"));
            Assert.Same(weapon, ((BossMoveEntry[])Field(state, "Entries"))[0]);
            // 記録している2つのカウンタ: EquipEpoch は直近の LightScan 時点の装備エポック、
            // Epoch は rt.Boss.Epoch の検証用スナップショットのまま。
            Assert.Equal(rig.Rt.ShieldEquipmentEpoch, (long)Field(state, "EquipEpoch"));
            Assert.Equal(rig.Rt.Boss.Epoch, (long)Field(state, "Epoch"));
        }
    }
}
