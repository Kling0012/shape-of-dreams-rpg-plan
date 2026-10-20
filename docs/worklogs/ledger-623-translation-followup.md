# 翻訳に関する改善案 L1〜L7 の対応

2026年10月20日、PR #964・#971・#973 の改善案を確認した。以下は今回の変更と、変更しなかった箇所の判断理由である。

| ID | 対応と根拠 |
| --- | --- |
| L1 | 用語集のコメントに、既訳の固有品では Cloak と Drake Hide を「斗篷」、Mantle と Shroud を「披风」とする使い分けを記録した。基本装備の `armor.flowing_cloak` と `armor.star_cloak` には中国語訳がなく、基本装備の既訳には合わせられなかった。 |
| L2 | `ProfileWriteException.Message` で `_english` の null を先に判定し、その他の訳は null に対応する `Loc.Zh` へ渡すよう変更した。第3引数は日英表示でも評価されるため、`StartsWith` と辞書の null キー参照をともに避ける必要がある。現在の生成箇所は非nullの英文を渡しており、防御的な変更である。保存処理や保存形式は変更していない。 |
| L3 | 図鑑の固有効果名を `Content.PowerName` の各言語の明示訳から取得するよう変更した。言語の取得・復元も `LocLanguage` で行う。一般辞書から図鑑専用の固有効果名を外し、共通関数が参照する項目を残した。これにより `Barrier` や `Second Wind` の固有名訳が一般語に適用されることを防ぐ。前の修正ラウンドで、PR の途中に削除していた一般ラベル14件と、図鑑以外に参照のある `Wildfire`・`Vigor` を復元した。マージ後のベースと比べると、これらの辞書項目の訳は変えていない。マージ後のベースでは `Families.cs` の `Frost` に中国語の第3値「冰霜」が既にあり、辞書には依存していない。家系名は「冰霜」、固有効果名は「霜寒」の既訳を維持する。 |
| L4 | Co-star を「联袂」にそろえ、対象の中国語説明に残る Ultimate を「奥义」に直した。Ember は装備名の「余烬」と固有効果の「火种」、Steam は銘品名の「白雾」と属性反応の「蒸汽」を使い分ける理由をコメントに記録した。Husk は `HeroNames` の日本語名も「空殻」であり、その意味に合わせた「空壳」を維持した。Overload と Overcharge はともに「过载」を維持した。前者は固有効果名、後者は強化・覚醒と並ぶ装備強化の説明で使われ、`sets.json` の既訳も後者を「过载」としている。今回だけ Overcharge を別訳にすると既訳と食い違うため変更しなかった。同じ語だけを表示すれば両者を区別できない点は残る。ゲーム内での読み取りやすさは未確認である。 |
| L5 | `PercentChange` は `value < 1` で増減を選び、`Math.Abs(value - 1) * 100` を表示する。GlassAegis の中国語も同じ条件・式で、0や1の特別扱いはどちらにもない。ソース上で一致しているため変更しなかった。倍率0は減少100%、1は増加0%になる。 |
| L6 | 指摘された3件は作業開始時点で「瞬间」「解开」になっていた。最終状態の `Content.cs` 2066〜2666行から文字列リテラルの `new Txt` 第3値399件を抽出し、ICUの `Traditional-Simplified` 変換との相違0件、「間・開・決・関」の検出0件を確認した。 |
| L7 | Content の「终极技能」3件を「奥义」、「疑念」を「疑心」に変更した。用語集の Ultimate の参照コメントは「奥义」の1件にまとめた。 |

## 実行した検証

