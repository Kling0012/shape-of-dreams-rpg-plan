# Issue #457 バグ再現記録

## 現在の扱い（2026-10-16、診断ログの修正ラウンド）

**利用者の症状は未再現・未修正。今回は診断ログの確認のみで、Issue #457 はリリース後も閉じず、ログを待つ。** 下記の過去工程にある失敗・修正・成功の記録は、今回のセッションで利用者の症状を再現・解消できたことを意味しない。

今回の開始スナップショットには診断ログと過去の条件・数値変更がすでに含まれている。過去の変更を追加・差し戻しせず、ゲームデータ・判定・効果量・説明・内容指紋・保存・通信を据え置いた。今回のコミットはREADMEと本記録の更新のみ。今回実行した検証と確認できなかった範囲は末尾「診断ログの修正ラウンド」に記す。

## 修正前の再現結果

修正前の本番 `Content.Uniques` と `Links.Satisfied` を使用した再現テスト2件が、期待する到達可能性を満たさず失敗した（終了コード1、2件実行／2件失敗）。製品コード・装備条件・数値・説明文は変更していない。

- 対象はセット品ではなく固有品「傑作の連剣」`unique.link_mist_masterwork`。
- 条件は `Hero_Mist`、`St_D_AstridsMasterpieceEnGarde`、`St_D_AstridsMasterpiecePriorite` のすべて。
- Mistの出発時の合法な8構成（Identity 2択 × Q 2択 × R 2択、Movementは固定）で発動数0。
- 診断として不可能なIdentity2種類の同時入力を渡した場合だけ発動した。
- 全固有品・セット連携393件にIdentity／Movement各1枠の必要制約を適用した検査で、同型の到達不能条件を計4件確認した。

| 固有品ID | 名前 | 同時に要求されるIdentity |
| --- | --- | --- |
| `unique.link_mist_masterwork` | 傑作の連剣 | `St_D_AstridsMasterpieceEnGarde` + `St_D_AstridsMasterpiecePriorite` |
| `unique.link_vesper_resolve_mercy` | 決意と慈悲 | `St_D_Resolve` + `St_D_MercyOfEl` |
| `unique.link_lacerta_powder_tap` | 双発の火薬庫 | `St_D_DoubleTap` + `St_D_SalamanderPowder` |
| `unique.link_husk_wind_scar` | 風傷の殺陣 | `St_D_TheKillingFlow` + `St_D_ScarOfTheWind` |

## 再現方法

リポジトリルートで次を実行する。対象は追加した再現クラスだけで、全スイートは実行しない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter FullyQualifiedName~Issue457LinkReachabilityTests \
  --logger 'console;verbosity=detailed' \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

2026-10-16のサンドボックスで上記コマンドを実行し、次の結果を確認した（終了コード1）。同一日の再現工程（別サンドボックス、ai/issue-457 ブランチ）で再実行しても同じ2件の失敗と終了コード1を確認した。前回作業ブランチ（ai/wip-issue-457）はこのサンドボックスから参照できないため、基本スナップショットに含まれる再現テストと本記録を用いて再検証した。

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

先行するビルド済みアセンブリの実行でも同じ失敗を確認し、`--logger 'trx;LogFileName=issue457-repro.trx' --results-directory /tmp/issue457-repro-results` でTRXを保存した。TRXは環境内の補助資料であり、追試は上記コマンドとコミットした再現テストから行える。

初回はNuGet脆弱性データの外部照会失敗 `NU1900` が警告のエラー扱いとなり復元を停止した。コマンド引数の `NuGetAudit=false` で当該照会を省いた。続く実行は.NET 8ランタイム不在でテストホストを起動できなかったため、`DOTNET_ROLL_FORWARD=Major` で利用可能な.NET 10.0.11を使用した。プロジェクトやCIの設定は変更していない。これらの環境エラーはバグ再現の根拠に含めず、その後のテストアサーション失敗を根拠とする。

## 根拠と検査の範囲

