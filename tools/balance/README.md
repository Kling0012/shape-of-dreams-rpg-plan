# バランス外部定義・比較（Issue149 段階0・1）

`forge.json` の強化失敗率3係数と、`stars.json` の `MemoryDamage` 基準値・倍率を
型付きC#へ生成します。記憶ダメージはmanifestの480親＋258選択肢と旧ルート98成分、
計836欄を移行済みです。基準値と初期倍率1は従来どおりで、数値バランス、
保存形式・Protocol・内容指紋は変更しません。MemoryHasteや仕掛けの調整数値は対象外です。

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
`python -m unittest discover -s tools/tests -p 'test_*.py'`→鍛冶→記憶効率→遠征→比較の順です。
`--no-tests` は2つのテスト実行を省略し、生成・ビルド・実測は実行します。
3モードは別プロセスで起動し、Coreの静的状態を共有しません。
既定の遠征は新規プロフィール3遠征×8人、seed=1、2ゾーン×2部屋、その他は既存の遠征モード既定です。
500pt星図の長時間シミュレーションは実行しません。

## 記憶ダメージの調整

調整するのは `stars.json` の1セルです。旅人別の例：

```json
"byHero": {
  "Hero_Yubar": { "MemoryDamage": 1.10 }
}
```

- `effects` のキーは `<星ID>/value`、Choice子は `<星ID>/options/0/value` または
  `options/1/value`、旧ルートの記憶成分は `<旧星ID>/link/value`。
- 有効値は `base × byKind.MemoryDamage × byHero[Hero_*].MemoryDamage`。
  種類・旅人倍率の省略は1。全体調整は `byKind`、旅人だけの調整は `byHero`。
- 共有外縁（`outer.json`）は `byKind` だけを使います。旅人倍率は同旅人の
  固有星・旧ルートへ適用し、同IDの移行行がある場合は移行後の効果だけを採用します。
- `1.10` は相対10%増。4%は4.4%になります。10ポイントを足す指定ではありません。
  段数・費用・ID・前提・接続や、CapGに併存する仕掛けは変えません。
- manifestのMemoryDamage欄は `value` を持たず、`valueRef` で表を参照します。
  原本の併存、参照漏れ、余分な表キー、未知の種類・旅人を生成時に拒否します。
- PythonはDecimalで計算し、MemoryDamageを既存 `LinkDef.Value` → `ValueMilli` へ通します。
  有効値は正の0.001刻み、ValueMilliはInt32の範囲内。表現不能な結果は該当pathを示して失敗し、
  丸め・切捨て・上限拡大はしません。MemoryHaste、Stat、Power、旧仕掛けのAmountは整数のままです。
- `run` / `gen_cs.py --check` は鍛冶・星定数・全9星図・登録ファイルをまとめて扱います。
  全入力の検証・renderが成功してから変更のある生成物だけを書きます。
  `python tools/star-manifest/validate.py` と単体の星生成も同じ解決済み値を検証します。

ゲーム起動時にJSON・Python・ファイルハッシュは使いません。値は生成済み定数／星定義で、
戦闘中の倍率再計算や文字列キー検索はありません。MOD起動時の登録失敗は警告し、
その旅人の生成星図・移行規則だけを外します。開発ツール側は失敗を非ゼロで伝えます。

## 軽量な記憶効率

`star-efficiency` は実登録 `HeroSigils.TreeFor` の有効MemoryDamage Linkを列挙します。
記憶別の効果量は `Value × MaxRank`、費用はその効果星の `RankCost × MaxRank` の合計、
効率は `合計ダメージ% / 合計点`。接続専用の別効果星の費用は含めません。
CapGは星全体の費用を使い、分子には記憶ダメージ成分だけを入れます。

Choiceは全A（option 0）／全B（option 1）の2構成を別集計し、同記憶の2択も両方を足しません。
各記憶行はその構成の投影であり、異なる記憶行を合計した購入ビルドではありません。
混合Choiceの全探索や接続込みの実戦効率は測定しません。
`private` / `shared` / `total` を分け、旅人×構成×由来ごとの記憶効率min/maxも出します。
費用0の効率は `null`／`—`。このmin/maxは表示構成内の記憶間比較で、選択最適化の上下限ではありません。

## 出力と前回成功基準

