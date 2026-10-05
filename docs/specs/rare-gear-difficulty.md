# 高レア装備の入手・合成難易度（2026-10-05）

## 方針・対象

- 伝説（固有品・6部位セット）とエピック帯の供給を、標準的な遠征で旧値のおおむね1/2〜1/3へ。
- 銘品は独立したレアリティではなくアンコモン／レア／エピック。低レア銘品・小セットの抽選補助は維持。
- 新機構なし。保存形式・Protocol番号・刻印・星図UI・ボス限定セットの定義は変更しない。
- 2026-10-05の追変更（balance/remove-legendary-pity）：ボスの救済（天井）を本仕様の113体目への緩和からさらに完全撤廃。下表の「新」は天井なしの最終値に更新した。

## ドロップ：現状 → 新

段階は Common / Uncommon / Rare / Epic / Legendary (`Ids.cs:15-21`)。
共通抽選は重み×`(1+0.6×luck)^rarity`を許可された段階で正規化 (`Loot.cs:62-81`)。
基礎重みは **1200/540/200/30/2 → 3000/1350/500/30/2**。低3段階の比率は同じ、上2段階の相対重みは40%。
以下は潜行0・追加luckなし。C/U/R/E/Lの順、装備が落ちたときの条件付き確率（%）。

| 抽選 | 現状 C/U/R/E/L | 新 C/U/R/E/L |
| --- | --- | --- |
| 通常・小型敵 | 60.914/27.411/10.152/1.523/0 | 61.475/27.664/10.246/0.615/0 |
| エリート | 50.280/30.772/15.500/3.162/0.287 | 51.343/31.422/15.827/1.291/0.117 |
| ボス主報酬（旧はpity=0込み） | 0/52.191/33.248/13.064/1.498 | 0/58.469/37.247/3.844/0.441 |
| ボス追加報酬 | 0/54.938/34.997/9.029/1.035 | 0/58.469/37.247/3.844/0.441 |
| 商人、Uncommon以上 | 0/57.143/33.863/8.127/0.867 | 0/60.403/35.794/3.436/0.367 |
| 基本製作、Uncommon以上 | 0/63.477/30.563/5.960/0 | 0/65.831/31.697/2.472/0 |
| 上等製作、Rare以上 | 0/0/80.645/19.355/0 | 0/0/91.241/8.759/0 |

| 項目 | 現状 | 新 |
| --- | --- | --- |
| 装備ドロップ率：小型／通常／エリート／ボス | 0.8%/2.7%/45%/100% | 維持 |
| 潜行heat 1段の補正（heat上限5） | ドロップ×(1+0.35h)、luck+0.3h | 維持 |
| 敵格luck：通常／エリート／ボス | 0/0.6/1.2 | 維持 |
| ボスの救済確率 | min(1,0.05+0.035k) | なし（撤廃） |
| 救済の天井 | 未取得28回の次、29体目 | なし |
| ボス長期平均E/L個数（潜行0） | 0.25755/0.02953個/体 | 0.06150/0.00705個/体（約24%） |

根拠：`Loot.cs:24,384-400`。ドロップは100%で頭打ち、ボス主報酬の下限Uncommon。
天井（救済）は撤廃済みで、主報酬・追加報酬とも同じ通常抽選（luck1.2・下限Uncommon）。
上の長期平均は通常抽選＋60%追加報酬の計算値（実機計測ではない）。
通常敵の伝説は0%、新エリートの伝説は1撃破あたり約0.0527%、新ボスは長期平均約0.0071個/体。

## 全入手経路の補正・例外

- **悪夢・変種**：小型/通常→エリート、エリート→ボス報酬。昇格エリートも追加報酬の対象。
  悪夢率はheat>0で小型1%h、通常2%h、エリート20%+8%(h−1)、ボス0%。装備倍率は最大1.5倍。
  変種はheat≥2で6%+2%(h−2)、1部屋最大1体。これらは維持 (`Nightmare.cs:112-137,259-267; Variants.cs:353-365; Rules.cs:151-165`)。
