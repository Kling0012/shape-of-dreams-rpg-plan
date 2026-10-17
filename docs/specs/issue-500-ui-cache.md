# 装備・鍛冶タブの描画キャッシュ（#500）

## 要件ごとの実装と今回の差分

PR #671・#678 でキャッシュ本体と計測切り替えはすでに main に取り込まれている。PR #682 のベースはそれらを含むため、今回の差分はゲーム外の計測ツールと、この仕様書の計測結果・要件対応の記録になる。前ラウンドで `requirements_done` に R10 だけを記載したことによる未実装判定を、実装箇所を確認したうえで訂正する。

| 要件 | 実装箇所 | コード上の確認結果 |
|---|---|---|
| R1 | `src/SodRpg.Mod/DreamforgeUi.cs` の `EnsureStashCache`・`SlotTabLabel`・`SortedCached` | 並べ替え済み一覧と同じ世代で保管庫を1回数え、6枠の件数とラベルを保持する。通常のタブ描画は保持したラベルを返す。 |
| R2 | `src/SodRpg.Mod/RelicIcons.cs` の `For(string, string)`・`Draw` | `uniqueId ?? baseId` で解決済みテクスチャを検索し、ヒット時には候補反復子も `uniques/` の文字列も作らない。 |
| R3 | `src/SodRpg.Mod/RelicIcons.cs` の `FrameColor` | レア度ごとの辞書を参照し、初回だけ `ColorUtility.TryParseHtmlString` を呼ぶ。 |
| R7 | `src/SodRpg.Mod/ClientSession.cs` の `DisplayBuild`・`MarkDirty`・`MarkAllocationDirty`、`DreamforgeUi.GearColumn.cs` の `DrawGearColumn` | 計算時刻から0.25秒未満だけ再利用し、装着や星変更の通知時に失効する。詳しい操作経路は下記。 |
| R8 | `src/SodRpg.Mod/RelicIcons.cs` の `RelicCache`・`For(string, string)` | R2と同じテクスチャキャッシュ。 |
| R9 | `src/SodRpg.Mod/RelicIcons.cs` の `FrameColors`・`FrameColor` | R3と同じレア度別の色キャッシュ。 |
| R10 | `tools/UiCacheBench/Program.cs` の `Bench.Run`・`Main`、`src/SodRpg.Mod/TabPerf.cs` | ゲーム外でCPU時間とGC割り当てを比較して採否を判断する。ゲーム内の実測用には既存のタブ別計測器と切り替えを使う。測定対象と限界は下記。 |
| R11 | `src/SodRpg.Mod/ClientSession.cs` の `DisplayBuild`・`MarkDirty`・`MarkAllocationDirty` | R7と同じ表示キャッシュと即時失効。 |

## 実装

- `DreamforgeUi.EnsureStashCache` は、並べ替え済み一覧と共通の0.3秒の更新周期で保管庫を1回走査し、6枠の個数とタブの文字列を保持する。`SlotTabLabel` は保持した文字列を返す。検索や選択枠によらず、個数は保管庫全体を対象にする。
- `ClientSession.MarkDirty` の `ViewRevision`、保存リビジョン、プロフィール参照、保管庫の件数、日本語／英語の変更でも更新する。保存前の装着・分解・強化操作は `MarkDirty`、取引の確定は保存リビジョンと既存の `ProfileChanged` 経路で更新される。一覧の行文字列も変更通知時に無効化する。
- `RelicIcons.For(uniqueId, baseId)` は `uniqueId ?? baseId` を鍵に解決済みテクスチャを保持する。候補列挙と `uniques/` の連結は初回解決時だけ行う。固有IDが変われば別の鍵になる。画像なしの実際のnullは保持し、Unityが破棄したテクスチャ参照はファイル側のキャッシュからも読み直す。読み込み元の変更と `Dispose` でキャッシュを空にする。
- `RelicIcons.FrameColor` はレア度ごとに色を保持する。セット品の橙色と空枠の灰色も従来どおり使う。言語や表示設定は画像・色の入力ではなく、固有ID・レア度・セット判定は描画時の遺物から取得する。

## 強さ表示の更新経路（R7）

次の既存実装で要件を満たしているため、計算や送信の方式は変更していない。