- Core と関連テストプロジェクトの Release ビルドが完了した。既存テストコード由来のコンパイル警告がある。
- 関連する既存11テストクラスを実行し、90件成功、失敗0件、スキップ0件。対象は CodexQuery、CodexNamedCategories、CodexTextConsistency、NewPowersV129、ElementReactions、Waypoints、AsyncProfileWriter、ProfileStoreRoundtripIssue429、NotificationHeroName、HeroSigil、ContentTableV129。環境に .NET 8 がなく最初の実行は testhost 起動時に中断したため、`DOTNET_ROLL_FORWARD=Major` を指定して .NET 10 上で再実行した。
- 上記399件の字体点検と、NewPowersV129・Waypoints の中国語内に Ultimate が残っていないことを機械的に確認した。
- 一般辞書から外した図鑑用の固有効果名93件は、すべて既存の明示的な中国語訳を持つことを機械的に確認した。
- `git -c core.whitespace=cr-at-eol diff --check` は成功。作業記録と更新履歴に yomiyasu lint を実行し、エラー0件。どちらも箇条書き比率の警告が1件あったが、検証・未実行項目の列挙と更新履歴の指定書式なので維持した。

## 未実行

- ゲーム内での言語切り替え、表示・フォント・折り返しの確認：この環境ではゲームを起動できない。
- null を渡した例外表示と GlassAegis の境界値表示の直接実行：今回の指示では新しいテストを作成せず、既存テストとソースの条件・式を確認した。既存テスト90件の成功を、これらの直接再現の代わりには扱っていない。
- .NET 8 上のテスト：ランタイム未導入。.NET 10 へのロールフォワードで実行した。
- 全テスト・低速テスト：作業指示に従い、変更に関係する既存テストだけを実行した。
- ゲームDLLを参照するMOD本体のビルド：ゲーム環境がなく、今回は純C#コアを検証した。

## PR #981 の修正ラウンド

2026年10月21日、指摘 F1 に従い、辞書を外す前提と用語の点検範囲を見直した。以下の結果はこのラウンドで実行した確認である。

### 一般ラベルと固有名の参照

`rg -n '"(Legendary|Normal memory|Basic attack|Ability power|Shield|Cooldown reduction|Follow-up damage|Bridge combo|Cooperative trade|Escrow|Receipt|Unsecured relics|Awakening points|Star experience|Barrier|Steam|Ember|Frost|Second Wind|Wildfire|Vigor)"' src --glob '!LocZhGlossary.cs'` で完全一致する文字列を検索した。単語だけの検索も行い、より長い説明文や識別子に含まれる一致を区別した。

| キー | 一般辞書以外の完全一致する文字列の参照 | 対応 |
| --- | --- | --- |
| Legendary | `Content.RarityName` の `Txt` 第2値1件。第3値に既訳あり | 一般ラベルの削除は要件外のため辞書に復元 |
| Normal memory、Basic attack、Ability power、Shield、Cooldown reduction、Follow-up damage、Bridge combo、Cooperative trade、Escrow、Receipt、Unsecured relics、Awakening points、Star experience | 各0件 | 動的な組み立てに対する未参照の証明とはせず、13件とも辞書に復元 |
| Barrier、Steam、Ember、Second Wind | 各1件。すべて `Content.PowerName` の第2引数で、第3引数に明示訳あり | 図鑑専用の辞書登録を削除した状態を維持 |
| Frost | `Content.PowerName` と `FamilyPrefs.LabelName` の2件 | マージ後のベースでは家系名に「冰霜」の明示訳あり。固有効果名「霜寒」と訳し分け、家系名は変更しない |
| Wildfire | `Content.PowerName` と `HeroStarRoutes.cs` の `CapG` 呼び出しの2件 | `CapG` が `new Txt(ja, en)` を作るため辞書に復元 |
| Vigor | `Content.PowerName` と `RoomOmens.Describe` の2件 | 兆し名が2引数の `Loc.T` を使うため辞書に復元 |

`rg -n 'Loc\.Zh|LocZhGlossary\.Lookup' src --glob '*.cs'` では、`Txt.ToString` と `Loc.T` からの共通経路、Waypoints・HeroSigils・HeroNames・PairCombos の変数経由、ProfileStore の保存エラー経路を確認した。Waypoints の `slot.Item2` は weapons / armor / charms / headgear / hand gear / footwear の6値で、今回の削除対象を組み立てない。`Txt` 第3値を省いた他のラベルや2引数の `Loc.T` も辞書を参照するため、直接の `Lookup` 検索だけでは未参照と断定しない。

