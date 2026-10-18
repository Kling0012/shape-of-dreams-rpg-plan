# #623 取引表示と悪夢化率のレビュー対応

2026-10-18、#573 と #572 のレビュー提案16件を現行コードと照合した。
この記録の L1〜L16 は今回の提案IDであり、同じIssueの別の対応記録とは分けて扱う。

## 取引一覧

L1 は `GUILayoutUtility.GetRect` の幅の上限を `0f` から `float.MaxValue` に変えた。
最小幅0と `ExpandWidth(true)` は残し、一覧領域が親の幅に広がる指定を明確にした。
Unityの実レイアウトで幅0になる症状は、この環境では再現していない。

L4 は説明のプレビューが46pxの高さを超える場合に「…」を表示するようにした。
`GUIStyle.CalcHeight` で判定し、切れる場合だけ右端に24pxの記号欄を確保する。
省略記号の `GUIContent` は再利用し、全文のツールチップも設定する。
行高100pxと、選択・絞り込み・提示内容は変えていない。

L3 は追加修正が不要だった。`WheelScroll` は内容高と直前の表示領域高を
`ScrollMath.Wheel` に渡し、その中で上限を「内容高 − 表示領域高」に制限している。
内容高から表示高を呼び出し側でも引くと、二重に差し引くことになる。
既存の境界テスト6件を実行して成功した。

L7 は `CHANGELOG.md` に、全文をホバーと選択済み欄で読める案内が既にある。
今回は同ファイルを変更せず、省略記号の追加だけを `changelog.d/623-trade-preview.md` に記した。
L8 は、取引タブの任意の `GUI.tooltip` に同じ描画処理が適用される点をハーネスのREADMEに追記した。

## 再現ハーネス

L5 は #573 の行高・一覧用バッファ・スタイルなどのフィールドを、製品から直接抽出する形にした。
`NativeApi.cs` の重複定義を外し、引数なしの `GUIContent` と簡易の `CalcHeight` を代役に加えた。
最初の実行は引数なしコンストラクターがなくビルドに失敗した。代役を追随させたあと、既存13件が成功した。
この代役はフォント・折り返し・Unityの幅計算を再現しないため、表示の見た目を証明するものではない。
READMEでは修正前の記録と現在の回帰確認を区別し、修正を次工程とする古い案内を整理した。

L6 は `tests/repro/issue573/artifacts/` を `.gitignore` に加えた。
L9 は32行が当初の58pxを想定した余裕付きの判定上限であることをREADMEに明記した。
現在の100px行高とハーネスの1010px表示高では、製品の描画上限は `floor(1010 / 100) + 5 = 15` 行となる。
既存の32行の期待値やケースは変えていない。

L13 は #572 の実行ファイル名を `SodRpg.Core.Tests` から `Issue572.Repro` に変え、
Core の `InternalsVisibleTo` に専用名を加えた。既存15件は名前の変更前後とも成功した。

## 圧の同期

L10 は現行コードで対応されていた。`StagePressure` が毎ティック `SendPressure` を呼び、
その時点の `PartyGearChanceMult()` を送信済みの値と比較する。
装備倍率が変われば定期送信の期限前でも送る。同値の場合だけ、対応相手には30秒、旧相手には5秒まで送信を省く。
今回は同期処理を変更していない。実通信での反映時間は測定していない。

L11 と L16 は保留にした。現在は悪夢化用の `nightmarePressureHealthMultiplier` も同期するが、
表示側では引き続きfloatからdoubleへの変換と道標の圧による除算・乗算を行う。
整数パーセントの境目でホストと厳密に一致することは、この確認では保証しない。
圧倍率や `RewardPressure` を別形式で送る変更は通信の互換性判断を伴うため、この工程では行わない。

## 保留と未実行

- L2：現在のツールチップはウィンドウの `BeginArea` 内で両スクロール欄を閉じてから描く。
  座標の前提はREADMEに記した。Unityの実描画での位置やはみ出しは、ゲームを起動できないため未確認。
- L12：共有計算関数を対象とする単体テストへの移行は、新規テストの作成・設計を含むため保留。
- L14：旧版ホストとの接続、フィールド省略時の最低値表示、プロトコル警告の有無は、実通信環境がないため未実行。
- L15：1920×1080・既定倍率での日英の確保地点パネルと、注記の折り返しは、ゲームを起動できないため未実行。
- 未実行：取引一覧の幅、省略記号、長い説明、ツールチップのゲーム内描画・スクリーンショット。
  コードとハーネスの確認にとどまり、見た目を確認済みとはしていない。
- 未実行：全件・低速テスト、`tools/test_changed.py --all`、`dreamforge_simkill`。
  変更部分に絞る指示に従い、全件検証は後続工程に委ねる。

## 実行した検証

- `python3 tests/repro/issue573/run.py`：変更前後とも13件成功。途中の代替APIのビルド失敗は上記のとおり解消した。
- `python3 tests/repro/issue572/run.py`：変更前後とも15件成功。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~StarMapViewTests.Wheel_scroll_moves_and_clamps'`：6件成功。
- `dotnet build src/SodRpg.Mod -c Release -p:GameDir='/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir='' -p:BuildProjectReferences=false`：エラー0・警告6。
  参照先に `Mods` ディレクトリはなく、配置処理は実行されていない。
- `git check-ignore -v tests/repro/issue573/artifacts/probe.log tests/repro/issue573/artifacts/review623/issue573-original.md`：両方が除外対象。
- `git diff --check`：成功。