- `DreamforgeUi.GearColumn.cs` の `DrawGearColumn` は `ClientSession.DisplayBuild` を使う。
- `ClientSession.cs` の `DisplayBuild` は、同じ旅人の計算結果を実際の計算時刻から0.25秒未満だけ使い回す。
- `DreamforgeUi.GearDetail.cs` の `DrawGearDetail` は装着・取り外し後に `MarkDirty(true)` を呼ぶ。
- `DreamforgeUi.cs` の星の割り振り・払い戻し、`DreamforgeUi.AuthoredStars.cs` の `DrawStarChoicePicker` は `MarkAllocationDirty` を呼ぶ。同メソッドは `MarkDirty(true)` を呼ぶ。
- `DreamforgeUi.StarLoadouts.cs` のロードアウト適用と `DreamforgeUi.AllocationRefund.cs` の割り振り払い戻し承認も `MarkDirty(true)` を呼ぶ。
- `MarkDirty(true)` は `_buildCacheFrame = -1` にするため、同じフレーム内でも次の表示要求は `CurrentBuild` で再計算する。星の保存・送信の遅延は表示キャッシュを延命しない。

## 性能評価（R10）

### ゲーム外での計測と判断

ゲームを起動できない実装環境でも採否を実測で決めるため、`tools/UiCacheBench` で キャッシュあり／なし を同じ条件で測った。`SlotTabLabel`／`EnsureStashCache`、`RelicIcons.For(uniqueId, baseId)`、`RelicIcons.FrameColor` の処理を Core の実データ（`Content.Bases`・`Content.Uniques`・`Relic.Slot`・`RelicIconKeys.Preference`・`Content.SlotName`）でなぞり、OnGUI イベント1回ごとに CPU 時間（`Stopwatch`）とメインスレッドの GC 割り当て（`TabPerf` と同じ `GC.GetAllocatedBytesForCurrentThread`）を記録する。条件は、保管庫1200個（固有品15%）、イベント1回につきタブ6個・アイコン26個（一覧の20行と装備欄の6個）・枠色26回、60fpsでLayoutとRepaintの2回、スクロール中を想定して見える行を少しずつずらす、とした。Unity の描画・テクスチャ・`ColorUtility` は含まない（枠色の解析は同じ `#rrggbb` の読み取りで代えた）。

```
dotnet run -c Release --project tools/UiCacheBench -- 1200 200000 3
```

2026-10-17、.NET 10.0.11（Linux）での結果（3往復の平均、イベント1回あたり）：

| 処理 | キャッシュなし | キャッシュあり |
|---|---|---|
| スロットタブ（6個） | 275.2µs・720B | 1.3µs・52B |
| アイコンの解決（26個） | 3.3µs・1983B | 0.7µs・0B |
| 枠色（26回） | 2.0µs・1040B | 0.5µs・0B |
| 1フレーム（イベント2回）の合計 | 560.9µs・7.31KB | 5.2µs・0.10KB |

3往復とも同じ傾向で、どの処理もキャッシュありで時間・割り当てが悪化していない。判断の基準（悪化していなければ採用を維持）に従い、3つのキャッシュはすべて維持する。効果の大半はスロットタブで、保管庫全体を6回数える処理が0.3秒に1回になったことによる。アイコンと枠色は1回あたりの差は小さいが、描画ごとの割り当てがなくなる。

Mono（ゲームの実行環境）と Unity の描画を含めた絶対値はこの計測では分からない。実機の値は下の手順で取る。

実機で評価するときは、変更前後で同じ保管庫（1000個以上）、装着状態、表示倍率、言語、画面解像度1920x1080を使い、装備タブと鍛冶タブをそれぞれ測る。初回の画像読み込みとキャッシュ構築を終え、同じスクロール操作と計測時間で、Unity ProfilerのOnGUI CPU時間、フレーム時間、GC Alloc/frameを比較する。計測器の設定とLayout/Repaintの回数も揃える。別途、装着・分解・取引・強化・星の変更・言語切り替え後の表示更新を確認する。これらの実機計測・目視確認は今回の依頼では対象外であり、新たな確認用Issueは作らない。

### 計測の仕組み（R10）

採用の判断を実測で行えるよう、装備タブと鍛冶タブの描画を個別に測る `TabPerf` を追加した。

