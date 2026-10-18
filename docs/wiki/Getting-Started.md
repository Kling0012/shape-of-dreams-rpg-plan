# 導入と基本操作 / Getting started

[ホーム / Home](Home)

## 導入と更新 / Installation and updates

1. [Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases) から `DreamforgeRPG-vX.Y.Z.zip` をダウンロードします。
2. zip を展開し、`DreamforgeRPG` フォルダを `<Shape of Dreams>/Mods/DreamforgeRPG/` に置きます。
3. ゲームを起動し、MOD の管理画面で Dreamforge RPG を有効にします。
4. 更新時は `DreamforgeRPG` フォルダを新しい版に置き換え、ゲームを再起動します。セーブデータは引き継がれます。

1. Download `DreamforgeRPG-vX.Y.Z.zip` from Releases.
2. Extract it and place the `DreamforgeRPG` folder at `<Shape of Dreams>/Mods/DreamforgeRPG/`.
3. Start the game and enable Dreamforge RPG in the MOD manager.
4. To update, replace the folder with the new version and restart the game. Saves carry over.

協力プレイでは参加者全員の版をそろえてください。ホストにも MOD が必要です。[保存先と再開の条件](Saves-and-Continue) も参照してください。

Use the same version for everyone in co-op, including the host. See Saves and Continue for the save location and resume requirements.

## キーと設定 / Controls and settings

| 既定キー / Default key | 操作 / Action |
| --- | --- |
| F6 | MOD メニューを開く。装備・鍛冶・星図・工房・交換・記録などを操作 / Open the MOD menu for gear, forge, Star Map, workshop, trade and records |
| F7 | 確保地点で「確保する」を選ぶ / Secure at a secure point |
| F8 | 確保地点で「深く潜る」を選ぶ / Delve at a secure point |

キー・言語・表示倍率・HUD の表示量・軽量化はゲームの MOD 設定から変更できます。検索欄は日本語の IME 入力に対応しています。

Change keys, language, UI scale, HUD detail and performance options in the game's MOD settings. Search fields support Japanese IME input.

## 確保と潜行 / Securing and delving

敵から手に入れた遺物は未確保のまま鞄に入ります。通常の遠征では新しいゾーンに着いたときに確保か潜行を選びます。開始直後で未確保品などがない場合は選択を表示しません。

Relics obtained from enemies enter the unsecured satchel. In a normal expedition, a new zone offers a choice to secure or delve. The opening zone skips the choice when there is nothing to secure or decide.

- **確保する**：鞄の遺物を保管庫へ移し、欠片と調律石を確保します。潜行深度は0へ戻ります。
- **深く潜る**：未確保品を持ったまま危険と報酬を増やします。
- **全滅する**：未確保の遺物は遺失物になり、次の遠征で取り戻せます。

- **Secure:** move satchel relics to the stash and secure shards and tuning stones. Delve depth resets to zero.
- **Delve:** retain unsecured loot while increasing danger and rewards.
- **Defeat:** unsecured relics move to Lost & Found and can be recovered in a later expedition.

インフィニティではボス後の帰還・潜行が進行の節目です。[専用ガイド](Infinity) を参照してください。

In Infinity, Return and Delve after a boss mark progression boundaries. See the Infinity guide.

## 鞄がいっぱいになったとき / Satchel overflow

基本容量は鞄30個・保管庫80個で、工房で広げられます。鞄があふれると低いレア度から、同じレア度なら低いスコアから欠片に変わります。取引予約中の品は対象外です。

Base capacity is 30 satchel relics and 80 stash relics, expandable in the workshop. Overflow converts lower-rarity relics first, then lower-score relics within the same rarity. Reserved trade assets are excluded.

| レア度 / Rarity | 1個あたりの欠片 / Shards per relic |
| --- | ---: |
| コモン / Common | 3 |
| アンコモン / Uncommon | 6 |
| レア / Rare | 12 |
| エピック / Epic | 30 |
| 伝説 / Legendary | 60 |

あふれた分にドリームダストを追加する設定は既定で OFF です。道標の報酬停止やインフィニティの供給上限も適用されるため、この表の量を常に受け取れるわけではありません。

The option to add Dream Dust on overflow is OFF by default. Waypoint reward suppression and Infinity supply limits still apply, so the table is not an unconditional payout guarantee.

根拠 / Sources: [README](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/README.md), [Rules](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Rules.cs) (`ShouldOfferSecurePoint`, `Secure`, `Delve`), [Content](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Content.cs) (`SatchelCapacity`, `StashCapacity`), [鞄あふれの説明 / Overflow details](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/README.md), [Forge balance](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/forge.json), [IME 対応 / IME support](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/CHANGELOG.md).
