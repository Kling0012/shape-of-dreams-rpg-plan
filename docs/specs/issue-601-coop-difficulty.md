# 協力プレイの序盤・高難度の緩和（Issue #601）

## PR #694取込後の仕様

Issue #601の最終要望「敵の数やHPを下げるのは簡単になりすぎました．分担の緩和で十分でした」に従い、v3.2.14の協力専用の緩和を撤回した。現在の圧HP・攻撃・敵数はソロと同じ式、本体人数ボーナスは本体の値、ボスHP合算係数は0.8となる。動的難易度Dは下限0.65でバランス上の上限はなく、報酬倍率は最大×2となる。担当外のダメージは後続の#701の仕様を優先し、遠征内の世界順に100%・70%・40%・10%、5世界目以降は10%を維持する。

悪夢化率の専用同期経路は取込前のmainに存在した。今回の差分による表示修正や経路の復元ではない。以下の「対象と判断」から「PR #681修正ラウンド」までの式・比較表・検証記録は過去の仕様を説明しており、現行仕様との差は末尾の取込記録に示す。

## 対象と判断

Issue の「高いリンボ」は本体の `GameMod_Limbo.depth` と解釈する。MOD の夢レベル、夢の深度、潜行 Heat、Infinity の圧段階とは別の設定である。本体 DLL の `GameMod_Limbo.GetStatBonus` には深さ2以上で攻撃・魔力+15%と防御+20、深さ5以上のボスに攻撃・移動速度+20%があり、深さ3以上では経験値・シールド等の制約も加わる。これらの選択した難度自体は維持し、その外側に重なる協力用強化を下げる。Limbo と Infinity は既存仕様で併用できない。

クリア率の目標は Issue にないため、次の数値を受け入れ範囲とした。

- 序盤：人数由来のHP・攻撃の**追加分**を半分にする。圧がない新規キャラクターの圧は1倍のまま、2人／4人ボスHPを1.6／3.2倍から1.4／2.8倍にする。
- 育成後：協力用の圧によるHP増分は従来の65%、攻撃増分は50%。深度・道標・Infinityまで掛け終えてから、HP8倍、攻撃2倍で止める。最低1倍を維持する。
- 圧による追加敵数は最大2.5倍（従来5倍）。自動難易度Dの協力時上限は1.5倍（従来3倍）。Dによる攻撃は引き続き `D^0.25`、下限0.65と苦戦時の下降速度は維持する。
- ソロは既存の圧・敵数・本体補正・任意の自動難易度を維持する。高難度の本体ルール、悪夢・変種・宿敵の性質、元敵の報酬を変更しない。
- 追加コメントの分担制緩和：他プレイヤーの担当敵・ボス持ち分へのダメージは、最初の世界100%、2つ目75%、3つ目以降50%。個人圧比による追加軽減と通常敵のHP30%下限は廃止する。

これはコード上の重複強化を抑える調整値であり、特定の構成やプレイスキルでのクリア可能性を実測したものではない。

## 確認した強化経路

| 経路 | 従来の処理と問題 | 今回の処理 |
| --- | --- | --- |
| 本体の人数補正 | `Monster.OnStartServer` が `GetMonsterBonusHealthPercentageByMultiplayer` と `GetMonsterBonusPowerPercentage` の結果を1つの `StatBonus` で追加する。生存者に加え、倒れた参加者も0.55人として補正に寄与 | 既存の `BossShareNativeHealth` フックで、このボーナスだけの追加HP・攻撃・魔力を半分にし、HP+100%、攻撃・魔力+15%までとする。Limbo等の別ボーナスは触らない |
| 部屋の圧 | `HostAuthority.RefreshPressure` が夢レベル・使用星を平均し、深度・道標・Infinityを乗算する。最大育成は深度0でもHP16.5倍・攻撃3.4倍 | 人間2人以上なら `WithCoopScaling`。担当制OFF・共有敵の経路にも有効 |
| 担当敵の圧 | `OwnershipBasePressure` が個人の圧を再構築するため、部屋だけの調整では担当敵とボス持ち分に届かない | 個人の圧にも同じ協力用圧を適用。担当変更・ビルド未受信の部屋圧への代替も同じ規則 |
| ボス・ミニボスHP | `PrepareBossShares` が各人の圧×Dを合計して0.8倍する。`OverrideBossShareHealth` は捕捉した本体の人数HPだけを除去するため、人数HPの二重掛けは既に防いでいる | 協力用圧を合計し、係数を0.7へ。既存の人数HP除去とボス子の除外を維持し、他人の持ち分への手伝い倍率は追加コメントの世界別指定に変更 |
| 圧による敵数 | `PressureEnemyCountMultiplier` → `InfinityIntervalScaling` → `PressureEnemyCount`。HPから0.25ごとに段換算、段ごと+8%、最大+400%。育成後は1体の耐久増と5倍の敵数が重なる | 緩和後HPから段換算し、協力時の圧由来の増加だけ+150%まで。本体の人口上限・ウェーブ数は維持 |
| 自動難易度 | `OnDynamicRoomLoading` が殲滅速度から次の部屋のDを決め、担当敵とボスHPに最大3倍、攻撃に `3^0.25` を乗算 | 協力時のD上限1.5。ソロの任意設定は上限3のまま |
| 悪夢・変種・道標 | `ApplyPressure` / `InstallPressureHealthProcessor` は本体・悪夢・変種の能力値計算後に圧を掛ける。Infinityの個人道標は#588により担当者本人のものを使う | それらの外側に掛かる圧を制限。悪夢化率・欠片は元の育成値を維持。表示用に上限適用前のHPを省略可能フィールドで同期する |

