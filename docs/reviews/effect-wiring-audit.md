# 効果の実機配線 監査（装備を最優先）

監査日 2026-10-03。対象は `main` の `dd3324d`（効果の修正コミット直後）。**読み取り専用**で、コードは変更していない。
Core の Debug ビルド（`SodRpg.Core.dll`、21:48 時点）を読み込む使い捨てスクリプトで、実データ（土台360・固有品1190・セット48）を総当たりした。
スクリプトは `C:/Temp/audit/` にある（`probe_content.ps1` `probe_stats.ps1` `probe_caps.ps1` `probe_ids.ps1` `sim_gear.ps1` `sim_build.ps1` `sim_stars.ps1`）。

分類:
A = 配線済み（実イベントを購読し、実在するゲームAPIを呼ぶ）／B = 配線済みだが条件が怪しい・到達しにくい／
C = Coreに実装と試験があるが、ホスト・クライアントのどこからも呼ばれない／D = 実装なし（文章だけ）・明示的に未接続。

> 範囲について。設計者の指示で、この文書は**装備**を実測した結果を先頭に置く。
> Power全91値・仕掛け・連携・合わせ技・反応・契約・出来事・道標・敵側・星図機構の各サブ監査は別担当が行い、
> 結果は調整役に渡っている。この文書には**その表を転記していない**（私の手元には結果がないため）。
> 末尾「他の効果型」には、調整役から聞いた要点だけを「伝聞」と明記して載せる。

---

## 0. 要約

- **装備の効果は、ゲームに届く道が一本ある**。遺物 → `Build.Compute`（クライアント）→ `HostBuildValidation.Encode` → ホストの `TryAccept`
  （遺物をその場で再構築・再計算）→ `HostAuthority.Apply`（`ToStatBonus` と各プロセッサ）。
  能力値26種はすべて行き先がある（A）。うち4種（回復量・シールド量・召喚獣の力・捧げる量の軽減）は `StatBonus` ではなく
  Actor のダメージ／回復／シールドのプロセッサ経由、エッセンス枠2種は `HeroSkill.SetMaxGemCount` 経由。
- **ホストの新しい検証は、正当な装備を拒否・減額しなかった。** 実際の抽選（`Loot`）と実際の強化・限界突破・覚醒・出来事の変更を通した
  **377,040個**の遺物（固有品1190個×レベル3×限界突破4×強化6×覚醒4を含む）と、**3,000ビルド**（装備6枠＋契約0〜4＋今日の夢＋潜行）の
  往復で、拒否0・減額0・クライアントとホストの結果の不一致0。星図込みの結果は 4.5 節。
- **ただし検証には「一つでも不正なら全部捨てる」性質がある**（4.3 節）。遺物1個でも `HostGearValidation` が拒否すると、
  そのプレイヤーのビルド全体（星図・他の装備も）が適用されず、クライアントへ通知もない。ホストのログに1回だけ出る。
  現行データでは起きないが、将来の定義変更で保存済みの遺物が通らなくなると、協力でだけ全効果が消える。
- **最大の問題は「上限による空振り」。** 固有品の固有効果2092行のうち、強化+20・覚醒Ⅲの遺物1個だけで合計上限（`PowerCap`）に
  届くものが **1971行（94%）**。強化+5・覚醒Ⅲでも778行（37%）、強化+10・覚醒Ⅲで1331行（64%）。覚醒の「固有効果1.8倍」と
  強化の伸びの多くは、上限で捨てられる（装備を重ねるなら、1個目の時点で上限）。連携も同様で、記憶加速の連携39個は覚醒Ⅲで上限90に切られる。
- 上位の問題（装備・全体）は 6 節。

分類の件数（装備の効果型）: 能力値26値は A 25・B 1（SacrificeReduction）。基礎能力・特性・系統セット・固有品セット・強化／限界突破／覚醒の倍率・エッセンス枠・固有品の連携は、いずれも配線は A。C（呼ばれない）と D（実装なし）は装備では見つからなかった。倍率と連携の「上限での空振り」は説明のずれとして 3.4・6 に別掲。

---

## 1. 経路（遺物から本体まで）

