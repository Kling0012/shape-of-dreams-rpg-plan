# Issue #1050 再現記録

日本語で起動して記録タブを開いた初回の表示で、自動分解の選択肢ラベルが作られず、「自動分解：」の後ろが空欄になることをテストで再現した。製品コードは変更していない。

## 手法

`src/SodRpg.Mod/DreamforgeUi.cs` から `AutoSalvageChoices` と `DrawAutoSalvageSettings` の2メソッド、`_autoSalvageLabelLanguage` と `_autoSalvageChoiceLabels` の2フィールドを、NativePersistence.targets の抽出（実ソースを構文ごと取り出す仕組み）でテスト アセンブリに組み込んだ。描いた文字列は ProfileSlotUiBoundary.cs の GUILayout 代役が記録する。設定は NativeEmbracePositionBoundary.cs の DreamforgeConfig 模倣へ `autoSalvageRarity` / `toastMaxLines` を足して間に合わせた。テストは `tests/Issue73.Native.Tests/Issue1050AutoSalvageLabelReproTests.cs` の3件で、次を確かめる。

- 日本語で初回描画: 設定値5種のそれぞれで、現在値（「しない」など）が「自動分解：」の後ろに出ること。
- 初回呼び出しの配列: 日本語・英語・中国語のそれぞれで、戻り値の5要素すべてが空でないこと。
- 言語切り替え: 日本語で1度描いた後に英語へ切り替えると、新しい言語で現在値が出ること（修正後も残すべき動作）。

## 実行環境

2026-10-22、Linux、ブランチ `ai/issue-1050`、基点 `c337beca` で実行した。SDKは10.0.111、ランタイムは Microsoft.NETCore.App 10.0.11。テストプロジェクトはnet8.0のため `DOTNET_ROLL_FORWARD=Major` を指定した。NuGetキャッシュは環境変数 NUGET_PACKAGES の worktree 外のパスを使用した。

## 再現コマンド

リポジトリ直下から実行する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter "FullyQualifiedName~Issue1050" --nologo
```

## 修正前の結果

| 対象 | 終了コード | 結果 |
| --- | --- | --- |
| Issue1050 再現テスト9ケース | 1 | 6件失敗、3件成功 |

日本語の初回描画は5ケースすべて失敗した。GUILayout 代役が記録した文字列は `自動分解と通知`, `自動分解：`, `−`, `+`, `通知の行数：7` … で、選択値の文言がない。`choices[salvageChoice]` が null のまま文字列補間に入るためである。日本語の初回呼び出しでは、`AutoSalvageChoices` の戻り値の5要素すべてが null だった。

英語と中国語の初回呼び出し、および言語切り替え後の英語表示は成功した。これは Issue の分析と一致する。フィールド `_autoSalvageLabelLanguage` の既定値0と `LocLanguage.Japanese` の列挙値0が一致するため、日本語では初回呼び出しで生成分岐（`_autoSalvageLabelLanguage != Loc.Language`）に入らず、英語・中国語では入る。

実行ログは `.ci-logs/issue1050/native-before-fix.log` に保存した。このディレクトリはgitの対象外で、再実行時は上のコマンドで結果を作り直せる。

## 次工程への引き継ぎ

- 原因の場所: `src/SodRpg.Mod/DreamforgeUi.cs` の `_autoSalvageLabelLanguage` と `AutoSalvageChoices`（L3946-3960）。フィールドは初期化されないまま0（= Japanese）で、言語が日本語のとき初回の条件判定が偽になる。
- 修正は生成分岐へ「ラベル未初期化」の判定（初期化フラグか配列要素の null チェック）を足す形になる。言語が変わったときの作り直しは残すこと。切り替えのテストは修正前から成功しているので、修正後に全9ケースが通ればよい。
- 保存値と分解の動作には影響しない見込み。今回の変更後も、ClientSession 側の抽出を使う AutoSalvageGraceTests は成功している。

## 未実行

- 実機（Unity）での画面確認。日本語・英語・中国語での起動、＋／− の操作、起動後の言語切り替え。ゲームを起動できないため、抽出した実ソースとスタブによる検証にとどめた。
- MOD 全体のビルドと全テスト。対象を Issue1050 の9ケースと、触ったスタブを共有する既存テスト（Issue771・ProfileSlot・Embrace・AutoSalvageGrace のフィルタで26件）に絞った。
