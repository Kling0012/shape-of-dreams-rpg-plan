# Wiki 原稿と更新範囲

Issue #818 の GitHub Wiki 用 Markdown 原稿です。利用者向けのページは `Home.md` からたどれます。`_Sidebar.md` は Wiki のナビゲーションです。この README は更新の根拠と公開時の扱いを記録するもので、Wiki のページには含めません。

## 対象と確認結果

2026-10-19 に既定ブランチ `dev` と既存の公開用ブランチ `wiki-pages` を照合しました。作業開始時の `dev` は `e3c0a212`、追加確認時は `448e729b` です。途中で入った変更は取引警告文の再利用と遠征単位の世界観測などであり、今回のガイドに載せた手順・条件・数値を変えていません。

既存の `tools/WikiGen` は DokuWiki 用の `.txt` を作り、`wiki-pages` には v3.8.4 の装備・星図一覧があります。取得時の `SOURCE_COMMIT` は `2683f1d2e13e1fcb904ee9d3de7378e465941c67` でした。GitHub Wiki 用の Markdown とは形式が異なるため、一覧を一括変換せず、利用者向けのガイドを別の原稿として追加しました。

GitHub の設定では Wiki は有効ですが、認証付きの `git ls-remote` でも `.wiki.git` は `Repository not found` でした。GitHub Wiki の既存ページ本文を比較できたとは扱っていません。今回の不足は取得できた `wiki-pages` のページ一覧と現行コードを比較して選びました。

## 不足・古い説明と対応ページ

| 確認した不足・古い説明 | 対応 |
| --- | --- |
| 既存の入口は装備一覧が中心で、導入や機能ガイドへの案内がない | `Home.md` と `_Sidebar.md` にガイドへの導線を追加 |
| 導入・更新・操作・確保と潜行・鞄あふれのまとまった案内がない | `Getting-Started.md` を追加 |
| `basics.txt` の再調律が一律最大3回で、禁忌の職人による追加3回を説明していない | `Relics-and-Forge.md` に条件・例外・消費時機を記載し、`tools/WikiGen/Program.cs` の基本ページ本文と追加費用表を更新 |
| 覚醒の対象を「装備を使うと」とだけ記し、装着中の伝説の撃破成長だと分かりにくい | `Relics-and-Forge.md` と基本ページ生成元で対象と条件を明記 |
| 融合と禁忌化の案内がない | `Relics-and-Forge.md` に対象・費用・上限・できない操作を追加 |
| 星図の個別データはあるが、現行の経験ポイント上限や5ロードアウトの使い方がまとまっていない | `Star-Map.md` に旅人一覧・最大500経験ポイント・図鑑加算・刻印枠・共有範囲を記載 |
| インフィニティの開始・リンボ解禁・周期・帰還・協力時の選択のページがない | `Infinity.md` を追加 |
| 協力取引の操作・提示条件・未確定時の案内がない | `Co-op-Trade.md` を追加。旧資料の「120秒無操作」をそのまま転記せず、現行ホスト処理の条件を反映 |
| 中断・Continue・ロビー帰還・中断した鞄の受け取りのページがない | `Saves-and-Continue.md` を追加 |

各ガイド末尾の Sources に、コード・数値の原本・導入文書への参照があります。履歴を含む MOD README は古い数値も残るため、星図・鍛冶・融合・禁忌化・インフィニティの数値は現行の Core と `tools/balance/*.json` を優先しました。

## 生成と公開の扱い

DokuWiki の既存ページは従来のコマンドで再生成できます。出力先には worktree 外の一時ディレクトリなどを指定します。

```sh
DOTNET_ROLL_FORWARD=Major dotnet run -c Release --project tools/WikiGen -- <出力先>
```

GitHub Wiki へ反映する原稿は、このフォルダの `README.md` を除く Markdown 8ファイルです。ページ間リンクは GitHub Wiki のページ名に合わせています。既存ページと名前が重なる場合は、取得した本文を残して差分を反映する必要があります。今回、GitHub Wiki のページ削除・リネーム・上書き・push・公開は行っていません。公開用ブランチや CI 設定も変更していません。

既存の DokuWiki 生成器は基本説明の訂正だけに変更を絞りました。新しいガイドを DokuWiki に公開したり、GitHub Wiki の公開経路を新設したりする変更は含めていません。

## 維持者による公開手順

公開は利用者の確認を受けたあとの別作業です。この原稿のマージだけでは Issue #818 を完了扱いにせず、クローズしません。書き込み権限のある維持者が次の手順を実行し、反映結果を #818 に記録します。

1. GitHub API の `repos/Kling0012/shape-of-dreams-rpg-plan` で `full_name` を確認します。2026-10-19 の再確認では正式名は `Kling0012/shape-of-dreams-rpg-plan` で、原稿の Sources と一致しました。名前が変わっていた場合は、以下のURLと原稿の根拠リンクを現行名にそろえます。
2. [GitHub Wiki](https://github.com/Kling0012/shape-of-dreams-rpg-plan/wiki) を開きます。最初のページがまだなければ、Web画面で `Home` を作成して保存し、`.wiki.git` を作成します。既存ページがある場合は先にその本文を取得して控えを残します。
3. worktree 外の作業用ディレクトリへ `https://github.com/Kling0012/shape-of-dreams-rpg-plan.wiki.git` を clone します。`Home.md`、`_Sidebar.md`、`Getting-Started.md`、`Relics-and-Forge.md`、`Star-Map.md`、`Infinity.md`、`Co-op-Trade.md`、`Saves-and-Continue.md` の8ファイルを比較して反映します。GitHub Wiki にだけある本文は残し、ページの削除やリネームは行いません。README はコピーしません。
4. 差分を確認して Wiki 側へ commit・push し、Web画面で8ページの本文、Home とサイドバーからのリンク、Sources が開くことを確認します。
5. DokuWiki 用には、上のコマンドでマージ後の `dev` から生成します。`wiki-pages` の最新内容を別の作業用 checkout で取得し、生成した `pages/dreamforge/basics.txt` の差分を比較して反映します。既存ページや手書きの追記を一括で上書きせず、公開先へ反映する前に差分を確認します。再調律の追加費用表は伝説のオレンジ枠固有品を対象とする表記も確認します。
6. 公開したページのURL、反映したソースのコミット、Wiki と `wiki-pages` の反映コミット、閲覧確認の結果を #818 に記録します。未反映の公開先や確認できなかった項目があれば、残作業として記録します。