| 段 | 内容 | 場所 |
|---|---|---|
| 遺物の個体 | 基礎ID・レア度・アイテムLv・強化・限界突破・覚醒・特性・固有効果。基礎能力は保存せず計算 | `src/SodRpg.Core/Game/Relic.cs` |
| 実効値 | `Implicit`（Lv・強化の倍率）、`EffectiveStats()`（特性に強化＋覚醒の特性倍率）、`EffectivePowers()`（固有効果に強化＋覚醒の固有効果倍率） | `Relic.cs:87,98,111` |
| 集計 | 装着中の遺物を足す → 系統セット → 固有品セット（2点=能力値、3点=固有効果）→ 星図 → 今日の夢の固有効果ブースト → 契約の恩恵 → **能力値・固有効果を種類ごとに合計上限で切る** | `Build.cs:98-99,121,135-136,207,220,224,229` |
| 連携 | 固有品の連携を覚醒倍率で伸ばし、`Links.EquippedCap` で切って `Build.Links` へ。常時の能力値には加えない | `Build.cs:109-117` |
| 送信 | クライアントが `Build.Compute` の結果＋遺物・星・契約・今日の夢の**入力**を送る（版13） | `ClientSession.cs:960,986`、`HostBuildValidation.cs:Encode` |
| 検証 | ホストは入力から遺物を再構築（`HostGearValidation`）し、`Build.Compute` をもう一度走らせて、送られた**効果の節（g,l,c,f,n,j,v）**と比べる。能力値と固有効果の合計（s,p,u）は信用せず捨てて、ホストの計算値を使う | `HostBuildValidation.cs:79-110,233`、`HostAuthority.BuildValidation.cs:69` |
| 適用 | `Apply` → `ToStatBonus` を `hero.Status.AddStatBonus`。固有効果は `PowerRuntime`、連携は `SatisfiedLinks`、枠は `ApplyGemSlots` | `HostAuthority.cs:1197,1323-1329,1652` |

`SendBuildIfNeeded` はソロでも同じ道を通る。ソロは「ホスト＝自分」のRPCで送るので、**検証はソロにも効く**（`ClientSession.cs:970` の `NetworkClient.active` が条件）。

---

## 2. 能力値（`Stat` 26値）— 遺物からゲームまで

ゲームAPIは `StatBonus` のフィールド（`C:/Users/67203/AppData/Local/sod-decomp/Dew.Core/StatBonus.cs`）を
`EntityStatus.CalculateStats`（`EntityStatus.cs:1279-1318`）が読む。換算は `StatUnits.ToGame`（`PowerRuntime.cs:575-590`）。
購読は `HostAuthority.Apply` が付ける `AddStatBonus`（`HostAuthority.cs:1325`）で、イベント購読ではなく常駐の補正（遺物の変更時に外して付け直す）。

