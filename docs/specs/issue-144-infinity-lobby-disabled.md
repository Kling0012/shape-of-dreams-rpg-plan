# Issue #144 ロビーで「インフィニティは無効です」と出て選べない：調査・修正

## 1. 結論
- **手元の本体 DLL（r.1.4.0.13、v2.2.0 と同じ版）ではインフィニティは無効にならない。** 実 DLL を使った起動診断で、インフィニティの本体割り込みパッチ13クラスすべてが入り、`InfinityMode.Available == true` になる。
- 報告の「ロビーで無効表示」は、(a) プレイヤー側でパッチ適用が一部失敗する（本体の版違い・他MOD干渉）、または (b) 同じゲームプロセス内で以前の遠征中に検査が止まった（#131/#146 が報告する種類の保存不一致・本体連携の例外）結果、`Available` がそのプロセスで false のままロビーに戻る、のどちらか。ロビー表示は最初に止めた検査の理由を1つだけ出し、**2番目以降の理由は修正前はログにも出ていなかった**。
- 修正：`InfinityMode.DisableFeature` が**異なる理由をすべて名前付きでログに出す**（同じ理由の連続は1回だけ）。ロビー表示は従来どおり最初の理由。機能を止める範囲・保存形式・Protocol は変えない。

## 2. 実 DLL による起動診断（#109 の手法の再構成）
- `dotnet build src/SodRpg.Mod -c Release -p:GameDir=...` でビルドした DreamforgeRPG.dll と、`Shape of Dreams_Data/Managed/` の実 DLL（Assembly-CSharp・Dew.Core・Dew.Contents・Unity 各モジュール）を .NET コンソール（Harmony 2.3.3-thin + MonoMod.Core 1.3.6、実ディトゥール）へ読み込む。
- `NativePatchPreflight.Validate`（本体DLLで実行）→ `DreamforgeMod.PatchEachClass` と同じ逐次処理（preflight の feature skip → `CreateClassProcessor(type).Patch()` → Infinity クラスのインストール確認）→ `InfinityMode.CompletePatchInstallation` を実行する。`UnityEngine.Debug.Log*` はコンソールへ転送。
- 結果（r.1.4.0.13、修正済みコード、2026-10-05）：

| 項目 | 結果 |
|---|---|
| NativePatchPreflight.Validate | 例外（後述）。Awake では警告して PatchEachClass へ。Infinity への影響なし |
| インストール成功 | 158 クラス |
| スキップ | 2 クラス（`BossNativeMovementSource`：ハーネスの Harmony 2.3.3 IL リーダーが本体メソッドの例外句を扱えない。`NativeSacrificeShieldDispatch`：CoreCLR のdetour制限。いずれもハーネス由来と推定される失敗（本体実行環境の Harmony 2.3.6・Mono とは差がある）。仮に本体でも失敗しても、そのクラスのスキップだけで Infinity への影響はない） |
| Infinity 13 クラス | すべてインストール（InfinityGenerated は Prefix+Postfix、他は1フック） |
| InfinityMode.Available / UnavailableReason | **True / 空** |

- したがって v2.2.0 + r.1.4.0.13 の組合せで、起動時のパッチ適用だけが原因でロビーから選べなくなる経路は確認できなかった。

## 3. 原因の評価
- ロビーで「インフィニティは無効です。通常モードは利用できます。」（＋理由）を出すのは `InfinityMode.UnavailableNotice` だけで、`Available == false` でだけ表示される（`DreamforgeUi` の ON ボタン無効化と注意書き、`ClientSession.ChooseInfinity` の拒否）。
- `Available` を false にするのは `DisableFeature` のみ。呼び出し経路は (1) 起動時のパッチ適用失敗（上表のとおり本DLLでは再現しない）、(2) 遠征中の保存照合・本体連携・上限の各検査、(3) ロビー開始条件の例外、の3群。v2.2.0 の同じ日に #131（無効化後に通常遠征まで止まる）・#146（報酬の再付与）が報告されており、(2) が実際に発生している環境がある。
- 修正前の `DisableFeature` は `_unavailable` フラグで早期 return するため、**最初の理由しかログ・表示に出ず、以降の理由はどこにも残らなかった**。プレイヤー環境でどの割り込み・どのパッチが原因かを特定する情報が失われていた。

