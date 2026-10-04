# 性能：プレイ中に重くなり固まって落ちる（#55、2026-10-05）

報告「長くやると重くなり、固まってクラッシュ」。コードから原因を特定し、根拠のはっきりしたものだけ修正した。
実機再現はできていない（検証はビルド＋既存テスト）。行番号は修正前。

## 原因と対策

### 1. 1キルごとの全文プロファイル保存（固まり・クラッシュの本命）
- `ClientSession.KillSync.cs:45-59`：ホストの撃破事実受領（`ReceiveAuthoritativeKill`）と参加者のnative撃破観測
  （`CaptureNativeKill`）が、それぞれ1キルごとに `SaveNow()` を呼ぶ。
- `SaveNow` は毎回 `PersistRunDurability()`（キル台帳 _facts/_resolvedEventIds の全複製、`KillClassificationLedger.cs:113-120`）
  ＋ `ProfileCodec.Write` の JSON 全文生成を **メインスレッド** で行う（`AsyncProfileWriter.cs:76-92`。ディスク書き出しだけ別スレッド）。
- 本体の `BossMonster.cs`（decompiled Dew.Core, OnDeath）はボス死亡時に残った雑魚を **同一フレームで一斉 Kill** する。
  雑魚M体が一斉に死ぬと、ホストでM回・各参加者でM回の全文保存が同じフレームに集中する。
- 台帳は遠征全体で削除されないため、1保存のコストは総キル数に比例し、**遠征が長いほど O(キル数²) で悪化**する。
  「長い遠征で重く、ボス撃破で固まる」報告と一致。
- **対策**：キル系の保存を `DeferKillSave()`（`_nextSave` を最長5秒後に手繰り寄せる予約）に置換。
  報酬の付与は従来どおり即時（メモリ）。MiniBoss 以上の撃破は `GrantPendingKill`（`ClientSession.RunChoices.cs:150-152`）が
  従来から `_nextSave=0` で次フレーム保存するので、重要キルの永続化は変わらない。クラッシュ時に直近5秒未満の
  雑魚キル分が失われ得る（二重付与ではなく欠落。全保存ごとのフリーズと引き換え）。

### 2. 全キル事実の5秒ごと全員へ再送（遠征が長いほど帯域が増える）
- `HostAuthority.KillSync.cs:53-61` `ReplayAuthoritativeRunKills`：5秒ごとの再同期で、**その遠征の全キル事実**を
  全参加者へ再送信。総キル数に比例して通信量・参加者のメッセージ処理が増え続ける。
- **対策**：今セッションで発行した事実は1周期ぶん（+余裕5秒）だけ再送し、全履歴は**初めて見る参加者**
  （途中参加・再接続・ホスト再登録後。`_killReplayPlayers` で管理）にだけ全件送る。
  Mirror の CustomRpc は信頼付き配信なので、接続中の参加者が通常送信を取りこぼすことはない。

### 3. 悪夢「群れ」の O(n²) 全モンスター走査（悪夢化敵の報告場面）
- `HostAuthority.Monsters.cs:66`（被ダメージごと）と `:224`（合図計算、0.1秒tickごと）が `FindBehaviorAlly`
  （全 `_monsters` 走査）を呼ぶ。Packbound の悪夢敵が n 体いると被弾のたび O(n)、tick ごと O(n²)。
- **対策**：`PackboundAllyNear`（`MonsterBehaviorRuntime` に `HasAlly`/`AllyCheckedAt` キャッシュ）を追加し、
  0.1秒の行動tickで更新。味方死亡の反映が最長0.15秒遅れる（実害は僅か）。Beacon の1回きり狙いは従来どおり実走査。

### 4. 毎フレームの List 確保と旅者数だけ重複する走査（ホスト常時のGC負荷）
- `HostAuthority.SacrificeShield.cs:185`：tick ごとに `new List<SacrificeBinding>`。
- `HostAuthority.ModShieldPools.cs:74`（死亡キーの List）、`:29`（`ModShieldEquipmentEpoch` の List）も同様。
- `HostAuthority.GimmicksV129.cs:344/354/376`：`UpdateGimmicksV129` が旅者1人ごとに3つの List を確保し、
  さらに `_sapProcessors` の期限切れ走査（旅者に依存しない O(雑魚×旅者)）を旅者数だけ繰り返していた。
- **対策**：スクラッチ List の再利用、空のとき早期リターン、樹液走査は `PruneSapProcessors` として tick で1回だけ実行。

### 5. 削除タイミングによる辞書の滞留
- `HostAuthority.cs:482-494` `RemoveMonster`（PruneMonsters 経路）は `_pressureDividendSpawns` などを消しておらず、
  OnEntityRemove が来ない破棄オブジェクト分が遠征中に蓄積する。
- **対策**：`RemoveMonster` で `_pressureDividendSpawns`/`_generatedPairDeaths`/`_pairEntities`/`_nativeDeathEntities`
  も冪等に掃除（イベント解除を伴う）。

## 確認したが直さなかったもの（参考）
- `ResyncMonsterClassifications` が5秒ごとに生存モンスター全員ぶんの variant/nightmare/cue メッセージを再送：
  量は生存数で打ち止め（遠征長で増えない）ため今回対象外。
- キル台帳（_facts 等）が遠征中に削除されない仕様：再接続時の全件再送の母体。#1–#2 の対策で保存・送信コストは
  打ち止めになったが、台帳自体のメモリ（キル数比例）と `Capture()` の複製コストは残る（1保存あたり1回のみ）。
- UI（OnGUI）は repaint/layout 分離・0.25秒キャッシュ・トースト上限7件など既に手当て済み（`DreamforgeUi.cs`）。
- #52（封じられた宝庫）・#53/#54（純白ルート）は別対応のため触れていない。クラッシュとの因果は未確認。

## 検証
- `dotnet build src/SodRpg.Mod -c Release` 成功（既存警告5件のみ）。
- `DOTNET_ROLL_FORWARD=Major DOTNET=/usr/bin/dotnet python tools/test_changed.py`：
  選択107クラス、Failed 0 / Passed 90（.NET 8 ランタイムが無い環境のため ROLL_FORWARD 必要）。
- 実機での長期遠征・ボス一斉撃破の再現確認は未実施。perf.flag / `dreamforge_perf` で Update/OnGUI/save avg を観測可。
