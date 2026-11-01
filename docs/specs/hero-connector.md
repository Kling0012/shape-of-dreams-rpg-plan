# ヒーローコネクタ 仕様（apiVersion 1）

2026-10-30。前提の調査は [hero-connector-survey.md](hero-connector-survey.md)。

## 1. 目的

ほかの開発者が作るMOD、特に新しい旅人（Hero のサブクラス）を足すキャラクターMODが、Dreamforge と簡単につながるようにする。

- つなぐ側の作業は、自分のMODフォルダにJSONを1つ置くだけで済む。C# のコードも、Dreamforge へのビルド時の参照も要らない。
- Dreamforge が入っていない環境でも、つなぐ側のMODはそのまま動く（JSONは読まれないだけ）。
- MODの読み込み順に依存しない。
- 宣言が壊れていても、止まるのはその宣言だけ。Dreamforge も相手のMODも止まらない（fail-soft）。
- Dreamforge の既存の9人の旅人の値・挙動・セーブ・通信は、宣言があってもなくても1ビットも変わらない。

キャラクターMODの本体（Hero プレハブの登録、スキル、アセット、解錠）はコネクタの範囲外。それは相手のMODが本体の仕組みで行う（調査 §3-5）。

## 2. 用語

- 拡張旅人: コネクタで宣言された、既存9人以外の旅人。キーは Hero のサブクラスの型名（`ClientSession.HeroKeyOf` と同じ）。
- 既存の旅人: `Links` の名前簿にある9人。
- 宣言: 拡張旅人1人分のJSONの記述。
- 供給元: 宣言を置いたMOD（metadata.json の id）。

## 3. 宣言ファイル

### 3-1. 置き場所と見つけ方

- 相手のMODフォルダの直下に `dreamforge.json` を置く（`about/` の中ではない）。
- Dreamforge は、本体が読み込んだ有効なMOD（`DewMod.loadedInstances`、またはそれに相当する有効MODの一覧）のフォルダを走査し、`dreamforge.json` があれば読む。無効にしたMODのフォルダは読まない。
- 走査の時期は2回。Dreamforge 自身の読み込み時と、本体の `DewMod.onModLoadEnded`（Dreamforge より後に読まれたMODの分）。参照先の `starManifest` も再解析し、同じ供給元・同じ採用内容（ハッシュ）なら重ねて登録しない。同じキーの内容を変更するにはゲームを再起動する。
- 走査はタイトル・ロビーの時点で終わる。遠征中は登録を変えない（遠征中に届いた登録は次にタイトルへ戻ったときに反映する）。

### 3-2. 形式

UTF-8 の JSON。知らない欄は無視する（将来の版の欄が入っていても読める）。

```json
{
  "dreamforgeApi": 1,
  "heroes": [
    {
      "key": "Hero_Nova",
      "name": { "en": "Nova", "ja": "ノヴァ", "zh": "诺瓦" },
      "melee": false,
      "memories": [
        { "key": "St_Q_NovaBlink", "name": { "en": "Blink", "ja": "瞬き", "zh": "闪烁" } }
      ],
      "starTree": {
        "nodes": [
          { "id": "edge", "stat": "AttackPct", "perRank": 3, "maxRank": 3,
            "name": { "en": "Edge", "ja": "刃", "zh": "锋刃" } },
          { "id": "focus", "stat": "CritDamagePct", "perRank": 2, "maxRank": 3, "tier": 2,
            "name": { "en": "Focus", "ja": "集中", "zh": "专注" } },
          { "id": "nova_core", "keystone": true, "stat": "CritDamagePct", "value": 20,
            "name": { "en": "Nova Core", "ja": "新星の核", "zh": "新星之核" },
            "description": { "en": "Critical damage +20%.", "ja": "会心ダメージ +20%。", "zh": "暴击伤害 +20%。" } }
        ]
      }
    }
  ]
}
```

