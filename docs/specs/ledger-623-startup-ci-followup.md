# #623 起動テストの CI 失敗に関する記録

2026-10-25、[PR #1159](https://github.com/Kling0012/shape-of-dreams-rpg-plan/pull/1159) のレビュー指摘 F2 を、改善案 L1 として記録した。CI の null 参照はローカルで未再現であり、原因と対策の効果は未確定である。

## 軽量化と CI 対策の経緯

PR #1159 の本来の目的はジェム枠 HUD の点検で移動ボタンの gem group を保持し、毎秒の子走査を省くことだった。同じ PR のコミット `28996803` はそれとは別の CI 失敗への対策である。`tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs` のテスト用 `ZoneManager.GenerateWorldAuto` にコメントと `MethodImplOptions.NoInlining` の2行を追加した。ゲーム本体の Infinity 処理やテストの検証内容は変更していない。

PR 本文には、マージ前の CI で `InfinityLobbyStartTests.GenerationAddsNativeServicesWithoutConvertingEventsOrOverridingOccupiedRooms` が null 参照で失敗したことが記載されている。対策前の対象テストと関連テストはローカルで通過し、CI の失敗直前のテスト順とローカルの順を比較しても再現しなかった、という記録である。

レビュー指摘 F2 は、対策後の [CI run 40116344418](https://github.com/Kling0012/shape-of-dreams-rpg-plan/actions/runs/40116344418) が全 shard で成功したと報告している。ただし、1回の成功では不安定な失敗が解消したとは言い切れない。今回の `gh run view` は HTTP 404 を返したため、この run の結果は再確認できていない。

## 原因候補と初期化の経路

既存コードでは次の順で Infinity の状態を接続する。

1. `tests/SodRpg.Mod.Startup.Tests/InfinityServiceRoomTests.cs` の `StartServiceGraph` が `StartRevealGraph` を呼ぶ。
2. `tests/SodRpg.Mod.Startup.Tests/InfinityRevealTests.cs` の `StartRevealGraph` が `zone.GenerateWorldAuto()` を呼ぶ。
3. `src/SodRpg.Mod/InfinityMode.cs` の Harmony Postfix `InfinityGenerated.Postfix` が条件を満たすと `InfinityMode.OnGenerated` を呼ぶ。
4. `OnGenerated` が `AttachInitialToHostRun` を呼び、ホストの Run に Infinity の初期状態を接続する。
5. `StartServiceGraph` が戻り値の `session.Profile.Run.Infinity.Interval` を設定する。

PR 本文の原因候補は、小さな生成スタブが Harmony パッチの付け外しの間に呼び出し元へ JIT インライン化され、生成フックを通らず `Run.Infinity` が null のままになる経路である。`NoInlining` はこのインライン化を防ぐための指定である。この経路が CI の失敗原因だったことは確認できていない。初期化の条件や共有状態など、ほかの原因を除外した記録でもない。

## 再発時に確認する情報

同じテストや初期化経路で null 参照が再発した場合は、失敗した run の URL、コミット、shard、.NET SDK と Runtime の版、実行コマンド、TRX のスタックトレースと直前のテスト順を残す。対象テストだけの結果と、失敗した shard の順序で実行した結果を区別する。

切り分けでは `GenerateWorldAuto` のパッチ登録・解除と呼び出しの順、Postfix と `OnGenerated` を通ったか、`AttachInitialToHostRun` の条件と `Run.Infinity` の状態を確認する。原因候補と異なる結果が得られた場合も、この記録に追記する。テストが通っただけで解消済みとせず、ゲーム本体の処理を変える根拠にも使わない。

## 今回の検証と未実行

.NET SDK 8.0.426 / Runtime 8.0.32 と、worktree 外の `NUGET_PACKAGES` を使用した。以下は今回の実行結果であり、PR #1159 の過去の検証結果とは区別する。

- `/tmp/pr1159-dotnet8-YS1JU7/dotnet test tests/SodRpg.Mod.Startup.Tests -c Release --filter 'FullyQualifiedName=SodRpg.Mod.Startup.Tests.InfinityLobbyStartTests.GenerationAddsNativeServicesWithoutConvertingEventsOrOverridingOccupiedRooms' -v quiet -clp:ErrorsOnly --nologo`：1件成功、失敗・スキップなし。
- `/tmp/pr1159-dotnet8-YS1JU7/dotnet test tests/SodRpg.Mod.Startup.Tests -c Release --no-build --filter 'FullyQualifiedName~SodRpg.Mod.Startup.Tests.InfinityLobbyStartTests' --nologo`：98件成功、失敗・スキップなし。
- 未実行：対策前の CI 失敗の再現と、失敗した shard 全件の順序での実行。今回は文書だけの変更であり、対象と関連クラスに検証を絞る指示に従った。既存の `NoInlining` を外していない。
- 未実行：今回のブランチの CI、全体・低速テスト、MOD 本体のビルド。コミットまでという担当範囲と、文書だけの変更であることによる。
- 未実行：実機での HUD と Infinity の動作。この環境ではゲームを起動できない。

この変更は調査用の記録だけであり、不具合を解消したという報告ではない。更新履歴の断片は追加しない。
