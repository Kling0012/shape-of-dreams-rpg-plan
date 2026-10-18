# Issue #781 の再現と検証の記録

ヴェスパーの遠征の鍛錬 `vesper.run.g1`（傷を糧にする）と本体の明晰夢「魚の鱗」（`LucidDream_FishScales`）の組み合わせについて、本体とMODの両方のコードから仕組みを確かめ、2026-10-18 の指示による修正（鍛錬が1段増えるたびに、魚の鱗で減っている最大HPの25%を取り戻す）を検証した記録。

## 本体側のコード（Dew.Contents.dll / Dew.Core.dll を ilspycmd で逆コンパイル、2026-10-18）

- `LucidDream_FishScales` は全ヒーローに状態 `Se_FishScales` を付けるだけの入れ物（`ClientEventOnHeroAdd` → `CreateStatusEffect<Se_FishScales>`）。
- `Se_FishScales.EntityEventOnTakeDamage`: モンスターからの被ダメージについて `失ったHP（= damage.amount - negatedAmountByShield、1未満は無視）` に通常敵 `normalRatio 0.3`・ボス `bossRatio 0.4`（近接旅人 `hero.IsMeleeHero()` は `meleeMultiplier 0.65`。ヴェスパーは近接）を掛けた値を累積し、`SetAmount(GetAmount() + 増分)` を呼ぶ。
- `Se_FishScales.SetAmount(decreasedAmount)`: `_currentAmount` のほか `StatBonus.maxHealthFlat = -decreasedAmount` と `FakeMaxHealthEffect.strength` を設定する。`StatBonus.maxHealthFlat` のsetterは `_isDirty = true` を立て、本体の再計算で実際の最大HPへ反映される（下限つき）。表示だけの値ではない。
- 障壁の上限: `TakenShieldProcessor` が受けた障壁を `GetMaxShieldAmount() = victim.maxHealth × maxShieldHpRatio(1.0)`（0.5秒キャッシュ）で抑える。上限が現在の最大HPから決まるため、被弾で最大HPが削られると障壁の上限も下がり、最大HPが戻れば並んで戻る。
- 減少のリセット: ゾーン移動（`ClientEventOnZoneLoaded` の `isTraveling`）とノックアウトで `SetAmount(0)`。導の社・祝福された導は `guidanceRestoreMaxHpRatio 0.25`・`blessedGuidanceRestoreMaxHpRatio 0.5` で一部を戻す（`SetAmount` の公開APIを本体自身も使っている）。

## MOD側のコードと問題の仕組み

- `src/SodRpg.Mod/HostAuthority.RunGrowth.cs` `OnRunGrowthDamage`: 鍛錬の溜まりは `RunGrowth.HealthPercent(失ったHP, hero.maxHealth)`、つまり「その瞬間の」最大HPへの%で数える。ヴェスパーの鍛錬 `vesper.run.g1` の契機は `DamageTakenMaxHpPct`（失ったHP。障壁の吸収を除く）だけ（`tools/star-manifest/vesper.json`・`tools/balance/run-growth.json`。閾値11%・上限60・1段に最大HP+13.25・防御+3.32・攻撃速度+1.32%）。障壁の吸収で溜まる `ShieldAbsorbedMaxHpPct` はケトゥスの鍛錬だけで、ヴェスパーは体で受けたダメージでしか溜まらない。
- この2つを合わせると、鍛錬を溜めようとして体で受けるダメージ1回ごとに最大HPが削られ（近接なら失ったHP×0.195〜0.26）、障壁の上限も下がる。修復はゾーン移動かノックアウトまで待つしかない。よって組み合わせは「溜まりすぎて強すぎる」のではなく「溜めようとするほど急死しやすく、弱い」（2026-10-18 の判断）。
- 補足: 最大HPが減って分母が縮むため、同じ傷がより大きな%として数え直され、溜まり自体はいくらか速くなる。この観察は最初の調査（同じ傷を与える模型シミュレーションで魚の鱗ありのほうが毎ゾーン鍛錬が先行した）で確認したが、本件の直し方としては採用していない（下記）。

## 決めた修正と実装

