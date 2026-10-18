# ゲーム本体 r1.4.1.2 対応

確認日: 2026-10-19。旧本体 r1.4.0.13 と新本体 r1.4.1.2（commit `ab05a05`）を比較し、Dreamforge RPG のパッチ先、名前参照、IL の前提、保存と協力プレイの接点を調べた。

## 入力と比較方法

本書では次の略記を使う。行番号は今回の入力に対する番号で、逆コンパイラーの版によって変わる。

| 略記 | 場所 |
|---|---|
| OLD | `/home/wang/archive/sod-gamedata-r1.4.0.13` |
| NEW | `/home/wang/dev/sod-gamedata` |
| Managed | 各ルートの `Shape of Dreams/Shape of Dreams_Data/Managed` |
| Core / UI | NEW の `decompiled/sod-decomp/Dew.Core` / `Dew.UI` |
| Contents | 作業ツリー内の `.ref/game-update-audit/new-contents`。新 DLL を ILSpy 11.1.0.9782 で再逆コンパイルした部分出力 |
| MOD | 作業ツリーの `src/SodRpg.Mod` |
| Audit | 作業ツリーの `.ref/game-update-audit`。git 管理対象外の解析資料 |

OLD / NEW は読み取りだけに使った。NEW の `decompiled/sod-contents`、`ExpLvl`、`RPGItems` は旧版のため、新本体の根拠には使っていない。旧 Mirror は指定場所に逆コンパイルがなく、DLL のメタデータと IL で比較した。本体のコード、DLL、解析生成物はコミットに含めない。

Mono.Cecil 0.11.6 で型、基底型、フィールド、メソッド署名、引数名、IL、ローカル変数、例外処理を比較した。IL の参照トークンをメンバー名に解決し、トークン番号の変化を処理の変化と区別した。MOD DLL の直接参照と Harmony 属性を抽出し、ソース内の `AccessTools`、手動 `Harmony.Patch`、動的 `TargetMethod(s)` も照合した。変わった本体メソッドの周辺は、呼び出し順序や親 Actor の変更も読んだ。

| DLL | 差分を検出した型数 |
|---|---:|
| Dew.Core | 443 |
| Dew.Contents | 97 |
| Dew.UI | 48 |
| Assembly-CSharp | 0 |
| Mirror | 0 |
| DTT.AreaOfEffectRegions.Runtime | 0 |

型数には追加・削除、入れ子型、コンパイラー生成型を含む。意味のある仕様変更の件数ではない。直接メンバー参照と変更された body の交差には `EntityStatus.CalculateStats` と `DewLocalization.GetUIValue` があるが、属性・文字列・動的参照の影響範囲はこれより広い。

修正後の属性パッチは 303 クラス、404 フック。このうち `TargetMethod(s)` を持つものは 61 クラスだった。静的に解決するパッチ先と通常フックの引数名に、未解決の候補は残っていない。フィールド注入は旧 counter の注入を除いた 13 件が名前・型とも解決した。`Transpiler` の `instructions` は本体引数ではないため、引数名の比較から除いた。

静的な照合は、すべての Harmony パッチが Unity 上でインストールできたことを意味しない。解析器は同名 overload の引数型指定を完全には再現しない。61 クラスの動的解決と手動登録も、静的な未解決件数ゼロには含めていない。

## 判定

- **(a)** MOD が使う名前・処理前提への不一致を検出しなかったもの。新本体の挙動をそのまま受ける場合も、影響を併記する。
- **(b)** 名前やメンバーの形が変わり、既存パッチまたはガードが外れるもの。
- **(c)** パッチ先は残るが、処理順序、生成親、計算の基準などが変わるもの。既存処理が新仕様に追従できる場合と、判断を残す場合を区別する。

### 修正した箇所