- 本番定義：`src/SodRpg.Core/Game/Content.cs` の上記4固有品。
- 本番判定：`src/SodRpg.Core/Game/Links.cs` の `Satisfied` は `Requires` 全要素に `RequirementSatisfied` を要求する。記憶条件は本人の装着記憶だけを参照するため、味方が別のIdentityを持っていても補えない。
- ゲームへの反映：`src/SodRpg.Mod/HostAuthority.cs` の連携走査は、`LinkSkills` の各枠から `hero.Skill.GetSkill(loc)` の実型名を1つずつ収集し、`Links.Satisfied` を呼ぶ。
- 記憶の分類・選択肢：`Links.cs` のIdentity／Movement分類と、`docs/specs/v1.27-traveler-kits.md` の「遠征の前に選べる装備」。Mistの記憶2種はどちらもIdentityとして明記されている。
- 本体の読み取り専用逆コンパイル資料：`/mnt/ghauto-sbx/sod-gamedata/decompiled/sod-decomp/Dew.Core/HeroSkill.cs`。`OnLateStartServer`（433〜458行）は各枠にロードアウト候補を1つ設定する。`GetSkill`（471〜483行）はIdentity枠から1つだけ返す。`EquipSkill`（560〜589行）は既存の同枠記憶を外してから設定し、`CanReplaceSkill`（682〜693行）はIdentityとMovementの通常の差し替えを禁止する。

393件の検査は `Content.Uniques` のすべての非null連携と、`Content.Sets` のすべての `LinkStages` を列挙する。装着記憶をIdentity最大1・Movement最大1に制限して、本番の判定が成立する構成を探す。通常記憶・エッセンス・近くの味方旅人は条件どおりに与え、今回のスロット矛盾を独立に検査する。残り389件がこの必要制約を通ることは、全入手経路・全イベント・全発動効果の成立を保証しない。

## 判断したこと・次工程への引き継ぎ

添付画像やIssue原文を取得せず、報告された記憶の実型名と本番定義から対象を特定した。本体スロット制約と既存の記憶一覧が一致するため、再現には追加の利用者情報を必要としない。

再現工程では条件緩和方法・効果量・バランスの変更は決めていない。4件の修正方法、日英の説明・図鑑・Wikiの整合、内容指紋が変わる場合の協力プレイ参加者全員への更新告知は修正工程に残す。今回はテストと再現記録のみなので、ゲーム内容の指紋は変更していない。

再現工程の未実行：Unity実機での装備操作・表示・協力通信・`dreamforge_simkill`（実機利用不可）、全テストスイート（工程の指示により対象クラスだけ実行）、MODのゲームDLL参照ビルド（製品コード変更なし）、NuGet脆弱性照会の完了（接続失敗のため実行引数で省略）。

## 修正工程（2026-10-16）

### 装備条件の修正

4件とも2つ目のIdentityを同じ旅人のMovementへ置き換えた。`Requires`の全条件一致と3条件の上限を維持し、既存の効果量・固有パワー・IDを据え置いた。

| 固有品ID | 修正後の記憶条件（加えて対応する旅人） | 同調の効果量 |
| --- | --- | ---: |
| `unique.link_mist_masterwork` | `St_D_AstridsMasterpieceEnGarde` + `St_M_FastFeet` | 46% |
| `unique.link_vesper_resolve_mercy` | `St_D_Resolve` + `St_M_Charge` | 46% |
| `unique.link_lacerta_powder_tap` | `St_D_DoubleTap` + `St_M_NimbleDodge` | 46% |
| `unique.link_husk_wind_scar` | `St_D_TheKillingFlow` + `St_M_FlashStep` | 48% |

Mistはアンガルド選択時に発動する。プリオリテ単独への条件拡張や、連携全体のOR条件化は行わず、既存の「旅人＋記憶2つ」の設計を保った。

### 全件見直しと使用イベントの修正

全固有品の連携とセット連携393件について、上記の装備枠制約に加え、定義と本番の発動経路を確認した。`Content.cs`の直接定義360件も走査した。Identityのみを使用対象にする迅化・鼓舞が別に10件あった。

`HostAuthority.OnSkillUse`は、使用された記憶の実型名が`Requires`に含まれるときだけ迅化・鼓舞を処理する。Identityは受け身で通常の使用イベントを発行しない。この制約は既存の`StarRoutesV127Tests.Links_target_only_their_memory_and_remain_inside_single_memory_caps`にも明記されている。装着条件を保ち、使用イベントを必要としない同調へ変更した。通常攻撃への専用イベントを足す方式は採らず、既存の同調処理を使用する。

