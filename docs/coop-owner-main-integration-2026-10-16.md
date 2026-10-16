# 担当制・共鳴の盾・自動詠唱の統合（2026-10-16）

対象: `/home/wang/dev/sod-owner`、`feat/coop-enemy-ownership-1016`。
依頼: `/home/wang/.claude/work/herobal/owner-merge.txt`。

## 取り込みと衝突解消

`git merge --no-commit origin/main` で、実行時の main `aa132898` を取り込んだ。マージコミットは `69e15fcd`。
`Coop.Generated.cs` は `python3 tools/balance/gen_cs.py` で再生成し、設定は担当制・動的難易度・共鳴の盾・近接協力の両方の変更を保持した。
`NativeBoundary.cs` は担当制の HP 再計算・ダメージ境界に、盾の免疫・戻り値・生成時刻を統合した。テストの追加・期待値の変更はしていない。
CHANGELOG は取り込んだ main の内容のままで、この作業の追記はしていない。Patches.cs の CRLF も維持した。

受ダメージの Postfix は、仕掛けダメージの確定 → 動的難易度の測定 → 共鳴の盾 → 担当制・ボス持ち分・流れ弾の順。
測定は盾・手伝い倍率・下限より前に一度だけ行う。直接の既存呼び出しでは従来の測定入口も維持する。
頭上表示は、担当の印 → 盾 → 名札を横に並べ、同じ敵の重複描画を避ける。64体・60m・画面内という共通上限を使い、宿敵のシアンも保持する。

## 統合処理

- `HostAuthority.EnemyOwnership.cs`: `CoopOmens.IsSharedObjective` が true の敵には担当を付けない。既存の担当がある敵へ盾が付いた場合は、`RefreshSharedObjectiveOwnership` が担当を外し、`ChangeOwnership` により HP 割合を保って部屋の圧へ戻す。担当人数の集計、保護、流れ弾、再割り当て、同期も共有扱いにする。
- `CoopOmens.cs`: 盾の選抜・交換・解除から上記の更新を呼ぶ。既存の 0.25 秒走査でも状態を補完する。毎フレームの割り当ては追加していない。
- `HostAuthority.AutoCastTargets.cs` → `HostAuthority.EnemyOwnership.cs`: partial フックで担当優先を追加する。自分の担当は 0、ほかの有効な優先条件がない保護中の他人の担当は 2。自分の直近命中・自分を狙う敵・既存の味方だけの直近命中の判定を引き継ぐ。担当制 OFF・ソロは既存の順位をそのまま返す。
- `EnemyOwnershipBoundary.cs`: AutoCastTargets を直接リンクしない既存 Native テスト用に partial の定義宣言を追加した。

保存形式・Protocol は変更していない。失敗時は各機能の既存のフォールバックを使う。

## 実行した検証

すべて前景で実行。ゲーム参照は `/home/wang/dev/sod-gamedata/Shape of Dreams`、MOD のビルド配置先は `/tmp/sod-owner-merge`。

| 検証 | 結果 |
| --- | --- |
| `python3 tools/balance/gen_cs.py` | 成功、Coop.Generated.cs を再生成 |
| `python3 tools/balance/gen_cs.py --check` | 成功、生成物と入力の一致をキャッシュで確認 |
| `dotnet build src/SodRpg.Mod -c Release -p:GameDir=… -p:ModDeployDir=/tmp/sod-owner-merge` | 0エラー・6警告 |
| `DOTNET_ROLL_FORWARD=LatestMajor dotnet build tests/Issue73.Native.Tests -c Release -p:GameDir=…` | 0エラー・114警告 |
| 同 Startup.Tests の Release ビルド | 0エラー・39警告 |
| `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests -c Release --no-build` | 236成功・失敗0・スキップ0 |
| 同 Startup.Tests | 147成功・失敗0・既存スキップ1 |
| `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Mod.AutoCast.Tests -c Release` | 19成功・失敗0・スキップ0 |
| `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py --base HEAD`（統合変更をコミットする前、HEAD=マージコミット） | 2050成功・失敗0。既存Slow系4件・性能計測3件はスキップ |
| `git diff --check` | 成功 |

Startup の既存スキップは `SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch`。Release の JIT inlining により Harmony パッチを観測できない旨の既存指定による。
`test_changed.py` の FxBudget 選択は該当テストなし。NativeApi・issue522 はテストアセンブリではないため成功件数には含めない。

## 未実行・未確認

- 比較元指定なしの `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py` の本実行: `--list` で担当制ブランチ全体の csproj・balance 差分から全件実行になることを確認したため、全体・Slow スイートを実行しない standing rule に従い、今回の統合差分を `--base HEAD` で検証した。全体テストはパイプラインへ残す。
- Slow・性能スイート: 上記指定と既存の実行条件によるスキップ。
- 実ゲームの二人プレイ、実際の自動詠唱による担当優先、盾が付く前後の HP・ダメージ、頭上表示の描画とスクリーンショット: この環境のゲームディレクトリには参照アセンブリのある `Shape of Dreams_Data` のみで、実行ファイルがないため未実行。既存テストの成功をこれらの実機確認の代わりとはしていない。
- 新規の統合テスト: テストを書かない指定に従い未作成。既存テストは各機能と従来動作の回帰確認であり、今回追加した組み合わせを直接再現する保証はない。
- 作業中に別セッションで origin/main が先へ進んだ。今回の取り込み対象は merge 実行時の `aa132898`。以後の main の変更は取り込んでいない。

push・公開・削除・共有記憶の保存は実行していない。
