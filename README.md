# Dreamforge RPG

Shape of Dreams を、持ち帰る装備と長く育てる星図を軸にしたRPGへ広げるMODです。協力プレイに対応しています。

A mod that turns Shape of Dreams into a long-term RPG built around gear you bring home and a star map you grow over many runs. Co-op is supported.

- 最新版と導入用 zip：[Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases)
- 版ごとの変更：[更新履歴（CHANGELOG）](CHANGELOG.md)

## 開発について / About development

このMODの開発はAIが担当しています。IssueやPull Requestの確認、不具合の再現と修正、レビュー、テスト、マージ、リリースまで、すべてAIが自動で行います。 / This mod is developed by AI. Everything from triaging issues and pull requests, reproducing and fixing bugs, to review, testing, merging and releasing is handled automatically by AI.

## 主な遊び

- **遺物**：敵を倒すと、各プレイヤーに個別に装備（遺物）が落ちます。6つの枠、5段階のレア度があり、エピック以上は固有の効果を持ちます。
- **確保と深く潜る**：拾った遺物は、まだ持ち帰っていない状態で鞄に入ります。新しいゾーンに着くたびに、保管庫へ「確保する」か、危険と引き換えに報酬を増やして「深く潜る」かを選びます。全滅すると、確保していない遺物は遺失物になり、次の遠征で取り戻せます。
- **旅人の星図**：旅人ごとに星の経験を貯めて、星図に割り振ります。星図はロードアウトとして旅人ごとに5つまで保存でき、遠征の外で切り替えられます。
- **鍛冶と工房**：強化・再調律・合成・限界突破・覚醒などで遺物を育てます。
- **夢の圧・悪夢化・道標**：育つほど敵も強くなり、悪夢化した敵や道標の選択で、遠征ごとに手応えと報酬が変わります。本体の Limbo とも連動します。
- **協力プレイ**：抽選と保存は各自のPCで行い、能力の反映はホストが行います。参加者どうしで遺物などを取引できます。

仕組みと数値の詳細は [MOD の説明](src/SodRpg.Mod/README.md) にあります。

## 導入

1. [Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases) から `DreamforgeRPG-vX.Y.Z.zip` をダウンロードします。
2. zip を展開し、中の `DreamforgeRPG` フォルダを、ゲームのフォルダの `Mods` に置きます（`<Shape of Dreams>/Mods/DreamforgeRPG/`）。
3. ゲームを起動し、MOD の管理画面で Dreamforge RPG を有効にします。
4. 更新するときは、`DreamforgeRPG` フォルダを新しいものに置き換えて、ゲームを再起動します。セーブデータは引き継がれます。

- **協力プレイでは、参加者全員が同じ版を入れてください。** 能力の反映にはホストのMODが必要です。
- セーブデータは `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/` に保存されます（Windows では `%USERPROFILE%/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/QuickSave/Mods/DreamforgeRPG/`）。直前の内容は `.bak` として残ります。

## 操作

| キー | 内容 |
| --- | --- |
| F6 | Dreamforge のメニュー（装備・鍛冶・星図・工房・記録） |
| F7 | 確保する（確保地点で） |
| F8 | 深く潜る（確保地点で） |

キー・言語・表示倍率・HUD の表示量・軽量化などは、ゲームの MOD 設定から変えられます。

## 不具合の報告

[Issues](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues) に、起きたこと、1人か協力プレイか、使っていた旅人を書いてください。ゲームのログ（`Player.log`）があると原因を早く特定できます。

