# Issue #48：ボス限定の全部位セットと任意連携

## 1. 概要・確定方針

14セット・84部位を、実際のボス技と固有報酬の仕組みに沿って再設計した承認済み仕様書。段階AのDemon、B-1のSkoll／Infernus／白夜／暗月、B-2のNyx／Erebos／Seeker／Azurak／PrimusAeronに続き、B-3で光の精霊／大顎／オブリヴィアクス／Polarisを実装した。全14セット84部位を登録済み。Demonの993ee1dの遊び方・数値は維持し、全体で使う機構へ定義方式を共通化する。既存48セット・既存ID・本体報酬単独の挙動・§5のドロップ率は変更しない。数値は設計値であり、戦闘で較正済みではない。

| 項目 | 方針 |
| --- | --- |
| 対象 | `BossMonster` 派生15型から空実装の `Mon_Despair_BossCrawler` を除く14型。白夜・暗月は別セット |
| 構成 | Weapon / Armor / Charm / Head / Hands / Feet 各1点。通常の2/3/6ピース効果は累積。4/5専用の通常効果なし（任意連携には4部位段階あり） |
| 入手 | 対応ボス撃破ごと、各プレイヤーが独立抽選。当選時、そのセットからランダムな1部位のみ |
| 限定 | 他ボス・通常敵・汎用プール・宝箱・商人・製作等から新規取得不可 |
| 強さ | 既存6部位セットより一段強くする。2/3点は対応する既存セットの予算約1.4倍を参考、6点追加は既存中央値0.925の約2倍＝1.85を設計目安とする |
| 任意連携 | 自分の対応記憶／エッセンス装着＋対応セット2/4/6部位で段階強化。セット単独で成立、本体報酬単独は本体どおり（このセットの連携なし） |
| 報酬なし | PrimusAeron / Polaris は本体報酬との連携なし。追加の任意要素も今回は採用しない |
| 同期 | ボス型名・抽選条件はホストの撃破factが正。抽選結果は個人別、戦闘効果はホストが適用 |
| 名称・自立性 | 日英名は§3を採用。任意の2/3部位で段階機構が成立し、6部位も特定記憶・別の召喚手段を必須にしない |

### 段階A／B-1／B-2／B-3の実装境界

- Coreは`BossProfiles`／`BossMoveProfile`／`BossRewardProfile`、`UniqueDef.BossMove`、`SetDef.BossStages`へ接続。`Build.BossMoves`／`BossRewards`は独立した`b:`／`z:` codec節に入り、各64件上限・既知ID・重複・channel／stage検証を行う。`HostBuildValidation`は装備から再導出する。`Relic.AuthoredEffectCount`で強化の固有効果枠を扱い、旧Demonの保存Powerは読み込み時に撤去する。
- 共通runtimeは`HostAuthority.BossRuntime.cs`の`internal BossCombatState`。`TickBossEffects`／`ClearBossEffects`を統合入口に、native主撃・ConfirmedUse・移動完了・damage・報酬instance・時計／modeを有限actionへdispatchする。移動完了には検証済み出発点、記憶使用には実castのforwardを短いscopeで渡す。

| 機構 | 実装クラス |
| --- | --- |
| M1 | `BossShapeAttack` |
| M2 | `BossProjectileExecutor` |
| M3 | `BossFieldExecutor`（同owner/setで全予約tokenを合計4まで、Nyx／Erebosは全場2・最古置換。地点列は1token） |
| M4 | `BossMovementExecutor` |
| M5 | `BossEnemyMovementExecutor` |
| M6 | `BossDeployableExecutor`（非Entity射手の生成。native召喚は検証済み個体登録のみ） |
| M7 | `BossDefenseExecutor`（独立container・shield・StatBonusの個別除去、source別解除・実残量からの線形減衰・成功damage限定の短stun） |
| M8 | `BossProgressLedger`（印8対象・stack3・counter6・mode期限・activation gate、native親ごとのstack出所） |

- `HostAuthority.BossNativeAdapters.cs`はcast／instance／displacement寿命と帰属packetを結合し、`Se_U_Hysteria`の生成時に返った固有SpeedEffectのみ捕捉する。装備なしでも固定64枠の捕捉だけを保持し、装備変更時に既に生存する状態へ現行段階を適用できる。生成済み爪の再発行はしない。
- 段階B-1のnative報酬は`HostAuthority.BossGlacialCore.cs`／`BossEternalFlame.cs`／`BossBeam.cs`。Cold回復は元の最終native healへ有界加算して本体bankへ自然に入り、優先敵と実射加速は本体の射出だけに適用。EternalFlameはown curseの差分・成功proc・最初のnative packetだけを変更。白夜／暗月は1共通Beam adapterでnative分岐→暗月加算→native成功→白夜shield→暗月cadenceを処理し、部位数・予算・対象枠・解除は独立する。
- 段階B-2のnative報酬は`BossHerWorld.cs`／`BossLastStarlight.cs`／`BossSoulPrison.cs`／`BossBurrow.cs`。HerWorldは本来の吸引軌道を保持した差分移動と、StopTimer直後の自然終了から実生成されたExplosionだけへ連携。LastStarlightはnative iteratorを保持して実waitの期限を現行段階へ追従し、親の再bind前に本人gem／skillを捕捉する。SoulPrisonは同じnative rescue healとoverflowを使い、本体消費を確認した追加shieldだけを残存時間まで保持する。Burrowは実Emergeのstun／daze引数とdamage packetだけを変更し、全対象で追加予算と本人CDを共有する。Primusには報酬profile／Linkを登録しない。
- 段階B-3のnative報酬は`BossWorldCracker.cs`／`BossBigChomp.cs`／`BossShoutOfOblivion.cs`。WorldCrackerの元tickで実際に使われた地形clip距離、BigChompの敵別weightと元Heal／GiveShield／firstTrigger CD処理、Shout自身のhunt増幅・backstep・hit-stun引数だけへ接続する。HP-only吸収は`Actor.DealDamage`の実HP減算箇所を同一packet scopeで捕捉し、shield／免疫／反射／generated packetを主撃回復へ混ぜない。`Shrine_CallOfTheRavenous`の報酬取得経路は変更しない。Polarisは報酬profile／Linkなし。
- M3の有限列は最大32pulseで1token、同owner/setの全予約合計4まで。Nyx／Erebosではmarker／seed／遅延列／channelを合計2・最古置換とし、置換したtokenの弾も解除する。同じE2は2点配置→部位→6点場の順で、新しい6点場を優先する。M2は波内の同敵hit数を制限し、従来の終点弾は飛行damage／壁爆発なし。Polaris6の槍だけは直撃を保ち、実際の最初の敵／壁／射程終端で1回の中立爆発を予約する。Seekerの追尾orbは8m／2秒以内の着弾・終端で1回だけ爆発、Primusの光弾は寿命1.5秒を延長せず異なる最大3敵へ連鎖する。M1の線／柱とM5は地形を越えず、短stun／slowは正の最終generated damageが成立した通常敵だけ。最大HP盾は吸収後の実残量から線形減衰し、補充しない。白夜の同target shieldは最高量だけを使い、古いshieldを復活させない。
- 表示は`DreamforgeBossEffectsMsg`、`HostAuthority.BossVisuals.cs`、`ClientSession.BossVisuals.cs`。1秒の生存snapshot、終了通知とepoch/revisionで再接続・順序・失効を処理する。native game時刻とMirror同期時刻の送信対から残り時間を算出し、pause／slow motionは本体のtimescaleに従う。属性色・扇／線／放射・固定幻影・対象ID・残数／予算・縮小域をMOD幾何描画で表示し、ボスモデル／network prefab／新規Summonは要求しない。
- live装備変更ではnative親寿命付きの予約・印・shield・予算・CDを保持し、変更したprofileだけを再判定して解除する。通常入力由来の予約／印は装備epochで破棄する。actorはcreation時刻だけでなくpool世代を照合し、死亡・遷移開始・部屋移動・owner離脱で全破棄する。
- Protocolは段階B-1から**17のまま**、wire／codec schemaの追加なし。保存形式4を維持し、内容指紋へB-3のprofile・native contract・event orderを含める。専用取得登録は全14セット84部位・11種adapter。旧Skollの汎用Power／Guard連携・互換aliasは復活させない。Primus／Polarisには報酬連携を登録しない。
- B-3の指定Releaseビルド成功（警告5・エラー0）。本体資料側に`Mods`がなく自動配置条件が成立しないため、ビルド済みDLLと既存about／iconsを`/tmp/sod-deploy-i48`へ明示配置した。B-3部位の強化／覚醒は§2.1どおり元式内cap適用後に最大3倍、通常／連携段階には掛けない。WikiGenを`/tmp/sod-wiki-i48-b3`へ実行して62セット・1418固有品、全14ボスセット84部位・11種adapterと新4セットの日英出力、Polarisの連携欄なしを確認した。本体資料とDLLのILはリポジトリ外でのみ参照。Managed DLLのみのため実機戦闘・表示・協力通信・GC／frame時間計測・較正は未確認。tests/・test csprojを変更せず、テストの設計・計画・追加・実行は行っていない。

### 設計原則

- **ボスの技の翻案**：各部位とセット段階は、実際の技の動き・予兆・間合い・時間差をプレイヤーが使う形にする。汎用Powerをテーマ名で包み直すだけにはしない。
- **報酬固有要素への直接作用**：連携は対応記憶／エッセンスの固有機構に接続する。汎用の使用後攻撃力バフで代用しない。セット単独・報酬単独は成立し、併用で操作や攻撃の組立てが変わる。
- 本体のコード事実、未取得のアセット値、新しいMOD設計値を分ける。本体ソース・長い文言引用はリポジトリへ持ち込まない。

### 根拠と読み分け

- MOD調査：`~/dev/sod-prompts/i48-research.md`。本体15型・報酬・戦闘テーマ：`~/dev/sod-prompts/i48-bosses-src.md`（r1.4.0.13）。本体データは転記せず、以下は型名と短い要約のみ。
- 今回の森の悪魔／ヒステリー／接続APIの行根拠と調査要約：`~/dev/sod-prompts/i48-demon-facts.md`。元DLLからの必要型抽出もリポジトリ外。旧調査の「樹木召喚ミサイル」「瞬間移動」という読みは同factsで訂正する。
- 残り13ボス・報酬の行根拠は `~/dev/sod-prompts/i48-facts-<boss>.md`（`<boss>`は§3見出しの`boss_`を除いたslug）。Unityのserialized値・説明本文が未取得の箇所は推測せず、MOD設計値と区別する。
- 全体の集計・装備検証・Build伝送・移動／効果／召喚APIの行根拠：`~/dev/sod-prompts/i48-facts-common.md`。これらはコード上の接続可能性であり、新機構のゲーム内動作を観測したという意味ではない。
- 現行定義：[Content.cs](../../src/SodRpg.Core/Game/Content.cs)（`UniqueDef` / `SetDef` / `Sets` / 上限）、[Links.cs](../../src/SodRpg.Core/Game/Links.cs)、[Loot.cs](../../src/SodRpg.Core/Game/Loot.cs)、[KillClassificationLedger.cs](../../src/SodRpg.Core/Game/KillClassificationLedger.cs)。
- [付録A A6/A10](../appendix-a-content.md)：限定固有品・非必須性の方針を継承。ただし同付録の独自ボスを本体14型と同一視しない。[付録B](../appendix-b-numbers.md)は初期設計値であり、現行ドロップ率の根拠にしない。
- [既存6点バランス](v1.32-six-piece-balance.md)と [SetBalance.cs](../../tools/BalanceSim/SetBalance.cs) の上限正規化を数値予算の参考にする。代理値はDPS・実効耐久ではない。

## 2. 共通仕様

### 2.1 ID・定義・名称

| 項目 | 追加仕様 |
| --- | --- |
| ID | §3のslugに対しセット `set.<slug>`、部位 `set.<slug>.<slot小文字>`。例：`set.boss_demon.weapon`。14＋84 IDを追加し、既存IDを改名・再採番しない |
| スロット | 保存値 Weapon=0 / Armor=1 / Charm=2 / Head=3 / Hands=4 / Feet=5を維持。表示順は既存 `Content.SlotOrder` |
| 土台・レア度 | 既存600土台から対応枠・テーマに合う `BaseId` を選ぶ。新土台・レア度なし。Legendary、通常のimplicit・特性3本・アイテムレベル・強化・覚醒を継承 |
| 出所 | 確定：`SetDef` に任意の `BossTypeName` を追加。既存セットはnull。型名→セットと限定Unique ID集合を定義から導出し、手書きの別ID台帳を増やさない |
| 部位固有効果 | `UniqueDef.BossMove` に既知の部位profile IDを1件指定する。旧セットはnull。ボス別Power列挙を増やさず、6部位の技を共通実行機構で表す |
| 名称 | ボス名を出さないテーマ名（日／英、§3）を採用。公式ボス日本語名は未取得。型名由来の「スコル」等の仮名は採用しない |
| 表示 | 出所表示もテーマ名＋ボス型名の内部対応を使用。名前・効果・連携は `Txt` と既存説明機構。公式名取得後もIDは不変 |

