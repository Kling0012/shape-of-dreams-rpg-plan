// NativeStealthRevealOverlapPerf（src/SodRpg.Mod/NativeStealthReveal.cs、リンク）を
// Issue73.Native.Tests で検証するための本体 API 境界。StealthRevealOverlapPerfTests が
// 円クエリの結果（コライダー並び）をここへ差し込み、打ち切り版と本体側の写しを同じ入力で
// 走らせて比べる。ここには本体の処理を書き直さない: 走査ループはリンクされた実ソース側と、
// この下の DewPhysics.OverlapCircleAllEntities の写しが実行する。
// 本体側の写しは実 DLL（Dew.Core、/tmp/dewdec の逆コンパイル一式と同じ版）の
// 分岐の忠実な転写で、等価性検証の「パッチ無効側」として動く。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    // 実機 UnityEngine.ContactFilter2D のうち、この検証が通しで運ぶだけの形。
    // 中身は誰も読まない（DewPhysics.EntityFilter から Physics2D.OverlapCircle へ
    // そのまま流れるため、打ち切り版と本体側で同じ値が同じ引数位置に渡ることだけが要件）。
    internal struct ContactFilter2D { }

    // 実機 UnityEngine.Collider2D。本体ではエンティティのコライダーは ProxyCollider
    // （DewPhysics.TryGetProxy の辞書と TryGetComponent）を経て Entity に引き当てられる。
    // この境界ではその引き当て結果だけを欄として持つ（entity が null なら非エンティティ）。
    internal class Collider2D
    {
        internal SodRpg.Mod.Entity entity;
    }

    // 実機 UnityEngine.Physics2D.OverlapCircle の境界。実機はシーンから円内のコライダーを
    // 集めるが、ここでは Scene へ差し込んだ並びをそのまま返す。中心・半径・呼び出し回数の
    // 記録は、打ち切り版が本体と同じ問いかけをしていることの確認に使う。
    internal static class Physics2D
    {
        internal static readonly List<Collider2D> Scene = new List<Collider2D>();
        internal static Vector2 LastPoint;
        internal static float LastRadius;
        internal static int Queries;

        public static int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter, List<Collider2D> results)
        {
            Queries++;
            LastPoint = point;
            LastRadius = radius;
            results.Clear();
            results.AddRange(Scene);
            return results.Count;
        }
    }
}

namespace SodRpg.Mod
{
    // 本体 Dew.Core IBinaryEntityValidator と同じ形。
    internal interface IBinaryEntityValidator
    {
        bool Evaluate(Entity self, Entity target);
    }

    // 本体 Dew.Core CollisionCheckSettings（struct）のうち、走査の述語が読む2字段。
    internal struct CollisionCheckSettings
    {
        public bool includeUncollidable;
        public IComparer<Entity> sortComparer;
    }

    // 本体 Dew.Core ListReturnHandle<T>（struct）と同じ意味: 借りたリストを1回だけ返せる。
    // needToReturn は「取得済みでまだ返していない」か（default でも安全）。返却は DewPool の
    // 貸し借り台帳（Outstanding）を1つ戻す。二重返却は本体と同じく何もしない
    // （本体は警告を出す。この境界では黙って無視する）。
    internal struct ListReturnHandle<T>
    {
        private readonly List<T> _resource;
        private bool _isReturned;

        internal ListReturnHandle(List<T> resource)
        {
            _resource = resource;
            _isReturned = false;
        }

        public bool needToReturn => _resource != null && !_isReturned;

        public void Return()
        {
            if (_isReturned) return;
            _isReturned = true;
            _resource.Clear();
            DewPool.Release(_resource);
        }
    }

    // 本体 Dew.Core DewPool の境界。実機はコレクションプールだが、この検証では
    // 「借りて返していないリストの数」だけを数える（例外経路の返し漏れの確認用）。
    internal static class DewPool
    {
        internal static int Outstanding;

        public static List<T> GetList<T>(out ListReturnHandle<T> handle)
        {
            Outstanding++;
            List<T> list = new List<T>();
            handle = new ListReturnHandle<T>(list);
            return list;
        }

        internal static void Release<T>(List<T> list) => Outstanding--;
    }

    // NativeBoundary.cs の DewPhysics への追加（#988）。NativeStealthReveal.cs（リンク）が
    // 読む本体 API のうち、この検証に必要な範囲。
    internal static partial class DewPhysics
    {
        // 本体 DewPhysics.EntityFilter（private static プロパティ）と同じ意味。
        // NativeStealthRevealOverlapPerf.Prepare が PropertyGetter で解決してコンパイル済み
        // デリゲートで読む。実機はレイヤー設定を持つ ContactFilter2D を返すが、この境界の
        // ContactFilter2D は中身を持たないため、空の値を返す（上の struct のコメント参照）。
        private static ContactFilter2D EntityFilter => new ContactFilter2D();

