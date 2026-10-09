# v1.31 星の機械可読マニフェスト

設計表 `docs/specs/v1.31-clusters-*.md` の**新しい星**（旅人固有の新規ID・刻印、共有外縁160）と、設計表が効果を**変える既存星**（移行行）を、1星1オブジェクトの JSON に書き起こしたもの。C01（AuthoredStarContract）の登録APIができたら、このJSONから C# のデータを生成する。設計の正は設計表と [正式仕様](../../docs/specs/v1.31-new-mechanisms.md)・[レビュー](../../docs/specs/v1.31-design-review.md)。食い違いはJSON側で勝手に直さず `notes` に書く。

後続の数値調整は **Issue #149 段階1・2で `tools/balance/stars.json` を唯一の数値原本へ切替済み**。MemoryDamage・MemoryHaste・GimmickBoost・GimmickParam・Notable・Keystone・Statの調整・生成・記憶別%/点／種類別値の比較は [balanceツール](../balance/README.md) を使う。manifestは参照と星の構造を持ち、v1.31の設計表は当時の設計値を記録したままにする。費用・段数・保存形式は変えない。

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
  "kind": "MemoryDamage | MemoryHaste | GimmickBoost | GimmickParam | Notable | Choice | Stat | Keystone | RunGrowth | RunGrowthMod（後ろの2つは下の「v1.32 の追加」参照）",
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
**MemoryDamage／MemoryHaste／GimmickBoost／GimmickParamは上の `value` を同じ位置の `valueRef` に置き換える。**
親星は `"<星ID>/value"`、Choice子は `"<親星ID>/options/0/value"` または
`options/1/value`。`value` との併存・欠落・別pathの `valueRef` は不可。
Notable／Statのtop-level `value` はnull。typed payloadの調整欄は
`{"valueRef":"<星ID>/<payloadPath>"}`（例：`gimmick/value`、`power/perRank`、`stat/perRank`、
`keystone/upsideSpec/0/pct`）を使う。rank表の `gimmick.value` は `valuesByRank/0` と同じ参照。
native Grantの共有原本は `legacy/<刻印ID>/native/<欄>`。数値の原本・精度・倍率の対象はbalanceツールの説明を参照。
validate／genは同じDecimal解決済みviewを使い、raw JSONには値を書き戻さない。
MemoryDamageは正の0.001刻みで既存Linkへ生成し、MemoryHasteのAmountは整数制約を維持する。


- `kind` ごとの必須欄：MemoryDamageは `memory` と上記 `valueRef`。MemoryHaste/GimmickBoost/GimmickParam は `memory` か `receiver` と `valueRef`（GimmickParam は `param` も）。Notable は `gimmick` か `power`。Stat は `stat`（**outer のみ**）。Choice は `options` ちょうど2つ（Choice 自身の効果欄は全て null）。Keystone は `keystone`（region も `keystone`）。
- 解決済み数値の単位は維持する（1 = 1%、確率は%ポイント）。個数・回数・Stat／Power等は既存の整数制約を維持する。
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
| `strike` | `effect: "IdentityStrike"` と必ず対。アイデンティティ記憶が与える追加ダメージ（下記 5a） |
| `tuning` | `effect: "MemoryTuning"` と必ず対。名前付きの本体記憶の静的な挙動変更（下記 5b） |

これ以外の欄（旧 `pair` `pairResolved` `maxCharge` など）は使わない。必要な補足は `notes` へ。

#### 5a. IdentityStrike（アイデンティティ記憶そのものが与えるダメージ）

空殻の風の傷・一歩一殺のように、**本体の追跡・増幅（神聖なる信仰など）が見える「記憶のダメージ」**を足す。実行は `HostAuthority.IdentityStrikes.cs`（ダメージ発生元を装備中のアイデンティティ記憶のSkillTriggerにする。`hero.PureDamage` ではない）。`memory` は `St_D_ScarOfTheWind` か `St_D_TheKillingFlow`、`trigger: "OnHit"`（自分の通常攻撃の命中）、`arg: 0`、`cooldown: 0`、`target: null`。`value` は攻撃力・魔力の**高い方**に対する百分率（60 = 60%）。

```json
"gimmick": {"trigger": "OnHit", "effect": "IdentityStrike", "value": 60, "arg": 0, "cooldown": 0, "target": null,
  "strike": {"mode": "AfterDisplacement", "element": "Dark", "shape": "ForwardArc", "range": 4.5, "width": 120, "maxTargets": 8, "windowSeconds": 4}}
```

