# 星図の回避エッセンス枠が反映されない件・再読み込み後の Infinity パッチ未導入の件（2026-10-08）

利用者報告の2件（協力プレイで星図の「回避の器を広げる」の枠が増えない／ゲーム内のMOD再読み込み後、2回目の起動で Infinity の全パッチクラスが「Infinity patch was not installed」になる）の原因調査と修正。

## 1. 星図の回避（移動の記憶）エッセンス枠

### 調査した経路

- 適用はホストのみ（`HostAuthority.GemSlots.cs`。`SetMaxGemCount` は Mirror の SyncVar への代入で、参加者の画面はこの同期値を描く。参加者側で枠を書く経路は存在しない）。
- 順序の自己修復：`ApplyGemSlots` は `hero.Skill` が無いときはスキップし、`TickGemSlots`（1秒ごと）が `rt.GemSlotOwner != hero.Skill` を検出して付け直す。Build が先に届いた場合も、HeroSkill が後から生成された場合も、1秒以内に設計値へ収束することを確認した（新規試験 `Build_before_the_skill_component_heals_on_the_next_tick`）。
- 競合ラッチ：v1.31 で「3回連続の外部変更で停止するラッチ」は撤廃済みで、現行は `WarnOnceOnForeignGemSlotChange` の**警告1回のみ**（`HostAuthority.GemSlots.cs`）。参加者がホスト適用値の同期を見て誤発火して機能を止める経路は現行コードにはない。`Player-prev.log`（2.10.3、ホスト側）326行目の "Gem slot counts were changed outside this mod" はこの警告そのもので、他MOD・本体による絶対値の書き戻しを検知した際に出る（機能は止まらない）。
- Movement 限定か：本体 `Dew.Core.dll` を IL で確認すると、`HeroSkill` のコンストラクタは Q/W/E/R を 3 に初期化するだけで **Identity と Movement は 0 のまま**、かつ `SetMaxGemCount` の呼び出し元は `Se_Shrine_Chaos_StatBonus.NotifyUpdate_Corrupted` の1箇所のみ（Identity を保存カウンタの絶対値で書き直す。**Movement は本体から一切書かれない**）。つまり Movement の枠数はこのMOD（と他MOD）だけが書く。Identity は Chaos 祠の絶対値書き換えがあるため `NativeChaosGemSlotReplacement` で観測する従来構造のまま。

### 根本原因

**MODの再読み込みでアセンブリが別コピーに入れ替わると、HeroSkill 単位の台帳（`GemSlotLedgers`：static `ConditionalWeakTable`）が消える。** 再読み込み時にゾーン遷移中などで Hero が非アクティブだと、旧コピーの `Stop()`→`Detach()` は `RestoreSkillGemSlots` の「非アクティブな本体には書かない」規則で自分の +1 を戻せないまま終わる。新しいコピーは同じ HeroSkill を初回捕捉し、`NativeGemSlotLedger` のコンストラクタが**現状の枠数（自分の +1 を含む）をそのまま外部基準値として記録する**。以後、

- `Decide` はその基準値に星の分を**もう一度足す**（設計値を超える。試験では基準2+星1の設計3に対し実測4）、
- `DecideRemoval`（Detach・解除）は外部値を触れないため取り除けず、残留は恒久化し、再読み込みのたびに積もる。

このため再読み込みを挟んだ協力プレイでは、参加者・ホストを問わず、星図の枠数が設計値と一致しない状態が固定されていた。振り返し（星を返却）でも残留分は消えない。参加者側で起きやすいのは、参加者の Build が届いて適用されるタイミングと HeroSkill 生成・再読み込み後の再捕捉の順序が絡みやすいことで、本質的な原因はホスト側のこの所有権喪失にある。

### 修正

`HostAuthority.GemSlots.cs`・`GemSlotLedger.cs`：

- 所有権（自分の加算数 `OurContribution` と最終受理値 `LastWritten`、Identity/Movement 各2 int）を **AppDomain データ上の `ConditionalWeakTable<HeroSkill, int[]>` に永続化**（キーは HeroSkill の弱参照なので本体の回収を妨げない）。アセンブリコピーをまたいで同じ HeroSkill の持ち分が復元される。
- 作業台帳（コピーごとの `GemSlotLedgers`）は従来どおりで、`TrackGemSkill` が初回捕捉時に永続化値から復元する。記録された持ち分がない場合は従来どおり観測値を未知の外部基準値として扱う（**他MODが先に上げた枠は従来どおり保持**）。
- `TickGemSlots` は作業台帳の欠落を `TrackGemSkill` で自己修復する（再読み込み直後の1秒でも取りこぼさない）。
- `GemSlotLedger` に所有権復元用の internal コンストラクタを追加。

結果：再読み込み後も自分の +1 は自分の持ち分として扱われ、上乗せされず、解除で正しく外れる。古い「枠が増え続ける」回帰の試験はすべて維持し、新しく「残留→再加算されない」「残留→振り返りで設計値へ」「クリーンな再読み込み」の3試験を追加した。

## 2. 再読み込み後の「Infinity patch was not installed」（全クラス）

### 現象と状況

