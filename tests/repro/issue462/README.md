# Issue #462 の再現・修正と #474 の追補記録

Issue #462 の修正前の再現結果と、製品コードの修正・再検証、Issue #474 の離脱時更新・検証範囲の追補を記録する。既存の native API ダブルに製品の C# ファイルをリンクしたテストで確認し、ゲーム本体の起動・描画はしていない。以下の「修正前の結果」は当時の記録であり、現在の挙動は「修正と再検証」「#474 の追補」を参照する。

## 実行

リポジトリ直下で次を実行する。

```sh
python3 tests/repro/issue462/run.py
```

スクリプトは2プロジェクトの対象テストだけを前景で順に実行する。片方が失敗してももう片方を実行し、いずれかの失敗を終了コード1として返す。コマンド、終了コード、TRXの集計、コンソール出力は `artifacts/<UTC日時>/` に保存する（git管理対象外）。

この環境には .NET 10.0.11 だけがあるため、`DOTNET_ROLL_FORWARD=Major` を未設定時に指定して net8.0 のテストを起動する。初回の既存テスト実行は .NET 8 ランタイム不在で中断した。その後は roll-forward でテスト本体の実行を確認した。

## 修正前の結果

2026-10-16（日本時間）に実行。統合スクリプトの終了コードは **1**。

| 対象 | 終了コード | 結果 |
| --- | --- | --- |
| `Issue462ReproductionTests` | 1 | 7件中4件成功・3件失敗。ロビーだけにいる参加者の版／Protocol／内容の差が表示一覧から抜ける |
| `RoomOmensHostTests` | 0 | 既存7件と追加の3ゾーン検証1件、計8件成功 |
| 既存 `NegotiationNativeTests` / `LevelEssenceSlotsTests` | 0 | 別途実行した33件が成功 |

### 1. 部屋の兆し: 通常のゾーン移動では未再現

`HostAuthority.RoomOmens.cs` は `RoomOmens.Roll(Rng.SeedFrom(run.RunId), currentRoomIndex)` を使用している。遠征の種は既に含まれる。

本体の逆コンパイル物 `<ゲーム本体の逆コンパイル先>/Dew.Core/ZoneManager.cs`（リポジトリ外の参照資料）を確認した。

- `LoadNode` の1125〜1130行: `isTravelingZone || isTravelingRoom` かつ `advanceTurn` なら `currentRoomIndex` を加算する。
- 1299〜1316行: 部屋番号を0にするのは、保存読込ではなく `currentZone == null` の新ループ開始時。通常のゾーン移動ではゾーン番号だけを加算し、部屋番号は0に戻さない。
- MODの `InfinityMode` による移動も調べ、部屋番号をゾーンごとに0へ戻す処理は見つからなかった。

追加テスト `RoomOmensHostTests.Issue462_ordinary_zone_travel_keeps_the_run_wide_room_counter_and_does_not_repeat_the_sequence` はこの部屋番号の進み方をダブルに与え、3ゾーン各6戦闘部屋で製品の抽選・適用処理を呼ぶ。3ゾーンで並びの反復は出なかった。**本体の `LoadNode` 自体を実行するテストではない**。同じ遠征IDと同じ部屋番号を人為的に与えれば同じ結果になることだけでは、通常移動時のバグとは確定しない。

### 2. ロビーの版違い一覧: 再現成功

`Issue462ReproductionTests.Lobby_only_peer_with_a_mismatch_is_visible_in_host_warnings` の3ケースが失敗する。

1. 人間の参加者を `lobbyPlayers` のみに追加する。
2. 版、Protocol、内容のいずれか1項目だけが異なる Hello を、登録済みの製品ハンドラーへ渡す。
3. Helloの返信が1件届き、製品の `_versionMismatches` に対象の警告が1件格納されたことを確認する。
4. UIが読む `HostAuthority.VersionWarnings` に1件あるべきところ、0件になる。

共通の失敗:

```text
difference=version/protocol/content, lobby member=true, game member=false, stored warnings=1, visible warnings=0
Assert.Single() Failure: The collection was empty
```

修正前の `HostAuthority.Hello.cs` の `RebuildMismatchList` は `gamePlayers` にいる参加者だけを表示対象にしていた。`OnHello` 自体は `lobbyPlayers` も受け付ける。`DreamforgeUi.cs:457` はその一覧を表示するため、製品の表示データまで欠落が確認できる。

