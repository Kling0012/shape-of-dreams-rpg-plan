# Dreamforge RPG 引き継ぎ

## 最新の引き継ぎ（2026-10-03・v1.29–v1.30 開発中）

旧3作業ツリーの成果を保全して本流へ統合し、第2回データ入力まで実施した。現在の固有品は1,171個、セット47種、合わせ技62個、通信はProtocol 9。P37を使う16個とセット1種は保留。最新のコミット・試験・ビルド先・未確認事項は [統合記録](specs/v1.30-handoff-integration.md) を参照。

以下はv1.22.0時点の過去記録。現在の規模や検証状態は上記の統合記録を優先する。

## 過去の引き継ぎ（2026-10-02・v1.22.0）

次にこのMODを触る人（人間・AIエージェント）向けの引き継ぎ。最初にこの文書、次に [開発計画](dreamforge-roadmap.md)、[保留事項](dreamforge-pending.md)、[シナジー再評価](dreamforge-synergy-review.md)、[CHANGELOG](../CHANGELOG.md) を読む。

## 1. 現在の状態

| 項目 | 状態 |
| --- | --- |
| 版 | **v1.22.0**（GitHub Releases、prerelease、zip 付き） |
| ブランチ | `claude/dreamforge-playable-v0.1`（PR #4、main へは未マージ） |
| 試験 | `dotnet test` で **600件すべて合格** |
| 規模 | 装備6枠（主装備・頭・防具・手・足・装飾品）、土台150、固有品334（一般262・セット品72）、セット24、固有効果30（v1.23 で+8予定）、特性 各枠13種、旅人の星図 最大35 |
| 実機確認 | 各タブの表示、確保地点・出来事・覚醒・鍛冶（節目・再調律の3択）は実機で確認済み。固有効果の戦闘中の手触りは利用者のプレイ待ち |
| 次 | v1.23 本体と噛み合う新しい固有効果8種、v1.24 モンスター側のつり合い（[開発計画](dreamforge-roadmap.md)） |

## 2. 方針（利用者の指示で決まったこと）

1. **これは Shape of Dreams のMOD**。単独のゲームとして要素を足さない。本体の仕組み（Memory・Essence・属性・旅人のキット・祭壇・商人・ハンター・Limbo・呪い・エリート）に乗せ、強める。新しい要素には土台にする本体APIを明記する。
2. **確認を待たずに進める**。すぐ返事がない判断は [保留事項](dreamforge-pending.md) に記録し、安全な既定で進める。ただし削除など取り返しのつかない操作はしない。
3. **計画を先に単独で push**：次に作るものを `docs/dreamforge-roadmap.md` に書き、そのコミットだけを先に push してから実装する。
4. **版ごとにアップデートノート**：`CHANGELOG.md` の先頭に追記（追加・変更・修正・試験・次）し、同じ内容で GitHub Release（タグ `vX.Y.0`、prerelease）を作り、MODの zip を添付する。
5. **複雑にしすぎず、なるべく多くを試験する**：ルールは純C#（`src/SodRpg.Core/Game`）に置き、`dotnet test` で確かめる。ゲームに触る部分（`src/SodRpg.Mod`）は薄く保つ。
6. **Workshop に登録できる形を保つ**：`about/`（metadata.json・description.txt・icon.png・preview.png）、`isAlteringGameplay = true`。他人の体験を壊すクライアント側チートや、Stardust など本体の限定資源の不正付与はしない（公式の行動規範）。
7. 会話・文書は日本語。

## 3. 構成

