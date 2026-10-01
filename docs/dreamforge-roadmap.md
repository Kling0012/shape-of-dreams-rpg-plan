# Dreamforge RPG 開発計画

次に作るものを、実装の前に単独のコミットとしてここへ記す。完了した版は「完了」に移す。

## 方針（2026-10-01 改定）

Dreamforge は単独のゲームではなく **Shape of Dreams のMOD**。新しい要素は、本体の仕組み（Memory・Essence・属性・旅人の固有能力・祭壇・商人・ハンター・Limbo の深度）に乗り、それを強めるものを優先する。各項目に、土台にする本体のAPIを明記する。

## 次：v1.0「本体とのシナジー」

1. **属性の遺物**（本体の属性システムに乗る）
   - 特性に「火・冷気・光・闇の効果増幅」を追加（`BonusStats.fire/cold/light/darkEffectAmpFlat`。本体の `ApplyElemental` がそのまま `ampAmount` に使う）。
   - 固有効果「火種・霜・輝き・影」：通常攻撃の命中時に確率で該当属性を1スタック付与（`Actor.ApplyElemental`）。Lacerta（火）・Cetus（冷気）・Yubar/Aurena/Nachia（光）・空殻（闇）のキットと噛み合う。
   - 固有効果「四元の共鳴」：敵に4属性すべてが乗った瞬間に爆発（`EntityStatus.fireStack/hasCold/lightStack/darkStack`、星座「全属性ダメージ」と同じ方向）。
2. **4発目と噛み合う「烈火」**：自前の数え方をやめ、本体の4発目カウンタ（`EntityEvent_OnAttackFired.isThisAttackFourthAttack`）で発動。Vesper・Lacerta の4発目キットや `everyFourAttackStartIndex` と連動する。
3. **Memory と噛み合う固有効果**
   - 「回避の残響」：回避（Movement）を使うたびに Memory のクールダウンを短縮（スキル使用イベント＋`ApplyCooldownReduction`）。
   - 「終の昂り」：Ultimate（R）を使うと数秒間 攻撃力・魔力が上がる。
4. **本体の行動を依頼にする**：Chaos の祭壇を使う（`OnChaosUsed`）、商人で買う（`OnItemBought`）、Memory/Essence を強化（`OnItemUpgraded`）、Essence を合成（`OnGemMergeUpgraded`）、分解（`OnDismantled`）、ハンターの領域に入る（`OnCurrentHuntLevelChanged`）。
5. **調査して可能なら**：悪夢化エリートに本体のエリート効果（`MirageSkin` 系の見た目と挙動）を付ける。
6. **試験**：属性特性・付与の確率・四元判定・4発目・回避短縮・新しい依頼の進み方、保存の往復。

## その後の候補

- 悪夢の契約を本体の呪い（`CurseStatusEffect`）・Evil の明晰夢と結び付ける
- 夢の商人の支払いをラン内のゴールドに（ホスト経由で `SpendGold`）、遺物の分解でドリームダスト
- 開始深度と本体の Limbo 深度の表示・係数をそろえる

- 悪夢化エリートの専用接頭効果（分裂・吸魂など、ゲーム側の挙動確認が要るもの）
- キャラ別の固有課題（計画書 第8章）と専用の刻印
- 依頼の掲示板（遠征前に3つから選ぶ・引き直し）

## 完了

- v0.1：遺物・確保と深度・遺失物・星図・鍛冶・協力
- v0.2：狙い系統・系統セット・遠征結果
- v0.3：依頼
- v0.4：悪夢の契約・合成・図鑑の節目
- v0.5：悪夢化エリート
- v0.6：今日の夢・旅人の熟練度
- v0.7：開始深度（深淵の段階）・固有効果4種・固有品4種
- v0.8：夢の工房（恒久強化6種）・依頼の引き直し
- v0.9：夢の出来事・セット遺物
