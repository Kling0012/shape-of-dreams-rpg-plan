"""Compile unchanged issue #573 production trade-tab drawing against GUILayout doubles."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]
TOKENS = re.compile(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"|\x27(?:\\.|[^\x27\\])*\x27|[{}]', re.S)


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


trade = "src/SodRpg.Mod/DreamforgeUi.CoopTrade.cs"
own = block(trade, r"private void DrawCoopTradeOwnRelics\(")
choices = block(trade, r"private void DrawCoopTradeRelicChoices\(")
# 抽出対象が描画ループそのものであることを保証する。修正で本体が動いたらここでビルドを失敗させる。
if "IsEquippedAnywhere" not in choices or "MatchesCoopTradeFilters" not in choices:
    raise ValueError("DrawCoopTradeRelicChoices moved off the unequipped+filtered loop; update the extractor")
if "p.Stash" not in own or "p.Run.Satchel" not in own:
    raise ValueError("DrawCoopTradeOwnRelics no longer draws stash and satchel; update the extractor")
text = (
    "using System; using System.Collections.Generic; using System.Globalization; using SodRpg.Core.Game; using UnityEngine;\n"
    "// 自動生成: tests/repro/issue573/extract.py が src/SodRpg.Mod/DreamforgeUi.CoopTrade.cs から取り出した製品コード（改変しない）。\n"
    "namespace SodRpg.Mod {\n"
    "internal sealed partial class DreamforgeUi {\n"
    + own + "\n\n"
    + choices + "\n\n"
    + (block(trade, r"private void EnsureCoopTradeRelicStyles\(") + "\n\n" if "EnsureCoopTradeRelicStyles" in choices else "")
    + block(trade, r"private bool MatchesCoopTradeFilters\(") + "\n\n"
    + block(trade, r"private bool TryCoopTradeAmounts\(") + "\n\n"
    + block(trade, r"private CoopTradeOffer CopyCoopTradeOffer\(") + "\n\n"
    + block(trade, r"private static bool CoopTradeRelicOfferable\(") + "\n\n"
    + block(trade, r"private static Relic FindCoopTradeRelic\(") + "\n"
    + "}\n}\n"
)
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
if not output.exists() or output.read_text(encoding="utf-8") != text:
    output.write_text(text, encoding="utf-8")
