# 保存と続きから / Saves and Continue

[ホーム / Home](Home) · [協力取引 / Co-op trade](Co-op-Trade) · [インフィニティ / Infinity](Infinity)

## 保存先と更新 / Save location and updates

MOD のプロフィールは次の場所に保存します。本体の遠征保存とは別です。

MOD profiles are stored separately from native expedition saves at:

```text
<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/
```

Windows の通常の場所 / Usual Windows location:

```text
%USERPROFILE%/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/QuickSave/Mods/DreamforgeRPG/
```

直前の内容は `.bak` として残ります。v3.8.4 へ更新してもセーブは引き継げます。更新後はゲームを再起動してください。

The previous contents remain as `.bak`. Saves carry over to v3.8.4; restart the game after updating.

## 中断と再開 / Suspending and resuming

「メニューに戻る」「デスクトップに戻る」は未完了の遠征を中断として残します。中断中はプロフィールの切り替えと星図の変更をロックします。

Return to menu and Return to desktop leave an unfinished expedition suspended. Profile switching and Star Map edits are locked during suspension.

本体に保存がある場合は「続きから」で再開します。MOD は同じ遠征IDのチェックポイントに合わせ、鞄や未確保素材・撃破報酬を本体の保存地点へ戻します。中断の表示だけで、再開できると保証するものではありません。

Use Continue when a native save exists. The MOD matches the same run's checkpoint and restores its satchel, unsecured materials and kill rewards to the native save point. A suspension notice alone does not guarantee resumability.

協力プレイではホストの保存地点に従い、各自にも対応する MOD チェックポイントが必要です。参加者だけでホストの遠征を再開することはできません。古い保存を読めても、存在しない過去のチェックポイントから報酬を復元することはできません。

Co-op follows the host's save point, and each player needs a matching local MOD checkpoint. Guests cannot resume the host's expedition on their own. Reading an older save cannot reconstruct rewards from a checkpoint that never existed.

## ロビーに戻る操作 / Returning to lobby

未決着の遠征で「ロビーに戻る」を確認した場合は、条件を満たすと敗北として精算します。「メニューに戻る」とは扱いが異なります。この操作で終了した遠征の本体保存を後から再開しても、その遠征の MOD 報酬は出ません。

Confirming Return to lobby during an unsettled expedition settles it as a defeat when the run can be identified correctly. This differs from Return to menu. Later continuing that ended run's native save does not grant MOD rewards for it.

## 中断した遠征の遺物 / Interrupted expedition relics

未精算の遠征が残ったまま別の遠征を始めると、前の鞄の遺物を受け取り待ちに保存します。死亡・敗北・勝利・確保帰還で精算済みの遠征は対象外です。

Starting another expedition while an unsettled one remains saves the previous satchel's relics as a pending claim. Death, defeat, victory and secured-return results are excluded.

遠征中に F6 メニュー上部の「中断した遠征の遺物を受け取る（N個）」を押すと、現在の鞄へ受け取ります。対象は直近の中断1回分で、受け取りは1遠征につき1回です。鞄があふれた場合は通常の欠片化と追加ドリームダスト設定を使います。受け取り前に元の遠征を Continue すると、その受け取り待ちはなくなります。

During an expedition, use Claim interrupted expedition relics (N) above the F6 tabs to move the latest interrupted batch into the current satchel, once per expedition. Normal shard overflow and optional Dream Dust rules apply. Continuing the source expedition before claiming cancels its pending claim.

さらに中断した場合は、古い未受け取り分を遺失物へ移します。受け取り済みの品を、古い Continue からもう一度得ることはできません。

A later interruption moves an older unclaimed batch to Lost & Found. Old Continue points cannot grant already claimed relics again.

## 交換した財産と Continue / Traded assets and Continue

プレイヤー間の交換で保存した経済状態と受領記録は Continue 後も反映します。戦闘進行が巻き戻っても、交換で渡した品の復活や二重受領は起こさない扱いです。結果未確定の予約品は元のホストへ照会して解決します。

Saved player-trade economic state and receipts still apply after Continue. Combat can rewind without restoring traded-away assets or paying a receipt twice. Unresolved reservations are checked with the original host.

根拠 / Sources: [保存先 / Save location](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/README.md), [中断・再開・中断遺物・ロビー帰還 / Resume, claims and lobby return](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/README.md), [中断遺物の処理 / Interrupted relic rules](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/Rules.InterruptedRelics.cs), [交換の保存処理 / Trade persistence](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/CoopTrade.cs).
