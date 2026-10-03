# 協力プレイ（MP）レビュー：UI・保存・ビルド検証・通信版（第3領域）

対象：`C:/Temp/wt-review`（読み取りのみ。コードは未変更、ゲームも未起動）。Protocol.Version = 12、保存形式 version = 3。
表記：**確認済み**＝該当コードとゲーム側の逆コンパイル（`%LOCALAPPDATA%/sod-decomp/Dew.Core`）を読んで確かめた事実。**推測**＝コードから導いた帰結で、実機・試験での再現はしていない。
重大度は、全員が信頼できる友人同士（想定）と、悪意あるクライアントや版違いの参加者を含む場合の両方を考えて付けた。
注：依頼文にある `ScopedBuildCodec` というクラスはこのリポジトリに存在しない（grep で 0 件）。ビルドの符号化は `Build.Encode/Decode`（`src/SodRpg.Core/Game/Build.cs`）と `BuildTransfer`（分割送信）のみ。

## 影響順の一覧

| # | 重大度 | 要旨 |
|---|---|---|
| 1 | 高（悪意あり）／中（信頼あり） | ホストはクライアントの Build を「項目ごとの上限」でしか検証せず、仕掛け・能力値・能力が星図や装備と無関係に最大値で通る。同条件の仕掛け300個でホストを過負荷にできる |
| 2 | 高 | 保存形式の版が 3 のまま内容だけ増えるので、古いMODで読むと未知の星・遺物を黙って捨てて上書き保存する |
| 3 | 中 | 星ポイントが 301〜304 になりうるのに `Build.Compute` が 300 超で例外。メニューが開けず、ビルドも送れず、定期保存も止まる |
| 4 | 中 | 通信の版は整数1つだけ。不一致は利用者へ伝わらず、同じ版12でも内容が違えばビルドが丸ごと黙って捨てられる |
| 5 | 中 | `ClientSession.Tick` が1つの try で全処理を包むため、どれか1つの例外で ビルド送信・定期保存が毎フレーム止まる |
| 6 | 中（推測） | `PruneAndApplyPending` の `Apply` が無防備。途中で例外が出ると、全員分のホスト処理が毎フレーム中断する |
| 7 | 中 | 取引の応答待ち（ゴールド・ダスト→欠片）は期限がなく、応答が消えると「確保」「深く潜る」が永久に押せない |
| 8 | 中低 | ホスト単独のMOD読み直し後、クライアントのビルドが最大30秒届かない |
| 9 | 中低 | `ClientSession` 構築や `Awake` の途中失敗で、MOD全体（ホスト処理を含む）が黙って止まる |
| 10 | 低中 | `OnTrade` はダストの受け取りを検証できず、無制限に繰り返せる |
| 11 | 低中 | `OnBuild` に頻度制限がなく、1通でホストの再適用と全員への送信を起こせる |
| 12 | 低 | 保存の非同期書き込み失敗は再試行されない。同一PCの2プロセスは最後の書き込みが勝つ |
| 13 | 低 | 終了処理が保存より先に未払い報酬を捨てる。MOD再読み込みで完了済みランを再開始しうる |
| 14 | 低 | 細かい漏れ（呪いの記録・未精算の撃破の無制限な蓄積・ホストが送る値の無検証） |
| 15 | 情報 | 問題なしと確認した点（UIのホスト専用操作の保護、.instance の使用箇所ほか） |

---

## 1. Build 検証が「項目ごとの上限」だけで、ホストが整合性を見ていない（高／中）

ファイル：`src/SodRpg.Core/Game/Build.cs:395-462`（Decode）、`Gimmicks.cs:203-226`（Clamp）、`BuildLimits.cs:24-26`、`src/SodRpg.Mod/HostAuthority.cs:1107-1141`（OnBuild）、`HostAuthority.PairCombos.cs:91-111,138-155`。

