# Issue #608 修正前の再現記録

「エリートが分担の横が0%のまま無敵化した」を、製品のホスト側コードとゲームAPI境界で再現した。本工程では製品コードを変更していない。

## 実行

```sh
python3 tests/repro/issue608/run.py
```

- 再現ケース: `tests/Issue73.Native.Tests/Issue608ZeroShareReproductionTests.cs`。
- 修正前の結果: **終了コード1、2件中1件失敗・1件成功**。ビルドと探索は成功し、失敗は「256回攻撃してもミニボスが生きている」というassertである。
- 最終実行: `tests/Issue73.Native.Tests/TestResults/issue608-20261017T000641540055Z/` の `repro.log` / `repro.trx` / `result.json`（既存のgit除外対象）。
- スクリプトは実行ごとに一意のログを保存する。ビルド失敗などでTRXがない場合は `harness_error` と終了2を返し、再現成功と区別する。
- 通常の `dotnet test` でも再現ケースは実行対象になる。補助スクリプトは、既存共有スタブを変更せず、本体のDamageDataの遅延倍率演算に差し替えて精度を再確認するためのもの。

## 判断したこと

- Issue #608の本文は「エリートが分担の横が0%のまま無敵化した」の1行、コメント0件だった。`gh issue view ... --comments` は出力が空だったため、`gh api repos/Kling0012/shape-of-dreams-rpg-plan/issues/608` と `/comments` で本文とコメント数を取得した。
- 報告のエリートは、本リポジトリの `MonsterTier.MiniBoss` / 本体の `Monster.MonsterType.MiniBoss` と解釈する。`DreamforgeUi.cs:727` と `:745` がこの対応を示す。
- 「分担の横の割合」は `DreamforgeUi.BossShares.cs:117` の `MiniBossShareMark` が表示する自分の持ち分の残量と解釈する。通常の `OwnershipMark` は番号と形だけで割合を出さない。`DreamforgeUi.cs:1207` はミニボスに前者を優先する。
- 割合は `HostAuthority.EnemyOwnershipSync.cs:68` / `:83` で `Remaining / Capacity` を0..1000へ丸め、UIで `(remaining + 5) / 10` とする。0%は自分の持ち分が尽きた（または非常に少ない）表示であり、HP倍率や悪夢化の確率ではない。#572の確保地点の悪夢化率表示とは別の経路である。
- 本再現は「無敵効果が付く」ことを意味しない。ダメージ処理が本体の閾値で終了し、実質的に倒せない状態を再現している。実際の報告者の入力やゲーム上の発生状況との同一性は未確認。

## 固定入力と観測

協力プレイ2人・担当制ON・動的難易度OFF。悪夢化・宿敵化は不要。防御0、シールド0、無敵なし、攻撃効果なし。テストはHPや残持ち分を微小値に直接設定せず、満HP・全員の持ち分100%から開始する。

| 入力 | 攻撃者 | もう1人 | ミニボスの初期HP | 1回の入力ダメージ | 結果 |
| --- | --- | --- | --- | --- | --- |
| 再現 | 夢46・使用星94 | 夢38・使用星234 | 22946.146484375 | 35967.796875 | 256回後もHP 0.00006103515625、自分の表示0%、生存 |
| 対照 | 夢2・使用星0 | 夢8・使用星40 | 1000 | 100 | 17回でHP 0、撃破 |

再現入力の最初の4回（表示はC#のround-trip形式）:

```text
hit=1, before=22946.146, after=811.10547, processed=22135.041
hit=2, before=811.10547, after=6.1035156E-05, processed=811.1054
hit=3, before=6.1035156E-05, after=6.1035156E-05, processed=6.1035156E-05
hit=4, before=6.1035156E-05, after=6.1035156E-05, processed=6.1035156E-05
```

256回後の持ち分は `[2.0713945991849665E-12, 6.10351541786054E-05, 0, 0]`。丸め後の自分の表示は0%。警告・例外はない。毎攻撃後の表示更新が原因にならないよう、Snapshotは初期値の確認と最終値の観測のみで呼ぶ。

## 次工程で参照する因果

1. `HostAuthority.BossShares.cs:163` の `PlanBossDamage` は自分の持ち分を消費し、残りを手伝い倍率で他の持ち分へ割り振る。
2. 同ファイル `:222` は受け入れた持ち分量で入力ダメージを制限し、`:228` のfloat倍率を重ねる。受け入れ量が残HP付近のとき、float演算で減算結果に端数が残る。
3. `:251` 以降は本体が実際に減らしたHP量を使い、`:260` で残持ち分の合計を実HPへ戻す。ここでは死亡閾値より大きい端数が残っている。
4. 以後の攻撃も持ち分の残量まで抑えられる。参照した本体 `Dew.Core/Actor.cs:900` は `finalDamageData.amount + discardedAmount <= 0.0001f` なら、HP setterと死亡判定の前にreturnする。
5. 本体 `Actor.cs:1054` の死亡判定はHP `<= 0.00001f`。観測したHPはこの閾値より大きく、次のダメージは処理下限以下なので進行しない。

