using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using SodRpg.Core.Game;
using UnityEngine;
using Xunit;

namespace SodRpg.Core.Tests
{
    using SodRpg.Mod;

    // 同行の刻印（HostAuthority.CoopKeystones.cs）の駆動ヘルパー。
    // Harmony は実ILを織り込まないため、RequireCoopHook が見るパッチ表へ
    // 各アダプターを事前登録し、本体の内部メソッドを直接呼んで判定を確かめる。
    internal static class CoopKeyTestRuntime
    {
        private static bool _adaptersInstalled;
        internal static void InstallAdapters()
        {
            if (_adaptersInstalled) return;
            _adaptersInstalled = true;
            Hook(typeof(NativeCoopKeystoneLifetime), typeof(Actor), nameof(Actor.ClearPooledEventsAndProcessors));
            Hook(typeof(NativeCoopKeystoneDamage), typeof(Entity), nameof(Entity.ProcessReceivedDamage));
            Hook(typeof(NativeCoopKeystoneHit), typeof(Actor), nameof(Actor.InvokeOnDealDamage));
            Hook(typeof(NativeCoopKeystoneKill), typeof(Actor), nameof(Actor.InvokeOnKill));
            Hook(typeof(NativeCoopKeystoneHeal), typeof(Actor), nameof(Actor.InvokeOnDoHeal));
            Hook(typeof(NativeCoopKeystoneAbility), typeof(Actor), nameof(Actor.InvokeOnAbilityInstanceCreated));
            Hook(typeof(NativeCoopKeystoneCold), typeof(Actor), nameof(Actor.ApplyElemental));
            Hook(typeof(NativeAttributedDamagePacket), typeof(Actor), nameof(Actor.DealDamage));
        }
        // RequireCoopHook はパッチ表から DeclaringType が一致するメソッドを探すだけなので、
        // スタブでは各アダプターの static メソッドを1つ登録すれば十分。
        private static void Hook(Type adapter, Type target, string method)
        {
            var info = AccessTools.Method(target, method) ?? throw new MissingMethodException(target.FullName, method);
            var patch = adapter.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.DeclaringType == adapter);
            if (patch == null) throw new MissingMethodException(adapter.FullName, "static patch");
            Harmony.For(info).Postfixes.Add(new HarmonyPatchEntry(patch));
        }

