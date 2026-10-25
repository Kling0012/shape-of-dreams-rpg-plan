using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// #1205 原則 C-II: Obliviax の軽量化（EquippedMask キャッシュ）が結果を変えないことの検証。
// 状態が覚えた mask が従来の毎ティック9回の BossFind の結果と一致すること、装備エポックと
// Build の参照が一致する間はティックごとの再走査をしないこと（テスト用の BossMoves 書き換えを
// 拾わないことで観測）、装備エポックが進んだか Build がエポックを進めずに差し替わったティックで
// 消えた区分の予約（Fields）とクールダウン（Ready）を旧実装と同じように撤去すること、
// 移動観測の頭区分の在確認が装備直後〜次のティックの窓で現在の装備に従うことを、
// 実API相当の double（BossNativeRuntimeApi.cs）経由で確かめる。
namespace SodRpg.Core.Tests
{
    [Collection("DewPhysics")]
    public class BossObliviaxScanCacheTests
    {
        private static readonly string[] Profiles = (string[])typeof(HostAuthority)
            .GetField("ObliviaxProfiles", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            // TickObliviaxBoss 単体を呼ぶ: TickBossEffects 経由だと BossEnsure が先に走り、装備エポックや
            // Build の変化で状態全体が破棄されてしまい、ティック側の付け直しが観測できない
            // （BossLightScanCacheTests と同じ理屈）。
            internal void ObliviaxTick(float now)
            {
                Time.time = now;
                Invoke(Host, "TickObliviaxBoss", Rt, now);
            }
            internal void Observe(Vector3 from, bool eligible, float now)
            {
                Time.time = now;
                Rt.Hero.position = from + new Vector3(.25f, 0, 0);
                Invoke(Host, "ObserveObliviaxVoluntaryMovement", Rt.Hero, from, eligible);
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

        private static bool BossFind(HostAuthority host, HostAuthority.HeroRuntime rt, string id)
        {
            var method = typeof(HostAuthority).GetMethod("BossFind", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            var args = new object[] { rt, id, null, null };
            return (bool)method.Invoke(host, args);
        }

        private static object TrackedState(Rig rig)
        {
            var field = typeof(HostAuthority).GetField("_obliviaxCombat", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var dict = (System.Collections.IDictionary)field.GetValue(rig.Host);
            return dict.Contains(rig.Rt) ? dict[rig.Rt] : null;
        }

        private static bool ObliviaxTracked(Rig rig) => TrackedState(rig) != null;

        private static Build BuildWith(int seedBase, params string[] slots)
        {
            var profile = Profile.CreateNew(48);
            for (int i = 0; i < slots.Length; i++)
            {
                Assert.True(Content.TryGetUnique("unique.boss_obliviax." + slots[i], out var unique));
                var relic = Loot.RollUnique(new Rng((ulong)(seedBase + i)), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, "Hero_A", relic.Uid);
            }
            return Build.Compute(profile, "Hero_A", 0);
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
            NetworkedManagerBase<GameManager>.softInstance.runId = "obliviax-scan-run";
            string[] order = slots.Length == 0 ? new[] { "weapon", "armor", "charm", "head", "hands", "feet" } : slots;
            var build = BuildWith(12050, order.Take(pieces).ToArray());
            var hero = new Hero { creationTime = 1f };
            hero.Skill.hero = hero;
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            Assert.True((bool)Invoke(host, "BossEnsure", rt));
            return new Rig { Host = host, Rt = rt, Hero = hero };
        }

        // 撤去の観測用に、9区分すべてのクールダウン鍵を Ready に入れて鍵の一覧を返す
        // （BossLightScanCacheTests と同じやり方）。
        private static Dictionary<int, List<string>> FillReadyKeys(Rig rig)
        {
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
            return keys;
        }

        private static int Reserve(Rig rig, int index, float now)
        {
            Assert.True(BossProfiles.TryGetMove(Profiles[index], out var profile));
            Assert.True(rig.Rt.Boss.Fields.Reserve(rig.Host, rig.Rt, BossProfiles.ObliviaxSetId, Profiles[index],
                profile.Actions[0], Vector3.zero, Vector3.zero, 10f, true, now));
            return rig.Rt.Boss.Fields.Count(BossProfiles.ObliviaxSetId, Profiles[index]);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(6)]
        public void ObliviaxState_mask_matches_nine_BossFind_results_on_the_live_build(int pieces)
        {
            var rig = Create(pieces);
            var state = Invoke(rig.Host, "ObliviaxState", rig.Rt);
            Assert.NotNull(state);
            // キャッシュは装備エポックと Build の参照の両方を記録していること。
            Assert.Equal(rig.Rt.ShieldEquipmentEpoch, (long)Field(state, "EquipEpoch"));
            Assert.Same(rig.Rt.Powers.Build, Field(state, "EquipBuild"));
            int expected = 0;
            for (int i = 0; i < Profiles.Length; i++)
                if (BossFind(rig.Host, rig.Rt, Profiles[i])) expected |= 1 << i;
            // 旧実装の毎ティック9回の BossFind と同じ mask であること。
            Assert.Equal(expected, (int)Field(state, "EquippedMask"));
            // 6ピースでは stage2→3→6 も含めて9区分すべてが在ありになる。
            if (pieces == 6) Assert.Equal(511, (int)Field(state, "EquippedMask"));
        }

        [Fact]
        public void TickObliviaxBoss_trusts_cached_mask_while_epoch_and_build_are_unchanged()
        {
            var rig = Create(2);
            var state = Invoke(rig.Host, "ObliviaxState", rig.Rt);
            Assert.NotNull(state);
            // 2ピース（weapon+armor）の固定枠は stage2 まで。stage3 は3ピース、stage6 は6ピースから。
            int full = (1 << 0) | (1 << 1) | (1 << 6);
            Assert.Equal(full, (int)Field(state, "EquippedMask"));
            var keys = FillReadyKeys(rig);
            var moves = rig.Rt.Powers.Build.BossMoves;
            var weapon = moves.Single(m => m.ProfileId == Profiles[0]);
            // 本番では起こらない操作（BossMoves の生成後の直接変更）で再走査の有無を観測する:
            // エポックも参照も変わらないティックはキャッシュを信じ、旧実装が毎ティック拾ったはずの
            // 変化（weapon の不在）を拾わない。
            moves.Remove(weapon);
            rig.ObliviaxTick(11f);
            Assert.Equal(full, (int)Field(state, "EquippedMask"));
            foreach (var key in keys[0]) Assert.True(rig.Rt.Boss.Ready.ContainsKey(key));
            // エポックが進んだ最初の1回だけ付け直す: 消えた weapon は mask から落ち、鍵も消える。
            rig.Rt.ShieldEquipmentEpoch++;
            rig.ObliviaxTick(11.1f);
            int reduced = (1 << 1) | (1 << 6);
            Assert.Equal(reduced, (int)Field(state, "EquippedMask"));
            foreach (var key in keys[0]) Assert.False(rig.Rt.Boss.Ready.ContainsKey(key));
            for (int i = 1; i < Profiles.Length; i++)
                foreach (var key in keys[i]) Assert.True(rig.Rt.Boss.Ready.ContainsKey(key));
            // エポックが変わらない間は再びキャッシュを信じる: 戻した weapon は拾わない。
            moves.Add(weapon);
            rig.ObliviaxTick(11.2f);
            Assert.Equal(reduced, (int)Field(state, "EquippedMask"));
            // 次のエポック変化でもう一度だけ走査する（一度きりのフラグではない）。
            rig.Rt.ShieldEquipmentEpoch++;
            rig.ObliviaxTick(11.3f);
            Assert.Equal(full, (int)Field(state, "EquippedMask"));
            Assert.Equal(rig.Rt.ShieldEquipmentEpoch, (long)Field(state, "EquipEpoch"));
            Assert.Same(rig.Rt.Powers.Build, Field(state, "EquipBuild"));
        }

        [Fact]
        public void TickObliviaxBoss_cancels_reservations_and_ready_when_equipment_epoch_advances()
        {
            var rig = Create(2);
            var state = Invoke(rig.Host, "ObliviaxState", rig.Rt);
            Assert.NotNull(state);
            Assert.Equal((1 << 0) | (1 << 1) | (1 << 6), (int)Field(state, "EquippedMask"));
            var keys = FillReadyKeys(rig);
            // weapon・armor・stage2 の3区分に Fields の予約を入れておく。
            Assert.Equal(1, Reserve(rig, 0, 10f));
            Assert.Equal(1, Reserve(rig, 1, 10f));
            Assert.Equal(1, Reserve(rig, 6, 10f));
            // BossEnsure が済んだあとのティック内での装備変化: BossMoves から weapon が消え、
            // 装備エポックだけが進む。
            var moves = rig.Rt.Powers.Build.BossMoves;
            moves.Remove(moves.Single(m => m.ProfileId == Profiles[0]));
            rig.Rt.ShieldEquipmentEpoch++;
            rig.ObliviaxTick(11f);
            // 消えた区分（weapon）だけが予約取消しと Ready.Remove で撤去され、他の区分の予約と
            // クールダウンはそのまま（状態全体の破棄ではない）。旧実装と同じ撤去。
            Assert.Equal(0, rig.Rt.Boss.Fields.Count(BossProfiles.ObliviaxSetId, Profiles[0]));
            Assert.Equal(1, rig.Rt.Boss.Fields.Count(BossProfiles.ObliviaxSetId, Profiles[1]));
            Assert.Equal(1, rig.Rt.Boss.Fields.Count(BossProfiles.ObliviaxSetId, Profiles[6]));
            foreach (var key in keys[0]) Assert.False(rig.Rt.Boss.Ready.ContainsKey(key));
            for (int i = 1; i < Profiles.Length; i++)
                foreach (var key in keys[i]) Assert.True(rig.Rt.Boss.Ready.ContainsKey(key));
            Assert.Equal((1 << 1) | (1 << 6), (int)Field(state, "EquippedMask"));
            Assert.True(ObliviaxTracked(rig));
        }

        [Fact]
        public void TickObliviaxBoss_rescans_when_build_is_replaced_without_epoch_advance()
        {
            var rig = Create(2);
            var state = Invoke(rig.Host, "ObliviaxState", rig.Rt);
            Assert.NotNull(state);
            Assert.Equal((1 << 0) | (1 << 1) | (1 << 6), (int)Field(state, "EquippedMask"));
            var keys = FillReadyKeys(rig);
            Assert.Equal(1, Reserve(rig, 0, 10f));
            Assert.Equal(1, Reserve(rig, 1, 10f));
            // 本番の装備変化と同じ形の Build の差し替え（エポックは進まない窓）。ふつうは次の
            // ティック先頭の BossEnsure が状態を捨てるが、ティック側も参照比較で検出して
            // 付け直すので、呼び出し順に依存せず旧実装と同じ撤去になる。
            var replacement = BuildWith(12600, "armor", "hands");
            rig.Rt.Powers.SetBuild(replacement);
            rig.ObliviaxTick(11f);
            // 新しい Build（armor+hands+stage2）に合わせた mask になり、消えた weapon の予約と
            // クールダウンだけが撤去される。
            Assert.Equal((1 << 1) | (1 << 4) | (1 << 6), (int)Field(state, "EquippedMask"));
            Assert.Equal(0, rig.Rt.Boss.Fields.Count(BossProfiles.ObliviaxSetId, Profiles[0]));
            Assert.Equal(1, rig.Rt.Boss.Fields.Count(BossProfiles.ObliviaxSetId, Profiles[1]));
            foreach (var key in keys[0]) Assert.False(rig.Rt.Boss.Ready.ContainsKey(key));
            for (int i = 1; i < Profiles.Length; i++)
                foreach (var key in keys[i]) Assert.True(rig.Rt.Boss.Ready.ContainsKey(key));
            Assert.Equal(rig.Rt.ShieldEquipmentEpoch, (long)Field(state, "EquipEpoch"));
            Assert.Same(replacement, Field(state, "EquipBuild"));
            Assert.True(ObliviaxTracked(rig));
            // 差し替え後のティックはエポックも参照も一致するので、再びキャッシュを信じる。
            rig.ObliviaxTick(11.1f);
            Assert.Equal((1 << 1) | (1 << 4) | (1 << 6), (int)Field(state, "EquippedMask"));
        }

        [Fact]
        public void ObserveObliviaxVoluntaryMovement_uses_cached_head_bit_while_epoch_and_build_are_unchanged()
        {
            var rig = Create(2, "head", "hands");
            // 初回の観測で BossFind（頭あり）を通り、状態が作られる。
            rig.Observe(Vector3.zero, false, 10f);
            var state = TrackedState(rig);
            Assert.NotNull(state);
            Assert.Equal((1 << 3) | (1 << 4) | (1 << 6), (int)Field(state, "EquippedMask"));
            // 観測が進む: 0.25m の自発移動ごとに QuietDistance が伸びる。
            rig.Observe(Vector3.zero, true, 10.05f);
            rig.Observe(new Vector3(.25f, 0, 0), true, 10.1f);
            Assert.Equal(.5f, (float)Field(state, "QuietDistance"), 5);
            // 本番では起こらない操作（BossMoves の生成後の直接変更）で在確認の有無を観測する:
            // エポックも参照も変わらない間はキャッシュ（bit 8）を信じ、頭区分を外しても
            // 旧実装の毎フレームの BossFind と違って観測をやめない。
            var moves = rig.Rt.Powers.Build.BossMoves;
            moves.Remove(moves.Single(m => m.ProfileId == Profiles[3]));
            rig.Observe(new Vector3(.5f, 0, 0), true, 10.15f);
            Assert.Equal(.75f, (float)Field(state, "QuietDistance"), 5);
        }

        [Fact]
        public void ObserveObliviaxVoluntaryMovement_checks_live_build_when_build_is_replaced_without_epoch()
        {
            var rig = Create(2, "head", "hands");
            rig.Observe(Vector3.zero, false, 10f);
            var state = TrackedState(rig);
            Assert.NotNull(state);
            rig.Observe(Vector3.zero, true, 10.05f);
            rig.Observe(new Vector3(.25f, 0, 0), true, 10.1f);
            Assert.Equal(.5f, (float)Field(state, "QuietDistance"), 5);
            // 本番の装備変化と同じ形の Build の差し替え（エポックは進まない窓）。新しい Build には
            // 頭がないので、旧実装と同じく現在の装備で頭の有無を確かめて観測をやめる。
            // 状態の破棄は次のティックの BossEnsure が担うまでの窓の挙動である。
            rig.Rt.Powers.SetBuild(BuildWith(12600, "armor", "hands"));
            rig.Observe(new Vector3(.5f, 0, 0), true, 10.15f);
            Assert.Equal(.5f, (float)Field(state, "QuietDistance"), 5);
            // マスクの書き直しと区分の取消しは TickObliviaxBoss の付け直しに任せるので、
            // 状態の mask はこの窓では書き換わらない。
            Assert.Equal((1 << 3) | (1 << 4) | (1 << 6), (int)Field(state, "EquippedMask"));
        }
    }
}
