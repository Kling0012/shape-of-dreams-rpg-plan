# テストガイド（tools/test_changed.py）

## 目的

開発中やエージェントの作業中に「変更した部分に関係するテストだけ」を素早く回すための
ツールとして `tools/test_changed.py`（Python 3.12・標準ライブラリのみ）を用意している。
テストプロジェクトごとのテストは直列（共有状態のため）なので、3 プロジェクトを
ビルド後に並列実行して実時間を縮める。

## 使い方

リポジトリルートから実行する。

```
python tools/test_changed.py [--base <git ref>] [--list] [--all] [--slow]
```

| オプション | 動作 |
|---|---|
| なし | 変更に対応するテストクラスだけを `dotnet test` で実行する |
| `--base <git ref>` | 比較元を指定する（既定: `origin/main` との merge-base） |
| `--list` | 選択されたテストクラス（完全修飾名）だけを表示して終了する（テストは実行しない） |
| `--all` | フィルタなしでスイート全体を実行する |
| `--slow` | `Speed=Slow` の網羅テスト（`SODRPG_SLOW=1`）も実行する（既定では除外） |

終了コードは最初の非ゼロの `dotnet test` 終了コード。テストに関係する変更が
一件もない場合は「変更なし」と表示して 0 を返す。

## 対象の収集

既定の比較元は `git merge-base HEAD origin/main`。`origin/main` が取得できない
場合は `HEAD` にフォールバックする（＝コミット済み差分は無く、ステージ済み・
未ステージ・未追跡の変更だけが対象になる）。収集対象は次の 3 種。

1. `git diff --name-only <base>...HEAD`（ブランチ上のコミット済み変更）
2. `git diff --name-only HEAD`（ステージ済み＋未ステージの作業ツリー変更）
3. `git ls-files --others --exclude-standard` のうち `.cs` ファイル（未追跡）

## 変更ファイルからテストクラスへのマッピング

`tests/` 配下の全 `.cs` ファイル（`bin`/`obj` を除く）をテストファイルのコーパスとし、
変更ファイルを次の規則でテストクラス（完全修飾名）へ写す。

1. **テストファイル自体の変更** — `tests/` 配下の変更された `.cs` は、
   そのファイルが宣言するテストクラスを選択する。
2. **製品コードの変更** — 変更された `.cs` のユニファイド diff から、その変更行に
   出てくるトークン（PascalCase の型名・メンバー名、4 文字以上の文字列リテラル）を
   抜き出し、それを単語として含むテストファイルのテストクラスを選択する
   （メンバーの変更ならその名前で絞り込める）。`new` を含む変更行は初期化データを
   別の型が所有している可能性があるため、差分トークンにファイル全体の宣言型も加える。
   これにより、ID や名前を直接書かず、表から期待値を読むテストも選択する（#170）。
   diff が取れない場合は従来どおりファイル全体の宣言型を使う。コメント-only の
   変更は何も選択しない。差分トークンとファイル内の宣言型が一つも重ならない場合も
   ファイル全体の宣言型を加える。
3. **テストが読む非コードファイルの変更** — `tools/star-manifest/*.json`、
   `tools/lowrarity/*.json`、生成済み `.cs`（`*.Generated.cs`、`Generated*.cs`）が
   変更された場合、そのパス（ディレクトリ名＋ファイル名、または相対パス）を
   参照しているテストファイル、および生成型を参照しているテストファイル（規則 2 と
   同じ方法）のテストクラスを選択する。
4. **全体実行へのエスカレーション** — 次の場合はスイート全体を実行する。
   - ビルドファイル（`*.csproj`、`Directory.Build.*`）が変更された
   - 変更ファイルの型名がテストファイルの 60% 超に登場する
     （＝どこでも使われている型への影響が広すぎるため）

どの規則にも当てはまらない変更（ドキュメント等）だけの場合は
「関係する変更なし」と表示して終了コード 0 で終わる。

## 実行方法

選択されたテストクラスから `dotnet test <project> --filter` 式を組み立てる。
フィルタは `FullyQualifiedName~名前空間.クラス` 条件を `|` で連結した 1 本の式で、
長すぎる場合（既定の上限 15000 文字）は複数回の `dotnet test` 実行に分割する。
テストプロジェクトは `tests/` 配下の `.csproj` を再帰収集する（`bin`/`obj` を除く）。

- まず `dotnet build -c Release` をプロジェクトごとに直列に実行し
  （`obj/` の共有を守るため。Release は CPU 主体のこのスイートで
  Debug の半分以下の実時間になる）、その後 `dotnet test -c Release --no-build` を
  プロジェクト並列で実行する（各アセンブリ内のテストは共有状態のため元々直列）。

