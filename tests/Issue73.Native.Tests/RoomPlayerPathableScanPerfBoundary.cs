// NativeRoomPlayerPathableScanPerf（NativePerfPatches.cs）を Issue73.Native.Tests で検証する
// ための本体 API 境界。実機の Dew・Room・IPlayerPathablePoint はグローバル名前空間の公開型
// だが、TryPrepare は型名を引数で受けるため、ここでは一意な名前の模倣型を渡す
// （BossSoulShrineScanPerfBoundary.cs と同じ扱い。NativePerfPatches.cs は MOD 側からこれらの
// 型を参照しない）。模倣の Dew は本体と同じ宣言（static・ジェネリック定義 1 つ・bool 1 引数・
// T[] 返し）の FindInterfacesOfType を持ち、シーン走査の代わりにテストが固定的に並べた点を
// 返し、呼び出し回数を数える。Room の OnStartClient は本体の呼び出し箇所と同じ形（閉じた
// ジェネリックの FindInterfacesOfType<IPlayerPathablePoint> を bool 1 つで呼び、結果を回す）の
// 転写で、Transpiler の差し替え対象になる。NoCall・TwoCalls は呼び出しが 0 か所・2 か所に
// 変わった本体の再現で、差し替え見送りの確認に使う。
using System;
using System.Collections.Generic;
using System.Linq;

internal static class PathableScanDew
{
    // 本体の走査が見つける点の代わり。テストが毎回作り直す。
    internal static readonly List<IPathableScanPoint> Points = new List<IPathableScanPoint>();
    internal static int Calls;
    public static T[] FindInterfacesOfType<T>(bool includeInactive) where T : class
    {
        Calls++;
        return Points.OfType<T>().ToArray();
    }
}

internal interface IPathableScanPoint { }

internal sealed class PathableScanPointA : IPathableScanPoint { }

internal sealed class PathableScanPointB : IPathableScanPoint { }

// 実機 Room.OnStartClient のうちパッチが触る範囲の転写。歩行可能地点の走査 1 回と、その結果を
// 1 つずつ処理するループだけを置く（本体の GetComponentInParent や NavMesh の標本化は差し替え
// 対象の外なので、ここでは収集だけに簡約する）。
internal class PathableScanRoom
{
    public readonly List<IPathableScanPoint> scanned = new List<IPathableScanPoint>();

    public void OnStartClient()
    {
        IPathableScanPoint[] array = PathableScanDew.FindInterfacesOfType<IPathableScanPoint>(includeInactive: true);
        foreach (IPathableScanPoint item in array)
        {
            scanned.Add(item);
        }
    }
}

// （拒否の見本1）走査の呼び出しが 0 か所の本体。将来の更新で OnStartClient からこの走査が
// 消えた場合の再現。
internal class PathableScanRoomNoCall
{
    public void OnStartClient() { }
}

// （拒否の見本2）走査の呼び出しが 2 か所並んだ本体。本体の IL が予想と変わった場合の再現。
internal class PathableScanRoomTwoCalls
{
    public readonly List<IPathableScanPoint> scanned = new List<IPathableScanPoint>();

    public void OnStartClient()
    {
        scanned.AddRange(PathableScanDew.FindInterfacesOfType<IPathableScanPoint>(includeInactive: true));
        scanned.AddRange(PathableScanDew.FindInterfacesOfType<IPathableScanPoint>(includeInactive: true));
    }
}
