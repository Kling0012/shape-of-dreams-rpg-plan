using DTT.AreaOfEffectRegions;
using UnityEngine;

// （#766）本体 NativePerfPatches.cs の NativeCastIndicatorCirclePerf のうち、抽出部分
// （ValidateBody・ShouldSkip・Prefix・Postfix）から参照される範囲の境界。
// NativePerfOptions・NativePerfPatch・UnityEngine.Vector2・Vector3 は SleepPositionPerfBoundary.cs・
// EnemyOwnershipBoundary.cs の既存の境界をそのまま使うため、ここには重複して置かない。
namespace UnityEngine
{
    // 本体の ValidateBody・Prefix が Material.SetFloat(int, float)・Renderer.sharedMaterial を
    // 解決するのに必要なシグネチャだけの境界。呼び出しの有無は ValidateBody が IL 命令で数えるため、
    // 中身は空でよい。ShouldSkip は実体の同一性だけを見るので状態は要らない。
    internal sealed class Material
    {
        internal void SetFloat(int propertyId, float value) { }
    }

    // 本体の CircleRegion が sharedMaterial を読むのは MeshRenderer の継承元 UnityEngine.Renderer。
    // 実 DLL の IL も callvirt Renderer::get_sharedMaterial なので、この継承関係が必要。
    internal partial class Renderer
    {
        internal Material sharedMaterial { get; set; }
    }

    internal sealed class MeshRenderer : Renderer
    {
    }

    // 本体 UpdateProperties が書き換える子トランスフォームの境界。
    internal sealed partial class Transform
    {
        internal Vector3 localScale { get; set; }
        internal Vector3 localPosition { get; set; }
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
        // 実 DLL の FillProgress は _fillProgress の純粋なパススルーだが、Prefix はこのプロパティを
        // 経由せずフィールドを直接読む。それを確かめるため、境界では意図的に常に違う値を返す。
        internal float FillProgress => -1f;
        internal UnityEngine.Transform _circle;
        internal UnityEngine.MeshRenderer _circleRenderer;
        internal static readonly int ProgressShaderID = 7;
        // 追跡していない入力の見本（拒否の見本 UntrackedExtraInput が読む）。
        internal float _opacity;
    }
}

namespace Issue73.Native.Tests
{
    // 本体 DTT.AreaOfEffectRegions.CircleRegion.UpdateProperties と同じ読み出し・書き込み一式の見本。
    // ValidateBody は追跡する4種（get_Radius 1回・get_Offset 2回・Material.SetFloat(int,float) 1回・
    // _fillProgress 読み出し 1回）の回数に加え、本体が参照するメンバー一式（ExpectedMembers）が
    // 過不足なく一致することを要求するため、拒否の見本は追跡外の読み出しを1つ混べた形も含む。
    internal sealed class CastIndicatorIlShapes
    {
        // 本体と同じ読み出し・書き込み一式（許可した参照先だけを同じ回数だけ使う）→ 受理。
        internal void SameShape(CircleRegion region)
        {
            region._circle.localScale = Vector3.one * 2f * region.Radius;
            region._circle.localPosition = new Vector3(region.Offset.x, region._circle.localPosition.y, region.Offset.y);
            region._circleRenderer.sharedMaterial.SetFloat(CircleRegion.ProgressShaderID, region._fillProgress);
        }

        // Radius の読み出しが2回 → 拒否。
        internal void SecondRadiusRead(CircleRegion region)
        {
            region._circle.localScale = Vector3.one * 2f * region.Radius * region.Radius;
            region._circle.localPosition = new Vector3(region.Offset.x, region._circle.localPosition.y, region.Offset.y);
            region._circleRenderer.sharedMaterial.SetFloat(CircleRegion.ProgressShaderID, region._fillProgress);
        }

        // Offset の読み出しが1回 → 拒否。
        internal void SingleOffsetRead(CircleRegion region)
        {
            region._circle.localScale = Vector3.one * 2f * region.Radius;
            region._circle.localPosition = new Vector3(region.Offset.x, region._circle.localPosition.y, region.Offset.x);
            region._circleRenderer.sharedMaterial.SetFloat(CircleRegion.ProgressShaderID, region._fillProgress);
        }

        // マテリアル float の書き込みがない（SetFloat 0回）→ 拒否。
        internal void WithoutMaterialFloat(CircleRegion region)
        {
            region._circle.localScale = Vector3.one * 2f * region.Radius;
            region._circle.localPosition = new Vector3(region.Offset.x, region._circle.localPosition.y, region.Offset.y);
        }

        // _fillProgress を読まない（ldfld 0回）→ 拒否。
        internal void WithoutFillField(CircleRegion region)
        {
            region._circle.localScale = Vector3.one * 2f * region.Radius;
            region._circle.localPosition = new Vector3(region.Offset.x, region._circle.localPosition.y, region.Offset.y);
            region._circleRenderer.sharedMaterial.SetFloat(CircleRegion.ProgressShaderID, 1f);
        }

        // 追跡する4種の回数はそのまま、追跡していない入力（_opacity）を1つ読み足す → 拒否。
        internal void UntrackedExtraInput(CircleRegion region)
        {
            region._circle.localScale = Vector3.one * 2f * region.Radius;
            region._circle.localPosition = new Vector3(region.Offset.x, region._circle.localPosition.y, region.Offset.y);
            region._circleRenderer.sharedMaterial.SetFloat(CircleRegion.ProgressShaderID, region._fillProgress * region._opacity);
        }
    }
}
