# ゲストでスキルが黒くなり発動できなくなる不具合の調査と修正

対象は協力プレイの参加者（ゲスト）側で起きる「スキルのアイコンが黒くなって発動できなくなる。処理落ちや重さが増すと起きやすい」という報告。この環境ではゲームを起動できないため、本体の逆コンパイル（`sod-decomp`）、MOD コード、既存テストを調べた。

1〜6章は v3.7.0 / PR #730 の調査と対策の記録。2026-10-18 の再報告（Issue #779）に対する追加調査と変更は7〜11章、2026-10-24 の3回目の報告への対応は12章に記す。報告された現象の発端は未特定で、今回コードで確認できた監視・再送の不足を修正した。

## 1. 確定した事実（コードから読める範囲）

スキルボタンが黒く見える条件は、本体 `UI_InGame_SkillButton.FrameUpdate` で2つある。

- `disabledIndicator`（黒いオーバーレイ）は `CanBeReserved()` が false のときに出る。
- `cooldownFill` の塗りは残りクールダウンの比率で、比率が 1 以上になると全面が黒く沈む。

チャージ（`currentCharges`）・クールダウン（`currentUnscaledCooldownTimes`）・最低待ち（`currentMinimumDelays`）は通常配列で、通常の SyncVar 差分には含まれない。`CanBeReserved()` が直接見るのはチャージと最低待ちで、クールダウンそのものは直接の判定要素ではない。配列の値は初期同期、イベント時の `RpcSetCharge` / `RpcSetCooldownTime` / `RpcSetMinimumDelay`、生成後の全配列 RPC で入る。時間の経過は各端末の `AbilityTrigger.LogicUpdate` が進め、チャージの回復はホストだけが行う。

ローカルティックは `maxDelta = dt / GetCooldownTimeMultiplier(i)` で時間を減らす。ここで乗数が退化（0 以下・∞・NaN）していると、ゲスト側では次が起きる。

- 負の乗数 → `Mathf.MoveTowards` が 0 から遠ざかる方向に動き、クールダウン残りが毎フレーム増える。ロジックの `dt` は上限がないため、処理落ちで巨大なフレーム時間が来るほど一気に膨らむ。
- ∞ → `maxDelta` が 0 になり、永久に凍結する。
- NaN → そのまま不動。

乗数を退化させる材料は本体に元からある（ヘイスト ≤ −100 での `Lerp`、アルティメットの `100 / cooldownTime`、`sbCooldownMultiplier` の積など）。PR #730 より前の `NativeCooldownSafety.GuardMultiplier` は `!NetworkServer.active` で早く返るサーバ専用のガードだった。当時はサーバが 0.01 への置き換えで回復して `RpcSetCharge` を送っても、ゲストの手元の時間が暴走・凍結した場合に次の更新まで残る経路があった。PR #730 でゲストにもガードを加えており、今回の再報告をこの経路だけで説明できたわけではない。

## 2. 原因の確度

- 確定：上記の機構（黒表示の条件、ローカルティックの式、乗数の退化経路、既存ガードがサーバ専用であること）はすべてコードから確認した。
- 推測：実際の現場で退化乗数がどの経路（ヘイスト・アルティメット・装備の積）で発生したかは実機ログで確認できていない。既存の `NativeCooldownSafety` が存在すること自体が、過去に退化が起きたことの傍証にはなる。
- 推測：処理落ちと相関する理由は、(a) `dt` の上限がないため負の乗数での暴走が処理落ちで加速する、(b) 高負荷なほど状態異常・ヘイスト系のスタックが増えて退化に近づく、(c) サーバのチャージ付与 RPC の間隔が開く、の複合と考える。

## 3. 修正A：ゲスト側の乗数ガード（`NativeCooldownSafety.cs`）

`GuardMultiplier` に純クライアント用の分岐を足した。サーバ側は従来どおりガード窓の中だけで 0.01 に置き換える。純クライアント（`NetworkClient.active` かつ非サーバ）では、退化乗数を読み出し場所を問わず `1f`（実時間）に置き換える。ゲストでのこの値はローカルの表示と予測にしか使われず、キャストの判定権はホスト側にあるため、置き換えても強さ・クールダウン・発動条件は変わらない。正の有限な乗数は従来どおり本体の値を通す。

これで暴走（負）と凍結（∞・NaN）の両方が起源で止まる。チャージ自体は引き続きサーバの RPC で供給される。

