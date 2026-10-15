# 星図差分更新の作業・検証履歴

[製品仕様と実機確認リスト](../specs/star-allocation-performance-2026-10-15.md) から、
#436・#440・PR #456 の修正ラウンド、取込時の検証、結果 JSON の対応記録を移した。
以下は当時の記録であり、今回実行した検証とは区別する。履歴の本文は維持している。

ビルド警告の読み方: PR #456 の「追加コメント確認」の0件は増分ビルドで、
「要件 R1 の対応報告」の6件はコンパイルを伴う完全ビルドの既存警告である。
同じ条件で測った件数ではなく、警告が解消・再発したという意味ではない。
Issue73 の境界スタブ由来の警告件数とも区別する。

## #466 の変更と検証（2026-10-16）

- `Fields` に境界が読む5フィールドを明示し、`exposeType` の置換を1回だけに限定した。
  r1・r3 の同趣旨の提案は同じ変更で対応した。
- 既存フィクスチャの冒頭で `Hero_Cetus` の2歩目の普通の星と二択2つ以上を検証する。
  テストケースの追加はない。初期化ヘルパーのずれは、対応式の構文比較を保守候補として仕様・コメントへ記録した。
- 修正前の `dotnet msbuild tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -t:CompileStarClusterDiffUpdate -p:Configuration=Release -verbosity:minimal` で、
  フィールドを改名しても抽出が成功し、入れ子の private クラスが internal へ変わることを再現した。
- 同じ抽出コマンドで5フィールドを個別に一時改名すると、修正後は各回 exit 1 のフィールド契約エラーになった。
  抽出定義だけ修正前へ戻すと各回 exit 0 になり、検知の欠落が再現した。
  一時追加した private / private sealed の入れ子クラスは、修正後はいずれも private のまま、
  修正前は両方 internal になった。製品ソース・抽出定義・生成物は復元し、抽出は exit 0。
- BFS の距離結果から2歩目を一時的に除外し、二択の取得結果も別途1つへ制限して、対応する既存テストを実行した。
  修正後はいずれも前提変更の理由を示して exit 1、コンストラクターを修正前へ戻すと
  `Sequence contains no matching element` / 理由のない `Assert.True() Failure` に戻った。すべての一時変更を復元した。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --no-restore -p:WarningLevel=0 --logger 'console;verbosity=minimal' --filter 'FullyQualifiedName~StarClusterDiffUpdateTests'`:
  exit 0、5件成功、失敗0、スキップ0。修正前の同じ対象5件も成功していた。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/issue466-mod-build-jr3qh3af/moddeploy`:
  初回は exit 0・既存警告6件・エラー0、直後の同じコマンドの増分ビルドは exit 0・警告0・エラー0。
  ゲームへ配置しない一時出力先を指定した。
- 履歴本文の移管前後の一致、文書の相互リンク、`git diff --check` を確認した。
- 未実行: 全件・低速スイート（作業範囲の指示に従い後段へ委ねる）、生成物整合性検証（原本・生成物に変更なし）、
  Unity 実機の描画・マウス入力・二択パネル・レイアウト・フレーム時間（実行環境なし）。
  実機項目は仕様書の優先確認リストに未実行として残した。

## 移管した履歴

### 回帰テストの現状と実機確認リスト（当時）


次の3つの差分更新は `DreamforgeUi` の Unity IMGUI（`Event.current`・`GUIStyle.CalcHeight`）に依存する
privateな内部状態で、Issue73抽出ハーネスの抽出対象にも入っていないため、リポジトリ内の既存テストでは
直接確認できていなかった。

- 左一覧の高さの部分再計算（`RefreshStarClusters` の変更行のみの計測と `_starClusterDirtyFrom` からの加算）
- `TextRank`/`TextChoice` による本文更新スキップ
- ツールチップの世代管理（`TooltipVersion`/`_starTextVersion`）

修正ラウンド2回目（2026-10-15）で、これら3つとホバー維持加点・二択変更の表示更新を、下記
「UI 差分更新の直接実行による確認」のシナリオランナーで実メソッドを実行して確認した
（全項目合格）。この時点では、リポジトリ内の恒常的な回帰テスト（xunit）は #440 に切り出し、
抽出ハーネスへの `DreamforgeUi` 抽出の追加と IMGUI 境界スタブの用意を案としていた。
その後 #440（PR #441）で実装した内容は、末尾「差分更新への直接回帰テスト」に記録する。