- 溜まり方（換算・閾値）もクールダウンも変えず、鍛錬が1段増えるたびに、その時点で魚の鱗によって減っている最大HPの25%を取り戻す（2026-10-18 の指示）。
- `HostAuthority.RunGrowth.cs` の `GainRunGrowth`: `RunGrowthLedger.Gain` の返す増加段数を受け取り、`RestoreFishScalesMaxHealth` で `Se_FishScales.SetAmount(現在の削り × 0.75^増加段数)` を呼ぶ（1段ずつに分けた複利）。対象は `vesper.run.g1` だけ。魚の鱗の状態がないとき・削りが0のときは何もしない。取り戻すのは最大HPの上限だけで現在HPは回復しない。障壁の上限は本体の仕組み（`GetMaxShieldAmount` が現在の最大HPから決まる）に従って並んで戻る。fail-soft（例外では鍛錬を止めない）。
- 25% は `tools/balance/run-growth.json` の `fishScales.vesperRunG1RestorePercent` に置き、生成された `StarRankBalance.VesperRunG1FishScalesRestorePercent` から使う。内容指紋は変えない。
- 最初の案だった「`RunGrowth.HealthPercent` の分母へ魚の鱗の減少を足し戻す」修正（同じ傷で鍛錬が速く溜まりすぎるのを防ぐ方向）は、溜まり方を変えるため取りやめ、`HealthPercent` と `OnRunGrowthDamage` は本体変更前の実装に戻した。

## 実行した検証

- 再現テスト `tests/SodRpg.Core.Tests/Issue781FishScalesGrowthReproTests.cs`（4件）: 受け入れの例（最大HPが200減った状態で1段増えると50戻る）、1段ずつの複利（1回の到達で2段増えたとき 0.75²）、魚の鱗がないときは取り戻しも働かず同じ傷で変わらず溜まる、ケトゥスの鍛錬（`cetus.run.g1`・閾値9%）には付かない、溜まり方と効果量（閾値11・上限60・1段の効果）は不変。`HostAuthority.RunGrowth.cs` をスタブ境界（`KillAssistSupportNativeTests.cs` の `Se_FishScales` スタブに `SetAmount` を追加）で駆動する。実行ログは `tests/SodRpg.Core.Tests/TestResults/issue781-repro.trx`。
- 戻し確認: `RestoreFishScalesMaxHealth` の呼び出しを無効化すると受け入れ例のテストが失敗（200のまま戻らない）し、戻すと成功することを確認した。
- 周辺の既存テスト: `KillAssistSupportNativeTests`・`StarAdditionsV132Tests`・`RunGrowthBalanceTests`・`BalanceSimV132StarsTests`・`FlatGearGrowthTests`（合計59件）と `tools/tests/test_run_growth_balance.py`（指紋のバイト同一性を含む2件）、`python tools/balance/gen_cs.py --check` が成功。
- 実際のゲームDLLに対するMODのReleaseビルドが成功（`Se_FishScales.GetAmount/SetAmount`・`TryGetStatusEffect`・`CalculateStatsIfDirty` が実APIと一致することの確認を兼ねる）。

## 実行コマンド

```sh
DOTNET_ROLL_FORWARD=Major NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue781FishScalesGrowthReproTests|FullyQualifiedName~KillAssistSupportNativeTests|FullyQualifiedName~StarAdditionsV132Tests|FullyQualifiedName~RunGrowthBalanceTests|FullyQualifiedName~BalanceSimV132StarsTests|FullyQualifiedName~FlatGearGrowthTests' --verbosity quiet -p:WarningLevel=0
python3 tools/tests/test_run_growth_balance.py
python3 tools/balance/gen_cs.py --check
DOTNET_ROLL_FORWARD=Major NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget dotnet build src/SodRpg.Mod -c Release -p:GameDir="<本体データ>/Shape of Dreams" -p:ModDeployDir=<出力先>
```

環境は SDK 10.0.111・ランタイム10.0.11へ `DOTNET_ROLL_FORWARD=Major` で切り替え、`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用した（issue704の再現記録と同じ手順）。

## 未実行と未解決

- 実ゲームでの再現と確認（魚の鱗を付けたヴェスパーの遠征で、最大HPの減り方・戻り方、障壁の上限の移り変わり、体感の釣り合い）: この環境ではゲームを起動できないため未実行。
- 25% という戻し幅が十分か（急死しやすさが実感として解消されるか）、導の社・祝福された導の戻りとの併用の体感: 実機での確認が必要。
- 鍛錬の説明文（ゲーム内の星の説明）への取り戻しの記載: 共通の契機テキスト（`RunGrowth.TriggerText`）をヴェスパー専用に変える箇所がなく、今回は付けていない。
