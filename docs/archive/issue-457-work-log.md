# Issue #457 過去の作業記録

2026-10-16の工程記録を保存したもの。修正内容と検証の要点は[再現記録](../issue-457-reproduction.md)を参照。以下の「今回」は当時の工程を指す。

## 修正前の再現環境

2026-10-16のサンドボックスで[再現記録のコマンド](../issue-457-reproduction.md#検証)を実行し、次の結果を確認した（終了コード1）。同一日の再現工程（別サンドボックス、ai/issue-457 ブランチ）で再実行しても同じ2件の失敗と終了コード1を確認した。前回作業ブランチ（ai/wip-issue-457）はこのサンドボックスから参照できないため、基本スナップショットに含まれる再現テストと本記録を用いて再検証した。

```text
Masterwork_flurry_can_activate_in_a_legal_Mist_loadout [FAIL]
Legal Mist loadouts: 8; active: 0
Diagnostic only, impossible dual-Identity input: active=True
Every_relic_link_can_be_satisfied_with_at_most_one_Identity_and_one_Movement [FAIL]
Checked relic/set links: 393; unreachable: 4
Test Run Failed.
Total tests: 2
     Failed: 2
```

先行するビルド済みアセンブリの実行でも同じ失敗を確認し、`--logger 'trx;LogFileName=issue457-repro.trx' --results-directory /tmp/issue457-repro-results` でTRXを保存した。TRXは環境内の補助資料であり、追試は再現記録のコマンドとコミットした再現テストから行える。

初回はNuGet脆弱性データの外部照会失敗 `NU1900` が警告のエラー扱いとなり復元を停止した。コマンド引数の `NuGetAudit=false` で当該照会を省いた。続く実行は.NET 8ランタイム不在でテストホストを起動できなかったため、`DOTNET_ROLL_FORWARD=Major` で利用可能な.NET 10.0.11を使用した。プロジェクトやCIの設定は変更していない。これらの環境エラーはバグ再現の根拠に含めず、その後のテストアサーション失敗を根拠とする。

## 判断したこと・次工程への引き継ぎ

添付画像やIssue原文を取得せず、報告された記憶の実型名と本番定義から対象を特定した。本体スロット制約と既存の記憶一覧が一致するため、再現には追加の利用者情報を必要としない。

再現工程では条件緩和方法・効果量・バランスの変更は決めていない。4件の修正方法、日英の説明・図鑑・Wikiの整合、内容指紋が変わる場合の協力プレイ参加者全員への更新告知は修正工程に残す。今回はテストと再現記録のみなので、ゲーム内容の指紋は変更していない。

再現工程の未実行：Unity実機での装備操作・表示・協力通信・`dreamforge_simkill`（実機利用不可）、全テストスイート（工程の指示により対象クラスだけ実行）、MODのゲームDLL参照ビルド（製品コード変更なし）、NuGet脆弱性照会の完了（接続失敗のため実行引数で省略）。

## 自動回復後の再現工程（2026-10-16、今回のセッション）

### 引き継ぎ状態と判断したこと

今回のサンドボックスは初期ブランチ`work`、コミット`65b93d0`の単一スナップショットだった。`ai/wip-issue-457`、`origin/main`、リモートの設定は存在しないため、指定された過去ブランチとの比較はできなかった。スナップショットには上記の再現テスト・修正コード・更新履歴がすでに含まれていた。作業用に`ai/issue-457`を作成して切り替えた。リポジトリ内と親ディレクトリに`AGENTS.md`／`CLAUDE.md`は存在せず、ルートと`src/`、`src/SodRpg.Mod/`のREADMEを確認した。

現在の`Content.cs`では「傑作の連剣」が`Hero_Mist`＋`St_D_AstridsMasterpieceEnGarde`＋`St_M_FastFeet`を要求する。同型の残る3件もIdentity 1種＋Movementに変更済みだった。過去の失敗記録を今回実行した結果とは扱わず、既存テストで現在の状態を再検証した。再現のために修正済み製品コードを旧条件へ書き換えることはせず、今回の判定は`NOT_REPRODUCED`とする。新しい修正・テストは追加していない。

### 今回実行した確認

リポジトリルートで次を実行した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter FullyQualifiedName~Issue457LinkReachabilityTests \
  --logger 'console;verbosity=detailed' \
  --logger 'trx;LogFileName=issue457-current.trx' \
  --results-directory /tmp/issue457-current-results \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

終了コードは**0**。復元・ビルド後に対象2件が成功した。ビルドには警告が出たが、テスト失敗・テストホスト起動失敗はなかった。

```text
Passed SodRpg.Core.Tests.Issue457LinkReachabilityTests.Masterwork_flurry_can_activate_in_a_legal_Mist_loadout
Legal Mist loadouts: 8; active: 4
Diagnostic only, impossible dual-Identity input: active=False
Passed SodRpg.Core.Tests.Issue457LinkReachabilityTests.Every_relic_link_can_be_satisfied_with_at_most_one_Identity_and_one_Movement
Checked relic/set links: 393; unreachable: 0
Test Run Successful.
Total tests: 2
     Passed: 2
```

TRXは`/tmp/issue457-current-results/issue457-current.trx`に保存された。全393件の検査範囲・限界は[再現記録の「検証」](../issue-457-reproduction.md#検証)のとおりで、Identity／Movement枠の必要制約の確認である。

### 品質懸念の確認と未実行

- 対象特定：Issueの本文・コメント・添付画像へアクセスせず、コードから「傑作の連剣」`unique.link_mist_masterwork`を確認した。
- 条件変更と説明・バランス：前工程の変更がスナップショットに含まれることを確認した。今回は再現工程のため条件・効果量・説明文を変更していない。日英の説明・図鑑・Wikiの整合を確かめる関連テストやWiki生成は**未実行**（今回の対象は既存再現クラス2件のみ）。過去セッションの成功記録を今回の検証実績とはしない。
- 内容指紋：`ContentFingerprint.cs`が`EquipmentItemsBalanceValues.ContentFingerprintRecords`を取り込むことと、参加者全員の更新を求める日英の更新履歴が存在することを確認した。指紋関連テスト・協力通信は**未実行**（今回の再現範囲外・実機利用不可）。今回の変更は再現記録だけで、ゲーム定義には変更がない。
- 修正前テストの失敗確認は**未実行**：修正前の履歴をこのサンドボックスから参照できず、開始時点で既存修正が含まれていたため。今回実行したテストは2件とも成功し、修正前失敗を再確認したとは報告しない。
- Unity実機での装備・画面・戦闘・協力プレイ・`dreamforge_simkill`は**未実行**（実機利用不可）。全テスト／slowテスト・MODビルドは**未実行**（製品コード変更なし、絞り込み実行の指示）。NuGet脆弱性照会は**未実行**（ネットワーク制約に合わせ`NuGetAudit=false`）。