## 4. 修正
- `src/SodRpg.Mod/InfinityMode.cs` `DisableFeature`：理由文字列が直前と異なるときだけ `Log.Warn("Infinity disabled; normal mode remains available. " + reason)` を出す。`UnavailableReason`（ロビー表示）は最初の理由のまま。重複する理由の連出は1回に抑える（毎フレーム検査でもログが増えない）。
- 無効化の範囲・復帰条件・保存形式・Protocol は変更しない（#131/#132 の帰還精算の回復は PR #145 で別途入っている）。

## 5. テスト
- `tests/SodRpg.Mod.Startup.Tests` は実 `InfinityMode.cs`・`ClientSession.InfinitySettings.cs`・`SafeReflection.cs` を取り込み、スタブの本体境界（PlayLobbyManager・ZoneManager・Room・Rift_RoomExit・DewPersistence・GameSettingsManager など、実メソッドシグネチャと同じ引数名）へ実 Harmony でパッチを入れる。
- `AllNativeInfinityPatchesInstallAndTheFeatureStaysAvailable`：Awake 後 `Available == true`、理由なし、Infinity 13 クラスすべてにオーナーのフック、MOD全体は起動。
- `EveryDistinctDisableReasonIsLoggedByNameAndOnlyInfinityStops`：複数の理由で無効化したとき、異なる理由がすべて名前でログに出る（修正前に失敗）。最初の理由が表示に残る。無効のままでも通常パッチ・MOD起動は維持される。
- 旧 `FixtureInfinity*` フィクスチャ（スタブ InfinityMode で代用していた #95 のテスト）は削除し、実クラスで置き換えた。

## 6. 未確認点
- プレイヤー環境のログ・本体の版は入手できていない。上記 (a)/(b) のどちらで発生したかは、修正後のログ（すべての理由が出る）で判別できる。
- 実機（Unity/Mono・Harmony 2.3.6）での起動は実行していない。実 DLL 診断は CoreCLR 上の Harmony 2.3.3-thin で行っており、本体実行環境と完全には一致しない（スキップ2クラスはその差の見本）。

## 7. v2.4.0 の「開始しても通常マップ」調査・修正