ボス部位は `SetId` と `BossMove` を持ち、旧汎用Powerと二重発動させない。`Loot.RollUnique` は既存UniqueIdを保存し、`Build.Compute` が定義から部位profile／2・3・6段階を導出する。ホストは `HostGearValidation` の検証済み装備から同じprofileを再構築し、申告された命令列を信用しない。固有技も著作済み効果1枠として強化milestoneの判定・表示へ接続し、空Power配列を理由に追加ランダムPowerを付けない。既存の強化・覚醒倍率は部位のdamage／heal／shieldだけへ1回適用。Demonは各channel明記cap、§3.2以降は元式内capの計算後に倍率を最大3倍へ制限して適用する。回数・距離・CD・寿命・CC強度は固定、セット／連携段階に倍率なし。既存48セット・単品Linkは不変。

### 2.2 専用ドロップ

確定式：**p = min(20%, 10% +（悪夢なら5ポイント、通常は0）+ 1ポイント × clamp(撃破時深度, 0, 5))**。部位は均等・重複あり、専用pityなし。

| 撃破時深度 | 通常p | 通常の完成期待撃破数 | 悪夢p | 悪夢の完成期待撃破数 |
| ---: | ---: | ---: | ---: | ---: |
| 0（基礎） | 10% | 147 | 15% | 98 |
| 1 | 11% | 約133.64 | 16% | 約91.88 |
| 3 | 13% | 約113.08 | 18% | 約81.67 |
| 5以上 | 15% | 98 | 20%（上限） | 73.5 |

- 「悪夢」は本体の難易度NightmareとLimboを含む方針で確定。MODの敵 `NightmareAffix` とは別：通常の悪夢化抽選ではボスが対象外なので、それだけでは確率上昇を満たせない。本体 `GameManager` / `DewDifficultySettings` が難易度を保持し、`DewSave` もNightmareとLimboを同群として扱うことを参照した。
- 深度はホストが遠征前に共有選択した**夢の深さ `ClientSession.HostRun.DreamDepth`（0〜5）**を使用。個人の潜行Heat、本体Limbo深度、ゾーン番号、危険度とは別軸であり、重ね掛けしない。難易度／夢の深さは撃破時ホストがfactへ凍結し、保留報酬の解放時に取り直さない。
- ホストで `m is BossMonster`、本来のtier=Boss、許可14型との完全一致を満たす撃破だけ型名を付ける。`disableLoot`／無報酬ハンター等の既存除外を維持。幻影・召喚雑魚・Polarisの石・Crawlerは対象外。同じ許可型の変種も、そのボス自身の報酬として扱う。
- `Rules.OnKill` の `Waypoints.ApplyKill` 後、`Profile.StoreRng` 前に個人の既存RNGで専用抽選を1回行う。当選時は6部位を各1/6、重複ありで1点選び、`Loot.RollUnique` 生成品を `reward.Relics` 末尾へ加える。通常の図鑑・統計・通知・未確保鞄・満杯時処理を共用。対象外／型名欠落では追加乱数を消費しない。所持数・狙い系統・人数・幸運でpや部位比率を変えない。
- 既存のボス通常ドロップは率100%、2点目60%、floor=Uncommon。mainの#56追従後のエピックpityは`min(1, 0.025 + 0.00875k)`（113体目で確定）を維持する。専用当選はpityを進行／リセットせず、通常側の救済にも失敗回数にも数えない。専用pity・重複救済は追加しない。
- 既存 `SetPieceWeight=4` / `SetCompletionWeight=60` は汎用セット専用。新セットは対象外。専用品は道標変換の後置きなので、BossTribute等の格上げ、強制部位、個数制限、複製、SupplyLine等の分解、覚醒への変換に通さない（鞄満杯時の既存処理は残す）。本体の魂の祠（固有報酬10%／隠し100%）とも独立、祠の使用は不要。
- 均等重複ありで6種完成までの期待当選数は `6H6=14.7`。表は固定pで `14.7 / p` を算出した期待値であり、当選品を保持できる前提。完成回数の保証ではなく、出現機会が少ないボスでは収集が長期化する。率・補正・上限は§5の確定値を使用する。

**汎用プール除外**：`Loot.RollRelic` の伝説候補で、Uniqueの `SetId` から解決したセットに `BossTypeName` がある品を候補化前に除く（重み0で代用しない）。通常固有品・既存セットは候補のまま。通常ボスの既存2抽選、商人、箱、遺物交換、製作・合成、道標再抽選は同じ入口を通るため一括除外できる。通常の限定ID生成は専用ボス報酬入口のみ。`dreamforge_giveunique` は限定IDを拒否し、開発確認では以下の専用コマンドでのみ付与できる。図鑑登録・既存品の保管／強化は従来どおり。

- `dreamforge_givebossset <セットIDまたはボス型名>`：登録済み限定セットの6部位を各1点付与。`boss_demon`、`set.boss_demon`、`Mon_Forest_BossDemon` を受け付ける。付与先・ドロップ通知は既存giveuniqueと同じ（遠征中は未確保鞄、遠征外は保管庫）。
- `dreamforge_simbosskill <ボス型名>`：遠征中に専用抽選だけを1回実行し、当選／落選・抽選確率を通知。実際のNightmare／Limbo難易度と共有深度を使用し、通常報酬・撃破統計・pityは変更しない。
- いずれも `DevAllowed()` が必須（プロフィールディレクトリ `QuickSave/Mods/DreamforgeRPG` に `dev.flag` を置いてゲームを再起動）。生成品は `Relic.DeveloperGranted=true` として保存・複製時にも保持し、装備詳細と通知・ログに「開発付与」と表示する。通常取得の除外ルールは変更しない。

### 2.3 「一段強い」の数値基準

強化0・覚醒0・連携なしで比較する。2/3点は従来の対応セットの予算約1.4倍を参考とするが、新しい技をArmor等の各行1.4倍へ機械的に置換しない。2点で技の起点、3点で位置／周期／回収を変え、6点で完成循環を追加する。

- 6点追加の設計予算は**1.85**。§3.2以降は独立channel A=90/cap100、B=38/cap40に配分し、`90/100 + 38/40 = 1.85`。係数がdamage以外なら単位と効果全体への配分を各節に明記する。Demonは元の90/100＋38/40を維持。
- これは新channel capを置いた定義予算であり、既存48組中央値0.925の2倍という強さの目安。実効DPS・耐久の2倍、あるいは新機構が既存Powerと等価だという証明ではない。実装後も比較母集団は旧48組に固定し、ボスセットを混ぜて中央値を上げない。
- 単品は小さな技1つと内部CD、2/3点は新しい操作循環、6点は高い追加予算を与える。報酬連携をセット基礎強さへ算入しない。cap・同一対象のhit gate・配置数・有効時間・世代制限を係数の上限と独立に保つ。
- 既存AD/AP・軽減・属性反応・Linkの上限は緩めない。即死・HP割合攻撃・無敵延長・CD撤廃・生成攻撃の自己再発動を行わない。本体のHP閾値や数値を、未取得のアセットの代わりに捏造しない。

### 2.4 段階連携・共通戦闘機構

12セットの連携条件は、自分の対応報酬装着＋異なるセット部位数。0/1はなし、2/3・4/5・6は段階1/2/3、最高段階プロファイルだけを選ぶ（下位の動作を含む場合はそのプロファイルに記載）。所持・味方の装着・祠使用だけでは成立しない。Primus／Polarisは連携なし。白夜／暗月は同じ記憶でも各セットの部位数を独立に数える。

**共通定義**：`BossMoveProfile` は部位の技と2/3/6段階、`BossRewardProfile` は報酬固有の作用を記す既知IDのデータ。`UniqueDef.BossMove`／`SetDef.BossStages`（閾値2/3/6）／`SetDef.LinkStages`（2/4/6）へ接続。汎用プログラムや自由な再帰グラフは作らず、profileは起点・条件・有限payload・係数cap・CD・個数／寿命の固定レコード。旧Demonの10Power名は効果channel IDへ移管し、新Power／Stat enumは**0種**。新LinkKindは **`BossReward` 1種**（Value=段階番号1/2/3、cap3）、旧`HysteriaGrove`案は撤去し同じ動作をDemon profileに収める。

profile IDはセットから導出し、同Requiresの異なるボス連携を合算しない。Buildのboss系集約キーは `(SetId, ProfileId)`、各段階は固定値・最大段階選択・覚醒倍率なし。`BossReward`は通常Linksに合算せず、選択したSetDefからprofileを導出して`BossRewards`へ入れる。既存のLinkキーや最高余韻を流用して白夜／暗月を潰さない。`Build.BossMoves` は既知profile IDとchannel係数Milli、`BossRewards` はSetId／profile ID／段階番号を持ち、それぞれ新しい独立codec節に必ず含める。重複ID・未知ID・過剰件数（各64件まで）を拒否し、`HostBuildValidation.EffectsMatch` の比較とApplied summaryにも含める。ホストは既存装備入力のUniqueId／強化／覚醒から再導出し、申告係数を使わない。永続保存は既存UniqueIdが正、予約・印・modeは保存しない。

| 共通実行機構 | 範囲・既存接続点とホスト／参加者の整合 |
| --- | --- |
| M1 形状攻撃・有限多段 | 円／扇／線／列を同じ範囲検索と `DamageAround` のdamage dispatch方式へ接続。1pulse1対象1hit、段数・対象上限はprofile固定、ホストのみ命中裁定 |
| M2 有界弾道 | 直線／放射／追尾の弾状態を `Tick` と `DewPhysics.SphereCastAllEntities`／本体Projectileの壁判定で処理。原則速度12m/秒・射程8m・幅0.4m、異なる値は§3へ明記。波内hit集合を持ち、参加者は発射／消滅通知を描画 |
| M3 予告・遅延・持続場 | `PendingGimmick` のCenter／Dueと同じ固定予約方式。円／線・噴出・安全域・回収は有限状態。独立寿命tokenごと1場、固定地点列は1予約に束ねる。本人・セットごと同時4場まで、満杯なら新規生成を見送り（§3でより小さい上限・最古置換を明記したprofileはその固定規則）。ホストの時刻で1回消費 |
| M4 本人の限定移動 | native移動完了の捕捉、追加dashは `GetValidAgentDestination_LinearSweep`／`StartDisplacement` の方式。地形越え・無敵を追加しない。自動爪dash／敵押出しを起点から除外し、ホストの到達地点を配信 |
| M5 敵の位置操作 | 本体Knockback／Controlの方式で吸引・押出し。新しい強制移動はボス／CC免疫／無効対象へ適用せず、1要求最大2m・同対象1秒gate。damageと移動の成否を分離し、サーバー状態同期を利用 |
| M6 短命の分身・設置射手 | 最大2体／本人・セット、寿命はprofile固定。親owner・装備epochを追跡し、有限攻撃だけ実行。Native Summonを使う型は `SpawnSummon` のCastInfo／owner検証と既存Spawn経路、非Entityの設置物はホストM1/M2＋表示通知で実装。どちらかを各節で確定し、偽のSummon proc／通常lootを付けない |
| M7 独立状態・防護 | `CreateBasicEffect`／既存shield pool／final stat processorを使い、発生源別IDとタイマーを保持。減装／解除では自身のcontainer／modifierだけ除去し、本体のCC耐性や状態を壊さない |
| M8 小さな印・段階ledger | 主撃／左右組／3形態／最大3段の循環。印の対象数は最大8、主撃カウンタは最大6。期限は明記した秒数を最後の有効native進行から数え、成功加算で更新する（個別に固定寿命を記す場は別）。activationを重複計上せず、生成世代から進行させない |

**共通イベント語彙6種**：E1本人のnative通常主撃成功、E2本人記憶のConfirmedUse、E3本人移動完了、E4本人native与／被damage、E5報酬型限定のinstance／状態event、E6ホスト時刻／mode遷移。E1/E4は帰属packetを確認し1activation1通知、E2は既存`NativeMemoryCasts`、E3はcastとdisplacement寿命を対応付ける追加adapter、E5は本体FromSkill生成event／限定Harmony／processor、E6は`HostAuthority.Tick`。別セットの生成物を本人nativeとして再入力しない。

**報酬アダプタ11種**：Hysteria、GlacialCore、EternalFlame、BeamOfBalance、HerWorld、LastStarlight、SoulPrison、Burrow、WorldCracker、BigChomp、ShoutOfOblivion。汎用の使用後AD/APバフで置換せず、型・owner・parent・instance寿命・native成功eventを確認する。Beamだけは1adapterへ白夜／暗月の別profileを渡す。対象数が増えてもprofileの発生数／回復量／配置上限を増殖させない。

**総数の数え方**：共通実行8種＋固有報酬adapter11種＋共通統合3経路（定義／集計／検証／表示、段階連携dispatch、視覚通知／snapshot）＝**22実装単位**。6イベント種・新LinkKind1種はこれらのschemaで重複加算しない。14セット・84部位・42通常段階・12連携profile／36連携段階はデータ件数で、別runtimeを162個作るという意味ではない。既存の専用取得経路は追加機構数へ再計上しない。

