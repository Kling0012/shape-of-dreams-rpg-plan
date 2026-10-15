# 星図の加点処理の調査（2026-10-15）

対象: `perf/star-allocate-1015`、基点 `4d947c9b`。Linux / .NET SDK 10.0.111。
実ゲームは起動できないため、コード追跡と Release の一時 Core ベンチマークで判断した。
以下は Unity/Mono 上のクリック時間ではない。

## クリックからの経路と判断

1. `DreamforgeUi.DrawStarCanvas` / 二択パネル / 刻印一覧から `Rules` を呼ぶ。
   `AddTalentRank` 等は `EffectiveAllocationValidation.Preview` → `Commit` を通る。
   割り振りのコピー、候補評価、必要なら払い戻し計画の作成はこの場で必要。
   既に依存関係の絞り込み、局所評価、プレビュー内キャッシュがあるため、今回は変更しない。
2. `_starDirty` とビルド変更を通知する。`MarkDirty(true)` はビルドキャッシュを無効化し、
   `Feats.Check` を呼ぶが、同期ディスク書き込みは元から行っていない。
3. 次の Layout で `RefreshStarState` を実行する。旧実装は約900星すべての条件説明、
   二択本文・候補本文、使用済みポイント依存のダメージ注記、検索用本文、ツールチップを生成する。
   ここが計測上の主因だった。左の一覧も全行のラベルを作り直し、幅を無効化していたため、
   次の描画で全行に Unity の `GUIStyle.CalcHeight` が走っていた。
4. `StarMapView.Update` は同じレイアウト・表示領域・パン・ズームなら即時復帰する。
   星の円形テクスチャも初回に作って再利用する。加点での幾何・テクスチャ再構築は確認されず、変更しない。
5. 取得済み効果の `StarSummary.Compute` は一覧を開いている場合のみ再計算する。
   `Build.Compute` は送信や既存HUD等の要求時に実行する。定義検証には既存キャッシュがある。
6. クライアントは dirty なビルドを次の Tick で送信していた。ホストの受信後の解析・再計算は
   既存 `BuildUpdateCoalescer`（最短0.5秒、最新値優先）でまとめられている。
7. 保存は `AsyncProfileWriter.Enqueue` が呼出側で直列化し、ディスク書き込みを別スレッドで行う。
   旧通常保存間隔は30秒。終了時の `SaveNow` / `FlushSaves` は既存のまま使用する。

## 変更

- Layout ごとの状態確認と、変更時の到達可否・空きポイントの全体更新は残す。
  残りポイント、前提、刻印枠は離れた星の可否にも影響するためである。
- 本文・検索文字列は段数または選択肢が変わった星だけ更新する。
  言語、旅人、レイアウト、効果上限・登録定義の変更時は全表示データを作り直す。
- ツールチップは更新世代で無効化し、キャンバス・刻印・左一覧でホバーした星と、開いた二択だけ生成する。
  二択の候補本文も表示時に生成し、使用済みポイントが同じ間は再利用する。
- 遅延生成には直前 Layout の割り振りコピーと到達可否を使う。
  MouseUp で本体が変わっても、次の Layout まで古い段数と新しい条件を混在させない。
- 左一覧は段数・選択結果・可否の変わった行だけラベルと高さを更新する。
  後続行の位置は最初の変更行から加算し直す。幅変更時は従来どおり全行を計測する。
  無変更フレームで高さ更新のための追加全件走査は行わない。
- 刻印比較用の `string.Join` を固定スロット比較に置き換え、無変更 Layout の文字列割り当てを減らす。
- ロビーの加点・返却・二択・刻印・全振り直しは最初の変更から最大150msでビルドを送る。
  連打で期限を後ろへ延ばさない。遠征中、再送要求、通常の別ビルド変更、旅人・DreamLevel変更は待たせない。
  検証、ローカルのビルドキャッシュ無効化、ホストの再検証は省略しない。
