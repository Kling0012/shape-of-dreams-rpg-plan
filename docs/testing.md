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
| `--all` | スイート全体を実行する（Core はクラス単位のフィルタで複数プロセスに分割） |
| `--slow` | `Speed=Slow` の網羅テスト（`SODRPG_SLOW=1`）も実行する（既定では除外）。低速テストを持つクラスはクラスごとに別プロセスで並列に走る |
| `--check-ci-matrix` | `ci.yml` の slow-tests matrix が低速テスト検出と一致するかだけ検査して終了する（CI の core-tests ジョブでも実行） |

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
   そのファイルが宣言するテストクラスと、そこで宣言された型を使うテストを選択する。
   共通の補助型や fake を変更した場合も、その利用者を含める。
2. **製品コードの変更** — `src/` と、テストプロジェクトの `ProjectReference` が
   参照する `tools/` 配下の `.cs` が対象。ツールの場合は宣言型に加えてツール名を
   挙げているテストも選択する。変更された `.cs` のユニファイド diff から、その変更行に
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
   同じ方法）のテストクラスを選択する。`tests/` 配下の `.cs` 以外の入力も
   同様にパスを参照するテストを選択し、参照元不明のデータなら全体を実行する。
   参照のない Markdown・reStructuredText 文書と `bin`/`obj` は対象外。
4. **全体実行へのエスカレーション** — 次の場合はスイート全体を実行する。
   - ビルドファイル（`*.csproj`、`Directory.Build.*`）が変更された
   - 変更ファイルの型名がテストファイルの 60% 超に登場する
     （＝どこでも使われている型への影響が広すぎるため）

どの規則にも当てはまらない変更（ドキュメント等）だけの場合は
「関係する変更なし」と表示して終了コード 0 で終わる。

## 実行方法

選択されたテストクラスから `dotnet test <project> --filter` 式を組み立てる。
フィルタはクラスごとに `FullyQualifiedName~名前空間.クラス.` と
`FullyQualifiedName~名前空間.クラス+` の 2 条件（末尾の区切り文字でクラス内だけに
一致させ、名前が先頭一致する別クラスの二重実行を防ぐ。`+` 条件は入れ子クラス
`Ns.Class+Nested` のテストを取りこぼさないため）を `|` で連結した式で、
長すぎる場合（既定の上限 15000 文字）は複数回の `dotnet test` 実行に分割する。
テストプロジェクトは `tests/` 配下の `.csproj` を再帰収集する（`bin`/`obj` を除く）。
`Microsoft.NET.Test.Sdk` の `PackageReference` または `IsTestProject=true` があるものを
対象とし、Culinary の補助ライブラリ等は単独で実行しない。XML のコメントは無視し、
最寄りの `Directory.Build.props` / `.targets` とリポジトリ内のリテラルパスの
`Import` も読む。条件付きの項目は安全側に含めるが、MSBuild のプロパティ展開や
ワイルドカード import は評価しない。
クラスとプロジェクトの対応は csproj から判定する: 明示的な `<Compile Include>` の
リンク先と、`EnableDefaultCompileItems`（既定のフォルダ glob）と `<Compile Remove>` を
考慮したフォルダ位置の両方。同じファイルを複数プロジェクトがコンパイルする場合
（他プロジェクトからリンクされつつ相手プロジェクトのフォルダ内にある等）は
両方のプロジェクトに割り当てる。どのプロジェクトにも属さないクラスは従来どおり
全プロジェクトで実行する。

- まず `dotnet build -c Release` をプロジェクトごとに直列に実行し
  （`obj/` の共有を守るため。Release は CPU 主体のこのスイートで
  Debug の半分以下の実時間になる）、その後 `dotnet test -c Release --no-build` を
  複数プロセス並列で実行する（各アセンブリ内のテストは共有状態のため
  `DisableTestParallelization` のまま直列。並列化はプロセス分割による。
  同時実行数は CPU コア数を超えない）。`--no-build` で実行する前に、MSBuild の
  `TargetPath` が存在するかを確認する。別の実行環境へテストだけを移して成果物が
  存在しない場合は、同じフィルタの `dotnet test -c Release` でビルドしてから実行する。
  `TargetPath` の取得に失敗した場合は、そのエラーを返す。

