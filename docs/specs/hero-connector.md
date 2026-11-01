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
- 走査の時期は2回。Dreamforge 自身の読み込み時と、本体の `DewMod.onModLoadEnded`（Dreamforge より後に読まれたMODの分）。同じ供給元・同じ内容（ハッシュ）なら2回目は何もしない。
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
| `heroes[].starTree` | 任意 | 星図。無ければ汎用ツリー（`Content.Talents`。今の未知の旅人と同じ）。 |
| `starTree.nodes[].id` | 必須 | `[a-z0-9_]`、32文字まで。旅人の中で一意。実際の星IDは `x.<key>.<id>`。 |
| `nodes[].stat` | Stat の星で必須 | `Stat` の名前（例 `AttackPct`）。`stat`・`link`・`power` のうち1つを書く。使える種類と上限は §5。 |
| `nodes[].link` / `memory` | 記憶の星で必須 | `LinkKind` の名前と、対象の記憶の型名。その旅人の `memories` で受け付けた記憶か、`Links` の名前簿にある既存の記憶だけを指定できる。キーストーンには使えない。 |
| `nodes[].power` | Power の星で必須 | `Power` の名前。`keystone: true` と併用すると Power のキーストーンになる。 |
| `nodes[].perRank` / `maxRank` | 通常の星で必須 | 1段あたりの値と段数。Stat と Power の値・段数は正の整数。記憶の星の値は `MemoryDamage` だけ小数点以下3桁まで、ほかの LinkKind は正の整数。 |
| `nodes[].tier` | 任意 | 1（既定）または 2（奥の星）。 |
| `nodes[].keystone` / `value` | キーストーンで必須 | `keystone: true` と正の整数の `value` を書く。熟練度の条件は既存と同じ `HeroSigils.KeystoneMastery`。 |
| `nodes[].description` | Stat のキーストーンで必須 | 説明文。Power のキーストーンでは省略でき、既存の Power の説明文を使う。 |
| `nodes[].name` | 任意 | 表示名。無ければ id。 |
| `nodes[].requires` | 任意 | 同じ旅人の星の `id` の配列。挙げた星のどれか1つを取ると、この星へ進める。省略した星は今までの自動のつながりを使う。空配列なら中心につながる。 |
| `nodes[].position` | 任意 | `{ "ring": 1, "angle": -90 }` の形。`ring` は1以上の整数、`angle` は度を表す数値。0度は右、90度は下。省略した星は今までの自動配置を使う。 |

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

`requires` は既存の星図と同じ無向の辺になる。通常の到達判定を使うため、取得した星から辺を逆向きにも進める。宣言に循環がある、参照先の id が無い、中心から届かない星がある場合は、その旅人の星図だけを汎用ツリーに戻し、理由を報告する。位置の半径は ring 1 が `InnerRadius`、2 が `DeepRadius`、3以降が `BranchRadius + BranchStep × (ring - 3)`。

apiVersion 1 で宣言できないもの

- 難易度・報酬・部屋の予兆などの旅人別スケール。拡張旅人は常に既定値（1.0）。
- ペア連携、仕掛け（Gimmick）、選択の星（Choice）、監修星図の契約や機構、記憶ルート・夢の輪・星群そのものの定義。
- 既存の旅人・既存の星・既存の記憶の値の変更。

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

値を上限に丸めて受け付けることはしない（作者が気づけないため）。拒否して理由を出す。

## 5. 星図の上限

上限はコードに数値を書かず、起動時に既存9人の星から求める。既存の調整が変われば上限も一緒に動く。Stat の上限は従来のまま、新しい種類の上限は次の原典から求める。

- Stat: 既存9人の Core の通常の星で使われている Stat だけを宣言できる。`perRank` はその Stat の既存の最大 `PerRank` 以下、`maxRank` は既存の最大 `MaxRank` 以下。Power の星を Stat の上限に数える従来の計算も変えない。
- Stat のキーストーン: `value` は 1 以上、その Stat の通常の星の最大 `PerRank` × 最大 `MaxRank` 以下。既存の Power のキーストーンとは単位が違うため、その値は使わない。
- 記憶の星: 既存9人の `HeroStarRoutes` の記憶ルートで使われている LinkKind だけを宣言できる（現在は `Guard`・`MemoryDamage`・`MemorySurge`・`MemoryHaste`）。`perRank` と `maxRank` は、それぞれその LinkKind の既存の最大 `LinkPerRank.Value` と最大 `MaxRank` 以下。`MemoryDamage` の値は丸めずに千分率まで受け付ける。
- Power: 既存9人の Core・記憶ルート・深層で使われている Power だけを宣言できる。通常の星の `perRank` と `maxRank` は、それぞれその Power の通常の星の最大 `PerRank` と最大 `MaxRank` 以下。キーストーンの `value` は、その Power の既存のキーストーンの最大 `PowerValue` 以下。該当する種類の星が無い Power は、その種類には使えない。
- 数: Stat・記憶・Power を合わせた通常の星の数は、既存9人の `TreeFor` の結果（Core・ルート・夢の輪・星群を含む）の通常の星数の最大以下。キーストーンの数は従来どおり Core の最大以下。
- 費用（`RankCost`）・熟練度の条件・割り当ての規則（`Rules`）は既存と同じものを使う。宣言では変えられない。星を増やしても、取るためのポイントの獲得量は変わらない。