- **深度等**：夢の深さluck+0.25d・1ゾーン+2d部屋、Limbo luck+0.2d/ドロップ+10%d、契約・今日の夢・イベント・道標の補正は維持。
  共通抽選の上位重み削減が適用される (`DreamDepth.cs:12-17; Rules.cs:94-95,134-147`)。
- **商人**：ゴールド60+15h（本体難易度倍率）、欠片払い50+10h、luck1+0.3hを維持。救済なし。
  1購入あたり伝説0.867%→0.367%（潜行0）。根拠：`Economy.cs:30; DreamEvents.cs:51; Rules.cs:868-870`。
- **宝箱**：通常の本体宝箱からMOD装備を生成する経路はない。深淵の宝箱はEpic確定のまま欠片30→75、潜行+1。
  双子の宝箱（道標）は戦利品を2複製。後者は元の抽選の減少を引き継ぐ (`Rules.cs:808-818; Waypoints.cs:84,178-179`)。
- **道標の確定効果は維持**：紫の蜃気楼は全遺物Epic以上、門番への貢ぎ物はボスEpic以上。
  封じられた宝庫は3倍、悪夢狩りは悪夢報酬2倍、明けない夜は全敵悪夢。追加の確率機構は導入しない。
  この保証によるEpic供給は共通重みだけでは減らない。伝説の抽選削減は適用される (`Waypoints.cs:74-87,153-219`)。
- **分布内の補助は維持**：伝説内のセット×4、所持セットの未所持部位×60、狙い系統×2。
  銘品の重みU/R/E=2/3/6、未図鑑×3、所持小セットの不足部位×4 (`Loot.cs:98-100,262-268; NamedItems.cs:104-112`)。
- **依頼・偉業は素材/経験だけ**、初期装備はUncommon。初回ボスEpic確定は設計上の案で現行の通常撃破にはない。
  回収・交換・確保は既存品の移動で、純増ではない (`Bounty.cs:86-88; Rules.cs:422-447; Onboarding.cs:138-171`)。

## 製作・合成・改善：現状 → 新

| 経路 | 現状 | 新 |
| --- | --- | --- |
| 基本/上等製作 | 欠片60/150、調律石0/2 | 費用維持、Epic率5.960/19.355%→2.472/8.759% |
| Common→Uncommon / Uncommon→Rare合成 | 同レア5個＋欠片10/20、100%成功 | 維持 |
| Rare→Epic合成 | Rare6個＋欠片30、100%成功 | Rare12個＋欠片60、100%成功 |
| Epic→Legendary合成 | Epic8個＋欠片150＋調律石2、100%成功 | Epic16個＋欠片300＋調律石4、100%成功 |
| 枠指定合成 | 欠片だけ1.5倍 | 維持（上位2経路45/225→90/450） |
| Epic以上の強化+1〜5 | 欠片20/35/60/90/130 | 40/70/120/180/260 |
| Epic以上の強化+6〜10 | 180/230/290/360/440 | 360/460/580/720/880 |
| Epic以上の強化+11〜15 | 270/345/435/540/660 | 540/690/870/1080/1320 |
| Legendary強化+16〜20 | 360/460/580/720/880 | 720/920/1160/1440/1760 |
| Epic以上の限界突破1/2/3回目 | 欠片200/400/800＋調律石5/10/20 | 欠片400/800/1600＋調律石10/20/40 |
| Epic以上の再調律1/2/3回目 | 調律石1/2/3、候補3つ | 調律石2/4/6、候補数維持 |
| Epic/Legendary洗い直しの初回 | 欠片240/300＋調律石8/10 | 欠片480/600＋調律石16/20 |
| Legendary覚醒Ⅰ/Ⅱ/Ⅲの累計 | 2000/6000/15000 | 5000/15000/37500（2.5倍） |
| 深淵の宝箱：Epic確定 | 未確保の欠片30 | 75 |
| 双子の鏡：Epic以上のコピー元 | 未確保の欠片30 | 60（低レア30維持、伝説コピーはEpic） |
| 遺物の賭け：Rare→Epic成功率 | 50% | 20%（低レアの昇格は50%維持） |
| 鍛冶の祠：Epic以上+1 | 未確保の欠片20 | 40（低レア20維持） |
| 記憶の井戸：Epic以上 | 調律石1 | 2（低レア1維持） |
| 影の交換：Epic以上 | 未確保の欠片25 | 50（低レア25維持） |

