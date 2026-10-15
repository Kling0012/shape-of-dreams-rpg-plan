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
  シーン/終了境界と送信待ちの組み合わせはコード確認のみ。

## 差分更新への直接回帰テスト（#440）

上記「内部動作は実機確認リストに残っている」差分更新3点を、Issue73抽出ハーネスで直接検証した
（`tests/Issue73.Native.Tests/StarClusterDiffUpdateTests.cs`、5件）。

- `NativePersistence.targets` が `RefreshStarState` / `RefreshStarClusters` / `EnsureStarTooltip` /
  `EnsureStarChoiceText` / `DrawStarClusterList`（と `StarRegionColor`）を実ソースから抽出する。
  境界は `NativeBoundary.cs` の Unity IMGUI スタブ（決定論的な `GUIStyle.CalcHeight`、`Event.current`、
  `GUIContent`、呼び出し計数）と `StarClusterUiBoundary.cs` の `StarNode`・`_star*` 状態。
- 左一覧: 加点後に高さ計測が変更行だけ（CalcHeight 1回）で、`_starClusterDirtyFrom` から位置を
  加算し直すこと、未変更行の高さ・位置・ラベルが保たれること、幅変更時は全行を再計測すること。
- 本文: 変更のない Layout では世代も本文も動かず、加点時に変わった星だけ `DisplayBody` が
  新しいインスタンスに作り直され、未変更星は同じインスタンスのまま残ること（TextRank/TextChoice）。
- ツールチップ: 同一世代では再生成せず（同じ文字列インスタンス）、加点で世代が進むと条件文と
  ダメージ注記が再生成されること（TooltipVersion/_starTextVersion）。
- 二択: 取得で本文と両候補が作り直され、無料切り替えでも使用済みポイントが変わらずとも
  世代が進むので本文・両候補・ツールチップが作り直されること（EnsureStarChoiceText/ChoiceTextVersion）。
  これに伴い `EnsureStarChoiceText` のキャッシュ判定を使用済みポイント基準（ChoicePoints）から
  `_starTextVersion` 世代基準（ChoiceTextVersion、TooltipVersion と同じ設計）に変えた
  （#440 レビュー指摘。無料切り替えでも候補が再生成されるように）。
- 壊し確認: 世代判定（TextRank/TextChoice と TooltipVersion）の削除、`_starClusterDirtyFrom`
  追跡の削除で、対応するテストが失敗することを確認してから元に戻した。ChoiceTextVersion の
  世代判定を旧来の使用済みポイント基準に戻しても `Choice_change_...` が失敗することを確認した。