一致するロビー参加者に警告が出ないケースと、両名簿から抜けた人を一覧再構築時に除外するケースは成功した。後者は一覧再構築を明示的に呼ぶテストであり、離脱イベントからの自動更新は検証していない。

### 3. エッセンス枠: 一時的な4枠は確認、固定化は未再現

仕様 `docs/specs/v2.11-level-essence-slots.md` はQWERのHUD準備が揃うまで実効capを4と定めている。

`LevelEssenceRuntime.Configure` / `Tick` は準備中のcapを4にし、準備済みなら8へ戻す。`DreamforgeMod.Update` は `LevelEssenceRuntime.Tick()` の後にホストを更新し、`HostAuthority` の更新工程には1秒間隔の `TickLevelGemSlots` が含まれる。従って再評価の経路は既にある。

追加テストは製品の `TickLevelGemSlots` を呼び、Lv20・要求cap5とLv50・要求cap8について、準備中の4枠から準備後の次回更新で5枠／8枠になることを確認した。**HUD準備を表す `RuntimeCapLimit` の4→8だけをテストで与えている**。Unityの `EnsureHud`、Prefab複製、画面描画や実際のフレームの順序を実行したわけではないため、HUD準備が実機で成功するかは未解決。

## 判断したこと

- 当初の再現工程では受け付け審査の3懸念を調査した。その後、下記の修正工程でロビー警告の表示条件を修正し、#474 で名簿変更時の自動更新を補った。
- 兆しはゾーン内のノード番号と遠征通算の `currentRoomIndex` を区別した。本体の通常移動で番号が重複する前提が確認できないため、ゾーンの種を足す修正は推測しない。
- HUD準備中の4枠は既存仕様に従う。後で再計算されずに不足が残るかを切り分け、ホスト側の固定化は再現しなかったと判断した。実機未確認を理由に工程全体は止めない。

## 未実行と次の実機確認

ゲーム環境を起動できないため、以下は未実行。

1. **兆しと部屋番号（R7）**: 同じ遠征で3ゾーン以上を進み、ログで遠征ID・ゾーン番号・通算 `currentRoomIndex`・兆しを突き合わせる。ゾーン内のノード番号と通算番号を区別し、通常移動では通算番号が継続して増えるというテストの解釈を確認する。インフィニティのループ切替は別に記録する。現行ログに番号が揃わなければデバッガー等の一時観測を併用し、記録が揃うまでは未確認とする。
2. **警告と離脱通知（R9）**: 異なる版の参加者をロビーに迎え、ホスト画面の警告を確認する。一致版への交代、ロビー退出、ロビー→ゲーム移行、ゲーム退出をそれぞれ行い、`DewPlayer` の名簿削除／追加通知と `RebuildMismatchList` の呼び出し時機を突き合わせる。完全離脱で警告が消え、ゲーム名簿へ移った参加者の警告は追加通知で再表示されること、新しいHelloを待たないことを確認する。必要なら一時ブレークポイントで観測する。
3. **HUD準備の成功・失敗・長期停滞（R8 / R10）**: cap5・Lv20とcap8・Lv50の新規読込／Continueを確認する。準備成功時はQWERの5〜8枠目の保持・編集を確かめる。準備失敗・長期停滞時も別ケースで、準備状態、エラー、`RuntimeCapLimit`、各キーの実枠数、経過時間を記録し、1秒程度の更新間隔を複数回越えて4枠のまま止まるか、後で準備が成功したときに5／8枠へ回復するかを観測する。`Level_slots_recover` は `RuntimeCapLimit` を人為的に4→8へ切り替えているため、準備が完了しない状況での回復を証明しない。準備未完了中のcap4は既存仕様であり、この観測は実機で準備が永久に完了しない問題を切り分けるために残す。

`dreamforge_simkill` はゲーム内実行環境がないため未実行。全テストスイートと製品MOD全体のビルドは、再現工程の対象外のため未実行。

## 修正と再検証（2026-10-16 日本時間）

`HostAuthority.Hello.cs` の `RebuildMismatchList` を、`gamePlayers` または `lobbyPlayers` にいる参加者を表示する条件へ変更した。辞書を1回走査するため、両方の名簿にいる参加者も警告は1件となる。nullと、両方の名簿から抜けた参加者の除外条件は維持する。Helloの受付条件と表示条件を揃える修正で、版の差があっても機能を続行する既存の動作は変えない。

### 実行した検証

