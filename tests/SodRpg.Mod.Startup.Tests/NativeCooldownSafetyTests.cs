using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // 実 NativeCooldownSafety パッチ（4クラス）を実 Harmony デトゥアで本体の境界スタブに適用して確かめる。
    // 境界スタブは docs/reviews/cdr-lockout-20261016.md の逆コンパイル結果に基づき、実 DLL での動作確認ではない。
    public sealed class NativeCooldownSafetyTests : IDisposable
    {
        private readonly Harmony owner = new Harmony("cooldown-safety-tests." + Guid.NewGuid().ToString("N"));

        public NativeCooldownSafetyTests()
        {
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = false;
            NativeCooldownSafety.Current = null;
            Log.Warnings.Clear();
            // PatchEachClass と同じ経路で1クラスずつ適用する（Prepare が false のクラスは飛ばされる）。
            foreach (var patchClass in new[]
            {
                typeof(NativeCooldownReductionSafety), typeof(NativeCooldownTickSafety),
                typeof(NativeCooldownMultiplierSafety), typeof(NativeBaseCooldownMultiplierSafety)
            })
                owner.CreateClassProcessor(patchClass).Patch();
        }

        /// <summary>正の有限倍率（0.01 未満を含む）では本体の数式・RPC・チャージが一切変わらない。</summary>
        [Fact]
        public void PositiveFiniteMultipliersKeepTheNativeArithmeticUntouched()
        {
            Mirror.NetworkServer.active = true;
            foreach (float value in new[] { 1f, 0.5f, 3f, 0.005f })
            {
                var trigger = NewTrigger(value);
                float expected = Math.Max(10f - 3f / value, 0f);

                trigger.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);

                // 0.005 のような 0.01 未満の正の有限値も置換しない。
                Assert.Equal(value, trigger.UsedMultipliers.Single());
                Assert.Equal(expected, trigger.currentUnscaledCooldownTimes[0]);
                Assert.Equal(0, trigger.currentCharges[0]);
                // 本体ループ自身の RPC だけが出て、補正の追加 RPC は無い。
                Assert.Equal(new[] { "time 0 " + expected.ToString("R"), "charge 0 0" }, trigger.RpcLog);
            }
        }

        /// <summary>倍率 0 / NaN / ±Infinity は 0.01 に置換され、短縮後の残り時間と次の時間更新が有限に保たれる。</summary>
        [Fact]
        public void NonFiniteOrNonPositiveMultipliersAreReplacedDuringServerReduction()
        {
            Mirror.NetworkServer.active = true;
            foreach (float bad in new[] { 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var trigger = NewTrigger(bad);

                trigger.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);

                Assert.Equal(NativeCooldownSafety.InvalidMultiplierFallback, trigger.UsedMultipliers.Single());
                // 10 - 3/0.01 = -290 が本体の下限 0 にクランプされ、有限のままチャージ回復条件に到達できる。
                Assert.True(float.IsFinite(trigger.currentUnscaledCooldownTimes[0]));
                Assert.Equal(0f, trigger.currentUnscaledCooldownTimes[0]);
                Assert.Equal(new[] { "time 0 0", "charge 0 0" }, trigger.RpcLog);
            }

            // 修理したゼロは次の本体時間更新でも有限のまま進む（dt / 保護済み倍率）。
            var tick = NewTrigger(0f);
            tick.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);
            tick.LogicUpdate(0.1f);
            Assert.Equal(NativeCooldownSafety.InvalidMultiplierFallback, tick.UsedMultipliers.Last());
            Assert.True(float.IsFinite(tick.currentUnscaledCooldownTimes[0]));

            // サーバ上でも短縮・時間更新のスコープ外の倍率取得には介入しない。
            Assert.True(float.IsNaN(NewTrigger(float.NaN).GetCooldownTimeMultiplier(0)));
        }

        /// <summary>NaN・Infinity・負の残り時間は 0 に正規化され、チャージを維持したまま本体 RPC で再同期される。</summary>
        [Fact]
        public void BrokenRemainingTimesAreNormalizedToZeroAndResynchronized()
        {
            Mirror.NetworkServer.active = true;
            var trigger = NewTrigger(1f);
            trigger.configs = new[] { new TriggerConfig(), new TriggerConfig(), new TriggerConfig() };
            trigger.currentUnscaledCooldownTimes = new[] { float.NaN, -7f, float.PositiveInfinity };
            trigger.currentCharges = new[] { 0, 2, 0 };

            trigger.ApplyCooldownReductionWithMinimum_Imp(0f, 0f, false, false);

            Assert.Equal(new[] { 0f, 0f, 0f }, trigger.currentUnscaledCooldownTimes);
            // チャージは書き換えず、回復は次の本体 LogicUpdate に任せる。
            Assert.Equal(new[] { 0, 2, 0 }, trigger.currentCharges);
            Assert.Equal(new[]
            {
                // 本体ループ自身の送出（設定1は満チャージで対象外、NaN/Infinity をそのまま送る）。
                "time 0 NaN", "charge 0 0", "time 2 Infinity", "charge 2 0",
                // 補正後の再同期: 壊れた時間だけ 0 で送り直し、チャージは現在値をそのまま公開する。
                "time 0 0", "charge 0 0", "time 1 0", "charge 1 2", "time 2 0", "charge 2 0"
            }, trigger.RpcLog);
        }

        /// <summary>サーバでもクライアントでもないときは倍率・残り時間・RPC のどれにも介入しない。</summary>
        [Fact]
        public void WithoutTheServerTheSafetyNeverIntervenes()
        {
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = false;
            var trigger = NewTrigger(float.NaN);

            // 本体の [Server] メソッドは何もしない。
            trigger.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);
            Assert.Empty(trigger.UsedMultipliers);
            Assert.Equal(10f, trigger.currentUnscaledCooldownTimes[0]);
            Assert.Empty(trigger.RpcLog);

            // 倍率取得も補正しない。
            Assert.True(float.IsNaN(trigger.GetCooldownTimeMultiplier(0)));

            // 補正同期も動かない: NaN は NaN のまま残る。
            trigger.currentUnscaledCooldownTimes[0] = float.NaN;
            NativeCooldownSafety.Normalize(trigger);
            Assert.True(float.IsNaN(trigger.currentUnscaledCooldownTimes[0]));
            Assert.Empty(trigger.RpcLog);

            // クライアント側の時間更新も本体と同じ伝播（NaN は NaN のまま）。
            trigger.LogicUpdate(0.1f);
            Assert.True(float.IsNaN(trigger.currentUnscaledCooldownTimes[0]));
        }

        /// <summary>
        /// 純クライアント（ゲスト）では、退化倍率を窓の外でも実時間の 1 倍に置き換える
        /// （docs/specs/guest-skill-lock.md）。サーバ側の窓・代替値は従来のまま。
        /// </summary>
        [Fact]
        public void OnAPureClientDegenerateMultipliersFallBackToRealTimeEverywhere()
        {
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = true;
            var trigger = NewTrigger(float.NaN);

            // 窓の外の通常の倍率取得でも置き換わる（ゲストの読み出しは表示・予測だけのため）。
            Assert.Equal(NativeCooldownSafety.ClientMultiplierFallback, trigger.GetCooldownTimeMultiplier(0));

            // 負の倍率も置き換える: 0 から遠ざかる暴走の代わりに実時間で進む。
            trigger.multiplier = -1f;
            trigger.currentUnscaledCooldownTimes[0] = 10f;
            trigger.LogicUpdate(0.1f);
            Assert.Equal(10f - 0.1f, trigger.currentUnscaledCooldownTimes[0]);
            Assert.Equal(NativeCooldownSafety.ClientMultiplierFallback, trigger.UsedMultipliers.Single());

            // 正の有限倍率は従来どおり本体の値を通す。
            trigger.multiplier = 0.5f;
            Assert.Equal(0.5f, trigger.GetCooldownTimeMultiplier(0));

            // 補正同期（RPC 再送）は引き続きサーバ専用で、ゲストからは何も送らない。
            trigger.currentUnscaledCooldownTimes[0] = float.NaN;
            NativeCooldownSafety.Normalize(trigger);
            Assert.True(float.IsNaN(trigger.currentUnscaledCooldownTimes[0]));
            Assert.Empty(trigger.RpcLog);
        }

        /// <summary>宣言オーバーライドが本体から消えると、その対象だけが外れ、他の補正パッチは有効のまま動く。</summary>
        [Fact]
        public void AMissingDeclaredNativeTargetDropsOnlyThatHookAndKeepsTheOthers()
        {
            // この境界では AttackTrigger には宣言オーバーライドが無い。
            Assert.Null(AccessTools.DeclaredMethod(typeof(AttackTrigger), "GetCooldownTimeMultiplier", new[] { typeof(int) }));

            // 他3クラスと倍率クラスの SkillTrigger 分は適用済み（ctor で例外なく Patch が完了している）。
            Assert.Contains(owner.GetPatchedMethods(), m => m.DeclaringType == typeof(AbilityTrigger) && m.Name == "ApplyCooldownReductionWithMinimum_Imp");
            Assert.Contains(owner.GetPatchedMethods(), m => m.DeclaringType == typeof(AbilityTrigger) && m.Name == "ApplyCooldownReductionByRatio_Imp");
            Assert.Contains(owner.GetPatchedMethods(), m => m.DeclaringType == typeof(AbilityTrigger) && m.Name == "LogicUpdate");
            Assert.Contains(owner.GetPatchedMethods(), m => m.DeclaringType == typeof(AbilityTrigger) && m.Name == "GetCooldownTimeMultiplier");
            Assert.Contains(owner.GetPatchedMethods(), m => m.DeclaringType == typeof(SkillTrigger) && m.Name == "GetCooldownTimeMultiplier");

            Mirror.NetworkServer.active = true;
            // 宣言の無い AttackTrigger は基底メソッドの補正が引き継ぐ。
            var attack = new AttackTrigger { multiplier = 0f };
            attack.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);
            Assert.Equal(NativeCooldownSafety.InvalidMultiplierFallback, attack.UsedMultipliers.Single());
            Assert.Equal(0f, attack.currentUnscaledCooldownTimes[0]);

            // SkillTrigger は自分の宣言フックが最終積（基底 × sbCooldownMultiplier）を補正する。
            var skill = new SkillTrigger { multiplier = 1f, sbCooldownMultiplier = 0f };
            skill.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);
            Assert.Equal(NativeCooldownSafety.InvalidMultiplierFallback, skill.UsedMultipliers.Single());
            Assert.Equal(0f, skill.currentUnscaledCooldownTimes[0]);
        }

        private static AbilityTrigger NewTrigger(float multiplier) => new AbilityTrigger { multiplier = multiplier };

        public void Dispose()
        {
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = false;
            NativeCooldownSafety.Current = null;
            foreach (var target in owner.GetPatchedMethods().ToArray())
                owner.Unpatch(target, HarmonyPatchType.All, owner.Id);
        }
    }
}
