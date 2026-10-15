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