- `H=max(AD,AP)`、追加攻撃は優位側の物理／魔法（同値物理）。属性は§3の指定、指定なしは無属性。`25`/`25%H`は`0.25H`、heal／shieldもH表記に従い、%最大HPは明記した値だけ。距離はm、時刻は秒。設計値は発生時に凍結、通常防御計算を1回だけ適用、追加会心・native通常攻撃procなし。M2/M3の生成damageにnative回復や報酬procをコピーしない。native状態延長も本体のboss／CC免疫判定を維持する。
- 部位の主撃／記憶／移動効果は、段階効果に必要な特定部位を要求しない。2/3/6は累積、4/5の通常段階なし。channel A/Bの係数cap以外に、1敵1発動の合計上限・CD・同時数も固定。各profileの数値が本体の元スキルを書き換える場合だけ報酬adapterに隔離する。
- ホストが装備からprofileを再構築して効果を裁定し、参加者の地点・対象・stage申告は信用しない。表示・予告通知はownerNetId／run・room・equipment epoch／effect ID／時刻／地点列を持ち、同じIDの再送は表示更新のみ。Clock offsetは本体同期時刻に合わせ、遅延受信は残り時間だけ描画する。
- `DewEffect.PlayNewNetworked` のasset pathは親identity内に必要。任意ボスFXをHero親で送る設計にせず、native資産のローカル再生または同じ形状を表すMOD描画を専用通知で行う。新しいMirror prefab型を必須にしない。再接続はホストの生存効果snapshot、終了一覧／epochで古い予告を除く。新wireにはProtocol更新が必要。
- 減装・記憶交換でepochを進め未消費予約／印／左右組を破棄、native寿命／pool再利用を越えて参照を使わない。死亡・部屋遷移・owner離脱でも破棄。消費型報酬の成功callbackで確定した有限shieldだけは、native消費で撤回せず期限まで保持（任意の減装・死亡・部屋移動では除去）。変更中のnative状態は固有modifierだけ現行段階へ再適用し、元値を復元する。段階上昇だけで攻撃や召喚を発動しない。

### 2.5 KillSync・Protocol・保存

| 経路 | 設計上の変更／不変条件 |
| --- | --- |
| ホスト捕捉 | `CaptureAuthoritativeRunKill` で許可ボス型名 `BossTypeName`、撃破時 `BossDropNightmare`（bool）、`BossDropDepth`（0〜5）をfactへ追加。他の撃破はnull/false/0 |
| 配信 | `AuthoritativeRunKill` → `DreamforgeMonsterKillMsg` → ホスト自身の `PublishHostKillFact` と全参加者。再送も同じ3フィールドを保持。本体側の新ネットワーク型は不要 |
| 結合・保留 | `KillClassificationLedger.TryResolve` → `PendingRunKill` → `PendingRunRewards` → `GrantPendingKill` → `Rules.OnKill`。型名・pの条件はfactからのみ採用し、local死亡側の推定で上書きしない |
| 権威と参加 | ローカル死亡は既存の敵対／報酬資格・HeroKey・level等の情報源。専用抽選資格はホストが許可型を付けたfact。とどめ／与ダメージの有無で人数分の抽選を減らさない。参加中の既存報酬資格を持つ各プレイヤーに同じpで1回ずつ |
| 重複・順序 | 既存 RunId / EventId / MonsterNetId の結合・解決済み記録を共用。通常＋専用を同じ撃破処理に含め、保留・再送・復元で専用だけ再抽選しない。抽選結果をホストと全員で一致させる必要はない |
| Protocol | 新Build profile節・固有報酬段階・視覚通知の実装時は、**その時点の `Protocol.Version` の次版**へ更新する（現コード15なら16、#47等が先に更新済みならさらに次）。異なるschemaで同じ版を共用しない。今回の設計書改訂ではコードの版は変更しない |
| 内容整合 | `ContentFingerprint` の対象に型名→セット対応・専用p・全profile／channelの係数cap・時間・個数／世代上限・LinkStages・native adapterの作用と順序を含める。Helloで異内容の適用を拒否し、Build／効果通知／専用factにも内容一致を要求する |
| 永続保存 | 装備は既存 BaseId / UniqueId で保存し、SetIdはUnique定義から参照。段階連携は定義・装備から再計算し、専用保存項目は追加しない。開発付与の出所だけは遺物の任意キー `developerGranted` に保存し、欠落時はfalse。`Profile.CurrentVersion=4`、`ResetBeforeVersion=3`、`LedgerState.CurrentSchemaVersion=1`を維持。プロフィールリセットなし |
| 撃破の保存 | `ProfileCodec.RunRecovery.cs` のpending killとkill分類fact両経路に任意キー `bossTypeName` / `bossDropNightmare` / `bossDropDepth` を追加。未結合 `KillClassificationCheckpoint.Facts`／`Deaths[].Kill`、結合済み未払い `RunRecoveryState.PendingKills` とclone・capture・restoreも保持。旧保存の欠落はnull/false/0＝通常報酬のみ継続、専用抽選なし。旧撃破を推定して遡及支給しない |
| 戻し運用 | 追加IDを知らない旧MODでは未知Uniqueの既存除外処理が働くため、新品を含む保存を旧版へ戻して使う互換は保証しない。旧版対応の別名／shimは設けない |

## 3. 14セット案

各部位と段階は§2.4の共通profileへ定義する。以下の値は本体データの転写でなく、新しいMODの設計値。「本体単独」は全対象共通で元の固有効果をそのまま使え、装備しないときの数値・挙動を変えない。専用ID／取得条件／強化・覚醒の基本規則は§2.1〜2.3を共用する。

### 3.1 `boss_demon`：荒ぶる樹界 / Rampaging Grove

**立ち止まって耐える装備ではなく、踏みつけで森を生やし、移動して次の噴出位置を作る装備。** `set.boss_demon` と6部位IDは不変。旧Retaliation／Bulwark／SecondWind／Vigor／Thorns／Whirlwind、旧2/3/6点、旧MemorySurge連携は全て置換し、別名や併存経路を残さない。

調査要約・ファイル:行の根拠は `~/dev/sod-prompts/i48-demon-facts.md` に置く。本案は踏みつけ／遅延樹木／放射弾／溜め攻撃／CCで止まらない高速移動を翻案し、樹木とミサイルを別の機構にする。ヒステリーには通常攻撃イベントでなく、自動爪・左右交互の周期への専用接続を設ける。本節の数値は**本体値ではなくMODの新設計値**。本体アセットにある秒数・係数・強化表・説明文は未取得で、そこを推測して設計の前提にしない。

#### 共通の攻撃単位・樹木

- `H = max(本人の現在AD, AP)`。追加攻撃は無属性、AD優位なら物理、AP優位なら魔法（同値は物理）。係数は発生予約時に凍結し、防御計算は通常どおり1回。放射弾以外は固定地点の範囲攻撃で、生存する敵だけに当てる。味方・自分へ当てず、追加会心・通常攻撃効果・本体爪の回復は付けない。
- 「主撃」＝本人の本体通常攻撃activationの最初の成功命中1件。多段・貫通・複数対象は1回として数え、空振りは数えない。召喚・反射・記憶・属性反応・MOD生成攻撃は除外する。ヒステリーだけは下記連携で別の資格を与える。
- 主撃カウンタは必要数まで蓄え、必要数と内部CDの両方を満たす主撃で1回発動して0へ戻す。CD中の超過主撃を貯めて後から連射しない。複数部位の発動は独立だが、派生攻撃からカウンタを進めない。
- 「樹芽」＝固定地点を予告する遅延攻撃。生成から**1.2秒後**に半径2.5mの樹木噴出、生成0.15秒後から回収可能。移動追尾・当たり判定壁・永続地形・Summonではない。本人ごと最大4個、満杯では新規生成を見送り、古い芽を勝手に消さない。回収と自動噴出は排他的に1回だけ。
- 通常の樹芽は回収時も自動時もその芽の係数で噴出する。同一主撃／移動activation由来の踏みつけから3点樹芽を作るのは最大1個。見た目は足元の環状衝撃、樹芽の予告円、時間差で突き出す木、森色の放射弾を区別する。

#### 6部位：単品でも技が使える

`Value / cap` は共通profileの効果channel定義値／合算上限（Power enumではない）。Armor以外のValueはHに対するダメージ%。部位の強化・覚醒は既存倍率でValueだけを伸ばしcapで止める。回数・半径・CD・寿命・樹芽数は増やさない。

| 部位 | 装備名（日 / 英） | 技の翻案・単品の動作 | channel：Value / cap |
| --- | --- | --- | --- |
| Weapon | 震根の槌 / Quakeroot Maul | 主撃3回ごと、命中地点に半径3mの踏みつけ衝撃波。内部CD2秒。密集へ通常攻撃を差し込む | `DemonImpact`：25 / 80 |
| Armor | 不退の樹皮 / Unyielding Bark | 自分の移動スキルによる移動完了後0.6秒Unstoppable、内部CD8秒。無敵・軽減ではなく、前進直後の攻撃をCCで止められにくくする | `DemonStride`：6 / 10（Value×0.1秒） |
| Charm | 萌発の核 / Sprouting Core | 本人の通常／奥義記憶の使用確定時、カーソル方向・本人から最大8mの有効地面に樹芽1個。内部CD4秒。記憶に遅れて木が噴く | `DemonSeed`：20 / 60 |
| Head | 放射の枝冠 / Radial Branch Crown | 主撃6回ごと、本人中心から60度間隔で6本の直線ミサイル、速度12m/秒・射程6m・幅0.4m・各弾は最初の敵または壁で消える。同じ敵への命中はこの波で1回まで、内部CD6秒。木は生成しない | `DemonVolley`：12 / 40（1命中） |
| Hands | 溜め裂きの手甲 / Delayed Rending Grips | 主撃4回ごと、命中地点を予告し0.65秒後に半径2mの遅延打撃。内部CD3秒。対象死亡後も地点に残るが追尾しない。溜めてから届く一撃の翻案 | `DemonDelay`：25 / 80 |
| Feet | 瞬駆の根履 / Blinkstride Treads | 本人の移動スキルによるdisplacement完了／Teleport後、3秒以内の次の主撃に半径3mの踏みつけ。内部CD4秒。移動自体は追加しない | `DemonArrival`：20 / 60 |

Armor／Feetは敵の押し出しとヒステリー爪自身のdashでは発動しない。Teleportはホストの本人移動として確認できるもののみ。移動を伴わないMovementスロット使用は着地と数えない。Headの弾はボスの放射波の翻案であって、未確認の本体ミサイル外見を流用する前提ではない。

#### 2/3/6部位：任意の組合せで累積

| 部位数 | 追加する機構・設計値 | プレイの変化 |
| --- | --- | --- |
| 2 | `DemonStomp` 50 / cap100：自分の移動スキルによる移動完了／確認済みTeleport地点に半径3m、50%Hの踏みつけ。内部CD3秒 | 回避が逃げるだけでなく、次の攻撃位置を選ぶ踏み込みになる |
| 3 | `DemonGrove` 45 / cap90：本人の部位／2点の踏みつけ地点に45%Hの樹芽1個を残す（生成CD1秒）。次の本体通常攻撃の主撃で命中地点から4m以内の回収可能な本人の芽を最も近い順で1個回収・噴出。または次の本人移動着地から8m以内をカーソルに最も近い順で回収（同距離は生成順、回収CD1秒共有） | その場の時間差攻撃か、次の攻撃／移動で早めに噴かせるかを選ぶ。回避のCDが長くても通常攻撃で森を回せる |
| 6 | `DemonMarch` 90 / cap100：芽の回収時、本人地点から回収芽へ最大8mの線を固定し、1/3・2/3・終点に0／0.25／0.5秒で半径2.5mの踏みつけ各30%H（Valueを3分割）。1敵は列全体で最大3命中、内部CD6秒。`DemonReplant` 38 / cap40：同じ列の開始点と終点に38%Hの樹芽を各1個残す | 一本の木を起点に、前方を時間差で踏み荒らす。本人の足元と前方の敵へ、次の木の噴出を予約する |

6点の列はその回収した芽の通常噴出に追加する。列の踏みつけは3点の芽生成・部位効果を再発動しない。6点の再植樹芽は**自動噴出専用で回収不可**（連携も不可）、発動列ごと最大2個・共通4芽上限に従う。列の多段も通常攻撃扱いにしない。線を地形越しに通さず、各地点は有効地面・射線内に丸め、無効ならその地点の攻撃／芽を省く。
通常主撃／移動完了の処理順は、開始時に存在した芽の回収→6点列／再植樹→当該イベントの部位／2点踏みつけ→3点の植樹。通常主撃による回収は本体通常攻撃だけで、ヒステリーの「森の主撃」は代用しない。本人と芽が同地点ならカーソル方向へ6mの列にし、有効な方向を取れなければ列を省く。再植樹の容量枠は開始点→終点の順で予約し、列発動時から1.2秒後に自動噴出する。

**強さの狙い**：連携抜きで、既存6部位セットより一段強い位置取り型。6点追加の定義予算は `90/100 + 38/40 = 1.85`（既存48組中央値0.925の2倍）。全命中・再植樹2個成立時は、6秒に追加90＋76＝166%Hの攻撃を与える。ただし新cap自体も設計値で、既存Powerとの実効等価性・DPS優位を証明した数値ではない。移動・回収窓・地形・4芽上限・敵の離脱で出力を失う代わりに高い追加予算を与え、連携・常時CC無効・無敵・HP割合攻撃で強さを水増ししない。

#### ヒステリー連携：爪で森を駆動する

共通 `LinkKind.BossReward` のDemon profile。Requiresは自分の `St_U_Hysteria`、2/4/6部位のValueは**段階番号1/2/3**（%ではない、cap3）。最高段階だけ採用し、下位動作を含むプロファイルを選ぶ。装着だけで爪を生成せず、本人の本体 `Se_U_Hysteria` 生存中・そこから生成された `Ai_U_Hysteria_Claw` だけに作用する。

