# インフィニティ / Infinity

[ホーム / Home](Home) · [保存と続きから / Saves and Continue](Saves-and-Continue)

## 開始とリンボの解禁 / Starting and Limbo unlock

遠征前のロビーで、ホストがモードとボス周期を選びます。初期設定は OFF、周期は10部屋です。遠征の途中では変更できません。

Before the expedition, the host selects the mode and boss interval in the lobby. Defaults are OFF and 10 rooms. These cannot be changed during an expedition.

リンボでインフィニティを遊ぶには、選択した旅人で通常のリンボ6をクリアする必要があります。本体に残る旅人別のクリア実績も使います。協力プレイでは、ホストがロビーで選んでいる旅人の解禁状態で判定します。参加者の実績では共有モードを判定しません。

Limbo Infinity requires a normal Limbo 6 clear with the selected traveler, including existing native progress. Co-op uses the traveler currently selected by the host in the lobby; guests' unlocks do not decide the shared mode.

## 地図とボス周期 / Map and boss intervals

地図には訪問済みの部屋と次の1部屋が見えます。周期は実際にクリアした戦闘部屋を数え、商人・イベント・再訪・ボス部屋は数えません。地図を使い切った場合は同じゾーンを再生成して続けます。

The map reveals visited rooms and one next room. Intervals count actual combat-room clears, excluding merchants, events, revisits and boss rooms. Exhausting a map regenerates the same zone to continue.

| ボス周期 / Interval | 開始時の圧段階への加算 / Initial pressure offset | 敵数への追加分 / Added enemy-count bonus | 通常の遺物報酬倍率 / Ordinary relic multiplier |
| --- | ---: | ---: | ---: |
| 10部屋 / rooms | +4 | +400% | ×2 |
| 15部屋 / rooms | +2 | +200% | ×1.5 |
| 20部屋 / rooms | +0 | +0% | ×1 |

敵数の追加分は圧による倍率への加算です。遺物倍率は高レア・限定品・固定保証をすべて同じだけ増やすものではなく、報酬の供給上限もあります。

The enemy-count bonus is added to the pressure-based multiplier. Relic multipliers do not uniformly multiply high-rarity, restricted or guaranteed rewards; supply limits still apply.

純白のように周期を満たすだけの戦闘部屋がない特殊ゾーンでは、そのゾーンの完結ボスへ進みます。実際にはクリアしていない部屋を累計へ足すことはありません。

Special zones such as Pure White may lack enough combat rooms for the interval. Their finale boss ends the segment without inventing combat clears.

## ボス後の帰還と潜行 / Return and Delve after a boss

ボスを倒した直後ではなく、魂の報酬処理が終わり、部屋がクリアされた後に選択します。

Choose after the soul reward finishes and the room clears, rather than immediately on the boss's defeat.

- **帰還**：未確保品を確保して遠征を終えます。通常の踏破とは別で、本体のエンディングや踏破実績の解禁には使いません。
- **潜行**：次のゾーンへ進みます。候補が複数あれば直前と同じゾーンを避けます。移動に失敗した回は警告し、元のゾーンを再生成します。
- **全員が倒れる**：通常の敗北として精算します。

- **Return:** secure loot and end the expedition. This is distinct from normal victory and does not unlock native endings or completion achievements.
- **Delve:** proceed to another zone, avoiding the previous zone when alternatives exist. A failed switch warns and regenerates the previous zone.
- **Everyone knocked out:** settle as a normal defeat.

## 協力プレイの選択 / Co-op choices

帰還・潜行と移動先はホストが決めます。契約・道標・出来事は各自で選びます。ホストの確定後は接続中の参加者の選択完了かスキップを最大60秒待ちます。未選択なら契約・道標なしで進み、切断した参加者は待ちません。

The host chooses Return or Delve and the destination. Each player chooses their own pact, waypoint and event. After host confirmation, progression waits up to 60 seconds for connected guests to finish or skip. Missing choices mean no pact or waypoint; disconnected guests do not hold progression.

## 利用できないとき / When unavailable

必要なパッチや実行処理に失敗した場合は、警告してインフィニティを無効にします。通常モードは利用できます。実行中の失敗で無効になった場合は、遠征を終えてロビーに戻ると回復する場合があります。起動時に必要なパッチを適用できなかった場合は再起動まで無効です。

Failed required patches or runtime operations warn and disable Infinity. Normal mode remains available. A runtime failure can recover after the expedition ends and returns to the lobby. Missing startup patches keep Infinity disabled until restart.

根拠 / Sources: [Infinity settings](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/ClientSession.InfinitySettings.cs), [InfinityRunState](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/InfinityRunState.cs), [InfinityIntervalScaling](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Core/Game/InfinityIntervalScaling.cs), [Infinity balance](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/tools/balance/infinity.json), [設計と実装記録 / Design and implementation](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/docs/specs/issue-95-infinity-mode.md), [MOD の説明 / MOD details](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/dev/src/SodRpg.Mod/README.md).
