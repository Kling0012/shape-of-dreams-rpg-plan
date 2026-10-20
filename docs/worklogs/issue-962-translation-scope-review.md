# Issue #962 の翻訳範囲と要件の確認

PR #971 の翻訳対象と、修正ラウンドで再確認した結果を記録する。確認日は2026年10月20日。翻訳前の基準は `a07abea0`、翻訳完了時点は `f5095bbb`。以下の差分件数と不変性の検証は、`dev` の取り込み後の `bd366b6f` を、PR のベース `6dbf8b1f` と比較した結果である。

## 対象と除外

対象は `src/SodRpg.Core/` 全体にある、中国語のない2引数の `Loc.T` と `new Txt`。#938 のほかの翻訳 Issue に含まれるファイルと、生成ファイルは除く。「約310件」は目安とし、対象条件で数えた329呼び出しを訳した。

除外するファイルは次のとおり。パスを省略したものは `src/SodRpg.Core/Game/` 以下を指す。

| 担当 Issue | 除外するファイル |
| --- | --- |
| #939 | `src/SodRpg.Mod/DreamforgeUi.cs` |
| #940 | `src/SodRpg.Mod/` のその他のファイル |
| #941・#942 | `NamedItems.Data.cs` 全体 |
| #951〜#958 | `Content.cs` 全体 |
| #959 | `Rules.cs`、`Links.cs`、`DreamEvents.cs`、`Variants.cs` |
| #960 | `StarMapPresentation*.cs`、`StarMapClusters.cs`、`StarClusters.cs`、`StarClusters/` 以下、`StarSummary.cs`、`StarLoadouts.cs`、`HeroStarRoutes.cs`、`CurrencyStars.cs`、`DailyDream.cs`、`Nightmare.cs`、`Pact.cs`、`RunGrowth.cs` |
| #961 | `BossProfiles.*.cs`、`Bounty.cs`、`RoomOmens.cs`、`Feats.cs`、`Forbidden.cs`、`DreamEssences.cs`、`Families.cs` |
| #963 | `tools/balance/equipment/sets.json`、セットの生成器、`SetBalanceValues.g.cs` |
| 生成ファイル | `*.g.cs`、生成済みのコード、`obj/`・`bin/` 以下 |

#943〜#950 は PR なので、翻訳対象を割り当てる Issue の一覧から除いた。`Rules.DirectStash.cs`、`Rules.InterruptedRelics.cs`、`ProfileCodec.StarLoadouts.cs` は上表のファイルとは別なので対象に含めた。

`NewPowersV129.cs` の「残り」は`Epithet` と `Describe` 内の名前・効果説明・上限説明にある54呼び出しとした。すでに中国語を持つ `Names` 配列は変更していない。`LocZhGlossary.cs` への追記は FR-5 で指定された共通用語の登録として扱う。

## 翻訳した呼び出し

以下は翻訳前に2引数だった呼び出しの件数。各ファイルに中国語の第3引数を追加した。名前や説明を共通関数へ渡す箇所では、用語集から中国語を参照する。

| `src/SodRpg.Core/Game/` 以下のファイル | 件数 |
| --- | ---: |
| `BossBuildCodec.cs` | 2 |
| `CodexQuery.cs` | 1 |
| `CoopTrade.cs` | 47 |
| `CoopTradeCodec.cs` | 18 |
| `CoopTradeJournal.cs` | 8 |
| `Economy.cs` | 2 |
| `EffectiveAllocationValidation.cs` | 7 |
| `ElementReactions.cs` | 4 |
| `FractionalScopedModifiers.cs` | 66 |
| `HeroNames.cs` | 3 |
| `HeroSigils.cs` | 6 |
| `LevelEssenceSettings.cs` | 2 |
| `Mechanisms/CoopKeystones.cs` | 10 |
| `Mechanisms/PressureDividend.cs` | 1 |
| `Mechanisms/Rules.PressureDividend.cs` | 1 |
| `NamedItems.cs` | 4 |
| `NewPowersV129.cs` | 54 |
| `PairCombos.cs` | 2 |
| `ProfileCodec.StarLoadouts.cs` | 2 |
| `ProfileCodec.cs` | 6 |
| `ProfileStore.cs` | 5 |
| `RuleMessages.cs` | 20 |
| `Rules.DirectStash.cs` | 3 |
| `Rules.InterruptedRelics.cs` | 5 |
| `RunCheckpoint.cs` | 2 |
| `TravelerCombat.cs` | 3 |
| `Waypoints.cs` | 25 |
| `Workshop.cs` | 20 |
| 合計 | 329 |