確認済みの事実：
- `Decode` は 能力値（`s:`）を `±StatCap` へ、能力（`p:`）を `0..PowerCap` へ丸めるだけ。装備・星から得られる範囲かは見ない。全26能力値・全91能力を同時に上限値で申告しても通る。
- 仕掛け（`g:`）はクライアントが 星ID・記憶・発動条件・効果・値・クールダウン・持続・半径・対象数・確率 をすべて自己申告する（合わせ技 `c:` は ID と段数だけで、効果は正規定義から復元されるのと対照的）。ホストは `Gimmicks.Clamp` で効果ごとの上限（例：Burst は 1000%）に切るだけで、星IDが実在するか、その星の定義と一致するか、星ポイント内かを確認しない（`Content.TryGetTalent` を一度も呼んでいない）。
- 件数の上限も `BuildLimits.MaxGimmickEntries = MaxPoints(300) × MaximumGimmicksPerStar(1) = 300` で、旅人ごとの正確な上限（`BuildCapacity.GimmickEntries`、同ファイルの `Analyze` が既に計算している）は Decode で使われていない。
- 重複の排除は `StarId` だけ。同じ記憶・同じ条件・同じ効果の仕掛けを、星IDを変えて300個並べられる。`MinimumCooldown` は Ricochet(0.3)・Siphon(0.5) 以外 0 秒。
- 発動1回ごとに `QueueGimmickRequests` が `rt.PendingGimmicks.Add` する（`HostAuthority.PairCombos.cs:99`）。待ち行列の上限が無い。

失敗シナリオ（推測。実行はしていない）：改造したクライアントが、同一記憶・OnHit・Burst（値1000、Cooldown 0、半径+300%、対象+16）の仕掛けを星IDだけ変えて300個送る。1回の命中で300件の範囲ダメージ要求（`DamageAround`＝範囲検索＋ダメージ処理）がホスト上で実行され、ホスト全員のフレームが落ちる。ダメージ自体も攻撃力の10倍×300になる。能力値・能力を全部上限にした正規外のビルドも同様に通る。

修正案：
1. Decode（またはホストの `OnBuild`）で、各仕掛けを `Content.TryGetTalent(StarId)` の定義と照合する（HeroKey 一致、記憶・条件・効果・Arg 一致、値は `定義×段数＋星団修飾` の範囲、`Cooldown` は定義値以上）。登録に無い星IDは拒否。
2. 件数は `BuildLimits.Registered` の旅人別値（`GimmickEntries/LinkEntries/PairComboEntries`）を旅人キー付きで使う。`Build` に旅人キーを持たせるか、`OnBuild` で `HeroKeyOf(caller.hero)` から引く。
3. `AggregateLinks` 後の合計だけでなく、`SpentStarPoints` を超える星の合計コストになる申告は拒否する。
4. ホスト側の保険：`rt.PendingGimmicks` に上限（例：64）を付け、超過分は捨てる。同一フレームの仕掛け発動に総数上限を設ける。

---

## 2. 保存形式の版が据え置きのまま内容が増える：古いMODが黙って捨てて上書き保存（高）

ファイル：`Profile.cs:242,245`（`CurrentVersion=3`）、`ProfileCodec.cs:36`（新しすぎる版だけ拒否）、`ProfileCodec.cs:272-280`（未知の星を除外）、`ProfileCodec.cs:518-523`（未知の基礎ID・固有品IDの遺物を除外）、`ClientSession.cs:92-110`。

確認済みの事実：読み込みは「版が新しすぎる」場合だけ `LedgerVersionException`（読み取り専用で動く）にする。内容が増えただけ（v1.31 の星団の星ID・選択の星、計画中の v1.32 の新しい土台など）で版が同じ 3 なら、古いMODは未知の星を `notes` へ書いて捨て、未知の遺物は読み飛ばし、次の保存で上書きする。`.bak` は直前1世代だけで、数秒で置き換わる。
また v1.30.2 と作業中の v1.31 は両方とも「通信の版12・保存形式3」（CHANGELOG の記述どおり）。協力では全員が同じ通信の版を要求されるので、友人が更新していなければ利用者が古い版へ戻す動機がある。

