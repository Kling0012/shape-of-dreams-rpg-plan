"""Compile the unchanged cooldown-idle patch against the existing native API doubles."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
source = (root / "src/SodRpg.Mod/NativePerfPatches.cs").read_text(encoding="utf-8")


def declaration(name):
    match = re.search(r"internal static class " + name + r"\b", source)
    if match is None:
        raise ValueError(f"Missing native declaration: {name}")
    opening = source.index("{", match.start())
    # Count braces outside C# comments and literals, as in extract_esscap_native.py.
    tokens = re.compile(r'//[^\n]*|/\*.*?\*/|\$?"(?:\\.|[^"\\])*"|\x27(?:\\.|[^\x27\\])*\x27|[{}]', re.S)
    depth = 0
    for token in tokens.finditer(source, opening):
        if token.group() == "{":
            depth += 1
        elif token.group() == "}":
            depth -= 1
            if depth == 0:
                start = match.start()
                if name == "NativeCooldownIdlePerf":
                    attribute = source.rfind("[HarmonyPatch]", 0, start)
                    if attribute < 0 or source[attribute + len("[HarmonyPatch]"):start].strip():
                        raise ValueError("Missing cooldown-idle HarmonyPatch attribute")
                    start = attribute
                return source[start:token.end()]
    raise ValueError(f"Unclosed native declaration: {name}")


text = ("using System; using System.Collections.Generic; using System.Reflection;\n"
        "using System.Reflection.Emit; using HarmonyLib;\n"
        "namespace SodRpg.Mod {\n"
        + "\n".join(declaration(name) for name in
                    ("NativePerfOptions", "NativePerfPatch", "NativeCooldownIdlePerf"))
        + "\n}\n")
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
if not output.exists() or output.read_text(encoding="utf-8") != text:
    output.write_text(text, encoding="utf-8")
