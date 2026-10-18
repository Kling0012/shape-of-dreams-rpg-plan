# PR #822 星図の保存互換性

レビュー指摘 F2 に従い、`HeroTreeLayout.ForHero` の分岐変更が既存セーブに与える影響を確認した。

## 配置を選ぶ条件

変更前の `Links.IsTraveler` が参照する `TravelerNames` は、Vesper、Lacerta、Cetus、Yubar、Husk、Mist、Nachia、Aurena、Bismuth の9人を含む。この9人すべてに `HeroSigils.Core` の星がある。`CreateAll` がそれらを `BaselineAll` に含め、`CreateTrees` が旅人キーで分類するため、全員で `HeroSigils.HasTree` が成立する。

したがって、追加登録のない現在の旅人について、旧条件で専用配置、新条件で汎用配置になる対象はない。`Hero_Kindred` など一覧にない旅人は変更前から `GenericLayout.Value` を使う。追加登録された配置は新旧ともに最初の `TryGetRegisteredLayout` で返される。この変更で登録済み配置が汎用配置に置き換わることもない。

参照先は `Links.cs` の `TravelerNames` と `IsTraveler`、`HeroSigils.cs` の `Core`、`CreateAll`、`CreateTrees`、`HasTree`、`HeroTreeLayout.cs` の `ForHero`。

## 保存キーと接続

`HeroSigils.BaselineTreeFor` は専用星図がなければ `Content.Talents` を返す。汎用配置も同じ `Content.Talents` から作られる。`HeroTreeLayout.Create` 内の `Add` は両方の配置で `talent.Id` をそのままノードIDに使う。始点のIDは共通の `tree.start` で、割り振りを保存する星ではない。

`ProfileCodec.WriteBody` は `HeroState.Talents` のキーとランクを保存し、刻印もIDで保存する。配置の座標、配列番号、接続は保存しない。`ReadBody` は `Content.TryGetTalent(heroKey, id, ...)` で同じ定義を解決し、ランクと刻印を復元する。専用星図のない旅人の検索は、変更前から旅人キーを `null` にして汎用定義を引く。

読み込み末尾には接続の検証もある。汎用配置はすべての星を始点へ直接つなぐため、保存済みの汎用IDが配置から消えたり、接続の不足で取得済みの星が孤立したりしない。今回の変更で星ID、ランク上限、費用、刻印の条件は変えていない。既存セーブの割り振りを変換する処理は不要で、追加していない。

参照先は `HeroSigils.BaselineTreeFor`、`HeroTreeLayout.Create`、`Content.TryGetTalent`、`ProfileCodec.WriteBody`、`ReadBody`、`HeroTreeLayout.AllocationsConnected`。

## 検証

次の既存テストを実行し、14件成功、失敗0件、スキップ0件だった。コンパイル時には既存のテストスタブの警告が出た。

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --nologo -p:PythonCommand=python3 --filter 'FullyQualifiedName~HeroSigilTests|FullyQualifiedName~AuthoredClusterPlacementTests|FullyQualifiedName~AuthoredStarContractTests.SavedSuffixRemainsStableAfterReorderAndUsesHeroQualifiedOuterKeys|FullyQualifiedName~StarmapV118Tests.Save_load_keeps_connected_deep_ranks|FullyQualifiedName~StarLoadoutsTests.Unused_loadouts_save_legacy_bytes_and_existing_data_becomes_slot_1' --verbosity quiet
```

上記は未知の旅人の汎用定義、旅人ごとの星の制限、登録配置の優先、保存キーの安定性、専用星図の接続済み割り振りとロードアウトの保存を確認する既存ケースである。旅人一覧の9人すべてに `Core` の定義があることもソース上で照合した。旧版の実セーブから新版への読み込みの直接再現は未実行で、対応する既存ケースがなく、新規テストも追加していない。互換性の判断は上記の保存・復元経路と配置の選択条件に基づく。

全件・低速テストは作業範囲を絞る指示に従い未実行。ゲーム内の読み込みと表示は、ゲームを起動できない環境のため未実行。
