# Issue #10 再現工程の記録

2026-11-01、修正前のコミット `4893569` で再現を試みた。結果は **NOT_REPRODUCED**。追加した再現ケース6件と既存の対象テスト8件はすべて成功した。製品コードは変更していない。ゲーム実機でのエリア移動を確認した結果ではない。

## 実行方法

```sh
python3 tests/repro/issue10/run.py
```

.NET SDK、NuGetへの接続、HDD上の既存ディレクトリを指す `TMPDIR` が必要。スクリプトはソースを `TMPDIR` にコピーし、既存のStartupテストプロジェクトを絞り込んで実行する。ビルド出力、ログ、TRX、結果JSONもコピー先に保存する。`NUGET_PACKAGES` は変更しない。

再現ケースは `tests/SodRpg.Mod.Startup.Tests/Issue10LimboTravelReproTests.cs` の `Issue10SoloLimboTravelThroughCombatCycle`。既存ハーネスにリンクされた製品のHarmonyパッチを使う。

## 入力と結果

| 入力 | 観測結果 |
| --- | --- |
| ソロ、`diffLimbo`、プロフィールのリンボ深度6、インフィニティON、選択旅人は解禁済み、周期10 | 入口から10戦闘部屋を経てボスへの移動要求11回が通り、`BossFight` に進んだ。ONの設定を維持した。 |
| 同じ条件でインフィニティOFF | 同じ11回の移動要求が通った。Infinity状態は作られなかった。 |
| リンボ深度5・1、各ON/OFF | 対照4ケースでも同じ11回の移動要求が通った。 |
| 既存の開始順序、開示先への移動、精算待ち後の移動、潜行時のゾーン更新 | 対象8件が成功した。 |

追加ケースでは、各部屋のクリア通知後に `CmdTravelToNode` を呼ぶ。移動先、実行回数、Infinityの有効状態、意図しない境界選択の有無を検査する。最初から報酬精算済み・保存待ち解除の条件を与えており、本物の報酬処理による待機の発生は再現していない。

最終実行の環境と記録は次のとおり。

- Linux、.NET SDK 10.0.111、ランタイム10.0.11。net8.0のテストを `DOTNET_ROLL_FORWARD=Major` で実行した。
- コマンド: `python3 tests/repro/issue10/run.py`
- 終了コード: **0**。成功14件、失敗0件、スキップ0件。
- ログ: `/srv/hddtmp/issue10-ebzukwm5/test.log`
- 結果: `/srv/hddtmp/issue10-ebzukwm5/results.json`
- TRX: `/srv/hddtmp/issue10-ebzukwm5/results/issue10.trx`

追加ケースだけの先行実行も成功6件、終了コード0だった。TRXは `/srv/hddtmp/issue10-results/repro/issue10.trx`。既存ケースだけの実行は成功8件、終了コード0。TRXは `/srv/hddtmp/issue10-results/baseline/baseline.trx`。

これらは期待動作を満たした結果であり、修正前に失敗する再現テストは得られていない。

## 次工程で使うコード上の事実

- `src/SodRpg.Mod/ClientSession.InfinitySettings.cs:17` は、選択旅人のMODの解禁フラグ、または本体のリンボ6踏破実績で開始可否を判断する。再現ケースではMODの解禁フラグを使った。
- `src/SodRpg.Mod/InfinityMapReveal.cs:212` の `InfinityTravelCommand` は、Infinity中の移動要求を開示済みの移動先で検証して、投票または `TravelToNode` へ送る。この判定にはリンボ深度の直接参照がない。
- `src/SodRpg.Mod/InfinityTravelRequest.cs:31` は、復元中、遷移中、未解決の選択、進行フェーズ、部屋の実クリア、開示先などで移動を拒否する。`src/SodRpg.Mod/InfinityMode.cs:681` は、さらに保存・報酬・個人選択の精算待ちなら移動を保留する。
- `src/SodRpg.Mod/InfinityMode.cs:1146` は、Infinity中の通常の `TravelToZone` を拒否し、初回または許可された技術更新だけを通す。本体リンボがどの経路で移動するかは、このハーネスでは検証できていない。
- `src/SodRpg.Mod/ClientSession.cs:782` は本体の `GameMod_Limbo.depth` を読むが、このファイルと本体の `GameMod_Limbo` はStartupハーネスに含まれない。再現ケースの深度は `RunState.LimboDepth` へ入力しただけで、本体の深度別効果を動かしていない。
- `tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs:785` の `TravelToNode` は、移動先と回数を記録するだけ。再現ケースは到着を `SetCurrentNodeIndexAndRevealAdjacent` で別途表現する。シーン読込、Unityの部屋生成、出口の操作は対象外。
- `tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs:699` の投票要否はテスト用の値であり、本体のソロ判定処理ではない。

本体逆コンパイル資料は、案内された `~/dev/sod-gamedata/decompiled/` には存在せず、`/home/wang/dev/sod-gamedata/decompiled/` はアクセス不可だった。現在の環境にはゲームDLLとreflection-dumpも見つからず、本体処理と製品パッチの相互作用は未検証。これらの条件から特定の原因を断定していない。

## 判断したこと

この工程は再現だけを行うという手順5を優先し、修正は次工程に残した。「エリア移動」は、まず出口から送られる移動要求と、ボス後のゾーン更新を対象と解釈した。再現ハーネスが既に扱う経路で、通常モードとの比較もできるため。

審査で挙がった通常モードの退行懸念には、OFFの対照ケースと既存の移動ケースで比較材料を残した。ただし、製品の修正も実機での保証も行っていない。進行不能の原因特定と解消は未完了。FR-1・FR-2・FR-3はいずれも修正工程での対応が必要で、この工程では達成済みと数えない。

## 環境での失敗と対処

最初の既存テスト実行は終了コード1。`--artifacts-path` に絶対パスを指定すると、既存のMSBuild抽出ターゲットがプロジェクトのパスを先頭に足し、`Esscap.Native.cs` と `CooldownIdle.Native.cs` がコンパイラの参照先に作られなかった（CS2001）。生成されたファイルはHDDへ移動した。

HDDへソースをコピーした次の実行も終了コード1。`tools/balance/essence-caps.json` のコピー漏れでMSB3030になった。必要なデータを揃えた後、対象テスト8件が成功した。保存したスクリプトはこのデータもコピーする。いずれも報告された進行不能の再現ではない。

## 未実行

- 実機は未確認。ゲーム起動、ソロのリンボ6での出口操作、探索継続、`dreamforge_simkill` はゲーム実行環境がないため未実行。通常モード・他の深度の実機確認も同じ理由で未実行。
- 本体DLL付きのMODビルド、本体リンボ効果・出口UI・シーン読込・実際の保存／報酬処理との結合確認。必要な本体資料と実行環境を利用できないため未実行。
- 全テスト、遅いテスト、Python unittest一式、`tools/test_changed.py`。対象の再現ケースと関連する既存ケースに限定したため未実行。
- yomiyasuによる文章検査。指定の `~/.claude/skills/yomiyasu/SKILL.md` と検査スクリプトがこの環境にないため実行できなかった。
- Serenaによるシンボル操作。対応ツールが提供されていないため、検索・読み取りツールを使った。
- 修正および修正後の検証。再現工程の対象外のため未実行。次工程は、未再現を停止理由にせず、上記の制約とコード上の事実から解析を続ける。