本体逆コンパイルの参照はローカルの `Shape of Dreams_Data/Managed/Dew.Core.dll`。本体の能力値は加算 `StatBonus` を合算するため、人数HP／Limbo／悪夢をすべて独立した乗算として見積もらない。ソロ倍率へ人数分を足し直す既存ボス処理も維持する。

## 数値原本と式

原本は `tools/balance/coop.json` の `difficulty` と `ownership`（ボスHP係数と世界別の手伝い倍率）。`coop_values.py` が検証し、`Coop.Generated.cs` へ生成する。実行時JSON読込やセーブ形式の変更はない。

従来の圧をH（HP）、A（攻撃）とすると、人間2人以上の戦闘用圧は次の値になる。

```
H_coop = 1 + min(7, (H - 1) * 0.65)
A_coop = 1 + min(1, (A - 1) * 0.50)
count_coop = min(2.5, 1 + min(4, floor((H_coop - 1) / 0.25 + 1e-9) * 0.08))
```

Dは出現時点で固定され、ボスHPは `sum(H_coop_i * D_i) * 0.7`。通常敵はさらに本体の能力値補正、悪夢等のボーナスがあり、ボス攻撃の個人圧比補正も従来どおり別に掛かる。ここで制限するのはMODの圧と協力用の追加強化であり、全種類の敵の最終HP・被ダメージを一律の値へ揃えるものではない。

Infinityの10／15部屋周期に設定された独立の敵数増加（+400%／+200%）は選択した周期の既存コストとして残す。上の敵数上限は**圧由来**の増加に適用し、周期由来の増加はその後に加算する。Limboの比較にはこの増加は入らない。

## 変更前後の倍率

全員が同じ育成・深度・道標、D=1。通常敵のHP・攻撃は圧だけ、敵数は圧由来だけ。ボスHPはソロ本体HPに対するMOD倍率で、本体の人数HPを除去した後の値。Limboの攻撃・防御・速度、悪夢の能力値、本体の人口補正は含めない。体数×HPは耐久量の比較用であり戦闘時間や勝率ではない。

| 条件 | 圧HP 前→後 | 圧攻撃 前→後 | 圧敵数 前→後 | 体数×圧HP 前→後 | 2人ボスHP 前→後 | 4人ボスHP 前→後 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 新規：夢Lv1・星0・深度0 | 1→1 | 1→1 | 1→1 | 1→1 | 1.6→1.4 | 3.2→2.8 |
| 序盤：夢Lv5・星20・深度0 | 1.56→1.364 | 1.08→1.04 | 1.16→1.08 | 1.8096→1.47312 | 2.496→1.9096 | 4.992→3.8192 |
| 最大育成：夢Lv30・星500・深度0 | 16.5→8 | 3.4→2 | 5→2.5 | 82.5→20 | 26.4→11.2 | 52.8→22.4 |
| 最大育成・深度5・儚い記憶 | 36.09375→8 | 5.95→2 | 5→2.5 | 180.46875→20 | 57.75→11.2 | 115.5→22.4 |
| 別軸の上限例：最大育成・深度5・深まる夜・Infinity100段 | 476.4375→8 | 35.7→2 | 5→2.5 | 2382.1875→20 | 762.3→11.2 | 1524.6→22.4 |

本体Limboの深さ2／5／6以上でも、同じMOD育成・道標条件なら上表の圧緩和が適用される。既に強い本体敵へ最大育成時のHP16.5〜36.09375倍・攻撃3.4〜5.95倍・敵数5倍を重ねていた部分を、それぞれ8倍・2倍・2.5倍に制限する。殲滅速度が速い部屋の後もD上限は1.5倍なので、最大育成時の圧×DはHP12倍、攻撃約2.2134倍まで（従来は深度0で49.5倍／約4.4747倍）。

本体人数補正の例（DLLのフィールド初期値を使用。実アセット値の保証ではなく、フックは実行時の値に同じ式を適用する）：