## 用語と差し込み

`LocZhGlossary.cs` の差分は348項目の追記で、ベースの既存123項目と辞書の構築・参照処理は変更していない。同じブランチ内に重複キーはなく、Core 全体の既訳の名前と英語キーが一致する49件は中国語も一致した。Legendary は「传说」とした。道標、連携技、旅人の星・刻印の共通関数が参照する名前・説明221件も登録を確認した。翻訳前の基準で数えた350項目の追記・既存103項目とは比較対象が異なるため、現在の PR 差分には348項目・123項目を用いる。

用語集を並行して編集する PR との競合は、各 PR を統合するときにも確認する必要がある。この確認は現在のブランチ内の重複・訳語の一致を対象にしており、未マージの全ブランチを合わせた確認ではない。

中国語式329件の構文と補間を点検した。英語の単複・倍率・増減・オン／オフを含む14式は、中国語で同じ値・条件・書式を表すために第3引数内で表現を置き換えている。連結式の差分3件は名前の中国語参照、引用符、単位の訳である。元の日英引数と処理の組み立ては変更していない。

## 要件との対応

- FR-1・FR-3：上表の28ファイル、329呼び出しに対応。対象条件に合う未訳の2引数呼び出しは0件。
- FR-2：`SetBalanceValues.g.cs` の変更なし。
- FR-4・FR-5：`LocZhGlossary.cs` に共通語・名称・説明を追記。既訳の名前との一致を確認。
- FR-6：中国語式の補間数・順序・書式と、英語専用表現を置き換えた式の値・条件を確認。
- FR-7・FR-10・FR-11：上表の28ファイルを構文比較し、第3引数を除けば元のコードと一致。日英原文と既訳中国語44呼び出しも一致。用語集は追記以外の変更なし。
- FR-8：翻訳は `87889785`、`2bdfb19c`、`a526a167`、`e943b0a1`、`7576bb63`、`667b5bc2`、`f5095bbb` の7コミット。約50件ごとに区切り、最後に共通関数と用語集をまとめた。対象の残件数は0件。
- FR-9：`CHANGELOG.md` と `changelog.d/` の変更なし。

## 修正ラウンドで実行した検証

- Core の Release ビルド：成功。警告0件、エラー0件。
- 関連する既存15テストクラス：212件成功、失敗0件、スキップ0件。`DOTNET_ROLL_FORWARD=Major` で .NET 10 上で実行。既存のテストコードにコンパイル警告あり。
- 一時的な Roslyn 解析：対象の未訳0件、329件の第3引数追加、既訳44呼び出しの維持、用語集348項目の追記と既存123項目の維持を確認。現行の中国語式329件も翻訳時の式と照合し、構文エラーがないことを確認した。解析プロジェクトが出力先を共有していたため、範囲集計・不変性の検証・補間解析はそれぞれ再ビルドした DLL を直接実行した。
- 対象外ファイル、生成ファイル、更新履歴の変更：なし。変更したソース29ファイルは翻訳対象28ファイルと用語集だけ。
- `git -c core.whitespace=cr-at-eol diff --check origin/dev...HEAD` と作業ツリーの `diff --check`：成功。

## 未実行

- フルテスト・低速テスト：作業指示に従い、変更に関係する既存テストだけを実行した。
- .NET 8 ランタイム上のテスト：環境には .NET 10 だけが導入されている。
- ゲーム内の中国語切り替え、字形・フォント、折り返し・はみ出し、差し込み後の自然さ：この環境ではゲームを起動できない。