- `dotnet` コマンド: 既定では `%LOCALAPPDATA%\dotnet-sdk\dotnet.exe` を使う。
  環境変数 `DOTNET` に実行ファイルのパスがあればそれを優先する。
- `DOTNET_ROOT` は子プロセスに設定される（既定 `%LOCALAPPDATA%\dotnet-sdk`、
  `DOTNET` 指定時はそのディレクトリ）。
- `DOTNET_CLI_UI_LANGUAGE=en` を設定して実行する（結果サマリの解析のため）。
- 標準入力は閉じた状態（`/dev/null` 相当）で起動する。

実行後、選択クラス数と合計の passed / failed を表示する。

## 例

```
# 変更が影響するテストだけを実行
python tools/test_changed.py

# 選択されるテストクラスを確認するだけ
python tools/test_changed.py --list

# main 以外の分岐との比較
python tools/test_changed.py --base origin/release/v2.0.1

# 全テスト実行
python tools/test_changed.py --all
```

## テスト削減（2回目）の記録

版別のデータ転記、細かい内部API確認、文言固定、Theoryの総当たりを削減し、
既存の代表シナリオへ集約した。新規テスト・ケース・製品コードの変更はない。
通信混乱・ホスト停止・再作成の `GrantSimulationTests` はシナリオ本体を維持し、
各60seedから既存の0／29／59の3seedだけを残した。

issue番号つき回帰、保存形式・互換・Protocol・内容指紋、取引・鞄あふれ・
続きから・Infinity・協力プレイ整合、全星到達性・参照購入等価性、fail-soft・
生成鮮度を保持。`Issue73.Native.Tests` と `SodRpg.Mod.Startup.Tests` は変更していない。
実装時の仕様書にある旧テスト名・実行件数は当時の検証記録として扱う。

`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all`
の実測は成功3,416→2,532、失敗0→0、既定skipのSlowFact4→4。
restore/build込みの実時間193.66→157.39秒（各1回の観測）。
884件、25.9%削減で、約4割の目安より必須保持を優先した。
このコマンドは.NETのみを対象とし、変更していないPythonテストや実ゲームは今回実行していない。


## #200 ローカル欠片化と旧取引の互換復旧

`tests/Issue73.Native.Tests` の現行回帰ケース：

- `Guest_overflows_match_stable_sequential_selection_and_bank_once_per_tick(int count)`：参加者の容量超過100／500回を、予約保護・レア度・スコア・同点時の順序を含む独立した逐次選択と比較。tick前は未加算、tick後は正しい合計で、本番 `Notify` を抽出して通知・ログ・トースト各1回、同数の鞄差し替え後の表示更新、空tickで追加なし、保存／送信なしを確認する。
- `A_normal_save_keeps_settled_kills_and_local_overflow_shards_without_rejoin_double_grants`：通常保存から再参加しても、精算済み撃破と容量超過の欠片を重複付与しない。
- `Local_overflow_shards_at_continue_checkpoint_restore_without_double_grants(bool infinity)`：通常／Infinityの参加者でチェックポイント時点の欠片を復元し、その後の容量超過を巻き戻して再実行しても1回分だけ付与する。
- `Persisted_legacy_overflows_query_and_recover_exactly_once(bool alreadyPaid)`：保存済みの旧義務を照会し、未払いなら既存の欠片回復を1回、支払い済みなら重複付与なしで解決する。

Coreの回帰ケースは最低レア度・最低スコア順、tick／保存境界での `Materials.Shard` 一括加算、cloneの独立性、保存で通知を消費しないこと、Infinity無料供給上限を扱う。Continueでは保存後の確定済み／未加算の集約分を両方戻す。手動分解の既存テストは変更しない。ネイティブハーネスはAPIダブルを使い、実ゲーム描画・実通信・実機の異常終了は対象外。

### #200 追加調査：あふれ1個ごとの費用

100個が同じtickにあふれた正常系を比較する。旧ダスト経路は #167 型の v2.4.0 ソース、直前は `0730e01`、今回が集約後。保存・確保境界がtickより先に来ると欠片を先に確定するが、通知の合計は消費せず、そのtickに1回だけ出す。

