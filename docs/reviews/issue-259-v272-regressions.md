# #259: v2.7.2 退行調査

## 根拠と確定範囲

- 対象: `git diff v2.7.1 v2.7.2`、#246 の `ffacafa`（48ファイル）と同版内の native adapter 修正、#247 の `e000770`。現 main の #248・#252・#253・#258 も確認。#255（トレード）と #256（図鑑説明）は変更していない。
- 通常の確保 UI は `InGame && Profile.Run.AwaitingChoice && ActiveRunId != null` が必要。遷移中・メニュー表示中は描画しない。通常モードは次ゾーン到着を `RunChoiceProgress.Arrive → TryAdvance → Rules.ReachSecurePoint` で確定する。ローカルの未分類撃破はこの処理を保留するが、Hello の互換許可フラグは条件にない。
- Infinity はボス撃破だけでは選択を開かない。魂の出現・処理完了と Rift 解錠を確認した後、`TryOpenInfinityChoice` が撃破・配当・取引と `InfinityBoundarySettled` を確認する。
- **実行で再現した欠陥**: `RegisterKillPeer` がホスト自身にも遠隔再送カーソルを作る。ホストの保存済み撃破を既に確認しても、別の自己宛て ACK の義務が境界を止める。#246 以前から登録条件に含まれていたため、これだけを新規退行の原因とは断定しない。
- **#246 で露出した欠陥**: 不一致側の peer 撤去と参加区間延長の互換条件を撤去した一方、まだ一度も撃破 receipt を返していない参加者への仮義務を残した。Hello の30秒は警告のみで、この義務を解かない。最初の撃破から Infinity の確保 UI と区間更新を無期限に止め得る。
- **追加の退行経路**: 未応答者の切断後も `connection.<generation>.<netId>` の未受領区間を残し、保存・再開後にも義務が復活する。履歴が整理できず、2048件または古い graph の制限で後の進行を止める。
- **別の無期限待機**: `PartyAcknowledged` に期限がなく、選択確定後も全 human の ACK を待ち続ける。Hello を警告扱いにしただけでは解消しない。
- 通常モードの確保選択は Hello 到着・未到着・遅延の3経路で修正前から到達した。利用者の通常ダンジョン UI 消失の直接原因は未確定。issue 本文に v2.7.2 の Player.log はなく、ローカルには DLL と逆コンパイル資料のみで Unity ゲームを起動できない。通常 UI 消失まで直ったとは主張しない。

## 修正と保護する条件

- ホスト自身は遠隔再送 peer に含めない。直接受領とホスト保存済み frontier を使う。既存保存のホスト自身の二重 peer も owner GUID で除外し、ホストの未保存撃破そのものは保持する。
- client identity が一度も確立していない仮 peer は、登録から30秒だけ receipt を待つ。未応答なら仮参加区間への待機義務を解除して警告する。接続候補を永続的な receipt 所有者として保存せず、既存 v2.7.2 保存の仮 peer も再開時に除外する。切断した仮 peer も撤去する。
- 遅着した実 receipt は新しい現在区間で再送を再開する。Protocol・版・内容・Infinity capability の一致は条件にしない。通常の broadcast 配信と5秒の stream-control 再送は継続する。30秒を超えて receipt 所有者が未確立だった過去区間の再送は保証しない。
- **識別済み client の本物の未受領区間、未保存のホスト撃破、保存整合性、既存の容量制限は保持する**。受領済み／保存済み ACK を捏造せず、撃破台帳全体を消さない。識別済み client の実債務を、この仮 peer 向け timeout で免除しない。
- 選択 ACK は現在の選択ごとに30秒まで待つ。期限後は未応答の遠隔 ACK を待機条件から外すが、ホスト自身の保存・選択 receipt は必須。次の選択へ旧期限・旧 ACK を流用しない。
- #247 のパッチは維持。Actor `LogicUpdate/FrameUpdate/InvokeOnCreateIfDidnt` の Prefix/Finalizer は移動元参照の保存・復元のみ。毎フレームの本文走査、BossEnsure、確保選択確定は行わない。本文走査は起動時のみ。shipped Harmony と native Actor/scheduler の逆コンパイルでも、通常本文の置換・例外抑制・全体停止は確認されなかった。実機での非原因証明ではない。