| 値 | Stat | 行き先（ホスト） | 本体の読み取り | 入手元（実測） | 上限 | 分類 / 備考 |
|---|---|---|---|---|---|---|
| 0 | AttackPct | `attackDamagePercentage` `HostAuthority.cs:1656` | `EntityStatus.cs:1280` | 特性（エピック以上のみ）・基礎・セット2点・系統2点・契約・星 | 100 | A |
| 1 | PowerPct | `abilityPowerPercentage` | `EntityStatus.cs:1282` | 同上 | 100 | A |
| 2 | AttackSpeedPct | `attackSpeedPercentage` | `EntityStatus.cs:1291`（/100） | 特性（武器・手・足）・基礎・系統3点・星 | 80 | A |
| 3 | CritChancePct | `critChanceFlat`（÷100） | `EntityStatus.cs:1294`、`AttackTrigger.cs:66` が乱数と比較 | 特性・基礎・系統4点・契約 | 50 | A |
| 4 | CritDamagePct | `critAmpFlat`（÷100） | `EntityStatus.cs:1292`、`Actor.cs:1103` | 特性・基礎 | 150 | A |
| 5 | MaxHealthPct | `maxHealthPercentage` | `EntityStatus.cs:1284` | 特性・基礎・系統3点・6点 | 120 | A |
| 6 | MaxHealthFlat | `maxHealthFlat` | `EntityStatus.cs:1283` | 特性・基礎・契約 | 600 | A |
| 7 | Armor | `armorFlat` | `EntityStatus.cs:1305` | 特性・基礎・系統2点・6点 | 150 | A |
| 8 | HealthRegen | `healthRegenFlat` | `EntityStatus.cs:1287`、`:976` | 特性・基礎 | 40 | A |
| 9 | Haste | `abilityHasteFlat` | `EntityStatus.cs:1296`、`AbilityTrigger.cs:498`（1/(1+h×0.01)） | 特性・基礎・系統 | 100 | A |
| 10 | MoveSpeedPct | `movementSpeedPercentage` | `EntityStatus.cs:1300` | 特性・基礎・系統 | 35 | A |
| 11 | Tenacity | `tenacityFlat` | `EntityStatus.cs:1298`、`:1692`（×(1−t/100)） | 特性・基礎・系統4点 | 60 | A。基礎の `armor.frost_coat` は強化+20・Lv60で62になり上限60に切られる（唯一の基礎の頭打ち） |
| 12–15 | Fire/Cold/Light/DarkAmp | `*EffectAmpFlat`（÷100） | `EntityStatus.cs:1301-1304`、`Se_Elm_Cold.cs:75`（×(1+amp)） | 特性・基礎・契約（光・闇） | 100 | A |
| 16 | AttackRangePct | `attackRangePercentage` | `AttackTrigger.cs:24`（×(1+p×0.01)） | **特性（武器・手のみ）と基礎9個。星図には無い** | 30 | A |
| 17 | FourthAttackShift | `everyFourAttackStartIndexFlat`（`HostAuthority.cs:1700`） | `AttackTrigger.cs:91,130,142`（`Mathf.Min(...,3)`） | **星図だけ**（装備からは出ない） | 1 | A（装備経路なし） |
| 18 | EssenceSlotIdentity | `ApplyGemSlots` → `HeroSkill.SetMaxGemCount` `HostAuthority.GemSlots.cs:31,53` | `HeroSkill.cs:1180` | **星図だけ** | 1 | A（装備経路なし）。能力補正にはならず、`ToStatBonus` は何もしない（意図どおり） |
| 19 | EssenceSlotMovement | 同上 | 同上 | **星図だけ** | 1 | A（同上） |
| 20 | HealPower | `hero.dealtHealProcessor.Add` `HostAuthority.cs:1297-1301` | `Actor.cs:2392-2400`（祖先のプロセッサも歩く） | 特性（鎧・装飾・頭）・基礎6個・セット2点・契約・星 | 60 | A。`SupportStats.AmplifyHeal` が再度上限で切る |
| 21 | ShieldPower | `hero.dealtShieldProcessor.Add` `HostAuthority.cs:1299-1302` | `Actor.cs:2430-2432` | 同上 | 60 | A |
| 22 | SummonPower | 召喚獣の `dealtDamageProcessor` `HostAuthority.cs:1353,1361` | 召喚獣が出すダメージに掛かる | **基礎5個・セット2点・契約・星。特性には出ない** | 80 | A（装備では基礎かセットだけ） |
| 23 | SacrificeReduction | `hero.takenDamageProcessor` 内 `ReduceSacrifice` `HostAuthority.cs:1236-1239` | 発生源は `Se_HealthCost`／`Ai_Q_GoldenBurst`／`Ai_Q_Reduction_Spawner`（`IsHealthSacrifice`） | **セット2点と星のみ。特性・基礎には出ない** | 40 | B。対象の発生源3種だけ。他のHP消費は軽減されない（説明は「HPを捧げる技」全般）。実機未確認 |
| 24 | AttackFlat | `attackDamageFlat` | `EntityStatus.cs:1279`（加算の後に%） | 特性（全枠）・基礎20個・セット2点・契約 | 150 | A。Lv・強化で伸びる（`ScalesWithItemLevel`） |
| 25 | PowerFlat | `abilityPowerFlat` | `EntityStatus.cs:1281` | 同上 | 150 | A |

`ToStatBonus` の `switch` は26値すべてを列挙している（`HostAuthority.cs:1652-1690`）。`default` は無く、将来 `Stat` を足して `case` を忘れると**黙って無効**になる（コンパイルは通る）。この監査時点では抜けなし。

### 能力値の注記

