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
        // 値の意味は本体と同じ。書き込んだ回数も数える（NativeHeroWeaponIdlePerf が
        // 同じ値の書き込みを省くかどうかの検証に使う）。
        private Vector3 _localScale;
        internal int localScaleWrites;
        internal Vector3 localScale
        {
            get => _localScale;
            set { _localScale = value; localScaleWrites++; }
        }
        internal Vector3 localPosition { get; set; }

        // LineRegion の UpdateProperties が body 側のワールド回転を読む分（ワールド位置
        // position は CharacterModelDisplayBoundsBoundary.cs の partial、向き forward は
        // SkollMarkBoundary.cs の partial が持つ）。意味は本体と同じで、状態は保持するだけ。
        internal Vector3 eulerAngles { get; set; }

        // 照準矢印の形状見本（LineSameShape など）が body へのローカル回転の書き込みに使う分。
        // 読み取り側はワールド回転（eulerAngles）だけが本体の入力になる。
        internal Vector3 localEulerAngles { get; set; }
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
        internal void UpdateProperties() { }
    }

    // 照準矢印（LineRegion）の境界。ValidateBody が typeof(LineRegionBase) の4つの getter を
    // 探すため、プロパティはここに置く。フィールドは Prepare が FieldRefAccess で解決する
    // 対象（ハーネスでは呼ばれないが、本体と同じメンバー一式を持つ）。
    internal class LineRegionBase
    {
        internal float _angle;
        internal float _length;
        internal float _width;
        internal float _fillProgress;
        internal float Angle { get; set; }
        internal float Length { get; set; }
        internal float Width { get; set; }
        internal float FillProgress { get; set; }
    }

    internal sealed class LineRegion : LineRegionBase
    {
        internal UnityEngine.Transform _bodyTransform;
        internal UnityEngine.Transform _headTransform;
        internal UnityEngine.MeshRenderer _bodyRenderer;
        internal UnityEngine.MeshRenderer _headRenderer;
        internal static readonly int ProgressShaderID = 8;
        // 追跡していない入力の見本（照準矢印の拒否の見本 LineUntrackedExtraInput が読む）。
        internal float _opacity;
        internal void UpdateProperties() { }
    }
}

namespace Issue73.Native.Tests
{
    // 本体 DTT.AreaOfEffectRegions.CircleRegion / LineRegion の UpdateProperties と同じ読み出し・
    // 書き込み一式の見本。ValidateBody は追跡する4種の回数（照準円: get_Radius 1回・get_Offset 2回・
    // SetFloat 1回・_fillProgress 読み出し 1回。照準矢印: get_Length 3回・get_Width 2回・
    // get_FillProgress 2回・SetFloat 2回）に加え、本体が参照するメンバー一式（ExpectedMembers）が
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

        // ---- 照準矢印（LineRegion）の見本 ----

        // 本体 LineRegion.UpdateProperties と同じ読み出し・書き込み一式（許可した参照先だけを
        // 同じ回数だけ使う）→ 受理。ValidateBody は追跡する4種（get_Length 3回・get_Width 2回・
        // get_FillProgress 2回・SetFloat 2回）の回数に加え、メンバー一式の照合で過不足を見るため、
        // 拒否の見本は回数を1つずらした形と、回数が同じまま追跡していない読み出しを混べた形も含む。
        // 本体の IL は _bodyTransform へのフィールド読み出しが8回・_headTransform が3回に対して
        // transform プロパティの呼び出しが合計14回あるため、見本では3回ぶんの呼び出しをローカル
        // （body/head）経由にして回数を数え合わせる。末尾の使用しないローカル2つは回数合わせ。
        internal void LineSameShape(LineRegion region)
        {
            Transform body = region._bodyTransform;
            Transform head = region._headTransform;

            // 向き: 現在のワールド回転の x/z を残し、y に Angle を入れる。
            Vector3 euler = body.eulerAngles;
            region._bodyTransform.eulerAngles = new Vector3(euler.x, region.Angle, euler.z);

            // body の太さと長さ。高さだけ現在値を残す。
            region._bodyTransform.localScale = new Vector3(region.Width, body.localScale.y, region.Length);

            // head を body の先端（位置 + 向き × 長さ）へ置く。
            region._headTransform.position = region._bodyTransform.position + region._bodyTransform.forward * region.Length;

            // head の大きさは幅に合わせ、高さだけ現在値を残す。
            Vector3 headScale = head.localScale;
            region._headTransform.localScale = new Vector3(region.Width, headScale.y, region.Length);

            // 進み度を body/head のマテリアルへ書く。
            float edge = region._bodyTransform.localScale.z;
            float bodyFill = Mathf.InverseLerp(0f, 1f, region.FillProgress);
            float headFill = Mathf.Max(0f, Mathf.Min(1f, Mathf.InverseLerp(edge, 1f, region.FillProgress)));
            int id = LineRegion.ProgressShaderID;
            region._bodyRenderer.sharedMaterial.SetFloat(id, bodyFill);
            region._headRenderer.sharedMaterial.SetFloat(id, headFill);

            // 回数合わせの読み出し・書き込み（本体が同じメンバーを同じ回数参照する分）。
            region._bodyTransform.localEulerAngles = region._bodyTransform.eulerAngles;
            Vector3 headSize = head.localScale;
            Vector3 aim = body.eulerAngles;
        }