| 部位数 | 固有要素への直接作用 | 使い方 |
| --- | --- | --- |
| 2/3 | 同じ本体状態の連続する左右2爪を1組として扱う（初回が右でもよい）。少なくとも片方が敵へ成功命中した組の完了で「森の主撃」を1回発行し、装備中のWeapon／Head／Hands／Feetの専用カウンタだけを進める。さらに本人地点で2点踏みつけを試みる（移動時とCD3秒共有） | 通常攻撃が封じられても、攻撃速度で速まる自動爪からセットが動く。部位の通常攻撃用効果全般へ爪を偽装しない |
| 4/5 | 左爪の最初の成功命中地点に45%Hの樹芽1個（CD1秒、共通4芽上限）。次の右爪の最初の成功命中時、その命中地点から4m以内の回収可能な本人の芽を最も近い順で1個回収（同距離は生成順、通常回収とCD1秒共有） | 左で木を仕込み、右で噴出させる。爪の左右交互という周期が「植える／刈る」になる。空振りでは植樹・回収しない |
| 6 | 右爪による回収でも6点の列と再植樹を発動できる（通常攻撃／移動回収とCD6秒共有、起点は本人地点）。本体ヒステリー自身の移動速度補正だけを-50から**-25**へ置換 | displacement中は爪が止まる本体の制約を残しつつ、移動スキルを使わない自動前進・爪連打からも森を踏み荒らせる。記憶固有の鈍足補正を半減して追い続ける |

爪の向き・dash距離・爪基礎間隔／攻撃速度式・本体持続・攻撃ロック・Unstoppable・命中ごとの回復／低HP増幅は変えない。初回右爪の植樹なし、樹芽未成熟・容量不足・右爪の空振りでは回収なし。爪1instanceの複数対象命中で植樹やカウンタを増殖させず、同じ本体状態から生成済みの左右組だけを結合する。生成攻撃へ本体爪の回復や`AttackEffectType.Others`を移植しない。

**単独／併用**：セット単独は通常攻撃と移動で森を作り、好きな記憶で成立。ヒステリー単独は本体の自動爪・自己回復・制約のまま。併用は、能力ロック中も爪が植樹と回収を担い、左右の手拍子に沿って森の列が伸びる。汎用の「記憶使用後AD/AP増加」は一切付けない。

#### 共通機構への接続（挙動・数値は993ee1dを維持）

M1/M2/M3/M7/M8、E1/E2/E3/E5/E6。主通常packetの帰属・移動完了・FromSkillのSe／Claw生成と成功damageを結合する。Hysteria adapterは左右組と最初の成功命中を確定し、`Se_U_Hysteria.OnCreate`の`DoSpeed(-50)`が返すSpeedEffectだけを追跡。段階3なら-25、解除なら `StopBasicEffect`／`DoSpeed` で-50へ戻し、本体ロック・回復・Unstoppableは変更しない。樹芽・列・弾・表示通知・減装失効は§2.4を共用する。

ホストが現行装備からDemon profileを裁定し、参加者は生存効果通知を表示する。減装時の未完了左右組・予約・芽の破棄と、同じ本体状態だけを扱う寿命規則は維持する。ゲーム実行による較正・表示検証は本設計作業では行っていない。

### 3.2 `boss_skoll`：氷刃の王装 / Iceblade Regalia

`Mon_SnowMountain_BossSkoll` / `Gem_U_GlacialCore`：予告斬撃で地点を確保し、氷矢の残留場を氷雨で囲む。Coldで発動する本体回復→冷気弾の循環を直接支援。
以下は全て設計値。H=max(AD,AP)、追加damageは優位type（同値物理）/Cold、追加会心・attackeffect・procなし、セット生成effectはE1〜E5 native判定から除外；CDは本人/部位別、1activation1count。

| 部位 | 装備名（日 / 英） | 実技翻案・単品挙動 |
| --- | --- | --- |
| Weapon | 氷雨の剣 / Icerain Sword | Atkの予告斬撃：E1で敵位置を固定（本人から最大8m）、0.25秒後長4m×幅1mの刃1本、0.28H、CD5秒。 |
| Armor | 氷刃の帷子 / Iceblade Mail | AuraBlade遅延切返し：E4被弾で被弾時本人地点を予告、0.40秒後半径2mへ0.25H、CD7秒。 |
| Charm | 雪嶺の核 / Snowcrest Core | SummonArrow：E2でカーソル合法地点最大8mへ氷矢、0.45秒後半径1.8mに0.18H＋0.06Hを0.5秒間隔3tick、寿命1.5秒、最大1場、CD8秒。 |
| Head | 氷輪の冠 / Icering Crown | AuraBladeRain：E1を3回/6秒で敵位置中心の横列3点（間隔1.5m）へ予告、0.50/0.75/1.00秒後に半径0.9m各0.12H、最大1列、CD8秒。 |
| Hands | 霜裂きの手甲 / Frostrend Grips | AuraSlice：E1の4回目/6秒で前方長4m・60°を0.25秒予告して0.30H＋1m押出し、boss/CC immunity移動除外、CD6秒。 |
| Feet | 氷渦の鉄靴 / Icewhorl Sabatons | Whirlwind：E3 native移動完了で本人追従半径2m旋風、0.25秒間隔4tick各0.07H、寿命1秒、最大1、CD5秒；自動追跡/操作blockなし。 |

**2点**：E1/E2で氷印1（最大3、寿命6秒）。3印の次E1/E2で全消費し、その敵/カーソル合法地点最大8mを固定、0.4秒後長5m×幅1mのオーラ裂き0.35H、CD4秒；任意2部位で成立。

**3点**：2点の固定地点に氷矢を追加、0.65秒予告後半径2mで0.15H、さらに0.5秒間隔3tick各0.05H、寿命1.5秒、同時1場；任意3部位で同じ入力から成立。

**6点**：2点発動時、固定地点を囲む半径2mの4点へ氷雨（各半径1m、予告0.6/0.8/1.0/1.2秒、各0.225H）→終段で本人shield0.38H/2秒非加算、CD10秒、同時1雨。追加A=90/cap100（雨計0.90H/cap1H）、B=38/cap40（shield0.38H/cap0.40H）、予算1.85未実測。

**報酬連携2**：装着Coreのnative Cold-trigger healだけ+20%（追加最大0.12H、CD2秒）。元のheal/crit/chainを保持し、その回復が本体bankへ自然に入る；セット生成Coldは本体healを再発動しない。

**報酬連携4**：上記native Cold healの原因敵を2秒記録、Coreが次に作るnative projectileの標的を、元shootRadius内/生存/敵対ならその敵へ優先；native bank・damage・proc・射数は増やさない。

**報酬連携6**：native Cold heal後1秒間、既存bankの実射のみ最大3発を最短0.10秒間隔へ加速（元の方が速ければ維持、CD8秒、1tick1射）；bank/合法敵なしでは加速せず弾・bank複製なし。
M1/3/5/7/8＋E1/2/3/4/5/6；API：Gem_U_GlacialCore.OnDealDamage内RoutineのMoveNext cold-heal IL入口、InitProjectile beforePrepare postfix(info)、ActiveLogicUpdate射間隔入口、Actor.GiveShield/AbilityInstance.info。host裁定、参加者へ刃/場/氷印/加速残射を表示、死亡/KO/部屋/装備epoch/報酬解除で独自状態破棄（本体bankは本体管理）。
調査根拠・入口詳細：`/home/wang/dev/sod-prompts/i48-facts-skoll.md`（serialized実値未取得、静的調査のみ）。

### 3.3 `boss_infernus`：噴火炉の軍装 / Eruptionforge Warplate

`Mon_LavaLand_BossInfernus` / `Gem_U_EternalFlame`：外へ走る噴火列→炎柱で占領→放射咆哮で押し広げる。報酬の呪い/火stack蓄積/5stack強制会心へ直接連携。
以下は全て設計値。H=max(AD,AP)、追加damageは優位type（同値物理）/Fire、追加会心・attackeffect・procなし、セット生成effectはnativeイベント/curse判定から除外；CDは本人/部位別、1activation1count。

| 部位 | 装備名（日 / 英） | 実技翻案・単品挙動 |
| --- | --- | --- |
| Weapon | 噴火の大剣 / Eruption Greatsword | Stomp：E1で攻撃方向1.5/3/4.5m地点へ噴火各半径0.8m、0.30/0.45/0.60秒予告後各0.10H、地形段差>2mで以遠中止、CD5秒。 |
| Armor | 炉壁の鎧 / Furnacewall Plate | Jump_Land：E4被弾時の本人位置に0.35秒予告→半径2m着地衝撃0.24H、移動可能な通常敵のみ外へ0.8m、硬CCなし、CD7秒。 |
| Charm | 不滅炉の火種 / Undying Furnace Spark | PowerBomb柱：E2でカーソル合法地点最大8mへ半径1.5m炎柱、0.50秒後0.16H＋0.06Hを0.5秒間隔3tick、寿命1.5秒、最大1柱、CD8秒。 |
| Head | 熔冠 / Molten Crown | BreathFire：E2で発動時方向の長5m×幅1m火炎3列（0/0.2/0.4秒、各0.12H、同対象各回1hit）、寿命0.6秒、最大1列、CD7秒；追跡回転/操作blockなし。 |
| Hands | 赤熱の拳 / Redheat Fists | Roar：E1の3回目/6秒で0.35秒予告→4方向の直線弾（速度10m/秒、射程4m、幅0.5m、寿命0.4秒、各0.05H）、同対象総0.20Hまで、最大4弾、CD6秒。 |
| Feet | 火柱越えの靴 / Flamepillar Treads | Dash初段：E3 native移動完了で前方長3m×幅1.5mへ0.18Hと1.2m押出し、壁で停止、boss/CC immunity移動除外、硬CC/壁追加damageなし、CD5秒。 |

**2点**：E1/E2で熱印1（最大3、寿命6秒）。3印の次E1/E2で全消費、前方2/4/6mに半径1m噴火各0.12H（予告0.35/0.55/0.75秒、段差>2mで以遠中止）、CD4秒；任意2部位で成立。

**3点**：2点列の最後の合法地点に0.8秒予告の炎柱（半径2m、0.5秒間隔4tick各0.08H、寿命2秒、同時1柱）；任意3部位で成立、敵数による増柱なし。

**6点**：2点発動から1秒後に本人から4方向咆哮弾（速度12m/秒、射程6m、幅0.7m、寿命0.5秒、各0.225H、同対象合計cap0.90H）、本人shield0.38H/2秒非加算、CD10秒、同時1burst。追加A=90/cap100（放射計0.90H/cap1H）、B=38/cap40（shield0.38H/cap0.40H）、予算1.85未実測。

**報酬連携2**：装着EternalFlame由来native curseのduration/remainingを本体取得値+1秒に限定延長（native refreshも同上限、他curseは不変）；報酬解除時は保存したnative期間へ差分復元。

**報酬連携4**：自分のcurseがnative本人damageで本体proc判定に成功してFire stackを追加した時だけ、さらに1Fire（同対象CD2秒、本人CD1秒、対象台帳最大3）；本体proc不成立/生成effect/他者damageでは追加なし。

**報酬連携6**：自分のcurseが付いたfireStack3〜4の敵1体について、装着SkillTriggerのnative main activation最初のdamageだけ本体Processorの強制会心門を5→3に下げる（CD4秒、1activation1回）；増幅計算は実fireStack、既存>=5門は維持。
M1/2/3/5/7/8＋E1/2/3/4/5/6；API：Se_U_EternalFlame_Curse.OnCreate/EntityEventOnTakeDamage、Gem_U_EternalFlame.Processor crit門IL入口、StatusEffect.SetTimer/ResetTimer、Actor.ApplyElemental/GiveShield。host裁定、参加者へ噴火方向/柱予告/熱印/門readiness表示、死亡/KO/部屋/装備epoch/報酬解除で独自状態破棄（本体curseは削除しない）。
調査根拠・入口詳細：`/home/wang/dev/sod-prompts/i48-facts-infernus.md`（serialized実値未取得、静的調査のみ）。

### 3.4 `boss_white_night`：白蓮の守装 / White Lotus Vestments

**白蓮域を張り、圏内から旋回波と三拍の掌を組む。** `Mon_Ink_BossWhiteNight`／任意報酬 `St_U_BeamOfBalance`。以下は新設計値、H/主撃/生成originは共通契約、追加攻撃はLight・優位物理/魔法。

| 部位 | 装備名（日 / 英） | 技の翻案・単品の動作 |
| --- | --- | --- |
| Weapon | 白蓮の錫杖 / White Lotus Staff | 予測掌：E1主撃3回ごと命中点を0.65秒予告し半径2.5mへ25%Hの掌、CD3秒・予約1個 |
| Armor | 安息域の法衣 / Haven Robe | 安全域：E4 native被弾後本人位置へ半径2m/1.5秒の域、域内本人へ15%H shieldを1回/1.5秒、CD8秒・1域 |
| Charm | 光秤の印 / Lightscale Seal | 縮む安全円：E2記憶confirmed useで本人位置に半径3→2mを1.2秒で縮める域、満了時圏内本人＋最寄り味方hero1人まで各12%H shield/2秒、CD6秒・1域 |
| Head | 白暁の冠 / White Dawn Crown | 旋回破滅波：E1主撃5回で射程5m/60°のLight扇を-30/0/+30°へ0/0.2/0.4秒に各10%H、CD5秒・列1組 |
| Hands | 蓮掌の手巻き / Lotus Palm Wraps | 寸勁：E1主撃4回で命中点半径2mの非boss・CC非免疫敵を最大1m吸引し0.35秒後20%H掌、CD4秒・予約1個、地形越え/本人移動なし |
| Feet | 光界の歩み / Lightward Steps | 転位後の波：E3本人移動完了後2秒以内の次主撃で到達点から命中方向へ長さ6m/幅1m/20%Hの線、CD4秒 |

