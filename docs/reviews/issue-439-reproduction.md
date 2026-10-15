# Issue #439：禁忌の核の未取得報告の再現調査

判定：**NOT_REPRODUCED**。報酬処理を変更する前に既存の再現候補テスト4件を実行したが、すべて成功した。修正前に失敗する再現ケースは得られていない。ゲーム内での報告症状の否定や、修正完了を意味しない。

## 判断したこと

- 報告の「禁忌化したボス」は、既存仕様の宿敵化したボスとして確認した。CHANGELOG v2.10.39 と `src/SodRpg.Mod/README.md` に、宿敵ボスは禁忌の核2個、撃破前の本人の因縁が3段以上なら追加1個、エリート宿敵は核なしと記載されている。`Forbidden.NemesisCores` と `Rules.OnKill` も同じ条件である。
- 核は地面の戦利品や未確保の鞄ではなく、プロフィールの所持素材へ直接加算される。再確認ではF6メニュー上部の素材数を比較する。
- 現行コードのホスト生成時の宿敵フラグ、撃破記録、通信変換、撃破分類台帳、報酬への受け渡しを確認した。死亡処理は宿敵の辞書を除去する前にホストの撃破情報を記録する。コード読解だけで実ゲームの通知順・通信・敵の実設定を保証するものではない。
- 利用者指示「ロギングを追加し 対応を求めてください」に従い、再確認に必要な診断ログだけを追加した。ドロップの条件・個数・保存形式・通信形式は変更していない。根本修正は次工程に残す。

## 修正前の再現候補の実行

前回から引き継いだ `tests/SodRpg.Core.Tests/NemesisCoreDropIssue439Tests.cs` を、診断ログ追加前に実行した。今回新しいテストは作成していない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --filter FullyQualifiedName~NemesisCoreDropIssue439Tests --nologo -p:PythonCommand=python3 --logger 'console;verbosity=normal'
```

終了コード0、4成功・0失敗。通常の宿敵ボスで核2個、因縁3段以上で3個、宿敵エリートで核0個と因縁加算、ホスト撃破情報のメッセージ往復から分類台帳を経由した核付与を確認した。**修正前の失敗は確認できていない**。

このテストはホストの出現・Unityの死亡イベントを実行せず、対象フラグとボスの分類を入力して報酬を確認するものなので、実機の再現の代用とはしない。

## 追加した診断ログ

既存の出現ログ `[Dreamforge] Nemesis:` に加えて、`[Dreamforge] Nemesis drop:` で次を追えるようにした。

- `native death`：敵の実型・netId・階級・表示側で観測した宿敵の印・本体の報酬除外設定・ローカル旅人の有無・遠征の有効状態・未解決死亡数。
- `host skipped`：ホストで追跡されていないボス、本体の報酬除外設定、ハンターの報酬除外による撃破情報の対象外。
- `host fact`：ホストが確定した敵の階級・宿敵の印・遠征ID・ストリーム・連番・撃破ID。
- `received fact`：受信した印・撃破ID・台帳への追加結果・遠征の有効状態・未解決死亡数。
- `grant begin` / `grant end`：精算対象の階級・印・撃破ID・核の所持数の加算前後・既存仕様での期待加算数・因縁。

ログはボスまたは宿敵の死亡・撃破情報受信・精算時だけで、毎フレームの文字列確保は追加していない。重複死亡コールや再送受信は同じnetId/撃破IDで照合する。`native death` の `observedAffixes=None` は表示用辞書の除去通知が先に届いた場合にも起こるため、その行だけでフラグ消失とは断定しない。

## 追加後の検証

- ゲームDLL参照のReleaseビルド：終了コード0、警告6・エラー0。ゲームディレクトリに実行ファイルやModsディレクトリはなく、自動配置は実行されていない。

  ```sh
  dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' --nologo
  ```

- 核の付与・保存・撃破分類・保留精算・欠落情報の期限切れの既存テスト：終了コード0、26成功・0失敗。

  ```sh
  DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --filter 'FullyQualifiedName~NemesisCoreDropIssue439Tests|FullyQualifiedName~NemesisCoreSaveIssue427Tests|FullyQualifiedName~KillClassificationTests|FullyQualifiedName~PendingKillSettlementTests|FullyQualifiedName~KillFactExpiryTests' --nologo -p:PythonCommand=python3 --logger 'console;verbosity=normal'
  ```

- 純白ルートと通常ルートの保留報酬の既存MOD連携テスト：終了コード0、8成功・0失敗。最初のフィルタの `NativeAcceptanceTests.Kill` に一致するテストはなく、この8件は `PureWhiteRewardChoiceTests` のみ。

  ```sh
  DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj --filter 'FullyQualifiedName~PureWhiteRewardChoiceTests|FullyQualifiedName~NativeAcceptanceTests.Kill' --nologo -p:PythonCommand=python3 --logger 'console;verbosity=minimal'
  ```

- 撃破情報の再送・分類差分・Continue・二重精算防止の既存MOD連携テスト：終了コード0、4成功・0失敗。

  ```sh
  DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj --no-build --filter 'FullyQualifiedName~DifferingVersionPeerWithoutHelloSharesFrameBudgetAndEventuallyReceivesEveryFact|FullyQualifiedName~DifferentialUpdatesCoalesceWithoutMovingIdsBehindTheirNeighborsAndRotateCategories|FullyQualifiedName~Host_rollback_resume_restores_the_expedition_and_prevents_double_rewards|FullyQualifiedName~A_normal_save_keeps_settled_kills_and_local_overflow_shards_without_rejoin_double_grants' --nologo --logger 'console;verbosity=normal'
  ```

- `git diff --check`：成功。
- 未実行：実ゲームでの宿敵化ボスの討伐、ソロ・協力通信でのログ採取、ゲーム内コマンド `dreamforge_simkill`。実行可能なゲーム環境がなく、同コマンドは宿敵の印を指定する引数も持たない。
- 未実行：全件テスト・低速テスト。再現工程の指示に従い関連部分に限定した。

`DOTNET_ROLL_FORWARD=Major` はこのホストに.NET 8ランタイムがなく.NET 10のみあるため指定した。テストプロジェクトの対象フレームワークは変更していない。

## 再確認の依頼文（後続の担当へ）

> 禁忌の核の取得経路を追跡するログを追加しました。ログ追加版で宿敵化したボスを再度討伐し、F6メニュー上部に表示される禁忌の核の所持数を討伐前後で確認してください。核は地面に落ちず、所持素材へ直接加算されます。再発した場合は、使用したMODの版、ソロか協力プレイか、ホストか参加者か、ボス名、発生時刻、討伐前後の核の所持数と因縁の段階、Player.logを提供してください。協力プレイではホストと症状が起きた参加者の両方のログがあると、生成から精算まで照合できます。

このセッションではIssue本文・コメントの取得、GitHubへの投稿、push、PR作成、リリース、デプロイは行っていない。追加ログを含む版の用意と上記依頼の送付は後続の担当に引き継ぐ。