| `strike.mode` | 意味 | 必須／禁止 |
|---|---|---|
| `AfterDisplacement` | ダッシュ・テレポートのあとの次の通常攻撃の命中で1回（1回の移動につき1回）。風の傷専用 | `windowSeconds` 必須（0.5〜10）。`everyN`・`bonusSpeed` は不可 |
| `EveryNthBasicAttack` | 通常攻撃がN回命中するごと（`gimmick.everyN`、1 = 毎回）。一歩一殺専用 | `everyN` 必須（1〜100）。`bonusSpeed` は任意：変換した追加攻撃速度1%ごとの上乗せ%（0.2 = 0.2%）。`windowSeconds` は不可 |
| `AfterDisplacementCritical` | ダッシュ・テレポート後、窓内の最初の通常攻撃の主命中が会心なら闇の斬撃。非会心でも準備を消費。斬撃時にMovement記憶の残りクールダウンを35%短縮する。風の傷専用 | `windowSeconds` 必須（0.5〜10）、`element: "Dark"`、`maxTargets` 1〜6（既定6）。`everyN`・`bonusSpeed` は不可。内部間隔1秒 |
| `ConsecutiveCritical` | 同じ敵へ通常攻撃の主命中が3回連続で会心なら闇の斬撃。各命中間隔は窓以内。非会心・敵の変更・窓切れで連続数をリセット。一歩一殺専用 | `windowSeconds` 必須（0.5〜10）、`element: "Dark"`、`maxTargets` 1〜6（既定6）。3回固定で`everyN`・`bonusSpeed` は不可。内部間隔1秒 |
| `DashBonusAsMemory` | ダッシュ攻撃の追加分（闇75%）を風の傷のダメージとして数える（ダメージは増えない）。風の傷専用 | `value: 0`。他の欄は不可 |

`element` は `None/Fire/Cold/Light/Dark`、`shape` は `ForwardLine`（`width` = 幅m）／`ForwardArc`（`width` = 角度）、`range` は1〜15m、`maxTargets` は1〜16（既定8）。`condition/once/valuesByRank/triggerByIdentity/replaces/basis/pool` は不可。

会心モードも同じ`gimmick`形を使う（星の`value`は`null`）：

```json
"gimmick": {"trigger": "OnHit", "effect": "IdentityStrike", "value": 120, "arg": 0, "cooldown": 0, "target": null,
  "strike": {"mode": "AfterDisplacementCritical", "element": "Dark", "shape": "ForwardArc", "range": 4.5, "width": 120, "maxTargets": 6, "windowSeconds": 3}}
"gimmick": {"trigger": "OnHit", "effect": "IdentityStrike", "value": 180, "arg": 0, "cooldown": 0, "target": null,
  "strike": {"mode": "ConsecutiveCritical", "element": "Dark", "shape": "ForwardLine", "range": 6, "width": 2, "maxTargets": 6, "windowSeconds": 4}}
```

会心モードの斬撃は生成ダメージとして実行する（仕掛け・橋・同じ斬撃への連鎖なし）。ホストのダメージ処理器（`rt.DamageDealt`）は生成ダメージでは補正の適用箇所の前で打ち切るため、この2モードだけは `FireIdentityStrike` が処理器がゲートの後に適用するのと同じ補正（構えに応じた与ダメージ増幅・「記憶の冴え」・中継窓・被ダメージ増加）をパケットごとに1回だけ自分で適用する（`ApplyMemoryPacketCorrections` ＋ `StrongestExposePercent`）。通常モードは `rt.DamageDealt` 経由のままなので、二重にはならない。

発生元はそれぞれ装備中の記憶『風の傷』／『一歩一殺』で、Identityに装着したエッセンス『神聖なる信仰』の本体のダメージ増幅・6秒の撃破追跡に入る。このエッセンスは斬撃の発動条件ではない。

#### 5b. MemoryTuning（名前付きの本体記憶の静的な変更）

発動イベントを持たず、その記憶を装備している間だけホストが適用する。`trigger: "OnUse"`、`arg: 0`、`cooldown: 0`、`target: null`、`value` は百分率。実行は `HostAuthority.MemoryTunings.cs`（一歩一殺）・`HostAuthority.StanceTuning.cs`（滅殺態勢）。