- プロセス分割（#404）: `--all` では Core スイートをクラス単位のフィルタで
  5 プロセスに分ける。所要時間の大半を占める重い 5 クラス
  （AllocationEquivalence・GeneratedHeroAcceptance・StarMapReachability・
  PressureRewardMultiplier・StarMapOptimize）がそれぞれ別々のプロセスの軸になり
  （重いクラスどうしを同じプロセスにしないため、5 が最小のプロセス数）、
  残りのクラスはテストメソッド数を重みにその 5 プロセスへ均等に振り分ける
  （同じクラスは必ず同じプロセス）。`--slow` では Speed=Slow テストを含む
  クラスもクラスごとに別プロセスで並列に走らせる。全プロセスの和が
  従来のフィルタなし 1 プロセスと同じテスト集合・合否になる。
  選択実行（`--all` 未指定）でも、`--slow` 時の低速クラスは同様に単独プロセスとする。
  CI の slow-tests ジョブ（main マージ後）も同じクラス単位の matrix で走る。
  matrix は手で列挙するため、`--check-ci-matrix`（CI の core-tests ジョブで実行）
  が列挙と検出の不一致を失敗として報告する。

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
  `NativeStartupTests` は開始時に模擬ゲームの `GameManager.softInstance` を消し、
  他フィクスチャの遠征状態を持ち越さずロビー状態から実行する。
  Infinity の停止時点の遠征判定はこの singleton の有無を使うため、
  Infinity 自身の静的フィールドを初期化するだけでは隔離できない。
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

## #435 宿敵の名札色（2026-10-16）

引き継いだスナップショットには、`DreamforgeUi` の共通定数として宿敵色
`#4dd8ff`（シアン）、悪夢化色 `#ff6ad5`（ピンク紫）、変種色 `#ffb347`（橙）と、
宿敵がある場合に変種名札を描かない処理が含まれていた。READMEと検証記録には
異なるシアン値が残っていたため、実装の `#4dd8ff` に統一し、CHANGELOGの重複した
説明をまとめた。宿敵の判定は `NightmareAffix.Nemesis` のビットで行うため、
ボス／ミニボス共通で、ほかの接頭効果が付いていても宿敵色を優先する。
ヴァイオレットは悪夢化のピンク紫に近いため、前回のシアン採用と既存の名札キャッシュを
維持した。通信・保存や毎フレームの処理は変更していない。

ほかの強調表示もコードで照合した。`MonsterCues.ColorFor` は金・茶橙・青・緑、
`BossEffectColor` は冷気の水色、闇の紫などを使うが、宿敵の `#4dd8ff` と同値ではない。
レア度の青 `#4fa8ff`・紫 `#c475ff` とも異なる。青系の強調表示と近い色域ではあるため、
あらゆる背景での視認性を保証する検証ではない。MODの敵名札3種の色と、
モデルの発光・戦闘効果・装備名の色は区別して確認した。

協力プレイの適用経路はソースで確認した。`HostAuthority.MakeNemesis` が宿敵ビットを
設定し、`SendMonsterClassification`／`FlushMonsterSync` が `DreamforgeNightmareMsg.affixes`
として個別同期／全クライアントへ送る。`ClientSession.OnNightmare` は
`Nightmares.Sanitize` で宿敵ビットを保持し、名札用辞書へ格納する。
`DreamforgeUi.DrawNightmareLabels` にはホスト／参加者による色の分岐がなく、
どちらも同じ定数を使用する。本体の通常ボス／エリート名表示とは別のMODの頭上名札が対象。

以下は今回のセッションで実行した検証：

