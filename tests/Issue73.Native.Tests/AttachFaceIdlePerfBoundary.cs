// （#760）NativePerfPatches.cs を直接コンパイルするための本体 API 境界。
// NativePerfOptions と NativePerfPatch はリンクした実ソースの定義を使い、
// Dew は EnemyOwnershipBoundary.cs の定義を使う。
using UnityEngine;

// 本体 Dew.Core/AttachToFace.cs の境界。Prefix が読むフィールドだけ。
internal class AttachToFace
{
    internal SkinnedMeshRenderer targetRenderer;
    internal int vertexIndex0;
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
