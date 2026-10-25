// NativeRoomPrewarmResolveOncePerf（NativePerfPatches.cs）を Issue73.Native.Tests で検証するための
// 本体 API 境界。RoomMonsters・SpawnManager・DewEffect をグローバル名前空間に置くのは、実機の
// Dew.Core がこの名前空間にこれらを持つからであり、Prepare の AccessTools.TypeByName が
// Assembly.GetType("RoomMonsters") の全名一致で解決するのと同じ経路をテストでも通すためである
// （BossSoulShrineScanPerfBoundary.cs と同じ扱い）。
// 注意: NativeBoundary.cs に SodRpg.Mod.RoomMonsters（部屋の兆しの資格判定用の別の境界）が
// 既にある。グローバル名前空間にこの RoomMonsters を置くと、using SodRpg.Mod だけを書いた
// 箇所の修飾なし RoomMonsters はこちらへ束縛し直されるため、該当箇所
// （CoopOmensWardTests・RoomOmensHostTests）は SodRpg.Mod.RoomMonsters に修飾してある。
// テスト用アセンブリにはこれ以上 RoomMonsters・SpawnManager・DewEffect という名前の型を
// 足さないこと（TypeByName の名前での拾いの先行が保証できなくなるため）。
using System.Collections.Generic;
using UnityEngine;

// 本体 Dew.Core r1.4.1.2 RoomMonsters のうち ApplyPrewarm が触る範囲の境界。
internal class RoomMonsters
{
    // 本体と同じ公開字段を持つネストした struct（Prepare は GetNestedType("PrewarmEntry") と
    // 字段の名前・型まで照合する）。
    public struct PrewarmEntry
    {
        public uint assetId;
        public int count;
        public uint ownerNetId;
    }

    // 本体の ResolveClientPrewarmPrefab は引数 (assetId, ownerNetId) ごとに決まる値を返す
    // （実機では DewResources の辞書引きと variant 計算が決定論的）。境界ではテストが
    // ResolveResults に置いた表引きで同じ意味を置く。未登録の組と、登録された値が null の組は
    // どちらも解決失敗（null）として扱う。ResolveCalls は呼び出しごとの (assetId, ownerNetId) の
    // 記録で、呼び出し回数の照合に使う。
    internal static readonly List<(uint assetId, uint ownerNetId)> ResolveCalls = new List<(uint, uint)>();
    internal static readonly Dictionary<(uint assetId, uint ownerNetId), GameObject> ResolveResults
        = new Dictionary<(uint, uint), GameObject>();

    // 本体と同じ private static（Prepare の MethodDelegate は static かどうかを含めて
    // シグネチャに合わせる）。
    private static GameObject ResolveClientPrewarmPrefab(uint assetId, uint ownerNetId)
    {
        ResolveCalls.Add((assetId, ownerNetId));
        return ResolveResults.TryGetValue((assetId, ownerNetId), out GameObject prefab) ? prefab : null;
    }

    // 本体 Dew.Core r1.4.1.2 RoomMonsters.ApplyPrewarm（逆コンパイルの IL と同じ分岐）の転写。
    // 等価性テストの「パッチ無効側」（2ループ版）として動く: 1ループ目で各項目の解決結果を
    // keep set に集め、ReleaseInactivePoolsExcept(keep) → AgeAndDropInactivePools() と進み、
    // 2ループ目で同じ引数の解決をやり直して prefab ごとの個数を数え、辞書の列挙順（挿入順）で
    // Prewarm に渡す。private インスタンスメソッドである点も本体と同じ。
    private void ApplyPrewarm(PrewarmEntry[] entries)
    {
        HashSet<GameObject> hashSet = new HashSet<GameObject>();
        for (int i = 0; i < entries.Length; i++)
        {
            GameObject gameObject = ResolveClientPrewarmPrefab(entries[i].assetId, entries[i].ownerNetId);
            if (gameObject != null)
            {
                hashSet.Add(gameObject);
            }
        }
        SpawnManager.ReleaseInactivePoolsExcept(hashSet);
        DewEffect.AgeAndDropInactivePools();
        Dictionary<GameObject, int> dictionary = new Dictionary<GameObject, int>();
        for (int j = 0; j < entries.Length; j++)
        {
            PrewarmEntry prewarmEntry = entries[j];
            GameObject gameObject2 = ResolveClientPrewarmPrefab(prewarmEntry.assetId, prewarmEntry.ownerNetId);
            if (!(gameObject2 == null))
            {
                dictionary.TryGetValue(gameObject2, out var value);
                dictionary[gameObject2] = value + prewarmEntry.count;
            }
        }
        foreach (KeyValuePair<GameObject, int> item in dictionary)
        {
            SpawnManager.Prewarm(item.Key, item.Value);
        }
    }
}

// 本体 SpawnManager（public class SpawnManager : ManagerBase<SpawnManager>）のうち
// ApplyPrewarm が使う2つの静的 API の境界。実機の ReleaseInactivePoolsExcept は受け取った
// keep set には手を加えない（プールの解放だけをする）ので、境界では中身の写しを記録する。
// Prewarm<T> は実機では where T : UnityEngine.Object の制約を持つが、Prepare は
// IsGenericMethodDefinition と (T prefab, int count) の引数形（MakeGenericMethod(GameObject) と
// MethodDelegate）だけを見るため、境界では制約なしの同じ形で再現する
// （テスト用アセンブリの UnityEngine.Object は静的クラスの模倣で、制約には使えない）。
internal class SpawnManager
{
    internal static readonly List<HashSet<GameObject>> ReleasedKeeps = new List<HashSet<GameObject>>();
    internal static readonly List<(GameObject prefab, int count)> Prewarms = new List<(GameObject, int)>();

    public static void ReleaseInactivePoolsExcept(HashSet<GameObject> keepPrefabs)
    {
        ReleasedKeeps.Add(new HashSet<GameObject>(keepPrefabs));
        RoomPrewarmTrace.Calls.Add("release");
    }

    public static void Prewarm<T>(T prefab, int count)
    {
        Prewarms.Add(((GameObject)(object)prefab, count));
        RoomPrewarmTrace.Calls.Add("prewarm");
    }
}

// 本体 DewEffect（public static class）のうち ApplyPrewarm が呼ぶ AgeAndDropInactivePools の境界。
internal static class DewEffect
{
    internal static int AgeAndDropCalls;

    public static void AgeAndDropInactivePools()
    {
        AgeAndDropCalls++;
        RoomPrewarmTrace.Calls.Add("age");
    }
}

// 呼び出し順の照合（release → age → prewarm…）のための記録欄。本体の意味論には無い検証専用。
internal static class RoomPrewarmTrace
{
    internal static readonly List<string> Calls = new List<string>();
}
