# 記憶の飛び道具・演出の集約調査

調査日: 2026-10-19〜2026-10-20。対象: Shape of Dreams r1.4.1.2、Dreamforge RPG。

## 今回の調査方針

集約単位を記憶 `St_*` から、実行時に生成される飛び道具・Actor の型へ変更した。生成元の本文が読めなくても、同じフレーム・同じ鍵で生成された個数を数える。生成数の事前集計を適用条件にしない。

型名から集めた候補は213型。記憶からの生成呼び出しを追えたのは12/182件（6.6%）、発動結果や親への明示参照で対応を説明できるものを含めると28/182件（15.4%）。同名族だけの候補対応は、この網羅率に加えない。固有の命中・効果処理が元 IL で `ret` だけの25型は、今回の方針に従ってCとする。

以下の「前回の調査記録」は停止時点の記録として残す。記憶単位の全件集計が再開条件という記述や、欠落型をCに入れないという判断は前回の方針である。今回の型単位の分類・集約条件・見積もりは後段に記載した。

## 前回の調査記録（2026-10-19）

### 調査の状態

**資料不足で停止中（blocked）。全件の分類と実装設計は未完了。**

指定された `Dew.Contents.dll` を全体逆コンパイルし、反射一覧にあるトップレベルの `St_*` 182型をすべて取り出した。型名の差分は0件だった。しかし、一部の生成元・命中処理は C# の復元に失敗するだけでなく、元の DLL の IL 自体が `ret` 1命令だけだった。値を返すメソッドにも同じ欠落がある。空の本文を「生成数0」「乱数なし」「単体なのでC」と解釈することはできない。

本調査は「複数生成する記憶をすべて表に載せる」ことを完了条件としている。欠落箇所を推測で分類すると、その条件も「元より強くならない」という条件も保証できない。実行不能な項目があれば止めて理由を報告するという今回の作業指示に従い、以下に確認済みの事実と再開条件を残す。完成した設計書として実装に使わない。

MOD のコード、既存の仕様書、更新履歴は変更していない。逆コンパイルした本体コードもリポジトリに含めていない。

## 1. 調査資料と網羅性

| 資料 | 確認結果 |
|---|---|
| `~/dev/sod-gamedata/VERSION` | `r1.4.1.2 ab05a05 (2026-10-19)` |
| `~/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/Dew.Contents.dll` | 指定の調査対象。全体を取り出したが、一部のメソッド本文が欠落 |
| `~/dev/sod-gamedata/reflection-dump/_alltypes.txt` | トップレベルの `St_*` は182型。入れ子型を含む `St_` 始まりの行は203行 |
| `/tmp/memcoalesce-decomp` | 全体逆コンパイルの出力先。トップレベルの C# ファイル2455個、うち `St_*.cs` 182個 |
| 既存の `~/dev/sod-gamedata/decompiled/sod-decomp/Dew.Contents` | 一部の型だけを含み、今回の欠落を補えない |
| `~/dev/sod-gamedata/decompiled/sod-contents` | README によれば旧 r1.4.0.13 の13ファイル。欠落箇所の本文は見つからなかった |
| `~/archive/sod-gamedata-r1.4.0.13/Shape of Dreams/Shape of Dreams_Data/Managed/Dew.Contents.dll` | `Ai_QR_TwistedTales_Spawner` を個別に取り出したが、同じメソッド本文が欠落。現行版の根拠には使わない |

使用した ilspycmd は11.1.0.9782。全体逆コンパイルは終了コード70で、23メソッドの復元エラーを報告した。ファイルは出力されたが、コマンドの成功とは扱わない。出力中には、この23件とは別に「Method body consists only of 'ret'」という注記があるため、復元エラーの件数だけでは不足を把握できない。

再現に使ったコマンドは次のとおり。出力先はいずれもリポジトリ外に置いた。

```sh
~/.dotnet/tools/ilspycmd -p --disable-updatecheck \
  -o /tmp/memcoalesce-decomp \
  "$HOME/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/Dew.Contents.dll" \
  > /tmp/memcoalesce-decompile.stdout 2> /tmp/memcoalesce-decompile.stderr

~/.dotnet/tools/ilspycmd -il -t St_D_DanceOfValor --disable-updatecheck \
  "$HOME/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/Dew.Contents.dll" \
  > /tmp/memcoalesce-dance-il.txt
```

今回の `-il -t` の出力にはアセンブリ全体の IL が含まれていた。型名の宣言から対応する終了コメントまでを切り出し、以下のメソッドを確認した。C# の見かけの空実装だけで判断していない。

### 欠落を確認した箇所

| 記憶 | 本文が欠落した型とメソッド | 確定できない項目 |
|---|---|---|
| `St_QR_TwistedTales` | `Ai_QR_TwistedTales_Spawner.OnCreate`、`get_clampedCount` | 生成数、対象の配分、星による増減、生成時の抽選 |
| `St_D_DanceOfValor` | `St_D_DanceOfValor.OnEquip`、`ClientHeroEventOnSkillUse`、`EntityEventOnAttackEffectTriggered` | 発射契機、チャージから生成数への変換、弾ごとの状態 |
| `St_D_CrystalsInOrbit` | `Se_D_CrystalsInOrbit.ActiveLogicUpdate`、`ActiveFrameUpdate`、`get_Network_count` | 結晶の生成・再利用、軌道、命中との関係、各端末の描画数 |
| `St_D_FrostTouch` | `Se_D_FrostTouch_EmpowerAttacks.OnCreate`、同メソッド内コールバック | 追加弾の生成契機と本数、強化攻撃との対応 |
| `St_Nor_MyOwnLittleNebula` | `Se_Nor_MyOwnLittleNebula.SkillUsed`、`AttackFired`、`ShootMissileSequenced` | スタックと発射数の関係、連射の開始条件。入れ子の状態機械には生成呼び出しが残るが、入口の本文がない |
| `St_D_ReactiveShockwave` | `St_D_ReactiveShockwave.ActiveLogicUpdate`、`CheckInterval` | 発動条件、間隔、生成経路。前者は C# 復元エラーも報告された |

これは欠落箇所の代表例であり、影響する記憶の全件表ではない。`crystalCount`、`countRaw`、`_effects`、`maxChargeCount` などのフィールド名が残っていても、それだけで生成数や挙動を決めない。欠落の原因が抽出・配布用の加工なのか、別の要因なのかは未確認。

## 2. 確認できた複数生成の例

次の5件では、生成ループまたは連鎖を本文から確認できた。全件調査が終わるまでの部分結果であり、複数生成する記憶の総数ではない。生成数は本体コード上の式で示す。プレハブから上書きされる値は未取得なので、フィールドの初期値をゲーム内の確定値として扱わない。

| 記憶 | 根拠となる型・メソッド | 1回の発動からの生成数 | 動き・命中・乱数 | 同期と分類 |
|---|---|---|---|---|
| `St_R_Scattershot` | `Ai_R_Scattershot_Spawner.OnCreate` | `RoundToInt(GetValue(shootCount))`。発動単位の強化抽選に成功すると `addedShootCount` を加算 | `-maxAngle` から `+maxAngle` の扇状に等間隔で発射。強化抽選はループ前に1回。各弾は別の `StandardProjectile` なので、代表の命中だけから全弾命中とは判断できない | サーバで `CreateAbilityInstance`。暫定B |
| `St_L_Blizzard` | `Ai_L_Blizzard.OnCreateSequenced` | `RoundToInt(GetValue(waveCount))` 回。波の間隔は `waveInterval` | 指定地点に `Ai_L_Blizzard_Instance` を時間差で生成。生成ループに乱数呼び出しはない。各波の命中判定時刻は別で、途中の移動・死亡・生成元の消滅が結果に影響する | サーバで波ごとに生成。暫定B |
| `St_E_ChainLightning` | `Ai_E_ChainLightning.OnComplete` 内の `Sequence` | 初弾と後続を合わせて最大 `maxChainCount + 1` 個。候補不在なら早く終わる | 各段でダメージ通知し、`chainDelay` 後に次の対象を選ぶ。到達済み回数の辞書と現在位置を使う。対象選択式に明示的な乱数呼び出しはないが、通知後の状態を次段が読む | 後続も `CreateAbilityInstance`。連鎖の履歴と時刻を残す必要があり暫定B |
| `St_D_SharedPain` | `St_D_SharedPain.EntityEventOnCastCompleteBeforePrepare`、`Ai_D_SharedPain_Instance.OnCreateSequenced`、`OnCompleteSequence` | 反応1回につき `clampedCount` が正なら最大その個数。`clampedCount = Min(RoundToInt(GetValue(countRaw)), maxCount)`。初段は候補があると作られるため、0以下の設定を単純に「生成0」とは扱わない | ビームの到達時にダメージと次段の検索。検索はランダム順。命中済みリスト、元のダメージ、直前対象を引き継ぎ、候補がなければ自身へ戻る場合もある | サーバで個々の Actor を生成し、各端末でビームを描画。暫定B |
| `St_L_LightExplosion` | `Ai_L_LightExplosion.OnCreateSequenced`、`ActiveFrameUpdate` | 爆発を1回以上。被弾対象のリストと、その後の有効状態によって繰り返すため固定上限はこのループから確定しない | 対象を追って位置を更新。予告、`explodeDelay`、爆発、`gracePeriod` の順で進む。爆発の通知から得た対象の状態で再試行を決める。生成ループに乱数呼び出しはない | サーバで爆発 Actor を生成し、予告はネットワーク演出。暫定B |

ここでの「乱数呼び出しはない」は表に挙げた生成・選択部分に限る。会心、命中から発生するエッセンス、属性の付与まで乱数がないという意味ではない。命中先の型・基底処理・プレハブ設定を追った最終表は未作成。

## 3. A/B/C の分類

全182型の分類は未完了。上表の5件は暫定Bだが、A/B/C の総内訳は確定できていない。本文が欠けた型をCへ入れたり、調査できた型だけを分母にして全件分類済みと報告したりしない。

Aの確定には、同じ対象、ダメージ、命中時刻、抽選回数に加え、各個体の命中履歴、消滅条件、コールバック、親子関係を通る反応も同等であるという根拠が要る。Bは、空間・時刻・履歴が違う個体を代表の命中へ置き換えると結果が変わるもの。Cは、単体や独立した状態の維持が必要なものなど、集約できないか効果がないと本文から確認できたものとする。

`St_*` が空に見えるだけではCにできない。通常の発動は `AbilityTrigger.OnCastComplete` が設定された型の生成を担うため、設定先と生成先も調査対象になる。

## 4. 集約設計の確定条件

既存の `src/SodRpg.Mod/NativeProcCoalesce.cs` と、`docs/specs/lacerta-incendiary-proc-performance.md` の第3段は読んだ。既存処理はエッセンス由来の4種を対象とし、同一キーのN本に対する実体数は `min(N, 5)`。しきい値4本に加え、5本目が代表になるため、5本を1本にする設計ではない。

記憶へ広げる具体的な差し込み位置、個体データの形式、対象別の合否条件は未設計。既存キーの位置を1cmに丸める扱いや代表の命中時刻を、そのままAの根拠には使わない。異なる出発位置や軌道では、到達順と被弾結果が変わり得る。

上表のBの5件についても、「代表が当たれば本数分を通知する」という規則は採用できない。Scattershot は別方向の外れ弾を命中に変え得る。Blizzard は後の波を早めると、その時刻には範囲外だった対象へ当て得る。ChainLightning と SharedPain は、途中の通知を経て初めて次の候補が決まる。LightExplosion は前の爆発結果が次の爆発の有無を変える。

このため、各Bについて個別の命中と時刻・履歴を残す方法まで示してから、「強化にならない」ことと期待値の損失を評価する。代表の命中から追加分を推測するだけの設計や、不明な弾をすべて捨てて大きく弱体化する設計では、今回の完了条件を満たさない。記憶ごとの最終規則と期待値の評価は未完了。

粒子や GameObject の拡大は、判定に使う Collider、検索範囲、移動速度を変えないことを型とプレハブの両方で確認してから設計する。ホストの拡大がゲストでも再現されるかは同期方法に依存するため、現時点では保証しない。

## 5. ゲスト側の削減見積もり

`Actor.PrepareAndSpawn` は `NetworkServer.Spawn` を呼ぶ。Actor の生成を省けば、ゲストに届くオブジェクトの生成と初期状態の同期を減らす余地がある。一方、`FxPlayNetworked` などの演出送信と、Actor 内の各端末での描画は別に数える必要がある。

記憶ごとの最終見積もりは未作成。たとえば Scattershot の実体N個を1個に置き換えられれば、生成数は計算上N−1個減る。ただし、その置き換えが今回の非強化・期待値の条件を満たす方法は未確定なので、採用する削減率としては扱わない。

SyncVar は生成時の初期同期と、生成後に値が変わったときの同期を分ける。飛び道具の本数が減った比率を、そのまま全通信量・FPS・粒子数の改善率にしない。プレハブの粒子数、変化する SyncVar、寿命、同時発動数、実測値がないため、数値を置く場合は条件を明記した推定が必要になる。

## 6. 実装順と再開条件

効果順の順位と、1ワーカーで収まる実装の束は未提案。生成数が多そうな型の本文が欠けており、効果と危険を比較して優先順位を確定できない。

再開には、r1.4.1.2 の上記メソッドの本文が残る `Dew.Contents.dll`、または同じ版の本文を確認できるローカル資料が必要になる。現行の DLL が配布用に加工されたものかどうかは未確認。別の逆コンパイラを使うだけで、`ret` 1命令から元の処理を復元できるとは扱わない。

型の設定先、生成数の実値、粒子数、Collider とスケールの関係を確定するため、対応するプレハブ情報も必要になる。今回の `Shape of Dreams_Data` には `Managed` ディレクトリだけがあり、asset bundle やプレハブのデータは見つからなかった。入手できない値は式と未確認の条件を残し、数値を作らない。

資料がそろった後は、182型の発動先から Actor・AbilityInstance・Projectile・StatusEffect・召喚物・星の変更先を追い、全件の表を完成させる。その後にBの各規則、条件付きの削減推定、実装の束を確定する。

## 検証と未実行

実行した確認は、全体逆コンパイル、反射一覧との182型の一致確認、欠落箇所の IL 確認、旧版 DLL の `TwistedTales` の個別確認、既存集約処理と基底生成経路の読解。全体逆コンパイルには上記の23メソッドのエラーがあった。

未実行・未完了は次のとおり。

- 全記憶の生成経路の調査、A/B/C の確定内訳、Bの記憶ごとの最終規則、期待値の評価、実装順の束分け。メソッド本文の欠落により停止した。
- プレハブ・asset bundle の検査。今回の資料に含まれていない。
- MOD のビルドとテスト。文書だけの変更であり、今回はコードを変更していない。新しいテストも作成していない。
- ゲーム内と協力プレイでの再現、Profiler、ネットワーク・SyncVar・粒子数の実測。この環境ではゲームを起動できない。実機確認だけを目的とする Issue は作成しない。


## 7. 型単位で再開した調査（2026-10-20）

### 7.1 対象集合と網羅率

候補集合は213型で、分類は **A=0、B=56、C=157**。Cのうち25型は固有処理の本文欠落である。これは記憶由来と全件確定したActorの総数ではなく、名前と読める生成経路から集めた調査候補の数である。型の分類と実装の準備状態を区別する。Bは空間判定を共有せずにゲスト表示を減らす検討対象であり、今回の資料だけで有効化できる型という意味ではない。

| 集計 | 件数 | 数え方と限界 |
|---|---:|---|
| 記憶の分母 | 182 | トップレベルの `St_*.cs`。入れ子型を除く |
| 名前が対応するAi候補 | 206型 / 153記憶 | `Ai_` と `St_` の接頭辞を除いた部分が一致、または記憶名の後ろが `_` で続く。生成の証拠ではない |
| 記憶用Summon候補 | 5型 | Dragon、Fenrir、LeafHound、AnnoyingBanner、LittleBaam。Aiと合わせた名前対応は154記憶 |
| 命名例外の追加 | 2型 | ButchersStrikeが作る `Ai_L_BloodStrike_Meat`、Sneezeが作る `Ai_Mon_Sky_BigBaam_BeamAtk` |
| 生成呼び出しをStから追えた記憶 | **12/182 = 6.6%** | `CreateAbilityInstance`・`CreateStatusEffect`・`SpawnSummon` を介する読めた経路。型検査だけの参照を除く |
| 発動結果・親参照を含むコード対応 | **28/182 = 15.4%** | 上記12件に追加16件。生成呼び出しの網羅率とは別の値 |
| 名前候補に命名例外の対応も含めた記憶 | 156/182 = 85.7% | 154記憶にM_ParryMasterとDangerousTheoryを追加。**確定した生成網羅率には使わない** |

生成を追えた12件は `St_D_ExplosionArtist`、`St_D_HeartOfThePack`、`St_D_IceColdPresence`、`St_D_IcyVeins`、`St_D_MercyOfEl`、`St_D_QuartetOfDeath`、`St_D_Resolve`、`St_D_SalamanderPowder`、`St_D_SharedPain`、`St_L_SmallMoltenCore`、`St_Q_MoonlightPact`、`St_R_AnnoyingBanner`。

追加16件は `St_C_Whirlwind`、`St_L_ButchersStrike`、`St_QR_InfernalTales`、`St_Q_GoldenBurst`、`St_Q_Reduction`、`St_E_Blink`、`St_E_ClutchesOfMalice`、`St_E_SearingCharge`、`St_L_CoinExplosion`、`St_Q_DeathMark`、`St_Q_Fleche`、`St_Q_SuperNova`、`St_R_BladeFan`、`St_M_ParryMaster`、`St_R_DangerousTheory`、`St_L_Blizzard`。`cast.instance`、`firstTrigger`、triggerのchildren内の型、または発動結果を扱う箇所を根拠とする。たとえば `St_C_Whirlwind` の子の型検査は対応の証拠であり、生成呼び出しではない。

名前候補・例外対応のどちらも得られなかった26件は、`St_C_Hemorrhage`、`St_D_AstridsMasterpieceEnGarde`、`St_D_AstridsMasterpiecePriorite`、`St_D_CircleOfLife`、`St_D_ConvergencePoint`、`St_D_CrystalsInOrbit`、`St_D_DisintegratingClaw`、`St_D_DoubleTap`、`St_D_MasterOfAssassination`、`St_D_MomentumOfCombat`、`St_D_TheKillingFlow`、`St_D_ThirstOfVengence`、`St_D_TrueDuelist`、`St_E_LizardlyBlessing`、`St_E_Rewind`、`St_E_UmbralEdge`、`St_L_HerosReturn`、`St_L_PyranasFireball`、`St_M_Charge`、`St_M_Flicker`、`St_M_IceColdPresence`、`St_Nor_ReptilianThirdEye`、`St_Nor_WindowToTheSoul`、`St_Q_PerfectExecution`、`St_Q_Suppression_Old`、`St_R_LightningDance`。状態効果だけの処理、設定先の不明、本文欠落が混在し、「Actorを作らない26件」とは断定しない。

`Ai_E_Aftershock_Damage`、`Ai_R_Lava_LavaField`、敵由来の `Sum_BossMaw_MoonlightPact_Fenrir` はこの集合から除いた。`St_D_IcyVeins` にある元攻撃 `Ai_Atk_CetusStaff` の型検査、`Ai_Q_Riposte` にある `GetAbility<St_M_FastFeet>` も生成対応に数えない。生成APIの括弧内にあるgeneric型・lambdaの型・typeofを確認し、保存済みdelegate経由は別に読む方法なので、12件は読めた経路の下限である。

### 7.2 分類と表の読み方

- **A**：1対象への即時通知を、本体と同じ回数・乱数・順序・個体の意味で再現できる型。今回の資料では確定0型。
- **B**：軌道、到達時刻、範囲、実命中先で結果が変わる型。代表の命中を流用しない。表の型別規則と8章の個別判定保持を両方満たす場合だけ、ゲストの生成公開を減らす候補にする。
- **C**：本文欠落、継続する個別状態、制御・召喚物、または集約効果や同等性を説明できない型。今回は個別生成・個別公開を保つ。読めない型をA/Bへ推測で入れない。