```json
"gimmick": {"trigger": "OnUse", "effect": "MemoryTuning", "value": 40, "arg": 0, "cooldown": 0, "target": null, "tuning": {"kind": "KillingFlowKeepSpeed"}}
```

| `tuning.kind` | `memory` | `value` | 効果 |
|---|---|---|---|
| `KillingFlowKeepSpeed` | `St_D_TheKillingFlow` | 0.01〜90（残す割合%） | 追加攻撃速度のこの割合は攻撃力に変換せず攻撃速度として残す（変換した攻撃力も同じ割合だけ戻す） |
| `KillingFlowOnHitHealScale` | `St_D_TheKillingFlow` | 100〜400（上限%。200 = 最大2倍） | 命中ごとの回復（吸血）に、変換で失った攻撃速度の倍率を掛ける |
| `StanceSwordQiAttackBasis` | `St_R_AnnihilationStance` | 100（固定） | 剣気が魔力ではなく攻撃力で伸び（係数は同じ）、物理ダメージになる |

2つの新機構は刻印のGrantには使えない（通常の星のみ）。`validate.py` が形を、`gen_cs.py` が `IdentityStrikeDefinition` / `MemoryTuningDefinition` の型付き生成を行う。設計は [v1.32 星の追加](../../docs/specs/v1.32-star-additions.md) C、実装と未検証事項は [v1.32-identity-strike-impl.md](../../docs/specs/v1.32-identity-strike-impl.md)。

### 選択肢（Choice の `options`）
`options[0]` = 選択肢A、`options[1]` = B。キーは `kind, memory, value, param, receiver, target, gimmick, power, stat, nameJa, nameEn`（この順・全て必須、使わない欄と無い名前は `null`）。`kind` は Choice/Keystone 以外。GimmickBoost/GimmickParam の選択肢は星と同じく `target` を持つ。旧 `label` は廃止（順序で表す）。
MemoryDamage／MemoryHaste／GimmickBoost／GimmickParamの選択肢は、キー順の `value` を `valueRef` に置き換える。nested欄は子kindのtyped payload参照を使う（上記参照）。


### 刻印（Keystone）
`keystone` は `upside`（文章）と `upsideSpec`（機械可読の配列、書いていなければ `null`）。`null` は「機械可読化していない」の意味で、**文章が正**。刻印にデバフ（代償）はもう無い。
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
MemoryDamage／MemoryHaste／GimmickBoost／GimmickParamの移行行は `value` を `valueRef` に置き換え、同IDの置換後効果を参照する。その他の種類も上記のnested参照を使う。
- 効果の欄は新規の星と同じ意味で、**置換後**の効果を書く。`value` は1段あたりの値、`maxRank` は既存の段数。段ごとに値が違うときは `gimmick.valuesByRank`。
- 旧効果の複数段Choice は `kind: "Choice"` と `options` 2つ。既存のStatのままの星は `kind: "Stat"` + `stat`。
- `kind: null` は置換後の機械表現が設計表に無い場合だけ。効果欄は全て `null` にして `notes` に設計表の記述を書く。
- `requires` / `requiresAny` は設計表がその既存星に**追加する取得前提**。接続の辺だけを足す変更は `notes` に書く。
- 既存刻印（`h.<hero>.key` / `key2`）は `kind: "Keystone"` と `keystone`（旧Powerの利点＋追加された代償）。

### 7b. 改訂（`revisions.json`）
すでに作った星（新規星や輪の中心）の中身をあとから作り替えるときは、その星のIDを `revisions.json` の `<hero>.stars` に列挙し、`revision`（2以上）を付ける。`gen_cs.py` が `AuthoredStarMigration` の改訂ルールを出力し、`AuthoredMigrationVersion` が改訂より古いセーブはその星を払い戻して取り直しにする（新規プロフィールは最初から最新の改訂）。
- ID は `<hero>.json` の星、または輪の中心（`h.<hero>.ring.*`）。同じIDを移行行（revision 1）と二重に挙げない。
- 空殻の改訂2は設計 [v2.4-husk-trio-starmap.md](../../docs/specs/v2.4-husk-trio-starmap.md)。
- 橋の星が**隣の橋の印**を `gimmick.condition` に使うときは、生成器がその記憶を `MemoryOwnership.SourceMemories` に明示する（自分の橋の2記憶以外を使うための所有の宣言）。

### その他
- 星の `mechanisms` は C01〜C15。`maxRank` は新規星では通常1。`rankCost` は新規星が1、旧星の費用は維持（移行行には書かない）。

