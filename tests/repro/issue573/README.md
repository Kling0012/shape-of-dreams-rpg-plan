# Issue #573 修正前の再現

取引タブ（`DrawCoopTradeOwnRelics` / `DrawCoopTradeRelicChoices`）が、保管庫の遺物を
スクロール位置に関係なく1回の OnGUI イベントごとにすべて描画することを、製品コードを
変更せずに再現する。修正は次工程で行う（この段階では製品コードに手を付けない）。

```sh
python3 tests/repro/issue573/run.py
```

.NET SDK と Python 3 が必要（.NET 10 のみの環境では `DOTNET_ROLL_FORWARD=Major` を
run.py が既定する）。ゲームの起動・DLL 配置は不要。`artifacts/<UTC時刻>/repro.log` と
`results.json` に実行出力を保存する（git管理対象外。bin・obj もコミットしない）。

## 方法と限界

- `extract.py` が `src/SodRpg.Mod/DreamforgeUi.CoopTrade.cs` から
  `DrawCoopTradeOwnRelics`・`DrawCoopTradeRelicChoices`・`MatchesCoopTradeFilters`・
  `TryCoopTradeAmounts`・`CopyCoopTradeOffer` をそのまま切り出して
  `obj/` 下に自動生成する（改変しない）。ループ本体（`IsEquippedAnywhere` と
  `MatchesCoopTradeFilters` の呼び出し、保管庫と鞄の両描画）が見つからなければ
  ビルドを失敗させる。
- `NativeApi.cs` の代役: `UnityEngine.GUI`・`GUILayout`・`GUILayoutUtility`・`GUIStyle`・
  `GUIContent`・`GUILayoutOption`・`Rect`・`Event`・`Vector2`・`Mathf` は呼び出しを記録するだけの実装、`ClientSession`（`SetCoopTradeOffer`
  を記録）・`UiStyles`・`CoopTradeView`・`DrawCoopTradeOfferedRelics`（呼ばれたことだけ
  記録）は本件の測定に不要な簡易版。`CoopTradeRelicText` も同様の簡易版で、本番と同じ
  辞書キャッシュと `○ `/`● ` のタイトル形式だけ合わせ、行の識別用に Uid を標題に添える。
- `Profile`・`Relic`・`CoopTradeOffer`・`Loc`・`Content` は実物（`SodRpg.Core` を
  プロジェクト参照）。遺物は `Loot.RollRelic` でレア度・部位・レベルを循環させて作る。
- 計測するのは「1回の `ReproDrawOwnRelics` 呼び出し（= OnGUI 1イベント）で何行の遺物行
  ボタンが描かれたか」。折り返しラベルの高さ計算そのものは実行しない（実 GUI がない）ため、
  フレームレートや GC の実測ではない。行数が件数に比例して増えることが重さの原因である
  こと（Issue の記述）を、行数の上限で捉える。
- 行数の上限は 1一覧あたり 32 行（行の最低高 58・1080p の取引ペインで見える行数＋
  RelicList と同じ前後の余白 −2/+5 を含む余裕付き）。保管庫＋鞄の2一覧は 64 行。

## 修正前の結果（2026-10-17、このブランチ）

`python3 tests/repro/issue573/run.py` の終了コードは **1**（13件中5件失敗・8件成功）。
記録: `artifacts/20261016T190818419381Z/`。

| ケース | 結果 | 修正前の観測 |
| --- | --- | --- |
| perf/1000-stash-rows-per-event | **失敗** | 保管庫1000件・スクロール位置0で1イベントに **1000行** 全描画 |
| perf/1960-stash-rows-per-event | **失敗** | 保管庫1960件（工房上限）で **1960行** |
| perf/mid-scroll-draws-only-that-window | **失敗** | スクロール位置40000でも先頭から **1000行**（位置が描画に反映されない） |
| perf/scroll-past-end-shows-last-relic | **失敗** | スクロール位置10000000でも **1000行** |
| perf/stash-and-satchel-two-windows | **失敗** | 保管庫500＋鞄500で **1000行** |
| control/small-list-draws-every-relic | 成功 | 10件は全行描画（仮想化で行が消えないことの保証） |
| filters/rarity-narrows-drawn-rows | 成功 | レジェンドだけが描かれる |
| filters/search-narrows-by-name | 成功 | 表示名の一致だけが描かれる |
| equipped/equipped-relic-not-offered | 成功 | 装着中の1件を除いた14行 |
| selection/click-toggles-offer | 成功 | 行クリックで提示に加わり、●で描かれ、再クリックで外れる |
| selection/disabled-not-clickable | 成功 | 取引できない状態ではクリックが提示にならない |
| empty/no-unequipped-relics-message | 成功 | 行0・案内ラベルあり |
| empty/no-filter-match-message | 成功 | 行0・案内ラベルあり |