数値は `旧値 × 同調の条件数別上限 ÷ 旧種類の条件数別上限` を整数へ四捨五入（0.5は切り上げ）した。迅化の上限は50/80/110、鼓舞は40/64/88、同調は25/40/55。これは上限に対する相対量を保つための調整であり、異なる効果の実戦上の等価性を保証するものではない。

| 固有品ID | 条件数 | 旧種類・旧値 | 新種類・新値 |
| --- | ---: | --- | --- |
| `unique.link_yubar_star_echo` | 1 | MemoryHaste 38 | Attune 19 |
| `unique.link3_dawnless_slash` | 3 | MemorySurge 62 | Attune 39 |
| `unique.c_menace_locket` | 2 | MemorySurge 36 | Attune 23 |
| `unique.c_flowkill_seal` | 1 | MemoryHaste 30 | Attune 15 |
| `unique.c_windscar_quill` | 2 | MemorySurge 34 | Attune 21 |
| `unique.c_twobeat_sight` | 2 | MemorySurge 34 | Attune 21 |
| `unique.c_rightofway_feather` | 1 | MemoryHaste 30 | Attune 15 |
| `unique.c_packheart` | 2 | MemorySurge 30 | Attune 19 |
| `unique.c_exotic_inkstone` | 1 | MemorySurge 26 | Attune 16 |
| `unique.c_convergence_comet` | 2 | MemoryHaste 60 | Attune 30 |

修正後の定義走査でIdentity／Movementの重複条件と、Identityだけを使用対象とする迅化・鼓舞は0件。`St_Q_MoonlightPact`を使用対象とする既存の固有連携は、使用できる星座構成があるため維持した（孤狼の星座による使用停止は任意の構成）。この見直しはコードと既存資料によるもので、全記憶の入手経路や全星座構成を実機で網羅したものではない。

### 表示・数値・協力プレイ

`Links.Describe`を共通に使う日英の装備説明・図鑑・Wikiは、修正後の条件と種類を表示する。数値の正本`tools/balance/equipment/uniques.json`、生成C#定数、v1.29の8行と種類別集計を同期した。ローカルWiki生成後、変更14件すべての日英連携説明で条件と効果量を確認した。

数値生成器は旧正本との差分が生じると装備の意味レコード群を出力するため、生成ファイルには約3千行の内容指紋レコードが追加される。`ContentFingerprint`はこのレコード群を取り込むので、今回の種類・数値変更により内容指紋が変わる。新たな互換性ゲートは追加せず、既存の内容不一致警告を維持する。協力プレイ参加者全員への同時更新を日英の更新履歴に記載した。

### 実行済みの検証

本修正工程では新しいテストを追加・変更していない。

- 指定の再現コマンド：修正前は終了1、2件失敗。修正後は終了0、2件成功。合法Mist 8構成中4構成で発動、393連携の枠制約による到達不能0件。
- 一時差し戻し検証：テストを変更せず、4件の装備条件だけを修正前へ戻して上記の指定コマンドを再実行。終了1、2件失敗、Mist 0/8、到達不能4件を確認。その後、保存した修正を再適用して同じコマンドを再実行し、終了0、2件成功、Mist 4/8、到達不能0件を確認。例外時も内容を復元する`finally`を用いた。
- 関連する既存テスト7クラス：終了0、22件成功（再現2件を含む）。`LinksV126Tests`、`LinkActivationV127Tests`、`ContentTableV129Tests`、`CodexTextConsistencyTests`、`ContentFingerprintTests`、`StarRoutesV127Tests`と再現クラスを選択した。
- `python3 tools/balance/gen_cs.py`：終了0、数値C#を生成。`python3 tools/balance/gen_cs.py --check`：終了0、最新と確認。
- `DOTNET_ROLL_FORWARD=Major dotnet build src/SodRpg.Mod -c Release -p:PythonCommand=python3 -p:NuGetAudit=false -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'`：終了0、警告6件、エラー0件。警告は既存APIの非推奨3件と既存診断フィールドの未代入3件。
- `DOTNET_ROLL_FORWARD=Major dotnet run --project tools/WikiGen -c Release -p:PythonCommand=python3 -p:NuGetAudit=false -- /tmp/issue457-wiki`：終了0、ローカル生成のみ。変更14件の日英の連携説明を確認。Wikiへの公開は行っていない。
- `git diff --check`：終了0。