失敗シナリオ（コードから確実に導けるが、実行はしていない）：v1.31 でクラスターの星を振った → 協力のため v1.30.2 へ戻して起動 → 未知の星が除外され、ポイントは払い戻し扱いになり、選択の星の選択も消える → そのまま遠征して保存 → 再度 v1.31 に戻しても星は戻らない。新しい土台の遺物（v1.32 計画）は遺物そのものが消える。

修正案：内容を足す版では必ず `Profile.CurrentVersion` を上げる（v1.31 で 4 など。`ResetBeforeVersion` は 3 のままにして 3→4 は無リセットで移行）。古いMODは `LedgerVersionException` で読み取り専用になる（既存の保護がそのまま働く）。加えて、読み込み時に「除外した星・遺物」があれば、元ファイルを `profile.json.pre-<日時>.json` へ一度だけ退避してから続行する。

---

## 3. 星ポイントが300を超えうるのに Build.Compute が 300 超で例外（中）

ファイル：`Build.cs:61-62`、`Profile.cs:338-345`（`TalentPoints`）、`Content.cs:286-287`（`CodexPerPoint=6`、`MaxCodexBonus=4`）、`StarProgression.cs:8`。UIの呼び出し：`DreamforgeUi.cs:1082`（装備タブ）、`DreamforgeUi.cs:296-302`（Draw の catch）。

確認済みの事実：`TalentPoints = Points(StarXp)（最大300） + 図鑑ボーナス（最大4） + 確認用ボーナス`。`AddTalentRank` は `FreePoints >= RankCost` で許可するので、ポイントが297以上で図鑑ボーナスが4あれば最大304点まで振れる。`ComputeTree` は `spent > StarProgression.MaxPoints`（300）で `InvalidOperationException` を投げる。読み込み時の過剰割り当ての払い戻し（`ProfileCodec.cs:414-425`）も `FreePoints < 0` を基準にするので、この状態は「正常」として残る。

失敗シナリオ（算術は確認済み。到達に必要な星の経験は約28万以上）：
- `CurrentBuild` を呼ぶたびに例外。装備タブ（メニューの既定タブ）が開けない。`Draw` の catch は `Open = false` にするので、メニューが開いた瞬間に閉じ、星図タブの振り直しにも辿り着きにくい。
- `SendBuildIfNeeded`（`ClientSession.cs:900-922`）も同じ例外で止まり、ホストへ Build が届かない（その旅人はMODの効果が全部無効）。例外は毎フレーム `Log.Error`。
- 項目5の通り、定期保存も止まる。

修正案：`Build.cs` の予算判定を「`spent > p.TalentPoints(heroKey)` またはデータ不整合のとき」へ変更するか、`MaxPoints + MaxCodexBonus` 以上でだけ例外にする。`DreamPressure/Decode` の `a:` は 300 に丸めているので問題ない（表示用）。あわせて `Draw` の catch は「毎フレーム同じ例外ならログを1回にする」「メニューを閉じない」へ。

---

## 4. 通信の版の不一致：利用者へ伝わらず、同じ版12でも内容違いは黙って全拒否（中）

ファイル：`NetMessages.cs:132-136`、`HostAuthority.cs:1107-1141`（`OnBuild`）、`ClientSession.cs:792-803`（`OnApplied`/`OnPressure`）、`DreamforgeUi.cs:342-346,394-395`、`ClientSession.RunChoices.cs:154`、`ClientSession.cs:382-407`（`TrackRun`）。