根拠：`Content.cs:362-384,5484-5521; Rules.cs:1256,1295-1308,1422,1464-1468,1533-1588,635-651,716,730,755-758,808-814`。
強化の成功率は維持：+1〜3は100%、以後は100−5×(現在強化−2)%、+20は15%。失敗で1段下がり費用消費（2026-10-05の追変更・issue #135。初版は+0に戻していた）。
限界突破は同枠・同レア以上1個を消費、Rare1回/Epic2回/Legendary3回。再調律/洗い直しはレア度を変えない。
洗い直しは既存の切り上げ(base×1.5^実施回数)・int.MaxValue飽和を維持。低レアの改善費と分解還元は変更しない。
泉の遺物1個→+1、焼き入れの祭壇の特性1個→+2、るつぼの固有効果1個消費は維持（新しい代償機構を作らない）。
合成結果は1段上・最高素材レベル、枠指定可。Legendary内/銘品内の分布は上記収集補助を共用。
各合成段階の材料は2倍。Rareから2段階でLegendaryを作る場合はRare48→192個（4倍）となり、ドロップ減少との重複でさらに時間が掛かる。

## 期待獲得ペース（BalanceSim）

旧新とも `dotnet run --project tools/BalanceSim -c Release -- --runs 20 --players 300 --seed 1 --policy POLICY`。
既存レポートの丸め済み平均を20遠征で均等平均した概算。各条件6000遠征、4ゾーン×5部屋、
部屋ごと小型10/通常8/エリート25%で1体、ゾーン末ボス1体、全滅15%。部屋/時間は実機測定ではない。

| 方針・指標 | 現状 | 新（天井なし再計測） | 新/現状 |
| --- | ---: | ---: | ---: |
| 毎回確保：Epic/遠征 | 約1.066 | 約0.279 | 約26% |
| 毎回確保：Legendary/遠征 | 約0.113 | 約0.027 | 約24% |
| 毎回確保：Legendary/時間（35分/遠征仮定） | 約0.193 | 約0.046 | 約24% |
| 深く潜行：Epic/遠征 | 約1.387 | 約0.451 | 約33% |
| 深く潜行：Legendary/遠征 | 約0.143 | 約0.046 | 約32% |
| 深く潜行：Legendary/時間（35分/遠征仮定） | 約0.244 | 約0.078 | 約32% |

毎回確保の低レアC/U/R供給は約4.400/5.042/2.731→4.461/5.533/3.040個/遠征。
初Rare以上の中央値1遠征は旧新とも同じ。天井撤廃後の初Epic以上の中央値は3遠征、初伝説は到達者ベースで中央値10遠征（20遠征時点の未到達約58%）。低レア供給は減っていない。
時間あたりは `個/遠征×60/T分`。35分は既存の標準遠征の設計時間 (`README.md:279`) を借りた比較用の仮定で、ゲームの所要時間や計算処理時間を測った値ではない。
この計測は撃破経路のみで、商人・イベント・道標・悪夢・製作/合成を使わない。全経路合算や特殊道標連打のペースは未検証。
合成は固定資源あたり個別経路の出力1/2、基本/上等製作のEpic出力は約41%/45%、賭けのRare→Epicは40%。
素材の供給とプレイ時間の実測がないため、合成の時間換算は断定しない。

## 互換・同期・検証

