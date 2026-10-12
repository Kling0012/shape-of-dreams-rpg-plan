# 石の恒久無敵の調査（2026-10-12、v2.10.27）

報告された「約4割・ロビーに戻って潜り直すと解除」の原因は未確定。実ゲーム・協力プレイの再現ログはなく、今回の変更は MOD 由来の保護効果だけを対象とした安全策である。安全策が報告症状を解消するかは実ゲームでの確認が必要。

## 本体で確認したこと

参照元: `/home/wang/dev/sod-gamedata/decompiled/sod-decomp`、本体 r.1.4.0.13。

- `Dew.Core/PropEntity.cs:41`: 被弾成功時だけ `prop_iframe` の InvulnerableEffect を `hitInvulnerableTime`（初期値 0.1 秒）で作成する。
- `Dew.Core/Actor.cs:911`: 無敵で拒否された攻撃は damage-negated 通知だけで return する。被弾通知まで進まないため、継続ダメージだけで `prop_iframe` を更新し続ける経路ではない。
- `Dew.Core/StatusEffect.cs:354`: サーバの FrameUpdate でタイマーが進み、満了時に Destroy する。同ファイル :472 で BasicEffect の登録を解除する。
- `Dew.Core/Se_GenericEffectContainer.cs:15`: 共通コンテナは部屋内で再利用されるが、`Actor.ApplyPendingBasicEffect` が effect・duration・id を新しく上書きし、OnCreate が SetTimer を呼ぶ。商人の無限時間がそのまま石へコピーされる処理は確認できない。
- `Dew.Core/PropEnt_Stone_Gold.cs`・`PropEnt_Stone_DreamDust.cs` は PropEntity。`Shrine_GoldBit` 等は Shrine（Actor）であり、壊す石とは型が異なる。
- `PropEnt_Stone_Nightmare` は存在するが、本体の部屋 Unity 資産とローカライズ本文が手元にないため、「不快な記憶 / Unpleasant Memory」の部屋 ID・正式表記と配置される石型の対応は未確認。CHANGELOG の部屋名は依頼時の表記を保持した。

## MOD の候補と判定

| 候補 | 調査結果・今回の扱い |
| --- | --- |
| `InfinityBossArena.cs:252,259,309` の Invulnerable / Untargetable | 唯一の直接付与元。型付きの Polaris ボスだけに付与する。石を直接対象にする経路なし。導入は `c40a78a5`（Infinity ボス抽選）。作成時に MOD 識別子を追加し、安全策で BasicEffect 自体の所有を記録できるようにした。 |
| `HostAuthority.UnbowedMind.cs:44` の Unstoppable | 明示的に Hero のみ。導入は `d64e744a`。石への直接付与なし。既存の `DreamforgeRPG.UnbowedMind` ID で安全策の対象を識別する。 |
| `HostAuthority.BossMechanisms.cs:974` の Unstoppable | HeroRuntime.Hero のみ。BossBasicEffectReuse はこの executor の専用インスタンスだけを初期化し、本体の石・商人の InvulnerableEffect には触れない。既存の `DreamforgeRPG.Boss.Container.*` ID で識別する。 |
| `NativeActorLifetime.cs:79,124` の更新停止・Retire | `6b673a6f`（v2.10.20）。被害者が生存・activeInHierarchy・Status 有りなら更新を許可し、無効なら破棄する。プール OnDisable で独自状態を除去する。普通の石のタイマーを止め続ける条件は特定できなかった。 |
| `RoomVisualSweep.cs`・`ClientSession.RoomSweep.cs` | `f0934244`（v2.10.20）。destroyed Monster の renderer を隠すだけ。石の無敵・チーム・targetable は書き換えない。 |
| StatusEffect タイマーのパッチ | EternalFlame は `Se_U_EternalFlame_Curse` のみ。HerWorld の満了ラッパーは他型ではそのまま Actor.Destroy を呼ぶ。共通コンテナの duration を延長する処理なし。 |
| `InfinityServiceRooms.cs` | 未訪問の通常 Combat ノードを Merchant に変えるが、Entity の型・所有者・無敵・チームは変更しない。イベントノードは対象外。 |
| ProcessReceivedDamage / Actor.DealDamage パッチ | ダメージ補正・発動元識別・HP 減少記録。石を商人扱いにする処理、チーム変更、無敵付与なし。 |
| 道標・悪夢化・出来事・Limbo・星図・エッセンス・ロードアウト | MOD の敵補正は Monster、装備補正は Hero。PropEntity に対する恒久無敵・Untargetable の付与元やチーム変更は見つからなかった。熱病・棘・下がれ！等の追加ダメージも、本体の被弾成功時無敵の条件を変えない。 |

Infinity のガード解除に再利用済み参照を Destroy する別の懸念はあるが、他の効果を早期解除する方向であり、石へ恒久無敵を付与する説明にはならない。今回その独立した箇所の変更は行っていない。

## 限定安全策

- `Actor.CreateBasicEffect` で `DreamforgeRPG.*` ID を持つ Invulnerable / Unstoppable / Untargetable だけを識別する。石への作成を拒否する。失敗時もアプリ・MOD 全体を止めない。
- 作成から3秒以上経過した MOD の保護効果が石の EntityStatus に登録されている場合、サーバで1秒間隔の走査により、その BasicEffect の登録だけを本体 API で解除する。3秒は石への登録時刻ではなく、MOD による効果作成時刻から数える。共有コンテナは破棄しない。
- MOD が作った BasicEffect 自体を弱参照で記録するため、コンテナの再利用だけで所有情報を失わない。同じ MOD の BasicEffect の再利用は既存記録の時刻を更新し、反復による記録オブジェクトの割り当てを増やさない。
- `prop_iframe`、本体の無限 Unstoppable、識別できない効果は変更しない。商人の基底型 `PropEnt_Merchant_Base` 全体と MockEntity は対象外。
- 走査は既存 allEntities / _basicEffects を使用し、毎フレームの一覧作成・割り当ては追加しない。解除 API が見つからない場合は回復機能だけを無効にする。
- 発動した場合は拒否・回復の型をログに残す。このログが次回の実ゲーム原因調査の根拠になる。

## 検証

最終変更後の Release ビルド成功（0エラー、既存警告6件）。`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all` は3,164件合格・失敗0件・既存スキップ5件。テスト追加・変更なし。実ゲーム・協力プレイの再現、部屋名・石型の資産上の対応、約4割となる抽選条件、報告症状に対する効果は未確認。
