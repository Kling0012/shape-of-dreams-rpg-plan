# Dreamforge RPG 引き継ぎ（2026-10-02 時点・v1.5.1）

次にこのMODを触る人（人間・AIエージェント）向けの引き継ぎ。最初にこの文書、次に [開発計画](dreamforge-roadmap.md)、[保留事項](dreamforge-pending.md)、[シナジー再評価](dreamforge-synergy-review.md)、[CHANGELOG](../CHANGELOG.md) を読む。

## 1. 現在の状態

| 項目 | 状態 |
| --- | --- |
| 版 | **v1.5.1**（GitHub Releases に v0.1.0〜v1.5.1、v0.7.0 以降は導入用 zip 付き） |
| ブランチ | `claude/dreamforge-playable-v0.1`（PR #4、main へは未マージ。CI 合格） |
| 試験 | `dotnet test` で **453件すべて合格**（既存の技術プロトタイプ250件＋ゲームルール203件。性能の試験を含む） |
| ビルド | MOD は警告0・エラー0。ゲームの `Mods/DreamforgeRPG` に配置済み |
| 実機確認 | v0.1〜v0.3 の主要部分のみ（読み込み・UI・能力反映・確保・精算・ライブリロード）。**v1.0 以降の本体連動は実機未確認**（保留事項を参照） |
| 次 | 利用者のテストプレイ結果を待って調整（`dreamforge_perf` の数値、手触り）。実機確認は利用者が行う（2026-10-02 指示） |

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
- 実機の確認用コマンド（開発者モードのコンソール `）：`dreamforge_status`・`_stats`・`_give 個数 レア度`・`_simkill 格 回数`・`_killnear 半径`・`_securepoint`・`_endrun 1|0`。MOD管理の「すべてリロード」でライブリロード。

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