関連テストのコマンド：

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Issue457LinkReachabilityTests|FullyQualifiedName~LinksV126Tests|FullyQualifiedName~LinkActivationV127Tests|FullyQualifiedName~ContentTableV129Tests|FullyQualifiedName~CodexTextConsistencyTests|FullyQualifiedName~ContentFingerprintTests|FullyQualifiedName~StarRoutesV127Tests' \
  --logger 'console;verbosity=normal' \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

### 修正工程の未実行・限界

- 未実行：Unity実機での装備操作、1920×1080での表示、実戦中の効果量・使用感、協力通信、`dreamforge_simkill`。理由：この環境では実機を利用できない。特に同調へ変えた10件の実戦バランスは未確認。
- 未実行：全テストスイート・slowテスト。理由：指示に従い既存の関連クラスだけを実行。
- 未実行：NuGet脆弱性照会。理由：ネットワーク制約により`NuGetAudit=false`を指定。
- 未実行：公開Wikiの更新。理由：ローカル生成による整合確認までが担当範囲。

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

TRXは`/tmp/issue457-current-results/issue457-current.trx`に保存された。全393件の検査範囲・限界は上記「根拠と検査の範囲」のとおりで、Identity／Movement枠の必要制約の確認である。

### 品質懸念の確認と未実行

- 対象特定：Issueの本文・コメント・添付画像へアクセスせず、コードから「傑作の連剣」`unique.link_mist_masterwork`を確認した。
- 条件変更と説明・バランス：前工程の変更がスナップショットに含まれることを確認した。今回は再現工程のため条件・効果量・説明文を変更していない。日英の説明・図鑑・Wikiの整合を確かめる関連テストやWiki生成は**未実行**（今回の対象は既存再現クラス2件のみ）。過去セッションの成功記録を今回の検証実績とはしない。
- 内容指紋：`ContentFingerprint.cs`が`EquipmentItemsBalanceValues.ContentFingerprintRecords`を取り込むことと、参加者全員の更新を求める日英の更新履歴が存在することを確認した。指紋関連テスト・協力通信は**未実行**（今回の再現範囲外・実機利用不可）。今回の変更は再現記録だけで、ゲーム定義には変更がない。
- 修正前テストの失敗確認は**未実行**：修正前の履歴をこのサンドボックスから参照できず、開始時点で既存修正が含まれていたため。今回実行したテストは2件とも成功し、修正前失敗を再確認したとは報告しない。
- Unity実機での装備・画面・戦闘・協力プレイ・`dreamforge_simkill`は**未実行**（実機利用不可）。全テスト／slowテスト・MODビルドは**未実行**（製品コード変更なし、絞り込み実行の指示）。NuGet脆弱性照会は**未実行**（ネットワーク制約に合わせ`NuGetAudit=false`）。

## 診断ログ追加工程（2026-10-16）

今回の開始点は`149927a`の単一スナップショット、ブランチ`work`で、リモート・`origin/main`・`ai/wip-issue-457`は存在しなかった。過去ブランチとの差分は取得できず、引き継がれたコードと本記録を確認して`ai/issue-457`を作成した。リポジトリと親ディレクトリに`AGENTS.md`／`CLAUDE.md`は存在せず、ルート・`src/`・`src/SodRpg.Mod/`のREADMEと既存の診断出力を読んだ。

不具合の調査のための記録を追加。原因の確定・修正はログ受領後。過去工程の修正・失敗記録は今回の再現実績とは扱わない。今回は条件・効果量・説明生成・内容指紋・保存・通信を変更していない。Issueは診断のリリース後も閉じず、ログを待つ。

`HostAuthority.UpdateLinks`の既存判定後（連携なしの早期returnも含む）から、独立した`DiagnoseLinks`へ観測値を渡す。連携定義・記憶・エッセンス・生存・成立結果が同じなら再出力せず、文字列生成は変化時のみ。ホスト全体で遠征ごとに32行を上限とし、状態ごとのIdentity連携詳細は6件まで。診断専用の再利用コレクションだけを更新し、判定や補正には書き戻さない。例外は診断内で吸収する。