保存・送信側（`MarkAllocationDirty`・`SavePendingAllocation`・`DeferSave` の期限）は、#438 で抽出契約
（`NativePersistence.targets`）に `MarkAllocationDirty`/`SavePendingAllocation`/`OnAllocationSceneChanged` を追加し
（境界の `Scene` 型と `_allocationSavePending`/`_allocationBuildDue` フィールドは #433 の 980224b で既に準備済み）、
実際のソースから抽出した実メソッドを一度限りのシナリオランナー（`/tmp/sod-alloc-436`、リポジトリには加えない。
#433 の一時ベンチマークと同じ方式）でリフレクション駆動して直接確認した。条件・期待結果・実際の結果
（2026-10-15、全項目合格、`ALL SCENARIOS PASSED`）:

| シナリオ | 操作（実メソッド） | 期待結果 | 実際の結果 |
|---|---|---|---|
| 連打中の送信期限 | t=100 でベースライン送信後 `MarkAllocationDirty`（due=100.15）、t=100.10 で再 `MarkAllocationDirty` | 送信期限は最初の変更+150msのまま後ろへ延びない | due=100.15 のまま（100.25 に延びない）。合格 |
| 送信ゲート | 期限内（100.10・100.149）に `SendBuildIfNeeded`、期限到達（100.16）で再実行 | 期限内は送らず、期限到達で一度だけ送る | 期限内 sent=1（不変）、100.16 で sent=2・`_buildDirty`=false。合格 |
| 連打中の保存期限 | t=100 `MarkAllocationDirty` → t=101.5 再実行（+ `MarkDirty(true)` も） | 保存予約は最初の変更+3秒のまま | `_nextSave`=103 のまま（min）。合格 |
| 装備変更等の保存予約 | t=100 `MarkDirty(true)`（ロビー） | `_allocationSavePending` が立ち+3秒の保存予約 | pending=true・`_nextSave`=103。合格 |
| メニュー・タブを閉じた直後 | `MarkAllocationDirty` → `SavePendingAllocation` | 即座に1回保存し pending を落とす。保留が無いときは再保存しない | saveCount=1・pending=false・ディスクへ現行リビジョン書込・再呼び出しで saveCount=1 のまま。合格 |
| シーン遷移時 | `MarkAllocationDirty` → `OnAllocationSceneChanged` | 未保存の割り振りを保存する | saveCount=1・pending=false・書込完了。合格 |
| 書き込み失敗後の再試行 | 保存先を壊して `SavePendingAllocation`（背景書き込み失敗）→ t=105 `TickPeriodicSave` → t=110 再実行 | 失敗で pending を立て直し+5秒の再試行期限（30秒に延ばさない）を設け、期限で書き直し、成功後は失敗表示を消す | 失敗検知→pending 復活・`SaveError` 表示・`_nextSave`=110→t=110 で書き直し成功・pending=false→`SaveError` 消滅。合格 |

UI から `SavePendingAllocation` を呼ぶ場所（メニューを閉じた時・星図タブを離れた時、`DreamforgeUi.cs` の
`Close`・タブ切替）はコード追跡のとおり。終了時の `FlushSaves` は既存テスト（`NativeAcceptanceTests` 等）が
後片付けで実行している。抽出契約を拡張した後の既存テストは 67件（`ContinueSave`/`ClientTickIsolation`/
`TradeSendException`/`SatchelOverflowSave`/`LobbyReturn`/`NativeAcceptance`/`InfinityClearSave`/
`OverflowBonusFailSoft`）すべて合格。

実機で確認すべき残項目（ゲームを起動する環境が無いため未実行）:

- ホバーを維持したまま別の星へ加点したとき、条件文とダメージ注記が即座に更新されること。
- 二択を変更した後、星の本文表示と左一覧の表示が正しいこと。

（連打中の送信・保存期限、メニュー・タブを閉じた直後とシーン遷移時の保存、書き込み失敗後の5秒再試行は、
上記のシナリオランナーで対象経路（実際の `ClientSession` の各メソッド）を実行して確認済み。
上の2項目も修正ラウンド2回目に UI シナリオランナーで対象経路（実際の `DreamforgeUi` の各メソッド）を
実行して確認した — 次節のとおり。実機に残るのは描画・操作感の最終確認。）

