# 協力取引の承諾後中止の修正（issue #400、2026-10-14）

報告: 「申し込んだあと，承諾後，取引中止までが早すぎる．切断，移動，取り消しはしていない」。
PR #403。担当: 方針は Claude（gh-auto）、実装は GLM-5.3、試験は Issue73.Native.Tests の再現テスト2件。

## 原因

1. ホスト（`HostAuthority.CoopTrade.cs` の `TickCoopTrade`）: 無操作タイマー `ActiveAt` は承諾・提示・確認のときしか更新されない。承諾後に両者の probe（5秒ごと）が届いていても、120秒で中止していた。ホスト本体セーブ中（`CoopHostNativeSaving`）も無条件に中止していた。
2. 参加者クライアント（`ClientSession.CoopTrade.cs` の `TickCoopTrade`）: 自動取消の条件が `CoopLocalSafe` で、本体セーブ中・書き込み待ち・分解等の応答待ちといった一時的な待ちも含んでいた。セーブが一瞬走っただけで取引を取り消していた。

## 修正後の中止条件（ホスト）

`TickCoopTrade` が取引を中止するのは次の場合だけ。

| 条件 | 承諾済み・非Preparingでの扱い |
| --- | --- |
| 無操作120秒 | **中止しない**（両者の probe が続く間。出す品を考える時間を制限しない） |
| 未承諾の申し込みが120秒 | 中止（従来どおり） |
| どちらかの probe が20秒以上途絶 | 中止（従来どおり。切断・無応答の保護） |
| Preparing が120秒停在 | 中止（従来どおり） |
| どちらかが非存在・世界非安全 | 中止（従来どおり。 combat/travel への移行は申し込み・提示・確定と同じ扱い） |
| ホスト本体セーブ中 | **Preparing の間だけ中止**（確定直前の帯域を保つため） |

クライアント側の自動取消は `CoopLocalCombatSafe`（戦闘・移動中でないこと）に置き換えた。一時的な保存待ち・応答待ちは新しい操作（提示・確定）をブロックするだけで、取引を終わらせない。

## 承諾後の開放と出口（レビュー指摘への確認）

承諾済みの取引は両者の probe が続く限り無制限に開いたままになる。これで閉じ込めが起きないことは、次の3点で保たれる。

1. **承諾のみの状態では他操作をロックしない。** `CoopTradeLocked`（`ClientSession.CoopTrade.cs`）は `OwnConfirmed || Preparing || 予約（CoopTradePending）|| 決済未保存` のときだけ true で、承諾直後の品選び中は false。装備・鍛冶などの操作はそのまま使える。
2. **取消はいつでも押せる。** 交換タブの「交換を取消」ボタン（`DreamforgeUi.CoopTrade.cs`）は `!trade.Preparing` の間常に有効。承諾後・確認済みでも取消できる（相手の確認を待つ間も含む）。ボタンは一覧スクロールの前に置いてあり、長い所持品一覧の下に隠れない。
3. **ロックされる期間は時間付き。** 確認済み・Preparing・予約で他タブがロックされる間も、画面は「交換の確認中です。「交換」タブで内容を変更・取消できます。」と案内する（`DreamforgeUi.Window.cs`）。Preparing 停在は120秒、probe 途絶は20秒、参加者側の予約未解決は60秒（`CoopPendingStale`）で必ず解消する。

## 保存・確定の安全性（レビュー指摘への確認）

取引がセーブ中も開いたまま続くように変わったが、状態が動く各点の安全確認は従来どおり残っている。

- **提示・確定（参加者→ホスト）**: `SetCoopTradeOffer` は `CoopLocalSafe`（保存完了・応答待ちなしを含む）を要求。`ConfirmCoopTrade` は `CanCoopTrade` を要求。ホストは `ReceiveCoopTrade` で `msg.safe && CoopWorldSafe(caller) && !CoopHostNativeSaving` を満たさない提示・確定を拒否して中止する。
- **確定（Prepared）**: 参加者は自分の予約書き込み（`_coopPrepareSaveRevision`）が書き終わった後だけ `prepared` を送り、`safe = CoopLocalWorldSafe && !_coopNativeSaving` を添える。prepare 受領時も `CoopLocalSafe` を再確認し、満たさなければ取消する。
- **成立（コミット）**: `CommitHostCoopTrade` は唯一のコミット点で、直前に両者の `CoopWorldSafe` と `!CoopHostNativeSaving` を再確認する。満たなければ中止し、ジャーナル置換は行わない。

## 残件・未確認

- 実ゲームでの2人協力プレイによる確認は未実施（テストは `tests/Issue73.Native.Tests/Issue400TradeAbortTests.cs` の再現2件ほか全部合格）。
- 「アイテムの分類や検索欄が必要」（#400 コメント）は #407 として分離した。本修正には含めない。
