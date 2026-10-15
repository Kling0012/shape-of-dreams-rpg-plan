# Issue #443：双方確認後に進まない交換の診断ログ

2026-10-15。**不具合の調査のための記録を追加。原因の確定・修正はログ受領後**。
[再現調査](issue-443-trade-reproduction.md) の8条件では報告症状を再現できていない。今回の診断追加を不具合の修正・解決とは扱わない。診断版のリリース後も Issue #443 は閉じず、再発時のログを待つ。この工程ではリリース・Issue投稿は行わない。

## ログの場所と送り方

Dreamforge は `DreamforgeMod : ModBehaviour` としてゲーム標準のMODローダーで起動し、既存の `Log.Info` → `UnityEngine.Debug.Log` へ出力する。`SodRpg.Mod.csproj` もゲーム標準の参照を使い、BepInEx の `BaseUnityPlugin`／`ManualLogSource` は使っていない。診断のためにBepInExや別のロガーを導入する必要はない。

- Windows の通常の保存先：`%USERPROFILE%\AppData\LocalLow\Lizard Smoothie\Shape of Dreams\Player.log`。エクスプローラーのアドレス欄へ `%USERPROFILE%\AppData\LocalLow\Lizard Smoothie\Shape of Dreams` を入力する。READMEの保存先にある `QuickSave` の1つ上のフォルダー。
- Linux のネイティブ Unity Player の一般形：`~/.config/unity3d/<CompanyName>/<ProductName>/Player.log`。Windows版をProtonで実行している場合は、Steamライブラリの `steamapps/compatdata/<AppID>/pfx/drive_c/users/steamuser/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/Player.log` を探す。Linux／Protonでの実ファイル位置はこの環境では未確認。
- 起動オプションで `-logFile` を指定している場合は、その出力先を使う。ゲームフォルダー内の `BepInEx/LogOutput.log` を標準の保存先とは案内しない。