## v1.32 の追加：通貨の星と「遠征の鍛錬」（RunGrowth）

設計は [v1.32-star-additions.md](../../docs/specs/v1.32-star-additions.md) の A・B。エンジンは新しい `Power` と型付き機構で、行の形は次のとおり（正準形の他の規約はそのまま）。

### A. 通貨（既存の `kind: "Notable"` + `power`、新しい Power 名だけ）

`power.name` に次の Power を書くだけで、生成・検証は既存の経路（`kind: "Notable"`・`power: {name, perRank}`・`maxRank: 1`）を通る。値は%。合計は Build が `Content.PowerCap` で打ち切る（設計の合計上限）。

| `power.name` | 効果 | 合計上限 |
| --- | --- | --- |
| `KillGoldPct` | 撃破ゴールド +perRank%（`monsterKillGoldMultiplier` へ加減算） | 12 |
| `EliteKillGoldPct` | エリート・ボスの撃破ゴールドがさらに +perRank%（ホストが追加分を付与） | 25 |
| `DreamDustPct` | 自分で拾う夢のダスト +perRank%（仲間の贈り物・MODの取引には掛けない） | 12 |
| `DreamDustDelvePct` | 夢のダスト +perRank%、**潜行中はさらに +perRank%**（「夢屑の籠」。常時分と潜行分が同じ値で付く） | 10 |

```json
"kind": "Notable", "power": { "name": "KillGoldPct", "perRank": 4 }, "maxRank": 1
```

夢のダストの合計は「夢屑の集め」3星×4% ＋ 籠10% ＝ 22%（潜行中は籠の分がもう10%付いて32%）。上限に収まるよう `DreamDustPct` は小さな星 4% ×3 まで、`DreamDustDelvePct` は 10 だけを使う。

### B. 遠征の鍛錬（`kind: "RunGrowth"` と `"RunGrowthMod"`、任意の欄 `growth`）

`growth` 欄は `power` の直後に置く**任意の欄**で、`kind` が `RunGrowth` / `RunGrowthMod` の行（と、Choice の選択肢）にだけ付く。ほかの効果欄（memory/value/param/receiver/target/gimmick/power/stat/options/keystone）は null。移行行には使えない。

入口の重要な星（仕組みを得る。生成物は `ClusterStarKind.Notable` + `RunGrowthDef`）：

```json
"kind": "RunGrowth",
"memory": null, "value": null, "param": null, "receiver": null, "target": null, "gimmick": null, "power": null,
"growth": {
  "trigger": "DamageTakenMaxHpPct",
  "threshold": 10,
  "cap": 60,
  "effects": [ { "stat": "Armor", "amount": 1 }, { "stat": "MaxHealthFlat", "amount": 4 } ]
},
"stat": null, "options": null, "keystone": null
```

- `trigger`：`DamageTakenMaxHpPct`（受けて失ったHPが最大HPの threshold% に達するたび）、`ShieldAbsorbedMaxHpPct`（障壁が吸収した量が最大HPの threshold% に達するたび）、`ParrySuccess`（ミストのパリィ `St_R_Parry` の成功 threshold 回ごと）、`CritBasicAttackKill`（会心の基本攻撃でとどめ threshold 回ごと）。
- `threshold`：HP系は 1〜100（最大HPに対する%）、出来事系は 1〜1000（回数。ふつう 1）。
- `cap`：1〜500。`effects`：1〜4個。`stat` は `Stat` の名前（エッセンス枠・`SacrificeReduction`・`FourthAttackShift` は不可）、`amount` は**1スタックあたり**で、千分の一まで（0.5、0.3 など）。`ShieldPower` / `HealPower` / `SummonPower` は戦闘の処理器が読み、そのほかはヒーローの能力値に足される。

修飾（上限 +N／1スタックの効果 +X%／溜まる速さ2倍）。小さな星は `kind: "RunGrowthMod"`、2択は Choice の `options` の各要素に同じ形で書く：

```json
"kind": "RunGrowthMod",
"memory": null, "value": null, "param": null, "receiver": null, "target": null, "gimmick": null, "power": null,
"growth": { "target": "husk.run.g1", "capBonus": 20, "effectPct": 0, "doubleGain": false },
"stat": null, "options": null, "keystone": null
```

