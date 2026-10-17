"""Run issue #572 production UI text and host roll methods without Unity rendering."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]
TOKENS = re.compile(r'//[^\n]*|/\*.*?\*/|\$?"(?:\\.|[^"\\])*"|\x27(?:\\.|[^\x27\\])*\x27|[{}]', re.S)


def block(path, declaration):
    source = (ROOT / path).read_text(encoding="utf-8")
    match = re.search(declaration, source)
    if match is None:
        raise ValueError(f"Missing production declaration: {path}: {declaration}")
    opening = source.index("{", match.start())
    depth = 0
    for token in TOKENS.finditer(source, opening):
        if token.group() == "{":
            depth += 1
        elif token.group() == "}":
            depth -= 1
            if depth == 0:
                return source[match.start():token.end()]
    raise ValueError(f"Unclosed declaration: {declaration}")


ui = "src/SodRpg.Mod/DreamforgeUi.cs"
source = (ROOT / ui).read_text(encoding="utf-8")
fields = source[source.index("        private (int Count, int Shards"):source.index("        private void CacheSecureText(")]
draw = block(ui, r"private void DrawSecurePrompt\(")
call = re.search(r"\bCacheSecureText\(run,.*?;", draw, re.S)
if call is None:
    raise ValueError("Secure panel cache call moved; update the reproduction extractor")
spawn = block("src/SodRpg.Mod/HostAuthority.cs", r"private void ProcessSpawns\(")
mult = re.search(r"double pressureMult = .*?;\s*double mult = .*?;", spawn, re.S)
if mult is None:
    raise ValueError("Host multiplier statements moved; update the reproduction extractor")
text = "using System; using SodRpg.Core.Game;\nnamespace SodRpg.Mod {\n"
text += "internal sealed partial class DreamforgeUi {\n" + fields
text += block(ui, r"private void CacheSecureText\(") + "\n"
text += "internal string PanelText() { var run = _s.Profile.Run; int bonus = 0, free = 100; " + call.group() + " return _secureNightmareText; }\n}\n"
text += "internal sealed partial class HostAuthority {\n"
text += "internal double SpawnMultiplier(int dailyId) { " + mult.group() + " return mult; }\n"
text += block("src/SodRpg.Mod/HostAuthority.Waypoints.cs", r"private NightmareAffix RollWaypointNightmare\(")
text += "\n}\n}\n"
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
if not output.exists() or output.read_text(encoding="utf-8") != text:
    output.write_text(text, encoding="utf-8")
