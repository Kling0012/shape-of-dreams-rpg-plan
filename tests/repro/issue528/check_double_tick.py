#!/usr/bin/env python3
"""Issue #528 点3: 個人選択の Tick が1フレームに2回走ることの再現確認。

本物のソースから、ClientSession.TickInfinity が1回呼ばれたときに
InfinityMode.TickPersonalChoices へ至る呼び出し経路が2本あるかを確認する。
両方の経路が存在するあいだ、この確認は失敗（終了コード1）として扱う。
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TICK_INFINITY = Path(ROOT, "src/SodRpg.Mod/ClientSession.Infinity.cs")
TICK_PERSONAL = Path(ROOT, "src/SodRpg.Mod/ClientSession.InfinityPersonalChoices.cs")
MODE_TICK = Path(ROOT, "src/SodRpg.Mod/InfinityMode.cs")


def method_body(path: Path, signature: str) -> str:
    """シグネチャの部分一致でメソッドを探し、波かっこの対応をたどって本体を返す。"""
    text = path.read_text(encoding="utf-8")
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f"error: {path.name} に {signature!r} が見つかりません")
    opened = text.find("{", start)
    depth = 0
    for index in range(opened, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[opened : index + 1]
    raise SystemExit(f"error: {path.name} の {signature!r} の本体が閉じません")


def main() -> int:
    tick_infinity = method_body(TICK_INFINITY, "private void TickInfinity()")
    tick_personal = method_body(TICK_PERSONAL, "private void TickInfinityPersonalChoice()")
    mode_tick = method_body(MODE_TICK, "internal static void Tick()")

    calls_session = "TickInfinityPersonalChoice();" in tick_infinity
    calls_mode_tick = "InfinityMode.Tick();" in tick_infinity
    # 2回目の呼び出しはこの行頭にあり、冪等な再実行になる。
    calls_personal = "InfinityMode.TickPersonalChoices();" in tick_personal
    calls_mode = "TickPersonalChoices();" in mode_tick

    print(f"ClientSession.TickInfinity -> TickInfinityPersonalChoice: {calls_session}")
    print(f"ClientSession.TickInfinity -> InfinityMode.Tick:          {calls_mode_tick}")
    print(f"TickInfinityPersonalChoice -> TickPersonalChoices:        {calls_personal}")
    print(f"InfinityMode.Tick -> TickPersonalChoices:                 {calls_mode}")

    if calls_session and calls_mode_tick and calls_personal and calls_mode:
        print(
            "FAIL: TickInfinity 1回につき TickPersonalChoices が2回呼ばれる呼び出し経路が"
            "両方とも存在します（プレイヤー走査と TryRelease の重複）。"
        )
        return 1
    print("OK: TickPersonalChoices への経路は1本です。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
