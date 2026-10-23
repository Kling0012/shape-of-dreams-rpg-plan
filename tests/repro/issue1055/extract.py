"""Extract unchanged production methods; native sources stay outside the repository."""
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
TOKENS = re.compile(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"|\x27(?:\\.|[^\x27\\])*\x27|[{}]', re.S)
NATIVE = Path(os.environ.get("SOD_DECOMPILED", "/home/wang/dev/sod-gamedata/decompiled/sod-decomp"))
GAME = Path(os.environ.get("SOD_MANAGED", "/home/wang/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed"))


def block(source, declaration):
    match = re.search(declaration, source)
    if match is None:
        raise ValueError("Missing production declaration: " + declaration)
    opening = source.index("{", match.start())
    depth = 0
    for token in TOKENS.finditer(source, opening):
        if token.group() == "{":
            depth += 1
        elif token.group() == "}":
            depth -= 1
            if depth == 0:
                return source[match.start():token.end()]
    raise ValueError("Unclosed declaration: " + declaration)


host = (ROOT / "src/SodRpg.Mod/HostAuthority.cs").read_text()
native_damage = (NATIVE / "Dew.Core/DamageData.cs").read_text()
ilspy = os.environ.get("ILSPYCMD", str(Path.home() / ".dotnet/tools/ilspycmd"))
completed = subprocess.run(
    [ilspy, "-t", "Ai_QR_Innocence_Spawner", str(GAME / "Dew.Contents.dll")],
    capture_output=True, text=True, check=True, timeout=120,
)
spawner = completed.stdout
clamped = re.search(r"public int clampedCount =>[^;]+;", spawner)
if clamped is None:
    raise ValueError("Missing native clampedCount")
text = (
    "using System; using System.Collections; using System.Collections.Generic; "
    "using System.Runtime.CompilerServices; using SodRpg.Core.Game; using UnityEngine;\n"
    "// Extracted production methods; surrounding native services are boundary doubles.\n"
    "namespace SodRpg.Mod { internal partial class HostAuthority {\n"
    + block(host, r"private void ApplyPressure\(") + "\n"
    + block(host, r"private sealed class ThornsKeyComparer\b") + "\n"
    + block(host, r"private void PruneThornsTimers\(") + "\n"
    + (block(host, r"private sealed class ThornsWindow\b")
       + "\nprivate readonly ConditionalWeakTable<Hero, ThornsWindow> _thornsWindows = new ConditionalWeakTable<Hero, ThornsWindow>();\n"
       if "private sealed class ThornsWindow" in host else "")
    + block(host, r"private void OnMonsterDamageTaken\(") + "\n} }\n"
    + "public partial struct DamageData {\n"
    + block(native_damage, r"public DamageData ApplyAmplification\(") + "\n}\n"
    + "public partial class Ai_QR_Innocence_Spawner : AbilityInstance {\n"
    + clamped.group() + "\n"
    + block(spawner, r"protected override IEnumerator OnCreateSequenced\(") + "\n}\n"
)
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
if not output.exists() or output.read_text() != text:
    output.write_text(text)
