# Issue #957 装備データ7/8の翻訳と要件の確認

2026-10-20、PR #967 の修正ラウンドで確認した。

## 対象と修正内容

`Content.cs` の3790〜4388行には、固有品200点の名前と説明、計400件の `new Txt(ja, en)` があった。既存の8コミットで各50件に中国語の第3値が追加されており、未訳は0件だった。この範囲に `Loc.T` はない。

修正ラウンド開始時の `.gh-auto-result.json` は `{"status": "PENDING"}` で、`requirements_done` がなかった。実装と差分を照合し、FR-1〜FR-13の対応箇所を結果JSONに記載するための根拠を以下に残した。翻訳と用語集への追加変更はない。

## 要件と対応箇所

| 要件 | 対応箇所と確認結果 |
| --- | --- |
| FR-1 | `src/SodRpg.Core/Game/Content.cs:3790` から4388行。400件すべてに中国語があり、残り0件。 |
| FR-2 | 同ファイルの `Txt` コンストラクタと `Txt.ToString()`、対象400件。既存の第3引数 `zh` を使用。対象内の `Loc.T` は0件。 |
| FR-3 | `src/SodRpg.Core/Game/LocZhGlossary.cs:17` 以降の訳語と `Content.PowerName` に合わせた訳。Relayは「接力」、Frostは「霜寒」、Emberは「火种」。既存の `Content.RarityName` はLegendaryを「传说」と表示する。 |
| FR-4 | `src/SodRpg.Core/Game/LocZhGlossary.cs:45` 以降。兽群・守护・前卫などの頻出語と部位名を参照用コメントに記録。 |
| FR-5 | `Content.cs` の対象400件について、書式トークンの内容・順序、エスケープ、波括弧を英語と照合して一致。対象内の差し込みは0件。 |
| FR-6 | 第3引数だけを取り除くと、`Content.cs` 全文が分岐点 `69c88fd0` と一致。日本語・英語・処理・数値・バランスに変更なし。 |
| FR-7 | `86fdf052`、`dbeb1845`、`7cc2e5ac`、`cf3ede70`、`0a823ef0`、`2af5b509`、`e85c6627`、`1bdcc644` の順で各50件。用語集は `01cc2575`。 |
| FR-8 | ベースとの差分に `CHANGELOG.md` と `changelog.d/` の変更なし。翻訳仕様の明示的な除外に従う。 |
| FR-9 | `Content.cs` の変更は指定範囲の第3引数だけ。用語集の追加はコメントだけで、処理の流れと表示の組み立てに変更なし。生成ファイルに変更なし。 |
| FR-10 | FR-2と同じ。既存の3言語対応を使用し、新しい翻訳処理は追加していない。 |
| FR-11 | FR-6、FR-9と同じ。既存の日本語・英語と実行処理を保持。 |
| FR-12 | `Content.cs` の3790行より前と4388行より後は、分岐点と全文一致。隣の分担の3788行、4390行も保持。 |
| FR-13 | ベースとの差分に `*.g.cs` の変更なし。 |

## 実行した検証

- `gh pr view 967 -R Kling0012/shape-of-dreams-rpg-plan --comments` と `gh pr diff 967 -R Kling0012/shape-of-dreams-rpg-plan` を確認。コメントとレビューは0件だった。
- Pythonによる分岐点との照合で、400件の第3値、対象範囲、既存文言と処理の保持、書式、各翻訳コミット50件を確認した。
- `dotnet build src/SodRpg.Core -c Debug` は成功。警告0件、エラー0件。
- `dotnet test tests/SodRpg.Core.Tests --filter 'FullyQualifiedName~ContentCountTests|FullyQualifiedName~CodexTextConsistencyTests'` は、テスト用コードのビルド時に警告が出たあと、.NET 8ランタイム不足で中断した。`--no-build` での再実行でも同じ中断を確認した。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests --no-build --filter 'FullyQualifiedName~ContentCountTests|FullyQualifiedName~CodexTextConsistencyTests'` は.NET 10.0.11で実行し、2件合格、失敗0件、スキップ0件。
- `git diff --check origin/dev...HEAD` は成功。

## 判断したこと

範囲の先頭は `unique.h_packcommand_bands`、末尾は `unique.c_vanguard_necklace` の説明で、分岐点と現在の行番号は一致していた。固有品の識別子と実際の変更行で範囲を確かめ、指定の3790〜4388行を採用した。用語集のコメント追加は仕様で許可された例外として扱った。

既存の翻訳は要件を満たしていた。結果JSONの要件報告が欠けていたため、この確認記録と要件報告を補う。

## 未実行

- フルテストと遅いテストは、作業手順で対象外とされているため実行していない。
- `SodRpg.Mod` のゲーム参照付きビルドは実行していない。今回のコード差分はCoreの翻訳データと用語集コメントで、ゲーム参照を使う処理の変更はない。
- ゲーム内の表示幅・フォント・数値と並べた文の自然さは、仕様で見送った実機確認のため未実行。
- 過去の8コミット時点でのビルドは今回再実行していない。各50件のコミット履歴と現在のビルド成功を確認した。
