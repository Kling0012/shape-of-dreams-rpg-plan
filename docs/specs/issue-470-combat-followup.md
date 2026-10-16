# #470 戦闘処理の追補

## 判断と対応

- `LivingPartySize` の読み取りは `PowerRuntime.NewPowers.cs` の `ShieldGainAmplification`（前衛の誓い / `VanguardsOath`）と `OutgoingDamageAmplification`（後衛の道 / `RearguardsWay`）のみ。`NearbyEnemies8` の読み取りは `OnNewBasicHit` の決闘者の道 / `DuelistsWay` のみ。表示側や別の能力での使用はない。`ScanNewPowerFacts` と `OnNewPowerDamage` の走査条件を維持し、利用能力と将来の追加時に条件を広げる必要をコードへ注記した。見守りの手 / `WatchfulHand` は味方の列挙を使うが、人数の値は使わない。
- `_abnormalCount` は弱キーの GC 回収で減らない保守的なカウンターであることを注記した。過大値は `TryGetValue` の省略を妨げるだけで、状態の誤判定を起こさない。定期走査・追加の強参照・毎フレームの確保を導入しない。
- `CanUpdate` 全体を確認した。`life.CleanupRequested` / `life.Faulted` は `TryGetValue` 成功時だけ読み、正常復帰時は既存の `life != null` ガードがある。無効 victim の後半は `life` を参照せず、`Retire` が `AbnormalLifetime` から自分で取得する。このため未初期化の `life` による NullReferenceException の経路はなく、該当箇所へその前提を注記した。
- 数つき報告は送信時だけ元素カウンターの配列をコピーする。`finally` は元のバッファを消すため、送信側がメッセージを保持してもその配列は消えない。対象・transport・遠征が不一致で破棄する場合、既存 `Log.Info`（Unity `Debug.Log`）で理由を1行記録する。通常の空カウンターでは記録しない。
- 境界の Harmony 対象を `WrapUp(DewGameResult.ResultType)`、`SerializeGameData(LoadNodeSettings)`、`RestartSession()`、`StartPrimusDeath()` に限定した。本体資料と `Dew.Contents.dll` の逆コンパイルで引数を確認した。
- `Configure`、`Tick(force: true)`、ホストから受けた表示設定の変更とローカル設定への復帰で `_hudScanPending` を立てる。全対象の準備済みでも要求された再検索は行う。通常 tick ごとの全検索は再導入せず、既存の弱キーで同じ HUD の拡張を繰り返さない。受信した設定変更は次の通常 tick で検索される。
- 数つき受信登録の解除直前に、接続中なら同じ Actor から既存の capability 通知で `bountyCounts=0` を送る。他の capability 欄は現在値を保持する。送信失敗は警告して受信登録の解除を続行する。共通 Protocol・Hello・保存形式は変更しない。

## ActorManager の照合と実機確認項目（R10、未実施）

2026-10-16、ゲーム参照資料 r.1.4.0.13 の `Dew.Core/ActorManager.cs` と `Actor.cs` を静的に確認した。

- 本体 `AddActor` は `allActors.Add` の直後に `ClientEvent_OnActorAdd` を呼び、その後で Entity / Hero の個別集合を更新する。`RemoveActor` は `allActors.Remove` が成功した場合だけ `ClientEvent_OnActorRemove` を呼ぶ。テスト用 `StartupGameApi.cs` の2つのメソッドは、この Actor 集合とイベントの順序に一致する。
- 本体は `Actor.InvokeOnCreateIfDidnt` の `OnCreate` 完了後に追加し、`OnDestroyActor` と `OnDisable` で除去する。イベント名に Client が含まれるが、Actor 集合更新の上記メソッド自体にはサーバー／クライアントによる除外がない。実通信でその呼び出しに至る順序までは、APIダブルのテストで証明できない。

実機ではホストと参加者の両方について、魂の祠の通常 spawn・遅延 spawn・Continue の既存オブジェクト・破棄・部屋移動・pool 再利用を確認する。各時点の `isActive`、`allActors` への追加／除去、Actor イベント、本機能の初回取り込み／イベント購読の順序と回数を記録する。追加通知時には既に Actor 集合に含まれ、除去通知時には含まれず、同じ除去で通知が重複しないこと、祠の検出漏れと消失後の残留がないことを確認する。ゲーム版・MODのコミット・ホスト／参加者・操作と観測順を記録する。ゲーム操作環境がないため、本作業では未実施。

## 実行した検証

2026-10-16、Linux / .NET SDK 10.0.111（net8 テストは `DOTNET_ROLL_FORWARD=LatestMajor`）で実施。

- Core：既存 `NewPowersV129Tests`、`NewPowersV123Tests`、`RunContentV129Tests`、`BountyTests`（フィルターで `SigilBountyV113Tests` も含む）、`LevelEssenceSlotsTests`、`MpHostFixesV131Tests`。226件成功、失敗0。
- Startup：既存 `InfinityZoneTests`、`InfinityLobbyStartTests`、`LevelEssenceConfigTests`、`ReloadStartupTests`。91件成功、失敗0。
- Issue73.Native：既存 `RoomOmensHostTests`、`NativeAcceptanceTests`、`ContinueSaveTests`、`LobbyReturnTests`。57件成功、失敗0。
- 本体 DLL 参照の MOD Release ビルド：エラー0、警告6。`GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams`、`ModDeployDir` はこの worktree の `artifacts/issue470-mod` を明示し、ゲームへ配置しない。
- `git diff --check`：成功。
- 修正を一時的に戻し、同じ3つの既存テスト選択を再実行した。修正前も226／91／57件すべて成功（各 exit 0）し、本件の不具合を検出できないことを確認した。4つのソースは `finally` で修正後へ復元し、ビルドと同じ3選択を再実行してエラー0／226・91・57件成功を再確認した。したがって「修正前に失敗・修正後に成功」という再現検証は成立していない。比較ログは worktree の `src/SodRpg.Mod/obj/issue470-artifacts/issue470-revert-check/` に保持し、コミットには含めない。

## 未実行の検証（理由付き）

- ゲーム内での配列遅延参照、協力通信での capability 解除、Harmony の実ゲーム内パッチ適用、HUD の描画・設定切替・再生成、上記 ActorManager のホスト／参加者イベント観測：ゲームを操作できる環境がないため未実施。
- 新規テストは禁止のため追加していない。既存テストは今回の `BountyReportBatching`、`NativeActorLifetime`、`LevelEssenceRuntime` を直接リンクしておらず、これらの不具合を修正前に失敗させる再現テストはブランチにない。既存テストの成功は本件の実機再現・修正の実証ではない。
- フルスイート・Slow テスト：実装工程の指定に従い後続パイプラインへ残す。`tools/test_changed.py --list` はコメントだけの変更を理由に全体実行へ拡大するため、上記の明示フィルターを使用した。
