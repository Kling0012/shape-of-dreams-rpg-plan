# #1171 仕掛け発動の報告の間引きテストに関する改善案

2026-10-25、PR #1171 のレビュー指摘 F1（C-II 部分未達）を確認し、対応を決めた記録である。

## 指摘の内容

この節は指摘時点の状態の記録である。

[`Issue1171GimmickReportThrottleTests`](../../tests/Issue73.Native.Tests/Issue1171GimmickReportThrottleTests.cs) は、間引きの判定と累計の取り出しを切り出した [`GimmickReportThrottle`](../../src/SodRpg.Mod/GimmickReportThrottle.cs) を、[`HostAuthority.cs`](../../src/SodRpg.Mod/HostAuthority.cs) と同じ呼び出し順をテスト内で書き直して動かしている。`StageGimmickApply` の `IsTickDue` による早期 return と、`Unhook` の即時 `FlushGimmickReports` 呼び出しという本体の実際の経路は、直接通していない。いまの差分では本体の呼び出し順がテストの再現と一致しているため不具合の筋書きはないが、将来本体の呼び出し順が変わっても、このテストでは検出できない。

## 対応したこと

レビューの裁定は「マージは止めない。本 PR での追加修正は不要。将来 HostAuthority の該当経路をテストから呼べるようになったら、本体の経路で合計が変わらないことを確かめるテストに置き換える案として、改善案の台帳に記録する」である。これに従い、実装とテストは変えずにこのファイルへ記録した。

本体の経路を直接通せない理由である。`HostAuthority.cs` 本体は `Mirror`・`UnityEngine`・`SodRpg.Core.Game` のゲーム型（`Hero`・`Entity` など）に依存し、`tests/Issue73.Native.Tests` はそれらのアセンブリを参照していない。テストプロジェクトにリンクされているのは `HostAuthority.KillSync.cs` のような依存の狭い部分クラスだけで、ティック工程（`StageGimmickApply`）とキャラの取り外し（`Unhook`）は本体側にしかない。この制約は、前ラウンドのレビューが切り出し方式（`GimmickReportThrottle`）を解決策として指示した前提である。

## 改善案の内容

HostAuthority の該当経路をテストから呼べるようになったとき（本体ファイルのリンク可能化、またはティック工程のリンク可能な単位への切り出し）、`Issue1171GimmickReportThrottleTests` が呼び出し順の再現で確かめている内容を、本体の経路を直接動かす単体テストに置き換える。確かめる点は次のとおりである。

- `StageGimmickApply` のフラッシュ経路: `IsTickDue` が送らないティックではどのランタイムの `FlushGimmickReports` も走らず、送ると判定したティックでのみ各ランタイムの保留が送信されること。
- `Unhook` の即時経路: 間引きの判定を待たずに、残り全体が送り出されること。
- 本体の経路で、1秒間引きありの送信合計（最後の即時取り出しを含む）が、毎フレーム送信の合計と一致すること。

置き換えるまでの間、現行の `Issue1171GimmickReportThrottleTests` は `GimmickReportThrottle` の意味論（間引きの判定と累計の取り出し）の検証として維持する。

## 見直す条件

`tests/Issue73.Native.Tests` が `HostAuthority.cs` 本体、または `StageGimmickApply` と `Unhook` の該当経路を含む切り出しをコンパイルできるようになったら、この改善案を実装する。
