using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

// #1115（perf-loop/20261024-1047）: InkNearestAlly に足した位置だけの事前確認が、
// 元の物理クエリ経路の結果を変えないことを固定するテスト。
// (1) allHeroes に候補がいないフレーム（自分だけ・範囲外の味方だけ・半径内の敵中立だけ・
//     半径内の死亡・気絶した味方だけ）では null を返し、物理クエリを発火しない。
//     発火の有無は、物理クエリにだけ見える有効な味方（canary）を半径内に置いて確かめる。
//     事前確認なしの経路なら canary が返る世界で null が返れば、クエリが省かれた証明になる。
// (2) 候補がいるフレームでは、元の経路（事前確認なし）と同じ最も近い英雄を返す。
//     最近傍は中心からではなく自分の位置からの距離で選ぶので、中心と自分を離した配置と、
//     同距離の候補を netId の昇順で選ぶ tie-break の両方で同じ結果になることを確かめる。
// (3) 候補の有無は allHeroes（本体が OnCreate 直後に全英雄を登録する一覧）を前提とし、
//     候補がいても物理クエリが返さなければ null になる。判定をすり抜けた結果として
//     allHeroes から直接英雄を選ぶような変更が入れば、このテストは落ちる。
// 新旧両経路の比較は、_am を一時的に外して事前確認なしで同じ式を通すことで行う
// （InkBeamHitVerdictReuseTests と同じ、同じ実行バイナリ内での比較手法）。
namespace SodRpg.Core.Tests
{
    // DewPhysics.Entities（物理クエリのテスト double が返す、プロセス全体で共有する一覧）を
    // Clear/Add するテストクラスをまとめる Collection（#1115 レビュー F1）。この一覧を書き換える
    // クラス同士が並列に走ると互いの Clear・Add が混ざるため、DisableParallelization で直列にする。
    // 現在は AssemblyInfo の CollectionBehavior(DisableTestParallelization) でアセンブリ全体が
    // すでに直列だが、共有状態の直列性をこの Collection の設定だけでも担保しておく
    // （BossEternalFlameNativeTests と同じやり方。BossEternalFlameNativeTests 自身は
    // DewPhysics.Entities のほかに NativeInstance・LiveStatusEffects も書き換えるため、
    // 同じく直列を保証する専用の Collection のままにしている）。
    [CollectionDefinition("DewPhysics", DisableParallelization = true)]
    public class DewPhysicsCollection { }

    [Collection("DewPhysics")]
    public class InkNearestAllyPrecheckTests
    {
        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal SodRpg.Mod.Hero Self;
        }

        // 英雄を1人置く。registered=true は allHeroes への登録（本体の OnCreate 直後の登録に相当）、
        // physical=true は物理クエリの返り一覧への追加（コライダーが円に重なることに相当）。
        private static SodRpg.Mod.Hero Place(Rig rig, float x, float z, uint netId = 1,
            SodRpg.Mod.EntityRelation relation = SodRpg.Mod.EntityRelation.Ally, bool registered = true, bool physical = true)
        {
            var hero = new SodRpg.Mod.Hero { position = new Vector3(x, 0f, z), netId = netId, Relation = relation };
            if (registered) rig.Host.TestActorManager.allHeroes.Add(hero);
            if (physical) SodRpg.Mod.DewPhysics.Entities.Add(hero);
            return hero;
        }

        private static Rig Create(float selfX = 0f, float selfZ = 0f)
        {
            SodRpg.Mod.DewPhysics.Entities.Clear();
            var rig = new Rig { Host = new HostAuthority() };
            rig.Self = Place(rig, selfX, selfZ, 100);
            rig.Rt = new HostAuthority.HeroRuntime { Hero = rig.Self };
            return rig;
        }

