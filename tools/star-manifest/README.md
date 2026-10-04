# v1.31 星の機械可読マニフェスト

設計表 `docs/specs/v1.31-clusters-*.md` の**新しい星**（旅人固有の新規ID・刻印、共有外縁160）と、設計表が効果を**変える既存星**（移行行）を、1星1オブジェクトの JSON に書き起こしたもの。C01（AuthoredStarContract）の登録APIができたら、このJSONから C# のデータを生成する。設計の正は設計表と [正式仕様](../../docs/specs/v1.31-new-mechanisms.md)・[レビュー](../../docs/specs/v1.31-design-review.md)。食い違いはJSON側で勝手に直さず `notes` に書く。

ファイル：`tools/star-manifest/<hero>.json`（vesper, lacerta, cetus, yubar, husk, mist, nachia, aurena, bismuth）と `outer.json`（共有外縁、hero は "shared"）。10ファイルは**すべて下記の正準形に統一**されている（書式は UTF-8・LF・インデント2）。

## ファイルの形

```json
{ "hero": "Hero_Cetus", "source": "docs/specs/v1.31-clusters-cetus.md", "stars": [ {星}, … , {移行行}, … ] }
```
トップレベルのキーはこの3つだけ（旧 `conventions` 欄は廃止。規約はこのREADMEが唯一の正本）。`stars` は新しい星、続けて `region: "migration"` の行。

## 星（新規）の正準形

キーはこの順・全て必須。使わない欄は `null`（配列は `[]`）。

```json
{
  "id": "cetus.mem.icy-veins.c1.e1",
  "region": "memory | bridge | outer | keystone",
  "cluster": "cetus.mem.icy-veins.c1",
  "shape": "fan | ring | chain | null",
  "anchor": "星団の外の既存星ID（入口のみ）または null",
  "edges": ["同じ星団の隣接星ID"],
  "requires": ["購入の前提（全部）"],
  "requiresAny": ["購入の前提（どれか1つ）"],
  "kind": "MemoryDamage | MemoryHaste | GimmickBoost | GimmickParam | Notable | Choice | Stat | Keystone",
  "memory": "St_D_IcyVeins | 選択子 | null",
  "value": 1.0,
  "param": "Duration | Radius | ExtraTargets | Chance | null",
  "receiver": "受け手の記憶 | 選択子 | null",
  "target": {"star": "修飾先のGimmick星ID | null", "effect": "GimmickEffect名 | null"},
  "gimmick": {"trigger": "OnUse|OnHit|OnKill|OnCrit|OnBasicAttack", "effect": "Recharge など", "value": 4, "arg": 0, "cooldown": 0, "target": null},
  "power": {"name": "Power 名", "perRank": 0},
  "stat": {"name": "Stat 名", "perRank": 0},
  "options": [ {選択肢}, {選択肢} ],
  "keystone": {"upside": "…", "downside": "…", "upsideSpec": [ … ], "downsideSpec": [ … ]},
  "mechanisms": ["C03", "C04"],
  "maxRank": 1,
  "rankCost": 1,
  "nameJa": "自然な日本語の名前",
  "nameEn": "English name",
  "notes": "設計表との食い違い・要確認（なければ空文字）"
}
```

- `kind` ごとの必須欄：MemoryDamage/MemoryHaste/GimmickBoost/GimmickParam は `memory` か `receiver` と数値 `value`（GimmickParam は `param` も）。Notable は `gimmick` か `power`。Stat は `stat`（**outer のみ**）。Choice は `options` ちょうど2つ（Choice 自身の効果欄は全て null）。Keystone は `keystone`（region も `keystone`）。
- `value` は設計表の単位のまま（1 = 1%、確率は%ポイント）。整数化しない。
- 名前は設計表にあればそれを使い、無ければ効果が分かる短い自然な日本語と英語を付ける。人名は書かない。
- 1つの星に効果が複数ある設計は、設計表の表記どおり別の星に分ける。分けられない刻印だけ `keystone.*Spec` を使う。

## 正準形の規約