既定の出力先は `tools/balance/results/`（gitignore対象）。成功した実行ごとに
日時付きディレクトリへ次を保存します。

- `forge.md` / `forge.json`: 全レア度・合法な限界突破回数・現在強化値の一覧。
- `star-efficiency.md` / `star-efficiency.json`: 旅人×記憶×全A/全B×由来の効果量・費用・%/点と旅人別min/max。
- `expeditions.md` / `expeditions.json`: 既存Simulationの遠征・節目・容量計測。
- `current.json`: 3モードのスナップショット、検証状態、日時、生成入力の `tableMetadata.forge`（schemaVersionと3係数）／`tableMetadata.stars`（schemaVersionと倍率）、既存Gitコミットの `gitRevision`（取得不能時null、比較表示はunknown）。
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
失敗率・記憶ダメージ (%) の絶対差は percentage points (`pp`)、効率の差は `%/点`、
相対差は `%` です。未到達割合は0〜1のfractionで、差もfractionのまま表示します。
小数は `Decimal` で読み込み・差分計算し、成功基準にもJSONの数値として保存します。
例えばユバール倍率を1→1.10にすると、全Aの `St_Q_SuperNova` は2.36→2.596 %/点（差0.236、+10%）、
共有の `St_C_FlashFreeze` は4→4 %/点です（既定表で実測）。

## 実測JSONの契約

BalanceSim単体でも利用できます（先にビルドしてください）。

```sh
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode forge --metrics-json /tmp/forge.json
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode star-efficiency --metrics-json /tmp/star-efficiency.json
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode expeditions --runs 2 --players 4 --metrics-json /tmp/expeditions.json
```

JSONは `modelVersion: 1`、`mode`、`contentFingerprint`、`conditions`、比較用 `metrics` を持ちます。
`metrics` は安定した `id`、表示用 `label`、未丸めの `value`、`status`（`measured` / `cap` / `not-reached`）。
System.Text.Jsonで直接数値を書き出し、Markdownの丸め値や文言を逆解析しません。

記憶効率の `metrics` はdecimalの値と `unit`（`percent` / `points` / `percent/point` / `nodes`）を持ち、
費用0の効率は `status: "no-damage-nodes"`。単位が異なる行の差は作りません。
`entries` に効果星ごとの `contributions`（星ID・選択肢番号・段あたり値・段数・費用）、
`configurations` にChoiceの選択、`choiceOptions` に両選択肢、`ranges` にmin/maxと該当記憶を保存します。

鍛冶の `forge` 配列は `rarity`、`limitBreaks`、`currentEnhance`、`maxEnhance`、`isCap`、
`failurePercent`、`baseShardCost`。失敗率は `Rules.EnhanceFailureChance`、基本欠片は
`Content.EnhanceCost` の実際の戻り値です。上限は失敗率0でも `isCap: true` かつ `status: cap`、
次の強化費用は `null`（強化不可）。基本欠片は実際のエピック以上の支払い額の2倍補正前です。
遠征は `samples` のプレイヤー順生値、`milestones.reachedAt`（未到達はnull）、`capacity.values` と、
その平均／線形補間の中央値・P10・P90等を出力します。Coreの鍛冶式を複製しません。
`--metrics-json` は forge / star-efficiency / expeditions に対応し、他モードは重い計測前に引数エラーになります。
JSON出力を指定しない既存モードの挙動は維持します（setsの既存問題はstage0対象外）。

初期表の星図登録後ContentFingerprintは `10575-81dd841870386d98` のままです。
初期の採用済み記憶成分の既存FNV-1a内容identityは凍結した互換情報で、編集可能な既定値ではありません。
有効数値を変更すると正準key/milliレコードがContentFingerprintへ追加されます。
相殺された基準値・倍率が同じ有効内容を作る場合や、移行行で置換された旧成分だけを変える場合は指紋を変えません。
既存の鍛冶期待値テストは原本 `forge.json` から独立計算する期待値へ移行し、実際の失敗率・固定seedでの成功失敗結果を検証します。
これは安全性、形式、契約の検証を緩めたり、任意の係数で全テストが常に成功すると保証したりするものではありません。
本レポートは実際のCore呼出と固定条件のシミュレーションであり、実ゲームの勝率や経済の保証ではありません。
