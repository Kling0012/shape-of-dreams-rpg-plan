# Issue #704 修正前の再現記録

同じ本体ゾーンでインフィニティの潜行を繰り返すと、担当外への攻撃の減衰が進まないことを再現した。通常敵とボスの他人の持ち分の両方が対象。本工程では製品コードを変更していない。

## 実行

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue704InfinityHelperDecayTests' --verbosity quiet -p:WarningLevel=0 --logger 'trx;LogFileName=issue704-repro.trx' --logger 'console;verbosity=normal'
```

再現テストは `tests/Issue73.Native.Tests/Issue704InfinityHelperDecayTests.cs`。修正前の実行は終了コード1、2件とも失敗した。コンパイルとテスト探索は完了し、失敗箇所はダメージ列の比較である。ログは既存の除外対象 `tests/Issue73.Native.Tests/TestResults/issue704-repro.trx` に保存した。

## 入力と結果

協力プレイ2人、担当制ON、両者とも夢2・使用星0。本体のゾーン番号0、ゾーン名 `Zone_Foo` を維持したまま、潜行0〜4回で100ダメージの攻撃を測った。

通常敵では、担当者以外が満HPの敵を攻撃する。ボスでは自分の持ち分を攻撃で使い切ったあとに他人の持ち分を攻撃する。持ち分を直接0に書き換えてはいない。

```text
潜行回数                  0    1    2    3    4
GraphEpoch                0    1    2    3    4
SegmentEpoch              0    1    2    3    4
本体ゾーン番号             0    0    0    0    0
観測したRunWorldIndex      0    0    0    0    0
期待ダメージ             100   70   40   10   10
通常敵の実ダメージ        100  100  100  100  100
ボスの実ダメージ          100  100  100  100  100
```

各区間で戦闘部屋のクリア、ボス突入、ボス撃破、魂の出現と消滅、確保地点到達、`Rules.Delve`、`CompleteGraphTransition` を製品の状態遷移で進める。警告はなかった。

## 判断したこと

- #700の変更は取り込み済み。`RunWorlds.cs` と `Coop.Generated.cs` により、現在の減衰率は0・30・60・90%。Issue #704本文の100・75・50%は旧仕様なので、期待値には現在の100・70・40・10%を使った。
- 再現の基準は依頼の方針どおり「潜行による次区間への移動ごとに進む」とした。夢の圧の段階を期待値の基準にはしていない。
- 現行コードではゾーン名が変わると累計が進むため、「すべてのインフィニティで必ず100%のまま」とは結論していない。同じゾーンを保つ潜行を再現対象にした。
- ゾーンを保持する移動は既存のStartupテスト `ExhaustionOrSingleCandidateKeepsZoneAndExistingCombatProgress` のdelveケースで扱われている。別ゾーンへの移動でも本体番号0とnoAdvanceを維持することは `DelveChoosesAcrossNativeZonesWithoutRepeatingAndReplaysSavedRunIdentity` が扱っている。

## 現行コードで確認した事実

- `HostAuthority.EnemyOwnership.cs:491` の `ObserveRunWorldCore` は本体番号とゾーン名を `RunWorldCounter.Observe` へ渡す。`RunWorlds.cs:29` は番号と名前が同じなら数えない。区間やグラフの進行はこの呼び出しの入力に含まれない。
- `HostAuthority.EnemyOwnership.cs:509` の `OwnershipHelpFactor` は `RunWorldIndex` を使う。通常敵は同ファイルの `ApplyOwnershipReceivedDamage`、ボスは `HostAuthority.BossShares.cs:175` の `PlanBossDamage` からこの係数を使う。両経路の失敗を再現テストで確認した。
- 表示は `DynamicStatusSnapshot` が同じ `RunWorldIndex` と減衰率を取り、`SyncDynamicDifficultyLegend` が `dynamicStatus` として送る。参加者は `ClientSession.ReceiveDynamicStatus` の値を表示する。同期経路の存在をコードで確認した範囲であり、実通信や画面は未確認。

## 実行した確認

- Startupの上記2メソッド: 終了0、3件成功。
- 追加前の通常敵の世界別係数・本体番号が戻る場合・ボスの世界別係数: 終了0、12件成功。
- 最終ソースで再現2件と上記Nativeの既存12件を同時実行: 14件中12件成功・再現2件失敗。ログは `TestResults/issue704-related.trx`。コマンドのfilterは `FullyQualifiedName~Issue704InfinityHelperDecayTests|FullyQualifiedName~Helpers_fight_at_the_world_factor|FullyQualifiedName~Helper_decay_follows_the_run_world_order|FullyQualifiedName~Strikes_drain_the_own_share_first`。
- `git diff --check`: 終了0。

最初のStartup実行は.NET 8ランタイム不在で起動できず、テストは未実行だった。環境にあるSDK 10.0.111・ランタイム10.0.11へ `DOTNET_ROLL_FORWARD=Major` で切り替えた後、上記3件が成功した。`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` は維持した。

再現テストの最初の実行では、通常敵は最終版と同じ失敗を示した。ボスは持ち分の初期値が割合であることを考慮せず攻撃量に使い、準備のassertで失敗した。初回攻撃を実HPの半分に直した最終実行では、自分の持ち分を使い切る前提が通り、減衰の比較で失敗している。製品コードは変更していない。

## 未実行と未解決

- 実ゲーム、`dreamforge_simkill`、実Harmonyパッチ、2台での通信、画面描画: この環境ではゲームを起動できないため未実行。
- 再現は既存のゲームAPI境界による。Native側ではInfinityMode本体がスタブのため、実際の `TravelToZone` は呼ばず、製品のInfinityRunStateとRulesで状態を進めた。移動の前提は別のStartupテストで確認した。
- MOD全体のビルド、全件・低速テスト: 再現工程の限定実行の指示に従い未実行。
- Continue、ホスト再構築、区間開始の同期後の表示値、別ゾーンをまたぐ累計: 今回の再現対象には追加せず、次工程の検証対象として残す。
- 審査の懸念については#700の反映、潜行を基準とする期待値、ボスにも共通する係数、表示値の同期経路を確認した。修正は次工程で行う。
