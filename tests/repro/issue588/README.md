# Issue #588 修正前の再現記録

インフィニティ協力プレイで、参加者の道標に含まれる敵HP・攻撃の代償が担当敵へ反映されず、ホストの道標が使われるケースを再現する。
本工程では製品コードを変更していない。

## 実行

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  --filter FullyQualifiedName~Issue588PersonalWaypointPressureTests \
  --nologo -v quiet -p:WarningLevel=0 \
  --logger 'trx;LogFileName=issue588-pressure.trx' \
  --logger 'console;verbosity=normal'
```

再現テスト: `tests/Issue73.Native.Tests/Issue588PersonalWaypointPressureTests.cs`。
TRX: `tests/Issue73.Native.Tests/TestResults/issue588-pressure.trx`、最終実行の生ログ: 同ディレクトリの `issue588-pressure.log`（既存のgit除外対象）。
このテストは修正前に失敗することが期待される。

## 環境と境界

- Linux、SDK 10.0.111、Microsoft.NETCore.App 10.0.11。既存のnet8.0向けテストを `DOTNET_ROLL_FORWARD=Major` で実行した。
- `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`。worktree内へパッケージキャッシュを変更していない。
- 基点コミット: `67a3839e`、ブランチ: `ai/issue-588`。
- 既存 `Issue73.Native.Tests` は `HostAuthority.EnemyOwnership.cs` と `InfinityMode.PersonalWaypoints.cs` 等の製品ソースをそのままリンクする。
- ゲームAPIは既存 `NativeBoundary.cs` / `EnemyOwnershipBoundary.cs` に置き換える。HPと与ダメは、実際の `PrepareEnemyOwnership` → `FinalizeEnemyOwnership` が登録した処理器を通して測る。
- `HostAuthority.RefreshPressure` はこの境界ではリンク外。部屋の圧は、現行 `HostAuthority.cs` の式に従い、夢1・星0・深度0・ホスト道標の倍率を持つ値をテスト入力として与える。したがって部屋の圧の更新・送信自体のE2E検証ではない。
- `CommitPersonalWaypoints` で両者の道標を保存し、`WaypointTotalsForPlayer` の読み取りと敵の担当者を先にassertする。倍率差がセットアップ失敗によるものではないことを確認する。
- Issueの「儚い記憶」は現行の `Waypoint.FleetingMemories`（日本語表示「駆ける記憶」）、圧×1.25・記憶CD×0.8として扱う。

## 修正前の実測

コマンドの終了コードは **1**。ビルド・探索・実行は完了し、14件中10件失敗、4件成功。失敗はすべて期待する倍率との差によるassertで、例外やビルドエラーによる失敗はない。
HP・与ダメを個別のテストとして測定する。

| モード・入力 | 測定対象 | HP/与ダメの期待倍率 | 実測倍率 | 件数・結果 |
| --- | --- | --- | --- | --- |
| Infinity: ホストNone、参加者FleetingMemories | 参加者の担当敵 | 1.25 | 1.00 | 2件失敗 |
| Infinity: ホストFleetingMemories、参加者None | 参加者の担当敵 | 1.00 | 1.25 | 2件失敗 |
| Infinity: ホストNone、参加者DeepeningNight | 参加者の担当敵 | 1.50 | 1.00 | 2件失敗 |
| Infinity: ホストDeepeningNight、参加者None | 参加者の担当敵 | 1.00 | 1.50 | 2件失敗 |
| Infinity: ホストNoneから、FleetingMemoriesを選んだ参加者へ担当変更 | 変更後の敵 | 1.25 | 1.00 | 2件失敗 |
| 通常協力: ホストNone | 参加者の担当敵 | 1.00 | 1.00 | 2件成功 |
| 通常協力: ホストFleetingMemories | 参加者の担当敵 | 1.25 | 1.25 | 2件成功 |

担当変更は3人のうち元担当者を切断し、残る2人で担当制が有効な状態を保って `ScanEnemyOwnership` を呼ぶ。新担当者の一致を確認後、倍率を測る。

追加前の既存テストは次の2件を実行し、終了コード0・2件成功を確認した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  --no-build --no-restore \
  --filter 'FullyQualifiedName~Owned_enemy_pressure_replaces_the_room_average_instead_of_stacking|FullyQualifiedName~InfinityPersonalChoiceTests' \
  --nologo -v quiet
```

最初の `DOTNET_ROLL_FORWARD` なしの実行は終了1。環境に.NET 8ランタイムがなく、Major指定で再実行した上記2件を成功として扱う。追加後は上記フィルターへ `|FullyQualifiedName~Ordinary_coop_retains_shared_host_waypoint` を足した6件も実行し、終了0・6件成功を確認した。ロールフォワードなしの `InfinityPersonalChoiceTests` 単独実行でも、.NET 8ランタイム不足によるTest Run Aborted（終了1）を確認した。

## 次工程への未実行事項

