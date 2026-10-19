# 記憶の飛び道具・演出の集約調査

調査日: 2026-10-19。対象: Shape of Dreams r1.4.1.2、Dreamforge RPG。

## 調査の状態

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
