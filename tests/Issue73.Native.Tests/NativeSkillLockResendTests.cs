using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    // （#1123）スキル再同期の軽量化: NativeSkillLockRecovery.ResendSkill が全配列 RPC
    // （RpcSyncAfterFrameDelayStates_Imp）を送れたときは HostAuthority.SkillLock.Resend が
    // NativeCooldownSafety.ResendAuthoritative（要素ごとの同値再送）を省き、送れなかったとき
    // （配列長不整合・本体 RPC 参照なし・送出時例外・既存の失敗フラグ）は従来どおり呼ぶことを確かめる。
    // 境界（SkillLockResyncBoundary.cs）では RPC の送出が観測記録になるため、
    // 「全配列だけ送れた」「要素ごとに落ちた」両方の経路が RPC 記録で見える。
    public sealed class NativeSkillLockResendTests : IDisposable
    {
        private static readonly MethodInfo ResendMethod = AccessTools.Method(typeof(HostAuthority), "Resend",
            new[] { typeof(Hero), typeof(SkillTrigger), typeof(int), typeof(bool) });
        private static readonly MethodInfo ResendHeroSkillsMethod = AccessTools.Method(typeof(HostAuthority), "ResendHeroSkills",
            new[] { typeof(Hero), typeof(bool) });
        private static readonly string[] FailFlags =
        {
            "_locksFailed", "_abilitiesFailed", "_arraysFailed", "_configFailed",
            "_uiFailed", "_reservationFailed", "_diagnosticFailed", "_snapshotFailed",
        };

        private static readonly string[] FullPerIndexLog =
        {
            "time 0 5", "mindelay 0 0.5", "charge 0 1",
            "time 1 0", "mindelay 1 0", "charge 1 0",
        };
        private static readonly string[] LockOps =
        {
            "OP_SET 0 False", "OP_SET 1 True",
            "OP_SET 0 True", "OP_SET 1 False",
        };

        private readonly bool _server;

        public NativeSkillLockResendTests()
        {
            _server = NetworkServer.active;
            NetworkServer.active = true;
            NativeSkillLockRecovery.Enabled = true;
            foreach (string flag in FailFlags)
                AccessTools.Field(typeof(NativeSkillLockRecovery), flag).SetValue(null, false);
            Log.Warnings.Clear();
        }

        public void Dispose()
        {
            foreach (string flag in FailFlags)
                AccessTools.Field(typeof(NativeSkillLockRecovery), flag).SetValue(null, false);
            NetworkServer.active = _server;
            Log.Warnings.Clear();
        }

        // ArrayRpc は static readonly（型初期化子で解決する）なので、.NET の Reflection では
        // 型初期化後に書き換えられない。Harmony の FieldRef（IL を発行した直接書き込み）で
        // null に差し替え、必ず元に戻す。
        private static void WithArrayRpcNull(Action act)
        {
            ref var arrayRpc = ref AccessTools.StaticFieldRefAccess<Action<AbilityTrigger, float[], int[], float[]>>(
                typeof(NativeSkillLockRecovery), "ArrayRpc");
            var original = arrayRpc;
            arrayRpc = null;
            try { act(); }
            finally { arrayRpc = original; }
        }

        /// <summary>全配列 RPC が送れたとき、要素ごとの再送は呼ばれず、他の副作用（dirty・ロックの積み直し）は従来どおり残る。</summary>
        [Fact]
        public void FullArrayRpcSentSkipsThePerIndexResendsAndKeepsTheOtherSideEffects()
        {
            var hero = new Hero { isServer = true };
            var skill = NewSkill();
            FillValues(skill);
            SeedLocks(skill);
            hero.Skill.Q = skill;

            ResendHeroSkillsMethod.Invoke(null, new object[] { hero, false });

            // 全配列 RPC は1回だけ出て、sanitize 済みの3配列を運ぶ。
            var sent = Assert.Single(skill.ArrayRpcs);
            Assert.Equal(new[] { 5f, 0f }, sent.times);
            Assert.Equal(new[] { 1, 0 }, sent.charges);
            Assert.Equal(new[] { 0.5f, 0f }, sent.delays);
            // ResendAuthoritative（要素ごとの3種 RPC）は呼ばれていない。
            Assert.Empty(skill.RpcLog);
            Assert.Empty(skill.CooldownTimeRpcs);
            Assert.Empty(skill.ChargeRpcs);
            Assert.Empty(skill.MinimumDelayRpcs);
            // ResendSkill の他の副作用（設定同期の dirty・ロック2リストの同値 SET の積み直し）は変わらない。
            Assert.True(skill._isConfigDirty);
            Assert.Equal(LockOps.Take(2), skill._configCastLocks.Operations);
            Assert.Equal(LockOps.Skip(2), skill._configCooldownLocks.Operations);
        }

        /// <summary>省いた要素ごとの再送は、全配列 RPC と同じ sanitize 済みの同じ値を同じ順で送るものであったこと（最終状態が旧動作と同じ）。</summary>
        [Fact]
        public void TheSkippedPerIndexResendsWouldHaveSentTheSameSanitizedValues()
        {
            // 成功側: Resend が全配列 RPC だけを送る。
            var sent = NewSkill();
            FillValues(sent);
            Resend(sent);
            var arrays = Assert.Single(sent.ArrayRpcs);
            Assert.Empty(sent.RpcLog);

            // 旧動作側: 同じ値のトリガーに、旧コードが呼んでいた ResendAuthoritative だけを直接呼ぶ。
            var old = NewSkill();
            FillValues(old);
            int sentCount = NativeCooldownSafety.ResendAuthoritative(old);

            Assert.Equal(2, sentCount);
            Assert.Equal(FullPerIndexLog, old.RpcLog);
            // 要素ごとの RPC が書く値は、全配列 RPC が運ぶ配列と要素ごとに一致する。
            // 受け手（逆コンパイル）は配列 RPC で3配列を丸ごと置き換え、要素ごと RPC は同じ値を
            // 1要素ずつ上書きするだけのため、どちらの経路でも参加者の最終状態は同じになる。
            Assert.Equal(old.CooldownTimeRpcs.Select(r => r.time), arrays.times);
            Assert.Equal(old.ChargeRpcs.Select(r => r.charge), arrays.charges);
            Assert.Equal(old.MinimumDelayRpcs.Select(r => r.delay), arrays.delays);
        }

        /// <summary>本体の全配列 RPC 参照が取れないときは要素ごとの再送に戻る（警告も1回出る）。</summary>
        [Fact]
        public void ArrayRpcUnavailableFallsBackToPerIndexResends()
        {
            var skill = NewSkill();
            FillValues(skill);

            WithArrayRpcNull(() => Resend(skill));

            Assert.Empty(skill.ArrayRpcs);
            Assert.Equal(FullPerIndexLog, skill.RpcLog);
            Assert.Contains(Log.Warnings, w => w.Contains("array RPC"));
        }

        /// <summary>配列の長さが設定数と揃っていないときは要素ごとの再送に戻り、揃った先頭部分だけを送る。</summary>
        [Fact]
        public void MismatchedArrayLengthsFallBackToPerIndexResends()
        {
            var skill = NewSkill();
            FillValues(skill);
            skill.currentUnscaledCooldownTimes = new[] { 5f }; // 設定数2に対して長さ1

            Resend(skill);

            Assert.Empty(skill.ArrayRpcs);
            // ResendAuthoritative は min(設定数, 各配列長) まで送る。この境界面の失敗は警告にならない。
            Assert.Equal(new[] { "time 0 5", "mindelay 0 0.5", "charge 0 1" }, skill.RpcLog);
            Assert.DoesNotContain(Log.Warnings, w => w.Contains("array RPC"));
        }

        /// <summary>全配列 RPC の送出で例外が出たときは失敗扱いで要素ごとの再送に戻る。</summary>
        [Fact]
        public void ArrayRpcSendExceptionFallsBackToPerIndexResends()
        {
            var skill = NewSkill();
            FillValues(skill);
            skill.throwOnArrayRpc = true;

            Resend(skill);

            Assert.Empty(skill.ArrayRpcs);
            Assert.Equal(FullPerIndexLog, skill.RpcLog);
            Assert.Contains(Log.Warnings, w => w.Contains("array RPC"));
        }

        /// <summary>直前の失敗で _arraysFailed が既に立っているときも要素ごとの再送に戻る（再試行しない）。</summary>
        [Fact]
        public void StickyArrayFailureFallsBackToPerIndexResends()
        {
            AccessTools.Field(typeof(NativeSkillLockRecovery), "_arraysFailed").SetValue(null, true);
            var skill = NewSkill();
            FillValues(skill);

            Resend(skill);

            Assert.Empty(skill.ArrayRpcs);
            Assert.Equal(FullPerIndexLog, skill.RpcLog);
            // 既に失敗済みなので警告は重ねて出ない。
            Assert.Empty(Log.Warnings);
        }

        /// <summary>ResendSkill の戻り値は全配列 RPC が送れたかだけを表す: 送れたら true、設定0・配列 null では false。</summary>
        [Fact]
        public void ResendSkillReturnsTrueOnlyWhenTheFullArrayRpcWentOut()
        {
            var sent = NewSkill();
            FillValues(sent);
            Assert.True(NativeSkillLockRecovery.ResendSkill(sent));
            Assert.Single(sent.ArrayRpcs);

            var noConfigs = NewSkill(0);
            Assert.False(NativeSkillLockRecovery.ResendSkill(noConfigs));

            var nullTimes = NewSkill();
            FillValues(nullTimes);
            nullTimes.currentUnscaledCooldownTimes = null;
            Assert.False(NativeSkillLockRecovery.ResendSkill(nullTimes));

            var nullCharges = NewSkill();
            FillValues(nullCharges);
            nullCharges.currentCharges = null;
            Assert.False(NativeSkillLockRecovery.ResendSkill(nullCharges));

            var nullDelays = NewSkill();
            FillValues(nullDelays);
            nullDelays.currentMinimumDelays = null;
            Assert.False(NativeSkillLockRecovery.ResendSkill(nullDelays));
        }

        private static void Resend(Hero hero, SkillTrigger skill)
            => ResendMethod.Invoke(null, new object[] { hero, skill, 0, false });

        private static void Resend(SkillTrigger skill) => Resend(new Hero { isServer = true }, skill);

        private static SkillTrigger NewSkill(int configs = 2)
        {
            var skill = new SkillTrigger
            {
                isActive = true,
                isServer = true,
                configs = new TriggerConfig[configs],
                currentUnscaledCooldownTimes = new float[configs],
                currentCharges = new int[configs],
                currentMinimumDelays = new float[configs],
            };
            for (int i = 0; i < configs; i++) skill.configs[i] = new TriggerConfig();
            return skill;
        }

        // 負の残り時間・負チャージ・NaN の最低待ちを混ぜ、両経路の sanitize が同じ結果になることを確認できる値。
        private static void FillValues(SkillTrigger skill)
        {
            skill.currentUnscaledCooldownTimes = new[] { 5f, -3f };
            skill.currentCharges = new[] { 1, -2 };
            skill.currentMinimumDelays = new[] { 0.5f, float.NaN };
        }

        private static void SeedLocks(SkillTrigger skill)
        {
            skill._configCastLocks.Add(false);
            skill._configCastLocks.Add(true);
            skill._configCooldownLocks.Add(true);
            skill._configCooldownLocks.Add(false);
        }
    }
}
