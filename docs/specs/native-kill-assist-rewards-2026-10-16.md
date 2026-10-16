# 撃破成長と一時効果のアシスト規則（2026-10-16）

対象: `feat/hero-run-growth-1016`。マルチで同じ敵の永続的な成長を人数分増やさず、戦闘中の一時的な撃破効果は既存の5秒アシストにも与える。シングルの本体処理・MOD処理は変更しない。担当制は別ブランチのため、この変更では実際の撃破Actorの親経路から旅人を求める。

## A/Bの最終分類

Aは撃破で遠征中ずっと積み上がる成長・追加資源。MODは5秒アシストへ追加しない。Bは一時的な戦闘効果で、5秒アシストへ共有する。分類不明の効果をBへ自動登録しない。本体の30秒アシストで動く効果と、通常撃破ゴールドは別枠として本体に任せる。

| 区分 | 本体効果 | 最終動作 |
|---|---|---|
| A | `Gem_E_Predation` | 本体どおり（本体の KillTracker の猶予内アシストを含む。MOD は追加も制限もしない）。通常敵で成長しない本体条件は維持 |
| A | `Gem_L_DivineFaith` | 本体どおり（同上）。上限付きスタック・最大HP・ダメージ／回復増幅の加算は本体の猶予条件に従う |
| A | `Gem_L_Culinary` | 本体どおり（同上）。料理素材の抽選・生成は本体条件に従う |
| A | `Gem_R_Wealth` | 本体どおり（同上）。遅延追加ゴールドは本体条件に従い、生成されたゴールドの本体分配も変更しない |
| B | `Gem_E_Metal` | 装着記憶のCD回復・演出を5秒アシストにも1回 |
| B | `SkillTrigger.ActorEventOnKill` | 究極記憶の撃破チャージを5秒アシストにも1回。小型0.12750001、通常0.63750005、ミニボス12.75、ボス42.5の本体値を使用 |
| B | `Se_Star_Aurena_F_CR_NoHealCooldownReduction` | 連鎖反応の対応記憶の割合CD回復を5秒アシストにも1回 |
| B | `Se_Star_Lacerta_F_PS_CooldownReset` | 精密射撃の対応記憶のCDリセットを5秒アシストにも1回 |
| B | `Se_Star_Cetus_F_BA_CdReductionOnKill` | 対応記憶のCD回復を5秒アシストにも1回 |

### 本体DLLから判明した依頼前提との差

Aの4種は、本体でも単純なラストヒット限定ではない。`Dew.Contents.dll` の各型と `Dew.Core.KillTracker.ReportKill` を確認したところ、`TrackKills` は猶予時間内に命中した敵の死亡でもコールバックを実行し、4コールバック自身にも撃破者条件がない。

- 捕食: 所有者の `TrackKills(killGraceTime, OnKill)`。
- 信仰: 装着記憶の `TrackKills(gracePeriod, IncreaseAmp)`。
- 料理: 装着記憶の `TrackKills(killGraceTime, OnKill)`（既定3秒）。
- 富: 装着記憶の `TrackKills(gracePeriod, Callback)`（既定1.5秒）。

このため A は本体どおり（本体の KillTracker の猶予内アシストを含む。MOD は追加も制限もしない）に戻した。5秒アシストからの追加発火もしないので、発火条件・回数とも完全に本体どおりである。以前入れていた「マルチ時に限り `EventInfoKill.actor` の親経路上の旅人とエッセンス所有者が一致する場合だけ本体処理を続行する」Prefix は、本体より厳しくする変更に当たるため除去した（MOD の独自発火がなくても制限だけは残ってしまうため）。

富の `DropGold` は個人宛ではなく本体の全員分配である。どの所有者が同じ撃破から追加ゴールドを生成しても、生成後のゴールドを撃破者へ独占支給する変更はしない。

## 本体の30秒アシストは触らない

`Hero` は本体の `TrackKills(30f, ...)` からRPCを送り、`ClientHeroEvent_OnKillOrAssist` を呼ぶ。この経路、イベントの購読一覧、および購読する効果に対し、MODは捕捉・保留・抑止・追加発火をしない。5秒を過ぎても本体の30秒条件を満たす場合は本体どおり発動する。永続／一時の区分によってこのイベントを制限しない。

前回実装の `AddEvent` 登録、購読一覧の収集、受信側Prefix、5秒対象外の本体報酬抑止は除去した。次の効果はいずれも本体のまま（5秒共有リストには含めない）。

| 購読する装備・バフ | 本体効果 |
|---|---|
| `Gem_E_OurStory_Unfinished` | 撃破蓄積による品質成長・変化 |
| `Gem_U_GuidingCompass_NotCharged` | ボス・ミニボスの品質成長用の弾 |
| `Se_E_LizardlyBlessing` | バフ中の個人ゴールド |
| `Se_E_UmbralEdge` | 条件付きのバフ時間延長 |
| `Se_D_TheKillingFlow` | ボス撃破時の回復 |