| 欄 | 必須 | 内容 |
| --- | --- | --- |
| `dreamforgeApi` | 必須 | 整数。この仕様は 1。Dreamforge が知らない大きい値なら、ファイル全体を読まずに警告1回。 |
| `heroes` | 必須 | 配列。1ファイルに複数の旅人を書ける。 |
| `heroes[].key` | 必須 | Hero のサブクラスの型名。`Hero_` で始まる英数字と `_`（64文字まで）。 |
| `heroes[].name` | 任意 | 表示名。`en`・`ja`・`zh`（apiVersion 1 で読むのはこの3言語。ほかの言語コードは無視される）。無ければ `Hero_` を取った型名。足りない言語は `en`、次に型名へ落ちる。 |
| `heroes[].melee` | 任意 | 近接の旅人なら true（既定 false）。Dreamforge の近接向けの扱い（`TravelerCombat.IsMelee` 系）に入る。 |
| `heroes[].memories` | 任意 | その旅人の記憶（St_* の型名）と表示名。Dreamforge の画面での記憶名と、記憶の星の対象・装着条件の判定に使う。装備・リンク・報酬・図鑑の生成対象には加えない。 |
| `heroes[].starTree` | 任意 | 従来の星図。`starManifest` が無い、または読めないときに使う。これも無ければ汎用ツリー（`Content.Talents`）。 |
| `heroes[].starManifest` | 任意 | MOD フォルダからの相対パス。正準 JSON の監修星図を本体と同じ登録へ渡す。§3-3 を参照。`dreamforgeApi` / `ApiVersion` は1のまま。 |
| `starTree.nodes[].id` | 必須 | `[a-z0-9_]`、32文字まで。旅人の中で一意。実際の星IDは `x.<key>.<id>`。 |
| `nodes[].stat` | Stat の星で必須 | `Stat` の名前（例 `AttackPct`）。効果は `stat`・`link`・`power`・`nativeModifier`・`scopedModifier`・`gimmickBoost`・`gimmickParam`・`authoredEffect`・`choices` のうち1つ。名前参照の `gimmick` だけでも通常星を宣言できる。使える種類と上限は §5。 |
| `nodes[].link` / `memory` | 記憶の星で必須 | `LinkKind` の名前と、対象の記憶の型名。その旅人の `memories` で受け付けた記憶か、`Links` の名前簿にある既存の記憶だけを指定できる。キーストーンには使えない。 |
| `nodes[].power` | Power の星で必須 | `Power` の名前。`keystone: true` と併用すると Power のキーストーンになる。 |
| `nodes[].perRank` / `maxRank` | 数値を指定する通常星で必須 | 1段あたりの値と段数。Stat と Power の値・段数は正の整数。記憶の星の値は `MemoryDamage` だけ小数点以下3桁まで、ほかの LinkKind は正の整数。1段あたりの値・段数・1星の合計（値×段数）の3つとも §5 の種類ごとの上限以下にする。`authoredEffect` は `maxRank` だけを指定し、`perRank` は指定できない。 |
| `nodes[].tier` | 任意 | 1（既定）または 2（奥の星）。 |
| `nodes[].keystone` / `value` | キーストーンで必須 | `keystone: true` と正の整数の `value` を書く。`authoredEffect` では `keystone: true` だけを指定し、`value` は指定できない。熟練度の条件は既存と同じ `HeroSigils.KeystoneMastery`。 |
| `nodes[].description` | Stat のキーストーンで必須 | 説明文。Power のキーストーンでは省略でき、既存の Power の説明文を使う。`authoredEffect` では参照元の説明文を使う。 |
| `nodes[].name` | 任意 | 表示名。無ければ id。 |
| `nodes[].requires` | 任意 | 同じ旅人の星の `id` の配列。挙げた星のどれか1つを取ると、この星へ進める。省略した星は今までの自動のつながりを使う。空配列なら中心につながる。 |
| `nodes[].position` | 任意 | `{ "ring": 1, "angle": -90 }` または `{ "x": 120.5, "y": -30 }`。両形式は併用できない。`ring` は1以上の整数、`angle` は度を表す数値。0度は右、90度は下。省略した星は自動配置を使う。 |
| `nodes[].center` | 任意 | 中心への辺の有無。true なら `requires` の辺に加えて中心にもつなぐ。false なら空の `requires` でも中心につながない。省略時は従来どおり。 |
| `nodes[].rankCost` | 任意 | 1段の費用。通常星は1、キーストーンは既存の費用が既定。上限は §5。Choice の選択肢は親と同じ費用にする。 |
| `nodes[].isDreamRing` | 任意 | 夢の輪の星として扱うか。既定 false。接続と位置はほかの宣言星と同じ方法で指定する。 |
| `nodes[].kind` | 任意 | `ClusterStarKind` の名前。効果と一致する種類だけを受け付ける。Power の通常星は `Notable`、記憶の星は `MemoryDamage` / `MemoryHaste`。 |
| `nodes[].choices` | 選択の星で必須 | 選択肢の配列。親は独立した効果を持たず、`maxRank` を指定する。選択肢は同じ段数の通常星で、id・星群・接続・位置・取得条件を持たない。入れ子の Choice は不可。 |
| `starTree.clusters` | 任意 | 星群の配列。各要素に一意な `id` と表示名 `name` を書く。`region` は `ClusterRegionKind`（既定 `Outer`）、`regionId` は任意の領域名、`shape` は `Fan` / `Ring` / `Chain`（既定 `Chain`）。配置プログラムは持たない。空の星群は不可。 |
| `nodes[].cluster` / `clusterOrder` | 任意 | `clusters` のローカル id と、星群内の並び順（既定0）。星群名は既存の表示処理で表示する。 |
| `nodes[].gimmick` | 任意 | 既存9人の単独の `GimmickDef` を持つ星のID。例 `h.vesper.route.resolve.2`。定義をコピーして既存の効果処理へ渡す。Stat・Link・Power の通常星と併用できる。新しい仕掛けの定義、機構の取り出し、キーストーンとの併用は不可。 |
| `nodes[].gimmickBoost` | 仕掛け強化で必須 | 1段の強化量（正の整数）。`memory` と `maxRank` を指定し、同じ記憶に参照先の仕掛けが必要。 |
| `nodes[].gimmickParam` | 仕掛けパラメータで必須 | `GimmickParam` の名前。`perRank`・`maxRank`・`memory` を指定する。対象の仕掛けがそのパラメータに対応している必要がある。 |
| `nodes[].nativeModifier` | Native 修飾で必須 | `{ "kind": "MemoryDamage", "valueUnits": 100 }`。`memory`・`maxRank` も指定する。100 units は1%。`capProfile` は作者向け報告の NativeModifier 表にある同種の名前だけを指定でき、省略時は既存の記憶星から求めたコネクタ用上限を使う。移動記憶は不可。 |
| `nodes[].scopedModifier` | Scoped 修飾で必須 | `scope`（`Memory` / `EffectChannel` / `Receiver`）、任意の `param`、ローカル星IDの `targets` 配列、`GimmickEffect` 名の `effects` 配列、任意の `capProfile`。値は通常 `amountUnits`（100で1%）、Chance は `probabilityUnits`（10000で100%）、ExtraTargets は `extraTargets`。該当しない値欄は0。`memory`・`maxRank` も指定する。既存の検証で対象なし・未知の対象・意味のないパラメータを拒否する。 |
| `nodes[].requiredStars` / `requiredAnyStars` | 任意 | 取得済みでなければならない星のローカルID配列。前者はすべて、後者はいずれか。`requires` の無向辺とは別の条件。既存の取得判定を使い、未知参照・自己参照・取得不能な依存を拒否する。 |
| `nodes[].routeOrder` | 任意 | 元の記憶ルート内の順序（既定0）。書き出し時の末尾星などの表示種別を保つために使う。 |
| `nodes[].authoredEffect` | 機構などの参照で必須 | `{ "hero": "Hero_Vesper", "star": "outer.ward.a2" }`。既存9人の機構・機構付きキーストーン・EffectChannel・ペア連携の星を参照する。`hero` と `star` は必須。選択肢の効果は親星のIDと0始まりの `option` を指定する。 |
| `starTree.effectBindings.stars` | `authoredEffect` で必須 | 元の星IDから、この宣言のローカル星IDへの対応表。所有星・取得条件・橋の両端・置換先・キーストーンの対象星を含め、必要な参照をすべて指定する。存在しない星や複数の元星を同じ星へ対応させる宣言は拒否する。 |
| `starTree.effectBindings.memories` | 任意 | 元の既存記憶から、既存記憶またはその旅人が宣言した記憶への対応表。省略した記憶は元のまま。省略分も含めて対応が重複してはいけない。チャンネルとペアのIDは拡張旅人ごとに分ける。 |

