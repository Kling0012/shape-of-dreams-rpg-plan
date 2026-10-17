# v3.7.0 協力通信の送信量と送信周期

対象ブランチは `perf/v370-net`。調査Bの N5、N7、N1、N2 をこの順で変更した。遊び方、戦闘の強さ、報酬の判定と数値は変更していない。通信量と待ち時間の実測は行っていない。

## N5：Pressure・RunChoices・Links

Pressure と現在の RunChoices に相手別の送信キャッシュを設けた。値の変更は次の検査時に送り、同値の回復通知は `stateSnapshots=1` が成立した相手で30秒、旧相手で従来の5秒とした。

キャッシュでは送信元 actor、相手の hero と接続、選択内容などを比較する。Pressure は authority と run も比較し、RunChoices はこれらを含む選択の符号化結果と capability の変更を確認する。初回、接続や hero の変更、authority や run の更新では期限を待たずに送る。ホストが送信元から外れると Pressure のキャッシュを消す。

Links の最高値が増えたときの即時通知は残した。同値の回復通知は対象者への Pressure と同じ周期とし、他の参加者への Pressure 送信だけでは再送しない。RunChoices の履歴回復、terminal、victory、lobbyReturn の通知経路は維持した。プレイヤー名簿が空の初期段階では、現在の RunChoices を従来どおり一括通知する。

主な変更ファイルは `HostAuthority.cs` と `ClientSession.RunChoices.cs`。capability の送受信は `BuildInputCapability.cs`、`ClientSession.Hello.cs`、`HostAuthority.Hello.cs`、`NetMessages.cs` を変更した。

## N7：世界のダスト上限後の要求

OverflowBonus の要求に optional な `blockedWorldAck=1` を加えた。対応ホストは実際の支払累計に加え、現在の世界で残枠がなく未払いが残る場合に `worldBlocked` と `blockedWorld` を返す。

参加者は world、run、ledger が一致する間、同値の要求を30秒周期にする。total、設定、world、run、ledger の変更時には期限を待たずに送り、接続を更新したときは抑制状態を消す。旧ホストはこの応答を返さないため、未払いの要求は従来の2秒周期となる。

累計 total と未払いは保持し、`paid=total` で支払を偽装しない。次の世界でも残りを要求できる。owner と ledger の検証、重複支払の防止、native 残高の変更が曖昧な場合に追加ダストだけを止める処理は維持した。

変更ファイルは `ClientSession.OverflowBonus.cs`、`HostAuthority.OverflowBonus.cs`、`NetMessages.cs`。

## N1：動的難易度の名簿と敵分類

`dynamicRoster=1` が成立した相手へは、既存の `DreamforgeNightmareMsg` の `netId=0` を使い、共通名簿と F6 用 status を敵分類より先に送る。敵分類には名簿配列を同梱せず、`dynamicRevision` を参照させる。名簿の更新だけでは全敵の ownership revision を変更しない。

共通通知のキャッシュは相手別に持ち、actor、接続、authority、run、room、名簿 revision、status revision を比較する。初回と変更時に送信し、同値でも5秒で回復通知を送る。分類、cue、撃破履歴と共通の32 RPC/frame 予算を使う。途中参加時に共通通知を送ったあとで分類と cue の予算が足りなければ、敵の再送位置を進めず次のフレームへ残す。

旧相手には分類への名簿同梱を残した。全員が旧相手なら分類は従来の broadcast を使い、対応相手がいる場合は相手別に送る。分類の宛先は `allHumanPlayers` とし、従来の通知対象を保つ。

参加者が未取得の名簿 revision を参照する分類を受けた場合は、古い名簿と status の表示キャッシュを消す。F6 は既存の未確定表示を使い、後続の共通通知で回復する。名簿の到着待ちでホストの戦闘処理を止めず、F6 を開く操作にも新たな要求往復を加えない。

主な変更ファイルは `HostAuthority.DynamicDifficulty.cs`、`HostAuthority.EnemyOwnershipSync.cs`、`HostAuthority.KillSync.cs`、`ClientSession.KillSync.cs`。能力と接続の状態は `HostAuthority.Hello.cs` と `HostAuthority.cs` で管理する。

## N2：協力取引の転送予算

接続先ごとに最大4件の FIFO を設けた。remote への大きな profile、prepare、executed と、参加者からの offer、confirm は先頭の転送から順に送る。同じ相手への別の転送を断片単位で交互に送らない。開始済みの転送は置き換えず、未送信の同じ action/kind・取引ID の snapshot だけを置き換える。同じ revision と内容が送信待ちにあれば重複投入しない。

予算は JSON の UTF-8 バイト数に4バイトを加えて計上する。

| 項目 | 値 |
|---|---|
| 1フレームの上限 | 64 KiB |
| 時間による token 補充 | 128 KiB/s |
| token の初期量・保持上限 | 64 KiB |
| 接続先ごとの待機上限 | 4転送 |

