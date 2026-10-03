# v1.31 星の機械可読マニフェスト

設計表 `docs/specs/v1.31-clusters-*.md` の**新しい星**（旅人固有の新規ID・刻印、共有外縁160）を、1星1オブジェクトの JSON に書き起こしたもの。C01（AuthoredStarContract）の登録APIができたら、このJSONから C# のデータを生成する。設計の正は設計表と [正式仕様](../../docs/specs/v1.31-new-mechanisms.md)・[レビュー](../../docs/specs/v1.31-design-review.md)。食い違いはJSON側で勝手に直さず `notes` に書く。

ファイル：`tools/star-manifest/<hero>.json`（vesper, lacerta, cetus, yubar, husk, mist, nachia, aurena, bismuth）と `outer.json`（共有外縁、heroは "shared"）。

## 形式

```json
{
  "hero": "Hero_Cetus",
  "source": "docs/specs/v1.31-clusters-cetus.md",
  "stars": [
    {
      "id": "cetus.mem.icy-veins.c1.e1",
      "region": "memory | bridge | outer | keystone",
      "cluster": "cetus.mem.icy-veins.c1",
      "shape": "fan | ring | chain | null",
      "anchor": "既存の星ID、または同じ星団の星ID（入口のみ）",
      "edges": ["つながる星ID", "..."],
      "requires": ["購入の前提の星ID（全部）"],
      "requiresAny": ["どれか1つ"],
      "kind": "MemoryDamage | MemoryHaste | GimmickBoost | GimmickParam | Notable | Choice | Stat | Keystone",
      "memory": "St_D_IcyVeins または null",
      "value": 1.0,
      "param": "Duration | Radius | ExtraTargets | Chance | null",
      "receiver": "受け手の記憶（RB・T など受け手を指定する星）または null",
      "gimmick": {"trigger": "OnUse|OnHit|OnKill|OnCrit|OnBasicAttack", "effect": "Recharge など GimmickEffect 名", "value": 4, "arg": 0, "cooldown": 0, "target": "受け手の記憶 または null"},
      "power": {"name": "Power 名", "perRank": 0},
      "stat": {"name": "Stat 名", "perRank": 0},
      "options": [ {同じ形の効果オブジェクト}, {…} ],
      "keystone": {"upside": "…", "downside": "…"},
      "mechanisms": ["C03", "C04"],
      "maxRank": 1,
      "rankCost": 1,
      "nameJa": "自然な日本語の名前",
      "nameEn": "English name",
      "notes": "設計表との食い違い・要確認（なければ空文字）"
    }
  ]
}
```

- 使わない欄は `null`（配列は `[]`）。`Choice` は `options` にちょうど2つ。`Keystone` は `keystone` と、効果を表す `power`/`gimmick`/`options` のいずれか。
- `value` は設計表の単位のまま（1 = 1%、確率は%ポイント）。整数化しない。
- 名前は設計表にあればそれを使い、無ければ効果が分かる短い自然な日本語と英語を付ける。人名は書かない。
- 既存の星（`h.*`）は書かない。ただし設計表が既存の星の効果を**変える**と書いている場合は `region: "migration"` で1件書き、`notes` に変更内容を書く。

## 検証

```
python tools/star-manifest/validate.py            # 全部
python tools/star-manifest/validate.py cetus      # 1人
```
新規ID数がレビューの表（New private IDs、外縁は160）と一致すること、IDの重複なし、kind ごとの必須欄、Choice の2択、mechanisms が C01〜C15、移動の記憶が起点（gimmick.trigger の発火元）になっていないこと、Stat は outer だけ、を確かめる。