- ロビーのビルド変更（既存の払い戻し承認を含む）は最初の変更から最大3秒で保存予約する。
  メニュー・星図タブを閉じたとき、active scene 変更、zone 到着でも未予約分を保存する。
  これらは非同期書き込みを予約し、終了時は既存の Flush で待つ。
  バックグラウンド書き込み失敗時は境界保存の pending も立て直し、既存の再試行を利用する。
  変更前の30秒より保存期限を早める。払い戻しUIの別経路も守るため、ロビーの装備変更にも同じ保存期限が適用される。
- 保存境界の例外は記録して閉じる操作・シーンイベントを継続する。
  タブ変更後に IMGUI が中断されても finally で保存境界を通す。

重い文字生成は全星数 V に比例する呼び出しから、本文変更数 Δ と表示中の星数 H に比例する呼び出しへ減る。
段数の確認、可否の更新、到達性の計算は引き続き全体を処理する（おおむね O(V log A + E)、A は取得星数）。
左一覧の文字計測は変更行だけ、位置の加算は変更行以降。ルール側の計算量は変えていない。

## 一時ベンチマーク

`/tmp/sod-starperf-1015/StarPerf.csproj` と `Program.cs` を作り、
`dotnet run --project /tmp/sod-starperf-1015/StarPerf.csproj -c Release` で実行した。
リポジトリには新規テストを追加していない。ログ: `/tmp/sod-starperf-1015/after.log`。

生成済み Mist / Vesper を登録し、実際の Rules で100・300・450ポイントまで取得。
各処理は3回のウォームアップ後15回、Stopwatch の中央値とスレッドの割り当て量を測った。
旧方式は全星の説明API呼び出しを再現したもの、新方式は変更星の本文と全体可否の更新、
スナップショット、ホバー中の二択ツールチップと候補2つを再現したもの。
GUI・ネイティブ文字計測、完全なUI文字連結、実際の ClientSession は含まない近似であり、
この表から実ゲーム全体の速度倍率を算出することはできない。

| 旅人 / 使用済み | 星数 | 旧方式の文字生成 ms / B | 新方式の近似 ms / B |
|---|---:|---:|---:|
| Mist / 100 | 899 | 50.669 / 24,796,610 | 0.904 / 20,272 |
| Mist / 300 | 899 | 41.432 / 24,372,005 | 0.628 / 30,368 |
| Mist / 450 | 899 | 31.070 / 22,822,378 | 1.129 / 52,944 |
| Vesper / 100 | 892 | 24.186 / 21,064,722 | 0.251 / 17,056 |
| Vesper / 300 | 892 | 26.864 / 20,603,644 | 0.548 / 27,576 |
| Vesper / 450 | 892 | 26.899 / 19,662,898 | 0.995 / 47,184 |

同じ実行で、プレビューは0.654〜6.832ms、Build.Computeは0.051〜0.700ms、
Feats.Checkは0.008ms以下、StarSummary.Computeは0.664〜2.309ms、
ProfileCodec.Writeは0.016〜0.293msだった。直列化以外の保存準備・実ディスクI/Oは未計測。

文字生成による数十msと約20MB以上の一時割り当ての大部分を避ける見込み。
取得済み効果一覧を開く場合の計算とルール検証の時間は残る。
実ゲームのクリック総時間、GC停止、ホスト上の時間は未計測。

## 検証と制限

- 指定 Mod Release ビルド: 0エラー（既存警告6件）。
- 既存テスト: 表示・概要・返却UI・非同期保存・ビルド集約等50件と、
  割り振り同値性・検証・ダメージ倍率・ロードアウト・表示文言等100件が合格。
  `.NET 8` 不在で最初の実行は testhost 起動前に中断し、
  `DOTNET_ROLL_FORWARD=LatestMajor` を付けて実行し直した。
  実行コマンドは次のとおり（2回目は `Speed!=Slow` を指定）。

  ```sh
  dotnet build src/SodRpg.Mod -c Release -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/x
  DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~StarMapPresentationTests|FullyQualifiedName~StarMapViewTests|FullyQualifiedName~StarSummaryTests|FullyQualifiedName~AllocationRefundUiTests|FullyQualifiedName~AsyncProfileWriterTests|FullyQualifiedName~BuildUpdateCoalescerTests'
  DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --no-build --filter '(FullyQualifiedName~AllocationEquivalenceV131Tests|FullyQualifiedName~EffectiveAllocationValidationV131Tests|FullyQualifiedName~StarDamageScalingTests|FullyQualifiedName~StarLoadoutsTests|FullyQualifiedName~StarUiTextV131Tests|FullyQualifiedName~StarTooltipNoRawIdTests)&Speed!=Slow'
  ```