最初の例は従来の3星の宣言で、登録される星と配置は変わらない。次は、その例の `nodes` に追加できる記憶の星と Power の星・キーストーンの例。キーストーンの合計は §5 の上限内に収める。

```json
{ "id": "blink_damage", "link": "MemoryDamage", "memory": "St_Q_NovaBlink",
  "perRank": 1, "maxRank": 3, "requires": ["edge", "focus"],
  "position": { "ring": 2, "angle": -45.5 } }
```

```json
{ "id": "aegis", "power": "Aegis", "perRank": 1, "maxRank": 3,
  "requires": ["blink_damage"], "position": { "ring": 3, "angle": 0 } }
```

```json
{ "id": "retaliation", "power": "Retaliation", "keystone": true, "value": 1,
  "requires": ["aegis"], "position": { "ring": 4, "angle": 0 } }
```

Choice・星群・仕掛けの修飾を併用する場合の `starTree` の例。`St_Q_NovaBlink` は最初の例と同じ `memories` に宣言する。

```json
{
  "clusters": [{ "id": "blink", "name": { "en": "Blink", "ja": "瞬き", "zh": "闪烁" } }],
  "nodes": [
    { "id": "gimmick", "memory": "St_Q_NovaBlink", "gimmick": "h.vesper.route.resolve.2",
      "maxRank": 1, "cluster": "blink", "requires": [] },
    { "id": "boost", "memory": "St_Q_NovaBlink", "gimmickBoost": 1,
      "maxRank": 1, "cluster": "blink", "requires": ["gimmick"] },
    { "id": "duration", "memory": "St_Q_NovaBlink", "gimmickParam": "Duration", "perRank": 1,
      "maxRank": 1, "cluster": "blink", "requires": ["gimmick"] },
    { "id": "native", "memory": "St_Q_NovaBlink", "nativeModifier": { "valueUnits": 100 },
      "maxRank": 1, "cluster": "blink", "requires": ["gimmick"] },
    { "id": "scoped", "memory": "St_Q_NovaBlink",
      "scopedModifier": { "scope": "Memory", "targets": ["gimmick"], "amountUnits": 100 },
      "maxRank": 1, "cluster": "blink", "requires": ["gimmick"] },
    { "id": "choice", "maxRank": 1, "rankCost": 2, "cluster": "blink", "isDreamRing": true,
      "requires": ["boost", "duration"], "choices": [
        { "stat": "AttackPct", "perRank": 1, "maxRank": 1 },
        { "power": "Aegis", "perRank": 1, "maxRank": 1 }
      ] }
  ]
}
```

