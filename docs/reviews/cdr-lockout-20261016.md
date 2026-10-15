# 黒曜石・勢いによるクールダウン短縮後の発動不能：調査途中記録

2026-10-16。作業ブランチ `fix/cdr-lockout-1016`。

## 状態

報告されたマルチプレイでの発動不能は未再現で、原因は未特定。修正済みとは扱わない。
通常の有限値の短縮について、確認したコードから恒久的な発動不能を説明できていない。
原因に直接対応する最小パッチを選べる証拠が不足しているため、Harmony パッチは追加していない。
この記録のコミットは調査記録であり、修正コミットではない。

## 調査対象

ローカルの `~/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/` にある
`Dew.Core.dll` と `Dew.Contents.dll` を ilspycmd 11.1.0.9782 で調査した。
必要な型の逆コンパイル結果は `~/.claude/work/herobal/decomp-cdr/` に保存した。
本体 DLL と逆コンパイルしたソースはリポジトリに含めない。
報告環境との本体バージョン一致は確認できていない。

## コードで確認できた経路

- `Gem_E_Obsidian.OnCastComplete` は攻撃のクールダウンをリセットし、
  `Se_Gem_E_Obsidian_EmpowerAttack` を作成する。この効果の `OnCreate` はサーバでのみ
  強化攻撃コールバックを登録し、最初の命中で `ApplyCooldownReduction(firstTrigger, amount)` を呼ぶ。
- `Gem_R_Momentum.OnEquipGem` はサーバでのみ攻撃効果イベントを登録し、
  `ReduceCooldown` が `ApplyCooldownReduction(skill, amount)` を呼ぶ。
- いずれも `Actor.ApplyCooldownReduction` のプロセッサ処理を経て、
  本体の `AbilityTrigger.ApplyCooldownReductionWithMinimum_Imp` に入る。
  黒曜石の攻撃リセットは `ApplyCooldownReductionByRatio_Imp` に入る。
- 固定秒短縮は時間倍率で短縮量・下限を割り、チャージをまたいだ短縮を行う。
  最終チャージの時間は下限でクランプされる。通常の下限はゼロ。
  割合短縮もチャージをまたいだ短縮を行う。
  両処理とも最後に `RpcSetCooldownTime` と `RpcSetCharge` を送る。
- `AbilityTrigger.LogicUpdate` は残り時間をゼロへ進め、サーバで残り時間が厳密にゼロなら
  `SetCharge` でチャージを回復する。クライアントは時間表示を進めるが、回復チャージはサーバの RPC を待つ。
- `CanBeCast` / `CanBeReserved` は正のチャージ数・最小遅延・各種ロック等で判定する。
  残りクールダウン時間のゼロ判定だけで入力を許可する構造ではない。
  `ControlManager.CheckSkillCast` も `CanBeReserved` に依存する。
- `UserCode_RpcSetCooldownTime__Int32__Single` と `UserCode_RpcSetCharge__Int32__Int32` は
  受信値を代入する。`NetworkServer.active` の場合は代入せず、ホストへの再適用を防ぐ。
  この経路にクライアント側の二重減算は見つからなかった。
- 時間・チャージ配列は初回シリアライズと明示 RPC で同期される。
  時間倍率等の SyncVar と、時間・チャージの同期方式は異なる。

## MOD 側で確認した箇所

| 箇所 | 短縮の実行 |
| --- | --- |
| `src/SodRpg.Mod/HostAuthority.DirectedRecharge.cs:127` | 残り時間比を求め、本体の割合短縮 API を呼ぶ。サーバ・対象の検証あり |
| `src/SodRpg.Mod/HostAuthority.NewPowers.cs:151` | 残り時間から割合を求め、本体 API を呼ぶ |
| `src/SodRpg.Mod/HostAuthority.NewPowers.cs:446` | 固定秒短縮を本体 API に渡す |
| `src/SodRpg.Mod/HostAuthority.PairCombos.cs:305` | 残り時間比から本体の割合短縮 API を呼ぶ。リロードは本体のリセット／チャージ設定を使う |
| `src/SodRpg.Mod/HostAuthority.IdentityStrikes.cs:225` | 対象移動スキルを検証して本体の割合短縮 API を呼ぶ |
| `src/SodRpg.Mod/HostAuthority.cs:1897` | リンク加速を本体の割合短縮 API に渡す |
| `src/SodRpg.Mod/HostAuthority.cs:1921` | 固定秒短縮を本体 API に渡す |

これらに本体の時間・チャージ配列を直接減算する実装は見つからなかった。
`NativeMemoryUse` はサーバの使用完了から MOD 効果を処理する。
MOD の通信間引きが本体のクールダウン／チャージ RPC を間引く経路も見つからなかった。
調査した範囲では、MOD 固有の同期不具合を特定できていない。

## 仮説の評価と残る調査

- 負の残り時間だけでは原因と断定できない。固定秒短縮は通常の下限を処理し、
  時間更新も通常の有限値をゼロへ進める。下限クランプを追加するだけで解決する根拠はない。
- 本体には数値境界の弱点がある。固定秒短縮は時間倍率を検証せずに割り、
  倍率ゼロなら下限の `0 / 0` が NaN、正の短縮量は Infinity になる。
  これは下限比較を無効にし得るが、その後の時間更新まで含めた恒久停止は確認していない。
  仮に残り時間が NaN なら `== 0` に到達せずチャージ回復を妨げるものの、
  今回の装備・能力値でその状態に至る経路を立証できていない。
  これを報告の原因として扱わない。
- `SkillTrigger.ShouldTickCooldown` は究極技の config 0 を時間で進めない。
  通常スキル・究極技・クールダウンロック中のスキルは区別して調べる必要がある。
- 発生時のスキル型、config、残り時間、時間倍率、チャージ数、最小遅延、
  キャスト／クールダウンロック、ホストと参加者双方の状態・ログがまだない。
  実環境でこれらを照合することが、原因に直接対応する修正の前提として残る。

## 実行した検証

- 指定の Release ビルド：成功、エラー0、警告6。
  `dotnet build src/SodRpg.Mod -c Release -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/x`
- 既存 Core テストを `--filter` で限定実行：130件成功、失敗0、スキップ0。
  対象：`DirectedRechargeTests`、`MemoryGimmicksV128Tests`、`GimmickEffectsV129Tests`、
  `NewPowersV129Tests`、`PowerRuntimeTests`。
- 既存 Startup テストを `--filter FullyQualifiedName~NativeStartupTests` で限定実行：
  16件成功、失敗0、スキップ0。
- テスト実行には `DOTNET_ROLL_FORWARD=LatestMajor` を指定した。
  これらの合格は報告された本体クールダウン不具合の再現・修正確認にはならない。

## 未実行・未完了

- Unity 実機でのマルチプレイ再現・修正確認：未実行。
  ローカルの対象ゲームディレクトリには Managed DLL 等のみで、ゲーム実行ファイルと UnityPlayer がない。
- 原因に直接対応する Harmony パッチの実装：未完了。原因を特定できていないため。
- 新規パッチの実 DLL への適用・実行検証：未実行。パッチ未実装のため。
- 全テスト：未実行。依頼に従って関連テストのみ実行した。

テスト追加、CHANGELOG、保存形式、Protocol の変更は行っていない。push は行わない。