## 追補: UI 差分更新の直接実行による確認（#436 修正ラウンド2回目、2026-10-15）

上の2項目（ホバー維持の加点・二択変更）と3つの差分更新（高さの部分再計算・TextRank スキップ・
ツールチップの世代管理）を、保存・送信側と同じ方式の一度限りのシナリオランナー
（`/tmp/sod-starui-436`、リポジトリには加えない）で対象経路そのものを実行して確認した。

構成: `NativePersistence.targets` と同じ Roslyn 構文抽出タスクで、実ソースから
`RefreshStarState`・`EnsureStarTooltip`・`EnsureStarChoiceText`（`DreamforgeUi.cs`）と
`RefreshStarClusters`・`DrawStarClusterList`・`StarRegionColor`（`DreamforgeUi.AuthoredStars.cs`）を
抽出し（実装のコピーではなく構文選択）、実 `SodRpg.Core`（`StarMapPresentation`・`HeroTreeLayout`・
`Rules`・生成星図 `StarClusters.RegisterGeneratedHero("Hero_Yubar")` — 本体の起動処理と同じ登録）
に対してコンパイルした。Unity IMGUI 境界（`Event`/`EventType`/`GUIContent`/`GUIStyle`/`GUILayoutUtility`/
`GUI`）のみスタブで置き、`GUIStyle.CalcHeight` は決定的な行数換算（呼び出し記録つき、
高さの差し替えも可能）にした。UI 状態は `RebuildStarTree`/`RebuildStarClusters` と同じ値で初期化し、
加点は実際の割り振りと同じ状態遷移（`Talents` に1段、二択星は `TalentChoices` も設定）で行った。
抽出契約どおり、メンバー構成が変われば抽出が失敗する。

条件・期待結果・実際の結果（2026-10-15、全48項目合格、`ALL SCENARIOS PASSED`）。A=星A（ホバー中）、
B=星B（加点対象）、C=二択星（行5、全886行）:

以下は #436 時点の実行記録を残したもの。「二択変更の選択パネル」の候補列を再利用する挙動は、
その後 #440（PR #441）で世代基準に変更したため、現在は無料切り替えでも両候補を再生成する。
変更後の挙動は末尾の回帰テストで検証している。

| シナリオ | 操作（実メソッド） | 期待結果 | 実際の結果 |
|---|---|---|---|
| ツールチップの世代管理（R17） | `EnsureStarTooltip(A)` → もう1回 → 変化なし `RefreshStarState` → もう1回 | 生成後に `TooltipVersion` が世代に揃い、世代が同じなら再生成しない。変化がない Layout では世代も進まない | 初回生成（498文字）→ sentinel 維持 → version=1 のまま再生成なし。合格 |
| ホバー維持の加点・ダメージ注記（R7） | `EnsureStarTooltip(A)` → B へ加点 → 次の Layout で `RefreshStarState` → `EnsureStarTooltip(A)`（キャンバス末尾のホバー経路と同じ呼び出し） | 加点直後は古いまま。世代進行後の最初の `EnsureStarTooltip` でダメージ注記が使用済み1点の倍率に更新される | 加点直後は旧文のまま → ×1（使用済み0点）が ×1.003（使用済み1点）に更新。`MissingRequirements` は現行状態と一致。合格 |
| ホバー維持の加点・条件文（R7） | ポイント余裕1の状態で上と同じ操作 | 余裕が尽きた星Aの条件文（ポイント不足）と状態行（条件不足）が更新される | 「取得可能」→ 条件文「ポイント不足：必要1・残り0・あと1。」＋状態行「条件不足」。合格 |
| TextRank スキップ（R16） | A・B に sentinel 本文を仕込み、B のみ加点して `RefreshStarState`。さらに変化なし `RefreshStarState` | 加点した B のみ `RankLabel`/`DisplayBody`/`SearchDescription` が再生成され、A は sentinel のまま。検索の汚れは再生成があったときのみ立つ | B は 1/3 ラベルに再生成・A は sentinel 維持・検索汚れ=立つ→変化なしでは立たない。合格 |
| 二択変更の本文と一覧（R18） | C を選択肢0で取得し一覧を確定 → 選択肢1へ切替（無料）→ `RefreshStarState` → `EnsureStarChoiceText(C)` | 本文（`ChoiceDescription`）と効果要約が選択肢1の内容に再生成され、左一覧の行ラベルも新しい要約で再構築される。汚れは変更行からのみ（`heights[C]=0`・`dirtyFrom=行C`） | 本文・要約・一覧ラベルとも選択肢1の内容（エキゾチック物質→集まる星々）に更新・dirtyFrom=5。合格 |
| 二択変更の選択パネル（R18/R11） | 切替後に `EnsureStarChoiceText(C)` | パネル列の本文は使用済みポイントのみに依存するため再構築されない（sentinel 維持）。パネルが読む `MissingRequirements` は現行の状態。ツールチップ本文も選択肢1に再生成 | 列は sentinel のまま・`MissingRequirements` 現行と一致・ツールチップは選択肢1の本文を含む。合格 |
| 高さの部分再計算・冷間start（R15） | 初回 `DrawStarClusterList`（Repaint） | 未測定の全行の高さを測定する | 886行すべて測定（calcCalls=886）。合格 |
| 高さの部分再計算・変更行のみ（R15） | 二択変更後の `DrawStarClusterList`（行C の高さを+30px に差し替え） | 変更行だけ再測定し、変更行より前の行位置は不変、より後の行位置は高さ差分（+30）だけ移動、一覧の高さ合計も差分反映、再計算後に汚れ解除 | calcCalls=1（行Cのみ）・前行不変・後続行+30・合計も+30・dirtyFrom=886。合格 |
| 高さの部分再計算・変化なし（R15) | 追加の変更なしでもう1回 `DrawStarClusterList` | 再測定も位置計算も起きない | calcCalls=0・高さ合計不変。合格 |
| 経路取得の汚れの局所性（R15 補助） | 経路の星を取得した後、二択星 C を取得して `RefreshStarState` | この Layout で一覧の汚れが立つのは C の行だけ | dirtyFrom=5（=行C）。合格 |