        internal static HostAuthority NewHost()
        {
            InstallAdapters();
            UnityEngine.Time.time = 100f;
            Mirror.NetworkServer.active = true;
            DewPlayer.gamePlayers.Clear();
            CoopDistanceNative.Clear();
            CoopDistanceNative.Enabled = true;
            Log.Warnings.Clear();
            var host = new HostAuthority();
            HostAuthority.NativeInstance = host;
            typeof(HostAuthority).GetMethod("InitializeKillAssists", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(host, null);
            return host;
        }

        internal static string HeroKeyOf(string keystoneId) => "Hero_" + char.ToUpperInvariant(keystoneId[0]) + keystoneId.Substring(1, keystoneId.IndexOf('.') - 1);

        internal static KeystoneDefinition Definition(CoopKeystoneKind kind)
        {
            string id = CoopKeystones.Id(kind);
            string heroKey = HeroKeyOf(id);
            StarClusters.RegisterGeneratedHero(heroKey);
            Assert.True(StarClusters.TryGetRegisteredTree(heroKey, out var tree), heroKey + " is not generated");
            var definition = tree.First(star => star.Id == id).KeystoneDefinition;
            Assert.NotNull(definition);
            return definition;
        }

        // 刻印を承認済み（ConfigureAuthoredKeystone）の旅人を作り、スキャン対象にも登録する。
        internal static Hero HeroWithKey(HostAuthority host, CoopKeystoneKind kind, Vector3 position)
        {
            var hero = new Hero { maxHealth = 1000, currentHealth = 1000, position = position };
            var build = new Build { SelectedKeystone = Definition(kind) };
            var runtime = new HostAuthority.HeroRuntime { Hero = hero, Powers = new PowerRuntime(build, 0) };
            host.BindAuthored(runtime, build);
            host.ConfigureCoopKeystonesForTest(hero, build);
            host.TestActorManager.allHeroes.Add(hero);
            return hero;
        }

        internal static Hero Ally(HostAuthority host, Vector3 position)
        {
            var ally = new Hero { maxHealth = 1000, currentHealth = 1000, position = position };
            host.TestActorManager.allHeroes.Add(ally);
            return ally;
        }

        internal static Monster Enemy(Vector3 position = default) =>
            new Monster { Relation = EntityRelation.Enemy, owner = new DewPlayer { isHumanPlayer = false }, currentHealth = 500, position = position };

        internal static void TrackVictim(HostAuthority host, Entity entity)
            => typeof(HostAuthority).GetMethod("TrackKillAssistEntity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(host, new object[] { entity });

        // 命中イベント（印・反射・接触記録）。実ゲームでは InvokeOnDealDamage の Prefix が先に走る。
        internal static void Hit(HostAuthority host, Actor attacker, Entity victim, float amount)
        {
            var info = new EventInfoDamage { actor = attacker, victim = victim, amount = amount, damage = new DamageData(amount) { actor = attacker } };
            host.OnCoopKeystoneHit(info);
            attacker.InvokeOnDealDamage(info);
        }

        // 被ダメージ処理（ProcessReceivedDamage の Postfix 相当）。増幅後の値を返す。
        // 増幅は乗算合成なので、1.2f 系の浮動小数端数（120.000008 など）を丸めてから比較する。
        internal static float Received(HostAuthority host, Entity victim, Actor attacker, float amount)
        {
            var damage = new DamageData(amount) { actor = attacker };
            host.ApplyCoopKeystoneDamage(victim, ref damage, attacker);
            return (float)Math.Round(damage.currentAmount, 2);
        }

        internal static void Kill(HostAuthority host, Entity enemy, Hero killer, float at)
        {
            UnityEngine.Time.time = at;
            var kill = new EventInfoKill { actor = killer, victim = enemy };
            enemy.RaiseDeath(kill);
            host.OnCoopKeystoneKill(kill);
            host.DispatchKillAssists(kill);
        }

        internal static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "tools", "star-manifest", "outer.json"))) return dir.FullName;
            throw new InvalidOperationException("tools/star-manifest was not found above " + AppContext.BaseDirectory);
        }

        internal static JsonElement ManifestNode(CoopKeystoneKind kind)
        {
            string id = CoopKeystones.Id(kind);
            string file = HeroKeyOf(id).Substring("Hero_".Length).ToLowerInvariant() + ".json";
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "star-manifest", file)));
            foreach (var star in document.RootElement.GetProperty("stars").EnumerateArray())
                if (star.GetProperty("id").GetString() == id) return star.Clone();
            throw new InvalidOperationException(id + " is not in the manifest");
        }

        internal static Dictionary<string, float> BalanceEffects()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "balance", "stars.json")));
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.GetProperty("effects").EnumerateObject())
                result[entry.Name] = entry.Value.GetSingle();
            return result;
        }
    }

    /// <summary>
    /// 同行の刻印9個（HostAuthority.CoopKeystones.cs）。manifest・stars.json との一致と、
    /// 代表刻印の判定ロジック（距離係数・有効時間・内部CD・1回限り）までを確かめる。
    /// </summary>
    [Collection("Generated hero registry")]
    public class CoopKeystonesNativeTests
    {
        private static readonly CoopKeystoneKind[] AllKinds = (CoopKeystoneKind[])Enum.GetValues(typeof(CoopKeystoneKind));

        [Fact]
        public void All_nine_coop_keystones_match_the_manifest_placement_and_cost()
        {
            Assert.Equal(9, AllKinds.Length);
            foreach (var kind in AllKinds)
            {
                string id = CoopKeystones.Id(kind);
                string heroKey = CoopKeyTestRuntime.HeroKeyOf(id);
                var node = CoopKeyTestRuntime.ManifestNode(kind);
                StarClusters.RegisterGeneratedHero(heroKey);
                Assert.True(StarClusters.TryGetRegisteredTree(heroKey, out var tree), heroKey);

                var star = Assert.Single(tree.Where(t => t.Id == id));
                Assert.True(star.IsKeystone, id + " is not a keystone");
                Assert.Equal("keystone", node.GetProperty("region").GetString());
                Assert.Equal(id.Substring(0, id.IndexOf('.')) + ".key", node.GetProperty("cluster").GetString());
                Assert.Equal(node.GetProperty("maxRank").GetInt32(), star.MaxRank);
                Assert.Equal(1, star.MaxRank);
                Assert.Equal(node.GetProperty("rankCost").GetInt32(), star.KeystoneCost);
                // 生成刻印の cost は manifest 側（全キー rankCost=1）。Content.KeystoneCost(3) は素体刻印の定数で、ここでは比較しない。

                var definition = star.KeystoneDefinition;
                Assert.NotNull(definition);
                Assert.Equal(id, definition.KeystoneId);
                var requires = node.GetProperty("requires").EnumerateArray().Select(r => r.GetString()).ToArray();
                Assert.Equal(requires, definition.Prerequisites);
                Assert.Contains(node.GetProperty("anchor").GetString(), tree.Select(t => t.Id)); // 配置規則: anchor は実在する星
                Assert.Equal(node.GetProperty("nameJa").GetString(), star.Name.Ja);
                Assert.Equal(node.GetProperty("nameEn").GetString(), star.Name.En);
                Assert.True(CoopKeystones.TryKind(id, out var parsed) && parsed == kind, id + " maps back to its kind");
            }
        }

        [Fact]
        public void Coop_keystones_carry_their_stars_json_values_and_describe_both_languages()
        {
            var balance = CoopKeyTestRuntime.BalanceEffects();
            bool previous = Loc.Japanese;
            try
            {
                foreach (var kind in AllKinds)
                {
                    string id = CoopKeystones.Id(kind);
                    var node = CoopKeyTestRuntime.ManifestNode(kind);
                    var scopes = node.GetProperty("keystone").GetProperty("upsideSpec").EnumerateArray()
                        .Select((spec, index) => (Scope: spec.GetProperty("scope").GetString(), Index: index))
                        .Where(entry => entry.Scope != null).ToList();
                    Assert.NotEmpty(scopes);

                    var definition = CoopKeyTestRuntime.Definition(kind);
                    foreach (var (scope, index) in scopes)
                    {
                        string valueRef = id + "/keystone/upsideSpec/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/to";
                        Assert.True(balance.TryGetValue(valueRef, out float expected), valueRef + " is missing from stars.json");
                        Assert.Equal(expected, CoopKeystones.Value(definition, scope), 3);
                    }
                    // Describe は全 required 値を読む。欠損・重複があれば例外になる。
                    Loc.Japanese = true;
                    string japanese = CoopKeystones.Describe(definition);
                    Loc.Japanese = false;
                    string english = CoopKeystones.Describe(definition);
                    Assert.False(string.IsNullOrWhiteSpace(japanese));
                    Assert.False(string.IsNullOrWhiteSpace(english));
                    Assert.Contains(CoopKeystones.Value(definition, scopes[0].Scope).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), japanese);
                }
            }
            finally { Loc.Japanese = previous; }
        }

        [Fact]
        public void Solo_run_gets_no_coop_keystone_effects()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var lacerta = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.SharedSights, new Vector3());
            var enemy = CoopKeyTestRuntime.Enemy();
            var memory = new SkillTrigger { owner = lacerta, parentActor = lacerta };
            // 味方がいないので、自分の命中でも印は付かず増幅も起きない。
            CoopKeyTestRuntime.Hit(host, lacerta, enemy, 40);
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, lacerta, 100));
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, memory, 100));
            CoopKeyTestRuntime.Kill(host, enemy, lacerta, 101); // 印がないので記憶強化も付かない
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, memory, 100));

            // 回復系も同様（自分への回復では黄金の信頼が始まらない）。
            var soloHost = CoopKeyTestRuntime.NewHost();
            var aurena = CoopKeyTestRuntime.HeroWithKey(soloHost, CoopKeystoneKind.GoldenTrust, new Vector3());
            aurena.currentHealth = 500;
            soloHost.OnCoopKeystoneHeal(new EventInfoHeal { actor = aurena, target = aurena, amount = 30 });
            var soloEnemy = CoopKeyTestRuntime.Enemy();
            Assert.Equal(100, CoopKeyTestRuntime.Received(soloHost, soloEnemy, aurena, 100));
            Assert.Equal(500, aurena.currentHealth);
        }

        [Fact]
        public void Shared_sights_mark_and_amp_follow_the_distance_coefficient()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var lacerta = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.SharedSights, new Vector3());
            // 距離係数（10mまで1、18mで0）ごとに別の味方と敵を使い、追跡履歴の猶予が混ざらないようにする。
            foreach (var (distance, expected) in new[] { (5f, 120f), (14f, 110f) })
            {
                var ally = CoopKeyTestRuntime.Ally(host, new Vector3(distance, 0, 0));
                var enemy = CoopKeyTestRuntime.Enemy();
                CoopKeyTestRuntime.Hit(host, lacerta, enemy, 40); // 係数0より大なら印が付く
                Assert.Equal(expected, CoopKeyTestRuntime.Received(host, enemy, ally, 100)); // +20% × 係数
            }
            var farAlly = CoopKeyTestRuntime.Ally(host, new Vector3(20, 0, 0));
            var farEnemy = CoopKeyTestRuntime.Enemy();
            CoopKeyTestRuntime.Hit(host, lacerta, farEnemy, 40); // 係数0では印自体が付かない
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, farEnemy, farAlly, 100));
        }

        [Fact]
        public void Shared_sights_mark_expires_and_empowers_one_memory_hit_after_an_ally_kill()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var lacerta = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.SharedSights, new Vector3());
            var ally = CoopKeyTestRuntime.Ally(host, new Vector3(5, 0, 0));
            var enemy = CoopKeyTestRuntime.Enemy();
            var memory = new SkillTrigger { owner = lacerta, parentActor = lacerta };

            CoopKeyTestRuntime.Hit(host, lacerta, enemy, 40); // 印4秒
            Assert.Equal(120, CoopKeyTestRuntime.Received(host, enemy, ally, 100));
            UnityEngine.Time.time = 104.2f; // 印の4秒経過
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, ally, 100));

            UnityEngine.Time.time = 105f;
            CoopKeyTestRuntime.Hit(host, lacerta, enemy, 40); // 印し直し
            CoopKeyTestRuntime.Kill(host, enemy, ally, 106);  // 味方が印の敵を撃破
            var next = CoopKeyTestRuntime.Enemy();
            Assert.Equal(150, CoopKeyTestRuntime.Received(host, next, memory, 100)); // 次の記憶1回 +50%
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, next, memory, 100)); // 1回だけ
        }

        [Fact]
        public void Parry_signal_empowers_one_close_ally_hit_within_two_seconds()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var mist = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.ParrySignal, new Vector3());
            var close = CoopKeyTestRuntime.Ally(host, new Vector3(6, 0, 0));  // 半径8mの内側
            var distant = CoopKeyTestRuntime.Ally(host, new Vector3(9, 0, 0)); // 半径の外
            var enemy = CoopKeyTestRuntime.Enemy();

            host.OnCoopKeystoneAbility(new EventInfoAbilityInstance
                { instance = new Se_R_Parry_End { victim = mist, blockedDamage = 50, parentActor = mist }, actor = mist });
            UnityEngine.Time.time = 101f;
            Assert.Equal(140, CoopKeyTestRuntime.Received(host, enemy, close, 100));    // 次の1命中 +40%
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, close, 100));    // 使用済み
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, distant, 100));  // 半径外は信号なし

            host.OnCoopKeystoneAbility(new EventInfoAbilityInstance
                { instance = new Se_R_Parry_End { victim = mist, blockedDamage = 50, parentActor = mist }, actor = mist });
            UnityEngine.Time.time = 104.1f; // 有効時間2秒の経過
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, close, 100));
        }

        [Fact]
        public void Golden_trust_ampifies_allies_and_heals_the_holder_for_three_seconds()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var aurena = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.GoldenTrust, new Vector3());
            aurena.currentHealth = 500;
            var ally = CoopKeyTestRuntime.Ally(host, new Vector3(4, 0, 0));
            var enemy = CoopKeyTestRuntime.Enemy();

            host.OnCoopKeystoneHeal(new EventInfoHeal { actor = aurena, target = ally, amount = 30 }); // 3秒
            UnityEngine.Time.time = 101f;
            CoopKeyTestRuntime.Hit(host, ally, enemy, 100); // その味方の与ダメージの10%で自分を回復
            Assert.Equal(510, aurena.currentHealth);
            Assert.Equal(115, CoopKeyTestRuntime.Received(host, enemy, ally, 100)); // +15%

            UnityEngine.Time.time = 103.5f; // 3秒の経過
            CoopKeyTestRuntime.Hit(host, ally, enemy, 100);
            Assert.Equal(510, aurena.currentHealth); // 反射なし
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, ally, 100));
        }

        [Fact]
        public void Shared_crystal_field_grants_haste_and_amp_inside_the_field()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var bismuth = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.SharedCrystalField, new Vector3());
            var ally = CoopKeyTestRuntime.Ally(host, new Vector3(2, 0, 0));
            var enemy = CoopKeyTestRuntime.Enemy();
            var field = new Ai_QR_InfernalTales { parentActor = bismuth, range = new DewCollider { CenterX = 0, CenterY = 0, Radius = 3 } };
            field.info.caster = bismuth;

            host.OnCoopKeystoneAbility(new EventInfoAbilityInstance { instance = field, actor = bismuth });
            host.ScanCoopKeystonesForTest();
            var bonus = Assert.Single(ally.Status.AttachedBonuses);
            Assert.Equal(10, bonus.abilityHasteFlat); // 領域内の味方へ加速+10
            Assert.Equal(110, CoopKeyTestRuntime.Received(host, enemy, ally, 100)); // 与ダメージ+10%

            UnityEngine.Time.time = 101f;
            CoopKeyTestRuntime.Hit(host, ally, enemy, 100); // 領域内の味方が当てた敵を3秒マーク
            Assert.Equal(110, CoopKeyTestRuntime.Received(host, enemy, bismuth, 100)); // 自分のダメージ+10%
            UnityEngine.Time.time = 104.5f;
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, bismuth, 100)); // マーク失効

            ally.position = new Vector3(10, 0, 0); // 領域の外へ
            host.ScanCoopKeystonesForTest();
            Assert.Empty(ally.Status.AttachedBonuses);
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, ally, 100));
        }

        [Fact]
        public void Shadow_cue_requires_absence_crits_once_and_cools_down_for_two_seconds()
        {
            var host = CoopKeyTestRuntime.NewHost();
            var husk = CoopKeyTestRuntime.HeroWithKey(host, CoopKeystoneKind.ShadowCue, new Vector3());
            var ally = CoopKeyTestRuntime.Ally(host, new Vector3(5, 0, 0));
            var enemy = CoopKeyTestRuntime.Enemy();
            CoopKeyTestRuntime.TrackVictim(host, enemy);
            var identity = new SkillTrigger { owner = husk, parentActor = husk };
            husk.Skill.EquipSkill(HeroSkillLocation.Identity, identity);
            var movement = new SkillTrigger { owner = husk, parentActor = husk };
            husk.Skill.EquipSkill(HeroSkillLocation.Movement, movement);
            husk.Status.critChance = 0.25f;
            husk.Status.critAmp = 0.5f;

            CoopKeyTestRuntime.Hit(host, ally, enemy, 40); // 3秒不在 → 合図
            Assert.Equal(1, movement.currentCharges[0]); // 移動チャージ+1
            float crit = CoopKeyTestRuntime.Received(host, enemy, identity, 100);
            Assert.Equal(300, crit); // 確定会心 ×2 ×(1+critAmp0.5)
            Assert.Equal(100, CoopKeyTestRuntime.Received(host, enemy, identity, 100)); // 1回だけ

            UnityEngine.Time.time = 101f;
            CoopKeyTestRuntime.Hit(host, ally, enemy, 40); // 内部CD2秒中 → 合図なし
            Assert.Equal(1, movement.currentCharges[0]);

            UnityEngine.Time.time = 105f;
            CoopKeyTestRuntime.Hit(host, husk, enemy, 40); // 自分が当てる
            CoopKeyTestRuntime.Hit(host, ally, enemy, 40); // 3秒以内の接触あり → 合図なし
            Assert.Equal(1, movement.currentCharges[0]);

            UnityEngine.Time.time = 108.2f; // 自分の接触から3秒経過
            CoopKeyTestRuntime.Hit(host, ally, enemy, 40);
            Assert.Equal(2, movement.currentCharges[0]);
        }
    }
}
