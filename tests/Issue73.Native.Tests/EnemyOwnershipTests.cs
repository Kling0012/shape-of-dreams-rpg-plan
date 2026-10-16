using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>敵担当制（HostAuthority.EnemyOwnership.cs / EnemyOwnershipSync.cs）のホスト側 E2E:
    /// 割り当て・強さ・保護と手伝い・流れ弾・付け替え・同期。実ソースをそのままリンクし、
    /// 境界（NativeBoundary.cs / EnemyOwnershipBoundary.cs）は本体の形だけを用意する。</summary>
    public sealed class EnemyOwnershipTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public EnemyOwnershipTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        private static void ResetStatics()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "ownership" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager { currentZoneIndex = 0 };
            NetworkedManagerBase<ActorManager>.softInstance = null;
            HostAuthority.NativeInstance = null;
            Time.time = 1f;
            Time.frameCount = 1;
            Log.Warnings.Clear();
        }

        // ---- 準備 ----

        private static DewPlayer Player(uint netId)
        {
            var hero = new Hero { netId = netId * 100 };
            var player = new DewPlayer { netId = netId, guid = "guid-" + netId, connectionToClient = new NetworkConnectionToClient() };
            hero.owner = player;
            player.hero = hero;
            DewPlayer.gamePlayers.Add(player);
            return player;
        }

        private static HostAuthority Host(out Actor actor)
        {
            actor = new Actor();
            var host = new HostAuthority();
            host.ConfigureEnemyOwnership(true); // 協力かつ人間2人以上で設定ON（F6）を前提とする
            HostAuthority.NativeInstance = host;
            Set(host, "_registeredOn", actor);
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            return host;
        }

        private static Entity Spawn(HostAuthority host, uint netId, Entity enemy = null)
        {
            var entity = enemy ?? new Entity();
            if (netId != 0) entity.netId = netId;
            if (entity.owner == null && !(entity is Hero)) entity.owner = new DewPlayer { isHumanPlayer = false }; // 湧き元は人間ではない
            host.PrepareEnemyOwnership(entity);
            host.FinalizeEnemyOwnership(entity);
            return entity;
        }

        private static void BuildFor(HostAuthority host, DewPlayer player, int dreamLevel, int spentStarPoints)
        {
            var builds = (Dictionary<DewPlayer, HostAuthority.ReceivedBuild>)Get(host, "_builds");
            builds[player] = new HostAuthority.ReceivedBuild { Build = new Build { DreamLevel = dreamLevel, SpentStarPoints = spentStarPoints } };
        }

        // HostAuthority.OwnershipPressure と同じ式（部屋と同じ深度・道標・Infinity の付加）。
        private static DreamPressure PressureOf(HostAuthority host, DewPlayer player)
        {
            var room = (DreamPressure)Get(host, "_pressure");
            var builds = (Dictionary<DewPlayer, HostAuthority.ReceivedBuild>)Get(host, "_builds");
            if (!builds.TryGetValue(player, out var received) || received.Build == null) return room;
            return DreamPressure.ForPlayer(received.Build.DreamLevel, received.Build.SpentStarPoints)
                .WithRunModifiers(room.Depth, room.WaypointMultiplier)
                .WithInfinityPressure(room.InfinityStage);
        }

        private static object State(HostAuthority host, Entity enemy) => ((IDictionary)Get(host, "_enemyOwnership"))[enemy];
        private static DewPlayer OwnerOf(HostAuthority host, Entity enemy) => (DewPlayer)Get(State(host, enemy), "Owner");
        private static bool ExcludedOf(HostAuthority host, Entity enemy) => (bool)Get(State(host, enemy), "Excluded");

        private static float RecalcMaxHealth(Entity entity)
        {
            entity.Status.MarkStatsDirty();
            entity.Status.CalculateStatsIfDirty();
            return entity.Status.maxHealth;
        }

        private static float TakenBy(HostAuthority host, Entity enemy, Actor attacker)
        {
            var damage = new DamageData(100f) { actor = attacker };
            host.ApplyOwnershipReceivedDamage(enemy, attacker, ref damage);
            return damage.amount;
        }

        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static object Get(object target, string name) => ObjectType(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return ObjectType(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        // ---- 割り当て ----

        [Fact]
        public void Assignment_picks_the_least_loaded_player_and_rotates_among_equal_counts()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);
            // 5体湧かせる: 担当数が最少の人へ。同数は前回担当者より後ろで最も若い番号へ。
            var owners = new[] { 1001u, 1002u, 1003u, 1004u, 1005u }.Select(id => OwnerOf(host, Spawn(host, id))).ToArray();
            Assert.Equal(new[] { a, b, c, a, b }, owners);
            Assert.All(owners, owner => Assert.NotNull(owner));
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Bosses_minibosses_and_their_children_stay_shared_but_owned_children_inherit_the_parent()
        {
            var host = Host(out _);
            var a = Player(5);
            Player(9);
            var boss = Spawn(host, 100, new Monster { type = Monster.MonsterType.Boss });
            var miniboss = Spawn(host, 101, new Monster { type = Monster.MonsterType.MiniBoss });
            Assert.True(ExcludedOf(host, boss), "trueのボスは共有のまま");
            Assert.Null(OwnerOf(host, boss));
            Assert.True(ExcludedOf(host, miniboss), "ミニボスも共有のまま");
            Assert.Null(OwnerOf(host, miniboss));

            var bossChild = Spawn(host, 0, new Entity { netId = 102, parentActor = boss });
            Assert.True(ExcludedOf(host, bossChild), "ボスの子も共有のまま");
            Assert.Null(OwnerOf(host, bossChild));

            var owned = Spawn(host, 103);
            var ownedChild = Spawn(host, 0, new Entity { netId = 104, parentActor = owned });
            Assert.False(ExcludedOf(host, ownedChild));
            Assert.Same(OwnerOf(host, owned), OwnerOf(host, ownedChild)); // 担当の敵の子は親の担当を継ぐ
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Ownership_stays_off_with_a_single_human_or_the_setting_disabled()
        {
            var host = Host(out _);
            Player(5); // 一人だけ
            Spawn(host, 100);
            Assert.Empty((ICollection)Get(host, "_enemyOwnership")); // 人間1人では何もしない

            var b = Player(9);
            var enemy = Spawn(host, 101);
            Assert.NotNull(OwnerOf(host, enemy));
            enemy.Status.SetHealth(enemy.Status.maxHealth * 0.5f);

            host.ConfigureEnemyOwnership(false); // 設定OFF
            Spawn(host, 102);
            var map = (IDictionary)Get(host, "_enemyOwnership");
            Assert.Single(map); // OFF後の湧きには担当を付けない
            Call(host, "ScanEnemyOwnership");
            Assert.Null(OwnerOf(host, enemy)); // 既存の敵は担当を外れ…
            Assert.Equal(100f, enemy.Status.baseMaxHealth, 3);
            RecalcMaxHealth(enemy);
            Assert.Equal(enemy.Status.baseMaxHealth, enemy.Status.maxHealth, 3); // …部屋の圧（ここでは倍率1）へ戻る
            Assert.Empty(Log.Warnings);
        }

        // ---- 強さ ----

        [Fact]
        public void Owned_enemy_pressure_replaces_the_room_average_instead_of_stacking()
        {
            var host = Host(out _);
            var room = DreamPressure.ForPlayer(12, 30); // 部屋の平均圧（最も高い）
            Set(host, "_pressure", room);
            var a = Player(5);
            BuildFor(host, a, 2, 0); // 個人圧は低め
            var b = Player(9);
            BuildFor(host, b, 4, 10);
            var c = Player(13); // ビルド未受信

            var personal = Spawn(host, 100);   // → a
            var veteran = Spawn(host, 101);    // → b
            var unannounced = Spawn(host, 102); // → c
            var boss = Spawn(host, 103, new Entity { anyBoss = true }); // 担当なし

            foreach (var enemy in new[] { personal, veteran, unannounced, boss }) enemy.Status.baseMaxHealth = 200f;
            Assert.Equal(200f * (float)PressureOf(host, a).HealthMultiplier, RecalcMaxHealth(personal), 2);
            Assert.Equal(200f * (float)PressureOf(host, b).HealthMultiplier, RecalcMaxHealth(veteran), 2);
            Assert.Equal(200f * (float)room.HealthMultiplier, RecalcMaxHealth(unannounced), 2); // 未受信Buildは部屋の平均で代替
            Assert.Equal(200f, RecalcMaxHealth(boss), 2); // 担当なしは補正なし
            // 部屋の平均との二重掛けではない（room × 個人 になっていないことの明示）。
            Assert.NotEqual(200f * (float)(room.HealthMultiplier * PressureOf(host, b).HealthMultiplier), personal.Status.maxHealth, 2);

            // 与ダメも担当者の個人圧に差し替わる。
            var damage = new DamageData(100f);
            foreach (var processor in veteran.dealtDamageProcessor.InOrder()) processor(ref damage, veteran, personal);
            Assert.Equal(100f * (float)PressureOf(host, b).DamageMultiplier, damage.amount, 2);

            // Monster 経路（本体 HostAuthority.cs:548 と同じ呼び出し）も個人圧へ置き換わる。
            var monster = Spawn(host, 104, new Monster { netId = 104 });
            var method = host.GetType().GetMethod("OverrideOwnershipHealth", Hidden, new[] { typeof(Monster), typeof(float).MakeByRefType() });
            var args = new object[] { monster, (float)room.HealthMultiplier };
            method.Invoke(host, args);
            Assert.Equal((float)PressureOf(host, OwnerOf(host, monster)).HealthMultiplier, (float)args[1], 5);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Owner_reassignment_switches_pressure_and_preserves_the_health_fraction()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 2, 0);
            var b = Player(9);
            BuildFor(host, b, 8, 40);
            var c = Player(13);
            var enemy = Spawn(host, 100); // → a
            enemy.Status.baseMaxHealth = 300f;
            Assert.Equal(300f * (float)PressureOf(host, a).HealthMultiplier, RecalcMaxHealth(enemy), 2);
            enemy.Status.SetHealth(enemy.Status.maxHealth * 0.4f); // 4割まで削れた状態

            a.connectionToClient = null; // a が切断（b・c は残る）
            Time.time = 2f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(b, OwnerOf(host, enemy)); // 即時に残りの人へ
            Assert.Equal(300f * (float)PressureOf(host, b).HealthMultiplier, enemy.Status.maxHealth, 2); // 新しい担当者の強さへ
            Assert.Equal(0.4, enemy.Status.currentHealth / enemy.Status.maxHealth, 3); // HP割合は保たれる
            Assert.Empty(Log.Warnings);
        }

        // ---- 保護と手伝い ----

        [Fact]
        public void Helpers_fight_at_half_strength_scaled_by_the_owner_pressure_ratio()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 2, 0); // 担当者は新人
            var b = Player(9);
            BuildFor(host, b, 8, 40); // 手伝う人はベテラン
            var enemy = Spawn(host, 100); // → a
            enemy.Status.baseMaxHealth = 1000f;
            RecalcMaxHealth(enemy);
            enemy.Status.SetHealth(enemy.Status.maxHealth); // 満タン（下限の関与なし）

            float pA = (float)PressureOf(host, a).HealthMultiplier, pB = (float)PressureOf(host, b).HealthMultiplier;
            Assert.True(pA < pB, "前提: 担当者の個人圧 < 手伝う人");
            Assert.Equal(100f * CoopBalance.Ownership.HelpFactor * (pA / pB), TakenBy(host, enemy, b.hero), 2); // 0.5 × min(1, P担当/P攻撃)
            var equal = Player(13);
            BuildFor(host, equal, 2, 0); // 担当者と同じ育ち具合
            Assert.Equal(100f * CoopBalance.Ownership.HelpFactor, TakenBy(host, enemy, equal.hero), 2); // 同圧なら半分
            Assert.Equal(100f, TakenBy(host, enemy, a.hero), 2); // 担当者自身は対象外

            // 召喚体の攻撃は親鎖で人間に帰属する。
            var summon = new Summon { hero = b.hero };
            Assert.Equal(100f * CoopBalance.Ownership.HelpFactor * (pA / pB), TakenBy(host, enemy, summon), 2);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Others_cannot_chip_an_owned_enemy_below_the_thirty_percent_floor()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 3, 10);
            var b = Player(9);
            BuildFor(host, b, 3, 10); // 同圧（手伝い係数はちょうど0.5）
            var enemy = Spawn(host, 100); // → a
            enemy.Status.baseMaxHealth = 1000f;
            RecalcMaxHealth(enemy);
            enemy.Status.SetHealth(enemy.Status.maxHealth * 0.31f); // 下限すれすれまで削れた

            float floor = (float)Math.Floor(enemy.Status.maxHealth * CoopBalance.Ownership.Floor);
            float allowed = enemy.Status.currentHealth - floor;
            Assert.True(allowed > 0f && allowed < 50f, "前提: 最初の一手で下限に届く量");
            float first = TakenBy(host, enemy, b.hero);
            Assert.Equal(allowed, first, 2); // 下限を超える分は捨てられる
            enemy.Status.SetHealth(enemy.Status.currentHealth - first);
            Assert.Equal(floor, enemy.Status.currentHealth, 2);
            Assert.Equal(0f, TakenBy(host, enemy, b.hero), 2); // 下限では他人のダメージは通らない
            Assert.Equal(100f, TakenBy(host, enemy, a.hero), 2); // 担当者は下限を超えて削れる
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Protection_needs_an_engaged_and_healthy_owner()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 3, 10);
            var b = Player(9);
            BuildFor(host, b, 3, 10);
            var enemy = Spawn(host, 100); // → a
            enemy.Status.baseMaxHealth = 1000f;
            RecalcMaxHealth(enemy);
            enemy.Status.SetHealth(enemy.Status.maxHealth);

            // 湧いて8秒以内は交戦とみなす。
            Time.time = 2f;
            Assert.Equal(50f, TakenBy(host, enemy, b.hero), 2);
            Time.time = 20f; // 猶予切れ・無交戦
            Assert.Equal(100f, TakenBy(host, enemy, b.hero), 2);

            // 担当者の命中から6秒以内は交戦中。
            enemy.RaiseEntityEventOnTakeDamage(new EventInfoDamage { actor = a.hero, damage = new DamageData(10f) });
            Time.time = 25f;
            Assert.Equal(50f, TakenBy(host, enemy, b.hero), 2);
            Time.time = 31.5f; // 6秒を過ぎた
            Assert.Equal(100f, TakenBy(host, enemy, b.hero), 2);

            // 敵が担当者を狙っていても交戦中。
            enemy.AI = new EntityAI { entity = enemy };
            enemy.AI.context.targetEnemy = a.hero;
            Time.time = 33f;
            Assert.Equal(50f, TakenBy(host, enemy, b.hero), 2);

            // 担当者のHPが35%未満なら保護しない（味方が助けに入れる）。
            a.hero.Status.SetHealth(a.hero.Status.maxHealth * 0.2f);
            Time.time = 34f;
            Assert.Equal(100f, TakenBy(host, enemy, b.hero), 2);
            Assert.Empty(Log.Warnings);
        }

        // ---- 流れ弾 ----

        [Fact]
        public void Stray_hits_on_non_owners_scale_with_the_victims_own_pressure()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 8, 40); // 敵の担当者はベテラン
            var b = Player(9);
            BuildFor(host, b, 2, 0); // 巻き込まれるのは新人
            var enemy = Spawn(host, 100); // → a

            float pA = (float)PressureOf(host, a).HealthMultiplier, pB = (float)PressureOf(host, b).HealthMultiplier;
            var ontoNewcomer = new DamageData(100f) { actor = enemy };
            host.ApplyOwnershipReceivedDamage(b.hero, enemy, ref ontoNewcomer);
            Assert.Equal(100f * (pB / pA), ontoNewcomer.amount, 2); // min(1, P被弾者/P担当)

            var ontoOwner = new DamageData(100f) { actor = enemy };
            host.ApplyOwnershipReceivedDamage(a.hero, enemy, ref ontoOwner);
            Assert.Equal(100f, ontoOwner.amount, 2); // 担当者本人は無倍率

            Time.time = 20f; // 保護の有無に関係なく常に（猶予切れ後も同じ倍率）。
            var afterGrace = new DamageData(100f) { actor = enemy };
            host.ApplyOwnershipReceivedDamage(b.hero, enemy, ref afterGrace);
            Assert.Equal(100f * (pB / pA), afterGrace.amount, 2);

            // 担当敵の子（召喚体）の攻撃も親の担当で扱う。
            var summon = (Summon)Spawn(host, 101, new Summon { netId = 101, parentActor = enemy });
            Assert.Same(a, OwnerOf(host, summon));
            var fromSummon = new DamageData(100f) { actor = summon };
            host.ApplyOwnershipReceivedDamage(b.hero, summon, ref fromSummon);
            Assert.Equal(100f * (pB / pA), fromSummon.amount, 2);
            Assert.Empty(Log.Warnings);
        }

        // ---- 付け替え ----

        [Fact]
        public void A_disconnected_owner_is_reassigned_immediately()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);
            var enemy = Spawn(host, 100); // → a
            Time.time = 1.5f;
            a.connectionToClient = null; // 切断（待ち時間なしで付け替え）
            Call(host, "ScanEnemyOwnership");
            Assert.Same(b, OwnerOf(host, enemy)); // 残った人へ1の規則で
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void A_knocked_out_owner_is_replaced_after_the_down_delay_by_the_recent_fighter()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);
            var enemy = Spawn(host, 100); // → a
            enemy.RaiseEntityEventOnTakeDamage(new EventInfoDamage { actor = c.hero, damage = new DamageData(10f) }); // c が交戦

            a.hero.isKnockedOut = true;
            Time.time = 4f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(a, OwnerOf(host, enemy)); // 3秒未満では替えない
            Time.time = 7f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(c, OwnerOf(host, enemy)); // 3秒で直近の交戦者へ
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void An_owner_far_away_is_replaced_after_the_away_timeout()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);
            var enemy = Spawn(host, 100); // → a
            a.hero.position = new Vector3(5f, 0f, 0f);
            Time.time = 3f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(a, OwnerOf(host, enemy)); // 30m 内なら替えない

            a.hero.position = new Vector3(40f, 0f, 0f); // 30m 以上離れた
            Time.time = 4f;
            Call(host, "ScanEnemyOwnership"); // 離脱の計測開始
            Time.time = 9.9f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(a, OwnerOf(host, enemy)); // 6秒未満では替えない
            Time.time = 10.1f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(b, OwnerOf(host, enemy)); // 6秒で最も近い生存者へ
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void An_idle_owner_keeps_the_enemy_until_someone_else_engages_it()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);
            var enemy = Spawn(host, 100); // → a

            Time.time = 20f; // 誰も交戦していない
            Call(host, "ScanEnemyOwnership");
            Assert.Same(a, OwnerOf(host, enemy)); // 他の交戦者がいなければ替えない

            enemy.RaiseEntityEventOnTakeDamage(new EventInfoDamage { actor = c.hero, damage = new DamageData(10f) });
            Time.time = 25f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(c, OwnerOf(host, enemy)); // 10秒無交戦かつ他の人が交戦中なら直近の交戦者へ
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void The_same_enemy_is_not_reassigned_more_than_once_per_five_seconds()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);
            var enemy = Spawn(host, 100); // → a
            enemy.RaiseEntityEventOnTakeDamage(new EventInfoDamage { actor = c.hero, damage = new DamageData(10f) });

            a.hero.isKnockedOut = true;
            Time.time = 2f;
            Call(host, "ScanEnemyOwnership");
            Time.time = 5f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(c, OwnerOf(host, enemy)); // 1回目の付け替え（t=5）

            c.hero.isKnockedOut = true; // 直後に新しい担当者も倒れた
            Time.time = 7f;
            Call(host, "ScanEnemyOwnership"); // 計測開始
            Time.time = 9.9f;
            Call(host, "ScanEnemyOwnership");
            Assert.Same(c, OwnerOf(host, enemy)); // 3秒経っても5秒のクールダウン中
            Time.time = 10f; // t=5 から5秒・KOから3秒
            Call(host, "ScanEnemyOwnership");
            Assert.Same(b, OwnerOf(host, enemy)); // クールダウン明けで最も近い生存者へ
            Assert.Empty(Log.Warnings);
        }

        // ---- 同期 ----

        private static (HostAuthority Host, Actor Actor, HostAuthority.MonsterRuntime Runtime, Monster Monster) SyncRig(DewPlayer owner)
        {
            var host = Host(out var actor);
            var monster = new Monster { netId = 501 };
            var runtime = new HostAuthority.MonsterRuntime { Monster = monster };
            Set(host, "_monsters", new Dictionary<Monster, HostAuthority.MonsterRuntime> { [monster] = runtime });
            Spawn(host, 0, monster);
            Assert.Same(owner, OwnerOf(host, monster));
            return (host, actor, runtime, monster);
        }

        [Fact]
        public void Ownership_rides_classification_deltas_with_monotonic_revisions()
        {
            var a = Player(5);
            Player(9);
            Player(13);
            var (host, actor, runtime, _) = SyncRig(a);
            Time.time = 2f; // 湧き直後（保護中）

            Call(host, "SendMonsterClassification", runtime, null);
            Call(host, "FlushMonsterSync", 32);
            var first = actor.Sent.Select(s => s.Message).OfType<DreamforgeNightmareMsg>().Single();
            Assert.Equal(a.guid, first.ownerParticipantId);
            Assert.Equal(1, first.ownerPlayerSlot); // gamePlayers 順の色番号
            Assert.True(first.ownershipProtected);
            Assert.Equal(1L, first.ownershipRevision);

            int sent = actor.Sent.Count;
            Call(host, "SendMonsterClassification", runtime, null); // 変化なし
            Call(host, "FlushMonsterSync", 32);
            Assert.Equal(sent, actor.Sent.Count); // 同じ担当・保護状態は再送しない

            Time.time = 20f; // 猶予切れ・無交戦で保護が外れる（共有に戻る）
            Call(host, "SendMonsterClassification", runtime, null);
            Call(host, "FlushMonsterSync", 32);
            var second = actor.Sent.Select(s => s.Message).OfType<DreamforgeNightmareMsg>().Last();
            Assert.Equal(a.guid, second.ownerParticipantId);
            Assert.False(second.ownershipProtected);
            Assert.True(second.ownershipRevision > first.ownershipRevision, "リビジョンは単調増加");
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void A_late_joiner_receives_the_current_ownership_state()
        {
            var a = Player(5);
            Player(9);
            var c = Player(13);
            var (host, actor, runtime, _) = SyncRig(a);
            Time.time = 2f;

            Call(host, "SendMonsterClassification", runtime, c); // 途中参加の再同期経路
            var sent = Assert.Single(actor.Sent, s => s.Target == c);
            var message = Assert.IsType<DreamforgeNightmareMsg>(sent.Message);
            Assert.Equal(a.guid, message.ownerParticipantId);
            Assert.Equal(1, message.ownerPlayerSlot);
            Assert.True(message.ownershipProtected);
            Assert.True(message.ownershipRevision > 0);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Old_format_messages_without_ownership_are_ignored_and_stale_revisions_rejected()
        {
            var session = new ClientSession();
            // 旧ホスト形式（欄なし = 既定値）でも壊れない: 担当の記録は作らない。
            Call(session, "ReceiveMonsterOwnership", 501u, null, 0L, false, 0);
            Assert.Empty(session.EnemyOwners);

            Call(session, "ReceiveMonsterOwnership", 501u, "guid-9", 2L, true, 2);
            Assert.True(session.EnemyOwners.TryGetValue(501u, out var owned));
            Assert.Equal("guid-9", owned.ParticipantId);
            Assert.Equal(2L, owned.Revision);
            Assert.True(owned.Protected);
            Assert.Equal(2, owned.PlayerSlot);

            Call(session, "ReceiveMonsterOwnership", 501u, "guid-5", 1L, true, 1); // 古いリビジョン
            Assert.Equal(2L, session.EnemyOwners[501u].Revision); // 拒否される

            Call(session, "ReceiveMonsterOwnership", 502u, "guid-13", 3L, true, 9); // 枠番号が範囲外
            Assert.False(session.EnemyOwners[502u].Protected); // 保護表示にはしない
        }
    }
}