`requires` は既存の星図と同じ無向の辺になる。通常の到達判定を使うため、取得した星から辺を逆向きにも進める。宣言に循環がある、参照先の id が無い、中心から届かない星がある場合は、その旅人の星図だけを汎用ツリーに戻し、理由を報告する。位置の半径は ring 1 が `InnerRadius`、2 が `DeepRadius`、3以降が `BranchRadius + BranchStep × (ring - 3)`。

`authoredEffect` は既存の定義を複製し、数値・発動条件・段数表をそのまま使う。`perRank`・`value` や名前参照の `gimmick` との併用は拒否する。`maxRank` は参照元以下、`rankCost` は参照元以上にする。キーストーンの参照では `keystone: true` を指定し、通常星への変更はできない。Choice では親の費用を検証に使う。元の取得条件は引き継ぎ、全取得の条件は追加できるが、元の「いずれか」の条件を別の集合へ変えることはできない。選択肢の参照元が持つ取得条件も親の星に引き継ぐ。

小さな星図では使う効果の星と、その効果が参照する星だけを宣言できる。元の記憶ルートの前後の星は不要で、`routeOrder` も省略できる。コネクタはルートの配列を配置に使わず、通常の星として自動配置する。`requires`・`position` で接続と位置を指定する場合も同じ。効果が使う記憶とルートの識別子は保持する。

例えば、次の `starTree` は能力値の星と Vesper の突進の効果だけを持つ。`St_Q_NovaBlink` は §3-2 冒頭の `memories` に宣言する。`memory` は省略でき、その場合も `effectBindings.memories` の対応を使う。この例には、参照先の星を足す必要はない。

```json
{
  "effectBindings": {
    "stars": { "h.vesper.route.charge.2": "impact" },
    "memories": { "St_M_Charge": "St_Q_NovaBlink" }
  },
  "nodes": [
    { "id": "edge", "stat": "AttackPct", "perRank": 3, "maxRank": 3 },
    { "id": "impact", "maxRank": 3, "requires": ["edge"],
      "authoredEffect": { "hero": "Hero_Vesper", "star": "h.vesper.route.charge.2" } }
  ]
}
```

取得条件・置換先・橋の両端・キーストーンの対象など、効果が別の星を参照する場合は、その星も `nodes` に書き、元の星IDからローカルIDへの対応を `effectBindings.stars` に加える。追加した星にも参照があれば同じように補う。対応が足りない場合は、`requires a star binding: <元の星ID>` などの理由で星図だけを拒否する。対応先の星が `nodes` に無い場合も、そのIDを示して拒否する。必要な星の宣言は §5-1 の `--probe-small` が出す JSON でも確認できる。

外部記憶を使う汎用の機構は受け付ける。相手のMODから既存の発動・命中・撃破・所有攻撃の通知が届かなければ効果が動かない場合があるため、宣言した外部記憶の一覧とこの注意を §7 の報告へ残す。独自のダメージ・召喚処理が通知を送ることまでは保証しない。

Native 固有の処理は付け替えを制限する。選択中の元キーストーンIDが必要な `StunSourceFilter`・`SacrificeShield`、未接続の Native adapter が必要な橋は拒否する。MemoryTuning、RelayWindow、固有の会心・移動攻撃・速度連動の IdentityStrike、Native の段階を読む橋、Circle of Life の所有攻撃発射、SharedCrystalField・ShieldWall・ShadowCue の固定記憶は別の記憶へ付け替えられない。

apiVersion 1 で宣言できないもの

- 難易度・報酬・部屋の予兆などの旅人別スケール。拡張旅人は常に既定値（1.0）。
- 新しいペア連携、旅人別の RunGrowth プログラム。`starTree` は既存の Gimmick の名前参照と `authoredEffect` の定義参照を扱う。`starManifest` では、本体で使う型付き仕掛け・修飾・機構付きキーストーンを宣言できる。ただし Power 系は `starManifest` の範囲外。
- 既存の旅人・既存の星・既存の記憶の値の変更。

### 3-3. 本体と同じ監修星図（starManifest）

作者向けの配置例は [MODDING.ja.md](../MODDING.ja.md)、英語版は [MODDING.md](../MODDING.md)。形式は [正準 JSON](../../tools/star-manifest/README.md) の `hero` と `stars` を使い、実行時に解析する。外部ファイルを `gen_cs.py` に渡す作業は要らない。