### 1. 何を修飾する星か：`target`
GimmickBoost/GimmickParam だけが `target: {"star", "effect"}` を持つ（他の kind は `null`）。
- `star`：修飾先の Gimmick 星のID（同じファイル、または既存の `h.<hero>.route.*` / `h.<hero>.ring.*`）。`effect`：その GimmickEffect 名（`star` の gimmick.effect と一致させる。既存星なら HeroStarRoutes.cs の G の効果）。
- 両方 `null` = その記憶（受け手側ならその受け手）の**仕掛け全体**に作用する（設計表の `B(m,x)` `P(m,…)` の通常形）。
- `star` だけあり `effect` が `null` = 既存の橋（`h.<hero>.ring.*`）の仕掛けpayloadを対象にする星（橋のB/T/R、設計表のN3）。
- 対象が分からない星は両方 `null` にして `notes` に書く。旧欄 `effectId` / `paramTarget` / 選択肢の `requires` / `scopeStars` は廃止し、すべて `target` に置き換えた。

### 2. 受け手側の星：`receiver` と `memory`
受信（移動記憶などが**受け取る**もの）を扱う星は、`receiver` = 受け手の記憶型名（例 `St_M_FrostyCharge`）、`memory` = 送り手の記憶、または `null`（送り手を限定しない＝全送り手）。
- 送り手が1つの Notable（`Recv`）は `memory` = 送り手、`receiver` = 受け手、`gimmick.target` = 受け手（`receiver` と必ず同じ値）。
- 受け手の受信量を伸ばす `RB`/`B(受け手)` の GimmickBoost は `memory: null`、`receiver` = 受け手。特定の送り手の受信だけを伸ばすなら `memory` = その送り手。
- 移動の記憶は起点（gimmick の発火元 `memory`）にならない（`St_M_*` を `memory` に置いて `gimmick` を付けるとエラー）。

### 3. スロット選択子
装備枠で記憶が決まる箇所は、記憶型名の代わりに文字列の選択子を使う：`"@ID"`（装備中のIdentity）、`"@Q"`、`"@R"`、`"@M"`（移動）。候補を絞るときは `"@Q(St_Q_A|St_Q_B)"`。`"@Q|@R"` のように `|` で枠を連結してもよい。`equippedQ(…)`・`Q*`・`@ALLSRC` は廃止（全送り手は `memory: null`）。選択子は `memory` / `receiver` / `gimmick.target` / `keystone.*Spec` の `memory`/`receiver` で使える。

### 4. 橋に条件づく仕掛け：`gimmick.condition`
`"BridgeSuccess:<橋ID>"`（合わせ技の成立）、`"BridgeMark:<橋ID>"`（橋の印を読む）、`"BridgeWindow:<橋ID>"`（橋の窓）。大文字小文字はこのとおり、`<橋ID>` は同じ旅人の `h.<hero>.ring.<force|insight|vessel|armor|recall|rhythm|resolve|renewal>`。橋の条件でない発動単位の補足（「印なし」「撃破は条件にしない」「基本攻撃1回につき1回」など）は `notes` に書く。橋の成立条件がない星（Mist の renewal）は `condition` を付けない。

`PairCombos` の既存62個に無い橋は `gen_cs.py` の表で扱う（`PairCombos` は増やさない）。
- `AUTHORED_PAIRS`：設計表が両端と成立条件を決めている新ペア（Bismuth renewal=I×S直接受信、Bismuth resolve=P×I印、Aurena renewal=C×G印+AlliedWard、Nachia renewal=循×森の窓）。中心の行が型付きの `BridgeSuccessDefinition`（`ManifestNewPair` / `ManifestNewDirectRechargePair`）になり、橋の領域と `condition` はこのペアIDに結び付く。印/窓の5段中心は「旧リング中心（MaxRank 5）だけが5段を保持できる」唯一の例外（`RetainedFiveRanks`）。印のExposeは段+1%（2/3/4/5/6%）。
- `RECEIVER_ONLY_BRIDGES`：ペアを作らない橋（Mist の `h.mist.ring.renewal`）。星団の全星が `ReceiverOnlyBridge = true` と、設計表のA/B（FL/LU）を宣言した `MemoryOwnership` を持つ。宣言外の記憶を使う星は生成エラー。
- `UNRESOLVED_BRIDGES`：設計の未決事項で実ペアを登録できない橋（現在は空）。
- 既存7ペアの中心行は `ManifestPair`（登録済みペアの型付きbase）として出力する。設計が直接受信へ変えた行（Vesper vessel/armor、Cetus vessel/armor）は行が定義し、Aurena B2 の印起点 Crit→Hit は `LEGACY_OPENING_OVERRIDES`。
- `condition = BridgeSuccess:…` の星の `trigger` はペアの成立トリガーと一致していなければならない（不一致は生成エラー。成立イベントからしか dispatch されず、決して発動しないため）。