- 保存形式4のまま。旧セーブのpityカウンタ（epicPity）は読み込めるまま無視する（保存形式は変えない）。覚醒済み段階は維持し、読込時の既存処理が新しきい値まで力の最低値を補う (`ProfileCodec.cs:594-597,612-614`)。
- 通信の構造は変更なし、Protocol=14を据え置く (`NetMessages.cs:167-173`)。
- `ContentFingerprint.cs:35-53` は今回の抽選重み・天井の有無・費用を含まない。この数値差では内容指紋の交渉は旧新を区別しない。
- 指定Releaseビルド成功（警告5、エラー0）。BalanceSim旧新2方針とWikiGenの実行成功、生成ガイドの費用/覚醒値を確認。
- 実装時点では実機検証なし、テストの追加・変更なし。後述の既存テスト更新で自動検証を完了。特殊道標の保証維持と二段階合成の4倍化は、標準遠征とは別に判断が必要な調整点。

### 更新前の既存テスト結果・失敗一覧
指定 `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py`：3000成功、30失敗、2スキップ（3032件）。初回300秒打切り後、制限なしで完走。
以下は `SodRpg.Core.Tests.` 省略。パラメータ別失敗を併記し、旧期待→同じ入力の実測（費用欄は旧→新仕様）を示す。

| テスト名（括弧は失敗したパラメータ） | 旧期待 → 新 |
| --- | --- |
| `NamedItemsV132Tests.Twin_mirror_and_wager_reroll_named_relics_as_normal_ones` | 鏡の欠片30→60 |
| `PlayV111Tests.Forge_shrine_spends_shards_and_enhances_the_best_relic` | 祠の欠片20→40（所持25で不足） |
| `PlayV111Tests.Twin_mirror_copies_type_and_rarity_and_downgrades_legendaries` | 鏡の欠片30→60 |
| `RunChoiceProgressTests.Multiple_arrivals_preserve_each_intervening_zones_rewards_with_reversed_commits` | 固定seedの鞄個数2→3。同期処理は未変更、抽選系列の変化に伴う結果 [INFERENCE] |
| `GameRulesTests.Boss_epic_pity_guarantees_within_twenty_kills` | テスト上限20体→新仕様の天井113体（旧実装の天井自体は29体） |
| `LimitBreakV127Tests.Break_consumes_the_material_and_charges_both_currencies` | 欠片200/調律石5→400/10 |
| `AffixRerollOverflowTests.Epic_relic_with_35_rerolls_costs_the_exact_ceiling_not_a_wrapped_negative`、`A_successful_reroll_at_the_boundary_spends_exactly_the_cost`（同クラス） | 欠片/調律石349466306/11648877→698932611/23297754 |
| `AffixRerollOverflowTests.Cost_is_exact_nonnegative_and_nondecreasing_across_the_overflow_boundaries` (Epic, Legendary) | 初回欠片240→480、300→600（2件） |
| `EconomyV131Tests.Transmute_inputs_and_costs_per_rarity` | Rare素材6→12、Epic素材8→16；欠片30/150→60/300；調律石2→4 |
| `EconomyV131Tests.Legendary_transmute_costs_shards_and_tuning_and_fails_without_tuning` | 支払後欠片850→700（支出150→300） |
| `EconomyV131Tests.Pity_chance_values` | k=0: 0.05→0.025、k=3: 0.155→0.05125 |
| `RunContentV129Tests.Memory_well_replaces_one_first_power_only_with_an_unowned_power_from_its_slot` | 支払後調律石99→98 |
| `AffixRerollV131Tests.Cost_is_exact_ceiling_nonnegative_and_monotonic_up_to_the_int_cap`、`Cost_is_shards60_and_tuning2_times_rarity_plus_one_growing_by_ceiling_of_1_5`（同クラス、各Epic/Legendary） | 初回240/8→480/16、300/10→600/20（計4件） |
| `AffixRerollV131Tests.Missing_materials_are_refused_with_the_real_costs_in_both_languages` | 表示費用300/10→600/20 |
| `AffixRerollV131Tests.Epic_cost_after_35_rerolls_is_exact_and_not_negative` | 349466306/11648877→698932611/23297754 |
| `AwakeningV117Tests.Legacy_awakened_relics_become_level_two_and_keep_climbing`、`Save_round_trip_preserves_awakening_and_progress_continues` (6000,true)（同クラス） | 保存済み段階Ⅱの力6000→15000、段階は維持（計2件） |
| `AwakeningV117Tests.Each_level_has_its_own_multipliers` (level=1/2/3) | 2000/6000/15000→5000/15000/37500（3件、倍率は未変更） |
| `AwakeningV117Tests.Boss_kills_climb_three_awakening_levels_and_stop_at_the_last` | 100体撃破後の覚醒段階1→0 |
| `AwakeningV117Tests.Loading_clamps_awakening_points` (saved=15001/int.MaxValue) | 15000→15001/37500（2件） |
| `AwakeningV117Tests.Crossing_the_threshold_clamps_and_awaken_all_equipped_legendaries` | 旧しきい値直前からの覚醒数6→0 |
| `EnhancementRiskV131Tests.Seeded_failure_spends_shards_lowers_enhancement_and_preserves_earned_progress` | 支払後欠片99120→98240（+20の費用880→1760） |

