// （#760）本体 NativePerfPatches.cs の NativeAttachFaceIdlePerf / NativeAttachFaceBakeRecord
// の抽出部分から参照される本体側の型の境界。NativePerfOptions と NativePerfPatch は
// SleepPositionPerfBoundary.cs、Dew は EnemyOwnershipBoundary.cs の既存の定義に
// BakeMeshInterval を足したものを使う。Warn のログ出力は本体の Log 依存のためここには置かない。
using UnityEngine;

// 本体 Dew.Core/AttachToFace.cs の境界。抽出した Prefix が読むフィールドだけ。
internal class AttachToFace
{
    internal SkinnedMeshRenderer targetRenderer;
    internal int vertexIndex0;
}

namespace UnityEngine
{
    // 本体の SkinnedMeshRenderer の境界。GetInstanceID はハーネス内で一意の連番を返す。
    internal class SkinnedMeshRenderer
    {
        private static int nextInstanceID;
        private readonly int instanceID = ++nextInstanceID;

        public int GetInstanceID() => instanceID;
    }
}