- `hero` は宣言の `key` と一致させる。`region` は `memory` / `bridge` / `outer` / `keystone`。`kind` は MemoryDamage / MemoryHaste / GimmickBoost / GimmickParam / Notable / Choice / Stat / Keystone。Choice は2択で、入れ子と Keystone の選択肢は不可。
- ID・星群・参照は `x.<key>.` に統一する。短い ID はこの接頭辞を補う。`h.` や別旅人の `x.` は受け付けない。既存星と自動ルートは上書きできない。同じ ID は先に受け付けた星を優先する。
- `memory`・`receiver`・選択子の候補は、その旅人の `memories` で宣言した記憶だけを使える。本体の記憶を使う場合も宣言が必要。候補を省いたスロット選択子は宣言記憶に限定する。独自記憶の `St_D_` は Identity、`St_M_` は移動、それ以外は編集可能枠として割り当て時に検証し、実行時は実際の装備枠を使う。
- 宣言記憶ごとに本体のルートから形・順序・段数・費用をコピーする。ID は `x.<key>.route.<記憶ID>.<順序>`。現行の形は7星で、最初の6星は3段・各1ポイント、最後は1段・3ポイント。効果は本体9人の登録済み通常星にある正の MaxHealthPct の最小値を使う（装備記憶で条件づけない通常Stat）。ルートの効果や費用を JSON で変えることはできない。
- `memory` の入口 `anchor` は自動ルートの星を指定する。同じ星群内は入口の anchor を継承できる。外縁はルート・記憶領域または効果を持つ Stat 根から入る。Keystone は取得条件の星を入口にできる。`edges` は無向の接続、`requires` は全件、`requiresAny` はいずれか1件の購入条件として共通検証へ渡す。
- 数値は実値、または本体の数値原本を参照する `valueRef` を使う。typed payload では `{ "valueRef": "…" }`。外部固有 ID に本体の原本は無いので通常は実値を書く。MemoryDamage は0.001刻み、ほかの整数欄は整数制約を保つ。解決後の数値を §5-2 で検証し、丸めて採用しない。
- ペア条件、旅人別スケール・RunGrowth、Power、固有記憶の native adapter は受け付けない。通常 Gimmick、DirectedRecharge、MemoryPrimed、AlliedWard と、その修飾・型付き Keystone 変換・Grant を扱う。受け手側の修飾は `memory: null` とし、送り手を限定した受信修飾は受け付けない。native damage の条件は `TargetHealthBelow:<百分率>` と `OutsideRetaliationWindow` の型付き条件を扱う。本体に同種が無い効果や数値は除外する。
- 読み込むファイルは各4 MiBまで、JSON の深さは64まで。同じ object のキー重複は拒否する。パスの絶対指定、`.` / `..`、空の構成要素、シンボリックリンクを拒否する。API から登録する場合も、`sourceModId` が有効な MOD のフォルダへ解決できる必要がある。

## 4. 受け付けと拒否（fail-soft）

宣言ごと（旅人ごと）に判定し、1つの失敗はほかの宣言に広げない。拒否の理由は Player.log に `[Dreamforge/Connector]` で1回だけ出し、§7 の報告ファイルにも書く。

| 状況 | 扱い |
| --- | --- |
| ファイルがJSONとして読めない、`dreamforgeApi` が無い・未知 | そのファイル全体を読まない |
| `key` が既存9人のどれか、または形式違反 | その旅人を拒否 |
| 同じ `key` を別の供給元も宣言 | 先に読んだ方を採り、後の方を拒否（読み込み順は本体の有効MODの並び） |
| `key` の Hero 型が読み込まれていない | 登録はする。報告に「型が見つからない」と書く（相手のMODが後から型を出す場合があるため） |
| `starTree` だけが §5 の上限を超える・形式違反 | 旅人の登録（名前・近接・記憶名）は受け付け、星図だけ汎用ツリーにする |
| `name` などの文字列欄の型違い | その欄だけ無視する |
| `starManifest` の星が不正・数値超過・ID 重複 | MOD 名・星 ID・理由を警告して、その星だけ除外する |
| 除外した星に参照が残る | 参照切れの星も反復して除外する |
| `starManifest` が読めない、全体の形式違反、採用星0件 | 同じ旅人の `starTree` へ戻す。それも無効なら汎用ツリーにする |

値を上限に丸めて受け付けることはしない（作者が気づけないため）。拒否して理由を出す。

## 5. 星図の上限

上限はコードに数値を書かず、既存9人の星から求める。実運用と書き出し道具は、生成済み星図を登録してから上限を取得する。既存の調整が変われば上限も一緒に動く。以下の §5-1 までの規則は従来の `starTree`、§5-2 は `starManifest` に適用する。

段のある星は1段あたりの値・段数・1星の合計（1段あたりの値×段数）の3つの上限をすべて満たす必要がある。合計の上限は同じ種類の既存の星1つごとに合計を計算し、その最大値を使う。別々の星の値と段数の最大を掛け合わせて、合計の上限にはしない。Choice の各選択肢にも同じ検証を行う。

- Stat・LinkKind・Power: 9人の `TreeFor` 全体とその選択肢から、種類ごとの1段あたりの値・段数・1星の合計の最大値を求める。従来の Core・記憶ルートの定義も含め、古い宣言で使えた種類を保つ。キーストーンの上限は従来どおりで、Stat は既存の最大値と「通常星の最大値×最大段数」の大きい方、Power はその Power の既存の最大値を使う。通常星・キーストーンの片方しか存在しない Power は、存在する側だけ宣言できる。
- GimmickBoost・GimmickParam: 9人の元定義に残る通常星と選択肢も含め、種類・パラメータごとの値・段数・1星の合計の最大値を使う。GimmickBoost の1段あたりの値は `gimmickBoost` 欄の値。
- ScopedModifier: scope・param・capProfile の組み合わせごとに、既存の値・段数・1星の合計の最大値を使う。通常は `amountUnits`、Chance は `probabilityUnits`、ExtraTargets は `extraTargets` の単位で集計し、合計の検証にも同じ単位を使う。
- NativeModifier: 同種・同上限定義の既存の `valueUnits`・段数・1星の合計の最大値を使う。コネクタ用上限定義は既存の MemoryDamage / MemoryHaste から求め、合計の上限はその種類の1星の合計上限を100倍した units とする。新しい計算式や新しい効果の種類は作らない。
- AuthoredEffect: 参照元1星の定義を上限とテンプレートにする。数値・発動条件・段数表の直接指定は受け付けない。段数は参照元以下、費用は参照元以上。記憶・星・チャンネル・ペアの参照だけを対応表で付け替える。参照先は `hero` と `star` で区別する（同じ星IDでも旅人ごとに効果が違う場合がある）。
- 数: 全星数・通常星数・キーストーン数・Choice数・1星の選択肢数・星群数を、9人の `TreeFor` 全体の最大以下にする。選択肢は独立した配置星には数えず、選択肢数の上限で検証する。
- `rankCost` と全体の段数も既存の最大以下。熟練度の条件と割り当ての規則（`Rules`）は既存どおり。星を増やしても、取るためのポイントの獲得量は変わらない。

