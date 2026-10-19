# 下方修正の履歴 / Nerf history

[ホーム / Home](Home) · [旅人と星図 / Travelers and Star Map](Star-Map)

過去の更新で、エッセンスと記憶が以前の版より弱くなった変更（下方修正）を一覧にします。値は v3.8.20（2026-10-19）時点です。

This page lists every change that made essences or memories weaker than they were in an earlier version. Values are as of v3.8.20 (2026-10-19).

調べた範囲は、更新履歴（CHANGELOG.md）と数値原本（`tools/balance/essence-caps.json` など）に記録のある全期間（v0.2.0 〜 v3.8.20）です。次のものは載せません。

- 上方修正だけを受けた項目、変更のない項目。
- 前後の値が記録にない効果。推測の数値は書かず、該当欄に「不明」と示します。
- 対象が遺物（装備）で、エッセンス・記憶ではない変更（ページ末尾に主なものを挙げます）。

The scope is every version recorded in the changelog and the balance data (v0.2.0 through v3.8.20). Buff-only items, unchanged items, and relic-only changes are not listed. Where before/after values are not recorded, the entry says "unknown" instead of guessing.

## 現在も有効な下方修正 / Nerfs currently in effect

| 対象 / Target | 弱くなった点 / Weakened effect | 変更前 → 変更後 / Before → after | 導入版 / Introduced |
| --- | --- | --- | --- |
| エッセンス「跳弾」（本体 / Gem_R_Ricochet） / the native Ricochet essence | 跳弾の発生確率 / ricochet chance | 上限なし → 60%で頭打ち。60%を超えた分は発動しない / no cap → capped at 60%; anything above 60% never fires | v3.0.0（2026-10-16） |
| 固有効果「結晶の循環 / Crystal Circuit」 | エッセンスの発動で通常記憶のクールダウンを短くする頻度 / how often the cut can hit the same memory | 同じ記憶への短縮に待ち時間なし → 0.5秒に1回まで（内部クールダウン0.5秒）。0.5秒以内に複数回エッセンスが発動しても、同じ記憶が短縮されるのは1回だけ / no wait → the same memory at most once per 0.5 s | v3.0.0（2026-10-16） |
| 星図のエッセンス枠の星（「記憶の器を広げる」「回避の器を広げる」） / the star-chart essence slot stars | 星図で増やせるエッセンス枠の段数 / ranks of essence slots bought from the star chart | 1つの記憶につき最大6段 → 最大4段（−2段。星図全体では合計8段まで） / up to 6 ranks per memory → 4 ranks (−2; 8 in total across the chart) | v2.10.6（2026-10-08） |

- 跳弾の上限と結晶の循環の内部クールダウンは、エッセンスと記憶の組み合わせで連鎖が止まらず、処理が重くなったりゲームが落ちたりする問題への対策として入りました（v3.0.0）。対策のうち強さだけを削る部分は同じ日のうちに無効化しました（下の表）。連鎖を止める側の対策は、この2つと「ストック付きの記憶の再使用間隔」（後の節）の3つでした。
- 星図の枠の段数は、本体が1つの記憶に4個を超える枠を表示できない不具合への対処です。5・6段を取っていた旅人は4段に切り詰められ、使ったポイントは戻ります。同じ版で、混沌の聖堂で増やした枠と足しても、書き込む枠の上限は1つの記憶につき4個までになりました。

## 一度適用され、後に戻した下方修正 / Nerfs applied once and later reverted

| 対象 / Target | 弱くなった点 / Weakened effect | 変更前 → 変更後 / Before → after | 適用された版 / Applied in | 戻した版 / Reverted in |
| --- | --- | --- | --- | --- |
| ストック付きの記憶（Q・W・E・R） / stocked memories | ストックが残っていても、1回使うごとに入る待ち時間 / extra wait after each use even with charges left | 待ち時間なし → 基礎クールダウンの20%（最低0.3秒）。例: 基礎クールダウン12秒・3回分の記憶は、残り2回あっても約2.3秒使えなかった / no wait → 20% of the base cooldown, at least 0.3 s | 2026-10-16 の版。v3.0.0〜v3.1.0 のいずれか（導入の告知は更新履歴にないため、版は不明） / one of v3.0.0–v3.1.0 (the introduction was not announced, so the exact version is unknown) | v3.2.12（2026-10-17） |