```
src/SodRpg.Core/Game/   ゲームのルール（純C#、netstandard2.0、試験対象）
  Ids.cs                列挙（Slot・Rarity・Line・Stat・Power・MonsterTier）
  Content.cs            装備基礎18・固有品13・セット2・特性・固有効果の値域・上限・文言
  Relic.cs Loot.cs Rng.cs   遺物・抽選・決定的な乱数（SplitMix64）
  Rules.cs              遠征（確保・潜行・精算）・鍛冶・装着・刻印・依頼・契約・出来事・工房の操作
  Build.cs              装着・刻印・セット・契約・今日の夢から「今回の強さ」を集計し通信用に符号化
  PowerRuntime.cs       固有効果の状態（スタック・タイマー・クールダウン）と単位変換・部屋カウンタ
  Bounty.cs Pact.cs DreamEvents.cs DailyDream.cs Nightmare.cs Workshop.cs HeroSigils.cs
  Profile.cs ProfileCodec.cs ProfileStore.cs   保存データ・JSON（sha256付き）・原子的保存（tmp→置換、.bak）
src/SodRpg.Mod/         ゲーム接続層（ゲームDLL参照のためCI外）
  DreamforgeMod.cs      ModBehaviour の入口、キー、ライブリロードの後片付け、確認用コンソールコマンド
  ClientSession.cs      各PC：保存、撃破・ゾーン・勝敗・本体行動のイベント → Rules、Build をホストへ送信
  HostAuthority.cs      ホスト：Build を StatBonus で反映、固有効果の発動、悪夢化（MirageSkin）、被ダメ増、呪いの付与
  DreamforgeUi.cs UiStyles.cs   IMGUI（メニュー5タブ・HUD・確保地点・結果・通知・悪夢の名札）
  NetMessages.cs Patches.cs Log.cs DreamforgeConfig.cs
tests/SodRpg.Core.Tests/   xUnit。ルール・保存・クラッシュ注入・乱数シミュレーション・各機能
docs/                   計画書の付録、開発計画、保留事項、シナジー再評価、この引き継ぎ
tools/make_about_images.py   Workshop 用画像の生成（Pillow）
```

ゲームは読み込み時にMODのアセンブリ名を書き換えるため、コアは別DLLにせず `SodRpg.Mod.csproj` がソースを取り込んで1つの `DreamforgeRPG.dll` にしている。

## 4. 本体との接点（使っているAPI）

| 用途 | 本体API |
| --- | --- |
| 能力の反映 | `EntityStatus.AddStatBonus/RemoveStatBonus/CalculateStatsIfDirty`、`StatBonus` |
| 通信 | `ActorManager.serverActor` の `CustomRpc_*`（型名で照合。型名は `Dreamforge*Msg`） |
| 撃破・被弾・命中・属性 | `ClientEventManager.OnDeath/OnTakeDamage/OnAttackHit/OnApplyElemental` |
| 4発目・スキル使用 | `Entity.EntityEvent_OnAttackFired`（`isThisAttackFourthAttack`）、`Hero.ClientHeroEvent_OnSkillUse` |
| 属性付与・CD短縮 | `Actor.ApplyElemental`、`Actor.ApplyCooldownReduction` |
| 被ダメ増 | `Entity.takenDamageProcessor`（本体の光／闇属性と同じ方式） |
| エリート | `DewResources.FindAllByType<MirageSkinEffect>()` ＋ `CreateStatusEffect` |
| 呪い | `DewResources.FindAllByType<CurseStatusEffect>()` ＋ `CreateStatusEffect`（`currentStrength` を設定） |
| ラン・ゾーン | `GameManager.runId/ambientLevel`、`ZoneManager.ClientEvent_OnZoneLoaded/OnClearedCombatRoomsChanged/OnCurrentHuntLevelChanged`、`GameResultManager.ClientEvent_OnGameConcluded` |
| Limbo | `GameMod_Limbo.depth`（`FindObjectOfType`） |
| 通貨（ホストのみ） | `DewPlayer.SpendGold/SpendDreamDust/EarnDreamDust`、`GameManager.GetAdjustedGoldAmount_Cost`（取引は `DreamforgeTradeMsg` → 結果で確定、`TradeLedger` で二重確定を防ぐ） |
| 本体の行動（依頼） | `ClientEventManager.OnChaosUsed/OnItemBought/OnItemUpgraded/OnGemMergeUpgraded/OnDismantled` |
| 入力の遮断 | `ControlManager.GetShouldProcessCharacterInputAllowKnockedOut`（Harmony）、`EventSystem.enabled` |

詳しい本体の調査は [シナジー再評価](dreamforge-synergy-review.md) と、逆コンパイル `%LOCALAPPDATA%/sod-decomp/Dew.Core`（下記）。

## 5. 開発環境（このWindows PC）