同じ `python3 tests/repro/issue462/run.py` を、この実装工程で以下の順に前景実行した。各記録は `artifacts/` 以下のログ・TRX・`results.json`（git対象外）にある。

| 状態 | 記録ディレクトリ | 終了コード | Core / 兆し |
| --- | --- | --- | --- |
| 修正前 | `20261015T182358512648Z` | 1 | 4成功・ロビー警告3失敗 / 8成功 |
| 修正後 | `20261015T182648680362Z` | 0 | 7成功 / 8成功 |
| 修正を一時的に戻した状態 | `20261015T182812488911Z` | 1 | 4成功・同じロビー警告3失敗 / 8成功 |
| 修正を再適用した状態 | `20261015T182913880053Z` | 0 | 7成功 / 8成功 |

修正を一時的に戻すと再現テストが失敗し、再適用すると成功することを確認した。

追加で次の既存テストを実行し、終了コード0・43件成功・失敗0・skip0を確認した。#462 全体では `Issue462ReproductionTests` と `RoomOmensHostTests` に再現・検証テストを追加している。この43件の実行時点では追加変更していない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --filter 'FullyQualifiedName~NegotiationNativeTests|FullyQualifiedName~LevelEssenceSlotsTests|FullyQualifiedName~RoomOmensTests' --logger 'console;verbosity=minimal' -p:PythonCommand=python3 -p:WarningLevel=0
```

### 判断と要件の対応

- **R1 / R5（兆し）**: 本体の `ZoneManager.LoadNode` の1125〜1130行と1299〜1316行を再確認し、通常ゾーン移動で通算部屋番号が増えることを確認した。既存の3ゾーン各6部屋のホストテストが成功し、通常移動で並びが毎回同じになる現象は未再現。既存の抽選を維持する。ゾーン内番号を与えていたと仮定して抽選の種を変更すると、既存の決定性も変わるため採用しない。
- **R2 / R6（ロビー警告）**: 版・Protocol・内容の差について、ロビーだけの参加者がHelloで受理され返信を受けた後に、UIが読む一覧へ警告が出るところまで既存の再現テストで確認した。
- **R3（離脱者）**: 両名簿からの離脱後、一覧を再構築すると警告が消える既存テストが引き続き成功した。離脱イベントから自動で再構築するかは、このテストでは検証していない。
- **R4 / R7（レベル枠）**: HUD準備中の実効cap4は `docs/specs/v2.11-level-essence-slots.md` の既存仕様。`DreamforgeMod.Update` が `LevelEssenceRuntime.Tick` を先に呼び、その後ホスト側で定期再評価する経路を再確認した。既存テストで準備完了を表すcap8への変更後、次の評価でLv20・要求cap5は5枠、Lv50・要求cap8は8枠へ回復した。ホスト側に枠不足が固定化する現象は未再現のため、既存仕様を維持する。

### 未実行の検証（理由付き）

- UnityのHUD準備・描画、ロビーの実画面、実協力通信、実際の複数ゾーン移動・Infinityループ・新規読込／Continue: ゲームを起動する実行環境がない。テストはリンクした製品コードと本体APIのダブルを使い、HUD準備の完了信号を与えている。実機での表示・準備成功は証明していない。
- `dreamforge_simkill`: ゲーム内実行環境がない。
- 全テストスイート、`tools/test_changed.py --all`、`SODRPG_SLOW=1`: この工程は変更箇所に絞った検証とし、全体・長時間検証は後続パイプラインで実行する指示のため。
- 製品MOD全体のReleaseビルド: #462 では対象テストプロジェクトのビルドで検証した。#474 での全体ビルド結果は次節を参照。

## #474 の追補（2026-10-16 日本時間）

### 名簿変更時の警告更新

コード上では `RebuildMismatchList` はHello受信時とハンドラー解除時にしか呼ばれておらず、ロビー退出だけでは警告が残った。本体の参照資料 `<ゲーム本体の逆コンパイル先>/Dew.Core/DewPlayer.cs` の `OnStateChanged` は、旧名簿から削除して削除通知を出し、その後で新名簿へ追加して追加通知を出す。ロビー→ゲーム移行の削除通知時点では、一時的に両名簿にいない。

`HostAuthority.Hello.cs` の `RegisterHello` で `onLobbyPlayerAdded` / `onLobbyPlayerRemoved` / `onGamePlayerAdded` / `onGamePlayerRemoved` を購読し、通知で一覧を再構築する。`UnregisterHello` で4購読を解除する。完全離脱では警告を取り除き、ゲームへ移った参加者は追加通知で再表示する。シーン移行でtransportが置き換わる場合は、従来どおり解除時にHelloの状態を消し、新transportのHelloで再取得する。毎フレームの走査は追加しない。

既存の `Departed_peer_is_excluded_when_the_warning_list_is_rebuilt` は直接のprivateメソッド呼出しから、本体と同じ名簿更新→通知の順へ最小修正した。ロビー離脱、Helloなしのゲーム名簿への追加、ゲーム離脱を確認する。テスト数は増やしていない。実ゲームのイベント配信・画面更新の時機は上の実機確認に残す。

### 直列化と兆しの前提

`Issue462ReproductionTests` / `NegotiationNativeTests` / `LevelEssenceSlotsTests` に同じ `Native negotiation and level slots` Collectionを付け、定義でも並列実行を無効にした。既存のアセンブリ全体の直列設定も維持する。

`RoomOmensHostTests` の既存3ゾーンテストに、ゾーン内の番号1〜6を毎ゾーン与えると同じ遠征の種から同じ6個の並びが繰り返されることをコメントで記録した。`currentZoneIndex` は抽選入力に含まれない。`RoomOmensTests.Roll_is_deterministic_for_the_same_seed_and_room` が既に同じ種・部屋番号の再抽選一致を検証している。新規テストを作成しない作業指示に従い、新しいテスト・ケースは追加しない。通常の通算番号を使う既存ホストテストと組み合わせて前提を記録し、実機の番号進行は未確認として扱う。

### 実行した検証

すべてこの工程で前景実行した。ログ・TRXは `artifacts/`（git対象外）に保存している。

| 検証 | 結果 |
| --- | --- |
| 変更前の `python3 tests/repro/issue462/run.py` | 終了コード0、Core 7件・兆し8件成功。旧離脱テストは再構築の直接呼出しのため問題を検出しない |
| 通知経由へ修正した既存離脱テスト（製品修正前） | 終了コード1、1件失敗。ロビー名簿削除通知後も警告が残ることを再現（`issue474-before/departure.trx`） |
| 修正後の `python3 tests/repro/issue462/run.py` | 終了コード0、Core 7件・兆し8件成功（`20261015T221133550420Z`） |
| 購読処理を一時的に取り消し、同じ離脱テストを実行 | 終了コード1、同じ `Assert.Empty` で1件失敗（`issue474-reverted/departure.trx`） |
| 修正を再適用し、関連Core 4クラスを実行 | 終了コード0、50件成功・失敗0・skip0（`issue474-restored/related-core.trx`）。修正後の先行実行も50件成功 |
| 起動側の `InfinityLobbyStartTests` を実行 | 終了コード0、87件成功・失敗0・skip0（`issue474-startup/lobby.trx`） |
| 本体DLLを参照した製品MODのReleaseビルド | 終了コード0、エラー0・警告6。配置先は作業ツリー内の未作成ダミー `GameDir` とし、ゲームへの配置は行わない |

関連テストとビルドのコマンド（`<ゲームのManaged>` は本体DLLのあるディレクトリ）:

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --filter 'FullyQualifiedName~Issue462ReproductionTests|FullyQualifiedName~NegotiationNativeTests|FullyQualifiedName~LevelEssenceSlotsTests|FullyQualifiedName~RoomOmensTests' --logger 'console;verbosity=minimal' -p:PythonCommand=python3 -p:WarningLevel=0
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj --filter 'FullyQualifiedName~InfinityLobbyStartTests' --logger 'console;verbosity=minimal' -p:PythonCommand=python3 -p:WarningLevel=0
dotnet build src/SodRpg.Mod -c Release -p:GameManagedDir="<ゲームのManaged>" -p:GameDir="<作業ツリー>/tests/repro/issue462/artifacts/issue474-build"
```

### 未実行の検証（理由付き）

- 上記の実機確認3項目、実協力通信・描画、`dreamforge_simkill`: ゲーム実行環境がないため未実行。HUD準備失敗・長期停滞時に4枠のまま止まるかは未解決であり、自動テストの成功で実機確認済みとはしない。
- 全テストスイート、`tools/test_changed.py --all`、`SODRPG_SLOW=1`: 作業指示により変更関連のテストに限定し、全体・長時間検証は後続パイプラインへ委ねる。
