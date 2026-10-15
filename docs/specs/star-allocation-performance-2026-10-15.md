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
  二択の候補本文も表示時に生成し、同じ更新世代の間は再利用する（#440 で使用済みポイント基準から変更）。
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

- 指定 Mod Release ビルド: 0エラー（コンパイルを伴う完全ビルドでは既存警告6件）。
  後日の増分ビルドの警告0件は再コンパイルを省略した結果であり、警告の解消を示さない。
  条件別の実行記録は[作業ログ](../worklogs/star-allocation-performance-review-2026-10-16.md)に保存する。
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

## 差分更新への直接回帰テストと保守契約

`tests/Issue73.Native.Tests/StarClusterDiffUpdateTests.cs` の既存5件で、次を直接検証する。
実ソースのメソッド・`StarNode`・依存フィールドは `NativePersistence.targets` が構文で抽出し、
Unity IMGUI と再構築などの周辺処理のみ境界に置く。

- 左一覧: 変更行だけ高さを測り、最初の変更行から位置を再計算する。未変更行の高さ・ラベルを維持し、幅変更時は全行を測り直す。
- 本文: `TextRank` / `TextChoice` が変わった星だけ本文を再生成する。
- ツールチップ: 同一世代では再利用し、加点後は条件文と使用済みポイント依存のダメージ注記を再生成する。
- 二択: 取得と無料切り替えで本文・両候補・ツールチップを再生成する。候補のキャッシュ基準は使用済みポイントではなく `ChoiceTextVersion` / `_starTextVersion`。
- 言語: 各ケースは日本語を明示し、高さ差を必要とするケースのみ英語に切り替え、終了時に元の言語へ戻す。

境界が直接読む `_starTextVersion`・`_starClusterHeights`・`_starHelpStyle`・
`_starCapFingerprint`・`_starAuthoredFingerprint` も `Fields` に明示する。
同時宣言による偶然の抽出に依存せず、個々の改名・削除を抽出フィールド契約エラーで検知する。
`StarNode` は最初の型宣言だけを `internal` に置換し、入れ子の `private` クラスは維持する。

フィクスチャの冒頭で `Hero_Cetus` の木に次の前提があることを検証し、崩れた場合は
バランスデータと検証対象の見直しを促すメッセージで失敗する。

- 始まりから2歩目に、1歩目の普通の星と隣接する普通の星がある（刻印・二択・橋を除く）。
- 本文の更新・維持を比較する二択の星が2つ以上ある。

### 初期化ヘルパーのずれの扱い

`SeedStarTree` / `SeedStarNavigation` は `RebuildStarTree` / `RebuildStarClusters` の
初期化を一部手書きで再現しており、初期化式の変更を自動検知する契約はまだない。
`_starCapFingerprint` と `_starAuthoredFingerprint` は登録内容の変更を検知する値であり、
初期化式の指紋ではない。フィールド名の変更は上の抽出契約で検知できるが、代入式の変更は検知しない。

ずれの自動検知には、Roslyn で対応する代入式と `StarNode` 初期化子を抽出し、
引数名などを正規化して構文比較する方法が適する。メソッド全体の比較は、境界で省く
Icon・座標・スタイルや、テストで差し替える一覧の行選択の違いまで検知するため採用しない。
この保守上の制限と比較箇所は境界のコメントにも記録する。
自動検知を導入するまでは、初期化を変更するときに両ヘルパーの対応式を併せて点検する。

## マージ後の実機確認リスト（優先・未実行）

Unity 実ゲームを起動できる環境がないため、以下は未実行。抽出メソッドのテストは
実際のフォント計測・描画・マウス入力・フレーム時間の確認を代替しない。
後段の動作確認で次を優先する。

- [ ] 1920x1080・既定倍率で、星図全体・左一覧・ツールチップ・二択パネルが画面に収まり、重なりや切れがないか、スクリーンショットでレイアウトを確認する。
- [ ] マウスのホバー、左クリック加点、右クリック返却、ドラッグ、ホイール、一覧からの移動が表示位置と一致することを確認する。
- [ ] ホバーを維持して別の星を加点し、条件文とダメージ注記が次の表示で更新されることを確認する。
- [ ] 二択パネルを開いて取得・無料切り替えを行い、本文・候補2つ・ツールチップ・左一覧の見た目が更新されることを確認する。
- [ ] 100・300・450ポイント使用時の加点・返却・二択切り替え・連打で、レイアウト処理とフレーム時間、GC による停止を計測する。`dreamforge_perf` と Unity 側の計測を使い、Core の近似ベンチマークと区別する。

過去の修正ラウンド・検証コマンド・結果 JSON の対応記録は
[作業ログ](../worklogs/star-allocation-performance-review-2026-10-16.md)に保存する。