## 4. 対策B：ゲストの検知とホストの再同期

修正Aの前提の外で手元の値が食い違った場合に備え、検知と再送の仕組みを足した。信頼チャンネルの単純なパケット損失が原因だと確認したわけではない。以下は PR #730 時点の動作で、今回の動作は9章に記す。

### ゲスト側（`ClientSession.SkillLock.cs`、tick ステップ「skill lock watchdog」）

毎フレームは時間の比較だけ。1 秒に 1 回だけ自分のキャラ（Q・W・E・R・.Identity・Movement）を走査して、次のいずれかを探す。

- 壊れた値：配列に非有限・負・あり得ない大きさ（600 秒超。本体最大のクールダウンより十分上）がある。
- 充填漏れ：クールダウンも最低待ちも終わっているのにチャージが 0 のまま（本体側は充填しているはずの状態）。

移動中の画面替わり・長い詠唱中・死亡など、値が安定しない状態では見ない。状態が 10 秒続いたときだけ `DreamforgeSkillResyncMsg` をホストへ送り、送ったら窓を立て直す（次の依頼には最低 10 秒の停滞がもう一度要る）。

### ホスト側（`HostAuthority.SkillLock.cs`）

`DreamforgeSkillResyncMsg` を受け取ったら、依頼者のキャラの各スキルについて、権威側の現在値（クールダウン・最低待ち・チャージ）を本体の RPC 形式そのままで送り直す（`NativeCooldownSafety.ResendAuthoritative`）。値は冪等で、受け取った側は同じ値を上書きするだけ。プレイヤーごとに 8 秒以上間隔を空け、権威側に壊れた値が残っていても非有限の値は絶対に送らない。

### 互換性

この型を送るのはこの版以降の参加者だけ。旧い版や MOD なしの環境は送らないし、受け取らない相手は未知のメッセージ型として無視する（本体 CustomRpc の既定動作）。ホストは Hello を交わした参加者からの依頼だけに応じる。既存のメッセージ型は一切変更していない。

## 5. 並行作業（perf/v370-* の C6）との関係

C6（状態異常の残り時間 SyncVar の送信間引き）は `StatusEffect` の同期を扱い、スキルのチャージ・クールダウン配列には触れない。期限切れの確定送信を通し、`AbilityTrigger` の RPC を間引く変更もない。直接ロックを残す経路は確認していないが、高負荷時の発生原因まで否定できたわけではない。現在の実装との関係は8章に記す。

## 6. 未確認の点

- 実機での再現と回復の確認（この環境ではゲームを動かせないため）。特に、実際に発生していた退化乗数の経路（ヘイスト・アルティメット・装備のどれか）は特定できていない。
- 再同期に未対応のホストと対応版のゲストの組み合わせで、依頼が無視されることの実動作確認（本体 CustomRpc の設計上は無視される）。
- `NetworkTrafficOptions`（netSkipUnchanged 等）の利用箇所は MOD 独自メッセージで、本体の SyncVar・RPC を間引く経路は確認していない。通信中のあらゆる不具合を否定するものではない。

## 7. 追加調査：黒表示と発動不可の条件

以下の本体ファイルは `/home/wang/dev/sod-gamedata/decompiled/sod-decomp/` 以下の逆コンパイルを指す。行番号は2026-10-18に確認した版のもの。