確認済みの動作：
- 旧クライアント（版9〜11）→ 新ホスト（12）：`OnBuild` は `msg.protocol != 12` を `Log.Warn` するだけで返信なし。旧クライアントは新ホストの `DreamforgePressureMsg`（protocol=12）を旧コードの版チェックで捨てるので `HostConfirmed` が立たない。
- 新クライアント（12）→ 旧ホスト（9〜11）：旧ホストが同じ理由で無視する。新クライアントは `OnPressure/OnRunChoices` を受け取れず、`TrackRun`（`!CanChooseRunRules && _receivedRunChoices == null` で return）が永久に遠征を開始しない。つまり**撃破報酬も依頼も一切入らない**。
- 利用者への通知：HUD は「ホストの確認待ち」と、「装備の効果がまだ反映されていません（ホストがこのMODを入れていない可能性があります）」だけ。後者は `run != null && ActiveRunId != null` のときしか出ず、遠征が始まらない版違いでは一度も出ない。版が違うという表示はどこにもない。ホスト側の画面にも出ない（ログのみ）。取引だけが `reason="protocol"` で返り、`TradeFailText` が版違いと説明する。
- 状態の破損は確認できなかった（クライアントの Profile はホスト由来の値では書き換わらない）。ただし副作用がある：ビルドを送れていない人は `RefreshPressure` で「夢1・星0」として平均に入るので、**版違いの参加者が1人いるだけで全員の敵が弱くなる**（確認済み：`HostAuthority.cs:295-312`、`DreamPressure.cs:44-54`）。
- 版が付いていない通知（Nightmare・Variant・MonsterCue・TradeResult）は版違いでも処理される。未知のIDは無視されるので害は小さいが、意味が変わった時に気づけない。

さらに：整数の版は「内容」を表さない。v1.30.2 とこれから出す v1.31 は同じ12だが、`Power`/`Stat`/星・仕掛けの定義と `BuildLimits.MaxEncodedChars` が違う。ホストの `Decode` は未知の列挙値・未知の合わせ技ID・上限超過を**ビルド全体の拒否**（null）にし、`BuildTransfer.Valid` も `TotalLength > 自分のMaxEncodedChars` で転送ごと拒否する。いずれも返信なしで、クライアントは `HostConfirmed=true`（圧のメッセージは届く）のまま、効果が1つも付かない。ホストに残るのは前回の古い Build（あれば）。

修正案：
1. `DreamforgeBuildMsg` に MOD版（`modVer`）と内容ハッシュ（星・合わせ技・列挙の件数など）を入れ、ホストは不一致なら `DreamforgeAppliedMsg` に `rejected=reason` を返す。クライアントはHUDに「ホストと版が違います（ホスト x.y.z / 自分 a.b.c）」を出す。ホストもトーストで「○○さんの版が違うため反映されません」を出す。
2. 版が違う参加者は圧の平均から外す（または未受信を平均に入れない）。
3. 内容（列挙・上限・星定義）を変える版では必ず `Protocol.Version` を上げる。
4. Decode の拒否理由を返す（どの区画でなぜ）。

---

## 5. ClientSession.Tick が単一の try：1つの例外でビルド送信と定期保存が止まる（中）

ファイル：`ClientSession.cs:199-222`（Tick）。`SendBuildIfNeeded` は `:213`、定期保存は `:214` で、どちらも同じ try の後ろにある。

確認済みの事実：`Wire → UpdateVariantVisuals → UpdateMonsterCues → TrackRun → TickRunChoices → …` が1つの try で、例外が出ると以降の `SendBuildIfNeeded` と `if (_dirty && …) SaveNow()` が毎フレーム飛ばされる。`SendBuildIfNeeded` 自身も `CurrentBuild(...).Encode()` が例外を投げうる（項目3）。`_buildDirty` が立ったままなので毎フレーム同じ失敗を繰り返し、ログも毎フレーム出る。

影響：明示的な `SaveNow()`（確保・撃破の節目）だけが保存の頼りになり、通常の30秒保存は止まる。クラッシュ時の喪失が広がる。

修正案：Tick を段ごとの try に分ける（少なくとも `SendBuildIfNeeded` と定期保存は独立させる）。同じ例外メッセージは10秒に1回だけログする。

---

## 6. PruneAndApplyPending の Apply が無防備（中・推測）

ファイル：`HostAuthority.cs:1275-1292`（`PruneAndApplyPending`）、`:1294-1404`（`Apply`、`_runtimes[hero] = rt` は `:1403`）。

確認済みの事実：`Apply` は新しい旅人で、先にイベント・プロセッサを大量に登録してから最後に `_runtimes[hero] = rt` を行う。`PruneAndApplyPending` は `foreach (_builds) … if (!_runtimes.ContainsKey(hero)) Apply(…)` で、try がない。`Tick`（`:250-290`）の先頭付近で呼ばれ、例外は `DreamforgeMod.Update` が捕まえて毎フレームログするだけ。