- v3.2.12 でこの待ち時間を廃止しました。ストックが残っている間は続けて使い、予約もできます。ストックが回復するまでの時間は変わっていません。最後の1回を使ったあと、先に回復したストックもすぐ使えます。

## 上限の定義があり、公開版では無効のもの / Caps defined but off in releases

数値原本には次の上限の定義がありますが、2026-10-16 の方針変更で「強さを削るだけ」の上限は無効化され、v3.8.20 現在も無効です。有効だった公開版があるかは記録にないため、適用期間は不明とします。

| 対象 / Target | 上限の内容（有効な場合） / Cap when enabled | 現在の状態 / Now |
| --- | --- | --- |
| 品質で伸びるエッセンス効果（GemDefault の品質倍率） / quality-scaled essence effects | 品質300を超えた分の伸びが3分の1（300 +（品質 − 300）÷ 3） / quality above 300 counts one third | 無効。品質に対して線形のまま / off; scaling stays linear |
| エッセンス「熱病」（Fever） / the Fever essence | 火スタック由来の爆発ダメージ増幅が +150% まで / explosion damage amplification from fire stacks capped at +150% | 無効 / off |
| エッセンス Embertail（Gem_L_Embertail。日本語名は記録になし） | クールダウンの乗算短縮が 50% まで / multiplicative cooldown reduction capped at 50% | 無効 / off |
| エッセンス Epiphany（Gem_R_Epiphany。日本語名は記録になし） | 究極技のクールダウン還元率が 60% まで / ultimate cooldown refund capped at 60% | 無効 / off |
| 連携と星の「記憶加速」 / Memory Haste from links and stars | 1回の短縮で残りクールダウンの最大60%まで / one proc shortens at most 60% of the remaining cooldown | 無効。従来どおり最大クールダウン基準・100%上限 / off; the legacy full-cooldown basis and 100% cap apply |

## 下方修正として載せなかったもの / Not counted as nerfs

- 連携の「記憶加速」の覚醒後の上限90%（v1.27.0）。導入時からの設計上限で、それより強かった版の記録がないためです。 / The 90% ceiling on awakened Memory Haste links (v1.27.0) is a design limit from their introduction; no earlier, stronger version is recorded.
- レベルで増えるエッセンス枠（初期設定は10レベルごと +1、上限4〜8）。設定項目で、過去の既定値から減った変更はないためです。 / Level-based essence slots are a setting; their defaults were never lowered.
- 熱病のエッセンスの爆発の軽量化（v2.10.21）。明るさ・画面の揺れなど見た目だけで、ダメージと範囲は不変のためです。 / The Fever explosion lightening (v2.10.21) caps only visuals; damage and area are unchanged.
- 装備（遺物）への上限。条件つき攻撃力・魔力の覚醒前120%の共通上限（v1.27.0）など。対象が遺物で、エッセンス・記憶ではないためです。 / Caps on relics, such as the shared 120% pre-awakening ceiling on conditional attack and power (v1.27.0), target gear, not essences or memories.

根拠 / Sources: [更新履歴 / Changelog](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/CHANGELOG.md)（v2.10.6・v2.10.21・v3.0.0・v3.2.12 の各項目）、[essence-caps.json](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/essence-caps.json)、[バランスデータの説明 / Balance data notes](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/README.md)（本体エッセンスと記憶の循環抑制）、[EssenceCaps.Generated.cs](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Balance/EssenceCaps.Generated.cs)、[NativeEssenceCaps.cs](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/NativeEssenceCaps.cs)、[HostAuthority.SupportPowersV129.cs](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/HostAuthority.SupportPowersV129.cs)、[HeroStarRoutes.cs](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/HeroStarRoutes.cs)、[NewPowersV129.cs](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/NewPowersV129.cs).