### 5. gimmick の欄
必須：`trigger, effect, value, arg, cooldown, target`。任意（ある星だけ、この順）：

| 欄 | 意味 |
|---|---|
| `condition` | 上記4 |
| `once` | 発動単位に1回（OncePerActivation） |
| `everyN` | N回の発射ごとに1回 |
| `valuesByRank` | 段ごとの値の配列（`value` は1段目） |
| `triggerByIdentity` | `{ Identity記憶: trigger }` の対応（装備Identityごとに発火元が違う） |
| `replaces` | 置き換える星のID（二重発動しない） |
| `basis` / `pool` | 値の基準（例 RecipientMaxHP）と障壁プール（例 Ordinary） |

これ以外の欄（旧 `pair` `pairResolved` `maxCharge` など）は使わない。必要な補足は `notes` へ。

### 選択肢（Choice の `options`）
`options[0]` = 選択肢A、`options[1]` = B。キーは `kind, memory, value, param, receiver, target, gimmick, power, stat, nameJa, nameEn`（この順・全て必須、使わない欄と無い名前は `null`）。`kind` は Choice/Keystone 以外。GimmickBoost/GimmickParam の選択肢は星と同じく `target` を持つ。旧 `label` は廃止（順序で表す）。

### 刻印（Keystone）
`keystone` は `upside` / `downside`（文章）と `upsideSpec` / `downsideSpec`（機械可読の配列、書いていなければ `null`）。`null` は「機械可読化していない」の意味で、**文章が正**。
Spec の要素は次のキーだけ：

| キー | 意味 |
|---|---|
| `effect` / `field` | 必須。何の（効果名）どの量（`Value` `Duration` `Radius` `Total` `Damage` `Delay` `Lifetime` `ExtraTargets` `Chance` `Arg` `EveryN` など）を変えるか |
| `pct` | 増減の%（-10 = -10%、100 = +100%） |
| `from` / `to` | 絶対値の変更（`from` は元の値、`to` は変更後。`to` だけでもよい） |
| `delta` / `max` | 加算（+1 など）と合計の上限 |
| `condition` | 条件付き刻印（M6）。`TargetHealthBelow:<0-100>`（命中直前の対象HP%が未満のときだけ適用）または `OutsideRetaliationWindow`（既存の逆襲（被弾後3秒）の窓の外側だけ適用）。最終本体ダメージの effect（`NativeDamage` / `DirectQR` / `DirectDamage` / `DirectBasicAttack` / `SummonDirectDamage`）とだけ併用可 |
| `memory` / `memories` / `scope` / `receiver` | 対象の記憶（1つ／複数／名前付きの集合）と受け手 |
| `field: "Enabled"` + `to: 0` | その効果を無効にする |
| `field: "Grant"` + `gimmick` | 刻印が追加で与える仕掛け（gimmick は上記5の形。`effect: "SacrificeShield"` / `"StunSourceFilter"` はC11/C08の名前付きネイティブアダプタで、`h.aurena.key2` / `h.cetus.key2` にだけ書ける） |
刻印星は `cluster: "<hero>.key"`、`edges: []`、置き場所は `anchor`（先頭の候補）と `requires`（全部必要）/`requiresAny`（どれか）で表す。**表が置き場所を指定していない刻印**は現在の仮置きのまま `notes` を `仮置き:` で始める（検証が数える）。

### 6. グラフ
- すべての星は `anchor`（入口の星：星団の**外**の既存星または別星団の星のID。入口でない星は `null`）か、空でない `edges` を持つ。
- `edges` は**同じ星団の中の無向な隣接**で、対称（AがBを持てばBもAを持つ）。星団をまたぐ・既存星への接続は `anchor` / `requires` / `requiresAny` で表す（星団の外を指す `edges` は不可）。入口が2つ以上ある星は `anchor` に主な入口、`requiresAny` に代わりの入口を並べる。
- 例外：`outer.json` の外周リング接続（`s2` と次星団の `s4`、取得前提ではない純粋な隣接）だけは両端の `edges` に対称に残す。
- `anchor` `edges` `requires` `requiresAny` `target.star` `gimmick.replaces` `condition` の橋ID は、すべて同じファイル、`outer.json`、または既存（legacy）の星ID（`h.<hero>.*`）に存在すること。既存IDは `src/SodRpg.Core/Game/HeroSigils.cs`（核）・`HeroStarRoutes.cs`（route 7星＋slot＋ring 8）・`StarClusters/*.cs`（登録済みsample）から検証時に導出する。