ベースの辞書との差分から削除キーを取り出し、`src/` 全体の文字列と照合した。復元後も削除する91件のうち、固有効果名の明示訳以外に残る参照は `Families.cs` の `Frost` だけだった。マージ後のベースでは家系名9種すべてに中国語の第3値があり、`Frost` も「冰霜」を明示しているため、辞書の削除で英語には戻らない。辞書キーの重複は0件だった。

### 奥義の表記

`rg -n '终极技能' src` は一致0件だった。続いて .NET SDK 同梱の Roslyn で `src/` の C# ファイル486個を構文解析し、`new Txt` 第3値と `Loc.T` 第3引数の計4,306件を調べた。複数行の呼び出し、補間文字列の表示部分、条件式の文字列を含め、大文字・小文字を区別しない Ultimate の残りは0件だった。`LocZhGlossary` の中国語側にも0件だった。Content の全区間、ElementReactions、Families、MOD の通知文を含む。

### Overcharge の改善案

Overload の固有効果名と Overcharge の装備強化用語が、ともに「过载」になっている。別訳へそろえる改善案をこの記録へ追加した。候補は Overcharge を「超载」または「充能过载」にする案である。対象は `LocZhGlossary` の項目・参照コメント、`tools/balance/equipment/sets.json` の `description.zh`、そこから生成する `SetBalanceValues.g.cs`。一部だけの置換を避け、固有効果名 Overload と装備強化の Overcharge を表示上で区別し、既訳と生成物の一致を確かめる必要がある。このラウンドでは指摘の指定どおり改善案の記録までとした。

### このラウンドの検証

- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter '<関連12クラス>' --nologo --verbosity quiet`：93件成功、失敗0件、スキップ0件。前ラウンドの11クラスに `FamilyRollV132Tests` を加えた。Release ビルドが完了し、既存テストコードのコンパイル警告があった。
- 同じプロジェクトで `--no-build --no-restore --filter 'FullyQualifiedName~RoomOmensTests|FullyQualifiedName~StarRoutesV127Tests|FullyQualifiedName~StarRoutesV128Tests'`：18件成功、失敗0件、スキップ0件。復元した辞書を参照する兆しと旅人ルートの既存テストを追加で実行した。
- 構文解析の作業用コマンドは `dotnet run --project /tmp/pr981-zh-audit-20261021/Audit.csproj -c Release -- "$PWD"`。表示そのものの再現テストではなく、呼び出し引数と辞書の確認である。新しいテストは作成していない。
- `git -c core.whitespace=cr-at-eol diff --check`：成功。Families.cs は既存の CRLF を保持した。

ゲーム内表示、MOD 本体のビルド、.NET 8 上のテスト、null 例外と GlassAegis の直接実行、全テスト・低速テストは、上記「未実行」と同じ理由でこのラウンドでも実行していない。一般ラベル14件と家系名の表示をゲーム内で直接再現したとは扱っていない。

## マージ後のベースに合わせた訂正

2026年10月21日、修正ラウンドの指摘 F1・F2 に従い、dev の `db38fa81` と PR 全体の差分を確認した。

- F1：家系名 `Frost` は dev で既に `new Txt("氷霜", "Frost", "冰霜")` となっていた。PR による「霜寒」への変更を取り消し、用語集の #961 の規定に合わせて「冰霜」に戻した。固有効果 `Frost` の明示訳「霜寒」はそのままで、家系名と区別される。`Families.cs` は dev との差分がなくなった。
- F2：`Wildfire`・`Vigor` はベースにも辞書項目があり、今回の最終差分では訳を変えていない。家系名にも既に明示訳があるため、これらの英語表示への後退をリリース済みの不具合として書いた `changelog.d/981-zh-fallback.md` を取り除いた。この PR で直した不具合は既存の `changelog.d/623-zh-terms.md` に記録されている。
- L3 と参照一覧の説明を、マージ後のベースに明示訳がある状態へ訂正した。前のラウンドによる辞書項目の復元は PR の途中の修正であり、リリース済みの不具合の修正とは扱わない。

### 訂正後の検証

- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter 'FullyQualifiedName~FamilyRollV132Tests|FullyQualifiedName~CodexQueryTests|FullyQualifiedName~CodexNamedCategoriesTests|FullyQualifiedName~CodexTextConsistencyTests' --nologo --verbosity quiet`：11件成功、失敗0件、スキップ0件。Release ビルドが完了し、既存テストコードのコンパイル警告があった。.NET 10 へのロールフォワードで実行した。
- `git diff origin/dev -- src/SodRpg.Core/Game/Families.cs`：出力なし。`Content.PowerName` の `Frost` は「霜寒」、用語集の家系名は「冰霜」であることをソースで確認した。