| 本体の追加分 | 2人 前→後 | 4人 前→後 |
| --- | ---: | ---: |
| 通常敵の攻撃・魔力（1人追加ごと+20%） | +20%→+10% | +60%→+15% |
| ミニボスの攻撃・魔力（同+17%） | +17%→+8.5% | +51%→+15% |
| ボスの攻撃・魔力（同+17%） | +17%→+8.5% | +51%→+15% |
| ミニボスHP（同+110%、持ち分への置換前） | +110%→+55% | +330%→+100% |
| ボスHP（同+120%、持ち分への置換前） | +120%→+60% | +360%→+100% |

## 比較の再実行

既存のBalanceSim `pressure` モードに1／2／4人の比較行を追加した。通常の1人用全軸は維持し、追加行にはHP・攻撃・敵数・欠片・悪夢化率・ボスHPを出す。

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/BalanceSim -c Release -- \
  --mode pressure --out /tmp/issue601-pressure.md --metrics-json /tmp/issue601-pressure.json
```

`pressure/coop/.../1` が従来と同じ1人用の圧、`.../2` と `.../4` が緩和後。変更前ボスHPは同条件の1人用HPに人数×0.8を掛ける。報酬用の圧は協力緩和から独立し、旧ホストの省略フィールド0は従来のHP同期値へ代替する。

## 要件との対応（今回の依頼のR1〜R5）

今回の依頼ではR1とR3が協力序盤、R2とR4が高リンボ等の難度上昇を指す。以下は今回の要件番号であり、後述するPR #677の過去の記録の要件番号とは区別する。

| 要件 | 実装箇所 | 対応内容 |
| --- | --- | --- |
| R1：マルチの序盤の難易度を緩和する | `BossShareNativeHealth.AddBonus`、`DreamPressure.WithCoopScaling`、`CoopBalance.Ownership.BossShareFactor` | 人数由来の追加HP・攻撃・魔力を半減。育成初期の圧増分と2／4人ボスHPも下げる |
| R2：高いリンボ等の難易度の跳ね上がりを緩和する | `HostAuthority.RefreshPressure`、`HostAuthority.OwnershipBasePressure`、`DreamPressure.CombatMultiplier` / `EnemyCountMultiplier` | 部屋と個人の双方で深度・道標適用後の圧を制限し、担当敵・共有敵・ボス持ち分に届ける |
| R3：マルチプレイにおいて序盤から難易度が高すぎる点を緩和する | R1と同じ実装に加え `HostAuthority.PrepareBossShares` | 夢Lv1・星0では圧1倍を維持し、人数の追加補正を半減。2／4人の初期ボスHPを1.6／3.2倍から1.4／2.8倍へ下げる |
| R4：高いリンボ等でクリア不可能レベルに跳ね上がる点を緩和する | R2と同じ実装に加え `HostAuthority.OnDynamicRoomLoading`、`HostAuthority.PrepareBossShares` | 圧HP8倍・攻撃2倍・圧敵数2.5倍・協力D1.5倍を上限とし、ボスHP合算係数も0.7へ下げる |
| R5：分担制を世界ごとに緩和する | `HostAuthority.OwnershipHelpFactor`、`ApplyOwnershipReceivedDamage`、`PlanBossDamage`、`tools/balance/coop.json:ownership` | 他の人の担当敵とボス持ち分へのダメージを1／2／3世界目以降で100／75／50%へ。個人圧比と通常敵のHP30%下限を外し、指定割合が通るようにする |

利用者の追加コメント「高いリンボなどにおいて特にクリア不可能レベルに難易度が跳ね上がっている」は今回のR2・R4に対応する。本体Limboの深さに関係なく、強化済みの敵へ重なる協力用の人数・圧・敵数・Dを緩和する。低深度限定の条件はない。前ラウンドでは本体DLLの `GameMod_Limbo.OnActorAdd` / `GetStatBonus` と `Monster.OnStartServer` を確認した。Limboのボーナスは前者で別に追加され、人数ボーナスだけを後者のフックで緩和する。`InstallPressureHealthProcessor` / `ApplyPressure` が最終能力値へ掛ける圧は部屋・個人の両経路で上限適用済みになるため、高リンボ専用の追加倍率は設けない。ゲーム内の踏破可能性は未確認。

## PR #673で実行した検証（過去の記録）

以下はPR #673の実装時とその修正ラウンド（2026-10-17）の記録であり、PR #677の今回の実行結果ではない。以下の63件・84件・166件・再現15件・Python6件、生成物チェック、Release MODビルド、BalanceSimは当時の修正ラウンドでも再実行して成功した。BalanceSimの新しい出力は `/tmp/issue601-r4-pressure.md` と `/tmp/issue601-r4-pressure.json`。1／2／4人の各比較行で上表の値と報酬・悪夢化率の維持を確認した。ソロ全1920行と旧式の全件照合は前ラウンドの結果であり、修正ラウンドでは再実行していない。

- 変更前の既存協力テスト：63件成功。
- 変更後の `EnemyOwnershipTests` / `BossSharesTests` / `DynamicDifficultyTests` / `Issue588PersonalWaypointPressureTests` / `CoopOmensWardTests`：63件成功。個人圧・担当変更・保護・流れ弾・ボスHPの合算と二重補正防止・分担・ソロ／OFF・道標の帰属を確認。協力用圧とD上限に合わせた既存期待値のみ更新し、新規テストは追加していない。
- Coreの夢の圧・深度・圧報酬・配当・接尾辞・Infinity周期の既存テスト：84件成功。従来のソロ最大育成HP15倍以上・敵数5倍以上の条件も維持。
- Coreの道標・悪夢・遠征耐久性・圧の進行記録・内容照合の既存テスト：166件成功。
- `python3 tests/repro/issue572/run.py`：15件成功。既存の表示・抽選再現ハーネスの代替APIに同期フィールドだけを追加した。
- `python -m unittest tools.tests.test_waypoint_balance tools.tests.test_stage6_balance`：6件成功。
- `python tools/balance/gen_cs.py --check`：成功。
- 本体DLL参照のRelease MODビルド：終了コード0、エラー0、既存警告6件。参照ディレクトリにModsはなく、ゲームへの配置は行っていない。
- BalanceSimの上記コマンド：終了コード0、実行したCoreから比較値を出力。既存ソロ全1920行を変更前の式と照合して差0、欠片・悪夢化率の1人対2／4人の比較は各10組で差0。
- `git diff --check`：成功。

途中で存在しない `tools.tests.test_stage5_balance` を指定してImportError（終了コード1）となった。該当モジュールはリポジトリになく、存在する上記2モジュールを指定し直して6件成功した。協力テストの最初の実行は旧期待値により20件失敗、更新後に丸め境界と部屋圧入力の旧値で2件失敗し、それぞれ仕様変更に追随した最終実行で63件成功した。

## 未実行

- ゲームを起動した協力序盤・Limboの踏破、Harmonyの実ゲーム適用、2台通信、確保地点の描画：ゲーム実行環境がなく、実機体感確認D1〜D3は依頼対象外。クリア可能性・体感難度を確認済みとはしない。
- フルテスト・遅いテスト：依頼に従い変更箇所の既存テストに限定し、パイプラインへ委ねる。

## PR #677の初回確認（2026-10-17、過去の記録）

PR #677の開始点 `15a379d4` には、PR #673の上記実装（`d9bdf45f`）が既に含まれていた。Issue本文と追加コメントに新しい調整値・追加症状はないため、同じ依頼でさらに緩和を重ねない。このPRの差分は、難易度の完了要件をR1〜R3に揃え、現在の実装を再検証した記録に限定する。以下は初回コミット `e8043b11` の作業時に実行した検証であり、今回の修正ラウンドとは区別する。

- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~EnemyOwnershipTests|FullyQualifiedName~BossSharesTests|FullyQualifiedName~DynamicDifficultyTests|FullyQualifiedName~Issue588PersonalWaypointPressureTests|FullyQualifiedName~CoopOmensWardTests' --nologo -v:quiet`：終了コード0、64件成功・失敗0・スキップ0。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter 'FullyQualifiedName~DreamPressureTests|FullyQualifiedName~PressureBalanceTests|FullyQualifiedName~PressureRewardMultiplierTests|FullyQualifiedName~PressureDividendTests|FullyQualifiedName~PressureSuffixCompatibilityTests|FullyQualifiedName~DreamDepthV130Tests|FullyQualifiedName~DepthRealignHealthTests|FullyQualifiedName~InfinityIntervalScalingTests|FullyQualifiedName~WaypointsTests|FullyQualifiedName~NightmareTests' --nologo -v:quiet`：終了コード0、133件成功・失敗0・スキップ0。
- `python3 tests/repro/issue572/run.py`：終了コード0、15件成功・失敗0。
- `python3 -m unittest tools.tests.test_waypoint_balance tools.tests.test_stage6_balance`：終了コード0、6件成功。
- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams' --nologo -v:quiet`：終了コード0、警告6・エラー0。参照先のModsディレクトリが存在しないことを事前確認し、ゲームへ配置していない。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/BalanceSim -c Release -- --mode pressure --out /tmp/issue601-current-pressure.md --metrics-json /tmp/issue601-current-pressure.json`：終了コード0。1／2／4人の比較90値は上の表と一致（ボス係数のfloat丸めを含む）。出力JSONのソロHP・攻撃1,920値を変更前から不変の `pressure.json` の式とPythonで照合し、すべて一致。協力2／4人の欠片・悪夢化率20値も同条件ソロとすべて一致した。

未実行は上記D1〜D3、Unity内のHarmony適用・実通信・描画と、フル／遅いテスト。実機でのクリア可否を判定する追加調整は行っていない。

## PR #677の修正ラウンド：CI代替検証（R4、2026-10-17）

追加の[PRコメント](https://github.com/Kling0012/shape-of-dreams-rpg-plan/pull/677#issuecomment-6228525648)の内容は、CIが4分以内に作成されなかったため、`e8043b1175f71946564820a6ba88d869387c27ee` に対してLabで代替検証を実施したという報告である。Release MODビルド、`gen_cs.py --check`、`test_changed.py` が終了コード0と記録されている。最後のコマンドは文書1ファイルのみを検出し、実行対象テストなしと判定している。このLab結果はコメントから参照した過去の実行結果であり、今回のローカル実行とは区別する。

PR #677の修正ラウンドにおけるR4は、この代替検証の確認・記録を指す（今回の依頼の高リンボ緩和R4とは異なる）。新しい症状・調整値・CI設定の変更要求は含まれていないため、当時のR1〜R3の既存実装と数値は維持した。CIが作成されなかった原因の特定や、ゲーム内の踏破確認を完了したとはしない。

今回の修正ラウンドでは、同じworktreeで以下を実際に実行した。パッケージキャッシュは既定のworktree外の `/home/wang/.cache/gh-auto/nuget` を使用した。

- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/gh-auto-moddeploy --nologo -v:quiet`：終了コード0、エラー0・警告6。コメントと同じ参照先・配置先指定で成功した。参照先にModsディレクトリが存在しないことを事前確認し、ゲームへの配置は行っていない。
- `python3 tools/balance/gen_cs.py --check`：終了コード0。入力・生成物に変更なしとキャッシュ判定され、再生成は省略された。
- `python3 tools/test_changed.py`：終了コード0。比較基準は `15a379d4`、変更は文書1ファイルのみ。実行対象テストなしという結果であり、回帰テストを実行したという意味ではない。
- 初回確認と同じフィルター・コマンドで `Issue73.Native.Tests` の協力関連5クラスを実行：終了コード0、64成功・失敗0・スキップ0。
- 初回確認と同じフィルター・コマンドで `SodRpg.Core.Tests` の圧・深度・報酬・道標・悪夢関連10クラスを実行：終了コード0、133成功・失敗0・スキップ0。

