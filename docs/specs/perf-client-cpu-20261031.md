# 参加者側CPUの軽量化と無効化パッチの復帰（2026-10-31）

対象は `perf/client-cpu-20261031`。開始コミットは `317c22e7`。
利用者環境（RTX 4070 / 128GB、協力プレイの参加者側）の測定では、
90秒・3460フレームの中央フレーム時間 16.8ms、p90 59ms、p99 112ms、最悪 1303ms。
GPUの中央は 4.3ms で余裕があり、CPU 1コアが平均 68%・しばしば 93〜100% に張り付く
（メインスレッド律速）。11個の他MOD入りで、Dreamforge の軽量化パッチ4つが
「IL の不一致やほかの MOD のパッチ」で無効になっていた。

このため、今回は新しい軽量化を足すのではなく、無効になっていた4パッチの
復帰条件そのものを直した。ゲームはこの環境で起動できないため、
実機の負荷とプレイ結果は未確認（効果はすべて推定）。

4つのうち3つ（装備の再同期・詠唱テレグラフ・ボスのログ）を直した。
残る1つ（TimescaleManager）は、コードの点検で無効化の経路を特定できず、
また本体側だけで動く仕組みのため参加者側のCPU削減にならないことから見送った（下記）。

## 途中参加の装備の再同期

コミット: `7b222452`（空判定を全字段の実行時判定にする）、
`d29833a8`（本体で無効になっていたのを直す）

`NativeMidJoinLoadoutIdlePerf` は、途中参加の待機中に毎フレーム届く
「空 → 空」のロードアウト代入だけを飛ばすパッチ。このパッチには
本来の機能に届く前の段階で、新品の r1.4.1.2 でも毎回外れる不具合があった。

- weaver 生成セッターの属性名を、逆コンパイラの表示綴り
  （`Network_003CselectedLoadout_003Ek__BackingField`）で探していた。
  本体のメタデータ上の名前は括弧のまま
  （`Network<selectedLoadout>k__BackingField`）なので、検索は常に失敗し、
  パッチは他MODの有無にかかわらず無効になっていた。
  両方の綴りを試すようにした（後者はテスト境界の型が C# で宣言できる名前）。

もう1つ、空の判定を名前一覧との照合から、型の全インスタンス字段
（公開・非公開とも）から起動時にコンパイルした述語に置き換えた。

変更前の結果を保つため、次の条件と処理を維持した。

- 飛ばすのは「空 → 空」の代入だけ。内容の変わる代入（空 → 非空・非空 → 空・
  null が絡む両方向）はすべて本体のセッターに通る。
- 星のリストが null の「空」も、従来どおり両側で同じ空扱い。
- 字段が増えた・名前が変わった・減った型でも判定は組み立てられ、
  未知の字段が非既定値なら空とは判定しない。したがって飛ばしてよい範囲は
  従来より広がらない（旧来の名前一覧は、追加字段を見落として空と誤判定する
  可能性があった点で、新しい判定のほうが狭く安全）。
- 既存の `DreamforgeConfig.nativeMidJoinLoadoutIdle`（既定は有効）で切れる。
  形が合わないときの警告1回とパッチだけ外れる fail-soft も同じ。

期待効果（推定）: 途中参加の待機中、毎フレームの空再代入が weaver セッターの
SyncVar ダーティビット更新を起こさなくなり、ホスト・参加者間の無意味な
再送とその直列化が減る。量は未測定。

## 詠唱テレグラフの書き込み省略

コミット: `96c86473`

`NativeCastTelegraphIdlePerf` は、詠唱の予告線（`FxCastTelegraph.UpdatePosition`）が
毎フレーム `Transform` へ書く値が前回と同じなら書きを省くパッチ。従来は
「書き込み箇所が position 1つ・rotation 1つぴったり」という形を要求していたため、
他MODが同じメソッドに書き込みを足すと数がずれて警告1回で外れていた。

新しい判定は、書き込み箇所が1つでもあれば成立し、ある箇所すべて
（他MODが足したものを含む）に同じ値の省略を巻く。

- 省くのは「書き込む値が現在の `Transform` の値と同じ」書き込みだけなので、
  書き足したのが誰であっても、結果の見た目・タイミングは変わらない。
  値が変わる書き込みは従来どおり書く。
- 書き込み箇所が1つもないときだけ警告1回を出して外れる（fail-soft）。
- 既存の `DreamforgeConfig.nativeCastTelegraphIdle`（既定は有効）で切れる。

期待効果（推定）: 詠唱の予告線が画面にある間、参加者・ホスト双方の
メインスレッドから毎フレームの `Transform` 書き込み（Unity 側の変更検査を含む）が省ける。
他MODと共存している環境でも外れなくなった。量は未測定。

## ボスのプールログの省略

コミット: `6573ff6f`

`NativeMonsterBossPoolLogPerf` は、`Monster.SelectAbilityPool` がボスの湧き時に
`[BossPool]` のログ文字列を組み立てて出すのを省くパッチ。従来は
「守られた範囲の中に分岐ラベルが無いこと」を要求していたが、本体 r1.4.1.2 の
その範囲にはメッセージの三項演算子による分岐が初めからあり、新品の環境でも
毎回「形が変わった」と誤判定して外れていた。

