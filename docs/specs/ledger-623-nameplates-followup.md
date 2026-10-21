# #1009（PR #1013）レビュー提案の対応記録

2026-10-21、マージ済み PR #1013 のレビューで挙がった軽微な提案3件（L1〜L3）を確認した。
3件とも文書と記録の整合の指摘で、製品コードとテストは変えていない。

## MOD 設定の文言の言語（L1）

`DreamforgeConfig` の `enemyNameplates`（`src/SodRpg.Mod/DreamforgeConfig.cs:73-76`）の LabelText と Description は
英語と日本語の2言語で、`showEnemyOwnershipMarks` などほかの全設定項目と同じ書式である。機能への影響はない。
MOD 設定の文言に中国語を足すかは、この項目だけでは決められない。足すなら全設定項目をまとめて扱う必要があるため、
方針が決まった時点で別の Issue に切り出す。今回は変更しない（見送り）。

## 仕様 FR-11 の読み替え（L2）

`changelog.d/1013-nameplates.md` が日本語と英語の2言語なのは正しい。[原則 VII](../constitution.md) と
[changelog.d/README.md](../../changelog.d/README.md) は、更新履歴に中国語を書かないと定めている
（2026-10-20 利用者の指示）。Issue #1009 の要件 FR-11（日本語 / English / 中文）だけがこの原則と食い違うため、
FR-11 は「日本語 / English」と読み替える。#1009 の実装（PR #1013）はこのまま正しく、コードは変えない。
gh-auto が管理する仕様の記録と Issue #1009 の本文はリポジトリの外にあり、この PR では触れない。
この2か所の FR-11 の文言は、gh-auto 本体がこの読み替えに合わせて直す。

## PR 本文の印の表記（L3）

PR #1013 の本文では、印の `*`、`**`、`***` がマークダウンの強調として解釈され、
悪夢や宿敵の印が欠けて表示される。コードの定数（`src/SodRpg.Mod/DreamforgeUi.cs:45-47` の
VariantDangerMark・NightmareDangerMark・DeadlyDangerMark）と更新履歴の断片（印をコード断片で囲んである）は
正しい。マージ済み PR の本文の修正は GitHub 上の操作で、この作業（コミットまで）の範囲外のため見送る。
今回の PR 本文では、印を `*` のようにコード断片で囲んで表記する。

## 実行した検証

- 対象ファイルの確認: `DreamforgeConfig.cs` の設定の書式、`DreamforgeUi.cs` の印の定数、
  `changelog.d/1013-nameplates.md` と `changelog.d/README.md` の断片の書式、`docs/constitution.md` のVII。
- `python3 tools/test_changed.py --base HEAD --list`: この変更から選択されるテストなし。

## 保留と未実行

- L1・L3 は見送り。理由は各節に書いたとおり。
- 実機での表示確認はしていない（ゲームを起動できる環境がない）。今回はコードを変えていないため、対象はなし。