## 検証

- 指定 Release build: 成功、警告5件・エラー0件。ゲームデータは参照のみ、配置先は `/tmp/x`。
- 指定 `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all`: Native 100、Core 2444、Startup 73 の計2617件成功、失敗0、skip4。
- 修正前に実 KillSync をリンクした4ケースで境界待機／切断後の仮台帳残留を再現。通常確保の到達条件は同じケースで修正前から通過した。
- 新規回帰9ケース: Hello 到着・未着・遅延と版／内容差、Infinity 確保選択の開放、ACK期限での実地図更新、次選択の期限分離、未応答者切断、旧仮peer／自己peerの再開、未保存ホスト撃破の保持。
- テスト runner を使わない独立スモークで、実 ClientSession/HostAuthority メソッドと ProfileStore/AsyncProfileWriter を実行。3経路すべて `kills=1, normalSecure=True, boundaryBefore=False, boundaryAfter=True, infinityChoice=True, saved=True`。ネイティブ境界は既存 double、ディスク保存は実ファイル。throwaway script と保存物は撤去。
- Unity画面・実ネットワークでのマルチは未検証。取得済みログも v2.7.1 であり、v2.7.2 の通常確保 UI 消失の直接原因には使えない。


## Continue 追加報告の追跡

- **今回再現した条件**：通常モードにも `InfinityNativeRestore` が `BeginRestore` を実行し、従来の `FinishRestore` は期限のない `ZoneManager.CallOnReadyAfterTransition` でしか `_restoring` を解除しなかった。チェックポイント復元が `ActiveRunId` を空にした後、`ClientSession.TrackRun` が `InfinityMode.Restoring` で戻り続け、`RunActive → TryFinishSecureArrival → AwaitingChoice` と UI に届かない。ホスト自身の Hello や参加者の受領 ACK は、この通常モード停止の必要条件ではない。
- **導入時期**：`7ed08b3`（2026-10-05）の lazy-ready 待ちと `bf26204` の通常モードにも掛かる `TrackRun` ガードの組合せ。v2.7.1・v2.7.2 の両タグに存在し、#246 で新たに導入された退行ではない。利用者の実機で通知が未着となった具体的な本体条件は未確認。
- **修正**：本体 `ApplyGameData` の完了は部屋復元後なので、そこから余分な準備完了待ちを挟まず共有フラグを解除する。完了通知自体も30秒で一度警告し、遷移終了済みの本体に対してチェックポイントを復元して進める。異なる本体runには復元せず候補を保持し、復元だけをスキップする。本体ロード未完了時だけはMOD報酬・Build反映を保留し、偽の完了・receiptは作らない。#248 の巻き戻し後にBuildの遮断を解除する順序、#259 の受領待ち期限、#255 の経済・予約・台帳は変更しない。
- **回帰**：実 `BeginRestore/FinishRestore`・`TrackRun`・`TickRunChoices`・確保パネル状態を同時に実行。ホスト単独／参加者あり × 本体完了通知あり／未着の4ケースで、Continue→ボス報酬→ゾーン終了→UI開放を確認。別runへの誤復元とロード中のBuild早期解放を防ぐ2ケースを追加。修正前は完了後でも未着のlazy-ready待ちにより `ActiveRunId` が空のままになるケースが失敗した。
- **独立スモーク**：テストrunnerを使わずリンク済み本番メソッドを呼ぶ実行プログラムでも同じ4経路を確認。全経路 `resumed=True bossKills=1 secureChoice=True panelOpen=True checkpointRetained=True lateCallbackSafe=True`。逆順の完了呼出し・遅着通知でも新しいボス報酬を巻き戻さない。一時プログラムは撤去した。
- **今回の指定検証**：Release build は警告5件・エラー0件。`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all` は Native 106・Core 2471・Startup 73、計2650件成功・失敗0件・既存skip4件。
- **限界**：本体境界は既存double。Unity画面、実ソケット通信、利用者の実際の通知未着条件は未確認。通常モードの当該停止経路を修正した証拠であり、別のInfinityボス／魂／保存待ちの全経路保証ではない。