| 処理 | 旧ダスト経路 | 直前の欠片化 | 今回 | 根拠 |
|---|---:|---:|---:|---|
| ホスト取引受付／台帳記録 | 各100回 | 0 | 0 | `HostAuthority.Trades.cs:94-130`、`TradeAuthority.cs:318-352`。残した処理は旧未確定取引専用 |
| 取引送信／返信 | 各100回 | 0 | 0 | 旧 `ClientSession.SatchelDust.cs:80-87`（削除済み）、`HostAuthority.Trades.cs:127-130` |
| 本体ダスト付与／獲得通知RPC呼び出し | 各100回 | 0 | 0 | `HostAuthority.Trades.cs:34-35` と `Dew.Core.dll` の `DewPlayer.EarnDreamDust`：`IL_001c AddDreamDust`、`IL_0023 RpcInvokeOnEarnDreamDust` |
| MODの明示的なホスト保存 | 0 | 0 | 0 | `OnTrade` は返信までで保存呼び出しなし。台帳のシリアライズは中断保存時の `HostAuthority.Continue.cs:7-10` |
| あふれ準備のクライアント確認保存 | バッチ1回 | 0 | 0 | 旧 `ClientSession.SatchelDust.cs:80-87`。通常・中断の保存は別途維持 |
| ローカル欠片の素材加算 | 通常成功時0 | 100回 | 1回 | `Profile.cs:342-359`、`ClientSession.cs:1231-1236` |
| あふれ通知／ログ／Toast割り当て | 各100回 | 各100回 | 各1回 | `Rules.cs:323-339`、`DreamforgeUi.cs:138-157`、`Log.cs:9` |
| あふれによるHUD更新要求 | 100回 | 100回 | 1回 | `DreamforgeUi.cs:147`。以前も要求は代入だけで、実再構築100回ではない |
| あふれ選択 | 100回の線形走査、追加でRemoveの探索 | 同左 | 100回の安定O(n)部分選択＋RemoveAt | `Rules.cs:287-315`。拾ったときだけ判定し、全件ソートなし。取り除きとInfinity上限消費の時点は維持 |
| 記録タブの鞄ソート／LINQ一覧割り当て | OnGUIの描画ごと | 同左 | 同じ表示キャッシュを再利用 | `DreamforgeUi.cs:1416-1433,2901`。通知で1回失効、同数の差し替えも反映。0.3秒更新の既存規約は維持 |
| 素材加算による保管庫ソートのキー変化 | ダストではなし | 100回の素材変化 | 0 | `DreamforgeUi.cs:1287-1318`。素材を順序キーから外した。実ソート回数は描画タイミング依存 |
| あふれによるProfileChanged／星図・装備再計算 | 0 | 0 | 0 | `ClientSession.cs:605-612,1193-1228`、`ProfileSlotView.cs:89,197`、`ClientSession.Continue.cs:292` |

追加の否定確認：`Relic.Score` は `Relic.cs:161-167` の整数算術でLINQ／割り当てなし。図鑑への登録は拾得の `Rules.cs:254` の集合追加で、あふれ専用の重い再構築はない。図鑑の表示更新は `CodexView.cs:262-276` のcount／言語／dirtyによる遅延キャッシュ。ステータス表示は `DreamforgeUi.Window.cs:35-44`、HUD／トーストの実描画はRepaintのみで、イベントから直接描画しない。通常ドロップ／回収のイベント、戦利品生成、既存の描画時タイトル文字列や遺失物一覧ソートはあふれ専用処理ではなく、変更していない。

本体DLLを使い捨てのメタデータ／IL読出しで確認した範囲では、`EarnDreamDust → AddDreamDust → set_dreamDust` にSave呼び出しはなく、SyncVar更新と `OnDreamDustChanged`、獲得RPCから `ClientEvent_OnEarnDreamDust` へ進む。つまり保存一括化だけではこれらの1個ごとのRPC・イベントは消えなかった。本体イベント購読先の実機費用・保存頻度や、他MODの購読処理の寄与率は未測定。

### 使い捨てベンチ／スモーク

Linux／.NET 10、Releaseの実Core DLLで、鞄30個・レア度／スコア混在、ウォームアップ20回＋60回の中央値。選択は本番privateメソッドのdelegateで呼び、生成・初期化・独立した逐次選択の検算は計測外。反復ごとに最終の鞄Uid順と欠片量が一致することを確認した。

| あふれ数 | 直前のCore時間 | 集約後のCore時間 | 直前の割り当て | 集約後の割り当て | あふれ通知 |
|---:|---:|---:|---:|---:|---:|
| 100 | 0.2440 ms | 0.1356 ms | 43,312 B | 792 B | 100 → 1 |
| 500 | 1.3028 ms | 0.7593 ms | 210,720 B | 792 B | 500 → 1 |

