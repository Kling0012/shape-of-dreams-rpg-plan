using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    // （#770）Bounds の境界。center と extents（サイズの半分）をそのまま持ち、
    // Encapsulate は min/max の和集合として計算する（本体の Bounds と同じ意味）。
    internal struct Bounds : IEquatable<Bounds>
    {
        public Vector3 center;
        public Vector3 extents;
        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            extents = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
        }
        public void Encapsulate(Bounds bounds)
        {
            Vector3 aMin = new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z);
            Vector3 aMax = new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z);
            Vector3 bMin = new Vector3(bounds.center.x - bounds.extents.x, bounds.center.y - bounds.extents.y, bounds.center.z - bounds.extents.z);
            Vector3 bMax = new Vector3(bounds.center.x + bounds.extents.x, bounds.center.y + bounds.extents.y, bounds.center.z + bounds.extents.z);
            Vector3 min = new Vector3(Math.Min(aMin.x, bMin.x), Math.Min(aMin.y, bMin.y), Math.Min(aMin.z, bMin.z));
            Vector3 max = new Vector3(Math.Max(aMax.x, bMax.x), Math.Max(aMax.y, bMax.y), Math.Max(aMax.z, bMax.z));
            center = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f);
            extents = new Vector3((max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f, (max.z - min.z) * 0.5f);
        }
        public bool Equals(Bounds other) => center == other.center && extents == other.extents;
        public override bool Equals(object obj) => obj is Bounds other && Equals(other);
        public override int GetHashCode() => (center.x.GetHashCode() * 397) ^ (center.y.GetHashCode() * 31) ^ center.z.GetHashCode();
        public static bool operator ==(Bounds a, Bounds b) => a.Equals(b);
        public static bool operator !=(Bounds a, Bounds b) => !a.Equals(b);
    }

    // transform.position の境界。移動・回転の意味はこの検証では不要なため値をそのまま持つ。
    // （#866）NativeDashSweepStepPerf が get_position / set_position をプロパティとして解決する
    // ため、フィールドではなくプロパティにする（読み書きの意味は変わらない）。set は書き込みを
    // 遅延記録するリストにも足す（ダッシュ掃引の等価性検証がパッチ前後の書き込み列を見る）。
    internal partial class Transform
    {
        private Vector3 _position;
        internal List<Vector3> positionWriteLog;

        // throwOnRead / throwOnWrite（FxAttachIdlePerfBoundary.cs の部分宣言の欄）は
        // 破棄済み Transform へのアクセスを模すための検証用の鉤。null なら何もしない。
        public Vector3 position
        {
            get { if (throwOnRead != null) throw throwOnRead; return _position; }
            set
            {
                if (throwOnWrite != null) throw throwOnWrite;
                _position = value;
                if (positionWriteLog == null) positionWriteLog = new List<Vector3>();
                positionWriteLog.Add(value);
            }
        }
    }

    // 本体のレンダラーの境界。Renderer.bounds の読み出しだけが必要。
    internal partial class Renderer
    {
        public Bounds bounds { get; set; }
    }
}

// 本体 Dew.Core のロジック更新の基底。CharacterModelDisplay はこれを継承する。
internal class LogicBehaviour
{
    public virtual void LogicUpdate(float dt) { }
}

// （#770）本体 Dew.Core CharacterModelDisplay.cs（逆コンパイル、2026-10-18 確認）と同じ形の境界。
// 本体では _currentRenderers の書き手は Setup の埋め直しだけ、読み手は LogicUpdate の bounds
// 走査だけ（FrameUpdate の不透明度は _currentEffectRenderers、フォーカス通知は _focusHandlers
// を使う。逆コンパイル全体で _currentRenderers を読む他の箇所はない）。ここに置くのはその
// 2つの経路だけ: LogicUpdate の走査と2つのゲッターは本体の逆コンパイルと同じ式で書き、
// Setup は現在フィールドが指すリストの埋め直し（Clear + AddRange）だけを模す
// （モデルの生成・破棄・アニメはこの検証の対象外）。
internal class CharacterModelDisplay : LogicBehaviour
{
    internal readonly Transform transform = new Transform();
    private List<Renderer> _currentRenderers = new List<Renderer>();
    private Bounds _bounds;
    // 実物（本体 Dew.Core CharacterModelDisplay.cs）は public フィールド。PlayLobby_Character の
    // 境界（NativeLobbyAccessoryIdleBoundary.cs）が書き、NativeLobbyAccessoryIdle（リンク）が読む。
    // 読み出し時の例外を注入できるよう仮想プロパティにする（DewNetworkManager.isEndingSession と同じ扱い）。
    public virtual string skinType { get; set; }
    public List<string> accessories = new List<string>();
    private string _currentSkinName;
    internal List<Renderer> NextSetupRenderers;
    // 本体 UpdateAccessories は資源の生成・破棄を行う。この境界では呼ばれた回数だけを数える
    // （#833: 省略したTickで呼ばれないことの観察用）。
    internal int UpdateAccessoriesCount;

    public void UpdateAccessories() => UpdateAccessoriesCount++;

    public override void LogicUpdate(float dt)
    {
        base.LogicUpdate(dt);
        if (skinType != _currentSkinName)
        {
            Setup(skinType);
        }
        if (_currentRenderers != null && _currentRenderers.Count != 0)
        {
            _bounds = _currentRenderers[0].bounds;
            for (int i = 1; i < _currentRenderers.Count; i++)
            {
                _bounds.Encapsulate(_currentRenderers[i].bounds);
            }
        }
    }

    internal void Setup(string skinName)
    {
        skinType = skinName;
        _currentSkinName = skinName;
        _currentRenderers.Clear();
        if (NextSetupRenderers != null)
        {
            _currentRenderers.AddRange(NextSetupRenderers);
        }
    }

    public Vector3 GetCenterPosition()
    {
        _ = transform.position;
        return _bounds.center;
    }

    public Vector3 GetAbovePosition()
    {
        Vector3 position = transform.position;
        Vector3 result = _bounds.center + Vector3.up * _bounds.extents.y;
        result.x = position.x;
        result.z = position.z;
        return result;
    }
}