表の記憶対応は「生」=本文の生成呼び出し、「参」=発動結果または親の明示参照、「名」=名前だけの候補。Se→Actorだけ読めても、St→Seの入口が未確定なら12件へ加えない。同じ記憶に対応する複数の型を、網羅率へ重複加算しない。

ネットワーク欄の「経路○」は記載の生成経路が本体のSpawnへ進むことを確認したもの。「基底○・設定?」は `Actor.PrepareAndSpawn` → `NetworkServer.Spawn` という共通経路はあるが、その記憶のプレハブ設定から実行されることは未確定という意味である。すべての候補が実際にネットワーク生成されると断定する欄ではない。

乱数欄は固有本文と読めた生成元の範囲に限る。「明示なし」でも、`StandardProjectile.enableRandomCurve` の `Random.insideUnitSphere`、基底DamageInstanceの属性処理、会心や命中からの反応、プレハブ内の演出まで乱数なしとはしない。基底の抽選も個体ごとに残す。B-Pは飛翔・衝突を保つ規則、B-Rは範囲の再検索と順序を保つ規則で、どちらもサーバの個体を減らす分類ではない。

根拠欄のメソッドは各型のC#復元結果にある。固有overrideがない型は基底を根拠にした。本文欠落25型は元ILで確認したもので、通常の空の基底メソッドやコンストラクタ、Mirror生成メソッドの空実装はこの25型に数えない。

### 7.3 型別の処理・分類・非強化規則