- `target`：修飾する RunGrowth 星のID（同じ旅人。`kind` が `RunGrowth` であること）。null ならその旅人のすべての RunGrowth。
- `capBonus`（0〜200）、`effectPct`（0〜500、1スタックの効果への上乗せ%）、`doubleGain`（true で1回の到達で2スタック。重ねても2倍まで）。どれも動かさない行は不可。段数（`maxRank`）を掛けて加算される。

Choice の選択肢（`OPT_KEYS` に `growth` が加わる。`options` の各要素のキー順は `kind, memory, value, param, receiver, target, gimmick, power, growth, stat, nameJa, nameEn`）：

```json
{ "kind": "RunGrowthMod", "memory": null, "value": null, "param": null, "receiver": null, "target": null, "gimmick": null, "power": null,
  "growth": { "target": null, "capBonus": 0, "effectPct": 50, "doubleGain": false }, "stat": null, "nameJa": "…", "nameEn": "…" }
```

スタックの扱い：ホストが旅人（プレイヤー）ごとに HeroRuntime の外で持ち、死亡・ゾーン移動・旅人の再生成でも残り、遠征の開始（`GameManager.runId` の変化）で0に戻る。協力プレイの復帰用に `RunRecoveryState`（`growthRunId` / `growth`）へ保存する。星の取得を外すと効果は外れるが、スタックはその遠征の間は残る。

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

## Read-only star text inventory (Issue #101, stage A)

Run from the repository root; this does not regenerate manifests or change product text:

```sh
dotnet run --project tools/WikiGen -- --export-stars ~/dev/sod-prompts/stars
```

If only a newer .NET runtime is installed, build `tools/WikiGen/WikiGen.csproj` and run
`dotnet --roll-forward Major tools/WikiGen/bin/Debug/net8.0/WikiGen.dll --export-stars ~/dev/sod-prompts/stars`.

- `<hero-slug>.tsv` uses the installed `HeroSigils.TreeFor` trees; `generic.tsv` includes the fallback `Content.Talents` tree.
- Columns: `star_id`, `kind`, `display_ja`, `display_en`, `typed_effect_json`, `partners_json`, `name_ja`, `name_en`, `template`, `wiki_ja`, `wiki_en`, `display_variants_json`.
- A choice has one parent row and two candidate rows (`choice-A`, `choice-B`); candidates are effects, not extra purchased stars. Identify rows by file, star ID and kind, not ID alone.
- Display bodies come from `StarMapPresentation`. Variants preserve choice cards/selected states and rank-dependent legacy combos. Live tooltip allocation/equipment wrappers are inventoried separately, not evaluated against a fabricated save.
- Wiki bodies follow `StarMapWiki`'s existing formatting before table escaping. `wiki_en` evaluates the same branch in English; the existing Wiki publishes effect bodies in Japanese only.
- Typed JSON retains model type tags, enum names, selectors, trigger/target/condition/budget data, authored units, rank tables, caps and directly referenced effects. Powers retain their native enum/value contract rather than inferring structured conditions from prose.
- `partners_json` classifies referenced `St_*`/`Gem_*` tokens and transcribes both official names through `Links.Name`; slot selectors remain selectors in typed data, not invented fixed partners.
- `template-counts.tsv` lists observed formatter branches with distinct parent-star, direct-use and candidate-use counts. `text-template-counts.tsv` additionally lists bilingual rendered shapes after lexical replacement of names, numbers and colors; these are text shapes, not a semantic equivalence claim.
- `surface-templates.tsv` reads bilingual literal legend/tooltip/state/action templates from the current UI source and records source lines. Interpolations retain placeholders; these shared templates have no per-star usage denominator.
- Files are UTF-8 TSV with `\\`, `\t`, `\r`, `\n` escaping inside cells. Decode this escaping before parsing JSON cells. Rich-text tags are preserved; original full bodies are never rewritten by template normalization.
## 星図のオフスクリーン描画・比較（issue #106）

登録済みのゲーム用レイアウトをSVGで保存し、`rsvg-convert` がPATHにあればPNGも保存する。レポートは既定で出力先の `metrics.md`。リポジトリのルートから実行する。

```sh
dotnet run --project tools/StarMapRender -c Release -- /home/wang/dev/sod-prompts/starmaps/after --before /home/wang/dev/sod-prompts/starmaps/before/metrics.md
```

