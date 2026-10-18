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
  確認待ちを 30 秒の待ち解除（擬似時間）で通して同世代で最初の ACK を成立させた後、地図を
  次の世代へ進め、適用済みで世代が古い choice（`state.GraphEpoch > choice.GraphEpoch`）への
  追加 ACK がないことを期待する。修正工程で、初回 ACK を確立する時点を同世代へ調整した。
  ACK の観測は境界の `InfinityMode.AcknowledgeLocal` を呼び出しカウンターに変えた観測点で
  行い、実際の Mirror RPC の送受信・取りこぼしは再現しない。
- **点3**（`check_double_tick.py`）: `ClientSession.TickInfinity` → `TickInfinityPersonalChoice` →
  `TickPersonalChoices` と `TickInfinity` → `InfinityMode.Tick` → `TickPersonalChoices` の
  2経路を、実ソースのメソッド本体から確認する。両プロジェクトのどちらも
  `TickInfinity` と本物の `InfinityMode.Tick` を同時にコンパイルしていないため、
  実行時の呼び出し回数の計測はこのハーネスでは行えない（静的構造の確認にとどめる）。

## 修正前の結果（2026-10-17 再実行）

実行環境: .NET SDK 10.0.111、net8.0 を Major roll-forward で実行。
`NUGET_PACKAGES` は worktree 外のキャッシュを指定した。再実行時も呼び出し元の設定を使う。

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

## 修正工程の結果（2026-10-17）

前節までの修正前の結果・未実行事項は再現工程の記録。以下は修正工程で実行した内容。

- `InfinityMode.TickNative` の ExitBoss 到着処理で `TryEnterBoss` 後に `room.didClearRoom` を
  再確認し、`ObserveBossClear` まで進めた相位を保存する。保存待ち・遷移・部屋非アクティブで
  callback を処理できなかった場合も、そのガードが解除された後の Tick で回収する。
  `ObserveBossClear` の BossFight 限定条件は維持するので、callback と Tick が重なっても
  WaitingSoulFinish／AwaitingChoice から先へ二重に進めない。
- 潜行適用済みかつ `state.GraphEpoch > choice.GraphEpoch` なら保存 ACK 処理前に戻る。
  `ClientSession.TickInfinity` の先頭では `CompleteInfinityStateSave()`、
  `TickInfinityPersonalChoice()`、`InfinityMode.Tick()` をこの順に呼ぶ。
  この早期 return は3つの呼び出しより後ろにあるため、個人選択の送信や定期 Tick を飛ばさない。
  この順序は製品ソースで確認した。Native ハーネスの `InfinityMode.Tick` は空実装なので、
  ACK テストの合格だけではこの点を検証できない。
  同世代での1秒ごとの再送と、ホストの `PartyAcknowledged` による全員受信／最大30秒の
  既存待ち解除は維持する。ホストも適用済みの古い決定に対する毎フレームの全員走査を省ける。
- `TickInfinityPersonalChoice` からの `TickPersonalChoices` 呼び出しを除き、定期経路は
  `InfinityMode.Tick` の1本にした。ローカル選択送信の後、移動・部屋進行の前に解放判定する
  順序を維持する。`SubmitPersonalChoice` 自身が通信登録を確かめるので、定期 Tick より先に
  完了操作が来ても初回送信・ACK 受信の登録を失わない。
- ACK 再現ケースは初回 ACK を同世代で成立させ、その後 GraphEpoch を進める最小調整をした。
  既存の純白入場2ケースは入場時を未クリアにそろえ、撃破時に初めてクリア済みにする。
  新規テストは追加していない。

### 実行した検証

- 冒頭の再現3コマンド: 修正前はすべて終了コード1。修正後はすべて終了コード0。
  純白ケースは WaitingSoulFinish から魂の出現・回収を経て AwaitingChoice まで成功。
- **一時差し戻し**: 本体の修正4ファイルだけを修正前へ一時的に戻し、既存テストの調整を
  残したまま冒頭の3コマンドを再実行。すべて終了コード1（BossFight のまま／ACK 計11回／
  二重経路）で失敗した。本体修正を復元し、同じ3コマンドがすべて終了コード0で成功した。
- Startup の `FullyQualifiedName~InfinityLobbyStartTests`: 終了コード0、88件成功。
  `-c Release --no-restore -p:PythonCommand=python3 -p:WarningLevel=0` で実行。
  全員完了・60秒期限・切断時の個人選択解放、ACK 未着の30秒待ち解除とホスト自身の保存保護を含む。
- Native の `FullyQualifiedName~InfinityPersonalChoiceTests|FullyQualifiedName~InfinityContinueTests|FullyQualifiedName~InfinityClearSaveTests|FullyQualifiedName~Issue528AckReproTests|FullyQualifiedName~PureWhiteRewardChoiceTests`:
  終了コード0、20件成功。`-c Release --no-restore -p:WarningLevel=0` で実行。
  保存失敗・遅延・保存先なしの待ち解除、Continue 復元、個人選択の一度だけの適用を含む。
- Core の `FullyQualifiedName~InfinityPersonalChoiceTests`: 終了コード0、1件成功。
  `-c Release -p:WarningLevel=0` で実行。上記 .NET テストはすべて `DOTNET_ROLL_FORWARD=Major`。
- `dotnet build src/SodRpg.Mod -c Release -p:GameDir="<参照用ゲームDLLのフォルダ>" --nologo -v:quiet`:
  終了コード0、エラー0・既存警告6。参照先に Mods がないことを確認してから実行し、配置なし。
- `git diff --check`: 終了コード0。

### 未実行の検証（理由付き）

- 実ゲームの起動・協力通信での保存遅延／RPC 欠落・実画面: R4／D1／D2 は対象外。
  R2 は保存待ち解除後の本体リンク再現と既存の保存待ちテストによる検証であり、
  実機での遅延・部屋非アクティブ／遷移中の撃破を個別に観察したものではない。
- 全テスト・`tools/test_changed.py --all`・遅いテスト: 依頼に従い後続パイプラインへ委ねる。
  変更した Infinity の関連テストだけを実行した。
- バランス生成鮮度検証: バランス原本・生成物を変更していないため未実行。