「0%だから倍率0」や「持ち主不在が原因」という推測を前提にせず、上記の端数・打ち切りの整合が修正担当の確認対象になる。修正案の実装は行っていない。

## 環境とテストの境界

- 基点 `c5488cd6`、ブランチ `ai/issue-608`、Linux、SDK 10.0.111、実行ランタイム .NET 10.0.11。
- net8.0プロジェクトを `DOTNET_ROLL_FORWARD=Major` で実行。`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を維持し、キャッシュをworktreeへ移していない。
- `HostAuthority.EnemyOwnership.cs` / `HostAuthority.BossShares.cs` / `HostAuthority.EnemyOwnershipSync.cs` は既存テストプロジェクトが製品コードをそのままリンクする。分配計算をテスト側へコピーしていない。
- ゲームAPIは既存NativeBoundary等の代替。本体の `Actor.DealDamage` 全体、Harmonyの実パッチ、Unityを動かすテストではない。既存境界の `CalculateStatsIfDirty` は本体の全能力・人数補正を再現しないため、22946.146484375は戦闘開始時の実HPに相当するテスト入力として扱う。
- 本再現のStrikeには、`/home/wang/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/Actor.cs:900` の小ダメージ打ち切りと `:1054` の死亡閾値を反映する。`FinalDamageData.cs:29` のオーバーキル分離も反映し、過剰ダメージを捨てたせいで閾値に入る誤再現を避ける。
- 補助スクリプトは `NativeBoundary.cs` のコピーを除外済みの `obj/issue608/<日時>/` に生成し、DamageDataだけ本体と同じ `originalAmount * amplificationMultiplier * reductionMultiplier` の順序へ置換する。ターゲットは `Issue73.Native.Tests` だけに適用し、通常の共有スタブ・製品コードは編集しない。
- 入力探索中の補助スクリプトは、1回目にCoreまで境界を適用してコンパイルエラー、2回目に他テストのamount代入との互換不足でコンパイルエラーになった。対象制限と代入用互換プロパティを直した後、上記の実行はビルド・探索・assert実行まで完了している。これらの初期失敗は再現結果に数えない。

## 実行済みの確認

- 追加前の `BossSharesTests|EnemyOwnershipTests`: 終了0、34件成功。持ち分消費後の手伝い・切断時の配り直し・KO・担当者の切断・通常敵の保護等は既存ケースで確認できた。ボス持ち分は有効な正の個人圧を前提に生成される（`HostAuthority.BossShares.cs:53`）。
- 単純な2人構成9入力と端数HP・個人圧の72入力: 既存境界でも本体倍率境界でも、撃破を確認した。これだけでは未再現だったため、固定seed608で端数入力を探索し、上記の失敗入力を抽出した。探索ループは最終テストから外し、固定の再現1件と対照1件だけ残した。
- 既存境界のままの固定2件: 終了1、1件失敗・1件成功（`TestResults/issue608-default-boundary.trx`）。追加した本体の打ち切り判定で同じ症状が出る。
- 本体倍率境界の最終固定2件: 終了1、1件失敗・1件成功。表示更新を攻撃ごとに挟まなくても同じ結果。
- 最終ソースで `BossSharesTests|EnemyOwnershipTests|Issue608ZeroShareReproductionTests` を通常の `dotnet test -c Release` で実行: **終了1、36件中35件成功・再現1件失敗**。既存34件と対照1件の成功を確認した（`TestResults/issue608-final.trx`）。

## 未実行・未解決

- 実ゲーム起動・画面描画・`dreamforge_simkill`: この環境ではゲームを起動できないため未実行。`dreamforge_simkill` は通常の攻撃処理ではなく撃破シミュレーションなので、この端数停止の再現証拠にしない。
- 実Harmonyフック・実通信・2台の協力プレイ・実装版ゲームDLLによる完全なActor.DealDamage: 本工程は既存のAPI境界による再現のため未実行。
- 防御・シールド・回復・入退室・宿敵化・悪夢化との組み合わせ: 再現入力に不要だったため、新規の組み合わせ検証は未実行。既存テストの成功を全組み合わせの保証には使わない。
- MOD全体のビルド、全件・低速テスト: 再現工程は限定実行の指示があるため未実行。
- 受け付け審査の「割合の正体」「持ち主不在・倍率0」の懸念は、表示と計算経路、既存の切断/KOケースの結果を記録した。修正は次工程であり、本工程では行っていない。
