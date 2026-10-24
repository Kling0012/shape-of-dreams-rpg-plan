# #623 霜演出のメモ化に関する改善案

2026-10-24、PR #1099 のレビュー提案 L1 を確認した。現在の実装への対応は不要とし、メモ化の前提を見直す条件を記録する。

## 現在の前提

`FrostFxLifetimePatch.Postfix` は、親の `NetworkIdentity` ごとに `Gem_R_Frost` の最初の探索結果を保持する。`Resolved = true` になった後は再探索せず、「霜なし」という結果も保持する。
対象は [`FrostFxLifetime.cs`](../../src/SodRpg.Mod/FrostFxLifetime.cs) の `ParentFrost` と `Postfix` である。

[PR #1099 の調査記録](perf-loop/20261024-0752.md) では、本体の `Dew.Core` に実行時に Gem 系コンポーネントを `AddComponent` する経路がなく、コンポーネント構成は出現時に固定されることを確認している。

## 既知の制限

外部 MOD が最初の探索後に同じ親へ `Gem_R_Frost` を `AddComponent` すると、保持した「霜なし」は更新されない。その霜演出には寿命延長が適用されず、本体の標準寿命で消える。例外や MOD 全体の機能停止は起きない。
今回の提案はこの制限への実装修正を求めていないため、再探索やキャッシュの無効化は追加しない。

## 見直す条件

本体の大型更新で Gem 系を `AddComponent` する経路が増えたら、メモ化の前提を見直す。
その際は、同じ親に対する最初の探索後に `Gem_R_Frost` が追加される経路がないかを確認する。