1. 再発したら、ゲームを再起動する前に `Player.log` を別の場所へコピーする。再起動で前回ログへ切り替わる場合がある。既に再起動した場合は、残っていれば `Player-prev.log` も確保する。
2. **ホストと交換した双方のPC**から同じ時間帯のログを集める。参加者同士の取引ではホストを含む3台分になる。ファイル名に `host`／`first`（申込側）／`second`（承諾側）を付け、停止時刻とタイムゾーンを添える。
3. 添付用コピーを開き、既存のゲーム・他MODログにあるプレイヤー名、ユーザー名を含む絶対パス、Steam ID、接続先情報などの個人情報を除く。セーブデータや `coop-trades.json` はこの案内では要求しない。
4. 再発直後のログを [Issue #443](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/443) にファイル添付する。下記の条件も記載する。`[DreamforgeRPG] CoopTrade diagnostic` 行にあるランダムな取引ID・段階・状態は、各PCの経路を照合するため残す。

保存先の根拠：`src/SodRpg.Mod/Log.cs`、`DreamforgeMod.cs` の起動・保存ディレクトリ作成、ルートREADMEのWindows保存先、および [Unity公式のログファイル案内](https://docs.unity3d.com/Manual/log-files.html)。実ゲームを起動したファイル採取は未実行。

## 再発時に知りたいこと

- 全員のゲーム本体・Dreamforgeの版、通常版／特別版、他MODの有無。
- 報告者はホストか参加者か。取引はホストと参加者か、参加者同士か。人数と申込側／承諾側。
- ロビーか遠征中か。遠征なら部屋クリア直後・確保地点・移動／投票中など、停止した場所とタイミング。
- 双方が出した品目と個数、保管庫／鞄、欠片・調律石の数、受け取る側の空き。
- 申込→承諾→品目編集→双方確認の順番。確認直前の提示変更や、同時操作があったか。
- 停止時に双方に出た表示・警告、何秒待ったか、毎回か時々か、再試行で変わるか。
- 本体保存・Continue・再接続・ホスト交代・通信切断の直前／直後か。取引後に資産の増減を観測したか。

## 記録する境界と出力量

追加行の共通形式は `[DreamforgeRPG] CoopTrade diagnostic side=... stage=... trade=... revision=... mod=...`。ゲームが作成するランダムな取引ID以外の識別情報は追加ログに含めない。取引IDでない値は `-` に置き換え、入力文字列をそのまま出力しない。

- ホスト：申込、提示／確認の転送開始と結合完了、古いrevision／提示不一致／遺物変更、確認状態、prepare開始、双方の予約保存応答、台帳コミット直前／直後、取消理由と取消保存完了、ホスト障害。
- 各PC：提示／確認送信、view反映、prepare／executedの転送開始と結合完了、prepare拒否、予約保存開始／成功／保留／失敗／回復、結果反映、結果保存と再試行、精算保留、接続変更、送信失敗、取引機能の障害。
- 状態は確認・準備・予約の有無、安全状態、保存中か、保存エラーの有無、プロフィール／保存済みrevision、接続・元ホスト一致の真偽など。プロフィール本文、交換品詳細、残高、名前、プレイヤーGUID、ホストキー、通信本文、例外メッセージは追加ログに含めない。既存ログの内容は変更しない。
- 定期probe／rosterや分割データの各断片を毎回記録しない。既存の5秒ごとの照会時機に取引中だけ待機状態を採取し、同一内容は30秒間抑制する。抑制用辞書は最大64件で、新しい行の追加時に上限なら最終出力が最も古い1件だけを置き換える。既存行の再出力時はその時刻だけを更新し、他の行の履歴を消さない。状態変化は調査に必要なため別行として記録し、履歴から外れた行は30秒以内でも記録し直すことがある。
- 新しい毎フレーム処理・タイマー・通信・保存を作らない。診断状態は保存せず、`CoopTradeDiagnostics.Record` と両側の採取ヘルパーは例外を外へ出さない。ログ失敗で取消・機能停止・戻り値変更を起こさない。

## 元 PR #450 の検証記録

以下は元 PR に記録されていた修正ラウンドの内容であり、取り込み時に実行した検証とは区別する。

### 元 PR の範囲点検（2026-10-16）

PR #450 のコメントでは、通常のビルド・生成物確認・差分テストは成功し、全件の遅いテストで `PerformanceTests.A_kill_takes_well_under_a_tenth_of_a_millisecond` と `Build_and_encode_are_fast` が失敗していた。同じコメントの main 比較でも同じ2件が失敗している。今回の単独実行では性能テスト3件が成功したが、全件実行時の失敗原因を特定・修正したとは扱わない。性能のしきい値やテスト実行基盤は変更しない。

マージ時に Core の `Relic.cs` と `Rules.cs` へ混入した改行だけの差分を main と同じ内容へ戻した。併せて、マージで入った空ファイル `3` を作業領域外の一時保管先へ退避し、PR差分から除いた。製品コードの差分はホスト／クライアントの観測呼出しと診断ヘルパーだけで、既存の条件・戻り値・通信・保存呼出し・保存形式に変更はない。

前の再現工程で追加済みの8条件と抽出対象を維持する。診断工程で追加したのは本番診断ヘルパーのコンパイル・抽出対象への登録であり、この修正ラウンドではテストを新設・変更していない。

### 元 PR で実行した検証

以下をすべて前景で実行した（.NET SDK 10.0.111、runtime 10.0.11。net8.0の既存テストには `DOTNET_ROLL_FORWARD=LatestMajor` を指定）。

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue443TradeCompletionTests|FullyQualifiedName~HostCoopTradeTests|FullyQualifiedName~CoopTradeClientTests|FullyQualifiedName~Issue400HostTradeIdleAbortTests|FullyQualifiedName~Issue400ClientTradeTransientAbortTests|FullyQualifiedName~TradeSendExceptionTests|FullyQualifiedName~CoopTradeSafeTests' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=issue443-round-native.trx' --results-directory /tmp/issue443-round-results
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~SodRpg.Core.Tests.CoopTradeTests|FullyQualifiedName~SodRpg.Core.Tests.CoopTradeMigrationTests|FullyQualifiedName~SodRpg.Core.Tests.TradeStuckPending389Tests' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=issue443-round-core.trx' --results-directory /tmp/issue443-round-results
env -u SODRPG_SLOW DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~SodRpg.Core.Tests.PerformanceTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=issue443-round-performance.trx' --results-directory /tmp/issue443-round-results
dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release -p:GameDir="/home/wang/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/issue443-round-mod-deploy -v:minimal
python tools/balance/gen_cs.py --check
git -c core.whitespace=cr-at-eol diff --check
git -c core.whitespace=cr-at-eol diff --cached --check
git diff --check origin/main
git diff --exit-code origin/main -- src/SodRpg.Core
```

- 取引の本番抽出ハーネス：26件成功・失敗0・skip0・終了コード0。再現試行8条件、ホスト／クライアント、#400の待機、一時保存、送信例外、安全判定を含む。
- Coreの既存保存整合・復旧テスト：45件成功・失敗0・skip0・終了コード0。mainの融合対応を含む。一括保存失敗時の巻き戻し、ジャーナル中断、重複防止、遅延取消、旧形式復旧の確認は既存処理の回帰確認であり、報告事象の解決の証明ではない。
- 前回失敗したクラスの単独確認：3件成功・失敗0・skip0・終了コード0。全件実行時の性能を保証する結果ではない。
- TRX：`/tmp/issue443-round-results/issue443-round-native.trx`、`issue443-round-core.trx`、`issue443-round-performance.trx`。
- MOD Releaseビルド：終了コード0、エラー0・既存警告6（廃止API参照3、未代入の検証フィールド3）。ゲームへの配置はせず、出力先を一時領域に指定した。
- 生成物確認、未ステージ／ステージ済み差分の空白確認、Coreとmainの差分確認：終了コード0。Coreはmainと同一。main由来のCRLFを戻す差分は通常の `git diff --check` では末尾空白と判定されたため、`core.whitespace=cr-at-eol` を指定して確認した。mainとの差分には通常の空白確認も通る。診断の取り消し操作は未実行。

未実行：実ゲーム・実Mirror通信・描画・実ログファイル採取・ロガー失敗の注入（実機を使わず、既存テストはログファイルI/Oを代替するため）、全テスト・`--all`・`SODRPG_SLOW=1`・別editionのビルド・`dreamforge_simkill`（限定検証の範囲外）。push・リリース・Issue投稿・クローズは指示により未実施。元の不具合は未再現・未修正のまま、診断版の配布と再発ログの受領は後工程。

## PR #450 の取り込み修正版（2026-10-16）

元 PR の診断呼出し、ログ収集案内、再現調査記録、既存の8条件と抽出対象を取り込んだ。元 PR に含まれる空ファイル `3` は適用対象から外し、移動・削除していない。取引条件・通信・資産・保存形式は変えていない。

受け付け審査の品質指摘には以下で対応した。

- `WireCoopTrade` の接続変更と `TickCoopTrade` の待機で使う「取引中だけ」の条件を、診断ヘルパーの `try` 内へ移した。`Profile` の採取もnull許容にした。通常の初期化では `InitializeProfiles` → `ApplyProfileLoadResult` が読み込み済み／fallbackのプロフィールを設定してからセッションが公開されるが、追加診断の参照で接続処理を妨げない構造にした。
- 抑制履歴64件での全消去をやめ、最終出力が最も古い1件だけを取り除く。既存行の更新では他の履歴を取り除かない。走査は新しい行で上限へ達した時だけ、最大64件で、LINQや一時リストを作らない。
- 状態フラグが変わった場合の再出力は、停止箇所を調べる目的に沿って維持する。待機の採取は既存の5秒周期のまま。
- 元 PR の `Issue443TradeCompletionTests.Dispose` で保存をflush後、自分の一時ディレクトリを片付ける。テストケース・期待値は追加／変更していない。

### 実行した検証

全コマンドを前景で実行し、結果を確認した。.NET SDK 10.0.111／runtime 10.0.11、テストは `DOTNET_ROLL_FORWARD=LatestMajor` を使用した。

- 品質修正前に取り込んだ `Issue443TradeCompletionTests` を限定実行：8件成功、失敗0。TRXの出力先8ディレクトリと40ファイルが残ることを確認した。元の停止症状は再現しなかった。
- 修正後のnative限定テスト：26件成功、失敗0、skip0、終了コード0。フィルタは `Issue443TradeCompletionTests`、`HostCoopTradeTests`、`CoopTradeClientTests`、`Issue400HostTradeIdleAbortTests`、`Issue400ClientTradeTransientAbortTests`、`TradeSendExceptionTests`、`CoopTradeSafeTests`。実行前後の `/tmp/issue443-coop-*` を照合し、新たな残存ディレクトリ0件を確認した。
- Core限定テスト：`CoopTradeTests`、`CoopTradeMigrationTests`、`TradeStuckPending389Tests` の45件成功、失敗0、skip0、終了コード0。
- 以前の失敗報告に対応する `PerformanceTests` の単独実行（`SODRPG_SLOW` 無効）：2件成功、1件失敗、終了コード1。`A_kill_takes_well_under_a_tenth_of_a_millisecond` が `0.1215 ms per kill` で失敗した。`Build_and_encode_are_fast` と `Serializing_a_full_profile_takes_a_few_milliseconds` は成功。元 PR のコメントではmainでも性能失敗が記録されているが、今回mainそのものを再実行したわけではなく、原因は未確定。Coreと性能テストの内容が作業開始時から不変であることだけを差分で確認した。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release -p:GameDir='/home/wang/dev/sod-gamedata/Shape of Dreams' -p:ModDeployDir=/tmp/primpl450-mod-deploy -v:minimal`：成功、エラー0・既存警告6。ゲームへの配置はしていない。
- `python tools/balance/gen_cs.py --check`、`git diff --check`、`git diff --exit-code HEAD -- src/SodRpg.Core tests/SodRpg.Core.Tests/PerformanceTests.cs`：終了コード0。
- TRXのCountersで件数を照合：`/tmp/primpl450-results/primpl450-before.trx`、`primpl450-native.trx`、`primpl450-core.trx`、`primpl450-performance.trx`。修正後の限定検証は計73件成功・1件失敗、skip0。

### 未実行の検証（理由付き）

- 実ゲームの停止再現、実Mirror通信、2人／3人の協力プレイ、描画、Player.log採取、Linux／Protonの実保存先：実機を使えないため。停止不具合の解決とは扱わない。
- ロガー失敗・nullプロフィール・64件の履歴入れ替えの直接的な動的検証：該当する既存テストがなく、新規テストを作らない指示のため。コード点検と本番／抽出コードのコンパイルで確認した範囲に限る。
- 全テスト、`test_changed.py`、`--all`、`SODRPG_SLOW=1`：限定検証だけを行う指示のため。`test_changed.py` はcsproj差分で全体実行へ進むので、対象クラスを直接指定した。性能失敗の原因調査は未解決。
- 別editionのビルド、`dreamforge_simkill`、revert適用：取引診断を取り込む今回の範囲外。
