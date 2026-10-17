# Issue #528 修正前の再現

純白（終点）ゾーンのボス撃破取りこぼし・潜行後の毎秒 ACK・個人選択の二重 Tick を、
製品コードを変更せずに再現する。いずれも修正前に失敗（終了コード1）する。

```sh
# 点1: 純白ゾーンでボス撃破を取りこぼし、進行が止まる（xunit）
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj \
  -c Release --filter FullyQualifiedName~PureWhiteBossKilledWhileSaveHoldBlocksTickStillReachesSoulFinish \
  -p:PythonCommand=python3 -p:WarningLevel=0

# 点2: 適用済みの潜行決定へ毎秒 ACK を送り続ける（xunit）
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  -c Release --filter FullyQualifiedName~Issue528AckReproTests -p:WarningLevel=0

# 点3: TickPersonalChoices が1フレームに2回呼ばれる（ソース構造の確認）
python3 tests/repro/issue528/check_double_tick.py
```

.NET SDK と Python 3 が必要。ゲームの起動・DLL 配置は不要。`DOTNET_ROLL_FORWARD` が
未設定の場合は `Major` を使う。NuGet キャッシュは呼び出し元の環境変数を引き継ぎ、
worktree 内には設定しない。成果物・bin・obj はコミットしない。

## 方法と限界

- **点1**（`tests/SodRpg.Mod.Startup.Tests/InfinityIssue528ReproTests.cs`）: 本物の
  `InfinityMode.cs`（TickNative / OnRoomClear / Tick）をコンパイルした Startup ハーネスで、
  準備の門によるボスノード直接到着を再現する。保存待ち（`HostInfinitySaveHoldSatisfied`）を
  制御できるように境界スタブを `= true` の書き換え可能フィールドに変えた（既定値は不変）。
  保存待ちの間にボスを倒し（`didClearRoom` + `OnRoomClear`）、解除後の `Tick` で
  `WaitingSoulFinish` に到達することを期待する。実際の保存失敗・30秒待ち解除の所要時間は
  擬似時間で代用し、実機の保存遅延そのものは再現しない。
- **点2**（`tests/Issue73.Native.Tests/Issue528AckReproTests.cs`）: ビルド時に本物の
  `ClientSession.TickInfinity`（NativePersistence.targets による抽出）を動かす。保存先なしの
  確認待ちを 30 秒の待ち解除（擬似時間）で通して最初の ACK を成立させた後、適用済みで
  世代が古い choice（`state.GraphEpoch > choice.GraphEpoch`）への追加 ACK がないことを期待する。
  ACK の観測は境界の `InfinityMode.AcknowledgeLocal` を呼び出しカウンターに変えた観測点で
  行い、実際の Mirror RPC の送受信・取りこぼしは再現しない。
- **点3**（`check_double_tick.py`）: `ClientSession.TickInfinity` → `TickInfinityPersonalChoice` →
  `TickPersonalChoices` と `TickInfinity` → `InfinityMode.Tick` → `TickPersonalChoices` の
  2経路を、実ソースのメソッド本体から確認する。両プロジェクトのどちらも
  `TickInfinity` と本物の `InfinityMode.Tick` を同時にコンパイルしていないため、
  実行時の呼び出し回数の計測はこのハーネスでは行えない（静的構造の確認にとどめる）。

## 修正前の結果（2026-10-16）

実行環境: .NET SDK 10.0.111、net8.0 を Major roll-forward で実行、
`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`。

| 点 | 修正前の観測 | 終了コード |
| --- | --- | --- |
| 1 | 保存待ち解除後の Tick で `BossFight` のまま（期待 `WaitingSoulFinish`）。その時点のアサートで失敗し、後続の魂回収・選択到達の処理は未実行 | 1 |
| 2 | 最初の ACK 後、10秒で10回の追加 ACK（計11回、期待は最初の1回のみ） | 1 |
| 3 | TickPersonalChoices への経路が2本とも存在 | 1 |

対照として、変更した境界スタブ（保存待ちの書き換え・ACK 観測点）を使う既存テストは
`InfinityLobbyStartTests` 87件合格・`InfinityPersonalChoiceTests`/`InfinityContinueTests`/
`InfinityClearSaveTests` 11件合格（失敗は上記の再現テストのみ）。

## 判断したこと

- この工程はバグ再現に限定し、修正は次工程に残す。
- 点1の期待値は Issue の修正案どおり「TickNative の ExitBoss 分岐で TryEnterBoss 後に
  `room.didClearRoom && ObserveBossClear()` を観測する」とした。onRoomClear との二重処理に
  ならないことは `ObserveBossClear` が `BossFight` 相位でのみ受理する冪等な遷移であることから
  確認済み（`InfinityRunState.ObserveBossClear`）。
- 点2の期待値は「最初の ACK 確立以降の毎秒再送がないこと」とし、初期 ACK そのものの要否は
  修正の設計に委ねた（Issue の案では適用済みかつ世代が古いとき初期 ACK も送らない）。

## 再実行結果（2026-10-17）

