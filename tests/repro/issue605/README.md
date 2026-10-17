# Issue #605 修正前の再現

```sh
python3 tests/repro/issue605/run.py
```

.NET SDK と Python 3 が必要。net8.0 の実行は `DOTNET_ROLL_FORWARD=LatestMajor`
を既定にする。この環境では SDK 10.0.111 / runtime 10.0.11 を使用した。
パッケージキャッシュは既存の `NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`
を使用し、worktree 内に変更していない。

終了コードは 0=全条件を満たす、1=再現アサーション失敗、2=ビルド等のハーネス異常。
実行ログと構造化結果を `artifacts/<UTC時刻>/repro.log` / `results.json` に保存する。
`artifacts/`・`bin/`・`obj/` は git 管理対象外。製品コードの修正は行っていない。

## Issue と #603 の関係

- #605 の本文は「スタックがあるのに一度クールダウンに入る」のみ。コメントなし。
- #603 はミストのフレッシュで「3ストックなのに1回で止まり、表示は3のまま」と報告。
  2026-10-16T21:39:45Z の所有者コメントにより、**#605 の重複としてクローズ**している。
  修正済みで閉じたものではなく、修正漏れ・再発が立証されたわけではない。
- 今回は共通の「残りストックを使えない」部分を再現する。#603 の表示が3のままの部分は未再現。

## 方法と限界

`Issue605.Repro.csproj` は製品 `src/SodRpg.Mod/NativeMemoryReuse.cs` をそのままリンクし、
実 Harmony で `MemoryReuseOnCast` / `MemoryReuseCastGate` / `MemoryReuseCooldownDisplay`
を境界 API に適用する。設定値は実 `SodRpg.Core` の `EssenceCapsBalance` を参照する。
4つの対象メソッドへのフック適用と警告なしを確認できない場合はハーネス異常にする。

`SurfaceApi.cs` は以下の本体コードで確認したチャージと最低待ち時間の関係をモデル化する。
本体ソース・DLL はコミットしない。

- `/home/wang/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/AbilityTrigger.cs:949`:
  `OnCastComplete` は CD 設定→チャージ消費→最低待ち時間設定の順に呼ぶ。
- 同 `:1149` / `:1157` / `:1162`: `OnCastCompleteSetCooldownTime` は回復時間を設定し、
  `OnCastCompleteSetCharge` は1を消費し、`OnCastCompleteSetMinimumDelay` は設定の最低待ちを設定する。
- 同 `:1214` / `:1407`: 回復時間と最低待ちは別々に進み、回復時間ゼロでチャージが回復する。
- 同 `:1502` / `:1511`: `CanBeCast` / `CanBeReserved` は正のチャージ数と最低待ちゼロを要求する。
  **チャージ回復用 CD が残っていること自体は、残りチャージの使用を禁止しない。**
- `/home/wang/dev/sod-gamedata/decompiled/sod-decomp/Dew.UI/UI_InGame_SkillButton.cs:383` / `:415`:
  maxCharges > 1 かつ残りチャージ > 0 なら、チャージ表示を用い、メイン CD の数値・塗りを消す。
- `/home/wang/.claude/work/herobal/decomp-ess/St_Q_Fleche.cs:1`:
  フレッシュにはこのキャスト消費処理のオーバーライドがなく、レベルによる追加チャージと
  条件付きのチャージ還元がある。ただしこの解析資産と報告されたゲーム版の一致は未確認。

キャストロックなし・十分なマナ・有効な所有者と対象を前提にし、倍率は1に固定する。
入力は基礎 CD 12秒・3チャージ・一斉回復3・本体最低待ち0.1秒の**合成ケース**。
CD 12秒は `docs/specs/v1.27-traveler-kits.md` のフレッシュの記録にもあるが、
チャージ数・最低待ちを含め、現行実アセット値を読み出したものではない。
3チャージは #603 の報告に合わせた。UI は値の記録だけで、描画は実行しない。
Unity、Mirror の実 RPC、技能固有のインスタンス・アニメーション・対象選択は実行しない。

## 修正前の結果（2026-10-17 JST）

