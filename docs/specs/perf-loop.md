# 軽量化ループの記録

1回に1つ、遊び方・強さ・判定・数値・見た目を変えずに軽くできる改善を積み重ねた記録。各節は作業ブランチ（PR）ごとに分かれる。新しい記録はこのファイルの末尾に足す。

## perf-loop/20261018-1420

対象は native Dew.Core/AbilityInstance.cs（StatusEffect と Projectile の基底クラス）。改善なし。

毎フレームの経路（LogicUpdate、HandleSequence）は添字の for ループで早抜けがあり、シーケンスの実行状態は静的プールで再利用していた。被弾ごとの経路（CreateDamage、Heal、GetValue、effectiveLevel）は必要な1つの確保と数回の分岐だけだった。通信（SerializeSyncVars、DeserializeSyncVars）はdirty bitの判定だけだった。OnCreate の反射による上書き判定は型ごとに辞書へキャッシュされ、FxPlay は効果がないときすぐ返った。ActorManager.enableUsefulActorName は既定で無効なので、GetActorReadableName の文字列組み立ても毎フレームには走らなかった。

見送った候補は次の3つ。

- DestroyOnDeath のデリゲート確保（1回の登録で4〜5件）。状態効果の付与時にしか呼ばれず頻度が低い。調査中に本体の不具合も見つけた（キャッシュ用のフィールドへ代入し忘れたため、購読の解除が機能していない）。直すには購読の管理を変えることになり、結果が変わらないと保証できないため触らない。
- OnDestroyActor の StartCoroutine とクロージャ（破棄ごと）。開始効果を止めるために2フレーム待っており、別の待ち方に置き換えると停止のタイミングが変わるおそれがあるため見送り。
- HandleSequence の enumerator.Current の重複読み出し（1ステップで最大5回）。Transpiler が必要な割に効果は毎フレーム数ナノ秒の桁のため見送り。

## perf-loop/20261018-1443

対象: 本体 `Dew.Core/AbilityTrigger.cs` の `LogicUpdate`（ホスト・ゲストの両方で、トリガーごとに毎ロジックフレーム走る）。

直した内容: クールダウンティックの `maxDelta = dt / GetCooldownTimeMultiplier(i)` は、残り時間がすでに 0 の設定（大半を占める待機状態）でも毎フレーム乗数計算を辿っていた（owner 解決 → `Status.finalStats` の構造体コピー → `GameManager.ges` 参照 → 除算と `Lerp`、さらに既存の安全装置 Postfix）。`NativePerfPatches.cs` に `NativeCooldownIdlePerf` を足し、`LogicUpdate` 内のこの1箇所の呼び出しだけを Transpiler で置き換え、残り時間が 0 のときは乗数計算を飛ばして 1 を返すようにした。設定は `nativeCooldownIdleMultiplier`（既定で有効）。ほかの呼び出し元（`SetCooldownTime`、`GetMaxCooldownTime`、クールダウン減少）は元の仮想呼び出しのまま。

結果が変わらない理由: このループで乗数は `Mathf.MoveTowards(残り, 0, maxDelta)` の `maxDelta` にだけ使われ、残りが 0 のときは移動量がどうであっても結果は 0 に保たれる。残りが 0 以外のときは従来どおり仮想呼び出し（オーバーライドと `NativeCooldownSafety` の Postfix も含む）を使う。パッチ先の形が変われば警告を1回出して無効になる。

効果の見込み（推定）: トリガー × 設定 × フレームのうち待機状態の呼び出しを丸ごと省く。部屋の中位の規模でトリガー 100〜200、待機が8〜9割として、毎秒数千〜1万回の呼び出し分（推定で毎秒0.5〜2ミリ秒程度）を削れる見込み。実機での計測はしていない。
