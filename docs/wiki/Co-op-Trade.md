# 協力取引 / Co-op trade

[ホーム / Home](Home) · [遺物と鍛冶 / Relics](Relics-and-Forge) · [保存と続きから / Saves and Continue](Saves-and-Continue)

## 交換の手順 / Trading steps

1. F6 メニューの「交換」タブで、同じセッションの相手を選んで申し込みます。
2. 相手が「交換を承諾」を押します。
3. それぞれ遺物と欠片・調律石の数を提示します。素材の数は0以上の整数で入力します。
4. 相手の遺物名・レア度・強化値・効果と素材数を確認します。説明に「…」が出る場合は、行や「…」にカーソルを合わせると全文を読めます。
5. 双方が「この内容で交換を確認」を押します。提示を変更すると双方の確認が解除されるため、変更後の内容を確かめて再確認します。

1. Open F6 → Trade, select a player in the same session, and request a trade.
2. The other player accepts.
3. Each player offers relics and nonnegative whole amounts of shards and tuning stones.
4. Review names, rarities, enhancement levels, effects and material amounts. Hover over a row or its ellipsis to read a truncated description in full.
5. Both confirm. Any offer change clears both confirmations, so review the new offer before confirming again.

## 提示できるものと受取先 / Eligible assets and destinations

保管庫や鞄にある未装着の遺物を提示できます。別の処理の応答待ちの品や、再調律の候補を選んでいる品は提示できません。装備判定は他の旅人に装着している場合も含みます。

Offer unequipped relics from the stash or satchel. Relics awaiting another operation or a retune choice are ineligible. Equipment checks include other travelers.

欠片・調律石は鞄の所持分も使えます。受け取る遺物は保管庫へ、素材は確保済み残高へ入ります。保管庫の空きや所持上限を超える交換は成立しません。

Offers can include satchel shards and tuning stones. Received relics go to the stash and materials to secured balances. Trades cannot exceed stash capacity or ownership limits.

提示した後に装着や再調律などで遺物を提示できなくなった場合は、警告横の「無効な遺物を提示から外す」を押して続けます。この操作は提示中の欠片と調律石の数を保ちます。確認後は財産を変える操作が一時的に制限されます。

If a relic becomes ineligible after being offered, use Remove invalid relics beside the warning. The offered shard and tuning amounts are preserved. Confirmation temporarily restricts other operations that change assets.

融合済みの遺物を交換するには、ホストと交換相手の両方が融合に対応している必要があります。

Trading fused relics requires fusion support from both host and partner.

## 場所・保存・時間制限 / Safety, saving and timeouts

ロビーか遠征の安全な時機に使います。戦闘中・移動中・移動投票中は利用できません。クリア済みの部屋や確保地点で操作してください。保存や報酬の精算を待っている間も新しい操作を保留します。一時的な保存待ちだけでは承諾済みで確定前の交換を終了しません。

Trade in the lobby or safe expedition moments, such as a cleared room or secure point. Combat, travel and travel votes block trading. Saving and reward settlement also block new actions. A transient save wait alone does not end an accepted trade before finalization.

120秒の制限は、未承諾の申込・接続確認が途切れた状態・確定処理が進まない状態に適用します。承諾後に双方の接続確認が続いていれば、品物を選ぶ時間が120秒を超えても交換を維持します。戦闘や移動などで安全な状態を失った場合や、切断した場合は中止します。

The 120-second limit applies to unanswered requests, unavailable peers and stalled finalization. An accepted trade stays open beyond 120 seconds while both sides remain available and keep probing. Losing the safe state through combat or travel, or disconnecting, aborts it.

## 確定待ちと再接続 / Finalization and reconnection

「交換を確定中」はホストの成立か取消の応答を待つ状態です。双方の予約品を保存した後、ホストが両者一組の成立記録を保存してから受領を進めます。結果が分からないまま予約品を返したり、同じ品を二度受け取ったりはしません。

Finalizing waits for the host's commit or abort decision. Both outgoing reservations save before the host records the paired result and payouts proceed. Unknown results do not release reserved assets or trigger duplicate payouts.

60秒以上未確定の場合は、予約品を保持したまま結果照会を再試行します。前の取引の案内が出たら、元のホストへの再接続と所持品の空きを確認してください。結果が未確定の間は別の交換を始められません。

After an unresolved trade lasts over 60 seconds, reserved assets remain held while result queries retry. If the earlier-trade notice appears, check connection to the original host and available inventory space. Another trade cannot start while the result is unresolved.

「続きから」で戦闘が巻き戻っても、交換で渡した品や素材は復活しません。保存済みの交換記録は巻き戻し後も反映されます。

Continue may rewind combat, but does not restore assets already given away through trade. Saved trade records still apply after rollback.

根拠 / Sources: [取引画面 / Trade UI](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/DreamforgeUi.CoopTrade.cs), [ClientSession.CoopTrade](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/ClientSession.CoopTrade.cs), [HostAuthority.CoopTrade](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/HostAuthority.CoopTrade.cs), [CoopTrade rules](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/CoopTrade.cs), [交換と Continue / Trade and Continue](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/README.md).
