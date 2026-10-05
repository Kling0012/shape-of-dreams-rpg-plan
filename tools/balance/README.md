# バランス外部定義・比較（Issue149 stage0）

対象は鍛冶の強化失敗率の3係数だけです。`forge.json` の
`currentLevelOffset` / `percentPerLevel` / `maximumPercent` を C# 定数へ生成します。
初期値は `2 / 3 / 45`。数値バランス、セーブ形式、Protocol、既存モードは変更しません。
ゲーム起動時のJSON読込・ファイルハッシュ・追加の必須ファイルはありません。

## コマンド

Python 3 と .NET SDK が必要です。`DOTNET` を指定しない場合は PATH の `dotnet` を使います。
.NET 8 ランタイムがない環境では、利用できる上位ランタイムへの移行を必要に応じて
呼出側で `DOTNET_ROLL_FORWARD=LatestMajor` に指定してください。ツールはこの設定を変更しません。

```sh
# 通常の全検証＋比較（低速テストは自動では実行しない）
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/balance/run

# 既存の低速テストも追加
python tools/balance/run --slow

# 明示的なスモーク／レポート専用。検証済みという扱いにはしない
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/balance/run --no-tests

# 一時ディレクトリで小規模に計測。相対出力先は呼出時の作業ディレクトリ基準
python tools/balance/run --no-tests --out-dir /tmp/sod-balance-smoke --runs 2 --players 4 --seed 1

# 生成と、書込なしの鮮度確認
python tools/balance/gen_cs.py
python tools/balance/gen_cs.py --check
```

`run` は実行可能です。任意の作業ディレクトリから絶対パスでも呼び出せます。
入力検証→生成→生成鮮度確認→BalanceSim Releaseビルド（1回）→
`python tools/test_changed.py --all`（`--slow` 時は同オプション追加）→
`python -m unittest discover -s tools/tests -p 'test_*.py'`→鍛冶→遠征→比較の順です。
`--no-tests` は2つのテスト実行を省略し、生成・ビルド・実測は実行します。
鍛冶と遠征は別プロセスで起動し、Coreの静的状態を共有しません。
既定の遠征は新規プロフィール3遠征×8人、seed=1、2ゾーン×2部屋、その他は既存の遠征モード既定です。
500pt星図の長時間シミュレーションは実行しません。

## 出力と前回成功基準

既定の出力先は `tools/balance/results/`（gitignore対象）。成功した実行ごとに
日時付きディレクトリへ次を保存します。

- `forge.md` / `forge.json`: 全レア度・合法な限界突破回数・現在強化値の一覧。
- `expeditions.md` / `expeditions.json`: 既存Simulationの遠征・節目・容量計測。
- `current.json`: 両モードのスナップショット、検証状態、日時、生成入力の `tableMetadata`（schemaVersionと3係数）、既存Gitコミットの `gitRevision`（取得不能時null、比較表示はunknown）。
- `comparison.md`: 現在／前回成功／差／相対差。

検証済み基準は `last-success.json`。`--no-tests` は独立した
`last-report-only.json` を参照・更新し、検証済み基準を置換しません。
それぞれ初回は「前回なし」、その成功結果を次回の基準とします。
全必須ステップ成功後に一時ファイルを置換して基準を原子的に更新します。
生成、ビルド、テスト、いずれかの計測、レポート書込に失敗すれば以前の基準を維持します。
`--out-dir` を変えれば基準も独立します。

JSONの `modelVersion` と正規化 `conditions` が異なるモードは比較不可として現在値だけを表示します。
ContentFingerprintの変更は調整による差を比較する目的なので、比較不可の理由にしません。
`tableMetadata` は比較互換条件に含めず、調整前後の定義を追跡するために表示します。
`conditions` には実測した.NETランタイム（framework/version/OS/processArchitecture）と、
`RegisterAllGenerated` 後の実登録ツリーから取得・重複排除・ソートした `registeredHeroes` を含めます。
登録候補カタログではなく `HeroSigils.All` の旅人を `TryGetRegisteredTree` で確認します。
ランタイム／登録集合の違いは比較不可です。Git revisionは由来の記録で、互換条件にはしません。
Git取得は開発ツールのみで行い、ゲーム起動の依存を追加しません。
指標は安定IDの和集合で照合し、消えた行は現在「削除」、増えた行は前回「追加/なし」、
いずれも架空の0や差分を表示しません。
相対差は `(現在−前回)/前回 ×100%`。前回0の場合は `—`、未到達の節目は `null`／「未到達」で、
架空の0遠征として扱いません。同条件・同係数で2回成功すれば測定値の差は0です。
失敗率 (%) の絶対差は percentage points (`pp`)、相対差は `%` です。
未到達割合は0〜1のfractionで、差もfractionのまま表示します。

## 実測JSONの契約

BalanceSim単体でも利用できます（先にビルドしてください）。

```sh
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode forge --metrics-json /tmp/forge.json
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode expeditions --runs 2 --players 4 --metrics-json /tmp/expeditions.json
```

JSONは `modelVersion: 1`、`mode`、`contentFingerprint`、`conditions`、比較用 `metrics` を持ちます。
`metrics` は安定した `id`、表示用 `label`、未丸めの `value`、`status`（`measured` / `cap` / `not-reached`）。
System.Text.Jsonで直接数値を書き出し、Markdownの丸め値や文言を逆解析しません。

鍛冶の `forge` 配列は `rarity`、`limitBreaks`、`currentEnhance`、`maxEnhance`、`isCap`、
`failurePercent`、`baseShardCost`。失敗率は `Rules.EnhanceFailureChance`、基本欠片は
`Content.EnhanceCost` の実際の戻り値です。上限は失敗率0でも `isCap: true` かつ `status: cap`、
次の強化費用は `null`（強化不可）。基本欠片は実際のエピック以上の支払い額の2倍補正前です。
遠征は `samples` のプレイヤー順生値、`milestones.reachedAt`（未到達はnull）、`capacity.values` と、
その平均／線形補間の中央値・P10・P90等を出力します。Coreの鍛冶式を複製しません。
`--metrics-json` は forge / expeditions のみ対応し、他モードは重い計測前に引数エラーになります。
JSON出力を指定しない既存モードの挙動は維持します（setsの既存問題はstage0対象外）。

星図登録後のContentFingerprintは旧係数 `2 / 3 / 45` で `10575-81dd841870386d98` のままです。
旧係数の組は凍結した互換情報であり、編集可能な既定値ではありません。
係数を変更すると生成された正規化レコードがContentFingerprintへ追加され、識別値が変わります。

既存の鍛冶期待値テストは原本 `forge.json` から独立計算する期待値へ移行し、実際の失敗率・固定seedでの成功失敗結果を検証します。
これは安全性、形式、契約の検証を緩めたり、任意の係数で全テストが常に成功すると保証したりするものではありません。
本レポートは実際のCore呼出と固定条件のシミュレーションであり、実ゲームの勝率や経済の保証ではありません。