## 6. Dreamforge 側の変更点

調査 §2-2 の門を、拡張旅人に対して開く。既存の旅人の経路は変えない。

1. 拡張旅人の登録簿（Core）を新設する。`Links.IsTraveler`・`Links.Name` などの旅人の名前簿の問い合わせは、既存の名前簿に無ければ登録簿を見る。
2. `HeroNames.Of` は登録簿の名前を使う（言語は既存の表示言語の選び方に合わせる）。
3. `TravelerCombat.IsMelee` は登録簿の `melee` を見る。MeleeCoop の設定の符号化（ビット列）は既存の旅人のまま変えない。
4. 星図: 宣言が通った旅人は、既存の監修ツリーの登録口（`StarClusters` / `AuthoredStarRegistration` の TryGetRegisteredTree 系）と同じ形で `HeroSigils.TreeFor` から引ける。既存の監修星図の契約（`AuthoredStarContract`）は流用しなくてよい。拡張旅人専用の小さい登録口を作り、§5 の検証を通す。
5. 装備欄の旅人選択（`DreamforgeUi.GearColumn` の KnownHeroes）に、登録簿の旅人を既存9人の後ろへ足す（Profile に状態が無くても選べる）。
6. 記憶: Dreamforge が記憶の表示名を引く所は、既存の名前簿に無ければ登録簿を見る。`Links.IsMemory` と装着条件の判定も拡張記憶を扱う。ただし、星図で参照できる拡張記憶はその旅人自身が宣言したものだけ。装備・リンク・報酬・図鑑の生成には入れない。
7. 難易度・報酬・予兆の旅人別スケールは、拡張旅人では既存の `default`（1.0）を通るだけで、変更は要らない。
8. 設定 `heroConnector`（`DreamforgeConfig`、既定 true）。false ならファイルを読まず、ランタイムAPIの登録も受け付けない。

## 7. 作者向けの報告ファイル

走査のたびに `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/connector-report.txt`（外部翻訳の lang/ と同じ親）を書き直す。英語で、次を含める。

- Dreamforge の版と apiVersion。
- 読んだファイル（供給元の id とパス）、受け付けた旅人、拒否した旅人と理由、星図を汎用にした理由。
- §5 の上限表（Stat ごとの最大 perRank・maxRank・キーストーン値、LinkKind ごとの最大 perRank・maxRank、Power ごとの最大 perRank・maxRank・キーストーン値、星の数の上限）。作者はこの表を見て値を決められる。Power の表の 0 は、その種類の星を宣言できないことを表す。

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
- 広告を返した相手とだけ、新しいメッセージ（例 `DreamforgeHeroConnectorMsg`）で、登録した拡張旅人のキーと宣言のハッシュの一覧を交換する。
- ホストは今までどおり自分の登録で参加者の装備を検証する（調査 §4-3）。参加者の拡張旅人の宣言がホストに無い、またはハッシュが違うときは、その参加者の拡張旅人の星は既存の検証で拒否される。装備・汎用の仕組みは今までどおり効く。このとき両側で警告を1回出し、参加者の画面に1行の知らせ（例「ホストの Dreamforge にこの旅人の星図が無いため、この部屋では星図の効果が乗りません」）を出す。
- 広告を返さない相手（古い Dreamforge、Dreamforge 無し）には何も送らず、今までどおり。
- `ContentFingerprint`: 拡張旅人が1人以上登録されたときだけ、キーと宣言のハッシュを記録に足す。1人もいなければ指紋は今と同じ値のまま（既存の組み合わせで不一致の警告が増えない）。

## 10. セーブ

- 拡張旅人の状態は既存どおり `profile.json` の `heroes` にキーで入る（調査 §2-4）。
- 相手のMODを外したとき、その旅人の状態は読み書きでそのまま残す（消さない・作り替えない）。戻せば元どおり。
- 星図が変わった（宣言の星が減った、汎用⇔専用が切り替わった）とき、今の星図に無い星IDへの割り当ては効果を持たず、その分のポイントは未使用に戻る。割り当ての記録自体は消さない（星図が戻れば復元できる）。既存の `RegisterMigrations` の考え方に合わせ、破壊ではなく無効化にする。
- コネクタを持たない古い Dreamforge で、`x.` で始まる星IDを含む profile.json を開いても壊れないことを、コードを読んで確かめる（壊れるなら、読み込み側の受け止めを今回の版に入れておく）。

## 11. 互換の約束

- apiVersion 1 の欄の意味は変えない。足すときは新しい欄として足し、古い宣言はそのまま読む。
- 意味を変える必要が出たら apiVersion 2 とし、1 の宣言も読み続ける。
- `Dreamforge.Api` の名前空間の public メンバーは apiVersion の間は削らない。

## 12. 範囲外（この版ではしない）

- Hero プレハブの登録、解錠、ロビーの並び（本体の仕組み。相手のMODの仕事）。
- 拡張旅人の専用のペア連携、記憶ルート・夢の輪・星群そのものの定義、Gimmick・Choice・監修星図の契約や機構、旅人別スケール。
- ほかの種類のMOD（記憶・本質・遺物を足すMOD）の接続。必要になったら同じファイルに新しい欄を足す形で広げる。
