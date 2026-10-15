# 黒曜石・勢いによるクールダウン短縮後の発動不能：調査途中記録

2026-10-16。作業ブランチ `fix/cdr-lockout-1016`。

## 状態

報告されたマルチプレイでの発動不能は未再現で、原因は未特定。修正済みとは扱わない。
通常の有限値の短縮について、確認したコードから恒久的な発動不能を説明できていない。
最初の調査時点では Harmony パッチを追加していなかった。
その後、利用者の指定に従い、本体の数値境界を守る防御パッチを実装した（下記「防御パッチ」）。
報告された発動不能の原因を特定・修正確認したものではない。

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

## 防御パッチ（2026-10-16 追加）

実装は `src/SodRpg.Mod/NativeCooldownSafety.cs`。

- `ApplyCooldownReductionWithMinimum_Imp` と `ApplyCooldownReductionByRatio_Imp` の Prefix で、
  現在のサーバ処理対象を保持する。Finalizer で復元するため、ネストした呼び出しや例外で対象を残さない。
- `GetCooldownTimeMultiplier` の本体実装（AbilityTrigger / SkillTrigger / AttackTrigger）の Postfix は、
  対象のサーバ処理中に限り、最終倍率が `<= 0`・NaN・正負 Infinity なら `0.01` を返す。
  正の有限値は、`0.01` 未満でも変更しない。SkillTrigger の基底呼び出しの途中結果は補正せず、
  `sbCooldownMultiplier` 等を掛けた最終結果だけを補正する。SyncVar や設定値は書き換えない。
- 固定秒短縮は本体のループをそのまま実行し、短縮量と下限の両方が同じ安全倍率で割られる。
  チャージをまたぐ短縮、通知、下限の単位を維持する。割合短縮は本体が非スケール最大時間を使い、
  時間倍率で割らないため、割合・計算式を変更しない。同じ Prefix / Postfix で保護する。
- 両短縮メソッドの Postfix は残り時間配列を走査し、NaN・正負 Infinity・負値だけを `0` にする。
  変更した要素だけ、本体の `RpcSetCooldownTime` と `RpcSetCharge` をキャッシュ済みの
  open delegate で呼ぶ。チャージ数は維持し、次の本体 `LogicUpdate` に回復と通知を任せる。
  `SetCharge` は不足チャージと時間ゼロの組み合わせで時間を再設定するため、補正の同期には使わない。
- **必要な追加フック**：`AbilityTrigger.LogicUpdate` にも同じ対象保持 Prefix / Finalizer を置く。
  短縮後にゼロへ直しても、NaN の倍率なら直後の `dt / NaN` が `MoveTowards(0, 0, NaN)` を
  再び NaN にし、チャージ回復条件に到達しないため。ここでもサーバ・異常倍率だけに介入する。
  `ShouldTickCooldown`、究極技 config 0、ロック、通常のチャージ更新条件は変更しない。
- 配列走査中の reflection・LINQ・配列作成はない。RPC のデリゲート解決と対象探索は起動時のみ。
  RPC 内の本体 writer pool を再利用し、新しい RPC・保存形式・Protocol は追加しない。
- 対象メソッドは名前と引数型で解決し、見つからない短縮メソッドは対象一覧から除く。
  既存 RPC が解決できなければ短縮補正だけを無効にする。Harmony の適用失敗は既存の
  `PatchEachClass` が該当クラスだけロールバックし、MOD 全体を止めない。

## MOD 側の倍率源の結論

MOD 側の合算上限は**追加していない**。列挙された効果を線形の「1 - 短縮率合計」として扱う根拠がない。

本体の算出元を確認した。

- `EntityStatus.CalculateStats` は能力加速を `(基礎・レベル加算 + abilityHasteFlat) * (1 + abilityHastePercentage / 100)`
  として `finalStats` SyncVar へ設定する。
- `AbilityTrigger.GetCooldownTimeMultiplierByAbilityHaste(H)` は
  `Lerp(F, 1, 1 / (1 + H * 0.01))`。`F` は `ges.cooldownFloorRatioByAbilityHaste`。
  正の有限 H の加算で結果を線形にゼロ以下へ押し下げる式ではない。
