using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // 実 NativeEssenceCaps / NativeMemoryReuse パッチ（a6ac439b・b8b8968d）を実 Harmony デトゥアで
    // 境界スタブに適用して確かめる。水晶回路と記憶加速は抽出した本体メソッド（Esscap.Native.cs）を直接駆動する。
    // 境界スタブは EssenceCapsNativeApi.cs の逆コンパイル基準で、実 DLL での動作確認ではない。
    // 2026-10-16 方針変更で無効化した上限（qualitySoftCap・fever・embertail・epiphany・memoryHaste）は
    // Prepare が false で飛ばされるので、本体値の素通しを確かめる。
    public sealed class EssenceCapsTests : IDisposable
    {
        private readonly Harmony owner = new Harmony("essence-caps-tests." + Guid.NewGuid().ToString("N"));

        public EssenceCapsTests()
        {
            Mirror.NetworkServer.active = false;
            NativeCooldownSafety.Current = null;
            NativeEssenceCaps.LinearQualityGem = false;
            NativeMemoryReuse.Disabled = false;
            Log.Warnings.Clear();
            UnityEngine.Time.time = 0f;
            // PatchEachClass と同じ経路で1クラスずつ適用する（Prepare が false のクラスは飛過される）。
            foreach (var patchClass in new[]
            {
                typeof(EssenceQualitySoftCap), typeof(FrostMortalityQualityExemption),
                typeof(RicochetChanceCap), typeof(FeverDamageAmplificationCap),
                typeof(EmbertailCooldownCap), typeof(EpiphanyRefundCap),
                typeof(MemoryReuseOnCast), typeof(MemoryReuseCastGate), typeof(MemoryReuseCooldownDisplay)
            })
                owner.CreateClassProcessor(patchClass).Patch();
        }

        /// <summary>品質ソフト上限は無効（2026-10-16 方針変更）。パッチ適用後も本体どおりの線形のまま。</summary>
        [Fact]
        public void QualitySoftCapDisabledKeepsTheVanillaLinearScaling()
        {
            var scaling = new ScalingValue { addedScalingPerLevel = 0.01f };
            const float added = 0.01f;

            // 無効ならソフト上限の式に入らず、品質300超でも本体の線形式そのまま。
            Assert.Equal(1f + added * 0f, scaling.GetScalingMultiplier(1));
            Assert.Equal(1f + added * 300f, scaling.GetScalingMultiplier(301));
            Assert.Equal(1f + added * 600f, scaling.GetScalingMultiplier(601));

            // 宝石スケーリング以外にも一切介入しない。
            scaling.leveling = LevelScaling.None;
            Assert.Equal(1f + added * 600f, scaling.GetScalingMultiplier(601));
        }

        /// <summary>品質ソフト上限が無効の間は Frost・Mortality の免除パッチも入らない。全宝石が本体どおり。</summary>
        [Fact]
        public void QualitySoftCapDisabledLeavesEveryGemOnTheVanillaValue()
        {
            var scaling = new ScalingValue { addedScalingPerLevel = 0.01f };
            const float linear = 1f + 0.01f * 600f;

            var plain = new Gem_R_Ricochet { level = 601 };
            var frost = new Gem_R_Frost { level = 601 };
            var mortality = new Gem_R_Mortality { level = 601 };

            Assert.Equal(linear, plain.GetValue(scaling));
            Assert.Equal(linear, frost.GetValue(scaling));
            Assert.Equal(linear, mortality.GetValue(scaling));
            // 免除判定フラグも立ったままにならない（説明文のプレビューも本体どおりになる）。
            Assert.False(NativeEssenceCaps.LinearQualityGem);
        }

        /// <summary>Ricochet の跳弾確率は 60% で頭打ち、60% 未満は変わらない。</summary>
        [Fact]
        public void RicochetChanceIsCappedAtSixtyPercent()
        {
            var over = new Gem_R_Ricochet { ricochetChance = 0.9f };
            var under = new Gem_R_Ricochet { ricochetChance = 0.4f };

            Assert.Equal(0.6f, over.ricochetChance);
            Assert.Equal(0.4f, under.ricochetChance);
        }

        /// <summary>Fever の上限は無効。増幅値は本体どおりで頭打ちに入らない。</summary>
        [Fact]
        public void FeverCapDisabledKeepsTheVanillaDamageAmplification()
        {
            var over = new Se_Gem_E_Fever_LivingBomb { damageAmp = 2.0f };
            var under = new Se_Gem_E_Fever_LivingBomb { damageAmp = 1.2f };

            Assert.Equal(2.0f, over.GetDamageAmp());
            Assert.Equal(1.2f, under.GetDamageAmp());
        }

        /// <summary>Embertail の上限は無効。サーバ側でも短縮の床付けに入らない。</summary>
        [Fact]
        public void EmbertailCapDisabledKeepsTheVanillaCooldownMultiplier()
        {
            Mirror.NetworkServer.active = true;
            var host = new Gem_L_Embertail();
            host._bonus.cooldownMultiplier = 0.2f;
            host.ActiveLogicUpdate(0f);
            Assert.Equal(0.2f, host._bonus.cooldownMultiplier);
        }

        /// <summary>Epiphany の上限は無効。還元率は本体どおりで頭打ちに入らない。</summary>
        [Fact]
        public void EpiphanyCapDisabledKeepsTheVanillaRefundRatio()
        {
            var over = new Gem_R_Epiphany { refundCooldownRatio = 0.8f };
            var under = new Gem_R_Epiphany { refundCooldownRatio = 0.5f };

            Assert.Equal(0.8f, over.refundCooldownRatio);
            Assert.Equal(0.5f, under.refundCooldownRatio);
        }

        /// <summary>記憶（Q/W/E/R）の再使用最短間隔: 基礎CDの 20%、最低 0.3 秒。発動中は Cast できない。</summary>
        [Fact]
        public void MemoryReuseIntervalIsFifthOfBaseCooldownWithFloor()
        {
            Mirror.NetworkServer.active = true;
            var hero = new Hero();
            var memory = new SkillTrigger { owner = hero };
            memory.configs = new[] { new TriggerConfig { cooldownTime = 10f } };
            hero.Skill.Q = memory;
            var shortCd = new SkillTrigger { owner = hero };
            shortCd.configs = new[] { new TriggerConfig { cooldownTime = 1f } };
            hero.Skill.W = shortCd;

            memory.OnCastCompleteSetMinimumDelay(0, new CastInfo());
            shortCd.OnCastCompleteSetMinimumDelay(0, new CastInfo());

            Assert.Equal(2f, memory.currentMinimumDelays[0]);
            Assert.Equal(0.3f, shortCd.currentMinimumDelays[0]);
            Assert.Contains("mindelay 0 2", memory.RpcLog);
            // 間隔が残っている間は Cast も予約もできない。
            Assert.False(memory.CanBeCast());
            Assert.False(memory.CanBeReserved());
        }

        /// <summary>移動・Identity（Q/W/E/R 以外）とサーバ外の発動には介入しない。</summary>
        [Fact]
        public void MemoryReuseLeavesNonMemoriesAndServerCastsAlone()
        {
            var hero = new Hero();
            var movement = new SkillTrigger { owner = hero };
            movement.configs = new[] { new TriggerConfig { cooldownTime = 10f } };
            var identity = new SkillTrigger { owner = hero };
            identity.configs = new[] { new TriggerConfig { cooldownTime = 10f } };
            var memory = new SkillTrigger { owner = hero };
            memory.configs = new[] { new TriggerConfig { cooldownTime = 10f } };
            hero.Skill.Q = memory;

            Mirror.NetworkServer.active = true;
            movement.OnCastCompleteSetMinimumDelay(0, new CastInfo());
            identity.OnCastCompleteSetMinimumDelay(0, new CastInfo());
            // 介入されていれば基礎CD 10 秒の 20% = 2 秒に持ち上げられるが、本体の 0 秒のまま。
            Assert.Equal(0f, movement.currentMinimumDelays[0]);
            Assert.Equal(0f, identity.currentMinimumDelays[0]);
            Assert.True(movement.CanBeCast());

            // サーバでない Cast 完了にも介入しない（ホスト権限の演算ではない）。
            Mirror.NetworkServer.active = false;
            memory.OnCastCompleteSetMinimumDelay(0, new CastInfo());
            Assert.Equal(0f, memory.currentMinimumDelays[0]);
            Assert.True(memory.CanBeCast());
        }

        /// <summary>再使用間隔と既存 NativeCooldownSafety の両パッチが同じトリガー上で共存する。</summary>
        [Fact]
        public void MemoryReuseGateCoexistsWithNativeCooldownSafety()
        {
            var safety = new Harmony("essence-caps-tests.safety." + Guid.NewGuid().ToString("N"));
            foreach (var patchClass in new[]
            {
                typeof(NativeCooldownReductionSafety), typeof(NativeCooldownTickSafety),
                typeof(NativeCooldownMultiplierSafety), typeof(NativeBaseCooldownMultiplierSafety)
            })
                safety.CreateClassProcessor(patchClass).Patch();
            try
            {
                Mirror.NetworkServer.active = true;
                var hero = new Hero();
                var memory = new SkillTrigger { owner = hero, multiplier = 0f };
                memory.configs = new[] { new TriggerConfig { cooldownTime = 10f } };
                hero.Skill.Q = memory;

                memory.OnCastCompleteSetMinimumDelay(0, new CastInfo());
                memory.ApplyCooldownReductionWithMinimum_Imp(3f, 0f, true, false);

                // 安全側は壊れた倍率を 0.01 に置換し…
                Assert.Equal(NativeCooldownSafety.InvalidMultiplierFallback, memory.UsedMultipliers.Single());
                // …再使用間隔の門はそのまま有効。
                Assert.Equal(2f, memory.currentMinimumDelays[0]);
                Assert.False(memory.CanBeCast());
            }
            finally
            {
                foreach (var target in safety.GetPatchedMethods().ToArray())
                    safety.Unpatch(target, HarmonyPatchType.All, safety.Id);
            }
        }

        /// <summary>水晶回路: 同じ記憶への短縮は 0.5 秒の内部 CD を空けないと再度入らない。</summary>
        [Fact]
        public void CrystalCircuitReducesAMemoryAtMostOncePerHalfSecond()
        {
            Mirror.NetworkServer.active = true;
            UnityEngine.Time.time = 100f;
            var host = NewHost(out var hero, out var memory, Power.CrystalCircuit, 15);
            var gem = new Gem { owner = hero };

            host.OnNativeGemUseV129(gem);
            Assert.Single(hero.RatioReductions);
            Assert.Equal((memory, 0.15f, false), hero.RatioReductions[0]);

            // 0.5 秒未満の再発動では同じ記憶への短縮が入らない。
            UnityEngine.Time.time = 100.2f;
            host.OnNativeGemUseV129(gem);
            Assert.Single(hero.RatioReductions);

            UnityEngine.Time.time = 100.6f;
            host.OnNativeGemUseV129(gem);
            Assert.Equal(2, hero.RatioReductions.Count);
            Assert.Equal(0.15f, hero.RatioReductions[1].ratio);
        }

        /// <summary>記憶加速の上限は無効（2026-10-16 方針変更）。無効化前と同じ割合短縮が上限 100% のまま入り、
        /// 残りCD基準の 60% 上限には入らない。</summary>
        [Fact]
        public void MemoryHasteCapDisabledAppliesTheUncappedRatioReduction()
        {
            Mirror.NetworkServer.active = true;
            // 連携と星の合算 150%: 上限が有効なら残りCDの 60% だが、無効なら従来どおり最大100%の割合短縮。
            var host = NewHost(out var hero, out var memory, nativeHasteMilliPercent: 150000);
            host.InvokeOnSkillUseForTests(host._runtimes[hero], new EventInfoSkillUse { skill = memory, type = HeroSkillLocation.Q });

            Assert.Single(hero.RatioReductions);
            Assert.Equal((memory, 1f, false), hero.RatioReductions[0]);

            var modest = NewHost(out var hero2, out var memory2, nativeHasteMilliPercent: 40000);
            modest.InvokeOnSkillUseForTests(modest._runtimes[hero2], new EventInfoSkillUse { skill = memory2, type = HeroSkillLocation.Q });

            Assert.Single(hero2.RatioReductions);
            Assert.Equal((memory2, 0.4f, false), hero2.RatioReductions[0]);
        }

        /// <summary>数値原本（essence-caps.json）と生成された EssenceCapsBalance が一致する。
        /// enabled を false に再生成すれば各項目が個別に無効化される（パッチの Prepare / IsMemory / 本体側の分岐が参照する）。</summary>
        [Fact]
        public void EssenceCapsBalanceMirrorsTheBalanceJsonKillSwitches()
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "EssenceCaps.json")));
            var balance = typeof(EssenceCapsBalance);

            bool Enabled(string section) => (bool)json[section]["enabled"];
            float Value(string section, string key) => (float)json[section][key];
            bool Flag(string name) => (bool)balance.GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            float Const(string name) => (float)balance.GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

            Assert.Equal(Enabled("qualitySoftCap"), Flag("QualitySoftCapEnabled"));
            Assert.Equal(Value("qualitySoftCap", "threshold"), Const("QualityThreshold"));
            Assert.Equal(Value("qualitySoftCap", "excessDivisor"), Const("QualityExcessDivisor"));
            Assert.Equal(Enabled("ricochet"), Flag("RicochetEnabled"));
            Assert.Equal(Value("ricochet", "maxChance"), Const("RicochetMaxChance"));
            Assert.Equal(Enabled("fever"), Flag("FeverEnabled"));
            Assert.Equal(Value("fever", "maxDamageAmplification"), Const("FeverMaxDamageAmplification"));
            Assert.Equal(Enabled("embertail"), Flag("EmbertailEnabled"));
            Assert.Equal(Value("embertail", "maxCooldownReduction"), Const("EmbertailMaxCooldownReduction"));
            Assert.Equal(Enabled("epiphany"), Flag("EpiphanyEnabled"));
            Assert.Equal(Value("epiphany", "maxRefundFraction"), Const("EpiphanyMaxRefundFraction"));
            Assert.Equal(Enabled("memoryReuse"), Flag("MemoryReuseEnabled"));
            Assert.Equal(Value("memoryReuse", "baseCooldownFraction"), Const("MemoryReuseBaseCooldownFraction"));
            Assert.Equal(Value("memoryReuse", "minimumSeconds"), Const("MemoryReuseMinimumSeconds"));
            Assert.Equal(Enabled("crystalCircuit"), Flag("CrystalCircuitEnabled"));
            Assert.Equal(Value("crystalCircuit", "internalCooldownSeconds"), Const("CrystalCircuitInternalCooldownSeconds"));
            Assert.Equal(Enabled("memoryHaste"), Flag("MemoryHasteEnabled"));
            Assert.Equal(Value("memoryHaste", "maxRemainingCooldownPercent"), Const("MemoryHasteMaxRemainingCooldownPercent"));
        }

        private static HostAuthority NewHost(out Hero hero, out SkillTrigger memory,
            Power power = (Power)(-1), int powerValue = 0, int nativeHasteMilliPercent = 0)
        {
            hero = new Hero { isActive = true };
            memory = new SkillTrigger { owner = hero };
            memory.configs = new[] { new TriggerConfig { cooldownTime = 10f } };
            hero.Skill.Q = memory;
            var powers = new Build();
            if (powerValue > 0) powers.Powers[power] = powerValue;
            var build = new Build();
            if (nativeHasteMilliPercent != 0)
                build.NativeModifiers.Add(new NativeMemoryModifierEntry
                { Memory = memory.GetType().Name, Kind = LinkKind.MemoryHaste, ValueMilli = nativeHasteMilliPercent });
            var rt = new HostAuthority.HeroRuntime
            {
                Hero = hero,
                Powers = new PowerRuntime(powers, 0f),
                AppliedBuild = new HostAuthority.ReceivedBuild { Build = build }
            };
            return new HostAuthority(rt);
        }

        public void Dispose()
        {
            Mirror.NetworkServer.active = false;
            NativeCooldownSafety.Current = null;
            NativeEssenceCaps.LinearQualityGem = false;
            foreach (var target in owner.GetPatchedMethods().ToArray())
                owner.Unpatch(target, HarmonyPatchType.All, owner.Id);
        }
    }
}
