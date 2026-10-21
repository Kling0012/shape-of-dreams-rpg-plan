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
    /// <summary>共有ボスの持ち分（HostAuthority.BossShares.cs / HostAuthority.SummonOwnership.cs）の
    /// ホスト側 E2E: 総HPと持ち分の配分・手伝い倍率・切断とKO・与ダメ補正・召喚体の狙い・無効条件。
    /// 実ソースをそのままリンクし、境界（NativeBoundary.cs / BossSharesBoundary.cs）は本体の形だけを用意する。
    /// ダメージは本体の Actor.DealDamage と同じ流れ（スコープを置いて受ダメ補正 → HP setter で確定）で通す。</summary>
    public sealed class BossSharesTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public BossSharesTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        private static void ResetStatics()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "shares" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager { currentZoneIndex = 0 };
            NetworkedManagerBase<ActorManager>.softInstance = null;
            HostAuthority.NativeInstance = null;
            Time.time = 1f;
            Time.frameCount = 1;
            Log.Warnings.Clear();
            BossShareDamageScope.Current = null;
            BossShareDamageScope.NotificationSupported = false;
            Set(typeof(NativeAttributedHpDamage), "<BossShareSupported>k__BackingField", false);
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

        // 本体では NativeAttributedHpDamage / BossShareDamageScope のトランスパイラ適用時に true になる。
        private static void PatchNativeShareHooks()
        {
            Set(typeof(NativeAttributedHpDamage), "<BossShareSupported>k__BackingField", true);
            BossShareDamageScope.NotificationSupported = true;
        }

        // ボス・ミニボス本体の湧き: 本体の人数HPボーナスの捕捉（BossSharesNative 経路）を通す。
        private static Monster SharedBoss(HostAuthority host, Monster.MonsterType type, uint netId, float nativeHealthPercent)
        {
            var boss = new Monster { netId = netId, type = type };
            boss.owner = new DewPlayer { isHumanPlayer = false };
            var bonus = new StatBonus { maxHealthPercentage = nativeHealthPercent };
            boss.Status.AddStatBonus(bonus);
            boss.Status.baseMaxHealth = 1000f;
            Assert.True(host.RecordBossNativeHealth(boss, bonus), "湧き時点で持ち分が有効になる");
            boss.Status.MarkStatsDirty();
            boss.Status.CalculateStatsIfDirty();
            boss.Status.SetHealth(boss.Status.maxHealth);
            return boss;
        }

        private static double SoloOf(HostAuthority host, DewPlayer player) => PressureOf(host, player).HealthMultiplier;

        private static object State(HostAuthority host, Entity enemy) => ((IDictionary)Get(host, "_enemyOwnership"))[enemy];
        private static object SharesOf(HostAuthority host, Entity enemy) => Get(State(host, enemy), "Shares");
        private static double[] ShareRemaining(HostAuthority host, Entity enemy) => (double[])Get(SharesOf(host, enemy), "Remaining");
        private static double[] ShareCapacity(HostAuthority host, Entity enemy) => (double[])Get(SharesOf(host, enemy), "Capacity");

        // 本体の Actor.DealDamage と同じ流れ: スコープを置いて（Prefix）受ダメ補正を掛け、
        // HP setter（ApplyBossShareHealth → CommitBossShareHealth）で持ち分の消費を確定する。
        private static float Strike(HostAuthority host, Entity boss, Actor attacker, float amount)
        {
            var scope = new BossShareDamageScope.Scope { Victim = boss };
            BossShareDamageScope.Current = scope;
            var damage = new DamageData(amount) { actor = attacker };
            host.ApplyOwnershipReceivedDamage(boss, attacker, ref damage);
            float health = boss.Status.currentHealth - damage.amount;
            host.ApplyBossShareHealth(boss.Status, ref health);
            boss.Status.currentHealth = health;
            BossShareDamageScope.Current = null;
            return damage.amount;
        }

        private static void OverrideHealth(HostAuthority host, Monster boss, ref float multiplier)
        {
            var method = host.GetType().GetMethod("OverrideOwnershipHealth", Hidden, new[] { typeof(Monster), typeof(float).MakeByRefType() });
            var args = new object[] { boss, multiplier };
            method.Invoke(host, args);
            multiplier = (float)args[1];
        }

        // 被弾ごとの入り口 TryBossShares を直接呼び、戻り値と out 引数（state・shares）を受け取る。
        private static (bool Result, object State, object Shares) TryShares(HostAuthority host, Entity enemy)
        {
            var method = host.GetType().GetMethod("TryBossShares", Hidden);
            var args = new object[] { enemy, null, null };
            try
            {
                var result = (bool)method.Invoke(host, args);
                return (result, args[1], args[2]);
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
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

        // ---- 総HPと持ち分の配分 ----

        [Fact]
        public void Boss_total_HP_is_the_solo_sum_without_the_native_count_bonus_and_splits_by_solo_ratio()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 8, 40);
            var b = Player(9);
            BuildFor(host, b, 2, 0);
            var room = DreamPressure.ForPlayer(12, 30); // 部屋の平均圧（二重掛けの検出用に誰の一人用より高い）
            Set(host, "_pressure", room);
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 50f); // 本体の人数HP補正 +50%

            double soloA = SoloOf(host, a), soloB = SoloOf(host, b), sum = soloA + soloB;
            float multiplier = (float)room.HealthMultiplier;
            OverrideHealth(host, boss, ref multiplier);
            // 総HP = 一人用倍率の合計 × 0.8。人数補正(+50%)は除外し、部屋の平均とは掛けない。
            Assert.Equal(sum * CoopBalance.Ownership.BossShareFactor * 100d / 150d, multiplier, 3);
            Assert.NotEqual((float)(room.HealthMultiplier * sum * CoopBalance.Ownership.BossShareFactor * 100d / 150d), multiplier, 3);

            // ミニボス本体も同じ扱い（人数補正なしの倍率で検査）。
            var miniboss = SharedBoss(host, Monster.MonsterType.MiniBoss, 101, 0f);
            float miniMultiplier = 1f;
            OverrideHealth(host, miniboss, ref miniMultiplier);
            Assert.Equal(sum * CoopBalance.Ownership.BossShareFactor, miniMultiplier, 3);

            // 持ち分は一人用倍率の比で配分（湧き時点では総HPに対する割合）。
            var remaining = ShareRemaining(host, boss);
            Assert.Equal(soloA / sum, remaining[0], 4);
            Assert.Equal(soloB / sum, remaining[1], 4);
            Assert.Empty(Log.Warnings);
        }

        // ---- ダメージの振り分け ----

        [Theory]
        [InlineData(0, 1d)]
        [InlineData(1, 0.7d)]
        [InlineData(2, 0.4d)]
        [InlineData(3, 0.1d)]
        [InlineData(7, 0.1d)]
        public void Strikes_drain_the_own_share_first_and_help_afterwards_at_the_helper_factor(int zoneIndex, double factor)
        {
            var host = Host(out _);
            RunWorldSteps.Enter(host, zoneIndex);
            var a = Player(5);
            BuildFor(host, a, 2, 0);  // 新人
            var b = Player(9);
            BuildFor(host, b, 8, 40); // ベテラン
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 0f);
            double soloA = SoloOf(host, a), soloB = SoloOf(host, b), sum = soloA + soloB;
            double capA = boss.Status.maxHealth * soloA / sum, capB = boss.Status.maxHealth * soloB / sum;

            // 自分の持ち分が残っているうちは倍率 1。
            Assert.Equal(100f, Strike(host, boss, a.hero, 100f), 2);
            Assert.Equal(capA - 100, ShareRemaining(host, boss)[0], 1);
            Assert.Equal(capB, ShareRemaining(host, boss)[1], 1);
            Assert.Equal(100f, Strike(host, boss, b.hero, 100f), 2); // 持ち分を持つ人も同じ
            Assert.Equal(capB - 100, ShareRemaining(host, boss)[1], 1);

            // a の持ち分（残り capA - 100）を境目をまたいで超える一撃: 超えた分だけ手伝い倍率。
            double rest = capA - 100;
            Assert.Equal(rest + (300 - rest) * factor, Strike(host, boss, a.hero, 300f), 1);
            var remaining = ShareRemaining(host, boss);
            Assert.Equal(0, remaining[0], 1); // 自分の持ち分を使い切った
            Assert.Equal(capB - 100 - (300 - rest) * factor, remaining[1], 1); // 手伝い分は b の持ち分から
            Assert.Equal(remaining[0] + remaining[1], boss.Status.currentHealth, 1); // 実HP = 持ち分の残りの合計
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Help_overflow_spills_from_the_largest_remaining_share_until_all_shares_are_gone()
        {
            var host = Host(out _);
            RunWorldSteps.Enter(host, 2);
            var a = Player(5);
            BuildFor(host, a, 2, 0);  // 1.00
            var b = Player(9);
            BuildFor(host, b, 4, 10); // 1.28
            var c = Player(13);
            BuildFor(host, c, 8, 40); // 2.30
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 0f);
            double soloA = SoloOf(host, a), soloB = SoloOf(host, b), soloC = SoloOf(host, c);
            double sum = soloA + soloB + soloC;
            double capA = boss.Status.maxHealth * soloA / sum, capB = boss.Status.maxHealth * soloB / sum,
                capC = boss.Status.maxHealth * soloC / sum;

            // c を先に削って残り割合を c < b にしておく（手伝い先の選択の前提）。
            Assert.Equal(300f, Strike(host, boss, c.hero, 300f), 1);
            // a が自分の持ち分を使い切る境目の一撃: 手伝いは残り割合最大の b から。
            double help = (250 - capA) * OwnershipHelp.Factor(2);
            Assert.Equal(capA + help, Strike(host, boss, a.hero, 250f), 1);
            var remaining = ShareRemaining(host, boss);
            Assert.Equal(0, remaining[0], 1);
            Assert.Equal(capB - help, remaining[1], 1);
            Assert.Equal(capC - 300, remaining[2], 1);

            // b の持ち分も引き切る大粒の一撃は b → c へ溢れ、全員の持ち分が尽きた時点で倒れる。
            // ShareRemaining は持ち分の生配列を返すので、後続の一撃で値が書き換わる前に手元に写す。
            double remB = remaining[1], remC = remaining[2];
            double fromB = remB;
            // 3世界目の減衰60%でも b と c の残りを引き切れる大きさ（手伝いで通るのは 1250 × 0.4 = 500）。
            double fromC = Math.Min((1250 - fromB / OwnershipHelp.Factor(2)) * OwnershipHelp.Factor(2), remC);
            Assert.Equal(fromB + fromC, Strike(host, boss, a.hero, 1250f), 1);
            var after = ShareRemaining(host, boss);
            Assert.Equal(0, after[0], 1);
            Assert.Equal(0, after[1], 1);
            Assert.Equal(remC - fromC, after[2], 1);
            Assert.Equal(0f, boss.Status.currentHealth, 1); // 全員の持ち分が尽きた → 倒れる
            Assert.Empty(Log.Warnings);
        }

        // ---- 切断とKO ----

        [Fact]
        public void A_disconnected_participants_share_is_redistributed_by_the_solo_ratio()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 2, 0);
            var b = Player(9);
            BuildFor(host, b, 8, 40);
            var c = Player(13);
            BuildFor(host, c, 4, 10);
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 0f);
            Strike(host, boss, c.hero, 100f); // c の持ち分を少し削った状態
            double soloA = SoloOf(host, a), soloB = SoloOf(host, b);
            double weight = soloA + soloB;
            var remaining = ShareRemaining(host, boss);
            // 生配列は配り直しで書き換わるので、走査の前に手元に写す。
            double remA = remaining[0], remB = remaining[1], remC = remaining[2];

            c.connectionToClient = null; // c が切断
            Time.time = 2f;
            Call(host, "ScanEnemyOwnership");
            // 残った人へ一人用倍率の比で配り直す（2人だけの湧きと同じ配分になる）。
            var afterRemain = ShareRemaining(host, boss);
            var afterCap = ShareCapacity(host, boss);
            Assert.Equal(remA + remC * soloA / weight, afterRemain[0], 1);
            Assert.Equal(remB + remC * soloB / weight, afterRemain[1], 1);
            Assert.Equal(0, afterRemain[2], 1);
            Assert.Equal(boss.Status.maxHealth * soloA / weight, afterCap[0], 1);
            Assert.Equal(boss.Status.maxHealth * soloB / weight, afterCap[1], 1);
            Assert.Equal(afterRemain[0] + afterRemain[1], boss.Status.currentHealth, 1);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void A_knocked_out_participant_keeps_their_share_and_helpers_can_still_drain_it()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 2, 0);
            var c = Player(9);
            BuildFor(host, c, 8, 40);
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 0f);
            double soloA = SoloOf(host, a), sum = soloA + SoloOf(host, c);
            double capA = boss.Status.maxHealth * soloA / sum, capC = boss.Status.maxHealth - capA;
            Strike(host, boss, a.hero, 250f); // a の持ち分が残り capA - 250
            double remA = capA - 250, remC = capC;
            double rest = capA - 250;

            c.hero.isKnockedOut = true; // c が倒れた（接続は残る）
            Time.time = 2f;
            Call(host, "ScanEnemyOwnership");
            var kept = ShareRemaining(host, boss);
            Assert.Equal(remA, kept[0], 1); // KO では持ち分を保つ（配り直さない）
            Assert.Equal(remC, kept[1], 1);
            Assert.Equal(ShareCapacity(host, boss)[1], capC, 1);

            // 他の人は手伝いで KO 中の人の持ち分を削れる（蘇生より放置が有利にならない）。
            double factor = 1; // 最初の世界はKO中の味方の持ち分にも全量が通る。
            Assert.Equal(rest + (400 - rest) * factor, Strike(host, boss, a.hero, 400f), 1);
            Assert.Equal(0, ShareRemaining(host, boss)[0], 1);
            Assert.Equal(remC - (400 - rest) * factor, ShareRemaining(host, boss)[1], 1);
            Assert.True(boss.Status.currentHealth > 0f, "ボスはまだ立っている");
            Assert.Empty(Log.Warnings);
        }

        // ---- ボスの与ダメ ----

        [Fact]
        public void Boss_and_child_damage_clamps_by_the_victims_own_pressure_ratio()
        {
            var host = Host(out _);
            var top = Player(5);
            BuildFor(host, top, 30, 200); // 最も高い個人圧
            var mid = Player(9);
            BuildFor(host, mid, 12, 30);  // 部屋の平均よりわずかに低い
            var low = Player(13);
            BuildFor(host, low, 2, 0);    // 最も低い個人圧
            var room = DreamPressure.ForPlayer(8, 40);
            Set(host, "_pressure", room);
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 0f);
            var child = Spawn(host, 0, new Entity { netId = 101, parentActor = boss }); // ボスの子
            var owned = Spawn(host, 102); // 通常の敵（担当制の対象。最初の湧きで top の担当になる）

            float Hit(DewPlayer victim, Actor attacker)
            {
                var damage = new DamageData(100f) { actor = attacker };
                host.ApplyOwnershipReceivedDamage(victim.hero, attacker, ref damage);
                return damage.amount;
            }

            float pRoom = (float)room.HealthMultiplier;
            float pTop = (float)SoloOf(host, top), pMid = (float)SoloOf(host, mid), pLow = (float)SoloOf(host, low);
            Assert.Equal(100f * Math.Min(3f, pTop / pRoom), Hit(top, boss), 2); // × clamp(P(x)/P(部屋), 0.2, 3)
            Assert.Equal(100f * pMid / pRoom, Hit(mid, boss), 2); // 範囲内はそのまま
            Set(host, "_pressure", DreamPressure.ForPlayer(30, 200)); // 部屋が非常に高い場面
            Assert.Equal(100f * CoopBalance.Ownership.BossDamageMin, Hit(low, boss), 2); // 下限 0.2
            Set(host, "_pressure", room);
            Assert.Equal(100f * Math.Min(3f, pTop / pRoom), Hit(top, child), 2); // ボスの子も同じ補正
            Assert.Equal(100f, Hit(top, owned), 2); // 担当制の通常の敵は補正しない（自分の担当敵）
            Assert.Empty(Log.Warnings);
        }

        // ---- 召喚体の狙い ----

        [Fact]
        public void Summons_prefer_owned_then_unowned_then_unprotected_then_protected_enemies()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var am = new ActorManager();
            Set(host, "_am", am);
            var summon = new Summon { hero = b.hero }; // 主人は b
            var ai = new EntityAI { entity = summon };

            var unprotected = Spawn(host, 101); // → a の担当。湧き猶予が切れて保護なし
            var boss = Spawn(host, 100, new Monster { type = Monster.MonsterType.Boss }); // 担当なし
            var bossChild = Spawn(host, 0, new Entity { netId = 103, parentActor = boss }); // 担当なし（ボスの子）
            var owned = Spawn(host, 104); // → b の担当（召喚体の主人）
            Time.time = 19f;
            var protectedOther = Spawn(host, 105); // → a の担当。湧き猶予内で保護中
            Time.time = 20f;
            unprotected.position = new Vector3(3f, 0f, 0f);  // rank 2（他人の担当・保護なし）・最も近い
            protectedOther.position = new Vector3(4f, 0f, 0f); // rank 3（他人の担当・保護中）
            boss.position = new Vector3(5f, 0f, 0f);   // rank 1（担当なし）
            bossChild.position = new Vector3(8f, 0f, 0f); // rank 1（担当なし）
            owned.position = new Vector3(15f, 0f, 0f); // rank 0（主人の担当）・最も遠い
            am.allEntities.AddRange(new Entity[] { owned, protectedOther, boss, unprotected, bossChild });

            Entity Pick()
            {
                Entity target = null;
                host.SelectSummonOwnershipAggro(ai, ref target);
                return target;
            }

            Assert.Same(owned, Pick()); // 主人の担当が最優先（距離では最も遠くても）
            am.allEntities.Remove(owned);
            Assert.Same(boss, Pick()); // 次は担当なし。同順位は最寄り（boss 5m < 子 8m）
            am.allEntities.Remove(boss);
            Assert.Same(bossChild, Pick()); // 担当なし（ボスの子）は保護なしの他人より優先
            am.allEntities.Remove(bossChild);
            Assert.Same(unprotected, Pick()); // 保護なしの他人の担当
            am.allEntities.Remove(unprotected);
            Assert.Same(protectedOther, Pick()); // 保護中の他人の担当は最後
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Summons_still_retaliate_against_enemies_attacking_the_master_or_themselves()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var am = new ActorManager();
            Set(host, "_am", am);
            var summon = new Summon { hero = b.hero };
            var ai = new EntityAI { entity = summon };

            var boss = Spawn(host, 100, new Monster { type = Monster.MonsterType.Boss });
            Time.time = 19f;
            var masterAttacker = Spawn(host, 102); // → a の担当・保護中（rank 3・最悪）
            Time.time = 20f;
            var filler = Spawn(host, 101); // → b の担当（担当数の調整用・索敵範囲外）
            boss.position = new Vector3(5f, 0f, 0f);
            filler.position = new Vector3(100f, 0f, 0f); // 索敵範囲外（rank 0 だが候補にならない）
            masterAttacker.position = new Vector3(15f, 0f, 0f);
            masterAttacker.AI = new EntityAI { entity = masterAttacker };
            masterAttacker.AI.context.targetEnemy = b.hero; // 主人を攻撃している
            am.allEntities.AddRange(new Entity[] { boss, filler, masterAttacker });

            ai.context.targetEnemy = masterAttacker;
            Entity target = null;
            host.SelectSummonOwnershipAggro(ai, ref target);
            Assert.Same(masterAttacker, target); // 主人への攻撃には優先度に関係なく反撃する

            var selfAttacker = Spawn(host, 103); // → a の担当
            selfAttacker.position = new Vector3(2f, 0f, 0f);
            selfAttacker.Control.attackTarget = summon; // 召喚体自身を攻撃している
            am.allEntities.Add(selfAttacker);
            target = selfAttacker; // 本体の再標的経路（Summon.cs 509 行付近）がすでに選んだ状態
            host.SelectSummonOwnershipAggro(ai, ref target);
            Assert.Same(selfAttacker, target); // 自分への反撃も帰属判定に入らず維持される
            Assert.Empty(Log.Warnings);
        }

        // ---- 無効条件 ----

        [Fact]
        public void Solo_and_disabled_ownership_leave_bosses_completely_untouched()
        {
            // 人間1人では何もしない。
            var host = Host(out _);
            var a = Player(5);
            PatchNativeShareHooks();
            var boss = new Monster { netId = 100, type = Monster.MonsterType.Boss };
            boss.owner = new DewPlayer { isHumanPlayer = false };
            var bonus = new StatBonus { maxHealthPercentage = 50f };
            boss.Status.AddStatBonus(bonus);
            Assert.False(host.RecordBossNativeHealth(boss, bonus)); // 持ち分は作らない
            Assert.Empty((ICollection)Get(host, "_enemyOwnership"));
            var stray = new DamageData(100f) { actor = boss };
            host.ApplyOwnershipReceivedDamage(a.hero, boss, ref stray);
            Assert.Equal(100f, stray.amount, 2); // 与ダメ補正も掛からない

            // 設定OFF: 一度作った持ち分を解除し、以降は従来どおり。
            host = Host(out _);
            a = Player(5);
            BuildFor(host, a, 2, 0);
            var b = Player(9);
            BuildFor(host, b, 8, 40);
            PatchNativeShareHooks();
            boss = SharedBoss(host, Monster.MonsterType.Boss, 101, 0f);
            Assert.Equal(100f, Strike(host, boss, a.hero, 100f), 2); // この時点では補正あり

            host.ConfigureEnemyOwnership(false);
            Time.time = 2f;
            Call(host, "ScanEnemyOwnership");
            Assert.Null(SharesOf(host, boss)); // 持ち分を捨てる
            float multiplier = 2f;
            OverrideHealth(host, boss, ref multiplier);
            Assert.Equal(2f, multiplier, 3); // HP倍率は差し替えない（部屋の圧のまま）
            Assert.Equal(100f, Strike(host, boss, a.hero, 100f), 2); // 手伝い倍率も掛からない
            Assert.Equal(boss.Status.maxHealth - 200f, boss.Status.currentHealth, 1); // 1本のHPとして減る
            Assert.Empty(Log.Warnings);
        }

        // ---- 被弾ごとの入り口（TryBossShares の同じ結果）----

        // 稼働確認を1評価に整えた軽量化（perf-loop/20261021-1219）が、稼働中・未稼働の
        // どちらでも従来と同じ結果を返すこと。偽のとき shares は null が契約。
        [Fact]
        public void TryBossShares_matches_the_previous_results_for_cached_cold_and_running_hits()
        {
            var host = Host(out _);
            var a = Player(5);
            BuildFor(host, a, 2, 0);
            var b = Player(9);
            BuildFor(host, b, 8, 40);
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 100, 0f);
            var enemy = Spawn(host, 101); // 通常敵: 冷経路のまま（Shares は null）
            Assert.NotNull(SharesOf(host, boss));

            // 稼働中: キャッシュ済みなら同じ持ち分を返し、冷経路の成立・不成立も従来どおり。
            var running = TryShares(host, boss);
            Assert.True(running.Result, "稼働中のキャッシュ済みは真");
            Assert.Same(SharesOf(host, boss), running.Shares);

            Set(State(host, boss), "Shares", null); // 冷経路に戻して成立を通す
            var prepared = TryShares(host, boss);
            Assert.True(prepared.Result, "稼働中の冷経路成立は真");
            Assert.Same(SharesOf(host, boss), prepared.Shares);

            var rejected = TryShares(host, enemy);
            Assert.False(rejected.Result, "稼働中でも通常敵の冷経路は不成立");
            Assert.Null(rejected.Shares);

            // 未稼働 + キャッシュ済み: 偽で shares は null。キャッシュ自体は壊さない。
            host.ConfigureEnemyOwnership(false);
            var cached = TryShares(host, boss);
            Assert.False(cached.Result, "未稼働ならキャッシュ済みでも偽");
            Assert.Null(cached.Shares); // 偽のとき shares は null（契約）
            Assert.NotNull(cached.State);
            Assert.NotNull(SharesOf(host, boss));

            // 未稼働 + 冷経路: 副作用なく偽（失敗印を作らない）。
            var cold = TryShares(host, enemy);
            Assert.False(cold.Result, "未稼働なら冷経路も偽");
            Assert.Null(cold.Shares);
            Assert.Null(SharesOf(host, enemy)); // 冷経路は null のまま
            Assert.Empty(Log.Warnings);
        }

        // ---- 同期 ----

        [Fact]
        public void Share_remaining_ratios_ride_the_classification_deltas()
        {
            var host = Host(out var actor);
            var a = Player(5);
            BuildFor(host, a, 2, 0);
            var b = Player(9);
            BuildFor(host, b, 8, 40);
            PatchNativeShareHooks();
            var boss = SharedBoss(host, Monster.MonsterType.Boss, 501, 0f);
            var runtime = new HostAuthority.MonsterRuntime { Monster = boss };
            Set(host, "_monsters", new Dictionary<Monster, HostAuthority.MonsterRuntime> { [boss] = runtime });
            double soloA = SoloOf(host, a), sum = soloA + SoloOf(host, b);
            double capA = boss.Status.maxHealth * soloA / sum;

            Strike(host, boss, a.hero, 100f); // a の持ち分だけが減る
            Call(host, "SendMonsterClassification", runtime, null);
            Call(host, "FlushMonsterSync", 32);
            var first = actor.Sent.Select(s => s.Message).OfType<DreamforgeNightmareMsg>().Single();
            Assert.Equal(2, first.bossShares.Length); // 参加者ごとに色番号と残割合（0..1000）
            Assert.Equal("guid-5", first.bossShares[0].participantId);
            Assert.Equal(1, first.bossShares[0].playerSlot);
            Assert.Equal((int)Math.Round((capA - 100) / capA * 1000), first.bossShares[0].remaining);
            Assert.Equal("guid-9", first.bossShares[1].participantId);
            Assert.Equal(2, first.bossShares[1].playerSlot);
            Assert.Equal(1000, first.bossShares[1].remaining); // 減っていない人は満割合

            int sent = actor.Sent.Count;
            Call(host, "SendMonsterClassification", runtime, null);
            Call(host, "FlushMonsterSync", 32);
            Assert.Equal(sent, actor.Sent.Count); // 変化がなければ再送しない

            Strike(host, boss, a.hero, 100f); // さらに削る
            Call(host, "SendMonsterClassification", runtime, null);
            Call(host, "FlushMonsterSync", 32);
            var second = actor.Sent.Select(s => s.Message).OfType<DreamforgeNightmareMsg>().Last();
            Assert.True(second.ownershipRevision > first.ownershipRevision, "リビジョンは単調増加");
            Assert.Equal((int)Math.Round((capA - 200) / capA * 1000), second.bossShares[0].remaining);
            Assert.Empty(Log.Warnings);
        }
    }
}