今回未実行：ゲーム実行が必要なD1〜D3・Unity内Harmony適用・実通信・描画、フル／遅いテスト。再現ハーネスissue572・Pythonの道標／stage6テスト・BalanceSimの数値照合・本体DLLの再逆コンパイルも、実装・数値に追加変更がないため今回の修正ラウンドでは再実行していない。GitHub CIの再作成とLabでの再実行は行わず、上記ローカル検証とコメントのLab記録を区別して報告する。

## Issue #601再依頼の初回確認（追加コメント前の記録、2026-10-17、開始点5569b2a8）

### 判断したこと

Issue本文・コメントは協力序盤の緩和と高リンボでの難度上昇の緩和であり、今回のR1／R3、R2／R4はそれぞれ同じ要望を表す。開始点には既存の緩和コミット `d9bdf45f` とPR #677のマージ `e583a536` が既に含まれることを `git merge-base --is-ancestor` で確認した。既存コードは上のR1〜R4すべてに対応していたため、初回確認では同じ要望で二重に緩和せず、要件対応の訂正と再検証記録のみを追加した。その後の追加コメントR5を受け、下記の修正ラウンドでは分担制の実行コード・数値と既存テストを変更した。

受付時の「条件・目標値が不明」という懸念には、上の受け入れ範囲と、同一育成の1／2／4人の固定入力比較で対応する。実測の勝率や踏破可能性を完了基準に含めず、ソロと報酬を維持しながら人数由来の増分と協力用の圧・敵数・Dを制限できることを確認した。