| 条件 | 表示・入力・発動への作用 | 根拠 |
| --- | --- | --- |
| 対象が null / inactive、編集中のドラッグ対象、owner が null | `ShowEmpty()` へ進む。黒いオーバーレイとは別の空表示 | `Dew.UI/UI_InGame_SkillButton.cs:347` |
| config の cast lock、チャージ0、最低待ちが厳密に0でない、マナ不足、能力の cast lock | `CanBeReserved()` が false となり黒いオーバーレイが出る。マウス入力もその表示の active 値で止まる | `Dew.Core/AbilityTrigger.cs:1511`、`Dew.UI/UI_InGame_SkillButton.cs:394`、`:455` |
| クールダウン・最低待ちの塗り、能力実体の fill、特殊オーバーレイ、鍵アイコン | それぞれ別の表示。クールダウンだけで予約不可が決まるわけではない。複数チャージが残る場合はチャージ用の塗りを使う | `Dew.UI/UI_InGame_SkillButton.cs:360`、`:383`、`:403`、`:415`、`:436` |
| config の `isActive=false` | パッシブ表示となり通常のスキル入力は拒否される | `Dew.UI/UI_InGame_SkillButton.cs:400`、`Dew.Core/ControlManager.cs:1231` |
| 詠唱中、self validator が false | `CanBeCast()` が false。予約の判定とは異なり、それだけで黒いオーバーレイになるとは限らない | `Dew.Core/AbilityTrigger.cs:1502` |
| 打ち上げ・スタン・沈黙・ダッシュ・移動スキルのルート | config の許可条件に従い self validator が拒否する | `Dew.Core/AbilitySelfValidator.cs:16` |
| Ability / Dodge の禁止・解除可能な禁止 | `ignoreBlock` でなければホストの `ActionCast` が待機する。初回の解除可能な禁止は本体の `DisobeyBlock` で扱う | `Dew.Core/ActionCast.cs:52`、`:71`、`Dew.Core/EntityControl.cs:2164` |
| 対象の生存、対象 validator、対象指定不可・隠密、射程 | 発動を終了または待機する。再同期でこれらの制限を回避しない | `Dew.Core/ActionCast.cs:48`、`:87` |
| 入力停止、画面遷移、メッセージ、入力欄のフォーカス、main / dodge の無効化 | 手元で入力を受け付けない。通常の UI 操作でも起きるため、ホストから強制解除しない | `Dew.Core/ControlManager.cs:1220`、`:1338`、`:1450` |
| owner / 操作対象 / Mirror の所有権のずれ | 手元の操作対象が一致しない、または権限が必要な `CmdCast` を送れない。能力の owner と hero の `isOwned` は別に診断する | `Dew.Core/ControlManager.cs:2096`、`Dew.Core/EntityControl.cs:1671`、`:3312`、`Dew.Core/Entity.cs:243` |
| 能力実体が active のまま残る | ホストの `_currentInstances` が config の cast / cooldown lock を保持し得る。inactive / null なら本体の更新で除かれる | `Dew.Core/AbilityTrigger.cs:1191`、`:1593` |

最低待ちは `== 0f` が必要なので、小さな負数でも発動を妨げる。クールダウンの進行は `ShouldTickCooldown()` と倍率に従う（`Dew.Core/AbilityTrigger.cs:1171`、`:1210`）。アルティメットの config 0 は通常の時間経過で進まず、イベントで充填される（`Dew.Core/SkillTrigger.cs:1006`、`:1324`）。この待ち時間を停滞として短縮しない。

### ゲストで状態が残る経路と確度

**コードで確認した不足**：旧監視は異常な数値と充填漏れだけを検出し、cast lock・詠唱・能力ロック・行動禁止・マナ・owner・UI参照のずれを検出していなかった。旧再送も3配列の要素別 RPC だけで、これらを更新しない。配列が正常でも制限が残った場合、復旧を頼まないか、頼んでも対象の状態を再送できなかった。

**コード上の残留経路**：能力辞書の値は netId と componentIndex で、受信時に対象がまだ spawn していなければ null に解決される（`Dew.Core/SyncedNetworkBehaviour.cs:39`）。辞書の getter はあとから再解決できるが、UI の `_target` は能力変更イベントから設定される（`Dew.Core/EntityAbility.cs:188`、`Dew.UI/UI_InGame_SkillButton.cs:244`）。辞書の通知時には未解決で、その後に実体が解決できる順序では、UI への再通知がないまま参照が残り得る。今回の監視は辞書から得たスキルと UI の参照を比較する。報告現場でこの順序が起きたことまでは未確認。

**再送で直せる範囲**：ゲストの SyncVar / SyncObject や配列がホストと食い違う場合は現在値を再送する。ホストにも同じ制限が残っている場合は、その正当性をゲストから判断できない。ホストの能力実体やチャネルを破棄したり、禁止を解除したりせず、診断値を残す。Mirror の所有権情報も再割り当てしない。

負荷や遅延は処理間隔を広げるが、それだけでは信頼チャンネルの RPC が失われた証拠にならない。spawn、辞書通知、SyncVar、RPC の適用順を実機ログで確認できていないため、Issue #779 の発端は未特定とする。

## 8. MOD の各処理との関係

