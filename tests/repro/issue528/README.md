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