| 通常段階 | 追加機構・設計値 | プレイ変化 |
| --- | --- | --- |
| 2 | E1主撃3回で本人位置へ半径3m白蓮域（CD4秒、1域）、0.75秒後40%H掌→域2秒、域内本人＋最寄り味方hero1人まで15%H shield各1回/2秒 | 予告を置いて圏内へ残る。任意2部位で成立 |
| 3 | 開始時に掌済み本人域の中にいる主撃で命中方向へ6m/90°/30%Hの波、CD2秒 | 域に立って扇を向ける。新規域・予告中の域は使えない |
| 6 | 同じ掌済み域内の主撃3回で域を消費、半径5m境界波90%H（cap100）＋同2heroまで各38%H shield/2秒（cap40）、CD6秒 | 退避円を三拍の攻勢へ変換。波CD中もnative主撃は数え、域失効で印消失 |

6点予算90/100+38/40=1.85（実効未較正）。域消費→6点→当該主撃の部位/2点新規域の順、生成は再発動しない。全白夜shieldは同target最高量へ置換・合計cap38%H、独立containerを使う。

**報酬2部位**：本人native Beamの非敵heal実overhealの25%を2秒shieldへ変換、先着native heal対象hero最大1人・target cap0.12H・親Se全体0.24H予算。native heal原量は維持。

**報酬4部位**：overheal25%、対象hero最大2人・target cap0.20H・親Se全体0.40H予算・2秒へ置換。対象枠を固定し、shield更新も予算消費。

**報酬6部位**：overheal50%・最大2hero・target cap0.30H・親Se全体0.60H予算・2秒へ置換。Hはbeam生成時凍結。他人/生成healから反応せずnative healを消費しない。

**白夜/暗月併用**：部位数/profileは独立。同一Beamでnative分岐→適格なら暗月固定量加算→native成功→白夜overheal shield→暗月cadence。汎用倍率なし、片profile解除は他方を残し、native原量/steering/range/interval/endTimeは維持。

**M/E・入口**：M1/3/5/7/8、E1–E6。共通Beam adapterはSeの子Ai捕捉＋ActorEvent_OnDoHeal/GiveShield、暗月だけAi.dealtDamageProcessor。ホストが装備/親寿命/epoch/生成originを照合して裁定、参加者は域・波・shield/残予算を表示。終了/死亡/部屋移動/離脱/装備・記憶変更で本人profileの予約・印・shield・購読だけ解除。
根拠・値未取得範囲・各技とAPIのfile:line：`~/dev/sod-prompts/i48-facts-white_night.md`（暗月も同じBeam単一抽出を参照）。

### 3.5 `boss_dark_moon`：黒月の刃装 / Black Moon Armament

**刃→槍→槌の形を回し、運ぶ槍と一斉幻影斬を組む。** `Mon_Ink_BossDarkMoon`／任意報酬 `St_U_BeamOfBalance`。以下は新設計値、H/主撃/生成originは共通契約、追加攻撃はDark・優位物理/魔法。

| 部位 | 装備名（日 / 英） | 技の翻案・単品の動作 |
| --- | --- | --- |
| Weapon | 黒月の三叉刃 / Black Moon Trident | 刃：E1主撃3回で本人前方4m/90°/25%Hの扇、CD3秒 |
| Armor | 怒影の鎧 / Wrathshadow Mail | 落下槌：E4 native被弾後、攻撃者の8m以内の地点を0.8秒予告し半径2m/20%H槌、CD8秒・予約1個、無敵なし |
| Charm | 月欠けの環 / Waning Moon Ring | 投槍：E2記憶confirmed useで照準方向へ直線弾1本、12m/s・射程8m・幅0.5m・20%H、最初の敵/壁で消滅、CD5秒・1弾 |
| Head | 自我の面 / Mask of the Self | 自我剣：E1主撃5回で本人から命中点（最大8m）へ12m/sの剣弾1本、終点2m/25%H、壁で消滅、CD5秒。非Summon |
| Hands | 切替の握り / Shifting Grips | 運ぶ槍：E1主撃4回で長さ5m/幅0.8m/20%H線、非boss・CC非免疫敵だけ前方最大1m押出し、CD4秒・地形越えなし |
| Feet | 跳影の靴 / Shadowleap Boots | 転位斬：E3本人移動完了後2秒内の次主撃で到達点へ幻影予告、0.4秒後命中方向へ長さ5m/幅1m/25%H線、CD4秒・非Entity固定表示 |

| 通常段階 | 追加機構・設計値 | プレイ変化 |
| --- | --- | --- |
| 2 | E1で刃4m/90°25%H→槍7m/幅0.8m25%H→槌0.6秒予告/半径2.5m40%Hを順送り、CD1秒、初期刃・無操作6秒でreset | 任意2部位で攻撃形が切り替わる。CD中の主撃は進行/超過蓄積なし |
| 3 | 2点形を予約した主撃のnative対象1体へ月印（最大3stack/6秒）、3stackで0.3秒後30%H斬・CD3秒、対象変更で旧印消去。E3で次形を1段送る/CD2秒 | 移動で必要な形を選び、同敵に三拍を重ねる |
| 6 | 6秒以内の刃→槍→槌完成で槌地点左右2mに固定幻影、0.35秒後各45%H/長さ6m/幅0.8m斬線（A90/cap100）＋0.8秒後同地点半径3m38%H槌（B38/cap40）、CD6秒・予約1組 | 3形完遂を一斉幻影の挟撃へ。E3で順を飛ばすとchain resetし次の刃から再開 |

6点予算90/100+38/40=1.85（実効未較正）。phantomはM1/M3の固定攻撃表示でEntity/Summonを作らずnative召喚procなし。2点形→3点印→6点完成→部位の順、生成は印/主撃を進めない。

**報酬2部位**：Beamの同敵native成功tick列1秒以後のnative敵packetへ固定0.10H加算、global CD1秒・親Se最大3回/計0.30H。tick間隔上限=本体hitInterval+checkInterval+0.1秒、ledger最大3敵、期限切れだけ入替。native原量/属性/procは維持。

**報酬4部位**：閾値0.75秒・固定0.15H×最大3回/計0.45Hへ置換。独立してnative敵tick成功からbeam位置→対象凍結点へ25%H投槍（12m/s・8m・幅0.5m・最初の敵/壁で消滅）、global CD1秒・親Se最大3本。

**報酬6部位**：閾値0.5秒・固定0.20H×最大3回/計0.60Hへ置換（加算CD1秒/Hはbeam生成時凍結）。native敵tick成功が2点形/3点印/6点完成へ照射主撃をglobal CD1秒・親Se最大6回供給（通常2点CD共有、部位counterには供給なし）。4投槍/6進行は加算閾値未達/加算枠なしでも各独立gateで成立。

**白夜/暗月併用**：1共通Beam adapterでnative分岐→暗月有界固定量加算→native成功→白夜overheal shield→暗月cadence。汎用倍率なし、native原量/照準/range/interval/持続/無敵は維持、生成再反応なし、部位数と解除はprofile独立。

**M/E・入口**：M1/2/3/5/8、E1–E6。Seの子Ai捕捉＋Ai.dealtDamageProcessor/DamageData.AddFlatAmount/ActorEvent_OnDealDamageで本人/親/寿命/native originを限定。ホストが形・月印・残加算/発数を裁定して通知、参加者は予告/固定幻影/照射進行を表示。終了/死亡/部屋移動/離脱/装備・記憶変更で当該profile予約/ledger/購読を解除し他profile/nativeを壊さない。
根拠・各技とAPIのfile:line：`~/dev/sod-prompts/i48-facts-dark_moon.md`、Beam実値未取得と共存契約は共有 `~/dev/sod-prompts/i48-facts-white_night.md`。

### 3.6 `boss_nyx`：星海の主衣 / Starsea Sovereign Raiment

`Mon_Sky_BossNyx`／報酬`St_U_HerWorld`。予告地点を攻撃で閉じ、移動で置き直し、3周目を有限の吸引→爆発へ変える（全数値は設計値、H=max(AD,AP)）。

| 部位 | 装備名（日 / 英） | 実技翻案・単品効果 |
| --- | --- | --- |
| Weapon | 星柱の杖 / Starpillar Staff | 星柱：E1被害地点≤8mに半径1.5m予告.35秒、初撃.12H＋.4秒後.06H Light、CD5秒 |
| Armor | 星海の外套 / Starsea Mantle | Blackholeの防護channel：E4被damage後、本人shield .16H/2秒＋足元半径2m予告.4秒→.08H Light爆発、CD8秒 |
| Charm | 夜空の種 / Nightseed | seed自壊：E2照準≤8mに非Entity種1個、.6秒後3方向弾（距離4m/速度8m/s/半径.25m/寿命.5秒、同敵1hit .10H Light）、CD6秒、本体召喚procなし |
| Head | 星軌の冠 / Starorbit Crown | 移動しながら地点レーザー：E3終点前方2mに半径1m予告.3秒→.14H Light爆発、CD5秒 |
| Hands | 星を結ぶ指 / Starbinding Fingers | Pull→AfterAtk：E1対象周辺半径2m、最大3敵を本人側へ≤.6m/.3秒吸引、.4秒後同円.12H Light、CD6秒（boss/immune移動不可） |
| Feet | 星渡りの靴 / Starcrossing Shoes | StellarDash：E2照準方向へ本人≤3m/.2秒有効地形dash、経路幅.6mで敵1回.10H Light、CD7秒、無敵なし・この自動移動はE3対象外 |

| 段階 | セット単独の操作循環（累積、どの部位組合せでも成立） |
| --- | --- |
| 2点 | E2照準≤8mに星点1個/6秒。次E1が星点へ半径2mの柱2hit各.10H Light（.35/.75秒）を起動し星点消費、CD6秒 |
| 3点 | 星点待機中E3で1回だけ終点前方2mへ置き直せる。起動柱に半径2m・≤.6m/.3秒の敵吸引を先行追加、星点消費で星印+1（最大3/12秒、boss/immune移動不可） |
| 6点 | 2点柱を3回起動して印3→次E2で全消費、照準≤8mに半径3mを予告.5秒→1秒吸引（最大3敵/2m/s/総移動≤2m）→.90H Light爆発＋本人shield .38H/3秒、CD10秒。印は各柱起動で得るためE3不要；A90/cap100+B38/cap40=1.85 |

| 報酬連携 | `St_U_HerWorld`装着＋対応部位数（累積；native報酬の実status/childのみ） |
| --- | --- |
| 2部位 | native `Se_U_HerWorld_Blackhole`のtick円内の敵へ追加吸引1m/s、1status・1敵追加移動≤2m（boss/immune除外）；native曲線/ability lock/death interruptは変更なし |
| 4部位 | 当該native statusのtickDamageRadiusを`r+min(1,max(0,8-r))`mへ；自分中心Light DoTの捕捉域を直接拡張し終了時復元 |
| 6部位 | native tick成功敵を最大3体記録→同status自然終了child `Ai_U_HerWorld_Explosion`実hit時、記録敵へ1回.45H Light追加。native stunは`d+min(.25,max(0,3-d))`秒、既存値を短縮しない |

共通M1/M2/M3/M4/M5/M7/M8・E1〜E6（M6不要、種はM3→M2）；native入口はstatus OnCreate/ActiveLogicUpdate/OnDestroyActorとExplosion.OnHitのowner/parent/epoch限定adapter、Shield/Displacement/OverlapCircle/Dispatch。host裁定・予告/星点/印/引力円を参加者同期；死亡/解除/部屋変更で追加分破棄・pooled field復元（報酬単独は不変）。追加damageは優位AD/AP・指定element・crit/proc再発動なし；全場本人計2個、最古置換。
根拠：`/home/wang/dev/sod-prompts/i48-facts-nyx.md`（実技・報酬・APIのfile:line、asset未取得と設計値を分離）。

### 3.7 `boss_erebos`：終星の流衣 / Laststar Vesture

`Mon_Special_BossErebos`／報酬`Gem_U_LastStarlight`。定点の波紋を起動し、移動で吸排を反転、3周目に予告線と中心爆発を重ねる（全数値は設計値、H=max(AD,AP)）。

| 部位 | 装備名（日 / 英） | 実技翻案・単品効果 |
| --- | --- | --- |
| Weapon | 終星の波琴 / Laststar Waveharp | Gaze：E1照準方向に固定線6m/幅.6m、予告.3秒→.08H Lightを.3秒間隔で2回、CD5秒・各敵2hit上限 |
| Armor | 重力なき肩衣 / Weightless Mantle | AntiGravityの浮上→降下：E4被damage地点半径2mを予告.5秒→.12H Light着地円＋敵を中心から≤.5m/.2秒押出し、CD8秒（boss/immune移動なし） |
| Charm | 残照の星核 / Afterglow Starcore | phase Whitehole：E3終点半径2.5mに.3秒予告→.10H Light＋最大3敵を≤.6m/.3秒押出し、CD6秒（boss/immune移動なし） |
| Head | 隕光の冠 / Meteorlight Crown | Meteor：E2照準≤8mに半径1.8m予告.7秒→.18H Fire着弾、CD7秒、HP比例damage/浮遊stunなし |
| Hands | 星雨の指環 / Starshower Fingerbands | StarRain：E1対象の確定位置≤8mへ2発を.2秒差で射出（速度12m/s/寿命1秒）、到着予告円半径1mで各.08H Light、CD6秒・敵最大2hit |
| Feet | 波紋の履 / Ripple Shoes | Ripple：E3終点から半径1/2/3mの円を.2/.4/.6秒後に順次起動、各.04/.05/.06H Light、CD6秒・敵最大3hit |

