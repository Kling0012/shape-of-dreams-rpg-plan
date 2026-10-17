# #700 動的難易度の状況表示と、担当外の減衰の世界別段階

## 担当外の減衰

- `tools/balance/coop.json` の `ownership.helpDecayPercentByWorld`（既定 `[0, 30, 60, 90]`）が、担当外の敵とボスの他人の持ち分へのダメージ減衰（%）を決める。係数は `1 - 減衰/100`。旧キー `firstWorldHelpFactor`・`secondWorldHelpFactor`・`helpFactor`（#601 の 100／75／50%）は廃止した。
- 添字は「そのランで何番目に入った世界か」（0始まりの累計）。画面では1始まりで「世界1」と表示する。配列の最後を超えた世界は最後の値を使う。ランの外（累計 -1）は減衰なし。
- 累計は `RunWorldCounter`（`src/SodRpg.Core/Game/RunWorlds.cs`）が数える。ホストは担当制の走査（0.25秒ごと）と減衰の計算の直前に観測する。通常モードでは本体の世界番号と世界名の組が前回と違えば1つ増やす。遷移中は見ない。新しいランIDを見たときだけ数え直す。
- インフィニティでは潜行で進む `SegmentEpoch` を基準にする（#704）。最初の区間は世界1、潜行後に次の区間へ到着するたびに1世界進む。同じゾーンへの潜行も数え、同一区間のマップ再生成やゾーン変更では増やさない。観測の間に複数区間進んだ場合は差分を加算する。`PressureStage` は使わず、本体のゾーン番号を保つ `noAdvance` の移動も変えない。通常敵とボスの他人の持ち分は同じ累計と係数を使う。
- 累計は `RunState.WorldIndex`・`WorldZoneIndex`・`WorldZoneName` に保存する。最後に数えたインフィニティ区間は省略可能な `WorldInfinitySegment` に保存し、Cloneにも引き継ぐ。中断からの再開やホストの作り直しでは保存値から続け、同じ区間を二重に数えない。区間の記録がない旧保存では、保存済みの世界累計と現在の区間番号の大きい方から続ける。世界累計もない旧保存は、通常モードなら現在の世界を0、インフィニティなら現在の区間番号を初期値にする。累計はラン内で戻さず、`int.MaxValue` で止める。
- `coop_values.py` は、配列が空でないこと、各値が0〜100の整数であること、単調非減少であることを検証する。

## 現在の状況（メニュー）

- `DreamforgeUi.DrawEnemyOwnershipSettings` の設定の下に表示する。文字列は0.25秒ごとに作り直し、例外が出たらこの表示だけを止める。
- ホストは自分の規則（`HostAuthority.DynamicStatusSnapshot`・`HostDynamicDisplay`）を読む。参加者は、担当制の名簿と同じ netId 0 のメッセージに足した省略可能な `dynamicStatus` と、既存の旅人ごとの倍率だけを表示し、今のランで受け取っていない値は「—」にする。`dynamicStatus` は値が変わったときと5秒ごとの名簿の再送で送る。
- 動的難易度の状態：有効（`DynamicApplicationRunning`）、ソロでは無効（1人で「ソロでも使う」が切）、無効（それ以外）。
- 旅人ごと：倍率 ×f、敵の体力 ×f、敵の攻撃 ×f^damageExponent、報酬 ×min(rewardMaxMultiplier, 1 + rewardShare × max(0, f−1))。2026-10-17に倍率のバランス上の上限を外し、報酬上限を独立した `rewardMaxMultiplier=2.0` とした。f<1 はやさしめ、f>1 はきびしめ、1 は標準。
