# SodRpg.Core

技術プロトタイプ（`docs/tech-prototype-plan.md`）の「純C#コア」。ゲーム本体（Unity・Dew.Core）に依存せず、`dotnet test` だけでビルドと検証ができる。

- 対象: netstandard2.0（.NET Framework 4.x 系のMOD環境から参照できる範囲。第三者パッケージへの依存なし）
- 範囲: 計画書の要素B（保存アイテム）と要素C（重複しない協力報酬）の論理、要素Aの補正の付け外し（冪等・全除去）。ゲームへの接続（EntityStatus、保存先の決定、通信）は接続層の仕事で、ここには含めない。

| ファイル | 役割 |
| --- | --- |
| `LedgerState.cs` / `Catalog.cs` | 台帳の内容（所持アイテム、素材、付与済み報酬、隔離）と、扱ってよいIDの定義 |
| `LedgerSerializer.cs` / `Internal/Json.cs` | チェックサム付きの保存形式、旧形式(版0)の移行、不正値の隔離 |
| `LedgerStore.cs` / `IFileSystem.cs` | 一時ファイル→置換の保存、バックアップ、復旧。確定点は置換の完了 |
| `GrantProtocol.cs` | 報酬ID（冪等キー）、ホストの未配布リスト、クライアントの受取（保存後にだけ ack） |
| `ModifierTracker.cs` | 能力補正の冪等な付与・除去、望ましい集合への Reconcile |
| `Game/` | 遊べるMOD（Dreamforge RPG）のルール一式：遺物の抽選、確保と夢の深度、遺失物、星図、鍛冶、能力の集計、固有効果の状態、プロフィールの保存 |

## 試験

```
dotnet test
```

`tests/SodRpg.Core.Tests/GrantSimulationTests.cs` は、メッセージの欠落・重複・遅延・並べ替え、切断、保存途中のクラッシュを固定シードで混ぜ、
「重複付与なし」「ホストが閉じた報酬は必ず受取人のディスクにある」「通信が回復すれば取りこぼしなし」を確認する。

## ゲーム内MOD

`SodRpg.Mod/` はゲーム本体のアセンブリを参照するMOD（Dreamforge RPG / 夢鍛RPG）。`Game/` のルールを取り込んで1つのDLLにする。ゲームのDLLが必要なため `SodRpg.sln`（CI）には含めない。詳細は [SodRpg.Mod/README.md](SodRpg.Mod/README.md)。
