# Continue 復元待機の期限

改善案 L1（Issue #1031 のレビュー）に従い、`_nativeContinueRestoring` と `_nativeContinueRestoreDeadline` の代入箇所を確認した。開始時と放棄判定のコメントに、期限の設定順と `PositiveInfinity` の意味を追記した。実行時の動作は変えていない。

## 開始経路と期限の設定

- `_nativeContinueRestoring = true` は `ClientSession.LoadNativeContinue` の1か所だけにある。ホスト・セッション・`serverActorData` の確認を通ると、直後に `_nativeContinueRestoreDeadline = Time.unscaledTime + 30f` を設定する。本体の有限な時刻を前提に、期限も有限になる。
- この設定は `GemContinueSources.Reset`、`BeginNativeContinueRestore`、チェックポイントの読み出しより前にある。キー不足やロビー帰還済みの判定で戻る場合も期限は設定済みで、チェックポイントのない旧保存も同じ経路を通る。
- 期限への代入はフィールドの初期値、上記の開始処理、`TickContinueRestore` の3か所だけにある。`TickContinueRestore` は期限前には戻り、期限に達すると `PositiveInfinity` を代入してから本体の復元状態を確認する。
- `FinishNativeContinueRestore` と放棄処理は待機フラグを `false` にする。次の読み込みでは `LoadNativeContinue` が期限を再設定する。したがって、待機フラグが `true` の間の `PositiveInfinity` は、現在の経路ではタイムアウト処理済みを表す。専用フラグは追加しない。

## 期限後の扱い

期限前にタイトルで同じ `null` を観測しても、放棄条件には入らない。期限切れの時点で `GameManager` がなければ、次の観測で待機を解除し、保持していたチェックポイントを復元対象から外す。保存は `Profile.ContinueCheckpoints` に残す。

タイトルからの読み込みが30秒を超え、期限切れの時点でも `GameManager` がない場合も同じ扱いになる。期限処理後に初めてゲームが現れる場合も、前回の観測が `null` なら放棄条件に入る。これは Issue #1031 の前提どおりで、遅れて現れたゲームへチェックポイントを適用する変更は行っていない。

## 検証

`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter FullyQualifiedName~ContinueSaveTests --nologo --verbosity minimal` を実行し、27件成功、失敗0件、スキップ0件だった。ビルド時に既存の警告が出た。

既存の #1031 の4件は、期限前の消失、消失と期限切れが同じ Tick になる順序、期限後の同じ `null` の再観測、チェックポイントを別の遠征へ適用せず保存を残す経路を扱う。通常の復元と、遷移中のタイムアウト後に完了通知が届く経路も同じテストクラスで確認した。テストの追加・変更はしていない。

未実行: ゲーム内でのタイトルからの読み込みと30秒を超える遅延。この環境ではゲームを起動できない。
未実行: MOD単体のゲームDLL参照ビルド、全件・低速テスト、生成データ検証。コメントと調査記録だけの変更で、既存の関連テストに絞った。

文書とコメントだけの変更のため、更新履歴の断片は追加していない。
