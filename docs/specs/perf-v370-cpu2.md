# v3.70 CPU・GC負荷の軽減（第2段）

対象は `report-a-cpu.md` のA02・A04・A10と、`report-c-native.md` のC3。戦闘の数値、判定、通信・保存形式は変更していない。性能の改善幅は未測定。

## A02 遠征の鍛錬

直した。`RunGrowthLedger` に持ち主別のVersionと、ラン切替・リセット・復元で増えるGenerationを追加した。能力の再集計は、本人のスタック、Generation、持ち主の鍵、Build参照が変わった場合に行う。他人のスタック増加だけでは再集計しない。

スタックの増加条件、上限、進捗、保存値と出力順は従来どおり。全体のVersionも残し、送信側は引き続きその値を使う。毎フレームの能力反映、0.5秒の通信チェック、変更がない場合の送信抑制とheartbeatは変更していない。テスト側のHeroRuntimeにもGenerationを追加した。

## A04 群れの生成中の担当数

直した。同じ `SpawnMonsterSettings` の生成中は、一度集計した担当数を使う。担当の割当・変更、共有目標や除外対象への変更、登録解除で差分を反映する。生存参加者の内容や順序が変われば再集計する。既存の0.25秒の担当制走査では再利用状態を無効にし、次の選択で数え直す。

本体の `Actor.Network_isActive` と `EntityStatus.Network_currentHealth` にPrefix/Postfixを入れ、有効状態の変化とHPが0をまたぐ変化を反映する。両方のフック導入を確認できた場合だけ再利用する。setterの形が違う、フックがない、通知処理が失敗した場合は警告を出し、従来の全敵集計へ戻す。通知処理の失敗は一度だけ警告し、そのホストでの再利用を止める。

同数時の `_ownershipLastPlayer` による順番、ボスの除外、親の担当の継承、共有目標、初期HPと担当の確定時期は従来どおり。スポーンを選んだ時点では数を予約加算せず、実際の登録と生存状態を反映する。スポーン失敗に対する新しい予約数は残さない。ネストした生成でsettingsが変われば再集計する。

設定 `nativeOwnershipSpawnCounts` は既定でtrue。falseなら従来の全敵集計を使う。定期走査と本体の生成間隔が重なる場合や、群れが切り替わる場合は再利用できる範囲が限られる。全スポーンで集計を省ける変更ではない。

## A10 撃破時の親Actor探索

直した。通常撃破の `OnMemoryKill` でmemoryを一度求め、`OnNewPowerKill` とギミック予約へ渡す。Actor単位の永続キャッシュは追加していない。

生成ダメージの除外、召喚の判定、アシストが保存している当時のmemoryとsummon情報、鍛錬のkiller-only規則は従来どおり。呼出しごとのローカル値なので、ネストした撃破や次のプール利用へ結果を持ち越さない。

## C3 敵AIの標的の伝播

確保の削減を実装した。静的なiteratorメソッドにAIとtargetを引数で渡し、本体が通知ごとに作るクロージャを省いた。本体ILのクロージャ・列挙子・WaitForSecondsという3つの確保のうち、クロージャ1個を省く。Unity内部の確保は削減対象に含めない。

各通知のコルーチン、`Random.Range(0.1f, 0.7f)`、WaitForSeconds、再開時の標的とisActiveの確認、Aggro呼出しは残した。MOD独自のtickによる時刻の丸めは追加していない。コルーチンの停止と寿命管理もUnityに任せる。設定 `nativeAggroPropagation` は既定でtrue。falseなら新しい通知は本体の実装を通る。既に始まった待機は完了まで続く。

1体1件の辞書への集約は見送った。本体は既に標的がある受信側でもコルーチンを始め、複数候補の待機が競合する。集約すると、後から来た短い遅延の候補を落とすなど、誰を狙うかや乱数の消費を変え得る。列挙子のプール化も、停止したコルーチンの回収を保証できないため実施していない。

本体の受信処理とiteratorのIL命令列、参照フィールド、呼出し先、遅延値が合う場合だけPrefixを導入する。不一致なら警告を一度出し、本体を使う。二重読込みでは `__runOriginal` を確認し、先のPrefixが処理した通知を二重に予約しない。実行中の例外で処理が途中まで進んだ可能性がある場合は、同じ通知を再実行せず、この最適化を止める。

## 検証

- 指定のMOD Releaseビルドは成功。GameDirに `~/dev/sod-gamedata/Shape of Dreams`、ModDeployDirに `/tmp/x` を指定した。警告6件、エラー0件。
- `python3 tools/balance/gen_cs.py --check` は成功。
- `DOTNET_ROLL_FORWARD=LatestMajor` でtests以下の全13個のcsprojをReleaseビルドした。最初はissue573だけ失敗し、抽出器の修正後に同プロジェクトのビルドも成功した。
- Nativeの既存テスト全件は278件成功、失敗・スキップなし。
- Startupの既存テスト全件は149件成功、失敗なし、1件スキップ。スキップは既存の `ModReloadTests.SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch` で、Releaseでのインライン化によりHarmonyパッチを観測できないという指定がある。
- Coreはクラス指定で69件成功。対象は `StarAdditionsV132Tests`、`RunGrowthBalanceTests`、`KillAssistSupportNativeTests`、`NativeEndingAttributionTests`、`NativeProjectileAttributionTests`、`MemoryActivationAttributionTests`、`KillClassificationTests`、`HeroGrowthKeystoneAdditionsTests`。
- issue573の既存再現プログラムは13項目すべて成功。式形式のメソッドを抽出する際に次のメソッドまで取り込んでいたため、抽出器を修正した。製品の遺物表示や既存の期待値は変更していない。
- 本体EntityAIのILとコンパイル済みの伝播処理を照合した。`git diff --check` は成功。

新規の `EnemyOwnershipSpawnCounts.cs` と `NativeAggroPropagationPerf.cs` はMODプロジェクトの自動収集でコンパイルされる。既存テストプロジェクトにこれらのリンクはない。Nativeの担当制テストはpartial実装がなく、従来の全敵集計を通る。新規フックによる再利用経路やC3の動作を、このテストの成功で確認済みとは扱わない。

未実行：Unity上での生成・死亡・切断・蘇生・共有目標切替・ボス・親継承・生成失敗・ネスト・プール再利用、フック導入と失敗時の復帰、設定切替、二重読込み、伝播候補の競合と実時刻、CPU・GC・FPSの測定。この環境ではゲームを動かせない。Core全件と、指定したもの以外のテスト実行は、全体・低速テストを避ける依頼に従い省略した。Startupのスキップ1件は未実行。

新しいテストファイル、commit、push、agent-memoryへの保存、ファイルの削除は行っていない。性能改善なのでCHANGELOGとchangelog fragmentは変更していない。
