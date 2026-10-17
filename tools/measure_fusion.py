#!/usr/bin/env python3
"""Measure production SyncFusion on this host without starting Unity.

Run: python tools/measure_fusion.py [--revision <git ref>]
Only the UI/session/clock boundary is substituted. Core rules and the SyncFusion
source are compiled as-is. Build artifacts stay under tools/obj/fusion-perf/.
This measures CPU/allocation cost, not in-game frame time or rendering.
"""
import argparse
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", help="read SyncFusion from this git revision")
    args = parser.parse_args()
    source_path = "src/SodRpg.Mod/DreamforgeUi.Fusion.cs"
    source = (subprocess.check_output(["git", "show", f"{args.revision}:{source_path}"], cwd=ROOT, text=True, encoding="utf-8")
              if args.revision else (ROOT / source_path).read_text(encoding="utf-8"))
    # Keep production fields, FusionRelicState, FusionState and SyncFusion verbatim.
    source = source.split("        private void DrawFusion(Relic target)")[0]
    source += """
        internal void MeasureSync(Relic target) => SyncFusion(target);
        internal DreamforgeUi(ClientSession session) { _s = session; }
        private readonly ClientSession _s;
    }
}
"""
    build = ROOT / "tools/obj/fusion-perf"
    build.mkdir(parents=True, exist_ok=True)
    (build / "FusionSource.cs").write_text(source, encoding="utf-8")
    # Use the production title formatting as well (all static helpers at the end).
    styles = (ROOT / "src/SodRpg.Mod/UiStyles.cs").read_text(encoding="utf-8")
    styles = styles[styles.index("        public static string RarityHex"):]
    (build / "UiStyles.cs").write_text("using SodRpg.Core.Game;\nnamespace SodRpg.Mod { internal partial class UiStyles {\n" + styles, encoding="utf-8")
    (build / "FusionPerf.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><NoWarn>CS0169;CS0649;CS0414</NoWarn><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="{ROOT / 'src/SodRpg.Core/SodRpg.Core.csproj'}" /></ItemGroup>
</Project>
""", encoding="utf-8")
    (build / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    subprocess.run(["dotnet", "run", "--project", str(build / "FusionPerf.csproj"), "-c", "Release"],
                   cwd=ROOT, env={**os.environ, "DOTNET_TieredCompilation": "0"}, check=True)


PROGRAM = r'''
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SodRpg.Core.Game;
using SodRpg.Mod;

// No game, drawing, persistence or network calls: only the inputs SyncFusion reads.
namespace UnityEngine
{
    public struct Vector2 { public static Vector2 zero => default; }
    public class GUILayoutOption { }
    public static class GUILayout
    {
        public static GUILayoutOption Height(int n) => new GUILayoutOption();
        public static GUILayoutOption MinHeight(int n) => new GUILayoutOption();
        public static GUILayoutOption MinWidth(int n) => new GUILayoutOption();
        public static GUILayoutOption ExpandWidth(bool b) => new GUILayoutOption();
    }
    public static class Time { public static float unscaledTime; }
}
namespace SodRpg.Mod
{
    internal sealed class ClientSession
    {
        public Profile Profile;
        public TradeLedger Trades = new TradeLedger();
        public bool CanEditLoadout => true;
    }
}
internal static class Program
{
    private const int Frames = 600;
    private const int Rounds = 7;
    private static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.WriteLine("CPU/allocation only; Release/.NET " + Environment.Version + "; 9 heroes; 4 reservations; no eligible materials");
        Console.WriteLine("count,mode,median_us_per_call,max_round_us_per_call,bytes_per_call");
        foreach (int count in new[] { 100, 1000, 10000 })
        {
            var session = new ClientSession { Profile = Profile.CreateNew(7) };
            var p = session.Profile;
            foreach (string hero in new[] { "Hero_Lacerta", "Hero_Aurena", "Hero_Nachia", "Hero_Mist", "Hero_Cetus", "Hero_Bismuth", "Hero_Husk", "Hero_Vesper", "Hero_Yubar" }) p.Hero(hero);
            for (int i = 0; i < count; i++)
                p.Stash.Add(new Relic { Uid = "relic-" + i, BaseId = i == 0 ? "weapon.chain_sword" : "weapon.bone_cleaver", Rarity = Rarity.Rare, ItemLevel = 5 });
            for (int i = 1; i <= 4; i++)
                session.Trades.Begin(TradeKind.SalvageForDust, 0, 0, 0, "relic-" + (count - i), now: 1);
            foreach (bool advanceClock in new[] { false, true })
            {
                var ui = new DreamforgeUi(session);
                var target = p.Stash[0];
                UnityEngine.Time.unscaledTime = 0;
                for (int i = 0; i < 1200; i++) ui.MeasureSync(target);
                var times = new List<double>(Rounds);
                long bytes = 0;
                for (int round = 0; round < Rounds; round++)
                {
                    long startBytes = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int frame = 0; frame < Frames; frame++)
                    {
                        if (advanceClock) UnityEngine.Time.unscaledTime = (round * Frames + frame + 1) / 60f;
                        ui.MeasureSync(target); // Layout
                        ui.MeasureSync(target); // Repaint
                    }
                    times.Add((Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency / (Frames * 2));
                    bytes += GC.GetAllocatedBytesForCurrentThread() - startBytes;
                }
                times.Sort();
                Console.WriteLine($"{count},{(advanceClock ? "60fps-layout+repaint" : "cache-hit")},{times[Rounds / 2]:F3},{times[Rounds - 1]:F3},{bytes / (double)(Rounds * Frames * 2):F3}");
            }
        }
    }
}
'''

if __name__ == "__main__":
    main()
