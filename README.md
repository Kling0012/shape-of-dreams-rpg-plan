# Dreamforge RPG

Shape of Dreams を、持ち帰る装備と長く育てる星図を軸にしたRPGへ広げるMODです。協力プレイに対応しています。

A mod that turns Shape of Dreams into a long-term RPG built around gear you bring home and a star map you grow over many runs. Co-op is supported.

- 最新版と導入用 zip：[Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases)
- 版ごとの変更：[更新履歴（CHANGELOG）](CHANGELOG.md)

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