| 段階 | セット単独の操作循環（累積、どの部位組合せでも成立） |
| --- | --- |
| 2点 | E2照準≤8mに中心印1個/4秒、次E1で半径2m/.10H→.3秒後半径3m/.14H Light波紋を起動し印消費、CD6秒 |
| 3点 | 印待機中E3を1回行うと「排」を「吸」に反転。起動波紋直前に半径3mの最大3敵を外/内へ≤.8m/.3秒移動；印消費で境界印+1（最大3/12秒、boss/immune移動なし） |
| 6点 | 波紋起動3回で印3→次E2で全消費、照準≤8mに半径4m場/3秒＋本人shield .38H/3秒。2秒目から中心の直交6m線2本（幅.6m）を予告.5秒→各.225H Light、3秒目に中心.45H Light円；準備中E3で線角度を1回だけ変更、CD10秒。A90/cap100+B38/cap40=1.85、E3なしでも成立 |

| 報酬連携 | `Gem_U_LastStarlight`装着＋対応部位数（累積；本人の実native instanceのみ） |
| --- | --- |
| 2部位 | `Ai_Gem_U_LastStarlight`のprepare delayを`d-min(.20,max(0,d-.25))`秒へ短縮；既存.25秒未満を延ばさず、予告から吸引への移行を直接早める |
| 4部位 | 当該native吸引半径`r+min(1,max(0,12-r))`m、native tick円`r+min(.5,max(0,8-r))`m；各元値を独立に拡張し既存cap超過値は下げない |
| 6部位 | native prepare中E3で1回だけ元中心から移動終点方向へ≤2m有効地点に再照準（有効化後は固定）。native durationは`d+min(1,max(0,6-d))`秒、skill/gem失効の自滅を尊重し追加寿命のみ解除時撤回 |

共通M1/M2/M3/M5/M7/M8・E1〜E6（M6不要、全場は非Entity・召喚procなし）；native入口はAi.OnCreateSequenced前のgem/skill/epoch識別とprepare/active/expiry adapter、Network_info/OverlapCircle/Dispatch/GiveShield。host裁定・予告線/吸排/中心/印/残時間を参加者同期；死亡/解除/部屋変更で追加分破棄・field復元・待機deadline再裁定（native単独不変）。追加damageは優位AD/AP・指定element・crit/proc再発動なし；全場本人計2個、最古置換。
根拠：`/home/wang/dev/sod-prompts/i48-facts-erebos.md`（実技・報酬・APIのfile:line、星生成/HP余命を推測せずasset未取得を分離）。

### 3.8 `boss_seeker`：幻彩の追装 / Mirage Spectrum Gear

三相オーブを巡らせ分身で射角を増やし、狭窄照準から爪列へ。`Mon_DarkCave_BossSeeker` / 報酬`Gem_U_SoulPrison`。以下すべて設計値、H=max(AD,AP)、追加damageは優位物理/魔法・同値物理、明記Fire以外無属性、追加会心/attackeffectなし。

| 部位 | 装備名（日 / 英） | 単品の技翻案（本人native eventのみ） |
| --- | --- | --- |
| Weapon | 幻彩の球杖 / Mirage Orb Rod | 球弾幕：E1で主対象の確定点へ球1発、射程8m/寿命1秒、到達爆発半径1.5m・0.18H、CD3秒。 |
| Armor | 分身の外套 / Double Mantle | 幻覚分身：E4被弾で非Entity分身1体を本人地点に2秒表示、本人shield0.18H/2秒、CD10秒。taunt/敵味方のtarget変更なし。 |
| Charm | 魂窓の珠 / Soulwindow Pearl | 緑球の寿命爆発：E2で最寄り敵（8m）へ誘導球1発、寿命2秒/移動上限8m、命中または満了で半径1.5m・0.24Hを1回、CD6秒。 |
| Head | 狭界の面 / Narrowworld Mask | 狭窄技の照準→clawを敵地点へ：E1で主対象点に0.4秒予告、半径2m・0.20H、CD5秒。本人/味方の画面暗転なし。 |
| Hands | 紫影の手袋 / Violetshadow Gloves | 紫場：E2で最寄り敵の確定点（8m）に半径2m/2秒の場1個、0.5/1.5秒に各0.10H、CD6秒、追従/追加子場なし。 |
| Feet | 裂け目の歩み / Fissure Steps | blink後の赤裂け目：E3で出発点に0.3秒予告→半径1.5m・0.22H＋Fire、非boss/非CC免疫だけstun0.25秒、CD5秒。生成移動はE3対象外。 |

| 段階 | セット単独のプレイ変化 |
| --- | --- |
| 2点 | E1成功ごとに三相を緑→紫→赤へ1段進め、CD3秒でその相の1発0.16H：緑は8m/1秒の球、紫は主対象点の0.4秒予告/半径1.5m、赤は同点の半径1.5m＋Fire。1activation1count、特定部位不要。 |
| 3点 | E3で出発点へ非Entity分身1体/3秒（CD5秒）。次E1の相を分身起点から主対象確定点へ1回だけ複製0.12H/射程8m、即消失。Armor表示分身と合算最大1体、召喚proc/lootなし。 |
| 6点 | E2で敵点（8m）を照準、0.5秒後から0.25秒間隔で90度/長さ4mの爪扇3回、各0.30H、本人shield0.38H/2.5秒、CD10秒。A90/100=3×30爪、B38/40=防護38；移動不能/無敵/暗転なし、特定記憶不要。 |

| 報酬連携 | 対象エッセンス装着時、2/4/6段階は累積・当該native救命branchだけ |
| --- | --- |
| 2部位 | Soul Prisonのquality倍率済みnative救命Healへmin(元回復量×10%,0.20H)加算。別Healを発行せずnative overflow処理に渡す；gem消費/HP=1を維持。 |
| 4部位 | 同native HealのdiscardedAmountが出た時だけ追加shield=min(overflow×25%,0.30H)/6秒を1回。元の同量・無期限shieldは変更せず、他Healやshieldから再発動しない。 |
| 6部位 | 同native救命Healのoverflowから本人6m内の最寄りalive味方Hero1人（本人除外）へshield=min(overflow×25%,0.40H)/3秒を1回。overflow無し/味方無しは0；4部位本人shieldとは別target、native本人の無期限shieldとgem消費を維持、無敵延長なし。 |

共有M1/M2/M3/M6（非Entity）/M7/M8、E1–E6；native `EntityEvent_OnAttackHit/OnCastComplete`、`StartDisplacement`完了、`DoDeathInterrupt` callback scope＋`takenHealProcessor/ActorEvent_OnDoHeal`＋`GiveShield`のSoulPrisonAdapterが入口。hostがowner/parent/instance/gem/epochを照合・有限裁定し相/照準/分身/救命shieldを参加者表示；他Heal/追加shieldから再反応なし。native消費確定callbackの本人/味方有限shieldだけ寿命まで保持、任意の減装/死亡/部屋移動は追加FX/ledger/効果解除。
根拠・未取得値・adapter詳細：`/home/wang/dev/sod-prompts/i48-facts-seeker.md`。

### 3.9 `boss_azurak`：轟召の重装 / Roarcall Heavy Gear

`Mon_Despair_BossAzurak`／穴掘り `St_U_Burrow`：二重踏みの予告を設置し、自分の砲塁を守って前進する。**セット単独で6点まで成立、別記憶・召喚手段不要、companionなし**。以下すべて設計値、追加damageはH優位/同値物理・None、追加会心/通常attackEffectなし。

| 部位 | 装備名（日 / 英） | 技の翻案・単品効果（設計値） |
| --- | --- | --- |
| Weapon | 轟召の戦棍 / Roarcall Mace | 二重踏み：E1で命中地点半径2.5mに0.09H×2、間隔0.15秒、CD4秒、1地点・2hit固定。 |
| Armor | 砲塁の鎧 / Artillery Rampart | 踏み防御：敵対native実HP被damage E4で本人shield0.10H/2秒、CD6秒、非重複、無敵blockなし。 |
| Charm | 呼び声の角笛 / Calling Horn | 咆哮：E1で本人円3mに0.14H＋押出し0.8m、CD6秒；boss/CC immuneには移動なし。 |
| Head | 召集の兜 / Muster Helm | 咆哮の安全地帯：E2で本人の開始地点に円2m/1秒、0.5秒後も本人が円内なら本人shield0.08H/1.5秒、CD6秒、1回のみ。 |
| Hands | 地鳴りの拳 / Earthrumble Fists | 砲撃：E1で8m以内の命中地点に半径1.5mの0.6秒予告→0.18H爆発1回、CD5秒、保存地点を追尾しない。 |
| Feet | 転輪の鉄靴 / Rolling Sabatons | 転輪の着地：本人native回避/移動完了E3の地点で円2mに0.16H、CD5秒；新dash/無敵/反復rollなし。 |

| 段階 | 組合せに依存しない追加の遊び（累積） |
| --- | --- |
| 2点 | profile自身のE1を3回数える（最大3、最終成功から4秒失効）。3回目の命中地点8m以内へ円2.5mの地鳴り0.16H、CD3秒；各部位の印・技は不要。 |
| 3点 | 同じ3回目に円2.5mの予告を同地点へ残し、0.45秒後0.22Hで再踏み、CD5秒。動く敵の先に足場を固定する。 |
| 6点 | 同じ3回目で円3m/予告0.35秒→0.45H×2（0.20秒間隔）＋本人shield0.38H/3秒、CD8秒、固定地点1、shield非重複。追加予算A=90/cap100（damage）、B=38/cap40（shield）、正規化1.85。召喚撃破/死亡を一切待たない。 |

| 報酬連携 | 当セット2/4/6部位＋Burrow装着、native本人Emergeのみ（累積・生成は除外） |
| --- | --- |
| 2部位 | native出土hitのstunへ最大0.15秒追加、追加後上限1秒（元が1秒以上なら追加0）、boss/CC immune/非敵対は追加0。Burrowの無敵duration・再使用config不変。 |
| 4部位 | 同Emergeのnative敵対damageへ20%加算、全対象合計cap0.20H/activation、CD8秒。native範囲/stun/元damageは保持、多体で加算枠を増やさない。 |
| 6部位 | 4部位の加算を25%・合計cap0.45Hへ置換（CD8秒維持）、同Emergeの本人dazeを0.05秒短縮/下限0秒。出土の制圧→即攻撃に直結し、別記憶や汎用hasteを与えない。 |

共有M1/M3/M5/M7/M8＋E1〜E6、BossMove/RewardProfile・BossReward共通Kind。入口：native attack/cast/移動/実HPdamage＋BurrowExitAdapter（native Emergeのstun/daze引数、instance dealtDamageProcessor）。hostが位置/CD/世代/epoch裁定、参加者へ予告/2踏み/盾を同期；死亡・装備不足・部屋移動・instance破棄で場/handler/印を解除、pool持越し禁止。
根拠・未取得値・API詳細：外部 `/home/wang/dev/sod-prompts/i48-facts-azurak.md`。

### 3.10 `boss_primus_aeron`：三相の武装 / Threefold Armament

主撃・記憶・移動をForce/Adapt/Rageへ翻案し、退いた場所から三相連撃する。型=`Mon_Primus_BossPrimusAeron`、固有報酬なし。以下は設計値、damage=%H／shield=%最大HP。

| 部位 | 装備名（日 / 英） | 実技翻案・単品効果（セット不要） |
| --- | --- | --- |
| Weapon | 三相の大剣 / Threefold Greatsword | Force剣撃：E1で前方3m・90°扇18、無属性、1hit/敵、CD4秒。 |
| Armor | 適応の胸甲 / Adaptation Cuirass | Rage減衰盾：E4本人の敵対native被damage後に盾8、3秒で線形減衰、1枚、CD12秒。HP再設定・無敵なし。 |
| Charm | 相転の核 / Phasechange Core | Adapt光連鎖：E2の方向へ光弾1本、射程8m/速度12/幅0.4、初着弾後3m内の未命中敵へ最大2跳、各8、間隔0.15秒、最大3敵/寿命1.5秒、CD7秒。 |
| Head | 激情の面 / Rage Mask | Rage双剣：E1前方3m・120°を0/0.25秒に各10の闇斬撃、同敵合計20まで、CD5秒。 |
| Hands | 流火の双手 / Flowfire Grips | Doom落星：E2の指定点を本人6m内へ裁定し、0.5秒予告→半径2mの火18を1回、同時1場、CD8秒。隕石弾幕・stunなし。 |
| Feet | 氷爪の脛当て / Iceclaw Greaves | IceClaw二連：E3着地点の前方2.5m・120°へ0/0.25秒に各10の冷斬撃、合計20、CD7秒。自動dash/追跡Frost/stunなし。 |

**2点**：E1/E2/E3でForce/Adapt/Rageを選ぶ小mode（入力gate0.5秒）。共有CD3秒でその入力の技1つ：Force=3m/90°無属性扇12、Adapt=8m/幅0.4光弾12、Rage=着地半径2m闇円12。CD中はmodeだけ更新、攻撃予約なし。