### 7. 移行行（region `migration`）
設計表が**効果を変える既存星**1つにつき1行。表の移行節が挙げる既存星（旧Stat・Guard・MemorySurge の置換、トリガーやCDの変更、橋の中心の再定義、既存刻印への代償の追加など）をすべて書く。変更のない星は書かない。`id` は既存ID（検証で照合）。

キーは次の順・全て必須：`id, region("migration"), kind, memory, value, param, receiver, target, gimmick, power, stat, options, keystone, requires, requiresAny, maxRank, mechanisms, nameJa, nameEn, notes`。
- 効果の欄は新規の星と同じ意味で、**置換後**の効果を書く。`value` は1段あたりの値、`maxRank` は既存の段数。段ごとに値が違うときは `gimmick.valuesByRank`。
- 旧効果の複数段Choice は `kind: "Choice"` と `options` 2つ。既存のStatのままの星は `kind: "Stat"` + `stat`。
- `kind: null` は置換後の機械表現が設計表に無い場合だけ。効果欄は全て `null` にして `notes` に設計表の記述を書く。
- `requires` / `requiresAny` は設計表がその既存星に**追加する取得前提**。接続の辺だけを足す変更は `notes` に書く。
- 既存刻印（`h.<hero>.key` / `key2`）は `kind: "Keystone"` と `keystone`（旧Powerの利点＋追加された代償）。

### その他
- 星の `mechanisms` は C01〜C15。`maxRank` は新規星では通常1。`rankCost` は新規星が1、旧星の費用は維持（移行行には書かない）。

## 検証

```
python tools/star-manifest/validate.py            # 全部
python tools/star-manifest/validate.py cetus      # 1人
```
上記の正準形を厳密に確かめる：キー集合と順序が完全一致（旧欄は不可）、新規ID数がレビューの表（New private IDs、外縁は160）と一致、IDの重複なし、kind ごとの必須欄、Choice の2択、Stat は outer と移行行だけ、選択子の文法、受け手の規則（`receiver` = `gimmick.target`）、`target` の整合、gimmick の欄と `condition` の書式、Spec の書式、グラフ（`anchor` か `edges`、`edges` の対称・星団内、刻印は `edges: []`）、**参照整合**（上記6）、**移行行のIDが既存IDであること**、mechanisms が C01〜C15、移動の記憶が起点になっていないこと、`仮置き:` の使い方。OK のとき、`仮置き:` の刻印数も表示する。

## 生成・登録・診断

- `python tools/star-manifest/gen_cs.py --all` は、全星がC#に写せた旅人だけ `<Hero>.Generated.cs` を出す（部分的な旅人は出さない）。`GeneratedRegistration.cs` の `CompiledHeroes` は生成できた旅人、`GeneratedHeroes` は **`registered.txt` に書いた旅人だけ**（実際の `StarClusters.RegisterAuthored` を通り、`GeneratedHeroAcceptanceTests` が通ることを確認したもの）。登録に失敗する旅人を載せるとゲームが起動時に落ちるので、`registered.txt` には確認済みの旅人だけを足す。
- `--diagnostic` は鍵の行などの失敗行を除いた診断用の写しを `tests/SodRpg.Core.Tests/Diagnostics/`（git管理外）に作る。`RegistrationDiagnostics`（環境変数 `DREAMFORGE_REG_REPORT` で出力先を指定）が全旅人の登録拒否を全件集め、`docs/specs/v1.31-registration-report.md` を作る。前文は `registration-report-preamble.md`（`DREAMFORGE_REG_PREAMBLE`）。

### 橋の窓/印の持続時間
`GimmickParam` の `param` には `"WindowDuration"`（橋の窓）と `"MarkDuration"`（橋の印）も使える。橋の星団の行で `target.star` がその橋の ring ID（`target.effect` は null）のときだけ許され、Window ペアには WindowDuration、Mark ペアには MarkDuration だけを使える（`validate.py` が形、`gen_cs.py` が実際のペアの種類を検査し、契約も登録時に拒否する）。意味は他の Duration と同じ%で、その橋のペアの窓／印だけが延びる。