| 型 | 作る記憶・対応の証拠 | 命中・効果処理 | 乱数 | ネットワーク生成 | 分類 | 根拠・型別の扱い |
|---|---|---|---|---|---|---|
| `Ai_C_BackStep` | `St_C_BackStep`（名） | 後退・daze・無衝突と時間差のBomb生成 | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：移動と0.03秒間隔の生成sequenceを保持する制御個体 |
| `Ai_C_BackStep_Bomb` | `St_C_BackStep`（名） | 完了時に範囲ダメージ・押し戻し・スタン | 明示なし | 基底○・設定? | B | `OnComplete`。B-P：各爆弾の完了位置・時刻で再検索し、押し戻しの後も次の爆弾を別判定する。 |
| `Ai_C_BeamOfLight` | `St_C_BeamOfLight`（名） | 継続channel、対象追跡、tickダメージと味方回復 | 味方候補のランダム順 | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate / OnDestroyActor`。C保持：対象再選択・tick・中止条件を持つ |
| `Ai_C_CorrosiveTrails_Gas` | `St_C_CorrosiveTrails`（名）；`Se_C_CorrosiveTrails_Spawner`→本型（生、St入口未確定） | 範囲内へpoisonを新規付与または寿命更新 | 明示なし | 経路○ | C | `ActiveLogicUpdate`。C保持：lingerDurationとtickごとの状態を持つ |
| `Ai_C_DarkBolt` | `St_C_DarkBolt`（名） | 強化抽選、CD還元、貫通数別ダメージとAttackEffect | 強化抽選 | 基底○・設定? | C | `OnCreate / OnEntity`。C保持：個体別の_hitCountとpenetrationCountを持つ |
| `Ai_C_DarkSpear` | `St_C_DarkSpear`（名） | 範囲ダメージ、盾への増幅、強化時自己回復、移動・スタン | 強化抽選、移動先の偏差 | 基底○・設定? | B | `OnCreate / OnBeforeDispatchDamage / OnHit`。B-R：強化抽選を個体別に残し、各対象の盾と移動先を通知時点で評価する。 |
| `Ai_C_FlashFreeze` | `St_C_FlashFreeze`（名） | 範囲ダメージ、Cold対象へRootと増幅、味方へ盾 | 明示なし | 基底○・設定? | B | `OnBeforeDispatchDamage / OnCollisionCheck`。B-R：各通知時点のCold・Rootを読み、敵判定の後に味方を再検索して盾を付ける。 |
| `Ai_C_GlacialStomp` | `St_C_GlacialStomp`（名） | 範囲ダメージ、浮かせ、命中ごとの自己盾・スタン | 明示なし | 基底○・設定? | B | `OnHit / OnBeforeDispatchDamage`。B-R：各範囲の命中数を残し、命中ごとの盾と浮かせを省略も水増しもしない。 |
| `Ai_C_IceBlock_Projectile` | `St_C_IceBlock`（名）；`Se_C_IceBlock`→本型（生、St入口未確定） | 派生は条件付き演出。盾・終了時効果は生成元Se側 | 明示なし | 経路○ | C | `OnEntity`。C保持：基底delegateとプレハブが不明で、独立した効果なしとは確定できない |
| `Ai_C_IceBolt` | `St_C_IceBolt`（名） | 固有処理の本文が欠落：OnEntity | 不明 | 基底○・設定? | C | `OnEntity（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_C_IceClaw` | `St_C_IceClaw`（名） | dash・次config変更、与ダメージ通知から自己回復 | 明示なし | 基底○・設定? | C | `OnCreateSequenced / OnHit`。C保持：Actor固有のイベント購読とdash状態を持つ |
| `Ai_C_IceClaw_Second` | `St_C_IceClaw`（名） | IceClaw基底のdash・回復・スタン | 継承先に明示なし | 基底○・設定? | C | `基底 Ai_C_IceClaw`。C保持：IceClawのイベントと移動状態を継承する |
| `Ai_C_LuckyHammer` | `St_C_LuckyHammer`（名） | 固有処理の本文が欠落：OnPrepare / OnCreate / OnEntity / ApplyTargetlessMode | 不明 | 基底○・設定? | C | `OnPrepare / OnCreate / OnEntity / ApplyTargetlessMode（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_C_MagicSword` | `St_C_MagicSword`（名） | AD/AP選択の範囲ダメージ、AttackEffect、dash | 明示なし | 基底○・設定? | C | `OnCreate / OnBeforeDispatchDamage`。C保持：攻撃方式の状態とcasterのdashを扱う。今回は制御側を集約しない |
| `Ai_C_MassProtection` | `St_C_MassProtection`（名） | 味方検索、盾、発動後channel | 明示なし | 基底○・設定? | B | `OnCreate`。B-R：各発動の味方検索・盾付与・channel開始を本体の順に実行する。 |
| `Ai_C_Pew` | `St_C_Pew`（名） | charge channelとProjectile生成 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate`。C保持：chargeとchannelを維持する発射制御個体 |
| `Ai_C_Pew_Projectile` | `St_C_Pew`（名） | charge別Fireダメージ、会心条件、任意の押し戻し | 明示なし | 基底○・設定? | B | `OnCreate / OnEntity`。B-P：charge・半径・始点・方向を個体別に残し、その個体の実命中へ通知する。 |
| `Ai_C_PressurePoint` | `St_C_PressurePoint`（名） | 範囲ダメージ・押し戻し、壁到達時の追加通知 | 移動処理の結果に依存 | 基底○・設定? | C | `OnCreate`。C保持：pendingWallHits・generationとcallbackを持つ |
| `Ai_C_Purgatory` | `St_C_Purgatory`（名） | 遅延範囲攻撃後、HP重みで選んだ味方へ回復弾 | 回復先の重み付き抽選 | 基底○・設定? | C | `OnCreateSequenced`。C保持：生成先と待機sequenceを持つ。子HealだけをB候補にする |
| `Ai_C_Purgatory_Heal` | `St_C_Purgatory`（名） | 実命中先への回復 | 明示なし | 基底○・設定? | B | `OnEntity`。B-P：回復先を代表へ固定せず、途中衝突と複数命中の可否を本体設定のまま残す。 |
| `Ai_C_ShockTrap_Explosion` | `St_C_ShockTrap`（名） | 固有処理の本文が欠落：OnHit | 不明 | 基底○・設定? | C | `OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_C_ShockTrap_Mine` | `St_C_ShockTrap`（名） | 固有処理の本文が欠落：OnPrepare / OnCreateSequenced / ActiveLogicUpdate / OnDestroyActor | 不明 | 基底○・設定? | C | `OnPrepare / OnCreateSequenced / ActiveLogicUpdate / OnDestroyActor（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_C_Sneeze` | `St_C_Sneeze`（名） | 確率で扇状にBeamAtkを生成 | 発動抽選、1〜4本、2回の倍増抽選 | 基底○・設定? | C | `OnCreate`。C保持：生成先BeamAtkが継続状態を持つ。発射機の集約は保留 |
| `Ai_C_SparklingWaterGun` | `St_C_SparklingWaterGun`（名） | 敵へPureダメージとスタン、味方へ回復・Fire解除 | 明示なし | 基底○・設定? | B | `OnEntity`。B-P：実命中先の関係・Summon判定・Fire状態を毎回評価する。 |
| `Ai_C_SpearThrow` | `St_C_SpearThrow`（名） | 固有処理の本文が欠落：OnPrepare / OnCreate / OnEntity / ApplyTargetlessMode | 不明 | 基底○・設定? | C | `OnPrepare / OnCreate / OnEntity / ApplyTargetlessMode（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_C_Starfall` | `St_C_Starfall`（名） | 一定間隔で対象を再検索しProjectile生成 | 対象のmaxHP重み、演出位置 | 基底○・設定? | C | `OnCreateSequenced`。C保持：生成sequenceとcaster死亡監視を持つ |
| `Ai_C_Starfall_Projectile` | `St_C_Starfall`（名） | 対象上空から飛来、Lightダメージ | 生成元に重み付き選択・演出位置抽選 | 基底○・設定? | B | `OnPrepare / OnEntity`。B-P：生成元の対象選択を変えず、各弾の始点・速度方向・命中時刻を残す。 |
| `Ai_C_SwiftSlash` | `St_C_SwiftSlash`（名） | dash攻撃、Unstoppable、初回命中CD還元 | 明示なし | 基底○・設定? | C | `OnCreate / OnBeforeDispatchDamage`。C保持：dash状態と初回命中フラグを持つ |
| `Ai_C_Whirlwind` | `St_C_Whirlwind`（参） | channel中の回転と周期的範囲ダメージ | 明示なし | 基底○・設定? | C | `OnCreateSequenced / ActiveLogicUpdate / OnDestroyActor`。C保持：継続時間・tick・channel中止条件を持つ |
| `Ai_D_BeautifulThreat_Feather` | `St_D_BeautifulThreat`（名）；`Se_D_BeautifulThreat`→本型（生、St入口未確定） | 敵へダメージと自己回復、味方へ回復 | 明示なし | 経路○ | B | `OnPrepare / OnEntity`。B-P：関係を実命中時に判定し、敵命中ごとの自己回復とchainを残す。 |
| `Ai_D_BurningFist_Attack` | `St_D_BurningFist`（名） | 近接・dash・channel、終了時に子Instance | 明示なし | 基底○・設定? | C | `OnCreate / OnBeforeDispatchDamage / OnHit / OnDestroyActor`。C保持：移動完了と攻撃イベントの状態を持つ |
| `Ai_D_BurningFist_Attack_Instance` | `St_D_BurningFist`（名） | 基底範囲ダメージ、遅延処理後の自己回復 | 明示なし | 基底○・設定? | B | `OnAfterDelay`。B-R：各範囲を個別検索し、OnAfterDelayの回復を個体ごとに同じ順で行う。 |
| `Ai_D_ChargedAnguillian_Lightning` | `St_D_ChargedAnguillian`（名）；`Se_D_ChargedAnguillian`→本型（生、St入口未確定） | Lightダメージ後に破棄 | 生成元が候補をランダム順に検索 | 経路○ | B | `OnEntity`。B-P：対象別の発射と基底衝突を残す。異なる対象を1対象への反復にしない。 |
| `Ai_D_DanceOfValor_Projectile` | `St_D_DanceOfValor`（名） | 固有処理の本文が欠落：OnEntity | 不明 | 基底○・設定? | C | `OnEntity（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_D_ExoticMatter_Projectile` | `St_D_ExoticMatter`（名）；`Se_D_ExoticMatter`→本型（生、St入口未確定） | 完了地点の範囲へstack比例のLightダメージ | 明示なし | 経路○ | B | `OnCreate / OnComplete`。B-P：各explosionStack・到達時刻・範囲を残し、前の通知後に次の範囲を再検索する。 |
| `Ai_D_ExplosionArtist_DodgeExplosion` | `St_D_ExplosionArtist`（生） | 基底範囲ダメージ後、親HandleHitへ自身と対象を渡す | 明示なし | 経路○ | C | `OnCreate / OnHit`。C保持：親が個体識別を使う。個体を省く同等性が未確定 |
| `Ai_D_ExplosionArtist_LargeExplosion` | `St_D_ExplosionArtist`（生） | 基底範囲ダメージ後、親HandleHitへ自身と対象を渡す | 明示なし | 経路○ | C | `OnCreate / OnHit`。C保持：親が個体識別を使う。個体を省く同等性が未確定 |
| `Ai_D_FrostTouch_FrostProjectile` | `St_D_FrostTouch`（名） | 固有処理の本文が欠落：OnPrepare / OnCreate / OnComplete | 不明 | 基底○・設定? | C | `OnPrepare / OnCreate / OnComplete（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_D_HeartOfThePack_Explosion` | `St_D_HeartOfThePack`（生） | 固有overrideなし、基底の範囲ダメージ | 基底の属性処理 | 経路○ | B | `基底 InstantDamageInstance`。B-R：基底OnCreateSequencedの遅延、対象検索、通知順を個体別に実行する。 |
| `Ai_D_IceColdPresence_AOE` | `St_D_IceColdPresence`（生） | 範囲命中で永続HP加算、postDelayの無敵・daze | 明示なし | 経路○ | C | `OnCreateSequenced / OnHit`。C保持：遅延callback・自己破棄と結び付く無敵を持つ |
| `Ai_D_IceColdPresence_Dodge` | `St_D_IceColdPresence`（名） | Ai_GenericDodgeの移動・終了処理 | 基底・設定依存 | 基底○・設定? | C | `基底 Ai_GenericDodge`。C保持：回避中の制御状態を継承する |
| `Ai_D_IcyVeins_Damage` | `St_D_IcyVeins`（生） | 基底範囲ダメージ、個体のisCritで会心指定 | 明示なし | 経路○ | B | `OnBeforeDispatchDamage`。B-R：isCrit・dmgFactor・strengthMultiplierを個体別に残し、通知をN倍値へ置き換えない。 |
| `Ai_D_MercyOfEl_Swipe` | `St_D_MercyOfEl`（生） | 固有overrideなし、基底の範囲ダメージ | 基底の属性処理 | 経路○ | B | `基底 InstantDamageInstance`。B-R：各個体のrange・damageDelay・重複判定を残して、その時点で再検索する。 |
| `Ai_D_ParryMaster_Parry` | `St_M_ParryMaster`（参）；`St_D_ParryMaster`は移動記憶をこれへ置換 | 無敵・判定半径変更、免疫通知でchargeとBuff | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：channelとイベント購読・解除を持つ。対応先はSt_M_ParryMaster |
| `Ai_D_PrismaticEyes_Attack` | `St_D_PrismaticEyes`（名）；`Se_D_PrismaticEyes`→本型（生、St入口未確定） | AttackProjectileの基本攻撃＋追加ダメージ | 基底攻撃と通知先依存 | 経路○ | C | `OnEntity`。C保持：元攻撃のcharge・beforePrepare・攻撃完了通知と結び付く |
| `Ai_D_QuartetOfDeath_Explosion` | `St_D_QuartetOfDeath`（生） | 単体へcurrentStack比例のPhysical/Dark通知後に破棄 | 固有本文に明示なし | 経路○ | C | `OnCreate`。C保持：Aに近いが、SetActor(this)と生成・破棄イベントを省く同等性が未確定 |
| `Ai_D_ReactiveShockwave_Shockwave` | `St_D_ReactiveShockwave`（名） | 固有処理の本文が欠落：OnHit | 不明 | 基底○・設定? | C | `OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_D_Resolve_Push` | `St_D_Resolve`（生） | 範囲内へ押し戻し・スタン、命中または元攻撃didHitで自己回復 | 明示なし | 経路○ | B | `OnCreate`。B-R：各rangeとattack.didHitをその発動時に読み、回復条件・押し戻し・回転を個別に通す。 |
| `Ai_D_RoaringFist` | `St_D_RoaringFist`（名） | 固有処理の本文が欠落：OnCreate / OnBeforeDispatchDamage / OnHit | 不明 | 基底○・設定? | C | `OnCreate / OnBeforeDispatchDamage / OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_D_SalamanderPowder_Projectile` | `St_D_SalamanderPowder`（生） | Fireダメージ、命中先が燃焼中なら会心・増幅 | 個体ごとに軌道偏差 | 経路○ | B | `OnCreate / OnEntity`。B-P：偏差抽選を共用せず、各実命中時のfireStackで増幅を判定する。 |
| `Ai_D_ScarOfTheWind_DashAtk` | `St_D_ScarOfTheWind`（名） | dash攻撃、追加Darkダメージと自己回復 | 明示なし | 基底○・設定? | C | `OnCreate / OnHit`。C保持：dash・無衝突・attack charge更新を持つ |
| `Ai_D_SharedPain_Instance` | `St_D_SharedPain`（生） | ビーム到達時の通知、次候補検索と子生成 | 次候補をランダム順に検索 | 経路○ | C | `OnCreateSequenced / OnDestroyActor`。C保持：hitEntities・originalDamage・到達時刻・Tween完了callbackを持つ |
| `Ai_E_AntiGravity` | `St_E_AntiGravity`（名） | bindとスタン後、待機してダメージ | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：対象拘束と待機sequenceを持つ |
| `Ai_E_Blink` | `St_E_Blink`（参） | teleportと戻り地点の管理 | 明示なし | 基底○・設定? | C | `OnCreateSequenced / OnDestroyActor`。C保持：backLocationとgenerationを持つ |
| `Ai_E_Blink_Back` | `St_E_Blink`（名） | 戻り先へのteleport | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：親Blinkの保存地点と寿命に結び付く |
| `Ai_E_ChainLightning` | `St_E_ChainLightning`（名） | 到達時通知、chainDelay後に次候補を選ぶ | 固有の選択式に明示なし | 基底○・設定? | C | `OnCreate / OnComplete`。C保持：到達済み辞書・段数・連鎖sequenceを持つ |
| `Ai_E_ClutchesOfMalice` | `St_E_ClutchesOfMalice`（参） | root・継続時刻とReachAndPull生成 | 子の移動先抽選 | 基底○・設定? | C | `OnCreateSequenced`。C保持：拘束と子の状態を維持する制御個体 |
| `Ai_E_ClutchesOfMalice_ReachAndPull` | `St_E_ClutchesOfMalice`（名） | 待機後の対象引き寄せ、描画更新 | 移動先の偏差 | 基底○・設定? | C | `OnCreateSequenced / OnDestroyActor`。C保持：対象別の移動・終了callbackを持つ |
| `Ai_E_CrimsonLance` | `St_E_CrimsonLance`（名） | BossへmaxHP加算・会心、trigger破棄とBossDrop | 明示なし | 基底○・設定? | C | `OnEntity`。C保持：firstTriggerの破棄と子StatusEffectの参照関係がある |
| `Ai_E_DoomsdayMeteor` | `St_E_DoomsdayMeteor`（名） | 範囲ダメージ・スタン、判定後に全周の子弾 | 明示なし | 基底○・設定? | B | `OnCreate / OnCollisionCheck / OnHit`。B-R：親の範囲判定後に各方向へ同数の子を生成する。子の親参照を保てない場合は個別公開へ戻す。 |
| `Ai_E_DoomsdayMeteor_SubProjectile` | `St_E_DoomsdayMeteor`（名） | Fireダメージ、基底衝突を継承 | 明示なし | 基底○・設定? | B | `OnEntity`。B-P：全周の各方向・地形衝突・命中履歴を残す。代表の到達先を子全体へ使わない。 |
| `Ai_E_FinalExplosion_Explosion` | `St_E_FinalExplosion`（名）；`Se_E_FinalExplosion`→本型（生、St入口未確定） | 範囲ダメージ・スタン、自傷・Slow、死亡時増幅 | 明示なし | 経路○ | B | `OnCreate / OnHit / OnBeforeDispatchDamage`。B-R：基底開始→自傷の順を変えず、各通知時のcaster生死で増幅を判定する。 |
| `Ai_E_FlameJet` | `St_E_FlameJet`（名） | channelと同期回転、継続Projectile生成 | 明示なし | 基底○・設定? | C | `OnCreate / OnCreateSequenced / OnDestroyActor / ActiveLogicUpdate`。C保持：継続channelと回転状態を持つ |
| `Ai_E_FlameJet_Projectile` | `St_E_FlameJet`（名） | FireとDOT、同casterの命中間隔を制限 | 明示なし | 基底○・設定? | C | `OnEntity`。C保持：対象上のAd_FlameJetとhitTimeを共有する |
| `Ai_E_Harvest` | `St_E_Harvest`（名） | 親の対象StatusEffectを列挙してExplode(this) | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：子が保持する親・個体識別を省く同等性が未確定 |
| `Ai_E_JusticeGuillotine` | `St_E_JusticeGuillotine`（名） | 移動・降下待機後、保存したDamageDataで範囲攻撃 | 明示なし | 基底○・設定? | C | `OnCreateSequenced / OnDestroyActor`。C保持：移動・取消・保存DamageDataの状態を持つ |
| `Ai_E_MassCleanse` | `St_E_MassCleanse`（名） | 味方の状態解除、Unstoppable、解除属性数による回復 | 明示なし | 基底○・設定? | B | `OnCreate`。B-R：各回で現存する状態を列挙し直し、1回目の解除数で後続回復を水増ししない。 |
| `Ai_E_MysticDagger` | `St_E_MysticDagger`（名） | ダメージとAttackEffect、casterのskill CD還元 | 明示なし | 基底○・設定? | B | `OnEntity`。B-P：各実命中でAttackEffectとCD還元を行い、すでに死亡した対象への通知も本体経路に任せる。 |
| `Ai_E_Permafrost` | `St_E_Permafrost`（名） | 味方shield更新、待機、Cold別の敵攻撃とスタン | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：castDelay・postDelay・dazeのsequenceを持つ |
| `Ai_E_SearingCharge` | `St_E_SearingCharge`（参） | 移動中の衝突判定と倍率更新 | 明示なし | 基底○・設定? | C | `OnCreateSequenced / ActiveLogicUpdate`。C保持：displacementと命中状態を持つ |
| `Ai_E_ShadowVolley` | `St_E_ShadowVolley`（名） | critChanceから本数を決め、channel中に時間差射撃 | 明示なし | 基底○・設定? | C | `OnCreateSequenced / ActiveLogicUpdate / OnDestroyActor`。C保持：射撃間隔とchannel中止を維持する発射制御個体 |
| `Ai_E_ShadowVolley_Projectile` | `St_E_ShadowVolley`（名） | 右手付近を始点にDarkダメージ後に破棄 | 明示なし | 基底○・設定? | B | `OnPrepare / OnEntity`。B-P：各始点・方向・途中衝突・命中時刻を残す。連射を同一frameへ前倒ししない。 |
| `Ai_E_SliceThroat` | `St_E_SliceThroat`（名） | channel完了時に内外rangeへダメージと回復 | 明示なし | 基底○・設定? | C | `OnCreate / Finish`。C保持：channelとFinish状態を持つ |
| `Ai_E_StygianRush` | `St_E_StygianRush`（名） | chargeとRush開始、取消・継続時間管理 | 明示なし | 基底○・設定? | C | `OnCreate / OnDestroyActor / ActiveLogicUpdate`。C保持：charge・移動・中止の制御個体 |
| `Ai_E_StygianRush_Rush` | `St_E_StygianRush`（名） | 移動と衝突判定、終了callback | 明示なし | 基底○・設定? | C | `OnCreate / OnCreateSequenced`。C保持：移動中の履歴とchannelを持つ |
| `Ai_E_VileStrike_Damage` | `St_E_VileStrike`（名）；`Se_E_VileStrike`→本型（生、St入口未確定） | 低HP対象への会心・増幅、範囲ダメージとスタン | 明示なし | 経路○ | B | `OnBeforeDispatchDamage / OnHit`。B-R：対象のHP割合を通知ごとに読み、最初の評価結果を後続へ使い回さない。 |
| `Ai_E_WinterDive` | `St_E_WinterDive`（名） | 上昇・降下、描画状態と移動 | 明示なし | 基底○・設定? | C | `OnCreateSequenced / OnDestroyActor`。C保持：sequenceとrenderer復元を持つ |
| `Ai_L_Blizzard` | `St_L_Blizzard`（参） | 上限を管理し、時間差でInstanceを生成 | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：waveIntervalと親の生存確認を持つ。子InstanceだけをB候補にする |
| `Ai_L_Blizzard_Instance` | `St_L_Blizzard`（名） | 基底範囲ダメージ後、味方へ盾 | Adaptive_ShouldPlayEffectsの抽選 | 基底○・設定? | B | `OnAfterDelay`。B-R：波ごとの時刻と敵・味方の別検索、演出抽選による乱数消費を残す。 |
| `Ai_L_BloodStrike_Meat` | `St_L_ButchersStrike`（参）；`Ai_L_ButchersStrike` / `Se_L_ButchersStrike_KillTimer`→本型（生） | 肉の移動・取得・寿命 | 処理・設定依存 | 経路○ | C | `OnCreateSequenced / ActiveLogicUpdate`。C保持：pickupに相当する個別状態を持つ |
| `Ai_L_ButchersStrike` | `St_L_ButchersStrike`（参） | CD reset、命中先KillTimer、死亡時にMeat生成 | 明示なし | 基底○・設定? | C | `OnCreate / OnHit`。C保持：kill通知と個別の子生成・親参照を保つ必要がある |
| `Ai_L_CoinExplosion_Coin` | `St_L_CoinExplosion`（名） | gold消費後に飛来、命中・完了でExplosion生成 | gold量のRandomRoundToInt | 基底○・設定? | C | `OnCreate / OnEntity / OnComplete`。C保持：支出と子生成を個体ごとに保持する |
| `Ai_L_CoinExplosion_Explosion` | `St_L_CoinExplosion`（参） | 範囲ダメージ後、待機して共有damageMultiplier更新 | 待機時間、倍率の当落抽選 | 基底○・設定? | C | `OnCreateSequenced / OnBeforeDispatchDamage / OnHit`。C保持：後続発動へ影響する共有倍率と時差状態を持つ |
| `Ai_L_LightExplosion` | `St_L_LightExplosion`（名） | 対象追跡、予告・爆発・待機、結果で再試行 | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：affected履歴と再試行sequenceを持つ |
| `Ai_L_LightExplosion_Instance` | `St_L_LightExplosion`（名） | 基底範囲ダメージとスタン | 明示なし | 基底○・設定? | B | `OnHit`。B-R：予告側の時刻で各爆発を作り、各爆発の実命中を親へ通知する。 |
| `Ai_L_MentalCorruption_Projectile` | `St_L_MentalCorruption`（名） | 状態stack、死亡監視、二次弾への拡散とCD還元 | 軌道偏差・拡散選択 | 基底○・設定? | C | `OnCreate / OnEntity / OnComplete`。C保持：isSecondary・_didSpread・死亡購読を持つ |
| `Ai_L_Multishot_Arrow` | `St_L_Multishot`（名）；`Se_L_Multishot_Buff`→本型（生、St入口未確定） | strength別ダメージ、任意のAttackEffect、chain | 個体ごとに軌道偏差 | 経路○ | B | `OnCreate / OnEntity`。B-P：strength・triggerAttackEffect・chainと偏差を個体別に保ち、矢の間隔を短縮しない。 |
| `Ai_L_SmallMoltenCore_Dragon_Atk` | `St_L_SmallMoltenCore`（生） | 強化攻撃、周囲爆発と初回相手に応じた回復 | 基底・通知先依存 | 経路○ | C | `OnCreateSequenced / OnEntity / OnDestroyActor`。C保持：caster死亡監視、回転拘束、寿命sequenceを持つ |
| `Ai_L_SoulKiller` | `St_L_SoulKiller`（名） | 固有処理の本文が欠落：OnCreateSequenced / ExplodeSoulsRoutine | 不明 | 基底○・設定? | C | `OnCreateSequenced / ExplodeSoulsRoutine（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_L_SoulKiller_Soul` | `St_L_SoulKiller`（名） | 固有処理の本文が欠落：OnCreateSequenced / ExplodeSoul / OnDestroyActor | 不明 | 基底○・設定? | C | `OnCreateSequenced / ExplodeSoul / OnDestroyActor（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_L_SpectreBullet` | `St_L_SpectreBullet`（名） | ダメージとAttackEffect後、gracePeriodで報酬判定 | 明示なし | 基底○・設定? | C | `OnEntity`。C保持：LockDestroyと遅延報酬・対象参照を持つ |
| `Ai_M_DreamyWaltz` | `St_M_DreamyWaltz`（名） | 回避終了時にBuffExplosion生成 | 基底・設定依存 | 基底○・設定? | C | `OnDestroyActor`。C保持：Ai_GenericDodgeの回避状態を継承する |
| `Ai_M_DreamyWaltz_BuffExplosion` | `St_M_DreamyWaltz`（名） | size別の味方範囲、既存Buffの置換 | 明示なし | 基底○・設定? | B | `OnCreate`。B-R：各範囲を再検索し、selfMultiplierと既存Buffの破棄・再作成を各回で保つ。 |
| `Ai_M_Endure_Shout` | `St_M_Endure`（名） | 固有処理の本文が欠落：OnHit | 不明 | 基底○・設定? | C | `OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_M_FastFeet` | `St_M_FastFeet`（名） | Ai_GenericDodgeの移動・終了処理 | 基底・設定依存 | 基底○・設定? | C | `基底 Ai_GenericDodge`。C保持：回避中の制御状態を継承する |
| `Ai_M_FeatheryDash` | `St_M_FeatheryDash`（名）；`St_R_DangerousTheory`→`Se_R_DangerousTheory`→本型（参・生） | Ai_GenericDodgeの移動・速度設定 | 基底・設定依存 | 経路○ | C | `基底 Ai_GenericDodge`。C保持：回避中の制御状態。DangerousTheoryからの生成も確認 |
| `Ai_M_FlashStep` | `St_M_FlashStep`（名） | Ai_GenericDodgeの移動・終了処理 | 基底・設定依存 | 基底○・設定? | C | `基底 Ai_GenericDodge`。C保持：回避中の制御状態を継承する |
| `Ai_M_FrostyCharge` | `St_M_FrostyCharge`（名） | 回避中に時刻別の衝突処理 | 基底・設定依存 | 基底○・設定? | C | `ActiveLogicUpdate`。C保持：回避と命中間隔の状態を持つ |
| `Ai_M_NimbleDodge` | `St_M_NimbleDodge`（名） | Ai_GenericDodgeの移動・終了処理 | 基底・設定依存 | 基底○・設定? | C | `基底 Ai_GenericDodge`。C保持：回避中の制御状態を継承する |
| `Ai_M_Sprint` | `St_M_Sprint`（名） | 回避開始時にSprint StatusEffect | 基底・設定依存 | 基底○・設定? | C | `OnCreate`。C保持：回避と子StatusEffectの寿命を持つ |
| `Ai_Mon_Sky_BigBaam_BeamAtk` | `St_C_Sneeze`（名）；`Ai_C_Sneeze`→本型（生） | 継続beam、間隔ごとのSphereCastとLight通知 | 明示なし | 経路○ | C | `OnPrepare / OnCreate / ActiveLogicUpdate / OnDestroyActor`。C保持：channel・affected履歴・配列の返却・endDazeを持つ |
| `Ai_Nor_CosmicBlast` | `St_Nor_CosmicBlast`（名） | 固有処理の本文が欠落：OnEntity / OnComplete / Explode | 不明 | 基底○・設定? | C | `OnEntity / OnComplete / Explode（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_Nor_MyOwnLittleNebula_Missile` | `St_Nor_MyOwnLittleNebula`（名） | 固有処理の本文が欠落：OnCreate / PositionSolver / OnEntity | 不明 | 基底○・設定? | C | `OnCreate / PositionSolver / OnEntity（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_QR_DistortedMind_Damage` | `St_QR_DistortedMind`（名） | 基底範囲ダメージとスタン | 明示なし | 基底○・設定? | B | `OnHit`。B-R：生成元が決めた位置・charge由来の値・生成時刻で各判定を行う。 |
| `Ai_QR_DistortedMind_Spawner` | `St_QR_DistortedMind`（名） | chargeと対象予測、Damage子生成 | 生成位置のoffset抽選 | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate`。C保持：chargeと時間差の射撃状態を持つ |
| `Ai_QR_InfernalTales` | `St_QR_InfernalTales`（参） | TickDamageInstanceの周期ダメージ | 基底・通知先依存 | 基底○・設定? | C | `OnCreate`。C保持：継続tickと同期状態を持つ |
| `Ai_QR_Innocence_Explosion` | `St_QR_Innocence`（名） | 属性override、基底範囲ダメージとSlow | 明示なし | 基底○・設定? | B | `OnCreate / OnHit`。B-R：elementalOverride・strength・範囲を個体別に保つ。本体の本数補正を重ねて掛けない。 |
| `Ai_QR_Innocence_Projectile` | `St_QR_Innocence`（名） | 属性・strengthをExplosionへ渡す | 基底軌道、生成元の位置抽選 | 基底○・設定? | C | `OnCreate / OnComplete`。C保持：個体別の子生成と属性参照。今回は子ExplosionだけをB候補にする |
| `Ai_QR_Innocence_Spawner` | `St_QR_Innocence`（名） | 協力人数・perfPressureで本数を絞りstrength補正 | 始点の偏差 | 基底○・設定? | C | `OnCreateSequenced`。C保持：本体に本数・強度補正があり、時間差射撃を管理する |
| `Ai_QR_TwistedTales_Projectile` | `St_QR_TwistedTales`（名） | 固有処理の本文が欠落：OnPrepare / OnEntity | 不明 | 基底○・設定? | C | `OnPrepare / OnEntity（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_QR_TwistedTales_Spawner` | `St_QR_TwistedTales`（名） | 固有処理の本文が欠落：get_clampedCount / OnCreate | 不明 | 基底○・設定? | C | `get_clampedCount / OnCreate（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_QR_ValiantHeart` | `St_QR_ValiantHeart`（名） | attack支出、待機後に距離順のtargetLimitとBuff | 見た目の左右反転 | 基底○・設定? | C | `OnPrepare / OnCreateSequenced`。C保持：支出・待機・初回命中状態を持つ |
| `Ai_Q_BigBorealChunk_Explosion` | `St_Q_BigBorealChunk`（名） | charge・radius・倍率別の範囲ダメージ、会心・スタン | 明示なし | 基底○・設定? | B | `OnCreate / OnBeforeDispatchDamage / OnHit`。B-R：chargeDuration・explodeRadius・damageAmpを個別に残し、範囲を合併・拡大しない。 |
| `Ai_Q_BigBorealChunk_Projectile` | `St_Q_BigBorealChunk`（名） | chargeと半径・倍率を子Explosionへ渡す | 基底・生成元依存 | 基底○・設定? | C | `OnEntity / OnComplete / OnCreate`。C保持：命中・完了分岐と子の同期参照。子ExplosionだけをB候補にする |
| `Ai_Q_BigBorealChunk_Spawner` | `St_Q_BigBorealChunk`（名） | chargeとProjectileへの値の設定 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate`。C保持：charge・channelを維持する発射制御個体 |
| `Ai_Q_CruelSun` | `St_Q_CruelSun`（名） | chargeとShockwaveの形状・属性設定 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate / OnDestroyActor`。C保持：charge・終了・描画更新を持つ |
| `Ai_Q_CruelSun_Shockwave` | `St_Q_CruelSun`（名） | 幅・長さ・charge別の範囲ダメージ、属性・浮かせ・スタン | 明示なし | 基底○・設定? | B | `OnCreate / OnBeforeDispatchDamage / OnHit`。B-R：各width・length・charge・属性で検索し、最大の形状を代表として全通知へ適用しない。 |
| `Ai_Q_DeathMark_Projectile` | `St_Q_DeathMark`（参） | 初回CD還元・amp、完全missでamp減、最後の相手へMark | 基底・生成元依存 | 基底○・設定? | C | `OnEntity / OnDestroyActor`。C保持：hitEntities・初回フラグ・完全miss分岐を持つ |
| `Ai_Q_Discipline_Stomp` | `St_Q_Discipline`（名）；`Se_Q_Discipline_Jump`→本型（生、St入口未確定） | radius・damage・strength別の範囲ダメージとスタン・浮かせ | 明示なし | 経路○ | B | `OnCreate / OnBeforeDispatchDamage / OnHit`。B-R：各radiusMultiplier・damageMultiplierを別に保ち、命中先リストを共用しない。 |
| `Ai_Q_EmbracingTheChill_AreaDoT` | `St_Q_EmbracingTheChill`（名） | 追跡する周期ダメージ、盾と自傷、子爆発 | 基底・通知先依存 | 基底○・設定? | C | `OnPrepare / OnCreate / OnHit / ActiveLogicUpdate`。C保持：無期限duration・追跡・tickを持つ |
| `Ai_Q_EmbracingTheChill_Explosion` | `St_Q_EmbracingTheChill`（名） | size別範囲、casterへの自傷軽減、他へダメージ・スタン | 明示なし | 基底○・設定? | B | `OnPrepare / OnCreate / OnHit`。B-R：自己対象の追加と自傷軽減を各個体で判定し、味方・敵の対象条件を広げない。 |
| `Ai_Q_EtherealInfluence` | `St_Q_EtherealInfluence`（名） | 往復弾、子生成・破棄イベント、rootと爆発 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate / OnDestroyActor`。C保持：子の位置・寿命・再使用状態を持つ |
| `Ai_Q_EtherealInfluence_Projectile_Backward` | `St_Q_EtherealInfluence`（名） | 親へ位置通知、caster到達で破棄 | 基底・設定依存 | 基底○・設定? | C | `基底 Ai_Q_EtherealInfluence_Projectile_Base`。C保持：往復・親子参照を継承する |
| `Ai_Q_EtherealInfluence_Projectile_Base` | `St_Q_EtherealInfluence`（名） | 親へ位置を書き戻し、caster到達で破棄、他へLight通知 | 基底・設定依存 | 基底型。直接生成未確認 | C | `OnCreate / OnEntity`。C保持：親と共有するprojectile位置を持つ。実体生成先は派生型 |
| `Ai_Q_EtherealInfluence_Projectile_Forward` | `St_Q_EtherealInfluence`（名） | 親へ位置通知、完了時にBackward生成 | 基底・設定依存 | 基底○・設定? | C | `OnComplete`。C保持：往復・親子参照を持つ |
| `Ai_Q_Fleche_Dash` | `St_Q_Fleche`（参） | 移動・channel、trackerとrefundList | 明示なし | 基底○・設定? | C | `OnCreate / OnDestroyActor / Finish`。C保持：個別命中履歴と移動状態を持つ |
| `Ai_Q_GoldenBurst` | `St_Q_GoldenBurst`（参） | 自傷、3段sweep、敵Damage・味方Heal、Wither更新 | 明示なし | 基底○・設定? | C | `OnCreate / OnCreateSequenced`。C保持：0.01秒ごとの段階と命中数別増幅を持つ |
| `Ai_Q_HandCannon` | `St_Q_HandCannon`（名） | 射撃に伴うrecoilと移動 | 基底・設定依存 | 基底○・設定? | C | `OnCreate`。C保持：casterのdisplacementと個別状態を持つ |
| `Ai_Q_IncendiaryRounds_Attack` | `St_Q_IncendiaryRounds`（名）；`Se_Q_IncendiaryRounds_EmpowerAttacks`→本型（生、St入口未確定） | 特定attackInstanceの命中購読、Fire通知と任意AoE | 元攻撃・通知先依存 | 経路○ | C | `OnCreate / OnDestroyActor`。C保持：attackInstance SyncVar、イベント解除と個別の攻撃識別を持つ |
| `Ai_Q_Laceration_Circle` | `St_Q_Laceration`（名） | charge斬撃、無敵、移動と複数の終了処理 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate / OnDestroyActor`。C保持：charge・invul・slash状態を持つ |
| `Ai_Q_Laceration_Dash` | `St_Q_Laceration`（名） | dash攻撃と命中処理 | 基底・通知先依存 | 基底○・設定? | C | `OnCreate / OnDestroyActor / OnHit`。C保持：移動・無敵・終了状態を持つ |
| `Ai_Q_Lunge` | `St_Q_Lunge`（名） | 移動完了時の攻撃と演出 | 明示なし | 基底○・設定? | C | `OnCreate / OnDestroyActor`。C保持：displacementとFinish状態を持つ |
| `Ai_Q_MoonlightPact` | `St_Q_MoonlightPact`（名） | Fenrir移動・channel、着地Actor生成 | 明示なし | 基底○・設定? | C | `OnCreate / OnDestroyActor`。C保持：召喚物と移動・飛行状態を持つ |
| `Ai_Q_MoonlightPact_Fenrir_Atk` | `St_Q_MoonlightPact`（名） | 基底範囲ダメージ、召喚者へのAttackEffect通知 | 明示なし | 基底○・設定? | B | `OnCreate / OnHit`。B-R：祖先HeroとsummonerAttackEffectを個体別に解決し、召喚者への通知回数を保つ。 |
| `Ai_Q_MoonlightPact_Land` | `St_Q_MoonlightPact`（名） | 範囲ダメージ・スタン、命中ごとFenrirとcasterを回復 | 明示なし | 基底○・設定? | B | `OnHit`。B-R：各fenrir参照と実命中数で回復し、代表の命中数を全着地へ複製しない。 |
| `Ai_Q_Reduction_Projectile` | `St_Q_Reduction`（名） | 敵へダメージ、味方へ回復 | 生成元に始点の抽選 | 基底○・設定? | B | `OnEntity`。B-P：各ランダム始点・関係判定・途中衝突を残し、生成元の自傷や射撃間隔を変えない。 |
| `Ai_Q_Reduction_Spawner` | `St_Q_Reduction`（参） | 自己HP消費後、時間差でProjectile生成 | 始点の偏差 | 基底○・設定? | C | `OnCreateSequenced`。C保持：自傷とshootIntervalを管理する発射制御個体 |
| `Ai_Q_Riposte` | `St_Q_Riposte`（名） | 固有処理の本文が欠落：OnCreateSequenced | 不明 | 基底○・設定? | C | `OnCreateSequenced（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_Q_SuperNova` | `St_Q_SuperNova`（参） | charge・使用・期限切れ・爆発 | 明示なし | 基底○・設定? | C | `OnCreate / Finish / OnDestroyActor / ActiveLogicUpdate`。C保持：長期のchargeと再使用状態を持つ |
| `Ai_Q_SuperNova_Activate` | `St_Q_SuperNova`（名） | 固有処理はbase後にserverで破棄 | 固有本文に明示なし | 基底○・設定? | C | `OnCreate`。C保持：固有の集約効果が小さく、基底の生成イベントを省ける根拠もない |
| `Ai_Q_SylvanCall` | `St_Q_SylvanCall`（名） | 範囲攻撃・スタン、hound上限処理と召喚 | 召喚rotation | 基底○・設定? | C | `OnPrepare / OnCreate / OnDestroyActor / OnHit`。C保持：古い召喚物のKillと新規召喚・寿命が結び付く |
| `Ai_Q_SylvanCall_LeafHound_Atk` | `St_Q_SylvanCall`（名） | 基底範囲ダメージ、召喚者へのAttackEffect通知 | 明示なし | 基底○・設定? | B | `OnHit`。B-R：各houndの祖先Heroと命中先を残し、別召喚物の反応鎖へ混ぜない。 |
| `Ai_R_AnnihilationStance_Projectile` | `St_R_AnnihilationStance`（名）；`Se_R_AnnihilationStance`→本型（生、St入口未確定） | 背後なら通知を行わず、前方へDarkダメージ | 明示なし | 経路○ | B | `OnCreate / OnEntity`。B-P：isLeft・速度・射程を個別に残し、各命中時の前後判定を通す。 |
| `Ai_R_BackOff_Damage` | `St_R_BackOff`（名） | charge別の扇形範囲ダメージ、会心・スタン | 明示なし | 基底○・設定? | B | `OnPrepare / OnCreate / OnBeforeDispatchDamage / OnHit`。B-R：各chargeから設定された角度・rangeとダメージを残し、最大chargeへ統一しない。 |
| `Ai_R_BackOff_Spawner` | `St_R_BackOff`（名） | chargeから範囲形状を設定してDamage生成 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate`。C保持：charge・終了・移動制御を持つ |
| `Ai_R_BaptismOfSun` | `St_R_BaptismOfSun`（名） | 範囲命中数をBuffへ渡し、停止・AggroとCD reset | 明示なし | 基底○・設定? | C | `OnCreateSequenced / OnCreate / OnHit`。C保持：Buff寿命と命中数の個体状態を持つ |
| `Ai_R_BlackArbalest` | `St_R_BlackArbalest`（名） | 強化・速度変更、自己反動、貫通数別増幅と押し戻し | 強化抽選 | 基底○・設定? | C | `OnPrepare / OnCreate / OnEntity`。C保持：命中数と移動・Dark stackの履歴を持つ |
| `Ai_R_BladeFan` | `St_R_BladeFan`（参） | 固有処理の本文が欠落：ActiveFrameUpdate / OnCreateSequenced / Hit | 不明 | 基底○・設定? | C | `ActiveFrameUpdate / OnCreateSequenced / Hit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_BoneCrusher_Explosion` | `St_R_BoneCrusher`（名）；`Se_R_BoneCrusher`→本型（生、St入口未確定） | 基底範囲ダメージとスタン | 明示なし | 経路○ | B | `OnHit`。B-R：各範囲・遅延・スタンの更新を個体別に通す。 |
| `Ai_R_Cataclysm_Activate` | `St_R_Cataclysm`（名） | 固有処理はbase後にserverで破棄 | 固有本文に明示なし | 基底○・設定? | C | `OnCreate`。C保持：固有の集約効果が小さく、基底イベントの省略も未確定 |
| `Ai_R_Cataclysm_Meteor` | `St_R_Cataclysm`（名）；`Se_R_Cataclysm`→本型（生、St入口未確定） | 範囲ダメージ・スタン、遅延でBurn子を生成 | 演出rotation | 経路○ | C | `OnCreate / OnHit`。C保持：遅延sequence・子TickDamageの参照を持つ |
| `Ai_R_Cataclysm_Meteor_Burn` | `St_R_Cataclysm`（名） | 固有overrideなし、基底の周期ダメージ | 基底の属性処理 | 基底○・設定? | C | `基底 TickDamageInstance`。C保持：TickDamageInstanceの継続状態を継承する |
| `Ai_R_ChainReaction` | `St_R_ChainReaction`（名） | tickダメージ、敵数・Boss別に味方回復 | 基底・通知先依存 | 基底○・設定? | C | `OnCreate / OnDestroyActor / OnCollisionCheck / OnBeforeDispatchDamage`。C保持：継続tick・timerと各回の命中数を持つ |
| `Ai_R_Chomp` | `St_R_Chomp`（名） | 対象を移動させ、完了時にダメージと自己回復 | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：対象のdisplacementと完了callbackを持つ |
| `Ai_R_DancingBlades` | `St_R_DancingBlades`（名） | 近敵追跡、AD/APを選ぶ周期ダメージ、見た目弾 | 基底・通知先依存 | 基底○・設定? | C | `OnPrepare / OnCreate / ActiveLogicUpdate / OnCollisionCheck`。C保持：tick・追跡・属性方式の再評価を持つ |
| `Ai_R_DancingBlades_MockProjectile` | `St_R_DancingBlades`（名） | 固有命中overrideなし、基底delegateと演出 | 基底・設定依存 | 基底○・設定? | C | `基底 StandardProjectile`。C保持：プレハブとdelegate未取得で効果なしとは確定できない |
| `Ai_R_DarkGrenade_Shard` | `St_R_DarkGrenade`（名） | 兄弟の既命中先なら減衰、強化時Stun | 生成元で強化と全周角度の抽選 | 基底○・設定? | C | `OnPrepare / OnEntity`。C保持：兄弟共有hitEntitiesを持つ。今回は共有履歴型を集約しない |
| `Ai_R_DarkGrenade_ShardSpawner` | `St_R_DarkGrenade`（名） | 命中・完了時に全周Shardを生成 | 強化抽選、全周の回転角 | 基底○・設定? | C | `OnCreate / OnEntity / OnComplete`。C保持：兄弟共有HashSetと爆発分岐を持つ |
| `Ai_R_Deception_EndExplosion` | `St_R_Deception`（名）；`Se_R_Deception`→本型（生、St入口未確定） | 固有overrideなし、基底の範囲ダメージ | 基底の属性処理 | 経路○ | B | `基底 InstantDamageInstance`。B-R：終了時点の各範囲を再検索し、別の分身の終了時刻へまとめない。 |
| `Ai_R_Fireball` | `St_R_Fireball`（名） | 固有処理の本文が欠落：OnComplete / Explode | 不明 | 基底○・設定? | C | `OnComplete / Explode（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_FlamingWhip` | `St_R_FlamingWhip`（名） | 基底範囲ダメージとSlow | 明示なし | 基底○・設定? | B | `OnHit`。B-R：各実命中先へダメージ後にSlowを付け、本体の重複・更新規則を残す。 |
| `Ai_R_Frostbite_Freeze` | `St_R_Frostbite`（名） | 固有処理の本文が欠落：OnCreate | 不明 | 基底○・設定? | C | `OnCreate（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_Frostbite_Freeze_Projectile` | `St_R_Frostbite`（名） | 固有処理の本文が欠落：OnEntity | 不明 | 基底○・設定? | C | `OnEntity（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_FrozenFists_Attack` | `St_R_FrozenFists`（名） | 基底近接攻撃、左右の演出切替 | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：MeleeAttackInstanceの攻撃個体とイベントを継承する |
| `Ai_R_FrozenFists_PunchDash` | `St_R_FrozenFists`（名） | Boss増幅、命中ごとの自己盾と押し戻し | 移動先偏差 | 基底○・設定? | C | `OnBeforeDispatchDamage / OnHit`。C保持：DashAttackInstanceとcurrentDisplacementを持つ |
| `Ai_R_GlacialHammer` | `St_R_GlacialHammer`（名） | 中央範囲なら増幅・会心、ランダムな引き寄せ | 距離・時間・移動先の抽選 | 基底○・設定? | B | `OnBeforeDispatchDamage / OnHit`。B-R：各通知時点の中央判定と移動先抽選を残す。先行の引き寄せ後に後続を判定する。 |
| `Ai_R_GreatFrostSword` | `St_R_GreatFrostSword`（名） | 待機後の範囲検索、単体なら会心・増幅、押し戻し | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：daze・animationと時差sequenceを持つ |
| `Ai_R_Ignite` | `St_R_Ignite`（名） | Fire stack別に単体または範囲通知、FakeProjectile生成 | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：子の親参照とDestroyOnDeathを伴う。子を非公開化する条件は未確定 |
| `Ai_R_Ignite_FakeProjectile` | `St_R_Ignite`（名） | 固有命中overrideなし、基底delegateと演出 | 基底・設定依存 | 基底○・設定? | C | `基底 StandardProjectile`。C保持：プレハブとdelegate未取得で効果なしとは確定できない |
| `Ai_R_Immolation_Damage` | `St_R_Immolation`（名）；`Se_R_Immolation`→本型（生、St入口未確定） | caster追従の周期ダメージ | 基底・通知先依存 | 経路○ | C | `OnCreate`。C保持：tick・追従・caster死亡監視を持つ |
| `Ai_R_Inspire_Roar` | `St_R_Inspire`（名） | 味方Buffを生成し既存Buffと合成、CD reset | Buff合成の当落と相手抽選 | 基底○・設定? | C | `OnCreate`。C保持：BuffのgivingTo/receivingFromとDestroyOnDestroyを持つ |
| `Ai_R_NaturesWhisper` | `St_R_NaturesWhisper`（名） | 味方Buff更新、召喚物へ命令、任意teleportと見た目弾 | teleport先の偏差 | 基底○・設定? | C | `OnCreate`。C保持：召喚物の制御・StatusEffect・子参照を扱う |
| `Ai_R_NaturesWhisper_MockProjectile` | `St_R_NaturesWhisper`（名） | 固有命中overrideなし、Point/Target指定の演出 | 基底・設定依存 | 基底○・設定? | C | `基底 StandardProjectile`。C保持：プレハブとdelegate未取得で効果なしとは確定できない |
| `Ai_R_OrbOfLight_BackwardHealing` | `St_R_OrbOfLight`（名） | 途中の味方と完了時casterへhitEnemies比例の回復 | 明示なし | 基底○・設定? | C | `OnCreate / OnEntity / OnComplete`。C保持：Forwardから受け継ぐhitEnemiesと往復状態を持つ |
| `Ai_R_OrbOfLight_ForwardDamaging` | `St_R_OrbOfLight`（名） | Light通知、通常1/Boss3で集計しBackwardへ渡す | 明示なし | 基底○・設定? | C | `OnComplete / OnEntity`。C保持：命中数と往復の子生成を持つ |
| `Ai_R_Parry_Projectile` | `St_R_Parry`（名）；`Se_R_Parry_Start`→本型（生、St入口未確定） | ダメージ、任意のAttackEffect、条件別押し戻し・スタン | 明示なし | 経路○ | B | `OnEntity`。B-P：各実命中時のdash状態とalwaysKnockbackを読み、押し戻しを代表1回に減らさない。 |
| `Ai_R_PhantomBlade` | `St_R_PhantomBlade`（名） | 固有処理の本文が欠落：OnCreateSequenced / OnHit | 不明 | 基底○・設定? | C | `OnCreateSequenced / OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_PhaseShift` | `St_R_PhaseShift`（名） | 対象stun、自身と対象の移動、待機 | 移動先のoffset | 基底○・設定? | C | `OnCreateSequenced`。C保持：双方の移動とsequenceを持つ |
| `Ai_R_PillarOfFlame` | `St_R_PillarOfFlame`（名） | delay後の初回攻撃と後続tickのDOT | 明示なし | 基底○・設定? | C | `OnCreateSequenced`。C保持：tick数・間隔・初回と後続の差を持つ |
| `Ai_R_PrecisionShot` | `St_R_PrecisionShot`（名） | chargeとProjectile生成、取消処理 | 明示なし | 基底○・設定? | C | `OnCreate / OnDestroyActor / ActiveLogicUpdate`。C保持：channel・charge・射程を維持する発射制御個体 |
| `Ai_R_PrecisionShot_Projectile` | `St_R_PrecisionShot`（名） | charge別攻撃、各命中後にamp増加、押し戻し・スタン | 明示なし | 基底○・設定? | C | `OnCreate / OnEntity`。C保持：_currentDmgAmpと貫通履歴を持つ |
| `Ai_R_QuickTrigger` | `St_R_QuickTrigger`（名） | 時間差の方向弾、角度の重複なし組を反復 | 角度の選択順 | 基底○・設定? | C | `OnCreateSequenced`。C保持：channel・fireInterval・角度候補の状態を持つ |
| `Ai_R_QuickTrigger_Projectile` | `St_R_QuickTrigger`（名） | Damage、任意AttackEffect、命中上限で破棄 | 基底・生成元依存 | 基底○・設定? | C | `OnPrepare / OnEntity`。C保持：個体別_currentHitCountを持つ |
| `Ai_R_RepulsiveShield_Projectile` | `St_R_RepulsiveShield`（名）；`Se_R_RepulsiveShield`→本型（生、St入口未確定） | ダメージ、dash中の対象へ押し戻し | 明示なし | 経路○ | B | `OnEntity`。B-P：実命中時のdash状態を個別評価し、非dash対象への押し戻しを追加しない。 |
| `Ai_R_SanctuaryOfEl_Ground` | `St_R_SanctuaryOfEl`（名） | 範囲内Buffを維持、timer | 明示なし | 基底○・設定? | C | `OnCreateSequenced / ActiveLogicUpdate / OnDestroyActor`。C保持：duration・checkInterval・滞在状態を持つ |
| `Ai_R_Scattershot_Projectile` | `St_R_Scattershot`（名） | 散開方向へDarkダメージ、強化・AttackEffect | 生成元で発動ごとに強化抽選 | 基底○・設定? | B | `OnPrepare / OnEntity`。B-P：各扇角と実命中を維持する。方向弾用の鍵を使っても、外れ弾を命中扱いにしない。 |
| `Ai_R_Scattershot_Spawner` | `St_R_Scattershot`（名） | 強化抽選後、同frameに扇状Projectile生成 | 発動単位の強化抽選 | 基底○・設定? | C | `OnCreate`。C保持：発射制御側は残す。方向別の子ProjectileだけをB候補にする |
| `Ai_R_SerpentineBlessing` | `St_R_SerpentineBlessing`（名） | 1〜2対象へBuff、上限と終了待機 | 対象・設定依存 | 基底○・設定? | C | `OnCreateSequenced`。C保持：Buff寿命と0.25秒ごとの監視を持つ |
| `Ai_R_SerpentineBlessing_Projectile` | `St_R_SerpentineBlessing`（名）；`Se_R_SerpentineBlessing_Buff`→本型（生、St入口未確定） | strength別スタンとダメージ、healTargetへの回復 | 明示なし | 経路○ | B | `OnEntity`。B-P：各strength・healTarget・chainを保ち、実命中ごとの回復だけを実行する。 |
| `Ai_R_ShadowOverdrive_Projectile` | `St_R_ShadowOverdrive`（名） | Darkダメージ、強化時に会心と演出 | 強化状態は外部から設定 | 基底○・設定? | B | `OnPrepare / OnEntity`。B-P：各isEmpoweredByCritを保ち、通常弾に強化弾の会心を移さない。 |
| `Ai_R_ShadowWalk` | `St_R_ShadowWalk`（名） | dashとDisappear、待機後に対象を選び射撃 | 強化抽選、候補順と対象抽選 | 基底○・設定? | C | `OnCreateSequenced`。C保持：移動・射撃間隔・Buff寿命を持つ |
| `Ai_R_ShadowWalk_Projectile` | `St_R_ShadowWalk`（名） | 偏差のあるDark弾、AttackEffectと強化会心 | 個体の軌道偏差、生成元の対象抽選 | 基底○・設定? | B | `OnCreate / OnEntity`。B-P：対象・偏差・強化状態を個別に残す。生成元のinitDelayとspawnIntervalを変えない。 |
| `Ai_R_Smite` | `St_R_Smite`（名） | delay後に範囲Lightダメージ、Fire抽選、スタン | 命中対象ごとのfireChance抽選 | 基底○・設定? | B | `OnCreateSequenced`。B-R：各遅延後に再検索し、実命中ごとにFire抽選する。初回の当選を全個体へ使わない。 |
| `Ai_R_Somersault` | `St_R_Somersault`（名） | 無敵・無衝突、channel完了時にCritBonusとspeed | 明示なし | 基底○・設定? | C | `OnCreate`。C保持：channelと完了callbackを持つ |
| `Ai_R_StaticDischarge` | `St_R_StaticDischarge`（名） | charge・範囲変化、Dischargeと演出 | 明示なし | 基底○・設定? | C | `OnCreate / ActiveLogicUpdate / OnDestroyActor`。C保持：generation・callback・charge状態を持つ |
| `Ai_R_SummonLittleBaam` | `St_R_SummonLittleBaam`（名） | Summon生成と親子管理 | 召喚rotation | 基底○・設定? | C | `OnPrepare / OnCreate / OnDestroyActor`。C保持：召喚物の親と寿命を扱う |
| `Ai_R_SummonLittleBaam_Baam_Atk` | `St_R_SummonLittleBaam`（名） | 単体Light通知と基本AttackEffect、beamを0.1秒表示 | 固有本文に明示なし | 基底○・設定? | C | `OnCreateSequenced / OnDestroyActor`。C保持：Damage actorはcasterだが、beam sequenceと生成・破棄イベントが残る |
| `Ai_R_Tranquility_Damage` | `St_R_Tranquility`（名） | 固有処理の本文が欠落：OnCreate / ActiveFrameUpdate / OnHit | 不明 | 基底○・設定? | C | `OnCreate / ActiveFrameUpdate / OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_UnbreakableDetermination_SubExplosion` | `St_R_UnbreakableDetermination`（名）；`Se_R_UnbreakableDetermination`→本型（生、St入口未確定） | 固有効果overrideなし、基底範囲ダメージ。生成元がstrengthとchainを設定 | 基底の属性処理 | 経路○ | B | `基底 InstantDamageInstance`。B-R：各AttackEffect時点のstrengthMultiplier・chain・被害者位置で生成し、その遅延後に別々に再検索する。 |
| `Ai_R_WarBoomerang` | `St_R_WarBoomerang`（名） | 固有処理の本文が欠落：OnComplete / FindNextTarget / sequence | 不明 | 基底○・設定? | C | `OnComplete / FindNextTarget / sequence（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_R_WrathOfEl` | `St_R_WrathOfEl`（名） | 固有処理の本文が欠落：OnCreate / OnHit | 不明 | 基底○・設定? | C | `OnCreate / OnHit（IL）`。C欠落：元ILのret-only。空実装や効果なしとは解釈しない |
| `Ai_U_BeamOfBalance_Beam` | `St_U_BeamOfBalance`（名）；`Se_U_BeamOfBalance`→本型（生、St入口未確定） | beam範囲へ敵Damage・味方Heal | 明示なし | 経路○ | C | `OnPrepare / ActiveLogicUpdate / Hit`。C保持：HitRecord辞書とhitIntervalを持つ |
| `Ai_U_BigChomp` | `St_U_BigChomp`（名） | 命中数とBoss係数で回復・盾・CD還元 | 明示なし | 基底○・設定? | C | `OnCreate / OnBeforeDispatchDamage / OnHit / OnAfterDelay`。C保持：個体別の命中数とAfterDelay状態を持つ |
| `Ai_U_Burrow_Emerge` | `St_U_Burrow`（名）；`Se_U_Burrow`→本型（生、St入口未確定） | casterのdaze、基底範囲ダメージとスタン | 明示なし | 経路○ | B | `OnCreate / OnHit`。B-R：各出現のdaze・遅延・範囲・スタンを本体と同じ順に通す。 |
| `Ai_U_HerWorld_Explosion` | `St_U_HerWorld`（名） | 基底範囲ダメージとスタン | 明示なし | 基底○・設定? | B | `OnHit`。B-R：各爆発時点の範囲を再検索し、初回の対象を後続へ固定しない。 |
| `Ai_U_Hysteria_Claw` | `St_U_Hysteria`（名）；`Se_U_Hysteria`→本型（生、St入口未確定） | dash、二段検索、初回AttackEffect、命中ごとの回復 | 明示なし | 経路○ | C | `OnCreate / OnCollisionCheck / OnBeforeDispatchDamage / OnHit`。C保持：初回フラグ・duplicate trackerと移動を持つ |
| `Ai_U_ShoutOfOblivion` | `St_U_ShoutOfOblivion`（名） | 脅威度別増幅・会心、範囲スタン、casterの後退 | 明示なし | 基底○・設定? | B | `OnCreate / OnBeforeDispatchDamage / OnHit`。B-R：各通知時点の脅威度を読み、各発動の後退を省略・合算しない。 |
| `Ai_U_WorldCracker` | `St_U_WorldCracker`（名） | channel中のbeam、命中間隔と周期演出 | 基底・通知先依存 | 基底○・設定? | C | `OnPrepare / OnCreate / OnDestroyActor / ActiveLogicUpdate`。C保持：hitTimes辞書・cast購読・channelを持つ |
| `Sum_L_SmallMoltenCore_Dragon` | `St_L_SmallMoltenCore`（生） | 召喚物AI、AttackLink、強化時刻、移動 | 移動時刻・位置 | 経路○ | C | `OnCreate / AIUpdate / OnDestroyActor`。C保持：HP・AI・個別寿命と親子関係を持つ |
| `Sum_Q_MoonlightPact_Fenrir` | `St_Q_MoonlightPact`（生） | 固有overrideなし、SummonのHP・AI・寿命 | 基底・設定依存 | 経路○ | C | `基底 Summon`。C保持：召喚物の継続状態を継承する |
| `Sum_Q_SylvanCall_LeafHound` | `St_Q_SylvanCall`（名） | 召喚物、hatの同期、攻撃設定 | hat抽選 | 基底○・設定? | C | `OnPrepare / OnCreate`。C保持：召喚物のHP・AI・寿命を持つ |
| `Sum_R_AnnoyingBanner` | `St_R_AnnoyingBanner`（生） | pulse、敵Aggro、Unstoppable | 明示なし | 経路○ | C | `OnCreate / ActiveLogicUpdate`。C保持：duration・tickとAI制御を持つ |
| `Sum_R_SummonLittleBaam_Baam` | `St_R_SummonLittleBaam`（名） | target再選択、AIUpdate、Unstoppable | 基底・設定依存 | 基底○・設定? | C | `OnCreate / AIUpdate`。C保持：召喚物のHP・AI・寿命を持つ |