- `dotnet build src/SodRpg.Mod -c Release -p:GameDir="/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams" --nologo`：終了コード0、エラー0・警告6。ゲーム参照DLLでコンパイルした。ゲームへの配置処理は実行されていない。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~SodRpg.Core.Tests.NightmareTests.|FullyQualifiedName~SodRpg.Core.Tests.MonstersV129Tests.|FullyQualifiedName~SodRpg.Core.Tests.VariantsV124Tests.|FullyQualifiedName~SodRpg.Core.Tests.NameplateTrimTests.|FullyQualifiedName~SodRpg.Core.Tests.MonsterAuthorityStateTests.' --nologo`：終了コード0、59成功・0失敗・0スキップ。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue73.Native.Tests.NativeAcceptanceTests.DifferentialUpdatesCoalesceWithoutMovingIdsBehindTheirNeighborsAndRotateCategories' --nologo`：終了コード0、1成功・0失敗・0スキップ。既存の敵分類差分の集約・配信順をAPIダブルで確認したもので、宿敵の色や実通信の検証ではない。
- 既存テストの対象は悪夢／変種のルール、宿敵ビットの保持、名札の生存管理、敵分類状態と差分配信。色選択と描画の優先順はソースを確認したもので、既存テストは描画メソッドを実行しない。新しいテストは追加していない。
- **未実行**：Unity実画面で宿敵化したボス／ミニボスの名札を比較する確認、本体名札も含めた背景別の視認性・1920×1080の実描画、実際の協力通信。ゲーム実行環境が提供されていないため、報告の画面上の状況は再現・目視確認していない。
- **未実行**：フルテスト・Slowテスト、今回の名札表示と無関係なテスト。作業中は関連部分だけを検証し、全体検証は後続工程に委ねる指示による。

## PR #505 取り込み：命中時の確保削減とボス装備（2026-10-16）

元PR #505の製品コード変更を取り込み、光の精霊の溜め処理で新しく読む
`Actions[1]`には`Actions.Count >= 2`のガードを追加した。不足時はその溜め処理だけを
見送る。`BossMoveProfile`はコンストラクターで1〜16個の非null行動を検証し、
配列のコピーを読み取り専用で保持するため、アズラク等の`Actions[0]`は必ず存在する。

設定の原本（`tools/balance/bosses/{maw,azurak,light}.json`）、生成済みの
`BossMawBalance`／`BossAzurakBalance`／`BossLightBalance`、`BossProfiles`を突き合わせた。
今回の読み取り先は従来の直書きと一致する。

| 効果 | プロファイルの行動・値 | 従来の動作 |
| --- | --- | --- |
| 大顎の護符 | `charm.Actions[0]`: MainHits=4、CounterLifetimeMillis=4000 | 3回分を保持し、4回目に回復。最後の命中から4秒でリセット |
| アズラクの2部位 | `stage2.Actions[0]`: MainHits=3、CounterLifetimeMillis=4000、RangeMilli=8000 | 3回・最後の命中から4秒・射程8m |
| 光の精霊の手 | `hands.Actions[0]`: MainHits=3、CounterLifetimeMillis=6000 | 最初の命中から6秒以内に3回 |
| 光の精霊の頭 | `head.Actions[0]`: RangeMilli=8000 | 射程8m |
| 光の精霊の2部位 | `stage2.Actions[1]`: Count=3、LifetimeMillis=6000。`Actions[0]`: DelayMillis=350 | 3回溜め、最終加算から6秒保持。次の入力で消費して350ms後に発射 |

`BossEquipmentSnapshot`は装備キャッシュがない場合にfalseを返す。
`BossEnsure`はその戻り値で`ClearBossEffects`を呼び、Build=null・EquipmentDirty=trueへ
戻す。次回はBuildの差分があるため早期成功せず再照合し、情報がそろえば再構築する。
`changed`をfalseのまま返しても、この解除・再試行を迂回しないことをソースで確認した。

刻印の`Upside`と選択子はそれぞれのコンストラクターが`Array.AsReadOnly`で保持し、
getterのみ公開する。添字ループはこのコレクションを直接参照し、列挙子を生成しない。
`GimmickRuntime.ActiveStates`は具体型のListを返し、各foreachで構造体列挙子を使う。
`ForgetVictim`は逆順RemoveAtで全一致を削除し、捕捉ラムダを生成しない。
刻印の結果オブジェクト等、ほかの経路に存在する確保をゼロにする変更ではない。

実行した検証：

- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter '<対象11クラス>' --nologo`：194成功・0失敗・0スキップ。対象はBossStageB3NativeTests、BossRuntimeNativeTests、BossSetsStageATests、BossSetsStageB12Tests、BossResourceBoundsTests、BossBalanceMigrationTests、GimmickEffectsV129Tests、GimmickPrecisionV131Tests、HotPathQueryFlagTests、ScopedKeystoneModifiersTests、FractionalScopedModifiersTests。
- 既存の光の精霊のケースは製品コードをリンクし、2／3／6部位で3回の溜め中は未発射、4回目の入力から350msを境に発射、終端の落雷・障壁・重複なしを確認する。ほかの既存ケースはボスの寿命管理・上限、設定JSONと行動件数／値の一致、仕掛けの期限・対象削除、刻印の対象選択・数値を確認する。
- `dotnet build src/SodRpg.Mod -c Release -p:GameDir="/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams" --nologo -v:quiet`：エラー0・警告6。参照用ディレクトリにModsがないことを確認して実行し、配置処理なし。
- `python3 tools/balance/gen_cs.py --check`、`git diff --check`：成功。
- `python3 tools/test_changed.py --list`は582クラスを選択したため実行には使わず、上記11クラスを明示した。新規テスト・ケースは追加せず、元PRの新規0バイト断言テストも取り込んでいない。

