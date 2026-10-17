using System;
using System.Collections;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;
using static SodRpg.Core.Game.CoopBalance.DynamicDifficulty;

namespace Issue73.Native.Tests
{
    /// <summary>殲滅速度に合わせた動的難易度（HostAuthority.DynamicDifficulty.cs）のホスト側 E2E:
    /// 測定（あふれ込み込みの素のダメージ・親鎖帰属・0.5 秒区切り）・部屋の移り目だけの更新
    /// （対数 EMA・minActiveSeconds・minRooms・上限下限・alphaDown・heroTargetScale）・適用
    /// （湧いた時点の D 固定・HP×D・与ダメ×D^damageExponent・ボスの持ち分は D 込み）・報酬・設定。
    /// 実ソースをそのままリンクし、期待値の数値は生成定数 CoopBalance.DynamicDifficulty から組み立てる。</summary>
    public sealed class DynamicDifficultyTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public DynamicDifficultyTests() => ResetStatics();

        public void Dispose() => ResetStatics();

        private static void ResetStatics()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            DewPlayer.local = null;
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "dyndiff" };
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

        private static DewPlayer Player(uint netId, Hero hero = null)
        {
            hero = hero ?? new Hero { netId = netId * 100 };
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
            host.ConfigureEnemyOwnership(true); // 協力かつ人間2人以上で設定ON（F6）を前提とする。
            // 動的難易度の設定は既定（マルチON・ソロOFF）のまま使う。
            HostAuthority.NativeInstance = host;
            Set(host, "_registeredOn", actor);
            NetworkedManagerBase<ActorManager>.softInstance = new ActorManager { serverActor = actor };
            return host;
        }

        // 指定した担当で湧かせる（基準 HP は個人圧・D を掛ける前の値）。
        private static Entity SpawnFor(HostAuthority host, DewPlayer owner, uint netId, float baseHealth = 100f)
        {
            var enemy = new Entity { netId = netId };
            enemy.owner = new DewPlayer { isHumanPlayer = false }; // 湧き元は人間ではない
            enemy.Status.baseMaxHealth = baseHealth;
            host.PrepareEnemyOwnership(enemy, owner);
            host.FinalizeEnemyOwnership(enemy);
            return enemy;
        }

        private static Monster SpawnMonsterFor(HostAuthority host, DewPlayer owner, uint netId)
        {
            var monster = new Monster { netId = netId };
            monster.owner = new DewPlayer { isHumanPlayer = false };
            host.PrepareEnemyOwnership(monster, owner);
            host.FinalizeEnemyOwnership(monster);
            return monster;
        }

        // 本体の Actor.DealDamage の受ダメ補正入口。測定は手伝い倍率・下限より前の素の値に対して行われる。
        private static float Hit(HostAuthority host, Entity victim, Actor attacker, float amount)
        {
            var damage = new DamageData(amount) { actor = attacker };
            host.ApplyOwnershipReceivedDamage(victim, attacker, ref damage);
            return damage.amount;
        }

        // 1 部屋ぶんの戦闘: 0.5 秒区切りごとに 1 発ずつ（区切りはすべて別）。
        private static void FightRoom(HostAuthority host, DewPlayer player, Entity victim, double totalDamage, int buckets)
        {
            float perHit = (float)(totalDamage / buckets);
            for (int i = 0; i < buckets; i++)
            {
                Time.time += (float)CoopBalance.DynamicDifficulty.ActiveBucketSeconds;
                Hit(host, victim, player.hero, perHit);
            }
        }

        // r =（活動 1 秒あたりのダメージ × targetSeconds）÷ 基準 HP を成り立たせる総ダメージ。
        private static double DamageFor(double ratio, int buckets, double referenceHealth = 100d)
            => ratio * referenceHealth * buckets * ActiveBucketSeconds / TargetSeconds;

        // 部屋の移り目が minActiveSeconds ぎりぎりの活動（ちょうど下限）で埋まる部屋数。
        private static int FullBuckets() => (int)(MinActiveSeconds / ActiveBucketSeconds);

        // rooms 部屋ぶん「湧き → 殲滅速度 ratio の戦闘 → 部屋の移り目」を繰り返す。
        private static void DriveRooms(HostAuthority host, DewPlayer player, uint firstNetId, double ratio, int rooms, double referenceHealth = 100d)
        {
            int buckets = FullBuckets();
            double damage = DamageFor(ratio, buckets, referenceHealth);
            for (int i = 0; i < rooms; i++)
            {
                var victim = SpawnFor(host, player, firstNetId + (uint)i);
                FightRoom(host, player, victim, damage, buckets);
                host.OnDynamicRoomLoading();
            }
        }

        // OnDynamicRoomLoading と同じ式（生成定数から）。同じ対数比の部屋を rooms 回更新した後の D。
        private static double ExpectedFactor(double logRatio, int rooms)
        {
            double average = 0;
            for (int i = 0; i < rooms; i++)
            {
                double alpha = logRatio < average ? AlphaDown : Alpha;
                average += alpha * (logRatio - average);
            }
            if (rooms < MinRooms) return 1;
            return Math.Max(MinFactor, Math.Min(MaxFactor, Math.Exp(Strength * average)));
        }

        private static object PlayerState(HostAuthority host, DewPlayer player)
        {
            foreach (var state in (Array)Get(host, "_dynamicPlayers"))
                if (ReferenceEquals(Get(state, "Player"), player)) return state;
            return null;
        }

        private static double DamageOf(HostAuthority host, DewPlayer player)
        {
            var state = PlayerState(host, player);
            return state == null ? 0d : (double)Get(state, "Damage");
        }

        private static int BucketsOf(HostAuthority host, DewPlayer player)
        {
            var state = PlayerState(host, player);
            return state == null ? 0 : (int)Get(state, "ActiveBuckets");
        }

        private static double FactorOf(HostAuthority host, DewPlayer player)
        {
            var state = PlayerState(host, player);
            return state == null ? 1d : (double)Get(state, "Factor");
        }

        private static int RoomsOf(HostAuthority host, DewPlayer player)
        {
            var state = PlayerState(host, player);
            return state == null ? 0 : (int)Get(state, "Rooms");
        }

        private static double LogAverageOf(HostAuthority host, DewPlayer player)
        {
            var state = PlayerState(host, player);
            return state == null ? 0d : (double)Get(state, "LogAverage");
        }

        private static int DynamicCount(HostAuthority host) => ((ICollection)Get(host, "_dynamicEnemies")).Count;

        // 撃破報酬の追加倍率（HostAuthority.KillSync.cs が ref で渡すのと同じ形）。
        private static double RewardOf(HostAuthority host, Monster monster)
        {
            var method = host.GetType().GetMethod("ApplyDynamicKillReward", Hidden, new[] { typeof(Monster), typeof(double).MakeByRefType() });
            var args = new object[] { monster, 1.0 };
            method.Invoke(host, args);
            return (double)args[1];
        }

        private static void PatchNativeShareHooks()
        {
            Set(typeof(NativeAttributedHpDamage), "<BossShareSupported>k__BackingField", true);
            BossShareDamageScope.NotificationSupported = true;
        }

        // ボス・ミニボス本体の湧き: BossSharesTests と同じ作法。
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

        private static void OverrideHealth(HostAuthority host, Monster boss, ref float multiplier)
        {
            var method = host.GetType().GetMethod("OverrideOwnershipHealth", Hidden, new[] { typeof(Monster), typeof(float).MakeByRefType() });
            var args = new object[] { boss, multiplier };
            method.Invoke(host, args);
            multiplier = (float)args[1];
        }

        private static object State(HostAuthority host, Entity enemy) => ((IDictionary)Get(host, "_enemyOwnership"))[enemy];
        private static object SharesOf(HostAuthority host, Entity enemy) => Get(State(host, enemy), "Shares");
        private static double[] ShareRemaining(HostAuthority host, Entity enemy) => (double[])Get(SharesOf(host, enemy), "Remaining");

        private static float RecalcMaxHealth(Entity entity)
        {
            entity.Status.MarkStatsDirty();
            entity.Status.CalculateStatsIfDirty();
            return entity.Status.maxHealth;
        }

        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static object Get(object target, string name) => ObjectType(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);

        // ---- 測定 ----

        [Fact]
        public void Damage_is_counted_raw_with_overkill_before_help_scaling_and_follows_the_parent_chain()
        {
            var host = Host(out _);
            RunWorldSteps.Enter(host, 2);
            var a = Player(5);
            var b = Player(9);
            var enemy = SpawnFor(host, b, 100, 1000f); // b の担当敵（a は手伝い側）
            enemy.Status.SetHealth(enemy.Status.maxHealth);

            // 一撃で HP の 100 倍: あふれた分も丸ごと数える。
            Time.time = 1f;
            float overkill = enemy.Status.maxHealth * 100f;
            float applied = Hit(host, enemy, a.hero, overkill);
            Assert.Equal((double)overkill, DamageOf(host, a), 0); // 手伝い倍率の前の値
            Assert.Equal(overkill * OwnershipHelp.Factor(2), applied, overkill * 1e-5f); // 3世界目の減衰60%（記録とは別）。

            // 活動時間は 0.5 秒区切り: 当てた区切りだけ。同じ区切りの複数ヒットは 1 区切り。
            Time.time = 1.4f; Hit(host, enemy, a.hero, 10f); // 区切り 2（1.0 秒と同じ）
            Time.time = 1.5f; Hit(host, enemy, a.hero, 10f); // 区切り 3
            Time.time = 2.0f; Hit(host, enemy, a.hero, 10f); // 区切り 4
            Assert.Equal(3, BucketsOf(host, a));
            Assert.Equal((double)overkill + 30d, DamageOf(host, a), 0);

            // 召喚体とその弾（親鎖）は主人に帰属する。
            var summon = new Summon { hero = b.hero };
            Time.time = 2.5f; Hit(host, enemy, summon, 40f);
            var projectile = new Actor { parentActor = summon };
            Time.time = 3.0f; Hit(host, enemy, projectile, 20f);
            Assert.Equal(60d, DamageOf(host, b), 0);
            Assert.Equal(2, BucketsOf(host, b));

            // 味方（Hero）へのダメージは数えない。
            Time.time = 3.5f;
            Hit(host, a.hero, b.hero, 5f);
            Assert.Equal(60d, DamageOf(host, b), 0);
            Assert.Empty(Log.Warnings);
        }

        // ---- 更新 ----

        [Fact]
        public void The_factor_moves_only_at_room_transitions_with_enough_activity_and_enough_rooms()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            int full = FullBuckets();
            double raw = DamageFor(3d, full);

            // 部屋1: 十分に戦っても minRooms 回未満は D = 1。
            DriveRooms(host, a, 100, 3d, 1);
            Assert.Equal(1, RoomsOf(host, a));
            Assert.Equal(1d, FactorOf(host, a), 10);

            // 部屋2: 活動が minActiveSeconds 未満の部屋では更新しない（E も部屋数も動かない）。
            var shortRoom = SpawnFor(host, a, 150);
            FightRoom(host, a, shortRoom, raw, full - 1);
            double before = LogAverageOf(host, a);
            host.OnDynamicRoomLoading();
            Assert.Equal(1, RoomsOf(host, a));
            Assert.Equal(before, LogAverageOf(host, a), 10);
            Assert.Equal(1d, FactorOf(host, a), 10);

            // 部屋3: 2 回目の更新で D が付く。
            DriveRooms(host, a, 160, 3d, 1);
            double factor = ExpectedFactor(Math.Log(3d), 2);
            Assert.Equal(2, RoomsOf(host, a));
            Assert.Equal(factor, FactorOf(host, a), 5);

            // 部屋4の戦闘中（移り目の前）は D が変わらない。
            var inRoom = SpawnFor(host, a, 170);
            FightRoom(host, a, inRoom, raw, full);
            Assert.Equal(factor, FactorOf(host, a), 5);
            host.OnDynamicRoomLoading();
            Assert.Equal(ExpectedFactor(Math.Log(3d), 3), FactorOf(host, a), 5); // 移り目でだけ更新

            // 戦っていない b は 1 のまま。
            Assert.Equal(1d, FactorOf(host, b), 10);
            Assert.Empty(Log.Warnings);
        }

        // ---- 係数 ----

        [Fact]
        public void The_factor_follows_the_log_ema_of_the_speed_ratio_up_to_the_cap()
        {
            var host = Host(out _);
            var a = Player(5);
            Player(9);

            // r = 3 が続く: D = exp(strength × E) どおりに r の平方根へ近づく。
            DriveRooms(host, a, 100, 3d, 2);
            double after2 = FactorOf(host, a);
            Assert.Equal(ExpectedFactor(Math.Log(3d), 2), after2, 5);
            DriveRooms(host, a, 110, 3d, 6); // 計 8 部屋
            double after8 = FactorOf(host, a);
            Assert.Equal(ExpectedFactor(Math.Log(3d), 8), after8, 5);
            Assert.True(after2 < after8 && after8 < Math.Sqrt(3d), "差の半分: r の平方根に満たない高さへ近づく");

            // 圧倒的な速さでも上限で頭打ち。
            DriveRooms(host, a, 120, 1e9, 1);
            Assert.Equal(MaxFactor, FactorOf(host, a), 10);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void A_struggling_player_descends_faster_with_alpha_down_to_the_floor()
        {
            var host = Host(out _);
            Player(5);
            var b = Player(9);

            // 苦戦側（r < 1）は alphaDown で速く追う: alpha のままの式とは一致しない。
            DriveRooms(host, b, 100, 0.6d, 2);
            double factor = FactorOf(host, b);
            Assert.Equal(ExpectedFactor(Math.Log(0.6d), 2), factor, 5);
            double withoutAlphaDown = Math.Exp(Strength * (1 - Math.Pow(1 - Alpha, 2)) * Math.Log(0.6d));
            Assert.NotEqual(withoutAlphaDown, factor);

            // さらに苦戦が続いても下限で止まる。
            DriveRooms(host, b, 110, 0.3d, 2);
            Assert.Equal(MinFactor, FactorOf(host, b), 10);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Hero_target_scale_multiplies_the_speed_ratio()
        {
            var host = Host(out _);
            var a = Player(5); // 通常の旅人（既定の目標スケール）
            var m = Player(9, new Hero_Mist { netId = 900 });
            Assert.True(HeroTargetScale(ClientSession.HeroKeyOf(m.hero)) < HeroTargetScale(ClientSession.HeroKeyOf(a.hero)),
                "前提: Mist の目標スケールは短い");

            // 同じ速さで戦っても、目標時間に旅人ごとの係数が掛かる。
            DriveRooms(host, a, 100, 3d, 2);
            DriveRooms(host, m, 110, 3d, 2);
            Assert.Equal(ExpectedFactor(Math.Log(3d * HeroTargetScale(ClientSession.HeroKeyOf(a.hero))), 2), FactorOf(host, a), 5);
            Assert.Equal(ExpectedFactor(Math.Log(3d * HeroTargetScale(ClientSession.HeroKeyOf(m.hero))), 2), FactorOf(host, m), 5);
            Assert.True(FactorOf(host, m) < FactorOf(host, a), "目標が短い旅人は同じ速さでも低く出る");
            Assert.Empty(Log.Warnings);
        }

        // ---- 適用 ----

        [Fact]
        public void Enemies_keep_their_spawn_time_factor_for_health_and_damage()
        {
            var host = Host(out _);
            var a = Player(5);
            Player(9);

            DriveRooms(host, a, 100, 3d, 2);
            double firstFactor = FactorOf(host, a);
            Assert.True(firstFactor > 1d, "前提: 速すぎた部屋が 2 つで D > 1");

            var first = SpawnFor(host, a, 200, 200f);
            Assert.Equal(200f * firstFactor, first.Status.maxHealth, 2); // HealthMultiplier に D

            // D がさらに上がっても、湧いた時点の D が敵ごとに固定される。
            DriveRooms(host, a, 210, 3d, 2);
            double secondFactor = FactorOf(host, a);
            Assert.True(secondFactor > firstFactor);
            Assert.Equal(200f * firstFactor, RecalcMaxHealth(first), 2); // 既存の敵の最大 HP は変わらない
            var second = SpawnFor(host, a, 201, 200f);
            Assert.Equal(200f * secondFactor, second.Status.maxHealth, 2); // 新しい敵は新しい D

            // DamageMultiplier には D^damageExponent。
            var dealtFirst = new DamageData(100f);
            foreach (var processor in first.dealtDamageProcessor.InOrder()) processor(ref dealtFirst, first, second);
            Assert.Equal(100f * Math.Pow(firstFactor, DamageExponent), dealtFirst.amount, 2);
            var dealtSecond = new DamageData(100f);
            foreach (var processor in second.dealtDamageProcessor.InOrder()) processor(ref dealtSecond, second, first);
            Assert.Equal(100f * Math.Pow(secondFactor, DamageExponent), dealtSecond.amount, 2);
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Boss_shares_include_each_participants_dynamic_factor()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            PatchNativeShareHooks();

            DriveRooms(host, a, 100, 3d, 2); // a だけ D > 1、b は戦っていないので 1
            double dA = FactorOf(host, a);
            Assert.True(dA > 1d);

            var boss = SharedBoss(host, Monster.MonsterType.Boss, 200, 0f);
            float multiplier = 1f;
            OverrideHealth(host, boss, ref multiplier);
            // 総HP = 各参加者の一人用倍率（=1）に湧いた時点の D を掛けた合計 × BossShareFactor。
            Assert.Equal((dA + 1d) * CoopBalance.Ownership.BossShareFactor, multiplier, 3);

            // 持ち分も D 込みの一人用倍率の比で配分される。
            var remaining = ShareRemaining(host, boss);
            Assert.Equal(dA / (dA + 1d), remaining[0], 4);
            Assert.Equal(1d / (dA + 1d), remaining[1], 4);

            // あとで D が上がっても、ボスの総HPは湧いた時点の D のまま。
            DriveRooms(host, a, 110, 3d, 2);
            Assert.True(FactorOf(host, a) > dA);
            float again = 1f;
            OverrideHealth(host, boss, ref again);
            Assert.Equal(multiplier, again, 6);
            Assert.Empty(Log.Warnings);
        }

        // ---- 報酬 ----

        [Fact]
        public void Kill_rewards_share_only_the_factor_gain_above_one()
        {
            var host = Host(out _);
            var a = Player(5);
            var b = Player(9);
            var c = Player(13);

            DriveRooms(host, a, 100, 3d, 2);   // a は D > 1
            DriveRooms(host, b, 110, 0.6d, 2); // b は D < 1
            Assert.True(FactorOf(host, a) > 1d);
            Assert.True(FactorOf(host, b) < 1d);

            var forA = SpawnMonsterFor(host, a, 200);
            var forB = SpawnMonsterFor(host, b, 201);
            var forC = SpawnMonsterFor(host, c, 202); // 戦っていない c は D = 1

            Assert.Equal(1d + RewardShare * (FactorOf(host, a) - 1d), RewardOf(host, forA), 5);
            Assert.Equal(1d, RewardOf(host, forB), 10); // D ≤ 1 では報酬を下げない
            Assert.Equal(1d, RewardOf(host, forC), 10);
            Assert.Empty(Log.Warnings);
        }

        // ---- 設定 ----

        [Fact]
        public void Multi_defaults_on_solo_defaults_off_and_ownership_off_does_nothing()
        {
            // マルチ（人間 2 人）は既定のままで有効。
            var host = Host(out _);
            var a = Player(5);
            Player(9);
            SpawnFor(host, a, 100);
            Assert.Equal(1, DynamicCount(host));

            // ソロは既定 OFF。
            DewPlayer.gamePlayers.Clear();
            host = Host(out _);
            a = Player(5);
            SpawnFor(host, a, 101);
            Assert.Equal(0, DynamicCount(host));

            // 「ソロでも使う」を ON にすると追跡する。
            host = Host(out _);
            a = Player(5);
            host.ConfigureDynamicDifficulty(true, true);
            SpawnFor(host, a, 102);
            Assert.Equal(1, DynamicCount(host));

            // 担当制 OFF のマルチでは何もしない。
            host = Host(out _);
            a = Player(5);
            Player(9);
            host.ConfigureEnemyOwnership(false);
            SpawnFor(host, a, 103);
            Assert.Equal(0, DynamicCount(host));
            Assert.Empty(Log.Warnings);
        }

        [Fact]
        public void Solo_mode_multiplies_the_room_pressure_by_the_factor()
        {
            var host = Host(out _);
            var a = Player(5);
            host.ConfigureDynamicDifficulty(true, true);
            var room = DreamPressure.ForPlayer(12, 30); // ソロには担当制がないので部屋の圧に D を掛ける
            Set(host, "_pressure", room);

            DriveRooms(host, a, 100, 3d, 2, 100d * room.HealthMultiplier);
            double factor = FactorOf(host, a);
            Assert.True(factor > 1d);

            var enemy = SpawnFor(host, a, 200, 200f);
            Assert.Equal(200f * room.HealthMultiplier * factor, RecalcMaxHealth(enemy), 1);
            var dealt = new DamageData(100f);
            foreach (var processor in enemy.dealtDamageProcessor.InOrder()) processor(ref dealt, enemy, enemy);
            Assert.Equal(100f * room.DamageMultiplier * Math.Pow(factor, DamageExponent), dealt.amount, 1);
            Assert.Empty(Log.Warnings);
        }
    }
}
