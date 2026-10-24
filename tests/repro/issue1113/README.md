# Issue #1113 の再現記録

2026-10-24、修正前の `24553f65` に再現テストだけを追加して実行した。製品コードは変更していない。

## 再現手順

対象は `tests/SodRpg.Core.Tests/Issue1113ReproTests.cs`。`LocExternal.Parse` に、言語コード `fr` と `strings` の `"Chain Sword": "Épée chaînée"` を含む JSON を渡し、外部言語を選択する。新規プロフィールの未発見の土台 `weapon.chain_sword` を対象に、`Txt.ToString()` と実際の行表示処理 `CodexPresenter.RowText` がフランス語の名前を返すことを確認してから、同じ名前で `CodexQuery.Filter` を呼ぶ。

独立した3ケースで、英語から外部言語への切替、ドイツ語の辞書からフランス語の辞書への切替、同じ `fr` の新しい辞書への差し替えを試した。再読込のケースは同じ言語コードの辞書を再解析して差し替えている。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --filter FullyQualifiedName~Issue1113ReproTests -p:PythonCommand=python3 --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue1113-before-fix-net10.trx' --results-directory /tmp/issue1113-repro-results
```

記録した実行では、先に通常の `dotnet test` でビルドし、上のコマンドに `--no-build --no-restore` を指定した。環境は Linux、SDK 10.0.111、ランタイム 10.0.11。既定の net8.0 テストホストは .NET 8 ランタイムがないため起動できず、終了コード 1 で中断した。その後、`DOTNET_ROLL_FORWARD=Major` を指定して既存のランタイムで実行した。`NUGET_PACKAGES` は既存の `/home/wang/.cache/gh-auto/nuget` を使用した。

## 修正前の結果

再現テストは全8件中、3件失敗、5件成功。終了コードは 1。

- `Displayed_external_base_name_remains_in_search_results` の `initial`・`switch`・`reload` が、最後の `Assert.Contains` で失敗した。失敗内容は `Assert.Contains() Failure: Filter not matched in collection`、結果は `Collection: []`。
- 失敗する前の検証で、表示名と行表示に `Épée chaînée` が含まれること、日英中の各名前では該当する土台が残ること、切替・差し替えで `Loc.CacheKey` が変わることを確認した。前の辞書の名前で該当する土台が残らない検証も通った。ただし、修正前は外部訳そのものが検索対象に入らないため、この検証だけでは修正後の古い訳の除去を保証できない。
- 未発見の固有品・セット・固有効果・銘品・組を外部訳で検索する5ケースは成功した。実際の行表示でも訳が隠れることを確認した。修正後も再実行する必要がある。

既存の `CodexQueryTests` と `Issue1024ReproTests` も絞り込み実行し、全8件成功、終了コード 0 を確認した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CodexQueryTests|FullyQualifiedName~Issue1024ReproTests' --logger 'console;verbosity=normal'
```

実行ログは `/tmp/issue1113-before-fix-net10.log`、TRX は `/tmp/issue1113-repro-results/issue1113-before-fix-net10.trx`。既存テストのログは `/tmp/issue1113-existing-tests.log`。

## コード上の根拠

- `src/SodRpg.Core/Game/Content.cs` の `Txt.ToString()` と `Loc.Zh()` は外部辞書の訳を返す。
- `src/SodRpg.Core/Game/CodexQuery.cs` の `AppendName` は日英中の名前だけを検索文字列に追加する。`Filter` は部分一致しない項目を除外する。今回の3件は表示が翻訳済みでも検索文字列にその訳がないため失敗した。
- 同ファイルの `Filter` は `found || e.Category == CodexCategory.Bases` を検索条件としている。未発見の秘密項目は `SearchText` の評価前に除外される。
- `src/SodRpg.Core/Game/LocExternal.cs` は辞書の生成ごとに新しいキャッシュキーを割り当て、`Content.cs` の `Loc.CacheKey` がその値を返す。`CodexEntry.SearchText` はこのキーが変わると検索文字列を作り直す。同じ `fr` の辞書を差し替えるケースでもキーが変わったため、同じ言語コードという理由でキャッシュキーが据え置かれる問題は再現していない。
- `src/SodRpg.Mod/ExternalLanguages.cs` の `Reload` は `LocExternal.Load` で辞書を読み直し、`Select` は選んだ辞書を `Loc.External` に設定する。この経路はコードを確認しただけで、実行していない。