        // 本体 DewPhysics.TryGetEntity と同じ意味（プロキシ引き当ての結果だけを境界の
        // Collider2D.entity 欄が持つ。コライダー・エンティティどちらかでも null なら偽）。
        public static bool TryGetEntity(Collider2D collider, out Entity entity)
        {
            if (collider == null || collider.entity == null)
            {
                entity = null;
                return false;
            }
            entity = collider.entity;
            return true;
        }

        // 本体 Dew.Core DewPhysics.OverlapCircleAllEntities（IBinaryEntityValidator の6引数
        // オーバーロード）の転写。全件集めて SortList を通して返す。検証の「本体側」。
        public static List<Entity> OverlapCircleAllEntities(out ListReturnHandle<Entity> handle, Vector3 center, float radius, IBinaryEntityValidator validator, Entity self, CollisionCheckSettings settings)
        {
            List<Collider2D> list = DewPool.GetList(out ListReturnHandle<Collider2D> handle2);
            Physics2D.OverlapCircle(center.ToXY(), radius, EntityFilter, list);
            List<Entity> list2 = DewPool.GetList(out handle);
            for (int i = 0; i < list.Count; i++)
            {
                if (TryGetEntity(list[i], out var entity) && entity.isActive && validator.Evaluate(self, entity) && (settings.includeUncollidable || !entity.Status.hasUncollidable))
                {
                    list2.Add(entity);
                }
            }
            handle2.Return();
            SortList(settings, list2, center);
            return list2;
        }

        // 本体 DewPhysics.SortList のうち、この呼び出し経路に効く最初の行だけの写し。
        // 呼び出し元（Se_GenericStealth.ActiveLogicUpdate）は sortComparer を渡さない
        // （null）ため、本体では即座に return する。null でない整列はこの検証の範囲外。
        private static void SortList(CollisionCheckSettings settings, List<Entity> list, Vector3 distanceCheckPivot)
        {
            if (settings.sortComparer == null)
            {
                return;
            }
            throw new NotSupportedException("sorted overlap results are outside the boundary");
        }
    }

    // NativeBoundary.cs の EntityStatus への追加（#988）。本体 Dew.Core EntityStatus の
    // hasUncollidable（公開フィールド）と同じ意味。
    internal partial class EntityStatus
    {
        public bool hasUncollidable;
    }

    // 本体 Dew.Core Se_GenericStealth（StatusEffect）のうち、NativeStealthRevealOverlapPerf の
    // Prepare が探す宣言（ActiveLogicUpdate(float) に本体があること）と、Transpiler が差し替える
    // 円クエリの呼び出し形状（6引数の IBinaryEntityValidator オーバーロード）の再現。
    // ActiveLogicUpdate の中身は本体末尾（猶予期間と 0.25 秒判定の後の円クエリ）の転写。
    // 実機の _revealable は AbilityTargetValidator（targets = EntityRelation.Enemy）だが、
    // この境界では IBinaryEntityValidator として常に真の判決を置く（この検証は
    // ActiveLogicUpdate を実行しない。Prepare の形検査だけがこの型を読む）。
    // FxPlayNetworked と Reveal は呼び出し回数だけ数える。
    internal class Se_GenericStealth : StatusEffect
    {
        private sealed class AnyTargetValidator : IBinaryEntityValidator
        {
            public bool Evaluate(Entity self, Entity target) => true;
        }

        public float revealRange = 1.5f;
        public float revealOnProximityGracePeriod = 0.65f;
        public float revealDuration = 2f;
        internal float _lastRevealCheckTime;
        private bool _isRevealRangeIndicatorActive;
        internal readonly IBinaryEntityValidator _revealable = new AnyTargetValidator();
        internal int Reveals, IndicatorFxPlays;

        internal void FxPlayNetworked(object fx, Entity target) => IndicatorFxPlays++;

        internal void Reveal(Entity target, float duration) => Reveals++;

        protected void ActiveLogicUpdate(float dt)
        {
            if (!isServer || Time.time - creationTime < revealOnProximityGracePeriod)
            {
                return;
            }
            if (!_isRevealRangeIndicatorActive)
            {
                _isRevealRangeIndicatorActive = true;
                FxPlayNetworked(null, victim);
            }
            if (!(Time.time - _lastRevealCheckTime < 0.25f))
            {
                if (DewPhysics.OverlapCircleAllEntities(out var handle, victim.agentPosition, revealRange, _revealable, victim, new CollisionCheckSettings
                {
                    includeUncollidable = true
                }).Count > 0)
                {
                    Reveal(victim, revealDuration);
                }
                handle.Return();
            }
        }
    }
}