| 本体の変更と根拠 | MOD の接点 | 判定と修正 |
|---|---|---|
| `AttackTrigger._nextEveryFourAttackIndex` が field から private property へ移動。実 counter は `EntityAbility` に保持。Core `AttackTrigger.cs:9,99,130`、`EntityAbility.cs:23,171` | `HostAuthority.MemoryActivationAttribution.cs:20` の `NativeAttributedBasicPrepare`。旧 `____nextEveryFourAttackIndex` は Harmony の field 注入 | **(b)** 本体 property getter を `Prepare` で解決し、`Func<AttackTrigger,int>` にした。BeforePrepare 通知時の `index == 3` を読む意味は維持。通知後の increment / wrap を先取りしない |
| `Se_R_DangerousTheory` の購読 list と解除処理が整理され、追加ダメージ callback の生成名が `b__21_0` から `b__20_0` へ変更。Contents `Se_R_DangerousTheory.cs:75,80,90,96` | `HostAuthority.MemoryNativeAdditionalAdapters.cs:46` の `NativeDangerousTheoryAddedPacket` | **(b)** 属性の callback 名だけを変更。`EventInfoAttackEffect effect`、会心条件、追加 packet の scope と元に戻す Finalizer は維持 |
| `Ai_EmbraceNewIdentity.UpdatePositions` の raw IL 指紋が変化。Core `Ai_EmbraceNewIdentity.cs:122` | `NativePerfPatches.cs:577` の `NativeEmbracePositionPerf` | **(b)** 処理を確認して FNV-1a を `7063811622246808011` から `8607548223805543170` へ更新。IL 長 159、locals 4、EH 0 は維持 |

counter の getter 解決または実行に失敗した場合は、Resolve の4回目攻撃への補助帰属だけを停止し、警告を1回出す。基本攻撃の Prefix と MOD の他機能は残る。毎攻撃の reflection 呼び出しや delegate の作り直しは追加していない。

Embrace は、旧新のメンバー名に解決した命令列、分岐先、locals、EH が一致した。locals は `Single, Transform[], Int32, Transform`。生の参照トークンの差で指紋が変わったもので、位置計算の変更は検出しなかった。`Audit/embrace-old.json` と `embrace-new.json` に比較結果がある。既存の指紋ガードと、skill 自身または祖先 transform を含む場合に本体へ戻す処理は維持した。

### 処理前提が変わる箇所