- 装備から出ない値: FourthAttackShift・EssenceSlot（星図専用）。SacrificeReduction は装備ではセット2点のみ（どのセットかは `probe_stats.ps1`）。
- 固定値の攻撃力・魔力が装備で伸びるのは `ScalesWithItemLevel` の7種（AttackFlat・PowerFlat・MaxHealthFlat・Armor・HealthRegen・Haste・Tenacity）だけ。
  %の値はLvで伸びない（v1.28の方針どおり。`Relic.cs:90` と `HostGearValidation.cs:88-91` の上限計算が同じ判定を使うので一致）。
- 契約の「代償」は `Penalties`（能力値の減少）が**どの契約にも定義されていない**（`Pact.cs` の `Penalties =` は既定の空のみ）。代償は本体の呪いだけ。

---

## 3. 基礎能力・特性・セット・倍率・枠・連携

### 3.1 基礎能力（implicit）— A

- 土台360個（6枠×60）。基礎能力は20種の `Stat`（AttackSpeedPct 22／CritChance 17／PowerFlat 22／CritDamage 17／MaxHealthPct 15／AttackFlat 20／Armor 31／MoveSpeed 12／Haste 28／MaxHealthFlat 17／HealthRegen 18／Tenacity 16／AttackRange 9／Light 26／Fire 25／Cold 25／Dark 24／HealPower 6／ShieldPower 5／SummonPower 5）。
- 実効値 `Relic.Implicit`（`Relic.cs:87`）= 基礎値 × Lv倍率（固定値のみ）× 強化倍率。`EffectiveStats()` の先頭で `Build` に入る（`Build.cs:98`）。
- ホスト検証: 基礎能力は保存されず、再構築で毎回 `Base` から計算する。送られた値は使わない。

### 3.2 特性（affix）— A

- 枠ごとのプール（`Content.cs:4265-`）。Common 1行／Uncommon 2行／Rare以上 3行（`AffixCount`）。強化の節目で最大 +4行（+3・+5（固有効果を持つ遺物のみ）・+10・+15）。
- レア度倍率 100/100/110/120/130%（`RarityValuePct`）、強化の倍率 +6%/段（+5まで）→+4%/段（+20で190%）。覚醒は特性だけ 100/110/120/130%（`AwakenAffixPctAt`）。切り捨て。
- 攻撃力%・魔力%の特性はエピック以上のみ（`AffixDef.MinRarity`）。
- ホスト検証（`HostGearValidation.cs:76-92`）: 基礎と同じ能力値・重複・プールに無い能力値・レア度不足は**拒否**。値は「元の最大値×レア度×Lv」で**切り詰め**（強化・覚醒は二重に掛けない）。

### 3.3 セット効果 — A（2種類ある）

| 種類 | どう届くか | 場所 |
|---|---|---|
| 系統セット（破壊・生命・想像の装着数） | 2/3/4/6個で `Content.SetBonus` の能力値を合計へ足す。表示用の `Build.Lines` は通信しない（ホストが再計算） | `Build.cs:119-121`、`Content.cs:5049` |
| 固有品セット（47+1組） | 同じ `SetId` の装着数。2個で `TwoPiece`（能力値）、3個で `ThreePiece`（固有効果）を足す。`Build.Sets` は表示用 | `Build.cs:122-137` |

- セット部位は固有効果を持たない固有品（`UniqueDef` の4引数版）。固有効果の「3点」は `PowerCap` で切られる。
- 3点の効果は `ThreePiece`（固有効果）。**12種のPowerはどのセットの3点にも出ない**（Retaliation・Fetters・PreyPride・OverflowingLife・Devotion・Wildfire・StillWater・SpendersWard・PerfectRead・UmbralHeritage・Apothecary・ShadowStep）。ShadowStep以外は固有品とプールには出るので、バグではなく偏り。

### 3.4 倍率（強化・限界突破・覚醒）— A、ただし上限で空振り（B）

