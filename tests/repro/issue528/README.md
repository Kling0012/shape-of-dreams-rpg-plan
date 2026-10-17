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
  `WaitingSoulFinish` に到達することを期待する。保存待ちの開始・解除はスタブの真偽値で
  代用し、実際の保存失敗・30秒タイマー・実機の保存遅延そのものは再現しない。
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

## 修正前の結果（2026-10-17 再実行）

実行環境: .NET SDK 10.0.111、net8.0 を Major roll-forward で実行、
`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`。

| 点 | 修正前の観測 | 終了コード |
| --- | --- | --- |
| 1 | 保存待ち解除後の Tick で `BossFight` のまま（期待 `WaitingSoulFinish`）。49行目で失敗し、魂の回収を確かめる後続アサートは未到達 | 1 |
| 2 | 最初の ACK 後、10秒で10回の追加 ACK（計11回、期待は最初の1回のみ） | 1 |
| 3 | TickPersonalChoices への経路が2本とも存在 | 1 |

対照として、変更した境界スタブ（保存待ちの書き換え・ACK 観測点）を使う既存テストは
`InfinityLobbyStartTests` 87件合格・`InfinityPersonalChoiceTests`/`InfinityContinueTests`/
`InfinityClearSaveTests` 11件合格（失敗は上記の再現テストのみ）。2026-10-17 に両方の
絞り込みを再実行して同じ件数の合格を確認した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~InfinityLobbyStartTests&FullyQualifiedName!~PureWhiteBossKilledWhileSaveHoldBlocksTickStillReachesSoulFinish' \
  -p:PythonCommand=python3 -p:WarningLevel=0
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~InfinityPersonalChoiceTests|FullyQualifiedName~InfinityContinueTests|FullyQualifiedName~InfinityClearSaveTests' \
  -p:WarningLevel=0
```

## 受け付け審査の懸念と次工程への事実

- **撃破取りこぼしと冪等性**: `InfinityMode.cs:560` の保存待ちガードにより、`BossDue` のまま
  `OnRoomClear`（同 `:616`）に入る条件を再現した。`InfinityRunState.cs:73` の
  `ObserveBossClear` は `BossFight` の場合だけ `WaitingSoulFinish` に遷移し、それ以外は
  `false` を返す。したがって同じ状態で繰り返しても二重に進まない。この工程では変更していない。
- **ACK 取りこぼし**: `InfinityMode.cs:812` の `PartyAcknowledged` は各プレイヤーを確認する。
  GraphEpoch が進んだだけで ACK 待ちを省略するガードはない。一方、リモートの未受信は
  `:839` 以降の待ち解除条件に従い、通常は最大30秒で通過する。ホスト自身の未保存・未 ACK は
  `:835` で引き続き待つ。今回合格した既存 `V272ChoiceAckTests.cs:14` の3ケースに、
  29.99秒では待ち、30秒で通過して GraphEpoch が進む確認が含まれる。
  `ClientSession.Infinity.cs:407` では GraphEpoch 更新後も `PartyAcknowledged` を先に評価し、
  再生成は `:415` の `Transitioning` 判定に依存する。点2の再現テストは送信頻度の観測に限り、
  送信抑制を導入した後の進行保証にはならない。修正工程で同世代の再送維持を含め検証する必要がある。
- **二重 Tick の順序**: `ClientSession.Infinity.cs:291` は保存完了処理、個人選択処理、
  `InfinityMode.Tick` の順。最初の `TickPersonalChoices` は
  `ClientSession.InfinityPersonalChoices.cs:117` にあり、その後 `:126` で選択を送信する。
  2回目は `InfinityMode.cs:519` で、保留中の移動・部屋進行の処理より前に実行する。
  `InfinityMode.PersonalChoices.cs:114` の `TryRelease` は選択送信の前後で呼ばれるため、
  単純な削除による同フレームの解放順への影響は静的な重複確認だけでは検証できない。
  既存の個人選択・期限・切断テストは合格したが、呼び出し削減後の順序は未検証。

## 未実行

- ゲーム起動・`dreamforge_simkill`・実ネットワークでの協力プレイ: この工程はリンクした本体コードと
  境界スタブによる再現であり、実機の保存遅延・RPC 欠落・画面表示は確認していない。
- 製品 MOD の DLL ビルド・全テスト: 本体を変更していないため、関連する2プロジェクトの
  ビルドと上記の絞り込みだけを実行した。
- 修正後の再現テスト・ACK 抑制時の進行・二重 Tick 削減後の順序: 本工程は修正禁止のため次工程に残す。

## 判断したこと

- この工程はバグ再現に限定し、修正は次工程に残す。
- 点1の期待値は Issue の修正案どおり「TickNative の ExitBoss 分岐で TryEnterBoss 後に
  `room.didClearRoom && ObserveBossClear()` を観測する」とした。onRoomClear との二重処理に
  ならないことは `ObserveBossClear` が `BossFight` 相位でのみ受理する冪等な遷移であることから
  確認済み（`InfinityRunState.ObserveBossClear`）。
- 点2の期待値は「最初の ACK 確立以降の毎秒再送がないこと」とし、初期 ACK そのものの要否は
  修正の設計に委ねた（Issue の案では適用済みかつ世代が古いとき初期 ACK も送らない）。
