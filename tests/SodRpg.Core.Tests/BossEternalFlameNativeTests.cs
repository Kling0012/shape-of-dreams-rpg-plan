using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace SodRpg.Core.Tests
{
    // Create() がプロセス共有の静的状態を書き換えるため、この Collection のテストは
    // 他のコレクションと並列に走らせない（SixPieceSetTests と同じやり方）。
    [CollectionDefinition("Boss EternalFlame native statics", DisableParallelization = true)]
    public class BossEternalFlameNativeStaticsCollection { }

    /// <summary>
    /// #1077: TickEternalFlame の毎フレーム再検査（親・キャスター・装備・所有者）の軽量化が
    /// 結果を変えないことの固定。装備が正しい呪いは stage==0 で延長分だけ復元され、stage&gt;0 で
    /// 延長未適用なら CaptureEternalFlameCurse による付け直しが走る。前提が崩れた呪いは
    /// EternalFlameCurseCurrent が必ず false にするため RestoreEternalFlameTimer＋ReleaseEternalFlameCurse
    /// が選ばれる。リンクされた本体の HostAuthority.BossEternalFlame.cs を実API相当の
    /// double（BossNativeRuntimeApi.cs）経由で駆動する。
    /// Create() がプロセス全体で共有する静的状態（NetworkServer.active・Time.time・
    /// HostAuthority.NativeInstance・EntityStatus.LiveStatusEffects・DewPhysics.Entities・
    /// ZoneManager/GameManager の softInstance）を書き換えるため、他のテストと並列に動かさない
    /// Collection（DisableParallelization）に入れる。あわせて IDisposable にし、Dispose で
    /// これらの静的状態を実行前の既定値へ戻す（#1077 レビュー F1。このテストより後に走る
    /// テストが NativeInstance==null・NetworkServer.active==false を前提に自分で初期化
    /// しない場合にも、このテストの実行有無で結果が変わらないようにする）。
    /// </summary>
    [Collection("Boss EternalFlame native statics")]
    public class BossEternalFlameNativeTests : IDisposable
    {
        private const string HeroKey = "Hero_A";
        private ZoneManager _previousZoneManager;
        private Room _previousRoom;
        private bool _zoneStateCaptured;

        public enum Premise { MoveGem, ChangeCaster, ChangeParent, ChangeGemOwner }

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            internal SkillTrigger Skill;
            internal Gem_U_EternalFlame Gem;
            internal Se_U_EternalFlame_Curse Curse;
            internal Entity Victim;
        }

        private static Build InfernusBuild(int pieces)
        {
            var profile = Profile.CreateNew(48);
            var order = Content.SlotOrder;
            int Rank(Slot slot) { for (int i = 0; i < order.Count; i++) if (order[i] == slot) return i; return order.Count; }
            var ids = Content.Uniques.Where(u => u.SetId == BossProfiles.InfernusSetId)
                .OrderBy(u => Rank(Content.GetBase(u.BaseId).Slot)).ToList();
            ulong seed = 42000;
            for (int i = 0; i < pieces; i++)
            {
                Content.TryGetUnique(ids[i].Id, out var unique);
                var relic = Loot.RollUnique(new Rng(seed++), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, HeroKey, relic.Uid);
            }
            var build = Build.Compute(profile, HeroKey, 0);
            Assert.Equal(pieces, build.Sets[BossProfiles.InfernusSetId]);
            return build;
        }

        // 報酬段階 n の Timer action（order 0）が足す秒数。プロファイルから計算し、値を固定しない。
        private static float ExtensionSeconds(int stage)
        {
            Assert.True(BossProfiles.TryGetReward(BossProfiles.InfernusRewardId, out var profile));
            var timer = profile.Stages[stage - 1].Actions[0];
            float extra = Math.Min(timer.ValueMilli, timer.CapMilli) / 1000f;
            Assert.True(extra > 0f, "the extension must be positive or these tests prove nothing");
            return extra;
        }

        // Infernus build（1部位=報酬段階0、4部位=段階2）のヒーロー。Identity の記憶と
        // その第1スロットの Gem_U_EternalFlame、対象 6s/残り 4s の呪いを組む。
        private Rig Create(int pieces)
        {
            if (!_zoneStateCaptured)
            {
                _previousZoneManager = NetworkedManagerBase<ZoneManager>.softInstance;
                _previousRoom = _previousZoneManager?.currentRoom;
                _zoneStateCaptured = true;
            }
            NetworkedManagerBase<ZoneManager>.softInstance ??= new ZoneManager();
            Mirror.NetworkServer.active = true;
            Time.time = 10f;
            NetworkedManagerBase<ZoneManager>.softInstance.currentRoom = new Room();
            NetworkedManagerBase<ZoneManager>.softInstance.isInAnyTransition = false;
            NetworkedManagerBase<GameManager>.softInstance.runId = "eternal-flame-run";
            EntityStatus.LiveStatusEffects.Clear();
            DewPhysics.Entities.Clear();
            NativeAttributedDamagePacket.Current = null;

            var build = InfernusBuild(pieces);
            var hero = new Hero { creationTime = 1f };
            hero.Skill.hero = hero;
            var skill = new SkillTrigger { owner = hero, creationTime = 2f };
            hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
            var location = new GemLocation { skill = HeroSkillLocation.Identity, index = 0 };
            var gem = new Gem_U_EternalFlame { owner = hero, skill = skill, location = location, creationTime = 3f };
            hero.Skill.EquipGem(location, gem);
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0f),
                AppliedBuild = new HostAuthority.GemBuildForTest { Build = build } };
            rt.Powers.SetBuild(build);
            host.Track(rt);
            HostAuthority.NativeInstance = host;
            Assert.True((bool)Invoke(host, "BossEnsure", rt));

            var victim = new Entity { creationTime = 4f };
            var curse = new Se_U_EternalFlame_Curse { victim = victim, parentActor = gem,
                info = new CastInfo(hero), creationTime = 5f, maxDuration = 6f, remainingDuration = 4f };
            return new Rig { Host = host, Rt = rt, Hero = hero, Skill = skill, Gem = gem, Curse = curse, Victim = victim };
        }

        // Create() が書き換えたプロセス共有の静的状態をテスト後に戻す（#1077 レビュー F1）。
        // xUnit はテストメソッドごとにインスタンスを作り直すため、Dispose は各テストの直後に走る。
        // これで、後に順番に走るテストが NativeInstance==null・NetworkServer.active==false を前提に
        // 自分で初期化しない場合にも、このテストの実行有無で結果が変わらない。
        // runId の "native-run" は AuthoredMechanismNativeApi.cs の GameManager の初期値。
        public void Dispose()
        {
            HostAuthority.NativeInstance = null;
            Mirror.NetworkServer.active = false;
            Time.time = 0f;
            if (_zoneStateCaptured)
            {
                if (_previousZoneManager != null)
                {
                    _previousZoneManager.currentRoom = _previousRoom;
                    _previousZoneManager.isInAnyTransition = false;
                }
                NetworkedManagerBase<ZoneManager>.softInstance = _previousZoneManager;
            }
            NetworkedManagerBase<GameManager>.softInstance.runId = "native-run";
            EntityStatus.LiveStatusEffects.Clear();
            DewPhysics.Entities.Clear();
            NativeAttributedDamagePacket.Current = null;
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(target, args);
        }

        private static int TrackedCurses(HostAuthority host)
            => ((IDictionary)typeof(HostAuthority).GetField("_eternalFlameCurses",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host)).Count;

        private static bool OwnerStateExists(HostAuthority host, HostAuthority.HeroRuntime rt)
            => ((IDictionary)typeof(HostAuthority).GetField("_eternalFlameOwners",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host)).Contains(rt);

        [Fact]
        public void Stage_upgrade_reapplies_the_extension_on_the_next_tick()
        {
            var rig = Create(1); // 報酬段階0: 捕捉だけが行われ、延長は入らない。
            rig.Host.CaptureEternalFlameCurse(rig.Curse);
            Assert.True(OwnerStateExists(rig.Host, rig.Rt));
            Assert.Equal(1, TrackedCurses(rig.Host));
            Assert.Equal(6f, rig.Curse.maxDuration);
            Assert.Equal(4f, rig.Curse.remainingDuration);
            float extra = ExtensionSeconds(2);

            rig.Rt.Powers.SetBuild(InfernusBuild(4));
            Invoke(rig.Host, "TickEternalFlame", rig.Rt, 10f);

            // 変更前と同じ分岐: 段階>0で延長未適用の呪いは CaptureEternalFlameCurse で付け直される。
            Assert.Equal(6f + extra, rig.Curse.maxDuration.Value, 3);
            Assert.Equal(4f + extra, rig.Curse.remainingDuration.Value, 3);
            Assert.True(rig.Curse.isActive);
            Assert.Equal(1, TrackedCurses(rig.Host));
        }

        [Fact]
        public void Stage_zero_tick_restores_only_the_extension_and_keeps_the_curse()
        {
            var rig = Create(1);
            rig.Host.CaptureEternalFlameCurse(rig.Curse); // 段階0のまま捕捉
            float extra = ExtensionSeconds(2);

            // 段階2のbuildで本体タイマー更新が来る（SetTimer パッチ相当）。延長が入るが
            // 所有者状態の段階はまだ0のままなので、段階の遷移は起きない。
            rig.Rt.Powers.SetBuild(InfernusBuild(4));
            rig.Host.SetEternalFlameNativeTimer(rig.Curse, 6f, 4f);
            Assert.Equal(6f + extra, rig.Curse.maxDuration.Value, 3);
            Assert.Equal(4f + extra, rig.Curse.remainingDuration.Value, 3);

            // 報酬段階0のbuildでティック → ループの stage==0 分岐が延長分だけ戻す。
            rig.Rt.Powers.SetBuild(InfernusBuild(1));
            Invoke(rig.Host, "TickEternalFlame", rig.Rt, 10f);

            Assert.Equal(6f, rig.Curse.maxDuration.Value, 3);
            Assert.Equal(4f, rig.Curse.remainingDuration.Value, 3);
            Assert.True(rig.Curse.isActive);
            Assert.Equal(1, TrackedCurses(rig.Host)); // 復元するだけで呪いは解除しない
        }

        [Fact]
        public void Stage_downgrade_after_extension_restores_the_native_timer_on_the_tick()
        {
            var rig = Create(4); // 報酬段階2: 捕捉と同時に延長が入る。
            rig.Host.CaptureEternalFlameCurse(rig.Curse);
            float extra = ExtensionSeconds(2);
            Assert.Equal(6f + extra, rig.Curse.maxDuration.Value, 3);
            Assert.Equal(4f + extra, rig.Curse.remainingDuration.Value, 3);

            rig.Rt.Powers.SetBuild(InfernusBuild(1));
            Invoke(rig.Host, "TickEternalFlame", rig.Rt, 10f);

            // 段階の下がりは延長分だけ戻し、呪い自体は残す。
            Assert.Equal(6f, rig.Curse.maxDuration.Value, 3);
            Assert.Equal(4f, rig.Curse.remainingDuration.Value, 3);
            Assert.True(rig.Curse.isActive);
            Assert.Equal(1, TrackedCurses(rig.Host));
        }

        [Theory]
        [InlineData(Premise.MoveGem)]
        [InlineData(Premise.ChangeCaster)]
        [InlineData(Premise.ChangeParent)]
        [InlineData(Premise.ChangeGemOwner)]
        public void Broken_premise_releases_the_curse_and_restores_the_timer(Premise premise)
        {
            var rig = Create(4);
            rig.Host.CaptureEternalFlameCurse(rig.Curse); // 段階2で延長済み
            float extra = ExtensionSeconds(2);
            Assert.Equal(6f + extra, rig.Curse.maxDuration.Value, 3);

            switch (premise)
            {
                case Premise.MoveGem: rig.Hero.Skill.gems.Remove(rig.Gem.location); break; // gem がスロットから外れる
                case Premise.ChangeCaster: rig.Curse.info.caster = new Hero(); break; // 詠唱者が別のヒーローになる
                case Premise.ChangeParent: rig.Curse.parentActor = new SkillTrigger(); break; // 親が gem でなくなる
                case Premise.ChangeGemOwner: rig.Gem.owner = new Hero(); break; // gem の所有者が変わる
            }

            Invoke(rig.Host, "TickEternalFlame", rig.Rt, 10f);

            // 毎フレームの再検査をやめた4条件（親・キャスター・gem.owner・装備）は、直前の
            // EternalFlameCurseCurrent が同じ意味で確認する。前提が崩れた呪いは
            // RestoreEternalFlameTimer＋ReleaseEternalFlameCurse で片付き、付け直しは起きない。
            Assert.Equal(6f, rig.Curse.maxDuration.Value, 3);
            Assert.Equal(4f, rig.Curse.remainingDuration.Value, 3);
            Assert.Equal(0, TrackedCurses(rig.Host));
        }
    }
}