| 要素 | 実装 | 注 |
|---|---|---|
| 強化 +1〜+5 | 特性+6%/段、固有効果+5%/段 | `Content.cs:4953,4960` |
| 限界突破（レア1・エピック2・伝説3回） | 強化上限を+5ずつ広げる。+6〜+20は 特性+4%/段、固有効果+3%/段 | `Content.cs:4988` |
| +20（伝説のみ） | 1つ目の固有効果が×1.2（`PowerCap` 止まり）。`Relic.Powers` の値そのものを書き換える | `Rules.cs:BoostMilestonePower` |
| 覚醒Ⅰ〜Ⅲ（伝説のみ） | 固有効果と連携 ×1.25/1.5/1.8、特性 ×1.1/1.2/1.3。しきい値 2000/6000/15000 | `Content.cs:323-340`、`Relic.cs:98-120` |
| 条件つき攻撃力・魔力の固有効果 | 覚醒前の値の合計を120%に制限（共通の予算）。ホストは `u` 節を再計算して使う | `NewPowersV129.IsConditionalAttribute`、`Build.cs:100-106,232-234` |

実測（`probe_caps.ps1`）。固有品の固有効果2092行について、**遺物1個の単独値**がすでに合計上限に達している行数:

| 強化・覚醒の状態 | 上限到達 | 割合 |
|---|---|---|
| +0 覚醒なし | 0 | 0% |
| +5 覚醒なし | 2 | 0.1% |
| +5 覚醒Ⅱ | 216 | 10% |
| +5 覚醒Ⅲ | 778 | 37% |
| +10 覚醒Ⅲ | 1331 | 64% |
| +15 覚醒Ⅲ | 1826 | 87% |
| +20 覚醒なし | 107 | 5% |
| +20 覚醒Ⅲ | 1971 | **94%** |

つまり覚醒「固有効果が1.8倍」と強化の伸びは、**表示どおりには効かない行が大半**（`Build` は合計を `PowerCap` で切る。`Build.cs:229`）。
画面の説明と数値が合わない（C/D ではなく「B・説明のずれ」として 6 節に載せる）。

連携の上限（`Links.EquippedCap`、`Links.cs:238`）: 連携を持つ固有品357個のうち **39個**（記憶加速 `MemoryHaste`）は覚醒Ⅲの値が上限90を超え、切られる。
基礎値そのものが上限を超える固有品は無い。

### 3.5 エッセンス枠 — A（星図専用）

- 供給は星図の頂点の2種だけ（`EssenceSlotIdentity`／`EssenceSlotMovement`、各上限1）。装備からは来ない。
- 購読: 常駐ではなく `Apply` のたびに `ApplyGemSlots`（`HostAuthority.cs:1329`）。APIは `HeroSkill.GetMaxGemCount`／`SetMaxGemCount`（`HostAuthority.GemSlots.cs:25,53`。本体は `HeroSkill.cs:1166,1180`）。
- 解除（`Unhook`）で自分が足した分だけ戻す（`RestoreGemSlots`、`EssenceSlots.TargetMax`）。範囲外になったエッセンスは足元へ落とす（`UnequipGem`）。

### 3.6 固有品の連携（Link）— A（条件つきで効く）

- `Relic.Link`（`Relic.cs`）は固有品定義から引く（遺物に保存しない）。`Build.cs:109-117` で覚醒倍率→`EquippedCap`→`Validate` を通して `Build.Links` へ。**能力値には足さない**。
- 連携の充足判定（記憶・エッセンス・旅人の装着）と適用（`LinkKind.MemoryDamage` など）はホストの `rt.SatisfiedLinks`。例: `LinkKind.MemoryDamage` は `HostAuthority.cs:1263-1268`。
- 全 `LinkKind` の配線は連携のサブ監査の担当（本書は転記していない）。

---

## 4. ホスト検証が正当な装備を落とすか

### 4.1 検証の仕様（読んだ結果）

`HostBuildValidation.TryAccept`（`HostBuildValidation.cs:79`）の流れ:
1. 受け取った文字列を `Build.Decode`（`s`/`p` は上限で丸めるだけで落とさない）。
2. 入力部（`D H M K T R P Y`）を `ReadInputs` で読み、遺物ごとに `HostGearValidation.TryValidate`。**1個でも失敗＝`FormatException`＝全体拒否**（`HostBuildValidation.cs:233`、`:110`）。
3. 星図の割り当てを検証（つながり・点数・選択・刻印）。
4. ホストが `Build.Compute` を再実行し、送られた効果の節（g,l,c,f,n,j,v）と一致しなければ拒否（`derived-effects`）。
5. 合格したら**クライアントの合計値は捨て**、ホストの計算結果を使う（`validated = expected`）。

