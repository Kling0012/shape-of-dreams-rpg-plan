# 外部の旅人MODとDreamforgeの連携

[English](MODDING.md) | [ヒーローコネクタの仕様](specs/hero-connector.md)

外部の旅人MODは、MODフォルダの `dreamforge.json` から名前・記憶・星図を宣言できます。旅人のプレハブと記憶の実装は、旅人MODが用意してください。Dreamforgeのヒーローコネクタを有効にし、宣言を変えたらゲームを再起動します。

## 宣言ファイル

`dreamforgeApi` は1です。`starManifest` はMODフォルダからの相対パスで、下の例では同じフォルダの `stars.json` を読みます。`starTree` は、マニフェストが読めない場合の代わりの星図です。`starManifest` を省いた既存の宣言も引き続き使えます。

```json
{
  "dreamforgeApi": 1,
  "heroes": [{
    "key": "Hero_Nova",
    "name": { "ja": "ノヴァ", "en": "Nova", "zh": "诺瓦" },
    "melee": false,
    "memories": [{
      "key": "St_Q_NovaBlink",
      "name": { "ja": "瞬き", "en": "Blink", "zh": "闪烁" }
    }],
    "starManifest": "stars.json",
    "starTree": {
      "nodes": [{ "id": "fallback", "stat": "AttackPct", "perRank": 1, "maxRank": 1 }]
    }
  }]
}
```

APIから登録する場合も、既存の `Dreamforge.Api.HeroConnector.RegisterHeroes` を使います。`sourceModId` は、読み込まれた旅人MODのIDに合わせてください。参照ファイルはそのMODのフォルダから読みます。遠征中の登録は遠征終了まで保留されます。

## 星図ファイル

[正準JSONの形式](../tools/star-manifest/README.md)を使います。以下は `stars.json` の1星の例です。実値は本体9人の同種の星から求める上限で検証されます。

```json
{
  "hero": "Hero_Nova",
  "source": "Nova MOD",
  "stars": [{
    "id": "blink.focus",
    "region": "memory",
    "cluster": "blink",
    "shape": "chain",
    "anchor": "route.St_Q_NovaBlink.1",
    "edges": [],
    "requires": [],
    "requiresAny": [],
    "kind": "MemoryDamage",
    "memory": "St_Q_NovaBlink",
    "value": 1,
    "param": null,
    "receiver": null,
    "target": null,
    "gimmick": null,
    "power": null,
    "stat": null,
    "options": [],
    "keystone": null,
    "mechanisms": [],
    "maxRank": 1,
    "rankCost": 1,
    "nameJa": "瞬きの冴え",
    "nameEn": "Focused Blink",
    "nameZh": "专注闪烁",
    "notes": ""
  }]
}
```

`hero` は宣言の `key` と一致させます。短いIDには `x.Hero_Nova.` が付き、この例の保存IDは `x.Hero_Nova.blink.focus` です。`h.` と別旅人の `x.` は予約されており、既存星や自動ルートを上書きできません。同じIDが複数あると、先の定義を優先します。

宣言した記憶ごとに7星のルートが自動でできます。IDは `x.<旅人キー>.route.<記憶ID>.1` から `.7` です。最初の6星は各3段・各段1ポイント、最後は1段・3ポイントで、本体ルートの構成を使います。効果は本体の登録済み通常星にある正のMaxHealthPctの最小値です。ルートの数値と費用は指定できません。

`memory` 領域の入口は、このルートの星を `anchor` に指定します。星群のほかの星は入口の配置を継承でき、接続は `edges` に書きます。辺は同じ星群の両側に記載してください。`requires` はすべて、`requiresAny` はいずれか1件の取得を必要とします。`bridge` は受け手の記憶を持つ橋、`outer` は外縁、`keystone` は刻印です。外縁はルート・記憶領域・効果を持つStat根から入れます。

## 効果と制約

- 記憶と受け手、選択子の候補は、その旅人の `memories` で宣言したものに限ります。本体の記憶も、使うなら宣言してください。`@Q` などの候補を省くと、宣言記憶に限定されます。
- MemoryDamage、MemoryHaste、GimmickBoost、GimmickParam、Notable、Choice、Stat、Keystoneを扱います。通常星は1段・1ポイントです。Choiceは2択で、入れ子とKeystoneの選択肢は使えません。Statは外縁に置きます。
- Notableは通常Gimmick、DirectedRecharge、MemoryPrimed、AlliedWardを扱います。Keystoneは型付きの変換とGrantを扱います。専用ペア連携、旅人別スケール・RunGrowth、Power、固有記憶のnative adapter、刻印の不利益効果は対象外です。
- 受け手の修飾は `memory: null` と `receiver` を指定します。送り手を限定する受信修飾は扱いません。仕掛け全体への修飾は、対応する実際の仕掛けへ解決します。対応先が無い修飾は除外します。
- 値は実値か `valueRef` で指定します。参照先は本体の登録済み星です。トップレベルの `value`、Choiceの `options/N/value`、`stat/perRank`、仕掛けの `value`・`arg`・`cooldown`・`everyN`・`valuesByRank/N`、Grantを含まない刻印の `upsideSpec/N` の数値欄を解決します。解決できないパスは除外理由になります。Power、legacy native原本、混在Grantの参照は扱いません。
- MemoryDamageは0.001刻みです。整数欄には整数を使います。本体の同種の値・トリガー・対象・間隔などを上限とし、実行時に切り詰められる入力は拒否します。刻印内の同種の変換・Grantの合計も検証します。
- 自動ルートを含む星数は `floor(本体最多の星数 × 1.2)` までです。通常星の追加でポイントの供給は増えません。通信・効果チャネルの容量も本体と共通です。
- 各ファイルは4 MiBまで、JSONの深さは64までです。キー重複、絶対パス、`.`、`..`、空のパス要素、シンボリックリンクは使えません。

## 診断と保存

不正な星は理由を警告して除外し、参照切れも順に除外します。ファイルが読めない、JSON全体が不正、採用星が0件の場合は、その旅人の `starTree` に戻ります。それも使えなければ汎用ツリーになります。警告は `Player.log` の `[Dreamforge/Connector]` を確認してください。

ゲームを起動せずに登録を確かめるには、リポジトリの診断コマンドを使えます。

```sh
dotnet run --project tools/hero-connector -- --manifest "<旅人MODのフォルダ>"
```

出力の `Authored: true` は監修星図の登録、`Stars` は自動ルートを含む星数です。終了コード0でも `starTree` に戻っている場合があるため、警告と `Authored` を確認してください。

購入状態・二択・刻印・ロードアウトは既存の保存形式を使います。旅人MODを外すと、その星の記録は休眠して効果を持ちません。記録は残り、同じIDの星図を戻すと再び使えます。別旅人の割り当てには使いません。

協力プレイでは、宣言と正規化した採用星・自動ルートを宣言ハッシュに含めます。除外した星の内容は含めません。参加者とホストで旅人MODと星図をそろえてください。