### 実行した検証

以下はこの再依頼で実際に実行した結果であり、前節までの過去の実行結果とは区別する。NuGetキャッシュはworktree外の `/home/wang/.cache/gh-auto/nuget` を使用した。

- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~EnemyOwnershipTests|FullyQualifiedName~BossSharesTests|FullyQualifiedName~DynamicDifficultyTests|FullyQualifiedName~Issue588PersonalWaypointPressureTests|FullyQualifiedName~CoopOmensWardTests' --nologo -v:quiet`：終了コード0、64成功・失敗0・スキップ0。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter 'FullyQualifiedName~DreamPressureTests|FullyQualifiedName~PressureBalanceTests|FullyQualifiedName~PressureRewardMultiplierTests|FullyQualifiedName~PressureDividendTests|FullyQualifiedName~PressureSuffixCompatibilityTests|FullyQualifiedName~DreamDepthV130Tests|FullyQualifiedName~DepthRealignHealthTests|FullyQualifiedName~InfinityIntervalScalingTests|FullyQualifiedName~WaypointsTests|FullyQualifiedName~NightmareTests' --nologo -v:quiet`：終了コード0、133成功・失敗0・スキップ0。
- `python3 tests/repro/issue572/run.py`：終了コード0、15成功・失敗0。
- `python3 -m unittest tools.tests.test_waypoint_balance tools.tests.test_stage6_balance`：終了コード0、6成功。
- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/gh-auto-moddeploy --nologo -v:quiet`：終了コード0、エラー0・既存警告6件。参照先のModsディレクトリは存在せず、ゲームへの配置はない。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/BalanceSim -c Release -- --mode pressure --out /tmp/issue601-rerequest-pressure.md --metrics-json /tmp/issue601-rerequest-pressure.json`：終了コード0。5条件×1／2／4人の圧HP・攻撃・敵数・ボスHPは上の比較表の値を出力した（ボス係数0.7fの丸めを含む）。出力JSONをPythonで照合し、ソロHP・攻撃1,920値は変更前から不変の `pressure.json` の式と差0、協力2／4人の欠片・悪夢化率20値は同条件ソロと差0、終了コード0。
- `git diff d9bdf45f^ HEAD -- tools/balance/pressure.json`：終了コード0、差分なし。ソロの数値原本を変更していない。
- `git diff --check`：終了コード0。

