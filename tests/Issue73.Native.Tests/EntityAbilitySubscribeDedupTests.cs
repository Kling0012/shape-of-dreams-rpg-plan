using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    using AbilityDict = Mirror.SyncDictionary<int, SyncedNetworkBehaviour>;

    /// <summary>
    /// （#1174）プール再利用で重複した EntityAbility の能力変化購読をまとめる
    /// NativeEntityAbilitySubscribeDedup（NativePerfPatches.cs、EntityAbility.OnStartClient の
    /// Prefix）の検証。境界は SkillLockResyncBoundary.cs に足した Mirror.SyncDictionary の
    /// Callback 欄と EntityAbility.OnStartClient の転写（再購読 1 行だけの本体）。それに対して
    /// Prefix を直接呼び、(1) 同じインスタンスの OnAbilityChanged の購読が K 個から 0 個に
    /// 外れ、本体の転写をもう1回動かすと 1 個に戻ること、(2) 別のインスタンスの同メソッドと
    /// 別のメソッドの購読は同じ並びのまま残ること、(3) 設定が無効のときは購読が全く変わらない
    /// こと、を確かめる。あわせて (4) Harmony で実際に当てた流れ（Prefix → 本体）で
    /// OnStartClient を何度呼んでも購読が 1 個に収まり、辞書の変化 1 つが OnAbilityChanged に
    /// 1 回だけ届くことを確かめる。
    /// </summary>
    public sealed class EntityAbilitySubscribeDedupTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public EntityAbilitySubscribeDedupTests()
        {
            NativePerfOptions.EntityAbilitySubscribeDedup = true;
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            NativePerfOptions.EntityAbilitySubscribeDedup = true;
            typeof(NativeEntityAbilitySubscribeDedup).GetField("_warned", Hidden)?.SetValue(null, false);
            Log.Warnings.Clear();
        }

        // Prepare が境界の形（OnStartClient・OnAbilityChanged・_synedAbilities・Callback）を
        // すべて解決できること。通らなければ以降の検証の前提が立たない。
        private static void PreparePatch()
        {
            Assert.True((bool)typeof(NativeEntityAbilitySubscribeDedup)
                .GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        // Prefix を直接呼ぶ（Harmony を通さず、除去の結果だけを確かめる）。
        private static void RunPrefix(EntityAbility ability)
            => typeof(NativeEntityAbilitySubscribeDedup)
                .GetMethod("Prefix", Hidden).Invoke(null, new object[] { ability });

        private static EntityAbility NewAbility() => new EntityAbility(new Hero());

        // 別インスタンスの OnAbilityChanged（private）の購読を作る。実機の本体は自分の辞書に
        // 自分のハンドラしか登録しないが、Prefix の照合（メソッドが同じでも対象が違えば
        // 外さない）を確かめるために、この辞書へ他人のハンドラを入れた状態を作れるようにする。
        private static Action<AbilityDict.Operation, int, SyncedNetworkBehaviour> HandlerOf(EntityAbility owner)
            => (Action<AbilityDict.Operation, int, SyncedNetworkBehaviour>)typeof(EntityAbility)
                .GetMethod("OnAbilityChanged", Hidden)
                .CreateDelegate(typeof(Action<AbilityDict.Operation, int, SyncedNetworkBehaviour>), owner);

        // Callback の購読を「対象がこのインスタンスでメソッドが OnAbilityChanged のもの」と
        // 「それ以外（対象とメソッドで識別）」に分けて数える。
        private static void Subscriptions(EntityAbility ability, out int own, out List<string> others)
        {
            own = 0;
            others = new List<string>();
            Delegate current = ability._synedAbilities.Callback;
            if (current == null) return;
            foreach (Delegate d in current.GetInvocationList())
            {
                if (ReferenceEquals(d.Target, ability) && d.Method.Name == "OnAbilityChanged") own++;
                else others.Add((d.Target == null ? "static" : "other") + "." + d.Method.Name);
            }
        }

        [Fact]
        public void Prefix_RemovesOwnDuplicates_ThenBodyLeavesOne_OthersUntouched()
        {
            PreparePatch();
            var a = NewAbility();
            var b = NewAbility();
            a.OnStartClient();
            a.OnStartClient();
            a.OnStartClient(); // プール再利用2回分の重複（同一ハンドラが3つ）
            a._synedAbilities.Callback += HandlerOf(b); // 別のインスタンスの同メソッド
            var foreign = 0;
            a._synedAbilities.Callback += (op, key, value) => foreign++; // 別のメソッド
            Subscriptions(a, out var ownBefore, out var othersBefore);
            Assert.Equal(3, ownBefore);
            Assert.Equal(2, othersBefore.Count);

            RunPrefix(a);

            Subscriptions(a, out var ownAfter, out var othersAfter);
            Assert.Equal(0, ownAfter); // 同一ハンドラの購読はすべて外れる
            Assert.Equal(othersBefore, othersAfter); // 他の購読者は同じ並びで残る
            Assert.Empty(Log.Warnings);

            // 本体（OnStartClient の転写）をもう1回: 未再利用のインスタンスと同じ1購読に戻る。
            a.OnStartClient();
            Subscriptions(a, out var ownFinal, out var othersFinal);
            Assert.Equal(1, ownFinal);
            Assert.Equal(othersBefore, othersFinal);
        }

        [Fact]
        public void Prefix_Disabled_ChangesNothing()
        {
            PreparePatch();
            var a = NewAbility();
            a.OnStartClient();
            a.OnStartClient(); // 重複1つ（無効のとき外れないまま残るべき状態）
            var foreign = 0;
            a._synedAbilities.Callback += (op, key, value) => foreign++;
            Subscriptions(a, out var ownBefore, out var othersBefore);
            Assert.Equal(2, ownBefore);

            NativePerfOptions.EntityAbilitySubscribeDedup = false;
            RunPrefix(a);

            Subscriptions(a, out var ownAfter, out var othersAfter);
            Assert.Equal(2, ownAfter); // 何も外れない
            Assert.Equal(othersBefore, othersAfter);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void PatchedStartClient_KeepsSingleSubscription_AndSingleDelivery()
        {
            PreparePatch();
            var harmony = new Harmony("issue73.entityabilitysubscribededup." + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.CreateClassProcessor(typeof(NativeEntityAbilitySubscribeDedup)).Patch();

                var a = NewAbility();
                a.OnStartClient();
                a.OnStartClient();
                a.OnStartClient(); // 再利用2回でも、毎回 Prefix が先に重複を外す
                Subscriptions(a, out var own, out var others);
                Assert.Equal(1, own);
                Assert.Empty(others);
                Assert.Empty(Log.Warnings);

                // 辞書の変化 1 つが OnAbilityChanged に 1 回だけ届く（重複配信の解消）。
                a._synedAbilities.Callback(AbilityDict.Operation.OP_ADD, 7, null);
                Assert.Single(a.AbilityChangedCalls);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }
    }
}