`--before <metrics.md>` は以前のStarMapRenderの生メトリクス表を読み、旅人ごとの線交差、線が星の上に通る組数/線数、線長CV、最近傍が他群である割合を before → after で追加する。交差・組数が各beforeの1/3以下、混在率が10%以下かをPASS/FAILで示す。現在の生メトリクス表と定義は残す。BeforeのCV・混在率は元表の丸め値であり、Afterの混在率の判定は丸め前の実測値を使う。未達の構造的原因は集計だけから推定せず、SVG/PNGなどの根拠とともに別途記録する。

`--before` を省くと現在の生メトリクスのみを記録する。`--max-edge 2000` は画像の最大辺（400以上）、`--no-png` はSVGのみ、`--metrics <file>` はレポート先の変更（`-` は標準出力）。

標準出力とレポートには初回 `RegisterAllGenerated()` の全旅人構築・登録時間を記録する（静的初期化/JIT込み、dotnet build・プロセス起動・描画・集計は除外）。旅人別時間はその後の `RegisterGeneratedHero` 1回による**暖まったプロセスでの再構築・登録**で、Coldではない。Cached `ForHero` は1回ウォームアップ後の10,000回平均µs/callであり、レイアウト構築時間ではない。

### 配置の最適化と検査（`--optimize` / `--check`）

星の**座標だけ**を最適化する。星ID・線（隣接）・取得条件・セーブは変えない。結果は座標表 `src/SodRpg.Core/Game/StarMapPlacements.Generated.cs`（生成物。手で編集しない）に焼き込み、`HeroTreeLayout` が既定の配置の上に当てはめる。表の作成時と星の集合（ID）が違うとき（星の追加・削除・ID変更の後）は当てはめず、既定の配置に戻る。

```sh
# 検査：星の重なり・線が星の上を通る数・線の交差・重なって見える線（近接／束）・最長の線を旅人ごとに出し、表が古ければ失敗する（終了コード1）
dotnet run --project tools/StarMapRender -c Release -- --check

# 再生成（全旅人。数分かかる）。--hero Cetus で一人だけ（ほかの旅人の座標は今の表のまま残る）、--iterations N で反復回数（既定 300万）、--seed N、--set 項目=値 で重みなどを変える
dotnet run --project tools/StarMapRender -c Release -- --optimize src/SodRpg.Core/Game/StarMapPlacements.Generated.cs --iterations 8000000

# 線の交差を旅人の中の種類別に数える（どの線の組が交わっているかの切り分け用）
dotnet run --project tools/StarMapRender -c Release -- --diag Hero_Cetus
```

- 方法：焼きなまし法。動かすのは星団ごと（平行移動・回転・鏡映・つながる相手へ寄せる）、同じ区画の2つの星団の入れ替え（ルートに沿った並び順を変える）、星団の中の星1つずつ（形の微調整）、橋のアクセス星・外縁の起点星・刻印、ルートの星（`RouteSlack` の範囲で道筋から外れてよい）。始まりの星と幹の内側の星は動かさない。
- 評価：線の交差、交わらなくても近づく線（`TouchDist` 未満）、同じ星から出て並んで走る長い線（束）、線が星の上を通る数（円盤＋余白）、線の長さ（長いほど強く減点）、星団の形・幹の形からのずれ。元から絡んでいる星団は形を保つ制約をゆるめて解く。
- 守る条件（必ず満たす）：星どうしの間隔（同じ星団86／別の星団140／そのほか90〜110、刻印どうし160）、星団の外側へ他の星が入り込まない（凸包）、星団は記憶の扇形（ルートの区画）の中、前提の星は後続の星より内側（記憶の星団）。
- 検査は `StarMapQuality.Measure`（Core）で、`tests/SodRpg.Core.Tests/StarMapLayoutQualityTests.cs` と `--check` が同じ計測を使う。テストは「座標だけが変わる」「重ならない」「線が星の上を通る数が上限以下」「最適化前より交差・長い線・線の平均長が減る」「表が現在の星に合っている」を確かめる。
- ケトゥスの座標は、上の重み（現在の既定値：近接6・束3・交差20・`RouteSlack` 3000・入れ替え5%）で作り直した。ほかの旅人の座標は以前の重み（近接・束の項なし、交差10、`RouteSlack` 100、入れ替えなし）で作ったままなので、全旅人を再生成すると全員の配置が変わる。
- 星や前提を変えて表が古くなったら `--check`（またはテスト）が知らせるので、上のコマンドで再生成する。表は決定的（同じ入力・同じ反復回数・同じ種なら同じ結果）。