拒否の扱い（`HostAuthority.BuildValidation.cs:69-72`）: `RejectBuildOnce` はプレイヤーごとに**最初の1回だけ**ログに出し、理由は握りつぶす。クライアントへは何も返さない。
前に受理されたビルドがあればそれが残る。無ければ、そのプレイヤーの旅人は補正なし（星図も装備も効かない）。

### 4.2 実測（総当たり）

| 試験 | 件数 | 拒否 | 減額（実効値が変わる） | 不一致 |
|---|---|---|---|---|
| 固有品1190個 × Lv{1,40,60} × 限界突破0〜3 × 強化{0,3,5,10,15,20}（上限内）× 覚醒0〜3、節目は実際の `GrantEnhanceMilestones` | 約34万 | 0 | 0 | — |
| 通常ドロップ（`Loot.RollRelic` 全レア度・全枠）＋強化・限界突破・覚醒 | 6万 | 0 | 0 | — |
| 出来事: 記憶の井戸／力の坩堝（第1固有効果の入れ替え）、影の交換、鍛冶の祠、錬成の祭壇、失われた霊廟（強化0へ）、道標の強制強化（節目なし） | 3万 | 0 | 0 | — |
| ビルド往復（`Build.Compute`→`Encode`→`TryAccept`）。9人の旅人・6枠・契約0〜4・今日の夢・潜行0〜5 | 3,000 | 0 | — | 0 |
| 星図込みの往復（`sim_stars.ps1`。実際に買った星・刻印・装備） | 45 | 0 | — | 0 |

注: `sim_gear.ps1` は `Loot` の乱数で作った遺物を使う。実際の保存データを読んだものではない。

### 4.3 リスク（現行では起きないが、起こりうる）

| # | 内容 | 影響 | 根拠 |
|---|---|---|---|
| R1 | 遺物1個の拒否で**ビルド全体**が落ちる。通知なし・理由はログ1回のみ | 星図と他5枠も無効になる。プレイヤーは気づけない。ソロにも効く | `HostBuildValidation.cs:233,110`、`HostAuthority.BuildValidation.cs:69,111-114` |
| R2 | 固有品の固有効果は「順序・種類・個数が定義と一致」でないと拒否（値は上限で切り詰め）。固有品の定義の**固有効果を入れ替える／順序を変える／個数を変える**更新をすると、その版以前に落とした遺物は協力で全拒否。保存形式は据え置き（3→4はリセットしない） | v1.30.0〜HEAD の間に固有品の固有効果が変わった例は**無い**（`git diff v1.30.0 HEAD -- Content.cs` の削除行に `UniqueDef` なし）。将来の変更が危険 | `HostGearValidation.cs:56-58,106` |
| R3 | 値は拒否ではなく**切り詰め**（`ClampAmount`）。固有品の固有効果の値を**下げる**更新をすると、旧遺物はホストの切り詰めで弱くなる。クライアントの表示は旧値のままなので、協力でだけ「表示より弱い」になる（合計値 s/p は比較されない）。値を上げる更新では旧遺物は旧値のまま | 協力限定の黙った減額（現行では未発生） | `HostGearValidation.cs:139` |
| R4 | `Enhance`／`EnhanceMilestones` の整合は「履歴の上限」までしか見ない（失われた霊廟は強化だけ0へ戻す）。これは**緩め**なので正当な遺物は落ちない | 問題なし（確認のみ） | `HostGearValidation.cs:36-43` |
| R5 | 非伝説の覚醒を拒否（`AwakenLevel!=0`）。覚醒を付与するのは伝説のみ（`Rules.cs:176`）。旧版の覚醒フラグ（`awakened`）を持つ非伝説が保存に残っていると拒否になるが、`ReadRelic` は伝説かを見ずに通す（`ProfileCodec.cs:540`） | 旧データ限定。形式3以降では発生しない見込み（`Rules.cs:176` が伝説だけ） | `HostGearValidation.cs:32`、`ProfileCodec.cs:530-545` |
| R6 | `ReadRelic` は特性・固有効果の値を `StatCap`／`PowerCap` で切る。切られた値はホスト側でも合う（上限内）ので拒否しない | 問題なし | `ProfileCodec.cs:561,573` |
| R7 | `SendBuildIfNeeded` は `Encode` が例外を投げる（装着中のUidが保管庫に無い）と毎回失敗する。`Compute` は黙って飛ばすので食い違う | 通常の操作では装着中を分解できない（`loadoutLocked`）。実害は不明 | `HostBuildValidation.cs:Encode`、`Build.cs:94-96` |
| R8 | 星図: クライアントの `Compute` は「つながっていない星」を黙って無視するが、ホストは「つながっていない割り当てが1つでもあれば全体拒否」（`AllocationsConnected`）。正常な操作では返金が連鎖するので起きないが、移行や破損で孤立星が残ると全体が落ちる | 4.5 節の試験で確認 | `HostBuildValidation.cs:TryValidateAllocation`、`Build.cs:Unlocked` |