ポラリス踏破後、次の遠征で夢の圧が下がる・装備効果が出ない報告（[#449](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/449)）は、まだ原因が確定しておらず、未修正です。次の発生時に切り分けるため、既存の `[DreamforgeRPG]` ログへ `diag449` の記録を追加しています。

- **ログの場所**：このMODはゲーム標準のMOD機構とUnityのロガーを使い、記録先は `Player.log` です。BepInEx の `LogOutput.log` ではありません。Windowsでは `%USERPROFILE%\AppData\LocalLow\Lizard Smoothie\Shape of Dreams\Player.log`（セーブの `QuickSave` フォルダと同じ階層）。Steamの「管理 → ローカルファイルを閲覧」で開くゲームフォルダ内ではなく、Windowsのユーザーフォルダ側にあります。LinuxでProtonを使う場合は、Steamライブラリの `steamapps/compatdata/<ゲームのAppID>/pfx/drive_c/users/steamuser/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/Player.log` を探してください。起動オプションの `-logFile` を指定している場合は、その指定先です。
- **送り方**：再発したら、ゲームを再起動する前に `Player.log` をコピーし、#449へ添付してください。再起動済みなら、直前のログ `Player-prev.log` が残っていればそちらも対象です。協力プレイではホストと症状が出た参加者それぞれのログが必要です。前後の警告・エラーも切り分けに使うため、`diag449` 行だけでなく対象ログを添付し、共有前にユーザー名・アカウントID・接続先・個人のフォルダ名などの個人情報を除いてください。セーブデータの添付は不要です。
- **一緒に知りたいこと**：ゲームとMODの版、ソロ／ホスト／ゲスト、通常／インフィニティ（踏破した遠征と次の遠征の両方）、旅人、踏破から次の遠征までの操作（ロビー帰還・Continue・再接続・旅人や装備の変更）、前後の夢の深度と夢の圧の具体的な数値、効果が出なかった装備名と効果、再装備や再起動で戻るかを書いてください。

診断は終幕通知・精算・遠征開始・ビルド送信／ホスト受付と適用・夢の圧の計算と受信を記録します。装備数、参照切れ数、夢レベル、使用星点、圧の倍率、ゲーム内オブジェクトの一時的な番号などが対象で、名前・アカウントID・セーブ全文は追加ログに含めません。同一内容を抑制し、クライアント／ホスト各48行までを本体の遠征IDが変わるごとに記録します。上限に達した最後の行には `run log limit reached` と出ます。ゲームのデータ・動作・通信は変えていません。Issueはログを待つため、診断追加のリリース後も開いたままにします。

### 記憶をすべて装着する固有効果の調査（#457）

利用者の症状は未再現・未修正です。不具合の調査のための記録を追加し、原因の確定・修正はログ受領後に行います。今回の診断追加では条件・効果量・保存・通信を変更していません。既存の連携テストの成功は、報告された症状の解消を示すものではありません。Issue はリリース後も閉じず、ログを待ちます。

- **ログの場所**：上記と同じゲーム標準の `Player.log`。Windows は `%USERPROFILE%\AppData\LocalLow\Lizard Smoothie\Shape of Dreams\Player.log`。Linux（Proton）は Steam ライブラリ内の `steamapps/compatdata/<ゲームのAppID>/pfx/drive_c/users/steamuser/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/Player.log`。起動オプションで `-logFile` を指定した場合は指定先です。
- **送り方**：症状が起きた直後、ゲームを再起動する前にログをコピーし、ユーザー名・アカウントID・接続先・個人のフォルダ名などを除いて #457 に添付してください。再起動済みなら `Player-prev.log` が残っていればそちらも添付してください。協力プレイではホストと症状が出た参加者の両方のログをお願いします。前後の警告・エラーも必要なので `diag457` 行だけに切り詰めず、セーブデータは添付不要です。
- **一緒に知りたいこと**：ゲームとMODの版、ソロ／ホスト／ゲスト、旅人、装備名と表示された条件、Identityを含む装着記憶の組み合わせ、症状までの操作（装備変更・ロビー帰還・Continueなど）、効果が出ないと判断した画面や数値を教えてください。

ホストの連携走査後に、旅人・装着記憶とエッセンスの種類、連携数・成立数、Identityを条件に含む連携の要求と判定を `diag457` として記録します。名前・アカウントID・セーブ全文は記録せず、旅人の一時的な番号だけを使います。同じ状態の連続出力を抑え、状態変更ごとに概要1行とIdentity連携最大6件（省略時は件数）、ホスト全体で遠征ごとに最大32行です。上限の最後に `run log limit reached` と出ます。ログ処理の失敗はゲーム処理を止めません。

## 開発

| 場所 | 内容 |
| --- | --- |
| `src/SodRpg.Core` | ゲームのルール（純 C#。ゲーム本体に依存しない） |
| `src/SodRpg.Mod` | ゲーム内 MOD（Harmony・Mirror・IMGUI） |
| `tests/` | 自動テスト |
| `tools/balance/` | 数値の原本（JSON）と C# の生成スクリプト |
| `docs/` | 計画書・仕様・引き継ぎ資料 |

ビルド（ゲームの DLL を参照するため、ゲームのフォルダが必要です）：

```sh
dotnet build src/SodRpg.Mod -c Release -p:GameDir="<Shape of Dreams のフォルダ>"
```

テスト：

```sh
python tools/balance/gen_cs.py --check
python -m unittest discover -s tools/tests -p "test_*.py"
python tools/test_changed.py --all        # 時間のかかる網羅テストも含めるときは --slow
```

### 資料

| 資料 | 内容 |
| --- | --- |
| [開発計画書 Ver1.0](docs/development-plan.md) | このMODの元になった大規模RPG化の計画書（旧 README） |
| [開発計画](docs/dreamforge-roadmap.md) | 次に作るものと候補 |
| [引き継ぎ](docs/dreamforge-handoff.md) | 開発の現状・方針・構成・環境・手順 |
| [テスト](docs/testing.md) | テストの構成と検証記録 |
| [仕様](docs/specs/) | 機能ごとの仕様と根拠 |
| [技術プロトタイプ計画](docs/tech-prototype-plan.md) | 補正・保存・協力報酬の接続点を確かめた計画 |
