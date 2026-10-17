# Issue #535 再現・回帰確認

素材の吸い寄せ主経路化・協力時の自動詠唱の実効頻度・MOD 再読込時の Harmony パッチの外れを確認する。修正前の実測は下に保持し、既存ケースを修正後の距離・owner・引数注入の仕様に追従した。

```sh
python3 tests/repro/issue535/run.py
```

.NET SDK と Python 3 が必要。ゲームの起動・DLL 配置は不要。`DOTNET_ROLL_FORWARD` が未設定の場合は `Major` を使う。NuGet キャッシュは呼び出し元の環境変数を引き継ぎ、worktree 内には設定しない。

`artifacts/<UTC時刻>/repro.log` に実行出力、`results.json` にコマンド・終了コード・各ケースの結果を保存する。ケース失敗は終了コード1、ビルド失敗などでケース結果を取得できない場合は2。成果物・bin・obj はコミットしない。

## 方法と限界

- ModUnderTest は本番の `src/SodRpg.Mod/CulinaryMagnet.cs` と `CulinaryIngredientCap.cs` を直接コンパイルする。その DLL のバイト列を `Assembly.Load(byte[])` で読み込み、別の型・static 状態を持つ MOD コピーを2つ作る（`tests/SodRpg.Culinary.Tests` と同じ手法）。`AutoCastCadence.cs` は再現 exe 側で直接コンパイルし、`CulinaryPickupSnap` は Core プロジェクト経由で本番のまま使う。
- ゲームの型は `NativeApi/` の最小スタブ。実機の逆コンパイル結果に合わせてある（下部の資料確認）。Unity の物理・Mirror の同期・実際のモーターの飛行は再現しない。吸い寄せ側では「モーターが担うべき距離であること」を、上げられたモーター値（attractionRange=500、maxVelocity=120）で確認する。
- **Harmony はスタブではなく実際の Lib.Harmony** を使う。パッチ導入・所有者 ID の記録（`Harmony.GetPatchInfo`）・`Unpatch(All, owner)` の挙動・引数名での注入は実物と同じ経路で動く。本環境の .NET 10 ランタイムでは 2.3.6（ゲーム同梱は 2.3.6.0）の detour 導入に失敗して Install が fail-soft 無効化されるため、2.4.2 を使う。所有者 ID による Unpatch の一致判定は 2.x 全般で同じ文書化された挙動だが、実機の 2.3.6 そのものではない点は限界として記録する。
- 自動詠唱（§2）は triage の方針どおり **特徴付け（コード不変）** として扱う。30Hz の理想ティックでも float の時刻丸めで約 4.63 回/秒、0.0333 秒ティックでは約 4.30 回/秒と、UI の「最大5回/秒」の上限に届かないことを本番の `AutoCastCadence.Compute` の値（interval=0.2s、cap=5）と `NativeBismuthAutoCast.Scan` の間隔ゲートの同じ比較式で示す。

## 修正前の結果（2026-10-16）

実行環境: .NET SDK 10.0.111、net8.0 を Major roll-forward で実行、`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`、Lib.Harmony 2.4.2。

**8件中2件失敗・6件成功、終了コード1。コンパイル成功後のアサーション失敗で再現した。**

| ケース | 修正前の観測 |
| --- | --- |
| snap-teleports-ordinary-drops-instead-of-native-flight | 持ち主から 4m の通常ドロップが、最初のフレームでホストの transform 書き込みにより持ち主の位置へワープ（§1）。8m・15m も同様（最初の 4m で検出） |
| duplicate-mod-copies-share-one-harmony-owner | 同じ所有者 ID `DreamforgeRPG.culinaryIngredientCap` で登録された2コピーの postfix のうち、古いコピーの `Stop` が両方を外す。残ったコピーは `_enabled=true` のまま、`GetIngredientCount` が 1000（上限 40 を無視）（§3） |
| autocast-coop-cadence-quantized-below-advertised-rate（特徴付け・成功） | interval=0.2s・上限5。理想 30Hz でも float 丸めにより約 4.63 回/秒、0.0333 秒ティックで約 4.30 回/秒（§2） |
| control-magnet-raises-native-motor-values（成功） | AfterCreate が attractionRange=500・maxVelocity=120・pickupDelay=0・加速 150〜300 を設定（モーター経路は存在する） |
| control-snap-keeps-close-or-dead-owner-pickups（成功） | 2.5m と 気絶中の持ち主では transform 書き込みなし |
| control-snaps-pickup-beyond-native-reach（成功） | 600m（上限 500m 超）の置き去りは保険として snap |
| control-distinct-owner-ids-keep-second-copy-patched（成功） | 所有者 ID が異なれば古いコピーの Stop 後も新しいコピーの上限 40 が有効（原因が共有 ID） |
| control-argument-name-mismatch-fails-loud-at-patch-time（成功） | 対象メソッドの引数名が変わると `Patch` が "Parameter \"hero\" not found" で失敗（本番 Install の catch により警告＋fail-soft 無効化。静かな無効化ではない） |

## 資料確認（読み取り専用の逆コンパイル）

所在: `~/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/Dew.Contents.dll`（ilspycmd で型単位で抽出）、`~/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/PickupInstance.cs`。