推測される失敗：`PowerRuntime`/`InitializeNewPowers`/`InitializeGimmicksV129` が、ある旅人のビルドで途中例外を出すと、`_runtimes` に入らないまま次のフレームで再度 `Apply` され、`hero.takenDamageProcessor` 等へ同じ登録が重複し、さらに以降の処理（`SyncWaypointHeroes`、仕掛け、悪夢、圧の同期）が全員分止まる。具体的に例外を起こす Decode 済みビルドは見つけていない（Decode 後の検証は堅い）が、内容違いや将来の追加で起こりうる。

修正案：`Apply` を旅人ごとの try/catch で包み、失敗した旅人は `Unhook` して `_builds` から外し（または失敗カウンタ付きで一定回数後に諦める）、他の旅人の処理を続ける。

---

## 7. 取引の応答待ちに期限がなく、確保・潜行が永久に押せなくなる（中）

ファイル：`Economy.cs:83-95`（`ExpireSalvage` は SalvageForDust だけ）、`ClientSession.RunChoices.cs:39`（`CanResolveSecureChoice` は `!HasPendingTrades`）、`DreamforgeMod.cs:134-141`（ホットキーも同条件）、`ClientSession.cs:292`（`Wire` の `_trades.Clear()` は接続が替わった時だけ）、`HostAuthority.cs:1157-1189`（`OnTrade`）。

確認済みの事実：`MerchantGold` と `DustToShards` の保留は、ホストの応答（`DreamforgeTradeResultMsg`）以外では解除されない。ホストは受信ハンドラ登録の隙（`EnsureRegistered` が新しい `serverActor` へ登録し直す前後、MOD読み直し中）で届いた取引メッセージを黙って捨て、返信しない。この間に出した取引の保留が残る。

失敗シナリオ：商人で買う／ダストを換える直後にホスト側のハンドラが外れていた → クライアントは「取引の応答を待っています。」のまま、確保地点の「確保する」「深く潜る」が無効（`GUI.enabled = CanResolveSecureChoice`）、ホットキーも無効になり、そのゾーンの選択が進められない。接続が切れ直すまで解除されない。

逆の事故：分解（SalvageForDust）は30秒で期限切れにして遺物を戻すが、ホストが遅れて成功を返した場合、ホストはダストを付与済みなのでクライアントは「遺物は残り、ダストも増える」二重取りになる（応答は `_trades.Complete` が null を返して捨てる）。

修正案：全種類の保留に60秒程度の期限を付け、期限切れは「結果不明。ゴールドとダストを確認してください」と表示して解除（付与はしない）。ホストの応答に `token` を使った結果を一定時間キャッシュし、期限後の遅延成功はクライアント側で「分解は完了済み」と処理するか、Salvage の期限切れ復元をやめる。

---

## 8. ホスト単独の MOD 読み直し後、クライアントの Build が最大30秒届かない（中低）

ファイル：`HostAuthority.cs:930-965`（登録替え時の `_builds.Clear()`）、`:1058`（Detach）、`ClientSession.cs:921`（`_nextBuildSend = now + (HostConfirmed ? 30f : 5f)`）、`ClientSession.cs:792-803`（`OnPressure` が `HostConfirmed=true`）。

確認済みの事実：ホストの `HostAuthority` を作り直す（MOD再読み込み、道標のホスト再読み込み対応＝CHANGELOG #21 と同じ状況）と `_builds` が空になる。クライアントは `serverActor` が同じなので `Wire` のリセットが起きず、`HostConfirmed` は true のまま（圧のメッセージは新しいホストからも届く）で、再送は30秒間隔。

影響（推測）：最大30秒、全員の旅人にMODの効果が無く、`RefreshPressure` は全員を夢1・星0として圧を下げる。

