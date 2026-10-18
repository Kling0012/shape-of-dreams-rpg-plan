# PR #518 テスト用スタブの差分確認

修正ラウンドの F1 は`tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs` に含まれる別件のスタブ変更を対象としている。
PR のコメントと全差分を取得し、ベース `071da86d` と作業開始時の HEAD `091eade1` を比較した。

`git diff --exit-code 071da86d HEAD -- tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs` は差分なし、終了コード0だった。
指摘されたスタブ変更は現在の PR 差分に含まれていないため、このファイルを変更しなかった。
ベース側のスタブを過去の状態へ戻すと、今回の修正とは無関係な変更になる。
PR 本文の「#517 と同じビルド修正も含む」という説明は、現在の差分に合わないため外す。

マージ後の最終差分も確認した。`git diff --exit-code 300afa70^1 300afa70^2 -- tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs` と `git diff --exit-code 300afa70^1 300afa70 -- tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs` はともに終了コード0で、最終ブランチとマージ結果のどちらにも同ファイルの差分はなかった。

このラウンドで追加したのは本記録のみ。本体コードと既存テストは変更していない。
PR に残る変更は討伐受領時の識別子切り替え、本体の再開読み込みを放棄した後の報酬停止解除、その回帰テストと日英の更新履歴である。

## 実行した検証

- `gh pr view 518 -R Kling0012/shape-of-dreams-rpg-plan --comments` と `gh pr diff 518 -R Kling0012/shape-of-dreams-rpg-plan`: コメントと5ファイルの差分を確認した。`StartupGameApi.cs` は含まれていなかった。
- `git diff --exit-code 071da86d HEAD -- tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs`: 終了コード0、差分なし。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests -c Release --nologo --filter 'FullyQualifiedName~ContinueSaveTests|FullyQualifiedName~V272ProgressionTests' --verbosity quiet`: 21件成功、失敗0、スキップ0。テスト用スタブなどの未使用フィールドに関するコンパイル警告が出た。
- `dotnet build tests/SodRpg.Mod.Startup.Tests -c Release --nologo --verbosity quiet -p:WarningLevel=0`: 終了コード0、エラー0。コンパイラ警告の表示を抑制して実行した。

## 未実行

- フルテスト、`tools/test_changed.py --all`、`SODRPG_SLOW=1`、対象外のテスト: 関連部分だけを確認する作業指示に従い、後続の検証工程に委ねる。
- 修正前の本体コードへの一時差し戻し: このラウンドでは本体コードを変更していないため実施していない。過去の PR 本文にある修正前の失敗確認は、今回の実行結果には含めない。
- ゲーム内での協力通信、潜行・帰還、タイトル復帰後の報酬: この環境ではゲームを起動できないため未確認。