| 本体の変更と根拠 | MOD の接点 | 判定と対応 |
|---|---|---|
| `maxHealthBeforeOverride` を `finalStatsProcessors` の直前に保存し、`DamageInstance.multiplyDamageByMaxHp` はこの値を使う。Core `EntityStatus.cs:323,1329`、`DamageInstance.cs:321` | `HostAuthority.PressureDividend.cs:64` の HP processor、`HostAuthority.EnemyOwnership.cs:333,373`、`HostAuthority.DynamicDifficulty.cs:203,241`、`HostAuthority.Waypoints.cs:124,133`、`BossSharesNative.cs:32` | **(c)** MOD の最終 HP 倍率より前の値が、この本体割合ダメージの基準になる。新仕様として受ける。MOD の HP 倍率や damage 算式を書き換えて旧仕様へ戻す変更はしていない。すべての割合ダメージがこの経路を通るという意味ではない |
| `Monster.OnPrepare` が `base.OnPrepare()` 後に hunter を reset。`Se_HunterBuff.OnDestroyActor` は active な monster だけ hunter を解除。Core `Monster.cs:159`、`Se_HunterBuff.cs:99` | `BossSharesNative.cs:10,56`、`EnemyOwnershipNative.cs:62`、報酬側の `enableGoldAndExpDrops` 判定 | **(c)** HP 初期化後に native multiplayer bonus を追加する順序は維持。所有権の Prepare / Finalize と pool 後始末を変更する必要は検出しなかった。本体の hunter reset に従う |
| Cetus BI の AOE が星自身からの生成から spawner の `ai.CreateAbilityInstance` へ変更。BeforePrepare 購読から Created 購読へ。Contents `Se_Star_Cetus_F_BI_SelfDamageAoeCold.cs:14,27,33` | `HostAuthority.MemoryActivationAttribution.cs:429,473` の子 instance binding と親巡回 | **(c)** 子 AOE は spawner の元発動 ID を継承し得る。実際の親を根拠にする C02 の仕様（`v1.31-new-mechanisms.md:43`）と一致するため、追加修正なし。新しい発動 ID を子ごとに作らず、既存の発動単位の上限を共有する |
| Cetus BI の自傷元は `selfDamageSource`、消失時は caster に fallback。OnDisable で参照を消す。Contents `Ai_Star_Cetus_F_BI_SelfDamageAoeCold_AOE.cs:13,17,45` | 同上、および実 damage を観測する鍛錬 | **(c)** 敵への AOE と自傷の actor を区別している。自傷は星または caster から dispatch され、子 AOE の記憶帰属をそのまま自傷へ転用しない。生成親の変更を打ち消す処理は加えない |
| Mist の幻影を hero からではなく元の `Ai_Q_Lunge` から生成。`FinalDamageData` を保持して `SetAmountOrigin`。Contents `Se_Star_Mist_F_LG_ChainAttack.cs:82,86`、`Ai_Star_Mist_F_LG_ChainAttack_Phantom.cs:27,89` | 同上。これらの具体型への固定パッチはない | **(c)** 元 Lunge の発動 ID と実際の記憶の親を継承する既存経路で扱う。damage origin の新しい計算を変更しない。Gem / 属性効果 / MOD 生成の除外と非空 chain の制限も維持 |
| Quartet は4回目の attack instance の破棄時に爆発。marks / pending は `ActorRef`、unequip で消去。Contents `St_D_QuartetOfDeath.cs:11,26,57,83,92` | 一般の Actor lifetime / 帰属。Quartet 内部 callback への固定パッチはない | **(c)** 破棄通知は Core `Actor.cs:598`、pool 後始末は `Actor.cs:641,656`。MOD の tag 消去は `HostAuthority.MemoryActivationAttribution.cs:236,646`。破棄通知より前に tag を消す変更は検出しなかった。爆発の時点や marks を旧仕様へ戻さない |
| IcyVeins の `SetStack(1)` が OnCreate から OnPrepare へ。Contents `Se_D_IcyVeins.cs:16,34` | `HostAuthority.MemoryNativeAdapters.cs` の exact payload scope | **(c)** 発動 callback と引数は残る。scope は実 callback で設定するため、stack 初期化時点へ依存しない。変更なし |
| Cetus BA は hit 記録を OnHit から ActorEvent_OnDealDamage へ変更。Contents `Se_Star_Cetus_F_BA_CdReductionOnKill.cs:63,67,85` | `HostAuthority.NativeKillRewards.cs:178` の OnVictimDeath と `_hitTimes`, `_skill`, `trackingGracePeriod` | **(c)** 対象と利用フィールドは維持。本体の新しい hit 記録に従ってアシスト報酬を判断する。旧記録方法を再導入しない |
| `SkillTrigger` は type 変更時に level haste bonus を再計算。ChaosApple は inactive な変換元と level を保持して復元 | `NativeSkillLockRecovery.cs:12`、`NativeMemoryReuse.cs:10`、`Content.cs` の記憶 ID | **(c)** 装備中の実 SkillTrigger、live config、native lock を参照する既存処理で扱う。ロックを toggle せず再送する方式も維持。ChaosApple 内部に固定 callback パッチなし |

Cetus / Mist の帰属判定は型名から推測する追加対応ではない。既存の `BindAttributedNativeInstance` が実際の生成 actor から tag を受け取り、`TryGetMemoryActivation` が所有者、生存、装備 epoch、実 SkillTrigger を確認する。従来 tag が得られなかった子が新本体の親を通して帰属する可能性はあり、「出力が旧本体と同一」とは判定していない。

## 軽量化パッチと IL の確認

`NativePerfPatches.cs` では Embrace 以外の次の対象に、比較した normalized body の変更を検出しなかった。周辺で変わったフィールドや寿命処理との接点も照合した。

