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