### 未実行の検証（理由付き）

- ゲームを起動した協力序盤・高リンボの再現と踏破、Unity内でのHarmony適用・実通信・描画：この環境ではゲームを実行できない。体感確認D1〜D3は対象外であり、実プレイでクリア可能と確認したという意味ではない。
- フル／遅いテスト（`test_changed.py --all`・`SODRPG_SLOW=1`）：依頼に従い関連する既存テストへ限定し、パイプラインへ委ねる。
- 本体DLLの再逆コンパイルと修正を戻す再現テスト：追加コメント前の初回確認は実行コード・数値の差分がなかったため未実行。既存の倍率接続部分はコード確認と本体DLL参照ビルドで確認した。

## PR #681修正ラウンド：分担制の世界別緩和（R5、2026-10-17）

### 判断したこと

追加コメントの「最初の世界ではお互いのダメージは完全に通り、2つ目で25%off、3で50%off」は、他プレイヤーの担当する通常敵およびボス・ミニボスの他人の持ち分に対する、分担制によるダメージ制限を指すと解釈した。味方同士への攻撃を追加する要望ではない。`ZoneManager.currentZoneIndex` は0始まりなので、0を100%、1を75%、2以上を50%とする。4世界目以降は最後に指定された50%を維持し、世界情報が未取得なら追加軽減をしない。

旧処理は世界番号を見ず、通常敵では `0.5 * min(1, P担当 / P攻撃)` に加えてHP30%下限、ボスの他人の持ち分でも同様の圧比を掛けていた。そのため最初の世界から50%以下に減り、通常敵の残りHP30%以下は手伝いダメージが0になる。倍率だけを100%にしても圧比や下限が残るため、「完全に通る」を満たさない。これらを世界別倍率へ置き換え、通常敵の下限用定数も原本・生成器から除いた。

- `OwnershipHelpFactor` がホストの現在世界から原本の倍率を選び、`ApplyOwnershipReceivedDamage` と `PlanBossDamage` の両方が使う。通常敵は健康かつ交戦中の担当者がいるときに軽減し、担当者の低HP・KO・無交戦時の救援解除は維持する。
- 自分の担当・自分のボス持ち分は100%。ボスの自分の持ち分をまたぐ一撃は、超過部分だけ世界倍率を適用し、HP確定時の再配分も同じ規則を使う。
- 割り当て・個人圧による敵の強さ・流れ弾の被害補正・召喚体の帰属・報酬・ソロ・担当制OFFの経路は維持する。追加のコレクションや毎フレームの確保はない。
- R1〜R4は開始点に含まれる既存緩和を維持する。受付時の条件・目標値不足の懸念には上記の数値基準と固定入力の既存テストで対応し、R5には利用者指定の100／75／50%を使う。体感での踏破確認を完了基準には含めない。

### この修正ラウンドで実行した検証

NuGetキャッシュはworktree外の `/home/wang/.cache/gh-auto/nuget` を使用した。テストは新規作成せず、既存の担当制・ボス持ち分の入力と期待値を世界別倍率に合わせて更新し、自動難度の測定テストは3世界目の50%軽減を使うよう最小限変更した。

