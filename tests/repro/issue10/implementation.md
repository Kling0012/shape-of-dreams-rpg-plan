# Issue #10 実装工程の記録

## 修正した経路

ソロのリンボ6・Infinity ONで、周期の戦闘部屋をクリアすると `InfinityRunState.Phase` は `BossDue` になる。修正前の `InfinityMode.RouteTravel` は、ボスへの移動要求を通す時点で `TryEnterBoss()` を呼び、`BossFight` を保存していた。このHarmony Prefixの後で、本体の `TravelToNode` が移動を中断・変更すると、実際の現在地は通常の部屋に残る。

この状態では `InfinityTravelRequest.cs` の `IsTravelRequestValid` が `BossFight` を拒否するため、次の移動要求が通らない。`TickNative` も現在地がボス部屋でなければボスの終了処理を進めない。通常の部屋から出られず、ボスも倒せない状態になる。本体の移動割り込みの存在は、既存仕様 `docs/specs/issue-95-infinity-mode.md` の§3（`AddTravelToNodeInterrupt`）と、`docs/specs/perf-loop/20261021-1434.md` の旅割り込みイベントの調査記録に根拠がある。本体資料とゲームDLLはこの環境では参照できず、リンボ6でどの割り込みが発火したかは特定できていない。

修正では、移動要求時の `TryEnterBoss()` と保存を取り除いた。到着前は `BossDue` を保つため、本体が移動を中断しても再試行できる。実際にボスノードへ着き、部屋がアクティブになり、遷移と保存待ちが終わった後で、既存の `TickNative` が `TryEnterBoss()` と保存を行う。移動保留後の再送も同じ `RouteTravel` を使うので、この修正が適用される。

`TickNative` には、保存待ちなどでクリア通知を見逃した場合に `room.didClearRoom` からボス撃破を取り込む処理が既にある。到着から次のTickまでにボスが倒れた場合もこの処理を使う。純白の扉からの直接到着も同じ処理で扱う。

Infinity OFFでは `RouteTravel` 冒頭から本体処理へ戻る。通常モードの移動判定は変えていない。リンボ深度、Infinity ON/OFF設定、周期、報酬、保存形式、通信形式も変えていない。

## 判断したこと

報告には停止場所とログがないため、「エリア移動」を通常部屋からボス部屋への移動も含むものとして扱った。移動要求時の状態変更には、中断後の再移動を拒否する因果関係がある。一方、ボス到着後の状態変更は既に実装されている。そのため、本体リンボへ新しいフックを足すことなく、この経路を修正した。報告された現象そのものの原因と断定するものではない。

依頼文の作業ディレクトリとブランチはサンドボックスに存在しなかった。実際のリポジトリの `work` から `ai/pub-issue-10` を作成し、そこで作業した。Serenaツールは提供されていないため、検索・読み取り・編集ツールを使用した。

テストは新規作成していない。既存のボス周期テストは、移動要求後の `BossDue` と到着後の `BossFight` を区別するよう最小限変更した。既存のIssue #10ケースには到着先の未クリア部屋とTickを補い、移動要求だけを記録するスタブを新しい確定時点に合わせた。

## 実行した検証

環境はLinux、.NET SDK 10.0.111。net8.0のテストは `DOTNET_ROLL_FORWARD=Major` で実行した。ビルド・結果は既存スクリプトで `TMPDIR`（`/srv/hddtmp`）へ出力し、`NUGET_PACKAGES` は変更していない。

- 修正前: `python3 tests/repro/issue10/run.py`。終了コード0、14成功・失敗0・スキップ0。今回も報告された現象は未再現。記録は `/srv/hddtmp/issue10-nwukhy0f/results.json`。
- 修正後: `python3 tests/repro/issue10/run.py`。終了コード0、14成功・失敗0・スキップ0。ソロ・diffLimbo・深度6/5/1の各ON/OFF、初回開始順序、開示先への移動、精算待ち後の移動、潜行時のゾーン更新を確認。記録は `/srv/hddtmp/issue10-xp82y4sl/results.json`。
- 修正後: 同コピー先のStartupプロジェクトで `dotnet test --no-build --no-restore` を対象名で絞って実行。終了コード0、20成功・失敗0・スキップ0。周期10/15/20の到着時のボス戦確定、純白の直接到着と保存待ち中の撃破、保留移動の失効、通常モードの地図と移動、Continueの次室保持、地図枯渇時の更新を確認。TRXは `/srv/hddtmp/issue10-xp82y4sl/related-results/related.trx`。
- 修正後: 同プロジェクトで `dotnet test --no-build --no-restore --filter 'FullyQualifiedName~Confirmed_room_travel_'`。終了コード0、6成功・失敗0・スキップ0。ソロのホスト選択を待たないこと、ゲストの選択・期限・切断による移動待ちを確認。TRXは `/srv/hddtmp/issue10-xp82y4sl/personal-results/personal.trx`。
- `git diff --check`。終了コード0。

修正後は計40件が成功した。修正前に失敗する再現テストは得られていないため、修正を戻す検査は実施していない。テストの成功は、本体リンボ効果・シーン読込・出口操作を含む実機での再現解消の証明ではない。

## 未実行

- 実機は未確認。ソロのリンボ6での出口操作、探索継続、ボス後の継続・帰還、OFFと他階層との実機比較は、ゲーム実行環境がないため未実行。
- 本体DLL参照のMODビルドと本体リンボ・シーン読込を含む結合確認。本体DLLと資料を利用できないため未実行。Startupハーネスで変更箇所のコンパイルは成功した。
- フルテスト、遅いテスト、`tools/test_changed.py --all`、`SODRPG_SLOW=1`。依頼に従い、変更に関連する既存ケースだけを実行した。
- yomiyasuによる文章検査。指定のスキルと検査スクリプトがこの環境にはなく、実行できなかった。日本語の記述は手動で見直した。

## 要件と審査懸念への対応

FR-1・FR-2・FR-3はいずれも、`src/SodRpg.Mod/InfinityMode.cs` の移動前の状態確定を取り除く修正で対応した。ソロのリンボ6・Infinity ONを含む既存ケースは `tests/SodRpg.Mod.Startup.Tests/Issue10LimboTravelReproTests.cs`、ボス戦を確定する時点は `tests/SodRpg.Mod.Startup.Tests/InfinityLobbyStartTests.cs` で確認した。

通常モードを壊す懸念には、OFFの早期returnのコード確認、深度6/5/1のOFF対照、既存の通常地図・移動テストで対応した。進行不能の懸念には、移動中断後に `BossFight` だけが残って再移動を拒否する経路を塞いだ。未解決なのは、実際の報告で発火した割り込みと、実機での修正結果である。