| 処理 | 確認した作用と、本件との関係 |
| --- | --- |
| 描画の軽量化 | `src/SodRpg.Mod/PerformanceTuner.cs:12` は描画品質・LOD・描画更新間隔などを扱う。`DeathFxThrottle.cs:74` は揺れ・振動・光を間引く。スキルの配列・ロックを直接書き換える経路は確認していない。描画停止と症状の相関は実機では未確認 |
| C6 の状態異常同期 | `src/SodRpg.Mod/NativePerfPatches.cs:81`、`:203` は通常の小さな正の残り時間の減算だけを最大0.5秒間隔で公開する。0・null・明示更新・大きい減算と、期限切れの StopTimer / Destroy を通す。実際のスタン・沈黙・ルートは別の `EntityStatus.basicEffectMask` で判定される（`Dew.Core/EntityStatus.cs:375`、`:1697`、`Dew.Core/StatusEffect.cs:354`、`:470`）。表示上の残り時間の遅れと、発動を止める mask を分けて読む |
| 状態効果の寿命保護 | `src/SodRpg.Mod/NativeActorLifetime.cs:91` は未解決の victim をゲストで直ちに破棄せず待つ。明確な終了は本体の後処理を通す。`FeverFxThrottle.cs:233` は LivingBomb の基底更新を呼んでから色・大きさの更新を間引く。今回の調査では skill lock を直接残す経路は確認していないが、ホストで効果が active のまま残る事例は実機で未確認 |
| 待機中の倍率計算の省略 | `src/SodRpg.Mod/NativePerfPatches.cs:387` は残り時間が厳密に0の計算だけを省く。チャージ回復・最低待ち・ロックの更新は省かない。既存の倍率ガードはすでにゲストにも適用されており、今回その計算は変更していない |
| 生成分散 | `src/SodRpg.Mod/NativeProcSpawnSpreadPatch.cs:20` の限定した4か所の反応弾生成を対象とする。`NativeProcSpawnSpread.cs:15`、`:123` は予算と待ち時間で排出し、本体の0.1秒の creator 寿命保護に対して0.07秒を期限に使う。処理落ちで次フレーム自体が遅れる可能性はあるが、`OnCastComplete` 全体や `_currentInstances` を直接遅延・編集する処理ではない。発生現場との因果は未確認 |
| 弾まとめ | `src/SodRpg.Mod/NativeProcCoalesce.cs:18`、`:87` は同フレーム・同 creator・同開始位置・同対象などが一致する対象の反応弾をまとめる。payload の処理を保持し、skill の cast / cooldown lock を直接変更しない。ホストの実体残留まで実機で否定したわけではない |
| 撃破同期 | `src/SodRpg.Mod/HostAuthority.KillSync.cs:327`、`ClientSession.KillSync.cs:14` は撃破の記録・再送・確認を扱う。スキルのロック・配列・入力権限を直接更新する経路は確認していない。通信量の増加と症状の相関は未確認 |
| MOD 独自通信の省略 | `NetworkTrafficOptions` の使用箇所は build、run growth、boss visuals、個人選択などの独自メッセージ。本体のスキル RPC や SyncObject を捨てる処理は確認していない |
| MOD の正当な発動制限 | `src/SodRpg.Mod/NativeMemoryReuse.cs:40`、`:109` は単チャージ記憶の再使用待ちを判定する。`NativeDreamContent.Effects.cs:79`、`:171` は対象を限定した Seedling をパッシブとして扱う。監視はパッシブ config を除外し、再使用待ちを強制解除しない |

## 9. 2026-10-18 時点の監視と再送

設定 `guestSkillLockRecovery` は既定で true。無効にするとこの監視とホストの応答を止める。既存の倍率ガードはこの設定とは独立する。

参加者は1秒ごとに6スロットを調べる。配列の形、スロット番号、異常値、owner、cast / cooldown lock、詠唱、能力ロック、行動禁止、self validator、マナ、チャージ、最低待ち、UI参照を観測する。これらが正常でも本体の `CanBeReserved()` が false なら、予約判定の残りの条件として検出する。死亡・ノックアウト・zone 不在・画面遷移中は観測をリセットする。通常のカウントダウンが進んでいれば待ち時間の観測を更新し、アルティメット config 0 の正当な充填待ちとパッシブは除外する。複数の制限が重なる場合は、ホストへの確認対象になり得る。

