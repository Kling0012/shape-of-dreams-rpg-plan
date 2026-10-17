"""Compile the real crystal-circuit and memory-haste bodies against native API doubles.

Extracts the exact production declarations (OnSkillUse, OnNativeGemUseV129, the
CrystalCircuitCut cut ledger, NormalMemories and ReduceNormalMemory) from the Mod
sources so tests exercise the shipped logic, not a re-implementation. The doubles
for the game types they touch live in EssenceCapsNativeApi.cs.
"""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]

def block(path, declaration):
    # The sources are UTF-8 (Japanese comments); without this the read uses the machine's
    # ANSI code page when PYTHONUTF8 is not set and the extraction crashes.
    source = (root / path).read_text(encoding="utf-8")
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

def lines(path, pattern):
    source = (root / path).read_text(encoding="utf-8")
    found = [line for line in source.splitlines() if re.search(pattern, line)]
    if not found:
        raise ValueError(f"Missing native declaration lines: {pattern}")
    return found

def stmt(path, declaration):
    # Single-statement declarations (expression-bodied methods, multi-line field
    # initializers): everything from the match through the terminating semicolon.
    source = (root / path).read_text(encoding="utf-8")
    match = re.search(declaration, source)
    if match is None:
        raise ValueError(f"Missing native declaration: {declaration}")
    end = source.index(";", match.start())
    return source[match.start():end + 1]

members = []
members.append(block("src/SodRpg.Mod/HostAuthority.cs", r"private void OnSkillUse\("))
members.append(block("src/SodRpg.Mod/HostAuthority.SupportPowersV129.cs", r"internal void OnNativeGemUseV129\("))
members.append(block("src/SodRpg.Mod/HostAuthority.SupportPowersV129.cs", r"private sealed class CrystalCircuitCut\b"))
members.append(stmt("src/SodRpg.Mod/HostAuthority.SupportPowersV129.cs",
                    r"private readonly ConditionalWeakTable<SkillTrigger, CrystalCircuitCut> _crystalCircuitCuts\s*="))
members.append(stmt("src/SodRpg.Mod/HostAuthority.SupportPowersV129.cs",
                    r"private static readonly ConditionalWeakTable<SkillTrigger, CrystalCircuitCut>\.CreateValueCallback CreateCrystalCircuitCut\s*="))
members.append(block("src/SodRpg.Mod/HostAuthority.NewPowers.cs", r"private readonly struct NormalMemoryEnumerable\b"))
members.append(stmt("src/SodRpg.Mod/HostAuthority.NewPowers.cs", r"private static readonly HeroSkillLocation\[\] NormalMemorySlots\s*="))
members.append(stmt("src/SodRpg.Mod/HostAuthority.NewPowers.cs", r"private static NormalMemoryEnumerable NormalMemories\("))
members.append(block("src/SodRpg.Mod/HostAuthority.NewPowers.cs", r"private static void ReduceNormalMemory\("))

output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
text = ("using System; using System.Collections.Generic; using System.Runtime.CompilerServices; using Mirror; using SodRpg.Core.Game; using UnityEngine;\n"
        "using Power = SodRpg.Core.Game.Power;\n"
        "namespace SodRpg.Mod {\n"
        "internal sealed partial class HostAuthority {\n"
        + "\n".join(members) + "\n}}\n")
# Only rewrite when the content differs to preserve incremental builds (issue #151).
if not output.exists() or output.read_text(encoding="utf-8") != text:
    output.write_text(text, encoding="utf-8")