- **再現した原因**：永続する `HostAuthority` が、シーンごとに作り直される `serverActor` の Hello 判定を引き継いでいた。前の通信先で内容不一致になった同じ `DewPlayer` が次の遠征にいると、新しい Hello が届く前に拒否記録で通常モードへ戻る。修正前の開始フロー回帰は4件中この1件だけ失敗した。利用者の Player.log は未提供なので、今回の報告がこの経路だったかは未確定。
- **設定保持**：選択値は `ClientSession.InfinitySettings.cs:73-83` でプロフィールへ保存し、`:11-13` のホスト判定が同じ値を読む。本体 `PlayLobbyManager.cs:319-336` は `customData` を preferred settings にコピーして PlayGame を読み込む。MODのセッションは `DreamforgeMod.cs:59` で作られ、シーン遷移では作り直さない。
- **本体の初回経路**：`~/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/PlayGameManager.cs:67-100,174-186` → `ZoneManager.cs:981-998,1312-1332,2048-2062`。クライアントready・Hero生成後、初回 `LoadNextZone` で Infinity を開始し、`currentZone` 設定後に本体の有限グラフを生成する。Infinity専用の異なる地図形状ではなく、早期ボス入場・通常の次ゾーンを抑止して同一ゾーンを再生成する設計（[仕様](issue-95-infinity-mode.md)）。
- **生成条件**：`InfinityMode.cs:627-661` の Prefix は Available/Enabled がfalseなら本体生成を通す。Enabledでmodifier識別子が使えなければ理由付きでInfinityだけ無効化して本体生成を通す。NativeSaveAgreementがfalseなら生成を保留し、Postfixを実行しない。Postfixの `OnGenerated`（`:340-382`）はホスト・有効・復元中でない場合だけ状態を作成／接続し、プール・固定ゾーン・ノード上限の不一致や例外は理由付きでInfinityだけ無効化する。`State` の `run.Infinity ?? _initial` と初回状態接続は維持した。
- **#188／#195**：v2.4.0の#188は「受信した不一致Hello」だけで通常モードへ戻り、最初のHello未着だけでは戻らない。現在の#195は通信準備後の参加者ごとの30秒未着で戻す。現在の発動箇所は `HostAuthority.Infinity.cs:91-108`。ホスト自身・非人間は`:90`で除外する。内容照合は `HostAuthority.Hello.cs:68-74` のProtocol・内容・中断保存対応・Infinity可否で、同版の通常Helloと異なる比較方式はない。
- **修正**：`HostAuthority.Hello.cs:38-61` で通信先変更時に旧ハンドラと受信済みの承認／拒否を解除し、`HostAuthority.cs:1101-1102` から通信先がnullになる場合も同期する。シーン切替直後のSession Tick／生成がHost Tickに先行しても、`HostAuthority.Infinity.cs:55-65` で旧通信先の判定／期限を使わない。新しい参加者を未確認のまま承認せず、既存の30秒待機・報酬条件は維持する。
- **ログ**：`InfinityMode.cs:206-227` は遠征ごとの初回生成で `Infinity initial map active; run=... zone=... selected=... interval=... nodes=...`、または `Infinity initial map uses normal mode; ... reason=...` を1回出す。遠征だけの降格は既存の `Infinity stopped for this expedition; normal mode continues.`、機能の無効化は `Infinity disabled; normal mode remains available.` に理由が残る。
- **回帰の範囲**：`InfinityLobbyStartTests.cs:57-125` はロビー選択→新しいscene manager／actor→初回 `OnLateStartServer`→`LoadNextZone`→`TravelToZone`→生成→実製品の `InitializeInfinityRun`→早期ボス拒否・通常次ゾーン拒否まで通す。ソロ、対応参加者、前の拒否記録がある対応参加者、通常モードを確認する。旧テストの空生成／状態確認だけを越えたが、本体境界は `StartupGameApi.cs:120-135,321-345` のモデルであり、Unityの描画・実アセット生成・実ネットワークは未確認。製品の初回接続処理はコピーせず `ClientSession.InfinitySettings.cs:86-108` に集約してリンクする。
- **実行結果**：Releaseの本体DLL参照ビルドは成功（警告5・エラー0）。`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py` は2,800件成功・失敗0（slow対象4件は既定でskip）。別コンソールの開始フロースモークではソロ／対応参加者ありで周期15・早期ボス拒否・通常次ゾーン拒否、通常モードでボス入場・次ゾーン生成の継続と初回理由ログを確認した。スモーク用の一時プロジェクトは削除済み。

## 8. #208：通常地図が見える問題は表示契約の更新

- §7の「Infinityが通常モードへ降格する」修正とは別に、#208でInfinity中の地図を開始部屋・訪問済み・次の1部屋に限定した。開始時の全node／ボスの表示は設計Aの本体表示をそのまま使っていたためで、ノードを追加する設計Bへ変更する必要はない。[更新設計・保存と通信の互換・検証（§17）](issue-95-infinity-mode.md)。
- 現在のInfinity native/UI割り込みは31クラス。実DLL＋Harmony 2.3.6-thinのCoreCLR診断で全クラスに自ownerのフックが入り、`Available == true`。対象不足・適用／実行失敗では従来どおりInfinityだけを無効化する。§2／§5の13クラスは#144当時の診断・テスト結果であり、現在の必要数ではない。
- Protocolは23へ更新。旧版は未知nodeを表示し、非隣接次室／周期ボスの選択も異なるため同一表示・選択を保証できない。保存形式5・既存envelope項目・native node保存形式は変更しない。Unity実機の表示・協力通信は未確認。