時間予算は token bucket とした。初期の64 KiB と時間による補充を許すため、任意の1秒窓の送信量を128 KiB以下にするという意味ではない。低FPSで時間が空いても token は64 KiBまでしか保持せず、フレーム予算も併用して一度に大量の断片を放出しない。取消や失敗で queue を消しても予算は再充填しない。Mirror の batching、transport ヘッダー、再送量は計上対象外である。

投入後は残り予算でその場で送るため、小さい payload は送信可能な範囲で従来の即時性を保つ。probe、view、prepared、query、cancel などの小さな制御通知は大容量 queue を通さない。ホスト自身へのローカル配送も回線予算の対象外とした。

offer の5秒再送は全断片の送信が完了してから計時し、転送途中には再投入しない。参加者側の送信例外では送信待ちを消し、pending offer を保持して5秒後に新しい転送IDで再送する。ホスト側の送信例外では内容を保持し、先頭を新しい転送ID・先頭断片に戻して5秒後に再試行する。予約品、journal の commit、保存確認、結果照会による回復は維持した。満杯の場合も経済状態を破棄しない。

既存の `DreamforgeCoopTradeUp/Down`、UTF-8 JSON → Base64 → BuildTransfer の形式を使う。断片の大きさは既存の10,893文字、転送上限は Base64 後の4 MiBである。進捗ACKや新しい wire 形式は追加していないため、この予算処理に別の capability は必要ない。

変更ファイルは `CoopTradeTransport.cs`、`ClientSession.CoopTrade.cs`、`HostAuthority.CoopTrade.cs`。既存の native テストが production のメソッドを抽出する構成に合わせ、`tests/Issue73.Native.Tests/NativeBoundary.cs` と `NativePersistence.targets` に queue のフィールドと新しい送信 helper を追加した。テストメソッドや期待値は変更していない。

## 互換性と変更範囲

固定 Hello、`Protocol.Version`、既存メッセージ型の名前は変更していない。BuildInputCapability の version=1 に optional な `stateSnapshots` と `dynamicRoster` を追加し、Hello 登録済みの相手が値1を送った場合だけ対応として扱う。旧版や MOD なしの相手へ新形式を一律に要求しない。

セーブ形式、所持品の検証、報酬の数値、戦闘判定は変更していない。担当外の `Patches.cs`、ダメージ経路、エッセンス、追加効果 queue に変更はない。今回は性能改善であり、`CHANGELOG.md` と `changelog.d/` は変更していない。

## 検証結果

2026-10-18 に次の検証を実行した。

| 検証 | 結果 |
|---|---|
| 指定の Mod Release ビルド | 成功、警告6件、エラー0件 |
| `python3 tools/balance/gen_cs.py --check` | 成功 |
| Native の関連クラスを指定した既存テスト | 137件成功、失敗0件、skip 0件 |
| Core の関連クラスを指定した既存テスト | 164件成功、失敗0件、skip 0件 |
| `git diff --check` | 成功 |

ビルドには次のコマンドを使った。一時ファイルの容量制限を避けるため、`TMPDIR` を作業ツリー内に指定した。

```bash
TMPDIR="$PWD/.perf-net-tmp" dotnet build src/SodRpg.Mod -c Release \
  -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" \
  -p:ModDeployDir=/tmp/x
python3 tools/balance/gen_cs.py --check
```

既存テストは次のクラスフィルターで直接実行した。インストール済み runtime が .NET 10 のため、net8 testhost に `DOTNET_ROLL_FORWARD=Major` を指定した。

```bash
TMPDIR="$PWD/.perf-net-tmp" DOTNET_ROLL_FORWARD=Major \
  dotnet test tests/Issue73.Native.Tests -c Release --no-restore \
  --filter 'FullyQualifiedName~OverflowBonus|FullyQualifiedName~DynamicDifficulty|FullyQualifiedName~EnemyOwnership|FullyQualifiedName~BossShares|FullyQualifiedName~LobbyReturn|FullyQualifiedName~CoopTrade|FullyQualifiedName~NativeAcceptanceTests|FullyQualifiedName~PureWhiteRewardChoice|FullyQualifiedName~InfinityPersonalChoice|FullyQualifiedName~Issue588PersonalWaypointPressure'

TMPDIR="$PWD/.perf-net-tmp" DOTNET_ROLL_FORWARD=Major \
  dotnet test tests/SodRpg.Core.Tests -c Release --no-restore \
  --filter 'FullyQualifiedName~BuildTransferV131|FullyQualifiedName~OverflowBonusLedger|FullyQualifiedName~SalvageDustCap|FullyQualifiedName~RunChoice|FullyQualifiedName~CoopTrade|FullyQualifiedName~Handshake'
```

## 未実行・未確認

- ゲームを起動できない環境のため、実機での協力接続、途中参加、再接続、F6 の画面は未確認。
- 弱回線と低FPSでの待ち時間、実際の通信量、GC の計測は未実行。削減量や体感の改善は実測値として報告していない。
- 新しい queue 予算と blocked-world の専用テストは追加していない。該当箇所はコード点検と関連する既存テストで確認した。
- full/slow suite は指示に従い未実行。
- commit と push は未実行。