スロットごとに同じスキル・config・理由が10秒続いたら診断を1回出す。同じ観測期間の依頼は最大3回で、全スロットを通じて8秒以上空ける。理由などが変われば別の観測期間になる。長い正常な制限でも問い合わせる場合があるが、参加者は値を変更せず、ホストの値を要求するだけである。通常フレームでは配列・LINQ・クロージャ・文字列を作らない。

ホストは version 1 の既存メッセージと Hello、human caller、本人の active な hero、netId を照合し、caller ごとに8秒以上空ける。再送するものは次のとおり。

- hero、Ability、Control、Status、各スキルの SyncVar。owner、config index、ability index、詠唱、能力ロック、行動禁止カウンタ、マナ、状態異常 mask などを現在値で公開する。
- Ability 辞書の同値 `OP_SET`。本体の能力変更イベントを再通知し、UI の参照を更新する。
- cast / cooldown lock の SyncList に同値 `OP_SET` を追加する。
- 本体の全配列 RPC と既存の要素別 RPC。全配列を先に送り、長さがずれた参加者の配列を置き換えてから要素別更新を送る。ホスト側の形がそろわない場合は全配列の再送だけを見送る。
- `_isConfigDirty` による本体 config RPC。クールダウン・マナ・チャージ数・最低待ちの現在の設定値を送る。config 数を実行中に変更することは本体側の契約で禁止されており、その違いを修復するものではない。

`NetworkBehaviour.SetDirty()` が立てるのは SyncVar の dirty bits で、SyncObject の差分は作られない。また今回の Mirror の `SyncList<T>` indexer は同値を無視する。そこで本体の `AddOperation(OP_SET, ...)` を型確認した delegate 経由で呼び、既存の差分の末尾に同値を追加する。初期 snapshot に含まれる差分を読み飛ばす `changesAhead` やリスト本体は書き換えず、ロックの一時反転・clear/reset もしない。辞書の indexer は同値でも差分を作るので、その公開 API を使う。これは今回参照した `Mirror.dll` の `NetworkBehaviour.SetDirty`、`SyncList<T>.AddOperation`、`SyncIDictionary<TKey,TValue>.this[]` と各 `OnDeserializeAll/Delta` で確認した。

ホストの正当なロック・能力実体・チャネル・状態異常は解除しない。クールダウンやダメージなどの数値・遊び方も変更しない。従来と同じく、送信用の時間が負・非有限なら0、チャージが負なら0として送り、再送元の配列は変更しない。

応答には本体の既存 RPC / SyncVar / SyncObject の形式だけを使う。新しい受信型やメッセージの版変更はなく、旧版や MOD なしの相手に専用の読取処理を要求しない。旧ホストは従来の再送範囲に従うか、未知の依頼を無視するため、追加した復旧の効果は組み合わせによって異なる。

反射した API の形が合わない、または補助処理が例外になった場合は `Skill lock recovery disabled for ...` を該当機能ごとに1回出し、その部分だけを停止する。診断の失敗は再送を止めない。監視やホスト handler 自体の予期しない失敗は、その監視・応答だけを停止し、MOD のほかの機能は動かす。

`ClientSession.Tick` の登録は23ステップのままで、既存の `TickSkillLockResync`（index 11）の中だけを広げた。`ClientTickIsolationTests.cs` のスタブ・番号は変更不要。起動時の HostAuthority 呼び出しにも追加がなく、`StartupGameApi.cs` のスタブ追加は不要だった。

## 10. 診断ログの読み方

`Skill lock guest: reason=...` は、同じスキル・config が使えない期間に1回出る。`Skill lock result: ...` は、ホストの返答を受けてから約2〜3秒後の観測で1回出る。制限が先に消えた場合はその観測で記録する。`before` が依頼時、`reason` が結果を観測したときの制限で、`host=[...]` はホストの再送時、`guest=[...]` は結果を観測したときの値である。`recovered=true` は監視対象の制限が消えたことを表す。実際に入力して発動したことや、再送だけが回復の原因だったことを証明するものではない。`recovered=false` なら、その観測時点では制限が残っている。後の再確認は続けるが、同じ期間の結果ログは重ねない。

`compared=true` のときだけ `different` を読む。`different=true` は依頼時のゲストの値と返答時のホストの値に差があったことを表す。通常の時間経過・マナ変化も差に含むため、それだけで同期不良とは断定しない。最初の version 1 依頼に対しては、対応通知に含まれるホストの値とゲストに残した送信前の値を照合する。対応通知を受け取ったあとの version 2 依頼では、ゲストの観測値をホストへ送り、ホスト側で照合する。