前回の再現コミット `2a881f26` を引き継いだ `ai/issue-528` で、上記3コマンドを
修正前のまま再実行した。環境は Linux / .NET SDK 10.0.111 / Python 3、
`DOTNET_ROLL_FORWARD=Major`、`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`。
製品コードの変更は行っていない。

- 点1: 終了コード1。1件実行・1件失敗。
  `InfinityIssue528ReproTests.cs:49` で `Expected: WaitingSoulFinish / Actual: BossFight`。
  保存待ち中の `OnRoomClear` の後も `BossDue`、待ち解除後の Tick で `BossFight` と
  なるところまでを実行した。失敗後の魂出現・回収・選択画面到達は未実行。
- 点2: 終了コード1。1件実行・1件失敗。
  `Issue528AckReproTests.cs:70` で `Expected: 1 / Actual: 11`。
  最初の ACK 呼び出し後、擬似時間を1秒ずつ10回進めると10回追加された。
- 点3: 終了コード1。4つの呼び出し辺がすべて `True`。
  これは静的確認であり、実行時の回数や `TryRelease` の解放順は計測していない。

変更済みの境界スタブに対する対照として、以下も今回のセッションで実行した。
どちらも終了コード0。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~InfinityLobbyStartTests&FullyQualifiedName!~PureWhiteBossKilledWhileSaveHoldBlocksTickStillReachesSoulFinish' \
  -p:PythonCommand=python3 -p:WarningLevel=0
# 87件成功・失敗0・skip0

DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~InfinityPersonalChoiceTests|FullyQualifiedName~InfinityContinueTests|FullyQualifiedName~InfinityClearSaveTests' \
  -p:WarningLevel=0
