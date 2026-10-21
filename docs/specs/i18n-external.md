# 外部翻訳ファイル

英語の原文をキーにした JSON ファイルで表示言語を追加する。既存の `Loc.T` と `Txt` の呼び出しはそのまま使い、未訳の文は英語で表示する。

## ファイルの形式と検査

`src/SodRpg.Core/Game/LocExternal.cs` の `LocExternal` が、Core の JSON 処理を使って UTF-8 のファイルを読む。ファイル全体の上限は 2 MB。必須の項目は `language`、`name`、`strings` で、`authors` は任意の文字列配列とする。

- `language` は `^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$` に合い、拡張子を除いたファイル名と一致する。組み込みの `ja`・`en`・`zh` と `template` は使えない。`zh-TW` などは使える。
- `name` はボタンに出す言語の自称で、1〜40文字。
- `strings` は英語の原文と訳の組。キーは完全一致で引き、空の訳は未訳として扱う。
- 訳の `{0}`・`{1}` などの集合、リッチテキストのタグの種類と数は原文と一致させる。`<color=...>` と `<size=...>` の属性値は変えられる。原文にないタグは追加できない。
- 原文にない `http`・`www.`・`://`、改行とタブ以外の制御文字は追加できない。訳の長さは原文の文字数の4倍に40文字を足した長さまで。

不正な訳は項目ごとに除き、件数を警告1回にまとめる。ファイルの構造や読み込みに問題があれば、そのファイルだけを無視する。

## 読み込みと表示

`src/SodRpg.Mod/ExternalLanguages.cs` が起動時と設定の再読込時に次の場所を読む。同じ言語の有効なファイルが両方にあれば、手元のものを優先する。

1. MOD フォルダの `lang/*.json`
2. `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/lang/*.json`

`DreamforgeConfig.language` の既定値は空文字列。読み込めた外部言語のコードを指定すると `LocLanguage.External` を選び、それ以外では従来の `chinese`・`japanese` の判定を使う。言語ボタンは日本語 → English → 中文 → 外部言語の自称順 → 日本語と巡回する。

外部言語では `Loc.T` の両オーバーロード、`Txt.ToString()`、`Loc.Zh` が外部辞書を引く。`Loc.Japanese` は false。`Loc.F(ja, en, zh, args)` は選んだ訳を書式に使い、書式化に失敗すると英語で再試行する。既存の3言語の選択と表示は変えない。

外部辞書と未訳の収集先は、`Loc.Language` と同じく共有値とスレッドごとの上書きを持つ。`Loc.CacheKey` は外部辞書の切り替えや再読込でも変わり、表示キャッシュを更新する。表示時の辞書検索とキャッシュキーの取得ではメモリを確保しない。

`DreamforgeConfig.translationDump` は既定で false。有効なときだけ、外部言語で表示した未訳の英語文を `LocMissing` に集める。プロフィール保存時と終了時に、手元の `lang/missing-<code>.json` へ雛形と同じ形式で書く。書き込みに失敗しても警告だけで続行する。収集を無効にした表示経路ではメモリを確保しない。

## 翻訳の雛形

`python3 tools/i18n/export_template.py --version vX.Y.Z --out PATH` で生成する。Python 標準ライブラリだけを使い、`src/` の C#（生成済みソースを含む）から `new Txt(` と `Loc.T(` の第2引数を抽出する。通常の文字列リテラルを対象とし、補間や連結などの式は除く。コメントや文字列中のコードらしい文は呼び出しとして扱わない。

`tools/i18n/exclude.txt` の `file:` はリポジトリからの相対パスの glob、`text:` は英語文に対する Python の正規表現。コンソール用ツール、開発者向け設定、ログ用ファイルを除き、プレイヤー向けの文も含むファイルでは開発用の文だけを指定して除く。キーは重複を除いて並べ替え、`version`、`count`、値がすべて空文字列の `strings` を出力する。

翻訳の提出先は公開リポジトリ `Kling0012/dreamforge-rpg` の `translations/`。MOD に同梱するファイルは `src/SodRpg.Mod/lang/` に置く。公開側の受付自動化はこの実装に含めない。