### 4.4 「正当な効果が検証で落ちるか」の個別回答

| 質問 | 結果 |
|---|---|
| 覚醒した・限界突破した遺物の値 | 落ちない。覚醒・強化は再構築側が同じ式で掛ける（遺物の保存値には含まれない）。+20の1.2倍は保存値に含まれるので、`cap` を `Relic.Scale(cap,120)` まで許している（`HostGearValidation.cs:118-130`） |
| セット効果 | 遺物から計算するので影響なし。セット部位の+5固有効果は `Min` 値まで許す（`HostGearValidation.cs:117`） |
| 今日の夢の固有効果ブースト | `Y:` を送り、ホストが `DailyDream.Get` で存在確認して同じ式で足す。落ちない（3,000ビルドで日替わりを含めて一致） |
| 契約の恩恵（Boons） | `P:` を送り、ホストが `Pacts.Get` で確認。重複は拒否だが、`Pacts.Offer` が重複を除外している。落ちない |
| 星図の能力値 | `T:`、`K:`、`M:`（撃破数）を送り、ホストが同じツリーで再計算。4.5 節 |
| 図鑑・工房の効果 | **`Build` には入らない**。図鑑は振れる星ポイントの上限（`MaxCodexBonus`）、工房は報酬・再抽選などクライアントの報酬側（`Workshop.cs`）。協力でもクライアントが自分の分を抽選する設計 |
| 端数の丸め | 特性は整数の `Relic.Scale`（四捨五入、ゼロでない値は最低±1）。ホストの上限は同じ関数で計算するので、丸めの差で落ちない（`Scale(max,pct)` は単調） |

### 4.5 星図込みの往復

`Rules.AddTalentRank`・`SetKeystone`・`Rules.Equip` で実際に買った星・刻印・装備（9人＋汎用ツリー、点数20〜304、購入4,459回）で45ビルドを往復: **受理45・拒否0・不一致0**。
標本は45件と少ない（遅いため）。星の割り当てを大量に試す場合は `sim_stars.ps1`（全600件）を使う。孤立星・移行後の旧データ（R8）は試していない。

---

## 5. 装備の固有効果（Power）が遺物のどこから出るか

`Power` の配線そのもの（購読・本体API）は Power のサブ監査の担当。ここでは「**手に入るか**」だけを実測した（`probe_content.ps1`）。

- ドロップ可能な固有効果は90種（`Content.IsPowerDroppable`: `None` と `ShadowStep` 以外）。**全90種が枠別プールのどこかに入っており**、固有品にも出る。
- レア度別: コモン・アンコモンは固有効果なし（+5の節目で1つ、最小値で得る）。レアは下半分の値で1つ。エピックは範囲どおり1つ＋下半分1つ。伝説の固有品は定義の2つ。条件つき攻撃力・魔力の固有効果は**エピック以上のみ**（`PowerAllowedForRarity`）。
- 1枠のプールしか持たない固有効果: Convergence（装飾）・SpendersWard（装飾）・PerfectRead（足）。
- **`ShadowStep`（瞬歩の刃）はドロップせず、Husk の刻印（`HeroSigils.cs:111`）と今日の夢のブーストだけ**。装備からは出ない（仕様どおり）。
- 3点セットの効果に出ない固有効果は 3.3 節の12種。