ホストの返答がないまま制限が消えた場合は、一定の待機後に `recovered=true hostReply=missing` を1回出す。旧ホストは結果返答を持たない。`Skill lock host: ...` は従来どおり caller の最初の version 1 依頼で6スロットを記録する。スロットは 0=Q、1=W、2=E、3=R、4=Identity、5=Movement。`request`、hero / skill netId、config を合わせて読む。違う端末・時点の値を同時点の状態とみなさない。

| ログの値 | 見る点 |
| --- | --- |
| `reason` | `Broken`=異常値、`Shape`=配列・参照・スロットの形、`Owner`=所有者または手元の所有権、`Undercharged`=時間終了後もチャージ0、`Waiting`=時間や充填の待ち、`UiTarget`=UI参照のずれ、`Reservation`=ほかの観測値は正常だが予約不可。ほかは詠唱・ロック・行動禁止・validator・マナの観測結果。原因確定の印ではない |
| `heroOwned`、`owner`、`controllingHero` | ゲストで自分の hero を所有し、skill の owner と操作対象がその hero か。ホスト側の `heroOwned` は同じ意味で比較しない |
| `lengths`、`config`、`abilityIndex`、`active` | configs / cooldown / charges / delay の長さと、対象の config・スロット・生存状態 |
| `charges`、`cooldown`、`delay`、`cost`、`mana`、`multiplier` | 権威値と手元値の差。時間の経過や通信の遅れを考慮する。最低待ちは厳密に0が必要 |
| `locksKnown`、`castLock`、`cooldownLock`、`casting`、`abilityMask` | `locksKnown=false` なら lock の false を有効な観測とみなさない。ホストもロック中なら再送で解除しない |
| `status`、`blockAbility`、`blockDodge`、`airborne`、`dashing`、`dodging`、`validator` | `status` は basic effect mask。禁止カウンタは「解除不可/解除可能」の順。`channels` はローカルのリスト数で、`StartChannel` は server-only のためゲストの0とホストの値を同一視しない |
| `reserved`、`cast`、`inputEnabled`、`mainDisabled`、`dodgeDisabled`、`uiStale` | 予約と実発動、手元の入力制限、UI参照のずれを分ける。範囲・対象・全 UI 入力条件をこのログだけですべて診断できるわけではない |
| `instances` | ホストの能力実体。件数と最大8件の `型:netId:active:configIndex:castLock:cooldownLock`。active の実体がロックを保持しているかを見るが、ログだけで不正と決めて破棄しない |

配列や config の形が不足している場合は、安全に読める項目までを出す。診断に失敗すると `Skill lock recovery disabled for state diagnostics` を1回出して診断だけを止める。ロックや全配列 RPC などの警告も、それぞれ停止した部分を示す。毎秒のログ、依頼ごとの成功ログは出さない。

## 11. 2026-10-18 時点の検証と未実行

生成コードの整合性確認（`python3 tools/balance/gen_cs.py --check`）と、ゲーム DLL を参照した最終コードの MOD Release ビルドは成功した。ビルドはエラー0・既存警告6件。`Issue73.Native.Tests` は308件成功、`SodRpg.Mod.Startup.Tests` は192件成功・既存スキップ1件だった。初回のテスト実行は .NET 8 ランタイムがないため中断し、導入済みの .NET 10 へ `DOTNET_ROLL_FORWARD=LatestMajor` でロールフォワードして再実行した。

既存テストは Tick の隔離・起動・既存の倍率ガードなどを検証するもので、新しく加えた Unity / Mirror の再送を実通信で検証するものではない。新しいテストファイルは作成していない。文書は yomiyasu で推敲・lint し、残った箇条書き比率の候補1件は独立した条件・確認項目の列挙として残した。更新履歴の lint は候補0件。

未実行：この環境ではゲームを起動できないため、報告された高負荷時の再現、回復、旧版・MOD なしとの混在通信、SyncList delegate と全配列 RPC のゲーム内実行。根本原因の実証は残る。全テストスイートは依頼の検証範囲外として実行しない。

## 12. 3回目の報告（2026-10-24）

### 発動を止める条件と確度

本体 `AbilityTrigger.CanBeReserved()` は、次のいずれかで false になる。全逆コンパイル内を検索し、このメソッドの override は見つからなかった。