**3点**：E3で出発点に非Entityの相紋1個を4秒残す。次の実発動E1/E2相技は本人6m内の相紋を消費し、0.35秒予告後そこから発射（本人起点との二重発射なし、damage不増）。新E3で上書き、範囲外なら本人起点。

**6点**：8秒内に異なる3入力を揃えるとCD18秒の三相合流、成立入力の通常相技を置換。A90/cap100=0/0.3/0.6秒に無属性4m/90°扇30＋光8m/幅0.6線30＋闇半径2.5m円30（起点/向き固定、同敵合計90）。B38/cap40=盾38を1枚、6秒で線形減衰。相紋が有効ならそこ、なければ本人起点；完成時に3入力印/相紋を消費、CD中の完成は保持しない。
連携なし（2/4/6ともなし）。特定記憶・報酬・召喚・HP閾値は不要；任意2/3部位で段階成立し、記憶単独は従来どおり。

**M/E・入口**：M1/2/3/4/7/8、E1/2/3/4/6；既存native主撃帰属/ConfirmedUse＋`EntityControl.StartDisplacement`寿命adapter、`Actor.PhysicalDamage/MagicDamage().Dispatch`・独立shield・host Tick。M6/E5/報酬adapterなし。ホストが装備/起点/命中を裁定、参加者はmode/相紋/予告のみ表示；1形状8敵上限、epoch変更/死亡/部屋離脱で予約・印・自前盾解除、生成物再入力・無敵・即死・永久CC禁止。
根拠・未取得値・mode詳細：`/home/wang/dev/sod-prompts/i48-facts-primus_aeron.md`（本体値と上の設計値は別）。

### 3.11 `boss_light_elemental`：光裂の法装 / Radiant Fracture Raiment

`Mon_Special_BossLightElemental` / `St_U_WorldCracker`：予告光線→落雷→3段掃引を充填で解禁、短命光晶が発動中を支持。報酬native beamの旋回・幅・実終端へ直接連携。
以下は全て設計値。H=max(AD,AP)、追加damageは優位type（同値物理）/Light、追加会心・attackeffect・procなし、セット生成effectはnativeイベントから除外；CDは本人/部位別、1activation1count。全装備beamは地形でclipし対象1beam1hit。

| 部位 | 装備名（日 / 英） | 実技翻案・単品挙動 |
| --- | --- | --- |
| Weapon | 光裂の杖 / Radiant Fracture Staff | BeamAtk：E1で敵方向を固定、0.30秒予告後に長7m×幅0.8mの瞬間beam0.28H、CD5秒。 |
| Armor | 光片の法衣 / Radiant Shard Robe | Summonの本体shield：E4被弾で本人にshield0.20H/2秒（独立container、非加算）、CD7秒；永続無敵なし。 |
| Charm | 世界罅の結晶 / Worldfissure Crystal | Summonの支持役を非Entity光晶へ翻案：E2で本人2m前の合法地点に1光晶/3秒、0.5秒後最も近い敵（光晶から6m内）へ長6m×幅0.6mbeam0.24Hを1回、本人所有最大1、CD8秒、native召喚proc/lootなし。 |
| Head | 雷光の冠 / Lightning Halo | Lightning：E2でカーソル合法地点最大8mを固定、0.45/0.75秒予告後半径1.2mの落雷各0.16H、同時1列、CD7秒。 |
| Hands | 光束の手巻き / Beam Wraps | BeamBarrage：E1の3回目/6秒で前方-25°→+25°を0.8秒掃引する長5m×幅0.6mbeam、0.30秒予告、対象全掃引で1hit0.30H、最大1、CD6秒。 |
| Feet | 閃光の履 / Flash Shoes | BeamAtk方向固定：E3 native移動完了で移動方向と±20°の3方向を0.25秒予告、長4m×幅0.5m各0.08H、同対象総cap0.24H、CD5秒；追加入力/自動移動なし。 |

**2点**：E1/E2で光相1（最大3、寿命6秒）。3相の次E1/E2で全消費、敵/カーソル方向へ0.35秒予告→長8m×幅1mbeam0.35H、CD4秒；任意2部位で成立、HP閾値を充填phaseへ置換。

**3点**：2点beamの地形clip済み終端へ0.65秒予告の半径1.8m落雷0.30H、同時1予約；任意3部位で成立、特定記憶不要。

**6点**：2点発動後、本人起点長6m×幅0.7mbeamを-45°→-15°/-15°→+15°/+15°→+45°へ各0.4秒掃引（予告0.3秒、各0.30H/対象1hit）、旧光晶を更新した支持光晶2個が本人へ各shield0.19Hを与える（1container合計0.38H/2秒非加算、光晶寿命3秒）、CD12秒、同時1phase、全光晶本人最大2。追加A=90/cap100（掃引計0.90H/cap1H）、B=38/cap40（shield0.38H/cap0.40H）、予算1.85未実測。

**報酬連携2**：本人装着WorldCrackerのnative Ai_U_WorldCrackerだけangleSpeedを本体取得値+30°/秒（最大native×1.5、native>0のみ）へ変更しカーソル旋回を改善；本体channel/消費/持続は維持。

**報酬連携4**：同native beamのSphereCast radiusを本体取得値+0.30m（最大native×1.5、native>0のみ）へ変更；元raycast/maxDistance/対象別damageIntervalを維持、他記憶不変。

**報酬連携6**：同敵へのnative成功tick3回（gap<=1秒、台帳最大3敵）で直前の実damage用clipped終端に半径1.8m/0.35秒予告/0.45H Light pulse1回、CD6秒、native beam lifetime最大2pulse・同時1；生成pulseはtickに数えずchannel停止時予約中止。
M1/3/6/7/8＋E1/2/3/4/5/6；M6は非Entity光晶（native Summon procなし、host有限攻撃＋通知）。API：Ai_U_WorldCracker.OnCreate/OnDisable(public angleSpeed/radius保存復元)、ActiveLogicUpdate成功Dispatch/実clipped numのIL通知入口、Actor.GiveShield。host裁定、参加者へ光相/掃引幅/雷予告/光晶期限表示、死亡/KO/部屋/装備epoch/報酬解除/channel停止で独自状態破棄・native取得field復元。
調査根拠・入口詳細：`/home/wang/dev/sod-prompts/i48-facts-light_elemental.md`（共用Beam調査含む、serialized実値未取得、静的調査のみ）。

### 3.12 `boss_maw`：飢影の狩装 / Ravenous Shadow Gear

`Mon_Special_BossMaw`／大噛みつき `St_U_BigChomp`：左右の牙と予告煉獄で追い詰め、低HPで前後を噛み吸収する。**本体報酬はhit数/boss重みの回復・盾・CD短縮でありdamage比例lifestealではない**。以下は設計値、追加damageはH優位/同値物理・Dark、追加会心/通常attackEffectなし。

| 部位 | 装備名（日 / 英） | 技の翻案・単品効果（設計値） |
| --- | --- | --- |
| Weapon | 飢影の牙刃 / Ravenous Fangblade | 噛み付き：E1で命中方向の扇120度/3mに0.18H、CD4秒、dash/無敵なし。 |
| Armor | 煉獄の革鎧 / Purgatory Leathers | ShieldConversion：敵対native実HP被damage E4で本人shield=min(0.14H,不足HP×0.15)/2秒、CD6秒、非重複、放置rechargeなし。 |
| Charm | 大顎の護符 / Greatjaw Talisman | 噛み付き人数回復の有限化：E1の4回目（最大3保存、4秒失効）で本人heal=min(0.06H,不足HP×0.08)、CD5秒、多体でも1回。 |
| Head | 飢えの面 / Hunger Mask | 滅殺態勢：E2で確定詠唱方向へ左右2斬波、各0.08H、幅1m/射程6m/寿命0.6秒、間隔0.15秒、CD6秒、追尾なし。 |
| Hands | 貪りの爪 / Devouring Claws | 煉獄の戻る回復：E1の実敵HPdamage×0.10を本人へ吸収、cap0.04H/activation、CD4秒。無敵hit/味方/反射/生成damageは回復0。 |
| Feet | 影歩きの靴 / Shadowwalk Boots | native移動完了E3後2秒以内の次E1に追加扇90度/3m/0.18H、CD5秒、1印だけ。untargetable・追加dashは付与しない。 |

| 段階 | 組合せに依存しない追加の遊び（累積） |
| --- | --- |
| 2点 | profile自身のE1を3回数える（最大3、最終成功から4秒失効）。3回目に前方扇120度/3m/0.18H、CD3秒；本人HP50%以下では前後各90度/3mの扇に各0.09H（合計0.18H）を配分。Weapon/Feetの印は不要。 |
| 3点 | 同じ3回目の命中地点8m以内へ煉獄円2.5m、0.5秒予告→0.22H爆発1回、CD5秒。HP50%以下では地点を本人足元に替え、囲む敵を吸収狩りへ誘う（追加damage量不変）。 |
| 6点 | 同じ3回目で前方から左右±30度の120度扇/3.5mを各0.45H（0.20秒間隔）、その実敵HPdamage合計×0.45を本人heal、総cap0.38H、CD8秒。HP50%以下では前後90度扇/3.5mを各0.45Hへ置換、吸収cap不変。追加予算A=90/cap100（damage）、B=38/cap40（heal）、正規化1.85；0damageなら吸収0、過剰回復は捨てる。 |

| 報酬連携 | 当セット2/4/6部位＋Big Chomp装着、本人native instanceの1回のOnAfterDelayへ直接作用（累積） |
| --- | --- |
| 2部位 | 敵対のalive/非immune native hitだけでboss重みを含むwを記録（cap3）。native本人Healへmin(0.08H,本体healPerHit実値×w×0.20)加算、追加枠CD8秒；本体の味方hit処理不変。 |
| 4部位 | 同じnative GiveShieldへmin(0.10H,本体shieldPerHit実値×w×0.20)加算、元duration維持。同instance/同8秒枠で1回、多体でw上限を増やさない。 |
| 6部位 | 同native instanceが自身firstTriggerへ行うCD短縮にmin(0.50秒,w×0.25秒)追加、同8秒枠。native minimum/scale/canReceiveCooldownを維持、別記憶/汎用after-useバフなし。 |

共有M1/M2/M3/M7/M8＋E1〜E6、BossMove/RewardProfile・BossReward共通Kind。入口：native attack/cast/移動/HPdamage＋BigChompAdapter（OnHit敵対重み、instance dealtHeal/Shield/CooldownReductionProcessor）。hostがHP形態/吸収/CD/世代/epoch裁定、参加者へ牙/斬波/円予告を同期；死亡・装備不足・部屋移動・instance破棄で印/場/handler解除、native報酬祠・drop/ID不変、M6なし。
根拠・未取得値・API詳細：外部 `/home/wang/dev/sod-prompts/i48-facts-maw.md`。

### 3.13 `boss_obliviax`：忘針の襲装 / Oblivion Needle Gear

影歩きの出発点を待伏せ射角に変え、残影砲座から予告砲撃へ。`Mon_Special_BossObliviax` / 報酬`St_U_ShoutOfOblivion`。以下すべて設計値、H=max(AD,AP)、追加damageは優位物理/魔法・同値物理＋Dark（新設計指定）、追加会心/attackeffectなし。

| 部位 | 装備名（日 / 英） | 単品の技翻案（本人native eventのみ） |
| --- | --- | --- |
| Weapon | 忘針の連刃 / Oblivion Needleblade | 左右連刃dash sweepを線撃へ：E1で主対象方向に幅1.5m/長さ4m・0.18Hを1回、本人移動なし、CD3秒。 |
| Armor | 巣影の外套 / Nestshadow Mantle | ChargeSequence消失中防護を有限shieldへ：E2で本人shield0.18H/2秒、CD8秒、不可視/無敵/入力拘束なし。 |
| Charm | 忘却の喉石 / Oblivion Throatstone | Artillery：E2で最寄り敵の確定点（8m）に0.6秒予告→半径1.5m・0.24H、非boss/非CC免疫のみslow20%/0.5秒、CD6秒。 |
| Head | 砲座の面 / Turret Mask | Turret姿勢：E6で本人自発移動1m未満/1秒を確認した次E1で主対象確定点へ球2発、0.2秒間隔/射程8m/寿命1秒/爆発半径1m、各0.09H、CD4秒。移動で待機解除、本人拘束なし。 |
| Hands | 針継ぎの手袋 / Needlestitch Gloves | Needleの全域Hero攻撃を近距離pulseへ：E1で本人周囲半径2m・0.18H、非boss/非CC免疫のみstun0.25秒、CD6秒。HP割合damage/技能CD resetなし。 |
| Feet | 攫い影の靴 / Abducting Shadow Boots | 影歩き後の捕縛翻案：E3で到着点3m内の最寄り敵1体に0.12H、非boss/非CC免疫のみ本人側へ最大1m/0.25秒pull、CD6秒。味方/部屋移動/入力停止なし。 |

