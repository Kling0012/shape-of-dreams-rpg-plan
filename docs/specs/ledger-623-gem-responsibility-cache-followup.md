# #623 責任の宝石の被弾演出キャッシュに関する改善案

2026-10-24、PR #1131 のレビュー指摘 F1（C-II 部分未達）を確認し、対応を決めた記録である。

## 指摘の内容

`NativeGemResponsibilityHitPerf` は処理を軽くする変更だが、結果が変わらないことの保証が IL の指紋（本体の形の確認）と spec の論証だけで、Prefix への転写（`pitchCurve`・`volumeCurve`・`shakeAmpCurve` の割当先、`localScale` へ `volumeMultiplier` を使う式など）に誤りがあっても検出する手段がコードレビューしかない。また指紋は `GetComponentsInChildrenNonAlloc` の呼び出し回数しか数えておらず、走査する型・`includeInactive` の引数・`localScale` に渡す値の式を見ていない。テスト環境では受け口の境界スタブが空実装のため指紋検査は必ず不一致になり、追加した Prefix 本体は全テストで一度も実行されていなかった。

## 対応したこと

修正ラウンド（head 84895c68 の後）で、境界（`tests/Issue73.Native.Tests`）に記録可能な `DewAudioSource`・`FxCameraShake`・曲線・子の置き場を整え、`GemResponsibilityHitPerfTests` から Prefix を直接呼び出した。このテストで、`strength` から `pitchMultiplier`・`volumeMultiplier`・`amplitude`・`localScale` への書き込み値と `FxPlayNew(fx, e)` の呼び出し引数が曲線の評価結果から意図どおり導かれること、キャッシュの再作成が `fxHit` の参照が変わったときだけであることを検証した。あわせて指紋検査（`ValidateShape`）を強化し、走査のジェネリック型引数（`DewAudioSource`・`FxCameraShake`）、2引数の `GetComponentsInChildrenNonAlloc`（`includeInactive=false` の意味）、`set_localScale` の直前の `op_Multiply` が `volumeCurve` の評価結果を使っていること、曲線と書き込み先の対応まで確かめるようにした。拒否の見本（型の替え間違い・3引数化・別の曲線で `localScale` を作る・`fxHit` 以外を走査する・割当て替え・`FxPlayNew` の引数替え）を弾くことも同じテストで確認した。spec（`perf-loop/20261024-1529.md`）には、`fxHit` がプレハブアセットである根拠（ilspycmd の逆コンパイルで、受け口は `fxHit` へ値を書いて `FxPlayNew` に渡すだけでシーンへ置かない）を1行添えた。テストの作成はこのラウンドの依頼で明示的に許可されたもので、憲章の「専用のテストは作らない方針」と C-II（処理を軽くする変更は結果が変わらないことをテストで確かめる）の衝突は、境界テストを足す側で解決した。

## 残る制限と見直す条件

指紋は既知の命令（曲線の評価・子走査・書き込み・`localScale`・`FxPlayNew`）の並びと回数を見るため、本体がこれら以外の種類の文（新しいメソッド呼び出しや別フィールドへの書き込み）を追加しても検出できない。ゲーム更新のたびに spec の規定どおりこの指紋を見直すこと。検出強化の改善案として、対象メソッドの命令総数の比較や IL のハッシュ化など、数えていない文も検出できる指紋への拡張を今後検討する（実装時期は未定。現行の指紋で誤検知なしに運用できている間は必須としない）。
