using System;
using UnityEngine;

// NativeFxAttachIdlePerf（NativePerfPatches.cs）を Issue73.Native.Tests で直接検証するための
// 境界。ラッパー（WriteLocalScale / WritePosition / WritePositionAndRotation）が読み書きする
// Transform の各プロパティ本体は既存の部分宣言（localScale は CastIndicatorPerfBoundary.cs、
// position は CharacterModelDisplayBoundsBoundary.cs、rotation と SetPositionAndRotation は
// DashSweepStepPerfBoundary.cs）が持つ。ここに置くのは:
//  - MissingReferenceException（実機 UnityEngine の破棄済みアクセスで出る例外の境界。
//    実機と同じく System.InvalidOperationException からの派生にする）
//  - throwOnRead / throwOnWrite（破棄済み Transform へのアクセスを模す検証用の鉤。
//    上の3つの部分宣言のプロパティが参照する。null なら何もしないので、既存のテストの
//    挙動は変わらない）
//  - FxAttachToEntity の書き込み指紋の見本（本体 UpdatePosition の末尾と同じ 4/1/1 並びの
//    見本と、set_position を1つ増らした拒否の見本。実行はしない——Transpiler に渡す IL の
//    取り出しだけに使う）
namespace UnityEngine
{
    internal class MissingReferenceException : InvalidOperationException { }

    internal partial class Transform
    {
        internal Exception throwOnRead;
        internal Exception throwOnWrite;
    }
}

namespace SodRpg.Mod
{
    internal partial class FxAttachToEntity
    {
        // 本体 UpdatePosition の末尾の書き込み並び（set_localScale 4・set_position 1・
        // SetPositionAndRotation 1、すべて Transform 宛て）と同じ形の見本。
        internal void TailWritesLikeShippedUpdatePosition(Transform tr, Vector3 scale, Vector3 pos, Quaternion rot)
        {
            tr.localScale = scale;
            tr.localScale = scale;
            tr.localScale = scale;
            tr.localScale = scale;
            tr.position = pos;
            tr.SetPositionAndRotation(pos, rot);
        }

        // 書き込みの数が変わった本体の見本（拒否の見本）: set_position を2つに増やして
        // 指紋（4/1/1）を外しただけの形。Transpiler はこれを1つも替えてはならない。
        internal void TailWritesWithAnExtraPositionWrite(Transform tr, Vector3 scale, Vector3 pos, Quaternion rot)
        {
            tr.localScale = scale;
            tr.localScale = scale;
            tr.localScale = scale;
            tr.localScale = scale;
            tr.position = pos;
            tr.position = pos;
            tr.SetPositionAndRotation(pos, rot);
        }
    }
}
