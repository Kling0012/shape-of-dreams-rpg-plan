using System;
using System.Collections.Generic;
using System.Reflection;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// （#988）ステルスの接近リビール走査を最初の1体で打ち切る
    /// NativeStealthRevealOverlapPerf.OverlapFirstEntities の検証。境界
    /// （StealthRevealOverlapPerfBoundary.cs）の写し DewPhysics.OverlapCircleAllEntities
    /// （本体側）と同じコライダー並び・同じ述語に対して比べる:
    /// (a) 条件に合うエンティティが 0・1・複数体のいずれでも Count &gt; 0 の真偽が一致し、
    ///     打ち切り版は1体目で止まる。問いかけ（中心・半径）も同じ。
    /// (b) 非エンティティ・非アクティブ・述語不一致・hasUncollidable が混ざる並びでも、
    ///     結果と述語の呼ばれ方（TryGetEntity → isActive → Evaluate → hasUncollidable の
    ///     短絡の順序）が本体側と一致する。includeUncollidable の両値で確かめる。
    /// (c) 設定（StealthRevealOverlapScan）を切ると本体実装へ委譲する（全件集めになる）。
    /// (d) 例外が出たらその場で本体実装へ戻り、以後も本体実装のまま（無効化の継続）で、
    ///     借りていたプールのリスト2本は返却される（返し漏れなし）。
    /// </summary>
    public sealed class StealthRevealOverlapPerfTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const float Radius = 1.5f;
        // ToXY で (3, -2) になる中心。問いかけの引数が両経路で同じことを 0 以外の値で確かめる。
        private static readonly Vector3 Center = new Vector3(3f, 7f, -2f);

        // 打ち切り版は静的な欄（_disabled・_warned）と設定を持つため、毎テストで戻してから
        // 実 Prepare を境界の型に対して通す（_entityFilter のデリゲートがここで結ばれる）。
        // Dispose で欄と設定を既定（有効）へ戻す。
        public StealthRevealOverlapPerfTests()
        {
            Reset();
            NativePerfOptions.StealthRevealOverlapScan = true;
            Assert.True((bool)typeof(NativeStealthRevealOverlapPerf).GetMethod("Prepare", Hidden).Invoke(null, null));
        }

        public void Dispose()
        {
            Reset();
            NativePerfOptions.StealthRevealOverlapScan = true;
        }

        private static void Reset()
        {
            var type = typeof(NativeStealthRevealOverlapPerf);
            type.GetField("_disabled", Hidden).SetValue(null, false);
            type.GetField("_warned", Hidden).SetValue(null, false);
        }

        // 検証用の述語。呼ばれた順に対象を記録し、対象ごとの判決を差し替えられる。
        // ThrowsBeforeSuccess は最初のその回数の呼び出しだけ例外を出す（例外経路の再現）。
        private sealed class RecordingValidator : IBinaryEntityValidator
        {
            public readonly List<Entity> Evaluated = new List<Entity>();
            public Func<Entity, bool> Verdict = _ => true;
            public int ThrowsBeforeSuccess;

            public bool Evaluate(Entity self, Entity target)
            {
                if (ThrowsBeforeSuccess > 0)
                {
                    ThrowsBeforeSuccess--;
                    throw new InvalidOperationException("validator boom");
                }
                Evaluated.Add(target);
                return Verdict(target);
            }
        }

        // 円内のコライダー並びを差し込む検証の現場。始まりと終わりにプールの貸し借りが
        // 釣り合っていること（全テストの不変）も確かめる。
        private sealed class Scene : IDisposable
        {
            public Scene(params Collider2D[] colliders)
            {
                Assert.Equal(0, DewPool.Outstanding);
                Physics2D.Scene.Clear();
                Physics2D.Scene.AddRange(colliders);
            }

            public void Dispose()
            {
                Physics2D.Scene.Clear();
                Assert.Equal(0, DewPool.Outstanding);
            }
        }

        private static Collider2D ColliderOf(Entity entity) => new Collider2D { entity = entity };

        // (a) 条件に合うエンティティが 0・1・複数体のとき、Count > 0 の真偽が一致する。
        // 複数体では打ち切り版は1体目で止まり、問いかけ（中心・半径）は同じものを渡す。
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void CountTruthinessMatchesTheNativeQuery(int qualifying)
        {
            var entities = new List<Entity>();
            var colliders = new List<Collider2D>();
            for (int i = 0; i < qualifying; i++)
            {
                var entity = new Entity();
                entities.Add(entity);
                colliders.Add(ColliderOf(entity));
            }
            using (new Scene(colliders.ToArray()))
            {
                var settings = new CollisionCheckSettings { includeUncollidable = true };
                var nativeValidator = new RecordingValidator();
                var earlyValidator = new RecordingValidator();
                int queriesBefore = Physics2D.Queries;

                var nativeList = DewPhysics.OverlapCircleAllEntities(out var nativeHandle, Center, Radius, nativeValidator, null, settings);
                var nativeCount = nativeList.Count;
                var nativePoint = Physics2D.LastPoint;
                var nativeRadius = Physics2D.LastRadius;
                var nativeQueries = Physics2D.Queries - queriesBefore;
                nativeHandle.Return();

                var earlyList = NativeStealthRevealOverlapPerf.OverlapFirstEntities(out var earlyHandle, Center, Radius, earlyValidator, null, settings);
                var earlyCount = earlyList.Count;
                var earlyQueries = Physics2D.Queries - nativeQueries - queriesBefore;
                earlyHandle.Return();

                Assert.Equal(nativeCount > 0, earlyCount > 0);
                // 打ち切り: 複数体では1体目だけ、それ未満では本体と同数。
                Assert.Equal(qualifying > 1 ? 1 : qualifying, earlyCount);
                // 同じ問いかけを同じ回数（1回）だけ出す。
                Assert.Equal(nativeQueries, earlyQueries);
                Assert.Equal(1, earlyQueries);
                Assert.Equal(nativePoint, Physics2D.LastPoint);
                Assert.Equal(nativeRadius, Physics2D.LastRadius);
            }
        }

        // (b) 非エンティティ・非アクティブ・述語不一致・hasUncollidable の除外の順序が同じ。
        // 述語の呼ばれ方で短絡の順序を確かめる: 非アクティブは Evaluate に届かず、
        // hasUncollidable による除外は Evaluate を通った後に起きる。
        [Fact]
        public void ExclusionOrderMatchesTheNativeQuery()
        {
            var nonEntity = new Collider2D();
            var inactive = new Entity { isActive = false };
            var rejected = new Entity();
            var uncollidable = new Entity();
            uncollidable.Status.hasUncollidable = true;
            var qualifying = new Entity();
            var early = new Entity();
            using (new Scene(nonEntity, ColliderOf(inactive), ColliderOf(rejected), ColliderOf(uncollidable), ColliderOf(qualifying), ColliderOf(early)))
            {
                var settings = new CollisionCheckSettings { includeUncollidable = false };
                var nativeValidator = new RecordingValidator { Verdict = e => e != rejected };
                var earlyValidator = new RecordingValidator { Verdict = e => e != rejected };

                var nativeList = DewPhysics.OverlapCircleAllEntities(out var nativeHandle, Center, Radius, nativeValidator, null, settings);
                var nativeResult = new List<Entity>(nativeList);
                var nativeEvaluated = new List<Entity>(nativeValidator.Evaluated);
                nativeHandle.Return();

                var earlyList = NativeStealthRevealOverlapPerf.OverlapFirstEntities(out var earlyHandle, Center, Radius, earlyValidator, null, settings);
                var earlyResult = new List<Entity>(earlyList);
                var earlyEvaluated = new List<Entity>(earlyValidator.Evaluated);
                earlyHandle.Return();

                // 本体側: 述語不一致と hasUncollidable は除外され、条件に合うものを全件集める。
                // 打ち切り版は同じ除外のうえ条件に合う1体目（qualifying）で止まる。
                Assert.Equal(new[] { qualifying, early }, nativeResult);
                Assert.Equal(new[] { qualifying }, earlyResult);
                // 述語の呼ばれ方（順序・回数）は、打ち切り版では1体目までの同じ並びになる。
                Assert.Equal(new[] { rejected, uncollidable, qualifying }, earlyEvaluated);
                Assert.Equal(earlyEvaluated, nativeEvaluated.GetRange(0, earlyEvaluated.Count));
                // 短絡の順序: 非アクティブは Evaluate に届かず、hasUncollidable は届く。
                Assert.DoesNotContain(inactive, earlyEvaluated);
                Assert.Contains(uncollidable, earlyEvaluated);
            }

            // includeUncollidable を立てると、本体側も打ち切り側も uncollidable を数え始める
            // （打ち切り版の1体目は uncollidable になる）。
            using (new Scene(ColliderOf(uncollidable), ColliderOf(early)))
            {
                var settings = new CollisionCheckSettings { includeUncollidable = true };
                var nativeList = DewPhysics.OverlapCircleAllEntities(out var nativeHandle, Center, Radius, new RecordingValidator(), null, settings);
                var nativeCount = nativeList.Count;
                nativeHandle.Return();
                var earlyList = NativeStealthRevealOverlapPerf.OverlapFirstEntities(out var earlyHandle, Center, Radius, new RecordingValidator(), null, settings);
                var earlyCount = earlyList.Count;
                earlyHandle.Return();
                Assert.Equal(2, nativeCount);
                Assert.Equal(1, earlyCount);
                Assert.Equal(nativeCount > 0, earlyCount > 0);
            }
        }

        // (c) 設定（StealthRevealOverlapScan）を切ると本体実装へ委譲する。
        // 全件集めの結果（2体）と全件評価（2回）が返り、打ち切り（1体・1回）にならない。
        [Fact]
        public void ADisabledOptionDelegatesToTheNativeQuery()
        {
            var first = new Entity();
            var second = new Entity();
            using (new Scene(ColliderOf(first), ColliderOf(second)))
            {
                NativePerfOptions.StealthRevealOverlapScan = false;
                var validator = new RecordingValidator();
                var list = NativeStealthRevealOverlapPerf.OverlapFirstEntities(out var handle, Center, Radius, validator, null, new CollisionCheckSettings { includeUncollidable = true });
                var result = new List<Entity>(list);
                handle.Return();
                Assert.Equal(new[] { first, second }, result);
                Assert.Equal(2, validator.Evaluated.Count);
            }
        }

        // (d) 例外が出たら、その場は本体実装の結果を返し、以後も本体実装のまま（無効化の
        // 継続）で、借りていたプールのリスト2本（コライダー用とエンティティ用）は返却される。
        [Fact]
        public void AnExceptionFallsBackToTheNativeQueryAndReturnsThePoolLists()
        {
            var first = new Entity();
            var second = new Entity();
            using (new Scene(ColliderOf(first), ColliderOf(second)))
            {
                var settings = new CollisionCheckSettings { includeUncollidable = true };
                var validator = new RecordingValidator { ThrowsBeforeSuccess = 1 };
                var type = typeof(NativeStealthRevealOverlapPerf);

                var firstList = NativeStealthRevealOverlapPerf.OverlapFirstEntities(out var firstHandle, Center, Radius, validator, null, settings);
                var firstResult = new List<Entity>(firstList);
                firstHandle.Return();
                // その場のフォールバックは本体実装: 全件（2体）を返す（打ち切りなら1体）。
                Assert.Equal(new[] { first, second }, firstResult);
                Assert.True((bool)type.GetField("_disabled", Hidden).GetValue(null));
                Assert.True((bool)type.GetField("_warned", Hidden).GetValue(null));
                // 例外で中断された走査が借りたリスト2本が返却されている（返し漏れなし）。
                Assert.Equal(0, DewPool.Outstanding);

                var secondValidator = new RecordingValidator();
                var secondList = NativeStealthRevealOverlapPerf.OverlapFirstEntities(out var secondHandle, Center, Radius, secondValidator, null, settings);
                var secondResult = new List<Entity>(secondList);
                secondHandle.Return();
                // 無効化は継続し、以後も本体実装のまま（全件・全件評価。打ち切りなら1体・1回）。
                Assert.Equal(new[] { first, second }, secondResult);
                Assert.Equal(2, secondValidator.Evaluated.Count);
            }
        }
    }
}
