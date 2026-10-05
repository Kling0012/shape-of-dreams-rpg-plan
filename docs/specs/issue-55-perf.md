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

## #73：残存する確保と長期遠征（2026-10-05）

- `HostAuthority.NativePowers.cs`：基本攻撃・ダメージ・属性付与の同期スコープを、
  ネスト深度ごとの再利用バッファに変更。参加者ではスナップショットを作らない。
  外側スコープは Harmony Finalizer で復元し、使い終えた Actor/Entity 参照を解放する。
- ダメージ前のシールド全走査は、ホストの `ShieldbreakBurst` を持つ旅人へのダメージだけに限定。
  `IReadOnlyList<BasicEffect>` は添字で走査し、インターフェース列挙子の確保を避ける。
- 再利用されるスコープの参照を発動・召喚獣の致死判定の識別子に使わず、
  ディスパッチごとに増えるシリアルを使用する。古いスコープと新しい攻撃を混同しない。
- `HostAuthority.NewPowers.cs`：通常記憶の走査は値型列挙子で行う。
  `HostAuthority.RunGrowth.cs`：持ち主の netId 文字列は HeroRuntime に保持し、netId が変わったときだけ再生成する。
- `DreamforgeUi.cs`：確保地点の動的説明とキー付きボタンの文字列は、
  数値・設定・言語が変わったときだけ再生成。契約一覧の `ToList()` を除去し、
  選択による一覧変更時は描画ループを終了する。
  名札用 Monster 参照は NetworkIdentity ごとにキャッシュし、不要・破棄済みエントリを掃除する。
  差分同期では、未スポーン・関心領域外という理由だけでタグを期限切れにせず、ホストの明示的な除去を待つ。
- 帰属・発動の装備スナップショットは `EntityAbility.SetAbility` / `RemoveAbility` で更新し、
  ダメージごとの装備辞書・リスト再構築を廃止する。パケット・要求・味方障壁の作業バッファは
  同期ネストごとに再利用し、刻印のペア識別子・遅延発動のクロージャを値メタデータへ変更する。
- 帰属台帳は256シリアルごとに、4096シリアル前までの終了済み履歴を掃除する。
  生存中の投射物・DoT、被害者の予算、遅延処理・発動中のネイティブ終端は保持する。
  退役済みのパケットは再入場できないため、通知や予算の二重消費を防ぐ。
  発動成功時の配当・有効ペイロード等の結果オブジェクトの確保は残り、戦闘全体が確保ゼロになる変更ではない。
- ネットワークは Protocol 16、保存形式は5へ更新。既存形式の読込を維持し、
  新形式を扱えない旧バイナリには読込を拒否させる。
  撃破チェックポイントは未対応の事実・死亡とストリーム別の連番受信境界・順序逆転の例外を保存し、
  新たな解決済み全履歴の複製を廃止する。ホスト再起動では新しいストリームを開始し、
  未保存の連番の再利用・native netId の再利用が古い受信境界に誤一致しないようにする。
  旧 GUID 履歴は分割再送時に連番へ移行し、報酬を再付与しない。
  旧形式には参加者の受信記録がないため、移行前のホスト履歴は固定の互換データとして遠征終了まで保持する。
  新しい撃破はその互換履歴へ追加しない。
- ACK は `AsyncProfileWriter.WrittenRevision` で確認した保存済み境界だけを送る。
  未受信事実がある間は ACK を先へ進めない。新しいストリームではホストと参加者の未確認事実のみ保存・再送し、
  切断した参加者については切断前の未確認区間だけを保持する。
  不在中の将来の撃破は、その参加者の再送台帳へ加えない。
- 復帰時は不在区間を受信境界から除外し、保存済みの未解決死亡はストリームと netId 指定の要求で回復する。
  撃破再送・未受信事実の回復・生存敵の途中参加同期は、合計32 RPC メッセージ/フレームまでに分割する。
  定期の分類・行動合図は差分だけを送り、死亡・除去は明示的に同期する。
  差分は値を合流した FIFO で保持し、除去・分類・行動合図をラウンドロビンで処理する。
  更新の多い敵が後続の敵を飢餓状態にしないよう、同じ netId の更新でも待ち順は変えない。
  実際に新しく発生した撃破の事実と最初のストリーム通知は即時送信し、
  native 死亡の通知より前に分類を送る順序を維持する（再送の32件枠とは別）。
- 接続直後の未知の参加者は最初のストリーム通知までの区間だけを保持し、
  互換性を確認した Hello/受信通知の後だけ将来の参加区間を延長する。
  MOD 非導入の味方が、将来の全撃破を保存に滞留させることはない。
  空の仮参加区間は ACK 不要で除去し、切断後の空の `connection.*` peer は保持しない。
  空または確認済み区間しか持たない仮 peer は保存・復元対象にも含めない。
  権威未確定の死亡は観測セッションIDを保存し、復旧候補が一意な場合だけ過去のストリームへ結び付ける。
  未確定・復旧候補なし・曖昧な死亡も実時間30秒で報酬なしとして完了し、後続の精算・進行・ACKを止め続けない。
  ストリームと観測セッションに紐づく期限切れ墓標を保存し、遅着事実による再付与を防ぐ。
  旧形式の `expiredMonsters` も移行読込する。未知の分類の捏造や推測による報酬付与はしない。