| MOD の箇所 | 対象と確認した前提 | 判定 |
|---|---|---|
| `NativeStatusDurationPerf:89` / `NativeStatusDurationPublication:244` | `StatusEffect.FrameUpdate`、duration setter 2 回、MoveTowards 1 回、countdown setter 1 箇所、dirty bit 1024 | (a) 同期値の公開条件を変更する必要なし |
| `NativeWeaponFxPerf:262` | `Hero.FrameUpdate` の renderer list と `EntityVisual._attachedEffects` | (a) model FX clone 追加は別経路。参照する list は維持 |
| `NativeObstacleAvoidancePerf:346` | `EntityControl.UpdateObstacleAvoidanceAndPriority` の setter 1 回 | (a) 命令数と更新対象を維持 |
| `NativeAttachFaceIdlePerf:395` / `NativeAttachFaceBakeRecord:505` | `Dew.GetBakedMeshOptimized` の skin / mesh / isNew、`Time.unscaledTime`、`BakeMeshInterval` | (a) bake 記録と idle 判定の前提を維持 |
| `NativeAccessoryOffsetPerf:722` | accessory の位置更新と identity offset | (a) 対象と演算を維持 |
| `NativeCooldownIdlePerf:796` | `AbilityTrigger.LogicUpdate` の multiplier 呼び出し 1 回 | (a) SkillTrigger の type 別 haste 更新とは区別 |
| `NativeTransformSyncIdlePerf:861` | `Network_syncData` / `_syncData` | (a) 同期 setter と比較対象を維持 |
| `NativeSleepPositionPerf:905` | `ActorManager.CanEntitySleep` の position / ToXY 各 2 回 | (a) 座標と判定順序を維持 |
| `NativeCastIndicatorCirclePerf:1005` | `CircleRegion.UpdateProperties` のフィールド、読取回数 | (a) DTT DLL の比較でも型差分なし |
| `NativeCameraZoomRotationPerf:1264` | カメラの zoom / rotation の取得と演算 | (a) 対象 body の変更なし |

他の `Native*.cs` と native adapter では次を確認した。名前が同じだけの箇所は、周辺処理の差分とも照合した。

| 接点 | 根拠と判定 |
|---|---|
| Actor の寿命と遅延生成 | `NativeActorLifetime.cs:165` 以降の OnStopClient / Cleanup / OnDisable / LogicUpdate / FrameUpdate、StatusEffect、Sanctification、Projectile の対象と注入フィールドは残る。`Actor.LockDestroyFor` の差は生成 closure 番号。`NativeProcSpawnSpread` の native lock 内へ収める前提に処理変更を検出しなかった。(a) |
| FX の prefab 保護 | Core `DewEffect.cs:628` 以降の PlayDetached は asset なら PlayNew へ fallback。`EntityVisual.cs:678,702,742,789` は model 外の FX を clone し、再ロード時に旧 clone を処分する。`NativeFxBudget.cs:145,171,186` は実 instance に保存・復元し、`NativeProcImpactFx.cs` は限定した攻撃 FX と overload を扱う。新しい detach 判断を上書きする必要なし。(c)、変更なし |
| 保護 effect の回復 | `NativeEntityProtectionRecovery.cs:17` の RemoveBasicEffect / `_spawnProtections` と参照元は維持。EntityStatus.AddBasicEffect は closure 番号の差が中心。未知の保護へ期限を追加しない。(a) |
| エッセンス cap と説明式 | `NativeEssenceCaps.cs:24` 以降の対象、`DewLocalization.EvaluateAndRenderExpression` を照合。GetUIValue の差は fallback / debug 表示側。cap の意味や式の書換対象を変える不一致を検出しなかった。(a) |
| Hysteria とボス処理 | `BossSharesNative.cs:10,67` の AddStatBonus 1 回、EventInfoDamage.damage への代入 1 箇所、`HostAuthority.BossNativeAdapters.cs:171` の DamageInstance.OnHit の Dispatch 1 回を確認。割合 HP の新しい値は本体から渡されたまま使う。(a)、HP 基準の差は前表の (c) |
| 動的な移動元走査 | `HostAuthority.BossNativeAdapters.cs:50,99` は実移動 API を呼ぶ body を選び、読めない method だけを空として扱う既存処理。新コンテンツの未読 body まで対応済みとは扱わない |
| 召喚獣と終了時処理 | SylvanCall / LittleBaam は death subscription list を削除したが、生成 API と親は維持。IceColdPresence の caster からの生成、JusticeGuillotine の無効対象破棄、Rewind の破棄待機は本体に従う。固定パッチ先の不一致を検出せず、寿命を早める変更は加えていない。(c) |
| 本体の damage 調整 | Ignite の ApplyAmplification 化、SmallMoltenCore Dragon の empowerAmp 追加を確認。MOD は FX の制限等を適用するが、この本体算式を旧版へ戻さない。(c) |