---

## 6. 問題の順位づけ（装備まわり・実測ベース）

| 順 | 区分 | 内容 | 場所 |
|---|---|---|---|
| 1 | B・説明のずれ | 覚醒・強化の効果は、固有効果の94%（+20覚醒Ⅲ）が1個で合計上限に達し、**伸びが見えない**。説明は「×1.8」。重ねるほど空振り | `Build.cs:229`、`Content.cs:4639-` |
| 2 | B・説明のずれ | 連携の記憶加速39個は覚醒Ⅲで上限90に切られる | `Links.cs:238-242` |
| 3 | リスク | 検証は遺物1個の不正でビルド全体を捨て、通知もない。ソロにも効く | `HostBuildValidation.cs:233,110`、`HostAuthority.BuildValidation.cs:111` |
| 4 | リスク | 固有品の固有効果の入れ替え・個数変更は、旧保存の遺物を協力で全拒否にする（保存形式を上げずに固有品を変えない運用が要る） | `HostGearValidation.cs:56-58,106` |
| 5 | B | `SacrificeReduction` は3種の発生源だけが対象。説明は「HPを捧げる技」全般。装備ではセット2点のみ | `HostAuthority.cs:IsHealthSacrifice`、`Content.cs:5168` |
| 6 | 設計の偏り | `SummonPower`は特性に出ない。`FourthAttackShift`・`EssenceSlot*`・`SacrificeReduction`（特性）は装備に出ない（星図専用）。説明には書かれていない | `probe_stats.ps1` |
| 7 | 保守 | `ToStatBonus` の `switch` に `default` が無く、`Stat` 追加時の入れ忘れが黙って無効になる | `HostAuthority.cs:1652` |
| 8 | 保守 | 拒否の理由をクライアントへ返さない。`HostConfirmed` は受理の返事でしか立たない。画面で「ホスト未確認」が続く以外の手がかりが無い | `ClientSession.cs:962`、`DreamforgeUi.cs:358,416` |
| 9 | 表示 | `armor.frost_coat`（行動妨害耐性）は強化+20・Lv60で62、上限60 | `probe_caps.ps1` |

---

## 7. 他の効果型（伝聞。本書では未確認）

調整役から受け取った要点。**私は表を持っていない**ので、数・分類は鵜呑みにしないこと。
- Power: 91値のうち90が配線済み、`StillWater`（明鏡止水）が壊れていた。
- 修正済み（コミット `dd3324d`）: 明鏡止水、終幕・過負荷・逆襲の説明とのずれ、星への供物への深さ倍率、契約の呪いの再付与（再起動後）、夢の深さの部屋を最初のゾーンにも、説明文のずれ5件（合わせ技・鎧の変種・倒れた時の爆発・棘・棘皮）。
- 仕掛け・連携・合わせ技、反応・契約・出来事・道標、敵側・星図機構の表は、別の監査の成果物を参照のこと。

### 確認できたこと（私が見た範囲）
- 契約: 能力値の恩恵（Boons）は `Build` に入りホスト再計算で届く（本書 2 節）。ドロップ率・幸運・欠片・経験値・残響なしの効果は `Rules.KillModifiers`（`Rules.cs:135`）で**クライアントの報酬抽選**に効く。ホストは関与しない。
- 契約の代償（本体の呪い）は `OnCurse`（`HostAuthority.cs`）がホストで `Hero.CreateStatusEffect` する。再付与は `dd3324d` で入った。

---

## 付録 A. 再現

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Temp\audit\sim_gear.ps1     # 遺物総当たり
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Temp\audit\sim_build.ps1     # ビルド往復
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Temp\audit\sim_stars.ps1     # 星図込み（遅い）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Temp\audit\probe_caps.ps1    # 上限の空振り
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Temp\audit\probe_stats.ps1   # 能力値の入手元
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Temp\audit\probe_content.ps1 # 固有効果の入手元
```
スクリプトはどれも `C:\Temp\audit\core\SodRpg.Core.dll`（または `src/SodRpg.Core/bin/Debug/...`）を読み込むだけで、リポジトリを変更しない。