実機に残るのは描画と操作感（画面上のツールチップの見え方・パネルの視覚）の確認だけで、
上記のとおり状態・文字列・行位置そのものは実コードの実行で確認済み。

## 差分更新への直接回帰テスト（#440）

上記で一度限りのシナリオランナーにより確認した差分更新3点を、Issue73抽出ハーネスの恒常的な回帰テストでも直接検証した
（`tests/Issue73.Native.Tests/StarClusterDiffUpdateTests.cs`、5件）。

- `NativePersistence.targets` が `RefreshStarState` / `RefreshStarClusters` / `EnsureStarTooltip` /
  `EnsureStarChoiceText` / `DrawStarClusterList`（と `StarRegionColor`）を実ソースから抽出する。
  `StarNode` と依存するフィールドも、型・初期値・同時宣言を実ソースから抽出する（`Fields` / `NestedTypes`）。
  `StarNode` はテストから読むために抽出時だけ `internal` にし、メンバーは変更しない。
  指定した型・フィールドが見つからなければ抽出契約エラーにするため、改名・削除を検知できる。
  境界は `NativeBoundary.cs` の Unity IMGUI スタブ（決定論的な `GUIStyle.CalcHeight`、`Event.current`、
  `GUIContent`、呼び出し計数）と `StarClusterUiBoundary.cs` の周辺メソッド・初期化ヘルパーのみ。
  状態定義を境界へ手で書き写す方式は、PR #441 の取込時の品質指摘に従い置き換えた。
- 左一覧: 加点後に高さ計測が変更行だけ（CalcHeight 1回）で、`_starClusterDirtyFrom` から位置を
  加算し直すこと、未変更行の高さ・位置・ラベルが保たれること、幅変更時は全行を再計測すること。
- 本文: 変更のない Layout では世代も本文も動かず、加点時に変わった星だけ `DisplayBody` が
  新しいインスタンスに作り直され、未変更星は同じインスタンスのまま残ること（TextRank/TextChoice）。
- ツールチップ: 同一世代では再生成せず（同じ文字列インスタンス）、加点で世代が進むと条件文と
  ダメージ注記が再生成されること（TooltipVersion/_starTextVersion）。
