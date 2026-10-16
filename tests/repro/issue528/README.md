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
| 1 | 保存待ち解除後も `BossFight` のまま（期待 `WaitingSoulFinish`）。以降の Tick でも進行しない | 1 |
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