## 未実行

- Unity・ゲーム実機での表示・検索・言語切替。環境でゲームを起動できないため。
- 実ファイル `fr.json` の読み書きと MOD の `ExternalLanguages.Reload` の実行。今回は Core の JSON 解析と辞書差し替えで再現したため。
- .NET 8 ランタイムでのテスト実行。ランタイムが未導入のため。
- 全テスト。依頼に従い、関連するテストだけを実行したため。
- 修正後の検証。製品コードの修正は次の工程が担当するため。

## 修正工程の検証

2026-10-24、実装工程でも修正前の製品コードをビルドし、再現テストの3件失敗・5件成功を確認した。`CodexQuery.AppendName` に `name.ToString()` を追加すると、全8件が成功した。テストの追加・変更はしていない。

追加した1行を一時的に外してビルドし直すと、`initial`・`switch`・`reload` の3件が再び68行目で失敗した。修正を戻してビルドすると全8件が成功した。これにより、修正を一時的に戻すと再現テストが失敗し、戻すと成功することを確認した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --no-restore -p:PythonCommand=python3 --filter FullyQualifiedName~Issue1113ReproTests --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue1113-implementation-restored.trx' --results-directory /tmp/issue1113-implementation-results -v:q -clp:ErrorsOnly
```

修正後はフランス語の表示名で `weapon.chain_sword` が残る。英語から外部言語への切替、別の外部辞書への切替、同じ `fr` の辞書を再解析して差し替える3ケースで成功した。日英中の検索名が使え、切替前の訳が残らず、未発見の固有品・セット・固有効果・銘品・組は外部訳で検索しても出ないことも確認した。

既存の `CodexQueryTests`・`Issue1024ReproTests`・`CodexNamedCategoriesTests`・`CodexTextConsistencyTests` は全13件成功、終了コード0。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CodexQueryTests|FullyQualifiedName~Issue1024ReproTests|FullyQualifiedName~CodexNamedCategoriesTests|FullyQualifiedName~CodexTextConsistencyTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue1113-existing-codex.trx' --results-directory /tmp/issue1113-implementation-results
```

TRX は `/tmp/issue1113-implementation-results/` に記録した。

- `issue1113-implementation-before.trx`、`issue1113-implementation-before-confirm.trx`: 修正前。3件失敗・5件成功、終了コード1。後者は指定された `--no-build --no-restore` のコマンドでも確認した記録。
- `issue1113-implementation-fixed.trx`: 最初の修正後。8件成功、終了コード0。
- `issue1113-implementation-reverted.trx`: 修正を一時的に外した状態。3件失敗・5件成功、終了コード1。
- `issue1113-implementation-restored.trx`: 修正を戻した状態。8件成功、終了コード0。
- `issue1113-existing-codex.trx`: 既存の関連テスト。13件成功、終了コード0。

### 秘匿と再読込の確認

`CodexQuery.Filter` の `searchable` 判定は変えていない。未発見の秘密項目は `SearchText` を評価する前に除外されるため、表示名を加えても検索から漏れない。

`LocExternal` は生成するたびに新しい `CacheKey` を割り当てる。`Loc.CacheKey` がその値を返し、`CodexEntry.SearchText` はキーが変わると作り直される。MOD 側も `DreamforgeMod.OnConfigChanged` で `ExternalLanguages.Reload` のあとに `config.ResolveLanguage(ExternalLanguages.Select)` を呼び、新しく読み込んだ辞書を選び直している。同じ言語で再読込しても古いキーが残る問題はコード上で見つからず、キャッシュ処理の変更は不要と判断した。

