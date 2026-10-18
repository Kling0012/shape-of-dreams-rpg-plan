# 旅人と星図 / Travelers and Star Map

[ホーム / Home](Home) · [遺物と鍛冶 / Relics](Relics-and-Forge)

## 旅人ごとの成長 / Per-traveler progression

星の経験と割り当ては旅人ごとに保存します。本体の夢レベルとは別の成長です。

Star experience and allocations are saved per traveler, independently of the game's Dream Level.

| 旅人 / Traveler | データ上の名前 / Data name |
| --- | --- |
| ヴェスパー / Vesper | Hero_Vesper |
| ラセルタ / Lacerta | Hero_Lacerta |
| ケトゥス / Cetus | Hero_Cetus |
| ユバール / Yubar | Hero_Yubar |
| 空殻 / Husk | Hero_Husk |
| ミスト / Mist | Hero_Mist |
| ナキア / Nachia | Hero_Nachia |
| アウレナ / Aurena | Hero_Aurena |
| ビスマス / Bismuth | Hero_Bismuth |

個々の星の名前・効果・前提条件は[旅人別の生成済み星図](https://github.com/Kling0012/shape-of-dreams-rpg-plan/tree/wiki-pages/pages/dreamforge) の `starmap_*` ページにあります。ゲームと Wiki は同じ説明処理を使います。

The generated `starmap_*` pages list each traveler's stars, effects and requirements. The game and Wiki share their description logic.

## ポイントと刻印 / Points and keystones

経験で得られる星図ポイントは最大500です。k個目に必要な経験は `6k + 50`、n個目までの累計は `3n² + 53n` です。図鑑は遺物6種類ごとに1ポイント、最大4ポイントを追加します。

Experience grants up to 500 Star Map points. Point k costs `6k + 50` experience, and the cumulative cost through point n is `3n² + 53n`. The codex adds one point per six relic types, up to four points.

撃破による経験の基本値は通常1、エリート5、ボス20で、悪夢では2倍です。確保の基本値は20、踏破は100です。これらは基礎値で、深度や道標などによる補正があります。

Base kill experience is 1 for normal enemies, 5 for elites and 20 for bosses, doubled for nightmares. Securing has a base reward of 20 and victory 100. Depth, waypoints and other modifiers can change the final reward.

刻印の同時選択枠は初期1枠で、星のレベル200と400で1枠ずつ増え、最大3枠です。枠が空いていても、個々の刻印の取得条件とポイントは必要です。

Keystone slots start at one, increase at star levels 200 and 400, and cap at three. An open slot does not waive a keystone's requirements or point cost.

## 5つのロードアウト / Five loadouts

F6 の星図から、旅人ごとに5つの割り当てを保存・切り替えできます。保存するのは星・選択肢・刻印の割り当てです。経験・ボーナスポイント・装備はロードアウトごとに増えず、その旅人で共有します。

Use the F6 Star Map to save and switch five allocations per traveler. Loadouts store stars, choices and keystones. Experience, bonus points and equipment are shared by that traveler rather than duplicated per loadout.

切り替え・コピー・名前の変更は遠征の外で行います。中断した遠征が残っている間も変更できません。保存した割り当てが現行の星図や予算に合わなければ、読み込み時に無効な星を無料で払い戻します。

Switch, copy and rename outside expeditions. A suspended expedition also blocks changes. Loading repairs saved allocations against the current tree and budget, refunding invalid stars for free.

根拠 / Sources: [HeroNames](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/HeroNames.cs), [StarProgression](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/StarProgression.cs), [Star progression balance](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/star-progression.json), [Content](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Content.cs) (`CodexPerPoint`, `MaxCodexBonus`), [StarLoadouts](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/StarLoadouts.cs), [StarMapWiki](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/WikiGen/StarMapWiki.cs), [中断と再開 / Suspension and resume](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/README.md).