| 条件 | ゲストだけに残る経路 | 今回の扱い |
| --- | --- | --- |
| config の cast lock が true | SyncList の解除差分が未適用、またはホストにも active な能力実体がある | ホストの同値 SET を再送。ホストの実体・ロックは解除しない |
| owner が null | 参照先の spawn と参照通知の順序、SyncVar の参照未解決 | owner の SyncVar と能力辞書を再公開。所有権そのものは再割り当てしない |
| 現 config のチャージが0以下 | 通常配列の古い値、保存復元後の全配列通知、同値再送の省略 | チャージの同値送信省略を外す。全配列または要素別 RPC でホスト値を再送 |
| 最低待ちが厳密に0でない | 通常配列の古い値、ローカル時間更新の遅れ・異常 | ホストの現在値を再送。正の待ちを0にしない |
| `owner.currentMana >= manaCost` が成立しない | マナ SyncVar または config RPC の値が古い。NaN でも不成立 | マナと config を再公開。マナを加算しない |
| `IsAbilityCastLocked(abilityIndex)` が true | 能力ロック bitmap または config / 能力参照の値が古い | Ability の SyncVar と辞書を再公開。`ignoreAbilityLock` を含む本体の判定は変更しない |

能力ロックは index が負なら false、64を超えると例外となる。該当 bitmap の bit が立ち、能力がなければ true、能力があれば `!currentConfig.ignoreAbilityLock` を返す。配列や index が不正な場合も予約条件を安全に読めないため監視する。詠唱・self validator・行動禁止・入力停止・UI参照の違いは7章のとおり、予約不可と分けて診断する。

**確定した不足**は、ホストの送信記録だけで同値の `RpcSetCharge` を止めていたこと。復旧用の `NativeCooldownSafety.ResendAuthoritative` もこのパッチを通るため、全配列 RPC を使えない場合のチャージ再送を省いていた。Mirror の `SendRPCInternal` は未 spawn、observer 不在、相手の準備未完了で実送信しないことがある。Prefix で記録した値は到着の確認ではない。通常配列には遅延した保存復元もあり、「前に送信メソッドを呼んだ」だけで全ゲストの値が同じとは保証できない。

**発端は未特定**である。信頼チャンネル上のパケット損失が起きたとも、保存復元が今回の報告を起こしたとも断定しない。保存復元では `DewPersistence` が `ApplyAfterFrameDelay` 付きの3配列を適用し、`AbilityTrigger.OnCreate` が2フレーム後に全配列を送る。配列参照の交換で旧送信記録がリセットされる経路もあるため、保存復元だけで不具合が確定するわけではない。

### 前回以降の軽量化の点検

| 処理・変更 | 確認した関係と対応 |
| --- | --- |
| チャージ同値 RPC（`f5405497`、設定復帰修正 `fd3ed41b`） | 上記の復旧不足を確認した。同値も含め本体を常に通すようにした。旧設定の読み取り互換は残すが、省略はしない |
| 満チャージ Ultimate（`b120ba9a`） | 満チャージのときに本体末尾の cooldown / charge の再通知を丸ごと省いていた。ゲストの古い充填待ちを戻す機会を減らすため、この省略も外した。現場との因果は未確認 |
| 全配列成功後の要素別省略（`333e3b65`） | 本体受信は全配列の置換と要素別の代入で、同じ値になる。成功時の重複送信省略は維持。失敗時は要素別へ戻り、そのチャージを前段の同値省略で止めなくなった |
| CustomRpc の型・反射キャッシュ（`08617d3a`、`4ee291d3`、`b20cc69f`） | 現在の辞書・handler list を読み、本体と同じ列挙で処理する。本体スキル RPC は別経路。今回の型が捨てられる根拠は見つからなかった。同名型の後登録という残余条件は現場で未確認 |
| cast preview の同値省略 | context の配送で記録をリセットし、最初の更新と最終の cast 情報を通す。config cast lock の同期とは別。欠落の証拠は見つからなかった |
| 画面内タイマー（`db9cd806` と後続修正） | ChargingChannel の表示値。接続準備前は省略せず、作成で初期値を記録し、削除通知は通す。スキルのチャージ数・発動ロックを更新する RPC ではない |
| C6、宝石 cooldown（`ac56a736`） | 通常の小さな減算だけを間引き、0到達・明示更新・終了を公開する。発動制限の mask とスキル配列とは別。終端を落とす根拠は見つからなかった |
| timescale、位置・Transform、詠唱テレグラフの同値省略 | 時間倍率の値は変えず、変更・終了値は公開する。位置・表示の省略はチャージ・最低待ち・config lock を変更しない。高負荷時の現場との因果は未確認 |