この時間はCore処理だけで、Unity描画・実通信・Player.logのディスクI/Oは含まない。別の使い捨てスモークでは、本番から抽出した `TickSatchelOverflow → Emit → DreamforgeUi.Notify` をxUnit外で実行し、100個で300欠片／500個で1500欠片、ログ・トースト各1回／保存0回を観測。途中のシリアライズと空tickを挟んでも増えないことを確認した。実機GUI／Player.logそのものは未確認。ベンチとIL読出しの使い捨てファイルは納品に残さない。


## #146 鞄あふれ準備保存後の再参加（旧経路の検証記録）

以下は #200 より前のダスト換金経路で得た検証記録。現在の新しい鞄あふれはローカルプロフィールへtick内または保存・確保境界前に欠片をまとめて加算し、準備保存・取引キューを作らない。旧保存の `PendingTrades` は受領記録の照会を繰り返し、ダスト取引自体は再送しない。同じ台帳で未払い・未送信と確認された場合は既存の欠片回復処理を使い、支払い済みは重複付与せず、確認不能な間は保留を維持する。現行の利用者向け仕様は [MOD README](../src/SodRpg.Mod/README.md#鞄のあふれと欠片issue-200) を参照。

`Issue73.Native.Tests.SatchelOverflowSaveTests` の1件は、参加者の満杯の鞄から
`GrantPendingKill → Emit → 換金準備保存` を実行し、実ディスクの保存を読み直して
同じ遠征・ゾーンへ再参加する。精算済みの撃破が保存の `PendingKills` に残らず、
熟練度・経験値・乱数・戦利品が再付与されないことを確認する。
複数のあふれの取引が台帳ID付きで保存されることも同じケースで確認する。
一括化前の `5ea38b5` の Mod 精算・換金・保存経路へ差し替えた一時環境では、
再参加後の撃破数が期待値1に対して2となり、同じテストが失敗することを確認した。
本体API・通信はダブルであり、ゲーム実機の異常終了・実通信は対象外。

## #62 圧の追加報酬の死亡順序

`AuthoredMechanismNativeTests` の `Pressure_dividend_*`（3メソッド、17ケース）は、
実装の `OnEntityAdd → OnMonsterDeath → RemoveMonster → NativeAttributedKill.Postfix
→ PublishAttributedKill → OnEntityRemove` を通す。死亡記録を直接作る旧ヘルパーは使わない。
`extract_issue62_native.py` が製品ソースの該当メソッドと通常撃破の
`CaptureAuthoritativeRunKill` をビルド時に抽出し、APIダブルとコンパイルする。
抽出結果は `obj/` のみで、製品ソースは変更しない。ビルドには `python` が必要。

- CoinExplosion／Shout × ローカル／協力プレイ所有者の4ケースで、乱数を0に固定し、
  抽選1回・所有者宛の未確保欠片+1・同一通知／別packet／親Actor通知の重複防止を確認。
  通常撃破の分類記録をledgerで解決し、通常報酬を先に比較してから追加欠片を適用する。
- 圧不足・報酬無効・召喚敵・生成ダメージ・未確認spawn・帰属packetなしを両記憶で確認。
- entity削除なしでも、非スケール時間の9.99秒では保持、10秒で回収。
  32死亡を繰り返し、当選済み／未受付のspawn・期限・出所・抽選記録が残らないことを確認。

検証境界: Unity／Harmony実機ではなくAPIダブルを使用する。ダメージpacketの帰属と
生成ダメージの不許可フラグは入力として与え、ネットワークは宛先とreceiptを記録する。
実機2台の配送、nativeダメージpacketの生成処理、通常敵以外のDeathBurst／elite goldは対象外。

## #47 刻印の代償撤廃の回帰確認

- `GeneratedHeroAcceptanceTests` は全82刻印の型付き効果、ダメージ・傷の非弱体化、
  日英説明に「代償／Drawback」がないこと、現在の通信仕様でのビルド往復を確認する。
- `StarCarryoverV131Tests` は従来の形式3/4互換に加え、v1.31移行済みの形式4で
  保存した刻印IDが現在の生成定義に解決され、保持するPowerが引き継がれることを確認する。
  v1.31未移行の保存には、別仕様の効果変更時返却が引き続き適用される。
- C11のHP支払い→障壁変換、C15の装備・選択変更の明示承認は既存テストを維持する。
- テストクラス間の並列実行は `AssemblyInfo.cs` の設定で無効（#151: 言語設定は
  スレッドローカル化したが、星のレジストリは実在の旅人 ID をキーにした
  プロセス全体の共有状態のため、並列化すると登録の干渉が起きる）。