- 同値性テストは既存の参照検証器との計画・保存結果比較を含む。
  Rules / EffectiveAllocationValidation / Build / 表示文生成APIは変更せず、呼ぶ対象と時期を変更した。
  UI組立文字列は旧実装と照合したが、新しいキャッシュそのものを実ゲームで検証したものではない。
- 未実行: `tools/test_changed.py --all` と Slow テスト。
  このワーカーでは全件・低速スイートを実行しない指示に従い、パイプラインに残す。
- 未実行: 実ゲームの描画・スクリーンショット・連打・払い戻し承認・タブ/シーン/終了境界保存・協力通信。
  ゲームを起動する環境がないため。`dreamforge_perf` の機能は確認したが、実コンソール計測はしていない。
  シーン境界と送信待ちの組み合わせは、#438 の追補（下記）で抽出した実メソッドの直接実行により確認した。
  終了時は既存テストが `FlushSaves` を実行している。

## 追補: レビュー提案の確認（#436、2026-10-15）

マージ後のレビューで挙がった軽微な指摘について、全参照箇所の洗い出しと確認結果。

### ツールチップと不足条件の参照箇所の監査

`node.Tooltip` と `n.MissingRequirements` の読み取りは、リポジトリ全体で次の4経路だけだった。
いずれも読む前に `EnsureStarTooltip` が走るため、空のツールチップや古い条件文は出ない。

| 経路 | 場所 | 保護 |
|---|---|---|
| キャンバスのホバー | `DreamforgeUi.DrawStarCanvas` 末尾 | 描画直前に `EnsureStarTooltip(hover)` |
| 刻印バーのホバー | `DrawKeystoneBar` → `_starKeystoneHover` | バー自身はラベルと状態色だけを描く。ホバー添え字は `DrawStarCanvas` 末尾で `hover` に合流し、同じ `EnsureStarTooltip` を通る。直接読んでいる箇所は無い |
| 左一覧（星一覧） | `DreamforgeUi.AuthoredStars.DrawStarClusterList` | 行にマウスがあるとき `EnsureStarTooltip(index)` 後に `tooltip` へコピー |
| 二択パネル | `StarChoiceOverlayRect` → `StarBuildChoicePanel` | `EnsureStarChoiceText` → `EnsureStarTooltip` が先。遠征中とクリックできない時の「取得できません」行（`state.MissingRequirements`）もこの後で読む |

ツールチップ以外の消費者も調べた。検索は `SearchDescription` だけを使い（`RefreshStarState` が変更星のみ更新）、
凡例の吹き出しは静的テキスト、左一覧の行ラベルは `NavigationLabel`（`node.Available` を使う。`MissingRequirements` 不使用）。
`MissingRequirements` を読むのは二択パネルの上記1か所だけで、null や古い値の利用は確認されなかった。
したがって、EnsureStarTooltip を追加したキャンバス・左一覧・二択の3経路以外に更新処理は不要です。

### 刻印配列の長さの安全化

`RefreshStarState` の刻印比較と `Array.Copy` は `hs.Keystones` が `KeystoneSlots.Max` と同長である前提だった。
将来この長さが変わると毎 Layout で例外が出てUIが閉じ続けるため、`Math.Min(_starKeystoneState.Length, hs.Keystones.Length)`
で短い方に揃え、余った枠は空（null）扱いに変更した（比較・コピーとも）。現在は同長なので挙動は変わらない。

### DeferSave の期限と連打

`DeferSave` は `if (_nextSave > deadline) _nextSave = deadline;` と、既存の期限と新しい期限の早い方（min）を残す実装であり、
既存の保存期限を後ろへ延ばさないことを確認した（変更不要）。ビルド送信も同じ形で、`MarkAllocationDirty` は
`buildWasDirty ? buildDue : now + 0.15f` と最初の期限を維持するため、連打で送信期限（150ms）・保存期限（3秒）が後ろへ延びない。