### 既存テスト更新後の検証（2026-10-05）

- 利用者の明示許可に基づき、製品コードを変更せず既存テストを新仕様へ更新。新規テストメソッド・削除なし。覚醒上限の境界パラメータを2件追加。
- 既存シナリオで、pityのk=111/112境界と113体目の主報酬保証・リセット、Rare12個＋欠片60とEpic16個＋欠片300＋調律石4の合成・不足時の拒否・正確な消費、低レア装備と素材の供給を検証。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all`：**3032成功、0失敗、2スキップ（3034件）**。
- `DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet run --project tools/BalanceSim -c Release -- --runs 3 --players 30 --seed 1 --policy secure`：正常終了。初Rare以上の中央値1遠征、初Epic以上2遠征。小規模の動作確認であり、旧新の供給量比較を再計測したものではない。
- 製品コードの新たな疑義は確認されなかった。内容指紋が数値差を検出しない既知の同期上の制約は維持。実機・協力プレイの検証なし。

### 天井撤廃の追更新（2026-10-05、balance/remove-legendary-pity）

- `Loot.EpicPityChance` と `RollKill` の `ref epicPity` 引数・救済抽選・カウンター更新を削除 (`Loot.cs:384-400`)。`Rules.OnKill` はカウンターを読み書きしない。通常抽選の重み・レア度の下限・60%の追加報酬は不変で、通常敵・エリートの乱数系列は変わらない（ボス撃破のみ変化）。
- `Profile.EpicPity` と `ProfileCodec` の `epicPity` 読み書き（0〜1000にクランプ）は保存互換のため維持し、抽選にもプロフィール初期化の検査 (`ProfileSlots.cs`) にも使わない。保存形式・Protocol=14・内容指紋 (`ContentFingerprint.cs:35-53`) への影響なし。
- 表示：記録タブの救済カウンタ行を削除。README・付録B5・本仕様の天井の記述を「天井なし」へ更新。
- テスト：`Boss_epic_pity_guarantees_by_the_113th_kill_and_resets_on_the_main_reward` を「天井なし」（113体を超える連続未取得が起き、主報酬のエピック以上は約4.28%のまま）へ、`Pity_chance_values` を「保存済みカウンタの無視」（epicPity=1000でも確定しない）へ置換。`RollKill` 呼び出しの引数を更新。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python tools/test_changed.py --all`：**3013成功、0失敗、2スキップ（3015件）**。
- 指定Releaseビルド成功（警告5・エラー0）。BalanceSim（secure/greedy、各6000遠征）で「期待獲得ペース」表を再計測。実機・協力プレイの検証なし。
