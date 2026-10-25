using System;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#1193）追尾弾の標的参照の辞書引きを使い回す NativeProjectileTargetSyncVarPerf の
    /// Prefix の分岐の検証。境界（NativeEmbracePositionBoundary.cs）のスタブ Projectile に
    /// 実物と同じ名前の Network_targetEntity getter と _targetEntity・____targetEntityNetId の
    /// 2 字段があるため、Prepare をそのまま通すと本番と同じ AccessTools の解決が済む
    /// （形が変われば Prepare が false を返してこの修正だけ外れる。ここでは Prepare が
    /// true を返すことも確かめる）。Prefix は本体（Mirror 生成の getter の辞書引き）に
    /// 任せるとき true を返し、解決済み参照を返せる純クライアントで両方が一致するときだけ
    /// false を返して __result にその参照を入れる。各分岐の戻り値と、false のときの
    /// __result が _targetEntity と同じ参照であることを確かめる。
    /// </summary>
    public sealed class ProjectileTargetSyncVarPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 毎テストで設定を有効に整え、Prepare を通して字段の読み取りを結び直す
        // （本番では Prepare が Harmony のパッチ適用前に同じことを行う）。
        public ProjectileTargetSyncVarPerfTests()
        {
            NativePerfOptions.ProjectileTargetClientFast = true;
            typeof(NativeProjectileTargetSyncVarPerf).GetField("_warned", Hidden).SetValue(null, false);
            Assert.True((bool)typeof(NativeProjectileTargetSyncVarPerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            NativePerfOptions.ProjectileTargetClientFast = true;
        }

        // Prefix を直接呼び、本体を走らせるか（true）・省くか（false）と __result を返す。
        private static bool Resolve(Projectile projectile, out Entity result)
        {
            object[] args = { projectile, null };
            bool runsOriginal = (bool)typeof(NativeProjectileTargetSyncVarPerf).GetMethod("Prefix", Hidden).Invoke(null, args);
            result = (Entity)args[1];
            return runsOriginal;
        }

        // 純クライアント（netIdentity があり、サーバーでなく、クライアントである）の土台。
        private static Projectile GuestProjectile()
            => new Projectile { netIdentity = new Mirror.NetworkIdentity(), isServer = false, isClient = true };

        // 標的の解決済み参照と SyncVar 値を一致する組で設定し、参照を返す。
        private static Entity SetTarget(Projectile projectile, uint netId, byte componentIndex)
        {
            var cached = new Entity { netId = netId, ComponentIndex = componentIndex };
            projectile._targetEntity = cached;
            projectile.____targetEntityNetId
                = new Mirror.NetworkBehaviourSyncVar { netId = netId, componentIndex = componentIndex };
            return cached;
        }

        // (1) 設定が無効なら、すべて一致していても常に本体経路（true）。
        [Fact]
        public void DisabledOptionRunsTheOriginalGetterEvenWhenEverythingMatches()
        {
            NativePerfOptions.ProjectileTargetClientFast = false;
            var projectile = GuestProjectile();
            SetTarget(projectile, 7, 3);

            Entity result;
            Assert.True(Resolve(projectile, out result));
            Assert.Null(result); // __result には触らない
        }

        // (2) ホスト（isServer）と非クライアントは対象外で、常に本体経路（true）。
        [Theory]
        [InlineData(true, true)]   // ホスト（ホストクライアントは isClient も true）
        [InlineData(false, false)] // まだクライアントとして動いていない
        public void HostOrNonClientRunsTheOriginalGetter(bool isServer, bool isClient)
        {
            var projectile = new Projectile
            {
                netIdentity = new Mirror.NetworkIdentity(),
                isServer = isServer,
                isClient = isClient
            };
            SetTarget(projectile, 7, 3);

            Entity result;
            Assert.True(Resolve(projectile, out result));
            Assert.Null(result);
        }

        // netIdentity がまだ無い（未スポーン）場合も本体経路。
        [Fact]
        public void MissingNetIdentityRunsTheOriginalGetter()
        {
            var projectile = new Projectile { isServer = false, isClient = true };
            SetTarget(projectile, 7, 3);

            Assert.True(Resolve(projectile, out Entity result));
            Assert.Null(result);
        }

        // (3) 解決済み参照が無い（まだ一度も設定されていない）ときは本体経路。
        [Fact]
        public void NullCachedTargetRunsTheOriginalGetter()
        {
            var projectile = GuestProjectile();
            projectile.____targetEntityNetId = new Mirror.NetworkBehaviourSyncVar { netId = 7, componentIndex = 3 };

            Assert.True(Resolve(projectile, out Entity result));
            Assert.Null(result);
        }

        // (4) SyncVar の netId が 0（未設定か despawn での Reset 済み）のときは、
        // 解決済み参照が残っていても本体経路。
        [Fact]
        public void ZeroSyncNetIdRunsTheOriginalGetter()
        {
            var projectile = GuestProjectile();
            SetTarget(projectile, 7, 3);
            projectile.____targetEntityNetId.netId = 0;

            Assert.True(Resolve(projectile, out Entity result));
            Assert.Null(result);
        }

        // (5) netId か componentIndex が食い違う（標的の差し替え・despawn 後の状態）ときは本体経路。
        [Theory]
        [InlineData(8, 3)] // netId の食い違い
        [InlineData(7, 4)] // componentIndex の食い違い
        public void MismatchedIdentityRunsTheOriginalGetter(int syncNetId, int syncComponentIndex)
        {
            var projectile = GuestProjectile();
            SetTarget(projectile, 7, 3);
            projectile.____targetEntityNetId = new Mirror.NetworkBehaviourSyncVar
            {
                netId = (uint)syncNetId,
                componentIndex = (byte)syncComponentIndex
            };

            Assert.True(Resolve(projectile, out Entity result));
            Assert.Null(result);
        }

        // (6) 純クライアントで netId・componentIndex の両方が一致するときだけ本体を省き（false）、
        // __result が解決済み参照と同じ参照になる。
        [Fact]
        public void MatchingTargetSkipsTheOriginalGetterAndReturnsTheCachedReference()
        {
            var projectile = GuestProjectile();
            var cached = SetTarget(projectile, 7, 3);

            Assert.False(Resolve(projectile, out Entity result));
            Assert.Same(cached, result);
        }
    }
}
