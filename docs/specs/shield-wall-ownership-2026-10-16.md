# 盾壁と敵担当制の接続（2026-10-16）

## 動作

太陽の洗礼の生成時、盾壁が6m以内の味方を狙う敵へ `AI.Aggro` する前に、既存の敵担当記録へ挑発対象のHeroと期限を記録する。`SelectOwnershipAggro` と `RetargetOwnership` は有効な挑発を担当者や担当者の召喚体より優先する。再標的時に既に挑発対象を狙っていれば `Aggro` を再実行せず、本体の再標的タイマーを毎tickリセットしない。

本体DLLの `Ai_R_BaptismOfSun.OnHit` は単発の `Aggro` を行うだけで、挑発持続時間を持たない。`Se_R_BaptismOfSun_Buff.duration` は攻撃強化・加速の時間であり、挑発時間ではない。coop.json・stars.jsonにも既存の挑発時間がないため、依頼で指定された代替値として `tools/balance/coop.json` に `shieldWallTauntSeconds: 4` を新設した。

期限切れまたは挑発対象の戦闘不能・離脱等で記録を消し、次の既存AI更新で担当者へ戻る（担当者が本来の標的条件を満たす場合）。挑発の有効性には本体の `CanAggroTo` と敵対関係・接続・生存を使い、新たな索敵距離制限は加えない。担当者・個人圧・手伝い保護・HP下限・流れ弾の計算は変更しない。

担当制OFFまたはソロでは記録を使わず、盾壁の従来の単発Aggroを維持する。OFF切替・既存走査でのソロ判定・敵記録の返却時に挑発を消す。例外は既存のTargeting単位のfail-soft経路を使う。新しい毎フレームのコレクションや割り当ては追加していない。

## 検証

- 修正前、盾壁が指定するヴェスパーを `SelectOwnershipAggro` に渡す再現テストが `Assert.Same` で失敗し、担当者に上書きされる症状を確認した。Unityの実プレイ上での再現ではない。
- `EnemyOwnershipTests` に4件追加。実ソースの `CoopDrawThreats`・`CoopPair`・`CoopEnemy`・部分メソッド宣言を既存のRoslyn抽出方式で取り込み、実際の盾壁処理と担当制を同じ境界で実行する。テスト境界のAggroは本体Harmony入口と同じ担当判定を通す。
- 4件の追加テスト：挑発中の優先とダメージ扱い維持、期限ちょうどでの復帰と6m境界、OFF・ソロ、戦闘不能・記録再利用を検証し、全件合格。
- `DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet test tests/Issue73.Native.Tests -c Release --no-build --logger 'console;verbosity=minimal'`：240件合格、0失敗、0スキップ。先行する追加テスト実行時にReleaseでビルド済み。
- `DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet test tests/SodRpg.Mod.Startup.Tests -c Release --logger 'console;verbosity=minimal' -p:WarningLevel=0`：147件合格、0失敗、既存スキップ1件（`ModReloadTests.SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch`）。
- `DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet build src/SodRpg.Mod -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/sod-integrate-vesper-build -v:minimal`：0エラー、警告6件。実ゲームDLL参照によるビルド。ゲームへの配置・起動は行っていない。
- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- `DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet test tests/SodRpg.Core.Tests -c Release --filter 'FullyQualifiedName~CoopKeystonesNativeTests|FullyQualifiedName~MeleeCoopNativeTests' --logger 'console;verbosity=minimal' -p:WarningLevel=0`：33件合格、0失敗、0スキップ。
- `git diff --check`：合格。

## 未実行・残る確認

- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py` 本実行：未実行。`--list` の終了コードは0だが、coop.json・coop_values.py・csproj変更により712クラスの全件実行を選択したため、今回の「全件・低速スイートを実行しない」という指示に従い停止した。上記Native・Startup全件と影響Coreテストを直接実行した。本コマンドの合格は未確認であり、パイプラインに委ねる。
- リポジトリ全件・低速テスト：未実行（今回の実行範囲外）。
- 上記Startupのスキップ1件：テスト側の既存スキップ指定により未実行。
- Unity実ゲームのホスト＋ゲストで、盾壁の発動→4秒維持→担当者への復帰：未実行。この作業環境で参照できるのは開発用DLLで、起動可能なUnityゲームと協力プレイ環境がない。実ソースの境界テスト合格を実ゲームの修正確認とは扱わない。