### NativePatchPreflight

`NativePatchPreflight.cs` は変更しなかった。機能別の診断であり、MOD 全体のインストールを止める条件ではない。Harmony の MethodCopier で既存と提案の Transpiler を合成し、detour を設けずに命令列を調べる構造を維持した。

確認対象は LastStarlight、Feather、Baptism、DoubleTap、NyxWorld、InkBeam、HP damage 帰属。capture field の型、動的 target の存在、Nyx の `local[1]` が Entity、Beam Hit の DamageData.Dispatch / HealData.Dispatch / CheckEnemyOrNeutral 各 1 回、LastStarlight iterator の WaitForSeconds ctor 2 回を照合した（`NativePatchPreflight.cs:235,277,291,322`）。新 counter の void Prepare は既存の Prepare 判定と整合する。getter の解決失敗は内部で処理し、Prefix のインストールを妨げない。

Startup.Tests は模倣 API による検証で、実ゲーム DLL に対して全 preflight / パッチを Unity 外からインストールしたものではない。

## 手動登録と名前参照

以下の手動登録を属性パッチの集計とは別に点検した。今回、登録先の削除や署名の不一致を検出しなかった。実 Unity での全登録成功は未確認。

| MOD ファイル群 | 主な対象 |
|---|---|
| `CulinaryIngredientDisplay.cs`, `CulinaryIngredientCap.cs`, `CulinaryMagnet.cs` | Culinary stack / pickup、CookingPot、StackedStatusEffect の stack 操作、GetIngredientCount / ConsumeIngredients、UI stack 表示、PickupInstance の吸引 fields |
| `PressureEnemyNativeRewards.cs`, `PressureEnemyCount.cs` | GameManager の gold / exp、PickupManager の death drop / stardust、Actor.PrepareAndSpawn、SpawnManager.Destroy、RoomMonsters の生成 routine、Room.StopRoom、Actor prepare / disable、Entity.Kill |
| `HostAuthority.NativeKillRewards.cs` | SafeAction<EventInfoKill>.Invoke、KillTracker.ReportKill、Actor pool / disable、SkillTrigger.ActorEventOnKill、Cetus.OnVictimDeath |
| `NativeDreamContent.cs`, `.Presentation.cs`, `.Effects.cs` | Actor prepare / startclient、loot 選択、equip / unequip、cast 判定、Gem 装着、Quicksilver、MergeGem、名前の overload と tooltip / ground UI |
| `InfinityBossArena.cs` | Erebos / Polaris の phase-change status、StatusEffect.OnCreate delegate |
| `DreamforgeMod.cs` | CreateClassProcessor と個別失敗の扱い |

`NativeSkillLockRecovery.cs` の lock lists、`EntityAbility._synedAbilities`、SyncList.AddOperation、RPC、UI button の `_target` も残る。同値 OP_SET を pending 操作の後に送る既存方式を維持し、解除と再ロックは行わない。Mirror の対象 DLL の比較で型差分はなかったが、実通信の確認とは区別する。

## 新本体機能との接点