- 実装変更前に更新済みの `EnemyOwnershipTests` / `BossSharesTests` を実行：42件中31成功・11失敗。旧実装が指定割合やHP30%以下での手伝いに一致しないことを確認した。これは本体の起動・実プレイによる再現ではなく、リンクした実ソースのダメージ入口による再現である。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~EnemyOwnershipTests|FullyQualifiedName~BossSharesTests|FullyQualifiedName~DynamicDifficultyTests|FullyQualifiedName~Issue588PersonalWaypointPressureTests|FullyQualifiedName~CoopOmensWardTests|FullyQualifiedName~Issue608ZeroShareReproductionTests' --nologo -v:quiet -p:WarningLevel=0`：73成功・失敗0・スキップ0。通常敵・召喚攻撃・低HPの敵・ボス持ち分の100／75／50%、4世界目の50%、既存の割り当て・救援・再配分・0%持ち分からの撃破を確認した。
- 初回の関連73件実行では、自動難度の測定テスト1件が旧HP下限の期待値に依存して失敗した。上記の最小限の修正後は成功した。
- 戻し確認：実行コード2ファイルを開始点の旧実装へ一時的に戻し、除去済みのHP下限定数の参照だけ同値の `0.3f` に置換して実行。上記42件は同じ11件が失敗・31件成功、終了コード1。現行実装を復元し、関連73件の成功を再確認した。
- 初回確認と同じCoreの10クラスを `-p:WarningLevel=0` 付きで実行：133成功・失敗0・スキップ0。圧・深度・圧報酬・配当・接尾辞・Infinity周期・道標・悪夢の既存条件を維持する。
- `python3 tools/balance/gen_cs.py` と `python3 tools/balance/gen_cs.py --check`：終了コード0、原本から生成した `Coop.Generated.cs` と整合。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/gh-auto-moddeploy --nologo -v:quiet`：終了コード0、エラー0・既存警告6件。参照先にModsディレクトリはなく、ゲームへの配置なし。
- `python3 tests/repro/issue572/run.py`：15成功・失敗0、終了コード0。
- `python3 -m unittest tools.tests.test_waypoint_balance tools.tests.test_stage6_balance`：6成功、終了コード0。

### この修正ラウンドの未実行

- 実ゲームでの序盤・高リンボ・世界移動・踏破確認、Unity内Harmony適用・実通信・描画：ゲームを起動できない環境のため。体感確認D1〜D3は依頼対象外。
- フル／遅いテスト（`test_changed.py --all`・`SODRPG_SLOW=1`）、対象無指定の `test_changed.py`：依頼に従い関連テストへ限定。balance差分は全体選択になるためパイプラインへ委ねる。
- BalanceSimの再実行とソロ全数値の再照合、本体DLLの再逆コンパイル：今回の差分は分担制のダメージ制限であり、圧・敵数・本体人数ボーナス・報酬の式は変更していない。上の初回確認結果は過去の記録として保持する。
- GitHub CI・Labでの再実行：このラウンドではローカルの関連テスト・参照DLLビルドを実行し、外部パイプラインは起動していない。

## PR #694の取込記録（2026-10-17）

### 判断したこと

元PR #694の目的は、敵数とHPなどを下げる協力専用の緩和を戻し、分担による難度調整を残すことと解釈した。取込先の開始点 `aa53fa87` には#701が含まれ、分担倍率は遠征内の世界順に100%・70%・40%・10%へ更新されていた。元PR本文の100%・75%・50%へは戻さず、世界順の記録、動的難易度の状況表示、救援、#608のボス持ち分処理を維持した。

### 撤回した緩和

- `DreamPressure.WithCoopScaling` と `CombatMultiplier` を除き、部屋・担当者の双方の圧にソロと同じ式を適用した。協力専用のHP8倍・攻撃2倍・敵数2.5倍の上限も撤回した。
- `BossShareNativeHealth.AddBonus` の本体人数ボーナスの半減と上限を撤回した。ボスHPの二重掛けを防ぐ既存の人数HP捕捉は残した。
- 取込時は協力時D上限1.5を撤回し、ソロ・協力共通のD上限3へ戻した。同日の後続変更でこの上限も外した（下記「動的難易度の上限撤廃」を参照）。`ownership.bossShareFactor` は0.7から0.8へ戻した。
- `coop.json` と生成器の `difficulty` セクションを除き、生成物を更新した。BalanceSimの `pressure/coop` 行を撤回し、READMEは現行仕様へ書き換えた。

### 難度への影響

次はv3.2.14の緩和あり→今回の撤回後の比較。深度0・道標なし・Infinity段階0・D=1、全員が同じ育成を仮定する。HP・攻撃・敵数は圧だけ、ボスHPは本体人数HPを除いたMOD倍率。本体Limboの能力値や悪夢の補正は含まない。

| 育成 | 圧HP | 圧攻撃 | 圧敵数 | 2人ボスHP | 4人ボスHP |
| --- | ---: | ---: | ---: | ---: | ---: |
| 夢Lv1・星0 | 1→1 | 1→1 | 1→1 | 1.4→1.6 | 2.8→3.2 |
| 夢Lv5・星20 | 1.364→1.56 | 1.04→1.08 | 1.08→1.16 | 1.9096→2.496 | 3.8192→4.992 |
| 夢Lv30・星500 | 8→16.5 | 2→3.4 | 2.5→5 | 11.2→26.4 | 22.4→52.8 |

育成が進んだ協力プレイや高リンボではv3.2.14より敵が強くなる。これは最終要望に沿った緩和の撤回であり、踏破可能性を確認した結果ではない。ソロ・圧の報酬式・悪夢化率の式は変更していない。

### 受付時の品質懸念への対応

悪夢化率の専用同期経路は開始点に存在し、`NetMessages`・`SendPressure`・`OnPressure`・`CacheSecureText` の機能差分はない。`ClientSession` の差分は初期化行の字下げだけである。「今回同期経路を復元した」「今回表示を修正した」という説明は引き継がない。既存の再現テストには元PRのInfinity段階10と表示専用値の入力を取り込んだが、これは表示計算とキャッシュの確認であり、実通信の受信確認ではない。

元PRの `changelog.d/694-nightmare-pressure.md` は取り込まない。現行mainに対する表示修正がなく、差分はバランス調整とその文書・検証の更新のため、`changelog.d/README.md` に従い更新履歴の断片を作らない。協力専用の緩和の撤回は、この節とPR本文に明記する。

### 今回実行した検証

NuGetキャッシュは環境変数のworktree外の `/home/wang/.cache/gh-auto/nuget` を使用した。テストは新規作成せず、元PRの既存テストの期待値変更と再現入力を取り込んだ。

- 取り込み前の既存Nativeテスト6クラス：78成功・失敗0・スキップ0。
- 取り込み後の `EnemyOwnershipTests|BossSharesTests|DynamicDifficultyTests|Issue588PersonalWaypointPressureTests|CoopOmensWardTests|Issue608ZeroShareReproductionTests`：78成功・失敗0・スキップ0。担当制、ボスHP、D上限、道標、救援、#608、#701の世界順と分担倍率を含む。
- Coreの `DreamPressureTests|PressureBalanceTests|PressureRewardMultiplierTests|PressureDividendTests|PressureSuffixCompatibilityTests|DreamDepthV130Tests|DepthRealignHealthTests|InfinityIntervalScalingTests|WaypointsTests|NightmareTests|RunWorldOwnershipTests`：144成功・失敗0・スキップ0。
- `python3 tests/repro/issue572/run.py`：15成功・失敗0。表示用の圧を手動設定する既存ハーネスであり、通信は対象外。
- `python3 -m unittest tools.tests.test_coop_balance tools.tests.test_waypoint_balance tools.tests.test_stage6_balance`：9成功。
- `python3 tools/balance/gen_cs.py` と `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/pr694-build-only --nologo -v:quiet`：成功、エラー0・既存警告6。参照先にModsディレクトリがなく、ゲームへの配置なし。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project tools/BalanceSim -c Release -- --mode pressure --out /tmp/pr694-pressure.md --metrics-json /tmp/pr694-pressure.json`：成功。出力2,250値のうちHP・攻撃1,920値を `pressure.json` の共通式と照合してすべて一致し、撤回対象の `pressure/coop` 行がないことも確認した。

