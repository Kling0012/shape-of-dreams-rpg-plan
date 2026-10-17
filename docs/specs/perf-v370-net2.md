# v3.7.0 通信改善 第2段

対象は `perf/v370-net2`。第1段を統合した `2d1bf9ea` を起点に、調査Bの N6、N8、N9、N11 を順に実装した。4項目とも修正し、見送った項目はない。

## N6：Hello と能力問い合わせ

`BuildInputCapability.cs` に送信予定をまとめ、Hello と Build 前の問い合わせが同じ期限を参照するようにした。未回答、回答済みの対応、回答済みの非対応を区別する。optional な `helloSchedule` を返すホストでは安定後の周期を30秒にし、設定や能力が変われば期限前でも送る。旧ホストには5秒の経路と独立した自動詠唱負荷の問い合わせを残した。

固定の `DreamforgeHelloMsg` の型と欄は変えていない。Continue の受領通知は引き続き Build より先に送り、送信に失敗した受領通知は再試行する。この経路から Hello を送った場合も共通の送信予定を更新し、直後の二重送信を防ぐ。接続と authority の変更では交渉状態を失効させる。Hello の authority、Continue、観測セッションの識別は維持した。

新しい capability の応答には自動詠唱負荷への対応も含めた。対応済みの相手へは、同値の capability を受け取るたびに RoomOmen を返さない。RoomOmen の active 中の独立した heartbeat は変更していない。

変更：`BuildInputCapability.cs`、`ClientSession.Hello.cs`、`HostAuthority.Hello.cs`、`NetMessages.cs`。authority の変更に伴う失効は `ClientSession.KillSync.cs`。

## N8：撃破の即時通知と履歴の重複

ホストの peer ごとに128スロットの固定配列を持ち、即時 broadcast した sequence と送信時刻を記録する。同じ actor、接続、現在の stream に対する通常の履歴再送だけを最大8秒待たせる。配列の上書きで失うのは重複抑制の情報だけで、履歴は失わない。

明示的な不足要求はこの待ち時間を迂回する。保存済み ACK が届けば従来の判定で不要になり、届かなければ期限後に再送する。再接続と観測セッションの変更では送信記録を失効させる。

死亡時の即時 broadcast は元の位置に残した。送信済みを保存済み ACK として扱わず、未 ACK の履歴、参加範囲、既存の回復予算を維持した。wire の変更はない。

変更：`HostAuthority.KillSync.cs`。

## N9：保存 ACK と不足要求の周期

`killReceiptDelta=1` を返したホストだけに、既存の `DreamforgeKillReceiptMsg` で変更した stream の frontier を送る。ホストは既存の累積マージ処理を使う。非対応ホストには従来の5秒周期の全 receipt と不足要求を送る。

- 保存済み frontier の変更は、最短0.25秒間隔で通知する。
- 不足の走査と要求は最短0.5秒間隔、1要求は最大32件とする。
- 同じ不足キーの再試行間隔は0.5、1、2、4、5秒と延ばし、以後は最大5秒とする。
- 同値の全 receipt は30秒の heartbeat で送る。
- ラン、actor、観測セッション、authority の変更では送信済み frontier と再試行状態を失効させる。

ACK の元になるのは従来と同じ保存完了分だけで、受信しただけの fact は ACK しない。保存 writer の完了判定と、既存の保存済み Infinity 失敗ランの扱いも変更していない。分類、cue、回復 replay が共有する32 RPC/frame の上限は維持した。

変更：`ClientSession.KillSync.cs`。能力の交渉は N6 の変更に含む。

## N11：選択履歴の分散送信

peer ごとのカーソルと接続情報を持ち、通常の履歴を共有予算で送る。予算は0.1秒ごとに最大4件、UTF-8 の choices と固定128バイトを足した見積もりで8KiBとした。既存の1件だけで8KiBを超える場合は、送信を止めないためその1件を許す。これは transport の厳密なバイト上限ではない。

履歴の変更時だけ送信用の一覧を更新し、変更した位置まで必要なカーソルを戻す。peer を順番に処理し、完走後の30秒周期には登録順で0、0.5、1、1.5秒のずれを付ける。送信待ちを進めるため、履歴の処理は毎 tick 呼び、内部の0.1秒制限を使う。

参加と再接続では最新履歴を先に送り、終了、勝利、ロビー返却の変更も通常の履歴予算より先に送る。これらの優先通知は通常予算の対象外とした。履歴を捨てず、既存の Infinity の履歴整理規則も変えていない。メッセージの型と欄は従来どおり。

変更：`RunChoicePublisher.History.cs`、`ClientSession.RunReplay.cs`、`ClientSession.RunChoices.cs`。

## 互換性と遊び方

戦闘の強さ、数値、報酬、とどめの判定、選択の内容、見た目は変更していない。変更したのは送信予定と再送の配分である。第1段の改善と担当外の native 処理には手を加えていない。毎 tick の履歴処理では一覧を再利用し、履歴の再構築は revision の変更時だけ行う。実際の GC と通信量は未計測。

## 検証

2026-10-18 に最終ソースで実行した。

| 検証 | 結果 |
|---|---|
| 指定 MOD Release build | 成功、0 errors・6 warnings |
| `python3 tools/balance/gen_cs.py --check` | 成功。実行後に balance 関係の変更なし |
| `tests/` 以下の全13プロジェクトの Release build | `DOTNET_ROLL_FORWARD=LatestMajor` で全件成功 |
| Core の撃破分類、保存、履歴、能力交渉、自動詠唱負荷などの関連テスト | 38件成功、失敗・スキップなし |
| Native の Continue、履歴、終了保存、tick 分離、取引、ロビー返却などの関連テスト | 79件成功、失敗・スキップなし |
| Startup テスト | 149件成功、1件スキップ、失敗なし |
| `git diff --check` | 成功 |
| 仕様書の yomiyasu lint | 見直し候補0件 |

MOD build の条件は次のとおり。出力を絞るオプションも付けて実行した。

```sh
dotnet build src/SodRpg.Mod -c Release -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/x
```

Native の既存プロジェクトに本物の `ClientSession.RunReplay.cs` をリンクし、空の代替メソッドを外した。Core 側では抽出する製品メソッドに合わせ、既存の代替型と reset メソッドを補った。新しいテストファイルやテストケースは追加していない。

途中の Native テストで、Continue 受領通知の直後に Hello が二重送信される失敗を確認した。共通の送信予定を更新して修正し、上記の79件を再実行して成功を確認した。

全プロジェクトのビルドでは、issue573 の既存抽出スクリプトが式形式のメソッドに続く別メソッドも取り出し、重複宣言になることを確認した。`tests/repro/issue573/extract.py` に式形式の抽出を補い、単独および全13件のビルドで成功を確認した。製品の UI は変更していない。

## 未実行

- `Issue73.Native.Tests` のフィルタなし全件実行と、全テストプロジェクトの全件実行。今回の「全体・低速テストを実行しない」という指定に従い、変更に関係する既存テストだけを実行した。Startup のフィルタは namespace 全体に一致し、150件が対象になった。
- Startup の `ModReloadTests.SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch`。既存のスキップ指定により実行されなかった。
- 実ゲームでの再接続、ホスト再読込み、handler 未登録からの回復、Continue、native 死亡との前後順序。この環境ではゲームを起動できない。
- 通信量、送信待ち、GC、低性能 PC での実測。
- 新 scheduler 専用のテスト作成。テスト作成は禁止されているため行っていない。

コミット、push、共有記憶への保存は行っていない。