## 8. 集約方法と適用条件

### 8.1 Aの候補を確定しなかった理由

`Ai_D_QuartetOfDeath_Explosion.OnCreate` は `base.OnCreate`、serverで単体にPhysical/Darkダメージ、`Destroy` の順である。`currentStack * GetValue(amountPerStack)` を個体ごとに計算して通知する形はAに近い。しかし、`AbilityInstance.CreateDamage` は `SetActor(this)` を設定し、通知はその個体と親を通る。`Actor.DealDamage` は通知ごとに双方のprocessor、armor、shield、immunity、AttackEffectを評価し、actorをlastAttackerや各イベントにも渡す。最終ダメージだけをN回使い回す方法では再現できない。

さらに、`Dew.CreateAbilityInstance` は生成元のBeforePrepare通知、個体のbeforePrepare、OnPrepareとSpawn、生成元のCreated通知をこの順で呼ぶ。`Actor.OnCreate` の通知も祖先へ伝わる。追加分をActorなしの数値へ変換すると、個体を引数にした購読処理や生成・破棄の意味が変わる。今回の資料ではその同等性を説明できないので、この型はCとした。DeathMarkごとに生成元や対象も違い、既存と同じcreator/source/targetの鍵では同じ束にならない可能性が高い。

`Ai_R_SummonLittleBaam_Baam_Atk` はDamageのactorをcasterへ設定し直すが、beam、0.1秒のsequence、基底の生成・破棄通知が残る。これもA確定とはしない。