ゲーム内での家系名と固有効果名の表示は、この環境ではゲームを起動できないため未実行である。上記の既存テストには両者の中国語名を直接比較する再現テストがなく、表示を直接再現したとは扱っていない。MOD 本体のビルド、.NET 8 上のテスト、null 例外と GlassAegis の直接実行、全テスト・低速テストは、上記「未実行」と同じ理由で実行していない。前ラウンドの広範囲な参照・字体・奥義表記の点検も、対象の文字列を変更していないため再実行していない。

## PR #981 のマージ後に残った改善案

2026年10月21日、L1・L2 をそれぞれ確認し、両方に対応した。

- L1：`rg -n '终极记忆' src/` で、Demon の装飾品1件、Skoll の装飾品・2部位セット効果2件、Nyx の装飾品・靴・2部位と6部位のセット効果4件、用語コメント1件の計8件が見つかった。説明7件を既訳の「奥义记忆」にそろえ、用語集の #961 のコメントも更新した。実績やエッセンスなど、ほかの箇所には該当表記がなかった。変更は訳語だけで、数値や発動条件は変えていない。
- L2：PR #981 のマージ直前のベース（`7d23dddb^1`）と比べ、一般辞書の Keystone を Legendary の直後、Nightmare を Awakening points と Star experience の間へ戻した。キーと訳は変えていない。

### 今回の検証

- `rg -n '终极记忆|Ultimate记忆' src/`：修正後は一致0件。対象7件と用語コメントが「奥义记忆」になったことも確認した。過去の記録の「奥義の表記」点検には含まれていなかった表記を、今回追加で調べた。
- `DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release -p:PythonCommand=python3 --filter 'FullyQualifiedName~BossSetsStageATests|FullyQualifiedName~BossSetsStageB12Tests|FullyQualifiedName~CodexTextConsistencyTests|FullyQualifiedName~CodexQueryTests|FullyQualifiedName~CodexNamedCategoriesTests' --nologo --verbosity quiet`：85件成功、失敗0件、スキップ0件。Release ビルドが完了し、既存テストコード由来のコンパイル警告があった。.NET 10 へのロールフォワードで実行した。
- `git diff --word-diff=plain --unified=0` で、ボス説明3ファイルの変更が訳語7件だけであることを確認した。用語集の差分でも、コメント1件と辞書項目2件の移動だけであることを確認した。

### 今回の未実行

- ゲーム内での中国語表示・フォント・折り返しの確認：この環境ではゲームを起動できない。既存テストの成功を、ゲーム内表示の直接再現とは扱っていない。
- ゲームDLLを参照するMOD本体のビルド：ゲーム環境がないため。
- .NET 8 上のテスト：ランタイムがなく、.NET 10 上で代わりに実行したため。
- 全テスト・低速テスト：作業指示に従い、変更箇所に関係する既存テストだけを実行したため。