### 今回未実行の検証

- 実ゲームでの協力序盤・高リンボの体感、UnityでのHarmony適用・実通信の受信・描画：ゲームを起動できない環境のため。専用同期経路が実機で動いたことや、踏破できたことは確認済みとしない。
- フル／遅いテスト（`test_changed.py --all`・`SODRPG_SLOW=1`）：依頼に従い関連する既存テストに限定し、後段のパイプラインへ委ねる。
- GitHub CI・Labでの再実行：この作業はローカルのコミットまでが対象のため。
- 本体DLLの再逆コンパイルと修正を一時的に戻す検証：既存処理を導入前の式へ戻す変更であり、関連テスト、数値照合、参照DLLビルドを実行したため。
- yomiyasu_checkのrun/verify：ハッシュ検証を行わないという依頼に従い、指定のlintと文章・差分の目視点検を実施した。

## 動的難易度の上限撤廃（2026-10-17）

大きなダメージを出すほど敵を強化するため、Dのバランス上の上限3を外した。比率・対数平均・`exp(strength × 平均)` の式と下限0.65は変えず、担当敵とボスHPにはD、攻撃には `D^0.25` を適用する。ボスの持ち分の計算は変更しない。

報酬上限は `dynamicDifficulty.rewardMaxMultiplier=2.0` として独立させ、`min(rewardMaxMultiplier, 1 + rewardShare × max(0, D − 1))` で計算する。従来どおり最大×2とし、Dが1未満でも報酬は減らない。撃破報酬・イベント記録・F6の報酬表示に同じ上限を使う。

`dynamicDifficulty.safetyMaxFactor=1000000` はInfinity・NaNやfloatのHPの桁あふれを防ぐためだけの安全装置で、バランス上の上限ではない。生成器は `minFactor <= 1`、`rewardMaxMultiplier >= 1`、`1000 <= safetyMaxFactor <= 1000000` を検証する。異常値への既存のtry/catchと機能単位の停止は維持する。上のD上限3・協力時上限1.5の記述と検証結果は過去の記録であり、現行仕様はこの節を優先する。
