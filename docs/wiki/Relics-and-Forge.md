# 遺物と鍛冶 / Relics and forge

[ホーム / Home](Home) · [旅人と星図 / Star Map](Star-Map) · [協力取引 / Trade](Co-op-Trade)

## 装備の枠と育成 / Slots and progression

遺物には武器・頭・胴・手・足・護符の6枠があります。レア度はコモン、アンコモン、レア、エピック、伝説の5段階です。土台・固有品・セット・能力の一覧は [装備データ](https://github.com/Kling0012/shape-of-dreams-rpg-plan/tree/wiki-pages/pages/dreamforge) を参照してください。

Relics occupy six slots: weapon, head, armor, hands, feet and charm. The five rarities are Common, Uncommon, Rare, Epic and Legendary. See the equipment data for bases, uniques, sets and effects.

| 育成 / Method | 対象と効果 / Requirements and effect |
| --- | --- |
| 強化 / Enhancement | 基本上限は+5。+3・+5・+10・+15・+20が節目 / Base cap +5; milestones at +3, +5, +10, +15 and +20 |
| 限界突破 / Limit break | 上限まで強化した遺物の上限を1回につき+5。同じ枠・同じレア度以上の別の遺物と素材を消費 / At the enhancement cap, consume another relic of the same slot and equal or higher rarity plus materials to raise the cap by 5 |
| 再調律 / Retune | 特性1つに対して3候補を提示。候補を選ぶか元の特性を保つ / Generate three choices for one affix; choose one or keep the original |
| 覚醒 / Awakening | 装着中の伝説が撃破で成長し、3段階で固有効果と特性を強める / Equipped Legendary relics gain power from kills and strengthen powers and affixes across three stages |
| 融合 / Fusion | 同じ名前・同じレア度の別の遺物を消費し、能力値を増幅 / Consume another relic of the same name and rarity to amplify stats |
| 禁忌化 / Forbidden crafting | 工房の解放後、対象の固有品に利点と欠点を付ける / After unlocking the workshop upgrade, add benefits and drawbacks to an eligible unique |

限界突破はコモン・アンコモンでは不可、レアは1回、エピックは2回、伝説は3回です。最大強化値は順に+5・+5・+10・+15・+20になります。材料の遺物は保管庫にある鍵なし・未装着・取引予約や再調律の応答待ちでない品から選びます。

Common and Uncommon cannot limit break; Rare allows one break, Epic two and Legendary three. Their maximum enhancement levels are +5, +5, +10, +15 and +20 respectively. Material relics must be in the stash, unlocked, unequipped, unreserved and not awaiting a retune choice.

## 再調律の上限と費用 / Retune limits and cost

通常の再調律は遺物1個につき最大3回です。工房の「禁忌の職人」を解放すると、セットに属さずボスの技も持たないオレンジ枠の固有品だけ、上限が3回増えて最大6回になります。禁忌化した遺物は再調律も特性の洗い直しもできません。

A relic normally allows three retunes. Unlocking the Forbidden Smith adds three, for a total of six, only for orange-frame uniques without a set or boss move. Forbidden relics cannot be retuned or have their affixes rerolled.

候補を出すと調律石と回数を消費します。元の特性を残しても戻りません。候補は保存され、選び終えるまで次の再調律には進めません。

Generating choices consumes tuning stones and one use. Keeping the original does not refund them. Choices are saved, and must be resolved before another retune.

## 覚醒 / Awakening

装着した旅人で敵を倒すと、伝説の遺物に覚醒の力が溜まります。累計の必要量と倍率は次のとおりです。

Legendary relics gain awakening power when the traveler wearing them defeats enemies. Thresholds are cumulative.

| 覚醒 / Stage | 累計必要量 / Cumulative threshold | 固有効果 / Powers | 特性 / Affixes |
| --- | ---: | ---: | ---: |
| I | 5,000 | 125% | 110% |
| II | 15,000 | 150% | 120% |
| III | 37,500 | 180% | 130% |

## 融合 / Fusion

融合は確保地点か遠征の外で使えます。育てる品と材料の両方を保管庫に用意し、鍛冶で対象と材料を選び、確認します。材料の遺物は消費されます。

Fusion is available at a secure point or outside an expedition. Both target and material must be in the stash. Select them in the forge and confirm; the material relic is consumed.

- 材料は同じ名前・同じレア度の別の遺物です。鍵なし・未装着・取引予約中でも再調律中でもない品を使います。
- 欠片の費用はコモン20、アンコモン40、レア60、エピック80、伝説100です。固有品は調律石1個も必要です。
- 最大5段で、1段ごとに能力値を6%、最大30%増幅します。負の能力値も増幅します。
- 固有効果・連携・セット効果は融合で増幅しません。

- Use another relic of the same name and rarity that is unlocked, unequipped, unreserved and not retuning.
- Shard costs are 20/40/60/80/100 for Common/Uncommon/Rare/Epic/Legendary. Uniques also cost one tuning stone.
- Each of the five levels amplifies stats by 6%, up to 30%. Negative stats are amplified too.
- Fusion does not amplify powers, links or set effects.

融合済みの遺物は融合に対応しない旧版のホストや相手とは交換できません。

Fused relics cannot be traded with a host or partner running a version without fusion support.

## 禁忌の職人 / Forbidden Smith

工房で欠片2,000個と調律石30個を使って解放します。宿敵化したボスから禁忌の核を入手し、セットに属さずボスの技も持たないオレンジ枠の固有品を一度だけ禁忌化できます。赤枠のセット品は対象外です。禁忌化後に再調律を続けることはできません。

Unlock the workshop upgrade with 2,000 shards and 30 tuning stones. Obtain forbidden cores from nemesis bosses, then make an orange-frame unique without a set or boss move forbidden once. Red-frame set items are excluded. Retuning is unavailable afterward.

根拠 / Sources: [Forge balance](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/forge.json), [Rules](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Rules.cs) (`LimitBreak`, `Retune`, `ChooseRetune`, `Fuse`, `OnKill`), [Forbidden](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Forbidden.cs), [Forbidden balance](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/forbidden.json), [Fusion](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Fusion.cs), [Fusion balance](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/fusion.json), [取引の互換性 / Trade compatibility](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/CoopTrade.cs).