        // Angle の読み出しが2回（追跡する4種の回数はそのまま）→ 拒否。
        internal void LineSecondAngleRead(LineRegion region)
        {
            Transform body = region._bodyTransform;
            Transform head = region._headTransform;
            Vector3 euler = body.eulerAngles;
            region._bodyTransform.eulerAngles = new Vector3(euler.x, region.Angle + region.Angle, euler.z);
            region._bodyTransform.localScale = new Vector3(region.Width, body.localScale.y, region.Length);
            region._headTransform.position = region._bodyTransform.position + region._bodyTransform.forward * region.Length;
            Vector3 headScale = head.localScale;
            region._headTransform.localScale = new Vector3(region.Width, headScale.y, region.Length);
            float edge = region._bodyTransform.localScale.z;
            float bodyFill = Mathf.InverseLerp(0f, 1f, region.FillProgress);
            float headFill = Mathf.Max(0f, Mathf.Min(1f, Mathf.InverseLerp(edge, 1f, region.FillProgress)));
            int id = LineRegion.ProgressShaderID;
            region._bodyRenderer.sharedMaterial.SetFloat(id, bodyFill);
            region._headRenderer.sharedMaterial.SetFloat(id, headFill);
            region._bodyTransform.localEulerAngles = region._bodyTransform.eulerAngles;
            Vector3 headSize = head.localScale;
            Vector3 aim = body.eulerAngles;
        }

        // Length の読み出しが2回（3回のはず）→ 拒否。
        internal void LineSingleLengthRead(LineRegion region)
        {
            Transform body = region._bodyTransform;
            Transform head = region._headTransform;
            Vector3 euler = body.eulerAngles;
            region._bodyTransform.eulerAngles = new Vector3(euler.x, region.Angle, euler.z);
            region._bodyTransform.localScale = new Vector3(region.Width, body.localScale.y, region.Length);
            region._headTransform.position = region._bodyTransform.position + region._bodyTransform.forward * region.Length;
            Vector3 headScale = head.localScale;
            region._headTransform.localScale = new Vector3(region.Width, headScale.y, 1f);
            float edge = region._bodyTransform.localScale.z;
            float bodyFill = Mathf.InverseLerp(0f, 1f, region.FillProgress);
            float headFill = Mathf.Max(0f, Mathf.Min(1f, Mathf.InverseLerp(edge, 1f, region.FillProgress)));
            int id = LineRegion.ProgressShaderID;
            region._bodyRenderer.sharedMaterial.SetFloat(id, bodyFill);
            region._headRenderer.sharedMaterial.SetFloat(id, headFill);
            region._bodyTransform.localEulerAngles = region._bodyTransform.eulerAngles;
            Vector3 headSize = head.localScale;
            Vector3 aim = body.eulerAngles;
        }

        // マテリアル float の書き込みが1回（2回のはず）→ 拒否。
        internal void LineWithoutMaterialFloat(LineRegion region)
        {
            Transform body = region._bodyTransform;
            Transform head = region._headTransform;
            Vector3 euler = body.eulerAngles;
            region._bodyTransform.eulerAngles = new Vector3(euler.x, region.Angle, euler.z);
            region._bodyTransform.localScale = new Vector3(region.Width, body.localScale.y, region.Length);
            region._headTransform.position = region._bodyTransform.position + region._bodyTransform.forward * region.Length;
            Vector3 headScale = head.localScale;
            region._headTransform.localScale = new Vector3(region.Width, headScale.y, region.Length);
            float edge = region._bodyTransform.localScale.z;
            float bodyFill = Mathf.InverseLerp(0f, 1f, region.FillProgress);
            float headFill = Mathf.Max(0f, Mathf.Min(1f, Mathf.InverseLerp(edge, 1f, region.FillProgress)));
            int id = LineRegion.ProgressShaderID;
            region._bodyRenderer.sharedMaterial.SetFloat(id, bodyFill);
            region._bodyTransform.localEulerAngles = region._bodyTransform.eulerAngles;
            Vector3 headSize = head.localScale;
            Vector3 aim = body.eulerAngles;
        }

        // 追跡する4種の回数はそのまま、追跡していない入力（_opacity）を1つ読み足す → 拒否。
        internal void LineUntrackedExtraInput(LineRegion region)
        {
            Transform body = region._bodyTransform;
            Transform head = region._headTransform;
            Vector3 euler = body.eulerAngles;
            region._bodyTransform.eulerAngles = new Vector3(euler.x, region.Angle, euler.z);
            region._bodyTransform.localScale = new Vector3(region.Width, body.localScale.y, region.Length);
            region._headTransform.position = region._bodyTransform.position + region._bodyTransform.forward * region.Length;
            Vector3 headScale = head.localScale;
            region._headTransform.localScale = new Vector3(region.Width, headScale.y, region.Length);
            float edge = region._bodyTransform.localScale.z;
            float bodyFill = Mathf.InverseLerp(0f, 1f, region.FillProgress);
            float headFill = Mathf.Max(0f, Mathf.Min(1f, Mathf.InverseLerp(edge, 1f, region.FillProgress)));
            int id = LineRegion.ProgressShaderID;
            region._bodyRenderer.sharedMaterial.SetFloat(id, bodyFill * region._opacity);
            region._headRenderer.sharedMaterial.SetFloat(id, headFill);
            region._bodyTransform.localEulerAngles = region._bodyTransform.eulerAngles;
            Vector3 headSize = head.localScale;
            Vector3 aim = body.eulerAngles;
        }
    }
}