# 11件成功・失敗0・skip0
```

## 受け付け審査の懸念と次工程への事実

この工程では修正せず、次の確認根拠を残す。

- **撃破の二重処理**: `src/SodRpg.Core/Game/InfinityRunState.cs:73` の
  `ObserveBossClear` は `BossFight` 以外なら `false`、受理時は `WaitingSoulFinish` に
  遷移する。直後に再度呼んでも進行しないことはコード上で確認した。
  `src/SodRpg.Mod/InfinityMode.cs:575` は入場後に `ObserveBossClear` を呼ばず、
  同ファイル `:618` のイベント経路だけで撃破を観測する。
  実際のディスク保存失敗や30秒待機の再現は未実行（保存待ち境界の置き換えによる検証）。
- **ACK 欠落時の停止**: `src/SodRpg.Mod/InfinityMode.cs:784` の `ReceiveAck` は
  現在の choice と RunId / Revision / GraphEpoch / Boundary が一致するものだけを受理し、
  `Acks[caller.guid]` に revision を保持する。GraphEpoch 自体は辞書に保持しない。
  `PublishChoice`（`:763`）で辞書を消去する。
  `PartyAcknowledged`（`:812`）はホスト自身の ACK がないと待機を続ける一方、
  リモート ACK 欠落には最大30秒の猶予、個人選択が時間切れなら追加待機なしの経路がある。
  このため「リモート ACK 欠落だけで必ず無期限停止する」とはコードからは言えない。
  ただし再現テストは ACK 呼び出し数の観測であり、送信抑制の安全性・損失・再接続は未検証。
  次工程で単純な return を入れる前にホスト自身の ACK 保存も含めて判断する必要がある。
- **個人選択の処理順**: `src/SodRpg.Mod/ClientSession.Infinity.cs:291` は
  保存確認 → `TickInfinityPersonalChoice` → `InfinityMode.Tick` の順。
  `ClientSession.InfinityPersonalChoices.cs:117` は個人選択を送信する前に Tick し、
  `InfinityMode.cs:519` は保留移動と `TickNative` より前に再び Tick する。
  `InfinityMode.PersonalChoices.cs:114` の `TryRelease` はこの2つの時点で評価される。
  呼び出しを削ると送信を挟む順序が変わり得る。順序変更後の解放時機は未検証のため、
  この再現工程では変更していない。

未実行: ゲーム起動、`dreamforge_simkill`、Unity の画面・魂回収・協力プレイでの実通信、
実ディスク保存失敗の注入、ACK 損失・遅延・再接続、修正後の冪等性／解放順の検証、
製品 MOD ビルド、全テストスイート。
再現用ハーネスと対象の既存テストに限定する依頼のため、実機・全体検証は行っていない。

## 実装修正と検証（2026-10-17）

今回の対象は R1〜R7。Issue 末尾の「今回の PR では触っていません」は元の時点の
説明であり、今回の二重 Tick 修正の禁止条件とは解釈しない。

- `InfinityMode.TickNative` の ExitBoss 分岐で、`TryEnterBoss` 後に
  `room.didClearRoom && state.ObserveBossClear()` を評価する。入場と取りこぼした撃破の
  保存は同じ条件分岐へまとめ、同じ Tick でその2遷移を個別に保存しない。
  既存の `OnRoomClear` が先に成功していても、`ObserveBossClear` は
  `WaitingSoulFinish` 以降では false となり、撃破の重複処理・重複保存はしない。
  魂の初観測／消失という別の遷移の保存は維持する。
- `ClientSession.TickInfinity` は、ゲストの潜行決定が適用済みで、かつ
  `state.GraphEpoch > choice.GraphEpoch` のとき、初回も含め ACK を送らず戻る。
  世代が進むまでは1秒ごとの再送を維持する。ホストのローカル ACK、帰還、グラフ境界の
  ACK も維持する。既存の `PartyAcknowledged` はリモート ACK 欠落を最大30秒で解除し、
  個人選択が期限切れなら追加待機をしない。ホスト自身の保存受領を期限で代用しない。
  新しいグラフを観測した後は、ホストが既に選択の待ちを解放しているため、古い決定を
  再送する必要はない。実通信の損失・再接続を確認したという意味ではない。
- 個人選択の毎 Tick の走査と `TryRelease` は `InfinityMode.Tick` 側に一本化する。
  `TickInfinityPersonalChoice` からは送信・受領の管理だけを行う。送信直前のハンドラー登録は
  `SubmitPersonalChoice` に残し、初回／Actor 交換時も送信できるようにする。
  解放判定は従来の2回目と同じく、送信後、`TickPendingTravel`／`TickNative` 前に行う。
  ホスト自身は個人選択の待機対象ではなく、受信 callback は同一 main-thread Tick の
  同期処理の途中に割り込まないため、先行する走査を省いても移動の解放判定は同じ位置に残る。

既存テストの最小追随:

- `Issue528AckReproTests` の初回 ACK 期待を1回から0回へ変更した。初回も送らずに return
  する R2／R6 に合わせたもので、古いグラフへの10秒間の再送なしという条件は維持する。
- `InfinityZoneTests` の純白入場2件は、戦闘前の Room を `didClearRoom = false` とする。
  従来は到着時から true だったため、修正後の最初の回帰実行ではこの2件が
  `BossFight` 期待／`WaitingSoulFinish` 実値で失敗した（86成功／2失敗、終了コード1）。
  撃破イベントを送る時点で true にすることで、通常入場と撃破後の進行を区別する。
- 新規テストは追加していない。

### 実行した検証

修正前に冒頭の3コマンドを再実行し、すべて終了コード1を確認した。点1は
`WaitingSoulFinish` 期待／`BossFight` 実値、点2は1回期待／11回実値、点3は2経路だった。
修正後、次を実行した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj \
  -c Release --filter FullyQualifiedName~InfinityLobbyStartTests -p:PythonCommand=python3 -p:WarningLevel=0
# 終了コード0、88成功／0失敗／0skip。点1、通常ボス入場、個人選択の完了／期限切れ／切断、
# 受信／欠落／遅い Hello の ACK、リモート欠落30秒解除・ホスト自身ACK必須を含む。

DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Issue528AckReproTests|FullyQualifiedName~InfinityPersonalChoiceTests|FullyQualifiedName~InfinityContinueTests|FullyQualifiedName~InfinityClearSaveTests|FullyQualifiedName~InfinitySoulPanelTests|FullyQualifiedName~PureWhiteRewardChoiceTests' \
  -p:WarningLevel=0
# 終了コード0、23成功／0失敗／0skip。

python3 tests/repro/issue528/check_double_tick.py
# 終了コード0。TickInfinityPersonalChoice からの辺が False、InfinityMode.Tick 側が True。

dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams' --nologo -v:quiet
# 終了コード0、エラー0・警告6。参照先に Mods がなく配置処理は実行されない。

git diff --check
# 終了コード0。
```

修正コミット `165fcecc` の後、製品4ファイルだけをその親の状態へ一時的に戻し、
テストの期待値は修正後のまま冒頭の3コマンドを実行した。すべて終了コード1で、点1は
`WaitingSoulFinish` 期待／`BossFight` 実値、点2は0回期待／1回実値、点3は2経路だった。
その後、製品4ファイルをコミット済みの修正へ復元して同じ3コマンドを実行し、点1は
1成功、点2は1成功、点3は1経路で、すべて終了コード0となった。
一時的に戻した変更は残っていない。

### 未実行の検証（理由付き）

- D1〜D3 のゲーム起動・純白での素早い撃破・実ディスク保存失敗／遅延・魂回収画面・
  実際の Mirror 通信の ACK 損失／遅延／再接続: 依頼で実機確認は対象外と指定。
  保存待ちの再現は境界スタブ、ACK は呼び出し回数とホスト側の既存期限テストによる。
- 同一フレームの実行時 Tick 回数の計測、実ディスク保存回数の計測: 両ハーネスで本物の
  ClientSession と InfinityMode を同時コンパイルしていない。二重 Tick は静的経路、
  保存の重複排除は状態遷移と保存呼び出し条件のコード確認による。
- 全テスト・`tools/test_changed.py --all`・`SODRPG_SLOW=1`: フル／遅いテストは依頼で禁止。
  変更箇所に関係する既存テストと製品ビルドに限定した。