新しい判定は、範囲を `ldarg.0 / isinst BossMonster / brfalse` のガードで特定し、
中身を「ログ文字列の組み立てと出力しかしていないか」で審査する。

- 許すのは読み取り（字段・引数0のプロパティ・`ToString`・等値演算子）、
  `String` の組み立て（`Format`/`Join`/`Concat`）、`Debug` の Log 系呼び出し。
- 字段や静的字段への書き込み、確保、間接書き込み、範囲の外へ出る制御
  （`ret`/`throw`/`leave` など）、範囲の後で読まれるローカルへの書き込み、
  外からの中への飛び込み、のどれか1つでもあれば不成立。
- 範囲の中の分岐は範囲の中（または直後の合流点）へ収まることを確かめる。
  文字列の組み立てを1つも含まなければ不成立。該当範囲が2つ以上なら不成立。
- 外れるときは従来どおり警告1回で、メソッドには触らない（fail-soft）。
  他MODが範囲の中に審査に通らない呼び出しを足しても、その時点で外れる。

飛ばすのはボス判定が真のときのログ出力だけ。プールの選択と
`AssignTriggerInstancesToCurrentPool` は変わらない。なお `SelectAbilityPool` は
`[Server]` のため、この省略が効くのはホスト側（ボスの湧きで毎回7要素の文字列組立と
ログ出力を省く。量は推定）。
既存の `DreamforgeConfig.nativeBossPoolSpawnLog`（既定は有効）で切れる。

## TimescaleManager.SerializeSyncVars の間引き（見送り）

4つ目の無効化報告（`NativeTimescaleIdlePerf` /
`NativeTimescaleIdlePublishPerf`）は、今回のコード点検では無効化の経路を
特定できなかった。本体 r1.4.1.2 の DLL に対して、Prepare の全前提を確認した。

- `TimescaleManager.FrameUpdate()` は引数なしで存在する。
- `SerializeSyncVars(NetworkWriter, bool)` が存在する。
- weaver セッターの属性名 `Network<effectTimescale>k__BackingField` は
  括弧のまま存在し、本体は `ldc.i8 2` を1つだけ含む（ダーティビット = 2）。
- Mirror の `syncVarDirtyBits` は getter・setter 付きのプロパティとして存在する。

また、ダーティビットの抽出に使う `PatchProcessor.GetOriginalInstructions` が
常に本体本来の命令列を返すことも、本体同梱の 0Harmony と Lib.Harmony 2.4.2 の
両方の実装（`maxTranspilers = 0`）で確かめた。他MOD がセッターに
transpiler を当てていても、この抽出の結果は変わらない。

したがって、この環境で再現・特定できる不具合はなく、利用者環境固有の
原因（実行時の例外など）は警告1行の理由の文字列を見るしかない。
次回のログで `Native performance patch disabled: timescale idle: <理由>` または
`... timescale idle publish: <理由>` の行を控えてもらえれば特定に進める。

加えて、このパッチは `NetworkServer.active` の間だけ働く本体側（ホスト側）の
仕組みで、協力プレイの参加者側では最初から何もしていない。参加者側のCPU削減という
今回の目的には効かないため、推測で条件を緩める変更はせず見送った。

## テストと検証

- 新しいテストファイルは作っていない。既存の期待値と境界の模倣だけを
  新しい判定に合わせた（`MidJoinLoadoutIdlePerfTests`・
  `CastTelegraphIdlePerfTests`、境界 `NativeBoundary.cs`・
  `NativeEmbracePositionBoundary.cs`）。ボスのプールログを直接検証する
  テストは元から無く、ビルドと本体 IL による確認で代用した。
- `python3 tools/balance/gen_cs.py --check`: 合格。
- `dotnet build src/SodRpg.Mod -c Release`（GameDir・ModDeployDir 指定）: 成功。
- `dotnet test tests/Issue73.Native.Tests`: 937 合格 / 3 スキップ / 10 不合格。
  この10件（DashSweepStep 3・CursorIdle 1・PositionSync 6）はブランチ開始点
  `317c22e7` でも同名・同数で失敗する既存の失敗であり、今回の変更による
  新規失敗はない（失敗名の一覧を開始点と突き合わせて確認）。
- `dotnet test tests/SodRpg.Mod.Startup.Tests`: 238 合格 / 1 スキップ / 0 不合格。
- `dotnet test tests/SodRpg.Mod.AutoCast.Tests`: 19 合格 / 0 不合格。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python3 tools/test_changed.py`:
  978 クラス選択、4346 合格 / 0 不合格（性能測定3件は既定のスキップ）。

## 未実行

- 実機での確認（この環境でゲームを起動できないため）:
  起動ログに上記4パッチ分の「Native performance patch disabled」が出ないこと、
  `perf.flag` による計測、協力プレイでのフレーム時間の再測定。
- Timescale の無効化原因の特定（利用者環境のログの該当行が必要）。