修正案：ホストの `RunChoicePublisher.AuthorityGeneration`（既にホスト起動ごとに一意）が変わった時、クライアントが `_buildDirty = true` にして即再送する（`RunChoiceSnapshotStream.TryAccept` の `changedAuthority` 分岐に1行足す）。または圧のメッセージに「あなたのビルドを受信済み」フラグを付ける。

---

## 9. 起動時の失敗でMOD全体が黙って止まる（中低）

ファイル：`ClientSession.cs:92-110`（`LedgerVersionException` だけ捕捉）、`ProfileCodec.cs:227`（`ulong.Parse(Str(b,"rng"))`）、`ProfileStore.cs:125`（`TryRead` が捕捉するのは Ledger形式・IO・Format・Overflow のみ）、`ProfileStore.cs:156-160`（コピー失敗は IOException のみ）、`DreamforgeMod.cs:27-52`（Awake）、`DreamforgeMod.cs:131-135`（HandleKeys）。

確認済みの事実：
- `Load()` が `UnauthorizedAccessException`・`ArgumentNullException`（`rng` 欠落）・`InvalidOperationException` など上記以外を投げると、`new ClientSession` が失敗し `Awake` の catch に落ちて `_session == null`。以後 `Update` は先頭で return し、`HostAuthority` も作られない（`_host` も null）。ホストならクライアントは「ホストの確認待ち」のまま。
- `Awake` の中で `_session` を作った後に `RelicIcons.Init/Preload` が例外を出すと、`_ui` と `_host` が null のまま残る。`Update` → `HandleKeys` の `_ui.Open`（`:135`）が毎フレーム NullReferenceException になり、後ろにある `_session.Tick()` と `_host.Tick()` に到達しない。

修正案：コンストラクタの `Load` を `catch (Exception)` にして、`LedgerVersionException` と同様に読み取り専用（`_store = null`）で動き続ける。`Awake` は各部品を独立した try にし、`_ui == null` でも `HandleKeys` を安全にする（`_ui?.Open`）。

---

## 10. OnTrade：ダストの受け取りを検証できず、繰り返しで無限に得られる（低中）

ファイル：`HostAuthority.cs:1157-1189`、`Economy.cs:19`（`MaxDustEarnPerTrade = 2000`）。

確認済みの事実：`earnDust` は 0〜2000 の範囲だけ確認し、対応する遺物の分解があったかは見ない（見る手段もない）。`token` の重複も見ない。同じ要求を何回でも送れば、そのたびに `EarnDreamDust(2000)`。ダストは `DustToShards` でクライアントの欠片に換えられる（換算は支払い側でホストの確認あり）。

修正案：1人あたりの総受け取りに上限（例：遠征ごと、または分解可能な遺物数から求めた上限）と、`token` の再利用拒否、頻度制限を置く。

---

## 11. OnBuild に頻度制限がなく、1通で再適用＋全員への送信（低中）

ファイル：`HostAuthority.cs:1107-1141`、特に `:1126`（同一ビルドの再送でも `RefreshPressure(true)`）と `:1138`。

確認済みの事実：完了した転送ごとに、内容が新しければ `Decode`＋`Encode`＋`Apply`（`SetBuild` で連携状態をリセット）＋`RefreshPressure(true)`（全員へ圧のメッセージと報告）＋送信元へ要約の全文（最大約25分割）を返す。同じ内容でも圧の再送と要約の再送が起きる。クライアントごとの間隔制限がない。

修正案：クライアントごとに「前回受理からの最短間隔（例：1秒）」と「同一内容は圧を再送しない」を入れる。

---

## 12. 保存：非同期失敗の再試行なし／同一PC2プロセス（低）

ファイル：`ClientSession.cs:931-955`（`SaveNow`）、`AsyncProfileWriter.cs:50-80`、`ProfileStore.cs:79-96`、`IFileSystem.cs`（`RealFileSystem`）。