- 二択: 取得で本文と両候補が作り直され、無料切り替えでも使用済みポイントが変わらずとも
  世代が進むので本文・両候補・ツールチップが作り直されること（EnsureStarChoiceText/ChoiceTextVersion）。
  これに伴い `EnsureStarChoiceText` のキャッシュ判定を使用済みポイント基準（ChoicePoints）から
  `_starTextVersion` 世代基準（ChoiceTextVersion、TooltipVersion と同じ設計）に変えた
  （#440 レビュー指摘。無料切り替えでも候補が再生成されるように）。無関係な星の更新で世代が進んだ場合も、
  開いている二択の候補は次の表示時に再生成する。同一世代の再描画では再生成しない。
- 言語: フィクスチャのコンストラクターで `Loc.Japanese = true` を明示し、高さ差を必要とするケースだけ英語にする。
  各ケースの `Dispose` で開始前の言語へ戻す。取込時の検証では既定言語を一時的に英語にし、
  明示設定のない元テストのツールチップ・二択2件が失敗、明示設定ありでは2件とも成功することを確認して復元した。
- 壊し確認: 世代判定（TextRank/TextChoice と TooltipVersion）の削除、`_starClusterDirtyFrom`
  追跡の削除で、対応するテストが失敗することを確認してから元に戻した。ChoiceTextVersion の
  世代判定を旧来の使用済みポイント基準に戻しても `Choice_change_...` が失敗することを確認した。
  取込時にも4種すべてを再実行し、対応するアサーションでの失敗を確認して復元した。
  さらに製品の `_starSpent` を一時的に改名すると、抽出処理がフィールド契約エラーで失敗することを確認して復元した。

### PR #456 の追加コメント確認（2026-10-16）

追加コメントは Release ビルド、生成物整合性、差分テスト482件、全件テスト3238件の成功報告であり、
新たな不具合や期待動作の変更は含まれていなかった。受け付け審査の状態定義の手書きと既定言語依存は
取込コミットで対応済みのため、機能・テストケースは追加変更せず、対象経路と修正の有効性を再確認した。
以下はコメントの実行結果の転載ではなく、この修正ラウンドで実行した検証である。

- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~StarClusterDiffUpdateTests' --logger 'console;verbosity=minimal'`:
  5件成功、失敗0、スキップ0。
- コメントと同じ `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/gh-auto-moddeploy`:
  成功、エラー0、警告0（増分ビルド）。
- 言語依存の再現: `Loc` の共有既定値を一時的に英語へ変更。コンストラクターの明示設定を外した状態では
  ツールチップ・二択の既存2件が `Assert.Contains` で失敗し、現行の明示設定ありでは2件とも成功した。
  共有既定値とテストの一時変更は復元した。
- 抽出契約の確認: 製品の `_starSpent` 宣言を一時改名すると、ネイティブハーネスのビルドが
  `Native persistence field contract changed`（MSB4018）で失敗した。改名は復元した。
- 全件・低速テスト、生成物整合性検証、Unity 実機の描画・操作確認はこのラウンドでは未実行。
  変更は検証記録のみであり、全件・低速テストは後段の動作確認へ委ねる。生成物は変更しておらず、
  Unity 実機の実行環境もないため、コメントの全件成功を実機描画・操作の確認済みとは扱わない。

### PR #456 の要件 R1 の対応報告（2026-10-16）

追加コメントの成功報告が、この取込PRの要件 R1 として列挙された。前ラウンドではコメントを確認したものの、
結果 JSON の `requirements_done` に R1 を記載しておらず、対応済みとして認識されなかった。
今回の結果 JSON では `id: R1` と、この節・検証コマンドを示す `where` を明記する。
元PR #441 の「左一覧の高さ」の R1 とは別の、この取込PRの追加コメントに対する要件である。

- 既存の `StarClusterDiffUpdateTests` を Release で再実行し、5件成功、失敗0、スキップ0。
- 追加コメントと同じ Mod Release ビルドを再実行し、エラー0、既存箇所の警告6件で成功。
- 状態定義の実ソース抽出、`Loc.Japanese = true` の明示設定と復元は対応済みであることを再確認。
  二択候補の世代基準による遅延更新方針も維持する。新しいテストや製品動作の変更はない。
- 全件・低速テスト、差分全体のテスト、生成物整合性検証、Unity 実機確認は今回は未実行。
  前二者は対象を絞る指示、生成物整合性はバランス原本・生成物に変更がないため、実機確認は実行環境がないため。
  過去の成功報告と今回実行した結果を区別して報告する。