| 分野と本体メンバー | MOD への影響・判定 |
|---|---|
| 保存: `DewProfileContinue.continueEpoch / continueStampUnix`、`DewSave.WriteContinueData` | Core `DewProfileContinue.cs:11`、`DewSave.cs:1747,1859`。epoch と時刻が追加されても、continueData と onSaveEnded は残る。`DewPersistence.GameData.serverActorData` と Serialize / Apply も維持（`DewPersistence.cs:132,702,772`）。`ClientSession.Continue.cs:57,83,196,227,556` は保存後の実 checkpoint 一致を確認して復元する。(c)、変更なし |
| クラウドと別端末での再開 | native continue に埋め込む MOD の checkpoint / run / profile / trades snapshot は既存経路で扱う。これだけで MOD 独立プロフィール全体のクラウド同期を保証できない。checkpoint 不一致や読取不能なら MOD 報酬 / Build だけを保留し、別遠征への巻き戻しを避ける。別端末の移行は未確認 |
| ロビー、crossplay、reload | Core `DewMod.cs:278,295,356,483`、`DewNetworkManager.cs:43,94`。gameplay altered / foreign hash / maxPlayers 判定と crossplay host 中の reload 制限に従う。NativeDreamContent は native 型の clone alias を使う。独自 Actor subclass を新規登録する変更なし。本体の制限を回避しない。(c) |
| reload と名前参照 | DewMod.Load の assembly 改名後に Assembly.Load(byte[]) する構造は維持。PatchMethodOwnership の前提を変える処理差は検出しなかった。削除された DewLaunchOptions.connectLobby や旧 mod 判定名への MOD の固定参照なし。(a) |
| 遠征と無限モード | Rift_RoomExit は地面投影後に sidetrack 位置を比較。MOD が patch する `UserCode_TpcInteract__NetworkConnectionToClient` に body 変更なし。Rift_Sidetrack.GetRoomNames は存在する sceneNames に絞った新 list を返す。MOD が共有 list の書換えに依存する箇所は検出しなかった。`InfinityMode.cs:626,1080` は room clear / exit lock と既存の travel を使う。(c)、変更なし |
| 鍛錬 | `HostAuthority.RunGrowth.cs:61` は実際の damage / shield と maxHealth を観測し、runId と owner.guid で保持する。parry 成功と会心基本攻撃の撃破 hook も残る。本体 damage 変更で受ける量や得る進行量は変わり得るが、MOD の閾値や数値は変えていない。(c) |
| map / health bar UI | UI_InGame_Map は localRotation を考慮した位置計算へ変更。無限モードの主対象は別型 UI_InGame_WorldMap。`DreamforgeUi.BossShares.cs:29` は UI_EntityHealthBar の RectTransform を使い、削除された色 cache に依存しない。(a)。描画は未確認 |
| ConversationShrineBase | 旧 BaseJonasInteractableObject の移動・改名。Shrine / ICustomInteractable を継承し、MOD は一般の成功使用イベントを観測する。旧具体型名への固定参照なし。(a) |
| WagesOfResolve、再開時の部屋生成 | 新 Shrine / RoomMod / Barrier を止める変更は加えない。DistantMemories は再開時に既存 prop を再利用し、新規生成を抑える。本体の LockDestroy / CreateActor / UnlockDestroy と既存の room 保存 hook を確認。WagesOfResolve の一部 body は未読のため、進行を含む全挙動は未確定 |
| 新記憶 LuckyHammer / ShockTrap / SpearThrow / BladeFan / WarBoomerang | これらを旧固定名で参照する MOD 箇所は検出しなかった。(a)。今回、専用星図や仕掛けを追加したものではない。新記憶の全処理を検証済みとはしない |

追加修正が必要とコードから判断できるクラッシュ・進行停止は、上記のパッチ不一致以外には検出しなかった。未読処理や実機挙動について、問題がないと保証するものではない。

## 未確認と残した課題

旧新の Contents 一括逆コンパイルは一部失敗し、両方とも exit 70 だった。部分出力と DLL の比較を併用した。提供ソースのファイルの有無だけで型の追加・削除は判断していない。