2026-11-01 の生成済み星図では、全星906・通常星895・キーストーン11・Choice99・1星の選択肢2・星群94・RankCost5・段数5。これは説明用の記録で、実際の判定は計算した上限を使う。上限を超えた場合は丸めず、その旅人の星図だけを汎用へ戻す。

同日の MemoryDamage は1段あたり150.21・段数3・1星の合計150.21がそれぞれの上限。`perRank: 150.21, maxRank: 3` は合計450.63となるため拒否し、`perRank: 150.21, maxRank: 1` は受け付ける。NativeModifier の既定の MemoryDamage も、合計の上限は15021 units。

### 5-1. 既存星図を再現できる範囲

`tools/hero-connector` は Core を参照するコンソール。指定した既存旅人の `TreeFor` と `HeroTreeLayout` を宣言へ書き出し、別キーで登録して比較する。JSON とビルド成果物はリポジトリへ入れず、HDD 上の `$TMPDIR` に置く。

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/hero-connector -c Release --artifacts-path "$TMPDIR/hero-connector-artifacts" -- Hero_Vesper "$TMPDIR/vesper-dreamforge.json" Hero_TestVesper
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/hero-connector -c Release --artifacts-path "$TMPDIR/hero-connector-artifacts" -- Hero_Bismuth "$TMPDIR/bismuth-dreamforge.json" Hero_TestBismuth
```

比較するのは星数、Stat・Power・LinkDef の値と記憶、段数、RankCost、Tier、Gimmick と修飾、Choice と全選択肢、星群のまとまりと表示名、IsDreamRing、取得条件、位置と表示種別、残した星同士の辺、キーストーン。異なる値は終了コード1と報告書へ出す。宣言の隣に `.report.txt` を出し、除外した星をIDごとに理由つきで列挙する。

監修機構、機構付きキーストーン、ペア連携の橋、EffectChannel は `authoredEffect` で参照する。RunGrowth と元の選択キー専用 adapter など、対応できない定義は除外する。その一部を残して普通の星と呼ぶことはしない。Choice の片方だけが再現できない場合はChoice全体を除外し、対象機構や取得条件への依存が残る星も除外する。

機構を外すと中心から切り離される成分や、取得条件と辺の組み合わせで進めなくなる箇所には、書き出し道具が中心への辺を補う。これは元と異なる接続として、辺を補った星のIDを報告書の `Layout adaptations` に列挙する。残した星同士の辺と座標は保つ。元の星図全体の取得経路が一致するという保証ではない。

2026-11-01 の比較では、Vesper は892星から888星（Choice53・星群52）、Bismuth は747星すべて（Choice40・星群42）を再現した。Vesper の除外4星はすべて RunGrowth。RunGrowth を除く再現率は両者100%で、キーストーンもそれぞれ11個すべてを含む。中心への辺の補完は両者0、効果・取得条件・座標・辺などの比較も不一致0だった。

再登録した各星と各選択肢を、全星を取得条件の判定用状態へ入れたうえで、個別に `Build.ComputeForTree` と `Build.Encode` / `Decode` の往復へ通す。Vesper は941件、Bismuth は787件で往復の不一致0。これは合法な割り当て手順や全組み合わせの同値性、ゲーム内のイベント発火を証明するものではない。実機は未確認。

`--check <宣言JSON>` は宣言の受理結果と外部記憶の警告を表示する。`--snapshot` は拡張旅人を登録する前の指紋と既存9人の定義・配置を出力する。

`--probe-small <旅人> [出力先ディレクトリ]` は、見本に含むすべての `authoredEffect` を1件ずつ、能力値の星1つと参照に必要な星だけの宣言へ通す。Choice の参照は元の選択肢を両方残す。必要な参照をたどったあと、対象以外の星を1つずつ外して再登録し、受理に不要な星を取り除く。残した各星を単独で外すと拒否されるところまで確認する。元の `routeOrder` を保つ宣言、省略する宣言、接続も省略する自動配置の宣言を作る。さらに `effectBindings.stars` の各対応を1つずつ外し、必要な対応の不足を星IDつきの理由で拒否することを確かめる。受理と理由つき拒否は正常な結果とし、内部例外による拒否や説明のない失敗があれば終了コード1を返す。JSON と結果の一覧は出力先に残す。出力先の省略時は一時ディレクトリを使う。

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/hero-connector -c Release --artifacts-path "$TMPDIR/hero-connector-artifacts" -- --probe-small Hero_Vesper "$TMPDIR/vesper-small"
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/hero-connector -c Release --artifacts-path "$TMPDIR/hero-connector-artifacts" -- --probe-small Hero_Bismuth "$TMPDIR/bismuth-small"
```

