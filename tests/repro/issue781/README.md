# Issue #781 修正前の再現記録

ヴェスパーの遠征の鍛錬 `vesper.run.g1`（傷を糧にする）が、本体の明晰夢「魚の鱗」（LucidDream_FishScales）と組み合わせたときだけ速く溜まることを、模型シミュレーションで再現した。同じ傷（同じ絶対ダメージ列）を与えても、魚の鱗ありでは常に鍛錬が先行し、育った旅人では上限60への到達が1ゾーン早まる。本工程では製品コードを変更していない。

## 実行

```sh
DOTNET_ROLL_FORWARD=Major NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue781FishScalesGrowthReproTests' --verbosity quiet -p:WarningLevel=0 --logger 'trx;LogFileName=issue781-repro.trx' --logger 'console;verbosity=normal'
```

再現テストは `tests/SodRpg.Core.Tests/Issue781FishScalesGrowthReproTests.cs`。修正前の実行は終了コード1（Test Run Failed）、1件中1件失敗。失敗箇所は最初の想定（最大HP280）のゾーン1の比較で、`魚の鱗あり 14 なし 12（許容 ±1）`。ログは `tests/SodRpg.Core.Tests/TestResults/issue781-repro.trx`。

## 再現の構成

本体とMODの両方のコードから、次の3点をそのまま模型に写した。

1. 魚の鱗の効果（本体 `Dew.Contents.dll` を ilspycmd で逆コンパイル、2026-10-18）:
   `LucidDream_FishScales` は全ヒーローに状態 `Se_FishScales` を付ける。モンスターからの被ダメージについて `失ったHP（= damage.amount - negatedAmountByShield）` に通常敵0.3・ボス0.4（近接旅人は×0.65）を掛けた値を累積し、`StatBonus.maxHealthFlat = -累積値` で最大HPを減らす。減少はゾーン移動とノックアウトで0に戻る。導の社・祝福された導で一部戻るが、再現では使わない想定とした。
2. 鍛錬の溜まり方（MOD `src/SodRpg.Mod/HostAuthority.RunGrowth.cs` の `OnRunGrowthDamage`）:
   `RunGrowth.HealthPercent(失ったHP, hero.maxHealth)`、つまり「その瞬間の」最大HPへの比で数える。上限60・閾値11%は `tools/balance/run-growth.json` の実値（テスト内で `Build.Compute` から取得し確認）。
3. 最大HPの移り変わり: `基礎 + 13.25×段数（鍛錬の最大HP上昇） - 魚の鱗の累積減少`。鍛錬の能力値は毎フレーム反映されるため、毎ヒット後に更新する。敵の攻撃力は旅人の最大HPに連動しないため、失うHPは絶対値で固定した。

balance模型（`tools/BalanceSim/V132Model.cs`）と同じ想定（戦闘部屋で基礎最大HPの25%・ボス戦は2倍、深度0・熱度0・4ゾーン・部屋5+ボス）を使う。ヴェスパーの基礎最大HP280は `docs/specs/v1.27-sigil-scaling-review.md` の記録。700と2000は装備と星図が揃った中盤〜終盤の目安。

## 入力と結果

```text
基礎最大HP   魚の鱗   ゾーン1   2    3    4
280          なし      12    21   29   35
280          あり      14    23   31   38
700          なし      14    26   36   45
700          あり      16    29   40   50
2000         なし      15    29   42   54
2000         あり      17    34   49   60   ← 上限60に到達（なしだと54）
```

同じ傷を与えているのに、魚の鱗ありでは毎ゾーン鍛錬が先行する。基礎値280では約+9%、育った旅人では差が広がり、基礎最大HP2000では4ゾーン目に上限へ達する。あわせて、魚の鱗ありの最大HPはゾーン内で魚の鱗なしの65〜70%まで下がる（次項）。

## 判断したこと

- **方向は「溜まりすぎて強くなりすぎる」**。逆（溜まらず弱くなる）は起こらない。魚の鱗は最大HPを減らすだけなので、鍛錬の計算の分母が縮み、同じ傷がより大きな%として数え直される。魚の鱗ありの最大HPは常になしより低く保たれるため、溜まり方は単調に速くなる。
- **悪夢の代償が鍛錬に相殺される**。魚の鱗の最大HP減少は、鍛錬の効果（1段につき最大HP+13.25・防御+3.32・攻撃速度+1.32%）で大部分が埋め戻される。上限到達時には最大HP+795・防御+199・攻撃速度+79%が付く。ヴェスパーにとって魚の鱗は、他の旅人より安くつく。
- 本体側の減少が実際の `maxHealth` に反映されることは、`StatBonus.maxHealthFlat` のsetterがdirtyを立て、本体のダメージ処理（逆コンパイル `Actor.cs` の `CalculateStatsIfDirty`）で再計算されることから確認した。表示だけの値ではない。
- 本再現は模型シミュレーションである。ただし、閾値・上限・効果量・スタック累積の計算（`RunGrowthLedger.Gain`）・`RunGrowth.HealthPercent` はすべて製品コードそのもので、魚の鱗の定数だけ逆コンパイル結果を写した。
- 魚の鱗なしの列（280なら[12, 21, 29, 35]）は、本Issueの対象外である鍛錬自身の最大HP上昇による減速（設計模型の「毎部屋25%固定」より遅くなる）も含んだ実動作の記録である。修正ではこの列が変わらないことを確かめる基準に使える。

## 実行した確認

再現テスト（修正前）は終了コード1で1件失敗（上記のとおり）。ログは `TestResults/issue781-repro.trx`。逆コンパイルは `LucidDream_FishScales`・`Se_FishScales`・`FakeMaxHealthEffect`・`EntityStatus`・`Actor` の5点（`~/dev/sod-gamedata/` の `Dew.Contents.dll`・`Dew.Core.dll` を ilspycmd で出力）。環境は SDK 10.0.111・ランタイム10.0.11へ `DOTNET_ROLL_FORWARD=Major` で切り替え、`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget` を使用した（issue704の再現記録と同じ手順）。

## 未実行と未解決

- 実ゲームでの再現（魚の鱗を付けたヴェスパーの遠征）、`dreamforge_simkill`、実Harmonyパッチ、画面表示: この環境ではゲームを起動できないため未実行。
- 導の社・祝福された導による減少の戻り、ノックアウトでのリセット、協力プレイ: 模型に含めていない。戻りがあれば差は縮むが、方向（あり方が速い）は変わらない。
- ヴェスパーが近接旅人であることは武器（ハンマー）と役割から近接側（×0.65）で計算した。`Hero.classType` は本体のアセット側の値でコードからは読めなかったため、遠接だった場合の確認は未実行（遠接なら比率が0.3/0.4のままなので、差はさらに広がる）。
- 修正は次工程で行う。本工程の製品コード変更はない。
