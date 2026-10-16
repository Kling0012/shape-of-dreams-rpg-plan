"""Compile unchanged issue #522 production method bodies against narrow API doubles."""
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
host = "src/SodRpg.Mod/HostAuthority.cs"
hello = "src/SodRpg.Mod/HostAuthority.Hello.cs"
# Draw's unchanged opening scopes FormatStat before rendering; no graphics API is emulated.
draw = block(ui, r"public void Draw\(")
if "float scale =" not in draw:
    raise ValueError("Draw's formatting preamble moved; update the reproduction extractor")
preamble = draw[:draw.index("float scale =")] + "}\n"
# Run the actual host reply initializer, rather than inventing a settings packet.
on_hello = block(hello, r"private void OnHello\(")
reply = re.search(r"new DreamforgeHelloMsg\s*\{[^}]*\}", on_hello, re.S)
if reply is None:
    raise ValueError("Host hello initializer moved; update the reproduction extractor")
text = "using System; using System.Collections.Generic; using SodRpg.Core; using SodRpg.Core.Game; using UnityEngine;\nnamespace SodRpg.Mod {\n"
text += block("src/SodRpg.Mod/NetMessages.cs", r"public class DreamforgeHelloMsg\b")
text += block("src/SodRpg.Mod/NetMessages.cs", r"internal static class Protocol\b")
text += "\ninternal sealed partial class HostAuthority {\n"
text += block(host, r"private void OnMemoryDamage\(") + "\n"
text += block(host, r"private string MemorySource\(") + "\n"
text += "internal DreamforgeHelloMsg ReplyForTest() => " + reply.group() + ";\n}\n"
text += "internal sealed partial class DreamforgeUi {\n" + block(ui, r"private Func<string, bool> LinkMarks\(") + "\n" + preamble + "}\n}\n"
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
if not output.exists() or output.read_text(encoding="utf-8") != text:
    output.write_text(text, encoding="utf-8")