2026-11-01 の確認では、Vesper は158参照（親星156個）×3通りで474件、Bismuth は122参照×3通りで366件を受理した。理由つき拒否・内部例外はともに0件。補った参照星は1宣言あたり Vesper が最大57個、Bismuth が最大63個。対応表を1件ずつ欠いた確認は計3947件で、3739件が不足した星IDつきの拒否、208件が不要な対応の省略として受理された。説明のない失敗は0件。

### 5-2. starManifest の上限

自動ルートを含む採用星数は `floor(本体9人の最多星数 × 1.2)` まで。ファイル順に検証し、上限を超える星を警告して除外する。通常星は1段・費用1、Keystone は1段で本体の最大 RankCost 以下。ポイントの供給と刻印の熟練度条件は変更しない。

値の比較は本体9人の登録済み星と Choice、Keystone の変換・Grant から求める。Stat と Keystone Stat、LinkKind、scope と param、仕掛けの kind・trigger・budget・effect、Ward の recipient・basis・pool・profile、Keystone の layer・field・operation・payload・effect ごとに分ける。同種が無い数値は受け付けない。cooldown と cadence は短縮側の本体最小値も検証する。共有の型検証・上限定義も適用し、clamp が起きる値を採用しない。

全体の通信・効果チャネル容量は既存の `BuildLimits` で確かめる。容量を超えた場合は末尾の星から除外し、参照を再検証する。毎フレームの処理は既存の型付き実行処理を使い、JSON 解析と数値走査は登録時に行う。

## 6. Dreamforge 側の変更点

調査 §2-2 の門を、拡張旅人に対して開く。既存の旅人の経路は変えない。

1. 拡張旅人の登録簿（Core）を新設する。`Links.IsTraveler`・`Links.Name` などの旅人の名前簿の問い合わせは、既存の名前簿に無ければ登録簿を見る。
2. `HeroNames.Of` は登録簿の名前を使う（言語は既存の表示言語の選び方に合わせる）。
3. `TravelerCombat.IsMelee` は登録簿の `melee` を見る。MeleeCoop の設定の符号化（ビット列）は既存の旅人のまま変えない。
4. 星図: `starManifest` は既存コネクタ内で解析し、`AuthoredStarDef` → `GenerateAuthored` → `RegisterAuthored` へ渡す。`AuthoredStarContract`・配置・割り当て・効果処理は本体と共通で、`HeroSigils.TreeFor` と `Content.TryGetTalent` は登録済み監修星図を優先する。`starTree` の経路は残す。
5. 装備欄の旅人選択（`DreamforgeUi.GearColumn` の KnownHeroes）に、登録簿の旅人を既存9人の後ろへ足す（Profile に状態が無くても選べる）。
6. 記憶: Dreamforge が記憶の表示名を引く所は、既存の名前簿に無ければ登録簿を見る。`Links.IsMemory` と装着条件の判定も拡張記憶を扱う。ただし、星図で参照できる拡張記憶はその旅人自身が宣言したものだけ。装備・リンク・報酬・図鑑の生成には入れない。
7. 難易度・報酬・予兆の旅人別スケールは、拡張旅人では既存の `default`（1.0）を通るだけで、変更は要らない。
8. 設定 `heroConnector`（`DreamforgeConfig`、既定 true）。false ならファイルを読まず、ランタイムAPIの登録も受け付けない。

## 7. 作者向けの報告ファイル

走査のたびに `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/connector-report.txt`（外部翻訳の lang/ と同じ親）を書き直す。英語で、次を含める。

- Dreamforge の版と apiVersion。
- 読んだファイル（供給元の id とパス）、受け付けた旅人、拒否した旅人と理由、星図を汎用にした理由。
- §5 の上限表。Stat・LinkKind・Power、GimmickBoost・GimmickParam、NativeModifier・ScopedModifier について、1段あたりの値・段数・1星の合計の上限を載せる。合計は各種類の値と同じ単位で示す。キーストーン値、星数・Choice数・選択肢数・星群数・RankCostも載せる。名前で参照できる既存の仕掛けも列挙する。作者はこの表を見て値を決められる。Power の表の 0 はその種類の星を宣言できないことを表す。

- `authoredEffect` の参照元（旅人・星・選択肢）、段数と最低費用。Native 固有の制限と、対応表を含む検証が別途必要であることも記す。
- 外部記憶を宣言し、仕掛けや機構を持つ旅人について、既存イベントの通知が必要であり、独自のダメージ・召喚経路では通知が届かず効果が動かない場合があるという警告。受理済みの登録簿に保持し、再走査後も報告する。

書き込みに失敗しても、警告1回で続ける。