### 実装工程での未実行

- Unity・ゲーム実機での表示・検索・言語切替。ゲームを起動できない環境のため。
- 実ファイル `fr.json` の読込と MOD の `ExternalLanguages.Reload` の実行。Core の JSON 解析・辞書差し替えと行表示の既存テストで確認し、MOD の呼出経路はコードを読んで確認したため。
- .NET 8 ランタイムでの実行。未導入のため、`DOTNET_ROLL_FORWARD=Major` で導入済みの .NET 10.0.11 を使った。
- 全テスト・遅いテスト・MOD 全体のゲーム DLL を参照するビルド。依頼の範囲に従い、変更に関係するテストとそのビルドだけを実行したため。

## 追加コメントを受けた再確認

2026-10-24、[追加コメント](https://github.com/Kling0012/shape-of-dreams-rpg-plan/pull/1116#issuecomment-6378653033)と PR 全体の差分を確認した。コメントは既存コミットの CI で plan と shard 1〜7 が成功したという通知で、仕様変更や追加の修正要求はなかった。今回は製品コードとテストを変更せず、再確認の結果を追記した。

受け付け審査の懸念も再確認した。`CodexQuery.cs:448` の秘匿判定は `SearchText` の評価前に適用される。辞書は `LocExternal.cs:18` で生成ごとに新しいキーを持ち、`Content.cs:57` と `CodexQuery.cs:48` がそのキーで検索キャッシュを作り直す。`DreamforgeMod.cs:128` の再読込後は129行目の `ResolveLanguage(ExternalLanguages.Select)` で新しい辞書を選び直す。秘匿と同じ言語での再読込に追加修正は不要と判断した。

このラウンドで既存の再現テスト8件と関連テスト13件を実行し、すべて成功した。表示名・行表示が `Épée chaînée` になることを確認してから同じ名前で検索する3ケース、日英中の検索、古い訳の除去、未発見5カテゴリの秘匿を確認した。

さらに `AppendName` の表示名を追加する1行を一時的に外してビルドすると、`initial`・`switch`・`reload` の3件が68行目の `Assert.Contains` で失敗し、検索結果は空になった。残る5件は成功した。1行を戻してビルドし、再現テストと関連テストを合わせた21件がすべて成功した。製品コードには元のコミットからの差分がない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj --no-restore -p:PythonCommand=python3 --filter 'FullyQualifiedName~Issue1113ReproTests|FullyQualifiedName~CodexQueryTests|FullyQualifiedName~Issue1024ReproTests|FullyQualifiedName~CodexNamedCategoriesTests|FullyQualifiedName~CodexTextConsistencyTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue1113-revision-restored.trx' --results-directory /tmp/issue1113-revision-results -v:q -clp:ErrorsOnly
```

TRX は `/tmp/issue1113-revision-results/` に記録した。

- `issue1113-revision-repro.trx`: 最初の再現テスト8件成功、終了コード0。
- `issue1113-revision-existing.trx`: 関連テスト13件成功、終了コード0。
- `issue1113-revision-reverted.trx`: 表示名の追加を一時的に外した状態。3件失敗・5件成功、終了コード1。
- `issue1113-revision-restored.trx`: 元の修正を戻した状態。21件成功、終了コード0。

### このラウンドの未実行

- Unity・ゲーム実機での表示・検索・言語切替・再読込。ゲームを起動できない環境のため。
- 実ファイル `fr.json` の読込と MOD の `ExternalLanguages.Reload` の実行。Core の JSON 解析・辞書差し替えと行表示を既存テストで確認し、MOD の呼出経路はコードで確認したため。
- .NET 8 ランタイムでの実行。未導入のため、導入済みの .NET 10.0.11 で実行した。
- 全テスト・遅いテスト・ゲーム DLL を参照する MOD 全体のビルド。依頼に従い、関連テストとそのビルドだけを実行したため。
- GitHub Actions の再実行。追加コメントの既存 CI 結果を確認し、このラウンドではローカルの関連テストを実行したため。