- `Gem_L_Culinary.OnKill` は `CreateAbilityInstance<Ai_Culinary_Pickup>(obj.victim.position, ...)`。**食材は敵の死亡位置に湧く**ため、持ち主から 3m 超えのドロップは通常運用で頻出する（SnapDistance=3f が主経路になる根拠）。
- `Shrine_CookingPot.ConsumeIngredients(Hero)` は `TryGetStatusEffect<Se_Gem_L_Culinary_Stack>` → `effect.SetStack(0)` のみ。在庫のリセット以外の副作用（報酬・演出・保存）はない。`GetIngredientCount` は `effect.stack` を返すだけ。Issue の「在庫を減らす以外の処理をしていない」は本体側で確認できた。
- `PickupInstance` の既定値: attractionRange=6、pickupRange=0.5、pickupDelay=0.5、maxVelocity=8、加速 10〜30、variation ±20%、`Network_target` SyncVar で持ち主へ同期飛行。

## 判断したこと

- §1 の再現は「主経路化」の通常ドロップ側（4/8/15m・持ち主は生存・瞬間移動なし）を失敗ケースにした。瞬間移動検知の入力は現在の純粋 API（`ShouldSnap(bool, float)`）に存在しないため、置き去り側は 600m の対照ケースで担保する。修正で API が拡張されたら、再現ケースと対照ケースの両方が通るよう追従することを想定している。
- §2 は triage の方針（コード不変・実機確認項目）に従い、失敗ケースにしない。数値はログと本 README に記録した。
- §3 は本番どおり、ローダー由来の固定 ID を両コピーに渡して再現した。対照ケース（ID を分ける）が通ることから、原因が所有者 ID の共有にあることを分離した。
- 引数名依存（§3の付随懸念）は「導入時に例外で失敗する（fail-soft）」ことを実際の Harmony で確認した。ゲーム更新で引数名が変わっても静かに無効にはならない。

## 未実行・未解決

- 実機確認不可のため未実行: 参加者の画面で遠くの素材がどう見えるか（§1 の見え方）、自動詠唱の体感（§2）、実際のゲームでの MOD 読み込み順序で §3 が発生するか（本再現は順序を決め打ちしている）。
- ゲーム同梱 Harmony 2.3.6.0 そのものでは実行していない（本ランタイムで detour が失敗するため 2.4.2 で代用）。
- 再現工程では既存テスト一式（`SodRpg.Culinary.Tests` 等）は実行していない（その工程では製品コード・既存テストは無変更）。修正工程の実行結果は下記。

## 修正後の結果（2026-10-16）

- `python3 tests/repro/issue535/run.py`：8件成功、終了コード0。4/8/15mの通常ドロップはホスト位置書き換えなし、600mの保険移動は本体 `SyncPosition` 境界へ移動後の位置を渡す。Unityや実ソケットの同期を試験しているわけではない。
- コピーごとのowner生成に合わせ、既存ケースの「共通ownerに2パッチ」の前提を「各コピーのownerが異なる」に追従した。古いコピーのStop後も新しいコピーの上限40を維持するアサーションは保持している。
- Harmony注入を `__0` へ変更したため、引数名不一致の既存対照ケースは「導入失敗」から「改名しても本番postfixが上限40を適用」に追従した。nativeスタブの `hero` は `h`、`value` は `amount` に変更して導入成功を確認する。モーターのスタブには実DLLで確認したprivate `SyncPosition()` 境界を追加した。新規テストケースは追加していない。
- `DOTNET_ROLL_FORWARD=Major dotnet test -c Release tests/SodRpg.Culinary.Tests --nologo -v:quiet`：20件成功。
- `DOTNET_ROLL_FORWARD=Major dotnet test -c Release tests/SodRpg.Core.Tests --filter FullyQualifiedName~CulinaryPickupSnapTests --nologo -v:quiet -p:PythonCommand=python3`：2件成功。距離仕様変更により、既存300mの期待値だけ保険対象外へ追従した。
- `DOTNET_ROLL_FORWARD=Major dotnet test -c Release tests/SodRpg.Mod.AutoCast.Tests --nologo -v:quiet`：19件成功。
- `dotnet build src/SodRpg.Mod -c Release -p:GameDir="/home/wang/dev/sod-gamedata/Shape of Dreams" --nologo -v:quiet`：エラー0・警告6。参照ディレクトリにModsがないことを確認し、ゲームへの配置なし。
- revert check：製品3ファイルと仕様追従した再現2ファイルをディスクへ退避し、一時的に修正前コミット `4c01c4ca` の内容へ戻した。同じ再現コマンドで元の2件が失敗（8件中6成功・終了コード1）。修正版を戻すと8件成功・終了コード0。記録は `artifacts/revert-check-20261016/{reverted-run,restored-run}.log`。
- §2はコード不変。約4.63回/秒（平均約0.216秒）／約4.30回/秒（平均約0.233秒）は同じ観測値。各間隔が常に0.23秒以上とは限らず、float丸めとtick位相により約0.20～0.233秒になる。UIの「最大5回/秒」は上限として正しい。体感の良否は実機でのみ判断できるため、今回の実機不可方針に従い未実行と記録する。
- 参加者画面・自動詠唱の体感・実際のゲームローダー・ゲーム同梱Harmony 2.3.6.0・フル／slow suiteは未実行。実機は利用不可、Harmony 2.3.6のdetourはこのランタイムで失敗、全体試験は後続パイプラインの担当のため。
