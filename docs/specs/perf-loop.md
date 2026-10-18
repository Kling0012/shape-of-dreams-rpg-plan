# 軽量化ループの記録

1回に1つ、遊び方・強さ・判定・数値・見た目を変えずに軽くできる改善を積み重ねた記録。各節は作業ブランチ（PR）ごとに分かれる。

## perf-loop/20261018-1443

対象: 本体 `Dew.Core/AbilityTrigger.cs` の `LogicUpdate`（ホスト・ゲストの両方で、トリガーごとに毎ロジックフレーム走る）。

直した内容: クールダウンティックの `maxDelta = dt / GetCooldownTimeMultiplier(i)` は、残り時間がすでに 0 の設定（大半を占める待機状態）でも毎フレーム乗数計算を辿っていた（owner 解決 → `Status.finalStats` の構造体コピー → `GameManager.ges` 参照 → 除算と `Lerp`、さらに既存の安全装置 Postfix）。`NativePerfPatches.cs` に `NativeCooldownIdlePerf` を足し、`LogicUpdate` 内のこの1箇所の呼び出しだけを Transpiler で置き換え、残り時間が 0 のときは乗数計算を飛ばして 1 を返すようにした。設定は `nativeCooldownIdleMultiplier`（既定で有効）。ほかの呼び出し元（`SetCooldownTime`、`GetMaxCooldownTime`、クールダウン減少）は元の仮想呼び出しのまま。

結果が変わらない理由: このループで乗数は `Mathf.MoveTowards(残り, 0, maxDelta)` の `maxDelta` にだけ使われ、残りが 0 のときは移動量がどうであっても結果は 0 に保たれる。残りが 0 以外のときは従来どおり仮想呼び出し（オーバーライドと `NativeCooldownSafety` の Postfix も含む）を使う。パッチ先の形が変われば警告を1回出して無効になる。

効果の見込み（推定）: トリガー × 設定 × フレームのうち待機状態の呼び出しを丸ごと省く。部屋の中位の規模でトリガー 100〜200、待機が8〜9割として、毎秒数千〜1万回の呼び出し分（推定で毎秒0.5〜2ミリ秒程度）を削れる見込み。実機での計測はしていない。