Aを将来実装するなら、追加分ごとの元値・対象・caster・親・skillLevel・chain・通知順序・個体識別の扱いを確定し、その通知時点で本体のDamage/Heal経路を各1回通す必要がある。乱数は追加通知にも個別に行い、N倍の1通知にしない。対象や購読、callback、個体参照を同等に扱えない条件では個別生成へ戻す。現時点でAの実体削減を見積もる型はない。

### 8.2 Bの非強化規則

Bの56型には、表の規則に加えて次の共通条件を課す。代表個体の命中を追加分へ複製する集約は採らない。

1. **サーバでは全N個を本体どおり生成する。** 各個体のOnPrepare、OnCreate、更新、軌道、衝突、範囲検索、時刻、通知、親子イベント、破棄とプール返却を維持する。個体のnetIdとDamage/Healのactorも残す。
2. **通知と乱数の順序を維持する。** 個別の乱数状態や通知値を代表へ合わせず、元の呼び出しを同じ回数・同じ順序で実行する。ゲストの見た目を減らすためにホスト側の乱数呼び出しを省かない。FX用の抽選も同じ乱数列を使う箇所では消費を維持する。
3. **空間・時刻を共有しない。** 飛翔弾は各軌道の実命中だけを通知する。範囲攻撃は各本体処理の時点で再検索する。後続波・遅延・射撃間隔を前倒しせず、先行通知による死亡、移動、shieldや状態の変更を後続へ反映する。
4. **変更はゲストへ公開する個体数に限定する案とする。** 同じframe・同じ表示用の鍵で1〜4個目は通常公開、5個目を表示代表、6個目以降を非公開候補とする。代表は5個目自身の処理だけを行い、非公開分のゲーム効果を代理しない。公開済みの個体を後から隠して生成済み数を削減したと数えない。
5. **適用できない条件では本体へ戻す。** 型の入口・受け口のIL形状、プレハブの構成、親子参照、クライアント通知の役割を説明できない型は登録しない。容量超過や鍵不足、ホスト不在、例外時にもその個体を通常公開し、型別の機能だけを止めて理由を記録する。

この条件がすべて成立すれば、サーバ上のゲーム効果の分布は本体と一致する。期待値の比は1であり、代表への置換による上振れも意図的な弱体化も加えない。この根拠は個別のゲーム処理を変えないことにある。**条件の成立は未確認であり、56型を適用可能と確認したという結論ではない。** プレイヤーが見える情報を減らす影響も別に残る。

サーバの個体まで減らす当初の方式に対して、これはゲストの生成公開だけを減らす代案である。サーバのActor数・衝突判定の軽量化は得られない。新たな軌道シミュレーターや仮想Actorで同等性を再実装する設計へは進めていない。サーバ個体を減らすことが必須なら、Bの実装方法は未確定として扱う。

### 8.3 Mirrorでの差し込み候補と未解決点

現行 `Mirror.dll` の `NetworkServer` と `NetworkIdentity` を個別に取り出した。`NetworkServer.SpawnObject` はisServer/isClient、netId、spawned辞書、OnStartServerを設定し、その後 `RebuildObservers(identity, initialize: true)` を呼ぶ。`Actor.OnStartServer` は準備を確定してactiveにする。一方、ActorのOnCreateは `OnStartClient` またはactive変更のhookから呼ばれる。したがって、単に `NetworkServer.Spawn` を飛ばす方法は使わない。

差し込み候補は、全個体を通常Spawnしたまま、非公開候補だけのobserver追加を制限する方法である。`NetworkIdentity.AddObserver` はconnectionのobservingへ追加する。`NetworkServer.BroadcastToConnection` はその集合を使ってSyncVarを送り、ClientRpcもidentityのobserverへ送る。初回生成以外に、途中参加の `SpawnObserversForConnection`、AOIによる再構築、明示的なShowにも対応が必要になる。1か所のSpawnMessage送信を抑えるだけでは、その後に未知のnetIdへの更新・RPCが届く状態になる。

次の点を解決するまでは、Bの生成公開の削減を有効にしない。

| 項目 | 差し込み・保持すべきこと | 個別公開へ戻す条件 |
|---|---|---|
| 初回公開 | beforePrepare後の型・値を記録し、初回observer追加より前に公開区分を確定する。サーバのSpawnとnetId取得は実行する | 生成順序や受け口の形を確認できない |
| ローカルホスト | localConnectionを通常経路へ通し、OnStartClient、OnCreate、乱数、親イベントと更新登録を本体どおり実行する | ローカル接続のない専用サーバ、またはローカル初期化を保持できない |
| リモートゲスト | 初回、途中参加、AOI再構築、Showのすべてで非公開候補を追加しない。owner authorityが必要な個体は対象外 | 所有者への同期やCommand/TargetRpcが必要、別経路の再公開を防げない |
| 親・子・参照 | 公開するActorから非公開候補へのparentActor、CastInfo、SyncVarその他の参照がないことを型・プレハブ・購読先で確認する | StatusEffectを含む子が非公開親を参照する、ほかの公開個体がnetIdを参照する。代表への親の付け替えでは代用しない |
| ゲスト側イベント | 省かれるOnCreate/OnDestroyedと祖先通知が表示以外を変えないことを確認する | ゲスト側の状態・購読・子管理がゲーム処理に使われる、本文が欠けて役割を判断できない |
| RPC | 非公開個体のRPCが表示だけかを確認する。公開Entityや別managerからのFX送信は別に扱う | RPCが状態を変える、別の公開RPCが非公開netIdを参照する |
| 再利用・破棄 | 本体の破棄とプール返却を維持し、公開区分とframe集計の記録だけを寿命に合わせて解除する | 再利用後に前の非公開区分が残る、すでに公開した分の扱いが不明になる |
| 失敗時 | 未適用の生成は通常公開する。型の登録を停止し、既存のゲーム処理を継続する | すでに非公開の個体に外部参照が生まれる危険を事前に除外できない |

`Visibility.ForceHidden` だけを設定する案は採らない。初期observerと途中参加の両方に作用し、ローカルホストの初期化まで変えるおそれがある。ホスト側のrendererを一括で止める案も、スクリプトが見た目の状態を参照する可能性を確認するまで対象外にする。

親子参照の問題は範囲攻撃にもある。`CreateBasicEffect` や `CreateStatusEffect` が命中元を親にした子を公開する場合、非公開の命中元をゲストで解決できない。該当する分岐がある型は、親を代表に差し替えず型全体を通常公開する。ゲストの生成数の上限を優先して、親や反応の意味を変えない。

### 8.4 frame、鍵、容量、見た目

表示用の鍵は型、生成元creator/source、caster、対象指定のmodeとtarget、開始位置、向き、設定の版を含める。targetなしのDirection/Pointはそのmode・方向・終点で区別する。既存Offerのtarget必須条件のままではScattershotなどの方向弾を受け取れない。開始位置はまず厳密に区別し、1cm丸めを同等性の根拠にしない。方向や対象の異なる個体を同じ表示代表へまとめる場合の情報損失も、別に判断する必要がある。

ダメージ、strength、charge、属性、強化状態はサーバの元個体に残す。表示代表にも必要な区別は鍵へ入れる。代表から通知値を再構成しない。frameが変われば新しい束とし、casterやcreatorが違う生成を合算しない。時間差生成の総数を同frameのNへ加えない。

frameごとの辞書・リスト・クロージャを新規作成しない。型登録時に確保する固定容量の記録を再利用し、容量超過分は通常公開する。容量の実値は今回の資料から決めない。既存の `NativeProcCoalesce.Offer` にそのまま渡すとサーバ個体も省かれるため、B用の公開制御を既存4型の処理と分ける必要がある。

見た目の上限は、各frame・各鍵で通常4個＋代表1個とする。root transformの拡大、Colliderやrangeの拡大、速度変更、ダメージ増幅による補償は行わない。プレハブ内の純粋な描画用transformを確定できるまでは、既存の1.15〜1.75倍のscale補償も転用しない。異なるframeの代表は同時に生存し得るため、この5個は部屋全体の同時表示数の上限ではない。

## 9. ゲスト側の削減見積もり（推定）

### 9.1 共通の式

Nは、同frame・同じ鍵で、適用条件を満たした1型の生成数とする。B案が成立すれば、サーバの生成はN個のまま、ゲストの生成は `min(N, 5)` 個、削減は `max(N - 5, 0)` 個である。Nが5以下なら削減は0。型や対象、creator/source、方向、frameで束が分かれるので、記憶1回の総本数から直接は求めない。

| 1束のN | ゲスト生成数 | 生成削減の推定 | 削減率の推定 |
|---:|---:|---:|---:|
| 1〜5 | N | 0 | 0% |
| 6 | 5 | 1 | 16.7% |
| 10 | 5 | 5 | 50% |
| 20 | 5 | 15 | 75% |
| 100 | 5 | 95 | 95% |

これは**適用条件が成立した束のActor生成数だけ**の推定であり、全通信量、FPS、粒子数の改善率ではない。条件不成立の型や生成の削減は0。実装済みの削減も今回の変更による削減も0である。Spawnの初期SyncVar、以後の差分SyncVar、RPC、公開された子、managerからのFX、各端末の粒子を別々に数える必要がある。

### 9.2 型別の条件と効果の上位候補

ここでの順位は読めた生成形態と個体状態の少なさから付けた調査・実装候補の順で、実測による負荷順位ではない。表7.3のすべてのB型にも9.1の式を適用する。以下は本数の根拠が読めた代表例と、効果が0になりやすい条件である。

| 優先 | 型 | 本文から読めた本数・生成形態 | ゲスト削減の推定と制約 |
|---:|---|---|---|
| 1 | `Ai_D_SalamanderPowder_Projectile` | 反応ごとの弾。各弾に独立の偏差がある | 同frame・同target・同creator/source等にN本集まれば `max(N-5,0)`。個別軌道を残す案なので命中の損失は加えない。頻度・同時本数は未測定 |
| 2 | `Ai_E_ShadowVolley_Projectile` | `RoundToInt(GetValue(baseShootCount)*(1+critChance))` をduration内に時間差で生成 | 連射総数をNとしない。通常は各frameの本数が少なく削減0もある。同frameの重複発動が同じ鍵へ集まった分だけ式を適用 |
| 3 | `Ai_R_Scattershot_Projectile` | 同frameの扇状本数 `RoundToInt(GetValue(shootCount)) + 強化時addedShootCount` | 厳密な方向の鍵では通常の1扇は方向別に分かれて削減0。方向が同じ重複発動の各束でN>5なら削減。全扇を1命中へ置換した大きな削減率は採らない |
| 4 | `Ai_QR_Innocence_Explosion` | 生成元はcount上限8＋offset、協力人数別上限30/20/10とperfPressure補正後の弾を時間差生成 | 補正後の弾が到達して同frame・同位置等に集まった爆発数をNとする。上限30をNとして固定しない。strength補正を再度掛けない |
| 5 | `Ai_L_Blizzard_Instance` | waveCount回、waveIntervalごと。複数の親発動が重なる余地がある | 波ごとに別frameなら各波は別束。親creator/sourceが異なれば別束。同じ鍵のN>5という条件を満たす部分だけ削減 |
| 6 | `Ai_E_DoomsdayMeteor_SubProjectile` | 各親からsubMeteorCount本を全周生成 | 方向と親が違えば分割されるため、通常の1全周だけでは削減0。親参照・子を扱う条件の解決が先 |
| 7 | `Ai_C_Purgatory_Heal` | 実命中した敵ごとに通常1本、Boss3本、HP重みで回復先を抽選 | 異なる回復先・始点なら別束。各束のN>5分だけ削減。敵数×3を同一鍵のNとして扱わない |
| 8 | `Ai_C_Starfall_Projectile` | baseCount回、intervalごとに再検索と重み付き対象選択 | 間隔・対象で分割されるので削減0もある。複数発動が同じframe・同じ鍵へ集まる場合だけ式を適用 |
| 9 | `Ai_D_ChargedAnguillian_Lightning` | maxStack時にintervalごと、ランダム順の別対象へ各1本 | 1回の対象一覧はtargetごとに別束になり通常削減0。同じ対象への重複反応だけが候補 |
| 10 | `Ai_L_Multishot_Arrow` | firstArrowDelay後、arrowIntervalごとの弾 | 時間差の総本数を束にしない。strength・chain・creatorも分かれるため、削減0の可能性がある |
| 対象外 | `Ai_D_QuartetOfDeath_Explosion` | markごとの単体通知。即時破棄 | Cなので削減0。仮にAへ昇格しても、target・creator/sourceの違いで束が分かれる |
| 対象外 | `Ai_R_DarkGrenade_Shard` | defaultShardCount＋強化追加、ランダム回転の全周弾 | Cなので削減0。初期値7＋4をプレハブ実値や採用削減率にしない |
| 対象外 | `Ai_D_SharedPain_Instance` / `Ai_E_ChainLightning` | 時差で次候補を決めて連鎖 | Cなので削減0。連鎖総段数を同frameのNへ置き換えない |

適用できる実際の束の分布がないため、全182記憶を通した削減率や、最も負荷が高い記憶は確定しない。現時点で保証できる削減は0であり、表の数値はすべて条件付きの推定である。

## 10. 実装の束

1束を1回のワーカー作業とする。束0は12節のとおり実装した。ほかの束のコード作業は未実行。束0の条件を満たさないまま、後続の型を有効にしない。束0の条件を満たさないまま、後続の型を有効にしない。型の条件が成立しなければ、その型だけ通常公開を維持して次の候補へ進む。