ゲーム標準の`Log.Info`は`UnityEngine.Debug.Log`へ出力している。ルートREADMEにある`Player.log`のWindows／Protonの保存先を使い、#457向けのコピー・個人情報除去・添付方法と追加質問を追記した。実機のログファイルそのものは取得していない。

### 今回実行した検証

- `DOTNET_ROLL_FORWARD=Major dotnet build src/SodRpg.Mod -c Release -p:PythonCommand=python3 -p:NuGetAudit=false -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'`：終了0、警告6件・エラー0件。診断状態の比較を最終調整後、同じコマンドで再ビルドし同じ結果を確認。警告は非推奨API3件・既存未代入フィールド3件。ゲームへの配置は実行されていない（ビルド出力に配置メッセージなし）。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue457LinkReachabilityTests|FullyQualifiedName~LinksV126Tests|FullyQualifiedName~LinkActivationV127Tests' --logger 'console;verbosity=detailed' -p:PythonCommand=python3 -p:NuGetAudit=false -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'`：終了0、既存11件成功。合法Mist構成4/8成立、固有品・セット連携393件のIdentity／Movement枠制約による到達不能0件。テストの追加・変更なし。Coreのテストであり、Unity上の診断出力を実行したものではない。
- `git diff --check`：終了0。差分でゲームデータと判定・効果処理が変更されていないことを確認。

### 今回の未実行・限界

- **未実行**：利用者の症状の実機再現、実際の`Player.log`への書き込み・ログ障害時の実行確認、Unity画面・戦闘・協力通信・`dreamforge_simkill`。理由：実機利用不可。元の不具合は修正済みとしない。
- **未実行**：全テスト・slowテスト。理由：指示に従い関連する既存テストだけを前景で実行。
- **未実行**：修正前条件へ差し戻した失敗確認。理由：修正前履歴がなく、本工程はログだけに限定されている。
- **未実行**：日英図鑑・Wiki生成・数値生成器・内容指紋の関連テスト。理由：今回は内容定義・数値・説明生成に差分がない。対象特定はコードで確認し、品質懸念の条件・バランス・指紋への対応はそれらを追加変更しないこととした。
- **未実行**：NuGet脆弱性照会。理由：ネットワーク制約に合わせ`NuGetAudit=false`を使用。
- **未実行**：push・PR作成・リリース・Issue投稿・クローズ。理由：この工程では禁止。ログ受領後の原因特定と修正は継続課題。

## 診断ログの修正ラウンド（2026-10-16）

### 引き継ぎと審査指摘への対応

開始点は`b2b5198`の単一スナップショット、ブランチ`work`。リモート・`origin/main`・`ai/wip-issue-457`はなく、`ai/issue-457`を作成した。`gh pr view 477 -R Kling0012/shape-of-dreams-rpg-plan --comments`と`gh pr diff 477 -R Kling0012/shape-of-dreams-rpg-plan`は、いずれも`gh: command not found`で取得できなかった。GitHubへの接続・認証も利用できない。したがってPRとmainの差分全体が診断だけかは未確認であり、今回の差分が文書だけであることと区別する。

提示された全体テストの失敗記録は、`dotnet restore SodRpg.sln`の終了0と復元出力の途中までで、失敗したテスト名・コマンド・エラー本文が含まれていない。失敗原因を推測してゲームの挙動やテストを変更せず、今回の許可範囲に関係する既存テスト22件とMODビルドを確認した。**前回の全体テスト失敗の解消は確認できていない。**

- 対象特定の懸念：`Content.cs`の固有品「傑作の連剣」`unique.link_mist_masterwork`を再確認した。開始時点の条件は`Hero_Mist`＋`St_D_AstridsMasterpieceEnGarde`＋`St_M_FastFeet`。画像とIssue本文による照合は未実行。
- 条件変更・バランス・説明の懸念：今回は条件緩和を選ばず、開始時点の定義・効果量を維持した。日英説明・図鑑・数値正本に関する既存テストが成功した。過去工程の条件変更を今回追加した修正と扱わない。
- 協力プレイの内容指紋への懸念：`ContentFingerprint`とその入力に変更を加えていない。既存の指紋テスト4件は成功したが、mainとの一致や実際の通信を確認したものではない。
- 元の不具合の扱い：READMEと本記録の冒頭に未再現・未修正を明記し、既存テストの成功だけで完了としない。Issueはリリース後も閉じず、利用者のログを待つ。

### 診断コードの確認

`HostAuthority.cs`の`HeroRuntime.LinkDiagnostic`と`UpdateLinks`内の2つの呼び出し、および`HostAuthority.LinkDiagnostics.cs`を確認した。既存判定の後から観測値を読み取り、比較結果とスナップショットは診断専用の状態にだけ保存する。`Requires`、`SatisfiedLinks`、装着中の記憶・エッセンス、補正値、ビルド、保存データ、通信メッセージへの書き戻しはない。

遠征IDが変わるとホスト共通の出力予算を戻し、最大32行で打ち切る。同じ定義・生存状態・採取状態・記憶・エッセンス・成立結果なら文字列を生成せず戻る。状態変更時だけ再利用コレクションの中身を更新する。Identity連携の詳細は状態ごとに最大6件、残りは件数だけ。診断全体が例外を吸収する。これらはコード確認であり、Unity上での例外注入や実ログ出力の動作試験ではない。診断の実装とゲームデータは今回変更していない。

### 今回実行した検証

新しいテストは追加・設計しておらず、既存テストも変更していない。以下は今回のセッションで前景実行し、終了を確認した結果。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Issue457LinkReachabilityTests|FullyQualifiedName~LinksV126Tests|FullyQualifiedName~LinkActivationV127Tests' \
  --logger 'console;verbosity=detailed' \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

終了0、既存11件成功。合法Mist 8構成中4構成で成立、393連携のIdentity／Movement枠制約による到達不能0件。これは現在の定義を検査するCoreテストで、利用者の症状や診断ログの実行を再現したものではない。コンパイル時の既存スタブの警告は出たが、エラーはなかった。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~ContentTableV129Tests|FullyQualifiedName~CodexTextConsistencyTests|FullyQualifiedName~ContentFingerprintTests|FullyQualifiedName~StarRoutesV127Tests' \
  --logger 'console;verbosity=normal' \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

終了0、既存11件成功（日英説明・図鑑1件、指紋4件、装備定義・数値4件、星図・Identity連携2件）。これらの成功はmainとのゲーム内容の一致を証明しない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet build src/SodRpg.Mod -c Release \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

終了0、警告6件、エラー0件。警告は既存API非推奨3件と既存診断フィールド未代入3件。参照先に`Mods`ディレクトリがなく、配置処理は走っていない。

`git diff --check`は終了0。`git diff --stat`と差分本文で、今回の変更がREADMEと本記録の2ファイルだけで、製品コード・データ・テストに変更がないことを確認した。

### 未実行・未解決

- **未実行**：PRコメント・PR差分・mainとの比較。理由：CLI・認証・ネットワーク・比較対象の履歴が利用できない。開始スナップショット内の過去の製品変更をmainとの差分と断定できない。
- **未実行**：前回失敗した全体テストの再実行。理由：全体・slowテスト禁止の指示に従った。提供ログに失敗箇所が含まれず、関連22件成功から全体テストの復旧を判断しない。
- **未実行**：利用者の症状の再現、`Player.log`への書き込み、ログ障害時の例外処理、1920×1080での画面、戦闘、協力通信、`dreamforge_simkill`。理由：実機利用不可。元の不具合は未修正。
- **未実行**：旧条件への差し戻し確認。理由：今回バグ修正や条件変更をせず、比較元の履歴もない。
- **未実行**：Wiki生成・公開、数値生成器、その他のテスト。理由：対象ファイルを変更せず、今回は関連する既存テストだけを実行する範囲。
- **未実行**：NuGet脆弱性照会。理由：制限されたネットワークに合わせ`NuGetAudit=false`を使用。
- **未実行**：push、PR更新、リリース、Issue投稿・クローズ。理由：この工程では実行しない。原因特定と修正はログ受領後の継続課題。