### 原因に依存しない照合と再送

従来監視には、理由の変化で10秒窓を戻す、通常の countdown の進行で窓を戻す、Ultimate config 0 の正の充填待ちを除く、同じ期間は3回で打ち切る、という見落としがあった。今回、同じスキル・config が使えない時間を数えるように変えた。理由が変わっても countdown が減っても10秒で問い合わせる。Ultimate の充填待ちと、同期される `maxCharges` が0以下の場合も対象にする。本当にパッシブである `isActive=false` は除外する。空きスロットに古い UI 参照だけが残る場合も対象にした。

1秒走査、全スロット共通の8秒以上の依頼間隔は維持する。スロットを巡回し、先頭の長い制限だけでほかの依頼が待たされないようにした。各スロットは最初の3回を8秒以上、その後は60秒以上空けて確認を続ける。死亡・遷移・hero / 接続の交換では観測をリセットする。ホストにも同じ制限がある場合も照合対象になるが、再送以外はしない。

最初は変更していない `DreamforgeSkillResyncMsg` version 1 を送る。対応ホストは本体の再送に加え、`DreamforgeSkillResyncCapabilityMsg` version 2 と6スロットの値を返す。この通知を受け取った接続だけが `DreamforgeSkillStateRequestMsg` version 2 を使う。ゲストはチャージ・cooldown・最低待ちの全 config 配列、現在 config、owner、ロック、マナ、状態異常・行動禁止などを送る。ホストは caller が Hello 済みの human であることと、本人の active server hero・netId・スロット・頻度を確認し、ホストの現在値と比較する。返答には request ID とホストの値・差の有無を含める。

ホストは差があれば権威値を再公開する。差がなくても、観測配列に含まれない UI 参照などに備えて再公開する。ゲストから受け取った値をホストへ代入しない。ゲストも返答の診断値をスキルへ直接代入せず、本体の RPC / SyncVar / SyncObject だけで更新する。正当なロック・状態異常・詠唱・マナ不足・充填待ち・最低待ちはホストのまま残す。再送の失敗は同じ頻度制限のあとで再試行し、MOD 全体を止めない。毎フレームの確保は増やしていない。配列の複製・文字列・メッセージは頻度を限った依頼と返答のときだけ作る。

旧ホストへは version 1 の動作を残す。旧ゲストは対応通知を無視し、本体形式の再送を受ける。MOD なしには従来どおり未知型が無視される。全員の同時更新を要求する形式変更はしていないが、状態照合と結果ログは対応ホスト・ゲストの組み合わせで使う。

### 今回の検証と未実行

- `python3 tools/balance/gen_cs.py --check` は成功。
- ゲーム DLL を参照した MOD Release ビルドは成功（エラー0、既存警告6件）。配置先は `/tmp/skilllock3-build`。
- 既存の再送・チャージ送信・Tick 隔離・接続保存などの関連テストは55件成功。起動・再読込・クールダウン・ロビー互換の関連テストは157件成功・既存スキップ1件。初回はテスト境界に能力ロックの API がなくコンパイルに失敗し、その境界を補って再実行した。
- 新しいテストファイル・テストケースは追加していない。既存の同値送信テストの期待値と説明を今回の動作に合わせた。新しい状態照合の実通信を単体テストで証明したわけではない。
- 仕様と更新履歴の日本語 lint、`git diff --check` を実行した。lint のエラーは0件。両文書に残った箇条書き比率の候補各1件は、独立した確認項目と更新履歴の指定書式として残した。
- 未実行：Issue73.Native.Tests / SodRpg.Mod.Startup.Tests / SodRpg.Mod.AutoCast.Tests の全件。full / slow suite を実行しないという今回の実行規則に従い、変更に関係する既存テストだけを実行した。
- 未実行：`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py` のテスト実行。`--list` は実行し、902クラスを選ぶことを確認した。広範囲になるため上記の関連テストを個別に実行した。
- 未実行：高負荷時の報告現象の再現、ゲーム内での復旧と結果ログ、旧版・MOD なしとの混在通信。この環境ではゲームを起動できない。実機確認だけの Issue は作成しない。
