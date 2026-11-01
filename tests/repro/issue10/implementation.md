# Issue #10 実装工程の記録

## 今回修正した経路

開始時点には、ボスへの移動要求時には `BossDue` を保ち、到着後に `BossFight` を確定する前回の修正が含まれていた。今回はこの修正を維持し、ボス撃破後に進行できなくなる別の経路を修正した。

ソロのリンボ6・Infinity ONで、ボス到着や撃破の保存が遅れると、`ClientSession.HostInfinitySaveHoldSatisfied` がfalseになる。修正前の `InfinityMode.TickNative` は、この間ボスの到着・撃破・魂の出現と消滅を観測しなかった。本体の魂の選択処理はMODの保存待ちでは止まらない。そのため、保存待ち中に魂が出現して回収されると、待機解除後に `WaitingSoulFinish` へ進んでも `SoulObserved` がfalseのままになる。

`src/SodRpg.Core/Game/InfinityRunState.cs` の `ObserveSoul` は、魂の出現を一度観測したこと、魂が消えたこと、部屋の実クリア、出口の解錠を条件にする。観測を逃した魂は戻らず、`AwaitingChoice` へ進めない。`src/SodRpg.Mod/InfinityTravelRequest.cs` の `IsTravelRequestValid` はこのフェーズの通常移動を拒否し、`InfinityExit` もボス出口の本体処理を止めるため、帰還も潜行もできない。本体の魂の非同期処理の根拠は `docs/specs/issue-95-infinity-mode.md` §2・§5にある。

修正では、`TickNative` が保存待ち中も到着・撃破・魂の出現と消滅を観測し、既存の保存処理へ渡すようにした。帰還・潜行の画面を開く処理と地図枯渇時の更新通知には、従来の保存待ち条件を残した。実際の移動・帰還・潜行も既存の `CanAdvance`、報酬精算、保存条件を使う。魂を観測したと偽ったり、精算待ちを解除したりはしない。

報告された現象そのものは未再現であり、今回修正した経路が実際の停止原因かは未確定である。

## 判断したこと

- 停止場所とログがないため、エリア移動にはボス後の潜行も含めた。保存待ちが魂の観測を妨げ、その後の進行を永久に止める因果関係をコードで示せるため、この経路を修正した。
- 魂の出現と消滅は本体の進行の観測として扱い、プロフィール保存の完了を待たない。プレイヤーの次の選択と移動は保存待ちの対象とする。既存の状態・保存処理を使い、新しいフックや通信項目は足していない。
- Infinity OFFでは `TickNative` の冒頭から戻る。リンボ深度の判定は加えていない。深度6/5/1のON/OFF対照と通常移動の既存ケースで、通常モードと他階層への影響を確認した。Infinity設定、周期、報酬、保存形式、通信形式は変更していない。
- テストは新規作成していない。既存の `PureWhiteBossKilledWhileSaveHoldBlocksTickStillReachesSoulFinish` は、保存待ち中も状態を観測する仕様に合わせて最小限変更した。魂の回収まで保存待ちを維持し、待機中は `CanAdvance` がfalse、解除後はtrueとなることを確認する。
- 指定の作業ディレクトリとブランチはサンドボックスに存在しなかったため、実際のリポジトリの `work` から `ai/pub-issue-10` を作成した。Serenaは提供されておらず、検索・読み取り・編集ツールを使用した。

## 実行した検証

2026-11-01、Linux・.NET SDK 10.0.111で実行した。ビルドとテスト出力は既存スクリプトで `TMPDIR` のHDDへ置いた。`NUGET_PACKAGES` は変更していない。net8.0の実行には `DOTNET_ROLL_FORWARD=Major` を使った。

- 変更前: `python3 tests/repro/issue10/run.py`。終了コード0、14成功・失敗0・スキップ0。結果は `/srv/hddtmp/issue10-qsqmz7oz/results.json`。報告された進行不能は未再現。
- 変更後: `python3 tests/repro/issue10/run.py`。終了コード0、14成功・失敗0・スキップ0。結果は `/srv/hddtmp/issue10-ju9i4zcj/results.json`。ソロ・diffLimbo・深度6/5/1のON/OFF、開始順序、開示先への移動、精算後の移動、潜行時のゾーン更新を確認した。
- 変更後: 同コピー先のStartupプロジェクトで `dotnet test --no-build --no-restore --filter` を以下の名前で絞って実行。終了コード0、30成功・失敗0・スキップ0。TRXは `/srv/hddtmp/issue10-ju9i4zcj/related-results/related.trx`。

```text
PureWhiteBossKilledWhileSaveHoldBlocksTickStillReachesSoulFinish
ScheduledBossIsChosenVisibleDestinationEvenWhenNonAdjacent
DelveWithASavedPrimusPlanPlaysThePureWhiteRouteAsABossFinale
PureWhiteGateArrivalFromALegacyExploringSaveEntersTheBossFight
PendingTravelCannotEscapeItsConfirmedContext
ConfirmedVoteIgnoresCompatibilityWarningsAndStillWaitsForNativeTravelReason
NormalMode
NativeContinueReplaysDestinationAndPreservesSavedNextAgainstNewerBossDueProfile
Confirmed_room_travel_
Exhaustion
```

各名前に `FullyQualifiedName~` を付け、`|` で結んだ。保存待ち中の魂の観測、周期10/15/20のボス到着、純白の直接到着、移動保留の失効、通常モード、Continue、個人選択待ち、地図枯渇時の更新を対象にした。

変更後は計44件が成功した。StartupハーネスはMODの製品ソースをリンクするが、ゲーム本体のリンボ効果、シーン読込、出口操作、実際のプロフィール保存は模擬APIである。成功を実機での再現解消の証明とはしない。

`git diff --check` も終了コード0。修正前の不具合を示す再現テストはブランチになかったため、修正を戻す検査は実施していない。

## 未実行

- 実機は未確認。ソロのリンボ6での出口操作、探索継続、ボス後の継続・帰還、OFFと他階層との実機比較は、ゲーム実行環境がないため未実行。
- 本体DLL参照のMODビルドと、本体リンボ・シーン読込・実際の保存を含む結合検証。本体DLLと実行環境がないため未実行。
- フルテスト、遅いテスト、`tools/test_changed.py --all`、`SODRPG_SLOW=1`、Python unittest一式、Issue73.Native.Testsの保存テスト。依頼に従い、変更したTickと移動に関係するStartupの既存ケースに絞った。
- yomiyasuによる文章検査。指定のスキルと検査スクリプトがこの環境に見つからず、実行できなかった。日本語の記述は手動で見直した。

## 要件と審査懸念への対応

FR-1・FR-2・FR-3は、`src/SodRpg.Mod/InfinityMode.cs` の保存待ち中の観測継続で対応した。ソロのリンボ6・Infinity ONは既存の `Issue10LimboTravelReproTests.cs`、観測と進行待ちの区別は既存の `InfinityIssue528ReproTests.cs` で確認した。

通常モードと他階層への影響懸念には、OFFの早期returnの確認、深度6/5/1のON/OFF対照、既存の通常移動テストで対応した。進行不能の懸念には、魂の観測を逃して `WaitingSoulFinish` に残る経路を塞いだ。ログと停止場所の不足は残るため、実際の報告との一致は断定していない。実機確認用のIssueは作成していない。