- 新 `Ai_C_ShockTrap_Mine.DestroyOldestTrapsOverLimit` と `Shrine_WagesOfResolve.OnUse` は Cecil の body 読取で例外になった。新 `RoomMod_WagesOfResolve` は提供 DLL の出力では ctor / MirrorProcessed のみ。一部の ret-only stub や不正な EH について、処理同一とは判断していない。
- 旧新とも読取不能だった既存 body は 23 メソッド。OnComplete は FrostTouch projectile / Sky BigBaam ranged / Nyx star projectile / StellaMatter throw、OnCreate は Polaris GravityPull / Frostbite Freeze / SantaHat / Endure、Explode は CosmicBlast / Fireball。ほかに UnstableMushroom.OnStartClient、BigBaam Buffer.GetFleeDestination、MisticIceVein callback、MasterOfAssassination / ReactiveShockwave / SpiritualDominance の ActiveLogicUpdate、TrueDuelist.CheckAndApplyExposed、SpiritualDominance.OnDestroyActor、Fleche.ActiveFrameUpdate、Suppression_Old callback、DebugTeleport.OnUse、MorasDomain の OnCreate_Waves / SpawnRewards がある。両方で同じ例外になっただけでは同一性を証明できない。固定名検索では MOD の直接パッチ先は検出せず、Fireball は FX の OnCreate 登録、Frostbite Freeze は CoopKeystones の型判定として参照している。動的な全 Actor 走査には別途制約が残る。
- 動的移動元走査の既存 catch は読めない method だけを除外する。新内容の body が読めない場合、その動作の捕捉までは証明できない。本体の新機能を一括で無効化する修正はしていない。
- 新 getter delegate の実 Unity / Harmony 上での生成と呼び出し、全パッチのインストール、実プレイ、協力通信、クラウド移行、UI 描画は未実行。この環境ではゲームを起動できないため、実機確認用の Issue は作成しない。
- HP 比例 damage の基準変更と、子攻撃の帰属が本体の親変更に従って変わり得る点は、本体仕様として残す。旧挙動へ戻す balance 調整は本対応に含めない。

## 検証結果

製品コードの最終変更後に、次をこのセッションで実行した。文書と履歴の追加後は製品コードを変更していない。

| 検証 | 結果 |
|---|---|
| `python3 tools/balance/gen_cs.py --check` | 成功。生成コードの差分なし |
| `dotnet build src/SodRpg.Mod -c Release -p:GameDir="/home/wang/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/gameupd-x` | 成功。0 errors / 6 warnings。変更前も同じ 6 warnings |
| `DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests -c Release --no-build` | 365 成功、0 失敗、0 skip。実ゲーム DLL の Embrace 指紋を読む既存 contract を含む |
| `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Mod.Startup.Tests -c Release` | 217 成功、0 失敗、1 skip |
| `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python3 tools/test_changed.py` | 選択 591 クラス / 11 プロセス、2070 成功、0 失敗。Startup 1 件と Core SlowFact 4 件は skip。個別 suite と重複するため件数は合算しない |
| 新 DLL と修正後 MOD DLL の静的解析 | 静的 target / 引数の未解決候補なし、field 注入の未解決なし。動的 install の代用にはしない |
| 文書・履歴の `yomiyasu_lint.py` と `git diff --check` | 成功。日本語 lint の候補 0 件、差分の空白エラーなし |

SDK は 10.0.111、実行時は .NET 10.0.11。net8.0 のテストを実行する .NET 8 runtime がないため、初回の通常実行は testhost 起動に失敗した。環境へ runtime を追加せず、`DOTNET_ROLL_FORWARD=Major` で成功を確認した。`test_changed.py` は既定の Windows 用 dotnet パスを利用できなかったため、`DOTNET=/usr/bin/dotnet` を指定した。

Startup の skip は `ModReloadTests.cs:26` に既存の明示 Skip 属性があり、Release の JIT inlining で Harmony patch を観測できないという理由。Core の4件は AllocationEquivalenceV131Tests の extended random 2 件と GeneratedHeroAcceptanceTests の全旅人走査 2 件で、既存の SlowFact。選択クラスに test でない helper / API 模倣を含むため、一部プロセスは該当 test なしで終了している。

未実行: 全 suite / slow test（依頼範囲外）、実ゲームの起動と上記実機項目（実行環境なし）。既存の模倣 API での成功だけを、新 counter getter の実機動作の証明には使っていない。