| 段階 | セット単独のプレイ変化 |
| --- | --- |
| 2点 | E3で出発点を3秒記録（1token、CD4秒）；次E1でその起点から主対象方向へ幅1.5m/長さ8mの待伏せ線撃0.20Hを1回、token消費。単品Feetの敵pull/生成移動は記録を増やさない。 |
| 3点 | 待伏せを撃った起点に非Entity残影砲座1体/3秒・残弾1。さらに次の別activationのE1で主対象確定点（砲座から8m）へ予告0.4秒→半径1.5m・0.15Hを1回、砲座消失；新設置で旧砲座解除、召喚proc/lootなし。 |
| 6点 | E2で最寄り敵点（8m）とそこから本人射角左右に2mずつの計3地点を固定予告；0.6/0.8/1.0秒に半径1.5m・各0.30H、本人shield0.38H/2.5秒、CD10秒。A90/100=3×30砲撃、B38/40=防護38；人数増殖/追従/他召喚要求なし。 |

| 報酬連携 | 対象記憶装着時、2/4/6段階は累積・本人native Shout instanceだけ |
| --- | --- |
| 2部位 | Shout固有のhunt倍率にlevel当たり0.03追加、native clamp0–5に従い合計bonus最大0.15。level0は追加0、実hunt値/全記憶倍率を変更せずnative会心条件を維持。 |
| 4部位 | Shout自身の後退displacementだけ4m→5m（追加上限1m）、0.4秒/terrain越え不可を維持し安全掃引点にclamp。通常回避や生成待伏せには適用しない。 |
| 6部位 | Shout自身のnative命中stunだけ実duration+0.4秒、追加上限0.4秒・敵1体1activation1回。boss/CC免疫には追加せず、詠唱無敵/complete後1秒無敵は延長しない。 |

共有M1/M2/M3/M4/M5/M6（非Entity）/M7/M8、E1/E2/E3/E5/E6；native `EntityEvent_OnAttackHit/OnCastComplete`、`StartDisplacement`完了、skill-owned `ActorEvent_OnAbilityInstanceBeforePrepare`＋`Ai_U_ShoutOfOblivion.currentDamageAmp/OnCreate/OnHit`のShoutAdapterが入口。hostがowner/parent/type/activation/epochを照合し射角/予告/捕縛を参加者表示；死亡/部屋/装備変更・native cast cancelで追加FX/砲座/ledger/scope解除、味方操作/カメラへ干渉しない。
根拠・未取得値・adapter詳細：`/home/wang/dev/sod-prompts/i48-facts-obliviax.md`。

### 3.14 `boss_polaris`：墜聖の双装 / Fallen Sanctity Regalia

記憶で聖の防護、移動で獣の踏撃、盾の終わりを攻勢へ変える。型=`Mon_Special_BossPolaris`、固有報酬なし。以下は設計値、damage=%H／shield・heal=%最大HP。

| 部位 | 装備名（日 / 英） | 実技翻案・単品効果（セット不要） |
| --- | --- | --- |
| Weapon | 墜聖の金槍 / Fallen Golden Spear | GoldenSpear：E1で正面±6°に光槍2本、各12、射程8m/速度12/幅0.4/寿命0.8秒、同敵は片槍のみ、CD5秒。 |
| Armor | 聖獣の胸甲 / Sacred Beast Cuirass | 初撃減衰防護：敵対native E4被damage後に盾8、4秒で線形減衰、1枚、CD12秒。本体の巨大armor/無敵は移さない。 |
| Charm | 星無き聖印 / Starless Sacred Seal | Purgatory還流：E2指定点（本人6m内）に半径2m火場1個、0.4秒予告後0/0.5/1秒の3pulse各6、同敵合計18；最初の成功hitで本人heal3を1回だけ、CD9秒。 |
| Head | 反呪の冠 / Counterspell Crown | CounterSpell：E2で盾4/1秒＋反撃窓1秒、最初の敵対native被damage E4後に攻撃者へ光弾14を1本（8m内、射程8/速度12/幅0.4/寿命0.8秒）、CD10秒。skill窃盗/stunなし。 |
| Hands | 降火の手甲 / Rainfire Gauntlets | RainFire：E1対象地点（本人6m内）を中心に半径1.5m上の0/120/240°へ火円3個（各半径1.2m）、各0.4秒予告→0/0.15/0.3秒ずれで各6、同敵合計18、CD8秒。追従/無敵なし。 |
| Feet | 踏星の鉄靴 / Starstomp Sabatons | 読めるJumpStompへ改名：E3着地点で0.25秒予告→半径2.5mの無属性踏撃18を1回、CD7秒。追加移動/地形越え/stunなし。 |

**2点**：Holy/Beastの小mode（E2/E3入力gate0.5秒）。E2=Holyへ＋段階専用盾6/3秒（盾CD8秒）、E3=Beastへ＋着地半径2.5m無属性踏撃18（攻撃CD4秒）。CD中もmode選択可、盾/攻撃予約なし。

**3点**：Holy→BeastはE3だけでなく専用盾の使切り/自然失効でも成立。E3なら出発点、盾終了なら現在地に離陸印1個/4秒；移行E3またはBeast中の次E1/E3踏撃を、本人6m内の印へ0.35秒予告で移し印を消費（着地との二重hitなし）。同mode入力は印を新設せず、E2で旧印破棄。

**6点**：E2開始から8秒内のHoly→Beast→Holy帰還でCD18秒の双相技、最後E2の盾6を置換。A90/cap100=印（有効かつ6m内）/本人から光槍45（射程8/速度12/幅0.4、最初の敵/壁/射程末で停止）→終点に0.5秒予告の半径3m無属性星踏45、同敵合計90。B38/cap40=盾38を1枚/6秒線形減衰（盾6と重複授与しない）。向きは最後E2で固定、cycle/印消費、同mode連打で進行/期限延長なし、CD中の完成は保持しない。
連携なし（2/4/6ともなし）。報酬・変身・特定記憶・特定部位・召喚不要；任意2/3部位で防護→移動→帰還成立。盾の自然失効でも遊べ、被弾は必須でない。

**M/E・入口**：M1/2/3/4/7/8、E1/2/3/4/6；native主撃帰属/ConfirmedUse＋`EntityControl.StartDisplacement`寿命adapter、`Actor.PhysicalDamage/MagicDamage().Dispatch`・`DoHeal`・独立shield終了観測・host Tick。M6/E5/報酬adapterなし。ホスト裁定、参加者mode/印/予告表示、1pulse8敵/同時4場まで；減装・記憶交換・死亡・部屋離脱で印/予約/自前盾解除。生成再入力・無敵・即死・永久CC・二重damage禁止。
根拠・欠落IL/未取得serialized値・mode詳細：`/home/wang/dev/sod-prompts/i48-facts-polaris.md`（Beam/CursedDiveの詳細は推測しない）。

## 4. マルチプレイの整合条件・実装段階・既存への影響

確認項目は仕様上の整合条件のみ。テスト設計・計画・新規ケース提案は本書の対象外で、テスト担当に委ねる。

| 確認項目 | 必須の整合条件 |
| --- | --- |
| 識別 | ホスト自身と参加者が同じfactの型名・難易度区分・深度を使用。白夜／暗月は個別撃破、同時死亡の雑魚には限定抽選なし |
| 抽選 | 各参加者に1回ずつ独立抽選。同じpであって、当否・部位・特性が同一である必要はない。ホストの当選を全員へ配らない |
| 保留・再送・継続 | 解決済みEventIdを共有する通常＋専用報酬の処理単位を維持。ルール選択待ち・再接続・チェックポイント復元で型名やpを変えず、支給を増殖させない |
| 効果 | 同じ装備・強化・覚醒・星図ならホストと参加者のprofile導出は同じ。2/3/6の累積・部位channel・固有modifierは装備epochで再計算 |
| 連携 | 自分の対応セット部位数で2/4/6の最高段階を1つ選び、自分の対象報酬装着で成立。減装・解除で再判定し旧余韻を失効。白夜／暗月の部位数は別集計。対象起点・上限はホストが適用 |
| 互換 | マージ順で確定するProtocol版と内容指紋の一致が前提。旧版混在時に「同じ表示なのにホストだけ効果不反映」を正常運用扱いしない |

全14セットを同じ設計原則で改訂済み。下表は現在の実装境界。Demonの専用Power案は共通profileへ置換済みで、旧汎用効果との併存・aliasを残さない。

| 実装段階 | 範囲 |
| --- | --- |
| 段階A（実装済み） | 専用取得基盤＋§2.4の共通8機構・統合3経路・Protocol16／保存連携。Demonの1セット6部位とHysteria adapter |
| 段階B-1（実装済み） | §3.2〜§3.5のSkoll／Infernus／白夜／暗月、4セット24部位、GlacialCore／EternalFlame／共通Beam adapter。白夜／暗月は独立profile、Protocol17 |
| 段階B-2（実装済み） | §3.6〜§3.10のNyx／Erebos／Seeker／Azurak／PrimusAeron、5セット30部位、HerWorld／LastStarlight／SoulPrison／Burrow adapter。Primusは連携なし、Protocol17を維持 |
| 段階B-3（実装済み） | §3.11〜§3.14の光の精霊／大顎／オブリヴィアクス／Polaris、4セット24部位、WorldCracker／BigChomp／ShoutOfOblivion adapter。Polarisは連携なし。全14セットと共通M1〜M8／native帰属／表示を事前確保・有界化、Protocol17を維持。較正は未実施 |
| 段階間 | 最終IDを最初から使用し、無効仮定義・互換aliasを残さない。データだけの追加は内容指紋、wire／schema追加はその都度Protocolを次版へ上げる |

既存への影響（テスト設計・修正はしない）：段階Aの49セット・1340固有品、B-1の53セット・1364固有品、B-2の58セット・1394固有品から、B-3で62セット・1418固有品へ増加。全セット部位は372（既存288＋ボス84）、通常ボス段階42・報酬profile12／連携段階36・distinct adapter11。全14組の承認設計をデータ登録済み。既存単品Linkの件数／倍率は不変。Loot／Build／HostGearValidation／説明／図鑑／装備codecのprofile導出と強化milestoneの著作済み効果判定、KillSync／予約表示同期が影響範囲。Protocol17・保存版4・既存ID・プロフィールリセットなしを維持する。保存済みボス部位の旧定義Powerは新版ロード時に新版定義から正規化し、旧Powerとの二重発動を残さない。

## 5. 確定事項（利用者指定）

1. 通常10%／悪夢15%、夢の深さごと+1ポイント・上限20%を採用することが確定。均等抽選・重複あり・専用pityなしで、完成期待は基礎通常147撃破／基礎悪夢98撃破／上限73.5撃破となる。

## 6. 性能

- 対象は段階A〜B-3の全14runtime／11種adapter／M1〜M8／Dispatch／Tick／専用表示。初回入力分もpool・固定配列・delegateを初期化時に確保し、ボス経路の一時配列・closure・動的key・interface列挙を撤去した。
- action keyをCore定義時に固定、set maskを装備時に計算。記憶6枠・gem最大192slotの有界照合と型名cacheで、不変装備の再構築・reflection boxing・入力ごとの文字列生成を避ける。
- M1：reentrant scratch8、対象ID64・wall hit128／scratch。候補走査128、profile対象数以下（B-3は8）、wall配列飽和時は地形越えの線を発射しない。
- M2：本人128弾・set64弾・wave64、弾／waveの既知対象64。1弾1tickの衝突候補128・処理64、連鎖3敵。終端場を先に確保してから発射する。
- M3：本人64場、set合計4（Nyx／Erebosは2）。1token32pulse／visual／point、1場1tickのdue処理32まで。予約全体のfirst-hit heal／HP-only吸収capをscalarで保持する。
- M4／M5：本人displacement1、敵操作gate／displacement各64、敵移動2m上限。native所有中・実行中のobjectを再貸出しない。
- M6：本人32射手、set2・射手32有限射、1tick1射／射手、候補128・native祖先32。新しいEntity／Summonは作らない。
- M7：本人64防護slot、StatBonus／Stun／Slowを事前確保。slot走査64、stun250ms・slow20%／500ms上限、盾減衰は実残量から行う。
- M8：progress64key・CD128key・印8対象×3stack・counter6・8入力ring×64ID・Hysteria爪組8。上限で新規admissionを拒否し、既存期限は維持する。
- 共通帰属：owner64・native cast128／source512／displacement64／Hysteria状態64／爪128／packet64、actor／instance寿命4096。参照countとpacket終了時の通知解放でpool再利用を安全にする。
- 既存adapter：Core128・heal記録64／Core、Flame128・敵CD3、Ink domain192（本人3）／shield128、Beam64・味方2／敵3、HerWorld128・移動64、LastStarlight128、Burrow64。候補処理は最大256。
- B-3：光beam12／本人・対象8／beam・結晶2、WorldCracker128／記録敵3／native寿命pulse2／pending1、BigChomp128／weight3、Shout128／activation128／敵寿命64。各owner状態pool64。
- adapter全体の保守走査は100ms間隔、native event／cancel／pool-clearは即時。Flame／Beamはowner-local列、重複する同時刻のnative再調整とNyx表示更新を間引く。
- 表示：host／client各owner16・owner64effect、snapshot配列0〜64長を事前確保。1秒snapshot、同一metadata通知を省略し、非重要counter更新は100ms間隔、captionは固定glyphを使う。
- 描画：10Hzで距離64m／frustum選別、1repaint64effect・segment試行2048・caption512glyph、円24／扇16／放射16segment。終了・mode／残数変更の通知は間引かない。
- 上記はMOD所有の固定資源・処理上限であり、native physics／Damage・Heal・Shield・Status／DewPersistence JSON／Mirror／Unity内部の確保を含むzero-GC実測ではない。実機frame時間・協力通信・表示の観測は未実施。