## #246 全48ファイルの見直し一覧

「追加なし」は、今回の静的差分・成功時経路の追跡で追加修正を裏付ける退行を確認しなかった意味。実機の全経路保証ではない。

| ファイル | 変更と今回の対応 |
|---|---|
| `CHANGELOG.md` | 警告のみ方針の記録。今回の待機修正を追記。 |
| `README.md` | 同期方針を警告のみへ。追加なし。 |
| `docs/specs/issue-95-infinity-mode.md` | 入場・Hello・保存方針。今回の有界待機を補足。 |
| `src/SodRpg.Core/Game/BossProfiles.Demon.cs` | signed fingerprint の invariant 表記。ゲーム値の変更なし。 |
| `src/SodRpg.Core/Game/BossProfiles.cs` | 同上。追加なし。 |
| `src/SodRpg.Mod/ClientSession.BossVisuals.cs` | 互換拒否撤去。所有者・run/room・epoch・finite/件数検証は維持。追加なし。 |
| `src/SodRpg.Mod/ClientSession.Continue.cs` | Hello-ready を checkpoint-blocked に置換。成功時の解除・復元は残る。追加なし。 |
| `src/SodRpg.Mod/ClientSession.GemSlotConflict.cs` | readable packet の警告化。枠の無効化は後続 #248 で除去済み。変更しない。 |
| `src/SodRpg.Mod/ClientSession.Hello.cs` | 互換許可を削除。continue・authority 受領処理は残る。追加なし。 |
| `src/SodRpg.Mod/ClientSession.Infinity.cs` | roster halt の downgrade 撤去。選択開放は今回修正した実台帳で検証。 |
| `src/SodRpg.Mod/ClientSession.InfinitySettings.cs` | mismatch によるモード停止・開始拒否を撤去。追加なし。 |
| `src/SodRpg.Mod/ClientSession.KillSync.cs` | mismatch 拒否撤去。run/stream/authority・耐久 frontier は維持。追加なし。 |
| `src/SodRpg.Mod/ClientSession.Monsters.cs` | cue の警告化。範囲・authority は維持。追加なし。 |
| `src/SodRpg.Mod/ClientSession.PressureDividend.cs` | 警告化。owner/run・1欠片 receipt は維持。追加なし。 |
| `src/SodRpg.Mod/ClientSession.RunChoices.cs` | readable mismatch を許可。decode・run/authority 保護と到着確定は残る。追加なし。 |
| `src/SodRpg.Mod/ClientSession.RunGrowth.cs` | 警告化。全行検証後に stack を置換する処理は残る。追加なし。 |
| `src/SodRpg.Mod/ClientSession.cs` | Build送信・Appliedの許可ゲート撤去。成功時更新は残る。通常確保消失との因果は未確定。 |
| `src/SodRpg.Mod/DreamforgeMod.cs` | preflight は助言、実パッチは class ごとに隔離。rollback 例外の隔離も v2.7.2 内で修正済み。 |
| `src/SodRpg.Mod/HostAuthority.BossVisuals.cs` | 全 human へ送信。単回 serialize と有界件数は維持。追加なし。 |
| `src/SodRpg.Mod/HostAuthority.BuildValidation.cs` | readable Build を許可。transfer/source/native 検証は残る。#248 の再開保護を維持。 |
| `src/SodRpg.Mod/HostAuthority.Hello.cs` | rejection/acceptance 撤去、診断と観測 session を維持。失われた準備完了フラグは確認できず。 |
| `src/SodRpg.Mod/HostAuthority.Infinity.cs` | roster 拒否撤去、Hello30秒を警告化。仮 receipt 義務の timeout がなかった。今回 KillSync 側で修正。 |
| `src/SodRpg.Mod/HostAuthority.KillSync.cs` | 全 cursor の参加区間延長。自己宛て・未応答・切断／再開の仮義務を修正。 |
| `src/SodRpg.Mod/HostAuthority.NewPowers.cs` | 個人 event の警告化。owner/run/generation/event gate と一回確定を維持。追加なし。 |
| `src/SodRpg.Mod/HostAuthority.Trades.cs` | 読み取りのみ。readable通信許可・malformed wire拒否。#255 は変更しない。 |
| `src/SodRpg.Mod/HostAuthority.cs` | curse/clear 警告化。ordinal冪等・hero寿命・実処理は維持。追加なし。 |
| `src/SodRpg.Mod/InfinityMode.cs` | roster halt/capability gate 撤去。独立した選択 ACK の無期限待機を今回修正。 |
| `src/SodRpg.Mod/NativePatchPreflight.cs` | FeatureAvailable/feature-wide gate 撤去。起動時助言のみ。追加なし。 |
| `src/SodRpg.Mod/NetMessageDecodeWarning.cs` | MOD の decode 例外の narrow finalizer。例外を握り潰さず、再送・全体停止なし。追加なし。 |
| `src/SodRpg.Mod/NetMessages.cs` | ローカル message 名ごとの bounded warn-once。wire変更なし。追加なし。 |
| `src/SodRpg.Mod/PressureDividendMessages.cs` | Protocol例外撤去。正規の報酬構築・個数検証は維持。追加なし。 |
| `src/SodRpg.Mod/README.md` | fail-soft／checkpoint 方針。adapter-local safeguards は後続修正に記載済み。 |
| `tests/Issue73.Native.Tests/ContinueSaveTests.cs` | 不一致・Hello未着の再開 coverage。実 checkpoint 不足は保持。 |
| `tests/Issue73.Native.Tests/InfinityClearSaveTests.cs` | obsolete Hello-ready fixture の除去。 |
| `tests/Issue73.Native.Tests/ManualTradeCurrencyFailureTests.cs` | 読み取りのみ。#255関連の取引テストは変更しない。 |
| `tests/Issue73.Native.Tests/NativeAcceptanceTests.cs` | 実 receipt ありの replay は検証していたが、never-receipt は未検証。今回の partial regression scenarios へ接続。 |
| `tests/Issue73.Native.Tests/NativeBoundary.cs` | obsolete gate double 除去。今回は実 Hello/Infinity/KillSync を同時にリンクできるよう更新。 |
| `tests/Issue73.Native.Tests/SatchelOverflowDustTests.cs` | obsolete Hello-ready fixture 除去。追加なし。 |
| `tests/SodRpg.Core.Tests/ContentFingerprintTests.cs` | culture表記の検証。追加なし。 |
| `tests/SodRpg.Core.Tests/HandshakeNativeApi.cs` | 互換許可doubleを撤去。追加なし。 |
| `tests/SodRpg.Core.Tests/InfinityModeTests.cs` | fingerprint一致assert撤去。mode/epoch検証は維持。 |
| `tests/SodRpg.Core.Tests/NegotiationNativeTests.cs` | warning lifecycle へ変更。追加なし。 |
| `tests/SodRpg.Mod.Startup.Tests/HostAuthorityLobbyHarness.cs` | `PeerNeedsFact=false` と空の binding が実台帳の停止を隠していた。今回は native suite で実 KillSync を併用。 |
| `tests/SodRpg.Mod.Startup.Tests/InfinityLobbyStartTests.cs` | warning-only開始を検証。実撃破の仮義務と選択後ACK不足は未検証だった。 |
| `tests/SodRpg.Mod.Startup.Tests/InfinityRevealTests.cs` | vote/travel の実精算を検証。今回は参加者ありのACK期限と地図更新を追加。 |
| `tests/SodRpg.Mod.Startup.Tests/NativeStartupTests.cs` | feature-wide gate assert を撤去。実起動／class隔離の検証は維持。 |
| `tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj` | 実 message/decode patch をリンク。追加なし。 |
| `tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs` | Protocolコピー撤去、FromJson double追加。追加なし。 |

同版内の native adapter follow-up は Feather・Baptism・DoubleTap の captured field/type、NyxWorld の局所 IL 契約、HP damage delegate の失敗を機能単位で隔離済み。新たな全体停止／私有APIを起動必須条件にする変更は加えていない。#247 は `HostAuthority.BossNativeAdapters.cs` と既存 native runtime tests を確認し、今回変更なし。