失敗5件はいずれも「1回の描画で N 行の遺物行が描かれた（見えている範囲の目安を超える）」
というアサーションで、Issue の「保管庫の遺物ごとに毎イベント描画する」を再現している。

### ハーネスの自己検証

`obj/` 下の自動生成ファイルだけを RelicList と同じ方式（行高固定・可視範囲のみ描画・
前後を空白で埋める・スクロール位置のクランプ）に一時的に差し替えたところ、**13件全部が
成功**した（製品コードは変更していない）。つまりこのテスト群は仮想化実装で満たせて、
失敗は仮想化がないことだけを指す。この自己検証で、対照case「装着中の遺物は一覧に出ない」
が窓の行数より多い件数で全行描画を前提にしていた欠陥も見つかり、窓に収まる15件に修正した。

## 判断したこと

- 再現の本体は行数計測とし、実際のフレームレート測定はしない（実 GUI がないため）。
- スクロール位置が一覧の長さを超えた場合（perf/scroll-past-end-shows-last-relic）は
  末尾の行が見えることを要求する。実機の IMGUI は内容の高さにスクロールをクランプするが、
  取引中に保管庫や鞄の件数が変わったときは UI の保持する位置が内容を超え得るため、
  描画側でもクランプ（または同等の耐性）が必要という受け付け審査の懸念を cases に組み込んだ。
- 絞り込みは適用後の一覧を基準に可視範囲を計算することを検証するため、レア度・検索の
  cases は小さい件数で「絞り込み結果だけが描かれる」ことを見る（大きい件数での絞り込みは
  perf の cases と同じ上限判定に含まれる）。
- 選択（クリック→提示への追加・解除・●表示）と、取引できない状態でのクリック不発は
  対照として押さえ、修正で壊れないことを保証する。通信そのものは代役が記録するのみ。

## 修正後の結果（2026-10-17）

- `python3 tests/repro/issue573/run.py`: **13件全部成功**。期待値・判定条件は変更していない。
- 行高は100px（カード96px＋間隔4px）。全行の高さを1つのレイアウト領域として確保し、
  可視行だけを固定座標の `GUI.Box`・`GUI.Button`・`GUI.Label` で描画する。
  領域の y 座標には選択済み欄・見出し・保管庫の高さが含まれるので、鞄にも同じスクロール位置を使える。
- 名前・レア度・部位の絞り込みと装着中の除外を行ってから可視範囲を計算する。
  フィルター変更で先頭へ戻し、保持するスクロール位置は内容高と表示領域高で制限する。
- 名前は RelicList と同じ単行、説明は固定領域の折り返しプレビューにして、行外への重なりを防ぐ。
  全文はホバー時のツールチップと既存の選択済み欄で表示する。実機の見た目は未確認。
- `NativeApi.cs` は固定座標の描画API・レイアウト領域・キャッシュ済みGUIContentに追随した。
  `extract.py` は製品のスタイル初期化も抽出する。新しいテストケースは追加していない。
- 修正を一時的に戻すと **5件失敗（exit 1）**、修正を復元すると **13件成功（exit 0）**。
  記録は `artifacts/revert-check.json` と各実行の `artifacts/<UTC時刻>/`。

## 次工程（修正）への引き継ぎ

- `DrawCoopTradeRelicChoices` の実装が変わっても抽出は継続する。新しい補助関数や
  フィールドを参照するようになったら `extract.py` の抽出対象と `NativeApi.cs` の
  ハーネス側 partial（`_tradeOwnScroll`・`_windowWidth`・`_windowHeight` は用意済み）に
  追随させる。判定条件・期待値（行数の上限・対照の挙動）は変えない。
- `git checkout -- <file>` 等で製品を修正前に戻せば perf 5件が再び失敗する。

## 未実行・未解決

- 実機（ゲーム内）でのフレームレート・描画の確認、折り返しラベル（内容により高さが変わる
  表示）が固定行高に収まるかの見た目の確認: 実行環境がないため未実行。固定行高と長い
  説明文の兼ね合いは修正工程での設計判断と、実機確認は PR の「未実行」に記す。
- `dreamforge_simkill`、全テストスイート、`tools/test_changed.py --all`、製品MOD全体の
  Release ビルド: 再現工程の範囲外として未実行（後続パイプラインで実施）。