`python3 tests/repro/issue605/run.py`: **終了コード1、11件中5件失敗・6件成功**。
ログ: `artifacts/20261016T234142822407Z/`。
最初の実行は内部 balance 型への参照権限でビルド失敗（終了コード2）。
ハーネスの assembly 名を既存 friend assembly に合わせて再実行し、以下を確認した。
製品コードには変更を加えていない。

| ケース | 結果 | 観測 |
| --- | --- | --- |
| パッチなし、3→2チャージ、0.15秒後 | 成功 | 回復時間11.85秒でも最低待ち0、cast/reserve=True |
| Q・W・E・R 各スロット、3→2チャージ、0.15秒後 | **4件失敗** | 最低待ち2.25秒、cast/reserve=False |
| 2チャージがあるスキルのメイン CD 表示 | **失敗** | 本体なら空欄だが、MOD が文字「2.3」・fill=0.1875 を重ねる |
| MOD の2.4秒間隔経過後 | 成功 | 回復時間9.5秒でも残り2チャージを使用可能 |
| Movement / Identity 各スロット | 成功 | 最低待ち0、残り2チャージで使用可能 |
| 単チャージ記憶の全額還元直後 | 成功 | 既存の再使用間隔は維持される |
| 最後のチャージ消費後と回復後 | 成功 | 残数0では不可、回復後3で可 |

製品上の因果は `NativeMemoryReuse.cs:40` の `OnUse` が Q/W/E/R かどうかだけを確認し、
残りチャージ数に関係なく `max(最低待ち, 基礎CD × 0.2)` を全 config に設定すること。
`MemoryReuseCooldownDisplay` も残りチャージがあってもメイン CD 表示を上書きする。
このモデルでは永久停止やフル CD 12秒のロックではなく、**2.4秒の追加ロック**を再現した。

## 関連する既存テスト

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Mod.Startup.Tests/SodRpg.Mod.Startup.Tests.csproj -c Release --filter 'FullyQualifiedName~EssenceCapsTests.MemoryReuse|FullyQualifiedName~NativeCooldownSafetyTests' --logger 'console;verbosity=normal'
```

**8件成功、終了コード0**。既存ビルド警告あり。
この既存の記憶再使用テストは非チャージ記憶のガードを確認するが、残りチャージの連続使用を検証しない。
既存の共用スタブでは `CanBeCast` / `CanBeReserved` が常に true なので、今回の再現には
最低待ち・チャージの本体条件を持つ独立ハーネスを使用した。共用スタブは変更していない。

## 判断したこと

- 「クールダウンに入らない」は、残りストックを本体の最低待ち後に使えること、および
  メイン CD 表示がその使用を妨げる見え方にならないことと解釈した。
  回復用 CD は本体が最初の消費で開始する仕様なので、回復タイマーが正であるだけを不具合にしない。
- #603 の実例と #605 の一般的な報告を合わせ、Q/W/E/R の全スロットを同じ3チャージ入力で確認した。
  旅人・記憶が不明でも共通処理の再現は可能であり、情報待ちにはしない。
- 移動チャージ付与は `HostAuthority.MovementCharges.cs:48` で本体 SkillBonus.addedCharge に委譲し、
  この再使用間隔処理の対象外。今回の対照ケースにも追加ロックは出ない。
  実際の星図から付与・同期されることを動的に証明したわけではない。
- この工程は再現のみ。受け付け審査の「#603 の修正範囲とスタック共通処理を調べる」懸念に対し、
  #603 のクローズ理由と共通処理によるロックの再現を記録した。修正対応は次工程へ引き継ぐ。

## 次工程への引き継ぎ・未実行

- 製品修正は未実行（工程指定）。再現ハーネスの失敗5件が修正前の根拠。
  単チャージ記憶のループ防止を保ったまま、複数チャージの消費・再使用・表示を検討すること。
- 「3のまま」の表示・チャージ同期、フレッシュ固有のダッシュ/還元、全チャージ技能の実ゲーム挙動:
  **未実行・未再現**。このハーネスでは3→2の正常消費をモデルにしており、該当症状を証明しない。
- Unity 実機・協力ホスト/ゲスト・`dreamforge_simkill`: **未実行**。
  ローカルゲームフォルダは `Shape of Dreams_Data` のみで、ゲーム実行ファイルがない。
- MOD 全体ビルド、全テスト、`tools/test_changed.py --all`: **未実行**。再現工程として関連箇所だけを実行。
