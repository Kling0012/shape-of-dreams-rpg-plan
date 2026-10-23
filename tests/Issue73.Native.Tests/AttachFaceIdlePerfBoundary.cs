// （#760）NativePerfPatches.cs を直接コンパイルするための本体 API 境界。
// NativePerfOptions と NativePerfPatch はリンクした実ソースの定義を使い、
// Dew は EnemyOwnershipBoundary.cs の定義、Room は NativeBoundary.cs の定義を使う。
using System;
using System.Collections.Generic;
using UnityEngine;

// 本体 Dew.Core/AttachToFace.cs の境界。2つの Prefix（変わらない焼き直しの省略と、
// 焼き直しフレームの頂点コピー共有）が読むフィールドだけ。
internal class AttachToFace
{
    internal SkinnedMeshRenderer targetRenderer;
    internal int vertexIndex0;
    // NativeAttachFaceSharedVerts の Prefix が読む残りの公開フィールド（実物は public）。
    internal int vertexIndex1;
    internal int vertexIndex2;
    internal Vector3 relPosition;
    internal Quaternion relRotation;
}

// 本体 Dew.Core/RoomComponent.cs と RoomComponentStartDependencyAttribute.cs の境界。
// NativeRoomStartDependencyPerf が依存関係の並べ替えで使う型と属性の実体だけ。
// AttributeUsage は本体と同じ AllowMultiple = true（Inherited は省略時の true）。
internal class RoomComponent
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class RoomComponentStartDependencyAttribute : Attribute
{
    internal Type targetRoomComponent;

    internal RoomComponentStartDependencyAttribute(Type targetRoomComponent)
    {
        this.targetRoomComponent = targetRoomComponent;
    }
}

namespace UnityEngine
{
    // NativeAttachFaceSharedVerts の Prefix が isNew フレームで呼ぶ受け口。
    // 模倣の GetBakedMeshOptimized は常に isNew=false を返すため、この受け口は呼ばれない。
    internal class Mesh
    {
        internal void GetVertices(List<Vector3> vertices) { }
    }

    // 本体の SkinnedMeshRenderer の境界。GetInstanceID はハーネス内で一意の連番を返す。
    // NativeAttachFaceSharedVerts の Prefix が transform を読む分だけ持つ。
    internal class SkinnedMeshRenderer
    {
        private static int nextInstanceID;
        private readonly int instanceID = ++nextInstanceID;

        internal Transform transform = new Transform();

        public int GetInstanceID() => instanceID;
    }
}