`MarkDirty(true)` は `Profile.Run == null` のとき `_allocationSavePending` を立てて `DeferSave(3f)` を呼ぶ。
ロビーでの装備変更（`DreamforgeUi.GearDetail` の装備・解除など、`MarkDirty(true)` を通るすべて）にも同じ3秒の保存予約が付く。
これは意図した変更（上記「変更」節のとおり、払い戻しUIの別経路も守るため）。

### 例外時の Close() の挙動変更

`DreamforgeUi` の描画例外時の処理を `Open = false` から `Close()` に置き換えたため、例外で閉じるときに
`SavePendingAllocation` だけでなく `CancelStarDrag()` と確認状態（強化・再調律・分解等の `_confirm*`）のクリアも走る。
プレイヤーから見れば「例外で閉じた際にドラッグや確認の途中状態が残らない」ことで害はないが、
本来の範囲外の振る舞い変更なので、ここに記録しておく。

### 回帰テストの現状と実機確認リスト

次の3つの差分更新は `DreamforgeUi` の Unity IMGUI（`Event.current`・`GUIStyle.CalcHeight`）に依存する
privateな内部状態で、Issue73抽出ハーネスの抽出対象にも入っていないため、リポジトリ内の既存テストでは
直接確認できていなかった。

- 左一覧の高さの部分再計算（`RefreshStarClusters` の変更行のみの計測と `_starClusterDirtyFrom` からの加算）
- `TextRank`/`TextChoice` による本文更新スキップ
- ツールチップの世代管理（`TooltipVersion`/`_starTextVersion`）

修正ラウンド2回目（2026-10-15）で、これら3つとホバー維持加点・二択変更の表示更新を、下記
「UI 差分更新の直接実行による確認」のシナリオランナーで実メソッドを実行して確認した
（全項目合格）。リポジトリ内の恒常的な回帰テスト（xunit）は #440 のままで、
抽出ハーネスへの `DreamforgeUi` 抽出の追加と IMGUI 境界スタブの用意を案としている。

保存・送信側（`MarkAllocationDirty`・`SavePendingAllocation`・`DeferSave` の期限）は、#438 で抽出契約
（`NativePersistence.targets`）に `MarkAllocationDirty`/`SavePendingAllocation`/`OnAllocationSceneChanged` を追加し
（境界の `Scene` 型と `_allocationSavePending`/`_allocationBuildDue` フィールドは #433 の 980224b で既に準備済み）、
実際のソースから抽出した実メソッドを一度限りのシナリオランナー（`/tmp/sod-alloc-436`、リポジトリには加えない。
#433 の一時ベンチマークと同じ方式）でリフレクション駆動して直接確認した。条件・期待結果・実際の結果
（2026-10-15、全項目合格、`ALL SCENARIOS PASSED`）:

| シナリオ | 操作（実メソッド） | 期待結果 | 実際の結果 |
|---|---|---|---|
| 連打中の送信期限 | t=100 でベースライン送信後 `MarkAllocationDirty`（due=100.15）、t=100.10 で再 `MarkAllocationDirty` | 送信期限は最初の変更+150msのまま後ろへ延びない | due=100.15 のまま（100.25 に延びない）。合格 |
| 送信ゲート | 期限内（100.10・100.149）に `SendBuildIfNeeded`、期限到達（100.16）で再実行 | 期限内は送らず、期限到達で一度だけ送る | 期限内 sent=1（不変）、100.16 で sent=2・`_buildDirty`=false。合格 |
| 連打中の保存期限 | t=100 `MarkAllocationDirty` → t=101.5 再実行（+ `MarkDirty(true)` も） | 保存予約は最初の変更+3秒のまま | `_nextSave`=103 のまま（min）。合格 |
| 装備変更等の保存予約 | t=100 `MarkDirty(true)`（ロビー） | `_allocationSavePending` が立ち+3秒の保存予約 | pending=true・`_nextSave`=103。合格 |
| メニュー・タブを閉じた直後 | `MarkAllocationDirty` → `SavePendingAllocation` | 即座に1回保存し pending を落とす。保留が無いときは再保存しない | saveCount=1・pending=false・ディスクへ現行リビジョン書込・再呼び出しで saveCount=1 のまま。合格 |
| シーン遷移時 | `MarkAllocationDirty` → `OnAllocationSceneChanged` | 未保存の割り振りを保存する | saveCount=1・pending=false・書込完了。合格 |
| 書き込み失敗後の再試行 | 保存先を壊して `SavePendingAllocation`（背景書き込み失敗）→ t=105 `TickPeriodicSave` → t=110 再実行 | 失敗で pending を立て直し+5秒の再試行期限（30秒に延ばさない）を設け、期限で書き直し、成功後は失敗表示を消す | 失敗検知→pending 復活・`SaveError` 表示・`_nextSave`=110→t=110 で書き直し成功・pending=false→`SaveError` 消滅。合格 |