- **実機・`dreamforge_simkill`**: この環境ではゲームを起動していない。ゲーム画面での再現とは区別する。
- **全員悪夢化・変異除外・抽選確率**: 既存Native境界の `ProcessSpawns` は空実装であり、本再現では実行しない。Issueの「終わらない夜」は現行 `Waypoint.EndlessNight`（「明けない夜」）。`HostAuthority.cs:ProcessSpawns` と `HostAuthority.Waypoints.cs:RollWaypointNightmare` が次工程での確認対象。
- **確保地点の表示・報酬・敵HP/攻撃の同期**: 動的検証していない。#572/#573との整合を確認する必要がある。`HostAuthority.PressureDividend.cs:InstallPressureHealthProcessor` は `OverrideOwnershipHealth` 後の適用倍率を報酬側へ記録しているが、本テストではその報酬処理を実行していない。
- **ソロ・担当なし敵・途中参加・ビルド未受信・現在HP割合の維持**: 本再現の対象外で未実行。通常協力4件の成功をこれらの保証に使わない。
- **全テストスイート・ゲームDLLを参照するMODビルド**: 再現工程では限定テストのみ実行するため未実行。

選択肢の削減・プロトコルやセーブの変更・本体修正は行っていない。品質懸念の解消確認は修正工程へ引き継ぐ。

## 修正工程の結果（2026-10-17）

- このセッションでも修正前に同じ14件を実行し、終了1・10件失敗／4件成功を確認した（`TestResults/issue588-before.trx`）。
- 修正後は冒頭と同じコマンドで終了0・14件成功。`issue588-pressure.trx` と `issue588-pressure.log` に記録した。
- 製品の `HostAuthority.EnemyOwnership.cs`・`HostAuthority.BossShares.cs`・`HostAuthority.Waypoints.cs`・`HostAuthority.cs` だけを一時的にHEADの修正前へ戻して同じテストを実行すると、終了1・10件失敗／4件成功。製品の修正を復元すると終了0・14件成功。`issue588-reverted.trx`・`issue588-reverted.log`・`issue588-revert-check.json` に記録した。再現テストの条件・期待値は変更していない。
- 担当制・ボス持ち分・個人選択を含む既存Nativeテスト49件、動的難易度・NativeAcceptance29件、Coreの道標・悪夢化・夢の圧・報酬114件を実行し、すべて成功した。これは各既存境界の検証であり、今回の個人道標との全組み合わせを動的に検証した意味ではない。
- `python3 tests/repro/issue572/run.py` は15件成功。通常モード用のAPI境界を新しい `EnemyWaypointTotals` の呼び出しに追随させ、既存ケース・期待値は変更していない。
- ゲーム参照DLLを使うMOD Releaseビルドは終了0、エラー0・既存警告6。参照ディレクトリに `Mods` がないため、ゲームへの配置なし。

### 実装と影響の点検

- `PersonalEnemyWaypointsRunning` でInfinity・接続人間2人以上に限定し、通常協力・ソロの共有道標を維持する。`OwnershipBasePressure` は受領済みの担当者個人道標へ差し替え、Build未受信でも道標を失わない。
- `EnemyWaypointTotals` を深度初期化の早期return・全員悪夢化による変種除外・悪夢化抽選へ共通で使う。ボス・共有目標・担当なしは道標なし。出現時の分類は担当変更で再抽選しない。
- 共通の圧・敵数・HUDには個人道標を含めない。ボスの共有HPの各持ち分と被ダメージ補正でも個人道標を除外する。ソロの道標倍率は残す。
- HP・攻撃は既存ホスト処理器へ差し込む。HP割合維持と担当／分類の再送は既存経路を使う。`InstallPressureHealthProcessor` は差し替え後の実適用倍率を配当へ記録する。Infinityの圧による欠片・悪夢化確率の追加倍率は既存どおり1、個人の記憶CD×0.8・覚醒等の報酬も既存の本人用経路を維持する。
- 確保地点には個人道標・確率の対象が担当敵である旨を日英で追記し、HUDのInfinity圧を共通値と明示した。新しい毎フレームのコレクション確保・演出・通信形式・保存項目は追加していない。
- Issueの案B（敵側道標を選択肢から削除）は案Aの代替案であり採用しない。R7はこの代替案の評価として扱い、全既存選択肢を維持する。

### 未実行

- 実機・`dreamforge_simkill`・2台間のHP／攻撃／分類の配送・画面描画（1080pを含む）: ゲームを起動できる環境がなく未実行。
- 今回の個人道標と全員悪夢化・変種除外・確率抽選・報酬・CD・ソロ・担当なし・Build未受信・途中参加の全組み合わせの動的検証: 該当する既存境界がなく、テスト新設禁止のためコード経路を点検した。既存関連テストの成功で代用したとは主張しない。
- 全テスト・遅いテスト（`tools/test_changed.py --all` 等）: 実装工程は変更部分に限定する指定に従い、後段パイプラインへ委ねる。
