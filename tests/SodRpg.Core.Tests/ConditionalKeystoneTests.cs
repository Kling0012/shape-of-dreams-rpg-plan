using System;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// M6 conditional keystone sides: a typed condition applies its transform only while its event predicate holds,
    /// the facts travel through the keystone codec, and the two named legacy keystone migrations stay typed.
    /// </summary>
    public sealed class ConditionalKeystoneTests
    {
        private const string Hero = "Hero_Mist";
        private static readonly KeystoneCaps Uncapped = new KeystoneCaps(decimal.MaxValue);

        private static AuthoredKeystoneSpec Downside(KeystoneSourceKind kind, decimal percent = -15m,
            KeystoneConditionKind? condition = null, decimal threshold = 90m) => new AuthoredKeystoneSpec
        {
            Layer = KeystoneLayer.NativeDamage, Percent = percent,
            Scope = new KeystoneScope(sourceKind: kind, condition: condition, conditionPercent: threshold)
        };

        private static AuthoredKeystoneSpec[] ThresholdDownside() => new[]
        {
            Downside(KeystoneSourceKind.NativeMemory, condition: KeystoneConditionKind.TargetHealthBelow),
            Downside(KeystoneSourceKind.OwnedBasicAttack, condition: KeystoneConditionKind.TargetHealthBelow)
        };

        private static ScopedKeystoneModifiers Runtime(KeystoneDefinition key)
        {
            var runtime = new ScopedKeystoneModifiers(new[] { key });
            runtime.Configure(new[] { key.KeystoneId }, 1, new[] { "St_Q_Fleche", "St_Q_SylvanCall" },
                Array.Empty<string>(), Array.Empty<KeystoneAllocatedEffect>());
            return runtime;
        }

        private static decimal NativeHit(ScopedKeystoneModifiers runtime, KeystoneSourceKind kind, string source,
            float? health = null, bool? retaliation = null) => runtime.Apply(
            new KeystonePayload(KeystoneLayer.NativeDamage, 100m, Uncapped),
            new KeystoneContext(1, source, kind, targetHealthPercent: health, retaliationWindowOpen: retaliation)).Value;

        [Fact]
        public void Target_health_condition_reduces_native_damage_only_below_the_threshold()
        {
            // h.mist.key: 自分の本体ダメージ = own native memory + own basic attack, both gated on HP90%未満 (strict).
            var key = StarClusters.ManifestKeystone(Hero, "h.mist.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                ThresholdDownside(), Array.Empty<string>(), Content.KeystoneCost);
            var runtime = Runtime(key);
            Assert.Equal(85m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_Fleche", health: 89.9f));
            Assert.Equal(85m, NativeHit(runtime, KeystoneSourceKind.OwnedBasicAttack, null, health: 50f));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_Fleche", health: 90f));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.OwnedBasicAttack, null, health: 91f));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_Fleche"));          // no fact: inert
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.OwnedBasicAttack, null));
        }

        [Fact]
        public void Outside_retaliation_window_condition_reduces_only_when_the_existing_window_is_closed()
        {
            // h.mist.key2: 本体記憶ダメージ −15%, exempt while the existing Retaliation window (被弾後3秒) is open.
            var key = StarClusters.ManifestKeystone(Hero, "h.mist.key2", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { Downside(KeystoneSourceKind.NativeMemory, condition: KeystoneConditionKind.OutsideRetaliationWindow) },
                Array.Empty<string>(), Content.KeystoneCost);
            var runtime = Runtime(key);
            Assert.Equal(85m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_Fleche", retaliation: false));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_Fleche", retaliation: true));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_Fleche"));          // no fact: inert
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.OwnedBasicAttack, null, retaliation: false));
        }

        [Fact]
        public void Summon_direct_damage_scope_reduces_only_owned_summon_packets()
        {
            // h.nachia.key2: 自分の召喚獣による直接ダメージを最終-15%.
            var key = StarClusters.ManifestKeystone("Hero_Nachia", "h.nachia.key2", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { Downside(KeystoneSourceKind.OwnedSummon) }, Array.Empty<string>(), Content.KeystoneCost);
            var runtime = Runtime(key);
            Assert.Equal(85m, NativeHit(runtime, KeystoneSourceKind.OwnedSummon, null));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.NativeMemory, "St_Q_SylvanCall"));
            Assert.Equal(100m, NativeHit(runtime, KeystoneSourceKind.OwnedBasicAttack, null));
        }

        [Fact]
        public void Conditions_round_trip_through_the_codec_and_change_the_wire_identity()
        {
            var key = StarClusters.ManifestKeystone(Hero, "h.mist.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { Downside(KeystoneSourceKind.NativeMemory, condition: KeystoneConditionKind.TargetHealthBelow) },
                Array.Empty<string>(), Content.KeystoneCost);
            var decoded = AuthoredKeystoneCodec.Decode(AuthoredKeystoneCodec.Encode(key));
            var scope = decoded.Downside.Single().Scope;
            Assert.Equal(KeystoneConditionKind.TargetHealthBelow, scope.Condition);
            Assert.Equal(90m, scope.ConditionPercent);
            Assert.Equal(AuthoredKeystoneCodec.Encode(key), AuthoredKeystoneCodec.Encode(decoded));
            var sharper = StarClusters.ManifestKeystone(Hero, "h.mist.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { Downside(KeystoneSourceKind.NativeMemory, condition: KeystoneConditionKind.TargetHealthBelow, threshold: 95m) },
                Array.Empty<string>(), Content.KeystoneCost);
            Assert.NotEqual(AuthoredKeystoneCodec.Encode(key), AuthoredKeystoneCodec.Encode(sharper));
            Assert.Throws<ArgumentException>(() => new KeystoneScope(
                condition: KeystoneConditionKind.TargetHealthBelow, conditionPercent: 0m));
        }

        [Fact]
        public void Explicit_same_id_power_migration_carries_the_manifest_value_exactly_once()
        {
            // h.yubar.key2: 旧Power.StarShield20を同IDで15へ明示移行 (実Ultimate使用時 最大HP15%・5秒) + native downside.
            var key = StarClusters.ManifestKeystone("Hero_Yubar", "h.yubar.key2", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -15m,
                    Scope = new KeystoneScope(sourceKind: KeystoneSourceKind.NativeMemory,
                        sourceSelectors: new[] { MemorySelector.Parse("St_R_Cataclysm") }) } },
                Array.Empty<string>(), Content.KeystoneCost, migratedPowerValue: 15);
            Assert.Equal(Power.StarShield, key.RetainedPower);
            Assert.Equal(15, key.RetainedPowerValue);
            var node = StarClusters.ManifestRetained("Hero_Yubar", "h.yubar.key2",
                new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("星の盾", "Stellar Ward"),
                    MaxRank = 1, KeystoneDefinition = key },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), null, "tests",
                new[] { "C07" }, "");
            Assert.Equal(Power.StarShield, node.Effect.Power);
            Assert.Equal(15, node.Effect.Amount);
            try
            {
                StarClusters.RegisterAuthored("Hero_Yubar", new[] { node });
                var build = Select("Hero_Yubar", "h.yubar.key2");
                Assert.Equal(15, build.Get(Power.StarShield));                       // the migrated value once, not 20 or 35
                Assert.Equal(15, build.SelectedKeystone.RetainedPowerValue);
                var decoded = Build.Decode(build.Encode());
                Assert.Equal(15, decoded.Get(Power.StarShield));
                Assert.Equal(15, decoded.SelectedKeystone.RetainedPowerValue);
            }
            finally { StarClusters.RegisterAuthored("Hero_Yubar", Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Calm_filter_grant_migrates_the_native_stillwater_power_of_its_named_key()
        {
            // h.cetus.key2: Power.StillWater=6 kept as the typed upside, replaced by the C08 Q/R stun filter, plus its downside.
            var key = AuthoredKeystoneCompiler.Compile("h.cetus.key2", Array.Empty<string>(),
                new[] { new AuthoredKeystoneSpec { Scope = new KeystoneScope(),
                    Grant = new AuthoredMechanismSpec { ChannelId = "h.cetus.key2.grant", Kind = AuthoredMechanismKind.StunSourceFilter } } },
                new[] { new AuthoredKeystoneSpec { Layer = KeystoneLayer.NativeDamage, Percent = -15m,
                    Scope = new KeystoneScope(sourceKind: KeystoneSourceKind.NativeMemory,
                        sourceSelectors: new[] { MemorySelector.Parse("@Q|@R") }) } },
                retainedPower: Power.StillWater, retainedPowerValue: 6);
            var node = StarClusters.ManifestRetained("Hero_Cetus", "h.cetus.key2",
                new ClusterStarDef { Kind = ClusterStarKind.Keystone, Name = new Txt("凪", "Calm Sea"),
                    MaxRank = 1, KeystoneDefinition = key },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<AuthoredStarEdge>(), null, "tests",
                new[] { "C07", "C08" }, "");
            try
            {
                StarClusters.RegisterAuthored("Hero_Cetus", new[] { node });
                var build = Select("Hero_Cetus", "h.cetus.key2");
                Assert.Equal(0, build.Get(Power.StillWater));                        // migrated: the filter replaces the native power
                var grant = build.SelectedKeystone.Grants.Single();
                Assert.Equal(AuthoredMechanismKind.StunSourceFilter, grant.Kind);
                Assert.Contains(build.Mechanisms, entry => entry.StarId == "h.cetus.key2"
                    && entry.Spec.Kind == AuthoredMechanismKind.StunSourceFilter);
                var decoded = Build.Decode(build.Encode());
                Assert.Equal(AuthoredMechanismKind.StunSourceFilter, decoded.SelectedKeystone.Grants.Single().Kind);
                Assert.Equal(0, decoded.Get(Power.StillWater));
            }
            finally { StarClusters.RegisterAuthored("Hero_Cetus", Array.Empty<AuthoredStarDef>()); }
        }

        [Fact]
        public void Native_damage_path_applies_the_condition_from_real_event_facts()
        {
            var key = StarClusters.ManifestKeystone(Hero, "h.mist.key2", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { Downside(KeystoneSourceKind.NativeMemory, condition: KeystoneConditionKind.OutsideRetaliationWindow) },
                Array.Empty<string>(), Content.KeystoneCost);
            var (host, runtime, identity, victim) = NativePath(key);
            var data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, identity, ref data);               // never hit: window closed → −15%
            Assert.Equal(85, data.currentAmount, 4);
            runtime.Powers.OnDamaged(UnityEngine.Time.time, 50f, attackerIsEnemy: true);  // enemy hit opens the existing 3 s window
            data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, identity, ref data);
            Assert.Equal(100, data.currentAmount, 4);
            UnityEngine.Time.time = PowerRuntime.RetaliationDuration + 1f;           // window expired → −15% again
            data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, identity, ref data);
            Assert.Equal(85, data.currentAmount, 4);
        }

        [Fact]
        public void Native_damage_path_reads_the_pre_hit_victim_health_for_the_threshold_condition()
        {
            var key = StarClusters.ManifestKeystone(Hero, "h.mist.key", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                ThresholdDownside(), Array.Empty<string>(), Content.KeystoneCost);
            var (host, runtime, identity, victim) = NativePath(key);
            victim.maxHealth = 100;
            victim.currentHealth = 85;
            var data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, identity, ref data, victim.currentHealth, victim.maxHealth);
            Assert.Equal(85, data.currentAmount, 4);                                 // 命中直前 85% < 90% → −15%
            victim.currentHealth = 95;
            data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, identity, ref data, victim.currentHealth, victim.maxHealth);
            Assert.Equal(100, data.currentAmount, 4);
            data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, identity, ref data);               // no health fact → unmodified
            Assert.Equal(100, data.currentAmount, 4);
        }

        [Fact]
        public void Native_damage_path_reduces_owned_summon_packets_only()
        {
            var key = StarClusters.ManifestKeystone("Hero_Nachia", "h.nachia.key2", Array.Empty<string>(), Array.Empty<AuthoredKeystoneSpec>(),
                new[] { Downside(KeystoneSourceKind.OwnedSummon) }, Array.Empty<string>(), Content.KeystoneCost);
            var (host, runtime, _, victim) = NativePath(key);
            var summon = host.Activation(runtime.Hero, "St_Q_GoldenBurst", NativePayloadKind.SummonAttack);
            var data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, summon, ref data);
            Assert.Equal(85, data.currentAmount, 4);
            var memory = host.Activation(runtime.Hero, "St_Q_GoldenBurst");
            data = new DamageData(100);
            host.NativeKeyDamage(runtime, victim, memory, ref data);
            Assert.Equal(100, data.currentAmount, 4);
        }

        private static (HostAuthority Host, HostAuthority.HeroRuntime Runtime, MemoryActivationIdentity Identity, Entity Victim)
            NativePath(KeystoneDefinition key)
        {
            var build = new Build { SelectedKeystone = key };
            build.Powers[Power.Retaliation] = 25;                                    // what the retained node adds once
            var hero = new Hero();
            hero.Skill.Skills[HeroSkillLocation.Q] = new St_Q_GoldenBurst { owner = hero };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, HeroKey = key.KeystoneId == "h.nachia.key2" ? "Hero_Nachia" : Hero,
                Powers = new PowerRuntime(build, 0) };
            var host = new HostAuthority();
            Mirror.NetworkServer.active = true; UnityEngine.Time.time = 0;
            DewPlayer.gamePlayers.Clear(); DewPhysics.Entities.Clear();
            NativeAttributedDamagePacket.Current = null; NativeAuthoredKeystonePacketFamily.Current = null;
            host.BindAuthored(runtime, Build.Decode(build.Encode()));
            var victim = new Entity { Relation = EntityRelation.Enemy, maxHealth = 100, currentHealth = 100 };
            return (host, runtime, host.Activation(hero, "St_Q_GoldenBurst"), victim);
        }

        private static Build Select(string hero, string id)
        {
            var profile = Profile.CreateNew(131);
            var state = profile.Hero(hero);
            state.StarXp = StarProgression.TotalXpForPoints(19);
            state.Kills = 600;
            foreach (var node in HeroSigils.TreeFor(hero).Where(t => t.Tier == 1 && !t.IsKeystone))
                for (int rank = 0; rank < node.MaxRank; rank++) Rules.AddTalentRank(profile, hero, node.Id);
            Rules.SetKeystone(profile, hero, id);
            return Build.Compute(profile, hero, 0);
        }
    }
}