| 購読する星図 | 本体効果 |
|---|---|
| `Se_Star_L_HealOnKill` | 最大HP比の回復 |
| `Se_Star_I_DreamDustOnKillChance` | 個人の夢の粉抽選 |
| `Se_Star_I_FirstBossGemDrop` | 初回ボスのエッセンス、自身の解除 |
| `Se_Star_I_GemOnMiniBossKill` | ミニボスの個人エッセンス抽選 |
| `Se_Star_Bismuth_I_KnowledgeBook` | ボスの個人能力報酬書 |
| `Se_Star_Cetus_F_FR_ChangeToUltHitReset` | 撃破ごとの持続時間増加 |
| `Se_Star_Cetus_I_GetDreamDustOnBossKill` | ボスの個人夢の粉 |
| `Se_Star_Husk_F_AS_NoShockwaveFasterAttack` | ボスごとの恒久的ヘイスト加算 |
| `Se_Star_Husk_I_BossKillPlatinumCoin` | 個人プラチナコイン抽選 |
| `Se_Star_Husk_L_DodgeCooldownOnKillOrAttack` | 回避CD回復 |
| `Se_Star_Husk_L_DodgeCooldownOnKillWithEmptyCharges` | 残チャージなしの場合の回避CD回復 |
| `Se_Star_Lacerta_I_BossBounty` | ボスの個人ゴールド |
| `Se_Star_Nachia_I_ForestKillDreamDust` | 場所条件付き個人夢の粉 |
| `Se_Star_Nachia_I_SummonKillGold` | 召喚撃破の個人ゴールド抽選 |
| `Se_Star_Yubar_I_StarCookie` | 敵種別のクッキー抽選 |
| `Star_Global_KillDreamDustBonus` | 旧星図の個人夢の粉 |

## Bの判定・配送・重複防止

- 敵死亡前の5秒以内の与ダメージ、敵への制御・弱体、撃破者への有効回復・障壁を使う。撃破者本人を除き、生存している味方だけを対象にする。
- 敵の `EntityEvent_OnDeath` に一致する `SafeAction<EventInfoKill>.Invoke` のPrefixで既存の `CaptureKillAssists` を呼び、本体の死亡リスナーより先に対象者・装備を凍結する。この汎用フックは旅人の `ClientHeroEvent_OnKillOrAssist` では何もしない。撃破時回復・障壁で新しいアシストを作らない。
- Bの本体コールバックは元の場で元の撃破情報を使って実行し、受領済みにする。保留しない。本体独自の猶予時間を満たす5秒対象外のBも抑止しない。例えば金属の本体追跡は10秒。
- `Actor.InvokeOnKill` の既存Postfixで、5秒対象者がまだ受けていないBだけを発火する。特殊3種は本体の追跡があれば元のクロージャ・ヒット表によるコールバックを先に実行し、追跡がない支援・制御等の場合は対応星図の現在値と本体の公開CD APIを使う。
- 受取者・効果・敵の生成世代ごとの既存受領記録を使用する。装備・発生源の `persistentNetId` を確認し、プール再利用された別世代へ共有しない。敵のプールイベント解放または無効化まで記録を保持する。
- ホストかつ複数人だけ有効。毎フレーム処理・割り当てを追加しない。既存の同期処理を使用し、Protocol・保存形式を変更しない。
- 導入失敗はこの拡張自身のパッチだけを巻き戻す。例外時は拡張だけを停止し、本体の撃破者処理と他のMOD機能を継続する。

## MOD側の撃破依存効果

| 区分 | 経路・効果 | 最終動作 |
|---|---|---|
| A | `RunGrowthTrigger.EnemyKill`（通常撃破数） | `ApplyKillAssist` の `OnRunGrowthKill` 呼出を除去。撃破者の `OnMemoryKill` は維持 |
| A | `RunGrowthTrigger.CritBasicAttackKill`（会心通常攻撃撃破数） | アシスト者の直近会心を使った `GainRunGrowth` 呼出と、そのためだけの会心記録を除去。撃破者の `TrackCritBasicKill` は維持 |
| A | 圧力配当・追加遠征資源 | 既存の `notification.IsAssist` 除外を維持。追加共有しない |
| B | 装備の `Momentum`、`Tailwind`、`SoulSiphon`、`Shatter` | 時限スタック・時限バフ・撃破時回復・即時の範囲効果として5秒共有を維持 |
| B | `ReturningBlade`、`PackFeast`、`RelayHand`、`ElementalHarvest`、`SpilloverStrike`、`UmbralHeritage` | 記憶CD回復・時限障壁・味方CD回復・即時の派生効果として5秒共有を維持 |
| B | 記憶の仕掛け・旧ペア・型付き連携、星の撃破通知 | CD／チャージ回復、時限バフ・障壁・準備、回復、即時ダメージ・制御等の既存共有を維持。再帰防止・発火回数制限・圧力配当除外を維持 |