        private static SodRpg.Mod.Hero Nearest(Rig rig, Vector3 center, float radius)
        {
            var info = typeof(HostAuthority).GetMethod("InkNearestAlly",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(info);
            return (SodRpg.Mod.Hero)info.Invoke(rig.Host, new object[] { rig.Rt, center, radius });
        }

        // 変更前の経路: _am を一時的に外して、事前確認なしでクエリだけを走らせる。
        private static SodRpg.Mod.Hero WithoutPrecheck(Rig rig, Vector3 center, float radius)
        {
            var field = typeof(HostAuthority).GetField("_am", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var saved = field.GetValue(rig.Host);
            field.SetValue(rig.Host, null);
            try { return Nearest(rig, center, radius); }
            finally { field.SetValue(rig.Host, saved); }
        }

        // 候補不在の世界で null が返ることと、クエリが省かれたこと（canary が拾える世界なので
        // クエリが走れば null にはならない）を、まとめて確かめる。
        private static void AssertQuerySkipped(Rig rig, SodRpg.Mod.Hero canary, string context)
        {
            var center = new Vector3(0f, 0f, 0f);
            Assert.True(ReferenceEquals(canary, WithoutPrecheck(rig, center, 3f)),
                context + ": the canary must be visible to the original query path");
            Assert.True(Nearest(rig, center, 3f) == null,
                context + ": no registered candidate must return null without the query");
        }

        [Fact]
        public void Frames_without_a_registered_candidate_return_null_without_the_query()
        {
            // 自分だけ。
            var selfOnly = Create();
            AssertQuerySkipped(selfOnly, Place(selfOnly, 1f, 0f, 90, registered: false), "self only");

            // 範囲外の味方だけ（半径3に対して距離10）。
            var farAlly = Create();
            Place(farAlly, 10f, 0f, 11);
            AssertQuerySkipped(farAlly, Place(farAlly, 1f, 0f, 91, registered: false), "out-of-range ally only");

            // 半径内でも敵・中立だけ（double では CheckEnemyOrNeutral を Relation=Enemy で表す）。
            var enemyWorld = Create();
            Place(enemyWorld, 1f, 0f, 12, SodRpg.Mod.EntityRelation.Enemy);
            AssertQuerySkipped(enemyWorld, Place(enemyWorld, -1f, 0f, 92, registered: false), "enemy in radius only");

            // 半径内でも死亡・気絶した味方だけ。
            var deadWorld = Create();
            var dead = Place(deadWorld, 1f, 0f, 13); dead.currentHealth = 0f;
            var knockedOut = Place(deadWorld, -1f, 0f, 14); knockedOut.isKnockedOut = true;
            AssertQuerySkipped(deadWorld, Place(deadWorld, 2f, 0f, 93, registered: false), "dead ally in radius only");
        }

        [Fact]
        public void Frames_with_a_registered_candidate_return_the_original_paths_nearest_hero()
        {
            var center = new Vector3(0f, 0f, 0f);
            // 自分を領域の中心から離す: 最近傍は中心ではなく自分からの距離で選ぶ。
            var rig = Create(10f, 0f);
            var nearestToSelf = Place(rig, 2f, 0f, 21);            // 中心には遠いが自分からの距離は 8
            Place(rig, 1f, 0f, 22);                                // 中心に近いが自分からの距離は 9
            Place(rig, -1f, 0f, 23, SodRpg.Mod.EntityRelation.Enemy); // 半径内の敵
            var dead = Place(rig, -2f, 0f, 24); dead.currentHealth = 0f; // 半径内の死亡した味方
            Place(rig, 50f, 0f, 25);                               // 範囲外の味方

            var modified = Nearest(rig, center, 3f);
            Assert.True(ReferenceEquals(nearestToSelf, modified),
                "the modified path must return the hero nearest to self");
            Assert.True(ReferenceEquals(WithoutPrecheck(rig, center, 3f), modified),
                "the modified path must match the original query path");
        }

        [Fact]
        public void Equidistant_candidates_keep_the_net_id_tie_break_of_the_original_path()
        {
            var center = new Vector3(0f, 0f, 0f);
            var rig = Create();
            Place(rig, 1f, 0f, 31);
            var lowerNetId = Place(rig, -1f, 0f, 30);

            var modified = Nearest(rig, center, 3f);
            Assert.True(ReferenceEquals(lowerNetId, modified),
                "equidistant candidates must resolve to the lower netId");
            Assert.True(ReferenceEquals(WithoutPrecheck(rig, center, 3f), modified),
                "the tie-break must match the original query path");
        }

        [Fact]
        public void A_candidate_the_query_does_not_return_stays_null_on_both_paths()
        {
            var center = new Vector3(0f, 0f, 0f);
            var rig = Create();
            // allHeroes には登録済みだが物理クエリが返さない味方: 事前確認は候補ありと進むが、
            // 結果はクエリの返りだけで決まる（クエリ経路が引き続き正であることの固定）。
            Place(rig, 1f, 0f, 41, physical: false);
            Assert.Null(Nearest(rig, center, 3f));
            Assert.Null(WithoutPrecheck(rig, center, 3f));
        }
    }
}