| 順序・束 | 目的と範囲 | 受け入れの例 | 境界の例・してはいけないこと |
|---|---|---|---|
| 0：公開制御の成立確認 | `Dew.CreateAbilityInstance`、Actorの生成・破棄、Mirrorの初回・途中参加・再構築・Show経路を対象に、8.3の差し込みを確定する。実装する場合は対象型の登録なしで固定容量のframe集計と公開区分の寿命管理までを1作業にする | 全Actorは本体どおりSpawnされ、非登録型は全公開。対象外・専用サーバ・容量不足では通常公開に戻る。非公開から参照される子やクライアントイベントの条件を型登録前に判断できる | Spawnの省略、ローカル初期化の省略、公開済み個体の後付け非公開化、全型への一括適用、既存4型の意味変更をしない。未解決なら成立しない箇所を報告し、型を有効にしない |
| 1：反応弾1型 | `Ai_D_SalamanderPowder_Projectile` の登録、偏差とfireStack評価の維持。ゲスト公開以外を変更しない | 同frame・同鍵のN>5だけ公開を絞り、各サーバ弾は独立の偏差・実命中・通知を維持する。条件不成立なら削減0 | 軌道を代表へ合わせない。Fire状態やダメージ値を初回から再利用しない。子StatusEffectやゲストイベントの条件を未確認で有効化しない |
| 2：単純な射撃通知 | `Ai_E_ShadowVolley_Projectile`、`Ai_R_ShadowOverdrive_Projectile`、`Ai_D_ChargedAnguillian_Lightning`。共通の衝突処理を利用し、生成元channelは触らない | 始点・強化状態・実命中を各個体に残す。対象違いと時間差は別束。IL形状が違う型だけ登録を停止する | 連射を同frameにする、別対象を代表の対象へ替える、強化会心を通常弾へ移すことをしない |
| 3：方向弾1型 | `Ai_R_Scattershot_Projectile`。Directionモードを含む鍵と同方向の重複だけを扱う。Spawnerは変更しない | 扇角と発動単位の強化抽選を維持する。通常の1扇で削減0になることを許容する | 扇の端の外れ弾を中心弾の命中にする、全扇を1束にして削減率を大きく見せる、向きの丸めだけで安全とすることをしない |
| 4：時間差の範囲子 | `Ai_L_Blizzard_Instance` と `Ai_L_LightExplosion_Instance`。親のsequenceは維持する | 各波・爆発の時刻で敵・味方を再検索し、shieldや親への通知を本体順に行う。生成する状態効果の親を保てない型は通常公開する | 初回の対象一覧を使い回す、後の波を前倒しする、再試行の親を1個に置き換えることをしない |
| 5：属性付きの範囲子 | `Ai_QR_Innocence_Explosion` と `Ai_Q_BigBorealChunk_Explosion`。属性・charge・radius・strengthの設定を個体に残す | 本体のcount/strength補正は1回だけ。各個体の形状で検索し、子や公開参照の条件不成立なら通常公開する | 最大の範囲や倍率へそろえる、ダメージを本数倍する、親弾やSpawnerまで同時に省くことをしない |
| 6：残りのBを型別に追加 | 表7.3のB規則を1型または同じ受け口の小群ずつ1作業として適用する。Purgatory_Heal、Starfall、Multishot、その他の範囲型を、実際にN>5が発生する見込みと参照の少なさで選ぶ | 各作業で対象型、保持する値・イベント、通常公開へ戻す条件を明記する。効果が0の型を無理に集約しない | B全56型を一括登録しない。Cの状態持続型・本文欠落型を便乗して追加しない |
| 別作業：Aの再判定 | `Ai_D_QuartetOfDeath_Explosion` の個体識別・購読・生成破棄通知を調べ、Aへ移せるかだけ判断する | 個別通知と個体識別を同等にできる根拠がある場合だけ後続実装を提案する | 「単体通知に見える」ことだけでAへ変更しない。現在のC分類のままなら削減0と報告する |

束1〜5の順序は、反応による多重生成の余地と、保持する個別状態の少なさを優先した暫定順である。束0の公開制御は影響範囲が広く、少数の直接通知弾を先に確認してから範囲型へ広げる。本文欠落25型、連鎖、召喚物、継続channel、共有履歴、遅延報酬の型は今回の実装束に入れない。

## 11. 今回の検証と未完了事項

### 11.1 調査で実行した確認

- `/tmp/memcoalesce-decomp` の候補名を再集計し、同名Ai206型、Summon5型、命名例外2型の重複なし213型と照合した。分類表もA0＋B56＋C157＝213型である。
- 表の25型すべてについて、元ILの型ブロックに固有の命中・生成等のret-onlyメソッドがあることを再確認した。コンストラクタとMirror生成の空メソッドだけを理由に欠落扱いしていない。
- 現行 `Dew.Core.dll` からProjectile、StandardProjectile、Actor、AbilityInstance、DamageInstance、InstantDamageInstance、TickDamageInstance、Dew、AttackProjectile、SkillTriggerを個別に取り出した。各コマンドは終了コード0。Actor生成順、基底衝突・範囲検索、通知のactorと親への伝播を読んだ。
- 現行 `Mirror.dll` からNetworkServerとNetworkIdentityを個別に取り出し、終了コード0を確認した。Spawnからobserver初期化への順、途中参加、Show、observer経由のSyncVar/RPCを読んだ。プレハブやゲーム内挙動の確認には代えない。
- Stからの生成呼び出しと、型検査・親参照・名前だけの対応を分けて集計した。反射型一覧と182ファイルの一致、全体逆コンパイルの終了コード70と23件の復元エラーは前回記録の値を引き継ぐ。

- 文書の分類表を候補集合と照合し、213行・重複0・各7列・A0/B56/C157の一致を確認した。前回の停止記録の本文がそのまま残ることと、変更対象が指定の仕様書1ファイルだけであることも確認した。
- `git diff --check` は指摘なし。`yomiyasu_lint.py` はerror 0・warn 0・info 2。候補集合と確定生成数の違いを示す否定、A/B/Cを並列に定義する箇条書きは、意味と読み分けに必要なので残した。

### 11.2 未完了・未実行

- **Bのサーバ個体を減らす設計は未完了。** 表の非強化規則は個別判定保持であり、ゲスト公開を減らす代案である。Mirrorの差し込み候補は見つかったが、型別の公開参照・ゲストイベントをすべて解決したわけではない。実装可能と確定した型数は0で、削減の実績も0。Aの確定型も0。
- **全182記憶の生成経路は未完了。** コードで対応を説明できた28件と、名前だけの候補対応を区別した。生成元欠落でも実行時集計は可能だが、未確認の対応や頻度が確定するわけではない。
- **プレハブ・asset bundleの検査は未実行。** 資料に含まれておらず、基底の軌道設定、Collider、range、描画用transform、追加NetworkBehaviour、イベント配線の実値は未確認。本文が読めない25型の元の処理も復元していない。
- **MODのコード変更・ビルド・テストは未実行。** 今回は仕様書だけを変更した。新しいテストの設計・作成は依頼範囲外として行っていない。後続実装の結果不変の確認はconstitutionの条件に従う。束0の実装はこのあと12節のとおり行った。
- **実機・協力プレイ・専用サーバ・途中参加の確認、Profiler、生成数・SyncVar・RPC・粒子の実測は未実行。** この環境ではゲームを起動できない。実機確認だけのIssueは作らない。

文書の分類表・型別規則・条件付き推定・実装束は作成したが、サーバ実体の集約方法とB案の適用可能性の確定は残っている。この追記を、そのまま56型の有効化を許す完成仕様として使わない。

## 12. 束0の実装（2026-10-20）

`src/SodRpg.Mod/NativeGuestPublishLimit.cs` に実装した。型の登録表は空のまま起動するため、すべての Actor は今までどおり Spawn され、全接続へ公開される。ゲーム効果も見た目も変わらない。既存の `NativeProcCoalesce`（エッセンス4型）には触れていない。設定は `DreamforgeConfig.nativeGuestPublishLimit`（既定ON）で、`NativePerfPatches` の `NativePerfOptions.GuestPublishLimit` へ反映する。設定の配線に合わせて、tests/Issue73.Native.Tests の DreamforgeConfig 模倣（NativeEmbracePositionBoundary.cs）へ同じ名前のフィールドを足した。

### 12.1 パッチ先

現行 `Mirror.dll` と `Dew.Core.dll` の逆コンパイルを読んで決めた。どの経路でも Spawn とローカルホスト側の初期化（OnStartClient・OnCreate）は省かない。

| パッチ先 | 位置 | 役割 |
|---|---|---|
| `NetworkServer.SpawnObject(GameObject, NetworkConnection)` | Prefix | 生成時の区分け。`Dew.CreateAbilityInstance` は parentActor と info を設定してから Spawn するため、この時点で鍵に必要な値が揃い、同じ呼び出し内で後に出る初回の observer 追加より前に区分が確定する |
| `NetworkIdentity.AddObserver(NetworkConnectionToClient)` | Prefix | AOI を使わない初回公開（RebuildObserversDefault）と、途中参加（SpawnObserversForConnection） |
| `DewInterestManagement.OnRebuildObservers(NetworkIdentity, HashSet<NetworkConnectionToClient>)` | Postfix | AOI の初回・再構築。`InterestManagement.Rebuild` は AddObserver を通らず observers を組み直すため、返しの集合から非公開候補へのリモート接続を除く |
| `NetworkServer.ShowForConnection(NetworkIdentity, NetworkConnection)` | Prefix | 明示的な Show。SpawnMessage を送る前に同じ判定を通す |
| `NetworkServer.DestroyObject(NetworkIdentity, DestroyMode)` | Postfix | 区分の解除。破棄とプール返却（UnSpawn・Reset）のどちらもこの経路を通る |

初回・途中参加・再構築・Show の4経路は、同じ `AllowObserver(identity, connection)` で判定する。ローカルホストの接続は常に通常どおり公開する。AddObserver のみを抑えて AOI を放置すると、SpawnMessage が送られないまま observers へ加わった接続へ、ゲストの知らない netId への更新が届く。このため OnRebuildObservers の集合の整理を必須の蓋とし、どれかのパッチ先が取り付かないときは機能全体を登録拒否にして通常公開へ戻す（Prepare で対象を探し、見つからなければ警告を1回出す）。DestroyObject の DestroyMode は private のため、名前と第1引数の形で探す。

### 12.2 区分けと解除

- 鍵は8.4のとおり、型、creator/source（parentActor）、caster、対象指定（target の有無と point・angle）、開始位置、向き、設定の版。位置と向きは丸めずに float の値そのもので比較し、NaN・Infinity が混ざれば鍵が作れないものとして扱う。
- フレーム集計は固定容量512の開放アドレス表を、フレームの刻印で使い回す。フレームごとに辞書・リスト・クロージャは作らない。1〜4個目は通常公開、5個目は表示代表、6個目以降は非公開候補で、各フレーム・各鍵の公開は5個まで。
- 非公開候補の区分は NetworkIdentity を鍵とする弱参照表に置き、DestroyObject の Postfix で必ず外す。プールで再利用した個体に前の区分は残らない。
- 権限を持つ接続のある個体（ownerConnection が null でない）と、非公開候補を親に持つ子は通常公開する。子だけの通常公開では親の netId が未解決になるため、14節の処理で親も先に通常公開する。

### 12.3 失敗時の動作

鍵が作れない、枠を使い尽くす、専用サーバ（NetworkServer.localConnection がない）、判定中の例外、のいずれかでは、該当する個体を通常公開し、その型の登録を外して警告を1回出す。専用サーバと、型が定まらないままの例外は全型の登録を外す。判定に失敗しても Spawn は妨げず、MOD全体は止まらない。登録0件の現在は判定の入り口で毎回返るため、どの場面でも何も起きない。

### 12.4 型の登録前の検証

`NativeGuestPublishLimit.TryRegister` を用意した。機械的に確かめる条件は AbilityInstance の具象サブクラスであること、Summon を継承しないこと、宣言している ClientRpc・Command・TargetRpc が表示だけだと申し立てられていること。登録要求は初回生成のプレハブ監査まで保留する。`referencesReviewed` は Spawn 時の SyncVar 以外の参照・RPC・ゲスト通知を型と購読先で審査したという申し立てで、未確認なら要求を拒否する。プレハブ構成と Spawn 時の参照を実行時に確認する方法は14節に記す。条件を満たさない型は登録せず、理由を文字列で返す。この時点での呼び出し元はない。

### 12.5 検証と未確認

`python3 tools/balance/gen_cs.py --check` 合格。`dotnet build src/SodRpg.Mod -c Release` はエラー0・警告6件（既存）。`dotnet test tests/Issue73.Native.Tests -c Release` は536件合格。`dotnet test tests/SodRpg.Mod.Startup.Tests -c Release` は225件合格・1件スキップ（既存の環境依存。この2つには DOTNET_ROLL_FORWARD=LatestMajor を指定）。`python3 tools/test_changed.py --base HEAD` は3650件合格・0失敗（DOTNET にシステムの dotnet を指定。作業ツリーの変更だけを対象。既定の比較元では dev 取り込み分の balance 入力も含まれ全走査になるため、全体はパイプラインに残す）。実機・協力プレイ・専用サーバ・途中参加の確認と、生成数・通信量の実測は未実行。changelog は、登録0件で挙動が変わらないため書いていない。

## 13. 束1の判定

2026-10-22、`Ai_D_SalamanderPowder_Projectile` は登録しないと判定した。12.4の参照審査と、8.3のゲストイベントの条件をすべて確認できなかったためである。コードと登録表は変更せず、削減は0のままとする。同frame・同鍵で6個以上生成されても全個体を通常公開する。

### 13.1 確認した条件と根拠

ゲーム資料の `decompiled/sod-decomp` と `reflection-dump` に加え、現行の `Dew.Core.dll` と `Dew.Contents.dll` から関係型を `ilspycmd -t` で個別に取り出して読んだ。個別抽出はすべて終了コード0。抽出先は `/tmp/sod-memco-b1-evidence` であり、逆コンパイルしたコードはリポジトリに含めない。

| 登録条件 | 判定と根拠 |
|---|---|
| AbilityInstanceの具象型で、Summonを継承しない | 確認済み。対象型 → StandardProjectile → Projectile → AbilityInstance → Actorの継承で、対象型はabstractではない |
| 宣言しているClientRpc・Command・TargetRpcが表示だけ | 対象型の固有宣言は0件。継承したProjectileの4つのClientRpc実体もFXの再生・停止処理。ただしFXプレハブ自体の内容は未確認 |
| 公開ActorのparentActor・CastInfo・SyncVarから参照されない | 審査未完了。CastInfoのcasterとtargetはEntity型で、対象弾を直接保持しない。一方、Actor.parentActorは同期されるActor参照であり、通知の購読先と追加コンポーネントを含む参照の不存在は確認できていない |
| 子StatusEffectなどが対象弾を親にしない | 審査未完了。通常のFire付与は祖先Entity（見つからなければserverActor）を親にするが、ダメージ通知から別の子を作る経路は排除できていない。詳細は13.2に記す |
| ゲストの生成・破棄と祖先への通知が表示以外を変えない | 審査未完了。対象型の固有OnCreateは偏差抽選と基底呼び出しで、固有の破棄処理はない。ただし基底ActorのOnCreateは祖先のClientActorEvent_OnCreateまで通知し、OnDestroyActorはClientActorEvent_OnDestroyed、親のchildren、ActorManagerを更新する。全購読先とプレハブの配線を含めて表示以外への影響がないとは確認できない |
| 偏差とfireStackの評価がサーバの各個体で行われる | 確認済み。OnCreateで個体ごとにRandom.insideUnitSphereを抽選し、PositionSolverがその個体の偏差を加える。Projectile.LogicUpdateはサーバ側だけで衝突判定を進め、対象型のOnEntityが命中時のfireStackを読み、会心・増幅を判定してDispatchする。初回の値を使い回していない |

偏差抽選はサーバ限定ではなく、ゲスト側のOnCreateでも行われる。束0はローカルホストの公開と初期化を残す設計だが、これだけでリモートゲストの初期化や通知を省略してよいとは判断しない。サーバ個体の生成・軌道・命中・ダメージ・Fire・通知・乱数順序と、既存のNativeProcCoalesce（エッセンス4型）には変更を加えていない。

### 13.2 未確認の参照と資料の不足

対象弾のDamageData.actorはAbilityInstance.CreateDamageのSetActor(this)により弾自身になる。DispatchからActor.DealDamageを経て、InvokeOnDealDamageがparentActorを辿って購読先へ通知する。St_D_SalamanderPowderからのCreateAbilityInstanceでは、この記憶が生成親になる。

GemのOnEquipSkillはサーバ側で装備先SkillTriggerのActorEvent_OnDealDamageを購読する。Gem_E_FeverとGem_R_WoundのOnDealDamageには、条件を満たしたときにCreateStatusEffectWithSource(info.actor, ...)を呼ぶ処理がある。この呼び出しはsource.CreateStatusEffectを通り、Dew.PrepareAndSpawnStatusEffect_Impが生成するStatusEffectのparentActorにsourceを設定する。したがって、その購読が対象弾の祖先に成立する場合は、公開される子が弾を親として参照する経路になる。

ただし、今回確認したHeroSkill.GetSkillとTryGetSkillの装備先はQ・W・E・R・Identity・Movementである。これらのGemの購読がSt_D_SalamanderPowderまたはその祖先で成立する経路までは確認できず、対象弾からこの子が実際に生成されるとは断定しない。通常のFire付与だけを調べて「子の親参照なし」と申し立てることもできない。

提供資料には対象プレハブ・asset bundleがなく、追加NetworkBehaviour、Collider、軌道設定、onEntityなどのイベント配線、FXプレハブの処理を確認できない。reflection-dumpの型・メンバー一覧では、これらの実値を代わりに確認できない。12.2の「非公開候補を親に持つ子は通常公開する」という防御も、ゲストが非公開親のnetIdを解決できない問題を解消しない。親の付け替えや通知の省略で条件を成立させる変更は行わない。

### 13.3 束1で実行した検証と未実行

- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `dotnet build src/SodRpg.Mod -c Release`：エラー0・警告6件。GameDirに `/home/wang/dev/sod-gamedata/Shape of Dreams`、ModDeployDirに `/tmp/sod-memco-b1-build-deploy` を指定した。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests -c Release`：696件合格・失敗0・スキップ0。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Mod.Startup.Tests -c Release`：233件合格・失敗0・1件スキップ。既存のModReloadTests.SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatchが、Release時のJITインライン化を理由にSkip指定されている。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py --base HEAD`：終了コード0。今回の仕様書1ファイルだけを対象にし、関連テストの選択は0件だった。12.5の3650件という過去の結果とは別である。
- `yomiyasu_lint.py docs/specs/memory-projectile-coalesce.md`：error 0・warn 0。今回の否定表現は、偏差抽選をサーバ限定と誤読しないために残した。`git diff --check`も指摘なし。

プレハブ・asset bundleの検査は、提供資料にないため未実行。実機・協力プレイ・専用サーバ・途中参加での確認、同frame・同鍵で6個以上生成する場面の再現、生成数・通信量・粒子の実測は、ゲームを起動できないため未実行。全体の遅いテストスイートも今回の指定範囲外として未実行とし、パイプラインに残す。実機確認だけのIssue、新しいテスト、changelog.dの断片は作成していない。

## 14. 実行時の安全網

ゲームのアセットがこのマシンにないため、プレハブの構成と、公開される子から非公開候補への参照を資料だけでは確かめられない。`NativeGuestPublishLimit` に実行時の監査と親の繰り上げ公開を足した。登録要求・監査済みの型はともに0件のままで、既定のゲーム効果と表示は変わらない。12節までの構成審査と生成時の参照確認は、この節の方法で補う。

### 14.1 プレハブの取得と監査

根拠は `Dew.CreateAbilityInstance<T>(Vector3, Quaternion?, Actor, CastInfo, Action<T>)` である。本体は `DewResources.GetSuggestedResourceLoadSettings(parent, typeof(T))` と `DewResources.GetByType<T>` でプレハブを引き、`Dew.InstantiateActor_Imp<T>` から `SpawnManager.Create` へ渡す。`GetByType(Type, ResourceLoadSettings)` は `DewResources.database.typeToGuid` からアセットを読み、型の Component を返す。

`TryRegister` はプレハブの GameObject を任意の引数で受け取り、省略時は初回生成で `DewResources.GetByType(type)` から取得する。戻り値 true は要求の受理を表し、有効な登録数にはまだ加えない。最初の `NetworkServer.SpawnObject` で監査に合格してから登録を有効にする。不合格なら保留も解除し、理由を型ごとに1回警告する。失敗結果もキャッシュするため、再登録や次の生成で監査を繰り返さない。

親の variant processor を再実行すると通知や乱数の順序を変えるおそれがあるため、監査では実行しない。実際に使ったバリエーションは `SpawnManager.activeInstanceToPool` と `pool` の対応から取得する。型の元プレハブ、実際のプレハブ、初回の生成個体を監査する。プールを使わず生成元を特定できない場合も登録しない。以後は同じプレハブと pool の対応だけを確認し、別のバリエーションへの変更や対応の消失では型の登録を止める。

監査は非アクティブな子を含むすべての Component を調べる。許可するのはルートの対象型1個と NetworkIdentity 1個に加え、Transform・RectTransform、Renderer の各派生型、MeshFilter、ParticleSystem・ParticleSystemRenderer、TrailRenderer、LineRenderer、Light、AudioSource、Animator、Collider・Rigidbody である。Collider と Rigidbody はサーバ側の判定を維持したまま許可する。許可リスト外の部品、欠落したスクリプト、2個以上の NetworkBehaviour は拒否する。

Component の UnityEvent と、直列化された値・配列・リスト内の UnityEvent に永続リスナーがあれば拒否する。継承元のフィールドも調べ、監査中の例外や過度な入れ子でも登録しない。配列、訪問済み集合、反射による調査は初回監査だけで確保し、フレームごとの再監査は行わない。

### 14.2 親の繰り上げ公開

`NetworkServer.SendSpawnMessage(NetworkIdentity, NetworkConnection)` の Prefix で、リモートの接続へ送る Actor の参照先を、payload を作る前に確認する。登録済みの型だけでなく StatusEffect など未登録の子も対象である。Actor の SyncVar を継承元まで調べ、Actor・NetworkBehaviour・NetworkIdentity・GameObject への参照と値型内の参照を読む。`Actor._parentActor`、`AbilityInstance._info` の caster・target、`_gem` などもこの方法で確認する。`GeneratedNetworkCode._Write_CastInfo` が caster・target を NetworkBehaviour として書くことも確認した。読み取り用 delegate は Actor の型ごとにキャッシュし、生成ごとの反射や boxing を避ける。

非公開候補の親とその祖先は子の SpawnMessage より先に通常公開する。送る Actor の参照先に非公開候補か繰り上げ済みの個体がある場合だけ、この処理を行う。参照を祖先から順にたどり、非公開候補の区分を外して、準備済みの全リモート接続へ `NetworkIdentity.AddObserver` を呼ぶ。本体の `AddToObserving → ShowForConnection → SendSpawnMessage` を通るため、identity.observers と connection.observing の両方へ加わる。未準備の接続は通常の途中参加経路で扱う。繰り上げた親は以後通常公開し、途中参加で子が先に列挙された場合も同じ確認で親を先に送る。区分は破棄・プール返却時に解除する。

読み取れない SyncVar の形、非公開個体同士の循環、参照の深さや件数の上限、送信処理の例外では、新しい公開制限を止める。残る非公開候補も通常公開へ戻す処理を試み、失敗は警告する。子の Spawn とサーバのゲーム処理、ローカルホストの初期化は止めない。親を代表個体へ付け替えることもしない。

### 14.3 代わりに確認できる条件と残る審査

プレハブの構成と永続イベントの配線は、実行中の本物を初回監査して確認する。生成時に同期される親子参照は、子の送信前に参照先を公開して解決する。資料にアセットがないことを理由に、監査を省いて登録することはない。

Spawn 後に変更される参照、別の公開個体の RPC が渡す netId、独自の同期形式、購読先のゲストイベントが表示以外の状態を変えないことは、実行時の2つの処理だけでは確認できない。これらは引き続き型と購読先のコード審査を必須とし、`referencesReviewed` と RPC の表示専用確認を省かない。本文が欠けた型を有効にする根拠にも使わない。

### 14.4 検証と未実行

- `python3 tools/balance/gen_cs.py --check` は合格。MOD の Release ビルドは実ゲームの DLL を参照し、エラー0件・既存の警告6件で成功した。
- `Issue73.Native.Tests` の全件一括実行は689件合格・7件失敗。失敗はすべて `GenericTransformIdlePerfTests` の初期化で、`NativeGenericTransformIdlePerf.TryPrepare("GenericTransformSync")` が false を返した。7件だけの実行では合格し、一括実行では同じ失敗が再現した。原因は未確定で、今回の範囲外のコードとテストは変更していない。
- `SodRpg.Mod.Startup.Tests` の全件実行は233件合格・1件スキップ・失敗0件。両テストには `DOTNET_ROLL_FORWARD=LatestMajor` を指定した。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py --base HEAD` は3,887件合格・8件スキップ・失敗0件。860クラスを14プロセスに分けて検査し、Issue73 も分割実行では696件すべて合格した。全件一括の失敗が解消したとは扱わない。
- 実行中のプレハブ監査による拒否・警告1回の観測、ゲストに届く親子の SpawnMessage の順序、協力プレイ・途中参加・専用サーバの確認、Profiler と生成数・通信量の実測は未実行。このマシンにはゲームのアセットと実行環境がない。リポジトリ全体の追加のテストはパイプラインに残す。

登録要求と有効登録は0件のままである。実機確認だけの Issue と、新しいテストファイルは作成していない。

## 15. 束1の再判定

2026-10-23、`Ai_D_SalamanderPowder_Projectile` の登録を再検討し、引き続き登録しないと判定した。固有のクライアント処理の本文は読めたが、別の公開個体の RPC が弾自身を参照する経路があり、14節の安全網では保護されない。`referencesReviewed=true` の条件を満たさないため、起動時の `TryRegister` 呼び出しは追加しない。同フレーム・同鍵で6個以上生成されても全個体を通常公開し、5個への削減は行わない。

### 15.1 型とクライアント処理の確認

現行の `Dew.Contents.dll` から対象型を `ilspycmd -t Ai_D_SalamanderPowder_Projectile` で個別に抽出し、`Dew.Core.dll` の継承元も読み直した。対象型の個別抽出と Core 全体の抽出は終了コード0だった。Contents 全体の抽出は一部の別型で復元に失敗し、終了コード70となったため、全体を確認済みとは扱わない。以下の判断には、本文を読めた型とメソッドを用いた。抽出先は `/tmp/sod-memco-b1r-evidence` で、逆コンパイルしたコードはリポジトリに含めない。

| 処理・条件 | 確認した内容 |
|---|---|
| 型・RPC・サーバ処理 | 13節で確認した AbilityInstance の具象型、Summon 非継承、固有 RPC なし、各個体の偏差抽選、命中時の fireStack 評価を前提とした |
| 固有の `OnCreate` | `Random.insideUnitSphere * deviateMag` を個体の `_deviateVector` に入れ、負の y を反転してから基底を呼ぶ。本文は `ret` だけではなく、独自の購読・子管理・通知はない。抽選はゲストでも行う |
| 固有の `PositionSolver`・`OnEntity` | 前者は個体の軌道に偏差を加える。後者は基底の命中通知後に Fire ダメージを作り、命中時の fireStack で会心・増幅を判定して Dispatch し、自身を破棄する。衝突と OnEntity の呼び出しは Projectile.LogicUpdate のサーバ側処理にある |
| 固有の破棄処理 | `OnDestroyActor` などの独自 override はない。StandardProjectile、Projectile、AbilityInstance、Actor の基底処理を通る |
| 基底の生成・破棄 | StandardProjectile は軌道と速度、Projectile は軌道・位置・回転と飛翔 FX、AbilityInstance は開始・終了 FX と sequence を扱う。Actor は生成時に祖先の `ClientActorEvent_OnCreate` を呼び、親の `children`・`childrenInNetwork` と ActorManager の一覧に登録する。破棄時は自身の `ClientActorEvent_OnDestroyed` を呼び、一覧から外す。基底まで表示処理だけだとは申し立てない |
| 生成元と生成通知の購読先 | St_D_SalamanderPowder はサーバで生成し、対象弾へ独自の beforePrepare callback を渡さない。確認した ACH_MASTER_OF_HAMMER と Se_R_AnnihilationStance の生成通知は別の弾型だけを処理する。St_D_CircleOfLife、St_D_HeartOfThePack、Se_Star_Nachia_L_AllyUnstoppable の該当購読はサーバ側で、対象弾の型にも該当しない。これだけで全購読先の安全性が確認できたとはしない |
| 継承した Projectile の4つの表示 RPC | `RpcPlayOnEntityEffect`、`RpcPlayOnDissipateEffect`、`RpcStopOnFlyEffect`、`RpcPlayOnCompleteEffect` の受信本文は FX の再生・停止処理。次節の ClientEventManager の通知 RPC は別経路である |

### 15.2 公開 RPC からの弾参照

非公開弾が命中した場合、公開された ClientEventManager からその弾の参照が届く経路を確認した。

1. 対象弾の `Damage(...).Dispatch(victim)` は、弾自身を `DamageData.actor` に設定して `actor.DealDamage` を呼ぶ。MOD の `HostAuthority.AuthoredKeystonePackets` は Dispatch を包むが、actor を別の公開個体へ置き換えない。
2. `Actor.DealDamage` は `actor = this` の `EventInfoDamage` を作り、被害 Entity の `EntityEvent_OnTakeDamage` を呼ぶ。また `SetLastAttacker(this)` で弾を記録し、致死時の `Entity.Kill` はその参照を `EventInfoKill.actor` として `EntityEvent_OnDeath` に渡す。
3. ClientEventManager はサーバでこの2つの Entity イベントを購読し、`InvokeOnTakeDamage` と `InvokeOnDeath` の ClientRpc を送る。`GeneratedNetworkCode._Write_EventInfoDamage` と `_Write_EventInfoKill` は、いずれも `writer.WriteNetworkBehaviour(value.actor)` で弾自身の netId と部品番号を書く。弾自身の observer を減らしても、公開された manager の通知は送られる。
4. ゲストに弾が生成されていなければ、Mirror の `ReadNetworkBehaviour<Actor>` は参照を解決できず null を返す。ClientEventManager の受信本文は victim だけを確認して通知し、actor を復元しない。
5. `DewPersistentGameObserver.AchOnKillLastHit` は actor が null の通知を除外する。購読先の例である `ACH_M_M_M_MONSTER_KILL` は、通知された actor または祖先の討伐数を更新し、`childrenInNetwork` を使って集計する。したがって非公開化によって、表示以外の通知・集計結果が変わる経路がある。実績の達成可否や具体的な集計差は実機で未確認だが、通知を除外する分岐は本文から確認できる。

14.2の `PreparePublication` は SendSpawnMessage の前に送信対象 Actor の SyncVar を読む処理で、これらの RPC payload の参照先は調べない。公開される子が生成されて弾を参照すれば親を繰り上げられるが、すべての命中でその子が先に生成される保証はない。子の生成を伴わない命中・死亡通知の安全性を、親の繰り上げ公開で代替することはできない。既存の `NativeEnemyHitDamageScope` も受信通知の FX 用スコープを設定するだけで、弾の公開や参照の復元は行わない。

### 15.3 実行時に確認する範囲と残る条件

プレハブの部品構成・永続 UnityEvent は14.1の初回監査に委ね、公開される子の Spawn 時の親参照は14.2の繰り上げ公開に委ねる。この2点は登録を見送る理由にはしていない。ただし、これらは15.2の別個体の RPC 参照を保証しない。

今回の判断では、全購読先と ActorManager の全消費側の審査完了も申し立てない。再び登録を検討するには、別の公開個体が送る RPC の弾参照と、ゲスト側の通知・一覧の省略が表示以外を変えないことを確認する必要がある。RPC の保護拡張や親の付け替えは今回の範囲に含めない。

コード・登録表・設定の配線は変更しない。サーバの Spawn・個体数・軌道・偏差・命中・ダメージ・Fire・通知・乱数順序、14節の監査と繰り上げ公開、既存のエッセンス4型と束0の判定にも変更はない。changelog.d と新しいテストファイルは追加しない。

### 15.4 今回の検証と未実行