`Player-prev.log`（2.10.3-special）では、初回起動で 194 クラスすべて導入（150行目）後、ゲーム内のMOD再読み込みを挟んだ2回目の起動（614行目以降）で Infinity の全パッチクラス（`InfinityLobbyStartCondition` はじめ31クラス）が一斉に "Infinity patch was not installed" となり、Infinity が無効化された（648行目 "Patches installed: 163 classes, skipped 31"）。_preflight は "194 patch classes, 331 targets" で通っており、対象メソッドの解決には失敗していない。例外（"Patch class skipped"）も出ていない。

### 根本原因

`DreamforgeMod.PatchEachClass` の導入確認 `HasInstalledClass`（と `RollBackClass`）は、`patch.PatchMethod.DeclaringType` を**参照等価**で比較している。一方、Harmony 2.3.x の `PatchInfo` は `HarmonySharedState` にバイト列でシリアライズされ、`Harmony.GetPatchInfo` のたびに復号される。復号された `Patch.PatchMethod` は `moduleGUID`（ModuleVersionId）でモジュールを遅延解決し、`AppDomain.CurrentDomain.GetAssemblies()...First(m => m.ModuleVersionId == moduleGUID)` で**先に読み込まれたモジュール**を返す（`Patch.cs` の getter）。

ゲームの再読み込みは同じ DLL バイト列を別アセンブリコピーとして読み込むため、2つのコピーは同じ MVID を持つ。2回目の起動では、自分が入れたパッチの `PatchMethod` がすべて**古いコピーの型**に解決され、参照比較が不一致 → 全 Infinity クラス（確認対象は Infinity/Hunter/LobbyGuard のみで、通常クラスはこの確認を通らないためエラーに出ない）が「未導入」と誤判定され、`DisablePermanently` でスキップされた。1回目はコピーが1つしかいないため起きない。パッチ自体は実際にはすべて入っており、Infinity だけが誤って止まっていた。

### 修正

`DreamforgeMod.cs`：`IsSameOrNested` を「参照等価、または型の完全名一致かつ ModuleVersionId 一致」に変更（`RollBackClass` の自分のパッチ特定も同じ比較を使う）。再読み込みで同じ MVID の2コピーがいても、名前が一致する自クラスのパッチと判定できる。1回目の動作は不変。本当に導入できなかったクラス（`Patch()` が空を返す・例外）は従来どおりスキップ＋`DisablePermanently`／クラス単位のロールバックで安全に落ちる（`HasInstalledClassStillReportsFalseForAClassThatGenuinelyFailed` で確認）。

### 試験

`tests/SodRpg.Mod.Startup.Tests/ReloadStartupTests.cs`：同じアセンブリバイト列を2つ目のアセンブリとして読み込み、1回目の起動→破棄→2回目の起動をシミュレートする。修正前は2回目の起動で `Player-prev.log` と同じ "Infinity patch outcome … / Infinity patch was not installed …" が全クラス分出て失敗し、修正後は2回目も初回と同じ全クラス数が導入され Infinity が有効のままになる。

## 検証

- `dotnet test tests/SodRpg.Core.Tests`（EssenceSlotLifecycleTests を含む全試験）
- `dotnet test tests/SodRpg.Mod.Startup.Tests`（ReloadStartupTests を含む全試験）
- MOD の Release ビルド（ゲーム DLL 参照、配置先はゲーム外）

Unity実機・実通信での確認は行っていない（利用者の手順ではゲームを起動しない）。アセンブリ再読み込みの再現は同一バイト列の2度読み込みで代替している。

## 補足：検証中に見つかった既存の順序依存

`tools/test_changed.py` の選択（プロジェクト並列実行）で `AuthoredClusterPlacementTests` の3件が、同じプロセス内で生成星図の登録（`StarClusters.RegisterAllGenerated`）が先に走ったかどうかで間欠的に失敗した。原因は当該試験が `HeroSigils.TreeFor`（登録済みなら生成星図込みのツリー）を `GenerateAuthored` の基準に使っていたことで、製品の呼び出し元（`AuthoredStarRegistration.RegisterAuthored`）は常に `BaselineTreeFor` を渡す。試験も基準ツリーを使うように合わせ、順序に依存しないようにした。本件の2つの修正とは無関係の既存問題。

## 補足：検証環境の Python 解決

検証環境（ハーネス）によっては、bash からは `python` が実行できるのに MSBuild が起動する cmd.exe からは解決できない（Store のスタブなど）ことがあり、また `PYTHONUTF8` が設定されていないと既定の ANSI コードページ（この機械では cp932）で UTF-8 のソースを読もうとして抽出スクリプトが落ちる。`SodRpg.Core.Tests.csproj` では抽出ステップの前に `ResolvePython` を置き、ユーザー単位のインストール（`%LOCALAPPDATA%\Programs\Python`）・`py -3`・PATH の `python` の順に実際に起動して確認し、最後に成功したものを使う（PATH の python が本当に動く環境では従来どおりそれが選ばれる）。スクリプト側は読み込みも `encoding="utf-8"` を明示した。どの候補も動かなければ従来どおりビルドを失敗させる（黙って省略しない）。