- ゲーム：`D:\app\stm\steamapps\common\Shape of Dreams`（r.1.4.0.13_s、Mono）。
- .NET SDK：システムには無い。ユーザー領域 `%LOCALAPPDATA%\dotnet-sdk`（8.0.425）。PowerShell で `$env:DOTNET_ROOT="$env:LOCALAPPDATA/dotnet-sdk"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"` を設定してから `dotnet`。
- ビルドと配置：`dotnet build src/SodRpg.Mod -c Release -p:GameDir="D:\app\stm\steamapps\common\Shape of Dreams"`（`Mods/DreamforgeRPG` へ自動配置）。
- 試験：リポジトリ直下で `dotnet test`。
- 逆コンパイル：`%LOCALAPPDATA%\sod-decomp\Dew.Core`（ilspycmd 8.2、`%LOCALAPPDATA%\dntools`）。Dew.Contents は一部のみ。
- GitHub：`gh` は winget で導入済みだが未ログイン。`git credential fill` のトークンをそのコマンドだけ `GH_TOKEN` に渡して使った（保存しない）。
- Codex CLI：`codex exec -C C:\Temp\sodreview -s read-only` でレビューを並行実行できる（Google Drive 配下は読めないので差分を `C:\Temp` に置く）。Computer Use は Codex デスクトップ専用で CLI からは使えない。Jev CU は Unity 画面に不向き。
- 実機の画面確認：`ALL/.tmp/sodtest/ui.ps1`（スクリーンショット・クリック・キー・文字入力）。**利用者がPCを使っていない時間だけ**使う。
- ゲーム設定の変更：開発者モードON、本MODを有効化（元は `QuickSave/r_platform.json.bak-dreamforge-20261001`）。テストで上書きした「夢の続き」は `QuickSave.backup-before-dreamforge-test` に全体バックアップ（復元は判断待ち）。
- 実機の確認用コマンド（開発者モードのコンソール `）：`dreamforge_status`・`_stats`・`_perf`。MOD管理の「すべてリロード」でライブリロード。

## 6. 作業の手順（1つの版）

1. `docs/dreamforge-roadmap.md` の「次」を書き換え、**そのコミットだけ** push。
2. コアを実装 → 試験を追加 → `dotnet test`。
3. MOD層を接続 → ビルド（自動配置）。
4. `src/SodRpg.Mod/README.md` と `about/metadata.json` の modVer を更新。
5. 実装をコミット → `CHANGELOG.md` に追記して別コミット → push。
6. CHANGELOG の該当節を本文にして `gh release create vX.Y.0 --prerelease`（MODフォルダの zip を添付。`--latest` は prerelease と併用不可）。
7. 必要なら Codex に差分レビューを依頼し、指摘は本体の逆コンパイルで裏取りしてから直す（Codex は「列挙型＋整数は不可」のような誤りも出す）。

## 7. 注意点・既知の制限

- **性能**：OnGUI は1フレームに何度も呼ばれる。毎回の文字列生成・GUILayout・計測（CalcSize）を避け、キャッシュして描画イベントでだけ描く（v1.4 で対処）。パネルを足すときは `DreamforgeUi.NeedsLayout` に含めないと GUILayout が呼ばれない。保存は `AsyncProfileWriter`（別スレッド）を使い、メインで `ProfileStore.Save` を直接呼ばない。

- **実機未確認の本体連動が多い**（v1.0〜v1.3）。特に MirageSkin・呪いの付与、`ClientHeroEvent_OnSkillUse` がホストで期待どおり発火するか、`ApplyElemental` の付与者の扱い。最初の実機確認で `Player.log` の `[DreamforgeRPG]` 行を見る。
- 協力時、ホストはクライアントが送る能力の集計値を上限で丸めて受け入れる（装備データ一式の検証はしない）。信頼できる仲間との協力が前提。
- 星座系統の日本語名（破壊・生命・想像）は直訳の仮置き。本体の表記に合わせる。
- 保存データは `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/profile.json`。互換を崩す変更は `ProfileCodec` で移行し、未知のIDは捨てずに注記する。
- 文字列の中で `\n` を Python のヒアドキュメントで編集すると実際の改行になって C# が壊れることがあった。編集スクリプトはファイルに書いてから実行する。
- 共有記憶（Agent Memory MCP）はこのセッションでは接続ツールが無く、保存できていない。Claude Code のローカル記憶（`~/.claude/projects/.../memory/`）には方針を保存済み。

## v1.5.1 の性能修正（2026-10-02 実機確認済み）

- 重さの原因は本体マネージャーの `.instance`（不在時に毎回 `FindObjectOfType` ×2）を毎フレーム呼んでいたこと。MOD内は必ず `.softInstance` を使う（新しいコードでも `.instance` を使わない）。
- 計測は `QuickSave/Mods/DreamforgeRPG/perf.flag` を置くと10秒ごとに Player.log へ出る。修正後：Update 約0.004ms、OnGUI 約0.006ms/回、全体 約135fps。
- 初回起動の動線を実機で確認（2026-10-02、MODプロフィールを一時退避して再現）：タイトルで「ようこそ、夢鍛へ」→「メニューを開く」で保管庫に初期遺物3つ→遠征開始で空き枠へ自動装備し「初期の遺物を装備しました」のヒント。遠征中の計測：MOD Update 約0.012ms、OnGUI 約0.07ms/回、全体110〜135fps。

## 役割分担と道具（2026-10-02 利用者指示）

- コードの実装は **OMP 経由の GPT**（`omp -p --mode json --approval-mode write --model openai-codex/gpt-6.1-sol --thinking high --cwd <クローン> "<依頼>" < /dev/null`）。Codex CLI は使わない。GLM も OMP から使える（テスト・反復向け）。
- **文章・デザイン・画面（UI）・仕様・数値の決定・確認は Claude**。
- GPT には Google Drive 外の ASCII パス（`C:\Temp\sod-*`）のクローンで作業させる。`write` モードではコマンドが実行できないので、本体APIの確認・ビルド・試験・実機確認は Claude が行い、確認済みのAPIを依頼文に書いて渡す。標準入力を `< /dev/null` で閉じないと起動待ちのまま止まる。
- バランスは `tools/BalanceSim` で確かめる（`dotnet run --project tools/BalanceSim -c Release -- --runs 30 --players 300 --seed 1`）。結果は `tools/BalanceSim/result-v1.7.md`。
- リリースのたびに GitHub の issues と PR のコメントも確認する。

## v1.8 の軽量化（2026-10-02 実機確認）

- `src/SodRpg.Mod/PerformanceTuner.cs`：設定変更・シーン読み込み・フォーカス変更のときだけ動く（毎フレームの処理なし）。本体APIは `inc8877.GraphicsConfigurator`（URPUnlocker）の `CurrentUnlockedURPAsset` 経由。
- 計測するときは、本体の垂直同期を一時的にオフにしないと60fpsに張り付いて差が見えない（`QuickSave/r_platform.json` の `"vSync"`。計測後はバックアップから戻す）。裏に回す操作は、MinimizeAll では効かず、Alt+Tab で効いた。
- 戦闘中や低性能PCでの効果は未確認。
- v1.8.1：ゲーム内の地図の移動ボタンは自動クリックに反応しなかった。戦闘部屋に入っても、敵が出る地点まで自動では進めていない。描画負荷の模擬は `dreamforge_renderscale 2`。
- 2026-10-02 同じ場所で10秒ごとに Off/Max を切り替えた計測（CPU 2コア制限、敵なし）：平均 Off 約10.5ms / Max 約10.4ms（ほぼ同じ）、10秒ごとの最大 Off 30〜52ms / Max 26〜33ms。CPUが弱い環境では軽量化は平均にほとんど効かず、引っかかりを少し抑える程度。CPU側の重さは本体の処理が主因。`perfPressureStrengthOverride`（本体の適応的なエフェクト間引き）は、表示中のエフェクトを毎フレーム確率で消すため、常時有効にはしない。
- 実機での確認のコツ（2026-10-02）：Mist の回避（Movement）は Space、右クリックは Q（RMB）のスキル。コンソールで `dreamforge_stats` を出す手順は時間がずれるので、一時補正の確認はホストの処理にログを仕込んで見る方が確実。
- 協力プレイの前提（2026-10-02 利用者決定）：参加者全員が同じ版のMODを読み込んでいる。版の違う相手との通信の互換は考えない（通信メッセージは版ごとに自由に変えてよい）。
- 名前（2026-10-02 利用者）：MOD名は「Dreamforge」に統一する。「夢鍛」は使わない（日本語の画面でも Dreamforge と表記し、パネルなどは役割で呼ぶ）。
- v1.13.1（issue #5・#6・#7 の修正、2026-10-02）：取引の返事待ちの間は確保・潜行・契約を止め、支払い済みの対価は `Rules.GrantPaidDustShards` / `GrantPaidMerchant` で状態に関係なく渡す。分解は `TradeLedger.IsReserved` で予約し、成功の返事で初めて鞄から取り除く（失敗・送信不能・30秒で予約解除。30秒後の遅い返事は無視するので、まれに二重取りが起きうる）。参加者は確認済みでも30秒ごとに Build を送り直し、ホストは同じ内容なら付け直さずに確認だけ返す。2台での確認は未実施。
- 画像（v1.14）：遺物のアイコンは `src/SodRpg.Mod/icons/<土台id>.png`（Codex の画像生成、透明背景、1024→128に縮小。元画像は C:\Temp\sod-art\icons）。出来事の挿絵は `icons/events/<DreamEvent名>.png`（Lab の ComfyUI・Qwen Image 2.1 で約14秒/枚、暗い背景を透明に切り抜き）。比べた結果、アイテムのアイコンは Codex の方が質が高く透明背景も作れるので Codex、量が要る挿絵は速い Lab を使う。土台を足したら、アイコンも同じ指示で追加する。
- v1.14.1（#8）：分解の返事待ちの遺物は `Rules.EndRun(p, victory, reservedUids)` で `Profile.PendingSalvage`（戻し先つき、保存される）に移し、成功で `Rules.SalvageUnsecured`、失敗・期限切れ・起動時に `Rules.RestorePendingSalvage` で戻す。
- v1.15：今日の夢・悪夢の契約の説明は `DailyDream.Description` / `Pacts.Describe` で数値から作る（説明文を手で書かない）。到達刻印の効果は `Content.FormatPower` で表示し、`TalentDef.Description` には向いている戦い方だけを書く。画面のクリックの重なりは DreamforgeUi.Draw でまとめて扱う（メニューを開いている間は下のパネルを描かない・ヒントの下はマウス位置をずらす）。
- 教訓（v1.15.1）：説明文の変更と実装の変更は、同じコミット（少なくとも同じ push）に入れる。途中の push が issue で指摘された（#10・#11）。数値は説明と効果で同じ定数を使う。

## 進め方（v1.16〜v1.22 で固まったこと）

- **利用者は助言だけをする**。方針の選択肢を並べて選ばせず、自分で判断して進め、判断と理由を報告する。
- **手を止めない**：GPT（OMP）や画像生成が走っている間も、次の版の仕様・データ・画面・実機確認を並行して進める。
- **実装の分担**：2026-10-02 から実装役は **GLM-5.3**（OMP の `--model zhipu-coding-plan/glm-5.3 --thinking max`）。それまでは GPT（OMP、`C:\Temp\sod-c` の複製で）と Claude の両方が実装する。重ならないよう、別の版・別の部分を受け持つ。Claude は `C:\Temp\sod-v120` のような別の複製で作り、`git cherry-pick` で取り込んだこともある。
- **大量のデータ**（固有品・セット）は、Sonnet のサブエージェントに下書きさせ、Claude が全件を読み、スクリプトで検査（ID・名前の重複、土台の枠、固有効果の組み合わせの重複、上限）してから組み込む。
- **アイコン**：`C:\Temp\sod-art` の Codex 画像生成（`run_batches.sh` 形式、5枚ずつ）。2本並行で約6分/5枚。`shrink.py` で128pxにして `src/SodRpg.Mod/icons/` へ。
- **本体の API 調査**：`C:\Temp\sod-reflect`（MetadataLoadContext のダンプ）。`dump/` に型ごとの一覧、`_events.txt` にイベント全件。
- **実機確認**：`.tmp/sodtest` の `prep.ps1`／`onboard_restore.ps1` は **pwsh（PowerShell 7）** で動かす（Windows PowerShell 5 は日本語の行で壊れる）。ようこその「了解」は (726,622)、ロビーの「開始」は (1642,1000)→(900,585)。
- **バランス**：変更のたびに `tools/BalanceSim` を回し、結果を `tools/BalanceSim/result-vX.md` に残す。プレイヤー側を強くしたら、モンスター側のつり合いも考える。
- **アイテム数はできるだけ増やす**（利用者の繰り返しの要望）。

## ビルドの置き場所（2026-10-02 利用者の指示）

利用者がゲームのフォルダの MOD で遊んでいる間は、**ゲームのフォルダ（`Mods/DreamforgeRPG`）に書き込まない**。ビルドは
`-p:ModDeployDir="C:\Temp\DreamforgeRPG-build\DreamforgeRPG"` を付けて別のフォルダへ出す（`GameDir` は参照のためだけに必要）。リリースの zip もそこから作る。実機での確認は、利用者の許可があるまでしない。
