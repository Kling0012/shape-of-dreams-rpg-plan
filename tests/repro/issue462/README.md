# Issue #462 の修正前再現記録

対象: `ai/issue-462`。製品コードは変更せず、既存の native API ダブルに製品の C# ファイルをリンクしたテストで確認した。ゲーム本体の起動・描画はしていない。

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

本体の逆コンパイル物 `~/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/ZoneManager.cs` を確認した。

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

`HostAuthority.Hello.cs:194` の `RebuildMismatchList` は `gamePlayers` にいる参加者だけを表示対象にする。`OnHello` 自体は `lobbyPlayers` も受け付ける。`DreamforgeUi.cs:457` はその一覧を表示するため、製品の表示データまで欠落が確認できる。

一致するロビー参加者に警告が出ないケースと、両名簿から抜けた人を一覧再構築時に除外するケースは成功した。後者は一覧再構築を明示的に呼ぶテストであり、離脱イベントからの自動更新は検証していない。

### 3. エッセンス枠: 一時的な4枠は確認、固定化は未再現

仕様 `docs/specs/v2.11-level-essence-slots.md` はQWERのHUD準備が揃うまで実効capを4と定めている。

`LevelEssenceRuntime.Configure` / `Tick` は準備中のcapを4にし、準備済みなら8へ戻す。`DreamforgeMod.Update` は `LevelEssenceRuntime.Tick()` の後にホストを更新し、`HostAuthority` の更新工程には1秒間隔の `TickLevelGemSlots` が含まれる。従って再評価の経路は既にある。

追加テストは製品の `TickLevelGemSlots` を呼び、Lv20・要求cap5とLv50・要求cap8について、準備中の4枠から準備後の次回更新で5枠／8枠になることを確認した。**HUD準備を表す `RuntimeCapLimit` の4→8だけをテストで与えている**。Unityの `EnsureHud`、Prefab複製、画面描画や実際のフレームの順序を実行したわけではないため、HUD準備が実機で成功するかは未解決。

## 判断したこと

- この工程は再現だけとし、受け付け審査の3懸念はいずれも調査した。製品コードの修正は次工程へ引き継ぐ。
- 兆しはゾーン内のノード番号と遠征通算の `currentRoomIndex` を区別した。本体の通常移動で番号が重複する前提が確認できないため、ゾーンの種を足す修正は推測しない。
- HUD準備中の4枠は既存仕様に従う。後で再計算されずに不足が残るかを切り分け、ホスト側の固定化は再現しなかったと判断した。実機未確認を理由に工程全体は止めない。

## 未実行と次の実機確認

ゲーム環境を起動できないため、以下は未実行。

1. 同じ遠征で複数ゾーンを進み、ログのゾーン番号・通算部屋番号と兆しの並びを比較する。特にインフィニティのループ切替も別途確認する。
2. 異なる版の参加者をロビーに迎え、ホスト画面に警告が出ることを確認する。その後の一致版への交代・離脱で警告が消えることも確認する。
3. capを5または8に設定し、該当レベルの新規読込／ContinueでHUD準備後のQWER枠数、5〜8番目のエッセンスの保持・編集を確認する。準備前後のログと1秒程度経過後の枠数を比較する。

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

追加で次の既存テストを実行し、終了コード0・43件成功・失敗0・skip0を確認した。新規テストの追加・既存テストの変更はしていない。

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
- 製品MOD全体のReleaseビルド: 今回は変更した製品ファイルをリンクする対象テストプロジェクトのビルドで検証し、全体ビルドは後続パイプラインへ委ねる。
