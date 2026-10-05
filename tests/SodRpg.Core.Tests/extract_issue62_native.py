"""Compile the real death/removal and kill adapter bodies against native API doubles."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]

def block(path, declaration):
    source = (root / path).read_text()
    match = re.search(declaration, source)
    if match is None:
        raise ValueError(f"Missing native declaration: {declaration}")
    opening = source.index("{", match.start())
    # Ignore comments and literals when balancing C# braces (including interpolated strings).
    tokens = re.compile(r'//[^\n]*|/\*.*?\*/|\$?"(?:\\.|[^"\\])*"|\x27(?:\\.|[^\x27\\])*\x27|[{}]', re.S)
    depth = 0
    for token in tokens.finditer(source, opening):
        if token.group() == "{":
            depth += 1
        elif token.group() == "}":
            depth -= 1
            if depth == 0:
                return source[match.start():token.end()]
    raise ValueError(f"Unclosed native declaration: {declaration}")

host = "src/SodRpg.Mod/HostAuthority.cs"
attribution = "src/SodRpg.Mod/HostAuthority.MemoryActivationAttribution.cs"
methods = [block(host, rf"private void {name}\(") for name in
           ("OnEntityAdd", "OnEntityRemove", "OnMonsterDeath", "RemoveMonster")]
methods += [block(attribution, r"internal void PublishAttributedKill\(")]
methods += [block("src/SodRpg.Mod/HostAuthority.KillSync.cs", r"private void CaptureAuthoritativeRunKill\(")]
patch = block(attribution, r"internal static class NativeAttributedKill\b")
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
text = ("using System; using System.Collections.Generic; using SodRpg.Core.Game; using UnityEngine; using Mirror;\n"
        "namespace SodRpg.Mod {\n" + patch + "\ninternal sealed partial class HostAuthority {\n"
        + "\n".join(methods) + "\n}}\n")
# Only rewrite when the content differs: an unconditional write changes the timestamp and
# recompiles the whole test project on every build (issue #151).
if not output.exists() or output.read_text(encoding="utf-8") != text:
    output.write_text(text, encoding="utf-8")