- 確認済み：`SaveNow` は先頭で `_dirty = false` にし、`_nextSave` を30秒先へ送る。書き込みスレッドの失敗（IOException：一時ファイルのウイルス対策ロック等）は `LastError` に残るだけで、`_dirty` は戻らず再試行もない。次の変更か終了時（`OnApplicationQuit`/`OnDestroy` が再度 `SaveNow`）まで保存されない。`SaveError` の表示はあるが、保存が止まっている間にクラッシュすると最大その間の進行を失う。修正案：書き込み失敗時に `_dirty = true` へ戻す、または作業スレッドで数秒後に自動再試行。
- 推測（確認はコードから）：同一PCで2つのゲームを起動する（協力の動作確認でありうる）と、同じ `profile.json` を共有し、各プロセスの `Revision` が同値のまま別内容を書く。`.tmp` も共有（`FileShare.None` で一方は IOException になる程度で、他方の書き込み内容を読み戻しても `Revision` が同じなので検証を通る）。結果は後勝ちで、片方の進行が消える。ロックファイル（`profile.json.lock`）で2つ目を読み取り専用にするとよい。
- 確認済み（良い点）：本体は一時ファイル→読み戻し検証→`File.Replace` で、書き込み中のクラッシュでも本体は壊れない。リセット（`ResetIfOld`）は `.bak` が旧版でも本体の版を優先し、写しが作れなければ保存を止める。v1.30.x の版3のプロフィールは `ResetBeforeVersion=3` に引っかからず、リセットされない。

---

## 13. 終了処理と再読み込み（低）

ファイル：`DreamforgeMod.cs:263-283`（`OnDestroy`）、`ClientSession.cs:335-380`（`Unwire`）、`ClientSession.RunChoices.cs:15-17`（`_completedRunId`）。

- 確認済み：`OnDestroy` は `Unwire()`（`_pendingRunRewards.Clear()`、`_pendingPressureDividends.Clear()`）→`SaveNow()` の順。ホストの選択待ちで未精算の撃破（遅れて入社した人・ゾーン移動直後）は、保存前に捨てられる。順序を保存→`Unwire` にし、未精算の撃破は `Profile` へ持ち越すか、保存前に `FlushPendingRunRewards` を試みる。
- 推測：`_completedRunId` はメモリのみ。完了後（結果画面）に MOD を再読み込みすると、`TrackRun` が終わったゲームの `runId` で新しい遠征を作り（`Stats.Runs++`・依頼の再抽選）、次の `BeginRun` で敗北扱いに終える。`Profile` に最後に完了した `runId` を保存すれば防げる。
- クライアントが遠征中に落ちた場合（確認済みの設計）：`Profile.Run` は最大30秒前の状態で残り、同じ `runId` に戻れば続きから、別のホスト・別の `runId` なら `BeginRun` が前回を敗北として終える（未確保の遺物は遺失物になる）。ホストが先に落ちた場合もクライアントは同じ扱いになる。

---

## 14. 細かい漏れ（低）

- `HostAuthority._pactCurses`（`HostAuthority.cs:1187,1231`）は退出したプレイヤーの項目を消さない（`_onPressurePlayerRemoved` が `_builds`/`_incomingBuilds` だけ消す）。小さな漏れ。
- `PendingRunRewards.Add`（`PendingRunRewards.cs:32`）は上限がなく、ホストの選択が届かない版違い・ホスト無しの状態では `ClientSession.cs:488` で撃破ごとに溜まり続ける（1件は小さい）。`PendingPressureDividends` の `_nonces/_deaths` も同様に遠征中は無制限。
- クライアントはホストが送る値を信用する：`OnPressure`（`ClientSession.cs:796-803`）は倍率を無検証で保存（NaN/∞は `OnPressureReported` が弾く）、`OnBountyReport` の `value`、`OnPressureDividend` の報酬は、悪意あるホストが参加者の保存データへ欠片・経験・依頼報酬を足せる。保存データを削除・破壊はできないので影響は水増し。ホスト信頼が前提なら許容。
- `BuildLimits.MaxEncodedChars` はプロパティで、`Enum.GetValues` を3回＋`PairCombos` 走査＋条件付き能力の判定を毎回行う（`BuildLimits.cs:60-66`）。`BuildTransfer.Valid`（部品ごと、`MaxParts` で再度）と `Decode` が呼ぶので、細かいメッセージを大量に送られると無駄な割り当てが増える。キャッシュ（`Lazy`）にするとよい。
- `HostAuthority.PairCombos.cs:200`：`ActorManager.instance.serverActor`（`.instance` は null のとき `FindObjectOfType` を探索する。Heal 仕掛けで null 参照の余地）。`HostAuthority.cs:1635`：`ManagerBase<TransitionManager>.instance`（`OnHeroSelfMovement` が移動ごとに呼ぶ）。どちらも `softInstance` を使うのが無難。（`.instance` の探索フォールバックは `NetworkedManagerBase` のデコンパイルで確認。`ManagerBase` 側は推測。）
- `Wire()`（`ClientSession.cs:289`）の `DreamforgePressureDividendMsg` の登録解除だけ try で包まれていない。前の行は包まれている。`_clientRpcOn != null`（Unity の等価比較）で破棄済みは弾かれるため、通常は発火しないが、一貫性のため包むとよい。
- `Actor.CustomRpc_UnregisterServerMessageHandler<T>()`（引数なしの版）はゲーム側の不具合で `_clientRpcHandlers` を消す（デコンパイルで確認）。現在のMODは引数付きだけを使っていて問題ない。今後も引数なしを使わないこと。