- `python3 tools/balance/gen_cs.py --check`：終了コード0。入力と生成済みファイルに差はない。
- `/usr/bin/dotnet build src/SodRpg.Mod -c Release`：成功、エラー0件・警告6件。GameDir は `/home/wang/dev/sod-gamedata/Shape of Dreams`、ModDeployDir は `/tmp/sod-memco-b1r-build-deploy` を指定した。
- `DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet test tests/Issue73.Native.Tests -c Release`：705件合格・失敗0件・スキップ0件。14.4に記録した7件の失敗も今回は起きなかったが、過去の失敗原因を解消したとは扱わない。
- 同じ環境指定による `SodRpg.Mod.Startup.Tests` の全件実行：237件合格・失敗0件・1件スキップ。スキップは既存の `ModReloadTests.SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch` で、Release 時の JIT インライン化を理由に Skip 指定されている。
- 同じ環境指定による `SodRpg.Mod.AutoCast.Tests` の全件実行：19件合格・失敗0件・スキップ0件。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py --base HEAD`：終了コード0。今回の仕様書1ファイルを対象にし、関連テストは0件だった。既定の比較元は `--list` で確認したところ41ファイルの差分とビルド定義の変更を含み、892クラスの全件実行を選んだ。全体のスイートを避ける指示に従い、既定の比較元での実行は未実行とした。

実行中のプレハブ監査と親の繰り上げ公開、協力プレイ・途中参加・専用サーバ、同フレーム・同鍵で6個以上生成する場面の再現、RPC の受信と実績への影響、生成数・通信量・粒子の実測は未実行。このマシンにはゲームのアセットと実行環境がない。リポジトリ全体の追加のテストと遅いスイートも未実行で、パイプラインに残す。

## 16. 発生源の参照の調査

2026-10-23、公開された別個体から非公開弾を参照する通信と、ゲストの受け手を調べた。現時点では案Cを推奨する。ダメージ弾への適用を見送り、束0の基盤を登録0件で残す。型・生成元・caster・owner が一致しても、個体に保存した集計値、子孫集合、具象型の状態、生成通知の時点まで一致するとは限らない。案Bは元個体の参照を保ちやすいが、後からの公開で補えない処理があり、命中弾などの追加公開によって削減も減る。

この節の案A・B・Cは既存の型分類A・B・Cとは別である。案Aは通信中の参照を同フレーム・同鍵の公開済み代表へ差し替える案、案Bは参照される元弾を通信より先に公開する案、案Cはダメージ弾への適用を取りやめる案を指す。サーバの全個体・命中・ダメージ・通知・乱数を維持し、ゲストへの公開だけを減らすという前提で比較した。今回はA・Bを実装していない。

### 16.1 資料と確認範囲

`/home/wang/dev/sod-gamedata/decompiled/sod-decomp` の Dew.Core・Dew.UI・Mirror と、提供された Managed 内の `Dew.Contents.dll`、MOD の `src/SodRpg.Mod` を用いた。根拠は以下に挙げる型・メソッドの本文と、生成された serializer・受信ハンドラである。逆コンパイルした本文はリポジトリに含めない。

Contents は `/tmp/memco-rpcref-contents` に抽出した。全体抽出は終了コード70で、23メソッドが復元エラーになった。出力済みの必要な型は読めたが、全体の復元成功や全受け手の審査完了とは扱わない。復元失敗には `Ai_D_FrostTouch_FrostProjectile.OnComplete`、`Ai_R_Fireball.Explode`、`Se_M_Endure.OnCreate` などが含まれる。

### 16.2 発生源を送る経路

Mirror の `WriteNetworkBehaviour` は netId と ComponentIndex を書く。`ReadNetworkBehaviour<T>` は、その netId が受信側の spawned に無ければ null を返す。送信元 manager・Entity・Hero の observer と、payload に入る弾の observer は別である。弾の公開を抑えても、別個体からの通知はそのまま送られる。

| 送信元・payload | 書かれる参照と発生源 | ゲストの受信条件・注意点 |
|---|---|---|
| ClientEventManager（以下CEM）`InvokeOnTakeDamage` / EventInfoDamage | actor、victim。DamageData の actor は弾の `Damage` から `this` が設定される | victim だけを確認し、actor=null でも OnTakeDamage を呼ぶ。damage 内の値と発生源の参照は別 |
| CEM `InvokeOnDeath` / EventInfoKill | actor、victim。`SetLastAttacker(this)` から死亡時の actor へ引き継ぐ | victim だけを確認し、actor=null でも OnDeath を呼ぶ |
| CEM `InvokeOnTakeHeal`・`InvokeOnTakeManaHeal` / EventInfoHeal | actor、target、ReactionChain 内の Actor リスト。Actor の回復処理は発生源を `this` にする | target だけを確認する。actor の欠落で通知全体は落ちない |
| CEM `InvokeOnTakeShield` / EventInfoShield | statusEffect、target、ReactionChain 内の Actor リスト。直接の actor 欄はない | statusEffect と target の両方を確認する。非公開弾は chain や StatusEffect の親を通じて関係する |
| CEM `InvokeOnGetManaSpent` / EventInfoSpentMana | actor、entity | entity だけを確認する |
| CEM `InvokeOnAttackMissed`・`InvokeOnAttackHit` | actor、attacker、victim | victim だけを確認する。attacker と発生源 actor を混同しない |
| CEM `InvokeOnDamageNegated`・`InvokeOnDamageNegatedByShield` | actor、victim。前者には BasicEffect 欄もある | victim だけを確認する。この版の BasicEffect writer は空、reader は null で、本体のフィールドは送られない |
| CEM `InvokeOnApplyElemental` / EventInfoApplyElemental | actor、victim、属性・追加スタック。Actor の付与通知は actor=`this` | actor と victim の両方を確認するため、actor=null なら OnApplyElemental 自体が呼ばれない |
| CEM `InvokeOnCastComplete` / EventInfoCast | instance、trigger、configIndex、CastInfo | instance と trigger の両方を確認する。CastInfo の caster・target は Entity で、候補の AbilityInstance 弾を直接入れる欄ではない |
| Entity `NotifyEntityDeathToClients` / EventInfoKill | CEM と同じ actor、victim | 受信で virtual な OnDeath(info) を呼ぶ。EntityEvent_OnDeath を再発火する経路とは別 |
| Hero `RpcInvokeOnKillOrAssist` / EventInfoKill | CEM と同じ actor、victim | ClientHeroEvent_OnKillOrAssist を呼ぶ。actor=null を除外しない |
| Se_HeroKnockedOut `RpcInvokeKnockedOut` / EventInfoKill | Hero の `_lastAttacker` から actor を設定 | ClientHeroEvent_OnKnockedOut に payload を渡し、CEM.OnHeroKnockedOut には Hero を渡す。後者だけを見て発生源参照が無いとは言えない |

これらの CEM 戦闘RPCは channel 0、includeOwner=true で送る。Actor の生成物には ElementalStatusEffect などもあり、属性ダメージの発生源が常に元弾になるわけではない。個々の発生源設定と親の選択を確認する必要がある。

`GeneratedNetworkCode._Write_EventInfoHeal` と `_Write_EventInfoShield` は `_Write_ReactionChain` を通じて `_actors` の各 Actor を送る。EventInfoDamage の chain は、調査対象の同 serializer では送られない。ReactionChain は型だけの判定に加え、`DidReact` の個体一致判定、`Equals` の要素と順序の比較を持つ。代表に置き換えても型が同じというだけでは、これらの意味を保てない。ただし今回読んだゲスト購読者で、受信した chain を具体的に消費する箇所は未確認である。

#### SyncVar と生成後の参照変更

| フィールド・書き込み先 | 候補弾との関係 | 読み手への影響・確認範囲 |
|---|---|---|
| `Actor._parentActor` | 任意の Actor を参照でき、公開された子が非公開弾を親にできる | 祖先探索、firstEntity、子の一覧、生成通知に関係する。14.2は Spawn 時の参照先公開を扱うが、Spawn 後の変更を扱わない |
| `AbilityInstance._info`・`_gem` | CastInfo は caster・target が Entity、Gem は生成元の系統に関係する | caster・target 自体が候補弾になる欄ではない。構造体内参照は受信時に読み取られ、後からの自動解決と同一視できない |
| `Ai_Q_IncendiaryRounds_Attack.attackInstance` | AbilityInstance 型の SyncVar で、弾を直接参照できる | OnCreate で Projectile なら飛翔FXを追加する。参照先が未公開ならこの処理が落ちる。後から getter で解決しても OnCreate は再実行されない。対象記憶弾と実際に組み合わされるかは未確認 |
| `StatusEffect._victim`、`AbilityTrigger._owner`・currentPassiveEffect、`Entity._owner`、攻撃trigger、`Projectile._targetEntity`、Gem の owner 等 | Entity・DewPlayer・StatusEffect・AbilityTrigger 等の型制約がある | これらすべてを「弾を直接参照する欄」とは扱わない。公開された StatusEffect や trigger の親参照は別途確認する |

専用の NetworkBehaviour SyncVar getter は保持した netId から後で解決できる。ただし受信時の OnChanged hook や OnCreate まで再実行する保証はない。`ActorManager.serverActor` も Actor 参照を同期するが、通常のダメージ弾を指すという根拠にはしない。

#### DewEffect の全員送信

RPC以外にも、`DewNetworkBehaviour.FxPlayNetworked` 等から `DewEffect` の NetworkMessage を送る経路がある。PlayEffectMessage、PlayPositionedEffectMessage、PlayAttachedEffectMessage、PlayCastEffectMessage、StopEffectMessage、ApplySpeedMultiplierToEffectMessage は、parent の NetworkIdentity と pathId を送る。送信は `NetworkServer.SendToAll` で、弾自身の observer を使わない。既定は channel 0、sendToReadyOnly=false である。

受信側の `HandleEffectMessage` は parent=null ならログを出して戻る。`ResolvePathById` は parent の gameObject、fxPathTable、Transform 階層を使う。再生処理は assetId、skin variant、他プレイヤー向けの調整も読む。通常再生の isNew は解決したFXの Transform 位置・回転を使い、位置指定の再生はメッセージの position・rotation を使う。停止と速度変更は、その親個体の特定FXを操作する。

案Aで parent を代表にすると、FXの配置・状態・停止対象まで同じとは限らない。明示位置を維持できる再生と、個体の状態を操作する停止を一括で同値とは扱えない。案Bでこれらの参照も公開条件にすると、外れ弾でもFXを親に持つ通信によって公開され得る。RPCだけの保護では、この経路は残る。

### 16.3 ゲストの受け手が読む情報

表の「非依存」は本文を読んだハンドラの発生源参照に限る。未読の購読先まで影響なしとする意味ではない。caster は主に CastInfo、owner は firstEntity 等から読み、生成元は parentActor・祖先探索で調べる。これらを一つの「同じプレイヤー」という条件へまとめない。

| 分類・受け手 | 参照先から読む情報 | 非公開化・案Aの判断 |
|---|---|---|
| 実績：`DewPersistentGameObserver.AchOnKillLastHit`・AchOnDealDamage・AchOnDoHeal | actor の存在、local Hero との同一性・子孫関係 | actor=null で callback が除外される。同じ祖先と帰属を持つ代表なら、この絞り込み自体は保ち得る |
| 実績：`ACH_M_M_M_MONSTER_KILL` | actor・parentActor の個体、AddData/TryGetData/RemoveData の tracker、childrenInNetwork、CastComplete の instance と通知時点 | 個体または祖先へ討伐数を保存し、instance 以下を走査して16体以上か判定する。型・親・caster・owner の一致だけでは一般の同値を証明できない。同じ root tracker に集約される場合まで必ず誤集計するとは断定しない |
| 実績：`ACH_ETERNAL_REST`・`ACH_THE_CYCLE_OF_REBIRTH`・`ACH_TASTE_OF_THEIR_OWN_MEDICINE` | AttackProjectile/MeleeAttackInstance、特定ボスなどの型と祖先型 | 同じ型・祖先を受信時まで保てる代表なら成立し得る。actor=null では helper の除外や型判定が変わる |
| 実績：`ACH_THEYRE_JUST_BIG_CATS` | victim の型、actor の ILightingActor 実装または `Ai_Q_Fleche_Dash.empoweredWithLightning` | 具象個体の強化状態を読む例。現在の鍵にこの SyncVar は入らない。ただし Fleche Dash が今回の対象束に含まれるという確認ではない |
| 実績：`ACH_GO_WITH_THE_FLOW`・`ACH_MASTER_OF_EVASION` | actor.firstEntity、Monster 型、owner、敵対関係 | 前者の被ダメージ callback は actor の null guard を持たず、後者は持つ。通知が届くだけでは同じ結果にならない |
| 実績：`ACH_PRAISE_THE_SUN`・`ACH_SHIELD_BATTERY`・`ACH_OVERFLOWING_HEALS` | firstEntity、生存、owner、敵対関係、祖先 `Se_R_DangerousTheory` 等。属性・量は payload も読む | 帰属と祖先を保つ必要がある。Overflowing Heals の callback に actor の null guard はない |
| 実績：`ACH_TRUE_SUPPORT`・`ACH_HORNS_FEATHERS_AND_GOLD`・`ACH_SHOOT_FOR_THE_MOON` | helper の発生源帰属、回復先個体・体力・量、victim と Hero の位置 | True Support の時間辞書は target 個体をキーにする。Shoot for the Moon の距離は元弾の位置ではない。payload の victim・target は変更しない |
| 実績：`ACH_BARE_SKIN_SUPREMACY` | statusEffect の祖先、target、finalAmount | actor 欄だけの対応では足りず、公開された StatusEffect の親系統も保つ必要がある |
| 実績：`ACH_THOUSAND_THUNDERS`・`ACH_ANGER_MANAGEMENT_PROFESSIONAL`・`ACH_SIBLING_FIGHT` | 読んだ処理は attacker/victim、会心・強度・免疫・関係、victim に保存する時刻 | actor に非依存。attacker/victim と通知を保つことが前提 |
| 実績：`ACH_MASTER_OF_HAMMER` | Hero の ClientActorEvent_OnCreate、生成された Vesper の mace 型と回数 | RPCの参照保護だけでは、非公開中の生成通知を補えない。今回の候補型への直接の反例とは確認していない |
| 統計：`StatisticsManager` | 戦闘不能の actor.firstEntity が Monster か、敵の種別 | actor=null で敵種別の死亡集計が抜ける。KillOrAssist の読んだモンスター集計は victim を読む |
| 統計・解析：`InGameAnalyticsManager` | 戦闘不能の actor.GetType().Name、ダメージの local Hero への帰属 | actor=null なら原因が Unknown になり、ダメージの集計対象も変わる |
| 結果集計：`GameResultManager` | actor と firstEntity の型、ダメージ・回復等 | StartTrackingLazy 等は OnStartServer から入る。サーバ直接イベントで集計し結果をRPCで送るため、ゲストCEMの欠落が最終結果表を直接変えるとはしない |
| UI：`UI_DamageNumberGroup`・`DewDamageNumberRenderer`・`UI_DamageNumberText` | focusedEntity との同一性・子孫関係、actor の型、ElementalStatusEffect、祖先Gem | MineOnly、重要度、表示や数値のマージに関係する。表示位置は victim/target の Visual 等で、読んだ処理は元弾の位置を使わない |
| UI：`FloatingWindowManager`・`EditSkillManager`・`InGameUIManager` | 読んだ damage handler は victim・DOT 等 | そのハンドラは actor に非依存 |
| UI：`EntityGroundMarker`・`UI_InGame_KnockedOutIndicator` | 戦闘不能通知による可視性更新・canvas 有効化 | 読んだ callback は actor に非依存 |
| 演出：`FeedbackManager`・`CameraManager` | actor の focusedEntity への帰属、DOT・会心、victim、量 | CameraManager は自分の子孫からの被害なら強度を0.1倍にする。actor=null で揺れ・overlay の強さが変わり得る |
| 演出：`FlavourManager` | ElementalStatusEffect 型、ignoreAssists 時の祖先 Hero と isOwned | 同じ型と帰属が必要。KillOrAssist 側の読んだ判定は victim。PlayKillFeedback 後段全体は未確認 |
| 演出：`Mon_RedMan.OnDeath`・`Mon_RedGiant.OnDeath` | actor.firstEntity.transform.position | 死亡演出の向きを決める。元弾自身の位置ではない。RedMan の読んだ本文には actor の null guard がなく、RedGiant にはある |
| 演出：DewEffect の各 HandleEffectMessage | parent の asset・階層・FX個体・Transform・現在の状態 | parent=null で処理が落ちる。代表への一般置換は、型と帰属の一致だけでは成立しない |
| ゲーム状態：`AbilityInstance.DestroyWatchCallback` | 通知を契機に自身の active を確認して Destroy | obj.actor は読まない。Actor.Destroy はクライアントでは処理しない |
| ゲーム状態：`Se_FishScales.ClientHeroEventOnKnockedOut` | SetAmount(0) | obj.actor は読まない。別のサーバ直接ダメージ処理の発生源参照と区別する |
| MOD：`ClientSession.OnDeath` | victim Monster の netId、所有・関係・レベル等を使う CaptureNativeKill | info.actor は読まない。victim と死亡通知が届くことが前提 |
| MOD：`HostAuthority` と各 partial | サーバの Entity/Actor 直接イベント、同期的な命中スコープ | 現行のCEM登録ブロックは空。解除コードだけを根拠にゲスト購読者へ数えない。サーバ側 EventInfo を変更してはならない |
| MOD：`NativeEnemyHitDamageScope` | CEM damage の受信中にFX向けの一時情報を保持 | 弾を公開せず、actor を差し替えず、通常の通知を補わない |

実績の多くは型や帰属だけを読むが、実績の全系統がその条件だけで成立するわけではない。`ACH_M_M_M_MONSTER_KILL` の tracker の帰属・寿命・集計時点と、生成通知の受け手が残る。ここでいう不成立は「現在の鍵だけを根拠にした一般適用が成立しない」という判断であり、同じ親を持つ対象弾のあらゆる場面で差が出るという主張ではない。

Entity の追加 OnDeath override も確認した。BossPolaris・PrimusAeron・BossMaw・AmbushSpawner・SpiderWarrior・UnstableRat・PitchBlackOre の読んだ固有処理はサーバ条件の内側だった。Scavenger・VikingWarrior・Ink Archer・DarkMoon は自身の表示を変更し、MisticIceVein の固有本文は空だった。これらの確認範囲では、追加のゲスト側 actor 読み取りは見つからなかった。`RoomMod_Artifact`、`Se_E_LizardlyBlessing`、`CurseStatusEffect` の該当購読もサーバ経路であり、ゲストの発生源差し替えによるゲーム状態変化の例には数えない。

### 16.4 案の比較と推奨

| 案 | 成立する範囲・利点 | 不成立または未証明の条件 | 削減の見込み |
|---|---|---|---|
| A：代表へ差し替え | 型・共通祖先・firstEntity・owner・caster だけを読む受け手は、値と有効期間を保てれば成立し得る | tracker と子孫走査、生成通知、具象フィールド、ReactionChain の個体一致、FX個体・位置・停止対象は追加条件が必要。現在の鍵だけでの一括適用は認められない | 追加公開を避けられる可能性はあるが、成立する型・受け手を未確定のため、実績値は無い |
| B：元弾を先に公開 | 元の型・netId・同一性・同期値を保ちやすい | 遅れた生成通知、公開前のローカルdata、失われたhookを自動では補えない。依存先・寿命・接続別の順序・Spawn後SyncVar・DewEffectを扱う必要がある | 命中・回復・死亡原因・chain・公開子の親・FXの親等として参照された弾は公開する。削減はそれらに該当しない個体へ限られる |
| C：ダメージ弾への適用を取りやめ | 通常公開を保ち、結果不変を未証明の条件に依存させない。束0の監査・繰り上げ公開の基盤は残せる | ゲストの公開数削減は得られない | 登録0件、今回の削減実績0 |

現時点ではCを推奨する。Aの審査は参照先の型と帰属だけでは終わらず、Bは削減対象を減らしながら生成・同期・寿命の順序を追加で管理する必要がある。対象型を登録する根拠としてはいずれも不足している。将来A・Bを採る場合も、`referencesReviewed` や生成通知・一覧の意味の審査を省く理由にはしない。

案Bの上限は個体の集合で表せる。同フレーム・同鍵の全個体をG、当初公開する最大5個をP、参照や依存先の保護のため追加公開する個体をRとすると、最終公開数は `|P ∪ R|`、公開を省ける数は `|G \ (P ∪ R)|` である。上限は `max(|G| − 5, 0)` だが、全弾が参照を要求されれば削減は0になる。外れ弾でもFXや子の親になればRへ入るため、単純に外れ率を掛けた見積もりにはしない。途中参加と接続ごとの公開状況も別途考慮する。命中率・Rの比率・通信量は未計測であり、Spawn数の削減をそのまま通信バイト数の削減率とはしない。

### 16.5 A・Bを実装する場合の差し込み先

#### 書き込み前の処理

1. CEM の各 `InvokeOn…` の送信wrapperで、`GeneratedNetworkCode._Write_EventInfo…` を呼ぶ前が候補になる。Aでは送信用の値だけを複製して承認済みの参照を変更する。Bでは元個体自身と依存先を公開する。サーバの元EventInfo、`_lastAttacker`、親子関係、共有の ReactionChain._actors を書き換えない。
2. `_Write_EventInfoKill` は CEM・Entity死亡・Hero撃破/アシスト・戦闘不能で共用される。CEMだけのpatchでは足りない。共通writerを使う場合は用途を限定する送信スコープが必要で、同名の GeneratedNetworkCode がアセンブリごとに存在する点も確認する。Heal/Shield は `_Write_ReactionChain` と Actor リストの書き込みまで扱う。
3. Spawn時は既存の `NativeGuestPublishLimit.PreparePublication` を利用できる。ただし現在の処理は「送信するActorの参照先」を調べるもので、RPCが参照する非公開個体自身を公開する入口の追加が要る。依存先を祖先順に処理し、必要な接続の observing / observers の両方を本来の AddObserver 経路で整える。
4. Spawn後のSyncVar変更は、各 `SerializeSyncVars` と構造体writerが参照を書く前に保護する必要がある。初回SpawnのPrefixだけでは覆えない。受信後のgetterによる解決を、hookやOnCreateの復元の代わりにしない。
5. DewEffect は parent の `WriteNetworkIdentity` と `SendToAll` の前に別途処理する。RPC用の変更はここには効かない。位置指定の再生、親の現在位置からの再生、停止・速度変更の同値条件を分ける。未準備接続も送信対象に含むAPIなので、readyなRPC observerと同じ集合だと仮定しない。

`WriteNetworkBehaviour` 全体を無条件で置換する方法は採らない。Command・TargetRpc・SyncVar・host内の処理まで巻き込むうえ、同じActor参照でも用途ごとの意味が違う。`Actor.CustomRpc_SendMessageToAllClients<T>` のJSON文字列や独自同期形式も、標準writerだけで覆えるとは限らない。

#### Mirror の実送信経路と順序

調査対象の ClientRpc は引数をwriterへ書いてから `NetworkBehaviour.SendRPCInternal` に入り、送信元identityのobserverを走査して `NetworkConnectionToClient.BufferRpc` に積む。接続のUpdateや容量超過で `FlushRpcs` が動き、その後通常の送信batchへ入る。`SendRPCInternal` 到達時にはpayloadが既にバイト列なので、そこだけをpatchして型付きのactorを差し替えることはできない。`NetworkServer.SendToReadyObservers` だけのpatchもこのRPC経路を覆わない。

Spawn は `ShowForConnection → SendSpawnMessage → conn.Send` を通る。`conn.Send` は即時のtransport送信ではなく、通常のbatchへの追加である。依存先のSpawn、参照先自身のSpawn、参照するRPCの順を、各接続で成立させる必要がある。`NetworkClient.ApplySpawnPayload` は initialState を復元した後に spawned へ登録するので、依存先を後回しにしない。

Destroy/UnSpawn のメッセージも通常batchへ入り、`NetworkServer.DestroyObject` 本文にはRPC bufferのflushが無い。一方、ゲームの `Dew.Destroy → EnqueueDestroy → StepDestroy` は cleanup の後に0.35秒のRpcWaitを置き、さらに各 `ICleanup.canDestroy` を確認してからUnSpawn等へ進む。Actor の cleanup は isActive をfalseにし、canDestroyは子のnetwork一覧とdestroy lockも確認する。したがって「命中直後に必ずDestroyがRPCを追い越す」とは言えない。固定時間の待機だけで全経路の送信順を証明したとも扱わず、Bでは公開時のinactive状態と、RPCを読む時点まで同じ個体を参照できる条件を確認する必要がある。

Aにも代表の寿命の問題がある。現在の `NativeGuestPublishLimit.Slot` は鍵・フレームのstamp・個数だけを持ち、代表Actorを保存していない。実装するなら、非公開個体から元フレーム・鍵の公開代表への対応と、その代表が破棄・プール再利用された世代の識別が必要になる。次フレームのslot再利用や、既に失われた代表に付け替えてはならない。代表を残すためサーバの寿命を変える方法は、子孫集計などへ影響するため、結果不変の根拠には使えない。

#### 受け入れの条件と境界

- Aを採る型は、全受け手が読む値・個体data・子孫集合・通知時点まで同値である根拠を示す。型とownerだけが一致する場合を合格にしない。
- Bを採る型は、元弾と依存先が対象接続で先に公開され、遅れたOnCreate・inactive時の初期化・参照の寿命を含めて結果が変わらない根拠を示す。命中弾を追加公開したのに、常に公開数5個と報告しない。
- 読めない形や送信時の例外では公開制限だけを止め、通常公開へ戻す方針を保つ。戦闘通知、サーバの弾、ゲーム全体やMOD全体を停止して回避しない。
- Cを採る現状は登録0件を保つ。今回の調査を登録許可と扱わず、コード・登録表・設定・changelog・テストファイルを変更しない。

### 16.6 未確認と今回の検証

未確認は、全生成・破棄通知の購読先とActorManagerの全消費側、Contentsの復元失敗23メソッド、独自JSON・persistentSyncedDataの参照形式、受信したReactionChainを読む具体的なゲスト購読者、SpawnMessage.parentActorNetIdのゲーム側利用全体である。前掲の個別型が今回の候補弾と実際に組み合わされるか、DewEffectのプレハブ配線全体、FlavourManagerの後段全体も未確認として残す。検索で見つかっただけの箇所を、影響なしとは判定していない。

今回確認したのは、CEMの送受信、各EventInfoのserializer、追加の死亡・撃破・戦闘不能通知、前掲の受け手、DewEffect、SyncVarの参照、Mirrorのbuffer・Spawn・Destroy経路、MODの購読と公開制限の基盤である。15節は別履歴の記録を復元して接続したもので、15.4のビルド・テスト結果を今回の実行結果には数えない。

文書の検査は `yomiyasu_lint.py`、`yomiyasu_check.py run` による原文との差分点検、`git diff --check` を実行した。初回lintはerror 0件・warn 2件で、「版」という表現の候補を見直した。diffとstatusで仕様書だけの追記であることも確認した。文章検査の実行は、通信の同値性や実機動作の確認を意味しない。

今回の変更は仕様書だけで、A・Bの実装、ビルド、生成チェック、既存テスト、関連テスト選択、全体の遅いスイートは未実行。コードと生成物を変更していないため、今回は文書の検査を行う。ゲームの実行環境が無いため、実機・協力プレイ・途中参加・専用サーバ、同フレーム同鍵で6個以上生成した際の受信順、実績の達成可否、Profiler、命中率・公開数・通信量・粒子の実測も未実行である。実機確認だけのIssueは作成しない。
