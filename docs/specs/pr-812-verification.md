# PR #812 の検証記録

## Lab 検証の参照エラー

2026-10-19 の修正ラウンドで、PR のコミット `448e729b` を Lab へ送る検証ツールを実行した。生成コード検査を指定しても、検査の開始前に終了コード128で停止した。

```bash
/home/wang/.claude/bin/ghauto-remote-test \
  --repo "$PWD" --rev HEAD --job pr812-reproduce-gen-20261019 \
  -- python3 tools/balance/gen_cs.py --check
```

出力は `fatal: Needed a single revision` だった。`ghauto-remote-test` は送信するコミットとは別に、比較元として `origin/HEAD` のコミットを解決する。この作業環境の `origin/HEAD` は、削除済みの `origin/main` を指していた。`git ls-remote --symref origin HEAD refs/heads/dev refs/heads/main` と GitHub のリポジトリ情報では、現在の既定ブランチは `dev` だった。

同じリポジトリで `git remote set-head origin -a` を実行し、`origin/HEAD` を取得済みの `origin/dev` へ更新した。その後、同じ検証ツール経由で生成コード検査と Release ビルドが成功した。今回の参照エラーへの対応として、製品コードや検証ツールの実装は変更していない。

## 実行した検証

以下はすべて `448e729b` を同じ検証ツールで Lab に送り、実行した結果である。NuGet キャッシュは worktree 外の `/gh-auto-cache/nuget` を使った。依存関係の復元後、検証はネットワークのないコンテナ内で実行した。

- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/mnt/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/gh-auto-moddeploy`：終了コード0、エラー0件、警告6件。`GameDir` は検証ツールが使う Lab コンテナ内のマウント先を指定した。
- 下記の既存テスト：Native 71件、Core 21件の計92件成功、失敗0件、スキップ0件。`DOTNET_ROLL_FORWARD=LatestMajor` を指定した。

```bash
dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~EnemyOwnershipTests|FullyQualifiedName~Issue704InfinityHelperDecayTests|FullyQualifiedName~DynamicDifficultyTests|FullyQualifiedName~CoopTradeClientTests|FullyQualifiedName~CoopTradeSafeTests|FullyQualifiedName~MechanismDispatchBufferReturn|FullyQualifiedName~BossSharesTests|FullyQualifiedName~Host_reasons_carry_a_code_each_player_reads_in_their_own_language' \
  --nologo --verbosity quiet -clp:ErrorsOnly

dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~RunWorldOwnershipTests|FullyQualifiedName~CoopTradeReasonTests' \
  --nologo --verbosity quiet -clp:ErrorsOnly
```

`python3 tools/test_changed.py --list` も Lab で実行した。比較元は PR のベースと同じ `7334173a` で、変更5ファイルから580クラスが選択された。実装工程では変更箇所に絞る指定に従い、上記フィルターで検証した。

## 未実行

- 引数なしの `python3 tools/test_changed.py` によるテスト実行：580クラスを選ぶため、この工程では実行せず、後段の動作確認に委ねる。`--list` はテストの成功として扱わない。
- 全件・低速テスト：実装工程では実行しない指定のため未実行。
- 世界順の観察で例外を発生させて次の遠征へ進む直接再現：該当する既存テストがなく、新規テストを作らない指定のため未実行。遠征ID変更時に失敗状態を解除する経路はコードで確認した。
- ゲーム内の遠征開始・ゾーン移動・取引警告と実メモリ確保量：この環境ではゲームを起動できないため未実行。
- GitHub Actions の CI：このラウンドでは起動・実行をしていない。
- 難読化・配布・実ゲームへのデプロイ：担当範囲外のため未実行。ビルドの配置先には `/tmp/gh-auto-moddeploy` を指定した。