---

## 15. 問題なしと確認した点

- UIのホスト専用操作：深さ選択・道標・確保・潜行は、ボタンの有効化（`GUI.enabled = CanChooseDepth/CanChooseRunRules/CanResolveSecureChoice`、`DreamforgeUi.cs:478,552,595,602,619`）と、`ClientSession` 側の再確認（`ChooseDreamDepth/ChooseWaypoint/Secure/Delve` が `CanChoose*`・`CanResolveSecureChoice` を見て文言を返す、`ClientSession.RunChoices.cs:42-62`、`ClientSession.cs:843-870`）の二重で保護されている。クライアントがホスト専用の状態を書き換える経路は見つからなかった。`Secure/Delve` のホットキーも同じ関数を通る。
- UI/CodexView/StarMapView は `.instance` を使わず、`softInstance` のみ。`CodexView.cs` はネットワーク・ホスト状態に触れない（読んだのは先頭約140行と grep のみ。残りは推測）。`DewPlayer.local`/`LocalHero` は null 判定されており、ロビー・死亡中・遠征未参加で null 参照になる箇所は見つからなかった（`HeroKey` はロビーの旅人へフォールバック）。
- Build の Decode の例外処理：`FormatException/OverflowException/ArgumentException/InvalidOperationException` を捕捉して null を返す。ASCII 以外・長さ超過・重複区画・重複ID・NaN/∞ のクールダウン・未定義の列挙・上限超過リンクを弾く。分割受信（`BuildTransferReceiver`）は1接続につき1転送、メモリ約300KB上限、重複・矛盾した部品は転送ごと破棄。他のプレイヤーの処理を壊す経路は、項目1・6以外では見つからなかった。
- 能力値・能力の全項目に上限がある（`Content.StatCaps` 26項目、`PowerCaps` 91項目を突き合わせて欠落なし）。
- 他のクライアントがホスト発の通知を偽装する経路は無い：`Actor.HandleRpc_Imp`（デコンパイルで確認）は、クライアント発のコマンドを `_serverRpcHandlers` だけへ、サーバー発を `_clientRpcHandlers` だけへ流す。ただし `CmdHandleRpc_Imp` は `requiresAuthority:false` なので、任意のクライアントが `serverActor` へ任意の型名のメッセージを送れる点は、項目1・10・11の前提になる。
- 保存：チェックサム、一時ファイル検証、`.bak` 復旧、リセット前の版を復旧候補にしない処理（#16/#17）は一貫している。

## 検証できていないこと

実機・2人以上の協力・Unity 上での再現はしていない。項目1の負荷、項目3の再現（星の経験28万以上が必要）、項目6の例外源、項目9の `RelicIcons` 失敗、項目12の2プロセス競合は、コードの読解からの推測を含む。