- `DreamforgeUi.Window.cs` の `DrawWindow` が `DrawGearTab` と `DrawForgeTab` の前後で `TabPerf.Begin` と `TabPerf.End` を呼ぶ。描画1回の時間（ms）と、メインスレッドのGC割り当て量（`GC.GetAllocatedBytesForCurrentThread` の差分）を、タブごと・Repaintとそれ以外（Layout・入力）ごとに集計する。
- 計測は `dreamforge_perf` を一度実行するか、保存先に `perf.flag` があるときだけ動く。それ以外は真偽値の確認だけで、割り当ては増えない。割り当て量を読めない環境では1回の失敗で止め、`alloc n/a` と表示する。
- 結果は `dreamforge_perf` の出力と `perf.flag` の10秒ごとのログで、既存の報告の後ろに `| gear tab repaint avg …ms max …ms alloc …KB/call (n calls) layout/input …` の形で出る。報告するたびに集計を空にする。
- 比較用の切り替え：`dreamforge_perf_nocache` を実行するたびに、#500 の描画キャッシュ（スロット別の個数とタブ文字列、`(uniqueId ?? baseId)` ごとのアイコン、レア度ごとの枠色）を使わない状態と使う状態が入れ替わる。使わない状態では導入前と同じく、描画のたびに保管庫を数え直し、`RelicIconKeys.Preference` で探し直し、`ColorUtility.TryParseHtmlString` で色を読み直す。切り替え時に集計を捨てるので、次の報告は新しい状態だけの値になる。報告の末尾に `| ui cache=on` または `| ui cache=off` が付く。既定はキャッシュを使う。ファイル単位の画像キャッシュ（`icons/*.png` の読み込み結果）は導入前からあるため、どちらの状態でも使う。
- 判断の手順：同じビルド・同じ条件で、`dreamforge_perf` を一度実行して集計を空にし、同じスクロール操作をしてから `dreamforge_perf` で `ui cache=on` の値を取る。`dreamforge_perf_nocache` で切り替えて同じ操作をし、`ui cache=off` の値を取る。これを2回以上入れ替えて繰り返す。
- 判断の基準：キャッシュありとなし（または導入前の版と現在の版）で、上の条件をそろえて `gear tab` と `forge tab` の Repaint の平均時間と `KB/call` を比べる。どちらも悪化していなければ採用を維持する。どちらかが悪化した場合は、悪化した側のキャッシュを見直す。

## 検証

- ゲーム参照DLLによるReleaseビルド成功（エラー0、警告6）。参照用ゲームディレクトリに `Mods` がないため配置処理なし。
- 既存の `RelicRowCacheTests`、`CoopTradeClientTests`、`StarClusterDiffUpdateTests`：23件成功。
- 既存の `RelicIconKeysTests`、`RelicFusionTests`、`AllocationRefundUiTests`：19件成功。
- 新しいテストは作成していない。既存テストで使う実ソース抽出の対象メンバーだけ更新した。
- `tools/UiCacheBench`（Release、保管庫1200個、20万イベント×3往復）：キャッシュありでどの処理も悪化なし。結果は「ゲーム外での計測と判断」の表。
- 未実行：ゲーム内の表示確認・破棄済みテクスチャの実機再取得・Unity Profiler での実機比較（この環境ではゲームを起動できないため）、全件／低速テスト（実装工程では実行しない方針のため）。

### PR #682 修正ラウンドでの再検証（2026-10-17）

- `DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~RelicRowCacheTests|FullyQualifiedName~CoopTradeClientTests|FullyQualifiedName~StarClusterDiffUpdateTests'`：23件成功、失敗0。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~RelicIconKeysTests|FullyQualifiedName~RelicFusionTests|FullyQualifiedName~AllocationRefundUiTests'`：19件成功、失敗0。
- `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release '-p:GameDir=/home/wang/dev/sod-gamedata/Shape of Dreams'`：エラー0、警告6。参照用ゲームディレクトリに `Mods` がなく、配置処理は走っていない。
- `dotnet run -c Release --project tools/UiCacheBench -- 1200 20000 3`：3往復の平均は、イベント1回あたりスロットタブ277.300→1.466µs・720→53B、アイコン3.407→0.769µs・1986→3B、枠色2.167→1.550µs・1040→0B。合計はイベント2回あたり565.746→7.569µs・7.32→0.11KB、`decision: keep`。初回の色キャッシュ計測は3.724µsで非キャッシュの2.258µsより遅く、短い計測では初期化・実行環境の揺れが残る。各往復が必ず速くなるという判断はせず、既存の20万イベントの結果と今回の平均を根拠に採用を維持する。
- 上の既存テストは、一覧行の更新、取引、星図の差分更新、アイコン候補、融合、割り振り払い戻しを検証する。`DisplayBuild` の0.25秒境界と装着・星変更の即時失効、Unityの破棄済みテクスチャ再取得を直接実行する既存テストはなく、該当経路はコードで確認した。新しいテストは作成していない。
- 全件／低速テスト、ゲーム内表示、Unity Profiler比較、前ラウンドの20万イベント×3往復の再実行は未実行。今回の再計測は対象を絞った2万イベント×3往復であり、Unityを含む効果の実測値とは扱わない。