## #48 ボスセットのnative回帰確認

- `SodRpg.Core.Tests` は製品の `HostAuthority.Boss*.cs` を直接リンクし、
  Unity／Mirror／ゲームAPIのみをスタブで置き換える。報酬の差分再適用、
  Primusの強化係数、Azurakの実HP被ダメージ条件、Big Chomp／Soul Prisonの
  追加量上限、Light表示の100ms集約、ボス装備なしの早期終了を確認する。
- `SodRpg.Mod.Startup.Tests` は実際のHarmonyで製品のIL事前検証と起動処理を実行し、
  契約不一致による適用拒否と、自MODのpatchだけを取り消す失敗時処理を確認する。
  ゲームと同じHarmony 2.3.3 APIを保つ `Lib.Harmony.Thin` と、
  .NET 10へのroll-forwardに対応する `MonoMod.Core` 1.3.6を固定している。
- Linuxでの全体実行：
  `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python tools/test_changed.py --all`
- このハーネスは実ゲームの戦闘、Unity描画、Mirror協力同期、GC／frame時間を検証しない。

## #73 台帳・戦闘バッファ・差分同期の回帰確認

- `Issue73LedgerTests` は形式4→5の保存互換、連番の欠落・順序逆転・保存再読込を
  含む報酬の過不足防止、30秒期限、曖昧な復旧、旧 `expiredMonsters` の移行を確認する。
- `tests/Issue73.Native.Tests` は本番 Mod ソースをリンクする独立ハーネス。
  途中参加の再送と差分の32 RPC/フレーム枠、FIFO合流・種類間公平性、保存済みACK後の圧縮、
  空の仮peerの除外、配当の5秒遅延保存と書込済みリビジョン、入れ子のバッファ再利用、
  Harmony経由の直接 `RemoveAbility` と装備epochを確認する。
- ネイティブの通信・時計・エンティティだけをスタブ化する。配当の保存は実際の
  `ProfileStore` / `AsyncProfileWriter` で一時ディレクトリへ書き、試験後に除去する。
  `NativePersistence.targets` は SDK の Roslyn AST で保存・Emit・取引／照会と
  `ClientSession.Infinity.cs` のチェックポイント準備同期を選び、そのままコンパイルする。
  製品側の処理は複製・変更しない。
  #200 より前の `ContinueSaveTests.Overflow_at_continue_checkpoint_survives_restore_and_settles_exactly_once`
  は通常の既存キュー／Infinity準備中の回収 × 未実行／支払い済みの4ケースで、
  台帳ID付き義務の保存、別セッションへの復元、欠片の一度だけの回復とダストの二重払い防止を検証した。
  これは旧ダスト換金経路の記録であり、現在の新しいあふれに取引義務を作る仕様ではない。
- Core既存ハーネスの `ExposeAuthoredPendingMetadata` は、テスト公開の `PendingGimmick` と
  アクセス範囲を揃えるため、生成したコンパイル単位だけで `AuthoredPendingGimmick` を
  `internal` にする。製品ソースと値型メタデータの処理内容は変更しない。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python tools/test_changed.py --all` で
  両プロジェクトを実行する。単独確認は `dotnet test <project> --filter FullyQualifiedName~Issue73LedgerTests`
  または `FullyQualifiedName~NativeAcceptanceTests`。
  実機のフレーム時間・確保量・Unity本体のRPC転送はこのハーネスの検証範囲外。

## マッピングロジックの単体テスト

`tools/tests/test_test_changed.py`（unittest）がマッピングロジックの単体テストを
持つ。実際の git / dotnet は使わず、一時ディレクトリのダミーリポジトリと、
実リポジトリのカタログ・全テストファイルを使って検証する。#170 の回帰試験は
`Boulderburst Hammer` の1文字変更で表を読むテストが選択されることを確認する。

```
python -m unittest discover -s tools/tests -p "test_test_changed.py"
```

## 制限

- 規則 2 は `src/` 配下の `.cs` のみが対象。`tools/BalanceSim` 等の他の製品コードの
  変更は、テスト側で対応するテストファイルを同時に変更しない限り選択されない。
- 型名・パスの抽出は正規表現ベースなので、コメント内の文字列は無視されるが、
  文字列リテラル内の `class Xxx` という形は誤検出される可能性がある
  （誤検出はテストの過剰選択としてのみ現れ、欠落にはならない）。
