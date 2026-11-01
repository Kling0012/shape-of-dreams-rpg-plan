# Issue #10 再現工程の記録

2026-11-01、サンドボックスの開始コミット `19c33a5` で既存の再現スクリプトを再実行した。結果は **NOT_REPRODUCED**。対象14件が成功し、報告された進行不能は再現しなかった。今回、製品コードとテストは変更していない。

開始時点で、前回の再現ケースと理論修正が既に含まれていた。`InfinityMode.RouteTravel` は移動要求時にボス戦を確定せず、`TickNative` が到着後に確定する。そのため、今回の結果を「理論修正を入れる前の再現結果」としては扱わない。前回の実装記録は [implementation.md](implementation.md) にあるが、そこにある検証結果は今回の実行結果とは分けて扱う。

## 実行方法と結果

```sh
python3 tests/repro/issue10/run.py
```

スクリプトはソースを `TMPDIR` にコピーし、Startupテストプロジェクトを対象名で絞って実行する。ビルド出力、ログ、TRX、結果JSONはHDD上のコピー先に保存する。`NUGET_PACKAGES` は変更していない。

- 環境: Linux、.NET SDK 10.0.111。net8.0のテストを `DOTNET_ROLL_FORWARD=Major` で実行した。
- 終了コード: **0**。成功14件、失敗0件、スキップ0件。
- ログ: `/srv/hddtmp/issue10-hpesnjb2/test.log`
- 結果: `/srv/hddtmp/issue10-hpesnjb2/results.json`
- TRX: `/srv/hddtmp/issue10-hpesnjb2/results/issue10.trx`

| 入力 | 今回の観測結果 |
| --- | --- |
| ソロ、`diffLimbo`、プロフィールのリンボ深度6、Infinity ON、選択旅人は解禁済み、周期10 | 入口から10戦闘部屋を経てボスへの移動要求11回が通り、到着を表現する処理の後に `BossFight` に進んだ。ONの設定を維持した。 |
| 同じ条件でInfinity OFF | 同じ11回の移動要求が通った。Infinity状態は作られなかった。 |
| リンボ深度5・1、各ON/OFF | 対照4ケースでも同じ11回の移動要求が通った。 |
| 既存の開始順序、開示先への移動、精算待ち後の移動、潜行時のゾーン更新 | 対象8件が成功した。 |

再現ケース6件は `tests/SodRpg.Mod.Startup.Tests/Issue10LimboTravelReproTests.cs` の `Issue10SoloLimboTravelThroughCombatCycle`。既存ハーネスにリンクされた製品のHarmonyパッチを使う。各部屋のクリア通知後に `CmdTravelToNode` を呼び、移動先、実行回数、Infinityの有効状態、意図しない境界選択の有無を検査する。

期待動作を満たした結果であり、修正前に失敗する再現テストは得られていない。

## 次工程で使うコード上の事実

- `src/SodRpg.Mod/ClientSession.InfinitySettings.cs:17` は、選択旅人のMODの解禁フラグ、または本体のリンボ6踏破実績で開始可否を判断する。再現ケースではMODの解禁フラグを使う。
- `src/SodRpg.Mod/InfinityMapReveal.cs:212` の `InfinityTravelCommand` は、Infinity中の移動要求を開示済みの移動先で検証して、投票または `TravelToNode` へ送る。この判定にはリンボ深度の直接参照がない。
- `src/SodRpg.Mod/InfinityTravelRequest.cs:31` は、復元中、遷移中、未解決の選択、進行フェーズ、部屋の実クリア、開示先などで移動を拒否する。`src/SodRpg.Mod/InfinityMode.cs:681` は、さらに保存・報酬・個人選択の精算待ちなら移動を保留する。
- 現在の `src/SodRpg.Mod/InfinityMode.cs:694` は移動要求時に `BossDue` を維持する。`src/SodRpg.Mod/InfinityMode.cs:625` はボスノードのアクティブな部屋に到着してから `TryEnterBoss()` を呼ぶ。前回の理論修正が既にあることを確認したが、本体が移動を中断する経路は今回動かしていない。
- `src/SodRpg.Mod/InfinityMode.cs:1146` は、Infinity中の通常の `TravelToZone` を拒否し、初回または許可された技術更新だけを通す。本体リンボがどの経路で移動するかは、このハーネスでは検証できない。
- `src/SodRpg.Mod/ClientSession.cs:782` は本体の `GameMod_Limbo.depth` を読むが、このファイルと本体の `GameMod_Limbo` はStartupハーネスに含まれない。再現ケースの深度は `RunState.LimboDepth` への入力であり、本体の深度別効果を動かしていない。
- `tests/SodRpg.Mod.Startup.Tests/StartupGameApi.cs:785` の `TravelToNode` は移動先と回数を記録するだけ。到着は `SetCurrentNodeIndexAndRevealAdjacent` と未クリア部屋の設定、Tickで別途表現する。シーン読込、Unityの部屋生成、出口の操作は対象外。
- 再現ケースは最初から報酬精算済みの条件を与える。本物の報酬処理による待機や、本体のソロ投票判定も再現していない。

## 判断したこと

この依頼は再現工程であり、手順5に従って修正は行わない。前回のテストを再利用し、既存の理論修正も戻さずに現在の状態を確認した。サンドボックスには指定ブランチがなかったため、開始時点の `work` から `ai/pub-issue-10` を作成した。

通常モードの退行懸念には、深度6/5/1のOFF対照と既存の移動ケースで比較材料を残した。進行不能の解消とFR-1・FR-2・FR-3の達成は、今回の再現工程では認定しない。ログと停止場所の情報がないため、今回通った経路が実際の発生箇所と一致するかは不明。次工程は未再現を停止理由にせず、上記の事実から解析を続ける。

## 未実行

- 実機は未確認。ゲーム起動、ソロのリンボ6での出口操作、探索継続、`dreamforge_simkill`、OFFと他階層の実機比較は、ゲーム実行環境がないため未実行。
- 本体DLL付きのMODビルド、本体リンボ効果・出口UI・シーン読込・実際の保存／報酬処理との結合確認。ゲームDLLと逆コンパイル資料を利用できないため未実行。
- 全テスト、遅いテスト、Python unittest一式、`tools/test_changed.py`。依頼に従い、対象の再現ケースと関連する既存ケースに限定したため未実行。
- yomiyasuによる文章検査。指定のスキルと検査スクリプトは利用可能な場所に見つからず、未実行。日本語の記述は手動で見直した。
- Serenaによるシンボル操作。対応ツールが提供されていないため、検索・読み取りツールを使った。
- 製品修正、新しいテストの作成、修正を戻しての比較検証。この再現工程では行っていない。
