using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // リンクした実装 FrostFxLifetime.cs の寿命管理を、本体の境界スタブ越しに検証する。
    // DewEffect.PlayIntoPool への Postfix ディトゥアは本物の Harmony で入り、
    // タイマー（InvokeRepeating）だけをスタブの時計で同期的に進める。
    public sealed class FrostFxLifetimeTests : IDisposable
    {
        private static readonly MethodInfo PlayIntoPool =
            AccessTools.Method(typeof(DewEffect), nameof(DewEffect.PlayIntoPool));
        private readonly Harmony owner = new Harmony("frost-fx-tests." + Guid.NewGuid().ToString("N"));

        public FrostFxLifetimeTests()
        {
            Log.Errors.Clear();
            Log.Warnings.Clear();
            Log.Infos.Clear();
            ResetFrostPatch();
            EffectAutoDestroy._entries.Clear();
            EffectAutoDestroy.ReleasedClones.Clear();
            DewEffect.Stopped.Clear();
            UnityEngine.Time.time = 0;
            NetworkedManagerBase<ZoneManager>.softInstance = null;
            NetworkedManagerBase<GameManager>.softInstance = null;
            PerformanceTuner.Starts = 0;
            // 実装のパッチクラスを設置する（前のテストで外れていれば入れ直す）。
            // Unpatch 後も GetPatchInfo は空の Patches を返すことがあるので実測で判定する。
            var frostPatches = Harmony.GetPatchInfo(PlayIntoPool);
            if (frostPatches == null || !frostPatches.Owners.Any())
            {
                var installed = owner.CreateClassProcessor(typeof(FrostFxLifetimePatch)).Patch();
                Assert.False(installed == null || installed.Count == 0, "FrostFxLifetimePatch が設置されなかった");
            }
        }

        public void Dispose()
        {
            foreach (var target in owner.GetPatchedMethods().ToArray())
                owner.Unpatch(target, HarmonyPatchType.All, owner.Id);
        }

        /// <summary>
        /// Prepare が対象（DewEffect.PlayIntoPool）を解決できないときは false を返して警告を1回出し、
        /// このパッチだけが飛ぶ。他のパッチ・MOD全体はそのまま起動する。
        /// </summary>
        [Fact]
        public void MissingPlayIntoPoolTargetDisablesOnlyTheFrostPatch()
        {
            // このテストだけは未設置状態から始める（fixture が入れたディトゥアをすべて外す）。
            var existing = Harmony.GetPatchInfo(PlayIntoPool);
            if (existing != null)
                foreach (var harmonyId in existing.Owners.ToArray())
                    new Harmony(harmonyId).Unpatch(PlayIntoPool, HarmonyPatchType.All, harmonyId);
            AssertFrostPatchAbsent();

            // 本体の更新で PlayIntoPool が消えた状況: その組の解決だけを null にする。
            var missing = new Harmony("frost-missing." + Guid.NewGuid().ToString("N"));
            var resolvers = typeof(AccessTools).GetMethods()
                .Where(method => method.Name == nameof(AccessTools.DeclaredMethod))
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length >= 2
                        && parameters[0].ParameterType == typeof(Type) && parameters[0].Name == "type"
                        && parameters[1].ParameterType == typeof(string) && parameters[1].Name == "name";
                })
                .ToList();
            Assert.NotEmpty(resolvers);
            foreach (var resolver in resolvers)
                missing.Patch(resolver, prefix: new HarmonyMethod(
                    typeof(FrostFxLifetimeTests), nameof(NullOutFrostPlayIntoPool)));
            try
            {
                var mod = new DreamforgeMod { harmony = owner };
                Invoke(mod, "Awake");

                Assert.Contains(Log.Warnings, message => message.Contains("Frost FX lifetime patch disabled"));
                Assert.Contains(Log.Warnings, message => message.Contains("other MOD features remain active"));
                Assert.DoesNotContain(PlayIntoPool, owner.GetPatchedMethods());
                AssertFrostPatchAbsent();
                // MOD全体は止まらない: 初期化は完了し、同じ起動の他パッチは入っている。
                Assert.Equal(1, PerformanceTuner.Starts);
                Assert.True(mod.instance.isAlteringGameplay);
                var first = AccessTools.Method(typeof(NativeContractTarget), nameof(NativeContractTarget.First));
                Assert.Contains(owner.Id, Harmony.GetPatchInfo(first).Owners);

                Invoke(mod, "OnDestroy");
            }
            finally
            {
                foreach (var target in missing.GetPatchedMethods().ToArray())
                    missing.Unpatch(target, HarmonyPatchType.All, missing.Id);
                ResetFrostPatch();
            }
        }

        /// <summary>
        /// 効果終了（frost_stun の破棄イベント）で、プールのレジストリを経由して付着 FX が回収される。
        /// テンプレートが異なる再生は追跡しない。
        /// </summary>
        [Fact]
        public void EffectEndEventReleasesTheRegisteredCloneThroughThePoolRegistry()
        {
            var (frost, hero, victim, identity) = CreateFrostScene(2f);
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager();
            UnityEngine.Time.time = 100f;

            var stray = DewEffect.PlayIntoPool(new GameObject { name = "unrelated" },
                null, victim, false, default, default, identity);
            Assert.Null(stray.GetComponent<FrostFxLifetime>());

            var clone = DewEffect.PlayIntoPool(frost.activateEffect,
                null, victim, false, default, default, identity);
            var lifetime = Assert.IsType<FrostFxLifetime>(clone.GetComponent<FrostFxLifetime>());
            Assert.Equal(2, EffectAutoDestroy._entries.Count);
            Assert.True(clone.activeSelf);

            var stun = BindFrostStun(frost, victim, lifetime);

            stun.RaiseClientActorDestroyed();

            Assert.Same(clone, EffectAutoDestroy.ReleasedClones.Single());
            Assert.Same(clone, DewEffect.Stopped.Single());
            Assert.Same(stray, Assert.Single(EffectAutoDestroy._entries).gameObject);
            Assert.False(clone.activeSelf);
            Assert.DoesNotContain(Log.Warnings, message => message.Contains("Frost FX lifetime patch disabled"));
        }

        /// <summary>
        /// 一度回収した FX は、その後のどのトリガー（対象/所有者/本体の破棄、所有者変更、部屋遷移、
        /// タイマー）でも二度返されない。
        /// </summary>
        [Fact]
        public void RetiredCloneIsNotReleasedAgainByLaterTriggers()
        {
            var (frost, hero, victim, identity) = CreateFrostScene(2f);
            var zone = new ZoneManager();
            NetworkedManagerBase<ZoneManager>.softInstance = zone;
            var clone = DewEffect.PlayIntoPool(frost.activateEffect,
                null, victim, false, default, default, identity);
            var lifetime = Assert.IsType<FrostFxLifetime>(clone.GetComponent<FrostFxLifetime>());
            var stun = BindFrostStun(frost, victim, lifetime);

            stun.RaiseClientActorDestroyed(); // 1回目で回収済み
            Assert.Single(EffectAutoDestroy.ReleasedClones);

            frost.RaiseClientActorDestroyed();
            hero.RaiseClientActorDestroyed();
            victim.RaiseClientActorDestroyed();
            frost.RaiseOwnerChanged(hero, null);
            zone.RaiseRoomLoaded();
            UnityEngine.Time.time += 100f;
            lifetime.Advance(100f);

            Assert.Single(EffectAutoDestroy.ReleasedClones);
            Assert.Single(DewEffect.Stopped);
            Assert.DoesNotContain(Log.Warnings, message => message.Contains("Frost FX lifetime patch disabled"));
        }

        /// <summary>
        /// 終了信号が1つも来なくても、有限の寿命上限（NaN 持続時間は30秒に丸める）で必ず回収される。
        /// 上限の前では、ステータス不在（リモートクライアント）を終了とは解釈しない。
        /// </summary>
        [Fact]
        public void FiniteDeadlineReclaimsTheCloneWhenNoEndSignalArrives()
        {
            var (frost, hero, victim, identity) = CreateFrostScene(float.NaN);
            UnityEngine.Time.time = 200f;
            var clone = DewEffect.PlayIntoPool(frost.activateEffect,
                null, victim, false, default, default, identity);
            var lifetime = Assert.IsType<FrostFxLifetime>(clone.GetComponent<FrostFxLifetime>());

            for (int i = 0; i < 118; i++)
            {
                UnityEngine.Time.time += 0.25f;
                lifetime.Advance(0.25f);
            }
            // 29.5秒: まだ追跡中で、レジストリにも残っている。
            Assert.Empty(EffectAutoDestroy.ReleasedClones);
            Assert.Contains(EffectAutoDestroy._entries, entry => ReferenceEquals(entry.gameObject, clone));

            UnityEngine.Time.time += 0.5f;
            lifetime.Advance(0.5f); // 30秒上限に到達

            Assert.Same(clone, EffectAutoDestroy.ReleasedClones.Single());
            Assert.Same(clone, DewEffect.Stopped.Single());
            Assert.False(clone.activeSelf);
        }

        /// <summary>
        /// 破棄イベントなしで効果だけ終わった状況は、0.25秒間隔の監視の次回走査で回収する。
        /// </summary>
        [Fact]
        public void PeriodicMonitorDetectsEffectEndWithoutADestroyEvent()
        {
            var (frost, hero, victim, identity) = CreateFrostScene(2f);
            var clone = DewEffect.PlayIntoPool(frost.activateEffect,
                null, victim, false, default, default, identity);
            var lifetime = Assert.IsType<FrostFxLifetime>(clone.GetComponent<FrostFxLifetime>());
            var stun = BindFrostStun(frost, victim, lifetime);

            stun.isActive = false; // 効果の終了だけが起きた
            UnityEngine.Time.time += 0.25f;
            lifetime.Advance(0.25f);

            Assert.Same(clone, EffectAutoDestroy.ReleasedClones.Single());
            Assert.Same(clone, DewEffect.Stopped.Single());
        }

        // --- helpers ---

        private static (Gem_R_Frost frost, Hero hero, Entity victim, Mirror.NetworkIdentity identity) CreateFrostScene(
            float stunDuration)
        {
            var hero = new Hero { isActive = true, netId = 11, creationTime = 1f };
            var victim = new Monster { isActive = true, netId = 12, creationTime = 1f };
            var frost = new Gem_R_Frost
            {
                owner = hero,
                stunDuration = stunDuration,
                isActive = true,
                netId = 13,
                creationTime = 1f,
            };
            var identity = new Mirror.NetworkIdentity();
            identity.components.Add(frost);
            return (frost, hero, victim, identity);
        }

        // 1監視周期進めて、victim の statusEffects から frost_stun を購読させる。
        private static Se_GenericEffectContainer BindFrostStun(Gem_R_Frost frost, Entity victim, FrostFxLifetime lifetime)
        {
            var stun = new Se_GenericEffectContainer
            {
                victim = victim,
                parentActor = frost,
                id = "frost_stun",
                isActive = true,
                netId = 41,
                creationTime = 100.5f,
            };
            victim.Status.statusEffects.Add(stun);
            UnityEngine.Time.time += 0.25f;
            lifetime.Advance(0.25f);
            return stun;
        }

        private static void AssertFrostPatchAbsent()
        {
            var patches = Harmony.GetPatchInfo(PlayIntoPool);
            Assert.True(patches == null || !patches.Owners.Any(), "FrostFxLifetimePatch のディトゥアが残っている");
        }

        private static void ResetFrostPatch()
        {
            typeof(FrostFxLifetimePatch)
                .GetField("_disabled", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, false);
        }

        private static bool NullOutFrostPlayIntoPool(Type type, string name, ref MethodInfo __result)
        {
            if (type != typeof(DewEffect) || name != nameof(DewEffect.PlayIntoPool)) return true;
            __result = null;
            return false;
        }

        private static void Invoke(DreamforgeMod mod, string name)
            => AccessTools.Method(typeof(DreamforgeMod), name).Invoke(mod, null);
    }
}
