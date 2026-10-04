# テストガイド（tools/test_changed.py）

## 目的

Core スイート全体の実行には約 3 分かかる。開発中やエージェントの作業中に
「変更した部分に関係するテストだけ」を素早く回すためのツールとして
`tools/test_changed.py`（Python 3.12・標準ライブラリのみ）を用意している。

## 使い方

リポジトリルートから実行する。

```
python tools/test_changed.py [--base <git ref>] [--list] [--all]
```

| オプション | 動作 |
|---|---|
| なし | 変更に対応するテストクラスだけを `dotnet test` で実行する |
| `--base <git ref>` | 比較元を指定する（既定: `origin/main` との merge-base） |
| `--list` | 選択されたテストクラス（完全修飾名）だけを表示して終了する（テストは実行しない） |
| `--all` | フィルタなしでスイート全体を実行する |

終了コードは `dotnet test` の終了コード（フィルタを複数回に分割した場合は
最初の非ゼロ終了コード）。テストに関係する変更が一件もない場合は
「変更なし」と表示して 0 を返す。

## 対象の収集

既定の比較元は `git merge-base HEAD origin/main`。`origin/main` が取得できない
場合は `HEAD` にフォールバックする（＝コミット済み差分は無く、ステージ済み・
未ステージ・未追跡の変更だけが対象になる）。収集対象は次の 3 種。

1. `git diff --name-only <base>...HEAD`（ブランチ上のコミット済み変更）
2. `git diff --name-only HEAD`（ステージ済み＋未ステージの作業ツリー変更）
3. `git ls-files --others --exclude-standard` のうち `.cs` ファイル（未追跡）

## 変更ファイルからテストクラスへのマッピング

`tests/` 配下の全 `.cs` ファイル（`bin`/`obj` を除く）をテストファイルのコーパスとし、
変更ファイルを次の規則でテストクラス（完全修飾名）へ写す。

1. **テストファイル自体の変更** — `tests/` 配下の変更された `.cs` は、
   そのファイルが宣言するテストクラスを選択する。
2. **製品コードの変更** — `src/` 配下の変更された `.cs` が宣言する型名
   （class / struct / enum / interface / record。`partial` は 1 回だけ収集）を抽出し、
   その型名を単語として（部分一致ではなく）含むテストファイルのテストクラスを
   すべて選択する。
3. **テストが読む非コードファイルの変更** — `tools/star-manifest/*.json`、
   `tools/lowrarity/*.json`、生成済み `.cs`（`*.Generated.cs`、`Generated*.cs`）が
   変更された場合、そのパス（ディレクトリ名＋ファイル名、または相対パス）を
   参照しているテストファイル、および生成型を参照しているテストファイル（規則 2 と
   同じ方法）のテストクラスを選択する。
4. **全体実行へのエスカレーション** — 次の場合はスイート全体を実行する。
   - ビルドファイル（`*.csproj`、`Directory.Build.*`）が変更された
   - 変更ファイルの型名がテストファイルの 60% 超に登場する
     （＝どこでも使われている型への影響が広すぎるため）

どの規則にも当てはまらない変更（ドキュメント等）だけの場合は
「関係する変更なし」と表示して終了コード 0 で終わる。

## 実行方法

選択されたテストクラスから `dotnet test <project> --filter` 式を組み立てる。
フィルタは `FullyQualifiedName~名前空間.クラス` 条件を `|` で連結した 1 本の式で、
長すぎる場合（既定の上限 15000 文字）は複数回の `dotnet test` 実行に分割する。
テストプロジェクトは `tests/` 配下の `.csproj`（現在は `SodRpg.Core.Tests`）。

- `dotnet` コマンド: 既定では `%LOCALAPPDATA%\dotnet-sdk\dotnet.exe` を使う。
  環境変数 `DOTNET` に実行ファイルのパスがあればそれを優先する。
- `DOTNET_ROOT` は子プロセスに設定される（既定 `%LOCALAPPDATA%\dotnet-sdk`、
  `DOTNET` 指定時はそのディレクトリ）。
- `DOTNET_CLI_UI_LANGUAGE=en` を設定して実行する（結果サマリの解析のため）。
- 標準入力は閉じた状態（`/dev/null` 相当）で起動する。

実行後、選択クラス数と合計の passed / failed を表示する。

## 例

```
# 変更が影響するテストだけを実行
python tools/test_changed.py

# 選択されるテストクラスを確認するだけ
python tools/test_changed.py --list

# main 以外の分岐との比較
python tools/test_changed.py --base origin/release/v2.0.1

# 全テスト実行
python tools/test_changed.py --all
```

## #47 刻印の代償撤廃の回帰確認

- `GeneratedHeroAcceptanceTests` は全82刻印の型付き効果、ダメージ・傷の非弱体化、
  日英説明に「代償／Drawback」がないこと、通信版15のビルド往復を確認する。
- `StarCarryoverV131Tests` は従来の形式3/4互換に加え、v1.31移行済みの形式4で
  保存した刻印IDが現在の生成定義に解決され、保持するPowerが引き継がれることを確認する。
  v1.31未移行の保存には、別仕様の効果変更時返却が引き続き適用される。
- C11のHP支払い→障壁変換、C15の装備・選択変更の明示承認は既存テストを維持する。
- テストクラス間の並列実行は `AssemblyInfo.cs` の設定で無効。

## マッピングロジックの単体テスト

`tools/tests/test_test_changed.py`（unittest）がマッピングロジックの単体テストを
持つ。実際の git / dotnet は使わず、一時ディレクトリに小さなダミーリポジトリを
作って検証する。

```
python -m unittest discover -s tools/tests -p "test_test_changed.py"
```

## 制限

- 規則 2 は `src/` 配下の `.cs` のみが対象。`tools/BalanceSim` 等の他の製品コードの
  変更は、テスト側で対応するテストファイルを同時に変更しない限り選択されない。
- 型名・パスの抽出は正規表現ベースなので、コメント内の文字列は無視されるが、
  文字列リテラル内の `class Xxx` という形は誤検出される可能性がある
  （誤検出はテストの過剰選択としてのみ現れ、欠落にはならない）。
