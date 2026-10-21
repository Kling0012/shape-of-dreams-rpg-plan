# Issue #1028 再現記録

形式7のコードで保存した協力取引の受領書を現行コードで読むと、台帳の初期化に失敗することを再現した。製品コードは変更していない。

## 保存データ

形式8を導入した `ffecc2ac` の親コミット `4e69ed52b5e5231c83b2f467be70a18fed13802a` から、未変更の Core の C# ソースを取り出して生成した。現行プロフィールの版番号だけを7に置き換えたデータではない。

生成器は `FixtureGenerator.cs`、生成手順は `generate_fixture.py` にある。生成済みのファイルは `tests/SodRpg.Core.Tests/Fixtures/CoopTradeV7/` に保存した。生成器は保存データが既にある場合に上書きを拒否する。

- `v7-journal.json`: 取引ID `10280000000000000000000000000001` の受領書を1件だけ含む台帳。
- `v7-first-escrow.json` / `v7-second-escrow.json`: 同じ取引を予約した直後のプロフィール。
- `v7-first-completed.json` / `v7-second-completed.json`: その取引の確定後に保存したプロフィール。双方の予約は解除済み。

ホストは `host-key`、参加者は `player-a` と `player-b`。seed は17と19で、双方にコモンの遺物を1個ずつ、恒久素材を100／50 shards、鞄の素材を各9 shards／3 tuning持たせた。提示したのは各自の遺物と20／7 shards。`default` の旅人と、遠征 `run` の Continue チェックポイントも含む。

生成時に形式7のコードで台帳を開き直し、双方の確定が1回だけ成功することを確認した。確定後の恒久素材は87／63 shardsで、双方の予約は解除されていた。同じ受領書による再確定は双方とも `false` だった。

## 実行環境

2026-10-21、Linux、ブランチ `ai/issue-1028`、製品コードの基点 `0e074ca2` で実行した。SDKは10.0.111、ランタイムは Microsoft.NETCore.App 10.0.11。既存のテストプロジェクトはnet8.0なので、`DOTNET_ROLL_FORWARD=Major` を指定した。NuGetキャッシュは環境に設定済みのworktree外のパスを使用した。

## 再現コマンド

リポジトリ直下から実行する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter FullyQualifiedName~CoopTradeVersionSevenReproTests --logger 'trx;LogFileName=core-v7-before-fix.trx' --results-directory .ci-logs/issue1028 --nologo

DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter FullyQualifiedName~Version_seven_history_allows_host_entry_and_recovery --logger 'trx;LogFileName=native-v7-before-fix.trx' --results-directory .ci-logs/issue1028 --nologo

DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --no-build --no-restore --filter FullyQualifiedName~CoopTradeMigrationTests --logger 'trx;LogFileName=legacy-v6-controls.trx' --results-directory .ci-logs/issue1028 --nologo
```

## 修正前の結果

| 対象 | 終了コード | 結果 |
| --- | --- | --- |
| 形式7のCore再現 | 1 | 3件失敗、成功0件 |
| ホストの受信処理 | 1 | 形式7の6ケースが失敗、形式8の対照6ケースが成功 |
| 既存の移行・拒否検証 | 0 | 13件成功、失敗0件 |

Coreでは、元の形式番号が7のまま、警告なしでプロフィールを読めることを確認した。その後の `WriteCoopTradeReceiptProfile(profile.Clone())` が `null` を返し、元文字列との一致検証が失敗した。

完了済みプロフィールと受領書を含む台帳を使うケースは、`CoopTradeJournal` の構築時に `ReadFrozenProfile` の `InvalidOperationException` で失敗した。予約中プロフィールを読み直して確定するケースも、同じ受領書検証で失敗した。

ホスト側のテストは既存の NativePersistence.targets が抽出する本物の `ReceiveCoopTrade` と `EnsureCoopJournal` を使う。probe、保存済み受領書のquery、確定後のcancel、新規request、未決定IDのqueryとcancelの6ケースで、期待する応答が得られなかった。同じ操作を形式8の受領書で実行した6ケースはすべて成功した。

初回の台帳読込に失敗すると `_coopHostPeers` に登録される前に戻る。`SendCoopState` は登録済みの参加者だけに送るため、今回の初回接続では `ownership` のエラー応答も観測できず、受信一覧は空だった。形式7を読む際の失敗は `CoopTradeJournal.cs:32` → `CoopTrade.cs:362` → `CoopTrade.cs:377` で確認できる。

実行ログとTRXは `.ci-logs/issue1028*` に保存した。このディレクトリはgitの対象外で、再実行時は上のコマンドで結果を作り直せる。

## 次工程への引き継ぎ

形式7の読み込みを無条件に許可したり、固定されたプロフィール文字列を書き換えたりする変更はしていない。不正データ拒否については既存の形式6テストが成功したことまでを確認した。形式7に対する厳密な拒否検証は修正後の確認が必要。

台帳の初期化失敗が新規受付と回復経路の入口を止めることを再現した。予約の解除・確定の二重実行や、予約の取り残しが解消するかは、この工程では確認できていない。製品コードの修正は次工程に残す。

未実行: ゲーム内での更新・取引操作。ゲームを起動できないため、旧コードで生成した保存データと既存のホスト検証環境を使った。

未実行: 修正後の形式6・7・8混在台帳の読込、形式7の改ざん拒否、取消による資産返却、確定後の再起動・再照会までの一連の確認。修正前の台帳初期化・受領書検証で失敗するため、次工程に残した。

未実行: 全テスト、MOD全体のビルド、実ゲームDLLを使った検証。今回の工程は不具合の再現で、対象をCoreの3件、ホスト受信処理の12ケース、既存の移行検証13件に絞った。
