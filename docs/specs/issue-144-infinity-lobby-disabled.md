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
