# 装備・鍛冶タブの描画キャッシュ（#500）

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

ゲームを起動できない実装環境では、プロファイラによる前後比較を実施していない。走査・文字列生成・色解析の反復をコード上で削減したが、フレーム時間やGC割り当ての改善幅は未評価とする。性能が改善したとは判定しない。

実機で評価するときは、変更前後で同じ保管庫（1000個以上）、装着状態、表示倍率、言語、画面解像度1920x1080を使い、装備タブと鍛冶タブをそれぞれ測る。初回の画像読み込みとキャッシュ構築を終え、同じスクロール操作と計測時間で、Unity ProfilerのOnGUI CPU時間、フレーム時間、GC Alloc/frameを比較する。計測器の設定とLayout/Repaintの回数も揃える。別途、装着・分解・取引・強化・星の変更・言語切り替え後の表示更新を確認する。これらの実機計測・目視確認は今回の依頼では対象外であり、新たな確認用Issueは作らない。

### 計測の仕組み（R9）

採用の判断を実測で行えるよう、装備タブと鍛冶タブの描画を個別に測る `TabPerf` を追加した。

- `DreamforgeUi.Window.cs` の `DrawWindow` が `DrawGearTab` と `DrawForgeTab` の前後で `TabPerf.Begin` と `TabPerf.End` を呼ぶ。描画1回の時間（ms）と、メインスレッドのGC割り当て量（`GC.GetAllocatedBytesForCurrentThread` の差分）を、タブごと・Repaintとそれ以外（Layout・入力）ごとに集計する。
- 計測は `dreamforge_perf` を一度実行するか、保存先に `perf.flag` があるときだけ動く。それ以外は真偽値の確認だけで、割り当ては増えない。割り当て量を読めない環境では1回の失敗で止め、`alloc n/a` と表示する。
- 結果は `dreamforge_perf` の出力と `perf.flag` の10秒ごとのログで、既存の報告の後ろに `| gear tab repaint avg …ms max …ms alloc …KB/call (n calls) layout/input …` の形で出る。報告するたびに集計を空にする。
- 判断の基準：変更前（このキャッシュ導入前の版）と変更後で、上の条件をそろえて `gear tab` と `forge tab` の Repaint の平均時間と `KB/call` を比べる。どちらも悪化していなければ採用を維持する。どちらかが悪化した場合は、悪化した側のキャッシュを見直す。

## 検証

- ゲーム参照DLLによるReleaseビルド成功（エラー0、警告6）。参照用ゲームディレクトリに `Mods` がないため配置処理なし。
- 既存の `RelicRowCacheTests`、`CoopTradeClientTests`、`StarClusterDiffUpdateTests`：23件成功。
- 既存の `RelicIconKeysTests`、`RelicFusionTests`、`AllocationRefundUiTests`：19件成功。
- 新しいテストは作成していない。既存テストで使う実ソース抽出の対象メンバーだけ更新した。
- 未実行：ゲーム内の表示確認・破棄済みテクスチャの実機再取得・プロファイラ比較（この環境ではゲームを起動できないため）、全件／低速テスト（実装工程では実行しない方針のため）。