現在の `ApplyKillPowers`、`OnNewPowerKill`、`QueueGimmicks`、`PublishMemoryActivation` の購読先を点検した範囲では、上記2つのRunGrowth呼出以外に「撃破で遠征中ずっと能力値を積み上げる」追加処理はない。`Momentum` や `Crescendo` 等のスタックには期限がありB。通常攻撃命中・召喚命中・記憶命中・回復／障壁支援など、撃破共有によらない既存の成長契機は維持する。

## ゴールドは本体どおり

本体は通常撃破ゴールドを元から全員へ配る。`PickupManager.HandleDeathDrop` による敵・人数・ゾーン・波数等の金額算出、宝珠への分割／即時支給、`Pickup_BaseGoldOrb.GrantGold` による人数除算、各人の `killGoldMultiplier`／`monsterKillGoldMultiplier` と各人の確率丸めをそのまま使う。

`NativeKillRewards.Gold.cs` の全フックを除去した。同ファイルは削除禁止に従って説明コメントだけを残す。`InstallGold`、`ClearGold`、死亡時の受取者収集、iterator・宝珠の弱参照記録、`EarnGold` の引数置換・支給保留・再支給は存在しない。個人倍率で従来のアシスト者の額が撃破者を上回る場合も、本体どおりその額を受け取る。通常額を撃破者に合わせて減らさない。

`DreamforgeMod.StartNativeKillRewards` の登録はB共有のために維持する。Aの4種へのパッチは存在しない。ゴールド専用の起動登録はない。

## この変更での検証

制限除去の再検証（2026-10-16、撃破者限定 Prefix 除去後）:

- `python3 tools/balance/gen_cs.py --check`: 成功。
- 指定の `dotnet build src/SodRpg.Mod -c Release ...`: 成功、エラー0・既存警告6。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py`: 3316件成功・失敗0。初回実行だけタイミング系の `PerformanceTests` が閾値超過で失敗し、再実行で合格（本変更はパッチを外す方向のため遅くはない）。
- 撃破者限定 Prefix を直接検証する既存テストは存在しなかった（追加もしない）。
- テスト境界のビルド修正: main 統合（91c6b182）で `DreamforgeUi` に追加された `_linkCoefficients` を `Issue73.Native.Tests/NativePersistence.targets` の抽出フィールドへ、`DreamforgeConfig`／`HostAuthority` の近接協力メンバーを `SodRpg.Mod.Startup.Tests/StartupGameApi.cs` のスタブへ、それぞれ main どおりに追加。本体コードは未変更。

実行済み（2026-10-16）:

- `python3 tools/balance/gen_cs.py --check`: 成功（入力・生成ファイル変更なし）。
- 指定の `dotnet build src/SodRpg.Mod -c Release -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/x`: 成功、エラー0・既存警告6。
- `KillAssistSupportNativeTests`: 変更前11件成功。成長共有除去後は旧仕様を期待する4件のみ失敗したため、既存の4期待値を1から0へ更新（新規テスト・テスト手順追加なし）。
- Core関連フィルター: 183件成功・失敗0・スキップ0。対象は `KillAssistSupportNativeTests`、`RunGrowthBalanceTests`、`PowerRuntimeTests`、`NewPowersV129Tests`、`PairComboNativeTests`、`AuthoredMechanismNativeTests`、`BridgeSuccessEffectsTests`、`DirectedRechargeTests`、`MemoryPrimedTests`、`RelayWindowTests`、`PressureDividendTests`。
- テストは `DOTNET_ROLL_FORWARD=Major` を付けて実行。最初の通常実行は.NET 8ランタイム不在（インストール済みは.NET 10.0.11）のため中断した。ランタイムのインストール・環境変更は行っていない。
- 本体DLLと逆コンパイル資料の確認: Aの4種の `TrackKills` とコールバック、Bの5種の呼出条件、本体30秒アシストの購読・配送経路。
- `git diff --check`: 成功。

既存境界テストはMODの5秒判定・成長・戦闘効果を駆動する。今回の本体効果フック自身をUnityで実行する検証ではない。CHANGELOG変更なし。

未実行:

- 実際の2人マルチで、捕食等Aの増加が撃破者だけになること、Bが5秒アシストへ1回発火すること、5秒超30秒以内の本体アシスト効果が発火すること、個人倍率の異なるゴールド額を維持することの再現。
- 実ゲームのHarmony導入、Mirrorの遅延配送、宝珠回収、特殊CDクロージャ・敵／装備プール再利用、シングルのゲーム内確認。
- 理由: この環境のゲームデータはManaged DLL・逆コンパイル資料のみで、起動可能なゲーム実行ファイル／Unityランタイムがない。上記ゲーム内挙動は未確認。
- 全テスト・低速スイート: 指示に従い未実行。変更関連のフィルターのみ実行した。