- 夢の圧の配当も `DeferKillSave()` にまとめ、同一フレームでの全文保存の連続を避ける。
  未解決・未保存の例外、および実際の所持品等の成長は保存に残るため、プロフィール全体の固定サイズは保証しない。

本体資料は Managed DLL と逆コンパイルソースのみで、ゲームの起動プログラムを含まない。
この環境では実戦の `dreamforge_perf` 前後比較、実画面、ホスト・参加者の再接続の実機確認はできない。
ビルド・既存テストの結果と、実機で未確認の受入条件は納品報告で区別する。

### #73 初回実装の検証結果（main 追従前）

- 指定の Release ビルド：成功、警告5件、エラー0件。
- 指定の `tools/test_changed.py --all`：終了コード1。テストプロジェクトのコンパイルエラー20件で停止し、
  テスト本体は未実行（runner の Passed 0 / Failed 0 は成功を意味しない）。
- テスト側のネイティブ API スタブは変更していない。リンクした本番ソースの新しいキャッシュ・値メタデータ・同期メンバーが
  スタブに存在しないため、次の CS0103 / CS1061 が発生した。下表のファイルはすべて `src/SodRpg.Mod/` 配下。

| ファイル | 行 | 不足メンバー |
| --- | --- | --- |
| `HostAuthority.PairCombos.cs` | 114 / 118 | `EnsureMemoryAttributionEquipment` / `PendingGimmick.Authored` |
| `HostAuthority.Hello.cs` | 55 / 60 / 71 | `BindKillObservationSession` / `_killReplayPlayers` / `ClientSession` |
| `HostAuthority.AuthoredMechanisms.cs` | 303 / 363 / 438 / 476 / 483 | `PendingGimmick.Authored` / `_attributionMemoryIds` / `PendingGimmick.Authored` / `PendingGimmick.Authored` / `EnsureMemoryAttributionEquipment` |
| `HostAuthority.NativeMemoryCasts.cs` | 34 | `EnsureMemoryAttributionEquipment` |
| `HostAuthority.AuthoredKeystones.cs` | 90 | `EnsureMemoryAttributionEquipment` |
| `HostAuthority.DirectedRecharge.cs` | 52 / 53 | `EnsureMemoryAttributionEquipment` / `_mechanismEquipment` |
| `HostAuthority.MemoryPrimedRelay.cs` | 88 | `EnsureMemoryAttributionEquipment` |
| `HostAuthority.StunSourceFilter.cs` | 59 / 86 / 101 | `EnsureMemoryAttributionEquipment` |
| `HostAuthority.GimmicksV129.cs` | 90 / 91 | `BasicAttackContext.Serial` |

### #73 レビュー修正・main 追従

- `origin/main` へ rebase。競合は5ファイル・16ブロック。
  #70 の30秒期限・報酬なし完了・旧墓標を新台帳へ統合し、#71 の heat/waypoint 撃破時スナップショット、
  #72 の保存再試行・例外時の未配当保持、#74 の TickGuard、#81 の Clone と明示的な `now` 引数を保持した。
  名札は #73 の差分同期に合わせ、時間経過ではなくホストの明示的な除去で消す。
- `LossOfIdentity` の直接 `Destroy()` が通る `EntityAbility.RemoveAbility(int)` をフックする。
  `SetAbility(int, AbilityTrigger)` も同じ実変更地点で更新し、既存の装備インスタンス比較で
  旧帰属を失効・epochを更新する。借用済みスナップショットは書き換えない。
  Q/W/E/R/Identity/Movement以外と非アクティブHeroは対象外。ダメージごとの走査へ戻さない。
- MOD 未導入・版不一致・切断の空の仮 peer を、共通の切断処理と保存フィルタで除去する。
  実際の未確認区間と、互換参加者の再接続に必要な受信境界は保持する。
  空の仮登録だけでは保存を要求しない。
- 保存済みACKは二重付与と再送履歴の抑制に必要なため維持する。
  期限前の追加処理は最短期限との比較だけとし、期限到達時の待ちキュー走査・既存のACK/保存/切断処理で収束させる。
  新しい毎フレームの装備・敵全走査、無制限の例外再試行、追加の同期方式は導入しない。
  実機の性能差はこの環境では未計測。
- 指定の Release ビルド：成功、警告5件、エラー0件。
  このレビュー修正ではテストを設計・追加・変更・実行していない。実ゲーム起動・再接続は未確認。
