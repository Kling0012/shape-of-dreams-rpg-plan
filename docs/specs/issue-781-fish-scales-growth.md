# 魚の鱗とヴェスパーの鍛錬の組み合わせの修正（Issue #781）

2026-10-18の報告「ヴェスパーの鍛錬の効果が、悪夢の『魚の鱗』と相性が悪すぎる」に対して決めた修正と理由。検証の記録は [tests/repro/issue781/README.md](../../tests/repro/issue781/README.md) を参照。

## 確かめたこと（本体の逆コンパイル）

本体の明晰夢 `LucidDream_FishScales`（`Dew.Contents.dll` を ilspycmd で逆コンパイル、2026-10-18）は全ヒーローに状態 `Se_FishScales` を付けるだけの入れ物である。`Se_FishScales` は次のように働く。

- **被弾のたびに最大HPを削る**。`EntityEventOnTakeDamage` はモンスターからの被ダメージについて、失ったHP（`damage.amount - negatedAmountByShield`、1未満は無視）に通常敵0.3・ボス0.4（近接旅人は×0.65。ヴェスパーは近接）を掛けた値を累積し、`SetAmount` で `StatBonus.maxHealthFlat = -累積値` を設定する。setter は `_isDirty` を立てるので、本体の再計算で実際の最大HPへ反映される（下限つき）。
- **障壁の上限も下がる**。`TakenShieldProcessor` は受けた障壁を `GetMaxShieldAmount() = victim.maxHealth × maxShieldHpRatio(1.0)`（0.5秒キャッシュ）で抑える。上限が現在の最大HPから決まるため、最大HPの減少に合わせて障壁の上限も下がり、最大HPが戻れば並んで戻る。
- 減少はゾーン移動（`isTraveling`）とノックアウトで0に戻る。導の社・祝福された導は一部を戻す。`SetAmount` は `StatBonus` と `FakeMaxHealthEffect` の両方へ反映する公開メソッドで、本体自身（導の社）も同じ経路で減少を戻す。

## 組み合わせが弱い仕組み（コード上の箇所）

- 鍛錬は体で受けたダメージでしか溜まらない: MODの `src/SodRpg.Mod/HostAuthority.RunGrowth.cs` `OnRunGrowthDamage` では、ヴェスパーの鍛錬 `vesper.run.g1` の契機は `DamageTakenMaxHpPct` だけ（`tools/star-manifest/vesper.json` の `vesper.run.g1/growth/trigger`）。数えるのは失ったHP（`damage.amount - negatedAmountByShield`）で、障壁が吸収した分は数えない。障壁の吸収で溜まる `ShieldAbsorbedMaxHpPct` はケトゥスの鍛錬だけである。
- 一方、魚の鱗は上記のとおり体で受けたダメージ1回ごとに最大HPを削り、障壁の上限も下げる。つまり鍛錬を溜めようとして体で受ければ受けるほど、最大HPと障壁の上限が下がって急死しやすくなる。修復（ゾーン移動・ノックアウト）は遠征の区切りまで待つしかない。
- 補足: 最初の調査で、最大HPが減って分母が縮むため同じ傷がより大きな%として数え直され、鍛錬の溜まり自体はいくらか速くなることも確認した。しかし1段の効果（最大HP+13.25・防御+3.32・攻撃速度+1.32%）を上回る速度で受けたダメージの危険が増えるため、組み合わせ全体としては「溜まりすぎて強すぎる」のではなく「溜めようとすると急死しやすく、弱い」側と結論した（2026-10-18 の判断）。

## 決めた修正（2026-10-18 の指示による）

- **鍛錬が1段増えるたびに、その時点で魚の鱗によって減っている最大HPの25%を取り戻す**。`HostAuthority.RunGrowth.cs` の `GainRunGrowth` が `RunGrowthLedger.Gain` の返す増加段数を受け取り、`RestoreFishScalesMaxHealth` が `Se_FishScales.SetAmount(現在の削り × 0.75^増加段数)` を呼ぶ。1回の閾値到達でまとめて段が増えたとき（`GainMultiplier` 2倍の星）も1段ずつに分けて数える。
- 取り戻すのは最大HPの上限だけで、現在HPは回復しない。残す削りは0未満にならないので、魚の鱗が削る前の最大HPを超えることはない。障壁の上限は本体が現在の最大HPから決めるため、最大HPの回復に従って並んで戻る。
- **鍛錬の溜まり方（受けたダメージの換算・閾値）もクールダウンも変えていない**。`OnRunGrowthDamage` と `RunGrowth.HealthPercent` は本体変更前の実装に戻した（最初の案だった「分母へ魚の鱗の減少を足し戻す」修正は、溜まり方を変えるため取りやめ）。
- 対象はヴェスパーの鍛錬 `vesper.run.g1` だけ。ケトゥスの鍛錬には付けない。魚の鱗の状態がないとき・削りが0のときは何もしない。失敗しても鍛錬自体は止めない（fail-soft）。
- 25% という値は mod 側の設定 `tools/balance/run-growth.json` の `fishScales.vesperRunG1RestorePercent` に置き、生成された `StarRankBalance.VesperRunG1FishScalesRestorePercent`（`src/SodRpg.Core/Game/StarRankBalance.g.cs`）を通して使う。本体には手を入れない。この値は挙動の修正であり、鍛錬の数値（閾値・上限・効果量）を変えないため、内容指紋（`ContentFingerprintRecord`）は変えない。
- ヴェスパーの鍛錬そのものは削除しない（鍛錬はヴェスパーとケトゥスにだけ残す方針、2026-10-18 決定）。

## 確認と未確認

- 確認済み: 再現テスト（`Issue781FishScalesGrowthReproTests`。受け入れの例「200減って1段で50戻る」、1段ずつの複利、魚の鱗がないときは無操作、ケトゥスの鍛錬には付かない、溜まり方と効果量の不変）が成功し、取り戻しを無効化すると失敗すること。鍛錬と通常時（魚の鱗なし）の挙動を検査する既存テスト（`RunGrowthBalanceTests`・`KillAssistSupportNativeTests`・`StarAdditionsV132Tests`・`BalanceSimV132StarsTests`・`tools/tests/test_run_growth_balance.py` など）が成功すること。実際のゲームDLLに対するMODのReleaseビルド。
- 未確認: ゲームを起動しての確認（この環境では起動不可）。25% という戻し幅の体感（実機での釣り合い）。
