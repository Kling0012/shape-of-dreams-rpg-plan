using System;
using System.Collections.Generic;

// 旅人連携の宣言キャッシュ（PR #1231 レビュー F2）のための境界。この検証プロジェクトは
// DreamforgeMod.HeroConnector.cs（実物）をコンパイルし、宣言の再走査で解析と警告が
// 繰り返されないことを確かめる（HeroConnectorCacheTests）。この境界はその実ソースが触る
// 本体 API の形だけを用意する（NyxMarkBoundary.cs と同じ扱い）。NetworkedManagerBase と
// GameManager は NativeBoundary.cs、HostAuthority.HeroConnectorEnabled と ModVersion は
// 実物（HostAuthority.Hello.cs）が既にあり、Log は NativeBoundary.cs の記録付きの模倣を使う。
namespace SodRpg.Mod
{
    // 本体の MOD 読み込み口（DewMod）。走査は loadedInstances の各インスタンスの mod
    // フォルダを辿り、onModLoadEnded で再走査を受け取る。検証では loadedInstances に
    // 一時フォルダの MOD を1つだけ置き、onModLoadEnded は発火させない。
    internal sealed class DewMod
    {
        public static List<DewMod> loadedInstances;
        public static Action onModLoadEnded;
        public ModRecord mod;
        public sealed class ModRecord { public string path; public ModMetadata metadata; }
        public sealed class ModMetadata { public string id; }
    }

    public partial class DreamforgeMod
    {
        // 宣言部（partial void の定義）は本体の DreamforgeMod.cs にある。このプロジェクトは
        // 実装（DreamforgeMod.HeroConnector.cs）だけをコンパイルするため、同じ形の宣言を置く。
        partial void StartHeroConnectorScan();
        partial void StopHeroConnectorScan();
        partial void TickHeroConnectorPending();
    }
}
namespace UnityEngine
{
    // 報告ファイルの置き場（実物は Unity の保存先）。検証では StartHeroConnectorScan を
    // 呼ばないため、パスは null のままで報告ファイルは書かない。
    internal static class Application
    {
        public static string persistentDataPath;
    }
}
