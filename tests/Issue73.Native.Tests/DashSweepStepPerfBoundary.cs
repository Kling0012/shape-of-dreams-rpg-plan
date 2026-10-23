using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

// NativeDashSweepStepPerf（NativePerfPatches.cs）を Issue73.Native.Tests で検証するための
// 本体 API 境界。DashAttackInstance をグローバル名前空間に置くのは、Prepare の
// AccessTools.TypeByName が実機の Dew.Core からこの名前（FullName）で解決するのと同じ
// 経路をテストでも通すため（ChaosShrineHitScanPerfBoundary.cs と同じ扱い）。
// NativePerfPatches.cs は MOD 側からはこの型を参照しないので、テストプロジェクトはこの
// 境界をこのファイルと、既存スタブへの継承の追加（NativeBoundary.cs の NetworkBehaviour、
// CharacterModelDisplayBoundsBoundary.cs の Transform.position）だけで持つ。
namespace UnityEngine
{
    // 実機では DewCollider も DamageInstance も UnityEngine.Component の子孫で、
    // range.transform も base.transform も Component.get_transform。パッチの Transpiler が
    // この呼び出しを形で探すため、ここにも同じ継承を置く。transform は実機では状態を持たない
    // プロパティだが、ここでは自分自身（this が Transform のとき）か遅延確保の子 Transform
    // （それ以外）を返す。遅延にするのは Transform : Component の初期化が再帰して
    // スタックを使い切らないため。
    internal class Component
    {
        private Transform _child;

        public Transform transform => this is Transform self ? self : (_child ??= new Transform());
    }

    // （#866）掃引の始点をずらす回転。Quaternion は CameraZoomRotationPerfBoundary.cs の
    // 既存の境界（op_Multiply は本体と同じ回転）を使うため、ここには置かない。
    // 基底の Component はここで宣言した部分宣言だけに書く（ほかの部分宣言は基底を書かない）。
    internal partial class Transform : Component
    {
        internal Quaternion rotation { get; set; }

        // NativeMerchantPropIdlePerf の SetIfChanged が本体経路へ戻るときの受け口。実機は
        // 位置と回転を1回のネイティブ呼び出しで書くが、境界では position / rotation の
        // 各プロパティへ写すだけでよい（境界のプロパティはただの値）。
        internal void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }
}

// 本体 Dew.Core DewCollider の、ActiveLogicUpdate が読む範囲の境界。実物は UnityEngine 側の
// コンポーネント（range.transform が Component.get_transform）。shape は本体と同じ 3 種で、
// 掃引の歩幅の決め方（Circle=radius、Box=size の短い方の半分）だけを写す。
internal class DewCollider : UnityEngine.Component
{
    internal enum ColliderShape { Circle, Box, Polygon }

    internal ColliderShape shape = ColliderShape.Circle;
    internal float radius;
    internal Vector2 size;
}

// 本体 Dew.Core DamageInstance の、DashAttackInstance.ActiveLogicUpdate が触れる範囲の境界。
// 実機と同じく Mirror.NetworkBehaviour の子孫（isServer と transform を継承）。
// DoCollisionChecks と range はこの基本クラスで宣言する（パッチの Transpiler が基本クラスから
// この 2 つを探す）。DoCollisionChecks は各回の range 位置を記録し、collisionCheckLimit を
// 超えた次の呼び出しで例外を出す番兵にもなる（dist が NaN の掃引は本体でも終わらないため、
// その観測のためだけの借り物で、本体にこの番兵はない）。position は実機と同じく transform を
// 読まないただの値にする（get_position の呼び出し回数の照合を壊さないため）。
internal class DamageInstance : NetworkBehaviour
{
    internal readonly DewCollider range = new DewCollider();
    internal float creationTime;
    internal bool shouldBeDestroyed;
    internal bool isDestroyed;
    internal int collisionCheckLimit;
    internal readonly List<Vector3> checkPositions = new List<Vector3>();
    internal Vector3 position { get; set; }

    internal bool CheckShouldBeDestroyed() => shouldBeDestroyed;
    internal void Destroy() => isDestroyed = true;
    internal virtual void ActiveLogicUpdate(float dt) { }

    internal void DoCollisionChecks()
    {
        if (collisionCheckLimit > 0 && checkPositions.Count >= collisionCheckLimit)
            throw new InvalidOperationException("dash sweep sentinel at check #" + (checkPositions.Count + 1));
        checkPositions.Add(range.transform.position);
    }
}

// 本体 Dew.Core r1.4.1.2 DashAttackInstance.ActiveLogicUpdate（逆コンパイル
// DashAttackInstance.cs）の掃引部分の忠実な転写。等価性テストの「パッチ無効側」として動き、
// NativeDashSweepStepPerf の Transpiler が差し込む先もこれと同じ形の IL になる。
// 前段（猶予と破棄判定）は掃引と無関係だが、本体の分岐と同じ場所に同じ形で置く。
// ループの各歩は判定（DoCollisionChecks）→ 歩数の加算 → 範囲の移動の順で、最後の 1 歩の
// 移動は直後の localPosition の復元で必ず上書きされ、tick の中でその後読む者はいない。
internal class DashAttackInstance : DamageInstance
{
    public float damageGracePeriod = 0.075f;

    internal bool _isDashing;

    internal Vector3 _lastPosition;

    internal override void ActiveLogicUpdate(float dt)
    {
        base.ActiveLogicUpdate(dt);
        if (!isServer || !_isDashing || Time.time - creationTime < damageGracePeriod)
        {
            return;
        }
        if (CheckShouldBeDestroyed())
        {
            Destroy();
            return;
        }
        Vector3 localPosition = range.transform.localPosition;
        Vector3 vector = _lastPosition + base.transform.rotation * localPosition;
        Vector3 vector2 = range.transform.position;
        float num = 0f;
        float num2 = Vector3.Distance(vector, vector2);
        float num3 = 2.5f;
        switch (range.shape)
        {
        case DewCollider.ColliderShape.Circle:
            num3 = range.radius;
            break;
        case DewCollider.ColliderShape.Box:
            num3 = Mathf.Min(range.size.x, range.size.y) * 0.5f;
            break;
        default:
            throw new ArgumentOutOfRangeException();
        case DewCollider.ColliderShape.Polygon:
            break;
        }
        range.transform.position = vector;
        do
        {
            DoCollisionChecks();
            num += num3;
            range.transform.position += (vector2 - vector).normalized * num3;
        }
        while (!(num > num2));
        range.transform.localPosition = localPosition;
        _lastPosition = base.position;
    }

    // 窓の形が変わった本体の見本（拒否の見本）。本体の掃引に position の読み出しを 1 つ
    // 混ぜただけの形で、Transpiler の回数照合（get_position は 2 回ちょうど）に引っ掛かる。
    // 実行はしない（Transpiler に渡す IL の取り出しだけに使う）。
    internal void SweepWithAnExtraPositionRead()
    {
        Vector3 localPosition = range.transform.localPosition;
        Vector3 vector = _lastPosition + base.transform.rotation * localPosition;
        Vector3 vector2 = range.transform.position;
        Vector3 extra = range.transform.position;
        float num = 0f;
        float num2 = Vector3.Distance(vector, vector2) + extra.x * 0f;
        float num3 = 2.5f;
        range.transform.position = vector;
        do
        {
            DoCollisionChecks();
            num += num3;
            range.transform.position += (vector2 - vector).normalized * num3;
        }
        while (!(num > num2));
        range.transform.localPosition = localPosition;
    }
}
