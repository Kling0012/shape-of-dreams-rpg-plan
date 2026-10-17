# Issue #606 空殻の前提星の表示名

## 再現手順

リポジトリ直下から次を実行する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Issue606StarPrerequisiteNameTests' \
  -p:PythonCommand=python -p:WarningLevel=0 --logger 'console;verbosity=normal'
```

再現テストは `tests/SodRpg.Core.Tests/Issue606StarPrerequisiteNameTests.cs`。
本体コード・マニフェスト・生成コードは変更していない。

2026-10-17、Linux x64、.NET SDK 10.0.111 / runtime 10.0.11 で実行した。
この環境に .NET 8 runtime がないため、`DOTNET_ROLL_FORWARD=Major` を指定した。
指定なしの既存テスト実行は testhost 起動時に中断した（終了コード1）。
NuGetキャッシュは環境の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用した。

## 修正前の結果

**終了コード1、4件失敗・0件成功・0件スキップ。**

空殻の生成済み星図を登録し、未取得の状態でゲームのツールチップが使う
`StarMapPresentation.MissingRequirements` を呼び出す。
期待する前提名は画面の `RebuildStarTree` と `EnsureStarTooltip` と同じ
`PairCombos.ForBridge(id)?.Name ?? talent.Name` から取得する。
前提以外の不足理由を混ぜないため、隣接条件は充足扱い、残りポイントは500とする。

| 前提を必要とする星 | ID | 前提ID | 画面での前提星名 | 未取得理由の名前 |
| --- | --- | --- | --- | --- |
| 剣気の貫禄 | `husk.bridge.b2.e1` | `h.husk.ring.insight` | 剣気の追い裂き | 夢輪の叡智 |
| 気に染まる風 | `husk.bridge.b3.n2` | `h.husk.ring.insight` | 剣気の追い裂き | 夢輪の叡智 |

日本語の実際の出力例（剣気の貫禄）:

```text
前提の星『夢輪の叡智』が未取得です。
前提の星『剣気の余波』が未取得です。
前提の星『傷の巡り』が未取得です。
```

英語の同じ2ケースでも、画面の `Aura Follow-Slash` に対して未取得理由は
`Prerequisite star “Dream Ring Wisdom” is not acquired.` となり失敗する。

## 次工程に渡す確認済みの事実

- `h.husk.ring.insight` は登録後の空殻のレイアウトに存在する。
  再現テストは空殻の全取得前提IDが同じレイアウトにあることも確認してから、表示名を比較している。
- マニフェストでは `husk.bridge.b2.e1` の `anchor` / `requires` と
  `husk.bridge.b3.n2` の `requires` がこのIDを参照する。
  `src/SodRpg.Core/Game/StarClusters/Husk.Generated.cs` にも対応する参照がある。
- `HeroStarRoutes.AddRing` は `insight` の元の名前を「夢輪の叡智」としている。
- `PairCombos` の空殻の橋2は、同じ `h.husk.ring.insight` に
  「剣気の追い裂き」/ `Aura Follow-Slash` という名前を持つ。
- `src/SodRpg.Mod/DreamforgeUi.cs` の `RebuildStarTree` と `EnsureStarTooltip` は、
  橋では `PairComboDef.Name` を画面のラベル・見出しに使っている。
- `src/SodRpg.Core/Game/StarMapPresentation.Allocation.cs` の
  `MissingRequirements` 内の `Name` は `node.Talent.Name` を使っている。
  この2つの実装により、同じIDの星が画面と前提文で別の名前になる。
- 未登録・別旅人のID・生成欠落は、今回の再現で確認した不具合ではない。
  表示名の不一致を再現した。修正方法の決定・本体修正は次工程に委ねる。

## 実行済みの関連確認

- `python tools/star-manifest/validate.py`: 終了コード0。全9旅人＋共有外縁がOK。
- `python tools/star-manifest/gen_cs.py --all --check`: 終了コード0。全9旅人で failed=0、生成物の差分なし。
- 次の既存テスト: 終了コード0。全9旅人×2の18件成功。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  --no-build --no-restore \
  --filter 'FullyQualifiedName~StarMapReachabilityTests.Every_star_is_attainable_from_the_start_without_a_keystone_and_without_a_prerequisite_loop|FullyQualifiedName~StarMapReachabilityTests.No_star_lists_itself_as_a_prerequisite_and_prerequisites_never_loop' \
  --verbosity quiet
```

## 未実行

- ゲーム実機での描画・操作・スクリーンショット: この環境ではゲームを起動できないため。
  再現はゲームが使用する登録済みデータ・表示文生成関数と、画面の名前選択ロジックの比較による。
- 全テストスイート・全 `StarMapReachabilityTests`: 再現工程のため対象を絞った。
- 指紋比較テスト・MODビルド・`dreamforge_simkill`: 本体変更がなく、表示名の再現に不要なため。
- 不具合修正および修正後の合格確認: 本工程では修正禁止のため。
