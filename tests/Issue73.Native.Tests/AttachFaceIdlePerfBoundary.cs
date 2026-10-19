// （#760）NativePerfPatches.cs を直接コンパイルするための本体 API 境界。
// NativePerfOptions と NativePerfPatch はリンクした実ソースの定義を使い、
// Dew は EnemyOwnershipBoundary.cs の定義、Room は NativeBoundary.cs の定義を使う。
using System;
using UnityEngine;

// 本体 Dew.Core/AttachToFace.cs の境界。Prefix が読むフィールドだけ。
internal class AttachToFace
{
    internal SkinnedMeshRenderer targetRenderer;
    internal int vertexIndex0;
}

// 本体 Dew.Core/RoomComponent.cs と RoomComponentStartDependencyAttribute.cs の境界。
// NativeRoomStartDependencyPerf が依存関係の並べ替えで使う型と属性の実体だけ。
internal class RoomComponent
{
}

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
    internal class Mesh { }

    // 本体の SkinnedMeshRenderer の境界。GetInstanceID はハーネス内で一意の連番を返す。
    internal class SkinnedMeshRenderer
    {
        private static int nextInstanceID;
        private readonly int instanceID = ++nextInstanceID;

        public int GetInstanceID() => instanceID;
    }
}