## 8. ランタイムAPI（任意）

コードから使いたい作者向け。JSONだけで足りる作者は使わなくてよい。

- 型は `Dreamforge.Api.HeroConnector`（public static）。難読化の対象から外す（`tools/obfuscation/obfuscar.xml`）。名前と引数は apiVersion の間は変えない。
- 本体はアセンブリ名に時刻印を付けて読むので（調査 §3-2）、相手は型の FullName でアセンブリを走査して探す。ビルド時の参照は不要。

| メンバー | 内容 |
| --- | --- |
| `int ApiVersion { get; }` | 1 |
| `string DreamforgeVersion { get; }` | metadata.json の modVer |
| `bool RegisterHeroes(string json, string sourceModId, out string error)` | §3-2 と同じJSONを登録する。成否と理由（拒否のとき）。遠征中は保留して true を返し、`error` に保留の旨を入れる。 |
| `bool IsHeroRegistered(string heroKey)` | 登録簿にあるか |
| `string[] GetRegisteredHeroes()` | 拡張旅人のキーの一覧 |
| `string GetReport()` | §7 と同じ内容の文字列 |

どのメンバーも例外を外へ投げない（中で受け止めて false や空を返し、警告1回）。

相手側の呼び出し例（相手のMODに貼る。Dreamforge が無ければ何もしない）:

```csharp
static System.Type FindDreamforgeConnector()
{
    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
    {
        var t = asm.GetType("Dreamforge.Api.HeroConnector", false);
        if (t != null) return t;
    }
    return null;
}
```

## 9. 協力プレイ

- 能力の広告: 既存の機能単位の広告（`DreamforgeBuildInputCapabilityMsg` などと同じ作り）に、コネクタの有無を足す。Hello の型には欄を足さない（古い版との直列化の互換を壊さないため）。
- 広告を返した相手とだけ、新しいメッセージ（例 `DreamforgeHeroConnectorMsg`）で、登録した拡張旅人のキーと宣言のハッシュの一覧を交換する。`starManifest` は、旅人宣言と正規化した採用星図・自動ルートを同じ宣言ハッシュに含める。除外した星の内容は含めない。ファイルの空白や object キー順だけの差は採用星図のハッシュを変えない。
- ホストは今までどおり自分の登録で参加者の装備を検証する（調査 §4-3）。参加者の拡張旅人の宣言がホストに無い、またはハッシュが違うときは、その参加者の拡張旅人の星は既存の検証で拒否される。装備・汎用の仕組みは今までどおり効く。このとき両側で警告を1回出し、参加者の画面に1行の知らせ（例「ホストの Dreamforge にこの旅人の星図が無いため、この部屋では星図の効果が乗りません」）を出す。
- 広告を返さない相手（古い Dreamforge、Dreamforge 無し）には何も送らず、今までどおり。
- `ContentFingerprint`: 拡張旅人が1人以上登録されたときだけ、キーと宣言のハッシュを記録に足す。1人もいなければ指紋は今と同じ値のまま（既存の組み合わせで不一致の警告が増えない）。

## 10. セーブ

- 拡張旅人の状態は既存どおり `profile.json` の `heroes` にキーで入る（調査 §2-4）。
- 相手のMODを外したとき、その旅人の状態は読み書きでそのまま残す（消さない・作り替えない）。戻せば元どおり。`x.` の休眠割り当て・二択の選択・刻印は、ロードアウトを切り替えた場合も記録を残す。別旅人の星として効果を適用しない。
- 星図が変わった（宣言の星が減った、汎用⇔専用が切り替わった）とき、今の星図に無い星IDへの割り当ては効果を持たず、その分のポイントは未使用に戻る。割り当ての記録自体は消さない（星図が戻れば復元できる）。既存の `RegisterMigrations` の考え方に合わせ、破壊ではなく無効化にする。
- コネクタを持たない古い Dreamforge で、`x.` で始まる星IDを含む profile.json を開いても壊れないことを、コードを読んで確かめる（壊れるなら、読み込み側の受け止めを今回の版に入れておく）。

## 11. 互換の約束

- apiVersion 1 の欄の意味は変えない。`starManifest` は省略可能な追加欄で、従来の `starTree` と3引数の Core 登録APIを残す。マニフェストを読む場合はホストのファイル読込関数を渡す。新しい公開APIや別のローダーは作らない。
- 意味を変える必要が出たら apiVersion 2 とし、1 の宣言も読み続ける。
- `Dreamforge.Api` の名前空間の public メンバーは apiVersion の間は削らない。

## 12. 範囲外（この版ではしない）

- Hero プレハブの登録、解錠、ロビーの並び（本体の仕組み。相手のMODの仕事）。
- 拡張旅人の専用ペア連携、旅人別スケール・RunGrowth。Power 系は `starManifest` では範囲外（既存 `starTree` の Power と §3-2 の既存効果参照は引き続き使える）。監修星図・星群・橋・外縁・Choice・型付き仕掛け・機構付きキーストーンは §3-3 の範囲で宣言できる。
- ほかの種類のMOD（記憶・本質・遺物を足すMOD）の接続。必要になったら同じファイルに新しい欄を足す形で広げる。
