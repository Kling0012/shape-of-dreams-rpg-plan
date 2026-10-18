using DTT.AreaOfEffectRegions;
using UnityEngine;

// （#766）本体 NativePerfPatches.cs の NativeCastIndicatorCirclePerf のうち、抽出部分
// （ValidateBody・ShouldSkip）から参照される範囲の境界。NativePerfOptions・NativePerfPatch・
// UnityEngine.Vector2 は SleepPositionPerfBoundary.cs と NativeBoundary.cs の既存の境界を
// そのまま使うため、ここには重複して置かない。
namespace UnityEngine
{
    // 本体の ValidateBody が Material.SetFloat(int, float) のオーバーロードを解決するのに
    // 必要なシグネチャだけの境界。呼び出しの有無は ValidateBody が IL 命令で数えるため、
    // 中身は空でよい。ShouldSkip は実体の同一性だけを見るので状態は要らない。
    internal sealed class Material
    {
        internal void SetFloat(int propertyId, float value) { }
    }
}

// 本体の DTT.AreaOfEffectRegions（照準円の領域クラス）と同じ意味の境界。ValidateBody が
// typeof(DTT.AreaOfEffectRegions.…) と AccessTools.Field(CircleRegion, "_fillProgress") で
// 完全修飾で参照するため、この名前空間にこの名前で置く。
namespace DTT.AreaOfEffectRegions
{
    internal class CircleRegionBase
    {
        internal float Radius { get; set; }
        internal UnityEngine.Vector2 Offset { get; set; }
    }

    internal sealed class CircleRegion : CircleRegionBase
    {
        internal float _fillProgress;
        // 見本の書き込み先。読み出し回数の検証にだけ使うので中身の意味はない。
        internal float CircleScale;
        internal UnityEngine.Vector2 CirclePosition;
    }
}

namespace Issue73.Native.Tests
{
    // 本体 DTT.AreaOfEffectRegions.CircleRegion.UpdateProperties と同じ読み出し回数の見本。
    // ValidateBody は get_Radius 1回・get_Offset 2回・Material.SetFloat(int,float) 1回・
    // _fillProgress 読み出し（ldfld）1回という形だけを手がかりに判定するため、
    // 拒否の見本はそれぞれ1カ所だけ回数を崩している。
    internal sealed class CastIndicatorIlShapes
    {
        internal const int FillPropertyId = 7;

        // 本体と同じ読み出し回数（Radius 1・Offset 2・SetFloat 1・_fillProgress 1）→ 受理。
        internal void SameShape(CircleRegion region, Material material)
        {
            region.CircleScale = region.Radius;
            region.CirclePosition = new Vector2(region.Offset.x, region.Offset.y);
            material.SetFloat(FillPropertyId, region._fillProgress);
        }

        // Radius の読み出しが2回 → 拒否。
        internal void SecondRadiusRead(CircleRegion region, Material material)
        {
            region.CircleScale = region.Radius * region.Radius;
            region.CirclePosition = new Vector2(region.Offset.x, region.Offset.y);
            material.SetFloat(FillPropertyId, region._fillProgress);
        }

        // Offset の読み出しが1回 → 拒否。
        internal void SingleOffsetRead(CircleRegion region, Material material)
        {
            region.CircleScale = region.Radius;
            region.CirclePosition = region.Offset;
            material.SetFloat(FillPropertyId, region._fillProgress);
        }

        // マテリアル float の書き込みがない（SetFloat 0回）→ 拒否。
        internal void WithoutMaterialFloat(CircleRegion region)
        {
            region.CircleScale = region.Radius;
            region.CirclePosition = new Vector2(region.Offset.x, region.Offset.y);
            region.CircleScale += region._fillProgress;
        }

        // _fillProgress を読まない（ldfld 0回）→ 拒否。
        internal void WithoutFillField(CircleRegion region, Material material)
        {
            region.CircleScale = region.Radius;
            region.CirclePosition = new Vector2(region.Offset.x, region.Offset.y);
            material.SetFloat(FillPropertyId, 1f);
        }
    }
}