- `SkillTrigger.UpdateSkillBonus` は `sbCooldownMultiplier` / 無視フラグ用 SyncVar を 1 から
  `SkillBonus.cooldownMultiplier` の積として作る。通常スキルの最終倍率は上記の加速倍率との積。
  MOD の SkillBonus 作成は移動チャージ用 `addedCharge` のみで、cooldownMultiplier を設定しない。
- `AttackTrigger` の時間倍率は `1 / owner.Status.attackSpeedMultiplier`。
  攻撃速度は本体が非有限値をフィルタしてゼロ以上にする。ゼロの場合の Infinity は今回のガード対象。

| MOD の源 | 実際の効果と根拠 |
| --- | --- |
| 星図 MemoryHaste / LinkKind.MemoryHaste / NativeModifier | `StarClusters.cs:322` からリンクまたは NativeModifier へ。`HostAuthority.cs:1895` で使用記憶ごとに合算し既に 100% 上限の割合短縮 API を呼ぶ。時間倍率 SyncVar には加算しない。 |
| Power.Momentum | `PowerRuntime.cs:534` の AttackSpeedPct。`HostAuthority.cs:2033` の attackSpeedPercentage に入り、スキル加速・SkillBonus には入らない。 |
| RunGrowth のスキル加速 / 通常 Stat.Haste | `HostAuthority.RunGrowth.cs:116` → `AddNativeStat` (`HostAuthority.cs:1989`) の abilityHasteFlat。RunGrowth は正の効果量、最大 500 スタック・効果増幅最大 500% を検証し、有限の加算。 |
| LinkKind.MemorySurge | `PowerRuntime.cs:527` で有効ソースの最大値を取り、AttackPct / PowerPct に加算。スキル加速・時間倍率には入らない。 |

`F` の実アセット値はローカルにないため未確認。外部の異常な本体設定、他 MOD による補正、
無制限の異常データまで正常だと証明したものではない。今回の列挙された MOD 効果に由来する
非正倍率の根拠は得られず、通常の正の有限値を変える `0.05` 下限や MOD 加算上限は入れない。

## 防御パッチ実装後に実行した検証

同じセッションで実行した。

- 最終 Release ビルド：成功、エラー0、警告6。
  `dotnet build src/SodRpg.Mod -c Release -p:GameDir='/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/sod-cdr-release-1016`
- 関連 Core テスト：130件成功、失敗0、スキップ0。
  `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~DirectedRechargeTests|FullyQualifiedName~MemoryGimmicksV128Tests|FullyQualifiedName~GimmickEffectsV129Tests|FullyQualifiedName~NewPowersV129Tests|FullyQualifiedName~PowerRuntimeTests'`
- 起動テスト：16件成功、失敗0、スキップ0。
  `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --filter 'FullyQualifiedName~NativeStartupTests'`
- `git diff --check`：成功。

Core テストは新パッチをコンパイルせず、既存 Startup テストもゲーム API のダブルを使う。
これらは新パッチの実 DLL 適用・ネットワーク同期・発動不能解消の証明にはならない。

## 未実行・未完了

- Unity 実機でのマルチプレイ再現・防御パッチ動作確認：**未実行**。
  ローカルの対象ゲームディレクトリには Managed DLL 等のみで、ゲーム実行ファイルと UnityPlayer がない。
  報告と同じ装備・能力値・ホスト／参加者での発動不能は未再現で、原因は未特定。
- 新規パッチの実 DLL への Harmony 適用・実行、異常値注入、実 RPC 受信、チャージ回復：**未実行**。
  Unity ランタイムがなく、テスト追加禁止のため新しい代替ハーネスも作成していない。
  Release ビルドによる本体 DLL 参照と API 整合性のみ確認した。
- 全テスト：**未実行**。依頼に従って関連テストのみ実行した。

テスト追加、CHANGELOG、保存形式、Protocol の変更は行っていない。push は行わない。