**未実行**：Unity実機での戦闘・装備情報未準備時の例外再現／回復・協力通信は、ゲーム実行環境が提供されていないため未確認。装備キャッシュ欠落時の解除／再試行と、大顎の4回目・アズラクの3回目・光の手の6秒の同等性は設定とコードの照合によるもので、当該条件を追加ケースで実行した証拠ではない。GC割当量・フレーム時間の実測は行っていない。実行環境依存の0バイト断言を追加せず、型と生成箇所を点検した。フルテスト・Slowテスト・無関係なテストは作業中に実行しない指示に従い、後続工程へ委ねる。

## #546 PR #505 レビュー追補：前提の明記と検証の強化（2026-10-17）

コードの動きは変えず、pr #505 のレビューで挙がった前提を明示した。光の精霊の溜め処理は
`stage2` の行動が2件未満のとき、その処理だけを見送り、警告ログを1回だけ出す
（`HostAuthority.BossLight.LightChargeInput`、静的フラグで1回に限定）。大顎の護符の
`MainHits - 1` 判定は、`boss_values.py` の `CELL_FLOORS` で `maw` の `charm` 1番目アクションの
`mainHits` が2以上であることを生成時検証で保証し、判定箇所にも前提のコメントを置いた。
`GimmickRuntime.ActiveStates` はListで受ける呼び出し規約をコメントで明示した
（IReadOnlyListへ受けるとボクシングが復活）。表側の下限の注意書きは `tools/balance/README.md` に追加。

後続の検討事項（このIssueでは対応しない）：

- 装備キャッシュ欠落時に `BossEnsure` が `ClearBossEffects` → 再照合 → 再構築する経路と、
  ボス装備の回数・持続の境界（大顎の4回目・アズラクの3回目・光の手の6秒等）へのシナリオテスト追加は、
  後続のIssueで検討する。現在は設定JSONとコードの照合（`BossBalanceMigrationTests` 等）のみ。
- `BossEquipmentSnapshot` 失敗後に装備情報がそろったときの効果の再構築・再照合の目視確認は
  実機確認が必要なため、実機確認リストに残す（作業環境ではゲームを起動できない）。

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