UI から `SavePendingAllocation` を呼ぶ場所（メニューを閉じた時・星図タブを離れた時、`DreamforgeUi.cs` の
`Close`・タブ切替）はコード追跡のとおり。終了時の `FlushSaves` は既存テスト（`NativeAcceptanceTests` 等）が
後片付けで実行している。抽出契約を拡張した後の既存テストは 67件（`ContinueSave`/`ClientTickIsolation`/
`TradeSendException`/`SatchelOverflowSave`/`LobbyReturn`/`NativeAcceptance`/`InfinityClearSave`/
`OverflowBonusFailSoft`）すべて合格。

実機で確認すべき残項目（ゲームを起動する環境が無いため未実行）:

- ホバーを維持したまま別の星へ加点したとき、条件文とダメージ注記が即座に更新されること。
- 二択を変更した後、星の本文表示と左一覧の表示が正しいこと。

（連打中の送信・保存期限、メニュー・タブを閉じた直後とシーン遷移時の保存、書き込み失敗後の5秒再試行は、
上記のシナリオランナーで対象経路（実際の `ClientSession` の各メソッド）を実行して確認済み。
上の2項目も修正ラウンド2回目に UI シナリオランナーで対象経路（実際の `DreamforgeUi` の各メソッド）を
実行して確認した — 次節のとおり。実機に残るのは描画・操作感の最終確認。）

## 追補: UI 差分更新の直接実行による確認（#436 修正ラウンド2回目、2026-10-15）

上の2項目（ホバー維持の加点・二択変更）と3つの差分更新（高さの部分再計算・TextRank スキップ・
ツールチップの世代管理）を、保存・送信側と同じ方式の一度限りのシナリオランナー
（`/tmp/sod-starui-436`、リポジトリには加えない）で対象経路そのものを実行して確認した。

構成: `NativePersistence.targets` と同じ Roslyn 構文抽出タスクで、実ソースから
`RefreshStarState`・`EnsureStarTooltip`・`EnsureStarChoiceText`（`DreamforgeUi.cs`）と
`RefreshStarClusters`・`DrawStarClusterList`・`StarRegionColor`（`DreamforgeUi.AuthoredStars.cs`）を
抽出し（実装のコピーではなく構文選択）、実 `SodRpg.Core`（`StarMapPresentation`・`HeroTreeLayout`・
`Rules`・生成星図 `StarClusters.RegisterGeneratedHero("Hero_Yubar")` — 本体の起動処理と同じ登録）
に対してコンパイルした。Unity IMGUI 境界（`Event`/`EventType`/`GUIContent`/`GUIStyle`/`GUILayoutUtility`/
`GUI`）のみスタブで置き、`GUIStyle.CalcHeight` は決定的な行数換算（呼び出し記録つき、
高さの差し替えも可能）にした。UI 状態は `RebuildStarTree`/`RebuildStarClusters` と同じ値で初期化し、
加点は実際の割り振りと同じ状態遷移（`Talents` に1段、二択星は `TalentChoices` も設定）で行った。
抽出契約どおり、メンバー構成が変われば抽出が失敗する。

条件・期待結果・実際の結果（2026-10-15、全48項目合格、`ALL SCENARIOS PASSED`）。A=星A（ホバー中）、
B=星B（加点対象）、C=二択星（行5、全886行）:

| シナリオ | 操作（実メソッド） | 期待結果 | 実際の結果 |
|---|---|---|---|
| ツールチップの世代管理（R17） | `EnsureStarTooltip(A)` → もう1回 → 変化なし `RefreshStarState` → もう1回 | 生成後に `TooltipVersion` が世代に揃い、世代が同じなら再生成しない。変化がない Layout では世代も進まない | 初回生成（498文字）→ sentinel 維持 → version=1 のまま再生成なし。合格 |
| ホバー維持の加点・ダメージ注記（R7） | `EnsureStarTooltip(A)` → B へ加点 → 次の Layout で `RefreshStarState` → `EnsureStarTooltip(A)`（キャンバス末尾のホバー経路と同じ呼び出し） | 加点直後は古いまま。世代進行後の最初の `EnsureStarTooltip` でダメージ注記が使用済み1点の倍率に更新される | 加点直後は旧文のまま → ×1（使用済み0点）が ×1.003（使用済み1点）に更新。`MissingRequirements` は現行状態と一致。合格 |
| ホバー維持の加点・条件文（R7） | ポイント余裕1の状態で上と同じ操作 | 余裕が尽きた星Aの条件文（ポイント不足）と状態行（条件不足）が更新される | 「取得可能」→ 条件文「ポイント不足：必要1・残り0・あと1。」＋状態行「条件不足」。合格 |
| TextRank スキップ（R16） | A・B に sentinel 本文を仕込み、B のみ加点して `RefreshStarState`。さらに変化なし `RefreshStarState` | 加点した B のみ `RankLabel`/`DisplayBody`/`SearchDescription` が再生成され、A は sentinel のまま。検索の汚れは再生成があったときのみ立つ | B は 1/3 ラベルに再生成・A は sentinel 維持・検索汚れ=立つ→変化なしでは立たない。合格 |
| 二択変更の本文と一覧（R18） | C を選択肢0で取得し一覧を確定 → 選択肢1へ切替（無料）→ `RefreshStarState` → `EnsureStarChoiceText(C)` | 本文（`ChoiceDescription`）と効果要約が選択肢1の内容に再生成され、左一覧の行ラベルも新しい要約で再構築される。汚れは変更行からのみ（`heights[C]=0`・`dirtyFrom=行C`） | 本文・要約・一覧ラベルとも選択肢1の内容（エキゾチック物質→集まる星々）に更新・dirtyFrom=5。合格 |
| 二択変更の選択パネル（R18/R11） | 切替後に `EnsureStarChoiceText(C)` | パネル列の本文は使用済みポイントのみに依存するため再構築されない（sentinel 維持）。パネルが読む `MissingRequirements` は現行の状態。ツールチップ本文も選択肢1に再生成 | 列は sentinel のまま・`MissingRequirements` 現行と一致・ツールチップは選択肢1の本文を含む。合格 |
| 高さの部分再計算・冷間start（R15） | 初回 `DrawStarClusterList`（Repaint） | 未測定の全行の高さを測定する | 886行すべて測定（calcCalls=886）。合格 |
| 高さの部分再計算・変更行のみ（R15） | 二択変更後の `DrawStarClusterList`（行C の高さを+30px に差し替え） | 変更行だけ再測定し、変更行より前の行位置は不変、より後の行位置は高さ差分（+30）だけ移動、一覧の高さ合計も差分反映、再計算後に汚れ解除 | calcCalls=1（行Cのみ）・前行不変・後続行+30・合計も+30・dirtyFrom=886。合格 |
| 高さの部分再計算・変化なし（R15) | 追加の変更なしでもう1回 `DrawStarClusterList` | 再測定も位置計算も起きない | calcCalls=0・高さ合計不変。合格 |
| 経路取得の汚れの局所性（R15 補助） | 経路の星を取得した後、二択星 C を取得して `RefreshStarState` | この Layout で一覧の汚れが立つのは C の行だけ | dirtyFrom=5（=行C）。合格 |

実機に残るのは描画と操作感（画面上のツールチップの見え方・パネルの視覚）の確認だけで、
上記のとおり状態・文字列・行位置そのものは実コードの実行で確認済み。
